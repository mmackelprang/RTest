using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Extensions;
using Radio.Core.Interfaces.Audio;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Handles debounced persistence of volume/mute/balance and source preferences.
/// Uses a 500ms debounce timer to avoid SQLite churn during slider drags.
/// </summary>
public class AudioPreferencePersistence : IDisposable
{
  private readonly ILogger<AudioPreferencePersistence> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IConfigurationManager? _configurationManager;

  private Timer? _volumePersistTimer;
  private Timer? _sourceGainPersistTimer;
  private readonly object _volumePersistLock = new();
  private readonly object _sourceGainPersistLock = new();
  private readonly Dictionary<string, float> _sourceGainOffsets = new();
  private bool _disposed;

  public AudioPreferencePersistence(
    ILogger<AudioPreferencePersistence> logger,
    IAudioEngine audioEngine,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IConfigurationManager? configurationManager = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioPreferences = audioPreferences;
    _configurationManager = configurationManager;
  }

  /// <summary>
  /// Schedules a debounced persistence of volume/mute/balance preferences.
  /// Waits 500ms after the last change before writing, to avoid SQLite churn during slider drags.
  /// </summary>
  public void ScheduleVolumePersist()
  {
    if (_configurationManager == null) return;

    lock (_volumePersistLock)
    {
      _volumePersistTimer?.Dispose();
      _volumePersistTimer = new Timer(
        _ => PersistVolumePreferencesAsync().SafeFireAndForget(_logger, "PersistVolumePreferences"),
        null,
        TimeSpan.FromMilliseconds(500),
        Timeout.InfiniteTimeSpan);
    }
  }

  /// <summary>
  /// Restores volume, mute, and balance from persisted preferences.
  /// Reads from the config store directly (SQLite) since IOptionsMonitor only reflects appsettings.json defaults.
  /// Sets the mixer directly to avoid triggering re-persistence.
  /// </summary>
  public async Task RestoreVolumePreferencesAsync()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();

      // Try to read from config store (SQLite) first — this has the actual persisted runtime values
      int volumePercent = _audioPreferences.CurrentValue.MasterVolume;
      bool isMuted = _audioPreferences.CurrentValue.IsMuted;

      if (_configurationManager != null)
      {
        try
        {
          var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
          _logger.LogDebug("Reading volume from config store '{StoreId}'", storeId);
          var store = await _configurationManager.GetStoreAsync(storeId);

          var volEntry = await store.GetEntryAsync("AudioPreferences:MasterVolume");
          _logger.LogDebug("Config store volume entry: {Entry}", volEntry?.Value ?? "null");
          if (volEntry != null && int.TryParse(volEntry.Value, out var storedVol))
            volumePercent = storedVol;

          var muteEntry = await store.GetEntryAsync("AudioPreferences:IsMuted");
          _logger.LogDebug("Config store mute entry: {Entry}", muteEntry?.Value ?? "null");
          if (muteEntry != null && bool.TryParse(muteEntry.Value, out var storedMuted))
            isMuted = storedMuted;
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Could not read volume from config store, using defaults");
        }
      }
      else
      {
        _logger.LogWarning("No configuration manager available, cannot restore persisted volume");
      }

      mixer.MasterVolume = volumePercent / 100f;
      mixer.IsMuted = isMuted;
      mixer.Balance = 0f; // Always centered — balance control removed from UI

      _logger.LogInformation(
        "Restored volume preferences: Volume={Volume}%, Muted={Muted}",
        volumePercent, isMuted);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to restore volume preferences");
    }
  }

  /// <summary>
  /// Persists the current source selection to preferences.
  /// </summary>
  public async Task PersistSourcePreferenceAsync(AudioSourceType sourceType, CancellationToken cancellationToken = default)
  {
    if (_configurationManager == null)
    {
      _logger.LogDebug("ConfigurationManager not available, skipping preference persistence");
      return;
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      await _configurationManager.SetValueAsync(
        storeId,
        "AudioPreferences:CurrentSource",
        sourceType.ToString(),
        cancellationToken);

      _logger.LogDebug("Persisted source preference: {SourceType}", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist source preference for {SourceType}", sourceType);
    }
  }

  /// <summary>
  /// Persists current volume, mute, and balance to the configuration store.
  /// </summary>
  private async Task PersistVolumePreferencesAsync()
  {
    if (_configurationManager == null) return;

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var mixer = _audioEngine.GetMasterMixer();
      var volume = (int)Math.Round(mixer.MasterVolume * 100);
      var balance = (int)Math.Round(mixer.Balance * 100);

      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:MasterVolume", volume.ToString());
      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:IsMuted", mixer.IsMuted.ToString());
      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:Balance", balance.ToString());

      _logger.LogDebug("Persisted volume preferences: Volume={Volume}%, Muted={Muted}, Balance={Balance}%",
        volume, mixer.IsMuted, balance);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist volume preferences");
    }
  }

  /// <summary>
  /// Gets the gain offset for a source type (linear multiplier, default 1.0).
  /// </summary>
  public float GetSourceGain(AudioSourceType sourceType)
  {
    lock (_sourceGainPersistLock)
    {
      return _sourceGainOffsets.TryGetValue(sourceType.ToString(), out var gain) ? gain : 1.0f;
    }
  }

  /// <summary>Minimum gain multiplier (silence).</summary>
  public const float MinGain = 0.0f;

  /// <summary>
  /// Maximum gain multiplier. Per-source trim for user adjustment.
  /// Capped at 5.0 (+14 dB) for sources that need significant boost.
  /// The LimiterModifier prevents clipping at the output stage.
  /// </summary>
  public const float MaxGain = 5.0f;

  /// <summary>
  /// Sets the gain offset for a source type and schedules persistence.
  /// </summary>
  public void SetSourceGain(AudioSourceType sourceType, float gain)
  {
    gain = Math.Clamp(gain, MinGain, MaxGain);
    lock (_sourceGainPersistLock)
    {
      _sourceGainOffsets[sourceType.ToString()] = gain;
    }
    _logger.LogInformation("Source gain set: {SourceType} = {Gain:F2}", sourceType, gain);
    ScheduleSourceGainPersist();
  }

  /// <summary>
  /// Gets all per-source gain offsets.
  /// </summary>
  public Dictionary<string, float> GetAllSourceGains()
  {
    lock (_sourceGainPersistLock)
    {
      return new Dictionary<string, float>(_sourceGainOffsets);
    }
  }

  /// <summary>
  /// Restores per-source gain offsets from the configuration store.
  /// </summary>
  public async Task RestoreSourceGainOffsetsAsync()
  {
    if (_configurationManager == null)
    {
      _logger.LogWarning("No configuration manager available, cannot restore source gain offsets");
      return;
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var store = await _configurationManager.GetStoreAsync(storeId);

      // Read all AudioPreferences:SourceGain:* keys concurrently
      var sourceTypes = Enum.GetValues<AudioSourceType>();
      var tasks = sourceTypes.Select(async sourceType =>
      {
        var key = $"AudioPreferences:SourceGain:{sourceType}";
        var entry = await store.GetEntryAsync(key);
        return (sourceType, entry);
      });

      var results = await Task.WhenAll(tasks);

      var restored = 0;
      lock (_sourceGainPersistLock)
      {
        foreach (var (sourceType, entry) in results)
        {
          if (entry != null && float.TryParse(entry.Value, CultureInfo.InvariantCulture, out var gain))
          {
            // Clamp handles migration from old MaxGain=25 values
            _sourceGainOffsets[sourceType.ToString()] = Math.Clamp(gain, MinGain, MaxGain);
            restored++;
          }
        }
      }

      if (restored > 0)
      {
        _logger.LogInformation("Restored {Count} source gain offsets from config store", restored);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to restore source gain offsets");
    }
  }

  /// <summary>
  /// Schedules a debounced persistence of source gain offsets.
  /// </summary>
  private void ScheduleSourceGainPersist()
  {
    if (_configurationManager == null) return;

    lock (_sourceGainPersistLock)
    {
      _sourceGainPersistTimer?.Dispose();
      _sourceGainPersistTimer = new Timer(
        _ => PersistSourceGainAsync().SafeFireAndForget(_logger, "PersistSourceGain"),
        null,
        TimeSpan.FromMilliseconds(500),
        Timeout.InfiniteTimeSpan);
    }
  }

  /// <summary>
  /// Persists all source gain offsets to the configuration store.
  /// </summary>
  private async Task PersistSourceGainAsync()
  {
    if (_configurationManager == null) return;

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      Dictionary<string, float> snapshot;
      lock (_sourceGainPersistLock)
      {
        snapshot = new Dictionary<string, float>(_sourceGainOffsets);
      }

      foreach (var (sourceType, gain) in snapshot)
      {
        await _configurationManager.SetValueAsync(
          storeId,
          $"AudioPreferences:SourceGain:{sourceType}",
          gain.ToString("F4", CultureInfo.InvariantCulture));
      }

      _logger.LogDebug("Persisted {Count} source gain offsets", snapshot.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist source gain offsets");
    }
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    lock (_volumePersistLock)
    {
      _volumePersistTimer?.Dispose();
      _volumePersistTimer = null;
    }

    lock (_sourceGainPersistLock)
    {
      _sourceGainPersistTimer?.Dispose();
      _sourceGainPersistTimer = null;
    }
  }
}
