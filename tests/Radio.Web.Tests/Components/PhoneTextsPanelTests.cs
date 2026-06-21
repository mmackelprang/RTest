using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radzen;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Components.Pages;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components;

public class PhoneTextsPanelTests : TestContext
{
  private void Register(bool sendEnabled, bool available)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
    var client = new HttpClient(new MockHttpHandler("{}")) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder().AddInMemoryCollection(
      new Dictionary<string, string?> { ["RotaryPhone:Gv:SendEnabled"] = sendEnabled.ToString() }).Build();
    var status = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    status.ApplyStatusForTest(available ? new GvBridgeStatusDto { Available = true } : null);
    Services.AddSingleton(status);
    Services.AddSingleton(new GvBridgeSendService(client,
      NullLogger<GvBridgeSendService>.Instance, config, status));
  }

  [Fact]
  public void EmptyThreads_ShowsEmptyState()
  {
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>()));
    Assert.Contains("No conversations yet", cut.Markup);
  }

  [Fact]
  public void Loading_ShowsSkeleton()
  {
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, (List<SmsThreadDto>?)null)
      .Add(x => x.Loading, true));
    Assert.NotEmpty(cut.FindAll(".skeleton-list-row"));
  }

  [Fact]
  public void ComposeHidden_WhenFlagOff()
  {
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1","+15551234567","Mom",DateTime.UtcNow,false,"hi") })
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.Messages, new List<SmsMessageDto>()));
    // Compose bar is not interactive when send is disabled — Send button absent/disabled.
    Assert.DoesNotContain("compose-send-enabled", cut.Markup);
  }

  [Fact]
  public void LoadedThreads_RenderRows()
  {
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1","+15551234567","Mom",DateTime.UtcNow,true,"see you soon") }));
    Assert.Contains("Mom", cut.Markup);
    Assert.Contains("see you soon", cut.Markup);
    // Unread thread → unread dot present.
    Assert.NotEmpty(cut.FindAll(".unread-dot"));
  }

  [Fact]
  public void Degraded_ShowsTextingUnavailable_WhenThreadOpen()
  {
    Register(sendEnabled: true, available: false);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>()));
    Assert.Contains("Texting unavailable", cut.Markup);
    Assert.DoesNotContain("compose-send-enabled", cut.Markup);
    // Degraded branch replaces the whole compose bar with the pill — no message
    // input and no Send path are rendered.
    Assert.Empty(cut.FindAll(".texts-compose-input"));
  }

  [Fact]
  public void Degraded_HidesComposeInput_EvenWhenFlagOn()
  {
    // Send flag ON but GV unavailable: the degraded gate must still win — the
    // "Texting unavailable" pill shows and the compose input is absent.
    Register(sendEnabled: true, available: false);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>()));
    Assert.Contains("Texting unavailable", cut.Markup);
    Assert.Empty(cut.FindAll(".texts-compose-input"));
    Assert.DoesNotContain("compose-send-enabled", cut.Markup);
  }

  [Fact]
  public void ComposeEnabled_WhenFlagOnAndAvailable()
  {
    Register(sendEnabled: true, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>()));
    Assert.Contains("compose-send-enabled", cut.Markup);
  }
}
