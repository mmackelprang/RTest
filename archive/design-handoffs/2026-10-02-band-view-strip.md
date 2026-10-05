# HANDOFF — BAND view: a bottom strip, a clearer axis, preset carets, and SCAN renamed DISCOVER

**Row:** `UI-31` · **Surface:** visualizer panel → **BAND** view (`VisualizerPanel.razor`, `visualizer.js` `drawBandMap` / `registerBandTap`, `design-system.css` §28)
**Author:** Designer · **Date:** 2026-10-02 · **Consumer:** Planner · **Status:** draft, awaiting owner review

> **Owner, 2026-10-02 (verbatim):** "No tooltip needed for the gray 'Band' selection, but I think having some extra context when the band is actually rendered at the bottom below the signal graph would be super useful - consider a large label that tells the radio band name, and a clearer x-axis that shows frequencies and presets as carets. This woudl take some of the vertical height of the signal graph but would make the use of it better. In the text section below the band, we could put some text saying that touching on a signal will snap to that signal and small adjustments can be made in the control panel or by the knob. The 'SCAN' button is confusing since it overlaps meaning with the 'SCAN' buttons on the central panel. Consider changing the name of that button to 'Discover' and moving it to the text band below the graph."

The first sentence (the disabled BAND tab) is out of scope here. No API change: `POST /api/radio/bandmap/scan` stays; only labels, placement and copy change.

### Follows, extends, deviates

| Handoff / prior work | Relation | Detail |
|---|---|---|
| `2026-10-01-radio-presets-bar-and-band-colour.md` §5.1–5.2, §5.5 (UI-22 tiers) | **follows** | Bar colours, thresholds, neutral trace fill and the "amber means tuned here" rule are unchanged. |
| same, §5.4 (legend on the top status line) | **deviates, at the owner's direction** | The legend moves with the status line into the bottom strip. Same text, swatches and hide rules. |
| same, §5.3 (dashed preset ticks + top labels "unchanged") | **deviates, at the owner's direction** ("presets as carets") | Dashed full-height lines and the two label lanes go; presets become carets on the axis. |
| same, §2.1 / §4.2 (preset bar 89 px at the bottom of the centre panel) | **follows** | The new strip is also 89 px, so its top edge lines up with the preset bar's across the panel gap (y ≈ 631 on the 720 px screen). |
| AUD-76 / AUD-91 BAND view (tap-to-tune with snap, per-band axis, empty states) | **extends** | Behaviour unchanged; layout and copy change. |
| `.band-scan-btn` (48 px, `--touch-min`) | **deviates** | Becomes the 58 px Discover button (see §4). |

**No new tokens.** Everything below uses `design-system.css:64-220`.

---

## 1. Vertical budget (panel below the 44 px header: 556 px)

| Region | Today | After |
|---|---|---|
| Top overlay (status + SCAN) | 64 | **0 — removed** |
| Preset label lanes + caret gap | 36 + 12 | **0 — removed** |
| Plot headroom | — | 8 |
| **Plot (bars)** | **≈ 425** (539 canvas − 112 − 2) | **≈ 427** (467 canvas − 8 − 32) |
| Axis | 17 (markup strip, 1 border + 16) | **32, inside the canvas** (§2) |
| Bottom strip | — | **89** (1 border + 10 + 68 + 10) |
| **Total** | 556 | 556 |

**Graph height cost: none (≈ +2 px).** The owner expected to pay plot height; the strip is paid for by deleting the top overlay and the preset label lanes, which this design no longer needs. Builder: re-measure the canvas on the box (AT-SPI extents) — these are from the 1920×720 screenshots.

```
 ┌ WAVE │ SPECTRUM │ RING │ PHASE │ BAND ───────────────────────────────────┐ 44
 │                                                                  8 headroom
 │        ▌                 │          ▌ ▌                          │      │
 │   ▌    ▌  ▌  ▌     ▌     │   ▌  ▌   ▌ ▌  ▌    ▌      ▌          ▌│      │ plot ≈427
 │ ▂▃▌▂▃▂▃▌▃▂▌▃▂▌▂▃▂▂▃▌▃▂▂▂▃│▂▃▌▂▂▌▂▃▂▌▂▌▂▂▌▃▂▂▌▂▃▂▃▂▌▃▂▂▂▃▂▃▂▃▂▃▌▃▂▃▂│      │
 │─────┴──▲──────────┴──────▲───────┴─────────┴─────▲───▲───────┴──────────│ axis line
 │        ▲                 ▲ (amber, tuned)         ▲   ▲                  │ carets
 │ 88               92              96             100             104 108│ labels  32
 ├──────────────────────────────────────────────────────────────────────────┤
 │ FM            DISCOVERED 3 MIN AGO        ▮WEAK ▮FAIR ▮STRONG ┌────────┐ │
 │ 87.5–108 MHZ  Touch a signal to snap to it. ▲ marks a preset. │DISCOVER│ │ 89
 │               Fine-tune with ‹ › on the radio panel or the knob└────────┘ │
 └──────────────────────────────────────────────────────────────────────────┘
```

## 2. Frequency axis

**Where:** the bottom 32 px of the canvas. The canvas keeps the full height of `.visualizer-canvas-wrap`; `plotBottom = height − 32`. The old 16 px `.visualizer-axis.is-band` strip under the canvas is deleted.

**Drawn on the canvas** (pixel-exact with the bars, which are at `x = f * width`):

| y from `plotBottom` | Element | Spec |
|---|---|---|
| 0 | Axis line | 1 px, full width, `--text-low` |
| 1–6 | Tick marks | 1 px × 6 px down from the line at each labelled tick, `--text-medium` |
| 1–9 | Preset carets | §3 |
| 1–12 | Tuned-station caret | §3 |

**Tick labels stay markup** (owner-readable, testable, AT-SPI-visible), in a new `.band-axis-labels` layer absolutely positioned **inside** `.visualizer-canvas-wrap`: `bottom: 2px; height: 18px; pointer-events: none; aria-hidden="true"`. Each label sits at its fraction exactly as today (`left: N%`, the `is-edge-start` / `is-edge-end` alignment rules carried over).

- **Type:** 13 px `--font-mono`, `font-variant-numeric: tabular-nums`, `--text-medium` (≈ 9:1 on `--surface-inset`; today is 10 px `--text-low` ≈ 2.6:1). Letter-spacing 0.04em.
- **Ticks:** unchanged from `BandAxis.Ticks()` (FM 88/92/96/100/104/108; others ≤ 6 nice ticks). Faint vertical gridlines in the plot at the same fractions stay (`--surface-separator`).
- **Units:** **dropped from the axis for every band.** The unit now lives in the strip's range line (§4). This also fixes today's clipped/wrapped `162.55 MHz` at the right edge on WB and VHF. Change `BandAxis.Labels()` and its tests accordingly.

## 3. Presets and the tuned station as carets

**Preset caret:** filled upward-pointing triangle, **10 px wide × 8 px tall**, apex touching the axis line from below (apex at `plotBottom + 1`, base at `plotBottom + 9`), centred on the preset's frequency. Fill `--text-high`. Drawn after tick marks so a preset on a tick reads as a caret.

- **Dashed full-height preset lines: removed.** **Preset text labels: removed** from the canvas (the two 18 px lanes go). Rationale: the tick labels are now the readable scale; the preset bar beside this panel names every preset; the caret answers "where are my presets". See the open question in §8.
- **Collisions:** none to manage. Adjacent FM channels are ≈ 6.9 px apart, so two neighbouring presets' carets overlap into one wider mark; that is acceptable and honest.
- **Shown band only**, as today. Presets outside the plotted range (VHF window) are not drawn.

**Tuned station (same caret family, larger, amber):**
- Caret **16 px wide × 12 px tall**, same orientation and apex position as preset carets, fill `--source-radio`. Drawn last, so it covers a preset caret at the same frequency (the preset bar already shows that preset as active).
- The **solid 2 px amber line** through the plot is **kept**, now running `plotTop → plotBottom` (it meets its own caret at the axis). The old downward caret at the plot top is removed.

## 4. Bottom strip (`.band-strip`)

Flex child of `.viz-panel` after `.visualizer-canvas-wrap`, BAND mode only. Height **89 px**: `border-top: 1px solid var(--surface-separator)`, `background: var(--surface-base)`, padding `10px 12px 10px 16px`. Three columns, `gap: 12px`, vertically centred:

| Column | Width | Content |
|---|---|---|
| **Band block** | 128 px fixed | **Name**: 32 px `--font-mono`, weight 600, letter-spacing 0.04em, `--text-high`, line-height 34 px — `FM`, `AIR`, `WB`, `VHF`, `AM`, `SW`. Not amber (amber means "tuned here"). **Range**: 12 px mono, uppercase, letter-spacing 0.06em, `--text-medium`, line-height 16 — `87.5–108 MHZ`, `108–137 MHZ`, `162.4–162.55 MHZ`, `530–1710 KHZ`, `1.6–30 MHZ` (the control panel pill sub-labels, with an en dash). **VHF** shows its window instead: name `VHF` followed inline by `WINDOW` (11 px mono `--text-medium`, baseline-aligned, 8 px gap), range `145.52–147.52 MHZ` (the plotted DisplayMin–Max, two decimals). |
| **Text block** | flex 1 (≈ 418 px) | **Line 1 (16 px):** status left, legend right (`margin-left: auto`). Status is today's `.band-status` type (12 px mono uppercase 0.10em, `--text-medium`; "Discovering…" in `--accent-primary`, full opacity). Legend unchanged from UI-22 (11 px, 8 × 8 swatches). **Lines 2–3 (18 px each):** help text, 13 px body font, `--text-medium`, two fixed lines (render as two block spans so wrapping is deterministic). |
| **Discover** | 112 × **58 px** | See below. |

**Discover button** (`.band-discover-btn`, the renamed `.band-scan-btn`): same outlined-accent language (1 px `--accent-primary` border, radius 6, accent text, `--accent-dim` on press, 0.4 opacity when disabled), label `DISCOVER`, 13 px mono, letter-spacing 0.06em, padding 0 16 px. **No icon** — the magnifier is the control panel's SCAN glyph, and reusing it would re-create the overlap. **Height 58 px, not `--touch-min` 48:** it is a primary action at arm's length, the house kiosk standard for those, and it lines up with the preset bar's 58 px arrows in the same bottom band across the gap. It sits fully outside the canvas, so no tap-ignore zone is needed.

**Transient tune/error message** ("Tuning 99.5 FM", "Could not tune to …", API errors) replaces **lines 2–3** for its existing lifetime: one line, 13 px, `--text-high` (`--signal-red` for errors), ellipsis on overflow. The help text returns when it expires. Status and legend on line 1 stay put.

### Copy

| Where | Copy |
|---|---|
| Help line 2 | `Touch a signal to snap to it. ▲ marks a preset.` |
| Help line 3 | `Fine-tune with ‹ › on the radio panel or the knob.` |
| Status, map present | `DISCOVERED JUST NOW` / `DISCOVERED 3 MIN AGO` / `DISCOVERED 7 H AGO` / `DISCOVERED 2 D AGO` (`"discovered " + FormatAge`) |
| Status, sweeping shown band | `DISCOVERING… 12 S` → `DISCOVERING… 40%` → `DISCOVERING…` (FormatScanning's three fallbacks) |
| Status, sweeping another band | `DISCOVERING AIR… 12 S` |
| Status, out of range | `OUT OF RANGE` |
| Empty state (plot), no map | title `NO STATIONS DISCOVERED YET`, sub `The band is swept automatically a few minutes after start-up, or tap Discover.` |
| Empty state (plot), out of range | unchanged: `AM is out of this radio's range` + the API reason |
| Error fallback (scan refused, no reason) | `Discover unavailable` (was `Scan unavailable`) |
| Canvas `aria-label` | `FM band map. Touch a signal to tune.` / `AM band map.` when not mappable |
| Discover `aria-label` | `Discover stations in the FM band`; before the first map read `Discover stations in this band` |

"Scan" must not appear anywhere in the BAND view after this change (code identifiers and the API path excepted).

## 5. States

| State | Plot / axis | Strip |
|---|---|---|
| **Before first map read** (`_bandMap == null`) | Axis line, FM ticks; no carets | Band block empty (no band is named until known, as AUD-91). Status empty, help shown, Discover enabled. |
| **Normal map** | Bars, gridlines, axis, preset + station carets, amber line | Name + range; `DISCOVERED …`; legend; help; Discover enabled |
| **No map yet, not sweeping** | Axis + carets + station drawn (tap still tunes to the nearest channel); centred empty state | Status line empty; legend hidden; help shown; Discover enabled |
| **Discovering this band, map exists** | Old map stays drawn | `DISCOVERING… 12 S` (accent); legend stays; Discover disabled |
| **Discovering this band, no map** | Axis + carets; no empty state (as today) | Same status; legend hidden; Discover disabled |
| **Discovering another band** | Shown band's map as is | `DISCOVERING AIR… 12 S`; Discover disabled |
| **Request in flight** | — | Discover disabled |
| **AM / SW out of range** | **No axis line, ticks, labels or carets** (there is no plot); centred out-of-range state, vertically centred in the full canvas | Name + nominal range; `OUT OF RANGE`; legend and help **hidden** (touch does nothing here); Discover **visible, disabled** (layout never shifts) |
| **VHF** | ≈ 2 MHz window; ticks at nice 0.25/0.5 MHz steps; only presets inside the window | `VHF WINDOW` / `145.52–147.52 MHZ` |
| **WB** | 7 wide bars, ticks `162.40 … 162.55` (last end-aligned, no unit) | `WB` / `162.4–162.55 MHZ` |
| **Tune message live** | — | Lines 2–3 show the message |

The centred empty states (`.band-empty`) centre on the **plot** (offset up by half the 32 px axis zone: `top: calc(50% − 16px)`), not the whole canvas, except out-of-range where no axis is drawn.

## 6. Accessibility

- **Live region:** one **persistent** element in the text block, `role="status" aria-live="polite" aria-atomic="true"`, empty when there is no message, holding tune confirmations. Errors render on demand as `role="alert"` in the same slot. Do not toggle `role` on one element; a polite region inserted together with its text is announced unreliably, so the status container must already exist.
- **Not live:** the status line (the countdown re-renders every second), the legend, the help text, the band block.
- Discover: native `<button disabled>` when disabled (as today), with the `aria-label`s in §4.
- Axis labels layer and canvas-drawn carets are `aria-hidden`; the canvas label carries the instruction.
- Contrast: axis labels and help text `--text-medium` ≈ 9:1; band name `--text-high` ≈ 18:1; preset carets `--text-high`.
- Colour is never the only cue: preset vs tuned carets differ in **size** (10×8 vs 16×12) as well as colour.

## 7. What the implementer must change

1. **`VisualizerPanel.razor`:** delete `.band-overlay`; delete the BAND branch of `.visualizer-axis`; add `.band-axis-labels` inside `.visualizer-canvas-wrap`; add `.band-strip` (band block, text block with status/legend/help/message slot, Discover) after the wrap. Copy per §4. Band range text: nominal range per band, VHF window from the map's DisplayMin/MaxHz. Planner: find the nominal range in the Web's existing band list (the control panel pills render it); **if it is not reachable from this component without an API change, escalate to Architect** rather than hard-coding a second copy.
2. **`design-system.css` §28:** remove `.band-overlay` and the `.visualizer-axis.is-band` rules; add `.band-strip`, `.band-strip-band`, `.band-strip-range`, `.band-strip-text`, `.band-strip-help`, `.band-axis-labels`; rename `.band-scan-btn` → `.band-discover-btn` at `min-height: 58px; width: 112px`; move `.band-status*`/`.band-legend*` into the strip unchanged; adjust `.band-empty` centring.
3. **`visualizer.js`:** `bandTopReserve` 64 → **8** (rename to `bandTopPad`); delete `bandLabelLane`, `bandCaretGap`; add `bandAxisZone: 32`; `plotBottom = height − bandAxisZone`; draw axis line, tick marks, preset carets, station caret per §2–3; delete dashed preset lines and preset label drawing; station line `plotTop → plotBottom`; skip the axis entirely when the model says not mappable. Update the header comment.
4. **`registerBandTap`:** remove the top-zone ignore (`yCanvas < bandTopReserve`) — nothing overlays the canvas any more. Taps anywhere on the canvas, including headroom and the axis zone, tune with snap as today.
5. **`BandAxis.Labels()`:** no unit suffix on the last label. **`BandMapText.FormatScanning`:** `Scanning…` → `Discovering…`, `Scanning AIR…` → `Discovering AIR…`.
6. **Tests:** update bUnit/unit assertions on "Scan", "Scanning…", "scanned … ago", "No scan yet", the axis unit, and the overlay markup; add assertions for the strip's band name/range per band, the disabled-Discover cases in §5, the persistent status region, and the absence of the word "Scan" in the BAND view's rendered text.

## 8a. Addendum (Builder, 2026-10-02) — scope added after this spec was written

> **Owner, 2026-10-02 (verbatim, relayed by the coordinator):** "One other change - we should rename the 'Band' visualization to something like 'Radio' - band isn't descriptive of what it does."

- **The tab reads `RADIO`.** Only user-facing text changes: the tab label, its `aria-label` ("Radio signal map mode"), and the canvas label ("FM signal map. Touch a signal to tune."). Where this spec says "BAND view" / "BAND tab", read "RADIO view" / "RADIO tab".
- **The saved preference value does not change.** `ui.visualizer/defaultMode` keeps storing `Band` (the enum member keeps its name), so the owner's saved `defaultMode=Band` still opens this view — renaming the stored value would orphan it (the `AUD-1` lesson). `/api/radio/bandmap…` is unchanged.
- **Possible confusion, flagged for the owner:** "RADIO" is also the radio control panel's own tab and part of the "FM/AM Radio" source pill. The view is only selectable while the radio is the active source (`UI-29`), so the three always appear together.
- **The band range in the strip** comes from `GET /api/RadioBands` — the same `Range` string the control panel's band pills show — so there is no second hard-coded copy and no API change. If that read fails, the strip falls back to the map's own plotted range.
- **The disabled tab's tooltip (`title`) is removed**, per the owner's first sentence ("No tooltip needed for the gray 'Band' selection"). The visually-hidden description stays for screen readers.
- **§8 is built as recommended (carets only)**; it is listed as an owner check on the PR.
- **Deviations from §1/§4 found by the pre-merge polish pass (measured at 1920×720), built that way:**
  - **The strip is 91 px, not 89.** The preset bar is 89 px but ends 2 px above the screen's bottom edge; 91 px puts both top edges at y = 629.
  - **Discover is 112 × 60 px and bottom-aligned** (strip padding-bottom 8 px), matching the preset bar's arrows, which are 58 px wide × **60 px tall** at y 652–712. §4's "58 px arrows" was their width.
  - **Help line 3 reads "Fine-tune with the tuner's ‹ › buttons or the knob."** — "the radio panel" became ambiguous once this view's own tab read Radio.
  - **The glyphs are `aria-hidden`**, with visually-hidden words ("A triangle", "step") read in their place.
  - **The empty states centre 12 px above the canvas centre**, the plot's true centre (8 px top pad, 32 px axis zone), not 16.
  - **WB's last label renders centred** (162.55 is not at the plot's edge); §5's "end-aligned" was wrong.

## 8. Open question for the owner

**Preset labels on the map: drop them (recommended) or keep a small frequency label above each caret?**
- **Drop (recommended):** the axis stays clean and readable; preset names are on the preset bar 10 cm away. Cost: you identify a caret by its position against the axis, not by text.
- **Keep:** one 11 px label lane (≈ 16 px) between the plot and the axis, with today's skip-on-collision rule. Costs ≈ 16 px of plot and brings back the crowding on FM's 105.1 / 105.5 pair.
