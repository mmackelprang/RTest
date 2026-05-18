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
}
