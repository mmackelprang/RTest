using RTLSDRCore.Models;

namespace RTLSDRCore.Sweep;

/// <summary>
/// The minimal tuner surface a <see cref="BandSweeper"/> needs: retune, then
/// read a block of IQ samples.
/// </summary>
public interface ISweepTuner
{
  /// <summary>Sample rate of the IQ blocks returned by <see cref="Read"/>, in Hz.</summary>
  int SampleRate { get; }

  /// <summary>Retunes to <paramref name="frequencyHz"/>.</summary>
  /// <returns><c>false</c> when the hardware rejected the frequency.</returns>
  bool Tune(long frequencyHz);

  /// <summary>
  /// Fills <paramref name="buffer"/> with the next IQ samples from the tuner.
  /// The first block read after <see cref="Tune"/> may contain samples
  /// captured before the retune took effect; <see cref="BandSweeper"/>
  /// discards one block per channel for that reason.
  /// </summary>
  /// <returns>
  /// The number of samples written. A value smaller than
  /// <c>buffer.Length</c> means the read could not be completed.
  /// </returns>
  /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
  int Read(Span<IqSample> buffer, CancellationToken ct);
}
