using Bunit;
using Microsoft.AspNetCore.Components;
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
    // Tightened (GV-8 H-1): the old assertion (".skeleton-list-row" non-empty) passed
    // even when the rows were empty static grey bands with zero shimmer — assert the
    // shimmer primitive itself, at the exact count the ×6 loop implies
    // (chip + 2 text bars per row).
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, (List<SmsThreadDto>?)null)
      .Add(x => x.Loading, true));
    Assert.Equal(18, cut.FindAll(".skeleton-loading").Count);
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

  // ── GV-8 / UAT F-1: the conversation pane must be able to say "failed" ──────

  [Fact]
  public void Conversation_ShowsErrorState_NotEmptyState_WhenErrorSet()
  {
    // THE regression gate. Assert both halves: the error is present AND the lie is
    // absent. Before GV-8 this rendered "Start the conversation below." for a 502.
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, (List<SmsMessageDto>?)null)
      .Add(x => x.Error, true));

    Assert.Contains("Couldn't load messages.", cut.Markup);
    Assert.DoesNotContain("Start the conversation below.", cut.Markup);
    Assert.Contains("Retry", cut.Markup);
  }

  [Fact]
  public void Conversation_ShowsEmptyState_WhenGenuinelyEmpty()
  {
    // The other side of the same coin: a real 200-with-zero-messages (which is also what
    // a group thread returns today, RotaryPhone Defect B) still reads as empty.
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>())
      .Add(x => x.Error, false));

    Assert.Contains("Start the conversation below.", cut.Markup);
    Assert.DoesNotContain("Couldn't load messages.", cut.Markup);
  }

  [Fact]
  public void Conversation_ShowsSkeleton_WhileLoading()
  {
    // The skeleton branch has existed since GV-3 but was unreachable dead code, because
    // PhoneMessagesPanel never passed Loading — which is why the UAT saw no spinner.
    // Tightened (GV-8 H-1): the old assertion (".skeleton-list-row" non-empty) passed
    // even when the rows were empty static grey bands with zero shimmer — assert the
    // shimmer primitive itself, at the exact count the ×5 loop implies
    // (chip + 2 text bars per row).
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, (List<SmsMessageDto>?)null)
      .Add(x => x.Loading, true));

    Assert.Equal(15, cut.FindAll(".skeleton-loading").Count);
    Assert.DoesNotContain("Start the conversation below.", cut.Markup);
    Assert.DoesNotContain("Couldn't load messages.", cut.Markup);
  }

  [Fact]
  public void Conversation_RetryButton_InvokesOnRetry()
  {
    Register(sendEnabled: false, available: true);
    var retries = 0;
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, (List<SmsMessageDto>?)null)
      .Add(x => x.Error, true)
      .Add(x => x.OnRetry, EventCallback.Factory.Create(this, () => retries++)));

    // Find by label: the header Back button and the compose Send button are also
    // <button>s, so a positional or class selector would be brittle.
    var retry = cut.FindAll("button").First(b => b.TextContent.Trim() == "Retry");
    retry.Click();

    Assert.Equal(1, retries);
  }
}
