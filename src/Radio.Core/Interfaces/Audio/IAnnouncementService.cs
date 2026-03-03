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
  Task AnnounceAsync(string message, int priority = 5, CancellationToken cancellationToken = default);

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
