# `UX-2` — the skeleton shimmer is on screen for ~0.3% of one animation cycle. Decide what it is for.

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2 — but it retroactively decides whether `UX-1` mattered at all.** Filed 2026-09-09 after
`UX-1` shipped twice.

## The measurement

| | |
|---|---|
| `/api/playhistory` load, measured on the box | **0.0030 – 0.0051 s** |
| `/api/queue` load | **0.0051 – 0.0067 s** |
| One `shimmer` animation cycle | **1500 ms** |
| Dead pause at each cycle boundary (default `ease`) | **~390 ms** |
| **Skeleton on screen** | **~0.3% of one cycle** |

⭐ **Independent corroboration by observed absence:** a CDP probe of the live kiosk returned
`{"reduce":false,"noPref":true,"skeletons":0}` — **zero skeleton nodes on the page**, with
reduced-motion directly falsified rather than inferred from the GNOME proxy.

## ⛔ THE MEASUREMENT IS TWO ENDPOINTS, NOT A UNIVERSAL — this correction is load-bearing

An earlier statement of this finding said *"no sweep is **ever** painted at **any** value."* ⛔ **That
is an overclaim and it was used to relax a merge gate.** It generalises **2 of 27+ call sites**, both
warm and both local.

⚠ **The unmeasured sites are exactly the ones most likely to be slow**: `DeviceManagementPage`
(device enumeration), Cast discovery, and the phone panels — which reach across the network to
`radio:5004`. **Measure those before concluding anything about them.** It is entirely possible some
skeletons ARE visible and the shimmer is doing its job there.

## The decision

**Three options. This row's first deliverable is an ANSWER, not code.**

1. **Leave it.** It costs nothing and covers the genuinely slow paths — cold start, a stalled API, the
   Cast path. ⭐ **This is the option the 27-site gap most supports** and it should not be dismissed as
   the lazy answer.
2. **Add a minimum dwell** (~300–400 ms) so the skeleton is visible whenever it appears. ⚠ **Cost: it
   deliberately makes fast loads FEEL SLOWER.** On a panel that resolves in 4 ms, this adds a
   100× delay to show a decoration.
3. **Drop the skeletons** on panels that resolve in single-digit ms, keep them where a real wait
   exists. ⚠ Requires per-site measurement first — i.e. it *depends on* the work in option 1's gap.

## ⛔ What this row decides for `UX-1`

`UX-1` shipped `#38383F` (56), then **reversed to `#24242B` (36)** after the owner saw the values
rendered rather than described. ⚠ **`docs/queue/UX-1.md` records a contradictory earlier judgement —
"36 is below the owner's dark-room visibility threshold"** — and the ambient conditions of the two
sittings that chose 36 were never recorded.

⭐ **That contradiction is UNRESOLVED, and it is currently MOOT rather than settled.** If the skeleton
is never seen, the value cannot matter. **If this row makes the skeleton visible, the value MUST be
re-judged at that point** — it will be the first time anyone actually sees it. ⛔ **Do not treat 36 as
settled-forever on the strength of two sittings against an element that was not on screen.**

## Verification

⭐ **The honest first deliverable is a per-site dwell census** — every `<Skeleton>` call site, measured,
not inferred. Everything else in this row is a decision that census informs.

⚠ **Do not measure dwell by reading the code.** The loading flag's lifetime is a function of a live
API call on a resource-constrained box; measure it on the box, warm and cold.

## Related

- **`UX-1`** — shipped twice; this row is its unanswered question.
- Both `UX-1` sittings that chose 36 are recorded at
  `docs/uat/2026-09-09-ux1-third-and-fourth-sittings/`.
