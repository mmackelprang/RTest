# ADR: GV Mark-Read / Durable Read-State — GV write-through (supersedes ADR-022 D4)

- **ID:** ADR-024 (see `design/DECISION-LOG.md` for the one-line pointer)
- **Status:** Accepted (Architect — ready for Planner; GV-4 builds now against stable shapes behind our flag)
- **Date:** 2026-06-20
- **Author:** Architect
- **Supersedes:** **ADR-022 Decision D4 (UI-local read-state) and the corresponding §3.4 DTO note, §10 "stays stubbed," and §12.3 open question.** Read-state is now **durable via GV write-through**, not UI-local.
- **Scope:** RadioConsole `Radio.Web` only. RadioConsole still holds **no** Google credentials and never talks to Google. The mark flows through RotaryPhone's `gvbridge` API on `radio:5004`, which writes through to Google's `api2thread/updateread`.
- **Source contract (authoritative — ratified):** `D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-markread-reply.md`
- **Originating request (traceability):** `D:/prj/RotaryPhone/docs/prompts/radioconsole-gv-markread-readstate-request.md`
- **RotaryPhone-side ADR:** `D:/prj/RotaryPhone/docs/architecture/decisions/2026-06-20-gv-markread-readstate-contract.md`
- **Parent ADR:** [`2026-06-20-gvbridge-voicemail-sms-integration.md`](2026-06-20-gvbridge-voicemail-sms-integration.md) (ADR-022)

---

## 1. Context

ADR-022 shipped voicemail "heard" / SMS thread "read" as **UI-local only** (D4 / §10): the kiosk flipped a badge in-session, but the flag was lost on reload and never reflected to Google Voice or to a second kiosk client. ADR-022 itself flagged this as the live open question (§12.3 — "pull GV mark-read forward, or stay UI-local"). The RadioConsole owner **declined UI-local as the end state** and we sent the originating request asking RotaryPhone to build a durable mark-read capability.

The RotaryPhone/GV team has now **ratified** the contract. The persistence model changed in a way that affects our data model: read-state is **GV write-through**, **Google is the single source of truth**, and **RadioConsole keeps no local read-state store**. This ADR records that change, the final routes/shapes/event, and the precise consumer-side delta the Planner builds as **GV-4** (the mark-read fast-follow to the Texts/Voicemail read work).

This is **not** a new integration — it extends the same files ADR-022 already touched (`GvBridgeApiService`, `PhoneHubService`, `ApiModels.cs`, `Program.cs`). The existing **feature-flagged no-op `MarkVoicemailReadAsync` seam** (built in GV-2) is the anchor point; GV-4 wires it to the real route and adds the SMS sibling.

---

## 2. The data-model decision (the thing that changed)

**Decision: read-state is durable via GV write-through. Google Voice is the single source of truth. RadioConsole keeps NO local read-state store. The `gvbridge` list endpoints (`isRead` on `VoicemailItemDto`, `hasUnread` on `SmsThreadDto`) are authoritative on every (re)load. The kiosk's per-session optimistic flip is presentation-only and is reconciled against server truth on the next list fetch / poll / push.**

### 2.1 Why this supersedes ADR-022 D4

| | ADR-022 D4 (superseded) | ADR-024 (this decision) |
|---|---|---|
| Persistence | UI-local, session-ephemeral | GV write-through; Google is source of truth |
| Survives reload | No | Yes — list endpoints return durable `isRead`/`hasUnread` |
| Cross-client sync | No | Yes — via `ReadStateChanged` push + authoritative reload |
| Hear-on-phone clears kiosk badge | No | Yes — GV-as-truth round-trips through the poller |
| Local store in RadioConsole | n/a (in-memory only) | **None — explicitly forbidden** (see 2.2) |

The requirement that forced GV-as-truth is #3 from the request: *hearing a voicemail on the phone must clear the kiosk badge.* That is only satisfiable when the flag lives in Google and our poller reads it back. A RotaryPhone-local store (the acceptable-fallback we offered) was **not** chosen — RotaryPhone confirmed it would be a second competing truth that still couldn't satisfy #3. So there is exactly one truth (GV), one write path (mark route → `updateread`), and one read path (list/poll reads the GV flag).

### 2.2 NO local read-state store on the RadioConsole side — load-bearing invariant

Because Google is the single source of truth and RotaryPhone keeps no store, **RadioConsole must not build one either.** Concretely:

- Do **not** persist `IsRead` / `HasUnread` to any RadioConsole store (no SQLite table, no JSON file, no `localStorage`, no static dictionary that outlives the page/circuit).
- The only in-memory state is the **per-circuit optimistic flip** the component holds between a user tap and the authoritative response — and even that is overwritten by the returned DTO / next list fetch / `ReadStateChanged` push. It is presentation, not persistence.
- On (re)load, the badge state is **whatever the list endpoint returns.** Never seed it from a cached local value.

This keeps the `IsRead` / `HasUnread` fields in the ADR-022 DTOs (`VoicemailItemDto`, `SmsThreadDto`) exactly as they are — but their **semantics change**: the ADR-022 doc-comment `// UI-LOCAL only — GV mark-read not in v1` on `VoicemailItemDto.IsRead` is now **false** and must be corrected to `// authoritative (GV write-through); see ADR-024`.

---

## 3. Final routes + shapes (ratified, frozen — embed verbatim)

Both routes: **idempotent**, **safe to retry**, body `{ "isRead": bool }` (camelCase), return the **updated frozen DTO** (not 204).

### 3.1 Route — mark voicemail read

```
POST /api/gvbridge/voicemail/{id}/read
Content-Type: application/json

{ "isRead": true }     // v1: only true is contractually supported (see §6 unread)
```

```jsonc
// 200 OK → VoicemailItemDto (frozen; byte-for-byte the read shape)
{
  "id": "string", "threadId": "string",
  "fromNumber": "+15551234567", "fromName": "string|null",
  "receivedAt": "2026-06-20T18:03:11Z", "durationSeconds": 0,
  "isRead": true,                          // authoritative
  "transcript": "string|null",
  "audioUrl": "/api/gvbridge/voicemail/{id}/audio"
}
```

### 3.2 Route — mark SMS thread read (per-thread grain)

```
POST /api/gvbridge/sms/threads/{threadId}/read
Content-Type: application/json

{ "isRead": true }
```

```jsonc
// 200 OK → SmsThreadDto (frozen)
{
  "threadId": "string",
  "counterpartyNumber": "+15551234567", "counterpartyName": "string|null",
  "lastMessageAt": "2026-06-20T18:03:11Z",
  "hasUnread": false,                      // authoritative
  "lastMessagePreview": "string|null"
}
```

### 3.3 Status codes (both routes)

| Code | When | Body | Our handling |
|---|---|---|---|
| `200 OK` | Mark applied **or already-in-state** (idempotent no-op) | updated `VoicemailItemDto` / `SmsThreadDto` | Reconcile badge from the returned DTO. Never 409 on re-mark. |
| `404 Not Found` | Unknown `{id}` / `{threadId}` | `{ "error": "..." }` | Treat as "item gone"; drop/refresh the row. Do not retry. |
| `409 Conflict` | **RotaryPhone's `GVBridge:EnableMarkRead` is `false` (feature dark).** Checked **first**, before any validation — **no GV call is made.** | `{ "error": "markread_disabled" }` | Currently indistinguishable from `502` — see the consequence note below. |
| `502 Bad Gateway` | Upstream GV `updateread` failed (auth blip / GV 5xx / timeout) | `{ "error": "..." }` | **Keep the optimistic flip; reconcile on next list/poll/push.** Never an empty 200 on failure, so we can always distinguish "marked" from "GV unreachable." Safe to retry. |

**Idempotency is guaranteed by the contract** — re-marking an already-read item returns `200` with the same DTO, not `409`. So our own mark, a retry, and the echoed broadcast are all safe. **Note the overload:** `409` here means "the feature is switched off," *not* "conflicting state." A re-mark still returns `200`.

> **⚠ Amended 2026-07-31 — the `409` dark-state row was missing from this table.** Verified live on the Ubuntu box during a UAT pass: with `GVBridge:EnableMarkRead=false`, both routes return `409 {"error":"markread_disabled"}` before touching Google. Source: `GvSmsController.MarkThreadRead` (step 0) and the voicemail sibling.
>
> **Consequence (accepted for now, tracked as GV-6):** our client maps every non-2xx to `null` and keeps the optimistic flip, so a dark server **degrades acceptably** — no crash, no wrong badge, and the next list fetch is authoritative. But the client **cannot distinguish "the feature is dark" from "GV is unreachable."** That matters in exactly one state: **config skew**, where our `RotaryPhone:Gv:MarkReadEnabled=true` while their `EnableMarkRead=false`. In that window every mark silently no-ops server-side, the optimistic flip reverts on the next refresh, and the logs give no hint whether we are misconfigured or Google is down. Since mark-read has **no user-visible error affordance by design (§6)**, the cost is entirely diagnostic — which is why this is a small follow-up, not a blocker.
>
> **Rollout order is therefore not arbitrary:** flip **theirs first**, confirm the routes stop returning `409`, then flip ours. Doing it in the other order produces the skew window above.

---

## 4. Real-time event — `ReadStateChanged` on the existing `/hub`

A **unified** `ReadStateChanged` event on the existing `/hub` `RotaryHub` (the one `PhoneHubService` already consumes). RotaryPhone is **not** shipping the split `*ReadChanged` fallback. We subscribe alongside the ADR-022 `VoicemailReceived` / `SmsReceived` handlers.

```csharp
hub.On<ReadStateChangedDto>("ReadStateChanged", OnReadStateChanged);
```

Payload (camelCase on the wire):

```jsonc
{
  "kind": "Voicemail",                    // "Voicemail" | "Sms" — treat unknown defensively
  "id": "string",                         // voicemail id when kind=Voicemail; null/empty for Sms thread-level
  "threadId": "string|null",              // thread id when kind=Sms (required); voicemail's threadId when kind=Voicemail
  "isRead": true,                         // new read-state; for Sms thread-level = "thread fully read" (!hasUnread)
  "changedAtUtc": "2026-06-20T18:05:00Z"  // ISO-8601 UTC
}
```

### 4.1 De-dupe / echo discipline (consumer invariant)

RotaryPhone broadcasts **unconditionally — including back to the originator.** So when *we* call a mark route, we get both the route's returned DTO **and** an echoed `ReadStateChanged`. The handler must therefore be **idempotent, keyed by `(id-or-threadId + isRead)`**: applying the same `(target, isRead)` twice is a no-op. This is the single most important correctness rule on our side (see §9).

### 4.2 Defensive parsing

- Unknown `kind` (anything other than `"Voicemail"` / `"Sms"`) → **ignore the event**, log at `Debug`. Never throw, never crash the hub handler. (Mirrors the ADR-022 Direction-unknown→Inbound defensiveness.)
- For `kind = "Sms"`, key off `threadId` (required); `id` may be null/empty.
- For `kind = "Voicemail"`, key off `id`; `threadId` is informational (lets us also bump the thread badge if the UI shows one).

### 4.3 Event sequencing (theirs) — what we get when

RotaryPhone ships this in two steps; **our handler is identical for both and needs no change when the second lands**:

- **Path (a) — ships with the routes (one PR):** `ReadStateChanged` fires **on a mark-route call.** This gives us **cross-client sync immediately** (two kiosk tabs agree, requirement #2). This is the piece that lands when their build is unheld.
- **Path (b) — fast-follow (separate PR, theirs):** `ReadStateChanged` also fires when **their poller detects an externally-originated read flip** (phone / GV web). This is the **"hear-on-phone clears the kiosk badge LIVE"** case (requirement #3 as an instant push). It is heavier on their side (per-item state + a second diff pass each poll) and is deferred.
- **Until path (b) ships:** the phone→kiosk case still works — just on our **next list refresh / poll-driven reconcile**, not as an instant push. The `GvBridgeStatusService`/list-refresh cadence already covers this; nothing extra to build on our side.

**Consumer consequence:** we wire the `ReadStateChanged` handler once (when path a lands) and it transparently starts covering path (b) when their fast-follow deploys. No second GV-4-side change.

---

## 5. Client methods — wire the existing seam + add the SMS sibling

GV-2 already built the **feature-flagged no-op `MarkVoicemailReadAsync`** seam. GV-4:

1. **Points `MarkVoicemailReadAsync` at the real route** (`POST /api/gvbridge/voicemail/{id}/read`), parses the returned `VoicemailItemDto`, reconciles the badge from the authoritative response.
2. **Adds the sibling `MarkSmsThreadReadAsync`** (`POST /api/gvbridge/sms/threads/{threadId}/read` → `SmsThreadDto`), same pattern.

Both live on the **existing `GvBridgeApiService`** (read client) — a mark is a read-state reconcile, returns a read DTO, and is unconditionally safe (idempotent, no irreversible account write the way SMS *send* is). They do **not** go in `GvBridgeSendService`. Proposed signatures:

```csharp
// On GvBridgeApiService (existing file). Both behind RotaryPhone:Gv:MarkReadEnabled.
// v1 callers pass isRead: true only (see §6).
Task<VoicemailItemDto?> MarkVoicemailReadAsync(string id, bool isRead = true, CancellationToken ct = default);
Task<SmsThreadDto?>     MarkSmsThreadReadAsync(string threadId, bool isRead = true, CancellationToken ct = default);
```

Error handling, consistent with the ADR-022 client convention:
- `200` → return the DTO; caller reconciles.
- `404` → return `null`; caller treats as "item gone."
- `502` (and any non-2xx) → return `null` **but the caller keeps the optimistic flip** and lets the next list/poll/push reconcile. Log at `Error`. **Do not auto-retry inside the client** (the contract is retry-safe, but a UI-driven retry is the right place — a wedged GV would otherwise spin). One attempt per user action.

**Feature flag:** `RotaryPhone:Gv:MarkReadEnabled` (default `false`) — this is the **in-tree consumer key GV-2 already wired** (`GvBridgeApiService.MarkVoicemailReadAsync` gates its HTTP call on it). Same pattern as `RotaryPhone:Gv:SendEnabled`. **Do not rename it** and do not confuse it with RotaryPhone's **server-side `EnableMarkRead` build flag** (the reply's line 171) — that one gates *their* route from shipping and is not a RadioConsole config key. Two flags, two sides: theirs gates the route's existence; ours (`MarkReadEnabled`) gates whether our seam calls it. While ours is off, the seam stays a no-op (session-optimistic only) so GV-4 can build, test, and merge **before** RotaryPhone's build is unheld. Flip ours to `true` when their routes deploy. This is why **GV-4 is not blocked on their owner-hold** — it builds against these stable shapes now and lights up via the flag.

---

## 6. Unread — best-effort, hidden in UI for v1

- v1 sends **only `{ "isRead": true }`.** Mark-read is the contract.
- `{ "isRead": false }` (unread) is best-effort and **may return `400 unread_unsupported`** until RotaryPhone's live GV capture confirms `updateread` accepts an unread transition.
- **Keep any unread toggle hidden in the UI.** The `isRead` parameter on the client methods exists for forward-compatibility, but no UI affordance calls it with `false` in v1. RotaryPhone will notify when unread is confirmed; we light it up then (one UI change, no contract change).

---

## 7. Auth — nothing new

RotaryPhone's PR5 inter-service gate is **prefix-based over `/api/gvbridge/*`**, so the two mark routes are **auto-covered** the moment `GVBridge:InterServiceAuthKey` is set on RotaryPhone — no per-route config. Same `X-RotaryPhone-Auth` posture as the existing read routes: off today (LAN-only, no header); when the key is set, the `RotaryPhoneAuthHandler` seam from ADR-022 §8.1 injects the header on these POSTs automatically (they ride the same `GvBridgeApiService` `HttpClient`). **The ADR-022 §12.1 / §8.1 worry about per-route auth for mark-read is resolved — no special posture, no new wiring.** (The `<audio>`-element auth caveat in ADR-022 §8.1 is unrelated and still stands — these mark routes are plain `fetch`/`HttpClient` POSTs that *can* carry the header, so they are unaffected.)

---

## 8. Consumer-side component delta (for the Planner — this is the GV-4 build list)

All changes are additive to files ADR-022 already established. **No new topology, no new client, no new hub, no local store.**

1. **`GvBridgeApiService` (extend, existing file):**
   - Repoint **`MarkVoicemailReadAsync`** (existing no-op seam) at `POST /api/gvbridge/voicemail/{id}/read`; parse `VoicemailItemDto`.
   - **Add `MarkSmsThreadReadAsync`** → `POST /api/gvbridge/sms/threads/{threadId}/read`; parse `SmsThreadDto`.
   - Both behind `RotaryPhone:Gv:MarkReadEnabled`; `200`→DTO, `404`→null, `502`/non-2xx→null (keep optimistic flip, no auto-retry).
2. **`ReadStateChangedDto` (new record in `ApiModels.cs`):** `{ string Kind, string? Id, string? ThreadId, bool IsRead, DateTime ChangedAtUtc }`. Defensive — unknown `kind` ignored.
3. **`PhoneHubService` (extend, existing file):** add `.On<ReadStateChangedDto>("ReadStateChanged", …)` on the **existing `/hub` connection** + an `event Action<ReadStateChangedDto>? ReadStateChanged` the Voicemail/Texts components subscribe to. Handler is idempotent, **keyed by `(id-or-threadId + isRead)`**. Place it on `PhoneHubService` specifically (the `/hub` consumer), **not** `GvTrunkHubService`.
4. **Drop UI-local read-state behavior:** remove any session-persistent / cached read flag; treat list-endpoint `isRead`/`hasUnread` as source-of-truth on (re)load. Correct the now-false `// UI-LOCAL only` comment on `VoicemailItemDto.IsRead` (and any equivalent on the SMS side) → `// authoritative (GV write-through); ADR-024`. Per-circuit optimistic flip is the only allowed in-memory state.
5. **Config:** add `RotaryPhone:Gv:MarkReadEnabled` (default `false`, `appsettings.json`; flip per-machine in `appsettings.Production.json` when RotaryPhone deploys). No new auth key — reuses `RotaryPhone:Gv:AuthKey` seam from ADR-022 §8.
6. **UI wiring (Designer/Planner):** voicemail "mark heard" and SMS thread "mark read" affordances call the seam; on success reconcile from the returned DTO; on `502` keep the optimistic flip and let reconcile fix it. **No unread toggle** (hidden — §6). New arrival / badge interaction is unchanged from ADR-022 §6.1 (never steal screen / pause audio).

**De-dupe key (state it once, loud):** `(id-or-threadId, isRead)`. Both the mark-route's returned DTO **and** the echoed `ReadStateChanged` resolve to the same key; applying twice is a no-op. This is what makes "broadcast unconditionally, including to the originator" safe.

**Build order suggestion:** `ReadStateChangedDto` → `GvBridgeApiService` mark methods → `PhoneHubService` handler + de-dupe → UI wiring → flag config. All shippable as one PR (GV-4) behind `MarkReadEnabled=false`; flip the flag when RotaryPhone deploys.

---

## 9. The single most important thing the Planner must get right

**Idempotent reconciliation keyed by `(id-or-threadId + isRead)`, because RotaryPhone broadcasts `ReadStateChanged` unconditionally — including back to the client that initiated the mark.** Every mark therefore produces *two* signals on our side (the route's returned DTO and the echoed broadcast), and `502` adds a third path (keep the optimistic flip, reconcile later). If the handler is not idempotent on that key, the badge will flicker / double-apply / fight the optimistic flip. Everything else here is mechanical; this is the one correctness invariant.

---

## 10. GV-2 forward-compatibility (assessment)

GV-2 ships the flagged no-op `MarkVoicemailReadAsync` seam + "UI-local mark-heard" **before** these routes are live. **Verdict: GV-2 stays forward-compatible — no plan change required, provided one guardrail holds.**

- **Compatible as-is:** GV-2's seam is the exact anchor GV-4 repoints. The flag (`MarkReadEnabled` off) keeping it a no-op is correct. Session-ephemeral optimistic flip is fine and is what GV-4 keeps.
- **The one guardrail (must hold):** GV-2 must **not** build a *persistent* local read-state store — no SQLite table, no JSON file, no `localStorage`, no static/singleton dictionary that survives reload/circuit. ADR-022 D4 said "UI-local state only," which is ambiguous between "session-ephemeral" (fine) and "persisted locally" (would become a competing truth GV-4 must rip out). **Planner action:** confirm GV-2's plan implements the optimistic flip as **per-circuit/in-memory only**. If GV-2's plan already says session-ephemeral / in-memory, **no change**. If it is silent or leans toward persistence, add a one-line constraint: *"read-state optimistic flip is in-memory per circuit; do NOT persist — superseded by ADR-024 GV write-through."*
- Net: GV-2 needs **at most a one-line clarifying constraint**, likely **none**. It does not need rework, and it can ship before GV-4.

---

## 11. Consequences

**Good:**
- One source of truth (Google), one write path, one read path. Hear-on-phone clears the kiosk badge; cross-client sync is real.
- Zero new topology — extends `GvBridgeApiService`, `PhoneHubService`, `ApiModels.cs`, `Program.cs` (same files ADR-022 established). No local store to maintain or reconcile.
- GV-4 is **not** blocked by RotaryPhone's owner-hold: it builds against frozen shapes behind `MarkReadEnabled=false` and lights up in one flag flip.
- Idempotent, retry-safe contract; `502`-keeps-optimistic-flip means a GV blip never loses a user's intent silently.

**Bad / costs:**
- Every kiosk mark depends on a RotaryPhone→Google round-trip; a GV outage means marks don't durably land until GV recovers (mitigated: optimistic flip + reconcile-on-recovery, and `502` is explicit so we never show a false "marked").
- Until RotaryPhone's poller-flip fast-follow (path b) ships, phone→kiosk read reflects only on the next list refresh/poll, not as an instant push. Acceptable; no extra work on our side when it lands.
- `RotaryPhone:Gv:MarkReadEnabled` must be flipped in `appsettings.Production.json` (deploy overwrites `appsettings.json`) at the moment RotaryPhone deploys — an operational coordination point, not a code change.

**Open questions:**
1. **Unread support** — `isRead:false` may `400 unread_unsupported` until RotaryPhone's live capture confirms it. UI toggle stays hidden until they confirm. (For the RotaryPhone owner; blocks nothing today.)
2. **Path (b) timing** — when RotaryPhone's poller-flip fast-follow deploys, the phone→kiosk-live case upgrades from "next-refresh" to "instant push" with no change on our side. Informational; not blocking.

---

### Handoff

- **Designer** consumes: §6 (no unread toggle in v1), §4.3/§10 (phone→kiosk reflects on next refresh until path b — set the badge-clear expectation accordingly), and the unchanged ADR-022 §6.1 "never steal screen / pause audio" rule for the mark interaction.
- **Planner** consumes: §8 (the GV-4 build list), §9 (the de-dupe invariant), §10 (GV-2 guardrail), §5 (flag + client signatures), §3 (routes/shapes/status codes). GV-4 is one PR behind `MarkReadEnabled`, fast-follow to the Texts/Voicemail read work.
