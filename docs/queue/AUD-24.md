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

---

## ✅ DIAGNOSED AND PLANNED 2026-09-10 — the layer is `FilePlayerAudioSource.SeekCoreAsync`

Plan: [`design/plans/AUD-24-the-seek-that-only-moved-the-readout.md`](../../design/plans/AUD-24-the-seek-that-only-moved-the-readout.md).

**The mechanism, in one paragraph.** `FilePlayerAudioSource.SeekCoreAsync` range-checks its argument,
executes `_position = position;`, logs *"Seeked to {Position}"* and returns. **It never calls the audio
engine.** `SoundFlowPlaybackService.Seek` exists, is correct, and is called properly by the sibling
`AudioFileEventSource`; the file player uses that same registration for `Pause`, `Resume`, `SetVolume`
and `StopAsync`, and **for `Seek` alone does not.** `Position` reads back the very field
`SeekCoreAsync` writes.

⭐ **That single field explains the pairing this row was filed on.** `MonitorPlaybackAsync` advances
`_position` by a wall-clock `+1s` per tick and never reads the player. So check #2 (`Time` advances)
and check #1 (seek repositions) **read the same field, and only one of them was ever coupled to the
audio.** The clock is honest about the field; the field is the lie.

**The layer was established by reading the chain, not by inferring from the adverb** — as this row and
the dispatch both required. UI markup → `HandleSeekAsync` → `UpdatePlaybackRequest` → `AudioController`
→ `PrimaryAudioSourceBase.SeekAsync` → `SeekCoreAsync` → *(nothing)*. The controller guard is cleared
by a closed argument rather than an assumption: the draggable slider renders **only** when `CanSeek`,
`CanSeek` **is** `primary.IsSeekable` read off the same object the guard tests, and a `true`
`IsSeekable` is exactly the condition under which the guard passes. The two facts cannot disagree.

## ⛔ Five of this row's own premises did not hold

**① The defect was already diagnosed in-tree, a week before the owner saw it.**
`design/FUTURE-WORK.md` §14a, dated **2026-09-02**, names the file, the method, the mechanism
(*"**It assigns a field.** No audio is repositioned"*), the fix, the blast radius, the `bool`
propagation rule, and **its own UAT requirement**. `design/AUDIO-PIPELINE-REVIEW.md` finding #3 says
*"File-player seek is display-only and position is a wall-clock estimate."* `design/DECISION-LOG.md`
(ADR-029 amendments, Decision 2) names `FilePlayerAudioSource` as *"out of scope, and logged with its
own UAT requirement in `design/FUTURE-WORK.md` §14a."*
⭐ **This row was filed as a fresh unknown with three open scope questions, all three of which those
documents already answered.** The `Provenance` section above says the item was *"carried through the
whole ADR-029 arc as an unverified `SOUND` item"* — it was carried as a **diagnosed** one, in a
different file, and the two were never connected. **A defect logged in `FUTURE-WORK` is worth exactly
the grep that finds it again.**

**② The `CLAUDE.md` attribution in this row is wrong.** The word *seek* does not appear in
`CLAUDE.md`, case-insensitively, anywhere. The *"a caller that posts … gets `200` … has been misled"*
quote is `design/DECISION-LOG.md`'s, it is about **`MaxSpeechChars` truncation**, and it cites the
seek throw only as precedent. The same misattribution is in the `BUILDER_QUEUE` row.

**③ Scope question 2 answers "neither", so there is no swallow to find.** The throw is real —
`PrimaryAudioSourceBase.SeekAsync` and `EventAudioSourceBase.SeekAsync` both throw
`NotSupportedException` when `!IsSeekable` — but it is unreachable on this path, because
`FilePlayerAudioSource.IsSeekable` is `true`. And `SeekCoreAsync` returns `Task.CompletedTask` on
every path, so nothing returns false either. **`Seek` silently succeeds.** (Separately: every
production caller pre-checks `IsSeekable` first, so that throw is unreachable from every route in the
app, not just this one.)

**④ ADR-029 §8.3 asserts the opposite of the truth.** It says `FilePlayerAudioSource` *"already
implements seeking over a local file through `SoundFlowPlaybackService`"* — false on both halves, and
false for five weeks. `FUTURE-WORK` §14a said so on 2026-09-02; the ADR still does not. The plan
annotates it.

**⑤ ⛔ THE SPECIFIED CHECK AND THE OBSERVED DEFECT ARE ON DIFFERENT SURFACES.**
`PHN-2` §3 U5 — the check this row was filed against — reads *"Start a **voicemail** at least 20
seconds long. **Tap** the progress bar."* Its chain ends at `AudioFileEventSource.SeekCoreAsync`,
**which is implemented correctly**: it calls `_playbackService!.Seek(_playbackId!, position)`, checks
the `bool`, warns on a refusal and re-arms the completion wait. The owner's report is *"For **Mp3
files**, **dragging** the position bar"* — `NowPlayingPanel` on the Home page, ending at
`FilePlayerAudioSource`. ⭐ **The gesture corroborates it:** `VoicemailPlayer` is tap-to-seek only and
says so in its own comment (*"There is no pointermove handler and never was"*); `NowPlayingPanel`
renders a draggable slider. ⛔ **Corroboration, not proof** — but the consequence stands either way:
**`PHN-2` check #1 as written may still be UNRUN**, and this row must not close it by implication. The
plan's UAT exercises both surfaces and records them separately.

## ⭐ This row's success criterion HOLDS — checked against the ADR, not inherited

ADR-029 §14 **Q3 is an open question, not a specification**: *"Does `SeekAsync` on a small local MP3
behave through SoundFlow? … **If it misbehaves, seek degrades to stop-and-restart-at-offset**."*
`PHN-2` §3 U5's own pass text agrees: *"if the audio restarts from the tapped offset … **that is an
accepted fallback, not a failure**."*

So *"assert the PRESENCE of repositioning"* is right — ⚠ **with the definition sharpened:
*repositioned* means the audio moves, NOT that `SoundPlayerBase.Seek` returned `true`.** A fix that
made the engine call and stopped there would satisfy this row's wording and could still fail the ADR;
the plan carries a contingency task for that case.

⛔ **And §14 Q4, which this row never mentions, is a second requirement:** *"a seek mid-playback must
**re-arm** that timer or completion will fire early."* The file player has the identical wall-clock
completion and no re-arm. It is repaired for free once `_position` and the audio agree — and the
prediction is falsifiable: **on the broken build, dragging to 75% of a four-minute track should end it
about a minute later, mid-song.** The plan's UAT checks it.

## ⛔ The coverage is worse than this row's Verification section said

This row says *"a test that asserts 'seek did not throw' passes today."* **Every seek test in the
solution passes on a build where seek is a total no-op** — not one asserts a player repositioned.
The worst is `FilePlayerAudioSourceTests.SeekAsync_ValidPosition_SeeksSuccessfully`, which asserts
`source.Position == 30s` after `SeekAsync(30s)`: **it asserts precisely the field write that IS the
defect, under a name claiming success.** Two more in `SoundFlowPlaybackServiceTransportTests` are
vacuous by their own in-file admission.

⭐ **The lever that makes a non-vacuous test possible needs no new seam.** `DeviceFreePlaybackService`
— already in the test project — builds a real `SoundFlowPlaybackService` that can never register a
player, so `Seek` returns `false` deterministically. Inject it, seek, and assert the **reported
position did not move**. That is red on `main` today (`main` assigns `_position` on every path) and
green only after the fix, with no hardware and no `InternalsVisibleTo` — which is ADR-030's first rule
observed rather than cited.

---

## ⛔ OWNER RULING 2026-09-10 — the resume rides along, and it was declined

Recorded here so it is not re-litigated. The plan's § 0.6 established that repairing `SeekCoreAsync`
would make **resume-where-you-left-off** start working for free, because `PlayCoreAsync` seeks to the
persisted `SongPositionMs` on the first play after a queue restore — a startup behaviour change
nobody asked for, arriving on a bug fix.

**The owner's decision: guard the resume, ship only the seek fix.**

It is separable in one line, exactly as the plan said: `_pendingSeekMs` is assigned at one site in
`InitializeAsync`, consumed at one site in `PlayCoreAsync`, and `SeekCoreAsync` touches neither. The
assignment is removed; the consumer is kept and documented as dormant, so restoring the feature is
the one line that was taken out rather than a rewrite.

⭐ **This makes behaviour CONSISTENT, which is the point of the ruling.** `InitializeAsync`'s fallback
arm — *"restore just the last played file"* — sets `_position` and never set `_pendingSeekMs`.
Un-guarded, a restored **queue** would have resumed audibly while a restored **last-played file** did
not. Guarded, neither resumes — which is exactly what the appliance has always done.

⛔ **The log line was fixed in the same PR, as the ruling required.** The `Information`-level
*"Restored queue position … (seek to {Ms}ms)"* had claimed a seek that never happened, on every
startup, for the life of the file — the `CLAUDE.md` § Pre-Merge Review class. With the resume
guarded it would have claimed one that definitely cannot happen. It now says the reported position
was set and that playback will start from the beginning of the track.

⚠ **One consequence of the ruling that the plan did not spell out, and it is a real one.** A restored
queue sets `_position` to the saved offset while the audio starts at zero, and
`MonitorPlaybackAsync` accumulates from `_position` — so such a track still ends early, by roughly
the saved offset. **This is pre-existing and not a regression** (it is equally true on `main`, where
the resume seek was a silent no-op), and **un-guarding the resume is what would have fixed it.** If
the early end is ever reported as a bug, this is the row that explains it.

## ⚠ Corrections to the plan, found while shipping it

1. ⛔ **The plan's Task 3 second lint test was vacuous against the mutation the plan itself named as
   its red.** It compared `Regex.Matches(body, @"\bif\s*\(").Count >= Regex.Matches(body,
   @"_position\s*=\s*position\s*;").Count`. **Measured on the mutated method: 4 ifs, 2 assignments**
   — so hoisting the assignment above the refusal guard leaves `4 >= 2` and the test stays GREEN.
   Replaced with an ordering property — every `_position` assignment after the engine call must have
   a `return` between it and that call — which was then **proven red on that exact mutation**. Same
   failure family as the brace-less-`if` hole `PHN-10`'s review found in its own lint.
2. ⚠ **The plan's Task 2 acceptance line says "two new tests red before Task 1". Only one is.**
   `SeekAsync_WhenThePlayerRefuses_StillRangeChecks` passes on `main`, because the range guard
   already precedes the (absent) engine call. The plan's own § 2.1 table is correct about this — it
   reds only under a different mutation — so the acceptance line, not the design, was wrong.
3. ⚠ **The plan's Task 1 comment asserted *"`_position` advances ONLY when the engine says it
   moved"*, which the no-playback-service arm makes false.** Shipped with the hedge made explicit
   rather than as written — a comment claiming an invariant the code does not enforce is the exact
   class this repo has shipped four times.
