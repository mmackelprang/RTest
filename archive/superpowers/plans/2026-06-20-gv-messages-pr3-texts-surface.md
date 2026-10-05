# GV Messages — PR3: Texts Surface

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the **Texts** half of the Messages feed: thread-list rows (all states), the master-detail conversation (inbound/outbound bubbles, append-in-place on inbound when open — no toast), the `GvSmsReceived` SignalR path, then the **compose/reply + new-recipient composer — feature-flagged OFF** and wired to a stubbed send method with the full optimistic/sending/sent/failed-with-preserved-text + 429/in-flight/degraded guardrails. On-screen text entry reuses RadioConsole's **existing global virtual keyboard** (auto-shows on input focus) — no new keyboard component is built.

> **Supersedes the design spec's "build a touch keyboard" recommendation — reuse the existing global virtual keyboard.** RadioConsole already ships a fully-working global on-screen keyboard (station naming and other inputs use it today). The Designer note calling `virtual-keyboard.css` "unconsumed" was incorrect. Owner directive: reuse the existing keyboard for ALL SMS/text input; do NOT build a new keyboard component or re-skin `virtual-keyboard.css`.

**Owner-baked decisions in scope here:**
- Texts fold into the unified feed (Designer Option C): in the feed a text is a **thread row** (one per conversation); tapping opens the conversation in the 520px detail pane.
- **Compose/reply + new-recipient composer are built in full but feature-flagged OFF** (decision 5): `GvBridgeSendService.SendAsync` throws `SendNotAvailableException` until `RotaryPhone:Gv:SendEnabled=true` AND RotaryPhone's `POST /api/gvbridge/sms/send` ships.
- On-screen text entry = the **existing global virtual keyboard** (`src/Radio.Web/wwwroot/js/virtual-keyboard.js`, loaded globally in `App.razor`). It auto-shows whenever any text-like input receives focus, so compose/new-recipient just use plain `<input>`/`<textarea>` — no new component, no new JS. The recipient field opts into a numeric layout via the `data-keyboard` attribute. If explicit control is ever needed, use `window.virtualKeyboardInterop.show(element)` / `.hide()` / `.toggle(element)` / `.isVisible()` (or the ES-module `toggleForInput(selector)`).

**Sources of truth (do not redesign):**
- Design handoff Screens C/D + Compose spec + §Ph bubbles: `docs/design-handoffs/HANDOFF-phone-messages-voicemail-sms.md`
- ADR-022 D5 (push), D7 (flagged send), §8 (config): `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`
- Contract (SMS read HTTP + push + provisional `direction`/`text`): `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-voicemail-sms-ui-handoff.md`

**Tech stack:** Blazor Server, Radzen, minimal JS for auto-scroll-to-bottom, `design-system.css` tokens. On-screen text entry uses the existing global virtual keyboard (no changes to `virtual-keyboard.js`/`.css`).

**Dependencies:** **PR1 must be merged** (DTOs, `GvBridgeApiService` SMS reads, `PhoneHubService.GvSmsReceived`, `GvBridgeStatusService`, `PhoneUnreadState`, `PhoneMessagesPanel`, the `FeedItem` interleave projection from PR2's Task 5). PR3 is independent of PR2 except for the shared `PhoneMessagesPanel` + `PhonePage` and the `FeedItem` projection introduced in PR2 — **if PR3 lands before PR2**, the implementer must introduce the `FeedItem` projection here instead (noted inline). Recommended order: PR1 → PR2 → PR3.

---

## File Map

### New files

| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` | Flagged `SendAsync` + `SendNotAvailableException` + in-flight/429/no-auto-retry guardrails. |
| `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` | Thread list (in-feed rows) + conversation detail (bubbles + compose bar + new-recipient composer). |
| `src/Radio.Web/Components/Pages/MessageBubble.razor` | Single inbound/outbound bubble with status glyph (sending/sent/failed). |
| `tests/Radio.Web.Tests/Services/GvBridgeSendServiceTests.cs` | Flag-off throws; in-flight guard; 429 mapping; degraded gate. |
| `tests/Radio.Web.Tests/Components/MessageBubbleTests.cs` | inbound/outbound/sending/failed render; null text placeholder; unknown direction → inbound. |
| `tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs` | thread-list states; append-in-place; compose disabled when flag off / degraded. |

### Modified files

| File | Changes |
|------|---------|
| `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` | Render text **thread rows** in `texts` + `all` filters; host the conversation in the detail pane; own thread list + states + new-inbound bump/append. |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Fetch threads; subscribe `GvSmsReceived`; fold unread-thread count into `UnreadSum`; toast (suppressed when thread open); pass send service + flag down. |
| `src/Radio.Web/Program.cs` | Register typed `HttpClient` for `GvBridgeSendService` (+ auth handler); read `RotaryPhone:Gv:SendEnabled`. |
| `src/Radio.Web/wwwroot/css/design-system.css` §Ph | Add `.msg-*` bubble spec (handoff verbatim) + thread-row + composer styles. |
| `design/FUTURE-WORK.md` | Send flagged; confirm `SendSmsResponse` shape before wiring; open thread to RotaryPhone. |

---

## Chunk 1: GvBridgeSendService (flagged, stubbed)

### Task 1: Create GvBridgeSendService

**Files:**
- Create: `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs`
- Test: `tests/Radio.Web.Tests/Services/GvBridgeSendServiceTests.cs`

> ADR D7: a single send method behind `RotaryPhone:Gv:SendEnabled` (default false). Today throws `SendNotAvailableException`. Guardrails (in-flight single-flight per thread, 429 → typed result + preserve text + no auto-retry, degraded gate via `GvBridgeStatusService.IsAvailable`) are specified now, enforced when wired. The send path is isolated from the read client so it's obvious and easy to light up.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class GvBridgeSendServiceTests
{
  private static GvBridgeSendService Build(bool sendEnabled, HttpClient client,
    GvBridgeStatusService status)
  {
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:SendEnabled"] = sendEnabled.ToString() })
      .Build();
    return new GvBridgeSendService(client,
      NullLogger<GvBridgeSendService>.Instance, config, status);
  }

  private static GvBridgeStatusService AvailableStatus()
  {
    var s = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    s.ApplyStatusForTest(new Radio.Web.Models.GvBridgeStatusDto { Available = true });
    return s;
  }

  [Fact]
  public async Task Throws_WhenFlagOff()
  {
    var client = new HttpClient(new MockHttpHandler("{}")) { BaseAddress = new Uri("http://radio:5004") };
    var svc = Build(sendEnabled: false, client, AvailableStatus());

    await Assert.ThrowsAsync<SendNotAvailableException>(
      () => svc.SendAsync("t1", "hi"));
  }

  [Fact]
  public async Task Throws_WhenDegraded_EvenIfFlagOn()
  {
    var client = new HttpClient(new MockHttpHandler("{}")) { BaseAddress = new Uri("http://radio:5004") };
    var status = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    status.ApplyStatusForTest(null);  // degraded
    var svc = Build(sendEnabled: true, client, status);

    await Assert.ThrowsAsync<SendUnavailableException>(
      () => svc.SendAsync("t1", "hi"));
  }

  [Fact]
  public async Task RateLimited_MapsTo429Result_WhenFlagOn()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.TooManyRequests);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    await Assert.ThrowsAsync<SendRateLimitedException>(
      () => svc.SendAsync("t1", "hi"));
  }

  [Fact]
  public async Task InFlightGuard_RejectsSecondConcurrentSendOnSameThread()
  {
    var handler = new MockHttpHandler(System.Threading.Timeout.InfiniteTimeSpan); // never completes
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    var first = svc.SendAsync("t1", "one");          // takes the slot
    await Assert.ThrowsAsync<SendInFlightException>(
      () => svc.SendAsync("t1", "two"));             // rejected while first outstanding
  }
}
```

> If `MockHttpHandler` cannot model an infinite delay, the implementer may use a `TaskCompletionSource`-backed handler local to the test. The contract is: a second `SendAsync` on the same thread while one is outstanding throws `SendInFlightException`.

- [ ] **Step 2: Implement the service + typed exceptions**

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Services.ApiClients;

/// <summary>SMS send is not built on RotaryPhone yet — flag off → "coming soon".</summary>
public class SendNotAvailableException : Exception
{
  public SendNotAvailableException()
    : base("Texting send is coming soon.") { }
}

/// <summary>GV bridge degraded (reconnecting) — send is gated regardless of flag.</summary>
public class SendUnavailableException : Exception
{
  public SendUnavailableException()
    : base("Google Voice is reconnecting.") { }
}

/// <summary>HTTP 429 — caller keeps the text, shows "Sending too fast", no auto-retry.</summary>
public class SendRateLimitedException : Exception { }

/// <summary>A send is already outstanding on this thread (single-flight guard).</summary>
public class SendInFlightException : Exception { }

/// <summary>
/// Isolated, flagged GV SMS send seam (ADR-022 D7). Read client stays
/// unconditionally safe; this is the only write path. Guardrails: single-flight
/// per thread, 429 → typed exception + preserve text + NEVER auto-retry, and a
/// degraded gate (GvBridgeStatusService.IsAvailable). The endpoint
/// (POST /api/gvbridge/sms/send) does not exist yet — flag default false.
/// </summary>
public class GvBridgeSendService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<GvBridgeSendService> _logger;
  private readonly IConfiguration _configuration;
  private readonly GvBridgeStatusService _status;
  private readonly ConcurrentDictionary<string, byte> _inFlight = new();

  public GvBridgeSendService(HttpClient httpClient,
    ILogger<GvBridgeSendService> logger,
    IConfiguration configuration,
    GvBridgeStatusService status)
  {
    _httpClient = httpClient;
    _logger = logger;
    _configuration = configuration;
    _status = status;
  }

  public bool SendEnabled => _configuration.GetValue("RotaryPhone:Gv:SendEnabled", false);

  public async Task<SmsMessageDto> SendAsync(string threadId, string text,
    CancellationToken ct = default)
  {
    if (!SendEnabled) throw new SendNotAvailableException();
    if (!_status.IsAvailable) throw new SendUnavailableException();
    if (!_inFlight.TryAdd(threadId, 1)) throw new SendInFlightException();

    try
    {
      // Wired when the endpoint ships. Request ≈ { threadId, text } → created
      // SmsMessageDto. Confirm SendSmsResponse shape first (contract risk #5).
      var response = await _httpClient.PostAsJsonAsync(
        "/api/gvbridge/sms/send", new SendSmsRequest(threadId, text), ct);

      if (response.StatusCode == HttpStatusCode.TooManyRequests)
        throw new SendRateLimitedException();
      if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"Send failed: {(int)response.StatusCode}");

      var result = await response.Content
        .ReadFromJsonAsync<SendSmsResponse>(cancellationToken: ct);
      if (result?.Message == null)
        throw new HttpRequestException("Send returned no message");
      return result.Message;
    }
    finally
    {
      _inFlight.TryRemove(threadId, out _);
    }
  }
}
```

- [ ] **Step 3: Register in Program.cs**

After the `GvBridgeApiService` client registration (PR1 added the auth handler there), add a parallel typed client + the auth handler. `GvBridgeStatusService` is already a singleton (PR1):

```csharp
builder.Services.AddHttpClient<GvBridgeSendService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});
```

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeSendServiceTests"
git add src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs src/Radio.Web/Program.cs tests/Radio.Web.Tests/Services/GvBridgeSendServiceTests.cs
git commit -m "feat(web): add flagged GvBridgeSendService with in-flight/429/degraded guardrails"
```

---

## Chunk 2: MessageBubble component

### Task 2: MessageBubble.razor + §Ph bubble CSS

**Files:**
- Create: `src/Radio.Web/Components/Pages/MessageBubble.razor`
- Modify: `src/Radio.Web/wwwroot/css/design-system.css` (add the §Ph bubble block verbatim from the handoff)
- Test: `tests/Radio.Web.Tests/Components/MessageBubbleTests.cs`

> §Ph bubbles (handoff): inbound left on `.surface-raised`; outbound right on `--accent-surface`. Outbound status glyph: sending spinner / sent check / failed ⚠. Defensive: `text == null` → "(no text)" placeholder, never crash; `direction` not exactly "Outbound" → inbound.

- [ ] **Step 1: Add the §Ph bubble CSS verbatim**

Append the handoff's §Ph block to `design-system.css` (the `.msg-list`, `.msg-bubble`(+`.inbound`/`.outbound`/`.sending`/`.failed`), `.msg-meta`(+`.msg-status-sent`/`.msg-status-fail`), `.msg-day-sep`, `.unread-dot` rules). `.unread-dot` may already exist from PR1/PR2 — if so, do not duplicate it.

- [ ] **Step 2: Write the failing bUnit test**

```csharp
using Bunit;
using Radio.Web.Models;
using Radio.Web.Components.Pages;

namespace Radio.Web.Tests.Components;

public class MessageBubbleTests : TestContext
{
  private SmsMessageDto Msg(string direction = "Inbound", string? text = "hi") =>
    new("m1", "t1", direction, "+15551234567", text, DateTime.UtcNow, false);

  [Fact]
  public void Inbound_AlignsLeft()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("Inbound")));
    Assert.Contains("inbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void Outbound_AlignsRight()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("Outbound")));
    Assert.Contains("outbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void UnknownDirection_TreatedAsInbound()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("garbage")));
    Assert.Contains("inbound", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void NullText_RendersPlaceholder()
  {
    var cut = RenderComponent<MessageBubble>(p => p.Add(x => x.Message, Msg("Inbound", null)));
    Assert.Contains("(no text)", cut.Markup);
  }

  [Fact]
  public void Sending_ShowsDimAndSpinner()
  {
    var cut = RenderComponent<MessageBubble>(p => p
      .Add(x => x.Message, Msg("Outbound"))
      .Add(x => x.Status, MessageBubble.SendStatus.Sending));
    Assert.Contains("sending", cut.Find(".msg-bubble").ClassList);
  }

  [Fact]
  public void Failed_ShowsRetryAffordance()
  {
    var cut = RenderComponent<MessageBubble>(p => p
      .Add(x => x.Message, Msg("Outbound"))
      .Add(x => x.Status, MessageBubble.SendStatus.Failed));
    Assert.Contains("failed", cut.Find(".msg-bubble").ClassList);
    Assert.Contains("Failed to send", cut.Markup);
  }
}
```

- [ ] **Step 3: Implement MessageBubble.razor**

```razor
@using Radio.Web.Models

<div class="msg-bubble @DirectionClass @StatusClass"
     @onclick="OnFailedClick" role="@(IsFailed ? "button" : null)">
  <span class="msg-text">@DisplayText</span>
  <span class="msg-meta">
    <span class="msg-time">@Message.SentAt.ToLocalTime().ToString("h:mm tt")</span>
    @if (IsOutbound)
    {
      @switch (Status)
      {
        case SendStatus.Sending:
          <span class="spinner"></span>
          break;
        case SendStatus.Sent:
          <RadzenIcon Icon="done" class="msg-status-sent" />
          break;
        case SendStatus.Failed:
          <span class="msg-status-fail">
            <RadzenIcon Icon="error_outline" /> Failed to send
          </span>
          break;
      }
    }
  </span>
</div>

@code {
  public enum SendStatus { None, Sending, Sent, Failed }

  [Parameter, EditorRequired] public SmsMessageDto Message { get; set; } = default!;
  [Parameter] public SendStatus Status { get; set; } = SendStatus.None;
  [Parameter] public EventCallback OnRetry { get; set; }

  private bool IsOutbound => GvDirection.IsOutbound(Message.Direction);  // unknown → inbound
  private bool IsFailed => Status == SendStatus.Failed;
  private string DirectionClass => IsOutbound ? "outbound" : "inbound";
  private string StatusClass => Status switch
  {
    SendStatus.Sending => "sending",
    SendStatus.Failed => "failed",
    _ => ""
  };
  private string DisplayText =>
    string.IsNullOrEmpty(Message.Text) ? "(no text)" : Message.Text!;

  private async Task OnFailedClick()
  {
    if (IsFailed) await OnRetry.InvokeAsync();
  }
}
```

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~MessageBubbleTests"
git add src/Radio.Web/Components/Pages/MessageBubble.razor src/Radio.Web/wwwroot/css/design-system.css tests/Radio.Web.Tests/Components/MessageBubbleTests.cs
git commit -m "feat(web): add MessageBubble (inbound/outbound, status glyph, null-text safe)"
```

---

## Chunk 3: On-screen keyboard — reuse the existing global virtual keyboard (no build)

> **Supersedes the design spec's "build a touch keyboard" recommendation.** RadioConsole already has a fully-working global on-screen keyboard in active use (station naming and other inputs). There is **nothing to build or re-skin** here — this chunk is a reference for how compose/new-recipient inputs hook into it, consumed by Chunk 4.

**No new files, no edits to `virtual-keyboard.js` / `virtual-keyboard.css`.** Verified facts about the existing keyboard:

- **Loaded globally** in `src/Radio.Web/Components/App.razor` (≈ line 25 `css/virtual-keyboard.css`, ≈ line 59 `<script type="module" src="js/virtual-keyboard.js?v=2">`). It is already on every page — no per-page include is needed.
- **Auto-shows on focus.** `src/Radio.Web/wwwroot/js/virtual-keyboard.js` implements a `VirtualKeyboard` class that auto-shows whenever any text-like input receives focus (designed for the no-physical-keyboard kiosk). So a plain `<input>`/`<textarea>` already gets a keyboard — no wiring required.
- **Layout selection** via a `data-keyboard` attribute on the input element. The E.164 new-recipient field requests the numeric layout via `data-keyboard` (whatever the existing numeric layout name is — implementer confirms the value from `virtual-keyboard.js`).
- **Explicit control (only if needed):** `window.virtualKeyboardInterop.show(element)`, `.hide()`, `.toggle(element)`, `.isVisible()`; plus the ES-module export `toggleForInput(selector)`. Use existing JS-interop patterns if the composer ever needs to force-show/hide; the compose flow does **not** need this for the default focus-driven behavior.

**Consequence for compose (Chunk 4):** the compose/reply message field and the new-recipient field are ordinary `<input>`/`<textarea>` elements (the recipient field adds `data-keyboard` for the numeric layout). The global keyboard appears on focus. The composer owns only the text buffer / send button / write-path states — there is no keyboard component to render or flag.

---

## Chunk 4: PhoneTextsPanel (thread list + conversation + compose)

### Task 3: PhoneTextsPanel.razor

**Files:**
- Create: `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`
- Test: `tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs`

> Screens C/D. Thread list states: loading (skeleton ~6), loaded, empty ("No conversations yet."), error+Retry, refresh-error (keep last good + warning). Conversation: bubbles newest-at-bottom, day separators, auto-scroll-to-bottom on open + new message. Compose bar + new-recipient composer: **flag-gated** (the global virtual keyboard auto-shows on input focus — nothing to render or gate for the keyboard itself). Outbound write-path states: optimistic sending → sent (check) → confirmed (de-dupe vs push) → failed (red edge, Retry, **preserve text**, never auto-retry). New inbound while open = append silently, no toast. Degraded gate: Send disabled + "Texting unavailable" pill.

- [ ] **Step 1: Write the failing bUnit tests (states + flag/degraded gating)**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Components.Pages;

namespace Radio.Web.Tests.Components;

public class PhoneTextsPanelTests : TestContext
{
  private void Register(bool sendEnabled, bool available)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
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
}
```

- [ ] **Step 2: Implement PhoneTextsPanel.razor**

The panel renders two things depending on host context: **thread rows** (consumed by `PhoneMessagesPanel` for the feed) and the **conversation pane** (rendered in the detail host). Keep both in one component with a `Mode` parameter, or split into `ThreadRow` + conversation — implementer's choice; the simplest is one component exposing a `RenderThreadRows` fragment and a `RenderConversation` fragment. Skeleton below shows the conversation + compose with the full write-path states:

```razor
@using Radio.Web.Models
@using Radio.Web.Services
@using Radio.Web.Services.ApiClients
@inject GvBridgeSendService SendService
@inject GvBridgeStatusService GvStatus
@inject IJSRuntime JS
@inject NotificationService Notifications

@* ── Conversation pane ──────────────────────────────────────── *@
<div class="texts-conversation">
  <div class="texts-conv-header">
    <button type="button" class="phone-btn-sm btn-ghost"
            aria-label="Back to conversations" @onclick="OnBack">
      <RadzenIcon Icon="chevron_left" />
    </button>
    <div class="texts-conv-title">
      <span class="texts-conv-name">@HeaderName</span>
      <span class="texts-conv-number">@HeaderNumber</span>
    </div>
  </div>

  <div class="msg-list" aria-live="polite" @ref="_listEl">
    @if (Messages == null && Loading)
    {
      @* skeleton handled by caller / simple shimmer *@
    }
    else if (Messages != null)
    {
      DateTime? lastDay = null;
      @foreach (var m in Ordered(Messages))
      {
        var day = m.SentAt.ToLocalTime().Date;
        if (lastDay != day)
        {
          lastDay = day;
          <div class="msg-day-sep">@DaySeparator(day)</div>
        }
        <MessageBubble Message="m"
                       Status="StatusFor(m)"
                       OnRetry="@(() => RetrySend(m))" />
      }
    }
  </div>

  @if (GvStatus.IsAvailable)
  {
    <div class="texts-compose">
      <input class="phone-input @(SendService.SendEnabled ? "compose-send-enabled" : "")"
             placeholder="Message"
             aria-label="Type a message"
             value="@_draft"
             @oninput="@(e => _draft = e.Value?.ToString() ?? "")"
             disabled="@(!SendService.SendEnabled)" />
      <button type="button" class="phone-btn-sm"
              disabled="@(!CanSend)" @onclick="SendDraftAsync">Send</button>
    </div>
  }
  else
  {
    <div class="texts-compose">
      <span class="phone-pill amber" title="Google Voice is reconnecting.">Texting unavailable</span>
    </div>
  }
</div>

@code {
  [Parameter] public List<SmsThreadDto>? Threads { get; set; }
  [Parameter] public bool Loading { get; set; }
  [Parameter] public string? OpenThreadId { get; set; }
  [Parameter] public List<SmsMessageDto>? Messages { get; set; }
  [Parameter] public string? HeaderName { get; set; }
  [Parameter] public string? HeaderNumber { get; set; }
  [Parameter] public EventCallback OnBack { get; set; }
  [Parameter] public EventCallback<SmsMessageDto> OnOptimisticAppend { get; set; }

  private string _draft = "";
  // Optimistic + failed tracking keyed by a client-generated temp id.
  private readonly Dictionary<string, MessageBubble.SendStatus> _statusById = new();
  private readonly HashSet<string> _sending = new();
  private ElementReference _listEl;

  private bool CanSend =>
    SendService.SendEnabled && GvStatus.IsAvailable
    && !string.IsNullOrWhiteSpace(_draft) && _sending.Count == 0;

  private static IEnumerable<SmsMessageDto> Ordered(List<SmsMessageDto> m) =>
    m.OrderBy(x => x.SentAt);

  private MessageBubble.SendStatus StatusFor(SmsMessageDto m) =>
    _statusById.TryGetValue(m.Id, out var s) ? s : MessageBubble.SendStatus.None;

  private async Task SendDraftAsync()
  {
    if (!CanSend) return;
    var text = _draft;
    var tempId = $"temp-{Guid.NewGuid():N}";
    var optimistic = new SmsMessageDto(tempId, OpenThreadId ?? "",
      GvDirection.Outbound, HeaderNumber ?? "", text, DateTime.UtcNow, true);

    _statusById[tempId] = MessageBubble.SendStatus.Sending;
    _sending.Add(tempId);
    _draft = "";                                    // clear input
    await OnOptimisticAppend.InvokeAsync(optimistic); // parent appends to Messages
    await ScrollToBottomAsync();

    try
    {
      var created = await SendService.SendAsync(OpenThreadId ?? "", text);
      _statusById.Remove(tempId);                   // de-dupe: confirmed message replaces optimistic
      _statusById[created.Id] = MessageBubble.SendStatus.Sent;
      // Parent reconciles the optimistic temp row with `created` (or the push).
    }
    catch (SendRateLimitedException)
    {
      _statusById[tempId] = MessageBubble.SendStatus.Failed;
      _draft = text;                                // PRESERVE text
      Notifications.Notify(NotificationSeverity.Error, "Slow down",
        "Sending too fast — wait a moment.");
    }
    catch (SendNotAvailableException)
    {
      _statusById[tempId] = MessageBubble.SendStatus.Failed;
      _draft = text;
      Notifications.Notify(NotificationSeverity.Info, "Coming soon",
        "Texting send isn't available yet.");
    }
    catch (Exception)                               // SendUnavailable / network / non-200
    {
      _statusById[tempId] = MessageBubble.SendStatus.Failed;
      _draft = text;                                // PRESERVE; never auto-retry
      Notifications.Notify(NotificationSeverity.Error, "Message not sent",
        "Couldn't send your message. Try again.");
    }
    finally
    {
      _sending.Remove(tempId);
      await InvokeAsync(StateHasChanged);
    }
  }

  private async Task RetrySend(SmsMessageDto m)
  {
    _draft = m.Text ?? "";                          // re-arm with the failed text
    await SendDraftAsync();
  }

  private async Task ScrollToBottomAsync()
  {
    try { await JS.InvokeVoidAsync("phoneScrollToBottom", _listEl); } catch { }
  }

  private static string DaySeparator(DateTime day)
  {
    var today = DateTime.Now.Date;
    if (day == today) return "Today";
    if (day == today.AddDays(-1)) return "Yesterday";
    return day.Year == today.Year ? day.ToString("MMM d") : day.ToString("MMM d, yyyy");
  }
}
```

> **Add `phoneScrollToBottom`** to a small JS file (or reuse an existing one): `window.phoneScrollToBottom = el => { if (el) el.scrollTop = el.scrollHeight; };`. The new-recipient composer (Screen D) is the same compose bar with a recipient `.phone-input` (a plain `<input>` carrying `data-keyboard` for the numeric layout so the **existing global virtual keyboard** opens numeric on focus) above an empty message region; implement it as a `_composingNew` mode in this component that, on success, transitions into the normal conversation for the resolved thread. Validation: block only obviously-empty/non-numeric; inline "Enter a valid phone number." on normalization failure (RotaryPhone normalizes to E.164). No keyboard component is rendered — the message field and recipient field are ordinary inputs.

- [ ] **Step 3: Compose / thread-row / conversation CSS (§Ph, no new tokens)**

Add thread-row (mirror the voicemail row chip pattern with a `chat_bubble` cyan chip + "You: " prefix on outbound previews), `.texts-conversation`/`.texts-conv-header`/`.texts-compose` layout. Reuse `.list-item-touch`, `.list-item-active`, `.phone-input`, `.phone-btn-sm`, `.phone-pill.amber`, `.msg-*`.

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhoneTextsPanelTests"
git add src/Radio.Web/Components/Pages/PhoneTextsPanel.razor src/Radio.Web/wwwroot/css/design-system.css tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs
git commit -m "feat(web): add PhoneTextsPanel conversation + compose (flag-gated) with write-path states"
```

---

## Chunk 5: Wire texts into the feed + new-inbound path

### Task 4: PhoneMessagesPanel — thread rows + conversation host

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor`

> Render **thread rows** in `texts` + `all` filters (interleaved via the `FeedItem` projection from PR2 Task 5; if PR3 lands first, introduce that projection here). Tapping a thread row sets `_openThreadId` and the detail pane renders the `PhoneTextsPanel` conversation. New-inbound (push): thread bumps to top + unread dot + badge ++ + toast WHEN the thread is not open; when it IS open, append the bubble in place, no toast, no unread increment.

- [ ] **Step 1: Add thread parameters + state**

```csharp
[Parameter] public List<SmsThreadDto>? Threads { get; set; }
[Parameter] public bool ThreadsLoading { get; set; }
[Parameter] public bool ThreadsError { get; set; }
[Parameter] public List<SmsMessageDto>? OpenThreadMessages { get; set; }
[Parameter] public EventCallback OnRetryThreads { get; set; }
[Parameter] public EventCallback<string> OnOpenThread { get; set; }   // threadId → parent loads messages + marks read
[Parameter] public EventCallback<SmsMessageDto> OnOptimisticAppend { get; set; }

private string? _openThreadId;
public string? OpenThreadId => _openThreadId;   // parent reads to suppress toast when open
```

- [ ] **Step 2: Thread rows in the feed + conversation in the detail**

In `RenderFeed` (texts/all), emit thread rows (one per conversation). In `RenderDetail`, when `_openThreadId != null`, render the `PhoneTextsPanel` conversation; else the empty hint (PR1) or call-detail (PR1):

```razor
@* RenderDetail, texts branch *@
@if (_openThreadId != null)
{
  <PhoneTextsPanel OpenThreadId="_openThreadId"
                   Threads="Threads"
                   Messages="OpenThreadMessages"
                   HeaderName="@OpenThreadName"
                   HeaderNumber="@OpenThreadNumber"
                   OnBack="@(() => _openThreadId = null)"
                   OnOptimisticAppend="OnOptimisticAppend" />
}
```

- [ ] **Step 3: Open/new-inbound helpers**

```csharp
private async Task OpenThread(string threadId)
{
  _openThreadId = threadId;
  await OnOpenThread.InvokeAsync(threadId);   // parent loads messages, marks thread read
}

// Called by the parent on GvSmsReceived for an inbound message.
public bool IsThreadOpen(string threadId) => _openThreadId == threadId;
```

- [ ] **Step 4: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor
git commit -m "feat(web): render text thread rows + conversation host with open/new-inbound wiring"
```

### Task 5: PhonePage — fetch threads, push, toast-when-not-open, count

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor`

- [ ] **Step 1: Add thread state + fetch**

```csharp
private List<SmsThreadDto>? _threads;
private bool _threadsLoading;
private bool _threadsError;
private string? _openThreadId;
private List<SmsMessageDto>? _openThreadMessages;
private readonly HashSet<string> _locallyReadThreads = new();

private async Task LoadThreadsAsync()
{
  _threadsLoading = true; _threadsError = false;
  var list = await GvBridgeApi.GetSmsThreadsAsync();
  if (list != null) _threads = ApplyLocalRead(list.Threads.ToList());
  else if (_threads == null) _threadsError = true;
  else NotificationService.Notify(NotificationSeverity.Warning, "Couldn't refresh",
         "Showing the last update.");
  _threadsLoading = false;
  PhoneUnread.Set(UnreadSum);
  await InvokeAsync(StateHasChanged);
}

private List<SmsThreadDto> ApplyLocalRead(List<SmsThreadDto> threads) =>
  threads.Select(t => _locallyReadThreads.Contains(t.ThreadId)
    ? t with { HasUnread = false } : t).ToList();

private async Task OpenThreadAsync(string threadId)
{
  _openThreadId = threadId;
  _locallyReadThreads.Add(threadId);                       // UI-local read
  var idx = _threads?.FindIndex(t => t.ThreadId == threadId) ?? -1;
  if (idx >= 0) _threads![idx] = _threads[idx] with { HasUnread = false };
  var messages = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);
  _openThreadMessages = messages?.Messages.ToList() ?? new();
  PhoneUnread.Set(UnreadSum);
  await InvokeAsync(StateHasChanged);
}
```

- [ ] **Step 2: Fold unread threads into the count + load on init + subscribe**

```csharp
private int UnreadThreadCount => _threads?.Count(t => t.HasUnread) ?? 0;
private int UnreadSum => MissedCallCount + UnheardVoicemailCount + UnreadThreadCount;
```

In `OnInitializedAsync`: `_ = LoadThreadsAsync();` and `PhoneHub.GvSmsReceived += OnGvSmsReceived;`.

- [ ] **Step 3: New-inbound handler (toast only when not open; append-in-place when open)**

```csharp
private void OnGvSmsReceived(SmsMessageDto msg)
{
  if (_disposed) return;
  var threadOpen = _openThreadId == msg.ThreadId
                   && (_messagesPanel?.IsThreadOpen(msg.ThreadId) ?? false);

  if (threadOpen)
  {
    // Append in place, no toast, no unread increment (hard rule).
    _openThreadMessages ??= new();
    if (_openThreadMessages.All(m => m.Id != msg.Id))
      _openThreadMessages.Add(msg);
  }
  else
  {
    // Bump thread + unread + toast.
    BumpThread(msg);
    NotificationService.Notify(NotificationSeverity.Info,
      ResolveThreadName(msg.ThreadId, msg.CounterpartyNumber),
      Truncate(msg.Text));
  }
  // Re-sort threads by activity.
  PhoneUnread.Set(UnreadSum);
  _ = InvokeAsync(StateHasChanged);
}

private void BumpThread(SmsMessageDto msg)
{
  _threads ??= new();
  var idx = _threads.FindIndex(t => t.ThreadId == msg.ThreadId);
  if (idx >= 0)
  {
    var t = _threads[idx] with
      { HasUnread = true, LastMessageAt = msg.SentAt, LastMessagePreview = msg.Text };
    _threads.RemoveAt(idx);
    _threads.Insert(0, t);
  }
  else
  {
    _threads.Insert(0, new SmsThreadDto(msg.ThreadId, msg.CounterpartyNumber,
      null, msg.SentAt, true, msg.Text));
  }
}

private static string Truncate(string? s, int n = 80) =>
  string.IsNullOrEmpty(s) ? "" : (s!.Length <= n ? s : s[..n] + "…");
```

- [ ] **Step 4: Optimistic-append reconciliation (de-dupe sent vs push)**

```csharp
private void OnOptimisticAppend(SmsMessageDto optimistic)
{
  _openThreadMessages ??= new();
  _openThreadMessages.Add(optimistic);
  _ = InvokeAsync(StateHasChanged);
}
// When the real message returns via GvSmsReceived/poll with matching text +
// recency (or a server id), drop the temp-* optimistic entry so it collapses to
// one bubble. Match: same ThreadId, Outbound, same Text, within ~30s.
```

- [ ] **Step 5: Pass into the panel + dispose**

Update the `PhoneMessagesPanel` usage to add `Threads`, `ThreadsLoading`, `ThreadsError`, `OpenThreadMessages="_openThreadMessages"`, `UnreadThreadCount="UnreadThreadCount"`, `OnRetryThreads="LoadThreadsAsync"`, `OnOpenThread="OpenThreadAsync"`, `OnOptimisticAppend="OnOptimisticAppend"`. Add `PhoneHub.GvSmsReceived -= OnGvSmsReceived;` to `Dispose`. Have `RefreshMessagesAsync` also `await LoadThreadsAsync();`.

- [ ] **Step 6: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "feat(web): wire SMS threads + GvSmsReceived (toast-when-not-open) + unread-thread count"
```

---

## Chunk 6: Documentation

### Task 6: FUTURE-WORK + the RotaryPhone open-thread deliverable

**Files:**
- Modify: `design/FUTURE-WORK.md`
- Create: `docs/HANDOFF-rotaryphone-gv-send-markread-auth.md` (the "open a thread back to RotaryPhone" deliverable)

- [ ] **Step 1: FUTURE-WORK** — record: compose built but **flag-gated** (`RotaryPhone:Gv:SendEnabled=false`); on-screen text entry reuses the **existing global virtual keyboard** (no new keyboard component — supersedes the design spec's "build a touch keyboard" note); `SendSmsResponse` shape **provisional** — confirm before wiring; lights up via one config flip + the endpoint.

- [ ] **Step 2: Write the open-thread handoff** (decision 4 deliverable + ADR contract risks). It must request from the RotaryPhone session:
  1. **GV mark-read** be pulled forward (so `MarkVoicemailReadAsync` + thread mark-read can persist; today UI-local). Reference decision 4.
  2. Keep the **voicemail audio endpoint unauthenticated (or token-in-query)** when `X-RotaryPhone-Auth` ships — a native `<audio>` cannot send a custom header (ADR §8.1 / contract risk #4).
  3. Confirm the **`POST /api/gvbridge/sms/send` request/response shape** (`SendSmsResponse` / created `SmsMessageDto`) before we wire `SendAsync` (ADR §7 / contract risk #5).
  4. Any **field-value corrections** from the live GV capture (provisional `direction`/`text`/`durationSeconds`).

- [ ] **Step 3: Commit**

```bash
git add design/FUTURE-WORK.md docs/HANDOFF-rotaryphone-gv-send-markread-auth.md
git commit -m "docs: flag send/keyboard; open RotaryPhone thread (mark-read, audio-auth, send shape)"
```

---

## Test Plan

**Unit / component (must pass before PR):**
- `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeSendServiceTests|FullyQualifiedName~MessageBubbleTests|FullyQualifiedName~PhoneTextsPanelTests"` — green.
- Full suite + build — no regressions, 0 warnings.

**Component assertions covered:**
- `SendAsync` throws `SendNotAvailableException` when flag off; `SendUnavailableException` when degraded; `SendRateLimitedException` on 429; `SendInFlightException` on a second concurrent send to the same thread.
- Bubble: inbound left / outbound right; unknown direction → inbound; null text → "(no text)"; sending → dim+spinner; failed → red edge + "Failed to send" + Retry.
- Thread list: loading → skeleton; empty → "No conversations yet."; compose not interactive when flag off / degraded.

**UAT (Tester, 1920×720, deploy first):**
1. Texts filter: thread list skeleton → loaded; tap a thread → conversation opens in the 520px detail pane (`list-item-active`).
2. Bubbles render inbound (left, raised) / outbound (right, cyan); day separators; auto-scroll to bottom on open.
3. Push an inbound SMS for a **closed** thread → thread bumps to top, unread dot, badge ++, **calm Info toast** with preview, music unaffected. For the **open** thread → bubble appends in place, **no toast**, no unread bump.
4. Open a thread → its unread clears (UI-local); Texts segment + rail + topbar badge decrement. Hard reload re-derives from `hasUnread` (UI-local caveat — expected).
5. Compose with `SendEnabled=false` (default): Send disabled / "coming soon" — the compose UI renders, the existing global virtual keyboard auto-shows when the message field is focused, but send no-ops with the calm message; typed text preserved.
6. Flip `RotaryPhone:Gv:SendEnabled=true` locally (endpoint still absent → 5xx): optimistic **sending** bubble (dim) → **failed** (red edge, Retry), **text preserved**, error toast; **no auto-retry**. (Full happy-path send validated when the real endpoint ships.)
7. Degraded (stop gvbridge): reconnecting banner (PR1) + Send disabled + "Texting unavailable" pill + tooltip; recovery re-enables.
8. New-recipient composer (＋ New): focusing the recipient field opens the existing global virtual keyboard in its numeric layout (via `data-keyboard`); invalid number → inline "Enter a valid phone number." keeping typed text.

**Self-review checklist (Planner ran):**
- Send is a flagged seam; default off throws "coming soon" (decision 5); compose built behind the flag. On-screen text entry reuses the existing global virtual keyboard (no new component — supersedes the design spec's "build a touch keyboard" recommendation).
- 429 → preserve text + no auto-retry; in-flight single-flight; degraded gate independent of flag.
- New inbound: toast only when thread not open; append-in-place + no toast when open (hard rule).
- GV SMS handler is `GvSmsReceived` on `/hub` — never confused with trunk SMS (contract risk #2).
- `direction` unknown → inbound; `text` null → placeholder (provisional-data rules).
- Open-thread-to-RotaryPhone deliverable written (decision 4 + contract risks #4/#5).
