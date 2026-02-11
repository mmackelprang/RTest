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
  /// When non-null, callers should prefer <see cref="IFingerprintService.GenerateFingerprintFromFileAsync"/>
  /// over tap-based capture, since AcoustID requires accurate track duration.
  /// </summary>
  string? SourceFilePath { get; }

  /// <summary>
  /// Gets whether the active source needs fingerprinting identification.
  /// Returns false when the source already has complete metadata (e.g., file with tags,
  /// Bluetooth with complete AVRCP data). Returns true for sources without metadata
  /// (radio, vinyl, USB) or sources with incomplete metadata.
  /// </summary>
  bool NeedsFingerprintingLookup { get; }
}
