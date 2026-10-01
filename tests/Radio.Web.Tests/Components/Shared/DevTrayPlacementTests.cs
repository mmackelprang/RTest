using FluentAssertions;
using Radio.Web.Components.Shared;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// UI-25: the DevTray opens under the point that was tapped, clamped into the viewport with a
/// margin, and flips above the tap when there is no room below. The kiosk is 1920x720.
/// </summary>
public class DevTrayPlacementTests
{
  private const double W = DevTrayPlacement.WidthPx;
  private const double H = DevTrayPlacement.MaxHeightPx;
  private const double M = DevTrayPlacement.MarginPx;
  private const double G = DevTrayPlacement.GapPx;

  [Fact]
  public void TapInTheTopbar_OpensBelowTheTap_CentredOnIt()
  {
    var p = DevTrayPlacement.ForTap(1200, 30, 1920, 720);

    p.Left.Should().Be(1200 - W / 2);
    p.Top.Should().Be(30 + G);
    p.Bottom.Should().BeNull();
    p.OpensAbove.Should().BeFalse();
  }

  [Fact]
  public void TwoDifferentTapPoints_GiveTwoDifferentPlacements()
  {
    // The owner's check: tap in two places, the tray follows.
    var a = DevTrayPlacement.ForTap(1150, 20, 1920, 720);
    var b = DevTrayPlacement.ForTap(1210, 50, 1920, 720);

    b.Left.Should().Be(a.Left + 60);
    b.Top.Should().Be(a.Top + 30);
  }

  [Fact]
  public void TapNearTheLeftEdge_ClampsToTheMargin()
  {
    DevTrayPlacement.ForTap(40, 30, 1920, 720).Left.Should().Be(M);
  }

  [Fact]
  public void TapNearTheRightEdge_ClampsSoTheTrayEndsAtTheMargin()
  {
    DevTrayPlacement.ForTap(1900, 30, 1920, 720).Left.Should().Be(1920 - M - W);
  }

  [Fact]
  public void TapLowOnTheScreen_FlipsAbove_WithTheTraysBottomEdgeJustAboveTheTap()
  {
    var p = DevTrayPlacement.ForTap(900, 650, 1920, 720);

    p.OpensAbove.Should().BeTrue();
    p.Top.Should().BeNull();
    // bottom offset from the viewport's bottom edge = 720 − (650 − gap)
    p.Bottom.Should().Be(720 - (650 - G));
  }

  [Fact]
  public void ExactlyEnoughRoomBelow_StaysBelow()
  {
    // below + H == vh − margin is the boundary; it must still open below.
    var tapY = 720 - M - H - G;
    var p = DevTrayPlacement.ForTap(900, tapY, 1920, 720);

    p.OpensAbove.Should().BeFalse();
    p.Top.Should().Be(tapY + G);
  }

  [Fact]
  public void NoRoomOnEitherSide_StaysOnScreen()
  {
    // 500 px tall: neither 440 px below nor above a mid-screen tap fits.
    var p = DevTrayPlacement.ForTap(900, 250, 1920, 500);

    p.OpensAbove.Should().BeFalse();
    p.Top.Should().Be(500 - M - H);
    (p.Top!.Value + H).Should().BeLessThanOrEqualTo(500 - M);
  }

  [Fact]
  public void ViewportNarrowerThanTheTray_KeepsTheLeftMargin()
  {
    DevTrayPlacement.ForTap(200, 30, 400, 720).Left.Should().Be(M);
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(double.NaN, double.NaN)]
  [InlineData(-1, 720)]
  public void NonsenseViewport_FallsBackToTheKioskPanel(double vw, double vh)
  {
    var p = DevTrayPlacement.ForTap(1900, 30, vw, vh);

    p.Left.Should().Be(1920 - M - W);
    p.Top.Should().Be(30 + G);
  }
}
