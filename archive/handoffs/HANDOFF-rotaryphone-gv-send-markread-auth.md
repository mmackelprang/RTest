# Handoff — RotaryPhone GV send / mark-read / audio-auth (status + open items)

**From:** RadioConsole (Web) — GV Messages PR3 (Texts surface)
**Date:** 2026-06-21
**Status:** REFERENCE / STATUS, not a fresh outbound request. The RotaryPhone session's
reply has **already been received**; GV mark-read is now tracked as its own queue
item **GV-4** (see `docs/BUILDER_QUEUE.md` and ADR-024). This document records what
RadioConsole needs from RotaryPhone so the flagged seams in PR3 can light up, and
captures the resolution of each item. Nothing here is being re-sent.

---

## Context

PR3 ships the **Texts** half of the `/phone` Messages feed: thread-list rows,
the master-detail conversation (inbound/outbound bubbles, append-in-place on
inbound when the thread is open), the `GvSmsReceived` push path, and a full
compose / reply / new-recipient composer that is **built but feature-flagged
OFF** (`RotaryPhone:Gv:SendEnabled=false`). The send write-path
(`GvBridgeSendService.SendAsync`) throws `SendNotAvailableException` until the
flag flips AND RotaryPhone's send endpoint ships.

Everything in PR1/PR3 except SMS **send** is functional today against the
existing read endpoints. The items below are the remaining cross-service
dependencies.

---

## Open items (with resolution status)

### 1. GV mark-read — RESOLVED → tracked as GV-4

RadioConsole originally requested that GV **mark-read** be pulled forward so
voicemail "heard" and thread "read" could persist (today it is UI-local only: a
hard reload re-derives unread from the server's `isRead` / `hasUnread` fields,
so two browsers/circuits won't agree until a reload).

**Resolution:** the RotaryPhone reply ratified the contract (ADR-024). Durable
read-state is now its own queued item **GV-4** — flips read-state from UI-local
to **durable via GV write-through** (Google is the single source of truth; no
local store). GV-4 ships behind `RotaryPhone:Gv:MarkReadEnabled` (default off)
and lights up when RotaryPhone's owner-HELD build deploys. Endpoints per
ADR-024: `POST /api/gvbridge/voicemail/{id}/read` and
`POST /api/gvbridge/sms/threads/{threadId}/read`, with a unified
`ReadStateChanged` SignalR event reconciled idempotently by
`(id-or-threadId + isRead)`. **No action needed from RotaryPhone here beyond the
already-planned build** — this is logged for traceability only.

### 2. Voicemail audio endpoint — keep unauthenticated (or token-in-query)

When the inter-service auth gate (`X-RotaryPhone-Auth`) ships, the **voicemail
audio endpoint must stay reachable by a native `<audio>` element**, which
**cannot** send a custom request header. Keep
`GET /api/gvbridge/voicemail/{id}/audio` either **unauthenticated** or accepting
a **token-in-query-string** so the direct-`<audio src>` binding (PR2) keeps
working. If it ever becomes header-auth-required, RadioConsole must switch to a
proxied/streamed fetch — a larger change to avoid.

Reference: ADR-022 §8.1 / contract risk #4.

### 3. Confirm `POST /api/gvbridge/sms/send` request + response shape

Before RadioConsole flips `RotaryPhone:Gv:SendEnabled=true` and wires send for
real, confirm the request/response contract:

- **Request (current assumption):** `SendSmsRequest { ThreadId, Text }` — JSON
  body to `POST /api/gvbridge/sms/send`.
- **Response (current assumption, PROVISIONAL):** `SendSmsResponse { Message, Error }`
  where `Message` is the created `SmsMessageDto` (`Id, ThreadId, Direction,
  CounterpartyNumber, Text, SentAt, IsRead`). `GvBridgeSendService.SendAsync`
  reads `result.Message` and uses its `Id` to de-dupe the optimistic bubble
  against the eventual push/poll. **A shape mismatch silently breaks the
  de-dupe** (the optimistic bubble would never collapse), so confirm field
  names + the created-message id before go-live.
- **New-recipient sends:** RadioConsole currently passes the raw recipient
  number as the `threadId` when no thread exists yet. Confirm whether send
  should accept a recipient number (vs. a thread id) for first-contact sends,
  or whether RotaryPhone resolves/creates the thread server-side and echoes it.
- **Status codes:** `429` → `SendRateLimitedException` (RadioConsole preserves
  the text, shows "Sending too fast", never auto-retries); any non-2xx →
  generic failure (text preserved, "Message not sent" toast).

Reference: ADR-022 §7 / contract risk #5.

### 4. Field-value corrections from the live GV capture

The `direction` / `text` (SMS) and `durationSeconds` (voicemail) fields are
marked **provisional** in the contract. RadioConsole already codes defensively
(`direction` not exactly `"Outbound"` → inbound; `text == null` → "(no text)"
placeholder; `durationSeconds == 0` → "unknown", never `0:00`). If the live
Google Voice capture reveals different field names, casings, or value domains
(e.g. `direction` arriving as an int, or `text` nested under another key),
send the corrected shapes so RadioConsole can tighten the DTOs.

---

## What RadioConsole has ready to flip

| Seam | File | Flag | Lights up when |
|------|------|------|----------------|
| SMS send | `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` | `RotaryPhone:Gv:SendEnabled` | endpoint ships + `SendSmsResponse` shape confirmed |
| Inter-service auth | `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs` | `RotaryPhone:Gv:AuthKey` non-empty | auth gate ships (keep audio endpoint header-free) |
| GV mark-read (GV-4) | (GV-4 plan) | `RotaryPhone:Gv:MarkReadEnabled` | RotaryPhone mark-read endpoints deploy |

All three are wired OFF today and require **no further RadioConsole code** to
enable beyond the config flip (send additionally needs the shape confirmation in
item 3).
