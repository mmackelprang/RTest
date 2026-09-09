using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>
/// GV-12 § the health predicate. The cases that matter are the ABSENT ones: this predicate
/// gates a recovery EDGE, so a term that can never clear silently disables the whole row.
/// </summary>
public class GvBridgeHealthTests
{
  private static readonly DateTimeOffset Now =
    new(2026, 9, 8, 15, 31, 17, TimeSpan.Zero);

  [Fact]
  public void NullStatus_IsUnhealthy()
  {
    Assert.False(GvBridgeHealth.IsHealthy(null, Now));
  }

  [Fact]
  public void TheOutageCapture_IsUnhealthy()
  {
    // The live capture from docs/queue/GV-12.md:33-36, verbatim. degraded and authBlackout
    // both false through a TOTAL outage — this is the case the retracted morning guidance
    // would have missed, and the reason Available is a term at all.
    var status = new GvBridgeStatusDto
    {
      Available = false,
      Degraded = false,
      AuthBlackout = false,
      CookiesValid = false,
      LastApiSuccessAt = null
    };

    Assert.False(GvBridgeHealth.IsHealthy(status, Now));
  }

  [Fact]
  public void AvailableWithEveryOtherFieldAbsent_IsHealthy()
  {
    // ⚠⚠ THE LOAD-BEARING TEST. This is what RotaryPhone serves on a build that has not
    // deployed the new fields, and it is the shape the response has TODAY. If this ever
    // asserts false, the predicate can never reach healthy on that build, the recovery edge
    // never fires, and GV-12 ships as a silent no-op with a green suite. Plan §0.4.
    var status = new GvBridgeStatusDto { Available = true, ActiveMode = "GoogleVoice" };

    Assert.True(GvBridgeHealth.IsHealthy(status, Now));
  }

  [Fact]
  public void PresentAndBad_IsUnhealthy()
  {
    Assert.False(GvBridgeHealth.IsHealthy(
      new GvBridgeStatusDto { Available = true, CookiesValid = false }, Now));
    Assert.False(GvBridgeHealth.IsHealthy(
      new GvBridgeStatusDto { Available = true, Degraded = true }, Now));
    Assert.False(GvBridgeHealth.IsHealthy(
      new GvBridgeStatusDto { Available = true, AuthBlackout = true }, Now));
  }

  [Fact]
  public void StaleLastSuccess_IsUnhealthy_ButAbsentIsNot()
  {
    var stale = new GvBridgeStatusDto
    {
      Available = true,
      LastApiSuccessAt = Now.UtcDateTime.AddMinutes(-3)
    };
    Assert.False(GvBridgeHealth.IsHealthy(stale, Now));

    var fresh = new GvBridgeStatusDto
    {
      Available = true,
      LastApiSuccessAt = Now.UtcDateTime.AddSeconds(-30)
    };
    Assert.True(GvBridgeHealth.IsHealthy(fresh, Now));

    // Absent — deliberately healthy, departing from docs/queue/GV-12.md:46. Plan §0.4.
    var absent = new GvBridgeStatusDto { Available = true, LastApiSuccessAt = null };
    Assert.True(GvBridgeHealth.IsHealthy(absent, Now));
  }
}
