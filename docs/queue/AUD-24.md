# `AUD-24` — the seek bar does not move the audio, and the clock reports a position the player is not at

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-09 from the owner's `PHN-2` UAT at the cabinet
([record](../uat/2026-09-09-phn2-sound-uat/RESULT.md), check #1 / §3 U5).

## What was observed

**Dragging the seek bar does not change the audio in MP3 mode.** Owner, verbatim: *"Dragging the seek
bar does **not** change the music in MP3 mode."*

⭐ **And the pairing is the interesting part: check #2 PASSED in the same sitting** — `Time` **does**
advance during MP3 playback. So the transport ignores the write while the clock keeps reporting
honestly.

⛔ **The display is therefore truthful about a position the player is not at.** A user drags to 2:30,
the bar moves, the elapsed time continues from wherever the audio actually is, and nothing anywhere
reports a failure. **That is a silent no-op wearing the costume of a working control** — the same
class this repo has spent the week removing.

## Scope questions for the plan

1. ⚠ **Is it the UI write or the engine seek?** `PHN-2`'s check #1 was written as *"`SoundPlayerBase.Seek`
   actually **repositions** a short local MP3"*, sourced from `PHN-1a` §2.2 (1) and ADR §14 Q3 — so the
   engine-level call was already suspected before any UI existed. **Establish which layer drops it
   before designing anything.** The bar moving proves the UI handler ran; it proves nothing below that.
2. **Does `Seek` throw, return false, or silently succeed?** ⚠ `CLAUDE.md` records that this repo made
   a non-seekable `SeekAsync` **throw** rather than no-op, deliberately, because *"a caller that posts
   … gets `200`, and hears [nothing] has been misled."* **If that throw exists and nothing surfaces
   it, the swallow is the defect and the seek may be fine.**
3. **Which sources are affected?** Observed on MP3 (file player). ⚠ **Do not assume it generalises** —
   BT and SDR are not seekable at all, and voicemail is a third path.

## ⚠ Related but NOT the same as `AUD-2`

`AUD-2` is the key-mismatch that makes **ducking and gain** miss. Both were found in the same sitting
and both are silent, but they are different mechanisms — **do not fold them together.** `AUD-2` writes
to a key nothing reads; this row's write may never leave the UI.

## Verification

⚠ **This cannot be closed by a green suite** — the same reason `PHN-2`'s nine checks could not be. The
honest gate is unit coverage of the seek path **plus** a repeat of §3 U5 at the cabinet: drag the bar
on a short local MP3 and confirm the audio moves.

⭐ **Assert the PRESENCE of repositioning, not the absence of an error.** A test that asserts "seek did
not throw" passes today.

## Provenance

⭐ **Found by the owner in fifteen minutes, on a row the suite reports green.** Deferred from
`PHN-1a` §2.2 (1) since before `PHN-2` merged, carried through the whole ADR-029 arc as an unverified
`SOUND` item, and confirmed broken the first time a person actually dragged the control.

## ✅ RE-CONFIRMED 2026-09-09 on `f4d71b28`, after the deploy

Owner, verbatim, on the freshly deployed build: *"For Mp3 files, dragging the position bar moves the
marker, but not the music at all."*

⭐ **Two things this sharpens, and both narrow the plan:**

1. **It is not a stale-build artifact.** The original sighting predates today's deploys; this one is on
   `f4d71b28` with `AUD-2` and `UX-1` on the box. **The defect survives a fresh deploy.**
2. ⭐ **"Moves the marker, but not the music AT ALL"** — *at all* is the useful word. This is **not**
   partial or approximate repositioning, not a rounding or granularity problem, and not a seek that
   lands in the wrong place. **The audio is entirely unaffected.** ⚠ That argues for a write that
   never reaches the engine over a `Seek` that executes and misses, and it should shift where the plan
   looks FIRST — but ⛔ **it does not prove it**, because a `Seek` that returns early on a guard would
   look identical from the room. **Establish the layer; do not infer it from the adverb.**

⚠ **`AUD-2` did NOT fix this and was never expected to.** Ducking and gain now work, confirmed by ear
on two paths; the seek is untouched. **Do not read the two as related because they were found in the
same sitting.**
