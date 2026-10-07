using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for recognizing audio using SongRec (Shazam).
/// The sole recognizer for all audio sources (radio, vinyl, file, USB).
/// </summary>
public interface ISongRecRecognitionService
{
  /// <summary>
  /// Attempts to recognize audio from captured samples using SongRec (Shazam algorithm).
  /// </summary>
  /// <param name="samples">The audio sample buffer to recognize.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>
  /// <see cref="SongRecOutcome.Match"/> with the track, <see cref="SongRecOutcome.NoMatch"/> when SongRec ran
  /// cleanly without recognising the audio, or <see cref="SongRecOutcome.Error"/> when the attempt itself
  /// failed (not available, process start failure, timeout, non-zero exit, unparsable output). Caller
  /// cancellation is not an outcome: it surfaces as <see cref="OperationCanceledException"/>.
  /// </returns>
  Task<SongRecRecognitionResult> RecognizeAsync(
    AudioSampleBuffer samples,
    CancellationToken ct = default);

  /// <summary>
  /// Gets whether the SongRec binary is available on this system.
  /// </summary>
  bool IsAvailable { get; }
}
