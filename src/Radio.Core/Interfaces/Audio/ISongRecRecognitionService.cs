using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for recognizing audio using SongRec (Shazam).
/// Used as a fallback when AcoustID/Chromaprint fails to identify live audio sources.
/// </summary>
public interface ISongRecRecognitionService
{
  /// <summary>
  /// Attempts to recognize audio from captured samples using SongRec (Shazam algorithm).
  /// </summary>
  /// <param name="samples">The audio sample buffer to recognize.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Track metadata if recognized, null if no match or error.</returns>
  Task<TrackMetadata?> RecognizeAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default);

  /// <summary>
  /// Gets whether the SongRec binary is available on this system.
  /// </summary>
  bool IsAvailable { get; }
}
