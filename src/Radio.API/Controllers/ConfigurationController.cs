using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Exceptions;
using IRadioConfigurationManager = Radio.Infrastructure.Configuration.Abstractions.IConfigurationManager;

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
  private readonly IRadioConfigurationManager _configurationManager;

  /// <summary>
  /// Initializes a new instance of the ConfigurationController.
  /// </summary>
  public ConfigurationController(
    ILogger<ConfigurationController> logger,
    IOptionsMonitor<AudioOptions> audioOptions,
    IOptionsMonitor<VisualizerOptions> visualizerOptions,
    IOptionsMonitor<AudioOutputOptions> outputOptions,
    IRadioConfigurationManager configurationManager)
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
      // Use the main configuration store (determined by CurrentStoreType)
      var mainStoreId = _configurationManager.CurrentStoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      
      // Try to get the store, create if it doesn't exist
      IConfigurationStore store;
      try
      {
        store = await _configurationManager.GetStoreAsync(mainStoreId);
      }
      catch (FileNotFoundException)
      {
        // Store doesn't exist yet, create it
        store = await _configurationManager.CreateStoreAsync(mainStoreId);
      }
      catch (DirectoryNotFoundException)
      {
        // Store doesn't exist yet, create it
        store = await _configurationManager.CreateStoreAsync(mainStoreId);
      }
      catch (ConfigurationStoreException)
      {
        // Store doesn't exist yet, create it
        store = await _configurationManager.CreateStoreAsync(mainStoreId);
      }
      
      var entries = await store.GetAllEntriesAsync();

      // Filter entries that match the section prefix
      var sectionPrefix = $"{section.ToLowerInvariant()}:";
      var result = new Dictionary<string, object?>();
      
      foreach (var entry in entries)
      {
        if (entry.Key.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
          // Remove the section prefix from the key
          var keyWithoutPrefix = entry.Key.Substring(sectionPrefix.Length);
          // Parse string values to proper JSON types so the client can deserialize correctly
          result[keyWithoutPrefix] = ParseConfigValue(entry.Value);
        }
      }

      // If no entries found, return NotFound
      if (result.Count == 0)
      {
        return NotFound(new { error = $"Configuration section '{section}' not found" });
      }

      return Ok(result);
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
      // Use the main configuration store (determined by CurrentStoreType)
      var mainStoreId = _configurationManager.CurrentStoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var sectionPrefix = $"{section.ToLowerInvariant()}:";
      
      // Set each key-value pair with the section prefix
      foreach (var kvp in data)
      {
        var fullKey = $"{sectionPrefix}{kvp.Key}";
        
        // Handle JsonElement values (from JSON deserialization)
        string valueToStore;
        if (kvp.Value is System.Text.Json.JsonElement jsonElement)
        {
          // For string values, use GetString() to extract the unquoted value
          // GetRawText() returns the raw JSON which includes quotes for strings
          if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
          {
            valueToStore = jsonElement.GetString() ?? "";
          }
          else
          {
            // For complex objects, arrays, numbers, booleans - use GetRawText()
            valueToStore = jsonElement.GetRawText();
          }
        }
        else if (kvp.Value is string str)
        {
          valueToStore = str;
        }
        else
        {
          valueToStore = System.Text.Json.JsonSerializer.Serialize(kvp.Value);
        }
        
        await _configurationManager.SetValueAsync(mainStoreId, fullKey, valueToStore);
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
  /// Parses a string config value to its proper JSON type (bool, long, double, or string).
  /// The configuration store persists all values as strings, but clients expect proper types.
  /// </summary>
  private static object? ParseConfigValue(string value)
  {
    if (bool.TryParse(value, out var boolVal))
      return boolVal;
    if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var longVal))
      return longVal;
    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
      return doubleVal;
    return value;
  }

  /// <summary>
  /// Updates a configuration setting.
  /// </summary>
  /// <param name="request">The configuration update request.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

      // Use the main configuration store with namespaced keys
      var mainStoreId = _configurationManager.CurrentStoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var fullKey = $"{request.Section.ToLowerInvariant()}:{request.Key}";
      
      try
      {
        await _configurationManager.SetValueAsync(mainStoreId, fullKey, request.Value);
        
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
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<ConfigurationStoreInfoDto>> GetStoreInfo([FromQuery] string? storeType = null)
  {
    try
    {
      // Determine which store to query
      var targetStoreType = _configurationManager.CurrentStoreType;
      if (!string.IsNullOrEmpty(storeType))
      {
        targetStoreType = storeType.ToLowerInvariant() == "sqlite" 
          ? Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite 
          : Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Json;
      }

      // Determine the main store ID
      var mainStoreId = targetStoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      
      // Get or create the store
      IConfigurationStore store;
      try
      {
        store = await _configurationManager.GetStoreAsync(mainStoreId);
      }
      catch (ConfigurationStoreException)
      {
        // Store doesn't exist, create it
        _logger.LogInformation("Store {StoreId} not found, creating it", mainStoreId);
        store = await _configurationManager.CreateStoreAsync(mainStoreId);
      }
      
      // Get all entries to count them
      var entries = await store.GetAllEntriesAsync();
      
      // Build the file path based on configuration
      var basePath = Path.GetFullPath("./config"); // From appsettings
      string filePath;
      if (targetStoreType == Radio.Infrastructure.Configuration.Models.ConfigurationStoreType.Sqlite)
      {
        filePath = Path.Combine(basePath, "configuration.db");
      }
      else
      {
        filePath = Path.Combine(basePath, $"{mainStoreId}.json");
      }
      
      // Build store info from the store object
      var storeInfo = new ConfigurationStoreInfoDto
      {
        StoreType = targetStoreType.ToString(),
        Location = filePath,
        SizeBytes = 0, // We'll calculate this if file exists
        LastModified = DateTime.UtcNow,
        EntryCount = entries.Count
      };
      
      // Try to get file info if it exists
      if (System.IO.File.Exists(filePath))
      {
        var fileInfo = new FileInfo(filePath);
        storeInfo.SizeBytes = fileInfo.Length;
        storeInfo.LastModified = fileInfo.LastWriteTime;
      }

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
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<ConfigurationComparisonDto>> CompareStores()
  {
    try
    {
      // Try to get both store types
      IConfigurationStore jsonStoreInstance;
      IConfigurationStore sqliteStoreInstance;
      
      try
      {
        jsonStoreInstance = await _configurationManager.GetStoreAsync("config");
      }
      catch (ConfigurationStoreException)
      {
        // JSON store doesn't exist, create an empty one for comparison
        jsonStoreInstance = await _configurationManager.CreateStoreAsync("config");
      }
      
      try
      {
        sqliteStoreInstance = await _configurationManager.GetStoreAsync("sqlite");
      }
      catch (ConfigurationStoreException)
      {
        // SQLite store doesn't exist, create an empty one for comparison
        sqliteStoreInstance = await _configurationManager.CreateStoreAsync("sqlite");
      }

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

        // Mask secret values for display (check if key contains common secret identifiers)
        bool isSecret = IsSecretKey(key);
        string? displayJsonValue = inJson ? (isSecret ? MaskSecretValue(jsonValue) : jsonValue) : null;
        string? displaySqliteValue = inSqlite ? (isSecret ? MaskSecretValue(sqliteValue) : sqliteValue) : null;

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
          JsonValue = displayJsonValue,
          SqliteValue = displaySqliteValue,
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
  public async Task<IActionResult> ReconcileStores([FromBody] ReconcileConfigurationRequestDto request)
  {
    try
    {
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

      // Get or create stores
      IConfigurationStore sourceStore;
      IConfigurationStore targetStore;
      
      try
      {
        sourceStore = await _configurationManager.GetStoreAsync(sourceStoreId);
      }
      catch (ConfigurationStoreException)
      {
        return BadRequest(new { error = $"Source store '{request.SourceStore}' not found" });
      }
      
      try
      {
        targetStore = await _configurationManager.GetStoreAsync(targetStoreId);
      }
      catch (ConfigurationStoreException)
      {
        // Target store doesn't exist, create it
        _logger.LogInformation("Target store {StoreId} not found, creating it", targetStoreId);
        targetStore = await _configurationManager.CreateStoreAsync(targetStoreId);
      }

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
  public async Task<IActionResult> ExportConfiguration([FromQuery] string format = "json", [FromQuery] string? storeType = null)
  {
    try
    {
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
  public async Task<IActionResult> ImportConfiguration(IFormFile file, [FromQuery] string? targetStore = null, [FromQuery] bool overwrite = true)
  {
    try
    {
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

  // ========== Helper Methods ==========

  /// <summary>
  /// Determines if a configuration key is likely to contain a secret value.
  /// </summary>
  private static bool IsSecretKey(string key)
  {
    var lowerKey = key.ToLowerInvariant();
    return lowerKey.Contains("secret") ||
           lowerKey.Contains("password") ||
           lowerKey.Contains("apikey") ||
           lowerKey.Contains("api_key") ||
           lowerKey.Contains("token") ||
           lowerKey.Contains("clientid") ||
           lowerKey.Contains("client_id") ||
           lowerKey.Contains("clientsecret") ||
           lowerKey.Contains("client_secret") ||
           lowerKey.Contains("refreshtoken") ||
           lowerKey.Contains("refresh_token") ||
           lowerKey.Contains("tts:") ||
           lowerKey.Contains("acoustid:");
  }

  /// <summary>
  /// Masks a secret value for display, showing a pattern indicator.
  /// </summary>
  private static string MaskSecretValue(string? value)
  {
    if (string.IsNullOrEmpty(value))
      return string.Empty;
    
    // If it's already a secret tag (${secret:...}), return as-is
    if (value.StartsWith("${secret:", StringComparison.OrdinalIgnoreCase) && value.EndsWith("}"))
      return value;
    
    // Otherwise, mask the value showing only first/last 4 chars
    if (value.Length <= 8)
      return "********";
    
    return $"{value.Substring(0, 4)}...{value.Substring(value.Length - 4)}";
  }
}
