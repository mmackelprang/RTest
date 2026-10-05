# HANDOFF — Radio presets bar, one-row bands, and signal-strength colour on the BAND map

**Surface:** Home page → centre panel → **Radio** tab (`RadioControlPanel.razor`), plus the visualizer's **BAND** view (`VisualizerPanel.razor`, `visualizer.js` `drawBandMap`).
**Author:** Designer · **Date:** 2026-10-01 · **Consumer:** Planner
**Status:** Owner-reviewed 2026-10-01. Five of the six §9 questions are decided (see **Owner decisions** below); Q4 (AM/SW) stays open as `AUD-94`. ✅ **PRs 1–3 shipped 2026-10-01, each UAT'd by the owner at the panel and archived:** [`UI-20`](../queue/UI-20.md) (PR 1, [#757](https://github.com/mmackelprang/RTest/pull/757), squash `0af90f4`), [`UI-21`](../queue/UI-21.md) (PR 2, [#758](https://github.com/mmackelprang/RTest/pull/758), squash `7d2124e`) and [`UI-22`](../queue/UI-22.md) (PR 3, [#756](https://github.com/mmackelprang/RTest/pull/756), squash `f30ed06`; the 12/20 dB thresholds kept). **PR 4: declined for now by owner 2026-10-02; revisit with the hardware** ([`AUD-94`](../../docs/queue/AUD-94.md); owner: *"#4 - keep as it is. Once I have the new hardware, we'll execute the roadmap item."*).

### Owner decisions (2026-10-01)

| § 9 | Decision | Effect on the spec |
|---|---|---|
| Q1 | **Four wider cards** (≈129 × 60 px), the recommendation. | §4.5 stands as written. |
| Q2 | **Yes — the empty-slot card becomes a tappable `＋ SAVE`.** Long-press on the active band pill keeps working. | §4.6's "optional extension" is **in scope for PR 1**. ⚠ This **reverses** the radio-controller handoff's save-by-long-press-only choice (`design_handoff_radio_controller/IMPLEMENTATION.md:254-256`, "instead of the icon button") **by owner decision**, not by drift. The "follows by default" row in the table below is superseded. |
| Q3 | **Drop the `⋮` kebab from bar cards; long-press a card for Rename / Delete.** | §4.5 "No kebab" and §4.6 stand. The `Card` variant on `RadioPage.razor` is out of scope and keeps its kebab. |
| Q4 | ✅ **Decided 2026-10-02: keep AM/SW as they are** (owner: *"#4 - keep as it is. Once I have the new hardware, we'll execute the roadmap item."*). _(Was: ⛔ NOT decided — the open owner decision [`AUD-94`](../../docs/queue/AUD-94.md).)_ | PR 4 declined for now; revisit with the all-band hardware. PR 1 leaves AM and SW behaving exactly as today, in one row. |
| Q5 | **Cool ramp**: grey noise < 6 dB → blue 6–12 → cyan 12–20 → green ≥ 20 dB above the map median. | §5.1 stands. **12 and 20 dB are provisional** until checked against a real map on the box. |
| Q6 | "Scan area" = **the BAND view's map.** The coordinator and the designer both read it that way; the owner did not object. Recorded as the shared reading, not as an explicit owner answer. | §5 stands. |

**Coordinator spot-checks against `main` `dcf89a45`, 2026-10-01** (the spec's citations re-read, not inherited):

- `RadioControlPanel.razor:840` — `.rcp-presets { width: 210px }` (the rail §2.1 removes).
- `RadioControlPanel.razor:428` — tuner padding `32px 24px 12px` (§4.2 changes the top to 20).
- `Home.razor:8` — Now Playing panel 520 px; `Home.razor:20` — visualizer panel 710 px (§2.1's centre-panel width).
- `src/Radio.Infrastructure/Platform/Input/PresetSelectorService.cs:262-266` — the PRESETS knob's ordering *"matches the on-screen rail exactly — band, then the per-band slot ordinal"*, deliberately. §4.7's "do not reorder" rests on this; the bar must keep that order.

### Follows, extends, deviates

| Handoff | Relation | Detail |
|---|---|---|
| `HANDOFF-saved-station-display.md` (Proposal A) | **follows** | Name first, frequency second in dim mono with no glow, amber active state plus a cue that does not rely on colour, frequency shown as the main line when a preset has no name. The bar card is a third `PresetCard` variant with the same field order. |
| `HANDOFF-rotary-encoder-mapping.md` Rev 3 §4.4 (ENC-7) | **follows** | The on-screen bank keeps the word **PRESETS**. The saved-station handoff's rename note (lines 9-26) says *"Do not 'fix' this on a later consistency pass."* The bar's list order is the knob's list order. The bar's active test is the knob's `IsCurrent` test. |
| `design_handoff_radio_controller` §P1·1 (two-line band pills) | **follows** | Pills keep the label plus range sub-label and stay at least 56 px tall (`IMPLEMENTATION.md:226`). They now sit in one row instead of wrapping. |
| `design_handoff_radio_controller` §P1·2 (memory presets as a right-hand rail) | **deviates, at the owner's direction** | The vertical rail becomes a horizontal bar. Owner request of 2026-10-01: *"moving the preset column to a bar across the bottom of the radio"*. Slot numbers, the next empty slot as a placeholder, and save by long-pressing the active band pill all stay. |
| `design_handoff_radio_controller/IMPLEMENTATION.md:254-256` (save is a long-press, "instead of the icon button") | **follows by default.** One optional extension is offered in §9 Q2. | The spec does **not** add a save button on its own initiative. That handoff deliberately removed one. |
| Kebab `⋮` on each preset (PR #371 hot-fix, `PresetCard.razor:59-64`) | **deviates in the bar variant only. Needs owner OK (§9 Q3).** | The kebab is 18 px (`design-system.css:4934-4948`), well under `--touch-min` 48 px (`:209`). Long-press already opens the same menu (`RadioControlPanel.razor:1470-1486`). |
| BAND map (AUD-76 / AUD-91) | **extends** | Bars are coloured by strength. Today they are all `--accent-primary` (`visualizer.js:439`). |

**No new tokens.** Every colour below is an existing token in `design-system.css:148-177`.

---

## 1. What the owner asked, and how this reads it

> "…moving the preset column to a bar across the bottom of the radio with touchable arrow pushbuttons to scroll left and right. This would widen the radio control area, hopefully providing enough horizontal space for all band buttons on one row, allowing space for a preset band above the current two-row band selection area… make the preset visible and more easily accessible… the scan area could be color coded based on signal strength."

- **"Bottom" vs "above the band selection".** Both placements are evaluated in §3. The recommendation is the **bottom**. Either way the vertical space comes from the same place: the band row collapsing from two rows to one.
- **"Scan area"** is read as the **BAND view's sweep map**, the surface showing "SCANNED 7 H AGO" with a **Scan** button. The tuner's SCAN◀ / ▶SCAN has no area to colour. If the owner meant something else, §9 Q6 covers it.

---

## 2. Measurements (from the code and the owner's 1920×720 screenshots)

Pixel positions were read from `aud91-screens/fm-map.png` and `Screenshot 2026-07-14 164444.png`, both captured at the kiosk's native 1920×720. **Planner: re-measure on the box (AT-SPI extents, per CLAUDE.md) before you rely on the vertical slack figures.** They are tight.

### 2.1 Horizontal

| Item | Width | Source |
|---|---|---|
| Now Playing panel | 520 | `Home.razor:8` |
| Visualizer panel | 710 | `Home.razor:20` |
| Two panel gaps | 2 × 2 | `Home.razor:6` |
| **Centre panel** | **1920 − 520 − 710 − 4 = 686** (684 measured inside the panel border) | — |
| `.rcp-root` inner (1 px border each side) | **682** | `RadioControlPanel.razor:403-405` |
| Presets rail today | 210 | `RadioControlPanel.razor:840-848` |
| Tuner today | 682 − 210 = 472; minus 2 × 24 padding = **424 content** (measured 548→972) | `:428` |
| **Tuner after (no rail)** | 682 − 48 = **634 content** | — |

### 2.2 Band pills: do six fit on one row?

Pill width follows its content. The sub-label is 7.5 px mono with 0.12 em letter-spacing (`design-system.css:4460-4468`), about 5.4 px per character, plus 10 px padding on each side (`:4446`). Measured widths:

| AM | FM | SW | AIR | WB | VHF | 5 gaps × 2 | group padding + border |
|---|---|---|---|---|---|---|---|
| 81 | 83 | 72 | 76 | 101 (`162.4–162.55 MHZ`) | 73 | 10 | 8 |

**Total ≈ 504 px.**
- Today's tuner is 424 px, which is why the row wraps 4 + 2 (`.rcp-band-group` `flex-wrap`, `RadioControlPanel.razor:476-488`).
- At **634 px it fits on one row with 130 px to spare.** Spec: give the six pills **equal widths** (`flex: 1`). That makes each one (634 − 8 − 10) / 6 ≈ **102 px × 56 px**. 102 is above WB's natural 101, so nothing truncates and the 7.5 px sub-labels stay readable. The equal pills also line up as a calmer row of console push-buttons.

**Vertical saving.** Two rows: 3 + 56 + 4 + 56 + 3 + 2 = **124 px** (measured 202→326). One row: 3 + 56 + 3 + 2 = **64 px**. **Saving: 60 px.**

### 2.3 Vertical budget of the Radio tab

`.rcp-root` runs y≈171 to y≈717: **546 px**. The tab strip sits above it.

| Today (with RDS, two-row bands) | px |
|---|---|
| Tuner top padding (`:428`) | 32 |
| Band group → AGC row bottom (measured 202→688) | 486 |
| Tuner bottom padding | 12 |
| **Used** | **530**, leaving 16 px slack |

⚠ **Bug that exists today, which this spec fixes:**
- During a tuner scan, `.rcp-scanning` / `.rcp-scan-signal` (`RadioControlPanel.razor:156-171`) insert an extra row of about 26 + 10 px.
- That needs 36 px against 16 px of slack, so the AGC row is pushed about 20 px past the bottom and clipped (`.rcp-tuner` is `overflow: hidden`, `:433`).
- See §4.4 for the fix.

---

## 3. Placement: bottom vs top

| | **A. Bottom of the radio (recommended)** | B. Above the band row |
|---|---|---|
| Vertical cost | Same in both: about 89 px (§4.1) | Same |
| Reading order | Bands → RDS → frequency → RSSI → tune/scan → AGC → **presets** | Tab strip → **presets** → bands → RDS → frequency … |
| Stacked button rows at the top | One (bands) | **Three in about 150 px**: tab strip, presets, bands. All three change what is playing or showing, so mis-taps at the edges become likely. |
| Idiom | Push-button preset row under the dial, as on console and car radios. Fits the cabinet. | Unusual on radios |
| Visual weight | The frequency well stays in the optical centre | The frequency well shifts about 90 px lower |
| Visibility | Bottom edge of the panel, full width, always on screen | Top, always on screen |

**Recommendation: A, a full-width bar along the bottom edge of `.rcp-root`, under the AGC row.**
- The owner's "above the two-row band area" works out to the same space: the vertical room exists *because* the band row shrinks.
- The bottom is where it reads as a radio. It also keeps preset buttons and band buttons apart, since both change the station.

---

## 4. Layout spec

### 4.1 Wireframes

**Before** (1920×720; top bar 0-120, content 120-720):

```
0                     520 522                                         1206 1208                     1918
┌──────────────────────┐┌───────────────────────────────────────────────┐┌──────────────────────────┐
│ NOW PLAYING  (520)   ││ [RADIO][HISTORY]                       (≈50)  ││ WAVE SPECTRUM BAND …     │
│                      │├────────────────────────────────┬──────────────┤│ FM · scanned 7 h ago [SCAN]
│  art                 ││  ┌AM ┐┌FM ┐┌SW ┐┌AIR┐          │PRESETS·8 HOLD││                          │
│                      ││  └───┘└───┘└───┘└───┘  ← 2 rows│ 01 FM - 10… ⋮││   |  ||  | |||  |  ||    │
│                      ││       ┌WB ┐┌VHF┐       124 px  │ 02 FM - 10… ⋮││   cyan bars, one colour  │
│                      ││  [RDS  WKRR  …       ] 46      │ 03 FM - 91… ⋮││                          │
│                      ││  [  92.30 MHz  ] 94  (≤420 w)  │ 04 FM WFJ…  ⋮││                          │
│                      ││  RSSI ▮▮▮▮▮▮▮▮▮▮▯▯   45        │ 05 FM WS…   ⋮││                          │
│  transport           ││  [<][SCAN◀][▶SCAN][>] 52       │▌06 FM Rock… ⋮││                          │
│                      ││  [AGC AUTO | 28.0 dB] 58       │ 07 FM 87.5… ⋮││                          │
│                      ││   tuner 424 content            │ rail 210  ↕  ││                          │
└──────────────────────┘└────────────────────────────────┴──────────────┘└──────────────────────────┘
```

**After:**

```
0                     520 522                                         1206 1208                     1918
┌──────────────────────┐┌───────────────────────────────────────────────┐┌──────────────────────────┐
│ NOW PLAYING  (520)   ││ [RADIO][HISTORY]                       (≈50)  ││ WAVE SPECTRUM BAND …     │
│   (unchanged)        │├───────────────────────────────────────────────┤│ FM · scanned 7 h ago     │
│                      ││ ┌ AM ─┐┌ FM ─┐┌ SW ─┐┌ AIR ┐┌ WB ─┐┌ VHF ┐  64 ││ ▮weak ▮fair ▮strong [SCAN]
│                      ││ └─────┘└─────┘└─────┘└─────┘└─────┘└─────┘     ││                          │
│                      ││ [RDS  WKRR  Eagles • Hotel California   ] 46  ││   ·  |  ▌  | ▐▌ ·  ▌ |   │
│                      ││ [          92.30 MHz      STEP  STEREO  ] 94  ││  grey/blue/cyan/green    │
│                      ││ RSSI  (SCANNING ▲)                    −2 dBu  ││  bars by strength        │
│                      ││ ▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▮▯▯▯▯▯▯          45    ││                          │
│  transport           ││       [<] [SCAN ◀] [▶ SCAN] [>]          52    ││                          │
│                      ││ [AGC  AUTO        | Tuner is choosing 28 dB] 58││                          │
│                      │├───────────────────────────────────────────────┤│                          │
│                      ││ PRESETS · 8 saved                  1–4 of 8    ││                          │
│                      ││ [◀]┌────────┐┌────────┐┌────────┐┌────────┐[▶]││                          │
│                      ││ 58 │WKRR    ││WFJA    │▌Rock 92.3││+ EMPTY │ 58 ││                          │
│                      ││    │01·FM 105│02·FM 105│06·FM 92.│ hold FM │    ││                          │
│                      ││    └────────┘└────────┘└────────┘└────────┘    ││  bar 89 px               │
└──────────────────────┘└───────────────────────────────────────────────┘└──────────────────────────┘
          tuner rows: 634 px wide, every row edge-aligned with the band row
```

**Where the freed space goes.**
- **Horizontal:** the 210 px from the rail goes to the tuner. RDS card, frequency well, RSSI meter and AGC row widen from `max-width: 420` (`RadioControlPanel.razor:553`, `:671`) to the full **634 px** column, so every row lines up with the band row and the preset bar. The RDS marquee gains about 210 px of visible text, which is the main practical win. Frequency digits stay at 43 px (`:578`) because the owner asked for them smaller earlier. The scan/tune button cluster stays centred at its current size (`:775-831`).
- **Vertical:** the 60 px from the band row, plus the 16 px of existing slack, plus a little trimming below, **pays for the 89 px preset bar.** Nothing is left over for anything else.

### 4.2 Vertical budget after the change

| Element | px |
|---|---|
| Tuner top padding: 32 → **20**. The amber hairline (`:437-454`) still needs some clearance; 20 is enough. | 20 |
| Content (486 measured) − 60 (one band row) − 10 (tuner `gap` 10 → **8**, 5 gaps; `:429`) | 416 |
| Tuner bottom padding | 12 |
| **Tuner total** | **448** |
| **Preset bar:** border-top 1 + padding 6 + caption 12 + gap 4 + cards 60 + padding 6 | **89** |
| **Sum** | **537 of 546, leaving 9 px slack** |

**Fallback if the box measures tighter:** put the AGC cell on the same row as the tune/scan cluster. That frees about 66 px. The cluster is 322 px and the AGC grid can take the remaining ~300 px. Do this only if needed; it changes the radio-controller handoff's AGC strip.

### 4.3 Band row

- **One row, six equal pills of about 102 × 56 px** (§2.2). Same two-line content and amber active state (`RadioControlPanel.razor:523-532`, `design-system.css:4470-4474`).
- **Long-pressing the active pill still saves** (`RadioControlPanel.razor:1151-1187`). This does not change.
- **AM and SW (not receivable, `AUD-94`):** see §6.

### 4.4 Tuner-scan indicator (fixes the clipping bug in §2.3)

- Move the `SCANNING UP` / `SIGNAL −12 dBu` text **into the RSSI meter's header row**, between "RSSI" and the dBu readout, so it takes no height.
- Keep the existing amber blink / green pulse treatment (`.rcp-scanning` / `.rcp-scan-signal`, `:735-761`), shrunk to the header's 9 px mono.
- The scan buttons already collapse to a red **STOP** while scanning (`:179-184`), so the state is visible twice.

### 4.5 The preset bar

**Container**
- Full `.rcp-root` width (682), pinned to the bottom of the panel.
- Background `#0b0b0d` with a 1 px `--surface-separator` top border. These are the rail's current colours (`RadioControlPanel.razor:846-847`), so the bank keeps its look.
- Padding: 6 px top and bottom, 8 px left and right, giving **666 px** inner width.

**Caption row (12 px)**
- Left: `PRESETS · 8 saved`. Reuse `.rcp-presets-count` (`design-system.css:4753-4760`).
- Right, by default: `HOLD [FM] TO SAVE`. Reuse `.rcp-presets-hint` and its kbd chip (`:4762-4781`). This is today's copy (`RadioControlPanel.razor:255-258`).
- Right, when there is more than one page: show the position instead, `1–4 of 8`, in `.rcp-presets-count` styling. The hold hint moves into the empty-slot placeholder card (below), so the save hint is never lost.

**Cards row (60 px)**

```
[◀ 58] 8 [ viewport 534 ] 8 [▶ 58]      58 + 8 + 534 + 8 + 58 = 666
viewport: 4 cards × 129 + 3 gaps × 6 = 534
```

- **Four cards visible at about 129 × 60 px.** Five would be about 102 px wide, about 10 characters per line, which is too close to today's truncation. Three at about 174 px would show fewer stations than the problem warrants. The rail showed about 8 at once; the bar shows 4, but with roughly three times the name width. This is the trade-off the owner should see (§9 Q1).
- **Arrows are 58 × 60.** That meets the kiosk dialog button size and is above `--touch-min` (48). Styling: the tune-button bevel (`.rcp-tune-btn`, `RadioControlPanel.razor:781-803`): amber chevron, dark gradient, 2 px bottom bevel, 1 px press-down. The arrows are console push-buttons like their neighbours, not Radzen defaults.

**Card: new `PresetCard` variant `Bar`**

```
┌───────────────────────┐  129 × 60, padding 5/8, radius 6
│ WKRR Classic Rock     │  name: 14 px / 500, --text-high, line-height 1.2,
│ Station               │        2-line clamp (≈16 chars/line → ≈32 chars)
│ 06 · FM 92.30         │  meta: 10 px --font-mono tabular-nums, pinned to the card bottom
└───────────────────────┘
```

- Height: 5 + 2 × 16.8 + 2 + 12 + 5 ≈ 58 px, inside the 60 px card.
- **The meta line is pinned to the card bottom**, so frequencies line up across cards whether a name takes one line or two.
- **Name:** two lines, then ellipsis. The full name goes in `title` and in the aria label. This removes the "FM - 10…" truncation: at 129 px the card fits about 32 characters, against about 8-10 in the rail.
- **Meta line:** `NN · BAND FREQ`.
  - The ordinal stays because the knob's notices name it ("Saved to 05", `PresetSelectorService.cs:527`), and because ordinals are per band, so two cards can both read 01 (`PresetSelectorService.cs:338-339`).
  - The frequency has no unit (Proposal A §5).
  - The ordinal is `--text-medium`, **not** `--text-low`. At 10 px, `--text-low` (#4B5563) on `--surface-elevated` is about 2.2:1 and fails AA. This is a deliberate small deviation from Proposal A's slot colour, in the bar variant only.
- **No-name fallback:** unchanged. The frequency becomes the main line in 14 px mono (`PresetCard.razor:48-58`).
- **No kebab** (§9 Q3). Long-press for 600 ms opens the existing Rename / Delete menu (`RadioControlPanel.razor:316-338`), which is unchanged.
- **Card colours:** `--surface-elevated` fill, 1 px `--surface-separator` border, radius 6. These are the rail values (`design-system.css:4798-4800`).
- **Band groups:** cards run in the knob's order (§4.7). Where the band changes between neighbouring cards, the gap widens from 6 to 14 px with a 1 px `--surface-separator` hairline in the middle. That shows the grouping at no vertical cost.
- **Empty-slot placeholder.** Today's rule stays: one dashed card for the next free slot in the *current* band only (`RadioControlPanel.razor:289-305`). In the bar it sits **at the end of the current band's group**, which is exactly where a new save will appear.
  - Copy: `EMPTY` on the first line, `hold FM to save` on the second.
  - Dashed border, 0.55 opacity, inert (`design-system.css:4903-4914`).

**Scrolling**
- **Arrow tap moves one page**: the viewport width, snapped to a card's left edge.
  - One card per tap is too slow at 20+ presets. 50 presets (the bank maximum, `RadioPresetService.cs:18`) take 12 taps by page.
  - **Hold an arrow to auto-repeat** one page every 400 ms after a 600 ms hold. The 600 ms matches `LongPressThresholdMs`.
- **Swipe / drag: yes.**
  - Native horizontal touch scrolling with `scroll-snap-type: x mandatory` and snap points at card starts.
  - `touch-action: pan-x` on the viewport.
  - Movement over 10 px cancels the tap and the long-press. The existing `pointercancel` / `pointerleave` handlers already cancel long-press (`PresetCard.razor:32-33`).
  - **No visible scrollbar.** Instead, a **2 px position track** in the bar's bottom padding: `--surface-separator` track, `--text-low` thumb. It costs no height.
- **Disabled at the ends:**
  - At the first page, ◀ goes to 0.4 opacity with `aria-disabled="true"`. ▶ does the same at the last page. 0.4 is the project's disabled convention (`.bt-scan-button:disabled`, `design-system.css:3302-3305`; `.encoder-selector-row.is-unavailable`, `:7031-7033`).
  - The arrows stay in place so the layout never shifts.
  - With 4 or fewer presets, both arrows are disabled.

**Active preset**
- **Visual:** unchanged from the rail. `.is-active` gives an amber border, 8 % amber fill, a 3 px inset amber left bar (the cue that does not rely on colour) and an amber ordinal (`design-system.css:4811-4835`). The name stays `--text-high`.
- **Same test as today:** band equal, |Δf| < 1 Hz (`RadioControlPanel.razor:1034-1042`). This is also the knob's `IsCurrent` (`PresetSelectorService.cs:343-345`).
- **Kept in view:**
  - When the active preset changes (bar tap, knob recall, band-map tap, tune arrows landing on a preset frequency), scroll the least distance that shows the active card fully. Smooth scroll over 200 ms; instant under `prefers-reduced-motion`.
  - **Exception:** within 10 s of a manual swipe or arrow tap, do not auto-scroll. Browsing must not be pulled away by a tune.
- **When the active card is off-screen**, the arrow on that side shows a **6 px amber dot** in its corner, meaning "your station is this way". It has `aria-describedby` text "Playing preset is to the right".

**Band change**
- On a band switch, scroll so the **active preset** is in view, or else the **first card of the new band's group**, or else (no presets in that band) the band's empty-slot placeholder.
- This gives the "current band first" effect **without reordering**.

### 4.6 Save, rename, delete

| Action | Today | In the bar |
|---|---|---|
| Save | Long-press the active band pill → dialog seeded with the stable RDS name, else "FM 92.30 MHz" (`RadioControlPanel.razor:1282-1307`, dialog `:360-374`) | **Unchanged.** Caption hint and placeholder copy point to it. PRESETS-knob hold also saves (`PresetSelectorService.cs:213-219`). |
| Recall | Tap row → `LoadPresetAsync` (`:1442-1451`) | Tap card. Same handler. |
| Rename / Delete | Kebab or long-press → menu (`:316-338`); Delete undo toast (`:1366-1390`) | Long-press only (§9 Q3). Same menu, same toast. |

**Optional extension, needs owner direction (§9 Q2): a touch Save without the long-press.**
- The empty-slot placeholder becomes **tappable**: `＋ SAVE` / `FM 92.30`. It opens the same dialog.
- When the current station is already saved, the placeholder does not render, because the active card is the confirmation.
- No width cost and no new chrome. It brings back a save button that `IMPLEMENTATION.md:254-256` removed on purpose, which is why it is not in the default spec.

### 4.7 Order and band filtering

- **Show all bands. Do not filter.** Filtering was removed after the owner could not find a WB preset while on FM (`RadioControlPanel.razor:236-249`, `:1012-1018`).
- **Do not put the current band first.** The order stays band (alphabetical) then slot (`:1019-1023`). The ENC-7 knob composes its list in the same order on purpose, so that *"the knob's list and the list the user can see are the same list in the same order"* (`PresetSelectorService.cs:262-266`, `:364-370`). The knob's "turn 3 detents" has to land on the third card the user can see. Band-aware **scrolling** (§4.5) gives the convenience without breaking that.

### 4.8 Agreement with the PRESETS knob (ENC-7)

- **Selected / playing:** the bar and the knob already compute it the same way (§4.5). No work needed.
- **Knob preview highlight (the turn before the press).** While the PRESETS overlay is open, the bar **mirrors the highlight**:
  - It scrolls the highlighted card into view.
  - The card gets the overlay's highlight treatment: `--surface-hover` fill (`design-system.css:6972-6974`) and a 2 px amber bar along the card's **bottom** edge. That is the horizontal version of the overlay's 2 px left bar, `.encoder-selector-bar` (`:6980-6988`), with `--row-accent` set to `--source-radio`.
  - When the overlay closes, the highlight clears and the bar returns to the active card.
- **Data:** the Web already receives the full row list and `HighlightIndex` in the HUD payload (`PresetSelectorService.cs:626-652`; row ids are `preset:{id}`, `:336`). Planner should confirm `EncoderHudService` exposes it to a second subscriber. **No API change is expected.** If one is needed, escalate to Architect.
- **Touch and knob together:** tapping a card while the overlay is open recalls that card. The overlay closes on its own idle timer. The touch action wins and the knob preview is just a preview, so nothing conflicts.

---

## 5. Signal-strength colour on the BAND map

### 5.1 The scale

- Levels are relative dBFS. Display height is normalised: 10th percentile → 0, maximum → 1, over a span of at least 10 dB (`FmBandMath.cs:43-49`, `:84-95`).
- **Colour therefore cannot be keyed to normalised height.** The same height means 10 dB on a dead map and 40 dB on a busy one.
- **Key it to dB above the map's median**, which is the noise estimate `PeakProminenceDb` already uses (`FmBandMath.cs:32-37`). "A station" then means the same thing on the colour scale as in tap-to-tune.

| Tier | dB above median | Token | Hex | Contrast on `--surface-inset` #0A0A0C |
|---|---|---|---|---|
| Noise | < 6 (= `PeakProminenceDb`) | `--text-low` | #4B5563 | ≈ 2.6:1. Recedes on purpose: it carries no station. |
| Weak | 6 – 12 | `--signal-blue` | #60A5FA | ≈ 7.8:1 |
| Fair | 12 – 20 | `--accent-primary` | #5CD4E8 | ≈ 11:1. Today's colour, so the view keeps its identity. |
| Strong | ≥ 20 | `--signal-green` | #4ADE80 | ≈ 12:1 |

- The 12 and 20 dB thresholds are **provisional**. Planner: define them as named constants next to `PeakProminenceDb`, and have the Builder log the tier counts from one real FM map on the box before fixing them.
- Tier is decided per channel by level alone, not by the peak rule. A strong station's adjacent-channel shadow can therefore be coloured. That is honest about energy, and the tap still snaps to the peak.
- **Data path:** tiers are computed in C# next to `NormalizeLevels` (unit-testable) and passed as one extra field per level into `drawBandMap` (`VisualizerPanel.razor:754-779`). This is Web-only.

### 5.2 Why the RSSI meter's green/amber/red is NOT reused (§9 Q5)

- The meter's scale (`RadioControlPanel.razor:143-148`, `.seg-*` `:705-731`) is a **level** meter: green is normal, amber is hot, red is overload. Red sits next to the `CLIP` pill (`:129-132`).
- On the band map, the strongest stations are the **best** ones. Painting them red says "bad".
- **Amber is already taken on this canvas.** The station marker is `--source-radio` #F0A830 (`visualizer.js:393`, `:477-494`), the same hex as `--signal-amber`. Amber bars would hide the "you are here" line among stations.
- The recommended ramp is the cool half of the classic SDR waterfall palette (blue → cyan → green), stopped before yellow and red. Radio users know it, and it leaves amber to mean only "tuned here".

### 5.3 Living with the other marks

- **Draw order is unchanged:** fill trace → bars → dashed preset ticks → amber station line and caret (`visualizer.js:421-494`).
- **Trace fill:** change the cyan gradient (`:425-427`) to a neutral `--text-medium` gradient running from 0.18 alpha at the top to 0.03 at the bottom. Otherwise a cyan wash tints every bar and the tiers stop reading.
- **Preset ticks:** unchanged. A dashed `--text-low` line through the bar, label in the lanes (`:447-475`). **No extra mark on preset bars.** The tick already passes through the bar, and recolouring a preset bar would make the colour lie about strength. The more useful question, "which strong stations have I *not* saved", is answered by green bars with no tick.

### 5.4 Legend

- **Yes**, in markup rather than on the canvas, so it is readable to AT-SPI and tests.
- It goes on the status line after the age (`VisualizerPanel.razor:120-135`): `FM · scanned 7 h ago · ▮ weak ▮ fair ▮ strong`.
  - 11 px `--font-mono` `--text-medium`, with 8 × 8 px swatches.
  - The noise tier is not listed; grey reads as "nothing".
  - There is about 560 px free left of the 96 px Scan button.
- Hide the legend when the map is empty or out of range.

### 5.5 Colour-blind safety

- **Bar height already encodes strength.** Colour is a second, redundant channel, so no information depends on hue.
- Blue, cyan and green also step in lightness (blue about 7.8:1 against the background, cyan and green about 11-12:1).
- Under deuteranopia and protanopia, cyan and green separate as bluish versus yellowish.
- Tritanopia flattens cyan and green together. Height still carries the meaning.

---

## 6. AM and SW on this tuner (`AUD-94` undecided)

> ⛔ **2026-10-02: the owner declined this for now — keep AM/SW as they are; revisit with the all-band hardware** (`AUD-94`). The recommendation below is kept for that revisit.

The R820T gets nothing below about 24 MHz. Tuning AM or SW *succeeds* and plays silence (`docs/queue/AUD-94.md:14-23`).

**Recommendation: dim, keep visible, block selection and give the reason.** This is AUD-94 option 1, "shown disabled with the reason".
- **Pill:** 0.4 opacity, the project's disabled convention. The range sub-label is replaced with `UNAVAILABLE`, which is 11 characters and fits the 102 px pill.
- **Tap:** does not switch band. It shows a toast with the BAND view's existing reason text, e.g. *"The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit."* (`VisualizerPanel.razor:152-156`).
- **Hide is rejected.** Hardware options 2-4 would bring the bands back, and hiding would orphan any AM/SW presets.
- **Keep-as-is is rejected** because it tunes into silence without saying why.

⚠ **This is the owner's AUD-94 decision. Do not let it ride inside PR 1.** Until the owner decides, PR 1 keeps AM/SW exactly as they behave today, just in one row.

**Data:** the band list (`RadioBandModel`, `RadioBandService.cs:35-48`) has no "receivable" flag. Only the band-map endpoint knows (`Mappable`, `UnavailableReason`). Where the pill reads that from is a **Planner / Architect question**, not a design one.

---

## 7. States

| State | Preset bar | Band map |
|---|---|---|
| **Loading** (`_radioState == null`) | The whole tab shows the radio skeleton (`RadioControlPanel.razor:13-18`). Planner: extend the skeleton's shape with a one-row band strip and a bar of 4 card ghosts, so the layout does not jump. | Unchanged ("Waiting…" / last map). |
| **Presets loading / read failed** | Today a failed read is silent (`:1025`) and shows "NO PRESETS", which is a false empty. Spec: caption `PRESETS`. Viewport shows one inert card: `Couldn't load presets` / `tap to retry`. This mirrors the knob's "Could not read your presets" (`PresetSelectorService.cs:316-317`) and is a 60 px tappable retry. | — |
| **No presets** | Caption `PRESETS · 0 saved`. Both arrows disabled. Viewport: one wide dashed card: `NO STATIONS SAVED` / `hold FM, or hold the PRESETS knob, to save what's playing`. The first line is the knob's empty copy (`PresetSelectorService.cs:640-643`). | — |
| **1–4 presets** | All visible, arrows disabled, no position text. | — |
| **20+ presets** (max 50) | Page arrows, hold to repeat, swipe, position text `9–12 of 23`, position track, off-screen active dot. | — |
| **Active preset on another band** | Cannot happen: active requires the same band. | — |
| **Preset on a band other than the current one** | Rendered **identically**, at full contrast. A dimmed card would read as unavailable, and recall switches band (`PresetSelectorService.cs:346-349`). The band shows in the meta line. | Not drawn. Ticks are shown-band only (`VisualizerPanel.razor:767-772`). |
| **Preset on AM/SW** (if §6 ships) | Card dimmed to 0.4 with meta `FM 92.30 · unavailable`. Tap shows the same toast as the pill. | — |
| **Tuner scanning** | Bar unchanged. Cards stay tappable; recall stops the scan, as the knob does (`PresetSelectorService.cs:445-450`). | — |
| **Band sweep running** | — | Status shows `Scanning… 25 s` (existing). Tiers are computed from whatever map is drawn. Legend stays. |
| **Map empty / never scanned** | — | Existing `No scan yet` empty state; legend hidden. |
| **Out-of-range band (AM/SW on BAND)** | — | Existing `AM is out of this radio's range` + reason; Scan disabled (`VisualizerPanel.razor:144`, `:149-158`); legend hidden. |
| **Knob overlay open** | Mirrored highlight (§4.8). | — |

---

## 8. Accessibility

**Touch targets**

| Control | Size |
|---|---|
| Arrows | 58 × 60 |
| Cards | 129 × 60 |
| Band pills | ≈ 102 × 56 |

All are at or above `--touch-min` 48. The kebab (18 px) leaves the bar.

**Contrast** (against `--surface-elevated` #1C1C1F)

| Element | Contrast |
|---|---|
| Name `--text-high` | ≈ 15:1 |
| Meta `--text-medium` | ≈ 9:1 (ordinal raised from `--text-low`, §4.5) |
| Caption `--text-low` 9 px | Existing rail treatment. Planner may raise it to `--text-medium` for AA. |

Disabled arrows at 0.4 opacity are exempt (WCAG 1.4.3 inactive components).

**Semantics**
- **Bar:** `role="region"` `aria-label="Presets"`.
- **Viewport:** `role="list"`. Each card is a `role="listitem"` button.
- **Card `aria-label`:** "Preset 6, Rock 92.3, FM 92.3 megahertz". Add "now playing" plus `aria-current="true"` when active. The hint "hold for rename or delete" goes in `aria-describedby`.
- **Arrows:**
  - `aria-label="Previous presets"` / `"Next presets"`.
  - `aria-controls` points to the list.
  - `aria-disabled="true"` at the ends; the buttons stay focusable and visible so nothing reflows.
- **Position text** (`1–4 of 8`) is **not** a live region, so swiping does not chatter.
- **Knob preview mirror** adds no announcement; the HUD already has a `role="status"` mirror (encoder handoff `:1574`).
- **Band-map legend:** plain text. The canvas keeps its existing `BandCanvasLabel` (`VisualizerPanel.razor:329`).

**Motion:** smooth scroll and auto-repeat respect `prefers-reduced-motion` (instant scroll; auto-repeat stays because it is input-driven).

**Keyboard** (not used on the kiosk, but cheap): arrow buttons and cards are in tab order. Left and Right move focus between cards.

---

## 9. Open questions for the owner (genuine decisions only)

> **Answered 2026-10-01 except Q4** — see **Owner decisions** at the top. Q1 four cards · Q2 yes · Q3 yes · Q4 ⛔ open (`AUD-94`) · Q5 cool ramp · Q6 BAND map (shared reading, not objected to). The questions are kept as asked.

1. ✅ *(Answered: four.)* **4 wider cards (≈129 px, about 32-character names) or 5 narrower (≈102 px, about 20 characters)?** Recommended: **4**. The rail showed about 8 at once but truncated every name. The bar shows fewer at once and all of each name.
2. ✅ *(Answered: yes.)* **Should the empty-slot placeholder become a tappable `＋ SAVE`** (§4.6)? Recommended: **yes**, because "more easily accessible" applies to saving too, and long-press is invisible on touch. It reverses the radio-controller handoff's choice to remove a save button, so it needs your say-so.
3. ✅ *(Answered: yes.)* **Drop the ⋮ kebab from bar cards and use long-press only for Rename / Delete?** Recommended: **yes**. At 18 px it is too small to hit on the panel. The cost is that the menu becomes undiscoverable to someone who doesn't know to hold.
4. ⛔ *(Open — `AUD-94`.)* **AM and SW: dim, block and explain** (§6, AUD-94 option 1)? Recommended: **yes**, decoupled from whether you later buy HF hardware. Until you say so, PR 1 leaves them as they are.
5. ✅ *(Answered: cool ramp.)* **Band-map colours: cool ramp (grey → blue → cyan → green, recommended) or reuse the RSSI meter's green / amber / red?** The meter colours would make the best stations red and lose the amber "tuned here" line among bars.
6. ✅ *(Shared reading: the BAND map; the owner did not object.)* **"Scan area" = the BAND view's map?** Confirm. If you meant the tuner's SCAN controls, they have no strength display to colour beyond the RSSI meter that already exists.

---

## 10. Suggested PR split

| PR | Scope | Rough effort | Depends on |
|---|---|---|---|
| **1. Preset bar + one-row layout** | Remove the rail. `.rcp-root` becomes a column. One-row equal-width bands. Full-width tuner rows. Scan indicator moves into the meter header (fixes the clipping). `PresetCard` `Bar` variant. Arrows, page step, hold-repeat, swipe and snap, position track, active-in-view and off-screen dot, band-change scroll, empty / error / placeholder states, skeleton update. bUnit tests for states and arrow disablement. AT-SPI extents check on the box for the §4.2 budget. | **M–L, about 2 days** | Q1, Q3 (Q2 can fold in if answered "yes") |
| **2. Knob ↔ bar highlight mirror** | Subscribe the bar to the PRESETS HUD payload; preview highlight + scroll (§4.8). | **S, about 0.5 day** | PR 1. Confirm no API change. |
| **3. Band-map strength colour** | Tier computation + constants + unit tests in `FmBandMath`; tier per level into `drawBandMap`; neutral trace fill; markup legend. | **S–M, about 0.5–1 day** | Q5. Independent of PR 1; can go first. |
| **4. AM/SW unavailable** (⛔ declined for now by owner 2026-10-02; revisit with the hardware) | Dim / block / toast on pills and on AM/SW preset cards. | **S, about 0.5 day, plus the data-source decision** | Q4 / AUD-94; Architect on where "receivable" comes from. |

**Out of scope:** the standalone `RadioPage.razor` presets panel (`Card` variant). It does not change and shares the field order through `PresetCard`.
