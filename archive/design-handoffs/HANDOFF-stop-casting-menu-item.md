# HANDOFF — Explicit "Stop Casting" menu item in Cast popover

**Component:** `src/Radio.Web/Components/Shared/CastDeviceDropdown.razor`
**Surface:** Topbar → "Cast" pill → popover
**Status:** `[PENDING REVIEW]` — ready for Builder
**Relationship to existing handoffs:**
- **Follows** `docs/design-handoffs/design_handoff_radio_console/` (token system, popover chrome, Radzen-icon vocabulary).
- **Extends** the existing popover with a new dedicated action row. No deviation from established visual language.
- **Path note:** Existing handoffs live under `docs/design-handoffs/`. This one was written to `design/design-handoffs/` per the explicit request; if Builder prefers, move to `docs/design-handoffs/` to keep the canonical location consistent.

---

## 1. Problem + context

When Cast is active, the only ways to stop casting today are (a) the small inline `Disconnect` Danger button tucked inside the green "connected device" banner at the top of the Cast popover, or (b) implicitly switching to a local output via the `Out` picker. Both are easy to miss: the inline button reads more like an "X" affordance on a banner than a primary action, and the Out-picker route never says the word "Cast." User feedback (2026-05-23) was specifically: "there should be an explicit 'Stop Casting' selection from the Cast menu to make this action explicit." The fix is a clearly-labelled list-style action row at the bottom of the device list — same visual weight as a device row, so the popover scans as "pick a device OR stop casting."

---

## 2. Visual mockup (ASCII)

### Before — Cast popover when a device is connected (today)

```
┌─────────────────────────────────────────┐
│ Cast Devices                        [↻] │  header (rescan)
├─────────────────────────────────────────┤
│ [cast_connected]  Living Room      [Disconnect] │  ← small Danger button (low discoverability)
├─────────────────────────────────────────┤
│ [speaker]  Living Room    Live          │  device list (selected row is opacity-0.5,
│ [tv]       Bedroom TV     Cached        │   pointer-events:none — see line 58)
│ [speaker]  Kitchen Hub    Cached        │
└─────────────────────────────────────────┘
```

### After — same popover with new "Stop Casting" row

```
┌─────────────────────────────────────────┐
│ Cast Devices                        [↻] │  header (rescan)
├─────────────────────────────────────────┤
│ [cast_connected]  Living Room           │  banner: device name only,
│                                         │  no inline Disconnect button (removed)
├─────────────────────────────────────────┤
│ [speaker]  Living Room    Live          │  active row stays dimmed (existing behavior)
│ [tv]       Bedroom TV     Cached        │
│ [speaker]  Kitchen Hub    Cached        │
├─────────────────────────────────────────┤ ← separator (border-top, same color
│ [cast_off]  Stop Casting                │   as inter-row dividers)
└─────────────────────────────────────────┘
```

### After — same popover when NO device is connected

```
┌─────────────────────────────────────────┐
│ Cast Devices                        [↻] │
├─────────────────────────────────────────┤
│ [speaker]  Living Room    Live          │  (no banner, no Stop Casting row —
│ [tv]       Bedroom TV     Cached        │   visibility rule hides it)
│ [speaker]  Kitchen Hub    Cached        │
└─────────────────────────────────────────┘
```

### After — disconnect in progress

```
┌─────────────────────────────────────────┐
│ Cast Devices                        [↻] │
├─────────────────────────────────────────┤
│ [cast_connected]  Living Room           │
├─────────────────────────────────────────┤
│ [speaker]  Living Room    Live          │  (rows still rendered but
│ [tv]       Bedroom TV     Cached        │   container has opacity:0.6,
│ [speaker]  Kitchen Hub    Cached        │   pointer-events:none)
├─────────────────────────────────────────┤
│ [○ spin]  Stopping…                     │  ← icon swaps to inline spinner,
└─────────────────────────────────────────┘   label changes, row is aria-busy
```

---

## 3. Interaction spec

### Visibility rule
- Render the row **iff** `_connectedDevice != null` (same condition that gates the existing connected-device banner at line 34).
- When no Cast device is connected, the row is omitted from the DOM entirely (not just hidden) so Tab order is clean.

### Click handler
- Wire `@onclick="StopCastingAsync"`. Reuse the existing `DisconnectAsync()` private method body — just rename to `StopCastingAsync` (more explicit) OR add a thin wrapper that calls `DisconnectAsync`. Internally it must:
  1. Set `_isDisconnecting = true` and `StateHasChanged()`.
  2. `await DevicesApi.DisconnectFromCastDeviceAsync()` (already routes to `DevicesController.DisconnectFromCastDevice` at `DevicesController.cs:714`, which after PR #409 goes through `SetActiveOutputAsync` and unmutes local output automatically).
  3. On success: `_connectedDevice = null`, invoke `OnDisconnect`, then `await Close()`.
  4. On exception: log, set `_disconnectError = true`, keep popover open, surface inline error (see Error state).
  5. `finally`: clear `_isDisconnecting`, `StateHasChanged()`.

### Loading state (during disconnect)
- The row's leading icon swaps from `cast_off` to a small inline `RadzenProgressBarCircular` (same `Size="ButtonSize.Small"` pattern used in the header rescan spinner at lines 22–24).
- Label text changes: `"Stop Casting"` → `"Stopping…"`.
- Row gets `aria-busy="true"` and `pointer-events: none` to block double-clicks.
- Device list above is dimmed (`opacity: 0.6; pointer-events: none`) so user can't initiate a conflicting connect-while-disconnecting.
- Header rescan button also disables during this window.

### Success state (post-disconnect)
- Popover closes (existing `Close()` helper). Topbar returns to its non-Cast state via the existing `OnCastDeviceDisconnected` handler in `MainLayout.razor:866` which clears the default Cast device and re-renders.
- No toast / no banner needed — the disappearance of the Cast pill's "connecting" dot + the topbar `Out` cluster reverting to the local output name is sufficient feedback.

### Error state (network failure during disconnect)
- Keep popover open.
- Replace the "Stop Casting" row content with a small inline error: `[error_outline] Couldn't stop. Try again.` using `color: var(--rz-danger)` (matches the existing inline Danger button color). The row stays clickable so the user can retry.
- After 5s of idle, auto-revert the row label back to `Stop Casting` so the popover doesn't get stuck in error state.
- Log via `Logger?.LogError` (matches existing `DisconnectAsync` catch at line 254).

### Keyboard nav
- The row is a `<button type="button">` (not a `<div>` — the existing device rows use `<div @onclick>` which is non-ideal but out of scope here; the new action row should be a real button so it lands in Tab order naturally).
- Tab order: header rescan → connected banner (no interactive child now) → each device row in order → **Stop Casting button** → click-away closes.
- `Enter` and `Space` activate the button (native button behavior).
- `Esc` while popover is open closes it (existing behavior; verify still works after the change).
- Focus ring: rely on Radzen / browser default focus outline; do not suppress.

---

## 4. Copy

| Element | Exact string | Notes |
|---|---|---|
| Default label | `Stop Casting` | Two words, title case. Matches Cast button vocabulary ("Cast", "Cast Devices"). |
| Loading label | `Stopping…` | Single ellipsis char `…` not three dots. |
| Error label | `Couldn't stop. Try again.` | Lowercase second sentence is intentional — matches conversational micro-copy elsewhere in the popover ("No Cast devices found", "Searching..."). |
| Tooltip (`title=`) | `Disconnect from Living Room` (interpolated with `_connectedDevice.Name`); fall back to `Disconnect from Cast device` if name is null/empty. |
| `aria-label` | Same as tooltip — `Disconnect from {device name}` or `Disconnect from Cast device`. |
| Confirmation prompt | **None.** Cast disconnect is non-destructive (audio routes back to local speakers immediately) and the user can reconnect with one click. |

---

## 5. Styling notes

- **Row container:** mirror the existing `.cast-device-row` pattern (lines 56–78) but distinguish as an action row:
  - `display: flex; align-items: center; gap: 8px; padding: 8px 12px;` (same as device rows but `padding: 8px 12px` not `6px 12px` to give the action slightly more visual weight).
  - `border-top: 1px solid rgba(255,255,255,0.1);` — separator above so it visually detaches from the device list. Same color as the header `border-bottom` at line 18 for consistency.
  - `background: rgba(0,0,0,0.2);` — subtle dark tint (matches the existing connecting-state strip at line 98) so the action zone reads as distinct from the device list.
  - Hover: `background: rgba(255,255,255,0.06);` — same hover as `.cast-device-row` so the family resemblance holds.
  - `cursor: pointer;`
- **Icon:** `cast_off` (Material Symbols name — confirmed available in the same set as `cast`, `cast_connected`). Size 18px, `color: var(--text-medium)`. Matches device-row icon sizing.
- **Label text:** `font-size: 12px; color: var(--text-high);` — same scale as device-row primary text.
- **Color choice — neutral, not danger:** Use `var(--text-high)` for the label, NOT `var(--rz-danger)`. Rationale: Stop Casting is non-destructive (audio simply re-routes locally) and is the standard "exit" affordance for the popover. Danger red would over-signal risk. The old inline `Disconnect` button used Danger because it was buried in a green banner and needed contrast; the new dedicated row gets that affordance from its position + separator instead.
- **Remove old inline button:** Delete the `<RadzenButton ... Text="Disconnect" />` inside the green banner (lines 42–44). The banner becomes display-only: icon + device name, no action. This is the discoverability fix — one canonical way to stop casting, not two competing ones.
- **No new CSS tokens.** Everything uses existing `--text-high`, `--text-medium`, `--text-low`, `--rz-danger` (error state only), and the same `rgba(...)` literals already in this file.

---

## 6. Accessibility

- Element: `<button type="button" class="cast-stop-row" aria-label="..." title="...">`.
- `aria-busy="true"` during the disconnect loading state.
- During loading, also set `aria-disabled="true"` (visual block via `pointer-events: none` is paired with semantic block).
- Focus visible by default (do not set `outline: none`).
- The icon should NOT be focusable/announced separately; it's decorative. Either omit aria entirely or set `aria-hidden="true"` on the `<RadzenIcon>`.
- Screen-reader flow when the popover opens with a device connected: "Cast Devices, rescan button, Living Room connected, [device list…], Disconnect from Living Room, button."
- Color contrast: `var(--text-high)` on `rgba(0,0,0,0.2)` over `rgba(30,30,30,0.95)` clears WCAG AA at 12px (existing pattern, no change).

---

## 7. Non-goals

Explicitly NOT in scope for this spec — flag any of these to the user before expanding:

1. **No confirmation modal.** Disconnect is one-click. (Aligns with feedback: user wants this MORE explicit / easier, not gated behind a second click.)
2. **No undo / toast.** Reconnecting is one click in the same popover; classic undo isn't needed.
3. **No multi-device disconnect.** Cast is single-device today; no group-cast logic.
4. **No new icon design.** Using stock `cast_off` Material glyph. If Material doesn't render this name in the current Radzen icon font, fall back to `stop_circle` or `power_settings_new`; Builder should verify visually after deploy.
5. **No topbar-level affordance.** The "Stop Casting" action lives only inside the popover. The Cast pill itself keeps its current single-click behavior (open popover).
6. **No keyboard shortcut.** Not adding `Ctrl+Shift+C` or similar; the existing popover Tab order is sufficient.
7. **No telemetry event.** If we later want a "stop_cast_explicit" metric to compare against implicit-via-Out-picker stops, that's a separate spec.
8. **Banner re-styling beyond removing the inline button.** The green banner stays — it still confirms "this is the device you're connected to." Only the inline `Disconnect` Radzen button inside it is removed.

---

## Hand-off summary for Planner / Builder

One new action row at the bottom of the `CastDeviceDropdown` popover device list, visible only when a device is connected, labeled `Stop Casting`, neutral color, icon `cast_off`, calls the existing `DisconnectAsync` flow. The old inline `Disconnect` Danger button inside the green banner gets removed in the same change so there's exactly one canonical way to stop casting. No new tokens, no new API, no new component — single-file edit to `CastDeviceDropdown.razor` plus possibly a rename of the private method for clarity.
