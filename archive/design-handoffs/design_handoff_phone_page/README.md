# Handoff: Phone Page Redesign

## Overview

The `/phone` page in Radio.Web is the only page in the kiosk that does not match the rest of the
app's "Command Surface" design language. This handoff redesigns that page from scratch — and adds
a few opportunistic consistency fixes to neighbouring config pages — so the Phone surface speaks
the same visual vocabulary as Home, System, Devices, and Bluetooth.

The redesign:

1. Replaces the top-tab layout with a **left-rail tab pattern** (Dashboard · Contacts · Call History),
   matching `SystemConfigPage.razor` and `DeviceManagementPage.razor`.
2. Introduces a new **Phone Status Hero** block on the Dashboard tab — a large LED-style state word
   (IDLE / RINGING / INCALL / DIALING) coloured by state, with caller info and contextual action
   buttons (Answer / Reject / Hang Up / Move-to-Soundbar / Cancel / New Call). This is the one
   "novel pattern" introduced; it is deliberately the focal point of the screen.
3. Compresses **System Status** from a wide bar into a 4-row compact card on the right column.
4. Reworks **Call Path** so connectors (Chrome Extension, SIP Trunk) render as icon · name · sub ·
   pill rows instead of mixed buttons + badges + inline labels.
5. Moves **Dev Controls** into a collapsible "Dev Tray" at the bottom of the Dashboard — visible
   but not occupying half the screen by default.
6. Rebuilds **Contacts** with a real two-column layout (list + detail rail) and a proper search /
   sync toolbar.
7. Rebuilds **Call History** with a filter chip row (All · Incoming · Outgoing · Missed) and a
   stats rail on the right.

Everything fits the kiosk's fixed `600px` content area (1920 × 720 viewport, 120px top bar) — the
current page is clipped at the bottom; the redesigned page is not.

## About the Design Files

The files in this bundle are **design references created in HTML/React** — a clickable prototype
showing the intended look and behaviour. **They are not production code to copy directly.**

The Radio Console UI is **Blazor Server + Radzen Blazor + the existing `design-system.css`**.
The implementation task is to recreate the design *in that environment*, reusing existing tokens,
existing Razor components, and existing Radzen components wherever they exist. The HTML/JSX in the
prototype is a pixel reference, not a port target.

When the prototype uses an inline SVG icon, use the corresponding Radzen Material icon name
(see "Icon mapping" below). When it uses a custom CSS class, prefer an existing class from
`design-system.css` first — only add new tokens if no equivalent exists.

## Fidelity

**High-fidelity.** Every colour, typography choice, and spacing value in the prototype is taken
directly from the existing `src/Radio.Web/wwwroot/css/design-system.css`. Recreate the UI
pixel-perfectly using Radzen components and the existing token set — do not invent new tokens
unless this document explicitly asks you to.

## What to read first

In this order:

1. `Phone Page Redesign.html` — open in a browser. Toggle Tweaks in the toolbar to cycle the four
   call states (Idle / Ringing / InCall / Dialing) and to flip between left-rail and top-tab
   variants. The left-rail variant is the canonical one to implement.
2. `IMPLEMENTATION.md` (next to this README) — section-by-section, file-by-file change script.
3. `styles.css` — the prototype's CSS. About 90% of this is **already in `design-system.css`**;
   the IMPLEMENTATION script identifies the genuinely new selectors (the `.hero`, `.dev-drawer`,
   `.mode-selector` blocks).
4. `phone-dashboard.jsx`, `phone-contacts.jsx`, `phone-history.jsx` — the three tab panels.
5. `app.jsx`, `topbar.jsx`, `data.jsx`, `tweaks-panel.jsx` — supporting scaffolding the
   prototype uses to render the shell. The Razor implementation does NOT need to touch the
   topbar; it already exists in `MainLayout.razor` and matches the prototype.

## Screens / Views

### 1. Phone — Dashboard tab (default)

**Purpose:** Show the current call state, the system's connectivity to its three call paths, and
(folded away) the developer-simulation controls.

**Layout (within the 600px content area):**

```
┌── 156px ──┬─────────────── 1.5fr ───────────────┬───── 1fr ─────┐
│           │                                     │ System Status │
│           │     PHONE STATUS HERO               │   (compact)   │
│  Tab Rail │     (1fr, full left col, row 1)     ├───────────────┤
│           │                                     │   Call Path   │
│           │                                     │   (mode +     │
│           ├─────────────────────────────────────┤    GV + SIP)  │
│           │  ⚙  DEV TRAY (collapsed, ~44px)     │               │
│           │   or expanded (~240px)              │               │
└───────────┴─────────────────────────────────────┴───────────────┘
```

- Tab rail: `156px` wide, dark inset background, three tabs (Dashboard · Contacts · Call History).
  Active tab gets a 3px cyan left-side bar with a glow.
- Right column (System Status + Call Path) spans both grid rows so the Dev Tray only steals
  height from the Hero.
- Dashboard padding: `14px`, gap: `12px`.

**Components:**

- **Phone Status Hero** — `.card`-style raised surface, 8px radius. Header row: `cluster-label`
  state name ("Awaiting Call" / "Incoming Call" / "Active Call" / "Dialing Out") on the left, a
  small pulsing dot + "via Rotary Phone" or "via Bluetooth HFP" on the right. Below: the state
  word in `--font-led` Orbitron-Bold at **96px** with 8px letter-spacing, colour-bound to the
  state (see colour table below). Below that: caller block — 44×44 rounded icon chip + phone
  number in `--font-mono` 28px + caller name in body 14px + an amber LED duration on the right
  (visible only during InCall). Bottom row: action buttons (see action button matrix).

  - Ringing state: the state word pulses with `ring-pulse` keyframes (`1.2s` ease-in-out
    infinite alternate, scale 1 → 1.04).
  - Idle state: the state word renders in `--text-medium` and the caller block is replaced with
    an empty-state hint ("Lift the handset to place a call, or wait for an incoming ring.").

- **System Status card** — `.card.accent-green` (3px green left border). Title row in
  `--font-mono` 11px / 0.16em / uppercase, with a green "Healthy" pill on the right. Then 4
  status rows, each in a `.surface-inset` row with a grid layout: `[90px label][1fr value][auto pill]`.

- **Call Path card** — `.card.accent-cyan` (3px cyan left border). Three pieces:
  1. "Active Mode" header + a segmented 3-button selector (Bluetooth / SIP Trunk / GV Browser).
     Active segment: `--accent-dim` background + `--accent-primary` text + 1px inset accent border.
  2. Chrome Extension connector row (globe icon · "Chrome Extension" · "v1.4.0 · GV Bridge"
     sub-line · "Connected" pill).
  3. SIP Trunk connector row (sip icon · "SIP Trunk" · "voip.ms · 5060" sub-line · status pill
     + "Re-register" link button when unregistered).

- **Dev Tray** — `.card.accent-amber` (3px amber left border). Header is `44px` tall, click to
  expand. Header text in amber `--font-mono` 11px / 0.16em / uppercase: "Dev Tray · Simulate
  Hardware Events". Right side: small dot-pulse when collapsed + "Click to expand/collapse" +
  caret. Body: 3-column grid (Handset · Network · Dialer) of simulate buttons. Default state:
  **collapsed**.

### 2. Phone — Contacts tab

**Purpose:** Show merged contacts (manual + PBAP-synced), with sync controls and per-contact
detail.

**Layout:** `1fr 320px` grid, 12px gap, 14px padding.

**Left column — Contacts list card:**
- Toolbar: search input (`.search`, 280px, with magnifying-glass icon at left) + "12 of 12"
  result count (mono, low contrast) on the left; "Sync from Phone" + "Add Contact" buttons on
  the right.
- Column header row (`.col-headers`): mono uppercase headers — Name · Phone · Source · Actions.
- Scrollable rows (`.contact-row`): `36px avatar` · `1fr name + email sub` · `160px phone` ·
  `90px source pill` · `110px actions`. Selected row gets a 3px cyan left border + accent
  background. Per-contact actions: Call (always), Edit + Delete (manual contacts only). PBAP
  contacts show only the Call action — they are read-only.

**Right rail:**
- **PBAP Sync card** — title in blue, "Fresh" / "Stale" pill, device strip (phone icon · device
  name · "{n} of {total} · {time ago}"), "Sync Now" button.
- **Contact detail card** — 64px circular avatar with initials, name (18px / 600), then a stack
  of `[60px label][1fr value]` rows for Phone / Email / Source. Footer: "Call" button (full
  width, green) + Edit icon-button (manual contacts only). Empty state when no contact selected.

### 3. Phone — Call History tab

**Purpose:** Browse the last N calls with filter chips and aggregate stats.

**Layout:** `1fr 320px` grid (same as Contacts), 12px gap, 14px padding.

**Left column — History list card:**
- Filter row: pill row `All · Incoming · Outgoing · Missed`, each pill shows the filter name +
  `· {count}` in dim text. Active pill: `--accent-dim` background, cyan text, cyan border. On
  the right: "Clear History" `.btn` with trash icon.
- Column header row: Caller · Number · When · Answered · Duration.
- Rows: `[32px direction-icon][1fr caller][140px number][100px when][80px answered-on pill][80px duration]`.
  Direction icon is colour-coded — incoming green, outgoing blue, missed red. Sub-text under
  the caller name shows "Incoming/Outgoing/Missed" in mono 10px. The Answered column shows a
  "Rotary" (amber pill) or "GV" (cyan pill) for answered calls; missed shows `—` in a gray pill.

**Right rail (stats):**
- Total Calls · 30 days — LED-style 32px amber number, sub-line "{n} in · {n} out · {n} missed".
- Missed — LED-style 32px red number, sub-line "2 today · 1 yesterday".
- Top Caller card (`.card.accent-amber`) — 40px avatar + name + "4 calls · 38 min total".

## Interactions & Behavior

| Event | Trigger | Result |
|---|---|---|
| Click tab in rail | Anywhere on the rail-tab | Switch the active tab; preserve scroll position per tab. |
| Hover rail tab (inactive) | Pointer enter | Background fades to `surface-hover` (`rgba(255,255,255,0.05)`). |
| Click dashboard mode segment | Mouse / touch up on segment | Call `GvBridgeApi.SetAdapterModeAsync(mode)`; optimistic flip of the active segment. Re-issue if it fails. |
| Click Answer (Ringing) | Pointer up | Call PhoneApi answer endpoint; transition the hero to InCall state (server SignalR will confirm). |
| Click Reject / Hang Up | Pointer up | Call PhoneApi hangup endpoint; transition back to Idle. |
| Click Dev Tray header | Anywhere on header | Toggle collapsed/expanded with a `max-height` transition (`200ms ease`). |
| Click "Lift" / "Drop" / "Incoming Call" simulate | Pointer up | Call existing `PhoneApi.SimulateHookAsync` / `SimulateIncomingCallAsync`. |
| Dial input + Dial button | Enter or click | Call `PhoneApi.SimulateDialAsync(digits)`, then clear the input. |
| Click contact row | Pointer up on row | Set as selected — populate the right detail panel. |
| Edit contact (icon) | Pointer up | Open existing edit dialog (no UI change here — wire to current handler). |
| Delete contact (icon) | Pointer up | Open existing confirmation, then call delete. |
| Click history filter pill | Pointer up | Filter the list client-side; re-compute the counts. |
| Sync from Phone | Pointer up | Call existing PBAP sync flow; show a spinner on the button while in-flight. |

### State transitions for the Hero

```
        ┌──── Lift (off-hook) ────┐
        ▼                         │
       Idle ─── Incoming call ──▶ Ringing
        ▲                         │
        │                         │
        │ Reject / Drop            │ Answer
        │                         ▼
        └────── Hang up ────── InCall
        ▲
        │
        └──── Cancel ── Dialing ◀── Lift + dial digits
```

The Razor `PhonePage.razor` already has the state machine — see the `_callState` object and the
SignalR `CallStateChanged` handler. **Do not rewrite the state logic** — only re-skin its consumer.

### Animations

| Element | Animation | Duration / Easing |
|---|---|---|
| Hero "RINGING" word | `ring-pulse` — opacity 0.45 ↔ 1, scale 1 ↔ 1.04 | `1.2s` ease-in-out infinite alternate |
| Dev Tray collapsed dot | `dot-pulse` — opacity 1 ↔ 0.35 | `1.4s` ease-in-out infinite |
| Dev Tray collapse/expand | `max-height` transition | `200ms` ease |
| Hero background glow | Radial gradient, colour from state — opacity 0.5 | `300ms` ease on state change |
| All button press | `transform: scale(0.96)` on `:active` | `80ms` ease |

## State Management

No new client state. Reuse the existing `PhonePage.razor` fields:

- `_callState` (`PhoneCallStateDto`) — drives the Hero.
- `_systemStatus` (`PhoneSystemStatusDto`) — drives the System Status card.
- `_gvActiveMode`, `_gvBridgeConnected`, `_gvExtensionVersion`, `_gvTrunkRegistered` — drive
  the Call Path card.
- `_contacts`, `_pbapContacts`, `_callHistory`, `_btStatus`, `_pbapStatus` — drive the Contacts
  and History tabs.
- `_dialDigits`, `_isSyncing`, `_showSyncDropdown` — already exist for dev controls.
- **NEW**: a single `bool _devTrayExpanded` (default `false`) to control the dev drawer.
- **NEW**: a single `string _activeTab` (default `"dashboard"`) to drive the left rail.

The SignalR subscription pattern (`PhoneHub`, `GvBridgeHub`, `GvTrunkHub`) stays exactly as it
is. Only the markup that renders these values changes.

## Design Tokens

All values pulled from `src/Radio.Web/wwwroot/css/design-system.css`. **No new colours.**

### Existing tokens used as-is

| Token | Value | Used for |
|---|---|---|
| `--surface-base` | `#0D0D0F` | Page background |
| `--surface-raised` | `#141416` | Cards |
| `--surface-inset` | `#0A0A0C` | Card-internal rows, tab-rail bg |
| `--surface-overlay` | `#1A1A1D` | Hover lift |
| `--surface-elevated` | `#1C1C1F` | Icon chips, buttons |
| `--surface-separator` | `#1F1F22` | Borders, dividers |
| `--accent-primary` | `#5CD4E8` | Cyan accent, tab actives, focus |
| `--accent-dim` | `rgba(92,212,232,0.06)` | Active tab background |
| `--signal-amber` | `#F0A830` | LED values, dev tray accent, Rotary pill |
| `--signal-green` | `#4ADE80` | Healthy / connected / Answer button |
| `--signal-red` | `#F87171` | Reject / Hang Up / Missed |
| `--signal-blue` | `#60A5FA` | Outgoing / Dialing / PBAP source |
| `--text-high` | `#F0EFF4` | Primary text |
| `--text-medium` | `#B5BCC9` | Secondary text |
| `--text-low` | `#4B5563` | Labels / chrome |
| `--font-led` | Orbitron stack | Hero state, LED stats, duration |
| `--font-mono` | JetBrains Mono stack | Labels, phone numbers, pills |
| `--font-body` | Inter stack | Body |
| `--sp-3` / `--sp-4` | `12px` / `16px` | Card padding, gaps |

### New selectors to add to `design-system.css`

These do not introduce new colours or new font stacks — they only assemble existing tokens into
phone-specific layouts. Add as a new `§28 Phone` section at the bottom of `design-system.css`:

- `.phone-shell` — left-rail grid for the phone surface.
- `.tab-rail`, `.rail-tab`, `.rail-tab.active` — left-rail tabs (also reusable on
  System/Devices pages once they migrate, but **out of scope here**).
- `.hero`, `.hero-glow`, `.hero-state`, `.hero-meta`, `.hero-actions`, `.hero-empty`,
  `.phone-btn`, `.phone-btn.btn-answer/.btn-hangup/.btn-ghost`.
- `.dev-drawer`, `.dev-drawer.collapsed/.expanded`, `.dev-header`, `.dev-body`, `.dev-section`,
  `.dev-label`, `.dev-buttons`.
- `.mode-selector`, `.mode-btn`, `.mode-btn.active`.
- `.connector-row`, `.conn-icon`, `.conn-name`.
- `.status-list`, `.status-row` — generalise: also useful on Bluetooth and System pages.
- `.pill` variants (green / red / amber / blue / cyan / gray) — generalise: many of these
  duplicate `RadzenBadge` styles; prefer the Radzen badge with `IsPill="true"` where the
  semantics line up; only fall back to `.pill` for the very compact mono variant the design
  uses for table cells.

The exact CSS is in `styles.css` next to this README. Most of it can be lifted verbatim —
just rename the file destination (see IMPLEMENTATION.md).

## Icon mapping (prototype SVG → Radzen Material name)

The prototype draws inline SVG paths. In Razor, use `<RadzenIcon Icon="..." />` with these names:

| Prototype | Radzen Icon |
|---|---|
| `home`        | `home` |
| `queue`       | `queue_music` |
| `metrics`     | `dashboard` |
| `devices`     | `devices` |
| `history`     | `history` |
| `settings`    | `settings` |
| `phone`       | `phone` |
| `phoneIn`     | `call_received` |
| `phoneTalk`   | `phone_in_talk` |
| `phoneOff`    | `call_end` |
| `ringVolume`  | `ring_volume` |
| `dialpad`     | `dialpad` |
| `bluetooth`   | `bluetooth` |
| `sip`         | `settings_phone` |
| `globe`       | `language` |
| `search`      | `search` |
| `add`         | `person_add` (Contacts) or `add` (generic) |
| `refresh`     | `refresh` |
| `edit`        | `edit` |
| `trash`       | `delete` |
| `contacts`    | `contacts` |
| `smartphone`  | `smartphone` |
| `dashboard`   | `dashboard` |
| `callIn`      | `call_received` |
| `callOut`     | `call_made` |
| `callMiss`    | `call_missed` |
| `chevron`     | `chevron_right` |
| `speaker`     | `speaker` |
| `mic`         | `mic` |
| `swap`        | `swap_horiz` |
| `caret`       | `expand_more` |
| `sleep`       | `power_settings_new` |

## Assets

None. No new images, no new fonts. The prototype loads Orbitron from Google Fonts; the live app
already loads it via `design-system.css`.

## Out of scope

The user asked for "light touches on the other config pages where consistency is obviously
broken." After auditing:

- **System Config / Devices pages** are already on left-rail tabs and align with the design
  system. The handoff doc at `docs/design-handoffs/design_handoff_radio_console/IMPLEMENTATION.md`
  already covers their type and pill cleanup in P0·2 and P0·3. **Do not duplicate that work
  here.** If those P0s have not landed yet, land them as part of their own PR.
- **Bluetooth page** uses a 50/50 panel split that works for its information density. Leaving
  it alone for this PR.

If you finish the Phone work and want to push consistency further, the next-best target is
generalising the `.status-row` / `.pill` / `.connector-row` blocks into shared CSS and reusing
them in `BluetoothPage.razor`'s "Adapter Status" and "Connected Device" cards. Coordinate with
the user before doing this.

## Files in this handoff

```
design_handoff_phone_page/
├── README.md                 — this file
├── IMPLEMENTATION.md         — Razor-file-by-file change script
├── PROMPT.md                 — copy-paste prompt for Claude Code CLI
├── Phone Page Redesign.html  — open in a browser, this is the source of truth
├── styles.css                — prototype CSS (mostly already in design-system.css)
├── data.jsx                  — mock data + inline-SVG icon dictionary
├── topbar.jsx                — prototype topbar (do not port; chrome already exists)
├── phone-dashboard.jsx       — Dashboard tab — Hero + System Status + Call Path + Dev Tray
├── phone-contacts.jsx        — Contacts tab — list + detail rail
├── phone-history.jsx         — Call History tab — list + stats rail
├── app.jsx                   — prototype shell + Tweaks panel
├── tweaks-panel.jsx          — starter component, not used in production
└── screenshots/              — 1920×720 PNG captures of every state (see screenshots/README.md)
    ├── 01-dashboard-idle.png
    ├── 02-dashboard-ringing.png
    ├── 03-dashboard-incall.png
    ├── 04-dashboard-dialing.png
    ├── 05-dashboard-dev-tray-expanded.png
    ├── 06-contacts.png
    └── 07-history.png
```

## How to verify

After implementation:

1. `dotnet build` clean.
2. `dotnet test tests/Radio.Web.Tests` green.
3. Load `http://radio:5002/phone` at 1920×720 and verify against the prototype side-by-side.
   No vertical scrollbars on Dashboard / Contacts / Call History at default state.
4. Cycle through all four call states (Idle / Ringing / InCall / Dialing) using the dev tray's
   simulate buttons — Hero colour and action buttons should flip per the prototype.
5. Switch Active Mode (Bluetooth / SIP Trunk / GV Browser) — segmented selector animates and the
   server logs an `AdapterModeChanged` event.
6. Sync contacts from a paired device — sync card pill flips Fresh, contact rows show the
   PBAP badge.
