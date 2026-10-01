using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>AUD-91: <see cref="BandSweeper"/> over a <see cref="BandSweepPlan"/>.</summary>
public class BandSweeperPlanTests
{
  private const int SamplesPerMeasurement = 16384;
  private const long WeatherStationHz = 162_475_000;
  private const long AirStationHz = 118_300_000;

  private sealed class RecordingProgress : IProgress<BandSweepProgress>
  {
    public List<BandSweepProgress> Reports { get; } = new();

    public void Report(BandSweepProgress value) => Reports.Add(value);
  }

  /// <summary>
  /// Serves, at any tuned centre, a strong tone for every station within the capture at its offset
  /// from the centre, over weak noise. Records every call in order.
  /// </summary>
  private sealed class StationTuner : ISweepTuner
  {
    private long _tunedHz;
    private int _reads;

    public StationTuner(params long[] stations)
    {
      Stations = stations;
    }

    public long[] Stations { get; }

    public List<string> Calls { get; } = new();

    public int SampleRate { get; init; } = SweepTestSignals.SampleRate;

    /// <summary>Returns the count for a read, given the 1-based read number; null fills the buffer.</summary>
    public Func<int, int?> ReadCount { get; init; } = _ => null;

    public Func<long, bool> TuneResult { get; init; } = _ => true;

    public bool Tune(long frequencyHz)
    {
      Calls.Add($"Tune:{frequencyHz}");
      _tunedHz = frequencyHz;
      return TuneResult(frequencyHz);
    }

    public int Read(Span<IqSample> buffer, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      _reads++;
      Calls.Add($"Read:{_tunedHz}");
      IqSample[] block = SweepTestSignals.Noise(buffer.Length, 0.01f, seed: _reads);
      foreach (long station in Stations)
      {
        long offset = station - _tunedHz;
        if (Math.Abs(offset) < SampleRate / 2)
        {
          block = SweepTestSignals.Add(block, SweepTestSignals.Tone(buffer.Length, offset, 0.5f, SampleRate));
        }
      }
      block.CopyTo(buffer);
      int count = ReadCount(_reads) ?? buffer.Length;
      return Math.Min(count, buffer.Length);
    }
  }

  [Fact]
  public void Weather_OneTune_PeakAtTheStationChannel()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Weather, null)!;
    StationTuner tuner = new(WeatherStationHz);

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(new[] { "Tune:162487500", "Read:162487500", "Read:162487500" }, tuner.Calls);
    Assert.Equal(plan.Channels, levels.Select(l => l.FrequencyHz));
    ChannelLevel peak = levels.MaxBy(l => l.LevelDbfs)!;
    Assert.Equal(WeatherStationHz, peak.FrequencyHz);
    Assert.All(levels.Where(l => l.FrequencyHz != WeatherStationHz),
      l => Assert.True(l.LevelDbfs < peak.LevelDbfs - 20f, $"{l.FrequencyHz} Hz at {l.LevelDbfs:F1} dB"));
  }

  [Fact]
  public void Aircraft_TunesEveryCentreInOrder_AndFindsTheStation()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Aircraft, null)!;
    StationTuner tuner = new(AirStationHz);

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(
      plan.Tunes.Select(t => $"Tune:{t.CentreHz}"),
      tuner.Calls.Where(c => c.StartsWith("Tune:", StringComparison.Ordinal)));
    Assert.Equal(1161, levels.Count);
    Assert.Equal(AirStationHz, levels.MaxBy(l => l.LevelDbfs)!.FrequencyHz);
  }

  [Fact]
  public void Progress_IsReportedOncePerTune_AsCumulativeChannels()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.VHF, 146_520_000)!;
    StationTuner tuner = new();
    RecordingProgress progress = new();

    BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, progress, CancellationToken.None);

    Assert.Equal(11, progress.Reports.Count);
    int expected = 0;
    for (int t = 0; t < plan.Tunes.Count; t++)
    {
      expected += plan.Tunes[t].ChannelHz.Count;
      Assert.Equal(new BandSweepProgress(expected, 161), progress.Reports[t]);
    }
  }

  [Fact]
  public void Fm_Plan_ReportsProgressPerChannel_AndMeasuresExactlyLikeTheChannelListSweep()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.FM, null)!;
    RecordingProgress progress = new();

    IReadOnlyList<ChannelLevel> byPlan = BandSweeper.Sweep(
      new StationTuner(99_500_000), plan, SamplesPerMeasurement, progress, CancellationToken.None);
    IReadOnlyList<ChannelLevel> byList = BandSweeper.Sweep(
      new StationTuner(99_500_000), FmChannelPlan.Channels, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(101, progress.Reports.Count);
    Assert.Equal(new BandSweepProgress(1, 101), progress.Reports[0]);
    Assert.Equal(byList, byPlan);
  }

  [Fact]
  public void ShortReadOnOneTune_SkipsOnlyThatTunesChannels()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.VHF, 146_520_000)!;
    // Reads come in pairs per tune: read 4 is the second tune's measurement read.
    StationTuner tuner = new() { ReadCount = read => read == 4 ? 100 : null };
    RecordingProgress progress = new();

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, progress, CancellationToken.None);

    Assert.Equal(plan.Channels.Except(plan.Tunes[1].ChannelHz), levels.Select(l => l.FrequencyHz));
    Assert.Equal(new BandSweepProgress(161, 161), progress.Reports[^1]);
  }

  [Fact]
  public void FailedTune_SkipsOnlyThatTunesChannels()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Aircraft, null)!;
    long failing = plan.Tunes[2].CentreHz;
    StationTuner tuner = new() { TuneResult = hz => hz != failing };

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(plan.Channels.Except(plan.Tunes[2].ChannelHz), levels.Select(l => l.FrequencyHz));
    Assert.DoesNotContain($"Read:{failing}", tuner.Calls);
  }

  [Fact]
  public void TunerSlowerThanThePlansDesignRate_ThrowsBeforeTuning()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Weather, null)!;
    StationTuner tuner = new() { SampleRate = 120_000 };

    Assert.Throws<InvalidOperationException>(() =>
      BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, null, CancellationToken.None));

    Assert.Empty(tuner.Calls);
  }

  [Fact]
  public void Cancelled_StopsBetweenTunes()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Aircraft, null)!;
    using CancellationTokenSource cts = new();
    StationTuner tuner = new();
    RecordingProgress progress = new();
    IProgress<BandSweepProgress> cancelAfterThird = new SyncProgress(p =>
    {
      progress.Report(p);
      if (progress.Reports.Count == 3)
      {
        cts.Cancel();
      }
    });

    Assert.ThrowsAny<OperationCanceledException>(() =>
      BandSweeper.Sweep(tuner, plan, SamplesPerMeasurement, cancelAfterThird, cts.Token));

    Assert.Equal(3, tuner.Calls.Count(c => c.StartsWith("Tune:", StringComparison.Ordinal)));
  }

  private sealed class SyncProgress : IProgress<BandSweepProgress>
  {
    private readonly Action<BandSweepProgress> _onReport;

    public SyncProgress(Action<BandSweepProgress> onReport)
    {
      _onReport = onReport;
    }

    public void Report(BandSweepProgress value) => _onReport(value);
  }
}
