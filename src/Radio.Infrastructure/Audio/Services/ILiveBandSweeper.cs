using RTLSDRCore.Sweep;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// A radio source that can sweep a band with its own running receiver
/// (AUD-76 live path). Output is silenced for the duration and the receiver
/// returns to its station afterwards.
/// </summary>
public interface ILiveBandSweeper
{
  /// <summary>True when the receiver is running and not seek-scanning.</summary>
  bool CanSweepLive { get; }

  /// <summary>
  /// Sweeps <paramref name="channels"/> on a background thread: one tune per channel, centred on
  /// it, each measured over the FM window (<see cref="BandSweepPlans.FmHalfWindowHz"/> /
  /// <see cref="BandSweepPlans.FmDcExcludeHz"/>).
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

  /// <summary>
  /// Sweeps <paramref name="plan"/> on a background thread (AUD-91).
  /// </summary>
  /// <remarks>
  /// The default implementation handles only a plan the channel-list overload can measure
  /// identically, one channel per tune at offset 0 with the FM window, by passing
  /// <see cref="BandSweepPlan.Channels"/> to it. For any other plan it returns a task faulted
  /// with <see cref="NotSupportedException"/>. <c>SDRRadioAudioSource</c> overrides it.
  /// </remarks>
  /// <param name="plan">The band's sweep plan.</param>
  /// <param name="gainDb">Manual tuner gain for the sweep, in dB.</param>
  /// <param name="samplesPerMeasurement">Samples per settling read and per measurement read.</param>
  /// <param name="progress">Receives progress after each tune, as cumulative channels.</param>
  /// <param name="cancellationToken">Cancels the sweep.</param>
  /// <returns>The measured channel levels.</returns>
  Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
    BandSweepPlan plan,
    float gainDb,
    int samplesPerMeasurement,
    IProgress<BandSweepProgress>? progress,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(plan);
    bool channelListEquivalent =
      plan.HalfWindowHz == BandSweepPlans.FmHalfWindowHz
      && plan.DcExcludeHz == BandSweepPlans.FmDcExcludeHz
      && plan.Tunes.All(t => t.ChannelHz.Count == 1 && t.ChannelHz[0] == t.CentreHz);
    if (!channelListEquivalent)
    {
      return Task.FromException<IReadOnlyList<ChannelLevel>>(new NotSupportedException(
        $"This live sweeper cannot measure the {plan.Band} plan's grouped tunes."));
    }

    return SweepLiveAsync(plan.Channels, gainDb, samplesPerMeasurement, progress, cancellationToken);
  }
}
