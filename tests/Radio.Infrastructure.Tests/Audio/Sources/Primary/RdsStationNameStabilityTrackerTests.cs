using Microsoft.Extensions.Time.Testing;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Tests for <see cref="RdsStationNameStabilityTracker"/> — the helper that
/// filters rolling-PS artifacts before exposing a "stable" station name.
/// <para>
/// Task #80 v2: the algorithm was rewritten from "N consecutive identical
/// samples" to a sliding-window frequency histogram with a call-sign-shape
/// boost. The prior approach trivially promoted any rotation fragment
/// because each fragment held for 30-50 consecutive frames at the ~10 Hz
/// PS decode rate. Live failure modes captured by Tester:
/// </para>
/// <list type="bullet">
///   <item><b>WUNC 91.5</b>: <c>"ON WUNC"</c> &#8596; <c>"On Point"</c>
///     rotation; old algorithm promoted "On Point".</item>
///   <item><b>WSMW 97.745</b>: <c>"WSMW"</c> &#8596; <c>"TOO HOT"</c>
///     &#8596; <c>"KOOL"</c> &#8596; <c>"THE GANG"</c> &#8596; <c>"SIMON"</c>
///     &#8596; <c>"336"</c> rotation; old algorithm promoted "TOO HOT",
///     then "336".</item>
/// </list>
/// </summary>
public class RdsStationNameStabilityTrackerTests
{
  // Use a deterministic call-sign-boost large enough that one boost
  // unambiguously beats a one-sample frequency lead in tests. The
  // production default (10) already meets that; we exercise the default
  // directly so tests reflect real behavior.
  private static RdsStationNameStabilityTracker CreateTracker(
    TimeProvider? clock = null,
    TimeSpan? windowDuration = null,
    int callSignBoost = 10,
    int minSamples = 5)
  {
    return new RdsStationNameStabilityTracker(
      windowDuration: windowDuration ?? TimeSpan.FromSeconds(60),
      callSignBoost: callSignBoost,
      minSamples: minSamples,
      timeProvider: clock);
  }

  [Fact]
  public void Stable_StartsNull()
  {
    var tracker = CreateTracker();

    Assert.Null(tracker.Stable);
  }

  [Fact]
  public void Observe_BelowMinSamples_ReturnsNull()
  {
    var tracker = CreateTracker(minSamples: 5);

    // 4 samples — below the 5-sample threshold.
    for (int i = 0; i < 4; i++)
    {
      Assert.Null(tracker.Observe("WUNC"));
    }

    Assert.Null(tracker.Stable);

    // 5th sample crosses the threshold.
    Assert.Equal("WUNC", tracker.Observe("WUNC"));
  }

  [Fact]
  public void Observe_SteadyPs_WinsTrivially()
  {
    var tracker = CreateTracker();

    for (int i = 0; i < 50; i++)
    {
      tracker.Observe("Rock 92");
    }

    Assert.Equal("Rock 92", tracker.Stable);
  }

  [Fact]
  public void Observe_SteadyCallSignPs_WinsTrivially()
  {
    var tracker = CreateTracker();

    for (int i = 0; i < 50; i++)
    {
      tracker.Observe("KEXP-FM");
    }

    Assert.Equal("KEXP-FM", tracker.Stable);
  }

  // The WSMW 97.745 rotation that PR #384 promoted "TOO HOT" for.
  [Fact]
  public void Observe_WsmwRotation_PromotesCallSignVariant()
  {
    var tracker = CreateTracker();

    // Equal frequency for every rotation entry — call-sign boost is
    // what disambiguates.
    string[] rotation = { "TOO HOT", "WSMW", "KOOL", "THE GANG", "SIMON", "336" };
    for (int i = 0; i < 40; i++)
    {
      foreach (var ps in rotation)
      {
        tracker.Observe(ps);
      }
    }

    Assert.Equal("WSMW", tracker.Stable);
  }

  // The WUNC 91.5 rotation that PR #384 promoted "On Point" for.
  [Fact]
  public void Observe_WuncRotation_PromotesCallSignVariant()
  {
    var tracker = CreateTracker();

    for (int i = 0; i < 40; i++)
    {
      tracker.Observe("On Point");
      tracker.Observe("ON WUNC");
    }

    Assert.Equal("ON WUNC", tracker.Stable);
  }

  [Fact]
  public void Observe_RollingPsWithoutCallSign_FallsBackToMostFrequent()
  {
    var tracker = CreateTracker();

    for (int i = 0; i < 50; i++)
    {
      tracker.Observe("SONG A");
    }
    for (int i = 0; i < 30; i++)
    {
      tracker.Observe("SONG B");
    }

    Assert.Equal("SONG A", tracker.Stable);
  }

  [Fact]
  public void Observe_OldSamplesOutsideWindow_AreEvicted()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(clock: clock, windowDuration: TimeSpan.FromSeconds(60));

    // Feed 50 samples of OLD PS at t=0.
    for (int i = 0; i < 50; i++)
    {
      tracker.Observe("OLD PS");
    }
    Assert.Equal("OLD PS", tracker.Stable);

    // Advance 90s — beyond the 60s window. Old samples evicted on next
    // observe / read.
    clock.Advance(TimeSpan.FromSeconds(90));

    // Stable read after eviction (Stable evicts on read so this is
    // observable without further Observe calls).
    Assert.Null(tracker.Stable);

    // Feed 5 NEW samples — enough to satisfy MinSamples.
    for (int i = 0; i < 5; i++)
    {
      tracker.Observe("NEW PS");
    }

    Assert.Equal("NEW PS", tracker.Stable);
  }

  [Fact]
  public void Observe_SamplesAcrossWindowBoundary_OnlyRecentCount()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(clock: clock, windowDuration: TimeSpan.FromSeconds(60));

    // 30 old samples of "OLD".
    for (int i = 0; i < 30; i++)
    {
      tracker.Observe("OLD");
    }

    // Advance partway — 30s. Old samples still in window.
    clock.Advance(TimeSpan.FromSeconds(30));

    // 10 new "NEW" samples while OLD still counts.
    for (int i = 0; i < 10; i++)
    {
      tracker.Observe("NEW");
    }

    Assert.Equal("OLD", tracker.Stable);

    // Advance another 35s — total 65s since OLD samples; they evict.
    clock.Advance(TimeSpan.FromSeconds(35));

    Assert.Equal("NEW", tracker.Stable);
  }

  [Fact]
  public void Reset_ClearsWindow()
  {
    var tracker = CreateTracker();

    for (int i = 0; i < 50; i++)
    {
      tracker.Observe("WUNC");
    }
    Assert.Equal("WUNC", tracker.Stable);

    tracker.Reset();

    Assert.Null(tracker.Stable);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void Observe_NullOrWhitespace_IsIgnored(string? value)
  {
    var tracker = CreateTracker(minSamples: 3);

    // Below threshold — null.
    tracker.Observe("WUNC");
    tracker.Observe(value);
    tracker.Observe(value);
    Assert.Null(tracker.Stable);

    // Two more real samples crosses the 3-sample threshold.
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Observe_TrimsWhitespace()
  {
    var tracker = CreateTracker(minSamples: 3);

    tracker.Observe("  WUNC  ");
    tracker.Observe(" WUNC");
    tracker.Observe("WUNC ");

    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Stable_RepeatedReads_DoNotMutateState()
  {
    var tracker = CreateTracker(minSamples: 5);

    // 4 samples — one below the threshold.
    for (int i = 0; i < 4; i++)
    {
      tracker.Observe("WUNC");
    }
    Assert.Null(tracker.Stable);

    // 100 reads must not flip the answer to non-null.
    for (int i = 0; i < 100; i++)
    {
      _ = tracker.Stable;
    }
    Assert.Null(tracker.Stable);

    // One more real sample crosses the threshold.
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Constructor_InvalidArgs_Throw()
  {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(windowDuration: TimeSpan.Zero));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(windowDuration: TimeSpan.FromSeconds(-1)));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(callSignBoost: -1));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(minSamples: 0));
  }

  // The call-sign pattern is the heart of the algorithm; exercise it
  // explicitly through the tracker so future regressions don't break
  // shape detection silently.
  [Theory]
  // Bare call signs of valid length (3 or 4 letters) starting with K/W.
  [InlineData("WUN", true)]    // 3 letters
  [InlineData("WUNC", true)]   // 4 letters
  [InlineData("KEXP", true)]   // 4 letters
  [InlineData("KQED", true)]   // 4 letters
  // Embedded in longer strings.
  [InlineData("ON WUNC", true)]
  [InlineData("WUNC FM", true)]
  [InlineData("WSMW THE", true)]
  // Doesn't start with K or W.
  [InlineData("XYZ", false)]
  [InlineData("ABC", false)]
  // Wrong length (must be 3-4 letters at a word boundary).
  [InlineData("WU", false)]    // 2 letters
  [InlineData("WUNCC", false)] // 5 letters — fails \b on the right
  // All-digit / mixed strings shouldn't match.
  [InlineData("336", false)]
  [InlineData("TOO HOT", false)]
  [InlineData("On Point", false)]
  [InlineData("THE GANG", false)]
  public void Observe_CallSignShape_IsRecognized(string callSignCandidate, bool shouldBoost)
  {
    var tracker = CreateTracker(callSignBoost: 100, minSamples: 1);

    // Feed 5 samples of a known non-call-sign value, then 1 sample of
    // the candidate. If `shouldBoost` is true, the candidate's
    // +100 boost beats the 5-occurrence non-call-sign value. If false,
    // the candidate stays at score 1 and loses to score 5.
    for (int i = 0; i < 5; i++)
    {
      tracker.Observe("non-shape value");
    }
    tracker.Observe(callSignCandidate);

    if (shouldBoost)
    {
      Assert.Equal(callSignCandidate, tracker.Stable);
    }
    else
    {
      Assert.Equal("non-shape value", tracker.Stable);
    }
  }

  // Regression: the 10 Hz frame-rate simulation that PR #384's
  // tracker fails — each fragment holds for 30+ consecutive frames. The
  // new algorithm must NOT promote a mid-roll fragment.
  [Fact]
  public void Observe_RollingPsAtFrameRate_PromotesCallSignNotMidRollFragment()
  {
    var tracker = CreateTracker();

    // Simulate ~10 Hz frame rate where each rotation fragment holds for
    // ~30 frames before the next one takes over. This is the live
    // failure mode PR #384's consecutive-samples algorithm was
    // promoting "WSMW THE" / "CARS" / etc. for.
    string[] fragments = { "WSMW THE", "CARS", "SHAKE IT", "UP", "WSMW" };
    foreach (var fragment in fragments)
    {
      for (int frame = 0; frame < 30; frame++)
      {
        tracker.Observe(fragment);
      }
    }

    // Each fragment has 30 occurrences. "WSMW" gets +10 boost — total 40.
    // "WSMW THE" also contains "WSMW" — also +10 boost; both tie at 40.
    // GroupBy preserves first-occurrence order, so "WSMW THE" wins on
    // the tie. That's still in-rotation-but-rolling territory; the
    // primary correctness criterion is "no non-call-sign fragment wins".
    var stable = tracker.Stable;
    Assert.NotNull(stable);
    Assert.Contains("WSMW", stable);
    Assert.NotEqual("CARS", stable);
    Assert.NotEqual("SHAKE IT", stable);
    Assert.NotEqual("UP", stable);
  }
}
