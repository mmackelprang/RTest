# `AUD-12` — the BT source stalls at `Ready` while audio is playing

[← Builder Queue index](../BUILDER_QUEUE.md)

> ⛔ **PLAN COLLISION — added 2026-09-08 by `TEST-2` ([#614](https://github.com/mmackelprang/RTest/pull/614)), which invalidated part of this row's plan. READ BEFORE CLAIMING.**
>
> `TEST-2` **retired** `BluetoothAudioSource.ApplyDeferredCaptureState` from `internal` to `private`
> and deleted the tests that entered through it, because the constraint that justified the seam
> turned out not to exist. `design/plans/AUD-12-the-source-that-stalled-at-ready.md` was written
> against the old shape and **no longer compiles as written**:
>
> - `:528` prescribes `internal void ApplyDeferredCaptureState()` — it is now `private`, and the
>   method's own doc says *"do not re-widen this to reach it."*
> - `:699` and `:765` call `_source.ApplyDeferredCaptureState()` from a test — no longer accessible.
> - `:299` and `:834` pin `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady` as
>   `⛔ C-177 MUST NOT BE INVERTED` — **that test no longer exists**; it was superseded by
>   `BluetoothAudioSourceTests.DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady`, which
>   asserts the same Created→Ready invariant through the real dispatch. **`C-177`'s invariant
>   survives; only its test name and entry point changed.**
>
> ⚠ **This is a note, not a re-plan** — Builder does not reshuffle another row's plan. Planner should
> re-point those anchors. The pattern to follow is in `design/TESTING.md` § *Test Seams*: reach the
> state by raising `IBluetoothService.DeviceConnected` on a `Mock<IBluetoothService>` with a mocked
> capture object, not by widening a method.

🟠 **P1.** **Observed live on `radio` 2026-09-06**, not inferred. Rank this **first** of the three
BT rows filed that day: it is the one with a user-visible consequence, and it has prior art to
check against.

## Symptom

Album art never appears for Bluetooth playback. It sits on `/images/default-album-art.png`
indefinitely, while the title and artist are correct.

`/api/audio/nowplaying` simultaneously reports `"isPlaying": false` **while audio is audibly
playing** through the speakers, with every PipeWire node `[active]`.

## Mechanism

`BluetoothAudioSource`'s state machine terminates at `Ready` and never returns to `Playing`.
Measured sequence, `/opt/radio-console/logs/radio-*.txt`:

```
10:16:53  Source state changed: Bluetooth Audio  "Playing" -> "Paused"
10:17:54  Source state changed: Bluetooth Audio  "Paused"  -> "Stopped"
10:18:19  Source state changed: Bluetooth Audio  "Stopped" -> "Ready"     ← terminal
```

It stayed `Ready` indefinitely, while at the same moment the graph was fully connected and audible:

```
radio-bt-stream:input_FL  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]
Radio.API:output_FL       -> SN6140 Analog:playback_FL                   [active]
```

**Background fingerprinting is gated on `Playing`.** Its last activity was `10:08:23`; nothing in
the following ten-plus minutes, against a 15 s interval. No identification means no cover-art
lookup, so the art can never resolve. The album art is a *symptom*; the state stall is the defect.

An earlier transition in the same session reached `Playing` briefly (`10:08:15 Paused -> Playing`)
and fingerprinting ran during exactly that window — which is what produced the one identification
of the session. That is the strongest evidence the gate is the cause: recognition tracks the state,
not the audio.

## ⚠ Check the prior art before writing a plan

`CLAUDE.md` § *Pre-Merge Review* documents this exact class in this exact file, as its own example #2:

> `BluetoothAudioSource` carried *"If source is already Playing … route to mixer now"* two lines
> below an assignment of `State = Ready`, making the `Playing` branch statically unreachable — **BT
> song recognition was silently disabled** (fixed in #469).

Same file, same gate, same silent consequence. **Establish whether this is a recurrence of #469 or
a sibling path before planning a fix** — read #469's diff first. Do not assume either answer: five
rows in the 2026-09-05 planning pass turned out to rest on a premise that did not survive reading
the code.

## Scope questions the plan must answer

1. Why does `Stopped -> Ready` happen at all while audio is flowing, and what is supposed to drive
   `Ready -> Playing` afterwards?
2. Is `isPlaying:false` the same defect surfacing through the API, or a second mapping bug? Say
   which; do not assume.
3. Does anything else gate on `Playing` and fail silently the same way? Fingerprinting is the one
   we caught because it has a visible output. Ducking, play-history and the UI's transport state
   are all candidates.

## Verification

Cannot be closed by a green suite. It needs the box: play over BT, confirm `Playing` in
`/api/audio/nowplaying`, confirm a `SongRec recognized` line appears within ~15 s, and confirm
`albumArtUrl` leaves the placeholder. Pause and resume, then confirm all three still hold —
the stall appeared *after* a pause/resume cycle, so a test that never pauses will pass on a broken
build.

⚠ Do not use `AUD-10`'s reconnect dance as part of the test without noting it: a full reconnect is
currently required to restore audio at all, which can mask this row's transition.

---

## Plan

[`design/plans/AUD-12-the-source-that-stalled-at-ready.md`](../../design/plans/AUD-12-the-source-that-stalled-at-ready.md),
written 2026-09-06 against `main` at `066a0d5c`, **repaired 2026-09-08 against `c9ebd824`**.
**0.5 d.** ⛔ **Not auto-mergeable.**

✅ **The `TEST-2` collision note at the top of this dossier is DISCHARGED.** All three anchors it
named are re-pointed: `ApplyDeferredCaptureState` is `private`; no test calls it; and
`C-177` now pins `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` and
`DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring` instead of the deleted
`ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`. **`TEST-2` also made the row smaller** — its
Task 4 is deleted, so the row touches `BluetoothAudioSource.cs` and nothing else.

**The § *Check the prior art* instruction is DISCHARGED.** Verdict: **sibling path, not a recurrence
of #469.** The handler that swallows the transition (`OnPlaybackStatusChanged`) was last edited
`b717314b`, 2026-03-10 — five months before #469, which never touched it.

**The three scope questions, answered:**

1. *Why `Stopped -> Ready`, and what drives `Ready -> Playing`?* `ApplyDeferredCaptureState` writes
   `Ready`; **nothing** drove it back out. ⚠ **Corrected 2026-09-09 — this answer originally read
   *"the only exit is an AVRCP edge, and BlueZ emits `PropertiesChanged` only on change"*, and both
   halves are false.** `AudioSourceBase` leaves `Ready` at `:96` (`PlayAsync`, unconditional), `:127`
   (`StopAsync`) and `:139` (`DisposeAsync`); what is true is narrower — **no exit fires on its
   own**, so an untouched source stays put. And BlueZ's change-only signalling is not why no edge
   comes: a **fresh** `AttachMediaPlayerAsync` re-reads `Status` and raises one unconditionally, so
   the silence belongs to a **re-attach** at the same object path returning at that method's dedup —
   `C-174`, queue row `AUD-14`. The code comments were corrected during this row's pre-merge review;
   this prose was not, until the post-rebase pass. **The conclusion survives; the reasons did not.**
   A second predicate compounds it: the `Playing` arm discarded an AVRCP `Playing` arriving while
   `Stopped`, which `AUD-10` makes routine.
2. *Is `isPlaying:false` the same defect?* **The same defect** — not a second mapping bug. ⚠ **The
   supporting citation was wrong in both halves and is corrected:** the line is
   `AudioController.cs:575`, not `:576` (`:576` is `IsPaused`), and it is **not a pure projection**
   of `primarySource.State == Playing` — it is a conjunction,
   `_audioEngine.State == AudioEngineState.Running && primarySource.State == AudioSourceState.Playing`.
   The conclusion holds for the measured incident because the engine was `Running` throughout (the
   mixer was audible), but a reader who trusts "pure projection" would wrongly rule the engine out
   as a second cause of `isPlaying:false`.
3. *What else gates on `Playing`?* Fifteen sites, enumerated in the plan's §0.4 — including
   **Sleep**, where `_wasPlayingBeforeSleep` stays false and the phone streams through the night,
   and the **SignalR push path** this dossier's own list missed. ⚠ **Ducking is NOT among them** —
   `DuckingService.cs` has zero `AudioSourceState` references, recorded so nobody looks there.

---

## Build status

**PR #623**, branch `fix/aud-12-bt-source-stalled-at-ready`. ✅ **Merged 2026-09-09 on the owner's
explicit authorisation.** ⚠ **Merged, not deployed.**

⛔⛔ **THE UAT BELOW IS DEFERRED, NOT DISCHARGED. The authorisation moved the gate; it did not
satisfy it.** No phone was connected, no A2DP source was played, no pause/resume was performed, and
the box was never touched — no SSH, no `curl`, no deploy. **The § *Verification* steps at the top of
this dossier, and the plan's §5, still have to be run.** Anything that reads this row as
"UAT complete" is wrong.

### The rebase and the re-gate, 2026-09-09

The PR had been open six days and its gates were green against a `main` **33 commits** older. It was
integrated by **merge rather than rebase** (one conflict resolution instead of seven; no force-push
onto a `git push` that fails over HTTP/2 while printing `Everything up-to-date`; the PR body's four
evidence shas stay resolvable). **One conflict, in `docs/BUILDER_QUEUE.md`'s banner line**, resolved
to `main`'s side.

**`main` was gated first, so the result would be attributable.** `main` @ `17e56719`: 47 warnings /
0 errors, 3,854 passed / 4 failed. Merged branch: 47 warnings / 0 errors, **3,864 passed / 4 failed**
— the same four `SrcVariableResamplerTests` by name, +10 passing cases (this row's eight new test
methods, two of them two-case `[Theory]`), and per-project equality everywhere else. **Zero
regressions.** Mutations `M1` and `M8` were re-run **in the merged tree at whole-project scope**
(1,556 cases, no `--filter`) and killed exactly their predicted tests. A local smoke start of the
merged build served `/api/health/version` reporting `gitSha ae2bd726`, plus `/api/audio/nowplaying`
and `/api/sources` at 200 with zero `[ERR]`/`[FTL]` lines — **that is a boot check, not this row's
UAT.**

⛔⛔ **`AUD-12` IS A COLLIDING ID.** `docs/HANDOFF-GA-PUNCH-LIST.md:1060` carries its own `AUD-12` —
**"PipeWire event subscription in place of polling"**, plan `pw-event-subscription` (2026-05-22),
unqueued and **still open** — also named in that file's P1 roll-up (`:1365`) and plan-traceability
table (`:1431`). It is a different work item, so **"`AUD-12` ✅ shipped" is true of this row and
false of that one.** ⚠ The two schemes diverged from `AUD-10` onward: punch-list `AUD-10` is *BT
disconnect-reason surfacing* vs. this queue's *pausing destroys the A2DP transport*; punch-list
`AUD-11` is *BT codec observability* vs. *the capture that recorded the wrong jack*. `AUD-1`, `AUD-2`,
`AUD-4` and `AUD-5` still agree. **Three colliding ids. Planner to reconcile — nothing was renumbered
here.** ⚠ This PR's own body had claimed `AUD-12` *"has no punch-list row"*; the conclusion (no
punch-list edit needed) was right and the reason was refuted by one grep.

### ⭐ Owner decision 2026-09-08 — `_avrcpReportsPlaying` is cleared on disconnect

Applied in `1ff6070f`. The pre-merge review found that nothing reset the field, while `AudioManager`
caches one `BluetoothAudioSource` per source type for the whole process — and that **this row adds a
promotion on the reconnect path** (`ApplyDeferredCaptureState`, reached from both `OnDeviceConnected`
and `OnCaptureStreamRecovered`). A phone that disconnected while playing therefore made the *next*
session claim `Playing` the moment capture landed, however idle it was: `isPlaying:true` with
fingerprinting running against silence. Demonstrated, not argued — the regression test reads
`Expected: Ready, Actual: Playing` against the unfixed code.

⚠ **What the decision accepts, and it is a choice rather than an oversight.** Clearing means a phone
that **keeps playing across a re-attach at the same D-Bus object path** gets no corrective `Status`
read — `AttachMediaPlayerAsync` returns at its dedup first — so that source parks in `Ready`, which is
`AUD-12`'s own symptom on the reconnect path. **That hole is `C-174`, filed as `AUD-14`, and closes
there. The hole on the other side had no row and no owner.** Recorded in the field's remarks, at the
reset site, and in the test's `<remarks>` so it cannot be reversed by a reader who thinks it was
missed.

⛔ **UAT is DEFERRED — it needs the owner's phone.** A real A2DP source is required to reproduce a
`Ready` stall: without one there is no AVRCP stream, no transport, and nothing to pause. The plan's
§5 carries the exact steps, in the order they must be run. **This is a documented deferral, not a
hand-off** — the automated evidence that stands in the meantime is a RED-then-green run plus seven
falsifying mutations, recorded in the PR body.

⚠⚠ **Whoever runs §5 must read its four-way vacuity table first.** Three of the four make the UAT
lie in the *dangerous* direction:

| # | Condition | Effect |
|---|---|---|
| 1 | `AUD-10` | A reconnect between the pause and the check re-runs `InitializeAsync`, whose catch-up predates this row — **a broken build passes** |
| 2 | `AUD-18` | A dead fingerprint tap (11½ h of zero bytes on 09-07/08) makes **a correct build fail**. Step 0b baselines `grep -c "No audio data captured"` at **0** before anything else |
| 3 | `AUD-1` | Album art is **not** a clean criterion — `UseShazamForAllSources: true` on the box can leave the placeholder after a correct fix |
| 4 | The UAT itself | **Pressing the transport button clears the stall** (`AudioSourceBase.cs:96` writes `Playing` unconditionally). §5 step 4 is ordered non-destructive-first: read the label, then the SongRec line, and only then press |

⭐ **Album art is DEMOTED from headline criterion.** This row was *filed* on the missing art, so that
demotion is worth stating plainly rather than leaving implicit: the art is a symptom two layers
downstream, and `AUD-1` can suppress it independently. **The unambiguous pass criteria are step 3's
`isPlaying: true` and step 4b's `SongRec recognized` line** — that fingerprinting resumes is what
this row promises.
