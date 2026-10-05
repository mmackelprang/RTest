# PLAN — `UI-13` · The cache-advance is real, reproducible, and only host shutdown can reach it

> **Row:** `UI-13`, [`docs/queue/UI-13.md`](../../docs/queue/UI-13.md). 🟡 **P2** as filed.
> **Branch:** `fix/ui-13-cache-advance-before-send` (unchanged — still accurate).
> **Estimate:** **0.5 d.**
> **Planned against** `main` at **`15772b58`**. Every line number below was read out of the tree at that commit.
> **Nothing on the box was touched.** No deploy, no restart, no config write. This row is entirely
> `src/Radio.API` + `tests/`.
>
> ### ⛔ READ §0.1 FIRST. THE ROW'S HEADLINE PREMISE IS FALSE AND THE RECOMMENDATION IS A RE-TIER.
> **`SendAsync` does not throw on a failed broadcast.** It throws on exactly one thing — cancellation
> — and this service's only token is `stoppingToken`. **Recommendation: re-tier 🟡 P2 → 🔵 P3.**
> The work below is a *tidy plus a written-down precondition*, not a defect fix, and §0.9 gives the
> argument for closing the row outright instead.

---

## 0. Read this before Task 1

### 0.1 ⚠⚠ `C-501` — "MAY THROW" IS FALSE. ONLY CANCELLATION ESCAPES

[`docs/queue/UI-13.md:14-16`](../../docs/queue/UI-13.md) says:

> ```csharp
> _lastRadioState = dto;          // :477 — cache advanced
> await SendAsync(...);           // may throw, may be cancelled
> ```

**It cannot throw. It can only be cancelled.** Established two independent ways on 2026-09-09 — by
reading the ASP.NET Core source on `release/10.0`, and by running a real Kestrel host with real
WebSocket clients against `Microsoft.AspNetCore.App` **10.0.11**, the runtime this repo targets.

This project uses the default in-process lifetime manager: `Program.cs:84` is a bare
`AddSignalR(options => { ClientTimeoutInterval; KeepAliveInterval; })`. **No backplane and no
MessagePack anywhere in `src/`** — `grep -rn "AddSignalR\|StackExchangeRedis\|AddAzureSignalR\|HubLifetimeManager\|MessagePack" src/` returns that one line and nothing else. So every send below runs
through `DefaultHubLifetimeManager<AudioStateHub>` and the JSON protocol.

**Measured. Each row is an observation, not an inference:**

| Probe | `SendAsync` faulted? | What actually happened |
|---|---|---|
| Zero clients — `Clients.All`, `Clients.Group("nobody")`, and the no-arg `SendAsync("SourceChanged", ct)` overload | **NO** | returns in 0 ms |
| Client's transport hard-killed (`ClientWebSocket.Abort()`, no close frame), then **129 consecutive group sends over 8.03 s** | **NO** | not one fault |
| Immediately after a graceful `StopAsync()` | **NO** | — |
| Unserializable payload (self-referencing cycle; a `System.Type` property) with a **verified-live** connection | **NO** — caller saw success in 8–10 ms | server logged `HubConnectionContext[6] Failed writing message. Aborting connection.` and **dropped the client**, which received 0 messages |
| Group that never existed / emptied by `RemoveFromGroupAsync` / whose only member disconnected | **NO** | all three, 0 ms |
| Already-cancelled token, **zero** connections | **NO** | the token is never looked at |
| Already-cancelled token, live connection **not in the target group** | **NO** | same reason |
| ⭐ Already-cancelled token, **≥1 connection that is a target of the send** | ⭐ **YES** | `TaskCanceledException` — *"A task was canceled."* Client proven live by a round-trip `InvokeAsync<string>("Echo")` immediately before |
| ⭐ Token cancelled **while a write is blocked on backpressure** (client completes the handshake then never reads; 256 KB message stalls the write >2.5 s) | ⭐ **YES, 4/4 runs** | `OperationCanceledException` |

The governing source, verbatim. `DefaultHubLifetimeManager.cs` (`release/10.0`,
`src/SignalR/server/Core/src/` — ⚠ **not** under `Internal/`; that path 404s):

```csharp
136        if (tasks == null)
137        {
138            return Task.CompletedTask;
139        }
140
141        // Some connections are slow
142        return Task.WhenAll(tasks);
```

With no connection matching the send, no per-connection task is ever created, `tasks` stays null and
`Task.CompletedTask` is returned **without the token being inspected at all**. `SendGroupAsync:206-221`
does the same for an absent or empty group.

`HubConnectionContext.cs`, the write path a broadcast actually takes:

```csharp
341        catch (Exception ex)
342        {
343            CloseException = ex;
344            Log.FailedWritingMessage(_logger, ex);
345
346            AbortAllowReconnect();
347
348            return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: true));
349        }
```

⭐ **Every failure is converted into a *successful* `FlushResult` and charged to the connection, not to
the caller.** That single `catch` is why a dead socket, a dead circuit and an unserializable payload all
return success.

And the one deliberate exception, at `:361`, repeated verbatim at `:392` and `:421`:

```csharp
358        // We care about errors while serializing to the PipeWriter as that will leave the Pipe
359        // in an invalid (for our scenario) state. OCE shouldn't occur while serializing bytes and
360        // writing to the Pipe. We assume that PipeWriter.WriteAsync(buffer) always writes the full message before calling FlushAsync
361        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
```

**The filter swallows everything except an `OperationCanceledException` raised while the caller's own
token is cancelled.** The two `WriteSlowAsync` overloads additionally `await _writeLock.WaitAsync(cancellationToken)` *outside* their `try` (`:378`, `:407`), so under write-lock contention a cancelled
token escapes with no filter at all. `_writeLock` is a `readonly SemaphoreSlim` that is **never
disposed**, so there is no `ObjectDisposedException` path either.

⚠ **A comment I half-remembered and went looking for — *"…they will never be faulted"* — DOES NOT
EXIST.** It is not in either file on `release/10.0`, and a GitHub code search across `dotnet/aspnetcore`
under `src/SignalR/` finds it nowhere. The nearest real thing is an XML doc at `HubConnectionContext.cs:179` — *"If the write throws this task will still complete successfully"* — which sits on the
**`HubMessage`** overload used by `SendConnectionAsync`, **not** the `SerializedHubMessage` overload
that `Clients.All` / `Clients.Group` use (its `<returns>` at `:229` is empty), and which line `361`
contradicts even there. **Do not cite that comment. It is recorded here only so nobody re-derives it
from memory as I nearly did.**

### 0.2 ⭐ `C-502` — the mechanism is REAL and was reproduced verbatim. It is also consequence-free

The production shape was replicated exactly — advance `_lastRadioState`, then
`await Clients.Group("RadioState").SendAsync("RadioStateChanged", dto, stoppingToken)` with a cancelled
token and a live subscribed client. **It threw `TaskCanceledException` with the cache already
advanced.** Same for `Group("Queue")` and for the no-arg `Clients.All.SendAsync("SourceChanged", ct)`.
And the delta really is lost, not merely delayed: the client received **0** messages for that payload,
the connection stayed `Connected`, and the next send with a fresh token arrived normally.

**So the row's mechanism is not imaginary. Its trigger is.** Both escape conditions require the
*caller's* token to be cancelled, and on this path that token is always `ExecuteAsync`'s
`stoppingToken` — passed down through `CheckAndBroadcastUpdatesAsync(stoppingToken)` at `:191` and
into all six checks. `BackgroundService` cancels it in `StopAsync`, i.e. **at host shutdown and
nowhere else.**

⭐ **And the closing argument is four lines below the loop that would suffer:**

```csharp
// src/Radio.API/Services/AudioStateUpdateService.cs:196-199
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        // Normal shutdown, don't log as error
        break;
      }
```

**The one exception `SendAsync` can produce is the one exception `ExecuteAsync` treats as "stop and
exit."** The loop breaks, the host disposes the service, and no comparison ever reads the stranded
cache again. `AddHostedService<AudioStateUpdateService>()` at `Program.cs:134` is the sole registration
— no `AddSingleton`, so nothing else holds the instance and no second `ExecuteAsync` exists.

✅ **Verdict: reachable, reproducible, and with zero production consequence. This is a tidy, not a
fix.** The row's own stop condition — *"if it swallows, the cache-advance is unreachable and this is a
tidy"* — is met in substance, though not by the mechanism it guessed.

### 0.3 `C-503` — the row's site list is wrong in two ways. There are SIX sites, and one of them is dead

The row lists `:266`, `:279`, `:453-454`, `:477`, `:502`. Enumerated exhaustively instead —
`grep -nE "_last(PlaybackState|NowPlaying|Queue|QueueSnapshot|RadioState|Volume|ActiveSourceType)\s*=" src/`:

| # | Line | Field | Proxy | Live? |
|---|---|---|---|---|
| 1 | **`:249`** | `_lastActiveSourceType` | `Clients.All` | ⭐ **YES — MISSING FROM THE ROW** |
| 2 | `:266` | `_lastPlaybackState` | `Clients.All` | yes |
| 3 | `:279` | `_lastNowPlaying` | `Clients.All` | yes |
| 4 | `:453` | `_lastQueueSnapshot` | `Clients.Group("Queue")` | yes |
| — | `:454` | `_lastQueue` | — | ⭐ **NO — assigned and never read** |
| 5 | `:477` | `_lastRadioState` | `Clients.Group("RadioState")` | yes |
| 6 | `:502` | `_lastVolume` | `Clients.All` | yes |

Two corrections:

1. ⭐ **`:249` has the identical shape and the row misses it.** It is also the one site with a real
   structural difference: the assignment is unconditional while the send is inside `if (!isFirstRun)`,
   so a naive move would stop the very first poll from establishing a baseline. Task 1a handles that.
2. ⭐ **`:454` is not a defect site — `_lastQueue` is a dead field.** It is declared at `:55`, written
   at `:454`, and **read nowhere in `src/`**. Change detection for the queue runs entirely off
   `_lastQueueSnapshot` (`HasQueueSnapshotChanged(_lastQueueSnapshot, snapshot)` at `:444`). The row
   counts `:453-454` as one site; it is one live site and one dead field. Task 1d moves both so the
   block stays internally consistent, but **only `:453` carries the behaviour.**

~~⚠ **Deleting `_lastQueue` is out of scope.** It is not currently flagged (the 47-warning baseline is
all `IDE0011`), and a dead-code deletion inside a mechanical diff is the thing `UI-12` §7 declined for
`ConfigChanged`. **File it; do not fold it in.**~~

⛔ **STRUCK 2026-09-09 BY THE BUILDER — `_lastQueue` WAS DELETED, and the plan's stated reason did not
survive contact with `git log`.** This plan classed it as ambiguous dead code. It is not ambiguous:
`_lastQueue` was the **original** queue change-detection cache, read by
`HasQueueChanged(_lastQueue, currentQueue)`. Commit **`d465cdd2`** replaced that comparison with the
cheaper `HasQueueSnapshotChanged(_lastQueueSnapshot, snapshot)` **and deleted `HasQueueChanged`
itself**, leaving the write orphaned in the same commit. It is a completed-refactor leftover, not a
half-finished feature — the distinction the "file it separately" instruction existed to protect.

Three things decided it:
1. **The deletion is compiler-verified.** Removing the declaration means any missed reader fails the
   build loudly. It did not; and the pre-merge reviewer independently confirmed no reader in `src/`
   or `tests/`.
2. **Keeping it cost more than removing it.** Task 1d's own replacement block below shipped a
   three-line comment whose entire content was *"this line does nothing"*, inside the very block this
   PR rewrites — and that comment would itself need deleting when the follow-up row landed.
3. ⛔ **Do NOT file the follow-up 🔵 row §6 asks for. The work is done.**

### 0.4 `C-504` — no site is deliberately ordered that way. The shape is copied, and was never reasoned about

The row's scope question 4 asks whether one site might be ordered that way on purpose. `git log -S`
on each assignment's exact text:

| Field | Introduced by | Date |
|---|---|---|
| `_lastPlaybackState`, `_lastNowPlaying`, `_lastRadioState`, `_lastVolume` | **`3bd166eb`** — *"Implement Audio State Hub with SignalR for real-time updates"* | 2025-12-04 |
| `_lastActiveSourceType` | `5b47fc74` — *"UI consistency, playlist persistence, TestTone toggle, sleep mode (#272)"* | 2026-03-04 |
| `_lastQueueSnapshot` | `d465cdd2` — *"perf: Reduce GC pressure and memory allocations…"* | 2026-03-05 |

Four sites were born in one commit with the order already this way; the two later ones copied it. **No
commit message mentions ordering, and no commit has ever reordered one of them.** Same finding shape as
`UI-12` §0.3: uniform, copied, unexamined. **Nothing here is being overridden.**

### 0.5 `C-505` — re-entrancy: nothing serialises the caches, because nothing needs to

The row's scope question 1, and the brief's ⚠. Answered by enumeration, not assumption:

1. **One caller.** `CheckAndBroadcastUpdatesAsync` is invoked at exactly one place — `:191`, inside
   `ExecuteAsync`'s single `while` loop, `await`ed. `BackgroundService.StartAsync` calls `ExecuteAsync`
   once per instance.
2. **One instance.** `Program.cs:134` registers it *only* as `AddHostedService<T>`. Nothing resolves the
   concrete type, so nothing else can call in or flip the public `IsEnabled` / `UpdateIntervalMs`.
3. **One writer per field.** The grep in §0.3 is exhaustive over `src/`: all seven assignments live
   inside the six `Check*Async` methods, which are reachable only from `CheckAndBroadcastUpdatesAsync`.
4. **The `async void` handlers do not touch them.** The eight event handlers (`OnBluetoothStateChanged`,
   `OnFingerprintStatusChanged`, `OnEncoder*`, `OnEventPlaybackChanged`) run on arbitrary threads and
   write only `_lastFingerprintBroadcast` (`:916`) and `_currentMatchId` (`:951`, already `volatile`,
   already documented at `:62-70`). **Neither is a change-detection cache for these six broadcasts.**

⭐ **So the swap is safe, and the reason is that there is no second writer to interleave with — not
that something guards against one.** The stale-overwrite hazard the row warns about requires two
concurrent updates; this service has one sequential one.

⚠ **One nuance, stated rather than left implied.** After the swap the assignment runs *after* an
`await`, so possibly on a different thread-pool thread (a `BackgroundService` has no
`SynchronizationContext`). That is a **visibility** question, not a race: the continuation has a
happens-before edge with the code preceding the `await`, and it is still the same single logical flow.
No `volatile` and no lock is warranted, and adding one would imply a concurrency that does not exist.

📌 **Those handlers are also incapable of this failure for a second reason:** every one of them calls
`SendAsync` with **no token**, so `CancellationToken.None` makes even the cancellation path
unreachable. They are correctly excluded from this row.

### 0.6 ⚠⚠ `C-506` — THE ROW'S `TimeProvider` INSTRUCTION IS A CATEGORY ERROR. DO NOT ADD THE SEAM

Both [`docs/queue/UI-13.md:62-64`](../../docs/queue/UI-13.md) and the `BUILDER_QUEUE.md:53` row say
*"this service is timer-driven … use the injectable `TimeProvider` idiom (`EncoderHudService` is the
worked example)."*

**The service is timer-driven. The defect is not.** It lives inside the six `Check*Async` methods,
which take no time input, read no clock, and contain no delay. The only clock in `ExecuteAsync` is
`await Task.Delay(updateDelay, stoppingToken)` at `:194`, and **the tests below never enter
`ExecuteAsync` at all** — they invoke each `Check*Async` directly and await it to completion, exactly
as the existing `AudioStateUpdateServiceTests` reaches `UpdateCurrentMatchAnchor` and
`HasRdsRelevantChanged` by reflection today.

⭐ **That satisfies `CLAUDE.md` § *Test Timing* in its strongest form, not a weaker substitute.** The
rule is *"synchronize on the observation, not on elapsed time."* Calling the method and awaiting its
task **is** the observation. There is no wall clock on either side of the assertion, so there is no
clock to fake — and injecting a `TimeProvider` would add a production seam that no test in this row
would drive. `Microsoft.Extensions.TimeProvider.Testing` **9.10.0 is already referenced** by
`tests/Radio.API.Tests/Radio.API.Tests.csproj`, so it is available; it is simply not needed.

⚠ **There IS a wall-clock throttle in this file, and it is not one of the six sites.**
`_lastFingerprintBroadcast` (`:49`) is stamped from `DateTime.UtcNow` at `:910-916`, before
`SendAsync` at `:919`. Testing *that* would need `TimeProvider`. It is deliberately excluded here:
it is a **rate limiter, not a change-detection cache**, and advancing it on a failed send is arguably
correct — it prevents a failing hub from being retried at full rate. **Do not "fix" it as part of this
row.**

### 0.7 ⭐ `C-507` — the row's "silent" claim is half wrong, and the true statement is sharper

The row says *"The failure is silent and self-erasing."* The **send** failure is not silent: an
exception escaping `CheckAndBroadcastUpdatesAsync` reaches `ExecuteAsync:201-204`, which logs
`LogError(ex, "Error broadcasting audio state updates")` and backs off 1 s. Only the **stranded cache**
is silent.

But there is a worse consequence the row does not state, and it is the one the tests pin. On `main`,
against a hypothetically persistent fault:

- **Tick 1** — `CheckPlaybackStateAsync` sends (cache was null), faults. Cache already advanced.
  `ExecuteAsync` logs one Error and waits 1 s.
- **Tick 2** — playback now compares equal, so it is skipped; `CheckNowPlayingAsync` sends, faults,
  advances. One Error.
- **Ticks 3–6** — the same, one cache at a time.
- **Tick 7 onward** — every cache is populated and every comparison says "no change".
  ⭐ **The service sends nothing and logs nothing. It goes completely silent in about three seconds and
  never recovers, even after the fault clears.**

After the fix, the same fault produces one Error line every ~1.5 s for as long as it lasts, and the
first successful tick afterwards delivers the state. ⚠ Note the sequence still aborts at the *first*
failing send each tick — the later checks do not run — which is true on `main` too and is correct:
the next tick re-attempts from the top.

📌 **All of this is hypothetical**, because §0.2 shows the only reachable fault is shutdown. It is
recorded because it is what the tests in Task 3 assert, and a reader must not mistake those
assertions for evidence of a live defect.

### 0.8 What must not regress

| Guard | Where | Why it constrains this row |
|---|---|---|
| `RdsRelevantChanged` is stamped against the **previous** state | `:471-475` | ⚠ `:475` must stay **before** the send — it is part of the payload. It reads `_lastRadioState` while that is still the previous value, which the swap preserves. Task 1e keeps the two statements in that order |
| First-poll suppression of `SourceChanged` | `:247-251` | The very first poll must still establish `_lastActiveSourceType` **without** broadcasting. Task 1a keeps the assignment reachable on the `isFirstRun` path |
| `PushMetadataToCastAsync` runs only after a successful `NowPlayingChanged` | `:286` | Unchanged by the swap; it already has its own catch (`:312-315`) and returns early when `_castOutput` is null |
| Existing `AudioStateUpdateServiceTests` | `tests/Radio.API.Tests/Services/` | 12 tests reach private members by reflection. This row renames nothing and moves no method |
| Release warning baseline | **47 warnings, 0 errors** | `CLAUDE.md`. The gate is *equality*, not zero |

### 0.9 ⚖ The recommendation, and the honest case against doing this at all

**Recommended: re-tier `UI-13` from 🟡 P2 to 🔵 P3 and ship Tasks 1–3.** Reasons, in order of weight:

1. ⭐ **The precondition is the deliverable.** What makes this harmless took a full source read plus a
   real-host probe to establish, and it is written down nowhere in the repo. Task 2's comment records
   it. That is worth more than the line moves.
2. ⚠ **The precondition changes silently.** A Redis or Azure SignalR backplane replaces
   `DefaultHubLifetimeManager` with one whose send genuinely faults on a backplane outage; passing any
   token other than `stoppingToken` makes cancellation reachable *while the service keeps running*.
   Neither would produce an error at this call site — both would produce panels that quietly stop
   updating, which is precisely the `AUD-12` / `GV-12` family the row names.
3. The ordering is wrong *as expression*: it records "the clients have this" as a fact before it is one.

**The case for closing instead, stated fairly:** the change is unobservable on every reachable path
today, and this repo's stated pathology is rows filed on premises nobody checked. Spending a Builder
cycle on a non-defect is a real cost. **If you prefer that, close `UI-13` and append §0.1–§0.3 to
[`docs/queue/UI-13.md`](../../docs/queue/UI-13.md) as a dated correction** — the falsification is the
valuable part and it survives either decision. I would not argue hard against it.

⛔ **What must NOT happen is shipping this at 🟡 P2 as filed**, which would tell the next reader that a
live defect was fixed.

### 0.10 Auto-merge

✅ **The row's guess holds: auto-mergeable on green gates.** `src/Radio.API` + `tests/` only; no
hardware, no live-audio path, no migration, no production config.

⚠ **But narrow what UAT can mean, exactly as `UI-12` §0.9 did.** The change is **unobservable in
production**: §0.2 shows the before and after behaviour are identical on every reachable path. UAT can
only confirm no regression on the normal path. **It cannot demonstrate the fix, and a UAT report
claiming it did would be wrong.** The evidence is Task 3's tests and nothing else.

---

## 1. Task 1 — move the cache advance after the send, at all six sites

**File:** `src/Radio.API/Services/AudioStateUpdateService.cs`

⚠ **All six, or none.** Repairing one and leaving five makes the class look handled — the row is right
about that even though it is wrong about the trigger.

**1a.** Replace `CheckSourceChangedAsync` (`:241-258`) with:

```csharp
  private async Task CheckSourceChangedAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    var currentSourceType = activeSource?.Type.ToString();

    if (currentSourceType != _lastActiveSourceType)
    {
      // Skip broadcast on first poll (null → initial value) to avoid spurious SourceChanged
      var isFirstRun = _lastActiveSourceType == null;

      if (!isFirstRun)
      {
        await _hubContext.Clients.All
          .SendAsync("SourceChanged", cancellationToken);
        _logger.LogInformation("Broadcast SourceChanged: {SourceType}", currentSourceType ?? "None");
      }

      // ⚠ AFTER the send — see the ordering remark on the cache fields above (UI-13). On the
      // first poll there is no send, so this still runs and establishes the baseline; that
      // asymmetry is why this site could not be moved by the same mechanical edit as the others.
      _lastActiveSourceType = currentSourceType;
    }
  }
```

**1b.** In `CheckPlaybackStateAsync`, replace `:266-269` with:

```csharp
      await _hubContext.Clients.All
        .SendAsync("PlaybackStateChanged", currentState, cancellationToken);
      _lastPlaybackState = currentState;
      _logger.LogDebug("Broadcast PlaybackStateChanged");
```

**1c.** In `CheckNowPlayingAsync`, replace `:279-283` with:

```csharp
      await _hubContext.Clients.All
        .SendAsync("NowPlayingChanged", currentNowPlaying, cancellationToken);
      _lastNowPlaying = currentNowPlaying;
      _logger.LogDebug("Broadcast NowPlayingChanged: Title={Title}, Artist={Artist}, Album={Album}, AlbumArt={AlbumArtUrl}, Source={Source}",
        currentNowPlaying.Title, currentNowPlaying.Artist, currentNowPlaying.Album, currentNowPlaying.AlbumArtUrl, currentNowPlaying.SourceName);
```

⚠ The `await PushMetadataToCastAsync(...)` at `:286` stays where it is, after the log. It must not run
for a broadcast that did not land.

**1d.** In `CheckQueueAsync`, replace `:453-457` with:

⛔ **AMENDED 2026-09-09 — this block is NOT what shipped.** `_lastQueue` was deleted rather than
moved (§0.3, struck note). What shipped is:

```csharp
    await _hubContext.Clients.Group("Queue")
      .SendAsync("QueueChanged", currentQueue, cancellationToken);
    _lastQueueSnapshot = snapshot;
    _logger.LogDebug("Broadcast QueueChanged with {Count} items", currentQueue.Count);
```

~~Superseded original:~~

```csharp
    await _hubContext.Clients.Group("Queue")
      .SendAsync("QueueChanged", currentQueue, cancellationToken);
    // ⚠ Only _lastQueueSnapshot participates in change detection (HasQueueSnapshotChanged, :444).
    // _lastQueue is written here and read nowhere — moved with it so the block stays consistent,
    // but it carries no behaviour. Its removal is filed separately, not folded in here (UI-13 §0.3).
    _lastQueueSnapshot = snapshot;
    _lastQueue = currentQueue;
    _logger.LogDebug("Broadcast QueueChanged with {Count} items", currentQueue.Count);
```

**1e.** In `CheckRadioStateAsync`, replace `:471-481` with:

```csharp
      // Stamp the per-broadcast discriminator BEFORE caching/sending so the
      // Web RDS path can skip its accumulator append on telemetry-only ticks.
      // Computed against the PREVIOUS state (the same baseline HasRadioStateChanged
      // used), so the very first broadcast (_lastRadioState == null) is RDS-relevant.
      //
      // ⚠ THIS STATEMENT MUST STAY ABOVE THE SEND — it writes a field of the payload. Only the
      // _lastRadioState assignment moved below it (UI-13). Both read the pre-send value of
      // _lastRadioState, so the stamp is unaffected by the move.
      currentRadioState.RdsRelevantChanged = HasRdsRelevantChanged(_lastRadioState, currentRadioState);

      await _hubContext.Clients.Group("RadioState")
        .SendAsync("RadioStateChanged", currentRadioState, cancellationToken);
      _lastRadioState = currentRadioState;
      _logger.LogDebug("Broadcast RadioStateChanged: {Frequency} {Band} RdsRelevant={Rds}",
        currentRadioState.Frequency, currentRadioState.Band, currentRadioState.RdsRelevantChanged);
```

**1f.** In `CheckVolumeAsync`, replace `:502-505` with:

```csharp
      await _hubContext.Clients.All
        .SendAsync("VolumeChanged", currentVolume, cancellationToken);
      _lastVolume = currentVolume;
      _logger.LogDebug("Broadcast VolumeChanged: {Volume}, Muted: {IsMuted}", currentVolume.Volume, currentVolume.IsMuted);
```

---

## 2. Task 2 — write the precondition down. This is the substance of the row

**Why:** §0.9 item 1. Without it, the next reader either believes a live defect was fixed, or deletes
the ordering as pointless. `CLAUDE.md` § *Pre-Merge Review* also requires that a comment offering a
reason a thing is safe states a reason that is actually checkable — so the comment names the source
lines and the measurement rather than asserting a conclusion.

Insert immediately above the `// Cached state to detect changes` block at `:52`:

```csharp
  // ⚠⚠ THE CHANGE-DETECTION CACHES BELOW ARE ADVANCED **AFTER** THE SEND, NEVER BEFORE (queue row
  // UI-13). Each _last* field is the baseline its Has*Changed comparison runs against. Advancing one
  // before the await records "the clients have this" as a fact before it is one: if the send does not
  // complete, the next comparison sees "no change" against a state the clients never received, and
  // that delta is never re-broadcast. There is no retry, and the evidence that a broadcast was missed
  // is destroyed by the same statement that misses it.
  //
  // 📌 WHY THIS IS CURRENTLY UNOBSERVABLE — a PRECONDITION, not a property of this file. Established
  // 2026-09-09 by reading dotnet/aspnetcore release/10.0 and by running a real Kestrel host with real
  // WebSocket clients on Microsoft.AspNetCore.App 10.0.11:
  //   • With no connection matching the send, DefaultHubLifetimeManager returns Task.CompletedTask
  //     without inspecting the token at all (DefaultHubLifetimeManager.cs:136-139 for Clients.All,
  //     :206-221 for an absent or empty group).
  //   • Every other failure is charged to the CONNECTION, not the caller: HubConnectionContext.cs
  //     :341-349 catches it, logs "Failed writing message", aborts the connection, and returns a
  //     SUCCESSFUL FlushResult. Measured: 129 consecutive sends to a hard-killed socket faulted none;
  //     an unserializable payload dropped the client while the caller saw success in 8 ms.
  //   • The single escape is an OperationCanceledException raised while the CALLER's token is
  //     cancelled — the filter at HubConnectionContext.cs:361 deliberately does not catch that one.
  // Here that token is always ExecuteAsync's stoppingToken, which BackgroundService cancels only at
  // host shutdown — and :196 catches exactly that and breaks, so the caches die with the process.
  //
  // ⚠ SO THIS ORDERING BECOMES LOAD-BEARING ONLY IF THAT PRECONDITION CHANGES, AND IT WOULD CHANGE
  // SILENTLY. A Redis or Azure SignalR backplane swaps in a lifetime manager whose send really does
  // fault on a backplane outage; passing any token other than stoppingToken makes cancellation
  // reachable while the service keeps running. Neither would raise an error at these call sites —
  // both would produce panels that quietly stop updating, the AUD-12 / GV-12 failure family.
  // Keep every send above its assignment.
```

---

## 3. Task 3 — the tests, and they must fail first

**File:** `tests/Radio.API.Tests/Services/AudioStateUpdateServiceCacheOrderingTests.cs` (new)

A new file rather than an addition to `AudioStateUpdateServiceTests.cs`: that file is 622 lines and its
`CreateServiceWith` helper mocks only `Clients.All`, while two of the six sites broadcast to
`Clients.Group(...)`.

⚠ **The fake fails by cancellation and nothing else, and that is deliberate.** An
`InvalidOperationException` would be a fault the real lifetime manager cannot produce (§0.1). Modelling
the one real escape keeps the test honest about what it pins.

```csharp
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Tests.Services;

/// <summary>
/// The cache-advance ordering contract of <see cref="AudioStateUpdateService"/> (queue row `UI-13`):
/// a change-detection cache must never be advanced past a broadcast that did not complete.
/// </summary>
/// <remarks>
/// ⚠⚠ WHAT THESE DO NOT PROVE, SAID PLAINLY BECAUSE THE ROW ASSUMED OTHERWISE. They do NOT show a live
/// defect. Measured 2026-09-09 against Microsoft.AspNetCore.App 10.0.11 with a real Kestrel host: the
/// default in-process DefaultHubLifetimeManager cannot fault a broadcast for a dead client, a dead
/// circuit, an unserializable payload or an absent group — HubConnectionContext.cs:341-349 converts
/// every one of those into a successful FlushResult and aborts the CONNECTION instead. The only escape
/// is an OperationCanceledException while the caller's token is cancelled (the filter at :361), and on
/// this path that token is ExecuteAsync's stoppingToken, cancelled only at host shutdown — where
/// AudioStateUpdateService.cs:196 breaks the loop and the caches are discarded anyway.
///
/// These pin the ORDERING, so that the day a backplane or a different token makes the fault reachable,
/// it is already handled rather than silently live. See `UI-13` §0.1-§0.2.
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK — CLAUDE.md § *Test Timing*. Each Check*Async is invoked
/// directly and awaited to completion before anything is asserted, so every observation is a fact
/// about control flow. ⛔ Do NOT introduce a TimeProvider seam for these: the defect is in the
/// Check*Async bodies, which read no clock, and ExecuteAsync's Task.Delay is never entered (§0.6).
/// </remarks>
public class AudioStateUpdateServiceCacheOrderingTests
{
  // ─── the fake hub ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Records every attempted send and fails exactly the way DefaultHubLifetimeManager can: by
  /// honouring a cancelled caller token. The attempt is recorded BEFORE the throw, because the
  /// question these tests ask is "was the send attempted and did it fail", not "did it succeed".
  /// </summary>
  private sealed class RecordingClientProxy : IClientProxy
  {
    public List<string> Methods { get; } = [];

    public List<object?> Payloads { get; } = [];

    public int Attempts => Methods.Count;

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
      Methods.Add(method);
      Payloads.Add(args.Length > 0 ? args[0] : null);
      cancellationToken.ThrowIfCancellationRequested();
      return Task.CompletedTask;
    }
  }

  private static (AudioStateUpdateService Service, RecordingClientProxy Proxy) CreateService(
    IAudioManager? audioManager = null)
  {
    var proxy = new RecordingClientProxy();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy);
    clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);

    var hubContext = new Mock<IHubContext<AudioStateHub>>();
    hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

    var collection = new ServiceCollection();
    collection.AddSingleton(audioManager ?? StableAudioManager());

    var service = new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContext.Object,
      collection.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

    return (service, proxy);
  }

  /// <summary>Constant readings, so a second tick with an unchanged world compares equal.</summary>
  private static IAudioManager StableAudioManager()
  {
    var mock = new Mock<IAudioManager>();
    mock.SetupGet(m => m.MasterVolume).Returns(0.42f);
    mock.SetupGet(m => m.IsMuted).Returns(false);
    mock.SetupGet(m => m.Balance).Returns(0.0f);
    return mock.Object;
  }

  // ─── reflection helpers, matching the idiom in AudioStateUpdateServiceTests ───────────────────

  private static Task InvokeCheck(
    AudioStateUpdateService svc, string name, params object?[] args)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      name, BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(m);
    return (Task)m!.Invoke(svc, args)!;
  }

  private static CancellationToken Cancelled()
  {
    var cts = new CancellationTokenSource();
    cts.Cancel();
    return cts.Token;
  }

  // ─── source doubles ──────────────────────────────────────────────────────────────────────────

  private static Mock<IAudioSource> BareSource(AudioSourceType type = AudioSourceType.Radio)
  {
    var mock = new Mock<IAudioSource>();
    mock.SetupGet(s => s.Id).Returns("src-1");
    mock.SetupGet(s => s.Name).Returns("Test Source");
    mock.SetupGet(s => s.Type).Returns(type);
    mock.SetupGet(s => s.Category).Returns(AudioSourceCategory.Primary);
    mock.SetupGet(s => s.State).Returns(AudioSourceState.Playing);
    mock.SetupGet(s => s.Volume).Returns(1.0f);
    return mock;
  }

  private static QueueItem Item(string id, int index) => new()
  {
    Id = id,
    Title = $"Track {index}",
    Artist = "Test Artist",
    Album = "Test Album",
    Index = index,
    FullPlaylistIndex = index,
  };

  // ─── the six sites ───────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// ⭐ THE HEADLINE, and the cheapest of the six. Site :502.
  /// Tick 1 attempts VolumeChanged and is cancelled. Tick 2 sees an unchanged world; because the
  /// cache was never advanced past the failed send, it must re-broadcast.
  /// On `main` the cache advanced at :502 before the await, so tick 2 compares equal and sends
  /// nothing — Attempts is 1, not 2.
  /// </summary>
  [Fact]
  public async Task VolumeDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckVolumeAsync", Cancelled()));

    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["VolumeChanged", "VolumeChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>Site :266.</summary>
  [Fact]
  public async Task PlaybackStateDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var source = BareSource().Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckPlaybackStateAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckPlaybackStateAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["PlaybackStateChanged", "PlaybackStateChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>Site :279.</summary>
  [Fact]
  public async Task NowPlayingDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var source = BareSource().Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckNowPlayingAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckNowPlayingAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["NowPlayingChanged", "NowPlayingChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// Site :249 — ⭐ THE ONE THE ROW MISSED, and structurally the odd one out (§0.3).
  /// Three calls, not two: the first poll establishes the baseline WITHOUT broadcasting, so the
  /// failed send has to be the second call and the retry the third.
  /// </summary>
  [Fact]
  public async Task SourceChangedDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var radio = BareSource(AudioSourceType.Radio).Object;
    var bluetooth = BareSource(AudioSourceType.Bluetooth).Object;

    // Tick 1 — first poll. Baseline only, no broadcast.
    await InvokeCheck(svc, "CheckSourceChangedAsync", radio, CancellationToken.None);
    Assert.Equal(0, proxy.Attempts);

    // Tick 2 — the source changed, so this broadcasts. Cancelled.
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckSourceChangedAsync", bluetooth, Cancelled()));

    // Tick 3 — the world is unchanged since tick 2, so only a non-advanced cache re-sends.
    await InvokeCheck(svc, "CheckSourceChangedAsync", bluetooth, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["SourceChanged", "SourceChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>Site :453 — the live half of the row's ":453-454" (§0.3).</summary>
  [Fact]
  public async Task QueueDeltaIsReSentAfterACancelledBroadcast()
  {
    var (svc, proxy) = CreateService();
    var playlist = (IReadOnlyList<QueueItem>)new List<QueueItem> { Item("a", 0), Item("b", 1) };

    var mock = BareSource(AudioSourceType.FilePlayer);
    mock.As<IPlayQueue>()
      .Setup(q => q.GetFullPlaylistAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(playlist);
    var source = mock.Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckQueueAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckQueueAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["QueueChanged", "QueueChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// Site :477 — the row's headline line.
  /// ⭐ The second assertion is the one that would otherwise be missed: the re-sent DTO must carry
  /// RdsRelevantChanged = true, exactly as the send that failed did. That flag is computed against
  /// _lastRadioState (:475), so an advanced cache would have made the retry — if there were one —
  /// arrive with the flag FALSE, and the Web RDS accumulator would have skipped it.
  /// </summary>
  [Fact]
  public async Task RadioStateDeltaIsReSentAfterACancelledBroadcastWithItsRdsFlagIntact()
  {
    var (svc, proxy) = CreateService();

    var mock = BareSource(AudioSourceType.Radio);
    var radio = mock.As<IRadioControl>();
    radio.SetupGet(r => r.CurrentFrequency).Returns(Frequency.FromMegahertz(105.1));
    radio.SetupGet(r => r.CurrentBand).Returns(RadioBand.FM);
    radio.SetupGet(r => r.FrequencyStep).Returns(Frequency.FromKilohertz(200));
    radio.SetupGet(r => r.SignalStrength).Returns(60);
    radio.SetupGet(r => r.RdsRadioText).Returns("Hotel California");
    var source = mock.Object;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => InvokeCheck(svc, "CheckRadioStateAsync", source, Cancelled()));

    await InvokeCheck(svc, "CheckRadioStateAsync", source, CancellationToken.None);

    Assert.Equal(2, proxy.Attempts);
    Assert.Equal(["RadioStateChanged", "RadioStateChanged"], proxy.Methods);

    var resent = Assert.IsType<RadioStateDto>(proxy.Payloads[1]);
    Assert.True(
      resent.RdsRelevantChanged,
      "the re-sent delta must still be RDS-relevant — it is the same delta the clients never got");
    svc.Dispose();
  }

  // ─── the regression half: the happy path must not have been broken ───────────────────────────

  /// <summary>
  /// ⚠ Without this, all six tests above would still pass against an implementation that simply
  /// never advanced the caches at all — which would re-broadcast every unchanged state twice a
  /// second forever, on a box where CPU churn is audible. Task 1 moves the assignment; it does not
  /// delete it.
  /// </summary>
  [Fact]
  public async Task AnUnchangedWorldIsBroadcastExactlyOnceWhenNothingFails()
  {
    var (svc, proxy) = CreateService();

    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);
    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);
    await InvokeCheck(svc, "CheckVolumeAsync", CancellationToken.None);

    Assert.Equal(1, proxy.Attempts);
    svc.Dispose();
  }

  /// <summary>The first poll must still establish the source baseline without broadcasting.</summary>
  [Fact]
  public async Task TheFirstSourcePollEstablishesTheBaselineWithoutBroadcasting()
  {
    var (svc, proxy) = CreateService();
    var radio = BareSource(AudioSourceType.Radio).Object;

    await InvokeCheck(svc, "CheckSourceChangedAsync", radio, CancellationToken.None);
    await InvokeCheck(svc, "CheckSourceChangedAsync", radio, CancellationToken.None);

    Assert.Equal(0, proxy.Attempts);
    svc.Dispose();
  }
}
```

⚠ **Builder: re-derive three shapes from the tree rather than trusting this file.**
`Frequency.FromMegahertz` / `FromKilohertz` (used at `IRadioControl.cs`'s own doc comments),
`RadioBand`'s member names, and `QueueItem`'s `required` members (`Id`, `Title`, `Artist`, `Album` —
`QueueItem.cs:24-51`). If `IRadioControl`'s default interface members (`SupportedBands`,
`RdsStationNameStable`, `RdsProgramId`) make Moq unhappy, set them explicitly; the values are
irrelevant as long as they are constant across the two ticks.

### 3.1 The RED step — run this against `main` BEFORE Task 1, and record the output verbatim

⭐ **Unlike `UI-12`, no new production seam is needed, so these tests can be written and run first.**
Add the file, do **not** apply Tasks 1 or 2, and run:

```bash
dotnet test tests/Radio.API.Tests -c Release --filter "FullyQualifiedName~AudioStateUpdateServiceCacheOrderingTests" > /tmp/ui13-red.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/ui13-red.log
```

**Expected: 6 failed, 2 passed.** The six site tests fail; the two regression tests pass on `main`
(they assert behaviour the swap preserves, which is what makes them controls rather than mutations).

**The RED failure, per site test.** The first assertion to fail is `Assert.Equal(2, proxy.Attempts)`,
and xUnit 2.9.3 renders an `int` mismatch as:

```
Assert.Equal() Failure: Values differ
Expected: 2
Actual:   1
```

`Actual: 1` **is the defect, stated numerically**: tick 1 attempted the send, tick 2 saw "no change"
against a state the client never received, and sent nothing. The `Assert.Equal(["…","…"], proxy.Methods)`
line never runs, because the count assertion fails first.

⚠ **`SourceChangedDeltaIsReSentAfterACancelledBroadcast` reaches the same assertion by a longer route**
— its earlier `Assert.Equal(0, proxy.Attempts)` after tick 1 passes on `main` and after the fix.

⚠ **Record the actual console text rather than pasting the block above into the PR.** The `Expected` /
`Actual` values are certain; the surrounding formatting is xUnit 2.9.3's and this plan has not run it.
`UI-12` §3.1 had to be corrected by its Builder for exactly this class of unverified detail.

### 3.2 Mutation matrix — run after Tasks 1–3 are green

A control must hold the **behaviour** fixed and vary only the **shape** (`UI-12`'s `M4` correction).
`C1` below is the control; `M1`–`M2` are mutations.

| # | Change | Must fail | Must still pass |
|---|---|---|---|
| `M1` | Revert **only** Task 1f (`_lastVolume` back above the send) | `VolumeDeltaIsReSentAfterACancelledBroadcast` — **and nothing else** | the other five site tests + both regression tests |
| `M1'` | Repeat `M1` independently for each of 1a–1e | that site's test only | all others |
| `M2` | Revert all of Task 1 | all six site tests | both regression tests |
| ~~`M3`~~ | ~~Delete the assignment entirely at one site (never advance the cache)~~ ⛔ **CANNOT BE RUN — see below** | — | — |
| `M3'` | **The compilable replacement.** Assign `null` instead of the value at one site | that site's `AnUnchanged…IsBroadcastExactlyOnce` | everything else |
| ⭐ `C1` | **THE CONTROL.** Keep the assignment after the send but move it *below* the `_logger.LogDebug` line at each site | ⛔ **nothing** — 8/8 green | all |

⚠ **`M1'` is the one that must not be skipped**, and it is the whole reason there are six tests rather
than one. It is what proves the row's "fix the shape, not the instance" requirement was met: if
reverting site 1a fails a test other than 1a's, the tests are not per-site and the shape guarantee is
an illusion.

⭐ **`C1` is what makes these behavioural rather than positional.** If `C1` reds, the tests are pinning
"the assignment is on the line immediately after the send" — a shape — instead of "the assignment
happens after the send" — the behaviour.

---

### ⛔ 3.3 CORRECTIONS FROM THE BUILDER'S ACTUAL RUN, 2026-09-09

**1. `M3` as written cannot be performed — the compiler stops it before any test does.** Deleting
`_lastVolume = currentVolume;` leaves the field read (`HasVolumeChanged(_lastVolume, …)`) but never
written, which is **`CS0649`**, and Release treats warnings as errors:

```
error CS0649: Field 'AudioStateUpdateService._lastVolume' is never assigned to, and will always have
its default value null
```

`M3'` (assign `null`) is the compilable equivalent and was run instead: **1 failed, 7 passed**,
failing exactly `AnUnchangedWorldIsBroadcastExactlyOnceWhenNothingFails`. Same class of finding as
`UI-14`, whose two non-compiling mutations were reported as measured rather than adjusted to fit.

**2. `C1` does not apply to site 1a**, and saying "at each site" implied six. `CheckSourceChangedAsync`'s
assignment already sits below its `LogInformation` — it trails the whole `if (!isFirstRun)` block — so
there is no distinct "below the log" position for it. `C1` was run on the five sites where the
distinction exists: **8/8 green**, so the tests are behavioural, not positional.

**3. ⭐ `M3` as scoped was too weak, and the gap it left was MEASURED.** One regression guard covered
`CheckVolumeAsync` alone. Setting `_lastPlaybackState = null` in place of its assignment left **all 8
tests GREEN** while re-broadcasting an unchanged state twice a second forever — a client storm on a
box where `CLAUDE.md` records CPU churn as audible. **Four more per-site guards were added**
(playback, now-playing, queue, radio state), each verified to fail on exactly its own mutation and
nothing else. `CheckSourceChangedAsync` needs none: never advancing `_lastActiveSourceType` keeps
`isFirstRun` true, so its own cancellation test stops throwing and fails unaided. Found by the
pre-merge reviewer, not by the plan.

---

## 4. Gates

```bash
dotnet clean RadioConsole.sln -c Release > /tmp/clean.log 2>&1; echo "exit=$?"
dotnet build RadioConsole.sln -c Release > /tmp/build.log 2>&1; echo "exit=$?"
grep -E "Warning\(s\)|Error\(s\)" /tmp/build.log   # expect "47 Warning(s)" / "0 Error(s)" — EQUALITY, not zero
dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

⛔ **Never pipe `dotnet test` into `tail`, `head` or `grep`** — `CLAUDE.md` records a measured run that
exited `0` with five tests failing, because a pipeline reports the *last* command's status. Redirect to
a file, echo the exit code, then read the file. Read the **per-project summary lines**
(`Passed! - Failed: 0, Passed: 141, …`), one per test project.

⛔ **Do not count warning lines.** `grep -cE "warning"` returns **94**, not 47, because MSBuild emits
each warning once per project-graph pass. Read the `Warning(s)` summary line. ⚠ And **build clean
before quoting a number** — an up-to-date project skips `CoreCompile` and re-emits none of its
warnings, so a warm build under-reports.

Known-failing on Windows and not a regression: four `SrcVariableResamplerTests` (`libsamplerate.so.0`),
`NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording` — all
`Category=Integration` or platform, all excluded by `build.yml:58`.

**Also run explicitly**, because this row edits the file they cover:

```bash
dotnet test tests/Radio.API.Tests -c Release --filter "FullyQualifiedName~AudioStateUpdateService" > /tmp/ui13-green.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!" /tmp/ui13-green.log
```

---

## 5. UAT

⚠ **Read §0.10 first: the change is not demonstrable on the box.** Before and after are identical on
every reachable path, so this is a no-regression check only.

1. Deploy: `./deploy/Deploy-ToLinux.ps1` (defaults are correct for `radio` since `OPS-1`).
2. Confirm both SHAs: `curl -s http://radio:5000/api/health/version` and `curl -s http://radio:5002/api/health/version`.
3. On the panel: tune the radio (frequency well, signal meter, STEREO badge, RDS marquee all update
   live) — exercises `RadioStateChanged`. Change volume — `VolumeChanged`. Switch source —
   `SourceChanged` + `PlaybackStateChanged` + `NowPlayingChanged`. Queue a file and skip a track —
   `QueueChanged`.
4. ⭐ **The one thing worth watching for**, because it is the failure mode a botched swap would cause:
   a panel that updates *once* and then freezes, or one that repaints continuously when nothing
   changed. Both would mean an assignment was dropped rather than moved.
5. Bounded log check only:
   `ssh mmack@radio "journalctl -u radio-api --since '-10min' --no-pager | grep -i 'Error broadcasting audio state'"` — expect **zero hits**.

⚠ Keep journal queries bounded — `CLAUDE.md` records log volume correlating with audible audio
distortion on this box.

---

## 6. Docs impact

- `docs/BUILDER_QUEUE.md` — mark `UI-13`, link this plan, and ⭐ **change the tier from 🟡 P2 to 🔵 P3**
  (§0.9). Also correct the row's site list to six and strike the `TimeProvider` note.
- ⭐ **`docs/queue/UI-13.md` — append a dated correction.** ⛔ Do **not** rewrite the row silently;
  append, as `AUD-17` and `UI-11` did. It must record: (a) `SendAsync` does not throw, only cancels
  (§0.1); (b) the trigger is host shutdown, so there is no live defect (§0.2); (c) `:249` was missing
  and `:454` is a dead field (§0.3); (d) the `TimeProvider` instruction does not apply (§0.6).
- **`design/DECISION-LOG.md`** — one entry is warranted, and it is the durable output of this row:
  *"SignalR broadcasts in the API cannot fault except by caller-token cancellation; cache-advance
  ordering in `AudioStateUpdateService` is therefore precautionary, and adding a backplane makes it
  load-bearing."*
- ~~**File the `_lastQueue` dead-field removal** as a new 🔵 row (§0.3). Not folded in.~~
  ⛔ **STRUCK — folded in and shipped. Do not file the row** (§0.3, struck note).
- No `design/FUTURE-WORK.md` or `design/INTEGRATIONS.md` change — nothing is stubbed and no integration
  surface moves.

---

## 7. What this row deliberately does not do

- ⛔ **Does not add a `TimeProvider` seam.** §0.6 — the defect reads no clock and the tests never enter
  the delay loop. Adding one would ship a production seam nothing drives.
- ⛔ **Does not touch `_lastFingerprintBroadcast` (`:910-916`).** It has the same textual shape but is a
  rate limiter, not a change-detection cache, and advancing it on a failed send is defensible (§0.6).
- ⛔ **Does not add a retry, a backoff, or an error counter.** The row's scope question 2 asks what
  should happen on a genuine send failure: the answer is that the next tick re-sends, which is already
  the behaviour Task 1 produces, and `ExecuteAsync:204` already backs off 1 s on any escape. A
  persistently failing client therefore costs *fewer* iterations per second than a healthy one, not
  more.
- ⛔ **Does not add a lock, a `volatile`, or any other synchronisation.** §0.5 — there is one writer.
  Adding one would imply a concurrency that does not exist, which is the kind of over-claiming comment
  `CLAUDE.md` § *Pre-Merge Review* exists to catch.
- ⛔ **Does not change `VisualizationBroadcastService`.** It was checked: `:106-130` broadcasts
  unconditionally with no change-detection cache, so it does not have this shape.
- ~~**Does not delete `_lastQueue`.** §0.3.~~ ⛔ **STRUCK — it does. See §0.3's struck note.**
