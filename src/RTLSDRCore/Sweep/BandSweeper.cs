using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore.Sweep;

/// <summary>A measured channel level produced by a sweep.</summary>
/// <param name="FrequencyHz">Channel centre, in Hz.</param>
/// <param name="LevelDbfs">In-channel level from <see cref="ChannelPowerMeter.MeasureDbfs"/>, in dB.</param>
public sealed record ChannelLevel(long FrequencyHz, float LevelDbfs);

/// <summary>Progress of a running sweep.</summary>
/// <param name="ChannelsDone">Channels processed so far, including ones that could not be measured.</param>
/// <param name="ChannelsTotal">Channels in the sweep.</param>
public sealed record BandSweepProgress(int ChannelsDone, int ChannelsTotal);

/// <summary>
/// Steps a tuner across a channel list and measures each channel's level.
/// Pure logic over <see cref="ISweepTuner"/>: it owns no device and reads no clock.
/// </summary>
public static class BandSweeper
{
  private static readonly ILogger Logger = Log.ForContext(typeof(BandSweeper));

  /// <summary>Default number of IQ samples measured per channel.</summary>
  public const int DefaultSamplesPerMeasurement = 16384;

  /// <summary>
  /// Sweeps <paramref name="channels"/> in order. For each channel: tune, read
  /// one block and discard it (it can hold samples from before the retune),
  /// read one more block and measure it with <see cref="ChannelPowerMeter"/>.
  /// </summary>
  /// <param name="tuner">The tuner to drive.</param>
  /// <param name="channels">Channel centres to measure, in Hz.</param>
  /// <param name="samplesPerMeasurement">Samples per discard read and per measurement read.</param>
  /// <param name="progress">Receives a report after each channel, measured or not.</param>
  /// <param name="ct">Cancels the sweep.</param>
  /// <returns>
  /// One level per channel that was measured. A channel whose tune failed or
  /// whose reads came back short is left out of the result.
  /// </returns>
  /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
  public static IReadOnlyList<ChannelLevel> Sweep(
    ISweepTuner tuner,
    IReadOnlyList<long> channels,
    int samplesPerMeasurement,
    IProgress<BandSweepProgress>? progress,
    CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(tuner);
    ArgumentNullException.ThrowIfNull(channels);
    if (samplesPerMeasurement < ChannelPowerMeter.FftSize)
    {
      throw new ArgumentOutOfRangeException(nameof(samplesPerMeasurement),
        $"Must be at least {ChannelPowerMeter.FftSize} samples (one FFT frame).");
    }

    IqSample[] buffer = new IqSample[samplesPerMeasurement];
    List<ChannelLevel> levels = new(channels.Count);

    for (int i = 0; i < channels.Count; i++)
    {
      ct.ThrowIfCancellationRequested();
      long frequencyHz = channels[i];

      if (MeasureChannel(tuner, frequencyHz, buffer, ct) is float level)
      {
        levels.Add(new ChannelLevel(frequencyHz, level));
      }

      progress?.Report(new BandSweepProgress(i + 1, channels.Count));
    }

    return levels;
  }

  private static float? MeasureChannel(ISweepTuner tuner, long frequencyHz, IqSample[] buffer, CancellationToken ct)
  {
    if (!tuner.Tune(frequencyHz))
    {
      Logger.Debug("Band sweep: tune to {FrequencyHz} Hz failed; channel skipped", frequencyHz);
      return null;
    }

    // Settling read: the first block after a retune can contain samples
    // captured before the tuner moved.
    int discarded = tuner.Read(buffer, ct);
    if (discarded < buffer.Length)
    {
      Logger.Debug("Band sweep: short settling read at {FrequencyHz} Hz ({Count}/{Expected}); channel skipped",
        frequencyHz, discarded, buffer.Length);
      return null;
    }

    ct.ThrowIfCancellationRequested();
    int read = tuner.Read(buffer, ct);
    if (read < buffer.Length)
    {
      Logger.Debug("Band sweep: short measurement read at {FrequencyHz} Hz ({Count}/{Expected}); channel skipped",
        frequencyHz, read, buffer.Length);
      return null;
    }

    return ChannelPowerMeter.MeasureDbfs(buffer, tuner.SampleRate);
  }
}
