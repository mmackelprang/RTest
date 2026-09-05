using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.API.Extensions;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;

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
  private readonly IBluetoothService? _bluetoothService;
  private readonly IOptionsMonitor<BluetoothOptions>? _bluetoothOptions;
  private readonly IOptionsMonitor<AudioPreferences>? _audioPreferencesOptions;
  private readonly ITTSFactory? _ttsFactory;
  private readonly AudioFileEventSourceFactory? _fileEventFactory;
  private readonly IDuckingService? _duckingService;
  private readonly SoundFlowPlaybackService? _playbackService;

  /// <summary>
  /// Initializes a new instance of the SourcesController.
  /// </summary>
  public SourcesController(
    ILogger<SourcesController> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null,
    IBluetoothService? bluetoothService = null,
    IOptionsMonitor<BluetoothOptions>? bluetoothOptions = null,
    IOptionsMonitor<AudioPreferences>? audioPreferencesOptions = null,
    ITTSFactory? ttsFactory = null,
    AudioFileEventSourceFactory? fileEventFactory = null,
    IDuckingService? duckingService = null,
    SoundFlowPlaybackService? playbackService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _bluetoothService = bluetoothService;
    _bluetoothOptions = bluetoothOptions;
    _audioPreferencesOptions = audioPreferencesOptions;
    _ttsFactory = ttsFactory;
    _fileEventFactory = fileEventFactory;
    _duckingService = duckingService;
    _playbackService = playbackService;
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
      // Prefer AudioManager's tracked active source
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      var activePrimary = primarySource as IPrimaryAudioSource;
      var bluetoothMetadata = activePrimary?.Metadata;
      var bluetoothDevice = bluetoothMetadata != null && bluetoothMetadata.TryGetValue("Device", out var device)
        ? new BluetoothDeviceDto
        {
          Name = device?.ToString() ?? "Bluetooth Device",
          Address = bluetoothMetadata.TryGetValue("DeviceAddress", out var addr) ? addr?.ToString() ?? string.Empty : string.Empty,
          IsConnected = true,
          IsPaired = true
        }
        : null;

      var allSources = new List<string>
      {
        AudioSourceType.Radio.ToString(),
        AudioSourceType.Vinyl.ToString(),
        AudioSourceType.FilePlayer.ToString(),
        AudioSourceType.GenericUSB.ToString(),
        AudioSourceType.Bluetooth.ToString(),
        AudioSourceType.TestTone.ToString()
      };

      // Filter out hidden sources from the UI source bar
      var hiddenSources = _audioPreferencesOptions?.CurrentValue.HiddenSources ?? new List<string>();
      var visibleSources = allSources
        .Where(s => !hiddenSources.Contains(s, StringComparer.OrdinalIgnoreCase))
        .ToList();

      var result = new AvailableSourcesDto
      {
        PrimarySources = visibleSources,
        ActiveSourceType = primarySource?.Type.ToString(),
        ActiveSources = activeSources.Select(s => s.MapToDto()).ToList(),
        BluetoothDevices = bluetoothDevice != null ? new List<BluetoothDeviceDto> { bluetoothDevice } : new List<BluetoothDeviceDto>(),
        DiscoveredBluetoothDevices = _bluetoothService?.DiscoveredDevices?.Select(d => new BluetoothDeviceDto
        {
          Name = d.Name,
          Address = d.Address,
          IsConnected = d.IsConnected,
          IsPaired = d.IsPaired,
          LastConnected = d.LastConnected
        }).ToList() ?? new List<BluetoothDeviceDto>(),
        Bluetooth = _bluetoothOptions != null ? new BluetoothSourceInfoDto
        {
          IsDiscovering = _bluetoothService?.IsDiscovering ?? false,
          AutoSwitchOnConnect = _bluetoothOptions.CurrentValue.AutoSwitchOnConnect,
          RequirePairing = _bluetoothOptions.CurrentValue.RequirePairing,
          DeviceName = _bluetoothOptions.CurrentValue.DeviceName,
          AudioQuality = _bluetoothOptions.CurrentValue.AudioQuality.ToString()
        } : null
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
      // Prefer AudioManager's tracked active source over the extension method
      // The extension method just returns the first primary source in the mixer,
      // which may not be the source the user actually switched to
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

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
            targetSource = await audioManager.GetOrCreateSourceAsync(sourceType, switchToSource: false);
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

      // Null check for targetSource
      if (targetSource == null)
      {
        return StatusCode(500, new
        {
          error = $"Failed to create source type {sourceType}",
          details = "Source creation returned null"
        });
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
  /// <param name="engine">The TTS engine to query (Google, Azure).</param>
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

      // Parse engine parameter. Enum.TryParse also accepts the decimal form of a value, so "0"
      // or "7" parse into an undefined member — Enum.IsDefined is what rejects those.
      if (!Enum.TryParse<TTSEngine>(engine, ignoreCase: true, out var ttsEngine) ||
          !Enum.IsDefined(ttsEngine))
      {
        return BadRequest(new { error = $"Invalid engine: {engine}. Valid values are: Google, Azure" });
      }

      var voices = await _ttsFactory.GetVoicesAsync(ttsEngine, cancellationToken);
      var voiceDtos = voices.Select(v => new TTSVoiceInfoDto
      {
        Id = v.Id,
        Name = v.Name,
        Language = v.Language,
        Gender = v.Gender.ToString(),
        IsFavorite = v.IsFavorite,
        PriceTier = v.PriceTier
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
  /// Refreshes the voice cache for a TTS engine by fetching from the cloud API.
  /// </summary>
  [HttpPost("events/tts/voices/refresh")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  public async Task<IActionResult> RefreshTTSVoices(
    [FromQuery] string engine,
    CancellationToken cancellationToken)
  {
    try
    {
      if (_ttsFactory == null)
      {
        return StatusCode(501, new { error = "TTS factory not available" });
      }

      if (!Enum.TryParse<TTSEngine>(engine, ignoreCase: true, out var ttsEngine) ||
          !Enum.IsDefined(ttsEngine))
      {
        return BadRequest(new { error = $"Invalid engine: {engine}. Valid values are: Google, Azure" });
      }

      var count = await _ttsFactory.RefreshVoicesAsync(ttsEngine, cancellationToken);
      return Ok(new { count });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error refreshing TTS voices for engine {Engine}", engine);
      return StatusCode(500, new { error = ex.Message });
    }
  }

  /// <summary>
  /// Marks a voice as a favorite.
  /// </summary>
  [HttpPost("events/tts/voices/favorite")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<IActionResult> SetTTSVoiceFavorite(
    [FromQuery] string engine,
    [FromQuery] string voiceId,
    CancellationToken cancellationToken)
  {
    try
    {
      if (_ttsFactory == null)
      {
        return StatusCode(501, new { error = "TTS factory not available" });
      }

      if (!Enum.TryParse<TTSEngine>(engine, ignoreCase: true, out var ttsEngine) ||
          !Enum.IsDefined(ttsEngine))
      {
        return BadRequest(new { error = $"Invalid engine: {engine}. Valid values are: Google, Azure" });
      }

      await _ttsFactory.SetVoiceFavoriteAsync(ttsEngine, voiceId, cancellationToken);
      return Ok();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting TTS voice favorite {VoiceId} for {Engine}", voiceId, engine);
      return StatusCode(500, new { error = ex.Message });
    }
  }

  /// <summary>
  /// Removes a voice from favorites.
  /// </summary>
  [HttpDelete("events/tts/voices/favorite")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<IActionResult> RemoveTTSVoiceFavorite(
    [FromQuery] string engine,
    [FromQuery] string voiceId,
    CancellationToken cancellationToken)
  {
    try
    {
      if (_ttsFactory == null)
      {
        return StatusCode(501, new { error = "TTS factory not available" });
      }

      if (!Enum.TryParse<TTSEngine>(engine, ignoreCase: true, out var ttsEngine) ||
          !Enum.IsDefined(ttsEngine))
      {
        return BadRequest(new { error = $"Invalid engine: {engine}. Valid values are: Google, Azure" });
      }

      await _ttsFactory.RemoveVoiceFavoriteAsync(ttsEngine, voiceId, cancellationToken);
      return Ok();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error removing TTS voice favorite {VoiceId} for {Engine}", voiceId, engine);
      return StatusCode(500, new { error = ex.Message });
    }
  }

  /// <summary>
  /// Gets subdirectories in the notification sounds folder.
  /// </summary>
  /// <remarks>
  /// Returns a list of subdirectory names for folder-based navigation.
  /// Use this to browse different categories of sounds (e.g., "alarm", "notify", "ring").
  /// Pass the subdirectory name to subsequent calls to navigate deeper.
  /// </remarks>
  /// <param name="subdirectory">Optional current subdirectory path to list subdirectories from.</param>
  /// <returns>List of subdirectory names.</returns>
  [HttpGet("events/sounds/directories")]
  [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
  public ActionResult<List<string>> GetSoundDirectories([FromQuery] string? subdirectory = null)
  {
    try
    {
      if (_fileEventFactory == null)
      {
        return StatusCode(501, new { error = "File event factory not available" });
      }

      var directories = _fileEventFactory.GetSubdirectories(subdirectory);
      return Ok(directories.ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting sound directories");
      return StatusCode(500, new { error = "Failed to get sound directories" });
    }
  }

  /// <summary>
  /// Gets available notification sound files.
  /// </summary>
  /// <remarks>
  /// Returns a list of available audio files with their absolute file paths.
  /// The FilePath property can be passed directly to the POST /api/sources/events/file
  /// endpoint to play the sound.
  ///
  /// Supported formats: .mp3, .wav, .ogg, .flac
  /// </remarks>
  /// <param name="subdirectory">Optional subdirectory to search in (e.g., "alarm", "notify").</param>
  /// <returns>List of notification sound files with absolute paths.</returns>
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

      // Parse engine if provided. Enum.TryParse also accepts the decimal form of a value, so "0"
      // or "7" parse into an undefined member — Enum.IsDefined is what rejects those.
      TTSEngine? engine = null;
      if (!string.IsNullOrWhiteSpace(request.Engine))
      {
        if (Enum.TryParse<TTSEngine>(request.Engine, ignoreCase: true, out var parsedEngine) &&
            Enum.IsDefined(parsedEngine))
        {
          engine = parsedEngine;
        }
        else
        {
          return BadRequest(new { error = $"Invalid TTS engine: {request.Engine}. Valid values are: Google, Azure" });
        }
      }

      // Build TTS parameters. A null engine or voice is left unspecified here and resolved
      // against the configured TTS defaults inside the factory.
      var parameters = new TTSParameters
      {
        Engine = engine,
        Voice = request.Voice,
        Speed = request.Speed ?? 1.0f,
        Pitch = request.Pitch ?? 1.0f
      };

      _logger.LogInformation("Playing TTS event: {Text} with engine {Engine}",
        LogSafeText.For(request.Text),
        engine?.ToString() ?? "(configured default)");

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
  /// <remarks>
  /// **Important:** The FilePath must be an absolute path to the audio file.
  /// Use the GET /api/sources/events/sounds endpoint to retrieve available sounds
  /// with their absolute file paths, then pass the FilePath directly to this endpoint.
  ///
  /// Supported formats: .mp3, .wav, .ogg, .flac
  ///
  /// Example request:
  /// ```json
  /// {
  ///   "filePath": "D:/prj/RTest/RTest/src/Radio.API/media/audio/PD - Morse Code.mp3"
  /// }
  /// ```
  /// </remarks>
  /// <param name="request">The file playback request containing the absolute file path.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Success response with sourceId if playback started.</returns>
  [HttpPost("events/file")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
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

      if (_playbackService == null)
      {
        return StatusCode(501, new { error = "Playback service not available" });
      }

      _logger.LogInformation("Playing file event: {FilePath}", request.FilePath);

      // Create file event source for tracking
      var fileSource = await _fileEventFactory.CreateFromFileAsync(request.FilePath, cancellationToken);

      // Add the event source to the mixer for tracking
      var mixer = _audioEngine.GetMasterMixer();
      mixer.AddSource(fileSource);

      // Start actual audio playback through SoundFlow
      var success = await _playbackService.PlayFileAsync(
        fileSource.Id,
        request.FilePath,
        fileSource.Volume,
        cancellationToken);

      if (!success)
      {
        mixer.RemoveSource(fileSource);
        return StatusCode(500, new { error = "Failed to start audio playback" });
      }

      // Mark the source as playing
      await fileSource.PlayAsync(cancellationToken);

      return Ok(new { message = "File event started successfully", sourceId = fileSource.Id });
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
