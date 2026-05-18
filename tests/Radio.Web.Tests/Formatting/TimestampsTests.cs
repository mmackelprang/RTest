using FluentAssertions;
using Radio.Web.Formatting;

namespace Radio.Web.Tests.Formatting;

/// <summary>
/// Tests <see cref="Timestamps.FormatRelative(DateTime, DateTime)"/> using a frozen "now"
/// of 2026-05-17 14:00:00 local time. Exercises each branch (today / yesterday / older).
/// </summary>
public class TimestampsTests
{
  private static readonly DateTime Now = new(2026, 5, 17, 14, 0, 0, DateTimeKind.Local);

  [Fact]
  public void FormatRelative_SameDayEarlier_RendersTodayHHmm()
  {
    var t = new DateTime(2026, 5, 17, 9, 30, 0, DateTimeKind.Local);
    Timestamps.FormatRelative(t, Now).Should().Be("Today 09:30");
  }

  [Fact]
  public void FormatRelative_SameDayMidnight_RendersTodayHHmm()
  {
    var t = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Local);
    Timestamps.FormatRelative(t, Now).Should().Be("Today 00:00");
  }

  [Fact]
  public void FormatRelative_Yesterday_RendersYesterdayHHmm()
  {
    var t = new DateTime(2026, 5, 16, 22, 15, 0, DateTimeKind.Local);
    Timestamps.FormatRelative(t, Now).Should().Be("Yesterday 22:15");
  }

  [Fact]
  public void FormatRelative_TwoDaysAgo_RendersMonthDayDotTime()
  {
    var t = new DateTime(2026, 5, 15, 8, 5, 0, DateTimeKind.Local);
    // Middle dot U+00B7 between date and time.
    Timestamps.FormatRelative(t, Now).Should().Be("May 15 · 08:05");
  }

  [Fact]
  public void FormatRelative_DifferentMonth_RendersMonthDayDotTime()
  {
    var t = new DateTime(2026, 3, 1, 17, 45, 0, DateTimeKind.Local);
    Timestamps.FormatRelative(t, Now).Should().Be("Mar 1 · 17:45");
  }

  [Fact]
  public void FormatRelative_OlderBranch_ContainsMiddleDotSeparator()
  {
    var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
    var result = Timestamps.FormatRelative(t, Now);
    result.Should().Contain("·");
    result.Should().NotContain(" - "); // never a hyphen separator
  }

  [Fact]
  public void FormatRelative_NoArg_DelegatesToNowOverload()
  {
    // Smoke test: the no-arg form should produce a non-empty string for the current moment.
    var result = Timestamps.FormatRelative(DateTime.Now);
    result.Should().NotBeNullOrWhiteSpace();
    result.Should().StartWith("Today ");
  }

  // ─── FormatRecentRelative ─────────────────────────────────────────────────
  // Extracted from NowPlayingPanel.FormatTimeAgo in Arc 3 PR C (item #35).
  // Distinct from FormatRelative because the recognition stream and dev-tray
  // event log anchor on very-recent timestamps where "Today HH:mm" reads as
  // too rigid — "12s ago" / "5m ago" is the appropriate scale.

  private static readonly DateTime NowUtc = new(2026, 5, 17, 14, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void FormatRecentRelative_FiveSecondsAgo_RendersSecondsAgo()
  {
    var t = NowUtc.AddSeconds(-5);
    Timestamps.FormatRecentRelative(t, NowUtc).Should().Be("5s ago");
  }

  [Fact]
  public void FormatRecentRelative_SubSecond_RendersJustNow()
  {
    // Delta below 1s: avoid the "0s ago" surprise — use "just now" instead.
    var t = NowUtc.AddMilliseconds(-500);
    Timestamps.FormatRecentRelative(t, NowUtc).Should().Be("just now");
  }

  [Fact]
  public void FormatRecentRelative_NinetySecondsAgo_RendersOneMinuteAgo()
  {
    // (int) cast on TotalMinutes truncates 1.5 → 1, matching the prior
    // NowPlayingPanel.FormatTimeAgo behaviour.
    var t = NowUtc.AddSeconds(-90);
    Timestamps.FormatRecentRelative(t, NowUtc).Should().Be("1m ago");
  }

  [Fact]
  public void FormatRecentRelative_FiveHoursAgo_RendersHoursAgo()
  {
    var t = NowUtc.AddHours(-5);
    Timestamps.FormatRecentRelative(t, NowUtc).Should().Be("5h ago");
  }

  [Fact]
  public void FormatRecentRelative_ThreeDaysAgo_RendersDaysAgo()
  {
    var t = NowUtc.AddDays(-3);
    Timestamps.FormatRecentRelative(t, NowUtc).Should().Be("3d ago");
  }

  [Fact]
  public void FormatRecentRelative_NoArg_DelegatesToUtcNowOverload()
  {
    // Smoke test: the no-arg form picks up DateTime.UtcNow and emits a
    // non-empty short-relative string for "right now".
    var result = Timestamps.FormatRecentRelative(DateTime.UtcNow);
    result.Should().NotBeNullOrWhiteSpace();
    // Either "just now" (< 1s elapsed) or "0s ago" (some millis elapsed) —
    // depending on test machine timing. Both are valid short-relative shapes.
    result.Should().Match(r => r == "just now" || r.EndsWith("s ago"));
  }
}
