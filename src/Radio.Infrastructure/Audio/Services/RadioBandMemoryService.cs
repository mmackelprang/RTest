using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using RTLSDRCore.Bands;
using RTLSDRCore.Enums;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Per-band dial memory, backed by the configuration store.
///
/// <para>
/// The fallback ladder is three rungs and each one exists because the rung above it can be empty on
/// a fresh install: what was remembered, then the configured default for that band, then the bottom
/// edge of the band. There is deliberately no fourth rung that keeps the current frequency — that is
/// today's behaviour and it is the bug: <c>RadioReceiver.SetBand</c> clamps the outgoing band's
/// frequency into the incoming band's range, which lands FM → AM at 1710 kHz every time.
/// </para>
///
/// <para>
/// One store entry per band — <c>RadioBandMemory:LastFrequencyHzByBand:FM = "98500000"</c> — rather
/// than one serialized blob. That is .NET's own dictionary-binding key shape, so
/// <see cref="RadioBandMemory"/> would bind from it if anyone registers the section with
/// <c>Configure&lt;&gt;</c>, and it is the same flat key-value mechanism
/// <c>PreferencesPersistenceService</c> already writes through.
/// </para>
/// </summary>
public sealed class RadioBandMemoryService : IRadioBandMemory
{
  /// <summary>Key prefix every remembered band sits under, without its trailing separator.</summary>
  private const string BandKeyPrefix =
    $"{RadioBandMemory.SectionName}:{nameof(RadioBandMemory.LastFrequencyHzByBand)}";

  private readonly ILogger<RadioBandMemoryService> _logger;
  private readonly IConfigurationManager _configurationManager;
  private readonly IOptionsMonitor<RadioOptions> _radioOptions;

  public RadioBandMemoryService(
    ILogger<RadioBandMemoryService> logger,
    IConfigurationManager configurationManager,
    IOptionsMonitor<RadioOptions> radioOptions)
  {
    _logger = logger;
    _configurationManager = configurationManager;
    _radioOptions = radioOptions;
  }

  /// <inheritdoc />
  public async Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default)
  {
    var memory = await LoadAsync(cancellationToken);
    if (memory.TryGetValue(band.ToString(), out long hz) && hz > 0)
    {
      return new Frequency(hz);
    }

    return DefaultFor(band);
  }

  /// <inheritdoc />
  public async Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default)
  {
    if (frequency.Hertz <= 0)
    {
      return;
    }

    try
    {
      var store = await ResolveStoreAsync(cancellationToken);
      await store.SetEntriesAsync(
        [
          new ConfigurationEntry
          {
            Key = $"{BandKeyPrefix}:{band}",
            Value = frequency.Hertz.ToString(CultureInfo.InvariantCulture),
          }
        ],
        cancellationToken);
      await store.SaveAsync(cancellationToken);
      _logger.LogDebug("Remembered {Band} at {Hz} Hz", band, frequency.Hertz);
    }
    catch (Exception ex)
    {
      // Losing one reading costs the next band switch its restore and nothing else, so this is
      // logged rather than propagated: the caller is either the 30 s preferences loop or a band
      // commit, and neither should fail because a write did.
      _logger.LogWarning(ex, "Failed to remember {Band} at {Hz} Hz", band, frequency.Hertz);
    }
  }

  /// <summary>
  /// Reads the remembered frequencies as a band-name to hertz map. Returns an empty map when
  /// nothing has been stored yet or the store could not be read.
  /// </summary>
  private async Task<Dictionary<string, long>> LoadAsync(CancellationToken cancellationToken)
  {
    var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    try
    {
      var store = await ResolveStoreAsync(cancellationToken);
      var entries = await store.GetEntriesBySectionAsync(
        BandKeyPrefix, ConfigurationReadMode.Resolved, cancellationToken);

      foreach (var entry in entries)
      {
        // The store matches on the prefix plus a ':' separator, so the band name is whatever
        // follows the last separator in the key.
        int sep = entry.Key.LastIndexOf(':');
        if (sep < 0 || sep == entry.Key.Length - 1)
        {
          continue;
        }

        string bandName = entry.Key[(sep + 1)..];
        if (long.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long hz))
        {
          map[bandName] = hz;
        }
        else
        {
          _logger.LogWarning(
            "Ignoring band memory entry {Key}: {Value} is not a frequency in hertz", entry.Key, entry.Value);
        }
      }
    }
    catch (Exception ex)
    {
      // An unreadable store means "nothing remembered", which the caller already handles by
      // falling back to the band default. It does not mean the band change should fail.
      _logger.LogWarning(ex, "Failed to read the band memory section; falling back to defaults");
    }

    return map;
  }

  /// <summary>
  /// Resolves the main configuration store, the same way
  /// <c>PreferencesPersistenceService.SavePreferenceSectionAsync</c> does.
  ///
  /// <para>
  /// Duplicated rather than shared because that method's copy is a private step inside a
  /// <c>BackgroundService</c>'s save path, and extracting it would change a persistence path this
  /// row has no reason to touch. Both copies are three lines and they resolve the same two store
  /// ids, so the cost of the duplication is a grep, not a divergence.
  /// </para>
  /// </summary>
  private async Task<IConfigurationStore> ResolveStoreAsync(CancellationToken cancellationToken)
  {
    var mainStoreId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

    try
    {
      return await _configurationManager.GetStoreAsync(mainStoreId, cancellationToken);
    }
    catch
    {
      return await _configurationManager.CreateStoreAsync(mainStoreId, cancellationToken);
    }
  }

  /// <summary>
  /// The default landing frequency for a band with nothing remembered.
  ///
  /// <para>
  /// FM and AM have configured defaults; the other four do not, so they land on the bottom edge of
  /// the band from <c>BandPresets</c>. The bottom edge is a real, tunable frequency and it is where
  /// a mechanical dial would sit at rest — it is not a placeholder.
  /// </para>
  /// </summary>
  private Frequency? DefaultFor(RadioBand band)
  {
    var opts = _radioOptions.CurrentValue;
    return band switch
    {
      RadioBand.FM => Frequency.FromMegahertz(opts.DefaultFMFrequencyMHz),
      RadioBand.AM => Frequency.FromKilohertz(opts.DefaultAMFrequencyKHz),
      _ => BottomEdge(band),
    };
  }

  private Frequency? BottomEdge(RadioBand band)
  {
    // RadioBand and BandType are two different enums with two different vocabularies (SW vs
    // Shortwave, WB vs Weather), so the mapping is written out rather than parsed by name.
    BandType? mapped = band switch
    {
      RadioBand.SW => BandType.Shortwave,
      RadioBand.WB => BandType.Weather,
      RadioBand.VHF => BandType.VHF,
      RadioBand.AIR => BandType.Aircraft,
      _ => null,
    };

    if (mapped is null)
    {
      return null;
    }

    try
    {
      return new Frequency(BandPresets.GetBand(mapped.Value).MinFrequencyHz);
    }
    catch (ArgumentException ex)
    {
      // BandPresets.GetBand throws rather than returning null for a type it does not know. Every
      // type mapped above is one it defines today, so this catches BandPresets losing one rather
      // than a path taken now.
      _logger.LogWarning(ex, "No band preset for {Band}; no default frequency available", band);
      return null;
    }
  }
}
