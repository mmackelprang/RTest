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
}
