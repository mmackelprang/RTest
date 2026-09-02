using System.Globalization;
using Microsoft.Extensions.Logging;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The configuration the app intends the device to run: the designed table from encoder handoff §5.2,
/// with the owner's per-knob direction overrides applied.
///
/// <para>
/// <b>One object, three consumers</b> — the bytes pushed, the bytes flashed, and the rows the Settings
/// page renders all come from <see cref="ResolveAsync"/>. That is what makes "Save writes what the
/// screen shows" a structural property rather than a promise (ENC-8 plan §0.5).
/// </para>
///
/// <para>
/// ⚠ The overrides are applied <b>here</b> and not inside <see cref="RotaryEncoderConfigDefaults.Create"/>,
/// because that method is the designed table and is asserted as such by
/// <c>RotaryEncoderConfigVerifierTests.Defaults_NeverWrapAndNeverReverse</c>. A wiring fact about one
/// cabinet is not a change to the design.
/// </para>
/// </summary>
public sealed class RotaryEncoderDesignedConfig
{
  /// <summary>Config-store key prefix for the per-knob direction overrides.</summary>
  internal const string ReverseKeyPrefix = "RotaryEncoder:Reverse:";

  /// <summary>Config-store key holding the UTC timestamp of the last successful flash write.</summary>
  internal const string LastSavedUtcKey = "RotaryEncoder:LastSavedToDeviceUtc";

  /// <summary>Config-store key holding the SHA-256 of the bytes last flashed. See ENC-8 plan §2.4.</summary>
  internal const string LastSavedHashKey = "RotaryEncoder:LastSavedConfigHash";

  private readonly ILogger<RotaryEncoderDesignedConfig> _logger;
  private readonly IConfigurationManager? _configurationManager;
  private readonly bool[] _reverse = new bool[RotaryEncoderDeviceConfig.EncoderCount];
  private bool _loaded;

  public RotaryEncoderDesignedConfig(
    ILogger<RotaryEncoderDesignedConfig> logger,
    IConfigurationManager? configurationManager = null)
  {
    _logger = logger;
    _configurationManager = configurationManager;
  }

  private string StoreId =>
    _configurationManager!.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

  /// <summary>
  /// The configuration to push, flash and display.
  ///
  /// <para>
  /// Overrides are read from the store on first use rather than from <c>IOptionsMonitor</c>, which
  /// only ever reflects <c>appsettings.json</c> and would silently discard a value the owner set at
  /// runtime — the same trap <c>PreferencesPersistenceService</c> documents against its own
  /// preference sections.
  /// </para>
  /// </summary>
  public async Task<RotaryEncoderDeviceConfig> ResolveAsync(CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);

    RotaryEncoderDeviceConfig config = RotaryEncoderConfigDefaults.Create();
    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      config.Encoders[i].Reverse = _reverse[i];
    }

    return config;
  }

  /// <summary>The current direction override for one knob, without re-reading the store.</summary>
  public bool IsReversed(int encoderIndex) => _reverse[encoderIndex];

  /// <summary>Persists a direction override. The caller is responsible for pushing it to the device.</summary>
  public async Task SetReverseAsync(int encoderIndex, bool reverse, CancellationToken ct = default)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(encoderIndex);
    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(encoderIndex, RotaryEncoderDeviceConfig.EncoderCount);

    await EnsureLoadedAsync(ct);
    _reverse[encoderIndex] = reverse;

    if (_configurationManager is null)
    {
      // No store configured: the override applies to this process and is not persisted. Said plainly
      // rather than logged as a success, because the owner will expect it to survive a restart.
      _logger.LogWarning(
        "No configuration store; encoder {Index} direction override is in-memory only and will not survive a restart",
        encoderIndex);
      return;
    }

    await _configurationManager.SetValueAsync(
      StoreId, $"{ReverseKeyPrefix}{encoderIndex}", reverse.ToString(), ct);
  }

  /// <summary>The SHA-256 of the bytes this app last wrote to the device's flash, or null if it never has.</summary>
  public async Task<string?> GetLastSavedHashAsync(CancellationToken ct = default)
  {
    ConfigurationEntry? entry = await TryGetEntryAsync(LastSavedHashKey, ct);
    return string.IsNullOrWhiteSpace(entry?.Value) ? null : entry.Value;
  }

  /// <summary>When this app last wrote the device's flash, or null if it never has.</summary>
  public async Task<DateTimeOffset?> GetLastSavedUtcAsync(CancellationToken ct = default)
  {
    // Round-trip format, invariant culture. This store is shared with the linux-arm64 Pi target and
    // read on a box whose locale is not guaranteed; a culture-sensitive parse here would read back a
    // different instant, and the value it feeds is a claim printed on the status card.
    ConfigurationEntry? entry = await TryGetEntryAsync(LastSavedUtcKey, ct);
    if (entry is not null &&
        DateTimeOffset.TryParse(entry.Value, CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
    {
      return parsed;
    }

    return null;
  }

  /// <summary>
  /// Records a successful flash write: when it happened, and a hash of exactly the bytes written.
  /// </summary>
  /// <remarks>
  /// Both values are stored because the status card claims more than "saved at T" — it claims the
  /// flashed bytes do or do not match the bytes the app would push now, and only the hash can settle
  /// that (ENC-8 plan §2.4).
  /// </remarks>
  public async Task RecordFlashWriteAsync(DateTimeOffset saved, string configHash, CancellationToken ct = default)
  {
    if (_configurationManager is null)
    {
      // Without a store the write happened but nothing about it survives this process, so the card
      // will read "never saved" after a restart. That is the honest reading of what is retained.
      _logger.LogWarning(
        "No configuration store; the encoder flash write was not recorded and will read as never saved after a restart");
      return;
    }

    await _configurationManager.SetValueAsync(
      StoreId, LastSavedUtcKey, saved.ToString("O", CultureInfo.InvariantCulture), ct);
    await _configurationManager.SetValueAsync(StoreId, LastSavedHashKey, configHash, ct);
  }

  private async Task<ConfigurationEntry?> TryGetEntryAsync(string key, CancellationToken ct)
  {
    if (_configurationManager is null)
    {
      return null;
    }

    try
    {
      IConfigurationStore store = await _configurationManager.GetStoreAsync(StoreId, ct);
      return await store.GetEntryAsync(key, ct: ct);
    }
    catch (Exception ex)
    {
      // Logged rather than thrown: the caller is rendering a status card, and a store that cannot be
      // read must not turn into a silent "never saved".
      _logger.LogWarning(ex, "Could not read encoder configuration key {Key}", key);
      return null;
    }
  }

  private async Task EnsureLoadedAsync(CancellationToken ct)
  {
    if (_loaded || _configurationManager is null)
    {
      _loaded = true;
      return;
    }

    try
    {
      IConfigurationStore store = await _configurationManager.GetStoreAsync(StoreId, ct);
      for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
      {
        ConfigurationEntry? entry = await store.GetEntryAsync($"{ReverseKeyPrefix}{i}", ct: ct);
        _reverse[i] = entry is not null && bool.TryParse(entry.Value, out bool v) && v;
      }
    }
    catch (Exception ex)
    {
      // Defaults are the safe answer: every knob turns the designed way. Logged as a warning because
      // a knob that is wired backwards will now feel backwards until the store is readable again.
      _logger.LogWarning(ex, "Could not read encoder direction overrides; using the designed directions");
    }

    _loaded = true;
  }
}
