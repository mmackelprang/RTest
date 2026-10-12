using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Tests.AmChain;

namespace RTLSDRCore.Tests.FmWatchdog;

/// <summary>
/// AUD-106 through the whole <see cref="RadioReceiver"/>: a fake device streaming synthetic FM, one block
/// at a time (<see cref="ReceiverHarness"/>), with each recovery step awaited through
/// <c>RadioReceiver.LastWatchdogTask</c>. Counted, not timed.
/// </summary>
public class FmWatchdogReceiverTests
{
  private const long FmHz = 99_500_000;
  private const long AmHz = 1_000_000;

  /// <summary>Three 16384-sample blocks: the window the tests use (the default is 3 s, 44 blocks).</summary>
  private const long Window = 3 * 16_384;

  private static Transmitter Stereo => new(FmHz, 0.5f, ToneHz: 1_000, FmDeviationHz: 50_000, PilotDeviationHz: 7_500);

  private static Transmitter Mono => new(FmHz, 0.5f, ToneHz: 1_000, FmDeviationHz: 50_000);

  private sealed class Fixture : IDisposable
  {
    public Fixture(Transmitter station)
    {
      Rf = new SyntheticRf();
      Rf.Transmitters.Add(station);
      Harness = new ReceiverHarness(Rf, BandType.FM, FmHz);
      Harness.Receiver.FmWatchdog.WindowSamples = Window;
      Harness.Receiver.FmAcquisitionReported += o =>
      {
        lock (Outcomes)
        {
          Outcomes.Add(o);
        }
      };
    }

    public SyntheticRf Rf { get; }

    public ReceiverHarness Harness { get; }

    public RadioReceiver Receiver => Harness.Receiver;

    public List<FmAcquisitionOutcome> Outcomes { get; } = new();

    public FmAcquisitionOutcome[] Snapshot()
    {
      lock (Outcomes)
      {
        return Outcomes.ToArray();
      }
    }

    /// <summary>Runs blocks one at a time, then waits for every watchdog task they queued.</summary>
    public void Run(int blocks)
    {
      Harness.Run(blocks);
      Receiver.LastWatchdogTask?.Wait(TimeSpan.FromSeconds(10));
    }

    public void OnAir(Transmitter station)
    {
      Rf.Transmitters.Clear();
      Rf.Transmitters.Add(station);
    }

    public int CallCount(string call) => Harness.Device.Calls.Count(c => c == call);

    public void Dispose() => Harness.Dispose();
  }

  [Fact]
  public void StereoStation_StartOutcome_IsReportedHealthy_AndTheFrequencyBecomesKnown()
  {
    using Fixture f = new(Stereo);
    f.Run(4);

    FmAcquisitionOutcome start = Assert.Single(f.Snapshot());
    Assert.Equal(FmAcquisitionKind.Start, start.Kind);
    Assert.Equal("start-up", start.Reason);
    Assert.NotNull(start.PilotAfterSamples);
    Assert.Equal(FmWatchdogAction.ReportOutcome, start.Action);
    Assert.True(f.Receiver.FmWatchdog.IsKnownLocked(FmHz));
  }

  [Fact]
  public void PilotLostAfterAnAmReturn_OnAKnownStation_RerunsSetup_ThenReopens_ThenGivesUp_AndNeverLoops()
  {
    using Fixture f = new(Stereo);
    f.Run(4);   // healthy start: the station is known

    Assert.True(f.Receiver.SetBand(BandType.AM, AmHz));
    f.Run(2);
    f.OnAir(Mono);   // the fault: IQ flows, but no pilot (and no RDS) after the return to FM
    int stopsBefore = f.CallCount("StopStreaming");
    int closesBefore = f.CallCount("Close");

    Assert.True(f.Receiver.SetBand(BandType.FM, FmHz));
    f.Run(4);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, f.Snapshot()[^1].Action);
    Assert.Equal("band change", f.Snapshot()[^1].Reason);
    Assert.True(f.CallCount("StopStreaming") > stopsBefore + 1, "step 1 did not pause streaming");
    Assert.Equal(closesBefore, f.CallCount("Close"));

    f.Run(4);
    Assert.Equal(FmWatchdogAction.ReopenDevice, f.Snapshot()[^1].Action);
    Assert.Equal("watchdog step 1: band setup re-run", f.Snapshot()[^1].Reason);
    Assert.Equal(closesBefore + 1, f.CallCount("Close"));

    f.Run(4);
    Assert.Equal(FmWatchdogAction.GiveUp, f.Snapshot()[^1].Action);
    Assert.Equal("watchdog step 2: device reopened", f.Snapshot()[^1].Reason);

    int actionsBefore = f.Snapshot().Count(o => o.Action is not FmWatchdogAction.ReportOutcome);
    int stopsAfterGiveUp = f.CallCount("StopStreaming");
    f.Run(12);
    Assert.Equal(actionsBefore, f.Snapshot().Count(o => o.Action is not FmWatchdogAction.ReportOutcome));
    Assert.Equal(stopsAfterGiveUp, f.CallCount("StopStreaming"));
    Assert.True(f.Receiver.IsRunning);
  }

  [Fact]
  public void PilotBackAfterStepOne_EndsTheEpisode_WithNoFurtherStep()
  {
    using Fixture f = new(Stereo);
    f.Run(4);
    Assert.True(f.Receiver.SetBand(BandType.AM, AmHz));
    f.Run(2);
    f.OnAir(Mono);
    Assert.True(f.Receiver.SetBand(BandType.FM, FmHz));
    f.Run(4);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, f.Snapshot()[^1].Action);

    f.OnAir(Stereo);   // the re-run fixed it
    f.Run(4);

    FmAcquisitionOutcome last = f.Snapshot()[^1];
    Assert.Equal("watchdog step 1: band setup re-run", last.Reason);
    Assert.False(last.Missed);
    Assert.Equal(0, f.Receiver.FmWatchdog.EpisodeStep);
    int closes = f.CallCount("Close");
    f.Run(8);
    Assert.Equal(closes, f.CallCount("Close"));
  }

  [Fact]
  public void GenuinelyMonoStation_IsReportedButNeverRecovered()
  {
    using Fixture f = new(Mono);
    f.Run(4);
    Assert.True(f.Receiver.SetBand(BandType.AM, AmHz));
    f.Run(2);
    Assert.True(f.Receiver.SetBand(BandType.FM, FmHz));
    f.Run(8);

    FmAcquisitionOutcome[] outcomes = f.Snapshot();
    Assert.Equal(2, outcomes.Length);
    Assert.All(outcomes, o =>
    {
      Assert.True(o.Missed);
      Assert.Equal(FmWatchdogAction.ReportOutcome, o.Action);
    });
    Assert.Equal(0, f.CallCount("Close"));
  }

  [Fact]
  public void AnAmStart_IsNotWatched()
  {
    using Fixture f = new(Stereo);
    f.Run(4);
    Assert.True(f.Receiver.SetBand(BandType.AM, AmHz));
    f.Run(8);

    Assert.Single(f.Snapshot());   // only the FM start-up
  }
}
