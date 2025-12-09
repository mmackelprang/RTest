using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for radio device controls.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RadioController : ControllerBase
{
  private readonly ILogger<RadioController> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IRadioPresetService _presetService;
  private readonly IRadioFactory _radioFactory;

  /// <summary>
  /// Initializes a new instance of the RadioController.
  /// </summary>
  public RadioController(
    ILogger<RadioController> logger,
    IAudioEngine audioEngine,
    IRadioPresetService presetService,
    IRadioFactory radioFactory)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _presetService = presetService;
    _radioFactory = radioFactory;
  }

  /// <summary>
  /// Gets the current state of the radio device.
  /// </summary>
  /// <returns>The current radio state.</returns>
  /// <response code="200">Returns the radio state.</response>
  /// <response code="400">If the radio is not the active source.</response>
  [HttpGet("state")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult<RadioStateDto> GetRadioState()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting radio state");
      return StatusCode(500, new { error = "Failed to get radio state" });
    }
  }

  /// <summary>
  /// Sets the radio frequency to a specific value.
  /// </summary>
  /// <param name="request">The frequency to set.</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or the frequency is invalid.</response>
  [HttpPost("frequency")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> SetFrequency([FromBody] SetFrequencyRequest request)
  {
    try
    {
      // Note: Frequency validation is delegated to IRadioControl.SetFrequencyAsync
      // which throws ArgumentOutOfRangeException for invalid values
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.SetFrequencyAsync(new Frequency(request.Frequency));
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (ArgumentOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Invalid frequency: {Frequency}", request.Frequency);
      return BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting frequency");
      return StatusCode(500, new { error = "Failed to set frequency" });
    }
  }

  /// <summary>
  /// Steps the radio frequency up by one step.
  /// </summary>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not the active source.</response>
  [HttpPost("frequency/up")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> StepFrequencyUp()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.StepFrequencyUpAsync();
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stepping frequency up");
      return StatusCode(500, new { error = "Failed to step frequency up" });
    }
  }

  /// <summary>
  /// Steps the radio frequency down by one step.
  /// </summary>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not the active source.</response>
  [HttpPost("frequency/down")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> StepFrequencyDown()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.StepFrequencyDownAsync();
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stepping frequency down");
      return StatusCode(500, new { error = "Failed to step frequency down" });
    }
  }

  /// <summary>
  /// Sets the radio band (AM, FM, etc.).
  /// </summary>
  /// <param name="request">The band to set.</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or the band is invalid.</response>
  [HttpPost("band")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> SetBand([FromBody] SetBandRequest request)
  {
    try
    {
      // Validate input first
      if (!Enum.TryParse<RadioBand>(request.Band, true, out var band))
      {
        return BadRequest(new { error = $"Invalid band: {request.Band}. Valid values are: {string.Join(", ", Enum.GetNames<RadioBand>())}" });
      }

      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.SetBandAsync(band);
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting band");
      return StatusCode(500, new { error = "Failed to set band" });
    }
  }

  /// <summary>
  /// Sets the frequency step size.
  /// </summary>
  /// <param name="request">The step size to set.</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or the step size is invalid.</response>
  [HttpPost("step")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> SetFrequencyStep([FromBody] SetFrequencyStepRequest request)
  {
    try
    {
      // Note: Step validation is delegated to IRadioControl.SetFrequencyStepAsync
      // which throws ArgumentOutOfRangeException for invalid values
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.SetFrequencyStepAsync(new Frequency(request.Step));
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (ArgumentOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Invalid frequency step: {Step}", request.Step);
      return BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting frequency step");
      return StatusCode(500, new { error = "Failed to set frequency step" });
    }
  }

  /// <summary>
  /// Starts scanning for stations in the specified direction.
  /// </summary>
  /// <param name="request">The scan direction.</param>
  /// <returns>The updated radio state with scanning active.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or the direction is invalid.</response>
  [HttpPost("scan/start")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> StartScan([FromBody] StartScanRequest request)
  {
    try
    {
      // Validate input first
      if (!Enum.TryParse<ScanDirection>(request.Direction, true, out var direction))
      {
        return BadRequest(new { error = $"Invalid scan direction: {request.Direction}. Valid values are: {string.Join(", ", Enum.GetNames<ScanDirection>())}" });
      }

      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.StartScanAsync(direction);
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error starting scan");
      return StatusCode(500, new { error = "Failed to start scan" });
    }
  }

  /// <summary>
  /// Stops the current scanning operation.
  /// </summary>
  /// <returns>The updated radio state with scanning stopped.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not the active source.</response>
  [HttpPost("scan/stop")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> StopScan()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.StopScanAsync();
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping scan");
      return StatusCode(500, new { error = "Failed to stop scan" });
    }
  }

  /// <summary>
  /// Sets the equalizer mode for the radio device.
  /// </summary>
  /// <param name="request">The equalizer mode to set.</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or the mode is invalid.</response>
  [HttpPost("eq")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> SetEqualizerMode([FromBody] SetEqualizerModeRequest request)
  {
    try
    {
      // Validate input first
      if (!Enum.TryParse<RadioEqualizerMode>(request.Mode, true, out var mode))
      {
        return BadRequest(new { error = $"Invalid equalizer mode: {request.Mode}. Valid values are: {string.Join(", ", Enum.GetNames<RadioEqualizerMode>())}" });
      }

      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.SetEqualizerModeAsync(mode);
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting equalizer mode");
      return StatusCode(500, new { error = "Failed to set equalizer mode" });
    }
  }

  /// <summary>
  /// Sets the device-specific volume level.
  /// </summary>
  /// <param name="request">The volume level (0-100).</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or the volume is invalid.</response>
  [HttpPost("volume")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RadioStateDto>> SetDeviceVolume([FromBody] SetDeviceVolumeRequest request)
  {
    try
    {
      // Validate input first
      if (request.Volume < 0 || request.Volume > 100)
      {
        return BadRequest(new { error = "Volume must be between 0 and 100" });
      }

      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      // Note: DeviceVolume is a synchronous property per IRadioControl interface design
      radioSource.DeviceVolume = request.Volume;
      
      // Return result on the same async context
      return await Task.FromResult(Ok(MapToRadioStateDto(radioSource)));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting device volume");
      return StatusCode(500, new { error = "Failed to set device volume" });
    }
  }

  /// <summary>
  /// Sets the manual gain value for the radio receiver.
  /// </summary>
  /// <param name="request">The gain value in dB.</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active or automatic gain is enabled.</response>
  [HttpPost("gain")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult<RadioStateDto> SetGain([FromBody] SetGainRequest request)
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      if (radioSource.AutoGainEnabled)
      {
        return BadRequest(new { error = "Cannot set manual gain while automatic gain control is enabled" });
      }

      radioSource.Gain = request.Gain;
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting gain");
      return StatusCode(500, new { error = "Failed to set gain" });
    }
  }

  /// <summary>
  /// Toggles automatic gain control on or off.
  /// </summary>
  /// <param name="request">Whether to enable automatic gain control.</param>
  /// <returns>The updated radio state.</returns>
  /// <response code="200">Returns the updated radio state.</response>
  /// <response code="400">If the radio is not active.</response>
  [HttpPost("gain/auto")]
  [ProducesResponseType(typeof(RadioStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult<RadioStateDto> SetAutoGain([FromBody] SetAutoGainRequest request)
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      radioSource.AutoGainEnabled = request.Enabled;
      return Ok(MapToRadioStateDto(radioSource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting automatic gain control");
      return StatusCode(500, new { error = "Failed to set automatic gain control" });
    }
  }

  /// <summary>
  /// Gets the power state of the radio receiver.
  /// </summary>
  /// <returns>The power state (true if powered on, false if off).</returns>
  /// <response code="200">Returns the power state.</response>
  /// <response code="400">If the radio is not active.</response>
  [HttpGet("power")]
  [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<bool>> GetPowerState()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      var powerState = await radioSource.GetPowerStateAsync();
      return Ok(powerState);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting power state");
      return StatusCode(500, new { error = "Failed to get power state" });
    }
  }

  /// <summary>
  /// Toggles the power state of the radio receiver.
  /// </summary>
  /// <returns>The updated power state.</returns>
  /// <response code="200">Returns the new power state.</response>
  /// <response code="400">If the radio is not active.</response>
  [HttpPost("power/toggle")]
  [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<bool>> TogglePowerState()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.TogglePowerStateAsync();
      var newPowerState = await radioSource.GetPowerStateAsync();
      return Ok(newPowerState);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error toggling power state");
      return StatusCode(500, new { error = "Failed to toggle power state" });
    }
  }

  /// <summary>
  /// Starts the radio receiver.
  /// </summary>
  /// <returns>Success status.</returns>
  /// <response code="200">If the radio started successfully.</response>
  /// <response code="400">If the radio is not active or failed to start.</response>
  [HttpPost("startup")]
  [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<bool>> Startup()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      var result = await radioSource.StartupAsync();
      return Ok(result);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error starting radio receiver");
      return StatusCode(500, new { error = "Failed to start radio receiver" });
    }
  }

  /// <summary>
  /// Shuts down the radio receiver.
  /// </summary>
  /// <returns>No content on success.</returns>
  /// <response code="204">If the radio shut down successfully.</response>
  /// <response code="400">If the radio is not active.</response>
  [HttpPost("shutdown")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult> Shutdown()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.ShutdownAsync();
      return NoContent();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error shutting down radio receiver");
      return StatusCode(500, new { error = "Failed to shut down radio receiver" });
    }
  }

  /// <summary>
  /// Gets the active radio source from the audio engine.
  /// </summary>
  /// <returns>The active radio source, or null if no radio is active.</returns>
  private IRadioControl? GetActiveRadioSource()
  {
    return _audioEngine.GetActiveRadioSource();
  }

  /// <summary>
  /// Maps an IRadioControl instance to a RadioStateDto.
  /// </summary>
  /// <param name="radioSource">The radio source.</param>
  /// <returns>The radio state DTO.</returns>
  private static RadioStateDto MapToRadioStateDto(IRadioControl radioSource)
  {
    return new RadioStateDto
    {
      Frequency = radioSource.CurrentFrequency.Hertz, // Convert to Hz for API
      Band = radioSource.CurrentBand.ToString(),
      FrequencyStep = radioSource.FrequencyStep.Hertz, // Convert to Hz for API
      SignalStrength = radioSource.SignalStrength,
      IsStereo = radioSource.IsStereo,
      EqualizerMode = radioSource.EqualizerMode.ToString(),
      DeviceVolume = radioSource.DeviceVolume,
      IsScanning = radioSource.IsScanning,
      ScanDirection = radioSource.ScanDirection?.ToString(),
      AutoGainEnabled = radioSource.AutoGainEnabled,
      Gain = radioSource.Gain,
      IsRunning = radioSource.IsRunning
    };
  }

  // ===== RADIO PRESET ENDPOINTS =====

  /// <summary>
  /// Gets all saved radio presets.
  /// </summary>
  /// <returns>List of all radio presets.</returns>
  /// <response code="200">Returns the list of presets.</response>
  [HttpGet("presets")]
  [ProducesResponseType(typeof(IEnumerable<RadioPresetDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<IEnumerable<RadioPresetDto>>> GetPresets()
  {
    try
    {
      var presets = await _presetService.GetAllPresetsAsync();
      return Ok(presets.Select(RadioPresetDto.FromModel));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting radio presets");
      return StatusCode(500, new { error = "Failed to get radio presets" });
    }
  }

  /// <summary>
  /// Adds a new radio preset.
  /// </summary>
  /// <param name="request">The preset to create.</param>
  /// <returns>The created preset.</returns>
  /// <response code="201">Returns the created preset.</response>
  /// <response code="400">If the preset already exists or validation fails.</response>
  /// <response code="409">If a preset with the same band/frequency already exists.</response>
  /// <response code="507">If the maximum number of presets has been reached.</response>
  [HttpPost("presets")]
  [ProducesResponseType(typeof(RadioPresetDto), StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  [ProducesResponseType(StatusCodes.Status507InsufficientStorage)]
  public async Task<ActionResult<RadioPresetDto>> CreatePreset([FromBody] CreateRadioPresetRequest request)
  {
    try
    {
      // Validate band enum
      if (!Enum.TryParse<RadioBand>(request.Band, true, out var band))
      {
        return BadRequest(new { error = $"Invalid band: {request.Band}. Valid values are: {string.Join(", ", Enum.GetNames<RadioBand>())}" });
      }

      // Validate frequency range (basic validation, specific ranges are enforced by radio controls)
      if (request.Frequency <= 0)
      {
        return BadRequest(new { error = "Frequency must be greater than 0" });
      }

      var preset = await _presetService.AddPresetAsync(request.Name, band, request.Frequency);
      var dto = RadioPresetDto.FromModel(preset);

      return CreatedAtAction(nameof(GetPresets), new { id = preset.Id }, dto);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
    {
      _logger.LogWarning(ex, "Preset collision for {Band} - {Frequency}", request.Band, request.Frequency);
      return Conflict(new { error = ex.Message });
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Maximum"))
    {
      _logger.LogWarning(ex, "Maximum preset limit reached");
      return StatusCode(507, new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error creating radio preset");
      return StatusCode(500, new { error = "Failed to create radio preset" });
    }
  }

  /// <summary>
  /// Deletes a radio preset by ID.
  /// </summary>
  /// <param name="id">The preset ID to delete.</param>
  /// <returns>No content if successful.</returns>
  /// <response code="204">If the preset was deleted successfully.</response>
  /// <response code="404">If the preset was not found.</response>
  [HttpDelete("presets/{id}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult> DeletePreset(string id)
  {
    try
    {
      var deleted = await _presetService.DeletePresetAsync(id);
      if (!deleted)
      {
        return NotFound(new { error = $"Preset with ID '{id}' not found" });
      }

      return NoContent();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error deleting radio preset {Id}", id);
      return StatusCode(500, new { error = "Failed to delete radio preset" });
    }
  }

  #region Device Factory Endpoints

  /// <summary>
  /// Gets the list of available radio device types.
  /// </summary>
  /// <returns>List of available device types with their capabilities.</returns>
  /// <response code="200">Returns the list of available radio device types.</response>
  [HttpGet("devices")]
  [ProducesResponseType(typeof(RadioDeviceListDto), StatusCodes.Status200OK)]
  public ActionResult<RadioDeviceListDto> GetAvailableDevices()
  {
    try
    {
      var availableDevices = _radioFactory.GetAvailableDeviceTypes().ToList();
      var deviceList = availableDevices.Select(deviceType => new RadioDeviceInfoDto
      {
        DeviceType = deviceType,
        IsAvailable = true,
        Capabilities = GetDeviceCapabilities(deviceType)
      }).ToList();

      _logger.LogInformation("Retrieved {Count} available radio devices", deviceList.Count);

      return Ok(new RadioDeviceListDto
      {
        Devices = deviceList,
        Count = deviceList.Count
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error retrieving available radio devices");
      return StatusCode(500, new { error = "Failed to retrieve available radio devices" });
    }
  }

  /// <summary>
  /// Gets the default radio device type from configuration.
  /// </summary>
  /// <returns>The default device type.</returns>
  /// <response code="200">Returns the default device type.</response>
  /// <response code="500">If no devices are available or configuration is invalid.</response>
  [HttpGet("devices/default")]
  [ProducesResponseType(typeof(RadioDeviceInfoDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public ActionResult<RadioDeviceInfoDto> GetDefaultDevice()
  {
    try
    {
      var defaultDeviceType = _radioFactory.GetDefaultDeviceType();
      var isAvailable = _radioFactory.IsDeviceAvailable(defaultDeviceType);

      var deviceInfo = new RadioDeviceInfoDto
      {
        DeviceType = defaultDeviceType,
        IsAvailable = isAvailable,
        Capabilities = GetDeviceCapabilities(defaultDeviceType)
      };

      _logger.LogInformation("Default radio device: {DeviceType}", defaultDeviceType);
      return Ok(deviceInfo);
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "No radio devices are available");
      return StatusCode(500, new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error retrieving default radio device");
      return StatusCode(500, new { error = "Failed to retrieve default radio device" });
    }
  }

  /// <summary>
  /// Gets the currently active radio device type.
  /// </summary>
  /// <returns>The currently active device type.</returns>
  /// <response code="200">Returns the currently active device type.</response>
  /// <response code="400">If no radio source is active.</response>
  [HttpGet("devices/current")]
  [ProducesResponseType(typeof(RadioDeviceInfoDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult<RadioDeviceInfoDto> GetCurrentDevice()
  {
    try
    {
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "No radio source is currently active" });
      }

      // Determine device type from the source
      string deviceType;
      if (radioSource is SDRRadioAudioSource)
      {
        deviceType = "RTLSDRCore";
      }
      else if (radioSource is RadioAudioSource)
      {
        deviceType = "RF320";
      }
      else
      {
        deviceType = "Unknown";
      }

      var deviceInfo = new RadioDeviceInfoDto
      {
        DeviceType = deviceType,
        IsAvailable = true,
        IsActive = true,
        Capabilities = GetDeviceCapabilities(deviceType)
      };

      _logger.LogInformation("Currently active radio device: {DeviceType}", deviceType);
      return Ok(deviceInfo);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error retrieving current radio device");
      return StatusCode(500, new { error = "Failed to retrieve current radio device" });
    }
  }

  /// <summary>
  /// Selects and activates a specific radio device type.
  /// This will stop the current radio source (if any) and create a new one.
  /// </summary>
  /// <param name="request">Device selection request.</param>
  /// <returns>Information about the newly selected device.</returns>
  /// <response code="200">Device successfully selected.</response>
  /// <response code="400">If the device type is invalid or unavailable.</response>
  /// <response code="500">If device selection fails.</response>
  [HttpPost("devices/select")]
  [ProducesResponseType(typeof(RadioDeviceInfoDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<RadioDeviceInfoDto>> SelectDevice([FromBody] SelectRadioDeviceRequest request)
  {
    if (string.IsNullOrWhiteSpace(request?.DeviceType))
    {
      return BadRequest(new { error = "Device type is required" });
    }

    try
    {
      // Check if device is available
      if (!_radioFactory.IsDeviceAvailable(request.DeviceType))
      {
        return BadRequest(new { error = $"Device type '{request.DeviceType}' is not available" });
      }

      // Note: Actual device switching would require AudioEngine/AudioManager support
      // For now, we validate the request and return success
      // TODO: Integrate with AudioEngine.SwitchSourceAsync or similar method

      _logger.LogInformation("Device selection requested: {DeviceType}", request.DeviceType);

      var deviceInfo = new RadioDeviceInfoDto
      {
        DeviceType = request.DeviceType,
        IsAvailable = true,
        IsActive = false, // Not yet active until AudioEngine switches
        Capabilities = GetDeviceCapabilities(request.DeviceType)
      };

      return Ok(deviceInfo);
    }
    catch (ArgumentException ex)
    {
      _logger.LogWarning(ex, "Invalid device type: {DeviceType}", request.DeviceType);
      return BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error selecting radio device: {DeviceType}", request.DeviceType);
      return StatusCode(500, new { error = "Failed to select radio device" });
    }
  }

  #endregion

  #region Helper Methods

  /// <summary>
  /// Gets the capabilities for a specific radio device type.
  /// </summary>
  private RadioDeviceCapabilitiesDto GetDeviceCapabilities(string deviceType)
  {
    return deviceType switch
    {
      "RTLSDRCore" => new RadioDeviceCapabilitiesDto
      {
        SupportsSoftwareControl = true,
        SupportsFrequencyControl = true,
        SupportsBandSwitching = true,
        SupportsScanning = true,
        SupportsGainControl = true,
        SupportsEqualizer = false, // SDR doesn't have hardware EQ
        SupportsDeviceVolume = true,
        Description = "RTL-SDR Software Defined Radio - Full software control via USB dongle"
      },
      "RF320" => new RadioDeviceCapabilitiesDto
      {
        SupportsSoftwareControl = false,
        SupportsFrequencyControl = false,
        SupportsBandSwitching = false,
        SupportsScanning = false,
        SupportsGainControl = false,
        SupportsEqualizer = true, // Device has hardware EQ
        SupportsDeviceVolume = true, // Device has hardware volume
        Description = "Raddy RF320 Bluetooth Radio - Bluetooth control with USB audio output"
      },
      _ => new RadioDeviceCapabilitiesDto
      {
        Description = "Unknown device type"
      }
    };
  }

  #endregion
}
