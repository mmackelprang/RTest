using RTLSDRCore.DSP;

namespace RTLSDRCore.Tests.FmWatchdog;

/// <summary>
/// AUD-106: <see cref="FmAcquisitionWatchdog"/>'s decisions, driven block by block (counted in
/// samples, not timed). The receiver-level behaviour is in <see cref="FmWatchdogReceiverTests"/>.
/// </summary>
public class FmAcquisitionWatchdogTests
{
  private const long Station = 97_745_000;
  private const int Block = 1_000;

  private sealed class ManualClock : TimeProvider
  {
    public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
  }

  private static FmAcquisitionWatchdog NewWatchdog(ManualClock? clock = null) =>
    new() { WindowSamples = 3 * Block, Clock = clock ?? new ManualClock() };

  /// <summary>Feeds blocks until one closes the window; returns its outcome (null when none closed within 10 blocks).</summary>
  private static FmAcquisitionOutcome? Feed(FmAcquisitionWatchdog w, int generation, bool pilot, bool rds = false,
    float signal = 0.5f, long frequencyHz = Station, bool? pilotLocked = null)
  {
    for (int i = 0; i < 10; i++)
    {
      FmAcquisitionOutcome? outcome = w.Observe(generation, frequencyHz, Block, pilot, rds, signal, pilotLocked ?? pilot);
      if (outcome is not null)
      {
        return outcome;
      }
    }
    return null;
  }

  /// <summary>Makes <see cref="Station"/> known (a healthy start) and returns the next generation.</summary>
  private static int MakeKnown(FmAcquisitionWatchdog w)
  {
    w.BeginStart(Station, 1, "start-up", byRecovery: false);
    FmAcquisitionOutcome? healthy = Feed(w, 1, pilot: true, rds: true);
    Assert.Equal(FmWatchdogAction.ReportOutcome, healthy!.Action);
    Assert.False(healthy.Missed);
    Assert.True(w.IsKnownLocked(Station));
    return 2;
  }

  [Fact]
  public void HealthyStart_ReportsWhenBothArrive_WithoutWaitingForTheWindow()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    w.BeginStart(Station, 1, "start-up", byRecovery: false);

    Assert.Null(w.Observe(1, Station, Block, pilot: true, rdsSynced: false, signal: 0.5f, pilotLocked: true));
    FmAcquisitionOutcome? outcome = w.Observe(1, Station, Block, pilot: true, rdsSynced: true, signal: 0.5f, pilotLocked: true);

    Assert.NotNull(outcome);
    Assert.Equal(FmAcquisitionKind.Start, outcome.Kind);
    Assert.Equal(Block, outcome.PilotAfterSamples);
    Assert.Equal(2 * Block, outcome.RdsAfterSamples);
    Assert.Equal(FmWatchdogAction.ReportOutcome, outcome.Action);
  }

  [Fact]
  public void MonoStationWithoutRds_IsReported_NeverRecovered_HoweverOftenItIsSelected()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    for (int g = 1; g <= 5; g++)
    {
      w.BeginStart(Station, g, "band change", byRecovery: false);
      FmAcquisitionOutcome? outcome = Feed(w, g, pilot: false);

      Assert.Equal(FmWatchdogAction.ReportOutcome, outcome!.Action);
      Assert.True(outcome.Missed);
      Assert.False(outcome.KnownLocked);
    }
    Assert.Equal(0, w.EpisodeStep);
  }

  [Fact]
  public void KnownStation_MissAfterABandChange_EscalatesRerunThenReopenThenGivesUp_Once()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    int g = MakeKnown(w);

    w.BeginStart(Station, g, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, Feed(w, g, pilot: false)!.Action);

    w.BeginStart(Station, ++g, "watchdog step 1", byRecovery: true);
    Assert.Equal(FmWatchdogAction.ReopenDevice, Feed(w, g, pilot: false)!.Action);

    w.BeginStart(Station, ++g, "watchdog step 2", byRecovery: true);
    Assert.Equal(FmWatchdogAction.GiveUp, Feed(w, g, pilot: false)!.Action);

    // Never loops: further recovery starts and retunes on the same broken station ask for nothing.
    w.BeginStart(Station, ++g, "watchdog step 2", byRecovery: true);
    Assert.Equal(FmWatchdogAction.ReportOutcome, Feed(w, g, pilot: false)!.Action);
    Assert.Null(Feed(w, ++g, pilot: false));   // a retune: no verdict worth reporting, no action
  }

  [Fact]
  public void AHealthyAcquisition_EndsTheEpisode()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    int g = MakeKnown(w);
    w.BeginStart(Station, g, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, Feed(w, g, pilot: false)!.Action);

    w.BeginStart(Station, ++g, "watchdog step 1", byRecovery: true);
    Assert.Equal(FmWatchdogAction.ReportOutcome, Feed(w, g, pilot: true)!.Action);

    Assert.Equal(0, w.EpisodeStep);
  }

  [Fact]
  public void AWeakStation_IsNotRecovered()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    int g = MakeKnown(w);
    w.BeginStart(Station, g, "band change", byRecovery: false);

    FmAcquisitionOutcome? outcome = Feed(w, g, pilot: false, signal: 0.05f);

    Assert.Equal(FmWatchdogAction.ReportOutcome, outcome!.Action);
  }

  [Fact]
  public void ARetune_ClosesTheOpenWindowWithoutAVerdict()
  {
    // A seek passing through a known station never completes a window there.
    FmAcquisitionWatchdog w = NewWatchdog();
    int g = MakeKnown(w);
    for (int i = 0; i < 20; i++)
    {
      Assert.Null(w.Observe(g + i, Station, Block, pilot: false, rdsSynced: false, signal: 0.5f, pilotLocked: false));
      Assert.Null(w.Observe(g + i, Station, Block, pilot: false, rdsSynced: false, signal: 0.5f, pilotLocked: false));
    }
    Assert.Equal(0, w.EpisodeStep);
  }

  [Fact]
  public void SteadyListening_IsNotWatched_OnlyAcquisitions()
  {
    // A known station that loses its pilot after a healthy acquisition (a fade, or a mono programme)
    // is never judged again on the same tune.
    FmAcquisitionWatchdog w = NewWatchdog();
    MakeKnown(w);
    for (int i = 0; i < 50; i++)
    {
      Assert.Null(w.Observe(1, Station, Block, pilot: false, rdsSynced: false, signal: 0.5f, pilotLocked: false));
    }
  }

  [Fact]
  public void AKnownStationsRetuneMiss_TriggersToo()
  {
    // A retune (a new tune generation without a new chain, e.g. a seek stop) to a known station that
    // then shows neither pilot nor RDS is judged like a start.
    FmAcquisitionWatchdog w = NewWatchdog();
    int g = MakeKnown(w);

    FmAcquisitionOutcome? outcome = Feed(w, g, pilot: false);

    Assert.Equal(FmAcquisitionKind.Retune, outcome!.Kind);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, outcome.Action);
  }

  [Fact]
  public void AUserStartAfterGivingUp_OpensANewEpisode_WithinTheHourlyLimit()
  {
    ManualClock clock = new();
    FmAcquisitionWatchdog w = NewWatchdog(clock);
    w.MaxEpisodesPerHour = 2;
    int g = MakeKnown(w);

    // Episode 1: rerun, reopen, give up.
    w.BeginStart(Station, g, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, Feed(w, g, pilot: false)!.Action);
    w.BeginStart(Station, ++g, "watchdog step 1", byRecovery: true);
    Assert.Equal(FmWatchdogAction.ReopenDevice, Feed(w, g, pilot: false)!.Action);
    w.BeginStart(Station, ++g, "watchdog step 2", byRecovery: true);
    Assert.Equal(FmWatchdogAction.GiveUp, Feed(w, g, pilot: false)!.Action);

    // Episode 2, opened by the person selecting FM again.
    w.BeginStart(Station, ++g, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, Feed(w, g, pilot: false)!.Action);

    // A third episode in the same hour is refused.
    w.BeginStart(Station, ++g, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.GiveUp, Feed(w, g, pilot: false)!.Action);

    // An hour after the first episode, one more is allowed.
    clock.Now += TimeSpan.FromHours(1);
    w.BeginStart(Station, ++g, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.RerunBandSetup, Feed(w, g, pilot: false)!.Action);
  }

  [Fact]
  public void APilotThatIsNotLocked_DoesNotMakeAStationKnown()
  {
    // The stereo detector reads noise as a pilot (AUD-109): an empty channel "with a pilot" must not
    // become a station the watchdog later tries to recover.
    FmAcquisitionWatchdog w = NewWatchdog();
    w.BeginStart(Station, 1, "band change", byRecovery: false);
    FmAcquisitionOutcome? noisy = Feed(w, 1, pilot: true, pilotLocked: false);
    Assert.False(noisy!.Missed);
    Assert.False(w.IsKnownLocked(Station));

    w.BeginStart(Station, 2, "band change", byRecovery: false);
    Assert.Equal(FmWatchdogAction.ReportOutcome, Feed(w, 2, pilot: false)!.Action);
  }

  [Fact]
  public void ALockedPilotWithoutRds_MakesAStationKnown()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    w.BeginStart(Station, 1, "band change", byRecovery: false);
    Feed(w, 1, pilot: true, pilotLocked: true);

    Assert.True(w.IsKnownLocked(Station));
  }

  [Fact]
  public void KnownIsPerFrequency()
  {
    FmAcquisitionWatchdog w = NewWatchdog();
    int g = MakeKnown(w);

    w.BeginStart(94_700_000, g, "band change", byRecovery: false);
    FmAcquisitionOutcome? outcome = Feed(w, g, pilot: false, frequencyHz: 94_700_000);

    Assert.Equal(FmWatchdogAction.ReportOutcome, outcome!.Action);
    Assert.False(outcome.KnownLocked);
  }
}
