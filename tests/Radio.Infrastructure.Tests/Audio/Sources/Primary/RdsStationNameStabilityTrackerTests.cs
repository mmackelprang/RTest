using Microsoft.Extensions.Time.Testing;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Tests for <see cref="RdsStationNameStabilityTracker"/> — the helper that
/// filters rolling-PS artifacts before exposing a "stable" station name.
/// <para>
/// Task #80 v3: the algorithm was rewritten from a 60s sliding-window
/// histogram (v2) to a lock-and-hold pattern. Live UAT of v2 revealed
/// two failure modes:
/// </para>
/// <list type="bullet">
///   <item><b>WSMW 97.745</b>: broadcasts SIMON / 98.7 / 336 — never
///     "WSMW". v2's call-sign-shape boost can't help when the call sign
///     isn't in the data.</item>
///   <item><b>WUNC 91.5</b>: v2 stabilized correctly during T+15-50s but
///     drifted as lock-time samples aged out of the sliding window.</item>
/// </list>
/// <para>
/// v3: accumulate samples for 30s after first observation; lock the
/// best candidate (histogram + call-sign tiebreaker); never update
/// again until Reset (called on frequency change).
/// </para>
/// </summary>
public class RdsStationNameStabilityTrackerTests
{
  // Use a deterministic call-sign-boost large enough that one boost
  // unambiguously beats a one-sample frequency lead in tests. The
  // production default (10) already meets that; we exercise the default
  // directly so tests reflect real behavior.
  private static RdsStationNameStabilityTracker CreateTracker(
    TimeProvider? clock = null,
    TimeSpan? lockAfter = null,
    int callSignBoost = 10,
    int minSamples = 5)
  {
    return new RdsStationNameStabilityTracker(
      lockAfter: lockAfter ?? TimeSpan.FromSeconds(30),
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
  public void Stable_BeforeMinSamples_ReturnsNull()
  {
    var tracker = CreateTracker(minSamples: 5);

    // 3 samples — below MinSamples and lockAfter not yet reached.
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");

    Assert.Null(tracker.Stable);
  }

  [Fact]
  public void Stable_BeforeLockTime_ReturnsTransientBest()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(clock: clock, minSamples: 5);

    // MinSamples reached before lockAfter elapses — Stable returns the
    // transient best-effort consensus so the UI can show *something*.
    for (int i = 0; i < 5; i++)
    {
      tracker.Observe("WUNC");
    }
    // Only 0s elapsed; lockAfter (30s) not yet reached.
    Assert.Equal("WUNC", tracker.Stable);

    // Advance partway — still pre-lock.
    clock.Advance(TimeSpan.FromSeconds(10));
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Lock_HappensAt30s_WithSufficientSamples()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // Feed 50 samples spread across 35 seconds — every sample is "WUNC"
    // so the histogram unambiguously promotes it at lock time.
    for (int i = 0; i < 50; i++)
    {
      tracker.Observe("WUNC");
      clock.Advance(TimeSpan.FromSeconds(0.7)); // ~35s total
    }

    // After 30s + ≥ MinSamples, the tracker is locked.
    Assert.Equal("WUNC", tracker.Stable);

    // Drown the tracker in a different value — locked, so no change.
    for (int i = 0; i < 1000; i++)
    {
      tracker.Observe("OTHER");
    }
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Lock_DoesNotHappenBeforeMinSamples_EvenIfTimeElapsed()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // 2 samples, then advance the clock past lockAfter. Still no lock —
    // MinSamples not met. Stable returns null (below MinSamples).
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    clock.Advance(TimeSpan.FromSeconds(60));
    Assert.Null(tracker.Stable);

    // 3rd and 4th samples — still below MinSamples=5.
    tracker.Observe("WUNC");
    tracker.Observe("WUNC");
    Assert.Null(tracker.Stable);

    // 5th sample crosses MinSamples — and lockAfter has elapsed — so
    // this sample triggers the lock.
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);

    // Confirm locked: subsequent observations are no-ops.
    tracker.Observe("OTHER");
    tracker.Observe("OTHER");
    tracker.Observe("OTHER");
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Observe_AfterLock_IsNoOp()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // Lock on "WUNC".
    for (int i = 0; i < 10; i++)
    {
      tracker.Observe("WUNC");
    }
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.Observe("WUNC"); // 11th sample triggers the lock check
    Assert.Equal("WUNC", tracker.Stable);

    // Drown the tracker in another value — must remain "WUNC".
    for (int i = 0; i < 1000; i++)
    {
      tracker.Observe("On Point");
    }
    Assert.Equal("WUNC", tracker.Stable);
  }

  [Fact]
  public void Reset_ClearsLock_AndAllowsRelock()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // Lock on "WUNC".
    for (int i = 0; i < 10; i++)
    {
      tracker.Observe("WUNC");
    }
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);

    // Tune to a new station.
    tracker.Reset();
    Assert.Null(tracker.Stable);

    // Observe new station samples — lock on "WSMW" after a fresh window.
    for (int i = 0; i < 10; i++)
    {
      tracker.Observe("WSMW");
    }
    // The fresh _firstSampleTime is "now" (clock has advanced past 31s).
    // Advance another 30s to satisfy lockAfter for the new window.
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.Observe("WSMW");
    Assert.Equal("WSMW", tracker.Stable);
  }

  // Regression: WUNC rotation observed in Tester's UAT. Both "WUNC" and
  // "ON WUNC" contain the call sign, both get +10 boost. The one with
  // higher count wins.
  [Fact]
  public void Observe_WuncLockWindow_PromotesCallSign()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // Lock window: WUNC ×10, On Point ×8, ON WUNC ×5.
    // Scores: WUNC=20, On Point=8, ON WUNC=15. WUNC wins.
    for (int i = 0; i < 10; i++) tracker.Observe("WUNC");
    for (int i = 0; i < 8; i++)  tracker.Observe("On Point");
    for (int i = 0; i < 5; i++)  tracker.Observe("ON WUNC");

    clock.Advance(TimeSpan.FromSeconds(31));
    // Trigger lock check via one more sample.
    tracker.Observe("WUNC");

    var stable = tracker.Stable;
    Assert.NotNull(stable);
    // Both "WUNC" and "ON WUNC" contain WUNC; spec accepts either as a
    // pass since both convey the station identity. WUNC has the higher
    // count so it wins here, but the regression criterion is "contains
    // the call sign".
    Assert.Contains("WUNC", stable);
    Assert.NotEqual("On Point", stable);
  }

  // Regression: WSMW rotation that broke v2. WSMW broadcasts both its
  // call sign AND non-call-sign rotation fragments. Spec example:
  // WSMW ×8, 98.7 ×10, SIMON ×12. With +10 boost: WSMW=18, 98.7=10,
  // SIMON=12. WSMW wins.
  [Fact]
  public void Observe_WsmwLockWindow_PromotesCallSign()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    for (int i = 0; i < 8; i++)  tracker.Observe("WSMW");
    for (int i = 0; i < 10; i++) tracker.Observe("98.7");
    for (int i = 0; i < 12; i++) tracker.Observe("SIMON");

    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.Observe("WSMW");

    Assert.Equal("WSMW", tracker.Stable);
  }

  // Regression: the exact failure scenario from v2 live UAT. Lock with
  // "WUNC" at T+30s, then observe 1000 rotation-fragment samples that
  // would have aged out the call-sign samples in v2's 60s window.
  // v3 must keep "WUNC" indefinitely.
  [Fact]
  public void Observe_PostLock_DriftSamples_DoNotChange_Stable()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // Lock window mimics WUNC: call sign present.
    for (int i = 0; i < 10; i++) tracker.Observe("WUNC");
    for (int i = 0; i < 8; i++)  tracker.Observe("On Point");
    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.Observe("WUNC");
    Assert.Equal("WUNC", tracker.Stable);

    // Now the rotation drifts post-lock. v2 would have aged out the
    // WUNC samples and promoted whichever fragment was most frequent in
    // the trailing window. v3 must hold WUNC.
    string[] driftFragments = { "On Point", "Indira", "Lakshman", "On Point" };
    for (int i = 0; i < 1000; i++)
    {
      tracker.Observe(driftFragments[i % driftFragments.Length]);
      clock.Advance(TimeSpan.FromSeconds(0.1));
    }

    Assert.Equal("WUNC", tracker.Stable);
  }

  // Regression: a station that broadcasts only non-call-sign values.
  // Lock the most-frequent value (the v2 boost can't help; v3 still
  // promotes the most frequent).
  [Fact]
  public void Observe_LockWindowWithoutCallSign_PromotesMostFrequent()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(
      clock: clock,
      lockAfter: TimeSpan.FromSeconds(30),
      minSamples: 5);

    // Pure non-call-sign rotation: SIMON ×15, 98.7 ×10, 336 ×8.
    // No values get the +10 boost. SIMON (15) wins.
    for (int i = 0; i < 15; i++) tracker.Observe("SIMON");
    for (int i = 0; i < 10; i++) tracker.Observe("98.7");
    for (int i = 0; i < 8; i++)  tracker.Observe("336");

    clock.Advance(TimeSpan.FromSeconds(31));
    tracker.Observe("SIMON");

    Assert.Equal("SIMON", tracker.Stable);
  }

  [Fact]
  public void Reset_ClearsPreLockState()
  {
    var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var tracker = CreateTracker(clock: clock, minSamples: 5);

    // Pre-lock samples present.
    for (int i = 0; i < 10; i++) tracker.Observe("WUNC");
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

    // Two more real samples crosses the 3-sample threshold (transient
    // pre-lock consensus).
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
      () => new RdsStationNameStabilityTracker(lockAfter: TimeSpan.Zero));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(lockAfter: TimeSpan.FromSeconds(-1)));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(callSignBoost: -1));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new RdsStationNameStabilityTracker(minSamples: 0));
  }

  // The call-sign pattern is the heart of the tiebreaker; exercise it
  // explicitly through the tracker so future regressions don't break
  // shape detection silently. Test runs in pre-lock mode (transient
  // best-effort) so we don't need to advance a clock.
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
    // the candidate stays at score 1 and loses to score 5. Pre-lock
    // transient consensus exposes the winner without needing a clock
    // advance.
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
}
