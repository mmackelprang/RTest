# PLAN — `TEST-7` · A `TimeProvider` seam for `NowPlayingPanel`'s two debounce timers

> **Row:** `TEST-7`, `docs/BUILDER_QUEUE.md:152`. 🟠 **P1**, [punch list §4.6 `:1095`](../../docs/HANDOFF-GA-PUNCH-LIST.md).
> Promoted 2026-09-03 from a note in § *Documented fast-follows* (`docs/BUILDER_QUEUE.md:518`) on that
> note's own instruction. **No dependencies; claimable now.**
> **Branch:** `fix/test-7-nowplayingpanel-timeprovider-seam`
> **Estimate:** **0.5 d**, as the punch list scoped it. §0.6 says what survives that and what pushes it to 1 d.
> **Planned against** `main` at **`656f58e6`**. Every line number below was read out of the tree at that
> commit. Where a line is likely to move it is quoted as well as numbered.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`TEST-4` removed a wall-clock race from `BluetoothCaptureWatchdogTests` and, while doing so, found the
same defect in `NowPlayingPanelVolumeDebounceTests` — and deliberately did not fix it, because closing
it needs **production** code to change and the two PRs would not have been reviewable together. This is
that row. Three tests sleep `Task.Delay(1500)` against a 300 ms `System.Threading.Timer` the panel owns,
with no rendezvous between the two clocks. The fix is the house idiom — an injectable `TimeProvider`
defaulting to `TimeProvider.System`, `FakeTimeProvider` in the test — plus the completion rendezvous the
row correctly insists a fake clock does not supply on its own. ⚠ **The house idiom does not transfer
unchanged, and §0.4 is the part of this plan a Builder must not skim:** a Blazor component has no
constructor to hang an optional parameter on, and `@inject` is a *required* resolve against a container
that registers no `TimeProvider` anywhere, deliberately, with a standing test saying so.

### 0.2 Exact current state — both timers, quoted, at `656f58e6`

**Timer 1 — the volume-preference debounce, 300 ms.** `src/Radio.Web/Components/Shared/NowPlayingPanel.razor:1054-1078`:

```csharp
  private static readonly TimeSpan VolumePreferenceDebounce = TimeSpan.FromMilliseconds(300);   // :1054
  private System.Threading.Timer? _volumePrefDebounceTimer;                                     // :1055
  private double _pendingVolumePreference;                                                      // :1056

  private void QueueVolumePreferenceSave(double newVolume)                                      // :1058
  {
    _pendingVolumePreference = newVolume;                                                        // :1060
    if (_volumePrefDebounceTimer != null)
    {
      _volumePrefDebounceTimer.Change(VolumePreferenceDebounce, Timeout.InfiniteTimeSpan);       // :1063
    }
    else
    {
      _volumePrefDebounceTimer = new System.Threading.Timer(                                     // :1067
        OnVolumePreferenceDebounceElapsed, null,
        VolumePreferenceDebounce, Timeout.InfiniteTimeSpan);
    }
  }

  private async void OnVolumePreferenceDebounceElapsed(object? _)                                 // :1073
  {
    // SavePreferenceAsync owns its own try/catch, which is what keeps this async void
    // callback from being able to throw onto a timer thread.
    await SavePreferenceAsync("volume", _pendingVolumePreference);                                // :1077
  }
```

Reached from `OnVolumeChanged` (`:1020`) → `HandleVolumeChangeAsync` (`:1026-1040`), which awaits
`AudioApi.SetVolumeAsync` first and *then* calls `QueueVolumePreferenceSave` (`:1034`).

**Timer 2 — the source-gain debounce, 200 ms.** Field at `:391`, body at `:886-924`:

```csharp
  private System.Threading.Timer? _gainDebounceTimer;                                            // :391

  private void OnGainSliderChanged(float newGain)                                                // :886
  {
    if (string.IsNullOrEmpty(_nowPlayingSourceType)) return;                                     // :888
    _currentSourceGain = newGain;
    _sourceGainOffsets[_nowPlayingSourceType] = newGain;                                         // :890

    // Debounce the API call — reuse existing timer via Change() instead of
    // dispose+recreate on each slider event to reduce GC pressure.
    _pendingGainSourceType = _nowPlayingSourceType;                                              // :894
    _pendingGainValue = newGain;
    if (_gainDebounceTimer != null)
    {
      _gainDebounceTimer.Change(TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);       // :898
    }
    else
    {
      _gainDebounceTimer = new System.Threading.Timer(                                            // :902
        OnGainDebounceElapsed, null,
        TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);                                // :904
    }
  }

  private string? _pendingGainSourceType;                                                        // :908
  private float _pendingGainValue;                                                               // :909

  private async void OnGainDebounceElapsed(object? _)                                             // :911
  {
    try
    {
      var sourceType = _pendingGainSourceType;
      var gain = _pendingGainValue;
      if (!string.IsNullOrEmpty(sourceType))
        await AudioApi.SetSourceGainAsync(sourceType, gain);                                      // :918
    }
    catch (Exception ex) { Logger.LogWarning(ex, "Failed to set source gain for {SourceType}", _pendingGainSourceType); }
  }
```

Also reached from `ResetGain` (`:880-884`), which is a synchronous call to `OnGainSliderChanged(1.0f)`.

**Both are disposed together**, `:1116-1132`:

```csharp
  public async ValueTask DisposeAsync()                                                          // :1116
  {
    …
    _gainDebounceTimer?.Dispose();                                                                // :1128
    _volumePrefDebounceTimer?.Dispose();                                                          // :1129
    _nowPlayingPollTimer?.Dispose();                                                              // :1130
    await Task.CompletedTask;
  }
```

**The hop counts differ, and the tests depend on that** (`C-121`). The volume callback makes **two**
HTTP hops — `ConfigurationApiService.UpdateConfigurationAsync(section, key, value)` at
`src/Radio.Web/Services/ApiClients/ConfigurationApiService.cs:83-103` GETs the section (`:88`) before it
POSTs it back (`:95`). The gain callback makes **one** — `AudioApiService.SetSourceGainAsync` at
`src/Radio.Web/Services/ApiClients/AudioApiService.cs:232-245` is a single `PostAsync`.

**A third raw timer exists in this file and is deliberately out of scope** (`C-125`, §6.1):
`_nowPlayingPollTimer`, `:507`, created at `:542-546` with a 60 s due time *and* a 60 s period, started
inside `OnInitializedAsync`.

### 0.3 Every call site that constructs the panel or would have to supply the clock

**Production — exactly one, and it passes nothing.**

| Site | Shape |
|---|---|
| `src/Radio.Web/Components/Pages/Home.razor:12` | `<Radio.Web.Components.Shared.NowPlayingPanel />` — no attributes, no parameters |

**DI — nothing to change, and that is the finding.** Blazor components are not container-registered.
`src/Radio.Web/Program.cs` mentions `NowPlayingPanel` only in a comment (`:475`), and mentions
`TimeProvider` **nowhere at all**. `EncoderHudService` — the house worked example — is registered bare at
`Program.cs:419` (`builder.Services.AddSingleton<Radio.Web.Services.EncoderHudService>();`) and gets its
clock from the *compile-time default*, not from the container.

**Tests — three fixtures, 46 activations.**

| Fixture | How it builds the panel | Count | Affected? |
|---|---|---|---|
| `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelTests.cs` | `RenderComponent<NowPlayingPanel>()` via bUnit `TestContext` | **33** | Only under Option 1 (§0.4) |
| `tests/Radio.Web.Tests/Components/Pages/HomePageTests.cs` | `RenderComponent<Home>()`, which renders the panel | **12** | Only under Option 1 |
| `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelVolumeDebounceTests.cs:78` | `new NowPlayingPanel()`, then reflection-sets `[Inject]` properties (`SetInjected`, `:88-95`) | **1** | **Yes — this is the file the row is about** |

Both bUnit fixtures call `Services.AddHermeticTestRig()` in their constructor
(`NowPlayingPanelTests.cs:53`, `HomePageTests.cs:28`), so under Option 1 a single line in
`tests/Radio.Web.Tests/TestHelpers/HermeticTestRig.cs:53-58` would cover all 45 renders. That is the
cheapest form of the expensive option — it is priced in §0.4, not adopted.

### 0.4 ⭐ The seam design — and yes, the Blazor shape is awkward. Saying so rather than papering over it.

**The house idiom cannot be transplanted.** `EncoderHudService.cs:44-49` is an *optional constructor
parameter* falling back to `TimeProvider.System`:

```csharp
  public EncoderHudService(
    AudioStateHubService? hub = null,
    TimeProvider? timeProvider = null,
    ILogger<EncoderHudService>? logger = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
```

A Blazor component is activated through a **parameterless** constructor by the renderer's
`ComponentFactory`. There is no constructor to hang that parameter on (`C-116`). So there is no clean
translation, and every option below trades something. Four were considered.

| | Option 1 — `@inject TimeProvider Clock` | Option 2 — `[Parameter] public TimeProvider? Clock` | **Option 3 — settable `internal` property with a default** | Option 4 — extract a `Debouncer` service |
|---|---|---|---|---|
| Files touched outside the panel | `Program.cs` + `HermeticTestRig.cs` (or 2 fixtures) | none | **none** | `Program.cs` + a new type |
| Preserves "nothing registers `TimeProvider`" | ❌ breaks it for one host | ✅ | ✅ | ❌ |
| Safe on a bare `new NowPlayingPanel()` | ❌ **null → NRE** | ✅ | ✅ | ✅ |
| Discoverable as a dependency | ✅ top of the file | ⚠ looks like a UI parameter | ⚠ one property in `@code` | ✅ |
| Generalises to the next component | ✅ | ❌ | ❌ | ✅ |

**Recommendation: Option 3.**

```csharp
internal TimeProvider Clock { get; set; } = TimeProvider.System;
```

Three reasons, in order of weight:

1. **It is the only option that keeps a documented, *tested* invariant intact.** `TimeProvider` is
   registered in neither host, deliberately, and the codebase says so three times in one file —
   `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs:414-415`
   (*"TimeProvider is deliberately unregistered in production and the constructor default
   (TimeProvider.System) is what should apply there"*), `:458-459` and `:494-496`. There is a **standing
   check** for the shape at `tests/Radio.Web.Tests/Services/EncoderHudServiceTests.cs:466-485`, whose own
   comment reads *"the container fills the hub from DI and takes the compile-time default for the
   TimeProvider that nothing registers."* Option 1 would make `Radio.Web` the one host where it *is*
   registered — behaviourally identical (it is the same `TimeProvider.System` singleton), but it turns a
   uniform rule into a cross-host asymmetry, for a test-only need (`C-117`).
2. **`@inject` has no default and cannot be forgotten safely.** It is a *required* resolve, and on a bare
   `new NowPlayingPanel()` — which is exactly how this row's own test file builds the panel
   (`:78`) — it leaves `Clock` null and the next `QueueVolumePreferenceSave` throws inside a timer arm.
   Option 3's default is the production behaviour and there is nothing to forget (`C-118`).
3. **The mechanism already exists and is already blessed for this.**
   `src/Radio.Web/Radio.Web.csproj:28-32` carries `<InternalsVisibleTo Include="Radio.Web.Tests" />`
   under a comment that reads *"Expose `internal` helpers … so unit tests can exercise them without
   making them part of the public API."* No reflection, no new plumbing (`C-119`).

**What Option 3 costs, stated plainly.** It is a settable property that production never writes. That is
a smell, and the mitigation is a comment saying so rather than pretending otherwise — Task 1's literal
code carries it. It also does not generalise: the next component that needs a clock repeats the pattern
instead of resolving one from DI.

**Rejected, with reasons.** *Option 2* puts a test seam on the component's public **parameter** surface,
where a reader will reasonably expect a value a parent supplies; `Home.razor` never would.
*Option 4* is the better refactor in the abstract — the two timer blocks are near-identical bookkeeping
— but it is **not an alternative to Options 1-3**: a `Debouncer` still has to be handed a clock by the
panel that constructs it, so it sits *on top of* this seam rather than replacing it. It is the right
move the day a third component needs a debounce; §6.2 files it.

⚠ **If a reviewer rejects Option 3 on discoverability grounds, Option 1 is the fallback and this plan
does not need re-planning for it** — §6.3 gives the three extra lines verbatim.

### 0.5 ⚠ Why the fake clock is only half the fix — and which half each mechanism closes

`CLAUDE.md` § *Test Timing* is explicit: *"Advancing a fake clock is only half the job when the callback
is `async`."* Both callbacks here are `async void` over awaited HTTP. So the plan uses **two** mechanisms,
one per failure direction, and neither is a wall clock (`C-120`):

| Failure the row names | Closed by | Why it is closed, not merely made rarer |
|---|---|---|
| **Overshoot to 2** — a stall inserts >300 ms between un-slept setup invokes, re-arming the trailing edge | **the fake clock** | A `FakeTimeProvider` cannot advance on its own. No timer can fire between two `InvokeVolumeChange` calls, however long the machine stalls. **Structurally impossible**, not improbable |
| **Undershoot to 0** — the callback plus its two HTTP hops do not drain inside the sleep | **the rendezvous** | The test does not proceed until the request has been *recorded*. It synchronises on the observation, not on elapsed time |

This is the same shape `TEST-4` landed, one layer out: `BluetoothCaptureWatchdogTests`' class `<remarks>`
(`tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothCaptureWatchdogTests.cs:16-44`) gates the
watchdog's *dependency* so *"every assertion runs while the watchdog is held at its next poll."* Here the
panel's only dependency inside the callback is HTTP, so the recording `HttpMessageHandler` is where the
rendezvous belongs.

**Say which kind each assertion is.** Under a fake clock a "nothing has been written yet" check is
**exact**, not a bounded negative — no timer is due, so none can fire. That is stronger than the
`DisabledByZeroThreshold_DoesNotRaise` case `TEST-4` had to hedge, and the test comments must say so
rather than implying a hedge that is not needed.

### 0.6 The estimate

**0.5 d**, matching the punch list. It survives because nothing here has to be invented:

1. **The clock library is already referenced.** `Microsoft.Extensions.TimeProvider.Testing` 9.10.0 at
   `tests/Radio.Web.Tests/Radio.Web.Tests.csproj:26`; `FakeTimeProvider` is already used in this exact
   project by `EncoderHudServiceTests`, `EncoderHudTests.cs:31`, `EncoderSelectorOverlayTests.cs:371`
   and `SleepTests.cs:106`.
2. **The production diff is ~25 lines in one file** and, by construction, behaviour-preserving — see the
   Task 3→4 checkpoint in §3.
3. **The recording handler already exists**; it is lifted out of the test file it lives in and given one
   method.

⚠ **What pushes it to 1 d:** if a repeat run (§4.5) still shows a flake, the completion signal is not
enough and the harness must escalate to `TEST-4`'s stronger form — the handler *blocks* in `SendAsync`
until the test grants the hop. §6.4 gives that shape verbatim so the escalation is not a re-plan.

### 0.7 ⚠ Constraints found while planning — numbering continues from `C-115` (`PHN-3`)

> If a concurrently-landing plan has claimed `C-116`+, renumber from the next free id rather than
> reusing one. `C-115` was the highest in `design/plans/` and `docs/` at `656f58e6`.

---

**`C-116` — ⚠ CHANGES THE SEAM. A Blazor component cannot take a constructor parameter, so the house
idiom has no direct translation.** The renderer's `ComponentFactory` activates components through a
parameterless constructor. `EncoderHudService.cs:44-49` is a constructor seam and is therefore a *model*
for this row, not a template. §0.4.

---

**`C-117` — `TimeProvider` is registered in neither host, deliberately, and there is a standing test for
it.** `AudioServiceExtensions.cs:414-415`, `:458-459`, `:494-496` state it in words and use
`sp.GetService<TimeProvider>()` (not `GetRequiredService`) because of it. `Radio.Web/Program.cs` never
mentions the type. `EncoderHudServiceTests.cs:466-485` is the check. Any `@inject TimeProvider` would be
the first registration in the codebase — permitted, but it is an owner-visible change to a stated rule,
not an implementation detail.

---

**`C-118` — `@inject` is a required resolve with no default, and this row's own test file builds the
panel bare.** `NowPlayingPanelVolumeDebounceTests.cs:78` is `new NowPlayingPanel()`. Under `@inject`,
`Clock` would be null there unless every construction site remembers to set it; under a defaulted
property it cannot be wrong.

---

**`C-119` — `InternalsVisibleTo("Radio.Web.Tests")` already exists and its comment already blesses this
use.** `src/Radio.Web/Radio.Web.csproj:31`. No csproj change is needed and no reflection is needed for
the new members.

---

**`C-120` — ⚠ THE TWO FAILURE DIRECTIONS NEED TWO DIFFERENT MECHANISMS.** The fake clock kills overshoot
structurally; only a completion rendezvous kills undershoot. A plan that advances a clock and asserts is
half a fix. §0.5.

---

**`C-121` — The two callbacks have different hop counts, so the rendezvous predicate differs per test.**
Volume = GET then POST (`ConfigurationApiService.cs:88`, `:95`). Gain = one POST
(`AudioApiService.cs:236`). Counting "requests" rather than "requests matching this path and method"
would count 2 for one volume write.

---

**`C-122` — `TimeProvider.CreateTimer` returns `ITimer`, not `System.Threading.Timer`, and that is a
compile-affecting change with no behavioural one.** `ITimer` lives in `System.Threading`, implements
`IDisposable`, and its `Change(TimeSpan, TimeSpan)` has the identical signature — so `DisposeAsync`
(`:1128-1129`) and the test helper `StopDebounceTimer` (`NowPlayingPanelVolumeDebounceTests.cs:111-118`,
which casts `as IDisposable`) both keep compiling untouched.

---

**`C-123` — The gain path has an early-return guard the new tests must satisfy.**
`OnGainSliderChanged` returns immediately when `_nowPlayingSourceType` is null or empty (`:888`), and it
indexes `_sourceGainOffsets` with it (`:890`). A gain test that skips setting that field passes
vacuously. `NowPlayingPanelTests.cs:638` already shows the reflection idiom for setting it.

---

**`C-124` — The debounce durations must become test-readable, or the tests re-encode them as literals
and quietly stop testing the debounce.** `VolumePreferenceDebounce` exists but is `private` (`:1054`);
the gain's 200 ms is an inline literal appearing **twice** (`:898`, `:904`), which is already a
same-file duplication hazard.

---

**`C-125` — ⚠ THERE IS A THIRD RAW TIMER IN THIS FILE AND IT IS DELIBERATELY OUT OF SCOPE.**
`_nowPlayingPollTimer` (`:507`), created at `:542-546` inside `OnInitializedAsync` with a 60 s due time
and a 60 s period. It is a poll, not a debounce; the row names two timers; nothing asserts on it; and
converting it changes the initialization path that **all 45** bUnit renders in §0.3 exercise. §6.1 files
it with the argument.

---

**`C-126` — ⚠ A DEFECT FOUND WHILE PLANNING TASK 6, AND IT IS NOT THIS ROW'S TO FIX.** Both API clients
interpolate a number into a URL with the **current culture**:
`AudioApiService.cs:236` — `$"/api/audio/sourcegain/{sourceType}/{gain:F2}"` — and `:96` —
`$"/api/audio/volume/{volume}"`. On a comma-decimal culture those become `.../0,25` and `.../0,5`.
The deployed box is not such a locale, so this is latent, and it is a `Radio.Web` API-client bug rather
than a timer bug. **Consequence for this row:** Task 6's tests must not assert on the formatted number.
§6.5 files it.

---

**`C-127` — The rendezvous `TaskCompletionSource` must be `RunContinuationsAsynchronously`, and must be
awaited, never `.Wait()`/`.Result`.** It is completed from inside `SendAsync`, which under
`FakeTimeProvider` runs on the thread that called `Advance` — i.e. the test thread. Without the flag the
awaiting test body resumes **inline, inside the handler, in the middle of `Advance`**. Task 4's literal
code carries it; do not drop it as noise.

---

**`C-128` — `Assert.Equal(88d, pending)` (`NowPlayingPanelVolumeDebounceTests.cs:183`) must survive the
rewrite.** The row and the punch list both single it out as the one assertion starvation cannot break —
`_pendingVolumePreference` is assigned synchronously at `:1060` before the awaited call returns. It is
also the file's only coverage of *which value* coalescing keeps. A rewrite that replaces it with a
clock-driven equivalent loses coverage while looking like a modernisation.

### 0.8 Things Builder must NOT do

- ⛔ **Do not register `TimeProvider` in `src/Radio.Web/Program.cs`** under the recommended option. That
  is Option 1 and it is a different decision (`C-117`); if you conclude it is right, say so in the PR
  body and take §6.3 wholesale rather than half-adopting it.
- ⛔ **Do not touch `_nowPlayingPollTimer`** (`C-125`).
- ⛔ **Do not "fix" the culture-sensitive URL formatting** (`C-126`). File it, per §6.5.
- ⛔ **Do not delete `Assert.Equal(88d, pending)`** (`C-128`).
- ⛔ **Do not change `VolumeDrag_StillAppliesEveryTickToTheAudioEngine`** (`:150-163`) beyond the
  mechanical `CountVolumeCalls` → `handler.Count(...)` swap. The row says it is genuinely safe; it
  performs no sleep and asserts synchronously on awaited calls.
- ⛔ **Do not raise a timeout or add a sleep anywhere.** `CLAUDE.md`: *"Raising a timeout or adding a
  sleep converts a flaky test into a slow flaky test."* The only `TimeSpan` left in the tests is the
  deadlock guard, and its comment must say that is what it is.

---

## 1. Decision — one property, two timers, two named durations

**The seam:** a single `internal TimeProvider Clock { get; set; } = TimeProvider.System;` on the
component. Both timers are created through `Clock.CreateTimer(...)` instead of `new
System.Threading.Timer(...)`. Both debounce durations become `internal static readonly TimeSpan`
constants so the tests advance by the production value rather than by a copied literal (`C-124`).

**The rendezvous:** the existing `RecordingHandler` moves to `tests/Radio.Web.Tests/TestHelpers/` and
gains `WaitForAsync(predicate, count)`, completed at the moment a matching request is **recorded** —
which is before the response is produced, so a caller that awaits it and then counts is reading state
already written.

**What that buys, measurably:** the three racing tests currently sleep **6.0 s** in total
(1.5 + 1.5 + 3.0) and, after this, sleep none. That number belongs in the PR body — it is the visible
half of a change whose real deliverable is invisible.

---

## 2. Tasks

### Task 1 — the seam and the two named durations

**File:** `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`

**1a.** Insert the seam immediately above the `_gainDebounceTimer` field at `:391`, so it precedes both
uses in file order:

```csharp
  /// <summary>
  /// The clock both debounce timers are armed against. Production never writes this; the default
  /// <em>is</em> the production behaviour.
  ///
  /// <para>
  /// <b>Why a settable property and not <c>@inject</c>.</b> The house idiom for a clock seam is an
  /// optional constructor parameter falling back to <see cref="TimeProvider.System"/>
  /// (<c>EncoderHudService</c>), and a Blazor component has no constructor to hang one on — the
  /// renderer activates it parameterlessly. <c>@inject</c> is the obvious substitute and is the
  /// wrong one twice over: it is a <em>required</em> resolve against a container that registers no
  /// <see cref="TimeProvider"/> in either host, deliberately (see the three comments saying so in
  /// <c>AudioServiceExtensions</c>), and it would leave this null on a bare
  /// <c>new NowPlayingPanel()</c> — which is exactly how the debounce tests build this panel.
  /// </para>
  ///
  /// <para>
  /// <c>internal</c> rather than <c>private</c> so <c>Radio.Web.Tests</c> can substitute a
  /// <c>FakeTimeProvider</c> through the <c>InternalsVisibleTo</c> the csproj already declares for
  /// exactly this purpose — and so the seam is greppable rather than reachable only by reflection.
  /// See TEST-7 in <c>docs/BUILDER_QUEUE.md</c>.
  /// </para>
  /// </summary>
  internal TimeProvider Clock { get; set; } = TimeProvider.System;

  /// <summary>
  /// Trailing-edge debounce for the source-gain slider. <c>internal</c> so the tests advance the
  /// fake clock by the production value instead of by a copied literal: a test that hardcodes
  /// 200 ms stops testing this the moment the number changes.
  /// </summary>
  internal static readonly TimeSpan SourceGainDebounce = TimeSpan.FromMilliseconds(200);
```

**1b.** Change the field on `:391` from `System.Threading.Timer?` to `ITimer?` (`C-122`):

```csharp
  private ITimer? _gainDebounceTimer;
```

### Task 2 — route the volume timer through `Clock`

**File:** same. Replace `:1054-1071`:

```csharp
  // ...the existing 12-line comment at :1042-1053 is unchanged and stays above this...
  internal static readonly TimeSpan VolumePreferenceDebounce = TimeSpan.FromMilliseconds(300);
  private ITimer? _volumePrefDebounceTimer;
  private double _pendingVolumePreference;

  private void QueueVolumePreferenceSave(double newVolume)
  {
    _pendingVolumePreference = newVolume;
    if (_volumePrefDebounceTimer != null)
    {
      _volumePrefDebounceTimer.Change(VolumePreferenceDebounce, Timeout.InfiniteTimeSpan);
    }
    else
    {
      _volumePrefDebounceTimer = Clock.CreateTimer(
        OnVolumePreferenceDebounceElapsed, null,
        VolumePreferenceDebounce, Timeout.InfiniteTimeSpan);
    }
  }
```

Two changes only: `private static readonly` → `internal static readonly` on the duration, and
`new System.Threading.Timer(` → `Clock.CreateTimer(`. `OnVolumePreferenceDebounceElapsed` (`:1073-1078`)
is **unchanged** — its signature already matches `TimerCallback`.

### Task 3 — route the gain timer through `Clock`

**File:** same. Replace `:892-905` (the comment on `:892-893` is kept verbatim):

```csharp
    // Debounce the API call — reuse existing timer via Change() instead of
    // dispose+recreate on each slider event to reduce GC pressure.
    _pendingGainSourceType = _nowPlayingSourceType;
    _pendingGainValue = newGain;
    if (_gainDebounceTimer != null)
    {
      _gainDebounceTimer.Change(SourceGainDebounce, Timeout.InfiniteTimeSpan);
    }
    else
    {
      _gainDebounceTimer = Clock.CreateTimer(
        OnGainDebounceElapsed, null,
        SourceGainDebounce, Timeout.InfiniteTimeSpan);
    }
```

The duplicated `TimeSpan.FromMilliseconds(200)` on `:898` and `:904` is gone in favour of the one
constant (`C-124`). `OnGainDebounceElapsed` (`:911-924`) is unchanged.

### Task 4 — the rendezvous harness

**New file:** `tests/Radio.Web.Tests/TestHelpers/RecordingHandler.cs`. This lifts the nested
`RecordingHandler` out of `NowPlayingPanelVolumeDebounceTests.cs:32-63` (delete it there) and adds the
one thing it lacked.

```csharp
using System.Net;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Records every request a component makes, and lets a test <em>rendezvous on the observation</em>
/// instead of on elapsed wall-clock time.
///
/// <para>
/// This is the outer layer of the shape <c>BluetoothCaptureWatchdogTests</c> uses one layer in
/// (TEST-4): a component's assertions must synchronize on the component having <em>observed</em>
/// something, never on a <c>Task.Delay</c> racing the component's own timer. For a Blazor panel the
/// only dependency inside a debounce callback is HTTP, so the handler is where the rendezvous
/// belongs. See TEST-7 in <c>docs/BUILDER_QUEUE.md</c>.
/// </para>
///
/// <para>
/// Pair it with a <c>FakeTimeProvider</c>, not instead of one — the two close opposite failure
/// directions. The fake clock makes an <em>extra</em> callback impossible (it cannot advance on its
/// own); this makes a <em>missing</em> one impossible (the test does not proceed until the request
/// is recorded).
/// </para>
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
  /// <summary>
  /// Deadlock guard, not a timing margin. It bounds how a broken test fails and is never reached on
  /// a passing run — the same role <c>BluetoothCaptureWatchdogTests.GateTimeout</c> plays. Raising
  /// it can never make a failing test pass.
  /// </summary>
  public static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(30);

  private readonly object _sync = new();
  private readonly List<(HttpMethod Method, string Path)> _requests = [];
  private readonly List<Waiter> _waiters = [];

  private sealed class Waiter
  {
    public required Func<HttpMethod, string, bool> Match { get; init; }
    public required int Target { get; init; }
    public int Seen { get; set; }

    // RunContinuationsAsynchronously is load-bearing, not a default worth copying blindly: this is
    // completed from inside SendAsync, which under FakeTimeProvider runs on the thread that called
    // Advance — the test thread. Without the flag the awaiting test body would resume inline,
    // inside the handler, in the middle of Advance.
    public TaskCompletionSource Signal { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
  }

  public static bool IsConfigWrite(HttpMethod method, string path) =>
    method == HttpMethod.Post && path == "/api/configuration/ui.playback";

  public static bool IsVolumeCall(HttpMethod method, string path) =>
    method == HttpMethod.Post && path.StartsWith("/api/audio/volume/", StringComparison.Ordinal);

  public static bool IsSourceGainCall(HttpMethod method, string path) =>
    method == HttpMethod.Post && path.StartsWith("/api/audio/sourcegain/", StringComparison.Ordinal);

  public IReadOnlyList<(HttpMethod Method, string Path)> Requests
  {
    get
    {
      lock (_sync)
      {
        return _requests.ToList();
      }
    }
  }

  public int Count(Func<HttpMethod, string, bool> predicate)
  {
    lock (_sync)
    {
      return _requests.Count(r => predicate(r.Method, r.Path));
    }
  }

  /// <summary>
  /// Completes once <paramref name="count"/> requests matching <paramref name="predicate"/> have
  /// been <b>recorded</b>. Recording happens before the response is produced, so a caller that
  /// awaits this and then calls <see cref="Count"/> is asserting on state already written.
  /// </summary>
  public Task WaitForAsync(Func<HttpMethod, string, bool> predicate, int count)
  {
    Waiter waiter;
    lock (_sync)
    {
      var already = _requests.Count(r => predicate(r.Method, r.Path));
      if (already >= count)
      {
        return Task.CompletedTask;
      }

      waiter = new Waiter { Match = predicate, Target = count, Seen = already };
      _waiters.Add(waiter);
    }

    return waiter.Signal.Task.WaitAsync(RendezvousTimeout);
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var method = request.Method;
    var path = request.RequestUri?.AbsolutePath ?? string.Empty;

    List<Waiter>? ready = null;
    lock (_sync)
    {
      _requests.Add((method, path));

      foreach (var waiter in _waiters)
      {
        if (!waiter.Match(method, path)) continue;
        if (++waiter.Seen < waiter.Target) continue;
        (ready ??= []).Add(waiter);
      }

      if (ready is not null)
      {
        foreach (var waiter in ready) _waiters.Remove(waiter);
      }
    }

    // Signalled outside the lock so a continuation can never re-enter SendAsync while it is held.
    if (ready is not null)
    {
      foreach (var waiter in ready) waiter.Signal.TrySetResult();
    }

    // "{}" satisfies both the PlaybackStateDto read on the volume call and the dictionary read the
    // configuration client performs before it writes.
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
    });
  }
}
```

### Task 5 — rewrite `NowPlayingPanelVolumeDebounceTests`

**File:** `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelVolumeDebounceTests.cs`

**5a.** Replace the class `<summary>`'s third paragraph (`:20-22`) and append the `<remarks>` that
records what changed and why, in the `BluetoothCaptureWatchdogTests` style:

```csharp
/// <remarks>
/// <para>
/// These tests are <b>clock-driven, not sleep-driven</b> (TEST-7). Every one of them advances a
/// <c>FakeTimeProvider</c> the panel was built with and then rendezvouses on the request the
/// debounce callback actually makes. Nothing here waits on wall-clock time.
/// </para>
/// <para>
/// The shape they replace raced <c>await Task.Delay(1500)</c> against the panel's own 300 ms timer
/// with no rendezvous, and could fail in <b>both</b> directions — undershoot to zero writes if the
/// callback and its two HTTP hops missed the window, overshoot to two if a stall inserted more than
/// 300 ms between the un-slept setup invokes. Both are now closed, and by different mechanisms: the
/// fake clock makes an extra callback impossible, and <c>WaitForAsync</c> makes a missing one
/// impossible. Same defect as TEST-4, one layer out.
/// </para>
/// <para>
/// So a "nothing has been written yet" assertion below is <b>exact</b>, not a bounded negative: no
/// timer is due, so none can fire, however loaded the machine is. <c>RendezvousTimeout</c> is a
/// deadlock guard and is not reached on a passing run.
/// </para>
/// </remarks>
```

**5b.** Delete `PastDebounceWindow` (`:26-27`), the nested `RecordingHandler` (`:32-63`), and
`CountConfigWrites`/`CountVolumeCalls` (`:120-124`). Add
`using Microsoft.Extensions.Time.Testing;`.

**5c.** `CreatePanel` becomes:

```csharp
  private static (NowPlayingPanel Panel, RecordingHandler Handler, FakeTimeProvider Clock) CreatePanel()
  {
    var handler = new RecordingHandler();
    var clock = new FakeTimeProvider();

    var audioClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };
    var configClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };

    // Clock is set through the internal seam rather than by reflection: Radio.Web.csproj already
    // grants this assembly InternalsVisibleTo, and a compile-time set breaks loudly if the seam is
    // ever renamed, where a reflective one would silently start testing the system clock again.
    var panel = new NowPlayingPanel { Clock = clock };
    SetInjected(panel, "AudioApi",
      new AudioApiService(audioClient, NullLogger<AudioApiService>.Instance));
    SetInjected(panel, "ConfigApi",
      new ConfigurationApiService(configClient, NullLogger<ConfigurationApiService>.Instance));
    SetInjected(panel, "Logger", NullLogger<NowPlayingPanel>.Instance);

    return (panel, handler, clock);
  }
```

`SetInjected` (`:88-95`), `InvokeVolumeChange` (`:97-104`) and `StopDebounceTimer` (`:111-118`) are
**unchanged** — the last still works because `ITimer` implements `IDisposable` (`C-122`).

**5d.** The five test bodies. §4.1-§4.5 give each one's rendezvous argument; the code is here.

```csharp
  /// <summary>
  /// The headline regression: a 13-tick drag must persist once, not 13 times.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_PersistsThePreferenceOnce()
  {
    var (panel, handler, clock) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      await InvokeVolumeChange(panel, 40 + i);
    }

    // Exact, not bounded: the clock has not advanced, so no callback can have run.
    Assert.Equal(0, handler.Count(RecordingHandler.IsConfigWrite));

    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);

    // Still exact: the timer is one-shot (InfiniteTimeSpan period) and the clock will not move
    // again, so no second write can appear after this line.
    Assert.Equal(1, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// The audible half of the slider must stay immediate — debouncing the volume itself would make
  /// the control feel broken. Every tick still reaches /api/audio/volume.
  /// </summary>
  /// <remarks>
  /// The one test in this file that never needed the seam: it performs no wait at all and asserts
  /// on calls the test itself awaited.
  /// </remarks>
  [Fact]
  public async Task VolumeDrag_StillAppliesEveryTickToTheAudioEngine()
  {
    var (panel, handler, _) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      await InvokeVolumeChange(panel, 40 + i);
    }

    Assert.Equal(13, handler.Count(RecordingHandler.IsVolumeCall));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Coalescing must keep the value the user actually released on, not the first tick.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_PersistsTheFinalValue()
  {
    var (panel, handler, clock) = CreatePanel();

    await InvokeVolumeChange(panel, 10);
    await InvokeVolumeChange(panel, 55);
    await InvokeVolumeChange(panel, 88);

    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingVolumePreference", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    // Kept deliberately (TEST-7 C-128): _pendingVolumePreference is assigned synchronously inside
    // QueueVolumePreferenceSave, so this assertion never depended on timing — and it is this
    // file's only coverage of *which* value coalescing keeps.
    Assert.Equal(88d, pending);
    Assert.Equal(1, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Two deliberate, separated adjustments are two user actions and must both persist.
  /// </summary>
  [Fact]
  public async Task SeparatedVolumeChanges_EachPersist()
  {
    var (panel, handler, clock) = CreatePanel();

    await InvokeVolumeChange(panel, 20);
    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);

    await InvokeVolumeChange(panel, 70);
    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 2);

    Assert.Equal(2, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Nothing is persisted before the window closes. New with TEST-7 — impossible to state exactly
  /// against a wall clock, where "not yet" is only ever "not yet on this machine".
  /// </summary>
  [Fact]
  public async Task VolumeDrag_DoesNotPersistBeforeTheWindowElapses()
  {
    var (panel, handler, clock) = CreatePanel();

    await InvokeVolumeChange(panel, 40);
    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce - TimeSpan.FromMilliseconds(1));

    Assert.Equal(0, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Every tick re-arms the window — the trailing edge is what makes a 13-tick drag one write.
  /// New with TEST-7, and the reason the seam is worth its production diff: this is the behaviour
  /// <c>QueueVolumePreferenceSave</c>'s <c>Change()</c> branch exists for, and nothing tested it.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_ReArmsTheWindowOnEveryTick()
  {
    var (panel, handler, clock) = CreatePanel();
    var half = NowPlayingPanel.VolumePreferenceDebounce / 2;   // 150 ms

    await InvokeVolumeChange(panel, 40);      // armed for t+300
    clock.Advance(half);                      // t = 150
    await InvokeVolumeChange(panel, 50);      // re-armed for t+450
    clock.Advance(half);                      // t = 300 — the ORIGINAL due time

    // Exact: 1 here would mean Change() did not re-arm and the first arming survived.
    Assert.Equal(0, handler.Count(RecordingHandler.IsConfigWrite));

    clock.Advance(half);                      // t = 450 — the re-armed due time
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);
    Assert.Equal(1, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }
```

### Task 6 — new `NowPlayingPanelGainDebounceTests`

**New file:** `tests/Radio.Web.Tests/Components/Shared/NowPlayingPanelGainDebounceTests.cs`.
The gain timer has **no tests at all today**; this is new coverage, not a repair.

```csharp
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Components.Shared;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// Clock-driven tests for the source-gain slider's 200 ms trailing-edge debounce.
///
/// <para>The gain slider has the same write-amplification exposure the volume slider had — a drag
/// emits a value-changed event per pixel and each one would otherwise reach
/// <c>/api/audio/sourcegain</c>. It was debounced from the start and, until TEST-7, never tested.
/// It is covered here rather than in <c>NowPlayingPanelVolumeDebounceTests</c> because it is a
/// different endpoint with a different hop count: one POST, where a volume preference write is a
/// GET followed by a POST.</para>
/// </summary>
/// <remarks>
/// Same discipline as its volume sibling: advance a <c>FakeTimeProvider</c>, then rendezvous on the
/// request itself. No test here waits on wall-clock time, and the "nothing written yet" assertions
/// are exact rather than bounded. See TEST-7 in <c>docs/BUILDER_QUEUE.md</c>.
/// </remarks>
public class NowPlayingPanelGainDebounceTests
{
  private const string SourceType = "FilePlayer";

  private static (NowPlayingPanel Panel, RecordingHandler Handler, FakeTimeProvider Clock) CreatePanel()
  {
    var handler = new RecordingHandler();
    var clock = new FakeTimeProvider();

    var audioClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };

    var panel = new NowPlayingPanel { Clock = clock };
    SetPrivate(panel, "AudioApi",
      new AudioApiService(audioClient, NullLogger<AudioApiService>.Instance), isProperty: true);
    SetPrivate(panel, "Logger", NullLogger<NowPlayingPanel>.Instance, isProperty: true);

    // OnGainSliderChanged returns immediately when _nowPlayingSourceType is empty (TEST-7 C-123),
    // so a test that skips this passes vacuously.
    SetPrivate(panel, "_nowPlayingSourceType", SourceType, isProperty: false);

    return (panel, handler, clock);
  }

  private static void SetPrivate(NowPlayingPanel panel, string name, object value, bool isProperty)
  {
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    if (isProperty)
    {
      var property = typeof(NowPlayingPanel).GetProperty(name, Flags);
      Assert.True(property is not null, $"NowPlayingPanel should inject a '{name}' service");
      property!.SetValue(panel, value);
      return;
    }

    var field = typeof(NowPlayingPanel).GetField(name, Flags);
    Assert.True(field is not null, $"NowPlayingPanel should hold a '{name}' field");
    field!.SetValue(panel, value);
  }

  /// <summary>Drives the slider handler. Synchronous by design — it arms a timer and returns.</summary>
  private static void InvokeGainChange(NowPlayingPanel panel, float gain)
  {
    var method = typeof(NowPlayingPanel).GetMethod(
      "OnGainSliderChanged", BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.True(method is not null, "NowPlayingPanel should expose OnGainSliderChanged");
    method!.Invoke(panel, [gain]);
  }

  private static void StopDebounceTimer(NowPlayingPanel panel)
  {
    var field = typeof(NowPlayingPanel).GetField(
      "_gainDebounceTimer", BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.True(field is not null, "NowPlayingPanel should debounce source-gain writes");
    (field!.GetValue(panel) as IDisposable)?.Dispose();
  }

  /// <summary>A 13-tick gain drag must reach the API once, not 13 times.</summary>
  [Fact]
  public async Task GainDrag_WritesOnceAfterTheWindow()
  {
    var (panel, handler, clock) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      InvokeGainChange(panel, 0.5f + (i * 0.01f));
    }

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    clock.Advance(NowPlayingPanel.SourceGainDebounce);
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);

    Assert.Equal(1, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>Nothing reaches the API before the window closes.</summary>
  [Fact]
  public void GainDrag_DoesNotWriteBeforeTheWindowElapses()
  {
    var (panel, handler, clock) = CreatePanel();

    InvokeGainChange(panel, 0.5f);
    clock.Advance(NowPlayingPanel.SourceGainDebounce - TimeSpan.FromMilliseconds(1));

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>Every tick re-arms the window, which is what collapses a drag to one write.</summary>
  [Fact]
  public async Task GainDrag_ReArmsTheWindowOnEveryTick()
  {
    var (panel, handler, clock) = CreatePanel();
    var half = NowPlayingPanel.SourceGainDebounce / 2;   // 100 ms

    InvokeGainChange(panel, 0.5f);     // armed for t+200
    clock.Advance(half);               // t = 100
    InvokeGainChange(panel, 0.6f);     // re-armed for t+300
    clock.Advance(half);               // t = 200 — the ORIGINAL due time

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    clock.Advance(half);               // t = 300
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);
    Assert.Equal(1, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Coalescing keeps the value the user released on.
  /// </summary>
  /// <remarks>
  /// Asserts on <c>_pendingGainValue</c>, not on the request path. <c>SetSourceGainAsync</c> builds
  /// its URL with <c>{gain:F2}</c>, which is <b>current-culture</b> formatting (TEST-7 C-126), so a
  /// path assertion would encode the runner's locale into the test. That defect is filed, not fixed
  /// here. <c>_pendingGainValue</c> is assigned synchronously inside <c>OnGainSliderChanged</c>, so
  /// this assertion is timing-independent for the same reason its volume twin is.
  /// </remarks>
  [Fact]
  public async Task GainDrag_WritesTheFinalValue()
  {
    var (panel, handler, clock) = CreatePanel();

    InvokeGainChange(panel, 0.50f);
    InvokeGainChange(panel, 0.75f);
    InvokeGainChange(panel, 0.25f);

    clock.Advance(NowPlayingPanel.SourceGainDebounce);
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingGainValue", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    Assert.Equal(0.25f, pending);
    Assert.Equal(1, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// The reset button is a slider event, not a second path — it must debounce identically.
  /// Pins the comment on <c>ResetGain</c> that says exactly this.
  /// </summary>
  [Fact]
  public async Task ResetGain_GoesThroughTheSameDebounce()
  {
    var (panel, handler, clock) = CreatePanel();

    var reset = typeof(NowPlayingPanel).GetMethod(
      "ResetGain", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.True(reset is not null, "NowPlayingPanel should expose ResetGain");
    reset!.Invoke(panel, []);

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    clock.Advance(NowPlayingPanel.SourceGainDebounce);
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingGainValue", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    Assert.Equal(1.0f, pending);

    StopDebounceTimer(panel);
  }
}
```

### Task 7 — the two deferrals, written down where they will be read

**File:** `design/FUTURE-WORK.md`. Append two entries (per the standing project rule that stubbed or
deliberately-deferred work is documented there, never left implicit):

```markdown
### `NowPlayingPanel._nowPlayingPollTimer` still uses the system clock (TEST-7 `C-125`)

**What exists.** `TEST-7` gave `NowPlayingPanel` an `internal TimeProvider Clock` seam and routed its
two debounce timers through it. The panel's third timer — `_nowPlayingPollTimer`
(`src/Radio.Web/Components/Shared/NowPlayingPanel.razor:507`, created at `:542-546`, 60 s due / 60 s
period) — was deliberately left on `new System.Threading.Timer(...)`.

**Why deferred.** It is a fallback poll, not a debounce; nothing asserts on it; and unlike the two
debounce timers it is armed inside `OnInitializedAsync`, which all 45 bUnit renders of the panel and of
`Home` exercise. Changing an initialization path that every render touches, for no test-determinism
gain, is a worse trade than leaving it.

**What is needed.** Three lines — `private ITimer? _nowPlayingPollTimer;` and
`Clock.CreateTimer(...)`. The seam it needs already exists. Do it if and when a test needs to drive
the fallback poll.

**Priority.** Low.

### `AudioApiService` builds two URLs with current-culture number formatting (TEST-7 `C-126`)

**What exists.** `src/Radio.Web/Services/ApiClients/AudioApiService.cs:236` —
`$"/api/audio/sourcegain/{sourceType}/{gain:F2}"` — and `:96` — `$"/api/audio/volume/{volume}"`. Both
interpolate a floating-point value into a route using `CultureInfo.CurrentCulture`.

**Why it matters.** On any comma-decimal locale these become `/api/audio/sourcegain/FilePlayer/0,25`
and `/api/audio/volume/0,5`, which will not bind. Latent today only because the box's locale is
dot-decimal.

**What is needed.** `CultureInfo.InvariantCulture` on both, plus a sweep of the other API clients for
the same shape, plus a test that runs under a comma-decimal culture. Found while planning `TEST-7` and
deliberately not fixed there — it is an API-client bug with its own blast radius, not a timer bug.

**Priority.** Medium — it is a correctness bug, not a style one.
```

---

## 3. Ordering

| # | Task | Gate before moving on |
|---|---|---|
| 1 | Seam + named durations | `dotnet build` clean |
| 2 | Volume timer → `Clock` | — |
| 3 | Gain timer → `Clock` | ⭐ **`dotnet test` on `Radio.Web.Tests` fully green with the test files still untouched** |
| 4 | `RecordingHandler` harness | Builds |
| 5 | Volume tests rewritten | Filtered run green |
| 6 | Gain tests added | Filtered run green |
| 7 | `design/FUTURE-WORK.md` | — |

⭐ **The gate after Task 3 is the most valuable one in this plan and must not be skipped.** After Tasks
1-3 the production seam is in and every existing test — including the three that still sleep 1500 ms —
is unchanged and must still pass, because `Clock` defaults to `TimeProvider.System`. That proves the
seam is behaviour-preserving *before* any test is rewritten. If a test fails at this point, the
production change is wrong; do not "fix" it in Task 5.

---

## 4. Test plan

Every test below states its rendezvous explicitly. "Exact" means the assertion cannot be weakened by a
slow machine; "rendezvous" names the observation the test waits on.

### 4.1 `VolumeDrag_PersistsThePreferenceOnce`

| | |
|---|---|
| **Pins** | 13 ticks → exactly 1 config write |
| **Setup** | 13 awaited `HandleVolumeChangeAsync` calls; clock not advanced |
| **Pre-assert** | 0 config writes. **Exact** — the clock has not moved, so no callback can have run |
| **Advance** | `VolumePreferenceDebounce` (the production constant, not a literal) |
| **Rendezvous** | `await handler.WaitForAsync(IsConfigWrite, 1)` — completes when the POST to `/api/configuration/ui.playback` is *recorded*, which happens before the response is built |
| **Post-assert** | exactly 1. **Exact** — the timer is one-shot and the clock will not move again, so no later write can appear |

### 4.2 `VolumeDrag_StillAppliesEveryTickToTheAudioEngine`

Unchanged in substance. No wait of any kind; it asserts on 13 calls the test itself awaited. The row and
the punch list both classify it safe; the only edit is `CountVolumeCalls(handler)` →
`handler.Count(RecordingHandler.IsVolumeCall)`.

### 4.3 `VolumeDrag_PersistsTheFinalValue`

Same rendezvous as §4.1. Two assertions of **different kinds**, and the test comment says which is which:
`Assert.Equal(88d, pending)` is timing-independent by construction (`_pendingVolumePreference` is
assigned at `:1060`, synchronously, before the awaited call returns) and is kept for its coalescing-value
coverage (`C-128`); `Assert.Equal(1, …)` is the one that needed the rendezvous.

### 4.4 `SeparatedVolumeChanges_EachPersist`

Two advance-then-rendezvous cycles, waiting for cumulative counts 1 then 2. The second rendezvous cannot
be satisfied by the first write — `WaitForAsync(pred, 2)` counts matching requests already recorded when
it is called, so it waits for a genuinely new one.

### 4.5 The two new volume tests

- **`VolumeDrag_DoesNotPersistBeforeTheWindowElapses`** — advance `debounce - 1 ms`, assert 0. **Exact,
  not a bounded negative:** no timer is due at that instant, so none can fire. Worth stating in the
  comment, because the same test written against a wall clock would only ever mean "not yet, here".
- **`VolumeDrag_ReArmsTheWindowOnEveryTick`** — the trailing-edge behaviour `Change()` exists for, which
  nothing currently tests. Arithmetic: arm at t=0 due 300; `Change` at t=150 moves due to 450; at t=300
  assert **0** (a 1 here means the re-arm did not happen); at t=450 rendezvous and assert 1. This test is
  **not writable** against a real clock — 150 ms sleeps between invokes would race the very 300 ms window
  under test.

### 4.6 The six gain tests

Same three shapes plus two the volume file has no analogue for. `GainDrag_WritesTheFinalValue` asserts on
`_pendingGainValue` rather than on the request path, because the path carries current-culture number
formatting (`C-126`) and a path assertion would bake the runner's locale into the suite.
`ResetGain_GoesThroughTheSameDebounce` pins the claim `ResetGain`'s own comment makes at `:874-879` —
*"the slider value-changed callback follows immediately so the debounce timer fires the API write through
the same path as a manual slider drag"* — which is currently an unverified assertion in a comment, the
precise class of thing `CLAUDE.md` § *Pre-Merge Review* says this repo ships wrong.

### 4.7 Gates

⚠ **Never pipe `dotnet test` into `tail`** (`CLAUDE.md`) — the pipeline reports `tail`'s exit code.

```bash
dotnet build RadioConsole.sln -c Release > /tmp/build.log 2>&1; echo "exit=$?"
grep -E "error|warning" /tmp/build.log

dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

Read the **per-project summary lines**, one per test project. Known-failing on Windows and **not** a
regression: four `SrcVariableResamplerTests` (`libsamplerate.so.0`, `TEST-5`) and
`NwsObservationIntegrationTests.RealNwsCall_*` (live network, `Category=Integration`, CI-excluded).

**The determinism gate — this is the row's actual deliverable and a single green run does not
demonstrate it.** Match `TEST-4`'s bar: 200 iterations of both debounce classes under CPU saturation,
200/200.

```bash
dotnet test tests/Radio.Web.Tests -c Release \
  --filter "FullyQualifiedName~NowPlayingPanelVolumeDebounceTests|FullyQualifiedName~NowPlayingPanelGainDebounceTests" \
  > /tmp/debounce.log 2>&1; echo "exit=$?"
```

**Report the wall-clock delta in the PR body.** The three racing tests sleep 6.0 s today
(1.5 + 1.5 + 3.0) and none after. Eleven tests that run in roughly the time three used to spend asleep is
the visible half of the change; the invisible half is that none of them can now fail on a loaded runner.

### 4.8 UAT

**None, and that is the correct answer here** — this is a test-determinism row whose production change is
behaviour-preserving by construction (the Task 3 gate proves it). The auto-merge policy's *"UAT where
appropriate"* is satisfied by the unit suite plus the repeat run. No deploy to `radio` is required.

---

## 5. Docs and queue

- `design/FUTURE-WORK.md` — the two entries in Task 7.
- `docs/BUILDER_QUEUE.md` — mark `TEST-7` shipped and fill its Plan cell. **The Plan-cell wording is at
  the end of this document; the Planner did not edit the queue file** because a Builder was updating a
  different row in it concurrently.
- `docs/HANDOFF-GA-PUNCH-LIST.md` — tick `TEST-7` in §4.6 and decrement the P1 open count at `:1346`.
- **No `CLAUDE.md` change.** Its § *Test Timing* section already prescribes exactly what this row does;
  `NowPlayingPanel` is not worth adding as a second worked example beside `EncoderHudService`.
- **No `design/INTEGRATIONS.md` change** — no integration surface moves.

---

## 6. Deliberately not done

### 6.1 `_nowPlayingPollTimer`

`C-125`, filed in Task 7. The seam it would need now exists; the reason to wait is that it is armed in
`OnInitializedAsync`, the one path all 45 bUnit renders traverse, and no test wants to drive it.

### 6.2 Extracting a shared `Debouncer`

The two timer blocks are near-identical bookkeeping and a `Debouncer(TimeProvider, TimeSpan, Func<Task>)`
would delete the duplication and be unit-testable with no reflection at all. Not done because it is
**not an alternative to this row's seam** — the panel would still have to hand the debouncer a clock, so
the property in Task 1 is a prerequisite either way — and because it turns a P1 determinism fix into a
refactor PR. Revisit when a third component needs a debounce.

### 6.3 Option 1 (`@inject TimeProvider Clock`), if the owner prefers it

Recorded verbatim so choosing it is not a re-plan. It is **three lines** plus dropping the property
default:

```csharp
// src/Radio.Web/Components/Shared/NowPlayingPanel.razor — replaces the Task 1a property
@inject TimeProvider Clock

// src/Radio.Web/Program.cs — near :419, the EncoderHudService registration
builder.Services.AddSingleton(TimeProvider.System);

// tests/Radio.Web.Tests/TestHelpers/HermeticTestRig.cs — inside AddHermeticTestRig, :55
services.AddSingleton(TimeProvider.System);
```

The third line is what keeps all 45 existing renders working, since both bUnit fixtures already call
`AddHermeticTestRig()`. **Two things must then be true and are not free:** `NowPlayingPanelVolumeDebounceTests`
must set `Clock` on its bare instance or NRE (`C-118`), and the codebase's *"nothing registers
`TimeProvider`"* statement — three comments and a standing test (`C-117`) — becomes true of `Radio.API`
only and should be amended in `AudioServiceExtensions.cs` in the same PR rather than left to rot.

### 6.4 The stronger, gated harness — if §4.7's repeat run still flakes

`TEST-4`'s full form blocks the component at its dependency instead of merely observing it. Add to
`RecordingHandler`:

```csharp
  private TaskCompletionSource? _hold;

  /// <summary>Blocks matching requests inside SendAsync until <see cref="Release"/> is called, so
  /// assertions run while the panel is frozen mid-hop rather than merely after it.</summary>
  public void HoldMatching() => _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);

  public void Release() => Interlocked.Exchange(ref _hold, null)?.TrySetResult();
```

…awaited at the top of `SendAsync` before recording. Only reach for this if the observation-only
rendezvous is measured insufficient; it costs a day, not an afternoon, and the simpler form should be
sufficient because the fake clock has already removed the only source of *extra* callbacks.

### 6.5 The culture-sensitive URLs

`C-126`, filed in Task 7. Out of scope: it is an `AudioApiService` correctness bug that wants its own
row, its own sweep of the sibling clients, and a culture-varying test.

---

## 7. Self-review

### 7.1 Verified first-hand at `656f58e6`

- Both timer bodies, both callbacks, the shared `DisposeAsync`, and the third timer — read and quoted
  in §0.2, line numbers from the tree.
- `NowPlayingPanel` has **exactly one** production render site (`Home.razor:12`) and takes no
  parameters there.
- `TimeProvider` appears **nowhere** in `src/Radio.Web/Program.cs`; `EncoderHudService` is registered
  bare at `:419`; the three "deliberately unregistered" comments are at
  `AudioServiceExtensions.cs:414-415`, `:458-459`, `:494-496`; the standing test is
  `EncoderHudServiceTests.cs:466-485`.
- `InternalsVisibleTo("Radio.Web.Tests")` at `Radio.Web.csproj:31`, unconditional.
- `Microsoft.Extensions.TimeProvider.Testing` 9.10.0 at `Radio.Web.Tests.csproj:26`; `FakeTimeProvider`
  already used in four classes in that project.
- Hop counts: `ConfigurationApiService.cs:88,95` (two) vs `AudioApiService.cs:236` (one).
- 33 `RenderComponent<NowPlayingPanel>()` and 12 `RenderComponent<Home>()`; both fixtures call
  `AddHermeticTestRig()`.
- **The gain timer has no tests today** — a repo-wide grep for `OnGainSliderChanged`,
  `SetSourceGainAsync` and `GainDebounce` under `tests/` returns nothing.

### 7.2 Not verified, and what it costs

- **`FakeTimeProvider`'s exact callback threading** was reasoned about, not executed. The plan
  deliberately does not depend on whether the `async void` callback completes inline: the rendezvous is
  correct whether it does or not, and `C-127`'s `RunContinuationsAsynchronously` covers the inline case.
- **Whether the two HTTP hops complete synchronously** through `RecordingHandler` — irrelevant by the
  same argument, and deliberately so. A plan that assumed "it's fast enough" would be the defect this
  row exists to remove, one level up.
- **`FakeTimeProvider.Advance(exactly the due time)` fires the timer.** Documented behaviour (timers due
  at or before the new time fire), not executed here. If it turns out to be strictly-greater-than, add
  1 ms to the advances — a mechanical fix, and the `- 1 ms` negative tests would then need `- 2 ms`.
- **No build or test run was performed.** This session is read-only by instruction; a Builder's Task 3
  gate is the first real execution.

### 7.3 What would falsify the central decision

Option 3 is right only while *"nothing registers `TimeProvider`"* remains a rule this codebase wants. If
the owner would rather have `TimeProvider` in the container — a defensible position, and the more
conventional .NET one — then Option 1 is better, §6.3 is the whole change, and the three comments in
`AudioServiceExtensions.cs` need amending in the same PR. **The seam choice is the only reversible-cost
decision in this plan; everything from Task 4 onward is identical under either option.**

---

## Queue row wording

`docs/BUILDER_QUEUE.md`, row `TEST-7` (`:152`) — **Planner did not apply these; a Builder held the file.**

**Plan cell** (currently `—`) becomes:

```
[`design/plans/TEST-7-timeprovider-seam-for-nowplayingpanel.md`](../design/plans/TEST-7-timeprovider-seam-for-nowplayingpanel.md)
```

**Branch cell** (currently `—`) becomes:

```
`fix/test-7-nowplayingpanel-timeprovider-seam`
```

**Append to the end of the row's description cell**, before the closing `|` — three findings that change
what the Builder does:

```
⚠ **PLANNED 2026-09-05, and the seam is NOT the one the row assumed.** `EncoderHudService`'s idiom is an optional **constructor** parameter, and a Blazor component has no constructor — the renderer activates it parameterlessly. `@inject TimeProvider` is the obvious substitute and is wrong twice: it is a *required* resolve against a container that registers `TimeProvider` **nowhere, in either host, deliberately** (`AudioServiceExtensions.cs:414-415`, `:458-459`, `:494-496`, with a standing check at `EncoderHudServiceTests.cs:466-485`), and it leaves `Clock` **null on a bare `new NowPlayingPanel()`** — which is exactly how this row's own test file builds the panel (`:78`). The plan lands `internal TimeProvider Clock { get; set; } = TimeProvider.System;` instead: zero DI change, zero fixture change, and `Radio.Web.csproj:31` already grants the test project `InternalsVisibleTo` under a comment blessing this use. Plan §6.3 carries the `@inject` variant verbatim if the owner prefers it. ⚠ **A THIRD raw timer in the same file is deliberately out of scope** — `_nowPlayingPollTimer` (`:507`, created `:542-546`, 60 s/60 s) is armed in `OnInitializedAsync`, the one path all **45** bUnit renders of the panel and of `Home` traverse; filed in `design/FUTURE-WORK.md`. ⚠ **The gain timer has NO tests today** — a repo-wide grep for `OnGainSliderChanged` / `SetSourceGainAsync` under `tests/` returns nothing — so its six tests are new coverage, not a repair. **Found while planning and NOT fixed here:** `AudioApiService.cs:236` (`{gain:F2}`) and `:96` build URLs with **current-culture** number formatting, so a comma-decimal locale emits `/api/audio/sourcegain/FilePlayer/0,25`; the gain tests therefore assert on `_pendingGainValue`, never on the request path. **Measurable outcome for the PR body:** the three racing tests sleep **6.0 s** today (1.5 + 1.5 + 3.0) and none after, and the determinism gate is `TEST-4`'s — 200/200 under CPU saturation, not one green run.
```
