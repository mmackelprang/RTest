# ADR: GV SMS Send — real contract, error taxonomy, and outbound echo de-dupe (supersedes ADR-022 D7)

> ## ✅ Provenance verified — the "wrong tree" concern was raised, investigated, and DISPROVEN (2026-07-31)
>
> **Keep this note.** It is short, and it exists so nobody re-raises a settled question.
>
> **The concern:** this ADR was derived by reading `D:\prj\RotaryPhone`, while the deployed RotaryPhone service was reported to run from `D:\prj\rp-deploy` @ `0a86898`. Since GV-5 exists *precisely because* GV-3 was built against an anticipated contract rather than the as-built one, deriving GV-5's contract from the wrong tree would have repeated that mistake one level up. GV-5 was blocked pending re-derivation.
>
> **The resolution: they are the same repository.** `D:\prj\rp-deploy\.git` is a **file**, not a directory, containing:
>
> ```
> gitdir: D:/prj/RotaryPhone/.git/worktrees/rp-deploy
> ```
>
> It is a detached-HEAD **git worktree** of `D:\prj\RotaryPhone`, pinned at `0a86898` for deployment — same object store, same branches, same remote. **This ADR was derived from the same git objects that get deployed.** There was never a divergent tree. A Builder subsequently landed the GV parser fix on that repo's `main` (merge `627b928`), confirming it as the development origin.
>
> **Supporting evidence** (gathered while the question was open, now corroboration rather than a lead): all four source files this ADR was derived from are **byte-identical** across both paths — which is exactly what a shared object store predicts.
>
> | File | Result |
> |---|---|
> | `Api/GvSmsController.cs` | identical |
> | `Api/GvBridgeReadDtos.cs` | identical |
> | `Services/GvThreadPoller.cs` | identical |
> | `Clients/SmsCorrelationId.cs` | identical |
>
> **GV-5 is unblocked** and depends on GV-3 alone.
>
> One unrelated point survives, and it is **not** a caveat on this ADR: *source parity is not binary provenance* — nothing yet proves which commit the running binary was built from. That is a general deployment-observability gap owned by **OPS-1** (build stamp), applies equally to every contract we consume, and blocks nothing here.

- **ID:** ADR-028 (see `design/DECISION-LOG.md` for the one-line pointer)
- **Status:** **Accepted** — provenance verified 2026-07-31 (see banner). Ready for Builder as GV-5; ships behind our existing flag, default OFF.
- **Date:** 2026-07-30
- **Author:** Planner
- **Supersedes:** **ADR-022 Decision D7 (§7) in full** — the request shape, the response shape, the "non-2xx = generic failure" error model, and the "confirm `SendSmsResponse` before wiring" open contract item. ADR-022 **§8 (config surface) is unaffected**: `RotaryPhone:Gv:SendEnabled` keeps its name, its meaning, and its `false` default.
- **Scope:** RadioConsole `Radio.Web` only. RadioConsole holds no Google credentials and never talks to Google; send flows through RotaryPhone's `gvbridge` API on `radio:5004`, which writes through to Google Voice's `sendsms`.
- **Source contract (authoritative — as-built, read from source not from docs):**
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Api/GvSmsController.cs` (`[HttpPost("send")]`, lines 86–162)
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Api/GvBridgeReadDtos.cs` (lines 50–80)
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Services/GvThreadPoller.cs` (lines 106–124, 164–174) — the outbound re-surface
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Clients/SmsCorrelationId.cs`
  - `D:/prj/RotaryPhone/src/RotaryPhoneController.Server/Services/GvMessagePushBridge.cs` (line 54) — the `SmsSent` broadcast
- **RotaryPhone-side plan (their error-taxonomy table + id-consistency rule):** `D:/prj/RotaryPhone/docs/superpowers/plans/2026-06-20-gv-pr4-sms-send.md`
- **Parent ADR:** [`2026-06-20-gvbridge-voicemail-sms-integration.md`](2026-06-20-gvbridge-voicemail-sms-integration.md) (ADR-022)
- **Sibling precedent:** [`2026-06-20-gv-mark-read-durable-readstate.md`](2026-06-20-gv-mark-read-durable-readstate.md) (ADR-024) — same shape of problem (unconditional broadcast → idempotent reconcile); this ADR stays deliberately consistent with it.

---

## 1. Context

GV-3 (PR #440) shipped the SMS **send** path built against an *anticipated* contract, flagged OFF behind `RotaryPhone:Gv:SendEnabled`. ADR-022 D7 said so explicitly: *"the exact `SendSmsResponse` shape is provisional… confirm when PR4 ships before wiring."*

RotaryPhone has since shipped the real endpoint. Reading it against ours, the divergence is **larger than the response-shape mismatch that was logged as the known fast-follow**. Four independent defects, in descending severity:

1. **The request shape is wrong, and it fails 100% of sends.** Ours is `SendSmsRequest(string ThreadId, string Text)`, constructed positionally as `new SendSmsRequest(threadId, text)`. Theirs is `SendSmsRequest(string ToNumber, string Text, string? ThreadId, string? ClientCorrelationId = null)`. On the wire we emit `{"threadId":…,"text":…}`. `ThreadId` and `Text` bind by name; **`ToNumber` binds to `null`**. Their handler then calls `PhoneNumberNormalizer.TryNormalize(null, …)`, which returns `false`, and returns **`400 invalid_number`**. Every send fails, in both reply mode and new-recipient mode. This was not previously known — the logged fast-follow only covered the response.

2. **The response shape drops `Queued` and the whole `Code` taxonomy** (the known issue). `Message`/`Error` still bind by name so deserialization *succeeds*, which is why it fails quietly.

3. **We never subscribe to the outbound echo channel.** Outbound messages are broadcast on a **`SmsSent`** SignalR event on `/hub` — never on `SmsReceived`. `PhoneHubService` handles `SmsReceived`, `VoicemailReceived`, and `ReadStateChanged`, but **not `SmsSent`**. Consequence: the optimistic→confirmed de-dupe written in GV-3 (`PhonePage.OnGvSmsReceived`, the `temp-` match) is **unreachable dead code** — the message it is waiting for arrives on a channel nobody is listening to. The optimistic bubble would linger un-reconciled until a manual refresh.

4. **The de-dupe window and key are wrong.** Ours is text + recency **≤ 30s**. RotaryPhone's poller re-surfaces outbound at 15s (active) / 60s (idle) / 120s (backoff), and their contract documents a **120s** agreed window. Worse, their re-surfaced copy carries a **different `Id`** than the send echo by construction, so an exact-id match cannot be the only tier.

Defect 1 alone means send is not "wired but mis-handled" — it is **non-functional**. That is what justifies a new ADR rather than an amendment: D7's stated contract is wrong on the request, wrong on the response, wrong on the error model, and silent about an entire event channel and a cross-service de-dupe invariant.

---

## 2. The complete `Code` taxonomy (verified against the as-built controller)

Nine codes, not the eight previously logged in `BUILDER_QUEUE.md` — **`invalid_text` was missing** from that list.

| `Code` | HTTP | `Queued` | Emitted when | Retryable with the same input? |
|---|---|---|---|---|
| `queued` | 200 | `true` | GV returned 200 — **accepted, not delivered** | n/a (success) |
| `send_disabled` | 409 | `false` | server `GVBridge:EnableSmsSend=false` — **checked first, no GV call, no rate-limit token burned** | **No** — needs a server-side config flip |
| `rate_limited` | 429 | `false` | sliding-window limiter rejected | **Yes**, after a cooldown |
| `invalid_text` | 400 | `false` | text null/whitespace | **No** — input must change |
| `invalid_number` | 400 | `false` | E.164 normalize failed, **or** GV returned `INVALID_ARGUMENT` | **No** — input must change |
| `auth_unavailable` | 502 | `false` | no authenticated GV client (cookie decay / recovery window) | **Yes**, shortly |
| `upstream_error` | 502 | `false` | GV returned a non-200, non-`INVALID_ARGUMENT` status | **Yes** |
| `timeout` | 504 | `false` | request timed out / network exception, **no response observed** | **Yes, but ambiguous** — see §5.3 |
| `error` | 500 | `false` | anything unclassified (unreachable by construction today) | **Yes** |

Check order in their handler, which matters for our expectations: **flag → rate limiter → text → number → resolve thread → send**. A malformed request still burns a rate-limit token; a dark-flag rejection does not.

**Rate limiter specifics:** 5 sends per 10-second sliding window, **process-wide** (not per-client, not per-thread), from `GVBridge:SmsSendMaxPerWindow=5` / `SmsSendWindowSeconds=10`. **No `Retry-After` header and no numeric hint in the body.** We must pick our own cooldown; §5.2 sets it at 10s to match their window.

---

## 3. Decision — the request

**Send the real four-field request. Never route a thread id into `ToNumber`.**

```
POST /api/gvbridge/sms/send
{ "toNumber": <string>, "text": <string>, "threadId": <string|null>, "clientCorrelationId": <string|null> }
```

- **Reply mode:** `toNumber` = the thread's counterparty number, `threadId` = the open thread's real GV id.
- **New-recipient mode:** `toNumber` = whatever the user typed (their server normalizes — do not pre-normalize into E.164 ourselves), `threadId` = **`null`**. GV-3 currently sets the thread id to the raw recipient number; that must stop.
- **`toNumber` is never a thread id.** Their normalizer strips all non-digits, so a *synthesized* thread id of the form `t.+19195551234` **silently "normalizes" to a plausible-looking `+19195551234`** rather than failing. That path is untested on their side. We avoid it structurally by always sourcing `toNumber` from a counterparty field.

**Decision: wire `ClientCorrelationId`, set to the optimistic bubble's own client id.**

The brief's hypothesis was right, but for a more useful reason than correlation-for-its-own-sake. Their handler uses `ClientCorrelationId` **verbatim as the echo's `Id`** when supplied, and synthesizes a `csid:` id when not. So supplying it makes the immediate echo's id **identical to the optimistic bubble's id we already rendered**, which means:

- the immediate `SmsSent` echo matches on tier-1 exact id — no fuzzy matching needed for the common case;
- the bubble's client-side id **never changes** during reconciliation, so the send-status map (`_statusById`, keyed by id) never needs re-keying across the swap.

It does **not** help with the poller's later re-surface — see §4 — and that is not a reason to skip it.

Because that id now travels cross-service and is the durable id other connected clients see, the optimistic id format changes from `temp-{guid:N}` to **`rc:{guid:N}`**. An id labelled "temp" that is in fact the permanent server-side id is actively misleading.

---

## 4. Decision — `Queued`, the echo, and the de-dupe invariant

### 4.1 What `Queued: true` means

`Queued: true` means **Google accepted the send**. It is not delivery confirmation; their ADR is explicit that `sendsms` returns a transaction ack, not the echoed message, and that we must "never report delivery."

**Decision: `Queued: true` maps to the existing `SendStatus.Sent`. No new UI state is introduced.**

The handoff's bubble table lists a fourth "Confirmed" row for when the message re-surfaces — but that row's own requirement is *"collapse to one. No visual jump."* It asks for a silent identity reconciliation, not a visible state change. And a visible "confirmed" tick would be dishonest: the poller re-surface proves Google **stored** the message, not that a carrier **delivered** it. So the re-surface is a data-level swap with no glyph change, and `MessageBubble.SendStatus` stays `{ None, Sending, Sent, Failed }`.

Defensively: a 200 whose body has `Queued != true`, `Code != "queued"`, or `Message == null` is treated as a **failure**, not a success.

### 4.2 The echo channel

Our own outbound message comes back to us **twice**, both times on **`SmsSent`** (never on `SmsReceived` — so there is no inbound-toast risk from either copy):

| Copy | When | `Id` it carries |
|---|---|---|
| **(a) Controller echo** | immediately; `Clients.All`, so the sender receives its own | our `ClientCorrelationId` when we supply one, else a synthesized `csid:` |
| **(b) Poller re-surface** | next poll tick — 15s active / 60s idle / 120s on backoff | **always** `csid:{threadId}:{sha1(text)[..12]}:{sentEpochMs}`, recomputed from Google's data |

Copy (a) is also handed to us synchronously as `SendSmsResponse.Message`, so the same message can land three times on one send.

### 4.3 Why exact-id matching is insufficient

Copy (b) can never equal copy (a)'s id:

1. **The epoch differs** — the controller stamps `DateTimeOffset.UtcNow` at send time; the poller uses Google's `sentEpochMs`, which is frequently second-granularity.
2. **The thread id can differ** — for a *new* conversation the controller's `csid:` embeds the synthesized, explicitly-UNVERIFIED `t.+<E164>`, while the poller embeds Google's real thread id.
3. **Supplying `ClientCorrelationId` guarantees divergence** — the poller always computes the `csid:` form and cannot reproduce an arbitrary client string.

RotaryPhone's own source says so at `GvThreadPoller.cs:164-169`: the belt-and-suspenders match is *"REQUIRED, not optional."*

### 4.4 The invariant (stated once, loud)

**Outbound reconciliation is idempotent, keyed first by exact `Id`, then by `(Outbound, normalized counterparty, ordinal-equal text, |ΔSentAt| ≤ 120s)`. On a match, the existing bubble is REPLACED IN PLACE — never removed-and-appended, never double-added.**

This is deliberately the same shape as ADR-024 §9's read-state invariant (`(id-or-threadId, isRead)`), for the same underlying reason: **RotaryPhone broadcasts unconditionally, including back to the originator.** ADR-024 solved it for read-state; this solves it for the outbound write path. Consistency here is intentional — one mental model, two channels.

Fields deliberately **excluded** from the key: `IsRead` (the controller echo hardcodes `true`; the poller copy carries Google's value) and exact `SentAt` equality (the two copies disagree by design).

**The 120s window is a cross-service agreement, not a local tuning knob.** It becomes `RotaryPhone:Gv:SendDedupeWindowSeconds` (default `120`) so the coupling is visible in config and adjustable without a rebuild if their poll cadence changes.

**Accepted residual risk:** two genuinely distinct sends of identical text to the same counterparty inside 120s collapse into one bubble. Judged strictly better than the alternative failure (a permanently duplicated bubble), and it matches the window RotaryPhone documents.

---

## 5. Decision — `Code` → UI state mapping

Each code maps to a typed exception, and each exception to exactly one bubble treatment. Copy comes from the handoff's send-failure matrix.

| `Code` | Exception | Optimistic bubble | Compose text | Retry affordance | User-visible |
|---|---|---|---|---|---|
| `queued` | — | → `Sent` | cleared | — | none (single check) |
| `send_disabled` | `SendDisabledException` | **removed** | **restored** | none | "Texting unavailable" pill + Send disabled |
| `rate_limited` | `SendRateLimitedException` | → `Failed` | restored | Retry, after 10s cooldown | toast: *Sending too fast — wait a moment.* |
| `invalid_number` | `SendRejectedException` | → `Failed` | restored | **no Retry** | toast + inline recipient error |
| `invalid_text` | `SendRejectedException` | → `Failed` | restored | **no Retry** | toast (defensive; compose already blocks empty) |
| `auth_unavailable` | `SendUnavailableException` | → `Failed` | restored | Retry | toast: *Couldn't send — Google Voice needs to reconnect. Try again shortly.* |
| `upstream_error` | `SendFailedException` | → `Failed` | restored | Retry | toast: *Couldn't send your message. Try again.* |
| `timeout` | `SendTimedOutException` | → `Failed` | restored | Retry | toast: *No response — check the connection and try again.* |
| `error` / unknown | `SendFailedException` | → `Failed` | restored | Retry | generic failure copy |

Three of these deserve their reasoning recorded.

### 5.1 `send_disabled` (409) is NOT a failed send — this is the brief's dark-rejection question

**Decision: treat it as an availability state, not a failure.**

The server made **no GV call**. Nothing was sent, nothing can be retried, and no amount of user action helps — it needs an owner to flip `GVBridge:EnableSmsSend` on RotaryPhone. Rendering a red failed bubble with a Retry button would be a lie in three directions at once.

So: **remove the optimistic bubble entirely** (nothing happened, so nothing should be shown as having happened), **restore the composed text**, and route to the same affordance as the degraded case — the handoff's **"Texting unavailable" pill + Send disabled**. This is distinct from `auth_unavailable`, which *is* a genuine transient failure of a real attempt.

This also unifies a latent inconsistency: our client-side `SendNotAvailableException` (our own flag off) currently marks the bubble `Failed` and toasts "Coming soon." Both flags-off cases now take the same not-a-failure path.

### 5.2 `rate_limited` gets a client-side cooldown

Their 429 carries no `Retry-After`. Without a cooldown the user simply re-burns tokens against a 5-per-10s window and stays rate-limited. **Decision: disable Send for 10s after a 429**, matching their `SmsSendWindowSeconds` default. This is a soft coupling to their config and is documented as such; it is a UX guard, not a correctness mechanism, so drift is tolerable.

### 5.3 `timeout` (504) is ambiguous and stays ambiguous

No response was observed, so the send may or may not have reached Google. Per RotaryPhone's no-auto-retry rule (a send is an irreversible account write), **we never auto-retry**. If the send *did* land, the poller re-surfaces it and §4.4 collapses it against the failed optimistic bubble. If the user manually retries and the original also landed, two genuine messages exist and neither side can tell them apart. **Accepted** — it matches their honest-status discipline, and the copy ("No response — check the connection") sets the right expectation rather than implying failure.

### 5.4 Terminal codes suppress Retry

`invalid_number` and `invalid_text` cannot succeed on retry with the same input, so the failed bubble offers no Retry target and the error is surfaced where the fix happens — for `invalid_number` in new-recipient mode, that is the **inline recipient-field error**, not just a toast.

---

## 6. Consequences

- **Send remains flagged OFF (`RotaryPhone:Gv:SendEnabled=false`) on merge.** GV-5 makes send *correct*; flipping it on is a separate, later decision requiring RotaryPhone's `GVBridge:EnableSmsSend` to be flipped first. Merging GV-5 with our flag off is a **no-op for users**: compose is not reachable, no new network calls are made. The one always-on delta is the new `SmsSent` subscription (§4.2), which is inert unless an outbound message exists.
- **A latent thread-identity bug is uncovered.** After a new-conversation send, the response's `ThreadId` is their *synthesized, UNVERIFIED* `t.+<E164>`, while the poller later surfaces the same conversation under Google's **real** thread id. `PhonePage.BumpThread` matches on `ThreadId` alone and will therefore insert a **duplicate thread row**. Fixed in GV-5 by falling back to a normalized-counterparty match.
- **`SendStatus` is explicitly session-scoped.** A full thread refetch returns outbound messages with Google's real message ids (that read path does not use `csid:`), so ids change across a reload and status glyphs reset. This is pre-existing and acceptable; it is recorded so it is not re-litigated as a bug.
- **Files touched:** `ApiModels.cs`, `GvBridgeSendService.cs`, `PhoneHubService.cs`, `PhonePage.razor`, `PhoneTextsPanel.razor`, `MessageBubble.razor`, `Program.cs`/`appsettings.json`, plus tests. No new services, no new auth posture — send rides the existing `RotaryPhoneAuthHandler` seam (ADR-022 §8.1), still off.
- **No backward compatibility.** Greenfield project rule: the provisional `SendSmsRequest`/`SendSmsResponse` records are **replaced**, not versioned or shimmed. Nothing outside `Radio.Web` consumes them.
- **Radio.Web's own DTOs are the target.** These records live in `src/Radio.Web/Models/ApiModels.cs`, which is separate from the `Radio.API` project's DTOs. The API project is not involved in this change at all.

---

## 7. Alternatives considered

- **Amend ADR-022 D7 instead of a new ADR.** Rejected: D7 is wrong on request shape, response shape, error model, and omits an entire event channel plus a cross-service de-dupe invariant. An amendment that replaces every substantive clause is a new ADR wearing a hat. ADR-024 set the precedent for splitting a contract this size out.
- **Do not send `ClientCorrelationId`; rely on the `csid:` text fingerprint.** Both ids would then be `csid:` form and share the middle `sha1(text)[..12]` segment, giving a recency-independent match. Rejected as the *primary* mechanism: it collapses legitimately-repeated identical texts with no time bound, and it forces the bubble id to change mid-flight (re-keying the status map). The fingerprint is noted here as a diagnostic aid, not implemented.
- **Add a fourth "Confirmed/Delivered" bubble state.** Rejected — see §4.1. Neither side can honestly assert delivery, and the handoff explicitly asks for no visual jump.
- **Auto-retry on `timeout` / `upstream_error`.** Rejected — a send is an irreversible account write; RotaryPhone's ADR forbids it and the handoff makes retry user-initiated.
- **Map `send_disabled` to the generic failure path** (status-quo behavior). Rejected — see §5.1.

> Added 2026-07-31. §8 is appended rather than inserted so the existing §-references in the GV-5 plan stay valid.

---

## 8. Decision — thread reply-ability (not every counterparty is dialable)

### 8.1 The finding

A Builder classified the counterparty identifier across the captured SMS threads and found it is **not always a phone number**:

| Counterparty kind | Count | Dialable? |
|---|---|---|
| E.164 phone number | 45 | yes |
| Numeric short code | 20 | **no** |
| Opaque 36-char sender ID | 7 | **no** |

**Roughly a third of inbound SMS comes from senders that cannot be replied to at all** — the automated end of the spectrum (verification codes, alerts, marketing).

> **⚠ Confirm the counts before relying on them.** The classification was reported as covering **60 captured threads**, but the three buckets sum to **72**. The proportions are directionally clear and the *design* below does not depend on the exact split — but the arithmetic does not reconcile, so treat the numbers as indicative and re-derive them if anything is ever keyed off the ratio. Recording the discrepancy rather than quietly propagating it.

### 8.2 Why this belongs to GV-5

"Can this thread be replied to at all" is a **send-side** question, and GV-5 owns the send path. The display consequences — how such a thread *renders* in the thread list, the conversation header, and contact resolution — are a separate concern tracked as **GV-7**.

### 8.3 Where classification lives

**Decision: classify client-side, in a small pure static helper in `Radio.Web`. Do NOT add a field to the DTO.**

- The classification is a **pure function of `CounterpartyNumber`**, a value we already receive. It needs no server-side knowledge.
- Adding `CanReply` / `CounterpartyKind` to `SmsMessageDto` / `SmsThreadDto` would make this a **cross-service contract change** requiring a RotaryPhone build and re-ratification. This session is a sustained lesson in how expensive contract drift is; taking on a new shared-shape dependency to compute something we can derive locally is a bad trade.
- **In-tree precedent:** `GvDirection` in `ApiModels.cs` is already exactly this — a client-side static defensive classifier over a raw string field from the same DTOs. `GvCounterpartyKind` should sit beside it and read the same way.

Shape:

```
public enum GvCounterpartyKind { PhoneNumber, ShortCode, OpaqueSenderId }
public static class GvCounterparty
{
  public static GvCounterpartyKind Classify(string? counterpartyNumber);
  public static bool CanReply(string? counterpartyNumber);   // == Classify(...) is PhoneNumber
}
```

Classification is **defensive and total** — every input returns a kind, `null`/empty included, and nothing throws. Anything not confidently a dialable number is treated as **not repliable**: the failure mode of wrongly disabling reply on an odd-but-valid number is a visibly disabled composer the user can report, whereas wrongly *enabling* it produces the failed-send lie §8.5 exists to prevent. Bias toward the recoverable error.

### 8.4 What `SendSmsRequest.ToNumber` carries for a non-E.164 thread

**Nothing — we never send.** The question is malformed, and that is the point.

Their server normalizes `ToNumber` to E.164 and returns **`400 invalid_number`** when it cannot. So sending anyway would surface a short-code thread as `SendRejectedException` → a red **failed bubble** with *"That number doesn't look right. Check it and try again."* That copy lies twice over: the user did not type a number, they replied to a thread; and it implies a fixable input error when the thread is **structurally** un-repliable. No amount of user action helps.

### 8.5 UI treatment — consistent with `send_disabled`

This is the **same problem §5.1 already solved**, and it takes the same shape: *a send that cannot succeed must never be presented as a send that failed.*

**Decision: gate compose at the thread level, before the POST.** For a thread whose counterparty is not `PhoneNumber`:

- **No optimistic bubble is ever created** — nothing was attempted.
- The composer renders **disabled with an explanation**, reusing the handoff's existing "GV unavailable" affordance shape (a `.phone-pill` + short reason) rather than inventing a new one.
- Do **not** hide the composer outright. The handoff's own reasoning for the degraded case applies verbatim — *"don't let the user type into a dead send path"* — and a silently absent composer reads as a rendering bug, whereas a disabled one with a reason reads as an answer.
- Copy sits in the calm register the handoff uses elsewhere: **"You can't reply to this sender."**

**Defense in depth:** `GvBridgeSendService.SendAsync` also gates, throwing a typed `SendNotRepliableException` **before** the flag / degraded / single-flight checks, so a bypassed or future UI path cannot produce the misleading failed send. Same belt-and-braces posture as the rest of §5.

### 8.6 Consequence

Reply-ability is **presentation and gating only** — it changes nothing about the wire contract, adds no config, and requires nothing from RotaryPhone. It ships inside GV-5 with `SendEnabled` still `false`.
