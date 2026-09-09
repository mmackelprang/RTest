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
| Status | ✅ [#641](https://github.com/mmackelprang/RTest/pull/641) — shipped 2026-09-09; see the note at the foot of this file |
| Plan | [`UX-1-the-shimmer-nobody-can-see.md`](../../design/plans/UX-1-the-shimmer-nobody-can-see.md) — ⚠ **its geometry half is SUPERSEDED and carries banners saying so**. _Original cell: "plan TBD — do not write one until the Designer has answered; scope depends entirely on whether the answer is 'new token,' 'retune the existing pair,' or 'leave it'"_ |
| Spec / handoff | [GV-8 UAT `L-1`](../uat/2026-07-31-gv8-error-state/REPORT.md) · evidence: `uat/2026-07-31-gv8-error-state/screenshots/03-c2-frame-a-108ms.png` vs `04-c2-frame-b-224ms.png` · [night sitting](../uat/2026-09-08-ux1-shimmer-variants/NIGHT-SITTING.md) · [daylight sitting](../uat/2026-09-08-ux1-shimmer-variants/DAYLIGHT-SITTING.md) |
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

`.skeleton-loading` is at **`:1104-1112`** on `main` — not `:1099-1104` as this row (`:260`) and
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
