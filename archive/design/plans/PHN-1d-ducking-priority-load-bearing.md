# PLAN — `PHN-1d` · ADR-029 PR 4: priority becomes load-bearing

> **Status:** ready for Builder **once `PHN-1c` has merged** — §0.3 is the list of things to re-check
> first. Written 2026-09-04 against `6b3dcc2e`; revised the same day for owner decision **C-46**, which
> removed the mirror-direction work from this row and sent it to PR 5 as a queue.
> **Punch list:** [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../docs/HANDOFF-GA-PUNCH-LIST.md) §3.5 `PHN-1` (P0), §2 `O6`.
> **Decision of record:** [ADR-029](../decisions/2026-08-03-gv-audio-through-engine.md) — **D5**, §6.1, §6.2, §6.3.
> **Sequencing:** [`design/plans/PHN-arc-pr-breakdown.md`](PHN-arc-pr-breakdown.md) — **this plan is PR 4 of 7.**
> The order is unchanged; nothing here re-sequences the arc.
> **Depends on:** `PHN-1a` ✅ ([#528](https://github.com/mmackelprang/RTest/pull/528)),
> `PHN-1b` ✅ ([#534](https://github.com/mmackelprang/RTest/pull/534)), and **`PHN-1c` 🚧 in flight** on
> `feat/phn-1c-event-playback-service`, whose service code was read at `56fef797` — see §0.3 for what
> that verified and what it did not.
> **Predecessor plans:** [`PHN-1a`](PHN-1a-event-playback-seam-contracts.md) §0.4 (C-1…C-11) and §5,
> [`PHN-1b`](PHN-1b-gvmedia-client-cache-and-auth.md) §0.3/§0.4 (C-12…C-20) and §5, and
> [`PHN-1c`](PHN-1c-event-playback-service-and-route.md) §0.4 (C-21…C-33) and §5.
> **Those lists are authority wherever they disagree with the ADR, and `PHN-1c`'s is authority over
> both.** Twelve further contradictions — **C-34…C-45** — are resolved in §0.4 below, four of which
> change what PR 4 builds. **⭐ Plus C-46, an owner decision of 2026-09-04 that REMOVES work from this
> row**: the mirror direction of D5 rule 2 becomes a queue rather than a refusal, and lands in PR 5 with
> the broadcast and the chip that make a waiting state visible. It needs a D-number in the punch list.
>
> ⚠ **This is the row the breakdown calls "the one to review hardest."** It is the first PR in this repo
> that makes a shared audio service behave differently for every caller, and the failure modes are
> audible rather than test-visible. §0.7 enumerates them.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

PR 3 shipped a seam that can play one attended thing and stop it on request. **PR 4 is the PR that lets
something else stop it.** Two changes, and everything else in this plan is a consequence of one of them:
`DuckingService.StartDuckingAsync` starts raising `DuckingStateChanged` for **every event source that
joins the ducking set**, not only for the one that caused the fade; and `EventPlaybackService`
subscribes to that event and stops itself when a source at or above `GvMedia:PreemptAtPriority` (8)
starts. That is ADR-029 D5 §6.3, verbatim. **It is also the first load-bearing use of `GetPriority`,
which has been read in exactly one place — inside a method with zero non-test callers** — so a
subsystem that has been decorative for the life of this project starts arbitrating what the room hears,
and `design/INTEGRATIONS.md`'s standing correction has to be rewritten in the same PR.

⚠ **It is HALF of D5, and the other half is deferred on purpose.** A playback *starting* while a ≥ 8
source is already sounding must wait for it and then play — the owner's decision of 2026-09-04, §0.4
**C-46** — and that queue lands in **PR 5**, with the broadcast and the chip that let a user see it is
waiting. PR 4 builds no part of it and pins today's behaviour instead (Task 6).

**Nothing in `Radio.Web` changes. No route changes. No broadcast, no duration cap, no `CircuitHandler`
— all PR 5. No config key is added: `GvMedia:PreemptAtPriority` already ships, in both
`GvMediaOptions.cs` and `src/Radio.API/appsettings.json`, and has never been read.**

### 0.2 The one engine change, and the three properties that follow from it being synchronous

`DuckingService` raises its events by a bare `Invoke` on the calling thread:

```csharp
// src/Radio.Infrastructure/Audio/Services/DuckingService.cs — RaiseDuckingStateChanged, today
DuckingStateChanged?.Invoke(this, args);
```

There is no dispatch, no queue and no `try`. Until this PR the only subscriber was
`AudioManager.OnDuckingStateChanged`, which logs and — on the `false` branch only — calls
`ClearDuckingMultiplier`. PR 4 adds the first subscriber that can stop a playback. Three properties
follow, and every one of them is a task below:

1. **The handler runs on the caller's thread**, which on the live path is `AnnouncementService`
   mid-announcement, reached from `POST /api/notifications/announce`. It must return promptly and must
   never wait on `EventPlaybackService._gate` — that gate is held by this very service whenever the
   raise came out of `TearDownAsync`. **Decide synchronously, dispatch the stop** (Task 4).
2. **A throwing subscriber propagates into the announcement path.** Guard the raise (Task 2).
3. **The determinism problem is the dispatch, not a timer.** PR 4 adds no timer, so it needs no
   `TimeProvider`; what it needs is a way for a test to synchronise on the *decision* having been made
   rather than on time passing. Task 4 adds an internal `PreemptionTail` for exactly that (C-44).

### 0.3 ⚠ Re-check after PR 3 lands — how far this plan was verified, and where it was not

`PHN-1c` was **in flight** on `feat/phn-1c-event-playback-service` while this plan was written, and this
plan was written **inside that working tree**. That turned out to be an advantage rather than a hazard:
by the time the tasks below were drafted, PR 3 had already committed
`src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs` at **`56fef797`**, so most of what
follows was read out of real code rather than out of plan text. **What it is not is merged**, and PR 3's
own pre-merge review can still change any of it.

**Verified against `56fef797` on the `PHN-1c` branch** — re-grep, do not re-derive:

| # | Assertion | Status | If review changed it |
|---|---|---|---|
| R1 | `EventPlaybackService` exists, is `sealed`, implements `IEventPlaybackService, IDisposable`, registered **singleton** by `AddEventPlayback` | ✅ verified | Stop. Tasks 4-6 have no host; re-plan rather than improvise. |
| R2 | `private readonly SemaphoreSlim _gate = new(1, 1)` (:55), `private readonly object _stateLock` (:58), `private Playback? _current` (:59), private nested `Playback` (:722) with `Source`, `Token`, `Cancel()`, `ClaimTerminal()` — **and `IsTerminal`** (:742, `Volatile.Read`) | ✅ verified | Adapt names in Tasks 4-6. The shapes are load-bearing; the identifiers are not. |
| R3 | Constructor takes `IOptionsMonitor<GvMediaOptions>` (:74) and `IDuckingService` (:78) | ✅ verified — **so Task 4 adds no constructor parameter** | If either is dropped, add it back; neither is a new dependency for the assembly. |
| R4 | `TearDownAsync` (:581) is called only under `_gate`, calls `StopDuckingAsync` then `StopAsync` then `DisposeAsync`, each independently guarded, and returns early when `Source` is null | ✅ verified | If teardown can run outside `_gate`, Task 5's serialisation is insufficient. Re-plan Task 5. |
| R5 | `AcquireAndPlayAsync` assigns `playback.Source = source` **before** `SetPriority`/`StartDuckingAsync` | ✅ verified | Both orderings are required by Task 4's identity check and by C-36. Restore them. |
| R6 | `StopAsync` (:180) takes `_gate`, returns `false` unless `_current.Id == playbackId && ClaimTerminal()`, then tears down, clears `_current`, publishes `Stopped` | ✅ verified | Task 4's "capture the id at raise time" semantics depend on it. Fix `StopAsync`, not the handler. |
| R7 | `FailAsync(Playback, string, Exception)` (:533) claims terminal, tears down under `_gate`, clears `_current` under `ReferenceEquals`, publishes `Failed`, logs at Warning | ✅ verified | Task 6 calls it. Adapt. |
| R10 | `EventPlaybackRequest.Priority` defaults to 6, `Validate` accepts 1-10, `GvMediaOptions.PreemptAtPriority` defaults to 8 and is present in `src/Radio.API/appsettings.json` | ✅ verified on `main` at `6b3dcc2e` | — |
| R12 | `DuckingService`, `DuckingServiceTests` and `DuckingServiceCharacterizationTests` are **untouched** by PR 3 — they do not appear in `git diff --name-only main...HEAD` | ✅ verified — PR 3 honoured its §0.5 item 3 | If PR 3 ends up touching them, re-read Tasks 1-3 against what it left. |

⚠ **One delta already found, and Task 5 is written against the real code rather than the plan's.** PR 3
does **not** end the acquisition tail with `Publish(SnapshotOf(playback, Playing, null))` as its own
plan showed. It ships `PublishNonTerminal(playback, EventPlaybackState.Playing)` (:663), which
re-checks `playback.IsTerminal` under `_stateLock` and returns without publishing when the transition
has already been claimed — because *"a source can fail synchronously inside `PlayAsync`"*. **Task 5
preserves that call.** Replacing it with a bare `Publish` would reintroduce the bug its remark
describes, and it is exactly the kind of thing a plan written from plan text gets wrong.

**Still NOT verified — check these before writing Task 7:**

| # | Assertion | Why it is open | If it is not true |
|---|---|---|---|
| R8 | `tests/…/Audio/Events/EventPlaybackServiceTests.cs` contains private nested `FakeEventSource`, `FakeDuckingService`, `FakeTtsFactory`, plus `CreateService(...)`, `SpeechRequest()`, `NextSnapshotWith(...)`, `WaitUntilAsync(...)` | The file exists in the working tree but is **untracked** — Builder is still writing it, so its final shape is unknown | Task 7 extends it in place. If a fake is missing, write the minimum Task 7 needs — never a second, divergent fake. |
| R9 | `CreateService` takes its collaborators by **name** | Same reason as R8 | Task 7 must inject a `FakeDuckingService` and a `GvMediaOptions`. If the fixture has no such parameter, **add one**; do not hand-construct a second service. |
| R11 | `AcquireSpeechAsync` fills all four `TTSParameters` fields | `PHN-1c` C-25 predates `TTS-9`, which made `TTSParameters.Engine` nullable and deleted `TTSEngine.ESpeak` | **PR 4 does not touch that method.** Listed only so a reviewer is not surprised that this plan quotes code near it. |

**Verified against the tree at `6b3dcc2e` and therefore NOT on this list** — these need no re-check, only
a grep for drift: `DuckingService.StartDuckingAsync` raises only on the transition;
`DuckingService.DefaultEventPriority == 8`; `StopDuckingAsync` removes the `_sourcePriorities` entry;
`AudioManager.OnDuckingStateChanged` is the only subscriber in `src/`; `GetActiveEventsByPriority` and
`StopAllDuckingAsync` have no non-test callers; `AnnouncementService` is the only caller of
`StartDuckingAsync`; `PhoneIntegration:Enabled` is `false`; `GvMedia:PreemptAtPriority: 8` is present in
`src/Radio.API/appsettings.json`; `DuckingServiceCharacterizationTests` has four tests, all passing.

### 0.4 ⚠ Twelve contradictions found while planning, and how each resolves

**C-34, C-35, C-38 and C-41 change what PR 4 builds or ships. C-46 is an owner decision that REMOVES
work from it** — it supersedes C-42's resolution, and C-42 is kept as the record of a rejected option.

---

**C-34 — ⚠ CHANGES WHAT SHIPS. The line every handoff points at does not exist, and PR 4 does not make
its claim true as written.**

`PHN-1a` §5, `PHN-1b` §5 and `PHN-1c` §5 all say the same thing: *"`design/INTEGRATIONS.md:566`'s
correction must be updated in the same PR."* **There is no such claim at `:566`.** At `6b3dcc2e` line 566
is inside the RotaryPhone hub connection runbook (`journalctl -u radio-api … | grep -i phone`). This is
C-19's rule arriving on its own doorstep: three documents copied a line number forward and none of them
re-checked it.

⚠ **AMENDED 2026-09-04 — do not replace the number, DROP it. Cite this claim by content.** This paragraph
originally ended *"the correction is at **line 932**"*, and that was true when it was written, at
`6b3dcc2e`. **It is no longer true, and it stopped being true within hours.** `PHN-1c` (#556) inserted
above it; the `PHN-1c` Builder cycle noticed the move and recorded the new position in the queue banner
as **`:980`** — **which is also wrong.** `:980` is inside a Python request example. Measured at
`b77ffe12`, the claim is at **`:997`**, and it will move again the next time anything is inserted above
it.

**So the citation is now by content, in this plan and in the `PHN-1d` queue row.** The claim is the
struck bullet under **§ *How Audio Ducking Works***, beginning:

> ~~Higher priority announcements can interrupt lower priority ones~~ — **this is not true today.**

**Grep that sentence. Do not trust a number, including the one in the sentence you are reading.** Four
documents have now carried a line number for this claim and **three of them were wrong** — `:566` twice
over (it never held it), and `:980` on the same day it was written. That is not three typos in a row; it
is a citation format that cannot survive an insertion, applied to a file this arc keeps inserting into.
Task 9 and Task 10's file list already address it by symbol and by `grep` string rather than by offset,
which is the pattern to copy.

The second half is the one that matters. The struck-through claim reads:

> ~~Higher priority announcements can interrupt lower priority ones~~ — **this is not true today.**

**PR 4 does not make that sentence true.** It makes exactly one narrower thing true: *an event source
starting at priority ≥ 8 stops attended GV playback.* **Announcement-versus-announcement still mixes**,
because ADR §6.2 rule 3 explicitly declines to fix it — *"Sub-8 events keep mixing… fixing it means
introducing a queue across every caller of `IAnnouncementService`, and that is separate work."* An
announcement at 9 still does not interrupt one at 3.

So restoring the original sentence would replace a true statement with a false one — the same failure
class the strike-through was written to correct, in the other direction. **Resolution: Task 9 rewrites
the item to say what is now true and what is still not**, by symbol and behaviour rather than by line
number, and both remaining doc citations of `:566` in this plan's predecessors are left alone (they are
history; correcting three merged plans is not this PR's job — Task 9 records the drift once, here).

---

**C-35 — ⚠ CHANGES THE WORK. ADR §6.3 says "on every `StartDuckingAsync`". PR 4 raises on every call
that ADDS a source, and the difference is deliberate.**

ADR §6.3: *"`StartDuckingAsync` must raise `DuckingStateChanged` on every call, not only on
transition."* Read literally that includes a repeat call for a source **already** in `_activeEvents` —
a call in which nothing happened at all: the set did not change, the duck level did not change, and no
source started.

Two reasons not to do that. First, semantics: the rule the event exists to drive is *"a source of
priority ≥ 8 **starts**"*, and a repeat call is not a start. Second, the box: trap 5 of the breakdown
disqualifies designs that add avoidable churn on an N100, and an event raised for a non-event is exactly
that — it fans out to `AudioManager`, which writes an **Information** line for every one.

**Resolution: raise when `needsTransition || wasNewlyAdded`** (Task 1). In today's tree
`needsTransition ⟹ wasNewlyAdded` in every reachable state — `_activeEvents` is non-empty only while
`_isDucking` is true, and `StopAllDuckingAsync` clears both together — so the disjunction is defensive
rather than load-bearing, and it is written that way so that a future state where they diverge still
announces the transition.

This satisfies both existing tripwires as their authors intended: the characterization test uses **two
different sources** and goes 0 → 1 (Task 3), and `DuplicateStartDucking_DoesNotAddDuplicateEvents` uses
the **same** source twice and is unaffected. Task 3 adds the test that pins the distinction so it cannot
silently become "every call" later.

---

**C-36 — ⚠ `GetPriority` answers 8 for any event source it has never been told about, and
`StopDuckingAsync` deletes what it was told. A priority resolved one moment too late reads 8.**

`DuckingService.GetPriority` falls back to the category default:

```csharp
return source.Category == AudioSourceCategory.Event
  ? DefaultEventPriority      // 8
  : DefaultPrimaryPriority;   // 3
```

and `StopDuckingAsync` removes the override **before** it does anything else:

```csharp
_activeEvents.Remove(eventSource.Id);
_sourcePriorities.Remove(eventSource.Id);   // added to stop the map growing per announcement
```

So the value `GetPriority` returns for a given source **changes over that source's life**: 3 while it is
ducking, 8 the instant it stops. A handler that captured `e.TriggeringSource` and resolved its priority
later — on a dispatched task, say — would read 8 for a source whose caller had explicitly claimed 3, and
would preempt on a **stop**.

**Resolution — two rules in `OnDuckingStateChanged`, both in Task 4:**

1. **Ignore every raise with `IsDucking == false`.** In today's `DuckingService` that raise happens only
   from `StopDuckingAsync` (when the set empties) and from `StopAllDuckingAsync` (with a **null**
   `TriggeringSource`), and neither is a source starting.
2. **Read `GetPriority` synchronously, on the raising thread, before dispatching anything.** The entry is
   guaranteed present at that instant: every caller in the tree does `SetPriority(source, p)` immediately
   before `StartDuckingAsync(source)`.

⚠ **Be exact about how much rule 1 is currently carrying, because the tempting summary overstates it.**
Traced against today's `DuckingService`, the `IsDucking:false` raise reaches the handler in exactly two
shapes, and each is *also* caught by a different guard: the set-empties raise carries the attended
source itself, which the identity check (C-40) rejects, and `StopAllDuckingAsync`'s carries **null**,
which the pattern-match rejects. So rule 1 is not today the only thing standing between this system and
a spurious preemption — **it is the only one of the three that is *designed* to be.** The identity check
exists for self-preemption and the null check for an NRE; relying on either to cover a stop would mean
the handler becomes wrong the day `StopDuckingAsync` raises on a non-final stop, or the day two attended
sources can coexist. Rule 1 is stated first and tested directly for that reason.

Task 7's `FakeDuckingService` models the priority removal and exposes the two `IsDucking:false` shapes
as explicit raise helpers, so a late resolution or a missing filter fails a test rather than only a
review.

---

**C-37 — the ADR says the existing subscriber "acts only on `!e.IsDucking`". It also logs on the true
branch, at Information, and after this PR it will do so more often.**

ADR §6.3's safety argument is *"the only existing subscriber is `AudioManager.OnDuckingStateChanged`,
which acts **only** on `!e.IsDucking`… Additional events carrying `IsDucking: true` are a no-op for it."*
Read against the code, the first half is right about **state** and wrong about **behaviour**:

```csharp
if (e.IsDucking)
{
  _logger.LogInformation(
    "Ducking started: source={TriggerSource}, duckLevel={DuckLevel:F0}%, activeEvents={EventCount}", …);
}
else { /* ClearDuckingMultiplier + LogInformation */ }
```

It is **log-only**, not a no-op. Mutating nothing is what makes the change safe; saying "no-op" when a
line is written is the overclaim class `CLAUDE.md` § Pre-Merge Review exists for, and this plan is not
going to repeat it.

**The measurable consequence:** one extra Information line per concurrent announcement — not per tick,
per *source*, and only while something is already ducking. Since `LOG-11` those go to the file sink, not
the journal. **Resolution: nothing to build.** It is recorded so a reviewer who checks the ADR's claim
against the code finds this note instead of a discrepancy, and so a future reader does not "fix"
`AudioManager` to be silent.

---

**C-38 — ⚠ CHANGES THE WORK. The raise is unguarded, and PR 4 adds the first subscriber that can throw.
The cost is not the one it looks like.**

The obvious fear is stuck ducking — the radio pinned at 20% forever. **That is not what happens, and it
is worth being exact rather than guessing**, because the guess would justify the wrong fix.

Traced: an exception from a `DuckingStateChanged` subscriber propagates out of
`RaiseDuckingStateChanged` → out of `StartDuckingAsync` → into its caller. Every caller in the tree has
a cleanup path that runs anyway:

- `AnnouncementService.AnnounceAsync` — `catch (Exception ex) { LogError }` then
  `finally { CleanupSourceAsync(ttsSource) }`, which calls `StopDuckingAsync`. Ducking is restored.
- `AnnouncementService.PlaySoundWithAnnouncementAsync` — the same shape for both phases.
- `EventPlaybackService.AcquireAndPlayAsync` — the generic catch → `FailAsync` → `TearDownAsync` →
  `StopDuckingAsync`.

So the real cost is: **the announcement is silently swallowed, and
`POST /api/notifications/announce` still answers `200`** — the exact property `PHN-1c` §0.6 documented
about that endpoint. A fault in the *attended* seam would silence the *unattended* one, invisibly, on a
box whose owner would experience it as "the doorbell stopped working."

**Resolution (Task 2): wrap the `DuckingStateChanged` invocation in `try`/`catch` and log a warning.**
Narrow, deliberately: `RaiseDuckingLevelChanged` is left alone — it gains no new subscriber in this PR,
it fires per fade step, and widening the change would put a `try` in a loop for no reason that exists
yet.

---

**C-39 — the raise happens from inside `TearDownAsync`, which `EventPlaybackService` calls while holding
`_gate`. A handler that awaited `_gate` would deadlock a non-reentrant semaphore.**

`StartAsync` tears down a replaced playback under `_gate`; `OnSourceCompleted` and `FailAsync` tear down
under `_gate`; `TearDownAsync` calls `StopDuckingAsync`, which raises. `PHN-1c`'s `OnSourceCompleted`
already carries this hazard and already solved it — its remark says so in as many words, and it
dispatches through `Task.Run` rather than waiting.

Today's `IsDucking == false` filter (C-36) means the teardown raise is discarded before any of that
matters. **That is a second line of defence, not the design**: relying on it would mean the handler
becomes a deadlock the day someone makes `StopDuckingAsync` raise on a non-final stop.

**Resolution: `OnDuckingStateChanged` never touches `_gate` on the raising thread** (Task 4). It decides
under `_stateLock` — which is never held across an `await` — and dispatches `StopAsync` on `Task.Run`,
exactly as `OnSourceCompleted` does, with the same reason written in the same place. Task 7 pins that
the raise returns before the stop completes.

---

**C-40 — `EventPlaybackService` ducks its own source, and `EventPlaybackRequest.Priority` accepts 8, 9
and 10. Without an identity check, an attended playback stops itself the instant it starts.**

`AcquireAndPlayAsync` calls `SetPriority(source, request.Priority)` then `StartDuckingAsync(source)`.
After Task 1 that call raises `DuckingStateChanged` with `TriggeringSource` = **our own source**. The
shipped default is `Priority = 6`, which is below the threshold — but `Validate` accepts anything in
1-10, and the route takes `Priority` from the wire.

So a caller that posts `{"priority": 8}` would, without a guard, have its own start raise a preemption
against itself. The visible result is a voicemail that reaches `Playing` and immediately reports
`Stopped`, with a warning naming a "preempting" source that is itself.

**Resolution: skip the raise when `ReferenceEquals(_current.Source, e.TriggeringSource)`** (Task 4).
Compared by **reference on the instance this service holds**, not by id: `PHN-1c` §0.7 establishes that
three id spaces meet in this file and only the instance is unambiguous. Task 7 pins it with a request at
`Priority = 8`.

Not resolved by rejecting `Priority >= PreemptAtPriority` in `Validate`: that couples a Core validator
to an Infrastructure config key, and the identity check is required regardless.

---

**C-41 — ⚠ CHANGES THE WORK, AND IT IS THE WORST OUTCOME AVAILABLE IN THIS PR. A teardown that lands
between `StartDuckingAsync` and `PlayAsync` produces audio that nothing can stop.**

`PHN-1c`'s handoff addresses one half of the start/stop race and says so: *"`StopAsync(playbackId)` … is
idempotent through `ClaimTerminal`, so a preemption racing a natural end cannot double-fire."* True, and
it is about **double-firing a snapshot**. The other half is about **the audio**.

`AcquireAndPlayAsync`'s tail runs outside `_gate`:

```csharp
token.ThrowIfCancellationRequested();
playback.Source = source;
source.PlaybackCompleted += …;
_duckingService.SetPriority(source, request.Priority);
await _duckingService.StartDuckingAsync(source, token);
await source.PlayAsync(token);              // ← a full teardown can complete before this line
Publish(SnapshotOf(playback, EventPlaybackState.Playing, …));
```

A preemption arriving in that window claims the terminal flag, runs `TearDownAsync` (stop ducking, stop
source, dispose source) and publishes `Stopped`. Then `PlayAsync` runs. Depending on how the source
reacts to being played after `StopAsync` and `DisposeAsync`, the outcomes range from a logged
`ObjectDisposedException` to **audio starting on a source the seam has already forgotten** — with no
`playbackId` that resolves to it, so no route, no chip and no later preemption can stop it. It plays to
the end, over the announcement that preempted it.

There is a second, quieter leak on the same path: `token.ThrowIfCancellationRequested()` fires *after*
acquisition has produced a source, and **nothing disposes it**. For the `RemoteMedia` arm that is an open
`FileStream` over a cached recording, which on Windows also blocks `GvMediaCache` from ever evicting the
file. PR 3 has this today; PR 4 is what makes cancellation common enough to matter.

**Resolution (Task 5) — serialise, do not sequence.** The tail from the cancellation check to
`Publish(Playing)` runs **under `_gate`**. `TearDownAsync` is only ever called under `_gate` (R4), so
"tear this playback down" and "start its audio" become mutually exclusive rather than merely ordered. On
the cancelled branch the acquired source is disposed. The gate is acquired with
`CancellationToken.None`, matching `OnSourceCompleted`: the acquisition itself must not be abandoned
half-way.

⚠ **A reviewer will ask whether holding `_gate` across `PlayAsync` blocks `StopAsync`.** It does, for the
duration of `PlayAsync` — which starts playback and returns; it does not await completion (that is the
one thing `PHN-1c` §0.6 says not to copy from `AnnouncementService`). A stop arriving in that window is
delayed by one `PlayAsync`, then serviced. That is strictly better than the alternative it replaces.

---

**C-42 — ⚠ CHANGES THE WORK. D5 rule 2 is written in the "a source starts" direction, and read that way
it leaves the mirror case mixing. PR 4 reads it as a state.**

ADR §6.2 rule 2: *"Priority ≥ `GvMedia:PreemptAtPriority` (default 8) → preempt. A ring (9) or caller
announcement (8) **stops** attended playback outright."* Implemented literally, as an edge, it covers
*announcement starts during voicemail* and does nothing for *voicemail starts during announcement* —
where the room gets exactly what rule 2 exists to prevent: two voices, neither intelligible, and the
user replays anyway.

The ADR's own justification for rule 2 is symmetric and says nothing about who moved first: *"For speech
over speech, stopping is strictly better than mixing… an announcement that talks across a voicemail
leaves both unintelligible."*

**Resolution: the symmetry is CONFIRMED and the remedy is a QUEUE, not a refusal — and it is PR 5's, not
this PR's. See C-46, which supersedes the rest of this item.**

⚠ **This entry is kept rather than rewritten, because the option it argued for was put to the owner and
overruled.** Its first draft resolved C-42 by refusing the start — *"the playback goes terminal as
`Failed` with `FailureReason = "PreemptedByPriority"`"*. The owner's decision (C-46) accepts the
symmetry argument above in full and rejects that remedy: a playback that starts under a live ≥ 8 source
**waits for it and then plays**. Deleting this paragraph would hide that a rejected option was
considered on the merits, and a record that shows only the option that won is not a record.

**What survives from the original three arguments, and what does not:**

- ~~It is the backstop that makes C-39's dispatch safe.~~ **Withdrawn as stated, and the correction
  matters.** Re-read against Task 5: the dispatched stop lands on a stale id and no-ops, and the new
  playback then *mixes* with the ≥ 8 source. So that residual race has no failure mode of its own —
  **it manifests as exactly the C-42 case**, and whatever the reverse-direction rule is, is its fix.
  C-41's orphaned source is closed by Task 5's `_gate` serialisation alone; it never needed this check.
- **`Failed` is contract-correct — for a refusal.** Still true, and it is why the queue's *staleness*
  expiry (C-46) reuses `Failed` rather than inventing a state for it.
- ~~It gives `GetActiveEventsByPriority` its first non-test caller.~~ **Not in PR 4.** It stays dead one
  PR longer; PR 5's queue is what wakes it.

---

**C-43 — `PreemptAtPriority` has a ceiling as well as a floor, and only the floor is documented. Above
8 it silently disables preemption for most sources.**

ADR §6.1 records the argument for lowering the threshold to 7 and rejects it, which reads as though the
knob is symmetric. It is not, and the asymmetry is invisible from the config file.

`GetPriority` returns `DefaultEventPriority` — **8** — for every event source whose caller never called
`SetPriority`. In this tree that is every event source except the ones `AnnouncementService` creates. So:

- **Threshold ≤ 8:** every unclaimed source preempts. This is the ADR's stated intent — *"anything that
  did not explicitly claim a rank still outranks a user listening to a recording."*
- **Threshold = 9 or 10:** unclaimed sources read 8 and stop preempting. Preemption still *works*, for
  the one dormant caller that explicitly sets 9, so nothing looks broken — it just stops happening for
  the live one. A knob turned two clicks silently deletes the feature.

**Resolution:** Task 8 pins `DuckingService.DefaultEventPriority == 8` and
`new GvMediaOptions().PreemptAtPriority <= DuckingService.DefaultEventPriority` in one test whose failure
message explains the coupling, and Task 9 states it in `GvMediaOptions.PreemptAtPriority`'s own remark
and in `INTEGRATIONS.md`'s config table. A test is the right instrument because the coupling is between
two **compile-time defaults**; an operator override in `appsettings.Production.json` is beyond what any
test can see, which is exactly what the doc line is for.

---

**C-44 — determinism. PR 4 adds no timer, so `TimeProvider` is the wrong seam; the thing a test must
synchronise on is the dispatch.**

`CLAUDE.md` § Test Timing: *"if an assertion depends on the component under test having observed
something, synchronize on the observation, not on elapsed time,"* and the house idiom for that is an
injectable `TimeProvider`. **That idiom does not apply here** — nothing in this PR waits, polls or
schedules. What is asynchronous is `Task.Run`, and the observation a test needs is *"the handler has
finished deciding and, if it decided to stop, has finished stopping."*

`PHN-1c`'s `NextSnapshotWith` covers the **positive** assertions: a preemption publishes `Stopped`, so
the test awaits that snapshot. It cannot cover the **negative** one — *"a source at priority 5 changed
nothing"* — where there is no event to await and the only options are a sleep (forbidden) or a bounded
poll that starvation can only make weaker.

**Resolution (Task 4): an internal `PreemptionTail` property** — the `Task` from the most recent
dispatch, or `Task.CompletedTask` when the most recent decision was "do nothing". `Radio.Infrastructure`
already declares `InternalsVisibleTo("Radio.Infrastructure.Tests")`, so this costs no public API. The
decision itself is made synchronously on the raising thread before the raise returns, so a test that
raises and then awaits `PreemptionTail` has a genuine rendezvous in **both** directions. This is the
"the plan adds a seam rather than widening a timeout" case, and it is a seam of the second kind the
brief names: an observable, not a clock.

---

**C-45 — there are TWO tripwires, not one. Every handoff names only the characterization file.**

`PHN-1a`, `PHN-1b` and `PHN-1c` all hand PR 4 the same instruction: update
`DuckingServiceCharacterizationTests`, do not delete it. Correct, and incomplete.
`DuckingServiceTests` — the ordinary unit-test file, sharing the same `DuckingServiceFixture` —
contains:

```csharp
// tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs
public async Task StartDuckingAsync_MultipleEvents_DoesNotDuckAgain()
{
  …
  await service.StartDuckingAsync(eventSource1.Object);
  await service.StartDuckingAsync(eventSource2.Object);

  // Only one state change event (the first one)
  Assert.Equal(1, stateChangeCount);
```

which asserts exactly the property Task 1 changes, in a file nobody was told to look at. A Builder who
updated only the characterization file would hit a red suite and be tempted to treat this as collateral.

**Resolution: Task 3 updates both, and renames this one** — the method name stays true (it still does
not duck again; the *level* does not move) but the assertion no longer matches it, so it becomes
`StartDuckingAsync_MultipleEvents_DoesNotChangeTheDuckLevel_ButAnnouncesEachSource`. Verified at
`6b3dcc2e`: those two files are the **only** places in `tests/` that reference `DuckingStateChanged` or
`StartDuckingAsync`, so after Task 3 no third surprise exists.

---

**C-46 — ⚠ OWNER DECISION, 2026-09-04. The reverse direction QUEUES rather than fails, and the queue is
PR 5's. PR 4 ships the stop direction only, and pins today's behaviour so PR 5's change is a visible
diff.**

C-42 established that D5 rule 2 is symmetric and that the mirror case — the user presses play while a
≥ 8 source is already talking — must not mix. It resolved that by refusing the start. **Three options
were put to the owner and he chose the third:**

| Option | Behaviour | Verdict |
|---|---|---|
| Mix | Two voices, as today | Rejected — it is what D5 exists to prevent |
| Refuse | `Failed` + `PreemptedByPriority` | **Rejected** — *"press play, get an error, nothing happens"* is the punch list's tier (b) embarrassing shape |
| **Queue** | **Wait for the blocking source to finish, then play** | **CHOSEN** — the only option where nothing is lost and nothing overlaps |

**The stop direction is unchanged**: a starting ≥ 8 source still stops an in-flight attended playback.
That is what the ADR states and it is Task 4.

**Resolution — the queue lands in PR 5, and PR 4 ships neither remedy. The argument is the owner's own
reasoning, turned on the queue:**

He rejected refusing because *"press play and nothing happens"* is embarrassing. **A queue whose waiting
state nobody can see is the same sentence, for longer.** Visibility requires a broadcast and a chip, and
those are **PR 5** (`/hubs/audio`, server-owned state) and **PR 6** (the topbar chip). Shipping the queue
in PR 4 would deliver the half of the decision the owner disliked and defer the half that redeems it. So
the queue goes where its visibility goes.

**Nothing reaches a user in between.** `GvMedia:Enabled` ships `false` and is not flipped until PR 6, so
between PR 4 and PR 5 the mirror case is unreachable in production — and what it falls back to is
today's mixing, which is not a regression but an un-fixed pre-existing wart (ADR §6.2 rule 3's
neighbourhood).

**What PR 4 owes instead: a characterization test.** Task 6 is repurposed from *refuse* to *pin what
happens today*, in exactly the discipline `PHN-1a` used on this PR — so PR 5's queue arrives as an edited
assertion in someone's diff rather than as a silent behavioural shift. **`ThrowIfAlreadyPreempted`,
`PreemptedAtStartException` and `FailureReason = "PreemptedByPriority"` are NOT built.**

**⚠ What PR 4 must NOT do, because it looks helpful:** add `EventPlaybackState.Waiting` "ready for PR 5".
An enum member on the wire that no code can ever produce is a lie the size of a state, and PR 5 needs it
appended at the END of the enum anyway (the `MediaIdHasIllegalCharacter` precedent). PR 5 adds it when
PR 5 can reach it.

**The five questions the queue has to answer are recorded in §5 for PR 5's planner**, with a lean on each
and the reason — including the one that is genuinely awkward: **the wake-up trigger is the
`IsDucking: false` raise that Task 4's handler deliberately ignores** (C-36), and `StopDuckingAsync`
raises it only when the ducking set **empties**, so a ≥ 8 source ending while a sub-8 source continues
produces no wake at all.

### 0.5 What this row is NOT

1. ⛔ **No `Radio.Web` change of any kind.** PR 6.
2. ⛔ **No `/hubs/audio` broadcast, no `AudioStateHub` change, no `CircuitHandler`, no max-duration
   cap.** All PR 5. `PlaybackChanged` still has no subscriber after this PR, and that is correct.
3. ⛔ **No new route, no route change, no DTO change.** PR 3 shipped the route family; PR 4 changes what
   the seam does, not what it exposes.
4. ⛔ **No config key.** `GvMedia:PreemptAtPriority` already exists in `GvMediaOptions.cs` **and** in
   `src/Radio.API/appsettings.json` (verified at `6b3dcc2e`, line 276). `git diff` must show no change
   to any `appsettings*.json` — Task 10 asserts it.
5. ⛔ **No queue in `IEventPlaybackService`, and no `EventPlaybackState.Waiting`.** Two separate
   queues are in play and neither is PR 4's. (a) *Sub-8 events keep mixing* — ADR §6.2 rule 3, *"the fix
   is a queue in `IEventPlaybackService`, not a priority tweak"*, explicitly out of this arc. (b) *A
   playback started under a live ≥ 8 source waits for it* — the owner's decision of 2026-09-04
   (§0.4 **C-46**), which is **PR 5's**, because it needs a waiting state on the wire and a chip that
   renders it. ⚠ Do not add the enum member "ready for PR 5": a state no code can produce is a lie on
   the wire. Task 6 pins today's behaviour instead.
6. ⛔ **No change to `AnnouncementService` or any of its callers.** ADR §6.3's whole argument for this
   design is *"with zero changes to `AnnouncementService` or any of its callers."* If a task appears to
   need one, the design is wrong, not the constraint.
7. ⛔ **No change to `DuckingService`'s duck LEVEL behaviour.** Ducking stays binary and pinned to the
   global `Audio:DuckingPercentage`. Priority still does not weight the fade, and
   `SetPriority_DoesNotChangeTheDuckLevel_TODAY` must still pass unmodified — see Task 3.
8. ⛔ **No fix to `SourcesController`'s two event paths.** They still do not duck and still leak; ADR
   §14 Q6 keeps that out of this arc.
9. ⛔ **No `TTSEventSource.Position` override** (`PHN-1c` C-27). Handed to PR 5 with the exact change.

### 0.6 What "priority ≥ 8" actually means in the shipped scale — and whether 8 is right

The brief for this row asks the question directly, so here is the answer with the evidence, from the
tree at `6b3dcc2e`.

| Value | Where it lives | Live? |
|---|---|---|
| `DefaultEventPriority = 8` | `DuckingService.cs` — what `GetPriority` returns for **any** event source with no override | **live, structural** |
| `?? 8` | `NotificationsController.Announce` — `Math.Clamp(request.Priority ?? 8, 1, 10)` | **live** — the external-event endpoint |
| `RingPriority: 9`, `AnnouncementPriority: 8` | `appsettings.json` `PhoneIntegration` | **dormant** — `Enabled: false`, and never true |
| `priority = 5` | `IAnnouncementService.AnnounceAsync`'s default parameter | unreached — both callers pass explicitly |
| `DefaultPrimaryPriority = 3` | `DuckingService.cs` | live, primary sources only |
| `Priority = 6` | `EventPlaybackRequest`'s initializer — attended playback | live from PR 3 |

**So "≥ 8" is not really a rank comparison.** Because the fallback *is* 8, the test it performs is:
*"did this source explicitly claim a rank below 8?"* Only `AnnouncementService`'s two callers ever call
`SetPriority`, so in practice the rule reads: **an announcement posted with `Priority` 1-7 mixes;
everything else — including everything that named no priority at all — stops attended playback.**

**Verdict: 8 is anchored, not guessed — but the ADR's anchor is one-sided.** §6.1 argues 8 upward, from
two live code facts landing there, and records 7 as considered-and-rejected. What it does not say is
that 8 is also the **ceiling** (C-43): the knob is safe to turn down and is a trap turned up. State both
halves wherever the key is documented.

**The live consequence, which is a deliberate acceptance and belongs in front of the reviewer:** with
`PhoneIntegration:Enabled` false, the only thing on this box that can preempt attended playback is an
external announcement posted to `/api/notifications/announce` at its default priority 8. **A doorbell or
a laundry notification will stop a voicemail mid-play.** Intended, per ADR §6.1.

### 0.7 The ten ways this PR could make the room sound wrong

The breakdown calls this the sharp one because its failure modes are audible rather than test-visible.
Each row names where it is closed. **A reviewer should walk this table against the diff.**

| # | Failure | Sound in the room | Closed by |
|---|---|---|---|
| 1 | Handler waits on `_gate` from the raising thread | Everything stops: ducking engaged, seam frozen, radio pinned at 20% | Task 4 — dispatch (C-39) |
| 2 | Handler throws | The doorbell goes silent and the API still answers `200` | Task 2 — guarded raise (C-38) |
| 3 | Priority resolved after `StopDuckingAsync` removed the entry, or a stop acted on at all | A source **ending** reads as priority 8 and stops the voicemail. ⚠ Not reachable through today's exact raise conditions — the identity and null checks happen to cover both shapes (C-36) — which is precisely why this guard needs its own test rather than a passing suite | Task 4 — `IsDucking` filter + synchronous read (C-36) |
| 4 | Teardown lands between `StartDuckingAsync` and `PlayAsync` | Audio starts that has no `playbackId` and **nothing can stop** — it plays over the announcement to the end | Task 5 — the tail runs under `_gate` (C-41) |
| 5 | No identity check | An attended playback posted at `Priority` 8 stops itself the instant it starts | Task 4 — `ReferenceEquals` (C-40) |
| 6 | Attended playback starts while a ≥ 8 source is sounding | Two voices, neither intelligible — the exact thing D5 exists to prevent | **NOT closed by PR 4.** Owner decision C-46 sends the fix (a queue) to PR 5; Task 6 pins the current behaviour. Unreachable by a user until PR 6 flips `GvMedia:Enabled` |
| 7 | Preemption addresses `_current` at dispatch time rather than the captured id | A replacement the user just started is stopped instead of the one that was playing | Task 4 — id captured at raise time |
| 8 | `TriggeringSource` null (`StopAllDuckingAsync`) | An NRE inside the raise, which is failure 2 | Task 4 — pattern-match null check |
| 9 | The dispatched task faults unobserved | A process-level hazard on the N100 | Task 4 — `try`/`catch` inside the dispatch |
| 10 | Threshold configured above 8 | Preemption silently stops happening; the voicemail mixes again | Task 8 + Task 9 (C-43) |

---

## 1. Tasks

Ten tasks. Tasks 1-3 are `DuckingService` and its two tripwires. Tasks 4-6 are the seam. Task 7 is the
seam's tests. Task 8 is the threshold coupling. Task 9 is docs. Task 10 is the gate.

⚠ **Every line number in this plan, in its three predecessors and in ADR-029 may have drifted (C-19 —
and C-34 is what happens when it does). Grep for the symbol; never `sed -n '<n>p'`.** Every code block
below is literal and complete unless it says otherwise.

⚠ **Do Task 0 first.** Work §0.3's table against merged `main` and write the answers into the PR
description. If R1 is false, stop and say so rather than improvising a host for Tasks 4-6.

---

### Task 1 — `StartDuckingAsync` announces every source that joins the ducking set

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Services/DuckingService.cs` — the whole body of
  `StartDuckingAsync` (grep for `public async Task StartDuckingAsync`)
- Test: covered by Task 3 (the two tripwires must move in the same commit as the behaviour)

**Interfaces:**
- Consumes: nothing.
- Produces: `DuckingStateChanged` is raised with `IsDucking = true` and
  `TriggeringSource = eventSource` once per source that joins `_activeEvents`. Task 4 subscribes.

- [ ] **Step 1: Replace the method body.**

Replace the entire existing `StartDuckingAsync` with this. The only behavioural change is the raise
condition; the fade, the logging levels and the lock discipline are unchanged.

```csharp
  /// <inheritdoc />
  /// <remarks>
  /// ⚠ DuckingStateChanged is raised for EVERY source that joins the ducking set, not only for the one
  /// that caused the fade. That is ADR-029 D5 §6.3, and it is what makes priority load-bearing:
  /// EventPlaybackService subscribes and stops attended playback when a source at or above
  /// GvMedia:PreemptAtPriority starts. Before this change a second concurrent event reached only a
  /// LogDebug, so nothing downstream could ever learn that it had started.
  ///
  /// The ADR's wording is "on every call"; this raises on every call that ADDS a source. A repeat call
  /// for a source already in the set is not a start — nothing joins, the level does not move — and
  /// raising for it would fan an event out to AudioManager, which writes an Information line per raise,
  /// on a box where avoidable churn is audible (PHN arc breakdown, trap 5).
  ///
  /// ⚠ Ordering is NOT the order of starts. The transition raise happens after ApplyFadeAsync, which
  /// awaits for Audio:DuckingAttackMs; a second source arriving inside that window is announced first.
  /// Each raise carries its own TriggeringSource, so a subscriber that reads that field rather than
  /// assuming sequence is unaffected. Do not "fix" this by moving the transition raise ahead of the
  /// fade: AudioManager's log line would then claim a duck level the fade has not reached.
  /// </remarks>
  public async Task StartDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(eventSource);
    ObjectDisposedException.ThrowIf(_disposed, this);

    var options = _audioOptions.CurrentValue;
    bool needsTransition;
    bool wasNewlyAdded;
    int activeCount;

    lock (_lock)
    {
      needsTransition = !_isDucking;

      wasNewlyAdded = !_activeEvents.ContainsKey(eventSource.Id);
      if (wasNewlyAdded)
      {
        _activeEvents[eventSource.Id] = eventSource;
      }

      activeCount = _activeEvents.Count;

      if (!_isDucking)
      {
        _isDucking = true;
      }
    }

    if (wasNewlyAdded)
    {
      _logger.LogDebug(
        "Added event source '{SourceId}' to ducking queue. Active events: {Count}",
        eventSource.Id, activeCount);
    }

    if (needsTransition)
    {
      var targetLevel = options.DuckingPercentage;
      var attackMs = options.DuckingAttackMs;

      _logger.LogInformation(
        "Starting ducking: target level {TargetLevel}%, attack time {AttackMs}ms, policy {Policy}",
        targetLevel, attackMs, options.DuckingPolicy);

      await ApplyFadeAsync(targetLevel, attackMs, options.DuckingPolicy, eventSource, cancellationToken);
    }

    // needsTransition implies wasNewlyAdded in every state reachable today — _activeEvents is non-empty
    // only while _isDucking is true, and StopAllDuckingAsync clears both together. The disjunction is
    // written out anyway so that a state where they diverge still announces the transition.
    if (needsTransition || wasNewlyAdded)
    {
      RaiseDuckingStateChanged(true, eventSource);
    }
    else
    {
      _logger.LogDebug(
        "Event source '{SourceId}' was already in the ducking queue; nothing started. Active events: {Count}",
        eventSource.Id, activeCount);
    }
  }
```

- [ ] **Step 2: Confirm the old local is gone.**

The previous body declared `bool wasAlreadyDucking;` and used it only in the `else if`. It is not in the
replacement. Run:

```bash
grep -n "wasAlreadyDucking" src/Radio.Infrastructure/Audio/Services/DuckingService.cs
```

Expected: no output.

- [ ] **Step 3: Build.**

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release
```

Expected: 0 warnings, 0 errors. (Release treats warnings as errors; an unused local would fail here.)

- [ ] **Step 4: Run the ducking tests and confirm exactly the two expected failures.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~Ducking"
```

Expected: **two** failures, and only these two —
`DuckingServiceCharacterizationTests.StartDuckingAsync_DoesNotRaise_ForASecondConcurrentEvent_TODAY`
(expected 0, actual 1) and
`DuckingServiceTests.StartDuckingAsync_MultipleEvents_DoesNotDuckAgain` (expected 1, actual 2).
A third failure means something else depends on this and §0.3's grep was wrong — stop and investigate.

- [ ] **Step 5: Do not commit yet.** Task 3 moves the tripwires; the behaviour and its tripwires belong
      in one commit so the diff shows both sides of the change.

---

### Task 2 — a throwing subscriber stops being able to silence an announcement

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Services/DuckingService.cs` —
  `RaiseDuckingStateChanged` (grep for `private void RaiseDuckingStateChanged`)
- Test: `tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs` (Task 3)

**Interfaces:**
- Consumes: nothing.
- Produces: `RaiseDuckingStateChanged` never throws.

- [ ] **Step 1: Write the failing test** in `DuckingServiceTests.cs`, at the end of the class.

```csharp
  [Fact]
  public async Task StartDuckingAsync_SurvivesASubscriberThatThrows()
  {
    // PR 4 adds the first DuckingStateChanged subscriber that does real work, so this is the first
    // moment a subscriber CAN throw. Unguarded, the exception propagates out of StartDuckingAsync into
    // AnnouncementService.AnnounceAsync, which catches it and cleans up — so ducking is restored and
    // nothing is stuck, but the announcement never plays AND POST /api/notifications/announce still
    // answers 200. A fault in the attended seam would silence the unattended one, invisibly.
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var reached = false;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, _) => throw new InvalidOperationException("subscriber is broken");
    service.DuckingStateChanged += (_, _) => reached = true;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.True(service.IsDucking);
    Assert.Equal(1, service.ActiveEventCount);
    // A later subscriber must still run: the guard is around the whole invocation list, so one broken
    // handler must not remove the others' notification. (Note this is what a single try around Invoke
    // does NOT give you for handlers registered AFTER the thrower — assert the reachable half.)
    Assert.False(reached, "documented: a throwing handler ends the invocation list; the guard stops the "
      + "exception escaping StartDuckingAsync, it does not resume the list");
  }
```

⚠ **Read that last assertion before you write the implementation.** A single `try` around
`Invoke` catches the exception but does **not** continue the invocation list — .NET stops at the first
handler that throws. The test asserts the honest behaviour rather than a stronger one, because
overclaiming in a test is the same failure class as overclaiming in a comment. PR 4 has exactly two
subscribers and does not need per-handler isolation; if a future PR does, that is a `GetInvocationList`
loop and its own decision.

- [ ] **Step 2: Run it and watch it fail.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~StartDuckingAsync_SurvivesASubscriberThatThrows"
```

Expected: FAIL — `InvalidOperationException: subscriber is broken` escapes `StartDuckingAsync`.

- [ ] **Step 3: Guard the raise.** Replace `RaiseDuckingStateChanged` entirely:

```csharp
  /// <summary>
  /// Raises the DuckingStateChanged event.
  /// </summary>
  /// <remarks>
  /// ⚠ Guarded because ADR-029 D5 makes this event load-bearing: EventPlaybackService subscribes to it
  /// to preempt attended playback, and it is the first subscriber that can throw. Unguarded, that
  /// exception propagates out of StartDuckingAsync into whichever event path called it. Traced against
  /// the tree: AnnounceAsync, PlaySoundWithAnnouncementAsync and EventPlaybackService.AcquireAndPlayAsync
  /// all catch and then restore ducking in a finally, so the cost is NOT stuck ducking — it is a
  /// silently swallowed announcement that POST /api/notifications/announce still reports as 200.
  ///
  /// This catches; it does not resume the invocation list. A handler that throws still prevents the
  /// handlers registered after it from running. That is accepted for two subscribers; anything more
  /// would want a GetInvocationList loop and a reason.
  ///
  /// RaiseDuckingLevelChanged is deliberately NOT given the same guard: it gains no new subscriber in
  /// this PR and it fires once per fade step, so a try inside that loop would buy nothing that exists.
  /// </remarks>
  private void RaiseDuckingStateChanged(bool isDucking, IEventAudioSource? triggeringSource)
  {
    var args = new DuckingStateChangedEventArgs
    {
      IsDucking = isDucking,
      TriggeringSource = triggeringSource,
      DuckLevel = CurrentDuckLevel,
      ActiveEventCount = ActiveEventCount
    };

    try
    {
      DuckingStateChanged?.Invoke(this, args);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "A DuckingStateChanged subscriber threw (isDucking={IsDucking}, source='{SourceId}'). "
        + "Ducking state is unaffected; the subscriber's work did not happen.",
        isDucking,
        triggeringSource?.Id ?? "<none>");
    }
  }
```

- [ ] **Step 4: Run it and watch it pass.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~StartDuckingAsync_SurvivesASubscriberThatThrows"
```

Expected: PASS.

---

### Task 3 — the two tripwires move, and the distinction they now encode gets pinned

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs`
- Modify: `tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs`

**Interfaces:**
- Consumes: Task 1's raise condition, Task 2's guard.
- Produces: nothing consumed later.

⚠ **Update these; never delete them.** Three predecessor plans say so. Deleting them makes the
behavioural change invisible again, which is the entire reason the characterization file was written.

- [ ] **Step 1: Rewrite the characterization file's class remark and its second test.**

Replace the class-level `<summary>` block with:

```csharp
/// <summary>
/// CHARACTERIZATION tests: these assert what DuckingService does, deliberately at a level of detail an
/// ordinary unit test would not bother with, so that a change to this shared audio service shows up as
/// an edited assertion in someone's diff rather than as a silent behavioural shift.
///
/// They were written by PHN-1a for ADR-029 D5 / PHN arc PR 4. ⚠ PR 4 HAS NOW LANDED and the second test
/// below is the one it changed: a second concurrent event used to raise nothing and now raises once.
/// Ducking itself is still binary and reference-counted — the duck LEVEL is still the fixed global
/// Audio:DuckingPercentage regardless of priority, which is what the third test still pins. What
/// changed is that the service now ANNOUNCES each source that joins the set, so a subscriber can
/// arbitrate on priority. It does not arbitrate on priority itself and this PR did not make it.
///
/// ⚠ If you are changing these again: update them, do not delete them.
/// </summary>
```

Replace the second test wholesale:

```csharp
  [Fact]
  public async Task StartDuckingAsync_RaisesOncePerSourceThatJoinsTheSet()
  {
    // ⚠ WAS StartDuckingAsync_DoesNotRaise_ForASecondConcurrentEvent_TODAY, asserting 0. ADR-029 §6.3
    // required this to become 1 and PR 4 is the change: the raise moved out of the if (needsTransition)
    // branch, so a second concurrent event is announced instead of reaching only a LogDebug. Without
    // this, EventPlaybackService could never learn that a priority-9 ring had started while a voicemail
    // was already ducking — which is the whole of D5.
    var service = CreateService();
    await service.StartDuckingAsync(CreateEventSource("event-1"));

    var raisedAfterFirst = 0;
    DuckingStateChangedEventArgs? last = null;
    service.DuckingStateChanged += (_, args) => { raisedAfterFirst++; last = args; };

    var second = CreateEventSource("event-2");
    await service.StartDuckingAsync(second);

    Assert.Equal(1, raisedAfterFirst);
    Assert.NotNull(last);
    Assert.True(last.IsDucking);
    // The identity of the STARTING source is the load-bearing field: EventPlaybackService reads its
    // priority from it, and reads it synchronously because StopDuckingAsync later deletes the entry.
    Assert.Same(second, last.TriggeringSource);
    Assert.Equal(2, last.ActiveEventCount);
  }
```

- [ ] **Step 2: Add the test that pins C-35's boundary**, immediately after it in the same file.

```csharp
  [Fact]
  public async Task StartDuckingAsync_DoesNotRaise_ForASourceAlreadyInTheSet()
  {
    // The boundary of PR 4's change, pinned so it cannot silently widen to "every call". ADR-029 §6.3
    // says "every StartDuckingAsync"; this service raises for every call that ADDS a source, because a
    // repeat call for an already-active source is not a start — nothing joins and the level does not
    // move — and every raise fans out to AudioManager, which writes an Information line for it.
    var service = CreateService();
    var source = CreateEventSource("event-1");
    await service.StartDuckingAsync(source);

    var raisedAfterFirst = 0;
    service.DuckingStateChanged += (_, _) => raisedAfterFirst++;

    await service.StartDuckingAsync(source);

    Assert.Equal(0, raisedAfterFirst);
    Assert.Equal(1, service.ActiveEventCount);
  }
```

- [ ] **Step 3: Leave the other two characterization tests untouched.**
      `StartDuckingAsync_RaisesDuckingStateChanged_OnTheFirstEvent` and
      `ActiveEventCount_IsReferenceCounted` are unaffected by Task 1.
      `SetPriority_DoesNotChangeTheDuckLevel_TODAY` **must still pass unmodified** — PR 4 does not make
      priority weight the fade, and if that test needs editing, something in Task 1 went further than it
      should have. Verify with:

```bash
git diff --stat tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs
grep -n "SetPriority_DoesNotChangeTheDuckLevel_TODAY" \
  tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs
```

- [ ] **Step 4: Fix the second tripwire in `DuckingServiceTests.cs`** (C-45). Replace the method
      wholesale — the name changes because the old one no longer describes what is asserted.

```csharp
  [Fact]
  public async Task StartDuckingAsync_MultipleEvents_DoesNotChangeTheDuckLevel_ButAnnouncesEachSource()
  {
    // ⚠ WAS StartDuckingAsync_MultipleEvents_DoesNotDuckAgain, asserting stateChangeCount == 1. This is
    // the SECOND tripwire for ADR-029 D5 and it lives outside DuckingServiceCharacterizationTests, which
    // is the only one the PHN-1a/1b/1c handoffs named. The name changed with the assertion: the service
    // still does not duck again — the level is unmoved — but it now announces the second source.
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");
    var stateChangeCount = 0;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, _) => stateChangeCount++;

    await service.StartDuckingAsync(eventSource1.Object);
    var levelAfterFirst = service.CurrentDuckLevel;
    await service.StartDuckingAsync(eventSource2.Object);

    Assert.Equal(2, stateChangeCount);
    Assert.Equal(2, service.ActiveEventCount);
    Assert.Equal(levelAfterFirst, service.CurrentDuckLevel);
  }
```

- [ ] **Step 5: Run the whole ducking suite green.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~Ducking"
```

Expected: PASS, all of them.

- [ ] **Step 6: Commit Tasks 1-3 together.**

```bash
git add src/Radio.Infrastructure/Audio/Services/DuckingService.cs \
        tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs \
        tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs
git commit -m "PHN-1d: DuckingService announces every source that joins the ducking set

ADR-029 D5 §6.3. The raise moves out of the if (needsTransition) branch so a
second concurrent event is announced rather than reaching only a LogDebug --
which is what lets a subscriber arbitrate on priority at all. Ducking itself is
unchanged: still binary, still reference-counted, still pinned to the global
Audio:DuckingPercentage.

Raises on every call that ADDS a source rather than literally every call: a
repeat call for an already-active source is not a start, and every raise costs an
Information line downstream on a box where churn is audible.

RaiseDuckingStateChanged is now guarded. PR 4 adds the first subscriber that can
throw, and an unguarded throw silently swallows an announcement that
POST /api/notifications/announce still reports as 200.

Both tripwires updated, neither deleted: the characterization test, and
DuckingServiceTests.StartDuckingAsync_MultipleEvents_DoesNotDuckAgain, which the
handoffs did not name."
```

---

### Task 4 — `EventPlaybackService` subscribes, and stops itself for a source at or above the threshold

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs`
- Test: Task 7

**Interfaces:**
- Consumes: Task 1's raise; `IDuckingService.GetPriority`; `GvMediaOptions.PreemptAtPriority`;
  `EventPlaybackService.StopAsync(string, CancellationToken)`; the private `Playback` class's `Id` and
  `Source`.
- Produces: `internal Task PreemptionTail { get; }` — consumed by Task 7 only.

- [ ] **Step 1: Add the field and its test seam**, next to the existing `_disposed` field.

```csharp
  private volatile Task _preemptionTail = Task.CompletedTask;

  /// <summary>
  /// Test seam: the tail of the most recent preemption decision, or a completed task when the most
  /// recent decision was "do nothing".
  /// </summary>
  /// <remarks>
  /// ⚠ This exists so tests can synchronise on the OBSERVATION rather than on elapsed time.
  /// OnDuckingStateChanged decides synchronously on the raising thread and then DISPATCHES the stop, so
  /// a test asserting straight after raising the event would be racing that dispatch. For the positive
  /// case PlaybackChanged is already a rendezvous; for the NEGATIVE case — "a priority-5 source changed
  /// nothing" — there is no event to wait for, and the only alternatives are a sleep (forbidden by
  /// CLAUDE.md § Test Timing, and the reason TEST-4 exists) or a poll that starvation can only weaken.
  ///
  /// PR 4 adds no timer, so the house TimeProvider/FakeTimeProvider idiom does not apply here: what is
  /// asynchronous is a Task.Run, not a clock.
  ///
  /// Last-writer-wins under no lock. Two concurrent preemptions would leave one tail unobserved, which
  /// costs a test its rendezvous and costs production nothing — the work is already dispatched and is
  /// idempotent through Playback.ClaimTerminal.
  /// </remarks>
  internal Task PreemptionTail => _preemptionTail;
```

- [ ] **Step 2: Subscribe in the constructor.** Append to the end of the existing constructor body:

```csharp
    // ADR-029 D5 §6.3. Subscribed here rather than lazily: both this service and DuckingService are
    // registered singleton (AddEventPlayback, AddSoundFlowAudio), so the subscription lives for the
    // process and Dispose is the only place it is removed.
    //
    // ⚠ This service is constructed lazily — on the first injection into EventPlaybackController — so
    // before anything has ever posted to /api/audio/events there is no subscription at all. That is
    // correct rather than a gap: with no attended playback there is nothing to preempt, and the
    // constructor necessarily runs before this instance's first StartAsync.
    _duckingService.DuckingStateChanged += OnDuckingStateChanged;
```

- [ ] **Step 3: Unsubscribe in `Dispose`.** Insert as the first statement after `_disposed = true;`:

```csharp
    _duckingService.DuckingStateChanged -= OnDuckingStateChanged;
```

- [ ] **Step 4: Add the handler**, in a new `// ── preemption ──` region immediately after
      `OnSourceCompleted`.

```csharp
  // ── preemption (ADR-029 D5) ─────────────────────────────────────────────

  /// <summary>
  /// ADR-029 D5 §6.2 rule 2: a source starting at or above GvMedia:PreemptAtPriority stops attended
  /// playback outright.
  /// </summary>
  /// <remarks>
  /// It STOPS rather than pausing. Resuming a voicemail mid-word twenty seconds after a phone call is
  /// worse than restarting it, and the recording is replayable at zero cost — it is a local cached
  /// file. The UI returns to an idle, replayable state (ADR-029 §12 item 4).
  ///
  /// ⚠ Three things in this method are load-bearing and none of them is obvious:
  ///
  /// (1) IsDucking:false is ignored. DuckingService.StopDuckingAsync removes the source's
  ///     _sourcePriorities entry BEFORE it raises, and GetPriority then falls back to
  ///     DefaultEventPriority (8) — so acting on a stop would read every ending announcement as a
  ///     priority-8 preemption. StopAllDuckingAsync also raises with a NULL TriggeringSource.
  ///
  /// (2) The priority is read SYNCHRONOUSLY, here, on the raising thread. Every caller in the tree does
  ///     SetPriority(source, p) immediately before StartDuckingAsync(source), so the entry is present at
  ///     this instant and gone after the source stops. Resolving it on the dispatched task would race
  ///     that removal and read 8 for a source whose caller had explicitly claimed 3.
  ///
  /// (3) The stop is DISPATCHED, never awaited here. This runs on the thread inside
  ///     DuckingService.StartDuckingAsync — on the live path that is AnnouncementService's, mid
  ///     announcement, reached from POST /api/notifications/announce — and StopAsync takes _gate, which
  ///     this service is already holding whenever the raise came out of TearDownAsync. Waiting would
  ///     deadlock a non-reentrant semaphore. Dispatching also keeps the doorbell from blocking for the
  ///     length of our teardown, which includes DuckingService's release fade.
  /// </remarks>
  private void OnDuckingStateChanged(object? sender, DuckingStateChangedEventArgs e)
  {
    if (!e.IsDucking || e.TriggeringSource is not { } trigger)
    {
      return;
    }

    int priority;
    try
    {
      priority = _duckingService.GetPriority(trigger);
    }
    catch (Exception ex)
    {
      // Reading a priority must never take down the announcement path that raised this.
      _logger.LogWarning(ex, "Could not read the priority of starting source '{SourceId}'", trigger.Id);
      return;
    }

    var threshold = _gvMediaOptions.CurrentValue.PreemptAtPriority;
    if (priority < threshold)
    {
      // ADR-029 §6.2 rule 3: sub-threshold events keep MIXING, exactly as they do today over TTS
      // announcements. This ADR does not fix that; the fix would be a queue across every caller of
      // IAnnouncementService, and it is separate work with its own risk.
      return;
    }

    Playback? victim;
    lock (_stateLock)
    {
      victim = _current;

      // ⚠ Never preempt ourselves. StartDuckingAsync raises for the ATTENDED source too, and
      // EventPlaybackRequest.Priority accepts 1-10 — so a caller posting Priority 8 would otherwise
      // stop its own playback the instant it started ducking. Compared by REFERENCE on the instance
      // this service holds: three id spaces meet in this file and only the instance is unambiguous.
      if (victim is null || ReferenceEquals(victim.Source, trigger))
      {
        return;
      }
    }

    // Warning, not Information: since LOG-11 the journal carries Warning and above, and "the voicemail
    // stopped by itself" is exactly what an operator diagnoses from the box. Source ids only — never a
    // media id and never request text (PHN-1b §0.3 ⓸).
    _logger.LogWarning(
      "Attended playback {Id} preempted: source '{SourceId}' started at priority {Priority}, "
      + "at or above GvMedia:PreemptAtPriority ({Threshold})",
      victim.Id, trigger.Id, priority, threshold);

    // Addressed BY ID, captured now. If a replacing StartAsync wins the race the id no longer matches
    // _current and StopAsync is a no-op — which is right: that playback started AFTER the preempting
    // source, so "a source starts" never applied to it. What SHOULD cover that case is PR 5's queue
    // (§0.4 C-46): a playback starting under a live >= 8 source waits for it. Until then it mixes, and
    // Task 6's characterization test is what pins that so PR 5's fix is a visible diff.
    var victimId = victim.Id;
    _preemptionTail = Task.Run(
      async () =>
      {
        try
        {
          await StopAsync(victimId);
        }
        catch (ObjectDisposedException)
        {
          // The container went away underneath us. Nothing left to stop.
        }
        catch (Exception ex)
        {
          // An unobserved faulted task is a process-level hazard on this box.
          _logger.LogWarning(ex, "Error preempting attended playback {Id}", victimId);
        }
      },
      CancellationToken.None);
  }
```

- [ ] **Step 5: Build.**

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Do not commit yet.** Tasks 5 and 6 change the same file and Task 7 tests all three.

---

### Task 5 — the acquisition tail is serialised against teardown, and a cancelled acquisition disposes what it acquired

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs` — `AcquireAndPlayAsync`
- Test: Task 7

**Interfaces:**
- Consumes: `_gate`, `TearDownAsync`, `Playback.Token`, `Playback.ClaimTerminal()`,
  `PublishNonTerminal(Playback, EventPlaybackState)`.
- Produces: `SafeDisposeAsync(IEventAudioSource, string)` — used by this task only.

- [ ] **Step 1: Replace the `try` block of `AcquireAndPlayAsync`.** Keep the `switch` that produces
      `source` exactly as it is; replace everything from the existing
      `token.ThrowIfCancellationRequested();` down to and including the
      `PublishNonTerminal(playback, EventPlaybackState.Playing);` call with:

```csharp
      // ⚠ From here to Publish(Playing) runs under _gate, and PR 4 is what makes that necessary.
      // TearDownAsync is only ever called under _gate, so holding it here makes "tear this playback
      // down" and "start its audio" MUTUALLY EXCLUSIVE rather than merely ordered.
      //
      // Without it there is a window between StartDuckingAsync and PlayAsync in which a preemption can
      // complete a whole teardown — stop ducking, stop the source, dispose it, publish Stopped — and
      // PlayAsync then starts audio on a source the seam has already forgotten. That sound has no
      // playbackId, so no route, no chip and no later preemption can address it: it plays to the end,
      // over the announcement that preempted it. It is the worst outcome available in this PR.
      //
      // CancellationToken.None on the wait, matching OnSourceCompleted: acquiring the gate must not be
      // abandoned half-way. The cancellation that matters is checked inside it.
      await _gate.WaitAsync(CancellationToken.None);
      try
      {
        if (token.IsCancellationRequested)
        {
          // Someone claimed the terminal transition while we were acquiring. TearDownAsync saw a null
          // Source and disposed nothing, so this is the only place that can release what we hold — for
          // the RemoteMedia arm an open FileStream over a cached recording, which on Windows would also
          // stop GvMediaCache ever evicting that file.
          playback.ClaimTerminal();
          await SafeDisposeAsync(source, playback.Id);
          return;
        }

        // Source is assigned BEFORE ducking starts, deliberately: StartDuckingAsync now raises
        // DuckingStateChanged for this very source, and OnDuckingStateChanged's identity check reads
        // _current.Source. Assigning after would make an attended playback at Priority >= 8 preempt
        // itself.
        playback.Source = source;
        source.PlaybackCompleted += (_, e) => OnSourceCompleted(playback, e);

        _duckingService.SetPriority(source, request.Priority);
        await _duckingService.StartDuckingAsync(source, token);

        // ⚠ Nothing is checked here about OTHER active sources, and that is the owner's decision
        // (§0.4 C-46), not an omission. A playback starting while a source at or above
        // GvMedia:PreemptAtPriority is already sounding must WAIT for it and then play — which needs a
        // waiting state on the wire and a chip that renders it, so it lands in PR 5 with them. Until
        // then this mixes, exactly as it does today. Do not "fix" it by refusing the start: that option
        // was put to the owner and rejected.
        await source.PlayAsync(token);

        // ⚠ PublishNonTerminal, NOT Publish. PR 3 ships it deliberately: it re-checks
        // playback.IsTerminal under _stateLock and returns without publishing when the transition has
        // already been claimed, because a source can fail SYNCHRONOUSLY inside PlayAsync —
        // AudioFileEventSource.PlayCoreAsync catches and raises Error completion on the calling
        // thread. Substituting a bare Publish here would reintroduce exactly the bug its remark
        // describes, and PR 4 makes the guard matter more rather than less.
        PublishNonTerminal(playback, EventPlaybackState.Playing);
      }
      finally
      {
        _gate.Release();
      }
```

- [ ] **Step 2: Add the disposal helper**, next to `TearDownAsync`.

```csharp
  /// <summary>
  /// Disposes a source this service acquired but never installed. Never throws.
  /// </summary>
  /// <remarks>
  /// The install path's disposal lives in TearDownAsync, which can only see a source that reached
  /// playback.Source. A cancellation that lands between acquisition and that assignment leaves the
  /// source held by a local and nothing else — this is the only thing that can release it.
  /// </remarks>
  private async Task SafeDisposeAsync(IEventAudioSource source, string playbackId)
  {
    try
    {
      await source.DisposeAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error disposing an unstarted source for {Id}", playbackId);
    }
  }
```

- [ ] **Step 3: Confirm the bare cancellation check is gone.**

```bash
grep -n "token.ThrowIfCancellationRequested" src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs
```

Expected: no output. Its work is now the guarded `if (token.IsCancellationRequested)` inside the gate.

⚠ **`PHN-1c`'s `catch (OperationCanceledException)` clause stays.** It still catches cancellation raised
from inside `GvMediaClient`, `ITTSFactory.CreateAsync`, `StartDuckingAsync` and `PlayAsync`. Do not
remove it because this task removed the explicit throw.

- [ ] **Step 4: Build.**

```bash
dotnet build src/Radio.Infrastructure/Radio.Infrastructure.csproj --configuration Release
```

Expected: 0 warnings, 0 errors. Task 5 is self-contained; nothing in Task 6 is needed to compile it.

---

### Task 6 — pin today's mirror-case behaviour, so PR 5's queue is a visible diff

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs`

**Interfaces:**
- Consumes: `FakeDuckingService.RaiseStarted`, `CreateService`, `SpeechRequest`, `NextSnapshotWith`.
- Produces: nothing. **This task adds no production code.**

⚠ **Read §0.4 C-46 before this task.** An earlier draft of this plan built a `ThrowIfAlreadyPreempted`
check here that refused the start with `FailureReason = "PreemptedByPriority"`. **The owner rejected
that remedy and chose a queue**, which needs a waiting state on the wire and a chip to render it — so it
lands in **PR 5** with them. `ThrowIfAlreadyPreempted`, `PreemptedAtStartException` and the
`"PreemptedByPriority"` reason are **not built by this PR**, and `EventPlaybackState.Waiting` is **not
added** in anticipation: an enum member on the wire that no code can produce is a lie the size of a
state.

What PR 4 owes is that PR 5's change shows up as an edited assertion in someone's diff rather than as a
silent behavioural shift — the same discipline `PHN-1a` Task 12 used on *this* PR, now paid forward.

- [ ] **Step 1: Write the characterization test**, in the preemption region alongside Task 7's.

```csharp
  [Fact]
  public async Task APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY()
  {
    // ⚠ CHARACTERIZATION. This asserts what the seam does TODAY, not what it should do, and it is the
    // ONE test in this file written to be changed rather than kept.
    //
    // ADR-029 D5 §6.2 rule 2 is symmetric — "for speech over speech, stopping is strictly better than
    // mixing" is about the audio, not about who moved first — so a playback starting while a source at
    // or above GvMedia:PreemptAtPriority is already sounding should not add a second voice. PR 4
    // implements only the direction the ADR states in words: a STARTING high-priority source stops an
    // in-flight playback (OnDuckingStateChanged). The mirror case still mixes.
    //
    // The owner's decision of 2026-09-04 (§0.4 C-46) is that the mirror case QUEUES: the playback waits
    // for the blocking source to finish and then plays. Refusing it was considered and rejected —
    // "press play, get an error, nothing happens" is the punch list's tier (b) shape. Queueing needs a
    // waiting state on /hubs/audio and a chip that renders it, so it ships in PR 5 with them.
    //
    // ⚠ PR 5: this assertion is what should fail when you add the queue. UPDATE it — to Waiting, then
    // Playing after the blocker completes — do not delete it.
    //
    // Nothing reaches a user in the meantime: GvMedia:Enabled ships false and is not flipped until
    // PR 6, and what this falls back to is the mixing this system has always done.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    // A doorbell announcement is already sounding at priority 8.
    var blocker = new FakeEventSource();
    ducking.RaiseStarted(blocker, 8);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    var final = await playing.WaitAsync(TimeSpan.FromSeconds(5));
    await service.PreemptionTail;

    // TODAY: it plays anyway, and the room gets two voices.
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(EventPlaybackState.Playing, final.State);
    Assert.Equal(1, source.PlayCalls);
    Assert.Equal(accepted.Id, service.Current?.Id);

    // The blocker really is above the threshold, so this test is about the RULE and not about a
    // mis-configured fixture. If this line ever fails, the fixture drifted, not the behaviour.
    Assert.True(ducking.GetPriority(blocker) >= new GvMediaOptions().PreemptAtPriority);
  }
```

- [ ] **Step 2: Run it and confirm it passes as written.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj   --configuration Release   --filter "FullyQualifiedName~APlaybackStartedUnderAHigherPrioritySourceStillMixes"
```

Expected: PASS. ⚠ **A characterization test that fails on the day it is written means the behaviour is
not what this plan says it is** — stop and re-read Task 4's handler rather than adjusting the assertion
until it goes green.

- [ ] **Step 3: Confirm the rejected remedy is nowhere in the diff.**

```bash
grep -rnE "PreemptedByPriority|ThrowIfAlreadyPreempted|PreemptedAtStartException|Waiting"   src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs   src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs
```

Expected: no output. All four belong to PR 5.

---

### Task 7 — the preemption tests

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs`

**Interfaces:**
- Consumes: `EventPlaybackService.PreemptionTail`, `FakeDuckingService`, `FakeEventSource`,
  `FakeTtsFactory`, `CreateService`, `SpeechRequest`, `NextSnapshotWith` (all from `PHN-1c` Task 6).
- Produces: nothing consumed later.

⚠ **Check §0.3 items R8 and R9 before writing a line.** These tests extend `PHN-1c`'s fixtures in place;
they do not introduce a second, divergent fake.

- [ ] **Step 1: Upgrade `FakeDuckingService`** so it models the three behaviours PR 4 depends on.
      Replace the whole nested class with:

```csharp
/// <summary>Records what the seam asked of ducking, and models the three behaviours PR 4 depends on.</summary>
/// <remarks>
/// ⚠ It raises DuckingStateChanged with a real TriggeringSource on every start, exactly as
/// DuckingService does after PHN-1d Task 1; it raises IsDucking:false only when the set EMPTIES, as the
/// real service does; and it drops the per-source priority entry in StopDuckingAsync, as the real
/// service does. That last one is what makes a late GetPriority resolution fail a test rather than only
/// a review (§0.4 C-36).
/// </remarks>
private sealed class FakeDuckingService : IDuckingService
{
  private readonly List<IEventAudioSource> _active = new();

  public List<(string Id, int Priority)> Priorities { get; } = new();
  public List<string> Started { get; } = new();
  public List<string> Stopped { get; } = new();

  /// <summary>When set, StopDuckingAsync parks on it. Used to prove the preemption stop is dispatched.</summary>
  public TaskCompletionSource? StopGate { get; set; }

  public float CurrentDuckLevel => _active.Count > 0 ? 20f : 100f;
  public bool IsDucking => _active.Count > 0;
  public int ActiveEventCount => _active.Count;

  public event EventHandler<DuckingStateChangedEventArgs>? DuckingStateChanged;
  public event EventHandler<DuckingLevelChangedEventArgs>? DuckingLevelChanged;

  public Task StartDuckingAsync(IEventAudioSource s, CancellationToken ct = default)
  {
    Started.Add(s.Id);
    if (!_active.Any(a => string.Equals(a.Id, s.Id, StringComparison.Ordinal)))
    {
      _active.Add(s);
    }

    DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs
    {
      IsDucking = true,
      TriggeringSource = s,
      ActiveEventCount = _active.Count,
      DuckLevel = CurrentDuckLevel
    });
    return Task.CompletedTask;
  }

  public async Task StopDuckingAsync(IEventAudioSource s, CancellationToken ct = default)
  {
    if (StopGate is { } gate)
    {
      await gate.Task;
    }

    Stopped.Add(s.Id);
    _active.RemoveAll(a => string.Equals(a.Id, s.Id, StringComparison.Ordinal));
    Priorities.RemoveAll(p => string.Equals(p.Id, s.Id, StringComparison.Ordinal));

    // The real service raises here only when the set empties.
    if (_active.Count == 0)
    {
      DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs
      {
        IsDucking = false, TriggeringSource = s, ActiveEventCount = 0, DuckLevel = 100f
      });
    }

    DuckingLevelChanged?.Invoke(this, new DuckingLevelChangedEventArgs { TransitionComplete = true });
  }

  public Task StopAllDuckingAsync(CancellationToken ct = default) => Task.CompletedTask;

  public int GetPriority(IAudioSource s) =>
    Priorities.LastOrDefault(p => string.Equals(p.Id, s.Id, StringComparison.Ordinal))
      is { Priority: var v and > 0 } ? v : DuckingService.DefaultEventPriority;

  public void SetPriority(IAudioSource s, int priority) => Priorities.Add((s.Id, priority));

  public IReadOnlyList<IEventAudioSource> GetActiveEventsByPriority() =>
    _active.OrderByDescending(GetPriority).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();

  public void Dispose() { }

  /// <summary>Models a foreign event source — an announcement — starting.</summary>
  public void RaiseStarted(IEventAudioSource source, int priority)
  {
    SetPriority(source, priority);
    StartDuckingAsync(source).GetAwaiter().GetResult();
  }

  /// <summary>
  /// Reproduces the args DuckingService.StopDuckingAsync raises when the set EMPTIES: IsDucking false,
  /// the stopping source as TriggeringSource, and its priority entry already deleted — so GetPriority
  /// answers DefaultEventPriority (8) for it.
  /// </summary>
  /// <remarks>
  /// ⚠ Driven directly rather than through StopDuckingAsync, because the set cannot empty while an
  /// attended playback holds an entry in it. The handler's contract is about the ARGS, not about how
  /// they arose, and this is the one shape that would preempt on a stop if the IsDucking filter were
  /// dropped. §0.4 C-36 records that the identity and null checks happen to cover today's two real
  /// occurrences; this pins the guard that is designed to.
  /// </remarks>
  public void RaiseSetEmptied(IEventAudioSource source)
  {
    Priorities.RemoveAll(p => string.Equals(p.Id, source.Id, StringComparison.Ordinal));
    DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs
    {
      IsDucking = false, TriggeringSource = source, ActiveEventCount = 0, DuckLevel = 100f
    });
  }

  /// <summary>Reproduces StopAllDuckingAsync's raise: IsDucking false and a NULL TriggeringSource.</summary>
  public void RaiseStopAll() =>
    DuckingStateChanged?.Invoke(this, new DuckingStateChangedEventArgs
    {
      IsDucking = false, TriggeringSource = null, ActiveEventCount = 0, DuckLevel = 100f
    });
}
```

- [ ] **Step 2: Make `CreateService` able to take the fake and the options** (R9). If the fixture does
      not already accept them, widen it — do not construct a second service by hand:

```csharp
  private EventPlaybackService CreateService(
    FakeTtsFactory? ttsFactory = null,
    FakeDuckingService? ducking = null,
    GvMediaOptions? gvMedia = null,
    /* …PHN-1c's existing optional parameters, unchanged… */)
```

- [ ] **Step 3: Write the eight preemption tests**, in a new region at the end of the class.
      (Task 6's characterization test joins the same region; it is separated only because a
      reviewer must be able to reject the tripwire and the mechanism independently.)

```csharp
  // ── ADR-029 D5: priority is load-bearing (PHN-1d) ───────────────────────

  [Fact]
  public async Task ASourceStartingAtTheThresholdStopsAttendedPlayback()
  {
    // The row's whole point. With PhoneIntegration:Enabled false, the live instance of this is a
    // doorbell posted to /api/notifications/announce at its default priority 8 (ADR-029 §6.1).
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 8);
    await service.PreemptionTail;

    var final = await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Null(service.Current);
    Assert.Equal(1, source.StopCalls);
    Assert.Contains(source.Id, ducking.Stopped);
  }

  [Fact]
  public async Task ASourceBelowTheThresholdDoesNotStopAttendedPlayback()
  {
    // ADR-029 §6.2 rule 3, pinned: sub-threshold events keep MIXING. Recorded so the next reader does
    // not mistake it for an oversight — a Home Assistant announcement at priority 5 talks over a
    // voicemail, and fixing that means a queue across every IAnnouncementService caller.
    //
    // ⚠ This is the assertion PreemptionTail exists for. There is no snapshot to await, so without a
    // rendezvous the only options are a sleep or a poll, and starvation would make either pass for the
    // wrong reason. The decision is made synchronously inside RaiseStarted, so by the time it returns
    // PreemptionTail is already the right task.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 5);
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AnAttendedPlaybackAtPriorityEightDoesNotStopItself()
  {
    // §0.4 C-40. EventPlaybackRequest.Priority accepts 1-10 and StartDuckingAsync now raises for the
    // ATTENDED source too, so without the identity check a caller posting Priority 8 would preempt
    // itself the instant it started ducking — reaching Playing and immediately reporting Stopped.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest() with { Priority = 8 });
    await playing.WaitAsync(TimeSpan.FromSeconds(5));
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AStoppingSourceNeverPreempts()
  {
    // §0.4 C-36, the sharpest trap in this PR. DuckingService.StopDuckingAsync deletes the source's
    // priority entry BEFORE it raises, and GetPriority then falls back to DefaultEventPriority (8). So
    // a handler that acted on IsDucking:false — or that resolved the priority late, on the dispatched
    // task — would read an ENDING announcement as a priority-8 preemption and stop the voicemail every
    // time something else finished talking.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // A foreign announcement that started at priority 3 and has now ended: its entry is gone, so
    // GetPriority answers 8 for it. Only the IsDucking filter stops this being a preemption.
    var announcement = new FakeEventSource();
    ducking.RaiseStarted(announcement, 3);
    await service.PreemptionTail;
    ducking.RaiseSetEmptied(announcement);
    await service.PreemptionTail;

    Assert.Equal(8, ducking.GetPriority(announcement));   // the trap is real, not hypothetical
    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current?.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task AStopAllRaiseWithNoTriggeringSourceIsIgnored()
  {
    // §0.7 failure 8. StopAllDuckingAsync raises with a NULL TriggeringSource. Without the
    // pattern-match null check that is a NullReferenceException inside DuckingService's invocation —
    // which is failure 2, a silently swallowed announcement the API still reports as 200.
    //
    // StopAllDuckingAsync has no non-test callers today and this PR does not give it one (§4 item 4).
    // The check is here because the event's own contract permits null, not because a caller does it.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    ducking.RaiseStopAll();
    await service.PreemptionTail;

    Assert.Equal(accepted.Id, service.Current?.Id);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task PreemptionIsDispatched_TheRaisingThreadIsNotHeldForTheTeardown()
  {
    // §0.4 C-39. This handler runs on the thread inside DuckingService.StartDuckingAsync — on the live
    // path AnnouncementService's, mid-announcement — and StopAsync takes _gate, which this service is
    // already holding whenever the raise came out of TearDownAsync. A handler that awaited the stop
    // would deadlock a non-reentrant semaphore there, and everywhere else would block the doorbell for
    // the length of a voicemail teardown.
    //
    // Parking StopDuckingAsync makes the teardown observably incomplete: if RaiseStarted returns at all
    // while PreemptionTail is still pending, the stop was dispatched rather than awaited. (If it were
    // awaited, this test would hang rather than fail — which is the correct signal for a deadlock.)
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ducking.StopGate = release;

    ducking.RaiseStarted(new FakeEventSource(), 9);

    Assert.False(service.PreemptionTail.IsCompleted);
    Assert.Equal(accepted.Id, service.Current?.Id);

    release.SetResult();
    await service.PreemptionTail;

    Assert.Null(service.Current);
  }

  [Fact]
  public async Task PreemptingAPlaybackThatAlreadyEndedChangesNothing()
  {
    // Idempotence through Playback.ClaimTerminal, from the direction PR 4 introduces. A preemption
    // arriving just after a natural end must not overwrite Completed with Stopped, must not publish a
    // second terminal snapshot and must not, from PR 5, broadcast a transition that did not happen.
    var ducking = new FakeDuckingService();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    var terminals = new List<EventPlaybackSnapshot>();
    service.PlaybackChanged += (_, s) =>
    {
      if (s.State is EventPlaybackState.Completed or EventPlaybackState.Stopped
          or EventPlaybackState.Failed)
      {
        lock (terminals) { terminals.Add(s); }
      }
    };

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    await WaitUntilAsync(() => service.Current is null, TimeSpan.FromSeconds(5));

    ducking.RaiseStarted(new FakeEventSource(), 9);
    await service.PreemptionTail;

    lock (terminals)
    {
      Assert.Single(terminals);
      Assert.Equal(EventPlaybackState.Completed, terminals[0].State);
    }
  }

  [Fact]
  public async Task PreemptingAPreparingPlaybackCancelsAcquisitionAndDisposesWhatItAcquired()
  {
    // §0.4 C-41, both halves. A preemption during Preparing must cancel the acquisition, and the source
    // the acquisition then produces must be DISPOSED — TearDownAsync saw a null Source and disposed
    // nothing, so the acquisition tail is the only thing that can release it. On the RemoteMedia arm
    // that is an open FileStream over a cached recording, which on Windows would also stop GvMediaCache
    // ever evicting the file. And it must never reach PlayAsync: audio started here would have no
    // playbackId, so nothing could ever stop it.
    var ducking = new FakeDuckingService();
    var acquired = new FakeEventSource();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var tts = new FakeTtsFactory
    {
      OnCreate = async (_, _, _) =>
      {
        await release.Task;                 // deliberately NOT observing the token: the point is that the
        return (IEventAudioSource)acquired; // tail must cope with a source that arrives after the stop.
      }
    };
    using var service = CreateService(ttsFactory: tts, ducking: ducking);

    // Something at priority 9 is already sounding, so the attended playback is preempted while Preparing.
    var accepted = await service.StartAsync(SpeechRequest());
    Assert.Equal(EventPlaybackState.Preparing, accepted.State);

    ducking.RaiseStarted(new FakeEventSource(), 9);
    await service.PreemptionTail;

    release.SetResult();
    await WaitUntilAsync(() => acquired.DisposeCalls == 1, TimeSpan.FromSeconds(5));

    Assert.Equal(0, acquired.PlayCalls);
    Assert.Null(service.Current);
  }

```

- [ ] **Step 4: Run the new tests.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~EventPlaybackServiceTests"
```

Expected: PASS, including every `PHN-1c` test in the file unchanged.

- [ ] **Step 5: Prove the tests can fail.** Do all four, one at a time, reverting each:

| Break | Test that must go red |
|---|---|
| Delete the `ReferenceEquals(victim.Source, trigger)` clause | `AnAttendedPlaybackAtPriorityEightDoesNotStopItself` |
| Drop `!e.IsDucking` from the guard, leaving `if (e.TriggeringSource is not { } trigger)` | `AStoppingSourceNeverPreempts` |
| Drop the `is not { } trigger` half, leaving `if (!e.IsDucking)` | `AStopAllRaiseWithNoTriggeringSourceIsIgnored` (NRE) |
| `await StopAsync(victimId)` inline instead of dispatching | `PreemptionIsDispatched_…` (hangs or fails) |

⚠ A test that stays green under its break is not testing what its name says. Fix the test, not the
table.

⚠ **One guard has no single-line mutation on this list, and pretending otherwise would be worse than
saying so: the synchronous `GetPriority` read (C-36).** The read and the decision are the same
statement, so "move it late" is a restructure into dispatch-then-decide rather than an edit — there is
no one line to delete. What the tests do give is the *material*: `FakeDuckingService` deletes the
priority entry on stop exactly as the real service does, so a restructured handler that resolved the
priority on the dispatched task would read 8 for a stopped source in this fixture too. **A reviewer
should check this one by reading, not by trusting a red test.**

- [ ] **Step 6: Commit Tasks 4-7.**

```bash
git add src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs \
        tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs
git commit -m "PHN-1d: attended playback yields to anything that outranks it

ADR-029 D5 rule 2, in the direction the ADR states in words. EventPlaybackService
subscribes to DuckingStateChanged and stops itself when a source starts at or
above GvMedia:PreemptAtPriority (8) -- the key's first reader in its life.

The mirror direction -- a playback STARTING while such a source is already
sounding -- is confirmed by the owner and deferred to PR 5, where it becomes a
queue rather than a refusal: it waits for the blocker and then plays. Refusing
was considered and rejected. The queue needs a waiting state on the wire and a
chip to render it, and both are PR 5/6; a queue nobody can see is the same
"press play, nothing happens" the refusal was rejected for. Today's mixing is
pinned as a characterization test so that change is a visible diff.

Three things are load-bearing and none is obvious: IsDucking:false is ignored
because StopDuckingAsync deletes the priority entry before raising and
GetPriority then answers the category default of 8; the priority is read
synchronously on the raising thread for the same reason; and the stop is
dispatched because this handler runs inside AnnouncementService's call and
StopAsync takes a gate this service may already hold.

The acquisition tail now runs under that gate. Without it a preemption can
complete a whole teardown between StartDuckingAsync and PlayAsync, and PlayAsync
then starts audio with no playbackId -- a sound nothing can stop, playing over
the announcement that preempted it.

GetPriority becomes load-bearing for the first time in this system."
```

---

### Task 8 — pin the coupling between the threshold and the category default

**Files:**
- Modify: `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs`

**Interfaces:**
- Consumes: `DuckingService.DefaultEventPriority`, `GvMediaOptions.PreemptAtPriority`.

- [ ] **Step 1: Add the test**, at the end of the preemption region.

```csharp
  [Fact]
  public void PreemptAtPriorityMustNotExceedTheEventCategoryDefault()
  {
    // §0.4 C-43. GetPriority answers DuckingService.DefaultEventPriority for EVERY event source whose
    // caller never called SetPriority — which is every source in this tree except the ones
    // AnnouncementService creates. So the threshold has a CEILING as well as a floor, and only the
    // floor is documented anywhere:
    //
    //   threshold <= 8  → unclaimed sources preempt. ADR-029 §6.1's stated intent: "anything that did
    //                     not explicitly claim a rank still outranks a user listening to a recording."
    //   threshold >= 9  → unclaimed sources read 8 and stop preempting. Preemption still works for the
    //                     one dormant caller that explicitly sets 9, so nothing LOOKS broken; it just
    //                     stops happening for the live one. Two clicks on a knob delete the feature.
    //
    // Lowering it to 7 is the change ADR-029 §6.1 anticipates and this test permits.
    Assert.Equal(8, DuckingService.DefaultEventPriority);

    var shipped = new GvMediaOptions().PreemptAtPriority;
    Assert.True(
      shipped <= DuckingService.DefaultEventPriority,
      $"GvMedia:PreemptAtPriority defaults to {shipped}, above DuckingService.DefaultEventPriority "
      + $"({DuckingService.DefaultEventPriority}). Every event source whose caller never calls "
      + "SetPriority reads as that default, so a threshold above it silently exempts almost everything "
      + "from preemption while leaving the feature apparently intact. Lower the threshold, or lower "
      + "the default with it.");
  }
```

⚠ **This pins the two compile-time defaults only.** An operator override in
`appsettings.Production.json` is beyond what any test can see — which is what Task 9's doc line is for.

- [ ] **Step 2: Run it.**

```bash
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~PreemptAtPriorityMustNotExceed"
```

Expected: PASS.

---

### Task 9 — the three statements this PR makes false

**Files:**
- Modify: `design/INTEGRATIONS.md` — § *How Audio Ducking Works* item 4 (grep for
  `Higher priority announcements can interrupt`)
- Modify: `design/INTEGRATIONS.md` — the `GvMedia` config table row for `PreemptAtPriority` (grep for
  `Priority at or above which a starting source preempts`)
- Modify: `src/Radio.Core/Configuration/GvMediaOptions.cs` — the `PreemptAtPriority` remark

⚠ **Grep for the text, never `sed -n '566p'`** — C-34 is what happens otherwise.

- [ ] **Step 1: Rewrite `INTEGRATIONS.md` item 4.** Replace the whole struck-through bullet with:

```markdown
4. **Higher-priority events interrupt attended GV playback — and nothing else.** ⚠ Be precise about the
   scope, because the claim here was wrong in both directions before. Since `PHN-1d` (ADR-029 D5), an
   event source that starts at or above `GvMedia:PreemptAtPriority` (**8**) **stops** an in-flight
   voicemail or spoken message outright — it does not pause it, and the recording is replayable at zero
   cost. ⚠ **Only that direction.** Pressing play while such a source is *already* sounding still
   **mixes** today; the owner's decision of 2026-09-04 is that it should **wait and then play**, and
   that queue ships with the console-playback chip that can show it is waiting. **Also still NOT true is
   announcement-versus-announcement.** Ducking remains binary and reference-counted:
   the first event fades the primary to the fixed global `Audio:DuckingPercentage` (20), every
   subsequent concurrent event leaves the *level* alone, and full volume returns only when the last
   event leaves. An announcement at 9 does not interrupt one at 3 — they **mix**. ADR-029 §6.2 rule 3
   declines to fix that on purpose: the fix is a queue across every caller of `IAnnouncementService`,
   which is separate work. ⚠ **The live consequence, which is intended (ADR-029 §6.1):** with
   `PhoneIntegration:Enabled` false, the only thing on this box that can preempt attended playback is a
   notification posted to `/api/notifications/announce` at its default priority 8 — **a doorbell will
   stop a voicemail mid-play.** Priority is otherwise still accepted, validated, stored, and used for
   nothing else; the guidance table below remains intent rather than behaviour outside that one rule.
```

- [ ] **Step 2: Rewrite the config-table row.** Replace the existing `PreemptAtPriority` row with:

```markdown
| `PreemptAtPriority` | `8` | Priority at or above which a starting event source stops attended playback (ADR-029 D5). ⚠ **Safe to lower, a trap to raise.** `DuckingService.GetPriority` answers its category default — **8** — for every event source whose caller never called `SetPriority`, which is all of them outside `AnnouncementService`. So `7` widens preemption to the documented "high importance" band, while **`9` or `10` silently exempts almost everything**: preemption keeps working for callers that name an explicit 9, so nothing looks broken, and stops happening for the live ones. Keep it at or below `DuckingService.DefaultEventPriority`. |
```

- [ ] **Step 3: Correct `GvMediaOptions.PreemptAtPriority`'s remark** — the shipped text says *"Consumed
      by PR 4 … Not read by this PR"*, which was true when PR 2 wrote it and is false the day this
      merges. That is the exact failure class `CLAUDE.md` § Pre-Merge Review exists for, and the
      precedent is `PHN-1b` Task 12 and `PHN-1c` Task 11. Replace the member with:

```csharp
  /// <summary>
  /// Priority at or above which a starting event source stops attended playback (ADR-029 D5 §6.1).
  /// Read by <c>EventPlaybackService.OnDuckingStateChanged</c>, which stops an in-flight playback when
  /// such a source starts. ⚠ The mirror direction — a playback STARTING while such a source is already
  /// sounding — is not implemented yet: it mixes. The owner's decision is that it must WAIT for the
  /// blocking source and then play, and that lands with the server-owned playback state that can
  /// broadcast a waiting state to a client. Do not implement it as a refusal; that option was
  /// considered and rejected.
  ///
  /// <para>
  /// ⚠ This value is safe to LOWER and a trap to RAISE, and only the lowering case is argued in the
  /// ADR. <c>DuckingService.GetPriority</c> answers <c>DefaultEventPriority</c> — 8 — for every event
  /// source whose caller never called <c>SetPriority</c>, which is every source in this tree outside
  /// <c>AnnouncementService</c>. So 7 widens preemption to the documented high-importance band, while 9
  /// or 10 exempts almost everything: preemption still works for a caller that names an explicit 9, so
  /// nothing looks broken, and it stops happening for the live ones. Keep this at or below
  /// <c>DuckingService.DefaultEventPriority</c>; a test pins the two shipped defaults against each
  /// other, and it cannot see a per-machine override.
  /// </para>
  /// </summary>
  public int PreemptAtPriority { get; set; } = 8;
```

- [ ] **Step 4: Verify nothing else in the tree still claims priority is inert.**

```bash
grep -rn "accepted, validated, stored, and then \*\*ignored\*\*\|zero non-test callers\|not true today" \
  design/ docs/ --include=*.md
```

Every remaining hit must be inside a **merged plan or ADR** (history — leave it) or must be about
`StopAllDuckingAsync`, which is still dead. A live claim in `INTEGRATIONS.md` or the punch list that
priority arbitrates nothing must be corrected here. ⚠ **Do not edit `docs/BUILDER_QUEUE.md`,
`docs/HANDOFF-NEXT-SESSION.md` or `docs/HANDOFF-GA-PUNCH-LIST.md` in this PR** — see §0.5 and the row
note in §6.

- [ ] **Step 5: Commit.**

```bash
git add design/INTEGRATIONS.md src/Radio.Core/Configuration/GvMediaOptions.cs
git commit -m "PHN-1d: correct the three statements this PR makes false

INTEGRATIONS.md's standing correction said higher-priority announcements cannot
interrupt lower ones. That is no longer wholly true and is not wholly false
either: attended GV playback is now preempted at >= 8, and announcement-versus-
announcement still mixes because ADR-029 6.2 rule 3 declines to fix it. Restoring
the original sentence would have replaced a true statement with a false one.

Three predecessor plans point at :566, which never held this claim -- it is a code
fence in the hub runbook. The number then moved twice in two days (:932 before
#556; :980 as recorded by that PR, also wrong; :997 at b77ffe12), so this claim is
now cited by its sentence and not by any offset. C-19 on its own doorstep.

PreemptAtPriority's own remark said 'not read by this PR'; it now names both
readers, and both doc surfaces now record the ceiling as well as the floor: the
key is safe to lower and a trap to raise, because GetPriority answers 8 for every
source that never claimed a rank."
```

---

### Task 10 — build, test, and the scope gate

**Files:** none.

- [ ] **Step 1: Full build.**

```bash
dotnet build --configuration Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite.**

```bash
dotnet test --configuration Release --verbosity normal
```

Expected: all green. ⚠ Known-flaky and unrelated: `Radio.Web`'s `AudioApiService`
`_WhenServerNotAvailable` timeout tests. Anything else red is this PR's.

- [ ] **Step 3: Scope gate — assert what this PR did NOT touch.**

```bash
# No config key (0.5 item 4). Must print nothing.
git diff --stat main -- '*appsettings*.json'

# No Radio.Web change (0.5 item 1). Must print nothing.
git diff --stat main -- src/Radio.Web/

# No route or controller change (0.5 item 3). Must print nothing.
git diff --stat main -- src/Radio.API/

# No AnnouncementService change (0.5 item 6). Must print nothing.
git diff --stat main -- src/Radio.Infrastructure/Audio/Services/AnnouncementService.cs

# The queue and the two handoff docs are owned by another cycle. Must print nothing.
git diff --stat main -- docs/BUILDER_QUEUE.md docs/HANDOFF-NEXT-SESSION.md docs/HANDOFF-GA-PUNCH-LIST.md

# Neither tripwire was deleted. Both must print a count > 0.
grep -c "\[Fact\]" tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs
grep -c "SetPriority_DoesNotChangeTheDuckLevel_TODAY" \
  tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs
```

- [ ] **Step 4: Scope gate — assert the two mistakes this arc keeps warning about.**

```bash
# No mixer.AddSource anywhere in this PR's diff (PHN-1c 0.6 -- the most copy-able mistake in the arc).
git diff main -- src/ | grep -n "AddSource" || echo "OK: no AddSource"

# The full expected file list. Anything else is scope creep.
git diff --name-only main
```

Expected exactly:

```
design/INTEGRATIONS.md
design/plans/PHN-1d-ducking-priority-load-bearing.md
src/Radio.Core/Configuration/GvMediaOptions.cs
src/Radio.Infrastructure/Audio/Services/DuckingService.cs
src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs
tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs
tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceCharacterizationTests.cs
tests/Radio.Infrastructure.Tests/Audio/Services/DuckingServiceTests.cs
```

- [ ] **Step 5: Write §0.3's answers into the PR description**, item by item, saying which assertions
      still held against merged `main` and which needed adapting. Nine were verified against PR 3's
      pre-merge code and three were open; **PR 3's own review could have moved any of them**, so a
      re-grep is not optional. That table is the record of what this plan checked and what it assumed,
      and a PR that does not answer it leaves the next reader unable to tell the two apart.

---

## 2. Test Plan

### 2.1 What the automated tests actually prove

- **`DuckingService` announces every source that joins the set, and only those.** Two characterization
  tests (`…RaisesOncePerSourceThatJoinsTheSet`, `…DoesNotRaise_ForASourceAlreadyInTheSet`) plus the
  renamed `DuckingServiceTests` one. The raise carries the right `TriggeringSource` and
  `ActiveEventCount`.
- **The duck level is still not priority-weighted.** `SetPriority_DoesNotChangeTheDuckLevel_TODAY`,
  unmodified, is the proof that Task 1 changed announcement and not arbitration.
- **A throwing subscriber cannot escape `StartDuckingAsync`**, so it cannot silence an announcement that
  the API then reports as `200`.
- **Preemption fires at the threshold and not below it**, and the below-threshold case is asserted
  deterministically rather than by waiting.
- **A playback never preempts itself**, even at `Priority = 8`.
- **A stopping source never preempts**, which is the C-36 trap and the one most likely to be
  reintroduced by a well-meaning refactor — pinned by driving the exact args `StopDuckingAsync` raises
  when the set empties, with the priority entry already deleted, so `GetPriority` really does answer 8.
- **A `StopAllDuckingAsync`-shaped raise, with a null `TriggeringSource`, is ignored** rather than
  throwing inside `DuckingService`'s invocation list.
- **The stop is dispatched, not awaited on the raising thread** — pinned by parking
  `StopDuckingAsync` and observing an incomplete `PreemptionTail`.
- **A preemption after a natural end changes nothing**, so exactly one terminal snapshot is ever
  published.
- **A preemption during `Preparing` cancels acquisition, disposes what acquisition produced, and never
  reaches `PlayAsync`** — the C-41 orphan.
- **A playback started under a live ≥ threshold source still MIXES**, which is today's behaviour and
  not the intended one — pinned as a characterization test (Task 6) so PR 5's queue (§0.4 C-46) arrives
  as an edited assertion rather than as a silent shift.
- **The threshold's two shipped defaults are coupled**, so lowering `DefaultEventPriority` without
  lowering the threshold fails a test with an explanation rather than silently disabling the feature.

### 2.2 What the tests cannot prove — carried to PR 6's UAT

1. **That preemption sounds right.** Every test here drives fakes. Whether a doorbell stopping a
   voicemail *feels* correct — versus startling, or too abrupt — is a listening judgement on the box,
   and it is the one thing that could send this design back. **Carry it into PR 6's UAT:** start a
   voicemail with the radio on, `curl -X POST http://radio:5000/api/notifications/announce -H
   'Content-Type: application/json' -d '{"Message":"Someone is at the door"}'` (priority defaults to 8),
   and confirm the voicemail stops, the announcement is intelligible, and the radio returns to full
   volume afterwards.
2. **That ducking releases cleanly after a preemption.** Two `StopDuckingAsync` calls now interleave —
   the attended source's, from `TearDownAsync`, and the announcement's, when it ends. The fake models
   the reference count; only the box proves the fade actually returns to 100%.
3. **That a preemption during `Preparing` does not leave a file handle on the appliance.** The test
   asserts `DisposeAsync` was called; whether the underlying `FileStream` is really released, and
   whether `GvMediaCache` can then evict, needs a real fetch — which requires `GvMedia:Enabled`, still
   `false`, still PR 6's.
4. **That queueing is the right call in the room — because PR 4 does not build it.** The owner chose a
   queue over a refusal for the mirror case (§0.4 C-46) and PR 5 implements it. What no test anywhere
   can settle is the wait itself: a voicemail that starts several seconds after the tap, once the
   doorbell has finished, may read as responsive or may read as broken. **Carry it into PR 5's or PR 6's
   UAT alongside item 1**, and check the two together — they are the same rule from opposite sides and
   they should feel like one behaviour, not two.
5. **That the extra Information line per concurrent announcement costs nothing audible.** C-37's
   consequence is one log line per source, to the file sink. Assumed negligible on an N100; not
   measured.

### 2.3 Commands

```bash
# Build
dotnet build --configuration Release

# The two suites this PR moves
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~Ducking"
dotnet test tests/Radio.Infrastructure.Tests/Radio.Infrastructure.Tests.csproj \
  --configuration Release --filter "FullyQualifiedName~EventPlaybackServiceTests"

# Everything
dotnet test --configuration Release --verbosity normal
```

---

## 3. Self-review

**Spec coverage — ADR-029 D5, clause by clause.**

| ADR clause | Where |
|---|---|
| §6.1 attended playback registers at priority 6 | Shipped by PR 3 (`EventPlaybackRequest.Priority = 6`). Verified, not re-implemented. |
| §6.1 `GvMedia:PreemptAtPriority` stays 8 | Unchanged; Task 8 pins it, Task 9 documents both directions. |
| §6.2 rule 1 — attended vs attended → replace | Shipped by PR 3 (`StartAsync`'s replacement path). Not touched. |
| §6.2 rule 2 — priority ≥ threshold → preempt | **Half.** Task 4 ships the direction the ADR states in words (a starting ≥ 8 source stops an in-flight playback). The mirror direction is confirmed by the owner and **deferred to PR 5 as a queue** (§0.4 C-46); Task 6 pins today's behaviour so that change is a visible diff. |
| §6.2 rule 3 — sub-8 keeps mixing | Task 4's early return; pinned by `ASourceBelowTheThresholdDoesNotStopAttendedPlayback`; documented by Task 9. |
| §6.3 `StartDuckingAsync` raises unconditionally | Task 1, narrowed to "every source that joins" (C-35). |
| §6.3 safe for the existing subscriber | C-37 — verified against `AudioManager`, and the ADR's "no-op" corrected to "log-only". |
| §6.2 rule 2 "does not pause-and-resume" | Task 4 calls `StopAsync`, never `PauseAsync`. Stated in the remark. |
| PR 4's handoff obligation: update the tripwire | Task 3, **both** tripwires (C-45). |
| PR 4's handoff obligation: correct `INTEGRATIONS.md` | Task 9, with the citation and the claim both corrected (C-34). |

**Placeholder scan.** No `TBD`, no "implement later", no "similar to Task N", no "add appropriate error
handling". Every code step carries literal, complete code. The one place this plan cannot be literal is
§0.3, and it is explicit about why and about what to do in each case.

**Type consistency.** `PreemptionTail` (property) / `_preemptionTail` (field);
`SafeDisposeAsync(IEventAudioSource, string)`; `PublishNonTerminal(Playback, EventPlaybackState)`,
which is PR 3's and is called rather than replaced (§0.3). `FakeDuckingService.RaiseStarted(IEventAudioSource,
int)` and `StopGate` are defined in Task 7 Step 1 and used in Steps 3. `GvMediaOptions.PreemptAtPriority`
and `DuckingService.DefaultEventPriority` are shipped members, verified at `6b3dcc2e`.

**The claim most likely to be wrong.** §0.4 C-38's trace — that a throwing subscriber costs a swallowed
announcement rather than stuck ducking. It rests on `AnnounceAsync`'s `finally` calling
`CleanupSourceAsync`, read at `6b3dcc2e`. A reviewer should re-read that `finally` rather than trust
this sentence; the guard is right either way, but the *reason* in the remark would need correcting.

---

## 4. Things this plan deliberately does not do, with the reason

1. **Make ducking priority-weighted.** The duck *level* stays the fixed global
   `Audio:DuckingPercentage` regardless of what is playing. D5 is about who stops, not about how far
   the radio drops, and `SetPriority_DoesNotChangeTheDuckLevel_TODAY` is left as the proof that this PR
   respected that line.
2. **Introduce either queue.** Two distinct ones are in play. **(a) The sub-8 mixing wart:** ADR
   §6.2 rule 3 names it and declines — *"there is no 'wait your turn'"* — and the fix is a queue across
   every caller of `IAnnouncementService`, outside this arc entirely. Task 9 documents that a priority-5
   announcement still talks over a voicemail. **(b) The mirror-case queue** the owner chose on
   2026-09-04 (§0.4 C-46): a playback starting under a live ≥ 8 source waits for it. That one **is** in
   the arc, and it is **PR 5's** — §5 carries its five open design questions with a lean on each.
   ⚠ The reason it is not here is the owner's own: he rejected refusing because *"press play, nothing
   happens"* is embarrassing, and **a queue whose waiting state nobody can see is that same sentence for
   longer.** Visibility is PR 5's broadcast and PR 6's chip. Shipping the queue ahead of them would
   deliver the half of his decision he disliked and defer the half that redeems it.
3. **Give the preemption a distinguishable stop reason.** `StopAsync` publishes `Stopped` with no
   reason, so a preemption and a user stop look identical on the wire. That is what `PHN-1c`'s handoff
   told PR 4 to call, and the shipped `EventPlaybackState.Stopped` doc already names preemption as one
   of its three causes. **If PR 6's chip needs to say "interrupted" rather than "stopped", the field to
   add is PR 5's** — it owns the broadcast and the snapshot's shape on the wire. Adding a second meaning
   to `FailureReason` on a non-`Failed` state here would have been the cheaper-looking, worse choice.
4. **Use `StopAllDuckingAsync`.** It is the other member ADR §1.2 correction 2 found dead, and the
   symmetry is tempting. It is wrong for this: it clears **every** active event and restores full volume
   instantly, so preempting a voicemail with it would also un-duck the announcement that preempted it.
   It stays dead, and it stays recorded as dead.
5. **Reject `Priority >= PreemptAtPriority` in `EventPlaybackRequest.Validate`.** It would couple a
   `Radio.Core` validator to an Infrastructure config key, and the identity check in Task 4 is required
   regardless — so the validation would be a second mechanism guarding a case the first already
   handles.
6. **Fix `TTSEventSource.Position`** (`PHN-1c` C-27), **`TTSFactory`'s engine cache** (C-31), or
   `SourcesController`'s two defective event paths. All recorded elsewhere, none this PR's.
7. **Add a queue row for anything found here.** Nothing new was found that needs one: C-43's ceiling is
   closed by a test and two doc lines, and C-41's orphan is fixed rather than filed. **And the queue is
   owned by another Builder cycle while this plan is written** — see §6.
8. **Touch `docs/HANDOFF-NEXT-SESSION.md` or `docs/HANDOFF-GA-PUNCH-LIST.md`.** Out of scope for this
   row by instruction; the punch list's `PHN-1` line is the owner's to move when the arc completes.

---

## 5. Handoff to the rest of the arc

**Do not re-sequence the arc.** The breakdown's order stands; this plan implements PR 4 of it unchanged.
C-42 widens rule 2 from an edge to a state within PR 4's own scope; it moves nothing between PRs.

**To PR 5 (server-owned state and the three stop conditions):**

- **`PlaybackChanged` now fires for preemptions too**, and they arrive on a `Task.Run` thread rather than
  on the request thread. The hub broadcast must be safe to invoke from an arbitrary thread pool thread —
  `IHubContext` is, but a subscriber that touches anything with affinity is not.
- **The max-duration cap lands on the same `CancellationTokenSource` preemption already uses.**
  `PHN-1c` §5 gives the recipe (`CancelAfter(GvMedia:MaxPlaybackSeconds)` on `Playback`'s CTS at the
  point the source starts playing). ⚠ **That point is now inside `_gate`** (Task 5) — set it there, not
  outside, or the cap can be armed on a playback that a preemption has already torn down.
- ⚠ **If the chip needs to distinguish a preemption from a user stop**, add the field here, in the
  snapshot, and change Task 4's `StopAsync` call to carry it (§4 item 3). Do not overload
  `FailureReason` on a `Stopped` state.

**⭐ To PR 5 — THE MIRROR-CASE QUEUE. Owner decision, 2026-09-04 (§0.4 C-46). This is net-new scope for
PR 5 and it should be sized before the row is claimed.**

**The rule:** a playback that starts while a source at or above `GvMedia:PreemptAtPriority` is already
sounding **waits for that source to finish and then plays**. It does not mix (today's behaviour, pinned
by PR 4's `APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY` — **that assertion is what should
fail when you build this; update it, do not delete it**). It does not fail: refusing was put to the
owner and rejected as the punch list's tier (b) *"press play, get an error, nothing happens"* shape.

**Why it is PR 5's and not PR 4's**, so it is not re-litigated: the owner's objection to refusing is that
nothing visibly happens. A queue whose waiting state nobody can see is the same complaint for longer, so
the queue is only better than a refusal once it is **visible** — and visibility is PR 5's `/hubs/audio`
broadcast plus PR 6's chip. It is also cohesive here rather than dumped: a waiting playback **is**
server-owned playback state, and its staleness expiry **is** a fourth stop condition.

**Five questions it must answer. A lean and a reason for each — none of these is settled, and PR 5's
planner owns the decision.**

1. **Depth — lean: ONE deep, with replace semantics.** A second tap while one is waiting replaces the
   waiting one; it never builds a list. Two reasons: `StartAsync` already does exactly this for
   in-flight playbacks (ADR §6.2 rule 1), so the console gains no second mental model; and a queue
   deeper than one is a list the user cannot see and cannot reorder on a wall panel. ADR D6 §8.1's
   "one set of speakers" argues the same way.
2. **The cancel path — lean: `StopAsync` must resolve the pending slot too, and `Current` must report
   it.** A queued playback nobody can cancel or even see is worse than a failure. ⚠ **This changes a
   shipped contract:** `IEventPlaybackService.Current`'s doc says *"The one **in-flight** attended
   playback, or null"*, and a waiting playback is not in flight. Correct that doc **in the same PR** —
   it is the `CLAUDE.md` § Pre-Merge Review failure class, and this arc has now corrected four of them.
   `GET /api/audio/events/current` is also ADR §8.1's re-attach path, so a waiting state that is absent
   from it is invisible to a client that just connected.
3. **Staleness — lean: a hard bound, `Failed` with `FailureReason = "WaitExpired"`, 30 seconds.** An
   unbounded wait that fires into a quiet room three minutes later is its own defect. 30 s because the
   thing being waited on is a TTS notification measured in seconds; a wait longer than its blocker means
   the blocker was not what we thought. **`Failed` is the honest state** (*"never produced sound"*), and
   failing is acceptable *here* precisely because by then the user has watched a visible "waiting"
   state — which is what made a bare failure embarrassing. Needs one new key, `GvMedia:MaxQueuedWaitSeconds`;
   ⚠ **check `PHN-1b` C-14 first** — a key added to `src/Radio.API/appsettings.json` does reach the
   appliance, but a per-machine override needs two hand edits to two long-diverged files.
4. **The trigger — and this is the awkward one, so read it before sizing the row.** Trap 5 forbids polls
   and ticks, so the wake must be an event, and the only event available is
   `IDuckingService.DuckingStateChanged` — **specifically the `IsDucking: false` raise that PR 4's
   handler deliberately ignores** (C-36). PR 4's `OnDuckingStateChanged` is already the right place:
   `if (!e.IsDucking) { WakeAnyPendingPlayback(); return; }`.
   ⚠ **Known starvation case, and it is real:** `StopDuckingAsync` raises `IsDucking: false` **only when
   the ducking set empties**. So a ≥ 8 announcement ending while a sub-8 announcement continues produces
   **no wake at all**, and the queued playback waits until the room is fully quiet or question 3's bound
   fires. **Lean: accept it in v1 and say so**, because the obvious alternative is worse. Making
   `StopDuckingAsync` raise for every source that leaves would be a second engine change with two
   distinct hazards: raising `IsDucking: false` while others remain makes `AudioManager` call
   `ClearDuckingMultiplier` and **the radio jumps to full volume mid-announcement**; raising
   `IsDucking: true` instead hands PR 4's handler a `TriggeringSource` whose priority entry
   `StopDuckingAsync` has **already deleted**, so `GetPriority` answers 8 and a *stop* becomes a
   preemption — C-36's exact trap, made reachable. Doing it safely needs a start/stop discriminator on
   `DuckingStateChangedEventArgs`, which is a `Radio.Core` change; that is a deliberate decision, not a
   detail to slip in.
5. **C-41 gets WORSE with a queue, not better — do not lose this.** A deferred start is a **second entry
   point** into the acquisition tail, reached from an event handler rather than from `StartAsync`. PR 4's
   protection is that the tail from the cancellation check to `PublishNonTerminal(Playing)` runs under
   `_gate`, and that `TearDownAsync` only ever runs under `_gate` too. **Route the deferred start through
   that same gated path**, not a parallel one. A wake that starts audio outside `_gate` reopens exactly
   the window Task 5 closed — audio with no `playbackId`, which nothing can stop — on a path that has no
   `StartAsync` to serialise against.

**And one thing PR 5 must NOT do:** add `EventPlaybackState.Waiting` anywhere but the **END** of the
enum. The shipped comment on `EventPlaybackRejection.MediaIdHasIllegalCharacter` records why — these
names reach log lines and the wire, and inserting into the middle is how one quietly stops meaning what
it used to.
- Everything `PHN-1c` §5 handed PR 5 still stands: seed `AudioStateStore` from
  `GET /api/audio/events/current`; `AudioStateStore` has never been constructed in its life, so it needs
  a consumer first; a speech playback's `PositionAtBroadcast` is always zero and
  `ASpeechSnapshotReportsPositionZeroForItsWholeLife` is the test to update when you fix it.

**To PR 6 (`PHN-2` — retire the `<audio>` element):**

- **Carry the preemption UAT.** It is item 1 of §2.2 above and it is the only thing that settles whether
  this design is right: play a voicemail with the radio on, post a default-priority notification, and
  listen. ⚠ **Record the outcome even if it is fine** — the ADR accepted "a doorbell stops a voicemail"
  on paper and nobody has yet heard it.
- **Render the waiting state PR 5 adds** (§0.4 C-46). The owner's whole reason for choosing a queue
  over a refusal is that the user should see something happen; the chip is where that happens. Until it
  renders, the queue is the refusal he rejected, with a longer delay.
- Everything `PHN-1a`/`PHN-1b`/`PHN-1c` carried to PR 6 is unchanged and unclaimed by this PR: seek
  repositions; `Time` advances; pausing a TTS source does not report completion; `./data/gvmedia` is
  writable under the service account; the row's own settling check (duck, mute, volume, Cast).
- ⚠ **A `MediaNotFound` during UAT is as likely to be the GV blackout as a bad id** (`PHN-1c` C-22).

**To the owner — one acceptance and one knob:**

1. **A doorbell will stop a voicemail mid-play.** That is ADR §6.1's intent and it is now real. If it
   proves annoying in the room, the fix is one key: `GvMedia:PreemptAtPriority` → 9 or 10 turns
   preemption off for everything that does not name an explicit rank, which on this box is everything.
   ⚠ **But read C-43 first** — that direction disables the feature while leaving it looking intact, so
   the honest way to turn it off is to say so, not to raise the number quietly.
2. **Attended playback can now fail because something else was already talking.** Until PR 6 renders it,
   a tap during an announcement looks like a dead button.

---

## 6. The `BUILDER_QUEUE` row — SHIPPED, and deliberately not reproduced here

⚠ **This section used to hold a ready-written `| PHN-1d | … |` row for the owner to paste.** It existed
because a Builder cycle owned `docs/BUILDER_QUEUE.md` while this plan was written (`PHN-1c` on
`feat/phn-1c-event-playback-service`) and a concurrent edit would have conflicted.

✅ **That is discharged. The row was appended to [`docs/BUILDER_QUEUE.md`](../../docs/BUILDER_QUEUE.md)
§ Queue on 2026-09-04, after `PHN-1c` merged as [#556](https://github.com/mmackelprang/RTest/pull/556),
and the copy that lived here is deleted rather than kept in sync.** The queue is the single copy on
purpose: a row that exists in two files is a row that will disagree with itself, and the *Depends on*
column has already changed once — `PHN-1c` was in flight when this plan was written and is merged now,
so the row says **✅ MET, claimable now** where the draft said *"— merged."*

Three things the row carries that changed after this section was drafted, recorded here so the diff is
not mysterious:

1. **The dependency is closed.** `PHN-1c` merged at `b77ffe12`. §0.3's re-check list can now be run
   against merged `main` for real, which is Builder's first action.
2. **C-46 has its D-number.** It is punch-list **`D28`** (§7, *Closed 2026-09-04 by the owner*). The
   draft asked for a D-number and guessed `D27`; `D27` was already taken by the
   `prefers-reduced-motion` decision of 2026-09-02.
3. **The `INTEGRATIONS.md` citation is by content, not by line** — see C-34 as amended above.
