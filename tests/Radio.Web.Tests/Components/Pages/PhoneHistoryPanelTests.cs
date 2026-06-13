using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for <see cref="PhoneHistoryPanel"/> — pins the call-duration
/// display formatting (raw TimeSpan strings from RotaryPhone carry full tick
/// precision; the panel must render a clean m:ss / h:mm:ss).
/// </summary>
public class PhoneHistoryPanelTests : TestContext
{
  public PhoneHistoryPanelTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private static CallHistoryEntryDto Entry(string? duration) => new()
  {
    Id = "1",
    PhoneNumber = "9195551212",
    StartTime = new DateTime(2026, 6, 13, 14, 9, 48, DateTimeKind.Local),
    Direction = CallDirection.Incoming,
    AnsweredOn = CallAnsweredOn.RotaryPhone,
    Duration = duration
  };

  [Fact]
  public void Duration_StripsSubSecondPrecision_ToMinSec()
  {
    var cut = RenderComponent<PhoneHistoryPanel>(p => p
      .Add(x => x.CallHistory, new List<CallHistoryEntryDto> { Entry("00:00:37.9710594") }));

    // 37.97s rounds to 38 → "0:38", not "00:00:37.9710594".
    cut.Find(".call-duration").TextContent.Trim().Should().Be("0:38");
  }

  [Fact]
  public void Duration_OverAnHour_UsesHoursMinutesSeconds()
  {
    var cut = RenderComponent<PhoneHistoryPanel>(p => p
      .Add(x => x.CallHistory, new List<CallHistoryEntryDto> { Entry("01:02:05.2") }));

    cut.Find(".call-duration").TextContent.Trim().Should().Be("1:02:05");
  }

  [Fact]
  public void Duration_Null_OmitsDurationElement()
  {
    var cut = RenderComponent<PhoneHistoryPanel>(p => p
      .Add(x => x.CallHistory, new List<CallHistoryEntryDto> { Entry(null) }));

    cut.FindAll(".call-duration").Should().BeEmpty();
  }
}
