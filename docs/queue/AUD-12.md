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
named are re-pointed: `ApplyDeferredCaptureState` is `private` at `:457-463`; no test calls it; and
`C-177` now pins `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` and
`DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring` instead of the deleted
`ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`. **`TEST-2` also made the row smaller** — its
Task 4 is deleted, so the row touches `BluetoothAudioSource.cs` and nothing else.

**The § *Check the prior art* instruction is DISCHARGED.** Verdict: **sibling path, not a recurrence
of #469.** The handler that swallows the transition (`OnPlaybackStatusChanged`) was last edited
`b717314b`, 2026-03-10 — five months before #469, which never touched it.

**The three scope questions, answered:**

1. *Why `Stopped -> Ready`, and what drives `Ready -> Playing`?* `ApplyDeferredCaptureState` writes
   `Ready`; **nothing** drove it back out. The only exit is an AVRCP edge, and BlueZ emits
   `PropertiesChanged` only on change — the phone is already playing, so no edge is coming. A second
   predicate compounds it: the `Playing` arm discarded an AVRCP `Playing` arriving while `Stopped`,
   which `AUD-10` makes routine.
2. *Is `isPlaying:false` the same defect?* **The same defect.** `AudioController.cs:576` is a pure
   projection of `primarySource.State == Playing`. Not a second mapping bug.
3. *What else gates on `Playing`?* Fifteen sites, enumerated in the plan's §0.4 — including
   **Sleep**, where `_wasPlayingBeforeSleep` stays false and the phone streams through the night,
   and the **SignalR push path** this dossier's own list missed. ⚠ **Ducking is NOT among them** —
   `DuckingService.cs` has zero `AudioSourceState` references, recorded so nobody looks there.

---

## Build status

**PR #623**, branch `fix/aud-12-bt-source-stalled-at-ready`. ⛔ **Open, NOT merged, and the row is
NOT ✅.**

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
