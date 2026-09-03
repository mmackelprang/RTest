# HANDOFF — Saved Station (Preset) Display Redesign

**Surface:** Memory Presets Bank in `RadioControlPanel.razor` (right-rail "MEMORY · n saved" list)
**Status vs. existing handoffs:** **deviates** from `design_handoff_radio_controller` §P1·2 (the current slot · name+band · freq grid) — with explicit user direction. Shrinks the frequency cell and drops its LED/amber "display chrome" treatment so the **station name** becomes the primary field. **Follows** all design tokens, panel chrome, slot-numbering, long-press-to-save, and kebab-menu conventions established in that handoff and its PR #371 hot-fix.
**Author:** Designer
**Date:** 2026-05-30
**Consumer:** Planner

> **⭐ Rename note (`ENC-7`, 2026-09-02) — the bank is now titled `PRESETS · n saved`, and this
> handoff's `MEMORY · n saved` is deliberately not the shipped wording.**
>
> The cabinet's third knob is **engraved PRESETS** (encoder handoff Rev 3, decision D10), and an
> engraving cannot be edited later. A panel that says PRESETS over a screen that says MEMORY is the
> mismatch class Rev 3 flagged in the settings table, so the on-screen bank took the engraved word.
> `RadioPage.razor` already headed its own panel `Presets`, so the rename moved the two on-screen
> surfaces **into** agreement rather than out of it.
>
> Recorded in three places on purpose:
> [`HANDOFF-rotary-encoder-mapping.md`](HANDOFF-rotary-encoder-mapping.md) Rev 3 (its header and
> §4.4 Knob 3), [`docs/HANDOFF-GA-PUNCH-LIST.md`](../HANDOFF-GA-PUNCH-LIST.md) §6's
> "deliberately parked" table, and here.
>
> **Do not "fix" this on a later consistency pass.** Everything else in this document — field
> hierarchy, slot numbering, long-press-to-save, the kebab menu — is untouched and still current;
> only the one word in the header changed. The body below is left as written because it is the
> record of what was deviated from.

---

## 1. Problem & intent

User: *"The saved station selection is pretty but very truncated. We can change the radio frequency to be SMALLER and we don't need to maintain the font/color for this display. The name of the station is truncated and I'd like to be able to see more of the station name."*

Decoded priorities (in order):
1. **Station NAME is the primary field** — give it maximum horizontal room and allow more characters before truncating.
2. **Frequency is secondary** — render it smaller, and it may drop the LED font (`--font-led`/Orbitron) + amber glow "tuner-display" treatment.
3. **Stay attractive, but legibility/information wins over stylization.**

---

## 2. Current-state audit (what renders today)

**Markup:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor:267-289`
Each preset row:
```
<div class="rcp-preset-item">
  <span class="rcp-preset-slot">01</span>
  <span class="rcp-preset-text">
    <span class="rcp-preset-name">FM - 1...</span>   ← truncates
    <span class="rcp-preset-band">FM</span>
  </span>
  <span class="rcp-preset-freq">105.10 MHz</span>    ← LED font, amber, glow
  <button class="rcp-preset-kebab">⋮</button>
</div>
```

**CSS:** `src/Radio.Web/wwwroot/css/design-system.css`

| Rule | Line | Relevant values |
|---|---|---|
| `.rcp-preset-item` | 4249 | `grid-template-columns: 22px 1fr 64px 24px; gap: 8px; padding: 6px 8px` |
| `.rcp-preset-name` | 4298 | `font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis` |
| `.rcp-preset-band` | 4311 | `font-mono; 9px; --text-low` (sub-line under name) |
| `.rcp-preset-freq` | 4327 | `font-led (Orbitron); 700; 13px; --signal-amber; text-shadow glow; text-align: right` |
| `.rcp-preset-slot` | 4274 | `font-mono; 10px; --text-low` (amber when `.is-active`) |
| `.rcp-preset-empty-hint` | 4357 | dashed placeholder, `grid-column: 2/5` |

**Why the name truncates (root cause):**
- The grid reserves a fixed **64px** freq column. At Orbitron 13px/700, `105.10 MHz` does **not** fit on one line in 64px, so it **wraps to two lines** ("105.10" / "MHz") — visible in the reference screenshot. The two-line freq cell makes the row taller and steals visual weight.
- The name lives in the `1fr` middle column, which in the ~200px-wide rail leaves only ~80–90px after slot (22px) + gap + freq (64px) + kebab (24px). At 12px that's roughly **8–10 characters** before the hard `ellipsis` cuts it ("FM - 1…", "FM W…").
- The amber LED freq is the brightest thing in the row, so the eye is pulled to the *secondary* datum while the *primary* datum (name) is the one being clipped — an inverted hierarchy.

**Screen context:** Deployed device is **1920×720** (wide, short). The MEMORY rail is a fixed-width vertical column (~200px in the `RadioControlPanel` Home context; the standalone `RadioPage.razor` presets panel is 480px and uses a *different*, 2-column card markup — see §8). Vertical space is the scarce dimension: rows must stay compact (single-line-name height) so 7+ slots fit without scrolling.

---

## 3. Layout proposals

All three keep the existing row chrome (border, `:hover` amber tint, `.is-active` amber border, slot column, kebab). They differ in how name vs. frequency share the row.

### Proposal A — Name-primary, single line + inline small freq tail (RECOMMENDED)

Freq drops to the **name's baseline as a small mono suffix**, no longer its own fixed column. Name gets the full remaining width and only yields what the (now-short) freq needs.

```
┌────────────────────────────────────────────┐
│ 01  KEXP Seattle                90.3 ⋮      │   ← name 14px high-contrast,
│     FM                                       │      freq 11px mono dim, no glow
├────────────────────────────────────────────┤
│ 02  Classic Vinyl Rock Channel  105.1 ⋮     │   ← long name fills the row,
│     FM                                       │      ellipsis only past ~22 chars
├────────────────────────────────────────────┤
│ 06  KQED Public Radio·News      88.5 ⋮      │   ← ACTIVE: amber left border
│     FM                          ▮            │      + amber dot, name stays white
└────────────────────────────────────────────┘
```
- **Grid:** `22px 1fr auto 24px` (slot · name+band · freq · kebab). Freq column is `auto` (intrinsic) so a 4–6 char freq like `90.3` takes ~34px instead of a hard-reserved 64px — handing **~30px back to the name**.
- **Name:** 14px (up from 12px), `--text-high`, single line, `ellipsis`. Now fits ~20–24 chars in the same rail.
- **Band:** unchanged dim mono sub-line (9px) under the name.
- **Freq:** 11px `--font-mono` (NOT LED), `--text-medium`, **no glow**, tabular-nums, unit ("MHz"/"kHz") dropped from the row (band sub-line already says FM/AM → MHz/kHz is implied; full unit only in tooltip). Right-aligned, vertically centered to the name baseline.
- **Overflow:** name `ellipsis` after one line; full name in `title`/tooltip and in the rename dialog.
- **Fit:** row height unchanged (~36px) since freq no longer wraps. 1920×720 friendly — more slots visible, name dominant.

### Proposal B — Two-line stack, freq demoted to the band sub-line

Name owns the **entire top line edge-to-edge**; freq joins band on the secondary line.

```
┌────────────────────────────────────────────┐
│ 01  KEXP Seattle                        ⋮   │   ← name 15px, full row width
│     FM · 90.3 MHz                            │   ← meta line: band + freq, dim mono
├────────────────────────────────────────────┤
│ 02  Classic Vinyl Rock Channel KZOK     ⋮   │   ← ~28 chars before ellipsis
│     FM · 105.1 MHz                           │
├────────────────────────────────────────────┤
│ 06  KQED Public Radio                   ⋮   │   ← ACTIVE: amber border, amber
│ ▸   FM · 88.5 MHz                            │      meta line
└────────────────────────────────────────────┘
```
- **Grid:** `22px 1fr 24px` (slot · text-stack · kebab). Freq leaves the row grid entirely.
- **Name:** 15px, `--text-high`, single line, **full 1fr width** → most characters of any proposal (~26–30 before ellipsis).
- **Meta line:** `FM · 90.3 MHz`, 10px `--font-mono`, `--text-low` (→ `--signal-amber` when active). Full unit fits here because it's on its own line.
- **Trade-off:** frequency is now a *sub-detail*, scannable but no longer column-aligned across rows — harder to compare freqs at a glance. Best when the user thinks in **names**, not dial positions.
- **Fit:** row height unchanged (already a 2-line stack today). Pure win on name length; mild loss on freq scannability.

### Proposal C — Name-primary with a small freq "chip"

Freq keeps a touch of identity as a **pill/chip** (bordered, not glowing), but small and de-emphasized; name leads.

```
┌────────────────────────────────────────────┐
│ 01  KEXP Seattle              ⟨90.3⟩  ⋮     │   ← chip: 1px border, mono 10px,
│     FM                                       │      --text-medium, no fill/glow
├────────────────────────────────────────────┤
│ 02  Classic Vinyl Rock Chan…  ⟨105.1⟩ ⋮     │
│     FM                                       │
├────────────────────────────────────────────┤
│ 06  KQED Public Radio         ⟨88.5⟩  ⋮     │   ← ACTIVE: chip border → amber
│     FM                                       │
└────────────────────────────────────────────┘
```
- **Grid:** `22px 1fr auto 24px` (chip is intrinsic width).
- **Name:** 14px, `--text-high`, single line, ellipsis.
- **Freq chip:** `padding: 1px 6px; border: 1px solid var(--surface-separator); border-radius: 4px; font-mono 10px; --text-medium`. On `.is-active`, chip border → `--signal-amber`. No background fill, no glow.
- **Trade-off:** chip adds ~16px of chrome vs. Proposal A's bare tail → slightly less name room than A. Buys a clearer "this is a separate tappable-looking datum" read, but the chip is **not** independently interactive (whole row tunes), so the affordance is cosmetic. Reuses the existing `kbd` chip styling language from `.rcp-presets-hint kbd`.

---

## 4. Recommendation

**Proposal A** (name-primary, single line + small inline freq tail).

**Why:**
- Directly satisfies the three stated priorities with the **least new chrome**: name jumps to 14px and gains ~30px of width (intrinsic `auto` freq column + dropped unit), freq shrinks to dim 11px mono with no glow.
- **Keeps freq column-aligned** on the right edge across rows (Proposal B loses this), so the user can still scan dial positions — useful on a *radio*.
- **Lowest implementation + visual risk**: it's a token swap and a `grid-template-columns` change on existing elements; no new component, no new interactive element (avoids Proposal C's fake-affordance chip).
- Holds the 1920×720 row-height budget (no freq wrap → no taller rows → more slots visible).

**When to prefer the alternatives:**
- **B** if the user, on seeing A, still wants *even longer* names and is fine with freq becoming a sub-detail.
- **C** only if the user explicitly wants the freq to keep a bit of visual "object" identity.

Recommend shipping **A**, and noting B as a one-line CSS follow-up if names still feel tight after real data lands.

---

## 5. Design spec — Proposal A (the deliverable for Planner)

### Field hierarchy (most → least prominent)
1. **Station name** — primary. `--text-high`, 14px, weight 500, single line.
2. **Frequency** — secondary. `--text-medium`, 11px mono, no glow, right-aligned.
3. **Slot number** — tertiary index. `--text-low` mono (amber when active).
4. **Band** — tertiary context. `--text-low` mono 9px sub-line.
5. **Kebab** — affordance, `--text-low`, reveals on hover/focus.

### Type & token map

| Element | Property | Value (token) | Change vs. today |
|---|---|---|---|
| `.rcp-preset-item` | `grid-template-columns` | `22px 1fr auto 24px` | freq col `64px → auto` |
| `.rcp-preset-name` | `font-size` | `14px` | up from 12px |
| `.rcp-preset-name` | `color` / weight | `--text-high` / 500 | unchanged |
| `.rcp-preset-name` | overflow | `nowrap` + `ellipsis` | unchanged (more room now) |
| `.rcp-preset-band` | — | `--font-mono` 9px `--text-low` | unchanged |
| `.rcp-preset-freq` | `font-family` | `--font-mono` | **was `--font-led`** |
| `.rcp-preset-freq` | `font-size` | `11px` | down from 13px |
| `.rcp-preset-freq` | `font-weight` | `500` | down from 700 |
| `.rcp-preset-freq` | `color` | `--text-medium` | **was `--signal-amber`** |
| `.rcp-preset-freq` | `text-shadow` | `none` | **glow removed** |
| `.rcp-preset-freq` | `font-variant-numeric` | `tabular-nums` | unchanged (column align) |
| `.rcp-preset-slot` | — | unchanged | — |
| `.rcp-preset-item` padding/gap | — | unchanged (`6px 8px` / `8px`) | — |

No new tokens introduced. (`--font-mono` = JetBrains Mono; `--text-medium` = #B5BCC9; `--signal-amber` = #F0A830 — see `design-system.css:74-105`.)

### Frequency string format
- Show **number only**, no unit, in the row: `90.3`, `105.1`, `88.5`, `1010` (AM kHz).
- Keep tabular-nums so decimals align down the column.
- Full `90.3 MHz` / `1010 kHz` belongs in the row `title` tooltip and the rename dialog, not the row face.
- Formatting still flows through the existing `FormatFrequency(preset.Frequency, preset.Band)` helper — Planner: add a unit-less variant or strip the unit suffix for the row; keep the full string for the tooltip.

---

## 6. States

| State | Spec |
|---|---|
| **Default** | As §5. Name `--text-high`, freq dim mono, hairline border `--surface-separator`. |
| **Hover** | Existing: border → 25% amber mix, bg → 4% amber tint (`design-system.css:4264`). Kebab raises to `--text-medium`. Name + freq colors unchanged. |
| **Selected / active station** (`IsActivePreset` true) | Existing amber border + 8% amber bg (`:4269`). **Name stays `--text-high`** (do NOT recolor name amber — keep legibility). **Slot number → amber** (existing). Freq → `--text-high` (lifts from dim to full, but **still mono, still no glow** — the active cue is the border + slot, not a glowing freq). Add a 6px amber dot or left-bar as a redundant non-color active cue for accessibility (see §7). |
| **Long-name overflow** | Single-line `ellipsis`. Full name in `title` tooltip + rename dialog. No marquee (marquee on a static list is distracting; reserve motion for the RDS ticker, not presets). If the user later wants more, escalate to Proposal B (full-width name line). |
| **No-name / freq-only fallback** | If `preset.Name` is empty/whitespace, render the **frequency as the primary line** in `--text-high` 14px mono (promoted from the tail), and drop the dim freq tail. Band sub-line stays. Prevents a blank primary row. |
| **Empty next slot** | Unchanged: dashed border, `EMPTY · long-press <band> to save`, `grid-column: 2/5` (now `2/4` if freq column collapses to `auto` and is absent on the placeholder — Planner verify span). |
| **No presets at all** | Unchanged `.rcp-presets-empty` (radio glyph + "NO PRESETS"). |
| **Loading** (`_presets == null`) | Unchanged (currently renders the empty block until loaded; acceptable). |

---

## 7. Accessibility & interaction

- **Active state must not rely on amber alone** (color-blind safety + the active row already uses amber for border): add a non-color cue — a 6px `--signal-amber` dot or a 3px left bar inside the row. Mirror the `border-left: 3px solid var(--signal-amber)` pattern already used elsewhere (`design-system.css:612`).
- **Name contrast:** 14px `--text-high` (#F0EFF4) on `--surface-elevated` (#1C1C1F) — passes AAA. Dim freq `--text-medium` (#B5BCC9) at 11px passes AA for the secondary datum.
- **Tooltip:** keep the existing row `title="Click to tune; long-press or use ⋮ for rename / delete"` AND surface the full name + full `90.3 MHz` unit. Planner: append name+freq to the title or add `aria-label`.
- **Keyboard:** rows are already `role="button" tabindex="0"`; no change. Kebab keeps its own `aria-label`.
- **Gestures unchanged:** tap = tune, long-press = action menu, kebab = action menu. None of this redesign touches the gesture layer.
- **No motion** added. Reduced-motion users unaffected.

---

## 8. Scope note for Planner (two renderers exist — confirm target)

There are **two** saved-station renderers in the codebase:
1. **`RadioControlPanel.razor:267-289`** + `design-system.css §X (4207+)` — the **`.rcp-preset-*` grid rows** in the Home right-rail. **This is the one in the user's screenshot and the target of this handoff.**
2. **`RadioPage.razor:146-165`** — a separate 2-column card grid (480px panel, inline styles, `--signal-amber` mono freq at 14px, `font-weight:600` name at 16px). Same truncation class of problem but different markup.

**Recommendation to Planner:** apply Proposal A to renderer #1 (the screenshot surface). Then decide with the user whether to (a) also retrofit renderer #2 for consistency, or (b) note it in the spec as a follow-up. Do **not** silently diverge the two — Polisher will flag the drift. If both are kept, the `.rcp-preset-*` rules should become the shared source of truth and `RadioPage.razor` should adopt them rather than re-implement inline.

---

## 9. Acceptance criteria (for Tester)

- [ ] Station name renders at 14px `--text-high`, single line, and shows ≥ ~20 chars before ellipsis in the ~200px rail (vs. ~8–10 today).
- [ ] Frequency renders in `--font-mono` (NOT Orbitron/`--font-led`), ≤ 11px, `--text-medium`, **no glow / no amber** in the default state, on a **single line** (no "105.10 / MHz" wrap).
- [ ] Frequency numerals stay column-aligned (tabular-nums) down the list.
- [ ] Active preset shows amber border + amber slot number + a non-color cue (dot/bar); the **name color stays `--text-high`**, not amber.
- [ ] A long name (e.g. "Classic Vinyl Rock Channel") truncates with ellipsis and exposes the full name via tooltip.
- [ ] A nameless preset shows the frequency as its primary line, not a blank row.
- [ ] Row height is unchanged or shorter; 7 slots still fit the rail without new scrolling on 1920×720.
- [ ] No new design tokens; only existing `--font-mono`, `--text-*`, `--signal-amber`, `--surface-*` are referenced.

---

## 10. Mockup reference

ASCII mockups in §3 are the visual reference (no Claude Design export exists for this surface). The current-state reference is the user's screenshot `Screenshot 2026-05-30 084818.png` — the right-rail "MEMORY · 7" column with truncated "FM - 1…" names and large amber two-line frequencies.
