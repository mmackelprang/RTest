# ROADMAP — what is actually left before GA

**Written:** 2026-09-09 (Planner) · **Read-only pass.** Nothing was claimed, reordered, or edited; this
file is the only thing it added.

**Derived from**, in this order: [`BUILDER_QUEUE.md`](BUILDER_QUEUE.md) (26 live rows) ·
[`HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md) · the per-row dossiers in [`queue/`](queue/) ·
the plans in [`../design/plans/`](../design/plans/) · [`BUILDER_QUEUE_ARCHIVE.md`](BUILDER_QUEUE_ARCHIVE.md)
(55 shipped rows — ⚠ figure re-derived from the instrument 2026-09-09; the previous **52** was stale and was flagged by `AUD-2`'s Builder) · [`queue/inbound/`](queue/inbound/).

> ⚠ **This is a decision aid, not an inventory.** It answers three questions and nothing else:
> **what is left before GA · what can proceed without you · what is waiting on you specifically.**
>
> ⚠ **Where a figure exists I cite it and say where from. Where none exists I write *unestimated*.**
> No number in this document was invented. Where two documents give different figures for the same row,
> both are shown.
>
> ⛔ **Retracted headlines are not repeated.** Several rows' original titles were later falsified by
> their own dossier or plan. Where that happened the corrected position is what appears here, and the
> retraction is named. §G lists every contradiction found; **none was fixed** — recording them was the
> brief.

---

## 0. The shape, in six sentences

> ⛔ **STALE IN PLACES — READ THIS FIRST, 2026-09-09 (Coordinator).** Several items below were completed
> the same day this document was written, and **nothing pointed the new facts back at it.** That is the
> failure mode this repo has been cataloguing all week: *a claim falsified by evidence its own author
> later produced, and never re-run.* Corrections, rather than a rewrite, so the drift stays legible:
>
> | Item below | Says | Actually |
> |---|---|---|
> | §C.2 #6 **"Deploy the Radio side"** | 8 rows merged-but-undeployed | ✅ **DONE** — deployed twice 2026-09-09, verified at `f4d71b28` on both services |
> | §C.2 #4 **"Run `PHN-2`'s owner UAT"** | 9 `SOUND` items unverified | ✅ **DONE** — 6 pass / 2 fail / 1 deferred, [record](uat/2026-09-09-phn2-sound-uat/RESULT.md) |
> | §F #1 **"Merge `UI-8` and `UI-15`"** | both open | ⚠ **HALF** — `UI-8` shipped (#640); `UI-15` still live |
> | §F #5 **"let a Builder take `UX-1`, `AUD-5`, `AUD-2`, `PHN-9`"** | all four open | ⚠ **`UX-1` shipped TWICE** (#641 at 56, then #644 reversing to 36) and **`AUD-2` shipped** (#642) and is **confirmed by ear**. `AUD-5` and `PHN-9` still open |
> | §A.3 **"the tier map — all 26 live rows"** | 26 rows | ⚠ **30 rows** — `OPS-12`, `TEST-10`, `AUD-24`, `AUD-25`, `AUD-26`, `UX-2` filed since; `UI-8`, `UX-1`, `AUD-2` archived |
> | §G.1 **"the punch list carries eleven shipped items as open"** | 11 | ⚠ **unre-counted since** — more have shipped; the finding stands, the number does not |
>
> ⭐ **`PHN-10` has been PLANNED SINCE, and the plan overturned three of its own row's premises** —
> including that `PHN-1f` was never structurally in scope, and that the verification criterion this
> coordinator supplied (*"assert the second voicemail waits, then plays"*) **contradicts ADR-029 §6.2
> rule 1** and would have gated on the wrong behaviour. See `design/plans/PHN-10-nothing-can-stop-a-voicemail.md`.
> **The defect is not "two voicemails" — nothing can stop a voicemail**, and every one played leaks a
> `SoundPlayer` plus a mixer component permanently. ⛔ **Re-tier before using §A.3's map to sequence.**

1. **The GA punch list has no open P0 items.** Its banner still says *"P0 remaining: 2"*; both — `PHN-1`
   and `PHN-2` — shipped on 2026-09-04/05. §A.1.
2. **The punch list carries eleven items as open that have already shipped**, across all three tiers.
   §G.1. Its last substantive content update was 2026-09-07.
3. **Seventeen of the 26 live queue rows appear nowhere in the punch list at all** — every row filed
   since 2026-09-06. They carry their own self-assigned tier in their dossier, tiered by a different
   author against a different question. §A.2.
4. **Two rows are ready to merge today with no involvement from you**: `UI-8` and `UI-15`. Six more are
   plan-complete and buildable but want your eyes at the merge, and four more need you at the cabinet
   to finish. §B.
5. **Nothing is blocked on an owner *decision* that gates a whole arc.** The punch list's decision
   register is empty — *"**None.**"* — and the last three panel sightings (`UX-1`, `UI-8`, `PHN-9`)
   closed on 2026-09-09. What remains are **owner *actions***: a deploy, a phone, and about an hour at
   the cabinet. §C.2.
6. **The largest single piece of unscheduled work is not in the queue.** Roughly thirty punch-list P1
   items — the whole `LOG-*` and `TTS-*` workstreams, `XR-2`…`XR-6`, `AUD-21`/`22`/`23` — are open,
   tiered *"blocks calling it finished"*, and have **no queue row and no plan**. §A.4.

---

## A. GA-blocking versus not

### A.1 P0 is empty

The punch list's tier definitions are the GA-blocking criterion — there is no separate "GA blocker"
column. **P0 = blocks installation. P1 = blocks calling it finished. P2 = post-GA.**

Its banner (`:43`) reads **"P0 remaining: 2, both in the phone arc"**, and §9's index cell reads
*"21 listed, 2 open"*. **Both are stale.** The two named items shipped:

| Item | Punch list says | Reality |
|---|---|---|
| `PHN-1` (ADR-029 seam) | open, *"PRs 5, 5b, 6 and 7 remain"*, `Queued? No` | All six sub-PRs merged — `PHN-1a` #528, `1b` #534, `1c` #556, `1d` #558, **`1e` #561** (`4ec0fb85`, 2026-09-04), **`1f` #564** (`ba1ae4a6`, 2026-09-04) |
| `PHN-2` (retire the `<audio>` element) | open, *"has not started"*, *"PR 6 has no plan file"* | Merged **#566** (`c61f9276`), 2026-09-05. The plan exists at [`design/plans/PHN-2-retire-the-audio-element.md`](../design/plans/PHN-2-retire-the-audio-element.md) |

The queue archive states it in the terms the punch list should have inherited: *"**`PHN-2` MERGED as
#566 … THE LAST OPEN P0 ON THE GA PUNCH LIST IS CLOSED**, and voicemail is ordinary console audio."*

⚠ **One P0 residual survives the code merge and it is yours.** `PHN-2`'s close-out commit (`603207df`)
records that **nine `SOUND`-class verification items remain unverified** — things only a person in the
room can confirm. The script is [`design/plans/PHN-2-retire-the-audio-element.md` §3](../design/plans/PHN-2-retire-the-audio-element.md),
written to be read standalone at the cabinet, **fifteen minutes**. Until it runs, "the last P0 is
closed" is a statement about the diff, not about the room.

### A.2 There are two tiering systems, and they do not know about each other

| Where the tier comes from | Rows | Tiered against |
|---|---|---|
| **Punch list §4/§5** | 9 of the 26 live rows: `AUD-1` `AUD-2` `AUD-4` `AUD-5` `GV-5` `GV-7` `GV-10` `OPS-3` `UX-1` | *"What must be true before this ships into a piece of furniture in a family home"* |
| **The row's own dossier** | the other 17 | Whatever the filer judged on the day, using the same 🔴🟠🟡🔵 vocabulary |

Verified by grep: `AUD-13`…`AUD-20`, `UI-8`, `UI-10`, `UI-15`, `PHN-7`, `PHN-8`, `PHN-9`, `OPS-10` and
`KIOSK-3` return **zero** matches in the punch list. `AUD-10`/`AUD-11` match only inside the
renumbering table, and refer to **different items** (see §G.2).

**This is the single most useful thing to know about the current state:** two thirds of the live queue
has never been assessed against the cabinet-install question. It is not that they were judged
non-blocking — nobody asked.

### A.3 The tier map — all 26 live rows

Tier source is marked: **[PL]** = punch list, **[D]** = the row's own dossier.

| Row | Tier | Subject, post-correction | Plan? | Auto-merge? |
|---|---|---|---|---|
| `AUD-18` | 🟠 P1 [D] | Fingerprint tap read zero bytes for 11 h 23 m; art + history dead, nothing noticed | no | ⛔ no |
| `AUD-15` | 🟠 P1 [D] | BT resampler is open-loop; `SetRatio` has zero callers; cushion drains in ~12 min | yes | ⛔ no |
| `AUD-11` | 🟠 P1 [D] | BT capture silently re-links to the built-in line-in; every indicator stays green | yes | ⛔ no |
| `AUD-10` | 🟠 P1 [D] | Pause on the handset destroys the A2DP transport; resume never restores it | no | — |
| `AUD-13` | 🟠 P1 [D] | USB capture *substitutes* a device rather than refusing, on a **non-empty** port | yes | ⛔ no |
| `AUD-14` | 🟠 P1 [D] | Stale AVRCP watcher survives re-attach; can raise `Playing` on a torn-down source | no | ⛔ no |
| `AUD-5` | 🟠 P1 [PL] | A superseded Cast connection persists its volume as system master | yes | not stated |
| `AUD-2` | 🟠 P1 [PL] | Four sources register under a minted key, are addressed by `Id`; gain + ducking miss silently | yes | not stated |
| `UI-10` | 🟠 P1 [D] | `radio-web`'s four hub clients run a 30 s timeout against a 30 s keep-alive | no | not stated |
| `PHN-9` | 🟠 P1 [D] | Topbar unread badge reads a latch that only `PhonePage` writes; absent after every restart | **yes** | ⛔ no |
| `PHN-7` | 🟠 P1 [D] | `BellHealthService` polls the REST transport, which returns `UtcNow` as "last checked" | no | not stated |
| `OPS-3` | 🟠 P1 [PL] | `BindsTo=` on web **+ `Upholds=` on api** — make joint failure actually joint | yes | ⛔ no, by standing exemption |
| `UX-1` | 🟠 P1 [PL] | Skeleton shimmer invisible; value decided, one new token | yes | ⛔ no (rationale now spent) |
| `AUD-1` | 🟡 P2 [PL] | Per-field metadata precedence: source wins where present, fingerprinting fills gaps | **yes** | ⛔ no |
| `AUD-4` | 🟡 P2 [PL] | Unify the three source-removal layers; rename `SoundFlowMasterMixer` — it is not a mixer | yes | ⛔ no |
| `AUD-17` | 🟡 P2 [D] | AVRCP album art has **never** worked; the code reads MPRIS names off a BlueZ interface | no | conditional |
| `AUD-19` | 🟡 P2 [D] | After `AUD-1`, History will not follow the per-field rule that now-playing does | no | likely yes |
| `UI-8` | 🟡 P2 [D] | Two glow tokens consumed, never declared — **delete three dead references** | no (fully specified) | ✅ **yes** |
| `UI-15` | 🟡 P2 [D] | `isFirstRun` conflates "never observed" with "no source" | **yes** | ✅ **yes** |
| `PHN-8` | 🟡 P2 [D] | Speak a thread's unread messages from the phone **dashboard**, not just inside a thread | no | not stated |
| `GV-7` | 🟡 P2 [PL] | Render non-dialable SMS senders legibly — **display half only**, gating half deleted | no | not stated |
| `GV-10` | 🟡 P2 [PL] | ❌ **Falsified as ours.** Bubbles already render the full body verbatim | n/a | n/a |
| `AUD-16` | 🔵 P3 [D] | Retire the deprecated USB radio path — a removal **decision**, the deprecation already shipped | no | conditional |
| `AUD-20` | 🔵 P3 [D] | SDR deadline misses up 2–4× since 09-08; **no cause established**, five hypotheses dead | no | n/a |
| `OPS-10` | 🔵 P3 [D] | Give `radio-api` a readiness signal so "active" means "the hub answers" | no | ⛔ no |
| `GV-5` | 🚫 parked | SMS send contract — **refused by owner decision `D31`. Never claim.** | kept, do not execute | n/a |

### A.4 Where the two documents disagree — say it, don't pick one

**Four disagreements are material, and one is a live trap.**

**1. `AUD-2` — state, not tier.** Punch list §4.2: *"An **unverified code-read inference** … The first
task is an investigation that may legitimately end in no code change."* Queue `:53`: *"✅ **CONFIRMED
2026-09-05** — four primary sources register under a minted key … so gain and ducking miss silently."*
The investigation was run and is not to be re-run. **Believe the queue**; the punch list predates it.

**2. `AUD-1` — tier versus how the queue is scheduling around it.** Punch list §5 tiers it P2 with the
reason *"The visible symptom is a slightly wrong track title. Real, annoying, **not a GA blocker**."*
The queue has since filed `AUD-19` explicitly as *"after `AUD-1` ships"* and the owner issued a
behavioural rule for it on 2026-09-08. **This is a real tension the punch list has not been asked
about**: is per-field metadata precedence post-GA work, or is it the thing two other rows are now
waiting behind? That is your call, not a bookkeeping error.

**3. ⛔ `AUD-1`'s punch-list cell contains a claim its own dossier has since falsified.** §5 says
*"The wanted behaviour already exists at `BluetoothAudioSource.cs:893-905`, on the branch the flag
never takes."* The dossier's ⛔ correction of 2026-09-08: that branch *"fills cover art and nothing
else. It never touches Title/Artist/Album"* — a phone with an empty AVRCP title would end up with **no
title at all**. Building from the punch-list cell *"would have shipped a regression."*

**4. ⛔ `AUD-4`'s punch-list cell, and ordering constraint `O3`, are both falsified.** §5 instructs
**"Sweep `_activeComponents`, not `oldSource.Id`"** and marks the row *"Yes 📋 (after `AUD-2`, `O3`)"*.
§2 states `O3` as a mechanism, not a preference. Both were **FALSIFIED 2026-09-06** while planning
`AUD-4`: the roster is a `List<IAudioSource>` mutated by **object reference**, so there is no key for
`AUD-2` to decide; and `SoundFlowPlaybackService.StopAll()` — the sweep prescribed — **has zero callers
in the tree.** The retraction is recorded in `AUD-2.md`, `AUD-4.md` and `ORDERING-NOTES.md`.
**`AUD-2` and `AUD-4` are independent. Either may be claimed first.**

**Effort figures also disagree**, and where they do the plan is the later measurement:

| Row | Punch list | Queue index | **Plan (authoritative)** |
|---|---|---|---|
| `AUD-1` | 1–2 d | 0.75 d / 0.5 d ⚠ stale | **1.25 d** (§0.11, re-priced after the owner's rule) |
| `AUD-2` | 0.5 d confirm + 1 d fix | — | **1 day** (§7); the confirm half is already spent |
| `AUD-5` | 1 d | 0.5 d | **0.5 d** (§0.8) |
| `AUD-4` | 2–3 d | 2 d / 3 d | **2 d minimal, 3 d recommended split** (§0.8) |
| `OPS-3` | 2–3 h | 0.5 d + ~20 min supervised | **1.25 d** (§0.8) — ⚠ **but that price includes the readiness work the owner split out into `OPS-10`.** For `OPS-3` as narrowed, the queue index's ~0.5 d is the right figure |

---

## B. Ready now — plan complete, unblocked, no owner input needed to *build*

Three bands, because "ready" means different things at the merge button.

### B.1 Ship today, no involvement from you at all

| Row | What it is | Size |
|---|---|---|
| **`UI-8`** | Delete exactly three dead CSS references — `design-system.css:5186`, `:5425`, `:5432`. ⛔ Do **not** declare the two tokens; after the deletions they have zero consumers. ⚠ `:5378` is a **comment**, not a consumer — leave it. | unestimated. It is a three-line deletion with a **literal zero visual change** |
| **`UI-15`** | Give "never observed" its own field so `null` can be an ordinary `ActiveSource` value. Four lines plus tests; the harness already exists. | unestimated |

Both are explicitly auto-mergeable — `UI-8` because the owner's 2026-09-09 decision made it a
zero-delta cleanup (*"a UAT claiming to have observed the change would be false"*), `UI-15` because its
plan §0.11 clears all four auto-merge gates.

⚠ **`UI-15` comes with a correction its queue row does not carry.** The row claims *"THIS IS A LIVE
DEFECT, unlike `UI-13`"*. The plan measured the appliance: **36 consecutive service lifetimes, zero
firings** — the hosted services start sequentially, so on a healthy boot the window does not exist, and
`source → none → source` is **impossible**, not merely unproven. The plan still recommends shipping (it
is reachable through six unguarded startup-failure paths, on the console's primary physical input) but
says plainly: **re-tier P2 → P3 whatever else is decided.**

### B.2 Ready to build, wants your eyes at the merge

| Row | What it is | Size |
|---|---|---|
| **`UX-1`** | One new CSS token, `#38383F` (value 56, delta 36). ⛔ Do **not** reuse `--surface-overlay` — it has 20 consumers and changing it re-themes twenty unrelated surfaces under a shimmer row. | **0.5 d Builder** — and the two sittings plus the ~30 min owner session the estimate included are **already spent** |
| **`AUD-5`** | Generation re-check before the Cast volume fire, **plus** the subscriber ignoring `IsInitialSync`. Both halves decided. | **0.5 d** |
| **`AUD-2`** | One key per source, agreed by both layers. Scope is **four source types across three files** — the title and branch name understate it. | **1 day** |
| **`PHN-9`** | Make `MainLayout` fetch the unread total instead of reading a latch. | unestimated; the plan is 1,283 lines with a full task list, gates and mutation matrix |
| **`AUD-4`** | Unify the three removal layers and split `SoundFlowMasterMixer` into a registry + output state. | **2 d** minimal / **3 d** split |
| **`GV-7`** | Render non-dialable senders legibly. Plan TBD but design-led and unblocked — see §D. | **~1 d**, down from 1–2 d |

⚠ **`PHN-9`'s "cross-repo blocker" is dissolved and neither the queue row nor the dossier says so.**
Both say the endpoint question must go to RotaryPhone first. The plan §7 records that it **was asked and
answered**: *"no unread total exists anywhere on their API, and none is planned as far as they can see"*
— independently confirmed by enumerating their 24 GET routes across 6 controllers. The plan therefore
does not branch; it builds the Radio-side poller and costs the one-endpoint future as a single call
site. **This row is buildable today.**

### B.3 Ready to build, but the box finishes it

| Row | Size | The box part |
|---|---|---|
| **`AUD-13`** | **0.5 d** build | one box question to confirm the Vinyl port |
| **`OPS-3`** | **~0.5 d** as narrowed | **~20 min supervised session** to install the unit files — merging alone is inert, `Deploy-ToLinux.ps1` does not install unit files |
| **`AUD-11`** | **1.5 d** build | **one box session with the handset** — the failure only exists when the `bluez_input` node vanishes |
| **`AUD-15`** | **2 d** build | **≥30 min sustained BT playback with you present** — onset is ~12 minutes |

⚠ **`AUD-13`'s headline is retracted and the row survives on a different defect.** `USBPort: ""` never
reaches `Contains("")` — three separate guards refuse a blank port first, and it *read* as reachable only
because `USBAudioSourceTests.cs:58-59` mocks the device manager away. **The real, reachable defect is the
fallback on a NON-empty port**: unmatched pattern → `captureDevices[0]`, ambiguous → first match silently,
zero devices → system default silently.

⚠ **`OPS-3` as specified was disproved by rehearsal.** `BindsTo=` alone fires during `radio-api`'s
ordinary 10 s back-off and the console **never returns** — a transient crash becomes permanent darkness,
the opposite of the row's purpose. `BindsTo=` **plus** `Upholds=` was measured working on the
appliance's exact systemd version. ⛔ **The `OPS-3` dossier's last section is itself superseded** — it
records the owner's *first* decision of 2026-09-08, which kept readiness in scope. The second decision
the same day split readiness out into `OPS-10`. A reader who stops at the dossier's last line builds the
wrong row.

### B.4 One row that is plan-ready but priced against a phone

**`AUD-1` — 1.25 d, and the plan was already re-written for your rule.** The 2026-09-08 decision —
*"when metadata is available from the audio source, use the source metadata; when one or more is
missing, use fingerprinting to augment"* — is **per field, not per track**, and applies to both BT and
FilePlayer. The plan was amended for it the same day and the estimate rose from 0.75 d accordingly.
⛔ **The flag must not be renamed**: the SQLite store already holds
`fingerprinting:useShazamForAllSources|true` and outranks both JSON layers; a rename orphans that row,
the new key falls through to `false`, and **BT album art dies** — the one outcome the row forbids.
(`fingerprinting:fpcalcPath` is already sitting orphaned there from the AcoustID→SongRec rename, so this
is measured, not theoretical.) It is not auto-mergeable: live audio path, user-visible metadata, and
**UAT needs a phone**.

---

## C. Blocked, and on exactly what

### C.1 Blocked on an owner DECISION

**The punch list's decision register is empty.** §7 *Still open* reads, verbatim: *"**None.** … `D31` is
now the most recent, answered 2026-09-05 … The board is still clear; only the date moved."* Everything
below is a **row-level** question, not an arc-level gate.

| Row | The decision | What each option costs |
|---|---|---|
| **`AUD-16`** | Remove the deprecated USB radio path, or keep it documented? Plus: is the `RaddyRF320BT/` submodule in or out? | **Remove:** blast radius across ~a dozen surfaces plus an orphaned SQLite store row. **Keep:** continued dead surface area, nothing else. ⭐ The dossier is explicit that *"a reasoned close as 'keep, documented' is an acceptable outcome"* — the deprecation **already shipped in behaviour** |
| **`AUD-17`** | AVRCP art has never worked. Implement OBEX BIP, **delete the dead path**, or upgrade BlueZ? | **BIP:** BlueZ 5.72 ships no client; you would be writing one. **Delete:** AVRCP art never becomes possible — but SongRec already supplies art on ~99% of rows. **Upgrade BlueZ:** on the appliance, for one feature. The dossier's steer is delete. Whichever you pick, the comment and the `:339`/`:358` log strings must be fixed — they claim MPRIS about a BlueZ interface and *"made a dead path look live for months"* |
| **`GV-10`** | Close it outright, or convert it to a cross-repo note? | Nothing shipped and nothing is left to build here — the row is **falsified as a Radio Console defect**. The only residual is whether RotaryPhone's wire value at `SmsTextIdx` is a full body or a snippet, decidable by one `curl`. **Cost of closing: zero.** This is the cheapest item on the board |
| **`AUD-19`** | Is History even *wrong*? | There is a real argument that history should record *what fingerprinting identified*. **If so the row closes as "correct, documented"** — a legitimate outcome. If it is wrong, it must call `AUD-1`'s shared predicate rather than re-implement it |
| **`PHN-8`** | Does speaking a message mark it read? | Interacts with `GV-6`'s dark `MarkReadEnabled` flag. Not decided; the plan must ask rather than assume |
| **`PHN-7`** | Keep polling REST and wait for RotaryPhone's convergence, or move `BellHealthService` onto the SignalR connection we already hold open? | **Wait:** predictive-degrade stays unbuildable — their REST `SystemStatus` returns `DateTime.UtcNow`, so it can never go stale. **Move:** we build against a path they have not finished changing |
| **`UI-15`** | Ship it, or close it with the correction appended? | The plan recommends **ship** — four lines, provably cannot affect the healthy path. But it also states the case against honestly: 36 lifetimes, zero firings, and `UI-13` was kept on that same reasoning one day earlier |

### C.2 Blocked on an owner ACTION — this is the real critical path

| # | Action | Why it matters | Time |
|---|---|---|---|
| 1 | **Run the `rotary-phone` deploy you were assigned.** Four RotaryPhone PRs (#78–#81) are merged and undeployed; `radio:5004` still runs the pre-#78 build. | ⚠ **Their top hazard lands on your audio, not theirs.** The tar-pipe fallback path in `Deploy-ToLinux.ps1` clobbers `appsettings.Production.json`, which carries `BluetoothAdapter: hci1`. If it fires, **their** config silently reverts and **your** A2DP breaks, with the cause in their file and no error pointing at it. Back the file up **off-box** first; confirm the rsync path actually ran; verify `hci1` after. Details: [`queue/inbound/2026-09-09-rotaryphone-deploy-handoff.md`](queue/inbound/2026-09-09-rotaryphone-deploy-handoff.md) | ~30 min + verification |
| 2 | **Install `radio-console-open` on the box before #79 deploys.** `KIOSK-3` shipped (#635) but **does not reach the box on a deploy** — `setup-kiosk.sh` installs it and `Deploy-ToLinux.ps1` never runs it; the live copy was still dated Aug 18. **The box has no checkout**, so the kiosk directory must be copied over first. | #79 removes `psidtsAgeSeconds`. Without the fix, the launcher's VOICE row reports **"needs sign-in" permanently, on every launch, regardless of GV's actual state** | ~10 min |
| 3 | **Answer RotaryPhone's two pre-deploy questions.** They asked and said *"tell us before we deploy"*; neither has been answered, and you now control the deploy. | ⚠ **Our first answer to Q1 was wrong and has been retracted.** We told them *"zero consumers"* of `psidtsAgeSeconds` three times; there was one — a **shell script**, invisible to a `src/`-scoped grep. That is exactly the shape of Q2 (*does anything key off `acknowledged == false`?*). A grep of `BellHealthService.cs`, `ApiModels.cs` and `PhoneDashboardPanel.razor` finds no consumer — **but that is the same instrument that was wrong last week.** Re-derive rather than re-assert | 15 min |
| 4 | **Run `PHN-2`'s owner UAT.** Nine `SOUND`-class items — a human confirming the room changed — are still unverified on the last P0 to ship. | Script is standalone at [`design/plans/PHN-2-retire-the-audio-element.md` §3](../design/plans/PHN-2-retire-the-audio-element.md) | **15 min at the cabinet** |
| 5 | **Run `AUD-12`'s deferred UAT.** It shipped as #623 with the gate outstanding. ⛔ **The merge moved the gate; it did not satisfy it** — no phone was connected, no A2DP source played, no pause/resume performed, the appliance was never touched. | ⚠ Read the plan's four-way vacuity table first: **three of the four make the UAT lie in the dangerous direction**, including its own step 4, where pressing the transport button clears the stall | needs a phone |
| 6 | **Deploy the Radio side.** `GV-12`, `UI-11`, `UI-12`, `UI-13`, `UI-14`, `AUD-12`, `KIOSK-3` and `UI-7` all record themselves as **merged but not deployed**. `GV-12`'s row notes the appliance was 13 PRs behind at merge time. | Several rows' UAT is impossible until their code is on the box; the backlog is compounding | one deploy |
| 7 | **Make the phone available.** `AUD-14` is explicitly *"currently blocked — the owner's phone is unavailable"*; `AUD-1`, `AUD-10`, `AUD-12` and `AUD-17` all need it too. | Five rows unblock together | — |

**Also outstanding, lower stakes:** `D18` — file the bell-failure contract request to RotaryPhone
(never filed). `D20` — write `docs/BUILDER_PROMPT.md`; **every Builder is pointed at a document that has
never existed**, and §6 records this as *"the owner's 20 minutes, not a row"*. Ratify the `PHN-4` id
(§9 note) — cheap now, expensive once cited.

### C.3 Blocked on another row

| Row | Waits on | Why |
|---|---|---|
| `AUD-19` | **`AUD-1`** | ⛔ *"This row has no meaning until the per-field rule exists to diverge from. Do not claim it first."* But it must be **named in `AUD-1`'s PR body before merge** — left undocumented, the divergence reads as a failed fix |
| `OPS-10` | **`OPS-3`** | `OPS-3` ships the coupling this makes safe. *"Claiming this first is possible but pointless"* — without `Upholds=` nothing starts web off the API's active edge except the deploy, which already polls |
| `PHN-8` | ⊗ **`GV-7`** | Not a dependency — a **mutual exclusion**. ⛔ *"`GV-7` must not run concurrently"*; both re-scope the same surface |
| `AUD-19`, `AUD-1` | ⚠ **`AUD-18` / `AUD-12` for UAT only** | With either live, **no identification lands and a broken build looks correct.** A green fingerprinting UAT is vacuous until the tap is known good |

⛔ **`O3` (`AUD-2` before `AUD-4`) is retracted — do not honour it.** See §A.4.
⛔ **Any ordering that schedules `GV-5` is dead** — parked by `D31`.
⛔ **`UI-10` is not upstream of `GV-12`**, despite three documents having said so: a reconnected Blazor
circuit resumes the same component instances and re-mounts nothing, so neither branch produces a fetch.

---

## D. Not ready — needs a plan first

**Genuinely unplanned (11):** `AUD-10` · `AUD-14` · `AUD-16` · `AUD-17` · `AUD-18` · `AUD-19` ·
`AUD-20` · `GV-7` · `PHN-7` · `PHN-8` · `UI-10` · `OPS-10`.

Four of those are cheap to plan because the analysis is already done:

- **`AUD-18`** — the dossier recommends splitting it: *"a restart-after-N-empty-captures watchdog is
  unit-testable and can ship without finding the cause."* ⚠ The failure needs **~37 h of uptime** to
  appear, so no test reproduces it and no green run proves the root-cause fix. Say so rather than
  implying otherwise.
- **`UI-10`** — the root cause is already identified in the row's own scope question #2, answered:
  `Radio.API/Program.cs:87` sets `KeepAliveInterval = 30 s` against a 30 s client default, violating the
  documented `ServerTimeout ≥ 2 × KeepAliveInterval` rule exactly. ⛔ **The title is wrong** — it is not
  the Blazor circuit (`App.razor:76` configures that at 120 s and cannot emit a 30 s message); it is the
  four `HubConnection` clients.
- **`OPS-10`** — *"the analysis already exists"* in `OPS-3`'s plan and should move here. ⛔ `Type=notify`
  was investigated and **rejected**; do not re-propose it without reading the dossier.
- **`AUD-20`** — ⛔ **its value is the five falsified hypotheses. Do not re-run them.** The first honest
  deliverable is an **answer**, not a fix, and *"cause not determined, here is what was excluded"* is a
  legitimate close. Most promising untested lead: correlate with **SDR stream uptime**, not date.

### ⚠ Three rows are marked "plan TBD" and have a finished plan on disk

| Row | Queue index says | On disk |
|---|---|---|
| `PHN-9` | *plan TBD — ⛔ NOT a small fix* | [`design/plans/PHN-9-the-badge-that-reads-a-latch-instead-of-fetching.md`](../design/plans/PHN-9-the-badge-that-reads-a-latch-instead-of-fetching.md) — 1,283 lines, 11 sections, gates and mutation matrix |
| `UI-15` | *plan TBD — establish reachability on the box FIRST* | [`design/plans/UI-15-the-suppression-that-only-a-failed-startup-can-reach.md`](../design/plans/UI-15-the-suppression-that-only-a-failed-startup-can-reach.md) — 770 lines; **the reachability question is answered in it** |
| `AUD-1` | *0.75 d (F1) / 0.5 d (F2)* against a split the owner superseded | [`design/plans/AUD-1-split-the-fingerprint-gate-from-the-overwrite.md`](../design/plans/AUD-1-split-the-fingerprint-gate-from-the-overwrite.md) — **amended for the owner's rule, re-priced to 1.25 d** |

Both plans landed in `17e56719` (*"docs: plans for PHN-9 and UI-15, both of which corrected their own
rows"*). **A Builder reading only the queue index would re-plan work that is already planned.**

---

## E. The arcs — sequencing inside a subject beats global priority

### E.1 The BT capture arc — `AUD-10` · `AUD-11` · `AUD-15` · `AUD-18` · `AUD-14`

These are five *different* failures that all look like "the Bluetooth stopped", and the dossiers are
emphatic about not conflating them:

- `AUD-10` — the **transport** dies on pause and never returns.
- `AUD-11` — when the node vanishes, capture **silently re-links to the built-in line-in**. Every
  indicator stays green; *"the only evidence anything is wrong is that the room is quiet."*
- `AUD-15` — the ring buffer **runs empty during playback** because the resampler is open-loop.
- `AUD-18` — the **fingerprint tap** reads zero from the mixer. A different path from playback.
- `AUD-14` — a stale AVRCP **watcher** raises `Playing` against a torn-down source.

**Sequence: `AUD-11` before `AUD-10`.** `AUD-11` is what makes `AUD-10` *silent* rather than loud —
fixing the re-link first means the next `AUD-10` reproduction reports a fault instead of quietly
recording an unplugged jack. Neither blocks the other, but debugging `AUD-10` on a box that lies about
its capture source is wasted box time. Both need the handset in the same session; **run them together.**

**`AUD-18` and `AUD-20` share the strongest untested lead** — SDR stream uptime — and `AUD-18` recovered
on an RTL-SDR *stream* restart with `radio-api` uptime unbroken. That refines `MEMORY.md`'s long-standing
*"restart fixes"* note: **restarting the source is enough; the service need not be bounced.** Investigate
them in one pass even though only `AUD-18` is P1.

**`AUD-18` is a precondition for other rows' verification, not just a bug.** While the tap returns zero,
no identification lands, so `AUD-1`'s and `AUD-19`'s UAT can pass vacuously.

### E.2 The metadata arc — `AUD-1` → `AUD-19`, with `AUD-17` beside it

`AUD-1` establishes the per-field rule. `AUD-19` is the same rule in `PlayHistoryTracker`, and has no
meaning until `AUD-1` exists. `AUD-17` is adjacent and self-correcting: **AVRCP never supplies art on
this box**, so under the new rule art is always "missing" and fingerprinting always fills it — which is
exactly today's working ~99%.

⚠ **`MetadataSource` is provenance and is load-bearing.** `AUD-17` was retracted for misreading exactly
this column — `Source` records the **title's** provenance, not the art's. Any row reasoning from
`Source='Avrcp'` about *art* is reasoning from the wrong column.

### E.3 The phone surface — `PHN-7` · `PHN-8` · `PHN-9` · `GV-7` · `GV-10`

The ADR-029 arc is **complete** — eight PRs, `PHN-1a` through `PHN-3`, all merged. What remains is
surface work on top of it.

- **`PHN-7` splits three ways and only one part is buildable now.** ⭐ Ship the **`Unknown` render fix
  first and soon** — `SystemStatus.Ht801IpAddress` became `string?` in their #77 and reports the
  *resolved* address; `PhoneDashboardPanel.razor:63` renders null as `--`, which reads as *"no HT801"*.
  ⚠ **Time-boxed: this must land before RotaryPhone deploys #77** — which is action C.2 #1. The
  staleness test can be written now. The predictive-degrade rule waits on their convergence.
- ⛔ **`PHN-7`'s copy guidance reversed once and the reversal is not obvious.** An owner decision made
  the bell note session-scoped; a second decision the same day **reversed it** — `acknowledged` **will**
  be persisted. *"Do NOT add session-scoped wording — the correction above told this row to do exactly
  that, and it is now wrong."*
- **`GV-7` before `PHN-8`, or `PHN-8` before `GV-7` — but never both at once.**
- **`GV-10` is a close, not a build.** Cheapest thing on the board.

### E.4 The state-broadcast arc — `UI-8` · `UI-10` · `UI-15`

The `UI-11`…`UI-15` family is one subject: **state that stops tracking reality with nothing reporting a
fault.** Four of the five shipped on 2026-09-09. What survives is `UI-15` (ready), `UI-10` (diagnosed,
unplanned) and `UI-8` (unrelated — CSS).

⚠ **This family has the worst premise-accuracy record in the repo, and it is worth knowing why.** `UI-11`'s
headline was retracted outright (*"`Radio.Web` has no SPA fallback and never had one"* — both cited
incidents were RotaryPhone's, and our own archive line 99 said so). `UI-13`'s headline was falsified and
re-tiered P2 → P3 (`SendAsync` cannot throw on a failed broadcast). `UI-14`'s discriminator fell, then
its replacement fell too — *"right conclusion, wrong reason, for the third row running."* **When you
read a `UI-*` title, read the dossier's last section before believing it.**

### E.5 Service lifecycle — `OPS-3` → `OPS-10`

Strictly ordered, and never to be merged into one row. `OPS-3` ships `BindsTo=` + `Upholds=`, unit files
only, so **merging and deploying both stay inert** — rollout is a separate supervised step. `OPS-10`
then makes *"active"* mean *"the hub answers"*. ⛔ **One number cannot be established without the box**:
how long `radio-api` takes from exec to the hub answering, cold. It is nowhere in the repo, and nobody
has recorded whether the deploy's 20-iteration poll has ever reached its limit. **Measure it in the
supervised session; do not write a task that assumes the answer.**

⚠ The hard ceiling is real: `4 × (RestartSec + t_ready) ≤ 300` → `t_ready ≤ ~65 s`, past which
`radio-api` **crash-loops forever**, which is worse than the wedge `OPS-3` reduces.

### E.6 Kiosk and the cross-repo deploy — the one true sequence right now

```
  install radio-console-open on the box   (KIOSK-3, shipped but not delivered)
              ↓  must precede
  deploy RotaryPhone #78–#81               (#79 removes psidtsAgeSeconds)
              ↓  and before that
  answer their two questions               (we got Q1 wrong once already)
              ↓  and around all of it
  back up appsettings.Production.json OFF-BOX, verify BluetoothAdapter: hci1 after
```

Get this order wrong and the launcher reports "needs sign-in" forever, or your A2DP breaks with the
cause sitting in another repo's config file.

---

## F. What I would do next, in order

1. **Merge `UI-8` and `UI-15`.** Free, auto-mergeable, zero risk, and `UI-8` is a literal zero-visual-change
   cleanup. Do it first because it costs you nothing and shortens the board by two.
2. **Do the cabinet hour.** In one sitting: `PHN-2`'s nine `SOUND` items (15 min, script is standalone),
   then `AUD-12`'s deferred UAT with the phone. **These are the only two verification debts sitting on
   already-shipped code**, and one of them is on the last P0. Everything else you might do at the panel
   can wait; these cannot, because they are the difference between "merged" and "true".
3. **Run the `rotary-phone` deploy — but install `radio-console-open` first and answer their two
   questions before either.** This is the only genuinely time-boxed thing on the board: their PRs are
   merged and waiting, and one of them removes a field our launcher still reads. Deploy the Radio side
   in the same session; eight rows are merged-but-undeployed and the backlog is compounding.
4. **Close `GV-10`.** One decision, zero code, and it removes a falsified row from a queue where a
   falsified row is the most expensive kind of noise. Do it while you are already deciding things.
5. **Then let a Builder take `UX-1`, `AUD-5`, `AUD-2`, `PHN-9`** in that order — all plan-complete, all
   unblocked, and `UX-1` is the only one with a visible payoff. `AUD-5` and `AUD-2` are both real
   correctness holes on the audio path with finished plans and modest prices (0.5 d, 1 d).
6. **Book one box session with the handset and spend it on `AUD-11` + `AUD-10` together.** They are the
   same hardware state seen twice, and `AUD-11` is what makes `AUD-10` diagnosable. This is the highest-value
   use of a phone-plus-box hour on the board.
7. **Decide `AUD-1`'s tier.** The punch list says post-GA; the queue is scheduling `AUD-19` behind it and
   you already issued its behavioural rule. Both positions are defensible — but they cannot both be
   acted on, and right now nobody has asked you.
8. **Ask for a Planner pass on the punch list.** Eleven shipped items are still listed as open across
   three tiers, and thirty P1 items have no queue row. **Neither is a correctness bug; both make the
   document unusable as a picture of what is left**, which is the job it exists to do. Half a day of
   reconciliation buys back the artifact.

**What I would *not* do next:** `AUD-4` (2–3 d refactor, no user-visible symptom, and its stated
priority justification was itself retracted — *"re-price the row if that was what bought its
priority"*); `AUD-20` (P3, no demonstrated user-visible symptom, and its honest deliverable is an
answer nobody is waiting on); `OPS-10` (P3, and pointless before `OPS-3`).

---

## G. Findings recorded, not fixed

### G.1 The punch list carries eleven shipped items as open

Verified against `BUILDER_QUEUE_ARCHIVE.md` and, for two of them, against the working tree.

| Item | Punch list | Actually |
|---|---|---|
| `PHN-1` | P0, open, `Queued? No` | shipped — #528/#534/#556/#558/**#561**/**#564** |
| `PHN-2` | P0, open, *"has not started"*, *"PR 6 has no plan file"* | shipped **#566**; the plan exists |
| `LOG-11` | §4.3 P1, open, `Queued? No`, 30 min | shipped — §8 quick win #10, **#494**. `CLAUDE.md` documents it as live on the box since 2026-09-02 |
| `TTS-11` | §4.4 P1, open, `Queued? No`, 0.5 h | shipped **#569**. ⚠ The archive row's *Spec/handoff* cell points **at punch list §4.4** — the link exists in one direction only |
| `UI-1` | §5 P2, open, `Queued? No`, 30 min | shipped — §8 quick win #8, **#492**. `src/Radio.Web/Components/Pages/Diagnostic.razor` **does not exist in the tree** |
| `UI-5` | §5 P2, open, `Queued? No`, 15 min | shipped — §8 quick win #9, **#489**. The *"planned follow-up"* string is gone from `DevTray.razor` |
| `OPS-2` | §5 P2, open, `Yes 📋` | shipped **#585** |
| `TEST-2` | §5 P2, open confirm-or-close, `Yes 📋` | shipped **#614** — *"the harness it waited for was never needed"* |
| `GV-6` | §5 P2, open, `Yes 📋` | shipped **#594** |
| `GV-9` | §5 P2, open, `Yes 📋` | shipped **#608** |
| `UI-6` | §5 P2, open, `Yes 📋` | shipped **#596** |

**`UI-1`, `UI-5` and `LOG-11` are the sharpest of these**, because the same document marks them ✅ in §8
while listing them open in §4/§5 — the punch list contradicts itself, not just the queue.

### G.2 ID collisions

**`AUD` was reconciled on 2026-09-09** and is now one namespace: the punch list's `AUD-10`/`11`/`11a`/`12`
became **`AUD-21`/`22`/`22a`/`23`**, all four are RESERVED, and the next free number is **`AUD-24`**.
⚠ **The queue's `AUD-10` (A2DP transport dies on pause) and `AUD-11` (capture re-links to line-in) are
different items from the punch list's former `AUD-10`/`AUD-11`. Do not conflate them.**

**`BUILDER_QUEUE.md:40-41` says the other prefixes *"are unaudited and may carry the same collision."*
That sentence is now answerable: audited, and they do not.** Method: for every ID appearing in both
documents, compare the subject each gives it — run twice, independently.

| Prefix | Result | Next free |
|---|---|---|
| `GV` | No collision. `GV-1`…`GV-10` agree; `GV-12` is queue-only | `GV-13` |
| `UI` | No collision. `UI-1`…`UI-6` agree; `UI-7`, `UI-9`, `UI-11`…`UI-15` are queue-only and return **zero** punch-list matches | `UI-16` |
| `TEST` | No collision. `TEST-1` and `TEST-4` agree; `TEST-5`/`6`/`8` are punch-list-only | `TEST-9` |
| `OPS` | No collision. `OPS-1`, `OPS-5`, `OPS-8` agree; `OPS-7`/`OPS-9` are queue-only; `OPS-4`/`OPS-6` punch-list-only | `OPS-11` |
| `PHN` | No collision. `PHN-1`…`PHN-5` agree, `PHN-1a`…`1f` included; `PHN-7`…`PHN-9` are queue-only | `PHN-10` |
| `ENC` | No collision. `ENC-4`, `ENC-4c`, `ENC-5`, `ENC-7`, `ENC-8`, `ENC-12`, `ENC-6`, `ENC-20` all match | `ENC-22` |
| `KIOSK` | No collision — `KIOSK-2` agrees; the prefix is otherwise punch-list-absent | `KIOSK-4` |

⭐ **`GV-11` and `PHN-6` were rejected candidates recorded at `:1491` and never minted — both numbers are
genuinely free.**

⭐ **There is a causal reason `AUD` drifted and these did not, and it is worth acting on.** The archive
rows **cite the punch list by section for their own IDs** — `:403` *"punch list §3.0 `ENC-4`"*, `:509`
*"§3.5 `ENC-8`"*, the six `PHN-1a`–`1f` rows all *"punch list §3.5 `PHN-1`, §2 `O6`"*, `:1294` *"§4.6
`TEST-7`"*, `:1230` *"punch-list §5 `UI-6`"* — and the punch list reciprocates at `:1487` (*"`PHN-5` was
minted past it"*), treating a queue-minted number as consuming the shared namespace. **`AUD` was the one
prefix where neither document ever referenced the other's numbering.** The collision was not bad luck;
it was the absence of a cross-citation. The §9 note on `PHN-4` (`:1471-1508`) is the same hazard handled
*correctly* once — it checks whether an id is cited elsewhere before reuse and states its own
falsification condition.

⚠ **Two entries read as scope evolution, not collision** — flagged so a future audit does not re-open
them. `ENC-5`: punch *"the SOURCE overlay, with the radio bands folded in"* vs archive *"…the shared
selector component, and the router remap"* — the punch list titles what was asked, the archive what
shipped. `PHN-2`: punch *"voicemail through the audio engine (Feature A)"* vs archive *"retire the
`<audio>` element (Feature A)"* — same ADR, same feature letter, same `VoicemailPlayer.razor:8`,
described from opposite ends.

⚠ **Observation, not a collision:** `ENC-0`, `ENC-1`, `ENC-2`, `ENC-3` and `ENC-11` are recorded as
shipped in the punch list but have **no archive row at all** — part of the encoder arc was tracked
outside the queue entirely.

⚠ **Two same-family tier splits nobody has explained**, flagged rather than resolved: `AUD-22a` sits at
P2 while its parent `AUD-22` is P1; and `TEST-3` states *"⬆ Promoted P2 → P1"* inside its own cell while
still being printed in the §5 P2 table.

### G.3 Other contradictions worth carrying

- **The punch list disagrees with itself about `PHN-1`.** Its banner says **eight** PRs with 1–4 landed;
  §9's index cell, 1,348 lines later, says *"PRs 1 and 2 of the **seven-PR** ADR-029 arc"*. Two counts,
  two landed-sets, one document, both stale.
- **`queue/ORDERING-NOTES.md:46` carries the same stale claim** (`PHN-1e` claimable, `PHN-1f` unplanned).
  So the queue's *ordering* artifact agrees with the punch list's error while the queue's *archive*
  refutes both. **A reader who consults only `ORDERING-NOTES` gets the wrong answer.**
- ⛔ **RETRACTED 2026-09-09 by the coordinator — this entry did not survive checking.** It read:
  *"`TEST-6` asserts **53** warnings as canonical and measured; `CLAUDE.md` asserts **47** … a Builder
  holding the wrong figure either waves through six new warnings or chases six that were never there."*
  **There is no `TEST-6` dossier, and the string `53 warning` appears nowhere in `docs/`, `design/` or
  `CLAUDE.md`.** The conflict is not reproducible. **The baseline is 47/0 and is not in dispute.**
  ⭐ Retracted rather than deleted because this was the one finding here that would have been
  *operationally* dangerous, and because a roadmap is a report — **reports get checked.**

- ⭐ **A REAL baseline hazard, found the same day and worth more than the retracted one:**
  **an INCREMENTAL Release build reports `0 Warning(s)`.** Measured by `UI-8`'s Builder; `--no-incremental`
  restored the true **47/0**. ⚠ **A Builder seeing `0 Warning(s)` concludes the baseline improved** and
  waves through anything, because the gate is *equality* and zero looks like success. **Always
  `--no-incremental` when measuring the warning gate.**
- **`OPS-3`'s dossier ends on a superseded decision** (readiness in scope). The corrected position lives
  in the queue index and `OPS-10.md:5`, not in `OPS-3.md`.
- **`AUD-13.md` and `AUD-15.md` end before the corrections that overturned them.** Both retractions live
  only in `design/plans/` and the queue index. Build either from the dossier alone and you carry a
  refuted premise: the unreachable empty-string defect, and the callback-lock red herring.
- **`AUD-19` was cited before it existed** — `AUD-1.md:113` claimed since 2026-09-08 that it was *"filed
  … documented in `design/FUTURE-WORK.md`"*; it was in neither, while `AUD-1`'s Builder was instructed to
  cite it before merge. The row exists now; the citation was a coordinator error.

### G.4 What I could not determine

- **Whether the ~30 open punch-list P1 items are still wanted.** They are tiered *"blocks calling it
  finished"*, have no queue row, no plan and — for the `LOG-*` and `TTS-*` workstreams — no activity
  since the encoder arc completed. That may be deliberate deferral or it may be drift. **The documents
  do not say, and I did not guess.**
- **Whether `acknowledged == false` is consumed anywhere.** A grep of the three obvious consumers finds
  nothing, but that is the same instrument that produced the wrong `psidtsAgeSeconds` answer. Owed to
  RotaryPhone as a **re-derivation**, not a re-assertion.
- **How far behind `main` the appliance actually is.** Eight rows record themselves as merged-but-not-deployed
  and one cites *"13 PRs behind at merge time"*, but no current figure exists in the repo and I did not
  touch the box.
- **`SEC-1`'s true state.** §9 says *"closed by verification"*; `D15` says the branch is *"awaiting
  review"*; §3.5 says the branch named in the row does not exist and the real one is *"0 commits ahead of
  `main`"*. Three statements, not obviously reconcilable from the documents alone.
