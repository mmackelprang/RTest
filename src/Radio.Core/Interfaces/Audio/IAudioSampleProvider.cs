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
}
