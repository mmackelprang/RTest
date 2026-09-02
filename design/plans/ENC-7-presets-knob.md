# PLAN — `ENC-7` · The PRESETS knob: recall and save on the existing bank, and the last remap

**Row:** `ENC-7` (P0, Encoders workstream) — [`docs/HANDOFF-GA-PUNCH-LIST.md` §3.0](../../docs/HANDOFF-GA-PUNCH-LIST.md)
**Spec:** [`docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md`](../../docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md) (Rev 3) — **§4.3, §4.4 Knob 3, §6.6** are the spec; also §4.2, §4.5, §5.3, §6.9, §8.3, §12.1, §12.2, §15.
**Relationship to the handoff:** **follows**, **with one declared deviation it inherits and must carry forward** (`MEMORY` → `PRESETS`, §0.5) and **four declared deviations of its own** (§0.4) — all forced by the preset bank being a different shape from the one the handoff describes.
**Depends on:** [`ENC-5`](ENC-5-source-overlay.md) — **hard.** This row consumes five artefacts `ENC-5` builds and remaps the index `ENC-5` leaves on the visualiser. It cannot start until `ENC-5` is merged.
**Author:** Planner, 2026-09-02.
**Effort:** 2–3 days · **11 tasks** across 5 phases.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

Designer's judgement, and it is right: *"if a guest touches exactly one knob after volume, this is
the one that should reward them."* Turn PRESETS and a list of saved stations appears; press one and
the console switches source, sets the band, tunes and plays; hold for 600 ms and what is playing is
saved. *"Put on my station"* is the single most common thing anyone does to a radio, and on this
console it currently costs a source switch plus twenty-five detents of tuning.

**Almost none of the UI is new.** `ENC-5` built the overlay, the row model, the preview machine, the
CSS and the wire format. This row supplies a second list, three actions, and the remap that finally
makes all four knobs say what the cabinet says.

### 0.2 Five things Builder must NOT do

1. ⛔ **Do NOT rebuild the preset bank.** `PresetCard.razor`, `RadioPresetService`,
   `SqliteRadioPresetRepository` and the five `RadioController` preset endpoints all exist and ship.
   This row reads and writes through them.

2. ⛔ **Do NOT modify `EncoderSelectorOverlay.razor`, `EncoderSelectorState`, `EncoderSelectorRow`,
   `EncoderSelectorRowDto` or the `.encoder-selector-*` CSS.** They are `ENC-5`'s and they were built
   with this row in hand — including the seven-row window that SOURCE never needed (`ENC-5` §0.4
   D-5) and the `SelectorNotice` phase that nothing in `ENC-5` publishes. If any of them needs
   changing, **say so in the PR rather than changing it quietly**: the punch list is explicit that
   *"building them apart is how they drift"*, and a silent edit here is that drift arriving one row
   late.

3. ⛔ **Do NOT make the PRESETS long-press overwrite anything.** Handoff §4.4 and §4.5: *"Never
   overwrites."* Replacement stays on the touchscreen behind the existing kebab, where it has a
   confirmation and an undo. This is the only gesture in the spec that writes data, and the reason it
   is safe is that it cannot destroy.

4. ⛔ **Do NOT add a cross-source preset model.** v1 saves radio stations only, and says so out loud
   on a non-radio source. Cross-source favourites are v2 and need a data model that does not exist
   (handoff §12.1 item 3).

5. ⛔ **Do NOT rename `MEMORY` back to `PRESETS`… nor `PRESETS` back to `MEMORY`.** §0.5. The rename
   is a **declared, deliberate deviation** from `HANDOFF-saved-station-display.md`, recorded in three
   places precisely so a later consistency pass does not revert it.

### 0.3 ⚠ The bank is not the shape the row's text describes — and this is the biggest finding

> The punch list says: *"Drives the shipped bank: `PresetCard.razor`, **7 slots**, save/rename/delete
> already shipped."*

**There are no 7 slots. There are no slots at all in the persisted sense.** Verified in tree:

| The row assumes | The tree has |
|---|---|
| 7 slots | `RadioPresetService.MaxPresets => 50` (`src/Radio.Infrastructure/Audio/Services/RadioPresetService.cs:18`) — one global cap across every band. The only `7` in the whole stack is prose in `RadioBandService.cs:14` about NOAA weather channels, and an illustrative `MEMORY · 7 of 16` in an older handoff |
| A slot number on a preset | `RadioPreset` (`src/Radio.Core/Models/Audio/RadioPreset.cs`) has **no** `SlotNumber`; nor does the `RadioPresets` SQLite table (`FingerprintDbContext.cs:148-156`). The slot is a **derived ordinal**, recomputed per request in `RadioController.GetPresets:585-589` as `GroupBy(Band).OrderBy(CreatedAt).Select((p, idx) => idx + 1)` |
| A "next free slot" to write into | There is no such thing, because **gaps cannot exist**. Delete slot 2 of 3 and the survivor renumbers 3 → 2 on the next read. There is no save-to-slot, no reorder, no move |
| A per-band capacity that fills up | `RadioBandModel.BandPresetCapacity` (16, or 4 for WB) is **advisory UI chrome only** — `AddPresetAsync` never consults it. You can save 20 FM presets against a capacity of 16 |

**The design survives this intact, and is in fact simpler.** Every requirement in §4.4 maps onto what
exists:

- **"Save to the next free slot"** — `AddPresetAsync` appends, and the ordinal the server then derives
  *is* the next slot. Nothing has to find a gap because there are none.
- **"Never overwrites"** — free by construction. `AddPresetAsync` has no overwrite path at all.
- **"If every slot is full … `PRESETS FULL — replace a slot on screen`, and write nothing"** — the
  real ceiling is 50, and `AddPresetAsync` already throws on it. The copy is kept verbatim; **it will
  essentially never fire**, and the plan says so rather than pretending it is a live guard.

**But one case the handoff does not cover fires constantly, and it is the one a guest will hit
first.** `AddPresetAsync` also throws when a preset already exists for that band and frequency
(`RadioPresetService.cs`, message `"A preset already exists for …"`). Holding the knob on a station
you already saved is not an error a person makes once — it is what someone does when they cannot
remember whether they saved it. Silence there is the same defect the row exists to fix.

> **Decision, recorded rather than assumed:** the duplicate case gets its own message,
> **`ALREADY SAVED · slot NN`**, for 1500 ms — the same duration and the same `SelectorNotice`
> channel as the other two boundaries. It reports a fact rather than a failure, and it tells the
> user where the thing they were trying to save already is. **This is an addition to the spec, not
> an interpretation of it, and the owner should be told.**

⚠ **`RadioController` routes these two exceptions by matching on the message text**
(`RadioController.cs:646,651` — `ex.Message.Contains("already exists")` / `.Contains("Maximum")`).
This row calls `IRadioPresetService` directly and must **not** reword those messages.

### 0.4 Four declared deviations of this row's own

| # | Handoff says | Tree says | Resolution |
|---|---|---|---|
| **D-1** | §6.6 mock shows the list as `01 KEXP Seattle 90.3` — an unqualified slot column | Slot ordinals are **per band**, so an FM preset and an AM preset can both be `01` (`RadioController.cs:585-589`) | **Show the ordinal exactly as the on-screen rail shows it, and put the band in the secondary line** (`AM 1010 kHz`). Two rows both reading `01` is then honest and matches the bank the user can see, rather than inventing a second, global numbering that only the knob uses |
| **D-2** | §6.6: *"Seven rows plus chrome fits comfortably"* — the list is assumed to fit | The bank holds up to 50 | Nothing to do here — **`ENC-5` Task 12 built the seven-row window for exactly this** (`ENC-5` §0.4 D-5). This row supplies 50 rows and the component windows them. Listed so the reviewer knows it was foreseen rather than discovered |
| **D-3** | §4.4: Recall must *"switch source and band if needed, tune, and play"* — usable from Bluetooth | `POST /api/radio/presets/{id}/load` — the only recall path that exists — returns **400 `"Radio is not the active source"`** when radio is not active (`RadioController.cs:757`, `GetActiveRadioSource:561`). It is *only* usable when you are already on the radio | **Recall is implemented server-side in the selector service, not through that endpoint**: `GetOrCreateSourceAsync(Radio, switchToSource: true)` → `SetBandAsync` → `SetFrequencyAsync`. The controller endpoint is left alone; it is correct for what the touchscreen does with it |
| **D-4** | §4.4: the empty state reads `NO STATIONS SAVED` / `hold this knob to save what's playing` | `RadioControlPanel`'s on-screen bank shows `.rcp-presets-empty` with **`NO PRESETS`** | **The overlay uses Designer's copy; the on-screen bank keeps its own.** They are different surfaces with different jobs — the overlay's line teaches a gesture that only exists on the knob, and putting *"hold this knob"* on a touchscreen would be a lie. Declared so Polisher does not read it as copy drift |

### 0.5 ⭐ The rename is a DECLARED deviation. Carry this forward.

`RadioControlPanel`'s bank is titled `MEMORY · n saved` (`RadioControlPanel.razor:249`). The cabinet
is engraved **PRESETS** (D10), so the bank becomes **`PRESETS · n saved`**.

**This is a deliberate, one-word deviation from
[`HANDOFF-saved-station-display.md`](../../docs/design-handoffs/HANDOFF-saved-station-display.md)**,
whose §3 titles that bank `MEMORY · n saved`. It is recorded in three places on purpose:

1. Designer Rev 3's own header — *"Deviates (small, deliberate) from `HANDOFF-saved-station-display.md`"*
2. Designer §4.4 Knob 3, with the reasoning
3. **Punch list §6, the "deliberately parked" table** (`docs/HANDOFF-GA-PUNCH-LIST.md:978`) —
   *"Renaming the on-screen bank back to `MEMORY` — **Do NOT 'fix' this on a later consistency
   pass.**"*

> **For Builder:** put the deviation in the PR description, not only in the diff. A one-word string
> change with no stated reason is exactly what a future reviewer reverts.
>
> **For Polisher:** `PRESETS · n saved` over a handoff that says `MEMORY · n saved` is **not drift.**
> Designer's reasoning, verbatim: *"A panel that says PRESETS over a screen that says MEMORY is the
> same mismatch class I flagged in the settings table; fixing one and not the other would have been
> worse than either."* A physical engraving cannot be edited later. Everything else in that handoff —
> field hierarchy, slot numbering, long-press-to-save, kebab menu — is untouched.

`RadioPage.razor:128` already heads its own panel `Presets`, so this rename moves the two on-screen
surfaces **into** agreement rather than out of it — worth noting, because it is the opposite of what
a drift report would assume.

### 0.6 The final remap — and the mapping test moves one last time

`ENC-5` left index 2 holding the visualiser as a seat-warmer. This row takes it.

| Index | After `ENC-5` | **After this PR** |
|---|---|---|
| 0 | Volume | **Volume** |
| 1 | SOURCE | **SOURCE** |
| 2 | Visualization *(seat-warmer)* | **PRESETS** |
| 3 | Tuning | **Tuning** |

**End state: `0 = VOLUME · 1 = SOURCE · 2 = PRESETS · 3 = TUNING`** — matching the escutcheon (D2),
matching `RotaryEncoderConfigDefaults.Create()`'s per-channel comments, and matching what `ENC-11`
has been pushing to the device since #509. The deliberate mismatch recorded in
`docs/HANDOFF-NEXT-SESSION.md` is closed by this PR.

**`ENC-5` Task 8's `[Theory]` expects `(2, "VISUALIZER")` and that row goes red here.** Its XML doc
says so in advance: *"index 2 … is expected to change once more: ENC-7 replaces the visualiser there
with PRESETS."* Task 5 changes the expectation to `(2, "PRESETS")`. **Do not chase the red by
reverting the remap.**

**Visualization loses its knob, and that is the design.** Handoff §11 is titled *"Capability
preservation — where visualization goes"* and moves it to the touchscreen. Two of its four
replacements already ship — the six-segment picker (`VisualizerPanel.razor:34-71`) and the System
Config dropdown — so **the capability survives this PR intact**. The other two (tap the canvas to
advance, long-press for the list) are `ENC-9`, which is **P1 and not queued**.

> ⚠ **Worth putting in front of the owner rather than deciding here.** The punch list's own
> justification for `ENC-9` is *"removing a knob must not remove a capability"*, which reads as
> though `ENC-9` should land with or before the removal. It does not have to — the segment picker is
> right there on Home — but this PR is the moment the knob goes, so it is the moment to notice that
> `ENC-9` is P1 and unqueued.

---

## 1. Architecture

### 1.1 What this row adds, and what it reuses

```
  Radio.API process
 ┌───────────────────────────────────────────────────────────┐
 │ RotaryEncoderActionRouter                                 │
 │   idx 2 turn      ─▶ HandlePresetsTurn  ──┐               │
 │   idx 2 press     ─▶ HandlePresetsPress ──┤               │
 │   idx 2 long-press▶ OnLongPress ──────────┤               │
 │                                           ▼               │
 │                          PresetSelectorService   ◀── NEW  │
 │                            ├ EncoderSelectorState  (ENC-5)│
 │                            ├ IServiceScopeFactory ─┐      │
 │                            │                       ▼      │
 │                            │        IRadioPresetService (scoped, exists)
 │                            ├ recall: GetOrCreateSource → SetBand → SetFrequency
 │                            └ save:   AddPresetAsync  +  IRadioBandMemory (ENC-5)
 │                                           │               │
 │                                           ▼               │
 │                          IEncoderFeedbackSink  (ENC-4)    │
 └───────────────────────────────────────────────────────────┘
                                             │
                                       (unchanged path)
                                             ▼
   Radio.Web:  EncoderHudService → EncoderHud → EncoderSelectorOverlay   ← ZERO CHANGES
```

**The Web side of this row is one string rename and its test.** Everything the overlay needs to
render a preset list — the ordinal cell, the seven-row window, the empty state, the
`SelectorNotice` phase — was built in `ENC-5`. That is what "one component, two lists" was supposed
to buy, and this PR is where the bill comes due. **If Builder finds themselves editing
`EncoderSelectorOverlay.razor`, something in `ENC-5` was under-built and the PR should say what.**

### 1.2 ⚠ A singleton cannot hold a scoped service

`IRadioPresetService` and `IRadioPresetRepository` are registered **scoped**
(`FingerprintingServiceExtensions.cs:47,53`). `RotaryEncoderActionRouter` and every service this row
adds are **singletons** — they are driven by a HID read loop with no request scope. Injecting
`IRadioPresetService` into a singleton constructor is a captive-dependency bug the container will
either refuse or, worse, silently satisfy with one scope that outlives its `FingerprintDbContext`.

**`PresetSelectorService` takes `IServiceScopeFactory` and opens a scope per operation.** This is
recorded in project memory as a repeated trap (*"Singleton cannot consume scoped — repositories used
by singletons must also be singleton"*); the scope factory is the other, less invasive answer, and
the right one here because it does not change the lifetime of anything the API already uses.

### 1.3 Recall is not scoped to the active source, and that is the whole point

Handoff §4.3: *"You are on Bluetooth, you turn PRESETS, you see your stations, you press one, and the
console switches to FM and tunes it. It is never dead — and it is the only candidate that is more
useful the further you are from what you want to hear."*

So the list is composed from the bank unconditionally, never filtered by the active source, and
recall performs a full source switch when it has to. That is also why §0.4 D-3 rules out the existing
`presets/{id}/load` endpoint, which refuses precisely the case the design exists to serve.

---

## 2. Tasks

> **Convention reminders for every task:** 2-space indent · file-scoped namespaces · nullable
> enabled · **warnings-as-errors in Release** · bUnit tests need `JSInterop.Mode = JSRuntimeMode.Loose`.
>
> **⚠ The pre-merge rule this repo enforces hardest** (`CLAUDE.md` § Pre-Merge Review): a comment,
> log message or XML doc must assert **only what the code actually does**. This row has three
> specific traps: (a) do not write "the next free slot" as though a slot were found — nothing is
> searched, the write appends and the ordinal is derived afterwards (§0.3); (b) do not write "never
> overwrites" as though a guard enforced it — no overwrite path exists, and the difference matters to
> whoever later adds one; (c) do not write that the bank has seven slots.

---

### Phase 1 — the preset list and its three actions

#### Task 1 — `PresetSelectorService` — composition and preview

**Why:** handoff §4.4 Knob 3, turn half.

**Create** `src/Radio.Infrastructure/Platform/Input/PresetSelectorService.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The PRESETS knob's list, preview, recall and save (ENC-7).
///
/// <para>
/// Deliberately the same grammar as <see cref="SourceSelectorService"/>, on the same
/// <see cref="EncoderSelectorState"/> and through the same overlay: the two knobs are adjacent and
/// the handoff wants them interchangeable in the hand — learn one, you have learned both. The lists
/// differ in their contents and in what a commit does, and in nothing else.
/// </para>
///
/// <para>
/// <b>The list is never filtered by the active source.</b> That is what keeps the knob alive from
/// Bluetooth or Phono: turn it from anywhere and your stations are there, and pressing one switches
/// source and band to get to it.
/// </para>
///
/// <para>
/// <b>Scope factory, not an injected repository.</b> <see cref="IRadioPresetService"/> is registered
/// scoped and this service is a singleton driven by the HID read loop, which has no request scope.
/// A scope is opened per operation rather than promoting the repository's lifetime, because the
/// repository holds a database context and the API's other consumers are request-scoped.
/// </para>
/// </summary>
public sealed class PresetSelectorService
{
  private readonly ILogger<PresetSelectorService> _logger;
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly Func<IAudioManager> _audioManagerFactory;
  // Func<> so the container can build this without IConfigurationStore present - see the
  // registration note below, and ENC-5 Task 6 for why that matters.
  private readonly Func<IRadioBandMemory> _bandMemoryFactory;
  private readonly IEncoderFeedbackSink _hud;
  private readonly EncoderSelectorState _state = new();
  private readonly object _gate = new();

  public PresetSelectorService(
    ILogger<PresetSelectorService> logger,
    IServiceScopeFactory scopeFactory,
    Func<IAudioManager> audioManagerFactory,
    Func<IRadioBandMemory> bandMemoryFactory,
    IEncoderFeedbackSink hud)
  {
    _logger = logger;
    _scopeFactory = scopeFactory;
    _audioManagerFactory = audioManagerFactory;
    _bandMemoryFactory = bandMemoryFactory;
    _hud = hud;
  }

  /// <summary>The encoder index this overlay renders above, set by the router.</summary>
  public int EncoderIndex { get; set; } = 2;

  /// <summary>
  /// A turn: open if closed, then move the highlight. <b>Nothing plays.</b>
  ///
  /// <para>
  /// The composition is async (it reads the bank) while the encoder event is not, so the turn
  /// applies the movement against whatever rows are loaded and refreshes them in the background.
  /// The first turn of a session therefore opens on a possibly-empty list and fills in within one
  /// coalescer window — which is the correct trade: blocking the HID read loop on a database read is
  /// how a knob becomes laggy.
  /// </para>
  /// </summary>
  public void Turn(int clampedDelta)
  {
    lock (_gate)
    {
      _state.Open();
      _state.Move(clampedDelta);
      PublishPreviewLocked();
    }

    _ = RefreshAsync();
  }
  // Press, LongPress, RefreshAsync, Dismiss below.
}
```

**`RefreshAsync` — composition:**

```csharp
  /// <summary>
  /// Reloads the bank and republishes, keeping the highlight on the same preset.
  ///
  /// <para>
  /// Ordering matches the on-screen rail exactly — band, then the per-band slot ordinal — so the
  /// knob's list and the list the user can see are the same list in the same order. Note the three
  /// orderings already in this stack: the repository sorts by Name, RadioControlPanel re-sorts by
  /// band/slot/created, and RadioPage sorts by CreatedAt. This follows RadioControlPanel, because
  /// that is the bank this knob is a remote control for.
  /// </para>
  /// </summary>
  private async Task RefreshAsync()
  {
    try
    {
      using var scope = _scopeFactory.CreateScope();
      var presets = await scope.ServiceProvider
        .GetRequiredService<IRadioPresetService>()
        .GetAllPresetsAsync();

      var mgr = _audioManagerFactory();
      var live = mgr.ActiveSource as IRadioControl;

      // The per-band ordinal, derived the same way RadioController.GetPresets derives it, so the
      // number on the knob is the number on the screen.
      var rows = presets
        .GroupBy(p => p.Band)
        .SelectMany(g => g.OrderBy(p => p.CreatedAt).Select((p, i) => (Preset: p, Slot: i + 1)))
        .OrderBy(x => x.Preset.Band.ToString(), StringComparer.Ordinal)
        .ThenBy(x => x.Slot)
        .Select(x => new EncoderSelectorRow
        {
          Id = $"preset:{x.Preset.Id}",
          Primary = x.Preset.Name,
          // Band in the secondary line because slot ordinals are per band: two rows can both read
          // "01", and the band is what tells them apart (see the plan's D-1).
          Secondary = $"{x.Preset.Band} {new Frequency((long)x.Preset.Frequency).ToDisplayString()}",
          Ordinal = x.Slot.ToString("00"),
          AccentVar = "--source-radio",
          IsCurrent = live is not null
            && live.CurrentBand == x.Preset.Band
            && Math.Abs(live.CurrentFrequency.Hertz - x.Preset.Frequency) < 1.0,
          IsAvailable = true,
        })
        .ToList();

      lock (_gate)
      {
        _state.SetRows(rows);
        if (_state.IsOpen)
        {
          PublishPreviewLocked();
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error loading the preset bank for the PRESETS knob");
    }
  }
```

> **Builder:** the `< 1.0` hertz tolerance on `IsCurrent` matches `RadioControlPanel`'s own
> active-preset test (`RadioControlPanel.razor:1034`). Keeping the same tolerance is what stops the
> knob's highlight and the rail's `.is-active` disagreeing on the same station.

**Every preset row is `IsAvailable = true`.** A saved station is always recallable — recall creates
whatever source it needs. **Do not add a speculative availability check**; a dimmed preset row would
be a State C flash for a recall that would have worked.

**The empty state and the title suffix** go on the publish, from `ENC-5`'s existing fields:

```csharp
      Title = "PRESETS",
      TitleSuffix = $"{_state.Rows.Count} saved",
      Footer = "PRESS TO PLAY · HOLD TO SAVE",
      EmptyPrimary = "NO STATIONS SAVED",
      EmptySecondary = "hold this knob to save what's playing",
```

⚠ `EmptySecondary` is lower-case in the handoff's mock and must stay that way — it is a sentence
spoken to the user, not a label. Do not let a CSS `text-transform` uppercase it; `ENC-5`'s
`.encoder-selector-empty` does not, and adding one here would be a regression against §6.6's mock.

---

#### Task 2 — Recall

**Why:** handoff §4.4 — *"Commit the highlight: switch source and band if needed, tune, and play."*
§0.4 D-3 explains why the existing endpoint cannot do it.

**Add** to `PresetSelectorService`:

```csharp
  /// <summary>
  /// A press: recall the highlighted preset. With the overlay closed this opens it instead — the
  /// same one-rule press SOURCE has, so a mis-grab in the middle of the panel costs nothing.
  /// </summary>
  public void Press()
  {
    EncoderSelectorRow? row;
    lock (_gate)
    {
      bool wasOpen = _state.IsOpen;
      _state.Open();

      if (!wasOpen)
      {
        PublishPreviewLocked();
        _ = RefreshAsync();
        return;
      }

      row = _state.Highlighted;
    }

    if (row is null)
    {
      return;
    }

    _ = RecallAsync(row);
  }

  /// <summary>
  /// Switches source and band as needed, tunes, and plays.
  ///
  /// <para>
  /// Not routed through <c>POST /api/radio/presets/{id}/load</c>: that endpoint resolves the tuner
  /// with <c>GetActiveRadioSource()</c> and returns 400 when radio is not already active — which is
  /// exactly the case this knob exists to serve. The steps below are the same three the endpoint
  /// performs, preceded by the source switch it cannot do.
  /// </para>
  /// </summary>
  private async Task RecallAsync(EncoderSelectorRow row)
  {
    string id = row.Id["preset:".Length..];

    try
    {
      RadioPreset? preset;
      using (var scope = _scopeFactory.CreateScope())
      {
        preset = await scope.ServiceProvider
          .GetRequiredService<IRadioPresetService>()
          .GetPresetByIdAsync(id);
      }

      if (preset is null)
      {
        // Deleted from the touchscreen while the overlay was open.
        PublishFailed(row, "That preset is gone");
        await RefreshAsync();
        return;
      }

      var mgr = _audioManagerFactory();

      if (mgr.ActiveSource is not IRadioControl radio)
      {
        // A real source switch: fade, spinner, and a failure card if it does not come up.
        PublishCommitting(row);
        if (await mgr.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: true) is not IRadioControl created)
        {
          PublishFailed(row, "Tuner unavailable");
          return;
        }

        radio = created;
      }

      if (radio.IsScanning)
      {
        // The touchscreen's recall does this too. Tuning under a running scan lands somewhere else
        // a second later.
        await radio.StopScanAsync();
      }

      await radio.SetBandAsync(preset.Band);
      await radio.SetFrequencyAsync(new Frequency((long)preset.Frequency));

      // Recall is also a tune, so the band memory learns from it the same way a knob turn does.
      await _bandMemoryFactory().SetAsync(preset.Band, new Frequency((long)preset.Frequency));

      PublishPreview();
      _logger.LogInformation("Recalled preset {Name} ({Band} {Frequency})", preset.Name, preset.Band, preset.Frequency);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error recalling preset {Id}", id);
      PublishFailed(row, "Could not tune that station");
    }
  }
```

⚠ **`SetFrequencyAsync` throws `ArgumentOutOfRangeException` on `SDRRadioAudioSource`** when the
receiver rejects the value (`SDRRadioAudioSource.cs:260`), and `SetBandAsync` throws
`ArgumentException` (`:400-403`). The `catch` above is what turns both into State E instead of an
unobserved task exception. **Do not narrow it to a specific exception type** — the point is that a
recall never leaves the user in silence.

---

#### Task 3 — Save, and the three boundaries

**Why:** handoff §4.4 long-press half, and §0.3.

**Add** to `PresetSelectorService`:

```csharp
  /// <summary>
  /// A 600 ms hold: save what is playing.
  ///
  /// <para>
  /// The write appends; it has no overwrite path, so "never overwrites" is a property of
  /// <see cref="IRadioPresetService.AddPresetAsync"/> rather than a guard this method applies. The
  /// slot number reported afterwards is the per-band ordinal the bank derives from creation order —
  /// nothing is searched for and no gap is filled, because deletion renumbers and gaps cannot exist.
  /// </para>
  /// </summary>
  public void LongPress() => _ = SaveAsync();

  private async Task SaveAsync()
  {
    try
    {
      var mgr = _audioManagerFactory();

      if (mgr.ActiveSource is not IRadioControl radio)
      {
        // The one context-limited gesture in the spec, and it says so out loud rather than failing
        // silently. Cross-source presets need a favourites model that does not exist (v2).
        PublishNotice("Only radio stations can be saved", null, EncoderInteractionTimings.SelectorNoticeShortMs);
        return;
      }

      var band = radio.CurrentBand;
      double hz = radio.CurrentFrequency.Hertz;
      string name = radio.RdsStationNameStable
        ?? RadioPreset.GetDefaultName(band, hz);

      using var scope = _scopeFactory.CreateScope();
      var presets = scope.ServiceProvider.GetRequiredService<IRadioPresetService>();

      if (await presets.PresetExistsAsync(band, hz))
      {
        int slot = await SlotOfAsync(presets, band, hz);
        PublishNotice($"ALREADY SAVED · slot {slot:00}", name, EncoderInteractionTimings.SelectorNoticeShortMs);
        return;
      }

      var saved = await presets.AddPresetAsync(name, band, hz);
      int newSlot = await SlotOfAsync(presets, band, hz);

      PublishNotice(
        $"Saved to {newSlot:00}",
        $"{saved.Name} · {new Frequency((long)hz).ToDisplayString()} {band}",
        EncoderInteractionTimings.SelectorNoticeMs);

      await RefreshAsync();
      _logger.LogInformation("Saved preset {Name} to {Band} slot {Slot}", saved.Name, band, newSlot);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Maximum", StringComparison.Ordinal))
    {
      // The bank is full. Nothing is written, and replacement stays on the touchscreen where it has
      // a confirmation and an undo.
      PublishNotice("PRESETS FULL", "replace a slot on screen", EncoderInteractionTimings.SelectorNoticeMs);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error saving a preset from the PRESETS knob");
      PublishNotice("Could not save that station", null, EncoderInteractionTimings.SelectorNoticeMs);
    }
  }
```

> **Builder — two things about this method.**
>
> 1. **`SlotOfAsync` is a small private helper** that reloads the bank and derives the per-band
>    ordinal the same way `RefreshAsync` does. It exists because `AddPresetAsync` returns a
>    `RadioPreset`, which has **no `SlotNumber`** — the ordinal is a projection, not a stored field.
>    Do not add `SlotNumber` to `RadioPreset` to avoid this; it would change a persisted model to
>    save one query, and it would then have to be maintained through every delete.
> 2. **The `when (ex.Message.Contains("Maximum"))` filter is matching a message this repo already
>    matches on** — `RadioController.cs:651` does the same. It is not a pattern to be proud of, and
>    the comment should not pretend otherwise; it is the contract `RadioPresetService` currently
>    offers, and changing that contract is out of this row's scope. Note it in
>    `design/FUTURE-WORK.md` (Task 11) as a typed-exception candidate.

**Add** to `src/Radio.Core/Configuration/EncoderInteractionTimings.cs`:

```csharp
  /// <summary>
  /// How long a selector notice stays up, in milliseconds — "Saved to 05", "PRESETS FULL"
  /// (handoff §6.6 State F).
  /// </summary>
  public const int SelectorNoticeMs = 2000;

  /// <summary>
  /// How long a short v1-boundary notice stays up, in milliseconds — "Only radio stations can be
  /// saved". Shorter than <see cref="SelectorNoticeMs"/> because it reports a rule rather than a
  /// result, and the user is still holding the knob when it appears (handoff §4.4).
  /// </summary>
  public const int SelectorNoticeShortMs = 1500;
```

**Register** in `AudioServiceExtensions.AddRotaryEncoders`, beside `SourceSelectorService` and by
an explicit factory for the same reason:

```csharp
    // ENC-7. Singleton for the same reason SourceSelectorService is. It reaches the scoped preset
    // repository through IServiceScopeFactory rather than capturing it, and defers IAudioManager and
    // IRadioBandMemory through Func<> so RotaryEncoderRegistrationTests' minimal provider still
    // resolves the router.
    services.AddSingleton<PresetSelectorService>(sp => new PresetSelectorService(
      sp.GetRequiredService<ILogger<PresetSelectorService>>(),
      sp.GetRequiredService<IServiceScopeFactory>(),
      () => sp.GetRequiredService<IAudioManager>(),
      () => sp.GetRequiredService<IRadioBandMemory>(),
      sp.GetRequiredService<IEncoderFeedbackSink>()));
```

`IServiceScopeFactory` is registered by the container itself and is always available, so it is the
one dependency here that needs no deferral.

**Extend** `tests/Radio.Infrastructure.Tests/DependencyInjection/RotaryEncoderRegistrationTests.cs`
with `AddRotaryEncoders_ResolvesThePresetSelector`, and leave
`AddRotaryEncoders_ResolvesTheActionRouter` green on its existing minimal provider.

---

### Phase 2 — the router, finally correct

#### Task 4 — Index 2 becomes PRESETS

**Edit** `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs`.

**4a. The class XML doc** — replace `ENC-5`'s paragraph with the end state and nothing hedged:

```csharp
/// <para>
/// <b>Index mapping: 0 = Volume, 1 = Source, 2 = Presets, 3 = Tuning.</b> This matches the cabinet's
/// engraving (VOLUME / SOURCE / PRESETS / TUNING) and the per-encoder configuration ENC-11 pushes to
/// the device. The transitional mismatch that ENC-4 and ENC-5 documented is closed.
/// </para>
```

**4b. Dispatch:**

```csharp
        case 2: HandlePresetsTurn(e.EncoderIndex, e.Delta); break;
```

```csharp
        case 2: HandlePresetsPress(); break;
```

**4c. The handlers**, replacing `HandleVizTurn` / `HandleVizPress`:

```csharp
  // --- Encoder 2: PRESETS ---

  private void HandlePresetsTurn(int index, int delta)
  {
    // ENC-3 clamp: one detent, one entry, always — the same rule as SOURCE, which is what keeps the
    // two adjacent selector knobs interchangeable in the hand.
    _presetSelector.EncoderIndex = index;
    _presetSelector.Turn(Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp));
  }

  private void HandlePresetsPress()
  {
    _presetSelector.Press();
  }
```

**4d. Delete the visualiser handlers and the `VisualizationModeService` dependency from the router.**

⚠ **Delete the field and the constructor parameter too, and remove the argument from the router's
factory in `AudioServiceExtensions.cs:418`.** Leaving an unused injected service behind is how a
reader concludes the knob still does something.

⚠ **Do NOT delete `VisualizationModeService` itself, its DI registration, its
`AudioStateUpdateService` subscription, or the `VisualizationModeChanged` broadcast.** They serve the
on-screen picker (handoff §11 item 5, shipped as `ENC-9a`) and the System Config dropdown. This row
removes an *input*, not a capability.

**4e. The long-press consumer.** `OnLongPress` currently returns early for anything but index 0.
Both spec consumers now exist:

```csharp
  private void OnLongPress(int index)
  {
    // The two long-press consumers the spec defines, and there is deliberately no third: VOLUME to
    // standby and PRESETS to save. SOURCE and TUNING have no long action at all.
    switch (index)
    {
      case 0:
        if (_sleepService is null)
        {
          _logger.LogDebug("Volume long-press ignored: no sleep service is registered");
          return;
        }

        _ = EnterStandbyAsync();
        break;

      case 2:
        _presetSelector.LongPress();
        PublishHold(2, EncoderHudPhase.HoldCommit);
        break;
    }
  }
```

**4f. `PublishHold` gains index 2.** `ENC-4` guards it on `index == 0` with the reason *"a ring
drawing on the other three would promise an action nothing performs."* That reason now stops being
true for index 2:

```csharp
  private void PublishHold(int index, EncoderHudPhase phase)
  {
    // Only the knobs with a long action publish hold phases. A ring that fills on SOURCE or TUNING
    // would promise something neither does.
    if (index is not (0 or 2))
    {
      return;
    }

    if (index == 2)
    {
      _hud.Publish(new EncoderHudEventArgs
      {
        EncoderIndex = 2,
        Label = phase == EncoderHudPhase.HoldStart ? "HOLD TO SAVE" : "PRESETS",
        Phase = phase,
      });
      return;
    }

    // ... ENC-4's volume branch unchanged
  }
```

⚠ **`ENC-4`'s comment on that guard must be rewritten, not extended.** It currently says encoder 0 is
*"the only index `OnLongPress` acts on"*, which this task makes false. A stale safety comment beside
a widened guard is precisely the failure mode `CLAUDE.md` §Pre-Merge Review lists three shipped
examples of.

**4g. Disconnect teardown.** `ENC-5` Task 7f wired `ConnectionChanged` to
`_sourceSelector.Dismiss()`. Add `_presetSelector.Dismiss()` beside it — an overlay you can no longer
navigate is a trap, and a half-teardown is worse than none because only one of the two knobs recovers.

Constructor gains `PresetSelectorService presetSelector`; the DI factory gains
`presetSelector: sp.GetRequiredService<PresetSelectorService>()`.

---

#### Task 5 — The mapping test reaches its final state

**Edit** `tests/Radio.Infrastructure.Tests/Platform/Input/RotaryEncoderRouterMappingTests.cs`.

Change the `[Theory]` row `(2, "VISUALIZER")` to `(2, "PRESETS")`, and replace the XML doc's
"expected to change once more" paragraph, which is no longer true:

```csharp
  /// <summary>
  /// Pins the final index-to-handler mapping: 0 = VOLUME, 1 = SOURCE, 2 = PRESETS, 3 = TUNING.
  ///
  /// <para>
  /// This matches the escutcheon (D2) and the configuration ENC-11 pushes to the device. A red
  /// assertion here means the router and the cabinet have diverged, and on the volume row it means
  /// they have diverged on the knob with a safety hazard behind it.
  /// </para>
  /// </summary>
```

**Two more shipped `ENC-4` tests go red here, and these are their real names:**

| Shipped test | Why it goes red | Do |
|---|---|---|
| `SelectorLongPress_DoesNothing` | it holds **index 2** for 1 s and asserts no `HoldStart` — index 2 now saves a preset | **move it to index 1** (SOURCE), which still has no long action, and keep the assertions |
| `HoldStart_IsPublishedForVolumeOnly` | it loops all four indices and asserts a single `HoldStart` on index 0 | **rename to `HoldStart_IsPublishedForVolumeAndPresetsOnly`** and assert two: index 0 labelled `HOLD FOR STANDBY` and index 2 labelled `HOLD TO SAVE`, with indices 1 and 3 silent |

Add:

1. `PresetsTurn_PublishesInItsOwnQuarter` — `EncoderIndex == 2`.
2. `PresetsTurn_PlaysNothing` — the fake manager records no `GetOrCreateSourceAsync` and the fake
   radio no `SetFrequencyAsync`. **Handoff §4.4: turn moves a highlight, nothing plays.**
3. `PresetsLongPress_PublishesAHoldStartRing` — the ring must draw on the knob that saves, since
   `ENC-4`'s reason for suppressing it was that a ring elsewhere "would promise an action nothing
   performs". Index 2 now performs one.
4. `SourceLongPress_StillDoesNothing` / `TuningLongPress_StillDoesNothing`.
5. `Router_NoLongerDependsOnVisualizationModeService` — a constructor-signature reflection guard, so
   re-adding the visualiser knob is a deliberate act.

---

### Phase 3 — the rename

#### Task 6 — `MEMORY` → `PRESETS`

**Why:** §0.5. D10 makes the engraving `PRESETS`, and the word has to be the same on the escutcheon,
in the overlay and on the screen or it is worse than either choice alone.

**The one functional change** — `src/Radio.Web/Components/Shared/RadioControlPanel.razor:249`:

```razor
          PRESETS · @presetCount saved
```

The separator is U+00B7 MIDDLE DOT and does not change.

**The one test that fails** — `tests/Radio.Web.Tests/Components/Shared/RadioControlPanelTests.cs:835`:

```csharp
    Assert.Contains("PRESETS", count.TextContent);
```

It sits in `PresetsHeader_ShowsTotalSavedCount` (`:819-839`), which also asserts `"2 saved"` and
`DoesNotContain(" of ")`. Leave both.

**Stale comments to correct while the files are open** — cosmetic, but each one describes this bank
and would send the next reader to the wrong word:

| File:line | Now reads | Becomes |
|---|---|---|
| `src/Radio.Web/Components/Shared/RadioControlPanel.razor:232` | `Presets Memory Bank — handoff §P1·2` | `Presets bank — handoff §P1·2` |
| `src/Radio.Web/Components/Shared/RadioControlPanel.razor:831` | `/* ═══ Presets Memory Bank ═══ */` | `/* ═══ Presets bank ═══ */` |
| `src/Radio.Web/Components/Shared/PresetCard.razor:2, :6` | `the Home MEMORY rail` | `the Home PRESETS rail` |
| `src/Radio.Web/Components/Shared/PresetCardVariant.cs:5` | `the Home MEMORY rail` | `the Home PRESETS rail` |
| `src/Radio.Web/wwwroot/css/design-system.css:4230` | `§X Memory Presets Grid (PR 3)` | `§X Presets Grid (PR 3)` |
| `src/Radio.Web/wwwroot/css/design-system.css:4234` | `the "MEMORY · n of N" count` | `the "PRESETS · n saved" count` — **also fixes a second staleness**: the header has not read `n of N` since the PR #371 hot-fix |
| `src/Radio.Core/Models/RadioBandModel.cs:25` | `Drives the <c>MEMORY · n of N</c> header count` | `Drives the <c>PRESETS · n saved</c> header count` — same double staleness |
| `tests/.../PresetCardTests.cs:12` | `Rail = Home MEMORY rail` | `Rail = Home PRESETS rail` |
| `tests/.../RadioControlPanelTests.cs:571` | `memory presets` | `presets` |
| `tests/Radio.Infrastructure.Tests/Services/RadioBandServiceTests.cs:9` | `per-band memory-slot count` | `per-band preset-slot count` |

**Add a rename note** to `docs/design-handoffs/HANDOFF-saved-station-display.md` — a short block under
its Status line pointing at Rev 3 and punch-list §6, so the deviation is discoverable **from the
handoff that was deviated from**, which is the only place a future consistency pass will look.

⚠ **Scope the verification grep, or it reports ~90 false positives:**

```bash
rg -n "MEMORY" src tests docs design --glob '!**/bin/**' --glob '!**/obj/**' --glob '!.claude/worktrees/**'
```

All three exclusions matter. `bin`/`obj` hold binaries and **generated XML doc files** carrying the
string from `RadioBandModel.cs:25` — they regenerate from the source comment and **must not be
hand-edited**. `.claude/worktrees/agent-*` holds stale full copies of `src/` and `tests/`, including
duplicate `RadioControlPanelTests.cs` files.

**Do NOT rename** — these are unrelated and a repo-wide find/replace will break them:

- `MemoryStream`, `IMemoryCache`, `MemoryMarshal`, `AsMemory`, `AddMemoryCache`, `Data Source=:memory:`
- The metrics surface: `SystemMonitorService.cs:44,129`, `MetricDescriptor.cs:11,29`,
  `MetricTile.razor:32`, `MetricsDashboardPage.razor:18,450,585,606`
- **References to the project's `MEMORY.md` notes file**, which read like the bank and are not:
  `OutputPickerDropdown.razor:7`, `WeatherDisplayOptions.cs:14`, `AudioServiceExtensions.cs:182`,
  `CastDeviceDropdownTests.cs:30`, `OutputPickerDropdownTests.cs:29`
- `docs/design-handoffs/HANDOFF-saved-station-display.md`'s own body, other than the new note above.
  **It is the record of what was deviated from; rewriting it erases the deviation.**

---

### Phase 4 — tests

#### Task 7 — `PresetSelectorService` tests

**Create** `tests/Radio.Infrastructure.Tests/Platform/Input/PresetSelectorServiceTests.cs` with a
fake `IRadioPresetService` behind a real `ServiceCollection`-built `IServiceScopeFactory`, a fake
`IAudioManager`, a fake `IRadioControl`, a fake `IRadioBandMemory` and a recording
`IEncoderFeedbackSink`.

| # | Test | Pins |
|---|---|---|
| 1 | `Turn_OpensTheOverlayAndMovesOneEntry` | §4.4 |
| 2 | `Turn_PlaysNothing` | **the whole turn contract** |
| 3 | `Rows_AreOrderedByBandThenSlot_MatchingTheOnScreenRail` | the three-orderings hazard |
| 4 | `Rows_CarryThePerBandOrdinal_SoTwoBandsCanBothShowSlotOne` | D-1 |
| 5 | `Rows_MarkTheCurrentlyTunedPresetAsCurrent` | |
| 6 | `Rows_UseTheSameOneHertzToleranceAsTheOnScreenRail` | |
| 7 | `EmptyBank_PublishesTheInstructionalEmptyState` | **State B — the knob teaches its own use** |
| 8 | `Recall_FromANonRadioSource_ActivatesRadioThenSetsBandThenFrequency` | **§4.3 — the reason this knob was chosen**, asserted in order |
| 9 | `Recall_FromANonRadioSource_PublishesCommittingThenPreview` | State D |
| 10 | `Recall_WhileAlreadyOnRadio_DoesNotRecreateTheSource` | |
| 11 | `Recall_StopsAnInFlightScanFirst` | |
| 12 | `Recall_OfADeletedPreset_PublishesFailed_AndRefreshes` | the touchscreen deleted it mid-overlay |
| 13 | `Recall_WhenSetFrequencyThrows_PublishesFailed_AndDoesNotRethrow` | `SDRRadioAudioSource` throws |
| 14 | `Recall_RecordsTheBandMemory` | |
| 15 | `Save_OnRadio_AddsAPresetAndReportsItsSlot` | |
| 16 | `Save_UsesRdsStationNameStable_WhenPresent` | |
| 17 | `Save_FallsBackToTheDefaultName_WhenThereIsNoRdsName` | `RadioPreset.GetDefaultName` |
| 18 | `Save_OnANonRadioSource_ReportsTheV1Boundary_AndWritesNothing` | **§4.4 — a clearly-messaged boundary, not a silent failure** |
| 19 | `Save_WhenTheBankIsFull_ReportsPresetsFull_AndWritesNothing` | **never overwrites** |
| 20 | `Save_OfAStationAlreadySaved_ReportsAlreadySaved_AndWritesNothing` | **§0.3 — the case the handoff does not cover** |
| 21 | `Save_NeverCallsAnyOverwriteOrDeletePath` | asserts the fake saw no delete/rename. The guard for the one gesture that writes |
| 22 | `Dismiss_ClosesWithoutRecalling` | the `ENC-0` disconnect teardown |

---

#### Task 8 — Web tests

The Web has one string change, so it has one test change (Task 6). **Add two component tests
covering the preset list through the shared overlay**, in
`tests/Radio.Web.Tests/Components/Shared/EncoderSelectorOverlayTests.cs`:

1. `PresetRows_RenderTheOrdinalCell` — a source list has no ordinals and a preset list does; this is
   the only rendering difference between the two lists, and it is worth one assertion that the same
   component handles both.
2. `FiftyPresetRows_StillRenderSeven` — the `ENC-5` window under this row's real load. It is the
   assertion that would have caught `ENC-5` shipping a fixed seven-row list.

⚠ **If either test requires a change to `EncoderSelectorOverlay.razor`, stop and say so in the PR.**
That is the drift signal §0.2 item 2 is watching for.

---

### Phase 5 — docs

#### Task 9 — Encoder documentation reaches its final state

1. **`design/INTEGRATIONS.md` § 1.** `ENC-5` Task 18 rewrote the mapping table with index 2 on the
   visualiser. Update it to the final `VOLUME · SOURCE · PRESETS · TUNING`, including the long-press
   column — **there are exactly two long-presses in the whole design** (volume→standby,
   presets→save) and the table is the place a future reader will look for that.
2. **`docs/HANDOFF-NEXT-SESSION.md`** — delete the **"Known mismatch, deliberate"** section. It is
   closed by this PR, and a closed warning left in a start-here document costs the next session the
   time it takes to prove it is stale.
3. **`docs/HANDOFF-GA-PUNCH-LIST.md`** — mark `ENC-7` shipped, and record under the row: the seven
   slots that are actually fifty (§0.3), the `ALREADY SAVED` message added to the spec, the four
   §0.4 deviations, and **the note that `ENC-9` is now the only thing standing between the removed
   visualiser knob and the handoff §11 replacements** (the segment picker and the System Config
   dropdown still ship, so nothing is lost today).
4. **`design/FUTURE-WORK.md`** — three things this row found and deliberately did not fix:
   - `RadioPresetService` signals *bank full* and *duplicate* through **exception message text**, and
     two call sites now match on those strings (`RadioController.cs:646,651` and this row's
     `SaveAsync`). A typed exception or a result object is the fix; it is a breaking change to a
     shipped service and does not belong in a P0 encoder row.
   - `RadioApiService.SavePresetAsync` / `LoadPresetAsync` / `RenamePresetAsync` return `bool` and
     **discard the server's `RadioPresetDto`**, so every touchscreen mutation is followed by a full
     refetch. `RenamePresetAsync`'s XML doc (`RadioApiService.cs:325`) promises *"the new name on
     success"* and returns a `bool` — a wrong doc comment, which is the failure class this repo has
     a pre-merge rule about.
   - `RadioPreset` ordering is derived three different ways in one stack — the repository sorts by
     `Name`, `RadioControlPanel` by band/slot/created, `RadioPage` by `CreatedAt`.
5. **`docs/ROADMAP.md`** — the encoder arc's row is added by the same PR that carries these plans;
   mark `ENC-5` and `ENC-7` as landed when they do.

---

## 3. Test Plan

### 3.1 Automated gates

```bash
dotnet build --configuration Release          # 0 warnings — warnings are errors in Release
dotnet test  --configuration Release
```

| Project | File | Count |
|---|---|---|
| `Radio.Infrastructure.Tests` | `PresetSelectorServiceTests.cs` | 22 |
| `Radio.Infrastructure.Tests` | `RotaryEncoderRouterMappingTests.cs` (**edited**) | ~14 |
| `Radio.Web.Tests` | `EncoderSelectorOverlayTests.cs` (extended) | +2 |
| `Radio.Web.Tests` | `RadioControlPanelTests.cs` (**one assertion edited**) | 0 net |

⚠ **Two `ENC-5` tests are expected to fail before Task 5 and Task 6 run:** the `(2, "VISUALIZER")`
theory row, and `RadioControlPanelTests:835`'s `Assert.Contains("MEMORY", …)`. Both are assertions
moving with a decision, not regressions.

### 3.2 Deploy

```powershell
./deploy/Deploy-ToLinux.ps1
```

No flags. The deploy verifies both services by SHA and reports kiosk liveness by
established-connection count. `journalctl` carries WARNING and above only since `LOG-11`; the
Information lines this row logs (`Recalled preset …`, `Saved preset … to … slot …`) are in
`/opt/radio-console/logs/radio-*.txt`.

### 3.3 Browser UAT — Tester drives these on the box at 1920×720

Prerequisite: encoder connected and `Configured`; a tuner present; **the bank seeded with at least
three presets across two bands** (save two FM and one AM from the touchscreen first), and one
Bluetooth device paired.

**A · All four knobs finally say what the cabinet says**

| # | Steps | Expected |
|---|---|---|
| A1 | Turn knob 1 | Volume card, far-left quarter. Volume changes |
| A2 | Turn knob 2 | The SOURCE overlay |
| A3 | Turn knob 3 | **The PRESETS overlay** — this is the row |
| A4 | Turn knob 4 | Tuning card, far-right quarter. The station changes |
| A5 | Turn knob 3 and confirm nothing else moves | The visualiser mode does **not** change. Its knob is gone by design |
| A6 | Change the visualiser from the six-segment picker on Home | Still works. The capability moved, it did not go |

**B · Turn — and nothing plays**

| # | Steps | Expected |
|---|---|---|
| B1 | With FM playing, turn PRESETS one detent | The overlay opens, headed `PRESETS · n saved`, footer `PRESS TO PLAY · HOLD TO SAVE` |
| B2 | Spin through every entry | **The audio never changes.** Not once, at any speed |
| B3 | Confirm the row shape | Zero-padded ordinal, name primary, `BAND frequency` secondary |
| B4 | Compare with the on-screen bank | Same stations, same order, same ordinals |
| B5 | With an FM preset tuned, look for the current marker | The matching row carries it |
| B6 | Spin fast | Exactly one entry per detent |
| B7 | Wait 4 s | Dismisses. Nothing changed |

**C · Recall — the reason this knob exists**

| # | Steps | Expected |
|---|---|---|
| C1 | On FM, open the overlay, highlight another FM preset, press | Tunes to it and plays. No spinner — radio was already active |
| C2 | Highlight an **AM** preset and press | Band changes to AM and it tunes. The on-screen band pill follows within ~500 ms |
| C3 | **Switch to Bluetooth**, then turn PRESETS | The overlay opens with **the same stations** — the list is not scoped to the active source |
| C4 | Press one | **Spinner (State D), then FM/AM comes up and plays.** *(This is the single most important check in the row)* |
| C5 | Press PRESETS with the overlay closed | It opens, and **nothing audible happens** |
| C6 | Delete a preset from the touchscreen while the overlay is open on it, then press | A failure card, not a crash and not silence |

**D · Save**

| # | Steps | Expected |
|---|---|---|
| D1 | Tune FM to an unsaved station, hold PRESETS | A ring draws from ~300 ms and completes at ~600 ms, labelled `HOLD TO SAVE` |
| D2 | Continue past 600 ms | **`Saved to NN`** plus the name and frequency, ~2 s. It fires **while still held** |
| D3 | Release | Nothing further happens. The release does not also recall |
| D4 | Check the on-screen bank | The station is there, at the slot the HUD named |
| D5 | Hold on the **same** station again | **`ALREADY SAVED · slot NN`**, ~1.5 s, and the bank count does not change |
| D6 | Switch to **Bluetooth** and hold PRESETS | **`Only radio stations can be saved`**, ~1.5 s. Nothing is written |
| D7 | Release at ~450 ms during a hold | The ring collapses and the **short press fires** — the overlay opens or recalls |
| D8 | Confirm across all of D | **No existing preset was ever modified or deleted** |

**E · The empty state**

| # | Steps | Expected |
|---|---|---|
| E1 | Delete every preset from the touchscreen, then turn PRESETS | **`NO STATIONS SAVED`** / `hold this knob to save what's playing` — the second line in lower case, and no footer |
| E2 | Follow that instruction | A station is saved and the list appears. **The knob taught its own use** |

**F · A long list**

| # | Steps | Expected |
|---|---|---|
| F1 | Seed ~12 presets (script it via `POST /api/radio/presets`), then turn PRESETS | **Seven rows visible**, and the overlay does not overflow the content area |
| F2 | Spin to the bottom | The window scrolls to keep the highlight visible |
| F3 | Spin past the end | Wraps to the top |

**G · Long-press discipline**

| # | Steps | Expected |
|---|---|---|
| G1 | Hold SOURCE (knob 2) for 1 s | **No ring, nothing happens** beyond the short press on release |
| G2 | Hold TUNING (knob 4) for 1 s | **No ring, nothing happens** |
| G3 | Hold VOLUME for 1 s | Standby, exactly as `ENC-4` shipped it |
| G4 | Count the long-presses on the panel | **Exactly two.** Handoff §4.4: there is no third, deliberately |

**H · Sleep, teardown, load**

| # | Steps | Expected |
|---|---|---|
| H1 | Navigate directly to `/sleep` (**not** the Sleep pill) and turn PRESETS | One dim line — `PRESETS · <name>`. Not the overlay |
| H2 | Open the overlay, then **unplug the encoder** | Dismisses **without recalling**, and `ENC-0`'s toast fires |
| H3 | Play audio, spin PRESETS continuously for 30 s | **No audible distortion.** Each turn triggers a bank read; if the read is on the hot path this is where it shows |
| H4 | During H3, sample SignalR frames | `EncoderHudChanged` at **≤ 20 Hz** |

**I · Naming — handoff §15's own check**

| # | Steps | Expected |
|---|---|---|
| I1 | Home, `RadioControlPanel`'s bank header | **`PRESETS · n saved`** |
| I2 | The overlay heading | `PRESETS` |
| I3 | The hold notices and the empty state | All say `PRESETS`, none says `MEMORY` |
| I4 | Walk `/`, `/queue`, `/metrics`, `/devices`, `/history`, `/phone`, `/radio` | **No surface says `MEMORY`** |
| I5 | AT-SPI dump of the overlay | The screen-reader text says `PRESETS` |

**J · Regression**

| # | Steps | Expected |
|---|---|---|
| J1 | The SOURCE overlay in full — `ENC-5` UAT B, C, D | Unchanged |
| J2 | Volume: turn, press, hold to standby | Unchanged |
| J3 | The on-screen bank: kebab → rename, kebab → delete, long-press a band pill to save | All three still work |
| J4 | `RadioPage`'s presets panel | Still renders and still recalls |

### 3.4 The four highest-weighted checks

1. **C4** — recall from Bluetooth. Handoff §4.3 chose this knob over three alternatives on exactly
   this behaviour. If it does not work from a non-radio source, the row has shipped a preset button
   that only works when you are already on the radio.
2. **D8 + D5** — save never destroys, and a duplicate says so. The one gesture on the panel that
   writes data.
3. **B2** — turning plays nothing. The preview contract, and the reason the knob is safe to spin.
4. **I4** — one word on the escutcheon, in the overlay and on the screen. It is a physical engraving;
   it cannot be edited later.

---

## 4. Self-review

**Spec coverage** — handoff §4.4 Knob 3 and §6.6, item by item:

| Handoff item | Where |
|---|---|
| Turn moves the highlight, **nothing plays** | Task 1; tests 1–2; UAT B1, B2 |
| Acceleration disabled | `ENC-3` clamp + `ENC-11` device config, both shipped; UAT B6 |
| The list is the existing bank, slot order, `PresetCard` field hierarchy | Task 1; tests 3–6; UAT B3, B4 |
| Fixed slot positions | Task 1's ordering; test 3; §0.4 D-1 |
| Press = Recall, switching source and band if needed | Task 2; tests 8–11; UAT C1–C4 |
| Recall not scoped to the active source | Task 1 composition + §1.3; test 8; UAT C3 |
| Closed-overlay press just shows the list | Task 2 `Press`; UAT C5 |
| Long-press 600 ms = Save to the next free slot | Tasks 3, 4e; test 15; UAT D1, D2 |
| The 300→600 ms ring | Task 4f; UAT D1 |
| **Never overwrites** | Task 3 + §0.3; tests 19, 21; UAT D8 |
| Bank full ⇒ `PRESETS FULL — replace a slot on screen`, 2 s, writes nothing | Task 3; test 19 |
| v1 saves radio only ⇒ `Only radio stations can be saved`, 1.5 s | Task 3; test 18; UAT D6 |
| Instructional empty state | Task 1; test 7; UAT E |
| **`MEMORY` → `PRESETS` everywhere, incl. the on-screen bank** | Task 6; UAT I |
| Same interaction grammar as SOURCE | `ENC-5`'s `EncoderSelectorState` + overlay, reused unchanged |
| §6.6 State D / E / F | Tasks 2, 3 through `ENC-5`'s component |
| §6.9 no new tokens | Nothing in this row touches CSS beyond two comment lines |
| §8.3 consumed input shows a value, not the overlay | `ENC-5` Task 13, already shipped; UAT H1 |
| §4.2 exactly two long-presses on the panel | Task 4e; test 4; UAT G4 |
| §11 visualisation capability preserved | Task 4d; UAT A5, A6 |
| Cross-source presets | **v2** — out of scope, handoff §12.1 item 3 |

**Placeholder scan:** no `TBD`, no "similar to Task N", no "implement later". Two places name a
decision Builder makes and state the criterion instead of faking it: `SlotOfAsync`'s implementation
(Task 3, with an explicit instruction *not* to add `SlotNumber` to the persisted model) and the
`when` filter's message match (Task 3, recorded as debt rather than defended).

**Scope check:** no new preset data model; no overwrite, reorder or save-to-slot path; no change to
`PresetCard.razor`'s markup or parameters; no change to `EncoderSelectorOverlay.razor`; no new design
token; no change to `VisualizationModeService` or its broadcast; no wake model or blanking
(`ENC-6`/`ENC-15`); no settings surgery (`ENC-8`).

**Type consistency:** `RadioPreset.Frequency` is a **`double` of hertz** and `RadioPreset.Band` is
the `RadioBand` **enum**, while both `RadioPresetDto`s carry `Band` as a **string** — this row works
against the domain model on the API side and never crosses that boundary, so no parse is needed.
`Frequency` takes hertz through `new Frequency(long)`, hence the `(long)` casts. The two
`RadioPresetDto` types (`Radio.API.Models` sealed init-record, `Radio.Web.Models` positional record
with **`Frequency` before `Band`**) are untouched here.

**Comment-accuracy scan:** four comments in this plan make a claim about safety or scope, and each is
written against what the diff actually does — the scope-factory rationale (Task 1), the "no overwrite
path exists" phrasing rather than "a guard prevents it" (Task 3), the widened `PublishHold` guard
whose stale predecessor is explicitly called out (Task 4f), and the `when` filter's honest note that
it matches a message string because that is the contract the service offers.

---

## 5. Things this plan deliberately does not do, with the reason

1. **Persisted slot numbers.** §0.3. Adding `SlotNumber` to `RadioPreset` and the SQLite table would
   turn a derived projection into state that has to be maintained through every delete, to save one
   query in one place.
2. **A typed exception on `RadioPresetService`.** Task 9 item 4 records it. It is a breaking change
   to a shipped service with an existing string-matching caller in `RadioController`, and folding it
   into a P0 encoder row is how a reviewable PR stops being one.
3. **Enforcing per-band capacity.** `BandPresetCapacity` is advisory today and nothing enforces it.
   Making it real would change what the touchscreen's save does, which is not this row's to change.
4. **Cross-source presets.** Handoff §12.1 item 3 — v2, needs a favourites model that does not exist.
5. **`ENC-9`'s canvas tap and long-press.** §0.6. The visualiser's capability survives on the
   six-segment picker and the System Config dropdown, both shipped, so this row does not block on a
   P1 item — but it flags that the row is unqueued.
6. **Overwrite-from-the-knob, in any form.** Handoff §4.4 and §4.5. Replacement lives on the
   touchscreen, behind the kebab, where it has a confirmation and an undo. A knob that can destroy a
   saved station has a failure mode nothing on this panel currently has.
