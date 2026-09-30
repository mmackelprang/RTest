using RTLSDRCore.Sweep;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// A radio source that can sweep the band with its own running receiver
/// (AUD-76 live path). Output is silenced for the duration and the receiver
/// returns to its station afterwards.
/// </summary>
public interface ILiveBandSweeper
{
  /// <summary>True when the receiver is running and not seek-scanning.</summary>
  bool CanSweepLive { get; }

  /// <summary>
  /// Runs the sweep on a background thread.
  /// </summary>
  /// <param name="channels">Channel centres, in Hz.</param>
  /// <param name="gainDb">Manual tuner gain for the sweep, in dB.</param>
  /// <param name="samplesPerMeasurement">Samples per settling read and per measurement read.</param>
  /// <param name="progress">Receives per-channel progress.</param>
  /// <param name="cancellationToken">Cancels the sweep.</param>
  /// <returns>The measured channel levels.</returns>
  Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
    IReadOnlyList<long> channels,
    float gainDb,
    int samplesPerMeasurement,
    IProgress<BandSweepProgress>? progress,
    CancellationToken cancellationToken);
}
