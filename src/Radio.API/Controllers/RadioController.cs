using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

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

  /// <summary>
  /// Initializes a new instance of the RadioController.
  /// </summary>
  public RadioController(
    ILogger<RadioController> logger,
    IAudioEngine audioEngine,
    IRadioPresetService presetService)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _presetService = presetService;
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
      // Note: Frequency validation is delegated to IRadioControls.SetFrequencyAsync
      // which throws ArgumentOutOfRangeException for invalid values
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.SetFrequencyAsync(request.Frequency);
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
      // Note: Step validation is delegated to IRadioControls.SetFrequencyStepAsync
      // which throws ArgumentOutOfRangeException for invalid values
      var radioSource = GetActiveRadioSource();
      if (radioSource == null)
      {
        return BadRequest(new { error = "Radio is not the active source" });
      }

      await radioSource.SetFrequencyStepAsync(request.Step);
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

      // Note: DeviceVolume is a synchronous property per IRadioControls interface design
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
  /// Gets the active radio source from the audio engine.
  /// </summary>
  /// <returns>The active radio source, or null if no radio is active.</returns>
  private IRadioControls? GetActiveRadioSource()
  {
    return _audioEngine.GetActiveRadioSource();
  }

  /// <summary>
  /// Maps an IRadioControls instance to a RadioStateDto.
  /// </summary>
  /// <param name="radioSource">The radio source.</param>
  /// <returns>The radio state DTO.</returns>
  private static RadioStateDto MapToRadioStateDto(IRadioControls radioSource)
  {
    return new RadioStateDto
    {
      Frequency = radioSource.CurrentFrequency,
      Band = radioSource.CurrentBand.ToString(),
      FrequencyStep = radioSource.FrequencyStep,
      SignalStrength = radioSource.SignalStrength,
      IsStereo = radioSource.IsStereo,
      EqualizerMode = radioSource.EqualizerMode.ToString(),
      DeviceVolume = radioSource.DeviceVolume,
      IsScanning = radioSource.IsScanning,
      ScanDirection = radioSource.ScanDirection?.ToString()
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
}
