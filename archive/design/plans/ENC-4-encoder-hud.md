# PLAN — `ENC-4` · `EncoderHud`: every knob visible within 100 ms, on every route

**Row:** `ENC-4` (P0, Encoders workstream) — [`docs/HANDOFF-GA-PUNCH-LIST.md` §3.0](../../docs/HANDOFF-GA-PUNCH-LIST.md)
**Spec:** [`docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md`](../../docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md) (Rev 3) — **§6 in full**, plus §4.4, §5.1, §5.4, §6.9, §8.6, §12.2, §15
**Relationship to the handoff:** **follows**, with three declared deviations and one scope narrowing — all recorded in §0.3 below with the evidence that forced them.
**Depends on:** `ENC-1` ✅, `ENC-3` ✅ (#511) — both shipped. **Dependencies are met.**
**Blocks:** `ENC-5` (SOURCE overlay), `ENC-7` (PRESETS overlay), `ENC-12` (config-fault notification).
**Author:** Planner, 2026-09-02.
**Effort:** 3–4 days · **17 tasks** across 6 phases.

---

> ## ⚠ SUPERSEDED IN ONE RESPECT — the HUD geometry. Read this before trusting any coordinate below.
>
> **This plan was written against handoff Rev 3, which read the knobs as a horizontal row beneath the
> screen.** The owner's as-built drawing (`design/hardware/front-panel-layout_4.svg`) puts them in a
> **vertical column to the LEFT of the LCD**, so every geometry instruction in this document is on the
> wrong axis: the quarter centres **240 / 720 / 1200 / 1680**, the `QuarterCentre(i) => 240 + (i * 480)`
> recipe, `bottom: 24px`, `margin-left: -180px`, the `Geometry_PlacesEachEncoderInItsOwnQuarter` test and
> UAT steps **A1–A5**.
>
> **Corrected in handoff Rev 4 §6.2 and closed in Rev 5; shipped as `ENC-4c`.** Cards anchor to
> `left: 24px` on bands **90 / 270 / 450 / 630** down the 720 px axis, vertically centred on the band and
> clamped ≥ 8 px inside the viewport, entering on §6.1's declared mirrored keyframe pair. The four bands
> now have **one definition**, `Radio.Core.Configuration.FrontPanelGeometry`.
>
> **Also superseded: §2.5's phase contract.** Rev 5 §6.10 makes an unrecognised phase *not holding* — it
> renders nothing either way, and a true `IsHolding` on one only suspends the dismissal timer and strands
> the card — with `Value` given its own explicit arm so a turn mid-hold still preserves the ring.
>
> **Everything else in this plan shipped as written and still describes the component**: the two hosts,
> the payload, the long-press synthesis, the timings, the throttling, `ENC-4b`, and the deliberate
> non-remap of the router's index→handler table.

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

Two of the four knobs currently produce **no visible evidence that anything happened**, and one of
them changes the machine's entire behaviour from an invisible internal counter. This row builds
`EncoderHud.razor` — one component, two hosts — that renders a transient card **in the screen
quarter above the knob that produced the event**. It also builds the host-side long-press synthesis
(600 ms) that the protocol does not provide, and closes `ENC-4b`: turning the volume knob while
muted unmutes.

**The reason a knob that acts silently is worse than a knob that does nothing:** the user's response
to silence is to turn it further. On the volume knob, that is the only genuine safety hazard this
machine has.

### 0.2 Four things Builder must NOT do

1. ⛔ **Do NOT remap `RotaryEncoderActionRouter`'s encoder-index → handler table.**
   The router maps `0=Volume · 1=Tuning · 2=Source · 3=Visualization`. The handoff's physical order
   is `VOLUME · SOURCE · PRESETS · TUNING`. **This mismatch is deliberate and documented**
   ([`docs/HANDOFF-NEXT-SESSION.md`](../../docs/HANDOFF-NEXT-SESSION.md), "Known mismatch,
   deliberate"). Index 0 is VOLUME in both, so the dangerous knob is already correct. **The remap
   belongs with `ENC-5` and `ENC-7`**, which introduce the SOURCE and PRESETS handlers the remap
   would point at. Remapping here would leave encoder 2 driving a PRESETS handler that does not
   exist. Task 5 adds a test that *pins* the current mapping so a later change has to be deliberate.

2. ⛔ **Do NOT add any new design token.** Handoff §6.9 is explicit: no `--hud-*` anything. Every
   value resolves to an existing token or to a per-component literal that matches an existing
   component (the project has no `--radius-*` / `--shadow-*` tokens; radii are literals). The token
   inventory Builder may draw on is listed in §2.4.

3. ⛔ **Do NOT build the SOURCE or PRESETS overlays.** Handoff §6.6 States A–F, the 4 s idle
   dismiss, the dimmed-with-a-reason rows, the in-flight spinner and the failure card are `ENC-5`
   and `ENC-7`. This row builds the **host** those overlays mount into (§2.5, the seam) and a
   minimal *value* card for the selector knobs — the same minimal form handoff §8.3 already
   specifies for a consumed input (`SOURCE · FM`, not the full overlay).

4. ⛔ **Do NOT build the three-state wake model, blanking, or the wake latch.** Handoff §8.2/§8.3
   (Rule 1 / Rule 2), DPMS re-enablement and the "consume exactly one event" latch are `ENC-6` and
   `ENC-15`. This row leaves `TryWakeFromSleep` exactly as it is. §0.4 explains why the Sleep host
   is still observable today despite that.

### 0.3 Deviations from the Designer handoff — declared, with evidence

Four places where the handoff's literal text does not survive contact with the tree. Each is
resolved here so Builder does not have to guess, and so Polisher does not flag the result as drift.

| # | Handoff says | Tree says | Resolution |
|---|---|---|---|
| **D-1** | §6.4: "reuse `.display-frequency` verbatim: `--font-display` **43px**/600" | `.display-frequency` is **42px**/600 (`design-system.css:717-733`; PR #371 hot-fix dropped 52→42 deliberately, with the letter-spacing scaled to match) | **Reuse the class verbatim, as instructed — do not override the size.** "Verbatim" is the load-bearing word; 43 was a transcription of the pre-hot-fix value. Builder applies `class="display-frequency"` and adds no `font-size`. |
| **D-2** | §6.2: the HUD label row is "the same treatment as `.sleep-screen-hint` **and `.rcp-presets-hint`**" — mono 11px, uppercase, `0.20em`, `--text-low` | `.sleep-screen-hint` matches exactly (`:2934-2943`). `.rcp-presets-hint` does **not** — it is 9px, `0.10em`, `--text-medium` (`:4246-4253`) | **Follow `.sleep-screen-hint`.** It is the class whose values the handoff actually quotes. `.rcp-presets-hint` is a different, denser context (an inline hint under a control) and is left alone. |
| **D-3** | §12.2: long-press synthesis should "reuse `RadioControlPanel.LongPressThresholdMs`" | It is a `private const int` inside `RadioControlPanel.razor:916`, in **`Radio.Web`**. The synthesis has to run in **`Radio.API`/`Radio.Infrastructure`**, a different process, because that is where the button edges arrive | **Promote the value, not the reference.** Task 1 puts `600` in `Radio.Core.Configuration.EncoderInteractionTimings` (both projects reference `Radio.Core`) and repoints `RadioControlPanel`'s const at it. One definition, honoured on both sides of the process boundary. |
| **D-4** | §8.6: the Ambient readout renders "inside `Sleep.razor`, within the anti-burn-in drift wrapper", **centered** — while §6.2 quarters the 1920 px width | Both are true, for different hosts | **Normal variant quarters; Sleep variant centers**, inside `.sleep-screen-drift`. The reason is not cosmetic: the drift wrapper is what stops a static composition burning into the panel over an overnight park, and a quartered card would sit outside it. Encoded in Task 12. |

### 0.4 One live finding that changes the Test Plan

**The Sleep-variant HUD is observable today, but only on the idle path — not via the Sleep pill.**

`idle-dimmer.js:73-81` navigates to `/sleep` **without** calling `SetSleepAsync(true)`; its own
comment says so ("Visual-only navigation — does NOT call SystemApi.SetSleepAsync because
idle-induced navigation must not pause playback"). So on the idle-reached `/sleep` route,
`SleepService.IsSleeping` is **false**, `RotaryEncoderActionRouter.TryWakeFromSleep` returns false,
and a knob turn **acts normally and publishes a HUD event** — which the Sleep host renders in place.

By contrast, `MainLayout.HandleSleepButtonAsync:1080-1087` calls `SetSleepAsync(true)` first, so on
the pill-reached `/sleep` route `IsSleeping` is **true**, any encoder input is consumed by
`TryWakeFromSleep`, and the browser navigates home before the card can be seen. That is correct
pre-`ENC-6` behaviour and this row does not change it.

> **Tester: reach `/sleep` by navigating to the route directly (or by idling), not by pressing the
> Sleep pill.** Verifying the Sleep variant against the pill path will produce a false failure.

---

## 1. Why the existing `VolumeChanged` broadcast cannot carry this

**This is the single most important architectural finding in the row, and it contradicts a plausible
first assumption.**

The obvious implementation is "the HUD listens to `VolumeChanged` and shows a card." It cannot meet
the requirement. `VolumeChanged` has exactly one call site —
`AudioStateUpdateService.CheckVolumeAsync:453-476` — inside a **500 ms change-detecting poller**.
That is 2 Hz. The acceptance criterion is **100 ms** (handoff §3 principle 2, §15 "Feedback"), and a
2 Hz poller misses it by up to 5×.

It also carries the wrong information: `VolumeDto` says *what the volume now is*, not *which knob
was turned*, and the HUD's whole trick is the second one.

**So this row adds a dedicated push path**: the router publishes a HUD event at the instant it acts,
it crosses the existing audio hub as `EncoderHudChanged`, and the Web renders it. The ≥50 ms
trailing-edge coalescing that handoff §6.8 requires is applied **on this new path only**.

> **⚠ Note for `ENC-3`'s record.** `docs/HANDOFF-NEXT-SESSION.md` states that the ENC-3 broadcast
> throttle "is already satisfied" because `VolumeChanged` is a 2 Hz poller, and instructs *"do not
> add a second throttle."* That remains correct **for `VolumeChanged`**. This row does not add a
> throttle to it. The coalescer added here belongs to a channel that did not exist when that note
> was written, and without it a fast spin at `PollIntervalMs = 10` would fan out at up to 100 Hz —
> exactly the hazard §6.8 describes. Builder must not "simplify" by deleting it.

---

## 2. Architecture

### 2.1 The shape, end to end

```
  Radio.API process                                    Radio.Web process
 ┌────────────────────────────────────┐               ┌──────────────────────────────────┐
 │ HidRotaryEncoderService            │               │ AudioStateHubService (singleton) │
 │   EncoderTurned / ButtonPressed    │               │   .On<EncoderHudDto>(            │
 │            │                       │               │      "EncoderHudChanged")        │
 │            ▼                       │               │            │                     │
 │ RotaryEncoderActionRouter          │               │            ▼                     │
 │   ├ EncoderLongPressGesture ───────┤               │ EncoderHudService (singleton)    │
 │   │   short ▸ today's press action │               │   Current / IsHolding            │
 │   │   long  ▸ enc 0 → Standby      │               │   1500 ms hold + re-arm          │
 │   └ IEncoderFeedbackSink.Publish() │               │   StateChanged                   │
 │            │                       │               │      │              │            │
 │            ▼                       │  SignalR      │      ▼              ▼            │
 │ EncoderFeedbackService             │  /hubs/audio  │ EncoderHud     EncoderHud        │
 │   ≥50 ms trailing-edge coalescer   │──────────────▶│  (MainLayout)  (Sleep, Variant=  │
 │            │                       │               │   quartered      Sleep, centered)│
 │            ▼                       │               └──────────────────────────────────┘
 │ AudioStateUpdateService            │
 │   Clients.All "EncoderHudChanged"  │
 └────────────────────────────────────┘
```

Every arrow follows a precedent already in the tree:

- `VisualizationModeService.ModeChanged` → `AudioStateUpdateService.OnVisualizationModeChanged:979`
  → `Clients.All.SendAsync` is the **exact** Infrastructure-event-to-hub shape used here.
- `EncoderConnectionChanged` (`:956-976`, from `ENC-0`) is the precedent for an encoder-specific
  broadcast with a typed Web-side DTO (`EncoderConnectionDto`, `ApiModels.cs:1315`).
- `GainPopoverService` is the precedent for a Web service that owns an overlay's visibility while
  `MainLayout` renders the element **outside `.page-transition`** — see §2.3.

### 2.2 Geometry — the whole trick

The four knobs sit left→right across the cabinet face, and encoder index equals physical position
by owner guarantee (D3, handoff §5.0). So the HUD divides the 1920 px viewport into quarters:

| Encoder index | Quarter centre | Physically, the knob engraved |
|---|---|---|
| 0 | **240 px** | VOLUME |
| 1 | **720 px** | SOURCE |
| 2 | **1200 px** | PRESETS |
| 3 | **1680 px** | TUNING |

**Geometry keys off the encoder index. Content keys off whatever the router's handler for that index
produced.** That separation is what makes this row survive the deliberate router mismatch (§0.2 item
1): today, turning the physical TUNING knob (index 3) lights a *visualizer* card at 1680 px — the
card is in the right place and says the wrong word, which is exactly the state the punch list
describes. When `ENC-5`/`ENC-7` remap the handlers, the content becomes right **with no change to
the HUD**.

Card is bottom-anchored at `bottom: 24px`, 360 px wide, horizontally centred on its quarter — i.e.
`left: <centre>px; transform: translateX(-50%)`.

### 2.3 Stacking

`MainLayout`'s `.page-transition` declares `transform` + `will-change`, which creates a sub-tree
stacking context that **traps any descendant's `z-index` regardless of magnitude**. This has already
cost this project one bug — the gain popover's click-away backdrop silently failed until it was
moved to the layout root (`GainPopoverService.cs` class comment, `MainLayout.razor:244-259`).

**The HUD mounts at the layout root, outside `.page-transition`, immediately after the gain
backdrop, at `z-index: 10000`** (handoff §6.1: "matching the gain-popover tier" — the backdrop is
9999, so the HUD sits one above it).

`pointer-events: none` on the card. It is a readout, not a control: nothing in this row is
clickable, and a 360 px transparent shield over the bottom of the screen would eat touches on the
UI beneath it. `ENC-5`/`ENC-7` re-enable pointer events on their own overlay element.

### 2.4 The token inventory — the complete list Builder may use

From handoff §6.9. Anything not on this list must be an existing class reused verbatim, or a literal
that matches an existing component (`10px` radius = `.nav-pill`, `12px` blur = `.surface-overlay`
family).

`--surface-overlay` · `--surface-separator` · `--surface-hover` · `--text-high` · `--text-medium` ·
`--text-low` · `--accent-primary` · `--signal-amber` · `--signal-amber-glow` · `--signal-red` ·
the five `--source-*` accents · `--font-led` · `--font-display` · `--font-mono` · `--sp-1`…`--sp-4` ·
`--touch-min` · `--anim-duration-*` · `--anim-ease-*`.

Animations come from the **already-present, currently-unconsumed** `.snackbar-enter` /
`.snackbar-exit` classes (`design-system.css:1218-1219`) over `@keyframes snackbarSlideIn` /
`snackbarSlideOut` (`:1029-1038`). **Do not write new keyframes for enter/exit.**

### 2.5 The seam `ENC-5` and `ENC-7` will mount into

Those rows must not have to reshape this component. The seam is three things, and Builder should
treat them as contract:

1. **`EncoderHudService` is the single entry point for HUD state**, and its `Publish(EncoderHudDto)`
   is `public`. `ENC-5`/`ENC-7` push overlay state through the same method with a different
   `Phase`; they do not add a second service or a second mount point.
2. **`EncoderHudDto.Phase` is an open string, not a closed enum, on the Web side.** `EncoderHud`
   renders a known phase and renders **nothing** for an unknown one. A future phase from a newer API
   build therefore degrades to silence rather than to an exception on a kiosk nobody is watching.
   (The API side keeps a real enum — see Task 2.)
3. **`EncoderHud.razor` dispatches on `Phase` to a render fragment.** Adding the overlay is adding a
   branch and a fragment, not restructuring the host, the geometry, the timers or the mounts.

Explicitly **not** built here, so those rows own them: list models, preview index, the current
marker, `SetBandAsync` band-vs-source commit, State C/D/E, the 4000 ms selector idle dismiss.

### 2.6 Long-press synthesis — where it lives and why

**The protocol has no long-press gesture** (handoff §4.4: "Host-side synthesis. The protocol reports
raw press/release only"). `HidRotaryEncoderService.ParseReport` (`:485-517`) already delivers **both**
edges — `EncoderButtonEventArgs.IsPressed` is `true` on press and `false` on release, and
`RotaryEncoderDecoder.ButtonChanges[]` is a `bool?[]` that is null when nothing changed. Everything
needed is on the wire; nothing is needed from the device.

`RotaryEncoderActionRouter.OnButtonPressed` currently discards the release edge outright
(`if (!e.IsPressed) return;`) and fires the action on **press**. Two changes follow, and the second
is a genuine behaviour change Builder must not miss:

- **The short action moves from press to release** (handoff §4.4: "Fires on *release*, before the
  long-press threshold"). It has to: firing mute on press would fire it on the way into every
  standby hold.
- **The long action fires AT the threshold while still held**, not on release, and the subsequent
  release then does nothing. That is what makes the ring meaningful — you see it complete and the
  thing happens, rather than the thing happening when you let go.

**Two consumers only** (handoff §12.2): volume→standby and PRESETS→save. In this row **only
volume→standby can be wired** — PRESETS→save is `ENC-7`'s action and encoder 2 is still the SOURCE
handler. Task 7 registers a long-press consumer for index 0 alone; indices 1–3 have none, so their
buttons behave exactly as before except for moving press→release.

### 2.7 The progress ring is animated on the client, not streamed

The ring starts at 300 ms and completes at 600 ms. The server sends **two** discrete events —
`hold-start` when the button goes down and one of `hold-cancel` / `hold-commit` when it resolves —
and the browser runs a CSS animation with `animation-delay: 300ms; animation-duration: 300ms`.

Streaming ring progress would put a render on the circuit every frame while a finger is held down,
on an Intel N100 where incidental load correlates with audible distortion. Two events is the whole
traffic.

---

## 3. Tasks

> **Convention reminders for every task:** 2-space indent · file-scoped namespaces · nullable
> enabled · **warnings-as-errors in Release** · MudBlazor/Radzen as already used in the file ·
> bUnit tests need `JSInterop.Mode = JSRuntimeMode.Loose` · comment internal logic, edge cases and
> protocol details.
>
> **⚠ The pre-merge review rule this repo enforces hardest** (`CLAUDE.md` § Pre-Merge Review): a
> comment, log message or XML doc must assert **only what the code actually does**. This repo has
> shipped three mismatches, two of which caused real bugs. When a comment offers a *reason* a thing
> is safe, the reason is the claim that gets checked. Write no comment here that says "always",
> "only", "never" or "guards every" unless the diff enforces it.

---

### Phase 0 — shared contracts

#### Task 1 — One definition of the interaction timings (`Radio.Core`)

**Why:** §0.3 D-3. The 600 ms threshold has to be honoured in two processes. `Radio.Web` and
`Radio.API` both reference `Radio.Core`, so it goes there.

**Create** `src/Radio.Core/Configuration/EncoderInteractionTimings.cs`:

```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// The interaction timings the encoder HUD and the host-side long-press synthesis share.
///
/// <para>
/// These live in Core because the two halves run in different processes: the synthesis is in
/// Radio.Infrastructure (Radio.API), the rendering is in Radio.Web. The handoff asks the synthesis
/// to reuse <c>RadioControlPanel.LongPressThresholdMs</c>, which is a private const in a Razor
/// component in the Web project — reachable as a value, not as a reference. This is that value,
/// with the component repointed at it so there is one definition rather than two that agree today.
/// </para>
/// </summary>
public static class EncoderInteractionTimings
{
  /// <summary>
  /// How long a button must be held before the long action fires, in milliseconds.
  ///
  /// <para>
  /// The long action fires <b>at</b> this threshold while the button is still held, and the
  /// subsequent release does nothing. Releasing before it fires the short action instead.
  /// </para>
  /// </summary>
  public const int LongPressThresholdMs = 600;

  /// <summary>
  /// When the progress ring starts drawing, in milliseconds after the press.
  /// The first 300 ms is indistinguishable from a click, so drawing earlier would put a ring on
  /// screen for every ordinary press.
  /// </summary>
  public const int LongPressRingStartMs = 300;

  /// <summary>
  /// How long a HUD card stays up after the last input, in milliseconds. Long enough to read a
  /// two-digit number after the hand stops; short enough not to camp on the visualizer.
  /// </summary>
  public const int HudHoldMs = 1500;

  /// <summary>
  /// Minimum interval between coalesced HUD broadcasts, in milliseconds (20 Hz).
  ///
  /// <para>
  /// Trailing-edge, always emitting the final value. The audio action itself is not throttled —
  /// volume applies per event at full rate; only the broadcast and render are coalesced.
  /// </para>
  /// </summary>
  public const int HudCoalesceMs = 50;
}
```

**Edit** `src/Radio.Web/Components/Shared/RadioControlPanel.razor` — replace the literal at line 916:

```csharp
  private const int LongPressThresholdMs = Radio.Core.Configuration.EncoderInteractionTimings.LongPressThresholdMs;
```

Leave the surrounding comment block (`:912-915`) as it is; it describes the band-pill behaviour and
is still accurate.

**Verify:** `dotnet build --configuration Release` is clean, and `RadioControlPanelTests` still pass
(they exercise the 600 ms band-pill and preset long-presses).

---

#### Task 2 — The feedback contract (`Radio.Core`)

**Why:** the router must be able to publish without knowing about SignalR, exactly as
`VisualizationModeService` does. Putting the sink interface in Core also lets the router be
constructed in a unit test with a recording fake.

**Create** `src/Radio.Core/Interfaces/Input/IEncoderFeedbackSink.cs`:

```csharp
namespace Radio.Core.Interfaces.Input;

/// <summary>
/// What a HUD card is showing, and why it appeared.
///
/// <para>
/// The Web side treats the wire value as an open string and renders nothing for a phase it does not
/// know, so a newer API build cannot throw on an older kiosk. This enum is the API-side source of
/// those names.
/// </para>
/// </summary>
public enum EncoderHudPhase
{
  /// <summary>A knob was turned (or a value otherwise changed) and the card shows the result.</summary>
  Value,

  /// <summary>A button went down. The client starts the progress ring at 300 ms.</summary>
  HoldStart,

  /// <summary>The button was released before the threshold. The ring collapses; the short action fired.</summary>
  HoldCancel,

  /// <summary>The hold reached the threshold and the long action fired while still held.</summary>
  HoldCommit,
}

/// <summary>
/// Event args for one HUD card update.
/// </summary>
/// <remarks>
/// <see cref="EncoderIndex"/> decides <b>where</b> the card renders — the HUD divides the 1920 px
/// viewport into quarters and puts the card in this encoder's own quarter, so the readout appears
/// above the knob that produced it. The remaining fields decide <b>what</b> it says. The two are
/// deliberately independent: the router's index-to-handler mapping is still the pre-ENC-5 one, so
/// the card is in the right place before it says the right word, and it will say the right word
/// without the HUD changing.
/// </remarks>
public class EncoderHudEventArgs : EventArgs
{
  /// <summary>Encoder index (0-3). Selects the screen quarter.</summary>
  public int EncoderIndex { get; init; }

  /// <summary>Label row text, uppercased by CSS — e.g. "VOLUME", "TUNING", "SOURCE".</summary>
  public required string Label { get; init; }

  /// <summary>Why this update was published.</summary>
  public EncoderHudPhase Phase { get; init; } = EncoderHudPhase.Value;

  /// <summary>
  /// Volume as whole percentage points (0-100), or null when this card is not a volume card.
  /// Present so the card can render numerals and a fill bar without a second round trip.
  /// </summary>
  public int? VolumePercent { get; init; }

  /// <summary>True when the console is muted. Drives the muted variant of the volume card.</summary>
  public bool IsMuted { get; init; }

  /// <summary>Primary line — a frequency, a track title, a source or mode name.</summary>
  public string? PrimaryText { get; init; }

  /// <summary>Secondary line — band and step, artist and album, or null.</summary>
  public string? SecondaryText { get; init; }

  /// <summary>
  /// True when the primary line is a radio frequency and should be rendered with
  /// <c>.display-frequency</c>. A flag rather than a parsed value: the Web must not re-derive
  /// formatting the API already did.
  /// </summary>
  public bool PrimaryIsFrequency { get; init; }
}

/// <summary>
/// Where the router publishes on-screen feedback. Implemented in Radio.Infrastructure and consumed
/// by Radio.API, which is what turns it into a SignalR broadcast.
/// </summary>
public interface IEncoderFeedbackSink
{
  /// <summary>Publishes one HUD update. Never throws to the caller.</summary>
  void Publish(EncoderHudEventArgs update);

  /// <summary>Fired after coalescing, on the thread the coalescer's timer runs on.</summary>
  event EventHandler<EncoderHudEventArgs>? Feedback;
}
```

**Verify:** build clean.

---

### Phase 1 — Infrastructure: publish, coalesce, synthesise

#### Task 3 — `EncoderFeedbackService` and its coalescer

**Why:** handoff §6.8 — "coalesce encoder-driven state broadcasts to ≥ 50 ms (20 Hz), trailing-edge,
always emitting the final value so the resting state is never stale." Per §1 above, this is the
channel that needs it; `VolumeChanged` already has its own 2 Hz poller and must not get a second.

**Create** `src/Radio.Infrastructure/Platform/Input/EncoderFeedbackService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Coalesces HUD updates and re-publishes them to whoever is broadcasting.
///
/// <para>
/// <b>Why a coalescer and not a straight pass-through.</b> <c>PollIntervalMs</c> is 10, so a fast
/// spin can present up to 100 movements a second. Each one that reached SignalR would fan out to a
/// Blazor Server circuit and re-render a component tree, on an Intel N100 where incidental load
/// correlates with audible distortion — and it would only reproduce while somebody was touching the
/// radio, which is a miserable thing to diagnose. The audio action is <b>not</b> throttled: the
/// router applies volume per event at full rate before it publishes here. The ear leads; the screen
/// catches up.
/// </para>
///
/// <para>
/// <b>Only <see cref="EncoderHudPhase.Value"/> is coalesced.</b> The hold phases are discrete edges,
/// not samples of a moving value, and dropping one would strand a progress ring on screen. They
/// flush immediately and clear any pending value for that encoder.
/// </para>
/// </summary>
public sealed class EncoderFeedbackService : IEncoderFeedbackSink, IDisposable
{
  private readonly ILogger<EncoderFeedbackService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly object _gate = new();

  // One pending value + one timer per encoder. Per-encoder rather than global so a turn on one knob
  // can never swallow a turn on another - two hands on the cabinet is an ordinary case.
  private readonly EncoderHudEventArgs?[] _pending = new EncoderHudEventArgs?[EncoderCount];
  private readonly ITimer?[] _timers = new ITimer?[EncoderCount];
  private readonly long[] _lastEmittedTicks = new long[EncoderCount];
  private bool _disposed;

  private const int EncoderCount = 4;

  public EncoderFeedbackService(ILogger<EncoderFeedbackService> logger, TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public event EventHandler<EncoderHudEventArgs>? Feedback;

  public void Publish(EncoderHudEventArgs update)
  {
    ArgumentNullException.ThrowIfNull(update);

    if (update.EncoderIndex < 0 || update.EncoderIndex >= EncoderCount)
    {
      _logger.LogDebug("Dropping HUD update for out-of-range encoder {Index}", update.EncoderIndex);
      return;
    }

    EncoderHudEventArgs? emitNow = null;

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      int i = update.EncoderIndex;

      if (update.Phase != EncoderHudPhase.Value)
      {
        // Discrete edge. Cancel anything pending for this encoder and let it through unchanged.
        CancelTimerLocked(i);
        _pending[i] = null;
        _lastEmittedTicks[i] = _timeProvider.GetTimestamp();
        emitNow = update;
      }
      else
      {
        long now = _timeProvider.GetTimestamp();
        double sinceMs = _lastEmittedTicks[i] == 0
          ? double.MaxValue
          : _timeProvider.GetElapsedTime(_lastEmittedTicks[i], now).TotalMilliseconds;

        if (sinceMs >= EncoderInteractionTimings.HudCoalesceMs)
        {
          // Leading edge of a burst: emit at once, so the first detent is on screen inside 100 ms.
          CancelTimerLocked(i);
          _pending[i] = null;
          _lastEmittedTicks[i] = now;
          emitNow = update;
        }
        else
        {
          // Inside the window: replace the pending value and arm a trailing-edge flush. Replacing
          // rather than queuing is what makes the last value the one that lands.
          _pending[i] = update;
          if (_timers[i] is null)
          {
            int captured = i;
            _timers[i] = _timeProvider.CreateTimer(
              _ => Flush(captured),
              null,
              TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudCoalesceMs - sinceMs),
              Timeout.InfiniteTimeSpan);
          }
        }
      }
    }

    if (emitNow is not null)
    {
      Raise(emitNow);
    }
  }

  private void Flush(int index)
  {
    EncoderHudEventArgs? toEmit;

    lock (_gate)
    {
      CancelTimerLocked(index);
      toEmit = _pending[index];
      _pending[index] = null;
      if (toEmit is not null)
      {
        _lastEmittedTicks[index] = _timeProvider.GetTimestamp();
      }
    }

    if (toEmit is not null)
    {
      Raise(toEmit);
    }
  }

  private void Raise(EncoderHudEventArgs update)
  {
    try
    {
      Feedback?.Invoke(this, update);
    }
    catch (Exception ex)
    {
      // A HUD update is cosmetic. A subscriber that throws must not take the encoder input path
      // down with it - the knobs stay live either way.
      _logger.LogError(ex, "Encoder HUD subscriber threw for encoder {Index}", update.EncoderIndex);
    }
  }

  private void CancelTimerLocked(int index)
  {
    _timers[index]?.Dispose();
    _timers[index] = null;
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      for (int i = 0; i < EncoderCount; i++)
      {
        CancelTimerLocked(i);
        _pending[i] = null;
      }
    }
  }
}
```

**Create** `tests/Radio.Infrastructure.Tests/Platform/Input/EncoderFeedbackServiceTests.cs`. Use
`Microsoft.Extensions.Time.Testing.FakeTimeProvider` (already referenced,
`Radio.Infrastructure.Tests.csproj:14`) and `NullLogger<EncoderFeedbackService>.Instance`.

Cases, one `[Fact]` each:

1. `FirstValue_EmitsImmediately` — a single `Value` publish raises `Feedback` synchronously. *(This
   is the 100 ms requirement: the leading edge must not wait for the window.)*
2. `BurstWithinWindow_EmitsLeadingThenOnlyTheFinalValue` — publish 5 values 10 ms apart, advance
   past 50 ms; exactly 2 raises, and the second carries the **last** payload.
3. `BurstAcrossWindows_EmitsAtMostTwentyPerSecond` — publish 100 values 10 ms apart across 1 s;
   assert raise count `<= 21`.
4. `HoldPhases_AreNeverCoalesced` — a `HoldStart` published 5 ms after a `Value` raises immediately.
5. `HoldPhase_CancelsAPendingValueForThatEncoder` — pending value + `HoldCommit` then advance;
   the stale value never arrives.
6. `EncodersCoalesceIndependently` — a value on encoder 0 does not delay a value on encoder 3.
7. `SubscriberThrow_DoesNotPropagate` — a throwing handler is swallowed and later publishes still work.
8. `OutOfRangeIndex_IsDropped` — index `-1` and `4` raise nothing.
9. `AfterDispose_PublishIsInert`.

---

#### Task 4 — `EncoderLongPressGesture`

**Why:** §2.6. The protocol has no long-press; this is the synthesis. Extracted from the router so
it can be tested against a fake clock without an `IAudioManager`.

**Create** `src/Radio.Infrastructure/Platform/Input/EncoderLongPressGesture.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Turns raw press/release edges into short-press and long-press actions.
///
/// <para>
/// <b>The device has no long-press gesture.</b> It reports button state changes and nothing else, so
/// the threshold, the timer and the decision all live here.
/// </para>
///
/// <para>
/// Two rules give this its feel, and both are deliberate:
/// <list type="bullet">
/// <item>The <b>short</b> action fires on <b>release</b>, not on press. Firing on press would fire
/// it on the way into every hold.</item>
/// <item>The <b>long</b> action fires <b>at</b> the threshold while the button is still held, and
/// the release that follows does nothing. That is what lets the on-screen ring complete and the
/// action happen together, instead of the action waiting for a finger to lift.</item>
/// </list>
/// </para>
/// </summary>
public sealed class EncoderLongPressGesture : IDisposable
{
  private readonly ILogger _logger;
  private readonly TimeProvider _timeProvider;
  private readonly object _gate = new();
  private readonly PressState[] _state;
  private bool _disposed;

  private sealed class PressState
  {
    public bool IsDown;
    public bool LongFired;
    public ITimer? Timer;
  }

  /// <summary>Fired on release, when the hold did not reach the threshold.</summary>
  public event Action<int>? ShortPress;

  /// <summary>Fired at the threshold, while the button is still held.</summary>
  public event Action<int>? LongPress;

  /// <summary>Fired on press-down, so the HUD can start the progress ring.</summary>
  public event Action<int>? HoldStarted;

  /// <summary>Fired on an early release, so the HUD can collapse the ring.</summary>
  public event Action<int>? HoldCancelled;

  public EncoderLongPressGesture(int encoderCount, ILogger logger, TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _state = new PressState[encoderCount];
    for (int i = 0; i < encoderCount; i++)
    {
      _state[i] = new PressState();
    }
  }

  /// <summary>Feeds one button edge in. <paramref name="isPressed"/> false is a release.</summary>
  public void OnButtonEdge(int index, bool isPressed)
  {
    if (index < 0 || index >= _state.Length)
    {
      return;
    }

    bool raiseHoldStarted = false;
    bool raiseHoldCancelled = false;
    bool raiseShort = false;

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      PressState s = _state[index];

      if (isPressed)
      {
        // A second press edge without an intervening release should not stack a second timer. The
        // device is change-only, so this is not expected - it is cheap to make it harmless anyway.
        if (s.IsDown)
        {
          return;
        }

        s.IsDown = true;
        s.LongFired = false;
        s.Timer = _timeProvider.CreateTimer(
          _ => OnThreshold(index),
          null,
          TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs),
          Timeout.InfiniteTimeSpan);
        raiseHoldStarted = true;
      }
      else
      {
        if (!s.IsDown)
        {
          return;
        }

        s.IsDown = false;
        s.Timer?.Dispose();
        s.Timer = null;

        if (s.LongFired)
        {
          // The long action already fired at the threshold. The release is deliberately inert -
          // firing the short action here as well would mute the console every time you held for
          // standby.
          s.LongFired = false;
        }
        else
        {
          raiseHoldCancelled = true;
          raiseShort = true;
        }
      }
    }

    if (raiseHoldStarted) { Raise(HoldStarted, index, nameof(HoldStarted)); }
    if (raiseHoldCancelled) { Raise(HoldCancelled, index, nameof(HoldCancelled)); }
    if (raiseShort) { Raise(ShortPress, index, nameof(ShortPress)); }
  }

  private void OnThreshold(int index)
  {
    bool fire = false;

    lock (_gate)
    {
      PressState s = _state[index];
      s.Timer?.Dispose();
      s.Timer = null;

      if (s.IsDown && !s.LongFired)
      {
        s.LongFired = true;
        fire = true;
      }
    }

    if (fire)
    {
      Raise(LongPress, index, nameof(LongPress));
    }
  }

  private void Raise(Action<int>? handler, int index, string name)
  {
    try
    {
      handler?.Invoke(index);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder {Index} {Gesture} handler threw", index, name);
    }
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      foreach (PressState s in _state)
      {
        s.Timer?.Dispose();
        s.Timer = null;
        s.IsDown = false;
      }
    }
  }
}
```

**Create** `tests/Radio.Infrastructure.Tests/Platform/Input/EncoderLongPressGestureTests.cs` with
`FakeTimeProvider`:

1. `PressThenQuickRelease_FiresShortPressOnly` — down, advance 200 ms, up ⇒ `HoldStarted`,
   `HoldCancelled`, `ShortPress`; no `LongPress`.
2. `ShortPress_FiresOnReleaseNotOnPress` — after down and 200 ms with no release, `ShortPress` has
   **not** fired.
3. `HoldToThreshold_FiresLongPressWhileStillHeld` — down, advance 600 ms ⇒ `LongPress` fired with
   the button still down.
4. `ReleaseAfterLongPress_DoesNotAlsoFireShortPress` — the §2.6 rule; this is the test that stops
   standby-then-mute.
5. `ReleaseAtExactlyTheThreshold_PrefersTheLongAction` — advance exactly 600 ms then release.
6. `Repeat_HoldThenShort_BothBehaveCorrectly` — the second gesture is not poisoned by the first.
7. `EncodersAreIndependent` — holding 0 while tapping 2 produces one long and one short.
8. `DuplicatePressEdge_DoesNotStackTimers` — two down edges then 600 ms ⇒ exactly one `LongPress`.
9. `Dispose_CancelsAPendingHold` — dispose mid-hold, advance past 600 ms ⇒ no `LongPress`.

---

#### Task 5 — Router publishes a HUD event from every handler, and the mapping is pinned

**Why:** "every knob visible within 100 ms" needs every handler to say what it did. And §0.2 item 1
needs the current mapping to be a *decision on the record* rather than an accident.

**Edit** `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs`.

Constructor gains `IEncoderFeedbackSink hud` and `TimeProvider? timeProvider = null`; store both.
Replace the class XML doc's mapping sentence with one that states the mismatch rather than hiding it:

```csharp
/// <summary>
/// Maps rotary encoder events to audio actions.
///
/// <para>
/// <b>Index mapping: 0 = Volume, 1 = Tuning, 2 = Source, 3 = Visualization.</b> The cabinet's
/// physical order is VOLUME / SOURCE / PRESETS / TUNING, so indices 1-3 do not yet match the
/// engraving. That is deliberate and tracked: the remap lands with ENC-5 (the SOURCE overlay) and
/// ENC-7 (PRESETS), because those rows introduce the handlers the remap would point at. Index 0 is
/// VOLUME under both orders, so the knob with a safety hazard on it is already correct.
/// </para>
///
/// <para>
/// The HUD's geometry keys off the encoder index, not off this table, so a card already appears
/// above the knob that was turned. Remapping later changes what the card says, not where it is.
/// </para>
///
/// Uses Func&lt;IAudioManager&gt; for deferred resolution to break circular DI.
/// </summary>
```

Add a private helper and call it at the end of each handler:

```csharp
  /// <summary>
  /// Publishes what this handler just did, so the HUD can put it above the knob that produced it.
  /// </summary>
  private void PublishHud(int index, string label, Action<HudBuilder> configure)
  {
    var b = new HudBuilder();
    configure(b);
    _hud.Publish(new EncoderHudEventArgs
    {
      EncoderIndex = index,
      Label = label,
      Phase = EncoderHudPhase.Value,
      VolumePercent = b.VolumePercent,
      IsMuted = b.IsMuted,
      PrimaryText = b.PrimaryText,
      SecondaryText = b.SecondaryText,
      PrimaryIsFrequency = b.PrimaryIsFrequency,
    });
  }

  private sealed class HudBuilder
  {
    public int? VolumePercent;
    public bool IsMuted;
    public string? PrimaryText;
    public string? SecondaryText;
    public bool PrimaryIsFrequency;
  }
```

Per-handler additions (append; do not change the existing action logic except where Task 6 says so):

- `HandleVolumeTurn` — after `mgr.MasterVolume = newVolume;`:
  ```csharp
    PublishHud(0, "VOLUME", b =>
    {
      b.VolumePercent = (int)Math.Round(newVolume * 100f);
      b.IsMuted = mgr.IsMuted;
    });
  ```
- `HandleTuningTurn` — publish **after** the async step completes, from inside
  `StepRadioFrequencyAsync`'s `try` (so the card shows where the tuner actually landed, not where it
  was aimed), and publish the track card on the non-radio branch:
  ```csharp
    // Radio branch, at the end of StepRadioFrequencyAsync's try:
    PublishHud(1, "TUNING", b =>
    {
      b.PrimaryText = radio.CurrentFrequency.ToDisplayString();
      b.SecondaryText = radio.CurrentBand.ToString().ToUpperInvariant();
      b.PrimaryIsFrequency = true;
    });
  ```
  ⚠ The current `HandleTuningTurn` does nothing at all when `ActiveSource` is not `IRadioControl` —
  the knob is silent on every non-radio source. Leave the **action** alone (track skip is `ENC-5`'s
  neighbourhood, per handoff §4.4 TUNING) but add an `else` branch that publishes a `TRACK` card
  from `mgr.ActiveSource` so the knob is not invisible:
  ```csharp
    else
    {
      PublishHud(1, "TRACK", b =>
      {
        b.PrimaryText = mgr.ActiveSource?.Name;
        b.SecondaryText = "no track control on this source";
      });
    }
  ```
  **Write that secondary line to say exactly that.** It is true today and the pre-merge rule is
  strict about copy that claims more than the code does.
- `HandleSourceTurn` — after computing `sourceType`:
  ```csharp
    PublishHud(2, "SOURCE", b =>
    {
      b.PrimaryText = sourceType.ToString().ToUpperInvariant();
      b.SecondaryText = "press to switch";
    });
  ```
- `HandleVizTurn` — after `CycleMode`:
  ```csharp
    PublishHud(3, "VISUALIZER", b => b.PrimaryText = _vizModeService.CurrentMode.ToUpperInvariant());
  ```

**Create** `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs`:

```csharp
[Fact]
public void EncoderIndexZero_IsVolume_UnderBothTheOldAndTheNewPhysicalOrder()
{
  // The one index the deliberate ENC-5/ENC-7 mismatch does not touch, and the only one with a
  // safety hazard behind it.
  Assert.Equal(0, RotaryEncoderConfigDefaults.VolumeEncoderIndex);
}
```

plus a test that constructs the router with a fake `IRotaryEncoderService`, a fake `IAudioManager`
and a recording `IEncoderFeedbackSink`, raises `EncoderTurned` on index 0, and asserts one
`EncoderHudEventArgs` with `EncoderIndex == 0`, `Label == "VOLUME"` and a non-null `VolumePercent`.
Name it `VolumeTurn_PublishesAHudCardForItsOwnQuarter`.

> **Note for Builder:** the router has no unit tests today, so this task also introduces the fakes.
> Keep them minimal — `IAudioManager` needs `MasterVolume`, `IsMuted` and `ActiveSource` only.
> If `IAudioManager` proves impractical to fake by hand, use Moq (already referenced).

---

#### Task 6 — `ENC-4b`: turning the volume knob while muted unmutes

**Why:** handoff §4.4 calls this "the most important small rule in this document." Today, turning
volume while muted moves a number nobody can hear: the user sees nothing change audibly and concludes
the radio is broken. Every car radio built in the last thirty years unmutes on a volume turn.

**Edit** `HandleVolumeTurn`, immediately before the volume is applied:

```csharp
    // ENC-4b. The first detent clears mute and applies the delta in the same frame.
    //
    // Without this the knob moves a value nobody can hear, and the user's response to that silence
    // is to turn it further - which is the input pattern the host clamp above exists to survive.
    // Unmuting first also means the delta lands on an audible volume rather than on a number that
    // will be revealed at some later, surprising moment.
    if (mgr.IsMuted)
    {
      mgr.IsMuted = false;
      _logger.LogInformation("Unmuted by a volume knob turn");
    }
```

The `PublishHud` call from Task 5 already reads `mgr.IsMuted` **after** this, so the card shows the
unmuted state — which is the point.

**Test** (`RotaryEncoderRouterMappingTests.cs`, or a sibling file):

1. `VolumeTurnWhileMuted_Unmutes` — fake manager starts `IsMuted = true`; raise a turn; assert
   `IsMuted == false`.
2. `VolumeTurnWhileMuted_AlsoAppliesTheDelta` — assert `MasterVolume` moved. *(The bug this guards
   is an "unmute-only, ignore the delta" implementation, which would need a second detent.)*
3. `VolumeTurnWhileMuted_PublishesAnUnmutedCard` — the emitted `EncoderHudEventArgs.IsMuted` is false.
4. `VolumeTurnWhileNotMuted_DoesNotTouchMute` — no spurious write.

---

#### Task 7 — Router: press/release edges into the gesture, long-press → Standby

**Why:** §2.6.

**Edit** the router:

- Field: `private readonly EncoderLongPressGesture _gesture;` built in the constructor with
  `new EncoderLongPressGesture(4, logger, timeProvider)`.
- Wire in the constructor:
  ```csharp
    _gesture.ShortPress += OnShortPress;
    _gesture.LongPress += OnLongPress;
    _gesture.HoldStarted += i => PublishHold(i, EncoderHudPhase.HoldStart);
    _gesture.HoldCancelled += i => PublishHold(i, EncoderHudPhase.HoldCancel);
  ```
- Replace `OnButtonPressed`'s body:
  ```csharp
  private void OnButtonPressed(object? sender, EncoderButtonEventArgs e)
  {
    try
    {
      // Both edges matter now. The short action fires on release and the long action fires at the
      // threshold while still held, so this handler routes the edge and decides nothing itself.
      //
      // The sleep-wake consumption stays on the PRESS edge: waking is what the input is spent on,
      // and letting the release through would fire a short action into a UI that has just changed
      // underneath the user.
      if (e.IsPressed && TryWakeFromSleep("encoder-button"))
      {
        return;
      }

      _gesture.OnButtonEdge(e.EncoderIndex, e.IsPressed);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} button edge", e.EncoderIndex);
    }
  }
  ```
  ⚠ **Consequence Builder must handle:** if the press edge is consumed by a wake, the release edge
  still arrives and `_gesture` never saw the press, so `OnButtonEdge(index, false)` returns early on
  `!s.IsDown`. That is correct and is why the guard in Task 4 exists. Add a one-line comment saying
  so at the `if (!s.IsDown) { return; }` site — it looks like defensive noise otherwise, and this is
  its real caller.
- New dispatchers:
  ```csharp
  private void OnShortPress(int index)
  {
    try
    {
      switch (index)
      {
        case 0: HandleVolumePress(); break;
        case 1: HandleTuningPress(); break;
        case 2: HandleSourcePress(); break;
        case 3: HandleVizPress(); break;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} short press", index);
    }
  }

  private void OnLongPress(int index)
  {
    // Two long-press consumers exist in the spec: VOLUME -> Standby and PRESETS -> Save. Only the
    // first is wired here. PRESETS is ENC-7's action, and encoder 2 still drives the source handler
    // under the pre-ENC-5 index mapping - registering a save on it now would put a preset write
    // behind a knob the cabinet does not label PRESETS yet.
    if (index != 0)
    {
      return;
    }

    if (_sleepService is null)
    {
      _logger.LogDebug("Volume long-press ignored: no sleep service is registered");
      return;
    }

    _ = EnterStandbyAsync();
  }

  private async Task EnterStandbyAsync()
  {
    try
    {
      await _sleepService!.EnterSleepAsync();
      PublishHold(0, EncoderHudPhase.HoldCommit);
      _logger.LogInformation("Standby entered by a volume knob long-press");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error entering standby from the volume knob");
    }
  }

  private void PublishHold(int index, EncoderHudPhase phase)
  {
    _hud.Publish(new EncoderHudEventArgs
    {
      EncoderIndex = index,
      // The label is what the card reads while the ring draws. Only the volume knob has a long
      // action wired today, so only it has a hold label to state.
      Label = index == 0 && phase == EncoderHudPhase.HoldStart ? "HOLD FOR STANDBY" : "VOLUME",
      Phase = phase,
      VolumePercent = (int)Math.Round(_audioManagerFactory().MasterVolume * 100f),
      IsMuted = _audioManagerFactory().IsMuted,
    });
  }
  ```

  ⚠ **Do not publish `HoldStart` for indices 1–3.** They have no long action, so a ring that fills
  and then does nothing would be a promise the code does not keep. Guard `PublishHold` on
  `index == 0` at its call sites, or return early inside it — Builder's choice, but the behaviour is
  not optional. Add the guard's *reason* to the comment, not just the guard.

- `Dispose`: `_gesture.Dispose();` before the event unsubscriptions.

**Tests** (extend the router test file):

1. `VolumeShortPress_TogglesMute` — down then up at 200 ms.
2. `VolumeLongPress_EntersStandby` — down, advance 600 ms; fake `ISleepService.EnterSleepAsync`
   called once.
3. `VolumeLongPress_ThenRelease_DoesNotAlsoToggleMute` — the sharpest of the four.
4. `SelectorLongPress_DoesNothing` — index 2 held 1 s ⇒ no source switch, no HUD `HoldStart`.
5. `HoldStart_IsPublishedForVolumeOnly`.

---

#### Task 8 — DI wiring

**Edit** `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`, inside
`AddRotaryEncoders` (`:397-423`):

```csharp
    // HUD feedback channel. Singleton because the coalescer is per-encoder state that must outlive
    // any single event, and because AudioStateUpdateService subscribes to it once for the process.
    services.AddSingleton<EncoderFeedbackService>();
    services.AddSingleton<IEncoderFeedbackSink>(sp => sp.GetRequiredService<EncoderFeedbackService>());
```

and extend the existing `RotaryEncoderActionRouter` factory with the two new constructor arguments:

```csharp
      sleepService: sp.GetService<ISleepService>(),
      hud: sp.GetRequiredService<IEncoderFeedbackSink>(),
      timeProvider: sp.GetService<TimeProvider>()));
```

> `TimeProvider` is resolved with `GetService` (not `GetRequiredService`) so production keeps
> `TimeProvider.System` via the constructor default and nothing has to be registered. This mirrors
> how `ISleepService` is already treated on this line.

**Verify:** `dotnet run --project src/Radio.API` starts without a DI resolution error. This is the
task most likely to break the container; run it before moving on.

---

### Phase 2 — API: broadcast

#### Task 9 — `AudioStateUpdateService` broadcasts `EncoderHudChanged`

**Why:** this is the only thing that gets the event out of the API process.

**Edit** `src/Radio.API/Services/AudioStateUpdateService.cs`, modelled exactly on
`OnEncoderConnectionChanged:956-976`:

- Field, beside `_encoderService` (`:32`):
  `private readonly IEncoderFeedbackSink? _encoderFeedback;`
- ⚠ **This class does not take its dependencies as constructor parameters** — it resolves them from
  an injected `IServiceProvider`. Follow the shape already at `:90`:
  ```csharp
    _encoderFeedback = serviceProvider.GetService<IEncoderFeedbackSink>();
  ```
  `GetService`, not `GetRequiredService`: the encoder subsystem may not be registered, which is
  exactly why `_encoderService` is nullable.
- At `:137-140`, where `ConnectionChanged` is subscribed, add the sibling block:
  ```csharp
    if (_encoderFeedback != null)
    {
      _encoderFeedback.Feedback += OnEncoderHudChanged;
    }
  ```
- Handler:
  ```csharp
  private async void OnEncoderHudChanged(object? sender, EncoderHudEventArgs e)
  {
    try
    {
      // Already coalesced to >= 50 ms by EncoderFeedbackService - this method does not throttle.
      await _hubContext.Clients.All.SendAsync("EncoderHudChanged", new
      {
        e.EncoderIndex,
        e.Label,
        Phase = e.Phase.ToString(),
        e.VolumePercent,
        e.IsMuted,
        e.PrimaryText,
        e.SecondaryText,
        e.PrimaryIsFrequency,
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting encoder HUD update");
    }
  }
  ```
  ⚠ **No `LogDebug` per broadcast here.** At 20 Hz during a spin that is 20 log lines a second on a
  box where log volume correlates with audible distortion (`CLAUDE.md`, and `LOG-11` exists for
  exactly this). `OnEncoderConnectionChanged` logs because it fires on plug events, not on movement.
  If Builder wants a diagnostic, gate it behind `LogTrace`.
- `Dispose` (`:948-951`): unsubscribe under the same null check, beside the `ConnectionChanged`
  removal.

**Verify:** `dotnet test --configuration Release` — `Radio.API.Tests` still green.

---

### Phase 3 — Web: transport and state

#### Task 10 — DTO + hub subscription

**Edit** `src/Radio.Web/Models/ApiModels.cs`, next to `EncoderConnectionDto` (`:1306-1320`):

```csharp
/// <summary>
/// Payload of the SignalR <c>EncoderHudChanged</c> broadcast (ENC-4).
///
/// <para>
/// <see cref="EncoderIndex"/> is the geometry: the HUD divides the 1920 px viewport into quarters
/// and renders this card in this encoder's quarter, so the readout appears above the knob that was
/// turned. Everything else is the content.
/// </para>
///
/// <para>
/// <see cref="Phase"/> is a string rather than an enum on purpose. A newer API build may send a
/// phase this kiosk does not know; an unknown phase renders nothing, which is the correct degraded
/// behaviour on a screen inside sealed furniture. Deserializing into a closed enum would throw.
/// </para>
/// </summary>
public class EncoderHudDto
{
  public int EncoderIndex { get; set; }
  public string Label { get; set; } = string.Empty;
  public string Phase { get; set; } = "Value";
  public int? VolumePercent { get; set; }
  public bool IsMuted { get; set; }
  public string? PrimaryText { get; set; }
  public string? SecondaryText { get; set; }
  public bool PrimaryIsFrequency { get; set; }
}
```

**Edit** `src/Radio.Web/Services/Hub/AudioStateHubService.cs`:

- Add to the class summary's event list (`:12-13`): `EncoderHudChanged`.
- Field: `public event Func<EncoderHudDto, Task>? EncoderHudChanged;` next to `EncoderConnectionChanged` (`:57`).
- Registration, modelled on `:220-230`:
  ```csharp
      _hubConnection.On<EncoderHudDto>("EncoderHudChanged", async (dto) =>
      {
        // No log line per message. This arrives at up to 20 Hz while a knob is moving.
        if (EncoderHudChanged != null && dto != null)
        {
          await EncoderHudChanged.Invoke(dto);
        }
      });
  ```

---

#### Task 11 — `EncoderHudService`

**Why:** the component must not own timers, or the card would die whenever a route change unmounted
it — and the sleep host and the main host would each run their own. One service, two renderers.

**Create** `src/Radio.Web/Services/EncoderHudService.cs`:

```csharp
using Radio.Core.Configuration;
using Radio.Web.Models;
using Radio.Web.Services.Hub;

namespace Radio.Web.Services;

/// <summary>
/// Owns what the encoder HUD is currently showing, and for how long.
///
/// <para>
/// <b>Singleton, not scoped</b> - unlike <see cref="GainPopoverService"/>, which is per-circuit
/// because it tracks a click the user made in that circuit. This tracks a physical knob on one
/// cabinet: there is exactly one, and both hosts (MainLayout and Sleep) must agree about it. It
/// also has to survive the route change between them.
/// </para>
///
/// <para>
/// The 1500 ms dismissal timer lives here rather than in CSS so that a new detent can re-arm it
/// without re-animating the card - continuous turning shows one stable card that stays up, which is
/// what the handoff's "re-arm" row asks for.
/// </para>
/// </summary>
public sealed class EncoderHudService : IDisposable
{
  private readonly TimeProvider _timeProvider;
  private readonly AudioStateHubService? _hub;
  private readonly object _gate = new();
  private ITimer? _dismissTimer;
  private bool _disposed;

  public EncoderHudService(AudioStateHubService? hub = null, TimeProvider? timeProvider = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
    _hub = hub;
    if (_hub is not null)
    {
      _hub.EncoderHudChanged += OnHubEvent;
    }
  }

  /// <summary>The card currently on screen, or null when nothing is showing.</summary>
  public EncoderHudDto? Current { get; private set; }

  /// <summary>
  /// True between a HoldStart and its HoldCancel / HoldCommit. Drives the progress ring.
  /// </summary>
  public bool IsHolding { get; private set; }

  /// <summary>Fired whenever <see cref="Current"/> or <see cref="IsHolding"/> changes.</summary>
  public event Action? StateChanged;

  /// <summary>
  /// Shows (or updates) the card. Public so tests and the future selector overlays (ENC-5, ENC-7)
  /// drive the HUD through one entry point rather than adding a second host.
  /// </summary>
  public void Publish(EncoderHudDto dto)
  {
    ArgumentNullException.ThrowIfNull(dto);

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      Current = dto;

      IsHolding = dto.Phase switch
      {
        "HoldStart" => true,
        "HoldCancel" or "HoldCommit" => false,
        _ => IsHolding,
      };

      // While a button is held the card must not time out from under the ring, so the dismissal
      // timer is armed only when nothing is being held.
      if (IsHolding)
      {
        CancelTimerLocked();
      }
      else
      {
        ArmDismissLocked();
      }
    }

    StateChanged?.Invoke();
  }

  /// <summary>Clears the card immediately. Used by ENC-0's disconnect teardown and by tests.</summary>
  public void Dismiss()
  {
    lock (_gate)
    {
      CancelTimerLocked();
      Current = null;
      IsHolding = false;
    }

    StateChanged?.Invoke();
  }

  private Task OnHubEvent(EncoderHudDto dto)
  {
    Publish(dto);
    return Task.CompletedTask;
  }

  private void ArmDismissLocked()
  {
    CancelTimerLocked();
    _dismissTimer = _timeProvider.CreateTimer(
      _ => Dismiss(),
      null,
      TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs),
      Timeout.InfiniteTimeSpan);
  }

  private void CancelTimerLocked()
  {
    _dismissTimer?.Dispose();
    _dismissTimer = null;
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      CancelTimerLocked();
    }

    if (_hub is not null)
    {
      _hub.EncoderHudChanged -= OnHubEvent;
    }
  }
}
```

**Edit** `src/Radio.Web/Program.cs`, next to the other singletons (`:402-436`):

```csharp
builder.Services.AddSingleton<Radio.Web.Services.EncoderHudService>();
```

⚠ **DI ordering:** it must be registered **after** `AudioStateHubService` (`:402`) so the constructor
injection resolves, and it must actually be **constructed** for the hub subscription to exist. A
singleton nobody injects is never built. `MainLayout` injects it (Task 14), and `MainLayout` renders
on every non-`/sleep` route — but the kiosk can boot straight onto `/sleep`. **So `Sleep.razor` must
inject it too** (Task 15), which it does anyway to render.

**Create** `tests/Radio.Web.Tests/Services/EncoderHudServiceTests.cs` with `FakeTimeProvider`:

1. `Publish_SetsCurrentAndRaisesStateChanged`.
2. `Card_DismissesAfterFifteenHundredMilliseconds`.
3. `NewValueBeforeTimeout_ReArmsWithoutClearing` — publish, advance 1400 ms, publish, advance
   1400 ms ⇒ still showing; advance a further 200 ms ⇒ cleared.
4. `HoldStart_SuspendsTheDismissalTimer` — hold, advance 5 s ⇒ still showing, `IsHolding` true.
5. `HoldCancel_ClearsIsHoldingAndReArmsTheTimer`.
6. `HoldCommit_ClearsIsHolding`.
7. `UnknownPhase_LeavesIsHoldingAlone` — the forward-compatibility rule from §2.5.
8. `Dismiss_ClearsImmediately`.
9. `AfterDispose_PublishIsInert`.

---

### Phase 4 — Web: the component

#### Task 12 — `EncoderHud.razor` + `EncoderHudVariant`

**Create** `src/Radio.Web/Components/Shared/EncoderHudVariant.cs` (sibling of the existing
`PresetCardVariant.cs`, same pattern):

```csharp
namespace Radio.Web.Components.Shared;

/// <summary>Which host the HUD is rendering into.</summary>
public enum EncoderHudVariant
{
  /// <summary>
  /// MainLayout, on every normal route. Quartered geometry: the card sits above the knob that
  /// produced it, bottom-anchored over the 1920 px viewport.
  /// </summary>
  Normal,

  /// <summary>
  /// Inside <c>Sleep.razor</c>'s anti-burn-in drift wrapper. Centered rather than quartered, and
  /// stripped to one emissive colour.
  ///
  /// <para>
  /// Centering is not a simplification. The drift wrapper is what stops a static composition
  /// burning into the panel over an overnight park, and a quartered card would have to sit outside
  /// it to reach its quarter - which is exactly the fixed-position bright element that wrapper
  /// exists to prevent.
  /// </para>
  /// </summary>
  Sleep,
}
```

**Create** `src/Radio.Web/Components/Shared/EncoderHud.razor`:

Structure (Builder writes the markup; these are the requirements it must satisfy):

- `@inject EncoderHudService Hud`, `@implements IDisposable`. Subscribe to `Hud.StateChanged` in
  `OnInitialized` with `InvokeAsync(StateHasChanged)`; unsubscribe in `Dispose`.
- `[Parameter] public EncoderHudVariant Variant { get; set; } = EncoderHudVariant.Normal;`
- Render nothing when `Hud.Current is null`.
- Render nothing for an unknown `Phase` (§2.5 item 2). Known: `Value`, `HoldStart`, `HoldCancel`,
  `HoldCommit`.
- **Geometry, Normal variant:** root element gets
  `style="left: @(QuarterCentre(Hud.Current.EncoderIndex))px;"` where
  `QuarterCentre(i) => 240 + (i * 480)`. Write it as that expression, not as a lookup table — the
  arithmetic *is* the spec (1920 / 4 = 480; first centre at half of that).
  Clamp the index to 0–3 defensively and add a `data-encoder-index` attribute for test selectors
  (`SourceBubble`'s `data-source` set the precedent).
- **Geometry, Sleep variant:** no `left`, no `bottom` — the drift wrapper positions it.
- **Cards.** One `@switch` on the shape, driven by the payload, not by the label:
  - `VolumePercent is not null` ⇒ the **volume card**: label row, numerals (`--font-led`, 64px/700,
    `--text-high`, `font-variant-numeric: tabular-nums`), and a 6 px fill bar
    (`--accent-primary` fill, `--surface-separator` track).
    - `IsMuted` ⇒ numerals to `--text-low`, the fill renders as an **unfilled `--signal-red`
      outline**, and a `MUTED` chip in `--signal-red` sits right of the label.
  - `PrimaryIsFrequency` ⇒ the **frequency card**: `PrimaryText` inside
    `<div class="display-frequency">` — **the class verbatim, no size override** (§0.3 D-1) —
    with `SecondaryText` on the label row's right.
  - otherwise ⇒ the **generic card**: `PrimaryText` at 20 px `--text-high` with `text-overflow:
    ellipsis`, `SecondaryText` at 14 px `--text-medium`.
- **The ring.** When `Hud.IsHolding`, wrap the numerals in a ring element that animates from 300 ms
  to 600 ms (§2.7 — CSS only, `animation-delay: 300ms; animation-duration: 300ms`), and render the
  label as `Hud.Current.Label` (which the server already set to `HOLD FOR STANDBY`).
- **Accessibility** (handoff §15): the card is a live region —
  `role="status" aria-live="polite" aria-atomic="true"` — and carries a text-only accessible summary
  so AT-SPI reports it. Every state must be distinguishable **without colour**: the muted state says
  the word `MUTED`, it is not only red.
- `.snackbar-enter` on mount. For exit, the service clears `Current` and the element unmounts;
  applying `.snackbar-exit` to an element being removed does not work in Blazor without a two-phase
  teardown — **do not add one.** Note this in a comment: the enter animation is the one that matters
  perceptually, and a 200 ms exit that sometimes does not run is not worth a state machine.

  ⚠ If Builder judges the exit animation to be required rather than nice, raise it rather than
  building a teardown state machine on their own initiative — it is a change to the service's
  contract, and `ENC-5` inherits it.

---

#### Task 13 — CSS

**Edit** `src/Radio.Web/wwwroot/css/design-system.css`. Append a new section at the end, in the
file's existing banner-comment style. **No new custom properties** (§2.4).

Required rules:

```css
/* ─── ENC-4  Encoder HUD ────────────────────────────────────────────────────
 *
 * A transient readout that appears in the screen quarter above the knob that
 * produced it. Enter/exit reuse the .snackbar-* primitives that have been in
 * this file, unconsumed, since the design-system pass - see :1218.
 *
 * No --hud-* custom properties, per the encoder handoff §6.9. The 10px radius
 * is a literal because this project has no --radius-* scale; it matches
 * .nav-pill, and the 12px blur matches the .surface-overlay family.
 * ─────────────────────────────────────────────────────────────────────────── */

.encoder-hud {
  position: fixed;
  bottom: 24px;
  transform: translateX(-50%);
  width: 360px;
  z-index: 10000;                 /* one tier above the gain-popover backdrop (9999) */
  /* A readout, not a control. Without this the card is a 360px transparent
     shield over the bottom of whatever route is underneath it. */
  pointer-events: none;
  background: color-mix(in srgb, var(--surface-overlay) 92%, transparent);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid var(--surface-separator);
  border-radius: 10px;
  padding: var(--sp-4);
}

/* Label row - the treatment .sleep-screen-hint already uses. */
.encoder-hud-label {
  font-family: var(--font-mono);
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.20em;
  color: var(--text-low);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.encoder-hud-value {
  font-family: var(--font-led);
  font-size: 64px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--text-high);
  line-height: 1;
}

.encoder-hud-bar { height: 6px; background: var(--surface-separator); border-radius: 3px; }
.encoder-hud-bar-fill { height: 100%; background: var(--accent-primary); border-radius: 3px; }

/* Muted variant. The word MUTED carries the state; the colour only reinforces
   it - handoff §10.5 requires every state to be readable without colour. */
.encoder-hud.is-muted .encoder-hud-value { color: var(--text-low); }
.encoder-hud.is-muted .encoder-hud-bar-fill { background: transparent; }
.encoder-hud.is-muted .encoder-hud-bar { border: 1px solid var(--signal-red); background: transparent; }
.encoder-hud-muted-chip { color: var(--signal-red); }

/* Progress ring: 300ms -> 600ms, matching the host-side long-press threshold.
   The first 300ms of a press is indistinguishable from a click, so the ring
   is delayed rather than started at zero. */
@keyframes encoderHudRing { from { --ring-turn: 0turn; } to { --ring-turn: 1turn; } }

.encoder-hud-ring {
  animation: encoderHudRing 300ms linear 300ms 1 forwards;
}

.encoder-hud--sleep {
  position: static;
  transform: none;
  width: auto;
  /* No card chrome. This replaces the clock composition inside the drift
     wrapper rather than floating above it, and that wrapper's one emissive
     colour is the load-bearing rule of the sleep-screen handoff. */
  background: none;
  backdrop-filter: none;
  -webkit-backdrop-filter: none;
  border: none;
  padding: 0;
  text-align: center;
}

/* Byte-identical to .sleep-screen-clock - the volume number IS the clock's
   typography, wearing a different label. */
.encoder-hud--sleep .encoder-hud-value {
  font-size: 96px;
  color: color-mix(in srgb, var(--signal-amber) 35%, #050507);
  text-shadow: 0 0 12px color-mix(in srgb, var(--signal-amber) 15%, transparent);
  letter-spacing: 0.02em;
}

.encoder-hud--sleep .encoder-hud-bar-fill {
  background: color-mix(in srgb, var(--signal-amber) 35%, #050507);
}

/* No cyan and no red on the sleep screen, not even for mute. */
.encoder-hud--sleep.is-muted .encoder-hud-bar { border-color: var(--text-low); }
.encoder-hud--sleep .encoder-hud-muted-chip { color: var(--text-low); }

@media (prefers-reduced-motion: reduce) {
  /* Enter/exit become instant; the ring becomes a filling bar rather than a
     rotating one. Matches RdsScrollMarquee and .sleep-screen-drift. */
  .encoder-hud.snackbar-enter,
  .encoder-hud.snackbar-exit { animation: none; }
  .encoder-hud-ring { animation-name: none; }
}
```

⚠ `@property`-free custom-property animation (`--ring-turn`) does not interpolate in every engine.
If Builder finds the ring does not animate in kiosk Chrome, implement it as a `conic-gradient` whose
angle is driven by a registered `@property --ring-turn { syntax: '<angle>'; ... }`, or fall back to
an SVG `stroke-dashoffset` animation. **Pick whichever renders; do not ship a ring that does not
move.** Verify on the box, not only in a desktop browser.

---

#### Task 14 — Mount in `MainLayout`

**Edit** `src/Radio.Web/Components/Layout/MainLayout.razor`.

- `@inject Radio.Web.Services.EncoderHudService EncoderHud` with the other injections (`:16`).
- Mount **immediately after** the gain-popover backdrop block (`:254-259`), still inside
  `.layout-container` and **outside `.page-transition`**:

```razor
  @* ENC-4 — encoder HUD host for every normal route.
     Mounted at the layout root for the same reason the gain backdrop is: .page-transition declares
     transform + will-change, which creates a stacking context that traps a descendant's z-index no
     matter how large it is. The HUD sits one tier above that backdrop (10000 vs 9999).
     The /sleep route is on EmptyLayout and is not in this tree, so it hosts its own copy. *@
  <EncoderHud />
```

- No `@code` changes. The component owns its own subscription; `MainLayout` does not relay.

---

#### Task 15 — Mount in `Sleep.razor`, and suppress the clock while it is up

**Edit** `src/Radio.Web/Components/Pages/Sleep.razor`.

- `@inject Radio.Web.Services.EncoderHudService EncoderHud`.
- Inside `.sleep-screen-drift` (`:64`), the composition becomes three-way. Today it is
  `forecast ? SleepForecastPane : clock`. It becomes:

```razor
  <div class="sleep-screen-drift" style="@_shiftStyle">
    @* ENC-4 / handoff §8.6 — while a knob readout is up it replaces the alternating
       clock/weather composition for its lifetime, and the composition is restored afterward.
       Rendering it alongside the clock would put two large emissive elements inside the same
       anti-burn-in wrapper. *@
    @if (EncoderHud.Current is not null)
    {
      <EncoderHud Variant="EncoderHudVariant.Sleep" />
    }
    else if (_isShowingForecast && _forecast is not null && _forecast.Days.Count > 0)
    {
      ...unchanged...
    }
    else
    {
      ...unchanged clock composition...
    }
  </div>
```

- Subscribe in `OnInitializedAsync`: `EncoderHud.StateChanged += OnEncoderHudChanged;` with
  `private void OnEncoderHudChanged() => InvokeAsync(StateHasChanged);`, and unsubscribe in
  `DisposeAsync` (`:415`).

  ⚠ **The page needs its own subscription even though the child component has one.** The child only
  re-renders itself; the *swap* between compositions is the parent's decision, so the parent has to
  learn about it too.

- **The hint line** (`:104`) is left as `tap anywhere to wake` in this row. Handoff §8.6's
  Standby-specific hint (`hold VOLUME or press any knob to turn on`) depends on distinguishing
  Ambient from Standby, which is `ENC-6`'s state model. Leave a `TODO(ENC-6)` comment saying exactly
  that, and no more.

---

### Phase 5 — component tests and docs

#### Task 16 — bUnit tests for `EncoderHud`

**Create** `tests/Radio.Web.Tests/Components/Shared/EncoderHudTests.cs`.

Rig: `using var ctx = new TestContext(); ctx.JSInterop.Mode = JSRuntimeMode.Loose;` plus
`ctx.Services.AddSingleton(new EncoderHudService(hub: null, timeProvider: fake));` — the `hub: null`
constructor path is why that parameter is optional.

> `MainLayoutTests.cs` is a documented stub (Radzen + JSInterop make the layout impractical to
> render). **Do not try to test the HUD through `MainLayout`.** Test the component directly; the
> mount points are covered by the browser Test Plan below.

Cases:

1. `NoCurrentCard_RendersNothing`.
2. `VolumeCard_RendersNumeralsAndFill` — `VolumePercent = 62` ⇒ markup contains `62` and a fill
   element whose width reflects 62%.
3. `MutedVolumeCard_SaysTheWordMuted` — the without-colour requirement, asserted on text.
4. `FrequencyCard_UsesDisplayFrequencyVerbatim` — the primary element carries class
   `display-frequency` and **no inline `font-size`** (§0.3 D-1 regression guard).
5. `Geometry_PlacesEachEncoderInItsOwnQuarter` — `[Theory]` over `(0,240) (1,720) (2,1200) (3,1680)`
   asserting the root's inline `left`.
6. `Geometry_ClampsAnOutOfRangeIndex`.
7. `SleepVariant_IsCenteredAndCarriesNoCardChrome` — root has `encoder-hud--sleep`, and **no**
   inline `left`.
8. `SleepVariant_UsesNoCyanAndNoRed` — assert the muted chip does not resolve to `--signal-red`
   (assert on the class, not the computed colour — bUnit does not compute styles).
9. `UnknownPhase_RendersNothing` — the §2.5 forward-compatibility contract.
10. `HoldingState_RendersTheRing` — `IsHolding` true ⇒ the ring element is present; false ⇒ absent.
11. `Card_IsAPoliteLiveRegion` — `role="status"`, `aria-live="polite"`.
12. `Card_DoesNotInterceptPointerEvents` — assert the `encoder-hud` class is present and that the
    CSS rule exists; if asserting CSS is impractical in bUnit, assert instead that the root renders
    no `@onclick` handler and record the `pointer-events` check as a browser-only item in the Test
    Plan. **Do not fake a passing assertion.**

---

#### Task 17 — Docs

Per the repo's per-PR docs rule:

- **`design/INTEGRATIONS.md`** — in the rotary-encoder section, add: the HUD exists and where it
  mounts (two hosts); the long-press threshold and where the constant lives; **and the fact that the
  short press now fires on release, not on press** (a behaviour change someone debugging at the
  cabinet will otherwise find surprising).
- **`design/WORK-LOG.md`** — one entry, in the file's existing form.
- **`docs/HANDOFF-GA-PUNCH-LIST.md`** — mark `ENC-4` shipped with the PR number, in the style
  `AUD-6`'s entry already uses. Record in one sentence that the router remap was **deliberately not
  done** and still belongs to `ENC-5`/`ENC-7`.
- **`docs/HANDOFF-NEXT-SESSION.md`** — update "Start here" to the next row, and keep the "Known
  mismatch, deliberate" section (it is still true).
- **`docs/BUILDER_QUEUE.md`** — flip the `ENC-4` row to ✅ with the PR link, and refresh the
  last-updated banner.
- **`design/FUTURE-WORK.md`** — record the two things this row deliberately left as seams: the
  PRESETS long-press consumer (`ENC-7`) and the exit animation (§Task 12).

---

## 4. Test Plan

### 4.1 Automated gates

```bash
dotnet build --configuration Release          # 0 warnings — warnings are errors in Release
dotnet test  --configuration Release          # ~1,697 existing + ~40 new
```

New tests, by project:

| Project | File | Count |
|---|---|---|
| `Radio.Infrastructure.Tests` | `EncoderFeedbackServiceTests.cs` | 9 |
| `Radio.Infrastructure.Tests` | `EncoderLongPressGestureTests.cs` | 9 |
| `Radio.Infrastructure.Tests` | `RotaryEncoderRouterMappingTests.cs` | ~10 |
| `Radio.Web.Tests` | `EncoderHudServiceTests.cs` | 9 |
| `Radio.Web.Tests` | `EncoderHudTests.cs` | 12 |

### 4.2 Deploy

```powershell
./deploy/Deploy-ToLinux.ps1
```

No flags — `OPS-1` fixed the defaults. The deploy verifies **both** services by SHA and will
`exit 1` on a mismatch, then reports kiosk liveness by established-connection count. A
`WARNING: 0 established connections to :5002` means the binaries landed and the browser did not come
back; recover with `ssh mmack@radio '/usr/local/bin/radio-kiosk-launch'`.

⚠ **`journalctl` carries WARNING and above only** since `LOG-11`. Information lines are in
`/opt/radio-console/logs/radio-*.txt`. And keep log reads bounded — heavy `journalctl` on this box
correlates with audio distortion, which would contaminate the load tests below.

### 4.3 Browser UAT — Tester drives these on the box at 1920×720

Prerequisite: encoder connected (`curl -s http://radio:5000/api/health/version` for the SHA; the
status card on System Config shows `Configured`).

**A · Geometry — the whole trick**

| # | Steps | Expected |
|---|---|---|
| A1 | On Home, turn the **leftmost** knob one detent | A card appears in the **far-left quarter** (centred ≈240 px), bottom-anchored, within 100 ms |
| A2 | Turn the **second** knob | A card appears in the **second** quarter (≈720 px) |
| A3 | Turn the **third** knob | A card in the **third** quarter (≈1200 px) |
| A4 | Turn the **rightmost** knob | A card in the **far-right** quarter (≈1680 px) |
| A5 | Screenshot each; measure the card's horizontal centre | Within ±20 px of 240 / 720 / 1200 / 1680 |

> **A2–A4 will show the WRONG WORDS, and that is the expected result of this row.** The router still
> maps `1=Tuning, 2=Source, 3=Visualizer` while the cabinet reads `SOURCE, PRESETS, TUNING`. What is
> being verified here is **placement**, which is what ENC-4 owns. Report a *content* mismatch on
> indices 1–3 as **expected**, not as a defect. If a card appears in the wrong **quarter**, that is a
> real failure.

**B · The 100 ms requirement, on every route**

| # | Steps | Expected |
|---|---|---|
| B1 | Repeat A1 on `/`, `/queue`, `/metrics`, `/devices`, `/history`, `/phone` | A card on every one of them |
| B2 | Record a video or a rapid screenshot burst of one detent | Visible change within 100 ms of the click |
| B3 | Turn continuously for 3 s, then stop | One stable card that does not re-animate per detent; it dismisses **1.5 s** after the last detent |

**C · `ENC-4b` — the most important small rule**

| # | Steps | Expected |
|---|---|---|
| C1 | Mute from the touchscreen. Confirm the topbar `MUTED` chip (`ENC-4a`, already shipped) | Chip visible |
| C2 | Turn the volume knob **one detent** | Audio comes back **on that detent**, the chip clears, and the card shows the new volume unmuted |
| C3 | Confirm the volume also moved | The delta was applied, not swallowed — a second detent must not be needed |
| C4 | Mute, then turn volume **down** one detent | Unmutes and goes down. The rule is not direction-specific |

**D · Long-press synthesis**

| # | Steps | Expected |
|---|---|---|
| D1 | Press and release the volume knob quickly (<300 ms) | Mute toggles **on release**; no ring appears |
| D2 | Press and hold the volume knob | At ~300 ms a ring begins drawing, label reads `HOLD FOR STANDBY`; it completes at ~600 ms |
| D3 | Release at ~450 ms | Ring collapses and **mute fires** — this is the early-release branch |
| D4 | Hold past 600 ms | **Standby is entered while still held**; audio pauses and mutes; the release does nothing further |
| D5 | After D4, confirm mute did **not** also toggle | The console is in standby, not standby-and-then-unmuted |
| D6 | Press and hold knobs 2, 3 and 4 for 1 s each | **No ring appears** and nothing happens beyond the short press on release. Only the volume knob has a long action wired |

**E · Muted card variant**

| # | Steps | Expected |
|---|---|---|
| E1 | Mute, then press the volume knob to unmute and immediately re-mute so a card is up while muted | Numerals dim, the bar renders as an unfilled red outline, and the card reads the word `MUTED` |
| E2 | Take a greyscale screenshot | The muted state is still identifiable from text alone |

**F · The Sleep host — read §0.4 first**

| # | Steps | Expected |
|---|---|---|
| F1 | Navigate the kiosk **directly to `/sleep`** (do **not** press the Sleep pill) | Clock composition |
| F2 | Turn the volume knob | The clock is **replaced** by a centred amber volume readout at 96 px inside the drift wrapper; no card border, no cyan, no red |
| F3 | Wait 1.5 s | The readout clears and the clock composition **returns** |
| F4 | Mute first, then repeat F2 | `MUTED` renders in dim `--text-low`, not red |
| F5 | Now reach `/sleep` via the **Sleep pill**, then turn a knob | The console **wakes and navigates home** — expected, pre-`ENC-6`. Not a defect |

**G · Stacking and pointer-through**

| # | Steps | Expected |
|---|---|---|
| G1 | On Home with `RadioControlPanel` visible, turn volume so a card is up over the panel | The card renders **above** the panel, not behind it |
| G2 | Open the gain popover, then turn the volume knob | The card renders above the gain backdrop |
| G3 | While a card is up, tap a control **underneath** it | The tap reaches the control. The HUD never eats a touch |

**H · Load — the §6.8 requirement**

| # | Steps | Expected |
|---|---|---|
| H1 | Play audio. Spin the volume knob continuously for 30 s | **No audible distortion** |
| H2 | During H1, sample SignalR traffic (DevTray "Updates/sec", or DevTools WS frames) | `EncoderHudChanged` at **≤ 20 Hz** |
| H3 | Confirm the audio itself was not throttled | Volume tracks the hand continuously; it does not step in 50 ms jumps |
| H4 | `ssh mmack@radio "journalctl -u radio-api --since '-5min' --no-pager \| wc -l"` after H1 | No per-broadcast log spam |

**I · Accessibility**

| # | Steps | Expected |
|---|---|---|
| I1 | With the AT-SPI environment exported (see `CLAUDE.md`), turn a knob and dump the Chrome tree | The HUD's text is present as a live region |
| I2 | Set `prefers-reduced-motion: reduce` in DevTools, turn a knob | The card appears instantly; the ring renders as a filling bar rather than a rotation |

**J · Regression — nothing this row touched should have moved**

| # | Steps | Expected |
|---|---|---|
| J1 | Long-press a band pill in `RadioControlPanel` | Still opens save-preset at 600 ms (Task 1 repointed its constant) |
| J2 | Long-press a preset card | Still opens the action menu |
| J3 | Mute from the topbar chip and from `NowPlayingPanel` | Both still work; the chip still appears on every route |
| J4 | Unplug the encoder mid-session | `ENC-0`'s toast still fires; any card on screen clears rather than sticking |

### 4.4 The three highest-weighted checks

If time is short, these are the ones that must not be skipped:

1. **C2** — unmute-on-turn. The rule the Designer singled out.
2. **D5** — hold-to-standby must not also fire mute. The failure mode is a console that goes to
   standby unmuted, which is audible and confusing.
3. **A5** — the quarter geometry. It is the entire reason this component exists rather than a toast.

---

## 5. Self-review

**Spec coverage** — handoff §6, item by item:

| Handoff item | Where |
|---|---|
| §6.1 one component, two hosts | Tasks 12, 14, 15 |
| §6.1 built from `.snackbar-*`, the `GainPopoverService` pattern, `SourceBubble` | Tasks 13, 14; `SourceBubble` is used by `ENC-5`, not here — §0.2 item 3 |
| §6.2 quarters at 240/720/1200/1680, `bottom: 24px`, 360 px, blur 12, 10 px radius | Tasks 12, 13; UAT A |
| §6.2 label row treatment | Task 13, §0.3 D-2 |
| §6.3 volume card + muted + long-press variants | Tasks 12, 13; UAT C, D, E |
| §6.4 frequency + track cards | Tasks 5, 12; §0.3 D-1 |
| §6.4 "suppressed when RadioControlPanel is visible" | **Deliberately not implemented** — see §6 below |
| §6.5 timings (200 / 1500 / re-arm / 300→600 / reduced-motion) | Tasks 1, 11, 12, 13; UAT B3, D2, I2 |
| §6.6 the two selector overlays | **`ENC-5` / `ENC-7`** — seam in §2.5 |
| §6.7 persistent mute chip | **Already shipped as `ENC-4a`** (#493, `MainLayout.razor:79-87`) |
| §6.8 ≥50 ms coalescing, audio not throttled | Task 3; UAT H |
| §6.9 no new tokens | Tasks 12, 13; §2.4 |
| §4.4 unmute-on-turn | Task 6; UAT C |
| §4.4 press fires on release | Tasks 4, 7; UAT D1 |
| §4.4 long-press fires at the threshold while held | Tasks 4, 7; UAT D4 |
| §8.6 the Ambient readout | Tasks 12, 15; UAT F |
| §12.2 long-press synthesis, two consumers | Task 7 — one wired, one seamed |

**Placeholder scan:** no `TBD`, no "similar to Task N", no "implement later". Every code block above
is literal. Two places name a decision Builder makes (the ring's CSS technique in Task 13; the
`pointer-events` assertion in Task 16 case 12) — both state the acceptance criterion and forbid
faking it.

**Scope check:** no audio DSP, no changes to `VisualizationModeService`, no router remap, no overlay
lists, no wake model, no blanking, no settings surgery, no `MEMORY`→`PRESETS` rename (that is
`ENC-7`).

**Type consistency:** `MasterVolume` is `float` 0–1 (`IAudioMixerControl.cs:13`) and is converted to
whole points exactly once, in `PublishHud`. `Frequency` is a struct with `ToDisplayString()`
(`Frequency.cs:53`) and is formatted on the API side so the Web never re-derives it.

---

## 6. Things this plan deliberately does not do, with the reason

1. **§6.4's "suppress the TUNING card when `RadioControlPanel` is the visible centre panel."**
   Whether that panel is visible is Web-side state owned by `RadioPanelToggleService`, and the HUD
   payload is produced in the API. Implementing it means either sending the panel's visibility to
   the API or filtering in the component. The second is correct and cheap — **but it also means the
   knob is invisible on the one route where it should be most reassuring if the user has toggled to
   the queue panel.** This is a genuine design question, not an implementation detail, so it is
   raised rather than guessed. **Recommendation: ship the card unconditionally in this row and let
   the owner judge whether two frequency readouts 400 px apart actually read as noise on the real
   panel.** It is a one-line filter to add afterward.

2. **The exit animation.** See Task 12. Blazor unmounts the element; a 200 ms exit needs a two-phase
   teardown that `ENC-5` would then inherit.

3. **The router remap.** §0.2 item 1.

4. **PRESETS long-press → save.** §2.6. There is no PRESETS handler to save into.

5. **The `/sleep` Standby hint copy.** Task 15. It needs `ENC-6`'s Ambient-vs-Standby distinction.
