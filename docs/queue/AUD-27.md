# `AUD-27` — album art appears on identification, then vanishes the instant you press pause

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-10 from the owner's phone-sitting UAT, items #5/#6.

## What was observed

Owner, verbatim: *"album art appears after ~20 seconds of playing, but disappears **immediately** when
pause is pressed."*

The appearing half is **correct and confirmed working** — captured live in the same session:

```
15:13:48 Identified track: 'Heart and Soul' by 'Huey Lewis & The News' (confidence: 80 %, coverArt: /api/albumart/0f924e4c2dd0504e.jpg)
15:13:48 Cover art found for 'Heart and Soul' by 'Huey Lewis & The News': /api/albumart/0f924e4c2dd0504e.jpg
```

**It is the disappearing half that is the defect.** Pausing is not stopping — the track, the artist and
the art are all still true of what is loaded. ⛔ **Metadata is being cleared on a state transition
that has not invalidated it.**

## ⚠ THIS IS A THIRD MECHANISM — do NOT fold it into `AUD-1` or `AUD-17`

All three touch BT album art and they are **different defects**. Establish which you are looking at
before changing anything:

| Row | Mechanism | Status |
|---|---|---|
| [`AUD-1`](AUD-1.md) | Shazam **overwrites** correct AVRCP metadata | Confirmed live 2026-09-10 — *"Shazam metadata replaced AVRCP for BT"* |
| [`AUD-17`](AUD-17.md) | AVRCP **cover-art path** has never worked (`ImgHandle` vs MPRIS names) | Open |
| **`AUD-27`** (this) | Art that **successfully landed** is **discarded on pause** | ⛔ **New** |

⭐ **`AUD-1` and `AUD-27` act in opposite directions on the same field**: `AUD-1` writes metadata that
should not have been written; this row erases metadata that should have been kept. **A fix for either
that does not name the other risks trading one for the other.**

## Scope questions for the plan

1. **Which layer clears it?** ⚠ Candidates are not equivalent: the source clearing its own metadata on
   `Paused`; `AudioStateUpdateService` broadcasting a cleared DTO; or the Web layer discarding it on a
   state change. **Establish the layer — the bar moving proved nothing on `AUD-24` and a vanishing
   image proves nothing here.**
2. ⚠ **Is pause distinguishable from stop at that layer?** If the same path serves both, the fix is a
   contract question (*what does pause mean for metadata?*) and not a bug fix. **Say which.**
3. **Does it also affect title/artist, or only art?** The owner reported art. ⛔ **Do not assume the
   scope from the symptom that was noticed** — `AUD-24` was reported as a seek defect and the same
   field carried the clock.
4. ⚠ **Does resume restore it?** Untested. If the metadata returns on resume, this is a display
   lifetime problem; if it does not, something was destroyed rather than hidden. **Different fixes.**

## Verification

⛔ **Not closable by a green suite** — the whole `PHN-2` arc is the standing evidence for that.

⭐ **Assert the PRESENCE of retained metadata across a pause** — read `albumArtUrl` from
`/api/audio/nowplaying` **before and after** a pause and assert it is unchanged. ⚠ **A test asserting
"pause did not throw" passes today**, which is the same vacuity that made every existing seek test
green on a total no-op.

⚠ **Beware a vacuous run**: if fingerprinting has not landed yet, `albumArtUrl` is the placeholder
both before and after, and the assertion passes on a broken build. **Wait for the identification, then
pause.**

## Provenance

⭐ **Found by the owner in a sitting whose stated purpose was four other rows.** It is not covered by
any `PHN-2` check, and no automated check in this repo asserts anything about metadata lifetime.

---

## ⛔ CONTRADICTED 2026-09-10, HOURS AFTER FILING — measured on the live box with BT PAUSED

**This row was filed on a single owner observation — *"album art … disappears immediately when pause is
pressed."* A direct measurement of the paused state does NOT reproduce that, and shows something
different:**

```
src=Bluetooth  playing=False  paused=True
title  = 'Pixel 10 Pro XL'          <- the DEVICE NAME, not the track
artist = 'Huey Lewis & The News'    <- correct, RETAINED
art    = /api/albumart/0f924e4c2dd0504e.jpg   <- STILL PRESENT
```

⛔ **The art did NOT disappear. The TITLE was clobbered to the Bluetooth device name**, while artist
and cover art both survived the pause.

### What this changes

| Filed as | Measured |
|---|---|
| *"Art vanishes on pause"* | ⛔ **Art persisted.** |
| Implied: all metadata cleared together | ⚠ **PARTIAL loss — one field lost, two retained.** |
| Implied: a single clearing action | ⚠ **Fields behave DIFFERENTLY**, so probably not one clear |

⭐ **The row's real shape is "partial metadata loss on pause, and the title is the field that goes" —
not "art disappears."** ⚠ **A plan written against the filed description would look for the wrong
thing in the wrong layer.**

### ⚠ Both observations are the owner's and BOTH may be true

⛔ **Do not resolve this by declaring the owner's original report mistaken.** Plausible reconcilers,
none yet tested:

1. **Pause BEFORE fingerprinting lands** — art was never set, so "disappears" was "never appeared".
2. **Two different pause paths** — the owner reported pausing *"on either the radio console or the
   phone"*; console-pause and handset-pause may not clear the same fields.
3. **Timing** — art may clear and then be restored by the next identification cycle (~15 s), so a
   check moments after pause and one minutes after pause disagree.
4. ⚠ **`title = device name` may be NORMAL AVRCP behaviour when paused** — the phone may simply stop
   publishing track metadata, and BlueZ falls back to the device name. **If so, the title half is not
   a defect at all and this row is smaller than it looks.** ⛔ **Establish this FIRST — it is the
   cheapest of the four and it can dissolve the row.**

### The measurement that settles it

⭐ **Poll `/api/audio/nowplaying` every second across a pause**, from before the press until 30 s
after, and record all three fields. That distinguishes all four reconcilers in one run and costs
nothing. ⛔ **Do not plan a fix before it has been done.**

⚠ **And record the wall-clock time of the button press** — the `AUD-11` investigation could not
correlate pauses with node events for exactly this reason.
