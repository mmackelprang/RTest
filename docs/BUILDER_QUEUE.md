# Builder Queue

> Work items queued by **Planner** for **Builder** to clear one PR per cycle.
> Planner appends rows + spec/plan links; Builder claims a 📋 row whose dependencies are all met, ships it as a PR, then marks it ✅.
>
> **Last updated:** 2026-06-20 (Planner) — queued the GV Messages (Voicemail + SMS) arc (PR1/PR2/PR3).

## Status legend

| Mark | Meaning |
|---|---|
| 📋 | Queued — ready to claim (all dependencies met) |
| 🔒 | Blocked — waiting on a dependency row |
| 🚧 | In flight — a Builder cycle is working it on a branch |
| ✅ | Done — merged to `main` |

---

## Queue

| # | Item | Status | Plan | Spec / handoff | Depends on | Branch |
|---|------|--------|------|----------------|-----------|--------|
| GV-1 | **GV Messages PR1 — Foundation + IA shell.** DTOs + `GvBridgeApiService` voicemail/SMS read methods (delete stale "no SMS routes" comment) + absolute audio-URL builder; `PhoneHubService` `GvSmsReceived`/`GvVoicemailReceived`; `GvBridgeStatusService` (~10s poll); `RotaryPhoneAuthHandler` seam (OFF); `PhoneUnreadState`; config keys + DI; restructure `PhonePage` into the unified Messages feed (segmented filter + "More ▸" rail + missed-call badge, calls folded in). | 📋 | [`plans/.../pr1-foundation-ia-shell.md`](superpowers/plans/2026-06-20-gv-messages-pr1-foundation-ia-shell.md) | [handoff](design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [ADR-022](../design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md) | — | `feat/gv-messages-pr1-foundation` |
| GV-2 | **GV Messages PR2 — Voicemail surface.** Voicemail rows (all list states) + inline accordion `VoicemailPlayer` (seekable Range scrubber, buffering/playing/paused/ended/audio-error incl. 502, transcript present/pending/absent) + `GvVoicemailReceived` new-arrival (never steal screen / never pause audio) + reconnecting-banner gate + UI-local mark-heard with flagged no-op `MarkVoicemailReadAsync` seam (decision 4). | 🔒 | [`plans/.../pr2-voicemail-surface.md`](superpowers/plans/2026-06-20-gv-messages-pr2-voicemail-surface.md) | [handoff Screen A/B](design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [ADR-022 D4/D5/D6](../design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md) | GV-1 | `feat/gv-messages-pr2-voicemail` |
| GV-3 | **GV Messages PR3 — Texts surface.** Thread-list rows (all states) + master-detail conversation (inbound/outbound bubbles, append-in-place on inbound when open — no toast) + `GvSmsReceived` + compose/reply + new-recipient composer + on-screen `TouchKeyboard` **all feature-flagged OFF** via `GvBridgeSendService` (`SendNotAvailableException` until `RotaryPhone:Gv:SendEnabled` + endpoint ship) with optimistic/sending/sent/failed-preserve-text + 429/in-flight/degraded guardrails. Includes the "open a thread back to RotaryPhone" deliverable (GV mark-read, audio-endpoint auth posture, send-shape confirmation). | 🔒 | [`plans/.../pr3-texts-surface.md`](superpowers/plans/2026-06-20-gv-messages-pr3-texts-surface.md) | [handoff Screen C/D](design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [ADR-022 D5/D7/§8](../design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md) | GV-1 | `feat/gv-messages-pr3-texts` |

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
- **GV mark-read persistence** — wire `MarkVoicemailReadAsync` / thread mark-read when RotaryPhone ships the endpoint (decision 4).
- **Audible new-text chime** — belongs in the Radio.API audio layer (ducking-aware), not Blazor (handoff).
