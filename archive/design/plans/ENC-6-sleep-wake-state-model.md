# PLAN — `ENC-6` · Sleep, wake, and the three-state model (the non-blanking half)

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended)
> or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** Make the console's three reachable sleep states — Awake, Ambient, Standby — a fact the encoder
router can read, so that a knob turned overnight stops acting invisibly on a machine showing a clock.

**Architecture:** One new server-side fact (*is the `/sleep` route on screen?*) composes with the existing
`IsSleeping` to derive three states. `RotaryEncoderActionRouter` gates every encoder input through those
states before dispatching, and a synchronous claim latch makes a wake spend exactly one input instead of
every input that arrives during the wake.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Server, SignalR, xUnit + bUnit + FluentAssertions + Moq.

**Row:** `ENC-6` (P0, Encoders workstream) — [`docs/HANDOFF-GA-PUNCH-LIST.md` §3.5](../../docs/HANDOFF-GA-PUNCH-LIST.md)
**Spec:** [`docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md`](../../docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md)
(**Rev 5**) — **§8 in full**, plus §2.3, §3 principles 1–2, §6.10, §8.6, §15's *Sleep, wake, blanking* block.
**Relationship to the handoff:** **extends** — §8's five-state model is reduced to three by `ENC-15`
(§0.1), with **two declared deviations** and **three mechanism decisions the handoff does not make**
(§0.3, §0.4).
**Depends on:** `ENC-1` ✅ [#498](https://github.com/mmackelprang/RTest/pull/498),
`ENC-4` ✅ [#519](https://github.com/mmackelprang/RTest/pull/519) / `ENC-4c` ✅ [#526](https://github.com/mmackelprang/RTest/pull/526).
**Both shipped — dependencies are met.**
**Does NOT depend on `ENC-5` or `ENC-7`** — see §0.5, which is the reason.
**Author:** Planner, 2026-09-02.
**Effort:** 2–3 days · **10 tasks** across 4 phases.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **2-space indentation. File-scoped namespaces. Nullable reference types enabled. Explicit type
  annotations preferred.** (`CLAUDE.md` § Code Style.)
- **Warnings are errors in Release builds.** `dotnet build --configuration Release` must produce
  **0 warnings**.
- **Comments, log messages and XML docs may assert only what the code beside them actually does.**
  This repo has shipped three comment/code mismatches, two of which caused real bugs
  (`CLAUDE.md` § Pre-Merge Review). **This row fixes a fourth one** (Task 4) — do not add a fifth.
- **Greenfield project: NO backward compatibility.** Changing an API response shape that has no
  caller is free and is not a breaking change.
- **`EncoderHudPhase` travels as an open string and an unrecognised value renders nothing**
  (handoff §6.10). The same forward-compatibility rule is applied to the new wake-state string in
  Task 7 — an unknown value must fall back to the Ambient copy, never throw and never blank.
- **Branch:** `feat/enc-6-sleep-wake` (never commit to `main`).
- **Every commit message ends with:**
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
  ```

---

## 0. Read this before Task 1

### 0.1 ⚠ The blanking half of `ENC-6` DOES NOT SHIP. Do not reinstate it.

`ENC-15` was the hard predecessor of `ENC-6`'s blanking half and **its gate FAILED** on 2026-09-02
([report](../../docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md)). The mechanism is worse than the
row anticipated:

- **The touchscreen is powered by the panel and leaves the USB bus when the panel blanks** —
  `usb 3-1: USB disconnect` about a second after the blank, re-enumeration about a second after the
  unblank. There is no device left to ignore a touch, so touch **cannot** be a wake path by construction.
- **The encoder is not a compositor input device either.** `cafe:4005` has **zero evdev nodes**, only
  `/dev/hidraw3`. A knob cannot reset the GNOME idle timer or unblank a panel on its own; a knob wake
  works only if `radio-api` reads hidraw and *itself* issues the D-Bus unblank — making `radio-api` a
  single point of failure in the only remaining wake path.
- The investigation **reproduced the brick**: a ~13 s on/off oscillation the documented recovery
  command could not break.

The punch list pre-committed to the consequence — *"If touch cannot wake it, blanking does not ship
until it has two wake paths."*

> ⛔ **Therefore this plan builds NO blanking.** No DPMS, no `SetDisplayPowerAsync` call, no
> `Ambient-Dark`, no `Standby-Dark`, no re-blank timers (the handoff's "60 s from Ambient, 30 s from
> Standby"), and no encoder-presence coupling rules. **Handoff §8.5 and §15's first and last
> *Sleep, wake, blanking* checkboxes are out of scope on this row.** If a later session reads
> "ENC-6" and reaches for blanking, `design/INTEGRATIONS.md` §1 and `design/FUTURE-WORK.md` §7
> (Sleep Mode) both carry the reason it must stay off.

### 0.2 The five-state model still coheres with the two Dark states removed — here is why

Handoff §8.2 gives two rules. **Rule 1 ("if the panel is dark, the first input lights it") governs
only the two Dark states.** With those withdrawn, Rule 1 has no reachable state and drops out whole.
**Rule 2 ("once lit: VOLUME acts in place; everything else wakes to the full UI and is consumed")
governs all three surviving states**, and §8.3's table already specifies both surviving columns
completely. Nothing in the model dangles.

Two things Rule 1 carried have to be re-homed rather than dropped, and both land cleanly:

1. **"A consumed input still renders that knob's current value."** §8.3 introduces this refinement
   while describing the dark→lit transition, but §8.3's own table also marks inputs **consumed** in
   Ambient and Standby, where the panel is already lit. On a lit panel a consumed input that renders
   nothing is a knob that is inert in a reachable state, which handoff §3 principle 1 forbids
   outright and principle 2 forbids again by name (*"including inputs that are consumed rather than
   applied (§8.3)"*). **So the refinement applies to every consumed input, not only the ones that
   were consumed in the dark.** Task 6 builds it.
2. **D22 — "a turn from Standby lights the panel and does NOT resume audio; a press or a screen tap
   does."** With no dark state the panel is always lit, so *"lights the panel"* has no work left to
   do — which leaves D22's **other**, load-bearing half exactly intact and *easier* to test: a turn
   from Standby is consumed and renders that knob's value; a press or a tap resumes. Designer's
   acceptance test (*"Standby: a turn lights the panel and does not resume audio; a press resumes and
   restores the pre-sleep mute state"*) is pinned verbatim by Task 5 and by UAT scenario **D**.

**The resulting model — three states, derived from two booleans, no new state machine:**

| State | `IsSleeping` | `/sleep` on screen | Audio | Entered by |
|---|---|---|---|---|
| **Awake** | false | no | playing | any wake |
| **Ambient** | false | **yes** | **playing** | 30 min idle (`idle-dimmer.js`), or direct navigation to `/sleep` |
| **Standby** | **true** | yes | **paused + muted** | topbar Sleep pill · VOLUME long-press · `POST /api/system/sleep` |

### 0.3 Three mechanism decisions this plan makes, that the handoff does not

The handoff specifies **behaviour**; these are the **mechanisms** chosen to deliver it. A reviewer
should push on these three and nothing else is Planner's invention.

| # | Decision | Why this one |
|---|---|---|
| **M-1** | **`Sleep.razor` itself reports "the sleep screen is on screen"**, on first render and on dispose — *not* `idle-dimmer.js` reporting "I navigated because of idle". | It is the one place that knows the screen is actually up, so **all three entry paths converge on the same server-side fact**. A Builder found today that `/sleep` reached by idle and `/sleep` reached by the pill behave as *different states*; reporting from the page is what collapses that. Reporting from `idle-dimmer.js` would cover only the idle path and leave direct navigation — the route `ENC-4`'s own test instructions tell Tester to use — still invisible to the server. |
| **M-2** | **A synchronous claim latch (`TryClaimWake`) that makes `WakeState` read `Awake` from the instant the claim is taken**, rather than when `WakeAsync` completes. | The handoff asks for *"exactly one event consumed, not a window"*. Under the model in M-1 the window is **worse** than the handoff assumed, not better: leaving Ambient requires a **browser round trip** (broadcast → navigate → dispose → report), which is far longer than the `WakeAsync` await the handoff was worried about. Without the latch a fast spin loses every detent for the length of a page navigation. |
| **M-3** | **The consumed-value readout dispatches through a fourth parallel array beside `_turnHandlers` / `_pressHandlers`**, with the encoder index threaded into each publisher. | It puts the remap surface **adjacent to the two arrays `ENC-5` Task 7 already rewrites**, so the conflict is visible rather than latent, and it leaves **no index literal to chase** — which is the trap `ENC-5`'s own plan calls out (*"Do not leave a literal behind"*). Task 6 adds a test that fails if the four arrays ever stop agreeing in length. |

### 0.4 Two declared deviations from the handoff — do NOT "correct" these back

| # | Handoff says | This plan ships | Why |
|---|---|---|---|
| **D-1** | §8.6: the Standby hint reads `hold VOLUME or press any knob to turn on` | `tap anywhere, or press any knob, to turn on` | The handoff line is **true but omits the tap**, on a touchscreen, where the Ambient line's entire content is *"tap anywhere"*. §8.3's own table says a screen touch in Standby **resumes → Awake**, so a user reading the handoff's line would reasonably conclude tapping does *not* work — the screen would be asserting something false by omission, which is this repo's pre-merge comment-accuracy rule applied to copy. `hold VOLUME` is also redundant here: the press edge resumes, so a hold and a press are indistinguishable in Standby. The replacement keeps the Ambient line's leading verb so the two states read as siblings. |
| **D-2** | §15: *"Ambient re-blanks after 60 s of no input; Standby after 30 s"* | Nothing re-blanks | Withdrawn with the blanking half by `ENC-15` (§0.1). Recorded here so it is not read as an omission. |

### 0.5 Which router mapping this targets, and why the answer is "either"

`RotaryEncoderActionRouter` maps `0 = Volume · 1 = Tuning · 2 = Source · 3 = Visualization` today.
`ENC-5` remaps it to `0 = Volume · 1 = Source · 2 = Visualization · 3 = Tuning`, and `ENC-7` finishes it.

**This row is mapping-agnostic, and that is a structural property rather than a promise:**

- The sleep gate runs **before** the dispatch tables, on the raw encoder index, and never consults a
  handler.
- The **only** index it compares against is `0`, via the existing shared constant
  `RotaryEncoderConfigDefaults.VolumeEncoderIndex`. Index 0 is VOLUME under **both** tables — the
  shipped test `EncoderIndexZero_IsVolume_UnderBothTheOldAndTheNewPhysicalOrder` already pins that.
- The consumed-value readout dispatches through a parallel array that a remap reorders **alongside**
  the two arrays it already reorders (M-3).

**Ordering with `ENC-5`:** either order works. If `ENC-5` lands first, re-read
`RotaryEncoderActionRouter.cs` before Task 5 — its handlers will have gained a leading `int index`
parameter and the four publishers in Task 6 should match that shape. **Where this plan and the
merged code differ, the code wins and the PR should say where.**

### 0.6 Four things Builder must NOT do

1. ⛔ **Do NOT make `idle-dimmer.js` call `SetSleepAsync(true)`.** It looks like the one-line fix for
   the headline defect and it is wrong. Ambient is defined by audio **still playing** (§8.2); calling
   `SetSleepAsync(true)` pauses and mutes, which converts Ambient into Standby and breaks the thing
   the idle path exists for. The comment at `idle-dimmer.js:69-72` is correct and stays. The fix is
   that the server learns about Ambient *without* audio changing.
2. ⛔ **Do NOT change what `IsSleeping` means.** It is the honest "audio is parked" truth, it is what
   `GET /api/system/sleep` reports, and making it read `false` during an in-flight wake would make the
   API lie. The latch lives on the new `WakeState`, not on `IsSleeping`.
3. ⛔ **Do NOT delete `SleepService.SetDisplayPowerAsync`.** It is dead today and stays dead
   (§0.1), but it is the recorded shape of the thing `ENC-15` ruled out, and deleting it would make
   the FUTURE-WORK entry that explains *why* point at nothing. Task 4 corrects the comments around
   it instead.
4. ⛔ **Do NOT delete `EncoderHudServiceTests` or `RotaryEncoderRouterMappingTests` facts that this
   plan changes.** Two shipped assertions become wrong (Task 6, Task 8) — **update them to assert the
   new contract**, with a comment saying what changed and why. A deleted test is a coverage hole
   wearing a green check.

### 0.7 ⚠ A green suite and a dead page are indistinguishable in this repo

`SleepTests` calls `Services.AddHermeticTestRig()`, whose own comment says it *"fails every outbound
HTTP request … without touching the network."* So in bUnit, `SetSleepScreenVisibleAsync` returns
`null` and the page keeps its default state. **That is the expected bUnit result and it proves
nothing about the HTTP path.** `ENC-8` shipped a page that could never deserialize its own API
response and every suite stayed green; only UAT caught it.

Two consequences, both binding:

- Task 8's bUnit test must drive the Standby hint through the **SignalR** path
  (`FireSleepStateChangedAsync(hub, true)`), which the rig *can* exercise — not through the HTTP
  response, which it cannot.
- **The HTTP round trip is covered by UAT scenario B and by nothing else.** Task 10 is not optional.

---

## 1. File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/Radio.Core/Interfaces/ISleepService.cs` | **Modify.** Adds `ConsoleWakeState` and four members. | 1 |
| `src/Radio.API/Services/SleepService.cs` | **Modify.** The screen-visible flag, the derived state, the claim latch, the Ambient-aware wake, and the comment corrections. | 1, 2, 4 |
| `src/Radio.API/Models/SystemModels.cs` | **Modify.** `SetSleepScreenVisibleRequest` + `SleepStateResponse`. | 3 |
| `src/Radio.API/Controllers/SystemController.cs` | **Modify.** `POST /api/system/sleep-screen`; `GET /api/system/sleep` repointed at the DTO. | 3 |
| `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs` | **Modify.** The sleep gate replaces `TryWakeFromSleep`; the consumed-value readout. | 5, 6 |
| `src/Radio.Web/Models/ApiModels.cs` | **Modify.** `SleepStateDto`. | 7 |
| `src/Radio.Web/Services/ApiClients/SystemApiService.cs` | **Modify.** `SetSleepScreenVisibleAsync`. | 7 |
| `src/Radio.Web/Components/Pages/Sleep.razor` | **Modify.** Reports visibility; holds the wake state; the Standby hint. | 7, 8 |
| `src/Radio.Web/Components/Layout/MainLayout.razor` | **Modify.** Reports *not* visible on first render — the self-correcting half. | 7 |
| `design/INTEGRATIONS.md` | **Modify.** The three-state model, documented where an operator will find it. | 9 |
| `design/FUTURE-WORK.md` | **Modify.** §7 (Sleep Mode) citation repair. | 4 |
| `tests/Radio.API.Tests/Services/SleepServiceTests.cs` | **Modify.** State-derivation, latch and Ambient-wake facts. | 1, 2 |
| `tests/Radio.API.Tests/Controllers/SystemControllerTests.cs` | **Modify.** Endpoint facts (file exists; has zero sleep references today). | 3 |
| `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs` | **Modify.** `FakeSleepService` grows; the gate matrix; one shipped assertion inverts. | 5, 6 |
| `tests/Radio.Web.Tests/Components/Pages/SleepTests.cs` | **Modify.** The Standby hint fact. | 8 |

---

## Phase 1 — the three states, on the server

### Task 1: `ConsoleWakeState`, the screen-visible flag, and the claim latch

**Files:**
- Modify: `src/Radio.Core/Interfaces/ISleepService.cs`
- Modify: `src/Radio.API/Services/SleepService.cs:16-43`
- Test: `tests/Radio.API.Tests/Services/SleepServiceTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Radio.Core.Interfaces.ConsoleWakeState` (`Awake` / `Ambient` / `Standby`);
  `ISleepService.WakeState { get; }` → `ConsoleWakeState`;
  `ISleepService.IsSleepScreenVisible { get; }` → `bool`;
  `ISleepService.SetSleepScreenVisible(bool visible)` → `void`;
  `ISleepService.TryClaimWake()` → `bool`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Radio.API.Tests/Services/SleepServiceTests.cs`, inside the existing class. Follow the
file's shipped construction pattern (`audioManager: null`) — read the top of the file first for the
exact `Mock<IHubContext<AudioStateHub>>` scaffolding it already builds and reuse it verbatim.

```csharp
  [Fact]
  public void WakeState_WithNoSleepScreenAndNotSleeping_IsAwake()
  {
    var service = CreateService();

    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    Assert.False(service.IsSleepScreenVisible);
  }

  [Fact]
  public void WakeState_WithTheSleepScreenUpAndAudioPlaying_IsAmbient()
  {
    // The overnight state, and the one the machine actually reaches: the browser idled onto /sleep
    // and nothing paused audio.
    var service = CreateService();

    service.SetSleepScreenVisible(true);

    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
    Assert.False(service.IsSleeping);
  }

  [Fact]
  public async Task WakeState_WhenSleeping_IsStandbyEvenBeforeTheScreenReportsItself()
  {
    // Standby is defined by audio being parked, not by a browser having caught up. The pill calls
    // the API and only then navigates, so there is a real window where IsSleeping is true and no
    // client has reported the route yet - a knob turned in that window must not act.
    var service = CreateService();

    await service.EnterSleepAsync();

    Assert.Equal(ConsoleWakeState.Standby, service.WakeState);
    Assert.False(service.IsSleepScreenVisible);
  }

  [Fact]
  public void TryClaimWake_WhenAwake_ReturnsFalseAndBurnsNoClaim()
  {
    var service = CreateService();

    Assert.False(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
  }

  [Fact]
  public void TryClaimWake_GrantsExactlyOneClaim_AndTheStateReadsAwakeFromThatInstant()
  {
    // The latch, and the whole reason it exists: with a 10 ms poll, a dozen detents arrive before
    // the browser has left /sleep. Exactly one is spent waking; the rest must find an awake console
    // and act. A fast spin loses one detent, not twelve.
    var service = CreateService();
    service.SetSleepScreenVisible(true);

    Assert.True(service.TryClaimWake());
    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    Assert.False(service.TryClaimWake());
  }

  [Fact]
  public void SetSleepScreenVisible_False_ReleasesTheClaim()
  {
    // The claim is released by the browser confirming it left /sleep, not by WakeAsync finishing:
    // WakeAsync completes while the page is still up, and releasing there would drop the console
    // straight back into Ambient and start consuming inputs again.
    var service = CreateService();
    service.SetSleepScreenVisible(true);
    Assert.True(service.TryClaimWake());

    service.SetSleepScreenVisible(false);

    Assert.Equal(ConsoleWakeState.Awake, service.WakeState);
    service.SetSleepScreenVisible(true);
    Assert.Equal(ConsoleWakeState.Ambient, service.WakeState);
  }

  [Fact]
  public async Task EnterSleepAsync_ReleasesAnOutstandingClaim()
  {
    // Otherwise a wake that was claimed and never confirmed would leave the console permanently
    // reading Awake, and the next Standby would not consume anything.
    var service = CreateService();
    service.SetSleepScreenVisible(true);
    Assert.True(service.TryClaimWake());

    await service.EnterSleepAsync();

    Assert.Equal(ConsoleWakeState.Standby, service.WakeState);
  }
```

If the shipped file has no `CreateService()` helper, extract one from the duplicated setup at the top
of the existing four facts and use it in all of them — do not leave two construction paths.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~SleepServiceTests"
```

Expected: **compile failure** — `ConsoleWakeState`, `WakeState`, `IsSleepScreenVisible`,
`SetSleepScreenVisible` and `TryClaimWake` do not exist.

- [ ] **Step 3: Add the enum and the interface members**

Replace the entire contents of `src/Radio.Core/Interfaces/ISleepService.cs` with:

```csharp
namespace Radio.Core.Interfaces;

/// <summary>
/// Which of the console's three reachable states it is in, as the encoder router must see it.
///
/// <para>
/// Handoff §8.2 describes five. <b>The two dark states are withdrawn by <c>ENC-15</c></b>: the
/// touchscreen is powered by the panel and leaves the USB bus when it blanks, and the encoder has no
/// evdev node at all, so a blanked panel would have one application-mediated wake path rather than
/// two. Blanking does not ship, so nothing can reach a dark state and there is no enum member for
/// one. See <c>design/INTEGRATIONS.md</c> §1 and <c>design/FUTURE-WORK.md</c> §7 (Sleep Mode).
/// </para>
/// </summary>
public enum ConsoleWakeState
{
  /// <summary>Full UI. Every knob acts.</summary>
  Awake,

  /// <summary>
  /// The dim clock is on screen and <b>audio is still playing</b>. Reached by the 30-minute idle
  /// timer or by navigating to <c>/sleep</c> directly. VOLUME acts in place here; every other knob
  /// is spent waking (handoff §8.3).
  /// </summary>
  Ambient,

  /// <summary>
  /// Audio is paused and muted. Reached by the topbar Sleep pill, a VOLUME long-press, or the API.
  /// A <b>turn</b> here never resumes audio — only a press or a screen tap does (D22).
  /// </summary>
  Standby,
}

/// <summary>
/// Abstraction for sleep/standby mode management.
/// Lives in Core so Infrastructure (e.g., RotaryEncoderActionRouter) can
/// depend on it without referencing Radio.API.
/// </summary>
public interface ISleepService
{
  /// <summary>
  /// True when audio is parked — paused and muted. <b>This is the audio truth and nothing else.</b>
  /// It is deliberately <i>not</i> affected by the wake claim below: a console whose resume is in
  /// flight still has paused audio, and reporting otherwise would make
  /// <c>GET /api/system/sleep</c> lie.
  /// </summary>
  bool IsSleeping { get; }

  /// <summary>
  /// True while a client reports the <c>/sleep</c> route on screen. Set by the page itself, on first
  /// render and on dispose, so all three ways of reaching that route produce the same server-side
  /// fact.
  /// </summary>
  bool IsSleepScreenVisible { get; }

  /// <summary>
  /// The state the encoder router gates on. <b>Reads <see cref="ConsoleWakeState.Awake"/> from the
  /// instant a wake is claimed</b>, which is earlier than either <see cref="IsSleeping"/> flipping
  /// or the browser leaving the route.
  /// </summary>
  ConsoleWakeState WakeState { get; }

  Task EnterSleepAsync();
  Task WakeAsync(string wakeSource = "unknown");

  /// <summary>
  /// Records that a client has put the sleep screen on screen, or taken it off. Releases any
  /// outstanding wake claim either way, because both edges mean the transition has settled.
  /// </summary>
  void SetSleepScreenVisible(bool visible);

  /// <summary>
  /// Claims the single input that is spent waking, synchronously.
  ///
  /// <para>
  /// Returns <c>true</c> to exactly one caller per wake. Every later caller gets <c>false</c> and
  /// finds <see cref="WakeState"/> already reading <see cref="ConsoleWakeState.Awake"/>, so its
  /// input acts instead of being discarded. Returns <c>false</c> immediately when the console is
  /// already awake, without burning a claim.
  /// </para>
  /// </summary>
  bool TryClaimWake();
}
```

- [ ] **Step 4: Implement the state and the latch in `SleepService`**

In `src/Radio.API/Services/SleepService.cs`, add to the field block (after `private bool _wasPlayingBeforeSleep;`):

```csharp
  // Set by the /sleep page reporting itself, cleared by that page disposing or by MainLayout
  // rendering. Written from request threads and read from the encoder thread, so it is volatile
  // rather than lock-guarded: it is one independent bool and taking _lock to read it would put an
  // await on the encoder input path.
  private volatile bool _isSleepScreenVisible;

  // 1 once a wake has been claimed and has not yet been confirmed by the browser leaving the route.
  private int _wakeClaimed;
```

Replace the `IsSleeping` property (`:33`) with:

```csharp
  public bool IsSleeping => _isSleeping;

  public bool IsSleepScreenVisible => _isSleepScreenVisible;

  /// <summary>
  /// The three states, derived rather than stored, so there is no second state machine to keep in
  /// step with <see cref="IsSleeping"/>.
  /// </summary>
  public ConsoleWakeState WakeState
  {
    get
    {
      // A claimed wake reads as Awake from this instant. Both of the things that would otherwise
      // clear it - the resume inside WakeAsync, and the browser navigating off /sleep - are far
      // slower than the 10 ms encoder poll, so without this the second detent of a fast spin is
      // discarded along with the tenth.
      if (Volatile.Read(ref _wakeClaimed) == 1)
      {
        return ConsoleWakeState.Awake;
      }

      // Standby is checked first because it is defined by audio being parked, which is true before
      // any client has reported the route.
      if (_isSleeping)
      {
        return ConsoleWakeState.Standby;
      }

      return _isSleepScreenVisible ? ConsoleWakeState.Ambient : ConsoleWakeState.Awake;
    }
  }

  public void SetSleepScreenVisible(bool visible)
  {
    _isSleepScreenVisible = visible;
    Interlocked.Exchange(ref _wakeClaimed, 0);
    _logger.LogDebug("Sleep screen reported {Visible}", visible ? "visible" : "hidden");
  }

  public bool TryClaimWake()
  {
    // Read before claiming so an already-awake console never burns the claim that the next genuine
    // sleep would need.
    if (WakeState == ConsoleWakeState.Awake)
    {
      return false;
    }

    return Interlocked.CompareExchange(ref _wakeClaimed, 1, 0) == 0;
  }
```

In `EnterSleepAsync`, immediately after `_isSleeping = true;` (currently `:84`), add:

```csharp
      // A claim that was never confirmed would otherwise keep WakeState reading Awake through this
      // standby, and every knob would act on a console the owner just parked.
      Interlocked.Exchange(ref _wakeClaimed, 0);
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~SleepServiceTests"
```

Expected: **PASS**, including the four facts that shipped before this task.

- [ ] **Step 6: Build the whole solution**

```bash
dotnet build --configuration Release
```

Expected: **FAIL** — `FakeSleepService` in `RotaryEncoderRouterMappingTests.cs` no longer implements
`ISleepService`. That is correct and Task 5 fixes it. **Do not stub it here**; leaving the break
visible is what stops the router work being forgotten. Commit anyway — the next task closes it.

- [ ] **Step 7: Commit**

```bash
git add src/Radio.Core/Interfaces/ISleepService.cs src/Radio.API/Services/SleepService.cs tests/Radio.API.Tests/Services/SleepServiceTests.cs
git commit -m "$(cat <<'EOF'
ENC-6: derive the three sleep states, and latch the wake to one input

Awake / Ambient / Standby, composed from IsSleeping and a new
IsSleepScreenVisible rather than stored as a second state machine.
TryClaimWake makes WakeState read Awake from the instant a wake is
claimed, so a fast spin loses one detent instead of every detent that
arrives before the browser leaves /sleep.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

### Task 2: `WakeAsync` can wake from Ambient, where audio was never parked

**Files:**
- Modify: `src/Radio.API/Services/SleepService.cs:105-153`
- Test: `tests/Radio.API.Tests/Services/SleepServiceTests.cs`

**Interfaces:**
- Consumes: `IsSleepScreenVisible`, `SetSleepScreenVisible` (Task 1).
- Produces: no new signatures. `WakeAsync` gains one behaviour: it broadcasts
  `SleepStateChanged(false)` when the sleep screen is up, even though `IsSleeping` was already false.

**Why this task exists, because it is not obvious:** `WakeAsync` early-returns on `!_isSleeping`
(`:110-113`). In **Ambient** that is exactly the case — so a knob that wakes from Ambient would start
a `WakeAsync` that does nothing, broadcast nothing, and leave the kiosk parked on `/sleep` forever.
`SleepStateChanged(false)` is the *only* signal `Sleep.razor` listens for to navigate home
(`Sleep.razor:403-410`), so it has to be sent on the Ambient path too.

- [ ] **Step 1: Write the failing tests**

```csharp
  [Fact]
  public async Task WakeAsync_FromAmbient_BroadcastsTheWakeEvenThoughAudioWasNeverParked()
  {
    // The Ambient wake is a NAVIGATION, not an audio change. SleepStateChanged(false) is the only
    // thing Sleep.razor listens for to leave /sleep, so skipping it here would strand the kiosk on
    // a clock with the knobs already acting.
    var clientProxy = new Mock<IClientProxy>();
    var service = CreateService(clientProxy);
    service.SetSleepScreenVisible(true);

    await service.WakeAsync("encoder-turn");

    clientProxy.Verify(
      p => p.SendCoreAsync(
        "SleepStateChanged",
        It.Is<object?[]>(a => a.Length == 1 && a[0] is bool b && !b),
        It.IsAny<CancellationToken>()),
      Times.Once);
    Assert.False(service.IsSleeping);
  }

  [Fact]
  public async Task WakeAsync_FromAmbient_DoesNotTouchAudio()
  {
    // Ambient's defining property is that audio never stopped. A wake from it must not "restore" a
    // mute state that was never saved.
    var audio = new Mock<IAudioManager>();
    var service = CreateService(audioManager: audio.Object);
    service.SetSleepScreenVisible(true);

    await service.WakeAsync("encoder-turn");

    audio.VerifySet(m => m.IsMuted = It.IsAny<bool>(), Times.Never);
  }

  [Fact]
  public async Task WakeAsync_WithNothingToWakeFrom_StillDoesNotRebroadcast()
  {
    // The shipped guard, restated against the new condition: awake plus no sleep screen is nothing
    // to wake from, and a broadcast there would navigate every other tab home for no reason.
    var clientProxy = new Mock<IClientProxy>();
    var service = CreateService(clientProxy);

    await service.WakeAsync("api");

    clientProxy.Verify(
      p => p.SendCoreAsync(
        It.IsAny<string>(),
        It.IsAny<object?[]>(),
        It.IsAny<CancellationToken>()),
      Times.Never);
  }
```

`CreateService` must gain optional `Mock<IClientProxy>` and `IAudioManager` parameters if it does not
already take them; keep the shipped four facts compiling against it.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~SleepServiceTests"
```

Expected: `WakeAsync_FromAmbient_BroadcastsTheWakeEvenThoughAudioWasNeverParked` FAILS —
`Times.Once` sees zero calls, because `WakeAsync` returned at `:110`.

- [ ] **Step 3: Rewrite the guard and the restore block**

In `WakeAsync`, replace the early-return guard (`:110-113`) with:

```csharp
      // Two ways to be somewhere other than Awake, and only one of them parked audio. Standby has
      // playback to restore; Ambient has nothing but a browser to send home. Both need the
      // broadcast, so both fall through.
      bool wasSleeping = _isSleeping;
      if (!wasSleeping && !_isSleepScreenVisible)
      {
        Interlocked.Exchange(ref _wakeClaimed, 0);
        return;
      }
```

Then wrap the audio restore so it runs only on the Standby path. Replace
`if (_audioManager != null)` (`:122`) with:

```csharp
      if (wasSleeping && _audioManager != null)
```

and leave the block's body exactly as it is. `_isSleeping = false;` (`:120`) stays where it is — it
is already a no-op on the Ambient path.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~SleepServiceTests"
```

Expected: **PASS**, including `WakeAsync_NotSleeping_DoesNotRebroadcast`, which still holds because
its fixture never reports the sleep screen.

- [ ] **Step 5: Commit**

```bash
git add src/Radio.API/Services/SleepService.cs tests/Radio.API.Tests/Services/SleepServiceTests.cs
git commit -m "$(cat <<'EOF'
ENC-6: WakeAsync can wake from Ambient, where audio was never parked

The early return on !_isSleeping is exactly the Ambient case, so a knob
waking from the idle clock started a wake that broadcast nothing and left
the kiosk on /sleep. The broadcast now fires whenever there was something
to wake from; the audio restore still runs only where audio was parked.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

### Task 3: The endpoint the sleep screen reports itself through

**Files:**
- Modify: `src/Radio.API/Models/SystemModels.cs:126-135`
- Modify: `src/Radio.API/Controllers/SystemController.cs:497-529`
- Test: `tests/Radio.API.Tests/Controllers/SystemControllerTests.cs`

**Interfaces:**
- Consumes: `ISleepService.SetSleepScreenVisible`, `.WakeState`, `.IsSleeping` (Task 1).
- Produces: `POST /api/system/sleep-screen` taking `SetSleepScreenVisibleRequest { bool Visible }` and
  returning `SleepStateResponse { bool IsSleeping; string WakeState }`;
  `GET /api/system/sleep` returning the same `SleepStateResponse`.

**One shape, deliberately:** `GET /api/system/sleep` currently returns an anonymous
`new { isSleeping }` and **has no caller anywhere in the tree**, so repointing it at the DTO is free
and leaves one response shape rather than two. `POST /api/system/sleep` is left alone — its only
caller reads `IsSuccessStatusCode` and nothing else.

**`WakeState` crosses as a string, not an enum.** That is the `ENC-8` lesson applied on purpose
(*"enums cross as strings"*), and it matches the shipped precedent in
`AudioStateUpdateService.OnEncoderHudChanged`, which sends `Phase = e.Phase.ToString()`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Radio.API.Tests/Controllers/SystemControllerTests.cs`. Read the file's existing
controller-construction helper first and reuse it; the sleep service parameter is the **concrete**
`Services.SleepService`, not `ISleepService` (`SystemController.cs:22`).

```csharp
  [Fact]
  public void GetSleepState_ReportsBothTheAudioTruthAndTheWakeState()
  {
    var sleep = CreateSleepService();
    var controller = CreateController(sleepService: sleep);

    var result = Assert.IsType<OkObjectResult>(controller.GetSleepState());
    var body = Assert.IsType<SleepStateResponse>(result.Value);

    Assert.False(body.IsSleeping);
    Assert.Equal("Awake", body.WakeState);
  }

  [Fact]
  public void SetSleepScreenVisible_True_PutsTheConsoleInAmbientAndSaysSo()
  {
    // This is the call the /sleep page makes on first render, and the response is how the page
    // learns which hint to draw without a second round trip.
    var sleep = CreateSleepService();
    var controller = CreateController(sleepService: sleep);

    var result = Assert.IsType<OkObjectResult>(
      controller.SetSleepScreenVisible(new SetSleepScreenVisibleRequest { Visible = true }));
    var body = Assert.IsType<SleepStateResponse>(result.Value);

    Assert.True(sleep.IsSleepScreenVisible);
    Assert.Equal("Ambient", body.WakeState);
    Assert.False(body.IsSleeping);
  }

  [Fact]
  public async Task SetSleepScreenVisible_True_WhileSleeping_ReportsStandby()
  {
    var sleep = CreateSleepService();
    await sleep.EnterSleepAsync();
    var controller = CreateController(sleepService: sleep);

    var result = Assert.IsType<OkObjectResult>(
      controller.SetSleepScreenVisible(new SetSleepScreenVisibleRequest { Visible = true }));
    var body = Assert.IsType<SleepStateResponse>(result.Value);

    Assert.Equal("Standby", body.WakeState);
    Assert.True(body.IsSleeping);
  }

  [Fact]
  public void SetSleepScreenVisible_WithNoSleepService_ReturnsNotImplemented()
  {
    // Matches the shipped POST /api/system/sleep posture rather than inventing a second one.
    var controller = CreateController(sleepService: null);

    var result = Assert.IsType<ObjectResult>(
      controller.SetSleepScreenVisible(new SetSleepScreenVisibleRequest { Visible = true }));

    Assert.Equal(501, result.StatusCode);
  }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~SystemControllerTests"
```

Expected: **compile failure** — `SetSleepScreenVisible`, `SetSleepScreenVisibleRequest` and
`SleepStateResponse` do not exist.

- [ ] **Step 3: Add the DTOs**

Append to `src/Radio.API/Models/SystemModels.cs`, after `SetSleepRequest`:

```csharp
/// <summary>
/// Request to report whether the <c>/sleep</c> route is on screen.
/// </summary>
public class SetSleepScreenVisibleRequest
{
  /// <summary>
  /// Gets or sets whether the sleep screen is currently rendered on this client.
  /// </summary>
  public bool Visible { get; set; }
}

/// <summary>
/// The console's sleep state, in both the forms a caller needs.
/// </summary>
/// <remarks>
/// A named DTO rather than an anonymous object because the Web deserializes this one. <c>ENC-8</c>
/// shipped a page that could not read its own API response and every test stayed green, because the
/// bUnit rig fails each HTTP call by design and a null result is what it expects either way.
/// </remarks>
public class SleepStateResponse
{
  /// <summary>True when audio is paused and muted.</summary>
  public bool IsSleeping { get; init; }

  /// <summary>
  /// <c>Awake</c>, <c>Ambient</c> or <c>Standby</c>. Crosses the wire as a <b>string</b> so a value
  /// a client does not recognise degrades to that client's default rather than failing to
  /// deserialize — the same open-string rule handoff §6.10 sets for the HUD phase.
  /// </summary>
  public string WakeState { get; init; } = nameof(ConsoleWakeState.Awake);
}
```

Add `using Radio.Core.Interfaces;` to the top of the file if it is not already there.

- [ ] **Step 4: Add the endpoint and repoint the GET**

In `src/Radio.API/Controllers/SystemController.cs`, replace `GetSleepState` (`:497-505`) with:

```csharp
  /// <summary>
  /// Gets the current sleep/standby state, as both the audio truth and the three-state model.
  /// </summary>
  [HttpGet("sleep")]
  [ProducesResponseType(typeof(SleepStateResponse), StatusCodes.Status200OK)]
  public IActionResult GetSleepState()
  {
    return Ok(BuildSleepState());
  }
```

and insert, immediately after `SetSleepState` (after `:529`):

```csharp
  /// <summary>
  /// Reports whether the <c>/sleep</c> route is on screen, and answers with the resulting state.
  /// </summary>
  /// <remarks>
  /// Called by the sleep page itself on first render and on dispose, and by <c>MainLayout</c> on
  /// first render to report the opposite. That is what makes all three ways of reaching
  /// <c>/sleep</c> — the idle timer, the Sleep pill, and a direct navigation — produce the same
  /// server-side fact, so a knob turned on the idle clock is no longer in a different state from a
  /// knob turned on the pill clock.
  ///
  /// <para>
  /// It changes no audio. Ambient is defined by playback continuing, so a call reporting the screen
  /// visible must never pause anything.
  /// </para>
  /// </remarks>
  [HttpPost("sleep-screen")]
  [ProducesResponseType(typeof(SleepStateResponse), StatusCodes.Status200OK)]
  public IActionResult SetSleepScreenVisible([FromBody] SetSleepScreenVisibleRequest request)
  {
    if (_sleepService == null)
    {
      return StatusCode(501, new { error = "Sleep service not available" });
    }

    _sleepService.SetSleepScreenVisible(request.Visible);
    return Ok(BuildSleepState());
  }

  private SleepStateResponse BuildSleepState()
  {
    if (_sleepService == null)
    {
      return new SleepStateResponse();
    }

    return new SleepStateResponse
    {
      IsSleeping = _sleepService.IsSleeping,
      WakeState = _sleepService.WakeState.ToString(),
    };
  }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~SystemControllerTests"
```

Expected: **PASS** (4 new facts).

- [ ] **Step 6: Commit**

```bash
git add src/Radio.API/Models/SystemModels.cs src/Radio.API/Controllers/SystemController.cs tests/Radio.API.Tests/Controllers/SystemControllerTests.cs
git commit -m "$(cat <<'EOF'
ENC-6: an endpoint the sleep screen reports itself through

POST /api/system/sleep-screen records whether /sleep is on screen and
answers with the resulting three-state value, so the page learns which
hint to draw in the same round trip. GET /api/system/sleep is repointed at
the same named DTO - it had no caller, so one shape costs nothing.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

### Task 4: Correct three comments that assert more than the code does

**Files:**
- Modify: `src/Radio.API/Services/SleepService.cs:9-15`, `:89-92`, `:117-118`, `:155-159`
- Modify: `design/FUTURE-WORK.md` §7 (Sleep Mode)

**Interfaces:**
- Consumes: nothing. Produces: nothing. **Documentation only — no behaviour changes.**

**Why this is a task and not a footnote.** `CLAUDE.md`'s pre-merge rule exists because this repo has
shipped three comment/code mismatches, two of which caused real bugs. `SleepService` currently carries
a fourth: its class doc says the service *"turns off display via DPMS"* and *"On wake: restores
display"*, and neither is true — both calls are commented out. The inline NOTE then says the calls
*"Will be re-enabled when rotary encoders provide a hardware wake source"*, which `ENC-15` established
will **never** happen on this hardware. A wrong comment survives the code it described, and the next
engineer debugs the description.

- [ ] **Step 1: Correct the class doc**

Replace `src/Radio.API/Services/SleepService.cs:9-15` with:

```csharp
/// <summary>
/// Manages the console's sleep states for the kiosk UI.
///
/// <para>
/// <b>Standby</b> pauses the active source, saves and applies mute, and broadcasts
/// <c>SleepStateChanged</c> over SignalR. <b>Ambient</b> changes no audio at all — it is the
/// <c>/sleep</c> route being on screen while playback continues, reported by the page itself. Waking
/// restores the pre-sleep mute state and resumes playback <i>only</i> where playback was parked.
/// </para>
///
/// <para>
/// ⚠ <b>This service does not touch display power, and must not.</b> <see cref="SetDisplayPowerAsync"/>
/// is retained but uncalled: <c>ENC-15</c> established on the box that the touchscreen is powered by
/// the panel and leaves the USB bus when it blanks, so touch cannot wake a blanked panel, and the
/// encoder exposes no evdev node so it cannot wake one either. See <c>design/INTEGRATIONS.md</c> §1
/// for the recovery commands and <c>design/FUTURE-WORK.md</c> §7 (Sleep Mode) for the full record.
/// </para>
///
/// Wake sources: a screen tap, an encoder input, or an API call.
/// </summary>
```

- [ ] **Step 2: Correct the two inline NOTEs**

Replace `:89-92` (the NOTE and the commented call inside `EnterSleepAsync`) with:

```csharp
      // Hardware DPMS stays off. ENC-15 (2026-09-02) tested the precondition on this box and it
      // failed: the touchscreen leaves the USB bus when the panel powers down, so no touch event can
      // be generated while dark, and the encoder has no evdev node so it cannot wake the compositor
      // either. That leaves one application-mediated wake path where two were required.
      // await SetDisplayPowerAsync(false);
```

Replace `:117-118` (inside `WakeAsync`) with:

```csharp
      // Hardware DPMS wake stays off - see the note in EnterSleepAsync.
      // await SetDisplayPowerAsync(true);
```

- [ ] **Step 3: Correct the `SetDisplayPowerAsync` doc**

Replace `:155-159` with:

```csharp
  /// <summary>
  /// Controls the physical display via GNOME ScreenSaver D-Bus.
  ///
  /// <para>
  /// ⚠ <b>Nothing calls this, deliberately</b> (see the class remarks). It is retained as the
  /// recorded shape of the thing <c>ENC-15</c> ruled out, so the FUTURE-WORK entry explaining why
  /// blanking does not ship points at real code. Two further reasons not to revive it as written:
  /// the ScreenSaver route <b>does not reach DPMS-off</b> — <c>ENC-15</c> found the panel dark with
  /// <c>dpms=Off</c> while the screensaver reported inactive — and it needs the desktop session bus,
  /// which it reaches by shelling out as another user.
  /// </para>
  /// </summary>
```

- [ ] **Step 4: Repair the FUTURE-WORK citations**

In `design/FUTURE-WORK.md` § *7. Sleep Mode — Rotary Encoder Wake/Sleep Button*, the two line
references to the commented-out display-power calls read `SleepService.cs:84-87` and `:114-115`.
Both are stale — the code moved when `ENC-4` landed. Replace them with **`SleepService.cs:89-92` and
`:117-118`**, and append this paragraph to that section:

```markdown
⚠ **There are two sections numbered 7 in this file** — *Google Cast — WebSocket + Web Audio API* and
this one. `SleepService`'s class remarks point at **this** one. Renumbering is deliberately not done
here: it would churn every cross-reference in the file for a cosmetic gain, and the ambiguity is now
recorded rather than latent.

**`ENC-6` (2026-09-02) shipped the sleep/wake half and left the blanking half withdrawn.** Sleep is
now a three-state model — `Awake` / `Ambient` / `Standby`, in `Radio.Core.Interfaces.ConsoleWakeState`
— derived from `IsSleeping` plus a new `IsSleepScreenVisible` that the `/sleep` page reports about
itself. Nothing in that work turns the panel off, and nothing should: the two dark states in
Designer Rev 5 §8.2 are withdrawn with the blanking half.
```

- [ ] **Step 5: Verify nothing else moved**

```bash
dotnet build --configuration Release
grep -n "SetDisplayPowerAsync" src/Radio.API/Services/SleepService.cs
```

Expected: the build's only failure is still `FakeSleepService` (Task 1 Step 6), and the grep returns
exactly **three** lines — the two commented calls and the method declaration.

- [ ] **Step 6: Commit**

```bash
git add src/Radio.API/Services/SleepService.cs design/FUTURE-WORK.md
git commit -m "$(cat <<'EOF'
ENC-6: SleepService's own comments said it blanked the panel; it does not

The class doc claimed DPMS control and display restore, and the inline NOTE
promised a re-enable when the encoders became a hardware wake source.
ENC-15 established that they never can on this hardware. Corrected in
place, and the stale FUTURE-WORK line citations repaired with them.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

## Phase 2 — the router

### Task 5: The sleep gate — three outcomes, decided before any handler runs

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs:127-173`, `:279-292`
- Test: `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs:67-84` and new facts

**Interfaces:**
- Consumes: `ISleepService.WakeState`, `.TryClaimWake()`, `ConsoleWakeState` (Task 1);
  `RotaryEncoderConfigDefaults.VolumeEncoderIndex` (shipped, `= 0`).
- Produces: `RotaryEncoderActionRouter.SleepGateOutcome` (private enum: `Dispatch`, `ConsumeAndWake`,
  `Consume`) and `private SleepGateOutcome GateInput(int index, bool isTurn)`.
  **`TryWakeFromSleep` is removed** — Task 6 supplies `PublishCurrentValue(int index)`, which the two
  consume branches call. **Until Task 6 lands, the two consume branches wake and dispatch no handler
  but publish nothing**; that is a deliberate two-task split, and Task 6's tests are what close it.

**The policy, in full.** Every cell comes from handoff §8.3's two surviving columns.

| `WakeState` | Input | Outcome | Handoff |
|---|---|---|---|
| `Awake` | anything | `Dispatch` | — |
| `Ambient` | encoder **0** turn | `Dispatch` | *"VOLUME turn — acts in place, dim readout, stays on the clock"* |
| `Ambient` | encoder **0** press edge | `Dispatch` | *"VOLUME press — acts in place"*; *"VOLUME hold 600 ms → Standby"* |
| `Ambient` | encoder 1–3 turn | `ConsumeAndWake` | *"SOURCE / PRESETS / TUNING turn — wakes → Awake. Consumed."* |
| `Ambient` | encoder 1–3 press edge | `ConsumeAndWake` | *"SOURCE / PRESETS / TUNING press — wakes → Awake. Consumed."* |
| `Standby` | **any** turn | `Consume` | **D22** — *"a turn lights the panel and does not resume audio"* |
| `Standby` | **any** press edge | `ConsumeAndWake` | *"press — resumes → Awake"*; *"VOLUME hold 600 ms — wakes → Awake"* |

**Why `Standby` + a VOLUME hold needs no special case:** the press edge is consumed for the wake, so
`EncoderLongPressGesture` never sees a press-down, never arms a timer, and the release is dropped by
its shipped orphan-release guard (`EncoderLongPressGesture.cs:104-111`). *"Wakes → Awake"* is what
happens, on the press edge, which is what §8.3 asks for.

- [ ] **Step 1: Extend the test fake**

Replace `FakeSleepService` (`RotaryEncoderRouterMappingTests.cs:67-84`) with:

```csharp
  private sealed class FakeSleepService : ISleepService
  {
    private int _wakeClaimed;

    public bool IsSleeping { get; set; }
    public bool IsSleepScreenVisible { get; set; }
    public int EnterSleepCalls { get; private set; }
    public int WakeCalls { get; private set; }
    public int ClaimAttempts { get; private set; }

    /// <summary>
    /// Mirrors the shipped derivation in <c>SleepService</c>, claim latch included, so a router test
    /// exercises the same three-way decision the box does rather than a simplified one.
    /// </summary>
    public ConsoleWakeState WakeState
    {
      get
      {
        if (Volatile.Read(ref _wakeClaimed) == 1) return ConsoleWakeState.Awake;
        if (IsSleeping) return ConsoleWakeState.Standby;
        return IsSleepScreenVisible ? ConsoleWakeState.Ambient : ConsoleWakeState.Awake;
      }
    }

    public Task EnterSleepAsync()
    {
      EnterSleepCalls++;
      Interlocked.Exchange(ref _wakeClaimed, 0);
      return Task.CompletedTask;
    }

    public Task WakeAsync(string wakeSource = "unknown")
    {
      WakeCalls++;
      return Task.CompletedTask;
    }

    public void SetSleepScreenVisible(bool visible)
    {
      IsSleepScreenVisible = visible;
      Interlocked.Exchange(ref _wakeClaimed, 0);
    }

    public bool TryClaimWake()
    {
      ClaimAttempts++;
      if (WakeState == ConsoleWakeState.Awake) return false;
      return Interlocked.CompareExchange(ref _wakeClaimed, 1, 0) == 0;
    }
  }
```

- [ ] **Step 2: Write the failing gate tests**

Append to `RotaryEncoderRouterMappingTests`:

```csharp
  // --- The sleep gate (ENC-6, handoff 8.3) --------------------------------------------------

  [Fact]
  public void Ambient_VolumeTurn_ActsInPlace()
  {
    // Rule 2. The lit clock is the one state where a knob still changes the machine, and it is the
    // knob whose readout the sleep screen was already built to host.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(0, h.Sleep.WakeCalls);
    Assert.True(h.Audio.MasterVolume > 0.5f);
  }

  [Fact]
  public void Ambient_TuningTurn_IsConsumedAndWakes()
  {
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);
    h.Audio.ActiveSource = null;

    h.Encoders.RaiseTurn(1, 1);

    Assert.Equal(1, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Ambient_ASecondTurnDuringTheWake_Acts()
  {
    // The latch, from the router's side. The browser has not left /sleep yet, so IsSleepScreenVisible
    // is still true - but the claim is spent, so the second detent must reach its handler.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(1, 1);

    Assert.Equal(1, h.Sleep.WakeCalls);
    Assert.Equal(ConsoleWakeState.Awake, h.Sleep.WakeState);
  }

  [Fact]
  public void Standby_ATurn_DoesNotResumeAudio()
  {
    // D22, verbatim: "a turn is what a passing sleeve does; a press is what a person does."
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(1, 1);
    h.Encoders.RaiseTurn(2, 1);
    h.Encoders.RaiseTurn(3, 1);

    Assert.Equal(0, h.Sleep.WakeCalls);
    Assert.Equal(0.5f, h.Audio.MasterVolume);
    Assert.Equal(0, h.Audio.MuteWrites);
  }

  [Fact]
  public void Standby_APress_Resumes()
  {
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseButton(2, isPressed: true);

    Assert.Equal(1, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Standby_APressAfterATurn_StillResumes()
  {
    // The turn must not have burned the claim - otherwise a sleeve brushing a knob would leave the
    // console unable to be turned on by the press that follows it.
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(3, 1);
    h.Encoders.RaiseButton(0, isPressed: true);

    Assert.Equal(1, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Ambient_VolumeLongPress_StillEntersStandby()
  {
    // 8.3's Ambient column keeps encoder 0 fully live, hold included. This is the one path from the
    // clock into Standby that does not involve the topbar.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseButton(0, isPressed: true);
    h.Time.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs));

    Assert.Equal(1, h.Sleep.EnterSleepCalls);
    Assert.Equal(0, h.Sleep.WakeCalls);
  }

  [Fact]
  public void Awake_NothingIsConsumed()
  {
    using var h = new Harness();

    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(0, h.Sleep.WakeCalls);
    Assert.Equal(0, h.Sleep.ClaimAttempts);
  }
```

Add `using Radio.Core.Configuration;` to the file if `EncoderInteractionTimings` is not already
reachable.

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test tests/Radio.Infrastructure.Tests --configuration Release --filter "FullyQualifiedName~RotaryEncoderRouterMappingTests"
```

Expected: `Ambient_*` and `Standby_ATurn_DoesNotResumeAudio` FAIL — today the router only looks at
`IsSleeping`, so Ambient consumes nothing and Standby consumes everything.

- [ ] **Step 4: Replace `TryWakeFromSleep` with the gate**

In `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs`, delete
`TryWakeFromSleep` (`:279-292`) and put in its place:

```csharp
  /// <summary>
  /// What the sleep model does with one encoder input, decided before any handler runs.
  /// </summary>
  private enum SleepGateOutcome
  {
    /// <summary>Run the handler. The console is awake, or this is VOLUME on the lit Ambient clock.</summary>
    Dispatch,

    /// <summary>Spend this input waking: publish this knob's current value, start the wake, run no handler.</summary>
    ConsumeAndWake,

    /// <summary>Spend this input: publish this knob's current value, run no handler, and do not wake.</summary>
    Consume,
  }

  /// <summary>
  /// Applies handoff §8.3's two surviving columns to one input.
  ///
  /// <para>
  /// Rule 2 on a lit panel: VOLUME acts in place and everything else is spent waking. Standby adds
  /// D22 on top of it — a <b>turn</b> never resumes audio, only a press or a screen tap does — so a
  /// turn there is consumed without a wake. <b>The two dark states are withdrawn by
  /// <c>ENC-15</c></b>, so Rule 1 has no reachable state and appears nowhere below.
  /// </para>
  ///
  /// <para>
  /// ⚠ <paramref name="index"/> is compared against
  /// <see cref="RotaryEncoderConfigDefaults.VolumeEncoderIndex"/> and nothing else, which is why
  /// this survives the ENC-5 / ENC-7 remap: index 0 is VOLUME under both the current handler table
  /// and the remapped one, and every other index reaches the same branch.
  /// </para>
  /// </summary>
  private SleepGateOutcome GateInput(int index, bool isTurn)
  {
    if (_sleepService is null)
    {
      return SleepGateOutcome.Dispatch;
    }

    switch (_sleepService.WakeState)
    {
      case ConsoleWakeState.Ambient when index == RotaryEncoderConfigDefaults.VolumeEncoderIndex:
        // The handler runs and publishes as usual; the card lands on Sleep.razor's own HUD host,
        // which is why this needs no code of its own.
        return SleepGateOutcome.Dispatch;

      case ConsoleWakeState.Standby when isTurn:
        return SleepGateOutcome.Consume;

      case ConsoleWakeState.Ambient:
      case ConsoleWakeState.Standby:
        // A lost claim means an earlier input in this same burst already started the wake, so
        // WakeState now reads Awake and dispatching is the correct answer rather than a fallback:
        // a fast spin must lose one detent, not twelve.
        return _sleepService.TryClaimWake()
          ? SleepGateOutcome.ConsumeAndWake
          : SleepGateOutcome.Dispatch;

      default:
        return SleepGateOutcome.Dispatch;
    }
  }
```

- [ ] **Step 5: Rewire the two entry points**

Replace the guard block inside `OnEncoderTurned` (`:131-135`) with:

```csharp
      SleepGateOutcome gate = GateInput(e.EncoderIndex, isTurn: true);
      if (gate != SleepGateOutcome.Dispatch)
      {
        PublishCurrentValue(e.EncoderIndex);
        if (gate == SleepGateOutcome.ConsumeAndWake && _sleepService is not null)
        {
          _ = _sleepService.WakeAsync("encoder-turn");
          _logger.LogInformation("Woke via encoder-turn on encoder {Index}", e.EncoderIndex);
        }
        return;
      }
```

Replace the guard block inside `OnButtonPressed` (`:155-165`) with:

```csharp
      // Both edges matter. The short action fires on release and the long action fires at the
      // threshold while still held, so this handler routes the edge and leaves the choice of action
      // to the gesture.
      //
      // The sleep gate is applied to the PRESS edge only: waking is what the input is spent on, and
      // letting the release through would fire a short action into a UI that has just changed
      // underneath the user. The release that follows a consumed press reaches the gesture and is
      // dropped by its orphan-release guard, which exists for exactly this path.
      if (e.IsPressed)
      {
        SleepGateOutcome gate = GateInput(e.EncoderIndex, isTurn: false);
        if (gate != SleepGateOutcome.Dispatch)
        {
          PublishCurrentValue(e.EncoderIndex);
          if (gate == SleepGateOutcome.ConsumeAndWake && _sleepService is not null)
          {
            _ = _sleepService.WakeAsync("encoder-button");
            _logger.LogInformation("Woke via encoder-button on encoder {Index}", e.EncoderIndex);
          }
          return;
        }
      }
```

- [ ] **Step 6: Add a temporary no-op `PublishCurrentValue` so this task compiles alone**

Add beside `PublishHud`:

```csharp
  // Task 6 replaces this body with the real readout. It is a no-op for exactly one commit so the
  // gate can be reviewed on its own; the tests that force it to publish are in Task 6.
  private void PublishCurrentValue(int index)
  {
  }
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/Radio.Infrastructure.Tests --configuration Release --filter "FullyQualifiedName~RotaryEncoderRouterMappingTests"
```

Expected: all new facts **PASS**. `WakeConsumesThePressEdge_AndTheReleaseDoesNotFireTheShortAction`
also still passes — its fixture sets `IsSleeping = true`, which derives `Standby`, where a press is
`ConsumeAndWake`, and `PublishCurrentValue` is still the no-op so `Assert.Empty(h.Hud.Published)`
holds. **Task 6 is what inverts it.**

- [ ] **Step 8: Full build and suite**

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Expected: **0 warnings**, full suite green.

- [ ] **Step 9: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs
git commit -m "$(cat <<'EOF'
ENC-6: gate every encoder input on the three states, not on one boolean

TryWakeFromSleep asked only whether audio was parked, so on the clock the
idle timer actually reaches, every knob acted normally and invisibly. The
gate now applies handoff 8.3's two surviving columns: on the lit clock
VOLUME acts in place and everything else is spent waking, and in Standby a
turn never resumes audio while a press does (D22).

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

### Task 6: A consumed input still says where you are

**Files:**
- Modify: `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs:68-113`, and the
  `PublishCurrentValue` stub from Task 5
- Test: `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs`

**Interfaces:**
- Consumes: `SleepGateOutcome`, `GateInput` (Task 5); the shipped `PublishHud(int, string, Action<HudBuilder>)`.
- Produces: `private readonly Action<int>[] _currentValuePublishers` and four methods
  `PublishCurrentVolume(int index)`, `PublishCurrentTuning(int index)`, `PublishCurrentSource(int index)`,
  `PublishCurrentViz(int index)`. `PublishCurrentValue(int index)` gains its real body.

**Why this is the difference between a loss and an answer.** Handoff §8.3: *"the first detent tells
you where you are; the second one moves it."* Without it, a knob spent on a wake is a knob that did
nothing visible — which handoff §3 principle 1 forbids outright (*"No knob may be inert in any
reachable state"*) and principle 2 forbids by name (*"including inputs that are consumed rather than
applied"*). It is also the difference between the D22 turn being a rule and being a dead knob.

- [ ] **Step 1: Write the failing tests, including the one that inverts**

Replace the assertion block of the shipped fact
`WakeConsumesThePressEdge_AndTheReleaseDoesNotFireTheShortAction` (`:415-424`) — **keep the fact, its
name and its arrangement, change only what it asserts:**

```csharp
    Assert.Equal(1, h.Sleep.WakeCalls);
    Assert.False(h.Audio.IsMuted);
    Assert.Equal(0, h.Audio.MuteWrites);
    // ENC-6 inverts the last assertion of this fact deliberately. It used to be
    // Assert.Empty(h.Hud.Published) - written when a consumed input produced nothing at all. A
    // consumed input now answers "where am I" without changing anything (handoff 8.3), so the card
    // is the deliverable and MuteWrites above is what carries the "no action fired" half.
    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(0, card.EncoderIndex);
    Assert.Equal("VOLUME", card.Label);
    Assert.Equal(EncoderHudPhase.Value, card.Phase);
```

Then append:

```csharp
  [Fact]
  public void Standby_AConsumedTurn_PublishesThatKnobsCurrentValueWithoutChangingIt()
  {
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);
    h.Audio.MasterVolume = 0.62f;

    h.Encoders.RaiseTurn(0, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(0, card.EncoderIndex);
    Assert.Equal("VOLUME", card.Label);
    Assert.Equal(62, card.VolumePercent);
    Assert.Equal(0.62f, h.Audio.MasterVolume);
  }

  [Fact]
  public void Ambient_AConsumedTurn_PublishesOnItsOwnBand()
  {
    // The card has to appear beside the knob that was turned, not beside the knob that woke the
    // console - the geometry keys off the index the event arrived on.
    using var h = new Harness();
    h.Sleep.SetSleepScreenVisible(true);
    h.Audio.ActiveSource = null;

    h.Encoders.RaiseTurn(3, 1);

    var card = Assert.Single(h.Hud.Published);
    Assert.Equal(3, card.EncoderIndex);
  }

  [Fact]
  public void ConsumedTurnsInStandby_KeepAnswering_TheyDoNotFallSilentAfterTheFirst()
  {
    // D22 makes a turn in Standby permanently consumed, so "spend one and stop rendering" would
    // leave three knobs looking broken for the whole standby.
    using var h = new Harness();
    h.Sleep.IsSleeping = true;
    h.Sleep.SetSleepScreenVisible(true);

    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(0, 1);
    h.Encoders.RaiseTurn(0, 1);

    Assert.Equal(3, h.Hud.Published.Count);
    Assert.Equal(0, h.Sleep.WakeCalls);
  }

  [Fact]
  public void TheFourDispatchArraysAgreeInLength()
  {
    // The ENC-5 / ENC-7 remap reorders _turnHandlers and _pressHandlers. This is the third and
    // fourth array beside them; a remap that reorders three of four is the exact failure this
    // pins - it would put a TUNING readout on the SOURCE band with nothing else disagreeing.
    using var h = new Harness();

    Assert.Equal(FrontPanelGeometry.EncoderCount, h.Router.Mapping.Count);

    for (int i = 0; i < FrontPanelGeometry.EncoderCount; i++)
    {
      h.Hud.Published.Clear();
      h.Sleep.IsSleeping = true;
      h.Sleep.SetSleepScreenVisible(true);

      h.Encoders.RaiseTurn(i, 1);

      var card = Assert.Single(h.Hud.Published);
      Assert.Equal(i, card.EncoderIndex);
    }
  }
```

Add `using Radio.Core.Configuration;` if `FrontPanelGeometry` is not already reachable.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Radio.Infrastructure.Tests --configuration Release --filter "FullyQualifiedName~RotaryEncoderRouterMappingTests"
```

Expected: every new fact FAILS on `Assert.Single` seeing zero cards, and the inverted fact FAILS the
same way. `PublishCurrentValue` is still Task 5's no-op.

- [ ] **Step 3: Add the fourth parallel array**

In the field block, after `private readonly Action[] _pressHandlers;` (`:70`), add:

```csharp
  private readonly Action<int>[] _currentValuePublishers;
```

In the constructor, immediately after the `_pressHandlers` assignment (`:113`), add:

```csharp
    // The third and fourth arrays in this block, and they are reordered together or not at all.
    // This one renders what a knob currently reads when the sleep model spends its input on a wake
    // (handoff 8.3). The index is threaded in rather than baked into each publisher so the ENC-5 /
    // ENC-7 remap moves entries here and leaves no literal behind to chase.
    _currentValuePublishers = [PublishCurrentVolume, PublishCurrentTuning, PublishCurrentSource, PublishCurrentViz];
```

- [ ] **Step 4: Give `PublishCurrentValue` its body and add the four publishers**

Replace the Task 5 stub with:

```csharp
  /// <summary>
  /// Renders what this knob currently reads, without changing it.
  ///
  /// <para>
  /// Handoff §8.3 — <i>the first detent tells you where you are; the second one moves it.</i> A knob
  /// whose input is spent on a wake and which shows nothing is indistinguishable from a broken one,
  /// and the user's response to that silence is to turn it harder (§3 principle 1).
  /// </para>
  /// </summary>
  private void PublishCurrentValue(int index)
  {
    if (index < 0 || index >= _currentValuePublishers.Length)
    {
      return;
    }

    try
    {
      _currentValuePublishers[index](index);
    }
    catch (Exception ex)
    {
      // The input has already been spent and the wake still has to happen, so a cosmetic readout
      // must not take it down.
      _logger.LogError(ex, "Error publishing the current value for encoder {Index}", index);
    }
  }

  private void PublishCurrentVolume(int index)
  {
    var mgr = _audioManagerFactory();
    PublishHud(index, "VOLUME", b =>
    {
      b.VolumePercent = (int)Math.Round(mgr.MasterVolume * 100f);
      b.IsMuted = mgr.IsMuted;
    });
  }

  private void PublishCurrentTuning(int index)
  {
    var mgr = _audioManagerFactory();
    if (mgr.ActiveSource is IRadioControl radio)
    {
      PublishHud(index, "TUNING", b =>
      {
        b.PrimaryText = radio.CurrentFrequency.ToDisplayString();
        b.SecondaryText = radio.CurrentBand.ToString().ToUpperInvariant();
        b.PrimaryIsFrequency = true;
      });
      return;
    }

    PublishHud(index, "TRACK", b =>
    {
      b.PrimaryText = mgr.ActiveSource?.Name;
      b.SecondaryText = "no track control on this source";
    });
  }

  private void PublishCurrentSource(int index)
  {
    var mgr = _audioManagerFactory();

    // The ACTIVE source, not _currentSourceIndex. Handoff 8.3 asks for "what is currently
    // selected", and the cycler's cursor is where an uncommitted preview left it, which can be a
    // source the console is not playing.
    PublishHud(index, "SOURCE", b =>
      b.PrimaryText = mgr.ActiveSource?.Name.ToUpperInvariant() ?? "NONE");
  }

  private void PublishCurrentViz(int index)
  {
    PublishHud(index, "VISUALIZER", b => b.PrimaryText = _vizModeService.CurrentMode.ToUpperInvariant());
  }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Radio.Infrastructure.Tests --configuration Release --filter "FullyQualifiedName~RotaryEncoderRouterMappingTests"
```

Expected: **PASS**, including the inverted fact.

- [ ] **Step 6: Full build and suite**

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Expected: **0 warnings**, full suite green.

- [ ] **Step 7: Commit**

```bash
git add src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs
git commit -m "$(cat <<'EOF'
ENC-6: a consumed input still says where you are

The first detent tells you where you are; the second one moves it. Without
this a knob spent on a wake shows nothing, which is a knob that is inert in
a reachable state - and in Standby, where D22 makes every turn consumed,
three knobs would look broken for the whole standby.

The readout dispatches through a fourth array beside the two the ENC-5 /
ENC-7 remap already reorders, with the index threaded in so the remap
leaves no literal behind.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

## Phase 3 — the Web

### Task 7: The sleep screen reports itself, and `MainLayout` reports the opposite

**Files:**
- Modify: `src/Radio.Web/Models/ApiModels.cs`
- Modify: `src/Radio.Web/Services/ApiClients/SystemApiService.cs:69-81`
- Modify: `src/Radio.Web/Components/Pages/Sleep.razor:119-163`, `:446-462`
- Modify: `src/Radio.Web/Components/Layout/MainLayout.razor:392-397`

**Interfaces:**
- Consumes: `POST /api/system/sleep-screen` and `SleepStateResponse` (Task 3).
- Produces: `Radio.Web.Models.SleepStateDto { bool IsSleeping; string WakeState }`;
  `SystemApiService.SetSleepScreenVisibleAsync(bool visible, CancellationToken)` →
  `Task<SleepStateDto?>`; a `private string _wakeState` field on `Sleep` that Task 8 reads.

- [ ] **Step 1: Add the Web DTO**

Append to `src/Radio.Web/Models/ApiModels.cs`:

```csharp
/// <summary>
/// The console's sleep state as <c>/api/system/sleep-screen</c> and <c>/api/system/sleep</c> report it.
/// </summary>
/// <remarks>
/// <see cref="WakeState"/> is a <b>string</b>, not an enum, for the same reason
/// <see cref="EncoderHudDto.Phase"/> is: a value this build does not recognise must degrade to this
/// build's default rather than fail to deserialize. <c>ENC-8</c> shipped a page that could not read
/// its own API response precisely because an enum crossed as a string.
/// </remarks>
public class SleepStateDto
{
  public bool IsSleeping { get; set; }
  public string WakeState { get; set; } = "Awake";
}
```

- [ ] **Step 2: Add the client method**

Insert into `SystemApiService`, after `SetSleepAsync`:

```csharp
  /// <summary>
  /// Reports whether the <c>/sleep</c> route is on screen, and returns the resulting state.
  /// </summary>
  /// <remarks>
  /// Returns <c>null</c> on any failure, and every caller must render correctly from that: the
  /// bUnit rig fails every outbound request by design, and the kiosk can call this while the API is
  /// still starting. Failing means the caller keeps its default, which is the Ambient copy.
  /// </remarks>
  public async Task<SleepStateDto?> SetSleepScreenVisibleAsync(
    bool visible,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync(
        "/api/system/sleep-screen", new { visible }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        return null;
      }

      return await response.Content.ReadFromJsonAsync<SleepStateDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to report sleep screen visibility {Visible}", visible);
      return null;
    }
  }
```

- [ ] **Step 3: Report from `Sleep.razor`**

Add to the `@code` field block:

```csharp
  // ENC-6. Which of the three states the server says this screen is in. Defaults to the Ambient
  // copy: a failed report must not put the Standby hint on a console whose audio is still playing.
  private string _wakeState = nameof(Radio.Core.Interfaces.ConsoleWakeState.Ambient);

  // Set only once the report has actually been sent, so the prerender pass - which renders and
  // disposes without ever reaching OnAfterRenderAsync - cannot clear a flag it never set.
  private bool _reportedVisible;
```

Add this method to the `@code` block:

```csharp
  /// <summary>
  /// ENC-6. Tells the server this route is on screen.
  ///
  /// <para>
  /// This is what collapses the two <c>/sleep</c> states into one. Reached by the idle timer the
  /// server previously knew nothing at all, so every knob acted normally on a screen showing a
  /// clock; reached by the Sleep pill it knew only that audio was parked. Reporting from the page
  /// means all three routes in — the idle timer, the pill, and a direct navigation — produce the
  /// same fact.
  /// </para>
  ///
  /// <para>
  /// <c>OnAfterRenderAsync</c> rather than <c>OnInitializedAsync</c> because the latter also runs
  /// during prerender, where a matching dispose would immediately retract the report.
  /// </para>
  /// </summary>
  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (!firstRender)
    {
      return;
    }

    _reportedVisible = true;
    var state = await SystemApi.SetSleepScreenVisibleAsync(true);
    if (state is null || _disposed)
    {
      return;
    }

    _wakeState = state.WakeState;
    await InvokeAsync(StateHasChanged);
  }
```

In `DisposeAsync`, immediately after `_disposed = true;`, add:

```csharp
    if (_reportedVisible)
    {
      // Best effort. A hard browser navigation can tear the circuit down before this lands, which
      // is why MainLayout reports the opposite on its own first render: between them the flag is
      // self-correcting, and the worst case is one knob input consumed on the way out.
      try
      {
        await SystemApi.SetSleepScreenVisibleAsync(false);
      }
      catch (Exception ex)
      {
        Logger.LogDebug(ex, "Could not report the sleep screen hidden during dispose");
      }
    }
```

- [ ] **Step 4: Report the opposite from `MainLayout`**

In `MainLayout.OnAfterRenderAsync`, inside the existing `if (firstRender)` block that registers the JS
bridge (`:392-397`), append:

```csharp
      // ENC-6. MainLayout rendering is proof the sleep screen is not up, which is what corrects a
      // stale flag left behind when a hard navigation killed Sleep.razor's circuit before its
      // dispose could report. Fire-and-forget on purpose: the layout must render whether or not the
      // API is reachable.
      _ = SystemApi.SetSleepScreenVisibleAsync(false);
```

Confirm `SystemApi` is the injected field name in this file before writing it; `MainLayout` already
calls `SystemApi.SetSleepAsync` at `:1092`, so it is in scope.

- [ ] **Step 5: Verify the build and the shipped Web suite**

```bash
dotnet build --configuration Release
dotnet test tests/Radio.Web.Tests --configuration Release
```

Expected: **0 warnings**, and every shipped `SleepTests` fact still green — the hermetic rig fails the
new POST, `SetSleepScreenVisibleAsync` returns `null`, and `_wakeState` keeps its Ambient default, so
the two facts asserting `"tap anywhere to wake"` are unaffected.

- [ ] **Step 6: Commit**

```bash
git add src/Radio.Web/Models/ApiModels.cs src/Radio.Web/Services/ApiClients/SystemApiService.cs src/Radio.Web/Components/Pages/Sleep.razor src/Radio.Web/Components/Layout/MainLayout.razor
git commit -m "$(cat <<'EOF'
ENC-6: the sleep screen reports itself, so both ways in are one state

/sleep reached by the idle timer and /sleep reached by the Sleep pill were
different states, and UAT through the pill produced a false pass for the
idle path. The page now reports itself on first render and on dispose, and
MainLayout reports the opposite on its own first render so a hard
navigation cannot leave the flag stale.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

### Task 8: The Standby hint

**Files:**
- Modify: `src/Radio.Web/Components/Pages/Sleep.razor:113-116`, `:403-410`
- Test: `tests/Radio.Web.Tests/Components/Pages/SleepTests.cs`

**Interfaces:**
- Consumes: `_wakeState` (Task 7); `AudioStateHubService.SleepStateChanged` (shipped).
- Produces: nothing consumed later.

**The copy, and the declared deviation.** Ambient keeps `tap anywhere to wake`. Standby reads
**`tap anywhere, or press any knob, to turn on`** — deviation **D-1** in §0.4, with the reason. Do not
substitute the handoff's literal line.

**⚠ The bUnit path is the SignalR event, not the HTTP response** (§0.7). The rig fails the POST, so a
test that tried to drive Standby through it would pass vacuously.

- [ ] **Step 1: Write the failing tests**

Append to `SleepTests`. Read `Sleep_ServerWake_NavigatesHome` first and reuse its
`FireSleepStateChangedAsync` helper exactly as written.

```csharp
  [Fact]
  public void Sleep_ByDefault_ShowsTheAmbientHint()
  {
    // The API report fails in this rig by design, so this also pins the fallback: a console whose
    // audio is still playing must never be labelled as switched off.
    var cut = RenderComponent<Sleep>();

    cut.Find(".sleep-screen-hint").TextContent.Trim().Should().Be("tap anywhere to wake");
  }

  [Fact]
  public async Task Sleep_WhenAudioIsParkedWhileTheScreenIsUp_SwitchesToTheStandbyHint()
  {
    // Handoff 8.6: in Standby a tap does something different from a turn, and this line is the only
    // place on screen that can say so. Reachable while the page is already up - the Sleep pill on
    // another tab, or a VOLUME long-press on the cabinet.
    var hub = Services.GetRequiredService<AudioStateHubService>();
    var cut = RenderComponent<Sleep>();

    await cut.InvokeAsync(() => FireSleepStateChangedAsync(hub, true));

    cut.Find(".sleep-screen-hint").TextContent.Trim()
      .Should().Be("tap anywhere, or press any knob, to turn on");
  }

  [Fact]
  public async Task Sleep_StandbyHint_NamesTheTap()
  {
    // ENC-6 deviation D-1. Handoff 8.6's own line omits the tap, and 8.3's table says a screen touch
    // in Standby resumes - so the handoff copy would have the screen assert something false by
    // omission on a touchscreen. Pinned so a later consistency pass does not "restore" it.
    var hub = Services.GetRequiredService<AudioStateHubService>();
    var cut = RenderComponent<Sleep>();

    await cut.InvokeAsync(() => FireSleepStateChangedAsync(hub, true));

    cut.Find(".sleep-screen-hint").TextContent.Should().Contain("tap");
  }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~SleepTests"
```

Expected: the two Standby facts FAIL — the hint is a hard-coded literal and
`OnSleepStateChanged(true)` returns `Task.CompletedTask` without touching state.

- [ ] **Step 3: Replace the hint markup and its TODO**

Replace `Sleep.razor:113-116` (the `TODO(ENC-6)` comment) and `:116` (the hint `div`) with:

```razor
  @* Handoff §8.6. In Standby a tap does something different from a turn — a tap or a press resumes,
     a turn only answers where you are — and this line is the only place on screen that can say so. *@
  <div class="sleep-screen-hint">@HintText</div>
```

- [ ] **Step 4: Add the hint property and make the sleeping edge update state**

Add to the `@code` block:

```csharp
  /// <summary>
  /// ENC-6 deviation D-1: the Standby line names the tap, which handoff §8.6's copy omits. §8.3's
  /// own table says a screen touch in Standby resumes, so a line reading only "hold VOLUME or press
  /// any knob" would have the screen assert something false by omission on a touchscreen. Any wake
  /// state this build does not recognise falls back to the Ambient line — the same open-string rule
  /// §6.10 sets for the HUD phase.
  /// </summary>
  private string HintText =>
    _wakeState == nameof(Radio.Core.Interfaces.ConsoleWakeState.Standby)
      ? "tap anywhere, or press any knob, to turn on"
      : "tap anywhere to wake";
```

Replace `OnSleepStateChanged` (`:403-410`) with:

```csharp
  /// <summary>
  /// If the server (or another tab) flips wake while we're on /sleep, follow
  /// it home so the route doesn't linger behind an already-awake system. Idempotent —
  /// firing on a wake we initiated is harmless because we already navigated
  /// away in HandleWakeAsync.
  ///
  /// <para>
  /// ENC-6: the sleeping edge is no longer a no-op. Audio has just been parked while this page was
  /// already up — the Sleep pill on another tab, or a VOLUME long-press on the cabinet — so the
  /// screen is now in Standby and the hint has to say what a tap does there.
  /// </para>
  /// </summary>
  private Task OnSleepStateChanged(bool isSleeping)
  {
    if (!isSleeping)
    {
      return InvokeAsync(() => Nav.NavigateTo("/"));
    }

    return InvokeAsync(() =>
    {
      if (_disposed)
      {
        return;
      }

      _wakeState = nameof(Radio.Core.Interfaces.ConsoleWakeState.Standby);
      StateHasChanged();
    });
  }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~SleepTests"
```

Expected: **PASS**, including `Sleep_ServerSleepingEvent_DoesNotNavigate` (it asserts the URI, which is
unchanged) and both shipped facts asserting `"tap anywhere to wake"` (they never fire the sleeping edge).

- [ ] **Step 6: Full build and suite**

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Expected: **0 warnings**, full suite green.

- [ ] **Step 7: Commit**

```bash
git add src/Radio.Web/Components/Pages/Sleep.razor tests/Radio.Web.Tests/Components/Pages/SleepTests.cs
git commit -m "$(cat <<'EOF'
ENC-6: the sleep screen says which state it is in

Standby and Ambient look identical and behave differently - a tap resumes
in one and a turn does nothing in either - so the hint line is the only
surface that can tell them apart. Closes the TODO(ENC-6) the sleep page has
been carrying since ENC-4.

The Standby copy deviates from handoff 8.6 on purpose: 8.6's line omits the
tap, while 8.3's table says a screen touch in Standby resumes, so the
handoff copy would have a touchscreen deny a gesture that works.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

## Phase 4 — documentation and verification

### Task 9: Document the model where an operator will find it

**Files:**
- Modify: `design/INTEGRATIONS.md` §1 (the rotary-encoder section, before the *"The screen is dark"*
  recovery block)

**Interfaces:** none — documentation only.

- [ ] **Step 1: Insert the section**

Add immediately before the `### ⚠ The screen is dark and will not come back — recovery` heading:

```markdown
### Sleep, wake, and the three states (`ENC-6`)

The console has **three** states, derived from two facts rather than stored as a state machine.
`ISleepService.WakeState` is the derivation and `Radio.Core.Interfaces.ConsoleWakeState` is the enum.

| State | `IsSleeping` | `/sleep` on screen | Audio | Reached by |
|---|---|---|---|---|
| **Awake** | false | no | playing | any wake |
| **Ambient** | false | yes | **playing** | 30 min idle (`idle-dimmer.js`), or navigating to `/sleep` |
| **Standby** | **true** | yes | **paused + muted** | the topbar Sleep pill · a VOLUME long-press · `POST /api/system/sleep` |

**Designer Rev 5 §8 describes five states. The two dark ones are withdrawn** with the blanking half —
see the recovery section below for why the panel must never blank.

**What each input does** (handoff §8.3, minus the two dark columns):

| Input | Awake | Ambient | Standby |
|---|---|---|---|
| VOLUME turn / press / hold | acts | **acts in place** — the readout renders on the sleep screen's own HUD host | consumed; the press **resumes** |
| SOURCE / PRESETS / TUNING turn | acts | consumed, and **wakes** to the full UI | consumed; **does not resume audio** (D22) |
| SOURCE / PRESETS / TUNING press | acts | consumed, and **wakes** | **resumes** |
| Screen tap | acts | wakes | **resumes** |

**Two things that are easy to get wrong here, both of which shipped as bugs:**

- **The idle timer must NOT call `SetSleepAsync(true)`.** Ambient is defined by playback continuing.
  The server learns about Ambient from **`Sleep.razor` reporting itself** via
  `POST /api/system/sleep-screen`, on first render and on dispose — which is also what makes all
  three ways of reaching `/sleep` one state rather than two. `MainLayout` reports the opposite on its
  own first render, so a hard browser navigation cannot leave the flag stale.
- **A wake spends exactly one input.** `ISleepService.TryClaimWake()` is a synchronous latch, and
  `WakeState` reads `Awake` from the instant a claim is taken — earlier than either `IsSleeping`
  flipping or the browser leaving the route. Without it a fast spin would lose every detent for the
  length of a page navigation instead of one. A consumed input still publishes that knob's current
  value, so the first detent answers *where am I* and the second one moves it.
```

- [ ] **Step 2: Commit**

```bash
git add design/INTEGRATIONS.md
git commit -m "$(cat <<'EOF'
ENC-6: document the three-state sleep model in INTEGRATIONS

Including the two things that already shipped as bugs: the idle timer must
not pause audio, and a wake must spend exactly one input.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

### Task 10: Run the UAT on the box, and record it

**Files:**
- Create: `docs/uat/2026-09-XX-enc6-sleep-wake/REPORT.md` (date it on the day it runs)

**Interfaces:** none.

⚠ **This task is not optional and it is not a formality.** The HTTP round trip in Task 7 is covered by
**nothing else** — the bUnit rig fails every outbound call by design (§0.7), and `ENC-8` shipped a page
that could not read its own API response with a fully green suite.

- [ ] **Step 1: Deploy**

```bash
./deploy/Deploy-ToLinux.ps1
```

Wait for both SHA verifications and the `Kiosk is live` line.

- [ ] **Step 2: Run the Test Plan below in full**, on the cabinet, with the knobs.

- [ ] **Step 3: Write the report**, one row per scenario, with the observed result and any deviation.
  Record the **measured** count for scenario E — the number of detents lost — because *"one, not
  twelve"* is the acceptance criterion and a number is what proves it.

- [ ] **Step 4: Commit the report.**

```bash
git add docs/uat/
git commit -m "$(cat <<'EOF'
ENC-6: UAT - both /sleep entry paths, exercised separately

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_017jnLVx1nqtYiEPdqY56Yst
EOF
)"
```

---

## Test Plan

**Automated coverage** is the 7 new `SleepServiceTests` facts (Task 1), 3 more (Task 2), 4
`SystemControllerTests` facts (Task 3), 8 gate facts + 4 readout facts + 1 inverted fact
(Tasks 5–6), and 3 `SleepTests` facts (Task 8). Run everything with:

```bash
dotnet build --configuration Release   # 0 warnings
dotnet test --configuration Release    # full suite green
```

### ⚠ UAT: the two `/sleep` entry paths must be exercised SEPARATELY

**A Builder confirmed today that `/sleep` reached by idle and `/sleep` reached by the Sleep pill are
different states, and that UAT through the pill produces a false pass for the idle path.** That is the
whole reason this row exists, and it is still true of the *test procedure* even after this row makes it
untrue of the *product*: the pill parks audio and the idle timer does not, so a scenario run through
the pill exercises Standby and says nothing whatsoever about Ambient.

**Shortening the 30-minute idle timer for UAT is not necessary and not permitted.** Reach Ambient by
navigating the kiosk directly to `http://radio:5002/sleep`, which produces the identical server-side
state — `Sleep.razor` reports itself the same way from every entry — and confirm the equivalence once,
in scenario A, by reading the API rather than by trusting it.

| # | Scenario | Steps | Expected |
|---|---|---|---|
| **A** | **The two paths converge — run this first** | (1) On the kiosk, navigate directly to `/sleep`. `curl -s http://radio:5000/api/system/sleep`. (2) Tap to wake. (3) Press the topbar **Sleep** pill. `curl` again. | (1) `{"isSleeping":false,"wakeState":"Ambient"}` — **audio still playing**. (3) `{"isSleeping":true,"wakeState":"Standby"}` — audio paused. ⚠ **If (1) reports `Awake`, the page is not reporting itself and every scenario below is invalid** — fix that before continuing. |
| **B** | **Ambient, the overnight failure mode** | From (A1)'s Ambient state, with music playing: turn **VOLUME** one detent. | The volume **changes**, audibly. A dim VOLUME readout appears on the sleep screen, on the **top** band. **The screen stays on the clock** — it does not navigate to Home. This is Rule 2's "acts in place". |
| **C** | **Ambient, everything else wakes** | From Ambient, turn **TUNING** one detent. Then repeat from a fresh Ambient with a **press** instead. | A readout for **that knob** appears showing its **current** value; the frequency **does not change**; the kiosk then navigates to the full Home UI. Repeat for the remaining two knobs. |
| **D** | **Standby — D22, the settled rule** | From (A3)'s Standby: (1) turn **SOURCE**. (2) turn **VOLUME**. (3) **press** any knob. | (1) and (2): a readout appears showing that knob's current value; **audio does not resume**; the clock stays up; nothing changes. (3) audio **resumes**, the pre-sleep mute state is restored, and the kiosk navigates Home. ⚠ **This is Designer's own acceptance test verbatim** and the one the owner approved. |
| **E** | **The wake latch — count the detents** | From Ambient, spin **TUNING** as fast as possible, ~12 detents in one motion. | The **first** detent is consumed and wakes; the rest **tune**. Read the resulting frequency and confirm it moved by roughly *(detents − 1)* channels. ⚠ **Record the measured number lost.** The criterion is *one, not twelve*. |
| **F** | **The Standby hint** | Enter Standby via the pill. Read the line under the clock. Then, from **Ambient**, hold **VOLUME** for 600 ms. | Standby: `tap anywhere, or press any knob, to turn on`. Ambient: `tap anywhere to wake`. The hold from Ambient enters Standby **and the hint changes on screen without a reload** — that is the SignalR path. |
| **G** | **The tap still works from both** | Tap the screen from Ambient. Tap it from Standby. | Both navigate Home. From Standby, audio resumes and the pre-sleep mute state is restored. |
| **H** | **Nothing blanks** | Leave the kiosk on `/sleep` for 5 minutes. | `ssh mmack@radio 'cat /sys/class/drm/card1-DP-1/dpms'` reads **`On`** throughout. ⚠ **`ENC-15` failed its gate; a blank here is a regression, not a feature.** |
| **I** | **Awake is untouched** | From Home, turn and press all four knobs. | Every knob behaves exactly as it did before this row. Nothing is consumed. |
| **J** | **No new log noise** | `ssh mmack@radio "journalctl -u radio-api --since '-10min' --no-pager \| tail -50"` | No warnings or errors from `SleepService` or `RotaryEncoderActionRouter`. ⚠ Keep the query bounded — heavy journal reads on this box correlate with audible audio distortion. |

---

## Self-Review

**Spec coverage.** Every clause of handoff §8 that survives §0.1 maps to a task:

| Handoff | Task |
|---|---|
| §8.2 Rule 2, lit-panel behaviour | 5 |
| §8.2 state table (Awake / Ambient / Standby) | 1 |
| §8.3 Ambient column | 5, 6, UAT B–C |
| §8.3 Standby column + **D22** | 5, UAT D |
| §8.3 "a consumed input still renders that knob's current value" | 6 |
| §8.5 "the wake must consume exactly one event, not a window" | 1 (latch), 5 (gate), UAT E |
| §8.5 "no knob input should restart the idle countdown while on `/sleep`" | **no change needed** — `idle-dimmer.js:43-52` already declines to run timers on that route, and this row adds no JS timer |
| §8.6 the Standby hint | 8 (deviation D-1) |
| §8.6 the Ambient readout inside the drift wrapper | **shipped by `ENC-4c`** — `Sleep.razor:70-73` already hosts `EncoderHud` with `Variant="Sleep"` |
| §2.3 "idle navigates without calling `SleepService`" | 7 |
| §6.10 open-string forward compatibility | applied to `WakeState` in 3, 7, 8 |
| §8.5 blanking, the coupling rules, the Dark states, the re-blank timers | **out of scope — `ENC-15`** (§0.1, deviation D-2) |

**Placeholder scan.** No `TBD`, no "implement later", no "similar to Task N", no "add error handling".
The one intentional stub — Task 5 Step 6's empty `PublishCurrentValue` — is scoped to a single commit,
is labelled as such in the code, and is closed by Task 6 with tests that fail until it is.

**Type consistency.** `ConsoleWakeState` is the enum in every task. `WakeState` is the property name on
`ISleepService`, the JSON field, the API DTO member and the Web DTO member. `SetSleepScreenVisible` is
the sync service method; `SetSleepScreenVisibleAsync` is the Web HTTP client method; `SetSleepScreenVisible`
is also the controller action — matching the shipped `SetSleepAsync` (client) / `SetSleepState`
(controller) / `EnterSleepAsync` (service) naming split rather than inventing a fourth convention.
`PublishCurrentValue(int)` dispatches to `PublishCurrent{Volume,Tuning,Source,Viz}(int)` throughout.
`TryClaimWake()` has one spelling everywhere.

**Three symbols this plan deliberately does NOT use, because they do not exist:** `SetSleepAsync` on
the service (it is `EnterSleepAsync` / `WakeAsync`; `SetSleepAsync` is only the Web client), `SleepAsync`
(nothing by that name), and `EncoderPhase` (it is `EncoderHudPhase` on the API side and an open string
on the wire).
