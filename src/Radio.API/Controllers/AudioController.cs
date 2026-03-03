using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for audio playback control.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AudioController : ControllerBase
{
  private readonly ILogger<AudioController> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager? _audioManager;
  private readonly IDuckingService _duckingService;
  private readonly BackgroundIdentificationService? _fingerprintService;
  private readonly IVisualizerService? _visualizerService;
  private readonly SoundFlowPlaybackService? _playbackService;

  /// <summary>
  /// Initializes a new instance of the AudioController.
  /// </summary>
  public AudioController(
    ILogger<AudioController> logger,
    IAudioEngine audioEngine,
    IDuckingService duckingService,
    IAudioManager? audioManager = null,
    BackgroundIdentificationService? fingerprintService = null,
    IVisualizerService? visualizerService = null,
    SoundFlowPlaybackService? playbackService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _duckingService = duckingService;
    _fingerprintService = fingerprintService;
    _visualizerService = visualizerService;
    _playbackService = playbackService;
  }

  /// <summary>
  /// Gets the current playback state.
  /// </summary>
  /// <returns>The current playback state.</returns>
  [HttpGet]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  public ActionResult<PlaybackStateDto> GetPlaybackState()
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      // Prefer AudioManager's tracked active source over the extension method.
      // The extension method just returns the first primary source in the mixer,
      // which may not be the source the user actually switched to.
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      var state = new PlaybackStateDto
      {
        // IsPlaying should be true only when both engine is running AND source is in Playing state
        IsPlaying = _audioEngine.State == AudioEngineState.Running &&
                    primarySource?.State == AudioSourceState.Playing,
        IsPaused = primarySource?.State == AudioSourceState.Paused,
        Volume = mixer.MasterVolume,
        IsMuted = mixer.IsMuted,
        Balance = mixer.Balance,
        DuckingState = new DuckingStateDto
        {
          IsDucking = _duckingService.IsDucking,
          DuckLevel = _duckingService.CurrentDuckLevel,
          ActiveEventCount = _duckingService.ActiveEventCount
        }
      };

      if (primarySource != null)
      {
        state.ActiveSource = primarySource.MapToDto();

        if (primarySource is IPrimaryAudioSource primary)
        {
          state.Position = primary.Position;
          state.Duration = primary.Duration;
          
          // Playback control capabilities
          state.CanPlay = true; // All primary sources can play
          state.CanPause = true; // All primary sources can pause
          state.CanStop = true; // All primary sources can stop
          state.CanSeek = primary.IsSeekable;
          
          // Navigation and queue capabilities
          state.CanNext = primary.SupportsNext;
          state.CanPrevious = primary.SupportsPrevious;
          state.CanShuffle = primary.SupportsShuffle;
          state.CanRepeat = primary.SupportsRepeat;
          state.CanQueue = primary.SupportsQueue;
          
          // Current state
          state.IsShuffleEnabled = primary.IsShuffleEnabled;
          state.RepeatMode = primary.RepeatMode.ToString();
        }
      }

      return Ok(state);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting playback state");
      return StatusCode(500, new { error = "Failed to get playback state" });
    }
  }

  /// <summary>
  /// Updates the playback state (play, pause, stop, volume, etc.).
  /// </summary>
  /// <param name="request">The playback update request.</param>
  /// <returns>The updated playback state.</returns>
  [HttpPost]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlaybackStateDto>> UpdatePlaybackState([FromBody] UpdatePlaybackRequest request)
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();

      // Validate and update volume if specified (route through AudioManager for persistence)
      if (request.Volume.HasValue)
      {
        if (request.Volume.Value < 0f || request.Volume.Value > 1f)
        {
          return BadRequest(new { error = "Volume must be between 0 and 1" });
        }
        if (_audioManager != null)
          _audioManager.MasterVolume = request.Volume.Value;
        else
          mixer.MasterVolume = request.Volume.Value;
        _logger.LogInformation("Volume set to {Volume}", mixer.MasterVolume);
      }

      // Validate and update balance if specified
      if (request.Balance.HasValue)
      {
        if (request.Balance.Value < -1f || request.Balance.Value > 1f)
        {
          return BadRequest(new { error = "Balance must be between -1 and 1" });
        }
        if (_audioManager != null)
          _audioManager.Balance = request.Balance.Value;
        else
          mixer.Balance = request.Balance.Value;
        _logger.LogInformation("Balance set to {Balance}", mixer.Balance);
      }

      // Update mute state if specified
      if (request.IsMuted.HasValue)
      {
        if (_audioManager != null)
          _audioManager.IsMuted = request.IsMuted.Value;
        else
          mixer.IsMuted = request.IsMuted.Value;
        _logger.LogInformation("Mute set to {IsMuted}", mixer.IsMuted);
      }

      // Handle playback actions
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      switch (request.Action ?? PlaybackAction.None)
      {
        case PlaybackAction.Play:
          if (primarySource is IPrimaryAudioSource playSource)
          {
            if (playSource.State == AudioSourceState.Paused)
            {
              await playSource.ResumeAsync();
              _logger.LogInformation("Resumed playback from paused state");
            }
            else
            {
              await playSource.PlayAsync();
              _logger.LogInformation("Started playback");
            }
          }
          else if (_audioEngine.State != AudioEngineState.Running)
          {
            await _audioEngine.StartAsync();
            _logger.LogInformation("Started audio engine");
          }
          break;

        case PlaybackAction.Pause:
          if (primarySource is IPrimaryAudioSource pauseSource)
          {
            await pauseSource.PauseAsync();
            _logger.LogInformation("Paused playback");
          }
          break;

        case PlaybackAction.Stop:
          if (primarySource is IPrimaryAudioSource stopSource)
          {
            await stopSource.StopAsync();
            _logger.LogInformation("Stopped playback");
          }
          break;

        case PlaybackAction.Seek:
          if (request.SeekPosition.HasValue && primarySource is IPrimaryAudioSource seekSource && seekSource.IsSeekable)
          {
            await seekSource.SeekAsync(request.SeekPosition.Value);
            _logger.LogInformation("Seeked to {Position}", request.SeekPosition.Value);
          }
          break;

        case PlaybackAction.None:
        default:
          // Just updating properties, no action needed
          break;
      }

      // Return updated state
      return GetPlaybackState();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error updating playback state");
      return StatusCode(500, new { error = "Failed to update playback state" });
    }
  }

  /// <summary>
  /// Starts/resumes audio playback on the active source.
  /// </summary>
  [HttpPost("start")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> StartPlayback()
  {
    try
    {
      // Ensure the engine is running
      if (_audioEngine.State == AudioEngineState.Uninitialized)
        await _audioEngine.InitializeAsync();
      if (_audioEngine.State == AudioEngineState.Ready)
        await _audioEngine.StartAsync();

      // Resume the active source if it was stopped/paused
      if (_audioManager?.ActiveSource is IPrimaryAudioSource primarySource)
      {
        if (primarySource.State == AudioSourceState.Stopped ||
            primarySource.State == AudioSourceState.Ready)
        {
          await primarySource.PlayAsync();
          _logger.LogInformation("Resumed playback on {Source}", primarySource.Name);
        }
        else if (primarySource.State == AudioSourceState.Paused)
        {
          await primarySource.ResumeAsync();
          _logger.LogInformation("Resumed playback on {Source}", primarySource.Name);
        }
      }

      return Ok(new { message = "Audio playback started", state = _audioEngine.State.ToString() });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error starting audio playback");
      return StatusCode(500, new { error = "Failed to start audio playback" });
    }
  }

  /// <summary>
  /// Stops audio playback on the active source.
  /// </summary>
  [HttpPost("stop")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> StopPlayback()
  {
    try
    {
      // Stop the active source (capture device, file player, etc.)
      if (_audioManager != null)
      {
        await _audioManager.StopAsync();
        _logger.LogInformation("Audio playback stopped via AudioManager");
      }
      else if (_audioEngine.State == AudioEngineState.Running)
      {
        // Fallback: stop the engine directly if no AudioManager
        await _audioEngine.StopAsync();
        _logger.LogInformation("Audio engine stopped (no AudioManager)");
      }

      var sourceState = _audioManager?.ActiveSource?.State.ToString() ?? "Stopped";
      return Ok(new { message = "Audio playback stopped", state = sourceState });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping audio playback");
      return StatusCode(500, new { error = "Failed to stop audio playback" });
    }
  }

  /// <summary>
  /// Gets the current volume level.
  /// </summary>
  [HttpGet("volume")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  public ActionResult GetVolume()
  {
    var mixer = _audioEngine.GetMasterMixer();
    return Ok(new
    {
      volume = mixer.MasterVolume,
      isMuted = mixer.IsMuted,
      balance = mixer.Balance
    });
  }

  /// <summary>
  /// Sets the volume level.
  /// </summary>
  /// <param name="volume">The volume level (0.0 to 1.0).</param>
  [HttpPost("volume/{volume:float}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult SetVolume(float volume)
  {
    if (volume < 0 || volume > 1)
    {
      return BadRequest(new { error = "Volume must be between 0.0 and 1.0" });
    }

    var mixer = _audioEngine.GetMasterMixer();
    if (_audioManager != null)
      _audioManager.MasterVolume = volume;
    else
      mixer.MasterVolume = volume;
    _logger.LogInformation("Volume set to {Volume}", volume);

    return Ok(new { volume = mixer.MasterVolume });
  }

  /// <summary>
  /// Toggles mute state.
  /// </summary>
  [HttpPost("mute")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult ToggleMute()
  {
    var mixer = _audioEngine.GetMasterMixer();
    var newMuted = !mixer.IsMuted;
    if (_audioManager != null)
      _audioManager.IsMuted = newMuted;
    else
      mixer.IsMuted = newMuted;
    _logger.LogInformation("Mute toggled to {IsMuted}", mixer.IsMuted);

    return Ok(new { isMuted = mixer.IsMuted });
  }

  /// <summary>
  /// Skips to the next track.
  /// </summary>
  [HttpPost("next")]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlaybackStateDto>> Next()
  {
    try
    {
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource is not IPrimaryAudioSource primary)
      {
        _logger.LogWarning("Next track failed: No primary audio source is active. Active sources: {Sources}", 
            string.Join(", ", _audioEngine.GetMasterMixer().GetActiveSources().Select(s => s.Name)));
        return BadRequest(new { error = "No primary audio source is active" });
      }

      if (!primary.SupportsNext)
      {
        _logger.LogWarning("Next track failed: Active source {SourceName} ({SourceType}) does not support next track navigation", 
            primary.Name, primary.Type);
        return BadRequest(new { error = "The active source does not support next track navigation" });
      }

      await primary.NextAsync();
      _logger.LogInformation("Skipped to next track on source {SourceName}", primary.Name);

      return GetPlaybackState();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error skipping to next track");
      return StatusCode(500, new { error = "Failed to skip to next track" });
    }
  }

  /// <summary>
  /// Goes to the previous track.
  /// </summary>
  [HttpPost("previous")]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlaybackStateDto>> Previous()
  {
    try
    {
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource is not IPrimaryAudioSource primary)
      {
        _logger.LogWarning("Previous track failed: No primary audio source is active");
        return BadRequest(new { error = "No primary audio source is active" });
      }

      if (!primary.SupportsPrevious)
      {
        _logger.LogWarning("Previous track failed: Active source {SourceName} does not support previous track", primary.Name);
        return BadRequest(new { error = "The active source does not support previous track navigation" });
      }

      await primary.PreviousAsync();
      _logger.LogInformation("Went to previous track on source {SourceName}", primary.Name);

      return GetPlaybackState();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error going to previous track");
      return StatusCode(500, new { error = "Failed to go to previous track" });
    }
  }

  /// <summary>
  /// Sets the shuffle state.
  /// </summary>
  /// <param name="request">The shuffle state request.</param>
  [HttpPost("shuffle")]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlaybackStateDto>> SetShuffle([FromBody] SetShuffleRequest request)
  {
    try
    {
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource is not IPrimaryAudioSource primary)
      {
        return BadRequest(new { error = "No primary audio source is active" });
      }

      if (!primary.SupportsShuffle)
      {
        return BadRequest(new { error = "The active source does not support shuffle mode" });
      }

      await primary.SetShuffleAsync(request.Enabled);
      _logger.LogInformation("Shuffle set to {Enabled}", request.Enabled);

      return GetPlaybackState();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting shuffle state");
      return StatusCode(500, new { error = "Failed to set shuffle state" });
    }
  }

  /// <summary>
  /// Sets the repeat mode.
  /// </summary>
  /// <param name="request">The repeat mode request.</param>
  [HttpPost("repeat")]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<PlaybackStateDto>> SetRepeatMode([FromBody] SetRepeatModeRequest request)
  {
    try
    {
      var mixer = _audioEngine.GetMasterMixer();
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource is not IPrimaryAudioSource primary)
      {
        return BadRequest(new { error = "No primary audio source is active" });
      }

      if (!primary.SupportsRepeat)
      {
        return BadRequest(new { error = "The active source does not support repeat mode" });
      }

      // Parse the repeat mode from string
      if (!Enum.TryParse<Radio.Core.Models.Audio.RepeatMode>(request.Mode, ignoreCase: true, out var repeatMode))
      {
        return BadRequest(new { error = $"Invalid repeat mode. Valid values are: Off, One, All" });
      }

      await primary.SetRepeatModeAsync(repeatMode);
      _logger.LogInformation("Repeat mode set to {RepeatMode}", repeatMode);

      return GetPlaybackState();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting repeat mode");
      return StatusCode(500, new { error = "Failed to set repeat mode" });
    }
  }

  /// <summary>
  /// Gets the current "Now Playing" information with structured track metadata.
  /// Always returns valid content with defaults when no track is available.
  /// </summary>
  /// <returns>The current now playing information.</returns>
  [HttpGet("nowplaying")]
  [ProducesResponseType(typeof(NowPlayingDto), StatusCodes.Status200OK)]
  public ActionResult<NowPlayingDto> GetNowPlaying()
  {
    try
    {
      var primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

      var nowPlaying = new NowPlayingDto();

      if (primarySource != null)
      {
        // Source information
        nowPlaying.SourceType = primarySource.Type.ToString();
        nowPlaying.SourceName = primarySource.Name;
        nowPlaying.IsPlaying = _audioEngine.State == AudioEngineState.Running && primarySource.State == AudioSourceState.Playing;
        nowPlaying.IsPaused = primarySource.State == AudioSourceState.Paused;

        if (primarySource is IPrimaryAudioSource primary)
        {
          // Timing information
          nowPlaying.Position = primary.Position;
          nowPlaying.Duration = primary.Duration;

          // Calculate progress percentage if both duration and position are valid
          if (primary.Duration.HasValue && 
              primary.Duration.Value.TotalSeconds > 0 && 
              primary.Position.TotalSeconds >= 0)
          {
            nowPlaying.ProgressPercentage = (primary.Position.TotalSeconds / primary.Duration.Value.TotalSeconds) * 100.0;
          }

          // Extract metadata with defaults
          var metadataDto = primary.MapToNowPlaying();
          nowPlaying.Title = metadataDto.Title;
          nowPlaying.Artist = metadataDto.Artist;
          nowPlaying.Album = metadataDto.Album;
          nowPlaying.AlbumArtUrl = metadataDto.AlbumArtUrl;
          nowPlaying.ExtendedMetadata = metadataDto.ExtendedMetadata;
        }
      }
      else
      {
        // No source active - return defaults
        nowPlaying.SourceType = "None";
        nowPlaying.SourceName = "No Source";
        nowPlaying.IsPlaying = false;
        nowPlaying.IsPaused = false;
      }

      return Ok(nowPlaying);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting now playing information");
      return StatusCode(500, new { error = "Failed to get now playing information" });
    }
  }

  /// <summary>
  /// Gets all per-source gain offsets.
  /// </summary>
  /// <returns>Dictionary of source type to gain multiplier (0.0-2.0, 1.0 = unity).</returns>
  [HttpGet("sourcegain")]
  [ProducesResponseType(typeof(Dictionary<string, float>), StatusCodes.Status200OK)]
  public ActionResult<Dictionary<string, float>> GetSourceGainOffsets()
  {
    if (_audioManager == null)
      return Ok(new Dictionary<string, float>());

    return Ok(_audioManager.GetAllSourceGains());
  }

  /// <summary>
  /// Sets the gain offset for a specific source type.
  /// </summary>
  /// <param name="sourceType">The source type name (e.g., Radio, FilePlayer, Bluetooth).</param>
  /// <param name="gain">Linear gain multiplier (0.0-2.0, 1.0 = unity/0dB).</param>
  [HttpPost("sourcegain/{sourceType}/{gain:float}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult SetSourceGain(string sourceType, float gain)
  {
    if (_audioManager == null)
      return StatusCode(500, new { error = "AudioManager not available" });

    if (!Enum.TryParse<Radio.Core.Interfaces.Audio.AudioSourceType>(sourceType, ignoreCase: true, out var parsed))
      return BadRequest(new { error = $"Invalid source type '{sourceType}'. Valid: Radio, Vinyl, FilePlayer, GenericUSB, Bluetooth" });

    if (gain < 0f || gain > 2f)
      return BadRequest(new { error = "Gain must be between 0.0 and 2.0" });

    _audioManager.SetSourceGain(parsed, gain);
    _logger.LogInformation("Source gain set: {SourceType} = {Gain:F2}", sourceType, gain);

    return Ok(new { sourceType, gain });
  }

  /// <summary>
  /// Gets the current fingerprint identification status, including recent events and throughput rates.
  /// </summary>
  [HttpGet("fingerprint/status")]
  [ProducesResponseType(typeof(FingerprintStatusDto), StatusCodes.Status200OK)]
  public ActionResult<FingerprintStatusDto> GetFingerprintStatus()
  {
    if (_fingerprintService == null)
    {
      return Ok(new FingerprintStatusDto { IsEnabled = false });
    }

    var snapshot = _fingerprintService.GetStatus();
    return Ok(snapshot.MapToDto());
  }

  /// <summary>
  /// Logs a distortion marker with a snapshot of current audio state for debugging.
  /// </summary>
  [HttpPost("debug/distortion-marker")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult ReportDistortionMarker()
  {
    var timestamp = DateTimeOffset.UtcNow;

    // Gather audio state snapshot
    var source = _audioManager?.ActiveSource;
    var mixer = _audioEngine.GetMasterMixer();

    var snapshot = new Dictionary<string, object?>
    {
      ["timestamp"] = timestamp.ToString("o"),
      ["engineState"] = _audioEngine.State.ToString(),
      ["masterVolume"] = mixer.MasterVolume,
      ["isMuted"] = mixer.IsMuted,
      ["balance"] = mixer.Balance,
    };

    // Active source info
    if (source != null)
    {
      snapshot["source"] = new
      {
        name = source.Name,
        type = source.Type.ToString(),
        state = source.State.ToString(),
      };
    }

    // Source gain offsets
    if (_audioManager != null)
    {
      snapshot["sourceGains"] = _audioManager.GetAllSourceGains();
    }

    // Visualization levels (RMS, peak, clipping)
    if (_visualizerService is { IsActive: true })
    {
      try
      {
        var levels = _visualizerService.GetLevelData();
        snapshot["levels"] = new
        {
          leftRms = levels.LeftRms,
          rightRms = levels.RightRms,
          leftPeak = levels.LeftPeak,
          rightPeak = levels.RightPeak,
          leftRmsDb = levels.LeftRmsDb,
          rightRmsDb = levels.RightRmsDb,
          leftPeakDb = levels.LeftPeakDb,
          rightPeakDb = levels.RightPeakDb,
          isClipping = levels.IsClipping,
        };
      }
      catch (Exception ex)
      {
        snapshot["levelsError"] = ex.Message;
      }
    }

    // Playback service diagnostics (buffer/player state)
    if (_playbackService != null)
    {
      try
      {
        var diag = _playbackService.GetDiagnostics();
        var format = _playbackService.GetAudioFormat();
        snapshot["playback"] = new
        {
          activePlayers = diag.ActivePlayers,
          activeComponents = diag.ActiveComponents,
          playerIds = diag.PlayerIds,
          sampleRate = format.SampleRate,
          channels = format.Channels,
        };
      }
      catch (Exception ex)
      {
        snapshot["playbackError"] = ex.Message;
      }
    }

    // Ducking state
    snapshot["ducking"] = new
    {
      isDucking = _duckingService.IsDucking,
      duckLevel = _duckingService.CurrentDuckLevel,
      activeEvents = _duckingService.ActiveEventCount,
    };

    // Log at WARNING so it stands out in journalctl
    _logger.LogWarning("AUDIO_DISTORTION_MARKER at {Timestamp} — {@Snapshot}", timestamp, snapshot);

    return Ok(snapshot);
  }
}
