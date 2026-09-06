# GV-5 — PARKED 2026-09-05 BY OWNER DECISION `D31` — NEVER CLAIM.

> Queue dossier for row **`GV-5`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 🚫 |
| Plan | [`plans/.../pr5-send-contract.md`](../superpowers/plans/2026-07-30-gv-messages-pr5-send-contract.md) _(kept for reconstruction; do not execute)_ |
| Spec / handoff | [ADR-028](../../design/decisions/2026-07-30-gv-sms-send-contract.md) · [handoff §send/bubble states](../design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) |
| Depends on | **GV-3** (`GvBridgeSendService`, compose/reply, `MessageBubble`, optimistic-append seam). _No external blocker: `POST /api/gvbridge/sms/send` is **shipped**. Their `GVBridge:EnableSmsSend=false` does **not** block — a dark server returns `409 send_disabled`, which this PR handles as a first-class state. **Merges as a user-visible no-op** (our flag off → compose unreachable, no new calls); the only always-on delta is the inert `SmsSent` subscription._ |
| Branch | `feat/gv-messages-pr5-send-contract` |

## Detail

🚫 **PARKED 2026-09-05 BY OWNER DECISION `D31` — NEVER CLAIM.** The owner was asked whether SMS sending is ever meant to be enabled and answered **no — replies stay off**.

**The reason lives in [punch list §6](../HANDOFF-GA-PUNCH-LIST.md); the decision is `D31` in §7.** This row's own value statement is what retires it — the punch list called it *"the row that unblocks ever turning send on"*, and nothing else.

**Nothing here is a defect on the read surface**, which is the test that separates it from `GV-9` / `GV-10`.

⚠ **Send was already dead behind two gates, so parking this breaks nothing:** the flag is `false` on every box (`appsettings.json:21`, `appsettings.Production.json:6`) **and** `SendSmsRequest` (`ApiModels.cs:1186`) omits the required `ToNumber`, which is absent from `src/` and `tests/` entirely — a flag flip alone would have produced `400 invalid_number` on **every** send.

**The plan and ADR-028 below are KEPT, not deleted** — they are the reconstruction path if `D31` is ever reversed, and they cost nothing sitting still.

⚠ **`GV-7` is NOT stranded by this** — it is re-scoped to drop the `GvCounterparty` dependency rather than parked alongside; see its row. *Original row preserved verbatim below, because a reversal restores it as written:* **Reconcile the GV SMS send contract.** Replace GV-3's *anticipated* send contract with RotaryPhone's **as-built** one (ADR-028). Fixes the **request** shape — ours omits their required `ToNumber`, which binds `null` server-side and returns `400 invalid_number` on **every** send, so send is currently **non-functional**, not merely mis-handled. Adopts the complete **nine-code** `Code` taxonomy (`invalid_text` was missing from the eight previously logged), driving mapping off `Code` not HTTP status, with per-code copy from the handoff matrix; `send_disabled` (409) becomes an **availability state, not a failed send**; `invalid_number`/`invalid_text` are terminal (no Retry). Subscribes to the **`SmsSent`** `/hub` event — the only channel outbound arrives on, which we have never listened to, making GV-3's optimistic de-dupe unreachable dead code. Adds `OutboundSmsReconciler`, **idempotent keyed by exact `Id` then `(Outbound, counterparty, text, ≤120s)`** (the poller's re-surfaced copy carries a different `Id` by construction), replacing in place so the bubble never jumps. Wires `ClientCorrelationId`; `Queued:true` → existing `Sent` (no "delivered" state — neither side can honestly assert delivery). Also fixes a duplicate-thread-row bug new conversations expose.

**Ships with `SendEnabled` still `false`.** **Also in scope: thread reply-ability** — ~a third of inbound SMS comes from senders that are **not dialable** (numeric short codes and opaque 36-char sender IDs, not E.164). Classify the counterparty client-side and **gate compose before the POST**, so an un-repliable thread yields a *disabled composer with an explanation*, never a red failed bubble — same reasoning as `send_disabled` (ADR-028 §5.1). _Provenance note: this row was briefly 🔒 on a "wrong tree" concern (2026-07-31) — **investigated and disproven**: `rp-deploy` is a **git worktree** of `D:\prj\RotaryPhone`, same object store, so ADR-028 was derived from the deployed objects all along. See the ADR banner._

**⚠ PLAN RECONCILED AGAINST POST-GV-8 `main` (2026-07-31) — read the plan's 🔄 banner before claiming.** The plan predates PR #461; **no ADR-028 decision changed**, but three things did.

**(a) Every line-number anchor in Chunks 5/6/6b was re-sited** (`PhonePage.razor` +109, `PhoneTextsPanel.razor` +51, `PhoneMessagesPanel.razor` +12) — **do not trust any pre-refresh citation**. `GvBridgeSendService.cs`, `ApiModels.cs:1187-1188`, `PhoneHubService.cs:25`/`:94` and `MessageBubble.razor:38` were **not** touched by GV-8 and are verified exact.

**(b) NEW Chunk 0 — `GvResult<T>` needs a small extension before the send path can adopt it.** `HttpError` nulls `Value` (a test-pinned invariant GV-6 relies on), but GV-5 maps off the **`Code` in the body** and needs `Code` **and** `Error` off a non-2xx. Chunk 0 adds a separate `FailureBody` + `HttpErrorWithBody`; widening `Value` was **rejected on the record**.

**⚠ Do NOT reuse `GvBridgeApiService.ReadErrorCodeAsync` for send** — it tries `error` before `code`, which is right for the read routes and for GV-6 but returns this endpoint's human **prose** instead of its machine code, collapsing all nine codes into one generic failure.

**(c) 🔵 ONE DECISION IS OPEN AND WANTS AN OWNER ANSWER BEFORE THE CYCLE STARTS — the composer during a failed load** (GV-8 UAT `O-2`, Chunk 6b Step 5). The plan **recommends** disabling it with a reason (`"Reply once this loads."`) rather than hiding it or leaving it live, because leaving it live is a **demonstrable defect, not a preference**: `OnOptimisticAppend` makes `Messages` non-null, which flips the pane out of GV-8's `Error && Messages == null` branch into the **list** branch, so the error state disappears and one outbound bubble renders as the whole conversation. GV-7 must not re-decide this.

**(d) Also folded in:** a companion guard so `BumpThread` adopting a new `ThreadId` cannot silently desync `_openThreadId` from the three sites GV-8 keyed on it, and Test-Plan preconditions for live verification (`192.168.86.50`, `-Runtime linux-x64`, the `psidtsAgeSeconds` blackout clock, binary-freshness grep).

**The duplicate-thread-row bug still reproduces** — `BumpThread` is absent from GV-8's diff — but remains **derived from RotaryPhone's source, never observed live**, since reproducing it needs `SendEnabled=true` on both sides.

**9 chunks / 9 tasks** after the refresh.
