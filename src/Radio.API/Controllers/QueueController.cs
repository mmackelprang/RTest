using Microsoft.AspNetCore.Mvc;
using Radio.API.Extensions;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for music queue management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class QueueController : ControllerBase
{
  private readonly ILogger<QueueController> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager? _audioManager;

  /// <summary>
  /// Initializes a new instance of the QueueController.
  /// </summary>
  public QueueController(
    ILogger<QueueController> logger,
    IAudioEngine audioEngine,
    IAudioManager? audioManager = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
  }

  /// <summary>
  /// Gets the current queue from the active primary source.
  /// </summary>
  /// <returns>The current queue items.</returns>
  [HttpGet]
  [ProducesResponseType(typeof(List<QueueItemDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<QueueItemDto>>> GetQueue()
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      var queue = await queueSource!.GetQueueAsync();
      var queueDtos = queue.Select(MapToQueueItemDto).ToList();

      return Ok(queueDtos);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting queue");
      return StatusCode(500, new { error = "Failed to get queue" });
    }
  }

  /// <summary>
  /// Adds a track to the queue.
  /// </summary>
  /// <param name="request">The add to queue request.</param>
  /// <returns>The updated queue.</returns>
  [HttpPost("add")]
  [ProducesResponseType(typeof(List<QueueItemDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<QueueItemDto>>> AddToQueue([FromBody] AddToQueueRequest request)
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      await queueSource!.AddToQueueAsync(request.TrackIdentifier, request.Position);
      _logger.LogDebug("Added track to queue: {TrackIdentifier}", request.TrackIdentifier);

      var queue = await queueSource.GetQueueAsync();
      var queueDtos = queue.Select(MapToQueueItemDto).ToList();

      return Ok(queueDtos);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error adding track to queue");
      return StatusCode(500, new { error = "Failed to add track to queue" });
    }
  }

  /// <summary>
  /// Removes an item from the queue at the specified index.
  /// </summary>
  /// <param name="index">The zero-based index of the item to remove.</param>
  /// <returns>The updated queue.</returns>
  [HttpDelete("{index}")]
  [ProducesResponseType(typeof(List<QueueItemDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<QueueItemDto>>> RemoveFromQueue(int index)
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      await queueSource!.RemoveFromQueueAsync(index);
      _logger.LogDebug("Removed item from queue at index: {Index}", index);

      var queue = await queueSource.GetQueueAsync();
      var queueDtos = queue.Select(MapToQueueItemDto).ToList();

      return Ok(queueDtos);
    }
    catch (ArgumentOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Invalid index for queue removal: {Index}", index);
      return BadRequest(new { error = $"Invalid index: {index}" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error removing item from queue");
      return StatusCode(500, new { error = "Failed to remove item from queue" });
    }
  }

  /// <summary>
  /// Clears all items from the queue.
  /// </summary>
  /// <returns>Empty queue confirmation.</returns>
  [HttpDelete]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> ClearQueue()
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      await queueSource!.ClearQueueAsync();
      _logger.LogDebug("Cleared queue");

      return Ok(new { message = "Queue cleared successfully", itemCount = 0 });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error clearing queue");
      return StatusCode(500, new { error = "Failed to clear queue" });
    }
  }

  /// <summary>
  /// Moves a queue item from one position to another.
  /// </summary>
  /// <param name="request">The move queue item request.</param>
  /// <returns>The updated queue.</returns>
  [HttpPost("move")]
  [ProducesResponseType(typeof(List<QueueItemDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<QueueItemDto>>> MoveQueueItem([FromBody] MoveQueueItemRequest request)
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      await queueSource!.MoveQueueItemAsync(request.FromIndex, request.ToIndex);
      _logger.LogDebug("Moved queue item from index {FromIndex} to {ToIndex}", request.FromIndex, request.ToIndex);

      var queue = await queueSource.GetQueueAsync();
      var queueDtos = queue.Select(MapToQueueItemDto).ToList();

      return Ok(queueDtos);
    }
    catch (ArgumentOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Invalid indices for queue move: FromIndex={FromIndex}, ToIndex={ToIndex}", request.FromIndex, request.ToIndex);
      return BadRequest(new { error = $"Invalid indices: FromIndex={request.FromIndex}, ToIndex={request.ToIndex}" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error moving queue item");
      return StatusCode(500, new { error = "Failed to move queue item" });
    }
  }

  /// <summary>
  /// Jumps to and plays the item at the specified index in the queue.
  /// </summary>
  /// <param name="index">The zero-based index of the item to play.</param>
  /// <returns>The updated playback state.</returns>
  [HttpPost("jump/{index}")]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<PlaybackStateDto>> JumpToIndex(int index)
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource, out var primarySource);
      if (result != null)
      {
        return result;
      }

      await queueSource!.JumpToIndexAsync(index);
      _logger.LogDebug("Jumped to queue index: {Index}", index);

      // Return updated playback state
      var state = BuildPlaybackStateDto(primarySource!);
      return Ok(state);
    }
    catch (ArgumentOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Invalid index for jump: {Index}", index);
      return BadRequest(new { error = $"Invalid index: {index}" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error jumping to queue index");
      return StatusCode(500, new { error = "Failed to jump to queue index" });
    }
  }

  /// <summary>
  /// Gets the full playlist (played + current + upcoming) with state annotations.
  /// </summary>
  /// <returns>The full playlist items with state.</returns>
  [HttpGet("full")]
  [ProducesResponseType(typeof(List<QueueItemDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<QueueItemDto>>> GetFullPlaylist()
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      var fullPlaylist = await queueSource!.GetFullPlaylistAsync();
      var dtos = fullPlaylist.Select(MapToQueueItemDto).ToList();

      return Ok(dtos);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting full playlist");
      return StatusCode(500, new { error = "Failed to get full playlist" });
    }
  }

  /// <summary>
  /// Jumps to a position in the full playlist (including already-played tracks).
  /// </summary>
  /// <param name="index">The zero-based index in the full playlist.</param>
  /// <returns>The updated playback state.</returns>
  [HttpPost("jump-playlist/{index}")]
  [ProducesResponseType(typeof(PlaybackStateDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<PlaybackStateDto>> JumpToFullPlaylistIndex(int index)
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource, out var primarySource);
      if (result != null)
      {
        return result;
      }

      await queueSource!.JumpToFullPlaylistIndexAsync(index);
      _logger.LogDebug("Jumped to full playlist index: {Index}", index);

      var state = BuildPlaybackStateDto(primarySource!);
      return Ok(state);
    }
    catch (ArgumentOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Invalid index for full playlist jump: {Index}", index);
      return BadRequest(new { error = $"Invalid index: {index}" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error jumping to full playlist index");
      return StatusCode(500, new { error = "Failed to jump to full playlist index" });
    }
  }

  /// <summary>
  /// Checks if a track identifier already exists in the queue.
  /// </summary>
  /// <param name="identifier">The track identifier to check.</param>
  /// <returns>True if the track is in the queue, false otherwise.</returns>
  [HttpGet("contains/{identifier}")]
  [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public async Task<ActionResult<bool>> ContainsTrack(string identifier)
  {
    try
    {
      var result = TryGetQueueSource(out var queueSource);
      if (result != null)
      {
        return result;
      }

      var queue = await queueSource!.GetQueueAsync();
      var contains = queue.Any(item => item.Id == identifier);

      return Ok(contains);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error checking if queue contains track");
      return StatusCode(500, new { error = "Failed to check queue" });
    }
  }

  private static QueueItemDto MapToQueueItemDto(Radio.Core.Models.Audio.QueueItem queueItem)
  {
    return new QueueItemDto
    {
      Id = queueItem.Id,
      Title = queueItem.Title,
      Artist = queueItem.Artist,
      Album = queueItem.Album,
      Duration = FormatDuration(queueItem.Duration),
      AlbumArtUrl = queueItem.AlbumArtUrl,
      Index = queueItem.Index,
      IsCurrent = queueItem.IsCurrent,
      State = queueItem.State.ToString(),
      FullPlaylistIndex = queueItem.FullPlaylistIndex
    };
  }

  private static string? FormatDuration(TimeSpan? duration)
  {
    if (duration == null)
    {
      return null;
    }

    var ts = duration.Value;
    return ts.TotalHours >= 1
      ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
      : $"{ts.Minutes}:{ts.Seconds:D2}";
  }

  private static AudioSourceDto MapToAudioSourceDto(IAudioSource source)
  {
    var dto = new AudioSourceDto
    {
      Id = source.Id,
      Name = source.Name,
      Type = source.GetType().Name.Replace("AudioSource", ""),
      Category = source.Category.ToString(),
      State = source.State.ToString(),
      Volume = source.Volume
    };

    if (source is IPrimaryAudioSource primary)
    {
      dto.IsSeekable = primary.IsSeekable;
      // Use AudioDtoMapper.CopyMetadataSafe rather than .ToDictionary() — see that
      // helper's doc for why .ToDictionary() can throw an ArgumentException when
      // background threads mutate the source's live metadata dictionary during
      // enumeration.
      dto.Metadata = AudioDtoMapper.CopyMetadataSafe(primary.Metadata);
    }

    return dto;
  }

  /// <summary>
  /// Tries to get the active queue source and validates it supports queue management.
  /// Falls back to the cached FilePlayer source if the active source doesn't support queue,
  /// allowing playlist state to be read even when FilePlayer is not the active source.
  /// </summary>
  /// <param name="queueSource">The queue source if found and valid, otherwise null.</param>
  /// <param name="primarySource">The primary source if found, otherwise null.</param>
  /// <returns>An error ActionResult if validation fails, otherwise null.</returns>
  private ActionResult? TryGetQueueSource(out IPlayQueue? queueSource, out IAudioSource? primarySource)
  {
    primarySource = _audioManager?.ActiveSource ?? _audioEngine.GetActivePrimaryAudioSource();

    // Check active source first
    if (primarySource is IPlayQueue activeQueue)
    {
      queueSource = activeQueue;
      return null;
    }

    // Fall back to cached FilePlayer source for playlist persistence across source switches
    var cachedFilePlayer = _audioManager?.GetCachedSource(AudioSourceType.FilePlayer);
    if (cachedFilePlayer is IPlayQueue cachedQueue)
    {
      primarySource = cachedFilePlayer;
      queueSource = cachedQueue;
      return null;
    }

    if (primarySource == null)
    {
      queueSource = null;
      return NotFound(new { error = "No primary audio source is active" });
    }

    queueSource = null;
    return BadRequest(new { error = "The active source does not support queue management" });
  }

  /// <summary>
  /// Tries to get the active queue source. Overload without primarySource output parameter.
  /// </summary>
  /// <param name="queueSource">The queue source if found and valid, otherwise null.</param>
  /// <returns>An error ActionResult if validation fails, otherwise null.</returns>
  private ActionResult? TryGetQueueSource(out IPlayQueue? queueSource)
  {
    return TryGetQueueSource(out queueSource, out _);
  }

  /// <summary>
  /// Builds a PlaybackStateDto from the given primary source and current engine state.
  /// </summary>
  /// <param name="primarySource">The primary audio source.</param>
  /// <returns>A fully populated PlaybackStateDto.</returns>
  private PlaybackStateDto BuildPlaybackStateDto(IAudioSource primarySource)
  {
    var mixer = _audioEngine.GetMasterMixer();
    var state = new PlaybackStateDto
    {
      IsPlaying = _audioEngine.State == AudioEngineState.Running,
      IsPaused = primarySource.State == AudioSourceState.Paused,
      Volume = mixer.MasterVolume,
      IsMuted = mixer.IsMuted,
      Balance = mixer.Balance
    };

    if (primarySource is IPrimaryAudioSource primary)
    {
      state.ActiveSource = MapToAudioSourceDto(primarySource);
      state.Position = primary.Position;
      state.Duration = primary.Duration;
      state.CanNext = primary.SupportsNext;
      state.CanPrevious = primary.SupportsPrevious;
      state.CanShuffle = primary.SupportsShuffle;
      state.CanRepeat = primary.SupportsRepeat;
      state.IsShuffleEnabled = primary.IsShuffleEnabled;
      state.RepeatMode = primary.RepeatMode.ToString();
    }

    return state;
  }
}
