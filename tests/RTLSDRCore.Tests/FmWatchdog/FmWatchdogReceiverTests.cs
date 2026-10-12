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
      Harness.Receiver.FmWatchdog.LockedBlocksRequired = 2;   // the window is 3 blocks here, not 44
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

    /// <summary>
    /// Runs blocks one at a time and, after each, waits for every watchdog task queued so far, so a
    /// recovery step always finishes before the next block is raised (the fake device raises blocks
    /// whether or not it is streaming). The wait is a rendezvous; its 10 s is only a fail-safe.
    /// </summary>
    public void Run(int blocks)
    {
      for (int i = 0; i < blocks; i++)
      {
        Harness.Run(1);
        Task? pending = Receiver.LastWatchdogTask;
        if (pending is not null)
        {
          Assert.True(pending.Wait(TimeSpan.FromSeconds(10)), "a watchdog task did not finish");
        }
      }
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
  public void StepOne_CyclesHfDirectSamplingOnAndOff()
  {
    // Review M1: the recorded mode already reads "off" on FM, so asking for "off" alone would be a
    // cached no-op; the step must actually run librtlsdr's HF-off path.
    using Fixture f = new(Stereo);
    f.Run(4);
    Assert.True(f.Receiver.SetBand(BandType.AM, AmHz));
    f.Run(2);
    f.OnAir(Mono);
    Assert.True(f.Receiver.SetBand(BandType.FM, FmHz));
    int before = f.Harness.Device.Calls.Count;

    f.Run(4);

    Assert.Equal(FmWatchdogAction.RerunBandSetup, f.Snapshot()[^1].Action);
    string[] hf = f.Harness.Device.Calls.Skip(before).Where(c => c.StartsWith("SetHfMode:", StringComparison.Ordinal)).ToArray();
    Assert.Equal(new[] { "SetHfMode:True", "SetHfMode:False" }, hf);
  }

  [Fact]
  public void ARecoveryStep_IsSkipped_WhenTheReceiverWasRetunedSinceTheMiss()
  {
    // Review M2: a step queued behind a person's preset press must not interrupt the new station.
    using Fixture f = new(Stereo);
    f.Run(4);
    int stops = f.CallCount("StopStreaming");

    f.Receiver.RecoverFm(reopen: false, step: 1, FmHz, f.Receiver.TuneGeneration - 1);
    Assert.Equal(stops, f.CallCount("StopStreaming"));

    f.Receiver.RecoverFm(reopen: false, step: 1, FmHz + 200_000, f.Receiver.TuneGeneration);
    Assert.Equal(stops, f.CallCount("StopStreaming"));

    f.Receiver.RecoverFm(reopen: false, step: 1, FmHz, f.Receiver.TuneGeneration);
    Assert.Equal(stops + 1, f.CallCount("StopStreaming"));
  }

  [Fact]
  public void ABlockQueuedBeforeABandChange_DoesNotTurnTheStartIntoARetune()
  {
    // Review H1: a block raised on the old tune and processed after the new chain is published must
    // not open the start window, or the start's reason and its outcome line are lost.
    using Fixture f = new(Stereo);
    f.Run(4);
    int startsBefore = f.Snapshot().Count(o => o.Kind == FmAcquisitionKind.Start);

    f.Harness.DspGate.Reset();
    f.Harness.RaiseOnly();          // processed; the DSP thread is then held in its audio event
    f.Harness.WaitForEvents(1);
    f.Harness.RaiseOnly();          // queued, stamped with the old generation
    Assert.True(f.Receiver.SetBand(BandType.FM, FmHz));   // a new chain, published while that block waits
    f.Harness.DspGate.Set();
    f.Harness.WaitForEvents(1);
    f.Run(4);

    FmAcquisitionOutcome[] starts = f.Snapshot().Where(o => o.Kind == FmAcquisitionKind.Start).ToArray();
    Assert.Equal(startsBefore + 1, starts.Length);
    Assert.Equal("band change", starts[^1].Reason);
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
