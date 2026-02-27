namespace Radio.Infrastructure.Audio.Validation;

/// <summary>
/// Interface for audio diagnostic validators that analyze pipeline audio at various tap points.
/// Implementations must be thread-safe — Submit is called from the audio thread.
/// </summary>
public interface IAudioValidator
{
  /// <summary>
  /// Submits a batch of interleaved stereo samples for analysis.
  /// Must return immediately (copy-and-queue, no blocking).
  /// </summary>
  /// <param name="samples">Interleaved stereo float samples.</param>
  /// <param name="stageName">Pipeline stage identifier (e.g., "V1-BTCapture", "V3-Mixer").</param>
  void Submit(ReadOnlySpan<float> samples, string stageName);

  /// <summary>
  /// Drains any pending analysis work. Called during shutdown.
  /// </summary>
  Task FlushAsync(CancellationToken cancellationToken = default);
}
