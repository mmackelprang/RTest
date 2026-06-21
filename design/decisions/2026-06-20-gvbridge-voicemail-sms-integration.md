# ADR: Google Voice Voicemail + SMS Integration on RadioConsole (gvbridge consumer)

- **ID:** ADR-022 (see `design/DECISION-LOG.md` for the one-line pointer)
- **Status:** Proposed (Architect — ready for Designer + Planner)
- **⚠ Partially superseded by [ADR-023](2026-06-20-gv-mark-read-durable-readstate.md) (2026-06-20):** read-state is now **durable via GV write-through**, not UI-local. ADR-023 supersedes this ADR's **read-state stance** — specifically the `// UI-LOCAL only — GV mark-read not in v1` note on `VoicemailItemDto.IsRead` (§4.2), the **§10 "Voicemail mark-read / delete — UI-local state only"** stub, and the **§12 open question #3**. **Note:** the "D4" in the §2 summary table is the *voicemail-audio-URL* decision and is **unaffected** — it still stands. Everything else in ADR-022 (boundary, audio URL, hub, status poll, send flag, config, auth seam) is unchanged. Read ADR-023 for the mark-read routes/event/de-dupe.
- **Date:** 2026-06-20
- **Author:** Architect
- **Scope:** RadioConsole `Radio.Web` only. RadioConsole holds **no** Google credentials and never talks to Google. Everything flows through the separate RotaryPhone service (`gvbridge` API on `radio:5004`).
- **Source contract (authoritative):** `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-voicemail-sms-ui-handoff.md`
- **RotaryPhone-side ADR:** `D:/prj/RotaryPhone/docs/architecture/decisions/2026-06-20-gv-voicemail-sms-radioconsole.md`
- **Boundary doc:** `D:/prj/RotaryPhone/docs/prompts/RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`

---

## 1. Context

RotaryPhone owns the Google Voice (GV) integration end-to-end (cookies, PSIDTS rotation, media proxy). RadioConsole's kiosk UI must surface two new read-first surfaces — **Voicemail** and **Texts (SMS)** — alongside the existing phone surfaces. The read side of the RotaryPhone API is built and merged; **SMS send is not built** on the RotaryPhone side (owner-hold) and must ship behind a feature flag on our side. DTO **shapes are frozen**; some GV field **values** are provisional (transcript/text nullable, direction may be unknown, durationSeconds 0 = unknown).

### 1.1 What already exists in `Radio.Web` (verified in-tree — this is the load-bearing context)

This is **not** a greenfield integration. RadioConsole already consumes RotaryPhone, and a `GvBridgeApiService` already exists:

| Component | File | Current state |
|---|---|---|
| Phone REST client | `src/Radio.Web/Services/ApiClients/PhoneApiService.cs` | Typed `HttpClient`, base `radio:5004`, calls RotaryPhone **directly** (no Radio.API hop). `try/catch` → null/false per method. |
| GV bridge REST client | `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` | **Exists.** Today only covers `/api/gvbridge/status`, `/api/gvbridge/adapter/mode`. Has a now-**stale doc-comment** asserting "there are no SMS routes under /api/gvbridge/* and there never have been." |
| GV trunk REST client | `src/Radio.Web/Services/ApiClients/GvTrunkApiService.cs` | VoIP.ms trunk SMS via `GET /api/gvtrunk/sms` (last-20 in-memory). **Different product** from GV SMS. |
| Phone push hub | `src/Radio.Web/Services/Hub/PhoneHubService.cs` | Singleton SignalR client on `RotaryPhone:HubUrl` (= `radio:5004/hub`). Handles `CallStateChanged`, `IncomingCall`, `CallHistoryUpdated`, `SystemStatusChanged`. **This is the hub the handoff says the new GV events ride.** |
| GV trunk push hub | `src/Radio.Web/Services/Hub/GvTrunkHubService.cs` | Separate SignalR client on `radio:5004/hubs/gvtrunk`. Has its **own** `SmsReceived` event (trunk SMS, not GV SMS). |
| Web DTOs | `src/Radio.Web/Models/ApiModels.cs` | Web owns its DTO records, separate from API DTOs (e.g. `GvBridgeStatusDto`, `GvSmsNotificationDto`). |
| DI registration | `src/Radio.Web/Program.cs` | Typed `HttpClient`s for `PhoneApiService`, `GvBridgeApiService`, `GvTrunkApiService`, `DiagnosticsApiService` all bound to `RotaryPhone:ApiBaseUrl` (`radio:5004`). Hubs registered as singletons and `StartAsync()`'d at boot. |
| Album-art proxy precedent | `src/Radio.Web/Program.cs` `MapGet("/api/albumart/{filename}")` | Web→API proxy that **buffers the whole file** (`ReadAsByteArrayAsync` → `Results.File`). **No Range support.** Relevant negative precedent for the voicemail audio decision (§5). |

### 1.2 Two contract collisions this ADR must resolve

1. **`GvBridgeApiService`'s stale comment is now false.** The new contract puts **both** voicemail and GV-SMS under `/api/gvbridge/*`. The existing client comment ("no SMS routes under /api/gvbridge/*") must be deleted; the Planner must not treat it as a constraint.
2. **Two unrelated "SMS" products now coexist.** `GvTrunkApiService` / `GvTrunkHubService.SmsReceived` = **VoIP.ms trunk** SMS. The new work = **Google Voice** SMS under `/api/gvbridge/sms/*` pushed on the **`/hub` RotaryHub**, not `/hubs/gvtrunk`. These must be kept namespaced apart (`GvBridge*` vs `GvTrunk*`) so a reader never confuses them. The new Texts UI consumes GV SMS; the trunk SMS feed is a separate, pre-existing surface.

---

## 2. Decisions (summary)

| # | Decision |
|---|----------|
| D1 | **Web calls `gvbridge` directly** at `radio:5004` (no Radio.API proxy), consistent with `PhoneApiService`/`GvBridgeApiService` today and the "RotaryPhone is UI-only" rule. |
| D2 | **Extend the existing `GvBridgeApiService`** with voicemail + SMS read + status methods (do **not** create a parallel client). Add a separate, thin `GvBridgeSendService` seam only for the flagged send path. |
| D3 | **New Web DTO records** for the frozen shapes, in `Radio.Web/Models/ApiModels.cs` alongside the existing GV DTOs. Defensive nullability per the provisional-data notes. |
| D4 | **Voicemail audio: browser hits `radio:5004` directly** via the absolute URL, **not** a Web proxy. The album-art proxy pattern is explicitly rejected here because it breaks HTTP Range (the seekable scrubber). |
| D5 | **GV push rides the existing `PhoneHubService`** (`/hub` RotaryHub) — add `SmsReceived` (GV message shape) and `VoicemailReceived` handlers there. **Do not** reuse `GvTrunkHubService` (different product, different hub). |
| D6 | **A `GvBridgeStatusService` singleton** owns the ~10s `/api/gvbridge/status` poll and exposes an observable availability state the UI binds to. |
| D7 | **SMS send is one client method behind a config feature-flag** (`RotaryPhone:Gv:SendEnabled`, default `false`) that throws a typed `SendNotAvailableException` ("coming soon") today; in-flight + 429 guardrails are client concerns specified now, wired when the endpoint ships. |
| D8 | **Config keys live under `RotaryPhone:Gv:*`**; per-machine values (base host, future auth key) go in `appsettings.Production.json` because deploy overwrites `appsettings.json`. |

---

## 3. Decision D1 — Boundary: direct vs proxy

**Decision: `Radio.Web` calls `http://radio:5004/api/gvbridge/*` directly. No Radio.API (port 5000) hop.**

Rationale:
- **Consistency.** Every existing RotaryPhone consumer (`PhoneApiService`, `GvBridgeApiService`, `GvTrunkApiService`, `DiagnosticsApiService`) is a typed `HttpClient` bound to `RotaryPhone:ApiBaseUrl` and calls RotaryPhone directly. Introducing a Radio.API proxy for GV alone would be a novel, inconsistent topology.
- **"RotaryPhone is UI-only" rule.** That rule forbids registering RotaryPhone **backend services** in `Radio.Web` (i.e. don't run RotaryPhone's own services in-process) and mandates consuming via REST/SignalR. A direct typed `HttpClient` is exactly that — a pure consumer. It does **not** violate the rule; proxying through Radio.API would add a hop without adding isolation.
- **Radio.API adds nothing here.** Radio.API owns the audio engine/hardware. GV voicemail/SMS is pure RotaryPhone state with no audio-engine involvement (the voicemail recording plays in the browser's `<audio>`, not through the SoundFlow pipeline). A proxy would only add latency and a second failure point.

**One exception is forced by browser mechanics — see D4** (voicemail audio still goes direct to `radio:5004`, but the reasoning differs: it's about Range, not about the API boundary).

---

## 4. Decisions D2/D3 — API client shape + DTOs

### 4.1 D2 — Extend `GvBridgeApiService`

Add read methods to the **existing** `GvBridgeApiService` (same file, same typed-`HttpClient` registration, same `try/catch → null` convention as `PhoneApiService`). New method signatures:

```csharp
// Voicemail
Task<VoicemailListDto?>        GetVoicemailsAsync(int count = 20, string? pageToken = null, CancellationToken ct = default);
Task<VoicemailItemDto?>        GetVoicemailAsync(string id, CancellationToken ct = default);
string                         GetVoicemailAudioUrl(string id);  // builds ABSOLUTE radio:5004 URL for <audio src>; see D4

// SMS (read)
Task<SmsThreadListDto?>        GetSmsThreadsAsync(int count = 20, CancellationToken ct = default);
Task<SmsThreadMessagesDto?>    GetSmsThreadMessagesAsync(string threadId, int count = 50, CancellationToken ct = default);

// Status (already partially present — extend the DTO, see §4.4)
Task<GvBridgeStatusDto?>       GetStatusAsync(CancellationToken ct = default);  // exists
```

Error handling:
- **List/item/threads:** keep the existing `try/catch → null` so the UI renders its error+Retry state on null (matches `PhoneApiService`). Log at `Error`.
- **Audio endpoint 502:** the audio URL is consumed by the browser's `<audio>` element, not by `GvBridgeApiService` (see D4), so the **502 surfaces as an HTML5 `error` event** the Designer handles as "audio-error," not as a C# exception. The service never reads the audio bytes.
- **Status:** unchanged `try/catch → null`; null = degraded (drives the reconnecting banner, §6).

The send method (`SendSmsAsync`) is **not** added to `GvBridgeApiService` — it lives behind a separate seam (D7/§7) so the read client stays unconditionally safe and the flagged write path is isolated and easy to find.

Delete the stale "no SMS routes under /api/gvbridge/*" doc-comment as part of this work.

### 4.2 D3 — New Web DTO records (in `src/Radio.Web/Models/ApiModels.cs`)

Mirror the frozen shapes. Use `record` types (the file already mixes `class` DTOs and records; records are preferred for new immutable response shapes per the boundary doc's "prefer typed records" guidance). All fields nullable exactly where the contract says, with defensive defaults:

```csharp
// ── GV Voicemail ──────────────────────────────────────────────
public record VoicemailItemDto(
    string Id,
    string ThreadId,
    string FromNumber,            // E.164
    string? FromName,             // null → UI shows number / contact lookup
    DateTime ReceivedAt,         // UTC; format to local for display
    int DurationSeconds,         // 0 = unknown → do NOT render "0:00" as real
    bool IsRead,                 // SUPERSEDED by ADR-023: authoritative (GV write-through), NOT UI-local
    string? Transcript,          // null = pending/absent
    string AudioUrl);            // RELATIVE from server: "/api/gvbridge/voicemail/{id}/audio"

public record VoicemailListDto(
    IReadOnlyList<VoicemailItemDto> Items,
    string? NextPageToken,       // null = no more pages
    DateTime FetchedAtUtc);

// ── GV SMS ────────────────────────────────────────────────────
public record SmsMessageDto(
    string Id,
    string ThreadId,
    string Direction,            // "Inbound" | "Outbound"; treat UNKNOWN → Inbound
    string CounterpartyNumber,   // E.164
    string? Text,                // null → render empty/placeholder, don't crash
    DateTime SentAt,             // UTC
    bool IsRead);

public record SmsThreadDto(
    string ThreadId,
    string CounterpartyNumber,
    string? CounterpartyName,
    DateTime LastMessageAt,
    bool HasUnread,
    string? LastMessagePreview);

public record SmsThreadListDto(
    IReadOnlyList<SmsThreadDto> Threads,
    DateTime FetchedAtUtc);

public record SmsThreadMessagesDto(
    string ThreadId,
    IReadOnlyList<SmsMessageDto> Messages,
    DateTime FetchedAtUtc);

// ── Send (flagged; see D7) ────────────────────────────────────
public record SendSmsRequest(string ThreadId, string Text);
public record SendSmsResponse(SmsMessageDto? Message, string? Error);  // shape provisional until PR4 ships
```

**Defensive parsing rules the Planner must enforce (provisional data):**
- `Direction`: map any value other than `"Outbound"` (case-insensitive) → treat as **Inbound**. Do not throw on an unrecognized string. Recommend a small helper rather than an enum that fails on unknown values.
- `Transcript == null` → "Transcript pending…" if `ReceivedAt` is recent (e.g. < 30 min), else "No transcript available." (Designer owns the exact thresholds/copy.)
- `Text == null` → render as empty bubble, never crash.
- `DurationSeconds == 0` → render "—" / hide duration, not "0:00".
- All `DateTime` are UTC; format to local at render time.

### 4.3 Naming discipline (collision guard)

Keep the new types `Voicemail*Dto` / `Sms*Dto` distinct from the **pre-existing** `GvSmsNotificationDto` (trunk) and `GvTrunkCallLogEntryDto`. Do not rename or merge them — the trunk SMS surface is unrelated and still live. A code comment near both blocks should cross-reference: "GV (gvbridge) SMS ≠ VoIP.ms trunk SMS."

### 4.4 Status DTO extension

`GvBridgeStatusDto` today has `{ Available, ActiveMode }`. The handoff's `/api/gvbridge/status` is the auth-decay signal. Add fields **defensively** (RotaryPhone may not populate all yet — keep them optional/defaulted so deserialization never fails):

```csharp
public class GvBridgeStatusDto
{
  public bool Available { get; set; }
  public string ActiveMode { get; set; } = "";
  public bool SipRegistered { get; set; }   // present per boundary-doc Phase B PR1
  public bool CookiesValid { get; set; }     // present per boundary-doc Phase B PR1
}
```

The Texts/Voicemail UI treats `Available == false` (or a null status response) as **degraded** → calm reconnecting banner + Send disabled.

---

## 5. Decision D4 — Voicemail audio URL (the critical one)

**Decision: the browser's `<audio src>` points at the absolute RotaryPhone URL `http://radio:5004/api/gvbridge/voicemail/{id}/audio` — a direct hit, NOT proxied through Radio.Web or Radio.API.**

The contract guarantees `Content-Type: audio/mpeg` + `Accept-Ranges: bytes` (HTTP Range), so a normal seekable `<audio>` scrubber works against it directly. RotaryPhone has already done the proxy+cache+Range work; RadioConsole must not undo it.

**Why direct, and why NOT the album-art proxy pattern:**
1. **The album-art proxy breaks Range.** `MapGet("/api/albumart/...")` does `ReadAsByteArrayAsync()` → `Results.File(bytes, contentType)` — it buffers the entire body and does not forward `Range`/`Accept-Ranges`. For album art (a few KB, no seeking) that's fine. For a seekable voicemail scrubber it is **disqualifying**: the browser could not seek without re-downloading, and first-play buffering would be worse. Copying that precedent here would actively defeat the Designer's scrubber.
2. **No credentials are involved on our side.** Unlike a redirect to a Google media URL (which would 401 without GV cookies), `radio:5004/api/gvbridge/voicemail/{id}/audio` is RotaryPhone's own already-authenticated, already-cached proxy endpoint. RadioConsole needs nothing but the URL.
3. **CORS is a non-issue for `<audio>`.** An `<audio src="http://radio:5004/...">` is a plain media load, not a `fetch()`/XHR — it is **not** subject to CORS preflight, and Range requests on media elements work cross-origin without `Access-Control-*` headers. (This only matters because Web is served from `:5002` and audio from `:5004`.) **Contract note to verify:** confirm RotaryPhone sets no `Access-Control-Allow-Origin` restriction that would interfere; for a bare `<audio>` element it should not matter, but if the Designer ever switches to a `fetch`-and-blob approach, CORS would then bite — so keep it a native `<audio>` element.
4. **Base-host resolution.** The DTO's `AudioUrl` is **server-relative** (`/api/gvbridge/voicemail/{id}/audio`). If bound to `<audio src>` as-is, the browser resolves it against the **Web origin (`:5002`)**, which would 404. Therefore `GvBridgeApiService.GetVoicemailAudioUrl(id)` (or a small razor helper) must **prefix the RotaryPhone base host** to produce an absolute `radio:5004` URL. This is the single most common way this feature would silently break — call it out for the Planner. Do **not** trust the relative `AudioUrl` field for the `src`; rebuild it absolute from `RotaryPhone:ApiBaseUrl` + the `{id}`.

**Consequence:** the kiosk browser must be able to reach `radio:5004` directly (it can — same LAN, same box). If RadioConsole is ever served to a client that cannot reach `:5004` directly, a Range-forwarding proxy on Web/API would be required — explicitly out of scope today and noted as a future constraint.

---

## 6. Decisions D5/D6 — SignalR push + status polling

### 6.1 D5 — Extend `PhoneHubService` (the `/hub` RotaryHub)

The handoff is explicit: GV events ride the **existing `RotaryHub`** that `PhoneHubService` already consumes (`RotaryPhone:HubUrl` = `radio:5004/hub`) — **not** `GvTrunkHubService`'s `/hubs/gvtrunk`. Add two handlers + two events to `PhoneHubService`:

```csharp
public event Action<SmsMessageDto>? GvSmsReceived;       // new GV inbound SMS
public event Action<VoicemailItemDto>? GvVoicemailReceived;

// inside StartAsync(), alongside the existing .On(...) registrations:
_hubConnection.On<SmsMessageDto>("SmsReceived", m => GvSmsReceived?.Invoke(m));
_hubConnection.On<VoicemailItemDto>("VoicemailReceived", v => GvVoicemailReceived?.Invoke(v));
```

**Naming-collision risk to flag:** the GV event name on the wire is `"SmsReceived"`, and `GvTrunkHubService` **also** has an `"SmsReceived"` handler — but they're on **different hub connections** (`/hub` vs `/hubs/gvtrunk`) carrying **different payloads** (`SmsMessageDto` vs the trunk notification). So there is no runtime collision, but the C# event on `PhoneHubService` should be named **`GvSmsReceived`** (not `SmsReceived`) to keep the two unambiguous to readers. The Planner must place the GV handler on `PhoneHubService` specifically.

**Semantics:** push = "freshen + notify." REST remains source-of-truth on (re)load. On `GvSmsReceived`: if the relevant thread is open, append silently (no toast); else bump thread + badge + toast. On `GvVoicemailReceived`: prepend row + badge + (calm) toast. **Hard rule from the handoff: a new arrival must never steal the screen or pause audio** — Designer owns the interaction, but the Architect constraint is that the hub handler must do nothing audio-affecting.

### 6.2 D6 — `GvBridgeStatusService` singleton (status poll)

Create a new singleton service `src/Radio.Web/Services/GvBridgeStatusService.cs` that:
- Polls `GvBridgeApiService.GetStatusAsync()` every **~10s** (boundary-doc cadence), via a `PeriodicTimer` in a background loop started at app boot (mirrors how `PhoneHubService.StartAsync()` is kicked off in `Program.cs`).
- Exposes a cached `GvBridgeStatusDto? Current` + an `event Action<GvBridgeStatusDto?> StatusChanged` the Texts/Voicemail components subscribe to.
- Derives a simple `bool IsAvailable` (status non-null AND `Available`) that drives: (a) the calm reconnecting banner, and (b) **disabling Send** (combined with the feature flag, §7). Auto-recovers when status returns available — RadioConsole only reflects; RotaryPhone does the actual cookie recovery.

Why a dedicated service (not per-component polling): one poll for the whole app, shared state, no N× polling from multiple open panels. Singleton matches `AudioStateStore`/`PhoneHubService` precedent. **DI gotcha (from memory):** a singleton cannot consume a scoped/typed-`HttpClient` directly — resolve `GvBridgeApiService` via `IHttpClientFactory` or inject `IServiceScopeFactory` and create a scope per poll. The Planner must not inject the typed client straight into the singleton.

---

## 7. Decision D7 — SMS send behind a feature flag

**Decision: a single send method behind `RotaryPhone:Gv:SendEnabled` (default `false`). Today it throws `SendNotAvailableException`; the network call is wired when RotaryPhone's `POST /api/gvbridge/sms/send` ships.**

New seam `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` (kept separate from the read client so the write path is isolated and obvious):

```csharp
public class SendNotAvailableException : Exception { /* "Texting send is coming soon." */ }

public class GvBridgeSendService
{
  // Reads RotaryPhone:Gv:SendEnabled via IOptions/IConfiguration.
  // Today: if !SendEnabled → throw SendNotAvailableException (UI shows "coming soon").
  // When the endpoint ships: POST /api/gvbridge/sms/send  { threadId, text } → created SmsMessageDto.
  public Task<SmsMessageDto> SendAsync(string threadId, string text, CancellationToken ct = default);
}
```

Client-side guardrails to **specify now, enforce when wired** (these are the handoff's send rules, owned by the client):
- **In-flight guard:** disable Send + reject a second `SendAsync` while one is outstanding (single-flight per thread). Surfaced to the UI as a `bool` the compose box binds to.
- **HTTP 429:** map to a typed result/exception → UI shows "Sending too fast — wait a moment," **preserve the typed text**, **never auto-retry**.
- **Ambiguous failure:** treat non-2xx as failed, keep the typed text, surface a manual Retry. No auto-retry (a send is an irreversible account write on the GV side).
- **Degraded gate:** Send is disabled whenever `GvBridgeStatusService.IsAvailable == false`, independent of the feature flag.

The compose/reply UI (Designer's) is built now and **rendered behind the same flag** so the whole send surface lights up in one config flip when PR4 lands. The flag default-off means the read experience ships fully today.

**Open contract item:** the handoff says send returns "the created outbound message" but the exact `SendSmsResponse` shape is provisional. `SmsMessageDto` is the safe assumption; confirm when PR4 ships before wiring `SendAsync`'s response parse.

---

## 8. Decision D8 — Config surface

All new keys nest under the existing `RotaryPhone` section (which already holds `HubUrl` + `ApiBaseUrl`). **Base host and the future auth key are per-machine → `appsettings.Production.json`** (deploy overwrites `appsettings.json`). Feature flags can default in `appsettings.json` and be overridden per-machine.

| Key | Default (appsettings.json) | Per-machine (appsettings.Production.json) | Consumer |
|---|---|---|---|
| `RotaryPhone:ApiBaseUrl` | `http://radio:5004` (exists) | override if host differs | all GV REST + the absolute audio URL builder |
| `RotaryPhone:HubUrl` | `http://radio:5004/hub` (exists) | override if host differs | `PhoneHubService` (carries the new GV events) |
| `RotaryPhone:Gv:SendEnabled` | `false` | flip to `true` when RotaryPhone send endpoint ships | `GvBridgeSendService` |
| `RotaryPhone:Gv:StatusPollSeconds` | `10` | tune if needed | `GvBridgeStatusService` |
| `RotaryPhone:Gv:AuthKey` | `""` (empty = OFF; **do not send header**) | set when `X-RotaryPhone-Auth` gate ships | all GV REST + hub (see §8.1) |

### 8.1 Future auth header — wire the seam now, OFF today

The boundary doc + RotaryPhone ADR plan an optional `X-RotaryPhone-Auth: <key>` gate (default-off, LAN-only today). Design the clients so **one** place adds the header when `RotaryPhone:Gv:AuthKey` is non-empty:
- **REST:** a tiny `DelegatingHandler` (e.g. `RotaryPhoneAuthHandler`) added to the `GvBridgeApiService`/`GvBridgeSendService` `HttpClient` registrations that injects the header **only when the key is non-empty**. Today the key is empty → no header sent (honors current no-auth posture). This mirrors the existing `ApiConnectionLoggingHandler` registration pattern in `Program.cs`.
- **SignalR:** `HubConnectionBuilder.WithUrl(url, o => o.AccessTokenProvider = ...)` or a custom header — added to `PhoneHubService` **only when the key is set**.
- **Audio `<audio>` element:** a native `<audio src>` **cannot set a custom request header.** If the auth gate is ever made *required* for the audio endpoint, the direct-`<audio>` approach (D4) breaks and we'd need a Range-forwarding authenticated proxy. **Flag this now:** the auth gate must keep the **voicemail audio endpoint either unauthenticated or token-in-query**, or D4 must change. This is a real cross-service contract dependency to raise with the RotaryPhone side before they make the audio endpoint auth-required.

---

## 9. What the Planner must build

**New / changed Web components:**
1. **`GvBridgeApiService` (extend, existing file):** add `GetVoicemailsAsync`, `GetVoicemailAsync`, `GetVoicemailAudioUrl` (absolute-URL builder), `GetSmsThreadsAsync`, `GetSmsThreadMessagesAsync`; extend `GetStatusAsync`/`GvBridgeStatusDto`. Delete the stale "no SMS routes" comment.
2. **`GvBridgeSendService` (new):** flagged `SendAsync` + `SendNotAvailableException` + in-flight/429/no-auto-retry guardrails. Register typed `HttpClient` in `Program.cs` against `RotaryPhone:ApiBaseUrl`.
3. **`GvBridgeStatusService` (new singleton):** ~10s poll, `Current` + `StatusChanged`, `IsAvailable`. Resolve `GvBridgeApiService` via `IHttpClientFactory`/scope (singleton-vs-scoped gotcha). Kick off in `Program.cs` like the hubs.
4. **`PhoneHubService` (extend, existing file):** add `GvSmsReceived` / `GvVoicemailReceived` events + `.On<SmsMessageDto>("SmsReceived", …)` / `.On<VoicemailItemDto>("VoicemailReceived", …)` handlers on the `/hub` connection.
5. **DTOs in `ApiModels.cs`:** `VoicemailItemDto`, `VoicemailListDto`, `SmsMessageDto`, `SmsThreadDto`, `SmsThreadListDto`, `SmsThreadMessagesDto`, `SendSmsRequest`, `SendSmsResponse`; extend `GvBridgeStatusDto`. Defensive nullability + Direction-unknown→Inbound helper.
6. **`RotaryPhoneAuthHandler` (new `DelegatingHandler`):** header-injection seam, no-ops while `RotaryPhone:Gv:AuthKey` is empty. Add to the GV `HttpClient` registrations + `PhoneHubService` connection builder.
7. **DI registrations in `Program.cs`:** typed `HttpClient` for `GvBridgeSendService`; singleton `GvBridgeStatusService` + boot-time start; auth handler on GV clients.
8. **Config:** add `RotaryPhone:Gv:{SendEnabled,StatusPollSeconds,AuthKey}` (+ keep base host/hub). Document which go to `appsettings.Production.json`.
9. **UI surfaces (Designer specs; Planner sequences):** Voicemail list + inline player (native `<audio>` + Range scrubber, absolute `:5004` src), Texts thread list + conversation, compose/reply (flag-gated), new-arrival toast/badge, reconnecting banner.

**Build order suggestion (for Planner):** DTOs → `GvBridgeApiService` reads → `PhoneHubService` events → `GvBridgeStatusService` → Voicemail UI (incl. audio) → Texts read UI → `GvBridgeSendService` + flagged compose (last; backend-blocked). The read experience (everything except send) ships in one PR-set; send lights up later via the flag.

---

## 10. What stays stubbed / flagged

- **SMS send** — `GvBridgeSendService.SendAsync` throws `SendNotAvailableException` until `RotaryPhone:Gv:SendEnabled = true` **and** RotaryPhone's `POST /api/gvbridge/sms/send` ships. Compose UI built but flag-gated.
- **Voicemail mark-read / delete** — ~~**UI-local state only** in v1 (`IsRead` does not persist to Google). No GV-side endpoints.~~ **SUPERSEDED by [ADR-023](2026-06-20-gv-mark-read-durable-readstate.md):** mark-read is now **durable via GV write-through** (routes ratified, built as GV-4 behind `RotaryPhone:Gv:MarkReadEnabled`). **Delete remains deferred.**
- **Inter-service auth header** — seam built (`RotaryPhoneAuthHandler`, `RotaryPhone:Gv:AuthKey`), **OFF** today (empty key → no header). Lights up when the gate ships.
- **`<audio>` + auth interaction** — if the audio endpoint ever becomes auth-required, D4 must change; raised as a contract dependency (§8.1).

---

## 11. Consequences

**Good:**
- Zero new topology — reuses the existing direct-to-`radio:5004` typed-`HttpClient` + `/hub` SignalR pattern; the GV work is additive to files that already exist.
- Voicemail scrubber works out of the box (direct Range-capable audio endpoint), avoiding the album-art proxy's no-Range trap.
- Send + auth are single config flips; read experience ships fully today.
- GV-SMS vs trunk-SMS kept cleanly namespaced, reducing the real risk of a future maintainer wiring the wrong feed.

**Bad / costs:**
- The kiosk browser now depends on reaching **two** origins (`:5002` Web + `:5004` audio). Fine on the box; a constraint if the UI is ever served remotely.
- `GvBridgeStatusDto` field-additions are speculative against RotaryPhone's exact serialization — mitigated by defensive optional fields, but verify against a live `/api/gvbridge/status` response.
- A future GV API/field-position change on the RotaryPhone side can still break list parsing; our DTOs are shape-frozen by contract, so the blast radius is bounded to RotaryPhone.

**Contract risks spotted (raise with RotaryPhone session):**
1. **Stale in-tree comment** in `GvBridgeApiService` flatly contradicts the new contract — must be deleted, and the Planner warned not to treat it as truth.
2. **Two "SmsReceived" events on two hubs** (GV `/hub` vs trunk `/hubs/gvtrunk`) — no runtime collision but a documentation/naming hazard; the GV handler must land on `PhoneHubService`, named `GvSmsReceived`.
3. **Relative `AudioUrl`** in the DTO resolves against the wrong origin (`:5002`) if used as-is — must be rebuilt absolute against `RotaryPhone:ApiBaseUrl`. Most likely silent-failure point.
4. **Future audio-endpoint auth** would break the native-`<audio>` approach (headers can't be set on `<audio>`). Ask RotaryPhone to keep the audio endpoint unauthenticated or token-in-query when the `X-RotaryPhone-Auth` gate ships.
5. **`SendSmsResponse` shape provisional** — confirm before wiring `SendAsync`'s response parse when PR4 lands.

---

## 12. Open questions

1. **Audio endpoint + auth gate** — will RotaryPhone exempt `voicemail/{id}/audio` from `X-RotaryPhone-Auth`, or support a query-token? (Blocks nothing today; blocks D4 if the gate goes auth-required.) — for RotaryPhone owner.
2. **`/api/gvbridge/status` exact fields** — confirm `SipRegistered`/`CookiesValid`/`Available` serialization so the degraded-state derivation is correct.
3. **Heard/read persistence** — ~~handoff confirms v1 UI-local; OK, or pull GV mark-read forward?~~ **RESOLVED → pulled forward, durable.** See [ADR-023](2026-06-20-gv-mark-read-durable-readstate.md): GV write-through, Google is source of truth, no local store.
4. **On-screen keyboard for compose** — Designer's call (touch kiosk); does not block the read experience or this ADR.

---

### Handoff
- **Designer** consumes: the DTOs (§4.2), the audio-URL decision (D4 — native `<audio>`, absolute `:5004` src, Range scrubber), the push semantics (§6.1 — append-silently-if-open, never steal screen/pause audio), the degraded/reconnecting + Send-disabled states (§6.2/§7), and the flagged compose surface.
- **Planner** consumes: §9 component list + build order, §8 config keys, §10 stub/flag list.
