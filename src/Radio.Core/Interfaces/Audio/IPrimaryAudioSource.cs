namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for primary audio sources that provide continuous playback.
/// Only one primary source can be active at a time in the audio system.
/// </summary>
public interface IPrimaryAudioSource : IAudioSource
{
  /// <summary>
  /// Gets the duration of the current content. Returns null for live streams.
  /// </summary>
  TimeSpan? Duration { get; }

  /// <summary>
  /// Gets the current playback position.
  /// </summary>
  TimeSpan Position { get; }

  /// <summary>
  /// Gets whether seeking is supported for this source.
  /// </summary>
  bool IsSeekable { get; }

  /// <summary>
  /// Starts playback of the audio source.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task PlayAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Pauses playback while maintaining the current position.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task PauseAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Resumes playback from the paused position.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task ResumeAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops playback and resets the position to the beginning.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Seeks to a specific position in the audio content.
  /// Only valid if <see cref="IsSeekable"/> is true.
  /// </summary>
  /// <param name="position">The position to seek to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  /// <exception cref="NotSupportedException">Thrown if seeking is not supported.</exception>
  Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

  /// <summary>
  /// Event raised when playback completes (for finite sources).
  /// </summary>
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  /// <summary>
  /// Gets metadata about the currently playing content.
  /// Common keys include: Title, Artist, Album, Duration.
  /// </summary>
  IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Event arguments for when an audio source completes playback.
/// </summary>
public class AudioSourceCompletedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the ID of the audio source that completed.
  /// </summary>
  public required string SourceId { get; init; }

  /// <summary>
  /// Gets the reason for completion.
  /// </summary>
  public PlaybackCompletionReason Reason { get; init; }

  /// <summary>
  /// Gets any error that occurred, if the completion was due to an error.
  /// </summary>
  public Exception? Error { get; init; }
}

/// <summary>
/// Reasons why playback may have completed.
/// </summary>
public enum PlaybackCompletionReason
{
  /// <summary>Content finished playing naturally.</summary>
  EndOfContent,

  /// <summary>User stopped playback.</summary>
  UserStopped,

  /// <summary>An error occurred during playback.</summary>
  Error,

  /// <summary>The source was disposed.</summary>
  Disposed
}
