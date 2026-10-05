# PLAN — `UI-15` · `isFirstRun` really does conflate two states, and only a failed startup can reach it

Row: [`docs/queue/UI-15.md`](../../docs/queue/UI-15.md) · Branch: `fix/ui-15-first-run-conflates-no-source`
Planned 2026-09-09. Anchors measured against `main` at `ab72bef3`; `git diff main` for
`src/Radio.API/Services/AudioStateUpdateService.cs` and `tests/Radio.API.Tests/Services/` is **empty**,
so every line number below is a `main` line number.

⚠ **Tree discipline.** A Builder is mid-cycle in this checkout and a second Planner is live. This plan
was written without a single `git checkout` / `switch` / `branch` / commit, and it added exactly one
file — this one. The Builder executing it must branch only when it owns the tree
(`CLAUDE.md` § *Two agents must never share one working tree*).

---

## 0. Read this before Task 1

### 0.1 `C-601` — the mechanism is real, and here it is exactly

`AudioStateUpdateService.cs:282-303` on `main`, quoted in full because the row's excerpt predates
`UI-13`'s comment:

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

`null` carries two meanings. It is the sentinel for *"this service has not observed anything yet"*,
and it is a legitimate observed value — `IAudioSourceManager.ActiveSource` is `IAudioSource?`
(`IAudioSourceManager.cs:13`) and `activeSource?.Type` handles that on purpose. So **any
`null → non-null` transition takes the `isFirstRun` branch and is suppressed**, and because
`_lastActiveSourceType` is what `isFirstRun` is computed from, nothing downstream ever notices.

⭐ **The same mechanism is already written down in-tree, by `UI-13`'s Builder, without being
recognised as this row.** `AudioStateUpdateServiceCacheOrderingTests.cs:305-306`:

> *"CheckSourceChangedAsync does not [need a per-site guard], because never advancing
> `_lastActiveSourceType` keeps `isFirstRun` true forever."*

That sentence is `UI-15` stated as a property of the test suite. It is correct, and it is corroboration
from an independent author.

### 0.2 ⛔ `C-602` — THE ROW'S HEADLINE IS NOT ESTABLISHED. 36 consecutive lifetimes, zero firings

The row says *"⭐ THIS IS A LIVE DEFECT, unlike `UI-13`"*. **Measured on the appliance 2026-09-09, that
is not established, and the evidence points the other way.**

The defect needs the service to complete **at least one poll while `ActiveSource` is null**, and then a
source to appear. On a healthy boot that window does not exist, because the hosted services start
**sequentially** — `AudioEngineInitializationService` is registered at `Program.cs:128`,
`AudioStateUpdateService` at `:134`, and the former is a plain `IHostedService` whose `StartAsync`
(`AudioEngineInitializationService.cs:107`) **awaits `ActivatePersistedSourceAsync` at `:171` before
returning**. `Program.cs:142` is `PostConfigure<HostOptions>(_ => { })`, a no-op, so
`ServicesStartConcurrently` keeps its `false` default.

Measured margin, from `/opt/radio-console/logs/radio-20260909.txt`:

| | lifetime A | lifetime B |
|---|---|---|
| `Switching from source none to SDR Radio (RTL-SDR) ("Radio")` | `08:11:32.996` | `11:14:11.122` |
| `AudioStateUpdateService starting with update interval: 500ms` | `08:11:35.394` | `11:14:13.732` |
| **margin** | **2.40 s** | **2.61 s** |

The source is active roughly 2.5 seconds *before* the poller exists. The first poll therefore observes
`"Radio"` directly — a genuine first observation, correctly suppressed.

**Base rate across every log the box holds (7 files, 2026-09-03 → 09-09):**

| Log | switches | `none → X` | `Broadcast SourceChanged` | `Failed to initialize audio engine` | `Failed to activate persisted source` | `Source … failed to initialize` |
|---|---|---|---|---|---|---|
| 09-03 | 12 | 12 | 0 | 0 | 0 | 0 |
| 09-04 | 20 | 14 | 6 | 0 | 0 | 0 |
| 09-05 | 5 | 5 | 0 | 0 | 0 | 0 |
| 09-06 | 7 | 2 | 5 | 0 | 0 | 0 |
| 09-07 | 2 | 0 | 2 | 0 | 0 | 0 |
| 09-08 | 1 | 1 | 0 | 0 | 0 | 0 |
| 09-09 | 2 | 2 | 0 | 0 | 0 | 0 |

⭐ **`switches − (none → X) == broadcasts`, exactly, on all seven days.** Every `none → X` produced no
broadcast; every other switch produced exactly one. Since `ActiveSource` can be null at most once per
process (§0.3), each `none → X` is one process lifetime: **36 lifetimes, 13 real post-startup source
changes, 13 broadcasts.**

And the ordering was checked per lifetime, not just in aggregate. Reducing each log to the marker
sequence `I` = `Initializing audio engine`, `N` = `Switching from source none to`,
`S` = `AudioStateUpdateService starting`:

```
radio-20260903.txt     INSINSINSINSINSINSINSINSINSINSINSINS
radio-20260904.txt     INSINSINSINSINSINSINSINSINSINSINSINSINSINS
radio-20260905.txt     INSINSINSINSINS          (re-run with grep -a; the first pass called it binary)
radio-20260906.txt     INSINS
radio-20260907.txt                              (no restart that day)
radio-20260908.txt     INS
radio-20260909.txt     INSINS
```

**36 lifetimes, perfect `I → N → S` alternation, not one `S … N`.** An `S … N` — a source appearing
after the poller started — is the log signature of this defect, and it has never occurred in the
retained history.

⛔ **So `UI-15` is latent, not live.** It is *not* unreachable the way `UI-13` was — see §0.4 — but the
row's framing (*"needs only that the box can idle with no source, which is the default state before any
selection"*) is **false**: the box never idles with no source, because startup activation completes
before the poller exists.

### 0.3 ⛔ `C-603` — `source → none → source` is IMPOSSIBLE, not merely unproven

The row lists this as *"plausible but unproven"*. It is now **disproven.**

`AudioManager._activeSource` is assigned in exactly one statement in the whole file —
`AudioManager.cs:235`, `_activeSource = source;` — and every other of its eighteen occurrences is a
read. The enclosing method guards its parameter:

```csharp
  public async Task SwitchSourceAsync(IAudioSource source, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    ArgumentNullException.ThrowIfNull(source);        // AudioManager.cs:176
```

`AudioManager` is the only implementation of `IAudioManager` (`AudioManager.cs:18`), registered as a
singleton at `AudioServiceExtensions.cs:213`. **Therefore `ActiveSource` is null from construction
until the first successful switch, and never again.** The recurrence the row describes does not exist,
and the row's "Why it matters" should not be read as describing a repeating fault.

### 0.4 `C-604` — what *does* reach it: six startup failure paths, none of which has fired

Reachability reduces to one question: **can startup finish without an active source, while
`AudioStateUpdateService` still polls?** Yes, by construction, on six paths — and the answer to the
second half is unconditional, because `IsEnabled` is gated only on `_audioManager != null`
(`AudioStateUpdateService.cs:139-143`) and `IAudioManager` is registered unconditionally
(`AudioServiceExtensions.cs:213`), independent of engine health.

| # | Path | Code | Logs |
|---|---|---|---|
| 1 | Engine init / device enumeration throws before `:171` | `AudioEngineInitializationService.cs:182-186` | `Failed to initialize audio engine`, then *"Don't throw - allow the application to start even if audio fails"* |
| 2 | `ActivatePersistedSourceAsync` throws | `:426-429` | `Failed to activate persisted source, UI will handle default selection` |
| 3 | Source type unsupported | `AudioManager.cs:374-375` | `Source type {SourceType} is not supported` |
| 4 | Factory threw | `AudioManager.cs:377-381` | `Failed to create source for type: {SourceType}` |
| 5 | Factory returned null | `AudioManager.cs:383-387` | `Source type {SourceType} is not supported` |
| 6 | ⭐ **Source failed to initialize** — `InitializeAsync` left it in `AudioSourceState.Error` | `AudioManager.cs:395-399` | `Source {SourceName} failed to initialize` (**WRN**) |

Path 6 is the realistic one on this appliance: `Radio` needs the RTL-SDR device, `Bluetooth` needs its
PipeWire capture node, and `MEMORY.md` already records a live bug where the BT source is switched to
with no capture node present. It has simply not happened during the retained week.

⚠ **Paths 3-6 are silent to the caller.** `ActivatePersistedSourceAsync` ignores the return value and
logs success regardless:

```csharp
      await _audioManager.GetOrCreateSourceAsync(sourceType, switchToSource: true, cancellationToken);
      _logger.LogInformation("Source {SourceType} activated on startup", sourceType);   // :424
```

So the one line an operator would use to confirm startup activation **cannot distinguish success from
silent failure**, and it is exactly the failure that makes `UI-15` reachable. See §7 — file it, do not
fold it in. (Measured: `activated on startup` and `none → X` counts match on all seven days, so
`GetOrCreateSourceAsync` returned non-null every time in this sample.)

⭐ **The consequence worth stating plainly:** this defect fires precisely when the appliance has
already failed to bring audio up at boot — the moment a user is most likely to be reaching for the knob
trying to get sound out of it. That is the argument for fixing it despite §0.2, not the row's
(incorrect) claim that it fires on every boot.

### 0.5 ⭐ `C-605` — the symptom, named: it is a PHYSICAL-KNOB bug, not a mouse bug

The row asks whether the user who made the switch sees it, or only other clients. **Neither framing is
right.**

`AudioStateHubService` is a **singleton** in `radio-web` (`Radio.Web/Program.cs:411`; `AudioStateStore`
likewise at `:456`), and component subscribers attach per circuit. So the local notify at
`MainLayout.razor:802` —

```csharp
      var success = await SourcesApi.SwitchSourceAsync(source.Type);
      if (success)
      {
        _selectedSourceId = newSourceId;
        Logger.LogInformation("Switched to source: {SourceType}", source.Type);
        await HubService.NotifySourceChangedAsync();          // :802
      }
```

— fans out to **every circuit in the `radio-web` process**, i.e. every browser on the box. ⛔ **A source
change made from the Web UI is therefore fully compensated for all clients, and shows no symptom at
all.**

The symptom needs a source change that did **not** come from a Web-UI click. There are three, and
`AudioStateUpdateService.cs:293-294` is the **only** sender of `"SourceChanged"` anywhere in `src/`, so
each depends entirely on the suppressed broadcast:

| Trigger | Reaches `SwitchSourceAsync` via | Broadcasts anything itself |
|---|---|---|
| ⭐ **Rotary encoder (the console's knob)** | `SourceSelectorService.cs:207`, `:223` | no — only `EncoderHudChanged` |
| **Bluetooth auto-switch on phone connect** | `BluetoothAutoSwitchService.cs:100`, `:168` | no |
| Any REST client that is not this UI | `SourcesController.cs:286` | no |

**What is then stale, with the surface and the wrong content named:**

- ⭐ **`MainLayout` — the source selector highlight and the radio panel.** `LoadSourcesAsync()` has
  exactly **two** call sites: `:375` (initialisation) and `:1187` (inside `OnSourceChanged`). The 1 s
  timer at `:592-610` updates the clock and system stats only, and `OnPlaybackStateChanged` /
  `OnNowPlayingChanged` do not call it. **So `_selectedSourceId` keeps the previous source highlighted
  indefinitely, and `RadioPanelToggle.Show/HideRadioPanelAsync` (`:1190-1198`) leaves the
  RadioControlPanel open after you leave Radio, or closed after you arrive on it.** There is no
  fallback path of any kind.
- **`QueueHistoryPanel` — the tab and the queue.** `SetDefaultTabForSourceAsync()` is called at `:413`
  (init) and `:755` (`OnSourceChanged`) only, so `_activeTab` and `_isRadioActive` are stale; the 30 s
  `PeriodicHistoryRefreshAsync` (`:425-447`) refreshes history **only when the History tab is already
  active** and never re-runs the tab selection.
- **`SystemConfigPage._eventSources`** — refreshed only by `OnHubSourceChangedAsync` (`:2270-2277`);
  no fallback poll.
- **`NowPlayingPanel._sourceGainOffsets`** — loaded at init and in `OnSourceChanged` only.

**What self-corrects, so do not claim it as a symptom:** title / artist / album art. On the very same
tick, `CheckNowPlayingAsync` and `CheckPlaybackStateAsync` **do** broadcast, because their comparisons
are not first-run-suppressed — `HasNowPlayingChanged` returns `true` whenever either side is null
(`:585-588`) and otherwise on `previous.SourceType != current.SourceType` (`:592`), and
`BuildNowPlayingDto(null)` yields `SourceType = "None"` (`:750`). ⭐ **So the source change *is*
reported to clients; it is reported by the two events that carry a payload, and withheld from the one
event four surfaces use to re-fetch.** That asymmetry is the whole of the user-visible harm.

⚠ **Reconnect does not compensate either.** `AudioStateHubService`'s `Reconnected` handler
(`:307-322`) re-subscribes to the `RadioState` and `Queue` groups and re-fetches nothing; `Closed`
(`:273-295`) only logs.

### 0.6 ⚠ `C-606` — "with nothing logged" is half wrong, and the true statement is the diagnostic

The row says the defect fires *"with nothing logged"*. The **suppression** logs nothing — the
`LogInformation` sits inside the suppressed branch — but the **switch** does:

```csharp
      _logger.LogInformation(
        "Switching from source {OldSource} to {NewSource} ({NewSourceType})",
        _activeSource?.Name ?? "none",                       // AudioManager.cs:188
```

which renders literally as `Switching from source none to …`. That is `Information`, so per
`CLAUDE.md` § `LOG-11` it goes to the **file sink** (`/opt/radio-console/logs/radio-*.txt`), not the
journal. It is what made §0.2 measurable, and it is the standing check:

```bash
# On the box. Bounded file reads, not journald — CLAUDE.md warns log volume is audible here.
F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1)
grep -n "Switching from source none to\|AudioStateUpdateService starting" $F
```

⭐ **Healthy is `none → X` BEFORE `AudioStateUpdateService starting`, in every lifetime. A `none → X`
that appears AFTER it is this defect firing**, and there will be no `Broadcast SourceChanged` for it.

### 0.7 ⛔ `C-607` — `TimeProvider` would be a category error here too, for a different reason

`UI-13` reached the same verdict; **do not inherit it, because the derivation is not the same.**
`UI-13`'s defect was the ordering of two statements. `UI-15`'s is a boolean derived from a nullable
field. Neither reads a clock — `CheckSourceChangedAsync` (`:282-303`) contains no `DateTime`, no
`TimeProvider`, no `Task.Delay` — and the transition under test is driven by the **world**
(`ActiveSource`), not by elapsed time. The tests invoke the method directly by reflection and never
enter `ExecuteAsync`, so its `Task.Delay` is never reached.

⛔ **Adding a `TimeProvider` seam would ship production code nothing drives.** `CLAUDE.md` § *Test
Timing* is satisfied by construction here: every assertion below runs after an awaited call completes,
so each is a fact about control flow, and no assertion sits on either side of a wall clock.

### 0.8 `C-608` — `UI-13` changed the comment at this site, not the behaviour. Anchors restated

The row was filed against pre-`UI-13` line numbers and the coordinator flagged the anchors as possibly
stale. **They are stale as numbers and sound as behaviour.**

`UI-13` shipped as `f141df2f` (#634). At this site it added the four-line comment now at `:298-300` and
**changed nothing executable** — the assignment already trailed the whole `if (!isFirstRun)` block, which
is exactly why `UI-13`'s own record notes its `C1` mutation *"does not apply to site `:249`"*.

| Was (row / `UI-13` plan) | Is (`main` at `ab72bef3`) |
|---|---|
| `:249` — `_lastActiveSourceType` assignment | **`:301`** |
| — | method `CheckSourceChangedAsync` **`:282-303`** |
| — | `isFirstRun` **`:289`**, send **`:293-294`**, log **`:295`** |
| — | cache-field block **`:95-101`**, `UI-13` precondition comment **`:52-93`** |

⛔ **Do not edit `:52-93`.** That block, ADR-034, and the `<remarks>` of
`AudioStateUpdateServiceCacheOrderingTests.cs` are the three deliberate homes of `UI-13`'s deliverable.
This row adds to them; it does not rewrite them.

### 0.9 What must not regress

1. `AudioStateUpdateServiceCacheOrderingTests.TheFirstSourcePollEstablishesTheBaselineWithoutBroadcasting`
   — the genuine first run stays silent. This row must not change it, and the row says so.
2. `AudioStateUpdateServiceCacheOrderingTests.SourceChangedDeltaIsReSentAfterACancelledBroadcast`
   — `UI-13`'s ordering contract: the assignment at `:301` stays **after** the send.
3. No spurious `SourceChanged` at startup. That is what the suppression is *for*, and §0.2 shows it is
   doing that job correctly 36 times out of 36.
4. Release build **47 warnings, 0 errors** — equality with the baseline.

### 0.10 ⚖ The recommendation, and the case against

**Recommendation: ship it, and correct the row's tier and headline.**

The case for, given §0.2 says it has never fired:

- The change is four lines and **cannot affect the healthy path**, which is provable rather than
  hoped: on a healthy boot the first poll observes a non-null source, and that branch is byte-for-byte
  unchanged.
- The failure mode arrives silently, at the worst possible moment (§0.4), on the console's primary
  physical input (§0.5), with no fallback on three of four surfaces.
- The mechanism is reachable by construction through six unguarded paths. This is **not** `UI-13`,
  which was reachable only through an exception that means *"stop"*. Closing `UI-15` as "documented"
  would be documenting a hole that a single hardware hiccup opens.

The case against, stated honestly so the owner can weigh it:

- It has not fired in 36 lifetimes. A row that has never fired is, by this repo's own standard, a
  candidate for closing with the correction appended — and `UI-13` was kept on exactly that reasoning
  one day ago, so keeping this one is not automatically consistent.
- It is P2 in the queue on the strength of *"unlike `UI-13` this is a LIVE defect"*, which §0.2
  refutes. **Re-tier P2 → P3** whatever else is decided.

⛔ **Do not fix it by broadcasting on the true first run.** The row forbids it, §0.9 item 3 says why, and
mutation `M2` proves the tests catch it.

### 0.11 Auto-merge

**Yes — auto-mergeable** on the four gates in `personal/AGENTS.md` § *Auto-merge policy*. No hardware,
no live-audio path, no migration, no secrets. The UAT limb is satisfied by the RED measurement plus the
mutation matrix; see §4 for why a live UAT is possible but not worth its cost.

---

## 1. Task 1 — give "never observed" its own field

Two edits in `src/Radio.API/Services/AudioStateUpdateService.cs`. **Nothing else in the file.**

**1a.** Add the latch immediately after the cache-field block, at `:101` (after
`private string? _lastActiveSourceType;`, before the `_currentMatchId` comment at `:103`):

```csharp
  // ⚠ SEPARATE FROM _lastActiveSourceType ON PURPOSE (UI-15). null is a legitimate VALUE of that
  // field, not just its initial state: IAudioSourceManager.ActiveSource is IAudioSource? and
  // CheckSourceChangedAsync's `activeSource?.Type` handles that deliberately. While null also served
  // as the "nothing observed yet" sentinel, every null → non-null transition was suppressed as though
  // it were the first poll, so a real source change was never broadcast and nothing logged it.
  //
  // Reachable only when startup finishes with no active source — AudioEngineInitializationService
  // awaits ActivatePersistedSourceAsync before this service starts polling (measured 2.4-2.6 s of
  // margin, 36 lifetimes), so on a healthy boot the first poll already sees a source and the
  // suppression below is doing its intended job. It is the six unguarded failure paths in
  // AudioEngineInitializationService and AudioManager.GetOrCreateSourceAsync that reach it.
  private bool _hasObservedSource;
```

**1b.** Replace the body of `CheckSourceChangedAsync` (`:282-303`) with:

```csharp
  private async Task CheckSourceChangedAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    var currentSourceType = activeSource?.Type.ToString();

    if (currentSourceType != _lastActiveSourceType)
    {
      // Skip the broadcast on the FIRST POLL ONLY. The baseline has to be established from whatever
      // the world already looks like, and reporting that as a change would put a spurious
      // SourceChanged on every client at startup. ⚠ Deliberately NOT "_lastActiveSourceType == null"
      // (UI-15): null is a real observable value, so that test also suppressed a genuine
      // no-source → source change.
      if (_hasObservedSource)
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

    // ⚠⚠ OUTSIDE the branch above, and that placement IS the fix. A first poll that observes "no
    // source" leaves currentSourceType == _lastActiveSourceType == null, so the branch is not
    // entered — a latch set inside it would not count that poll as an observation, and the next one
    // would still look like a first run. It is also the LAST statement, so a send that does not
    // complete leaves the baseline and the latch equally untouched, which is the UI-13 contract.
    _hasObservedSource = true;
  }
```

**Why a `bool` and not a sentinel string.** The alternative is
`private const string NeverObserved = " never-observed";` with `_lastActiveSourceType` initialised
to it. It is behaviourally identical — mutation `C1` in §2.2 proves that by running it — and worse to
read: it puts a value in `_lastActiveSourceType` that is not a source type, so every future reader has
to know the magic constant, and it makes the outer `if` fire on the first poll even when there is
genuinely nothing to compare. The `bool` says what it means.

**Why not seed the baseline in `ExecuteAsync` instead.** Reading `_audioManager.ActiveSource` once
before the loop and dropping `isFirstRun` altogether looks tidier and is not: the seed would run at
`t≈0` and, in exactly the failure case this row is about, would read `null` and change nothing. It
would also move the logic into a method the reflection-driven tests deliberately never enter (§0.7).

---

## 2. Task 2 — the tests, and they must fail first

**New file** `tests/Radio.API.Tests/Services/AudioStateUpdateServiceSourceObservationTests.cs`.

⚠ **Do NOT add these to `AudioStateUpdateServiceCacheOrderingTests.cs`, even though it already carries
the harness and the row suggests reusing it.** That file's class-level `<remarks>` opens by stating its
tests do **not** demonstrate a live defect — a sentence `UI-13` shipped on purpose, as one of the three
homes of its deliverable. These tests assert the opposite about a different field. Two contradictory
remarks in one file is precisely the failure mode this repo keeps paying for. The cost is a duplicated
~45-line harness; that is cheaper than a diluted claim, and it means this row touches one fewer file in
a shared tree. **A Builder that disagrees may merge them instead — but then it must amend that remark
honestly, not leave it standing.**

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
/// `UI-15` — CheckSourceChangedAsync must distinguish "this service has not observed anything yet"
/// from "there is currently no active source". null is a legitimate value of
/// IAudioSourceManager.ActiveSource, so it cannot also be the never-observed sentinel.
/// </summary>
/// <remarks>
/// ⚠ HOW THIS DIFFERS FROM AudioStateUpdateServiceCacheOrderingTests, whose remarks say its tests do
/// NOT show a live defect. These do — by construction. On `main`, a service that has polled at least
/// once while ActiveSource was null suppresses the next real source change forever after, because
/// `isFirstRun` is computed FROM the cache it is meant to gate.
///
/// ⚠ THAT SAID, THE ROW'S "LIVE DEFECT" HEADLINE IS STILL AN OVERCLAIM, AND THE PLAN SAYS SO.
/// Reachability needs startup to finish with no active source. It does not on a healthy boot:
/// AudioEngineInitializationService (Program.cs:128) awaits ActivatePersistedSourceAsync before
/// AudioStateUpdateService (Program.cs:134) starts, measured at 2.4-2.6 s of margin across 36
/// consecutive lifetimes in the appliance's retained logs, with zero firings. What reaches it is the
/// six unguarded failure paths listed in the plan's §0.4 — chiefly a source that fails to initialize
/// (AudioManager.cs:395-399), whose null return ActivatePersistedSourceAsync discards while logging
/// success anyway.
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK — CLAUDE.md § *Test Timing*. Each Check*Async is invoked
/// directly and awaited to completion before anything is asserted. ⛔ Do NOT add a TimeProvider seam:
/// this method reads no clock, and the transition under test is driven by the world, not by elapsed
/// time (plan §0.7).
/// </remarks>
public class AudioStateUpdateServiceSourceObservationTests
{
  /// <summary>Records every attempted send. No failure modelling is needed here — unlike the
  /// UI-13 tests, none of these cancels a token.</summary>
  private sealed class RecordingClientProxy : IClientProxy
  {
    public List<string> Methods { get; } = [];

    public int Attempts => Methods.Count;

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
      Methods.Add(method);
      cancellationToken.ThrowIfCancellationRequested();
      return Task.CompletedTask;
    }
  }

  private static (AudioStateUpdateService Service, RecordingClientProxy Proxy) CreateService()
  {
    var proxy = new RecordingClientProxy();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy);
    clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);

    var hubContext = new Mock<IHubContext<AudioStateHub>>();
    hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

    var audioManager = new Mock<IAudioManager>();
    audioManager.SetupGet(m => m.MasterVolume).Returns(0.42f);
    audioManager.SetupGet(m => m.IsMuted).Returns(false);
    audioManager.SetupGet(m => m.Balance).Returns(0.0f);

    var collection = new ServiceCollection();
    collection.AddSingleton(audioManager.Object);

    var service = new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContext.Object,
      collection.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

    return (service, proxy);
  }

  private static Task InvokeCheck(AudioStateUpdateService svc, IAudioSource? source, CancellationToken ct)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      "CheckSourceChangedAsync", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(m);
    return (Task)m!.Invoke(svc, [source, ct])!;
  }

  private static IAudioSource Source(AudioSourceType type)
  {
    var mock = new Mock<IAudioSource>();
    mock.SetupGet(s => s.Id).Returns("src-1");
    mock.SetupGet(s => s.Name).Returns("Test Source");
    mock.SetupGet(s => s.Type).Returns(type);
    mock.SetupGet(s => s.Category).Returns(AudioSourceCategory.Primary);
    mock.SetupGet(s => s.State).Returns(AudioSourceState.Playing);
    mock.SetupGet(s => s.Volume).Returns(1.0f);
    return mock.Object;
  }

  /// <summary>
  /// ⭐ THE HEADLINE. Startup produced no source, so the first poll observes null — correctly silent,
  /// there is nothing to report. The source that appears next (the knob, a phone connecting, a REST
  /// call) IS a change and must reach every panel.
  /// On `main`, tick 2 takes the isFirstRun branch because _lastActiveSourceType is still null, and
  /// sends nothing: Attempts is 0, not 1.
  /// </summary>
  [Fact]
  public async Task TheFirstSourceToAppearAfterAnIdleStartIsBroadcast()
  {
    var (svc, proxy) = CreateService();

    await InvokeCheck(svc, null, CancellationToken.None);
    Assert.Equal(0, proxy.Attempts);

    await InvokeCheck(svc, Source(AudioSourceType.Radio), CancellationToken.None);

    Assert.Equal(1, proxy.Attempts);
    Assert.Equal(["SourceChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// The same defect under the shape production actually has: the service polls at 500 ms, so a
  /// startup that never activated a source yields MANY idle polls, not one. Guards against a latch
  /// that only survives a single pass.
  /// </summary>
  [Fact]
  public async Task ManyIdlePollsStillLeaveTheNextRealSourceBroadcastable()
  {
    var (svc, proxy) = CreateService();

    await InvokeCheck(svc, null, CancellationToken.None);
    await InvokeCheck(svc, null, CancellationToken.None);
    await InvokeCheck(svc, null, CancellationToken.None);
    Assert.Equal(0, proxy.Attempts);

    await InvokeCheck(svc, Source(AudioSourceType.Bluetooth), CancellationToken.None);

    Assert.Equal(1, proxy.Attempts);
    Assert.Equal(["SourceChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// ⚠ GREEN ON `main` AND STAYS GREEN — this is a characterization test, not part of the RED
  /// measurement. It pins the reverse transition, which is UNREACHABLE TODAY: AudioManager assigns
  /// _activeSource in exactly one statement (AudioManager.cs:235) from a parameter guarded by
  /// ArgumentNullException.ThrowIfNull (:176), so ActiveSource can never return to null once set.
  /// It is here because this row makes null an ordinary value, and if that guarantee ever changes
  /// the transition must broadcast. It also fails under mutations M2 and M4, which is what earns it
  /// its place rather than the speculation above.
  /// </summary>
  [Fact]
  public async Task ASourceDisappearingIsBroadcastToo()
  {
    var (svc, proxy) = CreateService();

    await InvokeCheck(svc, Source(AudioSourceType.Radio), CancellationToken.None);
    Assert.Equal(0, proxy.Attempts);

    await InvokeCheck(svc, null, CancellationToken.None);

    Assert.Equal(1, proxy.Attempts);
    Assert.Equal(["SourceChanged"], proxy.Methods);
    svc.Dispose();
  }
}
```

⚠ **No guard test is duplicated from `UI-13`'s file, deliberately.** The two branches of the
suppression are each already guarded: `TheFirstSourceToAppearAfterAnIdleStartIsBroadcast`'s first
assertion guards the null-start first poll, and `UI-13`'s
`TheFirstSourcePollEstablishesTheBaselineWithoutBroadcasting` guards the non-null-start first poll.
Mutation `M2` in §2.2 proves the pair is sufficient.

### 2.1 The RED step — run this against `main` BEFORE Task 1, and record the output verbatim

Add **only** the new test file. Make **no** production change. Then:

```bash
cd /d/prj/rtest/rtest
dotnet test tests/Radio.API.Tests/Radio.API.Tests.csproj -c Release > /tmp/ui15-red.log 2>&1; echo "exit=$?"
```

⛔ **Never pipe `dotnet test` into `tail` / `head` / `grep`** — `CLAUDE.md` records a run that exited
`0` with five tests failing, because the shell reports the last command's status. Redirect, check
`$?`, then read the file.

**Expected RED: 2 failed, 1 passed** in the new class. Both failures render identically —

```
Assert.Equal() Failure: Values differ
Expected: 1
Actual:   0
```

⭐ **`Actual: 0` is the defect stated numerically**: the transition happened, the branch was taken as a
first run, and nothing went on the wire. The rendering is asserted with confidence because it is the
same `Assert.Equal(int, int)` overload whose output `UI-13` recorded verbatim one day earlier
(`Expected: 2 / Actual: 1`); only the operands differ.

`ASourceDisappearingIsBroadcastToo` **passes on `main`** — say so in the record rather than implying
three failures.

Whole-project count: `Radio.API.Tests` goes **440 → 443**.

### 2.2 Mutation matrix — run after Task 1 and Task 2 are green

Each mutation is applied alone and reverted before the next. Run the full `Radio.API.Tests` project each
time and record the actual result, not the predicted one.

| # | Mutation | Expected |
|---|---|---|
| `M1` | **Revert the fix**: restore `var isFirstRun = _lastActiveSourceType == null;` / `if (!isFirstRun)`, **and delete the `_hasObservedSource` field** | The two new tests fail on `Expected: 1 / Actual: 0`; `ASourceDisappearingIsBroadcastToo` and all 8 `UI-13` tests pass |
| `M2` | **The over-fix the row forbids**: delete the suppression, always send | ⭐ `UI-13`'s `TheFirstSourcePollEstablishesTheBaselineWithoutBroadcasting` fails (`Expected: 0 / Actual: 1`), `ASourceDisappearingIsBroadcastToo` fails, `SourceChangedDeltaIsReSentAfterACancelledBroadcast` fails. The two new tests **pass** — which is exactly why the `UI-13` test must not be deleted |
| `M3` | ⭐ **The plausible wrong implementation**: move `_hasObservedSource = true;` INSIDE the `if (currentSourceType != _lastActiveSourceType)` block | The two new tests fail on `Expected: 1 / Actual: 0`; everything else green. **This is the highest-value mutation** — it proves the tests pin the latch's *placement*, which is the entire subtlety of the fix |
| `M4` | Move `_hasObservedSource = true;` to the TOP of the method, before the `if` | `UI-13`'s first-poll test fails (`Expected: 0 / Actual: 1`) and `ASourceDisappearingIsBroadcastToo` fails; the two new tests pass |
| `M5` | Delete the assignment, keep the field | ⛔ **CANNOT BE RUN.** `CS0649` (*never assigned to*) + warnings-as-errors in Release: the **compiler** is the gate, before any test. Same shape as `UI-13`'s `M3` and `UI-14`'s two non-compiling mutations. Record it as measured, do not adjust it to fit |
| `C1` | **Control — the rejected alternative, implemented for real.** Delete `_hasObservedSource` entirely; add `private const string NeverObserved = " never-observed";`, initialise `private string? _lastActiveSourceType = NeverObserved;`, and gate the send on `if (!ReferenceEquals(_lastActiveSourceType, NeverObserved))` | ⭐ **11/11 GREEN.** Proves the tests pin the *contract* — null is an ordinary value, the first observation is silent — and not the chosen mechanism. It also confirms the alternative was rejected on readability (§1), not on correctness |

⚠ `M1` **must** delete the field as well as revert the logic. Leaving a field that is assigned and never
read is `CS0414`, which fails the Release build before any test runs — the mirror image of `M5`.

⚠ Note what `M2` shows and do not skip it: **the two new tests alone do not catch the forbidden
over-fix.** The gate is the pair — new tests plus `UI-13`'s untouched first-poll test. `UI-13`'s
pre-merge review found a guard covering one site of six; this is the same lesson applied before the
review rather than after it.

---

## 3. Gates

```bash
cd /d/prj/rtest/rtest

# Build — the gate is EQUALITY with the baseline, 47 warnings / 0 errors.
dotnet build RadioConsole.sln -c Release > /tmp/ui15-build.log 2>&1; echo "exit=$?"
grep -E "^ +[0-9]+ (Warning|Error)\(s\)" /tmp/ui15-build.log | tail -2

# Full suite.
dotnet test RadioConsole.sln -c Release > /tmp/ui15-test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/ui15-test.log
```

⛔ **Do NOT count warnings with `grep -cE "warning"`.** It returns **94**, not 47 — MSBuild emits each
warning once per project-graph pass. `UI-14`'s Builder measured this, and the same defective recipe is
still sitting in `OPS-2:536`. Read the **summary** lines the command above extracts, and if the number
looks wrong, build `main` too rather than trusting any written figure.

Read the **per-project** summary lines, one per test project. Known-failing on Windows and **not**
regressions: four `SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`),
`NwsObservationIntegrationTests.RealNwsCall_*` and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording` (both
`Category=Integration`, live network, CI-excluded by `build.yml:58`).

---

## 4. UAT

**Default: none, and the PR should say so.** On every path this appliance has actually taken in the
retained week (§0.2), before and after are byte-identical: the first poll sees a non-null source and
takes the same branch either way. **No observation on the box can distinguish them** without first
forcing the failure state.

**An optional live UAT does exist, unlike `UI-13`'s.** ⚠ It works by deliberately breaking startup
audio, so it is the owner's call and the recommendation is to decline:

1. Set the persisted source to one that cannot initialize on this box (`AudioPreferences:CurrentSource`;
   note the SQLite config store outranks both JSON layers — `MEMORY.md` § `AUD-1`).
2. `systemctl restart radio-api`, then confirm the reachable state in the file sink: a
   `Source … failed to initialize` warning, **no** `Switching from source none to` line before
   `AudioStateUpdateService starting`.
3. With a browser open on the panel, change the source **with the rotary knob** — not by clicking,
   which would locally notify and mask the whole thing (§0.5).
4. Before: the source highlight and the radio panel do not move. After: they do.
5. **Revert the preference and restart.**

⚠ Steps 1 and 5 touch box configuration on a shared appliance while a Builder may be deploying. Do not
run this without saying so first.

---

## 5. Docs impact

- **`docs/queue/UI-15.md`** — append a `⛔ PLANNED 2026-09-09` section carrying §0.2 (the 36-lifetime
  measurement and the base-rate table), §0.3 (`source → none → source` is impossible), §0.5 (it is a
  knob bug, and a UI click shows no symptom), §0.6 (the `Switching from source none to` diagnostic),
  and the **P2 → P3** re-tier with the reason.
- **`docs/BUILDER_QUEUE.md`** — row status, the last-updated banner, and ⛔ **strike the row's
  `⭐ ... unlike UI-13 it is a LIVE defect` claim in the banner text**, which §0.2 refutes.
- ⛔ **No ADR.** `UI-13` earned ADR-034 because its deliverable *was* a precondition. This row's
  deliverable is a code change that restores the documented intent of an existing rule; the reasoning
  lives in the field comment (Task 1a) and the test `<remarks>`. Do not write one reflexively.
- **`design/FUTURE-WORK.md`** — no entry. Nothing is stubbed or deferred by this change.

---

## 6. What this row deliberately does not do

- **Does not touch the true first run.** The suppression stays; §0.9 item 3 and mutation `M2`.
- **Does not touch `UI-13`'s five other cache sites**, the precondition comment at `:52-93`, ADR-034,
  or `AudioStateUpdateServiceCacheOrderingTests.cs`.
- **Does not add a `TimeProvider` seam** (§0.7).
- **Does not fix the two adjacent defects in §7** — they are separate rows.
- **Does not change what `SourceChanged` carries.** Making it a payload-bearing event so the four
  surfaces could skip their re-fetch is a real design question and an entirely different row.

---

## 7. ⛔ Two adjacent defects found while tracing. FILE THEM; DO NOT FOLD THEM IN

Both are `CLAUDE.md` § *Pre-Merge Review* item-1 shapes — a message asserting more than the code does.
Neither is in this row's diff.

**7.1 `AudioEngineInitializationService.cs:423-424` logs startup activation as successful
unconditionally.** The return value of `GetOrCreateSourceAsync` is discarded, and all four of its
`return null` paths (`AudioManager.cs:375`, `:380`, `:386`, `:398`) are followed by
`_logger.LogInformation("Source {SourceType} activated on startup", sourceType)`. So the one line an
operator reads to confirm boot activation cannot distinguish success from silent failure — and that
failure is exactly `UI-15`'s precondition. ⭐ **This is why `UI-15` would be hard to diagnose from the
log even while firing**, which makes it the more valuable of the two. Suggested fix: check the return,
and log a warning naming the source when it is null.

**7.2 `AudioStateUpdateService.cs:107-108` claims `_currentMatchId` is cleared on a source change.**
The comment reads *"cleared when the active source changes (OnSourceChanged equivalent) or when the
latest event is not a match"*. The field has four occurrences in the file — declaration `:111`, read
`:511`, doc `:976`, write `:999` — and the single write is inside `UpdateCurrentMatchAnchor`, called
only from `OnFingerprintStatusChanged`. **`CheckSourceChangedAsync` does not touch it.** The second
half of the claim is true; the first half is not. Whether the *behaviour* is wrong (a stale NOW anchor
surviving a source switch) or only the comment is, is the question the row should ask — do not assume
the comment describes an intent that was implemented and lost.
