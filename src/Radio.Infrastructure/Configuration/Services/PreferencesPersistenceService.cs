using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using System.Text.Json;

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
  private readonly IHostApplicationLifetime _lifetime;
  private readonly TimeSpan _savePeriod = TimeSpan.FromSeconds(30); // Save every 30 seconds

  public PreferencesPersistenceService(
    ILogger<PreferencesPersistenceService> logger,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IOptionsMonitor<FilePlayerPreferences> filePlayerPreferences,
    IOptionsMonitor<TTSPreferences> ttsPreferences,
    IOptionsMonitor<RadioPreferences> radioPreferences,
    IOptionsMonitor<GenericSourcePreferences> genericSourcePreferences,
    IConfigurationManager configurationManager,
    IHostApplicationLifetime lifetime)
  {
    _logger = logger;
    _audioPreferences = audioPreferences;
    _filePlayerPreferences = filePlayerPreferences;
    _ttsPreferences = ttsPreferences;
    _radioPreferences = radioPreferences;
    _genericSourcePreferences = genericSourcePreferences;
    _configurationManager = configurationManager;
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
      var tasks = new List<Task>
      {
        SavePreferenceSectionAsync(AudioPreferences.SectionName, _audioPreferences.CurrentValue, cancellationToken),
        SavePreferenceSectionAsync(FilePlayerPreferences.SectionName, _filePlayerPreferences.CurrentValue, cancellationToken),
        SavePreferenceSectionAsync(TTSPreferences.SectionName, _ttsPreferences.CurrentValue, cancellationToken),
        SavePreferenceSectionAsync(RadioPreferences.SectionName, _radioPreferences.CurrentValue, cancellationToken),
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
      var json = JsonSerializer.Serialize(preferences);
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
