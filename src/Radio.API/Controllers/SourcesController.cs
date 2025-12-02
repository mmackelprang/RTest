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

  /// <summary>
  /// Initializes a new instance of the SourcesController.
  /// </summary>
  public SourcesController(
    ILogger<SourcesController> logger,
    IAudioEngine audioEngine)
  {
    _logger = logger;
    _audioEngine = audioEngine;
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
  /// <remarks>
  /// Note: Full source switching requires AudioManager implementation.
  /// This endpoint validates the request and logs the selection.
  /// </remarks>
  [HttpPost]
  [ProducesResponseType(typeof(AudioSourceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public ActionResult<AudioSourceDto> SelectSource([FromBody] SelectSourceRequest request)
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

      // Note: Full implementation requires IAudioManager to switch sources
      // For now, we return 501 Not Implemented to indicate this requires Phase 3 completion
      _logger.LogInformation("Source selection requested: {SourceType}", sourceType);

      return StatusCode(501, new
      {
        message = "Source switching not yet implemented",
        requestedSource = sourceType.ToString(),
        note = "This requires Primary Audio Sources (Phase 3) to be completed"
      });
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
