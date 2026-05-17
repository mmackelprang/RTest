using FluentAssertions;
using Radio.Web.Formatting;

namespace Radio.Web.Tests.Formatting;

/// <summary>
/// Tests <see cref="Durations.FormatTrack(TimeSpan)"/> /
/// <see cref="Durations.FormatTrack(TimeSpan?)"/> / <see cref="Durations.FormatLong(TimeSpan)"/>.
/// Covers the acceptance cases from the design-tightening handoff §P0·0.
/// </summary>
public class DurationsTests
{
  [Fact]
  public void FormatTrack_180Seconds_RendersThreeMinutesZeroSeconds()
  {
    Durations.FormatTrack(TimeSpan.FromSeconds(180.6628))
      .Should().Be("3:00");
  }

  [Fact]
  public void FormatTrack_Zero_RendersEmDash()
  {
    Durations.FormatTrack(TimeSpan.Zero).Should().Be("—");
  }

  [Fact]
  public void FormatTrack_SubSecond_RendersEmDash()
  {
    Durations.FormatTrack(TimeSpan.FromMilliseconds(800)).Should().Be("—");
  }

  [Fact]
  public void FormatTrack_OverOneHour_RendersHMmSs()
  {
    Durations.FormatTrack(TimeSpan.FromSeconds(3742))
      .Should().Be("1:02:22");
  }

  [Fact]
  public void FormatTrack_NullableNull_RendersEmDash()
  {
    Durations.FormatTrack((TimeSpan?)null).Should().Be("—");
  }

  [Fact]
  public void FormatTrack_NullableValue_RendersAsValue()
  {
    Durations.FormatTrack((TimeSpan?)TimeSpan.FromSeconds(75))
      .Should().Be("1:15");
  }

  [Fact]
  public void FormatTrack_ExactlyOneHour_RendersHMmSs()
  {
    Durations.FormatTrack(TimeSpan.FromHours(1)).Should().Be("1:00:00");
  }

  [Fact]
  public void FormatTrack_SubMinuteSeconds_PadsLeadingZero()
  {
    Durations.FormatTrack(TimeSpan.FromSeconds(5)).Should().Be("0:05");
  }

  [Fact]
  public void FormatLong_OneHourThirtyMinutes_RendersHMmSs()
  {
    Durations.FormatLong(TimeSpan.FromMinutes(90)).Should().Be("1:30:00");
  }

  [Fact]
  public void FormatLong_TwoAndAHalfHours_RendersHMmSs()
  {
    Durations.FormatLong(TimeSpan.FromHours(2.5)).Should().Be("2:30:00");
  }

  [Fact]
  public void FormatLong_BelowOneHour_StillRendersHMmSs()
  {
    // Unlike FormatTrack, FormatLong never collapses to m:ss.
    Durations.FormatLong(TimeSpan.FromSeconds(45)).Should().Be("0:00:45");
  }

  [Fact]
  public void FormatLong_Zero_RendersZeroHMmSs()
  {
    Durations.FormatLong(TimeSpan.Zero).Should().Be("0:00:00");
  }
}
