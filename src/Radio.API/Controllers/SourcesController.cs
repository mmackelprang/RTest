using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

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
  private readonly ITTSFactory? _ttsFactory;
  private readonly AudioFileEventSourceFactory? _audioFileFactory;
  private readonly IDuckingService? _duckingService;

  /// <summary>
  /// Initializes a new instance of the SourcesController.
  /// </summary>
  public SourcesController(
    ILogger<SourcesController> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null,
    ITTSFactory? ttsFactory = null,
    AudioFileEventSourceFactory? audioFileFactory = null,
    IDuckingService? duckingService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _ttsFactory = ttsFactory;
    _audioFileFactory = audioFileFactory;
    _duckingService = duckingService;
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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

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
        ActiveSources = activeSources.Select(s => s.MapToDto()).ToList()
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

      return Ok(activeSources.Select(s => s.MapToDto()).ToList());
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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource == null)
      {
        return NotFound(new { error = "No primary source active" });
      }

      return Ok(primarySource.MapToDto());
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

      // If source not found in mixer, try to create it via AudioManager
      if (targetSource == null)
      {
        _logger.LogInformation("Source {SourceType} not in mixer, attempting to create", sourceType);

        // Use AudioManager to create the source
        if (_audioManager is Radio.Infrastructure.Audio.Services.AudioManager audioManager)
        {
          try
          {
            targetSource = await audioManager.GetOrCreateSourceAsync(sourceType);
          }
          catch (NotSupportedException ex)
          {
            _logger.LogWarning(ex, "Source type {SourceType} not supported", sourceType);
            return StatusCode(501, new
            {
              message = $"Source type {sourceType} is not yet implemented",
              supportedSources = new[] { "Radio" }
            });
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Failed to create source: {SourceType}", sourceType);
            return StatusCode(500, new
            {
              error = $"Failed to create source type {sourceType}",
              details = ex.Message
            });
          }
        }
        else
        {
          return BadRequest(new
          {
            error = $"Source type {sourceType} is not available or not configured",
            availableSources = activeSources.Select(s => s.Type.ToString()).ToList()
          });
        }
      }

      // Switch to the requested source
      try
      {
        await _audioManager.SwitchSourceAsync(targetSource);

        _logger.LogInformation(
          "Successfully switched to source: {SourceType}",
          sourceType);

        return Ok(targetSource.MapToDto());
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
        .Select(s => s.MapToDto())
        .ToList();

      return Ok(eventSources);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting event sources");
      return StatusCode(500, new { error = "Failed to get event sources" });
    }
  }

  /// <summary>
  /// Gets available TTS engines.
  /// </summary>
  /// <returns>List of available TTS engines.</returns>
  [HttpGet("events/tts/engines")]
  [ProducesResponseType(typeof(List<TTSEngineInfoDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public ActionResult<List<TTSEngineInfoDto>> GetTTSEngines()
  {
    if (_ttsFactory == null)
    {
      return StatusCode(501, new { error = "TTS service not available" });
    }

    var engines = _ttsFactory.AvailableEngines.Select(e => new TTSEngineInfoDto
    {
      Engine = e.Engine.ToString(),
      Name = e.Name,
      IsAvailable = e.IsAvailable,
      RequiresApiKey = e.RequiresApiKey,
      IsOffline = e.IsOffline
    }).ToList();

    return Ok(engines);
  }

  /// <summary>
  /// Gets available notification sounds.
  /// </summary>
  /// <param name="subdirectory">Optional subdirectory to search in.</param>
  /// <returns>List of available audio file paths.</returns>
  [HttpGet("events/sounds")]
  [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public ActionResult<List<string>> GetNotificationSounds([FromQuery] string? subdirectory = null)
  {
    if (_audioFileFactory == null)
    {
      return StatusCode(501, new { error = "Audio file service not available" });
    }

    var sounds = _audioFileFactory.GetAvailableNotificationSounds(subdirectory);
    return Ok(sounds.ToList());
  }

  /// <summary>
  /// Plays a TTS event.
  /// </summary>
  /// <param name="request">The TTS request.</param>
  /// <returns>The created event source info.</returns>
  [HttpPost("events/tts")]
  [ProducesResponseType(typeof(AudioSourceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult<AudioSourceDto>> PlayTTSEvent([FromBody] PlayTTSRequest request)
  {
    if (_ttsFactory == null || _duckingService == null)
    {
      return StatusCode(501, new { error = "TTS service not available" });
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
      return BadRequest(new { error = "Text is required" });
    }

    try
    {
      // Parse engine
      TTSEngine engine = TTSEngine.ESpeak;
      if (!string.IsNullOrEmpty(request.Engine) && Enum.TryParse<TTSEngine>(request.Engine, true, out var parsed))
      {
        engine = parsed;
      }

      var parameters = new TTSParameters
      {
        Engine = engine,
        Voice = request.Voice ?? "en",
        Speed = request.Speed ?? 1.0f,
        Pitch = request.Pitch ?? 1.0f
      };

      _logger.LogInformation("Creating TTS event: {Text} (engine={Engine})", request.Text, engine);

      var eventSource = await _ttsFactory.CreateAsync(request.Text, parameters);

      // Add to mixer and start ducking
      var mixer = _audioEngine.GetMasterMixer();
      mixer.AddSource(eventSource);
      await _duckingService.StartDuckingAsync(eventSource);

      // Set up cleanup when event completes
      eventSource.PlaybackCompleted += async (_, _) =>
      {
        await _duckingService.StopDuckingAsync(eventSource);
        mixer.RemoveSource(eventSource);
        _logger.LogInformation("TTS event completed and cleaned up");
      };

      // Start playback (fire-and-forget, cleanup handled by PlaybackCompleted)
      _ = eventSource.PlayAsync();

      return Ok(eventSource.MapToDto());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error playing TTS event");
      return StatusCode(500, new { error = "Failed to play TTS event", details = ex.Message });
    }
  }

  /// <summary>
  /// Plays an audio file event.
  /// </summary>
  /// <param name="request">The audio file request.</param>
  /// <returns>The created event source info.</returns>
  [HttpPost("events/file")]
  [ProducesResponseType(typeof(AudioSourceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult<AudioSourceDto>> PlayFileEvent([FromBody] PlayFileEventRequest request)
  {
    if (_audioFileFactory == null || _duckingService == null)
    {
      return StatusCode(501, new { error = "Audio file service not available" });
    }

    if (string.IsNullOrWhiteSpace(request.FilePath))
    {
      return BadRequest(new { error = "FilePath is required" });
    }

    try
    {
      _logger.LogInformation("Creating audio file event: {FilePath}", request.FilePath);

      var eventSource = await _audioFileFactory.CreateFromFileAsync(request.FilePath);

      // Add to mixer and start ducking
      var mixer = _audioEngine.GetMasterMixer();
      mixer.AddSource(eventSource);
      await _duckingService.StartDuckingAsync(eventSource);

      // Set up cleanup when event completes
      eventSource.PlaybackCompleted += async (_, _) =>
      {
        await _duckingService.StopDuckingAsync(eventSource);
        mixer.RemoveSource(eventSource);
        _logger.LogInformation("Audio file event completed and cleaned up");
      };

      // Start playback (fire-and-forget, cleanup handled by PlaybackCompleted)
      _ = eventSource.PlayAsync();

      return Ok(eventSource.MapToDto());
    }
    catch (FileNotFoundException ex)
    {
      return BadRequest(new { error = "File not found", details = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error playing audio file event");
      return StatusCode(500, new { error = "Failed to play audio file event", details = ex.Message });
    }
  }
}
