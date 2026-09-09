# PLAN — `UX-1` · The shimmer nobody can see: a new token, and the geometry that is doing most of the damage

> ## ✅ SHIPPED 2026-09-09 — but **NOT as this plan specifies**. Read this box before Task 5.
>
> **Phase A is complete and its outcome contradicted this plan's central premise.** What shipped is
> the **token change only**, at the **owner's** value of **`56` / `#38383F`** (delta 36), under the
> name `--skeleton-shimmer-highlight`.
>
> | This plan says | What shipped | Why |
> |---|---|---|
> | Task 5a: `--skeleton-shimmer: #242429` (36) | **`--skeleton-shimmer-highlight: #38383F`** (56) | 36 was the Designer's *seed*, and the night sitting put it **below the owner's dark-room visibility threshold**. The Designer explicitly declined to endorse a landing value. |
> | Task 5b: narrow the ramp to `35% / 50% / 65%` | ⛔ **not shipped — geometry unchanged** | See below. |
> | Task 6, second test: assert the `35%`/`65%` shoulders | ⛔ **not written** | It would pin a geometry that was never validated. |
>
> ⛔ **The geometry half is superseded, and re-reading §0.2 / §1.2 / §4.2 will not tell you that.**
> This plan's §7.5 named the falsifying outcome in advance — *"If V1 (geometry only) is invisible
> while V3 is obvious, the premise is wrong"* — and that is what the owner reported. Worse, the only
> experiment ever run on the narrowed geometry was the **`v1` harness, which was afterwards found
> unable to measure what it claimed.** **Geometry was never fairly tested, and `56` was chosen at the
> SHIPPING geometry, in a dark room and in daylight.**
>
> ⚠ **The published reason that harness failed is wrong and was corrected by measurement.**
> `NIGHT-SITTING.md` says the band was *"on the element only ~0.49 s"* and *"two-thirds of every
> cycle"* static; **the two figures are swapped** — that arithmetic counts one tile, but
> `background-repeat` defaults to `repeat`, so the 2W tile recurs and the band crosses twice per
> cycle. Measured in Chromium: **~1.01 s on, ~0.49 s off.** The real mechanism is `ease` — the
> animation declares no timing function, and the sweep decelerates to **2.1 %/s against a median of
> 221.7 %/s**, a ~390 ms dead stall at each cycle boundary plus ~110 ms mid-cycle, which a narrow
> band sits out entirely.
>
> ⚠ **Whether a steeper ramp would allow a *lower* amplitude is genuinely unknown** and is filed in
> [`design/FUTURE-WORK.md`](../FUTURE-WORK.md) § *Skeleton shimmer (`UX-1`)*, together with the
> **Task 0 dwell-time pre-check, which was never run.**
>
> ⚠ **Every line anchor in §0.2–§0.3 is stale** on `main` @ `143d678e`: `.skeleton-loading` is
> `:1104-1112` (not `:1099-1107`), `@keyframes shimmer` `:982-985` (not `:977-980`), the
> reduced-motion override `:1726-1729` (not `:1713-1724`).
>
> ⚠ **§4.2's automated gate is internally inconsistent.** It defines `S` as a *peak* windowed slope
> but calibrates V0 against **0.034**, which is the *average* slope (`6 / 178`). The peak windowed
> slope measured on the unfixed stylesheet is **0.052**, so Task 3's *"do not proceed past a V0 that
> disagrees"* would have halted a correct instrument. Both statistics are reported in the PR.
>
> ✅ **What held:** the mechanism in §0.2 (4W of travel per 1.5 s → one pass per 750 ms, no
> barber-pole), `.skeleton-loading` being defined in exactly one place, the `31` / `--surface-separator`
> collision, `C-218` (reduced-motion replaces `background` wholesale — untouched), `C-219`/`C-220`
> (the token is declared, consumed, and given no `var()` fallback), `C-221` (the adjacent-pixel trap,
> **demonstrated** rather than cited: it moves only 1.3× where the real change is 6×), `C-223`, and
> §7.3's single-`:root` finding, and `C-215`'s correction of the phone-panel node count (12, not 38 —
> re-verified: 26 + 8 + 4 across `Skeleton.razor` and the two panels). ⚠ **§0.3's `--surface-overlay`
> census is right for the stylesheet and incomplete for the app** — `PlayHistoryPage.razor:207,272,296`
> consume the token inline, so the true blast radius was **22** other consumers, not 19.

> **Row:** `UX-1`, [`docs/queue/UX-1.md`](../../docs/queue/UX-1.md). 📋 queued, `_plan TBD — do not write
> one until the Designer has answered_`.
> **Branch:** `feat/ux-skeleton-shimmer-amplitude` (the row names it).
> **Depends on:** — no code dependency. The row was gated on a design answer; **that gate is
> DISCHARGED** (Designer, 2026-09-07, recorded in the row).
> **Estimate:** **0.5 d of Builder time across two sittings, plus one ~30-minute owner session on the
> panel, in the dark room.** §0.6. The calendar dependency is the real cost, not the code.
> **Spec:** [GV-8 UAT `L-1`](../../docs/uat/2026-07-31-gv8-error-state/REPORT.md) (`:282-297`) ·
> evidence `screenshots/03-c2-frame-a-108ms.png` vs `04-c2-frame-b-224ms.png` · the Designer answer in
> [`docs/queue/UX-1.md`](../../docs/queue/UX-1.md) `:42-169`.
> **Planned against** `main` at **`a529ccf7`**. ⚠ Read at working-tree `c5a2ff7d`
> (`fix/gv-texts-polish-overflow-unread-align`), but **every anchor in this plan is byte-identical on
> both** — that branch's only edits to this stylesheet are at `:5962+`, ~4,200 lines below the lowest
> anchor here. Verified by hunk range, not assumed (§0.3).
> **Design gate:** ✅ discharged. **This plan consumes the Designer's answer and does not re-open it.**
> Every number in it was independently re-derived and **all of them check out** — §0.4 says so
> explicitly, because the brief invited a challenge and the honest answer is that there isn't one.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

The skeleton shimmer **works** and is **invisible**, and those are not in tension. `.skeleton-loading`
animates a `linear-gradient` whose two stops are `--surface-raised` `rgb(20,20,22)` and
`--surface-overlay` `rgb(26,26,29)` — a 6/255 delta — smeared across a full element width, so no
scanline in any frame contains an edge, a peak, or anything else the visual system is built to detect.
The GV-8 UAT proved the animation runs three independent ways and still recorded that the two captured
frames "read as identical to the eye." The Designer's finding, which reframes the row, is that
**amplitude is the smaller half**: the fix is a new `--skeleton-shimmer` token *and* a narrowing of the
gradient ramp from 50% of the tile to 15%, together worth about **9×** in local gradient steepness.
This is a design-token change landing on every skeleton in the app at once, and its success criterion
is perceptual — which is why §4 is the longest section here and why §7.4 declines auto-merge.

### 0.2 The mechanism, with the arithmetic checked

`.skeleton-loading` (`design-system.css:1099-1107`) is the shimmer primitive:

```css
.skeleton-loading {
  background: linear-gradient(90deg,
    var(--surface-raised) 0%,
    var(--surface-overlay) 50%,
    var(--surface-raised) 100%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
}
```

driven by `@keyframes shimmer` (`:977-980`), `background-position: -200% 0` → `200% 0`.

**The geometry, derived rather than quoted.** With `background-size: 200%` the tile is `2W` for an
element of width `W`. A percentage `background-position` of `P` places the tile's left edge at
`P × (W − 2W) = −P·W`, so the tile sweeps from `+2W` to `−2W` — **`4W` of travel per 1.5 s**.
`background-repeat` is unset and therefore `repeat`, so highlight peaks recur every `2W`; `4W` of
travel past a `W`-wide window yields **two peaks per cycle, i.e. one pass per 750 ms**, and because the
`2W` spacing exceeds the `W` window **at most one band is ever on screen** — no barber-pole. Both of
the Designer's claims on this point are correct.

**Why it is invisible.** The ramp from base to peak occupies 50% of a `2W` tile — exactly `W`, one full
element width. On the real 178 px `.skeleton-text` measured in the UAT, 6 levels spread over 178 px is

> **0.034 levels/px** — a staircase of six 1-level steps about 30 px apart.

A frame is a *monotonic wash* with no peak and no edge inside the element. An edgeless wash is the
single hardest stimulus for human vision, which is the same reason smooth gradients hide banding. The
6/255 is not itself trivial — linear luminance goes `0.00699 → 0.01033`, a **1.48× step, 48% Weber
contrast**, and a hard *edge* at that contrast would be plainly visible. There is simply no edge.

**What the fix does.** Amplitude `6 → 16` levels (2.67×) and ramp `50% → 15%` of the tile (3.33×)
multiply to **~8.9×**: `0.034 → ~0.30 levels/px`. The Designer's "roughly 9×" is right.

### 0.3 Anchor drift — the row's numbers are stale, the Designer's are exact

Re-read line by line at `c5a2ff7d`, and confirmed unchanged from `a529ccf7` by
`git diff -U0 main` on this file, whose only hunks are `@@ -5961,0 +5962,11 @@` and
`@@ -5964,0 +5976 @@` — both far below everything here.

| Source | Anchor claimed | Actual | Verdict |
|---|---|---|---|
| **Row** `:28` | `.skeleton-loading` at `:1075-1083` | **`:1099-1107`** | **moved +24** |
| **Row** `:36` | reduced-motion override at `:1683` | **`:1713-1724`** | **moved +30** |
| **Row** `:34` | `--surface-overlay` + `backdrop-filter` at `:184` | `.surface-overlay { … blur(20px) }` | ✅ **exact** |
| **Row** `:28` | tokens at `:65` / `:67` | `--surface-raised` `:65`, `--surface-overlay` `:67` | ✅ **exact** |
| **Designer** `:84` | surface block `:65-70` | `── Primary Surfaces ──` `:63-70` | ✅ **exact** |
| **Designer** `:123` | reduced-motion `:1713-1724` | exact, including the `rgba(255,255,255,0.05)` fill | ✅ **exact** |
| **Designer** `:71` | `--surface-overlay` in **19 other rules** | 20 `var(--surface-overlay)` occurrences; one is the shimmer at `:1102` → **19** | ✅ **exact** |
| **Designer** `:95` | 31 collides with `--surface-separator` `#1F1F22` | `:68` — `#1F1F22` = `(31,31,34)` | ✅ **exact** |
| **Designer** `:117-118` | `.skeleton-art` min-height 180 px; `.skeleton-progress` 4 px | `:1150-1154`, `:1156-1160` | ✅ **exact** |
| **Row** `:30` | 27 `<Skeleton>` call sites across 6 files | 15+5+4+1+1+1 = **27**, six files | ✅ **exact** |
| **Row** `:30` | **"38 raw `.skeleton-loading` nodes in the phone panels"** | 38 is the **whole-repo** total; only **12** are in phone panels | ❌ **wrong — `C-215`** |

**Every substantive claim survives.** The delta is still 6/255, the ramp is still a full element width,
the token is still shared with 19 other rules, and `.skeleton-loading` is still defined in exactly one
place. Builder should work from this table, not from the row's `§Detail` numbers.

### 0.4 The Designer's answer, checked rather than assumed

The brief asks me to say so if I think the Designer is wrong. **I do not.** Every quantitative claim
was re-derived independently and each one is correct:

| Claim | Re-derived | ✓ |
|---|---|---|
| 6/255 → linear `0.00699 → 0.01033`, **1.48×**, 48% Weber | sRGB EOTF, `((c/255+0.055)/1.055)^2.4` | ✅ |
| Luminance ladder 26 = 1.48× · 31 = 2.0× · 36 = 2.5× · 40 = 3.0× | 0.01033 / 0.01370 / 0.01764 / 0.02121 over 0.00699 → 1.48 / 1.96 / 2.52 / 3.03 | ✅ |
| `#242429` = (36,36,41); hue lean B−R **+5** vs +2 raised, +3 overlay | `0x24`=36, `0x29`=41; `#141416` +2, `#1A1A1D` +3 | ✅ |
| 31 is identical to `--surface-separator` | `#1F1F22` = (31,31,34) at `:68` | ✅ |
| ramp 50% → 15% is **3.3× steeper** | `0.50/0.15` = 3.33 | ✅ |
| combined **~9×**, `0.034 → ~0.30 levels/px` | 2.67 × 3.33 = 8.9; 16 / (0.15 × 2 × 178) = 0.30 | ✅ |
| 0.034 levels/px on a real 178 px bar | 6 / 178 | ✅ |
| one pass per 750 ms, unchanged; no barber-pole | derived in §0.2 | ✅ |
| `--surface-overlay` has 19 other consumers | counted, §0.3 | ✅ |
| reduced-motion premise in the row is outdated | `:1713-1724` replaces `background` wholesale | ✅ |

**One refinement, stated rather than applied silently** (`C-217`): the Designer says to add the token
"beside the surface block at `:65-70`", while the same paragraph argues the shimmer highlight is *a
motion value, not a surface* — which is the whole reason it must not borrow `--surface-overlay`.
Dropping it unlabelled into `── Primary Surfaces ──` contradicts that reasoning. Task 5 places it
**immediately after** the surfaces group under its own sub-comment: adjacent enough that a reviewer can
see the 20 / 26 / 31 / 36 ladder in one screen — which is what makes the `--surface-separator`
collision checkable — but labelled as what it is. This is a placement refinement, not a change to the
answer.

**Three corrections to the row and the Designer's supporting prose.** None changes the fix.

1. ❌ **`C-215` — the row's "38 raw `.skeleton-loading` nodes in the phone panels" is wrong, and wrong
   in a way that inflates the blast radius.** 38 is the repo-wide count. It breaks down as
   `Skeleton.razor` **26**, `PhoneTextsPanel.razor` **8**, `PhoneMessagesPanel.razor` **4** — so **12**
   are in the phone panels. The other 26 are *inside the component the 27 call sites mount*, and they
   are six mutually-exclusive `switch` branches (`Skeleton.razor:23-77`): one `<Skeleton>` renders
   **3–6** nodes (ListRow 3, NowPlaying / DeviceRow / MetricTile 4, Visualizer 5, Radio 6), never 26.
2. ❌ **`C-216` — the Designer's "up to ~65 nodes" is `27 + 38`, which double-counts.** The call sites
   *are* what render those nodes; adding the two is not a quantity. A comparable figure is reachable by
   a different route — `DeviceManagementPage.razor` holds **15** `<Skeleton>` call sites, and 15
   DeviceRow shapes is 60 concurrent animated nodes — so the conclusion it supports survives. But the
   number appears inside the argument against the *rejected* "leave it" option, so it is likely to be
   quoted onward; it should be quoted as "up to ~60 on the densest page", derived that way.
3. ⚠ **The brief's "2/255 peak" and the report's "3/255" are both right and are not a contradiction.**
   `REPORT.md:288-289` records **3/255** as the *measured* peak frame-to-frame change. ~2/255 is what
   the geometry *predicts* for a 116 ms interval: the band travels `0.31W` in 116 ms across a ramp of
   width `W` carrying 6 levels, so `0.31 × 6 ≈ 1.9`. Measurement and theory agreeing to within a level
   at this scale is corroboration. Use **3/255** as the documented baseline.

### 0.5 Reachability — what a human can actually see change

| | |
|---|---|
| **Reachable in production?** | **Yes, everywhere.** 27 `<Skeleton>` call sites across 6 pages plus 12 raw nodes in the two phone panels. Every initial-load state in the app. |
| **Reachable *for long enough*?** | ⚠ **Unknown, and it can moot the row.** One pass is 750 ms. If skeletons typically dwell <750 ms nothing completes a pass and the block looks static at *any* amplitude. **Task 0 measures this before anything else is built.** |
| **Behaviour change?** | None. Paint-only. No markup, no component, no API, no state. |
| **Reduced-motion users?** | Unaffected — `:1721-1724` replaces `background` with a flat `rgba(255,255,255,0.05)`, so the gradient never renders in that state (§0.7 `C-218`). |

### 0.6 The estimate

**0.5 d of Builder time, in two sittings, plus one ~30-minute owner session on the panel.**

| | |
|---|---|
| Task 0 — dwell-time pre-check (can moot the row) | 30 min |
| Task 1 — five-variant comparison harness | 45 min |
| Task 2 — the windowed-slope measurement script | 45 min |
| Task 3 — serve on the box, capture, measure | 45 min |
| Task 4 — **owner** A/B, from the chair, dark room | 30 min *(owner, scheduled)* |
| Task 5 — land the chosen variant (the actual CSS edit) | 10 min |
| Task 6 — the two guard tests | 30 min |
| Task 7 — gates, docs, PR body with evidence | 60 min |

**The code is ten minutes. Everything else is proving it worked** — which is the correct ratio for a
change whose entire value is perceptual and whose landing value the Designer rates LOW-TO-MODERATE
confidence.

**What pushes it out:** Task 0 reporting sub-750 ms dwell (the row changes shape entirely, §1.3), or
the owner rejecting all four candidate values at Task 4.

### 0.7 Constraints found while planning — numbering continues from `C-213`

> ⚠ **Numbering collision, pre-existing and not caused here.** `GV-9` and `UI-7` **both** say
> "numbering continues from `C-202`" and **both** allocate `C-203`–`C-213` to different constraints
> (`UI-7 §0.2` is `C-203` "the row's central premise is false"; `GV-9 §0.7` is `C-203` "the row's line
> numbers are stale"). The namespace is already ambiguous. This plan starts at **`C-214`** to avoid a
> third collision; whoever reconciles the two should know the overlap exists.

**`C-214` — no test in this repository can see this change, and none can be made to.** bUnit renders a
DOM, not a layout: no computed style, no box model, no gradient rasterisation, no animation clock.
`GV-9` established this as `C-209` for a layout rule and it is *more* true here — the thing under test
is a rasterised colour ramp. Task 6's tests pin the **CSS source text**, which is a real regression gate
(they fail on today's file) but is **not** evidence that anything is visible. Do not let a green suite
be cited as UAT for this row.

**`C-215` — the row's phone-panel node count is wrong.** §0.4 item 1. 12, not 38.

**`C-216` — the Designer's "~65 nodes" double-counts.** §0.4 item 2. Say "up to ~60 on
`DeviceManagementPage`" if the figure is needed at all.

**`C-217` — the token goes *after* the surfaces group, not inside it.** §0.4. It is a motion value; the
Designer's own argument says so.

**`C-218` — the reduced-motion override cannot be made worse, and must not be "improved".**
`:1713-1724` sets `background: rgba(255,255,255,0.05)` — an opaque replacement, not a modification —
so the gradient is never painted in that state. The row's constraint at `:36` ("widening the amplitude
must not make that state worse") is **satisfied by construction and needs no work**. ⛔ Do not add a
`--skeleton-shimmer` reference to that block to "keep it in sync": it would re-introduce a gradient
into the one state that deliberately has none.

**`C-219` — a new token that is consumed but never declared fails SILENTLY, and this file already has
one.** `--signal-red-glow` is used in a live `box-shadow` at `:5432` and **is never declared in
`:root`** — the comment at `:5378-5381` documents the trap and the glow renders as nothing today. A
misspelled or misplaced `--skeleton-shimmer` would behave identically: `var()` with no fallback
resolves to nothing, the gradient stop is dropped, and the result looks like "the fix didn't work" —
indistinguishable from the bug being fixed. Task 6's first test exists for exactly this.

**`C-220` — do NOT give `var(--skeleton-shimmer)` a fallback value.** `var(--skeleton-shimmer, #242429)`
would paper over `C-219` and make the declaration untestable. One declaration, no fallback, one test.

**`C-221` — naive adjacent-pixel differencing CANNOT distinguish fixed from unfixed, and would produce a
false pass.** At 0.034 levels/px the ramp is quantised into six 1-level steps ~30 px apart, so
`max |v(x+1) − v(x)|` is **1.0 levels/px** on today's code — numerically *larger* than the 0.30 the fix
targets. Any slope metric must be windowed. §4.2 specifies `±8 px`. This is the row's own failure mode
(a measurement that says "fine" about something invisible) reappearing in the instrument built to catch
it, and it is the single most likely way for this cycle to certify a broken result.

**`C-222` — CDP screenshots measure the signal, not the panel.** `Page.captureScreenshot` returns the
renderer's framebuffer. It answers "did the gradient change as designed" and **cannot** answer the
Designer's stated crux — the panel's unmeasured transfer function at code values 20–40. Only a camera
from the viewing position answers that. §4.4 keeps the two instruments and the two questions separate.

**`C-223` — the harness is a box-side artifact and must never be committed.** It is served from the
deployed `wwwroot` on `radio` and deleted afterwards. It is not a repo file, it is not a test, and it
must not end up in the PR diff.

**`C-224` — CSS-only, so the SHA gate cannot see it, but the cache no longer hides it.** `OPS-5` set
`Cache-Control: no-cache` on everything `UseStaticFiles` serves, so a CSS change now reaches the panel
on revalidation. **"It didn't work because of a stale cache" is no longer an available explanation** —
if the panel looks unchanged after a verified deploy, the change is genuinely not working. Confirm with
`curl -sI http://radio:5002/css/design-system.css | grep -i cache-control` → `no-cache`.

### 0.8 Things Builder must NOT do

- ⛔ **Do not re-open the design question.** The gate is discharged. If the A/B rejects all four values,
  stop and report — do not invent a fifth.
- ⛔ **Do not land `31` / `#1F1F22`.** It is byte-identical to `--surface-separator` (`:68`). It is on
  the ladder as a *rung to measure*, not a candidate to ship.
- ⛔ **Do not touch `--surface-overlay`.** 19 other rules, including `backdrop-filter: blur(20px)`,
  modals, bubbles, hover states and three `color-mix` recipes. This is the entire reason the row exists
  as a token decision.
- ⛔ **Do not add a second colour token per shape.** If `.skeleton-art` reads as too assertive the lever
  is a fixed-px `background-size` on that one shape (§5.2).
- ⛔ **Do not touch the reduced-motion block.** `C-218`.
- ⛔ **Do not add a `var()` fallback.** `C-220`.
- ⛔ **Do not report `animationPlayState`, `Animation.currentTime`, or a whole-frame percent-of-pixels-
  changed figure as evidence this row is fixed.** All three were already true before the fix — they are
  what produced the false PASS in GV-8 C2. §4.1.
- ⛔ **Do not chase `--signal-red-glow`.** Real, live, out of scope. §5.3.

---

## 1. Decision — measure first, land second

### 1.1 Why this is two phases and not one task

The Designer is explicit on both points: **"Test the free geometry change before the token change,"**
so that "leave the tokens alone" stays a live outcome; and **"Confidence: HIGH that 6/255 is too low.
LOW-TO-MODERATE that 36 is the right landing value. The number is a seed for the A/B, not an answer."**

A plan that opens by editing `:65` and `:1099` has quietly converted a seed into a decision. So:

- **Phase A (Tasks 0–4) changes no source.** It measures dwell time, builds a five-variant harness,
  measures the rendered signal, and puts the variants in front of the owner on the panel.
- **Phase B (Tasks 5–7) lands whichever variant won**, with the measurement as the PR's evidence.

### 1.2 The five variants

All five animate **simultaneously** in one view — sequential A/B across deploys is useless for a
10-level judgement.

| | Geometry | Highlight | Δ | Predicted slope | Role |
|---|---|---|---|---|---|
| **V0** | today, 50% ramp | `--surface-overlay` (26) | 6 | 0.034 | **control** — must look like today |
| **V1** | 15% ramp | `--surface-overlay` (26) | 6 | 0.113 | **the free change.** If this is enough, no token ships |
| **V2** | 15% ramp | (31) — 2.0× | 11 | 0.206 | ladder rung only — ⛔ never lands (`--surface-separator`) |
| **V3** | 15% ramp | **`#242429` (36)** — 2.5× | 16 | 0.300 | **the Designer's pick** |
| **V4** | 15% ramp | (40) — 3.0× | 20 | 0.375 | upper bound; the "does it pull the eye" check |

**V1 is the one the row's outcome turns on.** It costs no token, touches no palette, and is a 3.3×
improvement on its own. If the owner can see V1 from the chair, the correct outcome is to ship V1 and
close the row without `--skeleton-shimmer` at all — a strictly smaller change than the one the Designer
recommended, reached by the Designer's own stated method.

### 1.3 What Task 0 can do to this plan

If real dwell time is materially under 750 ms, **no variant completes a pass** and the whole ladder is
moot. The honest outcomes then are the two the Designer named — *make it visible* (by shortening the
1.5 s cycle, which is a **new** design question and a new row) or *stop paying for it* (delete the
animation, keep a flat block). Builder must **stop and report** in that case, not pick one.

---

## 2. Tasks

### Task 0 — measure real skeleton dwell time *(the pre-check that can moot the row)*

**Changes nothing.** On the box, with the kiosk up and CDP live on `:9223` (`CLAUDE.md` § *Remote UI
driving*; the dedicated `--user-data-dir` profile is what makes the port work on Chrome 151).

```bash
ssh mmack@radio "curl -sf http://localhost:9223/json/version"   # must return browser JSON
```

Instrument the skeleton's lifetime directly with a `MutationObserver`, evaluated over CDP against the
kiosk page:

```js
// Records how long each .skeleton-loading node stays in the DOM.
// One shimmer pass is 750 ms; anything materially below that never completes one.
(() => {
  const born = new WeakMap();
  const lives = [];
  const seen = n => { if (n.nodeType === 1) { if (n.matches?.('.skeleton-loading')) born.set(n, performance.now());
                      n.querySelectorAll?.('.skeleton-loading').forEach(k => born.set(k, performance.now())); } };
  const died = n => { if (n.nodeType === 1) { if (born.has(n)) lives.push(performance.now() - born.get(n));
                      n.querySelectorAll?.('.skeleton-loading').forEach(k => born.has(k) && lives.push(performance.now() - born.get(k))); } };
  new MutationObserver(ms => ms.forEach(m => { m.addedNodes.forEach(seen); m.removedNodes.forEach(died); }))
    .observe(document.body, { childList: true, subtree: true });
  document.querySelectorAll('.skeleton-loading').forEach(n => born.set(n, performance.now()));
  window.__skelLives = lives;
  return 'observing';
})()
```

Then drive the app through the surfaces that actually show skeletons — **Devices** (15 call sites),
**Play History**, **Radio**, and a phone text thread — and read back:

```js
JSON.stringify({ n: __skelLives.length,
                 min: Math.min(...__skelLives) | 0,
                 median: __skelLives.slice().sort((a,b)=>a-b)[__skelLives.length>>1] | 0,
                 max: Math.max(...__skelLives) | 0,
                 under750: __skelLives.filter(v => v < 750).length })
```

**Record the result in the PR body whatever it says.** Decision rule:

| Median dwell | Action |
|---|---|
| **≥ 1500 ms** | Proceed. Two or more passes complete; the ladder is meaningful. |
| **750–1500 ms** | Proceed, and note in the PR that most shapes complete one pass and no more. |
| **< 750 ms** | ⛔ **Stop.** §1.3 — report to the owner, do not pick a remedy. |

### Task 1 — the five-variant comparison harness

**A box-side artifact. Not a repo file** (`C-223`). Write it to the scratchpad, `scp` it into the
deployed `wwwroot`, open it in the kiosk, delete it afterwards.

It links the **real deployed stylesheet** so V0 is genuinely today's code, and overrides only what each
variant changes. Each row carries all three extremes in one view — a 178 px `.skeleton-text` (the shape
the UAT actually measured), a `.skeleton-art` (min-height 180 px, the largest moving area) and a
`.skeleton-progress` (4 px, where the Designer predicts it stays near-invisible and says that is
acceptable).

```html
<!doctype html>
<meta charset="utf-8">
<title>UX-1 shimmer ladder</title>
<link rel="stylesheet" href="/css/design-system.css">
<style>
  /* Harness chrome only. The variants below override nothing except the
     gradient under test, so V0 is the deployed stylesheet untouched. */
  body { background: var(--surface-base); color: var(--text-high);
         font: 13px/1.4 system-ui, sans-serif; margin: 0; padding: 24px; }
  .lane { display: grid; grid-template-columns: 92px 178px 1fr 220px;
          gap: 16px; align-items: center; margin-bottom: 20px; }
  .lane > b { font-weight: 600; letter-spacing: .04em; }
  .art { height: 180px; border-radius: 8px; }
  .prog { width: 220px; }

  /* ── V1..V4: geometry is identical, only the highlight stop differs. ──
     Ramp 35%→50%→65% of a 2W tile = 15% of the tile, vs V0's 50%.
     background-size stays 200% so the pass cadence is unchanged (750 ms). */
  .v1 .skeleton-loading, .v2 .skeleton-loading,
  .v3 .skeleton-loading, .v4 .skeleton-loading {
    background: linear-gradient(90deg,
      var(--surface-raised)   0%,
      var(--surface-raised)  35%,
      var(--hl)              50%,
      var(--surface-raised)  65%,
      var(--surface-raised) 100%);
    background-size: 200% 100%;
  }
  .v1 { --hl: #1A1A1D; }  /* 26 — today's amplitude, geometry only  */
  .v2 { --hl: #1F1F22; }  /* 31 — 2.0x  ⛔ ladder rung only          */
  .v3 { --hl: #242429; }  /* 36 — 2.5x  the Designer's pick          */
  .v4 { --hl: #282830; }  /* 40 — 3.0x  upper bound                  */
</style>

<div class="lane v0"><b>V0 today</b>
  <div class="skeleton-loading skeleton-text" id="m-v0"></div>
  <div class="skeleton-loading art"></div>
  <div class="skeleton-loading skeleton-progress prog"></div></div>

<div class="lane v1"><b>V1 geom only</b>
  <div class="skeleton-loading skeleton-text" id="m-v1"></div>
  <div class="skeleton-loading art"></div>
  <div class="skeleton-loading skeleton-progress prog"></div></div>

<div class="lane v2"><b>V2 &nbsp;31</b>
  <div class="skeleton-loading skeleton-text" id="m-v2"></div>
  <div class="skeleton-loading art"></div>
  <div class="skeleton-loading skeleton-progress prog"></div></div>

<div class="lane v3"><b>V3 &nbsp;36</b>
  <div class="skeleton-loading skeleton-text" id="m-v3"></div>
  <div class="skeleton-loading art"></div>
  <div class="skeleton-loading skeleton-progress prog"></div></div>

<div class="lane v4"><b>V4 &nbsp;40</b>
  <div class="skeleton-loading skeleton-text" id="m-v4"></div>
  <div class="skeleton-loading art"></div>
  <div class="skeleton-loading skeleton-progress prog"></div></div>
```

⚠ **The five `.skeleton-text` bars must be exactly 178 px** (the grid column pins them) so the measured
slope is directly comparable to the UAT's 0.034 baseline. If the column resolves to anything else,
record the actual width and rescale the thresholds in §4.2 proportionally — the threshold is a slope,
and slope depends on element width.

⚠ **All five lanes start their animation at page load**, so they are phase-aligned and the eye can
compare them. Do not stagger them with `animation-delay`.

Deploy and open:

```bash
scp <scratchpad>/ux1-ladder.html mmack@radio:/opt/radio-console/web/wwwroot/ux1-ladder.html
ssh mmack@radio "curl -sI http://localhost:5002/ux1-ladder.html | head -1"   # expect 200
```

Point the kiosk at it over CDP (`Page.navigate` to `http://localhost:5002/ux1-ladder.html`), so it is
rendered by the **kiosk Chrome on the actual panel at 1920×720** — not a laptop, not a scaled window.

### Task 2 — the windowed-slope measurement script

**The instrument that has to be right** (`C-221`). Run on the captured PNGs.

```python
#!/usr/bin/env python3
"""UX-1: measure whether the shimmer produces a DETECTABLE spatial gradient.

Why windowed, and not adjacent-pixel differencing:
  Today's ramp is 6 levels over 178 px = 0.034 levels/px, which 8-bit
  quantisation renders as six 1-level steps ~30 px apart. max|v(x+1)-v(x)|
  is therefore 1.0 levels/px on the UNFIXED code -- numerically LARGER than
  the 0.30 the fix targets. A naive difference metric reports the broken
  state as better than the fixed one. The +/-8 px window straddles at least
  half a step at today's spacing and averages the staircase back into the
  slope it approximates.

  Rows are averaged over the element's height first: Chrome dithers shallow
  gradients by +/-1 level, and averaging 16 rows cuts that noise ~4x.
"""
import sys
from PIL import Image

HALF_WIN = 8  # px each side; window is 16 px wide


def slope_profile(png, box):
  """box = (x0, y0, x1, y1) of ONE .skeleton-loading element, in image px."""
  x0, y0, x1, y1 = box
  im = Image.open(png).convert("RGB").crop(box)
  w, h = im.size
  px = im.load()

  # Column means of the R channel (R == G in every token on this ladder).
  col = [sum(px[x, y][0] for y in range(h)) / h for x in range(w)]

  slopes = [
    abs(col[x + HALF_WIN] - col[x - HALF_WIN]) / (2 * HALF_WIN)
    for x in range(HALF_WIN, w - HALF_WIN)
  ]
  return {
    "width_px": w,
    "peak_slope_levels_per_px": max(slopes),
    "amplitude_levels": max(col) - min(col),
    "min_level": min(col),
    "max_level": max(col),
  }


def temporal_peak(png_a, png_b, box):
  """Max per-pixel |A-B| inside the element. SECONDARY metric only -- this is
  the family of measurement that produced GV-8's false PASS."""
  a = Image.open(png_a).convert("RGB").crop(box).load()
  b = Image.open(png_b).convert("RGB").crop(box).load()
  w = box[2] - box[0]
  h = box[3] - box[1]
  return max(abs(a[x, y][0] - b[x, y][0]) for x in range(w) for y in range(h))


if __name__ == "__main__":
  frame_a, frame_b = sys.argv[1], sys.argv[2]
  # Boxes come from Task 3's getBoundingClientRect dump, one per variant.
  for name, box in eval(open(sys.argv[3]).read()).items():
    s = slope_profile(frame_a, tuple(box))
    s["temporal_peak_levels"] = temporal_peak(frame_a, frame_b, tuple(box))
    print(f"{name}: {s}")
```

Element boxes are read from the live page rather than guessed:

```js
JSON.stringify(Object.fromEntries(['v0','v1','v2','v3','v4'].map(v => {
  const r = document.getElementById('m-' + v).getBoundingClientRect();
  return [v, [Math.round(r.x), Math.round(r.y), Math.round(r.right), Math.round(r.bottom)]];
})))
```

⚠ **Capture at `deviceScaleFactor: 1` and lossless PNG.** A scaled or JPEG-compressed capture
resamples exactly the 1-level detail being measured. Confirm the dumped box width is **178**.

### Task 3 — capture on the box and measure

Two frames **116 ms apart** (matching the UAT's interval so the temporal number is comparable to its
3/255), via CDP `Page.captureScreenshot` against the kiosk showing the harness. Then run Task 2's
script over both.

Produce this table, which is the PR's quantitative evidence:

| Variant | peak slope (levels/px) | amplitude (levels) | temporal peak (levels) |
|---|---|---|---|
| V0 | *expect ≈0.034* | *≈6* | *≈3* |
| V1 | *expect ≈0.11* | *≈6* | *≈6* |
| V2 | *expect ≈0.21* | *≈11* | *≈11* |
| V3 | *expect ≈0.30* | *≈16* | *≈16* |
| V4 | *expect ≈0.38* | *≈20* | *≈20* |

⚠ **If V0 does not measure ≈0.034 / ≈6 / ≈3, the instrument is wrong and every other row is
worthless.** V0 is the calibration against a number three independent sources already agree on
(the UAT's measurement, the stylesheet's arithmetic, and §0.2's derivation). **Do not proceed past a
V0 that disagrees** — fix the instrument.

### Task 4 — the owner A/B *(owner, on the panel, in the dark room)*

The deciding gate. Builder sets it up and records the answer; **Builder does not score it.**

1. Kiosk showing the harness, **1920×720, on the box**.
2. From **the actual chair**: real distance, cabinet height, off-axis angle.
3. Then again **with the room dark**, the console's dominant condition.
4. Score **two separate questions per variant**, because they fail in opposite directions:
   - **(a) Looking straight at it — can you tell it moves?**
   - **(b) *Not* looking at it — does it stay calm, or does it pull the eye?**
   **(b) is what kills an over-tuned value**, and it is the question a designer at a desk never asks.
5. Check both extremes in one view: if the winner makes `.skeleton-art` assertive while
   `.skeleton-progress` still looks dead, that is the signal for the fixed-px fallback (§5.2).
6. **Photograph from the viewing position with manual, locked exposure on a tripod.** Auto-exposure
   lifts near-blacks and will lie. Use the photo only to compare variants *within one shot* — never to
   compare across shots (`C-222`).

**The lowest variant that passes both (a) and (b) wins.** If V1 passes, ship V1 and no token.

### Task 5 — land the chosen variant

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

> ⛔ **SUPERSEDED — see the banner at the top of this file.** 5a shipped with a different name and a
> different value (`--skeleton-shimmer-highlight: #38383F`, the owner's 56, not the Designer's seed of
> 36). **5b did not ship at all**; the geometry is unchanged. Do not implement either block below as
> written.

**5a — the token** *(skip entirely if V1 won)*. Insert after `:69` (`--border-subtle`), closing the
Primary Surfaces group — adjacent to the ladder, labelled as not a surface (`C-217`):

```css
  /* ── Motion values (not surfaces) ──
     UX-1. The skeleton shimmer's highlight stop. It is deliberately NOT
     --surface-overlay, which it borrowed until now: that token is a SURFACE,
     consumed by 19 other rules including .surface-overlay's backdrop-filter
     blur(20px), modals, bubbles, hover states and three color-mix recipes, so
     it is not free to move for a motion effect.

     36 sits on a LINEAR-LUMINANCE ladder against --surface-raised (20), not a
     code-value one: 26 = 1.48x, 31 = 2.0x, 36 = 2.5x, 40 = 3.0x. The ratio is
     what stays meaningful on a panel whose transfer function near black is
     unmeasured. Hue keeps the family's cool lean (B-R: +2 raised, +3 overlay,
     +5 here).
     ⚠ Do NOT "round" this to 31/#1F1F22 for the clean 2.0x rung -- that value
     is byte-identical to --surface-separator above, and two tokens at one
     value is how a system starts looking accidental. */
  --skeleton-shimmer:       #242429;
```

**5b — the geometry** *(always; this is the larger half)*. Replace `:1099-1107`:

```css
/* UX-1. Two changes, and the GEOMETRY is the bigger one.

   The shimmer was never broken -- GV-8's UAT proved it runs three ways
   (animationPlayState running, currentTime advancing, 14.5% of pixels changed
   between frames). It was UNDETECTABLE, which is a different defect. The old
   ramp ran base->highlight across 50% of a 2W tile, i.e. one full element
   width: on a real 178px bar that is 0.034 levels/px, a monotonic wash with no
   peak and no edge anywhere in frame. An edgeless wash is the hardest stimulus
   the visual system has -- the same reason smooth gradients hide banding.

   Narrowing the ramp to 35%->50%->65% confines it to 15% of the tile, 3.3x
   steeper, and costs nothing. With the new highlight (6 -> 16 levels, 2.7x)
   the local gradient goes 0.034 -> ~0.30 levels/px, roughly 9x.

   background-size stays 200%, which is load-bearing in two ways: the tile
   remains wider than the element so exactly one band is on screen (no
   barber-pole), and the 4W of travel per 1.5s still yields one pass per 750ms.
   Keyframes and duration are unchanged.

   ⚠ background-size is a PERCENTAGE, so geometry scales per element: the pass
   is 750ms for every shape but the moving AREA is not. .skeleton-art (180px
   min-height) is far more assertive than .skeleton-progress (4px), where this
   stays near-invisible -- accepted, that shape communicates by presence. If
   .skeleton-art ever reads as too much the lever is a fixed-px background-size
   on that ONE shape, never a second colour token. */
.skeleton-loading {
  background: linear-gradient(90deg,
    var(--surface-raised)    0%,
    var(--surface-raised)   35%,
    var(--skeleton-shimmer) 50%,
    var(--surface-raised)   65%,
    var(--surface-raised)  100%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
}
```

⛔ **`:1713-1724` is not edited.** `C-218`.

### Task 6 — the two guard tests

> ⛔ **SUPERSEDED — four tests shipped, not these two.** The first was renamed for the token's real
> name; **the second was NOT written**, because its `35%` / `65%` assertions would pin a geometry that
> was never validated (top banner). What shipped instead:
> `SkeletonShimmerToken_IsBothDeclaredAndConsumed`,
> `SkeletonShimmer_TakesItsHighlightFromItsOwnTokenAtTheJudgedGeometry`,
> `SkeletonShimmer_LeavesTheSharedSurfaceOverlayTokenWhereItWas` (the **control** — the shared token
> is a defect this plan never gated against, only warned about), and
> `ReducedMotion_StillReplacesTheShimmerWithAFlatFill` (`C-218`).
> ⚠ The `<remarks>` block below — itself an amendment applied by `UI-8` — was **not** shipped
> verbatim; the shipped version drops its "do not ship the previous wording" framing, which is an
> instruction to a Builder rather than a fact about the test.

**File:** `tests/Radio.Web.Tests/Configuration/StaticAssetPipelineTests.cs`

This class already fetches `/css/design-system.css` through the real pipeline and owns a working
`WebFactory`, so both tests drop in with no new fixture. Append inside the class, after
`StaticAssets_StillCarryAnETagToRevalidateAgainst` (`:54`):

```csharp
  /// <summary>
  /// UX-1 / <c>C-219</c>. A custom property that is CONSUMED but never DECLARED resolves to nothing:
  /// the gradient stop is silently dropped and the shimmer renders as a flat block — which looks
  /// exactly like the bug UX-1 fixed, with a green build and a green suite.
  /// </summary>
  /// <remarks>
  /// ⚠ AMENDED BY UI-8 (2026-09-09) — do not ship the previous wording, which is now false.
  /// It cited <c>--signal-red-glow</c> as a LIVE dangling consumer at design-system.css:5432.
  /// UI-8 DELETED that reference (and the two <c>--signal-green-glow</c> ones) rather than
  /// declaring the tokens, on an owner decision of NO GLOW, so this stylesheet now has no
  /// surviving example. The hazard is unchanged and so is the reason for this test: an undeclared
  /// custom property makes the whole declaration invalid at computed-value time, so the value is
  /// silently dropped. This test exists so <c>--skeleton-shimmer</c> cannot become the next one.
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmerToken_IsBothDeclaredAndConsumed()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    Assert.Contains("--skeleton-shimmer:", css);
    Assert.Contains("var(--skeleton-shimmer)", css);

    // No fallback (C-220): var(--skeleton-shimmer, #xxxxxx) would paper over a missing
    // declaration and make the assertion above unfalsifiable.
    Assert.DoesNotContain("var(--skeleton-shimmer,", css);
  }

  /// <summary>
  /// UX-1. Pins the GEOMETRY, which is the larger half of the fix and the half with no other gate.
  /// </summary>
  /// <remarks>
  /// ⚠ This asserts the CSS SOURCE TEXT, not that anything is visible — bUnit and this factory
  /// rasterise nothing (plan C-214). It fails on the pre-UX-1 stylesheet, which is what makes it a
  /// real regression gate rather than a tautology: the old rule had three stops (0/50/100) and a
  /// 50%-of-tile ramp. It cannot, and must not be read to, replace the on-panel UAT.
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmer_ConfinesTheHighlightRampToANarrowBand()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    var start = css.IndexOf(".skeleton-loading {", StringComparison.Ordinal);
    Assert.True(start >= 0, "the .skeleton-loading primitive is gone or was renamed");
    var rule = css[start..css.IndexOf('}', start)];

    // The 35%/65% shoulders are the whole point: they narrow the ramp from 50% of the
    // tile to 15%, a 3.3x steepening that costs nothing. Their absence is the regression.
    Assert.Contains("var(--surface-raised)   35%", rule);
    Assert.Contains("var(--skeleton-shimmer) 50%", rule);
    Assert.Contains("var(--surface-raised)   65%", rule);

    // The tile must stay wider than the element or a second band appears (barber-pole)
    // and the 750ms cadence changes.
    Assert.Contains("background-size: 200% 100%", rule);
  }
```

⚠ **Both assertions are whitespace-sensitive** because they match the aligned literals in Task 5b. If
Builder reformats that block, update the test to match — do **not** weaken it to a bare
`Contains("35%")`, which would pass against an unrelated percentage anywhere in the rule.

### Task 7 — docs, gates, PR

- `design/FUTURE-WORK.md` — **one entry**, only if Task 4 chose V1 (geometry only): record that the
  amplitude question was measured and deferred, with the ladder table, so it is not re-derived.
- `design/INTEGRATIONS.md` — no entry. No integration surface changes.
- `docs/queue/UX-1.md` + `docs/BUILDER_QUEUE.md` — §6. ⛔ **Not edited by this plan.**
- **PR body must carry**: Task 0's dwell numbers, Task 3's measured table including the V0 calibration
  row, the locked-exposure photograph, and an explicit sentence that the suite cannot see this change
  (`C-214`).

---

## 3. Ordering

**Task 0 first and alone** — it can end the row (§1.3).

Then **1 → 2 → 3 → 4** (Phase A, no source changes), a checkpoint with the owner, then **5 → 6 → 7**
(Phase B) on the branch.

Task 2 must be written **before** Task 3 captures anything, and Task 3 must **calibrate on V0** before
any other row is believed. Task 6 depends on Task 5's exact formatting. Task 5a is skipped entirely if
V1 wins, in which case Task 6's first test is skipped with it and the second one's `50%` assertion
becomes `var(--surface-overlay) 50%`.

---

## 4. Verification

### 4.1 What must NOT be offered as evidence

This row exists **because** a prior UAT measured the right things and drew the wrong conclusion. GV-8's
C2 was scored **PASS** on:

- `animationPlayState: running`;
- `Animation.currentTime` advancing 0 → 117 ms;
- `backgroundPosition` moving −200% → −174.531%;
- a screencast in which **14.5 % of pixels changed** between two frames 116 ms apart.

**All four are true of the unfixed code, and all four will be equally true after the fix.** They
establish that the animation runs — a question nobody is asking. ⛔ None of them may appear in this
row's evidence as proof of anything, and any UAT that reports them as a pass has reproduced the bug in
the instrument.

The one number in that report that *did* discriminate was the amplitude — **3/255 peak** — and it was
correctly filed as LOW rather than as a pass.

### 4.2 The automated gate — windowed spatial slope

**Primary metric:** peak windowed slope on a 178 px `.skeleton-text`, per Task 2.

```
S = max over x of |mean_col(x+8) − mean_col(x−8)| / 16     [levels/px]
```

| | V0 (today) | Pass threshold | V3 predicted |
|---|---|---|---|
| **peak slope** | 0.034 | **≥ 0.20 levels/px** | 0.30 |
| **amplitude** | 6 | **≥ 12 levels** | 16 |
| **temporal peak @116 ms** | 3 | **≥ 8 levels** | 16 |

**Why these thresholds.** The pass bar sits below every candidate that could ship (V2 0.21, V3 0.30,
V4 0.38) and roughly **6× above today's 0.034**, so it cannot be met by noise, by V0, or by the
geometry change alone at today's amplitude (V1, 0.11 — which is *deliberately* below the bar: V1 is a
legitimate outcome decided by the eye in §4.4, not by this gate). The amplitude and temporal rows are
corroboration, not independent gates — all three move together by construction.

⚠ **The window is not optional** (`C-221`). Adjacent-pixel differencing reports **1.0 levels/px** on
today's quantised staircase — better than the fixed target — and would certify the broken state.

⚠ **All three numbers describe the rendered signal and would be identical on a panel that displays
nothing** (`C-222`). They are **necessary and not sufficient**. Passing §4.2 means "the CSS does what
the Designer specified", not "a human can see it".

### 4.3 The regression gate

`dotnet test` — Task 6's two tests. They fail on the pre-UX-1 stylesheet, which is what makes them
worth having; they prove the **source** is right and nothing more (`C-214`).

```bash
dotnet build --configuration Release   # baseline 47 warnings / 0 errors — equality with baseline is the gate
dotnet test RadioConsole.sln -c Release > /tmp/ux1.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/ux1.log
```

⚠ **Never pipe `dotnet test` into `tail`** — `CLAUDE.md` records a run that exited `0` with five tests
failing. Read the per-project summary lines. Known-failing on Windows and **not** regressions: four
`SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`), `NwsObservationIntegrationTests.RealNwsCall_*`
and `CoverArtPipelineIntegrationTests.CoverArtArchive_*` (both live-network, `Category=Integration`,
CI-excluded).

Targeted:

```bash
dotnet test tests/Radio.Web.Tests -c Release --filter "FullyQualifiedName~StaticAssetPipelineTests"
```

### 4.4 The deciding gate — the eye, on the panel

**This is the gate.** §4.2 proves the signal changed as designed; only Task 4 answers whether the
change is *perceptible*, and only from the chair, in the dark room, on the 1920×720 panel. The
Designer's own crux — the panel's unmeasured transfer function at code values 20–40 — is unanswerable
any other way: if the panel crushes near-black, today's 20-vs-26 may deliver *literally zero*
difference, and no framebuffer measurement can reveal that.

**Pass = the owner answers (a) yes and (b) calm, for the lowest variant that does both.**

### 4.5 Deployment sanity

CSS-only, so the SHA gate is blind to it, but `OPS-5` means the cache is not an excuse (`C-224`):

```bash
curl -s  http://radio:5002/api/health/version                              # web is the build you think
curl -sI http://radio:5002/css/design-system.css | grep -i cache-control   # cache-control: no-cache
```

### 4.6 No timing dependency

`CLAUDE.md` § *Test Timing* is satisfied trivially: no task adds a timer, a `Task.Delay`, a poll or an
async rendezvous. Both new tests are single `GetStringAsync` calls with string assertions. Task 0's
`MutationObserver` is a **measurement instrument on the box, not a test**, and nothing gates on it
automatically.

### 4.7 Load

Paint-only; node count, animation count and duration are all unchanged, so it should be load-neutral.
Given the documented audio-distortion correlation on the N100, glance at the box with a skeleton-heavy
page (Devices, 15 call sites) up — but do not run heavy `journalctl` queries while doing it, which is
itself a distortion trigger.

---

## 5. Deliberately not done

### 5.1 Splitting the token per shape

The Designer is explicit and the CSS agrees: `background-size` is a percentage, so geometry is already
relative per element. A second colour token would decouple shapes that should stay one family.

### 5.2 A fixed-px `background-size` on `.skeleton-art`

The named lever if `.skeleton-art` (min-height 180 px) reads as too assertive while `.skeleton-progress`
(4 px) stays dead. **Not applied pre-emptively** — the Designer records that no captured evidence shows
a *wide* shape shimmering at all (the UAT frames contain only ~178 px bars and small chips), so the
`.skeleton-art` claims are derived from CSS geometry, not observed. Task 4 step 5 is where that gets
seen for the first time. Apply only if the owner asks.

### 5.3 `--signal-red-glow`

> ⚠ **CLOSED BY `UI-8`, 2026-09-09 — and the resolution was DELETE, not declare.** The paragraph
> below is kept as the record of the find, but every line citation in it is now stale and its
> "worth its own row" recommendation is discharged. The owner sighted the two call buttons on a
> blur ladder in daylight and chose **NO GLOW**, so `UI-8` deleted the three dead references and
> declared neither token. ⛔ **Do not chase, declare, or re-reference `--signal-red-glow` or
> `--signal-green-glow`** — see `docs/queue/UI-8.md` § *OWNER DECISION 2026-09-09*.

**A live, shipped, silent defect found while planning this row.** It is consumed in a real `box-shadow`
at `design-system.css:5432` and declared nowhere, so that glow renders as nothing today. Same class as
`C-219` and the reason Task 6's first test exists. **Out of scope** — it is an unrelated component, and
declaring the token would silently change a shipped surface that has been shipping without it. Worth
its own row; flagged, not chased.

### 5.4 The GV-8 frame-diff loose end

The Designer notes the selected message row (x≈158–1399, y≈363–425) also animates between the two
captured frames — (17,24,27)→(18,28,31), a 4–5/255 G·B change across 99.1% of that region, *larger than
the skeleton's own 3*. The UAT's static control sat ~5 px below it and correctly read 0%, so its
conclusion stands. Recorded so that anyone re-running a whole-frame diff on those files is not surprised
by a large `diff≥4` population unrelated to the skeleton. Nothing to fix.

### 5.5 Shortening the 1.5 s cycle

Only becomes a question if Task 0 reports sub-750 ms dwell, and then it is a **new design question** —
cadence, not amplitude or geometry — and belongs in a new row with a Designer answer of its own
(§1.3).

---

## 6. Queue row wording

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/UX-1.md`** — another agent is working
in this tree and queue files are edited concurrently. Wording for whoever applies it:

**`docs/BUILDER_QUEUE.md` § Queue, `UX-1` row — Plan cell**, replacing the `_plan TBD_` text:

> [`design/plans/UX-1-the-shimmer-nobody-can-see.md`](../design/plans/UX-1-the-shimmer-nobody-can-see.md) — 0.5 d + owner session

**`docs/queue/UX-1.md`, appended as a dated note** (do not rewrite the verbatim Detail):

> ✅ **PLANNED 2026-09-08 against `main` `a529ccf7`.** The 2026-09-07 Designer answer was re-derived in
> full and **every quantitative claim in it checks out** (plan §0.4) — it is consumed, not re-opened.
> ⚠ **Two of this row's own line anchors are stale**: `.skeleton-loading` is at `:1099-1107` (not
> `:1075-1083`) and the reduced-motion override at `:1713-1724` (not `:1683`); the Designer's anchors
> are all exact. Three corrections: (1) **the row's "38 raw `.skeleton-loading` nodes in the phone
> panels" is wrong** — 38 is the repo-wide total, only **12** are in phone panels, and the other 26 are
> six mutually-exclusive `switch` branches inside `Skeleton.razor` that render 3–6 nodes per instance
> (`C-215`); (2) the Designer's "~65 nodes" is `27 + 38` and double-counts, though ~60 is reachable on
> `DeviceManagementPage` by a different route (`C-216`); (3) the row's reduced-motion constraint is
> **satisfied by construction** and needs no work (`C-218`). The plan is two-phase — **measure on the
> panel, then land** — because the Designer rates the landing value LOW-TO-MODERATE confidence and says
> to test the free geometry change first; a geometry-only outcome that ships **no token at all** is a
> live result. ⛔ **Not auto-mergeable** (plan §7.4): no automated gate can judge it.

---

## 7. Self-review

### 7.1 Verified first-hand at `c5a2ff7d`, and confirmed identical on `main` `a529ccf7`

- Every anchor in §0.3, read out of the file.
- **That my anchors are unaffected by the concurrent GV-9 work**, via `git diff -U0 main` on this
  stylesheet: hunks at `@@ -5961,0 +5962,11 @@` and `@@ -5964,0 +5976 @@` only, ~4,200 lines below the
  lowest anchor here. Stated because reading a Builder's working tree and calling it `main` is exactly
  how a plan acquires anchors nobody else has.
- **`.skeleton-loading` is defined in exactly one place.** One gradient (`:1099`), one `@keyframes
  shimmer` (`:977-980`), and **no shape class overrides `background`** — checked across all 20 shape
  rules at `:1109-1237`. This is what makes the one-line token change genuinely land everywhere.
- The call-site census: `<Skeleton>` 15+5+4+1+1+1 = 27 across 6 files; `.skeleton-loading` 26+8+4 = 38
  across 3 files — the basis for `C-215`/`C-216`.
- `Skeleton.razor` read end to end: six `switch` branches, 3–6 nodes each, mutually exclusive.
- All 20 `var(--surface-overlay)` consumers, one of which is the shimmer → 19 others.
- `--signal-red-glow` used at `:5432`, declared nowhere — the basis for `C-219` and §5.3.
- `StaticAssetPipelineTests.cs` in full: it already fetches `/css/design-system.css` through a real
  `WebApplicationFactory<Program>`, so Task 6 needs no new fixture and its code compiles against the
  existing `_factory`.
- The single-theme finding (§7.3), and `App.razor:23`.
- `GV-9` and `UI-7` both allocating `C-203`–`C-213` — the basis for the collision note in §0.7.
- The GV-8 UAT's `L-1` text at `REPORT.md:282-297`, for the 3/255 baseline.

### 7.2 Derived, not measured — and what that costs

- **Every predicted number in §1.2 and §4.2 is arithmetic, not observation.** The 0.034 baseline is
  corroborated three ways (UAT measurement, stylesheet arithmetic, §0.2 derivation), which is why Task 3
  calibrates on V0 and refuses to proceed if V0 disagrees. The other four rows have no such
  corroboration and could be wrong.
- **Nothing was run.** No build, no test, no browser, no box. This is a planning session in a checkout
  another agent is working in.
- **The 178 px assumption.** Thresholds are slopes and scale with element width; Task 1 pins the width
  and Task 3 verifies it.
- **Chrome's dithering behaviour on shallow gradients is assumed, not measured.** Task 2 averages over
  element height to suppress it; if the V0 calibration comes back noisy, that is the first suspect.

### 7.3 ⚠ The brief's theme-parity requirement does not apply to this repo

The brief asks for dark-mode / theme parity, calling a token that works in only one theme a defect.
**There is no second theme here, and adding one for this token would itself be the defect.**

- Exactly **one** `:root` block in the entire stylesheet (`:49`).
- **No** `prefers-color-scheme`, **no** `data-theme`, no theme class, no toggle — across the whole
  `wwwroot/css` tree.
- `color-scheme: dark` is declared at `:57` with a comment explaining that it makes the UA render every
  native control dark.
- Radzen is pinned by a static `<link>` to `material-dark-base.css` (`App.razor:23`), deliberately, so
  the dark theme applies on first paint.

So `--skeleton-shimmer` needs exactly one declaration. A light-theme variant would be an **unreachable**
declaration — and unreachable CSS in this very file has already produced one live silent bug
(`--signal-red-glow`, §5.3). The parity question that *does* apply is the reduced-motion state, which is
`C-218`, and it is satisfied by construction.

### 7.4 ⛔ Auto-merge: NO

The repo's policy permits automatic merge on green gates. **This row does not qualify**, on condition 2
of the four.

| Condition | Status |
|---|---|
| 1. Implementation cycle complete | ✅ achievable — seven tasks, literal code |
| 2. **Tests pass, and UAT passes where appropriate** | ❌ **The change is user-facing and purely visual. Its success criterion is perceptual, it lands on every skeleton in the app, and no automated gate in this repository can judge it** (`C-214`). §4.2 measures the signal, not the panel (`C-222`). |
| 3. Code review passes | ✅ achievable |
| 4. Important issues addressed | ⚠ **the landing value is not decided.** The Designer: *"LOW-TO-MODERATE that 36 is the right landing value… a seed for the A/B, not an answer."* |

**Merging on green gates would merge a seed value the Designer explicitly declined to endorse**, on the
strength of tests that assert the stylesheet contains the strings the plan told them to assert. That is
the GV-8 failure — a true measurement supporting a false conclusion — one level up.

**Requires an explicit owner sign-off after Task 4**, with the ladder photograph and the measured table
in the PR body. Everything up to that point can proceed without asking.

**Pause and ask, additionally, if:** Task 0 reports median dwell < 750 ms (§1.3); V0 fails to calibrate
at ≈0.034 (Task 3); the owner passes V1 and the row therefore ships **no token at all**; or the owner
rejects all four values.

### 7.5 What would falsify this plan

The plan rests on the Designer's claim that **geometry, not amplitude, is the dominant lever** — that an
edgeless wash is undetectable at a contrast where an edge would be obvious. The arithmetic is verified
(§0.4); the *perceptual* premise is not, and cannot be off-panel.

**If V1 (geometry only, 6/255 unchanged) is invisible while V3 is obvious**, the premise is wrong: the
row was an amplitude problem after all, the 3.3× steepening bought nothing, and the narrowed ramp is
unjustified complexity that should be reverted in favour of a pure token change. Task 4 is what
distinguishes these, and it is the reason V1 is in the ladder as its own lane rather than folded into
the others.
