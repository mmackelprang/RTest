using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for managing audio sources.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SourcesController : ControllerBase
{
  private readonly ILogger<SourcesController> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager? _audioManager;

  /// <summary>
  /// Initializes a new instance of the SourcesController.
  /// </summary>
  public SourcesController(
    ILogger<SourcesController> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
  }

  /// <summary>
  /// Gets the available and active audio sources.
  /// </summary>
  /// <returns>The available sources information.</returns>
  [HttpGet]
  [ProducesResponseType(typeof(AvailableSourcesDto), StatusCodes.Status200OK)]
  public ActionResult<AvailableSourcesDto> GetSources()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();
      var primarySource = activeSources.FirstOrDefault(s => s.Category == AudioSourceCategory.Primary);

      var result = new AvailableSourcesDto
      {
        PrimarySources =
        [
          AudioSourceType.Spotify.ToString(),
          AudioSourceType.Radio.ToString(),
          AudioSourceType.Vinyl.ToString(),
          AudioSourceType.FilePlayer.ToString(),
          AudioSourceType.GenericUSB.ToString()
        ],
        ActiveSourceType = primarySource?.Type.ToString(),
        ActiveSources = activeSources.Select(MapToAudioSourceDto).ToList()
      };

      return Ok(result);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting sources");
      return StatusCode(500, new { error = "Failed to get sources" });
    }
  }

  /// <summary>
  /// Gets the currently active sources.
  /// </summary>
  /// <returns>List of active audio sources.</returns>
  [HttpGet("active")]
  [ProducesResponseType(typeof(List<AudioSourceDto>), StatusCodes.Status200OK)]
  public ActionResult<List<AudioSourceDto>> GetActiveSources()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();

      return Ok(activeSources.Select(MapToAudioSourceDto).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting active sources");
      return StatusCode(500, new { error = "Failed to get active sources" });
    }
  }

  /// <summary>
  /// Gets the current primary source.
  /// </summary>
  /// <returns>The active primary source, if any.</returns>
  [HttpGet("primary")]
  [ProducesResponseType(typeof(AudioSourceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public ActionResult<AudioSourceDto> GetPrimarySource()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();
      var primarySource = activeSources.FirstOrDefault(s => s.Category == AudioSourceCategory.Primary);

      if (primarySource == null)
      {
        return NotFound(new { error = "No primary source active" });
      }

      return Ok(MapToAudioSourceDto(primarySource));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting primary source");
      return StatusCode(500, new { error = "Failed to get primary source" });
    }
  }

  /// <summary>
  /// Selects a new primary audio source.
  /// </summary>
  /// <param name="request">The source selection request.</param>
  /// <returns>The selected source information.</returns>
  [HttpPost]
  [ProducesResponseType(typeof(AudioSourceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult<AudioSourceDto>> SelectSource([FromBody] SelectSourceRequest request)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.SourceType))
      {
        return BadRequest(new { error = "SourceType is required" });
      }

      // Validate source type
      if (!Enum.TryParse<AudioSourceType>(request.SourceType, true, out var sourceType))
      {
        return BadRequest(new { error = $"Invalid source type: {request.SourceType}" });
      }

      _logger.LogInformation("Source selection requested: {SourceType}", sourceType);

      // Check if audio manager is available
      if (_audioManager == null)
      {
        return StatusCode(501, new
        {
          message = "Source switching not yet implemented",
          requestedSource = sourceType.ToString(),
          note = "This requires IAudioManager implementation to be completed"
        });
      }

      // Get the mixer and find the requested source
      var mixer = _audioEngine.GetMasterMixer();
      var activeSources = mixer.GetActiveSources();
      
      // Look for an existing source of the requested type
      var targetSource = activeSources.FirstOrDefault(s => s.Type == sourceType);
      
      if (targetSource == null)
      {
        return BadRequest(new
        {
          error = $"Source type {sourceType} is not available or not configured",
          availableSources = activeSources.Select(s => s.Type.ToString()).ToList()
        });
      }

      // Switch to the requested source
      try
      {
        await _audioManager.SwitchSourceAsync(targetSource);
        
        _logger.LogInformation(
          "Successfully switched to source: {SourceType}",
          sourceType);

        return Ok(MapToAudioSourceDto(targetSource));
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to switch to source: {SourceType}", sourceType);
        return StatusCode(500, new
        {
          error = "Failed to switch audio source",
          details = ex.Message
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error selecting source");
      return StatusCode(500, new { error = "Failed to select source" });
    }
  }

  /// <summary>
  /// Gets event sources currently active (TTS, notifications, etc.).
  /// </summary>
  /// <returns>List of active event sources.</returns>
  [HttpGet("events")]
  [ProducesResponseType(typeof(List<AudioSourceDto>), StatusCodes.Status200OK)]
  public ActionResult<List<AudioSourceDto>> GetEventSources()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      var eventSources = mixer.GetActiveSources()
        .Where(s => s.Category == AudioSourceCategory.Event)
        .Select(MapToAudioSourceDto)
        .ToList();

      return Ok(eventSources);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting event sources");
      return StatusCode(500, new { error = "Failed to get event sources" });
    }
  }

  private static AudioSourceDto MapToAudioSourceDto(IAudioSource source)
  {
    var dto = new AudioSourceDto
    {
      Id = source.Id,
      Name = source.Name,
      Type = source.Type.ToString(),
      Category = source.Category.ToString(),
      State = source.State.ToString(),
      Volume = source.Volume
    };

    if (source is IPrimaryAudioSource primary)
    {
      dto.IsSeekable = primary.IsSeekable;
      dto.Metadata = primary.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    return dto;
  }
}
