# PLAN — `PHN-1f` · ADR-029 PR 5b: the wait-then-play queue (`D28`), and the ducking-args change that makes its wake correct

- **Row:** `PHN-1f` (`docs/BUILDER_QUEUE.md` § Queue). 🔴 **P0, `O6`.** Sixth of the eight-PR ADR-029 arc.
- **Branch:** `feat/phn-1f-mirror-case-queue`
- **Planned against:** `main` at **`4ec0fb85`** (`PHN-1e`, [#561](https://github.com/mmackelprang/RTest/pull/561)), with `7a6911ce` (ADR-029 Amendment 2) beneath it.
- **Decision:** owner **`D28`** — pressing play while a source at or above `GvMedia:PreemptAtPriority` is already talking must **wait and then play** — not mix (today), not refuse (rejected as *"press play, get an error, nothing happens"*). `docs/HANDOFF-GA-PUNCH-LIST.md:1303`.
- **Design input:** [`design/plans/PHN-1e-server-owned-state-and-the-queue.md`](PHN-1e-server-owned-state-and-the-queue.md) **§5**, which settled all five of `PHN-1d` §5's open questions. **This plan re-verified every one against merged code rather than inheriting it** — see §0.3. Four survive; the fifth (`Q5`) is **materially incomplete** and §0.4 **C-57** is the correction.
- **Estimate:** 2.5–3 d. **HIGH risk**, `PHN-1d`'s review posture: third rewrite of the same two ducking tripwire files in three PRs, on a shared audio service with two live subscribers.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`PHN-1d` shipped the **stop** direction of ADR-029 D5 §6.2 rule 2: a source starting at or above
`GvMedia:PreemptAtPriority` stops an in-flight attended playback. This row ships the **mirror**
direction, which is the same rule read symmetrically: a playback *starting* while such a source is
already sounding must not add a second voice. `D28` says it **waits and then plays**. `PHN-1e` shipped
the visibility that made the wait shippable — a `/hubs/audio` broadcast, server-owned state, and a
snapshot a client can re-attach to — because *"a queue whose waiting state nobody can see is the same
'nothing happens' complaint, for longer."* This row adds `EventPlaybackState.Waiting`, the wait, its
staleness bound, and the wake — and the wake is the bulk of the work, because **the raise it needs does
not exist** and has to be made to.

### 0.2 The shape, stated first — because four of the five design questions fall out of it

**A waiting playback IS the current playback, in a new state. There is no pending slot.**

`PHN-1d` §5 framed the queue as *"a pending slot that `StopAsync` must also resolve and `Current` must
also report"*. Against the shipped seam that framing costs a great deal and buys nothing: there is
**one** attended playback by construction (ADR §7.4, §8.1), and a playback that is waiting is one that
has not started producing audio yet — exactly like `Preparing`, which the seam already models.

```
StartAsync            → mints the playback, installs it as _current, publishes Preparing   (unchanged)
AcquireAndPlayAsync   → fetches or synthesises                                             (unchanged)
                      → ⭐ NEW: if the air is not clear, publish Waiting and await it
                      → _gate.WaitAsync … TryAdopt … StartDuckingAsync … PlayAsync … Playing (unchanged)
```

Four things come free rather than being built:

| Falls out of the shape | Why, verified in merged code |
|---|---|
| **Replace semantics** | `StartAsync`'s existing replacement arm (`EventPlaybackService.cs:237-244`) tears down whatever is in the slot, waiting or playing, and publishes `Stopped` for it |
| **`StopAsync` resolves it** | `StopAsync` resolves `_current` by id (`:289-301`); the waiting playback *is* `_current` |
| **`Current` reports it** | Same reason (`:190-199`). `GET /api/audio/events/current` — ADR §8.1's re-attach path — carries it with **no controller change** |
| **Pause / seek / resume refuse it correctly** | All three resolve `playback.Source` (`:326`, `:365`, `:381`), which is null until `TryAdopt` — so `EventPlaybackController.Transport` answers **409**, not 404, because `Current` still describes the id (`EventPlaybackController.cs:217-224`) |

⚠ **Acquire FIRST, then wait** — the reason is a UX one, not a mechanical one. Acquiring during the
wait means the audio is ready the instant the room goes quiet, and it means an acquisition **failure**
surfaces immediately rather than after thirty seconds of `Waiting` — *"wait, then fail"* being a
strictly worse version of the shape `D28` rejected. The cost is one open `FileStream` over a cached
recording held for the length of the wait, bounded by `GvMedia:MaxQueuedWaitSeconds` and disposed on
every exit (**C-57**).

⚠ **And the wait goes BEFORE `_gate`, not inside it.** Holding the gate across a 30-second wait would
block `StopAsync`, the replacement arm and `OnSourceCompleted` for its whole length — the user's own
Stop button would do nothing until the blocker finished, which is the shape `D28` exists to avoid.

### 0.3 ⚠ Re-check list — what was verified against `4ec0fb85`, and what did not survive

`PHN-1e` §5 was written **before** `4ec0fb85` and `7a6911ce` merged. Every claim below was re-derived
from merged code. **A Builder must re-grep anything marked ⚠ before trusting an anchor.**

| §5 claim | Verdict against `4ec0fb85` | Anchor |
|---|---|---|
| Q1 — one deep, replace semantics, no pending slot | ✅ **CONFIRMED** | `EventPlaybackService.cs:237-244` |
| Q2 — `StopAsync` resolves a wait; `Current` reports it; a **second** exception is owed on `Current`'s remark | ✅ **CONFIRMED.** The shipped remark does open *"⚠ 'In flight' is not the whole of it"* and documents exactly one exception | `IEventPlaybackService.cs:89-102` |
| Q3 — 30 s hard bound, `Failed` + `"WaitExpired"`, one `Task.WaitAsync(timeout, TimeProvider, token)` | ✅ **CONFIRMED.** `_timeProvider` is injected and defaults to `TimeProvider.System` | `EventPlaybackService.cs:61`, `:152`, `:161` |
| Q4 — `StopDuckingAsync` raises **only when the set empties**, so the starvation case is real | ✅ **CONFIRMED, exactly as described** | `DuckingService.cs:217` (`needsRestore = _isDucking && remainingEvents == 0`), raise at `:239` inside `if (needsRestore)` |
| Q4 — the two hazards that make the naive versions wrong | ✅ **CONFIRMED, both.** `IsDucking:false` while others remain → `ClearDuckingMultiplier` restores the radio mid-announcement; `IsDucking:true` on a stop → *"Ducking started"* logged for a source that stopped | `AudioManager.cs:497-514` |
| Q4 — `GetActiveEventsByPriority` has no non-test caller yet | ✅ **CONFIRMED** | `IDuckingService.cs:70`; no production call site outside `DuckingService` itself |
| Q5 — the orphaned-source window is **dissolved** | ⚠ **INCOMPLETE — see C-57.** True of the *wake* path; **false of the timeout and cancel paths**, which are new exits between acquisition and adoption that leak the acquired source | `EventPlaybackService.cs:1020-1033`, `:1211-1222` |
| C-47 — enums cross the wire as **strings**, so `Waiting` needs no lockstep `Radio.Web` build | ✅ **CONFIRMED, twice.** `State = snapshot.State.ToString()` on the hub; `JsonStringEnumConverter` on MVC | `AudioStateUpdateService.cs:1092`; `Radio.API/Program.cs:61` |
| `IsLive` treats an unrecognised state as live | ✅ **CONFIRMED** — it is a **deny-list** | `ApiModels.cs:1522` |
| The 300 s cap fires and is load-bearing | ✅ **CONFIRMED, untouched by this row** | `EventPlaybackService.cs:580`, `:1123-1210` |
| `/sleep` is two edges, neither of which is `IsSleeping` | ✅ **CONFIRMED** | `SleepService.cs:189-215` (screen report), `:250-286` (`EnterSleepAsync`) |
| ⭐ **The `/sleep` stop is an ALLOW-LIST** | ⚠ **NEW — NOT IN §5. See C-56.** Adding `Waiting` silently removes it from the `/sleep` rule | `SleepService.cs:356-358` |
| §5's line anchors | ⚠ **STALE.** §5 cites *"`FakeDuckingService`'s raising-thread instrument (already built, `:2007-2011`)"*; at `4ec0fb85` those lines are inside `AThrowingLogSinkInTheCapCallbackDoesNotEscape`. The instrument is at `EventPlaybackServiceTests.cs:2315-2319`, `:2339-2345`, `:2537-2555` | — |

### 0.4 ⚠ Twelve constraints found while planning — C-56 continues `PHN-1e`'s numbering

**C-56 and C-57 CHANGE THE WORK.** C-58 corrects an overstated instruction in §5. C-61 through C-63
are comment corrections this row owes; C-63 is a promise this row declines and must therefore rewrite.

---

**C-56 — ⚠ CHANGES THE WORK, AND ITS FAILURE MODE IS SILENT. The `/sleep` stop is an ALLOW-LIST; the
circuit backstop is a DENY-LIST. Adding `Waiting` therefore does opposite things to the two sibling
rules, and only one of them is right by default.**

Two rules act on the playback's state. They were written independently and have **opposite polarity**:

```csharp
// SleepService.cs:356-358 — ALLOW-LIST. A new state is EXCLUDED by default.
if (snapshot.State is not (EventPlaybackState.Preparing
    or EventPlaybackState.Playing
    or EventPlaybackState.Paused))
{
  return;
}
```

```csharp
// Radio.Web/Models/ApiModels.cs:1522 — DENY-LIST. A new state is INCLUDED by default.
public bool IsLive =>
  State is not null && State is not ("Completed" or "Stopped" or "Failed");
```

So, with no code change beyond the enum member:

- **The last-circuit backstop already covers `Waiting`** — `AttendedPlaybackCircuitHandler.cs:232`
  gates on `IsLive`, which answers true. Correct, and free.
- **The `/sleep` rule silently STOPS covering it.** A waiting playback survives entering sleep and
  then starts audio **up to thirty seconds later, on a dark panel, under `EmptyLayout`, with no stop
  control anywhere on screen.** That is the precise failure §7.5 exists to prevent.

⛔ **This is not a judgement call and it does not need an owner ruling.** ADR-029 §16.5's own reason
for including `Preparing` is *"a fetch in flight would otherwise start audio moments after the panel
goes dark"* (`design/decisions/2026-08-03-gv-audio-through-engine.md:1322-1323`). `Waiting` is that
argument with a longer fuse and a certainty in place of a maybe. **Add `Waiting` to the allow-list**
(Task 7).

⚠ **Do not "fix" this by flipping the allow-list to a deny-list.** The allow-list is the safer polarity
for that call site and its enumeration is deliberate — a terminal state must not be re-stopped. What is
wrong is that a new non-terminal member has to be added in two places, and the only defence against
forgetting is this constraint plus `TheSleepRuleCoversEveryNonTerminalState` (Task 11), which is
written to enumerate the enum by reflection so it reds for the *next* member too.

---

**C-57 — ⚠ CHANGES THE WORK. §5's Q5 says the orphaned-source window is "dissolved". That is true of
the wake and false of the other two exits, and the leak it leaves is the one `PHN-1c` already paid
for once.**

§5 Q5 argues, correctly, that the **wake** introduces no second entry point into the acquisition tail:
it is `TrySetResult` and nothing else, the already-running task resumes, and PR 3's gated tail runs
unchanged with a zero-line diff. **All of that holds.**

What it does not address is that the wait introduces **two new ways to leave `AcquireAndPlayAsync`
between acquisition and `TryAdopt`** — the staleness bound (`TimeoutException`) and a cancel
(`OperationCanceledException`). Before this row that stretch contained exactly one await,
`_gate.WaitAsync(CancellationToken.None)` (`:523`), which cannot throw — so no exit existed and none
was guarded.

The existing catches **cannot** release the source, and the reason is structural rather than an
oversight:

- `TearDownAsync` (`:1020-1033`) releases through `playback.ClaimSourceForRelease()`, which answers
  **null** for a playback that never adopted — and its own comment says so.
- `FailAsync` (`:966`) reaches the same path.

So without a guard the `RemoteMedia` arm leaks an **open `FileStream` over the cached recording** for
the life of the process, which on Windows also stops `GvMediaCache`'s evictor reclaiming that entry —
the exact consequence `TryAdopt`'s own comment (`:526-533`) was written about.

**The fix is four lines and it is in Task 6d:** wrap the wait, `await DisposeOrphanAsync(playback,
source)` on any throw, rethrow. `DisposeOrphanAsync` (`:1211-1222`) is already the right tool — *"this
source was never ducked and never played, so there is nothing to stop"* — and a later `TearDownAsync`
finds null and does nothing, so there is no double-dispose.

---

**C-58 — §5's instruction *"`AudioManager.OnDuckingStateChanged` branches on `Transition`, not on
`IsDucking`"* is stated too strongly, and taken literally it reintroduces the hazard it is guarding
against. Only the LOG may branch on `Transition`.**

§5's own next sentence has it right — *"`ClearDuckingMultiplier` stays on the `!IsDucking` edge and
therefore fires exactly when it fires today"* — but the headline is what a Builder will implement.

The danger is concrete. `DuckingSourceTransition.Started = 0` is the default value of an `init`-only
property (**C-59**), so **any** `DuckingStateChangedEventArgs` built without setting `Transition`
reports `Started`. If the outer branch keyed on `Transition`, such a raise carrying `IsDucking:false`
would take the "started" arm, skip `ClearDuckingMultiplier`, and leave the radio **stuck ducked** — a
worse failure than the mislabelled log line the field exists to fix.

⛔ **Therefore: the `ClearDuckingMultiplier` edge stays literally `!e.IsDucking`, byte for byte.**
`Transition` is consulted **only** to choose between two log lines on the `IsDucking:true` arm. Under
that shape a defaulted `Transition` can at worst produce today's exact log line, and no behaviour can
change. Task 5 is written this way and its test asserts the polarity directly.

---

**C-59 — the args change has exactly two construction sites in the whole tree, and that is what makes
it safe to add a defaulted enum.**

Verified: `new DuckingStateChangedEventArgs` appears at `DuckingService.cs:486` and
`EventPlaybackServiceTests.cs:2543` (inside `FakeDuckingService`) and **nowhere else**. There is one
other `IDuckingService` implementation in the tree (that same fake). Both are updated by this row, so
no site is left defaulting in practice — but C-58's polarity rule is what makes that a belt rather
than the only defence.

---

**C-60 — every line anchor in `PHN-1e` §5 is stale, and one is actively misleading.**

§5 cites the raising-thread instrument as *"already built, `:2007-2011`"*. At `4ec0fb85` those lines
are inside `AThrowingLogSinkInTheCapCallbackDoesNotEscape`. The instrument is real and is at
`EventPlaybackServiceTests.cs:2315-2319` (fields), `:2339-2345` (the two counters) and `:2537-2555`
(`RaiseStateChanged`, the single raise funnel that records the thread). Every anchor in this plan was
derived at `4ec0fb85`; re-grep before trusting one.

---

**C-61 — `PublishNonTerminal`'s summary says *"Publishes a NON-terminal state — Playing or Paused"*
and this row falsifies it.** `EventPlaybackService.cs:1270-1272`. `Waiting` is a third. Corrected in
Task 6h. This is the class `CLAUDE.md` § Pre-Merge Review exists for.

---

**C-62 — `GvMediaOptions.PreemptAtPriority`'s doc comment says the mirror direction *"is not
implemented yet: it mixes"*, and this row is what makes that false.** `GvMediaOptions.cs:93-97`,
including the sentence *"that lands with the server-owned playback state that can broadcast a waiting
state to a client"* — which is now this row rather than a future one. Corrected in Task 3.

---

**C-63 — ⚠ A MERGED COMMENT PROMISES THIS ROW WILL FIX SOMETHING IT DOES NOT. Decline it explicitly
and rewrite the comment; do not leave the promise standing.**

`SleepService.cs:183-187`:

> ⚠ **One case NEITHER edge covers, named so it is not mistaken for covered:** a playback *started*
> while the console is already on `/sleep`. No report and no sleep entry follows it, so nothing stops
> it. §7.5 is written about *entering* the surface; a playback arriving at one is the mirror case, and
> it belongs with `D28`'s queue in `PHN-1f`.

**This row does not take it, and the reason is the dependency direction rather than appetite.**
`SleepService` lives in `Radio.API` and holds `IEventPlaybackService`; `EventPlaybackService` lives in
`Radio.Infrastructure` and knows nothing about sleep. Making `StartAsync` consult the sleep state
inverts that, and the only alternatives are a refusal — the shape `D28` rejected — or a new
`Radio.Core` seam for "is there a surface with a transport", which is ADR §14 **Q12**'s multi-client
question and belongs to the sleep arc with the Designer.

It is also **narrow**: `GvMedia:Enabled` ships `false` until PR 6, and reaching the case needs a second
client on `/phone` while the kiosk sits on `/sleep` — which is Q12 exactly. Task 7b rewrites that
paragraph to say it is unowned and points at Q12; §5.3 carries it forward, and §6 proposes no row for
it because it is a design question and not a defect.

---

**C-64 — ⭐ `Waiting` reaches the browser with NO `Radio.Web` change and NO broadcast change. Verified
end to end, and it is C-47's payoff arriving.**

- Hub: `AudioStateUpdateService.cs:1092` sends `State = snapshot.State.ToString()` → `"Waiting"`.
- REST: `GET /api/audio/events/current` returns the Core record through MVC's
  `JsonStringEnumConverter` (`Radio.API/Program.cs:61`) → `"Waiting"`.
- Client: `EventPlaybackSnapshotDto.State` is `string?` (`ApiModels.cs:1503`), and `IsLive` is a
  deny-list, so the chip PR 6 builds gets *"something is happening, offer Stop"* for free.

⛔ **So no file under `src/Radio.Web/` is touched by this row.** If a diff shows one, the change went
in the wrong place.

---

**C-65 — the waiter belongs on `Playback`, not on the service.**

§5 sketches `Volatile.Read(ref _waiter)?.TrySetResult()` against a service field. A single service
field works today only because a replacing `StartAsync` cancels the displaced playback's token
synchronously before the replacement can arm its own — which is true, and is one refactor away from
not being. Hanging the waiter off `Playback` makes *"the waiting playback IS `_current`"* structural
rather than incidental: the wake reads `_current` under `_stateLock` and can only ever wake that
playback's own waiter, and a displaced playback's waiter is unreachable by construction.

---

**C-66 — ⚠ THE WAKE HAS A MISSED-WAKE RACE THAT §5 DOES NOT NAME, AND IT PARKS THE PLAYBACK FOR THE
FULL THIRTY SECONDS.**

The wake path (`TryWakeWaitingPlayback`) must ask *"is anything waiting?"* before it does any work —
otherwise it walks the ducking set on the raising thread for **every** announcement on the box, which
is trap 5 territory. But that guard creates a window: if the blocker ends between the predicate check
that decided to wait and the moment the waiter is armed, the wake finds nothing waiting, and the
playback then sits until `WaitExpired` **for a room that is already quiet**.

That is `D28`'s rejected option delivered thirty seconds late — the exact outcome the overturned lean
in §5 Q4 was rejected for producing. **Arm the waiter, then re-check the predicate**; the wake is
idempotent, so a redundant `TrySetResult` costs nothing. Task 6b's code does this and
`AWaitIsNotMissedWhenTheBlockerEndsWhileTheWaiterIsBeingArmed` samples it — see §2.2 item 1 for what
that test does *not* prove.

---

**C-67 — a waiting playback's `Duration` differs by arm, and both answers are honest. Do not
"fix" either.**

`SnapshotOf` (`:1249-1268`) nulls `Duration` only for `Preparing`, and reads `playback.Source` — which
is null until `TryAdopt`, i.e. for the whole of a wait. So:

- **RemoteMedia** reports the provider's duration, because `playback.ReportedDuration` is assigned
  during acquisition (`:632`), which happens *before* the wait. The chip can render a real bar.
- **Speech** reports `null`, because the source is unadopted and there is no other estimate. §0.6 item
  2 of `PHN-1e` already requires a client to render `duration: null` as **indeterminate, not zero**.

`SnapshotOf` is therefore **unchanged** by this row.

### 0.5 What this row is NOT

1. ⛔ **Not a change to `Radio.Web`.** C-64. Zero files under `src/Radio.Web/`.
2. ⛔ **Not the chip.** PR 6 (`PHN-2`) builds it. This row defines what it renders (§0.6).
3. ⛔ **Not a fix for ADR §6.2 rule 3.** Sub-8 sources still mix. That is the ADR's recorded wart and
   fixing it is a queue across every `IAnnouncementService` caller.
4. ⛔ **Not a change to the 300 s cap.** It is load-bearing for Amendment 2's P6 reasoning
   (`MaxPlaybackSeconds` 300 < `DisconnectedCircuitRetentionPeriod` 600) and two mutations prove it
   fires. Do not disturb it. `MaxQueuedWaitSeconds` is a **separate** key and a separate timer.
5. ⛔ **Not "a playback started while already on `/sleep`".** C-63.
6. ⛔ **Not an ADR edit.** §16.5's own table overstates its case (§5.1 records it for an Architect
   pass); this row must not amend a merged ADR.
7. ⛔ **Not a queue deeper than one.** `D28` is one deep with replace semantics (§0.2).
8. ⛔ **Not an `EndReason` field.** `PHN-1e` §0.6 records why it was not added speculatively, and
   `WaitExpired` arrives as a `FailureReason`, which already exists.

### 0.6 ⭐ What a viewer actually sees while waiting — the wire shape, named now

The brief weights this, and `D28`'s whole argument is that an invisible state is not a state. **PR 6
builds the chip; this section is the contract it renders against**, and it extends `PHN-1e` §0.6
rather than replacing it.

```jsonc
{
  "id": "evp-4f2c…",              // unchanged — the handle for DELETE, live while Waiting
  "kind": "RemoteMedia",
  "label": "Voicemail from Jane",
  "state": "Waiting",             // ⭐ NEW. String, so no lockstep Web build is needed (C-47, C-64)
  "duration": "00:00:29.9000000", // RemoteMedia: the provider's value. Speech: null (C-67)
  "positionAtBroadcast": "00:00:00",
  "broadcastAtUtc": "2026-09-04T18:22:41.117Z",
  "failureReason": null
}
```

**Exactly two lifecycles gain a broadcast, and only on the path that actually waits:**

```
Preparing → Waiting → Playing → Completed        (a wait that was woken)
Preparing → Waiting → Failed{"WaitExpired"}      (a wait that timed out)
Preparing → Playing                              (a quiet room — UNCHANGED, no extra broadcast)
```

⚠ **A quiet room produces no new traffic at all.** The predicate is evaluated before anything is
published, so the overwhelmingly common case costs one walk of an empty list and adds zero messages to
the wire. That is deliberate: trap 5 is about churn on an N100, and a queue that broadcast a `Waiting`
nobody waited for would be churn with a straight face.

**What PR 6 must do with it, as requirements:**

1. **`Waiting` is LIVE — offer Stop.** It is the state most in need of one: the user pressed play,
   nothing is audible, and the only thing worse than that is not being able to take it back.
   `IsLive` already answers true (C-64), so this is inherited, not built.
2. **Do not run the progress bar.** `state == "Waiting"` is not `Playing`; `positionAtBroadcast` is
   zero and stays zero. Interpolating from the anchor while waiting would show a bar advancing through
   audio that does not exist. `PHN-1e` §0.6 item 1 says *"while `state == "Playing"`"* — that word is
   now load-bearing.
3. **Say why, not just that.** *"Waiting for the announcement to finish"* is the information; a bare
   spinner is the complaint `D28` rejected, rendered. The snapshot does **not** carry the blocker's
   identity and this row does not add it — one voice at a time is the invariant, and naming the
   blocker would be a wire commitment bought with nothing. If the chip wants it, ask; §5.2 records the
   request path.
4. **`WaitExpired` is a `FailureReason` like any other**, and PR 6's error state (ADR §12 item 3)
   already exists for `MediaNotFound`. It needs copy, not a mechanism.
5. **A terminal state is still retained.** Unchanged from `PHN-1e` §0.6 item 4.

---

## 1. Tasks

### Task 1 — `Radio.Core`: the transition discriminator and the captured priority

**File:** `src/Radio.Core/Interfaces/Audio/IDuckingService.cs`

Append after the `IDuckingService` interface (after `:81`), before `DuckingStateChangedEventArgs`:

```csharp
/// <summary>What happened to the ducking set, as distinct from what the aggregate state now is.</summary>
/// <remarks>
/// ⚠ This exists because <see cref="DuckingStateChangedEventArgs.IsDucking"/> answers a DIFFERENT
/// question, and overloading it is what makes the obvious implementations wrong. IsDucking is the
/// AGGREGATE — "is anything ducking" — and AudioManager keys ClearDuckingMultiplier off its false
/// edge. A source LEAVING while others remain is an <see cref="Ended"/> transition with IsDucking
/// still TRUE, and the two facts must be separately expressible or one of them has to lie.
///
/// ⚠ <see cref="Started"/> is 0, so it is the value an args object gets when nothing sets this
/// field. That is why AudioManager consults this only to choose a LOG LINE and never to decide
/// whether to clear the ducking multiplier — see its handler, and plan PHN-1f C-58.
/// </remarks>
public enum DuckingSourceTransition
{
  /// <summary>A source joined the ducking set.</summary>
  Started = 0,

  /// <summary>A source left the ducking set. Others may remain — read IsDucking for that.</summary>
  Ended = 1,

  /// <summary>
  /// Every source was cleared at once (<see cref="IDuckingService.StopAllDuckingAsync"/>).
  /// TriggeringSource is null.
  /// </summary>
  AllCleared = 2
}
```

Then add two members to `DuckingStateChangedEventArgs`, after `ActiveEventCount` (`:106`):

```csharp
  /// <summary>What happened to the set. See <see cref="DuckingSourceTransition"/>.</summary>
  public DuckingSourceTransition Transition { get; init; }

  /// <summary>
  /// The triggering source's priority, CAPTURED AT RAISE TIME, or 0 when there is no triggering
  /// source.
  /// </summary>
  /// <remarks>
  /// ⚠ Captured rather than looked up, and that is the entire point of the field. A subscriber that
  /// calls <see cref="IDuckingService.GetPriority"/> for itself races the ducking service, which
  /// DELETES the override before it raises — so the answer for a source that has just left is the
  /// category default 8 for an announcement whose caller explicitly claimed 3. The same is true on
  /// the START path, because the transition raise happens after the attack fade: a stop landing
  /// inside that ~100 ms deletes the entry first. PHN-1d had to guard that with an ActiveEventCount
  /// check and could only narrow it; this closes it.
  /// </remarks>
  public int TriggeringSourcePriority { get; init; }
```

---

### Task 2 — `Radio.Core`: `EventPlaybackState.Waiting`, and the second exception on `Current`

**File:** `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs`

⛔ **`Waiting` goes at the END of the enum, value 6, never in the middle.** These names reach log lines
and the wire; inserting into the middle is how a name quietly stops meaning what it used to. The punch
list requires it in as many words (`docs/HANDOFF-GA-PUNCH-LIST.md:1303`).

Append after `Failed = 5` (`:164`), inside the enum:

```csharp
  Failed = 5,

  /// <summary>
  /// Accepted, its audio already acquired, and deliberately NOT sounding because a source at or above
  /// GvMedia:PreemptAtPriority is (ADR-029 D5 §6.2 rule 2 read symmetrically; owner decision D28).
  /// Resolves to Playing when the blocker leaves the ducking set, or to Failed with "WaitExpired"
  /// at GvMedia:MaxQueuedWaitSeconds.
  /// </summary>
  /// <remarks>
  /// ⚠ APPENDED AT THE END, deliberately, and it must stay there. See the remark on
  /// EventPlaybackRejection's illegal-media-id member for why: these names reach the wire and the
  /// logs, and inserting into the middle is how one quietly stops meaning what it used to.
  ///
  /// ⚠ It is LIVE, not terminal, and two rules read that differently. Radio.Web's
  /// EventPlaybackSnapshotDto.IsLive is a DENY-LIST and picks this up for free; SleepService's stop
  /// is an ALLOW-LIST and had to be told. A third reader must DECIDE which it is rather than
  /// inheriting one (plan PHN-1f C-56).
  /// </remarks>
  Waiting = 6
```

⚠ **Check the exact member name** on `EventPlaybackRejection` before writing any `<see cref>` for it —
`grep -n "MediaIdHasIllegal" src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs`. A wrong cref is
a Release build error (`TreatWarningsAsErrors`), which is the cheap way to find out; the prose form
above avoids the risk entirely and is fine to keep.

Then replace `Current`'s summary and remark (`:89-101`) — **this adds a second exception; it does not
replace the first**:

```csharp
  /// <summary>
  /// The one attended playback this seam is tracking, or null. There is one audio engine and one set
  /// of speakers, so this state is global rather than per-caller (ADR-029 D6).
  /// </summary>
  /// <remarks>
  /// ⚠ "In flight" is not the whole of it, in TWO directions, and both are load-bearing for the 202
  /// shape.
  ///
  /// The LAST snapshot is RETAINED after a playback ends — Completed, Stopped or Failed — until a new
  /// playback replaces it. It has to be: <see cref="StartAsync"/> answers before any audio exists, so
  /// an acquisition failure has no response left to carry it, and this is the surface a caller
  /// re-reads to find out what happened (ADR-029 §8.1's re-attach path).
  ///
  /// And a playback can be WAITING: accepted, its audio already acquired, and deliberately not
  /// sounding because a source at or above GvMedia:PreemptAtPriority is (owner decision D28). It is
  /// not in flight and it is not finished.
  ///
  /// So null means "nothing has been started yet", never "nothing is playing" —
  /// <see cref="EventPlaybackState"/> on the snapshot is what says whether audio is being produced.
  /// </remarks>
  EventPlaybackSnapshot? Current { get; }
```

⚠ **The summary line changes too** — it said *"The one **in-flight** attended playback"*, and a waiting
playback is not in flight. **This is the sixth correction of this class in this arc** (`CLAUDE.md`
§ Pre-Merge Review). Making it in the same PR that falsifies the sentence is the whole discipline.

---

### Task 3 — `Radio.Core`: the config key, and the doc comment this row falsifies

**File:** `src/Radio.Core/Configuration/GvMediaOptions.cs`

Add after `PreemptAtPriority` (`:118`):

```csharp
  /// <summary>
  /// How long an attended playback will WAIT for a blocking source before giving up (owner decision
  /// D28). Read by <c>EventPlaybackService.WaitForClearAirAsync</c>, which arms one
  /// <c>Task.WaitAsync</c> — a one-shot bound, not a poll.
  ///
  /// <para>
  /// Thirty seconds because the thing being waited on is a notification measured in seconds; a wait
  /// longer than its blocker means the blocker was not what we thought. On expiry the playback
  /// becomes <c>Failed</c> with reason <c>"WaitExpired"</c>, which is honest — it never produced
  /// sound — and is acceptable HERE and only here, because by then the user has watched a visible
  /// Waiting state, which is what made a bare refusal embarrassing.
  /// </para>
  ///
  /// <para>
  /// ⚠ There is no "off", and a value below 1 clamps to 1 rather than disabling the wait. A 0 meaning
  /// "never wait" would resolve to MIXING, which is the option D28 rejected — and this arc already
  /// has a worked example of a knob that deletes a behaviour while looking configured (see
  /// <see cref="PreemptAtPriority"/>, and PHN-1d C-43).
  /// </para>
  ///
  /// <para>
  /// ⚠ Unrelated to <see cref="MaxPlaybackSeconds"/> and armed by a different timer. A wait does not
  /// consume the playback cap: the cap is armed after PlayAsync returns, so the worst case is one
  /// wait plus one full-length playback.
  /// </para>
  /// </summary>
  public int MaxQueuedWaitSeconds { get; set; } = 30;
```

**File:** `src/Radio.API/appsettings.json` — add `"MaxQueuedWaitSeconds": 30` to the `GvMedia` section.

⛔ **NOT in `deploy/*/appsettings.Production.json`** — `PHN-1b` **C-14**: the deploy overwrites
`appsettings.json` and only *seeds* the Production file when absent, and `radio` already has one. A key
added there would never reach the box.

**C-62 — correct `PreemptAtPriority`'s doc** (`:93-97`). It currently reads:

> ⚠ The mirror direction — a playback STARTING while such a source is already sounding — is not
> implemented yet: it mixes. The owner's decision is that it must WAIT for the blocking source and then
> play, and that lands with the server-owned playback state that can broadcast a waiting state to a
> client. Do not implement it as a refusal; that option was considered and rejected.

Replace with:

```
  /// ⚠ The mirror direction — a playback STARTING while such a source is already sounding — is
  /// implemented as of PHN-1f and does NOT mix: it publishes EventPlaybackState.Waiting, waits for
  /// the blocker to leave the ducking set, and then plays (owner decision D28). This value is
  /// therefore read by TWO rules that are each other's mirror: OnDuckingStateChanged stops an
  /// in-flight playback when such a source starts, and WaitForClearAirAsync refuses to start one
  /// while such a source is present. ⛔ Do not reimplement either as a refusal; that option was put
  /// to the owner and rejected.
```

---

### Task 4 — `DuckingService`: capture the priority in the lock, and raise for every source that LEAVES

**File:** `src/Radio.Infrastructure/Audio/Services/DuckingService.cs`

⚠ **This is the highest-risk edit in the row.** Two hazards make the naive versions wrong, both
verified against `AudioManager.cs:497-514`: raising `IsDucking:false` while others remain restores the
radio to full volume **mid-announcement**; raising `IsDucking:true` for a stop logs *"Ducking started"*
for a source that stopped.

**4a. `RaiseDuckingStateChanged` gains two parameters** (`:484-509`):

```csharp
  private void RaiseDuckingStateChanged(
    bool isDucking,
    IEventAudioSource? triggeringSource,
    DuckingSourceTransition transition,
    int triggeringSourcePriority)
  {
    var args = new DuckingStateChangedEventArgs
    {
      IsDucking = isDucking,
      TriggeringSource = triggeringSource,
      DuckLevel = CurrentDuckLevel,
      ActiveEventCount = ActiveEventCount,
      Transition = transition,
      TriggeringSourcePriority = triggeringSourcePriority
    };

    // … the existing try/catch and its whole remark are UNCHANGED …
```

⚠ **Leave the existing remark on this method alone.** Its guard reasoning is orthogonal to this change,
is correct, and the subscriber count it reasons about is still two.

**4b. `StartDuckingAsync` — capture the priority BEFORE the fade** (`:130-191`).

Add `int priorityAtStart;` beside the existing locals (`:136-138`), then inside the existing
`lock (_lock)` (`:140-156`), after the `_activeEvents` insert:

```csharp
      // ⚠ CAPTURED HERE, inside the lock that adds the entry and BEFORE ApplyFadeAsync — and that
      // ordering is the whole fix for PHN-1d's fade window. The transition raise below happens AFTER
      // the attack fade (Audio:DuckingAttackMs, 100 ms shipped), so a StopDuckingAsync for THIS source
      // landing inside it deletes the override first, and a subscriber resolving the priority at raise
      // time reads the category default 8 for an announcement that explicitly claimed 3. Reading it
      // here means there is nothing left to race.
      //
      // GetPriority re-enters _lock; Monitor is reentrant and GetActiveEventsByPriority already relies
      // on exactly that.
      priorityAtStart = GetPriority(eventSource);
```

and change the raise (`:183`):

```csharp
      RaiseDuckingStateChanged(
        true, eventSource, DuckingSourceTransition.Started, priorityAtStart);
```

**4c. `StopDuckingAsync` — capture before the removal, raise on EVERY removal** (`:194-242`).

Add `int priorityBeforeRemoval;` beside the existing locals (`:200-201`), then inside the existing
`lock (_lock)` (`:203-227`), **above** the two `Remove` calls:

```csharp
      // ⚠ CAPTURED BEFORE THE REMOVALS, in the same lock that performs them. That is the whole of the
      // capture: two lines below, _sourcePriorities.Remove deletes the override, and every subscriber
      // that resolved the priority for itself after that point read the category default 8.
      priorityBeforeRemoval = GetPriority(eventSource);

      _activeEvents.Remove(eventSource.Id);
```

Then **replace** the tail of the method (`:229-241`) — the raise moves OUT of the `if (needsRestore)`
block and becomes unconditional:

```csharp
    if (needsRestore)
    {
      var releaseMs = options.DuckingReleaseMs;

      _logger.LogInformation(
        "Stopping ducking: releasing to 100%, release time {ReleaseMs}ms, policy {Policy}",
        releaseMs, options.DuckingPolicy);

      await ApplyFadeAsync(100f, releaseMs, options.DuckingPolicy, eventSource, cancellationToken);
    }

    // ⚠ RAISED FOR EVERY SOURCE THAT LEAVES, not only when the set empties. This is the mirror of what
    // PHN-1d did for StartDuckingAsync and it is here for the same reason: a subscriber cannot act on
    // a source ending if it is never told one did. Before this line moved, a priority-8 blocker ending
    // while a priority-5 announcement continued produced NO RAISE AT ALL — so EventPlaybackService's
    // D28 queue would never have been woken and would have expired as Failed/"WaitExpired", which is
    // D28's rejected option delivered thirty seconds late.
    //
    // ⚠ IsDucking carries the TRUE AGGREGATE — false only when the set is actually empty, which is
    // exactly what needsRestore means. That is what keeps AudioManager.ClearDuckingMultiplier firing
    // on precisely the occasions it fires today: raising IsDucking:false while other sources remain
    // would restore the radio to full volume MID-ANNOUNCEMENT, and that hazard is why this needed the
    // Transition field before it could be done at all.
    //
    // ⚠ PLACED AFTER the fade block, not inside it, so the emptying case still raises AFTER
    // ApplyFadeAsync — byte-identical timing to what AudioManager's "Ducking ended" line has always
    // had. Only the non-emptying case is new, and it has no fade to wait for.
    RaiseDuckingStateChanged(
      !needsRestore, eventSource, DuckingSourceTransition.Ended, priorityBeforeRemoval);
```

**4d. `StopAllDuckingAsync`** (`:263`):

```csharp
    RaiseDuckingStateChanged(false, null, DuckingSourceTransition.AllCleared, 0);
```

**4e. Correct the class remark on `StartDuckingAsync`** (`:120-128`), whose item (b) says the fade race
is *"rejected on ActiveEventCount"* and that closing it *"would mean carrying the priority on the args,
which is a Radio.Core change and a deliberate one."* That change is now made — rewrite (b) to say the
args carry the priority captured at add time and that the `ActiveEventCount` guard is gone.

---

### Task 5 — `AudioManager`: the log branches on `Transition`; the multiplier does NOT

**File:** `src/Radio.Infrastructure/Audio/Services/AudioManager.cs:486-515`

⛔ **C-58. Read it before editing.** The `ClearDuckingMultiplier` edge stays literally `!e.IsDucking`.

```csharp
  /// <summary>
  /// Handles ducking state changes.
  /// </summary>
  /// <remarks>
  /// ⚠ THE OUTER BRANCH IS <see cref="DuckingStateChangedEventArgs.IsDucking"/> AND MUST STAY THAT
  /// WAY. Transition (PHN-1f) is consulted only to choose between two LOG LINES; it never decides
  /// whether to clear the ducking multiplier. The reason is concrete: DuckingSourceTransition.Started
  /// is 0, so any args object built without setting the field reports Started — and an outer branch
  /// keyed on it would take the "started" arm for a raise carrying IsDucking:false, skip
  /// ClearDuckingMultiplier, and leave the radio STUCK DUCKED. That is a worse failure than the
  /// mislabelled log line the field exists to fix.
  ///
  /// ⚠ Since PHN-1f a raise can also mean "a source left while others remain" — Ended with IsDucking
  /// still TRUE. Before that field existed this method logged "Ducking started" for it, which is the
  /// comment-accuracy class CLAUDE.md § Pre-Merge Review names.
  /// </remarks>
  private void OnDuckingStateChanged(object? sender, DuckingStateChangedEventArgs e)
  {
    if (_playbackService == null)
    {
      return;
    }

    if (e.IsDucking)
    {
      if (e.Transition == DuckingSourceTransition.Started)
      {
        _logger.LogInformation(
          "Ducking started: source={TriggerSource}, duckLevel={DuckLevel:F0}%, activeEvents={EventCount}",
          e.TriggeringSource?.Name ?? "unknown", e.DuckLevel, e.ActiveEventCount);
      }
      else
      {
        // A source left and others remain. Debug rather than Information: the ducking level did not
        // move and nothing was restored, and since LOG-11 the journal carries Warning and above
        // anyway — this is file-sink detail, on a box where log volume correlates with audible
        // distortion.
        _logger.LogDebug(
          "Ducking continues: source={TriggerSource} left, activeEvents={EventCount}",
          e.TriggeringSource?.Name ?? "unknown", e.ActiveEventCount);
      }

      return;
    }

    // Ducking ended — clear all ducking multipliers to restore full volume.
    // ⚠ This edge is UNCHANGED from before PHN-1f, deliberately and byte for byte.
    if (_activeSource != null)
    {
      _playbackService.ClearDuckingMultiplier(_activeSource.Id);
    }

    _logger.LogInformation(
      "Ducking ended: volume restored, activeEvents={EventCount}",
      e.ActiveEventCount);
  }
```

---

### Task 6 — `EventPlaybackService`: the predicate, the wait, the wake, and the orphan guard

**File:** `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs`

**6a. The predicate — the file's single definition of "blocked".** Add to the preemption region, above
`OnDuckingStateChanged` (`:864`):

```csharp
  /// <summary>
  /// True while some event source at or above <paramref name="threshold"/> is in the ducking set.
  /// </summary>
  /// <remarks>
  /// ⚠ This gives <see cref="IDuckingService.GetActiveEventsByPriority"/> its FIRST non-test caller
  /// since it was written — which PHN-1d C-42 predicted would be the queue, and it was.
  ///
  /// ⚠ No exclusion for our own source is needed, and one is deliberately NOT written. The predicate
  /// is only ever evaluated for a playback that has not yet reached StartDuckingAsync, so the attended
  /// source is not in the set when it is asked. A guard for a state that cannot occur reads as
  /// evidence that it can. APlaybackAtPriorityEightDoesNotBlockItself pins it.
  ///
  /// ⚠ GetPriority is called here rather than read from event args because this is a question about
  /// the CURRENT SET, not about one transition — there are no args. The fade-window race the args
  /// exist to close does not apply: these sources are resident in the set, not arriving or leaving.
  /// </remarks>
  private bool IsBlockedByAHigherPrioritySource(int threshold) =>
    _duckingService.GetActiveEventsByPriority()
      .Any(s => _duckingService.GetPriority(s) >= threshold);
```

**6b. The wait.** Add below the predicate:

```csharp
  /// <summary>
  /// ⭐ Owner decision D28: waits for the air to clear before the acquisition tail starts audio.
  /// Returns as soon as nothing at or above GvMedia:PreemptAtPriority is in the ducking set.
  /// </summary>
  /// <remarks>
  /// ⚠ Called AFTER acquisition and BEFORE _gate. Both halves matter and neither is arbitrary.
  ///
  /// After acquisition, so the audio is ready the instant the room goes quiet and an acquisition
  /// FAILURE surfaces at once rather than after thirty seconds of Waiting — "wait, then fail" being a
  /// strictly worse version of the shape D28 rejected.
  ///
  /// Before the gate, because holding _gate across a wait this long would block StopAsync, the
  /// replacement arm and OnSourceCompleted for its whole length — the user's own Stop button would do
  /// nothing until the blocker finished.
  ///
  /// ⚠ THE CALLER OWNS THE SOURCE ACROSS THIS CALL AND MUST DISPOSE IT IF THIS THROWS. Nothing has
  /// been adopted yet, so TearDownAsync and FailAsync both reach ClaimSourceForRelease, which answers
  /// null. See AcquireAndPlayAsync's guard, and plan PHN-1f C-57.
  /// </remarks>
  private async Task WaitForClearAirAsync(Playback playback, CancellationToken token)
  {
    var gv = _gvMediaOptions.CurrentValue;

    // Evaluated BEFORE anything is armed or published, so the overwhelmingly common case — a quiet
    // room — walks an empty list, allocates nothing, and puts no extra message on the wire. Trap 5 is
    // about churn on an N100, and a queue that broadcast a Waiting nobody waited for would be churn.
    if (!IsBlockedByAHigherPrioritySource(gv.PreemptAtPriority))
    {
      return;
    }

    var waiter = playback.BeginWait();
    try
    {
      // ⚠ RE-CHECKED AFTER ARMING, and this closes a real missed-wake race rather than being belt and
      // braces. TryWakeWaitingPlayback asks "is anything waiting?" before it touches the ducking set —
      // it has to, because it runs on the raising thread for every announcement on this box. So a
      // blocker ending between the check above and BeginWait finds nothing waiting, wakes nothing, and
      // parks this playback until WaitExpired FOR A ROOM THAT IS ALREADY QUIET — which is D28's
      // rejected option delivered thirty seconds late, the exact outcome this row exists to prevent.
      // Arm, then re-check. The wake is idempotent, so a redundant TrySetResult costs nothing.
      if (!IsBlockedByAHigherPrioritySource(gv.PreemptAtPriority))
      {
        return;
      }

      // Information rather than Warning: this is the feature working, not a fault. Since LOG-11 it
      // lands in the file sink rather than the journal, which is where "why did the voicemail take a
      // moment" is diagnosed from. Source ids only — never a media id and never request text
      // (PHN-1b §0.3 ④).
      _logger.LogInformation(
        "Attended playback {Id} is waiting: a source at or above GvMedia:PreemptAtPriority "
        + "({Threshold}) is already sounding (owner decision D28)",
        playback.Id, gv.PreemptAtPriority);

      PublishNonTerminal(playback, EventPlaybackState.Waiting);

      // ⭐ ONE call is the wake, the staleness bound AND the cancel. A one-shot timer, not a poll and
      // not a tick — trap 5 forbids both. It takes the TimeProvider PHN-1e injected, so
      // FakeTimeProvider.Advance produces WaitExpired deterministically with no Task.Delay anywhere
      // near an assertion (CLAUDE.md § Test Timing).
      //
      // Clamped at 1 for the reason GvMediaOptions.MaxQueuedWaitSeconds gives: a 0 meaning "never
      // wait" would resolve to mixing, which is the option D28 rejected.
      await waiter.Task.WaitAsync(
        TimeSpan.FromSeconds(Math.Max(1, gv.MaxQueuedWaitSeconds)), _timeProvider, token);

      _logger.LogInformation("Attended playback {Id} stopped waiting; the air is clear", playback.Id);
    }
    finally
    {
      playback.EndWait();
    }
  }
```

**6c. The wake.** Add below the wait:

```csharp
  /// <summary>
  /// Re-evaluates whether a waiting playback can proceed, and releases it if so.
  /// </summary>
  /// <remarks>
  /// ⚠ A STATE re-evaluation, not an edge, and that is deliberate. An edge would have to be right
  /// about which transitions can unblock a wait; a state re-evaluation is idempotent, cannot be
  /// desynchronised by a missed raise, and — the part that matters — uses the SAME predicate that
  /// decided to wait, so "blocked" has exactly one definition in this file.
  ///
  /// ⚠ The "is anything waiting" guard comes FIRST, and it is a trap-5 requirement rather than a
  /// micro-optimisation: this runs on the raising thread for EVERY ducking transition on the box,
  /// including every announcement with no attended playback anywhere near it. Without the guard each
  /// one would walk the ducking set and call GetPriority per member, on an N100 where churn is
  /// audible. The race that guard creates is closed by WaitForClearAirAsync's re-check (C-66).
  ///
  /// ⚠ It never touches a source, never takes _gate and never starts audio. The acquisition task that
  /// was already running resumes, takes _gate, and runs PR 3's tail unchanged — so there is no second
  /// entry point into that tail and none of PHN-1d Task 5's properties has to be re-established.
  /// </remarks>
  private void TryWakeWaitingPlayback()
  {
    Playback? waiting;
    lock (_stateLock)
    {
      waiting = _current;
    }

    if (waiting is null || !waiting.IsWaiting)
    {
      return;
    }

    if (IsBlockedByAHigherPrioritySource(_gvMediaOptions.CurrentValue.PreemptAtPriority))
    {
      return;
    }

    waiting.TryWake();
  }
```

**6d. The call site and the orphan guard.** In `AcquireAndPlayAsync`, between the acquisition switch
(ends `:488`) and the gate wait (`:523`), insert:

```csharp
      // ⭐ D28's wait. See WaitForClearAirAsync's remarks for why it is here and nowhere else.
      try
      {
        await WaitForClearAirAsync(playback, token);
      }
      catch
      {
        // ⚠ C-57. The source is acquired and NOT adopted, so none of the catches below can release
        // it: TearDownAsync and FailAsync both go through ClaimSourceForRelease, which answers null
        // for a playback that never adopted. Before this row the only await between acquisition and
        // TryAdopt was _gate.WaitAsync(CancellationToken.None), which cannot throw — so no exit
        // existed here and none was guarded. The wait adds two (the staleness bound and a cancel),
        // and without this the RemoteMedia arm leaks an open FileStream over the cached recording for
        // the life of the process, which on Windows also stops GvMediaCache's evictor reclaiming that
        // entry.
        //
        // DisposeOrphanAsync is the right tool and already exists: this source was never ducked and
        // never played, so there is nothing to stop. A later TearDownAsync finds null and does
        // nothing, so there is no double-dispose.
        await DisposeOrphanAsync(playback, source);
        throw;
      }
```

**6e. The `TimeoutException` arm.** Add between the `OperationCanceledException` catch (`:593-604`) and
the `GvMediaUnavailableException` catch (`:605`):

```csharp
    catch (TimeoutException ex)
    {
      // D28's staleness bound. Thirty seconds is longer than any notification this box makes, so a
      // wait that reaches it means the blocker was not what we thought.
      //
      // Failed is the honest state — it never produced sound — and failing is acceptable HERE and
      // only here, precisely because by then the user has watched a visible Waiting state. That is
      // what makes this different from the bare refusal D28 rejected.
      await FailAsync(playback, "WaitExpired", ex);
    }
```

**6f. `OnDuckingStateChanged` — read the captured priority, delete the guard, wake on every raise**
(`:864-964`). Replace the head of the method:

```csharp
  private void OnDuckingStateChanged(object? sender, DuckingStateChangedEventArgs e)
  {
    // ⭐ FIRST, and on EVERY raise in both directions — including StopAllDuckingAsync's, which carries
    // a null TriggeringSource and clears the whole set, and is therefore one of the strongest reasons
    // a wait should end. See TryWakeWaitingPlayback: it returns before touching the ducking set when
    // nothing is waiting, which is the overwhelmingly common case.
    TryWakeWaitingPlayback();

    if (e.Transition != DuckingSourceTransition.Started || e.TriggeringSource is not { } trigger)
    {
      return;
    }

    // ⚠ READ FROM THE ARGS, captured by DuckingService inside the lock that ADDED the entry and before
    // the attack fade. PHN-1d resolved this with a synchronous GetPriority and had to guard the fade
    // window with an ActiveEventCount == 0 check; both the call and the guard are GONE, and this is
    // why. The guard's own acknowledged residual — "if some OTHER source is still ducking, the count
    // is non-zero and this guard does not fire" — is closed by the same change rather than narrowed.
    var priority = e.TriggeringSourcePriority;

    var threshold = _gvMediaOptions.CurrentValue.PreemptAtPriority;
    if (priority < threshold)
    {
      // ADR-029 §6.2 rule 3: sub-threshold events keep MIXING, exactly as they do today over TTS
      // announcements. This row does not fix that; the fix would be a queue across every caller of
      // IAnnouncementService, and it is separate work with its own risk.
      return;
    }

    // … the rest of the method — victim resolution, the identity check, the LogWarning and the
    // dispatched StopAsync — is UNCHANGED, except for the comment noted below.
```

**Deleted by this task**, and the deletions are the point:

- the `if (e.ActiveEventCount == 0)` block and its whole comment, including the residual paragraph
  (`:871-888`) — ⛔ **deleted with its comment, not left as a fossil**;
- the `try`/`catch` around `_duckingService.GetPriority(trigger)` (`:890-900`) — reading a field
  cannot throw;
- the `!e.IsDucking` test in the head, replaced by `e.Transition != Started`.

**Also update** the comment at `:939-944`, which says *"What SHOULD cover that case is `PHN-1f`'s queue
(plan §0.4 C-46): a playback starting under a live >= 8 source waits for it. Until then it mixes, and
`APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY` is what pins that so `PHN-1f`'s fix is a
visible diff."* — the queue now exists and that test has been renamed. Say so.

**And update** the class remark's points (1), (2) and (3) (`:814-862`): (1)'s *"IsDucking:false is
ignored"* becomes *"only a Started transition is acted on"*; (2)'s entire two-paragraph argument about
reading the priority synchronously is superseded by the captured field and must be rewritten rather
than left describing a mechanism that is gone; (3) is unchanged and correct.

**6g. `Playback` gains the waiter** (`:1354-1500`). Add beside `_capTimer` (`:1374`):

```csharp
    // D28's wait. Non-null only between the moment acquisition decides the air is not clear and the
    // moment it stops waiting.
    //
    // ⚠ On Playback rather than on the service, deliberately (plan PHN-1f C-65). A service field
    // works today only because a replacing StartAsync cancels the displaced playback synchronously
    // before the replacement can arm its own — true, and one refactor away from not being. Here the
    // wake reads _current under _stateLock and can only ever wake THAT playback's waiter, so "the
    // waiting playback IS _current" is structural rather than incidental.
    private TaskCompletionSource? _waiter;
```

and the three members:

```csharp
    /// <summary>True while this playback is parked waiting for the air to clear.</summary>
    public bool IsWaiting => Volatile.Read(ref _waiter) is not null;

    /// <summary>Arms the wait and returns the waiter to await.</summary>
    /// <remarks>
    /// ⚠ RunContinuationsAsynchronously is load-bearing, and the overclaim is the trap here so it is
    /// stated exactly. Without it, TrySetResult runs the continuation INLINE on the thread that raised
    /// DuckingStateChanged — and that continuation's next act is _gate.WaitAsync.
    ///
    /// It is NOT a deadlock today: the live wake comes from AnnouncementService's teardown, which
    /// holds none of this service's locks. What the flag buys is that it STAYS not-a-deadlock, because
    /// the acquisition tail does hold _gate across StartDuckingAsync — so a raising thread that also
    /// holds the gate is one refactor away. OnSourceCompleted and the preemption dispatch are both
    /// written from this same reasoning, and their remarks say so.
    /// </remarks>
    public TaskCompletionSource BeginWait()
    {
      var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      Volatile.Write(ref _waiter, waiter);
      return waiter;
    }

    /// <summary>Disarms the wait. Called from the waiter's own finally, so it always runs.</summary>
    public void EndWait() => Volatile.Write(ref _waiter, null);

    /// <summary>Releases a wait if one is armed. Idempotent, and safe from any thread.</summary>
    public bool TryWake() => Volatile.Read(ref _waiter)?.TrySetResult() ?? false;
```

**6h. C-61 — correct `PublishNonTerminal`'s summary** (`:1270-1272`):

```csharp
  /// <summary>
  /// Publishes a NON-terminal state — Waiting, Playing or Paused — for a playback that has not ended.
  /// </summary>
```

---

### Task 7 — `SleepService`: C-56, and the promise this row declines

**File:** `src/Radio.API/Services/SleepService.cs`

**7a. The allow-list** (`:354-361`):

```csharp
    // ⚠ AN ALLOW-LIST, so every new non-terminal EventPlaybackState must be added HERE as well as
    // wherever else it is read — and forgetting is SILENT. Radio.Web's EventPlaybackSnapshotDto.IsLive
    // is a DENY-LIST and picks a new member up for free; these two rules are siblings with opposite
    // polarity (plan PHN-1f C-56). TheSleepRuleCoversEveryNonTerminalState reds if a member is added
    // to the enum and not listed here.
    //
    // Preparing is included deliberately: a fetch or a synthesis still in flight would otherwise start
    // audio moments after the panel went dark.
    //
    // Waiting for the same reason, with a longer fuse and a certainty in place of a maybe: a queued
    // playback (D28) is holding acquired audio and will start it the instant the blocking source ends,
    // which can be up to GvMedia:MaxQueuedWaitSeconds after the screen goes dark — on /sleep, which
    // declares EmptyLayout and therefore renders no stop control at all. ADR-029 §7.5's principle is
    // exactly this case: attended playback may not exist on a surface that offers no way to stop it.
    if (snapshot.State is not (EventPlaybackState.Preparing
        or EventPlaybackState.Waiting
        or EventPlaybackState.Playing
        or EventPlaybackState.Paused))
    {
      return;
    }
```

⚠ **`StopAsync` resolves a waiting playback with no extra work** — it matches `_current` by id, claims
the terminal flag, and `TearDownAsync` cancels the token, which unblocks the parked
`waiter.Task.WaitAsync` as an `OperationCanceledException`. The acquisition path's catch then disposes
the source it is holding through the C-57 guard. Nothing new is needed on this path; it is the same
`StopAsync` §0.2 already inherits.

**7b. C-63 — rewrite the paragraph that assigns the arriving-at-`/sleep` case to this row**
(`:183-187`):

```csharp
  /// <para>
  /// ⚠ <b>One case NEITHER edge covers, and it is UNOWNED rather than pending:</b> a playback
  /// <i>started</i> while the console is already on <c>/sleep</c>. No report and no sleep entry
  /// follows it, so nothing stops it. An earlier revision of this remark assigned it to
  /// <c>PHN-1f</c> with <c>D28</c>'s queue; <c>PHN-1f</c> examined it and declined it, on the
  /// dependency direction rather than on appetite. This service lives in <c>Radio.API</c> and holds
  /// <see cref="IEventPlaybackService"/>; the seam lives in <c>Radio.Infrastructure</c> and knows
  /// nothing about sleep. Making <c>StartAsync</c> consult the sleep state inverts that, and the only
  /// alternatives are a refusal — the shape <c>D28</c> rejected — or a new <c>Radio.Core</c> seam for
  /// <i>"does any surface offer a transport"</i>, which is ADR-029 §14 <b>Q12</b>'s multi-client
  /// question and belongs to the sleep arc with the Designer. Reaching the case needs a second client
  /// on <c>/phone</c> while this one sits on <c>/sleep</c>, which is Q12 exactly.
  /// </para>
```

---

### Task 8 — `FakeDuckingService`: make the fake model the NEW production behaviour

**File:** `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs:2302-2560`

⚠ **This is the task most likely to produce a vacuous suite if skimped.** The fake currently mirrors
the OLD production rule at `:2412-2416` — *"The real service raises here only when the set empties"* —
so the starvation test **cannot even be expressed** until this changes, and a Builder who adds the test
without the fake change will watch it pass against a broken implementation. **Do Task 8 before Task 9.**

**8a. `RaiseStateChanged` gains the two fields** (`:2537-2555`) — keep the raising-thread instrument
exactly as it is:

```csharp
  private void RaiseStateChanged(
    IEventAudioSource? source,
    bool isDucking,
    int activeCount,
    float duckLevel,
    DuckingSourceTransition transition,
    int triggeringSourcePriority)
  {
    var previous = Interlocked.Exchange(ref _raisingThreadId, Environment.CurrentManagedThreadId);
    try
    {
      DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs
      {
        IsDucking = isDucking,
        TriggeringSource = source,
        ActiveEventCount = activeCount,
        DuckLevel = duckLevel,
        Transition = transition,
        TriggeringSourcePriority = triggeringSourcePriority
      });
    }
    finally
    {
      Interlocked.Exchange(ref _raisingThreadId, previous);
    }
  }
```

**8b. `StartDuckingAsync`** (`:2366-2392`) — capture inside the lock, raise with it:

Add `int priorityAtStart;` to the locals and assign `priorityAtStart = GetPriorityUnlocked(s);` inside
the existing `lock (Started)` (`:2370-2380`), after the `_active.Add`. Then:

```csharp
    // Captured inside the lock, mirroring DuckingService: the priority the args carry is the one the
    // source had when it JOINED, not one resolved later.
    if (newlyAdded)
    {
      RaiseStateChanged(
        s, isDucking: true, activeCount: count, duckLevel: 20f,
        DuckingSourceTransition.Started, priorityAtStart);
    }
```

**8c. `StopDuckingAsync`** (`:2394-2419`) — **the load-bearing change**:

```csharp
    int remaining;
    int priorityBeforeRemoval;
    lock (Started)
    {
      Stopped.Add(s.Id);

      // ⚠ Captured BEFORE the removals, exactly as DuckingService does. Modelling the capture is the
      // whole point: a fake that read the priority after the removal would answer the category default
      // 8, and the starvation test would pass for the wrong reason.
      priorityBeforeRemoval = GetPriorityUnlocked(s);

      _active.RemoveAll(a => string.Equals(a.Id, s.Id, StringComparison.Ordinal));
      // The real service removes the priority override here, BEFORE it raises. That is what makes
      // GetPriority answer the category default for a source that has just stopped.
      _effective.Remove(s.Id);
      remaining = _active.Count;
    }

    // ⚠ RAISES ON EVERY REMOVAL since PHN-1f, matching DuckingService, with the TRUE aggregate in
    // IsDucking. This line is what makes
    // AHigherPrioritySourceEndingWhileALowerOneContinuesStillWakesTheQueue meaningful: revert it to
    // `if (remaining == 0)` — the pre-PHN-1f rule — and that test must go RED.
    RaiseStateChanged(
      s, isDucking: remaining > 0, activeCount: remaining, duckLevel: remaining > 0 ? 20f : 100f,
      DuckingSourceTransition.Ended, priorityBeforeRemoval);

    DuckingLevelChanged?.Invoke(this, new DuckingLevelChangedEventArgs { TransitionComplete = true });
```

**8d. The four hand-rolled raise helpers** — update each to pass a transition, and keep every remark:

- `RaiseSetEmptied` (`:2482`) → `Ended`, priority `DuckingService.DefaultEventPriority`.
- `RaiseStartedWithNoSource` (`:2501`) → `Started`, priority 0.
- `RaiseStopAll` (`:2528`) → `AllCleared`, priority 0.
- `RaiseStartedAfterItAlreadyLeft` (`:2516`) → `Started`.
  ⚠ **Its remark must be rewritten and the test that uses it re-argued.** The fade-window race it
  reproduces is what the captured priority **closes**, so the args it now builds carry the priority the
  source actually claimed, and the assertion becomes *"the preemption still reads 3, not the default
  8"* rather than *"the guard rejects it"*. Do not delete the helper — the args shape it produces is
  still reachable and is still worth a test.

**8e. One new helper**, for the case that is the whole reason for the args change:

```csharp
  /// <summary>
  /// Models a higher-priority source LEAVING while a lower-priority one keeps ducking — the starvation
  /// case. Before PHN-1f this produced NO RAISE AT ALL from the real service.
  /// </summary>
  public void RaiseEndedWithOthersRemaining(IEventAudioSource source) =>
    StopDuckingAsync(source).GetAwaiter().GetResult();
```

---

### Task 9 — `EventPlaybackServiceTests`: the queue matrix

**File:** `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs`

⛔ **`APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY` (`:1567-1613`) is UPDATED, never
deleted.** It is the one test in that file written to be changed, it says so in its own comment, and
`D28`'s register entry requires the change to *"arrive as an edited assertion in someone's diff rather
than as a silent behavioural shift"*.

**9a. The rewritten characterization test:**

```csharp
  [Fact]
  public async Task APlaybackStartedUnderAHigherPrioritySourceWaitsAndThenPlays()
  {
    // ⚠ THIS WAS APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY, and it is the one test in
    // this file that was written to be changed rather than kept. PHN-1d pinned today's mixing so that
    // D28's queue would arrive as an edited assertion. This is that edit.
    //
    // The assertion that carries the decision is ducking.ActiveEventCount == 1 at the moment audio
    // starts: every other assertion here is about the new playback eventually reaching Playing, and
    // only that one shows the two voices no longer overlap.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    // A doorbell announcement is already sounding at priority 8.
    var blocker = new FakeEventSource();
    ducking.RaiseStarted(blocker, 8);

    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    var accepted = await service.StartAsync(SpeechRequest());
    var waited = await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    // It is WAITING, and it has not made a sound.
    Assert.Equal(accepted.Id, waited.Id);
    Assert.Equal(EventPlaybackState.Waiting, waited.State);
    Assert.Equal(0, source.PlayCalls);
    Assert.Equal(EventPlaybackState.Waiting, service.Current?.State);

    // The doorbell finishes.
    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await ducking.StopDuckingAsync(blocker);
    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Playing, final.State);
    Assert.Equal(1, source.PlayCalls);

    // ⭐ THE ASSERTION THAT IS THE DECISION. One voice, not two.
    Assert.Equal(1, ducking.ActiveEventCount);
  }
```

**9b. The rest of the matrix.** Each is listed in §2.1 with the mutation that must red it.

```csharp
  [Fact] public async Task AWaitingPlaybackIsReportedByCurrent()
  [Fact] public async Task StopAsyncResolvesAWaitingPlayback_AndDisposesWhatItAcquired()
  [Fact] public async Task ASecondStartReplacesAWaitingPlayback()
  [Fact] public async Task AWaitingPlaybackExpiresAsFailedWaitExpired()
  [Fact] public async Task AHigherPrioritySourceEndingWhileALowerOneContinuesStillWakesTheQueue()
  [Fact] public async Task AWaitingPlaybackIsNotWokenByASubThresholdSourceEnding()
  [Fact] public async Task APlaybackAtPriorityEightDoesNotBlockItself()
  [Fact] public async Task AQuietRoomPublishesNoWaitingSnapshotAtAll()
  [Fact] public async Task StopAllDuckingWakesAWaitingPlayback()
  [Fact] public async Task AWaitIsNotMissedWhenTheBlockerEndsWhileTheWaiterIsBeingArmed()
  [Fact] public async Task TheWakeDoesNotStartAudioOnTheRaisingThread()
  [Fact] public async Task AWaitingRemoteMediaSnapshotCarriesTheProvidersDuration()
```

Two need their mechanism spelled out, because the obvious implementation is wrong.

**`AWaitingPlaybackExpiresAsFailedWaitExpired`** — the deterministic one:

```csharp
    var time = new FakeTimeProvider();
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts, ducking: ducking, timeProvider: time,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxQueuedWaitSeconds = 30
      });

    ducking.RaiseStarted(new FakeEventSource(), 8);

    // ⚠ The rendezvous is the WAITING SNAPSHOT, not a delay. Advancing the clock before the service
    // has armed its Task.WaitAsync would advance past nothing, and the test would then hang on the
    // Failed snapshot — a race that reads as an unrelated timeout. CLAUDE.md § Test Timing: count the
    // observation, never the elapsed time.
    var waiting = NextSnapshotWith(service, EventPlaybackState.Waiting);
    await service.StartAsync(SpeechRequest());
    await waiting.WaitAsync(TimeSpan.FromSeconds(5));

    var failed = NextSnapshotWith(service, EventPlaybackState.Failed);
    time.Advance(TimeSpan.FromSeconds(30));
    var final = await failed.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(EventPlaybackState.Failed, final.State);
    Assert.Equal("WaitExpired", final.FailureReason);
    Assert.Equal(0, source.PlayCalls);
    // C-57: the acquired source was disposed rather than leaked.
    Assert.Equal(1, source.DisposeCalls);
```

⚠ **Confirm `FakeEventSource` exposes `DisposeCalls`** before relying on it; if it does not, **add
it** — that counter is the only thing that can falsify the C-57 leak, and without it two of these tests
assert nothing about the fix that motivated them.

**`AWaitIsNotMissedWhenTheBlockerEndsWhileTheWaiterIsBeingArmed`** — the C-66 sampler:

```csharp
    // ⚠ SAY WHAT THIS DOES AND DOES NOT PROVE. There is no rendezvous inside WaitForClearAirAsync, so
    // this cannot deterministically place the blocker's end inside the arm-then-recheck window. It is
    // a REPETITION test: N runs, each starting the playback and ending the blocker immediately, so the
    // interleaving is sampled rather than forced. Delete the re-check in WaitForClearAirAsync and this
    // reds MOST of the time, not every time.
    //
    // It is in the SAFE direction of CLAUDE.md § Test Timing: starvation can only make it pass more
    // often, never flip a pass to a fail. Recorded as a sampler rather than implied to be a proof —
    // §2.2 item 1 carries it as a gap.
```

⚠ Give each iteration a real bound (await the `Playing` snapshot with a 5 s timeout) and keep N small
enough that the suite does not slow measurably. **20–50 is the range; do not add seconds to CI.**

---

### Task 10 — `DuckingServiceTests` and `DuckingServiceCharacterizationTests`: against the real service

**Files:** `tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs`,
`.../DuckingServiceCharacterizationTests.cs`

⚠ **Third rewrite of these two files in three PRs.** Existing tests that assert the OLD raise rule are
**updated with their reasoning**, not deleted —
`StopDuckingAsync_RaisesDuckingStateChangedEvent_WhenLastEvent` (`DuckingServiceTests.cs:222`) and
`StopDuckingAsync_DoesNotRestoreVolume_WhenOtherEventsActive` (`:204`) both encode it.

New tests:

```csharp
  [Fact] public async Task AnEndedRaiseCarriesThePriorityTheSourceHadBeforeItWasRemoved()
  [Fact] public async Task AnEndedRaiseWithOtherSourcesStillActiveReportsIsDuckingTrue()
  [Fact] public async Task ASourceLeavingWhileOthersRemainStillRaises()
  [Fact] public async Task AStartedRaiseCarriesThePriorityCapturedBeforeTheAttackFade()
  [Fact] public async Task StopAllDuckingRaisesAllClearedWithANullSource()
```

`AnEndedRaiseWithOtherSourcesStillActiveReportsIsDuckingTrue` is the one that guards the hazard: it
must assert `IsDucking == true` **and** `Transition == Ended` on the same args, because it is the
combination — not either field alone — that stops `AudioManager` restoring the radio mid-announcement.

**And an `AudioManager` test for C-58**, in whichever file already covers that handler:

```csharp
  [Fact] public void AnEndedRaiseWithOthersRemainingDoesNotClearTheDuckingMultiplier()
  [Fact] public void ADefaultedTransitionOnAnIsDuckingFalseRaiseStillClearsTheMultiplier()
```

The second is the C-58 pin. It constructs args with `IsDucking = false` and **no** `Transition` set —
the defaulted `Started` — and asserts `ClearDuckingMultiplier` was still called. Key the outer branch
on `Transition` and it goes red. ⚠ **If `AudioManager` has no existing test seam for this handler, say
so and add one** rather than skipping the test; the whole of C-58 rests on this assertion.

---

### Task 11 — `SleepService` tests: C-56, written so the NEXT member reds it too

**File:** `tests/Radio.API.Tests/` — the file that already covers `SleepService`'s §7.5 rule.

```csharp
  [Fact] public async Task EnteringSleepStopsAWaitingPlayback()
  [Fact] public async Task TheSleepScreenReportStopsAWaitingPlayback()
```

and the one that makes C-56 hold for the member after this one:

```csharp
  [Fact]
  public async Task TheSleepRuleCoversEveryNonTerminalState()
  {
    // ⚠ Written against the ENUM rather than a hand-listed set, because the failure C-56 describes is
    // SILENT: SleepService's rule is an allow-list, so a new non-terminal member is excluded by
    // default and nothing else in the suite would notice. This reds when someone adds a member and
    // does not list it there.
    //
    // The terminal three are the deny-list Radio.Web's IsLive uses, so the two rules end up asserted
    // against one definition rather than two.
    var terminal = new[]
    {
      EventPlaybackState.Completed, EventPlaybackState.Stopped, EventPlaybackState.Failed
    };

    foreach (var state in Enum.GetValues<EventPlaybackState>().Except(terminal))
    {
      // … drive the real SleepService path with a Current snapshot in `state`, assert it stopped …
    }
  }
```

⚠ **This test must drive the real `SleepService` path**, not re-implement the predicate. A test that
restates the allow-list is a copy of the bug.

---

### Task 12 — documentation

1. **`design/FUTURE-WORK.md`** — file the two things this row declines: C-63's arriving-at-`/sleep`
   case (pointing at ADR §14 Q12), and the blocker's identity on the wire (§0.6 item 3), with the
   request path.
2. **`design/INTEGRATIONS.md`** — `MaxQueuedWaitSeconds` in the `GvMedia` config table, **if that
   table already lists the other keys. Check first**; do not add a table.
3. **`design/DECISION-LOG.md`** — a one-line pointer that `D28` is implemented as of this row, in the
   shape the existing lines use.
4. ⛔ **Do NOT edit** `docs/BUILDER_QUEUE.md`, `docs/HANDOFF-GA-PUNCH-LIST.md`,
   `docs/HANDOFF-NEXT-SESSION.md`, or **ADR-029**. §6 carries the proposed queue rows; the punch list
   and the ADR belong to other passes.

---

### Task 13 — build, test, and the scope gate

```bash
dotnet build --configuration Release          # 0 warnings; Release treats them as errors
dotnet test  --configuration Release
```

```bash
# ⛔ NO Radio.Web change (C-64). The wire carries strings and IsLive is a deny-list.
git diff --name-only main... | grep '^src/Radio.Web/'
# → nothing

# The ActiveEventCount guard is DELETED, not supplemented (Task 6f).
git diff main... -- src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs | grep '^+.*ActiveEventCount'
# → nothing

# The preemption path no longer resolves priority itself.
git diff main... -- src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs | grep '^+.*GetPriority(trigger)'
# → nothing

# ⛔ C-58: AudioManager's multiplier edge is IsDucking, never Transition.
grep -n 'ClearDuckingMultiplier' -B 8 src/Radio.Infrastructure/Audio/Services/AudioManager.cs
# → the enclosing test must be `if (e.IsDucking)` / else, NOT a Transition switch

# ⛔ C-56: Waiting is in the sleep allow-list.
grep -n 'EventPlaybackState.Waiting' src/Radio.API/Services/SleepService.cs
# → one hit, inside StopAttendedPlaybackAsync's state test

# ⛔ Waiting is the LAST enum member and its value is 6.
grep -n 'Waiting = 6' src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs
# → one hit, after `Failed = 5,`

# C-57: the wait's throwing path disposes what it acquired.
git diff main... -- src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs | grep '^+.*DisposeOrphanAsync'
# → at least one NEW call site, in AcquireAndPlayAsync's wait guard

# No poll, tick or per-client timer anywhere in the diff (trap 5).
git diff main... -- src/ | grep -E '^\+.*(Task\.Delay|PeriodicTimer|while \(true\))'
# → nothing

# The config key went to appsettings.json and NOT to a Production file (PHN-1b C-14).
git diff --name-only main... | grep 'appsettings'
# → src/Radio.API/appsettings.json only

# The characterization test was RENAMED, not deleted.
git diff main... -- tests/ | grep 'StillMixes_TODAY'
# → exactly one `-` line and no `+` line; the new name must appear as a `+`

# The full expected file list. Anything else is scope creep.
git diff --name-only main...
```

Expected files, and nothing else:

```
src/Radio.Core/Interfaces/Audio/IDuckingService.cs
src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs
src/Radio.Core/Configuration/GvMediaOptions.cs
src/Radio.Infrastructure/Audio/Services/DuckingService.cs
src/Radio.Infrastructure/Audio/Services/AudioManager.cs
src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs
src/Radio.API/Services/SleepService.cs
src/Radio.API/appsettings.json
tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs
tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs
tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs
tests/Radio.API.Tests/<the SleepService test file>
tests/Radio.Infrastructure.Tests/<the AudioManager ducking-handler test file>
design/FUTURE-WORK.md
design/INTEGRATIONS.md
design/DECISION-LOG.md
```

---

## 2. Test Plan

### 2.1 ⚠ Every pin is stated with the mutation that must RED it

**Four consecutive cycles have each found a test that passed against a deliberately broken
implementation** — three of them in one cycle, all plan-specified or review-passed. A test name is not
evidence. The column that matters is the mutation.

| Test | Mutation that must red it |
|---|---|
| `APlaybackStartedUnderAHigherPrioritySourceWaitsAndThenPlays` | Delete the `WaitForClearAirAsync` call in `AcquireAndPlayAsync` → `PlayCalls == 1` immediately and `ActiveEventCount == 2` |
| `AHigherPrioritySourceEndingWhileALowerOneContinuesStillWakesTheQueue` | ⭐ Revert `FakeDuckingService.StopDuckingAsync`'s raise to `if (remaining == 0)` — the pre-`PHN-1f` rule — **or** revert `DuckingService`'s. **This is the whole reason for the args change** |
| `AWaitingPlaybackIsNotWokenByASubThresholdSourceEnding` | Make `TryWakeWaitingPlayback` wake on any raise instead of re-evaluating the predicate |
| `AWaitingPlaybackExpiresAsFailedWaitExpired` | Drop the timeout argument from `waiter.Task.WaitAsync` → the wait never ends and the 5 s bound turns the hang into a red |
| `AWaitingPlaybackExpiresAsFailedWaitExpired` (`DisposeCalls`) | Delete C-57's `catch { await DisposeOrphanAsync(…); throw; }` → `DisposeCalls == 0` |
| `StopAsyncResolvesAWaitingPlayback_AndDisposesWhatItAcquired` | Same deletion; also reds if `StopAsync` stops matching a non-adopted `_current` |
| `ASecondStartReplacesAWaitingPlayback` | Make `StartAsync`'s replacement arm skip a playback whose `Source` is null |
| `APlaybackAtPriorityEightDoesNotBlockItself` | ⚠ Its real mutation is the reverse of the obvious one: **move `WaitForClearAirAsync` below `StartDuckingAsync`** and the playback blocks on itself until `WaitExpired`. Adding a self-exclusion to the predicate leaves it green, which is the point — it shows the exclusion is unnecessary |
| `AQuietRoomPublishesNoWaitingSnapshotAtAll` | Publish `Waiting` unconditionally before the predicate → an extra broadcast on every playback |
| `StopAllDuckingWakesAWaitingPlayback` | Move `TryWakeWaitingPlayback()` below the `TriggeringSource is not null` check → a null-source raise never wakes |
| `TheWakeDoesNotStartAudioOnTheRaisingThread` | Drop `RunContinuationsAsynchronously` from `BeginWait` → `PlayAsync` happens inline on the raising thread, and the fake's instrument sees it |
| `AnEndedRaiseCarriesThePriorityTheSourceHadBeforeItWasRemoved` | Move the capture below `_sourcePriorities.Remove` → reads 8 instead of 3 |
| `AStartedRaiseCarriesThePriorityCapturedBeforeTheAttackFade` | Move the capture below `ApplyFadeAsync` → the fade-window race returns |
| `AnEndedRaiseWithOtherSourcesStillActiveReportsIsDuckingTrue` | Raise `isDucking: false` unconditionally → red, and the live consequence is the radio at full volume mid-announcement |
| `ADefaultedTransitionOnAnIsDuckingFalseRaiseStillClearsTheMultiplier` | ⭐ Key `AudioManager`'s **outer** branch on `Transition` (C-58) → `ClearDuckingMultiplier` is skipped, radio stuck ducked |
| `TheSleepRuleCoversEveryNonTerminalState`, `EnteringSleepStopsAWaitingPlayback` | Remove `EventPlaybackState.Waiting` from `SleepService`'s allow-list (C-56) |

**Determinism.** Every timed assertion above is driven by `FakeTimeProvider.Advance` or by a snapshot
rendezvous (`NextSnapshotWith`), never by `Task.Delay`. The wake is synchronised on the `Playing`
snapshot; the negative preemption cases keep `PreemptionTail`, the internal seam `PHN-1d` added for
exactly the case where there is no event to wait for.

### 2.2 ⚠ What these tests CANNOT prove — said plainly rather than implied

1. ⛔ **`AWaitIsNotMissedWhenTheBlockerEndsWhileTheWaiterIsBeingArmed` is a SAMPLER, not a proof.**
   There is no seam that lets a test place a raise inside `WaitForClearAirAsync`'s arm-then-re-check
   window. Repetition samples the interleaving; it does not force it. A run that passes 50/50 has not
   shown the window is closed — it has shown it was not hit. **The re-check is justified by C-66's
   argument, not by this test.** Adding the seam would mean a test-only hook inside the wait, which is
   a bigger change to the production path than the race is worth; recorded rather than done.
2. ⛔ **Nothing here proves the wake works against the REAL `DuckingService` end to end.** Task 10
   tests `DuckingService`'s raise; Task 9 tests `EventPlaybackService`'s reaction to a *fake's* raise;
   no test wires the two together, because the seam takes `IDuckingService` and every existing test
   substitutes the fake. **So the fake's fidelity is the load-bearing assumption of this whole row** —
   which is why Task 8 comes before Task 9 and why its comments name the mutation.
3. ⛔ **No test can see the box's audio.** That two voices do not in fact overlap is
   `ducking.ActiveEventCount == 1` — a count, not a sound. **PR 6's UAT is where this is heard**, and
   the check is: start a voicemail while a doorbell announcement is sounding, confirm the chip says
   waiting, confirm the voicemail starts *after* the announcement ends, and confirm the room never
   carries both.
4. ⛔ **`MaxQueuedWaitSeconds = 30` has never been exercised on the appliance**, only against
   `FakeTimeProvider` — the same caveat ADR Amendment 2 records for the 300 s cap. Nor has the
   interaction between a wait and that cap: the cap is armed *after* `PlayAsync`, so a wait does not
   consume it, and **that is asserted nowhere**. Carry both to PR 6's UAT.
5. ⛔ **The `/sleep` latency ADR §16.5 asked for is still unmeasured**, and a wait now sits inside
   exactly that window. Carried, not closed.
6. ⛔ **No test covers two clients.** `Waiting` is global state like every other snapshot, and the
   multi-client questions the arc already carries (§14 Q12) are untouched by this row and unproven
   by it.

### 2.3 Commands

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --verbosity normal
dotnet test  --configuration Release --filter "FullyQualifiedName~EventPlaybackServiceTests"
dotnet test  --configuration Release --filter "FullyQualifiedName~DuckingService"
dotnet test  --configuration Release --filter "FullyQualifiedName~SleepService"
```

---

## 3. Self-review

- **Placeholder scan.** No `TBD`, no *"similar to Task N"*, no *"implement later"*. Every task carries
  literal code or a literal edit with a file and a line range. The three places that say *"check
  first"* — the `EventPlaybackRejection` cref name, `FakeEventSource.DisposeCalls`, the `AudioManager`
  test seam — are named as verifications with a stated consequence, not as gaps.
- **Spec coverage.** `D28`: wait ✓ (Task 6b), not mix ✓ (Task 9a's `ActiveEventCount == 1`), not
  refuse ✓ (`Waiting` precedes any failure, and `MaxQueuedWaitSeconds` has no off switch). §5's five
  questions: Q1 ✓ §0.2, Q2 ✓ Task 2, Q3 ✓ Task 6b, Q4 ✓ Tasks 1/4/5/6f, Q5 ✓ Task 6d **plus C-57**.
- **Type consistency.** `MaxQueuedWaitSeconds` is `int` seconds like its four siblings;
  `TriggeringSourcePriority` is `int` like `GetPriority`'s return; `Waiting = 6` continues the enum's
  explicit values; `BeginWait` returns the **non-generic** `TaskCompletionSource`, which is what
  `Task.WaitAsync(TimeSpan, TimeProvider, CancellationToken)` needs.
- **Scope.** Thirteen tasks, sixteen files, no `Radio.Web`. Task 13's greps fail the build on nine
  specific ways of exceeding it.
- **Comment accuracy.** Seven corrections are owed and each is a task, not a note: Task 2's summary
  line, Task 3 (C-62), Task 4e, Task 6f's three, Task 6h (C-61), Task 7b (C-63), Task 8d's remark.
  **This is the sixth through twelfth instance of the class in this arc**; the discipline is making
  them in the PR that falsifies them.
- **Where I could not verify.** §2.2, and §5 below.

---

## 4. What this plan deliberately does not do, and why

1. **Does not add a pending slot.** §0.2. Every property it would need already exists on `_current`.
2. **Does not make the wait configurable off.** A `0` resolves to mixing, which is `D28`'s rejected
   option wearing a config key.
3. **Does not name the blocker on the wire.** §0.6 item 3. A field no renderer branches on is a wire
   commitment bought with nothing; the request path is recorded.
4. **Does not restructure `Publish` to happen outside `_gate`.** The shipped remark
   (`EventPlaybackService.cs:1317-1325`) flags it and declines it, and this row adds no subscriber that
   forces the question — `PublishNonTerminal(Waiting)` is called from the acquisition task **before**
   it takes the gate, so it is the one publish in the file that never runs under it.
5. **Does not touch the 300 s cap.** §0.5 item 4.
6. **Does not fix `AudioStateStore.NotifyAsync`.** §6.2 proposes it as its own row, with the tiering
   argument and the ordering call.
7. **Does not amend ADR-029 §16.5.** §5.1 records it for an Architect pass.

---

## 5. Carried forward

### 5.1 ⭐ To an Architect pass — §16.5's own table overstates its case

ADR-029 `:1281-1284` claims the `SetSleepScreenVisible(true)` edge *"covers rows 1, 2, 3, 5"* of
§16.4's entry-point table. **That is true of PRODUCING THE FACT and false of STOPPING THE PLAYBACK**,
and the same word is doing both jobs in one cell. `PHN-1e`'s Builder flagged it rather than amending a
merged ADR; this plan does the same. Four reasons, all sourced from that same section:

1. **Row 2 is a hard navigation, and something else already stops the audio.** §16.4's own
   Circuit-effect column says row 2 tears the circuit down, and §16.1 (`:973-977`) says the idle path
   is therefore `1 → 0 → 1` and *"the circuit rule stops it"*. On that row the screen edge is not the
   mechanism that stops anything in the resting single-kiosk configuration.
2. **The fact arrives late.** §16.5 item 2 (`:1303-1306`): the report lands only after a full page
   load and the first *interactive* render on a brand-new circuit — *"plausibly seconds, and attended
   audio continues through it. Unmeasured."*
3. **The fact can be produced without the stop being observed.** §16.5 item 1 (`:1297-1302`): the
   setter is `void` on a synchronous action while the stop is `async`.
4. **The fact is a global last-writer-wins bool**, so it does not track *"a playback is unattended"* at
   all — §16.5 item 3, recorded as §14 **Q12**.

⚠ **And one this row adds.** §16.5 mandates *edge* semantics at the write sites (`:1291`), so a second
client arriving at `/sleep` while the flag is already `true` is a no-op — no transition, no stop. Row 5
is where that is most reachable. ⛔ **Do not edit the ADR from this row.**

### 5.2 To PR 6 (`PHN-2` — retire the `<audio>` element)

- **The chip renders `Waiting` per §0.6**: live, offers Stop, **does not** run the progress bar, and
  says *why* rather than showing a bare spinner.
- **`WaitExpired` needs copy**, not a mechanism — the error state ADR §12 item 3 requires already
  exists for `MediaNotFound`.
- **Carry §2.2 items 3, 4, 5 and 6** into the UAT: the two-voice check by ear, `MaxQueuedWaitSeconds`
  on the box, the wait/cap interaction, and §16.5's unmeasured `/sleep` latency.
- **If the chip needs the blocker's identity, ask** — §0.6 item 3 records why it was not added
  speculatively.
- Everything `PHN-1e` §5.2 carried forward still stands and is not restated here.

### 5.3 To the sleep arc / Designer

**C-63's case is unowned**: a playback *started* while the console is already on `/sleep`. Declined by
this row on the dependency direction; it is ADR §14 **Q12**'s multi-client question. Filed in
`FUTURE-WORK.md` by Task 12. **No queue row is proposed for it**, because it is a design question and
not a defect.

### 5.4 To the owner — one thing to know, and it is not a question

**Entering sleep now cancels a WAITING playback as well as a playing one, and a page reload stops one
too.** Neither is an extension of `D30` and neither needs a ruling:

- The **reload** behaviour is **inherited with zero code** — `IsLive` is a deny-list — and the
  alternative is worse than the case `D30` ruled on: a wait that survived a reload would start audio up
  to thirty seconds *after* the user left the page, which is unattended audio, forbidden independently
  by ADR §7.2's principle.
- The **sleep** behaviour is **C-56**, and ADR §16.5's own stated reason for covering `Preparing` —
  *"a fetch in flight would otherwise start audio moments after the panel goes dark"* — is the same
  argument with a longer fuse.

⚠ **The one thing worth an owner's eye** is the shape rather than either rule: **a queued voicemail can
be discarded by something the user did not connect to it** — a reload, an idle timeout — and the
console says only *"stopped"*. It is consistent, it is defensible, and it is the direction `D30` chose.
If it proves annoying in use, the fix is copy on PR 6's chip, not a change to any of these rules.

---

## 6. Proposed `BUILDER_QUEUE` rows

⚠ **Not applied by this plan.** `docs/BUILDER_QUEUE.md`, `docs/HANDOFF-GA-PUNCH-LIST.md` and
`docs/HANDOFF-NEXT-SESSION.md` were all out of scope for this pass. The rows below are proposals, in
the schema that file's § Queue already uses
(`| # | Item | Status | Plan | Spec / handoff | Depends on | Branch |`).

### 6.1 `PHN-1f` — flip 🔒 → 📋 and link this plan

The existing row's **Item** cell needs no rewrite; its `Status`, `Plan` and `Depends on` cells do. The
row's own text sets the flip condition — *"Flip 🔒 → 📋 only when a plan file exists and this cell
links it"* — and it now does.

- **Status:** `🔒` → `📋`
- **Plan:** `*to be written — design is [PHN-1e plan §5](…)*` →
  `` [`design/plans/PHN-1f-the-wait-then-play-queue.md`](../design/plans/PHN-1f-the-wait-then-play-queue.md) ``
- **Depends on:** `PHN-1e` → `✅ **MET — PHN-1e merged 2026-09-04** as [#561](https://github.com/mmackelprang/RTest/pull/561), merge commit `4ec0fb85`. **This row is CLAIMABLE NOW.**`
- **Append to Item**, because these change the work and are not in the row today:

  > ⚠ **TWO THINGS THE PLANNING PASS FOUND THAT `PHN-1e` §5 DID NOT, both in §0.4.** **C-56:**
  > `SleepService`'s stop is an **allow-list** (`SleepService.cs:356-358`) while
  > `EventPlaybackSnapshotDto.IsLive` is a **deny-list** (`ApiModels.cs:1522`), so adding `Waiting`
  > silently removes it from ADR §7.5's `/sleep` rule while adding it to the circuit backstop for free
  > — a queued playback would then start audio up to 30 s after the panel goes dark, on `EmptyLayout`,
  > with no stop control anywhere on screen. **C-57:** the wait adds two throwing exits between
  > acquisition and `TryAdopt`, and neither `TearDownAsync` nor `FailAsync` can release an *unadopted*
  > source — so without a `DisposeOrphanAsync` guard the `RemoteMedia` arm leaks an open `FileStream`
  > over the cached recording. ⭐ **And one thing that got SMALLER:** `Waiting` needs **no `Radio.Web`
  > change and no broadcast change at all** (C-64) — `.ToString()` on the hub plus `IsLive`'s
  > deny-list carry it, which is C-47's payoff arriving.

### 6.2 A new row — `AudioStateStore.NotifyAsync` awaits only the last subscriber

*(ID for the owner to assign. No punch-list ID exists yet; `UI-` fits the surface, and the queue also
uses `OPS-`, `LOG-`, `AUD-`.)*

- **Item:** **`AudioStateStore` notifies N subscribers and awaits one.** 🟡 **P2 — and PR 6 is NOT the
  deadline; see the argument below.** `AudioStateStore.NotifyAsync`
  (`src/Radio.Web/Services/AudioStateStore.cs:378-391`) does `await handler.Invoke()` on a multicast
  `Func<Task>`. `Delegate.Invoke` runs every handler but **returns only the last one's Task**, so every
  earlier subscriber runs to its first `await` and its continuation is never observed — the
  `try`/`catch` protects exactly one of N, and the other N−1 exceptions reach no log at all. **Two more
  sites hand-roll the identical defect and are NOT fixed by fixing `NotifyAsync`:**
  `OnHubRadioStateChanged` (`:202-212`) and `OnHubSleepStateChanged` (`:215-221`) — and the second has
  **no `try`/`catch` at all**. ⭐ **A second, sharper half the deferral note did not name:** a
  subscriber that throws **synchronously** — before its first `await` — propagates straight out of
  `Invoke`, so **every handler registered after it never runs.** That is starvation, not just a lost
  log line, and `DuckingService`'s own raise guard (`DuckingService.cs:481-483`) documents the same
  shape as a known, accepted limitation for two subscribers. **Fix:** iterate `GetInvocationList()`,
  await each, catch per subscriber; apply the same shape to the two hand-rolled sites. **Est. 0.5 d.**
- **Status:** `📋`
- **Plan:** *to be written — small enough for a plan-in-the-row if the owner prefers*
- **Spec / handoff:** `docs/BUILDER_QUEUE.md` § *What the two reviewers found* (`:284-295`)
- **Depends on:** `—`
- **Branch:** `fix/audio-state-store-multicast-notify`

---

#### ⚠ The tiering argument — and it contradicts the deferral note on two counts

The deferral note says the defect is *"shared by all eleven events"*, that *"the store is a singleton
and every circuit's components subscribe, so N > 1 is the normal multi-client state"*, and that *"PR 6
is the deadline that matters"*. **Two of those three are wrong, and the third is right for a different
reason.** All verified at `4ec0fb85`:

**1. Nine of the eleven events have ZERO production subscribers.** `AudioStateStore` is injected in
exactly **one** place in the whole tree — `MainLayout.razor:20` — and subscribes exactly **two** of its
events: `EncoderConfigStatusChanged` (`:402`) and `EncoderConnectionChanged` (`:403`). Its only other
consumer, `AttendedPlaybackCircuitHandler`, subscribes to nothing; it reads `EventPlayback` directly.
⚠ **The count that looks alarming comes from `AudioStateHubService`, a different type** — most
`@inject`ed fields named `AudioState` in this repo (e.g. `Sleep.razor:13`) are the **hub service**, not
the store. That naming collision is almost certainly what produced the "all eleven" reading.

**2. `EventPlaybackChanged` has ZERO subscribers today, and PR 6 takes it to ONE.** A multicast defect
needs **N ≥ 2** to bite: with one subscriber, `Invoke` returns that subscriber's own Task, it is
awaited, and its exceptions are caught — **no defect at all.** So **PR 6 does not cross the threshold**
and is not the deadline. What crosses it is a **second circuit**: the store is a singleton and
`MainLayout` is per-circuit, so two open browsers already give `EncoderConnectionChanged` two
subscribers **today** — which is the box's documented second-browser case (ADR §16.3 P4), not a
hypothetical.

**3. Tiered against punch list §1**, which tiers by *consequence in the cabinet*:

- **(a)** wrong or dangerous on day one — no. **(d)** substrate for verification — no. **(e)**
  permanent at install — no.
- **(c)** unrecoverable without a laptop — **no.** `ThrowUnobservedTaskExceptions` is **not set
  anywhere in the tree**, so on .NET 10 an unobserved task exception is swallowed at finalization.
  There is no process-crash path and nothing wedges.
- **(b)** *"the machine reports success and does nothing; a control does nothing"* — **not met by the
  async half.** Every subscriber still runs synchronously up to its first `await`, and both live
  handlers reach `await InvokeAsync(…)` there — so the re-render **is** dispatched on every circuit
  regardless. What is lost is a log line on a failure, plus the fact that `NotifyAsync` returns early.
  **The synchronous-throw half COULD meet (b)** — one bad subscriber starves the rest — but neither
  shipped handler can throw synchronously: both open with a null-guarded property read
  (`MainLayout.razor:1329`, `:1345`) before their first await.

**Therefore P2**, on §1's own definition: *"genuine work with real value and no schedule pressure —
refactors, coverage, hygiene, second-order polish."*

⚠ **What would move it to P1, stated so the tier can be re-argued rather than re-guessed:** a store
subscriber whose handler either (i) can throw **before** its first `await`, or (ii) does work after an
await that the *caller* of `UpdateX`/`NotifyAsync` depends on having finished. Neither exists today,
and **PR 6's chip is very unlikely to be either** — a chip handler is
`await InvokeAsync(StateHasChanged)`, the same shape as the two that already exist.

**Ordering call: it can land AFTER PR 6, and it should not block it.** PR 6 does not change the
reachability threshold, and the fix touches the failure semantics of ten events PR 6 does not own —
exactly the unrelated blast radius the `PHN-1e`/`PHN-1f` split exists to refuse. ⚠ **But it should land
before whatever adds a SECOND subscriber to any one store event**, and PR 6 is the row most likely to
be followed by one. **Recommendation: queue it now at P2, unblocked, and clear it in any cycle with
slack — including before PR 6 if one appears, since it is half a day and touches no file this arc
needs.**
