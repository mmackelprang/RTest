using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore.Sweep;

/// <summary>A measured channel level produced by a sweep.</summary>
/// <param name="FrequencyHz">Channel centre, in Hz.</param>
/// <param name="LevelDbfs">
/// In-channel level from <see cref="ChannelPowerMeter.MeasureDbfs"/> or
/// <see cref="ChannelPowerMeter.MeasureChannelsDbfs"/>, in dB.
/// </param>
public sealed record ChannelLevel(long FrequencyHz, float LevelDbfs);

/// <summary>Progress of a running sweep.</summary>
/// <param name="ChannelsDone">Channels processed so far, including ones that could not be measured.</param>
/// <param name="ChannelsTotal">Channels in the sweep.</param>
public sealed record BandSweepProgress(int ChannelsDone, int ChannelsTotal);

/// <summary>
/// Steps a tuner across a channel list or a <see cref="BandSweepPlan"/> and measures each channel's level.
/// Pure logic over <see cref="ISweepTuner"/>: it owns no device and reads no clock.
/// </summary>
public static class BandSweeper
{
  private static readonly ILogger Logger = Log.ForContext(typeof(BandSweeper));

  /// <summary>Default number of IQ samples measured per channel.</summary>
  public const int DefaultSamplesPerMeasurement = 16384;

  /// <summary>
  /// Granularity of <c>samplesPerMeasurement</c>, in IQ samples: 256 samples
  /// are 512 bytes, the USB bulk-packet size that librtlsdr's own tools
  /// require read lengths to be a multiple of.
  /// </summary>
  public const int SamplesPerMeasurementMultiple = 256;

  /// <summary>
  /// True when <paramref name="samplesPerMeasurement"/> is at least one FFT
  /// frame (<see cref="ChannelPowerMeter.FftSize"/>) and a multiple of
  /// <see cref="SamplesPerMeasurementMultiple"/>.
  /// </summary>
  /// <param name="samplesPerMeasurement">The value to check.</param>
  public static bool IsValidSamplesPerMeasurement(int samplesPerMeasurement) =>
    samplesPerMeasurement >= ChannelPowerMeter.FftSize
    && samplesPerMeasurement % SamplesPerMeasurementMultiple == 0;

  /// <summary>Throws when <see cref="IsValidSamplesPerMeasurement"/> is false.</summary>
  /// <param name="samplesPerMeasurement">The value to check.</param>
  /// <exception cref="ArgumentOutOfRangeException">The value is not valid.</exception>
  public static void ThrowIfInvalidSamplesPerMeasurement(int samplesPerMeasurement)
  {
    if (!IsValidSamplesPerMeasurement(samplesPerMeasurement))
    {
      throw new ArgumentOutOfRangeException(nameof(samplesPerMeasurement), samplesPerMeasurement,
        $"Must be at least {ChannelPowerMeter.FftSize} samples (one FFT frame) and a multiple of {SamplesPerMeasurementMultiple}.");
    }
  }

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
    ThrowIfInvalidSamplesPerMeasurement(samplesPerMeasurement);

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

  /// <summary>
  /// Sweeps <paramref name="plan"/> tune by tune (AUD-91). For each tune: tune to its centre,
  /// read one block and discard it, read one more block and measure every channel of the tune
  /// from it.
  /// </summary>
  /// <remarks>
  /// A tune with exactly one channel at offset 0 (every FM tune) is measured with
  /// <see cref="ChannelPowerMeter.MeasureDbfs"/> using the plan's window; any other tune with
  /// <see cref="ChannelPowerMeter.MeasureChannelsDbfs"/>.
  /// </remarks>
  /// <param name="tuner">The tuner to drive.</param>
  /// <param name="plan">The band's sweep plan.</param>
  /// <param name="samplesPerMeasurement">Samples per discard read and per measurement read.</param>
  /// <param name="progress">
  /// Receives a report after each tune, measured or not, carrying the number of channels in the
  /// tunes processed so far. For a plan of one channel per tune that is one report per channel.
  /// </param>
  /// <param name="ct">Cancels the sweep.</param>
  /// <returns>
  /// One level per channel that was measured, ascending. The channels of a tune whose retune
  /// failed or whose reads came back short are left out.
  /// </returns>
  /// <exception cref="InvalidOperationException">
  /// <c>tuner.SampleRate</c> is lower than <see cref="BandSweepPlan.SampleRate"/>. Checked before
  /// the tuner is touched.
  /// </exception>
  /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
  public static IReadOnlyList<ChannelLevel> Sweep(
    ISweepTuner tuner,
    BandSweepPlan plan,
    int samplesPerMeasurement,
    IProgress<BandSweepProgress>? progress,
    CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(tuner);
    ArgumentNullException.ThrowIfNull(plan);
    ThrowIfInvalidSamplesPerMeasurement(samplesPerMeasurement);
    ThrowIfSampleRateTooLow(tuner.SampleRate, plan);

    IqSample[] buffer = new IqSample[samplesPerMeasurement];
    List<ChannelLevel> levels = new(plan.Channels.Count);
    int channelsDone = 0;

    foreach (SweepTune tune in plan.Tunes)
    {
      ct.ThrowIfCancellationRequested();

      if (ReadTune(tuner, tune, buffer, ct))
      {
        if (tune.ChannelHz.Count == 1 && tune.ChannelHz[0] == tune.CentreHz)
        {
          float level = ChannelPowerMeter.MeasureDbfs(buffer, tuner.SampleRate, plan.HalfWindowHz, plan.DcExcludeHz);
          levels.Add(new ChannelLevel(tune.CentreHz, level));
        }
        else
        {
          long[] offsets = tune.ChannelHz.Select(hz => hz - tune.CentreHz).ToArray();
          float[] tuneLevels = ChannelPowerMeter.MeasureChannelsDbfs(
            buffer, tuner.SampleRate, offsets, plan.HalfWindowHz, plan.DcExcludeHz);
          for (int i = 0; i < tuneLevels.Length; i++)
          {
            levels.Add(new ChannelLevel(tune.ChannelHz[i], tuneLevels[i]));
          }
        }
      }

      channelsDone += tune.ChannelHz.Count;
      progress?.Report(new BandSweepProgress(channelsDone, plan.Channels.Count));
    }

    return levels;
  }

  /// <summary>
  /// Throws when <paramref name="sampleRate"/> is lower than the rate <paramref name="plan"/>
  /// was designed for: its channel windows would fall outside the captured bandwidth.
  /// </summary>
  /// <exception cref="InvalidOperationException">The rate is too low.</exception>
  public static void ThrowIfSampleRateTooLow(int sampleRate, BandSweepPlan plan)
  {
    ArgumentNullException.ThrowIfNull(plan);
    if (sampleRate < plan.SampleRate)
    {
      throw new InvalidOperationException(
        $"The {plan.Band} sweep plan needs at least {plan.SampleRate} samples/s; the tuner runs at {sampleRate}.");
    }
  }

  /// <summary>
  /// Tunes to <paramref name="tune"/>'s centre, discards one block and reads one into
  /// <paramref name="buffer"/>. False when the retune failed or a read came back short.
  /// </summary>
  private static bool ReadTune(ISweepTuner tuner, SweepTune tune, IqSample[] buffer, CancellationToken ct)
  {
    if (!tuner.Tune(tune.CentreHz))
    {
      Logger.Debug("Band sweep: tune to {FrequencyHz} Hz failed; {Count} channel(s) skipped",
        tune.CentreHz, tune.ChannelHz.Count);
      return false;
    }

    int discarded = tuner.Read(buffer, ct);
    if (discarded < buffer.Length)
    {
      Logger.Debug("Band sweep: short settling read at {FrequencyHz} Hz ({Count}/{Expected}); {Channels} channel(s) skipped",
        tune.CentreHz, discarded, buffer.Length, tune.ChannelHz.Count);
      return false;
    }

    ct.ThrowIfCancellationRequested();
    int read = tuner.Read(buffer, ct);
    if (read < buffer.Length)
    {
      Logger.Debug("Band sweep: short measurement read at {FrequencyHz} Hz ({Count}/{Expected}); {Channels} channel(s) skipped",
        tune.CentreHz, read, buffer.Length, tune.ChannelHz.Count);
      return false;
    }

    return true;
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
