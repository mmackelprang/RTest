# GV Messages — PR4: Mark-Read / Durable Read-State

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flip read-state from UI-local (GV-2/GV-3 v1) to **durable via GV write-through** against the now-**ratified, stable** contract (ADR-024). Wire the GV-2 `MarkVoicemailReadAsync` seam to the real route, add the SMS-thread sibling, subscribe to the unified `ReadStateChanged` SignalR event on the existing `/hub`, and make reconciliation **idempotent keyed by `(id-or-threadId + isRead)`** so the route's returned DTO and RotaryPhone's unconditional echo (and the 502 keep-optimistic path) all collapse to one badge state with no flicker. Ships behind `RotaryPhone:Gv:MarkReadEnabled` (default OFF) so it builds/tests/merges **before** RotaryPhone's owner-HELD build deploys, and lights up in one flag flip.

**Owner-baked decisions in scope here (from ADR-024):**
- **Read-state is GV write-through; Google is the single source of truth; RadioConsole keeps NO local read-state store** (ADR-024 §2.2). The only in-memory state is the per-circuit optimistic flip GV-2/GV-3 already hold — it is presentation, never persistence, and is overwritten by the returned DTO / next list fetch / `ReadStateChanged` push.
- **List endpoints' `isRead`/`hasUnread` are authoritative on every (re)load** (ADR-024 §2). Drop the UI-local seeding; the now-false `// UI-LOCAL only` comment from GV-2 is corrected.
- **The de-dupe invariant is the one correctness rule:** RotaryPhone broadcasts `ReadStateChanged` **unconditionally — including back to the originator** (ADR-024 §4.1 / §9). Every mark yields ≥2 signals (returned DTO + echoed broadcast); 502 adds a third path. The handler MUST be idempotent keyed by `(id-or-threadId + isRead)`.
- **No unread toggle in v1** (ADR-024 §6). `isRead:true` is the contract; `isRead:false` may `400 unread_unsupported` until RotaryPhone's live capture confirms it. The `isRead` parameter exists for forward-compat, but no UI affordance calls it with `false`.
- **No new auth** (ADR-024 §7). The gvbridge `/api/gvbridge/*` prefix gate auto-covers the two mark routes; reuse the existing `RotaryPhone:Gv:AuthKey` seam (the `RotaryPhoneAuthHandler` from GV-1) on the same `GvBridgeApiService` `HttpClient`.

**Sources of truth (do not redesign):**
- **ADR-024** (ratified contract + the GV-4 build list): `design/decisions/2026-06-20-gv-mark-read-durable-readstate.md` — §3 (routes/shapes/status codes), §4 (event), §8 (component delta), §9 (the de-dupe invariant).
- **Ratification reply** (source contract, frozen shapes): `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-markread-reply.md`.
- The GV-2 `MarkVoicemailReadAsync` seam + `_locallyHeard` it introduced: `docs/superpowers/plans/2026-06-20-gv-messages-pr2-voicemail-surface.md` (Chunk 1 Task 1; Chunk 5 Task 6 `OnVoicemailHeard` / `_locallyHeard`).
- The GV-3 SMS read surface + `_locallyReadThreads` flag pattern: `docs/superpowers/plans/2026-06-20-gv-messages-pr3-texts-surface.md` (Chunk 5 Task 5 `OpenThreadAsync` / `_locallyReadThreads`).

**Tech stack:** Blazor Server, Radzen, SignalR client (`PhoneHubService` on `/hub`), `design-system.css` tokens. No new JS, no new component, no new client, no new hub, no local store.

**Dependencies:** **GV-2 must be merged** (the `MarkVoicemailReadAsync` flagged no-op seam + the `_locallyHeard` optimistic flip in `PhonePage`) **and GV-3 must be merged** (the SMS read surface: `_locallyReadThreads`, `OpenThreadAsync`, the thread list + conversation). GV-4 repoints the GV-2 seam, adds the SMS sibling, and replaces the two UI-local flip mechanisms with authoritative-reconcile. GV-4 does **not** depend on RotaryPhone's build (owner-HELD): it builds against the frozen shapes behind `MarkReadEnabled=false` and lights up when they deploy.

---

## File Map

### New files

| File | Responsibility |
|------|---------------|
| `tests/Radio.Web.Tests/Services/ReadStateReconcilerTests.cs` | Unit tests for the idempotent `(id-or-threadId + isRead)` reconciler (the §9 invariant) in isolation. |

### Modified files

| File | Changes |
|------|---------|
| `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | Repoint `MarkVoicemailReadAsync` at `POST /api/gvbridge/voicemail/{id}/read` (parse `VoicemailItemDto`); **add** `MarkSmsThreadReadAsync` → `POST /api/gvbridge/sms/threads/{threadId}/read` (parse `SmsThreadDto`). Both behind `RotaryPhone:Gv:MarkReadEnabled`; `200`→DTO, `404`→null, `502`/non-2xx→null (keep optimistic flip, no auto-retry). |
| `src/Radio.Web/Models/ApiModels.cs` | Add `ReadStateChangedDto` record `{ Kind, Id, ThreadId, IsRead, ChangedAtUtc }`. Correct the now-false `// UI-LOCAL only` comment on `VoicemailItemDto.IsRead` (and any SMS equivalent) → `// authoritative (GV write-through); ADR-024`. |
| `src/Radio.Web/Services/PhoneHubService.cs` | Add `.On<ReadStateChangedDto>("ReadStateChanged", …)` on the **existing `/hub` connection** + an `event Action<ReadStateChangedDto>? ReadStateChanged`. Defensive parse: unknown `Kind` ignored at `Debug`. |
| `src/Radio.Web/Components/Pages/PhonePage.razor` | Subscribe to `PhoneHub.ReadStateChanged`; route through the **idempotent reconciler** keyed by `(id-or-threadId + isRead)`. Drop the UI-local seeding (`_locallyHeard` / `_locallyReadThreads` no longer source-of-truth on reload); apply server-truth from list endpoints + reconcile from the mark route's returned DTO + the echoed broadcast. Wire the voicemail-heard / thread-open seams to the real mark methods. |
| `src/Radio.Web/appsettings.json` | Confirm `RotaryPhone:Gv:MarkReadEnabled` exists (GV-2 added it `false`); no change to the value. |
| `design/FUTURE-WORK.md` | Mark-read is now **durable (GV write-through)** behind `MarkReadEnabled`; lights up when RotaryPhone's owner-HELD build deploys. Note path (b) (phone→kiosk LIVE push) is RotaryPhone's fast-follow; until then phone-side reads reconcile on next list refresh — same handler, no change. Remove the "wired-but-no-op" / "GV mark-read requested" framing (now ratified + implemented). |
| `design/INTEGRATIONS.md` | Document the two mark routes + the `ReadStateChanged` event + the `MarkReadEnabled` flag (distinct from RotaryPhone's server-side `EnableMarkRead` build flag) under the gvbridge integration. |

---

## Chunk 1: ReadStateChangedDto + DTO comment correction

### Task 1: Add ReadStateChangedDto and correct the read-state comments

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs`
- Test: `tests/Radio.Web.Tests/Services/ReadStateReconcilerTests.cs` (the dedup tests in Chunk 4 assert the shape parses; the DTO existence is verified by compilation here)

> ADR-024 §8.2: `ReadStateChangedDto` is a new record in `ApiModels.cs`. Defensive — unknown `Kind` ignored. Wire payload is camelCase (`kind`/`id`/`threadId`/`isRead`/`changedAtUtc`); SignalR's JSON options already lowercase-match by default, so the C# PascalCase property names bind without `[JsonPropertyName]`. ADR-024 §2.2 / §8.4: the GV-2 `// UI-LOCAL only` comment on `VoicemailItemDto.IsRead` is now **false** and must be corrected.

- [ ] **Step 1: Add the `ReadStateChangedDto` record**

In `ApiModels.cs`, near the other GV DTOs (`VoicemailItemDto`, `SmsThreadDto`), add:

```csharp
/// <summary>
/// Unified read-state change event (ADR-024 §4). Pushed on the existing /hub
/// "ReadStateChanged" alongside GvVoicemailReceived / GvSmsReceived. Fires on OUR
/// own marks (path a, ships with the routes) and — once RotaryPhone's poller-flip
/// fast-follow lands — on externally-originated flips (path b, phone/GV-web reads);
/// SAME handler covers both with no change. RotaryPhone broadcasts UNCONDITIONALLY,
/// including back to the originator, so de-dupe is keyed by (id-or-threadId + IsRead).
/// </summary>
/// <param name="Kind">"Voicemail" | "Sms". Anything else is ignored defensively.</param>
/// <param name="Id">Voicemail id when Kind=Voicemail; null/empty for Sms thread-level.</param>
/// <param name="ThreadId">Thread id when Kind=Sms (required); voicemail's threadId when Kind=Voicemail (informational).</param>
/// <param name="IsRead">New read-state. For Sms thread-level this is "thread fully read" (!hasUnread).</param>
/// <param name="ChangedAtUtc">ISO-8601 UTC timestamp of the change.</param>
public record ReadStateChangedDto(
  string Kind,
  string? Id,
  string? ThreadId,
  bool IsRead,
  DateTime ChangedAtUtc);
```

- [ ] **Step 2: Correct the now-false read-state comments**

Find the GV-2 comment on `VoicemailItemDto.IsRead`:

```csharp
//  ... isRead,   // UI-LOCAL only — GV mark-read not in v1
```

Replace with:

```csharp
//  ... isRead,   // authoritative (GV write-through); ADR-024
```

If `SmsThreadDto.HasUnread` carries any equivalent `// UI-LOCAL` note, correct it the same way to `// authoritative (GV write-through); ADR-024`. (If neither carries a UI-LOCAL comment because they are bare positional records, add the one-line `// authoritative (GV write-through); ADR-024` above each record so the semantics are unambiguous — do NOT change the record shape.)

- [ ] **Step 3: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Models/ApiModels.cs
git commit -m "feat(web): add ReadStateChangedDto; correct read-state semantics to GV write-through (ADR-024)"
```

---

## Chunk 2: GvBridgeApiService — real mark routes

### Task 2: Repoint MarkVoicemailReadAsync + add MarkSmsThreadReadAsync

**Files:**
- Modify: `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`
- Test: `tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs` (extend the GV-1/GV-2 file)

> ADR-024 §3 + §5 + §8.1. The GV-2 seam returned `bool` (no-op). GV-4 changes the return type to the **frozen DTO** (`VoicemailItemDto?` / `SmsThreadDto?`) so the caller reconciles the badge from authoritative state in one round-trip (the contract returns the updated DTO, not 204). Body `{ "isRead": true }` (camelCase). `200`→parse DTO; `404`→null (item gone, drop/refresh, no retry); `502`/non-2xx→null **but the caller keeps the optimistic flip** and reconciles on next list/poll/push. **No client-side auto-retry** — one attempt per user action (the contract is retry-safe, but a UI-driven retry is the right place; a wedged GV would otherwise spin). Both gated on `RotaryPhone:Gv:MarkReadEnabled` (the in-tree key GV-2 already wired — do NOT rename, do NOT confuse with RotaryPhone's server-side `EnableMarkRead` build flag).

- [ ] **Step 1: Replace the GV-2 no-op test, add the SMS test (failing)**

In `GvBridgeApiServiceVoicemailSmsTests.cs`, the GV-2 test `MarkVoicemailReadAsync_NoOps_WhenFlagOff` asserted `Assert.False(result)` against a `bool` return — that signature is changing. Update it to assert the no-op returns `null` and makes no HTTP call, and add the flag-on / 404 / 502 / SMS cases:

```csharp
private static GvBridgeApiService BuildSvc(MockHttpHandler handler, bool markReadEnabled)
{
  var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
  var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
      { ["RotaryPhone:Gv:MarkReadEnabled"] = markReadEnabled.ToString() })
    .Build();
  return new GvBridgeApiService(client, NullLogger<GvBridgeApiService>.Instance, config);
}

[Fact]
public async Task MarkVoicemailReadAsync_NoOps_WhenFlagOff()
{
  var handler = new MockHttpHandler("{}");
  var svc = BuildSvc(handler, markReadEnabled: false);

  var result = await svc.MarkVoicemailReadAsync("vm1");

  Assert.Null(result);                  // no DTO when flag off
  Assert.Equal(0, handler.RequestCount); // never hit the network
}

[Fact]
public async Task MarkVoicemailReadAsync_ReturnsDto_On200_WhenFlagOn()
{
  // Frozen VoicemailItemDto read shape (ADR-024 §3.1).
  const string body = """
    { "id":"vm1","threadId":"t1","fromNumber":"+15551234567","fromName":"Jane",
      "receivedAt":"2026-06-20T18:03:11Z","durationSeconds":42,"isRead":true,
      "transcript":"hi","audioUrl":"/api/gvbridge/voicemail/vm1/audio" }
    """;
  var handler = new MockHttpHandler(body);   // 200 OK
  var svc = BuildSvc(handler, markReadEnabled: true);

  var dto = await svc.MarkVoicemailReadAsync("vm1");

  Assert.NotNull(dto);
  Assert.True(dto!.IsRead);
  Assert.Equal("vm1", dto.Id);
  Assert.Equal(1, handler.RequestCount);
}

[Fact]
public async Task MarkVoicemailReadAsync_ReturnsNull_On404_WhenFlagOn()
{
  var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
  var svc = BuildSvc(handler, markReadEnabled: true);

  Assert.Null(await svc.MarkVoicemailReadAsync("gone"));
}

[Fact]
public async Task MarkVoicemailReadAsync_ReturnsNull_On502_NoRetry_WhenFlagOn()
{
  // 502 = GV unreachable. Caller keeps the optimistic flip; client never auto-retries.
  var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
  var svc = BuildSvc(handler, markReadEnabled: true);

  Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
  Assert.Equal(1, handler.RequestCount);  // exactly one attempt, no retry
}

[Fact]
public async Task MarkSmsThreadReadAsync_ReturnsDto_On200_WhenFlagOn()
{
  // Frozen SmsThreadDto read shape (ADR-024 §3.2).
  const string body = """
    { "threadId":"t1","counterpartyNumber":"+15551234567","counterpartyName":"Mom",
      "lastMessageAt":"2026-06-20T18:03:11Z","hasUnread":false,"lastMessagePreview":"ok" }
    """;
  var handler = new MockHttpHandler(body);
  var svc = BuildSvc(handler, markReadEnabled: true);

  var dto = await svc.MarkSmsThreadReadAsync("t1");

  Assert.NotNull(dto);
  Assert.False(dto!.HasUnread);
  Assert.Equal("t1", dto.ThreadId);
  Assert.Equal(1, handler.RequestCount);
}

[Fact]
public async Task MarkSmsThreadReadAsync_NoOps_WhenFlagOff()
{
  var handler = new MockHttpHandler("{}");
  var svc = BuildSvc(handler, markReadEnabled: false);

  Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));
  Assert.Equal(0, handler.RequestCount);
}
```

> `MockHttpHandler` already exposes `RequestCount` (added in GV-2 Task 1) and a `statusCode` ctor overload (used across `Radio.Web.Tests`). Ensure the file `using`s include `System.Net`, `System.Net.Http.Json`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging.Abstractions`, `Radio.Web.Models`.

- [ ] **Step 2: Replace `MarkVoicemailReadAsync` with the DTO-returning wire call**

Remove the GV-2 no-op body and replace with:

```csharp
/// <summary>
/// Mark a voicemail read via GV write-through (ADR-024 §3.1 / §5). Google is the
/// single source of truth; the returned DTO is authoritative — reconcile the badge
/// from it. Gated on RotaryPhone:Gv:MarkReadEnabled (in-tree consumer flag; distinct
/// from RotaryPhone's server-side EnableMarkRead build flag). Flag off → silent no-op
/// returning null (the caller has ALREADY flipped the row read optimistically; a no-op
/// must never disturb that). 200 → DTO; 404 → null (item gone); 502/non-2xx → null but
/// the caller KEEPS the optimistic flip and reconciles on the next list/poll/push.
/// NEVER auto-retries — one attempt per user action (a UI-driven retry is the right place).
/// v1 callers pass isRead: true only (§6 — unread may 400 unread_unsupported).
/// </summary>
public async Task<VoicemailItemDto?> MarkVoicemailReadAsync(
  string id, bool isRead = true, CancellationToken ct = default)
{
  if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
  {
    return null;  // UI-local optimistic flip already applied by the caller
  }
  try
  {
    var response = await _httpClient.PostAsJsonAsync(
      $"/api/gvbridge/voicemail/{Uri.EscapeDataString(id)}/read",
      new { isRead }, ct);

    if (response.StatusCode == HttpStatusCode.NotFound) return null;   // item gone
    if (!response.IsSuccessStatusCode)
    {
      // 502 = GV unreachable. Keep the optimistic flip; reconcile later. No retry.
      _logger.LogError("Mark-read voicemail {Id} failed: {Status}", id, (int)response.StatusCode);
      return null;
    }
    return await response.Content.ReadFromJsonAsync<VoicemailItemDto>(cancellationToken: ct);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Mark-read voicemail {Id} threw (non-fatal); optimistic flip kept", id);
    return null;
  }
}
```

- [ ] **Step 3: Add the `MarkSmsThreadReadAsync` sibling**

```csharp
/// <summary>
/// Mark a whole SMS thread read via GV write-through (ADR-024 §3.2 / §5). Per-thread
/// grain → hasUnread=false. Same posture as MarkVoicemailReadAsync: flag-gated,
/// 200→DTO, 404→null, 502/non-2xx→null (keep optimistic flip), no auto-retry.
/// </summary>
public async Task<SmsThreadDto?> MarkSmsThreadReadAsync(
  string threadId, bool isRead = true, CancellationToken ct = default)
{
  if (!_configuration.GetValue("RotaryPhone:Gv:MarkReadEnabled", false))
  {
    return null;
  }
  try
  {
    var response = await _httpClient.PostAsJsonAsync(
      $"/api/gvbridge/sms/threads/{Uri.EscapeDataString(threadId)}/read",
      new { isRead }, ct);

    if (response.StatusCode == HttpStatusCode.NotFound) return null;
    if (!response.IsSuccessStatusCode)
    {
      _logger.LogError("Mark-read thread {ThreadId} failed: {Status}", threadId, (int)response.StatusCode);
      return null;
    }
    return await response.Content.ReadFromJsonAsync<SmsThreadDto>(cancellationToken: ct);
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Mark-read thread {ThreadId} threw (non-fatal); optimistic flip kept", threadId);
    return null;
  }
}
```

> Ensure the file's `using`s include `System.Net`, `System.Net.Http.Json`. The `RotaryPhoneAuthHandler` already rides this client's `HttpClient` (GV-1) — the POSTs carry `X-RotaryPhone-Auth` automatically when `RotaryPhone:Gv:AuthKey` is set (ADR-024 §7). No new auth wiring.

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests"
git add src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs tests/Radio.Web.Tests/Services/GvBridgeApiServiceVoicemailSmsTests.cs
git commit -m "feat(web): wire GV mark-read routes (voicemail + sms thread) with 404/502/no-retry posture (ADR-024)"
```

---

## Chunk 3: PhoneHubService — ReadStateChanged subscription

### Task 3: Subscribe to ReadStateChanged on the existing /hub

**Files:**
- Modify: `src/Radio.Web/Services/PhoneHubService.cs`
- Test: `tests/Radio.Web.Tests/Services/PhoneHubServiceTests.cs` (extend the GV-1 file; if none exists, create it)

> ADR-024 §4 / §8.3. The event goes on `PhoneHubService` (the `/hub` `RotaryHub` consumer that already owns `GvVoicemailReceived`/`GvSmsReceived`) — **NOT** `GvTrunkHubService` (`/hubs/gvtrunk`, a different product; contract risk #2). Subscribe alongside the existing handlers. Defensive parse: unknown `Kind` (anything other than `"Voicemail"`/`"Sms"`) is ignored at `Debug`, never throws (ADR-024 §4.2). The de-dupe keying lives in `PhonePage` (Chunk 4) — `PhoneHubService` only surfaces the raw event.

- [ ] **Step 1: Add the event declaration**

Alongside the existing `public event Action<VoicemailItemDto>? GvVoicemailReceived;` / `GvSmsReceived`:

```csharp
/// <summary>
/// Fired when read-state changes from ANY source (ADR-024 §4). Path (a): our own
/// marks (ships with RotaryPhone's routes). Path (b): externally-originated flips —
/// phone/GV-web reads — once RotaryPhone's poller-flip fast-follow lands (same event,
/// no change here). Consumers MUST de-dupe by (id-or-threadId + isRead); RotaryPhone
/// broadcasts unconditionally, including back to the originator.
/// </summary>
public event Action<ReadStateChangedDto>? ReadStateChanged;
```

- [ ] **Step 2: Register the `.On<>` handler on the existing `/hub` connection**

Where GV-1 registered `_connection.On<VoicemailItemDto>("GvVoicemailReceived", …)` and `On<SmsMessageDto>("GvSmsReceived", …)`, add (same connection, `_connection` — do NOT open a second hub):

```csharp
_connection.On<ReadStateChangedDto>("ReadStateChanged", dto =>
{
  // Defensive: only Voicemail/Sms are known; ignore anything else (ADR-024 §4.2).
  if (dto is null ||
      (!string.Equals(dto.Kind, "Voicemail", StringComparison.OrdinalIgnoreCase) &&
       !string.Equals(dto.Kind, "Sms", StringComparison.OrdinalIgnoreCase)))
  {
    _logger.LogDebug("Ignoring ReadStateChanged with unknown Kind '{Kind}'", dto?.Kind);
    return;
  }
  ReadStateChanged?.Invoke(dto);
});
```

- [ ] **Step 3: Write a hub test (defensive-parse + invoke)**

```csharp
[Fact]
public void ReadStateChanged_RaisesEvent_ForKnownKind()
{
  var svc = BuildHubServiceUnderTest();   // same helper the GV-1 tests use
  ReadStateChangedDto? captured = null;
  svc.ReadStateChanged += d => captured = d;

  svc.RaiseReadStateChangedForTest(
    new ReadStateChangedDto("Voicemail", "vm1", "t1", true, DateTime.UtcNow));

  Assert.NotNull(captured);
  Assert.Equal("vm1", captured!.Id);
}

[Fact]
public void ReadStateChanged_IgnoresUnknownKind()
{
  var svc = BuildHubServiceUnderTest();
  var raised = false;
  svc.ReadStateChanged += _ => raised = true;

  svc.RaiseReadStateChangedForTest(
    new ReadStateChangedDto("Garbage", null, null, true, DateTime.UtcNow));

  Assert.False(raised);   // unknown Kind ignored
}
```

> Mirror the GV-1 test harness: if `PhoneHubService` already exposes a test seam for raising `GvVoicemailReceived` (the GV-1 tests must have one to test the `/hub` handlers without a live connection), add a parallel `internal void RaiseReadStateChangedForTest(ReadStateChangedDto dto)` that runs the same defensive-parse guard then invokes the event. If GV-1's tests instead exercise the raw `On<>` lambda, follow that pattern instead — match the existing file. The two behaviors under test are fixed: known Kind raises, unknown Kind does not.

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PhoneHubServiceTests"
git add src/Radio.Web/Services/PhoneHubService.cs tests/Radio.Web.Tests/Services/PhoneHubServiceTests.cs
git commit -m "feat(web): subscribe ReadStateChanged on /hub (defensive Kind parse) (ADR-024)"
```

---

## Chunk 4: The idempotent reconciler — THE KEY INVARIANT

### Task 4: Idempotent read-state reconciler keyed by (id-or-threadId + isRead)

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor`
- Test: `tests/Radio.Web.Tests/Services/ReadStateReconcilerTests.cs`

> **ADR-024 §9 — the single most important thing to get right.** RotaryPhone broadcasts `ReadStateChanged` **unconditionally, including back to the originator.** So a single user mark produces (1) the mark route's returned DTO and (2) the echoed broadcast — and a 502 adds a third path (keep optimistic flip, reconcile on next list/poll/push). All three resolve to the SAME logical state, **keyed by `(id-or-threadId + isRead)`**. The reconciler MUST be idempotent on that key: applying `(target, isRead)` that is already applied is a no-op. Get this wrong and the badge flickers / double-applies / fights the optimistic flip. This is a first-class task with its own isolated test, decoupled from Blazor rendering so the invariant is verifiable in a plain unit test.

> **Design:** extract the keying + idempotent-apply logic into a small static helper `ReadStateReconciler` (a plain class, no Blazor dependency) so it can be unit-tested in isolation. `PhonePage` holds the two domain lists (`_voicemails`, `_threads`) and calls the helper to compute "did this signal change anything?"; only a real change triggers `StateHasChanged` (so an echo of an already-applied mark is a silent no-op — no re-render, no flicker).

- [ ] **Step 1: Write the failing isolated reconciler test**

Create `tests/Radio.Web.Tests/Services/ReadStateReconcilerTests.cs`:

```csharp
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

public class ReadStateReconcilerTests
{
  private static VoicemailItemDto Vm(string id, bool isRead) =>
    new(id, "t1", "+15551234567", "Jane", DateTime.UtcNow, 42, isRead, "hi",
      $"/api/gvbridge/voicemail/{id}/audio");

  private static SmsThreadDto Thread(string id, bool hasUnread) =>
    new(id, "+15551234567", "Mom", DateTime.UtcNow, hasUnread, "ok");

  [Fact]
  public void ApplyVoicemail_FlipsUnreadToRead_ReturnsTrue()
  {
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: false) };

    var changed = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);

    Assert.True(changed);
    Assert.True(list[0].IsRead);
  }

  [Fact]
  public void ApplyVoicemail_AlreadyInState_IsNoOp_ReturnsFalse()
  {
    // The echoed broadcast of our own mark, or a re-mark, must be idempotent.
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: true) };

    var changed = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);

    Assert.False(changed);                // no change → caller skips StateHasChanged
    Assert.True(list[0].IsRead);
  }

  [Fact]
  public void ApplyVoicemail_UnknownId_IsNoOp_ReturnsFalse()
  {
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: false) };

    Assert.False(ReadStateReconciler.ApplyVoicemail(list, "missing", isRead: true));
    Assert.False(list[0].IsRead);
  }

  [Fact]
  public void ApplyVoicemail_TwoSignalsSameKey_AppliesOnce()
  {
    // Mark route returned DTO + echoed broadcast = same (id, isRead). Second is no-op.
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: false) };

    var first = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);
    var second = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);

    Assert.True(first);
    Assert.False(second);                 // idempotent on (id-or-threadId + isRead)
    Assert.True(list[0].IsRead);
  }

  [Fact]
  public void ApplyThread_FlipsHasUnread_ReturnsTrue_ThenNoOp()
  {
    // Thread "read" = hasUnread:false. isRead:true in the event maps to hasUnread:false.
    var list = new List<SmsThreadDto> { Thread("t1", hasUnread: true) };

    var first = ReadStateReconciler.ApplyThread(list, "t1", isRead: true);
    var second = ReadStateReconciler.ApplyThread(list, "t1", isRead: true);

    Assert.True(first);
    Assert.False(second);
    Assert.False(list[0].HasUnread);
  }
}
```

- [ ] **Step 2: Implement the reconciler**

Create `src/Radio.Web/Services/ReadStateReconciler.cs`:

```csharp
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// Idempotent read-state reconciliation keyed by (id-or-threadId + isRead) — ADR-024 §9.
/// RotaryPhone broadcasts ReadStateChanged UNCONDITIONALLY, including back to the
/// originator, so every mark produces ≥2 signals (the mark route's returned DTO and the
/// echoed broadcast); 502 adds a third "keep optimistic, reconcile later" path. All
/// resolve to the same key. Apply* returns TRUE only if the list actually changed, so the
/// caller skips StateHasChanged on an echo of an already-applied mark (no flicker, no
/// double-apply). Records are immutable → we replace the element via `with`.
/// </summary>
public static class ReadStateReconciler
{
  /// <summary>Set voicemail {id}.IsRead = isRead. Returns true iff something changed.</summary>
  public static bool ApplyVoicemail(List<VoicemailItemDto> voicemails, string? id, bool isRead)
  {
    if (string.IsNullOrEmpty(id)) return false;
    var idx = voicemails.FindIndex(v => v.Id == id);
    if (idx < 0) return false;                        // unknown id → no-op
    if (voicemails[idx].IsRead == isRead) return false; // already in state → idempotent no-op
    voicemails[idx] = voicemails[idx] with { IsRead = isRead };
    return true;
  }

  /// <summary>
  /// Set thread {threadId} read-state. The event's isRead:true means "thread fully read"
  /// → HasUnread = false (ADR-024 §4 payload note). Returns true iff something changed.
  /// </summary>
  public static bool ApplyThread(List<SmsThreadDto> threads, string? threadId, bool isRead)
  {
    if (string.IsNullOrEmpty(threadId)) return false;
    var idx = threads.FindIndex(t => t.ThreadId == threadId);
    if (idx < 0) return false;
    var hasUnread = !isRead;
    if (threads[idx].HasUnread == hasUnread) return false; // idempotent no-op
    threads[idx] = threads[idx] with { HasUnread = hasUnread };
    return true;
  }
}
```

- [ ] **Step 3: Route `ReadStateChanged` through the reconciler in `PhonePage`**

Subscribe in `OnInitializedAsync` (alongside the GV-2/GV-3 subscriptions):

```csharp
PhoneHub.ReadStateChanged += OnReadStateChanged;
```

Handler (the central join point for ALL three signal paths — own-mark DTO, echoed broadcast, and 502-deferred reconcile-on-next-list):

```csharp
private void OnReadStateChanged(ReadStateChangedDto dto)
{
  if (_disposed) return;
  var changed = false;

  if (string.Equals(dto.Kind, "Voicemail", StringComparison.OrdinalIgnoreCase))
  {
    if (_voicemails != null)
      changed = ReadStateReconciler.ApplyVoicemail(_voicemails, dto.Id, dto.IsRead);
    // dto.ThreadId is informational here; no thread-badge bump needed for v1.
  }
  else if (string.Equals(dto.Kind, "Sms", StringComparison.OrdinalIgnoreCase))
  {
    if (_threads != null)
      changed = ReadStateReconciler.ApplyThread(_threads, dto.ThreadId, dto.IsRead);
  }

  if (changed)                       // skip re-render on an echo of an already-applied mark
  {
    PhoneUnread.Set(UnreadSum);
    _ = InvokeAsync(StateHasChanged);
  }
}
```

Unsubscribe in `Dispose`: `PhoneHub.ReadStateChanged -= OnReadStateChanged;`.

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~ReadStateReconcilerTests"
git add src/Radio.Web/Services/ReadStateReconciler.cs src/Radio.Web/Components/Pages/PhonePage.razor tests/Radio.Web.Tests/Services/ReadStateReconcilerTests.cs
git commit -m "feat(web): idempotent read-state reconciler keyed by (id-or-threadId + isRead) (ADR-024 §9)"
```

---

## Chunk 5: Drop UI-local read-state; wire the mark seams to the real routes

### Task 5: Replace UI-local flip with authoritative reconcile

**Files:**
- Modify: `src/Radio.Web/Components/Pages/PhonePage.razor`

> ADR-024 §2 / §8.4. GV-2 seeded read-state from `_locallyHeard` on every reload (`ApplyLocalHeard`) and GV-3 from `_locallyReadThreads` (`ApplyLocalRead`). Those made the local flip a **competing truth** that survived reload — exactly what ADR-024 §2.2 forbids. GV-4 removes that seeding: list endpoints are source-of-truth on (re)load. The per-circuit optimistic flip remains as a presentation-only bridge between tap and the authoritative response — but it is NOT replayed onto fresh list data. The voicemail-heard and thread-open seams now ALSO call the real mark route and reconcile from its returned DTO.

- [ ] **Step 1: Remove the UI-local seeding from the list loads**

In `LoadVoicemailsAsync`, replace `_voicemails = ApplyLocalHeard(list.Items.ToList());` with:

```csharp
_voicemails = list.Items.ToList();   // list endpoint is source-of-truth (ADR-024 §2)
```

Delete the `ApplyLocalHeard` method and the `_locallyHeard` field. In `LoadThreadsAsync`, replace `_threads = ApplyLocalRead(list.Threads.ToList());` with:

```csharp
_threads = list.Threads.ToList();    // hasUnread is source-of-truth (ADR-024 §2)
```

Delete the `ApplyLocalRead` method and the `_locallyReadThreads` field.

> Also remove the GV-2/GV-3 new-arrival lines that consulted `_locallyHeard` / `_locallyReadThreads` (e.g. the `_locallyHeard.Contains(vm.Id) ? vm with { IsRead = true } : vm` ternary in `OnGvVoicemailReceived`) — new arrivals carry their authoritative `isRead`/`hasUnread` from the push DTO; do not re-seed.

- [ ] **Step 2: Voicemail-heard seam → optimistic flip + real mark + reconcile**

Replace the GV-2 `OnVoicemailHeard(string id)` (which only added to `_locallyHeard` and flipped locally) with:

```csharp
private async Task OnVoicemailHeard(string id)
{
  if (_voicemails == null) return;
  // 1) Optimistic flip (presentation-only; no local persistence).
  ReadStateReconciler.ApplyVoicemail(_voicemails, id, isRead: true);
  PhoneUnread.Set(UnreadSum);
  await InvokeAsync(StateHasChanged);

  // 2) Durable write-through. 200 → authoritative DTO reconciles (idempotent — no flicker
  //    since the optimistic flip already set it). 502/404 → null; the optimistic flip stays
  //    and the next list/poll/push reconciles. The echoed ReadStateChanged also reconciles
  //    via OnReadStateChanged — same (id, isRead) key, so it's a no-op.
  var dto = await GvBridgeApi.MarkVoicemailReadAsync(id, isRead: true);
  if (dto != null)
  {
    var changed = ReadStateReconciler.ApplyVoicemail(_voicemails, dto.Id, dto.IsRead);
    if (changed)
    {
      PhoneUnread.Set(UnreadSum);
      await InvokeAsync(StateHasChanged);
    }
  }
}
```

> `OnVoicemailHeard` was previously `void`; change its signature to `async Task` and update the `OnVoicemailHeard` callback wiring on `PhoneMessagesPanel` (the `EventCallback<string>` already awaits a `Task` or `void` handler — no markup change needed, just the method signature).

- [ ] **Step 3: Thread-open seam → optimistic flip + real mark + reconcile**

In `OpenThreadAsync` (GV-3), replace the `_locallyReadThreads.Add(threadId);` + manual `_threads[idx] = … with { HasUnread = false }` block with the reconciler + the real mark:

```csharp
private async Task OpenThreadAsync(string threadId)
{
  _openThreadId = threadId;

  // 1) Optimistic flip (presentation-only).
  if (_threads != null)
    ReadStateReconciler.ApplyThread(_threads, threadId, isRead: true);

  var messages = await GvBridgeApi.GetSmsThreadMessagesAsync(threadId);
  _openThreadMessages = messages?.Messages.ToList() ?? new();
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

> `UnheardVoicemailCount` / `UnreadThreadCount` / `UnreadSum` are unchanged (they already derive from `_voicemails`/`_threads`). With the local seeding gone they now reflect server truth on reload automatically.

- [ ] **Step 4: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/Components/Pages/PhonePage.razor
git commit -m "feat(web): drop UI-local read-state seeding; mark seams write through + reconcile (ADR-024)"
```

---

## Chunk 6: Config + UI affordances (no unread toggle)

### Task 6: Confirm the flag + mark affordances call the seam (no unread toggle)

**Files:**
- Modify: `src/Radio.Web/appsettings.json` (confirm only)
- Modify: `src/Radio.Web/Components/Pages/VoicemailPlayer.razor` (GV-2 already calls the seam on open; confirm it passes `isRead: true` and renders no unread affordance)

> ADR-024 §5 / §6. `RotaryPhone:Gv:MarkReadEnabled` was added (default `false`) by GV-2 Task 1 Step 4 — GV-4 does NOT add a third name or change the value. **No unread toggle in v1** (§6): `isRead:false` may `400 unread_unsupported` until RotaryPhone's live capture confirms it; keep any unread affordance hidden. The `isRead` parameter exists for forward-compat only.

- [ ] **Step 1: Confirm the config flag (no edit unless missing)**

Verify `appsettings.json` has, under `RotaryPhone:Gv`:

```jsonc
"MarkReadEnabled": false
```

If GV-2 added it, leave it. If absent, add it `false`. Do NOT add `EnableMarkRead` (that is RotaryPhone's server-side build flag, not a RadioConsole key).

- [ ] **Step 2: Confirm the voicemail mark affordance (no unread toggle)**

In `VoicemailPlayer.razor`, the GV-2 `MarkHeardOnceAsync` already calls `GvBridgeApi.MarkVoicemailReadAsync(Item.Id)` on open/play. Confirm the call site now reads `MarkVoicemailReadAsync(Item.Id, isRead: true)` (the `isRead` default already covers this; make it explicit for clarity). Confirm there is **no** "mark unread" button anywhere in the player or row (there was none in GV-2 — this is a guard, not a removal). The SMS thread mark is driven by `OpenThreadAsync` (Task 5) on thread open — no per-message or per-thread unread affordance is rendered.

> The actual badge flip + reconcile now happens in `PhonePage.OnVoicemailHeard` (Task 5), which `VoicemailPlayer` already bubbles to via `OnHeard`. `VoicemailPlayer` itself can drop its direct `MarkVoicemailReadAsync` call IF `OnHeard` is guaranteed to fire first — but to avoid a double-call race, keep the durable mark in ONE place: `PhonePage.OnVoicemailHeard`. Remove the direct `await GvBridgeApi.MarkVoicemailReadAsync(Item.Id);` line from `VoicemailPlayer.MarkHeardOnceAsync` (leave only `await OnHeard.InvokeAsync();`). This makes `OnVoicemailHeard` the single write path (cleaner de-dupe; the optimistic flip + write-through + reconcile all live together).

- [ ] **Step 3: Build + commit**

```bash
dotnet build src/Radio.Web --configuration Release
git add src/Radio.Web/appsettings.json src/Radio.Web/Components/Pages/VoicemailPlayer.razor
git commit -m "feat(web): single write path for voicemail mark-read; no unread toggle (ADR-024 §6)"
```

---

## Chunk 7: Documentation

### Task 7: Update FUTURE-WORK + INTEGRATIONS

**Files:**
- Modify: `design/FUTURE-WORK.md`
- Modify: `design/INTEGRATIONS.md`

- [ ] **Step 1: FUTURE-WORK** — replace the GV-2/GV-3 "mark-read seam wired-but-no-op / GV mark-read requested from RotaryPhone" entries with the shipped state: **mark-read is now durable (GV write-through, ADR-024)** behind `RotaryPhone:Gv:MarkReadEnabled` (default off). It lights up when RotaryPhone's owner-HELD build deploys (flip the flag in `appsettings.Production.json` — deploy overwrites `appsettings.json`). Record the two deferred/external items as informational (not blocking):
  - **Unread support** — `isRead:false` may `400 unread_unsupported` until RotaryPhone's live capture confirms it; UI toggle stays hidden. One UI change when they confirm, no contract change.
  - **Path (b) — phone→kiosk LIVE push** — RotaryPhone's poller-flip fast-follow. Until it ships, phone-side reads reconcile on our **next list refresh / poll**, not as an instant push. SAME `ReadStateChanged` handler covers both — no GV-4-side change when it lands.

- [ ] **Step 2: INTEGRATIONS** — under the gvbridge integration, document: the two mark routes (`POST /api/gvbridge/voicemail/{id}/read`, `POST /api/gvbridge/sms/threads/{threadId}/read`, body `{ "isRead": true }`, return the updated DTO, `404`→gone, `502`→keep-optimistic), the unified `ReadStateChanged` event on `/hub`, the de-dupe key `(id-or-threadId + isRead)`, and the **two-flag distinction**: our `RotaryPhone:Gv:MarkReadEnabled` (consumer; gates whether our seam calls the route) vs RotaryPhone's server-side `EnableMarkRead` build flag (gates whether their route exists). Note auth is auto-covered by the `/api/gvbridge/*` prefix gate (reuse `RotaryPhone:Gv:AuthKey`).

- [ ] **Step 3: Commit**

```bash
git add design/FUTURE-WORK.md design/INTEGRATIONS.md
git commit -m "docs: GV mark-read now durable (GV write-through); document routes/event/flags (ADR-024)"
```

---

## Test Plan

**Unit / component (must pass before PR):**
- `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~ReadStateReconcilerTests|FullyQualifiedName~GvBridgeApiServiceVoicemailSmsTests|FullyQualifiedName~PhoneHubServiceTests"` — green.
- Full suite + build — no regressions, 0 warnings.

**Component / unit assertions covered:**
- **Reconciler idempotency (the §9 invariant):** unread→read returns `true` + flips; already-in-state returns `false` (no-op); unknown id/threadId returns `false`; two signals with the same `(id-or-threadId + isRead)` apply once. Thread: `isRead:true` → `HasUnread:false`, second is no-op.
- **Client routes:** flag off → `null` + 0 HTTP calls (both voicemail + thread); flag on + `200` → parsed frozen DTO; `404` → `null`; `502` → `null` with exactly one attempt (no auto-retry).
- **Hub:** known `Kind` raises `ReadStateChanged`; unknown `Kind` is ignored (no raise).

**UAT (Tester, 1920×720, deploy first; `MarkReadEnabled` flows are testable with the flag flipped locally against a stub/fixture, since RotaryPhone's build is owner-HELD):**

1. **Flag OFF (default):** open a voicemail / open a thread → badge flips optimistically in-session; **no** HTTP mark call fires; a **hard reload** re-derives unread from the list endpoints' `isRead`/`hasUnread` (so a locally-flipped item reappears per server truth — expected while the flag is off and routes are unbuilt). No console errors.
2. **Flag ON, route returns 200:** open a voicemail → badge flips immediately (optimistic), the mark route fires, the returned DTO reconciles (no visible second flicker — idempotent on the same key). The Voicemail segment count + rail + topbar badge decrement and **stay** decremented across a hard reload (durable).
3. **Echo discipline (the key invariant):** with the `ReadStateChanged` echo enabled, marking one item produces the route DTO **and** the echoed broadcast → the badge flips **once**, no flicker, no double-decrement. (Verify the reconciler's no-op path: a second identical signal does not re-render.)
4. **502 path:** force the mark route to `502` (GV unreachable) → the optimistic flip **stays** (badge cleared in-session), an error is logged, **no auto-retry** fires; a subsequent successful list refresh / `ReadStateChanged` reconciles to server truth.
5. **404 path:** mark a stale id → route returns `404` → client returns `null`, optimistic flip stays for the session; the row reconciles/drops on next list refresh.
6. **SMS thread:** open an unread thread → `hasUnread` clears optimistically, `POST .../sms/threads/{id}/read` fires, returned `SmsThreadDto` reconciles; Texts segment + rail + topbar badge decrement and **survive a hard reload**.
7. **No unread toggle:** confirm there is no "mark unread" affordance anywhere in the voicemail player/row or the texts surface (§6).
8. **Phone→kiosk reconcile (path b not yet live):** simulate an externally-read item appearing read on the next list refresh → the kiosk badge clears on refresh (not instant) — confirms the "until path (b)" behavior; no extra wiring needed.
9. **Music never pauses / no modal** on any mark or `ReadStateChanged` (unchanged hard rule from ADR-022 §6.1).

**Self-review checklist (Planner ran):**
- The de-dupe invariant is a first-class, isolated, unit-tested helper (`ReadStateReconciler`), keyed by `(id-or-threadId + isRead)` (ADR-024 §9). Returned DTO + echoed broadcast + 502-deferred reconcile all collapse to one badge state with no flicker.
- Client: `200`→frozen DTO, `404`→null, `502`/non-2xx→null with optimistic flip kept, **no client-side auto-retry** (one attempt per user action).
- No local read-state store (no SQLite/JSON/localStorage/static dict); list endpoints' `isRead`/`hasUnread` are source-of-truth on reload (the GV-2 `_locallyHeard` / GV-3 `_locallyReadThreads` seeding is removed). Per-circuit optimistic flip is presentation-only.
- `ReadStateChanged` is on `PhoneHubService` (`/hub`), NOT `GvTrunkHubService`; unknown `Kind` ignored defensively.
- `RotaryPhone:Gv:MarkReadEnabled` is the single in-tree flag name (no third name), default off; distinct from RotaryPhone's server-side `EnableMarkRead`. No new auth key — reuses `RotaryPhone:Gv:AuthKey` via the existing `RotaryPhoneAuthHandler` (prefix gate auto-covers).
- No unread toggle (hidden — §6). `// UI-LOCAL only` comment corrected to `// authoritative (GV write-through); ADR-024`.
- Builds behind `MarkReadEnabled=false` against the STABLE contract now; functions once RotaryPhone deploys (owner-HELD). Path (b) is their fast-follow — same handler, no GV-4-side change.
- All literal code emitted; no `TBD`, no "similar to Task N" placeholders.
