namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for event audio sources that provide ephemeral playback.
/// Event sources are typically short-duration audio (TTS announcements, notifications)
/// that interrupt the primary source via ducking and auto-dispose when complete.
/// </summary>
public interface IEventAudioSource : IAudioSource
{
  /// <summary>
  /// Gets the duration of the event audio.
  /// Non-nullable, unlike IPrimaryAudioSource.Duration: an event always has a length, and the
  /// "unknown duration" case lives on EventPlaybackSnapshot.Duration instead (ADR-029 §4.1).
  /// </summary>
  TimeSpan Duration { get; }

  /// <summary>
  /// Gets the current playback position.
  /// </summary>
  TimeSpan Position { get; }

  /// <summary>
  /// Gets whether seeking is supported for this source.
  /// </summary>
  bool IsSeekable { get; }

  /// <summary>
  /// Starts playback of the event audio.
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
  /// Stops playback of the event audio.
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
  /// Raised when playback completes.
  /// </summary>
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;
}
