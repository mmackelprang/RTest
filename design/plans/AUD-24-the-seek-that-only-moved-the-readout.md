# `AUD-24` — the seek that only moved the readout

**Row:** [`docs/queue/AUD-24.md`](../../docs/queue/AUD-24.md) · 🟠 P1
**UAT that found it:** [`docs/uat/2026-09-09-phn2-sound-uat/RESULT.md`](../../docs/uat/2026-09-09-phn2-sound-uat/RESULT.md) check #1 / §3 U5
**Branch:** `fix/aud-24-seek-does-not-reposition`
**Planned:** 2026-09-10, against `main` at `c1b9972c`

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`FilePlayerAudioSource.SeekCoreAsync` range-checks its argument, assigns `_position = position`, logs
*"Seeked to {Position}"* and returns. **It never calls the audio engine.** `Position` reads back that
same field, so `/api/audio` and the panel both report the new position while the audio keeps playing
from where it was. The fix is to call `SoundFlowPlaybackService.Seek` — which exists, works, is used
correctly by the sibling voicemail source, and is called by nothing on this path — and to stop moving
the readout when the player refuses.

---

### 0.2 ⛔ FIVE OF THE ROW'S PREMISES DO NOT HOLD. Read this section before you read the row.

The row was filed as a fresh unknown with three open scope questions. **Four in-tree documents already
contained the diagnosis, and one of them contains the fix.** The corrections, in descending order of
how much work they save:

**① The defect is already fully diagnosed in `design/FUTURE-WORK.md` § 14a, dated 2026-09-02 — a week
before the owner observed it.** That entry names the file, the method, the mechanism (*"**It assigns a
field.** No audio is repositioned"*), the fix (*"`SoundFlowPlaybackService.Seek(sourceId, position)` …
called from `SeekCoreAsync`"*), the blast radius (the persisted resume position), the `bool`
propagation rule, and **its own UAT requirement**. Nothing in this plan is a discovery; it is that
entry, executed. ⭐ **The row and the FUTURE-WORK entry were never connected to each other.** Two more
documents also already knew: `design/AUDIO-PIPELINE-REVIEW.md` finding #3 (*"File-player seek is
display-only and position is a wall-clock estimate"*) and `design/DECISION-LOG.md` ADR-029
Amendments Decision 2, which names `FilePlayerAudioSource` as *"out of scope, and logged with its own
UAT requirement in `design/FUTURE-WORK.md` §14a"*.

> ⭐⭐ **THE TRANSFERABLE PART, STATED PLAINLY, BECAUSE IT IS THE MOST REUSABLE THING IN THIS ROW.**
> **This is not a claim that was wrong. It is a correct answer that sat unread for a week while a
> person found the same defect by ear.** Three documents held the diagnosis — one of them held the
> *fix* — and the row was opened with three scope questions they had already answered. The cost was
> not a bad decision; it was a fifteen-minute cabinet sitting and a P1 row that did not need to exist
> as an unknown.
>
> ⛔ **The failure mode is not laziness, it is that nothing pointed.** `FUTURE-WORK` §14a cites
> `PHN-1a`; nothing cites §14a. The `AUD` prefix collision recorded in `BUILDER_QUEUE`'s ID-namespace
> section diagnosed the identical mechanism and named the remedy: *"the collision was **the absence of
> a cross-citation**, not bad luck — so a new row that cites its counterpart section is a row that
> cannot silently diverge."* **That remedy was written for IDs and applies unchanged to defects.**
>
> **So: before filing a defect row, grep `design/FUTURE-WORK.md` and `design/AUDIO-PIPELINE-REVIEW.md`
> for the symbol.** Two greps. Had either been run on `SeekCoreAsync` or on the word *seek*, this row
> would have opened with the mechanism in hand.

**② `CLAUDE.md` does NOT record the "throw rather than no-op / has been misled" reasoning.** The row,
the queue row and the dispatch all attribute it there. The word *seek* does not appear in `CLAUDE.md`
at all. The quote lives in `design/DECISION-LOG.md`, it is about **`MaxSpeechChars` truncation**, and
it cites the seek throw only as precedent. ⚠ Fix the attribution wherever it is repeated.

**③ The throw is real, and it is irrelevant to this defect — so scope question 2 answers "neither".**
`PrimaryAudioSourceBase.SeekAsync` does throw `NotSupportedException` when `!IsSeekable`. But
`FilePlayerAudioSource.IsSeekable` is `true`, so nothing throws; and `SeekCoreAsync` returns
`Task.CompletedTask` on every path, so nothing returns false either. **`Seek` silently succeeds.**
There is no swallow to find. (Separately confirmed: every production caller pre-checks `IsSeekable`
first — `AudioController`, `EventPlaybackService`, `EventPlaybackApiService` — so that throw is
unreachable from every route in the app.)

**④ ADR-029 § 8.3 asserts the opposite of the truth and should be annotated.** It says
`FilePlayerAudioSource` *"already implements seeking over a local file through
`SoundFlowPlaybackService`"*. Both halves are false: the seek does not work, and
`SoundFlowPlaybackService` had no seek method at all until `PHN-1a` added one. `FUTURE-WORK` § 14a
already says so; the ADR still does not.

**⑤ ⚠ THE SPECIFIED CHECK AND THE OBSERVED DEFECT ARE ON DIFFERENT SURFACES, AND THE ROW CONFLATES
THEM.** This is the correction that changes what gets tested.

- **`PHN-2` § 3 U5 — the check that was written — is about a VOICEMAIL:** *"Start a voicemail at least
  20 seconds long. **Tap** the progress bar about three-quarters of the way along."* Its chain is
  `VoicemailPlayer.razor` → `EventPlaybackApiService` → `EventPlaybackController` →
  `EventPlaybackService` → `AudioFileEventSource.SeekCoreAsync`. **That path is implemented
  correctly** — it calls `_playbackService!.Seek(_playbackId!, position)`, checks the returned `bool`,
  warns on a refusal, and re-arms the completion wait.
- **The owner's report is about the music file player:** *"For **Mp3 files**, **dragging** the position
  bar."* Its chain is `NowPlayingPanel.razor` (on `Home.razor`) → `AudioController` →
  `FilePlayerAudioSource.SeekCoreAsync`. **That is the broken one.**
- ⭐ **The gesture corroborates the surface.** `VoicemailPlayer` is **tap-to-seek only** and says so in
  its own comment (*"There is no pointermove handler and never was"*); `NowPlayingPanel` renders a
  draggable `RadzenSlider` whose thumb follows the finger. A **drag** that *"moves the marker"* is the
  slider. ⛔ **This is corroboration, not proof** — the owner may have used the word loosely.

⛔ **The consequence: `PHN-2` check #1 as written may still be UNRUN.** Do not let this row's PR close
it by implication. The UAT in § 3 therefore runs **both** surfaces and records them separately.

---

### 0.3 ⭐ The layer, established — and the evidence, not the adverb

The dispatch and the row both warn that *"moves the marker, but not the music **at all**"* narrows the
symptom without establishing the layer, because a `Seek` returning early on a guard looks identical
from the room. Agreed. The layer below is established by reading every link in the chain, and the
adverb is used for nothing.

| Layer | Symbol | Verdict |
|---|---|---|
| UI markup | `NowPlayingPanel` `RadzenSlider`, rendered when `_canSeek` | ✅ fires `HandleSeekAsync` |
| UI handler | `NowPlayingPanel.HandleSeekAsync` | ✅ converts percent → `TimeSpan`, POSTs `Action: "Seek"`, then re-reads server state |
| API contract | `UpdatePlaybackRequest.SeekPosition`, `PlaybackAction.Seek` | ✅ present |
| Controller | `AudioController`, `case PlaybackAction.Seek` | ✅ **the guard passes** — see below |
| Source base | `PrimaryAudioSourceBase.SeekAsync` | ✅ `IsSeekable` is `true`, so it calls the hook |
| **Source hook** | **`FilePlayerAudioSource.SeekCoreAsync`** | ⛔ **THE DROP — assigns `_position` and returns** |
| Engine | `SoundFlowPlaybackService.Seek` | ✅ exists and is correct — **and has no caller on this path** |
| Library | `SoundPlayerBase.Seek(TimeSpan, SeekOrigin = Begin) → bool` | ✅ present in the pinned SoundFlow **1.4.1** |

**Why the controller guard is not the drop, stated as a closed argument rather than an assumption.**
The guard is `primarySource is IPrimaryAudioSource seekSource && seekSource.IsSeekable`.
`AudioController`'s state endpoint sets `state.CanSeek = primary.IsSeekable` **from the same
`primarySource` object**, and `NowPlayingPanel` renders the draggable slider **only when
`_canSeek`** — a non-seekable source gets a read-only `RadzenProgressBar` instead. **So the slider
existing is itself evidence that `IsSeekable` was `true`**, and a `true` `IsSeekable` is exactly the
condition under which the guard passes and `SeekAsync` is called. The two facts cannot disagree.
`FilePlayerAudioSource.IsSeekable => true` unconditionally, which closes it from the other side.

**Why the engine is not the drop.** `SoundFlowPlaybackService.Seek` and `GetPosition` both shipped in
`PHN-1a`, both look up `_activePlayers`, and `Seek` propagates `SoundPlayerBase.Seek`'s `bool`. The
file player registers a player under `_playbackId`, and uses that registration correctly for **four
other transport verbs** — `Pause`, `Resume`, `SetVolume` and `StopAsync` all call
`_playbackService.X(_playbackId, …)`. **Seek is the only verb in the class that does not.** The call
shape needed already exists in the same file, four times over.

**The two halves of the owner's pairing fall out of one field.** `Position => _position`;
`SeekCoreAsync` writes `_position`; and `MonitorPlaybackAsync` advances `_position` by a wall-clock
`+1s` per tick, never reading the player. **Check #2 (`Time` advances) and check #1 (seek repositions)
read the same field, and only one of them was ever coupled to the audio.** That is why the clock looks
honest — it is honest about the field, and the field is the lie.

⭐ **This is the `CLAUDE.md` § Pre-Merge Review failure class, sixth known instance**, and the closest
sibling to `SoundFlowMasterMixer`'s *"Removed audio source … from mixer"*: a log line asserting an
action stronger than the code performed, sitting on top of a field mutation. `FUTURE-WORK` § 14b
already counts it as the fourth; the owner's sighting makes it the first of them to be independently
observed from the room.

---

### 0.4 ⭐ The row's success criterion SURVIVES — checked against the ADR, not inherited

The dispatch requires this be confirmed rather than assumed, because `PHN-10`'s row asserted a
criterion its ADR contradicted. **`AUD-24`'s criterion holds, with one addition.**

**ADR-029 § 14 Q3 is an OPEN QUESTION, not a specification.** Verbatim: *"Does `SeekAsync` on a small
local MP3 behave through SoundFlow? … **If it misbehaves, seek degrades to stop-and-restart-at-offset**
— still workable for a ~1 MB local file, slightly worse latency. — **Planner to verify before
sequencing**."* `PHN-1a` § 2.2 (1) repeats the same fallback.

So the ADR specifies the **user-visible outcome** (the audio is repositioned) and **pre-authorises two
mechanisms** to reach it. `PHN-2` § 3 U5's own pass text says the same in owner-facing words: *"if the
audio restarts from the tapped offset rather than seeking cleanly, **that is an accepted fallback, not
a failure**."*

- ✅ **"Assert the PRESENCE of repositioning" is correct** and is what § 3 gates on.
- ⚠ **But "repositioned" is defined as *the audio moves*, NOT as *`SoundPlayerBase.Seek` returned
  true*.** A plan that made the engine call and stopped there would satisfy the row's words and could
  still fail the ADR. Task C1 exists for exactly that case.
- ⛔ **ADR-029 § 14 **Q4** is a second requirement the row does not mention at all**, and it is
  load-bearing here: *"a seek mid-playback must **re-arm** that timer or completion will fire early."*
  Q4 was written about `AudioFileEventSource` (which now calls `SignalTransportChange`), but **the file
  player has the identical wall-clock completion in `MonitorPlaybackAsync`** and no re-arm at all.
  ⭐ **The good news: on this path the fix re-arms it for free.** The monitor accumulates from
  `_position`, so once `_position` and the audio agree, the deadline is right again. Today, dragging
  to 75 % of a four-minute track sets `_position` to 3:00 and the monitor declares *"track ended"*
  about a minute later while the audio is nowhere near the end. **That is a falsifiable prediction, and
  § 3 U3 tests it** — it is the sharpest available check that the field and the audio are now the same
  thing.

---

### 0.5 Constraints found while planning

- **C-1. A unit test can never prove the audio moved.** `DeviceFreePlaybackService` (already in the
  test project) builds a real `SoundFlowPlaybackService` over an un-initialised engine, and its own
  header states the bound: *"All four `Play*Async` methods return `false` early … so **no player can be
  registered**."* `Seek` therefore always returns `false` there. ⭐ **This is not an obstacle, it is the
  lever** — see Task 2. Anything asserting audible repositioning is UAT, per ADR-030's first rule
  (*"prefer no seam, and check reachability before assuming"*). **This plan adds no test seam.**
- **C-2. Every seek test in the solution passes on a total-no-op build.** All of them. The worst is
  `FilePlayerAudioSourceTests.SeekAsync_ValidPosition_SeeksSuccessfully`, which asserts
  `source.Position == 30s` after `SeekAsync(30s)` — **it asserts precisely the field write that IS the
  bug**, under a name claiming success. Task 2 rewrites it. Two more in
  `SoundFlowPlaybackServiceTransportTests` are vacuous by their own admission in-file.
- **C-3. `_position` is the persisted resume position.** `StopCoreAsync` and the track-change path both
  write `_preferences.CurrentValue.SongPositionMs` from it. Changing when `_position` moves changes
  what gets persisted — which is why `FUTURE-WORK` § 14a demanded its own UAT rather than letting this
  ride along with something else. § 3 U4 is that check.
- **C-4. The resume-position restore has never worked either.** `PlayCoreAsync` calls
  `SeekAsync(_pendingSeekMs)` after a successful `PlayFileAsync` and logs *"Restored playback position
  to {Ms}ms"* — a second silent no-op wearing a costume, on the same root cause, inside a
  `catch (Exception) { LogWarning }`. **The fix repairs it for free**, which means resume-where-you-
  left-off starts working on every restart. That is a real user-visible behaviour change and § 3 U4
  must confirm it rather than discover it.
- **C-5. Two internal callers route through `SeekCoreAsync`.** `PreviousAsync` calls
  `SeekAsync(TimeSpan.Zero)` twice — once under *"Position > 3 seconds, seeking to beginning"* (which
  then calls `PlayCoreAsync`, so the restart does the audible work regardless) and once under
  *"Already at beginning of playlist"* (which does not restart, so today the readout zeroes while the
  audio plays on — a third instance of the same family, also fixed for free).
- **C-6. `_playbackId` is invariantly `Id`.** `PlayCoreAsync` assigns `_playbackId = Id` and nothing
  else writes it; the comment there explains why a per-session GUID was wrong twice. Task 1 passes
  `Id` and lets `SoundFlowPlaybackService` answer *"is a player live"* from `_activePlayers`, which is
  the only place that actually knows.
- **C-7. `SoundPlayerBase.Seek` is an overload set of three**, all returning `bool`:
  `Seek(TimeSpan, SeekOrigin = Begin)`, `Seek(float seconds)`, `Seek(int sampleOffset)` — verified by
  reflecting the restored **SoundFlow 1.4.1** assembly, which is what
  `<PackageReference Include="SoundFlow" Version="1.*" />` resolves to. `SoundFlowPlaybackService.Seek`
  already binds the `TimeSpan` overload. **Do not change which overload is used.**
- **C-8. `AudioController`'s `Seek` case has no `else`.** A failed guard falls through and the method
  still returns `GetPlaybackState()` — **a 200 with a fresh snapshot for a seek that was never
  attempted.** Not the cause of this defect (the guard passes here), but the same shape one layer up,
  and reachable today by any client that posts a seek for a non-seekable source. Task 4 makes it say
  so, without changing the status code.
- **C-9. Log volume is an audio hazard on this box.** `CLAUDE.md` records that log volume correlates
  with audible distortion, and `radio-api`'s console sink is WARNING-and-above since `LOG-11`. A
  refusal warning that fires on every scrub of a stopped player would be noise on the journal. Task 1
  gates the severity on whether playback was believed live.

---

### 0.6 ⭐ The one behaviour change that rides along — and it IS separable, in one line

> ## ⛔ RULED 2026-09-10 — **GUARDED. The resume was declined; only the seek fix shipped.**
> This section's recommendation ("ship both") was **not** taken. The owner ruled that the resume is
> a startup behaviour change nobody asked for and must not ride along on a bug fix.
> `InitializeAsync` no longer assigns `_pendingSeekMs`; the consumer in `PlayCoreAsync` is kept and
> documented as dormant, so restoring the feature is the one line that was removed. The
> `Information` log line that claimed *"(seek to {Ms}ms)"* was corrected in the same PR, as the
> ruling required. ⭐ **Both restore arms now agree** — neither resumes, matching today's behaviour
> exactly, which is the inconsistency point 1 below warned about. Full record:
> [`docs/queue/AUD-24.md`](../../docs/queue/AUD-24.md) § *OWNER RULING*.
>
> ⚠ **Consequence not spelled out below:** a restored queue still sets `_position` to the saved
> offset while audio starts at zero, so `MonitorPlaybackAsync` still ends such a track early.
> **Pre-existing, not a regression** — un-guarding the resume is what would have fixed it.
> ⛔ **§ 3 U4 therefore cannot pass and is not a gate.** See the note there.

C-4 notes that the fix repairs resume-where-you-left-off for free. **A Builder must know, before Task
1, whether that can be declined** — *"a startup behaviour change nobody chose"* riding along on a bug
fix is the kind of thing that gets discovered in a room three weeks later.

**Answer: fully separable. The two are coupled through exactly one field with exactly one writer.**

- `FilePlayerAudioSource.InitializeAsync` assigns `_pendingSeekMs = prefs.SongPositionMs` at **one
  site**, inside the queue-restoration arm, and logs *"Restored queue position at index {Index}:
  {File} (seek to {Ms}ms)"* at Information.
- `PlayCoreAsync` consumes it at **one site**, gated `if (_pendingSeekMs > 0)`, and clears it.
- **`SeekCoreAsync` neither reads nor writes it.**

So the seek fix and the resume behaviour meet only at that one assignment. **To ship the fix without
enabling resume, guard or remove that single assignment** — `_position` keeps being restored (the
readout still shows where you stopped, exactly as today) and no seek is attempted. **To ship both,
change nothing.** Either way Task 1 is identical.

⚠ **Two facts that should inform the choice, both measured while planning:**

1. ⭐ **The two restore arms already disagree, and nobody noticed.** `InitializeAsync`'s *fallback*
   arm — *"restore just the last played file"* — sets `_position` and **not** `_pendingSeekMs`. So
   after the fix, restoring from a saved **queue** would resume audibly while restoring a single
   **last-played file** would not. **That inconsistency exists today and is invisible today**, because
   neither arm resumes anything. The fix makes one of them start working and leaves the other alone.
   ⛔ **Whichever way the owner rules, make both arms agree** — that is the part not choosing is
   choosing.
2. **The Information-level log has been claiming a seek that never happened**, on every startup with a
   restored queue, for the life of the file. Same failure class as `SeekCoreAsync`'s own Debug line.

**Recommendation: ship both, and put U4 in front of the owner** — resuming where you stopped is the
behaviour the persisted field, the restore code and the log message have all claimed for years, and
declining it means keeping three pieces of code that describe a feature the appliance does not have.
⛔ **But it is the owner's call, not Builder's.** If the PR review has no ruling, ship the seek fix and
guard the assignment; a feature that appears without being asked for is worse than one that waits.

---

## 1. Tasks

### Task 1 — `FilePlayerAudioSource.SeekCoreAsync` calls the engine, and stops moving the readout when it cannot

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs`
**Symbol:** `SeekCoreAsync` (find it between `StopCoreAsync` and `OnVolumeChanged`)

Replace the method in full:

```csharp
  /// <inheritdoc/>
  /// <remarks>
  /// AUD-24. This method used to assign <c>_position</c> and return, which moved the readout and
  /// the API's reported position while the audio carried on from where it was — the defect
  /// FUTURE-WORK § 14a recorded on 2026-09-02 and the owner observed at the cabinet on 2026-09-09.
  /// Two rules hold it closed:
  ///   1. the engine is asked to reposition, through the same registration the class already uses
  ///      for Pause / Resume / SetVolume / StopAsync; and
  ///   2. <c>_position</c> advances ONLY when the engine says it moved. Position reads that field,
  ///      so writing it on a refusal would re-create the defect in a smaller shape. Leaving the
  ///      anchor where it was makes the scrubber snap back, which DECISION-LOG (ADR-029
  ///      amendments, Decision 2) names as the correct user-visible answer to a refused seek.
  /// </remarks>
  protected override Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    // Seeking is only valid for positive positions within the duration.
    // When duration is zero or not set, seeking is limited to position zero.
    if (position < TimeSpan.Zero || (_duration > TimeSpan.Zero && position > _duration))
    {
      throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
    }

    // No playback service at all is a degraded configuration in which this source cannot produce
    // audio by any route, so the field IS the position and there is nothing to contradict. Every
    // path that can actually play has a service.
    if (_playbackService is null)
    {
      _position = position;
      return Task.CompletedTask;
    }

    // Id, not _playbackId: PlayCoreAsync assigns _playbackId = Id and nothing else writes it, so
    // the registration key is invariant. Passing Id lets SoundFlowPlaybackService answer "is there
    // a live player" from _activePlayers, which is the only place that knows; _playbackId records
    // only that we once started one.
    var moved = _playbackService.Seek(Id, position);

    if (!moved)
    {
      // WARNING only when we believed playback was live — a scrub against a stopped player is an
      // ordinary refusal, and this box's journal is an audio hazard (CLAUDE.md § LOG-11).
      if (_playbackId is not null)
      {
        Logger.LogWarning(
          "🎵 FILE PLAYER: seek to {Position} was refused by the player for \"{FileName}\"; the reported position stays at {Reported}",
          position, Path.GetFileName(_currentFile ?? "(none)"), _position);
      }
      else
      {
        Logger.LogDebug(
          "🎵 FILE PLAYER: seek to {Position} ignored — no live player registered", position);
      }

      return Task.CompletedTask;
    }

    _position = position;
    Logger.LogDebug("Seeked to {Position}", position);
    return Task.CompletedTask;
  }
```

⚠ **Do not "simplify" the two-branch logging into one line.** The severity split is the point: one
arm is a fault, the other is a normal outcome.

⚠ **`Path` is already imported in this file** (`GetFullPath`, `Path.GetFileName` are both used in
`PlayCoreAsync`). Do not add a `using`.

**Acceptance:** Release build at the **47-warning, 0-error** baseline, measured with
`--no-incremental` (see `CLAUDE.md`).

---

### Task 2 — the tests that are RED on `main`, and the vacuous one that must go

**File:** `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/FilePlayerAudioSourceTests.cs`

**2a — replace `SeekAsync_ValidPosition_SeeksSuccessfully` in full.** Its name claims a success it
never checked; it asserts the field write that was the bug. What it *legitimately* covers is the
no-service arm, so keep that and say so.

```csharp
  [Fact]
  public async Task SeekAsync_WithNoPlaybackService_MovesTheReportedPositionOnly()
  {
    // AUD-24. This is the degraded arm: with no playback service there is no audio to contradict
    // the field, so moving it is honest. It is NOT evidence that a seek repositions anything —
    // that is SeekAsync_WhenThePlayerRefuses_LeavesTheReportedPositionAlone below, and § 3 U1/U2.
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    await source.SeekAsync(TimeSpan.FromSeconds(30));

    Assert.Equal(TimeSpan.FromSeconds(30), source.Position);
  }
```

**2b — add the two tests that fail on `main`.** Put them directly beneath 2a.

```csharp
  [Fact]
  public async Task SeekAsync_WhenThePlayerRefuses_LeavesTheReportedPositionAlone()
  {
    // AUD-24, and the whole row in one assertion. A device-free SoundFlowPlaybackService can never
    // register a player (see DeviceFreePlaybackService's own remarks), so Seek returns false
    // deterministically — which is exactly the shape of a refusal on the box. The reported
    // position must NOT move, because Position is what the panel and /api/audio read: moving it
    // here is how a seek that repositioned nothing came to look like one that worked.
    //
    // ⛔ RED ON main: SeekCoreAsync there assigns _position on every path, so this reads 30s.
    var source = CreateSource(DeviceFreePlaybackService.Create());
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    await source.SeekAsync(TimeSpan.FromSeconds(30));

    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public async Task SeekAsync_WhenThePlayerRefuses_StillRangeChecks()
  {
    // The range guard runs before the engine is consulted, and must keep doing so: an out-of-range
    // seek is a caller error, not a refusal, and EventPlaybackService distinguishes them.
    var source = CreateSource(DeviceFreePlaybackService.Create());
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.SeekAsync(TimeSpan.FromSeconds(-1)));
  }
```

**2c — the helper the two new tests need.** `CreateSource` currently takes no arguments; add an
overload beside it rather than changing the existing signature, so no existing test moves:

```csharp
  private FilePlayerAudioSource CreateSource(SoundFlowPlaybackService playbackService)
  {
    return new FilePlayerAudioSource(
      _loggerMock.Object,
      _optionsMock.Object,
      _preferencesMock.Object,
      _testDir,
      playbackService: playbackService);
  }
```

⚠ `DeviceFreePlaybackService` is `internal static` in `Radio.Infrastructure.Tests` — the same
assembly as this test file — so it needs no `InternalsVisibleTo` and no new seam. Add
`using Radio.Infrastructure.Audio.SoundFlow;` and
`using Radio.Infrastructure.Tests.Audio.SoundFlow;` if they are not already present.

**2d — verify the RED before implementing Task 1.** ⛔ **This is a gate, not a suggestion.** Stash
Task 1, run the two new tests, and confirm they fail:

```bash
git stash push src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs
dotnet test RadioConsole.sln -c Release --filter "FullyQualifiedName~FilePlayerAudioSourceTests.SeekAsync" > /tmp/red.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/red.log
git stash pop
```

**Expected:** `SeekAsync_WhenThePlayerRefuses_LeavesTheReportedPositionAlone` **FAILS** with
`Assert.Equal() Failure: Expected: 00:00:00, Actual: 00:00:30`. **If it passes on `main`, stop** —
the test is vacuous and the plan's central claim is wrong. Record the observed failure text in the
PR body; *"I verified it is red"* without the message is the thing this repo has been burned by.

**Acceptance:** two new tests red before Task 1, all four green after, and the rest of
`FilePlayerAudioSourceTests` unchanged and green.

---

### Task 3 — a structural test, so the engine call cannot be deleted again

**File (new):** `tests/Radio.Core.Tests/FilePlayerSeekReachesTheEngineLintTests.cs`

The house already uses this shape — `EventSourceStopIsNotActivityGuardedLintTests`,
`NoRawMiniAudioEngineConstructionTests` — for exactly this situation: the behaviour cannot be
asserted without hardware, but the *structure* that produces it can. Task 2's tests prove the
refusal arm; this one proves the call exists at all, which is the half no runtime test in a
device-free process can reach.

```csharp
using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// AUD-24. FilePlayerAudioSource.SeekCoreAsync shipped for the life of the project assigning
/// _position and returning — no engine call, under a log line that said "Seeked to {Position}".
/// The audible half cannot be asserted in a device-free process (see the plan § 0.5 C-1), so this
/// pins the one structural fact that made the defect possible: the seek path must ask the playback
/// service to move the player.
/// </summary>
/// <remarks>
/// ⚠ This asserts a CALL EXISTS, not that audio moved. It would stay green if the call were made
/// with the wrong argument. Repositioning is owner UAT (plan § 3 U1/U2), and always will be.
/// </remarks>
public class FilePlayerSeekReachesTheEngineLintTests
{
  [Fact]
  public void SeekCoreAsync_AsksThePlaybackServiceToMoveThePlayer()
  {
    var path = Path.Combine(
      RepositoryRoot.Find(),
      "src", "Radio.Infrastructure", "Audio", "Sources", "Primary", "FilePlayerAudioSource.cs");
    var source = File.ReadAllText(path);

    var body = ExtractSeekCoreBody(source);

    Assert.True(
      Regex.IsMatch(body, @"_playbackService\s*[.?]\s*Seek\s*\("),
      "FilePlayerAudioSource.SeekCoreAsync must call _playbackService.Seek(...). Without it the "
      + "method moves the reported position and no audio, which is AUD-24 exactly. If you are "
      + "deliberately replacing the mechanism (ADR-029 § 14 Q3 licenses stop-and-restart-at-offset "
      + "as a fallback), update this test and say which mechanism replaced it.");
  }

  [Fact]
  public void SeekCoreAsync_DoesNotAdvanceThePositionUnconditionally()
  {
    var path = Path.Combine(
      RepositoryRoot.Find(),
      "src", "Radio.Infrastructure", "Audio", "Sources", "Primary", "FilePlayerAudioSource.cs");
    var body = ExtractSeekCoreBody(File.ReadAllText(path));

    // Every "_position = position" must be reachable only after a decision. The defect's shape was
    // a single unconditional assignment as the last statement before the return.
    var assignments = Regex.Matches(body, @"_position\s*=\s*position\s*;").Count;
    var decisions = Regex.Matches(body, @"\bif\s*\(").Count;

    Assert.True(
      decisions >= assignments,
      $"SeekCoreAsync assigns _position {assignments} time(s) behind {decisions} guard(s). "
      + "An assignment that no guard protects reports a reposition that may not have happened.");
  }

  private static string ExtractSeekCoreBody(string source)
  {
    var start = source.IndexOf("protected override Task SeekCoreAsync", StringComparison.Ordinal);
    Assert.True(start >= 0, "SeekCoreAsync was not found in FilePlayerAudioSource.cs — if it was "
      + "renamed or moved, update this test rather than deleting it.");

    // Walk braces from the first '{' after the signature to its match.
    var open = source.IndexOf('{', start);
    var depth = 0;
    for (var i = open; i < source.Length; i++)
    {
      if (source[i] == '{') depth++;
      else if (source[i] == '}' && --depth == 0)
      {
        return source[open..(i + 1)];
      }
    }

    Assert.Fail("SeekCoreAsync's body was not brace-balanced.");
    return string.Empty;
  }
}
```

✅ **`RepositoryRoot` is `internal static` at `tests/Radio.Core.Tests/RepositoryRoot.cs`** (the `UI-7`
extraction), so a file in `Radio.Core.Tests` reaches `RepositoryRoot.Find()` with no `using` and no
copy — which is why this test goes in that project and not beside the class it scans. Four existing
lint tests there use it the same way (`PlaybackKeyLintTests`, `LogSafetyLintTests`,
`AsyncEventFanOutLintTests`, `EventSourceStopIsNotActivityGuardedLintTests`); **read one before
writing this, and match its shape.** ⚠ **If a git worktree is in play, re-read `CLAUDE.md`'s
warning**: a tree-scanning test inside `.claude/worktrees/` resolves to the wrong root and silently
scans a copy — `RepositoryRoot.Find`'s own doc comment records that this has happened.

⚠ **`PHN-10`'s pre-merge review found a hole in its own lint — a brace-less `if` defeated it
entirely.** Before accepting these two tests, mutate `SeekCoreAsync` deliberately (delete the
`_playbackService.Seek` call; then separately, restore it but move `_position = position` above the
`if`) and confirm each test goes red. **Commit the real change first** — `CLAUDE.md` records work
lost to `git checkout --` between mutation runs.

**Acceptance:** both tests green after Task 1, both proven red by mutation.

---

### Task 4 — `AudioController` stops returning 200 in silence for a seek it did not attempt

**File:** `src/Radio.API/Controllers/AudioController.cs`
**Symbol:** the `case PlaybackAction.Seek:` arm of the playback-update switch

C-8. Add the missing `else`. ⛔ **Do not change the status code** — `UpdatePlaybackState` returns
`GetPlaybackState()` for every action and the panel re-reads it; altering that is an API-shape
decision, not a bug fix, and it is recorded in § 6 instead.

```csharp
        case PlaybackAction.Seek:
          if (request.SeekPosition.HasValue && primarySource is IPrimaryAudioSource seekSource && seekSource.IsSeekable)
          {
            await seekSource.SeekAsync(request.SeekPosition.Value);
            _logger.LogInformation("Seeked to {Position}", request.SeekPosition.Value);
          }
          else
          {
            // AUD-24 C-8. This arm used to fall through in silence and still return a 200 with a
            // fresh state snapshot, so a client could post a seek, be told nothing was wrong, and
            // hear no change. The response shape is unchanged deliberately; what changes is that
            // the refusal is now visible to anyone reading the journal.
            _logger.LogWarning(
              "Seek to {Position} was not attempted: source {SourceId} is {Reason}",
              request.SeekPosition,
              primarySource?.Id ?? "(none)",
              primarySource is IPrimaryAudioSource s
                ? (s.IsSeekable ? "seekable but no position was supplied" : "not seekable")
                : "not a primary source");
          }
          break;
```

⚠ **The log message must not over-claim.** It says *"was not attempted"*, which is exactly what
happened — not *"failed"*, and not *"refused"*, which would describe a decision the source made.

**Acceptance:** `Radio.API.Tests` green; no existing controller test asserts on this branch (there
are none — confirmed while planning), so nothing should move.

---

### Task 5 — the documents that already knew, brought up to date

Three in-tree documents describe this defect and one asserts its opposite. **Docs Impact is not
optional here** — leaving them is how the row got filed as an unknown in the first place.

**5a — `design/FUTURE-WORK.md` § 14a.** Do **not** delete the entry; retitle it as resolved and keep
the diagnosis, which is the best account of the mechanism anyone wrote. Change the section heading to:

```
### 14a. `FilePlayerAudioSource.IsSeekable` claimed a seek that did not move any audio — ✅ FIXED by `AUD-24` (2026-09-10)
```

and insert, immediately under it:

```markdown
✅ **Fixed by `AUD-24`** — `SeekCoreAsync` now calls `SoundFlowPlaybackService.Seek` and advances
`_position` only when the player reports it moved. ⭐ **This entry predated the owner's sighting by a
week and was never connected to it**: the row was filed on 2026-09-09 as a fresh unknown with three
open scope questions, all three of which are answered above. **A defect logged here is only worth the
grep that finds it again** — cite this section from any row that touches seek.

⚠ **Two consequences of the fix that were not obvious and are now confirmed by UAT** (see
`design/plans/AUD-24-the-seek-that-only-moved-the-readout.md` § 3): the persisted resume position
(`SongPositionMs`) now actually restores, and `MonitorPlaybackAsync`'s wall-clock completion no longer
ends a track early after a scrub.
```

**5b — `design/AUDIO-PIPELINE-REVIEW.md` finding #3.** It reads *"File-player seek is display-only and
position is a wall-clock estimate."* **Only the first half is fixed.** Amend to:

```markdown
3. **File-player seek WAS display-only (✅ fixed by `AUD-24`, 2026-09-10); position is still a
   wall-clock estimate**, although the SoundFlow version in use (`1.*` → 1.4.1) exposes
   `SoundPlayerBase.Seek()/Time/Duration`. Comments claiming the API doesn't exist are stale.
   ⚠ `FilePlayerAudioSource.Position` still reports a `+1s`-per-tick accumulator rather than reading
   `SoundFlowPlaybackService.GetPosition`, so it drifts; `AUD-24` deliberately did not change that
   (see that plan § 5).
```

**5c — `design/decisions/2026-08-03-gv-audio-through-engine.md` § 8.3.** Add a `⟨AUD-24⟩` correction
note immediately after the sentence claiming `FilePlayerAudioSource` *"already implements seeking over
a local file through `SoundFlowPlaybackService`"*:

```markdown
> ⚠ **⟨AUD-24, 2026-09-10⟩ That sentence was false on both halves when written, and stayed false for
> five weeks.** `FilePlayerAudioSource.SeekCoreAsync` assigned a field and called nothing, and
> `SoundFlowPlaybackService` had no seek method at all until `PHN-1a` Task 4 added one.
> `design/FUTURE-WORK.md` § 14a recorded the first half on 2026-09-02; the owner observed it at the
> cabinet on 2026-09-09 (`PHN-2` check #1). Fixed by `AUD-24`. **The argument § 8.3 makes is
> unaffected** — seeking a materialised local file is the right mechanism, and it is now real — but
> the claim that it already worked was an assumption, not a reading.
```

**5d — `design/DECISION-LOG.md`.** ⛔ **No new ADR.** This row implements a decision already recorded
(ADR-029 § 14 Q3's outcome, `FUTURE-WORK` § 14a's prescription); a decision record for a bug fix that
changed no decision would be noise. If the ADR-licensed fallback (Task C1) is taken, that **is** a
decision and gets an entry.

**5e — `docs/queue/AUD-24.md`.** Append a `✅ DIAGNOSED` section recording § 0.2's five corrections,
above all the misattribution of the "misled" quote to `CLAUDE.md`, and the surface split in § 0.2 ⑤.

**Acceptance:** every claim above re-grepped against the file before the edit; no line numbers used as
anchors anywhere.

---

### Task 6 — build, test, and the scope gate

```bash
dotnet build RadioConsole.sln -c Release --no-incremental > /tmp/build.log 2>&1; echo "exit=$?"
grep -E "^\s+[0-9]+ Warning\(s\)" /tmp/build.log     # must equal the 47/0 baseline
dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

⛔ **Never pipe either into `tail`** — `CLAUDE.md` records a run that exited `0` with five tests
failing. ⚠ **The Release build must be `--no-incremental`** or it reports `0 Warning(s)` and the gate
is cold.

**Known-failing on Windows and not a regression:** four `SrcVariableResamplerTests`,
`NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.

**Scope gate — the files this PR may touch, and no others:**

| File | Task |
|---|---|
| `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` | 1 |
| `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/FilePlayerAudioSourceTests.cs` | 2 |
| `tests/Radio.Core.Tests/FilePlayerSeekReachesTheEngineLintTests.cs` (new) | 3 |
| `src/Radio.API/Controllers/AudioController.cs` | 4 |
| `design/FUTURE-WORK.md`, `design/AUDIO-PIPELINE-REVIEW.md`, `design/decisions/2026-08-03-gv-audio-through-engine.md`, `docs/queue/AUD-24.md`, `docs/BUILDER_QUEUE.md` | 5 |

⛔ **`AudioFileEventSource` is NOT in this list and must not be touched.** Its seek is correct. A
change there risks the voicemail path that `PHN-10` just stabilised, for no gain.

⛔ **Stage by explicit path.** No `git add -A`, no `git add .`, no `git commit -a`.

---

### Task C1 — CONTINGENCY: the ADR-licensed fallback, if UAT says `Seek` does not move MP3 audio

**Run this task only if § 3 U1 fails after Task 1.** ADR-029 § 14 Q3 pre-authorises it:
*"If it misbehaves, seek degrades to stop-and-restart-at-offset."*

**Diagnose first, from the box.** `SoundFlowPlaybackService.Seek` already logs
`"Seek for source {SourceId} to {Position} returned {Moved}"` at Debug — which since `LOG-11` goes to
the **file** sink, not the journal:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -n "Seek for source" $F | tail -20'
```

- **`returned False`** → the data provider refused. Take the fallback.
- **`returned True` but the audio did not move** → SoundFlow reported a success it did not deliver.
  Take the fallback, and record it in `DECISION-LOG` — that is a library-behaviour finding worth
  keeping, and it invalidates ADR-029 § 8.3's mechanism as well as its claim.
- **No line at all** → `SeekCoreAsync` is not being reached; re-establish the layer before writing
  any code. Check `state.CanSeek` on `/api/audio/playback` first.

**The fallback shape**, if taken: on `moved == false` with `_playbackId is not null`, tear the player
down and start it again, then seek immediately against the fresh provider — the same
`StopAsync` → `PlayFileAsync` → `Seek` sequence `PlayCoreAsync` already performs for the persisted
resume position, which is the in-tree precedent. ⚠ **It costs a re-open of the file and a gap in the
audio**, which the ADR calls *"slightly worse latency"* and § 3 U5's pass text calls *"an accepted
fallback, not a failure."* ⚠ **Update Task 3's first lint test** if the mechanism changes — its
failure message says so explicitly.

---

## 2. Test Plan

### 2.1 What the automated tests prove, and the mutation that reds each one

| Test | Proves | Mutation that reds it |
|---|---|---|
| `SeekAsync_WhenThePlayerRefuses_LeavesTheReportedPositionAlone` | A refused seek does not move the reported position — i.e. `Position` can no longer certify a reposition that did not happen | Restore `_position = position` as an unconditional statement (**this is `main`**) |
| `SeekAsync_WhenThePlayerRefuses_StillRangeChecks` | The range guard precedes the engine call | Move the guard below the `Seek` call |
| `SeekAsync_WithNoPlaybackService_MovesTheReportedPositionOnly` | The degraded arm is unchanged, so `PreviousAsync` and the persistence tests keep their meaning | Delete the `_playbackService is null` branch |
| `SeekCoreAsync_AsksThePlaybackServiceToMoveThePlayer` | The engine call exists in the seek path | Delete the `_playbackService.Seek(...)` call |
| `SeekCoreAsync_DoesNotAdvanceThePositionUnconditionally` | No `_position` write escapes a guard | Hoist `_position = position` above the `if` |

### 2.2 ⛔ What the tests do NOT prove, stated plainly

**Not one of them asserts that audio moved, and none can.** `DeviceFreePlaybackService` cannot
register a player, so the engine's success path is unreachable in a device-free process — the same
bound `PHN-10` accepted for the mixer detach, and `PHN-1a` § 2.2 accepted for this very question. A
green suite is **not** evidence for this row.

⚠ **`SeekCoreAsync_AsksThePlaybackServiceToMoveThePlayer` would stay green if the call passed the
wrong position, the wrong id, or ignored the result.** It closes the shape of the defect, not the
behaviour.

⭐ **And the reason this section is long: the entire existing seek suite — every test in the solution
that mentions seek — passes on a build where seek is a total no-op.** `AUD-24`'s own criterion (*"a
test that asserts 'seek did not throw' passes today"*) understates it. `SeekAsync_ValidPosition_
SeeksSuccessfully` did worse than that: **it asserted the defect and called it success.**

### 2.3 Commands

```bash
dotnet test RadioConsole.sln -c Release --filter "FullyQualifiedName~FilePlayerAudioSourceTests"
dotnet test RadioConsole.sln -c Release --filter "FullyQualifiedName~FilePlayerSeekReachesTheEngine"
dotnet test RadioConsole.sln -c Release --filter "FullyQualifiedName~AudioControllerTests"
```

---

## 3. Owner UAT — the only gate that closes this row

### ⛔ Part 0 — before the owner sits down. Four seconds, and it is not optional.

`CLAUDE.md` § *Read the deployed SHA BEFORE a human runs UAT* records an owner running a four-part
cabinet UAT on `PHN-10` **against a box that did not have the fix** — merged, never deployed, and
nothing in the workflow made the difference loud. ⛔ **Do not ask for this sitting until both of these
match the merge commit:**

```bash
curl -s http://radio:5000/api/health/version | grep -o '"gitShaShort":"[^"]*"'
curl -s http://radio:5002/api/health/version | grep -o '"gitShaShort":"[^"]*"'
git log --oneline -1 origin/main
```

Deploy with `./deploy/Deploy-ToLinux.ps1` (defaults are `-TargetHost radio -Runtime linux-x64`) and
re-read both before proceeding. ⚠ **Also confirm the console is not muted** — the `PHN-2` sitting
began `isMuted: true`, and *every* sound check would have failed for the wrong reason.

### Part 1 — the two checks that settle the row

**U1 — Does dragging move the music? (the owner's own report, `PHN-2` check #1 on the music player)**
*Do:* Play a local MP3 at least two minutes long from the queue on the Home page. Let it run ~15
seconds so you know the passage. **Drag** the position bar to about three-quarters along.
*Pass:* the audio **jumps** — a different part of the track, within about a tenth of a second — and
both the bar and the elapsed readout stay where you dragged them.
*Fail:* the bar moves and the music carries on unchanged (**the defect**), or the bar snaps back
(the player refused — that is the **honest** new failure, and it means Task C1, not a regression).
⭐ *Also note:* if the audio **restarts from the dragged offset** rather than jumping cleanly, that is
the ADR-licensed fallback and is a **PASS**. Say which one you got.

**U2 — Does tapping move a voicemail? (`PHN-2` check #1 as it was actually WRITTEN)**
⚠ **This is a different surface from U1 and has never been run.** § 0.2 ⑤ explains why.
*Do:* Play a voicemail at least 20 seconds long from the phone page. **Tap** — do not drag; there is
no drag handler — the progress bar about three-quarters along.
*Pass:* the audio jumps. *Fail:* it carries on unchanged.
⭐ **Expected to PASS without any change from this PR** — that path was already correct. **Record it
either way.** If it fails, the defect is wider than this plan and the row must be re-opened, not
closed.

### Part 2 — the three things the fix changes that nobody asked for

**U3 — Does the track still end when it should?**
*Do:* After U1's drag, let the track play to its natural end.
*Pass:* it ends at the end. *Fail:* it ends early, or runs past the end.
⭐ **This is the sharpest available proof that the readout and the audio are the same thing again.**
`MonitorPlaybackAsync` counts `+1s` per tick from `_position` and declares the track over at
`_duration`. **On the broken build, dragging to 75 % of a four-minute track should have ended it about
a minute later, mid-song.** If you ever saw that, it was this. (§ 0.4.)

**U4 — ⛔ SUPERSEDED BY THE OWNER'S RULING (§ 0.6). This check CANNOT pass and is NOT a gate.**
The resume was deliberately guarded, so playback after a restart starts from the beginning of the
track by design. ⭐ **Run it anyway, once, as a NEGATIVE check:** the readout should show roughly
where you stopped and the audio should start from the beginning. **That is the PASS.** If playback
instead resumes audibly near 1:00, the guard did not hold and the PR shipped a startup behaviour
change the owner declined — report that. The original text is kept below for the day the ruling is
revisited.

**U4 (original, for the day the resume is re-enabled) — Does resume-where-you-left-off work now? (`FUTURE-WORK` § 14a's own required check, C-3/C-4)**
*Do:* Play a track, let it reach ~1:00, press Stop. Restart `radio-api`
(`ssh mmack@radio 'sudo systemctl restart radio-api'`), then press Play.
*Pass:* playback resumes **audibly** near 1:00. *Fail:* it starts from the beginning while the readout
claims 1:00 — which is what it has always done, silently, under a log line saying
*"Restored playback position to {Ms}ms"*.
⚠ **A pass here is a behaviour CHANGE, not a bug fix the owner asked for.** If resuming mid-track is
unwanted, say so — that is a product decision and it goes in the queue, not in this PR.

**U5 — Does Previous still restart the track?**
*Do:* More than 3 seconds into a track, press Previous.
*Pass:* the same track restarts from the beginning. *Fail:* it jumps to the previous track, or the
readout zeroes while the audio plays on. (C-5.)

### Part 3 — on a fail, capture this before anything else

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -nE "Seek|Seeked" $F | tail -30'
ssh mmack@radio "journalctl -u radio-api --since '-15min' --no-pager | grep -i seek"
curl -s http://radio:5000/api/audio/playback | python3 -m json.tool | grep -iE 'position|duration|canSeek'
```

⚠ Bound every journal query (`--since`) and never tail one during audio testing — log volume on this
box correlates with audible distortion.

---

## 4. Self-review

- **Placeholder scan:** no `TBD`, no *"similar to Task N"*, no *"implement later"*. Every task carries
  literal code except Task 5, which carries literal markdown, and Task C1, which is a contingency
  whose trigger condition is stated.
- **Spec coverage:** ADR-029 § 14 Q3 (repositioning, with the fallback licensed — Task 1 + C1);
  Q4's re-arm requirement (§ 0.4, discharged as free on this path, checked by U3); `PHN-1a` § 2.2 (1)
  (U1 + U2); `PHN-2` § 3 U5 (U2, on the surface it was written for); `FUTURE-WORK` § 14a's own UAT
  requirement (U4).
- **The row's criterion was checked, not inherited.** § 0.4. It survives; one addition (Q4) and one
  sharpening (*repositioned* means the audio moves, not that a method returned true).
- **Anchors:** symbol names only. No line numbers anywhere in this document.
- **Type consistency:** `SoundFlowPlaybackService.Seek(string, TimeSpan) → bool`;
  `SoundPlayerBase.Seek(TimeSpan, SeekOrigin = Begin) → bool` in SoundFlow **1.4.1**, verified by
  reflection rather than inferred. `Position`/`_position`/`_duration` are all `TimeSpan`.
  `_pendingSeekMs` is `long` milliseconds and is **not** touched by this plan.
- **Scope:** four source/test files, five documents. `AudioFileEventSource` explicitly excluded.
- **Ambiguity:** one — whether the owner's sighting was U1's surface or U2's. **Not resolved by
  inference; resolved by running both.**

---

## 5. What this plan deliberately does not do, and why

1. ⛔ **It does not make `Position` read through to `SoundFlowPlaybackService.GetPosition`**, although
   `FUTURE-WORK` § 14a suggests it. That is a second change with a **different** blast radius —
   `_position` is the persisted resume position and the completion deadline, and a read-through
   changes what gets written to `SongPositionMs` on every stop. **The row is about seek.** Once the
   seek is real, `_position` and the player agree at every seek and drift only by timer error between
   them. Carried to § 6.
2. ⛔ **It does not tighten `FilePlayerAudioSource.IsSeekable`,** which is `true` unconditionally even
   with no playback service — dishonest by `AudioFileEventSource`'s own stated standard (*"Claiming
   IsSeekable and then failing is worse than reporting false"*). Tightening it makes
   `PrimaryAudioSourceBase.SeekAsync` **throw** on several existing paths, including two internal
   `PreviousAsync` callers and four existing tests. That is a bigger change than the defect. Carried.
3. ⛔ **It does not widen `EventPlaybackService.SeekAsync` to report *repositioned* rather than
   *dispatched*.** `DECISION-LOG` ADR-029 Amendments Decision 2 closed that as "no", deliberately.
   Not re-opened here.
4. ⛔ **It does not change `AudioController`'s status code** for a seek it did not attempt, only its
   log. Changing it is an API-shape decision.
5. ⛔ **It does not touch `AudioFileEventSource`.** Correct already; and `PHN-10` has an open UAT on
   that file.

---

## 6. Carried forward

- **`Position` as a wall-clock accumulator.** `MonitorPlaybackAsync` adds a nominal `1s` per loop
  iteration whose real period is `Task.Delay(1s)` plus work, so it drifts, and it is what
  `/api/audio` reports. `SoundFlowPlaybackService.GetPosition` exists and is correct. Worth a row;
  ⚠ it changes what `SongPositionMs` persists, so it needs § 3 U4 re-run.
- **`FilePlayerAudioSource.IsSeekable` is unconditionally `true`.** See § 5 (2). Worth a row together
  with the item above — they touch the same guard.
- **`EventPlaybackService.SeekAsync` returns `true` for a player-refused seek.** Closed as "no" once;
  the owner now has a surface where a refusal is visible, so it may be worth re-asking. **Do not
  re-open it inside this row.**
- **⚠ `docs/BUILDER_QUEUE.md`'s ID-namespace section said the next free `AUD` number was `AUD-24`.**
  It is taken, and so was `PHN-10`. Corrected in the same commit as this plan's queue update.
- **⛔ THE QUEUE'S COUNT SITES MUST BE RE-ENUMERATED, NOT ASSUMED — and this is a bigger finding than
  the stale number it produced.** The count sentence read `**30** live, **55** archived, **85**
  total` while the tree held 29/56/85: `c1b9972c` archived `PHN-10` without updating it. ⭐ **But the
  interesting part is why repeated verification missed it.** The standing procedure verifies "four
  count sites" with regexes tuned to the canonical phrasing; `PHN-10`'s Builder wrote its count in
  **prose** — `Counts unchanged: N live, N archived, N total` — a shape those patterns cannot see.
  **The instrument was validated against the sites that existed when it was written, and an agent
  then created a site in a phrasing it could not match.** *"Four count sites"* was a description of
  the file at a moment, never a constraint on it — and there is **one** count site in
  `BUILDER_QUEUE.md` today, not four.
  **The rule: enumerate by meaning, not by pattern**, with something markdown-tolerant:
  `grep -noE "[*0-9]+ ?(live|archived|total)" docs/BUILDER_QUEUE.md docs/BUILDER_QUEUE_ARCHIVE.md`.
  ⚠ **The obvious `[0-9]+ live` returns ZERO hits** against a file holding dozens of counts, because
  every one is bolded as `**29** live` and the digits are not adjacent to the word. *(That was this
  entry's own first draft. The advice was defeated by the exact mechanism it had just described — so
  validate the instrument on a case you know is there before trusting a clean sweep.)* ⭐ **Then read
  the hits rather than counting them:** only the **newest banner entry** must be current; every count
  below it belongs to a `Previously` entry, is a true record of its own moment, and must not be
  rewritten. ⭐ **Same family as the two traps
  `CLAUDE.md` already records** — `dotnet test | tail` reporting `tail`'s exit code, and a failed
  `git push` ending `Everything up-to-date`: **an instrument that cannot see the state it claims to
  measure, whose clean result is indistinguishable from a true one.**
- **⚠ To the owner — one question this plan does not answer.** § 3 U4 makes resume-where-you-left-off
  start working after a restart, because the fix repairs a call that has been silently failing since
  the file was written. **That is a change to how the appliance behaves at startup**, and nobody chose
  it. If you would rather a restart always begin a track from zero, say so and it becomes a one-line
  row — but it should be a decision, not a side effect.
