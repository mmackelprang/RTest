# Handoff: Phone page dark-theme fixes + app-wide scrollbar treatment

- **Status:** Draft for owner review (Designer phase)
- **Date:** 2026-07-16
- **Author:** Designer (Claude)
- **Surface:** `/phone` in `src/Radio.Web` (Blazor Server + **Radzen** `material-dark` + `wwwroot/css/design-system.css`), plus an **app-wide** scrollbar/`color-scheme` fix that touches every scroll container in the app.
- **Form factor:** wall-mounted kiosk, **1920×720**, touch-first, glanceable across a room. 120px topbar → **600px content area**; lists scroll internally.
- **Read-only handoff.** No source was changed. This is the artifact **Planner** consumes to scope PRs.

> **Stack correction for anyone carrying MudBlazor assumptions:** this app is **Radzen**, not MudBlazor. There is no `MudTheme`/`PaletteDark`. The dark theme is Radzen `material-dark-base.css` (linked in `Components/App.razor:23`) plus the app's own token block in `design-system.css` `:root` (lines 49–138). The `wwwroot/css/virtual-keyboard.css` file is MudBlazor-selectored and unconsumed — ignore it.

---

## follows / extends / deviates

**FOLLOWS (reused verbatim — do not reinvent):**
- The **Command Surface design system** `:root` tokens in `design-system.css` (49–138). **Zero new colours, zero new fonts.**
- The **Phone-surface visual language** (`design-system.css` §Ph, 4953+) and the **list-row + state primitives** (`.list-item-touch` 582; `.list-item-title/subtitle/meta` 616–638; `.list-item-active` 602; `.empty-state*` 838–857; `.skeleton*` 1042–1174; `.phone-pill` 5265–5288; `.nav-badge` 503; `unread-dot` 5562; `vm-chip` 5548) — the same primitives `HANDOFF-phone-messages-voicemail-sms.md` adopted.
- The **existing thin-scrollbar intent** (`design-system.css` §19, 1263–1304): thumb = `--text-low`, hover = `--text-medium`, transparent track. This handoff keeps that intent and makes it global + theme-aware.

**EXTENDS (new, built FROM the patterns above):**
- A **shared 44px identity chip** across all three unified-feed row kinds (call / voicemail / text). Voicemail already has `.vm-chip`; text rows have a small pill; **call rows have only a bare floating icon** — this handoff gives calls the same 44px chip footprint so the feed has one visual spine. Chip tints reuse `--signal-*` at the same alphas `.phone-pill` already uses. No new colours.
- **Smart/relative timestamps** in the feed (a formatting change in `PhoneCallFormatting.cs`, not a data-model change).
- **`:focus-visible`** ring on `.list-item-touch` (currently absent — required for keyboard / kiosk-remote nav).
- A single **canonical, theme-aware scrollbar** applied globally (replaces the fragile opt-in allowlist), plus **`color-scheme: dark`** on `:root`.

**DEVIATES (flagged — needs an owner nod, not Designer initiative):**
1. **Canonical scrollbar width changes from `3px` → `~4px visible` (8px hit-box).** The current opt-in rules use `width: 3px` (1286). Unifying every scrollbar to one value and making it a touch nudge more discoverable is a deliberate, app-wide change from the shipped 3px. Rationale: the current 3px is nearly invisible; the *complaint* is a native light bar, but once tamed we still want the thumb findable. If you prefer to keep 3px exactly, say so and I'll pin it — the fix works at either width. **This is the one true deviation.**

Everything else maps to an existing token/class or is a pure bug fix (restoring the intended dark appearance).

---

## Design token reference (confirmed values, `design-system.css` `:root` 49–138)

| Token | Value | Role in this handoff |
|---|---|---|
| `--surface-base` | `#0D0D0F` | page background (what the near-white rows/scrollbar clash with) |
| `--surface-raised` | `#141416` | cards |
| `--surface-inset` | `#0A0A0C` | rail bg, mode-selector bg |
| `--surface-overlay` | `#1A1A1D` | hover lift |
| `--surface-separator` | `#1F1F22` | borders / dividers / row hairline |
| `--surface-hover` | `rgba(255,255,255,0.05)` | rail-tab hover |
| `--accent-primary` | `#5CD4E8` | cyan — active tab, selected row, unread dot, focus ring |
| `--accent-surface` | `rgba(92,212,232,0.08)` | selected-row fill |
| `--accent-dim` | `rgba(92,212,232,0.06)` | active-tab bg |
| `--accent-glow` | `rgba(92,212,232,0.15)` | active-indicator glow |
| `--signal-amber` | `#F0A830` | badge bg, "Rotary" answered pill, LED |
| `--signal-green` | `#4ADE80` | incoming/answered direction |
| `--signal-red` | `#F87171` | missed direction |
| `--signal-blue` | `#60A5FA` | outgoing direction |
| `--text-high` | `#F0EFF4` | primary text (caller name) |
| `--text-medium` | `#B5BCC9` | secondary text (number, timestamp) |
| `--text-low` | `#4B5563` | chrome, meta, **scrollbar thumb** |
| `--text-inverse` | `#0D0D0F` | badge text on amber |
| `--sp-2 / --sp-3 / --sp-4` | `8 / 12 / 16px` | row padding, gaps |
| `--topbar-height / --content-height` | `120px / 600px` | shell geometry |

---

## Problem statement

The `/phone` **Messages** view (default tab) has three dark-theme defects visible in the owner's screenshot:

1. **Call/message rows render as bright near-white cards** on the `#0D0D0F` page — they look pasted-in and clash with every other surface.
2. **The left "PHONE / MESSAGES [2] / MORE" rail is clipped at the physical left edge** — content and the active-tab accent bar hug (and overhang) viewport `x=0`.
3. **A thick, bright light-gray native scrollbar** sits on the call list — harsh against the dark UI.

All three share a common thread: **the app never declares `color-scheme: dark`, and two `<button>`-based/opt-in surfaces fall back to the browser's light-mode defaults.** Fixing them is mostly *restoring the intended dark appearance*, not net-new design. On top of that, the call-log/messages rows have a weak information hierarchy worth a focused polish (P2).

---

## Issue 1 — Near-white rows (root cause + fix)

### Root cause
`.list-item-touch` (`design-system.css:582`) is applied to **`<button>` elements** — `PhoneMessagesPanel.razor` `RenderCallRow` (475), `RenderTextThreadRow` (505), and `VoicemailRow.razor`. The base rule sets layout (`display/gap/padding/min-height/border-left`) but **never resets the native button chrome** (`background`, `border`, `appearance`, `font`, `text-align`). Because **`color-scheme` is undeclared anywhere** (confirmed: zero matches in `src/Radio.Web`), Chromium paints the UA default `ButtonFace` — a near-white fill — behind every row. The sibling button classes already do the reset correctly: `.phone-rail-tab` (`background: transparent; border: none;` 4986–4987) and `.phone-mode-btn` (5322–5323). `.list-item-touch` simply missed it.

### Fix — `design-system.css`, `.list-item-touch` base rule (582)
Add the same reset the neighbouring button classes already carry:

```css
.list-item-touch {
  /* NEW — strip native <button> chrome so the dark surface shows through */
  appearance: none;
  -webkit-appearance: none;
  background: transparent;      /* was unset → UA ButtonFace (near-white) */
  border: none;                 /* remove UA button border */
  border-left: 3px solid transparent;  /* keep existing selected-state hook */
  color: inherit;               /* avoid UA ButtonText */
  font: inherit;                /* buttons reset font to the UA canvas font */
  text-align: left;             /* buttons center by default */
  width: 100%;                  /* buttons shrink-to-fit; rows must fill the feed width */
  /* existing: display:flex; align-items:center; gap:12px; padding:8px 16px;
     min-height:56px; cursor:pointer; transition:background 80ms ease;
     -webkit-tap-highlight-color:transparent; */
}
```

`width:100%; text-align:left; font:inherit; appearance:none` are no-ops where `.list-item-touch` is used on a `<div>` (e.g. queue rows), so this is safe everywhere the class appears. Combined with **`color-scheme: dark`** from Issue 3, rows become fully transparent over `--surface-base`, with the existing hover (`rgba(255,255,255,0.03)`) / active (`--accent-surface` + cyan left border) states now reading correctly.

### States (unchanged intent, now visible)
- **Rest:** transparent over `--surface-base`.
- **Hover:** `rgba(255,255,255,0.03)` (existing 594).
- **Pressed:** `rgba(255,255,255,0.06)` (existing 598).
- **Selected** (opens detail pane): `--accent-surface` fill + 3px `--accent-primary` left border (existing `.list-item-active` 602).
- **Focus-visible (NEW — add):**
  ```css
  .list-item-touch:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--accent-primary); }
  ```

---

## Issue 2 — Left rail clipped at the viewport edge (root cause + fix)

### Root cause
The phone surface **hugs the physical left edge of the panel with almost no gutter**, and the active-tab indicator is deliberately positioned at **negative x**:

- The shell chain has **no left margin/padding**: `.content-area` (544: `margin-top` only, `overflow:hidden`, no `padding-left`) → `.page-transition` (1027: `width:100%`) → `.phone-shell` grid (4956: `grid-template-columns: 156px 1fr`) starts at viewport `x=0`.
- `.phone-tab-rail` has only `padding: 16px 12px` (4968) → heading text starts at `x≈20px`; tab labels at `x≈24px`.
- `.phone-rail-tab.active::before` (5005–5012) is pulled to `left:-12px` (viewport `x≈0`) with `box-shadow: 0 0 8px` — the glow reaches `x≈-8`, where it is **clipped by the ancestor `.content-area { overflow:hidden }`**.

Net effect on the bezeled 1920×720 kiosk: the leftmost ~20px sliver (and the active accent bar/glow) sits at or past the screen edge, so "PHONE / MESSAGES / MORE" read as cut off.

### Fix — add a left safe-gutter + bring the indicator inside the padding box
`design-system.css`:
```css
.phone-tab-rail {
  padding: 16px 12px 16px 20px;   /* was 16px 12px — +8px left safe-gutter */
}
.phone-rail-tab.active::before {
  left: -8px;                     /* was -12px — bar + 8px glow now live at x≥0, no clip */
}
```
With a 20px rail left-padding, tab content sits at `x≈32px` (comfortable gutter) and the active bar at `x≈12px` with its glow fully inside the viewport.

### Verification step for Builder (do this in the running app)
This diagnosis is from the CSS; the *exact* truncation magnitude depends on the live layout. Before/after the fix, open `/phone` at 1920×720 in Chrome DevTools and confirm:
1. `.content-area`, `.page-transition`, `.phone-shell` have **no** negative `margin-left`, `transform: translateX(-…)`, or non-zero `scrollLeft`. (None exist in the current CSS — but if one is found, *that* is the true root cause; remove it and the gutter above is still good hygiene.)
2. The `.phone-rail-heading` text left edge is ≥ 24px from the viewport left.
3. The active `::before` bar + glow are fully within the viewport (nothing painting at x<0).

---

## Issue 3 — Scrollbars app-wide (root cause + canonical fix)

### Root cause (two compounding causes)
1. **`color-scheme` is never declared** (zero matches in `src/Radio.Web`). Chromium therefore renders *all* native controls — including scrollbars — in **light mode**: the "thick, bright light-gray/white" bar over the dark UI.
2. **The thin-scrollbar treatment is an opt-in allowlist**, not global. `design-system.css` §19 (1273–1304) styles only `.scrollable, #queue-scroll-container, .rz-tabview-panels, .rz-data-grid-data, .history-filter-scroll`. `scrollbar-width`/`::-webkit-scrollbar` **do not inherit**, so any scroll container *not* on the list gets the browser default. The phone feed containers — `.phone-messages-feed` (5506), `.phone-messages-detail` (5507), `.msg-list` (5570), all `overflow-y:auto` — are **not** on the list, so they show the raw native bar. Every future scroll container has the same trap.

### Canonical fix — one global, theme-aware treatment
**Step A — declare the colour scheme** (`design-system.css` `:root`, ~line 49):
```css
:root {
  color-scheme: dark;                          /* native scrollbars + form controls render dark */
  /* optional named tokens (or reuse --text-low / --text-medium inline as today) */
  --scrollbar-thumb:        var(--text-low);       /* #4B5563 */
  --scrollbar-thumb-hover:  var(--text-medium);    /* #B5BCC9 */
  /* …existing tokens… */
}
```
`color-scheme: dark` alone kills the "bright" appearance even on any container we forget to style — the belt.

**Step B — replace the opt-in block (delete 1273–1304) with a global treatment** (§19). Keep the outer-chrome hide (it wins by specificity):
```css
/* Firefox — thin, theme-aware, on every scroll container */
* { scrollbar-width: thin; scrollbar-color: var(--scrollbar-thumb) transparent; }

/* Keep the outer kiosk chrome scrollbar-free (higher specificity → wins over *) */
html, body, #app { scrollbar-width: none; }
html::-webkit-scrollbar,
body::-webkit-scrollbar,
#app::-webkit-scrollbar { display: none; }

/* Chromium/WebKit — thin dark scrollbar for all inner scroll containers */
::-webkit-scrollbar { width: 8px; height: 8px; }          /* hit-box; see DEVIATES note on width */
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb {
  background: var(--scrollbar-thumb);
  border-radius: 4px;
  border: 2px solid transparent;      /* transparent inset → thumb reads ~4px, subtle */
  background-clip: padding-box;
}
::-webkit-scrollbar-thumb:hover {
  background: var(--scrollbar-thumb-hover);
  background-clip: padding-box;
}
::-webkit-scrollbar-corner { background: transparent; }
```
This automatically covers the phone feed containers, the queue, Radzen grids/tabviews, and anything added later — no per-container opt-in.

**Step C — reconcile the two remaining local scrollbar rules** so nothing overrides the canonical treatment at a higher specificity:
- `RadioControlPanel.razor` scoped `<style>` `.rcp-presets-list::-webkit-scrollbar` (861–868) — remove; let the global rule cover it (or confirm it now matches).
- `.topbar-sources` (205–207) and `.file-browser-breadcrumbs` (1388–1391) intentionally hide their scrollbars — **leave as-is**, they win by specificity.

> **If you keep 3px instead:** set `::-webkit-scrollbar { width:3px; height:3px }`, drop the thumb `border`, and keep `scrollbar-width: thin`. Everything else is identical.

### Related layout bug to fix in the same PR (the feed's bottom + scrollbar are half-hidden)
`.phone-messages { height: 600px }` (5501) is hard-coded to the **full** content area, but `/phone` shows the `NowPlayingDock`, so `.content-area.has-dock .page-transition` shrinks to `calc(100% - 64px)` = **536px** (2320). The 600px panel overflows by 64px — its last rows and the bottom of its scrollbar disappear under the dock. **Fix:** `.phone-messages { height: 100%; }` (fill the already-correctly-sized `.page-transition`). Verify no vertical clipping under the dock afterward.

---

## Issue 4 (P2) — Call-log / Messages row redesign

The bright-card bug is a *rendering* defect; separately the **row hierarchy is weak**. Concrete, token-only improvements below. This **EXTENDS** `HANDOFF-phone-messages-voicemail-sms.md` (unified feed, `vm-chip`, `phone-pill`, `unread-dot`) — it does not restyle those; it brings **call rows** up to the same standard and adds three cross-cutting affordances.

### What's wrong today (`RenderCallRow` 475–490)
```
[✔ small colored icon]  Name            [ 6/28/2026 4:43 PM ]  [ 0:50 ]  [ › ]
                        "Incoming"
```
- Direction is shown **twice** — a coloured icon *and* the subtitle word ("Incoming/Outgoing/Missed"). The subtitle slot (best used for the number) is wasted on redundant info.
- Timestamp is `.ToString("g")` → `6/28/2026 4:43 PM` — verbose, hard to scan at a glance.
- Call rows have a **bare floating icon** while voicemail rows have a 44px chip and text rows a pill → the unified feed's left edge has no consistent rhythm.
- Missed calls aren't emphasised; duration shows even for missed (`0:00` noise).
- No answered-on (Rotary vs GV) cue in the feed, though the Call History tab has it.

### Redesigned row anatomy (all three feed kinds share this spine)
```
┌────────────────────────────────────────────────────────────────────────────┐
│ [ 44px    ]   Caller Name                                4:43 PM            │  ← title --text-high / meta --text-medium (smart ts)
│ [ chip    ]   (908) 555-0142            ·  Rotary          0:50        ›     │  ← subtitle mono --text-medium / answered pill / duration
└────────────────────────────────────────────────────────────────────────────┘
   ▲ tinted by kind/direction        ▲ number or preview     ▲ pill   ▲ dur   ▲ chevron
```
- **Column 1 — 44px identity chip** (radius 10px, matching `.vm-chip` 5548). Tint by kind/direction, reusing the exact alpha `.vm-chip`/`.phone-pill` use (`color-mix(in srgb, <signal> 14%, transparent)` bg, `<signal>` fg). **No new colours.**
  - Incoming/answered → `--signal-green` chip, glyph `call_received`.
  - Outgoing → `--signal-blue` chip, glyph `call_made`.
  - Missed → `--signal-red` chip, glyph `call_missed`.
  - Voicemail → existing cyan `.vm-chip` (`voicemail`) — unchanged.
  - Text → promote today's small cyan pill to the same 44px chip (`chat_bubble`).
- **Column 2 — identity** (`flex:1`, `min-width:0`):
  - **Title** = caller name, `.list-item-title` (`--text-high`). Missed keeps `--text-high` (don't dim the name).
  - **Subtitle** = the *useful* second line: the **phone number** in `--font-mono` `--text-medium` when a name is known; if the number *is* the title (unknown caller), fall back to the direction word. For voicemail/text rows the subtitle stays their transcript/message preview (unchanged).
- **Column 3 — meta** (right, mono, `tabular-nums`):
  - **Top:** smart timestamp, `--text-medium` (see copy.md rules).
  - **Bottom:** for answered calls, duration `--text-low`; optionally an answered-on `.phone-pill` — **amber "Rotary"** / **cyan "GV"** (reuse 5282–5287). For **missed**, replace duration with **"Missed"** in `--signal-red` mono; drop the `0:00`.
- **Column 4 — chevron** `chevron_right`, `--text-low` (unchanged).

### New CSS (assembles existing tokens; add to §Ph)
```css
.feed-chip {
  width: 44px; height: 44px; border-radius: 10px; flex-shrink: 0;
  display: flex; align-items: center; justify-content: center;
}
.feed-chip--in     { background: color-mix(in srgb, var(--signal-green) 14%, transparent); color: var(--signal-green); }
.feed-chip--out    { background: color-mix(in srgb, var(--signal-blue)  14%, transparent); color: var(--signal-blue);  }
.feed-chip--missed { background: color-mix(in srgb, var(--signal-red)   14%, transparent); color: var(--signal-red);   }
/* --vm / --text reuse the existing cyan vm-chip recipe */

.list-item-meta-stack { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
.list-item-missed { color: var(--signal-red); font-family: var(--font-mono); font-size: 13px; }

/* Optional hairline for scannability at kiosk distance; removed on hover/active */
.phone-messages-feed .list-item-touch { border-bottom: 1px solid var(--surface-separator); }
.phone-messages-feed .list-item-touch:hover,
.phone-messages-feed .list-item-touch.list-item-active { border-bottom-color: transparent; }
```

### States, empty, loading, error
- **Hover / pressed / selected / focus-visible:** per Issue 1 (unchanged intent; focus ring is new).
- **Unread (texts/voicemail):** existing cyan `.unread-dot` (5562) — keep; ensure it aligns on the 44px spine (place before the chip or as a top-corner dot on the chip).
- **Empty:** keep the existing per-segment `.empty-state` blocks (PhoneMessagesPanel 56–59, 86–89, 116–121, 149–154) — they're good. Copy tweaks in copy.md.
- **Loading:** keep `RenderCallSkeleton` (466–473) but **add a 44px chip block to the skeleton row** so layout doesn't jump when real rows arrive.
- **Error:** keep the existing retry blocks (80–82, 110–113, 143–147) unchanged.

---

## Component / file pointers

| Concern | File | Lines |
|---|---|---|
| Near-white row root cause + fix | `src/Radio.Web/wwwroot/css/design-system.css` — `.list-item-touch` | 582–605 |
| Row markup (calls/texts) | `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` — `RenderCallRow`, `RenderTextThreadRow` | 475–490, 505–521 |
| Voicemail row | `src/Radio.Web/Components/Pages/VoicemailRow.razor` | — |
| Direction icon/colour + duration | `src/Radio.Web/Components/Pages/PhoneCallFormatting.cs` | 13–43 |
| Nav rail markup (PHONE/MESSAGES/MORE) | `src/Radio.Web/Components/Pages/PhonePage.razor` | 17–59 |
| Rail CSS + active indicator | `src/Radio.Web/wwwroot/css/design-system.css` — `.phone-tab-rail`, `.phone-rail-tab.active::before` | 4963–4970, 5005–5012 |
| Shell / dock geometry | `src/Radio.Web/Components/Layout/MainLayout.razor` (`.content-area`), `design-system.css` | 25, 188–196 / 544–549, 2320 |
| Scrollbar rules (opt-in → global) | `src/Radio.Web/wwwroot/css/design-system.css` §19 | 1263–1304 |
| Global `color-scheme` + tokens | `src/Radio.Web/wwwroot/css/design-system.css` `:root` | 49–138 |
| Local scrollbar to reconcile | `src/Radio.Web/Components/Shared/RadioControlPanel.razor` scoped `<style>` | 861–868 |
| Feed containers (auto-covered by global rule) | `design-system.css` `.phone-messages-feed`, `.phone-messages-detail`, `.msg-list` | 5506–5510, 5570–5575 |
| Messages panel height bug | `design-system.css` `.phone-messages` | 5501 |
| Theme link (Radzen) | `src/Radio.Web/Components/App.razor` | 19–26 |

---

## Copy (`copy.md`)

**Smart feed timestamp** (replaces `.ToString("g")`; implement as `PhoneCallFormatting.FormatFeedTimestamp(DateTime local)`, mono, `tabular-nums`, right-aligned, `--text-medium`):

| Age of item | Format | Example |
|---|---|---|
| Today | `h:mm tt` | `4:43 PM` |
| Yesterday | literal | `Yesterday` |
| Within last 7 days | `ddd` | `Mon` |
| Same calendar year | `MMM d` | `Jun 28` |
| Older | `M/d/yy` | `6/28/25` |

**Missed row caption:** `Missed` (in `--signal-red`), replacing the duration for `CallDirection.Incoming && AnsweredOn == NotAnswered`.

**Answered-on pills:** `Rotary` (amber) / `GV` (cyan) — only on answered calls; missed shows no pill.

**Empty states (keep existing icons; confirm wording):**
- All: `No messages yet.`
- Calls: `No recent calls.`
- Voicemail: `No voicemails.`
- Texts: `No conversations yet.`

**Error states (keep):** `Couldn't load messages.` / `Couldn't load voicemail.` / `Couldn't load conversations.` + `Retry`.

---

## Accessibility

- **Focus-visible ring** on every `.list-item-touch` (Issue 1) — currently missing; needed for keyboard / kiosk remote.
- **Direction is not colour-only:** the glyph (`call_received/made/missed`) carries the meaning alongside chip tint; add `aria-label` on the call row summarising direction + name + time (e.g. `Missed call from Jane Doe, Jun 28`).
- **`color-scheme: dark`** also fixes light-on-light native form controls elsewhere (date/select popups), improving contrast globally.
- Chip tints at 14% alpha are decorative; text contrast is carried by `--text-high`/`--text-medium` on `--surface-base` (both ≥ AA).
- Keep min-height 56px (existing) — comfortable touch target.

---

## Prioritized punch-list (for Planner to scope PRs)

**P1 — quick, high-impact dark-theme correctness (small CSS, no logic).** Suggest one PR "Phone dark-theme + scrollbar correctness":
- **P1a — `color-scheme: dark` on `:root`** + global thin scrollbar replacing the opt-in allowlist (Issue 3, Steps A/B). *Single biggest visual win; also fixes the bright scrollbar and any other native control.*
- **P1b — `.list-item-touch` button-chrome reset** (Issue 1) → kills the near-white rows.
- **P1c — Left rail safe-gutter + indicator reposition** (Issue 2) → nav no longer clipped. *Include the DevTools verification step.*
- **P1d — `.phone-messages { height: 100% }`** (Issue 3, related) → feed/scrollbar no longer hidden under the dock.
- **P1e — reconcile `RadioControlPanel` scoped scrollbar** (Issue 3, Step C).
- Add `.list-item-touch:focus-visible` ring here (cheap, ships with 1b).

**P2 — call-log / messages row redesign (Issue 4).** Suggest a second PR "Unified feed row polish":
- Shared 44px `.feed-chip` for call rows (+ promote text pill to 44px); smart `FormatFeedTimestamp`; missed emphasis + answered-on pills; skeleton chip parity; optional row hairline. Touches `PhoneMessagesPanel.razor` (`RenderCallRow`/`RenderTextThreadRow`), `PhoneCallFormatting.cs`, and §Ph CSS. Extends `HANDOFF-phone-messages-voicemail-sms.md`.

**Dependencies:** P2 sits *on top of* P1 (rows must be dark before the chip/hierarchy work reads correctly). Ship P1 first; it independently resolves all three reported bugs.

**No Architect involvement needed** — no data-model/API change. `FormatFeedTimestamp` is presentation formatting only.

---

## How to verify (Tester, at 1920×720 in Chrome on the kiosk)

1. `/phone` → Messages: rows are transparent over the dark bg; no near-white cards. Hover/selected/focus states read correctly.
2. Left rail: "PHONE / MESSAGES / MORE" fully visible with a left gutter; active-tab accent bar + glow fully on-screen (nothing at x<0).
3. Scroll the feed: scrollbar is thin and dark (thumb `--text-low`, hover `--text-medium`), track transparent — no bright native bar. Confirm the same on the queue, a Radzen grid, and the texts conversation pane.
4. Bottom of the feed is not hidden under the NowPlayingDock; last row + scrollbar end are reachable.
5. (P2) Call rows show a tinted 44px chip, number as subtitle, smart timestamp, "Missed" in red for missed calls, Rotary/GV pill on answered calls.
6. `dotnet build` clean; `dotnet test tests/Radio.Web.Tests` green.
