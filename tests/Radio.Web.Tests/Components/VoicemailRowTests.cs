using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Components;

public class VoicemailRowTests : TestContext
{
  public VoicemailRowTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  // TEST-12: the row's clock and the item's time are both fixed, so nothing rendered depends on
  // when the suite runs. The clock's local zone is UTC (FakeTimeProvider's default), so the
  // timestamp the row shows is the UTC time below. The suite used to build the item from
  // DateTime.UtcNow, and ZeroDuration_RendersEmDash failed whenever that rendered as 10:00.
  private static readonly DateTimeOffset Now = new(2026, 1, 15, 15, 30, 0, TimeSpan.Zero);
  private readonly FakeTimeProvider _clock = new(Now);

  private VoicemailItemDto Vm(bool isRead = false, int duration = 42,
    string? transcript = "Hey, calling about…", string? name = "Jane", DateTime? receivedAt = null) =>
    new("vm1", "t1", "+15551234567", name, receivedAt ?? Now.UtcDateTime.AddMinutes(-5), duration, isRead,
      transcript, "/api/gvbridge/voicemail/vm1/audio");

  private IRenderedComponent<VoicemailRow> RenderRow(VoicemailItemDto item) =>
    RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, item)
      .Add(x => x.Expanded, false)
      .Add(x => x.Clock, _clock));

  [Fact]
  public void Unheard_ShowsUnreadDot()
  {
    var cut = RenderRow(Vm(isRead: false));
    Assert.Single(cut.FindAll(".unread-dot"));
  }

  [Fact]
  public void Heard_NoUnreadDot()
  {
    var cut = RenderRow(Vm(isRead: true));
    Assert.Empty(cut.FindAll(".unread-dot"));
  }

  [Fact]
  public void UnreadRow_KeepsTheStructureTheUnreadGutterRuleDependsOn()
  {
    // ⚠ bUnit evaluates no CSS (GV-9 plan C-209), so this does NOT prove the 20px
    // unread gutter renders. It pins the structural precondition the rule's first
    // selector line needs at this — the third — live render site:
    //   .phone-messages-feed .list-item-touch:not(:has(> .unread-dot))
    //     > :is(.list-item-identity, .vm-row-main)
    // The voicemail row is the only site that contributes .vm-row-main to that
    // :is(), so it is the only test that can catch it being nested or renamed.
    // See PhoneMessagesFeedRowTests for the call and text-thread sites.
    var cut = RenderRow(Vm(isRead: false));

    // Positive first, so the :scope assertions cannot pass on an unrendered row.
    Assert.Contains("Jane", cut.Find(".vm-row-title").TextContent);

    var row = cut.Find(".list-item-touch");
    Assert.NotNull(row.QuerySelector(":scope > .unread-dot"));
    Assert.NotNull(row.QuerySelector(":scope > .vm-row-main"));
  }

  [Fact]
  public void ZeroDuration_RendersEmDash()
  {
    var cut = RenderRow(Vm(duration: 0));
    // The duration element itself, not the whole markup: the timestamp beside it is a clock
    // time and may legitimately contain "0:00" (TEST-12).
    Assert.Equal("—", cut.Find(".vm-row-duration").TextContent.Trim());
  }

  [Fact]
  public void ZeroDuration_ReceivedAtTenOClock_StillRendersEmDash_AndTheTimeBesideIt()
  {
    // TEST-12's failing case, made deterministic: an item received at 10:00 renders "10:00 AM",
    // which contains "0:00". The duration must still be the em dash.
    var cut = RenderRow(Vm(duration: 0, receivedAt: new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)));
    Assert.Equal("10:00 AM", cut.Find(".list-item-meta").TextContent.Trim());
    Assert.Equal("—", cut.Find(".vm-row-duration").TextContent.Trim());
  }

  [Fact]
  public void NonZeroDuration_RendersMinutesAndSeconds()
  {
    var cut = RenderRow(Vm(duration: 42));
    Assert.Equal("0:42", cut.Find(".vm-row-duration").TextContent.Trim());
  }

  [Fact]
  public void Timestamp_IsReadInTheClocksLocalZone()
  {
    // 15:25 UTC is 20:55 at UTC+05:30. A zone no dev host or CI runner here uses, so a row that
    // read the host's zone instead of the clock's renders a different time and fails.
    TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Test+0530", new TimeSpan(5, 30, 0), "Test+0530", "Test+0530");
    _clock.SetLocalTimeZone(zone);
    var cut = RenderRow(Vm());
    Assert.Equal("8:55 PM", cut.Find(".list-item-meta").TextContent.Trim());
  }

  [Fact]
  public void NoTranscript_IsPendingForThirtyMinutes_ByTheRowsClock()
  {
    var item = Vm(transcript: null);
    var cut = RenderRow(item);
    Assert.Equal("Transcript pending…", cut.Find(".vm-row-preview").TextContent.Trim());

    _clock.Advance(TimeSpan.FromMinutes(30));
    cut.SetParametersAndRender(p => p.Add(x => x.Item, item));
    Assert.Equal("No transcript available.", cut.Find(".vm-row-preview").TextContent.Trim());
  }
}
