using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for generating audio fingerprints from audio samples.
/// </summary>
public interface IFingerprintService
{
  /// <summary>
  /// Generates a fingerprint from audio samples.
  /// </summary>
  /// <param name="samples">The audio sample buffer.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The generated fingerprint data.</returns>
  Task<FingerprintData> GenerateFingerprintAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default);

  /// <summary>
  /// Generates a fingerprint from an audio file.
  /// </summary>
  /// <param name="filePath">The path to the audio file.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The generated fingerprint data.</returns>
  Task<FingerprintData> GenerateFingerprintFromFileAsync(
    string filePath,
    CancellationToken ct = default);
}
