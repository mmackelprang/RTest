using FluentAssertions;
using Radio.Web.Formatting;
using Radio.Web.Models;

namespace Radio.Web.Tests.Formatting;

/// <summary>
/// Unit tests for <see cref="PlaybackProgressProjection"/>. The helper is the
/// single source of truth for the elapsed / total / percent / display-string
/// math shared between <c>NowPlayingPanel</c> and <c>NowPlayingDock</c> (Arc 3
/// PR C item #14). Tests pin the contract — sub-second floor, em-dash for
/// unknown totals, percent clamp at 100, NowPlayingDto / PlaybackStateDto
/// overloads round-trip cleanly.
/// </summary>
public class PlaybackProgressProjectionTests
{
  // ─── Core From(double, double) contract ─────────────────────────────────

  [Fact]
  public void From_BothZero_ReturnsEmptyStateShape()
  {
    var p = PlaybackProgressProjection.From(0, 0);

    p.ElapsedSeconds.Should().Be(0);
    p.TotalSeconds.Should().Be(0);
    p.Percent.Should().Be(0);
    p.PercentDisplay.Should().Be("0%");
    p.ElapsedDisplay.Should().Be("0:00");
    // Em-dash placeholder when the total is unknown / zero.
    p.TotalDisplay.Should().Be("—");
  }

  [Fact]
  public void From_ThirtySecondsOf180_RendersHalfMinuteAtSixteenPercent()
  {
    var p = PlaybackProgressProjection.From(30, 180);

    p.ElapsedSeconds.Should().Be(30);
    p.TotalSeconds.Should().Be(180);
    // 30/180 * 100 ≈ 16.666… — the raw Percent stays full-precision so the
    // progress-bar width animates smoothly.
    p.Percent.Should().BeApproximately(16.666, 0.01);
    // PercentDisplay rounds to a whole number for the on-screen readout.
    p.PercentDisplay.Should().Be("17%");
    p.ElapsedDisplay.Should().Be("0:30");
    p.TotalDisplay.Should().Be("3:00");
  }

  // ─── PercentDisplay rounding (bug fix) ──────────────────────────────────
  //
  // Position/duration sources (Bluetooth especially) produce a percent with a
  // long decimal tail — e.g. 100/209 → 47.8468…. The raw Percent must stay
  // full-precision for the bar width, but PercentDisplay must be a rounded,
  // whole-number string so the UI never shows "47.8468899521%".

  [Fact]
  public void From_FractionalPercent_PercentDisplayRoundsToWholeNumber()
  {
    // 100 / 209 * 100 = 47.8468899521… — a realistic BT position/duration ratio.
    var p = PlaybackProgressProjection.From(100, 209);

    // Raw value keeps its precision for the bar width.
    p.Percent.Should().BeApproximately(47.8468, 0.001);
    // Displayed value is rounded to a whole number with the unit, no decimals.
    p.PercentDisplay.Should().Be("48%");
    p.PercentDisplay.Should().NotContain(".");
  }

  [Fact]
  public void From_AnyInput_PercentDisplayNeverContainsADecimalPoint()
  {
    // Sweep a spread of elapsed/total pairs; none may leak a decimal tail into
    // the human-readable percent.
    var samples = new (double elapsed, double total)[]
    {
      (1, 3), (2, 7), (5, 6), (100, 209), (17, 60), (0.4, 180), (200, 180), (45, 0), (0, 0)
    };

    foreach (var (elapsed, total) in samples)
    {
      var p = PlaybackProgressProjection.From(elapsed, total);
      p.PercentDisplay.Should().NotContain(".",
        $"PercentDisplay for {elapsed}/{total} must be a whole number");
      p.PercentDisplay.Should().EndWith("%");
    }
  }

  [Fact]
  public void From_SubSecondElapsed_FloorsToZeroColonZero()
  {
    // Mirrors NowPlayingPanel + NowPlayingDock — a fresh scrub-to-zero or
    // start-of-track render must not flash em-dash in the elapsed cell.
    var p = PlaybackProgressProjection.From(0.4, 180);

    p.ElapsedDisplay.Should().Be("0:00");
  }

  [Fact]
  public void From_OverflowElapsed_ClampsPercentAt100()
  {
    // Tail-of-track skew or off-by-frame seek event must clamp the percent so
    // the progress bar can't render past full-width.
    var p = PlaybackProgressProjection.From(200, 180);

    p.Percent.Should().Be(100);
    p.PercentDisplay.Should().Be("100%");
    // Display strings still reflect the raw elapsed value so the skew is
    // visible to UAT rather than silently truncated.
    p.ElapsedDisplay.Should().Be("3:20");
    p.TotalDisplay.Should().Be("3:00");
  }

  [Fact]
  public void From_NegativeElapsed_ClampsToZero()
  {
    var p = PlaybackProgressProjection.From(-5, 180);
    p.ElapsedSeconds.Should().Be(0);
    p.ElapsedDisplay.Should().Be("0:00");
    p.Percent.Should().Be(0);
  }

  [Fact]
  public void From_ZeroTotal_PercentIsZeroAndTotalIsEmDash()
  {
    // Live / streaming sources (BT, Radio) have no track-total — the
    // progress bar stays at 0 and the total cell renders em-dash.
    var p = PlaybackProgressProjection.From(45, 0);

    p.Percent.Should().Be(0);
    p.TotalDisplay.Should().Be("—");
    p.ElapsedDisplay.Should().Be("0:45");
  }

  // ─── NowPlayingDto overload ─────────────────────────────────────────────

  [Fact]
  public void From_NullNowPlayingDto_ReturnsEmptyState()
  {
    var p = PlaybackProgressProjection.From((NowPlayingDto?)null);
    p.ElapsedDisplay.Should().Be("0:00");
    p.TotalDisplay.Should().Be("—");
    p.Percent.Should().Be(0);
  }

  [Fact]
  public void From_NowPlayingDtoWithPositionAndDuration_ProjectsCleanly()
  {
    var dto = new NowPlayingDto
    {
      Position = TimeSpan.FromSeconds(60),
      Duration = TimeSpan.FromSeconds(180),
    };
    var p = PlaybackProgressProjection.From(dto);
    p.ElapsedDisplay.Should().Be("1:00");
    p.TotalDisplay.Should().Be("3:00");
    p.Percent.Should().BeApproximately(33.333, 0.01);
  }

  [Fact]
  public void From_NowPlayingDtoWithNullPositionAndDuration_FallsBackToZero()
  {
    var dto = new NowPlayingDto { Position = null, Duration = null };
    var p = PlaybackProgressProjection.From(dto);
    p.ElapsedDisplay.Should().Be("0:00");
    p.TotalDisplay.Should().Be("—");
  }

  // ─── PlaybackStateDto overload ──────────────────────────────────────────

  [Fact]
  public void From_NullPlaybackStateDto_ReturnsEmptyState()
  {
    var p = PlaybackProgressProjection.From((PlaybackStateDto?)null);
    p.ElapsedDisplay.Should().Be("0:00");
    p.TotalDisplay.Should().Be("—");
  }

  [Fact]
  public void From_PlaybackStateDtoWithParsableStrings_ProjectsCleanly()
  {
    var state = MakePlaybackState(position: "00:00:45", duration: "00:03:00");
    var p = PlaybackProgressProjection.From(state);
    p.ElapsedDisplay.Should().Be("0:45");
    p.TotalDisplay.Should().Be("3:00");
    p.Percent.Should().Be(25);
  }

  [Fact]
  public void From_PlaybackStateDtoWithUnparsableStrings_FallsBackToZero()
  {
    var state = MakePlaybackState(position: "garbage", duration: null);
    var p = PlaybackProgressProjection.From(state);
    p.ElapsedDisplay.Should().Be("0:00");
    p.TotalDisplay.Should().Be("—");
    p.Percent.Should().Be(0);
  }

  [Fact]
  public void From_PlaybackStateDtoWithHourLongDuration_RendersHoursColumn()
  {
    var state = MakePlaybackState(position: "01:00:00", duration: "02:30:00");
    var p = PlaybackProgressProjection.From(state);
    p.ElapsedDisplay.Should().Be("1:00:00");
    p.TotalDisplay.Should().Be("2:30:00");
    p.Percent.Should().BeApproximately(40, 0.01);
  }

  // ─── Helpers ────────────────────────────────────────────────────────────

  private static PlaybackStateDto MakePlaybackState(string? position, string? duration) =>
    new(
      IsPlaying: true,
      IsPaused: false,
      Volume: 0.75f,
      IsMuted: false,
      Balance: 0,
      Position: position,
      Duration: duration,
      CanPlay: true,
      CanPause: true,
      CanStop: true,
      CanSeek: true,
      CanNext: true,
      CanPrevious: true,
      CanShuffle: true,
      CanRepeat: true,
      CanQueue: true,
      CanReorderQueue: true,
      IsShuffleEnabled: false,
      RepeatMode: "Off");
}
