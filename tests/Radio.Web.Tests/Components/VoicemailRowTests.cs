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
  public void ZeroDuration_RendersEmDash()
  {
    var cut = RenderComponent<VoicemailRow>(p => p
      .Add(x => x.Item, Vm(duration: 0))
      .Add(x => x.Expanded, false));
    Assert.Contains("—", cut.Markup);
    Assert.DoesNotContain("0:00", cut.Markup);
  }
}
