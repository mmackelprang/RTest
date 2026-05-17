# Implementation Script — Radio Controller Polish

> **For the implementing agent (Claude Code or human):**
> Companion to `design_handoff_radio_console/IMPLEMENTATION.md`. That script
> handled the shell. This one covers the four radio-source surfaces.
>
> Land changes in priority order (P0 → P1 → P2). Do **not** start any
> section whose status is not `[APPROVED]`.
>
> All tokens referenced live in
> `src/Radio.Web/wwwroot/css/design-system.css`. Do not invent new tokens;
> extend the token block first if you need a value that isn't there.

---

## Status legend

| Status | Meaning |
|---|---|
| `[APPROVED]` | Ship as specified. |
| `[PENDING REVIEW]` | Drafted, not locked. Don't start. |
| `[NEEDS ITERATION]` | User has comments; see the inline note. |
| `[PARKED]` | Out of scope. |

All findings below default to `[PENDING REVIEW]`.

---

## P0 — bugs

---

### P0·1 — Signal meter clamp + CLIP indicator

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"Signal meter — 118 % is impossible"**, Analysis § 01
**Files:**
- `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (`.rcp-meter` block)
- `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (`@code` — value mapping)
- `src/Radio.Web/wwwroot/css/design-system.css` (`§N Signal meter` — add scale labels)
- API/model: wherever `RadioStateDto.SignalStrength` is produced — surface a separate
  `IsOverdriven` / `Clip` boolean.

**Steps:**

1. **Clamp at the API boundary.** In whatever service projects the raw RTL-SDR power
   reading into `RadioStateDto.SignalStrength`, clamp the value to `[0, 100]`. Drop the
   absolute value into a separate `RssiDbu` field (mapped from the raw reading to a
   −60 → 0 dBu range; use a linear fit if the device doesn't expose a calibrated curve).
   Add a `Clip` boolean that is `true` when the raw value exceeded the calibrated
   full-scale.

2. **Update the meter markup** in `RadioControlPanel.razor`:
   - Replace the `<span class="rcp-meter-value">@_radioState.SignalStrength%</span>` with
     two siblings: a `CLIP` pill (rendered only when `_radioState.Clip` is true) and
     a dBu readout (`@_radioState.RssiDbu dBu`).
   - Add a scale-label strip under the meter bar with four mono labels:
     `−60`, `−30`, `−12`, `0 dBu`.

3. **Update the segment logic.** Drive the 20-segment fill off the *clamped* percent value;
   the per-segment colour thresholds (green / amber / red at `i < 12 / 17 / else`) stay.

4. **Style the CLIP pill** in `design-system.css`. Mono, 9 px, uppercase, 1 px solid
   `var(--signal-red)`, background `rgba(248,113,113,0.10)`, box-shadow
   `0 0 6px rgba(248,113,113,0.4)` while active.

5. Update the SCANNING indicator's stop-threshold check to use the clamped value or
   the dBu reading consistently. `_radioState.ScanStopThreshold` should be a dBu
   value in the proposed scheme, not a percentage.

**Acceptance:**

- [ ] Signal value never exceeds 100 % in the UI for any raw input.
- [ ] CLIP pill appears only when the front-end is actually overdriving.
- [ ] dBu scale labels render under the bar in mono 8–9 px.

**Notes:**

If recalibrating to dBu is risky, ship step 1's clamp + clip flag first as a hot-fix,
then layer the dBu conversion behind a feature flag.

---

### P0·2 — AGC strip: always-full two-cell layout

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"AGC / gain strip — never half-empty"**, Analysis § 02
**Files:**
- `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (`.rcp-sdr-controls` block,
  including the `<RadzenSwitch>` for AGC and the conditional slider).

**Steps:**

1. Rewrite the `.rcp-sdr-controls` flex into a **fixed two-cell grid**:
   - Left cell: 130 px wide, contains the AGC toggle and an inline `AUTO` / `OFF` chip.
   - Right cell: flex 1, always rendered, content swaps on AGC state.

2. **Left cell behaviour:**
   - Tap the cell anywhere → toggle AGC (the existing `<RadzenSwitch>` becomes a styled
     button with the same handler).
   - The chip reads `AUTO` (green border + soft fill) when on, `OFF` (dim border, no fill)
     when off.

3. **Right cell behaviour:**
   - When AGC is **on**: render `"Tuner is choosing"` (mono 11 px, low contrast) on the
     left, and the current gain reading on the right (`28.0 dB` in amber, mono, tabular).
     The right value is bound to a server-pushed `AppliedGain` field on `RadioStateDto`.
   - When AGC is **off**: render the existing slider with two new affordances — a
     leading current-value pill (`28 dB` amber, fixed width 56 px so width doesn't
     jitter as it changes) and a trailing range hint (`0 – 50` in dim mono).

4. **Add `AppliedGain` to `RadioStateDto`.** When AGC is on, this is the value the device
   reports it has selected. When AGC is off, this is the value the user set. Either way,
   the right cell can show one number bound to one field.

5. Remove the empty-cell branch (`<div class="rcp-gain-slot"> … @if (!_radioState.AutoGain && _radioState.Gain.HasValue) { … }</div>`) — the right cell is *always* populated.

**Acceptance:**

- [ ] Toggling AGC does not change the height of the strip.
- [ ] When AGC is on, a numeric gain value is visible in the right cell.
- [ ] The slider's "current value" pill never wraps, never grows or shrinks across the
      slider's range.

---

### P0·3 — Song recognition panel: confidence bucket + stream layout

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P0` → **"Song recognition — kill the 80 % column"**, Analysis § 03
**Files:**
- `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` (the searching/recognition table
  branch — render when `_searching && _matches.Any()`)
- `src/Radio.Web/Components/Shared/ConfidencePips.razor` *(new)*
- `src/Radio.Web/Models/ApiModels.cs` — change `Confidence` from `double` to a
  `ConfidenceBucket` enum (`None`, `Possible`, `Likely`, `Strong`).

**Steps:**

1. **Server side.** In whichever fingerprinter projection lives on the API side, fold the
   raw match score into a bucket:
   - `Strong` — score ≥ 0.90
   - `Likely` — 0.80 ≤ score < 0.90
   - `Possible` — 0.60 ≤ score < 0.80
   - `None` — no match returned
   Expose as `ConfidenceBucket Confidence` on the match DTO. Drop the raw score from the
   API surface (keep it in logs).

2. **Create `ConfidencePips.razor`.** Three 5 × 10 px pips with rounded 1 px corners.
   Parameter: `Bucket`. Pips light according to the bucket:
   - `Strong` → 3 pips, full green
   - `Likely` → 2 pips, light-green
   - `Possible` → 1 pip, amber
   - `None` → 0 pips
   The mono label (`Strong` / `Likely` / `Possible` / `No match`) sits to the right.

3. **Rewrite the recognition section** in `NowPlayingPanel.razor`. Drop the
   `<table>` entirely. Render a flex column of rows:
   - **NOW header** (mono 9 px, amber, 0.20em letter-spacing) once, above the
     currently-playing match (the row whose `MatchId` equals `_radioState.NowPlayingMatchId`).
   - **EARLIER header** below the now row.
   - Each row is a grid: art (34 × 34) · title + artist · `<ConfidencePips />` · time-ago.
   - The active row has a 2 px amber left border, a small amber dot in the top-right corner
     of the art square, and an `now` label instead of a time-ago.
   - Failed-lookup rows render with `track = "No match in window"` in italics, dimmed.

4. **Remove the telemetry strip** (`Fingerprints: X/min · Lookups: Y/min`) from the panel
   header. If the value matters, move it to the dev tray (see prior handoff § P2·2).

5. **Drop the `Art` column entirely.** Use the cover-art square as the row leader —
   present art renders the bitmap, absent art renders a `♪` placeholder in `var(--text-dim)`.

**Acceptance:**

- [ ] No row in the panel contains the literal text `80%`.
- [ ] The currently-playing match is visually anchored at the top of the stream with a
      "NOW" header.
- [ ] A row with no fingerprint match reads as a sentence (`"No match in window"`), not
      as `--`.
- [ ] Confidence reads as a word and a row of pips, never as a percentage.

---

## P1 — structural

---

### P1·1 — Tuner section header + RDS card + tall band buttons

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Tuner header & RDS"**, Analysis § 04
**Files:**
- `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (`.rcp-module-label`,
  `.rcp-band-group`, `.rcp-band-btn`, `.rcp-rds-info` blocks)
- `src/Radio.Web/Components/Shared/RdsCard.razor` *(new)*

**Steps:**

1. **Promote the header.** Replace the absolute-positioned `.rcp-module-label` with a real
   header row at the top of `.rcp-tuner`:
   - Left: `<h3>Tuner</h3>` in Inter 14 px medium.
   - Right: band + range, mono 10 px, amber, uppercase (`FM · 76–108 MHz`).
   - 1 px bottom border, 24 px top margin on the content below.

2. **Tall band pills.** Update `.rcp-band-btn`:
   - Increase to `min-width: 64px; padding: 8px 14px 6px;`
   - Inner layout: two lines (label `13 px / 700 / 0.08em`, sub `8 px / 0.14em / dim`).
   - The sub-range string comes from `RadioBandModel.Range` — add the field if it isn't
     already there, populate from `RadioBandModel.GetBandInfo()` server-side.

3. **Extract `RdsCard.razor`** from the existing inline `.rcp-rds-info`:
   - Renders only when `RdsStationName` is present.
   - Layout: mono `RDS` label (9 px dim), station name in cyan accent (mono 14 px,
     0.18em letter-spacing, with the existing text-shadow glow), program-type chip on
     the right.
   - Mount **above** the frequency well, not below.

4. **Add an `RT` (RadioText) line below the frequency well**. Mono 11 px, low contrast,
   one-line ellipsis, bound to `RadioStateDto.RdsRadioText`. Hide entirely if empty.
   Scroll horizontally with `text-overflow: ellipsis` for now; a marquee variant is a
   future-work item, not part of this pass.

**Acceptance:**

- [ ] Tuner header is visible and reads as a heading, not a label.
- [ ] Band pills are at least 56 px tall and show their range sub-text.
- [ ] When RDS is present, the station name appears above the frequency display and is
      the most visually prominent text on the panel after the frequency itself.

---

### P1·2 — Memory presets: slot · name · frequency three-column row

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Memory presets"**, Analysis § 05
**Files:**
- `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (`.rcp-presets`,
  `.rcp-preset-item`, `OpenSavePresetDialog` method)
- `src/Radio.Web/Models/ApiModels.cs` — add `SlotNumber` to `RadioPresetDto` (the
  ordinal already exists implicitly via `OrderBy(CreatedAt)`; promote it to a real field).

**Steps:**

1. **Restructure each row** as a CSS grid `22px 1fr 64px`:
   - Slot number: mono 10 px, right-aligned, `var(--text-lo)` (becomes `var(--signal-amber)`
     when the preset is the active one).
   - Name + band stack: name `var(--text-high)` 12 px medium, band line below at mono
     9 px dim.
   - Frequency: DSEG (`var(--font-led)`) at 13 px, right-aligned, amber, with the existing
     glow shadow when active.

2. **Preset header**:
   - Left: `MEMORY · 7 of 16` (current count / capacity, mono, dim).
   - Right: hint text — `HOLD <kbd>FM</kbd> TO SAVE` — instead of the icon button.
     The save action becomes a long-press on the active band pill rather than a separate
     button.

3. **Empty slots.** Always render the next empty slot as a dashed-border placeholder so
   capacity is legible. Don't render *all* empty slots — that's noise; render the next
   one to-be-filled plus one more.

4. **Save-preset dialog.** When the user invokes "save current," seed the name field
   with `RadioStateDto.RdsStationName` if present, else
   `DisplayNames.Band(_radioState.Band) + " " + _radioState.FrequencyFormatted` only
   as a last-resort fallback. **Never** seed with the raw frequency.

5. Capacity is a per-band concept on the device (16 FM slots, 4 WB slots, etc.). Wire the
   header count to the band-specific capacity.

**Acceptance:**

- [ ] No preset row contains the same frequency twice.
- [ ] Slot numbers are always visible (not hover-only or 20 %-opacity ghosts).
- [ ] The save flow defaults to the RDS station name when one is present.

---

### P1·3 — Now Playing status: unified strip + match badge

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P1` → **"Now Playing status"**, Analysis § 06
**Files:**
- `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`
- `src/Radio.Web/wwwroot/css/design-system.css` (new `§N Status strip`)

**Steps:**

1. **Build a `.np-status-strip`** across the top of the now-playing column. Four cells,
   1 px separators, flex layout:
   - Source: 8 × 8 square in source colour + name (`SDR · RTL-SDR`).
   - Frequency: DSEG 12 px + `MHz` chip (only present when the active source is a tuner).
   - RDS station: cyan accent name + dim `RDS` tag, ellipsis on overflow.
   - Gain: cyan tappable cell that opens the gain popover (see P2·1).
2. **Remove the three independent pills** (`Searching` green, `SDR RADIO (RTL-SDR)`
   purple, `0dB` cyan) — they fold into the strip.
3. **Attach the match badge to the song title** inside the album-art card. Render
   `<ConfidencePips Bucket="@_match.Confidence" />` followed by `"Strong match · 12 s ago"`
   in the relative-time format. When the song is from RDS and not the fingerprinter,
   the line reads `RDS · station-supplied`; when neither, hide the badge entirely
   (don't fake it).
4. The strip uses `backdrop-filter: blur(8px); background: rgba(20,20,22,0.85);`
   only when there's album art behind it; otherwise solid `var(--surface-raised)`.

**Acceptance:**

- [ ] Source, frequency, RDS station, and gain are visually one component.
- [ ] Match confidence is visible directly under the song title and is the same widget
      used in the recognition panel (P0·3).
- [ ] No floating, unrelated pills in opposing corners.

---

## P2 — polish

---

### P2·1 — Gain control popover: peak meter + AUTO + reset

**Status:** `[PENDING REVIEW]`
**Reference:** Canvas → `P2` → **"Gain control popover"**, Analysis § 07
**Files:**
- `src/Radio.Web/Components/Layout/MainLayout.razor` (the gain dropdown — currently inline)
  → extract to `src/Radio.Web/Components/Shared/GainControlPopover.razor`.
- `src/Radio.Web/Services/Hub/AudioVisualizationHubService.cs` — expose a peak-sample
  stream for the active source.

**Steps:**

1. **Extract the popover.** Move the existing slider into `GainControlPopover.razor`,
   parametered by source type. Replace the inline `<div class="overlay">` in MainLayout
   with a single `<GainControlPopover />` element wired to a service that tracks which
   source's popover is open.

2. **Layout the popover** as a 340 px card:
   - Header row: source kicker + "RF gain" title on the left; an `Auto on` / `Auto off`
     pill on the right that toggles AGC.
   - Body: 124 px tall, three columns:
     - Vertical peak meter (16 px wide), driven by hub samples at ~20 fps.
     - Vertical slider (DSEG amber knob, dashed `0 dB` tick at the midpoint).
     - Scale label column (`+6 / +3 / 0 / −12 / −24 / −∞`).
   - Footer: large DSEG current value on the left (24 px amber), `Reset to 0 dB` button
     on the right.

3. **Wire the peak meter.** Subscribe to the active visualization hub's RMS/peak stream
   in `OnInitializedAsync`; keep a hold value that decays at 0.5 dB / 200 ms. Reuse the
   visualizer panel's existing animation cadence so we don't add a second source of
   redraws.

4. **AUTO state.** When AGC is on, dim the slider knob and track to ~35 % opacity and
   block pointer events. Don't hide them — the user can still see what the tuner has
   chosen.

5. **Trim the title.** "SDR Radio (RTL-SDR) Level" → kicker `SDR · RTL-SDR` + title
   `RF gain`. Apply the same pattern to non-tuner sources (Vinyl, File, etc.) — the
   kicker is the source DisplayName, the title is "Gain" or the appropriate device verb.

6. **Reset button.** Tapping `Reset to 0 dB` sets the slider to 0 (unity gain) and posts
   a single hub message. Greyed out when AGC is on.

**Acceptance:**

- [ ] User can see the signal moving while dragging the gain slider.
- [ ] AGC state is visible inside the popover, not just on the toggle below.
- [ ] One-tap reset is available and labelled.

---

## Land order recap

1. P0·1 — signal meter clamp (smallest blast radius, hot-fixable today).
2. P0·2 — AGC strip layout (no API changes if `AppliedGain` already exists; add if not).
3. P0·3 — recognition panel rewrite (touches DTO + UI; do it as a single PR).
4. P1·1 — tuner header + RDS (visual only, can ride on top of P0·1).
5. P1·2 — preset rows (DTO field + UI).
6. P1·3 — status strip (last UI change because it folds together work that P0·3 set up).
7. P2·1 — gain popover (depends on the visualization hub being reachable from the
   popover's render mode; verify before starting).
