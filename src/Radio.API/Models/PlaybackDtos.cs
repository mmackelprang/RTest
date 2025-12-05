namespace Radio.API.Models;

/// <summary>
/// Represents the current playback state.
/// </summary>
public class PlaybackStateDto
{
  /// <summary>
  /// Gets or sets whether audio is currently playing.
  /// </summary>
  public bool IsPlaying { get; set; }

  /// <summary>
  /// Gets or sets whether playback is paused.
  /// </summary>
  public bool IsPaused { get; set; }

  /// <summary>
  /// Gets or sets the master volume (0.0 to 1.0).
  /// </summary>
  public float Volume { get; set; }

  /// <summary>
  /// Gets or sets whether audio is muted.
  /// </summary>
  public bool IsMuted { get; set; }

  /// <summary>
  /// Gets or sets the stereo balance (-1.0 left to 1.0 right).
  /// </summary>
  public float Balance { get; set; }

  /// <summary>
  /// Gets or sets the current position in the track (if applicable).
  /// </summary>
  public TimeSpan? Position { get; set; }

  /// <summary>
  /// Gets or sets the duration of the current track (if applicable).
  /// </summary>
  public TimeSpan? Duration { get; set; }

  /// <summary>
  /// Gets or sets the active audio source information.
  /// </summary>
  public AudioSourceDto? ActiveSource { get; set; }

  /// <summary>
  /// Gets or sets the current ducking state.
  /// </summary>
  public DuckingStateDto? DuckingState { get; set; }

  /// <summary>
  /// Gets or sets whether the source can skip to next track.
  /// </summary>
  public bool CanNext { get; set; }

  /// <summary>
  /// Gets or sets whether the source can go to previous track.
  /// </summary>
  public bool CanPrevious { get; set; }

  /// <summary>
  /// Gets or sets whether the source can toggle shuffle.
  /// </summary>
  public bool CanShuffle { get; set; }

  /// <summary>
  /// Gets or sets whether the source can change repeat mode.
  /// </summary>
  public bool CanRepeat { get; set; }

  /// <summary>
  /// Gets or sets whether shuffle is currently enabled.
  /// </summary>
  public bool IsShuffleEnabled { get; set; }

  /// <summary>
  /// Gets or sets the current repeat mode (Off, One, All).
  /// </summary>
  public string? RepeatMode { get; set; }

  /// <summary>
  /// Gets or sets whether the source can start playback.
  /// </summary>
  public bool CanPlay { get; set; }

  /// <summary>
  /// Gets or sets whether the source can pause playback.
  /// </summary>
  public bool CanPause { get; set; }

  /// <summary>
  /// Gets or sets whether the source can stop playback.
  /// </summary>
  public bool CanStop { get; set; }

  /// <summary>
  /// Gets or sets whether the source can seek to a position.
  /// </summary>
  public bool CanSeek { get; set; }

  /// <summary>
  /// Gets or sets whether the source supports queue management.
  /// </summary>
  public bool CanQueue { get; set; }
}

/// <summary>
/// Request to update playback state.
/// </summary>
public class UpdatePlaybackRequest
{
  /// <summary>
  /// Gets or sets the action to perform.
  /// </summary>
  public PlaybackAction Action { get; set; }

  /// <summary>
  /// Gets or sets the volume (0.0 to 1.0), if changing volume.
  /// </summary>
  public float? Volume { get; set; }

  /// <summary>
  /// Gets or sets the balance (-1.0 left to 1.0 right), if changing balance.
  /// </summary>
  public float? Balance { get; set; }

  /// <summary>
  /// Gets or sets whether to mute/unmute.
  /// </summary>
  public bool? IsMuted { get; set; }

  /// <summary>
  /// Gets or sets the position to seek to (if seeking).
  /// </summary>
  public TimeSpan? SeekPosition { get; set; }
}

/// <summary>
/// Playback control actions.
/// </summary>
public enum PlaybackAction
{
  /// <summary>No action, just update properties.</summary>
  None,

  /// <summary>Start playback.</summary>
  Play,

  /// <summary>Pause playback.</summary>
  Pause,

  /// <summary>Stop playback.</summary>
  Stop,

  /// <summary>Seek to a position.</summary>
  Seek
}
