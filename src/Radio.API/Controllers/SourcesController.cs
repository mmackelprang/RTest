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
  private readonly AudioFileEventSourceFactory? _fileEventFactory;
  private readonly IDuckingService? _duckingService;

  /// <summary>
  /// Initializes a new instance of the SourcesController.
  /// </summary>
  public SourcesController(
    ILogger<SourcesController> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null,
    ITTSFactory? ttsFactory = null,
    AudioFileEventSourceFactory? fileEventFactory = null,
    IDuckingService? duckingService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _ttsFactory = ttsFactory;
    _fileEventFactory = fileEventFactory;
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
  /// <returns>List of TTS engine information.</returns>
  [HttpGet("events/tts/engines")]
  [ProducesResponseType(typeof(List<TTSEngineInfoDto>), StatusCodes.Status200OK)]
  public ActionResult<List<TTSEngineInfoDto>> GetTTSEngines()
  {
    try
    {
      if (_ttsFactory == null)
      {
        return StatusCode(501, new { error = "TTS factory not available" });
      }

      var engines = _ttsFactory.AvailableEngines
        .Select(e => new TTSEngineInfoDto
        {
          Engine = e.Engine.ToString(),
          Name = e.Name,
          IsAvailable = e.IsAvailable,
          RequiresApiKey = e.RequiresApiKey,
          IsOffline = e.IsOffline
        })
        .ToList();

      return Ok(engines);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting TTS engines");
      return StatusCode(500, new { error = "Failed to get TTS engines" });
    }
  }

  /// <summary>
  /// Gets available voices for a specific TTS engine.
  /// </summary>
  /// <param name="engine">The TTS engine to query (ESpeak, Google, Azure).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>List of available voices for the engine.</returns>
  [HttpGet("events/tts/voices")]
  [ProducesResponseType(typeof(List<TTSVoiceInfoDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<TTSVoiceInfoDto>>> GetTTSVoices(
    [FromQuery] string engine,
    CancellationToken cancellationToken)
  {
    try
    {
      if (_ttsFactory == null)
      {
        return StatusCode(501, new { error = "TTS factory not available" });
      }

      if (string.IsNullOrWhiteSpace(engine))
      {
        return BadRequest(new { error = "Engine parameter is required" });
      }

      // Parse engine parameter
      if (!Enum.TryParse<TTSEngine>(engine, ignoreCase: true, out var ttsEngine))
      {
        return BadRequest(new { error = $"Invalid engine: {engine}. Valid values are: ESpeak, Google, Azure" });
      }

      var voices = await _ttsFactory.GetVoicesAsync(ttsEngine, cancellationToken);
      var voiceDtos = voices.Select(v => new TTSVoiceInfoDto
      {
        Id = v.Id,
        Name = v.Name,
        Language = v.Language,
        Gender = v.Gender.ToString()
      }).ToList();

      return Ok(voiceDtos);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting TTS voices for engine {Engine}", engine);
      return StatusCode(500, new { error = "Failed to get TTS voices" });
    }
  }

  /// <summary>
  /// Gets available notification sound files.
  /// </summary>
  /// <param name="subdirectory">Optional subdirectory to search in.</param>
  /// <returns>List of notification sound files.</returns>
  [HttpGet("events/sounds")]
  [ProducesResponseType(typeof(List<NotificationSoundDto>), StatusCodes.Status200OK)]
  public ActionResult<List<NotificationSoundDto>> GetNotificationSounds([FromQuery] string? subdirectory = null)
  {
    try
    {
      if (_fileEventFactory == null)
      {
        return StatusCode(501, new { error = "File event factory not available" });
      }

      var files = _fileEventFactory.GetAvailableNotificationSounds(subdirectory);
      var sounds = files.Select(filePath =>
      {
        var fileInfo = new FileInfo(filePath);
        return new NotificationSoundDto
        {
          FileName = fileInfo.Name,
          FilePath = filePath,
          FileSize = fileInfo.Length
        };
      }).ToList();

      return Ok(sounds);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting notification sounds");
      return StatusCode(500, new { error = "Failed to get notification sounds" });
    }
  }

  /// <summary>
  /// Plays a TTS event.
  /// </summary>
  /// <param name="request">The TTS playback request.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Success response if playback started.</returns>
  [HttpPost("events/tts")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult> PlayTTSEvent([FromBody] PlayTTSRequest request, CancellationToken cancellationToken)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.Text))
      {
        return BadRequest(new { error = "Text is required" });
      }

      if (_ttsFactory == null)
      {
        return StatusCode(501, new { error = "TTS playback not available" });
      }

      // Parse engine if provided
      TTSEngine? engine = null;
      if (!string.IsNullOrWhiteSpace(request.Engine))
      {
        if (Enum.TryParse<TTSEngine>(request.Engine, ignoreCase: true, out var parsedEngine))
        {
          engine = parsedEngine;
        }
        else
        {
          return BadRequest(new { error = $"Invalid TTS engine: {request.Engine}" });
        }
      }

      // Build TTS parameters
      var parameters = new TTSParameters
      {
        Engine = engine ?? TTSEngine.ESpeak,
        Voice = request.Voice ?? "en",
        Speed = request.Speed ?? 1.0f,
        Pitch = request.Pitch ?? 1.0f
      };

      _logger.LogInformation("Playing TTS event: {Text} with engine {Engine}", 
        request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text, 
        parameters.Engine);

      // Create TTS event source
      var ttsSource = await _ttsFactory.CreateAsync(request.Text, parameters, cancellationToken);

      // Add the event source to the mixer and play it
      var mixer = _audioEngine.GetMasterMixer();
      mixer.AddSource(ttsSource);
      
      // Start playback
      await ttsSource.PlayAsync(cancellationToken);

      return Ok(new { message = "TTS event started successfully" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to play TTS event");
      return StatusCode(500, new { error = "Failed to play TTS event", details = ex.Message });
    }
  }

  /// <summary>
  /// Plays an audio file event.
  /// </summary>
  /// <param name="request">The file playback request.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Success response if playback started.</returns>
  [HttpPost("events/file")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status501NotImplemented)]
  public async Task<ActionResult> PlayFileEvent([FromBody] PlayFileEventRequest request, CancellationToken cancellationToken)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.FilePath))
      {
        return BadRequest(new { error = "FilePath is required" });
      }

      if (_fileEventFactory == null)
      {
        return StatusCode(501, new { error = "File event playback not available" });
      }

      _logger.LogInformation("Playing file event: {FilePath}", request.FilePath);

      // Create file event source
      var fileSource = await _fileEventFactory.CreateFromFileAsync(request.FilePath, cancellationToken);

      // Add the event source to the mixer and play it
      var mixer = _audioEngine.GetMasterMixer();
      mixer.AddSource(fileSource);
      
      // Start playback
      await fileSource.PlayAsync(cancellationToken);

      return Ok(new { message = "File event started successfully" });
    }
    catch (FileNotFoundException ex)
    {
      _logger.LogWarning(ex, "File not found: {FilePath}", request.FilePath);
      return NotFound(new { error = "File not found", filePath = request.FilePath });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to play file event");
      return StatusCode(500, new { error = "Failed to play file event", details = ex.Message });
    }
  }
}
