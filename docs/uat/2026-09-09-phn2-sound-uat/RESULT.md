# `PHN-2` owner UAT — the nine `SOUND` checks, run 2026-09-09

**Result: 5 pass, 3 FAIL, 1 deferred. ⛔ `PHN-2` does not close.**

Run by the owner at the cabinet, ~15 minutes, against the script in
[`design/plans/PHN-2-retire-the-audio-element.md`](../../../design/plans/PHN-2-retire-the-audio-element.md) §3.
`PHN-2` merged 2026-09-05 as the last open P0 with these nine deferred; this is that debt discharged.

⚠ **Pre-condition worth recording: the console was `isMuted: true` when the sitting began**, volume
0.63, radio playing. **Every `SOUND` check would have failed for the wrong reason.** Caught before the
run started. A muted console is indistinguishable from a broken audio path through every check here.

## Results

| # | Check | § | Result |
|---|---|---|---|
| 5 | A fetched voicemail is **audible at all** | U1 | ✅ **PASS** |
| 6 | It **ducks the radio** | U1 | ⛔ **FAIL** — no ducking observed |
| 7 | It **follows mute** | U2 | ✅ **PASS** |
| 8 | It **follows master volume** | U3 | ✅ **PASS** — volume changes voicemail loudness |
| 9 | With Cast active it **goes to the Cast device** | U4 | ✅ **PASS** |
| 1 | `Seek` actually **repositions** a short local MP3 | U5 | ⛔ **FAIL** — dragging the seek bar does not change the audio |
| 2 | `Time` **advances** during playback | U6 | ✅ **PASS** |
| 10 | Doorbell **preempts** cleanly, ducking **releases** after | U7 | ⚠ **DEFERRED** — no doorbell configured on this box |
| 11 | The **wait** is not mistaken for a broken button, and the room never carries **two voices** | U8 | ⛔ **FAIL** — two voicemails can play simultaneously |

## What the three failures are

**#6 — ducking. ⭐ This is [`AUD-2`](../../queue/AUD-2.md), and it is now confirmed by ear.**

`AUD-2` is *"one key per source … gain and ducking miss silently"* — the multiplier is written under
`_activeSource.Id` at `SoundFlowPlaybackService.cs:686` while the SDR source registers under a minted
`sdr-radio-{guid}`, so the write lands on a key nothing reads.

⭐ **Until today that was a code-read plus one log line. It is now observed by the owner on TWO
independent paths** — voicemail ducking and TTS ducking — with the radio audibly playing. **This is the
strongest evidence that row has ever had, and it had been sitting at 📋 with a finished plan and no
dependencies.**

**#1 — seek. Filed as [`AUD-24`](../../queue/AUD-24.md).** New; no row existed. ⚠ **Note the pairing
with #2, which PASSED**: `Time` advances correctly while `Seek` is ignored, so **the clock is honest
about a position the player is not at.**

**#11 — two voices. Filed as [`PHN-10`](../../queue/PHN-10.md).** New; no row existed. ⚠ **And
`PHN-1f` — "the wait-then-play queue" — has already shipped**, which is precisely what this check
exists to verify.

⚠ **Two halves, and only one is certainly a defect.** The owner reported both *"two voicemails can
play simultaneously"* and *"when TTSing a text the second text interrupts the first."* **Simultaneity
is wrong. Interruption may be intended preemption.** Do not treat them as one symptom without
establishing which behaviour each path is supposed to have.

## ⭐ Why this sitting was worth fifteen minutes of the owner's time

**Nine automated checks would have reported nothing.** The plan said so before the run:

> *"A green 'it completed' is the least trustworthy evidence available on this row, and no `PATH`
> check may ever be reported as evidence for a `SOUND` claim."*

**Three defects in fifteen minutes**, one of which promoted a queued row from inference to
observation. ⛔ **And the suite was green throughout** — it asserts nothing about sound, by design.

## What remains

- ⚠ **#10 is DEFERRED, not passed.** It needs a doorbell configured. Do not let it decay into a pass
  by silence; it is the only check that exercises preemption *and* ducking release together, and
  ducking is now known broken.
- ✅ Five checks are genuinely discharged and need not be re-run unless the audio path changes.

---

## ✅ RE-RUN 2026-09-09, after `AUD-2` deployed — **now 6 pass / 2 fail / 1 deferred**

`AUD-2` shipped as [#642](https://github.com/mmackelprang/RTest/pull/642) (`f4d71b28`) and was
deployed to the box. The owner re-ran the affected checks at the cabinet. Verbatim:

> *"Ducking now works, but I'm still able to play two voicemails simultaneously. TTS also ducks and
> the second TTS cancels the first one. **I think this behavior is the correct one.** Shimmer 36
> looks good."*

| # | Check | Was | Now |
|---|---|---|---|
| 6 | A fetched voicemail **ducks the radio** | ⛔ FAIL | ✅ **PASS** — confirmed by ear on **two** paths (voicemail and TTS) |
| 11 | The room never carries **two voices** | ⛔ FAIL | ⛔ **STILL FAIL** — but **halved**, see below |

### ⭐ `AUD-2` is closed by the same method that opened it

It was a code-read inference for weeks, sitting 📋 with a finished plan and no dependencies. The
owner's first sitting promoted it to an **observation**; this sitting closes it. ⛔ **No automated
check participated in either direction** — the suite was green throughout, by design.

### ⭐ Check #11 HALVES — the owner ruled on the ambiguous behaviour

This record filed #11 with an explicit warning that it contained **two behaviours and only one was
certainly wrong**. The owner has now ruled:

- **Two voicemails simultaneously** — ⛔ still wrong, still reproduced, now the *whole* of
  [`PHN-10`](../../queue/PHN-10.md).
- **A second TTS interrupting the first** — ✅ **correct behaviour, not a defect.** ⛔ **A fix for
  `PHN-10` must PRESERVE TTS preemption**, not suppress it.

⭐ **The warning in this record and in `PHN-10` held exactly:** *"do not let a post-`AUD-2` re-listen
be read as evidence this is fixed."* The owner listened with ducking audibly working and the
simultaneity was still there. **The trap was named in advance and the check walked past it.**

### What still remains

- ⛔ **#1 (seek)** — untouched. [`AUD-24`](../../queue/AUD-24.md) is still open; nothing in `AUD-2`
  went near it.
- ⚠ **#10 (doorbell)** — still **DEFERRED**, not passed. It needs a doorbell configured, and it is
  the only check exercising preemption *and* ducking release together. ⭐ **Ducking is now known
  WORKING, which makes this check meaningfully runnable for the first time.**
