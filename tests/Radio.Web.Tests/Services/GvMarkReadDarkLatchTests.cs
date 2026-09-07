using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class GvMarkReadDarkLatchTests
{
  [Fact]
  public void StartsUnlatched()
  {
    Assert.False(new GvMarkReadDarkLatch().IsLatched);
  }

  [Fact]
  public void TryLatch_ReturnsTrueOnce_ThenFalse_AndStaysLatched()
  {
    var latch = new GvMarkReadDarkLatch();

    Assert.True(latch.TryLatch());
    Assert.False(latch.TryLatch());
    Assert.False(latch.TryLatch());
    Assert.True(latch.IsLatched);
  }

  // This test can never FAIL spuriously (CLAUDE.md § Test Timing): no clock, no sleep, and
  // Interlocked.Exchange guarantees exactly one caller observes the 0→1 transition under every
  // interleaving, so a saturated runner can only reorder the winners, never produce two.
  // ⚠ It is a REGRESSION GUARD for a documented property, NOT a detector. It does not reliably
  // catch a check-then-set implementation: measured against a naive `if (_b) return false;
  // _b = true;` latch, this exact body found two winners 0 times in 500 runs on a 32-core box
  // (a Barrier-gated 16-thread variant: 4 in 200). Nothing here forces the callers into the
  // nanosecond race window together. The property is guaranteed by Interlocked, not demonstrated
  // by this assertion — the service-level guard is covered by
  // Dark409_TwoCircuitsRacingTheFirstPost_StillLogOneWarningTotal.
  [Fact]
  public void TryLatch_GrantsExactlyOneWinner_UnderParallelCallers()
  {
    var latch = new GvMarkReadDarkLatch();
    var winners = 0;

    Parallel.For(0, 256, _ =>
    {
      if (latch.TryLatch())
      {
        Interlocked.Increment(ref winners);
      }
    });

    Assert.Equal(1, winners);
  }
}
