using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

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
  private readonly IDuckingService _duckingService;

  /// <summary>
  /// Initializes a new instance of the AudioController.
  /// </summary>
  public AudioController(
    ILogger<AudioController> logger,
    IAudioEngine audioEngine,
    IDuckingService duckingService)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _duckingService = duckingService;
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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

      var state = new PlaybackStateDto
      {
        IsPlaying = _audioEngine.State == AudioEngineState.Running,
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

      // Validate and update volume if specified
      if (request.Volume.HasValue)
      {
        if (request.Volume.Value < 0f || request.Volume.Value > 1f)
        {
          return BadRequest(new { error = "Volume must be between 0 and 1" });
        }
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
        mixer.Balance = request.Balance.Value;
        _logger.LogInformation("Balance set to {Balance}", mixer.Balance);
      }

      // Update mute state if specified
      if (request.IsMuted.HasValue)
      {
        mixer.IsMuted = request.IsMuted.Value;
        _logger.LogInformation("Mute set to {IsMuted}", mixer.IsMuted);
      }

      // Handle playback actions
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

      switch (request.Action)
      {
        case PlaybackAction.Play:
          if (primarySource is IPrimaryAudioSource playSource)
          {
            await playSource.PlayAsync();
            _logger.LogInformation("Started playback");
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
  /// Starts the audio engine.
  /// </summary>
  [HttpPost("start")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> StartEngine()
  {
    try
    {
      if (_audioEngine.State == AudioEngineState.Uninitialized)
      {
        await _audioEngine.InitializeAsync();
      }

      if (_audioEngine.State == AudioEngineState.Ready)
      {
        await _audioEngine.StartAsync();
      }

      _logger.LogInformation("Audio engine started");
      return Ok(new { message = "Audio engine started", state = _audioEngine.State.ToString() });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error starting audio engine");
      return StatusCode(500, new { error = "Failed to start audio engine" });
    }
  }

  /// <summary>
  /// Stops the audio engine.
  /// </summary>
  [HttpPost("stop")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<IActionResult> StopEngine()
  {
    try
    {
      if (_audioEngine.State == AudioEngineState.Running)
      {
        await _audioEngine.StopAsync();
      }

      _logger.LogInformation("Audio engine stopped");
      return Ok(new { message = "Audio engine stopped", state = _audioEngine.State.ToString() });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping audio engine");
      return StatusCode(500, new { error = "Failed to stop audio engine" });
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
    mixer.IsMuted = !mixer.IsMuted;
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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource is not IPrimaryAudioSource primary)
      {
        return BadRequest(new { error = "No primary audio source is active" });
      }

      if (!primary.SupportsNext)
      {
        return BadRequest(new { error = "The active source does not support next track navigation" });
      }

      await primary.NextAsync();
      _logger.LogInformation("Skipped to next track");

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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

      if (primarySource is not IPrimaryAudioSource primary)
      {
        return BadRequest(new { error = "No primary audio source is active" });
      }

      if (!primary.SupportsPrevious)
      {
        return BadRequest(new { error = "The active source does not support previous track navigation" });
      }

      await primary.PreviousAsync();
      _logger.LogInformation("Went to previous track");

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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

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
      var primarySource = _audioEngine.GetActivePrimaryAudioSource();

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
}
