# GV-7 — RE-SCOPED BY `D31` 2026-09-05 — the display half survives, the gating half is deleted, and the `GV-5` dependency DISSOLVES.

> Queue dossier for row **`GV-7`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD (design-led; consult the Designer handoff first)_ |
| Spec / handoff | [ADR-028 §8](../../design/decisions/2026-07-30-gv-sms-send-contract.md) · [handoff Screen C/D](../design-handoffs/HANDOFF-phone-messages-voicemail-sms.md) · [**UAT § Findings for GV-7**](../uat/2026-07-31-gv-live-data/REPORT.md) |
| Depends on | **GV-3** (texts surface). _The "wait for observations" caveat is **discharged** — they exist and are linked in the row. Coordinate with GV-5 if both are in flight: GV-5 adds `GvCounterparty` to `ApiModels.cs` and this row consumes it. **Coordinate with GV-8 too, and prefer GV-8 first:** GV-8 rewrites the conversation pane's state branches (`PhoneTextsPanel.razor:36-68`), which this row also touches — and designing the header/empty treatment on top of a pane that **cannot express "failed"** would bake the F-1 confusion into the new design._ |
| Branch | `feat/gv-messages-pr7-nondialable-senders` |

## Detail

🔄 **RE-SCOPED BY `D31` 2026-09-05 — the display half survives, the gating half is deleted, and the `GV-5` dependency DISSOLVES.**

⭐ **Read this before the original text below, which still describes the send-side gate.** The row was always two rows under one id.

**The display half is pure read surface and is the whole row now:** thread-list row, conversation header, contact resolution degrading gracefully, bubble/meta treatment — plus folded-in **G-6** and **G-8**.

**The gating half is gone** (there is no POST to gate, and `PHN-4` deletes the composer it would have disabled), and folded-in **F-3** moves to **`PHN-4`**.

⛔ **IGNORE the instruction below to *"reuse GV-5's `GvCounterparty.Classify`"*** — `GV-5` is parked 🚫 and that classifier will never exist.

**You do not need it.** Rendering a 36-char identifier legibly is a question about **string length and layout**, not repliability; `ContactResolutionService` already answers the only question that matters by failing to resolve.

**Real dependency is `GV-3`, as the column already says — this row is claimable.**

**Est. ~1 d**, down from 1–2 d. *Original text follows:* **Render non-dialable SMS senders (short codes + opaque sender IDs).** The **display** counterpart to GV-5's send-side reply-ability gate (ADR-028 §8). Roughly a third of inbound SMS comes from senders that are not phone numbers — **numeric short codes** and **opaque 36-char sender IDs** — and the texts surface was designed assuming a dialable counterparty throughout. Scope: **thread-list row** (what occupies the name line when there is no name and the identifier is 36 chars — truncation, `--font-mono`?, avoid layout blowout at **1920×720**); **conversation header**; **contact resolution** — `ContactResolutionService` resolves number→name and by definition **cannot** match a short code or opaque ID, so it must degrade gracefully rather than render an empty or misleading name; and the **bubble/meta** treatment for these threads.

**Critical constraint Builder confirmed: there is NO `fromName` anywhere in the GV payload** — only counterparty identifiers — so display names can come **only** from local contact resolution, which structurally cannot resolve these senders. Design for "identifier is all we will ever have." Reuse GV-5's `GvCounterparty.Classify` (do not write a second classifier). Composer behavior is **GV-5's** — this row must not re-decide it.

**Live observations now EXIST — design against them, not assumptions.** The prior "⚠ LIVE OBSERVATIONS INCOMING — do not design blind" warning is **retired**: the [2026-07-31 UAT](../uat/2026-07-31-gv-live-data/REPORT.md) observed this surface against real GV data, and its § "Findings for GV-7" (**G-1 … G-9**) is the input.

**Read it before starting.** The findings that actually change the design: **G-1 — the "2" in `Texts 2` is an UNREAD COUNT, not a thread count. There are 20 threads.** Design against a 20-row list, not the 2-row list an earlier session assumed.

**G-3 — zero opaque 36-char sender IDs are reachable from this surface**: the feed has no pagination or load-more control, so it caps at 20 threads and older threads are unreachable through the UI. That case is therefore **untested against real data, NOT proven absent** — do not conclude the corpus lacks them.

**G-4 de-risks the layout concern this row was written around:** a synthetic 36-char ID was injected and measured at 1920×720 — it **fits with no ellipsis and no page overflow** in both the thread-list row and the conversation header (`document.scrollWidth` stayed exactly 1920 at every length tested), so **no truncation strategy is needed for layout safety**. Also in scope from the pass: **G-6** — when no name resolves, the two-line header renders the **identical string on both lines** (`.texts-conv-name` over `.texts-conv-number`), confirmed for all 14 unresolved threads; **G-8** — MMS previews embed a sender prefix (`+1XXXXXXXXXX - <text>`), i.e. a *second* phone number inside a row that already displays a counterparty; and **F-3** — the composer is **rendered-but-disabled with no stated reason** (no tooltip, no helper text, empty `title`). Whether to hide it or label it is **a design decision for this row, not a prescription** — the UAT flagged it deliberately without deciding.

**Amended 2026-07-31 — that decision now has one more context than it had when this row was written.** GV-8 shipped an **error state** on this pane, and the GV-8 UAT's `O-2` observed the composer **still mounted behind it**: under a confirmed 502 the pane renders `cloud_off` + "Couldn't load messages." + `Retry` *and*, below it, a live `Message` field with `Send`. It is **inert** (`SendEnabled=false` is honoured — same as F-3), so this is **not a new defect and deliberately gets no row of its own**; it is routed here because it is the same composer decision, not a second one. But whatever this row decides — hide, label, or leave — now has to hold in **three** states rather than two: non-dialable counterparty, genuinely empty thread, and **failed load**. The third is the weakest case for leaving it: inviting a reply into a conversation the app has just admitted it could not read.

**G-5 is a PASS and needs no work** (resolution degrades gracefully — the raw identifier is used as the name, never blank, never wrong) **but recalibrates the priority: 14 of 20 (70%) of live threads render an identifier rather than a name**, so the fallback is the **common** case on this surface, not an edge case.
