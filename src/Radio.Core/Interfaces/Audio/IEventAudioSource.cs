namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for event audio sources that provide ephemeral playback.
/// Event sources are typically short-duration audio (TTS announcements, notifications)
/// that interrupt the primary source via ducking and auto-dispose when complete.
/// </summary>
public interface IEventAudioSource : IAudioSource
{
  /// <summary>
  /// Gets the duration of the event audio content.
  /// </summary>
  TimeSpan Duration { get; }

  /// <summary>
  /// Plays the event audio (one-shot playback).
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task PlayAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops playback immediately.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Event raised when playback completes.
  /// </summary>
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;
}
