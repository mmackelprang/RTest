# PLAN — `UI-14` · The null that must arrive, and the gate that only looks like one

**Row:** [`docs/queue/UI-14.md`](../../docs/queue/UI-14.md) · **Precedent:** [`UI-12`](UI-12-the-null-that-cannot-arrive.md), [ADR-033](../DECISION-LOG.md)
**Written:** 2026-09-09 (Planner) · **Scope:** `src/Radio.Web` + `tests/Radio.Web.Tests` + docs. No hardware, no audio path.

---

## 0. Read this before Task 1

### 0.1 What this row is actually for, in one paragraph

`UI-12` shipped a shared `AcceptPayload<T>` helper **above** the fan-out that rejects a null on the
four events declared `Func<T, Task>`, and it shipped an `internal On…MessageAsync` seam for each so a
test could reach the guard at all. ADR-033 states the rule in two halves. **Only the first half was
gated.** The second half — *an event declared `Func<T?, Task>` treats null as data and must pass it
through* — has no test anywhere in the repository. `UI-14` builds the seam and the assertions for the
second half.

⭐ **The reason this is worth a PR rather than a comment: the forbidden refactor is currently GREEN.**

### 0.2 ⛔ `C-501` — THE TWO FORMS OF THE MISTAKE, AND ONLY ONE OF THEM IS CAUGHT

`UI-12` §0.5 forbids putting the null check inside `NotifyAsync<T>`, because `T` is erased there and
the method cannot tell a contract violation from data. That prohibition is fenced by a long remark on
`AcceptPayload`. **A comment is not a gate.** There are two ways to violate it and they behave
completely differently under the existing suite:

| | The refactor | What the suite does today |
|---|---|---|
| **NAIVE** | Delete the four `AcceptPayload` calls; add `if (arg is null) return;` to `NotifyAsync<T>` | ⛔ **fails 4** — `NotifyAsync<T>` cannot name the event, so the four `Assert.Contains(… Message.Contains("<EventName>"))` assertions in `AudioStateHubServiceNullPayloadTests` break |
| ⭐ **REALISTIC** | **Keep** `AcceptPayload` and **also** add a "defensive" `if (arg is null) return;` to `NotifyAsync<T>` | ⭐ **GREEN — the entire `Radio.Web.Tests` assembly and the rest of the solution** — while silently dropping every `NowPlayingChanged(null)`, `VolumeChanged(null)` and `EventPlaybackChanged(null)` |

⚠ **The realistic form is the one an engineer actually writes.** Nobody deletes a helper that four
call sites depend on; a defensive null check added to a fan-out looks like tidying. It reads as
belt-and-braces and it is a silent data-loss bug.

⛔ **So the existing gate looks real and is not.** A plan whose mutation matrix only kills the naive
form has not done this row's job. §4 states, per mutation, **which form it kills**.

### 0.3 ⚠ `C-502` — do NOT design the test through `HubEventFire`

`HubEventFire.FireAsync` (`HubEventFire.cs:54-61`) reflects the compiler-generated backing field
(`:91-92`) and awaits the subscribers **directly**. That is downstream of `AcceptPayload`, downstream
of the `On<T>` handler body, and downstream of `NotifyAsync` — it does not execute one line of
`AudioStateHubService`'s dispatch code. **A null-payload test written through it passes whether the
pass-through works, is guarded, or is inverted.**

That is not a hypothesis. `NowPlayingDockTests.Dock_NullNowPlayingDto_ClearsState` (fires null at
`:248`) and `SleepTests.Sleep_NullNowPlayingDto_ClearsTrackBlock` (fires null at `:309`) are the only
two tests in the repository that fire a null on any of the three events, and **both go through
`HubEventFire`.** They are correct tests of the *components* and prove nothing about the hub client.

⭐ **Building the seam that can observe pass-through is the substance of this row**, exactly as it was
for `UI-12`. §3 is not scaffolding for §4; it is the work.

⚠ **A correction to the row while we are here.** `UI-14` says *"The only tests that fire a null on
those events — `NowPlayingDockTests.cs:248`, `SleepTests.cs:309`"*. Both fire **`NowPlayingChanged`**.
Grepped across `tests/`: **nothing anywhere fires `VolumeChanged(null)` or
`EventPlaybackChanged(null)`, through `HubEventFire` or by any other route.** Two of the three events
have no null coverage of *any* kind, not merely no coverage above the fan-out.

### 0.4 ⛔ `C-503` — THE ROW'S OWN DISCRIMINATOR IS OVERSTATED. Its conclusion survives.

The row's table contrasts the four covered events (*"unobservable in production — no producer can
send null"*) with the three uncovered ones (*"⭐ user-visible"*). **Read together, that says a producer
CAN send a null on the three. None can.** Enumerated the same way `UI-12` §0.2 enumerated the other
four — by mechanism, not by grep-and-hope:

- Every one of these three events is a `public event` on a **non-partial** class
  (`AudioStateHubService.cs:34`), so the language guarantees every raise site is in that one file.
- Grepping the string literals `"NowPlayingChanged"`, `"VolumeChanged"`, `"EventPlaybackChanged"`
  across all of `src/` returns **exactly one server-side sender each**, all in
  `Radio.API/Services/AudioStateUpdateService.cs`:

| Event | Sender | Payload expression | Can it be null? |
|---|---|---|---|
| `NowPlayingChanged` | `:281` | `currentNowPlaying`, from `BuildNowPlayingDto` (`:698`) | **No** — the method's return type is non-nullable `NowPlayingDto` and its single `return` path is a `new NowPlayingDto { … }` at `:700` |
| `VolumeChanged` | `:504` | `currentVolume` | **No** — `new VolumeDto { … }` at `:493` |
| `EventPlaybackChanged` | `:1087` | inline `new { … }` | **No** |

- `AudioStateHub` (`src/Radio.API/Hubs/AudioStateHub.cs`) contains **no `SendAsync` and no `Clients.`
  reference at all** — there is no client-callable broadcast.

⭐ **So both families are identical in reachability and differ only in DIRECTION OF FAILURE.** A null
on any of the seven payload-carrying reference-type events is reachable only from the untyped wire —
which is precisely ADR-033's stated justification: *"what makes the null unreachable today is a
property of `radio-api`'s current source, not a property the type system enforces."* That argument is
symmetric. It justifies the pass-through gate exactly as much as it justified the rejection gate.

**What actually differs, and it is worth stating correctly because it is the real argument for this
row:**

| | Correct behaviour | The regression | Worst case if a null ever arrives |
|---|---|---|---|
| The four (`UI-12`) | reject + log | *stops* rejecting | one swallowed NRE + a three-surface state wipe (`UI-12` `CORRECTION`) |
| The three (`UI-14`) | pass through | *starts* dropping | ⭐ **permanent** — the dock and the sleep screen strand on the previous track with no event that can ever clear them |

⭐ **The distinction that carries the row is PERMANENCE, not reachability.** A wrongly-rejected null is
a state change that never happens, and nothing later re-sends it: `AudioStateUpdateService` guards
every broadcast behind `HasNowPlayingChanged(_lastNowPlaying, currentNowPlaying)` and friends, so once
`_lastNowPlaying` records the null the *next* real track produces a change event but the intervening
"nothing is playing" is gone for good. `UI-12`'s failure repaints wrongly once; this one strands.

⛔ **Do not restate the row's "unobservable vs user-visible" framing in the PR body or in a comment.**
It is the same shape as `UI-12`'s own false premise — right conclusion, wrong reason — and
`CLAUDE.md` § *Pre-Merge Review* item 4 is about exactly this: a plan that inherits a reason it did
not check defends the right change with an argument that collapses.

### 0.5 The per-event contract, verified individually. ⚠ The three are NOT equivalent.

`UI-12`'s first draft asserted the three behave alike and that was wrong. Each was re-derived here
from the subscribers, not from the row.

#### `NowPlayingChanged(null)` — ⭐ demonstrated, and it means three different things to five subscribers

Five production types subscribe to the hub event directly (`NowPlayingPanel.razor:555`,
`NowPlayingDock.razor:117`, `AudioStateStore.cs:91`, `MainLayout.razor:382`, `Sleep.razor:221` —
note `Sleep.razor:13` injects the hub under the alias `AudioState`, so it is a **direct** hub
subscriber and not, as the name suggests, a store subscriber):

| Subscriber | What a null does |
|---|---|
| `NowPlayingDock.OnNowPlayingChanged` (`:148-162`) | ⭐ `ClearDockState()` — resets to "No Track Playing", em-dash subtitle, 0:00 / —. **Demonstrated** by `NowPlayingDockTests.Dock_NullNowPlayingDto_ClearsState` |
| `Sleep.OnNowPlayingChanged` (`:443-450`) | ⭐ `ApplyNowPlaying(null)` — the track block and art disappear. **Demonstrated** by `SleepTests.Sleep_NullNowPlayingDto_ClearsTrackBlock`, whose own comment names the cost: stale metadata *"for hours"* |
| `NowPlayingPanel.OnNowPlayingChanged` (`:593-606`) | ⚠ **a REST re-fetch trigger** (`RefreshPlaybackStateAsync()`), not a clear |
| `AudioStateStore.OnHubNowPlayingChanged` (`:178-186`) | ⚠ **keeps** the cached `NowPlaying` (the assignment is guarded) and notifies its own subscribers anyway |
| `MainLayout.OnNowPlayingChanged` (`:1173-1176`) | discards the payload, re-renders |

⚠ **The row says the cost is "the dock and panel". It is two clears plus one suppressed re-fetch**,
and the sleep screen is arguably the worse of the two clears — a kiosk parked on `/sleep` overnight
would show the last track played until someone touched it.

#### `VolumeChanged(null)` — ⛔ NOT "an absent snapshot"

Three subscribers (`NowPlayingPanel.razor:557`, `AudioStateStore.cs:92`, `MainLayout.razor:386`):

| Subscriber | What a null does |
|---|---|
| `NowPlayingPanel.OnVolumeChangedEvent` (`:617-631`) | ⭐ **a REST re-fetch trigger** — `RefreshPlaybackStateAsync()` in the `else` branch |
| `AudioStateStore.OnHubVolumeChanged` (`:204-213`) | **discards** the payload — `Volume`/`IsMuted` keep their previous values — and calls `NotifyAsync(VolumeChanged)` regardless |
| `MainLayout.OnVolumeChanged` (`:1544-1553`) | `if (dto is null || …) return;` — early-return, mute chip unchanged |

⚠ **Exactly one of the three does real work on a null, and it is the one that goes to the network.**
State the cost at that size and no larger: dropping it strands the panel's volume/mute readout until
the next non-null broadcast. The row is correct on this event; the AcceptPayload remark's earlier
revision (*"VolumeChanged and EventPlaybackChanged likewise"*) was not.

#### `EventPlaybackChanged(null)` — ⚠ UNEVIDENCED, and it stays that way

⛔ **No producer sends one** (§0.4 table). Do not invent a contract for it. What is true and what is
not:

- **True, by declaration:** it is `Func<EventPlaybackSnapshotDto?, Task>` (`:90`), so ADR-033 says it
  passes through. That alone is sufficient reason to gate it — the gate is that declaration and
  dispatch cannot silently disagree.
- **True, by code:** its **one** hub subscriber, `AudioStateStore.OnHubEventPlaybackChanged`
  (`:327-332`), does **two** things — `EventPlayback = dto` (a null clears the cached snapshot) **and**
  `Volatile.Write(ref _eventPlaybackBroadcastSeen, 1)`.
  ⭐ **The second is the non-obvious half and the row does not mention it.** That flag is what makes a
  broadcast beat the one-shot REST seed (`AudioStateStore.cs:334-353`, the ENC-12 broadcast-wins
  ordering). Dropping a null here would not merely fail to clear a snapshot — it would leave the flag
  at 0 and let a **later, staler** seed win.
- ⛔ **Not true, and must not be written:** that a null is expected, observed, or has ever occurred.

### 0.6 ⚠ What must not regress

1. The four `AcceptPayload` call sites and their eight `UI-12` tests. `UI-14` adds a gate beside
   `UI-12`'s; it changes nothing about it.
2. `AsyncEventFanOutLintTests` (`tests/Radio.Core.Tests`). It scans `src/` for an event name being
   invoked. The new seams keep the sanctioned form — the event name appears in front of a `,` in
   `NotifyAsync(NowPlayingChanged, dto)`, never in front of a `(` — so the rule stays quiet **by
   construction, not by luck** (that file's own `Violations` remark at `:232-234` makes the argument).
   Run it explicitly anyway; this row edits the file it polices.
3. `NowPlayingDockTests` and `SleepTests`' existing null tests. They go through `HubEventFire` and are
   unaffected by anything in Task 1 — which is the point of §0.3, and also a cheap sanity check that
   Task 1 did not change the events themselves.
4. **Log volume.** `src/Radio.Web/appsettings.json` puts `MinimumLevel.Default` at `Information` with
   **no `restrictedToMinimumLevel`** on the Console sink, so under systemd every `Information`-and-above
   line from this process lands in `journalctl -u radio-web`, on a box where log volume correlates with
   audible audio distortion (`CLAUDE.md` § *Deployment*). `NowPlayingChanged` fires on every metadata
   change. ⛔ **No new `Information` or `Warning` line may be added to these three seams.** `T7` gates it.

### 0.7 Auto-merge

**Yes, on green gates** — `Radio.Web` + tests + docs, no hardware, no live-audio path, no config, no
migration.

⚠ **But narrowed, and the PR body must say so: the change is not demonstrable on the appliance.** A
null cannot arrive on any of the three, so before and after are byte-for-byte identical on every
reachable path. §6 says what UAT *can* honestly prove and what it cannot. **A UAT report claiming it
demonstrated pass-through would be wrong**, in exactly the way `UI-12`'s dossier records.

---

## 1. Task 0 — measure the hole BEFORE changing anything

⭐ **This is the RED step, and it is available here in a way it was not for `UI-12`.** `UI-12` could
only mutate after the fact. `UI-14`'s central claim is a statement about the tree *as it stands*, so
it can be measured first and the same mutation re-run afterwards. Do this before touching Task 1.

**1a.** Apply the **realistic** forbidden refactor to the current tree — `AcceptPayload` untouched,
two lines added to `NotifyAsync<T>` (`AudioStateHubService.cs:704-709`):

```csharp
  private async Task NotifyAsync<T>(Func<T, Task>? handler, T arg)
  {
    if (handler == null)
    {
      return;
    }

    // ⛔ TASK 0 MUTATION — NOT FOR COMMIT. Remove before Task 1.
    if (arg is null)
    {
      return;
    }

    foreach (var subscriber in handler.GetInvocationList())
```

**1b.** Run the full assembly and **record the number**:

```bash
dotnet test tests/Radio.Web.Tests -c Release > /tmp/ui14-m0.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/ui14-m0.log
```

⭐ **Expected: 0 failed.** That is the row's premise, and it is now a measurement rather than a claim.
Put the actual per-project summary line in the PR body verbatim.

⚠ **The row states the assembly holds 1,172 tests. Re-derive that from your own run; do not quote the
row's figure.** A count copied forward is the shape `CLAUDE.md` warns about twice (the "~9
subscribers" and "THIRTEEN events" comments).

⚠ **If it is NOT green — if the mutation fails even one test — STOP and report.** The row's premise
is then false and the rest of this plan needs re-planning, not adapting.

**1c.** ⛔ **Remove the mutation with an `Edit` that deletes the five lines you added.** Do **not** use
`git checkout -- <path>`, `git restore`, or `git stash drop`; `CLAUDE.md` § *Commit before any
destructive revert* records that exact technique discarding real work on 2026-09-08, and it was reused
without re-establishing the precondition that made it safe.

**1d.** Confirm the tree is back to baseline before Task 1:

```bash
dotnet test tests/Radio.Web.Tests -c Release > /tmp/ui14-baseline.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!" /tmp/ui14-baseline.log
```

---

## 2. Task 1 — extract the three nullable handler bodies into `internal` seams

**File:** `src/Radio.Web/Services/Hub/AudioStateHubService.cs`

### 2a. Replace the three `On<T>` lambda registrations with method groups

At `:187-193`, replace:

```csharp
      // Server sends NowPlayingChanged with a NowPlayingDto payload —
      // deserialize and pass through so subscribers can use it directly.
      _hubConnection.On<NowPlayingDto?>("NowPlayingChanged", async (dto) =>
      {
        _logger.LogDebug("Received NowPlayingChanged event");
        await NotifyAsync(NowPlayingChanged, dto);
      });
```

with:

```csharp
      // Server sends NowPlayingChanged with a NowPlayingDto payload —
      // deserialize and pass through so subscribers can use it directly.
      // ⛔ A null here is DATA, not a contract violation (ADR-033). See OnNowPlayingMessageAsync.
      _hubConnection.On<NowPlayingDto?>("NowPlayingChanged", OnNowPlayingMessageAsync);
```

At `:215-221`, replace:

```csharp
      // Server sends VolumeChanged with a VolumeDto payload —
      // deserialize and pass through so subscribers can update directly.
      _hubConnection.On<VolumeDto?>("VolumeChanged", async (dto) =>
      {
        _logger.LogDebug("Received VolumeChanged event");
        await NotifyAsync(VolumeChanged, dto);
      });
```

with:

```csharp
      // Server sends VolumeChanged with a VolumeDto payload —
      // deserialize and pass through so subscribers can update directly.
      // ⛔ A null here is DATA, not a contract violation (ADR-033). See OnVolumeMessageAsync.
      _hubConnection.On<VolumeDto?>("VolumeChanged", OnVolumeMessageAsync);
```

At `:260-266`, replace:

```csharp
      // Server sends EventPlaybackChanged on every attended-playback transition (ADR-029 D6 §8.1).
      // Transitions only — there is no position tick, and §8.2 refuses one outright.
      _hubConnection.On<EventPlaybackSnapshotDto?>("EventPlaybackChanged", async (dto) =>
      {
        _logger.LogDebug("Received EventPlaybackChanged event");
        await NotifyAsync(EventPlaybackChanged, dto);
      });
```

with:

```csharp
      // Server sends EventPlaybackChanged on every attended-playback transition (ADR-029 D6 §8.1).
      // Transitions only — there is no position tick, and §8.2 refuses one outright.
      // ⛔ A null here is DATA, not a contract violation (ADR-033). See OnEventPlaybackMessageAsync.
      _hubConnection.On<EventPlaybackSnapshotDto?>("EventPlaybackChanged", OnEventPlaybackMessageAsync);
```

⚠ **The type arguments are already nullable and must stay that way.** Unlike `UI-12` Task 1, no
`On<T>` type argument changes here — these three were written nullable from the start.

### 2b. Add the three seam methods

Insert immediately after `OnEncoderHudMessageAsync` (which ends at `:634`), before the
`NotifyAsync(Func<Task>?)` remark block.

⚠ **Cite MEMBER NAMES, not line numbers, in these remarks.** This is a deliberate deviation from the
surrounding style. The file's existing comments carry `file:line` anchors and several have already
gone stale and been corrected in place; a member name survives every edit above it. Where a line
number genuinely helps, re-verify it in the working tree at implementation time rather than copying
one from this plan.

```csharp
  // ---------------------------------------------------------------------------------------------
  // The three events where NULL IS DATA. ⛔ None of these may grow an AcceptPayload call, and
  // NotifyAsync<T> below must never grow a null check — that is the `UI-12` §0.5 / ADR-033 defect,
  // and `UI-14` exists because the realistic form of it passed the entire suite until these seams
  // made it observable.
  // ---------------------------------------------------------------------------------------------

  /// <summary>Applies one "NowPlayingChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// ⛔ A NULL PAYLOAD IS DATA AND MUST REACH EVERY SUBSCRIBER. The event is declared
  /// <c>Func&lt;NowPlayingDto?, Task&gt;</c>, and under ADR-033 the declaration is the specification.
  ///
  /// ⚠ It does not mean one thing. FIVE production types subscribe to this event and a null means
  /// three different things to them:
  /// <list type="bullet">
  /// <item><c>NowPlayingDock.OnNowPlayingChanged</c> calls <c>ClearDockState()</c> — the dock resets
  /// to "No Track Playing". Demonstrated, not assumed:
  /// <c>NowPlayingDockTests.Dock_NullNowPlayingDto_ClearsState</c>.</item>
  /// <item><c>Sleep.OnNowPlayingChanged</c> calls <c>ApplyNowPlaying(null)</c> — the sleep screen's
  /// track block and art disappear. <c>SleepTests.Sleep_NullNowPlayingDto_ClearsTrackBlock</c>, whose
  /// own comment gives the cost: stale metadata "for hours". (Sleep.razor injects THIS service under
  /// the alias <c>AudioState</c>; it is a direct subscriber, not an AudioStateStore one.)</item>
  /// <item><c>NowPlayingPanel.OnNowPlayingChanged</c> treats it as a REST RE-FETCH TRIGGER
  /// (<c>RefreshPlaybackStateAsync</c>) — not as a clear.</item>
  /// <item><c>AudioStateStore.OnHubNowPlayingChanged</c> KEEPS its cached NowPlaying and notifies
  /// anyway; <c>MainLayout.OnNowPlayingChanged</c> discards the payload and re-renders.</item>
  /// </list>
  ///
  /// ⚠ A dropped null is PERMANENT, which is what separates this from the guarded four. Nothing
  /// re-sends it: AudioStateUpdateService only broadcasts on a CHANGE, so once its own
  /// <c>_lastNowPlaying</c> records the silence, the intervening "nothing is playing" is gone and the
  /// dock and sleep screen strand on the previous track until the next real track arrives.
  ///
  /// 📌 No producer can send one today — <c>AudioStateUpdateService.BuildNowPlayingDto</c> returns a
  /// non-nullable type and always builds a <c>new NowPlayingDto { … }</c>. That is a property of the
  /// current server source, not of the type system: the payload arrives from a JSON deserializer
  /// across a process boundary where nullable annotations are erased. Same argument as
  /// <see cref="AcceptPayload{T}"/>'s, pointed the other way.
  /// </remarks>
  internal async Task OnNowPlayingMessageAsync(NowPlayingDto? dto)
  {
    _logger.LogDebug("Received NowPlayingChanged event");
    await NotifyAsync(NowPlayingChanged, dto);
  }

  /// <summary>Applies one "VolumeChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// ⛔ A NULL PAYLOAD IS DATA AND MUST REACH EVERY SUBSCRIBER — declared
  /// <c>Func&lt;VolumeDto?, Task&gt;</c> (ADR-033).
  ///
  /// ⚠ IT IS NOT "AN ABSENT SNAPSHOT", and an earlier revision of the AcceptPayload remark said the
  /// three nullable events behaved alike. They do not. Of the three subscribers:
  /// <list type="bullet">
  /// <item><c>NowPlayingPanel.OnVolumeChangedEvent</c> — a REST RE-FETCH TRIGGER
  /// (<c>RefreshPlaybackStateAsync</c>). This is the only subscriber that does real work on a null,
  /// and it is the one that goes to the network.</item>
  /// <item><c>AudioStateStore.OnHubVolumeChanged</c> — DISCARDS the payload, keeps Volume/IsMuted, and
  /// notifies its own subscribers regardless.</item>
  /// <item><c>MainLayout.OnVolumeChanged</c> — early-returns; the mute chip is unchanged.</item>
  /// </list>
  /// So the cost of dropping one is precisely: the panel's volume/mute readout stays stale until the
  /// next non-null broadcast. Stated at that size on purpose.
  ///
  /// 📌 No producer can send one — the sole sender builds a <c>new VolumeDto { … }</c>.
  /// </remarks>
  internal async Task OnVolumeMessageAsync(VolumeDto? dto)
  {
    _logger.LogDebug("Received VolumeChanged event");
    await NotifyAsync(VolumeChanged, dto);
  }

  /// <summary>Applies one "EventPlaybackChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// ⛔ A NULL PAYLOAD IS DATA BY DECLARATION — <c>Func&lt;EventPlaybackSnapshotDto?, Task&gt;</c> —
  /// ⚠ AND UNEVIDENCED IN PRACTICE. No producer sends one: the sole sender,
  /// <c>AudioStateUpdateService.OnEventPlaybackChanged</c>, always builds an anonymous
  /// <c>new { … }</c>. ⛔ Do NOT write a contract for it that the code does not have. This seam exists
  /// so the declaration and the dispatch cannot silently disagree, NOT because a null is expected.
  ///
  /// 📌 What one WOULD do, since it is cheap to state and expensive to re-derive. There is ONE
  /// subscriber — <c>AudioStateStore.OnHubEventPlaybackChanged</c> — and it does TWO things: it
  /// assigns <c>EventPlayback = dto</c> (a null clears the cached snapshot) AND it sets
  /// <c>_eventPlaybackBroadcastSeen</c>. The second is the non-obvious half: that flag is what makes a
  /// broadcast beat the one-shot REST seed (the ENC-12 broadcast-wins ordering that
  /// <c>SeedEventPlaybackAsync</c> documents). Dropping a null would therefore not merely fail to
  /// clear a snapshot — it would leave a later, staler seed free to win.
  /// </remarks>
  internal async Task OnEventPlaybackMessageAsync(EventPlaybackSnapshotDto? dto)
  {
    _logger.LogDebug("Received EventPlaybackChanged event");
    await NotifyAsync(EventPlaybackChanged, dto);
  }
```

⚠ **The three `LogDebug` lines are carried across verbatim.** Behaviour preservation — Task 1 must
change *where* the code lives and nothing else. See §0.6 item 4 for why a level change here is not a
free call.

---

## 3. Task 2 — the tests

**New file:** `tests/Radio.Web.Tests/Services/AudioStateHubServicePassThroughTests.cs`

⚠ **A new file rather than an addition to `AudioStateHubServiceNullPayloadTests`.** That file is
titled and scoped to `UI-12`'s rejection contract; the pass-through contract is its mirror image and
mixing them makes the mutation matrix in §4 harder to read per-file. Keeping them apart also means
`M2` in §4 can be reported as *"7 fail, split 4 / 3 across two files"*, which is itself informative.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The hub client's null-PASS-THROUGH contract — the half of ADR-033 that `UI-12` did not gate
/// (queue row `UI-14`).
/// </summary>
/// <remarks>
/// ⚠⚠ THESE DRIVE THE On&lt;T&gt; HANDLER BODY, NOT THE EVENT, AND THAT IS THE ENTIRE POINT.
/// <c>HubEventFire</c> reflects the compiler-generated backing field and awaits subscribers directly
/// (HubEventFire.cs:54-61), which is DOWNSTREAM of the fan-out — a null test written that way passes
/// whether the null is passed through, dropped, or replaced. The two tests in this repository that
/// fire a null on a hub event (NowPlayingDockTests.Dock_NullNowPlayingDto_ClearsState and
/// SleepTests.Sleep_NullNowPlayingDto_ClearsTrackBlock) both do exactly that; they are correct tests
/// of those COMPONENTS and prove nothing about this service.
///
/// ⭐ THE MISTAKE THESE EXIST TO CATCH IS GREEN WITHOUT THEM. Keeping AcceptPayload and ALSO adding a
/// "defensive" `if (arg is null) return;` to AudioStateHubService.NotifyAsync&lt;T&gt; passed the
/// entire Radio.Web.Tests assembly before this file existed, while silently dropping every
/// NowPlayingChanged(null). Measured on the tree, as `UI-14` Task 0. The naive form of the same
/// refactor — deleting AcceptPayload — fails four tests in AudioStateHubServiceNullPayloadTests, so
/// the pre-existing gate looked real and was not.
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK (`CLAUDE.md` § Test Timing). Every seam is awaited to
/// completion before anything is asserted, so each observation is a fact about control flow.
///
/// 📌 What these do NOT prove: that a null can ever arrive. It cannot — each of the three events has
/// exactly one server-side sender and none can produce one (`UI-14` §0.4). These pin the BOUNDARY's
/// behaviour if one ever does, which is the same standing ADR-033 gives the rejection tests.
/// </remarks>
public class AudioStateHubServicePassThroughTests
{
  private static AudioStateHubService NewHub(List<(LogLevel Level, string Message)> sink) =>
    new(
      new CapturingLogger<AudioStateHubService>(sink),
      new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());

  private static EventPlaybackSnapshotDto Snapshot() =>
    new(
      Id: "evt-1",
      Kind: "Voicemail",
      Label: "Voicemail",
      State: "Playing",
      Duration: TimeSpan.FromSeconds(30),
      PositionAtBroadcast: TimeSpan.Zero,
      BroadcastAtUtc: DateTimeOffset.UnixEpoch,
      FailureReason: null);

  // --- T1/T2/T3 — THE HEADLINE: a null MUST reach every subscriber, AS a null -------------------

  /// <summary>
  /// ⭐ THE DISCRIMINATING TEST. Add `if (arg is null) return;` to NotifyAsync&lt;T&gt; — with or
  /// without keeping AcceptPayload — and this goes RED. Before `UI-14` no test in the repository
  /// could.
  /// </summary>
  /// <remarks>
  /// ⚠ TWO subscribers, and the `seen` slots are SEEDED WITH NON-NULL SENTINELS. Both are deliberate.
  /// Two subscribers means a regression to a direct `Event.Invoke(dto)` — which runs every handler but
  /// returns only the LAST one's Task (`UI-7`) — is still observed here. The sentinels make
  /// Assert.Null non-vacuous: without them a handler that never ran and a handler that ran with null
  /// leave the same value behind, and the assertion would pass against a dropped payload.
  /// </remarks>
  [Fact]
  public async Task NullNowPlayingPayloadReachesEverySubscriberAsNull()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();
    var seen = new NowPlayingDto?[] { new(), new() };

    hub.NowPlayingChanged += d => { order.Add(1); seen[0] = d; return Task.CompletedTask; };
    hub.NowPlayingChanged += d => { order.Add(2); seen[1] = d; return Task.CompletedTask; };

    await hub.OnNowPlayingMessageAsync(null);

    Assert.Equal([1, 2], order);
    Assert.All(seen, s => Assert.Null(s));
  }

  [Fact]
  public async Task NullVolumePayloadReachesEverySubscriberAsNull()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();
    var seen = new VolumeDto?[] { new(0.5f, false), new(0.5f, false) };

    hub.VolumeChanged += d => { order.Add(1); seen[0] = d; return Task.CompletedTask; };
    hub.VolumeChanged += d => { order.Add(2); seen[1] = d; return Task.CompletedTask; };

    await hub.OnVolumeMessageAsync(null);

    Assert.Equal([1, 2], order);
    Assert.All(seen, s => Assert.Null(s));
  }

  /// <summary>
  /// ⚠ UNEVIDENCED IN PRODUCTION and this test says so rather than implying otherwise. No producer
  /// sends EventPlaybackChanged(null) — AudioStateUpdateService.OnEventPlaybackChanged always builds
  /// an anonymous `new { … }`. What is pinned here is that the DECLARATION and the DISPATCH agree, so
  /// a future producer that does send one is not silently discarded at the boundary.
  /// </summary>
  [Fact]
  public async Task NullEventPlaybackPayloadReachesEverySubscriberAsNull()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();
    var seen = new EventPlaybackSnapshotDto?[] { Snapshot(), Snapshot() };

    hub.EventPlaybackChanged += d => { order.Add(1); seen[0] = d; return Task.CompletedTask; };
    hub.EventPlaybackChanged += d => { order.Add(2); seen[1] = d; return Task.CompletedTask; };

    await hub.OnEventPlaybackMessageAsync(null);

    Assert.Equal([1, 2], order);
    Assert.All(seen, s => Assert.Null(s));
  }

  // --- T4/T5/T6 — the regression half: the happy path must survive the extraction ---------------

  /// <summary>
  /// ⚠ Task 1 rewrites the ONLY production path that dispatches these three events, so the happy path
  /// needs its own assertion. T1-T3 cover the null; if the extraction broke dispatch for a NON-null
  /// payload as well, that is a total outage of the now-playing dock, the volume readout and the
  /// attended-playback state — and nothing in T1-T3 would say so, because they never send one. These
  /// three are the other side. Same argument as
  /// AudioStateHubServiceNullPayloadTests.NonNullRadioStatePayloadIsDispatchedUnchanged's.
  /// </summary>
  [Fact]
  public async Task NonNullNowPlayingPayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = new NowPlayingDto { Title = "Hey Jude", Artist = "The Beatles" };
    NowPlayingDto? seen = null;

    hub.NowPlayingChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnNowPlayingMessageAsync(dto);

    Assert.Same(dto, seen);
  }

  [Fact]
  public async Task NonNullVolumePayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = new VolumeDto(0.42f, true);
    VolumeDto? seen = null;

    hub.VolumeChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnVolumeMessageAsync(dto);

    Assert.Same(dto, seen);
  }

  [Fact]
  public async Task NonNullEventPlaybackPayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = Snapshot();
    EventPlaybackSnapshotDto? seen = null;

    hub.EventPlaybackChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnEventPlaybackMessageAsync(dto);

    Assert.Same(dto, seen);
  }

  // --- T7 — a null on a nullable event is ORDINARY, and must stay quiet -------------------------

  /// <summary>
  /// ⭐ A null here is DATA, so it must not be reported as a fault. This is the mirror of
  /// AudioStateHubServiceNullPayloadTests' "it is not silent" assertion, and it is the one gate a
  /// dispatch assertion cannot provide: a seam could pass the null through correctly AND log a
  /// Warning, which looks harmless and is not.
  /// </summary>
  /// <remarks>
  /// ⚠ WHY THIS ASSERTS "NO WARNING AT ALL" WHERE UI-12'S EQUIVALENT WAS NARROWED TO THE REJECTION
  /// WORDING. That narrowing was right for a test that runs a real dispatch with subscribers attached,
  /// where an unrelated legitimate warning could appear later and fail the test for the wrong reason.
  /// Here no subscribers are attached and each seam's whole body is one LogDebug plus one NotifyAsync
  /// over an empty invocation list — there is no legitimate Warning these three lines can emit. If a
  /// future edit adds one, this test failing IS the intended conversation, not a false positive:
  /// src/Radio.Web/appsettings.json leaves the Console sink unrestricted, so under systemd every
  /// Warning from this process reaches `journalctl -u radio-web`, and NowPlayingChanged fires on every
  /// metadata change (`CLAUDE.md` § Deployment — log volume correlates with audible distortion).
  /// </remarks>
  [Fact]
  public async Task ANullOnANullableEventIsNotReportedAsAFault()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);

    await hub.OnNowPlayingMessageAsync(null);
    await hub.OnVolumeMessageAsync(null);
    await hub.OnEventPlaybackMessageAsync(null);

    Assert.DoesNotContain(sink, e => e.Level >= LogLevel.Information);
  }
}
```

⚠ **Builder: re-derive `EventPlaybackSnapshotDto`'s and `VolumeDto`'s shapes from
`src/Radio.Web/Models/ApiModels.cs` rather than trusting this file.** As planned they are
`record EventPlaybackSnapshotDto(string Id, string? Kind, string? Label, string? State, TimeSpan?
Duration, TimeSpan PositionAtBroadcast, DateTimeOffset BroadcastAtUtc, string? FailureReason)` and
`record VolumeDto(float Volume, bool IsMuted)` — note the Web `VolumeDto` has **no `Balance`**, unlike
the API-side one. `NowPlayingDto` is a class with all-optional initializers.

---

## 4. Task 3 — the mutation matrix. ⭐ It must be RED against the REALISTIC form.

**RED cannot be shown by writing the test first**, because §3's tests call methods §2 creates. Same
constraint `UI-12` hit. What `UI-14` can do that `UI-12` could not is measure the hole **before** the
change (Task 0) and re-measure the identical mutation **after** — so `M0` → `M1` is a genuine
before/after RED demonstration rather than a post-hoc mutation.

Run each against the committed Tasks 1-2 code, one at a time, reverting with an `Edit` (⛔ never
`git checkout --`; see Task 0 step 1c). Record actual counts, not expected ones.

| # | Mutation | Expected | ⭐ **Which form it kills** |
|---|---|---|---|
| **M0** | *(Task 0, pre-change)* `if (arg is null) return;` in `NotifyAsync<T>`, `AcceptPayload` kept | ⭐ **GREEN — whole assembly** | ⛔ **nothing.** This is the hole, measured. It is the row's premise and the reason `UI-14` exists |
| **M1** | the identical mutation, post-change | **RED: T1, T2, T3. Nothing else.** | ⭐ **the REALISTIC form** — the one an engineer actually writes. `M0` → `M1` is the RED step |
| **M2** | delete the four `AcceptPayload(…)` calls **and** add `if (arg is null) return;` to `NotifyAsync<T>` | **RED: 7** — T1/T2/T3 here, plus the four event-name log assertions in `AudioStateHubServiceNullPayloadTests` | **the NAIVE form.** Also shows the two gates are independent: `UI-12`'s catch the rejection half, `UI-14`'s the pass-through half |
| **M3a** | `if (dto is null) return;` at the top of `OnNowPlayingMessageAsync` only | **RED: T1 only** | **the per-event form** — proves T1/T2/T3 are three independent gates and not one test carrying three events |
| **M3b** | same, in `OnVolumeMessageAsync` only | **RED: T2 only** | as M3a |
| **M3c** | same, in `OnEventPlaybackMessageAsync` only | **RED: T3 only** | as M3a |
| **M4** | in `OnVolumeMessageAsync`, dispatch `dto ?? new VolumeDto(0f, false)` | **RED: T2 — and specifically its `Assert.All(seen, …Null)`, while `Assert.Equal([1,2], order)` still PASSES** | **the transform-instead-of-drop form.** This is what justifies T1-T3 asserting on the *value* as well as on arrival; a "helpful default" is a silent data change that a `received == true` check cannot see |
| **M5** | `return;` unconditionally at the top of `OnNowPlayingMessageAsync` | **RED: T1 and T4** | **the "the extraction broke the only production dispatch path" form.** Task 1 rewrites that path; `UI-12`'s `NonNullRadioStatePayloadIsDispatchedUnchanged` exists for the same reason |
| **M6** | add `_logger.LogWarning("dropped a null payload", …)` to `OnNowPlayingMessageAsync`, still passing through | **RED: T7 only** | **the audible-but-correct form** — a log-volume regression on a 1-per-metadata-change path that no dispatch assertion can see (§0.6 item 4) |
| ⭐ **M7 — THE CONTROL** | replace the three seam bodies with a shared `PassThroughAsync<T>` helper above the fan-out — see below | ⭐ **GREEN, 100%** | ⛔ **nothing, and it must not.** It varies **shape only** |

### M7 in full — and why it is the control `UI-12`'s `M4` failed to be

```csharp
  /// <summary>M7 CONTROL — NOT FOR COMMIT.</summary>
  private async Task PassThroughAsync<T>(Func<T?, Task>? handler, T? dto) where T : class
  {
    await NotifyAsync(handler, dto);
  }

  internal async Task OnNowPlayingMessageAsync(NowPlayingDto? dto)
  {
    _logger.LogDebug("Received NowPlayingChanged event");
    await PassThroughAsync(NowPlayingChanged, dto);
  }
  // …the same for OnVolumeMessageAsync and OnEventPlaybackMessageAsync.
```

⭐ **`UI-12`'s lesson, applied.** Its `M4` restored the *pre-`UI-12`* inline guard and asserted nothing
must fail — but `UI-12` deliberately **added** behaviour (audible rejection), so pre-`UI-12` was not
behaviour-equivalent to post-`UI-12`, and it failed 3. **A control that cannot pass is not a control;
it is a second mutation.** ADR-033 records the correction.

`M7` holds the behaviour contract **completely** fixed — null reaches every subscriber unchanged,
non-null reaches every subscriber unchanged, nothing above Debug is logged — and varies only where
the code lives: one shared helper instead of three inline calls. It is also a refactor a future
engineer might genuinely attempt, which is what makes it meaningful rather than ceremonial: **the
tests must permit it.** If `M7` is red, the tests are pinning Task 1's shape and must be loosened
before this row ships.

⚠ **`M7` must not trip `AsyncEventFanOutLintTests`.** `PassThroughAsync(NowPlayingChanged, dto)` puts
the event name in front of a `,`, never a `(`, so the direct-call arm stays quiet by construction.
Run the lint under `M7` as well as at baseline — a control that silently disables a lint is not a
control either.

### What to record in the PR body

Per mutation: the **measured** failure count, the **names** of the failing tests, and the **form**
column above. `UI-12`'s corrected matrix is the format to copy. ⚠ **If any row's measured result
differs from the expected column, report the measurement and stop — do not adjust the expectation to
match.**

---

## 5. Task 4 — the census, so the gate does not go stale ⚠ one scope extension, stated

**New file:** `tests/Radio.Web.Tests/Services/AudioStateHubServiceEventDeclarationCensusTests.cs`

⚠ **This is the row's one scope extension and it is flagged as such.** The row scopes `UI-14` to
"mirror `UI-12` Task 1 for the three nullable events". Tasks 1-3 do exactly that. Task 4 answers the
question one level up — *who catches the eighth event?* — because `UI-14` exists precisely because
`UI-12` gated four of seven and nobody noticed the other three for a day. A gate that hard-codes
"these three" has the same shape as the gate it replaces.

⭐ **It is a tripwire, not a behaviour test, and its failure message says so and says what to do.**

```csharp
using System.Reflection;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Every reference-payload event on <see cref="AudioStateHubService"/> is classified by ADR-033, and
/// this census fails when a new one appears (queue row `UI-14`).
/// </summary>
/// <remarks>
/// ⚠ THIS IS A TRIPWIRE, NOT A PROOF. It asserts which events exist and how their payloads are
/// DECLARED. It cannot assert that each one behaves correctly — that is what
/// AudioStateHubServiceNullPayloadTests (reject) and AudioStateHubServicePassThroughTests
/// (pass through) are for. Its whole job is to stop an eighth payload event being added with neither.
///
/// ⭐ WHY IT EXISTS. `UI-12` gated the four events declared Func&lt;T, Task&gt; and left the three
/// declared Func&lt;T?, Task&gt; ungated. Both halves of ADR-033 are now gated — but only for the
/// seven events that existed on 2026-09-09, and nothing else would notice an eighth.
///
/// 📌 It reads the compiler-generated backing FIELD rather than the EventInfo, because that is the
/// same reflection route HubEventFire.InvocationListOf already depends on, so the two go blind
/// together rather than one of them silently. Converting any of these to explicit add/remove
/// accessors deletes the field (`UI-7` C-207) and fails this test loudly, which is correct.
/// </remarks>
public class AudioStateHubServiceEventDeclarationCensusTests
{
  private static readonly string[] NullPassesThrough =
  [
    nameof(AudioStateHubService.EventPlaybackChanged),
    nameof(AudioStateHubService.NowPlayingChanged),
    nameof(AudioStateHubService.VolumeChanged),
  ];

  private static readonly string[] NullIsRejected =
  [
    nameof(AudioStateHubService.EncoderConfigStatusChanged),
    nameof(AudioStateHubService.EncoderConnectionChanged),
    nameof(AudioStateHubService.EncoderHudChanged),
    nameof(AudioStateHubService.RadioStateChanged),
  ];

  [Fact]
  public void EveryReferencePayloadEventIsClassifiedByAdr033()
  {
    var type = typeof(AudioStateHubService);
    var context = new NullabilityInfoContext();

    var events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance)
      .Select(e => (e.Name, Field: type.GetField(e.Name, BindingFlags.NonPublic | BindingFlags.Instance)))
      .ToList();

    // Floors, so silence means something. There were 14 events when this was written; the class
    // remark on NotifyAsync<T> records the same number and has already been wrong once.
    Assert.True(
      events.Count >= 14,
      $"Only {events.Count} public events found on {type.Name} — there were 14 when this census was "
      + "written. Either events were deleted without updating this test, or GetEvents is looking at "
      + "the wrong type.");
    Assert.All(events, e => Assert.NotNull(e.Field));

    var payloadEvents = events
      .Select(e => (e.Name, Info: context.Create(e.Field!)))
      // Func<T, Task> has two type arguments; Func<Task> has one.
      .Where(e => e.Info.GenericTypeArguments.Length == 2)
      // SleepStateChanged is Func<bool, Task> — a value type cannot be null and ADR-033 does not
      // reach it.
      .Where(e => !e.Info.GenericTypeArguments[0].Type.IsValueType)
      .ToList();

    // ⭐ PROVE THE INSTRUMENT BEFORE TRUSTING ITS VERDICT. If NullabilityInfoContext reported
    // Unknown for everything, both expected sets below would come back empty and the equality
    // assertions would compare empty to non-empty — which fails, but for a reason nobody could
    // read. This says it in one line instead. (Idiom: VisualizerPanelTests.cs:233-236, and
    // AsyncEventFanOutLintTests' positive control.)
    Assert.NotEmpty(payloadEvents);
    Assert.All(payloadEvents, e => Assert.NotEqual(
      NullabilityState.Unknown, e.Info.GenericTypeArguments[0].ReadState));

    string[] Names(NullabilityState state) => payloadEvents
      .Where(e => e.Info.GenericTypeArguments[0].ReadState == state)
      .Select(e => e.Name)
      .OrderBy(n => n, StringComparer.Ordinal)
      .ToArray();

    Assert.Equal(
      NullPassesThrough.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
      Names(NullabilityState.Nullable));

    Assert.Equal(
      NullIsRejected.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
      Names(NullabilityState.NotNull));
  }
}
```

⚠ **When this test fails after someone adds an event, the fix is NOT to add the name to a list.** It
is to decide, per ADR-033, whether that event's null is data or a violation, then:

- **`Func<T?, Task>` → null is data.** Add an `internal On…MessageAsync` seam with **no**
  `AcceptPayload` call and a pass-through test in `AudioStateHubServicePassThroughTests`.
- **`Func<T, Task>` → null is a violation.** Add an `internal On…MessageAsync` seam that calls
  `AcceptPayload` and a rejection test in `AudioStateHubServiceNullPayloadTests`.

Then update the list. **Put that instruction in the assertion message**, not only in this plan — a
tripwire whose remedy lives in a document nobody opens gets silenced instead of answered.

⚠ **Contingency, with a defined outcome rather than a shrug.** If the "prove the instrument"
assertion fires — `NullabilityInfoContext` reporting `Unknown` on this TFM — the fallback is
`context.Create(e.Field!)` replaced by `context.Create(<the EventInfo>)`, the other documented
overload. If **both** report `Unknown`, delete this task, say so plainly in the PR body with the
measured output, and file the census as a follow-up row. ⛔ Do not substitute a weaker assertion that
compares runtime handler types — `Func<NowPlayingDto?, Task>` and `Func<NowPlayingDto, Task>` are the
same runtime type, so such a test would pass against exactly the drift it claims to catch.

---

## 6. Task 5 — the comments this change falsifies

Three, and each is a live example of `CLAUDE.md` § *Pre-Merge Review*'s first item.

**6a. `tests/Radio.Web.Tests/Services/AudioStateHubServiceTests.cs:148-158` — goes false with this PR.**
It currently reads, of `EventPlaybackChanged`:

> *"It does NOT show that a delivered "EventPlaybackChanged" message reaches this event with its
> payload intact. **No test in this assembly can**: the fixture runs on OfflineHubTransport, the
> connection is never started, and there is no in-tree precedent for reflecting into HubConnection's
> handler table to inject one…"*

⭐ **`UI-14` makes exactly that test exist**, for exactly that event — `T3` and `T6` drive
`OnEventPlaybackMessageAsync` directly. Rewrite the paragraph to the narrower true form: this test
does not prove *SignalR's* delivery or that the payload survives the real `JsonHubProtocol` (still
true, still settled on the appliance as `U1`); `AudioStateHubServicePassThroughTests` now proves that
the **handler body** dispatches the payload unchanged, null included.

⚠ **This is the same shape as the finding `UI-12`'s review called its sharpest** — a comment claiming
"no test can do X" while a test doing X is green — and `CLAUDE.md` records the version of it that left
`TEST-2` open for four weeks. Do not leave it standing.

**6b. `AudioStateHubService.AcceptPayload`'s remark — de-duplicate, do not delete.**
Its `<list>` currently carries the full per-event contract for all three nullable events. After Task 1
each seam states its own, in more detail and beside the code it describes. **Two copies of a contract
is two chances to drift**, which is the `UI-6` lesson (two hand-rolled fan-out sites had already
diverged, one carrying a try/catch and the other none). Collapse the list to the rule plus a pointer:

```csharp
  /// ⚠ The three events declared Func&lt;T?, Task&gt; — NowPlayingChanged, VolumeChanged and
  /// EventPlaybackChanged — must NOT route through here; their nulls are data. ⚠ AND THE THREE ARE
  /// NOT EQUIVALENT: each of OnNowPlayingMessageAsync, OnVolumeMessageAsync and
  /// OnEventPlaybackMessageAsync states what its own null means, beside the code that dispatches it.
  /// Gated since `UI-14` by AudioStateHubServicePassThroughTests.
```

⛔ **Keep the rest of the remark verbatim** — the `NotifyAsync<T>` prohibition, the "it is not silent"
paragraph, and the 📌 producer census are all still exactly true and were expensive to establish.

**6c. `NotifyAsync<T>`'s remark — add the one sentence that would have prevented this row.**
Append to the existing `<remarks>`:

```csharp
  /// ⛔ AND IT MUST NEVER GROW A NULL CHECK. T is erased here: this method cannot tell a contract
  /// violation from data, and three of the seven reference-payload events it fans out treat null as
  /// data. ⚠ The realistic form of that mistake — keeping AcceptPayload and adding a "defensive"
  /// check here as well — passed the ENTIRE Radio.Web.Tests assembly on 2026-09-09, measured, while
  /// silently dropping every NowPlayingChanged(null). It is gated now
  /// (AudioStateHubServicePassThroughTests), and the gate, not this comment, is what stops it.
```

---

## 7. Gates

```bash
dotnet build RadioConsole.sln -c Release > /tmp/ui14-build.log 2>&1; echo "exit=$?"
grep -E "Warning\(s\)|Error\(s\)" /tmp/ui14-build.log   # "47 Warning(s) / 0 Error(s)" — EQUALITY, not zero
dotnet test RadioConsole.sln -c Release > /tmp/ui14-test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/ui14-test.log
```

⛔ **Never pipe `dotnet test` into `tail`, `head` or `grep`** — `CLAUDE.md` records a measured run that
exited `0` with five tests failing, because the shell reports the last command's status.

⛔ **Do NOT count warning lines.** `grep -cE "warning"` returns **94** on a clean build against a 47
baseline — MSBuild emits each warning once per project-graph pass. Read the per-project
`Warning(s)` / `Error(s)` summary lines. `design/plans/OPS-2-…:536`'s
`grep -cE 'warning [A-Z]+[0-9]+'` variant has the same defect.

⚠ **Build clean before quoting a number.** An up-to-date project skips `CoreCompile` and re-emits none
of its warnings, so a warm incremental build under-reports. Every figure in this PR comes from a clean
rebuild.

Known-failing on Windows and **not** regressions: four `SrcVariableResamplerTests`
(`libsamplerate.so.0`), `NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording` — all
`Category=Integration` or platform, all excluded by `build.yml:58` in CI.

**Also run explicitly**, because this row edits the file they police and the assembly they cover:

```bash
dotnet test tests/Radio.Core.Tests -c Release --filter "FullyQualifiedName~AsyncEventFanOutLintTests"
dotnet test tests/Radio.Web.Tests -c Release --filter "FullyQualifiedName~AudioStateHubService"
dotnet test tests/Radio.Web.Tests -c Release --filter "FullyQualifiedName~NowPlayingDockTests|FullyQualifiedName~SleepTests"
```

The third is §0.6 item 3: those two fixtures fire nulls through `HubEventFire` and must be
**unaffected**. If Task 1 changed their result, something changed the events themselves.

---

## 8. UAT

⚠ **Read §0.7 first. The pass-through is NOT demonstrable on the appliance**, exactly as `UI-12`'s
guard was not. No producer can send a null on any of the three, so before and after are byte-for-byte
identical on every reachable path. UAT here is a no-regression check plus a proof that the rewritten
registrations actually execute.

1. Deploy: `./deploy/Deploy-ToLinux.ps1` (defaults are correct for `radio` since `OPS-1`).
2. Confirm both SHAs — `curl -s http://radio:5000/api/health/version` and
   `curl -s http://radio:5002/api/health/version`. The deploy exits non-zero on a mismatch.
3. ⭐ **Drive a real Blazor circuit in a browser, not a static `GET`.** The three rewritten `On<T>`
   registrations run inside `StartAsync`, which a static GET never reaches — `UI-12`'s first smoke
   attempt proved that by silently not running the changed code, which read as a pass.
4. Play a track and change it. Confirm the now-playing **dock** and the **panel** both follow, and
   that the topbar mute chip still tracks a mute/unmute. That exercises `NowPlayingChanged` and
   `VolumeChanged` through the rewritten seams on the non-null path — every subscriber in §0.5's two
   tables except the null branches.
5. `ssh mmack@radio "journalctl -u radio-web --since '-10min' --no-pager | grep -i 'null payload'"` —
   ⭐ **must be empty**, and its emptiness is the useful result: it confirms no seam started
   misclassifying an ordinary broadcast as a fault.
6. Browser console: **0 errors**.

⛔ **What the report must NOT say:** that UAT demonstrated a null passing through. It cannot, and
`UI-12`'s dossier records that claim being wrong when it was nearly made.

---

## 9. Docs impact

| Doc | Change |
|---|---|
| `design/DECISION-LOG.md` | ⭐ **Amend ADR-033 — do NOT write a new ADR.** `UI-14` does not change the rule; it gates the half of it that was unenforced. Append an amendment block recording (a) the pass-through half is now gated by `AudioStateHubServicePassThroughTests`, (b) the **measured** `M0` result — the realistic refactor was green across the whole assembly before this row — and (c) the two corrections in §9a below |
| `docs/queue/UI-14.md` | `✅ SHIPPED` block with the PR link, the measured mutation matrix, and the `C-503` correction from §0.4 |
| `docs/BUILDER_QUEUE.md` | mark the row ✅ and move it to `BUILDER_QUEUE_ARCHIVE.md` per the `OPS-2` convention (link to the dossier, prose stays in the dossier); update the last-updated banner |
| `tests/…/AudioStateHubServiceTests.cs` | §6a — the comment this PR falsifies |
| `src/…/AudioStateHubService.cs` | §6b, §6c |

Not touched, and deliberately: `design/FUTURE-WORK.md` (this row leaves no stub) and
`design/INTEGRATIONS.md` (no integration surface changes).

### 9a. Two corrections ADR-033 needs, both small and both measured

1. ⛔ **"NotifyAsync&lt;T&gt; already fans out all seven payload-carrying events" is off by one.**
   Counted from the declarations: **fourteen** events, of which **eight** carry a payload
   (`NowPlayingChanged`, `RadioStateChanged`, `VolumeChanged`, `EncoderConnectionChanged`,
   `EncoderConfigStatusChanged`, `EncoderHudChanged`, `SleepStateChanged`, `EventPlaybackChanged`) and
   **seven** of those eight have a reference-type payload — `SleepStateChanged` is `Func<bool, Task>`
   and ADR-033's rule does not reach it. Write it as *"eight payload-carrying events, seven of them
   with a reference-type payload"*. ⚠ This is the third counting slip in this file's own history; the
   `NotifyAsync<T>` remark already records "an earlier revision said THIRTEEN events. There are
   FOURTEEN." Task 4's floor assertion is what stops a fourth.
2. ⚠ **The producer asymmetry the row implies does not exist.** ADR-033 correctly says the four
   non-nullable events' nulls are unreachable from current server source. §0.4 establishes the same
   for all three nullable ones. The amendment should state the symmetry plainly, and that what
   distinguishes `UI-14` from `UI-12` is **permanence of the failure**, not reachability.

---

## 10. What this row deliberately does not do

- ⛔ **It does not change any runtime behaviour.** Every seam body is the lambda body it replaces,
  moved. If the diff changes what a subscriber receives on any path, something is wrong.
- ⛔ **It does not touch `AcceptPayload` or the four guarded events**, beyond §6b's de-duplication of
  a comment. `UI-12`'s eight tests must be green and untouched throughout.
- ⛔ **It does not add a lint asserting that `NotifyAsync<T>` contains no null check.** That was
  considered and rejected: a source-text rule pins *shape*, and `UI-12`'s own dossier is emphatic that
  *"a test that merely asserts the guard exists proves nothing about whether the guard should exist"* —
  which reads identically inverted. `M1` and `M7` together do the job properly: one proves the
  forbidden refactor is caught, the other proves a legitimate one is not.
- ⛔ **It does not give `EventPlaybackChanged(null)` a semantics.** No producer sends one. §0.5 says so,
  the seam comment says so, and `T3`'s summary says so. An unevidenced contract asserted confidently in
  a comment is the defect class `CLAUDE.md` § *Pre-Merge Review* lists four instances of.
- ⛔ **It does not re-tier or re-order the queue.** `UI-14` stays 🟡 P2 where the `UI-12` Builder filed
  it.
