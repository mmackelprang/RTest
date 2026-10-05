# UX-1 — the third and fourth sittings, 2026-09-09

**Result: the owner chose `36` (`#24242B`, delta 16) twice.** This contradicts the night sitting of
2026-09-08, which recorded *"36 is below the owner's dark-room visibility threshold"* and is why
`56` (`#38383F`) shipped as [#641](https://github.com/mmackelprang/RTest/pull/641).

⛔ **This record does not resolve that conflict.** See *"What this does NOT establish"* below.

## Why this record exists

⚠ **It was filed retroactively, on a pre-merge review finding.** The two sittings that now drive the
value existed only as prose in `docs/queue/UX-1.md`, while the two sittings being *superseded* had a
full directory with a harness and screenshots. **The instrument that produced them existed as a
single file on one box**, in `/opt/radio-console/web/wwwroot/`, which `Deploy-ToLinux.ps1:271` wipes
with `rsync -a --delete` — so one deploy would have destroyed it.

⭐ **This row's entire history is instruments being found broken after the fact** (the v1 harness; the
shuffle-vs-letter mapping). An unreproducible instrument is the same failure waiting to happen, so
the harness is committed here.

## The harness

[`harness.html`](harness.html) — byte-identical to the copy served on the box
(`md5 6969e58ed9de5ae3fda5ab17f39a3057`, captured 2026-09-09 before any deploy could remove it).

**It is preserved as-run and deliberately not corrected.** Two things in it are known to be stale or
wrong, and are recorded here rather than edited into it:

- Its warning cites **`UX-1.md:217`** for the dark-room threshold sentence. That anchor was correct on
  `main` @ `f4d71b28`; the supersede banner has since moved it to **`:287`**.
- Its warning says the PR is *"held unmerged until this check settles it."* The fourth sitting has
  since happened and the hold was relaxed — see the queue row.

**How it works.** It loads the real deployed `/css/design-system.css`, so it renders the product's
own shimmer primitive rather than a copy. Three buttons override
`--skeleton-shimmer-highlight` on the `#stage` element only:

| Button | Value | Delta vs `#141416` |
|---|---|---|
| Old | `#1A1A1D` | 6 |
| Seed 36 | **`#24242B`** | **16** |
| Shipped 56 | `#38383F` | 36 |

⚠ **It must be served by the app** (`http://radio:5002/shimmer-demo.html`), not opened from disk —
the stylesheet link is absolute. It holds four skeleton shapes on screen permanently, which is the
whole point: in the product they are on screen for milliseconds.

## Sitting 3 — three panels side by side

The owner viewed all three values simultaneously on the real console at 1920×720 and said:

> **"Designer seed 36 is my choice."**

⚠ **This sitting used a side-by-side arrangement**, which is the arrangement whose bias prompted
sitting 4.

## Sitting 4 — one value at a time

⭐ **The better instrument.** A single value is revealed on a button press, with nothing beside it —
the page opens with **no button pressed**, showing the currently deployed token. The owner said:

> **"Shimmer 36 looks good."**

**What this answers:** side-by-side viewing can bias a judgement toward the dimmest acceptable value,
because a brighter neighbour acts as an anchor. Removing the neighbour removes the bias, and 36 still
read as good.

## ⛔ What this does NOT establish

1. ⛔ **The ambient conditions of BOTH sittings are unrecorded.** Nobody wrote down whether either
   room was dark. The judgement being contradicted was made *specifically in a dark room*, so these
   sittings sit alongside it rather than answering it.
2. ⛔ **This is not a 2-to-1 verdict.** The sitting that rejected 36 ran on the **v1 harness the plan
   itself records as broken**, so it is weak evidence in its own direction — but "weak evidence
   against" is not "evidence for".
3. ⛔ **Nothing about geometry.** Neither sitting varied the gradient ramp. Task 5b remains
   unvalidated and unshipped.

**Unresolved, not settled.**

## ⭐ Why the value shipped anyway — the stakes, not the evidence

Measured the same day: the panels the skeleton decorates fetch in **3–7 ms** (`/api/playhistory`
3.0–5.1 ms, `/api/queue` 5.1–6.7 ms) against a **1500 ms** animation cycle. **On those two panels, in
warm steady state, no sweep is painted at any highlight value.**

Corroborated directly in the kiosk over CDP on `:9223`:

```
{"url":"http://localhost:5002/","reduce":false,"noPref":true,"skeletons":0}
```

**Zero `.skeleton-loading` nodes on the live home page**, and `prefers-reduced-motion` is genuinely
`false` — so the flat-fill override is not what is hiding the shimmer. It simply is not on screen.

**If 36 is too dim in a dark room, the consequence is that an invisible element is marginally more
invisible.** That is what makes an unresolved disagreement tolerable for this one token.

⛔ **COROLLARY: if dwell is ever fixed, the value must be RE-JUDGED at that point.** That will be the
first time anyone sees this shimmer in the product rather than on a harness holding it on screen
artificially — and **every sighting on record, in all four sittings, was made on such a harness.**
36 is settled-for-now on a surface nobody can currently see, not settled-forever.

⚠ **Scope of the dwell measurement.** It times the **data fetch** for **two** panels — it is not the
plan's Task 0 `MutationObserver` on DOM presence, and **Devices, Radio and the phone thread are
unmeasured**. `docs/queue/UX-1.md` puts the full population at 27 call sites across 6 pages plus 38
raw `.skeleton-loading` nodes. **A cold start or a genuinely slow query would dwell longer**, and on
those the highlight value is visible and does matter.
