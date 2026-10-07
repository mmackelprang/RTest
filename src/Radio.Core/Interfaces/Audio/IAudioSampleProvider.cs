using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Provides audio samples from various sources for fingerprinting.
/// </summary>
public interface IAudioSampleProvider
{
  /// <summary>
  /// Captures audio samples from the current source.
  /// </summary>
  /// <param name="duration">The duration of audio to capture.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The captured audio samples, or null if source is inactive.</returns>
  Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default);

  /// <summary>
  /// Gets whether the source is currently active and producing audio.
  /// </summary>
  bool IsActive { get; }

  /// <summary>
  /// Gets the name of the audio source.
  /// </summary>
  string SourceName { get; }

  /// <summary>
  /// Gets the source type for play history recording.
  /// </summary>
  PlaySource SourceType { get; }

  /// <summary>
  /// Gets the file path of the currently playing audio file, if the source is file-based.
  /// Returns null for non-file sources (radio, vinyl, Bluetooth).
  /// </summary>
  string? SourceFilePath { get; }

  /// <summary>
  /// Gets whether the active source needs fingerprinting identification.
  /// For the file player and Bluetooth: true while the current track's own metadata lacks a title, an
  /// artist or album art and no identification has yet been applied to it; false once it is complete or
  /// identified. Always true for sources without metadata of their own (radio, vinyl, USB).
  /// How often an identification is attempted is decided by the call policy, not by this flag.
  /// </summary>
  bool NeedsFingerprintingLookup { get; }

  /// <summary>
  /// For a source that knows when each track begins (file player, Bluetooth): the UTC instant the current
  /// track started, or the source last became the active one, whichever is later. A new value means a new
  /// track, which restarts that source's identification schedule. Null for sources that cannot tell
  /// (radio, vinyl, USB) and when nothing has been recorded yet.
  /// </summary>
  DateTime? CurrentTrackStartedUtc => null;
}
