# UX-1 — the daylight sitting, 2026-09-09

> ## ⚠ ANNOTATED 2026-09-09 — **this record is accurate and is NOT being rewritten.** A later sitting the same day reached a different answer.
>
> **Everything below correctly records what happened at this sitting.** It is left exactly as
> written. What has changed is only that it is no longer the last word:
>
> **A THIRD sitting, later on 2026-09-09 in afternoon light**, showed the owner three panels side by
> side on the real console — delta 6 (`#1A1A1D`), delta 16 (`#24242B`), delta 36 (`#38383F`) — and
> the owner chose **36 (`#24242B`)**. That contradicts the night sitting's finding that *"36 is below
> the owner's dark-room visibility threshold"*, and it means the sentence below — *"The row lands on
> 56 and is done"* — **no longer holds**, though it was a fair reading of the evidence available when
> it was written.
>
> **A FOURTH sitting the same day confirmed 36 on a better instrument** — a one-at-a-time harness
> showing a single value with no reference beside it — where the owner said *"Shimmer 36 looks
> good."*
>
> ⛔ **Neither sitting is being called wrong, and this is NOT a 2-to-1 verdict.** The **ambient
> conditions of both recent sittings are unrecorded**, so neither directly answers the dark room the
> night sitting was judged in; and the night sitting ran on the **v1 harness later found broken**, so
> it is weak evidence the other way. **Unresolved, not settled.** ⭐ What allows it to proceed is the
> **stakes, not the evidence**: on the two panels measured the shimmer is on screen ~3–7 ms per
> 1500 ms cycle, so no sweep is painted there at any value (⚠ two panels of 27+ call sites; a cold or
> slow path dwells longer). ⚠ **If dwell is ever fixed, the value must be re-judged then.**
>
> Full argument and both instruments: [`../../queue/UX-1.md`](../../queue/UX-1.md).

**Result: `56` (`#38383F`, delta 36). Same value as the dark room. The row lands on 56 and is done.**

⭐ **The expensive branch did NOT fire.** The night record set the decision rule in advance:

> *"If 56 also reads as the dimmest clearly visible at midday, the row lands on 56 and is done. If
> daylight wants more, ask whether that higher value still stays calm at night **before** treating it
> as the answer."*

Daylight did not want more. So `UX-1` stays *"pick a token value"* and never becomes *"the shimmer
must adapt to ambient light"* — the materially bigger work the row warned about. **The cheap version
is the correct version**, which is a result worth having explicitly rather than by assumption.

`66` was on the ladder and was **not** chosen. It remains unevaluated at night, and now never needs to
be.

## Conditions

Same v2 harness as the night sitting — shipping geometry, five middle stops, blind order, rebuilt from
the spec in [`NIGHT-SITTING.md`](NIGHT-SITTING.md) §"The v2 harness" (the original lived in `/tmp` and
did not survive). Served on the box at `127.0.0.1:8099`, opened in a **second kiosk tab** so the app UI
was never displaced.

**The instrument was verified before the owner looked** — the lesson the v1 harness taught:

| Check | Result |
|---|---|
| Columns / blocks | 5 / 20 |
| Gradient ends | `rgb(20,20,22)` = `#141416` on all five ✓ |
| Distinct middle stops | **5 of 5**, one each ✓ |
| Motion | `background-position` 79.39% → 165.20% in 350 ms — **continuously moving** ✓ |

That last row is the one v1 failed: its highlight band left the element for a large part of every
cycle, and the owner correctly read it as not moving. ⚠ **The duty-cycle figures this originally cited
were wrong — corrected 2026-09-09 by measurement**, see [`REPORT.md`](REPORT.md) and
[`NIGHT-SITTING.md`](NIGHT-SITTING.md) § 2: the band is on the element **~1.01 s** and off it
**~0.49 s**, not the reverse, and the mechanism is the `ease` timing function stalling the sweep to
~1% of its median speed at each cycle boundary — not a duty cycle.

## ⛔ The harness had a flaw of its own, and it nearly recorded a wrong answer

**Shuffle re-randomises the column order, but the answer is given as a letter.** So a letter-based
answer is only valid for the arrangement on screen at the moment of judging — and the coordinator's
copy of the mapping was twenty minutes stale.

The owner first answered **"A is the one."** Under the arrangement the coordinator had captured, `A`
was **66**; under the *live* arrangement, `A` was **26** — the value that ships today. **Two different
readings of the same answer, one of which would have overturned the row and one of which would have
triggered its expensive branch.**

⭐ **It was caught by re-querying the live page instead of trusting the captured mapping**, and the
result was **not recorded**. The owner then gave the value directly — `56` — which removes the letter
indirection entirely.

⚠ **The fix for next time: ask for the VALUE, not the letter** — or freeze the mapping once judging
begins. A blind harness must not let its own blinding change under the answer.

⭐ Same family as every other instrument failure this week: **the harness could not tell the
coordinator which state it had been in when the owner looked.**

## What this closes and what it does not

- ✅ **Both questions answered in both conditions.** Dimmest clearly visible: 56. Stays calm: yes
  (established at night; daylight calmness is not the binding constraint — a shimmer that is calm in a
  dark room is calm in daylight, where ambient light dominates).
- ✅ **Ambient-light adaptation is NOT needed.** Recorded so nobody re-opens it speculatively.
- ⛔ **Nothing about geometry.** v2 did not ask, and v1 could not answer. Whether a better geometry
  would let a *lower* amplitude work is **still unknown** — and is now moot for this row, since 56 is
  acceptable in both conditions at the shipping geometry.

## The change this authorises

> ⚠ **This is what THIS sitting authorised, and it shipped as #641.** A later sitting the same day
> chose `#24242B` instead — see the banner at the top of this file. The `--surface-overlay` warning
> below is **unaffected** and still binding.

One token value: the skeleton shimmer's middle stop, `#1A1A1D` → **`#38383F`**.

⛔ **DO NOT change `--surface-overlay`. Verified 2026-09-09:** `.skeleton-loading`
(`design-system.css:1099-1104`) takes its middle stop from `var(--surface-overlay)`, and that token has
**20 consumers in `design-system.css`**. Editing it to `#38383F` would lighten **twenty unrelated
surfaces** across the app — a change roughly the size of a re-theme, shipped under a row about
skeleton shimmer.

**The shimmer needs its own token** (e.g. `--skeleton-shimmer-highlight: #38383F`) consumed only by
`.skeleton-loading`. ⚠ The Builder must confirm the 20-consumer count itself before touching anything
— it is the whole reason this is a new token rather than a one-character edit.
