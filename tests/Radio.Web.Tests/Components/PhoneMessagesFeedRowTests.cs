using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Components;

/// <summary>
/// Renders the redesigned unified-feed call row (phone dark-theme handoff §Issue 4
/// + Task #6) and asserts: the direction-tinted 44px chip, contact name-primary /
/// number-secondary rendering (matched via contacts, matched via attached
/// CallerName, and the unmatched fallback), the red "Missed" caption, and the
/// answered-on pill.
/// </summary>
public class PhoneMessagesFeedRowTests : TestContext
{
  private void Register(HttpStatusCode lookupStatus = HttpStatusCode.NotFound)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    // Lookup handler defaults to 404 so unmatched numbers stay unresolved (no name).
    var handler = new MockHttpHandler(statusCode: lookupStatus);
    var http = new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };
    var pbap = new PbapApiService(http, NullLogger<PbapApiService>.Instance);
    Services.AddSingleton(new ContactResolutionService(
      pbap, NullLogger<ContactResolutionService>.Instance));
  }

  private IRenderedComponent<PhoneMessagesPanel> RenderCall(
    CallHistoryEntryDto entry, List<MergedContact>? contacts = null)
  {
    return RenderComponent<PhoneMessagesPanel>(p => p
      .Add(x => x.CallHistory, new List<CallHistoryEntryDto> { entry })
      .Add(x => x.Contacts, contacts ?? new List<MergedContact>()));
  }

  private static CallHistoryEntryDto Call(
    string number = "9193718044",
    string? callerName = null,
    CallDirection direction = CallDirection.Incoming,
    CallAnsweredOn answeredOn = CallAnsweredOn.RotaryPhone,
    string? duration = "00:00:50") => new()
    {
      Id = "c1",
      PhoneNumber = number,
      CallerName = callerName,
      Direction = direction,
      AnsweredOn = answeredOn,
      StartTime = DateTime.Now,
      Duration = duration
    };

  [Fact]
  public void ContactMatch_ShowsNamePrimary_NumberSecondary()
  {
    Register();
    var contacts = new List<MergedContact>
    {
      new(null, "Jane Doe", "9193718044", null, "PBAP")
    };
    var cut = RenderCall(Call(callerName: null), contacts);

    Assert.Contains("Jane Doe", cut.Find(".list-item-title").TextContent);
    var subnumber = cut.FindAll(".list-item-subnumber");
    Assert.Single(subnumber);
    Assert.Contains("(919) 371-8044", subnumber[0].TextContent);
  }

  [Fact]
  public void AttachedCallerName_ShowsNamePrimary_NumberSecondary()
  {
    Register();
    // CallerName resolved by RotaryPhone at call time (PBAP + contacts API) — the
    // feed reuses it directly as the primary label.
    var cut = RenderCall(Call(callerName: "Bob Smith"));

    Assert.Contains("Bob Smith", cut.Find(".list-item-title").TextContent);
    Assert.Contains("(919) 371-8044", cut.Find(".list-item-subnumber").TextContent);
  }

  [Fact]
  public void NoMatch_ShowsFormattedNumberPrimary_NothingBelow()
  {
    Register();   // 404 lookup → unresolved
    var cut = RenderCall(Call(number: "9995551212", callerName: null));

    Assert.Contains("(999) 555-1212", cut.Find(".list-item-title").TextContent);
    Assert.Empty(cut.FindAll(".list-item-subnumber"));
  }

  [Fact]
  public void MissedCall_ShowsRedChip_AndMissedCaption_NoDuration()
  {
    Register();
    var cut = RenderCall(Call(
      direction: CallDirection.Incoming,
      answeredOn: CallAnsweredOn.NotAnswered,
      duration: null));

    Assert.Single(cut.FindAll(".feed-chip--missed"));
    var missed = cut.FindAll(".list-item-missed");
    Assert.Single(missed);
    Assert.Contains("Missed", missed[0].TextContent);
  }

  [Fact]
  public void OutgoingCall_ShowsBlueChip()
  {
    Register();
    var cut = RenderCall(Call(
      direction: CallDirection.Outgoing,
      answeredOn: CallAnsweredOn.NotAnswered));

    Assert.Single(cut.FindAll(".feed-chip--out"));
  }

  [Fact]
  public void AnsweredOnRotary_ShowsGreenChip_AndAmberRotaryPill()
  {
    Register();
    var cut = RenderCall(Call(
      direction: CallDirection.Incoming,
      answeredOn: CallAnsweredOn.RotaryPhone));

    Assert.Single(cut.FindAll(".feed-chip--in"));
    var pill = cut.Find(".phone-pill.amber");
    Assert.Contains("Rotary", pill.TextContent);
  }

  [Fact]
  public void AnsweredOnCell_ShowsGvPill()
  {
    Register();
    var cut = RenderCall(Call(
      direction: CallDirection.Incoming,
      answeredOn: CallAnsweredOn.CellPhone));

    var pill = cut.Find(".phone-pill.cyan");
    Assert.Contains("GV", pill.TextContent);
  }

  [Fact]
  public void MissedCall_HasNoAnsweredOnPill()
  {
    Register();
    var cut = RenderCall(Call(
      direction: CallDirection.Incoming,
      answeredOn: CallAnsweredOn.NotAnswered,
      duration: null));

    Assert.Empty(cut.FindAll(".phone-pill"));
  }
}
