# PLAN — `AUD-12` · The Bluetooth source that stalled at `Ready` while the audio kept playing

> **Row:** `AUD-12`, [`docs/queue/AUD-12.md`](../../docs/queue/AUD-12.md). 🟠 **P1.** Observed live on
> `radio` 2026-09-06; ranked first of the three BT rows filed that day.
> **Branch:** `fix/aud-12-bt-source-stalled-at-ready`
> **Estimate:** **0.5 d.** §0.6 says why half a day survives, and what would push it to one.
> ⛔ **NOT auto-mergeable.** §0.8.
> **Planned against** `main` at **`066a0d5c`**. Every line number below was read out of the tree at
> that commit; where a line is likely to move it is quoted as well as numbered.
> **The investigation is closed.** A `team-debugger` pass on 2026-09-06 settled the verdict at ~85%
> confidence: **sibling path, not a recurrence of #469.** §0.2 records the git fact that closes it.
> This plan re-verified every anchor and does not re-litigate the verdict.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`BluetoothAudioSource` has a state machine whose `Ready` state is, in practice, terminal. Two
predicates put it there and neither can get it out. `OnPlaybackStatusChanged` (`:1133`) accepts an
AVRCP `Playing` edge only from `Ready` or `Paused`, so one arriving while the source is `Stopped` —
which `AUD-10` makes routine — is written into metadata and then silently discarded.
`ApplyDeferredCaptureState` (`:456-459`) *preserves* `Playing` but never *re-derives* it, so a source
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
and the row correctly told the planner to check it before writing anything. Checked, at `066a0d5c`:

- **`ApplyDeferredCaptureState` is present verbatim** at `BluetoothAudioSource.cs:454-460`, with its
  XML doc at `:435-453` intact, and all three call sites live: `:469` (platform-managed arm), `:486`
  (`AudioCaptureDevice` arm), `:497` (`SoundComponent` arm).
- **Its tests survive** at `BluetoothAudioSourceTests.cs:907-957`, all three of them.
- **The handler that actually swallows the transition was never touched by #469.**
  `git log -L 1123,1151:src/…/BluetoothAudioSource.cs` returns `b717314b` (2026-03-10,
  *"Eliminate all build warnings and clean up test output"* — braces only) as the most recent change
  to `OnPlaybackStatusChanged`. `ApplyDeferredCaptureState` was introduced by `9bfb7cbe`
  (2026-08-10). **Five months apart, and #469 never edited the handler.**

⭐ **What #469 *did* do is make this row's second half visible.** Its XML doc at `:438-446` states the
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
`primarySource`, the *active* source, and `AudioDtoMapper.ExtractMetadataToNowPlaying:148-155` builds
`ExtendedMetadata` as `metadata.Keys.Except(new[] { "Title", "Artist", "Album", "AlbumArtUrl" })` —
**not an allowlist**. So the projection would have shown `PlaybackStatus` had the dictionary been the
BT source's and had the key been there. The capture is fully explained by *which source was active*.

**(b) The key cannot be removed or cleared. Verified by exhaustive grep over `src/`.**

| Fact | Evidence |
|---|---|
| Written in exactly one place | `BluetoothAudioSource.cs:1125`, `MetadataInternal["PlaybackStatus"] = e.ToString();` — unconditional, **above** the `switch`, so it records every AVRCP report including the discarded ones |
| Read in exactly one place | `:189`, the `InitializeAsync` catch-up |
| `MetadataInternal.Clear()` | **does not appear anywhere in `src/`** |
| `MetadataInternal.Remove(...)` | one site, `:805`, and it removes `AlbumArtUrl` only |
| `SetDefaultMetadata` (`:740`, on disconnect) | **does not clear** — `USBAudioSourceBase.cs:157-165` assigns six keys (`Title`, `Artist`, `Album`, `AlbumArtUrl`, `Source`, `Device`) and `PlaybackStatus` is not among them |
| The source instance is long-lived | `AudioManager` caches one per `AudioSourceType` (`:403 _sourceCache[sourceType] = source;`) and clears the cache only at disposal (`:611`) — so the dictionary survives every source switch for the process's life |

⇒ **Once written, the key is sticky.** It is absent only in the window between a source's construction
and the first AVRCP status event it ever sees — and in that window there *is* no last-known status to
catch up to, so a catch-up correctly does nothing. **There is no "durable last-known-status field"
gap to close.**

**(c) ⛔ And the fix still must not read it.** Two independent reasons, and either alone is enough:

1. **It is a display projection, not a state-machine input.** `_metadata` is a plain
   `Dictionary<string, object>` (`USBAudioSourceBase.cs:26`), written from D-Bus callback threads
   (`OnPlaybackStatusChanged`, `OnMetadataChanged`) and **enumerated on ASP.NET request threads** by
   `AudioDtoMapper.cs:148-155`. That is a pre-existing hazard this row does not fix (`C-175`), and
   routing the state machine's only recovery path through it makes the hazard load-bearing.
2. **The existing read is an unguarded cast.** `:190` is `(string)pbStatus == "Playing"` — an
   `InvalidCastException` the day anything writes a non-string under that key.

⇒ The fix records the same fact in a dedicated field and **keeps writing the metadata key unchanged**,
because that key is a shipped API observable (`PlaybackStatusChanged_UpdatesMetadata`,
`BluetoothAudioSourceTests.cs:191-197`) and the UAT confirmation in §5 depends on it.

### 0.4 The blast radius — re-verified, with three corrections to the row's enumeration

Everything below is gated on `State == AudioSourceState.Playing` and therefore silently wrong while
the source sits at `Ready`. Line numbers are current at `066a0d5c`.

| Site | Line | What breaks |
|---|---|---|
| `SoundFlowAudioTap.cs` | **`:135`** `return activeSource?.State == AudioSourceState.Playing;` | `IsActive` → `CaptureAsync:142` returns `null`. **The one we caught, because it has a visible output.** |
| `PlayHistoryTracker.cs` | **`:95`** `if (e.NewState != AudioSourceState.Playing) { return; }` | Play history never records the track. Silent return, no log. |
| `PrimaryAudioSourceBase.cs` | **`:112`**, **`:126`** | `PauseAsync` / `ResumeAsync` return early. |
| `AudioController.cs` | **`:215-216`** | Logs `"Paused playback"` after a pause that did not happen. |
| `AudioController.cs` | **`:576`** | `nowPlaying.IsPlaying` — the `isPlaying:false` in the row. |
| `AudioController.cs` | **`:68-69`** | `GET /api/audio/state` → `PlaybackStateDto.IsPlaying`. **A second endpoint the row did not name.** |
| `AudioController.cs` | **`:276-279`** | Play/pause POST takes the `PlayAsync()` branch instead of `ResumeAsync()` and logs `"Resumed playback"` — a full re-activation of an already-streaming source. |
| `AudioStateUpdateService.cs` | **`:674`**, **`:704`** | The **SignalR push** payloads. ⭐ This, not the controller, is what actually drives the live Blazor UI. |
| `AudioStateUpdateService.cs` | **`:519`**, **`:546`** | Change detection: `IsPlaying` never flips, so the transition contributes no dirty signal and no push happens unless some other field also moved. |
| `NowPlayingDock.razor` | `:48`, `:81`, `:82`, `:219`, **`:238`**, **`:273`** | Button shows "Play" while playing — and `:273` `var action = _isPlaying ? "Pause" : "Play";` is what makes pressing it **send `Play`**. |
| `NowPlayingPanel.razor` | `:678`, `:734`, **`:21`**, **`:322`**, **`:328`**, **`:957`** | Same, plus the ken-burns gate. The row cited the assignment sites; `:322`/`:328` are the render sites. |
| `BluetoothAudioSource.cs` | `:640`, `:653` | The capture retry loop bails at the first 10 s tick **and** suppresses its own `"capture retry exhausted"` warning. |
| `SleepService.cs` | **`:299-300`**, `:302`, `:305` | `_wasPlayingBeforeSleep` stays `false`, `PauseAsync` is never called, **the phone streams through sleep**. `:316` still mutes the output, so it is inaudible but running. |
| `AudioManager.cs` | **`:273`** | Switching back to a stalled BT source re-invokes `PlayAsync` on a source already streaming (`:259` sets `Bluetooth => true` for auto-play). |

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
| 10:16:53 | `Playing -> Paused` | `:1144` `case Paused when State == Playing` |
| 10:17:54 | `Paused -> Stopped` | `:1147` `case Stopped when State == Playing \|\| State == Paused` |
| *(unlogged)* | — | Phone resumes. AVRCP `Playing` arrives while `State == Stopped`. `:1125` writes `"Playing"` into metadata; `:1133`'s accept set is `Ready \|\| Paused`, so the transition is **discarded**. |
| 10:18:19 | `Stopped -> Ready` | `ApplyDeferredCaptureState:458`. Nothing in `OnPlaybackStatusChanged` writes `Ready`. |
| 10:18:19 → ∞ | *nothing* | `Ready` is terminal. BlueZ already reported `Playing` and emits only on change. |

⇒ **Widening `:1133` alone does not fix the measured stall**, because at the moment the swallowed
edge arrived the source had no capture path yet (see `C-170`'s guard) and the promotion would be
refused. ⇒ **The catch-up in `ApplyDeferredCaptureState` is the load-bearing half**; the `:1133`
widening is the fast path that stops the same stall re-forming a different way. Ship both.

⭐ **Corroborating negative evidence.** The retry loop's exhaustion warning
(`:655 "capture retry exhausted after {Max} attempts"`) does **not** appear in the measured window —
consistent with `:640` `if (_playbackId != null || State != AudioSourceState.Playing) { return; }`
returning on the first tick because the source was no longer `Playing`. The absence of that line is
evidence *for* the verdict, not against it. `C-179`.

### 0.6 The estimate

**0.5 d.** It survives because the work is small and every seam it needs already exists:

1. **Three edits in one file**, all inside methods this plan quotes in full: one new field, one new
   helper, one widened predicate, one replaced `if`. No new types, no DI change, no migration, no UI.
2. **The test fixture already has the determinism seam.** `BluetoothAudioSourceTests` constructs a
   real `BluetoothAudioSource` over `MockBluetoothService`, whose `SimulatePlaybackStatusChange`
   (`MockBluetoothService.cs:154-157`) raises the event **synchronously**, and `AudioSourceBase`'s
   `State` setter (`:40-54`) runs `LogStateChange` and `OnStateChanged` **inline**. Every assertion in
   §4 is a straight-line read after a synchronous call. `C-176`.
3. **The negative case is already pinned** by `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`
   (`:946-957`), which this row must leave passing unchanged.

⚠ **What would push it to 1 d:** Task 3's `HasCapturePath`. If the Builder finds a real case where a
`Stopped` source legitimately holds a capture path it should *not* be promoted from — or, the reverse,
a promotion the guard wrongly refuses on the box — that is a design conversation, not a tweak. **Do
not widen or narrow the guard silently; say so in the PR body and put it to the owner.**

### 0.7 ⚠ Twelve constraints found while planning — numbering continues from `C-168`

**`C-169` answers the row's open scope question. `C-170` changes the design. `C-171`, `C-172` and
`C-173` correct the row's own blast-radius enumeration. `C-174` promotes an "adjacent" defect into
this row's justification.**

---

**`C-169` — ⚠ ANSWERS THE SCOPE QUESTION. `MetadataInternal["PlaybackStatus"]` cannot be absent or
cleared once written; the 10:24 capture is explained by source identity, not by a missing key. The fix
still must not read it.** §0.3 has the full derivation and the grep table. The consequence for the
design: a dedicated field, and the metadata key is preserved untouched as the API observable.

---

**`C-170` — ⚠⚠ CHANGES THE DESIGN. `Stopped` has TWO provenances and only one of them is safe to
promote out of. The suggested "add `Stopped` to `:1133`'s accept set" is unguarded.**

- `:1147` — the **phone's** transport stopped. The source's own pipeline is intact: `_playbackId`,
  `_captureDevice` / `SoundComponent` all still hold. Promoting back to `Playing` is correct.
- `:739` — **`OnDeviceDisconnected`.** By the time it assigns `Stopped`, `:714-735` has already
  removed the generator from the mixer, stopped and nulled `_captureDevice`, and nulled
  `SoundComponent` and `_captureGenerator`. Promoting *this* `Stopped` to `Playing` asserts audio is
  flowing from a phone that is not connected — the inverse of the bug being fixed, and worse, because
  `:1139` would then fire `TryReacquireCaptureAsync` into a device that is gone.

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

**`C-173` — the visible symptom of pressing Play on a stalled source is a full re-activation, and it
is fixed for free.** `AudioController.cs:276-277` branches on
`State == Stopped || State == Ready` → `PlayAsync()` at `:279`, then logs `"Resumed playback on
{Source}"`. So the button that shows "Play" (`NowPlayingDock.razor:273`) tears down and re-establishes
a stream that was already running, and says it resumed. **Named because it is the fastest UAT tell**
(§5 step 4), not because it needs its own change.

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
**Recommend its own row**; §7.1 states the shape and the cost.

---

**`C-175` — the metadata bag is unsynchronized, this row does not fix it, and that is the second
reason not to build the fix on it.** `USBAudioSourceBase.cs:26`
`private readonly Dictionary<string, object> _metadata = new();`, exposed as both `Metadata` (`:83`)
and `MetadataInternal` (`:98`) — **the same object**. Written from D-Bus callback threads; enumerated
on request threads at `AudioDtoMapper.cs:148-155`. A concurrent write during that enumeration throws
`InvalidOperationException`. Pre-existing, unrelated to this row's symptom, filed in §7.3.

---

**`C-176` — the determinism story is complete before this row starts, and there is no clock in it.**
`CLAUDE.md` § *Test Timing* forbids racing a wall clock against a wall clock. Nothing here needs one:
`MockBluetoothService.SimulatePlaybackStatusChange:154-157` is
`PlaybackStatusChanged?.Invoke(this, status);` — synchronous — and `AudioSourceBase.State`'s setter
(`:43-53`) is idempotent-guarded and raises `StateChanged` inline. **Every §4 assertion reads `State`
on the same thread, immediately after the call that changed it.** ⛔ **No `Task.Delay` in any new
test.** (Two exist elsewhere in the fixture — `:891` — and are not a precedent to copy.)

---

**`C-177` — ⛔ `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady` MUST NOT BE INVERTED.**
`BluetoothAudioSourceTests.cs:946-957` asserts a freshly-constructed source (`State == Created`) lands
in `Ready`. **That remains correct**: only a source whose last known AVRCP status is `Playing` may be
promoted, and a fresh source has no last known status. The design in §1 preserves it by construction
— the new field's default is "not playing" — so **the test must pass unmodified**, and a Builder who
finds themselves editing it has the design wrong.

---

**`C-178` — ⚠ `AUD-10` both causes this row's `Stopped` state and poisons its UAT.** The row it
depends on for its own realism is the one that makes it hard to verify: pausing on the phone destroys
the A2DP transport and resume never restores it, so a full reconnect is currently required to get
audio back — and a reconnect re-runs `InitializeAsync`, which **already** contained a catch-up. ⛔ **A
UAT that reconnects between the pause and the check proves nothing.** §5 step 5 says how to avoid it.

---

**`C-179` — the missing `"capture retry exhausted"` line is evidence for the verdict.** `:653`
`if (State == AudioSourceState.Playing && _playbackId == null)` gates that warning, and `:640` returns
out of the loop entirely once the state leaves `Playing`. A stalled source therefore loses its capture
**invisibly**. Recorded so the Builder does not read the log's silence as "the retry loop was fine".

---

**`C-180` — recorded negatives, so nobody re-derives them.** Checked and **not** affected by a BT
source stalled at `Ready`: `DuckingService.cs` (zero `AudioSourceState` references);
`EventPlaybackService.cs:365` (operates on **event** sources via `Resolve(playbackId).Source`, not the
primary source); `QueueController.cs:455` (`IsPlaying` from engine state only, never source state);
and the state comparisons in `FilePlayerAudioSource`, `SDRRadioAudioSource`, `RadioAudioSource`,
`TTSEventSource`, `EventAudioSourceBase` and `USBAudioSourceBase:468`, none of which is reachable from
a `BluetoothAudioSource` instance.

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
- ⛔ **Do not add `Stopped` to `:1133` unguarded.** `C-170`.
- ⛔ **Do not add `Created` to `:1133`'s accept set.** A source at `Created` has no capture, is not in
  the mixer, and has not been initialized; `InitializeAsync`'s catch-up promotes it at the right
  moment. Widening the accept set to `Created` would claim `Playing` for a source that cannot play.
- ⛔ **Do not invert `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady`.** `C-177`.
- ⛔ **Do not touch `LinuxBluetoothService.cs`.** `C-174`, §7.1.
- ⛔ **Do not fix `AudioController.cs:215`'s "Paused playback" log.** `C-172`, §7.2.
- ⛔ **Do not make `_metadata` concurrent.** `C-175`, §7.3.
- ⛔ **Do not use `Task.Delay` or any wall-clock wait in a new test.** `C-176`, `CLAUDE.md`
  § *Test Timing*.
- ⛔ **Do not touch the box.** No SSH, no `curl` against `radio`, until §5 is authorized.

---

## 1. Decision — one durable fact, one shared helper, one guarded predicate

### 1.1 The shape, stated first

**The phone's last reported AVRCP transport status becomes a first-class field on the source.** Two
places consult it: the recovery path that lands in `Ready`, and the AVRCP handler that must stop
treating `Stopped` as absorbing.

| Half | Where | What changes |
|---|---|---|
| **The backstop** — makes `Ready` non-terminal | `ApplyDeferredCaptureState:454-460` + `InitializeAsync:184-194` | Both call one helper that promotes `Ready → Playing` when the phone's last report was `Playing`. Today only `InitializeAsync` has this logic, inline. |
| **The fast path** — makes `Stopped` non-absorbing | `OnPlaybackStatusChanged:1132-1136` | `Stopped` joins the accept set, **guarded** on the source still holding a capture path. |

### 1.2 Why this shape and not the three alternatives

| Option | Verdict |
|---|---|
| **Widen `:1133` only** (add `Stopped`) | **Rejected.** It does not fix the measured sequence — §0.5 shows the swallowed edge arrives before a capture path exists — and it leaves `Ready` terminal for every other route into it. |
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
  // is what keeps ApplyDeferredCaptureState_WhenNotPlaying_SetsReady green.
  private volatile bool _avrcpReportsPlaying;
```

**1b.** Set it at the top of `OnPlaybackStatusChanged` (`:1123-1126`), leaving the metadata write and
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
  /// ApplyDeferredCaptureState_WhenNotPlaying_SetsReady (AUD-12 C-177).
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

**2c.** Rewrite `ApplyDeferredCaptureState` (`:454-460`). Its existing `<summary>` and both
`<para>` blocks (`:435-453`) stay **verbatim**; append one `<para>` and replace the body:

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
  internal void ApplyDeferredCaptureState()
  {
    if (State == AudioSourceState.Playing)
    {
      return;
    }

    State = AudioSourceState.Ready;
    TryPromoteToPlayingFromLastAvrcpStatus();
  }
```

⚠ **The early return is behaviour-identical to the old `if (State != Playing) { … }`** — it is written
this way only so the two statements after it read as one sequence. All three call sites (`:469`,
`:486`, `:497`) are unchanged.

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
  /// </remarks>
  private bool HasCapturePath =>
    _bluetoothService.IsAudioManagedByPlatform
    || _playbackId != null
    || _captureDevice != null
    || SoundComponent != null;
```

**3b.** Widen the `Playing` arm (`:1132-1136`):

```csharp
      case BluetoothPlaybackStatus.Playing:
        // ⚠ Stopped joined this accept set in AUD-12, and the guard on it is not
        // decoration. AUD-10 (the A2DP transport dies on pause) drives this source
        // to Stopped routinely, and before AUD-12 an AVRCP Playing arriving in that
        // state was written to metadata four lines up and then silently dropped
        // here — Stopped was absorbing.
        //
        // ⛔ Stopped has TWO provenances. The :1147 case below is the phone's
        // transport stopping while our pipeline stays intact — promoting back is
        // correct. OnDeviceDisconnected is the other, and by the time it assigns
        // Stopped it has already pulled the generator out of the mixer and nulled
        // _captureDevice / SoundComponent. Promoting THAT to Playing would assert
        // audio is flowing from a phone that is not connected, and would then fire
        // TryReacquireCaptureAsync below at a device that is gone. HasCapturePath
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

⛔ **The `Paused` (`:1144`) and `Stopped` (`:1147`) arms are untouched.** They mirror the phone
downward and are correct; widening them is a different change with a different argument.

---

### Task 4 — make the platform-managed flag settable on the test double

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs:31`

`HasCapturePath` cannot be made true from a unit test today: `_playbackId`, `_captureDevice` and
`SoundComponent` are all private or protected and none is reachable without a native SoundFlow engine,
and `MockBluetoothService.IsAudioManagedByPlatform` is a hard-coded `=> false`. One line opens it:

```csharp
        // Settable so a test can exercise the platform-managed routing arm — the
        // only route to BluetoothAudioSource.HasCapturePath that does not need a
        // native SoundFlow AudioCaptureDevice. Defaults to false, which is what
        // MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse asserts.
        public bool IsAudioManagedByPlatform { get; set; }
```

A settable auto-property still satisfies the get-only `IBluetoothService.IsAudioManagedByPlatform`.

⚠ **Two existing tests assert the default and must stay green** —
`BluetoothAudioSourceTests.cs:254-258` and `WasapiLoopbackTests.cs:174-178`, both
`Assert.False(mockBt.IsAudioManagedByPlatform)`. A fresh fixture per test means the default holds.

**Fallback if the owner would rather not touch `src/` for a test seam:** build a local
`Mock<IBluetoothService>` in the test as `InitializeAsync_WhenPlatformManagesAudio_SetsReadyWithoutCapture`
(`:199-233`) already does, and raise the event with
`btMock.Raise(b => b.PlaybackStatusChanged += null, btMock.Object, BluetoothPlaybackStatus.Playing)`.
More setup, no production-tree change. **Builder picks one and says which in the PR body.**

---

## 3. Ordering

Task 1 first — Tasks 2 and 3 both read `_avrcpReportsPlaying`. Task 2 before Task 3, so the backstop
(the half that actually fixes the measured stall, §0.5) is in place before the fast path. Task 4 any
time before §4's `T2`/`T3`.

**One PR.** The deliverable is a property — *the source's state reflects whether audio is flowing* —
and §0.5 shows neither half establishes it alone. Splitting would ship a PR whose test suite asserts
half an invariant, which is how a partial fix gets recorded as a complete one.

---

## 4. Test plan

**File:** `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` (extend; the fixture
at `:19-53` is what these use).

> ⚠ **`CLAUDE.md` § *Test Timing* applies and is fully satisfiable here — there is no clock to race.**
> `MockBluetoothService.SimulatePlaybackStatusChange` (`:154-157`) raises the event synchronously, and
> `AudioSourceBase`'s `State` setter (`:40-54`) runs `LogStateChange` and `OnStateChanged` inline. Every
> assertion below reads `State` on the calling thread immediately after the call that changed it —
> the state machine is *pinned by construction*, not by patience. ⛔ **No `Task.Delay`.** (`C-176`.)

> ⚠ **One background task exists and cannot affect an assertion — say so in the test file so the next
> reader does not add a wait "to be safe".** The `Playing` arm fires `_ = TryReacquireCaptureAsync()`
> (`:1141`) when the source is `Playing` with no `_playbackId`. Against `MockBluetoothService`,
> `GetAudioCaptureDeviceAsync` returns a `string` (`:126-129`), so neither the `AudioCaptureDevice` nor
> the `SoundComponent` arm is taken and the method returns without touching `State` — its own summary
> at `:665-669` says it "does not alter the source state". Likewise `PlayCoreAsync` starts
> `RetryCaptureInBackgroundAsync`, whose first act is a 10 s `Task.Delay`; `DisposeAsync` cancels it
> (`:289`) long before it ticks.

### 4.1 `T1` — the measured stall, reproduced end to end ⭐

**This is the regression pin.** It replays §0.5's table without a clock and exercises both halves.

```csharp
  [Fact]
  public async Task StalledAtReady_WhenPhoneResumed_IsPromotedWhenDeferredCaptureLands()
  {
    // AUD-12's measured sequence: Playing -> Paused -> Stopped -> Ready, then a
    // ten-minute silence while the mixer stayed audible.
    await _source.PlayAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, _source.State);

    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Paused);
    Assert.Equal(AudioSourceState.Paused, _source.State);

    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Stopped);
    Assert.Equal(AudioSourceState.Stopped, _source.State);

    // The phone resumes. This edge is still refused — the source holds no capture
    // path at this point, which is what HasCapturePath guards (AUD-12 C-170) — but
    // the fact that the phone reports Playing is now recorded.
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Playing);
    Assert.Equal(AudioSourceState.Stopped, _source.State);

    // The deferred-capture retry lands. Before AUD-12 this parked the source in
    // Ready permanently: BlueZ sends no further edge once the phone is playing.
    _source.ApplyDeferredCaptureState();

    Assert.Equal(AudioSourceState.Playing, _source.State);
  }
```

> **Falsifying mutations, both to be run:** revert Task 2c's `TryPromoteToPlayingFromLastAvrcpStatus()`
> call → the final assertion fails at `Ready`. Revert Task 1b's field write → same. ⭐ **Reverting Task
> 3b alone must NOT make this test fail** — that is the point of §0.5, and if it does, the test is
> asserting the wrong half.

### 4.2 `T2` — the fast path, when the capture path is intact

```csharp
  [Fact]
  public async Task PlaybackStatusPlaying_WhileStopped_PromotesWhenCapturePathIsIntact()
  {
    // Platform-managed routing stands in for "the capture path is live" — the only
    // arm of HasCapturePath reachable without a native SoundFlow engine.
    _mockBluetooth.IsAudioManagedByPlatform = true;

    await _source.PlayAsync(CancellationToken.None);
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Stopped);
    Assert.Equal(AudioSourceState.Stopped, _source.State);

    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Playing);

    Assert.Equal(AudioSourceState.Playing, _source.State);
  }
```

> **Falsifying mutation:** restore `:1133`'s original two-clause accept set → fails at `Stopped`.

### 4.3 `T3` — the guard actually guards ⛔

```csharp
  [Fact]
  public async Task PlaybackStatusPlaying_WhileStopped_DoesNotPromoteWithNoCapturePath()
  {
    await _source.PlayAsync(CancellationToken.None);
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Stopped);

    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Playing);

    // No playback id, no capture device, no generator, no platform routing.
    // Claiming Playing here would assert audio is flowing from a phone that may
    // have disconnected — OnDeviceDisconnected assigns Stopped only after tearing
    // the capture out of the mixer. Plan AUD-12 C-170.
    Assert.Equal(AudioSourceState.Stopped, _source.State);
  }
```

> **Falsifying mutation:** drop `&& HasCapturePath` from `:1133` → this test fails while `T1` and `T2`
> still pass. ⭐ **That asymmetry is the whole value of `T3`** — without it the guard could be deleted
> by a future simplification and every other test would stay green.

### 4.4 `T4` — the near-miss that must not promote

```csharp
  [Fact]
  public void ApplyDeferredCaptureState_WhenPhoneReportsPaused_StaysReady()
  {
    // Same shape as the promotion case, one status different. Without this, a
    // mutation that promotes on ANY seen status passes T1.
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Paused);

    _source.ApplyDeferredCaptureState();

    Assert.Equal(AudioSourceState.Ready, _source.State);
  }
```

> **Falsifying mutation:** change Task 1b's write to `_avrcpReportsPlaying = true;` unconditionally →
> fails.

### 4.5 `T5` — `InitializeAsync`'s catch-up still works through the shared helper

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

### 4.6 `T6` — the downstream invariant, in the fixture's own idiom

Mirror `DeferredCaptureAcquisition_AfterPlay_KeepsAudioTapActive` (`:923-944`), reusing its
`SoundFlowAudioTap` construction verbatim, but reaching `Playing` by promotion rather than by never
leaving it:

```csharp
  [Fact]
  public async Task PromotedSource_KeepsAudioTapActive()
  {
    _mockBluetooth.SimulatePlaybackStatusChange(BluetoothPlaybackStatus.Playing);
    await _source.InitializeAsync(CancellationToken.None);
    Assert.Equal(AudioSourceState.Playing, _source.State);

    var engineMock = new Mock<IAudioEngine>();
    engineMock.Setup(e => e.State).Returns(AudioEngineState.Running);
    var managerMock = new Mock<IAudioManager>();
    managerMock.Setup(m => m.ActiveSource).Returns(_source);

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

Name each in the PR body with its result:

| Test | Line | Why it is at risk |
|---|---|---|
| `ApplyDeferredCaptureState_WhenNotPlaying_SetsReady` | `:946-957` | ⛔ `C-177`. `Created` must still yield `Ready`. **Inverting it is wrong.** |
| `DeferredCaptureAcquisition_AfterPlay_LeavesSourcePlaying` | `:907-921` | Task 2c rewrites the method it exercises. |
| `DeferredCaptureAcquisition_AfterPlay_KeepsAudioTapActive` | `:923-944` | Same. |
| `PlaybackStatusChanged_UpdatesMetadata` | `:191-197` | Task 1b edits that handler; the metadata key must be untouched. |
| `InitializeAsync_WhenNoCaptureDevice_SetsReadyState` | `:160-169` | Task 2b replaces the block right after the assignment it asserts. |
| `MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse` | `:254-258` | Task 4 changes that member. |
| `MockBluetoothService_IsAudioManagedByPlatform_ReturnsFalse` (`WasapiLoopbackTests`) | `:174-178` | Same, different assembly. |

### 4.8 Gates

- `dotnet build --configuration Release` — 0 warnings (warnings are errors in Release).
- `dotnet test --configuration Release` — full suite green.
  ⛔ **Never pipe it to `tail`** (`CLAUDE.md`): redirect, `echo "exit=$?"`, then grep the file. Read the
  **per-project** summary lines.
  Known-failing on Windows and not regressions: four `SrcVariableResamplerTests`
  (`libsamplerate.so.0`, `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network,
  `Category=Integration`, CI-excluded).
- ⚠ If run from a git worktree under a path containing a `worktrees` segment, `LogSafetyLintTests` is
  red for an unrelated reason (`PHN-5` `C-100`). Not a finding about this row.
- **Every mutation in §4.1–§4.4 run, with its result in the PR body.** A mutation that does not make
  its test fail is a finding, not a formality — this repository has repeatedly shipped tests that
  passed against a deliberately broken implementation.

---

## 5. UAT — ⛔ DEFERRED, and the exact steps for when the phone returns

⛔ **This cannot be run now.** The owner's phone is unavailable, and without an A2DP source there is no
AVRCP stream, no transport, and nothing to pause. ⛔ **And nothing in this section may be attempted
against the box unattended** — `CLAUDE.md` records that heavy log reads on `radio` correlate with
audible audio distortion, and the box is on WiFi with nobody physically present.

**Run all six steps in order. Steps 3 and 5 are the ones that fail on a broken build.**

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

**4. The UI, which is the row's actual complaint.** On the kiosk, confirm all four:
- the transport button reads **Pause**, not Play (`NowPlayingDock.razor:81-82`,
  `NowPlayingPanel.razor:322/328`);
- pressing it **pauses** rather than re-activating the source (`C-173` — a broken build sends `Play`
  and logs `"Resumed playback on …"`);
- a `SongRec recognized` line appears within ~15 s of the resume;
- **`albumArtUrl` leaves `/images/default-album-art.png`.** This is the row's headline symptom.

⚠ **Check the UI, not only the endpoint** (`C-171`): the panel is driven by the SignalR push from
`AudioStateUpdateService.cs:674`/`:704`, not by the controller, and its change detection (`:519`,
`:546`) can suppress a push if nothing but `IsPlaying` moved.

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
| 3 | `design/FUTURE-WORK.md` — add §7.1's `LinuxBluetoothService` media-player lifecycle item and §7.3's metadata-dictionary concurrency item. |
| 4 | ⛔ **`CLAUDE.md` — nothing.** § *Pre-Merge Review*'s example #2 (#469) remains accurate; this row is a sibling path, not a recurrence, so there is no correction to make there. Stated so its absence is not read as an oversight. |
| 5 | ⛔ **`docs/HANDOFF-GA-PUNCH-LIST.md` — nothing.** `AUD-12` was minted straight into the queue on 2026-09-06 and has no punch-list row, so there is no tier count to move. |

---

## 7. Deliberately not done

### 7.1 ⭐ The `LinuxBluetoothService` media-player lifecycle — **recommended as its own row**

`C-174`. **Yes, it warrants a row.** The recommendation, and the reasoning, so the owner can decide
without re-deriving it:

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

### 8.1 Verified first-hand at `066a0d5c`

- **Both defect predicates**, read in full with their enclosing methods: `OnPlaybackStatusChanged`
  (`:1123-1151`) and `ApplyDeferredCaptureState` (`:435-460`) with all three call sites.
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
- **The determinism seam:** `MockBluetoothService` in full, and `AudioSourceBase`'s `State` setter.
- **The existing test file's relevant regions** — `:1-95`, `:158-258`, `:880-958` — so §4.7's list is
  read rather than guessed.
- **The blast radius**, independently re-verified site by site, which produced §0.4's three
  corrections and four additional sites.

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
| AUD-12 | ⭐ **NEW 2026-09-06, observed live — the BT source stalls at `Ready` while audio is playing, so fingerprinting is gated off and album art never resolves.** ✅ **INVESTIGATION CLOSED 2026-09-06: sibling path, NOT a recurrence of #469** — `git log -L` puts the handler's last edit five months before that PR. — [detail](queue/AUD-12.md) | 📋 | [`AUD-12-the-source-that-stalled-at-ready.md`](../design/plans/AUD-12-the-source-that-stalled-at-ready.md) · **0.5 d** · **both predicates** (the `Ready` catch-up **and** the guarded `Stopped` arm) · ⚠ **the investigation is DONE — do not re-run it** · ⛔ **NOT auto-mergeable: user-facing audio behaviour and UAT is BLOCKED on the owner's phone; §5 is written and deferred** | _no spec doc — measured on `radio` 2026-09-06; log evidence in the dossier_ · #469 (`9bfb7cbe`) is the adjacent merged fix and is **not** the cause | — _(no row dependency; claimable now. **⚠ `AUD-10` is what drives this source to `Stopped` routinely and also poisons the UAT — read the plan's `C-178` before testing: a reconnect between the pause and the check makes a BROKEN build pass.** Touches `BluetoothAudioSource.cs`, which **`AUD-1` and `TEST-2` also claim** — if any two are in flight, expect anchors to move. Also one line in `MockBluetoothService.cs`, which nothing else claims.)_ | `fix/aud-12-bt-source-stalled-at-ready` |
```

### 9.2 Banner line for the file's `Last updated`

> **Last updated:** 2026-09-06 (Planner) — `AUD-12` has a plan
> ([`design/plans/AUD-12-the-source-that-stalled-at-ready.md`](../design/plans/AUD-12-the-source-that-stalled-at-ready.md), **0.5 d**),
> and its investigation is closed: **sibling path, not a recurrence of #469**. ⛔ Flagged **not
> auto-mergeable** — UAT is blocked on the owner's phone and the plan's §5 carries the deferred steps.

### 9.3 Suggested append to `docs/queue/AUD-12.md`

To be added under a new `## Plan` heading, at the end of the dossier — it answers the three scope
questions the dossier itself asked:

> ## Plan
>
> [`design/plans/AUD-12-the-source-that-stalled-at-ready.md`](../../design/plans/AUD-12-the-source-that-stalled-at-ready.md),
> written 2026-09-06 against `main` at `066a0d5c`. **0.5 d.** ⛔ **Not auto-mergeable.**
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
>    is terminal. A second predicate compounds it: `:1133` discards an AVRCP `Playing` that arrives
>    while `Stopped`, which `AUD-10` makes routine.
> 2. *Is `isPlaying:false` the same defect?* **The same defect.** `AudioController.cs:576` is a pure
>    projection of `primarySource.State == Playing`. Not a second mapping bug.
> 3. *What else gates on `Playing`?* Fourteen sites, enumerated in the plan's §0.4 — including
>    **Sleep**, where `_wasPlayingBeforeSleep` stays false and the phone streams through the night, and
>    the **SignalR push path** the dossier's list missed. ⚠ **Ducking is NOT among them** —
>    `DuckingService.cs` has zero `AudioSourceState` references, recorded so nobody looks there.
>
> **One new row recommended:** the BlueZ media-player re-attach path
> (`LinuxBluetoothService.cs:2534-2540` + `:929-932`) skips the initial `Status` read **and** leaks a
> properties watcher onto a dead path. Plan §7.1 has the shape and the argument for keeping it
> separate.
