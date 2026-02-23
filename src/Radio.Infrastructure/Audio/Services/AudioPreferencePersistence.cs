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
  private readonly object _volumePersistLock = new();
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

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    lock (_volumePersistLock)
    {
      _volumePersistTimer?.Dispose();
      _volumePersistTimer = null;
    }
  }
}
