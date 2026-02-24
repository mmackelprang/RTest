using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radio.Infrastructure.Configuration.Services;

/// <summary>
/// Background service that periodically persists user preferences to the configuration store.
/// Ensures preferences are saved on shutdown and at regular intervals.
/// </summary>
public class PreferencesPersistenceService : BackgroundService
{
  private readonly ILogger<PreferencesPersistenceService> _logger;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IOptionsMonitor<FilePlayerPreferences> _filePlayerPreferences;
  private readonly IOptionsMonitor<TTSPreferences> _ttsPreferences;
  private readonly IOptionsMonitor<RadioPreferences> _radioPreferences;
  private readonly IOptionsMonitor<GenericSourcePreferences> _genericSourcePreferences;
  private readonly IConfigurationManager _configurationManager;
  private readonly IAudioManager _audioManager;
  private readonly IHostApplicationLifetime _lifetime;
  private readonly TimeSpan _savePeriod = TimeSpan.FromSeconds(30); // Save every 30 seconds
  private static readonly JsonSerializerOptions _serializerOptions = new()
  {
    Converters = { new JsonStringEnumConverter() }
  };

  public PreferencesPersistenceService(
    ILogger<PreferencesPersistenceService> logger,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IOptionsMonitor<FilePlayerPreferences> filePlayerPreferences,
    IOptionsMonitor<TTSPreferences> ttsPreferences,
    IOptionsMonitor<RadioPreferences> radioPreferences,
    IOptionsMonitor<GenericSourcePreferences> genericSourcePreferences,
    IConfigurationManager configurationManager,
    IAudioManager audioManager,
    IHostApplicationLifetime lifetime)
  {
    _logger = logger;
    _audioPreferences = audioPreferences;
    _filePlayerPreferences = filePlayerPreferences;
    _ttsPreferences = ttsPreferences;
    _radioPreferences = radioPreferences;
    _genericSourcePreferences = genericSourcePreferences;
    _configurationManager = configurationManager;
    _audioManager = audioManager;
    _lifetime = lifetime;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Preferences persistence service started");

    // Note: Final save is handled in StopAsync, no need for ApplicationStopping registration

    try
    {
      // Periodic save loop
      while (!stoppingToken.IsCancellationRequested)
      {
        await Task.Delay(_savePeriod, stoppingToken);
        await SaveAllPreferencesAsync(stoppingToken);
      }
    }
    catch (OperationCanceledException)
    {
      // Expected when stopping
      _logger.LogDebug("Preferences persistence service stopping");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error in preferences persistence service");
    }

    _logger.LogInformation("Preferences persistence service stopped");
  }

  /// <summary>
  /// Saves all preference sections to the configuration store.
  /// </summary>
  private async Task SaveAllPreferencesAsync(CancellationToken cancellationToken)
  {
    try
    {
      // Note: AudioPreferences (volume/mute/balance) are NOT saved here.
      // AudioManager handles volume persistence with its own debounced writes directly
      // to the config store. Saving IOptionsMonitor<AudioPreferences> here would overwrite
      // the correct runtime values with stale defaults from appsettings.json.
      var tasks = new List<Task>
      {
        SavePreferenceSectionAsync(FilePlayerPreferences.SectionName, _filePlayerPreferences.CurrentValue, cancellationToken),
        SavePreferenceSectionAsync(TTSPreferences.SectionName, _ttsPreferences.CurrentValue, cancellationToken),
        SaveRadioPreferencesFromLiveStateAsync(cancellationToken),
        SavePreferenceSectionAsync(GenericSourcePreferences.SectionName, _genericSourcePreferences.CurrentValue, cancellationToken)
      };

      await Task.WhenAll(tasks);
      _logger.LogDebug("All preferences saved successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save preferences");
    }
  }

  /// <summary>
  /// Saves radio preferences by reading live state from the active radio source.
  /// If radio is not the active source, skips saving to avoid overwriting good persisted
  /// values with stale IOptionsMonitor defaults (same issue as AudioPreferences — see comment above).
  /// </summary>
  private async Task SaveRadioPreferencesFromLiveStateAsync(CancellationToken cancellationToken)
  {
    try
    {
      var activeSource = _audioManager.ActiveSource;
      if (activeSource is IRadioControl radioControl)
      {
        // Read live state from the actual radio hardware
        var livePrefs = new RadioPreferences
        {
          LastBand = radioControl.CurrentBand.ToString(),
          LastFrequency = radioControl.CurrentFrequency.Hertz,
          LastFrequencyStep = radioControl.FrequencyStep.Hertz,
          LastDeviceVolume = radioControl.DeviceVolume,
          LastEqualizerMode = radioControl.EqualizerMode.ToString()
        };

        await SavePreferenceSectionAsync(RadioPreferences.SectionName, livePrefs, cancellationToken);
        _logger.LogDebug(
          "Saved live radio preferences: Band={Band}, Frequency={Frequency}Hz, Step={Step}Hz",
          livePrefs.LastBand, livePrefs.LastFrequency, livePrefs.LastFrequencyStep);
      }
      else
      {
        // Radio is not active — skip saving to avoid overwriting persisted values with
        // stale IOptionsMonitor defaults. The last-saved values remain in the config store.
        _logger.LogTrace("Radio source not active, skipping radio preferences save");
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to save radio preferences from live state");
    }
  }

  /// <summary>
  /// Saves a specific preference section to the configuration store.
  /// </summary>
  private async Task SavePreferenceSectionAsync<T>(string sectionName, T preferences, CancellationToken cancellationToken) where T : class
  {
    try
    {
      // Get or create the main configuration store
      var mainStoreId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      IConfigurationStore store;
      
      try
      {
        store = await _configurationManager.GetStoreAsync(mainStoreId);
      }
      catch
      {
        store = await _configurationManager.CreateStoreAsync(mainStoreId);
      }

      // Serialize preferences to key-value pairs, preserving JSON structure
      // Use JsonStringEnumConverter so enums are stored as names (e.g. "Off") not integers (e.g. 0)
      var json = JsonSerializer.Serialize(preferences, _serializerOptions);
      var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

      if (dict != null)
      {
        // Set each preference value with section prefix, preserving JSON structure
        var entries = dict.Select(kvp => 
          new ConfigurationEntry
          {
            Key = $"{sectionName}:{kvp.Key}",
            Value = kvp.Value.ValueKind == JsonValueKind.Null ? string.Empty : kvp.Value.ToString()
          }).ToList();

        await store.SetEntriesAsync(entries, cancellationToken);
        await store.SaveAsync(cancellationToken);
        
        _logger.LogTrace("Saved {Count} entries for section {Section}", entries.Count, sectionName);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to save preferences section: {Section}", sectionName);
    }
  }

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Preferences persistence service is stopping - saving final state");
    await SaveAllPreferencesAsync(cancellationToken);
    await base.StopAsync(cancellationToken);
  }
}
