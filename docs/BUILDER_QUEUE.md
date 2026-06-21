# Builder Queue

> Work items queued by **Planner** for **Builder** to clear one PR per cycle.
> Planner appends rows + spec/plan links; Builder claims a 📋 row whose dependencies are all met, ships it as a PR, then marks it ✅.
>
> **Last updated:** 2026-06-21 (Builder) — **GV-3** (Texts surface) 🚧 implemented + reviewed on `feat/gv-messages-pr3-texts`; **PR #440 open, NOT merged** (Coordinator sequences the GV-2→GV-3 merges). Build + full Web suite green (698 tests); pre-merge review HIGH/MEDIUM fixed; live UAT deferred to a combined post-merge pass (shared box with GV-2). No `FeedItem` placeholder needed. Earlier: 2026-06-20 (Planner) — wrote the GV-4 plan (durable GV mark-read / read-state) against the now-ratified+stable contract (ADR-024) and un-held the row: GV-4 ⛔ → 🔒 (external blocker resolved; now depends only on GV-2 + GV-3). GV-4 ships behind `RotaryPhone:Gv:MarkReadEnabled` (default off) and lights up when RotaryPhone's owner-HELD build deploys. Earlier: queued the GV Messages (Voicemail + SMS) arc (PR1/PR2/PR3). GV-1 MERGED ✅ (PR #437, 2026-06-21); GV-2 + GV-3 now claimable (📋). NU1903 SQLite blocker cleared (ADR-023, PR #438).

## Status legend

| Mark | Meaning |
|---|---|
| 📋 | Queued — ready to claim (all dependencies met) |
| 🔒 | Blocked — waiting on a dependency row |
| ⛔ | On hold — blocked on an **external** dependency (another repo/team must ship first); do not claim |
| 🚧 | In flight — a Builder cycle is working it on a branch |
| ✅ | Done — merged to `main` |

---

## Queue

| # | Item | Status | Plan | Spec / handoff | Depends on | Branch |
|---|------|--------|------|----------------|-----------|--------|
| GV-1 | **GV Messages PR1 — Foundation + IA shell.** DTOs + `GvBridgeApiService` voicemail/SMS read methods (delete stale "no SMS routes" comment) + absolute audio-URL builder; `PhoneHubService` `GvSmsReceived`/`GvVoicemailReceived`; `GvBridgeStatusService` (~10s poll); `RotaryPhoneAuthHandler` seam (OFF); `PhoneUnreadState`; config keys + DI; restructure `PhonePage` into the unified Messages feed (segmented filter + "More ▸" rail + missed-call badge, calls folded in). | ✅ | [`plans/.../pr1-foundation-ia-shell.md`](superpowers/plans/2026-06-20-gv-messages-pr1-foundation-ia-shell.md) | [handoff](design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [ADR-022](../design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md) | — | `feat/gv-messages-pr1-foundation` |
| GV-2 | **GV Messages PR2 — Voicemail surface.** Voicemail rows (all list states) + inline accordion `VoicemailPlayer` (seekable Range scrubber, buffering/playing/paused/ended/audio-error incl. 502, transcript present/pending/absent) + `GvVoicemailReceived` new-arrival (never steal screen / never pause audio) + reconnecting-banner gate + UI-local mark-heard with flagged no-op `MarkVoicemailReadAsync` seam (decision 4). | 📋 | [`plans/.../pr2-voicemail-surface.md`](superpowers/plans/2026-06-20-gv-messages-pr2-voicemail-surface.md) | [handoff Screen A/B](design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [ADR-022 D4/D5/D6](../design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md) | GV-1 | `feat/gv-messages-pr2-voicemail` |
| GV-3 | **GV Messages PR3 — Texts surface.** Thread-list rows (all states) + master-detail conversation (inbound/outbound bubbles, append-in-place on inbound when open — no toast) + `GvSmsReceived` + compose/reply + new-recipient composer + on-screen `TouchKeyboard` **all feature-flagged OFF** via `GvBridgeSendService` (`SendNotAvailableException` until `RotaryPhone:Gv:SendEnabled` + endpoint ship) with optimistic/sending/sent/failed-preserve-text + 429/in-flight/degraded guardrails. Includes the "open a thread back to RotaryPhone" deliverable (GV mark-read, audio-endpoint auth posture, send-shape confirmation). _PR #440 open, awaiting Coordinator merge sequencing (after GV-2)._ | 🚧 | [`plans/.../pr3-texts-surface.md`](superpowers/plans/2026-06-20-gv-messages-pr3-texts-surface.md) | [handoff Screen C/D](design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [ADR-022 D5/D7/§8](../design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md) | GV-1 | `feat/gv-messages-pr3-texts` |
| GV-4 | **Wire GV mark-read / durable read-state.** Fast-follow that flips read-state from UI-local to **durable via GV write-through** (Google is single source of truth; NO local store): repoint the GV-2 `MarkVoicemailReadAsync` seam at `POST /api/gvbridge/voicemail/{id}/read`, add the SMS-thread sibling (`POST /api/gvbridge/sms/threads/{threadId}/read`), subscribe to the unified `ReadStateChanged` SignalR event on the existing `/hub`, reconcile **idempotently keyed by `(id-or-threadId + isRead)`** (RotaryPhone echoes unconditionally, incl. to the originator), drop UI-local seeding (list endpoints' `isRead`/`hasUnread` are source-of-truth on reload), behind `RotaryPhone:Gv:MarkReadEnabled` (default off). No unread toggle (hidden). No new auth. | 🔒 | [`plans/.../pr4-mark-read.md`](superpowers/plans/2026-06-20-gv-messages-pr4-mark-read.md) | [ADR-024](../design/decisions/2026-06-20-gv-mark-read-durable-readstate.md) · [ratification reply](file:///D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-markread-reply.md) (`D:/prj/RotaryPhone/docs/handoffs/radioconsole-gv-markread-reply.md`) | **GV-2** (`MarkVoicemailReadAsync` seam) + **GV-3** (SMS read surface). _Contract ratified + stable (ADR-024); RotaryPhone build owner-HELD, so GV-4 ships behind `RotaryPhone:Gv:MarkReadEnabled` (default off) and lights up when they deploy._ | `feat/gv-messages-pr4-mark-read` |

---

## Dependency / ordering notes

- **GV-1 is the foundation.** GV-2 and GV-3 both depend on GV-1 (DTOs, the extended `GvBridgeApiService`, the new `PhoneHubService` events, `GvBridgeStatusService`, `PhoneUnreadState`, and the `PhoneMessagesPanel` shell). Builder must merge GV-1 before claiming either.
- **GV-2 and GV-3 are independent of each other** and may ship in either order after GV-1. **Recommended order: GV-1 → GV-2 → GV-3.** One shared coupling: GV-2 introduces the `FeedItem` interleave projection (PR2 Task 5) that GV-3 reuses; if GV-3 is built first, its plan instructs the implementer to introduce that projection there instead.
- **Read experience ships fully today; send lights up later.** Everything in GV-1/GV-2/GV-3 except SMS **send** is functional now. Send is one config flip (`RotaryPhone:Gv:SendEnabled=true`) once RotaryPhone's `POST /api/gvbridge/sms/send` ships and the `SendSmsResponse` shape is confirmed.

## Carried risks (baked into the plans as explicit tasks)

1. **Absolute audio URL** — the DTO `AudioUrl` is server-relative and resolves against the Web origin (`:5002`), 404ing. `GvBridgeApiService.GetVoicemailAudioUrl` rebuilds it absolute against `radio:5004`. Unit-tested in GV-1; the most likely silent-failure point.
2. **GV SMS ≠ trunk SMS** — the GV handler is `GvSmsReceived` on `PhoneHubService`/`/hub`; the pre-existing `GvTrunkHubService.SmsReceived` on `/hubs/gvtrunk` is a different product. Kept namespaced apart; cross-referenced in code comments (GV-1).
3. **Open thread to RotaryPhone** (GV-3 deliverable): request GV mark-read be pulled forward (decision 4), keep the voicemail audio endpoint unauthenticated/token-in-query when `X-RotaryPhone-Auth` ships (native `<audio>` can't send the header), and confirm `SendSmsResponse` before wiring send.

## Documented fast-follows (NOT in these PRs)

- **Voicemail Call back / Text back quick actions** (decision 3) — deferred; markers left in the player.
- **GV mark-read / durable read-state** — now **planned + un-held** as **GV-4** (🔒, depends only on GV-2 + GV-3). Contract ratified + stable (ADR-024): durable via GV write-through, Google is single source of truth, no local store. Wires `MarkVoicemailReadAsync` (repointed) + `MarkSmsThreadReadAsync` + the unified `ReadStateChanged` SignalR event on `/hub`, reconciled idempotently by `(id-or-threadId + isRead)`. Ships behind `RotaryPhone:Gv:MarkReadEnabled` (default off; RotaryPhone's build is owner-HELD — flip the flag when they deploy). Two RotaryPhone-side follow-ups remain informational (not blocking GV-4): **unread support** (`isRead:false` may `400 unread_unsupported` until their live capture — UI toggle stays hidden) and **path (b)** the phone→kiosk LIVE push (their poller-flip fast-follow; until then phone-side reads reconcile on our next list refresh — same handler, no GV-4-side change).
- **Audible new-text chime** — belongs in the Radio.API audio layer (ducking-aware), not Blazor (handoff).
