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

  // The feed's default segment is "all" (PhoneMessagesPanel.razor:281), and both
  // FeedLoading and FeedError require ALL THREE source lists to be null (:386-391),
  // so a one-element Threads with no CallHistory falls straight through to the
  // RenderTextThreadRow loop. No filter click or extra parameter is needed.
  private IRenderedComponent<PhoneMessagesPanel> RenderThread(SmsThreadDto thread)
  {
    return RenderComponent<PhoneMessagesPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto> { thread })
      .Add(x => x.Contacts, new List<MergedContact>()));
  }

  private static SmsThreadDto TextThread(bool hasUnread = true) =>
    new("t1", "+15551234567", "Mom", DateTime.Now, hasUnread, "see you soon");

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

  // ── GV-9 / F-7: the structure the unread-gutter CSS rule depends on ────────
  //
  // ⚠ bUnit evaluates no CSS (GV-9 plan C-209), so nothing below proves the 20px
  // gutter renders. What these pin is the structural precondition the rule's
  // FIRST selector line needs —
  //   .phone-messages-feed .list-item-touch:not(:has(> .unread-dot))
  //     > :is(.list-item-identity, .vm-row-main)
  // — at the two live sites this file renders. That line is the one every
  // production row matches; PhoneTextsPanelTests.ThreadRow_KeepsTheStructureThe-
  // UnreadGutterRuleDependsOn covers only the .texts-thread-list line, i.e. the
  // dead PhoneTextsPanel copy, so before these tests the live surface had no
  // structural coverage at all. VoicemailRowTests.UnreadRow_KeepsTheStructureThe-
  // UnreadGutterRuleDependsOn is the third live site.
  //
  // Nest the dot or the identity column one level deeper and the selector stops
  // matching SILENTLY: green build, green suite, and the 20px jump is back. That
  // is the GV-7 regression these exist to make loud (plan §5).

  [Fact]
  public void CallRow_KeepsTheStructureTheUnreadGutterRuleDependsOn()
  {
    Register();
    var cut = RenderCall(Call(callerName: "Bob Smith"));

    // Positive first — proves the row actually rendered, so the :scope
    // assertions below cannot be satisfied by an unreached branch.
    Assert.Contains("Bob Smith", cut.Find(".list-item-title").TextContent);

    var row = cut.Find(".list-item-touch");
    // A call row never carries a dot, so it is permanently on the
    // :not(:has(> .unread-dot)) side of the rule — the branch that actually
    // receives the margin. Its identity column must be a DIRECT child.
    Assert.Empty(row.QuerySelectorAll(":scope > .unread-dot"));
    Assert.NotNull(row.QuerySelector(":scope > .list-item-identity"));
  }

  [Fact]
  public void UnreadTextThreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn()
  {
    Register();
    var cut = RenderThread(TextThread(hasUnread: true));

    // Positive first, for the same reason as above: RenderTextThreadRow sits
    // behind three state gates, and a NotNull on an unrendered row would be
    // vacuous rather than passing.
    Assert.Contains("Mom", cut.Find(".list-item-title").TextContent);

    var row = cut.Find(".list-item-touch");
    Assert.NotNull(row.QuerySelector(":scope > .unread-dot"));
    Assert.NotNull(row.QuerySelector(":scope > .list-item-identity"));
  }
}
