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
  // PHN-4: the panel injects only IJSRuntime now — GvBridgeSendService is deleted
  // and GvBridgeStatusService is no longer read here, because neither the send flag
  // nor GV availability changes what a read surface renders.
  // The status service is still registered, and `available` still varied, on
  // purpose: it is the seam a reintroduced availability branch would consume, so
  // ReplyPill_ShowsRegardlessOfGvAvailability fails the moment one comes back.
  // Note what that does and does not prove — with nothing reading the service today
  // the assertion cannot fail for the current code; it is a guard against
  // regression, not evidence of a live gate.
  private void Register(bool available)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
    var status = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    status.ApplyStatusForTest(available ? new GvBridgeStatusDto { Available = true } : null);
    Services.AddSingleton(status);
  }

  [Fact]
  public void EmptyThreads_ShowsEmptyState()
  {
    Register(available: true);
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
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, (List<SmsThreadDto>?)null)
      .Add(x => x.Loading, true));
    Assert.Equal(18, cut.FindAll(".skeleton-loading").Count);
  }

  // ── PHN-4 / D31: replies are off permanently, so the gate is not a branch ────

  [Fact]
  public void Conversation_ShowsReplyPill_AndNoComposer()
  {
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1","+15551234567","Mom",DateTime.UtcNow,false,"hi") })
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.Messages, new List<SmsMessageDto>()));

    // Handoff §C4 tier 3a. Assert both halves: the reason is stated AND no
    // composer survives to reproduce UAT F-3 (an input disabled without a reason).
    Assert.Contains("Replies are turned off.", cut.Markup);
    Assert.Empty(cut.FindAll(".texts-compose-input"));
    Assert.DoesNotContain("compose-send-enabled", cut.Markup);
    Assert.DoesNotContain("Type a message", cut.Markup);
    // §C4: the slot itself is never hidden — an absent composer reads as a bug.
    // Assert on the slot's contents rather than on the string "Send", which would
    // also match unrelated markup and make this gate lie about what it proves.
    var slot = cut.Find(".texts-compose");
    Assert.Empty(slot.QuerySelectorAll("input"));
    Assert.Empty(slot.QuerySelectorAll("button"));
  }

  [Fact]
  public void ReplyPill_ShowsRegardlessOfGvAvailability()
  {
    // The regression gate for the one judgement PHN-4 made. The old compose bar
    // fell back to "Texting unavailable" when GV was reconnecting. Under D31 that
    // string would promise replies resume on reconnect, which is false — so the
    // tier-3a pill must win even in the degraded state.
    Register(available: false);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>()));

    Assert.Contains("Replies are turned off.", cut.Markup);
    Assert.DoesNotContain("Texting unavailable", cut.Markup);
  }

  [Fact]
  public void EmptyThreadList_OffersNoNewMessageAffordance()
  {
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>()));

    Assert.Contains("No conversations yet", cut.Markup);
    Assert.DoesNotContain("New message", cut.Markup);
  }

  [Fact]
  public void LoadedThreads_RenderRows()
  {
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.Threads, new List<SmsThreadDto>
        { new("t1","+15551234567","Mom",DateTime.UtcNow,true,"see you soon") }));
    Assert.Contains("Mom", cut.Markup);
    Assert.Contains("see you soon", cut.Markup);
    // Unread thread → unread dot present.
    Assert.NotEmpty(cut.FindAll(".unread-dot"));
  }

  // Three tests were deleted here by PHN-4, not ported: Degraded_ShowsTexting-
  // Unavailable_WhenThreadOpen, Degraded_HidesComposeInput_EvenWhenFlagOn and
  // ComposeEnabled_WhenFlagOnAndAvailable. All three asserted how the compose bar
  // reacted to RotaryPhone:Gv:SendEnabled and GV availability; there is no compose
  // bar and the flag no longer changes what renders. ReplyPill_ShowsRegardlessOf-
  // GvAvailability above covers what replaced them.

  // ── GV-8 / UAT F-1: the conversation pane must be able to say "failed" ──────

  [Fact]
  public void Conversation_ShowsErrorState_NotEmptyState_WhenErrorSet()
  {
    // THE regression gate. Assert both halves: the error is present AND the lie is
    // absent. Before GV-8 this rendered the empty-state copy for a 502 (that copy
    // read "Start the conversation below." then; PHN-4 restated it).
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, (List<SmsMessageDto>?)null)
      .Add(x => x.Error, true));

    Assert.Contains("Couldn't load messages.", cut.Markup);
    Assert.DoesNotContain("No messages in this conversation.", cut.Markup);
    Assert.Contains("Retry", cut.Markup);
  }

  [Fact]
  public void Conversation_ShowsMessages_WhenErrorSetButMessagesArrived()
  {
    // GV-8 M-1: content outranks a stale error flag. An inbound message can arrive
    // for an open-but-failed thread (PhonePage.OnGvSmsReceived appends while
    // _openThreadError is still set) — once Messages has content, show it instead of
    // keeping the error state until Retry or Back.
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>
        { new("m1", "t1", "Inbound", "+15551234567", "hi there", DateTime.UtcNow, false) })
      .Add(x => x.Error, true));

    Assert.Contains("hi there", cut.Markup);
    Assert.DoesNotContain("Couldn't load messages.", cut.Markup);
  }

  [Fact]
  public void Conversation_ShowsEmptyState_WhenGenuinelyEmpty()
  {
    // The other side of the same coin: a real 200-with-zero-messages (which is also what
    // a group thread returns today, RotaryPhone Defect B) still reads as empty.
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, new List<SmsMessageDto>())
      .Add(x => x.Error, false));

    Assert.Contains("No messages in this conversation.", cut.Markup);
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
    Register(available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, (List<SmsMessageDto>?)null)
      .Add(x => x.Loading, true));

    Assert.Equal(15, cut.FindAll(".skeleton-loading").Count);
    Assert.DoesNotContain("No messages in this conversation.", cut.Markup);
    Assert.DoesNotContain("Couldn't load messages.", cut.Markup);
  }

  [Fact]
  public void Conversation_RetryButton_InvokesOnRetry()
  {
    Register(available: true);
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
