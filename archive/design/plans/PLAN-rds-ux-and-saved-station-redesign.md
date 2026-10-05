# PLAN — RDS Scroll Stability + Duplicate RDS Removal + Saved-Station Card Redesign

> **One PR, three items + two structural changes.** Most changes are in `src/Radio.Web`
> (Blazor Server, MudBlazor/Radzen, Material 3, 2-space indent, file-scoped namespaces, nullable
> enabled, warnings-as-errors in Release); the broadcast-split touches `src/Radio.API` as well.
>
> **Branch:** work on a feature branch, e.g. `fix/rds-ux`. Never commit to `main`.
>
> **Follows:** `docs/design-handoffs/HANDOFF-saved-station-display.md` (Proposal A) for Item 4.
> The RDS scroll fix follows the root-cause analysis confirmed against the cited source below.
>
> **Deploy-before-test target:** Ubuntu x64 — `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`.
> Browser UAT at `http://radio:5002/`. Device screen is **1920×720**.
>
> **Scope note (revision 2):** the two items originally deferred are now **in scope** for this same
> PR — (A) the API broadcast split so the RDS path stops reacting to pure-telemetry ticks, and
> (B) unifying both preset renderers onto a single shared component. See the new task blocks below.
>
> **Last updated:** 2026-05-30 (rev 2)

---

## Overview

Three user-reported UX problems, batched into one reviewable PR because they all touch the
same two surfaces (`RadioControlPanel.razor` + `design-system.css`) and one adjacent surface
(`NowPlayingPanel.razor`):

1. **RDS marquee jerks / drops characters** (Items 1 + 3, same root cause). The CSS marquee
   restarts on every Blazor re-render, and re-renders fire ~twice a second because the SignalR
   broadcast lumps RDS text together with volatile signal telemetry. Fix: gate the marquee so it
   only re-renders when its composed text (or duration) actually changes.
2. **Duplicate RDS readout.** The Now-Playing status strip shows a second PS-station cell on top
   of the main `RdsCard`. Remove the redundant cell only.
3. **Saved-station cards over-truncate the name.** Redesign per Proposal A: name-primary single
   line, small de-styled mono frequency tail. Apply to **both** preset renderers.

### Root-cause confirmation (read before starting Item 1)

`src/Radio.API/Services/AudioStateUpdateService.cs:544-585` — `HasRadioStateChanged` returns
`true` whenever `sigDelta > 3` (line 558) **or** `Math.Abs(previous.RssiDbu - current.RssiDbu) > 1.8`
(line 562). On a live station these signal-telemetry fields drift every poll (~500 ms), so the hub
broadcasts a state change ~twice a second. `RadioControlPanel.HandleRadioStateChanged`
(`:1053-1070`) sets `_radioState = dto` and calls `StateHasChanged()` on every such broadcast.
That re-renders `RdsCard` → `RdsScrollMarquee`, and because the marquee's `<div class="rcp-rds-rt-track">`
is recreated/re-attributed every render (`RdsScrollMarquee.razor:27-31`, inline `--scroll-duration`
at `:29`), the CSS `@keyframes rcp-rds-rt-scroll` animation (`design-system.css:4150-4157`) restarts
from `translateX(100%)`. The marquee never completes a smooth pass → snaps to the right edge (the
"jerk") and yanks mid-flight glyphs (the perceived "dropped characters").

The C# `RdsAccumulatingScrollBuffer` is **not** the culprit — it dedups and is stable. The
defense-in-depth UI `ShouldRender()` guard (Tasks 1–2) is the primary fix; the API broadcast split
(Tasks 10–11) additionally stops the RDS-buffer append + RDS-card refresh from running at all on
pure-telemetry ticks, while leaving the signal meter, gain, and recognition stream fully fed.

### API broadcast contract change (Item A — read before Tasks 10–12)

**Subscriber graph for the `RadioStateChanged` hub event** (event `"RadioStateChanged"`, group
`"RadioState"`, broadcast in `AudioStateUpdateService.CheckRadioStateAsync:437-443`):

| Consumer | File | What it reads / does on the event |
|---|---|---|
| `AudioStateHubService` | `Services/Hub/AudioStateHubService.cs:135-142` | deserializes `RadioStateDto`, re-raises `RadioStateChanged` |
| `AudioStateStore` | `Services/AudioStateStore.cs:74,190-203` | caches `RadioState`, re-raises to its own subscribers |
| `RadioControlPanel` | `Components/Shared/RadioControlPanel.razor:997,1053-1070` | sets `_radioState`, appends RDS buffer, `StateHasChanged` → freq well, **RDS card**, active-preset highlight, signal meter, gain |
| `NowPlayingPanel` | `Components/Shared/NowPlayingPanel.razor:519,600-608` | sets `_radioState`, `StateHasChanged` → freq cell, gain cell, **fingerprint/recognition** NOW-row (`NowPlayingMatchId`) |
| `RadioPage` | `Components/Pages/RadioPage.razor:259,340-346` | sets `_radioState`, `StateHasChanged` → freq display, active-preset highlight |

**Chosen mechanism — flag-on-the-existing-event (NOT a second event).** Rationale: every consumer
above legitimately needs the full per-tick DTO for *something* that changes on telemetry (signal
meter, gain readout, recognition NOW-row), so we must keep broadcasting `RadioStateChanged` on
telemetry change — we must NOT gate the broadcast itself or those consumers go stale. Splitting into
two events would force every consumer to subscribe to both and re-merge state, a large blast radius.
Instead we keep the single event + full DTO and add a transient discriminator flag the *RDS-only*
work can check:

- Add `bool RdsRelevantChanged` to **both** `RadioStateDto` records (API `RadioDtos.cs` + Web
  `ApiModels.cs`). It is a per-broadcast signal, not persisted state.
- Server-side: `HasRadioStateChanged` keeps deciding *whether to broadcast at all* (unchanged
  semantics — meter/gain/recognition stay fed). A new `HasRdsRelevantChanged` predicate decides the
  flag value; the server stamps it onto the outgoing DTO right before `SendAsync`.
- "RDS-relevant" fields = the fields the RDS card + frequency well + active-preset highlight bind
  to: `Frequency`, `Band`, `Step`, `RdsStationName`, `RdsStationNameStable`, `RdsProgramType`,
  `RdsRadioText`, `RdsPi`, `NowPlayingMatchId`. "Telemetry-only" deltas (`SignalStrength`,
  `RssiDbu`, `Clip`, `AppliedGain`, `Gain`, `AutoGain`, `IsStereo`, `Equalizer`, `DeviceVolume`,
  `IsScanning`, `ScanDirection`) leave the flag `false`.
- Web-side: `RadioControlPanel.HandleRadioStateChanged` only appends to the RDS buffer + lets the
  RDS card refresh when `dto.RdsRelevantChanged` is true; it still updates `_radioState` every tick
  (so the signal meter/gain move). The `ShouldRender` guards from Tasks 1–2 remain as belt-and-
  suspenders, so even a stray RDS-relevant=false render can't restart the marquee.

> No consumer loses any field — the event and payload shape are unchanged except for one added
> nullable-default flag. `IsStereo` is treated as telemetry (it flips with signal quality, not a
> tune); the STEREO badge still updates because `_radioState` is always refreshed.

### Shared preset component (Item B — read before Tasks 13–15)

Both preset renderers are unified onto one shared component so Proposal A lives in exactly one
place: **`PresetCard`** at `src/Radio.Web/Components/Shared/PresetCard.razor`. It accounts for the
two surfaces' real differences (200px 1-row grid vs. 480px 2-col card) via a `Variant` enum
parameter, not duplicated markup. Details in Task 13.

### Files touched (summary)

| Action | File | Item(s) |
|---|---|---|
| Modify | `src/Radio.Web/Components/Shared/RdsScrollMarquee.razor` | 1 |
| Modify | `src/Radio.Web/Components/Shared/RdsCard.razor` | 1 |
| Modify | `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` | 2 |
| **Create** | `src/Radio.Web/Components/Shared/PresetCard.razor` | 4, B |
| Modify | `src/Radio.Web/Components/Shared/RadioControlPanel.razor` | 4, A, B |
| Modify | `src/Radio.Web/Components/Pages/RadioPage.razor` | 4, A, B |
| Modify | `src/Radio.Web/wwwroot/css/design-system.css` | 2, 4 |
| Modify | `src/Radio.API/Models/RadioDtos.cs` | A |
| Modify | `src/Radio.Web/Models/ApiModels.cs` | A |
| Modify | `src/Radio.API/Services/AudioStateUpdateService.cs` | A |
| Modify | `tests/Radio.Web.Tests/Components/Shared/RdsScrollMarqueeTests.cs` | 1 |
| Modify | `tests/Radio.Web.Tests/Components/Shared/RdsCardTests.cs` | 1 |
| **Create** | `tests/Radio.Web.Tests/Components/Shared/PresetCardTests.cs` | 4, B |
| Modify | `tests/Radio.API.Tests/...AudioStateUpdateService change-detection tests` | A |

### Task sequencing

Low-risk UI fixes land first (Tasks 1–5). The Proposal A styling lands next (Tasks 6–7) so the
visual spec is settled before extraction. The shared-component extraction (Tasks 13–15) then folds
that *settled* styling into one component and migrates both call sites — sequencing it after the
styling avoids re-churning the same CSS/markup twice. The API broadcast split (Tasks 10–12) is a
self-contained block that can be done any time after Tasks 1–2 (it depends on the `ShouldRender`
guards existing as the safety net) and before the final build gate.

- Task 1 — `RdsScrollMarquee.ShouldRender()` guard + duration memoization
- Task 2 — `RdsCard.ShouldRender()` guard (compose-string parity)
- Task 3 — Unit tests for the marquee/card render guards
- Task 4 — Remove duplicate RDS cell in `NowPlayingPanel.razor`
- Task 5 — Delete now-dead `.np-status-*-rds*` CSS
- Task 6 — Proposal A markup + states in `RadioControlPanel.razor` (interim, pre-extraction)
- Task 7 — Proposal A CSS for `.rcp-preset-*`
- Task 8 — Proposal A interim markup in `RadioPage.razor` preset cards
- Task 9 — *(removed — folded into Task 16 final gate; numbering preserved below as Task 16)*
- **Task 10 — API: add `RdsRelevantChanged` flag to both `RadioStateDto` records**
- **Task 11 — API: split change detection + stamp the flag in `AudioStateUpdateService`**
- **Task 12 — Web: gate RDS-buffer append on the flag in `RadioControlPanel`**
- **Task 13 — Create shared `PresetCard` component (Proposal A, all states, `Variant` enum)**
- **Task 14 — Migrate `RadioControlPanel` MEMORY rail to `PresetCard`**
- **Task 15 — Migrate `RadioPage` preset grid to `PresetCard`**
- **Task 16 — Build + full test pass + format/warnings gate**

> **Note on Tasks 6/8 vs 13–15:** Tasks 6 and 8 land Proposal A *inline* in each renderer first so
> the styling can be reviewed/UAT'd as a small diff; Tasks 13–15 then extract that now-approved
> styling into `PresetCard` and delete the inline copies. Builder MAY collapse 6→14 and 8→15 (skip
> the interim inline step and go straight to the shared component) if confident — see Task 13's
> "consolidation option". Either path ends in the same place; the plan documents both so the
> extraction is reviewable independently if desired.

---

## Task 1 — Gate the marquee re-render (`RdsScrollMarquee.razor`)

**File:** `src/Radio.Web/Components/Shared/RdsScrollMarquee.razor`

**Why:** Stop the CSS animation from restarting on every parent re-render. The marquee should
re-render only when the composed `Text`, `ScrollSpeedPxPerSec`, or `ContainerMaxWidthPx` changes
(any of which changes the visible track or its computed duration). Also stop recomputing the inline
`--scroll-duration` on every render — cache it and the static-fit flag, recomputing only when an
input changes.

**What to do:**

1. Move the per-render local computations (`isStatic`, `durationSeconds`) out of the markup block
   and into cached fields recomputed in `OnParametersSet()`.
2. Add a `ShouldRender()` override that compares the current inputs against the values captured at
   the last render and suppresses the render when nothing visible changed.

**Markup change** — replace the inline `var` computations at the top of the `@if` block
(currently `RdsScrollMarquee.razor:18-19`) and the inline style at `:29`. The track `<div>` now
reads cached fields:

```razor
@if (!string.IsNullOrEmpty(Text))
{
  <div class="rcp-rds-rt-scroll @(_isStatic ? "is-static" : string.Empty)"
       tabindex="0"
       aria-label="RDS RadioText"
       title="@Text">
    <div class="rcp-rds-rt-track"
         aria-hidden="true"
         style="@($"--scroll-duration: {_durationSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s;")">
      @Text
    </div>

    <div class="rcp-rds-rt-sr-only" aria-live="polite" aria-atomic="true">
      @Text
    </div>
  </div>
}
```

**`@code` additions** — add the cached fields, the recompute in `OnParametersSet`, and the
`ShouldRender` guard. `ApproximateTextWidthPx()` and `ComputeScrollDurationSeconds()` stay as-is
(they now read the parameter values during the recompute):

```csharp
  // Cached render inputs — recomputed in OnParametersSet, read by the markup.
  // We deliberately do NOT recompute these on every render: the parent
  // (RadioControlPanel) re-renders ~2x/second because the SignalR radio-state
  // broadcast lumps volatile signal telemetry (RSSI/signal-strength) in with
  // RDS text. Recomputing + re-attributing the track div on every one of those
  // restarts the CSS marquee keyframes, which is the "jerk + dropped chars"
  // the user reported. ShouldRender() below suppresses the no-op renders.
  private string? _renderedText;
  private int _renderedSpeed;
  private int _renderedContainerWidth;
  private bool _isStatic;
  private double _durationSeconds;

  protected override void OnParametersSet()
  {
    _isStatic = ApproximateTextWidthPx() <= ContainerMaxWidthPx;
    _durationSeconds = ComputeScrollDurationSeconds();
  }

  /// <summary>
  /// Suppress re-renders that wouldn't change anything the user can see. The
  /// CSS marquee animation restarts whenever this component re-renders (the
  /// track div is re-created and the inline --scroll-duration is re-emitted),
  /// so a no-op render visibly snaps the ticker back to the right edge. We
  /// only re-render when an input that affects the visible track changed.
  /// </summary>
  protected override bool ShouldRender()
  {
    var changed =
      !string.Equals(_renderedText, Text, StringComparison.Ordinal) ||
      _renderedSpeed != ScrollSpeedPxPerSec ||
      _renderedContainerWidth != ContainerMaxWidthPx;

    if (changed)
    {
      _renderedText = Text;
      _renderedSpeed = ScrollSpeedPxPerSec;
      _renderedContainerWidth = ContainerMaxWidthPx;
    }

    return changed;
  }
```

> **Note on the first render:** Blazor always renders the initial frame regardless of
> `ShouldRender` (it isn't consulted before the first render), so the cache-priming in
> `ShouldRender` correctly captures the first-render inputs and the marquee shows immediately.
> `OnParametersSet` runs before the first render too, so `_isStatic` / `_durationSeconds` are set.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings (warnings-as-errors).
- Behavioral verification is in the Test Plan (RDS smoothness on a live station). For a fast local
  check without hardware, the new unit tests in Task 3 assert the guard.

---

## Task 2 — Gate the card re-render (`RdsCard.razor`)

**File:** `src/Radio.Web/Components/Shared/RdsCard.razor`

**Why:** `RdsCard` composes `trackText` (`{StationName}{Separator}{RadioText}`) inline at
`:35-37` on every render and passes it to the nested marquee. Even with the marquee's own guard,
gating `RdsCard` avoids re-running the diff on the nested component tree on every telemetry tick.
This is defense-in-depth and keeps the compose logic in one place.

**What to do:**

1. Hoist the `trackText` composition into a cached field computed in `OnParametersSet()`.
2. Add a `ShouldRender()` guard comparing the composed track string, `ProgramType`,
   `ScrollSpeedPxPerSec`, and `StationName` (the latter still drives the static-PS branch when
   `RadioText` is empty).

**Markup change** — the `@if (!string.IsNullOrEmpty(RadioText))` branch now uses the cached
`_trackText` instead of the inline `var trackText = ...`:

```razor
    @if (!string.IsNullOrEmpty(RadioText))
    {
      <RdsScrollMarquee Text="@_trackText"
                        ScrollSpeedPxPerSec="@ScrollSpeedPxPerSec" />
    }
    else if (!string.IsNullOrEmpty(StationName))
    {
      <span class="rds-card-station">@StationName</span>
    }
    else
    {
      <span class="rds-card-station-spacer"></span>
    }
```

**`@code` additions:**

```csharp
  // Cached composed marquee track + last-rendered inputs. The parent
  // re-renders ~2x/second on signal telemetry; this guard stops that churn
  // from propagating into the nested RdsScrollMarquee diff when nothing the
  // user sees has changed.
  private string? _trackText;
  private string? _renderedStation;
  private string? _renderedRadioText;
  private string? _renderedProgramType;
  private int _renderedSpeed;
  private string _renderedSeparator = string.Empty;

  protected override void OnParametersSet()
  {
    _trackText = string.IsNullOrEmpty(StationName)
      ? RadioText
      : $"{StationName}{Separator}{RadioText}";
  }

  protected override bool ShouldRender()
  {
    var changed =
      !string.Equals(_renderedStation, StationName, StringComparison.Ordinal) ||
      !string.Equals(_renderedRadioText, RadioText, StringComparison.Ordinal) ||
      !string.Equals(_renderedProgramType, ProgramType, StringComparison.Ordinal) ||
      !string.Equals(_renderedSeparator, Separator, StringComparison.Ordinal) ||
      _renderedSpeed != ScrollSpeedPxPerSec;

    if (changed)
    {
      _renderedStation = StationName;
      _renderedRadioText = RadioText;
      _renderedProgramType = ProgramType;
      _renderedSeparator = Separator;
      _renderedSpeed = ScrollSpeedPxPerSec;
    }

    return changed;
  }
```

> Keep the inline track-text comment block (`RdsCard.razor:29-34`) — it documents the compose
> contract. Just move the actual computation into `OnParametersSet`.

**Verification:** `dotnet build src/Radio.Web --configuration Release` — 0 warnings. Unit
assertions in Task 3.

---

## Task 3 — Render-guard unit tests

**Files:**
- `tests/Radio.Web.Tests/Components/Shared/RdsScrollMarqueeTests.cs`
- `tests/Radio.Web.Tests/Components/Shared/RdsCardTests.cs` (optional, parity assert)

**Why:** Lock in the regression fix so a future refactor can't silently reintroduce the
per-render animation restart. bUnit exposes `cut.RenderCount` and `cut.SetParametersAndRender(...)`.

**What to do — add to `RdsScrollMarqueeTests`** (class already sets `JSInterop.Mode = Loose` and
`AddRadzenComponents()` in its ctor):

```csharp
  [Fact]
  public void Marquee_DoesNotReRender_WhenTextUnchanged()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    var before = cut.RenderCount;

    // Simulate the parent re-rendering on a telemetry tick with identical RDS text.
    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    cut.RenderCount.Should().Be(before,
      "an unchanged Text must not re-render the marquee (no CSS animation restart)");
  }

  [Fact]
  public void Marquee_ReRenders_WhenTextChanges()
  {
    var cut = RenderComponent<RdsScrollMarquee>(p => p
      .Add(x => x.Text, "WUNC News"));

    var before = cut.RenderCount;

    cut.SetParametersAndRender(p => p
      .Add(x => x.Text, "WUNC News • Morning Edition"));

    cut.RenderCount.Should().BeGreaterThan(before,
      "a changed Text must re-render so the new buffer scrolls");
  }
```

**Parity test in `RdsCardTests`** (the existing class already renders the card with `RadioText`
set, so this is safe to add — same `RenderComponent<RdsCard>(p => p.Add(...))` pattern the class
already uses):

```csharp
  [Fact]
  public void Card_DoesNotReRender_WhenInputsUnchanged()
  {
    var cut = RenderComponent<RdsCard>(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition"));

    var before = cut.RenderCount;

    cut.SetParametersAndRender(p => p
      .Add(x => x.StationName, "WUNC")
      .Add(x => x.RadioText, "Morning Edition"));

    cut.RenderCount.Should().Be(before);
  }
```

**Verification:**
```
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~RdsScrollMarqueeTests"
dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~RdsCardTests"
```
All green. If `RenderCount` semantics differ from expectation under bUnit's loose JS mode, fall
back to asserting the rendered `--scroll-duration` style string is identical across the two
`SetParametersAndRender` calls (the animation restart is what the `style` re-emit causes).

---

## Task 4 — Remove the duplicate RDS cell (`NowPlayingPanel.razor`)

**File:** `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`

**Why:** Item 2. The `np-status-cell-rds` block (`:60-67`) renders the PS station name a second
time on top of the main `RdsCard`. The user sees "RDS data shown twice".

**What to do:** Delete the entire RDS station cell block at `NowPlayingPanel.razor:60-67`:

```razor
      @* RDS station cell — hidden when RDS hasn't surfaced a station name. *@
      @if (_isTunerSource && !string.IsNullOrEmpty(_radioState?.RdsStationName))
      {
        <div class="np-status-cell np-status-cell-rds">
          <span class="np-status-rds-station">@_radioState.RdsStationName</span>
          <span class="np-status-rds-tag">RDS</span>
        </div>
      }
```

**Do NOT touch:**
- `_radioState` and its SignalR wiring — the **frequency cell** (`:52-58`) and **gain cell**
  (`:69-79`) still read it.
- The **match badge** at `:209-231` (the `np-match-badge` / `is-rds` block) — that is a
  source/provenance indicator ("RDS · station-supplied"), not a duplicate readout. **Keep it.**

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan: only ONE RDS readout (the `RdsCard` marquee in the radio panel) remains on the Home
  page; the frequency / gain / fingerprint badge cells still render.

---

## Task 5 — Delete the now-dead RDS-cell CSS

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

**Why:** With the markup in Task 4 removed, `.np-status-cell-rds`, `.np-status-rds-station`, and
`.np-status-rds-tag` are dead. Remove them to avoid orphaned rules (Polisher / dead-code review
will flag them otherwise).

**What to do:**
1. Remove the `.np-status-rds-station` rule (`design-system.css:4561` block, ends ~`:4570`).
2. Remove the `.np-status-rds-tag` rule (`:4572-4578`).
3. Search for and remove any `.np-status-cell-rds` rule if one exists (grep first; it may only be
   a markup class with no dedicated rule).

> Confirm the exact line span at edit time — line numbers drift if Task 4's markup edit is done
> first in the same working tree (it isn't, different file, but re-grep to be safe). Use
> `Grep` for `np-status-cell-rds|np-status-rds-station|np-status-rds-tag` and delete each block.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- `Grep` confirms zero remaining references to the three selectors across `src/Radio.Web`.

---

## Task 6 — Proposal A markup + states (renderer #1, `RadioControlPanel.razor`)

**File:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor`

> **Interim step.** Lands Proposal A inline so the rail visual is reviewable as a small diff; Task 14
> then replaces this inline row with `<PresetCard Variant="Rail">`. Builder MAY skip straight to
> Task 14 (Task 13 "consolidation option"). The CSS in Task 7 is **kept** either way — the `Rail`
> variant reuses the `.rcp-preset-*` classes.

**Why:** Item 4, renderer #1 (the Home right-rail MEMORY bank — the surface in the user's
screenshot). Implement Proposal A's field hierarchy and the required states from the handoff §6.

**What to do:**

### 6a. Add a unit-less freq helper + freq-only-fallback logic

The handoff (§5 "Frequency string format") wants the row to show the **number only** (`90.3`),
with the full `90.3 MHz` reserved for the tooltip. `FrequencyFormatter.FrequencyValue(hz, band)`
already returns the unit-less string — reuse it; no new formatter method is needed. There is
already a static wrapper at `RadioControlPanel.razor:1617`:

```csharp
  private static string FrequencyValue(double frequencyHz, string band) => FrequencyFormatter.FrequencyValue(frequencyHz, band);
```

`FormatFrequency` (`:1616`) stays for the full unit string used in the tooltip.

### 6b. Rewrite the preset row markup (`RadioControlPanel.razor:267-289`)

Implement the no-name fallback (handoff §6 "No-name / freq-only fallback"), a tooltip carrying the
full name + full unit, and a non-color active cue (handoff §7). Replace the row body:

```razor
            @{
              var hasName = !string.IsNullOrWhiteSpace(preset.Name);
              var freqValue = FrequencyValue(preset.Frequency, preset.Band);
              var freqFull = FormatFrequency(preset.Frequency, preset.Band);
              var rowTitle = hasName
                ? $"{preset.Name} — {preset.Band} {freqFull}. Click to tune; long-press or use ⋮ for rename / delete"
                : $"{preset.Band} {freqFull}. Click to tune; long-press or use ⋮ for rename / delete";
            }
            <div class="rcp-preset-item @(isActivePreset ? "is-active" : "")"
                 data-preset-id="@presetId"
                 @onclick="@(() => HandlePresetRowClickAsync(presetId))"
                 @onpointerdown="@(e => HandlePresetPointerDown(presetId))"
                 @onpointerup="@(e => HandlePresetPointerUp(presetId))"
                 @onpointerleave="@(e => HandlePresetPointerCancel())"
                 @onpointercancel="@(e => HandlePresetPointerCancel())"
                 role="button"
                 tabindex="0"
                 title="@rowTitle">
              @* Non-color active cue (a11y, handoff §7): a 3px amber left-bar
                 rendered via CSS on .is-active — no extra element needed here. *@
              <span class="rcp-preset-slot">@(preset.SlotNumber > 0 ? preset.SlotNumber.ToString("00") : "")</span>
              @if (hasName)
              {
                <span class="rcp-preset-text">
                  <span class="rcp-preset-name">@preset.Name</span>
                  <span class="rcp-preset-band">@preset.Band</span>
                </span>
                <span class="rcp-preset-freq">@freqValue</span>
              }
              else
              {
                @* No-name fallback (handoff §6): promote the frequency to the
                   primary line; drop the dim freq tail. Band sub-line stays. *@
                <span class="rcp-preset-text">
                  <span class="rcp-preset-name rcp-preset-name-freq">@freqValue</span>
                  <span class="rcp-preset-band">@preset.Band</span>
                </span>
                <span class="rcp-preset-freq"></span>
              }
              <button type="button"
                      class="rcp-preset-kebab"
                      aria-label="Rename or delete preset @(hasName ? preset.Name : freqFull)"
                      title="Rename / delete"
                      @onclick="@(e => HandleKebabClickAsync(presetId))"
                      @onclick:stopPropagation="true">⋮</button>
            </div>
```

> The empty `<span class="rcp-preset-freq"></span>` in the no-name branch keeps the 4-column grid
> intact (slot · text · freq · kebab) so the kebab stays in column 4. Alternatively the no-name
> branch could span the text across the freq column — but keeping the empty cell is simpler and
> avoids a second grid template. Builder: keep the empty cell.

### 6c. Empty-slot placeholder — verify the grid span

The handoff §6 ("Empty next slot") flags that the placeholder hint currently uses
`grid-column: 2 / 5` (`design-system.css:4362`). The grid stays 4-column (`auto` only changes the
3rd column's *width*, not the column count), so `grid-column: 2 / 5` is **still correct** and the
markup at `RadioControlPanel.razor:304-307` needs **no change**. (The handoff's "now 2/4" note was
conditional on collapsing the freq column entirely — we are not doing that.) Leave the empty-slot
markup as-is; just confirm it visually in UAT.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan covers: name shows more chars, freq number-only, no-name preset shows freq as primary,
  active cue, empty slot, empty list.

---

## Task 7 — Proposal A CSS for `.rcp-preset-*` (renderer #1)

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

**Why:** Item 4 styling. Apply the §5 type/token map and the §7 a11y active cue.

**What to do:**

### 7a. Grid: freq column `64px → auto` (`.rcp-preset-item`, `:4249-4262`)

```css
.rcp-preset-item {
  display: grid;
  grid-template-columns: 22px 1fr auto 24px;
  gap: 8px;
  align-items: center;
  padding: 6px 8px;
  margin-bottom: 4px;
  background: var(--surface-elevated, rgba(255, 255, 255, 0.02));
  border: 1px solid var(--surface-separator);
  border-radius: 6px;
  cursor: pointer;
  transition: all 80ms ease;
  -webkit-tap-highlight-color: transparent;
}
```

### 7b. Name: 14px (`.rcp-preset-item .rcp-preset-name`, `:4298-4307`)

```css
.rcp-preset-item .rcp-preset-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-high);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  padding-right: 0;
  line-height: 1.25;
}
```

### 7c. No-name fallback variant — freq promoted to primary line (new rule, add after 7b)

```css
/* No-name preset: the frequency is promoted to the primary line. Mono (not
   LED), high-contrast, 14px to match the name slot it replaces. */
.rcp-preset-item .rcp-preset-name.rcp-preset-name-freq {
  font-family: var(--font-mono);
  font-variant-numeric: tabular-nums;
  font-weight: 500;
}
```

### 7d. Freq tail: de-style (mono, 11px, dim, no glow) (`.rcp-preset-item .rcp-preset-freq`, `:4327-4337`)

```css
.rcp-preset-item .rcp-preset-freq {
  font-family: var(--font-mono);
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  font-size: 11px;
  text-align: right;
  color: var(--text-medium);
  text-shadow: none;
  margin-top: 0;
  line-height: 1.25;
}
```

### 7e. Active state: freq lifts to high-contrast, still mono/no-glow; remove the old glow rule

Replace the existing `.rcp-preset-item.is-active .rcp-preset-freq` glow rule (`:4339-4341`) with a
contrast lift (handoff §6 "Selected / active"):

```css
.rcp-preset-item.is-active .rcp-preset-freq {
  color: var(--text-high);
  text-shadow: none;
}
```

### 7f. Non-color active cue — 3px amber left bar (a11y, handoff §7)

Add to the existing `.rcp-preset-item.is-active` rule (`:4269-4272`). Use `box-shadow` inset rather
than `border-left` so it doesn't shift the grid columns:

```css
.rcp-preset-item.is-active {
  border-color: var(--signal-amber);
  background: color-mix(in srgb, var(--signal-amber) 8%, transparent);
  /* Non-color-dependent active cue (a11y): 3px amber inset left bar, mirrors
     the border-left pattern used elsewhere (design-system.css:612). Inset
     box-shadow avoids reflowing the grid the way border-left would. */
  box-shadow: inset 3px 0 0 0 var(--signal-amber);
}
```

> The slot number already turns amber when active (`:4286-4288`) — that stays as a second
> redundant cue. The name color does **not** change on active (handoff §6 / §9).

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan: freq is mono/small/dim/no-glow, single line (no `105.10 / MHz` wrap), tabular-aligned;
  active row shows amber border + amber slot + left bar; name stays high-contrast.

---

## Task 8 — Proposal A interim markup for `RadioPage.razor` preset cards (renderer #2)

**File:** `src/Radio.Web/Components/Pages/RadioPage.razor`

> **Interim step.** This task lands Proposal A inline so the renderer-#2 visual is reviewable as a
> small diff. Task 15 then replaces this inline markup with the shared `<PresetCard>`. Builder MAY
> skip straight to Task 15 (see Task 13 "consolidation option") — if so, do that task's migration
> instead of this inline edit and mark Task 8 done-by-15.

**Why:** Item 4 scope confirmed by user: apply Proposal A to **both** renderers. Renderer #2 is the
standalone Radio page presets panel (480px, 2-column card grid, inline styles at `:146-165`).

**What to do:** Re-weight the inline card to name-primary, freq-secondary, matching Proposal A's
hierarchy and token map. Keep the existing 2-column grid and the delete button. Replace the inner
card markup at `RadioPage.razor:149-163`:

```razor
              <div class="list-item-touch" style="flex-direction: column; align-items: stretch; padding: 12px; border-radius: 8px; border: 1px solid var(--surface-separator); cursor: pointer;"
                   @onclick="@(() => LoadPresetAsync(preset.Id))"
                   title="@($"{(string.IsNullOrWhiteSpace(preset.Name) ? preset.Band : preset.Name)} — {preset.Band} {FormatFrequency(preset.Frequency, preset.Band)}")">
                @{
                  var rpHasName = !string.IsNullOrWhiteSpace(preset.Name);
                }
                @if (rpHasName)
                {
                  <div style="font-size: 16px; font-weight: 600; color: var(--text-high); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">@preset.Name</div>
                }
                else
                {
                  @* No-name fallback: frequency becomes the primary line. *@
                  <div style="font-size: 16px; font-weight: 600; color: var(--text-high); font-family: var(--font-mono); font-variant-numeric: tabular-nums;">@FrequencyValue(preset.Frequency, preset.Band)</div>
                }
                <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 4px;">
                  <span style="font-size: 13px; color: var(--text-low);">@preset.Band</span>
                  <div style="display: flex; align-items: center; gap: 8px;">
                    @if (rpHasName)
                    {
                      @* Freq demoted: small dim mono tail, no amber, no glow (Proposal A). *@
                      <span style="font-size: 13px; color: var(--text-medium); font-family: var(--font-mono); font-variant-numeric: tabular-nums;">@FrequencyValue(preset.Frequency, preset.Band)</span>
                    }
                    <div @onclick:stopPropagation="true">
                      <RadzenButton Icon="delete" ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
                                    Variant="Variant.Text"
                                    Click="@(() => DeletePresetAsync(preset.Id))" title="Delete" />
                    </div>
                  </div>
                </div>
              </div>
```

**Add a `FrequencyValue` wrapper** if the page doesn't already have one. `RadioPage.razor:645`
has `FormatFrequency`; add a sibling wrapper next to it:

```csharp
  private static string FrequencyValue(double frequencyHz, string band) => FrequencyFormatter.FrequencyValue(frequencyHz, band);
```

> **Unification note:** this inline divergence is **temporary** — Task 15 replaces it with the
> shared `<PresetCard>` so both renderers follow Proposal A from one source. (Per the rev-2 scope
> change, the two renderers are no longer left divergent.)

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan: navigate to the standalone Radio page; preset cards show name-primary, freq dim mono.

---

## Task 10 — API: add `RdsRelevantChanged` flag to both `RadioStateDto` records

**Files:**
- `src/Radio.API/Models/RadioDtos.cs` (the `class RadioStateDto`, `:6-150`)
- `src/Radio.Web/Models/ApiModels.cs` (the `record RadioStateDto`, `:310-346`)

**Why:** Item A. A per-broadcast discriminator the RDS-only work can read to skip its append +
card refresh on telemetry-only ticks, without changing the event name, the group, or any existing
field (so no consumer loses data).

**What to do:**

**API side** — add to `RadioStateDto` in `RadioDtos.cs` (after `NowPlayingMatchId`, `:149`):

```csharp
  /// <summary>
  /// Per-broadcast discriminator (NOT persisted device state). True when this
  /// broadcast carries a change to an RDS- or tuning-relevant field
  /// (frequency, band, step, any RDS PS/PTY/RT/PI/stable field, or
  /// NowPlayingMatchId); false when only volatile signal telemetry
  /// (RSSI/signal-strength/gain/stereo/scan) changed. The Web RDS marquee
  /// path reads this to avoid re-running the RDS accumulator + restarting the
  /// CSS ticker ~twice a second on pure-telemetry ticks. Telemetry consumers
  /// (signal meter, gain readout, recognition NOW-row) ignore it and read the
  /// full DTO every broadcast. Defaults true so any non-broadcast construction
  /// (REST /api/radio/state) is treated as a full refresh.
  /// </summary>
  public bool RdsRelevantChanged { get; set; } = true;
```

**Web side** — add a matching trailing parameter to the `record RadioStateDto` in `ApiModels.cs`
(after `RdsPi`, `:345`). Default `true` so REST-fetched state is treated as a full refresh:

```csharp
  ushort? RdsPi = null,
  // Per-broadcast discriminator mirrored from the API DTO — true on RDS/tuning
  // changes, false on telemetry-only ticks. The RDS marquee path reads it to
  // skip the accumulator append + card refresh when nothing it shows changed.
  // Defaults true so REST /api/radio/state (which can't compute a delta) is
  // always treated as a full refresh.
  bool RdsRelevantChanged = true
```

**Verification:**
- `dotnet build src/Radio.API --configuration Release` and `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- System.Text.Json deserializes the new bool by name; missing-on-wire → default `true`, which is the
  safe "treat as full refresh" value. No serializer config change needed.

---

## Task 11 — API: split change detection + stamp the flag (`AudioStateUpdateService`)

**File:** `src/Radio.API/Services/AudioStateUpdateService.cs`

**Why:** Item A. `HasRadioStateChanged` keeps gating *whether to broadcast* (unchanged — telemetry
consumers stay fed). A new `HasRdsRelevantChanged` predicate computes the flag value; the broadcast
stamps it onto the outgoing DTO.

**What to do:**

### 11a. Stamp the flag in `CheckRadioStateAsync` (`:428-444`)

```csharp
  private async Task CheckRadioStateAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    if (activeSource is not IRadioControl radioControls)
    {
      return;
    }

    var currentRadioState = radioControls.MapToRadioStateDto(_currentMatchId);

    if (HasRadioStateChanged(_lastRadioState, currentRadioState))
    {
      // Stamp the per-broadcast discriminator BEFORE caching/sending so the
      // Web RDS path can skip its accumulator append on telemetry-only ticks.
      // Computed against the PREVIOUS state (the same baseline HasRadioStateChanged
      // used), so the very first broadcast (_lastRadioState == null) is RDS-relevant.
      currentRadioState.RdsRelevantChanged = HasRdsRelevantChanged(_lastRadioState, currentRadioState);

      _lastRadioState = currentRadioState;
      await _hubContext.Clients.Group("RadioState")
        .SendAsync("RadioStateChanged", currentRadioState, cancellationToken);
      _logger.LogDebug("Broadcast RadioStateChanged: {Frequency} {Band} RdsRelevant={Rds}",
        currentRadioState.Frequency, currentRadioState.Band, currentRadioState.RdsRelevantChanged);
    }
  }
```

> **Caching note:** stamping the flag onto `currentRadioState` before assigning it to
> `_lastRadioState` is fine — the flag is recomputed against the prior baseline on every tick, so a
> stale `true`/`false` on the cached instance is never read (the next call compares fields, not the
> flag). The flag is write-once-per-broadcast.

### 11b. Add the `HasRdsRelevantChanged` predicate (next to `HasRadioStateChanged`, after `:585`)

```csharp
  /// <summary>
  /// True when an RDS- or tuning-relevant field changed between broadcasts —
  /// the fields the Web RDS card, frequency well, and active-preset highlight
  /// bind to. Deliberately EXCLUDES volatile signal telemetry (signal strength,
  /// RSSI, clip, applied/manual gain, AGC, stereo, equalizer, device volume,
  /// scan state) so the RDS marquee doesn't re-run its accumulator ~twice a
  /// second. A null previous (first broadcast after tune/source-switch) counts
  /// as relevant so the card populates immediately.
  /// </summary>
  private static bool HasRdsRelevantChanged(RadioStateDto? previous, RadioStateDto? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    return Math.Abs(previous.Frequency - current.Frequency) > 0.001 ||
           previous.Band != current.Band ||
           Math.Abs(previous.Step - current.Step) > 0.001 ||
           previous.RdsStationName != current.RdsStationName ||
           previous.RdsStationNameStable != current.RdsStationNameStable ||
           previous.RdsProgramType != current.RdsProgramType ||
           previous.RdsPi != current.RdsPi ||
           previous.RdsRadioText != current.RdsRadioText ||
           previous.NowPlayingMatchId != current.NowPlayingMatchId;
  }
```

> **Do NOT change `HasRadioStateChanged`** (`:544-585`) — it must keep returning true on telemetry
> deltas so the signal meter / gain / recognition NOW-row keep updating. The new predicate is a
> strict subset of its conditions (RDS/tuning rows only), so any tick that is RDS-relevant is also a
> broadcast — the flag can never be true without a broadcast happening.

**Verification:**
- `dotnet build src/Radio.API --configuration Release` — 0 warnings.
- Add/extend unit tests in `tests/Radio.API.Tests` (Task 11c) asserting the predicate.

### 11c. Unit tests for the split predicate

**File:** `tests/Radio.API.Tests/Services/AudioStateUpdateServiceTests.cs` (existing fixture).

**Access pattern (confirmed):** this fixture already tests `private static` methods of
`AudioStateUpdateService` via **reflection** — see `InvokeUpdateCurrentMatchAnchor` which does
`typeof(AudioStateUpdateService).GetMethod("UpdateCurrentMatchAnchor", BindingFlags.NonPublic | BindingFlags.Static | ...)`.
Keep `HasRadioStateChanged` and `HasRdsRelevantChanged` **`private static`** (no visibility change
needed — the project already has `[InternalsVisibleTo("Radio.API.Tests")]` but these tests use
reflection, so even private is reachable). Add two reflection shims mirroring the existing one:

```csharp
  private static bool InvokeHasRdsRelevantChanged(RadioStateDto? prev, RadioStateDto? curr)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      "HasRdsRelevantChanged",
      BindingFlags.NonPublic | BindingFlags.Static)!;
    return (bool)m.Invoke(null, new object?[] { prev, curr })!;
  }

  private static bool InvokeHasRadioStateChanged(RadioStateDto? prev, RadioStateDto? curr)
  {
    var m = typeof(AudioStateUpdateService).GetMethod(
      "HasRadioStateChanged",
      BindingFlags.NonPublic | BindingFlags.Static)!;
    return (bool)m.Invoke(null, new object?[] { prev, curr })!;
  }
```

> Note: `RadioStateDto` here is the **API** DTO (`Radio.API.Models.RadioStateDto`, a `class` with
> settable properties), so the object-initializer construction below compiles. Add
> `using Radio.API.Models;` to the test file if not already present.

Assertions to add:
- Telemetry-only delta (e.g. `RssiDbu` 0 → 5, `SignalStrength` 40 → 60, everything else equal) →
  `HasRadioStateChanged` is **true** (still broadcasts) AND `HasRdsRelevantChanged` is **false**.
- RDS text change (`RdsRadioText` "A" → "B", telemetry equal) → both **true**.
- Frequency change (tune) → both **true**.
- `previous == null` → `HasRdsRelevantChanged` **true** (first-broadcast populates the card).

```csharp
  [Fact]
  public void RdsRelevant_False_OnTelemetryOnlyChange()
  {
    var prev = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RssiDbu = 0, SignalStrength = 40, RdsRadioText = "Hotel California" };
    var curr = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RssiDbu = 5, SignalStrength = 60, RdsRadioText = "Hotel California" };

    InvokeHasRadioStateChanged(prev, curr).Should().BeTrue("telemetry change must still broadcast for the signal meter");
    InvokeHasRdsRelevantChanged(prev, curr).Should().BeFalse("telemetry-only change must NOT flag the RDS path");
  }

  [Fact]
  public void RdsRelevant_True_OnRadioTextChange()
  {
    var prev = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RdsRadioText = "Hotel California" };
    var curr = new RadioStateDto { Frequency = 105_100_000, Band = "FM", RdsRadioText = "Life in the Fast Lane" };

    InvokeHasRdsRelevantChanged(prev, curr).Should().BeTrue();
  }
```

> `InvokeHasRadioStateChanged` / `InvokeHasRdsRelevantChanged` are whatever invocation shim the
> existing fixture already uses for the private statics (reflection helper or `internal` accessor).
> Reuse it; don't invent a new one.

**Verification:** `dotnet test tests/Radio.API.Tests --configuration Release --filter "FullyQualifiedName~RadioState"` — green.

---

## Task 12 — Web: gate the RDS-buffer append on the flag (`RadioControlPanel`)

**File:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor`

**Why:** Item A. Stop the RDS accumulator append + RDS-card refresh from running on telemetry-only
ticks. `_radioState` is still updated every tick so the signal meter / gain / freq well stay live.

**What to do:** Update `HandleRadioStateChanged` (`:1053-1070`):

```csharp
  private async Task HandleRadioStateChanged(RadioStateDto dto)
  {
    // Always refresh _radioState: the signal meter, gain readout, freq well,
    // STEREO badge, and active-preset highlight all bind to it and legitimately
    // change on telemetry ticks. The child ShouldRender guards (RdsCard /
    // RdsScrollMarquee) keep the marquee from restarting even on these renders.
    _radioState = dto;

    // Only touch the RDS accumulator when the broadcast actually carried an
    // RDS/tuning change. On telemetry-only ticks (RdsRelevantChanged == false)
    // we skip ResetOnTuneChange + AppendChunk entirely — the buffer text is
    // unchanged, so RdsCard's ShouldRender suppresses the marquee re-render.
    if (dto.RdsRelevantChanged)
    {
      // HANDOFF-rds-accumulating-scroll — order matters: reset BEFORE append.
      _rdsBuffer.ResetOnTuneChange(dto.Band, dto.Frequency, dto.RdsPi);
      _rdsBuffer.AppendChunk(dto.RdsRadioText);
    }

    await InvokeAsync(StateHasChanged);
  }
```

> **Why still call `StateHasChanged` on telemetry ticks:** the signal meter and gain readout in
> this same panel need to repaint. The RDS card won't repaint because its `ShouldRender` (Task 2)
> sees no input change. This is the belt-and-suspenders pairing: the API flag avoids the *buffer
> work*; the `ShouldRender` guard avoids the *animation restart*.

> **`NowPlayingPanel` and `RadioPage`:** no change needed for the flag. `NowPlayingPanel` has no
> RDS marquee (Task 4 removed its only RDS readout); it just refreshes `_radioState` for the freq
> cell / gain / recognition row — correct to do every tick. `RadioPage` has no RDS card either; it
> refreshes `_radioState` for the freq display + active-preset highlight every tick — also correct.
> Leave both `HandleRadioStateChanged` / `OnRadioStateChanged` as-is.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan items 11–13 (telemetry consumers move while marquee stays smooth).

---

## Task 13 — Create the shared `PresetCard` component

**File (new):** `src/Radio.Web/Components/Shared/PresetCard.razor`

**Why:** Item B. One component owns Proposal A and every state from the handoff (§6), so the two
renderers can't drift. Real surface differences (200px 1-row grid vs. 480px 2-col card) are handled
by a `Variant` parameter, not duplicated markup.

**Component contract:**

```razor
@* PresetCard — the single saved-station renderer (Proposal A). Two visual
   variants share one field hierarchy + token set so the Home MEMORY rail and
   the standalone Radio page can't drift (HANDOFF-saved-station-display §8).

   Variant.Rail  → compact 1-row grid (slot · name+band · freq · kebab),
                   used in RadioControlPanel's 200px MEMORY rail. Active cue +
                   slot + kebab + long-press gestures live here.
   Variant.Card  → 2-column-grid card body (name line, then band + freq + delete
                   row), used in RadioPage's 480px presets panel.

   All states from the handoff (§6) are honoured in BOTH variants:
   selected/active (a11y non-color cue), long-name ellipsis + tooltip,
   no-name→freq-as-primary fallback. Empty-slot + empty-list remain the
   PARENT's responsibility (they're list-level chrome, not per-card) — see
   the migration tasks. *@

@{
  var hasName = !string.IsNullOrWhiteSpace(Name);
  var freqValue = FrequencyFormatter.FrequencyValue(Frequency, Band);
  var freqFull = FrequencyFormatter.FormatFrequency(Frequency, Band);
  var fullLabel = hasName ? Name : freqFull;
  var rowTitle = $"{fullLabel} — {Band} {freqFull}";
}

@if (Variant == PresetCardVariant.Rail)
{
  <div class="rcp-preset-item @(IsActive ? "is-active" : "")"
       data-preset-id="@PresetId"
       @onclick="@(() => OnSelect.InvokeAsync(PresetId))"
       @onpointerdown="@(_ => OnPointerDown.InvokeAsync(PresetId))"
       @onpointerup="@(_ => OnPointerUp.InvokeAsync(PresetId))"
       @onpointerleave="@(_ => OnPointerCancel.InvokeAsync())"
       @onpointercancel="@(_ => OnPointerCancel.InvokeAsync())"
       role="button"
       tabindex="0"
       title="@($"{rowTitle}. Click to tune; long-press or use ⋮ for rename / delete")">
    <span class="rcp-preset-slot">@(SlotNumber > 0 ? SlotNumber.ToString("00") : "")</span>
    @if (hasName)
    {
      <span class="rcp-preset-text">
        <span class="rcp-preset-name">@Name</span>
        <span class="rcp-preset-band">@Band</span>
      </span>
      <span class="rcp-preset-freq">@freqValue</span>
    }
    else
    {
      <span class="rcp-preset-text">
        <span class="rcp-preset-name rcp-preset-name-freq">@freqValue</span>
        <span class="rcp-preset-band">@Band</span>
      </span>
      <span class="rcp-preset-freq"></span>
    }
    <button type="button"
            class="rcp-preset-kebab"
            aria-label="Rename or delete preset @fullLabel"
            title="Rename / delete"
            @onclick="@(_ => OnKebab.InvokeAsync(PresetId))"
            @onclick:stopPropagation="true">⋮</button>
  </div>
}
else
{
  <div class="preset-card @(IsActive ? "is-active" : "")"
       @onclick="@(() => OnSelect.InvokeAsync(PresetId))"
       role="button"
       tabindex="0"
       title="@rowTitle">
    @if (hasName)
    {
      <div class="preset-card-name">@Name</div>
    }
    else
    {
      <div class="preset-card-name preset-card-name-freq">@freqValue</div>
    }
    <div class="preset-card-meta">
      <span class="preset-card-band">@Band</span>
      <span class="preset-card-meta-right">
        @if (hasName)
        {
          <span class="preset-card-freq">@freqValue</span>
        }
        @if (OnDelete.HasDelegate)
        {
          <span @onclick:stopPropagation="true">
            <RadzenButton Icon="delete" ButtonStyle="ButtonStyle.Danger" Size="ButtonSize.Small"
                          Variant="Variant.Text"
                          Click="@(() => OnDelete.InvokeAsync(PresetId))" title="Delete" />
          </span>
        }
      </span>
    </div>
  </div>
}

@code {
  /// <summary>Stable preset id, echoed back through the callbacks.</summary>
  [Parameter, EditorRequired] public string PresetId { get; set; } = string.Empty;
  /// <summary>Station name. Empty/whitespace triggers the freq-as-primary fallback.</summary>
  [Parameter] public string? Name { get; set; }
  /// <summary>Frequency in Hz (raw API units).</summary>
  [Parameter, EditorRequired] public double Frequency { get; set; }
  /// <summary>Band token ("FM"/"AM"/...). Drives unit selection + the sub-line.</summary>
  [Parameter, EditorRequired] public string Band { get; set; } = string.Empty;
  /// <summary>One-based per-band slot number. 0 → blank slot cell (Rail only).</summary>
  [Parameter] public int SlotNumber { get; set; }
  /// <summary>Active/selected station — amber border + slot + non-color left bar.</summary>
  [Parameter] public bool IsActive { get; set; }
  /// <summary>Which surface this card renders for.</summary>
  [Parameter] public PresetCardVariant Variant { get; set; } = PresetCardVariant.Rail;

  [Parameter] public EventCallback<string> OnSelect { get; set; }
  [Parameter] public EventCallback<string> OnKebab { get; set; }
  [Parameter] public EventCallback<string> OnDelete { get; set; }
  [Parameter] public EventCallback<string> OnPointerDown { get; set; }
  [Parameter] public EventCallback<string> OnPointerUp { get; set; }
  [Parameter] public EventCallback OnPointerCancel { get; set; }
}
```

Add the enum (own file or top of the component's `@code` is fine; a small standalone file is
cleaner):

**File (new):** `src/Radio.Web/Components/Shared/PresetCardVariant.cs`

```csharp
namespace Radio.Web.Components.Shared;

/// <summary>
/// Visual variant for <see cref="PresetCard"/>. Rail = compact 1-row grid for
/// the Home MEMORY rail; Card = 2-line card body for the Radio page presets panel.
/// </summary>
public enum PresetCardVariant
{
  Rail,
  Card
}
```

**CSS for the `Card` variant** — add to `design-system.css` near the `.rcp-preset-*` block. These
mirror Proposal A's tokens at the card variant's larger sizes (name 16px, freq 13px dim mono):

```css
/* Shared PresetCard — Card variant (Radio page 480px presets panel). Proposal A
   hierarchy at card scale: name-primary high-contrast, freq dim mono no-glow. */
.preset-card {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  padding: 12px;
  border-radius: 8px;
  border: 1px solid var(--surface-separator);
  cursor: pointer;
  transition: all 80ms ease;
}
.preset-card:hover {
  border-color: color-mix(in srgb, var(--signal-amber) 25%, var(--surface-separator));
}
.preset-card.is-active {
  border-color: var(--signal-amber);
  box-shadow: inset 3px 0 0 0 var(--signal-amber); /* non-color a11y cue */
}
.preset-card-name {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-high);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.preset-card-name.preset-card-name-freq {
  font-family: var(--font-mono);
  font-variant-numeric: tabular-nums;
}
.preset-card-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 4px;
}
.preset-card-band {
  font-size: 13px;
  color: var(--text-low);
}
.preset-card-meta-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
.preset-card-freq {
  font-size: 13px;
  color: var(--text-medium);
  font-family: var(--font-mono);
  font-variant-numeric: tabular-nums;
}
.preset-card.is-active .preset-card-freq {
  color: var(--text-high);
}
```

> **Consolidation option:** if Builder prefers, Tasks 6 and 8 (interim inline Proposal A) can be
> skipped and this `PresetCard` built first, with Tasks 14–15 wiring it directly. The plan keeps
> the interim path documented so the styling is reviewable in isolation if desired, but going
> straight to the shared component is acceptable and avoids writing the inline markup twice.

> **States ownership:** `PresetCard` owns Default / Hover / Active / Long-name / No-name. The
> **Empty next-slot** placeholder and the **No-presets / empty-list** block are *list-level* chrome
> and stay in the parent (the rail's dashed placeholder + "NO PRESETS"; the card panel's "No
> presets saved yet"). Tasks 14–15 keep those parent blocks untouched.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Task block below adds `PresetCardTests`.

### 13 (tests) — `PresetCardTests`

**File (new):** `tests/Radio.Web.Tests/Components/Shared/PresetCardTests.cs` (mirror the
`RdsScrollMarqueeTests` ctor: `Services.AddRadzenComponents()` + `JSInterop.Mode = Loose`).

Assert, parameterised over both variants where applicable:
- `Variant.Rail` with a name → renders `.rcp-preset-name` = name, `.rcp-preset-freq` = unit-less value.
- `Variant.Rail`, no name → `.rcp-preset-name.rcp-preset-name-freq` carries the freq; empty freq tail.
- `Variant.Card` with a name → `.preset-card-name` = name, `.preset-card-freq` = unit-less value.
- `IsActive` true → root carries `is-active` (both variants).
- `OnSelect` fires with the `PresetId` on click (both variants).
- `OnKebab` fires on the rail kebab; rail row `title` contains the full name + full `MHz`/`kHz`.

**Verification:** `dotnet test tests/Radio.Web.Tests --configuration Release --filter "FullyQualifiedName~PresetCardTests"` — green.

---

## Task 14 — Migrate `RadioControlPanel` MEMORY rail to `<PresetCard>`

**File:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor`

**Why:** Item B. Replace the inline preset row (from Task 6) with the shared component; keep all the
existing handlers and the empty-slot / empty-list parent chrome.

**What to do:** Replace the `@foreach` row body (Task 6's markup at `:267-289`) with:

```razor
          @foreach (var preset in _presets)
          {
            var isActivePreset = IsActivePreset(preset);
            <PresetCard Variant="PresetCardVariant.Rail"
                        PresetId="@preset.Id"
                        Name="@preset.Name"
                        Frequency="@preset.Frequency"
                        Band="@preset.Band"
                        SlotNumber="@preset.SlotNumber"
                        IsActive="@isActivePreset"
                        OnSelect="HandlePresetRowClickAsync"
                        OnKebab="HandleKebabClickAsync"
                        OnPointerDown="HandlePresetPointerDown"
                        OnPointerUp="HandlePresetPointerUp"
                        OnPointerCancel="HandlePresetPointerCancel" />
          }
```

> The existing handler signatures already take the preset id (`HandlePresetRowClickAsync(string)`,
> `HandleKebabClickAsync(string)`, `HandlePresetPointerDown(string)`, etc.) so they bind directly to
> the `EventCallback<string>` parameters. `HandlePresetPointerCancel()` is parameterless → binds to
> `OnPointerCancel` (`EventCallback`). Confirm signatures at edit time; if any differ, wrap in a
> lambda.

**Keep unchanged:** the empty-next-slot placeholder block (`:298-308`), the `NO PRESETS` empty
block (`:255-260`), and the action-menu overlay (`:320-342`). These are list-level chrome.

**Remove:** the now-dead inline row markup and (if Task 6 added a per-row `@{ }` block) its locals.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan item 7 (rail renders identically to the inline Proposal A version) + item 14 (parity).

---

## Task 15 — Migrate `RadioPage` preset grid to `<PresetCard>`

**File:** `src/Radio.Web/Components/Pages/RadioPage.razor`

**Why:** Item B. Replace the inline card (Task 8) with the shared component in `Variant.Card`.

**What to do:** Replace the inner `@foreach` card markup (Task 8's block at `:147-164`) with:

```razor
          <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px;">
            @foreach (var preset in _presets.OrderBy(p => p.CreatedAt))
            {
              <PresetCard Variant="PresetCardVariant.Card"
                          PresetId="@preset.Id"
                          Name="@preset.Name"
                          Frequency="@preset.Frequency"
                          Band="@preset.Band"
                          IsActive="@(IsActivePreset(preset))"
                          OnSelect="LoadPresetAsync"
                          OnDelete="DeletePresetAsync" />
            }
          </div>
```

> **`IsActivePreset` on RadioPage:** the rail's `IsActivePreset` lives in `RadioControlPanel`. Check
> whether `RadioPage` already has an equivalent (it tracks `_radioState`); if not, add a small
> private helper mirroring `RadioControlPanel.IsActivePreset` (band match + `Math.Abs(freq - freq) < 1.0`).
> If the Radio page never highlighted an active preset before, passing `IsActive="false"` (omit the
> param) is acceptable to preserve current behavior — Builder's call; note which was chosen.
> **Decision flagged below.**

> **Signatures:** `LoadPresetAsync(string id)` and `DeletePresetAsync(string id)` already take the
> id, so they bind to `OnSelect` / `OnDelete` (`EventCallback<string>`). Confirm; wrap in a lambda
> if the existing signature differs (e.g. takes the whole DTO).

**Keep unchanged:** the "No presets saved yet" empty block (`:136-142`) and the panel header /
"Save Current" button.

**Remove:** the inline `FrequencyValue` wrapper added in Task 8 if it's now unused on the page
(grep for other uses first — `:40` uses `FormatFrequency`, not `FrequencyValue`, so the wrapper may
be removable). Leave `FormatFrequency` (`:645`) — still used at `:40`, `:535`.

**Verification:**
- `dotnet build src/Radio.Web --configuration Release` — 0 warnings.
- Test Plan item 8 (Radio page cards) + item 14 (both renderers from one component).

---

## Task 16 — Build, test, format gate

**Why:** Release builds treat warnings as errors; CI runs ~1,697 tests across 10 projects.

**What to do / Verification:**
```
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```
- 0 warnings on the Release build (API + Web + all libs).
- All tests green. Known-flaky: `AudioApiService` `_WhenServerNotAvailable` timeout tests
  (unrelated; re-run once if they flake).
- Confirm: no orphaned CSS selectors (Task 5); `_radioState` still wired in `NowPlayingPanel`
  (Task 4 keeps it); no leftover inline preset markup after Tasks 14–15; the new `RdsRelevantChanged`
  field round-trips (Task 10) and is read only in `RadioControlPanel.HandleRadioStateChanged` (Task 12).

---

## Test Plan (Tester — real browser at http://radio:5002/)

**Deploy first:** `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`. Then open
`http://radio:5002/` in a browser sized to **1920×720**. Tune to a **live FM station with active
RDS RadioText** (so the marquee actually scrolls — a station that broadcasts song/RT info).

### Item 1 + 3 — RDS scroll smoothness
1. On the Home page, observe the `RdsCard` marquee (the blue PS/RT ticker above the frequency well).
2. **Watch for a full 30+ seconds.** PASS = the text scrolls right-to-left at a constant speed and
   completes full passes; it does **not** snap back to the right edge mid-scroll, and no glyphs are
   dropped/yanked. FAIL = periodic jumps/snaps (~twice a second) or visibly missing characters.
3. Hover the marquee — it should pause; un-hover — it resumes from the same offset (existing
   behavior, must still work).
4. Tune to a station with a **short** static tagline → confirm it renders centered/static (no
   scroll) and doesn't twitch.

### Item 2 — single RDS readout
5. On the Home page with a tuner source active, confirm there is exactly **ONE** RDS readout: the
   `RdsCard` marquee. The Now-Playing status strip must **not** show a second "RDS" station cell.
6. Regression: the Now-Playing status strip still shows the **frequency cell**, the **gain cell**
   (tappable → gain popover opens), and — when a fingerprint/RDS match exists — the **match badge**
   (tap opens the recognition stream). None of these may disappear.

### Item 4 — saved-station cards (BOTH renderers)
7. **Renderer #1 (Home right-rail MEMORY bank):** save several presets including a **long name**
   (e.g. "Classic Vinyl Rock Channel"). Confirm:
   - Station name renders larger (14px), high-contrast, single line, showing **~20+ chars** before
     ellipsis (vs. the old ~8–10).
   - Frequency is **smaller (11px), dim, mono (NOT the amber LED/Orbitron font), no glow**, on a
     **single line** (no `105.10 / MHz` two-line wrap).
   - Frequency numerals stay **column-aligned** down the list (tabular-nums).
   - The **active** preset (tune to a saved freq) shows an amber border + amber slot number + a
     **left amber bar**; the **name stays high-contrast** (not recolored amber).
   - A **long name** truncates with ellipsis; hovering the row shows the full name + full
     `MHz`/`kHz` in the tooltip.
   - A **no-name** preset (if you can create one) shows the **frequency as the primary line**, not
     a blank row.
   - The **empty next-slot** placeholder ("EMPTY · long-press … to save") still renders and spans
     correctly; rows are not taller than before; **7 presets still fit without new scrolling** at
     1920×720.
   - The **empty list** state ("NO PRESETS" radio glyph) still renders when no presets exist.
8. **Renderer #2 (standalone Radio page presets panel):** navigate to the Radio page. Confirm the
   2-column preset cards now show **name-primary** (16px high-contrast) and **freq demoted** to a
   small dim mono tail (no amber). No-name card shows freq as primary. Delete button still works.

### Item A — telemetry consumers stay live while the marquee stays smooth (broadcast split)
11. With a live RDS station playing, watch the **signal-strength meter** (the RSSI/signal bar in the
    radio panel) for 30+ seconds. PASS = it continues to move/update roughly twice a second
    (telemetry still flowing). FAIL = it freezes (the split starved it).
12. Simultaneously confirm the **RDS marquee stays smooth** during those same telemetry updates (no
    snap-back) — this is the whole point: telemetry moves, RDS doesn't restart.
13. Confirm the **gain readout** updates when gain changes (toggle AGC or change gain) and the
    **fingerprint/recognition badge** still appears + the NOW-row anchors when a match lands (tap the
    badge → recognition stream opens, NOW row highlighted). None of these may go stale after the split.
    Also verify on a **tune** (change frequency/station): the RDS card resets and repopulates
    immediately (the first post-tune broadcast is RDS-relevant).

### Item B — both renderers come from the one shared component
14. Compare the Home MEMORY rail (`Variant.Rail`) and the Radio page presets panel (`Variant.Card`):
    both must reflect Proposal A (name-primary high-contrast, freq dim mono no-glow, active a11y cue,
    no-name→freq-primary fallback). Editing the shared `PresetCard` once must visibly change both —
    confirm there is no second inline copy left behind (grep the two parent files for leftover
    `.rcp-preset-name` / inline `font-size: 16px` preset markup → should be gone).
15. Active-preset highlight: on the rail, tuning to a saved freq highlights that row (amber border +
    slot + left bar). On the Radio page, confirm the chosen active-highlight behavior (see decision 5).

### Cross-cutting regression
16. 1920×720 layout intact on Home and Radio pages — no overflow, no clipped panels, frequency well
    + STEREO badge still visible (the prior #416 regression must not reappear).
17. Tune up/down and switch bands — the active-preset highlight tracks correctly; the RDS card
    resets cleanly on station change (no stale text). Empty-slot placeholder + empty-list states
    still render on both surfaces.

---

## Decisions / risks for the user

1. **API broadcast split — now IN SCOPE (rev 2), mechanism chosen.** Implemented as a
   **flag-on-the-existing-event** (`RdsRelevantChanged` on `RadioStateDto`), NOT a second hub event.
   Rationale: every `RadioStateChanged` consumer (signal meter, gain, recognition NOW-row) needs the
   full per-tick payload, so the broadcast itself must keep firing on telemetry change; a second
   event would force every consumer to subscribe to both and re-merge state (large blast radius).
   The flag lets only the RDS path skip its work. Affected consumers verified in the plan: `AudioStateHubService`,
   `AudioStateStore`, `RadioControlPanel` (the only behavior change — gates its RDS append),
   `NowPlayingPanel` (no change — RDS readout removed in Item 2), `RadioPage` (no change). If you'd
   prefer a clean two-event split instead, say so — it's more "correct" architecturally but touches
   all five consumers; the flag is the lower-risk equivalent.

2. **Two-renderer unification — now IN SCOPE (rev 2).** Both renderers move onto a single shared
   `PresetCard` (`src/Radio.Web/Components/Shared/PresetCard.razor`) with a `PresetCardVariant`
   enum (`Rail` / `Card`). Empty-slot and empty-list chrome stay in the parents (list-level, not
   per-card). Proposal A now lives in exactly one place.

3. **Active-highlight on the Radio page (`Variant.Card`) — needs your call.** The Home rail already
   highlights the active preset; the standalone Radio page historically may **not** have. Task 15
   either (a) adds a small `IsActivePreset` helper to `RadioPage` so the card highlights consistently,
   or (b) passes `IsActive="false"` to preserve the page's current (no-highlight) behavior. Default
   recommendation: **(a) consistent highlight** — it's strictly better UX and the component already
   supports it. Flag if you want (b).

4. **Proposal A only.** The handoff offers B (two-line, freq on band sub-line) and C (freq chip) as
   alternatives. We're shipping A as directed. If, after seeing A on real data, names still feel
   tight, B is a one-line CSS follow-up.

5. **`RenderCount` test brittleness (low).** The render-guard unit tests assert bUnit's
   `RenderCount`. If bUnit's loose-JS mode emits an extra render that throws off the exact count,
   the fallback is to assert the emitted `--scroll-duration` style string is byte-identical across
   two identical `SetParametersAndRender` calls (Task 3 documents this fallback).

6. **Interim-vs-direct extraction (Builder's call, documented both ways).** Tasks 6/8 land Proposal A
   inline first (small reviewable diff), then Tasks 14/15 extract into `PresetCard`. Builder may
   collapse 6→14 and 8→15 and go straight to the shared component (Task 13 "consolidation option").
   Either path ends identically; flag if you specifically want the interim diff for review.
