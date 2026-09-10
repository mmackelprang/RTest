# `AUD-28` — the seek bar seeks on every drag frame, so scrubbing stutters

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-10 from the owner's `AUD-24` UAT, on `9ca42590`.

## What was observed

Owner, verbatim: *"`AUD-24` works, but there are **lots of audio stutters when dragging**. We should
not interrupt the currently playing stream until the drag completes and maybe 'debounces' for a few
milliseconds."*

⭐ **`AUD-24` PASSED.** The seek reaches the engine and the audio moves. **This row is about what
happens on the way there.**

## ⛔ THIS IS A REGRESSION THAT `AUD-24` INTRODUCED — say so, do not soften it

Before `AUD-24`, `SeekCoreAsync` assigned a field and returned. **Dragging produced no audio effect
whatever, so it could not stutter.** Now every intermediate value the slider emits during a drag
reaches the engine and repositions playback — **dozens of real seeks for one gesture.**

⛔ **The fix did not cause a defect in the seek; it made a pre-existing UI behaviour AUDIBLE for the
first time.** The slider has always emitted continuously; nothing downstream ever acted on it.

⭐ **This is the same shape as `AUD-26`**, which `AUD-2` made observable by restoring ducking. ⚠ **Two
rows in two days where repairing a dead mechanism exposed a live one. Expect it after any fix that
resurrects a path — and do not read "new symptom after the fix" as "the fix was wrong."**

## The owner's prescription

> *"We should not interrupt the currently playing stream until the drag completes and maybe
> 'debounces' for a few milliseconds."*

⚠ **Treat that as the requirement, not the implementation.** It names two distinct behaviours and a
plan should say which it is doing, and why:

1. **Commit on drag-END only** — no seek until the gesture finishes. Exact, no mid-drag audio at all.
2. **Debounce/throttle** — seek during the drag but at a bounded rate.

⛔ **These are not the same and they feel different.** (1) gives silence-then-jump; (2) gives coarse
scrubbing feedback. ⭐ **A scrub preview is a real feature and (1) removes it** — say which the row is
choosing rather than picking the easier one silently.

## Scope questions for the plan

1. **Which layer emits the intermediate values?** `NowPlayingPanel`'s `RadzenSlider` — establish
   whether it exposes a change-on-release event, in which case this is a one-line binding change with
   no debounce logic at all.
2. ⚠ **Is the stutter the seeks themselves, or the STOP/RESTART inside them?** ADR §14 Q3 licenses
   stop-and-restart-at-offset. **If each seek tears down and restarts the player, the cost per seek is
   large and rate-limiting alone may not be enough.** ⛔ **Measure before choosing a remedy.**
3. **Does the same gesture exist elsewhere?** `VoicemailPlayer` has **no drag handler** (established
   in `AUD-24`), so it is likely unaffected — ⚠ **confirm rather than assume.**
4. ⚠ **Does the readout still track the finger during a drag?** If seeking is deferred to release, the
   *displayed* position must still follow the drag or the control feels dead — **which is how `AUD-24`
   started.** ⛔ **Do not fix a stutter by making the bar unresponsive.**

## Verification

⛔ **Not closable by a green suite** — it is an audible, timing-dependent quality problem.

⭐ **A unit test CAN pin the invariant that matters, though: count engine seek calls per gesture.**
Simulate a drag emitting N intermediate values and assert the engine received **1** (drag-end) or
**≤ k** (throttled). ⚠ **That test must fail on `9ca42590`, where it would receive N** — say so and
show it.

**Cabinet gate:** drag slowly across a playing track and listen. ⭐ **Also drag FAST** — a debounce
tuned on slow drags can still admit a burst on a quick one.

## Related

- **`AUD-24`** — shipped `9ca42590`, the parent. **Its UAT PASSED**; this row is a follow-on, not a
  failure of it.
- **`AUD-26`** — the other "a fix made a pre-existing defect observable" row, from `AUD-2`.
