# `AUD-29` — in Bluetooth mode the console's position bar never moves, and disagrees with the phone

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-10 from the owner's timestamped pause/resume sitting.

## What was observed

Owner, verbatim: *"the position bar on the radio console does **not** match the position bar on the
phone. The bar on the console **doesn't move at all** in BT mode."*

Confirmed by direct measurement, two reads three seconds apart with audio playing:

```
pos= 00:01:34.6550000   dur= None   pct= None   playing= True   src= Bluetooth
pos= 00:01:34.6550000   dur= None   pct= None   playing= True   src= Bluetooth
```

⛔ **`position` is FROZEN at a stale non-zero value; `duration` is null; `progressPercentage` is null.**
The bar cannot move: nothing advances the position, and with no duration there is no percentage to
render.

⭐ **Note the position is `00:01:34.655`, not zero** — **something set it once and nothing has updated
it since.** That is a different defect from "never populated" and points at a one-shot read rather
than a missing feature.

## ⭐ This is the EXACT INVERSE of `AUD-24`, and the pair is instructive

| Row | Clock | Transport |
|---|---|---|
| [`AUD-24`](AUD-24.md) (file player) | ✅ advanced correctly | ⛔ seek did nothing |
| **`AUD-29`** (Bluetooth) | ⛔ **frozen** | — |

⭐ **`AUD-24` was "the readout is honest about a position the player is not at." This is "the player is
somewhere and the readout does not know."** Both are the console telling the user something untrue
about position; the failures are mirror images.

## Scope questions for the plan

1. ⛔ **FIRST: is a moving position bar even ACHIEVABLE over A2DP?** ⚠ **Do not assume it is.** Position
   would have to come from **AVRCP** (`org.bluez.MediaPlayer1` `Position`), and this repo has a
   documented history of reading the wrong property off that interface — [`AUD-17`](AUD-17.md) is a row
   about exactly that (`ImgHandle` vs MPRIS names, a path that **has never executed**). **Establish
   whether BlueZ publishes a usable position here before designing anything.**
2. ⚠ **If it is NOT achievable, the correct fix may be to STOP SHOWING A POSITION BAR in BT mode** —
   or show elapsed-since-start rather than track position. ⛔ **A control that cannot work should not
   be displayed as though it can**; that is the same class this repo has spent the week removing.
   **"Hide it" is a legitimate outcome and must be priced against "make it work."**
3. **Where does the stale `00:01:34.655` come from?** One-shot read at connect, a leftover from the
   previous source, or an AVRCP value that arrived once? ⚠ **A leftover from a PREVIOUS SOURCE would be
   a cross-source contamination bug and considerably more serious than a missing feature.**
4. **Why is `duration` null when the phone knows it?** ⚠ AVRCP exposes track duration; if it is
   reachable and unread, that is a smaller and separable fix that alone would make `pct` renderable.

## Verification

⛔ **Not closable by a green suite.** ⭐ **Assert the PRESENCE of advancement**: read `position` twice,
≥3 s apart, during confirmed BT playback, and assert it **increased**. ⚠ **A test asserting
`position != null` passes today** — the value is non-null and wrong, which is the whole defect.

⚠ **Beware a vacuous run**: `AUD-10`'s degradation means BT audio can be *silently dead* while state
says `Playing`. **Confirm audio is actually flowing** (`🔬 PipeWire OnProcess` count climbing **and**
no `No audio data captured` warnings) before trusting a position reading.

## Related

- **`AUD-24`** — the mirror-image defect on the file player. Shipped.
- ⚠ **`AUD-17`** — the precedent for "we read the wrong property off `org.bluez.MediaPlayer1`". **Read
  it before touching AVRCP.**
- ⚠ **`AUD-10`** — its capture degradation can make any BT observation vacuous.
