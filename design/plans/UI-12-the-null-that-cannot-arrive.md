# PLAN — `UI-12` · The null cannot arrive, the guards were copied from a deleted event, and the row's reason for not adding one is false

> **Row:** `UI-12`, [`docs/queue/UI-12.md`](../../docs/queue/UI-12.md). 🔵 **P3.** Filed 2026-09-08 by the `UI-7` Builder as its `L-8`.
> **Branch:** `fix/ui-12-radiostatechanged-guard-shape` (unchanged — still accurate).
> **Estimate:** **0.5 d.**
> **Auto-mergeable on green gates.** §0.9 confirms the row's guess and narrows what UAT can mean here.
> **Planned against** `main` at **`56872b55`**. Every line number below was read out of the tree at that commit.
> **Nothing on the box was touched.** No deploy, no restart, no config write. This row is entirely `src/Radio.Web` + `tests/`.

---

## 0. Read this before Task 1

### 0.1 ⚠⚠ `C-401` — THE ROW'S STATED REASON FOR NOT ADDING A GUARD IS FALSE

[`docs/queue/UI-12.md:19-24`](../../docs/queue/UI-12.md) says:

> **Adding `if (dto is null) return;` would silently drop a broadcast the panel receives today.**
> […] the current behaviour *delivers* that event and the UI *handles* it. A guard would convert a
> visible, working broadcast into a silent no-op.

**The UI does not handle it. On a null payload the UI throws, and three of its four subscribers wipe
their cached radio state to null.** Read out of the tree at `56872b55`:

| Subscriber | Line | What a null does |
|---|---|---|
| `RadioControlPanel.razor` | **`:1053`** — `if (dto.RdsRelevantChanged)` | ⭐ **NullReferenceException.** `_radioState = dto` at `:1047` is safe; the very next statement dereferences |
| `NowPlayingPanel.razor` | `:646` — `_radioState = dto;` | Wipes the cached state, then `InvokeAsync(StateHasChanged)` at `:647` repaints the panel **without** frequency, signal meter or STEREO badge |
| `RadioPage.razor` | `:337` — `_radioState = dto;` | Same wipe, same repaint at `:338` |
| `AudioStateStore.cs` | `:241` — `RadioState = dto;` | Same wipe, then forwards the null on at `:242` |

There is **no code path anywhere in `src/Radio.Web` that treats a null `RadioStateDto` as meaningful.**
Its readers are all null-*safe* (`RadioControlPanel.razor:1029` returns `false` from `IsActivePreset`
when `_radioState == null`) — which is not the same as null being *data*. Null degrades the panel; it
never carries information.

So the row's sentence is wrong twice over:

1. **"a broadcast the panel receives today"** — the panel receives an exception, caught and logged by
   `NotifyAsync<T>` (`AudioStateHubService.cs:568-586`), plus a blanked readout on three other
   surfaces.
2. **"a visible, working broadcast"** — a swallowed `NullReferenceException` and a state wipe is the
   *precise* failure mode this week was spent removing: a failure wearing the costume of correct
   handling. The row invokes that principle to protect an instance of it.

📌 **The row's *conclusion* survives; its *reason* does not.** "Do not just add the guard" is still
right — but for the reason in §0.2, not the one the row gives. This distinction is the whole reason
this section exists: a plan that inherited the row's reasoning would reach the right shape by luck and
would defend it with an argument that falls over on first contact with `RadioControlPanel.razor:1053`.

### 0.2 `C-402` — the null cannot arrive, so nothing here is a live defect

The row's scope question 1 and the brief's re-tier condition both hinge on this. **Enumerated by
mechanism, not by a receiver-named grep**, per `UI-7` §0.3's lesson:

| # | Mechanism | Result |
|---|---|---|
| 1 | `event` raise sites in C# | ⭐ **A `public event` can only be raised inside its declaring type.** `AudioStateHubService` is declared **once** and is **not `partial`** (`AudioStateHubService.cs:33`), so the language guarantees every raise site is in that file. There is exactly **one**: `:206` |
| 2 | Literal-name send sites, server | **one** — `AudioStateUpdateService.cs:478-479` |
| 3 | Strongly-typed hub clients (`IHubContext<THub,TClient>`, `Clients.All.RadioStateChanged(dto)`) — invisible to a string grep | ⭐ **zero.** Both hubs are untyped `: Hub` (`AudioStateHub.cs:17`, `AudioVisualizationHub.cs:18`). Nothing can hide from the grep here |
| 4 | Constant / `nameof` indirection at a send site | **zero.** All 20 `SendAsync` sites in `src/Radio.API/` pass a string literal |
| 5 | Centralised broadcast helper | **none exists** |
| 6 | Another process publishing to `/hubs/audio` | `AudioStateHub` exposes no client-callable method that broadcasts — only the two group joins the client calls at `AudioStateHubService.cs:353`/`:394` |

The sole producer, verified by reading it:

```csharp
// src/Radio.API/Services/AudioStateUpdateService.cs:460-482
private async Task CheckRadioStateAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
{
  if (activeSource is not IRadioControl radioControls) { return; }
  var currentRadioState = radioControls.MapToRadioStateDto(_currentMatchId);
  if (HasRadioStateChanged(_lastRadioState, currentRadioState))
  {
    currentRadioState.RdsRelevantChanged = HasRdsRelevantChanged(_lastRadioState, currentRadioState);
    _lastRadioState = currentRadioState;
    await _hubContext.Clients.Group("RadioState")
      .SendAsync("RadioStateChanged", currentRadioState, cancellationToken);
```

`MapToRadioStateDto` returns non-nullable and its body is a single `return new RadioStateDto { … }`
(`RadioStateMapper.cs:48-51`) — no path returns null. And `:475` dereferences `currentRadioState`
*before* the send, so a null would already have thrown.

✅ **Therefore: no re-tier.** The brief's stop condition — "if a null can actually reach the UI today"
— is **not met**. This stays a 🔵 P3 consistency row.

⚠ **But "our server cannot send null" is not the same as "the parameter cannot be null."** The lambda
parameter comes out of a **JSON deserializer across a process boundary**, and C# nullable annotations
are erased at runtime. A JSON `null` on the wire binds to `default(RadioStateDto)` — null — and the
non-nullable annotation at `:194` does nothing to stop it. What makes the null unreachable is a
property of `radio-api`'s current source, not a property the type system is enforcing. That is the
gap the guards are about, and §0.4 is why it should be closed rather than deleted.

### 0.3 `C-403` — the `Encoder*` guards are copied boilerplate from an event that no longer exists

The row's scope question 3 said to find out why they exist before assuming they are the anomaly. The
answer, from `git log -S` on the guards' *original* text — `&& dto != null`, not the post-`UI-7`
`if (dto != null)`, which is why a search for the latter finds only `UI-7`:

| SHA | Date | What happened |
|---|---|---|
| `db132192` | — | `RadioStateChanged` typed parameterless → `Func<RadioStateDto, Task>`. ⭐ **No guard added** |
| **`2cc41567`** | **2026-09-01** | **ORIGIN.** `ENC-9a` (#491) types `VisualizationModeChanged` and writes `if (VisualizationModeChanged != null && dto != null)` in the same edit |
| `09616183` | 2026-09-02 | `ENC-0` (#506) types `EncoderConnectionChanged`, copies the shape **verbatim** |
| `29acc013` | 2026-09-02 | `ENC-4` adds `EncoderHudChanged` — guard present in its first-ever revision |
| `8df35ddc` | 2026-09-02 | `ENC-12` (#535) adds `EncoderConfigStatusChanged` — likewise guarded from birth |
| `01220d0c` | — | `ENC-9` (#549) **deletes `VisualizationModeChanged` entirely.** ⭐ The pattern's originator is gone; the three copies survive |
| `25980dd7` | 2026-09-08 | `UI-7` (#620) splits the compound guard and writes the "must survive" comment ×3 |

**No commit message states a reason.** `ENC-9a`'s message is long and detailed — it explains the
string-vs-enum wire choice and the direct `_currentMode` assignment — and says **nothing** about the
null guard. Nor was it a review suggestion: PR #491's only review comment is about visualization hub
re-subscription, checked directly.

⭐ **The decisive evidence that neither shape was reasoned about:** `db132192` performed the *identical*
parameterless→typed-DTO transformation on `RadioStateChanged`, in the same file, and did not add a
guard. Same problem shape, opposite outcome, no incident either way.

⚠ **And the justification comment at `AudioStateHubService.cs:244-247`/`:258-261`/`:272-275` was
written seven days after the guards, by `UI-7`, while refactoring around them.** It is a *post-hoc*
rationale, not the reason they were written. It happens to be a good rationale (§0.4 adopts it) — but
it must not be cited as evidence of intent, and this plan does not cite it that way.

**What this does NOT license.** Boilerplate origin makes the guards *unjustified by history*; it does
not make them *wrong*. §0.4 decides on the merits.

### 0.4 The decision: guard all four, honestly and audibly — do not delete the three

**Direction: keep a guard on all four events, make the wire's nullability visible in the type, route
all four through one shared helper, and log the rejection at Warning.**

The rule, derivable from declarations already in this file rather than invented here:

> ⭐ **An event declared `Func<T, Task>` cannot represent null, so a null payload must be rejected at
> the boundary. An event declared `Func<T?, Task>` treats null as data and must pass it through.**

Applied to the file as it stands (`AudioStateHubService.cs:58-106`):

| Event | Declared payload | Null means | Boundary action |
|---|---|---|---|
| `RadioStateChanged` | `RadioStateDto` — **non-nullable** | nothing; it is a contract violation | **reject + log** |
| `EncoderConnectionChanged` | `EncoderConnectionDto` — **non-nullable** | nothing | **reject + log** |
| `EncoderConfigStatusChanged` | `EncoderConfigStatusDto` — **non-nullable** | nothing | **reject + log** |
| `EncoderHudChanged` | `EncoderHudDto` — **non-nullable** | nothing | **reject + log** |
| `NowPlayingChanged` | `NowPlayingDto?` — **nullable** | ⭐ *nothing is playing* | **pass through** |
| `VolumeChanged` | `VolumeDto?` — **nullable** | absent volume snapshot | **pass through** |
| `EventPlaybackChanged` | `EventPlaybackSnapshotDto?` — **nullable** | ⭐ *no attended playback* | **pass through** |

**Why not remove the three guards** (the row's suggested alternative): removing them would delete a
correct-if-currently-unreachable defence and leave four handlers asserting a non-nullability that the
deserializer does not enforce (§0.2). The asymmetry would be resolved in the direction of *less*
truth.

**Why not leave `RadioStateChanged` alone:** it is the one event whose null path is an NRE plus a
three-surface state wipe (§0.1).

**Why the log line matters:** it answers the row's fear *on the row's own terms*. The row objects to a
**silent** drop. A rejection that logs at Warning, naming the event and the type, is not silent — and
it replaces N swallowed `NullReferenceException`s (one per subscriber per circuit) with one
deliberate, greppable line.

### 0.5 ⛔ `C-404` — the obvious unification is a serious defect. Do not put the guard in `NotifyAsync<T>`

The brief's scope question 4 asks whether `UI-7`'s fan-out changed where a guard would live. It did
change the surroundings — and it created a trap that looks exactly like the tidy this row wants.

`NotifyAsync<T>(Func<T,Task>? handler, T arg)` (`:568-586`) is generic and already handles all seven
payload-carrying events. Adding `if (arg is null) return;` to it would unify all four guards in one
line and delete three copies. **⛔ It would also silently drop every `NowPlayingChanged(null)`,
`VolumeChanged(null)` and `EventPlaybackChanged(null)` broadcast** — the three events where null is
*meaningful data*, per the table in §0.4.

The consequence is concrete: `NowPlayingChanged(null)` means "nothing is playing." Dropping it strands
the dock and the panel displaying the *previous* track forever. **That is precisely the defect the row
warns about — silently dropping a working broadcast — and `NotifyAsync<T>` is the one place in this
file where it could actually happen.**

⚠ **So the guard must stay ABOVE the fan-out, in the per-event handler, where the payload's declared
nullability is still known.** `NotifyAsync<T>`'s `T` has been erased to a type parameter by the time
it runs; it cannot tell the two cases apart, and it must not try.

### 0.6 ⚠⚠ `C-405` — the guard is UNREACHABLE FROM ANY TEST IN THE ASSEMBLY. This is the real work of the row

**The row and the brief both require the change to fail first. With today's seams that is impossible**,
and the repo already says so in the file where the test would go:

```
// tests/Radio.Web.Tests/Services/AudioStateHubServiceTests.cs:148-158
// ⚠ WHAT THIS DOES NOT PROVE […] It does NOT show that a delivered "EventPlaybackChanged" message
// reaches this event with its payload intact. No test in this assembly can: the fixture runs on
// OfflineHubTransport, the connection is never started, and there is no in-tree precedent for
// reflecting into HubConnection's handler table to inject one […]
```

Two independent reasons, both fatal to a naive test:

1. **The guard lives inside the `_hubConnection.On<T>(…)` lambda.** Reaching it needs a *started*
   `HubConnection` delivering a message. Every fixture uses `OfflineHubTransport`
   (`HermeticTestRig.cs:91-97`), which fails every request synchronously and by design.
2. ⭐ **`HubEventFire` fires the EVENT, which is downstream of the guard.**
   `HubEventFire.FireAsync(hub, "RadioStateChanged", dto)` reflects the compiler-generated backing
   field and invokes subscribers directly (`HubEventFire.cs:54-61`). **It never executes the `On<>`
   lambda at all.** A test written through it would pass identically whether the guard is present,
   absent, or inverted.

⛔ **A test that asserts through `HubEventFire` proves nothing about this row and must not be
written.** That is the same trap `UI-7` `C-213` documented — a harness that reports success against an
unfixed implementation — and it would land here in a new disguise.

**Therefore Task 1 builds the seam, and it is the substance of the change, not scaffolding.** The
house idiom already exists: `AudioStateStore` solved the identical problem by extracting `internal`
per-message methods, and says so at `AudioStateStore.cs:234-237` —

> `Radio.Web.csproj` already declares `InternalsVisibleTo("Radio.Web.Tests")`. A field-like event
> cannot be raised from outside the type that declares it, so a test holding an `AudioStateHubService`
> cannot make `RadioStateChanged` fire, and this handler has no public twin […]

`AudioStateStore.OnHubRadioStateChanged` (`:239`) is that extraction. **This row applies the same
pattern one layer up, to the hub client.**

### 0.7 What must not regress

| Guard | Where | Why it constrains this row |
|---|---|---|
| `AsyncEventFanOutLintTests` | `tests/Radio.Core.Tests/` | Every event on this class must still be raised via `NotifyAsync`. ⚠ Its floors are **`declaringFiles >= 5`** and **`fanOutSites >= 5`** (`:110-117`) — this row changes neither, but a refactor that moved a declaration would trip them |
| The lint's direct-call arm | `AsyncEventFanOutLintTests.cs:272-275` | `(?<![\w.])<EventName>\s*\(` — ⚠ an extracted method **must not** be named exactly after an event. The names in Task 1 are all `On…MessageAsync`, so the preceding `n` blocks the lookbehind |
| Class remarks | `AudioStateHubService.cs:17-32` | Claims every event is raised through `NotifyAsync`. Still true after this row — verify rather than assume, per `CLAUDE.md` § *Pre-Merge Review* |
| `HubEventFire`'s backing-field lookup | `HubEventFire.cs:86-100` | ⛔ **Do not convert any event to explicit `add`/`remove` accessors** — that deletes the compiler-generated field and breaks every caller (`UI-7` `C-207`) |
| Release warning baseline | **47 warnings, 0 errors** | `CLAUDE.md`. The gate is *equality*, not an absolute |

### 0.8 Anchors that could not be verified

- ⚠ **`RadioPage.razor:252` and `NowPlayingPanel.razor:559`/`:1166`** (the `+=`/`-=` sites) are
  reported from the census, not read line-by-line. The *handler bodies* at `RadioPage.razor:333-339`
  and `NowPlayingPanel.razor:640-648` **were** read directly and are quoted verbatim in §0.1. Builder:
  re-derive the `+=` line numbers before editing near them — **`UI-9` (#628, `c729fe4b`) landed in
  `SystemConfigPage.razor` on 2026-09-08** and this plan is written one commit after it.
- **SignalR's binding behaviour for a zero-argument send against `On<T>`** is asserted from the
  protocol contract, not measured. No such send exists for these four events, so it is not on any path
  this row changes.

### 0.9 Auto-merge

✅ **The row's guess is confirmed: auto-mergeable on green gates.** `src/Radio.Web` + `tests/` only; no
hardware, no live-audio path, no migration, no production config.

⚠ **But narrow what UAT can mean here.** The change is **unobservable in production** — §0.2 shows the
null cannot arrive, so on the appliance the before and after behaviour are byte-for-byte identical on
every reachable path. UAT can therefore only confirm **no regression on the normal path**: the radio
panel still tracks frequency, band, signal and the RDS marquee. It **cannot** demonstrate the guard,
and a UAT report claiming it did is wrong. The guard's evidence is Task 3's unit tests and nothing
else.

---

## 1. Task 1 — extract the four handler bodies into `internal` methods, and make the wire type nullable

**Why:** §0.6. Without this seam no assertion in this row can run. Making the `On<T>` type argument
nullable is not cosmetic — it is the type finally agreeing with what the deserializer can produce
(§0.2).

**File:** `src/Radio.Web/Services/Hub/AudioStateHubService.cs`

**1a.** Replace the `RadioStateChanged` registration (`:189-207`) with:

```csharp
      // Server sends RadioStateChanged with a RadioStateDto payload —
      // deserialize and pass through so subscribers can read NowPlayingMatchId
      // directly. (Previously the payload was discarded and subscribers
      // re-fetched via REST, which strips NowPlayingMatchId and silently
      // broke the recognition stream's NOW-row anchor.)
      //
      // ⚠ The type argument is RadioStateDto? — NULLABLE — and that is deliberate. The payload
      // arrives from a JSON deserializer across a process boundary, where C# nullable annotations
      // are erased. A JSON `null` binds to default(T) whatever this file annotates. Declaring it
      // non-nullable did not prevent a null; it only hid that one was representable (UI-12 §0.2).
      _hubConnection.On<RadioStateDto?>("RadioStateChanged", OnRadioStateMessageAsync);
```

**1b.** Replace the three `Encoder*` registrations (`:238-280`) with:

```csharp
      // Server sends EncoderConnectionChanged when encoder device connects/disconnects
      _hubConnection.On<EncoderConnectionDto?>("EncoderConnectionChanged", OnEncoderConnectionMessageAsync);

      // Server sends EncoderConfigStatusChanged when the configuration tier changes (ENC-12).
      _hubConnection.On<EncoderConfigStatusDto?>("EncoderConfigStatusChanged", OnEncoderConfigStatusMessageAsync);

      // Server sends EncoderHudChanged when a knob acts (ENC-4).
      _hubConnection.On<EncoderHudDto?>("EncoderHudChanged", OnEncoderHudMessageAsync);
```

**1c.** Add the four methods plus the shared helper, immediately above `NotifyAsync(Func<Task>?)`
(before `:534`):

```csharp
  /// <summary>
  /// Rejects a null payload for an event whose delegate cannot represent one, logging the rejection.
  /// Returns true when the payload may be dispatched.
  /// </summary>
  /// <remarks>
  /// ⚠⚠ THIS BELONGS HERE AND NOT IN <see cref="NotifyAsync{T}"/>, AND THE DIFFERENCE IS A REAL
  /// DEFECT, NOT A STYLE PREFERENCE (queue row `UI-12` §0.5). Three events on this class carry a
  /// DELIBERATELY NULLABLE payload where null is meaningful data: NowPlayingChanged
  /// (Func&lt;NowPlayingDto?, Task&gt;) means "nothing is playing", VolumeChanged and
  /// EventPlaybackChanged likewise. NotifyAsync&lt;T&gt;'s T is erased to a type parameter and cannot
  /// tell those apart from the four non-nullable ones, so a null check there would strand the dock
  /// and the panel showing the PREVIOUS track forever — silently dropping a working broadcast, which
  /// is the exact defect `UI-12` was filed to avoid causing.
  ///
  /// ⛔ Do NOT "simplify" this by moving the check down into the fan-out.
  ///
  /// ⚠ IT IS NOT SILENT, and that is the point rather than a nicety. The row objected to a silent
  /// drop. Before `UI-12` a null RadioStateDto reached RadioControlPanel.razor:1053
  /// (`if (dto.RdsRelevantChanged)`) and threw a NullReferenceException that NotifyAsync swallowed
  /// per subscriber, while NowPlayingPanel, RadioPage and AudioStateStore each wiped their cached
  /// radio state to null. This replaces N swallowed exceptions with one greppable line.
  ///
  /// 📌 No known producer can trigger this today: the sole server-side sender is
  /// AudioStateUpdateService.cs:478-479, which is provably non-null (`UI-12` §0.2). This guards the
  /// WIRE, which is untyped, not our current server, which is correct.
  /// </remarks>
  private bool AcceptPayload<T>(string eventName, [NotNullWhen(true)] T? payload) where T : class
  {
    if (payload is not null)
    {
      return true;
    }

    _logger.LogWarning(
      "Discarded a {EventName} broadcast with a null payload — its subscribers are typed to receive "
      + "a non-null {PayloadType}. The sender violated the hub contract.",
      eventName, typeof(T).Name);
    return false;
  }

  /// <summary>Applies one "RadioStateChanged" broadcast.</summary>
  /// <remarks>
  /// ⚠ internal for the test seam, exactly as <see cref="Radio.Web.Services.AudioStateStore"/> does
  /// at its own :234-243 and for the same reason: a field-like event cannot be raised from outside
  /// the type that declares it, and the SignalR On&lt;T&gt; lambda cannot be reached without a
  /// started HubConnection. Radio.Web.csproj already declares InternalsVisibleTo("Radio.Web.Tests").
  /// </remarks>
  internal async Task OnRadioStateMessageAsync(RadioStateDto? dto)
  {
    _logger.LogDebug("Received RadioStateChanged event");
    if (!AcceptPayload(nameof(RadioStateChanged), dto))
    {
      return;
    }

    await NotifyAsync(RadioStateChanged, dto);
  }

  /// <summary>Applies one "EncoderConnectionChanged" broadcast. ⚠ internal for the test seam.</summary>
  internal async Task OnEncoderConnectionMessageAsync(EncoderConnectionDto? dto)
  {
    _logger.LogDebug(
      "Received EncoderConnectionChanged: IsConnected={IsConnected}, WasEverConnected={WasEver}",
      dto?.IsConnected, dto?.WasEverConnected);
    if (!AcceptPayload(nameof(EncoderConnectionChanged), dto))
    {
      return;
    }

    await NotifyAsync(EncoderConnectionChanged, dto);
  }

  /// <summary>Applies one "EncoderConfigStatusChanged" broadcast. ⚠ internal for the test seam.</summary>
  internal async Task OnEncoderConfigStatusMessageAsync(EncoderConfigStatusDto? dto)
  {
    _logger.LogDebug("Received EncoderConfigStatusChanged: {Status}", dto?.Status);
    if (!AcceptPayload(nameof(EncoderConfigStatusChanged), dto))
    {
      return;
    }

    await NotifyAsync(EncoderConfigStatusChanged, dto);
  }

  /// <summary>Applies one "EncoderHudChanged" broadcast. ⚠ internal for the test seam.</summary>
  /// <remarks>
  /// No log line per message — this arrives at up to 20 Hz while a knob is moving (ENC-4). The
  /// rejection path in <see cref="AcceptPayload{T}"/> logs, but only on a payload that cannot occur.
  /// </remarks>
  internal async Task OnEncoderHudMessageAsync(EncoderHudDto? dto)
  {
    if (!AcceptPayload(nameof(EncoderHudChanged), dto))
    {
      return;
    }

    await NotifyAsync(EncoderHudChanged, dto);
  }
```

**1d.** Add to the using block at the top of the file (`:1-5`):

```csharp
using System.Diagnostics.CodeAnalysis;
```

⚠ **`[NotNullWhen(true)]` is load-bearing, not decoration.** Without it the compiler cannot know
`dto` is non-null after `AcceptPayload` returns true, and `NotifyAsync(RadioStateChanged, dto)` — whose
`T` is inferred as the non-nullable `RadioStateDto` — would raise `CS8604`. With warnings-as-errors in
Release (`CLAUDE.md` § *Code Style*) that is a build failure, not a warning.

---

## 2. Task 2 — correct the two comments this change falsifies

**Why:** `CLAUDE.md` § *Pre-Merge Review* — a comment must assert only what the code does, and this row
deletes the subject of two of them. A wrong comment survives the code it described.

**2a.** Delete the `⚠ ASYMMETRY, PRE-EXISTING AND DELIBERATELY PRESERVED` block
(`AudioStateHubService.cs:197-205`). It documents an asymmetry that no longer exists, and its reasoning
is the one §0.1 falsifies. It is replaced by the remarks on `AcceptPayload` written in Task 1c.

**2b.** The three `The payload guard is NOT a subscriber guard and must survive` comments
(`:244-247`, `:258-261`, `:272-275`) are removed with the inline guards they annotate. ⚠ **Their
substance is preserved** — it is the second paragraph of `AcceptPayload`'s remarks. Do not simply
delete the reasoning; `UI-7` wrote it to stop a future reader collapsing the payload guard into the
subscriber guard, and that hazard is unchanged.

**2c.** Verify — do not assume — that the class remarks at `:17-32` are still true. They claim every
event is raised through `NotifyAsync`, which this row preserves: the raise sites moved from lambdas
into named methods, but every one still calls `NotifyAsync`. **No edit expected. Confirm and say so.**

---

## 3. Task 3 — the tests, and they must fail first

**File:** `tests/Radio.Web.Tests/Services/AudioStateHubServiceNullPayloadTests.cs` (new)

⛔ **Do NOT write these through `HubEventFire`** — §0.6. It fires the event, not the handler, and would
pass against every possible version of this change.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The hub client's null-payload contract at the SignalR boundary (queue row `UI-12`).
/// </summary>
/// <remarks>
/// ⚠⚠ THESE DRIVE THE On&lt;T&gt; HANDLER BODY, NOT THE EVENT, AND THE DIFFERENCE IS THE WHOLE TEST.
/// `HubEventFire` reflects the compiler-generated backing field and invokes subscribers directly
/// (HubEventFire.cs:54-61), which is DOWNSTREAM of the guard — a test written that way passes
/// whether the guard is present, absent or inverted. That is `UI-7` `C-213`'s failure mode wearing a
/// new disguise. The internal On…MessageAsync seam (UI-12 Task 1) exists so these assertions can run
/// at all; before it, no test in this assembly could reach the guard (AudioStateHubServiceTests.cs
/// :148-158 records exactly that).
///
/// ⚠ NO ASSERTION HERE USES A WALL CLOCK — `CLAUDE.md` § *Test Timing*. Every handler is awaited to
/// completion before anything is asserted, so each observation is a fact about control flow.
///
/// 📌 What these do NOT prove: that a null can ever arrive. It cannot — the sole server-side sender
/// is provably non-null (UI-12 §0.2). These pin the BOUNDARY's behaviour if one ever does.
/// </remarks>
public class AudioStateHubServiceNullPayloadTests
{
  private static AudioStateHubService NewHub(List<(LogLevel Level, string Message)> sink) =>
    new(
      new CapturingLogger<AudioStateHubService>(sink),
      new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());

  private static RadioStateDto RadioState() =>
    new(98.5, "FM", 0.2, 40, false, null, 0.5, 20, false, "Rock", 75);

  // --- The headline: a null payload never reaches a subscriber ---------------------------------

  /// <summary>
  /// ⭐ THE DISCRIMINATING TEST. Before `UI-12` this subscriber received null and
  /// RadioControlPanel.razor:1053 (`if (dto.RdsRelevantChanged)`) threw a NullReferenceException
  /// that NotifyAsync swallowed. Revert Task 1's guard and `received` becomes true — RED.
  /// </summary>
  [Fact]
  public async Task NullRadioStatePayloadIsNotDispatchedToSubscribers()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.RadioStateChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnRadioStateMessageAsync(null);

    Assert.False(received, "a null payload must not reach a subscriber typed Func<RadioStateDto, Task>");
  }

  /// <summary>
  /// ⭐ The half that answers the row's actual objection. The row refused a guard because it would
  /// drop a broadcast SILENTLY. Delete the LogWarning in AcceptPayload and this fails while the test
  /// above still passes.
  /// </summary>
  [Fact]
  public async Task NullRadioStatePayloadIsLoggedAsAWarningNamingTheEvent()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);

    await hub.OnRadioStateMessageAsync(null);

    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("RadioStateChanged"));
  }

  /// <summary>A real payload is still delivered, unchanged. The guard must not cost the happy path.</summary>
  /// <remarks>
  /// ⚠ This is the regression half. Task 1 rewrites the ONLY production path that dispatches
  /// RadioStateChanged; if the rewrite dropped the NotifyAsync call the two tests above would both
  /// still pass. Invert Task 1c's condition and this fails.
  /// </remarks>
  [Fact]
  public async Task NonNullRadioStatePayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = RadioState();
    RadioStateDto? seen = null;

    hub.RadioStateChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnRadioStateMessageAsync(dto);

    Assert.Same(dto, seen);
    Assert.DoesNotContain(sink, e => e.Level == LogLevel.Warning);
  }

  /// <summary>Every subscriber is still awaited — Task 1 must not have bypassed the UI-7 fan-out.</summary>
  [Fact]
  public async Task NonNullRadioStatePayloadStillReachesEverySubscriber()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var order = new List<int>();

    hub.RadioStateChanged += _ => { order.Add(1); return Task.CompletedTask; };
    hub.RadioStateChanged += _ => { order.Add(2); return Task.CompletedTask; };
    hub.RadioStateChanged += _ => { order.Add(3); return Task.CompletedTask; };

    await hub.OnRadioStateMessageAsync(RadioState());

    Assert.Equal([1, 2, 3], order);
  }

  // --- The three Encoder events keep the behaviour they already had ----------------------------

  /// <summary>
  /// The Encoder guards are PRESERVED, not deleted. These three were already true before `UI-12`
  /// (the inline `if (dto != null)` at the old :248/:262/:276) and must stay true after it — this
  /// row changed where the check lives and made it audible, not whether it happens.
  /// </summary>
  [Fact]
  public async Task NullEncoderConnectionPayloadIsNotDispatched()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.EncoderConnectionChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnEncoderConnectionMessageAsync(null);

    Assert.False(received);
    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("EncoderConnectionChanged"));
  }

  [Fact]
  public async Task NullEncoderConfigStatusPayloadIsNotDispatched()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.EncoderConfigStatusChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnEncoderConfigStatusMessageAsync(null);

    Assert.False(received);
    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("EncoderConfigStatusChanged"));
  }

  [Fact]
  public async Task NullEncoderHudPayloadIsNotDispatched()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var received = false;

    hub.EncoderHudChanged += _ => { received = true; return Task.CompletedTask; };

    await hub.OnEncoderHudMessageAsync(null);

    Assert.False(received);
    Assert.Contains(sink, e =>
      e.Level == LogLevel.Warning && e.Message.Contains("EncoderHudChanged"));
  }

  [Fact]
  public async Task NonNullEncoderHudPayloadIsDispatchedUnchanged()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var hub = NewHub(sink);
    var dto = new EncoderHudDto { EncoderIndex = 1, Label = "Volume", Phase = "Value" };
    EncoderHudDto? seen = null;

    hub.EncoderHudChanged += d => { seen = d; return Task.CompletedTask; };

    await hub.OnEncoderHudMessageAsync(dto);

    Assert.Same(dto, seen);
  }
}
```

⚠ **`RadioState()`'s eleven positional arguments must match `ApiModels.cs:310-321`'s required
parameters** (`Frequency, Band, Step, SignalStrength, IsScanning, ScanDirection, ScanStopThreshold,
Gain, AutoGain, Equalizer, DeviceVolume` — everything after `DeviceVolume` has a default). Builder:
re-derive from the record declaration rather than trusting this line, and note that
`AudioStateStoreNotifyTests.cs` already has a `RadioState()` factory that can be copied verbatim if
its shape is closer to current.

### 3.1 The RED step — run this before Task 1, and record the output

⭐ **The tests cannot be written before the seam exists, so RED is demonstrated by mutation instead.**
After Task 1 and Task 3 are written and green, apply each mutation, run only this file, and confirm the
named test fails:

| Mutation | Must fail | Must still pass |
|---|---|---|
| `M1` — delete `if (!AcceptPayload(…)) return;` from `OnRadioStateMessageAsync` | `NullRadioStatePayloadIsNotDispatchedToSubscribers` | the two Encoder null tests |
| `M2` — delete the `LogWarning` from `AcceptPayload` | both `…IsLoggedAsAWarning…` and the three Encoder tests' log assertions | `…IsNotDispatchedToSubscribers` |
| `M3` — invert `AcceptPayload` to `return payload is null` | `NonNullRadioStatePayloadIsDispatchedUnchanged` | — |
| `M4` — restore the pre-`UI-12` inline `if (dto != null)` for the Encoders and drop the shared helper | nothing — ⭐ **this is the control.** It must stay GREEN, proving the Encoder tests pin *behaviour*, not the refactor |

⚠ `M4` is the one that matters most and is easiest to skip. Without it the Encoder tests could be
asserting the shape of Task 1 rather than the behaviour `UI-7` shipped, which is exactly the
"asserts the guard exists" trap the row forbids.

---

## 4. Gates

```bash
dotnet build RadioConsole.sln -c Release > /tmp/build.log 2>&1; echo "exit=$?"
grep -cE "warning" /tmp/build.log     # must equal the 47 baseline
dotnet test RadioConsole.sln -c Release > /tmp/test.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/test.log
```

⛔ **Never pipe `dotnet test` into `tail`** — `CLAUDE.md` records a measured run that exited `0` with
five failing tests.

Known-failing on Windows and not a regression: four `SrcVariableResamplerTests`,
`NwsObservationIntegrationTests.RealNwsCall_*`, and
`CoverArtPipelineIntegrationTests.CoverArtArchive_ReturnsValidUrl_ForKnownRecording`.

**Also run explicitly**, because this row edits the file they police:

```bash
dotnet test tests/Radio.Core.Tests -c Release --filter "FullyQualifiedName~AsyncEventFanOutLintTests"
dotnet test tests/Radio.Web.Tests -c Release --filter "FullyQualifiedName~AudioStateHubService"
```

---

## 5. UAT

⚠ **Read §0.9 first: the guard is not demonstrable on the box.** UAT here is a no-regression check on
the normal path only.

1. Deploy: `./deploy/Deploy-ToLinux.ps1` (defaults are correct for `radio` since `OPS-1`).
2. Confirm both SHAs: `curl -s http://radio:5000/api/health/version` and `:5002`.
3. On the panel, tune the radio and confirm the frequency well, signal meter, STEREO badge and RDS
   marquee all still update live. That exercises all four `RadioStateChanged` subscribers.
4. Turn an encoder knob and confirm the HUD still appears — that exercises `EncoderHudChanged`.
5. `ssh mmack@radio "journalctl -u radio-web --since '-10min' --no-pager | grep -i 'null payload'"` —
   ⭐ **expect ZERO hits.** A hit would mean §0.2 is wrong and the row must be re-tiered as a defect.

⚠ Keep the journal query bounded — `CLAUDE.md` records log volume correlating with audible audio
distortion on this box.

---

## 6. Docs impact

- `docs/BUILDER_QUEUE.md` — mark `UI-12` ✅, link this plan.
- ⭐ **`docs/queue/UI-12.md` — add a correction note.** §0.1 falsifies the row's stated reason, and the
  row's text will otherwise keep teaching that a null `RadioStateDto` is "handled" by the panel. ⛔ Do
  **not** silently rewrite the row; append a dated correction, as `AUD-17` and `UI-11` did.
- No `design/FUTURE-WORK.md` or `design/INTEGRATIONS.md` change — nothing is stubbed and no integration
  surface moves.

---

## 7. What this row deliberately does not do

- ⛔ **Does not put the guard in `NotifyAsync<T>`** — §0.5. That would drop the three nullable events'
  meaningful nulls.
- ⛔ **Does not delete the dead `ConfigChanged` event.** `UI-7` §6.1 filed that separately and the same
  argument applies: a dead-code deletion inside a defect-class PR muddies a diff whose value is that it
  is mechanical.
- ⛔ **Does not change any subscriber.** `RadioControlPanel.razor:1053` stays as it is — once the
  boundary rejects null, the dereference is safe by construction, and adding a second null check there
  would re-introduce the ambiguity this row removes.
- **Does not touch `AudioStateUpdateService.cs`.** ⚠ The census surfaced an adjacent, unrelated finding
  there — `_lastRadioState = currentRadioState` at `:477` is assigned **before** the `await SendAsync`,
  so a throwing or cancelled send advances the change-detection cache and that delta is never
  re-broadcast (same shape at `:266`, `:279`, `:453-454`, `:502`). **That is a real candidate row and
  is out of scope here.** File it; do not fix it in this PR.
