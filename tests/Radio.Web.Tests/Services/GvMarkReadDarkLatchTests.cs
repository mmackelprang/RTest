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

  // ⚠ This is NOT a timing test and cannot be weakened by a slow or saturated runner
  // (CLAUDE.md § Test Timing). There is no clock and no sleep: Interlocked.Exchange guarantees
  // exactly one caller observes the 0→1 transition under EVERY interleaving, so starvation can
  // only reorder the winners, never produce two of them. It exists because the property the row
  // asks for — "log once" — is a concurrency claim, and a check-then-set implementation would
  // pass every test above while failing this one.
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
