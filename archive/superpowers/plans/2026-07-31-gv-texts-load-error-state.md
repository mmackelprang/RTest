# GV-8 — Distinguish a Failed Conversation Load from an Empty One

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `/phone` texts conversation pane capable of expressing **"this failed to load."** Today `GvBridgeApiService.GetSmsThreadMessagesAsync` collapses every non-2xx, timeout and deserialization error to a bare `null`, and `PhonePage.razor:632` turns that `null` into an empty list — after which a **502 and a genuinely empty conversation are byte-identical state**, and the pane renders **"Start the conversation below."** with no error, no spinner and no retry (UAT **F-1**, HIGH; **F-2**, MEDIUM).

**Architecture:** Three edits in a straight line, plus the outcome type they hang off. (1) The API client returns a small `GvResult<T>` carrying an outcome, an HTTP status and RotaryPhone's error discriminator instead of `T?`. (2) `PhonePage` grows `_openThreadLoading` / `_openThreadError` beside the `_threadsLoading` / `_threadsError` pair it already has, and stops coalescing failure into `new()`. (3) `PhoneTextsPanel`'s conversation branch gains the missing **error** branch, and `PhoneMessagesPanel` finally passes the `Loading` / `Error` / `OnRetry` parameters that have existed on `PhoneTextsPanel` since GV-3 but were never wired in this hosting.

**The pattern already exists one level up — copy it, do not invent one.** The thread *list* does this correctly today: `PhonePage.razor:595-616` keeps a dedicated `_threadsError` flag (and toasts "Showing the last update" when a cached list exists), and `PhoneMessagesPanel.razor:110-117` renders `cloud_off` + **"Couldn't load conversations."** + `Retry` off that flag. This plan is mostly that same pattern one level down, with the conversation's own copy string.

**Tech stack:** Blazor Server, Radzen (`RadzenIcon`), `design-system.css` tokens (`.empty-state`, `.empty-state-icon`, `.empty-state-text`, `.phone-btn-sm`, `.skeleton-list-row`), xUnit + bUnit. **No new component, no new CSS, no new config key, no new JS, no new hub subscription, no new auth posture.**

**Dependencies:** **GV-3 must be merged** (the texts surface: `PhoneTextsPanel`, `PhoneMessagesPanel`, the `PhonePage` thread-open path). It is. **Explicitly NOT blocked on RotaryPhone** — their two defects ([`CROSS-REPO-HANDOFFS.md`](../../queue/CROSS-REPO-HANDOFFS.md) #5/#6) make this failure *rare*; this row makes it *honest*. Neither subsumes the other.

---

## Sources of truth (do not re-derive)

- **The scope, with line citations:** `docs/BUILDER_QUEUE.md` row **GV-8**.
- **The root cause:** `docs/uat/2026-07-31-gv-live-data/F-1-DIAGNOSIS.md` § **Defect C** (ours) and § "Why F-6 works and F-1 does not" (the fix template). §§ Defects A and B are **RotaryPhone's** and are out of scope.
- **The findings:** `docs/uat/2026-07-31-gv-live-data/REPORT.md` — **F-1** (HIGH, primary), **F-2** (MEDIUM, folded in), **F-6** (the PASS that is the template).
- **The copy, already specified:** `docs/design-handoffs/HANDOFF-phone-dark-theme-and-scrollbars.md:310` — `**Error states (keep):** Couldn't load messages. / Couldn't load voicemail. / Couldn't load conversations. + Retry`. **Use verbatim. Do not invent new copy.**
- **The in-tree copy of that pattern to mirror:** `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor:110-117` (thread list) and `:143-149` (unified feed).

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Branch:** `fix/gv-texts-load-error-state` (assigned in the queue row). Branch before the first source commit.
- **Copy is fixed, verbatim, ASCII apostrophe:** `Couldn't load messages.` and button label `Retry`. Do not reword, do not add a subtitle, do not add a tooltip. The empty-state copy `Start the conversation below.` is **unchanged in wording** — only the condition under which it renders changes.
- **Preserve the `journalctl` probe string.** The documented server-side probe is `journalctl -u radio-web --since '-30min' | grep 'Failed to get GV SMS thread'` ([`ORDERING-NOTES.md`](../../queue/ORDERING-NOTES.md); F-1-DIAGNOSIS § Scope-affecting notes). The rewritten log statement **must keep the literal substring `Failed to get GV SMS thread`** or the only monitoring this surface has goes dark.
- **Do NOT touch `Uri.EscapeDataString(threadId)`.** The group-thread `%2F` failure is RotaryPhone's Defect B. Both client-side workarounds were tested and both fail: `%252F` still yields 0 messages, and a raw `/` misses their API route and falls through to their SPA fallback, returning `index.html` with HTTP 200.
- **Style:** 2-space indent, file-scoped namespaces, nullable enabled, Allman braces with braces on every `if` (match surrounding code), explicit type annotations preferred. **Warnings are errors in Release.**
- **Line endings:** the `.razor` and `.cs` files in this diff are **CRLF** on disk despite `.editorconfig` saying `lf`. Edit in place; do not reflow or rewrite whole files, or the PR becomes an unreviewable whole-file diff.
- **Build:** `dotnet build --configuration Release`. **Test:** `dotnet test --configuration Release`.
- **Scope fence:** the only production files this PR may touch are the four listed in the File Map. If a fix seems to need a fifth, stop and say so.

---

## Non-goals (carried in from the queue row — do not quietly widen)

1. **No client-side workaround for the group-thread `%2F` bug.** See Global Constraints. A group thread returns a real **HTTP 200 with `messages: []`**, so from our side it is a *legitimate empty* and **must keep rendering the empty state, not the error state**. That is not a bug in this PR; it is the honest rendering of what the server said. Test Plan step C6 asserts it.
2. **Do not merge this with GV-6.** GV-6 is the same bug class (every non-2xx → `null`) in the two *mark-read* methods. They are deliberately separate rows: different methods, different dependencies (GV-4 vs GV-3), different severity and audience. **They share the idiom, not the PR.** This plan introduces `GvResult<T>` in a reusable shape and Task 1 documents exactly how GV-6 adopts it. _Planner considered merging while writing this plan and rejected it: the shared shape is ~60 lines and the two remainders are not trivial — GV-6 additionally needs a once-only Warning and a latch that suppresses further calls until restart, which is behaviour, not plumbing. Merging would also give this HIGH user-facing fix a needless GV-4 dependency._
3. **Do not redesign the pane.** GV-7 (non-dialable senders) is design-led and will touch these same branches. Keep this row to **state correctness**.
4. **F-2 is closed here only in its state half.** This PR guarantees `Start the conversation below.` renders **only for a genuine empty**. The *other* half of F-2 — that the copy invites a reply into a hard-disabled composer even when the thread really is empty — is **wording**, it needs new copy that does not exist in any handoff, and the UAT routed it to GV-7 alongside F-3 (composer labelling). Do not invent that string here.

---

## Scope additions beyond the literal queue row (flagged, not smuggled)

Three things this plan adds that the row does not spell out. Each is small, each follows directly from "make the failure honest," and each is **isolated so it can be dropped without unpicking the rest**. Flagging them rather than folding them in silently.

| # | Addition | Where | Why | If you disagree |
|---|---|---|---|---|
| 1 | Clear `_openThreadMessages` at the **start** of a thread open | Task 5 | Today the previous thread's bubbles stay on screen until the new fetch returns — and with `Loading` now wired, the skeleton branch (`Messages == null && Loading`) is **unreachable** unless we null it. Required for the row's own deliverable. | Not droppable — the skeleton the row asks for does not work without it. |
| 2 | Stale-response guard (`if (_openThreadId != threadId) return;`) | Task 5 | The UAT clicked threads in rapid succession. Without it a slow fetch for thread A can write A's messages, error flag and loading flag into B's open pane. Two lines. | Droppable; delete the two guard lines and their comments. |
| 3 | Don't mark-read a conversation that failed to load; restore the optimistic unread flip | **Task 6 (whole task)** | Same lie one level up: the unread dot disappears for a conversation the user was never shown. Also avoids spending 2–3 more upstream Google calls (F-1-DIAGNOSIS § Scope-affecting notes) inside a dead auth window. | Droppable in full — skip Task 6; Tasks 1–5 and 7 stand unchanged. |

---

## File Map

### New files

| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Services/ApiClients/GvResult.cs` | The reusable outcome type: `GvCallOutcome` enum + `GvResult<T>` (value, HTTP status, RotaryPhone error discriminator, `IsSuccess`). One responsibility: let a `GvBridgeApiService` caller tell *how* a call ended. No Blazor dependency. |
| `tests/Radio.Web.Tests/Services/GvResultTests.cs` | Unit tests for the factories and the `IsSuccess` / `IsFailure` semantics GV-6 will lean on. |

### Modified files

| File | Changes |
|------|---------|
| `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | `GetSmsThreadMessagesAsync` returns `GvResult<SmsThreadMessagesDto>`; `GetFromJsonAsync` (which throws on non-2xx via `EnsureSuccessStatusCode`) replaced by `GetAsync` + explicit status handling; add the private `ReadErrorCodeAsync` helper. **The log message keeps the `Failed to get GV SMS thread` substring.** |
| `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` | Add the missing **error branch** to the conversation `msg-list` (lines 36–68), ordered *before* the empty branch; clarify the `Loading` / `Error` / `OnRetry` parameter comment as mode-scoped. |
| `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` | Add `OpenThreadLoading` / `OpenThreadError` / `OnRetryOpenThread` parameters; forward them to `PhoneTextsPanel` as `Loading` / `Error` / `OnRetry` (lines 184–191, which today pass neither). |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Add `_openThreadLoading` / `_openThreadError`; extract `LoadOpenThreadMessagesAsync`; delete the `?? new()` coalesce; add `RetryOpenThreadAsync`; skip the durable mark-read when the load failed (Task 6); wire the three new panel parameters. |
| `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs` | Update the happy-path assertion for the new return type; add a **non-2xx** case (the explicit test gap in the row), an error-discriminator case, and a malformed-body case. |
| `tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs` | Add four bUnit cases: error-state-not-empty-state (the F-1 regression gate), genuine-empty, skeleton-while-loading, and Retry invokes the callback. |
| `design/INTEGRATIONS.md` | New gotcha under § Google Voice (gvbridge) Messages: outcome-aware reads, and why a group thread's `200` + `messages: []` is an empty, not a failure. |
| `design/FUTURE-WORK.md` | §12 Code Pointers: add `GvResult.cs`; annotate the `GvBridgeApiService.cs` pointer. |

---

## Chunk 1: The reusable outcome type

### Task 1: Add `GvCallOutcome` + `GvResult<T>`

**Files:**
- Create: `src/Radio.Web/Services/ApiClients/GvResult.cs`
- Test: `tests/Radio.Web.Tests/Services/GvResultTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Radio.Web.Services.ApiClients.GvCallOutcome` (enum: `Success`, `HttpError`, `Timeout`, `Transport`, `Malformed`) and `Radio.Web.Services.ApiClients.GvResult<T> where T : class`, with instance members `Outcome` (`GvCallOutcome`), `Value` (`T?`), `StatusCode` (`HttpStatusCode?`), `ErrorCode` (`string?`), `IsSuccess` (`bool`), `IsFailure` (`bool`); and static factories `Success(T value)`, `HttpError(HttpStatusCode statusCode, string? errorCode = null)`, `Timeout()`, `Transport()`, `Malformed()`. Task 2 returns it; Task 5 consumes `IsSuccess` and `Value`.

> **This is the "reusable shape" the queue row asks for.** GV-6 adopts it by changing `MarkVoicemailReadAsync` / `MarkSmsThreadReadAsync` to return `GvResult<VoicemailItemDto>` / `GvResult<SmsThreadDto>` and branching on `result.Outcome == GvCallOutcome.HttpError && result.StatusCode == HttpStatusCode.Conflict && result.ErrorCode == "markread_disabled"` — which is exactly why `ErrorCode` exists here. It is not speculative: GV-8 consumes it too, in the log line that the documented `journalctl` probe greps for.

- [ ] **Step 1: Write the failing test**

Create `tests/Radio.Web.Tests/Services/GvResultTests.cs`:

```csharp
using System.Net;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

/// <summary>
/// GV-8 (UAT F-1). GvBridgeApiService used to collapse every non-2xx, timeout and
/// deserialization error to a bare null, so a caller could not tell "the load failed"
/// from "there is nothing there" — and PhonePage rendered the failure as an empty
/// conversation. These tests pin the discrimination GV-6 will also depend on.
/// </summary>
public class GvResultTests
{
  private static readonly SmsThreadMessagesDto Dto =
    new("t1", Array.Empty<SmsMessageDto>(), DateTime.UtcNow);

  [Fact]
  public void Success_CarriesTheValue_AndReportsSuccess()
  {
    var result = GvResult<SmsThreadMessagesDto>.Success(Dto);

    Assert.True(result.IsSuccess);
    Assert.False(result.IsFailure);
    Assert.Equal(GvCallOutcome.Success, result.Outcome);
    Assert.Same(Dto, result.Value);
    Assert.Null(result.StatusCode);
    Assert.Null(result.ErrorCode);
  }

  [Fact]
  public void HttpError_CarriesStatusAndErrorCode_AndReportsFailure()
  {
    var result = GvResult<SmsThreadMessagesDto>.HttpError(
      HttpStatusCode.Conflict, "markread_disabled");

    Assert.False(result.IsSuccess);
    Assert.True(result.IsFailure);
    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    Assert.Equal("markread_disabled", result.ErrorCode);
    Assert.Null(result.Value);
  }

  [Fact]
  public void HttpError_AllowsAnAbsentErrorCode()
  {
    var result = GvResult<SmsThreadMessagesDto>.HttpError(HttpStatusCode.BadGateway);

    Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    Assert.Null(result.ErrorCode);
  }

  [Fact]
  public void Timeout_Transport_And_Malformed_AllReportFailure_WithNoValueOrStatus()
  {
    var timeout = GvResult<SmsThreadMessagesDto>.Timeout();
    var transport = GvResult<SmsThreadMessagesDto>.Transport();
    var malformed = GvResult<SmsThreadMessagesDto>.Malformed();

    Assert.Equal(GvCallOutcome.Timeout, timeout.Outcome);
    Assert.Equal(GvCallOutcome.Transport, transport.Outcome);
    Assert.Equal(GvCallOutcome.Malformed, malformed.Outcome);

    foreach (var result in new[] { timeout, transport, malformed })
    {
      Assert.False(result.IsSuccess);
      Assert.True(result.IsFailure);
      Assert.Null(result.Value);
      Assert.Null(result.StatusCode);
    }
  }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvResultTests"`
Expected: **compile error** — `The type or namespace name 'GvResult<>' could not be found` / `'GvCallOutcome' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/Radio.Web/Services/ApiClients/GvResult.cs`:

```csharp
using System.Net;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// How a GV Bridge call ended. Introduced by GV-8 (UAT F-1): the read methods used to
/// map EVERY non-2xx, timeout and deserialization error onto a bare <c>null</c>, so a
/// caller could not distinguish "the load failed" from "the server returned nothing" —
/// and <c>PhonePage</c> rendered the failure as an empty conversation.
/// </summary>
public enum GvCallOutcome
{
  /// <summary>2xx and the body deserialized.</summary>
  Success,

  /// <summary>A response arrived carrying a non-2xx status (e.g. RotaryPhone's 502
  /// during a GV auth blackout, or a 409 dark-feature rejection).</summary>
  HttpError,

  /// <summary>The request was abandoned on the HttpClient timeout. NOT caller
  /// cancellation — a token the caller cancelled rethrows.</summary>
  Timeout,

  /// <summary>No usable response: DNS failure, connection refused, connection reset.</summary>
  Transport,

  /// <summary>2xx, but the body did not deserialize into the expected DTO — including a
  /// non-JSON body such as an SPA fallback's index.html served with HTTP 200.</summary>
  Malformed
}

/// <summary>
/// Outcome of a GV Bridge call: the value on success, and enough shape on failure for a
/// caller to decide what the user should see and for an operator to read the log.
/// <para>
/// REUSABLE BY DESIGN. GV-6 (distinguish <c>409 markread_disabled</c> from a genuine
/// mark-read failure) adopts this same type for the two mark-read methods rather than
/// inventing a second mechanism — branch on
/// <c>Outcome == GvCallOutcome.HttpError &amp;&amp; StatusCode == HttpStatusCode.Conflict
/// &amp;&amp; ErrorCode == "markread_disabled"</c>. See
/// <c>docs/queue/ORDERING-NOTES.md</c> for why the two rows
/// share the idiom but not the PR.
/// </para>
/// </summary>
public sealed class GvResult<T> where T : class
{
  private GvResult(GvCallOutcome outcome, T? value, HttpStatusCode? statusCode, string? errorCode)
  {
    Outcome = outcome;
    Value = value;
    StatusCode = statusCode;
    ErrorCode = errorCode;
  }

  /// <summary>How the call ended.</summary>
  public GvCallOutcome Outcome { get; }

  /// <summary>The deserialized payload. Non-null if and only if <see cref="IsSuccess"/>.</summary>
  public T? Value { get; }

  /// <summary>The HTTP status, when a response actually arrived. Null otherwise.</summary>
  public HttpStatusCode? StatusCode { get; }

  /// <summary>RotaryPhone's error discriminator from the failure body
  /// (<c>{"error":"..."}</c> / <c>{"code":"..."}</c>), when present.</summary>
  public string? ErrorCode { get; }

  public bool IsSuccess => Outcome == GvCallOutcome.Success;

  public bool IsFailure => Outcome != GvCallOutcome.Success;

  public static GvResult<T> Success(T value) =>
    new(GvCallOutcome.Success, value, null, null);

  public static GvResult<T> HttpError(HttpStatusCode statusCode, string? errorCode = null) =>
    new(GvCallOutcome.HttpError, null, statusCode, errorCode);

  public static GvResult<T> Timeout() =>
    new(GvCallOutcome.Timeout, null, null, null);

  public static GvResult<T> Transport() =>
    new(GvCallOutcome.Transport, null, null, null);

  public static GvResult<T> Malformed() =>
    new(GvCallOutcome.Malformed, null, null, null);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvResultTests"`
Expected: **4 passed, 0 failed**.

- [ ] **Step 5: Commit**

```bash
git add src/Radio.Web/Services/ApiClients/GvResult.cs tests/Radio.Web.Tests/Services/GvResultTests.cs
git commit -m "feat(gv): add GvResult<T> outcome type for GV Bridge calls (GV-8)"
```

---

## Chunk 2: The API client stops lying

### Task 2: `GetSmsThreadMessagesAsync` returns an outcome, not a bare null

**Files:**
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs:204-218`
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor:631-632` (**behaviour-preserving** call-site fix only — keeps the build green; Task 5 changes the behaviour)
- Test: `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs`

**Interfaces:**
- Consumes: `GvResult<T>` / `GvCallOutcome` from Task 1.
- Produces: `Task<GvResult<SmsThreadMessagesDto>> GetSmsThreadMessagesAsync(string threadId, int count = 50, CancellationToken ct = default)` — the signature Task 5 calls.

> Two things this task must NOT do. **(a)** Do not touch `Uri.EscapeDataString(threadId)` — Defect B is RotaryPhone's and both client-side workarounds were tested and fail. **(b)** Do not change the other read methods. `GetVoicemailsAsync`, `GetVoicemailAsync`, `GetSmsThreadsAsync` keep returning `T?` in this PR: the thread list already handles `null` correctly with keep-last-good-plus-toast (`PhonePage.razor:595-616`), and converting it here would be a behaviour-neutral churn PR riding on a HIGH bug fix.
>
> Why the rewrite is structural rather than a wider `catch`: `GetFromJsonAsync` internally calls `EnsureSuccessStatusCode()`, so a 502 arrives as an `HttpRequestException` **after** the status has already been discarded. The status can only be recovered by doing the `GetAsync` and inspecting the response ourselves.

- [ ] **Step 1: Write the failing tests**

In `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs`, **replace** the existing happy-path test:

```csharp
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsMessages()
  {
    var dto = new SmsThreadMessagesDto("t1",
      new[] { new SmsMessageDto("m1", "t1", "Inbound", "+15551234567",
        "hello", DateTime.UtcNow, false) },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.NotNull(result);
    Assert.Single(result!.Messages);
  }
```

with this test plus three new ones:

```csharp
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsSuccess_WithMessages()
  {
    var dto = new SmsThreadMessagesDto("t1",
      new[] { new SmsMessageDto("m1", "t1", "Inbound", "+15551234567",
        "hello", DateTime.UtcNow, false) },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.True(result.IsSuccess);
    Assert.Equal(GvCallOutcome.Success, result.Outcome);
    Assert.NotNull(result.Value);
    Assert.Single(result.Value!.Messages);
  }

  // THE test gap the GV-8 queue row calls out. This is the exact live failure: during
  // RotaryPhone's ~9-minute GV auth blackout the bridge returns 502, which used to
  // collapse to null and render as an empty conversation (UAT F-1).
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsHttpError_OnNon2xx()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.False(result.IsSuccess);
    Assert.True(result.IsFailure);
    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    Assert.Null(result.Value);
  }

  // The error discriminator is what GV-6 will branch on for 409 markread_disabled;
  // here it is captured so the operator-facing log line names the upstream failure.
  [Fact]
  public async Task GetSmsThreadMessagesAsync_CapturesErrorCode_FromFailureBody()
  {
    var handler = new MockHttpHandler("{\"error\":\"upstream_error\"}",
      HttpStatusCode.BadGateway);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal("upstream_error", result.ErrorCode);
  }

  // A 200 whose body is not the DTO (RotaryPhone's SPA fallback serves index.html with
  // HTTP 200 — see F-1-DIAGNOSIS § Defect B) must be a failure, never an empty thread.
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsMalformed_OnUndeserializableBody()
  {
    var handler = new MockHttpHandler("<html><body>not json</body></html>");
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.False(result.IsSuccess);
    Assert.Equal(GvCallOutcome.Malformed, result.Outcome);
    Assert.Null(result.Value);
  }
```

`System.Net` and `Radio.Web.Services.ApiClients` are already imported at the top of this file; no new `using` is needed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"`
Expected: **compile errors** — `'SmsThreadMessagesDto' does not contain a definition for 'IsSuccess'` (and `Outcome`, `Value`, `StatusCode`, `ErrorCode`).

- [ ] **Step 3: Rewrite the method**

In `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`, replace lines 204–218 — the whole block currently reading:

```csharp
  public async Task<SmsThreadMessagesDto?> GetSmsThreadMessagesAsync(
    string threadId, int count = 50, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SmsThreadMessagesDto>(
        $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}?count={count}",
        JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}", threadId);
      return null;
    }
  }
```

with:

```csharp
  /// <summary>
  /// Fetch one conversation's message bodies. Returns an OUTCOME, never a bare null
  /// (GV-8 / UAT F-1): a 502, a timeout, a transport failure and an undeserializable
  /// body are each distinct from "the server returned zero messages," and the caller
  /// MUST NOT render any of them as an empty conversation.
  /// <para>
  /// A group thread whose id contains '/' currently returns a genuine HTTP 200 with an
  /// empty list — that is RotaryPhone's Defect B (thread ids arrive with a literal
  /// %2F and fail their exact string compare), and from our side it IS a successful
  /// empty result. Do NOT special-case it and do NOT change the escaping below:
  /// double-escaping (%252F) still yields 0 messages, and a raw '/' misses their API
  /// route entirely and falls through to their SPA fallback, returning index.html with
  /// HTTP 200. Both were tested. See docs/uat/2026-07-31-gv-live-data/F-1-DIAGNOSIS.md.
  /// </para>
  /// </summary>
  public async Task<GvResult<SmsThreadMessagesDto>> GetSmsThreadMessagesAsync(
    string threadId, int count = 50, CancellationToken ct = default)
  {
    var url = $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}?count={count}";
    try
    {
      // GetAsync, not GetFromJsonAsync: the latter calls EnsureSuccessStatusCode()
      // internally, so the status is already thrown away by the time we see the
      // exception — which is precisely how every non-2xx became an indistinguishable null.
      using var response = await _httpClient.GetAsync(url, ct);

      if (!response.IsSuccessStatusCode)
      {
        var errorCode = await ReadErrorCodeAsync(response, ct);
        // KEEP the literal "Failed to get GV SMS thread" substring: it is the documented
        // server-side probe for this surface (journalctl -u radio-web | grep ...). Blazor
        // Server fetches server-side over SignalR, so this log is the ONLY place the
        // failure is observable — it never reaches browser instrumentation.
        _logger.LogError(
          "Failed to get GV SMS thread {ThreadId}: HTTP {Status} {ErrorCode}",
          threadId, (int)response.StatusCode, errorCode ?? "-");
        return GvResult<SmsThreadMessagesDto>.HttpError(response.StatusCode, errorCode);
      }

      var dto = await response.Content
        .ReadFromJsonAsync<SmsThreadMessagesDto>(JsonOptions, ct);
      if (dto == null)
      {
        _logger.LogError(
          "Failed to get GV SMS thread {ThreadId}: 2xx with an empty body", threadId);
        return GvResult<SmsThreadMessagesDto>.Malformed();
      }
      return GvResult<SmsThreadMessagesDto>.Success(dto);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;                    // the CALLER cancelled — not a failure to report as one
    }
    catch (OperationCanceledException ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: timed out", threadId);
      return GvResult<SmsThreadMessagesDto>.Timeout();
    }
    catch (JsonException ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: malformed body", threadId);
      return GvResult<SmsThreadMessagesDto>.Malformed();
    }
    catch (NotSupportedException ex)
    {
      // 2xx with a non-JSON content type (e.g. an SPA fallback's index.html).
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: unsupported content type", threadId);
      return GvResult<SmsThreadMessagesDto>.Malformed();
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "Failed to get GV SMS thread {ThreadId}: transport failure", threadId);
      return GvResult<SmsThreadMessagesDto>.Transport();
    }
  }

  /// <summary>
  /// Best-effort read of RotaryPhone's error discriminator from a failure body
  /// (<c>{"error":"upstream_error"}</c> / <c>{"code":"send_disabled"}</c>). Returns null
  /// when the body is empty, is not a JSON object, or carries neither property. NEVER
  /// throws — this is a diagnostic, and a failed parse must not turn one failure into a
  /// different one. Property lookup is case-sensitive, so both casings are tried.
  /// GV-6 reuses this for <c>409 markread_disabled</c>.
  /// </summary>
  private static async Task<string?> ReadErrorCodeAsync(
    HttpResponseMessage response, CancellationToken ct)
  {
    try
    {
      var body = await response.Content.ReadAsStringAsync(ct);
      if (string.IsNullOrWhiteSpace(body))
      {
        return null;
      }
      using var doc = JsonDocument.Parse(body);
      if (doc.RootElement.ValueKind != JsonValueKind.Object)
      {
        return null;
      }
      foreach (var name in new[] { "error", "Error", "code", "Code" })
      {
        if (doc.RootElement.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
          return element.GetString();
        }
      }
      return null;
    }
    catch
    {
      return null;              // non-JSON error body (e.g. an HTML fallback page)
    }
  }
```

- [ ] **Step 4: Fix the single call site so the build stays green (no behaviour change yet)**

In `src/Radio.Web/Components/Pages/PhonePage.razor`, replace lines 631–632:

```csharp
    var messages = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);
    _openThreadMessages = messages?.Messages.ToList() ?? new();
```

with the exact behavioural equivalent under the new signature — **Task 5 is what actually changes this**:

```csharp
    // TASK 5 REPLACES THIS. Behaviour-preserving adaptation to the new return type so
    // this task compiles on its own; the "failure renders as empty" bug is still here.
    var messages = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);
    _openThreadMessages = messages.IsSuccess
      ? messages.Value!.Messages.ToList()
      : new();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"`
Expected: **all pass**, including the four `GetSmsThreadMessagesAsync_*` cases.

- [ ] **Step 6: Verify the whole solution still builds under warnings-as-errors**

Run: `dotnet build --configuration Release`
Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**.

- [ ] **Step 7: Commit**

```bash
git add src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs \
        src/Radio.Web/Components/Pages/PhonePage.razor \
        tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs
git commit -m "fix(gv): GetSmsThreadMessagesAsync returns an outcome, not a bare null (GV-8)"
```

---

## Chunk 3: The pane learns to say "failed"

### Task 3: Add the missing error branch to the conversation pane

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor:36-68` (the conversation `msg-list`) and `:170-177` (the parameter comment)
- Test: `tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs`

**Interfaces:**
- Consumes: the **existing** `[Parameter] public bool Loading`, `[Parameter] public bool Error`, `[Parameter] public EventCallback OnRetry` — declared at `PhoneTextsPanel.razor:172-173` and `:176`. **No new parameter is added.** Task 4 is what finally passes them in this hosting.
- Produces: a conversation `msg-list` with **four** ordered branches — skeleton, error, empty, list.

> **Branch order is the fix.** The error branch must sit **after** the skeleton and **before** the empty branch, so `Start the conversation below.` can only be reached when the load genuinely succeeded and returned zero messages (UAT F-2).
>
> **bUnit caveat (project lesson):** bUnit re-parses markup into a fresh AngleSharp document per render, so it **cannot** verify DOM node identity. Every assertion below is content-based on purpose. Anything needing node identity belongs in a real browser — see the Test Plan.

- [ ] **Step 1: Write the failing tests**

In `tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs`, add `using Microsoft.AspNetCore.Components;` to the usings block, then append these four tests inside the class, before the closing brace:

```csharp
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
    Register(sendEnabled: false, available: true);
    var cut = RenderComponent<PhoneTextsPanel>(p => p
      .Add(x => x.OpenThreadId, "t1")
      .Add(x => x.HeaderName, "Mom")
      .Add(x => x.Messages, (List<SmsMessageDto>?)null)
      .Add(x => x.Loading, true));

    Assert.NotEmpty(cut.FindAll(".skeleton-list-row"));
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhoneTextsPanelTests"`
Expected: `Conversation_ShowsErrorState_NotEmptyState_WhenErrorSet` **FAILS** (`Assert.Contains() Failure` — "Couldn't load messages." is not in the markup), and `Conversation_RetryButton_InvokesOnRetry` **FAILS** (`Sequence contains no matching element` — there is no Retry button). `Conversation_ShowsEmptyState_WhenGenuinelyEmpty` and `Conversation_ShowsSkeleton_WhileLoading` already pass — they pin behaviour that must survive.

- [ ] **Step 3: Add the error branch**

In `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`, replace lines 36–50 — from the `msg-list` opening tag through the end of the empty-state block:

```razor
    <div class="msg-list" aria-live="polite" @ref="_listEl">
      @if (Messages == null && Loading)
      {
        @for (var i = 0; i < 5; i++)
        {
          <div class="skeleton-list-row"><div class="skeleton skeleton-list-row-text"></div></div>
        }
      }
      else if (Messages != null && Messages.Count == 0)
      {
        <div class="empty-state">
          <RadzenIcon Icon="forum" class="empty-state-icon" />
          <div class="empty-state-text">Start the conversation below.</div>
        </div>
      }
```

with:

```razor
    <div class="msg-list" aria-live="polite" @ref="_listEl">
      @* GV-8 / UAT F-1. BRANCH ORDER IS THE FIX: skeleton → error → empty → list. The
         error branch must precede the empty branch so "Start the conversation below."
         is reachable ONLY when the fetch genuinely succeeded and returned zero messages
         (UAT F-2). Same cloud_off + copy + Retry shape the thread list already uses at
         PhoneMessagesPanel.razor:110-117; copy string from
         HANDOFF-phone-dark-theme-and-scrollbars.md:310. *@
      @if (Messages == null && Loading)
      {
        @for (var i = 0; i < 5; i++)
        {
          <div class="skeleton-list-row"><div class="skeleton skeleton-list-row-text"></div></div>
        }
      }
      else if (Error)
      {
        <div class="empty-state">
          <RadzenIcon Icon="cloud_off" class="empty-state-icon" />
          <div class="empty-state-text">Couldn't load messages.</div>
          <button type="button" class="phone-btn-sm" @onclick="OnRetry">Retry</button>
        </div>
      }
      else if (Messages != null && Messages.Count == 0)
      {
        <div class="empty-state">
          <RadzenIcon Icon="forum" class="empty-state-icon" />
          <div class="empty-state-text">Start the conversation below.</div>
        </div>
      }
```

Leave the `else if (Messages != null)` list branch (lines 51–67) and the `@ComposeBar()` call unchanged. Leave the **new-recipient** composer's `Start the conversation below.` at lines 100–105 unchanged — that one is a genuine empty by construction (a brand-new message has no history) and has no fetch to fail.

- [ ] **Step 4: Clarify the parameter comment**

In the same file, replace the comment line at 170 and the three declarations that follow:

```csharp
  // ── Thread-list inputs (hosted in the feed pane) ──────────────
  [Parameter] public List<SmsThreadDto>? Threads { get; set; }
  [Parameter] public bool Loading { get; set; }
  [Parameter] public bool Error { get; set; }
```

with:

```csharp
  // ── Thread-list inputs (hosted in the feed pane) ──────────────
  // GV-8: Loading / Error / OnRetry are MODE-SCOPED, not list-scoped. One instance
  // renders exactly one mode (conversation | new-recipient | thread list), so they
  // describe whichever fetch that mode is showing — the OPEN CONVERSATION's fetch when
  // OpenThreadId is set, the thread list's otherwise. PhoneMessagesPanel hosts the
  // conversation mode only and forwards its OpenThreadLoading / OpenThreadError /
  // OnRetryOpenThread into these three.
  [Parameter] public List<SmsThreadDto>? Threads { get; set; }
  [Parameter] public bool Loading { get; set; }
  [Parameter] public bool Error { get; set; }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhoneTextsPanelTests"`
Expected: **all pass** — the four new cases plus the seven pre-existing ones.

- [ ] **Step 6: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhoneTextsPanel.razor \
        tests/Radio.Web.Tests/Components/PhoneTextsPanelTests.cs
git commit -m "fix(gv): add the missing error branch to the texts conversation pane (GV-8)"
```

---

## Chunk 4: Wiring

### Task 4: Pass `Loading` / `Error` / `OnRetry` through `PhoneMessagesPanel`

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor:184-191` (the `PhoneTextsPanel` host) and the parameter block after `:264`

**Interfaces:**
- Consumes: `PhoneTextsPanel`'s `Loading` / `Error` / `OnRetry` (Task 3).
- Produces: `[Parameter] public bool OpenThreadLoading`, `[Parameter] public bool OpenThreadError`, `[Parameter] public EventCallback OnRetryOpenThread` — the three attributes Task 5 sets from `PhonePage`.

> Naming follows this file's own idiom: it already exposes `VoicemailLoading` / `VoicemailError` / `OnRetryVoicemail` and `ThreadsLoading` / `ThreadsError` / `OnRetryThreads`. `OpenThread*` is the third member of that family. Note the existing `ThreadsLoading`/`ThreadsError` describe the **thread list** and are already wired — do not reuse them here; the list and the open conversation are two independent fetches and one failing must not describe the other.
>
> This task changes nothing visible on its own: the new parameters default to `false` until Task 5 sets them. That is deliberate — it keeps the wiring reviewable in isolation.

- [ ] **Step 1: Add the three parameters**

In `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor`, find the line:

```csharp
  [Parameter] public List<SmsMessageDto>? OpenThreadMessages { get; set; }
```

and insert immediately after it:

```csharp
  // GV-8 / UAT F-1 — the OPEN CONVERSATION's own load state, distinct from the thread
  // list's ThreadsLoading/ThreadsError above. These forward into PhoneTextsPanel's
  // existing Loading/Error/OnRetry parameters, which this host previously passed NEITHER
  // of: that is why a failed load rendered as an empty conversation, and why the
  // skeleton branch was unreachable dead code (no spinner was ever seen).
  [Parameter] public bool OpenThreadLoading { get; set; }
  [Parameter] public bool OpenThreadError { get; set; }
  [Parameter] public EventCallback OnRetryOpenThread { get; set; }
```

- [ ] **Step 2: Forward them to `PhoneTextsPanel`**

In the same file, replace the host element at lines 184–191:

```razor
        <PhoneTextsPanel OpenThreadId="_openThreadId"
                         Threads="Threads"
                         Contacts="Contacts"
                         Messages="OpenThreadMessages"
                         HeaderName="@OpenThreadName"
                         HeaderNumber="@OpenThreadNumber"
                         OnBack="CloseThread"
                         OnOptimisticAppend="OnOptimisticAppend" />
```

with:

```razor
        <PhoneTextsPanel OpenThreadId="_openThreadId"
                         Threads="Threads"
                         Contacts="Contacts"
                         Messages="OpenThreadMessages"
                         Loading="OpenThreadLoading"
                         Error="OpenThreadError"
                         OnRetry="OnRetryOpenThread"
                         HeaderName="@OpenThreadName"
                         HeaderNumber="@OpenThreadNumber"
                         OnBack="CloseThread"
                         OnOptimisticAppend="OnOptimisticAppend" />
```

- [ ] **Step 3: Verify the build**

Run: `dotnet build --configuration Release`
Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**.

- [ ] **Step 4: Verify nothing regressed**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release`
Expected: **all pass** (no behaviour change — the new parameters are still `false`).

- [ ] **Step 5: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor
git commit -m "fix(gv): forward open-conversation loading/error/retry to PhoneTextsPanel (GV-8)"
```

---

### Task 5: `PhonePage` — stop coalescing failure into an empty conversation

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor` — the panel invocation at `:75-84`, the field block at `:176-179`, and `OpenThreadAsync` at `:621-648`

**Interfaces:**
- Consumes: `GvResult<SmsThreadMessagesDto>` (Task 2); `OpenThreadLoading` / `OpenThreadError` / `OnRetryOpenThread` (Task 4).
- Produces: `private async Task<bool> LoadOpenThreadMessagesAsync(string threadId)` (returns `true` iff the fetch succeeded) and `private async Task RetryOpenThreadAsync()`. Task 6 consumes the `bool` return.

> This is the task that actually fixes F-1: `?? new()` goes away. Note the two flagged additions here — clearing `_openThreadMessages` up front (required, or the skeleton branch stays unreachable and the previous thread's bubbles linger) and the stale-response guard (droppable). See § "Scope additions."

- [ ] **Step 1: Add the two state fields**

In `src/Radio.Web/Components/Pages/PhonePage.razor`, find:

```csharp
  private string? _openThreadId;
  private List<SmsMessageDto>? _openThreadMessages;
```

and insert immediately after:

```csharp
  // GV-8 / UAT F-1 — the open conversation's own load state, deliberately SEPARATE from
  // _threadsLoading/_threadsError: the thread list and the open conversation are two
  // independent fetches, and one failing must never be described by the other's flag.
  private bool _openThreadLoading;
  private bool _openThreadError;
```

- [ ] **Step 2: Wire the three new panel attributes**

In the same file, replace these five lines of the `<PhoneMessagesPanel …>` invocation:

```razor
                          Threads="_threads"
                          ThreadsLoading="_threadsLoading"
                          ThreadsError="_threadsError"
                          OpenThreadMessages="_openThreadMessages"
                          OnRefresh="RefreshMessagesAsync"
```

with:

```razor
                          Threads="_threads"
                          ThreadsLoading="_threadsLoading"
                          ThreadsError="_threadsError"
                          OpenThreadMessages="_openThreadMessages"
                          OpenThreadLoading="_openThreadLoading"
                          OpenThreadError="_openThreadError"
                          OnRetryOpenThread="RetryOpenThreadAsync"
                          OnRefresh="RefreshMessagesAsync"
```

- [ ] **Step 3: Extract the fetch and delete the `?? new()`**

In the same file, replace the whole `OpenThreadAsync` method — the block currently reading:

```csharp
  private async Task OpenThreadAsync(string threadId)
  {
    _openThreadId = threadId;

    // 1) Optimistic flip (presentation-only).
    if (_threads != null)
    {
      ReadStateReconciler.ApplyThread(_threads, threadId, isRead: true);
    }

    // TASK 5 REPLACES THIS. Behaviour-preserving adaptation to the new return type so
    // this task compiles on its own; the "failure renders as empty" bug is still here.
    var messages = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);
    _openThreadMessages = messages.IsSuccess
      ? messages.Value!.Messages.ToList()
      : new();
    PhoneUnread.Set(UnreadSum);
    await InvokeAsync(StateHasChanged);

    // 2) Durable write-through (idempotent reconcile; 502/404 keep the optimistic flip).
    var dto = await GvBridgeApi.MarkSmsThreadReadAsync(threadId, isRead: true);
    if (dto != null && _threads != null)
    {
      var changed = ReadStateReconciler.ApplyThread(_threads, dto.ThreadId, isRead: !dto.HasUnread);
      if (changed)
      {
        PhoneUnread.Set(UnreadSum);
        await InvokeAsync(StateHasChanged);
      }
    }
  }
```

with:

```csharp
  private async Task OpenThreadAsync(string threadId)
  {
    _openThreadId = threadId;

    // 1) Optimistic flip (presentation-only).
    if (_threads != null)
    {
      ReadStateReconciler.ApplyThread(_threads, threadId, isRead: true);
    }

    await LoadOpenThreadMessagesAsync(threadId);

    // 2) Durable write-through (idempotent reconcile; 502/404 keep the optimistic flip).
    var dto = await GvBridgeApi.MarkSmsThreadReadAsync(threadId, isRead: true);
    if (dto != null && _threads != null)
    {
      var changed = ReadStateReconciler.ApplyThread(_threads, dto.ThreadId, isRead: !dto.HasUnread);
      if (changed)
      {
        PhoneUnread.Set(UnreadSum);
        await InvokeAsync(StateHasChanged);
      }
    }
  }

  // Fetch the open conversation's bodies (GV-8 / UAT F-1). Returns TRUE iff the fetch
  // succeeded. The old body did `messages?.Messages.ToList() ?? new()`, which made a 502
  // byte-identical to a genuinely empty conversation and rendered it as "Start the
  // conversation below." A failure now sets _openThreadError and leaves the message list
  // NULL, so the pane's error branch owns the state instead of the empty branch.
  private async Task<bool> LoadOpenThreadMessagesAsync(string threadId)
  {
    // Drop the previous thread's bubbles BEFORE awaiting: they are not this thread's
    // history, and the pane's skeleton branch requires Messages == null to be reachable.
    _openThreadMessages = null;
    _openThreadError = false;
    _openThreadLoading = true;
    await InvokeAsync(StateHasChanged);

    var result = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);

    // The user may have opened a different thread while this fetch was in flight; the
    // newer open owns the pane state, so drop this stale result rather than writing one
    // thread's messages (or error) into another thread's pane.
    if (_openThreadId != threadId)
    {
      return false;
    }

    if (result.IsSuccess)
    {
      _openThreadMessages = result.Value!.Messages.ToList();
    }
    else
    {
      _openThreadError = true;    // leave _openThreadMessages null — never `?? new()`
    }
    _openThreadLoading = false;
    PhoneUnread.Set(UnreadSum);
    await InvokeAsync(StateHasChanged);
    return result.IsSuccess;
  }

  // Retry from the conversation pane's error state. Re-fetches the bodies ONLY — it does
  // not repeat the optimistic flip or the mark-read write-through, because each thread
  // open already costs 2-3 upstream Google calls (F-1-DIAGNOSIS § Scope-affecting notes)
  // and a retry usually happens while the upstream is still unhealthy.
  private async Task RetryOpenThreadAsync()
  {
    var threadId = _openThreadId;
    if (threadId == null)
    {
      return;
    }
    await LoadOpenThreadMessagesAsync(threadId);
  }
```

- [ ] **Step 4: Verify the build**

Run: `dotnet build --configuration Release`
Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**.

- [ ] **Step 5: Verify the suite**

Run: `dotnet test tests/Radio.Web.Tests --configuration Release`
Expected: **all pass**, including `PhonePageTests` (14 pre-existing cases).

- [ ] **Step 6: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "fix(gv): a failed conversation load no longer renders as an empty thread (GV-8)"
```

---

### Task 6: Don't mark read what we couldn't show (flagged addition — droppable)

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor` — `OpenThreadAsync` only

**Interfaces:**
- Consumes: the `bool` returned by `LoadOpenThreadMessagesAsync` (Task 5); `ReadStateReconciler.ApplyThread(List<SmsThreadDto>, string?, bool)` (existing, returns `true` iff the list changed).
- Produces: nothing new.

> **This whole task is droppable** — see § "Scope additions." If you skip it, Tasks 1–5 and 7 stand unchanged and F-1/F-2 are still closed.
>
> Why it belongs with this row: the queue row's thesis is that the UI must not lie about what happened. Silently clearing a thread's unread dot for a conversation the user was never shown is the same lie one level up. Secondary benefit: `MarkThreadRead` re-lists twice on RotaryPhone's side, so a failed open currently spends 2–3 more upstream Google calls inside what is usually a dead auth window.
>
> Note the asymmetry that stays: a *successful* load followed by a *failed* mark-read still keeps the optimistic flip and reconciles later — that is ADR-024 §2 and is deliberately unchanged.

- [ ] **Step 1: Capture the prior unread state and gate the write-through**

In `src/Radio.Web/Components/Pages/PhonePage.razor`, replace the `OpenThreadAsync` method Task 5 produced:

```csharp
  private async Task OpenThreadAsync(string threadId)
  {
    _openThreadId = threadId;

    // 1) Optimistic flip (presentation-only).
    if (_threads != null)
    {
      ReadStateReconciler.ApplyThread(_threads, threadId, isRead: true);
    }

    await LoadOpenThreadMessagesAsync(threadId);

    // 2) Durable write-through (idempotent reconcile; 502/404 keep the optimistic flip).
    var dto = await GvBridgeApi.MarkSmsThreadReadAsync(threadId, isRead: true);
    if (dto != null && _threads != null)
    {
      var changed = ReadStateReconciler.ApplyThread(_threads, dto.ThreadId, isRead: !dto.HasUnread);
      if (changed)
      {
        PhoneUnread.Set(UnreadSum);
        await InvokeAsync(StateHasChanged);
      }
    }
  }
```

with:

```csharp
  private async Task OpenThreadAsync(string threadId)
  {
    _openThreadId = threadId;

    // 1) Optimistic flip (presentation-only). Remember what it was: if the bodies fail
    // to load we put the unread marker back (GV-8) rather than clearing the dot for a
    // conversation the user was never actually shown.
    var wasUnread = _threads?.FirstOrDefault(t => t.ThreadId == threadId)?.HasUnread ?? false;
    if (_threads != null)
    {
      ReadStateReconciler.ApplyThread(_threads, threadId, isRead: true);
    }

    var loaded = await LoadOpenThreadMessagesAsync(threadId);

    // Superseded by a newer open while we were fetching — that call owns the state now.
    if (_openThreadId != threadId)
    {
      return;
    }

    if (!loaded)
    {
      // The conversation never rendered. Do NOT tell Google it was read, and do not
      // spend the 2-3 further upstream calls MarkThreadRead costs inside what is almost
      // always a dead auth window. Restore the unread marker we optimistically cleared.
      if (wasUnread && _threads != null
          && ReadStateReconciler.ApplyThread(_threads, threadId, isRead: false))
      {
        PhoneUnread.Set(UnreadSum);
        await InvokeAsync(StateHasChanged);
      }
      return;
    }

    // 2) Durable write-through (idempotent reconcile; 502/404 keep the optimistic flip).
    var dto = await GvBridgeApi.MarkSmsThreadReadAsync(threadId, isRead: true);
    if (dto != null && _threads != null)
    {
      var changed = ReadStateReconciler.ApplyThread(_threads, dto.ThreadId, isRead: !dto.HasUnread);
      if (changed)
      {
        PhoneUnread.Set(UnreadSum);
        await InvokeAsync(StateHasChanged);
      }
    }
  }
```

- [ ] **Step 2: Verify the build and the suite**

Run: `dotnet build --configuration Release && dotnet test tests/Radio.Web.Tests --configuration Release`
Expected: **Build succeeded, 0 Warning(s)**; **all tests pass**.

- [ ] **Step 3: Commit**

```bash
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "fix(gv): keep the unread marker when a conversation fails to load (GV-8)"
```

---

## Chunk 5: Documentation

### Task 7: Record the contract in INTEGRATIONS + FUTURE-WORK

**Files:**
- Modify: `design/INTEGRATIONS.md` § "Google Voice (gvbridge) Messages" → **Gotchas** list (around line 366)
- Modify: `design/FUTURE-WORK.md` § "12. GV (Google Voice) Messages …" → **Code Pointers** list

- [ ] **Step 1: Add the INTEGRATIONS gotcha**

In `design/INTEGRATIONS.md`, find the first gotcha bullet:

```markdown
- **Voicemail audio URL must be absolute.** The DTO's relative `AudioUrl` resolves against the Web origin (`:5002`) and 404s — always rebuild it against the API base via `GvBridgeApiService.GetVoicemailAudioUrl(id)` (→ `http://radio:5004/...`). Never bind the relative `AudioUrl` (ADR-022 D4).
```

and insert this bullet immediately **after** it:

```markdown
- **A failed conversation load is NOT an empty conversation (GV-8 / UAT F-1).** `GetSmsThreadMessagesAsync` returns `GvResult<SmsThreadMessagesDto>` — `Success` / `HttpError` (with `StatusCode` + RotaryPhone's `error`/`code` discriminator) / `Timeout` / `Transport` / `Malformed` — precisely because it used to map all of them to `null`, which `PhonePage` then coalesced with `?? new()` into an empty list. A 502 and a genuinely empty thread became byte-identical, and the pane rendered **"Start the conversation below."** with no error, no spinner and no retry. The conversation pane now branches **skeleton → error → empty → list** in that order, so the empty copy is reachable only for a real zero-message result. Two consequences worth holding: **(a)** a group thread (id containing `/`) returns a genuine **HTTP 200 with `messages: []`** because RotaryPhone never decodes the `%2F` — from our side that IS an empty, and rendering it as one is correct, not a bug (their fix is tracked as a cross-repo item); **(b)** the failure is **invisible to browser instrumentation** — Blazor Server fetches server-side over SignalR, so the UAT saw 0 console errors and 0 failed requests. The only probe is `journalctl -u radio-web --since '-30min' | grep 'Failed to get GV SMS thread'` — keep it bounded with `--since`, do not tail; the box is an Intel N100 and heavy journald reads compete with the audio pipeline. **`GvResult<T>` is the reusable shape** — GV-6 adopts it for the mark-read methods rather than inventing a second mechanism.
```

- [ ] **Step 2: Update the FUTURE-WORK code pointers**

In `design/FUTURE-WORK.md` § 12 "Code Pointers", replace this line:

```markdown
- `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` — read methods + audio-URL builder + **PR4 durable mark-read** (`MarkVoicemailReadAsync` / `MarkSmsThreadReadAsync` → the two `POST .../read` routes; 200→DTO, 404→null, 502→null-keep-optimistic, no retry; flag-gated).
```

with:

```markdown
- `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` — read methods + audio-URL builder + **PR4 durable mark-read** (`MarkVoicemailReadAsync` / `MarkSmsThreadReadAsync` → the two `POST .../read` routes; 200→DTO, 404→null, 502→null-keep-optimistic, no retry; flag-gated). **GV-8:** `GetSmsThreadMessagesAsync` is the one read method that returns `GvResult<T>` rather than `T?`; the others still return `T?` because their callers already handle `null` correctly (the thread list keeps its last good list and toasts).
- `src/Radio.Web/Services/ApiClients/GvResult.cs` — **GV-8** outcome type (`Success` / `HttpError` / `Timeout` / `Transport` / `Malformed`, plus `StatusCode` and RotaryPhone's `error`/`code` discriminator). Exists because collapsing every failure to `null` let a 502 render as an empty conversation (UAT F-1). **GV-6 adopts this same type** for the two mark-read methods — the two rows share the idiom, not the PR (see [`ORDERING-NOTES.md`](../../queue/ORDERING-NOTES.md) § Dependency / ordering notes).
```

- [ ] **Step 3: Final full verification**

Run:

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**; the full suite green (~1,416 tests across 10 projects; `Radio.Web.Tests` was 846 passed / 0 failed as of PR #441, plus the 8 added here).

- [ ] **Step 4: Commit**

```bash
git add design/INTEGRATIONS.md design/FUTURE-WORK.md
git commit -m "docs(gv): record the outcome-aware read contract and the F-1 probe (GV-8)"
```

---

## Test Plan

### A. Automated gates (REQUIRED before the PR)

| Gate | Command | Expected |
|---|---|---|
| Build | `dotnet build --configuration Release` | Build succeeded, **0 Warning(s)**, 0 Error(s) — warnings are errors in Release |
| Full suite | `dotnet test --configuration Release` | all green |
| The new unit tests | `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvResultTests"` | 4 passed |
| The client outcome tests | `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"` | all passed, incl. the 4 `GetSmsThreadMessagesAsync_*` cases |
| The pane tests | `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhoneTextsPanelTests"` | 11 passed (7 pre-existing + 4 new) |

**bUnit limitation to respect:** bUnit re-parses markup into a fresh AngleSharp document on every render, so it cannot assert DOM **node identity**. Do not add a test that depends on a node surviving a re-render — that check belongs in section C, in a real browser.

### B. Timing discipline — read this before touching the live box

RotaryPhone's Defect A causes a **deterministic ~9-minute GV auth blackout every 20 minutes** (their PSIDTS goes stale at ~11 min; their CDP refresh fires every ~20 min; there is no reactive refresh on 401). Any live result recorded without a wall-clock timestamp is noise — that is exactly how the original UAT came to hypothesise throttling, which the logs then falsified.

- [ ] Establish the window before starting, and **write the wall-clock time into the report**:

```bash
journalctl -u rotary-phone --since '-30min' | grep -E 'CDP cookie refresh|api2thread/list returned'
```

- [ ] Run steps **C1–C3** and **C6** within **~10 minutes of the most recent `CDP cookie refresh` line** (a healthy window). Do not tail either journal; always bound with `--since`. The box is an Intel N100 and heavy journald reads compete with the audio pipeline.
- [ ] Conversely, a blackout window is a **free, no-setup way to reproduce the 502** — see step C7.

### C. Live UAT — Radio.Web on `:5002`, viewport **1920×720**

Target `http://radio:5002/phone` → **Texts** tab. Record the wall-clock time of every step.

- [ ] **C1 — Baseline (healthy window).** Open a **non-group** thread with a real preview (e.g. `32665`, or any `t.`-prefixed row). **Expect:** message bubbles render; no `cloud_off`; no "Couldn't load messages."; no "Start the conversation below."
- [ ] **C2 — Skeleton (best-effort).** While C1's fetch is in flight the pane should show the five skeleton rows rather than the previous thread's bubbles. On a healthy LAN this is brief; treat a miss as inconclusive, not a failure — the authoritative check is `Conversation_ShowsSkeleton_WhileLoading`. **What IS a failure:** seeing the *previous* thread's bubbles while a different thread is loading.
- [ ] **C3 — Pick an unread thread and note it.** Note a thread row that still shows its unread dot; you will use it in C5. Do not open it yet.
- [ ] **C4 — Forced failure (the core check).** Make the bridge unreachable, then open a thread:

```bash
sudo systemctl stop rotary-phone
```

Click any thread row. **Expect the error state:** a `cloud_off` icon, the text **"Couldn't load messages."**, and a **`Retry`** button. **Expect NOT to see** "Start the conversation below." — that is the F-1 regression, and its absence is the whole point of this PR. _Side effects to expect and ignore: within ~10s the "Google Voice is reconnecting" banner appears and the compose bar becomes the "Texting unavailable" pill. Both are pre-existing behaviour._ Keep the service down for as short a time as possible — it also serves live calls.

- [ ] **C5 — Unread marker survives a failed open (Task 6 only).** Still with `rotary-phone` stopped, go **Back** and look at the thread you noted in C3 after opening it. **Expect:** its unread dot is still present — we did not mark read a conversation the user was never shown. _If Task 6 was dropped, this step is expected to fail; record it as "Task 6 not shipped" rather than as a defect._
- [ ] **C6 — Retry recovers.** Restart the bridge, wait for it to answer, then press `Retry` in the pane's error state:

```bash
sudo systemctl start rotary-phone
sleep 15 && curl -s -o /dev/null -w '%{http_code}\n' http://radio:5004/api/gvbridge/status
```

**Expect:** the conversation's bubbles render in place; the error state is gone; no page reload was needed.

- [ ] **C7 — A genuine empty still reads as empty (the boundary with RotaryPhone's Defect B).** In a **healthy** window, open a **group/MMS** thread — the rows whose ids begin `g.Group Message.` (in the live top-20 these were *Mary Carmen Wiser* and *Darlann Romney*). RotaryPhone returns a real **HTTP 200 with `messages: []`** for these because it never decodes the `%2F`. **Expect: "Start the conversation below."**, i.e. the **empty** state — **not** the error state. This is correct: the server said "zero messages," and misreporting that as a failure would be a new lie in the opposite direction. _If this shows the error state, something is misclassifying a 200._
- [ ] **C8 — Blackout reproduction (free, optional).** During a 401 window (per the `journalctl` output in section B), open any thread. **Expect** the same error state as C4, with no service manipulation.
- [ ] **C9 — Server-side probe.** The failures in C4 (and C8) must be visible in the only place they can be — the server log:

```bash
journalctl -u radio-web --since '-15min' | grep 'Failed to get GV SMS thread'
```

**Expect:** one line per failed open, now carrying the status — e.g. `Failed to get GV SMS thread t.32665: transport failure` for C4 (connection refused) or `... HTTP 502 -` for C8. **Expect 0 browser console errors and 0 failed network requests throughout** — this is Blazor Server, the fetch happens server-side over SignalR, and its failure never reaches browser instrumentation. Do not treat a clean console as a passing signal.

### D. What is explicitly NOT tested here

- **Group threads becoming readable.** That needs RotaryPhone's `%2F` decode (cross-repo item #5). C7 asserts only that our side reports their `200` honestly.
- **The blackout going away.** That needs RotaryPhone's cookie-refresh fix (cross-repo item #6).
- **The composer's disabled-state labelling** and the wording of the empty copy when send is off — F-2's wording half and F-3, both routed to **GV-7**.

---

## Self-review

**1. Spec coverage** — every clause of the GV-8 queue row maps to a task:

| Row clause | Task |
|---|---|
| (a) API client expresses outcomes, not a bare `null` | 1 + 2 |
| (b) `_openThreadError` / `_openThreadLoading` page state, passed through `PhoneMessagesPanel:184-191` | 4 + 5 |
| (b) the unreachable skeleton branch becomes reachable | 4 + 5 (`Loading` wired, `Messages` nulled) — asserted in Task 3's `Conversation_ShowsSkeleton_WhileLoading` |
| (c) missing error branch in `PhoneTextsPanel:36-68`, F-6 pattern verbatim, `cloud_off` + "Couldn't load messages." + `Retry` | 3 |
| (d) F-2 — the empty copy only for a genuine empty | 3 (branch order); wording half explicitly deferred to GV-7 (§ Non-goals 4) |
| Test gap: non-2xx case at `GvBridgeApiServiceVoicemailSmsTests.cs:81` | 2 |
| Test gap: bUnit case asserting the **error** state, not the empty state | 3 |
| Reusable shape GV-6 can adopt | 1 (documented on the type, in INTEGRATIONS, and in FUTURE-WORK) |
| No `%2F` workaround | § Global Constraints + § Non-goals 1 + Test Plan C7 |
| Not merged with GV-6 | § Non-goals 2 (with the reasoning stated, not implied) |
| No pane redesign | § Non-goals 3 |
| Server-side probe, not browser instrumentation | Test Plan C9 + the preserved log substring |
| UAT timing discipline | Test Plan § B |

**2. Placeholder scan** — no `TBD`, no "implement later", no "similar to Task N", no "add appropriate error handling". Every code step carries the literal text to write, including the exact block being replaced.

**3. Type consistency** — `GvResult<T>` members are spelled identically in Tasks 1, 2 and 5: `Outcome`, `Value`, `StatusCode`, `ErrorCode`, `IsSuccess`, `IsFailure`; factories `Success` / `HttpError` / `Timeout` / `Transport` / `Malformed`; enum `GvCallOutcome`. `LoadOpenThreadMessagesAsync` returns `Task<bool>` in Task 5 and is consumed as `bool` in Task 6. `OpenThreadLoading` / `OpenThreadError` / `OnRetryOpenThread` are spelled identically in Tasks 4 and 5. `PhoneTextsPanel`'s `Loading` / `Error` / `OnRetry` are pre-existing and unrenamed.

---

## Handoff

Branch `fix/gv-texts-load-error-state`, 7 tasks, 7 commits. Builder marks the GV-8 row ✅ in `docs/BUILDER_QUEUE.md` after merge. **Preferred ordering note from the queue: GV-8 ships before GV-6** (which then adopts `GvResult<T>`) **and before GV-7** (whose header/empty design should not be built on a pane that cannot express "failed").
