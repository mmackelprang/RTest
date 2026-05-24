using FluentAssertions;
using Radio.Web.Formatting;
using Radio.Web.Models;

namespace Radio.Web.Tests.Formatting;

/// <summary>
/// Unit tests for <see cref="Clocks.FormatWallClock(DateTime, DisplayOptions?, bool)"/>.
/// Pins the four format-matrix outputs (12h/24h × seconds on/off), the
/// <c>allowSeconds</c> override used by the queue ends-prediction, and the
/// null-options defensive fallback. AM/PM rendering is asserted in invariant
/// culture so the test passes regardless of host locale (handoff §7).
/// </summary>
public class ClocksTests
{
  // A fixed instant so AM/PM and HH:mm assertions stay deterministic.
  // 2024-03-08 15:45:22 local — covers PM half, two-digit hour in 24h,
  // single-digit hour in 12h, and non-zero seconds.
  private static readonly DateTime SamplePm = new(2024, 3, 8, 15, 45, 22);

  // Morning sample for AM coverage.
  private static readonly DateTime SampleAm = new(2024, 3, 8, 7, 5, 3);

  [Fact]
  public void FormatWallClock_DefaultOptions_Renders24HourNoSeconds()
  {
    // The DisplayOptions default — preserved from the pre-PR hardcoded "HH:mm"
    // behaviour so unconfigured kiosks render identically after deploy.
    var opts = new DisplayOptions();
    Clocks.FormatWallClock(SamplePm, opts).Should().Be("15:45");
  }

  [Fact]
  public void FormatWallClock_24h_WithSeconds_RendersHHmmss()
  {
    var opts = new DisplayOptions { TimeFormat = "24h", ShowSeconds = true };
    Clocks.FormatWallClock(SamplePm, opts).Should().Be("15:45:22");
  }

  [Fact]
  public void FormatWallClock_12h_NoSeconds_RendersHmmTt()
  {
    // Single-digit hour (3) renders without a leading zero — matches the
    // visual mock in the handoff (3:45 PM, not 03:45 PM).
    var opts = new DisplayOptions { TimeFormat = "12h", ShowSeconds = false };
    Clocks.FormatWallClock(SamplePm, opts).Should().Be("3:45 PM");
  }

  [Fact]
  public void FormatWallClock_12h_WithSeconds_RendersHmmssTt()
  {
    var opts = new DisplayOptions { TimeFormat = "12h", ShowSeconds = true };
    Clocks.FormatWallClock(SamplePm, opts).Should().Be("3:45:22 PM");
  }

  [Fact]
  public void FormatWallClock_12h_MorningHour_RendersAmSuffix()
  {
    // 07:05:03 → "7:05 AM" — pins that the AM glyph fires correctly for the
    // morning half and that single-digit minutes/seconds get padded only when
    // the field is the minute/second (hour stays single-digit).
    var opts = new DisplayOptions { TimeFormat = "12h", ShowSeconds = false };
    Clocks.FormatWallClock(SampleAm, opts).Should().Be("7:05 AM");
  }

  [Fact]
  public void FormatWallClock_12h_MorningHour_WithSeconds_PadsSecondsField()
  {
    var opts = new DisplayOptions { TimeFormat = "12h", ShowSeconds = true };
    Clocks.FormatWallClock(SampleAm, opts).Should().Be("7:05:03 AM");
  }

  [Fact]
  public void FormatWallClock_AllowSecondsFalse_SuppressesSecondsEvenWhenEnabled()
  {
    // The queue ends-prediction passes allowSeconds: false so the global
    // ShowSeconds setting can't introduce :ss precision that's meaningless
    // for a track-total forward estimate (handoff §3.4).
    var opts = new DisplayOptions { TimeFormat = "24h", ShowSeconds = true };
    Clocks.FormatWallClock(SamplePm, opts, allowSeconds: false).Should().Be("15:45");
  }

  [Fact]
  public void FormatWallClock_AllowSecondsFalse_12h_AlsoSuppressesSeconds()
  {
    var opts = new DisplayOptions { TimeFormat = "12h", ShowSeconds = true };
    Clocks.FormatWallClock(SamplePm, opts, allowSeconds: false).Should().Be("3:45 PM");
  }

  [Fact]
  public void FormatWallClock_NullOptions_TreatedAsDefault24h()
  {
    // Defensive null-safety: callers without an IOptionsMonitor wired up
    // shouldn't crash. They get the historical 24h/no-seconds default.
    Clocks.FormatWallClock(SamplePm, opts: null).Should().Be("15:45");
  }

  [Fact]
  public void FormatWallClock_UnknownTimeFormat_FallsBackTo24Hour()
  {
    // The TimeFormat field is a string for forward-compat (future "12h-no-suffix"
    // etc.). An unrecognised value must not throw — fall back to the safe default.
    var opts = new DisplayOptions { TimeFormat = "garbage", ShowSeconds = false };
    Clocks.FormatWallClock(SamplePm, opts).Should().Be("15:45");
  }

  [Fact]
  public void FormatWallClock_TimeFormatCaseInsensitive()
  {
    // Match the SQLite-bridge round-trip: keys are stored lowercased and value
    // case should not matter to the consumer.
    var opts = new DisplayOptions { TimeFormat = "12H", ShowSeconds = false };
    Clocks.FormatWallClock(SamplePm, opts).Should().Be("3:45 PM");
  }

  [Fact]
  public void FormatWallClock_Midnight24h_RendersZeroZero()
  {
    var midnight = new DateTime(2024, 3, 8, 0, 0, 0);
    Clocks.FormatWallClock(midnight, new DisplayOptions { TimeFormat = "24h" })
      .Should().Be("00:00");
  }

  [Fact]
  public void FormatWallClock_Midnight12h_Renders12Am()
  {
    // Conventional clock reading: 00:00 in 24h is 12:00 AM in 12h.
    var midnight = new DateTime(2024, 3, 8, 0, 0, 0);
    Clocks.FormatWallClock(midnight, new DisplayOptions { TimeFormat = "12h" })
      .Should().Be("12:00 AM");
  }

  [Fact]
  public void FormatWallClock_Noon12h_Renders12Pm()
  {
    var noon = new DateTime(2024, 3, 8, 12, 0, 0);
    Clocks.FormatWallClock(noon, new DisplayOptions { TimeFormat = "12h" })
      .Should().Be("12:00 PM");
  }
}
