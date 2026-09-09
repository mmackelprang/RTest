# UX-1 — Skeleton shimmer amplitude — is a 6/255 gradient delta enough on the dark theme?

> Queue dossier for row **`UX-1`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

> ## ⛔ SUPERSEDED IN PART, 2026-09-09 — **this row's landing value is REOPENED and the two owner sittings disagree.** Read this before believing any "56" or "DECIDED" below.
>
> **Everything below that says the value is `56` / `#38383F` / "DECIDED" records what was true when
> it was written, and is kept legible for that reason.** A third sitting has since chosen a
> different value, and the disagreement is not resolved. **Nothing below is retracted; it is
> superseded pending one more sighting.**
>
> | Sitting | Condition | Instrument | Value chosen |
> |---|---|---|---|
> | 2026-09-08 night | dark room | **v1 harness — recorded in this file as BROKEN** (band off the element for part of every cycle; geometry never fairly tested) | rejected 36 as *"below the dark-room visibility threshold"*; chose **56** |
> | 2026-09-09 midday | daylight | v2 harness, shipping geometry, blind five-value ladder | **56** — confirmed, shipped as [#641](https://github.com/mmackelprang/RTest/pull/641) |
> | **2026-09-09 afternoon** | ⚠ **unrecorded** | **three-panel static demo on the real console at 1920×720, rendering the deployed stylesheet** — delta 6 / delta 16 / delta 36 side by side | **36** (`#24242B`) |
> | **2026-09-09 later** | ⚠ **unrecorded** | ⭐ **one-at-a-time harness** — a single value revealed on a button press, so the side-by-side reference is removed. **A better instrument than the row above** | **36** (`#24242B`) — *"Shimmer 36 looks good."* |
>
> **Tally: two sittings chose 36, one earlier sitting rejected it.** ⛔ **That is NOT a 2-to-1 verdict,
> and this file does not treat it as one.** The ambient conditions of **both** recent sittings are
> **unrecorded** — nobody established whether either was a dark room — so they do not directly answer
> the condition the earlier sitting was judged in. And the earlier sitting used the **v1 harness the
> plan itself calls broken**, so it is weak evidence in the other direction. ⭐ **Unresolved, not
> settled.** This file does not pick a winner, and neither does the PR that implements 36.
>
> **Two confounds, both live:**
> 1. ⛔ **The dark-room sitting's instrument is suspect** — it ran on the **v1** harness, which the
>    section *"The first harness was flawed, and the flaw is the instructive part"* below records as
>    unable to measure what it claimed. So the earlier dark-room conclusion is questionable **on
>    instrument grounds**, independent of anyone's eye.
> 2. ⚠ **Side-by-side viewing may bias toward the dimmest acceptable value.** With delta 36 on
>    screen next to it as a reference, delta 16 reads as sufficient; alone in the dark it may not.
>    This is a real perceptual effect. ⛔ **It is NOT evidence that the owner was wrong this
>    afternoon** — it is the reason the re-check is required before anything merges.
>
> **The one-at-a-time re-check has now happened and 36 held** — the harness at
> `http://radio:5002/shimmer-demo.html` reveals a single value on a button press, which removes the
> side-by-side bias in confound 2. ⚠ **What it does NOT do is establish the ambient condition**;
> nobody recorded whether the room was dark. So confound 2 is answered and **confound 1's
> disagreement is not.** ⚠ The harness lives in `/opt/radio-console/web/wwwroot/`, which
> `Deploy-ToLinux.ps1:271` wipes with `rsync --delete` — it is not in this repo and will not survive
> the next deploy.
>
> ⭐ **WHAT ACTUALLY DEFUSES THIS — it changes the stakes, not the evidence.** The shimmer is on
> screen for **~3–7 ms against a 1500 ms cycle** on the two panels measured, so **on those, in warm
> steady state, no sweep is painted at any value** (⚠ two panels of 27+ call sites; Devices, Radio and
> the phone thread are unmeasured, and a cold or slow path would dwell longer — see the
> final section). If 36 *is* too dim in a dark room, the consequence is that **an invisible element
> is marginally more invisible.** The value choice is **very low stakes until the dwell question is
> answered** — which is the honest reason this can proceed on an unresolved disagreement.
>
> ⛔ **AND THE COROLLARY, WHICH MUST NOT BE LOST: if dwell is ever fixed, THE VALUE MUST BE RE-JUDGED
> AT THAT POINT** — that will be the first time anyone actually sees the shimmer in the product, and
> the only sightings on record were made on a harness that holds it on screen artificially. **36 is
> not settled-forever; it is settled-for-now, on a surface nobody can currently see.**
>
> ⚠ **The value being shipped is NOT literally the Designer's seed, though it sits on that rung.**
> The seed of record is **`#242429` = rgb(36,36,41)** (the Designer answer's *"The change"* block
> below (it was `:87` on `main` @ `f4d71b28`; this banner moved it and **no current-line number is
> quoted, because it keeps moving** — see the note under the dwell section) and the v2 ladder in
> [`NIGHT-SITTING.md`](../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md)). What the owner
> actually looked at this afternoon was **`#24242B` = rgb(36,36,43)** — **+2 on blue**, hue lean
> B−R **+7** rather than the Designer's **+5**. Two levels on one channel near black cannot have
> driven the judgement, but the ladder sighted at night and the panel sighted this afternoon were
> **not painting the same colour**, and rounding them together would be the kind of label-drift this
> row has already been bitten by twice.
>
> ⭐ **And a finding that reframes the whole row** — see the final section of this file, *"The
> shimmer is effectively invisible in normal operation"*. **The skeleton is on screen for roughly
> 0.3% of one animation cycle**, so no sweep is painted at *any* highlight value. The amplitude
> question is cosmetically real and operationally moot until dwell is answered.

| Field | Value |
|---|---|
| Status | ⚠ **REOPENED 2026-09-09** — [#641](https://github.com/mmackelprang/RTest/pull/641) shipped `56`/`#38383F` and is merged and deployed; a follow-up PR changing it to `36`/`#24242B` is **open and deliberately unmerged**, gated on the owner's dark-room re-check. See the banner above and the two new sections at the foot of this file. _Original cell: "✅ [#641](https://github.com/mmackelprang/RTest/pull/641) — shipped 2026-09-09; see the note at the foot of this file"_ |
| Plan | [`UX-1-the-shimmer-nobody-can-see.md`](../../design/plans/UX-1-the-shimmer-nobody-can-see.md) — ⚠ **its geometry half is SUPERSEDED and carries banners saying so**. _Original cell: "plan TBD — do not write one until the Designer has answered; scope depends entirely on whether the answer is 'new token,' 'retune the existing pair,' or 'leave it'"_ |
| Spec / handoff | [GV-8 UAT `L-1`](../uat/2026-07-31-gv8-error-state/REPORT.md) · evidence: `uat/2026-07-31-gv8-error-state/screenshots/03-c2-frame-a-108ms.png` vs `04-c2-frame-b-224ms.png` · [night sitting](../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md) · [daylight sitting](../uat/2026-09-08-ux1-shimmer-variants/DAYLIGHT-SITTING.md) · ⭐ [**third + fourth sittings, with the harness**](../uat/2026-09-09-ux1-third-and-fourth-sittings/REPORT.md) |
| Depends on | — _(no code dependency; it is gated on a design answer, not on a row)_ |
| Branch | `fix/ux-1-shimmer-amplitude` _(this row named `feat/ux-skeleton-shimmer-amplitude`; the coordinator's name was used)_ |

## Detail

**Skeleton shimmer amplitude — is a 6/255 gradient delta enough on the dark theme?**

**Design-led: get a Designer answer before a plan is written, and accept that this row may legitimately close as "no change."** From [GV-8 UAT `L-1`](../uat/2026-07-31-gv8-error-state/REPORT.md).

**The shimmer is NOT broken, and this row must not be re-filed as "the skeleton is broken"** — it demonstrably animates, on three independent kinds of evidence: `animationPlayState: running` with `Animation.currentTime` advancing **0 ms → 117 ms**; `backgroundPosition` moving `-200%` → `-174.531%`; and a CDP screencast in which **14.5 % of pixels changed** in the skeleton region between two frames **116 ms** apart, against a **0 %** change in a static control region of the same frame pair (which is what rules out compression noise and global repaint). `prefers-reduced-motion` was `false`, so the `animation: none` override was not in play.

**What is marginal is the amplitude.** `.skeleton-loading` (`src/Radio.Web/wwwroot/css/design-system.css:1075-1083`) is `linear-gradient(90deg, var(--surface-raised) 0%, var(--surface-overlay) 50%, var(--surface-raised) 100%)` at `background-size: 200% 100%`, and on this theme those tokens are `#141416` and `#1A1A1D` (`design-system.css:65`/`:67`) — `rgb(20,20,22)` → `rgb(26,26,29)`, a **6/255 stop-to-stop delta**, with **measured peak frame-to-frame change of 3/255**. Side by side the two captured frames read as identical to the eye.

**Scope is the whole app, not the texts pane — this is the point of the row.** `.skeleton-loading` is the shimmer primitive every skeleton composes with: `Skeleton.razor`'s seven shapes (NowPlaying, Radio, ListRow, DeviceRow, MetricTile, Visualizer, …) are mounted at **27 call sites across 6 pages** (`DeviceManagementPage`, `PlayHistoryPage`, `QueueHistoryPanel`, `RadioControlPanel`, `RadioPage`, `VisualizerPanel`), plus **38 raw `.skeleton-loading` nodes** in the phone panels.

**A one-line token change lands on all of them at once — which is precisely why this is a design-token decision and not a bug fix, and why it was correctly kept out of GV-8.**

**Questions for the Designer, none pre-decided here:** should the shimmer highlight get **its own token** rather than borrowing `--surface-overlay`, which exists to be a *surface* and is also used with `backdrop-filter: blur(20px)` (`design-system.css:184`) — i.e. it is not free to move? Is a wider delta still tasteful on a **wall-mounted kiosk in a dark room**, or does it start reading as a flashing band? Does the answer differ per shape — a full-bleed `Visualizer` block sweeping is a much larger moving area than a 16px `skeleton-text` bar.

**Constraint any fix must honour:** the `prefers-reduced-motion: reduce` override at `design-system.css:1683` sets `animation: none`, so reduced-motion users see the **static** gradient — widening the amplitude must not make that state worse.

**Judge it on the kiosk panel, not a laptop LCD:** the numbers above came from frames rendered on the box, and a 6/255 delta is exactly the range where two displays will not agree.

---

## ⭐ Designer answer, 2026-09-07 — the gate is discharged; this row can be planned

**Verdict: a new token, `--skeleton-shimmer` — but amplitude is the SMALLER half of the defect.**

### What was measured (reproducing the UAT's numbers exactly)

| Quantity | Value |
|---|---|
| Skeleton body → highlight (spatial, one frame) | (20,20,22) → (26,26,29) = **6/255** R·G, 7 B |
| Temporal delta over 116 ms | **max 3/255** — matches the row |
| Block vs. page behind it | 7–13/255 |
| **Local gradient steepness, real 178 px `.skeleton-text`** | **0.034 levels/px** |

### The finding that reframes the row

**6/255 near black is not a trivial contrast.** Linear luminance goes 0.00699 → 0.01033 — a 1.48×
step, **48% Weber contrast**. A hard *edge* at that contrast would be visible.

The problem is that `background-size: 200% 100%` stretches the raised→highlight ramp across **one
full element width**. A full-resolution scanline in frame A is a *monotonic* 20→26 ramp with no peak
and no edge anywhere; frame B shows the peak just inside the right edge, turning over. So it does
animate — and nobody sees it because there is nothing to detect. An edgeless wash is the single
hardest stimulus for human vision, which is the same reason smooth gradients hide banding.

**Amplitude and geometry multiply, and the geometry is doing most of the damage — and it is free to
fix.**

### Why not the alternatives

- **Not "retune the existing pair":** `--surface-overlay` is referenced by **19 other rules** —
  `.surface-overlay` with `backdrop-filter: blur(20px)`, modals, bubbles, hover states, and two
  `color-mix` recipes. It is a *surface* token; the shimmer highlight is a *motion* value. Moving it
  repaints half the app. **The row's instinct here was right.**
- **Not "leave it":** an infinite animation on up to ~65 nodes that is provably below threshold is a
  cost with no benefit, on a box with documented load-sensitivity in the audio path. The honest
  positions are *make it visible* or *stop paying for it* — not *keep invisible motion*.
- ⭐ **A fourth option the row did not list: delete the animation and keep a flat block.** Not
  recommended — the shimmer is the right affordance and the fix is cheap — but it is defensible and
  it beats the status quo.

### The change

Add beside the surface block at `design-system.css:65-70`:

```
--skeleton-shimmer: #242429;   /* (36,36,41) */
```

Delta becomes **16/255 R·G, 19 B**. 36 is **2.5× the linear luminance** of `--surface-raised`, chosen
on a luminance-ratio ladder (26 = 1.48×, 31 = 2.0×, 36 = 2.5×, 40 = 3.0×) rather than a code-value
one, which is what keeps the pick defensible on a panel whose gamma is unmeasured. Hue keeps the
family's cool lean (B−R: +2 raised, +3 overlay, +5 here).

⚠ **Do not land on 31** despite it being the clean 2.0× rung — `(31,31,34)` is *identical* to
`--surface-separator` `#1F1F22`, and two tokens at one value is how a system starts looking
accidental.

**And the geometry, which is the bigger half and costs nothing:**

```
background: linear-gradient(90deg,
  var(--surface-raised)    0%,
  var(--surface-raised)   35%,
  var(--skeleton-shimmer) 50%,
  var(--surface-raised)   65%,
  var(--surface-raised)  100%);
background-size: 200% 100%;   /* unchanged */
```

Ramp narrows from 50% of the tile to 15% — **3.3× steeper** — while the tile stays 2W so exactly one
band is on screen (no barber-pole). Combined with amplitude: **0.034 → ~0.30 levels/px, roughly 9×.**
Keyframes, the `1.5s`, and the one-pass-per-750 ms cadence are unchanged.

**Do not split the token per shape.** `background-size` is a percentage, so geometry is relative to
each element: every shape completes a pass in 750 ms but the moving *area* scales. `.skeleton-art`
(min-height 180 px) will be far more assertive than `.skeleton-progress` (**4 px tall**, where it
stays near-invisible — acceptable, that shape communicates by presence). If `.skeleton-art` reads as
too much, the lever is a **fixed-px** `background-size` on that one shape, not a second colour token.

### ⚠ The row's reduced-motion premise is outdated — nothing to do

`design-system.css:1713-1724` (the row cites `:1683`) does not merely set `animation: none`; it
replaces the background entirely with `rgba(255,255,255,0.05)`, a flat fill. The gradient never
renders in that state, so widening the amplitude **cannot** make it worse.

### How to verify on the panel — order matters

**Test the free geometry change before the token change**, so "leave the tokens alone" stays a live
outcome.

1. ⭐ **A pre-check that can moot this row: measure real skeleton dwell time.** One pass is 750 ms. If
   thread-open latency is typically <500 ms, no shape completes a pass and the block looks static at
   *any* amplitude — which would argue for "leave it" or for removal.
2. One throwaway comparison page, five rows animating **simultaneously**: today · geometry-only at
   today's 6/255 · 2.0× · 2.5× · 3.0×. Sequential A/B across deploys is useless for a 10-level
   judgement.
3. In the kiosk at **1920×720, on the box** — not a laptop, not a scaled window.
4. From the **actual chair**: real distance, cabinet height, off-axis angle. Then again **in the dark
   room**, the console's dominant condition.
5. **Score two separate questions:** (a) looking at it, can you tell it moves? (b) *not* looking at
   it, does it stay calm or pull the eye? **(b) is what kills an over-tuned value**, and it is the
   question a designer at a desk never asks.
6. Check both extremes in one view — if the winner makes `.skeleton-art` assertive while
   `.skeleton-progress` still looks dead, that is the signal for the fixed-px fallback.
7. Photograph from the viewing position with **manual, locked exposure on a tripod**. Auto-exposure
   lifts near-blacks and will lie; use it only to compare variants within one shot.
8. Confirm no N100 regression with a skeleton-heavy page up — paint-only, animation count unchanged,
   so it should be load-neutral, but the audio-distortion correlation makes it worth a glance.

### What could NOT be determined off-box

⚠ **The panel's actual transfer function at code values 20–40 is the crux, and it is unmeasured.** If
the panel crushes near-black, today's 20-vs-26 may deliver *literally zero* difference rather than
6/255 — meaning the problem is understated. A raised black floor cuts the other way.

**Confidence: HIGH that 6/255 is too low. LOW-TO-MODERATE that 36 is the right landing value.** The
number is a seed for the A/B, not an answer. Also unknown: off-axis gamma (cabinet mount implies a
vertical angle, which shifts near-black most), ambient light and usage hours, real dwell time, and —
no captured evidence of a *wide* shape shimmering, since the frames contain only ~178 px bars and
small chips. The `.skeleton-art` claims are derived from CSS geometry, not observed.

### Loose thread, flagged not chased

The selected message row (x≈158–1399, y≈363–425) is **also** animating between the two frames —
(17,24,27)→(18,28,31), a **4–5/255** G·B change across 99.1% of that region, i.e. *larger than the
skeleton's own 3*. The UAT's static control sat ~5 px below it and correctly read 0%, so its
conclusion stands — but anyone re-running a whole-frame diff on these files will find a large diff≥4
population that has nothing to do with the skeleton.

---

## ⚠ OWNER SITTING ABORTED 2026-09-08 — and it changed the row's verification

The five-variant harness was built and shown on the console. The owner stopped the test:

> "the graphics are very dark on the touchscreen (although I'm looking at it in the daylight) we
> should probably defer this test until nighttime."

Console restored, server stopped, nothing changed on the box. Harness and full write-up:
[`docs/uat/2026-09-08-ux1-shimmer-variants/`](../uat/2026-09-08-ux1-shimmer-variants/REPORT.md).

**⭐ The plan's deciding gate is incomplete as written.** It specifies the owner's eye on the panel
**in a dark room**. This console is *also used in daylight*, and in daylight the skeleton surface
reads as very dark — which is precisely the condition where a low-amplitude shimmer vanishes hardest.

Three consequences for the plan:

1. **Run the A/B in BOTH conditions, or the winner is proven for only one.** A value chosen at night
   that disappears at midday is this row's own defect, relocated rather than fixed.
2. **It leans toward the brighter candidates** — if one value must serve both, V1 (26) and V2 (31) are
   the least likely to survive daylight. ⚠ **Hypothesis, not a result.** Nobody has compared any
   variant in daylight, and reasoning about these amplitudes has already been wrong twice.
3. **There may be a bigger question underneath.** "Very dark on the touchscreen in daylight" is an
   observation about the *theme*, not just the skeleton. Out of scope here; worth its own row if the
   owner sees it again.

**No variant has been judged.** The sitting ended before any comparison was made, so every open
question in this row is still open.

---

## ⭐ NIGHT SITTING 2026-09-08 — the value is **56**, and two premises died

> ⛔ **SUPERSEDED IN PART 2026-09-09 — kept as written.** This sitting's rejection of 36 is the half
> of the record now in dispute: the owner chose 36 in afternoon light on 2026-09-09. ⚠ **And this
> sitting ran on the v1 harness that the very next subsection declares broken**, which is the
> strongest argument against its own conclusion. Nothing here is retracted; a dark-room re-check of
> `#24242B` alone decides it. See the banner at the top of this file.

Full record: [`../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md`](../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md).

**Result: `56` (`#38383F`, delta 36 against the `#141416` base) is the dimmest value the owner can
clearly see in a dark room, AND it stays calm — no distraction.** Both questions answered.

**That is 6× today's shipping delta of 6, and 2.25× the Designer's seed of 36.**

### ⛔ The Designer's central claim is contradicted by the owner's eye

The answer that discharged this row's gate said **amplitude is the SMALLER half and geometry is the key
lever.** The owner could see only the *full-width shipping ramp* and found even that *"very dim and not
easy to see in a dark room."* **Geometry did not help; amplitude is the whole problem.** The seed of
**36 is below the owner's dark-room visibility threshold.**

> ⛔ **THAT LAST SENTENCE IS THE ONE NOW IN DISPUTE. Superseded 2026-09-09; kept legible.** On
> 2026-09-09 the owner viewed delta 6 / 16 / 36 side by side on the real console in afternoon light
> and said **"Designer seed 36 is my choice."** ⚠ **Two of the owner's own judgements, on the same
> rung, in opposite directions.** Neither is being called wrong here. The two confounds — this
> sitting's **broken v1 instrument**, and the **side-by-side bias** that can make the dimmest
> acceptable value look sufficient — are set out in the banner at the top of this file, and the
> merge of the follow-up PR is gated on a **dark-room re-check of `#24242B` on its own**.

⭐ To its credit the Designer explicitly refused to endorse a landing value — *"a seed for the A/B, not
an answer."* That caution was well placed and is why this was A/B'd rather than built.

### ⛔ The first harness was flawed, and the flaw is the instructive part

> ⛔ **The two figures in this paragraph are SWAPPED and its arithmetic is wrong — corrected
> 2026-09-09 by measurement; see the shipped note at the foot of this file.** Kept as written because
> it is what was concluded at the time. Measured: **ON ~1.01 s, OFF ~0.49 s**, and the mechanism is
> `ease`, not a duty cycle.

Its V1–V4 concentrated the highlight into a band spanning ~**0.32 of the element width**. With the
sweep travelling 4 element-widths in 1.5 s, that band is on the element only ~**0.49 s per cycle** —
**so for two-thirds of every cycle those columns were flat, unmoving `#141416`.** The owner read them
as *static* because they **were** static.

So the first sitting did not test "geometry vs amplitude"; it tested a geometry that traded a
faint-but-continuous shimmer for a brighter-but-mostly-absent one. ⭐ **Another instrument that could
not see what it claimed to measure** — same family as `NRestarts=0` and `psidtsAgeSeconds: 608`.

### ⚠ 56 is a FLOOR. Daylight is still untested.

The morning sitting was aborted precisely because the surfaces read as *very dark* in daylight, so
**the daylight value will not be lower.** One sitting remains, on the same ladder, asking the same two
questions.

⭐ **If daylight demands more than a night-comfortable maximum, this row changes character entirely** —
from *"pick a token value"* to *"the shimmer must adapt to ambient light"*, which is materially bigger
work. **Establish that before anyone builds the cheap version.** 66 was deliberately not evaluated,
because it only matters in that branch.

---

## ✅ DECIDED 2026-09-09 — **56 (`#38383F`, delta 36)**. Both conditions agree. The expensive branch does NOT fire.

> ⛔ **"DECIDED" NO LONGER HOLDS — superseded the same day, kept as written.** A third sitting on
> 2026-09-09, in afternoon light against a three-panel comparison on the real console, chose
> **36 (`#24242B`, delta 16)** instead. ⚠ **"Both conditions agree" was true of the two sittings
> this section had; it is not true of the three that now exist.** The heading's other claim — that
> the ambient-light-adaptation branch does not fire — is **untouched** by the new sitting: nobody
> has asked for a value that varies with light. See the banner at the top of this file.

Full record: [`../uat/2026-09-08-ux1-shimmer-variants/DAYLIGHT-SITTING.md`](../uat/2026-09-08-ux1-shimmer-variants/DAYLIGHT-SITTING.md).

The night record set the decision rule in advance, and daylight met it:

> *"If 56 also reads as the dimmest clearly visible at midday, the row lands on 56 and is done."*

⭐ **Daylight did not want more than the night-comfortable value.** So this row stays *"pick a token
value"* and never becomes *"the shimmer must adapt to ambient light"* — the materially bigger work it
warned about. **`66` was on the ladder, was not chosen, and now never needs a night evaluation.**

### ⛔ The implementation trap — verified, and it is the whole risk in this row

`.skeleton-loading` (`design-system.css:1099-1104`) takes its middle stop from
**`var(--surface-overlay)`**, and that token has **20 consumers in `design-system.css`**.

**Changing `--surface-overlay` to `#38383F` would lighten twenty unrelated surfaces** — a re-theme
shipped under a row about skeleton shimmer. ⛔ **Do not touch it.** The shimmer needs **its own token**
(e.g. `--skeleton-shimmer-highlight`) consumed only by `.skeleton-loading`. ⚠ The Builder must
re-verify the 20-consumer count itself; it is the entire reason this is a new token rather than a
one-character edit.

### ⚠ The harness nearly recorded the wrong answer, and the lesson is reusable

Shuffle re-randomises the columns while the answer is given as a **letter**, so a letter-based answer
is valid only for the arrangement on screen at the moment of judging. The owner's first answer, `"A"`,
meant **66** under the coordinator's twenty-minute-old mapping and **26** under the live one — *one of
which would have overturned this row and the other triggered its expensive branch.* Caught by
re-querying the live page. **Ask for the VALUE, not the position.**

---

## ✅ SHIPPED 2026-09-09 as [#641](https://github.com/mmackelprang/RTest/pull/641) — the token only; the geometry deliberately did NOT ship

> ⚠ **Still accurate as history, but the VALUE it names is superseded — kept as written.** #641
> merged and was **subsequently deployed** (the box served `#38383F` when checked on 2026-09-09, so
> the "NOT deployed" note below is stale). A follow-up PR changes the value to **`#24242B`** and is
> **open and unmerged**, gated on a dark-room re-check. ⭐ **Everything else in this section still
> stands** — the token's existence, its single consumer, the untouched `--surface-overlay`, the
> 22-consumer blast radius, and the stale-anchor warnings are all unaffected by the value change.

`--skeleton-shimmer-highlight: #38383F` declared beside the surface block and consumed by
`.skeleton-loading` alone. `--surface-overlay` is untouched. ⛔ **Merged, NOT deployed** — the
coordinator deploys so the owner can see it.

### The trap, re-verified by the Builder as the row demanded

**The row's "20 consumers" is right for the file it scoped and understates the app by three.** On
`main` @ `143d678e`, `design-system.css` holds **22** textual occurrences of `--surface-overlay`: the
declaration at `:67`, a prose mention inside a comment at `:2867`, and **20 `var(--surface-overlay)`
consumers** — one of which was the shimmer, leaving **19 others**. ⭐ **But three more consumers live
outside the stylesheet**: inline `style=` attributes on three modal dialogs at
`PlayHistoryPage.razor:207,272,296`. **The real blast radius was 22 other consumers, not 19.** The
row, the plan, the Designer answer and the daylight record all scoped the count to the stylesheet and
none of them looked past it. After the change the file holds **19** consumers, and exactly one
declaration and one consumer of the new token.

### ⚠ Line anchors: this row's, and the plan's, are all stale

`.skeleton-loading` is at **`:1104-1112`** on `main` — not `:1099-1104` as this row (the *"⛔ The
implementation trap"* subsection, `:353`; this citation said `:260`, which was already wrong on `main`
and which this row's supersede banner has since pushed further out) and
[`DAYLIGHT-SITTING.md`](../uat/2026-09-08-ux1-shimmer-variants/DAYLIGHT-SITTING.md) (`:72-73`) both
say, and the rule is **9 lines, not 6**. The plan's `:1099-1107`, `@keyframes shimmer` `:977-980` and
reduced-motion `:1713-1724` are likewise stale (`:982-985`, `:1726-1729`). ⚠ On the merged branch
they have moved again — the token block is 46 lines and pushed everything below it down.

### ⛔ The geometry half of the plan was NOT shipped, and that is a decision

Plan Task 5b narrows the ramp to `35% / 50% / 65%` and the Designer rated it **the larger half of the
fix**. It did not ship. **The only experiment ever run on that geometry was the `v1` harness, which
was afterwards found unable to measure what it claimed.** `56` was chosen on the **`v2`** ladder at
the **shipping** geometry, in both conditions. Shipping the narrowed ramp would have put an
unvalidated change underneath a validated one.

⛔ **The published reason that harness failed is WRONG, and the Builder propagated it before pre-merge
review caught it.** The night record's *"band on the element only ~0.49 s per 1.5 s cycle, two-thirds
static"* has **both figures swapped**: the arithmetic counts one tile, but each `.vN .sk` sets
`background` as a **shorthand**, which resets `background-repeat` to `repeat`, so the 2W tile recurs
and the band crosses **twice** per cycle. ⭐ **The Builder's own comment contradicted itself eight
lines later**, correctly deriving *"4W of travel per 1.5 s yields one pass per 750 ms"* — true only
because of the tiling the duty-cycle figure ignored. **Sampled in Chromium over two cycles: ON
~1.01 s, OFF ~0.49 s** (duty 0.63–0.67). ⭐ **The real mechanism is `ease`**, which the animation gets
by declaring no timing function: measured sweep speed drops to **2.1 %/s against a median of
221.7 %/s**, a **~390 ms dead pause at every cycle boundary** plus **~110 ms** mid-cycle. A narrow
band sits those out; a full-width ramp never leaves the element. ⭐ *Right conclusion, wrong reason* —
corrected in nine artifacts including `NIGHT-SITTING.md` itself, **before** it reached the on-page
banner a human opens in a browser.

⚠ **Whether a steeper ramp would allow a *lower* amplitude is still unknown** and is filed in
`design/FUTURE-WORK.md`, along with **the plan's Task 0 dwell-time pre-check, which was never run** —
one pass is 750 ms, so if production skeletons dwell for less, no shape completes a pass **at any
amplitude**. The amplitude is now right; whether skeletons are on screen long enough to show it is
open.

### ⭐ The gate was a demonstrated DIFFERENCE with a CONTROL, and the control is the half that matters

Measured in Chromium against the real stylesheet, `main`'s copy vs the branch's. Computed middle stop
on `.skeleton-loading`: **`rgb(26,26,29)` → `rgb(56,56,63)`**. **CONTROL: `.surface-overlay` and
`.rz-dialog`, two *direct* consumers of the shared token, both held `rgb(26,26,29)` across the
change** — a gate checking only the subject would pass just as happily on the re-theme this row
exists to avoid. Instrument validated first: 859 rules loaded (not UA defaults),
`prefers-reduced-motion` false (so the flat-fill override was not in play), and ⭐ **the reading was
proven animation-invariant rather than assumed** — sampled 420 ms apart while running,
`background-position` moved `-136.261%` → `84.8031%` while `background-image` was byte-identical.

In the framebuffer, at a pinned phase: amplitude **6.0 → 35.83 levels**, min 20.0 both sides, max
26.0 → 55.83. ⭐ **The instrument calibrates on the published 6/255 baseline exactly.** Windowed slope
0.052 → 0.219; average slope 0.034 → 0.201, clearing the plan's §4.2 bar of 0.20 **at the shipping
geometry**. ⭐ **`C-221`'s trap was demonstrated, not merely cited**: naive adjacent-pixel differencing
moves only 0.5 → 0.667 where the real change is 6×.

⚠ **§4.2's gate is internally inconsistent** — it defines `S` as a *peak* windowed slope but
calibrates V0 against **0.034**, which is the *average* slope (`6/178`). The measured peak on the
unfixed sheet is **0.052**, so Task 3's *"do not proceed past a V0 that disagrees"* would have halted
a correct instrument.

### ⛔ A live false claim was found in this row's own evidence directory

[`REPORT.md`](../uat/2026-09-08-ux1-shimmer-variants/REPORT.md)'s second sentence reads *"The harness
works and is reusable; the conditions were wrong."* **The night sitting falsified the first half the
next day**, and while `NIGHT-SITTING.md` records the flaw, `REPORT.md` was never corrected and
`index.html` carried **no warning at all** — so the one file someone would open in a browser was the
one with nothing on it. Struck in `REPORT.md`; `index.html` now opens with a banner, kept
deliberately **dim** because that page is judged in a dark room.

### ⚠ Three defects in the Builder's own comment, found by re-deriving it

*"two `color-mix` recipes"* is **three** (`:2145`, `:6252`, `:6502` — the Designer said two, the plan
said three, the Builder inherited the wrong one); *"56 answers yes and yes in both conditions"*
overclaimed, because **calmness was asked only at night** and the daylight record *argues* rather than
establishes that it does not bind; and *"was measured below the threshold"* became *"was judged
below"* — nothing was measured, an owner looked at a ladder. Commit `62549d87`'s claim that all four
new tests are RED gates was also wrong: **three are, the fourth correctly passes on both sides** and
is a guard against a future edit.

**Gates:** Release build **47 warnings / 0 errors** (`--no-incremental`; an incremental build reports
`0 Warning(s)`, which is unmeasured, not better). Suite **3,868 passed / 4 failed**, the four being
the documented Windows-known-failing `SrcVariableResamplerTests`. ⚠ **No test in this repository can
see this change** — the four new tests assert CSS source text, and a green suite must never be cited
as UAT for this row.

---

## ⚠ SITTINGS THREE AND FOUR, 2026-09-09 — the owner chose **36 (`#24242B`)** twice, and the row is reopened

**Result: the owner viewed three panels side by side on the real console and said "Designer seed 36
is my choice."** That is the opposite of what the night sitting concluded about the same rung. **A
fourth sitting later the same day confirmed it on a better instrument** — see below.

⭐ **Full record, including the harness itself:**
[`../uat/2026-09-09-ux1-third-and-fourth-sittings/REPORT.md`](../uat/2026-09-09-ux1-third-and-fourth-sittings/REPORT.md).
⚠ **That directory was filed retroactively, on a pre-merge review finding.** These two sittings
existed only as prose here, while the two they supersede had a full evidence directory — and the
harness that produced them lived as **one file on one box**, in a directory `Deploy-ToLinux.ps1:271`
wipes with `rsync --delete`. It is now committed (`md5 6969e58ed9de5ae3fda5ab17f39a3057`, verified
identical to the box's copy). ⭐ **This row's whole history is instruments found broken after the
fact; an unreproducible one is that failure waiting to happen.**

### How it was judged — this matters more than the number

**The owner did not pick a number off a list.** A static demo page was built on the live box that
**held the skeleton on screen permanently** — necessary because, as the next section records, the
skeleton is otherwise on screen for a few milliseconds and cannot be looked at at all. It rendered
**three panels side by side using the real deployed stylesheet** at 1920×720:

| Panel | Value | Delta vs `#141416` |
|---|---|---|
| old (pre-`UX-1`) | `#1A1A1D` | 6 |
| "Designer seed 36" | **`#24242B`** | **16** |
| shipped by #641 | `#38383F` | 36 |

**The owner judged rendered pixels, not labels.** That is the strongest form of evidence this row
has ever had for any value — and it is also true of the night sitting, which is precisely the
problem.

### ⛔ The conflict, stated without resolving it

The night-sitting section above (still legible, under *"The Designer's central claim is contradicted
by the owner's eye"*) records **"36 is below the owner's dark-room visibility threshold."** ⚠ That
sentence was `:217` on `main` @ `f4d71b28`; this row's supersede banner moved it, so it is cited by
section rather than by number. ⛔ **No current-line number is quoted, deliberately** — see the note
under the dwell section. The owner rejected 36 **in a dark room** and has now
chosen it **in afternoon light**. **Both judgements are the owner's and they contradict each other
on the same value.**

**Two confounds. Neither is being used to declare a winner:**

1. ⛔ **The earlier sitting's instrument was broken.** The night sitting ran on the **v1** harness,
   which this file already records as unable to measure what it claimed — its highlight band was off
   the element for a large part of every cycle, so the geometry was never fairly tested. The old
   dark-room conclusion is therefore suspect **on instrument grounds**, before anyone's eye is
   questioned.
2. ⚠ **Side-by-side viewing may bias toward the dimmest acceptable value.** With delta 36 on screen
   beside it as a reference, delta 16 reads as sufficient; alone in the dark it may not. This is a
   real perceptual effect. ⛔ **It is NOT evidence that the owner was wrong this afternoon** — it is
   the reason a dark-room re-check is required before this merges.

⛔ **Do not write that 36 is "correct", and do not write that the earlier finding was "wrong".** The
sittings disagree and the ambient condition that would separate them was never recorded. ⚠ **This
paragraph previously ended "the merge is gated on a third condition that has not been run" — that
sentence is superseded by the fourth sitting below and by the stakes argument that follows it.**

### ⭐ FOURTH SITTING, later the same day — 36 confirmed on a better instrument

The owner viewed `#24242B` again on the **one-at-a-time** harness at
`http://radio:5002/shimmer-demo.html` — a single value revealed on a button press, nothing else on
screen — and said **"Shimmer 36 looks good."**

⭐ **That is a better instrument than the third sitting**, because it removes the side-by-side
reference and therefore **answers confound 2 directly**: 36 was judged sufficient with no brighter
value beside it to anchor against.

⛔ **It does NOT answer confound 1, and must not be written up as though it did.** Nobody recorded
the **ambient conditions** of either recent sitting. The earlier judgement — *"36 is below the
owner's dark-room visibility threshold"* — was made specifically **in a dark room**, and neither
recent sitting is known to have been. **Two sittings chose 36; one rejected it; the conditions of
the two are unrecorded and the instrument of the one was broken. Unresolved, not settled.**

### ⭐ Why this can proceed anyway — the stakes, not the evidence

**The shimmer is on screen for ~3–7 ms against a 1500 ms cycle, so no sweep is painted at any
value** (see the next section). **If 36 is too dim in a dark room, the consequence is that an
invisible element is marginally more invisible.** That is the honest reason a live disagreement
between two owner judgements does not need to block a one-token change: **the choice is very low
stakes until dwell is answered.**

⛔ **THE COROLLARY IS LOAD-BEARING AND MUST NOT BE LOST.** ⚠ **If dwell is ever fixed, the value must
be RE-JUDGED at that point.** That will be the first time anyone sees this shimmer in the product
rather than on a harness that holds it on screen artificially — and every sighting on record was made
on such a harness. **36 is settled-for-now on a surface nobody can currently see. It is not
settled-forever.** Any future row that changes skeleton dwell inherits this re-judgement.

⚠ The harness lives in `/opt/radio-console/web/wwwroot/` and `Deploy-ToLinux.ps1:271` wipes that
directory with `rsync --delete`; it is not in this repo and will not survive the next deploy.

### ⚠ The shipped hex is not the Designer's seed, and the record should not round them together

The seed of record is **`#242429` = rgb(36,36,41)** — the Designer answer's *"The change"* block
above (`:87` on `main` @ `f4d71b28`; moved since, and not re-quoted), the plan's Task 5a, and the
five-value ladder in [`NIGHT-SITTING.md`](../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md).
The panel the owner looked at this afternoon painted **`#24242B` = rgb(36,36,43)**: **+2 on blue**,
hue lean B−R **+7** rather than the Designer's **+5**. Two levels on one channel near black cannot
have changed the judgement — but **the night ladder and the afternoon panel were not the same
colour**, and the value being shipped is the one that was actually sighted. ⭐ Recorded because this
row has already been bitten twice by a label drifting from the quantity it named.

---

## ⭐ 2026-09-09 — the shimmer is effectively invisible in normal operation. Measured, not argued.

**This was never measured before, and it reframes the row.** ⛔ **Task 0 itself is still unrun — what
follows is a BOUND on it**, and an earlier draft of this section wrongly said *"it has now been
run."*

Two different thresholds are in play and they have been conflated before, so both are attributed
here:

| Source | Threshold | Method |
|---|---|---|
| **This row**, the Designer-answer verification list above | *"if thread-open latency is typically **<500 ms**, no shape completes a pass"* | informal |
| **The plan**, §1.3 / Task 0 | *"if real dwell time is materially under **750 ms**…"* | a `MutationObserver` on `.skeleton-loading` **DOM presence**, driven over CDP, with a three-band decision table |

⚠ **The `<500 ms` sentence is this file's, not the plan's** — an earlier draft attributed it to Task 0.
What was actually measured below is **endpoint latency**, which bounds how long those skeletons *can*
be up. It is **not** the `MutationObserver` Task 0 specifies.

**Measured on the box:**

| Endpoint | Response time |
|---|---|
| `/api/playhistory` | **0.0030 – 0.0051 s** |
| `/api/queue` | **0.0051 – 0.0067 s** |

**Those two panels load their data in 3–7 ms** against a **1500 ms** cycle. Taking the *slowest* time
measured, that is **under 0.5% of one cycle** (6.7/1500 = 0.45%; the fastest is 0.20%, the mean
0.32%). ⚠ **An earlier draft quoted "roughly 0.3%", which is the MEAN presented as a bound** — a page
rendering both panels is bounded by the slower one.

⛔ **On those two panels, in warm steady state, no sweep is painted at any highlight value.**

⚠ **THAT SENTENCE IS SCOPED ON PURPOSE, AND AN EARLIER DRAFT WAS NOT.** It said *"no sweep is **ever**
painted, at **ANY** highlight value"* — universal quantifiers over every surface and every condition,
asserted from **two warm endpoints, on one box, on one afternoon**, covering **2 of the 27 call sites
across 6 pages plus the 38 raw `.skeleton-loading` nodes** this row enumerates above. **Devices, Radio
and the phone thread are unmeasured, and a cold start or a genuinely slow query would dwell longer** —
in which case a sweep *is* painted and the highlight value does matter. The same paragraphs that made
the universal claim also listed the conditions that falsify it.

⭐ **Corroborated independently in the live kiosk**, over CDP on `:9223`:

```
{"url":"http://localhost:5002/","reduce":false,"noPref":true,"skeletons":0}
```

**Zero `.skeleton-loading` nodes on the home page** at the moment of sampling — a direct observation
of absence rather than an inference from latency.

⭐ **This is why sittings 3 and 4 needed a static demo page at all**: in the product the thing cannot
be looked at.

**Reduced motion was falsified as a cause**, not assumed — and falsified **directly**, which an
earlier draft did not do. It cited only `enable-animations: true`, the GNOME setting Chromium
*derives* the media query from: a reasonable proxy, but a proxy, and this row has been bitten by
proxies twice. The direct read, in the kiosk itself over CDP, is in the block above:
**`reduce:false`, `no-preference:true`.** So the `prefers-reduced-motion` block that overrides
`.skeleton-loading` — which would kill the animation outright — **is genuinely not firing**, and the
shimmer's absence is dwell, not the override.

⚠ **Cited by selector, not by line, and here is why.** `design-system.css` holds **five**
`@media (prefers-reduced-motion: reduce)` blocks, so a bare line number is doing real disambiguating
work — and it is also the first thing to rot. It is **`:1779` on `main` @ `f4d71b28`**. ⛔ **No branch
line number is quoted here as the citation:** the token's comment block sits above it, so every edit
to that comment shifts it, and it moved twice while this paragraph was being written (`:1805`, then
`:1817`). Same trap this file already records under "Line anchors: this row's, and the plan's, are all
stale." **A number that changes each time you touch the thing it documents should not be the
citation.**

> ⛔ **This paragraph said "six" until pre-merge review counted it. It is five.** `grep -c` for the
> bare string `prefers-reduced-motion` returns **seven** — five `@media` blocks plus two prose
> mentions (the token comment, and a scrollbar comment). ⭐ **The count was the entire stated reason
> for changing the citation style, and it was wrong** — a commit written to fix an unverified claim
> shipped a new one. Exactly the `GoogleCastOutput._lifecycleLock` shape `CLAUDE.md` § Pre-Merge
> Review warns about: *"the corrected comment's own first draft overclaimed in turn."*

### ⛔ THE RULE THIS ROW EARNED: in a file that is still being edited, cite a section, not a line

**Measured across this one change.** Every `file:line` anchor written into this row moved *while the
row was being written*, because the supersede banner and the new sections all insert above the things
they cite:

| Anchor | Written as | Then | Then | Now |
|---|---|---|---|---|
| reduced-motion block (`design-system.css`) | `:1779` | `:1805` | `:1817` | — |
| Designer's seed (`UX-1.md`) | `:87` | `:151` | `:154` | — |
| dark-room threshold (`UX-1.md`) | `:217` | `:287` | `:290` | — |

⭐ **Three anchors, moved three times, all self-inflicted, and pre-merge review caught the second
round after the first round had already been "fixed."** The fix that kept failing was *re-measuring
the number*; the fix that works is **not quoting a moving number at all.**

**So: quote only `main`'s anchor, which is stable and checkable, and otherwise cite by section title
or quoted phrase.** This file already carried a section titled *"Line anchors: this row's, and the
plan's, are all stale"* before any of this — the lesson had been written down and was still not
followed, which is why it is now a rule with its own heading.

⚠ **What this does and does not mean.** The value change is **cosmetically real** — it is the right
value for whenever a skeleton *is* on screen, e.g. a cold start, a slow network, or a genuinely slow
query. It is **operationally moot** on the two panels measured. ⛔ **Dwell is NOT being fixed in the
follow-up PR**, and the amplitude change must not be described as fixing it.

### ⛔ This trips the plan's §1.3 stop-gate, and the gate says report rather than choose

`design/plans/UX-1-the-shimmer-nobody-can-see.md` §1.3:

> If real dwell time is materially under 750 ms … the honest outcomes then are the two the Designer
> named — **make it visible** (by shortening the 1.5 s cycle …) or **stop paying for it** (delete the
> animation …). **Builder must stop and report in that case, not pick one.**

**3–7 ms is three orders of magnitude under 750 ms, so the gate is tripped.** ⭐ **This section is the
report §1.3 asks for; no remedy is being picked.**

⚠ **An earlier draft offered "a minimum-display floor, or removing the skeleton from panels this
fast" as "the honest options" — neither is one of the two §1.3 names**, and the first is a third
remedy invented here while citing §1.3's authority. It is recorded as an idea, not as a choice, and
**no follow-up row is filed by this change.**
