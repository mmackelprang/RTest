# PLAN — `PHN-10` · nothing can stop a voicemail. Two at once is only the cheapest way to hear it.

- **Row:** `PHN-10` (`docs/BUILDER_QUEUE.md` § Queue, dossier `docs/queue/PHN-10.md`). 🟠 **P1.**
- **Branch:** `fix/phn-10-two-voices-at-once`
- **Planned against:** `main` at **`48912474`**.
- **Filed from:** the owner's `PHN-2` `SOUND` UAT at the cabinet, check #11
  (`docs/uat/2026-09-09-phn2-sound-uat/RESULT.md`), and its **RE-RUN** after `AUD-2` deployed.
- **Owner ruling of 2026-09-09:** two voicemails at once is a defect; a second TTS cancelling the
  first is **correct behaviour**. ⛔ The fix must preserve the second. §0.6 is how it does.
- **Estimate:** 0.5–1 d. The production change is **six lines in one file**. Everything else in this
  plan is the diagnosis, the corrections the row needs, and the verification — because a green suite
  cannot close this row and the six lines are the least interesting part of it.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`AudioFileEventSource` — the source behind every voicemail — gates the **only call that actually
stops audio** behind a private `bool` that the teardown sequence has already cleared before it is
read. Every stop path in the attended-playback seam funnels through
`EventPlaybackService.TearDownAsync`, whose **first statement cancels the playback token** and whose
**last statements depend on a flag that cancellation destroys**. So the second voicemail's
replacement fires correctly, publishes `Stopped` for the first, installs itself — and the first
voicemail's `SoundPlayer` is never stopped, never detached from the SoundFlow mixer, and never
disposed. It plays to the end of the recording, underneath the second, addressable by nothing.
`TTSEventSource` calls the same stop **unconditionally**, which is the entire reason the owner's TTS
path behaves and the voicemail path does not.

### 0.2 ⛔ THREE OF THE ROW'S PREMISES ARE WRONG. Correct them before designing anything.

The dossier is careful and its trap warnings all held. Its **framing** does not, in three places,
and each one points a Builder at the wrong file.

---

#### ⛔ Correction 1 — `PHN-1f` was never supposed to cover this path, and structurally could not have.

The dossier's headline is *"`PHN-1f` SHIPPED the queue that was supposed to prevent it"*, and its
body says *"this is not 'a queue we never built'; it is **a queue that exists and did not cover this
path**, which is a different and more serious thing to diagnose."*

**That is false, and two independent facts falsify it.**

1. **The queue's predicate can never see a voicemail as a blocker.**
   `EventPlaybackService.IsBlockedByAHigherPrioritySource` tests
   `_duckingService.GetPriority(s) >= threshold`, where the threshold is `GvMedia:PreemptAtPriority`
   = **8**. `EventPlaybackRequest.Priority` defaults to **6** and the Web client never overrides it
   — `EventPlaybackController` applies a priority `with` only when the caller sent one, and neither
   `VoicemailPlayer.razor` nor `MessageBubble.razor` sends one. **6 < 8**, for both arms, always.
   This is deliberate: ADR-029 §6.1 sets voicemail and text-TTS at the same 6 because *"They are the
   same class of thing… different numbers would imply an ordering with no meaning."*

2. **Even at equal priorities the wait could not run.** `StartAsync`'s replacement arm tears the
   first playback down **inside `_gate`, before the second playback's acquisition task is
   started**. `WaitForClearAirAsync` runs later, on that task, after acquisition. By then the first
   voicemail has already left the ducking set. There is nothing left to wait for.

**What `PHN-1f` actually shipped** is the mirror of ADR-029 §6.2 **rule 2** — a playback starting
while an *announcement* at ≥ 8 is already sounding. Attended-vs-attended is **rule 1**, and rule 1
shipped with the single-slot replacement arm in the earlier PRs of the same arc.

⭐ **So the honest framing is not "a queue that did not cover this path". It is "the wrong shipped
mechanism was suspected."** The mechanism that owns this path is the replacement arm, and **the
replacement arm works** — it claims the terminal flag, calls teardown, and publishes `Stopped`. The
failure is two layers below it, in the source.

---

#### ⛔ Correction 2 — the second voicemail must NOT wait. The row's own success criterion is the wrong one.

The dossier and the dispatch both say: *"⭐ **Assert the PRESENCE of serialisation** — the second
waits, then plays — not the absence of an error."*

**For this path that is the opposite of the contract.** ADR-029 §6.2 rule 1, verbatim:

> **1. Attended vs attended → replace.** Starting a new attended playback **stops the in-flight one
> first**. Pressing play on a text while a voicemail plays switches to the text. The user just
> expressed a fresh intent; **queueing behind 40 seconds of voicemail would be baffling** and mixing
> would be unintelligible.

A build in which the second voicemail waits for the first to finish would **satisfy the row's stated
assertion and violate the ADR**. ⛔ **Do not build a queue here.** The correct assertion is:

> **The second voicemail starts promptly and the first goes silent.** One voice, and it is the new one.

"Waiting, then playing" remains correct for `PHN-1f`'s case — a voicemail behind a **doorbell**. It
is wrong for a voicemail behind a voicemail. The two are different rules and the row conflated them.

---

#### ⛔ Correction 3 — the two reported behaviours are ONE behaviour. The owner's TTS ruling is evidence FOR the fix, not a constraint against it.

The dossier splits the report into two rows of a table and warns that *"simultaneity and preemption
are opposite failures and a fix for one can create the other."* That warning is sound in general and
does not apply here.

Both arms enter **the same method** (`EventPlaybackService.StartAsync`), take **the same single
slot**, carry **the same priority**, and are governed by **the same ADR rule**. They are one
behaviour with two implementations, and the owner has confirmed by ear that the implementation on
the TTS arm is the one he wants.

⭐ **So the ruling is not a hazard to design around — it is the specification.** "Make voicemail do
what TTS already does" is the whole fix, and it is stated that literally in Task 1.

---

### 0.3 ⭐ THE DIAGNOSIS, and how it was established

The row offered three candidates. The answer is **(b)** — *"it serialises **requests** while two
**sources** still reach the mixer"* — with one correction to the row's wording: the failure is not in
the queue and **not in `EventPlaybackService` at all.** That class does everything right.

**`TearDownAsync`'s first statement disarms the guard that its last statements depend on.**

The full chain, every symbol verified against `main` at `48912474`:

| # | What happens | Where |
|---|---|---|
| 1 | Voicemail B's `StartAsync` takes `_gate`, finds voicemail A in `_current`, claims A terminal, calls `TearDownAsync(A)` | `EventPlaybackService.StartAsync`, replacement arm |
| 2 | **`TearDownAsync`'s FIRST line is `playback.Cancel()`** | `EventPlaybackService.TearDownAsync` |
| 3 | `Playback.Cancel()` is `_cts.Cancel()`, and `playback.Token` is exactly what `AcquireAndPlayAsync` passes to `source.PlayAsync(token)` | `EventPlaybackService.Playback.Cancel` |
| 4 | `AudioFileEventSource.PlayCoreAsync` built `_playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` from that token, so it is now cancelled | `AudioFileEventSource.PlayCoreAsync` |
| 5 | `AwaitCompletionAsync` is parked on that token. Its filter is `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` — **false**, because that is the token just cancelled — so the exception propagates | `AudioFileEventSource.AwaitCompletionAsync` |
| 6 | ⛔ **`PlayWithSoundFlowAsync`'s `catch (OperationCanceledException)` sets `_isPlaybackActive = false` and does nothing else.** Its comment says `// Playback was stopped`. **Nothing stopped it.** | `AudioFileEventSource.PlayWithSoundFlowAsync` |
| 7 | `TearDownAsync` → `ReleaseSourceAsync` → `await _duckingService.StopDuckingAsync(source)`, which awaits `ApplyFadeAsync` — a stepped fade of `Audio:DuckingReleaseMs` = **500 ms shipped**, on a token unrelated to the playback's | `EventPlaybackService.ReleaseSourceAsync`, `DuckingService.ApplyFadeAsync` |
| 8 | `await source.StopAsync()` → `StopCoreAsync` reads `_isPlaybackActive` — **false** — and **skips `SoundFlowPlaybackService.StopAsync`** | `AudioFileEventSource.StopCoreAsync` |
| 9 | `await source.DisposeAsync()` → `DisposeAsyncCore` → **identical guard, identical skip** | `AudioFileEventSource.DisposeAsyncCore` |
| 10 | The `SoundPlayer` is never `Stop()`ed, never `MasterMixer.RemoveComponent`ed, never `Dispose()`d. It plays voicemail A to the end of the file, under B | `SoundFlowPlaybackService.StopAsync`, not reached |

⭐ **Step 7 is what makes this deterministic rather than a race.** A 500 ms fade sits between the
cancellation and the guard read. There is no timing question to argue about.

⭐ **And step 9 is provably unreachable as a backstop even without step 7**, because `StopCoreAsync`
*awaits `_playbackTask` to completion* after its guard. Once that await returns, step 6 has
definitively run. **The voicemail arm has no working backstop at any layer.**

**How this was established, so the next reader can weigh it:**

- The chain above was read directly from the source on `main` at `48912474`.
- It was **independently reproduced** by building a standalone .NET 10 model of the exact control
  flow — fire-and-forget play task, linked-CTS completion wait, cancel-then-fade-then-stop. With the
  shipped 500 ms fade: **100/100 iterations reached neither stop path.** With a 0 ms fade:
  **200/200 stopped correctly.** The TTS shape stopped correctly in **100% of both** runs.
  ⚠ **That model is evidence about the shape, not about the appliance.** It is recorded because it
  makes the fade's role measurable; it is **not** a gate and §2.3 explains why it must not become one.

#### ⛔ Two plausible leads that are NOT this bug. Do not spend time on them.

1. **It is not the `SoundFlowMasterMixer` "removed from mixer" lie.** `CLAUDE.md` records that
   `SoundFlowMasterMixer.RemoveSource` logs a removal while only mutating a `List<IAudioSource>`.
   **That is still true today and it is irrelevant here.** The real detach is
   `AudioPlaybackDevice.MasterMixer.RemoveComponent` — SoundFlow's own mixer, a *different object* —
   and the event-source path never touches `IMasterMixer` at all. `EventPlaybackService`'s own class
   doc says it never calls `AddSource`, and that is accurate.
2. **It is not an `AUD-2` key mismatch.** `AudioFileEventSource` registers under `_playbackId`
   (minted once in `InitializeAsync`, never re-minted) and stops under `_playbackId`. Same field
   both ways. `PlaybackKeyLintTests` deliberately exempts event sources and its remark says why.

### 0.4 ⛔ THE ROW UNDERSTATES ITS OWN SEVERITY. This is not "two voicemails" — it is "nothing can stop a voicemail."

Every stop in the attended-playback seam reaches the same disarmed guard. All of these are affected:

| Stop path | Reaches teardown via | Consequence today |
|---|---|---|
| A second attended playback | `StartAsync` replacement arm | **The observed defect.** Two voices. |
| The **user's own Stop button** | `StopAsync` | ⛔ The voicemail keeps playing. `Stopped` is published; the room disagrees. |
| **Doorbell preemption** (ADR §6.2 rule 2) | `OnDuckingStateChanged` → `StopAsync` | ⛔ The announcement talks over the voicemail instead of replacing it. **This is `PHN-2` UAT check #10, which is DEFERRED — this plan predicts it FAILS when run.** |
| **`GvMedia:MaxPlaybackSeconds` cap** | `ArmDurationCap` callback → `StopAsync` | ⛔ ADR-029 §7.1 calls this *"THE guarantee"* — the stop that needs no client cooperation. It does not stop voicemail audio. |
| **The `/sleep` edges** (ADR §16.5) | `SleepService` → `StopAsync` | ⛔ Audio continues on a dark panel with no stop control on screen. |
| **The last-circuit backstop** (`D30`) | `AttendedPlaybackCircuitHandler` → `StopAsync` | ⛔ A browser reload does not silence it. |
| **Natural end of content** | `OnSourceCompleted` → `TearDownAsync` | The audio ends correctly (the file ran out), but **the player and its mixer component are never removed** — one leak per voicemail played, for the life of the process. |

⚠ **The last row is a standing resource leak on an appliance with weeks of uptime**, and it is worth
recording as a *hypothesis only* that it may interact with the known long-running capture-device
degradation. **Do not claim that connection in the PR** — nothing here measures it.

⭐ **Say all of this in the PR body.** The row was filed at P1 for "two voicemails". On this
diagnosis, the user's own Stop button, the doorbell rule, and the ADR's headline guarantee are all
inert on the voicemail path. **Nothing in this plan changes the fix, but the owner should be told
what he is actually getting.**

### 0.5 ⭐ Why every test in the suite is blind to it, and why that is not an oversight to scold

`AudioFileEventSourceTests` has 27 tests. **All of them construct the source with no playback
service** — `CreateSource` and `CreateSourceFromStream` both omit the argument. With
`_playbackService == null`, `PlayCoreAsync` takes the `PlaybackLoopAsync` **silent-simulation**
branch, `_isPlaybackActive` is **never true in any test**, and the guard is entirely uncovered.

`EventPlaybackServiceTests.ASecondStartReplacesTheFirst_AndTheFirstIsTornDown` — the **only**
replacement test in the suite — uses `SpeechRequest()` twice with a `FakeEventSource`, and asserts
`first.StopCalls == 1` / `first.DisposeCalls == 1`. **Both assertions are true today.** The service
does call them. The fake then does what the fake does.

⭐ **That is exactly the `PATH` versus `SOUND` split `PHN-2`'s plan §2.2 named in advance**, and it
is also **the single-path test the dossier warned about**: the one replacement test in the tree
exercises the arm that works.

### 0.6 ⛔ HOW THIS FIX PRESERVES TTS PREEMPTION — the question the dispatch demands be answered explicitly

**It preserves it by not touching it, and by not adding the thing that would break it.**

- ⛔ **No serialisation layer is added.** The naive fix the dispatch warns about — "serialise all
  event audio" — would make the second TTS *wait* instead of cancelling, suppressing behaviour the
  owner has explicitly endorsed. **This plan adds no queue, no arbitration, no mutual exclusion, and
  no new state.** It **deletes** a guard.
- ⛔ **No file that TTS depends on is edited.** The production diff is confined to
  `AudioFileEventSource`. `TTSEventSource`, `EventPlaybackService`, `DuckingService`,
  `SoundFlowPlaybackService`, `AudioSourceBase` and `EventAudioSourceBase` are **not modified**.
  A diff touching any of them has gone somewhere this plan did not send it.
- ⭐ **The fix's own definition of correct IS the TTS arm.** Task 1 makes
  `AudioFileEventSource.StopCoreAsync` call the playback service the way `TTSEventSource.StopCoreAsync`
  already does. The two arms converge rather than diverging, so "voicemail serialises" and "TTS
  preempts" stop being two behaviours that could drift apart and become **one behaviour with one
  implementation shape**.
- ✅ **And it is checked at the cabinet in the same sitting**, deliberately back-to-back — §3 U2 is
  the TTS control and it is **not optional**.

### 0.7 ⛔ What this row is NOT

1. ⛔ **Not a queue.** Correction 2. ADR-029 §6.2 rule 1 rejects queueing for this case in as many
   words.
2. ⛔ **Not a ducking change.** `AUD-2` owns whether the radio ducks; this row owns how many things
   play. `AUD-2` is closed and confirmed by ear. ⚠ **And the dossier's warning stays in force: a
   post-`AUD-2` re-listen sounding quieter is not evidence this is fixed.** It already predicted its
   own false-clear once and the owner's re-run walked past it.
3. ⛔ **Not a change to `TTSEventSource`.** §0.6.
4. ⛔ **Not a `Radio.Web` change.** No component, no razor file, no DTO. The UI is already correct:
   it publishes the right request and renders the right snapshot.
5. ⛔ **Not a fix for ADR §6.2 rule 3.** Sub-8 announcements still mix over attended playback. That
   is the ADR's recorded wart and fixing it is a queue across every `IAnnouncementService` caller.
6. ⛔ **Not a refactor of `SoundFlowPlaybackService`.** §2.2 establishes, with evidence, that it
   cannot be driven device-free — and that is a reason to be honest in the test file, not a reason
   to re-architect a live shared audio class inside a P1 bug fix.
7. ⛔ **Not the `SoundFlowMasterMixer` comment defect.** §0.3. It is real, it is unrelated, and it
   should not be swept into this diff. If the Builder wants it fixed, file a row.

### 0.8 Constraints found while planning — `C-906` continues `PHN-9`'s numbering

---

**`C-906` — ⛔ `AudioFileEventSource` VIOLATES A CONTRACT ITS OWN BASE CLASS DOCUMENTS. This is the
repo's signature failure class, and it is the reason the bug survived review.**

`AudioSourceBase.StopAsync` carries a doc comment stating that teardown is **deliberately not** gated
on `State` being `Playing`/`Paused` — a past bug, removed — and that *"every `StopCoreAsync`
implementation is null-guarded and idempotent."*

`AudioFileEventSource.StopCoreAsync` is **activity-guarded, not null-guarded**, and its guard's value
is destroyed by the cancellation that precedes it. The base class promises an invariant one of its
two event-source implementations does not honour.

⭐ **This is PR #469's shape one layer down.** #469 was a `State = Ready` assignment that made a
`Playing` branch statically unreachable and silently disabled BT song recognition. Here it is not
`State` — it is a **private mirror of `State`**, which is worse, because the base class's guarantee
reads as covering it.

⛔ **And a second false comment sits at the scene:** `PlayWithSoundFlowAsync`'s cancellation handler
says `// Playback was stopped`. It was not. That sentence is why the guard below it looks safe.
**`CLAUDE.md` § Pre-Merge Review exists for exactly this, and this is the fifth logged instance.**
Task 2 corrects both.

---

**`C-907` — ⚠ `EventPlaybackService.ArmDurationCap`'s remark got HALF of this right and then drew the
wrong conclusion. Correct it in this PR; do not leave it standing.**

The remark already says, accurately:

> *"the `OperationCanceledException` arm below it only clears `_isPlaybackActive`. Nothing on that
> path stops the player — in `AudioFileEventSource` only `StopCoreAsync` and `DisposeAsyncCore` call
> `SoundFlowPlaybackService.StopAsync`."*

and then concludes:

> *"What actually stops audio is `TearDownAsync -> ReleaseSourceAsync`, and `StopAsync` is the public
> door to it."*

⛔ **The conclusion does not follow from the premise, and the missing step is that `TearDownAsync`
cancels first.** The remark identified the exact flag, the exact handler and the exact two methods —
and then trusted that teardown would reach them. **This is a comment that reasoned its way to within
one line of the bug and stopped.**

⚠ It is *load-bearing* prose: it is the argument for why the cap is a dispatched timer rather than a
`CancelAfter`. **That argument is still correct** — a `CancelAfter` would be strictly worse. Only the
final claim about teardown needs correcting. Task 3 does that and no more.

---

**`C-908` — ⚠ `PlayFileAsync` calls `StopAsync(sourceId)` on entry. It is NOT a safety net here, and
it must not be mistaken for one.**

`SoundFlowPlaybackService.PlayFileAsync` opens with `await StopAsync(sourceId, cancellationToken)` —
*"Stop any existing playback for this source."* It is keyed by `sourceId`, and **every
`AudioFileEventSource` instance mints its own `_playbackId`**, so voicemail B's play call stops
*voicemail B's* nonexistent prior player and leaves voicemail A's running. It cleans up a
replay of the same source instance, nothing more.

---

**`C-909` — the fix newly DEPENDS on `SoundFlowPlaybackService.StopAsync` being a safe no-op on an
unregistered id, and that dependency is currently unpinned.**

Before this row the guard meant `StopAsync` was never called with a stale or unknown key. After it,
it will be — on every stop of a source that never played, and on the second of the two calls
(`StopCoreAsync` then `DisposeAsyncCore`).

It **is** safe: `StopAsync` does `TryGetValue` on `_activePlayers` and `_activeComponents`, the
`Remove` calls on `_baseVolumes`/`_duckingMultipliers` are no-ops for absent keys, and the work is
all behind `if (player != null)`. ⚠ **But it also calls `ThrowIfDisposed()` and can throw
`ObjectDisposedException`.** Task 1 wraps both call sites — `TTSEventSource` already does, and
`AudioFileEventSource` currently wraps neither. Task 5 pins the no-op property.

---

**`C-910` — ⚠ `_isPlaybackActive` has a THIRD reader, and this row deliberately leaves it alone.**

`OnVolumeChanged` also gates on the flag. It is left as-is for two reasons, both worth stating so
the next reader does not think it was missed:

- **Making it unconditional would be a regression.** `SoundFlowPlaybackService.SetVolume` **writes**
  `_baseVolumes[sourceId]` before doing anything else, so an unconditional call would create a
  dictionary entry for a source that never played — the orphaned-entry class `AUD-2` was about.
- **The failure directions are not comparable.** A dropped volume change is a cosmetic miss on a
  source that is already stopping. A skipped stop is a voice nothing can silence. `PHN-2` UAT
  check #8 (*volume changes voicemail loudness*) **PASSED**, so this reader is working in the case
  that matters.

⛔ **Do not delete the field, and do not "tidy" the remaining reader.** Task 2 instead documents that
it now has exactly one reader and why that one is safe — so the next engineer cannot re-add the
guard to a stop path by pattern-matching.

---

**`C-911` — ⚠ There is a SECOND window in the same flag, narrower and untouched by this fix. Name it, do not chase it.**

`_isPlaybackActive = true` is assigned only **after** `await PlayFileAsync(...)` returns — by which
point `soundPlayer.Play()` has run and the component is already in the mixer. So there is a window in
which audio is live and the flag still reads `false`.

**Task 1 closes this window as a by-product**, because the stop paths stop consulting the flag at
all. It is recorded because it is the *reason* the flag was never a valid proxy for "is audio
reaching the speakers" — `SoundFlowPlaybackService._activePlayers` is the only thing that knows that,
and it already exposes it.

---

## 1. Tasks

### Task 1 — ⭐ THE FIX. `AudioFileEventSource`: stop the audio unconditionally, exactly as `TTSEventSource` does.

**File:** `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs`

**1a. `StopCoreAsync`.** Replace the guarded block with an unconditional, exception-wrapped call.

⚠ **Keep it BEFORE the `await _playbackTask.WaitAsync(...)` join** — stop the audio, then reap the
task. Reversing them re-opens the window by a second.

```csharp
    // ⛔ UNCONDITIONAL, and the missing condition is the whole of PHN-10. This used to read
    // `if (_playbackService != null && _playbackId != null && _isPlaybackActive)`, and
    // _isPlaybackActive was ALWAYS false by the time control reached here on every real stop path.
    //
    // EventPlaybackService.TearDownAsync's FIRST statement is playback.Cancel(); _playbackCts is a
    // linked source over that token; AwaitCompletionAsync's filter does not swallow it; and
    // PlayWithSoundFlowAsync's catch (OperationCanceledException) clears the flag while stopping
    // nothing. TearDownAsync then awaits a ducking release fade (Audio:DuckingReleaseMs, 500 ms
    // shipped) before it calls us, so the flag is deterministically false rather than racily so.
    //
    // The consequence was not "two voicemails" but "nothing can stop a voicemail": the user's Stop
    // button, doorbell preemption, the GvMedia:MaxPlaybackSeconds guarantee, the /sleep edges and the
    // last-circuit backstop all funnel through the same disarmed guard.
    //
    // ⚠ SoundFlowPlaybackService.StopAsync is a safe no-op on an unregistered id — it TryGetValues
    // and does nothing when the key is absent — which is precisely the "null-guarded and idempotent"
    // property AudioSourceBase.StopAsync's contract already assumes of every StopCoreAsync. It also
    // calls ThrowIfDisposed, hence the catch: an escaping ObjectDisposedException here would skip
    // OnPlaybackCompleted(UserStopped) below. TTSEventSource.StopCoreAsync wraps for the same reason.
    if (_playbackService != null && _playbackId != null)
    {
      try
      {
        await _playbackService.StopAsync(_playbackId, cancellationToken);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "Error stopping audio file event playback through SoundFlow");
      }

      _isPlaybackActive = false;
    }
```

**1b. `DisposeAsyncCore`.** The same change, without a `cancellationToken`.

⚠ **This is not belt-and-braces — it is the backstop the voicemail arm has never had.**
`TTSEventSource.DisposeAsyncCore` gates on `_playbackService.IsPlaying(Id)`, a **live** query.
⛔ **Do not copy that form here.** `IsPlaying` returns `player.State == PlaybackState.Playing`, so a
**paused** voicemail would not be stopped or detached — trading this bug for a quieter one. An
unconditional call has no such hole.

```csharp
    // ⛔ UNCONDITIONAL — see StopCoreAsync. This guard was doubly dead: StopCoreAsync awaits
    // _playbackTask to completion before returning, so by the time disposal runs, the cancellation
    // handler that clears _isPlaybackActive has PROVABLY run. The voicemail arm therefore had no
    // working backstop at any layer, where TTSEventSource has one.
    //
    // ⚠ Deliberately NOT the IsPlaying(...) form TTSEventSource uses. IsPlaying answers
    // `player.State == PlaybackState.Playing`, so a PAUSED source would be left registered and still
    // attached to the SoundFlow mixer. Unconditional has no such hole and costs a dictionary miss.
    if (_playbackService != null && _playbackId != null)
    {
      try
      {
        await _playbackService.StopAsync(_playbackId);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "Error stopping audio file event playback during disposal");
      }

      _isPlaybackActive = false;
    }
```

⛔ **`OnVolumeChanged` is NOT changed.** `C-910`.

---

### Task 2 — the two false comments at the scene

**File:** the same.

**2a. The cancellation handler.** Its `// Playback was stopped` is the sentence that made the guard
below it look safe.

```csharp
    catch (OperationCanceledException)
    {
      // ⚠ NOTHING HAS BEEN STOPPED HERE, and an earlier version of this comment said "Playback was
      // stopped", which is the sentence that hid PHN-10. Cancellation unblocks AwaitCompletionAsync
      // and ends this task; the SoundFlow SoundPlayer is untouched and still attached to the mixer.
      // Only SoundFlowPlaybackService.StopAsync detaches it, and only StopCoreAsync and
      // DisposeAsyncCore call that.
      //
      // ⚠ This flag is therefore NOT a proxy for "audio is reaching the speakers" and must never be
      // used to gate a stop again. It is cleared here so that OnVolumeChanged — since PHN-10 its ONE
      // remaining reader — stops writing volumes for a source that is going away.
      _isPlaybackActive = false;
    }
```

**2b. The field declaration.** Give the flag a comment that says what it is for, so its scope cannot
quietly grow back.

```csharp
  // ⛔ ONE READER ONLY: OnVolumeChanged. Do NOT gate a stop, a dispose or a detach on this.
  //
  // It is a cached mirror of state SoundFlowPlaybackService._activePlayers owns authoritatively, and
  // it is wrong in BOTH directions: it reads false while audio is live (it is assigned only after
  // PlayFileAsync returns, by which point the player is already in the mixer), and the cancellation
  // handler clears it without stopping anything — which is PHN-10, where it disabled every stop path
  // in the attended-playback seam. It survives only because the alternative for OnVolumeChanged is
  // worse: SoundFlowPlaybackService.SetVolume WRITES _baseVolumes[sourceId] before checking
  // anything, so an unconditional call would orphan an entry for a source that never played.
  private bool _isPlaybackActive;
```

---

### Task 3 — `C-907`: correct `ArmDurationCap`'s conclusion, and only its conclusion

**File:** `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs`

⛔ **Change nothing else in that remark.** Its argument for a dispatched timer over a `CancelAfter`
is correct and load-bearing. Replace only the paragraph beginning *"What actually stops audio is"*:

```csharp
  /// What actually stops audio is SoundFlowPlaybackService.StopAsync, reached through
  /// TearDownAsync -> ReleaseSourceAsync -> source.StopAsync / source.DisposeAsync, and StopAsync is
  /// the public door to it. So a timer whose callback dispatches a stop.
  ///
  /// ⚠ Until PHN-10 that chain was BROKEN for AudioFileEventSource, and the paragraph above had
  /// already identified every ingredient without drawing the conclusion. TearDownAsync's FIRST
  /// statement is playback.Cancel(), which fires the very OperationCanceledException arm named above
  /// — so the _isPlaybackActive flag that AudioFileEventSource's StopCoreAsync and DisposeAsyncCore
  /// then consulted was always false, and neither reached SoundFlowPlaybackService.StopAsync. The
  /// cap, the Stop button, preemption, the /sleep edges and the circuit backstop were all inert on
  /// the voicemail path. PHN-10 removed those guards; the reasoning above is what identified them.
```

---

### Task 4 — `C-906`: the base-class contract now holds, so say so where it is claimed

**File:** `src/Radio.Infrastructure/Audio/Sources/AudioSourceBase.cs`

`StopAsync`'s doc comment asserts that every `StopCoreAsync` implementation is *"null-guarded and
idempotent."* Until Task 1 that was false of `AudioFileEventSource`. Append one paragraph recording
that the claim is now enforced by a lint rather than by hope:

```csharp
  /// ⚠ "Null-guarded and idempotent" is a CONTRACT ON IMPLEMENTORS, not a description of what the
  /// base class enforces, and AudioFileEventSource broke it until PHN-10: its StopCoreAsync was
  /// ACTIVITY-guarded on a private bool that the caller's own cancellation cleared first, so the
  /// only call that detaches a source from the SoundFlow mixer never ran. That is PR #469's shape
  /// one layer down — not State, but a private mirror of it, which is worse because this paragraph
  /// reads as covering it. EventSourceStopIsNotActivityGuardedLintTests is what now checks it.
```

---

### Task 5 — the regression gates

⚠ **Read §2 before writing these.** Neither of them can prove the defect is fixed, and the test file
must say so rather than implying coverage it does not have.

**5a. The lint.** New file `tests/Radio.Core.Tests/EventSourceStopIsNotActivityGuardedLintTests.cs`,
following the four existing precedents in that project (`PlaybackKeyLintTests`,
`LogSafetyLintTests`, `AsyncEventFanOutLintTests`, `TestSeamLabelLintTests`).

**What it asserts:** for every `.cs` file under `src/Radio.Infrastructure/Audio/Sources/`, no `if`
condition that guards a call to a playback service's `StopAsync` may reference a private `bool`
field. It reds today on `AudioFileEventSource`, greens after Task 1, and reds for the **next** source
that reintroduces the shape.

⛔ **Follow the existing lints' root-resolution idiom exactly.** `CLAUDE.md` records that a
tree-scanning test resolving to the wrong root silently scans a copy — the `.claude/worktrees/`
trap. Copy how `PlaybackKeyLintTests` finds the repo root; do not invent a new walk.

**5b. Pin `C-909`.** Add to `SoundFlowPlaybackServiceTransportTests` (which already constructs the
service device-free and documents why that is safe):

```csharp
  [Fact]
  public async Task StopAsync_IsANoOp_WhenNoPlayerIsRegistered()
  {
    // ⭐ PHN-10 made AudioFileEventSource call this UNCONDITIONALLY, on every stop and again on
    // every dispose — so "safe on an unknown key" went from incidental to load-bearing. Before that
    // row the _isPlaybackActive guard meant it was never called with a stale key at all.
    var service = CreateService();

    await service.StopAsync("no-such-source");
  }
```

⚠ **Do not dress this up as coverage of the defect.** It pins a precondition the fix relies on. §2.2
says what it does not reach.

---

### Task 6 — documentation

**6a. `docs/queue/PHN-10.md`** — append a `## ✅ DIAGNOSIS` section recording §0.2's three
corrections and §0.4's severity escalation. ⛔ **Correct the row rather than deleting its wrong
framing** — the dossier's `PHN-1f` premise sent the investigation at the wrong file and the record of
that is worth more than a tidy page.

**6b. `docs/uat/2026-09-09-phn2-sound-uat/RESULT.md`** — check #11's entry gains the diagnosis and,
⛔ **importantly, a note against check #10**: doorbell preemption is `DEFERRED`, and this plan
predicts it would have **FAILED** for the same root cause. It becomes meaningfully runnable only
after this row ships.

**6c. `design/FUTURE-WORK.md`** — per the project's standing rule, record the two residuals §5 names.

⛔ **No `design/INTEGRATIONS.md` change** — no integration surface, protocol or config key moves.
⛔ **No ADR.** This row implements ADR-029 §6.2 rule 1 as already written; it changes no decision.

---

## 2. Test plan

### 2.1 What the automated gates are, and exactly what each proves

| Gate | Proves | Does **not** prove |
|---|---|---|
| `EventSourceStopIsNotActivityGuardedLintTests` (5a) | The source shape that caused this is gone, and reds if it returns | That audio stops. It reads text. |
| `StopAsync_IsANoOp_WhenNoPlayerIsRegistered` (5b) | The precondition Task 1 newly relies on | Anything about the defect |
| The existing suite, unchanged | No regression in the seam's request-level behaviour | It was **green throughout the defect** |

⛔ **`ASecondStartReplacesTheFirst_AndTheFirstIsTornDown` will keep passing and always would have.**
It is green today, with the bug live. **Do not cite it as evidence.**

### 2.2 ⛔ Why there is no behavioural unit test, established rather than asserted

The Builder will be tempted to write one. **Here is the check, already done, so it is not re-run:**

`SoundFlowPlaybackService` **is** constructible device-free — `SoundFlowPlaybackServiceTransportTests`
does it, and documents that the MiniAudio device is not created until `InitializeAsync`, which no
test calls. So the obvious plan is: build one, hand it to an `AudioFileEventSource`, play, cancel,
stop, assert.

⛔ **It cannot work, and the reason is two early returns in `PlayFileAsync`:**

```csharp
    if (engine == null)          { …; return false; }
    if (playbackDevice == null)  { …; return false; }
```

Against an un-initialised engine both are null, `PlayFileAsync` returns `false`,
`PlayWithSoundFlowAsync` takes its `if (!success)` arm, and **`_isPlaybackActive` is never assigned
`true`**. The state the bug lives in is unreachable without a real audio device.

⭐ **This is a VERIFIED untestability claim, not an asserted one, and the distinction is the point.**
`TEST-2` / ADR-030 is this repo's worked example of the opposite: a doc comment asserted a seam
*"cannot be exercised directly in a unit test"*, a queue row cited it as authority and sat open for
four weeks — and a `Mock<SoundComponent>` test proving otherwise was **already green in CI**. So the
rule this plan follows is: **go and check, then write down what you found and how.** The two lines
above are the finding.

⛔ **And do NOT promote §0.3's standalone control-flow model into the suite.** It reproduces the
shape faithfully and it was genuinely useful for diagnosis, but a test of a hand-built model is a
test of the model. It would be a green check that asserts nothing about `AudioFileEventSource` while
looking exactly like one that does — which is the failure mode this entire row exists to punish.

### 2.3 Build and test commands

```bash
dotnet build RadioConsole.sln -c Release --no-incremental > /tmp/build.log 2>&1; echo "exit=$?"
grep -E "^\s+[0-9]+ Warning\(s\)" /tmp/build.log     # must equal the 47-warning / 0-error baseline

dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log        # read the PER-PROJECT summary lines
```

⛔ **Never pipe either into `tail`.** ⛔ **`--no-incremental` is mandatory for the warning count** —
an incremental Release build reports `0 Warning(s)`, and a clean-looking gate is exactly what a
broken one looks like. Known-failing on Windows and not regressions: four `SrcVariableResamplerTests`,
`NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.

---

## 3. ⭐ Owner UAT at the cabinet — the only thing that closes this row

⛔ **This row is not closable by a green suite.** Nine automated checks reported nothing while the
owner found three defects in fifteen minutes. Deploy first
(`./deploy/Deploy-ToLinux.ps1 -TargetHost radio`), then read this at the cabinet.

**Pre-flight, and it is not optional.** The last sitting nearly failed for the wrong reason:

- ⚠ **Confirm the console is NOT muted and the volume is up.** A muted console is indistinguishable
  from a broken audio path through every check here.
- ⚠ **Confirm the radio is playing**, so ducking is observable and a second voice is obvious.
- ✅ **Confirm the deploy landed:** `curl -s http://radio:5000/api/health/version` and
  `curl -s http://radio:5002/api/health/version` agree on the new `gitSha`.

---

### **U1 — ⛔ THE ROW. Two voicemails.**

1. Open `/phone`, expand a voicemail with **at least 20 seconds** of audio. Press play. Let it reach
   a recognisable point.
2. **Before it ends**, expand a *different* voicemail and press play.

| | |
|---|---|
| ✅ **PASS** | **You hear ONE voice — the second voicemail.** The first stops when the second starts. |
| ⛔ **FAIL** | You hear two voices, or the first keeps going, or the second never arrives. |

⛔ **The second voicemail must NOT wait for the first to finish.** ADR-029 §6.2 rule 1 makes a fresh
press of play a *replacement*, not a queue entry — *"queueing behind 40 seconds of voicemail would be
baffling."* **A build in which the second waits is a FAIL**, even though it also produces one voice.
⚠ The row's original wording asked for a wait; §0.2 Correction 2 is why that was wrong.

⚠ **Ducking is not what is being checked.** It works now (`AUD-2`, confirmed by ear) and it makes the
second voice *quieter over the radio*, not absent. ⛔ **Listen for the number of voices, not the
volume.** The dossier predicted this exact false-clear once already and it held.

---

### **U2 — ✅ THE CONTROL. Two TTS texts, in the same sitting, back to back.**

⛔ **Not optional, and it must be run in the same sitting as U1.** It is what proves the fix did not
buy U1 by breaking behaviour the owner has explicitly endorsed.

1. On `/phone`, press the speak button on an inbound text with a reasonably long body.
2. **Before it finishes**, press speak on a *different* inbound text.

| | |
|---|---|
| ✅ **PASS** | The first utterance **stops** and the second starts. Exactly what you ruled correct on 2026-09-09. |
| ⛔ **FAIL** | Two voices, **or** the second waits for the first, **or** the second never speaks. |

⚠ **A "waits its turn" result here is a FAIL and a serious one** — it means a serialisation layer was
added despite §0.6, and the ruling was overturned by accident.

---

### **U3 — ⭐ THE STOP BUTTON. This is expected to be newly fixed and nobody has ever checked it.**

Per §0.4 the Stop button has been inert on the voicemail path for as long as the seam has existed.

1. Play a long voicemail. Let it get going.
2. Press the transport's **Stop**.

| | |
|---|---|
| ✅ **PASS** | The room goes quiet **immediately**, and the radio returns to full volume. |
| ⛔ **FAIL** | The transport shows stopped while the voicemail keeps playing. |

⚠ **If this FAILS while U1 passes, stop and report it** — it would mean Task 1 landed on the
replacement path only, which is not possible from the diff this plan describes, so something else is
going on.

---

### **U4 — ⭐ DOORBELL PREEMPTION. `PHN-2` check #10, deferred since it was written.**

⚠ Needs no doorbell hardware — the endpoint is the doorbell.

1. Start a long voicemail with the radio on.
2. From any machine on the LAN:
   ```bash
   curl -X POST http://radio:5000/api/notifications/announce \
     -H 'Content-Type: application/json' \
     -d '{"Message":"Someone is at the door"}'
   ```

| | |
|---|---|
| ✅ **PASS** | The voicemail **stops**, the announcement is **intelligible**, and afterwards the radio returns to **full volume**. |
| ⛔ **FAIL** | The announcement talks over the voicemail, or the radio stays ducked afterwards. |

⭐ **This plan predicts U4 was failing before this fix**, for the same root cause. If it now passes,
`PHN-2` check #10 flips `DEFERRED → PASS` and the arc's last preemption debt is discharged. Record
the result **either way** — a deferred check must not decay into a pass by silence.

---

### **U5 — the long-running check, optional but valuable**

Play six or seven voicemails to their natural end over a few minutes, then:

```bash
curl -s http://radio:5000/api/audio/debug/players    # or whatever the diagnostics route is named
```

⭐ Per §0.4's last row, every voicemail played used to leak one `SoundPlayer` and one mixer
component permanently. After this fix the count should return to its idle baseline. ⚠ **A
non-baseline count here is a finding, not a failure of U1** — report it and let it be filed.

### **If anything fails**

Capture: the wall-clock time, which voicemails, and
`ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); tail -200 $F'`.
⚠ `journalctl -u radio-api` carries **Warning and above only** since `LOG-11`, so the
`Information`-level lifecycle lines that matter here are in the **file sink**, not the journal.

---

## 4. Docs impact

| File | Change |
|---|---|
| `design/plans/PHN-10-nothing-can-stop-a-voicemail.md` | This plan (Planner) |
| `docs/BUILDER_QUEUE.md` | `PHN-10` row: plan link replaces `_plan TBD_`; warnings kept; §0.2's corrections summarised (Planner) |
| `docs/queue/PHN-10.md` | `## ✅ DIAGNOSIS` section — Task 6a (Builder) |
| `docs/uat/2026-09-09-phn2-sound-uat/RESULT.md` | Check #11 outcome; the check #10 prediction — Task 6b (Builder) |
| `design/FUTURE-WORK.md` | The two §5 residuals — Task 6c (Builder) |
| `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs` | Tasks 1, 2 |
| `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs` | Task 3 — comment only |
| `src/Radio.Infrastructure/Audio/Sources/AudioSourceBase.cs` | Task 4 — comment only |
| `tests/Radio.Core.Tests/EventSourceStopIsNotActivityGuardedLintTests.cs` | Task 5a — new |
| `tests/Radio.Infrastructure.Tests/Audio/SoundFlow/SoundFlowPlaybackServiceTransportTests.cs` | Task 5b |

⛔ **No file under `src/Radio.Web/` is touched.** If a diff shows one, the change went somewhere this
plan did not send it. Same for `TTSEventSource.cs`, `DuckingService.cs` and
`SoundFlowPlaybackService.cs` — §0.6.

---

## 5. Residuals this row does NOT take

1. **`TTSEventSource.DisposeAsyncCore` has the paused-source hole `C-909`/Task 1b describes.** It
   gates on `_playbackService.IsPlaying(Id)`, which is false for a paused player, so a TTS source
   disposed while paused is left registered and attached. ⚠ **Narrower than `PHN-10`** — `StopCoreAsync`
   is unconditional there, so it only bites on a dispose that no stop preceded. Worth a row; not
   worth widening this diff into the arm the owner just confirmed works.
2. **`SoundFlowMasterMixer.RemoveSource` still logs a removal it does not perform.** `CLAUDE.md`
   records it, it is still true, and it is genuinely unrelated to this defect (§0.3). It has already
   cost one fix that landed a layer too high (`03a6fea`). Worth a row of its own.

⛔ **Neither belongs in this PR.** Propose them as rows; do not append them to `PHN-10`.

---

## 6. Definition of done

- [ ] Release build at the **47-warning / 0-error** baseline, measured `--no-incremental`.
- [ ] Full suite green apart from the known-failing set (§2.3).
- [ ] The lint (5a) **red before Task 1, green after** — demonstrate both, in that order.
- [ ] Pre-merge review, with `C-906` and `C-907` explicitly on its list: **do the comments this PR
      writes assert only what the code does?** This row exists because two of them did not.
- [ ] Deployed to `radio`, both `/api/health/version` endpoints agreeing on the new SHA.
- [ ] ⛔ **§3 U1 and U2 both run by the owner, in one sitting.** U1 alone does not close this row —
      the whole risk of the fix is that it buys U1 by breaking U2.
- [ ] U3 and U4 run and recorded either way. U4's result updates `PHN-2` check #10.
