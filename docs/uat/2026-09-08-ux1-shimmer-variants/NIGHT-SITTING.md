# `UX-1` night sitting — 2026-09-08, ~20:30 EDT

**Result: `56` (`#38383F`) is the dark-room value. Visible AND calm.** ⚠ It is a **floor**, not the
final answer — daylight is untested and will not want less.

## What the owner reported, in order

1. On the **v1** harness (geometry variants): *"Even in the dark I only see movement on the first
   panel (V0). The remaining ones look static. For this to be visible at all in the daytime, they
   would have to be brighter."*
2. Then, unprompted: *"Even V0 is **very** dim and not easy to see in a dark room."*
3. On the **v2** harness (amplitude ladder): *"**56** is the dimmest I can clearly see."*
4. And: *"**56 stays calm, no distraction.**"*

## ⛔ Two premises this sitting destroyed

### 1. The Designer's central claim is contradicted by the owner's eye

The design answer that discharged this row's gate said **amplitude is the SMALLER half and geometry is
the key lever.** The owner could only see the *full-width ramp* — the shipping geometry — and found
even that too dim. **Geometry did not help; amplitude is the whole problem.**

The Designer's seed of **36** (delta 16) is **below the owner's visibility threshold in a dark room.**
⭐ To its credit it explicitly declined to endorse a landing value, calling 36 *"a seed for the A/B,
not an answer."* That caution was well placed.

### 2. ⚠ My v1 harness was flawed, and the flaw is instructive

V1–V4 concentrated the highlight into a band spanning 42–58% of a 200%-wide gradient — about **0.32 of
the element width**. With the sweep travelling 4 element-widths in 1.5 s, that band is on the element
only ~**0.49 s per 1.5 s cycle**. **For two-thirds of every cycle those columns were a flat, unmoving
`#141416`.**

So v1 did not test "geometry vs amplitude". It tested a geometry that traded a faint-but-continuous
shimmer for a brighter-but-mostly-absent one — and the owner correctly read the result as *static*.

⭐ **Another instrument that could not see what it claimed to measure**, and this one was mine. Same
family as `NRestarts=0`, `psidtsAgeSeconds: 608`, and a green gate on the wrong tree.

## The numbers

Base surface is `--surface-raised` `#141416` = **(20,20,22)** throughout.

| Value | Hex | Delta | Verdict |
|---|---|---|---|
| 26 | `#1A1A1D` | **6** | **ships today** — "very dim, not easy to see" even in the dark |
| 36 | `#242429` | 16 | Designer's seed — **below threshold** |
| 46 | `#2E2E34` | 26 | not the dimmest visible |
| **56** | **`#38383F`** | **36** | ⭐ **dimmest clearly visible, and calm** |
| 66 | `#42424A` | 46 | not evaluated — see below |

**The chosen value is 6× today's delta and 2.25× the Designer's seed.**

## ⚠ What this sitting does NOT establish

- **Daylight.** Untested. The 2026-09-08 morning sitting was aborted precisely because the surfaces
  read as *very dark* in daylight, so **56 is a floor and the daylight value may be higher.**
- **Whether 66 is comfortable.** Deliberately not asked. It only matters if daylight demands more than
  56 — and then the question becomes whether one static value can serve both conditions at all.
  ⭐ **If daylight needs more than a night-comfortable maximum, this row changes character entirely**:
  from "pick a token value" to "the shimmer must adapt to ambient light", which is materially bigger
  work. **Establish that before anyone builds the cheap version.**
- **Anything about geometry.** v1 could not answer it and v2 did not ask. The shipping geometry is
  adequate at a sufficient amplitude; whether a better geometry would let a *lower* amplitude work is
  simply unknown.

## The v2 harness

Reconstructible in full from this section — it is the **shipping geometry** with five highlight values:

```css
.sk { background-size: 200% 100%; animation: shimmer 1.5s infinite; }
/* per column, only the middle stop changes: */
background: linear-gradient(90deg, #141416 0%, <VALUE> 50%, #141416 100%);
/* values: #1A1A1D  #242429  #2E2E34  #38383F  #42424A */
@keyframes shimmer { 0% { background-position: -200% 0 } 100% { background-position: 200% 0 } }
```

Blind by default; **Shuffle** re-randomises column order and restarts every animation in step. Lives on
the box at `/tmp/ux1/v2.html` — ⚠ `/tmp`, so it will not survive a reboot; rebuild from the block above.

## Next step

**The daylight sitting**, on the same ladder, asking the same two questions. If 56 also reads as the
dimmest clearly visible at midday, the row lands on 56 and is done. If daylight wants more, ask whether
that higher value still stays calm at night **before** treating it as the answer.
