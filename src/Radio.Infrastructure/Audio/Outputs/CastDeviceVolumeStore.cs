using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Core.Extensions;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Remembers the last volume the console had for each Cast device, keyed by
/// <see cref="ChromecastDeviceInfo.Id"/>, so a reconnect can put the speaker back where it
/// was (AUD-80).
/// </summary>
public interface ICastDeviceVolumeStore
{
  /// <summary>
  /// The remembered volume (0.0-1.0) for <paramref name="deviceId"/>, or null when this device
  /// has never been seen or the store cannot be read.
  /// </summary>
  Task<float?> GetVolumeAsync(string deviceId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Records <paramref name="volume"/> for <paramref name="deviceId"/>. Visible to
  /// <see cref="GetVolumeAsync"/> immediately; written to durable storage in the background.
  /// </summary>
  void Remember(string deviceId, float volume);
}

/// <summary>
/// <see cref="ICastDeviceVolumeStore"/> backed by the configuration store, as ONE JSON-object
/// entry under <see cref="Key"/> rather than one entry per device.
/// </summary>
/// <remarks>
/// <para>One entry because a device id is a URI (<c>https://192.168.86.25/</c>) and contains
/// <c>:</c>, the configuration section separator. Embedded in a key it would be split into
/// nested sections by the IConfiguration bridge.</para>
/// <para>The key is written with the same casing <see cref="Services.AudioPreferencePersistence"/>
/// uses for <c>AudioPreferences:MasterVolume</c>. The SQLite store's key column is case-sensitive,
/// which is how the box came to hold <c>audiopreferences:masterVolume</c> beside it.</para>
/// <para>Writes are ordered by construction rather than by scheduling: <see cref="Remember"/>
/// updates the in-memory map synchronously, and every background persist writes a snapshot of
/// the WHOLE map taken under the write gate. However the fire-and-forget persists interleave,
/// the last one to take the gate writes a snapshot at least as new as every earlier one, so a
/// burst of speaker-button steps cannot leave an older level on disk.</para>
/// </remarks>
public sealed class ConfigStoreCastDeviceVolumeStore : ICastDeviceVolumeStore
{
  /// <summary>The configuration key holding the per-device map.</summary>
  public const string Key = "AudioPreferences:CastDeviceVolumes";

  private readonly ILogger<ConfigStoreCastDeviceVolumeStore> _logger;
  private readonly IConfigurationManager? _configurationManager;
  private readonly Dictionary<string, float> _volumes = new(StringComparer.Ordinal);
  private readonly object _mapLock = new();
  private readonly SemaphoreSlim _gate = new(1, 1);
  private bool _loaded;

  /// <summary>Creates the store. With no configuration manager it remembers in memory only.</summary>
  public ConfigStoreCastDeviceVolumeStore(
    ILogger<ConfigStoreCastDeviceVolumeStore> logger,
    IConfigurationManager? configurationManager = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _configurationManager = configurationManager;
  }

  /// <inheritdoc />
  public async Task<float?> GetVolumeAsync(string deviceId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrEmpty(deviceId))
    {
      return null;
    }

    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _gate.Release();
    }

    lock (_mapLock)
    {
      return _volumes.TryGetValue(deviceId, out var volume) ? volume : null;
    }
  }

  /// <inheritdoc />
  public void Remember(string deviceId, float volume)
  {
    if (string.IsNullOrEmpty(deviceId) || float.IsNaN(volume))
    {
      return;
    }

    var clamped = Math.Clamp(volume, 0f, 1f);
    lock (_mapLock)
    {
      if (_volumes.TryGetValue(deviceId, out var existing) && Math.Abs(existing - clamped) < 0.005f)
      {
        return;
      }

      _volumes[deviceId] = clamped;
    }

    PersistAsync().SafeFireAndForget(_logger, "PersistCastDeviceVolumes");
  }

  /// <summary>
  /// Writes the whole map. Loads first, so a Remember that ran before the first load does not
  /// overwrite entries for other devices that are only on disk.
  /// </summary>
  private async Task PersistAsync()
  {
    if (_configurationManager == null)
    {
      return;
    }

    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
      await EnsureLoadedAsync(CancellationToken.None).ConfigureAwait(false);

      string json;
      lock (_mapLock)
      {
        json = JsonSerializer.Serialize(_volumes);
      }

      await _configurationManager.SetValueAsync(StoreId(), Key, json).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist remembered Cast device volumes");
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Merges the persisted map into memory once. Entries already in memory win: they were
  /// remembered in this process and are newer than anything on disk. Caller holds the gate.
  /// A failed read is logged and retried on the next call rather than cached as empty.
  /// </summary>
  private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
  {
    if (_loaded)
    {
      return;
    }

    if (_configurationManager == null)
    {
      _loaded = true;
      return;
    }

    try
    {
      var raw = await _configurationManager.GetValueAsync<string>(
        StoreId(), Key, ConfigurationReadMode.Raw, cancellationToken).ConfigureAwait(false);

      if (!string.IsNullOrWhiteSpace(raw))
      {
        var persisted = JsonSerializer.Deserialize<Dictionary<string, float>>(raw);
        if (persisted != null)
        {
          lock (_mapLock)
          {
            foreach (var (id, volume) in persisted)
            {
              _volumes.TryAdd(id, Math.Clamp(volume, 0f, 1f));
            }
          }
        }
      }

      _loaded = true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Could not read remembered Cast device volumes from the config store");
    }
  }

  private string StoreId() =>
    _configurationManager!.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
}
