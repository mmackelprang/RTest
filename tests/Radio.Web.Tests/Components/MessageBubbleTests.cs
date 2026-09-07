using System.Net;
using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Models;
using Radio.Web.Components.Pages;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components;

/// <summary>
/// MessageBubble — the §Ph text bubble, and since PHN-3 the speak button in its gutter.
/// </summary>
/// <remarks>
/// ⚠ WHAT THIS FILE CANNOT FALSIFY: anything about audio. bUnit renders markup and drives
/// callbacks. That a real TTSEventSource is created, that it ducks the music, that it comes out of
/// the cabinet speakers, that the voice is intelligible at kiosk distance and that a digit run is
/// spoken verbatim are provable ONLY by a person in the room (plan PHN-3 §4.5, verification items
/// 17-21). A green run of this file is evidence that the right STRING is posted to the right route
/// in the right shape, and nothing more. Do not cite it as evidence that the feature works.
///
/// ⚠ Two of the pre-PHN-3 tests below cover a code path production cannot reach.
/// <c>Sending_ShowsDimAndSpinner</c> and <c>Failed_ShowsRetryAffordance</c> both set Status
/// explicitly, and since PHN-4 the only production call site never passes it
/// (PhoneTextsPanel.razor). They are kept deliberately: they guard the dormant parameter
/// MessageBubble still carries, and Failed_ShowsRetryAffordance is the reason
/// <c>.msg-bubble.failed</c> must survive in design-system.css. ⛔ Do not delete them — and do not
/// count them as evidence about the speak button either.
///
/// ⚠ The service graph here is the REAL EventPlaybackApiService over a stubbed
/// HttpMessageHandler, not a fake service. Its methods are not virtual, and making them virtual to
/// suit a test would be the test changing production shape rather than the other way round.
/// </remarks>
public class MessageBubbleTests : TestContext
{
  private const string SpeechId = "evp-speech-1";

  private AudioStateStore _store = default!;
  private ConsolePlaybackState _console = default!;
  private StubEventsHandler _handler = default!;

  private SmsMessageDto Msg(string direction = "Inbound", string? text = "hi") =>
    new("m1", "t1", direction, "+15551234567", text, DateTime.UtcNow, false);

  /// <summary>Registers everything MessageBubble now injects. Idempotent per test.</summary>
  /// <param name="status">The status every POST answers with.</param>
  /// <param name="gate">Parks every POST until released — the start-arm re-entrancy window.</param>
  /// <param name="deleteGate">Parks every DELETE until released — the stop-arm window.</param>
  private void Register(
    HttpStatusCode status = HttpStatusCode.Accepted,
    TaskCompletionSource? gate = null,
    TaskCompletionSource? deleteGate = null)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;   // project memory: required for Radzen/JS-interop
    Services.AddRadzenComponents();
    Services.AddHermeticTestRig();

    _store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));
    Services.AddSingleton(_store);

    _console = new ConsolePlaybackState(_store, NullLogger<ConsolePlaybackState>.Instance);
    Services.AddSingleton(_console);

    _handler = new StubEventsHandler(status, gate, deleteGate);
    Services.AddSingleton(new EventPlaybackApiService(
      new HttpClient(_handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<EventPlaybackApiService>.Instance));
  }

  private IRenderedComponent<MessageBubble> RenderBubble(
    SmsMessageDto? message = null, string? senderName = null,
    Action<string?>? onSpeakFailed = null)
  {
    return RenderComponent<MessageBubble>(p =>
    {
      p.Add(x => x.Message, message ?? Msg());
      p.Add(x => x.SenderName, senderName);
      if (onSpeakFailed is not null)
      {
        p.Add(x => x.OnSpeakFailed, onSpeakFailed);
      }
    });
  }

  private Task BroadcastAsync(IRenderedComponent<MessageBubble> cut, EventPlaybackSnapshotDto snapshot) =>
    cut.InvokeAsync(() => _store.OnHubEventPlaybackChanged(snapshot));

  private static EventPlaybackSnapshotDto Snapshot(
    string state, string id = SpeechId, string? failureReason = null) =>
    new(id, "Speech", "a message from Jane", state, null,
      TimeSpan.Zero, DateTimeOffset.UtcNow, failureReason);

  // ── the pre-PHN-3 row, unchanged apart from Register() ───────────────────────────────────────

  [Fact]
  public void Inbound_AlignsLeft()
  {
    Register();
    var cut = RenderBubble(Msg("Inbound"));
    Assert.Contains("inbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void Outbound_AlignsRight()
  {
    Register();
    var cut = RenderBubble(Msg("Outbound"));
    Assert.Contains("outbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void UnknownDirection_TreatedAsInbound()
  {
    Register();
    var cut = RenderBubble(Msg("garbage"));
    Assert.Contains("inbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void NullText_RendersPlaceholder()
  {
    Register();
    var cut = RenderBubble(Msg("Inbound", null));
    Assert.Contains("(no text)", cut.Markup);
  }

  [Fact]
  public void Sending_ShowsDimAndSpinner()
  {
    Register();
    var cut = RenderComponent<MessageBubble>(p => p
      .Add(x => x.Message, Msg("Outbound"))
      .Add(x => x.Status, MessageBubble.SendStatus.Sending));
    Assert.Contains("sending", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void Failed_ShowsRetryAffordance()
  {
    Register();
    var cut = RenderComponent<MessageBubble>(p => p
      .Add(x => x.Message, Msg("Outbound"))
      .Add(x => x.Status, MessageBubble.SendStatus.Failed));
    Assert.Contains("failed", cut.Find(".msg-bubble").ClassList);
    Assert.Contains("Failed to send", cut.Markup);
  }

  // ── PHN-3 T2: the speak button ───────────────────────────────────────────────────────────────

  [Fact]
  public void InboundBubble_RendersASpeakButton()
  {
    Register();
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"));

    // ⛔ The wrapper is the assertion, not just the button. Handoff :325 puts the control in the
    // gutter OUTSIDE .msg-bubble; a button nested inside the bubble would satisfy a bare
    // "a .msg-speak-btn exists" check while breaking the layout the §Ph CSS is written for.
    var row = cut.Find(".msg-row-inbound");
    Assert.NotNull(row.QuerySelector(".msg-bubble"));
    Assert.NotNull(row.QuerySelector(".msg-speak-btn"));
    Assert.Contains("Read this message aloud", cut.Markup);
  }

  [Fact]
  public void OutboundBubble_RendersNoSpeakButtonAndNoWrapper()
  {
    // ⭐ Handoff :323 — inbound only. "You do not need the console to read back something you
    // sent", and it is what leaves the entire outbound render path untouched.
    Register();
    var cut = RenderBubble(Msg("Outbound", "Dinner at 7?"));

    Assert.Empty(cut.FindAll(".msg-speak-btn"));
    Assert.Empty(cut.FindAll(".msg-row-inbound"));
  }

  [Fact]
  public void BubbleWithNullText_RendersNoSpeakButton()
  {
    // C-114. The bubble renders "(no text)"; a helper fed DisplayText rather than Message.Text
    // would compose an utterance out of that placeholder and the console would say the words
    // "no text" to the room. The button must be ABSENT, not present-and-disabled.
    Register();
    var cut = RenderBubble(Msg("Inbound", null));

    Assert.Contains("(no text)", cut.Markup);
    Assert.Empty(cut.FindAll(".msg-speak-btn"));
  }

  [Fact]
  public async Task TappingSpeak_PostsTheComposedUtteranceNotTheRawBody()
  {
    // The seam between Task 2 and Task 3. The fixture is an MMS-prefixed body with a resolved
    // sender, so the raw body and the composed utterance differ in TWO ways — the stripped prefix
    // and the added lead-in — and a straight pass-through of Message.Text fails on both.
    Register();
    var cut = RenderBubble(Msg("Inbound", "+15551234567 - Dinner at 7?"), senderName: "Jane");

    await cut.Find(".msg-speak-btn").ClickAsync(new MouseEventArgs());

    var body = Assert.Single(_handler.Bodies);
    using var document = System.Text.Json.JsonDocument.Parse(body);
    Assert.Equal(
      "Message from Jane. Dinner at 7?", document.RootElement.GetProperty("text").GetString());
    Assert.Equal("a message from Jane", document.RootElement.GetProperty("label").GetString());
    Assert.Equal("Speech", document.RootElement.GetProperty("kind").GetString());
  }

  [Fact]
  public async Task ASecondTapWhileTheFirstIsInFlight_StartsOnlyOnePlayback()
  {
    // ⭐ A double-tap is the DEFAULT gesture on a wall panel. _playbackId is cleared before the
    // awaited POST, so without _starting the second tap sees a null id, falls through every "is
    // this mine" predicate, and starts a second playback — which the server's replacement arm
    // honours by tearing the first one down mid-sentence.
    //
    // ⚠ NO SLEEP, and CLAUDE.md § Test Timing is why: this synchronises on the OBSERVATION rather
    // than on elapsed time. The stub handler parks on a TaskCompletionSource the test owns, so
    // both taps are issued while the first request is provably still in flight, and the assertion
    // runs after both handlers have completed rather than after a hopeful delay.
    var gate = new TaskCompletionSource();
    Register(gate: gate);
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"));

    var button = cut.Find(".msg-speak-btn");
    var firstTap = button.ClickAsync(new MouseEventArgs());
    var secondTap = button.ClickAsync(new MouseEventArgs());

    gate.SetResult();
    await firstTap;
    await secondTap;

    Assert.Single(_handler.Bodies);
  }

  [Fact]
  public async Task ASecondTapWhileStopping_DoesNotStartANewPlayback()
  {
    // ⭐⭐ THE OTHER HALF OF THE DOUBLE-TAP, AND _starting NEVER COVERED IT. The stop arm used to
    // null _playbackId BEFORE awaiting the DELETE. ComponentBase re-renders at that await, so the
    // button flipped to play_arrow while the console was still speaking — actively inviting the
    // second tap — and that tap found Mine null, fell through every "is this mine" predicate into
    // the START arm, and POSTed a new playback: the message began reading again from the top.
    // Measured against the original code: Deletes == 1, Posts == 2.
    //
    // The fix is to RETAIN the handle and let the server's Stopped broadcast return the button to
    // rest, which is what VoicemailPlayer.razor:394-395 has always done and what
    // MainLayout.razor:1415-1417 states as the rule for this same control ("No optimistic hide").
    //
    // ⚠ NO SLEEP — CLAUDE.md § Test Timing. The stub parks the DELETE on a TaskCompletionSource the
    // test owns, so the second tap is issued while the stop is provably still in flight.
    var stopGate = new TaskCompletionSource();
    Register(deleteGate: stopGate);
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"));

    await cut.Find(".msg-speak-btn").ClickAsync(new MouseEventArgs());
    await BroadcastAsync(cut, Snapshot("Playing"));      // now live: the button is the stop control

    var button = cut.Find(".msg-speak-btn");
    var firstStop = button.ClickAsync(new MouseEventArgs());
    var secondStop = button.ClickAsync(new MouseEventArgs());

    stopGate.SetResult();
    await firstStop;
    await secondStop;

    // ONE post, for the one playback that was ever started. A second is the bug.
    Assert.Single(_handler.Bodies);

    // Two stops is fine and is what a retained handle produces — the server owns idempotence here.
    Assert.InRange(_handler.Deletes, 1, 2);
  }

  [Fact]
  public async Task AFailedSnapshot_RaisesTheSpeakFailedCallbackExactlyOnce()
  {
    // ⭐⭐ THE FAILURE THAT ACTUALLY HAPPENS. OnSpeakFailed used to fire only for a SYNCHRONOUS
    // refusal, but the API answers 202 Accepted and then broadcasts a Failed snapshot carrying
    // FailureReason "SpeechSynthesisFailed" — driven live three times during PHN-3's UAT, where the
    // button spun, returned to rest, and the user was told nothing at all. Handoff §B4's Engine
    // error row specifies "back to Rest PLUS a toast", and plan §0.3 consequence 2 calls that toast
    // "the whole of the UX" now that synthesis is a cloud round trip.
    //
    // ⚠ EXACTLY ONCE. ConsolePlaybackState.cs:66-71 RETAINS a terminal snapshot until a new
    // playback replaces it, deliberately — so the same Failed snapshot arrives again on every later
    // broadcast, and without _failureReportedFor the room gets a fresh toast each time.
    Register();
    var reasons = new List<string?>();
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"), onSpeakFailed: reasons.Add);

    await cut.Find(".msg-speak-btn").ClickAsync(new MouseEventArgs());
    await BroadcastAsync(cut, Snapshot("Failed", failureReason: "SpeechSynthesisFailed"));

    Assert.Equal("SpeechSynthesisFailed", Assert.Single(reasons));

    // The retained snapshot, re-delivered. Still one toast.
    await BroadcastAsync(cut, Snapshot("Failed", failureReason: "SpeechSynthesisFailed"));

    Assert.Single(reasons);

    // And the button is back at rest, which is the other half of the §B4 row.
    Assert.DoesNotContain("speaking", cut.Find(".msg-speak-btn").ClassList);
    Assert.Contains("Read this message aloud", cut.Markup);
  }

  [Fact]
  public async Task WhenTheAmbientSnapshotIsAnotherId_TheButtonIsAtRest()
  {
    // C-110 / handoff §B4's "Replaced" row. There is ONE attended playback by construction, so
    // when another item starts, the ambient snapshot's Id stops being ours and this button returns
    // to rest SILENTLY — no client bookkeeping, no shared "currently speaking bubble id".
    Register();
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"));
    await cut.Find(".msg-speak-btn").ClickAsync(new MouseEventArgs());

    // Ours, playing: the button is the stop affordance.
    await BroadcastAsync(cut, Snapshot("Playing"));
    Assert.Contains("speaking", cut.Find(".msg-speak-btn").ClassList);
    Assert.Contains("speaking", cut.Find(".msg-bubble").ClassList);

    // Somebody else's, playing: back to rest, and the cyan marks move off this bubble.
    await BroadcastAsync(cut, Snapshot("Playing", id: "evp-somebody-else"));
    Assert.DoesNotContain("speaking", cut.Find(".msg-speak-btn").ClassList);
    Assert.DoesNotContain("speaking", cut.Find(".msg-bubble").ClassList);
    Assert.Contains("Read this message aloud", cut.Markup);
  }

  [Fact]
  public async Task WaitingRendersTheSpinner_AndSaysWhy()
  {
    // C-107. PHN-1f's D28 queue means a Speech playback can legitimately sit in Waiting for up to
    // GvMedia:MaxQueuedWaitSeconds while an announcement finishes; the handoff's §B4 table predates
    // that state. It renders exactly as Preparing — one spinner — and differs only in the title,
    // which takes VoicemailPlayer's already-shipped copy. A bare spinner is the complaint D28
    // rejected, rendered.
    Register();
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"));
    await cut.Find(".msg-speak-btn").ClickAsync(new MouseEventArgs());

    await BroadcastAsync(cut, Snapshot("Waiting"));

    Assert.NotNull(cut.Find(".msg-speak-btn").QuerySelector(".spinner"));
    Assert.Equal(
      "Waiting for the announcement to finish…",
      cut.Find(".msg-speak-btn").GetAttribute("title"));

    // And Preparing renders the same spinner under different copy.
    await BroadcastAsync(cut, Snapshot("Preparing"));
    Assert.NotNull(cut.Find(".msg-speak-btn").QuerySelector(".spinner"));
    Assert.Equal("Preparing…", cut.Find(".msg-speak-btn").GetAttribute("title"));
  }

  [Fact]
  public void Dispose_UnsubscribesFromConsolePlaybackState()
  {
    // C-111. A conversation with forty messages mounts forty subscribers — the largest fan-out
    // this event has ever had — on a kiosk that runs for weeks.
    //
    // ⚠ This reflects over ConsolePlaybackState's field-like event, and the reason that is not the
    // vacuous shape ConsolePlaybackStateTests warns about is the FIRST assertion: it proves the
    // reflection found a live subscriber before disposal. If the event ever grows explicit
    // accessors, SubscriberCount throws rather than quietly answering zero, so this test cannot
    // start passing for the wrong reason.
    Register();
    var cut = RenderBubble(Msg("Inbound", "Dinner at 7?"));

    Assert.Equal(1, SubscriberCount(_console));

    cut.Instance.Dispose();

    Assert.Equal(0, SubscriberCount(_console));
  }

  private static int SubscriberCount(ConsolePlaybackState state)
  {
    var field = typeof(ConsolePlaybackState).GetField(
      nameof(ConsolePlaybackState.Changed), BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    return ((Delegate?)field.GetValue(state))?.GetInvocationList().Length ?? 0;
  }

  /// <summary>
  /// Answers every POST /api/audio/events with a fixed status, recording the request bodies, and
  /// optionally parks until the test releases it.
  /// </summary>
  private sealed class StubEventsHandler(
    HttpStatusCode status, TaskCompletionSource? gate, TaskCompletionSource? deleteGate = null)
    : HttpMessageHandler
  {
    private int _deletes;

    /// <summary>POST bodies only — a DELETE carries none, so a count is the only thing to record.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>How many stops were issued. Interlocked because two can be in flight at once.</summary>
    public int Deletes => Volatile.Read(ref _deletes);

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.Method == HttpMethod.Delete)
      {
        Interlocked.Increment(ref _deletes);
        if (deleteGate is not null)
        {
          await deleteGate.Task;
        }

        return new HttpResponseMessage(HttpStatusCode.NoContent);
      }

      if (request.Content is not null)
      {
        Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
      }

      if (gate is not null)
      {
        await gate.Task;
      }

      const string body = """
        {"id":"evp-speech-1","kind":"Speech","label":"a message from Jane","state":"Preparing",
         "duration":null,"positionAtBroadcast":"00:00:00",
         "broadcastAtUtc":"2026-09-05T00:00:00+00:00","failureReason":null}
        """;

      return new HttpResponseMessage(status)
      {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
      };
    }
  }
}
