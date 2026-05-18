using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Tests for <see cref="RdsStationNameStabilityTracker"/> — the helper that
/// filters rolling-PS artifacts before exposing a "stable" station name
/// (PR D #40 of the Arc follow-up backlog).
/// </summary>
public class RdsStationNameStabilityTrackerTests
{
  [Fact]
  public void Stable_StartsNull()
  {
    var tracker = new RdsStationNameStabilityTracker();

    Assert.Null(tracker.Stable);
  }

  [Fact]
  public void Observe_BelowWindow_DoesNotEmitStable()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    var first = tracker.Observe("Rock 92");
    var second = tracker.Observe("Rock 92");

    Assert.Null(first);
    Assert.Null(second);
  }

  [Fact]
  public void Observe_WindowOfIdenticalSamples_PromotesToStable()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");
    var stable = tracker.Observe("Rock 92");

    Assert.Equal("Rock 92", stable);
    Assert.Equal("Rock 92", tracker.Stable);
  }

  [Fact]
  public void Observe_MidRollFragments_NeverPromote_UntilConsensus()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    // Rolling PS cycles: each frame is a different fragment.
    tracker.Observe("Rock 92 ");
    tracker.Observe("anoidRoc");
    tracker.Observe("k92 PaR");

    Assert.Null(tracker.Stable);

    // Now three identical frames in a row — consensus emerges.
    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");

    Assert.Equal("Rock 92", tracker.Stable);
  }

  [Fact]
  public void Observe_BreakingConsensus_KeepsPreviousStableUntilNewConsensus()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    // Reach consensus on first station.
    tracker.Observe("WCPE");
    tracker.Observe("WCPE");
    tracker.Observe("WCPE");
    Assert.Equal("WCPE", tracker.Stable);

    // Now a couple of transient frames — stable should hold.
    tracker.Observe("WUNC");
    tracker.Observe("transient");
    Assert.Equal("WCPE", tracker.Stable);

    // New consensus takes over once 3-in-a-row of "WUNC" arrives.
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void Observe_NullOrWhitespace_DoesNotPromoteStableNull(string? value)
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    // First reach a stable value.
    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");
    Assert.Equal("Rock 92", tracker.Stable);

    // Now flood with null/empty — stable should hold the previous consensus
    // rather than transitioning to null.
    tracker.Observe(value);
    tracker.Observe(value);
    tracker.Observe(value);

    Assert.Equal("Rock 92", tracker.Stable);
  }

  [Fact]
  public void Reset_ClearsStable()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");
    tracker.Observe("Rock 92");
    Assert.Equal("Rock 92", tracker.Stable);

    tracker.Reset();

    Assert.Null(tracker.Stable);
  }

  [Fact]
  public void Constructor_NonPositiveWindowSize_Throws()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new RdsStationNameStabilityTracker(windowSize: 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new RdsStationNameStabilityTracker(windowSize: -1));
  }

  // Task #80: prove that reading the Stable property is purely a read —
  // it must not consume window samples. Otherwise the SDR source's old
  // bug (Observe called from the property getter) could sneak back in
  // disguised as "reading Stable accidentally feeds the tracker".
  [Fact]
  public void Stable_RepeatedReads_DoNotConsumeWindowSamples()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    // Feed only 2 samples — not enough to promote on their own.
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    Assert.Null(tracker.Stable);

    // Now read Stable a bunch of times. If the getter were observing,
    // each read of `null` (Stable) would have no effect, but if some
    // future regression had it observe the receiver's current PS, we'd
    // see consensus form here without a real third sample. Either way,
    // pure reads must not mutate state.
    for (int i = 0; i < 100; i++)
    {
      _ = tracker.Stable;
    }

    Assert.Null(tracker.Stable);

    // One real third sample completes the window — proves nothing in
    // the window was consumed by the previous reads.
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);
  }

  // Task #80: prove that the tracker behaves like an event-driven feed
  // (observe each PS frame) under a rolling-PS scenario where the
  // station ID gets re-observed multiple times between roll fragments.
  // This is the scenario the broken integration was failing: at 2 Hz
  // polling, "WSMW THE" was sampled 3 times in a row during one 1.5s
  // roll. At ~10 Hz with mid-roll fragments interleaved, no 3-in-a-row
  // of a fragment forms unless the fragment dominates the window.
  [Fact]
  public void Observe_RollingPsAtFrameRate_DoesNotPromoteMidRollFragment()
  {
    var tracker = new RdsStationNameStabilityTracker(windowSize: 3);

    // Simulate a rolling PS station at ~10 Hz frame rate where each
    // fragment lasts ~2 frames before being replaced. None of the
    // mid-roll fragments should ever fill the 3-window.
    string[] frames =
    {
      "WSMW THE",
      "WSMW THE",
      " CARS  ",
      " CARS  ",
      "SHAKE IT",
      "SHAKE IT",
      "  UP    ",
      "  UP    ",
      "WSMW THE",
      "WSMW THE",
    };

    foreach (var frame in frames)
    {
      tracker.Observe(frame);
    }

    Assert.Null(tracker.Stable);
  }
}
