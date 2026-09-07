using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radzen;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
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
  // PHN-3: MessageBubble now injects ConsolePlaybackState and EventPlaybackApiService, and the
  // panel injects NotificationService. Every test that renders conversation mode with a message in
  // it therefore needs the whole graph — a setup requirement, not a regression.
  //
  // ⚠ NotificationService is NOT registered separately here. AddRadzenComponents() already
  // registers it (which is why NowPlayingPanel, which injects it, renders in its own tests over
  // nothing but that call). Adding a second registration would put two lifetimes on one service
  // for no benefit; the plan's "add Services.AddSingleton<NotificationService>()" was written
  // before that was checked.
  private AudioStateStore _store = default!;
  private SpeakEventsHandler _speak = default!;

  private void Register(bool available, bool speakFails = false)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
    Services.AddHermeticTestRig();
    var status = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    status.ApplyStatusForTest(available ? new GvBridgeStatusDto { Available = true } : null);
    Services.AddSingleton(status);

    _store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));
    Services.AddSingleton(_store);
    Services.AddSingleton(sp => new ConsolePlaybackState(
      sp.GetRequiredService<AudioStateStore>(), NullLogger<ConsolePlaybackState>.Instance));

    _speak = new SpeakEventsHandler(speakFails);
    Services.AddSingleton(new EventPlaybackApiService(
      new HttpClient(_speak) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance));
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

  // ── PHN-3 T4: what the console is allowed to say the message is FROM ────────
  //
  // ⚠ SenderName is not visible in the markup — it only shapes the utterance and the label. So
  // every test below drives the real path: tap the speak button, then read what was POSTed. That
  // is deliberate; asserting on a parameter the component received would pass against a
  // MessageBubble that then ignored it.

  private const string Body = "Dinner at 7?";
  private const string Number = "+15551234567";

  private IRenderedComponent<PhoneTextsPanel> OpenConversation(
    string? headerName, string? headerNumber) =>
    RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, headerName)
      .Add(x => x.HeaderNumber, headerNumber)
      .Add(x => x.Messages, new List<SmsMessageDto>
        { new("m1", "t1", "Inbound", Number, Body, DateTime.UtcNow, false) }));

  private async Task<System.Text.Json.JsonElement> TapSpeakAsync(
    IRenderedComponent<PhoneTextsPanel> cut)
  {
    await cut.Find(".msg-speak-btn").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
    var posted = Assert.Single(_speak.Bodies);
    return System.Text.Json.JsonDocument.Parse(posted).RootElement.Clone();
  }

  [Fact]
  public async Task SenderName_IsPassedWhenAContactNameResolved()
  {
    Register(available: true);
    var cut = OpenConversation(headerName: "Jane", headerNumber: Number);

    var posted = await TapSpeakAsync(cut);

    Assert.Equal($"Message from Jane. {Body}", posted.GetProperty("text").GetString());
    Assert.Equal("a message from Jane", posted.GetProperty("label").GetString());
  }

  [Fact]
  public async Task SenderName_IsNullWhenTheNameIsJustTheNumber()
  {
    // ⭐ PhoneMessagesPanel.OpenThreadName falls back to the bare CounterpartyNumber when no
    // contact matches — 14 of 20 live threads. Handoff :386: "Do not read the identifier aloud."
    // "Message from plus one five five five one two three four five six seven" costs ~6 seconds
    // and conveys nothing the user is not already looking at.
    Register(available: true);
    var cut = OpenConversation(headerName: Number, headerNumber: Number);

    var posted = await TapSpeakAsync(cut);

    Assert.Equal(Body, posted.GetProperty("text").GetString());
    Assert.Equal("a message", posted.GetProperty("label").GetString());
    Assert.DoesNotContain(Number, posted.GetProperty("text").GetString());
  }

  [Fact]
  public async Task SenderName_IsNullWhenNoNumberResolved()
  {
    // ⭐⭐ THE clause an equality test alone does not cover, and the one whose absence is audible.
    // PhoneMessagesPanel.OpenThreadName:326 returns the RAW THREAD ID when the open thread is not
    // in Threads at all, while OpenThreadNumber (:339) answers "". A guard of the form "if the name
    // equals the number, pass null" answers FALSE there — and a Google Voice thread identifier gets
    // read out to the room. See plan PHN-3 C-108.
    Register(available: true);
    var cut = OpenConversation(headerName: "t_abc123", headerNumber: "");

    var posted = await TapSpeakAsync(cut);

    Assert.Equal(Body, posted.GetProperty("text").GetString());
    Assert.Equal("a message", posted.GetProperty("label").GetString());
    Assert.DoesNotContain("t_abc123", posted.GetProperty("text").GetString());
    Assert.DoesNotContain("t_abc123", posted.GetProperty("label").GetString());
  }

  /// <summary>
  /// ⚠ THIS TEST PINS THE CONTRACT, NOT THE CLAUSE, AND THE DIFFERENCE WAS MEASURED.
  /// </summary>
  /// <remarks>
  /// Plan PHN-3 §4.3b names "delete the first clause" as this row's mutation. It was run, and the
  /// suite stayed GREEN — so the assertion below cannot be cited as evidence that
  /// SpeakableSenderName's <c>IsNullOrWhiteSpace(name)</c> guard is load-bearing. It is not:
  /// GvSpeechText.ForMessage and MessageBubble.SpeakLabel each re-check IsNullOrWhiteSpace on the
  /// name they are given, so an empty or whitespace SenderName produces no lead-in and the generic
  /// "a message" label whether the panel filters it or the helpers do. The clause is defence in
  /// depth at the panel boundary, deliberately kept — it is what makes the property's contract
  /// readable in one place — but it is not observable through this surface.
  ///
  /// What this test DOES pin is the contract itself: an empty header name never puts anything
  /// name-shaped into the utterance or the label. That would fail the moment either downstream
  /// guard was removed, which is the regression worth catching. Recorded rather than quietly
  /// passing, because this repository's standing hazard is a test read as proof of something it
  /// never exercised.
  /// </remarks>
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  public async Task SenderName_IsNullWhenHeaderNameIsEmpty(string? headerName)
  {
    Register(available: true);
    var cut = OpenConversation(headerName, headerNumber: Number);

    var posted = await TapSpeakAsync(cut);

    Assert.Equal(Body, posted.GetProperty("text").GetString());
    Assert.Equal("a message", posted.GetProperty("label").GetString());
  }

  [Fact]
  public async Task SpeakFailure_RaisesAWarningToastAndNoMessageContent()
  {
    // Handoff §Cross-5 :174 — a synthesis failure is a TOAST, because there is no room beside a
    // bubble. Warning, not Error: the message is still on screen and still readable by eye.
    //
    // ⭐ The second half of this test is the point. These toasts render on a kiosk in a family room
    // and this row's subject matter is private SMS. The reason CODE is all that may cross that
    // boundary — never the body, the sender, the number, or the raw token itself.
    Register(available: true, speakFails: true);
    var cut = OpenConversation(headerName: "Jane", headerNumber: Number);

    await cut.Find(".msg-speak-btn").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

    var toast = Assert.Single(Services.GetRequiredService<NotificationService>().Messages);
    Assert.Equal(NotificationSeverity.Warning, toast.Severity);
    Assert.Equal("Couldn't read that message.", toast.Summary?.ToString());
    Assert.Equal("The console isn't responding. Try again.", toast.Detail?.ToString());

    var rendered = $"{toast.Summary} {toast.Detail}";
    Assert.DoesNotContain(Body, rendered);
    Assert.DoesNotContain(Number, rendered);
    Assert.DoesNotContain("Jane", rendered);
    Assert.DoesNotContain("Transport", rendered);
  }

  /// <summary>
  /// Answers POST /api/audio/events, recording the bodies — or throws, to drive the failure toast.
  /// </summary>
  private sealed class SpeakEventsHandler(bool fails) : HttpMessageHandler
  {
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.Content is not null)
      {
        Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
      }

      if (fails)
      {
        // A transport failure, which StartSpeechAsync answers as "Transport" — the same token
        // StartVoicemailAsync uses and the one the panel's switch maps.
        throw new HttpRequestException("no route to host");
      }

      const string body = """
        {"id":"evp-speech-1","kind":"Speech","label":"a message","state":"Preparing",
         "duration":null,"positionAtBroadcast":"00:00:00",
         "broadcastAtUtc":"2026-09-05T00:00:00+00:00","failureReason":null}
        """;

      return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
      {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
      };
    }
  }
}
