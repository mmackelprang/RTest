# HANDOFF — GA punch list for the cabinet install

**Status:** **`[APPROVED 2026-09-01 — EXECUTING]`**. **All 11 §8 quick wins are shipped, plus `TEST-1`, `TEST-3`, `LOG-1`, `LOG-11`, `OPS-1`, `AUD-6`, `AUD-7`, `SEC-1`, and the first half of the encoder arc — `ENC-0`/`ENC-0a`, `ENC-1`, `ENC-2`, `ENC-3`, `ENC-11`/`ENC-11a` — all deployed and verified on the box.**
> ⭐ **THE ENCODER ARC IS COMPLETE — all 12 encoder P0s shipped, 2026-09-02/03.** `ENC-0` `ENC-1` `ENC-2`
> `ENC-3` `ENC-4` `ENC-5` `ENC-6` `ENC-7` `ENC-8` `ENC-11` `ENC-12` `ENC-15`. The knobs are live, the router
> matches the escutcheon (`0 = Volume · 1 = SOURCE · 2 = PRESETS · 3 = Tuning`), the HUD renders on the
> panel's real axis, and `ENC-11`'s fault model finally has a voice. This was priced at 3–4 working weeks.
>
> ⚠ **Two things temper that, and both are recorded rather than glossed.** **(1) `ENC-15`'s gate FAILED**, so
> panel blanking is withdrawn permanently — touch cannot wake a blanked panel *by construction*, and the
> encoders are not compositor input devices either. **(2) Roughly half of every encoder row's UAT could not be
> run**, because there is no software path to inject encoder input; every row stated that as an uncovered gap
> rather than a pass, and `ENC-17` is filed to close it. **The behaviour a guest actually touches is the part
> least verified**, and it still wants the owner's hand on the panel.
>
> **P0 remaining: 2, both in the phone arc.** `PHN-1` is
> **partially shipped** — ⚠ **the ADR-029 arc is EIGHT PRs, not seven, and PRs 1-4 have landed:**
> `PHN-1a` ([#528](https://github.com/mmackelprang/RTest/pull/528)),
> `PHN-1b` ([#534](https://github.com/mmackelprang/RTest/pull/534)), `PHN-1c` ([#556](https://github.com/mmackelprang/RTest/pull/556)) and
> `PHN-1d` ([#558](https://github.com/mmackelprang/RTest/pull/558)),
> so **PRs 5, 5b, 6 and 7 remain**. **This line previously read** *"the arc is seven PRs and PRs 1, 2 and 3
> landed … so PRs 4-7 remain"*, which was true when it was written and is now wrong twice over: PR 4
> has shipped, and **owner decision `D28` split PR 5 into `PHN-1e` (server-owned state) and `PHN-1f` (the
> mirror-case queue)** — see `docs/BUILDER_QUEUE.md` § *Dependency / ordering notes*. `PHN-1e` is queued
> and claimable; **`PHN-1f` is queued but has no plan yet**; `PHN-2` has not started and sits behind them (`O6`). ⚠ **As of `PHN-1c` the seam is
> reachable over HTTP** — `/api/audio/events`, six routes, both arms — but **`GvMedia:Enabled` still ships
> `false`**, so nothing fetches on a stock box, and `Radio.Web` still plays voicemail through its own
> `<audio>` element until PR 6. **That a voicemail is audible remains unverified and is PR 6's UAT**, along
> with `./data/gvmedia` writability, which `PHN-1c` re-carried rather than claimed.
>
> ✅ **Shipped 2026-09-02:** `ENC-4` ([#519](https://github.com/mmackelprang/RTest/pull/519), geometry corrected by [#526](https://github.com/mmackelprang/RTest/pull/526)),
> `ENC-5` ([#536](https://github.com/mmackelprang/RTest/pull/536)), `ENC-8` ([#527](https://github.com/mmackelprang/RTest/pull/527)),
> `ENC-12` ([#535](https://github.com/mmackelprang/RTest/pull/535)),
> **`ENC-6` ([#539](https://github.com/mmackelprang/RTest/pull/539) — the non-blanking half only)**, and `ENC-15` — closed with a **FAILED**
> gate, which removes `ENC-6`'s blanking half from scope entirely and is why `#539` ships three states rather than five. `TTS-1`'s two P0 parts are both closed; part (iii) is P1. The owner has read §7, closed `D23` / `D24` / `D9`, and
authorised autonomous execution against this list in the §2 order, merging on green review + tests + UAT.
**Every decision is now closed** — `D25` was answered 2026-09-02 (full ADR-029 arc, no stopgap).
⚠ **AMENDED 2026-09-02 — the as-built front panel.** The owner supplied the real drawing, now committed at
**`design/hardware/front-panel-layout_4.svg`**. **D2's order held; D2's dimensions did not.** The knobs are
**one vertical column left of the screen at a uniform 29.63 mm pitch, all 15 mm** — not a horizontal row at
90/70/90 mm with two sizes. `O9`, the `D2` and `D3` rows, and the `ENC-4` entry are corrected in place.
~~**`ENC-4` shipped its HUD on the wrong axis** and needs a 90° rotation (Designer Rev 4 §6.2 specifies it;
that is a separate PR).~~ ✅ **Rotated and shipped 2026-09-02 as `ENC-4c`** — see the `ENC-4` entry. Designer Rev 4 also **re-derived §10.1's mis-grab safety conclusion**, whose stated
premise was the 90 mm spacing.
**Designer Rev 5 then closed the rotation's open questions** — left-edge collisions resolved (accept
transient occlusion; four alternatives rejected with reasons), the phase contract settled (§6.10), and two
owner decisions recorded: **D29** — *recorded as `D26` until 2026-09-04, when that number was found to be
carrying the eSpeak removal as well; renumbered, see §7* — the knobs are straight-sided so the engraving
clearance holds (§9.5), and **D27** `prefers-reduced-motion` keeps the shipped sweeping ring (§6.5, and §6
below). **Both now have entries in §7; until 2026-09-04 neither did**, and this banner was the only place
either one existed.
Original state: planner-phase draft, nothing queued.
**Date:** 2026-08-19
**Author:** Planner, consolidating six research scouts and one Designer pass.
**Consumers:** Owner (§7 decisions, §8 quick wins) → Architect (three escalations) → Planner (spec + plan per
approved item) → Builder (queue rows, one PR per cycle).

> **⚠ RECONCILED AGAINST DESIGNER REV 2 (2026-08-19), `HANDOFF-rotary-encoder-mapping.md`, now 1,033 lines.**
> Four things changed that invalidate anything written against Rev 1:
>
> 1. **Tone is out in full** — the owner ruled no new audio DSP for GA. Knob 2's slot is filled by
>    **PRESETS** (the saved-station bank, on a knob), and Designer **withdrew its own Browse fallback**
>    rather than defaulting to it. **Decision D6 is CLOSED**, and the Architect tone-DSP ADR dependency
>    goes with it.
> 2. **The physical order changed:** `VOLUME · TONE · SOURCE · TUNING` → **`VOLUME · SOURCE · PRESETS ·
>    TUNING`**, ~~with ≈90 mm outer gaps and ≈70 mm between the inner pair~~. **This is the irreversible one —
>    D2 is restated below, and any older copy of the order is now actively dangerous.**
>    ⚠ **AMENDED 2026-09-02 — the owner's drawing arrived and the pitches are gone.** The **order is
>    unchanged and still final**; the **geometry is not what D2 recorded**. The panel puts all four knobs in
>    **one vertical column at a uniform 29.63 mm pitch, all 15 mm**, left of the screen. `90 + 70 + 90 =
>    250 mm` cannot fit a 152.4 mm panel — **the orientation forces the pitch, and this is a consequence,
>    not a drafting error.** Drawing committed at `design/hardware/front-panel-layout_4.svg`; Designer
>    Rev 4 §9 is the build document and §10.1 re-derives the safety conclusion that lost its premise.
> 3. **Startup config push is new, owner-directed, runtime work** (Designer §7): push per-encoder config on
>    every detect and reconnect, verify by read-back, tier the fault by which field disagreed. That is
>    `ENC-11` … `ENC-14` — four new items, one of them P0.
> 4. **The button model is named and uniform: ACTION on all four knobs.** No button ever changes what
>    turning its knob does.
>
> The `sbyte` retraction this document made independently is now **also formally recorded by Designer**
> (Rev 2 §0 and §5.6). The two documents agree. Section references below point at Rev 2's numbering.

---

## 0. What this is and how to use it

This is the punch list for putting Grandpa Anderson's console radio **into the cabinet**. It is not a
backlog dump and it is not a list of everything technically outstanding. Every item was triaged against one
question and one question only:

> **What must be true before this ships into a piece of furniture in a family home, where it will be used
> by the owner and by guests who walk up to it expecting a radio?**

That framing does a lot of work. It promotes things that would normally be low priority — a settings field
that lies, a log file with no retention cap, a knob that acts silently — because in a living room those are
not cosmetic. It demotes things that would normally be high priority — refactors, coverage gaps, hygiene —
because a cabinet does not care about them. Several items in here are *worse* than their engineering
severity suggests, and several are *better*. Where that is true it is argued, not asserted.

### The deployment reality this is priced against

- An Intel N100 Ubuntu box **inside a sealed cabinet**, on WiFi, with a 1920×720 touchscreen in kiosk Chrome.
- **Recoverable only by SSH from a laptop.** Physical access is awkward once the cabinet is closed up.
- Heavy `journalctl` reads and DB churn on this box **correlate with audio distortion** (`CLAUDE.md` §
  "What the box actually is"). That single fact re-ranks half of the logging workstream.
- It will run for **weeks between restarts**. Every long-uptime bug is an exposure case, not a curiosity.

### How a future session uses this document

1. Read §1 (tier criteria) and §2 (ordering constraints). §2 is the part that will bite you if you skip it.
2. Pick **one workstream** and run it end to end. They are cut so they do not interleave: Encoders, Audio
   Reliability, Logging & Distortion, Phone & TTS, UI Surface, Test & Ops Hygiene, Cross-Repo.
3. For each item you intend to ship: confirm the tier still holds (several are conditional on §7 answers),
   then run the normal Planner cycle — spec, plan, **then** a queue row.
4. **Do not add rows to `docs/BUILDER_QUEUE.md` from this document wholesale.** This is a review artifact.
   The queue is the dispatch artifact. A row goes in when the owner has approved the item *and* a plan
   exists that Builder can execute without re-planning.

### Relationship to `docs/BUILDER_QUEUE.md`

The queue currently holds **17 open `📋` rows and 0 in flight**. Every one of them appears in this document
with its existing ID, its tier argued explicitly, and a note that it is **already queued**. New work gets a
new ID with a clear prefix (`ENC-`, `LOG-`, `PHN-`, `TTS-`, `UI-`, `XR-`, `SEC-`, `HW-`) and is marked **not
queued**. Nothing here re-orders the existing queue — the owner sets priority; Planner appends. Where this
document disagrees with the queue's current order it says so in §2 and asks rather than shuffles.

**Scale: 74 tracked items across 7 workstreams, plus 22 explicitly parked.** Tier counts and the full
index are in §9.

> **✅ STATUS AS OF 2026-08-19: THE OWNER HAS ANSWERED ALMOST EVERY OPEN DECISION.** §7 is no longer a list
> of questions — it is a record of answers. The headline is **D1: the knobs ship live at install**, which
> collapses the conditional tiering and makes **22 items P0**, of which **11 are the encoder arc** at ≈3–4
> working weeks. **✅ Designer Rev 3 landed (1,126 lines) — since amended to Rev 4 (as-built panel) and Rev 5 (HUD collisions, `ENC-15` absorbed) — and unblocks everything that was marked
> pending** — `ENC-6` (the D8 wake collision) and `ENC-8` (the D21 settings surface) are both fully specced,
> `ENC-13`'s safe-baseline idea was withdrawn by Designer and folded into `ENC-8`, and one new P0 gate
> (`ENC-15`) and one new decoder hazard (the re-baseline rule, in `ENC-1`) came out of it. **A short startup
> handoff for the next session lives at [`docs/HANDOFF-NEXT-SESSION.md`](HANDOFF-NEXT-SESSION.md).**

---

## 1. Tier criteria

The tiers are defined by *consequence in the cabinet*, not by engineering severity. A tier assignment that
cannot be argued from one of these tests is a bug in this document.

### P0 — Blocks installation

An item is P0 if **at least one** of these is true:

- **(a) Wrong or dangerous on day one.** A guest's first interaction produces the wrong result, or a
  physically unsafe one — volume slam being the only real safety hazard this machine has.
- **(b) Embarrassing in front of people.** The machine reports success and does nothing; a control does
  nothing; two sounds play over each other at full level; the screen is dark.
- **(c) Unrecoverable without a laptop.** The disk fills, a service wedges, or the only in-app diagnostic
  surface is itself unsafe to open. On a sealed cabinet on WiFi this is the tier's sharpest test.
- **(d) It is the substrate every other item's verification rests on.** If you cannot trust the test suite
  or the deploy, you cannot claim anything else is fixed.
- **(e) It becomes permanent at install.** Anything that gets drilled, engraved, or mounted.

### P1 — Blocks calling it finished

A real defect a user will hit, that a person who knows the workaround can survive for a few weeks. The test:
*would you be comfortable saying "yes, it's done" with this outstanding?* If the honest answer is "it's done
except…", it is P1.

### P2 — Post-GA

Genuine work with real value and no schedule pressure. Refactors, coverage, hygiene, second-order polish,
and features that are nice rather than expected.

### P3 / Won't do

Explicitly parked, with the reason recorded so a future session does not re-litigate it. §6.

### Conditional tiers — **there are none left**

An earlier revision of this document tiered the entire encoder arc conditionally: *P0 if the knobs ship
live, P1 if the owner accepts blank, inert knobs at install.*

> **✅ D1 ANSWERED 2026-08-19: THE KNOBS SHIP LIVE AT INSTALL.**

**The hedge is gone and every tier below is unconditional.** The consequence is the single largest change
this document has taken: **11 encoder items are now P0**, and the P0 encoder bundle alone is **3–4 working
weeks**. Nothing in the encoder arc is optional any more, and nothing is waiting on a decision — only on
Designer Rev 3 for the detail of four items, marked in place.

---

## 2. Ordering constraints — read this before claiming anything

These are not preferences. Each has a mechanism behind it, and at least one has already cost this project
real time.

| # | Constraint | Why, mechanically |
|---|---|---|
| **O1** | **`TEST-1` before anything it verifies.** | The suite produces false CI signals because unit tests can reach an ambient `localhost:5000`. A green run on the self-hosted runner currently means "…and `radio-api` happened not to be running." Every other row in this document is verified by that suite. Shipping fixes against a suite that lies is how you get a second `03a6fea`. |
| **O2** | **`AUD-6` before `AUD-7`.** | `AUD-7` makes startup *act* on the persisted output preference. Acting on an unstable key is strictly worse than ignoring it — shipping `AUD-7` first converts today's **mis-report** into tomorrow's **mis-route**, sending audio to USB because `playback-1` now resolves there. |
| **O3** | **`AUD-2` before `AUD-4`.** | `AUD-2` establishes whether the source-key mismatch (`sdr-radio-<guid>` vs `Radio-<guid>`) is real. `AUD-4` unifies the source-removal layers on the assumption that it is. Unifying layers on an unverified premise is exactly the failure mode `AUD-4` exists to clean up. |
| **O4** | **`LOG-6` before `LOG-10`. Never negotiable.** | `LOG-10` promotes the capture thread to `SCHED_FIFO`-50. `LOG-6` removes logging and a managed lock from that thread's hot path. Bumping a thread that logs and takes a lock to real-time priority converts a latency problem into a **potential priority-inversion hang** — on a box you can only reach by SSH. This is the one constraint here that can brick the appliance if violated. |
| **O5** | **`ENC-1` before every other encoder item.** | The shipped decoder speaks an 8-byte protocol the device does not send. Every UX behaviour in the Designer handoff — HUD, overlays, acceleration, long-press — is built on delta values that are currently garbage. Building UX on a broken parser means debugging the UX for a parser bug. **Rev 2 §12.2 states it as a gate in its own words: the shipped service "has no concept of the device's config or diagnostics reports at all — §7 cannot be built until this lands."** |
| **O10** | **`ENC-1` → `ENC-2` → `ENC-11` → `ENC-8` / `ENC-12` / `ENC-14`.** | ✅ **A firmware defect briefly blocked this on 2026-09-02 and is now FIXED AND FLASHED** (RotaryUsb [#11](https://github.com/mmackelprang/RotaryUsb/pull/11)): the device has an interrupt OUT endpoint, so TinyUSB delivered every host-to-device report with `report_id == 0` and the real ID in `buffer[0]`, while the callback dispatched only on the parameter — config pushes and commands alike were silently dropped, and the host could not tell. Verified after flashing: `0x04` returns a 107-byte Input Report `0x02`. See `design/research/ENC-11-firmware-drops-output-reports.md`. The chain is a chain, not a set: `ENC-2` supplies the report plumbing and the config model, `ENC-11` is the runtime push/verify loop built on it, and the fault surfacing, flash baseline and diagnostics card all read state that only exists once `ENC-11` runs. |
| **O11** | ✅ **DISCHARGED 2026-09-03 by `TTS-9`, not deleted — the constraint is kept as the record of a hazard that existed and how it was retired.** | Both rows it ordered are now closed by the same change: eSpeak was **removed entirely**, so there is no longer a vulnerable path to guard (`SEC-4`) and nothing left to install (`TTS-7`). The ordering mattered right up until the moment neither row had a body. **Do not re-open this as a live constraint, and do not delete it** — a future reader should be able to see that shipping `TTS-7` before `SEC-4` would have armed a remote file-write primitive on the appliance. *The original constraint, preserved verbatim:* **`SEC-4` before `TTS-7`. Not negotiable, and it is the unusual case where completing a queued row is what creates the vulnerability.** `TTSFactory.cs:323` interpolates a caller-supplied voice id into an `espeak-ng` command line reachable **unauthenticated** via `POST /api/sources/events/tts`. There is no shell, so it is argument injection — but `-w <path>` makes it an arbitrary file write as `mmack`, which owns `/opt/radio-console`. **The only thing preventing it today is that `espeak-ng` is not installed, and `TTS-7` exists to install it.** Ship `TTS-7` first and the appliance gains a remote file-write primitive in the same change that fixes a silent-audio bug. |
| **O6** | **`PHN-1` (the ADR-029 seam) before or with `PHN-2` / `PHN-3`.** | ADR-029's whole argument is that voicemail-through-the-engine and speak-a-text are **one mechanism, not two features**. Shipping either first means building the seam twice, and the second build inherits whatever shortcuts the first took. |
| **O7** | **`LOG-1` before `LOG-2`, and `LOG-2` behind `LOG-5`.** | `LOG-2` silences `Radio.Infrastructure.Audio` and `...Platform.Bluetooth` — but `scripts/research/bt_drift_analyze.py` and `bt_stall_detect.py` **parse those exact Information lines**. Gate it behind the runtime level switch so the capability is *toggled*, not deleted. |
| **O8** | **`OPS-1` before the first post-install deploy.** | Today a stale `radio-web` binary passes deploy verification silently. The first time you deploy a fix into the cabinet and it does not take effect, you will debug the fix instead of the deploy. |
| **O9** | **Owner decision D2 (physical layout) before the escutcheon is drilled.** ✅ **SATISFIED — the panel is drawn** (`design/hardware/front-panel-layout_4.svg`, 2026-09-01, owner-confirmed). | Permanent. Nothing else in this document is irreversible; this is. ⚠ **The drawing kept D2's order and superseded D2's dimensions** — one vertical column, uniform 29.63 mm, all knobs 15 mm. **The drawing now outranks every dimension in either handoff** (Designer Rev 4 §9.6); if it is ever recut, that file changes first. |

---

## 3. P0 — Blocks installation

### 3.0 Workstream: Encoders

> **✅ D1 ANSWERED 2026-08-19: THE KNOBS SHIP LIVE AT INSTALL. Everything in §3.0 is unconditionally P0.**
> The earlier hedge — *P0 if live, P1 if the owner accepts four inert knobs* — is gone. **Eleven items,
> ≈3–4 working weeks, none of it optional — and **12 items, not 11**, since Rev 3 added the `ENC-15`
> touch-wake gate.** Nothing in the arc is pending a design decision any more: **Designer Rev 3 closed the
> last two** (`ENC-6`, `ENC-8`).
>
> The encoder service is `Enabled: false` by default today, which is the only reason the headline defect has
> never bitten anyone — and under the new auto-detect direction (`ENC-0`) that flag stops being the thing
> that decides whether the subsystem runs.
>
> **This is the largest untested hardware path in the project.** Structurally everything exists — interface,
> action router, hosted service, DI registration, status endpoint, settings UI at
> `SystemConfigPage.razor:1433-1545`. It is written against a protocol the device does not speak.

---

**`ENC-0` — Auto-detect the encoders and degrade gracefully when they are absent.** ✅ **SHIPPED 2026-09-02 as [#506](https://github.com/mmackelprang/RTest/pull/506), with the udev/permission half as [#507](https://github.com/mmackelprang/RTest/pull/507) (`ENC-0a`).** ⭐ **RESCOPED BY THE
OWNER 2026-08-19.** *Effort was: 1 day.*

> The owner's words: *"I'd like to auto-detect whether the encoders are available and have the system
> respond appropriately."*

**This replaces the row's previous posture entirely.** It used to be "keep `RotaryEncoder:Enabled` false
behind a flag until `ENC-1` ships" — a 15-minute guard. With the knobs shipping live, a config flag the
owner has to remember to flip is the wrong shape: the app should look for the device and behave correctly
whether or not it finds one. Three cases, and all three are real on this hardware:

| Case | Required behaviour |
|---|---|
| **Absent at boot** | Start clean, no error spam, no reconnect thrash. The status card reads not-connected; nothing on Home. |
| **Appears mid-session** (USB plugged in, or the Pico re-enumerates after a re-flash) | Detect, configure via `ENC-11`, and go live without a restart. |
| **Disappears mid-session** | Stop cleanly. ⚠ **Reports are change-only — idle silence is NOT a disconnect**, so detection must key on enumeration, not on quiet. This is the trap `ENC-1` also has to respect. |

- **The flag does not simply vanish.** A manual override still has value for bench work; what changes is
  that *presence*, not configuration, decides whether the subsystem runs.
- ✅ **Rev 3 §7.3 answers "does the touchscreen change when the knobs are absent?" — NO, with exactly one
  behavioural exception.** Every knob function has a touch equivalent by construction (volume slider, source
  strip, preset bank, tuner panel), so **absent knobs cost convenience, not capability**: there is no
  degraded mode to design and no "the knobs are missing" chrome to add. Adding hints like *"or turn the
  SOURCE knob"* would clutter the UI when knobs are present and lie when they are absent.
  **The one exception is not chrome, it is safety: panel blanking requires present knobs** (`ENC-6`,
  `ENC-15`).
- **`RotaryEncoderOptions.Enabled` flips to default `true` and changes meaning** — no longer a gate that
  must be opened, now an **escape hatch** for disabling a misbehaving encoder without crawling behind the
  furniture. When it is `false`, presence detection is **silent about everything**: the owner turned the
  knobs off on purpose and must not be nagged.
- **Notification policy per event, and it is asymmetric on purpose.** Boot-absent: **badge, no toast** — the
  owner is most likely standing at the cabinet having just installed or unplugged something. Appears
  mid-session: a toast **only if absence had been reported** — *announce a recovery only for a fault you
  announced*. Disappears mid-session: **a toast**, because it is genuinely surprising and may land
  mid-interaction — plus tear down in-flight knob state: **dismiss any open overlay without committing**
  (an overlay you can no longer navigate is a trap), cancel any long-press ring, dismiss the HUD.
- **Why the cabinet cares:** the encoder USB dropping is a known event on this box — the reconnect loop
  exists precisely because it happens. Combined with D8's re-enabled screen blanking, a drop that is handled
  badly is now the difference between "the knobs stopped working" and "the screen will not come back on"
  (§10, carried risks).
- **Evidence:** `src/Radio.Infrastructure/Platform/Input/HidRotaryEncoderService.cs:9-13`, `:154`, `:170`,
  `:184-215`. All verified in tree.
- **Depends on:** `ENC-1` for the report handling; the detection half is independent.

---

**`ENC-1` — Rewrite the HID decoder for the protocol the device actually speaks.** ✅ **SHIPPED 2026-09-02 as [#498](https://github.com/mmackelprang/RTest/pull/498).** ⚠ **WAS THE HEADLINE DEFECT.**
*Effort was: 2–3 days.* The 37-byte report, the accumulator semantics and the re-baseline rule are all live; the rest of the arc now rests on a decoder that speaks the device's actual protocol.

`HidRotaryEncoderService` decodes an **8-byte report: bytes 1–4 as `sbyte` deltas, byte 5 as a button
bitmask**. The device (`github.com/mmackelprang/RotaryUsb`, Pi Pico, 4 detented encoders each with a shaft
button, no LEDs or display) sends Input Report `0x01` at **37 bytes**: 4× `int32` absolute positions at
buffer offsets 1–16, the button bitmask at index **17**, and 4× `int32` free-running movement accumulators
at 21–36.

Consequences on first plug-in, in the order they appear:

1. The 8-byte read buffer against a 37-byte report **likely faults or truncates first**, before any parsing
   happens.
2. If it does not, `bytesRead < 6 → continue` **spins silently** — no log, no error, no disconnect signal.
3. If parsing runs, turning **one** knob fires spurious events on **all four**, because bytes 1–4 of a
   37-byte position block are the low bytes of encoder 0's `int32`, not four deltas.
4. Buttons chatter constantly — byte 5 sits inside encoder 1's position, not the bitmask at index 17.

Scope:

- Correct report length, offsets and `int32` decoding; drive events from the **accumulators** (21–36), per
  Designer §5.1 — all four encoders accumulator-driven, host-clamped.
- Handle Report `0x04` (diagnostics, every 100 ms: edge / invalid / detent counts). This is the
  wiring-sanity and steps-per-detent calibration instrument — see `ENC-2`.
- **Reports are change-only. Idle silence is normal and must NOT be treated as a disconnect.** The reconnect
  loop must not be re-tuned to fire on quiet.
- ⭐ **THE RE-BASELINE RULE — new in Rev 3, and it is the highest-weighted test in Designer's §15.** The
  movement accumulator is **free-running: it keeps counting whether or not anything is listening.** If the
  app restarts, or a USB lead is knocked loose inside the cabinet and re-seats, and the host resumes by
  **diffing the new sample against its last remembered value**, then **every detent turned during the
  outage arrives as one delta — on the volume knob.**

  > **On every connect, the first sample from each encoder is a BASELINE, not an input. It is recorded and
  > discarded. No delta is ever computed across a disconnect.**

  ⚠ **Diff-against-last-remembered-value is the obvious way to write this decoder, and it is wrong.** This
  is a decoder/transport requirement, not a UX one — it belongs here and in `ENC-2`, not in any HUD work.
  Designer's acceptance test, verbatim: *"Turn a knob ~50 detents while unplugged, then replug: volume does
  not jump."*
- The udev rule covers `cafe:4005` only. The device has **two firmware identities** — C++ `0xCAFE:0x4005`
  and CircuitPython `0x239A:0x80F4`, usage page `0xFF00`. Cover both, or the knobs are dead after a re-flash.
- Test coverage today is **one smoke test**. Add report-parsing unit tests against captured byte arrays —
  the shape `PipeWireNodeParsingTests.cs` already established for the pw-cli parser.

- **Why the cabinet cares:** four holes are being drilled in restored furniture. If the knobs do not work,
  the most visible part of the machine is broken, and broken in a way a guest reads as "this is a prop."
- **Evidence:** `HidRotaryEncoderService.cs:9-13`, `:154`, `:170`, `:184-215`. Protocol per the RotaryUsb
  repo (public).
- **Depends on:** nothing. **Blocks:** `ENC-2` … `ENC-9`, and the DPMS screen-blanking decision (`ENC-6`).
- **Plan/spec:** none exists. Designer §12.2 lists the host-side work and names this row as the gate on all
  of it — but does **not** specify the parser itself.

> **⚠ CORRECTION TO THE DESIGNER HANDOFF — apply this; do not carry the old reasoning forward.**
> `HANDOFF-rotary-encoder-mapping.md` §2.2 defect 5 and §5.6 argue against the factory acceleration tiers
> partly on the grounds that *"the HID report is `sbyte` per encoder (±127), so a hard spin at ×50 wraps the
> sign."* **That is an artifact of the current broken parser, not a property of the device.** The device
> sends `int32`. There is no sign-wrap ceiling. §5.6 should be struck.
>
> **Designer's other argument against the factory tiers stands on its own and is load-bearing:** with
> `step_size = 2` and T3 at ×50, **one detent takes volume from silence to full** — in a living room, from a
> knob a guest may be touching for the first time. That is why the factory tiers must not be left on the
> volume encoder. The `sbyte` argument was never needed.

---

**`ENC-2` — The per-encoder config model and the `0x02` / `0x03` / `0x04` report plumbing.** ✅ **SHIPPED 2026-09-02 as [#504](https://github.com/mmackelprang/RTest/pull/504).**
*Effort was: 2–3 days.* ⚠ **Rescoped by Designer Rev 2** — this is now the transport and the data
model only. The runtime push/verify loop built on it is `ENC-11`; the calibration flow is `ENC-14`.

**There is no host→device config path at all today.** `RotaryEncoderOptions` has 8 flat fields and cannot
express per-encoder bounds or acceleration tiers.

- Output Report `0x02` is a **106-byte** config: per encoder `min` / `max` / `step_size` / `wrap` /
  `reverse`, plus three acceleration tiers (`threshold_ms`, `multiplier`).
- Output Report `0x03` is commands: save-to-flash, factory reset, **reset positions to min**, read-back,
  zero counters. ⚠ **D4 confirms there is NO set-position command** — `reset positions to min` is the only
  host-side position control that exists, which is why accumulator semantics are **forced by the protocol
  rather than merely preferred**, and why it is also the documented recovery for a knob whose reported
  values look wrong.
- ⭐ **The startup handshake issues `0x03 reset positions` as belt-and-braces**, then baselines the first
  sample per `ENC-1`'s re-baseline rule. Cheap, since positions are unused under accumulator semantics, and
  it means a knob that has drifted for any reason starts from a known state. **Order: `0x03/0x03` reset
  positions → `0x02` config push → `0x03/0x04` read-back verify → baseline the first sample.**
- ⚠ **Bad config is silently rejected. The host MUST read back and verify.** A write that appears to succeed
  and did not is how the volume knob ends up on factory tiers.
- Replace the 8 flat option fields with a per-encoder shape matching Designer §5.2.

**A one-minute calibration check belongs on this row** (the full flow is `ENC-14`). Designer §15's first
test settles the assumption every number in §5 rests on:

> Turn the volume knob **exactly 10 detents** slowly (>200 ms between clicks). Volume must move **exactly 20
> points**. If it moves 80, the detent divisor is wrong and every figure in §5 is off by 4×.

Report `0x04`'s detent counter answers the same question in about a minute without touching audio at all.

**⚠ The Rev 1 fork is CLOSED.** Rev 1 asked whether the host writes the config or the owner flashes it with
RotaryUsb's tooling. **The owner has directed that the app pushes it** — on every detect and every
reconnect, verified by read-back. Designer §7 is new and is a *runtime* requirement, not a table in a
document. ✅ **Rev 3 update: the flash channel holds exactly the OPERATING configuration** — Designer
withdrew its own "safe baseline" idea (§6), and the flash write is now the `Save to device` action inside
`ENC-8`. Owner-initiated only, never automatic. **`ENC-3`'s host clamps stay regardless** — device config lives in flash and can be lost,
factory-reset, or absent on a replacement Pico.

- **Why the cabinet cares:** the difference between "a firm sweep crosses the FM band in ≈2.5 revolutions"
  and "two detents cross the entire FM band" lives entirely in this config. One is a tuning dial; the other
  is a random station generator.
- **Evidence:** RotaryUsb protocol; `RotaryEncoderOptions.cs`; Designer §5.2, §13 Q2, §13 Q3, §15.
- **Depends on:** `ENC-1`. **Blocks:** `ENC-11`, and the "Feel" acceptance criteria in Designer §15.

---

**`ENC-3` — Host-side safety clamps, acceleration policy, and broadcast throttling.** ✅ **SHIPPED 2026-09-02 as [#511](https://github.com/mmackelprang/RTest/pull/511).**
*Effort was: 1–2 days.* ⚠ **Two deviations from this row's text, both deliberate and both recorded in the PR.** (1) **The broadcast throttle was NOT added** — the row's justification is wrong. `VolumeChanged` has exactly one call site, inside a 500 ms change-detecting poller: 2 Hz, trailing-edge, final-value already. A second throttle would have been dead code guarding a path that cannot reach 100 Hz. (2) **The volume ramp was deliberately deferred** — it changes gain application in the audio callback path, where the long-running capture bug and the distortion reports live, and its acceptance criterion is whether it *sounds* right on a fast spin. It wants someone in the room. The clamps themselves — the actual safety content — all shipped.

Four independent guards against the only genuine safety hazard this machine has, plus the throttle that
keeps the knobs from becoming a new distortion source.

- **Per-event host clamp, applied unconditionally regardless of what arrives on the wire:** volume `±6`,
  tone `±4`, source `±1`, tuning `±8` (radio) / `±1` (track). This is what makes a factory-reset or
  replacement Pico *sluggish* rather than *dangerous*.
- **Volume must not wrap.** One detent past zero being full scale, at 2 a.m., pointed at a sofa, has no
  interaction design that makes it acceptable.
- **Acceleration disabled entirely on SOURCE** — one detent is always exactly one entry in a seven-item list.
- **Volume ramp 60–80 ms per applied step**, to avoid zipper noise on a fast spin.
- **Coalesce encoder-driven state broadcasts to ≥50 ms (20 Hz), trailing-edge, always emitting the final
  value.** The audio action itself is *not* throttled — the ear leads, the screen catches up.

> **The throttle is a P0 requirement, not an optimization.** `PollIntervalMs = 10` with no rate limiting
> means a fast spin can drive up to **100 state changes per second**, each fanning out over SignalR to a
> Blazor Server circuit that re-renders a component tree. On an Intel N100 where audio distortion already
> correlates with incidental CPU load, that is a plausible new distortion trigger — **and a miserable one to
> diagnose, because it would only reproduce while someone was touching the radio.**

- **Why the cabinet cares:** criterion (a). **No single detent may move volume by more than 4 points**,
  which the host clamp enforces whatever the device sends; silence to full is 2.0 s of deliberate spinning
  at 80 ms per detent and 1.0 s at 40 ms, the fastest rate the top tier still qualifies for. The rest of the
  row is about not making the distortion problem worse.
  ⚠ **`ENC-20` (2026-09-03) re-derived this criterion, and the original was never satisfied by the shipped
  code.** This bullet used to read *"minimum time from silence to full must be ≥1.33 s"*. That figure came
  from handoff §5.4's table, which computed points per detent as `multiplier × 2` — correct only at
  `step_size = 1`, while the code shipped `step_size = 2`. Every points figure was therefore **half** the
  real value and the true time was 0.67 s, so the criterion as written was failing from the day it was
  written and nothing could have detected that, because the criterion and the defect shared an assumption.
  It is now stated as **points per detent**, the quantity the clamp actually bounds: a tier threshold is a
  *maximum interval*, not the user's spin rate, so a bare figure in seconds was never a floor. The clamp
  values in the bullets above are the row's original text and are superseded — volume is **±4**, not ±6,
  and at `VolumeStepPercent = 1` a unit is a point. Handoff §5.4 and §10.2 carry the full derivation.
- **Evidence:** `RotaryEncoderActionRouter.cs:128-136` (today's clamp is on the *value*, not the delta);
  Designer §5.2 host-clamp row, §5.4, §6.8, §10.2. **Rev 2 §7.3 gives the clamps the concrete justification
  they previously had only in the abstract: between USB detect and a verified config the device runs
  whatever is in its flash — on a fresh or reset Pico, factory defaults, including volume acceleration at
  ×50. That window opens on every boot and every reconnect, and the knobs stay live throughout it.**
- **Depends on:** `ENC-1`. Deliberately independent of `ENC-2` and `ENC-11`.

---

**`ENC-4` — `EncoderHud` + the persistent mute indicator: every knob visible within 100 ms, on every route.** ✅ **SHIPPED 2026-09-02 as [#519](https://github.com/mmackelprang/RTest/pull/519)** — whose review found three HIGH defects that had reached `main` without a PR, including an unguarded timer-thread callback that could kill the web process. ⚠ **Its geometry was then corrected by [#526](https://github.com/mmackelprang/RTest/pull/526)**: the original shipped horizontal quarters (x = 240/720/1200/1680) for knobs that are in fact a **vertical column left of the LCD**, so cards now anchor left and band at y = 90/270/450/630. `ENC-4a` shipped separately as [#493](https://github.com/mmackelprang/RTest/pull/493).
✅ **SHIPPED 2026-09-02** — implementation in `507b0d3`/`eb4005e`/`bd762d1`/`29acc01`, pre-merge review and its
three HIGH fixes in #519. Plan: [`design/plans/ENC-4-encoder-hud.md`](../design/plans/ENC-4-encoder-hud.md), 17 tasks.
Dependencies were `ENC-1` #498 and `ENC-3` #511. **`ENC-4a` shipped separately as #493.**
Verified on the box at 1920×720: 1500 ms dismissal with re-arm, and coalescing measured at 11 broadcasts for 100
publishes over 1.1 s (the §6.8 ≥ 50 ms rule). ~~quarter centres exact at **240 / 720 / 1200 / 1680**~~ — that
verification confirmed the card sat where the *then-current* spec put it, and the spec was wrong about the axis.
✅ **Rotated and re-verified on the box 2026-09-02** — see the geometry bullet below.
⛔ **The router index→handler remap was deliberately NOT done** — it still reads `0=Volume 1=Tuning 2=Source
3=Visualization` against a cabinet engraved VOLUME / SOURCE / PRESETS / TUNING, and belongs to `ENC-5`/`ENC-7`, which
introduce the handlers it would point at. A test pins the current mapping. Cards on knobs 2–4 therefore say the wrong
words today; the card is in the right *place*, which is what this row owns.
⚠ **The implementation reached `main` without a PR and was never reviewed pre-merge**; #519 carries that review.
*Effort: 3–4 days.*

> **⚠ Three findings from planning that this row's text does not anticipate.**
> 1. **The existing `VolumeChanged` broadcast cannot carry the HUD.** Its only call site is a **500 ms**
>    change-detecting poller (`AudioStateUpdateService.CheckVolumeAsync:453-476`) — 2 Hz against a 100 ms
>    requirement — and it reports *what the volume now is*, not *which knob moved*, which is the whole trick.
>    The plan adds a dedicated push channel carrying the encoder index, with its own ≥50 ms coalescer.
>    This does **not** contradict `HANDOFF-NEXT-SESSION.md`'s "do not add a second throttle" — that note is
>    about `VolumeChanged`, which this row leaves alone.
> 2. **The Sleep host is reachable today, but only on the idle path.** `idle-dimmer.js:73-81` navigates to
>    `/sleep` *without* calling `SetSleepAsync(true)`, so `IsSleeping` is false there and a knob turn acts and
>    renders in place. Reached via the **Sleep pill** it is true, the input is consumed by the wake, and the
>    browser leaves the route — so UAT via the pill produces a false failure.
> 3. **Designer §12.2's "reuse `RadioControlPanel.LongPressThresholdMs`" is not literally possible.** It is a
>    `private const` in a Razor component in `Radio.Web`; the synthesis has to run in `Radio.API`. The plan
>    promotes the value into `Radio.Core` and repoints the component at it, so there is one definition.

> **This is the actual defect the Designer handoff exists to fix.** Two of the four knobs currently produce
> **no visible evidence that anything happened**, and one of them changes the machine's entire behaviour
> from an invisible internal counter. A knob that acts silently is worse than a knob that does nothing,
> because **the user's response to silence is to turn it further.**

- One component, two hosts: `EncoderHud.razor` in `MainLayout` for normal routes, again inside `Sleep.razor`
  with `Variant="Sleep"` (the sleep screen is a separate route on `EmptyLayout`, so `MainLayout` is not in
  that tree).
- **Geometry is the whole trick:** ~~the HUD divides 1920 px into quarters — centres at 240 / 720 / 1200 /
  1680 px — and renders the active knob's card in its own quarter. Turn the second knob from the left,
  something lights up above the second knob from the left.~~ Nobody has to be told this.
  ⚠ **WRONG AXIS — corrected in Designer Rev 4 §6.2 on 2026-09-02, after `ENC-4` had already shipped it.**
  The knobs are a **vertical column left of the screen**, not a row beneath it. **The principle is
  untouched and the fix is a 90° rotation, not a redesign:** cards anchor to the **left edge**, stacked
  vertically at **90 / 270 / 450 / 630 px**, beside the knob at the same height — *turn the second knob
  from the top, something lights up beside it at the same height.* Those clean quarters sit within
  **3.05 px (0.5 mm)** of the measured knob positions; Rev 4 §6.2 says to use them rather than the measured
  values **and gives the reason**, so that nobody converts one into the other later. **Rev 4 also flags two
  mechanics the rotation breaks** (no vertical twin for `margin-left: -180px` when card height varies; the
  `snackbarSlideIn` entrance now points the wrong way) **and a full left-edge collision list — the VOLUME
  band lands on the fixed topbar and covers `ENC-4a`'s own `MUTED` chip.** *(The correction was a separate
  reviewed PR. Rev 4 specified it; it did not build it.)*
  ✅ **Rev 5 closed every open question the rotation had, so it is implementable as specced.**
  **Decision: accept transient occlusion on all bands** — inset, a narrower card, per-route
  suppression, z-order yielding and a permanent left gutter were each considered and rejected with
  reasons (§6.2). The VOLUME/topbar overlap is safe because **every VOLUME card carries and displays
  the console's mute state**, verified in the router and the gesture's documented
  `ShortPress`-before-`HoldCancelled` ordering, and now an invariant in §6.7. Rev 5 also **corrects
  Rev 4's own over-report**: only **two** bands carry left-edge cards in the target mapping — SOURCE
  and PRESETS are selector knobs whose feedback centres (§6.6), so bands 270 and 450 are
  **transitional**, present only while the router runs the pre-`ENC-5` index table. Vertical centring
  must be **clamped to the viewport** (a ~173 px volume card on band 90 sits 3.5 px from the top),
  and §6.10 adds the phase contract below.
  ✅ **SHIPPED 2026-09-02 as `ENC-4c`, PR [#526](https://github.com/mmackelprang/RTest/pull/526).** The
  rotation is done, reviewed and on the box. Cards anchor at `left: 24px` on bands **90 / 270 / 450 / 630**,
  and the four bands, the engraved names and the index→knob mapping now have **one definition** —
  `Radio.Core.Configuration.FrontPanelGeometry`, citing the drawing — because §6.2 names four surfaces that
  need them and this repo has already paid once for a value defined in three places. Vertical centring is
  clamped to ≥ 8 px inside the viewport and expressed on the independent `translate` property, so the
  entrance animation cannot drop it and no wrapper element was needed. ⚠ **The clamp matters more than the
  spec expected:** the volume card measures **178.5 px** on the box, not the ~173 px Rev 5 estimated, so
  centred on band 90 it would sit *off the top of the viewport* rather than 3.5 px inside it. The mirrored
  keyframe pair is §6.1's **declared** exception, scoped to the Normal variant — **do not "correct"
  `.encoder-hud-enter` back to `.snackbar-enter`**; the Sleep variant, placed by the drift wrapper rather
  than by an edge, still uses the original. No new tokens; §6.9 stands. Occlusion accepted on every band per
  §6.2, with §6.7's mute invariant as the reason band 90 is safe.
  **UAT was measured, not eyeballed** — driven through the kiosk's own CDP at 1920×720, three card variants
  × four bands, 12/12: left anchor exact at 24.00 px, the clamp engaging on exactly bands 90 and 630 and
  nowhere else, and the mid-animation rect identical to the resting one. ⚠ **One operator finding came out
  of it and is NOT fixed here** — the kiosk was serving a **stale cached `design-system.css`**, because
  `radio-web` sends it with no `Cache-Control` header and the kiosk profile's HTTP cache survives the
  deploy's relaunch. **A CSS-only change can land, verify by SHA, and still not be on the panel.** Candidate
  row for Planner.
- ✅ **Phase contract — a spec change, not a tidy-up (Designer Rev 5 §6.10). SHIPPED in `ENC-4c`,
  PR [#526](https://github.com/mmackelprang/RTest/pull/526), with all four arms explicit and the test
  updated rather than deleted.** *The diagnosis is kept below as the record of what was wrong.* An
  unrecognised HUD phase **preserved `IsHolding`**, and a true `IsHolding` cancels the
  1500 ms dismissal timer — so `HoldStart` → *unknown* → `Value` strands a card on screen with nothing to remove it.
  Unreachable today (both builds know the same four names), but **`ENC-5` and `ENC-7` add phases** and
  an API-ahead-of-Web deploy is ordinary. **Decision: an unrecognised phase is *not holding*** — it
  renders nothing, so it can never draw a ring, and suppressing the timer is its only reachable
  effect. ⚠ **The obvious edit is wrong:** `"Value"` shares the same default arm and **must keep
  preserving `IsHolding`**, or turning the knob mid-hold collapses the ring. Four explicit arms, not
  three. This supersedes the plan's §2.5 contract; **update
  `EncoderHudServiceTests.UnknownPhase_LeavesIsHoldingAlone` to the new contract rather than deleting
  it.**
- Built entirely from three existing pieces: the **unused** `.snackbar-enter` / `.snackbar-exit` primitives
  (`design-system.css:1218-1219`), the `GainPopoverService` overlay-hosting pattern, and `SourceBubble`.
  **No new design tokens** (Designer §6.9 — Builder must not add `--hud-*` anything).
- **Long-press synthesis** (600 ms, reusing `RadioControlPanel.LongPressThresholdMs`) plus the progress ring
  that starts at 300 ms and completes at 600 ms. **Two consumers, and Rev 2 moved one of them:
  volume→standby and PRESETS→save.** Save-station sat on the TUNING long-press in Rev 1; it now lives on
  PRESETS, where recall and store belong together, and **TUNING has no long-press at all**. There is no third
  long-press anywhere in the spec, deliberately. The protocol has no long-press gesture; this is host-side
  synthesis.
- **`ENC-4a` — the persistent topbar `MUTED` chip.** Mute currently shows as a single icon glyph inside
  `NowPlayingPanel`, which exists **only in Home's left rail**. On `/queue`, `/metrics`, `/devices`,
  `/history` and `/phone` there is **no mute indication at all**. *A muted console with no visible reason is
  indistinguishable from a broken one.* Independently shippable — see §8.
- **`ENC-4b` — turning the volume knob while muted unmutes.** Designer calls this "the most important small
  rule in this document" and is right. Today it moves a number nobody can hear; every car radio built in the
  last thirty years unmutes on a volume turn.

- **Why the cabinet cares:** criterion (b), squarely. Three separate "is it broken?" silences disappear.
- **Evidence:** Designer §6 in full; `RotaryEncoderActionRouter.cs:200-207` (the source knob logs at Debug
  and does nothing else), `:231-234`; `AudioStateUpdateService.cs:463-473`, `:969-978`.
- **Depends on:** `ENC-1`, `ENC-3`.

---

**`ENC-5` — The SOURCE overlay, with the radio bands folded in.** ✅ **SHIPPED 2026-09-02 as [#536](https://github.com/mmackelprang/RTest/pull/536).** Carries the first half of the router remap — index 1 becomes SOURCE, with Visualization a deliberate seat-warmer on index 2 until `ENC-7`. ⚠ **Its plan omitted `EncoderHudService.IsKnownPhase`, which gates all rendering** — without extending it every selector payload would have rendered **nothing, with a fully green test suite**. Also measured on the box: one `StepFrequencyUpAsync` costs ~52 ms and the tuner serializes, so an 8-step detent is ~416 ms and **a hard flick keeps tuning for ~6 s after the hand stops** — designed acceleration meeting a slow tuner, logged to `FUTURE-WORK.md` with three ranked options.
✅ **SHIPPED 2026-09-03 — [#536](https://github.com/mmackelprang/RTest/pull/536).**
*Effort: **5–6 days** (2–3 originally; D7 added the bands, and Rev 3 specified what that actually costs).*

> **End state of the remap: `0 = Volume · 1 = SOURCE · 2 = Visualization · 3 = Tuning`.** Index 2 holds the
> visualiser as a deliberate seat-warmer until `ENC-7` puts PRESETS there; leaving the *old source cycler*
> there was rejected, because index 1 now opens the overlay and a cycler beside it would have given two
> adjacent knobs two divergent copies of the source selection — the defect §4.4 forbids. The Settings page
> names the one remaining mismatch by knob and empties itself when `ENC-7` lands.
>
> **Five plan deviations, all forced by the tree rather than chosen** (plan §0.4), plus three the build found:
> `IConfigurationStore` has no section API, so `RadioBandMemoryService` uses `IConfigurationManager` and one
> entry per band, the mechanism `PreferencesPersistenceService` already uses; the plan's Task 11 never
> mentions **`EncoderHudService.IsKnownPhase`**, which gates *all* rendering — without extending it every
> selector payload would have rendered nothing **with a fully green suite**; and the plan's Task 14 CSS
> snippet contradicted the shipped `ENC-4` rule beside it three times (surface `color-mix`,
> `-webkit-backdrop-filter`, and centring on `translate` rather than `transform`, which `.snackbar-enter`
> animates and would otherwise drop for the whole 200 ms entrance).
>
> ⚠ **`ENC-9a` did not remove `VisualizerPanel`'s local `_currentMode`**, contrary to this row's own text
> above. `VisualizerPanel.razor:155` still declares it; #491 added the missing *subscription* plus a typed
> DTO. The real template is "one authoritative owner, a typed broadcast, and a component that re-syncs a
> derived copy without echoing" — a weaker claim than "no component may hold a copy", and worth knowing
> before anyone tries to enforce the stronger one.
>
> ⚠ **`RadioControlPanel` already satisfied the one-state rule**, so that task became a regression guard
> plus a one-line rollback rather than a rebuild.
>
> ⚠ **Pre-merge review caught two HIGH defects, both fixed with regression tests.** The State D spinner
> dismissed itself at 1500 ms mid-switch (it sends no duration *on purpose*, and that null was read as
> "use the default"); and the no-tuner band fallback was cached for the life of the process, so a knob
> turned during boot froze FM+AM permanently and an SDR's SW/WB rows could never appear.
>
> ⚠ **UAT H2 passed but measured something the plan did not predict.** No distortion — 1,664 tuner calls
> over ~94 s produced **zero** new PipeWire xruns. But one tuner step costs **~52 ms**, so a full 8-step
> detent is ~416 ms and a hard flick keeps tuning for ~6 s after the hand stops. Logged in
> `design/FUTURE-WORK.md`; not changed here, because every fix alters how tuning *feels*. ⚠ **Rev 2 note: SOURCE is now encoder 1, not encoder 2** — and this overlay
and `ENC-7`'s PRESETS overlay are **one component with two lists** (Designer §6.6, "the two selector
overlays"). Same interaction grammar on purpose: *learn one, you have learned both.* Build them together or
back to back; building them apart is how they drift.

Keep the preview-then-commit mechanism — **it is correct and only ever lacked a screen.** Do not "simplify"
it to live-commit-per-detent: spinning through five sources would tear down and stand up an audio source at
every detent, straight into the long-running capture-lifecycle bug and the `autoSwitchOnConnect` bug
(`AUD-8` / `AUD-9`). Cheap-looking source cycling is not cheap on this box.

Five states, and **D and E are not optional polish** — a Bluetooth switch here can take seconds or fail
outright:

| State | Behaviour |
|---|---|
| A | Open, previewing. Highlight moves one entry per detent. **Nothing switches.** |
| B | Unavailable entry renders dimmed **with a reason**, reusing `SourceBubble`'s `" · offline"` idiom |
| C | Pressing a dimmed entry flashes it amber for 1.5 s and **leaves the picker open** — never a silent no-op |
| D | Committed, switch in flight — spinner, card stays up |
| E | Switch failed — `"Bluetooth unavailable / Staying on FM 98.5"`, 4 s, then dismiss |

A picker that dismisses on press and leaves the user in silence with the old source still playing is how a
person concludes the knob is broken and starts pressing it repeatedly — **which is precisely the input
pattern that provokes the capture-lifecycle bug.**

**Auto-commit on dwell was considered and rejected**: it converts every accidental brush of the knob into a
real source change 1.2 s later, from across the room, with nobody touching anything.

**Press is one rule, not two** (Rev 2 §4.4): *press commits the highlight.* With the overlay closed the
highlight is the current source, so a press commits what is already playing — which changes nothing and
opens the overlay showing you where you are. The "open" behaviour falls out of the rule rather than being a
second meaning for the button. **This is what makes a mis-grab in the middle of the panel free**, and it is
the interaction design paying for the new physical layout (D2).

> **✅ D7 ANSWERED YES — the bands are in, and this row absorbs the scope increase.** `FM` / `AM` / (`SW` /
> `WB` where the tuner reports them) / `BLUETOOTH` / `PHONO` / `USB` / `FILES`, at **fixed positions that
> never move** — no recency ordering, no hiding unavailable entries, because *a physical selector whose
> detent 3 is Bluetooth on Tuesday and Phono on Wednesday is not a physical selector*. Unavailable entries
> render dimmed **with a reason**.
>
> **Rev 3 specified what D7 actually costs, and it is more than adding rows to a list. Estimate moves again:
> 4–5 days → 5–6 days.** Four requirements do the damage:
>
> 1. **Committing a band while the radio source is already active is a BAND CHANGE, not a source switch.**
>    `SetBandAsync(band)` — no engine teardown, no spinner, no 150 ms fade. *It should feel instant, because
>    it is.* Restore that band's **last-tuned frequency**, falling back to the band default.
> 2. **Committing a band from another source does both** — activate radio, set band, restore frequency.
>    That one *is* a real source switch: fade, State D spinner, State E on failure.
> 3. **The current-marker tracks the active *band*, not "Radio".** On AM, row 2 is marked — not row 1.
>    *"Getting this wrong makes the knob feel like it lost its place."*
> 4. ⚠ **The knob and the on-screen band pills must be ONE state, not two copies.** `RadioControlPanel`'s
>    band pills and this overlay both read and write the active band; **neither may hold its own copy.**
>    This is what pushes the row outside the overlay's own files — and it is the same defect class as
>    `VisualizerPanel` holding a local `_currentMode` (`ENC-9`). ⚠ **Do not read this as precedent any more:** `ENC-9` shipped 2026-09-03 by *accepting* the local `_currentMode` as the single source of truth and deleting the sync layer around it, because that layer had no writer. The band-pill case is different — it has two live writers, which is what makes a shared owner necessary there.
>
> Plus: **list composition resolves once per tuner at startup**, so a set that never reports SW does not
> render a permanently dead row — and a set that does gets it at position 3, always. Composition does not
> change during a session, which is the only sense in which "positions never move" is achievable.

- **Why the cabinet cares:** the source knob is the one control that changes what the machine is *doing*,
  and today it does so from an invisible counter. Criterion (b), arguably (a).
- **Evidence:** Designer §4.4, §6.6; `RotaryEncoderActionRouter.cs:200-227`.
- **Depends on:** `ENC-1`, `ENC-4` (shares the HUD host). **Pair with:** `ENC-7`.

---

**`ENC-7` — The PRESETS knob: recall and save on the existing preset bank.** ⭐ **NEW SHAPE IN REV 2.** ✅ **SHIPPED 2026-09-03 as [#541](https://github.com/mmackelprang/RTest/pull/541), completing the router remap — the four knobs now do what the escutcheon says: `0 = Volume · 1 = SOURCE · 2 = PRESETS · 3 = Tuning`, verified live via `/api/integrations/encoder/mapping`.** ⭐ **`ENC-5`'s five shared artifacts came through untouched**, and the seven-row window and `SelectorNotice` phase `ENC-5` built *speculatively for this row* both worked as delivered — the "one component, two lists" bet paid off exactly as argued. ⚠ **Two traps its plan had wrong, both invisible to CI:** `.encoder-hud-ring` lives inside `EncoderHud.razor`'s **volume** branch, so a PRESETS hold card renders **no ring with a green suite**; and `HoldStarted` fires on the **press-down** edge, so an early fix wiped the overlay on *every* press. It also removed `VisualizationModeService`'s last writer — see `ENC-9`.
✅ **SHIPPED 2026-09-02 — [#541](https://github.com/mmackelprang/RTest/pull/541).** Plan at [`design/plans/ENC-7-presets-knob.md`](../design/plans/ENC-7-presets-knob.md).

> ⚠ **Five corrections this row makes to the text below — read these before trusting it.**
> **(1) There are no 7 slots.** `RadioPresetService.MaxPresets` is **50 globally**; `RadioPreset` has no `SlotNumber` and neither does the SQLite table; the ordinal is re-derived per request. So *“next free slot”* searches nothing and *“never overwrites”* is free by construction — there is no overwrite path to guard — and `PRESETS FULL` is kept verbatim while being honestly a message that will essentially never fire.
> **(2) Recall could not use `POST /api/radio/presets/{id}/load`** — it returns 400 *“Radio is not the active source”*, which is exactly the case the knob exists to serve. Recall is implemented server-side instead.
> **(3) `ALREADY SAVED · slot NN` is an ADDITION to the spec**, not an interpretation. Holding on a station already saved throws a duplicate error this handoff never covers, and silence there is the defect the arc exists to fix.
> **(4) The `MEMORY → PRESETS` rename is now recorded in a FOURTH place** — a note at the head of [`HANDOFF-saved-station-display.md`](design-handoffs/HANDOFF-saved-station-display.md) itself, which is the only place a consistency pass will actually look. ⛔ **Do not revert it.**
> **(5) `ENC-9` is now more load-bearing than “removing a knob must not remove a capability” implies.** Taking the visualiser off index 2 removed the **last writer** of `VisualizationModeService`, so `ModeChanged` cannot fire and the `VisualizationModeChanged` broadcast plus `VisualizerPanel`'s subscription are unreachable. The capability survives on Home's six-segment picker — which never went through that service — but the cross-surface sync does not. ✅ **RESOLVED 2026-09-03: `ENC-9` shipped and the owner chose to DELETE the chain** — service, broadcast, client-side listener and subscription all removed; the picker still works, verified live. Single-surface, local-only mode is the accepted design. See `design/FUTURE-WORK.md` §17 for what a second surface would have to rebuild.

> ⚠ **UAT gap, declared:** Test Plan sections A–H need a hand on the physical knobs and no agent can inject a HID report. **C4 — recall from Bluetooth, the plan's own highest-weighted check — is unverified on hardware.**

⚠ **This item replaces Rev 1's "Tone DSP or Browse fallback" entirely, and it changes tier: P1 → P0.** Rev 1's
knob 2 was P1 because Tone was blocked on an Architect ADR that might never land and had a weak fallback.
PRESETS is fully specified, needs **no new DSP and no new data model in v1**, and is one of the four knobs —
so under D1 it is P0 exactly as SOURCE is.

- **Turn** moves a highlight through the saved-station bank. **Nothing plays.** Acceleration disabled, same
  as SOURCE.
- **Press — Recall.** Commit the highlight: **switch source if needed**, tune, and play. Recall is *not*
  scoped to the active source, which is what makes the knob alive from Bluetooth or Phono — turn it from
  anywhere and your stations are there.
- **Long-press 600 ms — Save** what is playing to the **next free slot**. **Never overwrites**: if every slot
  is full the HUD says `PRESETS FULL — replace a slot on screen` for 2 s and writes nothing. Replacement
  stays on the touchscreen, behind the existing kebab, where it has a confirmation and an undo.
- **v1 saves radio stations only.** On a non-radio source the hold reports `Only radio stations can be
  saved` for 1.5 s — a clearly-messaged v1 boundary, not a silent failure. Cross-source favourites are v2
  and need a data model that does not exist (Architect, Designer §12.1 item 2).
- **The empty state is instructional, not empty:** `NO STATIONS SAVED` / `hold this knob to save what's
  playing`. The knob teaches its own use, which matters more here than anywhere else on the panel.
- ⭐ **D10 KNOCK-ON — the on-screen bank is renamed too, and this is a DELIBERATE, DECLARED deviation.**
  `RadioControlPanel`'s bank is titled `MEMORY · n saved` today. The cabinet is engraved **PRESETS**, so
  the bank becomes **`PRESETS · n saved`** — a one-word deviation from `HANDOFF-saved-station-display.md`,
  **declared in Rev 3's own header so Polisher does not flag it as drift.** Designer's reasoning:
  *"A panel that says PRESETS over a screen that says MEMORY is the same mismatch class I flagged in the
  settings table; fixing one and not the other would have been worse than either."* ⚠ **Do not "fix" this
  back to `MEMORY` on a later consistency pass** — it is recorded in §6 for exactly that reason.

- **Why the cabinet cares:** *"put on my station"* is the single most common thing anyone does to a radio,
  and on this console it currently costs a source switch plus twenty-five detents of tuning. One turn and
  one press replaces that. Designer's judgement, and it is right: **if a guest touches exactly one knob
  after volume, this is the one that should reward them.**
- **Evidence:** Designer §4.3 (and its three recorded rejections — Browse, Balance, Output/Cast), §4.4,
  §6.6. Drives the shipped bank: `HANDOFF-saved-station-display.md`, `PresetCard.razor`, 7 slots,
  save/rename/delete already shipped.
- **Depends on:** `ENC-1`, `ENC-4`. **Pair with:** `ENC-5` — one overlay component, two lists.

---

**`ENC-11` — Startup config push + read-back verification.** ✅ **SHIPPED 2026-09-02 as [#509](https://github.com/mmackelprang/RTest/pull/509), preceded by the firmware fix in [#508](https://github.com/mmackelprang/RTest/pull/508) and corrected by [#510](https://github.com/mmackelprang/RTest/pull/510) (`ENC-11a`).** ⭐ **NEW IN REV 2 §7, owner-directed.**
*Effort was: 2–3 days.* ✅ **The tiered fault model is live and verified on hardware, and as of `ENC-12` [#535](https://github.com/mmackelprang/RTest/pull/535) it can finally tell the owner about it** — a Degraded or Hard-fault outcome used to reach only the log and the API. `ENC-4` hosts the badge; `ENC-12` shipped the badge and the notification. The safety response is no longer silent.

> **The governing rule, and it is the whole item:** *the app owns the configuration; the device holds a
> cache of it, and a cache is never trusted.* **No write counts as applied until read-back matches it field
> for field.** The device silently rejects values it does not like — a write without a verified read-back is
> not a write, it is a hope.

- **Push on:** first detect, every USB reconnect, and any change to the app's own config. Never assume a
  device configured five minutes ago is still configured — it may have been replaced by an identical one.
- **Verify by read-back, field for field.** Retry ladder **250 ms / 1 s / 3 s**, silent for attempts 1–3 (a
  USB peripheral missing a report on the first try is ordinary).
- **The safety-tier response lives here, not in the UI item:** a mismatch on a *safety* field — `wrap` on
  VOLUME, or `reverse` on any knob — **drops the host clamp to ±2 per event on volume and holds it there
  until a verified push succeeds.** A mismatch on a *feel* field (any acceleration tier, `step_size`) leaves
  the knobs live on host clamps and treats acceleration as **absent** rather than assumed.
- **Normal boot is completely silent** — no toast, no splash, no banner. Designer §7.2 follows the
  convention `HANDOFF-kiosk-desktop-launcher.md` §5.1 already set: the repair path speaks only when
  something needed repairing. The happy path is **one row on the existing status card**
  (`SystemConfigPage.razor:1440-1478`): `Configuration: [ Configured ]  verified 2026-08-19 07:14:02`.
- **Target: verified within 2 s of detect.** Past that the status card reads `Configuring…` — and still
  nothing appears on Home.
- **Never silently retry forever, and never silently accept.** Designer names the worst outcome precisely:
  *a knob configured with stale bounds that reports itself fine* — the same failure class as `TuningStepKHz`,
  a thing that claims to be set and is not.

- **Why the cabinet cares:** criterion (a). Without this the device runs whatever survived in its flash, and
  on a fresh, reset or replacement Pico that means **volume acceleration at ×50 — one detent from silence to
  full**. `ENC-3`'s clamps make that window survivable; `ENC-11` is what closes it.
- **Evidence:** Designer §7.1–§7.4; RotaryUsb Output Report `0x02` (106-byte config), `0x03` (commands),
  `0x04` (read-back / diagnostics).
- **Depends on:** `ENC-1`, `ENC-2` (O10). **Blocks:** `ENC-8`, `ENC-12`, `ENC-14`.
- **Architect note:** where this lives — inside `IRotaryEncoderService` or a separate provisioning service —
  and how its health reaches the API is an architecture question (Designer §12.1 item 3). **The UX contract
  is already fixed and is not Architect's to revisit:** silent when healthy, tiered when not, never trusted
  without read-back.

---

### 3.1 Workstream: Audio Reliability

**`AUD-6` — Output device identity is an enumeration ordinal.** ✅ **SHIPPED 2026-09-02 as [#499](https://github.com/mmackelprang/RTest/pull/499) and deployed.** Keyed on the raw platform name (owner chose option B). ⚠ **Option A — the device's own `DeviceInfo.Id` — was ruled out by measurement, not opinion: it is an `nint` heap pointer that changes every process** (`design/research/AUD-6-stable-device-key-options.md`). **Correction to that document's own migration note: legacy values do NOT generally cost a re-selection.** Verified live — the box held `playback-0`, the fallback-to-default path re-persisted it as `out:Built-in Audio Analog Stereo`, and the store self-healed on first boot with no user action. A re-selection is only needed when the stored preference was *not* the system default. ⚠ **Claim BEFORE `AUD-7` (O2).**
*Already queued 📋. Effort: 1–2 days including a store migration.*

`SoundFlowDeviceManager.cs:517` mints `Id = $"playback-{i}"` straight off the enumeration index, and that
string is what reaches storage. Resolution back to hardware is **pure string parsing, not a lookup** —
`SoundFlowAudioEngine.GetDeviceIndexById:1030-1043` is an `int.TryParse` and nothing else. Measured across
one deploy restart: on Aug 10 `playback-1` meant the soundbar; on Aug 11 it meant USB Audio Out. **Nothing
in the store changed — the meaning did.**

- **Why the cabinet cares:** criteria (a) and (c). "No sound from the speakers" after a routine restart, in
  a sealed cabinet, is a laptop-and-SSH recovery for a problem that presents as dead hardware.
- **Fix shape:** a stable key **plus a migration** — every existing store holds ordinals.
- **Depends on:** `TEST-1`. **Blocks:** `AUD-7`, hard.

---

**`AUD-7` — The reported active output diverges from where audio physically goes.** ✅ **SHIPPED 2026-09-02 as [#500](https://github.com/mmackelprang/RTest/pull/500) and deployed.** Startup now resolves the preference and performs the native switch; `GetDeviceIndexById` and `SwitchPlaybackDevice` moved onto `IAudioEngine` so it could, and so a test could assert it did. Verified on the box: *"Native playback device now matches the preference: Soundbar (index 0)"*.
*Already queued 📋. Effort: ~1 day.*

The native device switch is reachable **only from the interactive HTTP path**.
`SoundFlowAudioEngine.SwitchPlaybackDevice:838` — the only code anywhere that stops, disposes and
re-initializes `_playbackDevice` — has exactly one caller, `DevicesController.SetOutputDevice:250-254`. The
startup path never calls it, and `SoundFlowDeviceManager.SetOutputDeviceAsync:213-244`, which the gate's own
comment nominates as the thing that performs the swap, **does not touch the native device at all** — it
validates an id against a cache, assigns a string field, and persists.

**This is deterministic, not intermittent.** It fires on **every** restart where the persisted preference is
not the system default, with or without any enumeration reordering.

- **Why the cabinet cares:** the output selector cannot be trusted. In furniture with a Cast device and a
  soundbar in play, "the UI says one thing and the audio comes out somewhere else" is the most confusing
  failure mode available.
- **Evidence:** `SoundFlowAudioEngine.cs:838`, `:222-224` (a comment assigning a responsibility the method
  does not discharge — the fourth instance of the pattern `CLAUDE.md` § Pre-Merge Review warns about);
  `DevicesController.cs:250-254`; `SoundFlowDeviceManager.cs:213-244`.
- **Depends on:** `AUD-6` (hard), `TEST-1`.
- **⚠ Evidence-handling note carried from the queue:** a later truthful `/api/devices/output` is **not**
  evidence this is fixed. It only fires after a restart.

---

**`AUD-8` — BT capture watchdog for long-uptime quiescence.** ✅ **ALREADY SHIPPED — CLOSED 2026-09-02 WITHOUT CODE.** `BluetoothCaptureWatchdog.cs` exists in tree, shipped as [#390](https://github.com/mmackelprang/RTest/pull/390) / `f82660a` (2026-05-22). ⚠ **The underlying capture-quiescence bug is NOT thereby proven fixed** — what is proven is that the watchdog this row asked for was built and merged. Whether it actually recovers the long-uptime case wants an observation over weeks of real uptime, which is the only way that bug presents. Re-file as a *confirm-in-service* row if it recurs.

After days of uptime plus source switches, SoundFlow capture stops delivering audio: the generator is in the
mixer, output is 0. Affects **all** capture sources. Only a restart fixes it.

- **Why the cabinet cares:** this is *the* exposure case. An appliance in furniture runs for weeks.
  Everything else in this document is a bug you hit while using the machine; this is a bug you hit by **not**
  using it, and its only current remedy is SSH into a sealed cabinet. Criterion (c).
- **Evidence:** `docs/plans/` (⚠ **not** `design/plans/` — this row had the path wrong) — `2026-05-22-bt-capture-watchdog`; project memory
  `project_long_running_capture_bug.md`, marked HIGH PRIORITY for production stability.
- **Plan:** exists, unqueued. Needs a staleness pass against current `main` before a queue row.
- **Depends on:** `TEST-1`. **Recommended:** land with or after `AUD-9`.

---

**`AUD-9` — `autoSwitchOnConnect` gate.** ✅ **ALREADY SHIPPED — CLOSED 2026-09-02 WITHOUT CODE.**
*This row was never open.* It shipped **the same day the plan was written**, as [#391](https://github.com/mmackelprang/RTest/pull/391) / `85b14b1` *"feat(bt): gate autoSwitchOnConnect on PW capture-node availability"* (2026-05-22), verified as an ancestor of `main`. `BluetoothAutoSwitchService` probes for a PipeWire capture node, switches only on success, defers to a `CaptureNodeAvailable` subscription when the probe window expires, and **abandons the switch** on timeout rather than switching to a source with no audio behind it — the exact shape this row asked for, with 10 unit tests. The probe reuses `ParsePwCliOutputForBtNode`, the same static parser the acquisition path uses.

The app switches to the BT source even when **no PipeWire capture node exists**, producing hours of failed
retries — and project memory records that this **triggers capture-lifecycle degradation**, i.e. it feeds
`AUD-8`.

- **Why the cabinet cares:** it is a self-inflicted load generator on a resource-constrained box where
  incidental load correlates with audio distortion, and it degrades the very subsystem `AUD-8` is trying to
  keep alive. Fixing `AUD-8` without this treats the symptom.
- **Evidence:** `docs/plans/2026-05-22-bt-autoswitch-gate.md` (⚠ **not** `design/plans/` — this row had the path wrong). ⚠ **Project memory `project_autoswitch_bt_bug.md` asserts a defect that does not exist:** it describes "hours of failed retries" from an unbounded loop. Both retry loops are bounded and were **before** the gate landed — 12 attempts × 10 s outer (`BluetoothAudioSource.cs:631`), 20 × 1 s inner (`LinuxBluetoothService.cs:1289`), ~140 s worst case. The 13-hour log spam came from repeated re-entry, which the gate removes. That memory should be retired rather than cited.
- **Depends on:** `TEST-1`.

---

### 3.2 Workstream: Logging & Distortion

> **The framing that reorders this whole workstream:** on this box, logging is not an observability concern,
> it is an **audio** concern. `CLAUDE.md` records that heavy journald reads correlate with audio distortion;
> `scripts/research/heavy_load_harness.sh` uses log and DB churn as a deliberate load source **to reproduce
> the distortion**. Two of the P0 items below are logging items for that reason alone.

**`LOG-1` — `radio-web` logs at Debug in production and its logging config is dead.** ✅ **SHIPPED 2026-09-01 as [#488](https://github.com/mmackelprang/RTest/pull/488).** Both file sinks also gained size caps. Verified by falsification rather than by reading the config back.
*Effort was: 30 min. **Largest single volume reduction available anywhere in the project.***

`src/Radio.Web/Program.cs:14` hardcodes `.MinimumLevel.Debug()` and **never calls `ReadFrom.Configuration`**,
so the `Logging:LogLevel` block in its appsettings is read by nothing (verified in tree). All **106 Debug +
56 Information** sites are live. The file sink has **no retention limit and no size cap**. Measured: **65 MB
for one day** on a dev machine. `radio-web.service` also lacks `SyslogLevelPrefix=true`, so everything lands
in journald at info priority and cannot be filtered by level.

- **Why the cabinet cares:** criterion (c). An uncapped log sink on an appliance that runs for weeks fills a
  disk, and the disk is inside the cabinet. The journald half is a continuous, self-inflicted contribution
  to the distortion problem.
- **Fix:** honour configuration; Information in production; `rollingInterval` + `retainedFileCountLimit` +
  `fileSizeLimitBytes` on both services' file sinks; `SyslogLevelPrefix=true` on the unit.
- **Depends on:** nothing. **Blocks:** `LOG-2` (O7).

---

**`LOG-3` — The log-read allocation bomb.** ⚠ **RE-TIERED P0 → P1 by D12, and re-scoped. It does not
disappear with the page.** *Not queued. Effort: 1–2 h (down from 2–3).*

*(This block stays printed under §3 because its evidence is prose rather than a table row. The index in §9
is authoritative: it is **P1**.)*

The shape: `sr.ReadToEnd().Split(...)` on a file permitted to be **50 MB** — roughly **100 MB of LOH string
+ array** — inside a process running under `MemoryHigh=350M` and `DOTNET_GCHeapHardLimit=0x30000000`
(768 MiB, both verified at `deploy/common/radio-api.service:73,:78`), triggering a Gen2/LOH collection **on
cores 2/3, underneath the audio callbacks** (see `LOG-2m`).

**There are two call sites and D12 only removes one of them.**

| Call site | Fate under D12 |
|---|---|
| `SystemController.cs:226-231` — the viewer endpoint | Dies with the Logs tab (`UI-3`). **Delete the endpoint too**, or it is an orphan with a live allocation bomb in it. |
| `SystemLogsController.cs:98-127` — the zip download | **Survives, and is still reachable from `DevTray.razor:253`.** Same read-everything-into-memory shape, same cores, same GC. |

- **Why it drops to P1:** the P0 argument was that *the only in-app error surface was itself unsafe to
  open* — the recovery tool damaging the thing you are recovering. D12 removes that surface, so that
  argument goes with it. What remains is a real allocation bomb on a less-travelled path.
- **⚠ Why two other things get MORE important, not less.** With no in-app log reader: **(1) `LOG-1`'s
  retention and size caps are now the only thing bounding that file** — nothing in the UI will ever show
  you it has grown, and an unbounded log inside a sealed cabinet is a disk-full waiting to happen.
  **(2) The download path's correctness is now load-bearing**, because it is the only non-SSH way to get
  logs off the box at all. Fixing it is no longer optional polish.
- **Fix:** streaming read with a bounded tail buffer on the zip path; delete the orphaned viewer endpoint.

---

> **`LOG-2m` — THE MECHANISM. Not a work item; read it before touching anything in this workstream.**
>
> **`CPUAffinity=2 3` in `deploy/common/radio-api.service:42` applies to the whole process** (verified in
> tree). Both `Serilog.Sinks.Async` worker threads — the ones doing the journald socket write and the file
> write — are pinned to **the same two cores as the PipeWire `OnProcess` callback and the miniaudio render
> callback.**
>
> Plan D isolated `radio-api` from journald *the daemon*. **It never isolated the audio threads from
> `radio-api`'s own log-emission threads.** `docs/plans/2026-05-22-audio-thread-isolation.md:405` already
> says *"if Plan D's verification shows residual gap, Plan #10 is the next move."* **That point has arrived.**
>
> This is why `LOG-1`, `LOG-3`, `LOG-6` and `LOG-7` are audio work wearing logging costumes — and why
> `LOG-9` (per-thread affinity) is P2 rather than P0: the cheap fixes target the same mechanism and may make
> it unnecessary.

---

### 3.3 Workstream: Phone & TTS

**`TTS-1` — Google TTS produces no audio at all, and the app reports success.** ⚠ **ROOT CAUSE FOUND.**
*Not queued. Effort: **5 minutes** for the config fix; 2–3 h for the validation that stops it recurring.*

The live config store reads `tts:defaultEngine = Google` with a valid `tts:googleAPIKey` present — and
**`tts:defaultVoice = "en"`, which is an eSpeak voice ID.** The failure chain, end to end:

1. `TTSFactory.cs:398` sends Google `{languageCode:"en-US", name:"en"}` → **400**.
2. `TTSFactory.cs:430-435` logs and throws.
3. `AnnouncementService.cs:69-72` is a **bare catch that swallows it**.
4. `NotificationsController.cs:53` returns **200 OK**.
5. The UI shows **"Success — Announcement sent."**

**The notification produces no audio whatsoever and the app reports success.** `appsettings.json:178` had
the correct `en-US-Standard-A`; the store value written 2026-02-11 by the TTS config tab clobbered it. **The
UI field's own hint text (`SystemConfigPage.razor:684`) recommends `"en"`** — the UI is actively teaching
the wrong value. Nothing validates that the voice belongs to the selected engine.

- **Why the cabinet cares:** criterion (b) in its purest form. A console that announces "the front door is
  open" by saying nothing, while telling you it worked, is worse than one with no announcements at all —
  because you will believe it and stop checking.
- **Fix, in three separable pieces:** (i) set `tts:defaultVoice` to a valid Google voice in the store —
  ✅ **DONE ON THE BOX 2026-08-19**, see below; (ii) correct the hint text; (iii) validate engine/voice
  compatibility on write and refuse the save. (ii) is P0 and takes minutes; (iii) is P1.

> **✅ (i) RESOLVED — live on `radio`, 2026-08-19.** `tts:defaultVoice` was set to **`en-US-News-K`**
> (Google's female broadcaster voice, owner's choice from the 69 available en-US voices) via
> `POST /api/configuration/tts`, which fires `ConfigStoreChangeNotifier` so no restart was needed. The
> write handler (`ConfigurationController.cs:306`) upserts only the keys posted rather than replacing the
> section, so a single-key POST left `tts:googleAPIKey` intact — **verify this before any future
> section-level write.** Confirmed working end to end from the Serilog file sink: engine `Google`, no
> `Google TTS API error`, ducking engaged at 20% `FadeSmooth` and released to 100%, source removed cleanly.
> **Ducking was never broken on the notifications path** — it simply was never reached, because TTS threw
> on the invalid voice first. That closes the owner's ducking complaint as a duplicate of this row.
>
> **Two things this fix surfaced that remain open.** (1) **`espeak-ng` is not installed on the box at all**
> — `/api/sources/events/tts/engines` reports ESpeak `isAvailable: false`. Anything routed to ESpeak
> produces nothing, which includes the **Event Sources → TTS preview button** that hardcodes it
> (`SourcesController.cs:636`, `SystemConfigPage.razor:1834`). That is a second, independent failure and is
> almost certainly the button the owner was testing. Tracked as **`TTS-7`**. (2) **The fix is box-only.**
> `appsettings.json:178` still ships `en-US-Standard-A`, and the store value overrides it — durable across
> restarts and deploys (`data/config/` is not wiped), but a fresh install elsewhere still gets the robotic
> Standard voice. Tracked as **`TTS-8`**.

---

### 3.4 Workstream: Test & Ops Hygiene

**`TEST-1` — The suite produces false CI signals.** ✅ **SHIPPED 2026-09-01 as [#483](https://github.com/mmackelprang/RTest/pull/483).** Measured proof rather than a passing suite: with a listener bound to the port during full runs, `Radio.Web.Tests` went from **74 TCP connections to `127.0.0.1:5000` to 0** (and 0 on :5004). Part (b) turned out not to belong to this row at all — see `TEST-3`. ⚠ **First, by a wide margin (O1).**
*Already queued 📋, owner-ranked first. Effort: 1–2 days.*

Four tests fail under timing/load pressure and pass on retry. The bUnit one
(`VisualizerPanelTests.cs:184-193`) races a real `await` and wants `WaitForAssertion` — five files in that
project already use the idiom. **Underneath it sits the actual defect: unit tests can reach an ambient
`localhost:5000`.** A unit test whose result depends on whether `radio-api` happens to be running on the
self-hosted runner is the root problem.

⚠ **CORRECTED 2026-09-01 — the `Radio.API.Tests` half of this row was misdiagnosed, and the correction
matters because it splits one row into two unrelated defects.** The record described *three* tests timing out
at *30–46 s*, and treated them as a second symptom of the non-hermetic rig. Measured: **16–18 tests inflate,
none times out** (325 passed / 0 failed both under load and in isolation), and the worst case is **14.2 s,
not 46 s**. Every inflated test is **the first-executed test of a class taking
`IClassFixture<CustomWebApplicationFactory<Program>>`** — 17 such classes, 17 inflated entries — so what is
being measured is `WebApplicationFactory` host boot, which xUnit bills to the first test in the collection.
**`Radio.API.Tests` cannot reach the network at all**: every `HttpClient` comes from `_factory.CreateClient()`
over an in-memory `TestServer`. A control run of the project alone against 40 CPU-burner processes, with
`:5000` closed, reproduced the inflation exactly (1.05 s → 3.43 s per first-of-class, wall 5 s → 20 s). The
cause is **CPU oversubscription** — 11 test assemblies running as concurrent processes, each defaulting
xUnit's `maxParallelThreads` to all 32 logical cores, with only `Radio.Web.Tests` carrying an
`xunit.runner.json` and that one setting no limit. The record's *"22/22 in ~5 s in isolation"* also does not
survive checking: **no class in `Radio.API.Tests` has 22 tests**, and ~5 s is the whole project's isolation
wall time — a class count appears to have been conflated with a project-level timing. **Re-filed as `TEST-3`, and see that row before
trusting the paragraph above.** This was first written up as a benign performance artifact — that was
wrong, and the correction is instructive. On CI (a more contended runner than the 32-core dev box these
measurements came from) the same two tests do not merely inflate, they **fail**, with
`InternalServerError` where `NotFound` / success was expected. It blocked a merge on PR #485. The `30–46 s`
in the original record was reading *durations* as timeouts, which is the one thing the original got right
in substance and wrong in mechanism: the tests do fail under full-suite parallel load, just not by timing
out. **The cause is shared SQLite state** — `CustomWebApplicationFactory` never isolates
`Database.RootPath` (`"./data"`, relative), so all 17 hosts open the same files at once. CPU pressure is
only what makes the collision likely.

- **Why the cabinet cares:** criterion (d). This is not quality-of-life. **Everything else in this document
  is verified by this suite**, and a false green already cost one session a wrong diagnosis. The most recent
  Builder cycle (`KIOSK-2`, PR #480) shipped with 5 test failures that reproduce identically on `main`, on a
  branch that **changes no C# at all** — the problem in miniature.
- **Depends on:** nothing. **Blocks:** honest verification of every other row.

---

**`OPS-1` — Build stamp on `radio-web` + real deploy verification for both services.** ✅ **SHIPPED 2026-09-01 as [#485](https://github.com/mmackelprang/RTest/pull/485) and deployed.** Both services now answer `/api/health/version` and the deploy `exit 1`s on either mismatch. The box was found running `d9d5477` from Aug 18 — a commit **not in main's history** — with nothing flagging it. Also fixed the two defaults, which had to move together: flipping `$TargetHost` alone would have made `Deploy-ToPi.ps1` ship ARM64 to the x64 box.
*Already queued 📋. Effort: 0.5–1 day.*

~80% already built: the SHA is stamped by `Directory.Build.props`, `Deploy-ToLinux.ps1` already passes
`-p:SourceRevisionId` to both publishes and already `exit 1`s on an API mismatch, and `Radio.API` already
serves `/api/health/version`. The gap is narrow: **`Radio.Web` has no version endpoint** — its assembly
already carries the SHA, it is just unreadable — and the deploy checks only `systemctl is-active` for
`radio-web`. **A stale web binary passes verification silently.**

Already folded in (queue, 2026-07-31): `Deploy-ToLinux.ps1:51` defaults to `-Runtime linux-arm64` while the
box is `x86_64`, so **the literally documented invocation ships ARM binaries to this x64 host**, and
`$TargetHost` still defaults to the stale `piradio`.

- **Why the cabinet cares:** criteria (c) and (d). Once the cabinet is closed, every fix arrives by deploy. A
  deploy that reports success without proving what it did means the first failed fix gets debugged as a code
  bug. Today's interim gate is `grep -ac <branch-only-symbol> /opt/radio-console/web/Radio.Web`, which is a
  workaround, not a gate.
- **Should land before the first post-install deploy** (O8).

---


### 3.5 P0 items promoted by the 2026-08-19 owner decisions

> Six items moved up out of P1 on the day the owner answered the open questions. They are collected here
> rather than scattered so the delta is auditable: **three because the knobs now ship live (D1), two because
> GV read demonstrably works (D17), one because the keyring question got an answer (D15).**

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`ENC-6`** | **Sleep, wake and the dark panel — the three-state model.** ⚠ **SPLIT 2026-09-02 BY THE `ENC-15` RESULT — read this before claiming the row.** **The blanking half DOES NOT SHIP.** `ENC-15` failed its gate ([report](uat/2026-09-02-enc15-touch-wake-gate/REPORT.md)): the touchscreen leaves the USB bus when the panel powers down, so touch cannot be a wake path, and the encoder has no evdev nodes, so it cannot wake the compositor either — a knob wake would depend entirely on `radio-api` being alive to call the unblank itself. That is one application-mediated wake path, not two. **This is the consequence this document pre-committed to** — *"If touch cannot wake it, blanking does not ship until it has two wake paths"* — so it is a recorded outcome, not a new decision. Re-enabling DPMS, the two coupling rules, and the Ambient-Dark / Standby-Dark states all go with it. ✅ **The NON-BLANKING half is unaffected and remains P0** — do not read "blocked" and skip the row. Still in scope and still worth its tier: the sleep/wake state model minus the two dark states; the **wake latch** (`TryWakeFromSleep` calls `WakeAsync` fire-and-forget at `:121` and returns true, so a 10 ms poll silently discards several more events before `IsSleeping` flips); the **D22 rule** (a turn from Standby lights the panel and does **not** resume audio; a press or a screen tap does); the refinement that **a consumed input still renders that knob's current value**; and the underlying defect that idle-at-30-min navigates to `/sleep` **without** calling `SleepService`, so `IsSleeping` stays `false` and every knob acts silently. Re-estimate the non-blanking half at **2–3 d**. ✅ **UNBLOCKED: Designer Rev 3 §8 resolves the D8 collision.** Rev 2 said *volume acts in place*; D8 said *any input is a wake signal*. They disagreed because they were written about **different states** — Rev 2's rule assumed a lit panel showing a clock; D8's instruction is about a **physically dark** one, which did not exist when Rev 2 was written. Resolved on one criterion the user can always perceive — **the panel's own light**: <br><br>**Rule 1 (dark):** the first input does exactly one thing — **light it.** Consumed. Changes no audio, no station, and never resumes from Standby. <br>**Rule 2 (lit):** VOLUME **acts in place** and does not change screens; everything else wakes to the full UI and is consumed. <br><br>**Three refinements that carry most of the value:** **(a) waking from dark lands on the dim Ambient screen, never the full bright UI** — a 2 a.m. nudge produces a dim clock and a readout, not a 1920×720 wall of Home; **(b) a consumed input still renders that knob's current value** — *the first detent tells you where you are, the second one moves it*, which converts the consumed input from a loss into an answer; **(c) re-blank after 60 s from Ambient, 30 s from Standby.** Five states total: Awake / Ambient / Ambient-Dark / Standby / Standby-Dark. <br><br>**Also in scope:** re-enable DPMS blanking with its **two coupling rules** — never blank when the encoder device is absent, and **if it disappears while blanked, unblank immediately and stop blanking until it returns** (fail toward light: *a screen left on is a nuisance, a screen that cannot be turned on is a service call*). ✅ **D22 is settled scope on this row, not an open question: a turn from Standby lights the panel and does NOT resume audio; a press (any button) or a screen tap does.** The owner approved Designer's narrowing verbatim — *"that D8 narrowing is fine, keep it."* It is one branch in the router, and Designer's acceptance test pins it: *"Standby: a turn lights the panel and does not resume audio; a press resumes and restores the pre-sleep mute state."* <br><br>Plus the **wake latch** — `TryWakeFromSleep` calls `WakeAsync` fire-and-forget (`:121`) and returns true, so with a 10 ms poll several more events arrive before `IsSleeping` flips and each is silently discarded. **Blanking makes this more important, not less: a fast spin in the dark must lose one detent, not twelve.** | The overnight failure mode, and it is current behaviour: idle-at-30-min navigates to `/sleep` **without calling `SleepService`**, so `IsSleeping` is `false`, audio keeps playing, and every knob acts silently on a screen showing a clock. ⚠ **The cost the owner is accepting knowingly is one detent** — at 2 a.m. the first volume nudge lights the panel and shows where you are; the second moves it.<br><br> ✅ **SHIPPED 2026-09-02 — the non-blanking half, [#539](https://github.com/mmackelprang/RTest/pull/539).** Three states (`Awake` / `Ambient` / `Standby`) derived from `IsSleeping` plus a new `IsSleepScreenVisible` that `Sleep.razor` reports about itself via `POST /api/system/sleep-screen`; the sleep gate, the synchronous `TryClaimWake()` latch, D22, the consumed-value readout and the Standby hint. **The blanking half stays withdrawn** — `ENC-15`'s gate failed and nothing here touches display power. ⚠ **Open follow-up:** `IsSleepScreenVisible` is in-memory, so an API restart while the kiosk sits on `/sleep` reinstates this row's own defect until something re-renders, and every deploy restarts `radio-api`. Recorded in `design/FUTURE-WORK.md` §7 with the mechanism that closes it. | 2–3 d (non-blanking half only) | `ENC-1`, `ENC-4`; ⚠ **`ENC-15` was the HARD predecessor of the blanking half and it FAILED — that half is out of scope** | No |
| **`ENC-8`** | **The encoder Settings surface, plus the stale docs.** ✅ **UNBLOCKED: Designer Rev 3 §7.7–§7.8 reconciles D21 with Rev 2's "delete the editors".** Four cards under System Config → Integrations → Rotary Encoders: **(1) Status** — connection, config tier, last verified, last saved-to-device with a staleness comparison. **(2) Configuration — read-only, complete, comparison always on:** **11 fields × 4 encoders = 44, plus the device-level `steps_per_detent` = 45 compared**, labelled by **cabinet name** (`VOLUME · SOURCE · PRESETS · TUNING`), never by index, each row showing whether the device agrees. *This* is what "configuration in the app" needs to mean — full visibility, not 45 numeric footguns. ⚠ **CORRECTED 2026-09-04 — this row said *"all 24 fields × 4 encoders"* and, one sentence later, *"not 24 numeric footguns"*; the second reads off the first, so both were wrong together.** The comparable set is defined by `RotaryEncoderConfigVerifier.Compare` — one device-level `steps_per_detent`, then per encoder `min_value`, `max_value`, `step_size`, `wrap`, `reverse`, and three acceleration tiers × (`threshold_ms`, `multiplier`): **11 per encoder, 44 across four, 45 compared** (`src/Radio.Infrastructure/Platform/Input/RotaryEncoderConfigVerifier.cs:54-77` at this commit; cited by content because it is a method body that moves). Confirmed the same day against a live device payload. **`24` was wrong on either reading of the phrase** — as *24 per encoder* it would total 96, and as *24 in all* it is short by 21 — which is why it is corrected rather than re-worded. **(3) Four `Reverse direction` toggles — the one editable thing.** Toggling pushes immediately (`0x02` + verify) and marks the flashed copy stale. **(4) Actions:** `Save to device` (config + verify + flash), `Re-apply settings` (push + verify, no flash), `Reset counters`. ⚠ **Factory reset is deliberately NOT on this page** — it would put the device on defaults where one volume detent spans the full range. <br><br>⚠ **`ENC-13` is folded in here and its safe-baseline idea is withdrawn** — see the P1 table note and §6. Flash now holds **exactly the operating configuration**, i.e. exactly what the screen shows. <br><br>**The two numeric fields, settled:** **`TuningStepKHz` — delete outright** (nothing reads it, nothing should; the tuner owns its own step). **`VolumeStepPercent` — RELOCATED, not deleted**: it is a genuine device field (VOLUME `step_size`), so it appears read-only in card 2. What goes away is the **duplicate editable numeric** on the Integrations tab, which was a second source of truth for a value the device also holds. **One value, one place, visible.** <br><br>✅ **SHIPPED 2026-09-02** — [#527](https://github.com/mmackelprang/RTest/pull/527), plan at [`design/plans/ENC-8-encoder-settings-surface.md`](../design/plans/ENC-8-encoder-settings-surface.md) (20 tasks; its §7 records the Task 4 hardware-gate result). ⚠ **Task 4's gate caught a HIGH regression and is the argument for having it:** unifying the boot and maintenance read-back paths left the boot push awaiting a reply only the read loop could deliver, which does not hang — it times out and settles in `Degraded`, tightening the volume clamp to 2 units per event on every boot. ⚠ **Two further defects were invisible to every automated gate**: the page could not deserialize its own API response (enums cross as strings; the bUnit rig fails every call, so a green suite and a dead page look the same), and Radzen renders only the selected tab's body, so markup-absence assertions pass vacuously unless the panel is proven to have rendered. ⚠ **Two claims in this row were false and are corrected by the PR**: `TuningStepKHz` survived in `INTEGRATIONS.md` at `:111`/`:126`, not `:80`/`:95`, and `CLAUDE.md`'s MudBlazor line was already fixed by #518 — the surviving copy was `design/DECISION-LOG.md:77`. A seventh stale-protocol statement not listed here (`:158`, button state "at byte 5") was fixed in the same pass. ⚠ **`Reset counters` copy deviates from the plan deliberately**: it claimed counters were "zeroed on the device", which the protocol cannot confirm. <br><br>Plus the doc fixes — ⚠ **two of the citations below were checked against the tree while planning and are corrected here.** The UI mapping table is at `SystemConfigPage.razor:1482-1496`, and **it no longer contradicts the router**: quick win #6 (#489) corrected the rows by hand, so the remaining work is structural — serve it **from** the router so it cannot drift again when `ENC-5`/`ENC-7` remap. The wrong 8-byte report format is at **`INTEGRATIONS.md:22-26`**, not `:19-24`. Its troubleshooting still says to swap A/B pins on the Pico (`:125`) — superseded by `reverse`. **Three further false statements were found in the same file and folded into the row:** `:5` and `:88` both say the encoder integration is **disabled by default**, which `ENC-0` reversed; and `:80` / `:95` still document **`TuningStepKHz`**, which PR #490 deleted from every code path. **`CLAUDE.md` also describes `Radio.Web` as MudBlazor; there is no MudBlazor in this repository** — it is Radzen only. | ⚠ **The `Save to device` copy is load-bearing and a reviewer should treat a mismatch as a defect.** Designer's own words on why the baseline died: *"A Save button that writes something other than what the screen shows is exactly the class of lie this project keeps shipping."* That is this repo's pre-merge comment-accuracy rule applied to a button. | 1–2 d | `ENC-1`, `ENC-2`, `ENC-11` | No |
| **`ENC-12`** | **Tiered config-fault surfacing.** ⭐ **New in Rev 2 §7.4.** Three surfaces, in the order the owner will actually meet them: **(1)** a **cross-route fault badge on the topbar nav pill** — amber for Degraded, `--signal-red` for hard fault — reusing the pattern `HANDOFF-bell-failure-surfacing.md` §3.7 already established for a persistent hardware degradation that must be legible from any page. **(2)** **One** Radzen notification, **once per session, on the first transition** into degraded or fault — *"Knob settings couldn't be applied. The knobs still work, but they may feel wrong."* / *"Knob safety settings couldn't be applied. Volume is limited until this is fixed."* Never repeated on retry churn: **a fault that flaps must not become a notification storm.** **(3)** The status card carries the field-level `Sent` vs `Read back` table plus its actions. ⚠ **CORRECTED 2026-09-02 while planning: surface (3) is `ENC-8`'s, not this row's** — it is the same card `ENC-8`'s §7.8 card 1 describes, written up twice by two rows drafted at different times, and planning them apart would have built it twice. **`ENC-8` owns the whole page; `ENC-12` owns the badge and the notification and adds no markup there.** The two button names here are also stale: **`Retry now` is Rev 3's `Re-apply settings`** under an older name (one button, built by `ENC-8`), and **`Restore designed defaults` is not built by either row** — Rev 3 removed the 24 editable numerics it would have undone, the undo for a `Reverse` toggle is the toggle, and a button reading “defaults” on this page is one misread from the factory reset §7.8 deliberately excludes. <br><br>✅ **SHIPPED 2026-09-02 in [#535](https://github.com/mmackelprang/RTest/pull/535)** — badge + one-per-session notification; a healthy boot stays silent. **Neither `Retry now` nor `Restore designed defaults` was built by this row:** `Retry now` is Rev 3’s `Re-apply settings` under an older name and is `ENC-8`’s, and `Restore designed defaults` is built by neither row. Planned as — [`design/plans/ENC-12-config-fault-surfacing.md`](../design/plans/ENC-12-config-fault-surfacing.md), 11 tasks, queued 🔒 behind `ENC-8` and `ENC-4`. | The safety *behaviour* is in `ENC-11`; this is the legibility layer on top of it, which is why it separates cleanly. Its value is that it is the one thing standing between the owner and a knob that quietly feels wrong forever. It also reconciles Designer §7.2's silent-boot rule with this document's `UI-3` argument — **silent when healthy, legible from any page when not**, without a new diagnostic surface. Promoted to P0 with D1: with the knobs live at install on a sealed cabinet, an undiagnosable degradation is the difference between "the knob feels wrong" and "the knob feels wrong and nobody can find out why without a laptop". | 1–2 d | `ENC-11` (O10) | No |
| **`PHN-1`** | **The ADR-029 seam.** A new `IEventPlaybackService` **beside (not inside)** `IAnnouncementService`; `POST /api/audio/events`; a new `GvMediaClient` with Radio.API fetching the media **itself, server-side**, into a bounded LRU cache at `./data/gvmedia/`; a **closed discriminated request set with asymmetric arms** — speech carries literal text, voicemail carries a `(kind,id,duration)` **reference**, never a caller-supplied URL, **which would be an SSRF primitive**; and `IEventAudioSource` gaining `Position` / `IsSeekable` / `Seek` / `Pause` / `Resume` copied from `IPrimaryAudioSource`. ⚠ **ADR-029 warns: do NOT use `POST /api/sources/events/{tts,file}` as the template** — `SourcesController.cs:601` injects `IDuckingService` and never uses it (those events don't duck), `:651` adds a mixer source that is **never removed or disposed** (leaks per play), and `PlayFileEvent` **double-plays**. | Once it is an event source on the mixer, **mute, volume, balance, ducking and output routing all apply for free**. The cache is **blackout mitigation, not optimization** — GV auth is dead ~9 min in every 20, so a replay has ~45% odds of 502ing. ⚠ **Promoted to P0 as the hard enabler of `PHN-2`, which D17 established is a live defect today.** ⚠ **D19 also widened the arc: canned replies (ADR-029 Feature C) are explicitly wanted** — *"A few simple/canned responses will suffice"* — **and the owner does not want an on-screen keyboard**, which makes C easier, not harder. The phone arc is **A + B + C**, not A + B. | 1.5–2 wk for the seam + A; C adds ~2–3 d | — | **No** |
| **`PHN-2`** | **Voicemail through the audio engine (ADR-029 Feature A).** Today `VoicemailPlayer.razor:8` is an HTML5 `<audio>` element pointed straight at `http://radio:5004/api/gvbridge/voicemail/{id}/audio`; **the browser fetches and decodes it itself.** Mute is enforced only at `SoundFlowAudioEngine.cs:1136-1146`, as a gain on the API's SoundFlow playback device. **No shared node exists between the two paths** — this is architectural, not a wiring bug. | **Volume level, balance, ducking and Cast routing are ALL bypassed too.** With Cast active the voicemail still comes out of the **local speakers**. The radio does not duck under a playing voicemail — **they mix in the room's air at full level each.** The most viscerally wrong thing this machine currently does. ⚠ **D17 SETTLES THIS AND IT CHANGES THE TIER.** The owner states plainly: *"I can see voicemail and text messages in the Radio Console UI today. I can listen to the voicemail and read the texts on the screen."* **GV read works.** So this is **not latent — it is what the cabinet does right now**: press play on a voicemail while the radio is on and two sounds run in the room at full level each, with mute, master volume, balance, ducking and Cast routing all bypassed. Criterion (b), live. **P1 → P0.** ⭐ **THIS ROW ALSO CARRIES THE PHONE ARC'S ENTIRE VERIFICATION DEBT, pinned here 2026-09-03 because it had nowhere else to live.** The `PHN-1a`/`PHN-1b`/`PHN-1c` plans each defer their device-only checks to *"PR 6"* — which **is** this row — and **PR 6 has no plan file**, so a re-sequencing of the arc would evaporate all four silently. They are: (1) **`SoundPlayerBase.Seek` actually repositions a short local MP3** (ADR-029 §14 Q3; planning proved only that the method exists and returns a bool — fallback is stop-and-restart-at-offset); (2) **`SoundPlayerBase.Time` advances during playback** (`Position` and the re-armed completion wait both read it; if pinned at zero, `AwaitCompletionAsync` degrades to today's `Task.Delay(_duration)`); (3) **pausing a TTS source no longer reports completion** (`PHN-1a` §0.4 C-6 — unit tests reach only the not-playing guard; the defect lives in the live monitor loop); and (4) **`./data/gvmedia` is writable under the service account** (`PHN-1b` §2.2 item 4, re-carried by `PHN-1c` because `GvMedia:Enabled` ships `false` and PR 3 performs no first fetch). ⚠ **A `MediaNotFound` during this UAT is as likely to be `XR-3`'s blackout as a bad id** — record the wall-clock time of every failure and retry after five minutes before concluding anything. The `curl` recipe is in `PHN-1a` §2.2. | 3–4 d | `PHN-1` (O6) | No |
| **`SEC-1`** | ✅ **CLOSED 2026-09-02 — verified on the box, no code required.** Both halves were already satisfied and the row was stale in two ways. **(1) The branch name in this row does not exist.** It is recorded as `fix/dataprotection-keyring-persist-path`; the actual branch is `fix/web-dataprotection-keyring`, and it is **0 commits ahead of `main`** — already merged. **(2) The keys demonstrably persist.** `/opt/radio-console/data/keys/key-c718ac1f….xml` dates from **Jul 17** and `/opt/radio-console/data/keys-web/key-24242edf….xml` from **Aug 18**; both survived every deploy since, including two on 2026-09-02, because they live under the persistent data root that the deploy's `rsync --delete` of `api/`/`web/` does not touch. **(3) The owner re-entry (remediation (a)) is done.** Zero decrypt failures in the log, `/api/secrets/tts` returns real masked values, and a live announcement produced *"Creating TTS audio … with engine Google"* followed by ducking at 20% and a clean teardown — so the secret decrypts and the whole path works. D15's *"we need to ensure the keys are retained"* is answered by evidence rather than by more code. ⚠ **One defect found while verifying, filed as `SEC-2`:** the Google and Azure key slots returned byte-identical masks. That was read at the time as a mis-pasted key; it was not — see the corrected `SEC-2` row, which turned out to be a P0 in the secrets write path, fixed by PR #523. | Latent, not blocking — see `SEC-2`. | 0 (closed by verification) | — | — |
| ~~**`SEC-1`** (original text)~~ | ~~Resolve the investigation's outstanding owner action, and land or close the branch,~~ recorded as awaiting review. ✅ **D15 ANSWERED and it resolves to P0:** *"We need to ensure the keys are retained."* No longer confirm-or-close. if DataProtection keys do not persist across restarts, encrypted secrets (`${secret:identifier}`) break on every restart, which in a sealed cabinet is criterion (c). **First task is to read the investigation, then land or close the branch.** | If DataProtection keys do not survive a restart, encrypted secrets (`${secret:identifier}`) break on **every** restart inside a sealed cabinet — criterion (c), and the recovery is a laptop. The branch `fix/dataprotection-keyring-persist-path` is **awaiting review**, so most of the work may already exist. | 0.5 d triage + land the branch | Owner has decided; needs review | No |
| **`ENC-17`** | ✅ **DONE 2026-09-03 — the instrument ships, and the row's stated route did not work.** **`tools/encoder-harness/virtual_encoder.py`** creates a **real USB HID device** carrying the RotaryUsb identity and report descriptor and injects genuine Input Report `0x01` frames, so the shipped `HidRotaryEncoderService` opens, decodes and acts on them exactly as it does the physical knobs — same udev rule, same HidSharp enumeration, same decoder, same `ENC-1` accumulator semantics. Nothing in the console is modified, reconfigured or restarted to accommodate it, and nothing was installed on the appliance. Usage lives in `tools/encoder-harness/README.md`, beside the encoder section of `design/INTEGRATIONS.md`, and at the top of `docs/HANDOFF-NEXT-SESSION.md`. ⚠ **`/dev/uhid` — the route this row specified — does not work, and the reason is recorded so it is not rediscovered.** A uhid device is parented to `/sys/devices/virtual/…` and therefore has no USB ancestor. The shipped udev rule matches `ATTRS{idVendor}`, which resolves by walking to a USB parent, so it never fires (workaroundable by chmod). **HidSharp is not workaroundable**: measured 2026-09-03, with a uhid `cafe:4005` device present and readable by `mmack`, `DeviceList.Local.GetHidDevices()` did not list it **at all**, so the filtered query returned 0 and even `RotaryEncoderOptions.DevicePath` could not select it — you cannot filter a list that never contained the device. HidSharp's Linux backend resolves vendor and product from the USB parent's sysfs attributes. The transport is therefore a **usbip loopback USB gadget** (`usbip-vudc` + `vhci-hcd` + `libcomposite` + `usb_f_hid`, all already present; `dummy_hcd` is **not** available for this kernel). That is *higher* fidelity than uhid would have been — `lsusb` shows the device, the real udev rule grants access unaided, and `detach` is an actual USB port detach. **Identity/coexistence, decided deliberately:** the harness takes the real `cafe:4005` identity and **unbinds the real encoder** for the duration, rebinding on exit — so exactly one device answers. A distinct VID/PID via `RotaryEncoder:VendorId`/`:ProductId` was rejected because it moves UAT off the shipped configuration at the layer under test, and because an override left behind points the console at a device that does not exist, leaving the real knobs dead across reboots with nothing on screen to say why. **Cannot be left running by accident:** stdin EOF exits, `--max-seconds` (default 300) is a hard watchdog, `SIGINT`/`SIGTERM`/`SIGHUP` exit cleanly, teardown runs in a `finally:` on every exit the process can observe, and `usbipd` is a child with `PR_SET_PDEATHSIG=SIGKILL` so the kernel reaps it regardless. No unit file, no autostart. ⚠ **`SIGKILL` is the exception and does leak — measured, not assumed:** usbipd died as designed but the configfs gadget and the vhci attachment both survived, leaving a virtual `cafe:4005` enumerated and the real encoder unbound. An earlier draft of this row claimed losing usbipd detaches the device; it does not. What survives is **inert** — every report originates from a typed command, so the `ENC-3` synthetic-volume hazard is not reachable from a leak; the cost is that the physical knobs stay dead until recovery. `--cleanup`, restarting the harness, and a reboot were each verified to restore it. **Verified on the appliance against `5e571b8`:** configuration verifies `Configured` on attempt 1 at `107`-byte reports with accumulators; a turn moves volume at 2%/unit; **the `ENC-3` per-event clamp holds at ±6 units (±12 points) against single events of 20 and 50 detents**; the **`ENC-4` HUD renders left-anchored** at `left: 24px`, observed live with `--encoder-band-y` of `90px` (index 0) and `630px` (index 3) — indices 1 and 2 emitted selector phases, which `EncoderHud` routes to the centred `EncoderSelectorOverlay` rather than a banded card, and that branch is on **phase, not index**, so the other two bands were not exercised in the browser; all four are pinned by `FrontPanelGeometryTests` and `EncoderHudTests`. A 900 ms hold on encoder 0 **synthesises a long press with the progress ring** and a 200 ms hold does not (only encoder 0 has a long action wired). ⭐ **`ENC-1`'s re-baseline rule HOLDS against a real USB disconnect** — Designer's highest-weighted test, never previously run that way: 50 detents accrued while unplugged produced a **0-point jump** on replug, where unclamped it would have been +100. **21 xUnit tests** bind the harness's byte layout to the shipped decoder through a shared golden-vector file, so a drift on either side fails. **No encoder defects were found**; scope held to delivering the instrument. ⚠ **It does not replace the owner's hand on the panel** — feel, acceleration and whether a spin *sounds* right are still his — and it does not exercise the firmware. <br><br>*Original row text follows.* ⭐ **NEW 2026-09-02. The whole encoder arc has been shipping with half its UAT unrunnable, and nobody wrote that down until three rows in a row hit it.** There is **no software path to inject encoder input**: `cafe:4005` exposes only `/dev/hidraw3` with **zero evdev nodes**, so nothing can synthesise a turn or a press. Writing to `hidraw` sends *to* the device, not from it. The consequence, in the shippers' own words — `ENC-5`: *"the overlay's on-screen behaviour, states A–E, the wrap animation, the band-vs-source split, is **not verified**; it needs a hand on a physical knob."* `ENC-6`: *"scenarios B, C, D, E and I require physically turning the knobs."* `ENC-12` worked around the presence half only, by unbinding the USB device — which tests *disconnect*, not *input*. **So the arc's most user-visible behaviour is the part least verified, and every row has had to state an honest uncovered gap rather than a pass.** ⭐ **The fix is available and cheap: `/dev/uhid` is present on the appliance** (verified 2026-09-02), so a virtual HID device can present the RotaryUsb report descriptor and inject Input Report `0x01` — real 37-byte reports through the real decoder, `ENC-1`'s accumulator path included. ⚠ **Two design constraints, both load-bearing.** (1) The service matches on VID/PID `cafe:4005`, so a virtual device using the same identity **will be picked up alongside the real one** — the harness must either take a distinct id that the service is told about explicitly, or be usable only while the real device is unbound. Decide that deliberately; two encoders answering at once is its own confusing failure. (2) It must be **impossible to leave running** — a synthetic volume source inside sealed furniture is exactly the hazard `ENC-3`'s clamps exist for. Prefer a foreground process that dies with its SSH session over anything installed. | **This is punch-list criterion (d): the substrate other verification rests on.** Six encoder rows shipped on 2026-09-02 and not one could fully verify the behaviour a guest actually touches. Every one recorded the gap honestly, which is the right conduct and not a substitute for coverage. It is also the cheapest remaining way to raise confidence in the arc before the cabinet closes, because it converts *"a human must turn each knob"* into *"a test can."* ⚠ **It does not replace the owner's own hand on the panel** — feel, acceleration and whether a spin *sounds* right are still his, and `ENC-3`'s deferred volume ramp says so explicitly. | 1 d | `ENC-1` (shipped) | No |
| **`ENC-16`** | ✅ **SHIPPED 2026-09-03 — the owner decided to relax the clamp, and one case had to change tiers to make that safe.** `Degraded` and `Configured` now run the normal ±6 clamp; `Unknown`, `Transient` and `HardFault` keep the tight ±2 — exactly the tiers where `wrap`/`reverse` are unverified or disagreeing. **`Transient` deliberately keeps the tight clamp:** it means *"not confirmed yet"*, not *"confirmed fine"*, and the boot window is when a fresh or factory-reset Pico runs acceleration at ×50. ⚠ **The trap this row did not see: `Classify(null, 3)` — the device never answered — also returned `Degraded`**, and that state confirms nothing at all, `wrap` and `reverse` included. Relaxing the tier without moving that case would have handed the normal clamp to a device possibly still on factory tiers — re-creating the exact hazard the arc exists to prevent, inside the row written to close it. It is now `HardFault`, which is also the only tier whose toast tells the owner volume is limited. **No owner-facing copy changed** — both toasts were written for this behaviour and are true of it for the first time. Handoff §7.6 gains the clamp column it never had (Rev 6). _Original text follows._ ⭐ **NEW 2026-09-02, found while shipping `ENC-12`. The console tells the owner something untrue about its own safety state.** `RotaryEncoderConfigVerifier.VolumeClampFor` returned the tightened clamp for **every** tier except `Configured` — so a **`Degraded`** console is volume-clamped *exactly as hard* as a hard-faulted one. But handoff §7.6 shows the tightened clamp only on the Hard-fault row, and `ENC-12`'s two toasts inherit that: Degraded says *"The knobs still work, but they may feel wrong"*, while only Hard fault says *"Volume is limited until this is fixed."* **A `Degraded` owner reads "they may feel wrong", finds the volume knob sluggish, and has been told that is not happening.** ⚠ **Decide which side is wrong before changing either** — it is genuinely arguable that the clamp should relax for a feel-only mismatch (acceleration disagreeing is not a safety fault), and equally arguable that anything unverified deserves the tight clamp and only the copy is wrong. **Do not "fix" this by editing the toast alone**; the two must be made to agree deliberately. `ENC-12`'s Builder deliberately did **not** touch spec-verbatim owner-facing copy and logged it instead, which was the right call. | Small, but it is the exact defect class this repo keeps paying for: a message asserting more than the code does — the seventh found on 2026-09-02 — and this one is read by the owner rather than by an engineer. | 0.5 d incl. the decision | `ENC-12` (shipped) | ✅ **SHIPPED** — see the resolution at the head of this cell |
| **`ENC-15`** | ✅ **DONE 2026-09-02 — GATE FAILED.** Full [report](uat/2026-09-02-enc15-touch-wake-gate/REPORT.md). **Touch cannot wake a blanked panel on this hardware, and the reason is more final than the row anticipated: the touchscreen is powered by the panel and leaves the USB bus when it blanks** (`usb 3-1: USB disconnect` about a second after the blank, re-enumeration about a second after the unblank). The row asked whether the compositor *ignores* touch while dark; the answer is that there is no device left to ignore it, so no event is generated at any layer. ⚠ **Two further findings, both worse than the verdict.** **(1) The encoder is not a compositor input device either** — `cafe:4005` has **zero evdev nodes**, only `/dev/hidraw3`, so a knob cannot reset the idle timer or wake a blank by itself; a knob wake works only if `radio-api` reads hidraw and *itself* calls the D-Bus unblank, **which makes `radio-api` a single point of failure in the only remaining wake path**. Designer Rev 3 §8.5 treats the encoders as *the* hardware wake source; on this hardware they are an application-mediated one. **(2) The recovery line this row specified is insufficient as written** — `org.gnome.ScreenSaver SetActive false` is a **no-op** in the dark state that was actually reached (`GetActive=(false,)` while `dpms=Off`), because DPMS-off is a separate control. `org.gnome.Mutter.DisplayConfig PowerSaveMode` read `3` at the time and setting it to `0` is what recovered the panel *and held*, where the screensaver cycle bought 13 s. **`INTEGRATIONS.md` now carries the Mutter line as primary.** ⚠ **The brick this row exists to prevent was reproduced on real hardware** — a ~13 s on/off oscillation the documented line could not break. The box was left verified stable with no `gsettings` changed. **The one limitation: nobody touched the glass** — the verdict rests on the mechanism, and only undocumented panel-firmware wake-on-touch could overturn it; an optional confirmatory script is staged at `/tmp/enc15-touchtest.sh`. <br><br>*Original row text follows.* ⭐ **NEW — THE TOUCH-WAKE GATE. Verify on the box that touch can independently wake a blanked panel, BEFORE any blanking ships.** `SleepService.cs:84-87` disabled DPMS in the first place because **touch-to-wake does not work when the compositor blanks input.** If that is still true, then after blanking there is **exactly one wake path — the encoders** — and losing the USB while dark leaves a panel that cannot be woken without a keyboard, inside a piece of furniture. Designer Rev 3 §8.5: *"This is a gate, not a caveat."* **Deliverable: a recorded result, one way or the other.** If touch cannot wake it, blanking does not ship until it has two wake paths. Fold in the recovery line so it is findable at 2 a.m. — `INTEGRATIONS.md`, beside the encoder section: `ssh mmack@radio` → `gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver --method org.gnome.ScreenSaver.SetActive false`. | **The single cheapest item in this document with the largest downside if skipped.** One hour of verification stands between the approved blanking feature and a console that cannot be turned back on from the room it lives in. It is P0 not because it is hard but because **it is a precondition that will look skippable right up until it isn't.** | 1–2 h on the box | ⚠ **Hard predecessor of `ENC-6`'s blanking half** | No |
| **`ENC-21`** | ✅ **DONE 2026-09-04 — the owner put the knobs in the cabinet and every one of them turned the wrong way. Fixed the same day with no code change, no build and no deploy.** The report: **clockwise decreased volume**, and the direction felt reversed on all four knobs. The fix was the per-knob `reverse` override `ENC-8` had already shipped — `PUT /api/integrations/encoder/reverse/{0..3}` with `{"reverse":true}` (each call pushes and verifies immediately), then `POST /api/integrations/encoder/save` to write the verified bytes to the device's flash. **Verified live, not assumed:** all four `reverse` read back `True`, **every field Agrees and zero disagree**, status `Configured`, flash `MatchesCurrentDesign`, saved `2026-09-04T19:44:16Z`. ⭐ **WHY IT NEEDED NO CODE — this is the reusable half of the row.** The seam was built for exactly this and said so before anyone needed it: `RotaryEncoderConfigDefaults.cs:251` reads *"false on every knob, meaning clockwise increases. If a knob is wired backwards this flag is the fix, and it is the one field a human should ever edit."* And `RotaryEncoderDesignedConfig` is shaped so that being right about that costs nothing — the override is a **config-store value** (`RotaryEncoder:Reverse:{index}`) applied in `ResolveAsync`, deliberately **not** inside `RotaryEncoderConfigDefaults.Create`, whose own class comment gives the reason: *"A wiring fact about one cabinet is not a change to the design."* That is why the designed table is still all-`false`, why `RotaryEncoderConfigVerifierTests.Defaults_NeverWrapAndNeverReverse` still passes, and why this row closed through the API instead of through a branch. ⛔ **CAUTION 1 — A HOST-SIDE OR FIRMWARE NEGATION WOULD HAVE BEEN A BUG, NOT AN ALTERNATIVE FIX.** `design/INTEGRATIONS.md` says so under *Troubleshooting → "Encoder turns in wrong direction"* — *"Do not swap the A/B pins on the Pico or negate the delta in firmware — that was the pre-`ENC-2` remedy, and doing it as well as setting `reverse` reverses the knob twice, leaving it exactly as wrong as before."* (`:328` at this commit; **quoted rather than trusted to a line number**, because a number in that file has been wrong in three successive documents.) `reverse` is a **device** field: `RotaryEncoderConfigCodec` writes it at byte 13 of each channel block, it is pushed and read back like every other config field, and **no host-side code negates a delta anywhere** — checked across `src/Radio.Infrastructure/Platform/Input`, where every occurrence of `Reverse` is the store override, the codec, the verifier, the provisioning passthrough or the status card, and a search for a negated delta returns nothing. The direction is decided on the device, so the host must never add a negation of its own. ⚠ *That the firmware is what applies it is inferred from those three facts and from the warning above, not read — RotaryUsb is a separate repository and was not opened for this row.* ⚠ **CAUTION 2 — HARDWARE UNIFORMITY IS UNVERIFIED, AND NO FILE IN THIS REPO RECORDS IT.** The owner observed **VOLUME only**; all four knobs were set reversed on his instruction. Whether all four are wired identically is a fact about the cabinet, not about the code, and nothing here knows it. The flag is per-knob, so a knob that is now wrong *the other way* is **one toggle on the encoder Settings surface** to correct — the cheapest failure mode available, which is why this is written down as an open assumption rather than closed as a certainty. ⚠ **TWO COVERAGE GAPS FOUND WHILE INVESTIGATING, NEITHER CLOSED BY THIS ROW.** **(1) Tuning direction is pinned by no test.** `RotaryEncoderActionRouter.StepRadioFrequencyAsync` branches on `delta > 0` between `StepFrequencyUpAsync` and `StepFrequencyDownAsync`, and the router tests' radio double implements **both as `Task.CompletedTask`** (`tests/Radio.Infrastructure.Tests/Platform/Input/EncoderSelectorTestDoubles.cs:108-109`), so it records neither — **swap the two branches and the whole suite stays green.** ⭐ Volume is the contrast, and it shows the gap is specific rather than general: `Ambient_VolumeTurn_ActsInPlace` and `VolumeTurnWhileMuted_AlsoAppliesTheDelta` each assert the volume went **up** after a `+1` turn, so a host-side negation *there* would go red. **(2) `ENC-17`'s harness cannot verify this class of fix at all** — and its own row already says why, in one clause that had no worked example until now: *"it does not exercise the firmware."* The harness writes movement values straight to `/dev/hidg0` (`tools/encoder-harness/virtual_encoder.py:564`), i.e. it produces the report a device emits *after* firmware has applied `reverse`. It can prove the decoder, the accumulator and the router; it can prove nothing about whether the flag reached the device or did anything once it got there. **`ENC-21` was verified on the real Pico, and only the real Pico could have verified it.** | **Criterion (a), in the purest form this tier has.** *"A guest's first interaction produces the wrong result"* — the first thing anyone touches on this cabinet is the volume knob, and turning it clockwise made the room quieter. The same reversal was on all four, so every knob's first interaction went the wrong way. It is **not** (b): nothing reported a false success and nothing was silent — the machine did exactly what it was told, in the wrong direction, which is a different and more findable failure than the ones (b) lists. ⚠ **Filed at P0 although it is already closed**, because the tier is what puts it beside the rest of the install-blocking encoder work, and because Caution 2 says a knob may yet be wrong the other way — that arrives at this same tier and should find this row when it does. | **0 — no code.** Roughly ten minutes of API calls plus the read-back. | `ENC-2`, `ENC-8`, `ENC-11` (all shipped) | **No — closed without a queue row**, like `SEC-1`. |

---

## 4. P1 — Blocks calling it finished

### 4.1 Encoders

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`ENC-9`** | ✅ **SHIPPED 2026-09-03 — the owner chose DELETE. Single-surface, local-only visualiser mode is now the accepted design.** Removed together, because removing any two of the three would leave the next reader concluding the feature still exists: `VisualizationModeService` (`CycleMode` / `ToggleEnabled` / `ModeChanged` / `VisualizationModeChangedEventArgs`), its DI registration, `AudioStateUpdateService`'s field + resolve + subscribe + unsubscribe + `OnVisualizationModeChanged` broadcast, **`AudioStateHubService`'s client-side `VisualizationModeChanged` event and its `_hubConnection.On` handler**, `VisualizationModeDto`, and `VisualizerPanel`'s hub injection + subscription + `HandleModeChangedRemotely` + dispose-time unsubscribe. ⚠ **The client-side listener is the part a narrower reading of this row would have missed** — deleting the producer and leaving a live `_hubConnection.On` registration is exactly how a dead feature keeps looking alive. ✅ **The capability survives and was verified on the appliance, not reasoned about:** the six-segment picker still switches modes, live on the box. ⚠ **What is genuinely lost is cross-client sync, and it must be stated precisely rather than as a regression:** *the picker never produced that sync either.* The knob was the only writer, so no user ever had it from the picker — what is gone is the **mechanism**, not a behaviour anyone was using. Rebuilding it means writing a **writer**, not re-adding a listener, which is the mistake `ENC-9a` made. Recorded in `design/FUTURE-WORK.md` §17. ⚠ **Two claims in the original text below are FALSE and are corrected here rather than preserved:** **(a)** the *"System Config dropdown unchanged"* second surface **does not exist** — that tab is FFT size / smoothing / peak-hold and has no mode control at all (established by `ENC-7`'s Builder); **(b)** the *"Fold in the real defect"* claim that `VisualizerPanel` **never subscribes** to `VisualizationModeChanged` was already stale when written — `ENC-9a` ([#491](https://github.com/mmackelprang/RTest/pull/491)) subscribed it in 2026-08; the subscription existed and was simply inert. _Original text follows._ ⭐ **PROMOTED 2026-09-03 — the premise changed, and it is now measured rather than anticipated.** `ENC-7` ([#541](https://github.com/mmackelprang/RTest/pull/541)) took index 2 for PRESETS, which removed `VisualizationModeService`'s **only writer**. Verified directly: `CycleMode` and `ToggleEnabled` have **zero call sites outside the service**, so `ModeChanged` can never fire, `AudioStateUpdateService`'s `VisualizationModeChanged` SignalR broadcast can never be sent, and **`ENC-9a` ([#491](https://github.com/mmackelprang/RTest/pull/491)) — which subscribed `VisualizerPanel` to exactly that broadcast — is now inert code.** ⚠ **The capability itself is NOT lost, and that distinction is the whole row:** `VisualizerPanel`'s six-segment picker still works, because it mutates a **private Web-side enum** and never touched the service. What died is the **cross-client sync** — change the mode on the kiosk and a phone browsing the same console will not follow, where before the knob it would have. `ENC-7`'s Builder also falsified the punch list's own reassurance here: the System Config tab this row cites as a second surface **has no mode dropdown at all**. So the honest position is one surface, no sync, and a dead service plus a dead broadcast plus an inert subscription behind it. **Nothing was deleted** — `ENC-7` correctly left the removal to this row rather than widening its own. This is no longer *"worth watching"*; it is a decision that has to be made: **re-point the picker at the service (restoring sync and reviving #491's work), or delete the service, the broadcast and the subscription together and accept single-surface local-only mode.** Choosing neither leaves three layers of dead code asserting a mechanism that does not run. Visualization loses its knob outright (to PRESETS); the capability moves to the touchscreen — tap the visualizer canvas to advance, long-press for the list, System Config dropdown unchanged. **Fold in the real defect:** `VisualizerPanel.razor` holds its own local `_currentMode` and **never subscribes to `VisualizationModeChanged`** (`AudioStateUpdateService.cs:969-978`), so any out-of-band change leaves the on-screen picker showing the wrong segment. | Removing a knob must not remove a capability. The subscription bug is independently real today. | 1 d (the subscription fix alone ≈30 min — §8) | — | ✅ **SHIPPED** — see the resolution at the head of this cell |
| **`ENC-14`** | **Diagnostics card + the `Calibrate a knob` flow.** ⚠ **D5 removed this card's original headline job — Rev 3 §7.9 re-argues it honestly and it survives smaller.** It was justified primarily as the tool that resolves *"does one detent report one count or four?"*; the firmware now answers that. Three merits remain: **(1) wiring sanity, which nothing else can see** — the invalid-transition rate is the only signal separating "this encoder is noisy" from "this encoder is fine" *before* it becomes an intermittent fault someone chases for a week; **(2) confirming D3's index-order guarantee after any hardware work** — turn the **topmost** knob, see which row moves *(Rev 3 said "leftmost"; the as-built panel is a vertical column)*; **(3) measuring detents per revolution**, the last mechanical unknown (D23). **Edges-per-detent changes job, not value:** it was a calibration input, it is now a **wiring health signal** — it should read exactly `4.00`, and anything else means edges are being dropped or invented, which is a hardware problem and **not** a reason to re-derive §5. **Invalid transitions are shown as a *rate*, never a total** (`per 1,000 edges`; <1 OK / 1–10 Marginal / >10 Faulty) — *"400 invalid transitions is fine after a year and alarming after a minute."* The card **says what the pattern means**: one encoder high → that encoder's wiring or shielding; all four high → a shared ground or supply problem, look at the Pico's power and the cable run. **`Calibrate a knob`** zeroes the counters, prompts *"Turn the VOLUME knob exactly one full revolution, slowly"*, and reports **detents per revolution**, edges-per-detent, and which encoder index moved. | Fifteen seconds, and it settles D23, **confirms D3 after any hardware work** — which matters because D3 is an owner-owned wiring guarantee that *must be re-established, not assumed, if the Pico is ever replaced or the harness re-terminated* — and works the same way on a replacement Pico years from now. ⚠ **Polls at 2 Hz and only while the card is open**, explicitly for the `UI-2` reason: a background diagnostic poll on this box is exactly the incidental load that correlates with audio distortion. This is the pattern `UI-2` should adopt, not an exception to it. | 1–2 d | `ENC-11` (O10) | No |
| **`ENC-18`** | **`ENC-0`'s presence state machine has no unit test and no seam**, and the `ENC-0` commit says so rather than glossing it: *"the presence state machine itself is welded to HID enumeration, so there is no seam to drive it from a test."* Two service-side behaviours are therefore unasserted: the **latched** absence announcement (so an uninstalled device does not log every couple of seconds forever) and the **15 s backoff cap** with its reset on a successful open. ⚠ **Corrected 2026-09-03 — an earlier draft of this row also claimed the `WasEverConnected` asymmetry was unasserted and that "only the `Enabled` default flip" was covered. Both were wrong.** The badge-vs-toast decision *is* pinned, at the consumer end, by `EncoderFaultAnnouncerTests.AbsentAtBoot_GetsNoToast_ButAbsentMidSessionDoes` (`:92-99`), and `RotaryEncoderOptionsTests.VendorAndProductId_MatchTheShippedDevice` (`:25`) covers the VID/PID as well as the default. **What has no seam is the service-side transition that decides `WasEverConnected` in the first place**, which is the part welded to HID enumeration. ⚠ **`ENC-17` does NOT close this.** `ENC-17` injects **input** through `/dev/uhid`; this is **enumeration**, which is a different seam — and `ENC-12` already worked around the presence half by unbinding the USB device, which tests *disconnect*, not the state machine. Fix is an enumeration abstraction the service takes by constructor. | `WasEverConnected` is what makes an absent-at-boot device silent and a mid-session disappearance noisy. Get it backwards and the cabinet either nags about knobs the owner removed, or says nothing when they fall off mid-song. | 0.5 d | `ENC-0` (shipped) | No |
| **`ENC-19`** | **The RotaryUsb firmware fix must survive any re-flash, and nothing tracks that.** RotaryUsb [#11](https://github.com/mmackelprang/RotaryUsb/pull/11) normalises report IDs arriving on the interrupt OUT endpoint. **Flashing an older build silently reinstates the defect: every host-to-device write is accepted and ignored**, so `ENC-11`'s config push does nothing, the read-back verify has no channel, and **nothing complains**. Today this lives only in `docs/HANDOFF-NEXT-SESSION.md` gotcha #6 and `design/research/ENC-11-firmware-drops-output-reports.md` — neither is a tracked row. **Deliverable: a post-flash check in `design/INTEGRATIONS.md` beside the encoder section**, plus a pointer from the research note. The check is one command: send `03 04 00` (read config); a working device answers with a **107-byte Input Report `0x02`**, a broken one answers with diagnostics only. | **The encoder arc's entire safety argument rests on this.** A factory-default Pico runs volume acceleration at ×50 — measured, not inferred — which is one fast flick from silence to full inside sealed furniture. `ENC-11` exists to overwrite that, and on an older firmware it silently does not. | 30 min | RotaryUsb #11 (flashed) | No |

### 4.2 Audio Reliability

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`AUD-2`** | **Confirm-or-close: is SDR gain/ducking silently dead?** An **unverified code-read inference** that `SDRRadioAudioSource.cs:908` mints `sdr-radio-<guid>` while `AudioManager` addresses `Radio-<guid>` (`AudioSourceBase.cs:28`). If it holds, **TTS and event audio never duck the radio** — silently: none of the gain/duck setters check membership, and `SetGainOffset` still logs *"Applied gain offset …"* on a key that matched nothing. **The first task is an investigation that may legitimately end in no code change.** | Ducking is a documented core pattern. If confirmed, the doorbell announcement plays *over* the radio at full level instead of under it. | 0.5 d confirm + 1 d fix | `TEST-1` | Yes 📋 |
| **`AUD-5`** | **A superseded Cast connection can persist its volume as the system master volume.** `SyncInitialVolumeAsync` is a *read* whose success path fires `CastVolumeChanged`, whose sole subscriber unconditionally sets **and persists** `AudioManager.MasterVolume` — with **no `_connectionGeneration` re-check between the status response and the event fire**. `IsInitialSync` exists on the event and is **never branched on**. ⚠ Widening `_lifecycleLock` will NOT close this (it would hold a lock across a network call and recreate the `AUD-3` hang); the correct shape is a **generation re-check immediately before the fire**. It **predates #468** — do not "fix" it by unpicking the Cast work. | The room's volume changes because a Chromecast nobody is using reported its own level. Unexplained volume changes read as a haunted machine. | 1 d | `TEST-1` | Yes 📋 |
| **`AUD-10`** | **Confirm-or-close: BT disconnect-reason surfacing.** Plan `design/plans/2026-03-11-bt-disconnect-reason.md` exists. **The first task is confirming BlueZ actually supplies a usable reason on this stack** — it may not, in which case this closes with no code. | "The phone disconnected and I don't know why" is a support call the owner makes to himself. Cheap if the data exists; not worth synthesising if it does not. | 0.5 d confirm | — | No |
| **`AUD-11`** | **BT codec observability.** Plan exists (2026-05-22), unqueued. | Diagnostic depth on the path a guest's phone uses. Not user-visible. | 1–2 d | — | No |
| **`AUD-12`** | **PipeWire event subscription in place of polling.** Plan `pw-event-subscription` exists (2026-05-22), unqueued. | Removes polling load on the box where incidental load correlates with distortion. | 2–3 d | — | No |

### 4.3 Logging & Distortion

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`LOG-5`** | **Runtime `LoggingLevelSwitch` + endpoint + DevTray toggle.** **No runtime log-level control exists anywhere today** — no switch, no endpoint, no config key. Changing any level requires a restart. The Logs page's level dropdown is a **read filter**, not an emission control. | **Highest diagnostic value per hour in this entire document.** It turns A/B testing the distortion hypothesis from a deploy-and-restart cycle into a **2-second operation**, on a box reachable only by SSH. Also the gate that makes `LOG-2` safe. | 1 d | — | No |
| **`LOG-6`** | **Move logging off `OnProcess` entirely, onto the existing 2 s watchdog timer.** `PipeWireNativeStream.cs:484-500` calls `LogInformation` **inside the audio callback**, every 10 s, **enabled in production**, allocating on the audio thread. | ⚠ **Hard prerequisite for `LOG-10` (O4).** Independently, it removes allocation from the audio callback on a box with a distortion problem. | 0.5 d | — | No |
| **`LOG-7`** | **Same treatment for `BufferedSoundGenerator.GenerateAudio`.** The best-behaved of the three — deliberate 1 s / 5 s / 10 s throttles — but during a bad BT stretch its 1 Hz underrun warning fires **forever**, it does `DateTime.UtcNow` per callback **plus a `lock` every 10 s from inside the render callback**, and it allocates 9- and 11-arg `params object[]` at call sites whose messages are discarded. | The `lock` inside the render callback is the same hazard shape `LOG-6` removes. A bad BT stretch is exactly when you least want extra work on the audio thread. | 0.5 d | — | No |
| **`LOG-4`** | **journald rate-limit + `SystemMaxUse`; consider `Storage=volatile`.** | Bounds the journald half of the mechanism at the daemon rather than per-service, and bounds disk growth inside a sealed cabinet. | 2–3 h | — | No |
| **`LOG-2`** | **`Serilog:MinimumLevel:Override` for `Radio.Infrastructure.Audio` and `...Platform.Bluetooth`** in `deploy/debian-x64/appsettings.Production.json`, **which currently has no Serilog section at all.** ⚠ `scripts/research/bt_drift_analyze.py` and `bt_stall_detect.py` **parse those exact Information lines** — gate behind `LOG-5` so the capability is toggled, not deleted (O7). | Second-largest volume reduction, on the two noisiest namespaces, on a box where log volume is an audio problem. | 1 h | `LOG-1`, `LOG-5` | No |
| **`LOG-11`** | **Drop or level-restrict the API's duplicate console sink.** | Every line is currently written twice. Free reduction. | 30 min | — | No |
| **`LOG-8`** | **Serilog rate-limiting filter.** The backstop for `SrcVariableResampler.cs:146-153` — an **unthrottled `LogWarning` inside per-buffer `Process()`**. If libsamplerate enters a persistently-failing state this becomes **~94–350 warnings/sec on the audio thread**, plus a P/Invoke marshal per call. | Defence in depth for a failure mode that would otherwise be indistinguishable from "the radio broke." Arguably P0; kept P1 only because the failing state has not been observed. | 0.5 d | — | No |

### 4.4 Phone & TTS

> **Finding D is one dropped work item, not two.** `docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md`
> (2026-08-01, "Draft for owner review") and **ADR-029** (`design/decisions/2026-08-03-gv-audio-through-engine.md`,
> status **"Proposed — Ready for Planner"**) both specify it. **Zero queue rows reference any of it.**
> Designed twice over, both marked ready for Planner, dropped between Designer and Planner; the next queue
> activity was 2026-08-18. That is a process failure, not a scoping gap, and it is the reason this document
> exists.

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`PHN-3`** | **The SMS speak button (Feature B) — fully specified at handoff `:297-430`, never queued.** Gutter placement, inbound-only, always visible, no seek or pause, 1000-char cap, URLs → "a link", emoji dropped, never speak the timestamp, strip the MMS `+1XXXXXXXXXX - ` prefix, **keep digit runs verbatim** ("verification codes are the single most valuable thing this feature reads"), one voice at a time. `MessageBubble.razor` is **56 lines and has no speak button, not even a disabled placeholder.** | A console you can ask to read you a text from across the room is the feature that makes the phone surface make sense *in furniture*. Spec-complete; nearly all its cost is `PHN-1`. | 2–3 d | `PHN-1` (O6) | ✅ **YES — corrected 2026-09-05.** Row created in [`BUILDER_QUEUE.md`](BUILDER_QUEUE.md) 📋 with a written plan ([`PHN-3-the-sms-speak-button.md`](../design/plans/PHN-3-the-sms-speak-button.md)); `O6` is met now that `PHN-1a`…`PHN-1f` and `PHN-2` have merged. ⚠ **§9's P1 counts do NOT move for this** — `PHN-3` was always *listed* and is still *open*; queueing is not shipping. |
| **`TTS-2`** | **Stop returning 200 for a swallowed announcement failure** (`NotificationsController.cs:53`). This is *why* the owner believes Google TTS works. | Criterion (b). `TTS-1` removes today's symptom; this removes the class. Without it the next misconfiguration is equally invisible. | 3–4 h | — | No |
| **`TTS-3`** | ✅ **CLOSED 2026-09-03 by `TTS-9` — the root was removed rather than the symptom patched.** eSpeak is gone from the codebase, so the preview path can no longer hardcode it: `SourcesController.cs:636` now passes the caller's engine through as `null` when unspecified and lets the configured default resolve, and the Razor page no longer offers eSpeak at all. **`ParseEngine`'s silent fallback — the part of this row that was a latent defect in its own right — now throws** naming the offending value and the valid set, instead of quietly selecting an engine that produces silence. ⚠ **The ducking half of this row was NOT addressed and is not closed by this change**; if it still matters it needs a row of its own. *Original text:* **The TTS preview path hardcodes eSpeak in two independent places** (`SourcesController.cs:636`, `SystemConfigPage.razor:1834`) **and does not duck.** **There is NO eSpeak fallback anywhere** — the real failure mode is silence, so *"it sounds like eSpeak"* means eSpeak is genuinely being selected, and the likely reason is that the owner tested via **Event Sources → TTS preview**. Also `ParseEngine` (`TTSFactory.cs:223-228`) silently falls back to ESpeak on any typo **with zero diagnostics**. | The preview is the surface the owner uses to judge whether TTS works. It currently answers a different question than the one being asked. | 3–4 h | — | No |
| **`TTS-6`** | **Ducking is cleared only for the *current* active source, then logs "volume restored"** (`AudioManager.cs:505-513`). Switch sources mid-announcement and **source A stays permanently attenuated** until restart. | Criteria (b) and (c). "The radio is quiet now and I don't know why," with no in-UI remedy, is a restart-the-appliance problem. | 3–4 h | — | No |
| **`TTS-5`** | **`AudioSourceBase.cs:95-96` assigns `State = Playing` AFTER `PlayCoreAsync` returns** — but `TTSEventSource.PlayCoreAsync` returns immediately while a background task may already have set `Error` / `Stopped`. **The terminal state is overwritten and the source reports Playing forever.** **Structurally identical to the `BluetoothAudioSource` `State = Ready` bug fixed in #469.** | This repo has now shipped this exact defect shape three times; two caused real bugs. Fixing the instance is cheap — the value is fixing it before it silently disables something else. | 0.5 d | `TEST-1` | No |
| **`TTS-4`** | **The ducking priority model is inert.** `GetPriority` / `GetActiveEventsByPriority` have **zero production callers**; `StartDuckingAsync` uses only the flat `DuckingPercentage=20`. **Setting priority 1–10 on the Notifications page has no audible effect whatsoever.** A second, higher-priority event arriving while ducked only logs. | Another control that lies. ✅ **D14 ANSWERED: WIRE IT.** The "remove the control" option is off the table and its quick win is withdrawn. Scope: give `GetPriority` / `GetActiveEventsByPriority` real callers, and make `StartDuckingAsync` honour priority instead of the flat `DuckingPercentage=20` — including the case it currently only logs, a higher-priority event arriving while already ducked. **Note the UAT tool actively confirms a capability that does not exist:** `tools/Radio.Tools.AudioUAT/Phases/Phase5/DuckingPriorityTests.cs:96-118` prints *"All priority assignments stored correctly"* and passes **without ever calling `SetPriority`** — fix that in the same PR or the wiring will look verified when it is not. | 1–2 d | — | No |
| **`TTS-7`** | ✅ **CLOSED 2026-09-03 by `TTS-9` — resolved as *remove*, not *install*.** This row explicitly offered the owner a choice: install `espeak-ng` so there is a working offline fallback, **or** remove ESpeak so it stops being selectable. **The owner chose removal on 2026-09-03**, because it costs about the same as remediating `SEC-4` and closes three rows instead of guarding one. `espeak-ng` is therefore never installed, `O11` is discharged, and the arbitrary-file-write primitive this row would have armed does not come into existence. ⚠ **Accepted trade-off, recorded so it is not rediscovered as a bug:** eSpeak was the only `IsOffline = true` engine, so **there is now no TTS at all without network.** The owner accepted this on the grounds that announcements are smart-home events — if the network is down the events are not arriving either. *Original text:* ⛔ **DO NOT SHIP THIS ROW BEFORE `SEC-4`. Installing `espeak-ng` is what makes an unauthenticated arbitrary-file-write reachable** — see `SEC-4` below. The binary's absence is currently the only thing preventing it, so *this row's completion is the exploit's precondition.* **`espeak-ng` is not installed on the box at all.** `/api/sources/events/tts/engines` reports ESpeak `isAvailable: false` (verified live 2026-08-19). Every path routed to ESpeak produces **nothing** — including the Event Sources → TTS preview button, which hardcodes it. | Compounds `TTS-3`: the preview button is broken for **two** independent reasons, and neither is visible. Decide deliberately — either install `espeak-ng` so there is a working offline fallback when the network or Google is down, **or** remove ESpeak from the engine list so it stops being selectable. A console that cannot speak when the WiFi drops is a real cabinet failure mode. | Install: 15 min. Remove from UI: 1–2 h | — | No |
| **`TTS-8`** | **The `en-US-News-K` fix is box-only.** `appsettings.json:178` still ships `en-US-Standard-A`; the config-store value overrides it. Durable on `radio` across restarts and deploys (`data/config/` is not wiped), but a **fresh install elsewhere gets the robotic Standard voice**, which is the voice most likely to be mistaken for eSpeak. | Low urgency for this cabinet, real for reproducibility — and it ties to `design/plans/IAC-PRISTINE-INSTALL-AUDIT.md`, which already found a pristine rebuild only 55–60% reproducible. | 15 min | — | No |
| **`GV-5`** | 🚫 **PARKED 2026-09-05 BY `D31` — see §6. Not closed, not shipped: refused, because the thing it unblocks is not wanted.** This row's own "why it matters" cell below is the argument that retires it — it says in as many words that GV-5 *"is the row that unblocks ever turning send on."* `D31` says send is never turned on, so the row's stated value is its whole value and it is now zero. **Nothing here is a defect on the read surface**, which is the test that separates it from `GV-9` and `GV-10`. *Original text, preserved because a reversal of `D31` restores this row verbatim:* **SMS send omits the required `ToNumber` → every send returns `400 invalid_number`.** Send has **never** been functional, only silent. Nine-code taxonomy (not eight); we also **never subscribe to `SmsSent`**, the only channel outbound messages arrive on, so GV-3's optimistic de-dupe is unreachable dead code. ⚠ **`GV-7` was the only row depending on GV-5's machinery, and parking this one does NOT strand it** — see `GV-7` in §5, which is re-scoped to drop the dependency rather than parked alongside. | ~~Ships as a **user-visible no-op** (`SendEnabled` stays `false`), so it does not block install — but it is the row that unblocks ever turning send on. ADR-028 + a refreshed plan already exist.~~ **The plan and ADR-028 are kept, not deleted** — they are the reconstruction path if `D31` is ever reversed, and they cost nothing sitting still. | ~~2–3 d~~ **0** | `GV-3` ✅ | 🚫 **Parked (§6)** — the queue row is 🚫, *never claim*, **not** ⛔; ⛔ means *another team must ship first*, which would promise this resumes. It does not. |
| **`PHN-4`** | ⭐ **NEW 2026-09-05 — Feature C is no longer a feature. Delete the composer and show the honest pill.** `D31` converts the canned-replies row from a 2–3 d feature into a **~0.5 d cleanup**. **What goes:** the `ComposeBar()` fragment (`PhoneTextsPanel.razor:259-279`) and both its call sites (`:101` conversation mode, `:138` new-recipient mode); the recipient field and its inline validation (`:118-129`); `_composingNew` (`:237`), `StartNew` (`:294-300`), `CancelNew` (`:302-306`), `_recipient`/`_recipientError` (`:235-236`) and `OnRecipientInput` (`:318-323`); the draft path — `_draft` (`:234`), `OnDraftInput` (`:312-316`), `CanSend` (`:244-247`), `SendDraftAsync` (`:325-394`), `RetrySend` (`:396-404`); the `New message` button (`:177`); and the CSS block at **`design-system.css:5916-5934`**. **What stays, and this is the part a fixer will get wrong:** **`.texts-compose` (`:5928-5933`) must SURVIVE** — it is the compose-bar container used by the degraded pill wrapper at `PhoneTextsPanel.razor:275`, which is the very thing this row keeps. **What lands instead:** the amber pill Designer already specified as tier **3a** — **`Replies are turned off.`** — as the unconditional resting state, closing UAT **F-3** (a disabled input with no stated reason) at the same time. ⭐ **It also takes the repo's only `data-keyboard` consumer to zero.** `PhoneTextsPanel.razor:122` (`inputmode="tel" data-keyboard="numeric"`) is the sole element in `src/` carrying the attribute; `wwwroot/js/virtual-keyboard.js:328` reads it. After this row the numeric-layout branch of the on-screen keyboard has **no caller** — a genuine simplification, and the fixer should decide deliberately whether the JS branch goes too rather than leaving it orphaned. ⚠ **Three of Feature C's six gate branches were never implementable and two of those never become so.** The handoff's C4 table (`:567-574`): branch **1** `!ThreadIsRepliable` needs `GvCounterparty`/`CanReply`, which is **zero matches in `src/` and `tests/`** and was `GV-5`'s to build; branch **3b** `_serverSendDark` needs the `409 send_disabled` code, and **`GvBridgeSendService` reads no error codes at all** — seven string literals, none of them one of ADR-028's nine, and only `429` is inspected, so a `409` falls to the generic throw at `:79`. Both branches die with the composer. ⚠ **Branch 2 is the one to NOT over-claim:** its symbol `ConversationFailedToLoad` does not exist, but the *state* already renders at `PhoneTextsPanel.razor:61` — it needed a rename, not machinery. Calling all three "unimplementable" would have been one claim too many. ⚠ **Two items on the handoff's own removal list DO NOT EXIST and must not be hunted for:** there is **no segment counter** anywhere in `src/Radio.Web/Components/`, and there is **no `＋ New` button** — that one is cited to the *design spec*, never built; the only new-message affordance in code is `New message` at `:177`. **A cleanup row whose list contains two phantoms will read as incomplete when it is done.** 🔵 **THE ID IS A PLANNER JUDGEMENT AND WANTS OWNER RATIFICATION — see the note under §9.** | Deleting a composer nobody can use, and replacing a permanently-disabled input with a sentence that explains itself. Criterion (b) in miniature: an input you cannot type into, with no reason given, is the machine failing to explain itself in front of someone. | 0.5 d | `D31` | Yes 📋 |
| **`TTS-10`** | **`TTSOptions.GenerationTimeoutSeconds` has never had a reader — TTS synthesis is unbounded.** Declared at `src/Radio.Core/Configuration/TTSOptions.cs:41` (default 30), mirrored in the Web DTO at `src/Radio.Web/Models/ApiModels.cs:802`, **owner-editable** at `SystemConfigPage.razor:695` (a `RadzenNumeric`, Min 5 / Max 120), and documented in `design/CONFIGURATION.md:978` and `design/SYSTEMCONFIGURATION.md:826`. **Zero readers anywhere in `src/Radio.Infrastructure`** — verified repo-wide at `5e571b88`, i.e. after `TTS-9` removed eSpeak; that PR did not give the key a reader either. So the owner can set a timeout, the UI accepts it, and a hung Google/Azure call still hangs forever. ⚠ **`PHN-1c` gives the key its first reader but does NOT close this row** — it bounds `EventPlaybackService`'s own acquisition and explicitly does not touch `TTSFactory`, so every other synthesis path stays unbounded. | **This is the settings-field-that-lies class the punch list promotes on sight** — `ENC-8a` deleted `TuningStepKHz` for exactly this. Either give it a reader in `TTSFactory` or delete the field and its two doc rows; a knob the owner can turn that does nothing is worse than no knob. | 2–3 h | — | No |
| **`TTS-11`** | **`AudioManager`'s ducking-started log line writes the utterance, and for a spoken text that utterance is the SMS body.** The line is `LogInformation("Ducking started: source={TriggerSource}, …", e.TriggeringSource?.Name ?? "unknown", …)` in `src/Radio.Infrastructure/Audio/Services/AudioManager.cs` — the call opens at `:499` and the offending argument is at `:501`; **quoted rather than trusted to the number**, since it is an argument line inside a method body. `TTSEventSource.Name` is not a category label: the constructor builds it as `$"TTS: {truncatedText}"` from the **first 47 characters of the text**, so every announcement logs its own content at Information. Found live during `PHN-1d`'s UAT and deliberately not fixed there — outside that row's file list, and it changes an operator-facing line. ⭐ **THE ARGUMENT FOR A ROW RATHER THAN A `FUTURE-WORK` LINE IS THAT THIS IS THE SECOND INDEPENDENT PATH TO ONE DEFECT.** `design/FUTURE-WORK.md` § *TTS seam* item 5 — filed 2026-09-04 during `PHN-1c`'s review, off the back of [#556](https://github.com/mmackelprang/RTest/pull/556) — has the first: `TTSFactory` logs a 50-character prefix and `TTSEventSource:92` logs **the whole string**, both at Information. **Fixing those three lines does not fix this one.** This one logs no text of its own; it logs a *display name* that happens to contain the text, from a different subsystem, reached on a different event. Two independent paths to one defect is precisely the case a single tracked row exists for — a fix to either leaves the appliance still writing message bodies to disk, and a `FUTURE-WORK` line under a *TTS seam* heading is the wrong place for anyone to find a defect in the **ducking** observer. ⚠ **A third line of the same class, so a fixer is not surprised by it:** `NotificationsController.cs:48-49` logs `request.Message` in full at Information (`_logger.LogInformation("Notification announce request: '{Message}' (priority {Priority})", request.Message, priority)`). Today that carries smart-home text rather than private content — the SMS body reaches TTS through `/api/audio/events`, not `/api/notifications/announce` — so it is a smaller exposure, but it is the same shape into the same sink. ⚠ **`LOG-11` made this durable rather than volatile, and quieter while doing it.** The API's console sink is Warning-and-above and under systemd the console *is* the journal, so these Information lines are **absent from `journalctl -u radio-api` and present in `/opt/radio-console/logs/radio-*.txt`** on a box that runs for weeks. ⚠ **And they are reachable from the console's own screen**, which is the fact the tier argument turns on: `GET /api/system/logs` reads `logs/radio-*.txt` (`SystemController.ReadLogFiles`) and **Settings → Logs renders it**, defaulting to `warning` with **`Info` one dropdown away** (`SystemConfigPage.razor:816`, `:2046`). `UI-3` proposes removing that tab, which would close the *display* and not the *defect*. **Fix:** log the source **type** and a character count, never the name — the shape `GvMediaCache.MaskFor` already establishes in this repo. ~30 min including a capture-logger test. | **P1, and the P0 tests are why it is not higher — walked rather than waved at.** **(a)** nothing a guest touches produces a wrong or unsafe result; the audio behaves correctly and the leak is a side effect. **(b)** is the closest and still does not fire. What the machine does in the room is exactly what it was asked to do — speak the message aloud to the person who asked for it; the defect is the **copy left on disk**, and putting that copy on the screen takes a person deliberately opening Settings → Logs and switching the level to `Info`. Real, which is why the sentence above is there — but not the machine embarrassing itself unprompted, which is what every one of (b)'s own examples is. **(c)** it is recoverable with an `rm`; the disk-fill hazard belongs to the `LOG` rows, not here. **(d)** it is not the verification substrate. **(e)** nothing becomes permanent at install. The **P1** test then fires cleanly: the honest sentence is *"it's done, except it keeps a plaintext copy of every text message it reads aloud."* ⚠ **Not P2, and the difference is a deadline rather than a severity.** P2 is *genuine work with no schedule pressure*; this has one — the Speech arm is unreachable on a stock box while `GvMedia:Enabled` is `false`, and **PR 6 of the phone arc flips it**. This should close before that PR, not after. | 0.5 h | — | No |

### 4.5 UI Surface

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`UI-2`** | ✅ **D11 ANSWERED, and the owner's direction is structural rather than a delete/keep vote.** His words: *"having Diagnostics available (perhaps on the config page menu) makes more sense than having this be at the top level."* **The call, made and recorded as decided: remove Metrics from top-level navigation and fold a trimmed diagnostics surface in under Settings (`UI-4`). Kill the 40-parallel-query fan-out as part of the move, not afterwards.** | `MetricsDashboardPage.razor` (668 LOC of ~1,870 across 6 files, ~43 tests) **polls 6 endpoints every 10 s and fans out up to 40 parallel history queries per refresh** (`:277-290`). `scripts/research/heavy_load_harness.sh:14-17` names **"sqlite3 busy-loop against the metrics DB"** as one of three deliberate load sources used **to reproduce the audio distortion**. **Having this page open is an instance of the problem under investigation.** One live break if deleted: `DevTray.razor:272` navigates to `/metrics`. | Whether the page survives is decision **D8**; the fan-out should stop either way. ⚠ The owner's stated premise — maintenance cost — **is not supported by git history** (last touched 2026-05-18, 15 commits lifetime). The load argument is the real one, and it is stronger. What would be lost: the only GUI for descriptor-registered `audio.buffer.*` / `audio.callback.*` / `api.request_duration_ms` bands. Note `bluetooth.capture_stall_detected_total` has **no descriptor and no threshold**, so the page was never a good surface for the known capture bug anyway. | Delete 0.5 d · fix in place 1 d · fold into `/diagnostics` 2–3 d | D8 | No |
| **`UI-3`** | **REMOVE the Settings → Logs tab.** ✅ **D12 answered — and it overrides this document's own recommendation.** This document argued *keep*, on the grounds that it is the only in-app legible error surface and that the SSH alternative aggravates the distortion. **The owner overruled it, and gave the reason:** *"if needed, it's easy to ssh into the box. The UX of looking through the logs in the UI is not good."* That is a legitimate call on a surface the owner is the only user of — recorded plainly rather than quietly absorbed, because a future session will otherwise find this document recommending the opposite. Scope: delete the tab (~136 LOC, 2 trivial assertions, zero churn) **and the now-orphaned viewer endpoint** (`LOG-3`). | Fewer surfaces, and it removes one of the two log allocation bombs outright. **The consequence to hold onto:** the box's only remaining non-SSH log route is the zip download, which makes `LOG-1` and `LOG-3`'s surviving half matter more than they did. | 2–3 h | Land with `LOG-3` | No |
| **`UX-1`** | **Skeleton shimmer amplitude.** ⚠ The shimmer is **not broken** and must not be re-filed as if it were — `Animation.currentTime` advances and 14.5% of pixels change between frames against a 0% static control. What is marginal is the *amplitude*: `--surface-raised #141416` → `--surface-overlay #1A1A1D` is **6/255** stop-to-stop, measured peak frame-to-frame change 3/255. It is the primitive behind **every** skeleton in the app (27 `<Skeleton>` call sites across 6 pages + 38 raw nodes). **Design-gated; may close as no-change.** | A design-token decision needing a Designer answer first. At 1920×720 across a room, a 6/255 delta may simply be invisible. | 0.5 d after a Designer call | Designer | Yes 📋 |

### 4.6 Test & Ops Hygiene

| ID | Item | Why it matters in the cabinet | Effort | Deps | Queued? |
|---|---|---|---|---|---|
| **`OPS-3`** | **`BindsTo=` for `radio-web` — make joint failure actually joint.** ⚠ **The one row in the queue exempt from the auto-merge policy** — it changes production service coupling and the owner reviews it personally. The reasoning is already written into `deploy/common/radio-web.service:12-22` by #467 and needs no re-deriving. | Correct coupling is what lets a wedged service recover itself instead of needing SSH into the cabinet — but a *wrong* coupling change is exactly the kind of thing that makes the box unreachable. Hence owner review. | 2–3 h | Owner review | Yes 📋 |
| **`HW-2`** | **The RotaryPhone hub contract is assumed, not verified** (`FUTURE-WORK` §11). | The phone surface's live updates rest on an unverified contract. Cheap to verify against the running service; expensive to discover wrong in front of people. | 0.5 d | — | No |
| **`TEST-5`** | **Four `SrcVariableResamplerTests` fail on every Windows dev machine, and no document says the local gate tolerates it.** `PipeWireNative.cs:344` declares the native library as `libsamplerate.so.0` — a Linux shared object — so all four facts throw `System.DllNotFoundException` from `src_new` via `SrcVariableResampler..ctor:61`. **Measured, not inferred:** `dotnet test --filter FullyQualifiedName~SrcVariableResamplerTests` on Windows at `01220d0c` gives **Failed: 4, Passed: 0**. They pass on Linux CI. The file has been this way since #404 (2026-05-22) and **three separate people hit it in one day**. **Deliverable is a sentence, not a fix:** state in `CLAUDE.md` § Build & Test that the honest local gate is **"adds no new failures"**, not **"zero failures"**, and name this class as the known Windows baseline. Skipping them behind an OS guard is the *optional* second half. | Every fresh session re-derives this and some conclude the tree is broken. Worse, a real regression hides in a 4-failure baseline nobody has written down. | 30 min | — | No |
| **`TEST-6`** | **Record the warning baseline together with the command that produces it — because the same tree honestly reports two different numbers, and that is the actual defect.** ✅ **The figure already in the tree is CORRECT.** Measured 2026-09-03 at `5e571b88`: `dotnet build RadioConsole.sln --configuration Release --no-incremental` → **`Build succeeded. 53 Warning(s)`, all `IDE0011`, across exactly 15 files** — which confirms `WORK-LOG.md:51`, the queue banner and the two 2026-05-22 plans rather than contradicting them. ⚠ **But drop `--no-incremental` and the same commit reports `Build succeeded. 30 Warning(s)` across 13 files** — measured the same day, in a clean worktree, by this row's own author, who briefly filed the 30 as a correction to the 53 before catching it. **That is the whole content of this row:** the number is meaningless without the command, MSBuild skips up-to-date projects and does not re-emit their analyzer warnings, and the 15 files span multi-targeted projects whose warnings are counted once per TFM in the raw log (78 raw occurrences) but deduplicated to 53 in the summary. ⚠ **`CLAUDE.md`'s "warnings as errors in Release builds" is NOT contradicted** — `Directory.Build.props:6-7` carries `<WarningsNotAsErrors>$(WarningsNotAsErrors);IDE0011;IDE0161</WarningsNotAsErrors>` under the comment *"IDE style rules: enforced as warnings, not errors (pre-existing violations in older code)"*, and `TreatWarningsAsErrors` is genuinely `true` (`:3`) for every other class. **Deliverable: one canonical sentence — "a full `--no-incremental` Release build produces exactly 53 pre-existing `IDE0011` warnings across 15 files; they are deliberately exempt; any warning of any other class still fails the build."** Fixing the 53 is optional and separate. | "0 warnings expected" appears in project memory and has never been true, so every session either re-measures or quietly stops trusting the gate — and a session that re-measures *incrementally* gets a different number and may "correct" the record wrongly, which is precisely what happened while filing this row. | 20 min to record | — | No |
| **`TEST-7`** | **`NowPlayingPanel`'s two hardcoded debounce timers need a `TimeProvider` seam — promoted from a queue note on that note's own instruction** (§ Documented fast-follows: *"Planner: this is a row, not a note."*). Three tests in `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelVolumeDebounceTests.cs` — `VolumeDrag_PersistsThePreferenceOnce` (`:129`), `VolumeDrag_PersistsTheFinalValue` (`:168`) and `SeparatedVolumeChanges_EachPersist` (`:192`) — race `await Task.Delay(1500)` against the panel's own 300 ms `System.Threading.Timer` (`NowPlayingPanel.razor:1054-1078`) with **no rendezvous**. **Be precise about which assertion is at risk:** `Assert.Equal(88d, pending)` is *safe* (`_pendingVolumePreference` is assigned synchronously); it is the **write-count** assertion that fails — `Assert.Equal(1, …)` at `:141` and `:184`, and `Assert.Equal(2, …)` at `:202` — and it fails in **both** directions — **undershoot to 0** if the callback plus its *two* HTTP hops (`ConfigurationApiService.cs:83-97` GETs before it POSTs) miss the 1500 ms, and **overshoot to 2** if a stall inserts >300 ms between the un-slept setup invokes. The panel carries a **second** hardcoded timer with the same exposure (`_gainDebounceTimer`, 200 ms, `:891-909`) — pull both into the seam. ⚠ **A fake clock alone is not sufficient**: the callback is `async void` over two awaited hops, so the fix also needs a completion rendezvous. Sibling `VolumeDrag_StillAppliesEveryTickToTheAudioEngine` (`:150`) is genuinely safe. | This is `TEST-4`'s exact defect surviving in production code rather than test code. The house idiom already exists in `Radio.Web` (`EncoderHudService.cs:32,45-48`) and `FakeTimeProvider` is already used in this same test project. | 0.5 d | — | Yes 📋 |
| **`TEST-8`** | **`NotificationsController`'s `Priority ?? 8` is pinned by no test, and it is the coupling that decides whether `PHN-1d` preempts at all.** `src/Radio.API/Controllers/NotificationsController.cs:46` reads `var priority = Math.Clamp(request.Priority ?? 8, 1, 10);`. That literal `8` is where **every doorbell's priority comes from**, and [#558](https://github.com/mmackelprang/RTest/pull/558) made `GvMedia:PreemptAtPriority` — also `8` — load-bearing against it. **Change the literal to `?? 5` and attended playback silently stops being preempted, with nothing red anywhere.** `NotificationsControllerTests` has exactly two tests (`tests/Radio.API.Tests/Controllers/NotificationsControllerTests.cs`); both post `Priority = 5` **explicitly** and both assert only an HTTP status, and the empty-message one returns `BadRequest` before the line is reached — so **the `?? 8` branch is executed by zero tests in this repository.** ⭐ **This row exists because `PHN-1d` falsified its own headline argument and the conclusion survived on a different mechanism.** That PR justified *"`PreemptAtPriority` is safe to lower, a trap to raise"* by `GetPriority`'s category-default fallback — which turned out to be unreachable, because all four `StartDuckingAsync` call sites call `SetPriority` first. The reason moved to this literal, and **a defaulting rule that no test states is a defaulting rule the next refactor is free to change.** **Fix:** a characterization test that posts an announcement with **no `Priority` field at all** and asserts the value reaching `IAnnouncementService.AnnounceAsync` is `8`. Most of the work is the seam rather than the assertion — the current fixture resolves the real announcement service, so this needs a capturing double. ⚠ **Assert the number, not the constant.** Reading `GvMediaOptions.PreemptAtPriority` in the assertion lets both sides move together and proves nothing — the same defect shape as the `TTSParameters` fixture whose `1.0f` matched the type's own initializer and would have passed against the bug it existed to catch. | **P1, and the fit is worth stating honestly rather than asserting.** Of the P0 tests, **(d)** — *"the substrate every other item's verification rests on"* — is the only near miss, and it misses: (d) is about a suite or a deploy that **lies**, and this suite does not lie, it is merely silent on one line. **(a)** and **(b)** describe nothing a guest meets today, because the defect is latent rather than present; **(c)** and **(e)** have no bearing. For P1, *"a real defect a user will hit"* is also the weak part of the fit and should be said out loud: **nobody hits an untested default.** What they hit is the regression it fails to catch — and that regression is *two voices at full level*, which is criterion (b) if it ever lands. The honest *"it's done, except…"* is *"except the one number that makes priority work is not written down anywhere a machine checks."* | 1–2 h | — | No |

### 4.7 Cross-Repo (RotaryPhone owns — gates what a guest sees)

> **None of these are claimable in this repo.** They route via the boundary-doc protocol into
> `D:\prj\RotaryPhone\docs\prompts\`. They are P1 here because they gate whether the phone surface shows
> anything true, and one of them feeds the audio-distortion problem.

| ID | Item | Why it matters in the cabinet | Queue ref |
|---|---|---|---|
| **`XR-1`** | ✅ **RESOLVED BY D17 — the contradiction is settled and there is no cross-repo defect left here.** The owner: *"I can see voicemail and text messages in the Radio Console UI today. I can listen to the voicemail and read the texts on the screen."* **GV read works.** The queue's § Cross-repo item **#3 is STALE** — it still says read has never worked, which the 2026-07-31 banner (upstream fix `627b928`, 20 SMS threads, 20 voicemails with real transcripts) already contradicted. **What remains is a 15-minute record correction, not engineering** (§8). | **The consequence is the significant part:** with read confirmed working, `PHN-2` — voicemail through a browser `<audio>` element, bypassing mute, master volume, balance, ducking and Cast routing — is **what the cabinet does today**, not a latent risk. That is why `PHN-1`/`PHN-2` moved to P0 (§3.5). | #3 — correct, do not file |
| **`XR-2`** | **Thread ids containing `/` are never decoded.** We send `Uri.EscapeDataString`; Kestrel deliberately leaves `%2F` encoded in the path; their exact string compare fails → **HTTP 200 with `messages: []`**. **Every group/MMS conversation is permanently unreadable.** Reproduced deterministically in a healthy window. **Not fixable client-side** — both escaping workarounds were tested and fail. | A guest's group text is silently invisible. Framing correction worth carrying: the predicate is *"the thread id contains `/`"*, **not** "the thread is MMS". | #5 |
| **`XR-3`** | **PSIDTS staleness → a deterministic ~9-minute auth blackout every 20 minutes.** Their PSIDTS is good ~11 min; their CDP refresh fires every ~20; no reactive refresh on 401. Their `/api/gvbridge/status` reports `available:true, degraded:false` **during** a blackout, so our reconnect banner never fires in the exact window it exists for. | ~45% of the time the phone surface is dead and says it is healthy. It also sets the cache requirement in `PHN-1`. | #6 |
| **`XR-4`** | **`CdpCookieExtractor` log spam every ~20 min with a ~20-line stack trace.** Non-fatal, but **journald churn on this box correlates with audio distortion** — a performance issue wearing a log-noise costume. Concrete root cause to hand them: `GVBridgeConfig.ChromeCdpPort` defaults to **9224** and nothing listens there. | The cross-repo half of the distortion story. Our `LOG-*` workstream cannot close it. | #1 |
| **`XR-5`** | **Bell-failure surfacing** (`FUTURE-WORK` §13): **`BellHealth.Failed` has no producer**, blocked on four RotaryPhone contract items — **and the request file has never been filed.** | The bell is the most physical thing on this machine after the knobs. A failure state with no producer means it fails silently. **The first action is filing the request, which has never happened.** | not filed |
| **`XR-6`** | ⭐ **NEW 2026-09-03 — the voicemail audio route answers `404` during a GV auth blackout, and we map that to "permanent".** `GvVoicemailController.GetAudio` resolves through `FindNodeAsync`, which calls `ListVoicemailsAsync` and **never checks the result's `Succeeded` flag** — unlike its sibling `GetList`, whose guard and `502` are at `:45-46` under the comment at `:43-44` saying why. A failed authenticated list returns an **empty** item set, so `FirstOrDefault` yields null and the route answers **`404 "has no recording"`** (`:64-65`). Verified by reading their source directly (`D:/prj/RotaryPhone/src/RotaryPhoneController.GVBridge/Api/GvVoicemailController.cs`), per the queue's own "trust their source over their docs" rule. **Ask: propagate `Succeeded` so `GetAudio` answers `502` the way `GetList` already does.** | **Wrong ~45% of the time**, because `XR-3`'s blackout is ~9 minutes in every 20. Our `GvMediaUnavailableException.IsPermanent` (`GvMediaClient`-side, `:73`) treats `NotFound` as *"retrying will not help"*, so the UI tells a guest a voicemail is permanently gone when it will play in a minute. **This is the second unfiled cross-repo request** — see `XR-5`. `PHN-1c` Task 10 narrows `IsPermanent` to `Disabled`-only on our side, which makes the lie stop; only their fix makes `MediaNotFound` mean what it says. | not filed — request text ready in `design/plans/PHN-1c-event-playback-service-and-route.md` §5 item 2 |

---

## 5. P2 — Post-GA

| ID | Item | Why P2 | Effort | Queued? |
|---|---|---|---|---|
| **`AUD-1`** | **Split `UseShazamForAllSources` into the two independent decisions it conflates** — *always fingerprint BT* (**necessary**: BlueZ 5.72 ships **no BIP/cover-art implementation**; 7 days of data show **0 AVRCP-sourced art vs 2,560 SongRec-sourced**) and *let SongRec overwrite AVRCP title/artist/album* (**not** necessary — it rewrites `Enter Sandman (Remastered)` → `Enter Sandman`). ⚠ **Do NOT "fix" it by setting the flag `false` — that kills BT album art entirely.** The wanted behaviour already exists at `BluetoothAudioSource.cs:893-905`, on the branch the flag never takes. | The visible symptom is a slightly wrong track title. Real, annoying, not a GA blocker. The danger note is what makes the row worth its size. | 1–2 d | Yes 📋 |
| **`AUD-4`** | **Unify the source-removal layers and rename `SoundFlowMasterMixer`** — it is a `List<IAudioSource>`, **not** a mixer, and its false log line *"Removed audio source … from mixer"* is **precisely why commit `03a6fea` landed one layer too high and silently did nothing for months.** ⚠ There are now **three** layers, not two: #468 rewrote `AudioSourceBase.StopAsync` (`:100-124`) and its new doc comment asserts `StopCoreAsync` is *"the only code that detaches the component."* Both claims cannot be true — reconcile before unifying. **Sweep `_activeComponents`, not `oldSource.Id`.** | A correctness-flavoured refactor with no current user-visible symptom — but the single best example in this repo of a wrong comment outliving the code it described. | 2–3 d | Yes 📋 (after `AUD-2`, O3) |
| **`LOG-9`** | **Per-thread CPU affinity instead of whole-process.** Moderate-to-high risk. **Do `LOG-1`, `LOG-3`, `LOG-6`, `LOG-7` first — they may prove this unnecessary.** Plan: `docs/plans/2026-05-22-audio-thread-isolation.md`. | The correct structural fix for `LOG-2m`, but the expensive one, and the cheap fixes target the same mechanism. | 1–2 d | No |
| **`LOG-10`** | **Enable `UseRealtimeCaptureThread`** — already plumbed, `LimitRTPRIO=99` already set, defaults false. ⚠ **HARD-BLOCKED on `LOG-6` (O4).** | Real latency benefit, but the ordering constraint is absolute and the prerequisite is not done. | 2–3 h after `LOG-6` | No |
| **`UI-1`** | **Delete `/diagnostic`** (`Components/Pages/Diagnostic.razor`, 97 LOC) — a Blazor render-mode smoke test: "Click Me", a click counter, one API ping. **Unlinked from all navigation, zero tests, squatting on the best route name in the app.** A **stronger deletion candidate than either page the owner asked about.** | Free, and it frees `/diagnostics` for the consolidation below. | 30 min | No |
| **`UI-5`** | **Two stale UI/doc mismatches in DevTray:** `DevTray.razor:253` calls `/api/system/logs/download` "a planned follow-up" when `SystemLogsController` implements it; `DevTray.razor:272` navigates to `/metrics` and is the one live break if `UI-2` deletes that page. | Trivial, but it is the same comment-accuracy failure class `CLAUDE.md` § Pre-Merge Review exists for. | 15 min | No |
| **`OPS-2`** | **Pin the six floating package versions.** ⚠ Hygiene, not a bug fix — a floating-version theory was investigated and **DISPROVED** as the cause of a CI failure. **Pin to what currently resolves; upgrade nothing.** | Build reproducibility. No user-facing consequence. | 2–3 h | Yes 📋 |
| **`TEST-2`** | **Confirm-or-close: the deferred-capture branch-dispatch coverage gap left by PR #469.** The test reaches the acquisition through an **internal seam**, never the `capture is …` branch dispatch, because constructing either type needs a native SoundFlow `AudioEngine`. **May legitimately close as "still infeasible."** Folded in: closing the `AUD-3` gap cost **two** new `internal` test seams — the second time this codebase has bought coverage with a seam, now tracked as a pattern rather than an incident. | Coverage, and it may be impossible. | 0.5 d confirm | Yes 📋 |
| **`GV-6`** | ✅ **UNAFFECTED BY `D31` — assessed and left exactly as it was, 2026-09-05.** **Distinguish `409 markread_disabled` from a genuine mark-read failure.** Rollout order matters: flip theirs first, confirm the 409 stops, then flip ours. ⚠ **This row is adjacent to the send path and is not part of it, which is the whole reason it needed assessing rather than assuming.** Mark-read is gated on a **different flag** — `RotaryPhone:Gv:MarkReadEnabled` (`appsettings.json:22`), read at `GvBridgeApiService.cs:142` and `:322` — and the file's own doc-comment at `:131` calls it *"in-tree consumer flag; **distinct**"* from send. **Marking a thread read is not replying to it**; it is the read surface recording that reading happened, which is precisely what `D31` says this console is for. **The `409` codes are also different codes** (`markread_disabled`, not `send_disabled`) on different routes, so nothing here depends on the taxonomy `GV-5` was going to build. ⛔ **Do not park this because `GV-5` was parked** — that inference is the error this pass was told to avoid, and it would have deleted a row whose subject `D31` actively endorses. | Diagnostic clarity on a feature dark by config on both sides. | 0.5 d | Yes 📋 |
| **`GV-7`** | 🔄 **RE-SCOPED BY `D31`, NOT PARKED — and re-scoping it is what keeps it shippable.** **Render non-dialable SMS senders** — numeric short codes and opaque 36-char sender IDs, **~a third of inbound SMS**. ⭐ **This row was always two rows wearing one id, and `D31` cuts cleanly between them.** The **display** half — what occupies the name line when there is no name and the identifier is 36 characters, the conversation header, contact resolution degrading gracefully, the bubble/meta treatment — is **pure read surface and survives untouched**. It is arguably the row `D31` makes most sense of: a third of what this console will ever show comes from senders it can never name, and rendering them legibly is the read experience. ~~Compose is gated **before** the POST so an un-repliable thread shows a disabled composer **with a reason**, rather than their `400 invalid_number` rendered as "That number doesn't look right."~~ **The gating half is DELETED, not deferred** — there is no POST to gate, and `PHN-4` removes the composer the gate would have disabled. ⭐ **The dependency this dissolves is the useful part.** The row said *"Reuse `GV-5`'s `GvCounterparty.Classify` (do not write a second classifier)"* — and with `GV-5` parked that instruction would have blocked this row behind a row that will never ship. **It does not, because the display half never needed the classifier.** Rendering a 36-char identifier legibly is a question about *string length and layout*, not about *repliability*; `ContactResolutionService` already answers the only question that matters here by failing to resolve. **So `GV-7` needs no `GvCounterparty`, no `CanReply`, and nothing from `GV-5` — its real dependency was and remains `GV-3`.** ⚠ **Two folded-in items go with the composer:** **F-3** (composer rendered-but-disabled with no stated reason) is now **`PHN-4`'s**, answered by the tier-3a pill; **G-6** (header duplicates the identifier when no name resolves) and **G-8** (MMS preview sender prefix) stay here. **G-4 measured a 36-char ID as safe at 1920×720** — no truncation needed. **G-3: zero opaque sender IDs are currently reachable from this surface**, so that case is *untested against real data, not proven absent.* | Real polish on a surface itself gated by `XR-1` / `XR-2` — **and now the read surface is the only surface, which raises this row's share of what a user sees.** Tier held at P2: it is legibility polish on threads that already render, not a defect. | ~~1–2 d~~ **~1 d** (the send-side gate leaves) | Yes 📋 |
| **`GV-9`** | ✅ **UNAFFECTED BY `D31` — assessed 2026-09-05 and every item survives.** **Texts-surface polish: overflow hardening + unread-row alignment + the missing `Error && <collection> == null` guard** at `PhoneTextsPanel.razor:162`. ⚠ **No existing test sets `Error` in thread-list mode**, so the behaviour is unasserted in *both* directions — the fix must **add** an assertion, not preserve one. **All three items are read-surface and none touches send:** `.texts-conv-number` overflow is a CSS property on a *displayed* identifier; the 20px unread-row jump is a *list* alignment defect made visible by `GV-4`'s mark-read, which `D31` does not touch; and the missing guard is on the **thread-list** branch, a different collection from the conversation-mode branch the send path interacts with. ⚠ **One real interaction with `PHN-4`, and it is an ordering note rather than a re-tier.** `PhoneTextsPanel.razor:175-177` puts a send-gated `New message` button **inside the "No conversations yet" empty state** — the same empty-state block `GV-9` hardens. `PHN-4` deletes that button. **Whichever ships second edits a block the first one moved**, so claim them in either order but not concurrently. | Cosmetic plus one latent guard. | 0.5 d | Yes 📋 |
| **`GV-10`** | ⬆ **UNAFFECTED IN CONTENT, STRENGTHENED IN ARGUMENT BY `D31` — and deliberately NOT promoted, 2026-09-05.** **Confirm-or-close: do conversation bubbles render the list snippet instead of the full message body?** The mechanism is **plausible but unproven**. **May close with no code.** ⭐ **`D31` makes this the sharpest of the four.** If the console can only ever *read* texts, then rendering the whole message is not polish — it is the feature. A machine that shows you two-thirds of a text and no indication it has stopped is the read surface failing at the one job `D31` leaves it. **On the honest-sentence test that defines P1, *"it's done, except it may silently truncate the texts it shows you"* fires cleanly.** ⛔ **It is NOT promoted to P1, and the reason is discipline rather than doubt.** The row is **unproven** — one bubble ending in a literal `...`, with a plausible upstream mechanism (RotaryPhone derives per-thread messages by filtering the SMS folder list, whose entries carry snippets). **Promoting an unconfirmed row on a strengthened premise would be tiering a hypothesis**, which is the failure `AUD-6`'s parked row in §6 exists to record. ⬆ **It moves to P1 the moment the confirming `curl` shows the truncation is OURS** — and to a cross-repo note if it is theirs. **What changes today is priority within P2, not the tier:** this was *"lowest priority of the three UAT rows"*; under `D31` it is the **highest**, because the other two are polish and this one may be a content defect. **The confirmation is one command and should be run before anything else in the GV set.** | ~~Unknown until someone looks.~~ **Still unknown — but it is now the read surface's own correctness rather than a curiosity, so the looking is worth scheduling first.** | 0.5 d confirm | Yes 📋 |
| **`HW-1`** | **The real phone-ring WAV** at `media/sounds/phone-ring.wav` (`FUTURE-WORK` §11). ✅ **D16 answered — deprioritised.** The owner has a **physical rotary phone with a working ringer**, so the software ring is not the thing that announces a call in that room. | Dropped from P1 and from the quick-win list. It stays on the board only because a placeholder that logs a file-not-found is still untidy. | Owner, ~20 min | No |
| **`ENC-10`** | **A roadmap row for physical input.** Designer §12.2: *"There is no roadmap row for physical input at all. This arc needs one, and §7 makes it at least two PRs (protocol + config/diagnostics, then mapping + HUD)."* | Bookkeeping — but the encoder arc is the largest body of work in this document and it is currently invisible in `docs/ROADMAP.md`. | 30 min | No |
| **`AUD-11a`** | ⭐ **NEW 2026-09-02 — the capture-node probe forks `pw-cli` even when the event listener is healthy.** Found while confirming `AUD-9` had shipped. `LinuxBluetoothService.IsCaptureNodeAvailableAsync:1489` always shells out to `pw-cli list-objects` (`:1450`), and the 500 ms poll across a 5 s probe window means **~10 process forks per BT device connect**. Plan E's `PipeWireRegistryListener` already tracks node arrival from real registry events, but it is consulted only to skip the *rescan* loop (`:1607`) — never the probe, because it exposes `IsHealthy` / `NodeAppeared` / `NodeDisappeared` and **no "is node X present now" query**. Fix shape: have the listener keep a live set of present BT node addresses and let the probe consult it when `IsHealthy`, falling back to the scrape otherwise. | Self-inflicted load on a box where incidental load correlates with audible distortion — the same argument `AUD-9` itself was justified on. Small and well-bounded, with an obvious static test seam. | 0.5 d | No |
| **`SEC-4`** | ✅ **CLOSED 2026-09-03 by `TTS-9` — by deleting the vulnerable path, not by sanitising it.** The recorded fix below was *"allow-list `VoiceId` against the engine's known voices"*; the owner chose instead to **remove eSpeak entirely**, which is comparable effort and also closes `TTS-7` and the root of `TTS-3`. `GenerateESpeakAsync` — the method containing the interpolation — no longer exists, along with `IsESpeakAvailable`, `GetESpeakVoicesAsync`, the `TTSEngine.ESpeak` enum member and `TTSOptions.ESpeakPath`. There is no remaining code path from `POST /api/sources/events/tts` to a subprocess command line. ⚠ **This closes the *instance*, not the *class*, and the very next engine had the same defect shape** — see **`SEC-5`**, an SSML injection through the same route's voice id on the Azure path, found by `TTS-9`'s own pre-merge pass. The route also remains **unauthenticated**; *"the TTS route is authenticated"* is not something this change earns the right to claim. *Original text, preserved:* ⭐ **NEW 2026-09-03, found while planning `PHN-1c`. Unauthenticated argument injection into a subprocess.** `TTSFactory.GenerateESpeakAsync` builds its command line by interpolation — `src/Radio.Infrastructure/Audio/Services/TTSFactory.cs:323`, `var arguments = $"-v {voice} -s {espeakSpeed} -p {espeakPitch} --stdout"` — and **`POST /api/sources/events/tts` reaches it unauthenticated**, with ESpeak as the default branch. There is **no shell**, so this is *argument* injection rather than command injection — but `espeak-ng` accepts `-w <path>`, which makes it an **arbitrary file write running as `mmack`**, the account that owns `/opt/radio-console`. ⚠ **It is NOT exploitable today, and that is precisely the trap:** `espeak-ng` is not installed on the appliance (verified 2026-09-03: `command -v espeak-ng` → not found; the engines endpoint reports ESpeak `isAvailable: false`). **The binary's absence is the only mitigation, and `TTS-7` is a queued row whose entire purpose is to install it.** Shipping `TTS-7` first arms this. **Fix: allow-list `VoiceId` against the engine's known voices and reject anything else — do not attempt to escape or sanitise the string.** `PHN-1c` allow-lists `VoiceId` on **its own** new route, which does not cover this live path; the planner filed the live path to `FUTURE-WORK` and explicitly said it wants a row of its own. This is that row. | ⚠ Latent, and latent in the most dangerous way — a **P1 row's completion is its precondition**, so the two must be ordered rather than merely both done. On a sealed cabinet the write target would be the deploy root itself. ✅ **Ordering constraint `O11` is DISCHARGED, not deleted** — both rows it ordered are closed by `TTS-9`, and it is kept as the record that shipping `TTS-7` first would have armed a remote file-write primitive on the appliance. | 0.5 d | No |
| **`SEC-2`** | ✅ **CLOSED 2026-09-02 by PR #523 — and the diagnosis recorded here was wrong.** *As recorded:* "the Azure TTS key slot appears to hold a Google key," inferred from `/api/secrets/tts` returning `GoogleAPIKey` and `AzureAPIKey` as **byte-identical masked values** with a Google-shaped prefix. **That inference was backwards.** Nobody pasted a Google key into the Azure slot. The two slots held the same string because **the Secrets form persisted the mask itself over the real secret**: `GET /api/secrets/{section}` returns masked values, the UI bound those masks into *editable* boxes, and `POST` wrote every field back verbatim with no check that a value was a placeholder — so saving *one* field overwrote *every other* secret in the section with its own mask, and both slots converged. So this row was not a data-entry slip needing ten minutes of owner re-entry; it was the visible symptom of a **P0 data-destroying defect in the write path**. Confirmed live on 2026-09-02: after the owner saved an unrelated field, `texttospeech.googleapis.com` began returning `API_KEY_INVALID` and TTS was dead in the cabinet, while `POST /api/notifications/announce` still returned `200 {"message":"Announcement played"}`. Re-entry restored it — 260 voices on a live fetch. **Why it hid for so long:** a mask is a *fixed point* of the masking function, so a read-back after the damage returns exactly what a read-back before it returned; neither the UI nor any read-back test could tell a preserved secret from a destroyed one. **PR #503 reached the same wrong conclusion from the same evidence** — identical masks are a symptom of this bug and must not be read as a mis-paste a third time. **Fixed by PR #523**: the API treats a submitted value equal to `MaskValue(stored)` as unchanged and does not write it (both the `first4...last4` form and the short `********` form that `AzureRegion` produces), blank now means "unchanged" rather than "delete", clearing moved to explicit routes, and the UI no longer puts a mask in an editable box at all. ⚠ **The Azure key and region are destroyed on the box and need owner re-entry** — **proven by decrypting the live store with the application's own key ring**, not inferred from the mask. `tts_azure_api_key` is **11 characters** and contains a literal `...`; `tts_azure_region` is **8 characters** of asterisks. Both are byte-for-byte `MaskValue` output rather than credentials, and both Azure endpoints return **401**. This is the **third occurrence today and the first with proof**. The API log for 2026-09-02 shows two saves under the old code, `13:39:04` and `13:43:07`, each reporting *"Section 'tts': stored 3 secrets"* — every field written every time. Azure fetched 196 voices at `13:40:31` and again at `13:42:17`, then returned `Unauthorized` from `13:48:33` onward. So the `13:43` save is the one that killed it. Read together with the Google failure, the two saves reconstruct the whole episode and show the defect's **ratchet**: at `13:39` the owner entered a real Azure key while the Google box held its loaded mask, which stored the real Azure key and destroyed Google; at `13:43` he re-entered Google while the Azure boxes held *their* loaded masks, which restored Google and destroyed Azure. **Through that form the two secrets could never both be valid at once.** The masks cannot show this — a mask is a fixed point, so a destroyed key and a real one mask identically — which is also the real reason both slots once read the same and why `SEC-2` and PR #503 both misread it. **After the fix, the same POST is a no-op**: at `14:09:34` the identical request logged three *"submitted as its own mask; left unchanged"* lines and *"stored 0 secret(s), left 3 unchanged"*, and Google still returned 260 voices afterwards. Re-entering Azure will now stick, and will no longer cost the Google key. | The recorded diagnosis pointed at the wrong layer: it filed an owner-side data-entry error against a P0 in the write path, and its "10 min (owner)" remediation would have been destroyed by the very next save. | Closed by PR #523 | — |
| **`SEC-3`** | ⭐ **NEW 2026-09-02 — the Secrets page's *Azure Region* field writes a secret nothing reads.** Found while verifying `SEC-2`. `SecretsController` maps the UI's `AzureRegion` property to the secret tag **`tts_azure_region`**, but `appsettings.json` binds only the two API keys through `${secret:…}` — `AzureRegion` is the literal string `"eastus"`. So `TTSFactory` reads the plain config value, and every region the owner types into the Secrets page is encrypted into a tag that no consumer ever resolves. The field is not merely redundant, it is **actively misleading**: it presents itself as the place the region is configured, reports "Stored", and changes nothing. It also explains a diagnostic oddity from `SEC-2` — a region clobbered to eight asterisks produced a clean **401** rather than the DNS failure `https://********.tts.speech.microsoft.com` would have caused, because the URL was never built from the clobbered value at all. **Deliberately not fixed inside the secrets PR**: the right resolution is a decision (bind the tag and drop the literal, or drop the field and let it be plain config), not a drive-by edit in a change about the write path. | A settings field that lies — the class this list promotes on sight, and the same comment/code-mismatch family as the three incidents named in `CLAUDE.md` § Pre-Merge Review, except expressed in a UI rather than a comment. | 1–2 h | No |
| **`SEC-5`** | ⭐ **NEW 2026-09-03, found by the pre-merge pass on `TTS-9` — SSML injection into the Azure synthesis request, and it is `SEC-4`'s surviving sibling.** `TTSFactory.BuildAzureSsml` interpolated the caller-supplied voice id straight into `<voice name='{voice}'>` while carefully escaping the *text* one line below it. The `name` attribute is single-quoted, so a voice id containing `'` closes the attribute and the element and appends attacker-chosen SSML. Reachable from the same **unauthenticated** `POST /api/sources/events/tts` route as `SEC-4`, with `engine=Azure`. ✅ **The breakout itself is CLOSED by `TTS-9`** — `voice` is now `SecurityElement.Escape`d like `text`, and `Ssml_EscapesMarkupInTheVoiceId` pins it. **That fix was falsified before being trusted:** reverting the one-line escape makes the test fail with `</voice><voice` present in the rendered document, so the breakout was demonstrated rather than inferred. ⚠ **What is left open, and why this is a row rather than a footnote:** escaping stops the *breakout*, it does **not** validate the voice. `SEC-4`'s recorded guidance was *"allow-list `VoiceId` against the engine's known voices and reject anything else — do not attempt to escape or sanitise the string"*, and that guidance still applies here: the voice cache (`ITTSVoiceRepository`) already holds the known-good set per engine, so an allow-list is cheap. **The decision is whether the live route allow-lists, and what it does when the cache is empty** (reject everything, or fall through to the engine) — that is a design call, not a drive-by edit inside a removal PR. Note the Google path is **already safe** by construction: it builds a `Dictionary` and `JsonSerializer.Serialize`s it, so the voice is encoded rather than interpolated. | Same class as `SEC-4`, in the code that **survives** it — which is the point. Removing eSpeak closed the instance, not the class, and this row is the evidence: the very next engine had the same defect shape. Lower severity than `SEC-4` (no subprocess, no file write — the sink is an HTTPS body sent to Microsoft, and the worst realistic outcome is a request shaped by an attacker using the owner's key), but it is the same failure to distinguish *data* from *structure*. | 0.5 d for the allow-list | No |
| **`TEST-3`** | ⚠ **CORRECTED 2026-09-01 (second pass) — this is NOT benign, and the first correction had the mechanism wrong.** It **failed CI** on PR #485 and blocked a merge. Two tests went red: `PlaylistsControllerTests.Load_WithNonExistentId_ReturnsNotFound` (expected `NotFound`, got **`InternalServerError`**) and `PlayHistoryControllerTests.GetBySource_WithValidSource_ReturnsOk` (*"Expected success, got InternalServerError"*). **They are assertion failures, not timeouts** — the `[48 s]` / `[30 s]` in the log is duration, which is what made the original record read them as timing out. **Mechanism, corrected:** CPU oversubscription is only the *trigger*. The *cause* is that `CustomWebApplicationFactory` does not isolate storage — `Database.RootPath` is the relative `"./data"` and there is no `appsettings.Testing.json` override — so **all 17 hosts open the same SQLite files concurrently**, and under contention lock/busy errors surface as unhandled 500s. That is why only the **first-executed test of each class** fails, and only under full-suite load. **Proof it is intermittent rather than caused by any change:** two runs of the same branch one minute apart, whose only delta was a `CLAUDE.md` edit, went pass then fail; re-running the identical failed commit went green. **Fix: isolate per-host storage** (a temp `RootPath` per factory) — capping parallelism is a mitigation of the trigger, not the cause. | ⬆ **Promoted P2 → P1.** It fails CI intermittently, so it silently re-creates the exact condition `TEST-1` was ranked first to remove: a suite whose result does not depend on the code. Every row after this one is verified by that suite. | 0.5–1 d | No |
| **`OPS-4`** | **CI runner migration** (`docs/ROADMAP.md` § "CI infrastructure — RTest appserver runner migration"). | Infrastructure. Related to `TEST-1` in spirit — the self-hosted runner is where the ambient-`localhost:5000` problem lives — but independent of it. | Unscoped | No |
| **`OPS-6`** | **Fingerprint the static assets so caching can be made cheap again.** `OPS-5` shipped a uniform `Cache-Control: no-cache` and `src/Radio.Web/Configuration/StaticAssetCaching.cs` is explicit that this is a decision, not an oversight: *"no URL this app links is content-hashed, so there is no class of asset that can safely be cached hard."* This is Blazor **Server**, so `_framework/blazor.web.js` sits at a fixed path rather than in a fingerprinted asset set, and `_content/Radzen.Blazor/*`, `Radio.Web.styles.css`, `css/*`, `js/*` and `fonts/*` are all stable names. The one near-exception proves it — `App.razor` links `js/virtual-keyboard.js?v=2`, a hand-maintained buster from #320 that only ever protected that one file. **Route: `MapStaticAssets` plus `@Assets[...]` at every reference.** | Every asset now costs a conditional request. On a kiosk on the same box plus occasional LAN browsers that is a real but small cost, deliberately traded for a correctness guarantee — so this is an optimisation, not a defect. ⚠ **Do not "optimise" it by raising `max-age` without fingerprinting first**; that is the stale-CSS bug of 2026-09-02 with a longer fuse. | 0.5–1 d | No |
| **`UI-6`** | ⭐ **NEW 2026-09-04, filed by `PHN-1f`'s planning pass — `AudioStateStore` notifies N subscribers and awaits one.** `AudioStateStore.NotifyAsync` (`src/Radio.Web/Services/AudioStateStore.cs:378-391`) does `await handler.Invoke()` on a multicast `Func<Task>`: `Delegate.Invoke` runs every handler but returns only the **last** one's Task, so the `try`/`catch` protects exactly one of N and the other N−1 exceptions reach no log at all. **Two more sites hand-roll the identical defect and are NOT fixed by fixing `NotifyAsync`** — `OnHubRadioStateChanged` (`:199-213`) and `OnHubSleepStateChanged` (`:214-220`), the second with **no `try`/`catch` at all**. ⭐ **The sharper half:** a subscriber that throws **synchronously**, before its first `await`, propagates straight out of `Invoke`, so **every handler registered after it never runs** — starvation, not a lost log line. **Fix:** iterate `GetInvocationList()`, await each, catch per subscriber. ⚠ ID assigned by the Builder on the plan's `UI-` suggestion; plan §6.2 left it for the owner. | ⚠ **Tiered against §1's own criteria, and the earlier *"P1, and PR 6 is the deadline"* framing was wrong on two counts.** **(b) is not met by the async half** — every subscriber still runs synchronously to its first `await`, and both live handlers reach `await InvokeAsync(…)` there, so the re-render **is** dispatched on every circuit; what is lost is a log line. **(c) is not met** — `ThrowUnobservedTaskExceptions` is set nowhere in the tree, so there is no crash path and nothing wedges. **And PR 6 is not the deadline:** `EventPlaybackChanged` has **zero** production subscribers today and PR 6 takes it to **one**, below the N ≥ 2 a multicast defect needs. What crosses the threshold is a **second circuit** — two open browsers already give `EncoderConnectionChanged` two subscribers today. ⚠ **Moves to P1 if** a store subscriber appears that can throw **before** its first `await`, or whose post-await work the caller depends on; neither exists. | 0.5 d | Yes 📋 |
| **`OPS-8`** | ⭐ **NEW 2026-09-05, promoted out of `FUTURE-WORK` §25 where it was buried under the `OPS-7` sweep that found it — `deploy-to-pi.sh` destroys BOTH Production overlays on every run.** `deploy/deploy-to-pi.sh:114-115` runs `sudo rsync -a --delete` into `$PI_PATH/api/` and `$PI_PATH/web/` with **no `--exclude`** on either, and the script carries no seed block; the twin at `Deploy-ToLinux.ps1:217` excludes `appsettings.Production.json` from both. ⚠ **The two halves fail differently.** **`api/` is deleted outright** — `src/Radio.API/` ships no `appsettings.Production.json`, so nothing replaces it. **`web/` is overwritten** by a tracked stub — `src/Radio.Web/appsettings.Production.json` is in the repo and `Microsoft.NET.Sdk.Web` auto-includes `appsettings*.json`, so it reaches the publish output carrying `"AuthKey": ""`. ⭐ **The deletion is louder; the silent replacement with an empty key is the one that would pass a casual look**, because the file is still present and still well-formed — the service just comes up with inter-service auth off. ⚠ **Not fixed by adding the two `--exclude` flags:** the exclude and the seed are a matched pair, so an exclude alone leaves a fresh Pi with no web overlay at all; the fix needs a seed block ported from the **post-`OPS-7`** per-destination shape, verified on an ARM target the finding Builder could not exercise. Full detail: `design/FUTURE-WORK.md` §25; queue row `OPS-8`. | ⚠ **Tiered against §1's own criteria, and the whole argument turns on which box the criteria mean.** §1 defines the tiers by *consequence in the cabinet*, and the cabinet is `radio` — x86_64, deployed by `Deploy-ToLinux.ps1`, with `Deploy-ToPi.ps1` passing `-TargetHost piradio` explicitly since `OPS-1`. `deploy-to-pi.sh` defaults `PI_HOST=piradio` / `PI_USER=pi` (`:22-23`) and is not a path the cabinet is deployed by. **(a)** and **(b)** fail — no guest interaction, nothing on screen. **(e)** fails — nothing gets drilled. **(c)** fails in the sense that bites: the person who lost the file is standing at the laptop they just deployed from. **(d) is the one with a real claim, and it still fails today** — *"if you cannot trust the deploy, you cannot claim anything else is fixed"* is the criterion `OPS-1` was P0 under, but it means *the* deploy the cabinet's claims rest on, and **no item on this list is verified on `piradio`**. ⚠ **It is P2 for the opposite reason `OPS-7` is P2:** `OPS-7` sits on the cabinet's own deploy path and needs a divergent box state to fire; this fires unconditionally and sits on a path the cabinet does not use. ⛔ **Do not read this as disagreeing with `FUTURE-WORK` §25's "Priority: high"** — that is a different scale (how reliably it destroys config, on which it outranks `OPS-7` outright), and both readings are correct on their own axis. ⬆ **Moves to P0 under (d) the moment either becomes true:** `piradio` is used to verify anything on this list, or a second appliance ships on the ARM path. **And pointing this script at the cabinet is not a plausible accident** — it hardcodes `sudo chown -R radio:radio` (`:116`) while the cabinet's tree is `mmack:mmack` and `radio-api` runs as `mmack` for PipeWire access (both verified on the box 2026-09-05, and the `radio` user does exist, so the chown would succeed silently), so a misdirected run breaks the box **louder** than the overlay loss it also causes. | 0.5–1 d | Yes 📋 |

---

## 6. Deliberately parked (P3 / Won't do)

Recorded with reasons so future sessions do not re-litigate them.

| Item | Decision | Reason |
|---|---|---|
| **`prefers-reduced-motion` turning the hold ring into a filling bar** (Designer §6.5 as originally written) | ⭐ **WON'T DO — owner-accepted deviation, D27, 2026-09-02. `ENC-4`'s shipped sweeping ring stands.** | **Do NOT "restore" this on a later consistency pass** — it is a *declared* deviation from an accessibility line item, not drift, and §6.5 has been amended to describe what actually ships. The ring is **the only on-screen indication that a 600 ms hold is arming standby**; freezing it puts the machine back in the state that row exists to fix — an input that acts with no visible evidence. ⚠ **The original spec was also never implementable as drafted:** the plan's literal `animation-name: none` freezes `--ring-turn` at `0turn`, producing an **empty** ring rather than a filling bar. Same class of declared deviation as the `MEMORY` → `PRESETS` rename, and recorded here for the same reason: **findable by whoever would otherwise revert it.** |
| ~~**"Delete the Settings → Logs tab"**~~ | ⚠ **SUPERSEDED — this document argued *keep* and the owner overruled it (D12).** | The *arguments* are kept for the record: the maintenance-cost premise was not supported by git history (near-zero churn), and it was the only in-app legible error surface on a box where the SSH alternative aggravates the distortion. **The owner's counter is also sound, and it is his surface to judge:** *"if needed, it's easy to ssh into the box. The UX of looking through the logs in the UI is not good."* **The tab is being removed** (`UI-3`); what survives is `LOG-3`'s zip-download half. |
| **"Delete the Metrics page for maintenance cost"** | **Reason rejected; a different reason stands.** | Last touched 2026-05-18, 15 commits lifetime — **it is not costing time.** The real argument is the 40-query fan-out (`UI-2`). Decide on **that** basis, not the original one. |
| **Removing the `Radio.Metrics` package** | **Won't do — and it is not the same decision.** | Deleting the *page* touches **zero lines** of `Radio.Metrics` or any collector. The package is consumed by ~30 files across API / Infrastructure / Fingerprinting plus 3 background services; collection, rollup and `metrics.db` all survive a page deletion. |
| **SignalR / visualization broadcast paths, BlueZ D-Bus handlers, `TappedOutputStream`, `VisualizationTapModifier`, `BufferedTapModifier`, log-to-SQLite, Logs-page polling** | **Won't investigate — already cleared.** | All explicitly checked and clean. The highest-frequency BlueZ signal (`Position`) logs nothing; the three tap classes have **zero** log calls; nothing writes logs to SQLite; the Logs page does not poll. **Do not spend time here.** |
| **`AUD-6`'s unstable device id as the cause of the 2026-08-10 soundbar silence** | **Settled — do not reopen.** | Raised as a hypothesis and **correctly falsified**. The app had opened the right device; the real cause was the Cast startup mute, shipped in #468. What is new is that the *fragility itself* has since been observed firing — a different claim from the one that was tested and rejected. |
| **Designer's `sbyte` argument against the factory acceleration tiers** (handoff §2.2 defect 5, §5.6) | **Struck.** | An artifact of the broken parser, not a device constraint — the device sends `int32`. **The volume-slam argument stands on its own and is the one to keep.** |
| **`docs/BUILDER_PROMPT.md` does not exist** — nor the "Handoff Protocol at the top of `BUILDER_QUEUE.md`" the Builder role points at | **Deliberately not a queue row** — but worth 20 minutes from the owner. | Already decided in the queue's § Dependency / ordering notes. Recorded here because every Builder dispatched at this project is being pointed at two documents that are not there. |
| **Playwright MCP being unusable in WSL** | **Not a queue row.** | Environment / tooling, not product. The fix is installing Chrome (needs root) or repointing the plugin at `--browser chromium` (the user's own config). Neither is a PR against this repo. |
| **The in-app Blazor favicon** (`App.razor:9`) | **Parked; the owner reversed on evidence.** | `--kiosk` hides the tab strip, address bar and title bar — that favicon is **never visible on the appliance**. |
| **A configurable maximum volume ceiling** (Designer §13 Q8) | **Recommend no** (owner may overrule — D9). | Trivial to add, easy to forget about, and then confusing when the knob stops moving. The four guards in `ENC-3` already bound the hazard. |
| **Pointer or "chicken-head" knobs** | **Won't do.** | These are endless encoders with no absolute position and no end stops. A pointer sitting at 3 o'clock while volume is at 20% is a knob that **lies about the machine's state every time you look at it.** |
| **Any knob gesture that powers off or reboots the box** | **Never.** | Long-press volume is *Standby*, reversible by a second long-press. A physical control that can terminate the machine, in furniture, reachable by anyone including a child, is not worth the convenience. Likewise: no knob may delete anything, and no destructive action may be reachable by turning alone. |
| **Auto-commit on dwell for the selector knobs** | **Considered and rejected** (Designer §6.6). | It converts every accidental brush of the knob into a real source change 1.2 s later, from across the room, with nobody touching anything. On this hardware that is both startling and expensive. |
| **Live-commit-per-detent for the source knob** | **Rejected.** | It would tear down and stand up an audio source at every detent, straight into `AUD-8` and `AUD-9` territory. |
| **The flash "safe baseline"** (Rev 2 §7.5, the old `ENC-13`) | ⭐ **WITHDRAWN BY DESIGNER in Rev 3 §7.7, on its own initiative — and the reasoning is worth keeping.** | Rev 2 wanted flash to hold a deliberately *duller* config than the operating one, so a stale flash could only ever make the knobs quieter. Two problems. **(1) It was solving a solved problem:** the boot window is already made safe by the **host clamp**, which lives in the app and bounds volume regardless of what the device believes — and the *operating* config is itself the safe one (T3 disabled on volume, ceiling ×3). The dangerous configuration was never the operating one; it was only ever the **factory** default. **(2) It would have made `Save to device` write something other than what the screen shows** — *"exactly the class of lie this project keeps shipping."* **Flash now holds the operating config, and staleness is surfaced (`differs from current design ⚠`) instead of prevented by dulling.** Strictly better: honest *and* diagnosable. |
| **Renaming the on-screen bank back to `MEMORY`** | **Do NOT "fix" this on a later consistency pass.** | `RadioControlPanel`'s bank moves from `MEMORY · n saved` to **`PRESETS · n saved`** to match the engraved knob (D10). It is a **deliberate, declared one-word deviation** from `HANDOFF-saved-station-display.md`, recorded in Rev 3's own header precisely so Polisher does not flag it as drift. A panel that says PRESETS over a screen that says MEMORY is worse than either name alone. |
| **The mode / shift button model** ("does the button change what turning the knob does?") | **Rejected outright in Rev 2 §4.1. The Action model applies to all four knobs.** | **You cannot see the mode.** No LEDs, no display, and the only feedback surface may be asleep, showing a clock, on another page, or out of the line of sight of the person with a hand on the knob. *A mode you cannot see is a mode you cannot trust* — and the way a person resolves "which mode is this in?" is **to turn it and find out**, which on a volume knob costs the room. Modes are also sticky across users and time: a knob left in its secondary mode at 11 p.m. behaves wrongly at 8 a.m. for someone who did not set it. **The hybrid is worse than either pure model** — if three knobs are press-to-act and one is press-to-shift, checking which means pressing it, converting a safe gesture into a test. ⚠ **Context is not mode, and the distinction is load-bearing:** TUNING's press does seek on radio and play/pause otherwise, selected by *what is playing* — something the user already knows and can see — not by a hidden toggle. |
| **Browse on knob 2** (Rev 1's own fallback) | **Withdrawn by Designer in Rev 2 §4.3, rather than defaulted to.** | **Dead or near-dead on half the sources:** Bluetooth is a phone-driven stream with no queue the console owns; Phono and USB are line inputs with no list at all — so on three of six entries the knob does nothing, **which is precisely the failure used to demote Visualization.** And browsing is a look-at-the-screen activity, which is the wrong ergonomics for a knob. *"[PRESETS] is what Browse was reaching for, with the browsing removed."* (Designer wrote MEMORY; the knob was renamed by D10.) |
| **Balance on knob 2** | **Rejected** (Rev 2 §4.3). | Tempting — `BalanceModifier` already exists, so it needs literally nothing new, and it is meaningful on every source. But it is a **set-once installation control**: two speakers, one cabinet, one room; nobody touches balance twice a year. It fails *reached for often* completely, and **its failure mode is silent** — a knob knocked off centre leaves the console quietly lopsided with no reason a user would ever look for. |
| **Output / Cast destination on knob 2** | **Rejected on safety** (Rev 2 §4.3). | **A mis-grab sends the audio to another room** — the most startling failure this hardware can produce. The console goes silent, the sound appears somewhere else, and the person holding the knob has no model for what happened or how to undo it. |
| **A third long-press anywhere on the panel** | **Deliberately not added** (Rev 2 §4.4). | Rev 1 put save-station on the TUNING long-press; Rev 2 moved it to PRESETS, where recall and store belong together. The result is that **the two continuous knobs have at most one long-press between them** and the two selector knobs have a clean press-to-commit. In track mode the TUNING button always resolves as play/pause on release regardless of hold duration — **no hidden gesture, no dead hold.** |
| **Cross-source favourites (PRESETS v2)** | **Out of scope for GA**, escalated to Architect (Rev 2 §12.1 item 2). | Making a memory slot point at a playlist, a Bluetooth device, or "the record player" needs a favourites data model that does not exist. `ENC-7` v1 ships against the shipped radio preset bank and does not block on it. The v1 boundary is **clearly messaged, not silent**: on a non-radio source the hold reports `Only radio stations can be saved`. |
| **Turning SMS send on at all** — and with it **`GV-5`**, the row that existed to make it work | ⭐ **WON'T DO — owner decision `D31`, 2026-09-05.** *"No — replies stay off."* | **The `/phone` texts surface is a read surface.** ⛔ **Do NOT re-file this as a quick win, a fast-follow, or a `FUTURE-WORK` seam** — it is refused on the decision, not on cost, in the same shape as `D25`'s stopgap and `D30`'s grace period. **Send was never functional, so nothing is being removed:** the flag is `false` on every box (`appsettings.json:21`, `appsettings.Production.json:6`) **and** `SendSmsRequest` (`ApiModels.cs:1186`) omits the server's required `ToNumber`, which appears nowhere in `src/` or `tests/` — a flag flip alone yields `400 invalid_number` on **every** send. **`GV-5`'s plan and ADR-028 are KEPT, not deleted.** They cost nothing sitting still and they are the reconstruction path if the owner ever reverses; deleting them would make a reversal expensive for no saving. ⚠ **What is NOT parked by this, because the adjacency misleads:** **mark-read** (`GV-6` — a distinct flag, `GvBridgeApiService.cs:131` says so in its own words), the **SMS speak button** (`PHN-3`), and the three read-surface rows `GV-7` / `GV-9` / `GV-10`. |
| **Feature C (canned replies) as a shipping feature** | ⭐ **WON'T DO as a feature — `D31`. It becomes a deletion instead (`PHN-4`).** | **A canned-reply chip set is a composer**, and `D31` removes composers. The handoff had already conceded the outcome at `:582` — *"3a is the state this feature actually ships in"* — so this is Designer's own reading confirmed, not overruled. **The amber `Replies are turned off.` pill is the finished state, not a placeholder**, which also closes UAT **F-3** (a disabled input with no stated reason) and answers the handoff's open **Q3**. ⛔ **Do not "restore" the composer on a later consistency pass** on the grounds that the handoff specifies five gate tiers — three of the six branches were never implementable (`CanReply` is zero matches in `src/`; `GvBridgeSendService` reads no error codes), and two of those can never become implementable now. |
| **Deleting `.texts-compose` along with the rest of the compose CSS** | ⚠ **MUST NOT — recorded here because it is the obvious next move and it breaks the surface.** | `design-system.css:5928-5933` is the compose-bar **container**, used at `PhoneTextsPanel.razor:262` (the composer, which goes) **and `:275` (the degraded pill wrapper, which stays)**. `PHN-4` deletes `5916-5934` *except* this block. ⚠ **A related trap in the same file:** an earlier scoping pass cited the dead CSS as **`design-system.css:5842-5859`**. **That range is live** — `.skeleton-feed-chip`, `.msg-list` and `.msg-bubble`, all referenced from `MessageBubble.razor:8`, `PhoneTextsPanel.razor:36`/`:53`/`:131`/`:154` and `PhoneMessagesPanel.razor:580`. **Deleting it would break every message bubble and both loading skeletons.** The correct block is `5916-5934`. |

---

## 7. Decisions — answered 2026-08-19

**The owner cleared almost the whole board in one pass.** This section used to be a list of questions; it is
now a record of answers, kept so nobody reopens them. Where an answer creates new work or changes a tier,
the affected item says so in place.

⚠ **AUDITED 2026-09-04 — this section was two answers short and was double-booking a third, which is a
claim in its own opening paragraph failing.** *"A record of answers"* is something a reader is entitled to
take literally, and could not. **`D6`** (tone is out in full, closed 2026-08-19) and **`D27`**
(`prefers-reduced-motion` keeps the shipped sweeping ring, closed 2026-09-02) were each cited as closed
**elsewhere in this very document** — `D6` in the §0 Rev 2 reconciliation banner, `D27` in the §0 banner and
again in §6's parked table — and **neither had an entry here at all.** A decision that exists only as a
citation is findable only by someone who already knows its number, which is the opposite of what a register
is for. **`D26` named two unrelated decisions** for two days; see the note under *Closed 2026-09-03*. All
three are fixed below. ⚠ **Nothing cited from an ADR was renumbered** — that constraint is what decided the
`D26` collision, and it is stated here because it is the rule to apply if this happens again.

### The two that gated the most

| ID | Decision | Answer | Consequence |
|---|---|---|---|
| **D1** | Do the knobs ship live at install, or inert? | ✅ **SHIP LIVE.** | **The single largest change to this document.** The conditional tiering collapses — **11 encoder items are now unconditionally P0**, ≈3–4 working weeks, and §1's "conditional tiers" subsection now says there are none. |
| **D2** | Approve the physical layout. | ✅ **`VOLUME · SOURCE · PRESETS · TUNING` is FINAL** — **and the order is the part that held.** ~~≈90 mm outer→inner, ≈70 mm inner pair; VOLUME/TUNING 40–45 mm knurled and deep-fluted, SOURCE/PRESETS 25–30 mm smooth~~ — **superseded 2026-09-02 by the owner's drawing**: one **vertical column** at x 25.4 mm, **uniform 29.63 mm pitch**, **all four knobs 15 mm**, groove still on PRESETS. | **Closed. The escutcheon is drawn.** ⚠ Note the engraving is **`PRESETS`, not `MEMORY`** (D10) — this document has been swept for the old word. Detail refinements landed in Designer Rev 3; **the as-built geometry landed in Rev 4**, which also records what the change cost: **size differentiation and grouping-by-spacing are both gone**, tactile identification now rests on **surface alone**, and **§10.1's mis-grab conclusion was re-derived because its stated premise was the 90 mm spacing.** The owner accepted the first two; the third was re-argued rather than re-worded. |

### Encoder decisions

| ID | Question | Answer |
|---|---|---|
| **D3** | Is encoder index order the same as physical panel order? *(asked as "left-to-right"; the as-built panel is a **vertical column**, so it reads **top-to-bottom**)* | ✅ **The owner guarantees it.** Recorded as an **owner-owned constraint**, not an app assumption — the wiring is his and he is standing behind it. `0 = VOLUME` **(top)**`, 1 = SOURCE, 2 = PRESETS, 3 = TUNING` **(bottom)**. The guarantee is unchanged; only the axis it is stated on rotated. |
| **D4** | Does RotaryUsb support a host→device *set position* command? | ✅ **No. Reset-position exists; set-position does not.** Confirms the existing research: `0x03` carries save-to-flash, factory reset, reset-positions-to-min, read-config-back and zero-counters — and nothing that writes an arbitrary position. **Accumulator semantics are now forced rather than preferred**, which removes the only argument that was ever made against them. |
| **D5** | Detents per revolution, and does one detent report one count or four? | ✅ **Handled by firmware.** The host sees **monotonic counts governed by the pushed config**. ⚠ **This retires what this document called the single riskiest assumption in the spec** — the possible 4× error running through every acceleration figure in Designer §5. It is gone, not mitigated: the divisor is not the host's problem. `ENC-14`'s `Calibrate a knob` loses its headline justification and keeps only its wiring-health one. |
| **D6** | Does GA ship a tone control — i.e. new audio DSP — and does knob 2 carry it? | ✅ **CLOSED 2026-08-19 — NO, and *tone is out in full*.** The owner ruled out new audio DSP for GA outright. **Knob 2's slot was re-argued rather than backfilled:** it is filled by **PRESETS** (the saved-station bank, on a knob), and **Designer withdrew its own Browse fallback rather than defaulting to it** — which is the part worth keeping, because a withdrawn fallback is the thing a later reader is most likely to mistake for an oversight and reinstate. **The Architect tone-DSP ADR dependency goes with it:** this decision removed an escalation, not merely a feature. ⚠ **FILED INTO THIS REGISTER 2026-09-04, six weeks after it was made.** It had been cited as CLOSED in this document's own §0 Rev 2 reconciliation banner since 2026-08-19 and asserted nowhere else — the exact shape §7 exists to prevent. |
| **D7** | Fold the radio bands into the SOURCE list? | ✅ **YES.** `ENC-5` absorbs it and is **re-estimated 2–3 d → 4–5 d** — the source knob now changes *bands*, so `RadioBandService` joins the commit path. |
| **D8** | Re-enable hardware screen blanking? | ✅ **YES — and ANY press or knob movement wakes.** Two consequences, both real: **(a)** it collides with Designer §8.1's *"sound controls act in place and do not wake"* rule — **Designer is resolving it in Rev 3; `ENC-6` is marked pending and must not be specced until it lands**. **(b)** the risk this document already carried — blanked panel plus an encoder USB drop leaving the screen unwakeable — **is now live rather than hypothetical**, and is promoted in §10. |
| **D22** | ⭐ **Literal D8, or Designer's narrowing?** Taken literally at the *audio* layer, *"any knob movement is a wake signal"* means a sleeve brushing a knob at 3 a.m. **resumes a console deliberately left in Standby** — the radio starts playing in a dark house. | ✅ **THE NARROWING IS APPROVED.** Owner: *"that D8 narrowing is fine, keep it."* **A turn from Standby lights the panel only; resuming audio requires a press (any button) or a screen tap.** The display still honours D8 in full — every input wakes the screen. The narrowing applies **only to restarting audio, only from Standby, only for turns.** Designer's framing: *"a turn is what a passing sleeve does; a press is what a person does."* Settled scope on `ENC-6`. |
| **D10** | Cabinet wording. | ✅ **`SOURCE` and `PRESETS`.** `PRESETS` over `MEMORY`: marginally clearer to a stranger, and a stranger is exactly who this knob is for. |
| **D21** | Confirm the flash policy. | ✅ **One-time flash approved** — **and the owner additionally wants device configuration plus an explicit Save action exposed in the Settings UI.** ⚠ **That partially reversed Rev 2 §7.6's "delete the editors" direction — and ✅ Rev 3 §7.7–§7.8 has since reconciled it.** The answer: **full read-only visibility of all 24 fields with sent-vs-read-back comparison, four `Reverse` toggles, and Save / Re-apply / Reset-counters actions.** ⭐ **Designer also withdrew its own "safe baseline" idea on the record, and the reasoning is worth keeping:** the baseline would have made Save write something *duller than the screen showed*, defending a boot window **the host clamp already defends**, at the cost of a button that lies. **Flash now holds exactly the operating config; staleness is *surfaced* (`differs from current design ⚠`) rather than prevented by dulling.** `ENC-13` is folded into `ENC-8`. |
| **NEW** | Encoder auto-detect. | ✅ **Owner: *"I'd like to auto-detect whether the encoders are available and have the system respond appropriately."*** Replaces `ENC-0`'s keep-it-behind-a-flag posture entirely — now presence detection plus graceful degradation across absent-at-boot / appears-mid-session / disappears-mid-session. UX detail pending Rev 3. |

### UI surface

| ID | Question | Answer |
|---|---|---|
| **D11** | Metrics page — delete, fix, or fold? | ✅ **The owner's direction was structural, not a delete/keep vote:** *"having Diagnostics available (perhaps on the config page menu) makes more sense than having this be at the top level."* **Call made and recorded as decided: remove Metrics from top-level nav; fold a trimmed diagnostics surface in under Settings; kill the 40-parallel-query fan-out as part of the move.** That fan-out is the load pattern implicated in the distortion — it does not survive the move in any form. |
| **D12** | Settings → Logs tab — keep or remove? | ✅ **REMOVE.** ⚠ **This overrides this document's own recommendation**, which argued keep. The owner's reason: *"if needed, it's easy to ssh into the box. The UX of looking through the logs in the UI is not good."* Recorded plainly rather than absorbed quietly, because a future session will otherwise find this document recommending the opposite. **`LOG-3` does not vanish with it** — see that item. |
| **D13** | Delete `/diagnostic`? | ✅ **YES.** And it **frees the route name for D11's consolidated diagnostics**, which is the tidiest available outcome. |

### Audio, phone and ops

| ID | Question | Answer |
|---|---|---|
| **D14** | Wire the ducking priority model, or remove the control? | ✅ **WIRE IT.** `TTS-4` becomes a 1–2 day job; the "remove the control" quick win is **withdrawn**. |
| **D15** | Resolve the DataProtection keyring question. | ✅ ***"We need to ensure the keys are retained."*** Decided, and it resolves to **P0** — keys that do not survive a restart break encrypted secrets on every restart inside a sealed cabinet. The branch `fix/dataprotection-keyring-persist-path` is **awaiting review**, so much of the work may already exist. |
| **D16** | Supply the real ring WAV? | ✅ **Deprioritised.** The owner has a **physical rotary phone with a working ringer** — the software ring is not what announces a call in that room. `HW-1` → P2, and out of the quick wins. |
| **D17** | What is the current truth about GV read? | ✅ ***"I can see voicemail and text messages in the Radio Console UI today. I can listen to the voicemail and read the texts on the screen."*** **GV read works.** The queue's § Cross-repo item **#3 is stale and should be corrected.** ⚠ **The consequence is the significant part:** `PHN-2` — voicemail through a browser `<audio>` element at full level, bypassing mute, master volume, balance, ducking and Cast routing — **is what the cabinet does today.** `PHN-1` and `PHN-2` are now P0 (§3.5). |
| **D19** | Does SMS send / composing matter for GA? | ✅ **Canned responses ARE wanted, explicitly without a keyboard:** *"A few simple/canned responses will suffice."* That is **ADR-029 Feature C**, already specced. **The phone arc is A + B + C, not A + B** — and no on-screen keyboard makes C cheaper, not harder. |

### Still open

**None.** ~~`D25` was the last one, answered 2026-09-02.~~ ~~`D28` is now the most recent, answered
2026-09-04.~~ ~~`D30` is now the most recent, answered 2026-09-04.~~ **`D31` is now the most recent,
answered 2026-09-05** — whether SMS *sending* is ever meant to be enabled; see *Closed 2026-09-05 by
the owner* below. `D30` (a page reload mid-voicemail kills the audio) is the entry above it. The board
is still clear; only the date moved.

⚠ **Two non-blocking questions were raised by ADR-029 Amendment 2 and are NOT owner questions** —
they live in that ADR's §14 as **Q11** (a *physical* stop control, which would satisfy §7.2 on every
surface at once with no browser attached) and **Q12** (`IsSleepScreenVisible` is a single global
bool, last-writer-wins, so with the kiosk on `/sleep` and a laptop on `/phone` it reads `true` although
a client does have a transport). Both are design questions for the sleep arc, both have a recommended
default recorded, and neither gates anything. They are named here so a reader scanning this section for
"what is outstanding" does not conclude the amendment left nothing behind.

### Closed 2026-09-02 by the owner

| ID | Question | Answer |
|---|---|---|
| **D25** | The `PHN-2` stopgap, or the full ADR-029 arc only? | ✅ **CLOSED — the full arc (option A).** No stopgap. The half-day JS-interop patch is not taken, and should not be revived as a "quick win" later: it fixes mute and level only, leaves ducking and Cast routing bypassed, and every line of it is thrown away when the `<audio>` element disappears. **This makes `PHN-1` the next major body of work** — the seam is a hard enabler for `PHN-2`, and ADR-029's argument is that voicemail-through-the-engine and speak-a-text are one mechanism rather than two features. D19 already added canned replies (Feature C) to the same arc, so the scope is **A + B + C**, not A + B. |
| **D27** | Should `prefers-reduced-motion` turn `ENC-4`'s hold ring into a filling bar, as Designer §6.5 originally specified? | ✅ **CLOSED — NO. `ENC-4`'s shipped sweeping ring stands**, as an owner-accepted deviation from the handoff rather than a defect anyone still owes a fix for. Designer Rev 5 amended §6.5 to describe what actually ships, so the handoff and the code now agree. ⛔ **Do NOT "restore" the filling bar on the grounds that the handoff specifies it** — it no longer does, and §6's parked table carries this as **WON'T DO** for precisely that reason. Also closes seam 3 in `design/FUTURE-WORK.md`. ⚠ **FILED INTO THIS REGISTER 2026-09-04.** Until then it lived in the §0 banner and in the §6 parked row (*"owner-accepted deviation, D27, 2026-09-02"*) and had **no §7 entry**, so `D27` was a number you could only resolve if you already knew the answer. |
| **D29** | Are the knob bodies straight-sided all the way down to the panel face, so §9.5's engraving clearance holds? | ✅ **CLOSED — YES, straight-sided, and the clearance holds.** Rev 4's shop-floor finding went to the owner and came back confirmed; the handoff §9.5 records it as a **checked-and-closed constraint rather than a silent non-issue**, and that framing is the whole value of the entry. The column is tight: a label's top edge clears the knob above it by about **0.8 mm** — VOLUME's label occupies y 40.03–43.99 mm while a 15 mm VOLUME knob ends at y 39.25 mm, and TUNING is the same to within a tenth — **and that clearance assumes 15 mm all the way down.** So the 0.8 mm figure is the reason *"any 15 mm knob"* is **not** a safe substitution later, which is a thing the answer alone would not have told anyone. ⚠ **RENUMBERED FROM `D26` ON 2026-09-04 — read the note under *Closed 2026-09-03* for why this one moved and the eSpeak decision did not.** |

### Closed 2026-09-01 by the owner

| ID | Question | Answer |
|---|---|---|
| **D23** | Detents per revolution (Rev 3 §5.2, §13 Q2). | ⚠ **RECONCILED AGAINST THE FIRMWARE 2026-09-02 — and the half of my earlier note that guessed was wrong.** The owner's answer holds: detent density and acceleration are firmware-owned, `movement` arrives in `step_size × tier_multiplier` units, and the host reimplements neither. `steps_per_detent = 4`, read live off the device. **But the accumulator hazard is NOT designed out.** `RotaryUsb/docs/INTEGRATION.md` §4: movement is *"a running total since power-on, not a per-report delta. You must difference it"*, and the vendor's own reference implementation re-baselines on every connect with the same reasoning §10 gives. So the re-baseline rule stands exactly as written, and `ENC-1` implements it. ✅ **CLOSED — the question was mis-framed, and the correction lands on `ENC-1`, not on the feel figures.** The owner: *"the encoder firmware already manages detents per revolution — you just need to manage configuring the device and reading direction and velocity from the device."* So detent density is **the device's business, already handled in firmware**. The host's job is (a) push configuration to the device and (b) read **direction and velocity** off it. ~~This is an input to `ENC-1`, and it must be reconciled against Rev 3 §5 before the decoder is written. If the device reports direction and velocity per report, that hazard is designed out rather than defended against.~~ **Superseded by the reconciliation above — the firmware sends a free-running accumulator, so the hazard is real and §10 stands.** Rev 3's description of the 37-byte report is confirmed exactly. The *"≈2.5 revolutions to cross the FM band"* figures are unaffected either way, and `ENC-14`'s Calibrate flow is no longer needed to answer this. |
| **D24** | Hand-edit the 24 config fields? (Rev 3 §7.8, §13 Q3) | ✅ **CLOSED — no.** Owner agrees with Designer and this document. Read-only visibility, four `Reverse` toggles, Save. No direct numeric editing. *"A field that can be set to ×50 will eventually be set to ×50."* A number that needs changing comes back through the handoff so the reasoning moves with it. |
| **D9** | A configurable maximum volume ceiling? (Rev 3 §13 Q4) | ✅ **CLOSED — no.** Owner agrees. The four guards in `ENC-3` already bound the hazard; a ceiling is easy to forget about and then be confused by. |

**Not decisions, just unfiled actions:** **D18** (file the bell-failure contract request to RotaryPhone —
never filed) and **D20** (write `docs/BUILDER_PROMPT.md` — every Builder is pointed at a document that has
never existed). Nobody is blocked on a ruling for either.

**Also still open, unrelated to the encoders:** `UX-1` needs a Designer answer on skeleton shimmer amplitude
and may close as no-change. ~~`TTS-7` needs a call on whether `espeak-ng` gets installed as an offline
fallback or removed from the engine list.~~ ✅ **ANSWERED 2026-09-03 — removed. See `D26` below.**

### Closed 2026-09-03 by the owner

| ID | Question | Answer |
|---|---|---|
| **D26** | `TTS-7`: install `espeak-ng` as an offline fallback, or remove eSpeak from the engine list? | ✅ **CLOSED — remove it, entirely.** Chosen **over** remediating `SEC-4` in place, on the grounds that removal is comparable effort and closes three rows (`SEC-4`, `TTS-7`, `TTS-3`) instead of guarding one. Shipped as queue row **`TTS-9`**. ⚠ **The trade-off was accepted with eyes open and must not be re-filed as a bug:** eSpeak was the only `IsOffline = true` engine, so **the console now has no TTS at all without network.** The owner's reasoning: announcements are smart-home events, so if the network is down the events are not arriving either. ⚠ **A second, narrower decision rides along:** the eSpeak defaults were **not** silently repointed at Google. Several of them were defaults *because* eSpeak needed no API key, and a cloud engine inherited as an implicit fallback fails with no key configured. They became **empty, with a loud and actionable failure** on use instead — explicit selection over a silent substitution. |

⚠ **`D26` WAS DOUBLE-BOOKED FOR TWO UNRELATED DECISIONS, AND THIS IS HOW IT RESOLVED (2026-09-04).**
Between 2026-09-02 and 2026-09-04 the number `D26` named **two** decisions: the straight-sided knobs
(Designer Rev 5 §9.5, 2026-09-02 — recorded in §0's amendment banner and in four places in
[`docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md`](design-handoffs/HANDOFF-rotary-encoder-mapping.md))
and the eSpeak removal (`TTS-9`, 2026-09-03 — the row immediately above). Two decisions, one number.
Neither was wrong to exist; only one of them could keep the number.

**The eSpeak decision KEEPS `D26`. The knobs decision is renumbered `D29`.** ⚠ **The tie-breaker is not
seniority and it is not which one is more important — it is where each number is cited *from*.**
`D26`-as-eSpeak is cited by number from **[ADR-029](../design/decisions/2026-08-03-gv-audio-through-engine.md)**
and from **`design/CONFIGURATION.md`**, and an ADR is the one class of document in this repo whose
citations must not be allowed to rot: it is a dated record of a decision, not a living page that gets
reconciled on the next pass. `D26`-as-knobs was cited only from documents this same commit can correct —
this file's §0 banner, and the encoder handoff — so moving it costs nothing that cannot be paid here.

**Neither decision loses its identity.** Every place that carried the old number now carries
**`D29` (recorded as `D26` until 2026-09-04)**, so a search for *either* number still lands on the right
decision, and a reader arriving from a document written before today does not silently land on the wrong
one. **Renumbering both to something clean was considered and rejected** — it would have invalidated a
live ADR citation to buy tidiness, which is the trade this repo does not make.

### Closed 2026-09-04 by the owner

| ID | Question | Answer |
|---|---|---|
| **D28** | ADR-029 `D5` rule 2 is symmetric. The *stop* direction is specified — a source starting at priority ≥ 8 stops an in-flight attended playback. **But what happens in the mirror direction: the user presses play while a ≥ 8 source is *already* talking?** Mix, refuse, or queue? | ✅ **CLOSED — QUEUE IT. It waits, then plays.** Three options went to the owner and he took the third. **Mix** — two voices, as today — **rejected**, because it is the exact outcome `D5` exists to prevent. **Refuse** — `Failed` with a `PreemptedByPriority` reason — **also rejected**, in the owner's own terms: *"press play, get an error, nothing happens"* is this punch list's tier (b) embarrassing shape, and shipping it deliberately would be filing the complaint on purpose. **Queue** — wait for the blocking source to finish, then play — **chosen as the only option where nothing is lost and nothing overlaps.** ⚠ **AND IT SHIPS IN PR 5, NOT PR 4 — on the owner's own reasoning, turned back on the queue.** He rejected refusing because *"nothing happens"* is embarrassing; **a queue whose waiting state nobody can see is that same sentence, for longer.** Visibility needs a broadcast and a chip, and those are **PR 5** (`/hubs/audio`, server-owned state) and **PR 6** (the topbar chip). Shipping the queue in PR 4 would deliver the half of the decision he disliked and defer the half that redeems it, so **the queue goes where its visibility goes.** ⚠ **Nothing reaches a user in between:** `GvMedia:Enabled` ships `false` until PR 6, so between PR 4 and PR 5 the mirror case is unreachable in production, and what it falls back to is today's mixing — **an un-fixed pre-existing wart, not a regression** (ADR §6.2 rule 3's neighbourhood). **What PR 4 owes instead is a characterization test** pinning today's mixing, in the discipline `PHN-1a` used, so PR 5's queue arrives as an edited assertion in someone's diff rather than as a silent behavioural shift. ⛔ **PR 4 must NOT add `EventPlaybackState.Waiting` "ready for PR 5"** — an enum member on the wire that no code can produce is a lie the size of a state, and PR 5 needs it appended at the *end* of the enum anyway. ⚠ **The awkward question this hands PR 5's planner, named rather than buried:** the queue's wake-up trigger is the `IsDucking: false` raise that PR 4's handler deliberately ignores, and `StopDuckingAsync` raises it only when the ducking set **empties** — so a ≥ 8 source ending while a sub-8 source continues produces **no wake at all**. Four further open questions are recorded in the plan's §5. **Source:** [`design/plans/PHN-1d-ducking-priority-load-bearing.md`](../design/plans/PHN-1d-ducking-priority-load-bearing.md) §0.4 **C-46**, which is the full text this entry summarises. ⚠ **AMENDED 2026-09-04, not rewritten — this entry says the queue *"ships in PR 5, not PR 4"*, and PR 5 is now two PRs.** The `PHN-1e` planning pass found the combined scope at 5–6 d and split it: **`PHN-1e`** takes server-owned state and the three stop conditions, **`PHN-1f`** takes this decision — so `D28` ships in **PR 5b, `PHN-1f`**, and the arc is eight PRs. The reasoning above is unchanged and still governs: the queue goes where its visibility goes, and `PHN-1f` still lands before PR 6. ⭐ **The "awkward question this hands PR 5's planner" is answered, and the answer went the other way.** That question — a ≥ 8 source ending while a sub-8 source continues produces **no wake at all** — is settled in `PHN-1e` plan §5: the starvation case is **rejected, not accepted**, because accepting it delivers `D28`'s rejected *refuse* option thirty seconds late. `PHN-1f` therefore adds `DuckingSourceTransition` and `TriggeringSourcePriority` to `DuckingStateChangedEventArgs`, which also closes both residuals PR 4 handed forward. **The other four open questions in that plan's §5 are settled there too** — three leans confirmed, one overturned, one dissolved. ⚠ **The plan proposed this as `D27`. That was wrong and the number was already taken** (`prefers-reduced-motion`, 2026-09-02) — caught and corrected on filing, which is the only reason this register does not now have a second double-booking in it. |
| **D30** | ADR-029 §7.3's last-circuit backstop was measured on the appliance and **stops attended playback on a plain browser refresh** — 1 → 0 → 1 on the only browser, a 49-second voicemail dead 7 seconds in. §7.3 was rewritten specifically to *survive* refreshes, so its own reasoning is falsified. **Is a refresh mid-voicemail supposed to keep the audio alive, or kill it?** | ✅ **CLOSED — KILL IT. The measured behaviour is the wanted behaviour.** The owner, verbatim: ***"If the page reloads mid-voicemail, the audio should fail. If the user wants to hear it they can replay."*** ⭐ **This is the rare correction that makes the work SMALLER, and it is worth being precise about what moved.** Nothing measured changed and nothing documented changed — **only the verdict, because the objective moved.** §7.3's goal was *"audio is playing and no client is watching"*, under which a reload is a **false positive**: the user is watching and is coming straight back. `D30` replaces that goal, and the identical firing becomes a **true positive**. ⛔ **`AttendedPlaybackCircuitHandler` therefore ships EXACTLY as `PHN-1e` built it — no grace period, no circuit-identity matching, no survival mechanism, and none to be proposed as a future row.** The Architect's own draft recommendation to *delete* §7.3's stop is **withdrawn**; so is the grace-period option keyed on `OnConnectionUp`/`Down`, which was viable and is now refused on the decision rather than on its merits. ⚠ **What DOES fall is §7.3's prose**, and it falls hard: its worked example (circuit B is "already open" when A closes — it is not, the close strictly precedes the open), its retention-window claim (the 10-minute window covers *unexpected* disconnects only; a graceful close disposes at once), and its self-description as *"the weakest of the three defences, worth having, not worth trusting"*. Under `D30` this rule is **promoted** — it is the mechanism that implements the decision in this box's resting single-kiosk configuration, and §7.1's 300 s cap is the backstop behind **it**. ⚠ **One divergence recorded rather than decided:** with a second browser open a kiosk reload is 2 → 1 → 2 and stops nothing, so the rule implements `D30` literally only when one browser is open. The only mechanism that would close that is the ownership model ADR-029 ⟨A1·4⟩ deleted and `D30`'s framing excludes again — there is no third option to put to the owner — and the divergence is benign, because a second client still holds the transport so the audio never becomes unattended. **If the owner reads `D30` literally — *every* reload stops, second browser or not — this is the sentence that reopens it, with the ownership model as its only answer.** ⭐ **The reusable half, which is why this entry is long.** The defect was caught **only** because [`PHN-1e`'s plan](../design/plans/PHN-1e-server-owned-state-and-the-queue.md) declared the circuit ordering *unverifiable in a test host* and pinned it to an on-box check with *"stop and re-plan"* attached to the false branch. **No test that could have been written for that row would have gone red** — nothing in a bUnit or xUnit host has a browser, an `unload` event or a real circuit. And note where the value actually was: U3's **prediction was wrong** (it guessed 1 → 2 → 1; the box answered 1 → 0 → 1). The value was in **declaring the assumption unverifiable and naming the observation that would settle it**, not in guessing right. **Source:** [ADR-029](../design/decisions/2026-08-03-gv-audio-through-engine.md) **§16 Amendment 2**, whose §16.3 enumerates all eight firing paths against this decision; `design/DECISION-LOG.md` carries the summary. ⚠ **This register entry was deliberately left unwritten by the ADR** — that file said in as many words that §7 is owned by another pass — and is filed here by `PHN-1e`'s Builder on merge. `D30` was verified free before use: `D28` is the wait-then-play queue, `D29` the knob geometry renumbered out of the `D26` collision the same day. |

### Closed 2026-09-05 by the owner

| ID | Question | Answer |
|---|---|---|
| **D31** | **Nothing in the repo records whether SMS *sending* is ever meant to be enabled.** `RotaryPhone:Gv:SendEnabled` has shipped `false` since GV-3, every plan says *"ships with `SendEnabled` still `false`"*, and no document says whether that is a staging step or a permanent state. Is send a feature that is waiting, or a feature that is not coming? | ✅ **CLOSED — NO. REPLIES STAY OFF.** The owner, asked directly, answered that sending is not meant to be enabled. **The `/phone` texts surface is a read surface**: voicemail and SMS are things this console *shows and speaks*, not things it *writes*. ⭐ **THE CONSEQUENCE IS LARGER THAN THE ANSWER, AND IT IS A SUBTRACTION.** This is the second decision in three days that makes the work smaller (`D30` was the first), and like `D30` it changes no measurement — it changes which of two goals the existing code is judged against. **Every send-path row was tiered on the premise that send would one day be turned on.** With that premise withdrawn, `GV-5` — a 2–3 d P1 whose entire content is the send contract — has no remaining value, and **Feature C (canned replies) stops being a feature and becomes a deletion.** See `PHN-4` in §4.4, the four GV dispositions in §5, and §6's **three** new parked rows. ⚠ **Send was ALREADY dead behind two independent gates, and the second one is the interesting half.** (1) The flag: `SendEnabled` is `false` in **both** `src/Radio.Web/appsettings.json:21` and `appsettings.Production.json:6`, so no box has it on. (2) **The contract: `SendSmsRequest` omits the server's required `ToNumber`** — `src/Radio.Web/Models/ApiModels.cs:1186` is `public record SendSmsRequest(string ThreadId, string Text)`, and `ToNumber` appears **nowhere** in `src/` or `tests/`. So flipping the flag alone would not have produced working send; it would have produced **100% failures**, `400 invalid_number` on every attempt. That is what `GV-5` existed to fix. **The decision does not break send — it ratifies a state send has always been in.** ⚠ **The handoff had already conceded this in writing and nobody read it as a decision.** [`HANDOFF-phone-console-audio-and-canned-replies.md:582`](design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md): *"Since `RotaryPhone:Gv:SendEnabled` is `false` today, **3a is the state this feature actually ships in.** It must be the best-looking of the five, not an afterthought."* Tier 3a is the `!SendEnabled` branch — the amber pill reading **`Replies are turned off.`** Designer designed the disabled state as the shipping state a month before the owner was asked, and escalated it as **Q3**. `D31` answers Q3 as well: the pill is not a placeholder for a feature arriving later, it is the finished state. ⛔ **DO NOT re-file "turn on SMS send" as a quick win, a fast-follow, or a `FUTURE-WORK` seam.** It is refused on the decision, not on its cost — the same shape as `D25`'s refusal of the `PHN-2` stopgap and `D30`'s refusal of the grace period. If the owner reverses, the reversal reopens `GV-5` from §6 and re-scopes `PHN-4`; nothing is deleted that could not be rebuilt from the ADR-028 plan, which is kept. ⚠ **What this decision does NOT touch, stated because the adjacency is misleading:** **mark-read is a different flag and a different feature** (`RotaryPhone:Gv:MarkReadEnabled`, and `GvBridgeApiService.cs:131`'s own comment calls it *"distinct"*), so `GV-6` survives untouched; and **`PHN-3`, the SMS *speak* button, is unaffected** — reading a text aloud is the read surface working as intended, and `D31` arguably strengthens it. **`D31` was verified free before use:** `D28` is the wait-then-play queue, `D29` the knob geometry renumbered out of the `D26` collision, `D30` the page-reload decision; `git grep` for `D31` and for `D32`…`D39` returns **zero** matches repo-wide. |

---

## 8. Quick wins — under an hour each, independently shippable

~~The owner should be able to knock several of these out in one sitting.~~ ✅ **ALL ELEVEN SHIPPED 2026-09-01** — PRs #488 - #495. Kept in full rather than deleted, because several carried conclusions worth not re-deriving (the `VolumeStepPercent` fate in #5, the two-transports trap in #4, and #6's deliberate refusal to renumber the encoder table before the hardware exists).

| # | Item | Effort | Why it is a win |
|---|---|---|---|
| ~~**1 ★**~~ | ~~**`TTS-1(i)` — set `tts:defaultVoice` in the config store.**~~ ✅ **DONE 2026-08-19 — set to `en-US-News-K`, verified working on the box.** | ~~5 min~~ | **Closed.** This also closed the owner's separate ducking complaint: ducking was never broken, it was never reached. See `TTS-1` for the full record and the two follow-ons it surfaced (`TTS-7` espeak-ng absent, `TTS-8` repo default still wrong). |
| **2 ★** | ✅ **#488.** Web never called `ReadFrom.Configuration`; both file sinks also gained size caps. Verified by falsification, not by reading the diff. **`LOG-1` — honour `ReadFrom.Configuration` in `src/Radio.Web/Program.cs:14`, set Information in production, add retention + size caps to both file sinks.** | **30 min** | **Largest single log-volume reduction available.** 65 MB/day measured, 106 Debug sites live, no retention cap at all on an appliance that runs for weeks inside a cabinet. |
| **3 ★** | ✅ **#493.** Built to handoff §6.7. Seeds initial state and does *not* optimistically clear — the broadcast confirms the unmute. UAT'd on a live stack. **`ENC-4a` — the persistent topbar `MUTED` chip.** | **45 min** | Independently valuable even if the whole encoder arc slips. Today, on `/queue`, `/metrics`, `/devices`, `/history` and `/phone` there is **no mute indication at all** — a muted console with no visible reason is indistinguishable from a broken one. |
| 4 | ✅ **#489.** **`TTS-1(ii)` — fix the hint text at `SystemConfigPage.razor:684`, which recommends `"en"`.** | 5 min | The UI is actively teaching the value that breaks Google TTS. Fixing #1 without this invites the same mistake again. |
| 5 | ✅ **#490.** `TuningStepKHz` deleted; **`VolumeStepPercent` deliberately kept** — it is a real device field awaiting relocation in `ENC-8`. **`ENC-8a` — delete `TuningStepKHz`, the field that is read by nothing.** ✅ **SETTLED by Rev 3 §7.8 — and the two fields have different fates.** `TuningStepKHz` is **deleted outright**: nothing reads it and nothing should, the tuner owns its own step, and D21 does not change that because *it is not device configuration, it is a field that never did anything.* ⚠ **`VolumeStepPercent` is RELOCATED, not deleted** — it is a genuine device field (VOLUME `step_size`), so it moves to the read-only configuration card in `ENC-8`. What goes away is the **duplicate editable numeric**, a second source of truth for a value the device also holds. **Do not delete it here.** | 20 min | `TuningStepKHz` is declared, set in appsettings, documented, **editable by the owner at `SystemConfigPage.razor:1535`, and read by nothing.** A settings field that does nothing is a lie the owner will discover by testing. `VolumeStepPercent` is worse than useless — it is an app-owned safety constant sitting in an editable box. Deleting both is the free half of `ENC-8`; the four `Reverse direction` toggles that replace them need `ENC-2`. |
| 6 | ✅ **#489.** Press actions corrected against the router. Encoder *order* deliberately left stale — see the row. **`ENC-8b` — correct the Encoder Mapping table at `SystemConfigPage.razor:1492-1493`.** | 20 min | It claims encoder 1 press = "Seek Next Station" and encoder 2 press = "Play/Pause"; the code does scan-toggle and source-commit. Hand-typed HTML — it will drift again, so the real fix is serving it from the router (`ENC-8`). |
| 7 | ✅ **#491.** Also had to type the event: parameterless, it said *something changed* without saying *to what*. **`ENC-9a` — subscribe `VisualizerPanel` to `VisualizationModeChanged`.** | 30 min | The broadcast already exists (`AudioStateUpdateService.cs:969-978`); **no Razor component consumes it.** Any out-of-band mode change leaves the picker showing the wrong segment. |
| 8 | ✅ **#492.** **`UI-1` — delete `/diagnostic`.** ✅ **Decided by D13**, and it frees the route name for the consolidated Settings → Diagnostics surface. | 30 min | 97 LOC, unlinked from navigation, zero tests, a "Click Me" button, and it occupies the best route name in the app. |
| 9 | ✅ **#489.** Only `:253` — `:272` is currently accurate and belongs to `UI-2`. **`UI-5` — fix the two stale DevTray comments/links** (`:253` calls an implemented download "a planned follow-up"; `:272` points at `/metrics`). | 15 min | Same comment-accuracy class `CLAUDE.md` § Pre-Merge Review exists for. |
| 10 | ✅ **#494.** Level-restricted, not dropped: dropping would take the `journalctl -p` triage path with it. **`LOG-11` — drop or level-restrict the API's duplicate console sink.** | 30 min | Every line is currently written twice, on a box where log volume is an audio problem. |
| 11 | ✅ **#495.** ✅ **`XR-1a` — correct the queue's § Cross-repo item #3.** It still records that GV read has never worked; **D17 confirms it does.** | 15 min | A stale record that says a working feature is broken will send the next session chasing a cross-repo defect that does not exist. **Do not file a request file for it.** |


---

## 9. Index and tier counts

| Tier | Count | Notes |
|---|---|---|
| **P0** | **21 listed, 2 open** | **Non-encoder (11):** `TEST-1`, `OPS-1`, `LOG-1`, `TTS-1` (part (i) ✅ done), `AUD-6`, `AUD-7`, ~~`AUD-8`~~, ~~`AUD-9`~~ (**both already shipped 2026-05-22 — closed without code**), ~~`SEC-1`~~ (**closed by verification**), `PHN-1`, `PHN-2`. **Encoder (12):** `ENC-0`, `ENC-1`, `ENC-2`, `ENC-3`, `ENC-4`, `ENC-5`, `ENC-6`, `ENC-7`, `ENC-8`, `ENC-11`, `ENC-12`, `ENC-15`. ⚠ **The encoder bundle went 6 → 8 (Rev 2) → 11 (D1) → 12 (Rev 3's `ENC-15` touch-wake gate), and it is no longer conditional on anything.** ✅ **ALL TWELVE encoder rows have now shipped (2026-09-02/03)** — `ENC-0`, `ENC-1`, `ENC-2`, `ENC-3`, `ENC-4`, `ENC-5`, `ENC-6`, `ENC-7`, `ENC-8`, `ENC-11`, `ENC-12` and `ENC-15` (the last closed with a **FAILED** gate, which withdraws panel blanking rather than delivering it). ⚠ **This line previously read "five of the twelve" and the tier count read "9 open"; both were stale, and the header of this document has said "P0 remaining: 2" since [#544](https://github.com/mmackelprang/RTest/pull/544). Corrected 2026-09-03.** The two that remain are **`PHN-1`** (partially shipped — PRs 1 and 2 of the seven-PR ADR-029 arc landed as `PHN-1a`/`PHN-1b`) and **`PHN-2`**, both in the phone arc. Departures and arrivals since the last revision: **LOG-3 left** this tier under D12; **SEC-1** (D15) and **PHN-1** / **PHN-2** (D17) joined it. ⚠ **ARRIVAL 2026-09-04: `ENC-21`** — the owner's four reversed knobs, filed in §3.5 at P0 by criterion (a) and **already ✅ closed**, so ***open* stays at 2**. ⚠ **And a counting defect this pass found but did NOT silently repair, on the same discipline the P1 cell states below: the arithmetic in this cell does not add up.** It reads **21 listed** while naming *"Non-encoder (11)"* and *"Encoder (12)"*, which is 23; and §3.5 carries three closed rows — **`ENC-15`, `ENC-16`, `ENC-17`** — of which only `ENC-15` appears in the encoder list here at all. Whether those belong in this tally is a tiering judgement rather than an addition, so it is flagged for Planner and left visible instead of folded into a new total. **What is safe to rely on is the *open* figure**, which this pass did not change: `PHN-1` and `PHN-2`. |
| **P1** | **38 listed, 34 open** | **`TEST-3`** (promoted from P2 2026-09-01 — it failed CI and blocked a merge), ~~`ENC-9`~~ (✅ **shipped 2026-09-03 — resolved by deletion; single-surface, local-only visualiser mode**), `ENC-14`, `AUD-2`, `AUD-5`, `AUD-10`, `AUD-11`, `AUD-12`, `LOG-2`, `LOG-3`, `LOG-4`, `LOG-5`, `LOG-6`, `LOG-7`, `LOG-8`, `LOG-11`, `PHN-3`, `PHN-4`, `TTS-2`, ~~`TTS-3`~~ (✅ **closed 2026-09-03 by `TTS-9` — eSpeak removed, so the preview path can no longer hardcode it; its **ducking half stays open***), `TTS-4`, `TTS-5`, `TTS-6`, ~~`TTS-7`~~ (✅ **closed 2026-09-03 by `TTS-9` — resolved as *remove*, not *install***), `TTS-8`, `GV-5`, `UI-2`, `UI-3`, `UI-4`, `UX-1`, `OPS-3`, `HW-2`, plus **6 cross-repo**: ~~`XR-1`~~ (✅ **resolved by `D17` — GV read works; there is no cross-repo defect left in that row**), `XR-2`, `XR-3`, `XR-4`, `XR-5`, `XR-6` (⭐ new 2026-09-03). ⚠ **RECOUNTED 2026-09-04. This cell previously read *"37 listed, 34 open"* and named only `XR-1`…`XR-5`.** **The *listed* figure was one short and the *open* figure was right by coincidence, which is the part worth recording:** `XR-1` has been ✅ closed by `D17` since 2026-08-19 but was still being counted as open, and `XR-6` had never been added to this cell at all — **one over-count and one under-count, cancelling exactly.** A number that is correct because two errors annihilate is not a number anyone should rely on next time, so both are now visible in the list rather than only in the total. **The arithmetic, so it can be checked rather than trusted: 32 named items above + 6 cross-repo = 38 listed; struck through as closed are `ENC-9`, `TTS-3`, `TTS-7` and `XR-1` = 4; 38 − 4 = 34 open.** ⚠ **A separate reconciliation this pass did NOT do, because it needs tiering judgement rather than counting:** this cell and §4's tables disagree about membership. §4 carries **eight** P1 rows this cell never names (`ENC-18`, `ENC-19`, `TTS-10`, `TEST-5`, `TEST-6`, `TEST-7`, and — added 2026-09-04 — `TTS-11` and `TEST-8`; the clause said **six** before those two were filed), and three ids listed here have no row in §4 at all (`LOG-3`, whose row sits in §3.2 where D12 re-tiered it from P0 — correct, just filed elsewhere; ~~`PHN-4`, which appears only in §9's traceability table~~ ✅ **`PHN-4` NOW HAS A ROW — §4.4, filed 2026-09-05 by `D31`. This cell asked Planner to decide it and Planner did; the argument and the ratification flag are in the note below this table**; and `UI-4`, which is folded into `UI-2`'s text). `TEST-3` is a fourth: this cell says *promoted from P2 2026-09-01*, and its row is still physically in §5. **None of that is arithmetic — deciding it is Planner's, and it is flagged here rather than silently folded into a total.** |
| **P2** | **17** ⚠ stale | `AUD-1`, `AUD-4`, `LOG-9`, `LOG-10`, `UI-1`, `UI-5`, **`UI-6`** (new 2026-09-04), `OPS-2`, `OPS-4`, `OPS-6`, `TEST-2`, `GV-6`, `GV-7`, `GV-9`, `GV-10`, `ENC-10`, `HW-1`, `AUD-11a`, `SEC-3`, `SEC-5`. ⚠ **The count and the list had already drifted apart before `UI-6`** — the count said 17 while the list named 15, and §5's table carried four IDs (`OPS-6`, `AUD-11a`, `SEC-3`, `SEC-5`) that appeared in neither. The list is now reconciled to §5; **the count is left as found rather than re-derived here**, because §5 also holds three rows that are closed or promoted (`SEC-2`, `SEC-4`, `TEST-3`) and deciding what a P2 count means is a separate pass. |
| **P3 / parked** | **22** | §6 — six added in the Rev 2 reconciliation, two more from Rev 3 (Designer's withdrawn flash baseline; the on-screen bank rename, recorded so nobody reverts it). |

### Where Designer Rev 2 and this document's tiering interact — stated plainly

Four places where reconciling Rev 2 changed something material rather than just re-wording it. None is an
unresolved disagreement; all four are things a reader of the Rev 1-era text would get wrong.

1. **`ENC-7` moved P1 → P0, and that is a real schedule delta, not bookkeeping.** Rev 1's knob 2 was P1
   because Tone was blocked on an Architect ADR that might never land, behind a fallback Designer has since
   withdrawn. Rev 2's PRESETS is fully specified, needs no new DSP and no new data model, and is simply one
   of the four knobs — so it inherits the same tier as SOURCE. **Combined with the new `ENC-11`, the P0
   encoder bundle went from 6 items to 8 and from roughly 3 working weeks to 3–4.** D1's stakes rose
   accordingly — and **D1 has since been answered "ship live", so this is no longer a hypothetical price:
   it is the schedule.** The bundle is now 11 items, not 8, because D1 also pulled in `ENC-6`, `ENC-8` and
   `ENC-12`.

2. **§7 is new P0 scope that did not exist when the tiers were first set.** The startup config push is not
   polish and does not defer cleanly: without it the device runs whatever survived in its flash, which on a
   fresh, reset or replacement Pico is factory defaults — **volume acceleration at ×50, one detent from
   silence to full.** `ENC-3`'s host clamps make that window survivable, which is why they are stated as
   unconditional; `ENC-11` is what closes it. Splitting the arc so that only the push/verify loop is P0 and
   the fault surfacing, flash baseline and diagnostics card are P1 is a judgement call, and the line drawn
   is: **the behaviour that keeps the room safe is P0; the layer that explains it to the owner is P1.**

3. **⚠ SUPERSEDED BY D12, and the supersession is worth reading.** This item used to reconcile Designer
   §7.2's silent-boot rule with `UI-3`'s argument for *keeping* the Logs tab. **The owner has since removed
   the Logs tab entirely** — so the tension no longer exists, and the in-app diagnostic surface this
   document was defending is gone by choice. **What survives, and now carries more weight than it did:**
   `ENC-12`'s cross-route nav-pill badge is the *only* remaining in-app channel for a persistent hardware
   degradation. Silent when healthy, legible from any page when not. With no log reader behind it, that
   badge is not a convenience — it is the whole diagnostic surface.

4. **`ENC-14` polls at 2 Hz while `UI-2` exists to stop a polling surface.** Not a contradiction: Designer
   pre-empted it explicitly — the diagnostics card polls **only while it is open**, for exactly the reason
   `UI-2` is filed. **Treat `ENC-14` as the pattern `UI-2` should adopt, not as an exception to it:** poll
   while visible, stop when not, and never fan out.

**One thing Rev 2 makes easier, worth saying because it is rare:** Rev 1's advice to leave knob 2 unlabelled
until the DSP question resolved is **obsolete**. Designer §9.5: *"All four assignments are now settled, so
unlike Rev 1 there is no reason to defer engraving."* The escutcheon can be cut and engraved as soon as D2
and D10 are answered — the only remaining gate on the permanent, irreversible step.
✅ **Both answered, and as of 2026-09-01 the panel is drawn** — `design/hardware/front-panel-layout_4.svg`,
with the four names on the drawing's `labels` layer as outlined paths. ⚠ **One tolerance to check before
cutting:** each engraved label clears the knob above it by about **0.8 mm**, which assumes knob bodies stay
15 mm down to the panel face. **A flared skirt or wide base will cover its own engraving** (Designer Rev 4
§9.5).

---

**Confirm-or-close items — the first task is an investigation that may end in no code change:** `AUD-2`,
`AUD-10`, `GV-10`, `TEST-2`, `UX-1`. **Down from seven to five on 2026-08-19:** `SEC-1` is decided by D15
(and is now P0), and `XR-1` is resolved by D17 — GV read works, so what was a cross-repo investigation is
now a 15-minute record correction. Do not budget the remaining five as guaranteed shipped work.

**Already queued (17 open rows, all accounted for):** `GV-5`, `GV-6`, `GV-7`, `GV-9`, `GV-10`, `OPS-1`,
`OPS-2`, `OPS-3`, `UX-1`, `TEST-1`, `TEST-2`, `AUD-1`, `AUD-2`, `AUD-4`, `AUD-5`, `AUD-6`, `AUD-7`.

**Existing plans that have never been queued — a standing source of dropped work:**

| Plan | Item |
|---|---|
| `design/plans/` `bt-capture-watchdog` (2026-05-22) | `AUD-8` |
| `design/plans/` `bt-autoswitch-gate` (2026-05-22) | `AUD-9` |
| `design/plans/` `bt-codec-observability` (2026-05-22) | `AUD-11` |
| `design/plans/` `pw-event-subscription` (2026-05-22) | `AUD-12` |
| `docs/plans/2026-05-22-audio-thread-isolation.md` | `LOG-9`, `LOG-10` |
| `design/plans/2026-03-11-bt-disconnect-reason.md` | `AUD-10` |
| `docs/design-handoffs/HANDOFF-phone-console-audio-and-canned-replies.md` (2026-08-01) + **ADR-029** (2026-08-03) | `PHN-1`, `PHN-2`, `PHN-3`, `PHN-4` |
| `docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md` (**Rev 5**, 2026-09-02, 1,573 lines) | `ENC-0` … `ENC-15` (`ENC-13` folded into `ENC-8`) |
| `design/plans/SECRET-KEYRING-INVESTIGATION.md` + branch `fix/dataprotection-keyring-persist-path` | `SEC-1` |

> **Worth noticing as a pattern rather than a list:** nine plan or handoff documents exist for work that has
> **no queue row**, two of them explicitly marked *"Ready for Planner."* The gap in this project is not
> planning capacity — it is the handoff from Designer/Architect into the queue. If one process change comes
> out of this document, it should be that a document marked *ready for Planner* gets a queue row or an
> explicit written decline within one Planner cycle.

### 🔵 `PHN-4` — the id was adopted rather than minted, and the owner should ratify it

**Filed 2026-09-05 with `D31`. This is a Planner judgement on an ambiguous token, not a finding, and it is
marked for the owner because an id is cheap to change today and expensive to change once it is cited.**

**The problem.** Feature C had no id. `PHN-4` was a token that appeared in **exactly two places** — the P1
count cell above (`§9`) and the traceability row two tables up — and **had never had a row anywhere.**
`git log -S "PHN-4"` shows it arrived **undefined in the punch list's very first commit** (`dfa7af83`), and
the only other commit to touch it (`a5aa91b5`) is the pass that *noticed* it was undefined. So for the whole
life of this document `PHN-4` has named nothing.

**The decision: adopt `PHN-4` for the Feature C cleanup row. Two candidates were considered and one was
rejected on the record.**

- ✅ **Adopt `PHN-4`.** The traceability row maps `HANDOFF-phone-console-audio-and-canned-replies.md` +
  ADR-029 to `PHN-1, PHN-2, PHN-3, PHN-4`, and that handoff specifies Features A, B and C. `PHN-2` is
  Feature A, **`PHN-3` is explicitly Feature B**, and `PHN-5` was minted past it. **Feature C is the only
  thing left in that handoff for the fourth id to be**, and the position is not a coincidence — it is a
  four-item traceability row for a three-feature handoff plus its seam. Adopting it **resolves** a dangling
  token instead of leaving it.
- ❌ **Mint a parallel id** (`PHN-6`, `GV-11`). **Rejected:** it would leave `PHN-4` undefined *forever* —
  a number a future reader can only resolve by reading this note — **and add a second id for one feature.**
  That is strictly worse than the state being fixed.

⚠ **The `D26` precedent was applied deliberately, and it points the same way.** That collision was settled
on *"where is the number cited **from**"* — an ADR citation must not be allowed to rot. **`PHN-4` is cited
from no ADR, no plan, and no external document**: `git grep "PHN-4"` returns **two lines, both in this
file**, both of which this same commit corrects. So adopting it costs nothing outside these pages, which
is exactly the test that let `D26`-as-knobs move and `D26`-as-eSpeak stay.

⚠ **What would make this wrong.** If `PHN-4` was originally minted for something else in that handoff —
a fourth item nobody has since named — then this adoption quietly mis-files it. **Nothing in the repo
records such an item**, and the handoff has three features. But the honest statement is *"the positional
evidence is strong and no statement of intent exists,"* not *"this is what it meant."* **The owner should
confirm or rename; if he renames, only this document and the queue change, because nothing else cites it.**

---

## 10. Carried risks worth restating

- ⭐ **NEW, and it is the highest-weighted safety test in the encoder spec: the accumulator is free-running,
  so a naive decoder delivers an entire outage as one volume delta.** The device keeps counting whether or
  not anything is listening. If the app restarts, or a USB lead is knocked loose inside the cabinet and
  re-seats, and the host resumes by **diffing the new sample against its last remembered value**, then
  **every detent turned while nobody was listening arrives as a single delta — on the volume knob.**
  The rule: **on every connect, the first sample from each encoder is a baseline, not an input — recorded
  and discarded. No delta is ever computed across a disconnect.** ⚠ **Diff-against-last-value is the obvious
  way to write this decoder and it is wrong**; the hazard is in `ENC-1` / `ENC-2` scope, not in any UX work.
  Designer's test, verbatim: *"Turn a knob ~50 detents while unplugged, then replug: volume does not jump."*
- ⚠ **PROMOTED 2026-08-19 — this one is now live rather than hypothetical. A blanked panel plus an encoder
  USB drop can leave the screen unwakeable.** D8 answered **yes** to re-enabling hardware screen blanking,
  with any press or knob movement waking. That makes the black panel the normal overnight state — and the
  encoder USB **does** drop on this box; the reconnect loop exists precisely because it happens. If the
  panel is blanked and the encoders are gone, the wake path is gone with it, **inside a sealed cabinet,
  recoverable only by SSH or by plugging in a keyboard.** Three mitigations, and they are not optional:
  **(1)** touch must independently and reliably wake — verify it on the box before blanking ships;
  **(2)** `ENC-0`'s disappears-mid-session handling has to be correct, and must not mistake change-only
  idle silence for a disconnect; **(3)** ship blanking **after** `ENC-0` and `ENC-6`, never before.
  ✅ **Rev 3 §8.5 hardened all three into requirements rather than advice, and mitigation (1) is now its own
  P0 item, `ENC-15`** — *"This is a gate, not a caveat."* The other two became **coupling rules**: the app
  must **never blank when the encoder device is absent**, and if it **disappears while blanked, unblank
  immediately and stop blanking** until it returns. Designer's framing for the direction to fail in:
  *"a screen left on is a nuisance, a screen that cannot be turned on is a service call."* The 2 a.m.
  recovery line belongs in `INTEGRATIONS.md` beside the encoder section, where someone will actually find
  it: `ssh mmack@radio` → `gdbus call --session --dest org.gnome.ScreenSaver --object-path
  /org/gnome/ScreenSaver --method org.gnome.ScreenSaver.SetActive false`.
- **The voicemail audio endpoint must stay unauthenticated (or token-in-query) when `X-RotaryPhone-Auth`
  ships, because a native `<audio>` element cannot send a header.** ⚠ **This risk dissolves entirely if
  ADR-029 lands** — Radio.API fetching the media server-side can send any header it likes. **`PHN-1` is
  therefore also the fix for a security-shaped constraint that is currently recorded as permanent.**
- **`AUD-7` verification is restart-only.** A truthful `/api/devices/output` in a running session proves
  nothing.
- **Any future UAT of the phone surface must record wall-clock time against the 20-minute blackout cycle**
  (`XR-3`), or the results look random — that is precisely how one pass came to hypothesise GV throttling,
  which was subsequently falsified three ways.
- **`OPS-3` does not auto-merge.** It is the single row in the queue exempt from the auto-merge policy.
- **Cross-repo work is never a Radio Console queue row.** It routes via the boundary-doc protocol into
  `D:\prj\RotaryPhone\docs\prompts\`. Note that **two boundary-doc Change Log entries are themselves sitting
  uncommitted in that repo — including the one that established the rule they are both breaking.**

---

**Approved 2026-09-01. Executing in §2 order.** `D23` / `D24` / `D9` / `D25` all closed by the owner. **No open decisions remain.** The §2 ordering constraints still bind — `O4` (`LOG-6` before
`LOG-10`) can brick the appliance and `O9` (knob order before drilling) is irreversible.
