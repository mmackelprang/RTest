using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.API.Models;
using Radio.Core.Configuration;
using RadioConfigurationManager = Radio.Infrastructure.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for configuration management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ConfigurationController : ControllerBase
{
  private readonly ILogger<ConfigurationController> _logger;
  private readonly IOptionsMonitor<AudioOptions> _audioOptions;
  private readonly IOptionsMonitor<VisualizerOptions> _visualizerOptions;
  private readonly IOptionsMonitor<AudioOutputOptions> _outputOptions;
  private readonly RadioConfigurationManager? _configurationManager;

  /// <summary>
  /// Initializes a new instance of the ConfigurationController.
  /// </summary>
  public ConfigurationController(
    ILogger<ConfigurationController> logger,
    IOptionsMonitor<AudioOptions> audioOptions,
    IOptionsMonitor<VisualizerOptions> visualizerOptions,
    IOptionsMonitor<AudioOutputOptions> outputOptions,
    RadioConfigurationManager? configurationManager = null)
  {
    _logger = logger;
    _audioOptions = audioOptions;
    _visualizerOptions = visualizerOptions;
    _outputOptions = outputOptions;
    _configurationManager = configurationManager;
  }

  /// <summary>
  /// Gets all configuration settings.
  /// </summary>
  /// <returns>The current configuration settings.</returns>
  [HttpGet]
  [ProducesResponseType(typeof(ConfigurationSettingsDto), StatusCodes.Status200OK)]
  public ActionResult<ConfigurationSettingsDto> GetConfiguration()
  {
    try
    {
      var audio = _audioOptions.CurrentValue;
      var visualizer = _visualizerOptions.CurrentValue;
      var output = _outputOptions.CurrentValue;

      var settings = new ConfigurationSettingsDto
      {
        Audio = new AudioConfigurationDto
        {
          DefaultSource = audio.DefaultSource,
          DuckingPercentage = audio.DuckingPercentage,
          DuckingPolicy = audio.DuckingPolicy.ToString(),
          DuckingAttackMs = audio.DuckingAttackMs,
          DuckingReleaseMs = audio.DuckingReleaseMs
        },
        Visualizer = new VisualizerConfigurationDto
        {
          FFTSize = visualizer.FFTSize,
          WaveformSampleCount = visualizer.WaveformSampleCount,
          PeakHoldTimeMs = visualizer.PeakHoldTimeMs,
          ApplyWindowFunction = visualizer.ApplyWindowFunction,
          SpectrumSmoothing = visualizer.SpectrumSmoothing
        },
        Output = new OutputConfigurationDto
        {
          Local = new LocalOutputSettingsDto
          {
            Enabled = output.Local.Enabled,
            PreferredDeviceId = output.Local.PreferredDeviceId,
            DefaultVolume = output.Local.DefaultVolume
          },
          HttpStream = new HttpStreamSettingsDto
          {
            Enabled = output.HttpStream.Enabled,
            Port = output.HttpStream.Port,
            EndpointPath = output.HttpStream.EndpointPath,
            SampleRate = output.HttpStream.SampleRate,
            Channels = output.HttpStream.Channels
          },
          GoogleCast = new GoogleCastSettingsDto
          {
            Enabled = output.GoogleCast.Enabled,
            DiscoveryTimeoutSeconds = output.GoogleCast.DiscoveryTimeoutSeconds,
            DefaultVolume = output.GoogleCast.DefaultVolume
          }
        }
      };

      return Ok(settings);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting configuration");
      return StatusCode(500, new { error = "Failed to get configuration" });
    }
  }

  /// <summary>
  /// Gets audio configuration settings.
  /// </summary>
  /// <returns>The audio configuration.</returns>
  [HttpGet("audio")]
  [ProducesResponseType(typeof(AudioConfigurationDto), StatusCodes.Status200OK)]
  public ActionResult<AudioConfigurationDto> GetAudioConfiguration()
  {
    try
    {
      var audio = _audioOptions.CurrentValue;

      return Ok(new AudioConfigurationDto
      {
        DefaultSource = audio.DefaultSource,
        DuckingPercentage = audio.DuckingPercentage,
        DuckingPolicy = audio.DuckingPolicy.ToString(),
        DuckingAttackMs = audio.DuckingAttackMs,
        DuckingReleaseMs = audio.DuckingReleaseMs
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting audio configuration");
      return StatusCode(500, new { error = "Failed to get audio configuration" });
    }
  }

  /// <summary>
  /// Gets visualizer configuration settings.
  /// </summary>
  /// <returns>The visualizer configuration.</returns>
  [HttpGet("visualizer")]
  [ProducesResponseType(typeof(VisualizerConfigurationDto), StatusCodes.Status200OK)]
  public ActionResult<VisualizerConfigurationDto> GetVisualizerConfiguration()
  {
    try
    {
      var visualizer = _visualizerOptions.CurrentValue;

      return Ok(new VisualizerConfigurationDto
      {
        FFTSize = visualizer.FFTSize,
        WaveformSampleCount = visualizer.WaveformSampleCount,
        PeakHoldTimeMs = visualizer.PeakHoldTimeMs,
        ApplyWindowFunction = visualizer.ApplyWindowFunction,
        SpectrumSmoothing = visualizer.SpectrumSmoothing
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting visualizer configuration");
      return StatusCode(500, new { error = "Failed to get visualizer configuration" });
    }
  }

  /// <summary>
  /// Gets output configuration settings.
  /// </summary>
  /// <returns>The output configuration.</returns>
  [HttpGet("output")]
  [ProducesResponseType(typeof(OutputConfigurationDto), StatusCodes.Status200OK)]
  public ActionResult<OutputConfigurationDto> GetOutputConfiguration()
  {
    try
    {
      var output = _outputOptions.CurrentValue;

      return Ok(new OutputConfigurationDto
      {
        Local = new LocalOutputSettingsDto
        {
          Enabled = output.Local.Enabled,
          PreferredDeviceId = output.Local.PreferredDeviceId,
          DefaultVolume = output.Local.DefaultVolume
        },
        HttpStream = new HttpStreamSettingsDto
        {
          Enabled = output.HttpStream.Enabled,
          Port = output.HttpStream.Port,
          EndpointPath = output.HttpStream.EndpointPath,
          SampleRate = output.HttpStream.SampleRate,
          Channels = output.HttpStream.Channels
        },
        GoogleCast = new GoogleCastSettingsDto
        {
          Enabled = output.GoogleCast.Enabled,
          DiscoveryTimeoutSeconds = output.GoogleCast.DiscoveryTimeoutSeconds,
          DefaultVolume = output.GoogleCast.DefaultVolume
        }
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting output configuration");
      return StatusCode(500, new { error = "Failed to get output configuration" });
    }
  }

  /// <summary>
  /// Gets a generic configuration section by section name.
  /// </summary>
  /// <param name="section">The configuration section name.</param>
  /// <returns>The configuration section data.</returns>
  [HttpGet("{section}")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult> GetConfigurationSection(string section)
  {
    // Validate section name
    if (string.IsNullOrWhiteSpace(section))
    {
      return BadRequest(new { error = "Section name is required" });
    }

    if (!IsValidSectionName(section))
    {
      return BadRequest(new { error = "Invalid section name. Only alphanumeric characters, hyphens, and underscores are allowed." });
    }

    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      var storeId = section.ToLowerInvariant();
      
      // Try to get the store
      try
      {
        var store = await _configurationManager.GetStoreAsync(storeId);
        var entries = await store.GetAllEntriesAsync();

        // Build a dictionary from the entries
        var result = new Dictionary<string, object?>();
        foreach (var entry in entries)
        {
          result[entry.Key] = entry.Value;
        }

        return Ok(result);
      }
      catch (FileNotFoundException)
      {
        // Store doesn't exist
        return NotFound(new { error = $"Configuration section '{section}' not found" });
      }
      catch (DirectoryNotFoundException)
      {
        // Store doesn't exist
        return NotFound(new { error = $"Configuration section '{section}' not found" });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting configuration section {Section}", section);
      return StatusCode(500, new { error = "Internal server error while retrieving configuration" });
    }
  }

  /// <summary>
  /// Updates an entire configuration section.
  /// </summary>
  /// <param name="section">The configuration section name.</param>
  /// <param name="data">The configuration data to save.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost("{section}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult> UpdateConfigurationSection(string section, [FromBody] Dictionary<string, object> data)
  {
    // Validate section name
    if (string.IsNullOrWhiteSpace(section))
    {
      return BadRequest(new { error = "Section name is required" });
    }

    if (!IsValidSectionName(section))
    {
      return BadRequest(new { error = "Invalid section name. Only alphanumeric characters, hyphens, and underscores are allowed." });
    }

    // Validate data
    if (data == null || data.Count == 0)
    {
      return BadRequest(new { error = "Configuration data is required" });
    }

    // Limit data size to prevent memory issues (max 100 keys)
    if (data.Count > 100)
    {
      return BadRequest(new { error = "Configuration data exceeds maximum allowed size (100 keys)" });
    }

    // Validate keys
    foreach (var key in data.Keys)
    {
      if (string.IsNullOrWhiteSpace(key))
      {
        return BadRequest(new { error = "Configuration keys cannot be null or empty" });
      }
    }

    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      var storeId = section.ToLowerInvariant();
      
      // Set each key-value pair
      foreach (var kvp in data)
      {
        await _configurationManager.SetValueAsync(storeId, kvp.Key, kvp.Value);
      }

      _logger.LogInformation("Configuration section {Section} updated successfully with {Count} keys", section, data.Count);
      return Ok(new { message = "Configuration updated successfully", section });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error updating configuration section {Section}", section);
      return StatusCode(500, new { error = "Failed to update configuration" });
    }
  }

  /// <summary>
  /// Validates that a section name contains only allowed characters.
  /// </summary>
  private static bool IsValidSectionName(string section)
  {
    if (string.IsNullOrWhiteSpace(section))
      return false;

    // Allow alphanumeric, hyphens, underscores, and dots
    return System.Text.RegularExpressions.Regex.IsMatch(section, @"^[a-zA-Z0-9_\-\.]+$");
  }

  /// <summary>
  /// Updates a configuration setting.
  /// </summary>
  /// <param name="request">The configuration update request.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult> UpdateConfiguration([FromBody] UpdateConfigurationRequest request)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.Section))
      {
        return BadRequest(new { error = "Section is required" });
      }

      if (string.IsNullOrWhiteSpace(request.Key))
      {
        return BadRequest(new { error = "Key is required" });
      }

      _logger.LogInformation(
        "Configuration update requested: {Section}:{Key} = {Value}",
        request.Section, request.Key, request.Value);

      // Check if configuration manager is available
      if (_configurationManager == null)
      {
        return StatusCode(501, new
        {
          message = "Configuration update requires IConfigurationManager integration",
          section = request.Section,
          key = request.Key,
          value = request.Value,
          note = "Configuration values are read-only at runtime without the managed configuration system"
        });
      }

      // Update the configuration using the configuration manager
      // The store ID typically corresponds to the section name
      var storeId = request.Section.ToLowerInvariant();
      
      try
      {
        await _configurationManager.SetValueAsync(storeId, request.Key, request.Value);
        
        _logger.LogInformation(
          "Configuration updated successfully: {Section}:{Key}",
          request.Section, request.Key);

        return Ok(new
        {
          message = "Configuration updated successfully",
          section = request.Section,
          key = request.Key,
          value = request.Value
        });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to update configuration: {Section}:{Key}", 
          request.Section, request.Key);
        return BadRequest(new
        {
          error = "Failed to update configuration",
          details = ex.Message
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error updating configuration");
      return StatusCode(500, new { error = "Failed to update configuration" });
    }
  }

  // ========== Phase 5: Configuration Store Management Endpoints ==========

  /// <summary>
  /// Gets metadata about the current configuration store.
  /// </summary>
  /// <param name="storeType">Store type to query (json or sqlite). Defaults to current store.</param>
  /// <returns>Store metadata including type, location, size, and entry count.</returns>
  [HttpGet("store-info")]
  [ProducesResponseType(typeof(ConfigurationStoreInfoDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult<ConfigurationStoreInfoDto>> GetStoreInfo([FromQuery] string? storeType = null)
  {
    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      // Determine which store to query
      var targetStoreType = _configurationManager.CurrentStoreType;
      if (!string.IsNullOrEmpty(storeType))
      {
        targetStoreType = storeType.ToLowerInvariant() == "sqlite" 
          ? Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite 
          : Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Json;
      }

      // List all stores and find the one matching our criteria
      var stores = await _configurationManager.ListStoresAsync();
      var targetStore = stores.FirstOrDefault(s => s.StoreType == targetStoreType);

      if (targetStore == null)
      {
        return NotFound(new { error = $"No {targetStoreType} store found" });
      }

      var storeInfo = new ConfigurationStoreInfoDto
      {
        StoreType = targetStoreType.ToString(),
        Location = targetStore.Path,
        SizeBytes = targetStore.SizeBytes,
        LastModified = targetStore.LastModifiedAt.DateTime,
        EntryCount = targetStore.EntryCount
      };

      return Ok(storeInfo);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting store info");
      return StatusCode(500, new { error = "Failed to get store info", details = ex.Message });
    }
  }

  /// <summary>
  /// Compares configuration between JSON and SQLite stores.
  /// </summary>
  /// <returns>Comparison results showing differences between stores.</returns>
  [HttpGet("compare")]
  [ProducesResponseType(typeof(ConfigurationComparisonDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult<ConfigurationComparisonDto>> CompareStores()
  {
    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      var stores = await _configurationManager.ListStoresAsync();
      var jsonStore = stores.FirstOrDefault(s => s.StoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Json);
      var sqliteStore = stores.FirstOrDefault(s => s.StoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite);

      if (jsonStore == null || sqliteStore == null)
      {
        return BadRequest(new { error = "Both JSON and SQLite stores must exist for comparison" });
      }

      // Get all entries from both stores
      var jsonStoreInstance = await _configurationManager.GetStoreAsync("config");
      var sqliteStoreInstance = await _configurationManager.GetStoreAsync("sqlite");

      var jsonEntries = await jsonStoreInstance.GetAllEntriesAsync(Radio.Infrastructure.Configuration.Models.ConfigurationReadMode.Raw);
      var sqliteEntries = await sqliteStoreInstance.GetAllEntriesAsync(Radio.Infrastructure.Configuration.Models.ConfigurationReadMode.Raw);

      // Build dictionaries for comparison
      var jsonDict = jsonEntries.ToDictionary(e => e.Key, e => e.Value);
      var sqliteDict = sqliteEntries.ToDictionary(e => e.Key, e => e.Value);

      var allKeys = jsonDict.Keys.Union(sqliteDict.Keys).OrderBy(k => k).ToList();
      var differences = new List<ConfigurationDifferenceDto>();

      foreach (var key in allKeys)
      {
        var inJson = jsonDict.TryGetValue(key, out var jsonValue);
        var inSqlite = sqliteDict.TryGetValue(key, out var sqliteValue);

        string status;
        if (inJson && !inSqlite)
        {
          status = "OnlyInJson";
        }
        else if (!inJson && inSqlite)
        {
          status = "OnlyInSqlite";
        }
        else if (jsonValue != sqliteValue)
        {
          status = "Different";
        }
        else
        {
          status = "Same";
        }

        differences.Add(new ConfigurationDifferenceDto
        {
          Key = key,
          JsonValue = inJson ? jsonValue : null,
          SqliteValue = inSqlite ? sqliteValue : null,
          Status = status
        });
      }

      var comparison = new ConfigurationComparisonDto
      {
        JsonEntryCount = jsonDict.Count,
        SqliteEntryCount = sqliteDict.Count,
        Differences = differences
      };

      return Ok(comparison);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error comparing stores");
      return StatusCode(500, new { error = "Failed to compare stores", details = ex.Message });
    }
  }

  /// <summary>
  /// Reconciles configuration by copying values between stores.
  /// </summary>
  /// <param name="request">Reconciliation request with source, target, and keys to copy.</param>
  /// <returns>Success message with count of reconciled keys.</returns>
  [HttpPost("reconcile")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<IActionResult> ReconcileStores([FromBody] ReconcileConfigurationRequestDto request)
  {
    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      if (request.Keys == null || request.Keys.Count == 0)
      {
        return BadRequest(new { error = "No keys specified for reconciliation" });
      }

      // Validate store names
      var sourceStoreId = request.SourceStore.ToLowerInvariant() == "json" ? "config" : "sqlite";
      var targetStoreId = request.TargetStore.ToLowerInvariant() == "json" ? "config" : "sqlite";

      if (sourceStoreId == targetStoreId)
      {
        return BadRequest(new { error = "Source and target stores must be different" });
      }

      // Get stores
      var sourceStore = await _configurationManager.GetStoreAsync(sourceStoreId);
      var targetStore = await _configurationManager.GetStoreAsync(targetStoreId);

      int copiedCount = 0;
      var errors = new List<string>();

      foreach (var key in request.Keys)
      {
        try
        {
          var entry = await sourceStore.GetEntryAsync(key, Radio.Infrastructure.Configuration.Models.ConfigurationReadMode.Raw);
          if (entry != null)
          {
            await targetStore.SetEntryAsync(key, entry.Value);
            copiedCount++;
          }
          else
          {
            errors.Add($"Key '{key}' not found in source store");
          }
        }
        catch (Exception ex)
        {
          errors.Add($"Failed to copy key '{key}': {ex.Message}");
          _logger.LogError(ex, "Failed to copy key {Key} during reconciliation", key);
        }
      }

      await targetStore.SaveAsync();

      _logger.LogInformation(
        "Reconciled {Count} keys from {Source} to {Target}",
        copiedCount, request.SourceStore, request.TargetStore);

      return Ok(new
      {
        message = "Reconciliation completed",
        copiedCount,
        totalRequested = request.Keys.Count,
        errors = errors.Count > 0 ? errors : null
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error reconciling stores");
      return StatusCode(500, new { error = "Failed to reconcile stores", details = ex.Message });
    }
  }

  /// <summary>
  /// Exports configuration as a downloadable file.
  /// </summary>
  /// <param name="format">Export format (json or radiobak). Default is json.</param>
  /// <param name="storeType">Store type to export (json or sqlite). Default is current store.</param>
  /// <returns>File download.</returns>
  [HttpGet("export")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<IActionResult> ExportConfiguration([FromQuery] string format = "json", [FromQuery] string? storeType = null)
  {
    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      // Determine which store to export
      var storeId = "config"; // Default to JSON
      if (!string.IsNullOrEmpty(storeType) && storeType.ToLowerInvariant() == "sqlite")
      {
        storeId = "sqlite";
      }

      var store = await _configurationManager.GetStoreAsync(storeId);
      var entries = await store.GetAllEntriesAsync(Radio.Infrastructure.Configuration.Models.ConfigurationReadMode.Raw);

      if (format.ToLowerInvariant() == "radiobak")
      {
        // Create a backup using the backup service
        var backup = await _configurationManager.Backup.CreateBackupAsync(
          storeId, 
          store.StoreType, 
          $"Manual export at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

        // Export backup to memory stream
        var memoryStream = new MemoryStream();
        await _configurationManager.Backup.ExportBackupAsync(backup.BackupId, memoryStream);
        memoryStream.Position = 0;

        var fileName = $"radio-config-{DateTime.UtcNow:yyyyMMdd-HHmmss}.radiobak";
        return File(memoryStream, "application/octet-stream", fileName);
      }
      else
      {
        // Export as JSON
        var config = new Dictionary<string, string>();
        foreach (var entry in entries)
        {
          config[entry.Key] = entry.Value;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
          WriteIndented = true
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var fileName = $"radio-config-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        
        return File(bytes, "application/json", fileName);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error exporting configuration");
      return StatusCode(500, new { error = "Failed to export configuration", details = ex.Message });
    }
  }

  /// <summary>
  /// Imports configuration from an uploaded file.
  /// </summary>
  /// <param name="file">Configuration file to import (.json or .radiobak).</param>
  /// <param name="targetStore">Target store (json or sqlite). Default is current store.</param>
  /// <param name="overwrite">Whether to overwrite existing values. Default is true.</param>
  /// <returns>Import result with count of imported keys.</returns>
  [HttpPost("import")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<IActionResult> ImportConfiguration(IFormFile file, [FromQuery] string? targetStore = null, [FromQuery] bool overwrite = true)
  {
    try
    {
      if (_configurationManager == null)
      {
        return StatusCode(501, new { error = "Configuration manager not available" });
      }

      if (file == null || file.Length == 0)
      {
        return BadRequest(new { error = "No file uploaded" });
      }

      var fileName = file.FileName.ToLowerInvariant();
      var storeId = targetStore?.ToLowerInvariant() == "sqlite" ? "sqlite" : "config";

      if (fileName.EndsWith(".radiobak"))
      {
        // Import backup file
        using var stream = file.OpenReadStream();
        var backup = await _configurationManager.Backup.ImportBackupAsync(stream);
        await _configurationManager.Backup.RestoreBackupAsync(backup.BackupId, overwrite);

        _logger.LogInformation("Imported backup: {BackupId}", backup.BackupId);

        return Ok(new
        {
          message = "Backup imported and restored successfully",
          backupId = backup.BackupId
        });
      }
      else if (fileName.EndsWith(".json"))
      {
        // Import JSON file
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();

        var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (config == null)
        {
          return BadRequest(new { error = "Invalid JSON format" });
        }

        var store = await _configurationManager.GetStoreAsync(storeId);
        int importedCount = 0;

        foreach (var kvp in config)
        {
          if (overwrite || !await store.ExistsAsync(kvp.Key))
          {
            await store.SetEntryAsync(kvp.Key, kvp.Value);
            importedCount++;
          }
        }

        await store.SaveAsync();

        _logger.LogInformation("Imported {Count} configuration entries from JSON", importedCount);

        return Ok(new
        {
          message = "Configuration imported successfully",
          importedCount,
          totalInFile = config.Count,
          overwrite
        });
      }
      else
      {
        return BadRequest(new { error = "Invalid file format. Only .json and .radiobak files are supported." });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error importing configuration");
      return StatusCode(500, new { error = "Failed to import configuration", details = ex.Message });
    }
  }
}
