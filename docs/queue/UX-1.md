# UX-1 — Skeleton shimmer amplitude — is a 6/255 gradient delta enough on the dark theme?

> Queue dossier for row **`UX-1`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
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
| Plan | _plan TBD — **do not write one until the Designer has answered**; scope depends entirely on whether the answer is "new token," "retune the existing pair," or "leave it"_ |
| Spec / handoff | [GV-8 UAT `L-1`](../uat/2026-07-31-gv8-error-state/REPORT.md) · evidence: `uat/2026-07-31-gv8-error-state/screenshots/03-c2-frame-a-108ms.png` vs `04-c2-frame-b-224ms.png` |
| Depends on | — _(no code dependency; it is gated on a design answer, not on a row)_ |
| Branch | `feat/ux-skeleton-shimmer-amplitude` |

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

Full record: [`../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md`](../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md).

**Result: `56` (`#38383F`, delta 36 against the `#141416` base) is the dimmest value the owner can
clearly see in a dark room, AND it stays calm — no distraction.** Both questions answered.

**That is 6× today's shipping delta of 6, and 2.25× the Designer's seed of 36.**

### ⛔ The Designer's central claim is contradicted by the owner's eye

The answer that discharged this row's gate said **amplitude is the SMALLER half and geometry is the key
lever.** The owner could see only the *full-width shipping ramp* and found even that *"very dim and not
easy to see in a dark room."* **Geometry did not help; amplitude is the whole problem.** The seed of
**36 is below the owner's dark-room visibility threshold.**

⭐ To its credit the Designer explicitly refused to endorse a landing value — *"a seed for the A/B, not
an answer."* That caution was well placed and is why this was A/B'd rather than built.

### ⛔ The first harness was flawed, and the flaw is the instructive part

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
