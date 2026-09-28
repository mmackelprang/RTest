namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for playing TTS announcements with audio ducking.
/// Shared by phone call integration and notification endpoints.
/// </summary>
public interface IAnnouncementService
{
  /// <summary>
  /// Announce a message via TTS with audio ducking.
  /// </summary>
  /// <param name="message">Text to speak.</param>
  /// <param name="priority">Ducking priority (1-10, higher = more important).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>
  /// How the announcement ended. Failures are still caught and logged inside the service — this
  /// method does not throw for them — but they are no longer indistinguishable from success (TTS-2).
  /// </returns>
  Task<AnnouncementOutcome> AnnounceAsync(string message, int priority = 5, CancellationToken cancellationToken = default);

  /// <summary>
  /// Play a sound file followed by a TTS announcement, both with ducking.
  /// Used for phone ring + caller announcement patterns.
  /// </summary>
  /// <param name="soundPath">Path to the sound file to play first.</param>
  /// <param name="message">Text to speak after the sound.</param>
  /// <param name="priority">Ducking priority (1-10).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  Task PlaySoundWithAnnouncementAsync(string soundPath, string message, int priority = 5, CancellationToken cancellationToken = default);

  /// <summary>
  /// Stop any currently playing announcement.
  /// </summary>
  Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// How an announcement ended (TTS-2).
/// </summary>
/// <remarks>
/// ⚠ <see cref="Failed"/> is deliberately the zero value, so an outcome nobody assigned — a
/// <c>default</c>, an unconfigured test double — reads as failure rather than as success. Success
/// silently reported for something that did not happen is exactly the defect this type exists to
/// end.
/// </remarks>
public enum AnnouncementOutcome
{
  /// <summary>The announcement did not play: TTS synthesis, ducking or playback failed.</summary>
  Failed = 0,

  /// <summary>The announcement played to its end.</summary>
  Completed = 1,

  /// <summary>The announcement started but was stopped or cancelled before it ended.</summary>
  Interrupted = 2
}
