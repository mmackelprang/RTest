using Bunit;
using Microsoft.Extensions.DependencyInjection;
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

  private VoicemailItemDto Vm(bool isRead = false, int duration = 42,
    string? transcript = "Hey, calling about…", string? name = "Jane") =>
    new("vm1", "t1", "+15551234567", name, DateTime.UtcNow, duration, isRead,
      transcript, "/api/gvbridge/voicemail/vm1/audio");

  [Fact]
  public void Unheard_ShowsUnreadDot()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(isRead: false))
      .Add(x => x.Expanded, false));
    Assert.Single(cut.FindAll(".unread-dot"));
  }

  [Fact]
  public void Heard_NoUnreadDot()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(isRead: true))
      .Add(x => x.Expanded, false));
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
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(isRead: false))
      .Add(x => x.Expanded, false));

    // Positive first, so the :scope assertions cannot pass on an unrendered row.
    Assert.Contains("Jane", cut.Find(".vm-row-title").TextContent);

    var row = cut.Find(".list-item-touch");
    Assert.NotNull(row.QuerySelector(":scope > .unread-dot"));
    Assert.NotNull(row.QuerySelector(":scope > .vm-row-main"));
  }

  [Fact]
  public void ZeroDuration_RendersEmDash()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(duration: 0))
      .Add(x => x.Expanded, false));
    Assert.Contains("—", cut.Markup);
    Assert.DoesNotContain("0:00", cut.Markup);
  }
}
