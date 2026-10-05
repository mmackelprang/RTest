# HANDOFF — Sleep-mode weather pane: visual redesign (v2)

**Component:** `src/Radio.Web/Components/Shared/SleepForecastPane.razor` (rewrite of markup + scoped styles) + scoped rules in `src/Radio.Web/wwwroot/css/design-system.css` §P·6.
**Surface:** Kiosk sleep screen (route `/sleep`) — the same drift cluster as v1.
**Status:** `[PENDING REVIEW]` — ready for Planner / Builder.
**Supersedes:** `HANDOFF-sleep-mode-weather-forecast.md` §2 (visual treatment only). Sections §3 (alternation), §4 (icon mapping), §5 (config UI), §6 (failure-mode UI), §7 (anti-burn-in), §8 (a11y), §9 (PR coordination), §10–§12 (open questions / out-of-scope) remain authoritative and are NOT replaced.

**Relationship to existing handoffs:**
- **Follows** `docs/design-handoffs/design_handoff_radio_console/` — `--font-led` (Orbitron), `--signal-amber` (#F0A830), `--text-low/medium/high`, the "stereo's off-state" sleep intent (design-system.css §P comment block, lines 2798–2808).
- **Follows** `HANDOFF-configurable-time-format.md` — `Clocks.FormatWallClock` is still the source of all timestamp formatting in the pane (location/day/time sub-line).
- **Extends** `HANDOFF-sleep-mode-weather-forecast.md` — keeps every non-visual decision (alternation cadence, icon mapping table, failure-mode UI, accessibility contract, anti-burn-in math, config keys, all 12 out-of-scope items). Only the visual layout and typography are rewritten.
- **Deviates** from `HANDOFF-sleep-mode-weather-forecast.md` §2 — the v1 "wall of three equal cards" layout is replaced with a two-region composition (primary current + sub-line + 3 forecast cards). The change is driven by user feedback after live review of PR #415: *"For the weather info in sleep mode, I expected to see something more stylish than a wall of text. Something like this, but styled in a way that matches the current aesthetic."*

---

## 1. Problem + user reference

PR #415 shipped the v1 forecast pane: three equal-weight cards in a single row, each with a day name strip, a 40px icon, an uppercase condition label, a 36px LED high temperature, and a 24px low. Single emissive dim-amber, opacity-only stale affordance, all of it living inside the drift wrapper. The data is correct, the aesthetic is correct, and the user has confirmed the data hierarchy is wrong: three coequal cards read as a "wall of text" rather than a single glanceable weather summary. There is no visual anchor for "what is it doing right now."

### User reference layout (from the brief — for LAYOUT, not for color)

```
☁️  60°  F C       Partly sunny
                   77°  66°
Pittsboro, NC
Sunday 7:18 AM

┌─────────┬─────────┬─────────┐
│  ☁️     │  ☁️     │  ☁️     │
│  Sun    │  Mon    │  Tue    │
│ 77° 66° │ 76° 68° │ 80° 66° │
└─────────┴─────────┴─────────┘
```

Decomposition the user expects:
- **Primary block, top-left:** big condition icon + big current temperature + small unit indicator
- **Primary block, top-right:** today's condition text + today's H/L stacked beneath
- **Sub-line beneath primary:** location + day + time on one line
- **Forecast row, bottom:** three small cards, one column each (icon + day + H/L)

Important: the user said *"styled in a way that matches the current aesthetic."* The reference image is presumed to use bright sky-blue gradients + white text — those are NOT to be copied. The radio's "off-state" amber-on-near-black palette is preserved exactly as in v1.

### What the redesign solves

1. **Visual hierarchy.** A glance answer to "what is it doing right now" (the primary block) before the 3-day outlook (the row below).
2. **Density without crowding.** The current temperature, location, and time all become visible in the primary region — info the v1 layout had to push into the footer (timestamp) or omit entirely (current temperature).
3. **Same emissive budget.** No additional emissive elements, no new colors — the new "big number" (current temp) replaces a previously-absent element rather than adding to the count of bright pixels on screen.

---

## 2. Two-region layout decision

The pane is now a vertical stack of two regions, both centered horizontally within the drift wrapper.

### Region 1 — Primary block (current weather)

Three sub-columns, baseline-aligned:

- **Left column:** large condition icon (96px Material Symbol, dim amber)
- **Middle column:** large current temperature (96px LED numerals, dim amber, identical sizing/treatment to `.sleep-screen-clock`) + small unit indicator (F·C, lowercase, with the active unit emissive and the inactive at `--text-low`)
- **Right column:** condition text (24px `--text-medium`, sentence-case — e.g. *"Partly sunny"*) on top, today's H/L (28px LED, dim amber, separated by `/`) directly beneath

### Sub-line (between Region 1 and Region 2)

One row, single line, mono 14px, `--text-medium`:

```
Pittsboro · Sun 7:18 AM
```

- Location strips the state from `LocationName` when present (`"Pittsboro, NC"` → `"Pittsboro"`) — keeps the sub-line short. State-only or missing-comma cases render the full string verbatim.
- Day is a full weekday name (`"Sunday"`, not `"Sun"`) — there's only one date on the sub-line so the abbreviation has no reason to exist here. (The forecast cards still use 3-letter abbreviations in their own slot — that's where horizontal width matters.)
- Time uses `Clocks.FormatWallClock` with `allowSeconds: false` — same helper the v1 footer already uses; honors the user's 12h/24h preference automatically.

### Region 2 — Forecast row (3 days)

Three identically-sized cards in a horizontal row, evenly spaced, each with:

- 48px Material Symbol icon (dim amber)
- 14px mono day label (uppercase, `--text-medium`)
- 28px LED high / 20px LED low, one above the other, `/` separator NOT used here (vertical stack is its own separator)

Cards are visually separated by whitespace only — no border, no divider rule. The 32px gap between cards is enough to read them as discrete units without adding chrome.

### Today vs. day-1 vs. day-2 in Region 2

The forecast row always shows **the next 3 calendar days starting with today**. Today's card in Region 2 is a deliberate redundancy with the primary block — its H/L matches the primary block's H/L, its icon matches the primary block's icon. The user gets a visual "what's today" anchor on both sides of the layout. This matches the user's reference image, which shows Sun/Mon/Tue with Sunday's icon and H/L mirroring the primary block.

(Reasoning: removing today's card from the row leaves an uneven 2-card forecast and forces the user to mentally link the primary block to the row. Keeping it produces the satisfying "today is here, and here are the next two" pattern in the reference. Cost is one card-width of horizontal space; benefit is the layout reads correctly at a glance.)

### Region 1 + Region 2 layout — ASCII

See §3 for the full mockup. The two regions are stacked with a 28px gap; the sub-line sits 12px below Region 1 and 28px above Region 2.

---

## 3. ASCII mockup — full pane at 1920×720

The drift wrapper centers this composition. Origin (0,0) is the geometric center of the viewport; the pane's total bounding box at default drift offset is ≈ 880px wide × 360px tall. Card and icon sizes below are the actual rendered pixel sizes the CSS will produce.

```
                         ────────── 880 px ──────────
                        ┌───────────────────────────┐
                        │                           │
                        │   ☁           °  f·C      │   ← Region 1 (180 px tall)
                        │  ╔══╗   60  ┌──┐──────────│
                        │  ║  ║   °°  │  │ Partly   │
                        │  ╚══╝       └──┘ sunny    │   ← icon 96 px | temp 96 px LED | text 24 px
                        │                  77/66    │   ← today H/L  28 px LED
                        │                           │
                        │     Pittsboro · Sun 7:18 AM   ← sub-line 14 px mono, --text-medium
                        │                           │
                        │ ┌────────┬────────┬────────┐ ← Region 2 (140 px tall)
                        │ │   ☁    │   ☁    │   ☀    │
                        │ │  SUN   │  MON   │  TUE   │   ← day labels 14 px mono uppercase
                        │ │  77°   │  76°   │  80°   │   ← high 28 px LED dim amber
                        │ │  66°   │  68°   │  66°   │   ← low  20 px LED --text-low
                        │ └────────┴────────┴────────┘
                        │                           │
                        └───────────────────────────┘
                            tap anywhere to wake          ← .sleep-screen-hint (existing, unchanged)
```

Region 1 inner alignment:

```
   [ICON 96 px]    [TEMP 96 px LED]   °     [CONDITION 24 px]
                                      f·C   [TODAY H/L 28 px LED]
   ◀─ 120 px ─▶   ◀─ 200 px ─▶  ◀40▶  ◀─── 200 px ────▶

   • icon, temp, and the right column are all baseline-aligned on the
     mid-line of the 96 px numerals (visual center of the row).
   • the unit indicator is a 18 px mono column tucked between the temp
     numerals and the right column; active unit is dim amber, inactive
     is --text-low. (Read-only display — the actual toggle lives in
     System Config → Display → Sleep-screen weather.)
   • the right column wraps to two lines (condition above H/L) and
     left-aligns within its own 200 px slot.
```

### State A — Forecast (3 days, F units, fresh)

The mockup above is State A. Default and most common.

### State B — Forecast with stale-data indicator (`IsStale=true`)

Same composition as State A, with two modifications:

1. The outer `.sleep-forecast-pane.is-stale` selector applies `opacity: 0.7` to the entire pane (existing v1 behavior, kept).
2. A `sync_problem` Material Symbol (16px, `--text-low`) appears immediately to the LEFT of the sub-line, with 8px of gap. The sub-line text then prepends a relative qualifier per the v1 rules — `"yesterday at 3:00 PM"` or `"N days ago"` — replacing the day name only when the data is from a different calendar day than now.

```
                        │   ☁           °  f·C      │
                        │  ╔══╗   60  ┌──┐──────────│
                        │  ║  ║   °°  │  │ Partly   │
                        │  ╚══╝       └──┘ sunny    │
                        │                  77/66    │
                        │                           │
                        │  ⟳!  Pittsboro · yesterday at 3:00 PM   ← stale sub-line
                        │                           │
                        │ ┌────────┬────────┬────────┐
                        │ │  ...   │  ...   │  ...   │
                        │ └────────┴────────┴────────┘
```

The entire pane being at 70% opacity means the stale icon doesn't need to be its own color — it dims along with everything else, which is exactly the affordance the v1 rationale defended. No new tokens.

### State C — Partial: 2 days returned

Region 1 unchanged. Region 2 shows 2 cards centered (32px gap preserved). No placeholder card.

```
                        │ ┌────────┬────────┐       │
                        │ │  ...   │  ...   │       │
                        │ └────────┴────────┘       │
```

The pane width contracts from 880 → 760 px in this case; the primary block stays at its natural width and the centered cards land beneath it. Whitespace handles the layout — no `flex-grow` tricks needed.

### State D — Partial: 1 day returned (only today)

Region 2 is omitted entirely. The pane collapses to Region 1 + sub-line only.

```
                        │   ☁           °  f·C      │
                        │  ╔══╗   60  ┌──┐──────────│
                        │  ║  ║   °°  │  │ Partly   │
                        │  ╚══╝       └──┘ sunny    │
                        │                  77/66    │
                        │                           │
                        │     Pittsboro · Sun 7:18 AM
                        │                           │
                        │  (no forecast row)        │
```

Reasoning: a single forecast card centered below the primary block would duplicate today's data twice (once in the primary, once in the lone card) with no comparative value. Dropping the row keeps the layout truthful.

### State E — Zero days

Per v1 §6.A: the pane never renders. Sleep screen falls through to the clock cluster. (Sleep.razor already enforces this via `_forecast.Days.Count > 0` guard.)

### State F — TemperatureUnit = "both"

The primary block becomes `60°F · 16°C` for the current temp — but at 96 px LED that string is ~480px wide, blowing the column budget. **Decision:** in `both` mode, the primary block shows only the unit currently designated as "primary" by the user — fall back to **F** for `both` (matches v1 default-unit story; the user explicitly opted into `both` for compactness, not for screen-filling). The unit indicator becomes `F·C` with both glyphs emissive (no inactive). Region 2 cards keep the dual-numeric `77°F · 16°C` rendering on a single line at 18px LED — same as v1 State E, just moved to the new card geometry. The forecast cards in `both` mode also widen their card column from 160 → 200 px to keep the dual line readable; total pane width grows from 880 → 920 px which still clears the 230px-bezel margin (§7 — see also v1 §2 State E justification, math reproduced in §7 below).

---

## 4. Typography spec

All sizes are absolute pixels (kiosk panel is fixed-resolution 1920×720; no responsive type). All weights and `font-variant-numeric` settings echo the existing `.sleep-screen-clock` rule (design-system.css:2880-2901) so the temperature glyphs read as the same "instrument" as the wall clock.

| Element | Font family | Size | Weight | Color | Other |
|---|---|---|---|---|---|
| **Region 1 — current temp** | `var(--font-led)` (Orbitron) | 96 px | 700 | `color-mix(in srgb, var(--signal-amber) 35%, #050507)` | `font-variant-numeric: tabular-nums; letter-spacing: 0.02em; line-height: 1; text-shadow: 0 0 12px color-mix(in srgb, var(--signal-amber) 15%, transparent);` — **byte-identical to `.sleep-screen-clock`** |
| **Region 1 — degree symbol** | `var(--font-led)` | 40 px | 700 | same dim amber | Aligned to the top of the temp glyphs (superscript-style position); achieved by `vertical-align: top` + small negative `margin-top`. |
| **Region 1 — unit indicator (`f·C`)** | `var(--font-mono)` | 18 px | 400 | active = dim amber; inactive = `var(--text-low)` | `text-transform: uppercase; letter-spacing: 0.16em;` Active letter is the one matching `TemperatureUnit`; the `·` separator is always `var(--text-low)`. **Display only — no click target, no hover state.** |
| **Region 1 — condition text** | `var(--font-mono)` | 24 px | 400 | `var(--text-medium)` | Sentence-case from `WeatherDay.ConditionShort` verbatim (NWS labels are already capitalized — *"Partly Sunny"*, not lowercase). `letter-spacing: 0.02em; line-height: 1.1; max-width: 200px;` truncates with ellipsis. |
| **Region 1 — today H/L** | `var(--font-led)` | 28 px | 700 | dim amber + `text-shadow` (same recipe as the big temp) | `tabular-nums; letter-spacing: 0.02em;` Slash separator is `var(--text-low)` at 400 weight so the numerals dominate visually. Format: `{HighF}/{LowF}` — no degree glyph here (the unit is implied by the big temp). |
| **Sub-line — location · day · time** | `var(--font-mono)` | 14 px | 400 | `var(--text-medium)` | `letter-spacing: 0.12em;` — matches the v1 `.sleep-forecast-day` rule so the sub-line and the card day-labels share a visual rhythm. `·` separator at `var(--text-low)`. |
| **Sub-line — stale icon (when present)** | Material Symbols Rounded | 16 px | 400 | `var(--text-low)` | `font-variation-settings: 'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 20;` Inherits the pane's `opacity: 0.7` because it's inside `.is-stale`. |
| **Region 2 — card day label** | `var(--font-mono)` | 14 px | 400 | `var(--text-medium)` | `text-transform: uppercase; letter-spacing: 0.12em; line-height: 1;` Identical to v1 `.sleep-forecast-day` — preserves card vocabulary. |
| **Region 2 — card high** | `var(--font-led)` | 28 px | 700 | dim amber + `text-shadow` (same recipe) | `tabular-nums; letter-spacing: 0.02em;` Includes degree glyph: `77°`. (V1 used 36px; reduced here to balance against the primary block's 96px — see §7.) |
| **Region 2 — card low** | `var(--font-led)` | 20 px | 700 | `var(--text-low)` (NOT amber) | `tabular-nums; letter-spacing: 0.02em;` (V1 used 24px; reduced here for the same reason.) |
| **`.sleep-screen-hint`** | unchanged (existing rule) | 11 px | n/a | `var(--text-low)` | No change — the wake hint sits outside the pane. |

### Why the temp numerals match the clock byte-for-byte

The user's mental model on this surface is "this is the amber readout of my radio." The wall clock and the current temperature are **the same instrument** doing two jobs; if they used different sizes, weights, or colors, the temperature would read as a different element class — louder or quieter than the clock — and break the unified emissive budget. Same font, same size, same color-mix, same text-shadow.

### Why condition text is `--text-medium` and not dim amber

Adding a 200×30px slab of dim amber to the right of the big temp would compete with the temp for visual weight (two emissive things at near-equal area). The condition is contextual, not the headline — `--text-medium` reads as "tag/label" rather than "readout," which is the right relationship to the big number.

### Why the unit indicator is mono and small

It's a stamp on the temperature, not a control. The 18px size matches the sub-line's mono treatment; the dim emissive active letter tells the user which unit they're reading without inviting them to tap it (the actual toggle lives in System Config). Read-only display.

---

## 5. Iconography

### Primary block icon (Region 1)

- **Size:** 96px (matches the temp numeral height — the icon and temp read as a horizontal pair on the same baseline).
- **Family:** Material Symbols Rounded (already loaded — same family as v1 cards and as the rest of the site).
- **Variation settings:** `font-variation-settings: 'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 48;` — `opsz: 48` is the largest standard optical-size axis, which is the closest Material Symbols offers for the 96px rendering. The icon SVG paths scale cleanly from 48 → 96 via the variable font.
- **Color:** same `color-mix(in srgb, var(--signal-amber) 35%, #050507)` as everywhere else.
- **No text-shadow.** The big icon is already a solid filled silhouette at 96px; adding the LED-style glow makes it read as a smudge rather than a graphic. Glow stays exclusive to the LED numerals (clock, temp, H/L).

### Forecast card icon (Region 2)

- **Size:** 48px (V1 used 40; bumped to 48 to read clearly at the new card geometry without competing with the 96px primary icon).
- **Family / fill / color:** same as primary, with `'opsz' 48` (the natural Material Symbols size axis at 48px — no scaling stretch).
- **Color:** dim amber, identical to v1.

### Icon mapping table

Unchanged from `HANDOFF-sleep-mode-weather-forecast.md` §4 — the same 18-entry table (`sunny`, `mostly-sunny`, `partly-cloudy`, `mostly-cloudy`, `cloudy`, `clear-night`, `partly-cloudy-night`, `rain`, `rain-light`, `rain-heavy`, `thunderstorm`, `snow`, `sleet`, `fog`, `wind`, `hot`, `cold`, `unknown`) maps the same way for both Region 1 and Region 2. The same Material Symbol name appears at 96px in the primary block and at 48px in today's card — Builder MUST use the same `IconKeyToSymbol` helper from the v1 `SleepForecastPane.razor` (lines 152–172) so the two icon renderings are guaranteed identical.

### Single-color rule (preserved from v1 §4)

All icons stay dim amber. No multicolor weather icon set. The icon shape carries the meaning; color stays uniform across the whole pane. This is the load-bearing aesthetic choice — the sleep screen has **one emissive color** and a multicolor weather palette would shatter the "off-state" feel.

---

## 6. Color palette

**Total emissive elements on screen (3-card fresh state):**

1. Region 1 big icon (1 silhouette)
2. Region 1 big temp + degree (numerals + glyph)
3. Region 1 unit-indicator active letter (1–2 glyphs)
4. Region 1 today H/L numerals (5 glyphs typical)
5. Region 2 × 3 icons (3 silhouettes)
6. Region 2 × 3 high temps (≈ 9 glyphs)

That is the entire emissive surface area; everything else (condition text, sub-line, card day labels, card low temps, sep glyphs) is **passive** in `--text-medium` or `--text-low`. The pane's emissive footprint is roughly comparable to the v1 layout: v1 had 3 icons + 3 high numerals + 3 low-amber numerals = ~18 amber glyphs/silhouettes; v2 has 1 big icon + 4 big-numeral glyphs + 1 active unit + 5 today H/L numerals + 3 small icons + 9 card-high numerals = ~23 amber glyphs/silhouettes. Slightly higher count, but the v2 distribution is concentrated (big primary + smaller cards) rather than evenly spread, so total amber luminance area is comparable.

| Token / color | Where it's used | Notes |
|---|---|---|
| `color-mix(in srgb, var(--signal-amber) 35%, #050507)` | All LED numerals (big temp, today H/L, card highs), all Material Symbol icons (big + cards), active unit indicator letter | The single emissive color of the pane. **No exceptions.** Builder MUST NOT introduce `var(--signal-amber)` at full intensity, full-saturation `#F0A830`, or any opacity-only variant. |
| `var(--text-medium)` (`#B5BCC9`) | Condition text in Region 1; sub-line text body; card day labels in Region 2 | Passive — reads as "label" not "readout". |
| `var(--text-low)` (`#4B5563`) | Inactive unit indicator letter; H/L `/` separator; sub-line `·` separators; card low temps; stale icon (when present) | Quietest tier; recedes from the layout. The card low being `--text-low` (not amber) is a deliberate hierarchy step — the high is "the answer," the low is "the supporting datum." |
| `0 0 12px color-mix(in srgb, var(--signal-amber) 15%, transparent)` text-shadow | Big temp, today H/L numerals, card-high numerals | Inherited from `.sleep-screen-clock` recipe. **NOT** applied to icons (see §5) or to anything passive. |
| Stale state | Entire pane → `opacity: 0.7` via `.sleep-forecast-pane.is-stale` | Single opacity knob; no per-element color override. **Preserved exactly from v1.** |

### No new tokens

The redesign introduces zero new CSS custom properties. Builder MUST NOT add `--sleep-forecast-*` color or sizing tokens — every color above already exists, every size is a one-off literal that belongs in the scoped section.

---

## 7. Layout dimensions

Kiosk panel: 1920×720, fixed. Drift wrapper safe area: ±384 px horizontal, ±144 px vertical (existing `Sleep.razor` math, lines 141–145).

### Pane bounding box

| Variant | Width | Height | Worst-case offset clearance |
|---|---|---|---|
| 3-day, single unit (F or C) | 880 px | 360 px | center + 384 + 440 = 824 px from viewport center → 136 px of bezel clearance |
| 3-day, `both` unit | 920 px | 380 px | center + 384 + 460 = 844 px → 116 px clearance |
| 2-day, single unit | 760 px | 360 px | center + 384 + 380 = 764 px → 196 px clearance |
| 1-day (Region 2 omitted) | 480 px | 220 px | center + 384 + 240 = 624 px → 336 px clearance |

All four variants leave ≥ 100 px of clearance to the panel bezel at the worst-case drift offset, which is the threshold v1 §2 established. (V1 used 230 px in the 3-day single-unit case — we tighten to 136 px in v2 because the primary block is wider than v1's 3-equal-cards layout. Still inside the safe-area contract; if Builder wants to reduce drift amplitude to `0.18` instead of `0.20` for an extra margin, that's a one-line change in `Sleep.razor` and it stays within Designer's blessing.)

### Region 1 internal dimensions

- **Total width:** 760 px (icon col 120 + gap 24 + temp col 200 + unit col 40 + gap 24 + right col 200 + safety 152 = padded to 880 outer)
- Wait — recompute exact: outer pane is 880, internal layout: `[120 icon][24 gap][200 temp][40 unit][32 gap][200 right] = 616 px` → centered within 880 px gives 132 px of horizontal padding each side. Builder uses CSS `justify-content: center` on the flex row; no fixed widths needed except per-column maxes.

- **Total height:** 180 px (96 px temp row + 84 px breathing room for the right column's two stacked lines and overall vertical centering)
- **Icon column:** 120 px wide × 96 px tall, icon centered
- **Temp column:** 200 px wide × 96 px tall (the numeral baseline owns this region — degree symbol nests inside)
- **Unit column:** 40 px wide × 96 px tall, vertical-center on the temp baseline
- **Right column:** 200 px wide, two stacked rows: condition (≈ 30 px) + today H/L (≈ 28 px) with 12 px gap; total ≈ 70 px, vertically centered on the temp baseline

### Sub-line dimensions

- **Width:** auto (content-sized), centered horizontally beneath Region 1
- **Height:** ≈ 20 px (14 px font + line-height)
- **Top margin to Region 1:** 12 px
- **Bottom margin to Region 2:** 28 px

### Region 2 card dimensions

| Per card | Single-unit | `both` |
|---|---|---|
| Width | 160 px | 200 px |
| Height | 140 px (icon 48 + gap 8 + day 14 + gap 6 + high 28 + gap 4 + low 20 + padding) | 150 px |
| Inter-card gap | 32 px | 32 px |
| Padding (top/bottom) | 12 px | 12 px |

3-card row total: 160×3 + 32×2 = 544 px (single-unit) or 200×3 + 32×2 = 664 px (both).

### Vertical stack (drift wrapper inner gap)

The pane root sets `display: flex; flex-direction: column; gap: 28px;` between Region 1 group and Region 2 group; the sub-line is a sibling element with `margin-top: 12px; margin-bottom: 0;` (the 28 px parent gap then provides the bottom space to Region 2).

### Padding around the pane (none)

The pane has zero outer padding. The drift wrapper already provides the only spacing budget that matters; adding pane padding would just push the bounding box further toward the bezel. The sleep-screen wake hint (`bottom: 32px`) is unaffected because it lives outside the drift wrapper.

---

## 8. Responsive behavior

The kiosk is fixed at 1920×720. There is no media-query story. The "responsive" question is purely about the data shape:

| `Forecast.Days.Count` | Region 1 | Sub-line | Region 2 |
|---|---|---|---|
| 3 (typical) | Today's data | Always rendered | 3 cards (today / tomorrow / day-after) |
| 2 | Today's data | Always rendered | 2 cards centered (today / tomorrow) |
| 1 (today only) | Today's data | Always rendered | **Omitted entirely** (see §3 State D rationale) |
| 0 | Pane does not render (Sleep.razor guards) | n/a | n/a |
| `Forecast == null` | Pane does not render (Sleep.razor guards) | n/a | n/a |

Sleep.razor's existing guard `_forecast is not null && _forecast.Days.Count > 0` handles cases 0 and null. **For case 1 (`Days.Count == 1`), the pane DOES render** — the primary block + sub-line are the entire output. The CSS handles this via `.sleep-forecast-cards:empty` not being possible (Razor conditionally renders the row container only when `Days.Count >= 2`), so empty-state visuals don't apply.

### Builder note for the 1-day case

```razor
@if (Forecast.Days.Count >= 2)
{
  <div class="sleep-forecast-cards">
    @foreach (var d in Forecast.Days) { ... }
  </div>
}
```

(Whether to also drop today from the cards row in the 2-day case — i.e. show only tomorrow as a single card — was considered and rejected: 2 cards still gives a comparative reading, which is the row's purpose. Only the 1-day case omits the row.)

### `TemperatureUnit` switching

Three values: `"F"`, `"C"`, `"both"`. The primary block always shows a single unit (F if `both` is selected, per §3 State F). The cards switch between single and dual rendering per the same rule v1 used; the `.unit-both` modifier on `.sleep-forecast-pane` continues to do the column-width adjustment.

### `IsStale` toggle

Single opacity flip on the outer pane class. No layout shift.

---

## 9. Affected files

Two files. No new files, no backend changes.

### `src/Radio.Web/Components/Shared/SleepForecastPane.razor`

**Markup:** Replace the entire `<div class="sleep-forecast-pane">…</div>` body with the two-region structure:

```razor
<div class="sleep-forecast-pane @(Forecast.IsStale ? "is-stale" : null) @(IsBoth ? "unit-both" : null)"
     role="region"
     aria-live="polite"
     aria-atomic="true"
     aria-label="@_ariaLabel">

  @* Region 1 — primary current weather block *@
  <div class="sleep-forecast-primary">
    <div class="sleep-forecast-primary-icon">
      <span class="material-symbols-rounded">@IconKeyToSymbol(Today.IconKey)</span>
    </div>
    <div class="sleep-forecast-primary-temp">
      @CurrentTempDisplay<span class="sleep-forecast-primary-degree">°</span>
    </div>
    <div class="sleep-forecast-primary-unit">
      <span class="@(IsCelsius ? "is-inactive" : "is-active")">F</span><span class="sleep-forecast-primary-unit-sep">·</span><span class="@(IsCelsius ? "is-active" : "is-inactive")">C</span>
    </div>
    <div class="sleep-forecast-primary-right">
      <div class="sleep-forecast-primary-condition">@Today.ConditionShort</div>
      <div class="sleep-forecast-primary-hl">
        <span class="sleep-forecast-primary-high">@TodayHigh</span><span class="sleep-forecast-primary-hl-sep">/</span><span class="sleep-forecast-primary-low">@TodayLow</span>
      </div>
    </div>
  </div>

  @* Sub-line — location · day · time *@
  <div class="sleep-forecast-subline">
    @if (Forecast.IsStale)
    {
      <span class="sleep-forecast-stale-icon material-symbols-rounded" aria-hidden="true">sync_problem</span>
    }
    <span class="sleep-forecast-subline-text">@SubLineText</span>
  </div>

  @* Region 2 — forecast row (omitted entirely when Days.Count < 2) *@
  @if (Forecast.Days.Count >= 2)
  {
    <div class="sleep-forecast-cards">
      @foreach (var day in Forecast.Days)
      {
        <div class="sleep-forecast-card">
          <div class="sleep-forecast-card-icon">
            <span class="material-symbols-rounded">@IconKeyToSymbol(day.IconKey)</span>
          </div>
          <div class="sleep-forecast-card-day">@day.DayName</div>
          @* card temps follow the same F/C/both rules as v1 — preserved verbatim *@
          ...
        </div>
      }
    </div>
  }
</div>
```

**Code-behind additions:** new computed properties `Today` (= `Forecast.Days[0]`), `IsBoth`, `IsCelsius`, `CurrentTempDisplay` (integer cast of `Today.HighF` or `Today.HighC`), `TodayHigh` / `TodayLow`, `SubLineText` (composed via `Clocks.FormatWallClock` + location parsing). The existing `BuildAriaLabel()` is **extended** to lead with the current condition + temperature before the day-by-day breakdown — the SR string becomes *"Currently 60 degrees Fahrenheit, partly sunny in Pittsboro. Today partly sunny, high 77, low 66. Sunday partly sunny, high 77, low 66. Monday cloudy, high 76, low 68. Tuesday sunny, high 80, low 66."* (Today appears twice in the SR because it appears twice in the visual — the SR description is faithful to the visual layout, not a deduplicated summary.)

**Existing methods preserved verbatim:**
- `IconKeyToSymbol(string)` — the 18-entry switch (lines 152–172)
- `OnParametersSet` footer-timestamp logic (lines 88–109) — REPURPOSED into `SubLineText` computation since the timestamp now lives on the sub-line, not in a separate footer
- `_lastAnnouncedSignature` SR caching (lines 113–119) — the signature gains a `Today.IconKey` field so a same-day icon change retriggers the announcement

**Removed elements:**
- `.sleep-forecast-footer` div (replaced by `.sleep-forecast-subline`)
- `.sleep-forecast-footer-text` span (replaced by `.sleep-forecast-subline-text`)
- (Builder may rename the stale-icon CSS class from `.sleep-forecast-stale-icon` if convenient; same selector name is reused above for minimum diff.)

### `src/Radio.Web/wwwroot/css/design-system.css`

**Section to modify:** §P·6 (lines 2927–3063 in current source).

**Action:** Replace the v1 rules with the v2 rules. Keep the `.sleep-forecast-pane`, `.sleep-forecast-pane.is-stale`, and `.sleep-forecast-pane.unit-both` selectors (their behavior is preserved). Add new rules for `.sleep-forecast-primary` and its children, `.sleep-forecast-subline`, and reshape the card rules per §7 dimensions. Delete the v1 `.sleep-forecast-day` rule's existing dimensions and rebuild for the smaller card geometry; same for `.sleep-forecast-temp-*`.

Builder MUST keep the section-header comment block (lines 2927–2941) and just update its body to describe the v2 layout. The v1 comment about "cards are 200 px each; pane is 640 px total" becomes "Region 1 is the primary readout (96px LED current temp + 96px icon + condition + today H/L); Region 2 is a 3-card forecast row at 160px per card. Total pane 880px (920px in `both` mode), fits inside the 1920px kiosk panel with ≥ 100px bezel clearance at worst-case drift."

### Out of scope (NO changes)

- `src/Radio.Web/Components/Pages/Sleep.razor` — alternation logic, drift math, forecast fetching all unchanged
- `src/Radio.Core/Models/WeatherForecast.cs` + `WeatherDay.cs` — data contract unchanged
- `src/Radio.Web/Services/ApiClients/WeatherApiService.cs` — API contract unchanged
- `src/Radio.Web/Components/Pages/SystemConfigPage.razor` — config UI unchanged
- `src/Radio.Infrastructure/**/*` — no backend changes
- ADR-022 — no architectural revisits

---

## 10. Out of scope

These are explicitly NOT in this iteration:

1. **Weather data sources / providers.** NWS, ZIP config, refresh interval, contact email — all unchanged from v1.
2. **Alternation timing.** Clock ↔ forecast still flips every drift cycle per v1 §3.
3. **Anti-burn-in math.** Drift amplitude, interval, easing, reduced-motion behavior all unchanged.
4. **Accessibility surface.** Same wake behavior, same `aria-live` polite region, same SR caching pattern. (The SR string content does extend to lead with the current condition — that's a content change, not an a11y mechanism change.)
5. **Stale-data threshold.** 12h "yesterday" cutoff unchanged.
6. **Failure modes.** Null forecast still hides the pane; partial days still gracefully degrade (with the new 1-day rule in §3 State D being the only behavioral addition).
7. **New tokens or fonts.** Zero new design tokens; no new font family or weight.
8. **Configuration UI.** No new knobs; the unit indicator in Region 1 is a display-only mirror of the existing `Display:Weather:TemperatureUnit` setting.
9. **Backend changes.** Markup + CSS only.
10. **Animated icons / motion vocabulary changes.** Pure static rendering inside the existing drift wrapper.
11. **Per-element interactions.** Cards remain inert; tapping the pane wakes the system as before. No tap-to-expand for the primary block.
12. **Multi-location / hourly detail / severe weather alerts.** Same out-of-scope list as v1 §12 items 1–10.

---

## Hand-off summary for Planner / Builder

Replace the v1 "three coequal cards" layout with a two-region composition: a primary block (96px Material Symbol icon + 96px Orbitron LED current temperature with byte-identical sizing to `.sleep-screen-clock` + small mono F·C unit indicator + 24px condition text and 28px today H/L on a right column) above a single mono sub-line (location · weekday · time using `Clocks.FormatWallClock`) above a 3-card forecast row (48px icons, 28/20 LED H/L per card). Single emissive dim-amber (`color-mix(in srgb, var(--signal-amber) 35%, #050507)`) for everything emissive, `--text-medium` for condition and sub-line, `--text-low` for separators and card lows. Stale state stays `opacity: 0.7` on the outer pane + a `sync_problem` glyph on the sub-line. Partial-day handling: 2 days centers two cards in Region 2; 1 day omits Region 2 entirely; 0/null is caught upstream and the pane never renders. No new tokens, no new fonts, no backend changes, no config additions. Builder touches `SleepForecastPane.razor` (markup + a few computed properties) and `design-system.css` §P·6 (rules replaced); v1 §3/§4/§5/§6/§7/§8/§9/§10/§11/§12 of `HANDOFF-sleep-mode-weather-forecast.md` remain authoritative for everything outside the visual treatment.
