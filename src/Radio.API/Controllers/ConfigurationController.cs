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
}
