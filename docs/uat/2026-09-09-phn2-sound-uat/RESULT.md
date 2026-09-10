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

---

## ⭐ `PHN-10` DIAGNOSED AND FIXED — what check #11 was actually detecting, and what it means for #10

Check #11's `FAIL` was one guard in `AudioFileEventSource`, and the fix is on
`fix/phn-10-two-voices-at-once`. Plan: [`PHN-10-nothing-can-stop-a-voicemail.md`](../../../design/plans/PHN-10-nothing-can-stop-a-voicemail.md).

### Check #11 — the mechanism

`AudioFileEventSource.StopCoreAsync` and `DisposeAsyncCore` both gated the call to
`SoundFlowPlaybackService.StopAsync` — **the only call that performs `MasterMixer.RemoveComponent`** —
behind a private `bool _isPlaybackActive`. `EventPlaybackService.TearDownAsync`'s **first** statement
is `playback.Cancel()`; `AudioFileEventSource`'s `_playbackCts` is linked over that token;
`AwaitCompletionAsync`'s exception filter does not swallow the cancellation; and
`PlayWithSoundFlowAsync`'s `catch (OperationCanceledException)` set the flag to `false` **while
stopping nothing** — under a comment that said `// Playback was stopped`. A 500 ms `DuckingReleaseMs`
fade then sits between the cancel and the guard read, so the flag was **deterministically** false, not
racily so. `TTSEventSource` calls the same stop unconditionally, which is the entire reason the TTS
arm behaved and the voicemail arm did not.

⛔ **The scope is wider than check #11.** Every stop in the attended-playback seam funnels through
that guard: the **user's own Stop button**, **doorbell preemption**, ADR-029 §7.1's
`GvMedia:MaxPlaybackSeconds` *"THE guarantee"*, the `/sleep` edges and the last-circuit backstop were
**all inert on the voicemail path** — and every voicemail played to its natural end leaked a
`SoundPlayer` plus a mixer component permanently.

⛔ **The correct behaviour is REPLACE, not queue.** This record's original wording for #11 spoke of
*"the **wait**"*. ADR-029 §6.2 rule 1 is attended-vs-attended → **replace**, and rejects queueing in
as many words (*"queueing behind 40 seconds of voicemail would be baffling"*). **A build in which the
second voicemail waits for the first is a FAIL**, even though it also produces one voice. Count
voices, and check that the *second* one is the one you hear.

### ⚠ Check #10 (doorbell) — a PREDICTION, recorded before it is run

`PHN-10`'s plan predicts **#10 would have FAILED** had it been run before this fix, for the same root
cause: doorbell preemption reaches the voicemail through `OnDuckingStateChanged` → `StopAsync` →
the same disarmed guard, so the announcement would have talked **over** the voicemail rather than
replacing it.

⚠ **It is still `DEFERRED` and must not decay into a pass by silence.** But it is now runnable
without doorbell hardware — the endpoint is the doorbell:

```bash
curl -X POST http://radio:5000/api/notifications/announce \
  -H 'Content-Type: application/json' \
  -d '{"Message":"Someone is at the door"}'
```

Start a long voicemail with the radio on, then fire that. **PASS** = the voicemail stops, the
announcement is intelligible, and the radio returns to full volume afterwards. **Record the result
either way** — a `DEFERRED` check that is never re-run is indistinguishable from one that passed.

### ⛔ What still cannot be closed from here

A green suite does not close #11. The only replacement test in the tree
(`EventPlaybackServiceTests.ASecondStartReplacesTheFirst_AndTheFirstIsTornDown`) was **green
throughout the defect** and would have stayed green — it uses a fake source, and the service half was
always correct. The cabinet gate is: start one voicemail, start a second, confirm the first goes
**silent** while the second plays — **plus a TTS control in the same sitting**, because the whole risk
of the fix is buying #11 by breaking the preemption the owner ruled correct.

---

## ✅ FINAL 2026-09-10 — **8 pass / 1 fail / 0 deferred. Every one of the nine has now actually been run.**

`PHN-10` shipped ([#649](https://github.com/mmackelprang/RTest/pull/649), `914748fe`) and was deployed
as `914748f`. The owner re-ran the affected checks at the cabinet.

| # | Check | Was | Now |
|---|---|---|---|
| 11 | The room never carries **two voices** | ⛔ FAIL | ✅ **PASS** — two voicemails now **replace** rather than overlap |
| 10 | Doorbell **preempts** cleanly, ducking **releases** after | ⚠ DEFERRED | ✅ **PASS** — triggered by `curl`; **no doorbell hardware was needed after all** |

⭐ **The owner tested MORE than was asked**, and it is the part that matters most: **both directions of
cross-type preemption** — TTS interrupts voicemail, **and** voicemail interrupts TTS. ⛔ **That is
precisely what a naive "serialise all event audio" fix would have broken silently**, and no automated
check in this suite would have caught it.

### ⚠ Check #10 was DEFERRED for a reason that turned out not to hold

It was deferred on 2026-09-09 as *"no doorbell configured on this box"*. It is triggerable by `curl`.
⭐ **The check sat unrun for a hardware dependency it did not have** — and it was the only one
exercising preemption *and* ducking release together, on a build where ducking was later proven
broken. **Re-examine a deferral's stated reason before carrying it forward.**

### What remains

⛔ **#1 (seek) is the only outstanding failure** — [`AUD-24`](../../queue/AUD-24.md), re-confirmed by
the owner on `f4d71b28` after the deploys: *"dragging the position bar moves the marker, but not the
music at all."* Untouched by everything shipped in this arc.

⭐ **Nine `SOUND` checks, deferred since `PHN-2` merged 2026-09-05, are now discharged to a single
known defect** — and every one of the three failures the first sitting found (`AUD-2`, `AUD-24`,
`PHN-10`) was invisible to a green suite.
