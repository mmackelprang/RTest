# PLAN — `ENC-5` · The SOURCE overlay, the shared selector component, and the router remap

**Row:** `ENC-5` (P0, Encoders workstream) — [`docs/HANDOFF-GA-PUNCH-LIST.md` §3.0](../../docs/HANDOFF-GA-PUNCH-LIST.md)
**Spec:** [`docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md`](../../docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md) (Rev 3) — **§4.4 Knob 2, §6.6, §6.2** are the spec; also §5.2, §5.3, §6.9, §8.3, §12.1, §12.2, §15.
**Relationship to the handoff:** **follows**, with **five declared deviations** — all in §0.4, each forced by something in the tree that the handoff assumed existed and does not.
**Depends on:** `ENC-1` ✅ (#498), `ENC-3` ✅ (#511), **`ENC-4`** — whose implementation landed on `main` at `29acc01` while this plan was being written. This plan mounts into its seam and edits four of its files, so it is written against that tree, not against the `ENC-4` plan document alone. **Re-read `RotaryEncoderActionRouter.cs`, `EncoderFeedbackService.cs`, `EncoderHudService.cs` and `EncoderHud.razor` as merged before Task 1** — where this plan and the shipped code differ, the code wins and the PR should say where.
**Pairs with:** [`ENC-7`](ENC-7-presets-knob.md) — **one component, two lists.** This PR builds the component; `ENC-7` consumes it. Build them back to back.
**Author:** Planner, 2026-09-02.
**Effort:** 5–6 days · **18 tasks** across 7 phases.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

The SOURCE knob today moves an invisible counter and logs at Debug
(`RotaryEncoderActionRouter.HandleSourceTurn`). This row gives it a screen: a centred overlay that
previews a highlight, commits on press, and says something when the commit is slow or fails. Because
decision **D7** folded the radio bands into that list, committing row 1 or row 2 is a **band change**
on an already-active radio — not a source switch — and the overlay's "current" marker has to track
the active *band*, not the word "Radio". And because the punch list is explicit that this overlay and
`ENC-7`'s are *"one component with two lists … building them apart is how they drift"*, this PR
builds the shared parts — the row model, the preview/commit engine, the overlay component, the CSS —
and `ENC-7` adds a list to them.

**It also carries the router remap**, because this is the first row that creates a handler the remap
can point at. See §0.3, which is the sharpest part of this plan.

### 0.2 Six things Builder must NOT do

1. ⛔ **Do NOT "simplify" preview-then-commit into live-commit-per-detent.** Spinning through the
   list would tear down and stand up an audio source at every detent — straight into the
   long-running capture-lifecycle bug and `autoSwitchOnConnect`. Designer §4.4 argues it; the punch
   list repeats it; it is not a performance nicety, it is the reason the mechanism exists.

2. ⛔ **Do NOT add auto-commit on dwell.** Considered and rejected (handoff §6.6): it converts every
   accidental brush of the knob into a real source change 1.2 s later, from across the room, with
   nobody touching anything. The footer line carries the discoverability that dwell would have
   bought.

3. ⛔ **Do NOT add any new design token.** Handoff §6.9: no `--hud-*` anything. The inventory is
   `ENC-4`'s §2.4 list, unchanged, plus the five `--source-*` accents which this row is the first to
   use in the HUD. Anything else must be an existing class reused verbatim or a literal that matches
   an existing component.

4. ⛔ **Do NOT build the PRESETS list, its recall, or its save.** That is `ENC-7`. This row builds
   the *engine and the component* they run on and leaves encoder 2 on the visualiser handler — see
   §0.3.

5. ⛔ **Do NOT reshape `ENC-4`'s `EncoderHud.razor`, `EncoderHudService` or `EncoderFeedbackService`
   beyond the four additive edits this plan names** (Tasks 1, 9, 11, 13). `ENC-4` §2.5 declares them
   a seam. Three of the four edits are places where the seam as written does not quite reach — §0.4
   D-3, D-4 and D-5 say exactly where and why.

6. ⛔ **Do NOT touch `AudioStateUpdateService.CheckVolumeAsync` or the `VolumeChanged` broadcast.**
   `HANDOFF-NEXT-SESSION.md`'s "do not add a second throttle" note is about that channel and is still
   true. The coalescer this row extends belongs to `ENC-4`'s separate HUD channel.

### 0.3 ⚠ The router remap — what it is, why it is here, and the exact end state

`RotaryEncoderActionRouter` maps `0 = Volume · 1 = Tuning · 2 = Source · 3 = Visualization`.
The cabinet reads `VOLUME · SOURCE · PRESETS · TUNING`, and `ENC-11` already pushes device
configuration in that **new** order (`RotaryEncoderConfigDefaults.Create()` comments each channel by
its cabinet name). Index 0 is VOLUME under both, so the knob with a safety hazard behind it is
already correct; **indices 1–3 have been deliberately left wrong** because remapping earlier would
have pointed encoder 2 at a PRESETS handler that did not exist.

**This PR fixes two of the three, and `ENC-7` fixes the last one. At no point does any index lack a
handler.**

| Index | Before this PR | **After this PR** | After `ENC-7` |
|---|---|---|---|
| 0 | Volume | **Volume** *(unchanged — never moves)* | Volume |
| 1 | Tuning | **SOURCE** *(this row's new handler)* | SOURCE |
| 2 | Source | **Visualization** *(moved here from 3)* | **PRESETS** *(`ENC-7`)* |
| 3 | Visualization | **Tuning** *(moved here from 1)* | Tuning |

**Why visualization takes the interim seat at index 2 rather than leaving the old source cycler
there.** After this PR index 1 opens the SOURCE overlay. If index 2 still ran the old
`_currentSourceIndex` cycler, two adjacent knobs would both change source from **two divergent
copies of the selection state** — precisely the defect class §4.4 spends a paragraph forbidding.
Visualization is a shipped, harmless, visible behaviour that already runs on a selector clamp, and
moving it costs no new code. It is a seat-warmer, and the PR says so in the code.

**The device configuration is a separate table and it is already right.** `RotaryEncoderConfigDefaults`
pushes acceleration-disabled to encoders 1 and 2 and the tuning tiers `(150 ×2 / 80 ×4 / 40 ×8)` to
encoder 3. Today those land on the wrong knobs. After this PR they land correctly, which has one
consequence worth stating in advance:

> ⚠ **Tuning acceleration goes live for the first time in this PR.** `TuningClamp = 8` stops being
> theoretical, and `StepRadioFrequencyAsync` awaits **one hardware call per step** — so a hard spin
> can now issue up to 8 sequential tuner calls per detent on a box where incidental load correlates
> with audible distortion. It is the designed behaviour (§5.5: a hard flick crosses the FM band in
> ≈0.6 revolutions) and the clamp is what bounds it, but UAT check **H2** exists because this PR is
> where it first happens.

**`ENC-4` pins the current mapping with tests, and those tests go red in this PR. That is intended.**
`tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs` (shipped with
`ENC-4`) contains `TurnOnEachIndex_PublishesThePreEnc5HandlerLabels`, which asserts
`["VOLUME", "TRACK", "SOURCE", "VISUALIZER"]` across indices 0–3 — the test's own name says which row
retires it — plus two more that raise turns on the indices this PR reassigns. **Task 8 names all
three and says exactly what each becomes.** Builder must not "fix" a red mapping test by reverting
the remap.

**`ENC-4`'s hard-coded HUD indices move with the handlers, and this is the trap.** `ENC-4` Task 5
writes `PublishHud(1, "TUNING", …)` inside `HandleTuningTurn`, `PublishHud(2, "SOURCE", …)` inside
`HandleSourceTurn`, and `PublishHud(3, "VISUALIZER", …)` inside `HandleVizTurn` — literals chosen to
match the *old* table. After the remap those literals put the tuning card in the SOURCE quarter and
the visualiser card in the TUNING quarter: **the card would appear above the wrong knob, which is the
one thing `ENC-4` exists to get right.** Task 7 removes the possibility by threading the real encoder
index into every handler. Do not leave a literal behind.

**`ENC-4`'s UAT note A2–A4 expires here.** Its Test Plan says the cards on indices 1–3 "will show the
WRONG WORDS, and that is the expected result of this row." After this PR, indices 0, 1 and 3 are
correct and only index 2 reads `VISUALIZER` where the cabinet says `PRESETS`. §4 restates the
expectation.

### 0.4 Five declared deviations from the Designer handoff — with the evidence that forced them

Each of these is a place where Rev 3's literal text assumes something the tree does not contain.
Resolved here so Builder does not guess and Polisher does not flag the result as drift.

| # | Handoff says | Tree says | Resolution |
|---|---|---|---|
| **D-1** | §4.4: *"List composition is fixed per tuner, resolved once at startup, **from the bands the active tuner reports**"* — so a tuner that never reports SW does not render a dead SW row | **No tuner reports its bands.** `IRadioControl` has no `SupportedBands` / capability member of any kind (`src/Radio.Core/Interfaces/Audio/IRadioControl.cs`). `IRadioBandService.GetAvailableBands()` is **device-agnostic** — it reflects over `BandPresets` and returns all six every time (`src/Radio.Infrastructure/Services/RadioBandService.cs:29`) | **Add the capability the spec requires** (Task 3): `IReadOnlyList<RadioBand> SupportedBands { get; }` on `IRadioControl`, **with a default interface implementation** returning `[FM, AM]` so the existing `FakeRadioControl` in `RadioStateMapperTests.cs:113` keeps compiling. `SDRRadioAudioSource` overrides with the `BandPresets` set; `RadioAudioSource` (RF320) overrides with `[FM]`, which is the truth — its `SetBandAsync` is a logged no-op (`RadioAudioSource.cs:177`) |
| **D-2** | §4.4: the overlay's band rows are `FM`, `AM`, `(SW / WB)` — four bands at most | `RadioControlPanel`'s on-screen pills render **six** — AM, FM, WB, VHF, SW, AIR — because they come from the device-agnostic `GetBandsAsync()` | **The overlay carries Designer's four; the pills keep their six.** VHF and AIR stay touchscreen-only: they are not broadcast bands anyone reaches for a knob to find, and adding them puts the list at 10 rows against a 7-row fit (§6.6). **The knob and the pills share the *active band*, which is what §4.4's one-state rule is about; they do not share list composition.** Recorded here rather than silently resolved |
| **D-3** | §6.6: the selector overlay idle-dismisses at **4000 ms**; §6.5: a HUD card holds **1500 ms** | `ENC-4`'s `EncoderHudService` arms a single dismissal timer at the constant `EncoderInteractionTimings.HudHoldMs` (1500) | **Carry the duration on the payload** (Tasks 1, 11): `DurationMs` is nullable and the service uses `Current.DurationMs ?? HudHoldMs`. One three-line change covers 1500 / 2000 / 4000 and every later message duration, instead of a phase-to-timer lookup that `ENC-7` would have to extend again |
| **D-4** | `ENC-4` §2.5 item 3: adding an overlay is *"adding a branch and a fragment, not restructuring the host, the geometry, the timers or the mounts"* | The geometry genuinely differs: §6.2 is explicit that **"transient readouts appear above the knob that produced them. Selection overlays center."** `ENC-4`'s root is 360 px wide, `bottom: 24px`, quartered by `left: QuarterCentre(index)` | **Both are satisfied by a branch, not a restructure**: `EncoderHud.razor` picks a root class and skips the `left` style for selector phases. `.encoder-hud` (quartered, 360 px) and `.encoder-selector-overlay` (centred, 440 px) are siblings. The host, the timers, the mounts and the subscription are untouched — only the root's class and inline style become phase-dependent |
| **D-5** | §6.6 mock shows a fixed list *("Seven rows plus chrome fits comfortably inside the 600 px content area")* | For SOURCE the list is at most 7 and this is fine. **For `ENC-7` it is not** — the preset bank's cap is **50** (`RadioPresetService.cs:18`), not 7 (see the `ENC-7` plan's §0.3) | **The shared overlay windows to 7 visible rows** around the highlight from the start (Task 12), even though SOURCE never needs it. Building the window here is the difference between `ENC-7` consuming the component and `ENC-7` rewriting it — which is the exact drift the punch list says to prevent |

### 0.5 Two live findings the row's text does not anticipate

1. **`RadioControlPanel` already subscribes to the authoritative band state.** The punch list says the
   band pills and this overlay "both read and write the active band; neither may hold its own copy",
   and calls it *"the same defect class as `VisualizerPanel` holding a local `_currentMode`"*.
   **The pills are already service-driven**: `@inject AudioStateHubService HubService` (`:6`),
   `HubService.RadioStateChanged += HandleRadioStateChanged` (`:981`), `_radioState = dto` (`:1047`),
   unsubscribed at `:1626`. The one local write is an optimistic
   `_radioState = _radioState with { Band = newBand }` at `:1096`, reconciled by the next broadcast.
   **So the one-state requirement is met by construction and Task 15 is a regression guard, not a
   rebuild.** The work D7 genuinely pushes outside the overlay's files is the *per-band frequency
   memory* (Task 4), not the pill wiring.

2. **`ENC-9a` (#491) did not do what the punch list says it did.** The punch list names it as the
   template for "one state, not two copies", implying `VisualizerPanel`'s local `_currentMode` was
   removed. It was **not** — `VisualizerPanel.razor:155` still declares
   `private VisualizationMode _currentMode = VisualizationMode.VUMeter;`. What #491 added was the
   **missing subscription** (`:170-174`) plus a typed DTO, and a handler that assigns the field
   directly and deliberately does *not* route back through `SelectMode` to avoid echoing the change
   outward (`:214-241`). **The real template is therefore: one authoritative owner in the API
   process, a typed broadcast, and a component that re-syncs a derived copy without echoing.** That
   is the pattern this row follows, and it is a weaker claim than "no component may hold a copy" —
   which is worth knowing before anyone tries to enforce the stronger one.

---

## 1. Architecture

### 1.1 The shape, end to end

```
  Radio.API process                                        Radio.Web process
 ┌────────────────────────────────────────┐              ┌──────────────────────────────────┐
 │ RotaryEncoderActionRouter              │              │ AudioStateHubService (singleton) │
 │   idx 1 turn ─▶ HandleSourceTurn ──┐   │              │   .On<EncoderHudDto>(            │
 │   idx 1 press ▶ HandleSourcePress ─┤   │              │      "EncoderHudChanged")        │
 │                                    ▼   │              │            │                     │
 │             SourceSelectorService      │              │            ▼                     │
 │               ├ composition (once)     │              │ EncoderHudService (singleton)    │
 │               ├ EncoderSelectorState   │◀── shared ──▶│   Current / IsHolding            │
 │               │    (preview index,     │   with       │   DurationMs-driven dismissal    │
 │               │     wrap, window)      │   ENC-7      │   StateChanged                   │
 │               └ commit:                │              │      │                           │
 │                   band  → SetBandAsync │              │      ▼                           │
 │                   source→ GetOrCreate  │              │ EncoderHud.razor                 │
 │                          │             │  SignalR     │   ├ Value phases → .encoder-hud  │
 │                          ▼             │  /hubs/audio │   └ Selector    →                │
 │             IEncoderFeedbackSink ──────┼─────────────▶│      <EncoderSelectorOverlay/>   │
 │             EncoderFeedbackService     │              │      (SHARED — ENC-7 reuses it)  │
 │               ≥50 ms coalescer         │              └──────────────────────────────────┘
 │                          │             │
 │                          ▼             │
 │             AudioStateUpdateService    │
 │               "EncoderHudChanged"      │
 └────────────────────────────────────────┘
```

Everything on the API side follows the `VisualizationModeService` precedent that `ENC-4` §2.1 already
names: a singleton owner in `Radio.Infrastructure`, state behind a lock, mutation through named
methods, an event raised outside the lock, bridged to the hub by `AudioStateUpdateService`.

### 1.2 What "one component, two lists" means concretely

Five artefacts are built here and **consumed unchanged** by `ENC-7`. If `ENC-7` has to modify any of
them beyond adding a caller, the split has drifted and the reviewer should say so.

| Artefact | Project | `ENC-7` does |
|---|---|---|
| `EncoderSelectorRow` | `Radio.Core` | fills it with presets instead of sources |
| `EncoderHudPhase.Selector*` + the selector fields on `EncoderHudEventArgs` | `Radio.Core` | publishes the same phases |
| `EncoderSelectorState` (preview index, wrap, window, blocked-flash) | `Radio.Infrastructure` | constructs a second instance |
| `EncoderSelectorRowDto` + the selector fields on `EncoderHudDto` | `Radio.Web` | nothing |
| `EncoderSelectorOverlay.razor` + `.encoder-selector-overlay` CSS | `Radio.Web` | nothing |

The two lists differ in exactly three places, all of them data: the row contents, the title/footer
strings, and what commit does. Nothing about the *grammar* — one detent one entry, wrap with a
200 ms bottom→top animation, press commits the highlight, dimmed rows flash instead of no-op'ing,
4 s idle dismiss — is written twice.

### 1.3 The overlay is a readout, not a control

`pointer-events: none`, exactly as `ENC-4`'s card. Handoff §6.6: *"Not a modal — no backdrop dimming,
no focus trap, no Escape requirement. A heads-up list the machine forgets about on its own."* Every
function it offers already has a touch equivalent (the topbar source strip, the band pills), per
handoff §7.3's "absent knobs cost convenience, not capability". Making it tappable would add a
second, divergent way to switch sources and a 440 px touch shield over the centre of Home.

### 1.4 Preview state lives in the API process, and why

The knob events arrive in `Radio.API`. Putting the preview index in `Radio.Web` would mean a
round trip per detent to decide what the highlight is, and would fork the state the moment a second
browser connected. The API owns it; the Web renders it. Handoff §12.1 item 1 flags "whether ephemeral
preview state belongs on the audio hub at all" as an architecture question and no ADR answers it —
**this plan answers it by reusing `ENC-4`'s channel rather than adding a second one**, on the grounds
that a HUD card and an overlay are the same thing at different sizes, and a second hub event would
need its own coalescer, its own DTO and its own subscription for no behavioural gain. Recorded here
so it is a decision rather than an accident.

### 1.5 Every selector publish carries the whole list — deliberately

`EncoderFeedbackService` coalesces by **replacing** the pending update for an encoder inside a 50 ms
window. If an "overlay opened, here are the rows" event could be replaced by a rows-less "highlight
moved" event, the Web would hold a highlight index and no list. So every `SelectorPreview` publish
carries the full `Rows` array, and every selector update is self-contained.

The cost is ~800 bytes of JSON at up to 20 Hz on loopback between two processes on the same box, and
no extra browser traffic at all — Blazor diffs the rendered DOM, and only the highlight class
changes. **Do not "optimise" this into a rows-null delta.** The saving is invisible and the failure
it re-introduces is an empty overlay that only appears when somebody is spinning the knob fast.

---

## 2. Tasks

> **Convention reminders for every task:** 2-space indent · file-scoped namespaces · nullable
> enabled · **warnings-as-errors in Release** · MudBlazor/Radzen as already used in the file ·
> bUnit tests need `JSInterop.Mode = JSRuntimeMode.Loose` · comment internal logic, edge cases and
> protocol details.
>
> **⚠ The pre-merge review rule this repo enforces hardest** (`CLAUDE.md` § Pre-Merge Review): a
> comment, log message or XML doc must assert **only what the code actually does**. This repo has
> shipped three such mismatches, two of which caused real bugs. Where a comment offers a *reason* a
> thing is safe, the reason is the claim that gets checked. Write no comment in this PR saying
> "always", "only", "never" or "guards every" unless the diff enforces it. **This row has two
> specific traps:** (a) do not write that the overlay and the band pills "share one state" without
> qualifying that they share the active band and not the list composition (§0.4 D-2); (b) do not
> write that a band commit "cannot fail" — on an RF320 it silently does nothing (Task 6).

---

### Phase 0 — the shared contract

#### Task 1 — Selector phases, the row model, and `DurationMs` (`Radio.Core`)

**Why:** §1.2. Every artefact `ENC-7` reuses starts here.

**Create** `src/Radio.Core/Interfaces/Input/EncoderSelectorRow.cs`:

```csharp
namespace Radio.Core.Interfaces.Input;

/// <summary>
/// One row of a selector overlay — a source, a radio band, or a saved preset.
///
/// <para>
/// Deliberately flat and presentation-shaped rather than a union of domain types. The overlay
/// renders it without knowing what it stands for, which is what lets ENC-5's source list and
/// ENC-7's preset list share one component instead of two that drift apart. What a commit
/// <i>does</i> is decided on the API side from <see cref="Id"/>; the Web never parses it.
/// </para>
/// </summary>
public sealed class EncoderSelectorRow
{
  /// <summary>
  /// Stable identity for this row, in a <c>kind:value</c> shape — <c>"band:FM"</c>,
  /// <c>"source:Bluetooth"</c>, <c>"preset:0f3c…"</c>. The owning service parses it on commit; it is
  /// also the Blazor <c>@key</c>, so it must not change while the overlay is open.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>Primary line — "FM", "BLUETOOTH", or a preset's name.</summary>
  public required string Primary { get; init; }

  /// <summary>Secondary line — a frequency, a paired device name, or null.</summary>
  public string? Secondary { get; init; }

  /// <summary>
  /// Leading ordinal, zero-padded by the caller ("01"), or null for rows that have no slot.
  /// Source rows never have one; preset rows carry the same per-band slot the on-screen bank shows.
  /// </summary>
  public string? Ordinal { get; init; }

  /// <summary>
  /// Radzen icon name for the row glyph, or null for no glyph. Sourced from
  /// <c>SourceTypeHelper.GetIcon</c>'s vocabulary so the overlay and the topbar strip cannot drift.
  /// </summary>
  public string? Icon { get; init; }

  /// <summary>
  /// CSS custom-property name for this row's accent — e.g. <c>"--source-radio"</c>. Null falls back
  /// to <c>--accent-primary</c> in CSS. Values come from <c>SourceTypeHelper.GetAccentVar</c>; this
  /// row introduces no new colour.
  /// </summary>
  public string? AccentVar { get; init; }

  /// <summary>True for the row that is currently playing. At most one row should carry it.</summary>
  public bool IsCurrent { get; init; }

  /// <summary>
  /// False when committing this row cannot succeed right now. A false value renders the row dimmed
  /// and makes a commit flash it rather than act.
  /// </summary>
  public bool IsAvailable { get; init; } = true;

  /// <summary>
  /// Why the row is unavailable, as a short phrase with no leading separator — "no device paired",
  /// "no tuner detected". The overlay renders it with SourceBubble's " · " idiom. Required whenever
  /// <see cref="IsAvailable"/> is false: handoff §6.6 State B is "dimmed <b>with a reason</b>",
  /// because a dimmed row with no reason is a dead end.
  /// </summary>
  public string? UnavailableReason { get; init; }
}
```

**Edit** `src/Radio.Core/Interfaces/Input/IEncoderFeedbackSink.cs` (created by `ENC-4` Task 2).

Add to `EncoderHudPhase`, **after** the four `ENC-4` members so the existing ordinals do not move:

```csharp
  /// <summary>
  /// A selector overlay is open and previewing. Nothing has been committed. Coalesced like
  /// <see cref="Value"/> — a moving highlight is a sampled value, not a discrete edge.
  /// </summary>
  SelectorPreview,

  /// <summary>
  /// A commit landed on an unavailable row. The overlay stays open and flashes that row.
  /// Handoff §6.6 State C — never a silent no-op.
  /// </summary>
  SelectorBlocked,

  /// <summary>A real switch is in flight. Handoff §6.6 State D — spinner, card stays up.</summary>
  SelectorCommitting,

  /// <summary>The switch failed. Handoff §6.6 State E — reason plus what is still playing.</summary>
  SelectorFailed,

  /// <summary>
  /// A short message replacing the list for its own duration — ENC-7's "Saved to 05", "PRESETS
  /// FULL", "Only radio stations can be saved". Declared here rather than in ENC-7 so the phase set
  /// is one enum and the Web's dispatch table is written once.
  /// </summary>
  SelectorNotice,
```

Add a helper beside the enum:

```csharp
/// <summary>
/// Which phases are samples of a moving value and may therefore be coalesced, and which are
/// discrete edges that must not be dropped.
/// </summary>
public static class EncoderHudPhases
{
  /// <summary>
  /// True for phases that represent "the current value, sampled" — a turning knob. False for edges
  /// whose loss would strand something on screen: a progress ring that never resolves, a spinner
  /// that never clears, a flash that never fires.
  /// </summary>
  public static bool IsCoalescable(EncoderHudPhase phase) =>
    phase is EncoderHudPhase.Value or EncoderHudPhase.SelectorPreview;
}
```

Add to `EncoderHudEventArgs`:

```csharp
  /// <summary>
  /// How long the client should hold this card before dismissing it, in milliseconds. Null means
  /// the default (<see cref="EncoderInteractionTimings.HudHoldMs"/>).
  ///
  /// <para>
  /// Carried on the payload rather than derived from <see cref="Phase"/> because the handoff
  /// specifies four different durations across five states (1500 value / 1500 blocked / 2000 saved /
  /// 4000 selector idle / 4000 failed), and ENC-7 adds more. One nullable field beats a lookup
  /// table each row has to extend.
  /// </para>
  /// </summary>
  public int? DurationMs { get; init; }

  /// <summary>
  /// The selector list, when this is a selector phase. Null on every non-selector phase.
  ///
  /// <para>
  /// <b>Always the complete list, never a delta.</b> <c>EncoderFeedbackService</c> coalesces by
  /// replacing the pending update for an encoder, so a rows-less update arriving inside the 50 ms
  /// window would discard the rows the overlay needs. Every selector update is self-contained.
  /// </para>
  /// </summary>
  public IReadOnlyList<EncoderSelectorRow>? Rows { get; init; }

  /// <summary>Index into <see cref="Rows"/> of the highlighted row, or -1 when the list is empty.</summary>
  public int HighlightIndex { get; init; } = -1;

  /// <summary>Overlay heading — "SOURCE" or "PRESETS".</summary>
  public string? Title { get; init; }

  /// <summary>Right-hand side of the heading row — ENC-7's "4 saved". Null for SOURCE.</summary>
  public string? TitleSuffix { get; init; }

  /// <summary>Footer line — "PRESS THE KNOB TO SWITCH" / "PRESS TO PLAY · HOLD TO SAVE".</summary>
  public string? Footer { get; init; }

  /// <summary>Primary line of the instructional empty state, when <see cref="Rows"/> is empty.</summary>
  public string? EmptyPrimary { get; init; }

  /// <summary>Secondary line of the instructional empty state.</summary>
  public string? EmptySecondary { get; init; }
```

**Edit** `src/Radio.Core/Configuration/EncoderInteractionTimings.cs` (created by `ENC-4` Task 1) —
append:

```csharp
  /// <summary>
  /// How long a selector overlay stays up with nothing committed, in milliseconds (handoff §6.5).
  ///
  /// <para>
  /// Longer than a value card's 1500 ms because a list has to be read, and because dismissing it
  /// costs nothing: nothing has been committed, so a timeout is not a lost action.
  /// </para>
  /// </summary>
  public const int SelectorIdleDismissMs = 4000;

  /// <summary>
  /// How long a commit on an unavailable row flashes that row before the overlay returns to
  /// previewing, in milliseconds (handoff §6.6 State C).
  /// </summary>
  public const int SelectorBlockedFlashMs = 1500;

  /// <summary>
  /// How long a failed switch stays on screen before dismissing, in milliseconds (§6.6 State E).
  /// It has to outlast a glance across a room, because the whole point is that the user learns the
  /// old source is still playing rather than concluding the knob is broken.
  /// </summary>
  public const int SelectorFailedMs = 4000;

  /// <summary>
  /// How many rows the selector overlay shows at once. Seven rows plus chrome is what fits the
  /// 600 px content area (handoff §6.6); a longer list scrolls a window of this size around the
  /// highlight.
  /// </summary>
  public const int SelectorVisibleRows = 7;
```

**Verify:** `dotnet build --configuration Release` clean.

---

#### Task 2 — `EncoderFeedbackService` coalesces the preview phase

**Why:** §1.5 and Task 1's `IsCoalescable`. Without this the preview flushes on every event —
`PollIntervalMs = 10`, so up to 100 SignalR fan-outs a second while a hand is on the knob, which is
the exact hazard handoff §6.8 exists to prevent.

**Edit** `src/Radio.Infrastructure/Platform/Input/EncoderFeedbackService.cs` (created by `ENC-4`
Task 3). One predicate changes:

```csharp
      // was: if (update.Phase != EncoderHudPhase.Value)
      if (!EncoderHudPhases.IsCoalescable(update.Phase))
      {
        // Discrete edge. Cancel anything pending for this encoder and let it through unchanged.
        CancelTimerLocked(i);
        _pending[i] = null;
        _lastEmittedTicks[i] = _timeProvider.GetTimestamp();
        emitNow = update;
      }
```

and the class summary's paragraph that reads *"Only `EncoderHudPhase.Value` is coalesced"* becomes:

```csharp
/// <para>
/// <b>Only sampled phases are coalesced</b> — a turning knob's value and a moving selector
/// highlight (see <see cref="EncoderHudPhases.IsCoalescable"/>). The hold phases and the selector's
/// commit phases are discrete edges, not samples, and dropping one would strand a progress ring or
/// a spinner on screen. They flush immediately and clear any pending value for that encoder.
/// </para>
```

⚠ **The comment must move with the code.** `CLAUDE.md`'s pre-merge rule has three shipped
counter-examples; a summary still naming `Value` alone after the predicate widened is a fourth.

**Tests** — extend `tests/Radio.Infrastructure.Tests/Platform/Input/EncoderFeedbackServiceTests.cs`:

1. `SelectorPreview_IsCoalescedLikeAValue` — publish 10 previews inside 50 ms ⇒ one leading emit
   plus one trailing emit carrying the **last** highlight index.
2. `SelectorCommitting_FlushesImmediately_AndClearsAPendingPreview` — a preview inside the window
   followed by a `SelectorCommitting` emits the committing update and never emits the preview.
3. `SelectorPreview_AlwaysCarriesRows` — a guard test asserting the fake sink saw non-null `Rows` on
   every emitted preview. It is a regression pin for §1.5, and its name says what it protects.

---

### Phase 1 — the tuner's bands, and per-band frequency memory

#### Task 3 — `IRadioControl.SupportedBands`

**Why:** §0.4 D-1. Without it, "composition is fixed per tuner" is unimplementable and every tuner
gets an SW row it may not have.

**Edit** `src/Radio.Core/Interfaces/Audio/IRadioControl.cs`, beside `CurrentBand` (`:108`):

```csharp
  /// <summary>
  /// The bands this tuner can actually be switched to.
  ///
  /// <para>
  /// A <b>default implementation</b> rather than an abstract member, deliberately: adding an
  /// abstract member here breaks every existing implementer and test double, and the honest default
  /// for "a tuner we know nothing else about" is the two broadcast bands every consumer already
  /// assumes. Implementations that know better override it, and <see cref="RadioAudioSource"/>
  /// overrides it downward.
  /// </para>
  ///
  /// <para>
  /// This is a <i>capability</i> list, not an availability one. A band appearing here means the
  /// tuner can tune it; whether it can right now — whether the hardware is present at all — is a
  /// separate question the caller answers from <see cref="IsRunning"/> and from whether a radio
  /// source exists.
  /// </para>
  /// </summary>
  IReadOnlyList<RadioBand> SupportedBands => [RadioBand.FM, RadioBand.AM];
```

**Edit** `src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs`, beside
`CurrentBand` (`:393`):

```csharp
  /// <summary>
  /// Every band the RTL-SDR front end covers, from <c>BandPresets</c> — the same definitions the
  /// band list endpoint serves, so the two cannot disagree.
  /// </summary>
  public IReadOnlyList<RadioBand> SupportedBands { get; } =
    [RadioBand.FM, RadioBand.AM, RadioBand.SW, RadioBand.WB, RadioBand.VHF, RadioBand.AIR];
```

**Edit** `src/Radio.Infrastructure/Audio/Sources/Primary/RadioAudioSource.cs`, beside `CurrentBand`
(`:173`):

```csharp
  /// <summary>
  /// FM only. Not a conservative guess — <see cref="SetBandAsync"/> on this device logs a warning
  /// and returns without doing anything, because the RF320's band selector is a physical switch.
  /// Reporting anything else here would put rows in the SOURCE overlay whose commit silently does
  /// nothing.
  /// </summary>
  public IReadOnlyList<RadioBand> SupportedBands { get; } = [RadioBand.FM];
```

**Tests** — `tests/Radio.Infrastructure.Tests/Audio/Sources/RadioControlCapabilityTests.cs`:

1. `Rf320_ReportsFmOnly_BecauseItsBandSetterIsANoOp` — the test name carries the reason.
2. `Sdr_ReportsEveryBandPresetsDefines` — asserts the list equals the six `BandPresets` bands, so
   adding a band preset without extending this list fails here rather than in the cabinet.

---

#### Task 4 — Per-band last-tuned frequency

**Why:** handoff §4.4 — *"Restore the **last-tuned frequency for that band**, falling back to the
band's default when there isn't one."* It does not exist. `RadioPreferences` holds exactly one
`LastFrequency` (`:24`), so a band switch overwrites it, and `RadioReceiver.SetBand` currently clamps
the *previous* band's frequency into the new band's edges (`RadioReceiver.cs:529-530`) — so FM → AM
lands on 1710 kHz, the top of the AM dial, every time.

⚠ **Do not add a field to `RadioPreferences`.** Two writers already share that section and one of
them will clobber you: `PreferencesPersistenceService.SaveRadioPreferencesFromLiveStateAsync:118`
constructs a whole `RadioPreferences` from live state and saves it, while `SystemConfigPage`
(`:2985`) writes a **three-field** `RadioPreferencesDto` through
`/api/configuration/radiopreferences`. A dictionary added to that class would be silently dropped by
whichever wrote last. `RadioOptions` also declares `SectionName = "Radio"`. **A new section is the
cheap way out of a mess this row did not create.**

**Create** `src/Radio.Core/Configuration/RadioBandMemory.cs`:

```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// The last frequency tuned on each band, so switching bands returns you where you were.
///
/// <para>
/// <b>Its own configuration section on purpose.</b> <c>RadioPreferences</c> and
/// <c>RadioOptions</c> both declare <c>SectionName = "Radio"</c>, and that section already has two
/// writers with different field sets — one persisting live state, one a three-field settings-page
/// DTO. A map added there would be dropped by whichever wrote last, silently, and the symptom would
/// be "the band memory works until you open Settings".
/// </para>
/// </summary>
public class RadioBandMemory
{
  public const string SectionName = "RadioBandMemory";

  /// <summary>
  /// Band name (<c>RadioBand.ToString()</c>) to last-tuned frequency in <b>hertz</b>.
  ///
  /// <para>
  /// Hertz because that is what <c>Frequency.Hertz</c> and the whole radio API carry. The older
  /// <c>RadioPreferences.LastFrequency</c> is a <c>double</c> whose doc comment says "MHz (for FM)
  /// or kHz (for AM)" while the code writing it stores hertz — do not copy that.
  /// </para>
  /// </summary>
  public Dictionary<string, long> LastFrequencyHzByBand { get; set; } = [];
}
```

**Create** `src/Radio.Core/Interfaces/Audio/IRadioBandMemory.cs`:

```csharp
using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>Remembers where the dial was left on each band.</summary>
public interface IRadioBandMemory
{
  /// <summary>
  /// The frequency to tune when entering <paramref name="band"/>, or null when there is nothing
  /// remembered and no default is known for it.
  /// </summary>
  Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default);

  /// <summary>Records where the dial was left on <paramref name="band"/>.</summary>
  Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default);
}
```

**Create** `src/Radio.Infrastructure/Audio/Services/RadioBandMemoryService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using RTLSDRCore.Bands;
using RTLSDRCore.Enums;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Per-band dial memory, backed by the configuration store.
///
/// <para>
/// The fallback ladder is three rungs and each one exists because the rung above it can be empty on
/// a fresh install: what was remembered, then the configured default for that band, then the bottom
/// edge of the band. There is deliberately no fourth rung that keeps the current frequency — that is
/// today's behaviour and it is the bug: <c>RadioReceiver.SetBand</c> clamps the outgoing band's
/// frequency into the incoming band's range, which lands FM → AM at 1710 kHz every time.
/// </para>
/// </summary>
public sealed class RadioBandMemoryService : IRadioBandMemory
{
  private readonly ILogger<RadioBandMemoryService> _logger;
  private readonly IConfigurationStore _store;
  private readonly IOptionsMonitor<RadioOptions> _radioOptions;

  public RadioBandMemoryService(
    ILogger<RadioBandMemoryService> logger,
    IConfigurationStore store,
    IOptionsMonitor<RadioOptions> radioOptions)
  {
    _logger = logger;
    _store = store;
    _radioOptions = radioOptions;
  }

  public async Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default)
  {
    var memory = await LoadAsync(cancellationToken);
    if (memory.LastFrequencyHzByBand.TryGetValue(band.ToString(), out long hz) && hz > 0)
    {
      return new Frequency(hz);
    }

    return DefaultFor(band);
  }

  public async Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default)
  {
    if (frequency.Hertz <= 0)
    {
      return;
    }

    var memory = await LoadAsync(cancellationToken);
    memory.LastFrequencyHzByBand[band.ToString()] = frequency.Hertz;
    await _store.SetSectionAsync(RadioBandMemory.SectionName, memory, cancellationToken);
    _logger.LogDebug("Remembered {Band} at {Hz} Hz", band, frequency.Hertz);
  }

  private async Task<RadioBandMemory> LoadAsync(CancellationToken cancellationToken) =>
    await _store.GetSectionAsync<RadioBandMemory>(RadioBandMemory.SectionName, cancellationToken)
      ?? new RadioBandMemory();

  /// <summary>
  /// The default landing frequency for a band with nothing remembered.
  ///
  /// <para>
  /// FM and AM have configured defaults; the other four do not, so they land on the bottom edge of
  /// the band from <c>BandPresets</c>. The bottom edge is a real, tunable frequency and it is where
  /// a mechanical dial would sit at rest — it is not a placeholder.
  /// </para>
  /// </summary>
  private Frequency? DefaultFor(RadioBand band)
  {
    var opts = _radioOptions.CurrentValue;
    return band switch
    {
      RadioBand.FM => Frequency.FromMegahertz(opts.DefaultFMFrequencyMHz),
      RadioBand.AM => Frequency.FromKilohertz(opts.DefaultAMFrequencyKHz),
      _ => BottomEdge(band),
    };
  }

  private Frequency? BottomEdge(RadioBand band)
  {
    BandType? mapped = band switch
    {
      RadioBand.SW => BandType.Shortwave,
      RadioBand.WB => BandType.Weather,
      RadioBand.VHF => BandType.VHF,
      RadioBand.AIR => BandType.Aircraft,
      _ => null,
    };

    if (mapped is null)
    {
      return null;
    }

    try
    {
      return new Frequency(BandPresets.GetBand(mapped.Value).MinFrequencyHz);
    }
    catch (ArgumentException ex)
    {
      // BandPresets.GetBand throws rather than returning null for a type it does not know.
      _logger.LogWarning(ex, "No band preset for {Band}; no default frequency available", band);
      return null;
    }
  }
}
```

> **Builder:** confirm the exact `IConfigurationStore` section-read/write method names before
> writing this file — the shape above is `GetSectionAsync<T>` / `SetSectionAsync`. If the store's
> API differs, follow whatever `PreferencesPersistenceService` already calls, and change **only**
> those two lines. Do not add a new persistence mechanism.

**Register it beside `IConfigurationStore`, NOT inside `AddRotaryEncoders`:**

```csharp
    // ENC-5. Singleton: per-band dial memory is one physical dial's state, read on every band
    // commit and written after every tune, and every consumer of it is a singleton.
    services.AddSingleton<IRadioBandMemory, RadioBandMemoryService>();
```

> ⚠ **This registration must live wherever `IConfigurationStore` is registered, not in
> `AddRotaryEncoders`.** The shipped test `RotaryEncoderRegistrationTests.AddRotaryEncoders_ResolvesTheActionRouter`
> (`tests/Radio.Infrastructure.Tests/DependencyInjection/RotaryEncoderRegistrationTests.cs`) builds a
> provider containing **only** `AddLogging()` and `AddRotaryEncoders(...)` — deliberately, so that
> resolving the router does not initialise real audio hardware. `RadioBandMemoryService` needs
> `IConfigurationStore`, which that provider does not have, so registering it there would turn a
> passing guard into a resolution failure. Task 6 keeps the guard green by deferring the resolution
> the same way the router already defers `IAudioManager`.

**Write path.** The memory has to be *written* or it never fills. Edit
`src/Radio.Infrastructure/Configuration/Services/PreferencesPersistenceService.cs`, inside
`SaveRadioPreferencesFromLiveStateAsync` after the existing save (`:126-135`), guarded by the same
"radio is the active source" check that already wraps it at `:142-145`:

```csharp
      // ENC-5. The flat LastFrequency above is one slot for every band; this records the same
      // reading against the band it was taken on, so a band switch can return here.
      await _bandMemory.SetAsync(radioControl.CurrentBand, radioControl.CurrentFrequency, cancellationToken);
```

with `IRadioBandMemory` added to that service's constructor.

**Tests** — `tests/Radio.Infrastructure.Tests/Audio/Services/RadioBandMemoryServiceTests.cs`:

1. `Get_ReturnsRememberedValue_WhenPresent`
2. `Get_FallsBackToConfiguredDefault_ForFm`
3. `Get_FallsBackToConfiguredDefault_ForAm`
4. `Get_FallsBackToBandBottomEdge_ForShortwave`
5. `Get_ReturnsNull_WhenNoMemoryAndNoDefault` — pass a band with no `BandPresets` mapping.
6. `Set_ThenGet_RoundTripsInHertz` — the unit guard. `RadioPreferences.LastFrequency`'s doc comment
   disagrees with the code that writes it; this test is what stops that repeating.
7. `Set_IgnoresNonPositiveFrequency`

---

### Phase 2 — the selector engine

#### Task 5 — `EncoderSelectorState` — the shared preview machine

**Why:** §1.2. This is the half of "one component, two lists" that lives in the API process. `ENC-7`
constructs a second instance and writes none of this again.

**Create** `src/Radio.Infrastructure/Platform/Input/EncoderSelectorState.cs`:

```csharp
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The preview state of one selector overlay: which rows it holds, which one is highlighted, and
/// whether it is currently open.
///
/// <para>
/// Shared by ENC-5's SOURCE list and ENC-7's PRESETS list. The two knobs are adjacent and behave
/// identically by design — handoff §4.4: "two adjacent selector knobs that behave identically is a
/// feature: learn one, you have learned both" — so the grammar is written once here and the lists
/// differ only in their contents and in what a commit does.
/// </para>
///
/// <para>
/// Not thread-safe on its own. Callers hold the router's own event ordering: encoder events arrive
/// on the single HID read loop, and the idle timer's callback is the only other writer, which is why
/// <see cref="Close"/> and <see cref="Move"/> are both called under the owner's lock.
/// </para>
/// </summary>
public sealed class EncoderSelectorState
{
  private IReadOnlyList<EncoderSelectorRow> _rows = [];
  private int _highlight = -1;

  /// <summary>True while the overlay is on screen.</summary>
  public bool IsOpen { get; private set; }

  /// <summary>The rows as last composed. Never null; empty means the instructional empty state.</summary>
  public IReadOnlyList<EncoderSelectorRow> Rows => _rows;

  /// <summary>Index of the highlighted row, or -1 when there are no rows.</summary>
  public int HighlightIndex => _highlight;

  /// <summary>The highlighted row, or null when the list is empty.</summary>
  public EncoderSelectorRow? Highlighted =>
    _highlight >= 0 && _highlight < _rows.Count ? _rows[_highlight] : null;

  /// <summary>
  /// Replaces the rows, keeping the highlight on the same <see cref="EncoderSelectorRow.Id"/> where
  /// possible.
  ///
  /// <para>
  /// Identity rather than position, because the reason to recompose mid-overlay is that a row's
  /// availability changed or (in ENC-7) a preset was added — and moving somebody's highlight because
  /// the list grew underneath them is how a selector loses its place.
  /// </para>
  /// </summary>
  public void SetRows(IReadOnlyList<EncoderSelectorRow> rows)
  {
    string? keep = Highlighted?.Id;
    _rows = rows;

    if (_rows.Count == 0)
    {
      _highlight = -1;
      return;
    }

    int found = keep is null ? -1 : IndexOfId(keep);
    _highlight = found >= 0 ? found : DefaultHighlight();
  }

  /// <summary>
  /// Opens the overlay, seeding the highlight on the current row.
  ///
  /// <para>
  /// Seeding on "current" is what makes handoff §4.4's one-rule press work: with the overlay closed
  /// the highlight is what is already playing, so a press commits the status quo — it changes
  /// nothing and opens the overlay showing you where you are. That is what makes a mis-grab free.
  /// </para>
  /// </summary>
  public void Open()
  {
    if (!IsOpen)
    {
      _highlight = DefaultHighlight();
      IsOpen = true;
    }
  }

  /// <summary>Closes the overlay without committing anything.</summary>
  public void Close() => IsOpen = false;

  /// <summary>
  /// Moves the highlight by <paramref name="delta"/> entries, wrapping.
  ///
  /// <para>
  /// The caller has already applied the ENC-3 per-event clamp of ±1, so one detent is one entry at
  /// every spin speed. Wrapping is host-side: the device is configured <c>wrap = false</c> on both
  /// selector knobs precisely so the host owns it (handoff §5.2).
  /// </para>
  /// </summary>
  public void Move(int delta)
  {
    if (_rows.Count == 0)
    {
      _highlight = -1;
      return;
    }

    int next = (_highlight < 0 ? 0 : _highlight) + delta;
    _highlight = ((next % _rows.Count) + _rows.Count) % _rows.Count;
  }

  private int IndexOfId(string id)
  {
    for (int i = 0; i < _rows.Count; i++)
    {
      if (string.Equals(_rows[i].Id, id, StringComparison.Ordinal))
      {
        return i;
      }
    }

    return -1;
  }

  /// <summary>The current row if there is one, otherwise the first row.</summary>
  private int DefaultHighlight()
  {
    for (int i = 0; i < _rows.Count; i++)
    {
      if (_rows[i].IsCurrent)
      {
        return i;
      }
    }

    return _rows.Count == 0 ? -1 : 0;
  }
}
```

**Tests** — `tests/Radio.Infrastructure.Tests/Platform/Input/EncoderSelectorStateTests.cs`:

1. `Open_SeedsHighlightOnTheCurrentRow`
2. `Open_SeedsFirstRow_WhenNothingIsCurrent`
3. `Open_IsIdempotent_AndDoesNotResetTheHighlight`
4. `Move_WrapsForward_PastTheEnd`
5. `Move_WrapsBackward_PastTheStart`
6. `Move_OnAnEmptyList_LeavesHighlightAtMinusOne`
7. `SetRows_KeepsTheHighlightOnTheSameId_WhenTheListReorders`
8. `SetRows_FallsBackToCurrent_WhenTheHighlightedIdIsGone`
9. `SetRows_Empty_ClearsTheHighlight`

---

#### Task 6 — `SourceSelectorService`

**Why:** the whole of handoff §4.4 Knob 2 and §6.6 States A–E, on the API side.

**Create** `src/Radio.Infrastructure/Platform/Input/SourceSelectorService.cs`. Full shape, with the
decisions that matter written into the code:

```csharp
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The SOURCE knob's list, preview and commit (ENC-5).
///
/// <para>
/// The list is a <b>band selector</b> (D7): the tuner's bands are first-class entries, the way the
/// original cabinet's selector read BROADCAST / SHORTWAVE / PHONO. Committing a band while the radio
/// is already active is a <b>band change</b> — no engine teardown, no fade, no spinner — while
/// committing one from another source is a real source switch that gets all three.
/// </para>
///
/// <para>
/// Composition is resolved once, on the first open after a radio source exists, and cached for the
/// process. Handoff §4.4: "positions never move" is only achievable if the set does not change under
/// the user's hand, so a row that is unavailable is <b>dimmed with a reason</b> rather than removed.
/// </para>
/// </summary>
public sealed class SourceSelectorService
{
  /// <summary>
  /// The order of the list, fixed. Not <c>Enum.GetValues</c> order — <c>RadioBand</c> declares AM
  /// first — and not recency: a physical selector whose detent 3 is Bluetooth on Tuesday and Phono
  /// on Wednesday is not a physical selector.
  /// </summary>
  private static readonly RadioBand[] BandOrder = [RadioBand.FM, RadioBand.AM, RadioBand.SW, RadioBand.WB];

  /// <summary>
  /// The non-radio entries, in the handoff's §4.4 order. <c>AudioSourceType.Radio</c> is absent on
  /// purpose: the bands above are the radio, and a seventh row reading "RADIO" would be a second way
  /// to reach the same place from the same list.
  /// </summary>
  private static readonly AudioSourceType[] SourceOrder =
    [AudioSourceType.Bluetooth, AudioSourceType.Vinyl, AudioSourceType.GenericUSB, AudioSourceType.FilePlayer];

  private readonly ILogger<SourceSelectorService> _logger;
  private readonly Func<IAudioManager> _audioManagerFactory;
  // Func<> for the same reason the router uses one for IAudioManager: it defers resolution past
  // container build, so the minimal provider in RotaryEncoderRegistrationTests still resolves the
  // router without needing IConfigurationStore.
  private readonly Func<IRadioBandMemory> _bandMemoryFactory;
  private readonly IEncoderFeedbackSink _hud;
  private readonly EncoderSelectorState _state = new();
  private readonly object _gate = new();

  private RadioBand[]? _composedBands;

  public SourceSelectorService(
    ILogger<SourceSelectorService> logger,
    Func<IAudioManager> audioManagerFactory,
    Func<IRadioBandMemory> bandMemoryFactory,
    IEncoderFeedbackSink hud)
  {
    _logger = logger;
    _audioManagerFactory = audioManagerFactory;
    _bandMemoryFactory = bandMemoryFactory;
    _hud = hud;
  }

  /// <summary>
  /// The encoder index this overlay renders above. Passed by the router so the geometry follows the
  /// knob rather than a constant this class would have to be edited to change.
  /// </summary>
  public int EncoderIndex { get; set; } = 1;

  /// <summary>A turn: open if closed, then move the highlight. Nothing switches (handoff §4.4).</summary>
  public void Turn(int clampedDelta)
  {
    lock (_gate)
    {
      RecomposeLocked();
      _state.Open();
      _state.Move(clampedDelta);
      PublishPreviewLocked();
    }
  }

  /// <summary>
  /// A press: commit the highlight. One rule, not two — with the overlay closed the highlight is
  /// what is already playing, so a press commits the status quo, which changes nothing and opens the
  /// overlay showing you where you are.
  /// </summary>
  public void Press()
  {
    EncoderSelectorRow? row;
    lock (_gate)
    {
      RecomposeLocked();
      bool wasOpen = _state.IsOpen;
      _state.Open();

      if (!wasOpen)
      {
        // Opening. The highlight is the current row, so committing it would be a no-op anyway;
        // showing the list is the whole of the behaviour here.
        PublishPreviewLocked();
        return;
      }

      row = _state.Highlighted;
    }

    if (row is null)
    {
      return;
    }

    if (!row.IsAvailable)
    {
      PublishBlocked(row);
      return;
    }

    _ = CommitAsync(row);
  }

  /// <summary>
  /// Tears the overlay down without committing. Called when the encoder disappears mid-session —
  /// ENC-0's disconnect path — because an overlay you can no longer navigate is a trap.
  /// </summary>
  public void Dismiss()
  {
    lock (_gate)
    {
      _state.Close();
    }
  }
  // ... CommitAsync, RecomposeLocked, PublishPreviewLocked, PublishBlocked below
}
```

**`CommitAsync` — the four D7 requirements, each explicit:**

```csharp
  private async Task CommitAsync(EncoderSelectorRow row)
  {
    try
    {
      var mgr = _audioManagerFactory();

      if (row.Id.StartsWith("band:", StringComparison.Ordinal))
      {
        var band = Enum.Parse<RadioBand>(row.Id["band:".Length..]);

        if (mgr.ActiveSource is IRadioControl liveRadio)
        {
          // D7 requirement 1. Radio is already playing, so this is a BAND CHANGE, not a source
          // switch: no engine teardown, no fade, no spinner. It should feel instant because it is.
          await ApplyBandAsync(liveRadio, band);
          PublishPreview();
          return;
        }

        // D7 requirement 2. Radio is not active, so this is a real source switch AND a band change.
        PublishCommitting(row);
        var created = await mgr.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: true);
        if (created is not IRadioControl newRadio)
        {
          PublishFailed(row, "Tuner unavailable");
          return;
        }

        await ApplyBandAsync(newRadio, band);
        PublishPreview();
        return;
      }

      // A plain source switch. Bluetooth in particular can take seconds or fail outright, which is
      // why State D is not optional polish.
      var type = Enum.Parse<AudioSourceType>(row.Id["source:".Length..]);
      PublishCommitting(row);
      var switched = await mgr.GetOrCreateSourceAsync(type, switchToSource: true);
      if (switched is null)
      {
        PublishFailed(row, $"{row.Primary} unavailable");
        return;
      }

      PublishPreview();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error committing source selection {Id}", row.Id);
      PublishFailed(row, $"{row.Primary} unavailable");
    }
  }

  /// <summary>
  /// Sets the band and restores that band's last-tuned frequency.
  ///
  /// <para>
  /// The read-back is not defensive noise. <c>RadioAudioSource.SetBandAsync</c> logs a warning and
  /// returns <c>Task.CompletedTask</c> — it succeeds and does nothing — so a commit that trusted the
  /// absence of an exception would report a band change that never happened. The row for such a band
  /// is normally dimmed by <see cref="SupportedBands"/> composition and never reaches here; this
  /// covers a tuner swapped underneath a composed list.
  /// </para>
  /// </summary>
  private async Task ApplyBandAsync(IRadioControl radio, RadioBand band)
  {
    await radio.SetBandAsync(band);

    if (radio.CurrentBand != band)
    {
      _logger.LogWarning("Tuner did not change to {Band}; it reports {Actual}", band, radio.CurrentBand);
      return;
    }

    var restore = await _bandMemoryFactory().GetAsync(band);
    if (restore is { } freq)
    {
      await radio.SetFrequencyAsync(freq);
    }
  }
```

**`RecomposeLocked` — composition once, availability every time:**

```csharp
  /// <summary>
  /// Resolves the band set once, then refreshes availability and the current marker on every call.
  ///
  /// <para>
  /// Two different lifetimes on purpose. <b>Composition</b> — which rows exist — is fixed for the
  /// session so positions never move under the user's hand. <b>Availability</b> and the current
  /// marker change constantly (a phone connects, a band is switched from the touchscreen) and are
  /// recomputed each time.
  /// </para>
  /// </summary>
  private void RecomposeLocked()
  {
    var mgr = _audioManagerFactory();
    var radio = mgr.ActiveSource as IRadioControl ?? mgr.GetCachedSource(AudioSourceType.Radio) as IRadioControl;

    _composedBands ??= radio is null
      // No tuner has ever existed this session, so nothing can be asked what it supports. FM and AM
      // are rendered anyway, dimmed with a reason: handoff §4.4 wants "no tuner detected" on a row
      // that is there, not an absent row that gives the user nothing to aim at.
      ? [RadioBand.FM, RadioBand.AM]
      : BandOrder.Where(radio.SupportedBands.Contains).ToArray();

    var rows = new List<EncoderSelectorRow>(_composedBands.Length + SourceOrder.Length);
    var activeBand = radio is not null && mgr.ActiveSource is IRadioControl ? radio.CurrentBand : (RadioBand?)null;

    foreach (var band in _composedBands)
    {
      bool available = radio is not null;
      rows.Add(new EncoderSelectorRow
      {
        Id = $"band:{band}",
        Primary = band.ToString(),
        // The current band's live frequency, so the marked row reads like the frequency well.
        Secondary = available && activeBand == band ? radio!.CurrentFrequency.ToDisplayString() : null,
        Icon = "radio",
        AccentVar = "--source-radio",
        // D7 requirement 3: the marker tracks the active BAND, not "Radio". On AM, row 2 is marked.
        IsCurrent = activeBand == band,
        IsAvailable = available,
        UnavailableReason = available ? null : "no tuner detected",
      });
    }

    foreach (var type in SourceOrder)
    {
      var cached = mgr.GetCachedSource(type);
      bool available = cached is null || (cached.State != AudioSourceState.Error);
      rows.Add(new EncoderSelectorRow
      {
        Id = $"source:{type}",
        Primary = DisplayNameFor(type),
        Secondary = cached?.Name,
        Icon = IconFor(type),
        AccentVar = AccentFor(type),
        IsCurrent = mgr.ActiveSource is not null && ReferenceEquals(mgr.ActiveSource, cached),
        IsAvailable = available,
        UnavailableReason = available ? null : "unavailable",
      });
    }

    _state.SetRows(rows);
  }
```

> ⚠ **Availability is deliberately shallow, and the comment must say so.** There is **no**
> source-availability query on `IAudioManager` — `GetCachedSource` returns null for a source never
> created, and `IAudioSource.State` is the only signal (`MainLayout.IsSourceAvailable:881-883`
> derives the topbar's own answer the same way). A source that has never been created reads as
> available, and finding out it is not is what State E is for. **Do not write a comment claiming the
> list knows a source is reachable.** It knows one has not failed.

> **Builder:** `DisplayNameFor` / `IconFor` / `AccentFor` are three small private switches over
> `AudioSourceType`. Their values must match `SourceTypeHelper.GetIcon` and
> `SourceTypeHelper.GetAccentVar` (`src/Radio.Web/Components/Shared/SourceTypeHelper.cs:13,45`) —
> `"bluetooth"`/`--source-bluetooth`, `"album"`/`--source-vinyl`, `"usb"`/`--source-usb`,
> `"audio_file"`/`--source-file`, `"radio"`/`--source-radio`. That helper is in `Radio.Web` and
> cannot be referenced from `Radio.Infrastructure`; **do not move it** — a Web display helper is not
> an Infrastructure concern, and the coupling is four string pairs that a test pins (Task 16).

**The publish methods:**

```csharp
  /// <summary>
  /// Builds one selector payload. Every publish goes through here, so the title, footer and — most
  /// importantly — the full row list cannot be forgotten on one path and present on another.
  /// </summary>
  private EncoderHudEventArgs Compose(
    EncoderHudPhase phase,
    int? durationMs = null,
    string? primary = null,
    string? secondary = null) =>
    new()
    {
      EncoderIndex = EncoderIndex,
      // ENC-4's Label is required and drives the card's label row when a phase renders as a card.
      Label = "SOURCE",
      Phase = phase,
      Title = "SOURCE",
      Footer = "PRESS THE KNOB TO SWITCH",
      // Always the whole list — see the plan's §1.5. A rows-less selector update can be swallowed
      // by the coalescer's 50 ms replace window and leave the overlay with a highlight and no rows.
      Rows = _state.Rows,
      HighlightIndex = _state.HighlightIndex,
      DurationMs = durationMs,
      PrimaryText = primary,
      SecondaryText = secondary,
    };

  private void PublishPreview()
  {
    lock (_gate)
    {
      PublishPreviewLocked();
    }
  }

  private void PublishPreviewLocked() =>
    _hud.Publish(Compose(
      EncoderHudPhase.SelectorPreview,
      EncoderInteractionTimings.SelectorIdleDismissMs));

  private void PublishBlocked(EncoderSelectorRow row) =>
    // State C. The overlay stays open and the highlighted row flashes — the flash is the answer,
    // not a dismissal. The component knows which row to flash from HighlightIndex.
    _hud.Publish(Compose(
      EncoderHudPhase.SelectorBlocked,
      EncoderInteractionTimings.SelectorBlockedFlashMs));

  private void PublishCommitting(EncoderSelectorRow row) =>
    // State D. Only ever published for a real source switch; a band change on an already-active
    // radio skips it, which is what makes that path feel instant.
    _hud.Publish(Compose(
      EncoderHudPhase.SelectorCommitting,
      durationMs: null,
      primary: $"Switching to {row.Primary}…"));

  private void PublishFailed(EncoderSelectorRow row, string reason) =>
    // State E. The second line is what is STILL PLAYING, which is the part that stops the user
    // concluding the knob is broken.
    _hud.Publish(Compose(
      EncoderHudPhase.SelectorFailed,
      EncoderInteractionTimings.SelectorFailedMs,
      primary: reason,
      secondary: $"Staying on {CurrentDescription()}"));

  /// <summary>
  /// What is playing right now, for State E's second line — the current band and frequency on radio,
  /// otherwise the active source's name, otherwise nothing.
  /// </summary>
  private string CurrentDescription()
  {
    var mgr = _audioManagerFactory();
    return mgr.ActiveSource switch
    {
      IRadioControl radio => $"{radio.CurrentBand} {radio.CurrentFrequency.ToDisplayString()}",
      { } source => source.Name,
      _ => "nothing",
    };
  }
```

> ⚠ **`PublishCommitting` passes no duration on purpose.** State D has no timeout: the card stays up
> until the switch succeeds or fails, because a spinner that quietly disappears while a Bluetooth
> connection is still being attempted is worse than no spinner. `null` means "use `ENC-4`'s default"
> on the Web side, which would time out — **so `CommitAsync` must always publish a terminal phase**
> (preview on success, failed on failure) on every path out, including the exception path. The tests
> at Task 16 cases 14, 16 and 17 are what hold that.

**The 4 s idle dismiss.** `EncoderHudService` on the Web already dismisses on `DurationMs`
(Task 11), so the *card* goes away on its own. The API-side `_state.IsOpen` must follow it or the
next press would commit into an overlay the user can no longer see. Arm a `TimeProvider` timer for
`SelectorIdleDismissMs` on every preview publish, cancelled by a commit, calling `Dismiss()`.
Use `ITimer` and `TimeProvider` exactly as `EncoderFeedbackService` does so the tests can drive it
with `FakeTimeProvider`.

**Register** in `AudioServiceExtensions.AddRotaryEncoders`, as an **explicit factory** modelled on
the router's own registration immediately below it:

```csharp
    // ENC-5. Singleton: one physical knob, one preview state, and the router that drives it is a
    // singleton.
    //
    // Built by a factory rather than by constructor injection so the two Func<> arguments defer
    // their resolution, exactly as the router defers IAudioManager. That is what keeps
    // RotaryEncoderRegistrationTests' deliberately minimal provider - AddLogging plus
    // AddRotaryEncoders and nothing else - able to resolve the router without building the audio
    // graph or the configuration store.
    services.AddSingleton<SourceSelectorService>(sp => new SourceSelectorService(
      sp.GetRequiredService<ILogger<SourceSelectorService>>(),
      () => sp.GetRequiredService<IAudioManager>(),
      () => sp.GetRequiredService<IRadioBandMemory>(),
      sp.GetRequiredService<IEncoderFeedbackSink>()));
```

and add the router's new constructor argument to the factory below it:
`sourceSelector: sp.GetRequiredService<SourceSelectorService>()`.

**Extend** `tests/Radio.Infrastructure.Tests/DependencyInjection/RotaryEncoderRegistrationTests.cs`
with `AddRotaryEncoders_ResolvesTheSourceSelector`, and **leave
`AddRotaryEncoders_ResolvesTheActionRouter` green with its existing minimal provider** — if that test
needs new registrations to pass, the deferral above was not applied and the failure would reappear at
service start on the appliance, which is what that test exists to prevent.

---

### Phase 3 — the router remap

#### Task 7 — Thread the index, remap the table, retire the cycler

**Why:** §0.3 in full.

**Edit** `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs`.

**7a. Replace the class XML doc's mapping paragraph** (`ENC-4` wrote the one being replaced):

```csharp
/// <summary>
/// Maps rotary encoder events to audio actions.
///
/// <para>
/// <b>Index mapping: 0 = Volume, 1 = Source, 2 = Visualization, 3 = Tuning.</b> The cabinet reads
/// VOLUME / SOURCE / PRESETS / TUNING, so three of the four now match the engraving. Index 2 does
/// not: it holds the visualiser as a seat-warmer until ENC-7 puts PRESETS there. Leaving the old
/// source cycler on index 2 instead would have given two adjacent knobs two divergent copies of the
/// source selection, which is worse than a knob that does something harmless and unlabelled.
/// </para>
///
/// <para>
/// The HUD's geometry keys off the encoder index the event arrived on, not off this table, so a
/// card always appears above the knob that was turned.
/// </para>
///
/// Uses Func&lt;IAudioManager&gt; for deferred resolution to break circular DI.
/// </summary>
```

**7b. Thread the index through every handler.** Every `Handle*Turn` gains a leading `int index`
parameter and every `Handle*Press` gains one; every `PublishHud(<literal>, …)` inside them becomes
`PublishHud(index, …)`. The dispatch tables become:

```csharp
      switch (e.EncoderIndex)
      {
        case 0: HandleVolumeTurn(e.EncoderIndex, e.Delta); break;
        case 1: HandleSourceTurn(e.EncoderIndex, e.Delta); break;
        case 2: HandleVizTurn(e.EncoderIndex, e.Delta); break;
        case 3: HandleTuningTurn(e.EncoderIndex, e.Delta); break;
      }
```

```csharp
      switch (index)
      {
        case 0: HandleVolumePress(); break;
        case 1: HandleSourcePress(); break;
        case 2: HandleVizPress(); break;
        case 3: HandleTuningPress(); break;
      }
```

⚠ **Passing `e.EncoderIndex` rather than the `case` literal is the point.** A literal reproduces the
bug this task exists to remove, one remap later.

**7c. Delete the old cycler.** Remove `PrimarySourceTypes`, `_currentSourceIndex` and
`SwitchSourceAsync`; `HandleSourceTurn` / `HandleSourcePress` become:

```csharp
  // --- Encoder 1: SOURCE ---

  private void HandleSourceTurn(int index, int delta)
  {
    // ENC-3 clamp: one detent, one entry, always. Acceleration is disabled on this encoder in the
    // device configuration too, so this bounds the window before a configuration push is verified
    // rather than a value the device would normally send.
    _sourceSelector.EncoderIndex = index;
    _sourceSelector.Turn(Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp));
  }

  private void HandleSourcePress()
  {
    _sourceSelector.Press();
  }
```

Constructor gains `SourceSelectorService sourceSelector`; `AddRotaryEncoders`'s router factory gains
`sourceSelector: sp.GetRequiredService<SourceSelectorService>()`.

**7d. Tuning keeps its HUD publish but on its real index.** Inside `StepRadioFrequencyAsync`'s `try`
(`ENC-4` Task 5's block), the publish becomes `PublishHud(index, "TUNING", …)` — so
`StepRadioFrequencyAsync` gains an `int index` parameter and `HandleTuningTurn` passes it through.
The non-radio `TRACK` branch does the same.

**7e. `PublishHold`'s guard stays `index == 0`.** VOLUME is still the only wired long-press
consumer; `ENC-7` adds index 2. Leave `ENC-4`'s comment in place — it is still true — but correct its
second sentence, which currently says *"encoder 2 still drives the source handler under the
pre-ENC-5 index mapping"*. After this task encoder 2 drives the **visualiser**. Rewrite it to say so.

> ⚠ This is a live example of the pre-merge rule. The comment describes a mapping this task changes;
> leaving it is shipping a comment that asserts something the code no longer does.

**7f. Tear the overlay down on disconnect.** `ENC-0`'s notification policy requires that a
mid-session disappearance *"dismiss any open overlay without committing"* (punch list §3.0, handoff
§15). Subscribe to `_encoderService.ConnectionChanged` in the constructor and, on
`IsConnected == false`, call `_sourceSelector.Dismiss()`; unsubscribe in `Dispose`.

---

#### Task 8 — Rewrite the mapping tests

**Why:** §0.3. `ENC-4` pinned the old table on purpose so that changing it would be deliberate. This
is the deliberate change, and the pin has to move with it.

**Edit** `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs`.

**These are the tests as shipped in `ENC-4`, and this is exactly which ones move.** Named rather
than described, because guessing here is what costs a day:

| Shipped test | After the remap | Do |
|---|---|---|
| `EncoderIndexZero_IsVolume_UnderBothTheOldAndTheNewPhysicalOrder` | still passes | **keep unchanged** — the one index that must never move |
| `TurnOnEachIndex_PublishesThePreEnc5HandlerLabels` | **red** — it asserts `["VOLUME", "TRACK", "SOURCE", "VISUALIZER"]` and its own name says `PreEnc5` | **replace** with the `[Theory]` below |
| `TuningTurnOnANonRadioSource_PublishesACardThatSaysWhatItDidNotDo` | **red** — it raises the turn on **index 1**, which is now SOURCE | **move to index 3**; the assertions are otherwise unchanged |
| `SourceTurn_PublishesTheSelectionWithoutSwitchingToIt` | **red** — it raises the turn on **index 2**, which is now the visualiser | **move to index 1**, and expect `Phase == SelectorPreview` rather than `Value`. `Assert.Empty(h.Audio.GetOrCreateCalls)` is the assertion that matters and it stays |
| `SelectorLongPress_DoesNothing` | passes (index 2 is the visualiser, still no long action) | **leave it** — `ENC-7` moves it to index 1 when index 2 gains a long action |
| `HoldStart_IsPublishedForVolumeOnly` | passes | **leave it** — `ENC-7` changes it |
| every `Volume*` test, `WakeConsumesThePressEdge_AndTheReleaseDoesNotFireTheShortAction` | pass | **leave them** — index 0 does not move |

Replace `TurnOnEachIndex_PublishesThePreEnc5HandlerLabels` with a table that states the new mapping
and names the row that will change it next:

```csharp
  /// <summary>
  /// Pins the index-to-handler mapping after ENC-5's remap.
  ///
  /// <para>
  /// Index 2 is the odd one out and it is expected to change once more: ENC-7 replaces the
  /// visualiser there with PRESETS. When that lands, this row's expectation moves with it — a red
  /// assertion here means the mapping changed, which is either ENC-7 or a mistake, and the diff says
  /// which.
  /// </para>
  /// </summary>
  [Theory]
  [InlineData(0, "VOLUME")]
  [InlineData(1, "SOURCE")]
  [InlineData(2, "VISUALIZER")]
  [InlineData(3, "TUNING")]
  public void EncoderTurn_PublishesACardLabelledForThatKnob(int index, string expectedLabel)
  {
    var sink = new RecordingFeedbackSink();
    var router = BuildRouter(sink);

    RaiseTurn(router, index, delta: 1);

    var card = Assert.Single(sink.Published);
    Assert.Equal(index, card.EncoderIndex);
    Assert.Equal(expectedLabel, card.Label);
  }
```

Plus:

1. `SourceTurn_PublishesInItsOwnQuarter_NotTheOldSourceIndex` — asserts `EncoderIndex == 1`. This is
   the regression guard for §0.3's hard-coded-literal trap; name it so the next reader knows.
2. `TuningTurn_PublishesInItsOwnQuarter` — asserts `EncoderIndex == 3`.
3. `SourceTurn_DoesNotSwitchAnySource` — the preview-not-commit rule, at the router level.
4. `SourcePress_WithOverlayClosed_OpensWithoutSwitching`.
5. `SourceLongPress_DoesNothing` — SOURCE has no long action (handoff §4.4) and must not publish a
   `HoldStart`, because a ring that fills and does nothing is a promise the code does not keep.

---

### Phase 4 — API broadcast

#### Task 9 — Carry the selector fields over the wire

**Edit** `src/Radio.API/Services/AudioStateUpdateService.cs`, `OnEncoderHudChanged` (added by `ENC-4`
Task 9). Extend the anonymous payload:

```csharp
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
        // ENC-5 selector payload. Null on every non-selector phase, so a volume card costs the same
        // bytes it did before.
        e.DurationMs,
        e.Title,
        e.TitleSuffix,
        e.Footer,
        e.EmptyPrimary,
        e.EmptySecondary,
        e.HighlightIndex,
        Rows = e.Rows?.Select(r => new
        {
          r.Id,
          r.Primary,
          r.Secondary,
          r.Ordinal,
          r.Icon,
          r.AccentVar,
          r.IsCurrent,
          r.IsAvailable,
          r.UnavailableReason,
        }),
      });
```

⚠ **Still no per-broadcast log line.** `ENC-4`'s note applies unchanged and matters more here: at
20 Hz with a seven-row list, a `LogDebug` would put both the rate and the payload into the log on a
box where log volume correlates with audible distortion.

**Verify:** `dotnet test --configuration Release` — `Radio.API.Tests` green.

---

### Phase 5 — Web: transport, state, and the shared component

#### Task 10 — DTO

**Edit** `src/Radio.Web/Models/ApiModels.cs`, beside `EncoderHudDto` (added by `ENC-4` Task 10):

```csharp
/// <summary>
/// One row of a selector overlay, as it crosses the wire (ENC-5).
///
/// <para>
/// A flat presentation record with no behaviour. <see cref="Id"/> is opaque to the Web — the API
/// decides what committing it does — and is used here only as the Blazor <c>@key</c>.
/// </para>
/// </summary>
public class EncoderSelectorRowDto
{
  public string Id { get; set; } = string.Empty;
  public string Primary { get; set; } = string.Empty;
  public string? Secondary { get; set; }
  public string? Ordinal { get; set; }
  public string? Icon { get; set; }
  public string? AccentVar { get; set; }
  public bool IsCurrent { get; set; }
  public bool IsAvailable { get; set; } = true;
  public string? UnavailableReason { get; set; }
}
```

and extend `EncoderHudDto` with `int? DurationMs`, `List<EncoderSelectorRowDto>? Rows`,
`int HighlightIndex` (default `-1`), `string? Title`, `string? TitleSuffix`, `string? Footer`,
`string? EmptyPrimary`, `string? EmptySecondary`.

No change to `AudioStateHubService` — `ENC-4` Task 10 already registers `.On<EncoderHudDto>` for
`"EncoderHudChanged"`, and the new members deserialize into it by name.

---

#### Task 11 — `EncoderHudService` honours `DurationMs`

**Why:** §0.4 D-3.

**Edit** `src/Radio.Web/Services/EncoderHudService.cs` (created by `ENC-4` Task 11). One method:

```csharp
  private void ArmDismissLocked()
  {
    CancelTimerLocked();

    // ENC-5. The payload carries how long to hold, because the handoff specifies four different
    // durations across the value card, the blocked flash, the save notice and the selector's idle
    // dismiss. Null keeps ENC-4's default.
    int holdMs = Current?.DurationMs ?? EncoderInteractionTimings.HudHoldMs;

    _dismissTimer = _timeProvider.CreateTimer(
      _ => Dismiss(),
      null,
      TimeSpan.FromMilliseconds(holdMs),
      Timeout.InfiniteTimeSpan);
  }
```

⚠ **`ArmDismissLocked` is called after `Current` is assigned** in `Publish`. Confirm that ordering
before relying on it; if the assignment moves, this reads the previous card's duration.

**Tests** — extend `tests/Radio.Web.Tests/Services/EncoderHudServiceTests.cs`:

1. `SelectorPreview_HoldsForItsOwnDuration_NotTheDefault` — publish with `DurationMs = 4000`,
   advance 1600 ms ⇒ still showing; advance to 4100 ⇒ cleared.
2. `NullDuration_FallsBackToTheDefaultHold`
3. `ANewPublishReArmsWithTheNewDuration` — a 4000 ms preview followed by a 1500 ms value card
   dismisses at 1500, not 4000.

---

#### Task 12 — `EncoderSelectorOverlay.razor` — **the shared component**

**Why:** §1.2. This is the artefact the punch list means by *"one component with two lists"*.
`ENC-7` adds a caller and changes nothing here.

**Create** `src/Radio.Web/Components/Shared/EncoderSelectorOverlay.razor`.

Requirements it must satisfy — Builder writes the markup:

- **Parameters, and only these.** The component knows nothing about sources, bands or presets:
  ```csharp
    [Parameter, EditorRequired] public EncoderHudDto Hud { get; set; } = default!;
  ```
  Everything it renders comes off that one payload. Resist adding a `ListKind` parameter — the moment
  the component branches on which list it is showing, "one component, two lists" has become two
  components sharing a file.
- **Root:** `<div class="encoder-selector-overlay" role="status" aria-live="polite"
  aria-atomic="true">`. No `left` style — it is centred by CSS. `pointer-events: none` from CSS
  (§1.3).
- **Heading row:** `Hud.Title` left, `Hud.TitleSuffix` right when non-null, in the
  `.sleep-screen-hint` treatment (mono 11 px, uppercase, `0.20em`, `--text-low`) — the same call
  `ENC-4` §0.3 D-2 made, for the same reason.
- **Empty state:** when `Hud.Rows` is null or empty, render `Hud.EmptyPrimary` and
  `Hud.EmptySecondary` in place of the list and **omit the footer**. SOURCE never reaches this;
  `ENC-7`'s empty bank is the whole point of it existing here (handoff §6.6 State B).
- **Windowing** (§0.4 D-5): render at most `EncoderInteractionTimings.SelectorVisibleRows` rows,
  as a window that keeps the highlight visible. Write it as a small pure helper so it is testable
  from bUnit without a hub:
  ```csharp
    /// <summary>
    /// First row index of the visible window: the whole list when it fits, otherwise a window of
    /// <c>SelectorVisibleRows</c> clamped to the ends with the highlight inside it.
    /// </summary>
    internal static int WindowStart(int total, int highlight, int visible)
    {
      if (total <= visible)
      {
        return 0;
      }

      int start = highlight - (visible / 2);
      return Math.Clamp(start, 0, total - visible);
    }
  ```
- **Row rendering**, one fragment for every list:
  - `@key="row.Id"` — so a recompose does not re-create every element and restart the wrap animation.
  - `.encoder-selector-row`, plus `.is-highlighted` when the index matches `Hud.HighlightIndex`,
    plus `.is-current`, plus `.is-unavailable` when `!row.IsAvailable`, plus `.is-blocked` when
    `Hud.Phase == "SelectorBlocked"` **and** this is the highlighted row.
  - Inline `style="--row-accent: var(@(row.AccentVar ?? "--accent-primary"));"` — the same
    per-row-accent technique `SourceBubble` uses (`SourceBubble.razor:15`), so no new colour is
    introduced.
  - `row.Ordinal` in a leading cell when non-null; `row.Icon` through `<RadzenIcon>` when non-null.
  - **The unavailable idiom, reused verbatim in spirit from `SourceBubble.razor:26-29`:** the
    secondary cell renders `$"{row.Secondary} · {row.UnavailableReason}".TrimStart(' ', '·')` when
    the row is unavailable, so a row with no secondary reads `no tuner detected` rather than
    ` · no tuner detected`.
  - `row.IsCurrent` renders the `◀` marker (handoff §6.6 State A mock).
- **States D and E** (`SelectorCommitting` / `SelectorFailed`): render `Hud.PrimaryText` and
  `Hud.SecondaryText` **in place of the list**, with a spinner for `SelectorCommitting`. The card
  stays up. Handoff §6.6 is explicit that these are not optional polish.
- **`SelectorNotice`**: `Hud.PrimaryText` / `Hud.SecondaryText` in place of the list, no spinner.
  Nothing in this PR publishes it; `ENC-7` does. **Build it here anyway** — it is the fifth branch of
  a dispatch that would otherwise be rewritten by the next row.
- **Accessibility** (handoff §15): every state distinguishable without colour. A dimmed row says its
  reason in words; the highlighted row is announced by the live region; the current row's `◀` is a
  glyph, not only a colour. Give the root a text-only accessible summary of the form
  `"{Title}. {highlighted row's Primary}{, reason when unavailable}."`.
- `.snackbar-enter` on mount, exactly as `ENC-4`. **No new keyframes.**
- ⚠ **The 200 ms bottom→top wrap animation** (handoff §4.4: "so it reads as a wrap rather than a
  teleport") is a CSS class toggled when the highlight index jumps from the last row to the first or
  the reverse. Detect it in `OnParametersSet` by remembering the previous index. If Builder finds
  this fights the `@key`ed rows, **raise it rather than dropping it silently** — it is a named
  requirement, and dropping it should be a recorded decision, not an omission.

---

#### Task 13 — `EncoderHud.razor` dispatches; Sleep collapses the overlay

**Why:** §0.4 D-4, and handoff §8.3.

**Edit** `src/Radio.Web/Components/Shared/EncoderHud.razor` (created by `ENC-4` Task 12).

**13a.** Add a phase classifier and branch the root:

```csharp
  /// <summary>
  /// True for the phases that render as a centred selection overlay rather than a quartered card.
  ///
  /// <para>
  /// Handoff §6.2 draws the line: "transient readouts appear above the knob that produced them.
  /// Selection overlays center." The quarter geometry is what makes a readout legible at a glance;
  /// a list is read, not glanced at, and a 440 px list pinned under one knob would run off the
  /// edge for encoder 0.
  /// </para>
  /// </summary>
  private static bool IsSelectorPhase(string? phase) =>
    phase is "SelectorPreview" or "SelectorBlocked" or "SelectorCommitting"
      or "SelectorFailed" or "SelectorNotice";
```

The root becomes: for a selector phase in the `Normal` variant, `<EncoderSelectorOverlay Hud="..."/>`
with **no `left` style**; otherwise `ENC-4`'s quartered card unchanged. The unknown-phase rule from
`ENC-4` §2.5 item 2 stays: an unrecognised phase renders nothing.

**13b. The Sleep variant collapses a selector to a value line.** Handoff §8.3: a consumed wake input
*"shows what is currently selected (`SOURCE · FM`), **not the full overlay**"*, and `ENC-4` Task 15
mounts the Sleep HUD **inside `.sleep-screen-drift`**, the anti-burn-in wrapper. A 440 px list with
its own border inside that wrapper is exactly the fixed bright composition the wrapper exists to
prevent.

```razor
  @* ENC-5. On the sleep screen a selector renders as one dim line, not the overlay.
     Two reasons, and the second is the load-bearing one: handoff §8.3 specifies "SOURCE · FM, not
     the full overlay" for a consumed input, and this host is inside the anti-burn-in drift wrapper,
     which a bordered 440px panel would defeat. *@
```

Render `$"{Hud.Current.Title} · {highlighted row's Primary}"` in the Sleep variant's existing
single-emissive-colour treatment.

> ⚠ **This path is reachable today**, and it is not hypothetical. `ENC-4` §0.4: `/sleep` reached by
> the idle timer has `SleepService.IsSleeping == false`, so `TryWakeFromSleep` returns false and a
> SOURCE turn acts and renders in place. Reached by the Sleep pill it is consumed by the wake.
> Both are correct pre-`ENC-6` behaviour; only the idle path exercises this branch. UAT F says so.

---

#### Task 14 — CSS

**Edit** `src/Radio.Web/wwwroot/css/design-system.css`. Append a section after `ENC-4`'s, in the
file's banner-comment style. **No new custom properties** (§0.2 item 3).

```css
/* ─── ENC-5  Encoder selector overlay ───────────────────────────────────────
 *
 * The SOURCE and PRESETS lists, one component. Centred rather than quartered:
 * handoff §6.2 puts transient readouts above their knob and selection lists in
 * the middle, because a list is read rather than glanced at.
 *
 * No --hud-* / --selector-* custom properties, per §6.9. The 12px radius is a
 * literal because this project has no --radius-* scale, and 12px is the value
 * §6.6 specifies for this surface (the HUD card's 10px matches .nav-pill; this
 * one is deliberately one step larger).
 * ─────────────────────────────────────────────────────────────────────────── */

.encoder-selector-overlay {
  position: fixed;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  width: 440px;
  z-index: 10000;                 /* one tier above the gain-popover backdrop (9999) */
  /* A readout, not a control. Every function it offers has a touch equivalent
     (the topbar source strip, the band pills), and a 440px transparent shield
     over the middle of Home would eat taps meant for the panel underneath. */
  pointer-events: none;
  background: var(--surface-overlay);
  backdrop-filter: blur(12px);
  border: 1px solid var(--surface-separator);
  border-radius: 12px;
  padding: var(--sp-3);
}
```

Remaining rules Builder writes to the same discipline:

| Selector | Must express |
|---|---|
| `.encoder-selector-title` | mono 11 px, uppercase, `letter-spacing: 0.20em`, `--text-low` — matching `.sleep-screen-hint` (`:2934-2943`), **not** `.rcp-presets-hint`, per `ENC-4` §0.3 D-2 |
| `.encoder-selector-row` | 48 px tall for source rows / 44 px for preset rows via a modifier; grid `22px 28px 1fr auto 16px` (ordinal · glyph · text · secondary · marker), echoing `.rcp-preset-*`'s `22px 1fr auto 24px` (`:4277`) |
| `.encoder-selector-row.is-highlighted` | 2 px left bar in `var(--row-accent)` + `--surface-hover` background (handoff §6.6 State A) |
| `.encoder-selector-row.is-current` | the `◀` marker cell visible |
| `.encoder-selector-row.is-unavailable` | `opacity: 0.4` — the same value `.source-bubble.is-disabled` uses (`:2033`) |
| `.encoder-selector-row.is-blocked` | border flashes `--signal-amber` (State C) |
| `.encoder-selector-footer` | same treatment as the title row, top border `--surface-separator` |
| `.encoder-selector-empty` | `--text-low`, centred, two lines (State B) |
| `.encoder-selector-wrap-in` | the 200 ms bottom→top highlight animation, `--anim-ease-emphasized` |
| `@media (prefers-reduced-motion: reduce)` | enter/exit instant, wrap animation none — matching `RdsScrollMarquee` and `.sleep-screen-drift` (handoff §6.5) |

---

### Phase 6 — the band pills

#### Task 15 — Pin the one-state rule rather than rebuild it

**Why:** §0.5 finding 1. The pills already follow `AudioStateHubService.RadioStateChanged`
(`RadioControlPanel.razor:6, :981, :1047, :1626`), so the D7 requirement 4 is **met today** and the
correct action is to stop it regressing, not to re-plumb it.

**Create** `tests/Radio.Web.Tests/Components/Shared/RadioControlPanelBandSyncTests.cs`:

1. `BandPills_FollowARadioStateBroadcast_WithoutAClick` — render with band FM, raise a
   `RadioStateChanged` carrying AM, assert `.rcp-band-active` moved to the AM pill. **This is the
   regression guard for a band committed from the SOURCE knob.**
2. `BandPills_DoNotHoldTheirOwnBandField` — a reflection guard: assert `RadioControlPanel` declares
   no private field whose name contains `band` and whose type is `string` other than the documented
   gesture fields (`_bandPointerDownBand`). Name it so its purpose is legible; a plain "no local
   copy" assertion is the kind of test that gets deleted for looking pointless.

**Edit** `RadioControlPanel.razor:1092-1096` — add the missing rollback. The optimistic write
`_radioState = _radioState with { Band = newBand }` is not undone when `SetBandAsync` returns false,
so a failed band change leaves the pill lying until the next 500 ms broadcast corrects it. One line
and a comment that says only what it does:

```csharp
      // The optimistic band above is corrected by the next RadioStateChanged tick either way; this
      // just makes a failed call correct itself now rather than up to 500 ms later.
      if (!ok && previous is not null) { _radioState = previous; }
```

⚠ **Do not describe this as "keeping the pills and the knob in sync".** It does not do that — the
broadcast does. It shortens one window.

**A latency this row accepts, and states rather than hides.** A band committed on the knob reaches
the pills through `AudioStateUpdateService`'s **500 ms** poller
(`AudioStateUpdateService.UpdateIntervalMs = 500`, `CheckRadioStateAsync:428`). The *audio* changes
immediately and the *overlay* updates immediately; the on-screen pill follows within half a second.
That is acceptable — it is one poll tick on a surface the user is not looking at while their hand is
on a knob — and building a push path for it would mean a second broadcast channel for radio state.
**UAT E4 bounds it.** If it reads badly on the box, the fix is a nudge into the existing poller, not
a new event.

---

### Phase 7 — tests and docs

#### Task 16 — `SourceSelectorService` tests

**Create** `tests/Radio.Infrastructure.Tests/Platform/Input/SourceSelectorServiceTests.cs` with
`FakeTimeProvider`, a fake `IAudioManager`, a fake `IRadioControl`, a fake `IRadioBandMemory` and a
recording `IEncoderFeedbackSink`.

| # | Test | Pins |
|---|---|---|
| 1 | `Turn_OpensTheOverlayAndMovesOneEntry` | §4.4 |
| 2 | `Turn_SwitchesNothing` | the preview rule — the fake manager records zero `GetOrCreateSourceAsync` calls |
| 3 | `Composition_PutsFmFirstAndAmSecond` | not `Enum.GetValues` order — `RadioBand` declares AM first |
| 4 | `Composition_OmitsSw_WhenTheTunerDoesNotSupportIt` | D-1 |
| 5 | `Composition_IncludesSwAtPositionThree_WhenSupported` | handoff §4.4's table |
| 6 | `Composition_IsStableAcrossReopens_EvenWhenAvailabilityChanges` | "positions never move" |
| 7 | `Composition_WithNoTuner_RendersFmAndAmDimmedWithAReason` | State B |
| 8 | `CurrentMarker_TracksTheActiveBand_NotTheWordRadio` | **D7 requirement 3 — on AM, row 2 is marked** |
| 9 | `CommitBand_WhileRadioIsActive_CallsSetBandAndNeverCreatesASource` | **D7 requirement 1** |
| 10 | `CommitBand_WhileRadioIsActive_PublishesNoCommittingPhase` | no spinner on a band change |
| 11 | `CommitBand_RestoresThatBandsLastTunedFrequency` | |
| 12 | `CommitBand_FallsBackToTheBandDefault_WhenNothingIsRemembered` | |
| 13 | `CommitBand_FromAnotherSource_ActivatesRadioThenSetsBandThenFrequency` | **D7 requirement 2**, asserted in order |
| 14 | `CommitBand_FromAnotherSource_PublishesCommittingThenPreview` | State D |
| 15 | `CommitBand_OnATunerThatIgnoresIt_DoesNotClaimSuccess` | the RF320 no-op |
| 16 | `CommitSource_ThatReturnsNull_PublishesFailed` | State E |
| 17 | `CommitSource_ThatThrows_PublishesFailed_AndDoesNotRethrow` | |
| 18 | `PressOnAnUnavailableRow_PublishesBlocked_AndLeavesTheOverlayOpen` | **State C — never a silent no-op** |
| 19 | `PressWithTheOverlayClosed_OpensIt_AndCommitsNothing` | the one-rule press / free mis-grab |
| 20 | `IdleForFourSeconds_ClosesWithoutCommitting` | |
| 21 | `Dismiss_ClosesWithoutCommitting` | the `ENC-0` disconnect teardown |
| 22 | `RowIconsAndAccents_MatchSourceTypeHelper` | the four string pairs Task 6's note flags. Assert against literals with the helper's file path in the message, since the projects cannot reference each other |

---

#### Task 17 — bUnit tests for the overlay

**Create** `tests/Radio.Web.Tests/Components/Shared/EncoderSelectorOverlayTests.cs`
(`JSInterop.Mode = JSRuntimeMode.Loose`, `Services.AddRadzenComponents()`).

1. `RendersTitleAndFooter`
2. `HighlightedRow_CarriesTheHighlightClass`
3. `CurrentRow_CarriesTheCurrentClass_IndependentlyOfTheHighlight`
4. `UnavailableRow_IsDimmedAndStatesItsReason` — asserts the rendered text contains the reason,
   because "dimmed with a reason" is the requirement and dimming alone is not it
5. `UnavailableRow_WithNoSecondary_RendersTheReasonWithoutALeadingSeparator` — the
   `SourceBubble.TrimStart(' ', '·')` idiom
6. `BlockedPhase_FlashesOnlyTheHighlightedRow`
7. `CommittingPhase_ReplacesTheListWithASpinnerAndStaysUp`
8. `FailedPhase_ShowsTheReasonAndWhatIsStillPlaying`
9. `EmptyRows_RenderTheInstructionalEmptyState_AndOmitTheFooter`
10. `[Theory] WindowStart_KeepsTheHighlightVisible` — 0/3/6 of 7 ⇒ start 0; 0/25/49 of 50 ⇒ 0/22/43
11. `MoreThanSevenRows_RendersExactlySeven`
12. `Overlay_IsNotClickable` — asserts the computed style carries `pointer-events: none`, or in
    bUnit's absence of layout, that the root's class is the one the stylesheet declares it on and
    that no row renders a `<button>`. **Assert the second form; do not fake the first.**
13. `UnknownPhase_RendersNothing` — the forward-compatibility rule (`ENC-4` §2.5 item 2)
14. `SleepVariant_CollapsesASelectorToOneLine` — in `EncoderHudTests.cs`, against `EncoderHud`

---

#### Task 18 — Docs

1. **`design/INTEGRATIONS.md` § 1 Rotary Encoders.** Three things are wrong there and this PR makes
   two of them worse if left:
   - `:11` — *"Four encoders control Volume, Tuning, Source selection, and Visualization mode."*
     Rewrite for `VOLUME · SOURCE · PRESETS · TUNING`, noting that PRESETS lands in `ENC-7` and that
     index 2 currently holds the visualiser.
   - `:28-30` — the **Encoder Mapping** table. Replace with the post-`ENC-5` table from §0.3, and add
     the press column.
   - `:24-25` — the report format is still described as *"Bytes 1–4: signed encoder deltas (sbyte
     per encoder)"*. **That is the pre-`ENC-1` protocol and it has been wrong since #498.** Correct
     it to the 37-byte report while the file is open; it is three lines and it is actively
     misleading.
   - `:125` — *"If clockwise decreases volume, swap the A/B encoder pins on the Pico, or negate the
     delta in firmware."* Wrong since `ENC-2`: there is a `reverse` flag in the pushed configuration
     and it is the one field a human should edit (handoff §5.2, §12.2). Replace the advice.
2. **`docs/HANDOFF-GA-PUNCH-LIST.md`** — mark `ENC-5` shipped with the PR number, and add the five
   §0.4 deviations plus the two §0.5 findings as a note under the row. **Specifically record that
   `ENC-9a` did not remove `VisualizerPanel`'s local `_currentMode`**, since the row's own text cites
   it as the template for something it did not do.
3. **`docs/HANDOFF-NEXT-SESSION.md`** — the "Known mismatch, deliberate" section is now half true.
   Rewrite it to the post-`ENC-5` state and say that `ENC-7` closes index 2.
4. **`design/FUTURE-WORK.md`** — record the two things this row found and deliberately did not fix:
   `RadioApiService.GetPowerStateAsync` deserializes a DTO against an endpoint returning a bare
   `bool` (always null), and `RadioApiService.SetEqualizerAsync` posts `{ preset }` against a server
   binding `Mode` (always empty). Neither is in this row's path; both are real.

---

## 3. Test Plan

### 3.1 Automated gates

```bash
dotnet build --configuration Release          # 0 warnings — warnings are errors in Release
dotnet test  --configuration Release
```

New tests by project:

| Project | File | Count |
|---|---|---|
| `Radio.Infrastructure.Tests` | `EncoderSelectorStateTests.cs` | 9 |
| `Radio.Infrastructure.Tests` | `SourceSelectorServiceTests.cs` | 22 |
| `Radio.Infrastructure.Tests` | `RadioBandMemoryServiceTests.cs` | 7 |
| `Radio.Infrastructure.Tests` | `RadioControlCapabilityTests.cs` | 2 |
| `Radio.Infrastructure.Tests` | `EncoderFeedbackServiceTests.cs` (extended) | +3 |
| `Radio.Infrastructure.Tests` | `RotaryEncoderRouterMappingTests.cs` (**rewritten**) | ~10 |
| `Radio.Web.Tests` | `EncoderSelectorOverlayTests.cs` | 13 |
| `Radio.Web.Tests` | `EncoderHudTests.cs` (extended) | +1 |
| `Radio.Web.Tests` | `EncoderHudServiceTests.cs` (extended) | +3 |
| `Radio.Web.Tests` | `RadioControlPanelBandSyncTests.cs` | 2 |

⚠ **Expect `ENC-4`'s mapping tests to fail before Task 8 is done.** That is the remap landing, not a
regression. Task 8 is not optional cleanup — it is the assertion moving with the decision.

### 3.2 Deploy

```powershell
./deploy/Deploy-ToLinux.ps1
```

No flags — `OPS-1` fixed the defaults, and the deploy verifies both services by SHA. A
`WARNING: 0 established connections to :5002` means the binaries landed and the browser did not come
back; recover with `ssh mmack@radio '/usr/local/bin/radio-kiosk-launch'`.

⚠ `journalctl` carries WARNING and above only since `LOG-11`; Information lines are in
`/opt/radio-console/logs/radio-*.txt`. Keep log reads bounded — heavy `journalctl` on this box
correlates with audio distortion, which would contaminate §3.3 H.

### 3.3 Browser UAT — Tester drives these on the box at 1920×720

Prerequisite: encoder connected and `Configured` on the System Config status card; a tuner present;
one Bluetooth device paired but **not** connected (needed for D and E).

**A · The remap — the knobs finally say what they do**

| # | Steps | Expected |
|---|---|---|
| A1 | Turn knob 1 (far left, VOLUME) | Volume card in the far-left quarter (≈240 px). Volume changes |
| A2 | Turn knob 2 (SOURCE) | **The SOURCE overlay opens, centred** — not a quartered card |
| A3 | Turn knob 3 (PRESETS) | A `VISUALIZER` card at ≈1200 px, and the on-screen visualiser mode changes. **Expected — index 2 is `ENC-7`'s** |
| A4 | Turn knob 4 (TUNING) | A `TUNING` card at ≈1680 px showing the frequency. The station changes |
| A5 | Turn knob 4 fast for ~1 s on FM | Frequency moves in large steps — acceleration, live for the first time. No audible distortion, no stall |
| A6 | Turn knob 2 fast | The highlight moves **exactly one entry per detent** at every speed |

**B · The overlay — States A and B**

| # | Steps | Expected |
|---|---|---|
| B1 | With FM playing, turn SOURCE one detent | Overlay opens centred, ~440 px, blurred; the highlight starts on **FM** and moves one row |
| B2 | Read the rows | `FM`, `AM`, then `SW`/`WB` if the tuner reports them, then `BLUETOOTH`, `PHONO`, `USB`, `FILES` — in that order |
| B3 | Confirm the current marker | On **FM**, the FM row carries the `◀` |
| B4 | Switch to AM from the on-screen pills, reopen the overlay | The marker is on **row 2**, not row 1. *(This one check is the whole of D7 requirement 3)* |
| B5 | Spin past the last row | Wraps to the top, animating bottom→top rather than teleporting |
| B6 | Stop and wait 4 s | The overlay dismisses. **Nothing has changed** |
| B7 | Find the Bluetooth row with no phone connected | Dimmed, and it says **why** in words |

**C · Press is one rule**

| # | Steps | Expected |
|---|---|---|
| C1 | With the overlay closed, press SOURCE | It opens on the current entry. **Nothing audible happens** |
| C2 | Press again immediately | Commits the current entry — still nothing audible changes |
| C3 | Turn to a dimmed row and press | The row **flashes amber for 1.5 s and the overlay stays open**. Never a silent no-op (State C) |
| C4 | Press and hold SOURCE for 1 s | **No ring appears, nothing happens beyond C1/C2.** SOURCE has no long action |

**D · Band commit vs source switch — the D7 split**

| # | Steps | Expected |
|---|---|---|
| D1 | On FM, open the overlay, highlight `AM`, press | **Instant.** No spinner, no fade, no source teardown. Audio is AM |
| D2 | Confirm the frequency | AM lands on **AM's last-tuned frequency**, not on 1710 kHz and not on the FM number clamped |
| D3 | Go back to FM the same way | FM returns to the frequency it was on, not a default |
| D4 | Restart `radio-api`, then repeat D1 | The remembered frequencies survive — the memory is persisted, not in-process |
| D5 | Switch to Bluetooth, then commit `FM` from the overlay | **A real source switch:** spinner (State D), then FM plays at its remembered frequency |
| D6 | Time D1 and D5 side by side | D1 is perceptibly instant; D5 is not. If they feel the same, the band-change path is teardown-and-rebuild and requirement 1 is not met |

**E · States D and E, and the pills**

| # | Steps | Expected |
|---|---|---|
| E1 | With the phone out of range, commit `BLUETOOTH` | Spinner, card stays up, then **`Bluetooth unavailable / Staying on FM 98.5`** for 4 s (State E) |
| E2 | Confirm what is playing after E1 | FM, uninterrupted. The failure did not stop the old source |
| E3 | Commit `AM` from the knob while `RadioControlPanel` is on screen | The **on-screen band pill moves to AM** without a tap |
| E4 | Time E3 | Within ~500 ms of the commit. Longer than that means the broadcast path is not carrying it |
| E5 | Tap the `FM` pill on screen, then open the overlay from the knob | The overlay's marker is on **FM**. Sync is both ways |

**F · The sleep host — read `ENC-4` §0.4 first**

| # | Steps | Expected |
|---|---|---|
| F1 | Navigate the kiosk **directly to `/sleep`** (do **not** press the Sleep pill) | Clock composition |
| F2 | Turn SOURCE | **One dim line — `SOURCE · FM`.** Not the overlay, no border, no accent colours |
| F3 | Wait; then reach `/sleep` via the **Sleep pill** and turn SOURCE | The console wakes and navigates home — expected, pre-`ENC-6`. Not a defect |

**G · Stacking, pointer-through, teardown**

| # | Steps | Expected |
|---|---|---|
| G1 | Open the overlay over `RadioControlPanel` | It renders **above** the panel |
| G2 | Open the gain popover, then turn SOURCE | The overlay renders above the gain backdrop |
| G3 | While the overlay is up, tap a control underneath it | The tap reaches the control. The overlay never eats a touch |
| G4 | Open the overlay, then **unplug the encoder** | The overlay **dismisses without committing**, `ENC-0`'s toast fires, and nothing switched |

**H · Load**

| # | Steps | Expected |
|---|---|---|
| H1 | Play audio. Spin SOURCE continuously for 30 s | **No audible distortion** |
| H2 | Play FM. Spin TUNING hard for 30 s | **No audible distortion.** This is the first time tuning acceleration is live and each detent can be 8 awaited tuner calls |
| H3 | During H1, sample SignalR frames | `EncoderHudChanged` at **≤ 20 Hz**, and every selector frame carries a full `rows` array |
| H4 | `ssh mmack@radio "journalctl -u radio-api --since '-5min' --no-pager \| wc -l"` after H1 | No per-broadcast log spam |

**I · Accessibility**

| # | Steps | Expected |
|---|---|---|
| I1 | With AT-SPI exported (see `CLAUDE.md`), open the overlay and dump the Chrome tree | The overlay's text is present as a live region, including the highlighted row |
| I2 | Greyscale screenshot with a dimmed row visible | The dimmed row is identifiable from **text**, not only from opacity |
| I3 | `prefers-reduced-motion: reduce` in DevTools, then spin past the wrap | The overlay appears instantly and the wrap does not animate |

**J · Regression**

| # | Steps | Expected |
|---|---|---|
| J1 | Volume knob: turn, press, hold to standby | `ENC-4`'s behaviour unchanged in every respect |
| J2 | Long-press a band pill; long-press a preset card | Both still open their dialogs at 600 ms |
| J3 | The topbar source strip | Still switches sources; offline bubbles still read `· offline` |
| J4 | The visualiser's six-segment picker and the System Config dropdown | Both still change the mode |

### 3.4 The four highest-weighted checks

1. **B4** — the current marker on AM. Designer: *"getting this wrong makes the knob feel like it lost
   its place."*
2. **D1 + D6** — a band commit on an active radio must be a band change, not a source switch. It is
   the difference between the feature and a re-implementation of the old cycler.
3. **E1** — State E. An overlay that dismisses into silence is what makes a person conclude the knob
   is broken and press it repeatedly, which is the input pattern that provokes the capture-lifecycle
   bug.
4. **A2 + A4** — the remap. Two knobs that have never said the right word now do.

---

## 4. Self-review

**Spec coverage** — handoff §4.4 Knob 2 and §6.6, item by item:

| Handoff item | Where |
|---|---|
| Turn opens and moves one entry, nothing switches | Tasks 5, 6, 7; UAT B1, A6, C |
| Acceleration disabled on SOURCE | `ENC-3`'s clamp, already shipped; asserted Task 8 |
| The list is a band selector (D7) | Task 6; UAT B2 |
| Band commit on active radio = band change, no fade/spinner | Task 6 `CommitAsync`; tests 9–12; UAT D1, D6 |
| Band commit from another source = full switch | Task 6; test 13; UAT D5 |
| Current marker tracks the active **band** | Task 6 `RecomposeLocked`; test 8; UAT B4 |
| Last-tuned frequency per band, falling back to the default | Task 4; tests 11–12; UAT D2, D3, D4 |
| Knob and band pills are one state | Task 15 + §0.5 finding 1; UAT E3, E5 |
| Composition once per tuner; positions never move | Task 6; tests 4–6 |
| Unavailable dimmed **with a reason**, `SourceBubble` idiom | Tasks 6, 12; tests 7, 17.4, 17.5; UAT B7, I2 |
| List wraps, 200 ms bottom→top | Tasks 5, 12; UAT B5 |
| Press commits the highlight — one rule | Task 6 `Press`; test 19; UAT C1, C2 |
| Pressing a dimmed entry flashes 1.5 s, stays open | Task 6; test 18; UAT C3 |
| No long-press on SOURCE | Task 8 test 5; UAT C4 |
| State D spinner | Tasks 6, 12; UAT D5, E1 |
| State E failure card, 4 s | Tasks 6, 12; UAT E1, E2 |
| §6.6 geometry — centred, 440 px, blur 12, 12 px radius | Task 14; UAT B1 |
| §6.5 4000 ms idle dismiss | Tasks 1, 6, 11; UAT B6 |
| §6.9 no new tokens | Tasks 12, 14 |
| §8.3 consumed input shows `SOURCE · FM`, not the overlay | Task 13; UAT F2 |
| §6.8 ≥50 ms coalescing | Task 2; UAT H3 |
| The PRESETS list | **`ENC-7`** — §1.2 |

**Placeholder scan:** no `TBD`, no "similar to Task N", no "implement later". Four places name a
decision Builder makes and state the acceptance criterion instead of faking it: the
`IConfigurationStore` method names (Task 4), the three display switches (Task 6), the wrap-animation
technique (Task 12), and the `pointer-events` assertion form (Task 17 case 12). Each says what must
be true and forbids a fake.

**Scope check:** no audio DSP; no change to `VisualizationModeService` itself (only which index calls
it); no PRESETS list, recall or save; no `MEMORY`→`PRESETS` rename (that is `ENC-7`); no wake model,
blanking or wake latch (`ENC-6`/`ENC-15`); no settings surgery (`ENC-8`); no new hub event.

**Type consistency:** `Frequency` is hertz-backed with `new Frequency(long)` as the only constructor
and `FromKilohertz` / `FromMegahertz` as the factories — the band memory stores `Hertz` and nothing
re-derives units. `RadioBand` is `Radio.Core.Models.Audio.RadioBand` (an **enum**, `AM = 0`), which
is *not* `RTLSDRCore.Models.RadioBand` (a **class**) and whose members are *not* `BandType`'s
(`SW` vs `Shortwave`, `WB` vs `Weather`) — Task 4's `BottomEdge` is the only place the two meet and
it maps explicitly. `AudioSourceType` lives in `Radio.Core.Interfaces.Audio`, not
`Radio.Core.Models.Audio`.

**Comment-accuracy scan** (the repo's hardest pre-merge rule): four comments in this plan make a
safety or scope claim, and each is checked against its own diff — the coalescer summary (Task 2), the
availability shallowness note (Task 6), the RF320 read-back rationale (Task 6 `ApplyBandAsync`), and
the rollback note (Task 15) that deliberately does **not** claim to keep the pills in sync.

---

## 5. Things this plan deliberately does not do, with the reason

1. **VHF and AIR in the overlay.** §0.4 D-2. Four bands is Designer's list; six is the pills'. The
   knob is for a guest, and a ten-row list does not fit the 600 px content area.
2. **A push path for band state.** Task 15. The 500 ms poller already carries it; a second broadcast
   channel for radio state is a bigger change than the 500 ms it saves on a surface nobody is
   watching while their hand is on a knob.
3. **A source-availability API on `IAudioManager`.** Task 6's note. There isn't one, the topbar
   derives its answer the same shallow way, and State E is the design's answer to a source that
   turns out to be unreachable. Adding a real reachability probe would mean touching every source's
   lifecycle for a row about a knob.
4. **Making the overlay tappable.** §1.3. Every function has a touch equivalent already, and a
   440 px shield over the centre of Home is a real cost.
5. **The PRESETS handler on index 2.** §0.3. `ENC-7` owns it, and putting it here would ship a preset
   write behind a knob this PR still labels VISUALIZER.
6. **Fixing `RadioApiService.GetPowerStateAsync` and `SetEqualizerAsync`.** Task 18 item 4 records
   them. Both are real pre-existing client/server contract bugs found while mapping this surface;
   neither is in this row's path, and folding unrelated fixes into a P0 encoder row is how a
   reviewable PR stops being one.
