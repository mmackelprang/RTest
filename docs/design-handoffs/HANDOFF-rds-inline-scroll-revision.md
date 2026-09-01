# HANDOFF — RDS inline-scroll revision (collapses PR #416 second row)

**Component:** `src/Radio.Web/Components/Shared/RdsCard.razor` + `RdsScrollMarquee.razor`
**Surface:** Home → Radio source → Radio Control Panel → the `RdsCard` above the frequency well.
**Status:** `[PENDING REVIEW]` — supersedes the second-row layout shipped in PR #416 only. All other behaviour from `HANDOFF-rds-accumulating-scroll.md` (buffer accumulation, dedup, station-change reset, configurable speed/length/separator, prefers-reduced-motion, SR mirror) is **unchanged**.
**Relationship to existing handoffs:**
- **Follows** `HANDOFF-rds-accumulating-scroll.md` for everything *except* layout.
- **Deviates** from PR #416's "second row inside the card" decision — that change cured the duplicated-surface bug but introduced a vertical-footprint regression that pushes the frequency display + STEREO badge out of view at the project's 1920×720 viewport.

---

## 1. Problem

PR #416 nested `<RdsScrollMarquee>` as a second flex-column child of `.rds-card` (see `design-system.css:3691-3703`, `flex-direction: column`). With both rows visible the card grew from one line to two, and on the production Ubuntu console (1920×720) the frequency well's lower edge — including the `STEREO` badge — now sits below the visible area.

**User verbatim:**
> "I'd like the light blue RDS text to continuously scroll in the space it has with the latest RDS data. With the extra line for the additional RDS text, the data below the frequency is cut off (STEREO badge for example)."

**Screenshot ref:** user-supplied screenshot, top-to-bottom:
- Row 1 (`.rds-card-row`): `RDS  Eagles  CLASSIC ROCK`
- Row 2 (nested marquee): `Green Day · Boulevard of Broken Dreams · Rock 92`
- Below: frequency partially obscured, STEREO badge clipped.

The fix is to collapse row 2 back into row 1 — the light blue "station" slot becomes the marquee surface for the accumulating RT buffer when RT is present, anchored by the PS name.

---

## 2. Layout decision — option (c): PS-anchored, RT scrolls after it in the same line

Of the four options:

| Option | Choice | Why not |
|---|---|---|
| (a) PS only | rejected | doesn't scroll — no RT visibility, regresses PR #414. |
| (b) RT replaces PS | rejected | loses the station-identity anchor. User looks at the card to know *what station* — PS disappearing under RT makes the card feel like a generic ticker. |
| **(c) PS anchored, then RT scrolls after it, single track** | **chosen** | preserves identity ("Eagles" stays visible as the first thing read) and gives RT its scroll. Matches the user's phrase "in the space it has." |
| (d) PS left-anchored, RT scrolls in a middle region between PS and PTY | rejected | requires a three-column flex split. At the card's 420 px max-width with a 14 px PS and a PTY pill, the middle region is often <120 px — too cramped for a readable marquee. Adds layout complexity for marginal gain. |

**Rule:** when RT is non-empty, the marquee track text is `"{PS} • {RT}"` (where ` • ` is the existing `RtChunkSeparator` from `RdsScrollOptions` — re-used so the PS-to-RT join visually matches the inter-chunk joins inside the RT buffer). When PS is empty (transient tune-in), the track is just `"{RT}"`. When RT is empty, no marquee — render the static PS only (current pre-#414 behaviour).

The static-fit branch in `RdsScrollMarquee` (`.is-static` — handoff §8 q8) still applies: if `PS + sep + RT` width ≤ container width, no animation, just left-aligned static text.

PTY chip remains a separate flex child pinned to the right of the same row, unchanged.

---

## 3. Single-line mockup

### State A — PS + RT both present, exceeds row width (the common case)

```
┌─ .rds-card (single flex-row, max-width 420 px) ────────────────────────┐
│ RDS │ Eagles • Green Day · Boulevard of Broken Dr…→ scroll  │ CLASSIC ROCK │
│ ^^^   ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^   ^^^^^^^^^^^^^ │
│ label  marquee track (flex:1, overflow hidden, edge fade)    PTY (pinned right) │
└────────────────────────────────────────────────────────────────────────┘
   ┌─ frequency well (now fully visible again, STEREO badge restored) ─┐
   │   98.5 MHz                                                STEREO  │
   └────────────────────────────────────────────────────────────────────┘
```

- Marquee region is the **PS-slot itself** — same blue colour, same 14 px mono, same letter-spacing. The PS name `Eagles` *is* the first text in the scroll track, so it reads as one continuous identity-plus-context string.
- Edge-fade gradients (existing on `.rcp-rds-rt-scroll`) handle the chars-appearing-on-the-right behaviour at the marquee's right boundary (the PTY pill's left edge).
- PTY pill stays right-pinned; never participates in the scroll.

### State B — PS present, RT empty (no RDS RT broadcast)

```
┌─ .rds-card ────────────────────────────────────────┐
│ RDS │ Eagles                              │ CLASSIC ROCK │
│       (static, no marquee, no animation)   (pinned)      │
└────────────────────────────────────────────────────┘
```

Identical to the pre-PR-#414 card. No marquee container at all — `RdsCard` falls back to the original `<span class="rds-card-station">` element.

### State C — PS empty (transient), RT present

```
┌─ .rds-card ────────────────────────────────────────┐
│ RDS │ Green Day · Boulevard of Broken Dreams…→     │ CLASSIC ROCK │
│       (marquee with RT only, no PS prefix)                       │
└────────────────────────────────────────────────────┘
```

Track text is just `RT` — no leading separator.

### State D — both empty

Card does not render (existing `@if` gate in `RdsCard.razor:12` preserved).

---

## 4. CSS changes (`design-system.css`)

All edits land in the block at lines ~3681–3766.

1. **`.rds-card`** — revert to flex-row:
   - `flex-direction: column` → `flex-direction: row`
   - `gap: 4px` → `gap: 12px` (matches the pre-#416 `.rds-card-row` value, restores PS↔PTY spacing)
   - `align-items: center` added
   - Other properties (padding, background, border, max-width) unchanged.

2. **`.rds-card-row`** — **delete the rule entirely**. With the wrapper element removed in markup (§5), the class no longer exists.

3. **`.rds-card-station-spacer`** — **keep**, but it's now applied directly under `.rds-card` (no `.rds-card-row` parent). The `flex: 1; min-width: 0` rule still does the right job in the new flat layout.

4. **`.rds-card .rcp-rds-rt-scroll`** (the marquee-nested adjustment block, lines 3763–3766) — rewrite to make the marquee fill the same slot the static PS used to fill:
   ```css
   .rds-card .rcp-rds-rt-scroll {
     margin-top: 0;
     flex: 1;            /* take the row remainder between RDS label and PTY pill */
     min-width: 0;       /* allow shrink so PTY stays pinned right */
     max-width: 100%;
     height: auto;       /* override the 1.6em that the standalone variant uses */
   }
   ```

5. **`.rds-card .rcp-rds-rt-track`** — **new** override so the in-card marquee inherits PS typography, not the dim 11 px RT typography of the standalone variant:
   ```css
   .rds-card .rcp-rds-rt-track {
     font-size: 14px;
     font-weight: 700;
     color: var(--accent-primary);
     letter-spacing: 0.18em;
     text-shadow: 0 0 8px color-mix(in srgb, var(--accent-primary) 40%, transparent);
   }
   ```
   This is the single deviation from "marquee looks the same everywhere" — justified because the marquee now occupies the PS slot and must read as the PS slot. The standalone `.rcp-rds-rt-scroll` (if ever re-used elsewhere) keeps its dim-low styling.

6. **`.rcp-rds-rt-scroll` base rule** — unchanged (still 11 px / `--text-low` for any future standalone use).

7. **Doc comment at line 3680–3690** — rewrite to describe the single-row layout + the in-card style override. Reference this revision handoff by filename.

---

## 5. Component changes

### `RdsCard.razor`
- **Remove** the `<div class="rds-card-row">` wrapper element.
- Compose the marquee text once: `var trackText = string.IsNullOrEmpty(StationName) ? RadioText : $"{StationName}{Separator}{RadioText}";` where `Separator` defaults to ` • ` (new parameter `[Parameter] public string Separator { get; set; } = " • ";` — `RadioControlPanel` will thread the `RtChunkSeparator` option through).
- New render rule:
  - If `RadioText` is non-empty → render `<RdsScrollMarquee Text="@trackText" ScrollSpeedPxPerSec="@ScrollSpeedPxPerSec" />` in the station slot (between `.rds-card-label` and `.rds-card-pty`).
  - Else if `StationName` is non-empty → render the original `<span class="rds-card-station">@StationName</span>`.
  - Else → render `<span class="rds-card-station-spacer"></span>` (so PTY stays right-pinned during transient PS-null state).
- PTY chip block unchanged.
- Outer `@if (!string.IsNullOrEmpty(StationName) || !string.IsNullOrEmpty(RadioText))` gate unchanged.

### `RdsScrollMarquee.razor`
- **No code changes.** The component already accepts any string; PR-#416's reuse contract is preserved. The new in-card typography is applied entirely by the CSS override in §4.5.

### `RadioControlPanel.razor`
- Pass the resolved `RtChunkSeparator` option to `RdsCard.Separator` so PS↔RT and RT-chunk↔RT-chunk joins use the same glyph.

---

## 6. Empty-state behaviour matrix

| StationName | RadioText | Rendered |
|---|---|---|
| empty | empty | Card hidden (existing `@if`). |
| present | empty | Static `<span class="rds-card-station">` — no marquee, no animation. **This is the regression-prevention case** — single short station name should not scroll. |
| empty | present | Marquee with RT only (no PS prefix, no leading separator). |
| present | present | Marquee with `"{PS} • {RT}"`. Static-fit branch (`.is-static`) auto-engages when total width ≤ container; otherwise scrolls at configured speed. |

---

## 7. Regression test updates

`tests/Radio.Web.Tests/Components/Shared/RadioControlPanelTests.cs` already contains `RtLine_RendersExactlyOnce_InsideRdsCard_NoDuplicate` (the PR #416 anti-duplicate test). Builder should:

1. **Keep** that test — the underlying invariant (RT text appears exactly once in the DOM) is preserved by this revision and remains the load-bearing guard against the original PR #414 bug.
2. **Add** a new test `RdsCard_RendersAsSingleRow_PSAndRtShareOneLine`:
   - Render `<RdsCard StationName="Eagles" RadioText="Green Day · Boulevard" ProgramType="ROCK" />`.
   - Assert `cut.Find(".rds-card").Children.Length` excludes any `.rds-card-row` element (the wrapper should be gone).
   - Assert `cut.FindAll(".rcp-rds-rt-track")` count == 1 AND its text content contains both `"Eagles"` and `"Green Day · Boulevard"` joined by the separator.
3. **Add** `RdsCard_RtEmpty_RendersStaticStationOnly`:
   - Render with `RadioText=null` and `StationName="Eagles"`.
   - Assert `cut.FindAll(".rcp-rds-rt-scroll")` is empty AND `cut.Find(".rds-card-station").TextContent == "Eagles"`.
4. **Add** `RdsCard_PsEmpty_RtPresent_RendersMarqueeWithoutLeadingSeparator`:
   - Render with `StationName=null`, `RadioText="Some RT"`.
   - Assert the marquee track text equals `"Some RT"` (no leading ` • `).

---

## 8. Out of scope (do not regress, do not extend)

Explicitly NOT changing in this revision:
- Buffer accumulation logic (`RdsAccumulatingScrollBuffer` — dedup, overflow truncation, station-change reset).
- The three SQLite config keys (`RtBufferMaxChars`, `RtScrollSpeedPxPerSec`, `RtChunkSeparator`).
- Scroll animation engine (CSS keyframes on `translateX`, pause-on-hover/focus, `prefers-reduced-motion` fallback).
- SR-only `aria-live="polite"` mirror.
- API contract / SignalR DTO / `RdsDecoder`.
- PTY chip styling and placement.
- The frequency well chrome, signal meter, controls (untouched as in original handoff).

---

## Hand-off summary

Collapse PR #416's two-row card back to a single row. The light-blue PS slot becomes the marquee surface: when RT is present, the track scrolls `"{PS} • {RT}"` (or just `{RT}` if PS is transient-null); when RT is absent, render the original static PS. PTY pill stays right-pinned. CSS reverts `.rds-card` to flex-row, deletes `.rds-card-row`, and adds an in-card typography override on `.rcp-rds-rt-track` so the marquee inherits PS visual weight. All buffer/scroll/a11y/config behaviour from `HANDOFF-rds-accumulating-scroll.md` is preserved. Existing anti-duplicate test stays; three new tests pin the single-row invariant.
