# PLAN — `AUD-4` · Unify the source-removal layers, and stop calling the registry a mixer

> **Row:** `AUD-4`, `docs/BUILDER_QUEUE.md:135`. 📋, owner-protected tranche (near the end).
> **Branch:** `refactor/unify-source-removal-and-rename-mixer`
> **Estimate:** **2 d** for the minimal shape, **3 d** for the recommended split. §0.8.
> **Planned against** `main` at **`35e4ed5a`**. Every line number below was read out of the tree at
> that commit. The row's own anchors were re-verified 2026-08-11 against `8b1ce0a` and **four of them
> have since moved** — see `C-155`.
> **⚠ This plan contradicts its own row on three points**, one of which is the row's headline
> justification. §0.2, §0.3, §0.4. Read them before Task 1.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs` is not a mixer and has nothing to
do with SoundFlow. It is a `List<IAudioSource>` roster plus three scalars (master volume, balance,
mute) and three change events; its only `using`s are `Microsoft.Extensions.Logging` and
`Radio.Core.Interfaces.Audio` (`:1-2`). It moves no audio samples and holds no SoundFlow object
(`C-153`). Its `RemoveSource` mutates that list by object reference (`:134`) while logging
`"Removed audio source {SourceId} ({SourceType}) from mixer"` (`:136-138`) — a line that reads as an
audio detach and is not one. The row pairs a rename with a unification of the removal layers, on the
theory that the rename is what stops the bug recurring. **That theory does not survive contact with
the history** (`C-147`), and this plan says so rather than executing a justification it disproved.
What *does* survive is a real, unnamed, live half-job in the engine's stop path (`C-151`) — which is
the row's scope (a) with a concrete defect attached to it at last.

### 0.2 ⚠ The row's `03a6fea` story is false in its specifics, and `CLAUDE.md` repeats it

This is the row's headline provenance and it is the thing most likely to be copied forward, so it
gets settled first. **The row, `CLAUDE.md:442`, `docs/ROADMAP.md` and `docs/HANDOFF-GA-PUNCH-LIST.md`
all assert the same wrong causal chain.**

The claim, as written in `CLAUDE.md`:

> `SoundFlowMasterMixer` logs *"Removed audio source … from mixer"* while only mutating a
> `List<IAudioSource>` — the real detach lives elsewhere. A later fix (`03a6fea`) **trusted the
> wording, landed one layer too high**, and silently did nothing for months.

**What `03a6fea` actually changed** (`git show 03a6fea -- .../AudioManager.cs`, hunk `@@ -208,13 +208,14 @@`):

```csharp
-      if (oldSource != null && oldSource != source &&
-          (oldSource.State == AudioSourceState.Playing ||
-           oldSource.State == AudioSourceState.Paused))
+      if (oldSource != null && oldSource != source)
```

It changed the **entry guard of the whole stop block** and the log string beneath it. It did not
touch `mixer.RemoveSource(oldSource)`; that call is not even in the hunk's context. The block it
guards contains **both** `await oldPrimary.StopAsync(...)` (the real teardown chain) and
`mixer.RemoveSource(...)` (the roster mutation). Before `03a6fea` both were skipped together; after
it, both ran.

**Why it did nothing anyway.** The identical predicate was duplicated one stack frame down. At
`03a6fea`, `src/Radio.Infrastructure/Audio/Sources/Primary/PrimaryAudioSourceBase.cs:169-179`:

```csharp
  public virtual async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    await StopCoreAsync(cancellationToken);
    State = AudioSourceState.Stopped;
  }
```

That is character-for-character the predicate `03a6fea` deleted at the `AudioManager` layer, and it
gates the entire detach chain (`StopAsync` → `StopCoreAsync` → `_playbackService.StopAsync(key)` →
`MasterMixer.RemoveComponent`). Control now reached `oldPrimary.StopAsync(...)` and that method
returned early. The guard was in fact **triplicated** — `EventAudioSourceBase.cs:97-107` carried a
stricter `if (State != AudioSourceState.Playing)`.

**The defect was a duplicated state guard, not a wrong layer, and no part of it ran through
`RemoveSource`.** It was fixed 166 days later by PR #468 / `8b1ce0a`, which rewrote the base guard to
`if (State is AudioSourceState.Created or AudioSourceState.Disposed)` and pinned it with
`AudioSourceBaseStopTests.cs`.

One further detail, because a fixer will otherwise "correct" the wrong thing: the false comment
`// Remove from mixer so its audio components are disconnected` (`AudioManager.cs:213` today)
**predates `03a6fea`** and was inherited by it, not written by it (`C-157`).

### 0.3 ⚠⚠ So the rename would NOT have prevented `03a6fea`. The row's central claim fails.

The row says: *"The rename is therefore the durable half of this row, not the cosmetic half."* The
task that commissioned this plan asked for a straight answer, so here it is.

**No. A registry-honest name would not have prevented that commit.**

The author of `03a6fea` was not misled by the wording. Their diagnosis was *correct* and is nearly
verbatim what #468 concluded 5.5 months later — the commit message reads *"remove state guard in
`SwitchSourceAsync()` that skipped stopping sources already in Stopped/Ready state. AVRCP events can
set BT state to Stopped while `BufferedSoundGenerator` and `pw-record` remain active in mixer."*
They were reasoning about the **stop path and the state flag**, not about the roster mutation. The
predicate that defeated them lives in a different file, in a call they did not modify. Renaming a
sibling call on the next line does not change that predicate, does not appear anywhere in its path,
and supplies no prompt to step into it.

**Where the naming complaint does earn credit is detection, not prevention — and that is a real but
weaker argument.** After `03a6fea`, switching away from a Stopped-state source newly emitted *both*
`"Stopping old source X (State=Stopped) before switching to Y"` **and** `"Removed old source X from
mixer"`, where previously neither appeared. The commit manufactured a log trace that reads exactly
like a successful teardown while the audio kept playing. An honest name and an honest log
(*"removed from source registry"*) would have made that trace visibly **not** a teardown, and would
have put pressure on the false comment at `:213`. That plausibly shortens 166 days of
non-detection. It does not stop the commit being written.

**Consequence for this row, stated plainly so the owner can overrule it:** the recurrence-preventer
for this defect class **already shipped in #468** (guard removed, contract pinned by test). The
rename is still worth doing — on clarity, on log honesty, and because the type's name is wrong in
both halves (`C-153`) — but it must be justified as **hygiene**, not as the thing that stops the bug
coming back. If the owner's appetite for this row was purchased by the stronger claim, the honest
move is to re-price it now. §0.8 prices both shapes.

### 0.4 ⚠ The `AUD-2` dependency is false. This row does not wait.

`BUILDER_QUEUE.md:437` asserts `AUD-2` and `AUD-4` are *"two symptoms of ONE root cause"*, that
`AUD-4` sees the mismatch *"from the teardown side — a sweep that would miss the SDR component"*,
and that `AUD-2` must be claimed first because *"it decides the key."* Verified at `35e4ed5a`, all
three are wrong (`C-148`):

- **Per-source teardown is key-symmetric.** `SDRRadioAudioSource.cs:915` mints
  `_playbackId = $"sdr-radio-{Guid.NewGuid():N}"`; `:1027` calls
  `await _playbackService.StopAsync(_playbackId, cancellationToken)` — the same field, and again at
  `:1059`. Every source registers and stops under one key. Only a *third party* addressing by
  `IAudioSource.Id` guesses wrong, and that is `AUD-2`'s ducking bug, not this row's.
- **The registry is not keyed by string at all.** `_sources.Remove(source)` (`:134`) is an object-
  reference removal. There is no key here to mismatch.
- **The row's prescribed workaround is unnecessary.** It says *"Sweep `_activeComponents` instead."*
  There is nothing to sweep around; see also `C-150`, because the sweep it names never runs.

**`AUD-4` has no dependency on `AUD-2` and must not be sequenced behind it.** The queue-row wording
in §6 records this.

### 0.5 The row's designated "first task" dissolves on a read

The row makes the three-layer reconciliation the mandatory first task and forbids code movement
until it is settled, on the grounds that #468's *"`StopCoreAsync` — the only code that detaches the
component"* and the row's *"the real detach is `SoundFlowPlaybackService.StopAsync` →
`RemoveComponent`"* *"cannot both be the whole truth."*

**They are not competing claims. They are the same chain at two points** (`C-149`). Every
`StopCoreAsync` implementation delegates to the playback service — verified across all of them:

| Source | `StopCoreAsync` | delegates to |
|---|---|---|
| `BluetoothAudioSource` | `:238` | `_playbackService.StopAsync(_playbackId, ct)` `:249-262` |
| `SDRRadioAudioSource` | `:1009` | `_playbackService.StopAsync(_playbackId, ct)` `:1027` |
| `FilePlayerAudioSource` | `:868` | `_playbackService.StopAsync(...)` |
| `USBAudioSourceBase` | `:394` | `_playbackService.StopAsync(...)` `:408` |
| `TestToneAudioSource` | `:90` | `_playbackService.StopAsync(Id, ct)` `:94` |
| `TTSEventSource` | `:292` | `_playbackService.StopAsync(...)` `:304` |
| `AudioFileEventSource` | `:428` | `_playbackService.StopAsync(...)` |

`StopCoreAsync` is the **entry point**; `SoundFlowPlaybackService.StopAsync` → `RemoveComponent`
(`:494` → `:526`/`:548`) is the **mechanism it calls**. The detach is not "doubly non-unified". So
the true layer map is **two things, not three**:

- **A — the roster.** `SoundFlowMasterMixer._sources`, object-keyed, moves no audio.
- **B — the detach chain.** `AudioSourceBase.StopAsync` → `StopCoreAsync` →
  `SoundFlowPlaybackService.StopAsync(key)` → `MasterMixer.RemoveComponent`. String-keyed,
  key-symmetric, and **authoritative**.

Builder may treat §0.5 as the reconciliation the row demands, and start at Task 1.

### 0.6 ⭐ The defect nobody named: engine stop clears the roster and leaves the audio attached

This is the row's scope (a) with an actual bug under it, and it is the strongest reason to run this
row at all (`C-151`).

`SoundFlowAudioEngine.StopAsync` (`:684-706`):

```csharp
      State = AudioEngineState.Stopping;
      _logger.LogInformation("Stopping audio engine");

      // Clear all sources from the mixer
      _masterMixer.ClearSources();        // :700  — roster only
```

It clears layer **A** and never touches layer **B**. Stopping the audio engine therefore empties the
roster — so `GetActiveSources()` reports nothing playing — while every `SoundPlayer` and
`SoundComponent` stays attached to `playbackDevice.MasterMixer` and keeps producing audio.

And the sweep that would have fixed it exists and is never called (`C-150`):

```
$ grep -rn 'StopAll' src tests tools --include=*.cs
src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs:777:  public void StopAll()
```

**Zero callers, anywhere** — src, tests and tools. (Every other `StopAll` hit in the tree is
`StopAllDucking*`, an unrelated method.) So tonight's falsification note is right that
`StopAll` *cannot* miss a key — `_activePlayers.Keys.Concat(_activeComponents.Keys).Distinct()`
(`:788`) enumerates the dictionaries' own keys — but it is dead code, and the premise that a global
sweep is in service is false.

**This is the "caller able to do half the job" the row was aiming at, and it is a live defect, not a
hypothetical.** Task 4 wires it.

### 0.7 ⚠ Ten constraints found while planning — numbering starts at `C-146`

**⚠ Numbering note (`C-161`).** The global `C-nn` sequence has already collided among tonight's
parallel plans: `AUD-5` and `TEST-7` both claim `C-115`–`C-126`; `OPS-2` and `GV-6` both claim
`C-128`–`C-137`. This plan starts at **`C-146`**, past `GV-6`'s `C-145`, the current high-water
mark. A reconciliation of the sequence is not this row's job, but the collision should not be
discovered a third time.

**`C-146` — ⚠ CHANGES THE ROW'S PROVENANCE. `03a6fea` did not land at `RemoveSource`.** It widened
the guard around the whole stop block. It failed because the same predicate was duplicated in
`PrimaryAudioSourceBase.StopAsync` (and triplicated in `EventAudioSourceBase`), not because it was
at the wrong layer. §0.2. The wrong story is repeated in `CLAUDE.md:442`, `docs/BUILDER_QUEUE.md`
(`:76`, `:135`, `:437`, `:440`), `docs/ROADMAP.md` (`:131`, `:146`) and
`docs/HANDOFF-GA-PUNCH-LIST.md` (`:196`, `:1120`). Task 7 corrects `CLAUDE.md`; §6 carries the
queue wording; the rest are the owner's call.

**`C-147` — ⚠⚠ CHANGES THE ROW'S JUSTIFICATION. The rename would not have prevented `03a6fea`.**
§0.3. It earns its place on log honesty and detection latency, not on recurrence prevention — the
recurrence-preventer shipped in #468. This is the one finding most likely to change the owner's
appetite, so it is stated before any task.

**`C-148` — ⚠ THE `AUD-2` DEPENDENCY IS FALSE.** §0.4. Per-source teardown is key-symmetric
(`SDRRadioAudioSource.cs:915` mints, `:1027` and `:1059` stop with the same field); the roster is
object-keyed and has no string key to mismatch. `AUD-4` does not wait for `AUD-2`.

**`C-149` — the row's "competing only-detach claims" are caller and callee.** §0.5. The row's
mandatory first task is answerable from a read; there are two layers, not three.

**`C-150` — `SoundFlowPlaybackService.StopAll()` has zero callers in the entire tree.** §0.6. It is
structurally incapable of missing a key and it never runs.

**`C-151` — ⚠⚠ A LIVE DEFECT THE ROW DOES NOT NAME. `SoundFlowAudioEngine.StopAsync:700` clears the
roster and never stops the audio.** §0.6. Engine stop empties `GetActiveSources()` while every
component stays attached to the real mixer. This is the row's scope (a) made concrete, and it is the
strongest argument for running the row.

**`C-152` — ⚠⚠ RENAMING THE FILE SILENTLY DISABLES A LINT RULE.**
`tests/Radio.Core.Tests/LogSafetyLintTests.cs:157` is:

```csharp
    Of(@"\bsource\s*\??\s*\.\s*Name\b", "source.Name", "SoundFlowMasterMixer.cs"),
```

The third argument is `OnlyInFile`, matched against `Path.GetFileName(file)` with
`StringComparison.Ordinal` (`:215`). Rename the file without updating the string and the rule
applies to **no file at all** and passes green forever. The test has scan floors for `files.Count`
and `callsScanned` (`:233-236`) but **no assertion that each `OnlyInFile` names a file that exists**.
Four other rules share the fragility (`AnnouncementService.cs`, `TTSFactory.cs`,
`GvTrunkApiService.cs`, `ContactResolutionService.cs`). This is precisely the failure class the row
exists to stop — a check asserting more than it does — arriving inside the row's own blast radius.
Task 6 fixes the aliveness gap for all five.

**`C-153` — the class has no SoundFlow dependency at all, so BOTH halves of its name are wrong.**
`SoundFlowMasterMixer.cs:1-2` imports only `Microsoft.Extensions.Logging` and
`Radio.Core.Interfaces.Audio`. It is not SoundFlow's, and it is not a mixer. The row only requires
dropping "Mixer"; dropping "SoundFlow" is equally warranted and free in the same diff. Its folder
(`Audio/SoundFlow/`) is wrong for the same reason.

**`C-154` — ⭐ the two halves of the type share no state, which is what makes a split cheaper than a
rename.** `_masterVolume` / `_balance` / `_isMuted` are touched only by `MasterVolume`, `Balance`,
`IsMuted`, `GetEffectiveVolume`, `GetLeftChannelGain`, `GetRightChannelGain` and the three events.
`_sources` / `_sourcesLock` are touched only by `AddSource`, `RemoveSource`, `GetActiveSources`,
`ClearSources`. **There is not one line where the halves interact.** No single honest name exists for
a type that is both, which is why the prior design review's `MasterAudioState`
(`design/AUDIO-PIPELINE-REVIEW.md:173`) is not adopted verbatim — it names the volume half and hides
the roster half, which is the half that caused the bug. §1.

**`C-155` — four of the row's re-verified anchors have drifted since `8b1ce0a`.** The row states
they were byte-exact on 2026-08-11. At `35e4ed5a`:

| Row's anchor | Actual at `35e4ed5a` | Cause |
|---|---|---|
| `SoundFlowMasterMixer.cs:109-121` (`RemoveSource`) | **`:128-141`** | `TTS-11` added a 19-line comment block to `AddSource` |
| `SoundFlowMasterMixer.cs:118` (the log) | **`:136-138`** | same |
| `SDRRadioAudioSource.cs:908` (`sdr-radio-<guid>`) | **`:915`** | — |
| `AudioSourceBase.cs:100-124` (`StopAsync`) | **`:118-128`** | #468's own doc comment `:99-117` |
| `AudioManager.cs:214-217` | `:213-218` (comment `:213`, call `:216`, log `:217`) | — |

Also: **the log string the row quotes no longer exists.** The row quotes
`"Removed audio source {SourceId} ({SourceName}) from mixer"`; `TTS-11` changed `{SourceName}` to
`{SourceType}`. The current text is `"Removed audio source {SourceId} ({SourceType}) from mixer"`.
Anchors that held exactly: `SoundFlowMasterMixer.cs:10`, `:13`; `SoundFlowPlaybackService.cs:25`,
`:494`, `:526`, `:548`.

**`C-156` — `RemoveSource` must NOT be made to detach audio, and one caller proves it.**
`SourcesController.cs:729-733` removes a file event source from the roster on a failure path where
`PlayFileAsync` returned false and **no audio component was ever created**:

```csharp
      if (!success)
      {
        mixer.RemoveSource(fileSource);
        return StatusCode(500, new { error = "Failed to start audio playback" });
      }
```

A `RemoveSource` that also tore down audio would be doing compensating work for a component that
does not exist. The unification must therefore make roster eviction a **consequence** of the source
stopping, not fold a detach into the roster call. §1.2.

**`C-157` — the false comment at `AudioManager.cs:213` predates `03a6fea`.** `// Remove from mixer so
its audio components are disconnected` was inherited by that commit, not introduced by it. Worth
knowing so the fix is not mis-attributed a second time.

**`C-158` — corrected blast-radius counts.** A first enumeration pass overcounted `tests/` for
`SoundFlowMasterMixer` (reported 37, actual **30**). §0.9 carries counts re-derived by hand.

**`C-159` — `SoundFlowPlaybackService.Dispose()` disposes components without `RemoveComponent`.**
`:880-922` calls `player.Stop()` / `player.Dispose()` / `component.Dispose()` and clears both
dictionaries, but never calls `MasterMixer.RemoveComponent`. Defensible at dispose, since the device
is going away — but it is a **third** removal shape (`StopAsync` removes-then-disposes; `StopAll`
would remove-and-dispose but never runs; `Dispose` disposes without removing). Recorded, **not
fixed here** — §5.

**`C-160` — a TTS source added to the roster is never removed.** `SourcesController.cs:655` calls
`mixer.AddSource(ttsSource)` and nothing ever removes it;
`tests/Radio.API.Tests/Controllers/AudioControllerLogSafetyTests.cs:24-26` already records this. The
state-driven eviction in Task 3 closes it as a side effect. Called out so the behaviour change is
expected rather than discovered.

### 0.8 The estimate

**2 d** minimal / **3 d** recommended, against the row's `_medium_`.

| | Minimal (rename in place) | Recommended (split) |
|---|---|---|
| Rename `SoundFlowMasterMixer` → one type | ½ d | — |
| Split into two types + two interfaces | — | 1 d |
| Update all references (§0.9) | ½ d | ½ d |
| Roster eviction on state change (Task 3) | ½ d | ½ d |
| Wire `StopAll` into engine stop (Task 4) | ¼ d | ¼ d |
| Log/comment honesty (Task 5) | ¼ d | ¼ d |
| Lint aliveness (Task 6) | ¼ d | ¼ d |
| Tests + docs (Tasks 6–7) | ½ d | ½ d |

The rename dominates the diff; Tasks 3 and 4 dominate the risk. The row's `_medium_` was priced
before `C-151` was known and before the split was on the table.

### 0.9 Blast radius, counted by hand at `35e4ed5a`

Every count below was re-derived directly (`C-158`).

**`SoundFlowMasterMixer` — 46 occurrences across 17 code files:**

| Area | Files | Occurrences |
|---|---|---|
| `src/` | 5 | 15 |
| `tests/` | 11 | 30 |
| `tools/` | 1 | 1 |

`src/`: `AudioServiceExtensions.cs` 5 · `SoundFlowMasterMixer.cs` 4 · `BalanceModifier.cs` 3 ·
`SoundFlowAudioEngine.cs` 2 · `AudioManager.cs` 1.
`tests/`: `SoundFlowMasterMixerTests.cs` 7 · `SoundFlowAudioEngineTests.cs` 5 ·
`SoundFlowMasterMixerLogSafetyTests.cs` 5 · `LogSafetyLintTests.cs` 4 ·
`SoundFlowAudioEngineActiveOutputTests.cs` 2 · `EventPlaybackServiceTests.cs` 2 ·
`SoundFlowPlaybackServiceTransportTests.cs` 1 · `AudioManagerTests.cs` 1 ·
`AudioManagerDuckingLogTests.cs` 1 · `SourcesControllerLogSafetyTests.cs` 1 ·
`AudioControllerLogSafetyTests.cs` 1. `tools/`: `Radio.Tools.AudioUAT/Program.cs` 1.

**`IMasterMixer` — 27 occurrences across 19 code files** (`src/` 8 files / 12; `tests/` 11 / 15).
**`GetMasterMixer` — 43 occurrences across 17 code files** (`src/` 7 / 29; `tests/`+`tools/` 10 / 14).

**Files whose NAME must change (3):**
- `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs`
- `tests/Radio.Infrastructure.Tests/Audio/SoundFlowMasterMixerTests.cs` — note: **not** in the
  `SoundFlow/` subfolder, unlike its sibling
- `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SoundFlowMasterMixerLogSafetyTests.cs`

**Test doubles:** 8 files use `Mock<IMasterMixer>`; 8 construct a real `new SoundFlowMasterMixer(...)`;
`AudioManagerTests.cs` and `AudioManagerDuckingLogTests.cs` do **both**.

**Markdown carrying the identifier — 20 occurrences across ~14 files**, the load-bearing ones being
`CLAUDE.md:442`, `design/AUDIO-PIPELINE-REVIEW.md:173`, `design/AUDIO-DATAFLOW.md` (`:91`, `:291`),
`design/FUTURE-WORK.md` (`:392`, `:420`, `:1473`) and
`design/plans/TTS-11-no-user-text-at-rest-in-the-log.md` (10). **Merged plans under `design/plans/`
are historical records and must NOT be rewritten** — §0.10.

**⚠ Do not blind-replace the bare string `MasterMixer`.** 22 occurrences in `src/` are SoundFlow's
own property — `playbackDevice.MasterMixer` — in `SoundFlowAudioEngine.cs` (14) and
`SoundFlowPlaybackService.cs` (8). Those are the *real* mixer and must not be touched. Rename by
identifier (`SoundFlowMasterMixer`, `IMasterMixer`, `GetMasterMixer`), never by substring.

### 0.10 Things Builder must NOT do

- ⛔ **Do not sequence this behind `AUD-2`.** `C-148`.
- ⛔ **Do not build a sweep keyed by `_activeComponents` "instead of `Id`".** The row prescribes it;
  there is no mismatch on this side to work around (`C-148`) and the sweep it names is dead
  (`C-150`).
- ⛔ **Do not make `RemoveSource` detach audio.** `C-156`.
- ⛔ **Do not substring-replace `MasterMixer`.** §0.9.
- ⛔ **Do not rewrite merged plans under `design/plans/`.** They record what was believed at the
  time. `TTS-11`'s ten references and `PHN-1c`'s five stay as written.
- ⛔ **Do not rename the file without updating `LogSafetyLintTests.cs:157`.** `C-152`. This is the one
  step that fails silently and green.
- ⛔ **Do not "fix" `SoundFlowPlaybackService.Dispose`.** `C-159`, §5.
- ⛔ **Do not touch the live box.** This row has no on-box step.

---

## 1. Decision — what the type is, and what to call it

### 1.1 What it actually is

Two unrelated things wearing one name:

| Concern | State it touches | Members | Consumed by |
|---|---|---|---|
| **Master output state** | `_masterVolume`, `_balance`, `_isMuted` | `MasterVolume`, `Balance`, `IsMuted`, `GetEffectiveVolume`, `GetLeftChannelGain`, `GetRightChannelGain`, 3 events | `BalanceModifier`, `SoundFlowAudioEngine`, `DuckingService`, `AudioController` |
| **Source roster** | `_sources`, `_sourcesLock` | `AddSource`, `RemoveSource`, `GetActiveSources`, `ClearSources` | `AudioManager`, `SourcesController`, `SystemController`, `AudioEngineExtensions` |

They share no state and no method (`C-154`). Neither concern is a mixer; neither is SoundFlow's.

### 1.2 The name, justified against what the type does

**Recommended: split into two types and two interfaces.**

| Old | New | Rationale |
|---|---|---|
| `IMasterMixer` (volume half) | `IMasterOutputState` | It holds the master output's volume/balance/mute; the engine reads it and applies it to SoundFlow's real mixer. |
| `IMasterMixer` (roster half) | `IAudioSourceRegistry` | It is a roster. The row requires a name that says *registry*. |
| `SoundFlowMasterMixer` | `MasterOutputState` + `AudioSourceRegistry` | Neither is SoundFlow's (`C-153`), so the prefix goes too. |
| namespace `…Audio.SoundFlow` | `…Audio.State` (both) | Nothing here touches SoundFlow. |

**Why a split rather than one rename.** The row asks for a name that says *registry*. No single name
can say that while the type also owns master volume — which is exactly why the prior design review
reached for `MasterAudioState` (`design/AUDIO-PIPELINE-REVIEW.md:173`), a name that describes the
volume half and leaves the roster half as anonymous as it is today. Since the two halves share no
state (`C-154`), splitting costs one extra file and one extra DI line, and every call site has to be
edited either way.

**What the split buys, concretely.** The line `03a6fea`'s author read becomes:

```csharp
    _sourceRegistry.RemoveSource(oldSource);
    // "Removed old source {SourceName} from the source registry (audio detach is StopAsync)"
```

There is no "mixer" left in the sentence to mistake for one. **Per `C-147` this is a
detection-latency improvement, not a prevention guarantee** — and the plan should not be sold as
more than that.

**Fallback if the owner prefers the smaller diff:** keep one class, name it `AudioSourceRegistry`,
and leave volume/balance/mute on it as a documented wart with an `⚠` XML comment saying the type is
two things. Saves ~1 d, keeps the misnomer at half strength. **Not recommended, but legitimate** —
the row explicitly leaves the name to the implementer, and `C-147` has weakened the case for
spending three days on naming.

**⚠ This is a decision the owner should make before Task 1**, because it sets the diff size and
Tasks 1–2 differ between the two shapes.

---

## 2. Tasks

### Task 1 — Split the type

Delete `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs`. Add two files under
`src/Radio.Infrastructure/Audio/State/`.

**`MasterOutputState.cs`:**

```csharp
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.State;

/// <summary>
/// Holds the master output's volume, balance and mute state, and raises change events.
///
/// ⚠ This type moves no audio. It is a state holder that <see cref="SoundFlowAudioEngine"/> reads
/// and applies to SoundFlow's real mixer (<c>playbackDevice.MasterMixer</c>), and that
/// <see cref="BalanceModifier"/> reads per sample. It was called <c>SoundFlowMasterMixer</c> until
/// AUD-4; it was never SoundFlow's and it was never a mixer.
/// </summary>
public class MasterOutputState : IMasterOutputState
{
  private readonly ILogger<MasterOutputState> _logger;

  private float _masterVolume = 0.75f;
  private float _balance;
  private bool _isMuted;

  /// <summary>Event fired when master volume changes.</summary>
  public event EventHandler<float>? MasterVolumeChanged;

  /// <summary>Event fired when balance changes.</summary>
  public event EventHandler<float>? BalanceChanged;

  /// <summary>Event fired when mute state changes.</summary>
  public event EventHandler<bool>? MuteStateChanged;

  /// <summary>Initializes a new instance of the <see cref="MasterOutputState"/> class.</summary>
  /// <param name="logger">The logger instance.</param>
  public MasterOutputState(ILogger<MasterOutputState> logger)
  {
    _logger = logger;
  }

  /// <inheritdoc/>
  public float MasterVolume
  {
    get => _masterVolume;
    set
    {
      var clampedValue = Math.Clamp(value, 0f, 1f);
      if (Math.Abs(_masterVolume - clampedValue) > float.Epsilon)
      {
        _masterVolume = clampedValue;
        _logger.LogDebug("Master volume set to {Volume:P0}", clampedValue);
        MasterVolumeChanged?.Invoke(this, _masterVolume);
      }
    }
  }

  /// <inheritdoc/>
  public float Balance
  {
    get => _balance;
    set
    {
      var clampedValue = Math.Clamp(value, -1f, 1f);
      if (Math.Abs(_balance - clampedValue) > float.Epsilon)
      {
        _balance = clampedValue;
        _logger.LogDebug("Balance set to {Balance:F2}", clampedValue);
        BalanceChanged?.Invoke(this, _balance);
      }
    }
  }

  /// <inheritdoc/>
  public bool IsMuted
  {
    get => _isMuted;
    set
    {
      if (_isMuted != value)
      {
        _isMuted = value;
        _logger.LogDebug("Mute state set to {IsMuted}", value);
        MuteStateChanged?.Invoke(this, _isMuted);
      }
    }
  }

  /// <summary>Gets the effective volume after applying mute state.</summary>
  /// <returns>The effective volume (0 if muted).</returns>
  public float GetEffectiveVolume() => _isMuted ? 0f : _masterVolume;

  /// <summary>Calculates the left channel gain based on balance.</summary>
  /// <returns>The left channel gain (0.0 to 1.0).</returns>
  public float GetLeftChannelGain()
  {
    // When balance is positive (right), reduce left channel
    return _balance > 0 ? 1f - _balance : 1f;
  }

  /// <summary>Calculates the right channel gain based on balance.</summary>
  /// <returns>The right channel gain (0.0 to 1.0).</returns>
  public float GetRightChannelGain()
  {
    // When balance is negative (left), reduce right channel
    return _balance < 0 ? 1f + _balance : 1f;
  }
}
```

**`AudioSourceRegistry.cs`** — note every log string now says *registry*, and the class remarks
carry the reason (Task 5 depends on this wording):

```csharp
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.State;

/// <summary>
/// The roster of audio sources the console currently considers active. Bookkeeping only.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>NOTHING IN THIS CLASS ROUTES, ATTACHES OR DETACHES AUDIO.</b> It mutates a
/// <c>List&lt;IAudioSource&gt;</c> by object reference. The real audio graph is SoundFlow's
/// <c>playbackDevice.MasterMixer</c>, and the only code that attaches or detaches a component is
/// <c>SoundFlowPlaybackService</c> (<c>PlayAsync</c>/<c>StopAsync</c> → <c>AddComponent</c>/
/// <c>RemoveComponent</c>), reached from a source's own <c>StopCoreAsync</c>.
///
/// This class was called <c>SoundFlowMasterMixer</c> until AUD-4, and its removal log line read
/// "Removed audio source … from mixer" — which reads as an audio detach and never was one. Every
/// log line here now says <i>registry</i> for that reason. Do not reintroduce the word "mixer".
///
/// ⚠ Since AUD-4 this roster is maintained by <see cref="AttachLifecycle"/>: a source is evicted
/// when it reports Stopped or Disposed. <see cref="RemoveSource"/> remains public for the one
/// caller that must undo an <see cref="AddSource"/> for a source that never started
/// (<c>SourcesController.PlayFileEvent</c>'s failure path).
/// </remarks>
public class AudioSourceRegistry : IAudioSourceRegistry
{
  private readonly ILogger<AudioSourceRegistry> _logger;
  private readonly List<IAudioSource> _sources = [];
  private readonly object _sourcesLock = new();

  /// <summary>Initializes a new instance of the <see cref="AudioSourceRegistry"/> class.</summary>
  /// <param name="logger">The logger instance.</param>
  public AudioSourceRegistry(ILogger<AudioSourceRegistry> logger)
  {
    _logger = logger;
  }

  /// <inheritdoc/>
  public void AddSource(IAudioSource source)
  {
    ArgumentNullException.ThrowIfNull(source);

    lock (_sourcesLock)
    {
      if (!_sources.Contains(source))
      {
        _sources.Add(source);
        AttachLifecycle(source);
        // Type, not Name: this is domain-agnostic bookkeeping, and TTSEventSource.Name embeds
        // the utterance text (TTS-11). Type is redundant with Id's prefix — AudioSourceBase builds
        // Id as $"{Type}-{Guid:N}" — and is kept for readability, not information.
        _logger.LogInformation(
          "Registered audio source {SourceId} ({SourceType})",
          source.Id, source.Type);
      }
    }
  }

  /// <inheritdoc/>
  public void RemoveSource(IAudioSource source)
  {
    ArgumentNullException.ThrowIfNull(source);

    lock (_sourcesLock)
    {
      RemoveLocked(source);
    }
  }

  /// <inheritdoc/>
  public IReadOnlyList<IAudioSource> GetActiveSources()
  {
    lock (_sourcesLock)
    {
      return _sources.ToList().AsReadOnly();
    }
  }

  /// <summary>Clears the roster. Does NOT stop audio — see the class remarks.</summary>
  public void ClearSources()
  {
    lock (_sourcesLock)
    {
      foreach (var source in _sources)
      {
        source.StateChanged -= OnSourceStateChanged;
      }
      _sources.Clear();
      _logger.LogInformation("Cleared the source registry (no audio was detached)");
    }
  }

  /// <summary>
  /// Subscribes to the source's state so the roster evicts it when it stops. Called under
  /// <c>_sourcesLock</c> from <see cref="AddSource"/>.
  /// </summary>
  private void AttachLifecycle(IAudioSource source)
  {
    // Idempotent: -= on a handler that is not subscribed is a no-op, so a re-add cannot
    // double-subscribe and cause a double eviction.
    source.StateChanged -= OnSourceStateChanged;
    source.StateChanged += OnSourceStateChanged;
  }

  private void OnSourceStateChanged(object? sender, AudioSourceStateChangedEventArgs e)
  {
    if (e.NewState is not (AudioSourceState.Stopped or AudioSourceState.Disposed))
    {
      return;
    }

    if (sender is not IAudioSource source)
    {
      return;
    }

    lock (_sourcesLock)
    {
      RemoveLocked(source);
    }
  }

  /// <summary>Removes and unsubscribes. Caller must hold <c>_sourcesLock</c>.</summary>
  private void RemoveLocked(IAudioSource source)
  {
    if (_sources.Remove(source))
    {
      source.StateChanged -= OnSourceStateChanged;
      _logger.LogInformation(
        "Deregistered audio source {SourceId} ({SourceType}) — bookkeeping only, no audio detached",
        source.Id, source.Type);
    }
  }
}
```

**⚠ Re-entrancy note for the reviewer.** `OnSourceStateChanged` takes `_sourcesLock`, and it is
raised from `AudioSourceBase.State`'s setter (`:51-52`), which runs on whatever thread called
`StopAsync`. `AddSource` calls `AttachLifecycle` while already holding the lock, but that only
subscribes — it raises nothing. No path raises `StateChanged` from inside the lock, so the lock is
never re-entered. **The reviewer must confirm this still holds after Task 3**, because it is exactly
the kind of precondition `CLAUDE.md` § *Pre-Merge Review* asks to be re-checked in-diff.

### Task 2 — Split the interface

In `src/Radio.Core/Interfaces/Audio/IAudioEngine.cs`, replace `IMasterMixer` (`:202-239`):

```csharp
/// <summary>
/// The master output's volume, balance and mute state.
/// </summary>
/// <remarks>
/// ⚠ Not a mixer. This is state the audio engine reads and applies to SoundFlow's real mixer.
/// Renamed from <c>IMasterMixer</c> by AUD-4.
/// </remarks>
public interface IMasterOutputState
{
  /// <summary>Gets or sets the master volume level (0.0 to 1.0).</summary>
  float MasterVolume { get; set; }

  /// <summary>Gets or sets the stereo balance (-1.0 left to 1.0 right).</summary>
  float Balance { get; set; }

  /// <summary>Gets or sets whether the master output is muted.</summary>
  bool IsMuted { get; set; }
}

/// <summary>
/// The roster of audio sources the console currently considers active.
/// </summary>
/// <remarks>
/// ⚠⚠ Bookkeeping only — no member of this interface attaches or detaches audio. Adding a source
/// here does not make it audible and removing it does not silence it. Audio is attached and
/// detached by <c>SoundFlowPlaybackService</c>, reached from a source's own <c>StopCoreAsync</c>.
/// Split out of <c>IMasterMixer</c> by AUD-4, whose row exists because the old name and its log
/// lines claimed otherwise.
/// </remarks>
public interface IAudioSourceRegistry
{
  /// <summary>Adds a source to the roster. Does not route audio.</summary>
  /// <param name="source">The source to register.</param>
  void AddSource(IAudioSource source);

  /// <summary>
  /// Removes a source from the roster. Does not detach audio.
  /// </summary>
  /// <remarks>
  /// Since AUD-4 the roster evicts a source automatically when it reports Stopped or Disposed, so
  /// most callers need not call this. It remains for the one case that has no stop to hang off:
  /// undoing an <see cref="AddSource"/> for a source whose playback never started.
  /// </remarks>
  /// <param name="source">The source to deregister.</param>
  void RemoveSource(IAudioSource source);

  /// <summary>Gets all currently registered audio sources.</summary>
  /// <returns>A read-only list of registered sources.</returns>
  IReadOnlyList<IAudioSource> GetActiveSources();
}
```

And at `:34`, replace `IMasterMixer GetMasterMixer();` with the two accessors:

```csharp
  /// <summary>Gets the master output state (volume, balance, mute).</summary>
  /// <returns>The master output state.</returns>
  IMasterOutputState GetMasterOutputState();

  /// <summary>Gets the registry of currently active audio sources.</summary>
  /// <returns>The source registry.</returns>
  IAudioSourceRegistry GetSourceRegistry();
```

**DI** — `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`, at **both**
`:73-75` and `:279-281` (the registration block is duplicated; both must change):

```csharp
    // Master output state and the source registry (singletons to maintain state).
    // Two types since AUD-4: they share no state, and no single honest name covers both.
    services.AddSingleton<MasterOutputState>();
    services.AddSingleton<IMasterOutputState>(sp => sp.GetRequiredService<MasterOutputState>());
    services.AddSingleton<AudioSourceRegistry>();
    services.AddSingleton<IAudioSourceRegistry>(sp => sp.GetRequiredService<AudioSourceRegistry>());
```

`SoundFlowAudioEngine`'s explicit factory (`:83-92`) takes `masterMixer`; it now needs both. Update
the constructor and the factory together — `BalanceModifier` (`:11`, `:17`) takes only
`MasterOutputState`.

### Task 3 — Roster eviction becomes a consequence of stopping

The eviction handler ships in Task 1. This task removes the hand-maintained call that the row's
scope (a) is about.

`src/Radio.Infrastructure/Audio/Services/AudioManager.cs:206-223` becomes:

```csharp
        try
        {
          if (oldSource is IPrimaryAudioSource oldPrimary)
          {
            // This is the audio detach: StopAsync → StopCoreAsync → SoundFlowPlaybackService
            // .StopAsync(key) → MasterMixer.RemoveComponent. The source registry evicts oldSource
            // on its own when the state reaches Stopped, so there is no second call to make here.
            //
            // ⚠ Do NOT re-add a registry removal "to be safe". The pair of calls that used to live
            // here is what AUD-4 removed: one of them logged a successful detach it had not
            // performed, and a reader could not tell which call was load-bearing.
            await oldPrimary.StopAsync(cancellationToken);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Error stopping old source {SourceName} during switch", oldSource.Name);
        }
```

The `if (mixer.GetActiveSources().Contains(oldSource))` block (`:213-218`) — its false comment
(`C-157`), its `RemoveSource` call and its misleading log — is deleted entirely.

**⚠ `oldSource` may be a non-primary source**, in which case no `StopAsync` runs and nothing evicts
it. Verify against `AudioManager`'s invariant that `_activeSource` is always primary
(`SwitchSourceAsync` throws on a non-primary `source` at `:178-181`, so `_activeSource` can only
ever have been primary). Record the finding in the PR body either way.

### Task 4 — Wire the dead sweep into engine stop (`C-151`)

`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs`, in `StopAsync` at `:698-700`:

```csharp
      // Detach every attached player/component from SoundFlow's real mixer FIRST. Until AUD-4 this
      // method cleared the roster and left the audio attached: GetActiveSources() reported nothing
      // playing while every component kept producing samples (C-151). StopAll() existed for exactly
      // this and had no callers anywhere in the tree.
      _playbackService.StopAll();

      // Then the roster. Sources evicted by StopAll's state transitions are already gone; this
      // clears anything registered that never started.
      _sourceRegistry.ClearSources();
```

**⚠ This needs `SoundFlowAudioEngine` to reach `SoundFlowPlaybackService`, and today it does not —
the dependency runs the other way** (`SoundFlowPlaybackService` takes `IAudioEngine`). A direct
constructor injection would be a cycle. **Builder must resolve this before writing the call**, and
the two acceptable shapes are:

1. **Lazy resolution** — inject `IServiceProvider` (or `Lazy<SoundFlowPlaybackService>`) into the
   engine and resolve at stop time. Smallest diff; adds a service-locator, which this tree
   otherwise avoids.
2. **Invert it** — have `SoundFlowPlaybackService` subscribe to an engine `Stopping` event and call
   its own `StopAll`. Keeps the dependency direction, costs a new event.

**Recommend (2), and it is not a new pattern — it is the one already in use for this exact pair.**
`SoundFlowAudioEngine` raises `PlaybackDeviceSwitched`, and `SoundFlowPlaybackService` subscribes to
it and re-attaches its components in response (unsubscribed in `Dispose` at `:887`; the engine's own
comment at `SoundFlowAudioEngine.cs:915` describes it as *"Notify services (e.g.
SoundFlowPlaybackService) to re-attach active …"*). A `Stopping` event is the same seam pointed the
same way, and `SoundFlowAudioEngine` holds **no reference to the playback service at all** today —
verified: the only match for `PlaybackService` in that file is that comment.

**This is still a design decision inside a task and the owner should see it** — it is the single
riskiest edit in the row, because it changes what happens on every engine stop on a live audio path.

### Task 5 — Every log line and comment in the blast radius says what it does

Task 1 covers the registry's four lines. The rest:

| File:line | Current | Replace with |
|---|---|---|
| `AudioManager.cs:213` | `// Remove from mixer so its audio components are disconnected` | deleted with the block (Task 3) |
| `AudioManager.cs:217` | `"Removed old source {SourceName} from mixer"` | deleted with the block (Task 3) |
| `AudioManager.cs:230` | `"Adding new source {SourceName} to mixer"` | `"Registering new source {SourceName}"` |
| `SoundFlowPlaybackService.cs:522` | `"🔇 AUDIO ROUTING: Removing player from SoundFlow mixer …"` | **keep** — true; this one really does detach |
| `SoundFlowPlaybackService.cs:545` | `"… Removing component '{ComponentName}' from SoundFlow mixer …"` | **keep** — true |
| `EventPlaybackService.cs:14-16` | comment describing `IMasterMixer.AddSource` | retarget to `IAudioSourceRegistry`, keep the (correct) claim that it never calls it |
| `BalanceModifier.cs:7` | `Reads balance gain values from the SoundFlowMasterMixer.` | `Reads balance gain values from MasterOutputState.` |

**`SoundFlowPlaybackService`'s lines are the control group and must not be swept.** They are the two
log statements in this subsystem that *do* describe an audio detach, and a fixer running a
find-and-replace over the word "mixer" would break the only honest pair in the tree.

### Task 6 — Tests, and the lint that would have gone quiet (`C-152`)

**6a. `LogSafetyLintTests.cs:157`** — update the filename, and add the aliveness assertion the rule
family has never had:

```csharp
    Of(@"\bsource\s*\??\s*\.\s*Name\b", "source.Name", "AudioSourceRegistry.cs"),
```

And inside `NoLogCallInTheSolutionPassesAKnownUserTextArgument`, after the existing floors at
`:233-236`:

```csharp
    // ⚠ A per-file rule whose OnlyInFile names a file that no longer exists applies to NOTHING and
    // passes green forever. That is not hypothetical: AUD-4 renamed SoundFlowMasterMixer.cs, and
    // without this assertion the source.Name rule would have silently stopped covering anything.
    // Five rules are scoped this way; this proves every one of them still has a file to scan.
    var scannedNames = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
    foreach (var scoped in Forbidden.Where(r => r.OnlyInFile is not null))
    {
      Assert.True(
        scannedNames.Contains(scoped.OnlyInFile!),
        $"Rule [{scoped.Shape}] is scoped to '{scoped.OnlyInFile}', which is not in the scanned " +
        "tree. The file was renamed or deleted and the rule now matches nothing. Re-point it or " +
        "remove it — do not leave it green and dead.");
    }
```

**6b. Rename the two test files** (§0.9) and their classes:
`SoundFlowMasterMixerTests.cs` → `AudioSourceRegistryTests.cs` + `MasterOutputStateTests.cs`;
`SoundFlowMasterMixerLogSafetyTests.cs` → `AudioSourceRegistryLogSafetyTests.cs`. Update the
cross-references in `LogSafetyLintTests.cs:31` and `:45`.

**6c. New tests pinning the unification.** These are the behaviour this row changes and the only
part that is not mechanically safe:

- `RegistryEvictsASourceThatReportsStopped` — add a fake source, drive `State = Stopped`, assert
  `GetActiveSources()` is empty.
- `RegistryEvictsASourceThatReportsDisposed` — same for `Disposed`.
- `RegistryDoesNotEvictOnPlayingOrPaused` — the negative case; the handler's early return.
- `ReAddingASourceDoesNotDoubleSubscribe` — add, stop, add, stop; assert one eviction log per stop
  and no exception. Pins `AttachLifecycle`'s idempotence.
- `RemoveSourceStillWorksForASourceThatNeverStarted` — `C-156`'s caller; add then remove with no
  state transition.
- `SwitchSourceAsyncEvictsTheOldSourceWithoutCallingRemoveSource` — drive `AudioManager` with a real
  registry and a fake primary source; assert the old source leaves the roster and that the fake's
  `StopCoreAsync` ran. **This is the test that would have failed under `03a6fea`+`PrimaryAudioSourceBase`'s
  guard**, and it is the closest this row gets to pinning the original defect.
- `EngineStopDetachesBeforeItClearsTheRegistry` — `C-151`. Assert `StopAll` (or the `Stopping` event
  from Task 4's option 2) fires and that it precedes `ClearSources`.

**⚠ `CLAUDE.md` § *Test Timing* applies to none of these and that is deliberate.** Every assertion
above is driven by an explicit state transition or an explicit method call — there is no timer, no
`Task.Delay`, and nothing that waits on production code's own clock. **If a Builder finds itself
adding a sleep to make one of these pass, the test is wrong**, and the house idiom (injectable
`TimeProvider`, `FakeTimeProvider`) is the fix. `AudioSourceBaseStopTests.cs` (from #468) is the
in-tree model: it drives state explicitly and asserts synchronously.

### Task 7 — Docs

- **`CLAUDE.md:442`** — correct example #1 of the *Pre-Merge Review* list per `C-146`/`C-147`. The
  example is still valid and still worth keeping: the log line **did** assert more than the code
  did. What must change is the causal tail — `03a6fea` did not "trust the wording" and did not land
  "one layer too high"; it hit a duplicated state guard one frame down, which #468 removed. Proposed
  replacement, preserving the lesson and dropping the false mechanism:

  > 1. `SoundFlowMasterMixer` (now `AudioSourceRegistry`, AUD-4) logged *"Removed audio source … from
  >    mixer"* while only mutating a `List<IAudioSource>` — the real detach lives elsewhere. The line
  >    was not what caused `03a6fea` to fail (that was a state guard duplicated in
  >    `PrimaryAudioSourceBase.StopAsync`, removed by #468), but it is what made the failure invisible
  >    for 166 days: after `03a6fea` the logs read like a successful teardown while audio kept
  >    playing.

- **`design/AUDIO-PIPELINE-REVIEW.md:173`** — mark the rename recommendation done, and note the split
  went further than the `MasterAudioState` it proposed, with `C-154`'s reason.
- **`design/AUDIO-DATAFLOW.md`** (`:91`, `:291`) and **`design/FUTURE-WORK.md`** (`:392`, `:420`,
  `:1473`) — retarget identifiers.
- **`design/DECISION-LOG.md`** — new ADR: the type was two things, the halves shared no state, the
  split is why; and record `C-147` so the next reader does not re-derive the rename's justification
  from the queue row's stronger claim.
- ⛔ **Merged plans under `design/plans/` are not edited.** §0.10.

---

## 3. Ordering

Task 1 → 2 (the tree does not build in between; one commit) → 6a **before any file rename lands**
→ 6b → 3 → 4 → 5 → 6c → 7.

**6a leads the test work deliberately.** It is the one edit that fails silently if forgotten, so it
goes in while the reason is on screen — not at the end with the rest of the test churn.

---

## 4. Test plan

### 4.1 Gates

```bash
dotnet build --configuration Release            # 0 warnings; Release treats warnings as errors
dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

**Never pipe `dotnet test` into `tail`** — `CLAUDE.md` § *Build & Test Commands*. Read the
per-project summary lines, one per test project.

**Known-failing on Windows, not regressions:** four `SrcVariableResamplerTests`
(`libsamplerate.so.0`, `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network,
`Category=Integration`, CI-excluded).

### 4.2 What pins the behaviour change

The rename is mechanically safe — the compiler is the gate, and a missed reference is a build error,
not a silent bug. **Three things are not mechanically safe and carry the risk:**

| Change | Pinned by |
|---|---|
| Roster now evicts on state (Task 3) | 6c's four registry tests + `SwitchSourceAsync…` |
| `AudioManager` no longer removes by hand (Task 3) | `SwitchSourceAsyncEvictsTheOldSource…` |
| Engine stop now detaches audio (Task 4) | `EngineStopDetachesBeforeItClearsTheRegistry` |
| The lint stays alive across the rename (6a) | the `OnlyInFile` aliveness assertion — and it is self-proving: delete the filename update and this assertion fails |

### 4.3 UAT

**Required, because Task 4 changes what happens on every engine stop on a live audio path.** Not
on the box during this row's development — run it as the Builder's normal pre-merge UAT:

1. Play a primary source; switch to another. **Assert only one is audible** — this is `03a6fea`'s
   original symptom and Task 3's blast radius.
2. Same switch, but with the old source's state driven to `Stopped` externally first (the AVRCP
   case `03a6fea` was written for).
3. Stop the audio engine with a source playing. **Assert audio actually stops** — `C-151`. This is
   new behaviour; it did not stop before.
4. Trigger a TTS event, let it finish, and check `GET /api/sources/active` no longer lists it
   (`C-160`).
5. `POST` a file event with a bad path so `PlayFileAsync` fails; assert a 500 and that the roster is
   clean (`C-156`).

---

## 5. Deliberately not done

- **`SoundFlowPlaybackService.Dispose`'s missing `RemoveComponent`** (`C-159`). A third removal
  shape, defensible at dispose because the device is being torn down. Fixing it here would widen an
  already-large diff on the audio path for no observed symptom. **Worth its own row.**
- **The `AUD-2` key-identity work.** Genuinely a different bug (`C-148`), and this row neither needs
  nor blocks it.
- **`docs/ROADMAP.md` and `docs/HANDOFF-GA-PUNCH-LIST.md`'s copies of the false `03a6fea` story**
  (`C-146`). This plan corrects `CLAUDE.md` because that file is loaded into every session. The
  other two are the owner's call and are listed in §6 so they are not lost.
- **Reconciling the collided `C-nn` sequence** (`C-161`).

---

## 6. Queue row wording

**⚠ Planner did not edit `docs/BUILDER_QUEUE.md`.** The following is proposed wording for the owner
to apply.

**Replace the row's dependency cell** — currently *"no hard row dependency. **Prefer AUD-2 first**…"*:

> — _(**no dependency. ⚠ The "prefer `AUD-2` first" note that stood here was FALSIFIED 2026-09-06
> and is removed, not softened.** `BUILDER_QUEUE.md:437`'s claim that `AUD-2` and `AUD-4` are "two
> symptoms of ONE root cause" and that `AUD-2` "decides the key" is **false**: per-source teardown is
> key-symmetric — `SDRRadioAudioSource.cs:915` mints `_playbackId` and `:1027`/`:1059` stop with the
> same field — and this row's roster is keyed by **object reference**, not by string, so there is no
> key here to decide. The row's prescribed "sweep `_activeComponents` instead" is unnecessary and
> also points at dead code: `SoundFlowPlaybackService.StopAll()` has **zero callers in the tree**.
> Plan: `design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md` §0.4, `C-148`, `C-150`.
> **Still rebase past #468.**)_

**Amend `:437`** so the false claim is not left standing for the next reader:

> - ~~**`AUD-2` and `AUD-4` are two symptoms of ONE root cause — claim `AUD-2` first.**~~ **❌
>   FALSIFIED 2026-09-06 while planning `AUD-4`.** They are unrelated bugs. `AUD-2` is a
>   key-identity defect in a *third party* (`AudioManager` addressing a source by `Id` when the
>   source registered under a key it minted). `AUD-4` involves no string key at all — it is a
>   `List<IAudioSource>` mutated by object reference. Either may be claimed first.

**Amend `:440`** — the "both exist because a log line asserted a success it never verified" note is
**half right and must be re-pointed**:

> - **⚠ `AUD-4` exists because a log line asserted a success it never verified — but ⚠ that log line
>   did NOT cause `03a6fea`.** Corrected 2026-09-06: `03a6fea` widened the *state guard* around the
>   whole stop block; it failed because the identical predicate was duplicated in
>   `PrimaryAudioSourceBase.StopAsync` (and triplicated in `EventAudioSourceBase`), which #468
>   removed 166 days later. The misleading log is what kept it **undetected**, not what made it
>   wrong. Do not repeat "trusted the wording, landed one layer too high" — `CLAUDE.md:442` carries
>   it too and the plan's Task 7 corrects it.

**Amend the row's scope and status cell:**

> **Scope, revised by the plan:** (a) the unification now has a **named live defect** under it —
> `SoundFlowAudioEngine.StopAsync:700` clears the roster and never stops the audio, while the sweep
> written for exactly that (`StopAll`) has no callers (`C-151`, `C-150`); (b) the rename is
> **recommended as a split into `AudioSourceRegistry` + `MasterOutputState`**, because the two halves
> of the type share no state and no single honest name covers both (`C-154`) — and the class has no
> SoundFlow dependency at all, so the prefix goes with the suffix (`C-153`); (c) log/comment honesty
> as filed. **⚠ The row's claim that "the rename is the durable half — it is what stops the bug
> recurring" did not survive planning (`C-147`): the rename would not have prevented `03a6fea`, and
> the actual recurrence-preventer shipped in #468. The rename is justified as log honesty and
> detection latency. Re-price the row if that was what bought its priority.**
> _plan: `design/plans/AUD-4-unify-source-removal-and-rename-the-mixer.md` · **2 d** minimal / **3 d**
> split · **not auto-mergeable — Task 4 changes engine-stop behaviour on the live audio path**_

---

## 7. Self-review

### 7.1 Verified first-hand at `35e4ed5a`

- `SoundFlowMasterMixer.cs` read in full: no SoundFlow import (`:1-2`), `_sources` at `:13`,
  `RemoveSource` at `:128-141`, the log at `:136-138` saying `{SourceType}` not `{SourceName}`.
- `03a6fea`'s diff read at `-U25`; the hunk does not contain `RemoveSource`.
- `PrimaryAudioSourceBase.cs:169-179` at `03a6fea` read directly — the duplicated guard, quoted
  verbatim in §0.2.
- `BluetoothAudioSource.cs` at `03a6fea`: `StopCoreAsync` → `_playbackService.StopAsync(_playbackId, ct)`.
- `AudioSourceBase.cs:118-128` at HEAD — #468's `Created or Disposed` guard and its doc comment.
- `SoundFlowPlaybackService.cs:494-561` read in full; `:526`/`:548` `RemoveComponent` confirmed.
- `grep -rn 'StopAll' src tests tools --include=*.cs` — one hit, the definition. Zero callers.
- `SoundFlowAudioEngine.cs:684-706` — `ClearSources()` with no `StopAll`.
- `LogSafetyLintTests.cs:131-182` and `:203-247` — `OnlyInFile`, its `Ordinal` match, and the absence
  of an aliveness assertion.
- `IAudioEngine.cs:202-239` — the interface, both concerns.
- `SourcesController.cs:712-739` — the `RemoveSource` failure path (`C-156`).
- `IAudioSource.cs:48` — `StateChanged` exists on the interface, which is what Task 3 hangs on.
- `SoundFlowAudioEngine.cs` holds no reference to `SoundFlowPlaybackService` (only the comment at
  `:915`), and the reverse subscription `PlaybackDeviceSwitched` exists
  (`SoundFlowPlaybackService.cs:887`) — the precedent Task 4's option (2) reuses.
- `AudioManager.cs:178-181` — the non-primary throw underwriting Task 3's invariant.
- Blast-radius counts re-derived by hand (`C-158`).

### 7.2 Not verified, and what it costs

- **Task 4's dependency-cycle resolution is specified as a choice, not a decision.** The engine
  cannot reach the playback service today; this plan recommends option (2) and shows that the same
  seam already exists for the same pair (`PlaybackDeviceSwitched`), but **it does not write that
  event**. A Builder still has to add it and decide where in `StopAsync` it is raised relative to the
  `State = Stopping` assignment. Called out rather than papered over because guessing wrong here
  changes an audio-path lifecycle.
- **The non-primary `oldSource` case in Task 3** is argued from `SwitchSourceAsync`'s throw at
  `:191-194` but not proved across every assignment to `_activeSource`.
- **No behaviour was observed on the box.** Every claim here is from the tree and from git history.
  `C-151` in particular is derived by reading, not by hearing audio continue after an engine stop —
  UAT step 3 is what would confirm it.

### 7.3 What would falsify this plan

- If some caller reaches `SoundFlowAudioEngine.StopAsync` only after every source has already been
  stopped individually, `C-151` is latent rather than live and Task 4 drops to hygiene. The engine's
  `StopAsync` has no such precondition documented, but this was not traced from every caller.
- If `IAudioSource.StateChanged` is not raised reliably by some source that bypasses
  `AudioSourceBase.State`'s setter, Task 3's eviction leaks roster entries for that source. Every
  source in the tree derives from `AudioSourceBase`; a future one might not, which is why
  `RemoveSource` stays public.
