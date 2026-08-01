# GV Messages — PR5: Reconcile the SMS Send Contract

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace GV-3's *anticipated* SMS send contract with RotaryPhone's **as-built** one (ADR-028). Fix the request shape (which currently fails **100%** of sends), adopt the full nine-code `Code` taxonomy, subscribe to the **`SmsSent`** outbound echo channel we have never listened on, and make outbound reconciliation **idempotent — keyed first by exact `Id`, then by `(Outbound, normalized counterparty, ordinal text, |ΔSentAt| ≤ 120s)`**. Ships with `RotaryPhone:Gv:SendEnabled` **still `false`**; flipping it on is a separate, later decision.

---

> ## 🔄 Reconciled against `main` — 2026-07-31 (Planner). **Refresh pass, not a re-plan.**
>
> **GV-8 merged as PR #461 (`b2d1ffc`) after this plan was written.** Every decision below is **unchanged** — the nine-code taxonomy, `send_disabled` as an availability state, the `SmsSent` subscription, `OutboundSmsReconciler` and its idempotency key, `SendEnabled` staying `false`, and ADR-028 §8's reply-ability gate are all settled and were **re-sited, not revisited**. What changed:
>
> **1. Line numbers moved in the three files GV-8 touched.** Every cited anchor in Chunks 5, 6 and 6b was re-verified against `main` and corrected. The offsets, recorded so the diff reads honestly:
>
> | File | GV-8 delta | Effect on this plan's citations |
> |---|---|---|
> | `PhonePage.razor` | +109 lines, all inserted **above** line 686 | every Chunk 5 anchor shifts (`+3` in the markup, `+109` in `@code`) |
> | `PhoneTextsPanel.razor` | +51 lines (+47 before `@code`) | every Chunk 6 / 6b anchor shifts `+47` |
> | `PhoneMessagesPanel.razor` | +12 lines | Chunk 6 Step 7's two anchors shift `+3` / `+12` |
>
> **Unchanged and re-verified byte-exact:** `ApiModels.cs:1187-1188` (the two records to replace) and `GvDirection` at `:1192`; `PhoneHubService.cs:25` / `:94`; `MessageBubble.razor:38`; and **`GvBridgeSendService.cs` in full** — GV-8 did not touch it, so Chunk 2's whole-file replacement is valid as written.
>
> **2. `GvResult<T>` is now adopted for the send path — and it needed a small extension first, which is the new Chunk 0.** GV-8 introduced `GvResult<T>` / `GvCallOutcome` (`src/Radio.Web/Services/ApiClients/GvResult.cs`) explicitly so later rows adopt it rather than reinventing an outcome shape. This plan predates it and hand-rolled one inline (`result is null` → `MapStatusOnly`). **But `GvResult` as shipped cannot express what this endpoint needs**, for a specific reason: `HttpError(status, errorCode)` sets `Value = null`, and that is a *documented, test-pinned invariant* (`GvResult.cs:58` — "non-null if and only if `IsSuccess`"; `GvResultTests.HttpError_CarriesStatusAndErrorCode_AndReportsFailure` asserts `Assert.Null(result.Value)`). GV-5's mapping is driven off the **`Code` in the response body**, not off HTTP status, and it needs **two** fields off a non-2xx body — `Code` (the nine-way discriminator) and `Error` (prose, into the typed exception). `GvResult` today carries exactly one (`ErrorCode`). Chunk 0 adds a **separate** `FailureBody` + `HttpErrorWithBody` factory, deliberately leaving `Value` null so GV-8's invariant and GV-6's branch idiom survive untouched. See Chunk 0 for the rejected alternative (widening `Value`).
>
> **⚠ 3. A trap for anyone "adopting the idiom" the obvious way — recorded, not fixed here.** `GvBridgeApiService.ReadErrorCodeAsync` (`:297-320`) extracts the discriminator by trying `{ "error", "Error", "code", "Code" }` **in that order, first match wins**. That is **correct for the read endpoints**, where RotaryPhone returns `{"error":"upstream_error"}` and `error` *is* the discriminator — and correct for GV-6, whose `{"error":"markread_disabled"}` has the same shape. It is **wrong for `/sms/send`**, whose body carries **both**, with opposite meanings: `code` is the machine taxonomy and `error` is human prose (ADR-028 §2). Feeding a send failure through that helper returns the **prose**, every one of the nine codes falls through `MapCode`'s `_ =>` arm, and the taxonomy silently collapses to a single generic `SendFailedException` — precisely the class of quiet failure this PR exists to remove. **This plan therefore deserializes the whole typed `SendSmsResponse` on non-2xx instead of reusing that helper** (which is also why Chunk 0 stayed small — no hoist, no refactor of GV-8 code). Chunk 0 Step 2 adds a warning comment at the helper so the next reader does not have to rediscover this.
>
> **4. The composer now has to compose with a pane that can render a *failed* state — and that decision is GV-5's.** GV-8 rewrote the conversation pane into **four** branches (skeleton → error → empty → list, `PhoneTextsPanel.razor:43-98`), but `@ComposeBar()` at `:101` still renders **unconditionally**, below whichever branch won. GV-8's UAT recorded this as `O-2`. GV-7 is explicitly forbidden from re-deciding composer behaviour, so **the failed-load case is answered here** — new **Chunk 6b Step 5**, with the reasoning and the rejected alternatives. It is not a taste call: see the concrete defect it prevents.
>
> **5. Chunk 5 Step 4's blast radius widened.** GV-8 added three new call sites keyed on the open thread id (`LoadOpenThreadMessagesAsync`'s stale-response guard, `RestoreThreadUnreadAsync`, `RetryOpenThreadAsync`). Step 4 makes `BumpThread` **rewrite a row's `ThreadId`**, which can now silently desync `_openThreadId` from `_threads`. A companion guard was added to Step 4.
>
> **6. Three pre-existing plan defects fixed while re-siting** (none GV-8's doing, all fatal to a literal-code plan): Chunk 2 Step 3's new test referenced a `NeverCompletesHandler` class **that does not exist** (the real helper is `BlockingHandler(Task<HttpResponseMessage>)`); Chunk 6 Step 1 was the plan's only prose-only step and is now literal; and the Test Plan's §E re-used numbers 27–32 already spent by §D2.
>
> **Still true, re-verified, and deliberately NOT changed:** the duplicate-thread-row bug (ADR-028 §6) **still reproduces** — `BumpThread` (`PhonePage.razor:828-849`) is absent from GV-8's diff and still matches on `ThreadId` alone with an unconditional insert-new `else`. It remains **derived from RotaryPhone's source, not observed live**, because reproducing it needs `SendEnabled=true` on both sides.

---

**Owner-baked decisions in scope here (from ADR-028 — do not redesign):**
- **The request is four fields:** `{ toNumber, text, threadId, clientCorrelationId }`. `toNumber` is **never** a thread id. Reply mode sends the real thread id; new-recipient mode sends `threadId: null` (ADR-028 §3).
- **`ClientCorrelationId` IS wired**, set to the optimistic bubble's own client id, so the immediate echo matches on exact id and the bubble's client-side id never changes mid-flight (ADR-028 §3). The optimistic id format becomes `rc:{guid:N}` (it is no longer "temp" — it travels cross-service and other clients see it).
- **`Queued: true` → `SendStatus.Sent`. No fourth bubble state.** The poller's later re-surface is a silent data-level swap, not a visible "confirmed" transition — neither side can honestly assert delivery (ADR-028 §4.1).
- **`send_disabled` (409) is NOT a failed send** (ADR-028 §5.1). No GV call was made; nothing was sent; retry is futile. Remove the optimistic bubble, restore the text, show the "Texting unavailable" affordance — the same path as the degraded gate, distinct from a red failed bubble.
- **`invalid_number` / `invalid_text` are terminal** — failed bubble with **no Retry**, and `invalid_number` surfaces inline on the recipient field in new-recipient mode (ADR-028 §5.4).
- **Never auto-retry, always preserve the composed text** (ADR-022 D7, unchanged and still correct — a send is an irreversible account write).
- **The de-dupe invariant is the one correctness rule** (ADR-028 §4.4). RotaryPhone broadcasts `SmsSent` to `Clients.All` — including back to the sender — and the poller re-surfaces the same message later **with a different `Id`**. Same shape of problem ADR-024 §9 solved for read-state; stay consistent with it.
- **No backward compatibility.** The provisional `SendSmsRequest`/`SendSmsResponse` records are **replaced**, not versioned. Nothing outside `Radio.Web` consumes them.

**Sources of truth (do not redesign):**
- **ADR-028** (this contract): `design/decisions/2026-07-30-gv-sms-send-contract.md` — §2 (taxonomy), §3 (request), §4 (echo + de-dupe), §5 (UI mapping).
- **As-built RotaryPhone source** (authoritative over their docs, some of which are stale):
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Api/GvSmsController.cs` lines 86–162
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Api/GvBridgeReadDtos.cs` lines 50–80
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Services/GvThreadPoller.cs` lines 106–124, 164–174
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.Server/Services/GvMessagePushBridge.cs` line 54
- **Handoff copy matrix + bubble states:** `docs/design-handoffs/HANDOFF-phone-messages-voicemail-sms.md` §"Outbound write-path bubble states", §"Send-failure copy matrix".
- **GV-3's as-shipped send surface** (what we are replacing): `docs/superpowers/plans/2026-06-20-gv-messages-pr3-texts-surface.md` Chunk 1 Task 1.
- **The reconciler pattern to mirror:** `src/Radio.Web/Services/ReadStateReconciler.cs` (GV-4) — static, list-mutating, returns `bool` changed, unit-tested in isolation.

**Tech stack:** Blazor Server, Radzen, SignalR client (`PhoneHubService` on `/hub`), `design-system.css` tokens. No new JS, no new component, no new hub, no new auth posture.

**Dependencies:** **GV-3 must be merged** (`GvBridgeSendService`, `PhoneTextsPanel` compose/reply, `MessageBubble`, the optimistic-append seam in `PhonePage`). GV-4 is merged and unrelated except that its `ReadStateReconciler` is the pattern this plan mirrors. **GV-8 is merged (PR #461)** and is a *soft* dependency in two directions: it supplies `GvResult<T>`, which Chunk 0 extends and Chunk 2 adopts, and it supplies the conversation pane's error branch, which Chunk 6b Step 5 must compose with. **No external dependency:** RotaryPhone's endpoint is shipped; it is dark behind *their* `GVBridge:EnableSmsSend=false`, which does not block us — a dark server returns `409 send_disabled`, which §5.1 now handles as a first-class state.

**Interacts with, but does not block:** **GV-7** consumes `GvCounterparty` from Chunk 6b and must not re-decide composer behaviour (this plan owns it — see Chunk 6b Steps 4 and 5). **GV-6** adopts `GvResult<T>` for the two mark-read methods; Chunk 0 is additive and leaves its idiom (`IsFailure` + `StatusCode` + `ErrorCode`) intact. **GV-9** touches `PhoneTextsPanel`'s thread-list branch; if both are in flight, expect that file to move.

---

## File Map

### New files

| File | Responsibility |
|------|---------------|
| `src/Radio.Web/Services/OutboundSmsReconciler.cs` | The ADR-028 §4.4 invariant in isolation: exact-`Id` then fuzzy `(Outbound, counterparty, text, ≤ window)` match; **replace in place**, never remove-and-append. |
| `tests/Radio.Web.Tests/Services/OutboundSmsReconcilerTests.cs` | Unit tests for the invariant — the highest-value tests in this PR. |
| `tests/Radio.Web.Tests/Services/GvBridgeSendServiceCodeMappingTests.cs` | One test per `Code` → typed exception, plus the defensive 200-but-not-queued cases. |

### Modified files

| File | Changes |
|------|---------|
| `src/Radio.Web/Services/ApiClients/GvResult.cs` | **(Chunk 0, added 2026-07-31)** Add `FailureBody` + the `HttpErrorWithBody` factory so a non-2xx that carries a *meaningful typed body* — which `/sms/send` does and no read endpoint does — can be expressed without breaking the `Value`-non-null-iff-`IsSuccess` invariant GV-8 documented and GV-6 will branch on. |
| `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | **(Chunk 0, added 2026-07-31)** Comment only, **no behaviour change**: warn at `ReadErrorCodeAsync` that its `error`-before-`code` precedence is right for the read endpoints and **wrong for `/sms/send`**, whose body carries both with opposite meanings. |
| `tests/Radio.Web.Tests/Services/GvResultTests.cs` | **(Chunk 0, added 2026-07-31)** Cases for the new factory, including one that **pins `Value` still null** on `HttpErrorWithBody`. |
| `src/Radio.Web/Models/ApiModels.cs` | **Replace** `SendSmsRequest`/`SendSmsResponse` with the as-built shapes; add `GvSendCode` constants. Delete the `// shape provisional` comment. |
| `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` | Real request; parse `Queued`/`Code`; map the nine codes to typed exceptions; add `SendDisabledException`, `SendRejectedException`, `SendTimedOutException`, `SendFailedException`; single-flight key tolerates a null thread id; new `SendAsync` signature. |
| `src/Radio.Web/Services/Hub/PhoneHubService.cs` | Add `.On<SmsMessageDto>("SmsSent", …)` on the **existing `/hub`** + `event Action<SmsMessageDto>? GvSmsSent`. |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Subscribe `GvSmsSent` → route through `OutboundSmsReconciler`; **delete** the unreachable `temp-` de-dupe from `OnGvSmsReceived`; `BumpThread` falls back to a normalized-counterparty match so a new conversation does not produce a duplicate thread row. |
| `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` | Pass `toNumber`/`threadId`/`clientCorrelationId`; `rc:` id format; per-code catch blocks + copy; 10s rate-limit cooldown; server-dark → unavailable affordance; terminal codes suppress Retry. |
| `src/Radio.Web/Components/Pages/MessageBubble.razor` | `Retryable` parameter so terminal failures render no Retry target. |
| `src/Radio.Web/appsettings.json` | Add `RotaryPhone:Gv:SendDedupeWindowSeconds: 120`. `SendEnabled` stays `false`. |
| `tests/Radio.Web.Tests/Services/GvBridgeSendServiceTests.cs` | Update for the new `SendAsync` signature; keep the flag-off / degraded / in-flight contracts. |
| `tests/Radio.Web.Tests/Components/MessageBubbleTests.cs` | Add the non-retryable render case. |
| `design/FUTURE-WORK.md` | Send is now contract-correct and flag-gated; remaining work is a **two-side config flip**, not a build. |
| `design/INTEGRATIONS.md` | Document the send route, the nine-code taxonomy, `SmsSent`, and the de-dupe window key. |
| `design/DECISION-LOG.md` | ADR-028 pointer entry. |
| `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md` | Supersession banner for D7 (§7). |

---

## Chunk 0: Let `GvResult<T>` carry a typed failure body (added 2026-07-31)

### Task 0: `FailureBody` + `HttpErrorWithBody`, and a hazard note on `ReadErrorCodeAsync`

**Files:**
- Modify: `src/Radio.Web/Services/ApiClients/GvResult.cs`
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` (comment only)
- Test: `tests/Radio.Web.Tests/Services/GvResultTests.cs` (extend)

> **Why this chunk exists.** GV-8 introduced `GvResult<T>` / `GvCallOutcome` as *the* reusable outcome shape, and this PR should adopt it rather than hand-roll a second one. It cannot be adopted as-is. `GvResult` models "**success carries a value, failure carries none**" — exactly right for a read, where a non-2xx body is only a diagnostic string. `/api/gvbridge/sms/send` is different in kind: **its failure body is the contract.** ADR-028 §2 is nine `Code`s in the body, not nine HTTP statuses, and mapping needs two fields off a non-2xx — `Code` (the discriminator) and `Error` (prose, into the typed exception's message).
>
> **Do NOT solve this by widening `Value` to be populated on `HttpError`.** That invariant is stated in the type's own XML doc (`GvResult.cs:58`) and **pinned by a test** (`GvResultTests.HttpError_CarriesStatusAndErrorCode_AndReportsFailure` → `Assert.Null(result.Value)`). GV-6 is about to branch on `IsFailure` and then read `StatusCode`/`ErrorCode`; a `Value` that can be non-null on a failure invites `result.Value!` on a failure path, which is the same shape of bug GV-8 was written to remove. A **separate** property costs three lines and keeps the invariant absolute.
>
> This chunk is deliberately additive: **no existing factory, property or test changes**, so every GV-8 test stays green untouched.

- [ ] **Step 1: Add `FailureBody` and the `HttpErrorWithBody` factory**

In `src/Radio.Web/Services/ApiClients/GvResult.cs`, extend the private constructor and add the property + factory. The constructor currently takes `(outcome, value, statusCode, errorCode)`; add a fifth parameter defaulted to `null` so the four existing factories are unchanged:

```csharp
  private GvResult(GvCallOutcome outcome, T? value, HttpStatusCode? statusCode,
    string? errorCode, T? failureBody = null)
  {
    Outcome = outcome;
    Value = value;
    StatusCode = statusCode;
    ErrorCode = errorCode;
    FailureBody = failureBody;
  }
```

Beside the existing `ErrorCode` property:

```csharp
  /// <summary>
  /// The deserialized body of a NON-2xx response, for the one endpoint whose failure body is
  /// part of the contract rather than a diagnostic: <c>POST /api/gvbridge/sms/send</c> answers
  /// every failure with the full <c>SendSmsResponse</c> shape, and ADR-028 §2's nine-code
  /// taxonomy is read out of it (the HTTP status is only corroboration).
  /// <para>
  /// DELIBERATELY SEPARATE FROM <see cref="Value"/>. <c>Value</c> is non-null if and only if
  /// <see cref="IsSuccess"/>, and that stays absolute — GV-6 and every other consumer branch on
  /// <c>IsFailure</c> and must never be tempted into <c>Value!</c> on a failure path. Null for
  /// every read endpoint, and null whenever the failure body was absent or unparseable.
  /// </para>
  /// </summary>
  public T? FailureBody { get; }
```

and beside the existing factories:

```csharp
  /// <summary>
  /// A non-2xx whose body DID deserialize into the expected DTO. <see cref="Value"/> stays null
  /// (this is still a failure); the payload is exposed as <see cref="FailureBody"/>. Use only
  /// where the failure body is contractual — today that is the SMS send endpoint alone.
  /// </summary>
  public static GvResult<T> HttpErrorWithBody(HttpStatusCode statusCode, T body,
    string? errorCode = null) =>
    new(GvCallOutcome.HttpError, null, statusCode, errorCode, body);
```

> **Implementer note:** `Outcome` is still `HttpError` — this is *not* a new `GvCallOutcome` member. The outcome enum answers "how did the call end," which is unchanged; only "did we manage to keep the body" differs, and that is a property, not an outcome.

- [ ] **Step 2: Add the hazard comment to `ReadErrorCodeAsync` (no behaviour change)**

In `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`, immediately above the `foreach (var name in new[] { "error", "Error", "code", "Code" })` loop (`:312`):

```csharp
      // ⚠ PRECEDENCE IS LOAD-BEARING, and it is right ONLY for endpoints whose failure body
      // carries ONE discriminator. The read routes return {"error":"upstream_error"} and GV-6's
      // dark-flag case returns {"error":"markread_disabled"} — for both, `error` IS the code and
      // trying it first is correct.
      // It is WRONG for POST /api/gvbridge/sms/send, whose body carries BOTH with OPPOSITE
      // meanings: `code` is the machine taxonomy (ADR-028 §2, nine values) and `error` is human
      // prose. This helper would return the prose, every code would fall through to the generic
      // failure, and the taxonomy would silently collapse to one. GV-5 therefore does NOT use
      // this helper — it deserializes the whole typed SendSmsResponse on non-2xx. If a future
      // endpoint needs `code`-first, add a preference parameter rather than reordering here.
```

**Do not change the array or the order.** Two shipped call sites depend on the current behaviour.

- [ ] **Step 3: Extend `GvResultTests`**

Append to `tests/Radio.Web.Tests/Services/GvResultTests.cs`:

```csharp
  [Fact]
  public void HttpErrorWithBody_CarriesTheBody_ButValueStaysNull()
  {
    // GV-5 / ADR-028 §2: the send endpoint's failure body IS the contract. It must be
    // reachable — and it must NOT arrive via Value, because "Value is non-null iff
    // IsSuccess" is the invariant GV-6 branches on.
    var result = GvResult<SmsThreadMessagesDto>.HttpErrorWithBody(
      HttpStatusCode.Conflict, Dto, "send_disabled");

    Assert.True(result.IsFailure);
    Assert.False(result.IsSuccess);
    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    Assert.Equal("send_disabled", result.ErrorCode);
    Assert.Same(Dto, result.FailureBody);
    Assert.Null(result.Value);                 // the invariant, pinned
  }

  [Fact]
  public void EveryOtherFactory_LeavesFailureBodyNull()
  {
    // Read endpoints never populate it; nothing may start depending on it there.
    Assert.Null(GvResult<SmsThreadMessagesDto>.Success(Dto).FailureBody);
    Assert.Null(GvResult<SmsThreadMessagesDto>.HttpError(HttpStatusCode.BadGateway).FailureBody);
    Assert.Null(GvResult<SmsThreadMessagesDto>.Timeout().FailureBody);
    Assert.Null(GvResult<SmsThreadMessagesDto>.Transport().FailureBody);
    Assert.Null(GvResult<SmsThreadMessagesDto>.Malformed().FailureBody);
  }
```

- [ ] **Step 4: Verify — GV-8's existing cases must be untouched and green**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvResult|FullyQualifiedName~GvBridgeApiServiceVoicemailSms"
```

---

## Chunk 1: The DTOs

### Task 1: Replace SendSmsRequest / SendSmsResponse with the as-built shapes

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs`

> ADR-028 §2/§3. Greenfield rule: **replace**, do not version. The old two-field records have exactly one consumer (`GvBridgeSendService`), updated in Task 2. Note `Queued` and `Code` come first in their record — but we bind by **name**, not position, so field order is irrelevant on the wire; it matters only for our own construction in tests.

- [ ] **Step 1: Replace the two records and add the code constants**

In `src/Radio.Web/Models/ApiModels.cs`, replace the block currently reading:

```csharp
// ── Send (flagged; wired in PR3, endpoint ships later) ─────────
public record SendSmsRequest(string ThreadId, string Text);
public record SendSmsResponse(SmsMessageDto? Message, string? Error);  // shape provisional
```

with:

```csharp
// ── Send (ADR-028 — as-built contract; flag-gated by RotaryPhone:Gv:SendEnabled) ──
/// <summary>
/// Cross-service SMS send request (ADR-028 §3). Mirrors RotaryPhone's
/// GvBridgeReadDtos.SendSmsRequest exactly.
/// • ToNumber — whatever the user typed; RotaryPhone normalizes to E.164. NEVER a thread id:
///   their normalizer strips non-digits, so a synthesized "t.+1555…" id would silently
///   "normalize" to a plausible number instead of failing.
/// • ThreadId — OPTIONAL. Present = reply to an existing thread (Google's real id);
///   null = start a new conversation.
/// • ClientCorrelationId — OPTIONAL. We ALWAYS send it (ADR-028 §3): the server uses it
///   verbatim as the echo's Id, so the immediate SmsSent echo matches our optimistic
///   bubble on exact id and the bubble's client-side id never changes mid-flight.
/// </summary>
public record SendSmsRequest(
  string ToNumber,
  string Text,
  string? ThreadId,
  string? ClientCorrelationId);

/// <summary>
/// Cross-service SMS send result (ADR-028 §2). Queued=true means GOOGLE ACCEPTED the send
/// (HTTP 200) — NOT confirmed delivery. Never report delivery.
/// Code is the machine-readable outcome; map it WITHOUT parsing Error prose.
/// Error is human-readable (log it; do not surface it raw — use the handoff copy matrix).
/// </summary>
public record SendSmsResponse(
  bool Queued,
  string Code,
  string? ThreadId,
  string? Error,
  SmsMessageDto? Message);

/// <summary>
/// The complete Code taxonomy (ADR-028 §2), verified against RotaryPhone's as-built
/// GvSmsController. Unknown values must be handled defensively as a generic failure.
/// </summary>
public static class GvSendCode
{
  public const string Queued = "queued";                   // 200
  public const string SendDisabled = "send_disabled";      // 409 — server flag off, NO GV call
  public const string RateLimited = "rate_limited";        // 429 — 5 per 10s, process-wide
  public const string InvalidText = "invalid_text";        // 400 — terminal
  public const string InvalidNumber = "invalid_number";    // 400 — terminal
  public const string AuthUnavailable = "auth_unavailable";// 502 — cookie decay
  public const string UpstreamError = "upstream_error";    // 502
  public const string Timeout = "timeout";                 // 504 — AMBIGUOUS, may have landed
  public const string Error = "error";                     // 500 — unclassified
}
```

- [ ] **Step 2: Verify**

```bash
dotnet build src/Radio.Web --configuration Release
```

Expect exactly one break — `GvBridgeSendService.cs` constructing the old two-arg request. Task 2 fixes it. Do not commit until Task 2 compiles.

---

## Chunk 2: GvBridgeSendService — the real contract

### Task 2: Real request, code-driven error mapping, typed exceptions

**Files:**
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs`
- Test: `tests/Radio.Web.Tests/Services/GvBridgeSendServiceCodeMappingTests.cs` (new)
- Test: `tests/Radio.Web.Tests/Services/GvBridgeSendServiceTests.cs` (update)

> ADR-028 §2/§3/§5. The critical fix is the **request**: today we POST `{"threadId":…,"text":…}`, their `ToNumber` binds `null`, and every send returns `400 invalid_number`. Mapping is driven by **`Code`**, not HTTP status — status is only the fallback when the body is unparseable.
>
> **Updated 2026-07-31:** the transport half of this task now runs through **`GvResult<SendSmsResponse>`** (Chunk 0) instead of the inline `result is null` handling this plan originally specified. The **typed exceptions stay** — ADR-028 §5's whole mapping table is written in exception terms and `PhoneTextsPanel`'s per-code catch ladder consumes them, so `GvResult` is the *internal transport inside `SendAsync`*, converted to the settled exception at the boundary. Two things get strictly better and neither is a scope change: a `PostAsJsonAsync` **timeout** now maps to `SendTimedOutException` instead of escaping `SendAsync` as a raw `TaskCanceledException` into the UI's generic `catch (Exception)`, and a **transport failure** becomes a mapped `SendFailedException` rather than a bare `HttpRequestException`. `GvBridgeSendService.cs` was **not** touched by GV-8, so the rest of this task's whole-file replacement applies verbatim.

- [ ] **Step 1: Write the code-mapping tests first (TDD)**

Create `tests/Radio.Web.Tests/Services/GvBridgeSendServiceCodeMappingTests.cs`:

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class GvBridgeSendServiceCodeMappingTests
{
  // Mirrors the AvailableStatus() helper GV-3 already uses in GvBridgeSendServiceTests —
  // the ctor takes (HttpClient, ILogger, pollSeconds) and availability is applied via
  // ApplyStatusForTest. Do NOT invent a second mechanism.
  private static GvBridgeStatusService AvailableStatus()
  {
    var s = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    s.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    return s;
  }

  // Returns a service whose HttpClient always answers with the given status + JSON body.
  private static GvBridgeSendService Build(HttpStatusCode status, string json)
  {
    var client = new HttpClient(new StubHandler(status, json))
    {
      BaseAddress = new Uri("http://radio:5004")
    };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:Gv:SendEnabled"] = "true"
      })
      .Build();
    return new GvBridgeSendService(client, NullLogger<GvBridgeSendService>.Instance,
      config, AvailableStatus());
  }

  private static string Body(bool queued, string code, string? error = "boom")
    => $$"""{"queued":{{(queued ? "true" : "false")}},"code":"{{code}}","threadId":null,"error":"{{error}}","message":null}""";

  private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
      => Task.FromResult(new HttpResponseMessage(status)
      {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
      });
  }

  [Theory]
  [InlineData(HttpStatusCode.Conflict, GvSendCode.SendDisabled, typeof(SendDisabledException))]
  [InlineData(HttpStatusCode.TooManyRequests, GvSendCode.RateLimited, typeof(SendRateLimitedException))]
  [InlineData(HttpStatusCode.BadRequest, GvSendCode.InvalidNumber, typeof(SendRejectedException))]
  [InlineData(HttpStatusCode.BadRequest, GvSendCode.InvalidText, typeof(SendRejectedException))]
  [InlineData(HttpStatusCode.BadGateway, GvSendCode.AuthUnavailable, typeof(SendUnavailableException))]
  [InlineData(HttpStatusCode.BadGateway, GvSendCode.UpstreamError, typeof(SendFailedException))]
  [InlineData(HttpStatusCode.GatewayTimeout, GvSendCode.Timeout, typeof(SendTimedOutException))]
  [InlineData(HttpStatusCode.InternalServerError, GvSendCode.Error, typeof(SendFailedException))]
  public async Task Code_MapsToTypedException(HttpStatusCode status, string code, Type expected)
  {
    var svc = Build(status, Body(queued: false, code));
    var ex = await Record.ExceptionAsync(() => svc.SendAsync("+15551234567", "hi", null, "rc:1"));
    Assert.IsType(expected, ex);
  }

  [Fact]
  public async Task UnknownCode_FallsBackToGenericFailure()
  {
    var svc = Build(HttpStatusCode.InternalServerError, Body(queued: false, "brand_new_code"));
    await Assert.ThrowsAsync<SendFailedException>(
      () => svc.SendAsync("+15551234567", "hi", null, "rc:1"));
  }

  [Fact]
  public async Task Http200_ButQueuedFalse_IsAFailure()
  {
    // Defensive (ADR-028 §4.1): a 200 that is not an honest "queued" must not read as success.
    var svc = Build(HttpStatusCode.OK, Body(queued: false, GvSendCode.Queued));
    await Assert.ThrowsAsync<SendFailedException>(
      () => svc.SendAsync("+15551234567", "hi", null, "rc:1"));
  }

  [Fact]
  public async Task Http200_QueuedTrue_ButNullMessage_IsAFailure()
  {
    var json = """{"queued":true,"code":"queued","threadId":"t.1","error":null,"message":null}""";
    var svc = Build(HttpStatusCode.OK, json);
    await Assert.ThrowsAsync<SendFailedException>(
      () => svc.SendAsync("+15551234567", "hi", null, "rc:1"));
  }

  [Fact]
  public async Task Http200_QueuedTrue_ReturnsTheEchoedMessage()
  {
    var json = """
      {"queued":true,"code":"queued","threadId":"t.+15551234567","error":null,
       "message":{"id":"rc:1","threadId":"t.+15551234567","direction":"Outbound",
                  "counterpartyNumber":"+15551234567","text":"hi",
                  "sentAt":"2026-07-30T12:00:00Z","isRead":true}}
      """;
    var svc = Build(HttpStatusCode.OK, json);
    var msg = await svc.SendAsync("+15551234567", "hi", null, "rc:1");
    Assert.Equal("rc:1", msg.Id);                 // our ClientCorrelationId came back as the Id
    Assert.Equal("t.+15551234567", msg.ThreadId);
  }

  [Fact]
  public async Task RequestBody_CarriesToNumberAndCorrelationId()
  {
    // The defect this PR exists to fix: ToNumber must NOT be null and must NOT be a thread id.
    string? captured = null;
    var handler = new CapturingHandler(json => captured = json);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:Gv:SendEnabled"] = "true"
      })
      .Build();
    var svc = new GvBridgeSendService(client, NullLogger<GvBridgeSendService>.Instance,
      config, AvailableStatus());

    await Record.ExceptionAsync(() => svc.SendAsync("555-123-4567", "hey", "t.abc", "rc:42"));

    Assert.NotNull(captured);
    Assert.Contains("\"toNumber\":\"555-123-4567\"", captured);
    Assert.Contains("\"threadId\":\"t.abc\"", captured);
    Assert.Contains("\"clientCorrelationId\":\"rc:42\"", captured);
  }

  private sealed class CapturingHandler(Action<string> capture) : HttpMessageHandler
  {
    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      capture(await request.Content!.ReadAsStringAsync(cancellationToken));
      return new HttpResponseMessage(HttpStatusCode.InternalServerError)
      {
        Content = new StringContent(
          """{"queued":false,"code":"error","threadId":null,"error":"x","message":null}""",
          Encoding.UTF8, "application/json")
      };
    }
  }
}
```

> **Implementer note:** `AvailableStatus()` is duplicated from `GvBridgeSendServiceTests` deliberately — the two test classes are independent. If you prefer, hoist it into a shared internal test helper, but do **not** add a new seam to `GvBridgeStatusService`: `ApplyStatusForTest` already exists and is the sanctioned mechanism.

- [ ] **Step 2: Rewrite `GvBridgeSendService`**

Replace the whole of `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` with:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Radio.Core.Utilities;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Services.ApiClients;

/// <summary>OUR flag (RotaryPhone:Gv:SendEnabled) is off — compose should not be reachable.</summary>
public class SendNotAvailableException : Exception
{
  public SendNotAvailableException()
    : base("Texting send is coming soon.") { }
}

/// <summary>
/// THEIR flag (GVBridge:EnableSmsSend) is off — 409 send_disabled. NOT a failed send
/// (ADR-028 §5.1): no GV call was made, nothing was sent, retry cannot help.
/// </summary>
public class SendDisabledException : Exception
{
  public SendDisabledException()
    : base("Texting is disabled on the phone service.") { }
}

/// <summary>
/// Transient unavailability: our degraded gate (GvBridgeStatusService) OR the server's
/// auth_unavailable (GV cookie decay). Retry shortly.
/// </summary>
public class SendUnavailableException : Exception
{
  public SendUnavailableException()
    : base("Google Voice is reconnecting.") { }
}

/// <summary>HTTP 429 / rate_limited — preserve text, cool down, NEVER auto-retry.</summary>
public class SendRateLimitedException : Exception { }

/// <summary>A send is already outstanding for this target (single-flight guard).</summary>
public class SendInFlightException : Exception { }

/// <summary>
/// TERMINAL rejection — invalid_number / invalid_text. Retrying the SAME input cannot
/// succeed, so the UI offers no Retry and points the user at the field to fix.
/// </summary>
public class SendRejectedException(string code, string? serverError) : Exception(serverError)
{
  public string Code { get; } = code;
}

/// <summary>
/// 504 / timeout — AMBIGUOUS. No response was observed; the send may or may not have
/// reached Google. Never auto-retry (ADR-028 §5.3).
/// </summary>
public class SendTimedOutException : Exception { }

/// <summary>upstream_error / error / unknown code / malformed 200 — generic retryable failure.</summary>
public class SendFailedException(string code, string? serverError) : Exception(serverError)
{
  public string Code { get; } = code;
}

/// <summary>
/// Isolated GV SMS send seam (ADR-022 D7 → superseded by ADR-028). The read client stays
/// unconditionally safe; this is the only write path.
///
/// Guardrails: single-flight per target, degraded gate, and Code-driven error mapping with
/// NO auto-retry (a send is an irreversible account write on the GV side).
///
/// Contract: POST /api/gvbridge/sms/send
///   { toNumber, text, threadId?, clientCorrelationId? }
///   → { queued, code, threadId?, error?, message? }
/// Mapping is driven by `code`, not HTTP status; status is only the fallback when the body
/// cannot be parsed. See ADR-028 §2 for the complete nine-code taxonomy.
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

  /// <summary>
  /// Send an SMS. Returns the server's echoed OUTBOUND message on success (Queued == true).
  /// </summary>
  /// <param name="toNumber">Recipient as typed. NEVER a thread id (ADR-028 §3).</param>
  /// <param name="text">Message body.</param>
  /// <param name="threadId">Real GV thread id when replying; null to start a conversation.</param>
  /// <param name="clientCorrelationId">Our optimistic bubble's id; comes back as Message.Id.</param>
  public async Task<SmsMessageDto> SendAsync(string toNumber, string text,
    string? threadId, string? clientCorrelationId, CancellationToken ct = default)
  {
    // Check order matters (ADR-022 D7 + handoff guardrails): our flag → degraded →
    // single-flight, then the POST. Each gate throws a distinct typed exception the
    // compose UI catches to render the right calm message.
    if (!SendEnabled) throw new SendNotAvailableException();
    if (!_status.IsAvailable) throw new SendUnavailableException();

    // Single-flight key: threadId when replying, else the normalized recipient — a
    // new-conversation send has no thread id yet, and keying on "" would collapse every
    // concurrent new-recipient send into one slot.
    var flightKey = !string.IsNullOrWhiteSpace(threadId)
      ? threadId
      : PhoneNumberNormalizer.Normalize(toNumber);
    if (!_inFlight.TryAdd(flightKey, 1)) throw new SendInFlightException();

    try
    {
      _logger.LogDebug("Sending GV SMS to {To} on thread {ThreadId}", toNumber, threadId ?? "(new)");

      var result = await PostSendAsync(toNumber, text, threadId, clientCorrelationId, ct);

      // 1) No usable response at all — there is no Code to map, so map the OUTCOME.
      if (result.Outcome is GvCallOutcome.Timeout
          or GvCallOutcome.Transport
          or GvCallOutcome.Malformed)
      {
        throw MapOutcome(result.Outcome);
      }

      // 2) Non-2xx. The BODY is the contract on this endpoint (ADR-028 §2) — nine codes,
      //    not nine statuses. FailureBody (Chunk 0) carries it; Value stays null by design.
      //    An unparseable failure body is the only case that falls back to status.
      if (result.Outcome == GvCallOutcome.HttpError)
      {
        var failure = result.FailureBody;
        _logger.LogWarning("GV send failed: code={Code} status={Status} error={Error}",
          failure?.Code ?? "(unparseable)", (int)result.StatusCode!.Value, failure?.Error);
        throw failure is null
          ? MapStatusOnly(result.StatusCode!.Value)
          : MapCode(failure.Code, failure.Error);
      }

      // 3) 2xx that deserialized. A 2xx is NOT automatically an honest success —
      //    ADR-028 §4.1 requires Queued == true AND Code == "queued" AND a Message.
      var body = result.Value!;
      if (!body.Queued || !string.Equals(body.Code, GvSendCode.Queued, StringComparison.Ordinal))
      {
        _logger.LogWarning("GV send returned 2xx but not an honest queued: code={Code} queued={Queued}",
          body.Code, body.Queued);
        throw MapCode(body.Code, body.Error);
      }

      // Queued == true. This means GOOGLE ACCEPTED it — not that it was delivered.
      if (body.Message is null)
      {
        _logger.LogWarning("GV send reported queued but returned no message");
        throw new SendFailedException(GvSendCode.Error, "Send returned no message");
      }

      return body.Message;
    }
    finally
    {
      _inFlight.TryRemove(flightKey, out _);
    }
  }

  /// <summary>
  /// POST the send and reduce it to an outcome (Chunk 0 / GV-8's GvResult idiom) rather than
  /// letting transport exceptions escape into the compose UI's generic catch. On a NON-2xx the
  /// whole typed SendSmsResponse is kept via HttpErrorWithBody — the failure body is contractual
  /// here, unlike every read endpoint.
  ///
  /// Deliberately does NOT use GvBridgeApiService.ReadErrorCodeAsync: that helper tries "error"
  /// BEFORE "code", which is right for the read routes but returns this endpoint's human PROSE
  /// instead of its machine code, collapsing all nine codes into one generic failure.
  /// </summary>
  private async Task<GvResult<SendSmsResponse>> PostSendAsync(string toNumber, string text,
    string? threadId, string? clientCorrelationId, CancellationToken ct)
  {
    try
    {
      using var response = await _httpClient.PostAsJsonAsync("/api/gvbridge/sms/send",
        new SendSmsRequest(toNumber, text, threadId, clientCorrelationId), ct);

      SendSmsResponse? body = null;
      try
      {
        body = await response.Content.ReadFromJsonAsync<SendSmsResponse>(cancellationToken: ct);
      }
      catch (Exception ex)
      {
        // A body we cannot read is a failure, but not a DIFFERENT failure — keep the status.
        _logger.LogWarning(ex, "GV send returned an unparseable body ({Status})",
          (int)response.StatusCode);
      }

      if (!response.IsSuccessStatusCode)
      {
        return body is null
          ? GvResult<SendSmsResponse>.HttpError(response.StatusCode)
          : GvResult<SendSmsResponse>.HttpErrorWithBody(response.StatusCode, body, body.Code);
      }

      // 2xx with no readable body is malformed, not success — §4.1 is checked by the caller
      // against a body that actually exists.
      return body is null
        ? GvResult<SendSmsResponse>.Malformed()
        : GvResult<SendSmsResponse>.Success(body);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;                    // the CALLER cancelled — not a send failure
    }
    catch (OperationCanceledException)
    {
      // ADR-028 §5.3: ambiguous. The send may or may not have reached Google.
      _logger.LogWarning("GV send timed out (thread {ThreadId})", threadId ?? "(new)");
      return GvResult<SendSmsResponse>.Timeout();
    }
    catch (HttpRequestException ex)
    {
      _logger.LogWarning(ex, "GV send transport failure (thread {ThreadId})", threadId ?? "(new)");
      return GvResult<SendSmsResponse>.Transport();
    }
  }

  // ADR-028 §2 taxonomy → typed exception. Unknown codes degrade to a generic retryable
  // failure; never throw on an unrecognized value.
  private static Exception MapCode(string? code, string? error) => code switch
  {
    GvSendCode.SendDisabled => new SendDisabledException(),
    GvSendCode.RateLimited => new SendRateLimitedException(),
    GvSendCode.InvalidNumber => new SendRejectedException(GvSendCode.InvalidNumber, error),
    GvSendCode.InvalidText => new SendRejectedException(GvSendCode.InvalidText, error),
    GvSendCode.AuthUnavailable => new SendUnavailableException(),
    GvSendCode.Timeout => new SendTimedOutException(),
    GvSendCode.UpstreamError => new SendFailedException(GvSendCode.UpstreamError, error),
    GvSendCode.Error => new SendFailedException(GvSendCode.Error, error),
    _ => new SendFailedException(code ?? GvSendCode.Error, error),
  };

  // No response body existed to read a Code out of. Maps how the CALL ended, not what the
  // server said — deliberately narrower than MapCode, and Timeout stays ambiguous (§5.3).
  private static Exception MapOutcome(GvCallOutcome outcome) => outcome switch
  {
    GvCallOutcome.Timeout => new SendTimedOutException(),
    GvCallOutcome.Transport => new SendFailedException(GvSendCode.Error, "No connection to the phone service"),
    _ => new SendFailedException(GvSendCode.Error, "Send returned an unreadable response"),
  };

  // Fallback when a NON-2xx arrived but its body could not be parsed — status only.
  private static Exception MapStatusOnly(HttpStatusCode status) => status switch
  {
    HttpStatusCode.Conflict => new SendDisabledException(),
    HttpStatusCode.TooManyRequests => new SendRateLimitedException(),
    HttpStatusCode.BadRequest => new SendRejectedException(GvSendCode.InvalidNumber, null),
    HttpStatusCode.BadGateway => new SendUnavailableException(),
    HttpStatusCode.GatewayTimeout => new SendTimedOutException(),
    _ => new SendFailedException(GvSendCode.Error, $"Send failed: {(int)status}"),
  };
}
```

> **Implementer note (added 2026-07-31):** `MapCode` lost its unused `HttpStatusCode status` parameter — it never read it, and with the outcome split the status now has a dedicated home in `MapStatusOnly`. The nine-way mapping itself is **byte-identical** to what this plan originally specified; ADR-028 §5's table is unchanged.

- [ ] **Step 3: Update the GV-3 tests for the new signature**

> **Corrected 2026-07-31 — the snippet this step used to carry would not compile.** It referenced a `NeverCompletesHandler` class that **does not exist** in the file. The real helper is `BlockingHandler(Task<HttpResponseMessage> gate)` at `GvBridgeSendServiceTests.cs:88-95`, driven by a `TaskCompletionSource` the test releases at the end. Use it. (This was a defect in the original plan, not a GV-8 consequence.)

In `tests/Radio.Web.Tests/Services/GvBridgeSendServiceTests.cs`:

1. Every `svc.SendAsync("t1", "hi")` (`:37`, `:49`, `:60`) becomes `svc.SendAsync("+15551234567", "hi", "t1", "rc:test")`.
2. `InFlightGuard_RejectsSecondConcurrentSendOnSameThread` (`:63-82`) — its two calls must keep a shared `threadId` (`"t1"`) so they still collide on one flight key: `svc.SendAsync("+15551234567", "one", "t1", "rc:1")` / `("+15551234567", "two", "t1", "rc:2")`.
3. **`:81` must change type.** It currently asserts `HttpRequestException` on the released first send. Under the new implementation a `500` with an **empty** body parses to `null`, becomes `GvResult.HttpError(500)` with no `FailureBody`, and maps through `MapStatusOnly`'s `_ =>` arm — so the assertion becomes:

```csharp
    gate.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    await Assert.ThrowsAsync<SendFailedException>(() => first);
```

4. Add one new case, mirroring the existing `BlockingHandler` shape exactly:

```csharp
  [Fact]
  public async Task InFlightGuard_KeysOnRecipient_WhenThreadIdIsNull()
  {
    // New-conversation sends have no thread id; they must still single-flight per
    // recipient. Keying on "" instead would collapse every concurrent new-recipient
    // send into one slot.
    var gate = new TaskCompletionSource<HttpResponseMessage>();
    var client = new HttpClient(new BlockingHandler(gate.Task))
    {
      BaseAddress = new Uri("http://radio:5004")
    };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    var first = svc.SendAsync("555-123-4567", "one", null, "rc:1");   // takes the slot
    await Assert.ThrowsAsync<SendInFlightException>(
      () => svc.SendAsync("(555) 123-4567", "two", null, "rc:2"));    // same number, different formatting

    // Release the first send so the in-flight key is cleaned up in the finally.
    gate.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    await Assert.ThrowsAsync<SendFailedException>(() => first);
  }
```

5. Add the two outcome cases the `GvResult` adoption makes reachable (added 2026-07-31) — these are new *coverage*, not new *behaviour*:

```csharp
  [Fact]
  public async Task TransportFailure_MapsToSendFailed_NotARawHttpRequestException()
  {
    // Before GvResult adoption this escaped SendAsync unmapped and landed in the compose
    // UI's generic catch. It is a failure either way — but a TYPED one, like every other.
    var client = new HttpClient(new ThrowingHandler(new HttpRequestException("no route")))
    {
      BaseAddress = new Uri("http://radio:5004")
    };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    await Assert.ThrowsAsync<SendFailedException>(
      () => svc.SendAsync("+15551234567", "hi", "t1", "rc:1"));
  }

  [Fact]
  public async Task HttpClientTimeout_MapsToSendTimedOut_WhichIsAmbiguousByDesign()
  {
    // ADR-028 §5.3: no response observed, so the send may or may not have landed. The
    // caller must NOT auto-retry — it gets the ambiguous copy, not the generic failure.
    var client = new HttpClient(new ThrowingHandler(new TaskCanceledException()))
    {
      BaseAddress = new Uri("http://radio:5004")
    };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    await Assert.ThrowsAsync<SendTimedOutException>(
      () => svc.SendAsync("+15551234567", "hi", "t1", "rc:1"));
  }

  /// <summary>Handler that always throws, to exercise the transport / timeout outcomes.</summary>
  private sealed class ThrowingHandler(Exception toThrow) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
      => Task.FromException<HttpResponseMessage>(toThrow);
  }
```

> **Implementer note:** the timeout case relies on `SendAsync`'s `catch (OperationCanceledException)` arm being reached with `ct.IsCancellationRequested == false` — which holds because the test passes no token. `TaskCanceledException` derives from `OperationCanceledException`, so this is the same path `HttpClient.Timeout` takes.

- [ ] **Step 4: Verify**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeSendService"
```

---

## Chunk 3: PhoneHubService — subscribe to the echo channel

### Task 3: Add the SmsSent subscription

**Files:**
- Modify: `src/Radio.Web/Services/Hub/PhoneHubService.cs`

> ADR-028 §4.2. Outbound messages are broadcast on **`SmsSent`**, never on `SmsReceived`. We have never listened on it — which is why GV-3's optimistic de-dupe is dead code. Same `/hub` connection, same camelCase binding, no new connection.
>
> **Re-verified 2026-07-31: both anchors below are still exact.** GV-8 did not touch `PhoneHubService.cs`.

- [ ] **Step 1: Add the event declaration**

Next to the existing `GvSmsReceived` / `GvVoicemailReceived` declarations (around line 25):

```csharp
  /// <summary>
  /// Outbound SMS echo (ADR-028 §4.2). Fires for BOTH copies RotaryPhone sends us:
  /// (a) the immediate controller echo right after our POST — broadcast to Clients.All,
  ///     so we receive our own send back; and
  /// (b) the poller's later re-surface of the same message, which carries a DIFFERENT Id.
  /// Outbound NEVER arrives on GvSmsReceived, so this handler must never toast and must
  /// never mark anything unread. Reconcile via OutboundSmsReconciler.
  /// </summary>
  public event Action<Radio.Web.Models.SmsMessageDto>? GvSmsSent;
```

- [ ] **Step 2: Add the hub subscription**

Immediately after the existing `SmsReceived` registration (line 94):

```csharp
      // ADR-028 §4.2: outbound echo. Distinct from SmsReceived precisely so the UI can
      // append WITHOUT an inbound toast. Both the controller echo and the poller
      // re-surface arrive here.
      _hubConnection.On<Radio.Web.Models.SmsMessageDto>("SmsSent", m =>
      {
        _logger.LogDebug("GV SMS sent-echo on thread {ThreadId} id {Id}", m.ThreadId, m.Id);
        GvSmsSent?.Invoke(m);
      });
```

- [ ] **Step 3: Verify**

```bash
dotnet build src/Radio.Web --configuration Release
```

---

## Chunk 4: The outbound reconciler — THE KEY INVARIANT

### Task 4: OutboundSmsReconciler, keyed by exact Id then the fuzzy window

**Files:**
- Create: `src/Radio.Web/Services/OutboundSmsReconciler.cs`
- Test: `tests/Radio.Web.Tests/Services/OutboundSmsReconcilerTests.cs`

> ADR-028 §4.4 — the one correctness rule in this PR. Mirrors `ReadStateReconciler` (GV-4): static, mutates the caller's list, returns `bool` changed, unit-tested in isolation with no Blazor involved.

- [ ] **Step 1: Write the tests first (TDD)**

Create `tests/Radio.Web.Tests/Services/OutboundSmsReconcilerTests.cs`:

```csharp
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

public class OutboundSmsReconcilerTests
{
  private static readonly DateTime T0 = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

  private static SmsMessageDto Msg(string id, string threadId, string text,
    DateTime sentAt, string direction = "Outbound", string number = "+15551234567")
    => new(id, threadId, direction, number, text, sentAt, true);

  [Fact]
  public void ExactIdMatch_ReplacesInPlace_AndKeepsPosition()
  {
    var list = new List<SmsMessageDto>
    {
      Msg("a", "t.1", "first", T0.AddMinutes(-5), "Inbound"),
      Msg("rc:1", "t.1", "hello", T0),
      Msg("b", "t.1", "later", T0.AddSeconds(1), "Inbound"),
    };
    var echo = Msg("rc:1", "t.1", "hello", T0.AddMilliseconds(400));

    Assert.True(OutboundSmsReconciler.Apply(list, echo, 120));

    Assert.Equal(3, list.Count);                       // no append
    Assert.Equal("rc:1", list[1].Id);                  // still at index 1
    Assert.Equal(T0.AddMilliseconds(400), list[1].SentAt);  // replaced, not ignored
  }

  [Fact]
  public void PollerResurface_WithDifferentId_CollapsesOntoTheOptimisticBubble()
  {
    // The crux (ADR-028 §4.3): the poller's csid: id can NEVER equal the echo's id.
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "hello", T0) };
    var resurfaced = Msg("csid:t.1:ab12cd34ef56:1780000000000", "t.1", "hello", T0.AddSeconds(45));

    Assert.True(OutboundSmsReconciler.Apply(list, resurfaced, 120));

    Assert.Single(list);
    // Identity is PRESERVED as the client id so the status map never needs re-keying.
    Assert.Equal("rc:1", list[0].Id);
  }

  [Fact]
  public void FuzzyMatch_IgnoresThreadId_SoNewConversationSendsStillCollapse()
  {
    // New-recipient sends group the optimistic bubble under the raw number; the server
    // resolves a real thread id. A ThreadId-based match would miss.
    var list = new List<SmsMessageDto> { Msg("rc:1", "5551234567", "hello", T0) };
    var resurfaced = Msg("csid:t.+15551234567:aaaaaaaaaaaa:1", "t.+15551234567", "hello", T0.AddSeconds(20));

    Assert.True(OutboundSmsReconciler.Apply(list, resurfaced, 120));
    Assert.Single(list);
  }

  [Fact]
  public void CounterpartyMustMatch_ModuloFormatting()
  {
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "hello", T0, number: "(555) 123-4567") };
    var resurfaced = Msg("csid:x:y:1", "t.1", "hello", T0.AddSeconds(10), number: "+15551234567");

    Assert.True(OutboundSmsReconciler.Apply(list, resurfaced, 120));
    Assert.Single(list);
  }

  [Fact]
  public void DifferentCounterparty_DoesNotCollapse()
  {
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "hello", T0, number: "+15551234567") };
    var other = Msg("csid:x:y:1", "t.2", "hello", T0.AddSeconds(10), number: "+15559999999");

    Assert.True(OutboundSmsReconciler.Apply(list, other, 120));   // appended
    Assert.Equal(2, list.Count);
  }

  [Fact]
  public void OutsideTheWindow_IsTreatedAsADistinctMessage()
  {
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "hello", T0) };
    var late = Msg("csid:x:y:1", "t.1", "hello", T0.AddSeconds(121));

    Assert.True(OutboundSmsReconciler.Apply(list, late, 120));
    Assert.Equal(2, list.Count);
  }

  [Fact]
  public void TextComparisonIsOrdinal_AndCaseSensitive()
  {
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "Hello", T0) };
    var different = Msg("csid:x:y:1", "t.1", "hello", T0.AddSeconds(5));

    Assert.True(OutboundSmsReconciler.Apply(list, different, 120));
    Assert.Equal(2, list.Count);
  }

  [Fact]
  public void InboundMessage_IsNeverReconciledHere()
  {
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "hello", T0) };
    var inbound = Msg("g1", "t.1", "hello", T0.AddSeconds(5), direction: "Inbound");

    Assert.True(OutboundSmsReconciler.Apply(list, inbound, 120));
    Assert.Equal(2, list.Count);        // appended as a genuinely different message
  }

  [Fact]
  public void ApplyingTheSameEchoTwice_IsIdempotent()
  {
    // Both copies + the HTTP response can deliver the same message three times.
    var list = new List<SmsMessageDto> { Msg("rc:1", "t.1", "hello", T0) };
    var echo = Msg("rc:1", "t.1", "hello", T0.AddMilliseconds(200));

    OutboundSmsReconciler.Apply(list, echo, 120);
    OutboundSmsReconciler.Apply(list, echo, 120);
    OutboundSmsReconciler.Apply(list, echo, 120);

    Assert.Single(list);
  }

  [Fact]
  public void NullText_DoesNotThrow_AndMatchesOtherNullText()
  {
    var list = new List<SmsMessageDto>
    {
      new("rc:1", "t.1", "Outbound", "+15551234567", null, T0, true)
    };
    var echo = new SmsMessageDto("csid:x:y:1", "t.1", "Outbound", "+15551234567", null, T0.AddSeconds(3), true);

    Assert.True(OutboundSmsReconciler.Apply(list, echo, 120));
    Assert.Single(list);
  }

  [Fact]
  public void EmptyList_AppendsWithoutThrowing()
  {
    var list = new List<SmsMessageDto>();
    Assert.True(OutboundSmsReconciler.Apply(list, Msg("rc:1", "t.1", "hi", T0), 120));
    Assert.Single(list);
  }
}
```

- [ ] **Step 2: Implement the reconciler**

Create `src/Radio.Web/Services/OutboundSmsReconciler.cs`:

```csharp
using Radio.Core.Utilities;
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// The ADR-028 §4.4 invariant: outbound reconciliation is idempotent, keyed FIRST by exact
/// <c>Id</c>, then by <c>(Outbound, normalized counterparty, ordinal text, |ΔSentAt| ≤ window)</c>.
///
/// Why two tiers: RotaryPhone delivers our own outbound message up to three times — the HTTP
/// response body, the immediate <c>SmsSent</c> controller echo (broadcast to Clients.All, so the
/// sender gets its own send back), and the thread poller's later re-surface. The poller's copy
/// carries a DIFFERENT id by construction: it recomputes <c>csid:{threadId}:{sha1(text)[..12]}:{epoch}</c>
/// from Google's data, so its epoch differs from the controller's send-time stamp, its thread id
/// may be Google's real id rather than the synthesized one, and supplying a ClientCorrelationId
/// guarantees divergence outright. RotaryPhone's own source calls the fuzzy match
/// "REQUIRED, not optional" (GvThreadPoller.cs:164-169).
///
/// On a match the existing entry is REPLACED IN PLACE — never removed-and-appended — so the
/// bubble keeps its list position (no visual jump) and its client-side Id (so the caller's
/// send-status map never needs re-keying).
///
/// Deliberately EXCLUDED from the key: IsRead (the controller echo hardcodes true while the
/// poller copy carries Google's value) and exact SentAt equality (the two copies disagree by
/// design). ThreadId is also excluded — a new-conversation send groups its optimistic bubble
/// under the raw recipient number while the server resolves a real thread id.
///
/// Accepted residual risk (ADR-028 §4.4): two genuinely distinct sends of identical text to the
/// same counterparty inside the window collapse into one bubble. Better than a permanently
/// duplicated bubble, and it matches the window RotaryPhone documents.
/// </summary>
public static class OutboundSmsReconciler
{
  /// <summary>
  /// Reconcile <paramref name="incoming"/> into <paramref name="messages"/>. Returns true iff
  /// the list changed (either a replacement or an append) — callers use it to gate re-render.
  /// </summary>
  /// <param name="messages">The open thread's message list. Mutated in place.</param>
  /// <param name="incoming">The echoed or re-surfaced message.</param>
  /// <param name="windowSeconds">The cross-service de-dupe window (ADR-028 §4.4; default 120).</param>
  public static bool Apply(List<SmsMessageDto> messages, SmsMessageDto incoming, int windowSeconds)
  {
    // Tier 1: exact Id. Catches the immediate echo (which carries our ClientCorrelationId
    // verbatim) and any repeat delivery of the same copy.
    var idx = messages.FindIndex(m => string.Equals(m.Id, incoming.Id, StringComparison.Ordinal));

    // Tier 2: the belt-and-suspenders match, outbound only.
    if (idx < 0 && GvDirection.IsOutbound(incoming.Direction))
    {
      var incomingNumber = PhoneNumberNormalizer.Normalize(incoming.CounterpartyNumber);
      idx = messages.FindIndex(m =>
        GvDirection.IsOutbound(m.Direction)
        && string.Equals(m.Text, incoming.Text, StringComparison.Ordinal)
        && string.Equals(PhoneNumberNormalizer.Normalize(m.CounterpartyNumber), incomingNumber,
             StringComparison.Ordinal)
        && Math.Abs((m.SentAt - incoming.SentAt).TotalSeconds) <= windowSeconds);
    }

    if (idx < 0)
    {
      messages.Add(incoming);
      return true;
    }

    // Replace in place, PRESERVING the existing Id. Keeping our client id stable is what
    // lets PhoneTextsPanel's status map (keyed by Id) survive reconciliation untouched.
    messages[idx] = incoming with { Id = messages[idx].Id };
    return true;
  }
}
```

- [ ] **Step 3: Verify**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~OutboundSmsReconciler"
```

All 12 tests must pass before continuing.

---

## Chunk 5: PhonePage — wire the echo, delete the dead code, fix the thread-identity bug

### Task 5: Subscribe GvSmsSent; remove the unreachable de-dupe; harden BumpThread

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor`

> ADR-028 §4.2 / §6. Three changes: (1) subscribe to the channel outbound actually arrives on; (2) delete the `temp-` de-dupe in `OnGvSmsReceived`, which can never fire because outbound never arrives on `SmsReceived`; (3) fix the duplicate-thread-row bug the new-conversation flow exposes.
>
> **⚠ Line numbers re-sited 2026-07-31.** GV-8 inserted **+109 lines** into this file, all **above** line 686 (`LoadOpenThreadMessagesAsync`, `RetryOpenThreadAsync`, `RestoreThreadUnreadAsync`, `MarkOpenThreadReadAsync`, plus the `_openThreadLoading`/`_openThreadError` fields and three new parameters on the `PhoneMessagesPanel` mount). Every anchor below is the **verified current** line on `main` @ `1503a5c`. The *code* to change is unaltered — GV-8 did not touch `OnGvSmsReceived`, `BumpThread` or `OnOptimisticAppend`.

- [ ] **Step 1: Subscribe / unsubscribe**

Beside the existing `PhoneHub.GvSmsReceived += OnGvSmsReceived;` (**line 247**, was cited as ~238):

```csharp
    PhoneHub.GvSmsSent += OnGvSmsSent;
```

and beside the existing `PhoneHub.GvSmsReceived -= OnGvSmsReceived;` (**line 1093**, was cited as ~984):

```csharp
    PhoneHub.GvSmsSent -= OnGvSmsSent;
```

- [ ] **Step 2: Delete the unreachable de-dupe from `OnGvSmsReceived`**

In `OnGvSmsReceived` (**now at `:774-823`**), delete this entire block (**lines 781–797** — comment `781-788` plus the `if` at `789-797`; was cited as ~672–688) along with its comment:

```csharp
      // Optimistic→confirmed de-dupe: [...full comment abridged here — delete the whole
      // comment block down to and including the closing brace below...]
      if (GvDirection.IsOutbound(msg.Direction) && _openThreadMessages != null)
      {
        var tempIdx = _openThreadMessages.FindIndex(m =>
          m.Id.StartsWith("temp-", StringComparison.Ordinal)
          && GvDirection.IsOutbound(m.Direction)
          && string.Equals(m.Text, msg.Text, StringComparison.Ordinal)
          && Math.Abs((m.SentAt - msg.SentAt).TotalSeconds) <= 30);
        if (tempIdx >= 0) _openThreadMessages.RemoveAt(tempIdx);
      }
```

Replace it with a single orienting comment so the next reader does not re-add it:

```csharp
      // NOTE (ADR-028 §4.2): outbound NEVER arrives here — RotaryPhone routes it to the
      // distinct "SmsSent" event precisely so we can append it without an inbound toast.
      // Outbound reconciliation lives in OnGvSmsSent / OutboundSmsReconciler.
```

- [ ] **Step 3: Add the `OnGvSmsSent` handler**

Immediately after `OnGvSmsReceived` (**after line 823**, was cited as ~714):

```csharp
  // Outbound echo (ADR-028 §4.2). Arrives up to twice per send: the immediate controller
  // echo (Clients.All — we receive our own) and the poller's later re-surface with a
  // DIFFERENT id. NEVER toast, NEVER mark unread, NEVER touch the unread badge: the user
  // sent this message themselves. Runs on the SignalR thread → marshal onto the Blazor
  // sync context before touching component state, exactly like OnGvSmsReceived.
  private void OnGvSmsSent(SmsMessageDto msg)
  {
    if (_disposed) return;
    _ = InvokeAsync(() =>
    {
      if (_disposed) return;

      // Only reconcile into the open conversation — the optimistic bubble only ever lives
      // there. When the thread is not open there is nothing to collapse; just refresh the
      // thread row so the preview and ordering stay honest.
      var threadOpen = _openThreadId != null
                       && (_messagesPanel?.IsThreadOpen(msg.ThreadId) ?? false);

      if (threadOpen && _openThreadMessages != null)
      {
        OutboundSmsReconciler.Apply(_openThreadMessages, msg, SendDedupeWindowSeconds);
      }

      BumpThread(msg, markUnread: false);      // our own send never marks unread
      PhoneUnread.Set(UnreadSum);
      StateHasChanged();
    });
  }

  // ADR-028 §4.4: the de-dupe window is a cross-service agreement with RotaryPhone's poll
  // cadence (15s active / 60s idle / 120s backoff), not a local tuning knob. Surfaced as
  // config so it can be adjusted without a rebuild if their cadence changes.
  private int SendDedupeWindowSeconds =>
    Configuration.GetValue("RotaryPhone:Gv:SendDedupeWindowSeconds", 120);
```

> **Implementer note:** if `PhonePage` does not already `@inject IConfiguration Configuration`, add it at the top of the file alongside the other injects.

- [ ] **Step 4: Fix the duplicate-thread-row bug in `BumpThread`**

ADR-028 §6: after a new-conversation send the response's `ThreadId` is RotaryPhone's *synthesized, explicitly-UNVERIFIED* `t.+<E164>`, while the poller later surfaces the same conversation under Google's **real** thread id. `BumpThread` matches on `ThreadId` alone and therefore inserts a second row for one conversation.

> **Re-verified 2026-07-31 — this bug STILL REPRODUCES.** `BumpThread` (**now `:828-849`**) is **absent from GV-8's diff**: it still matches on `ThreadId` alone, still has an unconditional insert-new `else`, and its update branch still does not carry the thread id forward. GV-8 changed the thread-*open* and read-state paths, and touched `_threads` only through `ReadStateReconciler.ApplyThread` (which mutates `HasUnread`, never membership), so neither the trigger nor the mechanism moved. It remains **derived from RotaryPhone's source (ADR-028 §6), not observed live** — reproducing it needs `SendEnabled=true` on both sides.

Replace the `FindIndex` on **line 831** (was cited as ~722):

```csharp
    var idx = _threads.FindIndex(t => t.ThreadId == msg.ThreadId);
```

with:

```csharp
    // Prefer the exact thread id; fall back to the normalized counterparty number.
    // A new conversation can legitimately arrive under two different ids — the
    // synthesized "t.+<E164>" the send resolved, then Google's real id from the poller
    // (ADR-028 §6) — and matching on id alone would leave a duplicate row behind.
    var idx = _threads.FindIndex(t => t.ThreadId == msg.ThreadId);
    if (idx < 0)
    {
      var key = PhoneNumberNormalizer.Normalize(msg.CounterpartyNumber);
      idx = _threads.FindIndex(t =>
        PhoneNumberNormalizer.Normalize(t.CounterpartyNumber) == key);
    }
```

and in the update branch, carry the newest thread id forward so subsequent replies target the real one:

```csharp
      var t = existing with
      {
        ThreadId = msg.ThreadId,          // adopt the server's resolved/real id
        HasUnread = markUnread || existing.HasUnread,
        LastMessageAt = msg.SentAt,
        LastMessagePreview = msg.Text
      };
```

- [ ] **Step 4b: Keep `_openThreadId` in step when the row adopts a new id (added 2026-07-31)**

> **Why this is new.** Adopting `msg.ThreadId` onto an existing row means a row's identity can now **change under an open conversation**. Before GV-8 the only consequence was one missed read-flip. GV-8 added **three** more sites keyed on that id — `LoadOpenThreadMessagesAsync`'s stale-response guard (`if (_openThreadId != threadId)`, `:709`), `RestoreThreadUnreadAsync(threadId, wasUnread)`, and `RetryOpenThreadAsync` (reads `_openThreadId`, `:736`) — and all three fail **silently** against a stale id: `ReadStateReconciler.ApplyThread` simply finds nothing and no-ops, so a failed open would leave the unread marker un-restored and Retry would re-fetch under an id the row no longer has.

Immediately after the `existing with { … }` assignment above, before the `RemoveAt`/`Insert`:

```csharp
      // The row's identity just changed (synthesized "t.+<E164>" → Google's real id). If
      // this is the conversation currently on screen, follow it — GV-8 keys the stale-fetch
      // guard, the unread restore and Retry off _openThreadId, and every one of them fails
      // SILENTLY (a no-op reconcile) against an id no row carries any more.
      if (_openThreadId == existing.ThreadId && existing.ThreadId != msg.ThreadId)
      {
        _openThreadId = msg.ThreadId;
      }
```

- [ ] **Step 5: Verify**

```bash
dotnet build src/Radio.Web --configuration Release
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhonePage"
```

---

## Chunk 6: PhoneTextsPanel — the send call site and the UI mapping

### Task 6: Real arguments, rc: ids, per-code copy, cooldown, terminal codes

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor`
- Modify: `src/Radio.Web/Components/Pages/MessageBubble.razor`

> ADR-028 §3 / §5 + the handoff's send-failure copy matrix. This is where the taxonomy becomes user-visible.
>
> **⚠ Line numbers re-sited 2026-07-31.** GV-8 added **+51 lines** to `PhoneTextsPanel.razor` — the conversation pane went from three branches to **four** (skeleton → error → empty → list, `:43-98`), and `Loading`/`Error`/`OnRetry` are now actually fed from `PhonePage` → `PhoneMessagesPanel`. Net effect on this chunk: **every `@code` anchor shifts +47.** The code being changed is untouched — GV-8 rewrote the *markup* branches, not `SendDraftAsync`, `RetrySend`, `CanSend` or `ComposeBar()`. `MessageBubble.razor` was not touched at all.

- [ ] **Step 1: Add the `Retryable` parameter to `MessageBubble`**

> **Made literal 2026-07-31.** This was the plan's only prose-only step ("wherever the failed bubble currently wires `OnRetry`…"), which is exactly the kind of instruction that drifts. `MessageBubble.razor` is 56 lines and **unchanged** by GV-8, so the edits are given exactly.

In `MessageBubble.razor`, beside the existing parameters (**line 38**, `OnRetry`):

```csharp
  /// <summary>
  /// False for TERMINAL failures (invalid_number / invalid_text, ADR-028 §5.4) — retrying
  /// the same input cannot succeed, so no Retry target is rendered and the bubble is not a
  /// tap target. The user fixes the input instead.
  /// </summary>
  [Parameter] public bool Retryable { get; set; } = true;
```

Add the derived flag beside `IsFailed` (**line 41**):

```csharp
  private bool IsFailed => Status == SendStatus.Failed;
  // A failed bubble is only a TAP TARGET when retrying it could actually help (§5.4).
  private bool IsRetryTarget => IsFailed && Retryable;
```

Fold it into `StatusClass` (**lines 43-48**) so the terminal case gets its own hook:

```csharp
  private string StatusClass => Status switch
  {
    SendStatus.Sending => "sending",
    SendStatus.Failed => Retryable ? "failed" : "failed failed-terminal",
    _ => ""
  };
```

Retarget the root element's two affordance bindings (**lines 8-9**) from `IsFailed` to `IsRetryTarget`:

```razor
<div class="msg-bubble @DirectionClass @StatusClass"
     @onclick="OnFailedClick" role="@(IsRetryTarget ? "button" : null)">
```

and the click guard (**line 54**):

```csharp
  private async Task OnFailedClick()
  {
    if (IsRetryTarget) await OnRetry.InvokeAsync();
  }
```

Finally, in `design-system.css`, beside the existing `.msg-bubble.failed` rule, add the terminal variant — it keeps the failed *colouring* but drops the affordance, since the whole point is that there is nothing to tap:

```css
/* ADR-028 §5.4: invalid_number / invalid_text cannot succeed on retry, so the bubble
   is not a tap target — no pointer cursor, no 48px touch-target inflation. */
.msg-bubble.failed.failed-terminal { cursor: default; min-height: 0; }
```

> **Implementer note:** confirm the exact declarations `.msg-bubble.failed` sets (it is documented in `MessageBubble.razor:6` as "≥48px via `.msg-bubble.failed`") and neutralize *those*, rather than assuming the two above. The rule is a targeted override, not a redefinition.

- [ ] **Step 2: Add the new state fields**

Beside the existing fields (**line 234**, was cited as ~187):

```csharp
  // ADR-028 §5.2: their 429 carries no Retry-After, and their window is 5 sends per 10s.
  // Without a cooldown the user just re-burns tokens and stays rate-limited. Soft coupling
  // to GVBridge:SmsSendWindowSeconds (default 10) — a UX guard, not a correctness mechanism.
  private const int RateLimitCooldownSeconds = 10;
  private DateTime? _cooldownUntil;

  // Ids whose failure is TERMINAL (no Retry) — ADR-028 §5.4.
  private readonly HashSet<string> _terminalFailures = new();

  // Set when the SERVER reports send is dark (409 send_disabled, ADR-028 §5.1). Distinct
  // from our own SendEnabled flag and from the degraded gate, but renders the same calm
  // "Texting unavailable" affordance — because in all three cases nothing was sent.
  private bool _serverSendDark;
```

- [ ] **Step 3: Gate `CanSend` on the cooldown and the dark flag**

Replace `CanSend` (**lines 244-247**, was cited as ~197):

```csharp
  private bool CanSend =>
    SendService.SendEnabled && GvStatus.IsAvailable && !_serverSendDark
    && !InCooldown
    && !string.IsNullOrWhiteSpace(_draft) && _sending.Count == 0
    && (!_composingNew || !string.IsNullOrWhiteSpace(_recipient));

  private bool InCooldown => _cooldownUntil is { } until && DateTime.UtcNow < until;
```

- [ ] **Step 4: Show the unavailable affordance when the server is dark**

In `ComposeBar()`, change the guard on **line 260** (was cited as 213) from `@if (GvStatus.IsAvailable)` to:

```razor
    @if (GvStatus.IsAvailable && !_serverSendDark)
```

The existing `else` branch already renders the handoff's `Texting unavailable` amber pill, so the server-dark case reuses it verbatim — no new markup.

> **Note (2026-07-31):** two more branches land on this same `ComposeBar()` — `!ThreadIsRepliable` in **Chunk 6b Step 4** and the failed-load gate in **Chunk 6b Step 5**. Their **precedence is specified once, in Chunk 6b Step 5**. Write this step as given, then let 6b insert both ahead of it; do not try to merge the three guards into one expression.

- [ ] **Step 5: Rewrite `SendDraftAsync`**

Replace the whole of `SendDraftAsync` (**lines 325–394**, was cited as 278–347):

```csharp
  private async Task SendDraftAsync()
  {
    if (!CanSend) return;

    // Resolve the two independent fields the contract needs (ADR-028 §3). toNumber is the
    // RECIPIENT and is never a thread id; threadId is null when starting a conversation.
    string toNumber;
    string? threadId;
    string groupingKey;          // local-only: which bubble list the optimistic message joins

    if (_composingNew)
    {
      // Light validation only — RotaryPhone owns E.164 normalization and will return a
      // coded invalid_number if it disagrees. Block only the obviously-empty case.
      var normalized = PhoneNumberNormalizer.Normalize(_recipient);
      if (normalized.Length < 7)
      {
        _recipientError = "Enter a valid phone number.";
        return;
      }
      toNumber = _recipient;     // raw, as typed — the server normalizes
      threadId = null;           // no thread yet; the server resolves/creates one
      groupingKey = _recipient;
    }
    else
    {
      toNumber = HeaderNumber ?? "";
      threadId = OpenThreadId;
      groupingKey = OpenThreadId ?? "";
    }

    var text = _draft;

    // "rc:" not "temp-": this id is sent as ClientCorrelationId, comes back as the server's
    // Message.Id, and is what OTHER connected clients see. It is not temporary (ADR-028 §3).
    var clientId = $"rc:{Guid.NewGuid():N}";
    var optimistic = new SmsMessageDto(clientId, groupingKey,
      GvDirection.Outbound, toNumber, text, DateTime.UtcNow, true);

    _statusById[clientId] = MessageBubble.SendStatus.Sending;
    _sending.Add(clientId);
    _draft = "";                                    // clear input
    await OnOptimisticAppend.InvokeAsync(optimistic);
    await ScrollToBottomAsync();

    try
    {
      var created = await SendService.SendAsync(toNumber, text, threadId, clientId);

      // Queued == true → Sent. NOT delivered (ADR-028 §4.1); no fourth state. Because we
      // supplied ClientCorrelationId, created.Id == clientId, so the status key is stable
      // and the reconciler's in-place replace keeps it that way.
      _statusById[clientId] = MessageBubble.SendStatus.Sent;
      if (_composingNew) _composingNew = false;
    }
    catch (SendDisabledException)
    {
      // ADR-028 §5.1 — NOT a failure. No GV call was made; nothing was sent. Remove the
      // optimistic bubble entirely, restore the text, and show the unavailable affordance.
      _statusById.Remove(clientId);
      await OnOptimisticRemove.InvokeAsync(clientId);
      _draft = text;
      _serverSendDark = true;
      Notifications.Notify(NotificationSeverity.Info, "Texting unavailable",
        "Texting isn't switched on yet.");
    }
    catch (SendNotAvailableException)
    {
      // Our own flag is off — compose should not have been reachable. Same not-a-failure
      // treatment as the server-dark case.
      _statusById.Remove(clientId);
      await OnOptimisticRemove.InvokeAsync(clientId);
      _draft = text;
      Notifications.Notify(NotificationSeverity.Info, "Coming soon",
        "Texting send isn't available yet.");
    }
    catch (SendRateLimitedException)
    {
      _statusById[clientId] = MessageBubble.SendStatus.Failed;
      _draft = text;                                // PRESERVE
      _cooldownUntil = DateTime.UtcNow.AddSeconds(RateLimitCooldownSeconds);
      Notifications.Notify(NotificationSeverity.Error, "Slow down",
        "Sending too fast — wait a moment.");
    }
    catch (SendRejectedException ex)
    {
      // TERMINAL (ADR-028 §5.4): the same input cannot succeed. No Retry; point the user
      // at the field that needs fixing.
      _statusById[clientId] = MessageBubble.SendStatus.Failed;
      _terminalFailures.Add(clientId);
      _draft = text;
      if (ex.Code == GvSendCode.InvalidNumber)
      {
        if (_composingNew) _recipientError = "That number doesn't look right.";
        Notifications.Notify(NotificationSeverity.Error, "Message not sent",
          "That number doesn't look right. Check it and try again.");
      }
      else
      {
        Notifications.Notify(NotificationSeverity.Error, "Message not sent",
          "Couldn't send your message. Try again.");
      }
    }
    catch (SendUnavailableException)
    {
      // Our degraded gate OR the server's auth_unavailable (GV cookie decay). Retryable.
      _statusById[clientId] = MessageBubble.SendStatus.Failed;
      _draft = text;
      Notifications.Notify(NotificationSeverity.Error, "Message not sent",
        "Couldn't send — Google Voice needs to reconnect. Try again shortly.");
    }
    catch (SendTimedOutException)
    {
      // AMBIGUOUS (ADR-028 §5.3): may or may not have landed. Never auto-retry. If it DID
      // land, the poller re-surfaces it and OutboundSmsReconciler collapses it onto this
      // very bubble.
      _statusById[clientId] = MessageBubble.SendStatus.Failed;
      _draft = text;
      Notifications.Notify(NotificationSeverity.Error, "Message not sent",
        "No response — check the connection and try again.");
    }
    catch (Exception)                               // SendFailed / in-flight / network
    {
      _statusById[clientId] = MessageBubble.SendStatus.Failed;
      _draft = text;                                // PRESERVE; never auto-retry
      Notifications.Notify(NotificationSeverity.Error, "Message not sent",
        "Couldn't send your message. Try again.");
    }
    finally
    {
      _sending.Remove(clientId);
      await InvokeAsync(StateHasChanged);
    }
  }
```

- [ ] **Step 6: Add the optimistic-remove callback and fix `RetrySend`**

Beside the existing `OnOptimisticAppend` parameter (**line 232**, was cited as ~185):

```csharp
  /// <summary>
  /// Remove an optimistic bubble the parent appended. Used when the send was never
  /// attempted (ADR-028 §5.1: server-dark / our-flag-off) so the UI does not claim a
  /// message exists that was never sent. Also clears the orphan on a retry.
  /// </summary>
  [Parameter] public EventCallback<string> OnOptimisticRemove { get; set; }
```

Replace `RetrySend` (**lines 396–404**, was cited as 349–357), which currently leaves an orphan bubble behind — the `TODO(send-ship)` marker GV-3 left at `:401`:

```csharp
  private async Task RetrySend(SmsMessageDto m)
  {
    // Terminal failures offer no Retry target, so this should be unreachable for them —
    // guard anyway rather than re-issuing a send that cannot succeed.
    if (_terminalFailures.Contains(m.Id)) return;

    // Drop the failed bubble AND its status so the old red bubble does not linger beside
    // the new sending one (resolves GV-3's TODO(send-ship) orphan).
    _statusById.Remove(m.Id);
    await OnOptimisticRemove.InvokeAsync(m.Id);

    _draft = m.Text ?? "";                          // re-arm with the failed text
    await SendDraftAsync();
  }
```

Update the retry status lookup so terminal failures render no Retry. There is exactly **one** `MessageBubble` instantiation, in the conversation pane's list branch (**`PhoneTextsPanel.razor:94-96`**) — note the loop variable is `captured`, not `m`:

```razor
          <MessageBubble Message="captured"
                         Status="StatusFor(captured)"
                         Retryable="@(!_terminalFailures.Contains(captured.Id))"
                         OnRetry="@(() => RetrySend(captured))" />
```

- [ ] **Step 7: Wire `OnOptimisticRemove` in the parents**

In `PhoneMessagesPanel.razor`, beside `OnOptimisticAppend="OnOptimisticAppend"` (**line 194**, was cited as 191):

```razor
                         OnOptimisticRemove="OnOptimisticRemove"
```

plus the matching `[Parameter] public EventCallback<string> OnOptimisticRemove { get; set; }` beside the `OnOptimisticAppend` parameter at **line 280** (was cited as 268).

In `PhonePage.razor`, beside `OnOptimisticAppend="OnOptimisticAppend"` (**line 87**, was cited as 84) add `OnOptimisticRemove="OnOptimisticRemove"`, and add the handler beside `OnOptimisticAppend` (**after line 867**, was cited as after 758):

```csharp
  // Remove an optimistic bubble whose send was never attempted (ADR-028 §5.1) or which is
  // being retried. Keyed on the client id, which is stable across reconciliation.
  private void OnOptimisticRemove(string clientId)
  {
    if (_disposed) return;
    _ = InvokeAsync(() =>
    {
      if (_disposed || _openThreadMessages == null) return;
      var idx = _openThreadMessages.FindIndex(m =>
        string.Equals(m.Id, clientId, StringComparison.Ordinal));
      if (idx >= 0) _openThreadMessages.RemoveAt(idx);
      StateHasChanged();
    });
  }
```

- [ ] **Step 8: Verify**

```bash
dotnet build src/Radio.Web --configuration Release
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhoneTextsPanel|FullyQualifiedName~MessageBubble"
```

---

## Chunk 6b: Thread reply-ability (added 2026-07-31)

### Task 6b: Classify the counterparty and gate compose before the POST

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs` (add `GvCounterpartyKind` + `GvCounterparty`, beside `GvDirection`)
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` (add `SendNotRepliableException` + the pre-flight gate)
- Modify: `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` (disable compose + explanation)
- Test: `tests/Radio.Web.Tests/Services/GvCounterpartyTests.cs` (new)

> ADR-028 §8. **~A third of inbound SMS is from senders that cannot be replied to** — numeric short codes and opaque 36-char sender IDs, not E.164. Sending anyway yields their `400 invalid_number` → a red failed bubble reading *"That number doesn't look right"*, which lies twice: the user did not type a number, and the thread is **structurally** un-repliable. Identical in shape to the `send_disabled` problem §5.1 already solved — **a send that cannot succeed must never render as a send that failed.** Classification is client-side by design (§8.3): it is a pure function of a field we already receive, and adding a DTO field would make it a cross-service contract change.

- [ ] **Step 1: Write the classifier tests first (TDD)**

Create `tests/Radio.Web.Tests/Services/GvCounterpartyTests.cs`:

```csharp
using Radio.Web.Models;

namespace Radio.Web.Tests.Services;

public class GvCounterpartyTests
{
  [Theory]
  [InlineData("+15551234567")]
  [InlineData("5551234567")]
  [InlineData("(555) 123-4567")]
  [InlineData("+1 555 123 4567")]
  public void E164AndCommonFormats_AreRepliable(string value)
  {
    Assert.Equal(GvCounterpartyKind.PhoneNumber, GvCounterparty.Classify(value));
    Assert.True(GvCounterparty.CanReply(value));
  }

  [Theory]
  [InlineData("262966")]      // 6-digit short code
  [InlineData("22395")]       // 5-digit
  [InlineData("4321")]        // 4-digit
  [InlineData("911")]         // 3-digit
  public void NumericShortCodes_AreNotRepliable(string value)
  {
    Assert.Equal(GvCounterpartyKind.ShortCode, GvCounterparty.Classify(value));
    Assert.False(GvCounterparty.CanReply(value));
  }

  [Theory]
  [InlineData("A1B2C3D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8")]   // 36-char opaque
  [InlineData("VERIFY")]                                  // alphabetic sender id
  [InlineData("My-Bank")]
  public void OpaqueSenderIds_AreNotRepliable(string value)
  {
    Assert.Equal(GvCounterpartyKind.OpaqueSenderId, GvCounterparty.Classify(value));
    Assert.False(GvCounterparty.CanReply(value));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void NullOrEmpty_IsNotRepliable_AndDoesNotThrow(string? value)
  {
    // Defensive + total: bias toward the RECOVERABLE error (§8.3). Wrongly disabling
    // reply is a visible, reportable disabled composer; wrongly enabling it produces
    // the failed-send lie this task exists to prevent.
    Assert.False(GvCounterparty.CanReply(value));
  }
}
```

- [ ] **Step 2: Implement the classifier**

In `src/Radio.Web/Models/ApiModels.cs`, immediately after the `GvDirection` class (it is the in-tree precedent this mirrors — a client-side, defensive, total classifier over a raw DTO string):

```csharp
/// <summary>Counterparty kind (ADR-028 §8). Only PhoneNumber is repliable.</summary>
public enum GvCounterpartyKind { PhoneNumber, ShortCode, OpaqueSenderId }

/// <summary>
/// Classifies an SMS counterparty identifier (ADR-028 §8.3). Roughly a third of inbound
/// GV SMS comes from senders that are NOT dialable — numeric short codes and opaque
/// sender IDs — and replying to them is structurally impossible, not merely likely to fail.
///
/// Client-side by design: this is a pure function of a field we already receive, so adding
/// a DTO field would turn it into a cross-service contract change for no gain.
///
/// Defensive and TOTAL: every input returns a kind, null/empty included; nothing throws.
/// Anything not confidently a dialable number is treated as NOT repliable.
/// </summary>
public static class GvCounterparty
{
  public static GvCounterpartyKind Classify(string? counterpartyNumber)
  {
    if (string.IsNullOrWhiteSpace(counterpartyNumber))
    {
      return GvCounterpartyKind.OpaqueSenderId;   // unknown → not repliable
    }

    // Any non-digit (beyond the formatting punctuation a real number carries) means this
    // is not a dialable number — e.g. an alphabetic or 36-char opaque sender id.
    var hasLetter = counterpartyNumber.Any(char.IsLetter);
    if (hasLetter)
    {
      return GvCounterpartyKind.OpaqueSenderId;
    }

    // Reuse the same normalization the rest of the phone surface uses, so "(555) 123-4567"
    // and "+15551234567" classify identically.
    var digits = PhoneNumberNormalizer.Normalize(counterpartyNumber);

    // NANP national number is 10 digits after normalization. Anything shorter that is
    // purely numeric is a short code (3-8 digits in practice); anything longer is not
    // something we can hand to their E.164 normalizer with confidence.
    return digits.Length == 10
      ? GvCounterpartyKind.PhoneNumber
      : GvCounterpartyKind.ShortCode;
  }

  /// <summary>True only when the thread can actually be replied to.</summary>
  public static bool CanReply(string? counterpartyNumber)
    => Classify(counterpartyNumber) == GvCounterpartyKind.PhoneNumber;
}
```

> **Implementer note:** `ApiModels.cs` will need `using Radio.Core.Utilities;` for `PhoneNumberNormalizer` if it is not already present. If a non-NANP international number ever needs to be repliable, this is the single place to widen — do not scatter the rule.

- [ ] **Step 3: Add the service-level gate (defense in depth)**

In `GvBridgeSendService.cs`, add beside the other typed exceptions:

```csharp
/// <summary>
/// The thread's counterparty is not a dialable number (short code / opaque sender id) —
/// ADR-028 §8. Structurally un-repliable: no optimistic bubble, no failed bubble, no retry.
/// </summary>
public class SendNotRepliableException : Exception
{
  public SendNotRepliableException()
    : base("You can't reply to this sender.") { }
}
```

and gate **first** in `SendAsync`, ahead of the flag check — a send that is impossible should short-circuit before anything else is considered:

```csharp
    // ADR-028 §8.4: refuse before the POST. Their server would return 400 invalid_number,
    // which the UI would render as "That number doesn't look right" — a lie on both counts.
    if (!GvCounterparty.CanReply(toNumber)) throw new SendNotRepliableException();
    if (!SendEnabled) throw new SendNotAvailableException();
```

- [ ] **Step 4: Gate the composer in `PhoneTextsPanel`**

Add a computed guard beside `CanSend`:

```csharp
  // ADR-028 §8.5: reply-ability is a property of the OPEN THREAD's counterparty. In
  // new-recipient mode the user types the number, so the recipient-validation path owns it.
  private bool ThreadIsRepliable =>
    _composingNew || GvCounterparty.CanReply(HeaderNumber);
```

Fold it into `CanSend`:

```csharp
  private bool CanSend =>
    SendService.SendEnabled && GvStatus.IsAvailable && !_serverSendDark
    && ThreadIsRepliable
    && !InCooldown
    && !string.IsNullOrWhiteSpace(_draft) && _sending.Count == 0
    && (!_composingNew || !string.IsNullOrWhiteSpace(_recipient));
```

and give `ComposeBar()` a dedicated branch **before** the degraded branch, so the more specific reason wins:

```razor
    @if (!ThreadIsRepliable)
    {
      <div class="texts-compose">
        <span class="phone-pill" title="This sender is a short code or automated ID.">You can't reply to this sender.</span>
      </div>
    }
    else if (GvStatus.IsAvailable && !_serverSendDark)
    {
```

> Reuses the existing `.phone-pill` + `.texts-compose` shapes — no new CSS. Do **not** hide the composer: the handoff's degraded-state reasoning ("don't let the user type into a dead send path") applies, and an absent composer reads as a rendering bug where a disabled one reads as an answer.
>
> **Note (2026-07-31):** `HeaderNumber` is fed from the **thread row**, not from the message bodies, so this gate keeps working even when the conversation's bodies failed to load. That matters for Step 5.

- [ ] **Step 5: Gate the composer on a FAILED conversation load (added 2026-07-31)**

> ### 🔵 DECISION — needs the owner's blessing before Builder claims this row
>
> **The question, which is now GV-5's to answer.** GV-8 gave the conversation pane a **fourth** state — `cloud_off` + "Couldn't load messages." + `Retry` — but `@ComposeBar()` at `PhoneTextsPanel.razor:101` renders **unconditionally**, below whichever branch won. GV-8's UAT recorded this as **`O-2`**: under a confirmed 502 the pane shows the error state *and*, beneath it, a live-looking `Message` field with `Send`. It is inert today only because `SendEnabled=false` — and this PR is the one that makes send work. **GV-7 is explicitly forbidden from re-deciding composer behaviour, so the failed-load case is answered here.**
>
> **Recommendation: disable the composer with a stated reason. Do not hide it, do not leave it live.**
>
> **This is not a taste call — leaving it live is a demonstrable defect.** `PhonePage.OnOptimisticAppend` (`:856-867`) does `_openThreadMessages ??= new(); _openThreadMessages.Add(optimistic);`. On a failed load `_openThreadMessages` is `null` and `_openThreadError` is `true`. Send one message and `Messages` becomes non-null with `Count == 1`, so GV-8's M-1 guard (`Error && Messages == null`) goes false and the pane **falls through to the list branch** — the `cloud_off` error state silently vanishes and is replaced by a single outbound bubble that reads as *the entire conversation*. The user is now looking at a thread the app could not read, presented as one they can. Every branch behaves exactly as designed; the composite is a lie.
>
> **Two supporting reasons.** (1) It is the **same argument §5.1 and §8.5 already made twice** — *a send the app cannot honestly stand behind must not be presented as an ordinary send* — resolved the same way both times: disable, explain, don't hide. (2) Replying to a conversation whose last messages you were never shown is a real-world footgun on a **kiosk** surface, where the user cannot scroll back or open another client to check.
>
> **Rejected alternatives, recorded:** *Hide the composer* — the handoff's own reasoning kills it ("an absent composer reads as a rendering bug"), and the pane height would jump between error and loaded states. *Leave it enabled and fix only the append* (e.g. teach `OnOptimisticAppend` to preserve the error state) — this keeps the footgun and adds a state the pane has no design for ("failed to load, plus one message you just sent"); GV-7's row already calls the failed-load case "the weakest case for leaving it."
>
> **Scope note:** this is a fourth **reason** on an affordance this chunk is already rewriting — one computed property, one `else if`, no new CSS, no new parameter. It is not new scope.

Add the guard beside `ThreadIsRepliable`:

```csharp
  // ADR-028 §5.1 / §8.5, extended to GV-8's error state (UAT O-2). The conversation's
  // bodies failed to load, so we do not know what was last said in it. Inviting a reply
  // is bad on its own — but the concrete failure is that OnOptimisticAppend would make
  // Messages non-null, which flips the pane out of its `Error && Messages == null` branch
  // and renders the single sent bubble as if it were the whole conversation.
  // Predicate deliberately mirrors the pane's own error branch (:61) verbatim.
  private bool ConversationFailedToLoad =>
    OpenThreadId != null && !_composingNew && Error && Messages == null;
```

Fold it into `CanSend`, which now carries all four reasons:

```csharp
  private bool CanSend =>
    SendService.SendEnabled && GvStatus.IsAvailable && !_serverSendDark
    && ThreadIsRepliable
    && !ConversationFailedToLoad
    && !InCooldown
    && !string.IsNullOrWhiteSpace(_draft) && _sending.Count == 0
    && (!_composingNew || !string.IsNullOrWhiteSpace(_recipient));
```

and insert the branch into `ComposeBar()` **between** the reply-ability branch (Step 4) and the degraded/dark branch (Chunk 6 Step 4):

```razor
    @if (!ThreadIsRepliable)
    {
      <div class="texts-compose">
        <span class="phone-pill" title="This sender is a short code or automated ID.">You can't reply to this sender.</span>
      </div>
    }
    else if (ConversationFailedToLoad)
    {
      @* Deliberately does NOT repeat the pane's "Couldn't load messages." — that string is
         already on screen six lines up, with the Retry that fixes it. This says what the
         COMPOSER is doing and points at the path out. *@
      <div class="texts-compose">
        <span class="phone-pill amber" title="This conversation didn't load. Retry above.">Reply once this loads.</span>
      </div>
    }
    else if (GvStatus.IsAvailable && !_serverSendDark)
    {
```

**Precedence is specified here, once, for all four reasons — most permanent first:**

| Order | Condition | Why it outranks the next | Resolves when |
|---|---|---|---|
| 1 | `!ThreadIsRepliable` | **structural** — no action by anyone ever makes this thread repliable | never |
| 2 | `ConversationFailedToLoad` | scoped to **this** conversation; the user has a Retry right there | on Retry |
| 3 | `!GvStatus.IsAvailable \|\| _serverSendDark` | **global** to the surface; nothing the user does here helps | on reconnect / a config flip |
| 4 | — | normal composer | — |

This extends the rule the Test Plan already states at **§D2 / 31** ("on a short-code thread while GV is *also* degraded, the reply-ability message wins") rather than inventing a second one.

- [ ] **Step 6: Catch the new exception at the call site**

In `SendDraftAsync` (Chunk 6 Step 5), add ahead of the other catches (it should be unreachable given Step 4, which is the point):

```csharp
    catch (SendNotRepliableException)
    {
      // Unreachable if the composer gate works — kept so a future/bypassed path cannot
      // produce the misleading failed bubble. No bubble, no retry, text preserved.
      _statusById.Remove(clientId);
      await OnOptimisticRemove.InvokeAsync(clientId);
      _draft = text;
      Notifications.Notify(NotificationSeverity.Info, "Can't reply",
        "You can't reply to this sender.");
    }
```

- [ ] **Step 7: Verify**

Add a bUnit case for Step 5's gate alongside the existing `PhoneTextsPanelTests` (which GV-8 extended — see `:124`, `:141`, `:197` for the `Error` + `OpenThreadId` setup to copy):

```csharp
  [Fact]
  public void Composer_IsGated_WhenTheConversationFailedToLoad()
  {
    // GV-8 UAT O-2 + ADR-028 §5.1. The pane says it could not read this conversation;
    // the composer must not invite a reply into it. Asserts the PILL, not just a
    // disabled input — "disabled with no stated reason" is the F-3 defect one row over.
    var cut = RenderConversation(openThreadId: "t.1", messages: null, error: true);

    Assert.Contains("Reply once this loads.", cut.Markup);
    Assert.DoesNotContain("texts-compose-input", cut.Markup);
  }

  [Fact]
  public void Composer_IsNotGated_WhenTheConversationLoadedEmpty()
  {
    // The important negative control: a genuine empty thread is NOT a failed one, and
    // GV-8 exists precisely to keep those two apart. Do not let this gate blur them again.
    var cut = RenderConversation(openThreadId: "t.1", messages: [], error: false);

    Assert.DoesNotContain("Reply once this loads.", cut.Markup);
  }
```

> **Implementer note:** `RenderConversation` is illustrative — mirror whatever helper/`SetParametersAndRender` shape `PhoneTextsPanelTests` already uses rather than adding a second one. The second test must pass an **empty list**, not `null`, or it asserts nothing.

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvCounterparty|FullyQualifiedName~PhoneTextsPanel"
dotnet build src/Radio.Web --configuration Release
```

---

## Chunk 7: Config + documentation

### Task 7: Config key, ADR pointer, supersession banner, integration docs

**Files:**
- Modify: `src/Radio.Web/appsettings.json`, `design/DECISION-LOG.md`, `design/FUTURE-WORK.md`, `design/INTEGRATIONS.md`, `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`

- [ ] **Step 1: Add the de-dupe window key. `SendEnabled` STAYS `false`.**

In `src/Radio.Web/appsettings.json`, under `RotaryPhone:Gv`:

```json
        "SendEnabled": false,
        "SendDedupeWindowSeconds": 120
```

> **Do not flip `SendEnabled`.** Turning send on is a separate decision and requires RotaryPhone's `GVBridge:EnableSmsSend` to be flipped first. Remember deploy overwrites `appsettings.json` — per-machine flips go in `appsettings.Production.json`.

- [ ] **Step 2: Add the ADR-028 pointer to `design/DECISION-LOG.md`**

Above the `<!-- NEW ENTRIES GO ABOVE THIS LINE -->` marker:

```markdown
## ADR-028: GV SMS Send — real contract, error taxonomy, outbound echo de-dupe (supersedes ADR-022 D7)

**Date:** 2026-07-30
**Status:** Accepted
**Full ADR:** `design/decisions/2026-07-30-gv-sms-send-contract.md`

GV-3 shipped send against an anticipated contract. RotaryPhone's as-built endpoint diverges on four axes, and the worst one was not the logged fast-follow: our `SendSmsRequest(ThreadId, Text)` omits their required `ToNumber`, which binds `null` server-side and returns **`400 invalid_number` on every send** — send was non-functional, not merely mis-handled. Also adopted: the complete nine-code `Code` taxonomy (`invalid_text` was missing from the previously logged list of eight); a subscription to the **`SmsSent`** SignalR event, which we had never listened on and which is the only channel outbound messages arrive on (making GV-3's optimistic de-dupe unreachable dead code); and an idempotent reconciler keyed by exact `Id` then `(Outbound, counterparty, text, ≤120s)`, because the poller's re-surfaced copy carries a different `Id` by construction. `Queued: true` maps to the existing `Sent` state — no "delivered" state, because neither side can honestly assert delivery. `send_disabled` (409) is treated as an availability state, not a failed send. Ships behind `RotaryPhone:Gv:SendEnabled`, still default `false`.

---
```

- [ ] **Step 3: Add the supersession banner to ADR-022**

In `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md`, directly below the existing ADR-024 banner near the top:

```markdown
- **⚠ Partially superseded by [ADR-028](2026-07-30-gv-sms-send-contract.md) (2026-07-30):** **Decision D7 (§7 — SMS send behind a feature flag) is superseded in full.** The request shape, the response shape, the "non-2xx = generic failure" error model, and the §7 open contract item ("confirm `SendSmsResponse` before wiring") are all replaced by the as-built contract in ADR-028. **§8 (config surface) is unaffected** — `RotaryPhone:Gv:SendEnabled` keeps its name, meaning, and `false` default. The *policy* parts of D7 still stand and are carried forward by ADR-028: never auto-retry, always preserve the composed text, single-flight per target, and the degraded gate.
```

- [ ] **Step 4: Update `design/FUTURE-WORK.md` and `design/INTEGRATIONS.md`**

`FUTURE-WORK.md` — replace the "confirm `SendSmsResponse` shape before wiring send" entry with: send is now contract-correct per ADR-028 and gated by `RotaryPhone:Gv:SendEnabled=false`; the remaining work is a **two-side config flip** (theirs `GVBridge:EnableSmsSend` first, then ours), not a build. Note the accepted residuals: the 120s de-dupe collapse of identical repeated text, and the ambiguous-`timeout` double-send possibility.

`INTEGRATIONS.md` — under the gvbridge integration, document: `POST /api/gvbridge/sms/send` with the four request fields; the nine-code taxonomy table (ADR-028 §2); the `SmsSent` `/hub` event and how it differs from `SmsReceived`; the `RotaryPhone:Gv:SendDedupeWindowSeconds` key and its coupling to their poll cadence; and the rate limit (5 per 10s, process-wide, no `Retry-After`).

- [ ] **Step 5: Full verification**

```bash
dotnet build RadioConsole.sln --configuration Release
dotnet test tests/Radio.Web.Tests --configuration Release
```

Expect 0 warnings (warnings-as-errors in Release) and the full Web suite green. **Baseline updated 2026-07-31:** the 846-passing figure was as of GV-4; GV-8 (PR #461) added `PhonePageThreadLoadErrorTests`, `GvResultTests`, and cases in `PhoneTextsPanelTests` / `GvBridgeApiServiceVoicemailSmsTests`. Take the count from a clean run on `main` before starting rather than trusting either number.

---

## Test Plan

> Consumed by the Tester agent. **Send is flagged OFF on merge**, so §A is the merge gate and §B/§C require a temporary local flag flip and must NOT be taken as permission to ship the flag on.

> ### ⚠ Live-verification preconditions (added 2026-07-31 — read before §B/§C)
>
> These were learned the hard way across the GV-8 cycle; skipping them is how a pass gets invalidated after the fact.
>
> 1. **The box is `192.168.86.50`.** `radio` and `piradio` do **not** resolve from WSL, and `Deploy-ToLinux.ps1` defaults to both a wrong host **and** a wrong architecture — the box is `x86_64` while the script defaults to `linux-arm64`. Deploy with `-TargetHost 192.168.86.50 -Runtime linux-x64` (tracked as **OPS-1 (c)**).
> 2. **Prove the binary under test is actually running.** `systemctl is-active` proves nothing about *which* build is deployed. Until OPS-1 ships a web version endpoint, use a branch-only symbol: `grep -ac OutboundSmsReconciler /opt/radio-console/web/Radio.Web` (non-zero → the new binary is live). `GvCounterparty` and `HttpErrorWithBody` work as second and third probes.
> 3. **Record wall-clock time against the 20-minute GV auth blackout, for every observation.** `curl -s http://192.168.86.50:5004/api/gvbridge/status` → **`psidtsAgeSeconds` is the only honest field** (`<660` healthy, `660–1200` blackout); `available`/`degraded`/`cookiesValid` read healthy straight through a confirmed outage. §B and §C.14–16 and 18–21 **must run inside a healthy window** or an upstream 502 will masquerade as the result being tested.
> 4. **`auth_unavailable` (§C.17) is the one code that is free to observe** — it fires *naturally* during a blackout with **no server config change at all**. Run it deliberately at `psidtsAgeSeconds` 660–1200 rather than trying to synthesize it.

### A. Flag-OFF regression gate (REQUIRED before merge)

With `RotaryPhone:Gv:SendEnabled=false` (shipped default):

1. `/phone` loads; Messages feed renders voicemail + texts + calls as before. **No visual change anywhere.**
2. Open a text thread — bubbles render inbound/outbound, day separators intact, auto-scroll to bottom works.
3. The compose bar renders with input **disabled** and Send **disabled**; no "Texting unavailable" pill (that is the degraded/dark case, not the our-flag-off case).
4. **No new network calls** to `/api/gvbridge/sms/send` in the network panel.
5. Browser console: **zero** errors, zero unhandled SignalR exceptions.
6. Kill the gvbridge status endpoint → amber reconnecting banner appears, compose shows the "Texting unavailable" pill; restore → auto-clears. (Unchanged from GV-3.)
7. Confirm the `SmsSent` subscription is inert: with no outbound traffic, no new log lines, no re-renders.
8. **GV-8 regression — the four pane branches still work and this PR did not disturb them** (added 2026-07-31). Open a thread during a blackout (`psidtsAgeSeconds` 660–1200): the pane must still render `cloud_off` + "Couldn't load messages." + a working `Retry`, the thread's unread dot must be **restored**, and a genuinely empty thread must still read as **empty**. This PR touches `OnOptimisticAppend` and the compose bar on the same pane; PR #461's behaviour is the baseline it must not move.
9. **The new composer gate renders in the failed state** (Chunk 6b Step 5, added 2026-07-31). In that same failed pane, the compose bar shows the **"Reply once this loads."** pill instead of the message field — and the pane still shows its own `cloud_off` + `Retry` above it. **This check runs with the flag OFF**, because the gate is independent of `SendEnabled`. On a successful `Retry`, the normal composer returns.

### B. Send-path happy path (temporary local flag flip; RotaryPhone `EnableSmsSend=true`)

8. **Reply to an existing thread.** Type → Send. Optimistic bubble appears immediately, right-aligned, dimmed with a spinner; input clears; Send disables.
9. On the 200 the bubble goes full-opacity with a **single check**. **No second bubble appears** when the `SmsSent` echo arrives — verify in the network/SignalR panel that the echo *was* received and that the message count did not increase.
10. **Wait through a poller cycle (up to ~120s).** The re-surfaced copy arrives with a different `Id`. Confirm: still exactly one bubble, still in the same list position, **no visual jump**, no status-glyph change.
11. **New-recipient send.** `＋ New` → enter a number → type → Send. Verify the request body carries `toNumber` = the typed number and `threadId: null` (network panel). On success the view drops into the resolved conversation.
12. **No duplicate thread row** after the poller re-surfaces the new conversation under Google's real thread id (this is the ADR-028 §6 bug fix — the row count must stay at one).
13. Send from a **second browser tab** and confirm the first tab appends the message once, with **no inbound toast** and **no unread-badge increment**.

### C. Error taxonomy (temporary local flag flip; force each server-side)

One case per code. For each: confirm the exact bubble treatment, that the **composed text is preserved**, and that **no auto-retry** occurs (watch the network panel for a single request).

14. **`send_disabled` (409)** — set RotaryPhone's `GVBridge:EnableSmsSend=false`. Expect: **optimistic bubble disappears** (not red), text restored to the input, "Texting unavailable" pill shown, Send disabled, Info toast. **This is the most important negative case** — a red failed bubble here is a bug.
15. **`rate_limited` (429)** — send 6 messages inside 10s. Expect: failed bubble + Retry, text preserved, "Slow down / Sending too fast" toast, **Send disabled for ~10s**, then re-enabled.
16. **`invalid_number` (400)** — send to a malformed number via the new-recipient composer. Expect: failed bubble with **no Retry affordance**, inline recipient-field error, actionable toast.
17. **`auth_unavailable` (502)** — expect failed bubble + Retry + the "Google Voice needs to reconnect" toast (distinct copy from the generic failure).
18. **`upstream_error` (502)** and **`error` (500)** — generic "Couldn't send your message. Try again." + Retry.
19. **`timeout` (504)** — expect "No response — check the connection and try again." Then, if the send actually landed, confirm the poller's re-surface **collapses onto the failed bubble** rather than adding a second one.
20. **Retry works and leaves no orphan** — on any retryable failure, tap Retry; confirm exactly one new sending bubble and that the old red bubble is **gone** (GV-3's `TODO(send-ship)` orphan must not reappear).
21. **Unknown code** — if it can be forced, confirm it degrades to the generic failure and does not throw.

### D. Unit tests (must be green)

22. `OutboundSmsReconcilerTests` — all 12. These encode the ADR-028 §4.4 invariant; treat any failure as blocking.
23. `GvBridgeSendServiceCodeMappingTests` — all nine code mappings, the unknown-code fallback, both defensive-200 cases, and **`RequestBody_CarriesToNumberAndCorrelationId`** (the regression test for the defect this PR exists to fix).
24. `GvBridgeSendServiceTests` — flag-off, degraded gate, in-flight (both the thread-keyed and recipient-keyed cases), plus the two new outcome cases (transport → `SendFailedException`, timeout → `SendTimedOutException`).
25. `MessageBubbleTests` — the non-retryable render case.
26. **`GvResultTests` — GV-8's four original cases must be green UNCHANGED**, plus Chunk 0's two additions. The one that matters is `HttpErrorWithBody_CarriesTheBody_ButValueStaysNull`: if `Value` ever becomes non-null on a failure, GV-6's branch idiom is compromised and this PR caused it. Also re-run `GvBridgeApiServiceVoicemailSmsTests` untouched — Chunk 0 Step 2 is a comment and must change no behaviour there.
27. Full suite: `dotnet test tests/Radio.Web.Tests --configuration Release` green, `dotnet build RadioConsole.sln --configuration Release` at 0 warnings.

### D2. Thread reply-ability (ADR-028 §8)

**Runs with the flag OFF as well as on** — the composer gate is independent of `SendEnabled`.

> **Note:** live coverage here is bounded by UAT `G-3` — **zero opaque 36-char sender IDs are reachable from this surface** (the feed caps at 20 threads with no pagination). Case 29 must therefore be run against an **injected** identifier, and recorded as such. Numeric short codes *are* reachable in the live corpus.

28. **Open a thread from a numeric short code.** Composer renders **disabled** with the pill **"You can't reply to this sender."** No red bubble, no Retry, no network request on any interaction.
29. **Open a thread from an opaque sender ID.** Same treatment. Confirm a long (36-char) identifier does not break the compose-bar layout at **1920×720**. (`G-4` already measured 36 chars as safe in the row and header; this checks the *compose bar*, which it did not cover.)
30. **Open a normal E.164 thread.** Composer is **enabled** exactly as before — confirm this gate did not regress ordinary replies. This is the important negative control.
31. **New-recipient composer is unaffected** — `＋ New` still allows typing a number; reply-ability gating applies to open threads, not to the composer where the user supplies the number.
32. **Gate precedence, all four reasons** (updated 2026-07-31 — Chunk 6b Step 5 added a third). Verify the table's order holds: on a short-code thread that *also* failed to load while GV is *also* degraded, the **reply-ability** message wins; with a repliable counterparty, a failed load beats "Texting unavailable"; and with neither, the degraded pill shows as before.
33. **A failed load does not leak a bubble** (the defect Chunk 6b Step 5 prevents). With the flag ON and a thread in its failed state, confirm there is **no reachable path** that appends an optimistic bubble — the pane must stay on `cloud_off` + `Retry` and must **never** flip to a one-bubble "conversation."
34. `GvCounterpartyTests` green — including the null/empty/whitespace defensive cases — plus the two `PhoneTextsPanelTests` composer-gate cases from Chunk 6b Step 7.

### E. Restore before merge

35. **Confirm `RotaryPhone:Gv:SendEnabled` is back to `false`** in `appsettings.json` and that no `appsettings.Production.json` override was committed. This is a merge blocker.
36. Confirm RotaryPhone's `GVBridge:EnableSmsSend` is back to its shipped `false` if §B/§C flipped it — the two flags are independent and both must land dark.
