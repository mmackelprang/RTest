using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

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
  private Timer? _sourceRmsPersistTimer;
  private readonly object _volumePersistLock = new();
  private readonly object _sourceGainPersistLock = new();
  private readonly object _sourceRmsLock = new();
  private readonly Dictionary<string, float> _sourceGainOffsets = new();
  private readonly Dictionary<string, float> _sourceLearnedRms = new();
  private readonly Dictionary<string, string> _sourceGainMode = new();
  private readonly Dictionary<string, int> _sourceSampleCount = new();
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
        _ => _ = PersistVolumePreferencesAsync(),
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
  public void RestoreVolumePreferences()
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
          var store = _configurationManager.GetStoreAsync(storeId).GetAwaiter().GetResult();

          var volEntry = store.GetEntryAsync("AudioPreferences:MasterVolume").GetAwaiter().GetResult();
          _logger.LogDebug("Config store volume entry: {Entry}", volEntry?.Value ?? "null");
          if (volEntry != null && int.TryParse(volEntry.Value, out var storedVol))
            volumePercent = storedVol;

          var muteEntry = store.GetEntryAsync("AudioPreferences:IsMuted").GetAwaiter().GetResult();
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

  /// <summary>
  /// Sets the gain offset for a source type, marks it as manual, and schedules persistence.
  /// </summary>
  public void SetSourceGain(AudioSourceType sourceType, float gain)
  {
    gain = Math.Clamp(gain, 0f, 2f);
    lock (_sourceGainPersistLock)
    {
      _sourceGainOffsets[sourceType.ToString()] = gain;
    }
    // User-initiated change → switch to manual mode
    SetSourceGainMode(sourceType, "manual");
    _logger.LogInformation("Source gain set: {SourceType} = {Gain:F2} (manual)", sourceType, gain);
    ScheduleSourceGainPersist();
  }

  /// <summary>
  /// Sets the gain offset for a source type WITHOUT switching to manual mode.
  /// Used by the learning service for auto-gain application.
  /// </summary>
  public void SetSourceGainInternal(AudioSourceType sourceType, float gain)
  {
    gain = Math.Clamp(gain, 0f, 2f);
    lock (_sourceGainPersistLock)
    {
      _sourceGainOffsets[sourceType.ToString()] = gain;
    }
    _logger.LogDebug("Source gain set internally: {SourceType} = {Gain:F2} (auto)", sourceType, gain);
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
  public void RestoreSourceGainOffsets()
  {
    if (_configurationManager == null)
    {
      _logger.LogWarning("No configuration manager available, cannot restore source gain offsets");
      return;
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var store = _configurationManager.GetStoreAsync(storeId).GetAwaiter().GetResult();

      // Read all AudioPreferences:SourceGain:* keys
      var sourceTypes = Enum.GetValues<AudioSourceType>();
      var restored = 0;
      lock (_sourceGainPersistLock)
      {
        foreach (var sourceType in sourceTypes)
        {
          var key = $"AudioPreferences:SourceGain:{sourceType}";
          var entry = store.GetEntryAsync(key).GetAwaiter().GetResult();
          if (entry != null && float.TryParse(entry.Value, CultureInfo.InvariantCulture, out var gain))
          {
            _sourceGainOffsets[sourceType.ToString()] = Math.Clamp(gain, 0f, 2f);
            restored++;
          }
        }
      }

      if (restored > 0)
      {
        _logger.LogInformation("Restored {Count} source gain offsets from config store", restored);
      }

      // Also restore learning data (learned RMS + gain mode)
      RestoreSourceLearningData();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to restore source gain offsets");
    }
  }

  // --- Source Level Learning ---

  /// <summary>Target RMS: -18 dBFS = 10^(-18/20) ≈ 0.126 linear.</summary>
  public const float TargetRms = 0.126f;

  /// <summary>Minimum samples before auto-gain is applied.</summary>
  public const int MinSamplesForAutoGain = 10;

  /// <summary>EMA smoothing factor (0.1 = slow adaptation, ~30-sample effective window).</summary>
  private const float EmaAlpha = 0.1f;

  /// <summary>
  /// Gets the learned RMS for a source type, or null if not enough data.
  /// </summary>
  public float? GetSourceLearnedRms(AudioSourceType sourceType)
  {
    lock (_sourceRmsLock)
    {
      var key = sourceType.ToString();
      if (!_sourceLearnedRms.TryGetValue(key, out var rms))
        return null;
      if (!_sourceSampleCount.TryGetValue(key, out var count) || count < MinSamplesForAutoGain)
        return null;
      return rms;
    }
  }

  /// <summary>
  /// Updates the learned RMS for a source using exponential moving average.
  /// </summary>
  public void UpdateSourceLearnedRms(AudioSourceType sourceType, float rms)
  {
    lock (_sourceRmsLock)
    {
      var key = sourceType.ToString();
      if (_sourceLearnedRms.TryGetValue(key, out var existing))
      {
        _sourceLearnedRms[key] = EmaAlpha * rms + (1 - EmaAlpha) * existing;
      }
      else
      {
        _sourceLearnedRms[key] = rms;
      }

      _sourceSampleCount[key] = _sourceSampleCount.GetValueOrDefault(key, 0) + 1;
    }
    ScheduleSourceRmsPersist();
  }

  /// <summary>
  /// Gets the gain mode for a source type ("auto" or "manual", default "auto").
  /// </summary>
  public string GetSourceGainMode(AudioSourceType sourceType)
  {
    lock (_sourceRmsLock)
    {
      return _sourceGainMode.TryGetValue(sourceType.ToString(), out var mode) ? mode : "auto";
    }
  }

  /// <summary>
  /// Sets the gain mode for a source type and persists immediately.
  /// </summary>
  public void SetSourceGainMode(AudioSourceType sourceType, string mode)
  {
    lock (_sourceRmsLock)
    {
      _sourceGainMode[sourceType.ToString()] = mode;
    }
    // Persist mode immediately (not debounced — mode changes are infrequent)
    _ = PersistSourceGainModeAsync(sourceType, mode);
  }

  /// <summary>
  /// Gets the sample count for a source type (resets on restart).
  /// </summary>
  public int GetSourceSampleCount(AudioSourceType sourceType)
  {
    lock (_sourceRmsLock)
    {
      return _sourceSampleCount.GetValueOrDefault(sourceType.ToString(), 0);
    }
  }

  /// <summary>
  /// Returns auto-gain status for all source types.
  /// </summary>
  public Dictionary<string, AutoGainInfo> GetAutoGainStatus()
  {
    var result = new Dictionary<string, AutoGainInfo>();
    var sourceTypes = Enum.GetValues<AudioSourceType>();

    foreach (var sourceType in sourceTypes)
    {
      var key = sourceType.ToString();
      float? learnedRms;
      float? suggestedGain = null;
      string mode;
      int sampleCount;

      lock (_sourceRmsLock)
      {
        learnedRms = _sourceLearnedRms.TryGetValue(key, out var rms) ? rms : null;
        mode = _sourceGainMode.TryGetValue(key, out var m) ? m : "auto";
        sampleCount = _sourceSampleCount.GetValueOrDefault(key, 0);
      }

      if (learnedRms.HasValue && sampleCount >= MinSamplesForAutoGain && learnedRms.Value > 0.001f)
      {
        suggestedGain = Math.Clamp(TargetRms / learnedRms.Value, 0.1f, 2.0f);
      }

      result[key] = new AutoGainInfo(learnedRms, suggestedGain, mode, sampleCount);
    }

    return result;
  }

  /// <summary>
  /// Restores learned RMS and gain mode from the configuration store.
  /// Called from RestoreSourceGainOffsets().
  /// </summary>
  public void RestoreSourceLearningData()
  {
    if (_configurationManager == null) return;

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var store = _configurationManager.GetStoreAsync(storeId).GetAwaiter().GetResult();
      var sourceTypes = Enum.GetValues<AudioSourceType>();
      var restored = 0;

      lock (_sourceRmsLock)
      {
        foreach (var sourceType in sourceTypes)
        {
          var key = sourceType.ToString();

          // Restore learned RMS
          var rmsEntry = store.GetEntryAsync($"AudioPreferences:SourceRms:{key}").GetAwaiter().GetResult();
          if (rmsEntry != null && float.TryParse(rmsEntry.Value, CultureInfo.InvariantCulture, out var rms))
          {
            _sourceLearnedRms[key] = rms;
            // Start with MinSamplesForAutoGain so restored data is immediately usable
            _sourceSampleCount[key] = MinSamplesForAutoGain;
            restored++;
          }

          // Restore gain mode
          var modeEntry = store.GetEntryAsync($"AudioPreferences:SourceGainMode:{key}").GetAwaiter().GetResult();
          if (modeEntry != null)
          {
            _sourceGainMode[key] = modeEntry.Value;
          }
        }
      }

      if (restored > 0)
      {
        _logger.LogInformation("Restored {Count} source learning entries from config store", restored);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to restore source learning data");
    }
  }

  private void ScheduleSourceRmsPersist()
  {
    if (_configurationManager == null) return;

    lock (_sourceRmsLock)
    {
      _sourceRmsPersistTimer?.Dispose();
      _sourceRmsPersistTimer = new Timer(
        _ => _ = PersistSourceRmsAsync(),
        null,
        TimeSpan.FromMilliseconds(500),
        Timeout.InfiniteTimeSpan);
    }
  }

  private async Task PersistSourceRmsAsync()
  {
    if (_configurationManager == null) return;

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      Dictionary<string, float> snapshot;
      lock (_sourceRmsLock)
      {
        snapshot = new Dictionary<string, float>(_sourceLearnedRms);
      }

      foreach (var (sourceType, rms) in snapshot)
      {
        await _configurationManager.SetValueAsync(
          storeId,
          $"AudioPreferences:SourceRms:{sourceType}",
          rms.ToString("F6", CultureInfo.InvariantCulture));
      }

      _logger.LogDebug("Persisted {Count} source RMS values", snapshot.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist source RMS values");
    }
  }

  private async Task PersistSourceGainModeAsync(AudioSourceType sourceType, string mode)
  {
    if (_configurationManager == null) return;

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      await _configurationManager.SetValueAsync(
        storeId,
        $"AudioPreferences:SourceGainMode:{sourceType}",
        mode);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist source gain mode for {SourceType}", sourceType);
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
        _ => _ = PersistSourceGainAsync(),
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

    lock (_sourceRmsLock)
    {
      _sourceRmsPersistTimer?.Dispose();
      _sourceRmsPersistTimer = null;
    }
  }
}

