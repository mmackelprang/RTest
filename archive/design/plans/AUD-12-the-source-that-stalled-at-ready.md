# PLAN — `AUD-12` · The Bluetooth source that stalled at `Ready` while the audio kept playing

> **Row:** `AUD-12`, [`docs/queue/AUD-12.md`](../../docs/queue/AUD-12.md). 🟠 **P1.** Observed live on
> `radio` 2026-09-06; ranked first of the three BT rows filed that day.
> **Branch:** `fix/aud-12-bt-source-stalled-at-ready`
> **Estimate:** **0.5 d.** §0.6 says why half a day survives, and what would push it to one.
> ⛔ **NOT auto-mergeable.** §0.8.
> **Planned against** `main` at `066a0d5c`; ⭐ **REPAIRED and re-anchored 2026-09-08 against `main` at
> **`c9ebd824`**.** Every line number below was re-read out of that tree; where a line is likely to
> move it is quoted as well as numbered.
> **The investigation is closed.** A `team-debugger` pass on 2026-09-06 settled the verdict at ~85%
> confidence: **sibling path, not a recurrence of #469.** §0.2 records the git fact that closes it.
> This plan re-verified every anchor and does not re-litigate the verdict.

---

## ⭐ 0.0 What the 2026-09-08 repair changed — read this before anything else

`TEST-2` shipped as [#614](https://github.com/mmackelprang/RTest/pull/614) on 2026-09-08 and
invalidated part of this plan. `docs/queue/AUD-12.md` carries the collision warning its Builder left.
**This section is the response to it.** Every change below was derived by reading `main` at
`c9ebd824`, not by reading `TEST-2`'s plan.

**The three things `TEST-2` actually did to this row's surface:**

1. **`ApplyDeferredCaptureState` went `internal` → `private`** (`BluetoothAudioSource.cs:457`), and its
   doc now says *"do not re-widen this to reach it."* Task 2c is re-pointed and its signature
   corrected; §0.9 gains a ⛔.
2. **All three tests this plan leaned on were deleted.** `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`,
   `DeferredCaptureAcquisition_AfterPlay_LeavesSourcePlaying` and
   `DeferredCaptureAcquisition_AfterPlay_KeepsAudioTapActive` are all gone from
   `BluetoothAudioSourceTests.cs`. `C-177` is re-pointed at the survivors; §4.7 is rebuilt.
3. **Everything in `BluetoothAudioSource.cs` below `:445` moved +3.** The doc comment grew by three
   lines and nothing else in the file changed. `AUD-1`'s plan predicted this shift exactly (its §0.14);
   this plan now carries the moved numbers rather than the prediction.

**And one thing `TEST-2` did that makes this row *smaller*:**

⭐ **Task 4 is deleted.** It existed because this plan asserted that `HasCapturePath` *"cannot be made
true from a unit test today … none is reachable without a native SoundFlow engine."* **That was the
same false premise `TEST-2` was filed on and then refuted.** `Mock<T>` subclasses both
`AudioCaptureDevice` and `SoundComponent` with a `null!` engine — their constructors store the
reference without dereferencing it — so `_captureDevice != null` and `SoundComponent != null` are both
reachable now, and `IsAudioManagedByPlatform` is reachable through a plain
`Mock<IBluetoothService>`. **No production-tree change is needed for the test seam**, which also means
this row no longer touches `MockBluetoothService.cs` at all. `design/TESTING.md` § *Test Seams* rule 1
is now the governing rule, and it names this exact case.

**Four defects found while repairing that have nothing to do with `TEST-2`** — §0.4's corrections plus
`C-173`, `C-181`, `C-182` and `C-183`. The worst is `C-173`: its endpoint, its branch condition **and**
its log string were all wrong, and §5 step 4 told a tester to grep for a string that never appears on
the path it describes. `C-181` is worse in consequence: **the UAT's own step 4 could clear the stall
before it was recorded.**

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`BluetoothAudioSource` has a state machine whose `Ready` state is, in practice, terminal. Two
predicates put it there and neither can get it out. `OnPlaybackStatusChanged` (`:1136`) accepts an
AVRCP `Playing` edge only from `Ready` or `Paused`, so one arriving while the source is `Stopped` —
which `AUD-10` makes routine — is written into metadata and then silently discarded.
`ApplyDeferredCaptureState` (`:459-462`) *preserves* `Playing` but never *re-derives* it, so a source
that reaches that method in any other state lands in `Ready`. And `Ready` has no other exit, because
the only route out is an AVRCP edge and BlueZ emits `PropertiesChanged` **only on change**
(`LinuxBluetoothService.OnPlayerPropertiesChanged:2708-2731`) — the phone is already playing, so no
further edge is coming. Everything downstream that asks *"is this source playing?"* then answers no,
while the mixer is audibly streaming. The album art the row was filed for is the visible end of that:
fingerprinting is gated on `Playing`, no identification means no cover-art lookup, so the art can
never resolve.

The fix is two predicates and one shared helper. §1 is the design; §0.3 is the scope question the
brief flagged, and its answer changed the design.

### 0.2 ⭐ #469 is not the cause, and that is a git fact rather than an inference

`CLAUDE.md` § *Pre-Merge Review* documents this exact class in this exact file as its own example #2,
and the row correctly told the planner to check it before writing anything. Checked at `066a0d5c`,
**re-checked at `c9ebd824`**:

- **`ApplyDeferredCaptureState` is present** at `BluetoothAudioSource.cs:457-463` — ⚠ **`private`
  since `TEST-2`, not `internal`** — with its XML doc at `:435-456` (`TEST-2` appended a `<para>`
  recording the narrowing), and all three call sites live: `:472` (platform-managed arm), `:489`
  (`AudioCaptureDevice` arm), `:500` (`SoundComponent` arm).
- ⛔ **Its three tests DO NOT survive. `TEST-2` deleted all of them.** This bullet previously read
  *"Its tests survive at `BluetoothAudioSourceTests.cs:907-957`, all three of them"* and that
  sentence is now false. They are superseded by `DeviceConnectedEvent_*` and
  `DeferredCaptureAcquisition_ThroughDispatch_KeepsAudioTapActive` — see §4.7 for the mapping. **This
  does not disturb the #469 verdict**, which rests on the git facts below and not on those tests.
- **The handler that actually swallows the transition was never touched by #469.**
  `git log -L 1123,1151:src/…/BluetoothAudioSource.cs` (the handler's range at `066a0d5c`; it is
  `1126,1154` at `c9ebd824`) returns `b717314b` (2026-03-10, *"Eliminate all build warnings and clean
  up test output"* — braces only) as the most recent change to `OnPlaybackStatusChanged`.
  `ApplyDeferredCaptureState` was introduced by `9bfb7cbe` (2026-08-10). **Five months apart, and #469
  never edited the handler.**

⭐ **What #469 *did* do is make this row's second half visible.** Its XML doc at `:439-445` states the
invariant in so many words — *"demoting to `Ready` here silently kills fingerprinting while audio
keeps flowing through the mixer"* — and then the method only enforces one direction of it. The
comment is true; the code is half of it. That is not the comment-accuracy defect `CLAUDE.md` warns
about, but it is next door to it, and it is why the fix belongs in that method rather than beside it.

### 0.3 ⭐⭐ The scope question the brief raised, answered: `PlaybackStatus` cannot be cleared — and the fix must not read it anyway

**The question.** The suggested fix re-derives state from `MetadataInternal["PlaybackStatus"]`. A live
`/api/audio/nowplaying` capture at 10:24 showed no `PlaybackStatus` key at all. If that key can go
missing, a fix built on it does nothing in exactly the situation it was written for.

**The answer, in three parts.**

**(a) The 10:24 observation carries no information about the Bluetooth source.** Its
`extendedMetadata` held `Frequency` / `SignalStrength` / `Stereo` / `Genre` / `Year` / … — the
**Radio** source's keys, captured after a user source-switch. `AudioController.cs:576` projects
`primarySource`, the *active* source, and `AudioDtoMapper.ExtractMetadataToNowPlaying` (`:134`, block
`:148-156`) builds `ExtendedMetadata` as
`metadata.Keys.Except(new[] { "Title", "Artist", "Album", "AlbumArtUrl" })` —
**not an allowlist**. So the projection would have shown `PlaybackStatus` had the dictionary been the
BT source's and had the key been there. The capture is fully explained by *which source was active*.

**(b) The key cannot be removed or cleared. Verified by exhaustive grep over `src/`; re-verified at
`c9ebd824`.**

| Fact | Evidence |
|---|---|
| Written in exactly one place | `BluetoothAudioSource.cs:1128`, `MetadataInternal["PlaybackStatus"] = e.ToString();` — unconditional, **above** the `switch`, so it records every AVRCP report including the discarded ones |
| Read in exactly one place | `:189`, the `InitializeAsync` catch-up |
| `MetadataInternal.Clear()` | **does not appear anywhere in `src/`** |
| `MetadataInternal.Remove(...)` | one site, `:808`, and it removes `AlbumArtUrl` only |
| `SetDefaultMetadata` (`:743`, on disconnect) | **does not clear** — `USBAudioSourceBase.cs:157-165` assigns six keys — `StandardMetadataKeys.Title` (`:159`), `.Artist` (`:160`), `.Album` (`:161`), `.AlbumArtUrl` (`:162`), and the literals `"Source"` (`:163`) and `"Device"` (`:164`) — and `PlaybackStatus` is not among them |
| The source instance is long-lived | `AudioManager` (`src/Radio.Infrastructure/Audio/Services/AudioManager.cs`) caches one per `AudioSourceType` (`:403 _sourceCache[sourceType] = source;`) and clears the cache only at disposal (`:611 _sourceCache.Clear();`) — so the dictionary survives every source switch for the process's life |

⇒ **Once written, the key is sticky.** It is absent only in the window between a source's construction
and the first AVRCP status event it ever sees — and in that window there *is* no last-known status to
catch up to, so a catch-up correctly does nothing. **There is no "durable last-known-status field"
gap to close.**

**(c) ⛔ And the fix still must not read it.** Two independent reasons, and either alone is enough:

1. **It is a display projection, not a state-machine input.** `_metadata` is a plain
   `Dictionary<string, object>` (`USBAudioSourceBase.cs:26`), written from D-Bus callback threads
   (`OnPlaybackStatusChanged`, `OnMetadataChanged`) and **enumerated on ASP.NET request threads** by
   `AudioDtoMapper.cs:148-156` (`src/Radio.API/Mappers/`). That is a pre-existing hazard this row does
   not fix (`C-175`), and routing the state machine's only recovery path through it makes the hazard
   load-bearing.
2. **The existing read is an unguarded cast.** `:190` is `(string)pbStatus == "Playing"` — an
   `InvalidCastException` the day anything writes a non-string under that key.

⇒ The fix records the same fact in a dedicated field and **keeps writing the metadata key unchanged**,
because that key is a shipped API observable (`PlaybackStatusChanged_UpdatesMetadata`,
`BluetoothAudioSourceTests.cs:191-197` — ✅ still present and unmoved after `TEST-2`) and the UAT
confirmation in §5 depends on it.

### 0.4 The blast radius — re-verified, with three corrections to the row's enumeration

Everything below is gated on `State == AudioSourceState.Playing` and therefore silently wrong while
the source sits at `Ready`. ⭐ **Every line was re-derived at `c9ebd824` during the 2026-09-08 repair.**
Only four rows moved, and **two rows were plain wrong** — see the ⛔ rows.

| Site | Line | What breaks |
|---|---|---|
| `Audio/Fingerprinting/SoundFlowAudioTap.cs` | **`:135`** `return activeSource?.State == AudioSourceState.Playing;` | `IsActive` → `CaptureAsync` (guard `:142`, `return null;` at `:149`). **The one we caught, because it has a visible output.** |
| `Audio/Services/PlayHistoryTracker.cs` | **`:95`** `if (e.NewState != AudioSourceState.Playing)` (`return;` at `:97`) | Play history never records the track. Silent return, no log. |
| `PrimaryAudioSourceBase.cs` | **`:112`**, **`:126`** | `PauseAsync` (`:109`) / `ResumeAsync` return early. |
| `AudioController.cs` | **`:215-216`** | Logs `"Paused playback"` after a pause that did not happen. |
| `AudioController.cs` | **`:576`** | `nowPlaying.IsPlaying` — the `isPlaying:false` in the row. (`:577` `IsPaused` is also false.) |
| `AudioController.cs` | **`:68-69`** | ⛔ **ROUTE CORRECTED.** The lines are right; the endpoint is **`GET /api/audio`**, not `GET /api/audio/state`. The controller is `[Route("api/[controller]")]` and this is a bare `[HttpGet]` at `:53`. **There is no `state` sub-route in the file** — the only `HttpGet` sub-routes are `volume`, `nowplaying`, `sourcegain`, `fingerprint/status`. A UAT that curls `/api/audio/state` gets a 404 and learns nothing. |
| `AudioController.cs` | **`:191-203`** | ⛔ **ENDPOINT CORRECTED — this row previously cited `:276-279`, which is a different endpoint.** The dock button posts to **`POST /api/audio`** (`UpdatePlaybackState`, `:124`). Its `PlaybackAction.Play` arm branches **only on `Paused`** (`:194` → `ResumeAsync`, log `"Resumed playback from paused state"` at `:197`); a stalled `Ready` source falls to the `else` at `:199` → **`PlayAsync()` at `:201`, logged `"Started playback"` at `:202`.** `C-173`. |
| `AudioController.cs` | `:276-281` | The *other* endpoint — **`POST /api/audio/start`** (`StartPlayback`, `:255`) — which really does branch on `Stopped \|\| Ready` (`:276-277`) → `PlayAsync()` (`:279`) → `"Resumed playback on {Source}"` (`:280`). ⚠ **Nothing in the transport UI reaches it**, so it is not the UAT tell this plan used to claim. |
| `AudioStateUpdateService.cs` | **`:674`**, **`:704`** | The **SignalR push** payloads. ⭐ This, not the controller, is what actually drives the live Blazor UI. |
| `AudioStateUpdateService.cs` | **`:519`**, **`:546`** | Change detection: `IsPlaying` never flips, so the transition contributes no dirty signal and no push happens unless some other field also moved. |
| `Components/Shared/NowPlayingDock.razor` | `:48`, `:81`, `:82`, `:219`, **`:238`**, **`:273`** | Button shows "Play" while playing — and `:273` `var action = _isPlaying ? "Pause" : "Play";` is what makes pressing it **send `Play`** (to `POST /api/audio`, `:274`). All six unmoved. |
| `Components/Shared/NowPlayingPanel.razor` | **`:21`**, **`:322`**, **`:328`**, `:710`, `:766`, `:989` | Same, plus the ken-burns gate. ⚠ **MOVED:** the three assignment/handler sites drifted **+32** since `066a0d5c` (`:678→:710`, `:734→:766`, `:957→:989`, the last inside `HandlePlayPauseAsync`, declared `:985`). `:21`/`:322`/`:328` are unmoved. |
| `BluetoothAudioSource.cs` | `:643`, `:656` | ⚠ **MOVED +3.** The capture retry loop bails at the first 10 s tick (`:643`) **and** suppresses its own `"capture retry exhausted"` warning (guard `:656`, log `:658`). |
| `Radio.API/Services/SleepService.cs` | **`:299-300`**, `:302`, `:305` | `_wasPlayingBeforeSleep` stays `false`, `PauseAsync` is never called, **the phone streams through sleep**. `:316` still mutes the output, so it is inaudible but running. |
| `Audio/Services/AudioManager.cs` | **`:273`** (call at `:278`) | Switching back to a stalled BT source re-invokes `PlayAsync` on a source already streaming (`:259` sets `Bluetooth => true` for auto-play). |

⚠ **Four paths in this table were wrong in the pre-repair revision and would not resolve if pasted:**
`SleepService.cs` is in `src/Radio.API/Services/`, not `Radio.Infrastructure`; `AudioManager.cs` and
`PlayHistoryTracker.cs` are in `src/Radio.Infrastructure/Audio/Services/`; `AudioDtoMapper.cs` is in
`src/Radio.API/Mappers/`; both `.razor` files are in `src/Radio.Web/Components/Shared/`. Corrected in
the column above.

**Three corrections to the enumeration the row inherited, all verified:**

1. ⚠ **Pause/Resume are NOT silent.** `PrimaryAudioSourceBase.cs:114` and `:128` each
   `LogWarning("Cannot pause/resume {SourceId} - not playing/paused (state: {State})", …)`. The
   journal therefore contains a **contradiction**, not a silence: a `Warning` saying the pause was
   refused, one frame under an `Information` saying `"Paused playback"`. `C-172`.
2. ⚠ **`SoundFlowAudioTap.cs:103`'s `is BluetoothAudioSource` branch is a different property.** It
   belongs to `NeedsFingerprintingLookup` (`:92-121`), which is **not** gated on `Playing`. Do not
   describe it as part of the same gate.
3. ⭐ **The SignalR path was missing entirely** (`AudioStateUpdateService.cs:674`/`:704`). It is the
   one the UI actually consumes. `C-171`.

**⚠ Ducking is NOT affected, and this negative is recorded so a fixer does not go looking.**
`src/Radio.Infrastructure/Audio/Services/DuckingService.cs` (625 lines) has **zero** matches for
`AudioSourceState`, and zero for `IsPlaying` or `.State`. Ducking is driven by event counts and
levels only.

**`isPlaying:false` is the same defect, not a second mapping bug.** `AudioController.cs:576` is
`nowPlaying.IsPlaying = _audioEngine.State == AudioEngineState.Running && primarySource.State ==
AudioSourceState.Playing;` — a pure projection with no fallback. With the engine `Running`, the
`false` is entirely attributable to the source state. (`:577` `IsPaused` also reads false, so the DTO
reports neither playing nor paused, which is itself a tell.)

### 0.5 ⚠ Why one predicate is not enough — trace the measured sequence

The row's own log is the argument for fixing both halves. Reconstructed against the code:

| Time | Log | Code path |
|---|---|---|
| 10:16:53 | `Playing -> Paused` | `:1147` `case Paused when State == Playing` |
| 10:17:54 | `Paused -> Stopped` | `:1150` `case Stopped when State == Playing \|\| State == Paused` |
| *(unlogged)* | — | Phone resumes. AVRCP `Playing` arrives while `State == Stopped`. `:1128` writes `"Playing"` into metadata; `:1136`'s accept set is `Ready \|\| Paused`, so the transition is **discarded**. |
| 10:18:19 | `Stopped -> Ready` | `ApplyDeferredCaptureState:461`. Nothing in `OnPlaybackStatusChanged` writes `Ready`. |
| 10:18:19 → ∞ | *nothing* | `Ready` is terminal. BlueZ already reported `Playing` and emits only on change. |

⇒ **Widening `:1136` alone does not fix the measured stall**, because at the moment the swallowed
edge arrived the source had no capture path yet (see `C-170`'s guard) and the promotion would be
refused. ⇒ **The catch-up in `ApplyDeferredCaptureState` is the load-bearing half**; the `:1136`
widening is the fast path that stops the same stall re-forming a different way. Ship both.

⭐ **Corroborating negative evidence.** The retry loop's exhaustion warning
(`:658 "capture retry exhausted after {Max} attempts"`) does **not** appear in the measured window —
consistent with `:643` `if (_playbackId != null || State != AudioSourceState.Playing) { return; }`
returning on the first tick because the source was no longer `Playing`. The absence of that line is
evidence *for* the verdict, not against it. `C-179`.

### 0.6 The estimate

**0.5 d — unchanged by the 2026-09-08 repair**, and the two halves of that happen to cancel. `TEST-2`
*removed* work (Task 4 is deleted; no production-tree test seam is needed) and *added* work (the new
tests must be built on `TEST-2`'s `Mock<IBluetoothService>` harness rather than on the fixture's
`_source`). Net: the same half day.

1. **Four edits in one file**, all inside methods this plan quotes in full: one new field, one new
   helper, one new guard property, one widened predicate and one replaced `if`. No new types, no DI
   change, no migration, no UI. **And now genuinely one file** — the repair removed the
   `MockBluetoothService.cs` edit.
2. ⭐ **The harness `TEST-2` built is exactly the one this row needs**, and it is why the estimate
   holds. `BuildBtMock` / `BuildSource` / `RaiseDeviceConnected` / `NewCaptureMock` / `WaitForAsync`
   (`BluetoothAudioSourceTests.cs:990-1040`, `:1258-1267`) already stand up a `BluetoothAudioSource`
   over a `Mock<IBluetoothService>` with a mocked capture object and drive the deferred-acquisition
   path through the real `DeviceConnected` event. **Every §4 test below is that harness plus AVRCP
   edges.** `C-176`.
3. **The negative case is still pinned**, by `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady`
   (`:1068-1091`, a `[Theory]` over both capture kinds) and
   `DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring` (`:1147-1168`), which this
   row must leave passing unchanged. `C-177`. *(The pre-repair revision named
   `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady` at `:946-957`; that test no longer exists.)*

⚠ **What would push it to 1 d:** Task 3's `HasCapturePath`. If the Builder finds a real case where a
`Stopped` source legitimately holds a capture path it should *not* be promoted from — or, the reverse,
a promotion the guard wrongly refuses on the box — that is a design conversation, not a tweak. **Do
not widen or narrow the guard silently; say so in the PR body and put it to the owner.**

### 0.7 ⚠ Sixteen constraints — twelve found while planning, four added by the 2026-09-08 repair

**`C-169` answers the row's open scope question. `C-170` changes the design. `C-171`, `C-172` and
`C-173` correct the row's own blast-radius enumeration. `C-174` promotes an "adjacent" defect into
this row's justification. ⭐ `C-181`–`C-184` are new, and `C-181` is the one that would have made a
UAT lie.**

---

**`C-169` — ⚠ ANSWERS THE SCOPE QUESTION. `MetadataInternal["PlaybackStatus"]` cannot be absent or
cleared once written; the 10:24 capture is explained by source identity, not by a missing key. The fix
still must not read it.** §0.3 has the full derivation and the grep table. The consequence for the
design: a dedicated field, and the metadata key is preserved untouched as the API observable.

---

**`C-170` — ⚠⚠ CHANGES THE DESIGN. `Stopped` has TWO provenances and only one of them is safe to
promote out of. The suggested "add `Stopped` to `:1136`'s accept set" is unguarded.**

- `:1150` — the **phone's** transport stopped. The source's own pipeline is intact: `_playbackId`,
  `_captureDevice` / `SoundComponent` all still hold. Promoting back to `Playing` is correct.
- `:742` — **`OnDeviceDisconnected`.** By the time it assigns `Stopped`, `:717-738` has already
  removed the generator from the mixer, stopped and nulled `_captureDevice`, and nulled
  `SoundComponent` and `_captureGenerator`. Promoting *this* `Stopped` to `Playing` asserts audio is
  flowing from a phone that is not connected — the inverse of the bug being fixed, and worse, because
  `:1144` would then fire `TryReacquireCaptureAsync` into a device that is gone.

⚠ **And the second case is reachable, not theoretical** — see `C-174`. The guard is
`HasCapturePath`, defined in Task 3.

---

**`C-171` — ⚠ CORRECTS THE ROW. The UI's real source of truth is the SignalR push, not the
controller.** `AudioStateUpdateService.cs:674` and `:704` project `IsPlaying` from
`activeSource?.State == AudioSourceState.Playing` into `BuildPlaybackStateDto` / `BuildNowPlayingDto`,
and those are what `NowPlayingDock.razor:219`/`:238` and `NowPlayingPanel.razor:678`/`:734` consume.
The row's enumeration named only `AudioController.cs:576`. ⚠ **Second-order and worth knowing for
UAT:** `:519`/`:546` use `IsPlaying` in change detection, so while the flag never flips, the
play/pause transition contributes *no* dirty signal — which is why the panel can look frozen rather
than merely wrong. **No code change here** (all four are correct projections of a wrong input); it
changes what §5's UAT looks at.

---

**`C-172` — ⚠ CORRECTS THE ROW. Pause and Resume are not silent no-ops — they log at `Warning`. What
is silent is the caller.** `PrimaryAudioSourceBase.cs:114` / `:128`. Paired with
`AudioController.cs:215-216`'s unconditional `LogInformation("Paused playback")`, the journal carries
a self-contradicting pair. ⛔ **Do not fix the controller line in this row** — `PauseAsync` returns
`Task`, so making the log honest means changing a base-class signature and every caller. §7.2 files
it.

---

**`C-173` — ⛔ REWRITTEN 2026-09-08. THE PRE-REPAIR VERSION NAMED THE WRONG ENDPOINT, THE WRONG BRANCH
AND THE WRONG LOG STRING, AND ITS CONCLUSION WAS ALSO WRONG.**

It read: *"`AudioController.cs:276-277` branches on `State == Stopped || State == Ready` → `PlayAsync()`
at `:279`, then logs `"Resumed playback on {Source}"`. So the button that shows "Play"
(`NowPlayingDock.razor:273`) tears down and re-establishes a stream that was already running."* **Every
clause of that is false**, traced end to end at `c9ebd824`:

| Claim | Reality |
|---|---|
| The dock button hits `:276-279` | It does not. `NowPlayingDock.razor:273-274` posts `UpdatePlaybackRequest("Play", …)` to **`POST /api/audio`** (`UpdatePlaybackState`, `:124`). `:276-281` is `StartPlayback` (`POST /api/audio/start`, `:255`), which the transport UI never calls. |
| It branches on `Stopped \|\| Ready` | The play/pause arm branches **only on `Paused`** (`:194`). A `Ready` source falls to the `else` at `:199`. |
| It logs `"Resumed playback on {Source}"` | It logs **`"Started playback"`** (`:202`). `"Resumed playback on {Source}"` is `:280`, on the endpoint nothing reaches. ⛔ **§5 step 4 told a tester to look for a string that cannot appear.** |
| It "tears down and re-establishes" the stream | It does **not**. `PlayAsync` (`AudioSourceBase.cs:81`) skips `InitializeAsync` because the state is `Ready`, not `Created`; `PlayCoreAsync` finds `_captureDevice != null` and calls `RouteCaptureThroughMixerAsync`, which **returns at `:532`** because `_playbackId != null`, logging `"capture already routed to mixer"`. Nothing is torn down. |

⇒ **What actually happens is worse for verification and better for the user**, and it is `C-181`:
`AudioSourceBase.cs:96` sets `State = AudioSourceState.Playing` unconditionally after `PlayCoreAsync`,
so **pressing the button silently clears the stall.** `C-173` survives only as the observation that
the button *label* is wrong and that pressing it sends `Play` rather than `Pause`. **The label, not
the press, is the UAT tell.**

---

**`C-174` — ⭐⭐ THE "ADJACENT" `LinuxBluetoothService` DEFECT IS WHY `C-170`'s GUARD EXISTS. It is not
merely nearby.**

`AttachMediaPlayerAsync:2534-2540` dedups on `_mediaPlayerPath == objectPath && _mediaPlayer != null`,
and `_mediaPlayer` is **never nulled**, because `OnInterfaceRemoved:929-932` returns early for any
interface that is not `Device1`:

```csharp
    if (Array.IndexOf(change.interfaces, Linux.BluezConstants.DeviceInterface) < 0)
    {
      return;
    }
```

BlueZ removes `MediaPlayer1` on disconnect; that removal is ignored. Two consequences, and the second
is this row's business:

1. A re-attach at the same path takes the `return` at `:2539` **before** `:2549-2556`'s initial
   `Status` / `Track` read, so BlueZ's own catch-up never runs.
2. ⚠ **The dedup also returns before `_playerPropertiesWatcher?.Dispose()` at `:2542`.** The watcher
   from the *previous* player stays subscribed to that D-Bus path. So an AVRCP `Playing` **can** reach
   a source that `OnDeviceDisconnected` has already torn down and parked at `Stopped` — which is
   exactly `C-170`'s unsafe case.

⛔ **Not fixed here.** It is a BlueZ-lifecycle change on the live audio path with its own UAT.
✅ **The recommendation was acted on: the row exists as `AUD-14`** (`docs/queue/AUD-14.md`,
`docs/BUILDER_QUEUE.md:44`), filed from this plan's §7.1 and citing it. Its row says explicitly that
**`AUD-12` must NOT wait for it**, and that if `AUD-14` lands first the `HasCapturePath` guard becomes
belt-and-braces rather than load-bearing. ⚠ `AUD-14`'s row cites the dedup at `LinuxBluetoothService.cs:2536-2542`;
this plan cites `:2534-2540`. Both are right about different things — `:2534` is the enclosing
`lock (_mediaPlayerLock)`, `:2536` is the `if` condition itself, `:2539` the `return`, `:2542` the
`_playerPropertiesWatcher?.Dispose()`. Verified unmoved at `c9ebd824`.

---

**`C-175` — the metadata bag is unsynchronized, this row does not fix it, and that is the second
reason not to build the fix on it.** `USBAudioSourceBase.cs:26`
`private readonly Dictionary<string, object> _metadata = new();`, exposed as both `Metadata` (`:83`)
and `MetadataInternal` (`:98`) — **the same object**. Written from D-Bus callback threads; enumerated
on request threads at `AudioDtoMapper.cs:148-155`. A concurrent write during that enumeration throws
`InvalidOperationException`. Pre-existing, unrelated to this row's symptom, filed in §7.3.

---

**`C-176` — ⚠ REWRITTEN 2026-09-08. The determinism story is still complete, but it is now `TEST-2`'s,
and it is NOT the "no clock at all" story this constraint used to tell.**

The pre-repair version said every assertion is *"a straight-line read after a synchronous call"* and
banned `Task.Delay` outright. That was true of tests built on the fixture's `MockBluetoothService`.
**The deferred-capture tests cannot be built that way** — `MockBluetoothService.GetAudioCaptureDeviceAsync`
(`:126-129`) returns `Task.FromResult<object?>("mock-capture-endpoint")`, a **boxed string**, so it can
only ever reach the `else` at `BluetoothAudioSource.cs:508`, which never calls
`ApplyDeferredCaptureState`. *(The pre-repair text called this "returns a `string`"; the signature is
`Task<object?>`. The conclusion was right, the description was not.)*

**So §4's new tests use `TEST-2`'s harness, and its determinism argument, verbatim:**

- `OnDeviceConnected` (`:361`) starts `TryAcquireAudioCaptureAsync` by **direct invocation** at `:370`,
  not `Task.Run`, so the handler runs to completion **synchronously inside `btMock.Raise`**.
- `BuildBtMock`'s `GetAudioCaptureDeviceAsync` returns `Task.FromResult`, so there is no incomplete
  await on the path, and `_routeLock.WaitAsync()` is uncontended.
- `AudioSourceBase.State`'s setter (`:40-54`) still raises `StateChanged` inline.

⚠⚠ **`WaitForAsync` (`:1258-1267`) IS a wall clock and is NOT starvation-proof — its own remarks say
so, and say it was measured wrong twice before landing.** It fails at its own closing assertion when
the deadline passes. It is safe here only because the condition is already true on its first
evaluation, so it never actually awaits. ⛔ **Use `WaitForAsync` and nothing else — no bare
`Task.Delay` — and if you add an await point to the acquisition path, these tests become races.**
(A `Task.Delay(100)` exists at `:891` in an unrelated fingerprinting test and is not a precedent.)

---

**`C-177` — ⛔ RE-POINTED 2026-09-08. THE INVARIANT SURVIVES; ITS TEST DOES NOT.**

The pre-repair version pinned `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`
(`BluetoothAudioSourceTests.cs:946-957`) as MUST NOT BE INVERTED. ⛔ **`TEST-2` deleted that test.**
Leaving the constraint pointing at it would be a constraint pointing at nothing, so it is re-pointed
rather than retracted — **the invariant it protects is unchanged and still load-bearing.**

**The invariant:** a source that has never seen an AVRCP `Playing` — a freshly-constructed one
included — lands in `Ready` and **must not be promoted**. The design in §1 preserves it by
construction: `_avrcpReportsPlaying` defaults to `false`.

**The surviving tests that pin it, both of which must pass unmodified:**

| Test | Line | What it pins |
|---|---|---|
| `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` | `:1068-1091` | `Created → Ready` through the **real dispatch** at `:489`/`:500`. A `[Theory]` over both capture kinds, so it is two cases, not one. |
| `DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring` | `:1147-1168` | The same `Created → Ready`, through the **third** `ApplyDeferredCaptureState` call site (`:472`). |

⭐ **The re-pointed pin is strictly stronger than the one it replaces.** The deleted test called the
method directly through a Kind-D seam — which, per `design/TESTING.md` § *Test Seams*, executed the
state decision **without** executing the dispatch that selects it. Both survivors enter through
`IBluetoothService.DeviceConnected`. **A Builder who finds themselves editing either has the design
wrong.**

---

**`C-178` — ⚠ `AUD-10` both causes this row's `Stopped` state and poisons its UAT.** The row it
depends on for its own realism is the one that makes it hard to verify: pausing on the phone destroys
the A2DP transport and resume never restores it, so a full reconnect is currently required to get
audio back — and a reconnect re-runs `InitializeAsync`, which **already** contained a catch-up. ⛔ **A
UAT that reconnects between the pause and the check proves nothing.** §5 step 5 says how to avoid it.

---

**`C-179` — the missing `"capture retry exhausted"` line is evidence for the verdict.** `:656`
`if (State == AudioSourceState.Playing && _playbackId == null)` gates that warning (logged at `:658`),
and `:643` returns
out of the loop entirely once the state leaves `Playing`. A stalled source therefore loses its capture
**invisibly**. Recorded so the Builder does not read the log's silence as "the retry loop was fine".

---

**`C-180` — recorded negatives, so nobody re-derives them.** Checked and **not** affected by a BT
source stalled at `Ready`: `DuckingService.cs` (zero `AudioSourceState` references);
`EventPlaybackService.cs:365` (operates on **event** sources via `Resolve(playbackId).Source`, not the
primary source); `QueueController.cs:455` (`IsPlaying` from engine state only, never source state);
and the state comparisons in `FilePlayerAudioSource`, `SDRRadioAudioSource`, `RadioAudioSource`,
`TTSEventSource`, `EventAudioSourceBase` and `USBAudioSourceBase:468`, none of which is reachable from
a `BluetoothAudioSource` instance. ✅ **`DuckingService.cs` re-probed at `c9ebd824`: 0 matches for
`AudioSourceState`, 0 for `IsPlaying`, 0 for `.State`, across 625 lines.**

---

**`C-181` — ⛔⛔ NEW, AND IT IS THE ONE THAT WOULD HAVE MADE THE UAT LIE. PRESSING THE TRANSPORT BUTTON
CLEARS THE STALL.**

`AudioSourceBase.PlayAsync` (`:81-97`) ends with `State = AudioSourceState.Playing;` at **`:96`**,
unconditionally, after `PlayCoreAsync` returns. For a stalled BT source `PlayCoreAsync` is a no-op —
`_captureDevice != null`, so `RouteCaptureThroughMixerAsync` returns at `:532` because `_playbackId`
is already set. **So the whole effect of pressing "Play" on a source stuck at `Ready` is to write
`Playing`.** The bug disappears, on a build that still has it.

⛔ **The pre-repair §5 step 4 instructed the tester to press that button and then check fingerprinting
and album art.** On a BROKEN build those two checks would have passed, because step 4 itself fixed the
state before measuring it. **`§5` is re-ordered: observe the label, record step 3's result, and only
then touch the transport.** This is a fourth way this row's UAT can be vacuous, and unlike `AUD-10`,
`AUD-18` and `AUD-1` it was created by the plan.

---

**`C-182` — ⚠ NEW. `AUD-18` is a THIRD external way this UAT goes silently vacuous: if the fingerprint
tap is dead, a `Ready`-stall test proves nothing.**

`AUD-18` (filed 2026-09-08, `docs/queue/AUD-18.md`) records the fingerprint tap returning **zero bytes
for 11 h 23 m** on 2026-09-07/08 — 240 failures an hour, one per 15 s capture window — with **zero
`TrackMetadata` rows created for the entire window**, confirmed independently in the fingerprint DB.
It recovered on a *source-stream* restart with `radio-api` uptime unbroken, and needed ~37 h of uptime
to appear.

⚠ **This row's headline UAT criteria — a `SongRec recognized` line and `albumArtUrl` leaving the
placeholder — are both downstream of that tap.** If it is dead they fail on a *correct* build.
**Check it before trusting any §5 result**, mirroring `AUD-1`'s plan §4.4:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "No audio data captured" $F'
```

**The established baseline is 0** — four consecutive days at zero before 2026-09-06. Anything else
means the tap is dead and §5 steps 4–5 are uninterpretable. The warning is
`SoundFlowAudioTap.cs:234`. ⚠ `AUD-18`'s own dossier says *"Not `AUD-12`"* — the two are different
defects — **but that is a statement about causation, not about UAT interference.** It interferes.

---

**`C-183` — ⚠ NEW. `AUD-1` contaminates this row's HEADLINE pass criterion. Album art may stay on the
placeholder even after this row is correctly fixed.**

`appsettings.Production.json:51` sets `UseShazamForAllSources: true` (the repo default at
`appsettings.json:93` is `false`), so SongRec runs on Bluetooth and **replaces** the phone's own AVRCP
metadata. Observed live 2026-09-06: *"Shazam metadata replaced AVRCP for BT: 'Spirit In The Sky'"*
while the phone was playing Green Day — a misidentification off residual radio audio seconds after a
BT reconnect — after which the next AVRCP update restored the right title **with no art**, leaving the
placeholder. That is `AUD-1`, and it is unfixed.

⇒ ⛔ **Do not treat `albumArtUrl` still showing `/images/default-album-art.png` as a failure of this
row without first confirming an identification actually ran.** The unambiguous criteria are §5 step
3's `isPlaying: true` and the presence of a `SongRec recognized` line — *that fingerprinting resumed*
is what this row promises. **Whether the art then resolves is `AUD-1`'s business.** ⛔ **And do not
"fix" it by setting `UseShazamForAllSources: false`** — `AUD-1`'s row records that this kills BT album
art entirely.

---

**`C-184` — ⛔ NEW. `ADR-030` now governs this file. Do not add a test seam, and do not re-widen the
one `TEST-2` just retired.**

`design/TESTING.md` § *Test Seams* shipped with `TEST-2` and is enforced by
`TestSeamLabelLintTests` (`tests/Radio.Core.Tests/`). Two of its rules bind this row directly:

- **Rule 1 — "Prefer no seam. Check whether the real path is reachable before assuming it is not."**
  Its worked examples are *this class*: `Mock<T>` subclasses `SoundComponent` and
  `AudioCaptureDevice` with a null engine, and a `private` method reached by an interface event is
  drivable by raising that event. **That is why Task 4 is deleted** (§0.0) and why every test in §4
  reaches its state through `IBluetoothService`.
- **Rule 6 — "A labelled seam is a standing invitation to delete it."** `ApplyDeferredCaptureState`'s
  own doc (`:448-454`) now says *"do not re-widen this to reach it."*

⛔ **`ApplyDeferredCaptureState` stays `private`.** ⛔ **Neither `TryPromoteToPlayingFromLastAvrcpStatus`
nor `HasCapturePath` may be made `internal`** — §4 reaches both through the production path. If a
Builder believes a seam is unavoidable, that is a design conversation and needs an `ADR-030`
kind classification with all three label clauses, **not** a quiet visibility widening.

### 0.8 ⛔ Not auto-mergeable, and why

The global auto-merge policy needs four things; this row cannot supply the second. **UAT is blocked**
— the owner's phone is unavailable, and no A2DP source means the pause/stop/resume sequence cannot be
driven at all. The change is **user-facing audio behaviour** on the live path (it makes a source claim
`Playing`, which starts fingerprinting, play-history writes and sleep's auto-pause), so the test suite
does not stand in for a running-app check the way it does for a library change. **Merge on the owner's
say-so after §5 runs**, or on an explicit owner decision to merge ahead of UAT with §5 filed as a
follow-up.

### 0.9 Things Builder must NOT do

- ⛔ **Do not re-run the investigation.** §0.2's git facts were re-verified at `066a0d5c`. Re-reading
  #469's diff is re-doing settled work.
- ⛔ **Do not read `MetadataInternal["PlaybackStatus"]` in the new code path.** `C-169`. Do **not**
  stop writing it either — it is an API observable pinned by an existing test and by §5's UAT.
- ⛔ **Do not add `Stopped` to `:1136` unguarded.** `C-170`.
- ⛔ **Do not add `Created` to `:1136`'s accept set.** A source at `Created` has no capture, is not in
  the mixer, and has not been initialized; `InitializeAsync`'s catch-up promotes it at the right
  moment. Widening the accept set to `Created` would claim `Playing` for a source that cannot play.
- ⛔ **Do not invert `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` or
  `DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring`.** `C-177`. *(These replace
  `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`, which `TEST-2` deleted.)*
- ⛔ **Do not re-widen `ApplyDeferredCaptureState` from `private` to `internal`, and do not add any
  other test seam.** `C-184`, `design/TESTING.md` § *Test Seams* rules 1 and 6, `ADR-030`. The method's
  own doc comment says so at `:454`.
- ⛔ **Do not edit `MockBluetoothService.cs`.** This row no longer touches it — Task 4 is deleted
  (§0.0). Its `IsAudioManagedByPlatform` stays the hard-coded `=> false` at `:31`.
- ⛔ **Do not touch `LinuxBluetoothService.cs`.** `C-174`, §7.1 — and it now has its own row, `AUD-14`.
- ⛔ **Do not fix `AudioController.cs:215`'s "Paused playback" log.** `C-172`, §7.2.
- ⛔ **Do not make `_metadata` concurrent.** `C-175`, §7.3.
- ⛔ **Do not use a bare `Task.Delay` in a new test** — synchronize with `TEST-2`'s `WaitForAsync`
  (`BluetoothAudioSourceTests.cs:1258`), and read its remarks first: it is a deadline, not a
  rendezvous. `C-176`, `CLAUDE.md` § *Test Timing*.
- ⛔ **Do not press the transport button before §5 step 3 is recorded** — it clears the stall.
  `C-181`.
- ⛔ **Do not touch the box.** No SSH, no `curl` against `radio`, until §5 is authorized.

---

## 1. Decision — one durable fact, one shared helper, one guarded predicate

### 1.1 The shape, stated first

**The phone's last reported AVRCP transport status becomes a first-class field on the source.** Two
places consult it: the recovery path that lands in `Ready`, and the AVRCP handler that must stop
treating `Stopped` as absorbing.

| Half | Where | What changes |
|---|---|---|
| **The backstop** — makes `Ready` non-terminal | `ApplyDeferredCaptureState:457-463` + `InitializeAsync:184-194` | Both call one helper that promotes `Ready → Playing` when the phone's last report was `Playing`. Today only `InitializeAsync` has this logic, inline. |
| **The fast path** — makes `Stopped` non-absorbing | `OnPlaybackStatusChanged:1135-1139` | `Stopped` joins the accept set, **guarded** on the source still holding a capture path. |

### 1.2 Why this shape and not the three alternatives

| Option | Verdict |
|---|---|
| **Widen `:1136` only** (add `Stopped`) | **Rejected.** It does not fix the measured sequence — §0.5 shows the swallowed edge arrives before a capture path exists — and it leaves `Ready` terminal for every other route into it. |
| **Add the catch-up to `ApplyDeferredCaptureState` only** | **Rejected.** It closes the observed stall and leaves `Stopped` absorbing, so the next variant is filed as `AUD-14`. The two predicates are one defect wearing two faces. |
| **Both, reading `MetadataInternal["PlaybackStatus"]`** — the brief's suggested shape | **Rejected**, and this is the only real decision in the row. It works. It also makes an unsynchronized display dictionary (`C-175`) and an unguarded `(string)` cast (`:190`) load-bearing for audio-path recovery, to save one field. §0.3(c). |
| **Both, with a dedicated last-known-status field and a capture-path guard** ✅ | **Taken.** |

### 1.3 Why a `volatile bool` and not a `BluetoothPlaybackStatus?`

The only question anything asks of this fact is *"does the phone say it is playing?"* — a single bit.
A `bool` is the exact shape, and `volatile` is legal on it, which a nullable enum is not: `T?` is two
fields (`hasValue` + `value`) and can tear across the D-Bus-callback / caller-thread boundary. The
`false` default also **is** the correct answer for "no AVRCP status has ever been seen", which is what
keeps `C-177`'s test green without a special case.

⚠ **The seek states deliberately do not touch it.** `ForwardSeek` / `ReverseSeek` are transient;
BlueZ returns to `playing` or `paused` when the seek ends. Letting a seek clear a known-`Playing`
status would open a window in which the catch-up refuses a promotion it should make.

---

## 2. Tasks

### Task 1 — record the phone's last reported transport status

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

**1a.** Add the field beside `_hasMediaPlayer` (`:41`):

```csharp
  private bool _hasMediaPlayer;

  // The phone's last reported AVRCP transport status, reduced to the one bit
  // anything asks of it.
  //
  // ⚠ This exists instead of reading MetadataInternal["PlaybackStatus"] back out,
  // even though the two are written on the same event. That dictionary is a
  // DISPLAY projection — a plain Dictionary shared with the public Metadata
  // property, written from D-Bus callback threads and enumerated on ASP.NET
  // request threads by AudioDtoMapper — and reading a state-machine input out of
  // it also requires an unguarded (string) cast. See plan AUD-12 C-169 / C-175.
  //
  // ⚠ volatile, and a bool rather than a BluetoothPlaybackStatus?, because a
  // nullable enum is two fields and can tear across the callback boundary. The
  // false default is also the right answer for "no AVRCP status seen yet", which
  // is what keeps BluetoothAudioSourceTests.DeviceConnectedEvent_TakesTheMatching
  // Branch_AndLandsReady and _WhenPlatformManaged_LandsReadyWithoutAcquiring green.
  private volatile bool _avrcpReportsPlaying;
```

**1b.** Set it at the top of `OnPlaybackStatusChanged` (`:1126-1129`), leaving the metadata write and
the debug log exactly as they are:

```csharp
  private void OnPlaybackStatusChanged(object? sender, BluetoothPlaybackStatus e)
  {
    // ⚠ Recorded BEFORE the switch below, because the switch DISCARDS transitions
    // it does not accept and the fact that the phone reported Playing must survive
    // that. That discard is exactly what AUD-12 was.
    //
    // ⚠ ForwardSeek / ReverseSeek are excluded on purpose: they are transient and
    // BlueZ returns to "playing" or "paused" when the seek ends, so letting one
    // clear a known-Playing status would open a window in which the catch-up in
    // TryPromoteToPlayingFromLastAvrcpStatus refuses a promotion it should make.
    if (e is BluetoothPlaybackStatus.Playing or BluetoothPlaybackStatus.Paused
        or BluetoothPlaybackStatus.Stopped or BluetoothPlaybackStatus.Error)
    {
      _avrcpReportsPlaying = e == BluetoothPlaybackStatus.Playing;
    }

    MetadataInternal["PlaybackStatus"] = e.ToString();
    Logger.LogDebug("Bluetooth playback status: {Status}", e);
```

⛔ **`MetadataInternal["PlaybackStatus"]` is unchanged and stays unchanged.** It is pinned by
`PlaybackStatusChanged_UpdatesMetadata` (`BluetoothAudioSourceTests.cs:191-197`), it is projected to
`/api/audio/nowplaying` through `AudioDtoMapper.cs:148-155`, and §5's UAT confirmation reads it.

---

### Task 2 — one shared catch-up, called from both places that land in `Ready`

**File:** same.

**2a.** Add the helper next to `ApplyDeferredCaptureState`:

```csharp
  /// <summary>
  /// Promotes a source sitting in <see cref="AudioSourceState.Ready"/> to
  /// <see cref="AudioSourceState.Playing"/> when the phone's last reported AVRCP
  /// transport status was <c>Playing</c>. Returns true if it promoted.
  /// </summary>
  /// <remarks>
  /// ⚠ WITHOUT THIS, Ready IS TERMINAL, and that is AUD-12. The only other route
  /// out of Ready is an AVRCP edge in OnPlaybackStatusChanged, and BlueZ raises
  /// PropertiesChanged only when a value CHANGES
  /// (LinuxBluetoothService.OnPlayerPropertiesChanged) — so a phone that is
  /// already playing sends nothing further, and the source sits in Ready
  /// indefinitely while audio flows through the mixer. Measured on the box
  /// 2026-09-06: the source reached Ready at 10:18:19 and never left it.
  ///
  /// ⚠ It reads _avrcpReportsPlaying and NOT MetadataInternal["PlaybackStatus"],
  /// even though both are written by the same handler. See that field's remarks
  /// and plan AUD-12 C-169 / C-175.
  ///
  /// ⚠ A source that has never seen an AVRCP Playing — including a freshly
  /// constructed one — is NOT promoted. That is deliberate and is pinned by
  /// BluetoothAudioSourceTests.DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady
  /// and _WhenPlatformManaged_LandsReadyWithoutAcquiring, both of which drive a
  /// Created source to Ready through the real DeviceConnected dispatch (AUD-12 C-177).
  /// </remarks>
  private bool TryPromoteToPlayingFromLastAvrcpStatus()
  {
    if (State != AudioSourceState.Ready || !_avrcpReportsPlaying)
    {
      return false;
    }

    Logger.LogInformation(
      "BluetoothAudioSource: phone reports Playing but the source is Ready — promoting to Playing");
    State = AudioSourceState.Playing;
    return true;
  }
```

⚠ **The log line is `Information`, and it is in `Radio.API`, so it goes to the file sink, not
journald** (`CLAUDE.md` § *Deployment*, and `PHN-5` `C-93` for why that asymmetry matters). §5 reads
it from `/opt/radio-console/logs/radio-*.txt`.

**2b.** Replace the inline catch-up in `InitializeAsync` (`:184-194`) with a call to it:

```csharp
    // Fix race: PlaybackStatusChanged may fire during StartAsync() (D-Bus sends the
    // current status immediately) before State is set to Ready. The handler records
    // the status but skips its own transition because State wasn't Ready yet.
    //
    // ⚠ AUD-12: this catch-up was correct and was the ONLY one. The deferred-capture
    // path lands in the identical Ready state by a different route and had no way
    // back out, so both now call the one helper. Do not re-inline this.
    TryPromoteToPlayingFromLastAvrcpStatus();
```

**2c.** Rewrite `ApplyDeferredCaptureState` (`:457-463`). Its existing `<summary>` and **both** `<para>`
blocks (`:435-456`) stay **verbatim** — ⚠ **including `TEST-2`'s second `<para>` at `:447-455`, the one
recording the `internal` → `private` narrowing and saying "do not re-widen this to reach it."** Append
one `<para>` **after** it and replace the body:

```csharp
  /// <para>
  /// ⚠ AUD-12: preserving <c>Playing</c> is only half the invariant this method's
  /// first paragraph states. A source that arrives here in any other state lands in
  /// <c>Ready</c>, which is terminal — see
  /// <see cref="TryPromoteToPlayingFromLastAvrcpStatus"/>. So it also RE-DERIVES
  /// <c>Playing</c> when the phone's last AVRCP report says the phone is playing.
  /// Measured stall: <c>Stopped -&gt; Ready</c> at 10:18:19 on 2026-09-06, then
  /// nothing, for ten-plus minutes, while the graph was audible and fully connected.
  /// </para>
  private void ApplyDeferredCaptureState()
  {
    if (State == AudioSourceState.Playing)
    {
      return;
    }

    State = AudioSourceState.Ready;
    TryPromoteToPlayingFromLastAvrcpStatus();
  }
```

⛔ **`private`, NOT `internal`.** The pre-repair revision of this plan prescribed
`internal void ApplyDeferredCaptureState()`, which was correct at `066a0d5c` and is wrong now:
`TEST-2` narrowed it, `design/TESTING.md` § *Test Seams* rule 6 is why, and `C-184` forbids
re-widening it. **This is the single line that made the plan "no longer compile as written."**

⚠ **The early return is behaviour-identical to the old `if (State != Playing) { … }`** — it is written
this way only so the two statements after it read as one sequence. All three call sites (`:472`,
`:489`, `:500`) are unchanged.

---

### Task 3 — stop treating `Stopped` as absorbing, with the guard `C-170` requires

**File:** same.

**3a.** Add the guard predicate, next to `NeedsFingerprintingLookup` (`:71`) or immediately above
`OnPlaybackStatusChanged`:

```csharp
  /// <summary>
  /// True while this source still holds a route from the phone's A2DP stream to the
  /// mixer — a routed playback id, a capture device, a capture generator, or a
  /// platform that owns the routing itself.
  /// </summary>
  /// <remarks>
  /// ⚠ This exists for exactly one caller: the Stopped arm of
  /// OnPlaybackStatusChanged. Stopped has TWO provenances and only one of them is
  /// safe to promote out of — see the comment there and plan AUD-12 C-170.
  ///
  /// ⚠ private, and it stays private. Three of its four arms are reachable from a
  /// unit test through IBluetoothService alone — see design/TESTING.md § Test Seams
  /// rule 1 and plan AUD-12 C-184. The fourth (_playbackId) is not, and that gap is
  /// recorded in the plan's §4.8 rather than closed with a seam.
  /// </remarks>
  private bool HasCapturePath =>
    _bluetoothService.IsAudioManagedByPlatform
    || _playbackId != null
    || _captureDevice != null
    || SoundComponent != null;
```

**3b.** Widen the `Playing` arm (`:1135-1139`):

```csharp
      case BluetoothPlaybackStatus.Playing:
        // ⚠ Stopped joined this accept set in AUD-12, and the guard on it is not
        // decoration. AUD-10 (the A2DP transport dies on pause) drives this source
        // to Stopped routinely, and before AUD-12 an AVRCP Playing arriving in that
        // state was written to metadata four lines up and then silently dropped
        // here — Stopped was absorbing.
        //
        // ⛔ Stopped has TWO provenances. The :1150 case below is the phone's
        // transport stopping while our pipeline stays intact — promoting back is
        // correct. OnDeviceDisconnected (:742) is the other, and by the time it
        // assigns Stopped it has already pulled the generator out of the mixer and
        // nulled _captureDevice / SoundComponent. Promoting THAT to Playing would
        // assert audio is flowing from a phone that is not connected, and would then
        // fire TryReacquireCaptureAsync below at a device that is gone. HasCapturePath
        // is false in exactly that case. Plan AUD-12 C-170; C-174 records the BlueZ
        // watcher leak that makes it reachable rather than theoretical.
        if (State == AudioSourceState.Ready
            || State == AudioSourceState.Paused
            || (State == AudioSourceState.Stopped && HasCapturePath))
        {
          State = AudioSourceState.Playing;
        }
        // Phone started streaming — if source is active but has no capture, try to acquire.
        // This handles the case where the phone was paused when the source was activated.
        if (State == AudioSourceState.Playing && _playbackId == null && !_bluetoothService.IsAudioManagedByPlatform)
        {
          _ = TryReacquireCaptureAsync();
        }
        break;
```

⛔ **The `Paused` (`:1147`) and `Stopped` (`:1150`) arms are untouched.** They mirror the phone
downward and are correct; widening them is a different change with a different argument.

---

### ~~Task 4 — make the platform-managed flag settable on the test double~~ ⛔ DELETED 2026-09-08

**This task no longer exists. Do not do it.** It proposed changing
`src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs:31` from
`public bool IsAudioManagedByPlatform => false;` to a settable auto-property, on the stated grounds
that *"`HasCapturePath` cannot be made true from a unit test today … none is reachable without a
native SoundFlow engine."*

⭐ **That premise was false, and `TEST-2` is the row that proved it false.** It is the same
native-engine claim `TEST-2` was filed on, chased for four weeks, and then refuted — now
`design/TESTING.md` § *Test Seams* rule 1's leading worked example. Three of `HasCapturePath`'s four
arms are reachable with no production-tree change at all:

| Arm | How a test reaches it | §4 test |
|---|---|---|
| `_captureDevice != null` | `Mock<AudioCaptureDevice>(Loose, null!, default(AudioFormat), null!)` handed back by `GetAudioCaptureDeviceAsync`; `InitializeAsync:159-165` assigns it | `T2` (`AudioCaptureDevice` case) |
| `SoundComponent != null` | the same with `Mock<SoundComponent>`; `InitializeAsync:166-174` assigns it | `T2` (`SoundComponent` case) |
| `IsAudioManagedByPlatform` | `Mock<IBluetoothService>` + `.Setup(b => b.IsAudioManagedByPlatform).Returns(true)` — which `BuildBtMock(platformManaged: true)` already does | `T2b` |
| `_playbackId != null` | ⚠ **not reachable** — it is only set inside `RouteCaptureThroughMixerAsync`, which needs a `SoundFlowPlaybackService`. §4.8 records the gap rather than closing it with a seam. | — |

⇒ **This row no longer touches `MockBluetoothService.cs` or `WasapiLoopbackTests.cs`**, and §4.7 drops
their two `MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse` rows: nothing puts them at risk
any more. `C-184`.

---

## 3. Ordering

Task 1 first — Tasks 2 and 3 both read `_avrcpReportsPlaying`. Task 2 before Task 3, so the backstop
(the half that actually fixes the measured stall, §0.5) is in place before the fast path. **There is no
Task 4.**

**One PR.** The deliverable is a property — *the source's state reflects whether audio is flowing* —
and §0.5 shows neither half establishes it alone. Splitting would ship a PR whose test suite asserts
half an invariant, which is how a partial fix gets recorded as a complete one.

---

## 4. Test plan

**File:** `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` (extend).

> ⭐⭐ **REWRITTEN 2026-09-08. Every test below was re-derived after `TEST-2`; none of them calls
> `ApplyDeferredCaptureState` and none of them uses the fixture's `_source` for the deferred path.**
> The pre-repair §4 reached the `Ready` stall by calling the `internal` seam directly. That seam is
> gone, and per `design/TESTING.md` § *Test Seams* it was never real coverage anyway: it executed the
> state decision **without** executing the dispatch that selects it.

**How the `Ready` stall is reached now, concretely.** Through the production path, exactly as
`TEST-2`'s own tests do — **raise `IBluetoothService.DeviceConnected` on a `Mock<IBluetoothService>`
whose `GetAudioCaptureDeviceAsync` hands back a mocked capture object.** That runs
`OnDeviceConnected` (`:361`) → `TryAcquireAudioCaptureAsync` (`:370`) → the real
`capture is AudioCaptureDevice` / `capture is SoundComponent` arms at `:486`/`:497` → the real
`ApplyDeferredCaptureState()` calls at `:489`/`:500`. ✅ **The state is genuinely reachable; no new
seam is needed and none may be added** (`C-184`).

**Reuse `TEST-2`'s harness verbatim — do not build a second one** (`design/TESTING.md` rule 5):

| Helper | Line | Use |
|---|---|---|
| `BuildBtMock(capture, probe, nullAcquisitions, platformManaged)` | `:990-1014` | the `Mock<IBluetoothService>`. `nullAcquisitions: 2` is what leaves a source `Playing` **with no capture** — the state the deferred arm exists for |
| `BuildSource(btMock)` | `:1016-1022` | a real `BluetoothAudioSource` over it |
| `RaiseDeviceConnected(btMock)` | `:1024-1027` | the event that drives the deferred acquisition |
| `NewCaptureMock(kind)` / `CaptureKinds` | `:1037-1040` | `Mock<AudioCaptureDevice>` or `Mock<SoundComponent>`, both with a `null!` engine |
| `CaptureAcquisitionProbe` | `:960-966` | counts acquisitions, so a test synchronizes on the **observation** |
| `WaitForAsync(condition)` | `:1258-1267` | the only permitted wait |

⚠ **AVRCP edges reach these sources through the mock, not through `SimulatePlaybackStatusChange`.**
`PlaybackStatusChanged` is `EventHandler<BluetoothPlaybackStatus>` (`IBluetoothService.cs:142`), so:

```csharp
  btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing);
```

⚠ **Why not the fixture's `_source`:** `MockBluetoothService.GetAudioCaptureDeviceAsync` (`:126-129`)
returns `Task.FromResult<object?>("mock-capture-endpoint")` — a **boxed string** — so it can only ever
reach the `else` at `BluetoothAudioSource.cs:508`, which never calls `ApplyDeferredCaptureState`. A
deferred-path test written against the fixture would be vacuous. (`T5` is the one exception below, and
it does not use the deferred path at all.)

> ⚠ **`CLAUDE.md` § *Test Timing*, and the honest version of it.** The determinism here is real but it
> is **not** "no clock": `WaitForAsync` is a 5 s deadline. What makes it safe is upstream — the whole
> handler runs to completion **synchronously inside `btMock.Raise`**, because `OnDeviceConnected`
> invokes `TryAcquireAudioCaptureAsync` directly (`:370`, not `Task.Run`), `BuildBtMock` returns
> `Task.FromResult`, and `_routeLock` is uncontended. The condition is therefore already true on its
> first evaluation and the helper never actually awaits. ⛔ **Add a real await point to that path and
> these become timing races.** `C-176`; read `WaitForAsync`'s own remarks (`:1230-1257`) before
> touching any of this.

> ⚠ **Two background tasks exist and neither can affect an assertion — say so in the test file so the
> next reader does not add a wait "to be safe".** (a) The `Playing` arm fires
> `_ = TryReacquireCaptureAsync()` (`:1144`) when the source is `Playing` with no `_playbackId`; that
> method returns at its `:681` guard once a capture is held, and its own summary (`:668-672`) says it
> "does not alter the source state". (b) `PlayCoreAsync` starts `RetryCaptureInBackgroundAsync`, whose
> first act is a 10 s `Task.Delay` (`:641`); `DisposeAsyncCore` cancels it via `StopCaptureRetryLoop()`
> (`:289` → `:627`) long before it ticks.

### 4.1 `T1` — the measured stall, reproduced end to end ⭐

**This is the regression pin.** It replays §0.5's table without a clock and exercises both halves.

```csharp
  [Theory]
  [MemberData(nameof(CaptureKinds))]
  public async Task StalledAtReady_WhenPhoneResumed_IsPromotedWhenDeferredCaptureLands(string kind)
  {
    // AUD-12's measured sequence: Playing -> Paused -> Stopped -> Ready, then a
    // ten-minute silence while the mixer stayed audible.
    //
    // nullAcquisitions: 2 because PlayAsync acquires TWICE on a Created source
    // (AudioSourceBase.cs:84-87 via InitializeAsync, then BluetoothAudioSource.cs:206-209
    // from PlayCoreAsync). Both must answer null, or the capture is consumed by the
    // OTHER dispatch at :159/:166 and DeviceConnected returns at the "already
    // acquired" guard (:479) without ever reaching :486/:497. TEST-2 measured this.
    var capture = NewCaptureMock(kind);
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe, nullAcquisitions: 2);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);
    Assert.Equal(2, probe.Calls);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Paused);
    Assert.Equal(AudioSourceState.Paused, source.State);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Stopped);
    Assert.Equal(AudioSourceState.Stopped, source.State);

    // The phone resumes. This edge is still refused — the source holds no capture
    // path at this point, which is what HasCapturePath guards (AUD-12 C-170) — but
    // the fact that the phone reports Playing is now recorded.
    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing);
    Assert.Equal(AudioSourceState.Stopped, source.State);

    // The deferred-capture retry lands — through the REAL path, not through a seam.
    // Before AUD-12 this parked the source in Ready permanently: BlueZ sends no
    // further edge once the phone is already playing.
    RaiseDeviceConnected(btMock);
    await WaitForAsync(() => source.State == AudioSourceState.Playing);

    Assert.Equal(AudioSourceState.Playing, source.State);
    Assert.Equal(3, probe.Calls);
    AssertArmTaken(kind, source, capture);
  }
```

⭐ **This is strictly more faithful to the box than the pre-repair version**, which called
`_source.ApplyDeferredCaptureState()` directly. On `radio` the 10:18:19 `Stopped -> Ready` transition
happened *because* `TryAcquireAudioCaptureAsync` acquired a capture; the test now replays that cause,
not just its effect. `AssertArmTaken` (`:1055-1066`) additionally pins **which** arm ran.

> **Falsifying mutations, all three to be run:** revert Task 2c's
> `TryPromoteToPlayingFromLastAvrcpStatus()` call → the final assertion fails at `Ready` (⚠ it will
> fail *inside* `WaitForAsync` after the full 5 s, naming the timeout — that is the expected shape,
> not a flake). Revert Task 1b's field write → same. ⭐ **Reverting Task 3b alone must NOT make this
> test fail** — that is the point of §0.5, and if it does, the test is asserting the wrong half.

### 4.2 `T2` — the fast path, when the capture path is intact

⭐ **Rewritten to use a real capture object rather than the platform-managed stand-in.** The
pre-repair version reached for `IsAudioManagedByPlatform` because it believed the `_captureDevice` and
`SoundComponent` arms were unreachable in a unit test. They are not (deleted Task 4), and these are
the arms that matter on the box — `radio` is Linux and `IsAudioManagedByPlatform` is false there.

```csharp
  [Theory]
  [MemberData(nameof(CaptureKinds))]
  public async Task PlaybackStatusPlaying_WhileStopped_PromotesWhenCapturePathIsIntact(string kind)
  {
    // nullAcquisitions defaults to 0, so InitializeAsync's OWN dispatch (:159/:166)
    // consumes the capture and assigns _captureDevice or SoundComponent. That is
    // exactly the HasCapturePath arm under test, and it is the arm that is live on
    // the box — radio is Linux, where IsAudioManagedByPlatform is false.
    var capture = NewCaptureMock(kind);
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);
    AssertArmTaken(kind, source, capture);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Stopped);
    Assert.Equal(AudioSourceState.Stopped, source.State);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing);

    Assert.Equal(AudioSourceState.Playing, source.State);
  }
```

> **Falsifying mutation:** restore `:1136`'s original two-clause accept set → fails at `Stopped`.

### 4.2b `T2b` — the third `HasCapturePath` arm, platform-managed routing

```csharp
  [Fact]
  public async Task PlaybackStatusPlaying_WhileStopped_PromotesWhenPlatformOwnsRouting()
  {
    // The Windows arm. InitializeAsync short-circuits at :141-155 and PlayCoreAsync
    // at :200-204, so the source reaches Playing holding no capture object at all —
    // and HasCapturePath is still true, which is the point.
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture: null, probe, platformManaged: true);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);
    Assert.Equal(0, probe.Calls);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Stopped);
    Assert.Equal(AudioSourceState.Stopped, source.State);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing);

    Assert.Equal(AudioSourceState.Playing, source.State);
  }
```

> **Falsifying mutation:** drop the `_bluetoothService.IsAudioManagedByPlatform` clause from
> `HasCapturePath` → this fails while `T2` still passes.

### 4.3 `T3` — the guard actually guards ⛔

```csharp
  [Fact]
  public async Task PlaybackStatusPlaying_WhileStopped_DoesNotPromoteWithNoCapturePath()
  {
    // capture: null, so NOTHING can ever be acquired and every HasCapturePath arm
    // is false for the whole test. (The pre-repair version used the fixture's
    // _source, whose mock hands back a boxed string that lands in the `else` at
    // :508 — same outcome, but by accident rather than by construction.)
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture: null, probe);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, source.State);
    Assert.Throws<InvalidOperationException>(() => source.GetSoundComponent());

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Stopped);

    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing);

    // No playback id, no capture device, no generator, no platform routing.
    // Claiming Playing here would assert audio is flowing from a phone that may
    // have disconnected — OnDeviceDisconnected (:742) assigns Stopped only after
    // tearing the capture out of the mixer (:717-738). Plan AUD-12 C-170.
    Assert.Equal(AudioSourceState.Stopped, source.State);
  }
```

> **Falsifying mutation:** drop `&& HasCapturePath` from `:1136` → this test fails while `T1`, `T2`
> and `T2b` still pass. ⭐ **That asymmetry is the whole value of `T3`** — without it the guard could be
> deleted by a future simplification and every other test would stay green.

### 4.4 `T4` — the near-miss that must not promote

```csharp
  [Fact]
  public async Task DeferredCapture_WhenPhoneReportsPaused_StaysReady()
  {
    // Same shape as T1's promotion, one status different. Without this, a mutation
    // that promotes on ANY seen status passes T1.
    //
    // ⚠ Renamed from ApplyDeferredCaptureState_WhenPhoneReportsPaused_StaysReady:
    // the method is private since TEST-2 and the test no longer names it, because
    // it no longer calls it. It enters through DeviceConnected like everything else.
    var capture = NewCaptureDeviceMock();
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe);
    await using var source = BuildSource(btMock);

    // Created, so the Paused arm's `when State == Playing` guard refuses the
    // transition and the source stays Created — but _avrcpReportsPlaying is
    // written to false regardless, which is what this test is about.
    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Paused);
    Assert.Equal(AudioSourceState.Created, source.State);

    RaiseDeviceConnected(btMock);
    await WaitForAsync(() => source.State == AudioSourceState.Ready);

    Assert.Equal(AudioSourceState.Ready, source.State);
  }
```

> **Falsifying mutation:** change Task 1b's write to `_avrcpReportsPlaying = true;` unconditionally →
> fails. ⭐ **And note what does NOT catch that mutation:**
> `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` stays green, because it raises no AVRCP
> event at all and the field is `false` either way. `T4` is the only test that closes it.

### 4.5 `T5` — `InitializeAsync`'s catch-up still works through the shared helper

✅ **This is the one test `TEST-2` did not disturb, and it survives exactly as written.** It uses the
fixture's `_source` and `MockBluetoothService`, which is correct here: it exercises
`InitializeAsync`'s own catch-up (`:184-194`), not the deferred-capture path, so the mock's boxed
string landing in the `else` at `:508` is the *intended* shape — the source reaches `Ready` with no
capture and the catch-up promotes it.

```csharp
  [Fact]
  public async Task InitializeAsync_WhenPhoneAlreadyPlaying_TransitionsToPlaying()
  {
    // The race the :184 comment describes: the AVRCP edge lands before the source
    // is Ready, so the switch discards it (Created is not an accept state) and the
    // catch-up recovers it. This behaviour predates AUD-12; the test pins that
    // moving it into a shared helper did not change it.
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Playing);
    Assert.Equal(AudioSourceState.Created, _source.State);

    await _source.InitializeAsync(CancellationToken.None);

    Assert.Equal(AudioSourceState.Playing, _source.State);
  }
```

⚠ **Check first whether an equivalent already exists** — `:160-169`
(`InitializeAsync_WhenNoCaptureDevice_SetsReadyState`) covers the no-status case and must stay green
unchanged; this plan found no test covering the already-playing case. If one exists, extend it rather
than adding a second.

### 4.6 `T6` — the downstream invariant, reached **by promotion**

⚠ **`TEST-2` already ships the tap-active assertion**, as
`DeferredCaptureAcquisition_ThroughDispatch_KeepsAudioTapActive` (`:1195-1222`). ⛔ **Do not modify it
and do not duplicate it.** It reaches `Playing` by *never leaving it*; `T6` reaches `Playing` by
*promotion out of `Ready`*, which is the state transition this row creates and which nothing else
covers. Reuse its `SoundFlowAudioTap` construction (`:1210-1219`) verbatim.

```csharp
  [Fact]
  public async Task PromotedSource_KeepsAudioTapActive()
  {
    // Reaches Playing the AUD-12 way: the phone reported Playing while the source
    // could not act on it, and the deferred capture landing promoted it. Contrast
    // DeferredCaptureAcquisition_ThroughDispatch_KeepsAudioTapActive, which is
    // already Playing throughout and pins the #469 invariant instead.
    var capture = NewCaptureDeviceMock();
    var probe = new CaptureAcquisitionProbe();
    var btMock = BuildBtMock(capture, probe, nullAcquisitions: 2);
    await using var source = BuildSource(btMock);

    await source.PlayAsync(CancellationToken.None);
    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Stopped);
    btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing);
    Assert.Equal(AudioSourceState.Stopped, source.State);

    RaiseDeviceConnected(btMock);
    await WaitForAsync(() => source.State == AudioSourceState.Playing);

    var engineMock = new Mock<IAudioEngine>();
    engineMock.Setup(e => e.State).Returns(AudioEngineState.Running);
    var managerMock = new Mock<IAudioManager>();
    managerMock.Setup(m => m.ActiveSource).Returns(source);

    var tap = new SoundFlowAudioTap(
      new Mock<ILogger<SoundFlowAudioTap>>().Object,
      engineMock.Object,
      managerMock.Object);

    // SoundFlowAudioTap.cs:135 — the gate that made AUD-12 visible as missing
    // album art. Fingerprinting is the row's user-facing consequence.
    Assert.True(tap.IsActive, "Fingerprinting must be active once the source is promoted");
  }
```

### 4.7 Tests that must pass **unmodified**

⭐ **REBUILT 2026-09-08.** The pre-repair table named three tests `TEST-2` deleted and two that this
row no longer puts at risk. Name each survivor in the PR body with its result:

| Test | Line | Why it is at risk |
|---|---|---|
| `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` | `:1068-1091` | ⛔ `C-177`, **and it is a `[Theory]`, so two cases.** `Created` must still yield `Ready` through the real dispatch. **Inverting it is wrong.** |
| `DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring` | `:1147-1168` | ⛔ `C-177`, the third call site (`:472`). Same invariant, different route. |
| `DeviceConnectedEvent_WhilePlaying_TakesTheBranchAndStaysPlaying` | `:1093-1145` | Also a `[Theory]`. Task 2c rewrites the method it exercises; the `Playing` early return must stay behaviour-identical. This is the #469 invariant. |
| `DeferredCaptureAcquisition_ThroughDispatch_KeepsAudioTapActive` | `:1195-1222` | Same method; and `T6` must not be written by editing it. |
| `PlaybackStatusChanged_UpdatesMetadata` | `:191-197` | Task 1b edits that handler; the metadata key must be untouched. ✅ Unmoved by `TEST-2`. |
| `InitializeAsync_WhenNoCaptureDevice_SetsReadyState` | `:160-169` | Task 2b replaces the block right after the assignment it asserts. ✅ Unmoved. |

**Deleted from this table, and why:**

| Was | Now |
|---|---|
| `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady` (`:946-957`) | ⛔ **Deleted by `TEST-2`.** Replaced by rows 1–2 above. `C-177`. |
| `DeferredCaptureAcquisition_AfterPlay_LeavesSourcePlaying` (`:907-921`) | ⛔ **Deleted by `TEST-2`.** Superseded by `DeviceConnectedEvent_WhilePlaying_TakesTheBranchAndStaysPlaying`. |
| `DeferredCaptureAcquisition_AfterPlay_KeepsAudioTapActive` (`:923-944`) | ⛔ **Deleted by `TEST-2`.** Superseded by `DeferredCaptureAcquisition_ThroughDispatch_KeepsAudioTapActive`. |
| `MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse` (`:254-258`) | ✅ **Still exists and is no longer at risk** — Task 4 is deleted, so nothing in this row touches that member. |
| the same in `WasapiLoopbackTests` (`:174-178`) | ✅ Same. |

### 4.8 ⚠ One coverage gap this row records rather than closes

`HasCapturePath`'s **`_playbackId != null`** arm is not exercised by any test above. `_playbackId` is
assigned only inside `RouteCaptureThroughMixerAsync` (`:564`, `:583`), which requires a
`SoundFlowPlaybackService` — an optional constructor parameter the test fixture deliberately does not
pass, which is precisely why routing safely no-ops in every test here.

⛔ **Do not close this with a seam.** `design/TESTING.md` § *Test Seams* rule 5 forbids adding a
second seam to re-assert from another angle, and rule 1 asks whether the real path is reachable — it
is not, without standing up the playback service. **State it in the PR body**, in the shape `TEST-2`
used for `TryReacquireCaptureAsync`: *recorded as still uncovered, not claimed closed.* The practical
risk is low: the arm is a disjunct in an OR, so its absence can only make the guard *more* refusing,
and `T2`/`T2b` already pin the promoting direction through two other arms.

### 4.9 Gates

- `dotnet build --configuration Release` — ⚠ **the baseline is 47 warnings, 0 errors**, measured on
  `main` 2026-09-06 and re-confirmed by `TEST-2` on 2026-09-08. **The gate is equality with that
  baseline, not zero.** *(The pre-repair revision said "0 warnings", which would fail on unmodified
  `main`; `CLAUDE.md` also warns that earlier notes citing 53 are stale.)*
- `dotnet test --configuration Release` — full suite green.
  ⛔ **Never pipe it to `tail`** (`CLAUDE.md`): redirect, `echo "exit=$?"`, then grep the file. Read the
  **per-project** summary lines.
  Known-failing on Windows and not regressions: four `SrcVariableResamplerTests`
  (`libsamplerate.so.0`, `TEST-5`), `NwsObservationIntegrationTests.RealNwsCall_*` and
  `CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording` (all live
  network, `Category=Integration`, CI-excluded). ⚠ `TEST-2` additionally logged a **pre-existing
  load-sensitive flake in `GoogleCastOutputConcurrencyTests`** on 2026-09-08 — its 5 s `.WaitAsync`
  timeout is swallowed by a broad `catch (Exception)`. Unrelated to this row; do not chase it.
- ⚠ If run from a git worktree under a path containing a `worktrees` segment, `LogSafetyLintTests` is
  red for an unrelated reason (`PHN-5` `C-100`). Not a finding about this row.
- **Every mutation in §4.1–§4.4 run, with its result in the PR body.** A mutation that does not make
  its test fail is a finding, not a formality — this repository has repeatedly shipped tests that
  passed against a deliberately broken implementation. ⭐ **`TEST-2` is the fresh worked example: its
  own plan's §4.1 merge gate was *vacuous*** — it prescribed swapping two mutually exclusive `is`
  arms, which is a no-op — **and run literally it would have certified the tests without testing
  them.** Before running a mutation here, ask whether it can actually change behaviour.

---

## 5. UAT — ⛔ DEFERRED, and the exact steps for when the phone returns

⛔ **This cannot be run now.** The owner's phone is unavailable, and without an A2DP source there is no
AVRCP stream, no transport, and nothing to pause. ⛔ **And nothing in this section may be attempted
against the box unattended** — `CLAUDE.md` records that heavy log reads on `radio` correlate with
audible audio distortion, and the box is on WiFi with nobody physically present.

⚠⚠ **FOUR separate conditions can make this UAT silently vacuous, and only one of them is this row's
own bug. Read all four before starting; three are new since the plan was first written.**

| # | Condition | What it does | Guard |
|---|---|---|---|
| 1 | **`AUD-10`** | A reconnect between the pause and the check re-runs `InitializeAsync`, which has had a catch-up since long before this row — **a BROKEN build passes** | `C-178`; step 3's ⛔ |
| 2 | ⭐ **`AUD-18`** | The fingerprint tap returned zero bytes for 11½ h on 09-07/08 with **zero `TrackMetadata` rows**. If it is dead, steps 4–5's fingerprinting checks fail on a **CORRECT** build | `C-182`; **new step 0b** |
| 3 | ⭐ **`AUD-1`** | SongRec replaces correct AVRCP metadata and can leave `albumArtUrl` on the placeholder even after this row is fixed — so the row's *headline symptom* is not a clean criterion | `C-183`; step 4's ⚠ |
| 4 | ⛔⛔ **This plan's own step 4** | Pressing the transport button calls `PlayAsync`, which writes `State = Playing` at `AudioSourceBase.cs:96` — **it clears the stall before it is measured** | `C-181`; step 4 is re-ordered |

**Run every step in order — 0, the new 0b, then 1 through 6.** *(The pre-repair text said "all six
steps" and then listed seven; it was already miscounted.)* **Steps 3 and 5 are the ones that fail on a
broken build; step 4a is the fastest non-destructive tell.**

**0. Before the fix — capture the confirmation, if the stall is still reproducible.** This is the
single measurement that proves the mechanism rather than the symptom, and it can only be taken while
the bug is live:

```bash
curl -s http://radio:5000/api/audio/nowplaying
```

⭐ **`"isPlaying": false` alongside `extendedMetadata.PlaybackStatus == "Playing"` confirms the
mechanism exactly**: the AVRCP `Playing` report reached the source and was recorded, and the state
machine did not act on it. Paste the raw JSON into the PR body. If the stall cannot be reproduced
before deploying, say so — do not synthesize this evidence.

**0b. ⭐ NEW — confirm the fingerprint tap is alive, BEFORE trusting anything in steps 4 and 5.**
`C-182`. `AUD-18` records the tap returning zero bytes for **11 h 23 m** on 2026-09-07/08 while audio
played normally, producing **zero `TrackMetadata` rows** for the whole window. Every fingerprinting
observation this UAT makes is downstream of that tap, so if it is dead they fail on a build that is
perfectly correct:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "No audio data captured" $F'
```

**The established baseline is 0** — four consecutive days at zero before 2026-09-06. ⛔ **Anything
other than 0 means steps 4 and 5 are uninterpretable: stop, note the count in the PR body, and get the
tap healthy first.** `AUD-18` records that a *source-stream* restart was sufficient — `radio-api` need
not be bounced. The warning comes from `SoundFlowAudioTap.cs:234`. ⚠ **The uptime matters:** `AUD-18`
needed ~37 h to appear, so a box that has just been deployed to is unlikely to be in this state —
check anyway, because the check is one command and the alternative is a wrong verdict.

**1. Deploy.** `./deploy/Deploy-ToLinux.ps1` (defaults are `-TargetHost radio -Runtime linux-x64`
since `OPS-1`). Confirm both SHAs:

```bash
curl -s http://radio:5000/api/health/version
curl -s http://radio:5002/api/health/version
```

**2. Establish BT playback.** Connect the phone via the **TP-Link UB500** adapter
(`bluetoothctl select 78:20:51:F5:FB:A7` first — `CLAUDE.md` § *Cross-Service Boundary*), activate the
Bluetooth source, start a track. Confirm the baseline:

```bash
curl -s http://radio:5000/api/audio/nowplaying   # expect "isPlaying": true
```

**3. ⭐ The pause/resume cycle — the step the stall appeared after.** Pause **on the phone**, wait for
the source to reach `Stopped` (`AUD-10` makes this take about a minute), then resume **on the phone**.

⛔ **`C-178`: do not reconnect, do not re-activate the source, and do not restart `radio-api` between
the pause and the resume.** Every one of those re-runs `InitializeAsync`, which has had a catch-up
since long before this row — a reconnect would make a broken build pass.

Then, within ~20 s:

```bash
curl -s http://radio:5000/api/audio/nowplaying
```

**Pass:** `"isPlaying": true`, and `extendedMetadata.PlaybackStatus == "Playing"`.
**Fail:** `"isPlaying": false` with `PlaybackStatus == "Playing"` — the exact signature from step 0.

**4. The UI, which is the row's actual complaint.** ⛔⛔ **RE-ORDERED 2026-09-08 — `C-181`. DO NOT
PRESS THE TRANSPORT BUTTON UNTIL 4a AND 4b ARE RECORDED.** `PlayAsync` writes
`State = AudioSourceState.Playing` unconditionally at `AudioSourceBase.cs:96`, so **pressing the
button clears the stall on a broken build** and every check after it then passes. The pre-repair
version of this step told the tester to press it first.

On the kiosk, in this order:

- **4a. Read the transport button's label without touching it.** It must say **Pause**, not Play
  (`NowPlayingDock.razor:81-82`, `NowPlayingPanel.razor:322`/`:328`). ⭐ **This is the fastest tell and
  it is non-destructive.** On a broken build it reads Play, because `_isPlaying` is false.
- **4b. Confirm a `SongRec recognized` line appears within ~15 s of the resume** — still without
  pressing anything. Read the **file sink**, not journald (`CLAUDE.md` § *Deployment*). ⚠ Only
  meaningful if step 0b came back `0`.
- **4c. Only now, press the button.** It must **pause**. ⚠ **On a broken build it sends `Play`
  (`NowPlayingDock.razor:273-274`) to `POST /api/audio`, which takes the `else` at
  `AudioController.cs:199` and logs `"Started playback"` (`:202`).** ⛔ **Do NOT grep for
  `"Resumed playback on …"` — the pre-repair plan named that string and it CANNOT appear here.** It
  belongs to `POST /api/audio/start` (`:280`), which the transport UI never calls. `C-173`.
- **4d. `albumArtUrl` leaves `/images/default-album-art.png`.** This is the row's headline symptom —
  ⚠ **but it is NOT a clean pass/fail criterion, `C-183`.** `appsettings.Production.json:51` sets
  `UseShazamForAllSources: true`, and `AUD-1` (unfixed) lets a SongRec misidentification replace
  correct AVRCP metadata and leave the placeholder. **If 4b showed an identification and 4d still
  shows the placeholder, that is `AUD-1`, not a failure of this row** — record it and move on. ⛔ Do
  not "fix" it by flipping the flag; `AUD-1`'s row records that this kills BT album art entirely.

⚠ **Check the UI, not only the endpoint** (`C-171`): the panel is driven by the SignalR push from
`AudioStateUpdateService.cs:674`/`:704`, not by the controller, and its change detection (`:519`,
`:546`) can suppress a push if nothing but `IsPlaying` moved.

⚠ **And do not curl `GET /api/audio/state` — it does not exist.** The state endpoint is
`GET /api/audio` (`AudioController.cs:53`, projecting `IsPlaying` at `:68-69`). §0.4 carries the
correction.

**5. The promotion log line.** It is `Information` in `Radio.API`, so it is in the **file sink**, not
journald (`CLAUDE.md` § *Deployment*):

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c "promoting to Playing" $F'
```

⚠ **A zero here is not automatically a failure.** If step 3's resume produced a clean AVRCP edge that
Task 3b accepted, the fast path handled it and the backstop never ran. **Both routes are correct
outcomes**; record which one fired. What *is* a failure is `isPlaying:false` in step 3.

**6. Sleep.** With BT playing, trigger sleep and confirm the phone's stream is actually paused
(`SleepService.cs:299-305`) rather than merely muted (`:316`). This is the highest-consequence item in
the blast radius and the only one a user experiences as *"it played all night"*.

⚠ **Keep every log read bounded** — `--since '-10min'`, never a tail. `CLAUDE.md` records the
correlation between log volume on this box and audible distortion.

---

## 6. Docs and queue

| # | Task |
|---|---|
| 1 | `docs/BUILDER_QUEUE.md` — Builder marks `AUD-12` 🚧 at claim and ✅ at merge. ⛔ **Planner did not touch this file**; §9 carries the row wording for the owner to apply. |
| 2 | `docs/queue/AUD-12.md` — Builder appends the plan link and the §5 UAT outcome. ⛔ **Planner did not touch this file either.** |
| 3 | `design/FUTURE-WORK.md` — add §7.3's metadata-dictionary concurrency item (`C-175`). ⛔ **NOT §7.1's `LinuxBluetoothService` item any more** — it was filed as queue row **`AUD-14`** on 2026-09-06 and duplicating a live queue row in `FUTURE-WORK.md` is how two records drift apart. |
| 4 | ⛔ **`CLAUDE.md` — nothing.** § *Pre-Merge Review*'s example #2 (#469) remains accurate; this row is a sibling path, not a recurrence, so there is no correction to make there. Stated so its absence is not read as an oversight. |
| 5 | ⛔ **`docs/HANDOFF-GA-PUNCH-LIST.md` — nothing.** `AUD-12` was minted straight into the queue on 2026-09-06 and has no punch-list row, so there is no tier count to move. |

---

## 7. Deliberately not done

### 7.1 ⭐ The `LinuxBluetoothService` media-player lifecycle — ✅ **FILED as `AUD-14`**

`C-174`. **Yes, it warranted a row, and it now has one:** `docs/queue/AUD-14.md`,
`docs/BUILDER_QUEUE.md:44`, filed 2026-09-06 from this section and citing it. ⛔ **This section is now
history, not a recommendation** — do not file it a second time. Its row records the two facts that
matter here: **`AUD-12` must NOT wait for it**, and if `AUD-14` lands first the `HasCapturePath` guard
becomes belt-and-braces rather than load-bearing. The reasoning is kept below because `AUD-14`'s own
plan will need it:

**What it is.** `AttachMediaPlayerAsync:2534-2540` dedups on `_mediaPlayerPath == objectPath &&
_mediaPlayer != null`; `OnInterfaceRemoved:929-932` returns early for anything that is not `Device1`,
so `_mediaPlayer` is never nulled and `_mediaPlayerPath` is never cleared. A player re-attaching at
the same path therefore takes the `return` at `:2539` **before** both `:2542`'s
`_playerPropertiesWatcher?.Dispose()` and `:2549-2556`'s initial `Status` / `Track` read.

**Why it is a row and not a bullet in this one.** Three reasons:
1. **It is a different layer with a different blast radius.** It changes BlueZ object-lifecycle
   handling for every consumer of `IBluetoothService`, on the live audio path, on a box that is
   already the subject of two other open BT rows.
2. **It needs its own UAT**, and a harder one — a disconnect/reconnect cycle that exercises the
   re-attach, which is precisely the sequence `AUD-10` currently makes painful.
3. **Fixing `BluetoothAudioSource` alone is sufficient for this symptom.** The catch-up in Task 2
   recovers from a missed initial `Status` read regardless of why it was missed.

**Why it is nevertheless not merely adjacent.** Its second consequence — a stale properties watcher
left subscribed on a dead path — is the mechanism by which an AVRCP `Playing` can reach a
`BluetoothAudioSource` that `OnDeviceDisconnected` has already torn down. That is `C-170`'s unsafe
case, and it is why Task 3b ships with a guard instead of the two-word change the brief suggested.
**Cite this row when filing it.**

**Rough shape, so the row can be estimated:** null `_mediaPlayer` / `_mediaPlayerPath` and dispose
`_playerPropertiesWatcher` when `MediaPlayer1` is removed, which means `OnInterfaceRemoved` stops
returning early for non-`Device1` interfaces; plus a test that a re-attach at the same path re-reads
`Status`. Small in diff, large in what it touches.

### 7.2 `AudioController.cs:215`'s "Paused playback" log

`C-172`. It logs `Information` unconditionally after a `PauseAsync` that the base class may have
refused at `Warning` one frame earlier. That is the comment-and-log-accuracy class `CLAUDE.md`
§ *Pre-Merge Review* exists for, and it is worth fixing. ⛔ **Not here:** `PrimaryAudioSourceBase.PauseAsync`
returns `Task`, so there is no result to check, and giving it one changes a base-class signature and
every call site — a refactor wearing a logging fix's clothes. **Also note it becomes far less
misleading once this row lands**, since the state it lies about stops occurring.

### 7.3 The unsynchronized metadata dictionary

`C-175`. `_metadata` (`USBAudioSourceBase.cs:26`) is a plain `Dictionary<string, object>` written from
D-Bus callback threads and enumerated on request threads at `AudioDtoMapper.cs:148-155`. A concurrent
write during that enumeration throws `InvalidOperationException`. ⛔ **Not fixed here** — it is
pre-existing, it affects **every** source class that derives from `USBAudioSourceBase`, and the right
answer (a `ConcurrentDictionary`, or a lock, or an immutable snapshot on read) is a design choice with
an allocation cost on a resource-constrained box. §1.3 and §0.3(c) are this row's response to it:
**do not make it load-bearing**, rather than fix it.

### 7.4 The four other `IsPlaying` projections

`C-171`. `AudioController.cs:68-69`, `AudioStateUpdateService.cs:674`, `:704`, and the change
detection at `:519`/`:546` are all **correct projections of a wrong input**. Fixing the input fixes
all four. Listed in §0.4 so they are covered by §5's UAT, not because they need edits.

### 7.5 Ducking

`C-180`. Confirmed clean — `DuckingService.cs` has zero `AudioSourceState` references. Recorded as a
negative result so a future fixer does not spend a session there.

---

## 8. Self-review

### 8.1 Verified first-hand at `066a0d5c`, and **re-verified at `c9ebd824` on 2026-09-08**

⚠ **Everything in this subsection was read again during the repair.** The re-read used `main` at
`c9ebd824`; `git diff main HEAD` touched only `docs/`, so no source file differed.

- **Both defect predicates**, read in full with their enclosing methods: `OnPlaybackStatusChanged`
  (`:1126-1154`) and `ApplyDeferredCaptureState` (`:435-463`) with all three call sites
  (`:472`/`:489`/`:500`).
- **`TEST-2`'s merged diff** (`ae474372`, #614) for both `BluetoothAudioSource.cs` and
  `BluetoothAudioSourceTests.cs`, plus the current state of both files — **not** `TEST-2`'s plan.
- **`design/TESTING.md` § *Test Seams*** in full (`:288-417`), including the four kinds, the six rules
  and the two enforcement holes.
- **The `#469` git facts.** `git log -L 1123,1151` on the handler returns `b717314b` (2026-03-10);
  `git log -S 'ApplyDeferredCaptureState'` returns `9bfb7cbe` (2026-08-10). Five months apart.
- **⭐ The scope question, exhaustively.** Every write and read of `MetadataInternal` in `src/`; the
  absence of any `Clear()`; `SetDefaultMetadata`'s six keys in full (`USBAudioSourceBase.cs:157-165`);
  `AudioDtoMapper.ExtractMetadataToNowPlaying`'s `Except` (not an allowlist);
  `AudioManager._sourceCache`'s every reference, establishing one long-lived instance per type.
- **The BlueZ edge semantics** that make `Ready` terminal: `OnPlayerPropertiesChanged:2708-2731`,
  `UpdatePlaybackStatus:2733-2752`, and both callers of `AttachMediaPlayerAsync` (`:735`, `:2508`).
- **`C-174` in full**, including the ordering of the dedup `return` against
  `_playerPropertiesWatcher?.Dispose()`.
- **The determinism seam:** `MockBluetoothService` in full, `AudioSourceBase`'s `State` setter
  (`:40-54`) and `PlayAsync` (`:81-97`), and — added by the repair — `TEST-2`'s whole harness and its
  `WaitForAsync` remarks (`:899-1268`).
- **The existing test file's relevant regions** — `:19-53`, `:157-258`, `:886-1268` — so §4.7's list is
  read rather than guessed. ⭐ **The repair confirmed by grep that all three previously-named tests are
  absent from the file**, rather than assuming the queue note was right about which ones went.
- **The blast radius**, independently re-verified site by site at `066a0d5c` and **again at
  `c9ebd824`**, which produced §0.4's three original corrections, four additional sites, **two flatly
  false claims** (`GET /api/audio/state`; the play/pause endpoint) and **four wrong file paths**.
- **The `C-173` trace end to end**, which is what exposed the wrong endpoint:
  `NowPlayingDock.HandlePlayPauseAsync` (`:269-280`) → `POST /api/audio`
  (`AudioController.UpdatePlaybackState`, `:124`) → `:191-203` → `AudioSourceBase.PlayAsync:96`.
- **`AUD-1`'s plan §0.14 and §4.4**, and **`AUD-18`'s dossier**, for the collision and UAT sections.

### 8.2 What could not be verified, and what it costs

1. **Nothing here was built or run.** Every code block is written against read source and is
   unexecuted. `HasCapturePath`, `TryPromoteToPlayingFromLastAvrcpStatus` and the widened predicate
   have not been compiled.
2. **No box was touched**, by instruction. §5 is entirely unexecuted, and §0.5's *"unlogged"* row —
   the swallowed AVRCP `Playing` between 10:17:54 and 10:18:19 — is an **inference** from the code, not
   a log line. It is the load-bearing inference in the verdict. Step 0 of §5 is what would confirm it
   directly; if the stall is reproducible and step 0 shows `PlaybackStatus == "Stopped"` rather than
   `"Playing"`, **the diagnosis is incomplete** and something other than a swallowed edge produced the
   10:18:19 transition.
3. **`_avrcpReportsPlaying`'s `volatile` is reasoned, not measured.** No torn read was observed; the
   argument is that a nullable enum could tear and a bool cannot. If the Builder prefers
   `Volatile.Read`/`Volatile.Write` over the `volatile` keyword, that is equivalent and fine.
4. **Whether `T5`'s scenario is already covered** by an existing test was checked by grep, not by
   reading every test in the file. §4.5 says to check before adding.
7. ⭐ **NEW — the repaired §4 is written against read source and has not been compiled either.** In
   particular: that `Mock<SoundComponent>` reaching `InitializeAsync:166-174` leaves
   `GetSoundComponent()` returning that exact object, and that `T2`'s `PlayAsync` therefore lands
   `Playing` with `HasCapturePath` true, are **inferences from `TEST-2`'s own tests doing the same
   thing through a different call site** (`:486`/`:497` rather than `:159`/`:166`). The dispatch is
   textually identical in both places, but nobody has run it.
8. ⭐ **NEW — `C-181`'s consequence is derived, not observed.** `AudioSourceBase.cs:96` assigns
   `Playing` unconditionally and `RouteCaptureThroughMixerAsync` returns at `:532`, so pressing the
   button *should* clear the stall silently. **This was never done on the box** — it is a code read.
   If a tester presses the button on a stalled source and the state does **not** move to `Playing`,
   something in `PlayCoreAsync` throws on that path and `C-181` is wrong in a way worth knowing.
5. **`HasCapturePath`'s completeness is a judgement.** It enumerates the four things this class knows
   about routing. If a fifth exists — some state inside `SoundFlowPlaybackService` this source does not
   mirror — the guard would refuse a promotion it should make, and the symptom would be `T2` passing
   while the box still stalls in one narrow case. §0.6 flags this as the one thing that could double
   the estimate.
6. **`AUD-10`'s interaction is assumed from its queue row, not reproduced.** If the transport does
   *not* die on pause for a given phone, §5 step 3 may never reach `Stopped` and would then exercise
   only the `Paused → Playing` path, which was never broken.

### 8.3 What would falsify this plan's central decision

§1's design assumes the phone's AVRCP report is a **trustworthy** statement about whether audio is
flowing. If it is not — if BlueZ can report `Playing` for a transport that is dead, which `AUD-11`
hints at from a different direction — then promoting on it substitutes one wrong answer for another,
and the correct input is the capture pipeline's own liveness (`BluetoothCaptureWatchdog`,
`GeneratorStalled`) rather than AVRCP at all. **That would be a larger row and a different design.**
This plan takes AVRCP as authoritative because the row's own evidence does: the graph was `[active]`
and the audio was audible at the moment the source claimed otherwise, so AVRCP and reality agreed and
only the state machine disagreed with both.

---

## 9. Queue row wording

⛔ **Planner did not edit `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-12.md`.** The wording below is
for the owner to apply.

### 9.1 Replacement line for `docs/BUILDER_QUEUE.md` § Queue

Replace the existing `AUD-12` line with this one. It keeps the row's shape, status, dossier link and
empty-dependency cell; only the **Plan** and **Branch** cells change substantively.

```
| AUD-12 | ⭐ **NEW 2026-09-06, observed live — the BT source stalls at `Ready` while audio is playing, so fingerprinting is gated off and album art never resolves.** ✅ **INVESTIGATION CLOSED 2026-09-06: sibling path, NOT a recurrence of #469** — `git log -L` puts the handler's last edit five months before that PR. — [detail](queue/AUD-12.md) | 📋 | [`AUD-12-the-source-that-stalled-at-ready.md`](../design/plans/AUD-12-the-source-that-stalled-at-ready.md) · **0.5 d** · ✅ **PLAN REPAIRED 2026-09-08 after `TEST-2` (#614) — re-anchored to `c9ebd824`, `C-177` re-pointed, Task 4 DELETED; the collision note on the dossier is DISCHARGED** · **both predicates** (the `Ready` catch-up **and** the guarded `Stopped` arm) · ⚠ **the investigation is DONE — do not re-run it** · ⛔ **NOT auto-mergeable: user-facing audio behaviour and UAT is BLOCKED on the owner's phone; §5 is written and deferred** | _no spec doc — measured on `radio` 2026-09-06; log evidence in the dossier_ · #469 (`9bfb7cbe`) is the adjacent merged fix and is **not** the cause | — _(no row dependency; claimable now. **⚠ FOUR conditions can make this row's UAT vacuous — the plan's §5 opens with the table: `AUD-10` (`C-178`, a reconnect makes a BROKEN build pass), `AUD-18` (`C-182`, a dead fingerprint tap makes a CORRECT build fail — run the pre-UAT check), `AUD-1` (`C-183`, album art is not a clean criterion), and pressing the transport button (`C-181`, it CLEARS the stall).** Touches **`BluetoothAudioSource.cs` only** — `TEST-2` has now merged, and `AUD-1` also claims that file but at disjoint regions (`:840`, `:870-894`, `:896-908` vs. this row's `:41`, `:184-194`, `:435-463`, `:1126-1154`). **Prefer `AUD-12` first**: `AUD-1`'s own plan says so, because this row is what makes `AUD-1`'s UAT possible at all.)_ | `fix/aud-12-bt-source-stalled-at-ready` |
```

⚠ **`MockBluetoothService.cs` is deliberately gone from that cell.** The pre-repair row said *"Also
one line in `MockBluetoothService.cs`, which nothing else claims"*; Task 4 is deleted and this row now
touches exactly one production file.

### 9.2 Banner line for the file's `Last updated`

> **Last updated:** 2026-09-08 (Planner) — `AUD-12`'s plan
> ([`design/plans/AUD-12-the-source-that-stalled-at-ready.md`](../design/plans/AUD-12-the-source-that-stalled-at-ready.md), **0.5 d**)
> was **repaired after `TEST-2` (#614)**: re-anchored to `main` at `c9ebd824`, `C-177` re-pointed at
> the tests that replaced the deleted one, and Task 4 dropped — `TEST-2` refuted the premise it rested
> on, so the row no longer touches `MockBluetoothService.cs`. Its investigation remains closed:
> **sibling path, not a recurrence of #469**. ⛔ Still **not auto-mergeable** — UAT is blocked on the
> owner's phone, and §5 now names **four** ways that UAT can go silently vacuous.

### 9.3 Suggested append to `docs/queue/AUD-12.md`

To be added under a new `## Plan` heading, at the end of the dossier — it answers the three scope
questions the dossier itself asked:

> ## Plan
>
> [`design/plans/AUD-12-the-source-that-stalled-at-ready.md`](../../design/plans/AUD-12-the-source-that-stalled-at-ready.md),
> written 2026-09-06 against `main` at `066a0d5c`, **repaired 2026-09-08 against `c9ebd824`**.
> **0.5 d** (unchanged). ⛔ **Not auto-mergeable.**
>
> ✅ **The `TEST-2` collision note at the top of this dossier is DISCHARGED.** All three anchors it
> named are re-pointed: `ApplyDeferredCaptureState` is prescribed as `private` at `:457-463`; no test
> calls it; and `C-177` now pins `DeviceConnectedEvent_TakesTheMatchingBranch_AndLandsReady` (`:1068`)
> and `DeviceConnectedEvent_WhenPlatformManaged_LandsReadyWithoutAcquiring` (`:1147`) instead of the
> deleted `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`. **`TEST-2` also made this row smaller**
> — its Task 4 is deleted, because the "needs a native SoundFlow engine" premise it rested on is the
> same one `TEST-2` refuted. The row no longer touches `MockBluetoothService.cs`.
>
> **The § *Check the prior art* instruction is DISCHARGED.** Verdict: **sibling path, not a recurrence
> of #469.** `ApplyDeferredCaptureState` is present verbatim with all three call sites and all three
> tests; the handler that swallows the transition (`OnPlaybackStatusChanged`) was last edited
> `b717314b`, 2026-03-10 — five months before #469, which never touched it.
>
> **The three scope questions, answered:**
> 1. *Why `Stopped -> Ready`, and what drives `Ready -> Playing`?* `ApplyDeferredCaptureState:458`
>    writes `Ready`; **nothing** drives it back out. The only exit is an AVRCP edge, and BlueZ emits
>    `PropertiesChanged` only on change — the phone is already playing, so no edge is coming. `Ready`
>    is terminal. A second predicate compounds it: `:1136` discards an AVRCP `Playing` that arrives
>    while `Stopped`, which `AUD-10` makes routine.
> 2. *Is `isPlaying:false` the same defect?* **The same defect.** `AudioController.cs:576` is a pure
>    projection of `primarySource.State == Playing`. Not a second mapping bug.
> 3. *What else gates on `Playing`?* Fifteen rows, enumerated in the plan's §0.4 — fourteen of them
>    genuinely gated, plus one (`POST /api/audio/start`) recorded only because the pre-repair plan
>    mistook it for the transport button's endpoint. Including
>    **Sleep**, where `_wasPlayingBeforeSleep` stays false and the phone streams through the night, and
>    the **SignalR push path** the dossier's list missed. ⚠ **Ducking is NOT among them** —
>    `DuckingService.cs` has zero `AudioSourceState` references, recorded so nobody looks there.
>
> **One new row recommended:** the BlueZ media-player re-attach path
> (`LinuxBluetoothService.cs:2534-2540` + `:929-932`) skips the initial `Status` read **and** leaks a
> properties watcher onto a dead path. Plan §7.1 has the shape and the argument for keeping it
> separate.
