using System.Globalization;
using FluentAssertions;
using Radio.Web.Components.Pages;

namespace Radio.Web.Tests.Components;

/// <summary>
/// Tests the presentation helpers introduced by the unified-feed row polish
/// (phone dark-theme handoff §Issue 4 + Task #6): the smart relative feed
/// timestamp and the phone-number formatter for the name-primary / number-
/// secondary row. Timestamps are asserted against InvariantCulture so CI locale
/// can't shift the expected strings.
/// </summary>
public class PhoneCallFormattingTests
{
  // Fixed reference "now" so the relative buckets are deterministic.
  private static readonly DateTime Now = new(2026, 7, 16, 15, 0, 0);   // Thu 3:00 PM

  [Fact]
  public void FormatFeedTimestamp_Today_RendersClockTime()
  {
    PhoneCallFormatting.FormatFeedTimestamp(new DateTime(2026, 7, 16, 16, 43, 0), Now)
      .Should().Be("4:43 PM");
  }

  [Fact]
  public void FormatFeedTimestamp_EarlierToday_RendersClockTime()
  {
    PhoneCallFormatting.FormatFeedTimestamp(new DateTime(2026, 7, 16, 9, 5, 0), Now)
      .Should().Be("9:05 AM");
  }

  [Fact]
  public void FormatFeedTimestamp_Yesterday_RendersLiteral()
  {
    PhoneCallFormatting.FormatFeedTimestamp(new DateTime(2026, 7, 15, 9, 0, 0), Now)
      .Should().Be("Yesterday");
  }

  [Fact]
  public void FormatFeedTimestamp_ThreeDaysAgo_RendersWeekday()
  {
    var d = new DateTime(2026, 7, 13, 12, 0, 0);
    PhoneCallFormatting.FormatFeedTimestamp(d, Now)
      .Should().Be(d.ToString("ddd", CultureInfo.InvariantCulture));   // e.g. "Mon"
  }

  [Fact]
  public void FormatFeedTimestamp_SixDaysAgo_RendersWeekday()
  {
    var d = new DateTime(2026, 7, 10, 12, 0, 0);
    PhoneCallFormatting.FormatFeedTimestamp(d, Now)
      .Should().Be(d.ToString("ddd", CultureInfo.InvariantCulture));
  }

  [Fact]
  public void FormatFeedTimestamp_SevenDaysAgo_FallsBackToMonthDay()
  {
    // Exactly 7 days ago is excluded from the weekday bucket (it would collide with
    // today's weekday) and drops to the same-year "MMM d" format.
    PhoneCallFormatting.FormatFeedTimestamp(new DateTime(2026, 7, 9, 12, 0, 0), Now)
      .Should().Be("Jul 9");
  }

  [Fact]
  public void FormatFeedTimestamp_SameYearOlder_RendersMonthDay()
  {
    PhoneCallFormatting.FormatFeedTimestamp(new DateTime(2026, 6, 28, 16, 43, 0), Now)
      .Should().Be("Jun 28");
  }

  [Fact]
  public void FormatFeedTimestamp_PriorYear_RendersShortDate()
  {
    PhoneCallFormatting.FormatFeedTimestamp(new DateTime(2025, 6, 28, 16, 43, 0), Now)
      .Should().Be("6/28/25");
  }

  [Theory]
  [InlineData("9193718044", "(919) 371-8044")]
  [InlineData("+19193718044", "(919) 371-8044")]     // 11-digit with leading country "1"
  [InlineData("(919) 371-8044", "(919) 371-8044")]   // already grouped → re-normalized
  [InlineData("919-371-8044", "(919) 371-8044")]
  public void FormatPhoneNumber_TenDigitUs_GroupsNicely(string raw, string expected)
  {
    PhoneCallFormatting.FormatPhoneNumber(raw).Should().Be(expected);
  }

  [Theory]
  [InlineData("5551234", "5551234")]     // too short → untouched
  [InlineData("911", "911")]             // short code → untouched
  [InlineData("+44 20 7946 0958", "+44 20 7946 0958")]   // non-US → untouched
  public void FormatPhoneNumber_NonTenDigit_ReturnsTrimmedRaw(string raw, string expected)
  {
    PhoneCallFormatting.FormatPhoneNumber(raw).Should().Be(expected);
  }

  [Fact]
  public void FormatPhoneNumber_NullOrEmpty_ReturnsEmpty()
  {
    PhoneCallFormatting.FormatPhoneNumber(null).Should().BeEmpty();
    PhoneCallFormatting.FormatPhoneNumber("").Should().BeEmpty();
    PhoneCallFormatting.FormatPhoneNumber("   ").Should().BeEmpty();
  }
}
