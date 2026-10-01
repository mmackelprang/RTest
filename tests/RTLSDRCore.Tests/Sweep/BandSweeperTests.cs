using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

public class BandSweeperTests
{
  private const long StationHz = 99_500_000;
  private const int SamplesPerMeasurement = 16384;

  /// <summary>Synchronous IProgress so reports are observed in call order.</summary>
  private sealed class RecordingProgress : IProgress<BandSweepProgress>
  {
    public List<BandSweepProgress> Reports { get; } = new();

    public void Report(BandSweepProgress value) => Reports.Add(value);
  }

  /// <summary>
  /// Serves a strong in-band tone only when tuned to <see cref="StationHz"/>;
  /// weak noise everywhere else. Records every call in order.
  /// </summary>
  private sealed class FakeTuner : ISweepTuner
  {
    private long _tunedHz;

    public List<string> Calls { get; } = new();

    public Func<long, bool> TuneResult { get; set; } = _ => true;

    public Action<int>? OnTune { get; set; }

    public int SampleRate => SweepTestSignals.SampleRate;

    public int TuneCount { get; private set; }

    public bool Tune(long frequencyHz)
    {
      TuneCount++;
      Calls.Add($"Tune:{frequencyHz}");
      _tunedHz = frequencyHz;
      OnTune?.Invoke(TuneCount);
      return TuneResult(frequencyHz);
    }

    public int Read(Span<IqSample> buffer, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      Calls.Add($"Read:{_tunedHz}");
      IqSample[] noise = SweepTestSignals.Noise(buffer.Length, 0.01f, seed: (int)(_tunedHz / 100_000));
      IqSample[] block = _tunedHz == StationHz
        ? SweepTestSignals.Add(SweepTestSignals.Tone(buffer.Length, 40_000, 0.5f), noise)
        : noise;
      block.CopyTo(buffer);
      return buffer.Length;
    }
  }

  [Fact]
  public void Sweep_PeakIsAtTheStation()
  {
    FakeTuner tuner = new();

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(
      tuner, FmChannelPlan.Channels, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(FmChannelPlan.Channels.Count, levels.Count);
    ChannelLevel peak = levels.MaxBy(l => l.LevelDbfs)!;
    Assert.Equal(StationHz, peak.FrequencyHz);
  }

  [Fact]
  public void Sweep_DiscardsOneReadAfterEveryTune()
  {
    FakeTuner tuner = new();
    long[] channels = { 99_300_000, StationHz };

    BandSweeper.Sweep(tuner, channels, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.Equal(
      new[]
      {
        "Tune:99300000", "Read:99300000", "Read:99300000",
        "Tune:99500000", "Read:99500000", "Read:99500000",
      },
      tuner.Calls);
  }

  [Fact]
  public void Sweep_MeasuresTheSecondReadNotTheFirst()
  {
    // A tuner whose first read after each tune carries the tone of the
    // PREVIOUS station (pre-retune samples) and whose second read is clean.
    // If the sweeper measured the first read, 99.7 would look like a station.
    SettlingTuner tuner = new();
    long[] channels = { StationHz, 99_700_000 };

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(
      tuner, channels, SamplesPerMeasurement, null, CancellationToken.None);

    Assert.True(levels[0].LevelDbfs - levels[1].LevelDbfs > 20f,
      $"station {levels[0].LevelDbfs:F1} dB vs next {levels[1].LevelDbfs:F1} dB");
  }

  private sealed class SettlingTuner : ISweepTuner
  {
    private long _tunedHz;
    private long _previousHz;
    private int _readsSinceTune;

    public int SampleRate => SweepTestSignals.SampleRate;

    public bool Tune(long frequencyHz)
    {
      _previousHz = _tunedHz;
      _tunedHz = frequencyHz;
      _readsSinceTune = 0;
      return true;
    }

    public int Read(Span<IqSample> buffer, CancellationToken ct)
    {
      _readsSinceTune++;
      long effectiveHz = _readsSinceTune == 1 && _previousHz != 0 ? _previousHz : _tunedHz;
      IqSample[] noise = SweepTestSignals.Noise(buffer.Length, 0.01f);
      IqSample[] block = effectiveHz == StationHz
        ? SweepTestSignals.Add(SweepTestSignals.Tone(buffer.Length, 40_000, 0.5f), noise)
        : noise;
      block.CopyTo(buffer);
      return buffer.Length;
    }
  }

  [Fact]
  public void Sweep_CancelledMidway_ThrowsAndStopsTuning()
  {
    using CancellationTokenSource cts = new();
    FakeTuner tuner = new() { OnTune = count => { if (count == 5) { cts.Cancel(); } } };

    Assert.ThrowsAny<OperationCanceledException>(() =>
      BandSweeper.Sweep(tuner, FmChannelPlan.Channels, SamplesPerMeasurement, null, cts.Token));

    Assert.Equal(5, tuner.TuneCount);
  }

  [Fact]
  public void Sweep_ProgressReachesTotal()
  {
    FakeTuner tuner = new();
    RecordingProgress progress = new();

    BandSweeper.Sweep(tuner, FmChannelPlan.Channels, SamplesPerMeasurement, progress, CancellationToken.None);

    Assert.Equal(FmChannelPlan.Channels.Count, progress.Reports.Count);
    Assert.Equal(new BandSweepProgress(101, 101), progress.Reports[^1]);
    Assert.Equal(new BandSweepProgress(1, 101), progress.Reports[0]);
  }

  [Fact]
  public void Sweep_FailedTune_OmitsChannelButKeepsGoing()
  {
    FakeTuner tuner = new() { TuneResult = hz => hz != 99_300_000 };
    long[] channels = { 99_300_000, StationHz };

    IReadOnlyList<ChannelLevel> levels = BandSweeper.Sweep(
      tuner, channels, SamplesPerMeasurement, null, CancellationToken.None);

    ChannelLevel only = Assert.Single(levels);
    Assert.Equal(StationHz, only.FrequencyHz);
  }

  [Fact]
  public void FmChannelPlan_Has101OddTenthChannels()
  {
    Assert.Equal(101, FmChannelPlan.Channels.Count);
    Assert.Equal(87_900_000, FmChannelPlan.Channels[0]);
    Assert.Equal(107_900_000, FmChannelPlan.Channels[^1]);
    Assert.All(FmChannelPlan.Channels, hz => Assert.Equal(1, hz / 100_000 % 2));
  }
}
