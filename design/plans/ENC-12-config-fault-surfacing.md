# PLAN — `ENC-12` · Tiered config-fault surfacing: giving `ENC-11`'s safety response a voice

> **Status:** ready for Builder **after `ENC-8` merges**. Written 2026-09-02 against `194b16b`.
> **Punch list:** [`docs/HANDOFF-GA-PUNCH-LIST.md`](../../docs/HANDOFF-GA-PUNCH-LIST.md) §3.5 `ENC-12` (P0).
> **Design:** [`HANDOFF-rotary-encoder-mapping.md`](../../docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md)
> Rev 3 §7.2, §7.3, §7.4, §7.6 · pattern reused from
> [`HANDOFF-bell-failure-surfacing.md`](../../docs/design-handoffs/HANDOFF-bell-failure-surfacing.md) §3.7, §8.3.
> **Depends on:** `ENC-11` (O10) — shipped · **`ENC-4`** (the topbar/`MainLayout` edits) · **`ENC-8`**
> (this row consumes its status contract and links to its page).
> **Follows the handoff**, with one declared narrowing (§0.4 C-3) and five contradictions resolved in §0.4.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`ENC-11`'s tiered fault model is live and verified on hardware. When a *safety* field does not read
back, the host **already** drops the volume knob's per-event clamp from 6 units to 2 and holds it
there until a push verifies (`RotaryEncoderConfigVerifier.VolumeClampFor`). **That response is real,
correct, and completely silent** — it reaches one log line and the API, and stops. The owner
experiences it as a volume knob that has quietly become sluggish, on a console inside sealed
furniture, with no way to find out why without a laptop. `ENC-8` builds the page that explains it.
**This row is the two surfaces that get the owner to that page** — a badge legible from any route, and
one notification, once.

Normal boot stays **completely silent**. No toast, no splash, no banner (§7.4). The repair path
speaks only when something needed repairing.

### 0.2 The shared-card decision — this row adds **nothing** to the Settings page

The punch list describes `ENC-12`'s third surface as *"the status card carries the field-level `Sent`
vs `Read back` table plus `Retry now` / `Restore designed defaults`."* That surface is **`ENC-8`'s
page**, described from a row written before Rev 3 reconciled the two. It is built once, by `ENC-8`,
and this row consumes it.

> **Decision, recorded in both plans: `ENC-8` owns every pixel under System Config → Integrations →
> Rotary Encoders, and every provisioning endpoint. `ENC-12` owns the topbar badge and the
> notification, and adds no markup to that page, no endpoint to `IntegrationsController`, and no
> button anywhere.**

What this row does add is the **push** channel. `ENC-8`'s page pulls at 2 Hz while it is open, which
is right for a page and useless for a badge that must be correct on `/queue`. So this row adds one
SignalR broadcast carrying **the tier and nothing else** — no field detail, no 24-row table. The
detail stays behind the page the owner has to open, which is the same `UI-2` load argument that made
`ENC-8` a pull surface, applied to a different job.

### 0.3 Things Builder must NOT do

1. ⛔ **Do not announce anything on a healthy boot.** Handoff §7.4 is the governing rule and it has
   three reasons behind it, one of which is that *"a status message for a thing that always succeeds
   trains people to ignore status messages."* `Configured` produces no toast, no badge, no sound.
   Task 10 pins it.

2. ⛔ **Do not let a flapping fault become a notification storm.** This is stated in the punch list,
   in handoff §7.6, and again in the task brief. §0.5 defines the exact latch and Task 10 proves it
   with a test that raises the same tier fifty times and asserts one notification.

3. ⛔ **Do not put the badge or the toast on `/sleep`.** That route is on `EmptyLayout` and is
   deliberately dark; `ENC-6` owns what may appear there. Everything in this row mounts in
   `MainLayout` only. A fault that begins while the panel is asleep announces when the owner returns
   to a normal route, which is when they can act on it.

4. ⛔ **Do not signal with colour alone.** Bell handoff §8.3 and WCAG 1.4.1. Each tier gets its own
   **glyph**, and the `aria-label` carries the state in words regardless (§2.3).

5. ⛔ **Do not add a design token, and specifically do not reference or declare `--signal-red-glow`.**
   It is consumed at `design-system.css:5364` and **never declared** in `:root`; declaring it would
   silently change an unrelated shipped component. Recorded in `ENC-8` §0.4 C-11 and logged to
   `design/FUTURE-WORK.md` there.

6. ⛔ **Do not build a second notification channel.** Radzen `NotificationService`, the same one
   `RadioControlPanel.razor:1366-1373` uses for the preset-delete undo, mounted through the existing
   `<RadzenComponents />` at `MainLayout.razor:25`.

### 0.4 Contradictions found while planning, and how each is resolved

| # | The disagreement | Resolution, and why |
|---|---|---|
| **C-1** | The punch list's `ENC-12` row gives this row a **`Retry now`** button and a **`Restore designed defaults`** button on the status card. | **Neither is built here, and only one is built at all.** `Retry now` and handoff §7.8's `Re-apply settings` are the same action under two names; Rev 3's name wins and `ENC-8` Task 16 builds it. `Restore designed defaults` is not built by either row — see `ENC-8` §0.4 C-2 for the full argument. This row's toasts link to that page rather than carrying buttons of their own. |
| **C-2** | Handoff §7.6's tier table gives **Absent** an amber badge, and §7.3 gives a mid-session disconnect a toast. The punch list's `ENC-12` row mentions **only** Degraded and hard fault. | **The badge covers Absent** — it is the same badge in the same corner, it costs one branch, and leaving it out would mean building this badge twice. **The disconnect toast is Task 8b and is marked cuttable**: it is the one piece of scope here that comes from the handoff rather than the punch list, and the owner can drop it without affecting anything else in this row. |
| **C-3** | Handoff §7.3 wants a toast on **every** mid-session disconnect and a *"Knobs connected"* toast on recovery. §7.6 forbids notification storms. **A USB lead flapping inside furniture satisfies both at once.** | **Narrowed, deliberately: at most one disconnect toast and one reconnect toast per browser session.** A lead that drops repeatedly is itself a fault, and the badge stays visible for the whole of it — the toast's job is to tell you once that something changed, not to narrate every bounce. This is a genuine narrowing of the handoff and is called out here so it is a decision rather than a bug. Task 10 proves it. |
| **C-4** | Designer §7.6 says *"one notification, once per session, **per transition**"*, which reads as *re-announce each time the fault returns*. §7.6 also says a flapping fault must not storm. Those conflict when a tier oscillates. | **Escalation-only, monotonic for the life of the circuit** (§0.5). Degraded announces once; a later escalation to a hard fault announces once more, because it is strictly worse and the volume knob has just been clamped; nothing de-escalating or repeating announces again. This is the only reading that satisfies both sentences, and it is testable as a rule rather than a feel. |
| **C-5** | `AudioStateStore.cs:222-224` documents `EncoderConnection` as *"Latest encoder presence transition, or null if none has been observed **this circuit**."* | **False — `AudioStateStore` is registered `AddSingleton` at `Program.cs:443`, so that field is process-wide, not per-circuit.** A shipped comment asserting more than the code does, in the exact file this row extends, and precisely the failure class `CLAUDE.md` § Pre-Merge Review enumerates. Fixed in Task 4. **It also decides where this row's latch lives** — a latch in a singleton would mean a page reload never re-announces a fault that is still present. §2.2. |

### 0.5 The latch, stated as a rule

> **Each browser session announces each reportable severity at most once, and only on escalation.**

`Configured` and `Transient` are rank 0 and never announce. `Degraded` and `Absent` are rank 1.
`HardFault` is rank 2. The circuit remembers the highest rank it has announced, and speaks only when
an incoming rank is **strictly greater**. The memory is never reset — not on recovery, not on
reconnect.

| Sequence | Notifications |
|---|---|
| `Configured` forever | **0** |
| `Transient` × 20 → `Configured` | **0** |
| `Degraded` × 50 | **1** |
| `Degraded` → `Configured` → `Degraded` → `Configured` → `Degraded` | **1** |
| `Degraded` → `HardFault` | **2** |
| `HardFault` → `Degraded` → `HardFault` | **2** |
| `HardFault` first, no prior degrade | **1** |

Not resetting on recovery is the whole anti-storm property, and it is a deliberate trade: a fault
that clears and returns an hour later is silent the second time. **The badge is what covers that** —
it is stateless, it tracks the live tier exactly, and it is on screen the entire time the fault
exists. The toast's job is to get the owner's attention once; the badge's job is to still be there
when they come back.

---

## 1. What this row consumes, and what it must not duplicate

| From | What | Where |
|---|---|---|
| `ENC-11` | `IRotaryEncoderService.ConfigStatus` — the live tier | `IRotaryEncoderService.cs:22` |
| `ENC-0` | `EncoderConnectionChanged` broadcast + `EncoderConnectionDto` (`IsConnected`, `WasEverConnected`) — **already on the wire and already cached** | `AudioStateUpdateService.cs:973-993`, `AudioStateHubService.cs:58,225-234`, `AudioStateStore.cs:214-225` |
| `ENC-8` | The Settings page the toasts link to, and its tier vocabulary | `SystemConfigPage.razor`, `EncoderTierText` |
| `ENC-4a` | The pattern for a persistent topbar indicator | `MainLayout.razor:73-84`, `design-system.css:5865-5899` |
| Bell surfacing | The corner-pinned nav-pill fault badge, **shipped and working** | `MainLayout.razor:156-184`, `BellHealth.cs:74,116-120`, `design-system.css:5303-5332` |

**The presence half is already half-built.** `ENC-0` shipped the transport, the asymmetric
`WasEverConnected` flag, and the singleton cache. What it did **not** ship is any UI that consumes
them — nothing in `src/Radio.Web` renders a badge or raises a toast from `EncoderConnectionChanged`;
the only consumer is `SystemConfigPage.razor:1998`, which just refetches its own status card. So the
`WasEverConnected` distinction ENC-0's XML doc explains at length has, to date, never been used.
**This row is what uses it.**

**The config half has no transport at all.** `ConfigStatus` has no change event and no broadcast; its
only consumers in `src/` are the router's clamp and the verifier. Tasks 1–3 build that.

---

## 2. Architecture

### 2.1 The shape, end to end

```
  HidRotaryEncoderService              AudioStateUpdateService          AudioStateHubService
    ConfigStatus setter  ──────────►     OnEncoderConfigStatus   ──────►   EncoderConfigStatusChanged
    raises ConfigStatusChanged             SendAsync(...)                          │
    ONLY on a real change                                                          ▼
                                                                            AudioStateStore
                                     (EncoderConnectionChanged — already exists)  caches both
                                                                                   │
                                                       ┌───────────────────────────┴────────────┐
                                                       ▼                                        ▼
                                            EncoderFaultRules (pure)              EncoderFaultAnnouncer (SCOPED)
                                              badge glyph / severity                  the §0.5 latch
                                              aria text / toast copy                        │
                                                       │                                    ▼
                                                       ▼                          NotificationService (one toast)
                                            MainLayout Settings nav pill
                                              .encoder-nav-fault
```

Two things about this diagram are load-bearing:

- **`EncoderFaultRules` is a pure static class with no dependencies**, mirroring `BellHealthRules`
  (`src/Radio.Web/Models/BellHealth.cs`). §2.5 explains why that is not stylistic.
- **`EncoderFaultAnnouncer` is scoped (per circuit); everything else it reads is singleton.** §2.2.

### 2.2 The latch is per circuit, and that is a decision

"Once per session" needs a definition of session, and the tree offers two precedents that disagree:
`AudioStateStore` and `EncoderHudService` are **singletons** (process-wide), while
`GainPopoverService` is **scoped** (per Blazor circuit).

> **The announcer is scoped, like `GainPopoverService`.**

- A toast is a per-browser-session UI event. A **singleton** latch would mean a reload never
  re-announces a fault that is still present, and that a second browser (a laptop on the desk during
  a UAT pass) never sees the fault at all.
- On the kiosk this is a distinction without a difference — one Chrome, one circuit, essentially
  permanent — so the scoped choice costs nothing there and is correct everywhere else.
- ⚠ It also means the announcer **must not** be the thing that caches state. It reads
  `AudioStateStore` (singleton, already correct for that job) and holds only its own two latch
  values. Note in passing that `AudioStateStore`'s own doc comment claims to be per-circuit and is
  not (§0.4 C-5) — Task 4 fixes the comment, not the lifetime; singleton is right for a cache of one
  cabinet's hardware state.

### 2.3 The badge goes on the **Settings** pill, and each tier gets its own glyph

The bell badge sits on `/phone` because that is where the explanation lives (bell handoff §3.7: *"its
job is only to get someone to `/phone`"*). The encoder fault's explanation lives on **`/system` →
Integrations → Rotary Encoders**, so the badge sits on the **Settings** pill
(`MainLayout.razor:151-155`), bottom-right, exactly as `.phone-nav-fault` does.

⚠ **One deliberate improvement on the bell pattern.** The bell has one fault state and therefore one
glyph. This row has **three** reportable states, and Designer separates two of them only by colour —
amber for Degraded/Absent, red for hard fault. Colour alone fails WCAG 1.4.1 and the project's own
§8.3 rule, so each state gets a distinct Material glyph:

| Tier | Glyph | Colour | `aria-label` suffix |
|---|---|---|---|
| `Degraded` | `warning` | `--signal-amber` | *"— knob settings not applied"* |
| `HardFault` | `error` | `--signal-red` | *"— knob safety settings not applied, volume limited"* |
| Absent | `link_off` | `--signal-amber` | *"— knobs not connected"* |
| everything else | none | — | (no suffix) |

Precedence when both a config fault and absence are live: **absence wins the glyph**, because a
device that is not there cannot have its configuration fixed and "not connected" is the actionable
fact. The `aria-label` still names it as one state, never two.

### 2.4 One broadcast, and it fires on change only

`ConfigStatus` is written inside `ApplyConfigurationAsync`'s retry loop, which can set the same value
several times in a row (`Transient` on attempts 1 and 2). Broadcasting each write would put SignalR
traffic on the wire for a state that did not change, on a box where incidental load correlates with
audible distortion.

> The event fires **only when the value actually changes**, and the change detection lives in the
> property setter so no caller can forget it (Task 1).

There is no throttle beyond that and none is needed: the tier changes at most a handful of times per
connection, unlike `ENC-4`'s HUD which needed a 50 ms coalescer.

### 2.5 `MainLayout` cannot be rendered in bUnit, so the rule must be a pure function

`tests/Radio.Web.Tests/Components/Layout/MainLayoutTests.cs` is a **documented stub that renders
nothing** — its own XML doc says Radzen + JSInterop make the layout impractical to render, and
`EncoderHudTests.cs:26-27` restates it. The consequence is measurable: **nothing under `tests/`
asserts `.topbar-mute-chip` or `.phone-nav-fault` in rendered markup.**

So a badge implemented as inline `@if` logic inside `MainLayout.razor` would ship with **zero**
automated coverage. The project already solved this once: `BellHealthRules` is a pure static class
holding every decision the bell badge makes — `IsFaulted`, `NavPillAriaLabel`, `PillClass`,
`PillText` — and it is thoroughly unit-tested (`tests/Radio.Web.Tests/Models/BellHealthRulesTests.cs`)
while the markup that consumes it is covered by the browser Test Plan.

> **`EncoderFaultRules` follows that pattern exactly** (Task 5). Every decision — whether to badge,
> which glyph, which colour class, the accessible name, the toast severity and both toast strings —
> is a pure function of `(status, isConnected, wasEverConnected)` and is unit-tested. `MainLayout`
> gets branches with no logic in them.

That is also what makes §0.5's latch table testable: the latch is exercised against the rules, with
no browser in the loop.

### 2.6 Tokens

Existing only: `--signal-amber`, `--signal-red`, `--text-medium`. The badge reuses
`.phone-nav-fault`'s geometry (absolute, `bottom: 4px`, `right: 4px`, 12 px glyph) via a sibling class
— it does **not** reuse the class itself, because that one is named for the phone and carries
`--signal-red` unconditionally.

⛔ `--signal-red-glow` does not exist (§0.3 item 5).

---

## 3. Tasks

Eleven tasks in five phases. Phases 0–1 are transport, Phase 2 is the logic (and is where nearly all
the test value is), Phase 3 is markup with no logic in it.

Every task ends with `dotnet build --configuration Release` clean and the relevant test project green.

---

### Phase 0 — API: make the tier observable

#### Task 1 — `ConfigStatus` raises an event, on change only

**Why:** §2.4. Today `ConfigStatus` is a silent auto-property written in three places inside
`ApplyConfigurationAsync`. Nothing can observe it.

**Edit** `src/Radio.Core/Interfaces/Input/IRotaryEncoderService.cs` — add beside the existing events:

```csharp
  /// <summary>
  /// Fired when <see cref="ConfigStatus"/> changes value (ENC-12).
  ///
  /// <para>
  /// <b>On change only.</b> The push loop assigns this property once per attempt and may assign the
  /// same value repeatedly — <c>Transient</c> on attempts 1 and 2 is the ordinary case. Broadcasting
  /// every assignment would put SignalR traffic on the wire for a state that did not change, on a box
  /// where incidental load correlates with audible audio distortion.
  /// </para>
  /// </summary>
  event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;
```

and, at the bottom of the same file beside the other event-args types:

```csharp
/// <summary>
/// Event args for a configuration-tier change (ENC-12).
/// </summary>
public class EncoderConfigStatusEventArgs : EventArgs
{
  /// <summary>The tier the device is now in.</summary>
  public RotaryEncoderConfigStatus Status { get; init; }

  /// <summary>The tier it was in immediately before. Never equal to <see cref="Status"/>.</summary>
  public RotaryEncoderConfigStatus PreviousStatus { get; init; }
}
```

**Edit** `src/Radio.Infrastructure/Platform/Input/HidRotaryEncoderService.cs` — turn the
auto-property at `:91` into a backing field with change detection, so no assignment site can forget
to raise it:

```csharp
  private RotaryEncoderConfigStatus _configStatus = RotaryEncoderConfigStatus.Unknown;

  /// <inheritdoc />
  public RotaryEncoderConfigStatus ConfigStatus
  {
    get => _configStatus;
    private set
    {
      // The change check lives here rather than at the four assignment sites, so a fifth site added
      // later cannot introduce a duplicate broadcast by omission.
      if (_configStatus == value)
      {
        return;
      }

      RotaryEncoderConfigStatus previous = _configStatus;
      _configStatus = value;
      ConfigStatusChanged?.Invoke(this, new EncoderConfigStatusEventArgs
      {
        Status = value,
        PreviousStatus = previous,
      });
    }
  }

  /// <inheritdoc />
  public event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;
```

⚠ **On disconnect, reset the tier to `Unknown`.** Today `ConfigStatus` keeps its last value after the
device goes away, so a device that was `Configured` and is then unplugged still reports `Configured`.
That is wrong on its own terms — the app cannot know what an absent device is running — and it would
make the badge claim a healthy configuration for hardware that is not there. In `ReadLoopAsync`,
wherever `_isConnected` is set to `false` and `RaiseConnectionChanged(false)` is called (four sites:
`:177`, `:253`, `:265`, `:282`), set `ConfigStatus = RotaryEncoderConfigStatus.Unknown;` immediately
before. **This is a behaviour change to shipped `ENC-11` code and is intentional** — note that the
volume clamp is derived from the same value, so an unplugged device now also holds the tight clamp,
which is the correct direction (`VolumeClampFor` already returns the tight value for `Unknown`).

**Tests** — extend `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderConfigVerifierTests.cs`
or add a sibling file:

- `ConfigStatus_RaisesOnceWhenItChanges`
- `ConfigStatus_DoesNotRaiseWhenAssignedTheSameValue` — assign `Transient` three times, expect one raise
- `ConfigStatusChanged_CarriesThePreviousTier`

---

#### Task 2 — Broadcast it

**Edit** `src/Radio.API/Services/AudioStateUpdateService.cs`. ⚠ **`ENC-4` is editing this file** —
rebase; this subscription goes beside its `_encoderFeedback` one.

In the subscription block (beside `:143`):

```csharp
    // ENC-12: the config-fault push path. The Settings page polls at 2 Hz while it is open, which is
    // useless for a badge that must be correct on /queue and /metrics, so the tier — and only the
    // tier — goes out on the hub.
    if (_encoderService != null)
    {
      _encoderService.ConfigStatusChanged += OnEncoderConfigStatusChanged;
    }
```

The handler, beside `OnEncoderConnectionChanged` (`:973`):

```csharp
  private async void OnEncoderConfigStatusChanged(object? sender, EncoderConfigStatusEventArgs e)
  {
    try
    {
      // The tier only. No field detail: the 24-field comparison belongs on the Settings page (ENC-8),
      // which is the only place it is actionable, and shipping it to every circuit on every change
      // would be traffic nobody reads.
      await _hubContext.Clients.All.SendAsync("EncoderConfigStatusChanged", new
      {
        Status = e.Status.ToString(),
        PreviousStatus = e.PreviousStatus.ToString(),
      });
      _logger.LogInformation(
        "Broadcast EncoderConfigStatusChanged: {Previous} -> {Status}", e.PreviousStatus, e.Status);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting encoder config status");
    }
  }
```

Unsubscribe in `Dispose` beside the existing two (`:960-968`).

⚠ `Status` is sent as a **string**, matching how `ENC-4` sends `EncoderHudDto.Phase` and for the same
reason: an unknown tier from a newer API build must degrade to "show nothing special" on a kiosk
nobody is watching, not throw during deserialization.

---

### Phase 1 — Web: transport and cache

#### Task 3 — Hub event and DTO

**Edit** `src/Radio.Web/Models/ApiModels.cs` (⚠ `ENC-4` is appending here; rebase):

```csharp
/// <summary>
/// Payload of the SignalR <c>EncoderConfigStatusChanged</c> broadcast (ENC-12).
///
/// <para>
/// <see cref="Status"/> is an open string, not an enum, for the same reason
/// <c>EncoderHudDto.Phase</c> is: a tier this build does not recognise must render as "nothing
/// special" rather than throw on a kiosk nobody is watching. <c>EncoderFaultRules</c> treats every
/// unrecognised value as not-reportable.
/// </para>
/// </summary>
public class EncoderConfigStatusDto
{
  /// <summary>Unknown / Configured / Transient / Degraded / HardFault.</summary>
  public string Status { get; set; } = "Unknown";

  /// <summary>The tier immediately before this change.</summary>
  public string PreviousStatus { get; set; } = "Unknown";
}
```

**Edit** `src/Radio.Web/Services/Hub/AudioStateHubService.cs` (⚠ `ENC-4` is editing this file):

```csharp
  /// <summary>Raised when the encoder's configuration tier changes (ENC-12). Fires on change only, so
  /// this is a handful of events per connection rather than a stream.</summary>
  public event Func<EncoderConfigStatusDto, Task>? EncoderConfigStatusChanged;
```

and the registration, beside the `EncoderConnectionChanged` one at `:225`:

```csharp
      // Server sends EncoderConfigStatusChanged when the configuration tier changes (ENC-12).
      _hubConnection.On<EncoderConfigStatusDto>("EncoderConfigStatusChanged", async (dto) =>
      {
        _logger.LogDebug("Received EncoderConfigStatusChanged: {Status}", dto?.Status);
        if (EncoderConfigStatusChanged != null && dto != null)
        {
          await EncoderConfigStatusChanged.Invoke(dto);
        }
      });
```

Update the class-level `<summary>` list of handled events (`:11-14`) to include it. That comment is a
list of facts about the code and must stay true.

---

#### Task 4 — Cache it, and correct a comment that is false today

**Edit** `src/Radio.Web/Services/AudioStateStore.cs`.

Subscribe in the constructor beside `:76`, unsubscribe in `DisposeAsync` beside `:252`, and add:

```csharp
  /// <summary>Raised when the encoder configuration tier changes.</summary>
  public event Func<Task>? EncoderConfigStatusChanged;

  /// <summary>
  /// Latest encoder configuration tier, or null if none has been observed since this process started.
  /// </summary>
  public EncoderConfigStatusDto? EncoderConfigStatus { get; private set; }

  private async Task OnHubEncoderConfigStatusChanged(EncoderConfigStatusDto dto)
  {
    // Cached so a circuit that connects after the transition still knows the current tier — the badge
    // has to be right on a page loaded ten minutes after the fault, not only on the one that was open
    // when it happened.
    EncoderConfigStatus = dto;
    await NotifyAsync(EncoderConfigStatusChanged);
  }
```

⚠ **Also fix the neighbouring comment at `:222-224`**, which is false (§0.4 C-5):

```csharp
  /// <summary>
  /// Latest encoder presence transition, or null if none has been observed since this process started.
  /// </summary>
  public EncoderConnectionDto? EncoderConnection { get; private set; }
```

It currently reads *"…or null if none has been observed **this circuit**."* `AudioStateStore` is
registered `AddSingleton` (`src/Radio.Web/Program.cs:443`), so the field is process-wide. **Fix the
comment, not the lifetime** — singleton is right for a cache of one cabinet's hardware state, and the
per-circuit thing in this row is the latch, which lives elsewhere (§2.2).

> This is a shipped instance of the failure class `CLAUDE.md` § Pre-Merge Review enumerates. It is
> being corrected here rather than filed, because this row is already in this file.

---

### Phase 2 — the rules and the latch

#### Task 5 — `EncoderFaultRules`, a pure static class

**Why:** §2.5. `MainLayout` cannot be rendered in bUnit, so every decision must live somewhere that
can be. This is `BellHealthRules`' shape, deliberately.

**Create** `src/Radio.Web/Models/EncoderFault.cs`:

```csharp
namespace Radio.Web.Models;

/// <summary>What the owner needs told about the knobs, in order of severity (ENC-12).</summary>
public enum EncoderFaultLevel
{
  /// <summary>Nothing to say. Includes Configured and Transient — see <see cref="EncoderFaultRules"/>.</summary>
  None = 0,

  /// <summary>The knobs work but may feel wrong, or are not plugged in. Amber.</summary>
  Warning = 1,

  /// <summary>A safety field did not apply and the host has tightened the volume clamp. Red.</summary>
  Critical = 2,
}

/// <summary>
/// Every decision the encoder fault badge and its notification make, as pure functions (ENC-12).
///
/// <para>
/// <b>Why this is a separate class rather than logic in <c>MainLayout.razor</c>.</b>
/// <c>MainLayoutTests</c> is a documented stub that renders nothing — Radzen plus JSInterop make the
/// layout impractical to render in bUnit — so nothing under <c>tests/</c> asserts
/// <c>.topbar-mute-chip</c> or <c>.phone-nav-fault</c> in markup. Logic written inline in that file
/// ships with no automated coverage at all. <c>BellHealthRules</c> solved this first and this
/// follows it: the rules are unit-tested here, the markup is covered by the browser Test Plan.
/// </para>
/// </summary>
public static class EncoderFaultRules
{
  /// <summary>
  /// How severe the current hardware state is.
  ///
  /// <para>
  /// ⚠ <c>Transient</c> is deliberately <see cref="EncoderFaultLevel.None"/>. Encoder handoff §7.6:
  /// a USB peripheral missing a report on the first try is ordinary, and reporting it would train the
  /// owner to ignore the badge that matters. Attempts 1-3 are silent by design.
  /// </para>
  ///
  /// <para>
  /// ⚠ An <b>unrecognised</b> status is also <see cref="EncoderFaultLevel.None"/>. A newer API build
  /// sending a tier this kiosk does not know must degrade to silence, not to a badge nobody can
  /// interpret.
  /// </para>
  /// </summary>
  /// <param name="status">Serialized <c>RotaryEncoderConfigStatus</c> name, or null if never observed.</param>
  /// <param name="isConnected">Whether the device is currently present. Null when never observed.</param>
  /// <param name="encoderEnabled">
  /// False when <c>RotaryEncoder:Enabled</c> is off. The owner switched the knobs off deliberately and
  /// must not be nagged about the consequence (encoder handoff §7.3), so this suppresses everything.
  /// </param>
  public static EncoderFaultLevel Level(string? status, bool? isConnected, bool encoderEnabled = true)
  {
    if (!encoderEnabled)
    {
      return EncoderFaultLevel.None;
    }

    // Absence outranks a stale configuration tier: a device that is not there cannot have its
    // configuration fixed, and "not connected" is the actionable fact.
    if (isConnected == false)
    {
      return EncoderFaultLevel.Warning;
    }

    return status switch
    {
      "HardFault" => EncoderFaultLevel.Critical,
      "Degraded" => EncoderFaultLevel.Warning,
      _ => EncoderFaultLevel.None,
    };
  }

  /// <summary>Material icon name for the badge. Empty when nothing should be shown.</summary>
  /// <remarks>
  /// Three states get three glyphs rather than one glyph in two colours. Designer separates Degraded
  /// from a hard fault by colour alone, which fails WCAG 1.4.1 and the project's own rule at bell
  /// handoff §8.3 — so the shape carries the distinction too.
  /// </remarks>
  public static string BadgeIcon(string? status, bool? isConnected, bool encoderEnabled = true)
  {
    if (Level(status, isConnected, encoderEnabled) == EncoderFaultLevel.None)
    {
      return "";
    }

    if (isConnected == false)
    {
      return "link_off";
    }

    return status == "HardFault" ? "error" : "warning";
  }

  /// <summary>CSS modifier for the badge colour. Empty when nothing should be shown.</summary>
  public static string BadgeClass(string? status, bool? isConnected, bool encoderEnabled = true) =>
    Level(status, isConnected, encoderEnabled) switch
    {
      EncoderFaultLevel.Critical => "encoder-nav-fault encoder-nav-fault-critical",
      EncoderFaultLevel.Warning => "encoder-nav-fault encoder-nav-fault-warning",
      _ => "",
    };

  /// <summary>
  /// Accessible name for the Settings nav pill, carrying the fault in words.
  ///
  /// <para>
  /// Not colour, not a glyph — text, because §8.3 requires the state to survive for a user who
  /// perceives neither.
  /// </para>
  /// </summary>
  public static string NavPillAriaLabel(string? status, bool? isConnected, bool encoderEnabled = true) =>
    Level(status, isConnected, encoderEnabled) switch
    {
      EncoderFaultLevel.Critical => "Settings — knob safety settings not applied, volume limited",
      EncoderFaultLevel.Warning when isConnected == false => "Settings — knobs not connected",
      EncoderFaultLevel.Warning => "Settings — knob settings not applied",
      _ => "Settings",
    };

  /// <summary>
  /// The notification copy, verbatim from encoder handoff §7.6. Null when nothing should be said.
  ///
  /// <para>
  /// ⚠ These strings are assertions about what the machine did and each one is true of the shipped
  /// behaviour: the Degraded line promises the knobs still work (they do — host clamps stay in force
  /// and acceleration is treated as absent), and the hard-fault line promises volume is limited (it
  /// is — <c>RotaryEncoderConfigVerifier.VolumeClampFor</c> returns 2 instead of 6 units per event
  /// until a push verifies). <b>Do not soften either one</b>; a reviewer should check them against
  /// that method rather than against plausibility.
  /// </para>
  /// </summary>
  public static (string Summary, string Detail)? NotificationCopy(string? status, bool? isConnected)
  {
    if (isConnected == false)
    {
      return ("Knobs disconnected", "Touch controls still work.");
    }

    return status switch
    {
      "HardFault" => ("Knob safety settings couldn't be applied",
                      "Volume is limited until this is fixed."),
      "Degraded" => ("Knob settings couldn't be applied",
                     "The knobs still work, but they may feel wrong."),
      _ => null,
    };
  }
}
```

---

#### Task 6 — `EncoderFaultAnnouncer`, the latch

**Why:** §0.5 and §2.2. This is the class the anti-storm rule lives in, and it is scoped.

**Create** `src/Radio.Web/Services/EncoderFaultAnnouncer.cs`:

```csharp
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// Decides whether a hardware state change is worth interrupting the owner for, once (ENC-12).
///
/// <para>
/// <b>Scoped, not singleton</b> — unlike <see cref="AudioStateStore"/>, which caches one cabinet's
/// hardware state and is correctly process-wide. This tracks what <i>this browser session</i> has
/// already been told. A process-wide latch would mean a page reload never re-announces a fault that
/// is still present, and that a second browser never hears about it at all. On the kiosk, which runs
/// one long-lived circuit, the two behave identically.
/// </para>
///
/// <para>
/// <b>The rule: each session announces each severity at most once, and only on escalation.</b> The
/// remembered level is never reset — not on recovery, not on reconnect. That is deliberate and it is
/// the whole anti-storm property: a tier that oscillates Degraded → Configured → Degraded speaks
/// exactly once. The trade is that a fault which clears and returns an hour later is silent the
/// second time, and the <b>badge</b> is what covers that — it is stateless, tracks the live tier, and
/// is on screen for as long as the fault exists.
/// </para>
/// </summary>
public sealed class EncoderFaultAnnouncer
{
  private EncoderFaultLevel _highestAnnounced = EncoderFaultLevel.None;
  private bool _announcedDisconnect;
  private bool _announcedReconnect;

  /// <summary>
  /// Whether this state change should raise a notification, and what it should say.
  ///
  /// <para>Returns null — meaning stay silent — for the healthy path, for every repeat, and for every
  /// de-escalation.</para>
  /// </summary>
  /// <param name="status">Serialized <c>RotaryEncoderConfigStatus</c> name.</param>
  /// <param name="isConnected">Current presence.</param>
  /// <param name="wasEverConnected">
  /// From <c>EncoderConnectionDto</c> (ENC-0). Absent at boot gets a badge and <b>no</b> toast — the
  /// owner is most likely standing at the cabinet having just installed or unplugged something.
  /// Disappearing mid-session gets a toast, because it is surprising and may land mid-interaction.
  /// Those are the same <c>IsConnected == false</c> and they are not the same event.
  /// </param>
  public (string Summary, string Detail, EncoderFaultLevel Level)? Evaluate(
    string? status, bool? isConnected, bool wasEverConnected)
  {
    if (isConnected == false)
    {
      // Absent at boot: badge only. wasEverConnected is exactly the flag ENC-0 added to tell the two
      // apart, and until now nothing consumed it.
      if (!wasEverConnected || _announcedDisconnect)
      {
        return null;
      }

      _announcedDisconnect = true;
      var copy = EncoderFaultRules.NotificationCopy(status, isConnected)!.Value;
      return (copy.Summary, copy.Detail, EncoderFaultLevel.Warning);
    }

    // Recovery: announced only for an absence we announced (handoff §7.3), and only once per session
    // (plan §0.4 C-3 — a lead that flaps inside furniture must not narrate every bounce).
    if (isConnected == true && _announcedDisconnect && !_announcedReconnect)
    {
      _announcedReconnect = true;
      return ("Knobs connected", "The knobs are working again.", EncoderFaultLevel.None);
    }

    EncoderFaultLevel level = EncoderFaultRules.Level(status, isConnected);
    if (level <= _highestAnnounced || level == EncoderFaultLevel.None)
    {
      return null;
    }

    _highestAnnounced = level;
    var faultCopy = EncoderFaultRules.NotificationCopy(status, isConnected);
    return faultCopy is null ? null : (faultCopy.Value.Summary, faultCopy.Value.Detail, level);
  }
}
```

**Register** in `src/Radio.Web/Program.cs`, beside `GainPopoverService` (`:459`):

```csharp
// ENC-12. Scoped, like GainPopoverService and unlike AudioStateStore: this tracks what THIS browser
// session has already been told about the knobs, not the state of the knobs themselves.
builder.Services.AddScoped<Radio.Web.Services.EncoderFaultAnnouncer>();
```

---

### Phase 3 — the surfaces

#### Task 7 — The badge on the Settings nav pill

**Edit** `src/Radio.Web/Components/Layout/MainLayout.razor`. ⚠ **`ENC-4` is editing this file** — it
adds `@inject EncoderHudService` to the injection block and mounts `<EncoderHud />` after the gain
backdrop (`:254-259`). Different hunks, but rebase.

Replace the Settings pill (`:151-155`) with:

```razor
        @* ENC-12 — encoder fault badge (encoder handoff §7.6 surface 1), reusing the pattern the
           bell fault badge established at §3.7 of HANDOFF-bell-failure-surfacing.md. It sits on the
           SETTINGS pill because that is where the explanation is, exactly as the bell badge sits on
           /phone for the same reason. Bottom-right, so it would clear a count badge if this pill ever
           gained one.
           Three states get three GLYPHS, not one glyph in two colours: Designer separates Degraded
           from a hard fault by colour alone, and colour alone fails WCAG 1.4.1 and this project's own
           §8.3 rule. EncoderNavLabel carries the state in text regardless.
           title stays the short static "Settings", matching every other pill: mirroring the dynamic
           aria-label would give the element an identical accessible name and description, which
           several screen readers announce twice. *@
        <a href="/system" class="nav-pill @(IsCurrentPage("/system") ? "nav-active" : "")"
           title="Settings"
           aria-label="@EncoderNavLabel">
          <RadzenIcon Icon="settings" />
          <span class="nav-pill-label">Settings</span>
          @if (EncoderBadgeIcon.Length > 0)
          {
            <span class="@EncoderBadgeClass">
              <RadzenIcon Icon="@EncoderBadgeIcon" aria-hidden="true" />
            </span>
          }
        </a>
```

In `@code`, beside the `ENC-4a` mute block (`:1155-1202`) — **branches only, no logic**:

```csharp
  // --- ENC-12: encoder fault badge + notification ---
  //
  // Every decision is in EncoderFaultRules / EncoderFaultAnnouncer rather than here, because
  // MainLayoutTests renders nothing (Radzen + JSInterop) and logic written in this file would ship
  // with no automated coverage. Same reason BellHealthRules exists.

  private string? _encoderStatus;
  private bool? _encoderConnected;
  private bool _encoderWasEverConnected;

  private string EncoderBadgeIcon => EncoderFaultRules.BadgeIcon(_encoderStatus, _encoderConnected);
  private string EncoderBadgeClass => EncoderFaultRules.BadgeClass(_encoderStatus, _encoderConnected);
  private string EncoderNavLabel => EncoderFaultRules.NavPillAriaLabel(_encoderStatus, _encoderConnected);
```

Seed from the store's cache in `OnInitializedAsync` (so a circuit that starts *after* the fault still
shows the badge), then subscribe to both events:

```csharp
    _encoderStatus = AudioState.EncoderConfigStatus?.Status;
    _encoderConnected = AudioState.EncoderConnection?.IsConnected;
    _encoderWasEverConnected = AudioState.EncoderConnection?.WasEverConnected ?? false;
    AudioState.EncoderConfigStatusChanged += OnEncoderConfigStatusChangedAsync;
    AudioState.EncoderConnectionChanged += OnEncoderConnectionChangedAsync;
```

⚠ **Seeding matters and is not belt-and-braces.** The kiosk reloads (a deploy relaunches Chrome); a
badge that only ever reacted to live events would come back blank after every reload while the fault
was still present.

---

#### Task 8 — The notification

**8a — config faults (required).** In the same `@code` block:

```csharp
  private async Task OnEncoderConfigStatusChangedAsync()
  {
    _encoderStatus = AudioState.EncoderConfigStatus?.Status;
    await InvokeAsync(() =>
    {
      AnnounceEncoderStateIfNeeded();
      StateHasChanged();
    });
  }

  /// <summary>
  /// Raises at most one notification per escalation, per browser session. The latch is in
  /// EncoderFaultAnnouncer; this method only renders what it decides.
  /// </summary>
  private void AnnounceEncoderStateIfNeeded()
  {
    var announcement = EncoderAnnouncer.Evaluate(_encoderStatus, _encoderConnected, _encoderWasEverConnected);
    if (announcement is not { } a)
    {
      return;
    }

    NotificationService.Notify(new NotificationMessage
    {
      Severity = a.Level switch
      {
        EncoderFaultLevel.Critical => NotificationSeverity.Error,
        EncoderFaultLevel.Warning => NotificationSeverity.Warning,
        _ => NotificationSeverity.Success,
      },
      Summary = a.Summary,
      Detail = a.Detail,
      // Longer than the 5 s preset-undo toast: that one races a user who is already looking at the
      // control they just used, while this one may land while nobody is at the cabinet.
      Duration = 10000,
      // Handoff §7.6 gives each of these an action: "-> Open encoder settings". Clicking the toast
      // body is how this codebase already does toast actions (RadioControlPanel.razor:1372).
      Click = _ => Navigation.NavigateTo("/system"),
    });
  }
```

⚠ **Do not report the healthy path.** `Evaluate` returns null for `Configured`, for `Transient` and
for every repeat; this method has no branch that speaks without it (§0.3 item 1).

⚠ **`Click` navigates to `/system` but cannot open the Integrations sub-tab directly** — the page's
tabs are `RadzenTabs` with no route parameter. Landing on `/system` is one tap short of the card.
Making it exact would mean adding tab deep-linking to `SystemConfigPage`, which is a separate change
to a page `ENC-8` is already rewriting. **Recorded in `design/FUTURE-WORK.md`, not built here** — and
the toast copy does not promise more than it delivers.

**8b — the mid-session disconnect toast (⚠ CUTTABLE, §0.4 C-2/C-3).**

```csharp
  private async Task OnEncoderConnectionChangedAsync()
  {
    _encoderConnected = AudioState.EncoderConnection?.IsConnected;
    _encoderWasEverConnected = AudioState.EncoderConnection?.WasEverConnected ?? _encoderWasEverConnected;
    await InvokeAsync(() =>
    {
      AnnounceEncoderStateIfNeeded();
      StateHasChanged();
    });
  }
```

This is the branch that finally consumes `WasEverConnected`, the flag `ENC-0` added specifically for
it and which nothing has used since. If the owner cuts 8b, keep this handler minus the
`AnnounceEncoderStateIfNeeded()` call — the **badge** must still track presence either way.

---

#### Task 9 — CSS

**Edit** `src/Radio.Web/wwwroot/css/design-system.css`, beside `.phone-nav-fault` (`:5303-5332`):

```css
/* ENC-12 — encoder configuration fault badge on the Settings nav pill.
   Geometry is deliberately identical to .phone-nav-fault above: same corner, same 12px glyph, so the
   two read as one pattern rather than two. It is a separate class because that one is named for the
   phone and hard-codes --signal-red, while this has an amber tier as well. */
.encoder-nav-fault {
  position: absolute;
  bottom: 4px;
  right: 4px;
  display: inline-flex;
  pointer-events: none;
}

.encoder-nav-fault-warning { color: var(--signal-amber); }
.encoder-nav-fault-critical { color: var(--signal-red); }

/* .nav-pill .rzi sets font-size: 22px !important, so the badge glyph has to override it the same way
   .phone-nav-fault does. */
.nav-pill .encoder-nav-fault .rzi {
  font-size: 12px !important;
  width: 12px;
  height: 12px;
}
```

⛔ **No `box-shadow` / glow.** The obvious flourish would be `0 0 6px var(--signal-red-glow)` to match
`.nav-badge` — **that token does not exist** (§0.3 item 5) and it would render as nothing while
looking correct in the source.

---

### Phase 4 — tests and docs

#### Task 10 — Tests

**`tests/Radio.Web.Tests/Models/EncoderFaultRulesTests.cs`** — modelled on the existing
`BellHealthRulesTests.cs`:

```csharp
[Theory]
[InlineData("Configured")]
[InlineData("Transient")]
[InlineData("Unknown")]
[InlineData("SomeTierFromANewerBuild")]
[InlineData(null)]
public void HealthyOrUnrecognisedTiers_ShowNoBadge(string? status)
{
  // Transient is silent BY DESIGN (handoff §7.6): a USB peripheral missing a report on the first try
  // is ordinary, and badging it would train the owner to ignore the badge that matters. An
  // unrecognised tier is silent so a newer API build degrades to nothing rather than to noise.
  Assert.Equal(EncoderFaultLevel.None, EncoderFaultRules.Level(status, isConnected: true));
  Assert.Equal("", EncoderFaultRules.BadgeIcon(status, isConnected: true));
  Assert.Equal("Settings", EncoderFaultRules.NavPillAriaLabel(status, isConnected: true));
}

[Fact]
public void DisabledEncoders_ShowNothingAtAll()
{
  // The owner switched the knobs off deliberately and must not be nagged about the consequence.
  Assert.Equal(EncoderFaultLevel.None,
    EncoderFaultRules.Level("HardFault", isConnected: false, encoderEnabled: false));
}

[Fact]
public void EachReportableStateHasItsOwnGlyph_NotJustItsOwnColour()
{
  // WCAG 1.4.1 and bell handoff §8.3. Colour alone is not a signal.
  var icons = new[]
  {
    EncoderFaultRules.BadgeIcon("Degraded", isConnected: true),
    EncoderFaultRules.BadgeIcon("HardFault", isConnected: true),
    EncoderFaultRules.BadgeIcon("Configured", isConnected: false),
  };
  Assert.Equal(icons.Length, icons.Distinct().Count());
  Assert.All(icons, i => Assert.NotEqual("", i));
}

[Fact]
public void AriaLabel_CarriesTheStateInWords_ForEveryReportableState()
{
  Assert.Contains("volume limited", EncoderFaultRules.NavPillAriaLabel("HardFault", true));
  Assert.Contains("not applied", EncoderFaultRules.NavPillAriaLabel("Degraded", true));
  Assert.Contains("not connected", EncoderFaultRules.NavPillAriaLabel("Configured", false));
}

[Fact]
public void AbsenceOutranksAStaleConfigurationTier()
{
  Assert.Equal("link_off", EncoderFaultRules.BadgeIcon("HardFault", isConnected: false));
}
```

**`tests/Radio.Web.Tests/Services/EncoderFaultAnnouncerTests.cs`** — **this is the file that proves
§0.5.** Every row of that table is a test:

```csharp
[Fact]
public void AFlappingFault_AnnouncesExactlyOnce()
{
  // The anti-storm property, stated in the punch list, in handoff §7.6, and in the task brief. Fifty
  // transitions, one notification.
  var sut = new EncoderFaultAnnouncer();
  int announcements = 0;

  for (int i = 0; i < 25; i++)
  {
    if (sut.Evaluate("Degraded", isConnected: true, wasEverConnected: true) is not null) announcements++;
    if (sut.Evaluate("Configured", isConnected: true, wasEverConnected: true) is not null) announcements++;
  }

  Assert.Equal(1, announcements);
}

[Fact]
public void EscalationFromDegradedToHardFault_SpeaksASecondTime()
{
  // Strictly worse, and the volume knob has just been clamped. Worth interrupting for.
  var sut = new EncoderFaultAnnouncer();
  Assert.NotNull(sut.Evaluate("Degraded", true, true));
  Assert.NotNull(sut.Evaluate("HardFault", true, true));
}

[Fact]
public void DeEscalation_IsSilent()
{
  var sut = new EncoderFaultAnnouncer();
  sut.Evaluate("HardFault", true, true);
  Assert.Null(sut.Evaluate("Degraded", true, true));
  Assert.Null(sut.Evaluate("HardFault", true, true));
}

[Fact]
public void AHealthyBootSaysNothing()
{
  // Handoff §7.4. No toast, no splash, no banner.
  var sut = new EncoderFaultAnnouncer();
  Assert.Null(sut.Evaluate("Transient", true, true));
  Assert.Null(sut.Evaluate("Transient", true, true));
  Assert.Null(sut.Evaluate("Configured", true, true));
}

[Fact]
public void AbsentAtBoot_GetsNoToast_ButAbsentMidSessionDoes()
{
  // The asymmetry ENC-0 added WasEverConnected for, finally consumed.
  Assert.Null(new EncoderFaultAnnouncer().Evaluate("Unknown", isConnected: false, wasEverConnected: false));
  Assert.NotNull(new EncoderFaultAnnouncer().Evaluate("Unknown", isConnected: false, wasEverConnected: true));
}

[Fact]
public void RecoveryIsAnnouncedOnlyForAnAbsenceWeAnnounced()
{
  // Handoff §7.3: "announce a recovery only for a fault you announced."
  Assert.Null(new EncoderFaultAnnouncer().Evaluate("Configured", isConnected: true, wasEverConnected: true));

  var sut = new EncoderFaultAnnouncer();
  sut.Evaluate("Unknown", isConnected: false, wasEverConnected: true);
  Assert.NotNull(sut.Evaluate("Configured", isConnected: true, wasEverConnected: true));
}

[Fact]
public void AFlappingUsbLead_AnnouncesAtMostOnceEachWay()
{
  // Plan §0.4 C-3 — a deliberate narrowing of handoff §7.3, because a lead that bounces inside
  // furniture would otherwise produce exactly the storm §7.6 forbids.
  var sut = new EncoderFaultAnnouncer();
  int announcements = 0;
  for (int i = 0; i < 10; i++)
  {
    if (sut.Evaluate("Unknown", false, true) is not null) announcements++;
    if (sut.Evaluate("Configured", true, true) is not null) announcements++;
  }

  Assert.Equal(2, announcements);
}

[Fact]
public void TheLatchIsPerInstance_SoASecondCircuitIsToldToo()
{
  // Why the service is scoped rather than singleton (§2.2). A reload must not be permanently silent
  // about a fault that is still present.
  var first = new EncoderFaultAnnouncer();
  Assert.NotNull(first.Evaluate("Degraded", true, true));
  Assert.NotNull(new EncoderFaultAnnouncer().Evaluate("Degraded", true, true));
}
```

**`tests/Radio.Infrastructure.Tests`** — the three `ConfigStatusChanged` tests from Task 1, plus:

```csharp
[Fact]
public void DisconnectResetsTheTier_SoAnAbsentDeviceIsNeverReportedConfigured()
{
  // The app cannot know what an unplugged device is running, and the same value drives the volume
  // clamp — VolumeClampFor(Unknown) is the tight one, which is the correct direction.
  Assert.Equal(RotaryEncoderConfigDefaults.VolumeClampUnverified,
    RotaryEncoderConfigVerifier.VolumeClampFor(RotaryEncoderConfigStatus.Unknown));
}
```

⚠ **`MainLayout` itself is not bUnit-testable** (§2.5) — the badge markup is covered by the browser
Test Plan below, and by nothing else. That is the existing state of affairs for `.topbar-mute-chip`
and `.phone-nav-fault` too; this row does not fix it and does not pretend to.

---

#### Task 11 — Docs

- **`design/INTEGRATIONS.md`** — in the rotary-encoder section, record what the owner sees when
  something is wrong: the Settings pill badge, its three states, and the fact that the notification
  fires **once per browser session on escalation and never repeats** (so someone who dismissed it and
  wants it back reloads the page). Also state plainly that a hard fault **limits volume movement**
  until it is fixed — that is the most surprising shipped behaviour in the whole encoder arc and it is
  currently documented nowhere the owner would look.
- **`design/FUTURE-WORK.md`** — two entries: `SystemConfigPage` has no tab deep-linking, so the toast
  lands one tap short of the card (Task 8a); and `MainLayout` has no bUnit coverage at all, which is
  now load-bearing for three separate topbar indicators (`.topbar-mute-chip`, `.phone-nav-fault`,
  `.encoder-nav-fault`).
- **`design/WORK-LOG.md`** — one entry, in the file's existing form.
- **`docs/HANDOFF-GA-PUNCH-LIST.md`** — mark `ENC-12` shipped with the PR number, and correct the
  row's `Retry now` / `Restore designed defaults` wording (§0.4 C-1) so the next reader does not go
  looking for two buttons that do not exist.
- **`docs/BUILDER_QUEUE.md`** — flip the `ENC-12` row to ✅; refresh the banner.
- **`docs/HANDOFF-NEXT-SESSION.md`** — the "ENC-11 has no way to tell the owner anything" paragraph
  is **no longer true once this merges**. Rewrite it rather than deleting it: the safety response is
  now legible, and the record of why it needed to be is worth keeping.

---

## 4. Test Plan

### 4.1 Automated gates

```bash
dotnet build --configuration Release
dotnet test  --configuration Release --verbosity normal
dotnet test --filter "FullyQualifiedName~EncoderFault"
```

### 4.2 Deploy

```powershell
./deploy/Deploy-ToLinux.ps1
```

```bash
curl -s http://radio:5000/api/health/version
curl -s http://radio:5002/api/health/version
```

⚠ Read `Information` lines from the **file sink**, not `journalctl` — since `LOG-11` the journal
carries WARNING and above only, and this row's broadcast line is `Information`:

```bash
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); tail -100 $F'
```

### 4.3 Browser UAT — Tester drives these at 1920 × 720

**Forcing a fault without breaking the hardware.** The device rejects a whole config when
`min_value >= max_value` (`ENC-11a`), and validation is all-or-nothing. So a *temporary* local edit
to `RotaryEncoderConfigDefaults.Create()` — set TUNING's `t3` multiplier to a value the firmware will
reject, or set `MaxValue = 0` — produces a genuine `Degraded`. For a **hard fault**, temporarily flip
`Reverse = true` on one encoder in `Create()` **without** going through `ENC-8`'s override path: the
push then carries a value the comparison does not expect and `reverse` is a safety field.
⚠ **Revert both before merging** and re-run T2 to confirm the console is back to `Configured`.

| # | Steps | Expected |
|---|---|---|
| **T1** | Boot the console healthy, with the encoder connected. Watch the whole startup on `/`. | **Nothing.** No toast, no banner, no badge on the Settings pill. This is handoff §7.4 and it is the single most important check here. |
| **T2** | Open `/system` → Integrations → Rotary Encoders. | `Configured`, verified timestamp present. Still no badge. |
| **T3** | Force a Degraded fault (above), redeploy, and sit on **`/queue`** — not `/system`. | Within a few seconds: an **amber `warning` glyph** appears bottom-right on the Settings pill, **and one Radzen toast** — *"Knob settings couldn't be applied / The knobs still work, but they may feel wrong."* |
| **T4** | Click the toast body. | Navigates to `/system`. (It lands on the page, not the sub-tab — a known limit, recorded in FUTURE-WORK.) |
| **T5** | Navigate `/queue` → `/metrics` → `/devices` → `/history` → `/phone` → `/`. | The badge is present on **every** route. **No further toasts.** |
| **T6** | Re-apply the config from `ENC-8`'s Actions card so it recovers, then force the fault again. | The badge clears and returns. **Still no second toast** — §0.5's anti-storm rule, observed live. |
| **T7** | Reload the browser (F5) while the fault is still present. | The badge is **still there immediately** on first paint — it is seeded from the cache, not only from live events. A toast **may** appear again here, and that is correct: a reload is a new session. |
| **T8** | Force a **hard fault** instead, from a healthy state. | Badge turns **red** with an `error` glyph. Toast: *"Knob safety settings couldn't be applied / Volume is limited until this is fixed."* Then **turn the volume knob fast** — it should move noticeably less per detent than normal. That sluggishness is `ENC-11`'s clamp, and this row is the first time the console explains it. |
| **T9** | With a Degraded fault already announced, escalate it to a hard fault. | A **second** toast fires, and the badge goes amber → red. |
| **T10** | Unplug the encoder mid-session. | Badge shows the amber **`link_off`** glyph; one toast *"Knobs disconnected / Touch controls still work."* Touch volume still works. |
| **T11** | Plug it back in. | Toast *"Knobs connected."*; badge clears. Unplug and replug **five more times**: **no further toasts** (§0.4 C-3), though the badge tracks every transition. |
| **T12** | Reboot the console with the encoder **unplugged**. | Badge present from first paint. **No toast** — absent at boot is badge-only (§7.3). Plug it in: badge clears, **and no "Knobs connected" toast**, because no absence was announced. |
| **T13** | Set `RotaryEncoder:Enabled=false`, restart, unplug the encoder. | **Nothing at all** — no badge, no toast. The owner turned the knobs off deliberately. Restore the setting afterwards. |
| **T14** | With a fault live, inspect the Settings pill with the accessibility tree. | The pill's accessible **name** carries the fault in words (*"Settings — knob safety settings not applied, volume limited"*), and its `title` is still the plain *"Settings"*. |
| **T15** | Go to `/sleep` by navigating directly to the route (**not** the Sleep pill — see the note below) with a fault live. | No badge and no toast on the sleep screen. Return to `/` — the badge is there. |

> ⚠ **T15: reach `/sleep` by URL or by idling, not by the Sleep pill.** The pill calls
> `SetSleepAsync(true)` first, so `SleepService.IsSleeping` is true and any encoder input is consumed
> by the wake; the idle path (`idle-dimmer.js:73-81`) navigates without it. This is documented
> pre-`ENC-6` behaviour and testing through the pill produces a false failure.

### 4.4 The four highest-weighted checks

1. **T1** — a healthy boot is silent. If anything appears, the row has broken handoff §7.4 and made
   every future badge less trustworthy.
2. **T6** — a flapping fault produces one toast. This is named as a requirement in three separate
   documents.
3. **T8** — the hard-fault toast lands beside a genuinely sluggish volume knob. That pairing is the
   whole point of the row: the safety response becomes explainable at the moment it is felt.
4. **T12** — absent at boot is badge-only. It is the asymmetry `ENC-0` built `WasEverConnected` for
   and that nothing has consumed until now, so it has never once been exercised end to end.

---

## 5. Self-review

**Spec coverage — the three surfaces.** (1) Cross-route nav-pill badge, amber for Degraded, red for
hard fault → Tasks 5, 7, 9. (2) One notification, once per session on first transition, never on
retry churn → Tasks 6, 8; latch defined in §0.5 and proved by eight tests in Task 10. (3) The status
card's field-level table and its actions → **`ENC-8`, not this row** (§0.2), which is the shared-card
decision this pass exists to make.

**Placeholders:** none. Every code block is literal, including all eight latch tests.

**Type consistency:** the tier crosses the wire as a **string** (`EncoderConfigStatusDto.Status`),
matching `EncoderHudDto.Phase`; `EncoderFaultRules` treats every unrecognised value as
`EncoderFaultLevel.None`, so an unknown tier degrades to silence. `EncoderFaultLevel` is compared with
`<=`, so its declaration order is load-bearing and is documented as such.

**Load:** one SignalR event that fires only on a real tier change — a handful per connection. No
poll, no timer, no throttle needed.

**Assertions this row makes the code print, and where each is checked:**

| Claim on screen | Checked by |
|---|---|
| *"The knobs still work, but they may feel wrong."* | `Degraded` leaves the knobs live on host clamps and treats acceleration as absent — `RotaryEncoderConfigVerifier.Classify` + `VolumeClampFor` |
| *"Volume is limited until this is fixed."* | `VolumeClampFor(HardFault)` returns `VolumeClampUnverified` (2) instead of `VolumeClamp` (6), consumed at `RotaryEncoderActionRouter.cs:284` |
| *"Touch controls still work."* | true by construction — handoff §7.3, every knob function has a touch equivalent |
| *"The knobs are working again."* | fired only on a `false → true` presence transition after an announced absence |
| the badge's presence | `EncoderFaultRules.Level` — unit-tested across all five tiers plus an unrecognised one |
| the pill's accessible name | `EncoderFaultRules.NavPillAriaLabel` — unit-tested per state |

**Rebase surface.** `ENC-4` touches `MainLayout.razor` (mounts `<EncoderHud />` at `:254-259`; this
row edits the Settings pill at `:151-155`), `ApiModels.cs` tail, `AudioStateHubService.cs`,
`AudioStateUpdateService.cs`, and `AudioServiceExtensions.cs`. `ENC-8` touches `ApiModels.cs`,
`AudioStateHubService.cs` is untouched by it, and `HidRotaryEncoderService.cs` heavily — **Task 1 of
this row edits the same property `ENC-8` Task 7 adds fields around**, so `ENC-8` must merge first.

---

## 6. Things this plan deliberately does not do, with the reason

1. **Anything on the Settings page.** §0.2 — `ENC-8` owns it, including the `Sent` vs `Read back`
   table and every button the punch list attributes to this row.
2. **`Retry now` and `Restore designed defaults`.** §0.4 C-1 and `ENC-8` §0.4 C-2.
3. **Deep-linking the toast to the Integrations sub-tab.** Task 8a — `SystemConfigPage`'s
   `RadzenTabs` has no route parameter, and adding one is a change to a page `ENC-8` is already
   rewriting. Logged to `design/FUTURE-WORK.md`.
4. **Anything on `/sleep`.** §0.3 item 3 — `ENC-6`'s territory.
5. **bUnit coverage of `MainLayout`.** §2.5. Making that layout renderable in bUnit is a real piece of
   test infrastructure affecting three shipped indicators; it is logged, not smuggled into this row.
6. **A shared `NotificationService` wrapper.** This row adds the second `new NotificationMessage` in
   the tree. Two is not yet a pattern; recorded in `design/FUTURE-WORK.md` by `ENC-8`.
7. **Sound.** Nothing here makes a noise. A console that beeps at 2 a.m. because a USB lead moved is
   strictly worse than one that shows a badge.
