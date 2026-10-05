# Handoff: Radio Console — Design Tightening Pass

## Overview

This package contains a curated set of design improvements for the **Radio.Web** Blazor Server kiosk app (the 1920×720 multi-source audio console at `src/Radio.Web/`). It is the deliverable from a design-review pass that audited the current screens (`screenshots/*.png`) against `design-system.css` and the shared panel components.

The goal is not a rebuild. The aesthetic — broadcast-console dark + DSEG amber LED + electric cyan accent + per-source color coding — is intact and good. The package fixes **what's leaking through the surface** (raw IDs, raw `TimeSpan`s, unit bugs, debug controls in production chrome, half-empty pages) and **promotes a few components** that were sitting in the corner doing real work (Visualizer mode picker, source-pill detail affordance).

## About the design files

The HTML files in this folder are **design references**, not code to copy into the app:

- `Design Analysis.html` — long-form audit identifying 5 P0, 8 P1, and 11 P2 findings, with severity ratings and rationale.
- `Handoff Canvas.html` (+ `design-canvas.jsx`, `mocks.jsx`, `app.jsx`) — a pan/zoom canvas of **12 focused before/after artboards**, one per proposed change. This is what the developer should treat as the visual spec.

Both files are styled in the project's own design language (same tokens, same fonts where shippable). The real implementation happens **in the existing Blazor codebase** using its existing components (Radzen, the design-system tokens, the SignalR hub) — not by porting HTML into the app.

## Fidelity

**High-fidelity for visuals, behaviour-spec for interactions.**

- Spacing, colors, typography, and component shapes shown in the canvas mocks should be matched within ±2px and exact token values (mocks use the same hex values as `design-system.css`).
- Interaction details (gestures, state machines, route changes) are written out in `IMPLEMENTATION.md`. The mocks are static — they cannot show motion or transitions, so the script is the source of truth there.

## How to use this package

1. **Review `Handoff Canvas.html`** first. The user has labeled which artboards are approved, parked, or need iteration via the inline edit / drag-reorder controls.
2. **Read `IMPLEMENTATION.md`**. Each section maps one canvas artboard → one or more files in `src/Radio.Web/` with concrete steps, code references, and acceptance criteria.
3. **Skip any section marked `[PARKED]` or `[NEEDS ITERATION]`** at the top — those are not approved yet.
4. **Land changes in the order given** (P0 → P1 → P2). P0 includes one new file (`Formatting/DisplayNames.cs`) that several later changes depend on.

## Status legend

Each change in `IMPLEMENTATION.md` carries one of:

| Status | Meaning |
|---|---|
| `[APPROVED]` | Ship as specified. |
| `[PENDING REVIEW]` | Drafted but not yet locked. Don't start. |
| `[NEEDS ITERATION]` | User has comments; see the inline note for what to change. |
| `[PARKED]` | Out of scope for this pass. |

## Files in this folder

- `README.md` — this file.
- `IMPLEMENTATION.md` — the developer script, one section per change.
- `Design Analysis.html` — long-form audit with rationale and severity ratings.
- `Handoff Canvas.html` + `design-canvas.jsx` + `mocks.jsx` + `app.jsx` — visual specs.

## Design tokens

All values referenced in the implementation script already exist in `src/Radio.Web/wwwroot/css/design-system.css`. Do **not** introduce new tokens; if a value is needed that isn't tokenized, add it to design-system.css first under the appropriate section (`§2 CSS Custom Properties`) and reference it from there.
