# Implementation Script — Phone Page Redesign

> Land changes in the order given. Each section maps one design region to one set of file
> changes. Sections are independent enough that you can stop after any one and have a
> shippable PR — but the full redesign assumes all of them land together.
>
> Tokens to use are in `src/Radio.Web/wwwroot/css/design-system.css`. Do not invent new
> colours, fonts, or spacing — see the "Design Tokens" section of `README.md`.

---

## P0·1 — Add the new selectors to `design-system.css`

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/wwwroot/css/design-system.css`

**Steps:**

1. Append a new section at the bottom of `design-system.css`:

   ```css
   /* ─── §28  Phone Page Surface ───────────────────────────────────── */
   ```

2. Copy the following selectors **verbatim** from `design_handoff_phone_page/styles.css`,
   preserving order and comments:
   - `.phone-shell` (left-rail grid)
   - `.tab-rail`, `.rail-heading`, `.rail-tab`, `.rail-tab:hover`, `.rail-tab.active`,
     `.rail-tab.active::before`
   - `.top-tabs`, `.top-tab` — optional top-tab variant; only include if you want the
     consistency Tweak to be reachable from Razor (it isn't required for this PR).
   - `.tab-pane`
   - `.dashboard` (grid)
   - `.card`, `.card.accent-green/.accent-cyan/.accent-amber/.accent-blue`, `.card-title`,
     `.card-title.title-green/.title-cyan/.title-amber/.title-blue` — **but first check
     `§9 Content Area & Panel Layout`**; if `.panel-header` already satisfies the
     `.card-title` semantics, alias instead of duplicate.
   - `.hero` + all `.hero-*` children + `.phone-btn` + variants
   - `.status-list`, `.status-row` (left/value/pill grid)
   - `.pill` + `.pill.green/.red/.amber/.blue/.cyan/.gray`
   - `.callpath`, `.callpath-row`, `.mode-selector`, `.mode-btn`, `.mode-btn.active`,
     `.connector-row`, `.conn-icon`, `.conn-name`, `.right-cluster`, `.link-btn`
   - `.dev-drawer`, `.dev-drawer.collapsed/.expanded`, `.dev-header`, `.dev-body`,
     `.dev-section`, `.dev-label`, `.dev-buttons`
   - `.btn`, `.btn.btn-success/.btn-danger/.btn-warn`, `.input`
   - `@keyframes ringPulse`, `.ring-pulse`, `@keyframes dotPulse`, `.dot-pulse`

3. **Replace** references to the prototype-only `--font-led-light`, `--surface-elevated`, and
   `--surface-hover` if they don't already exist — they do (added in PR 2). Verify by
   grepping `design-system.css`.

4. **Do not duplicate** these prototype rules — they already exist:
   - `.cluster`, `.cluster-label`, `.cluster-value`, `.cluster-swatch` (§5)
   - `.nav-pill`, `.nav-badge` (§7)
   - All `--surface-*`, `--source-*`, `--signal-*`, `--text-*` tokens (§2)
   - `--font-led`, `--font-mono`, `--font-display`, `--font-body` (§2)

**Acceptance:**
- [ ] `design-system.css` grows by ~400 lines, no duplicate selectors.
- [ ] Visual regression run shows no change on Home / Devices / Bluetooth pages.

---

## P0·2 — Restructure `PhonePage.razor` into the left-rail shell

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/Components/Pages/PhonePage.razor`
- `src/Radio.Web/Components/Pages/PhonePage.razor.css`

**Steps:**

1. Delete the entire `<RadzenTabs class="phone-tabs">` block. The phone page no longer uses
   Radzen tabs.

2. Replace the page-level wrapper:

   ```razor
   <div class="phone-shell">
     <div class="tab-rail">
       <span class="rail-heading">Phone</span>
       <button type="button" class="rail-tab @(IsTab("dashboard") ? "active" : "")"
               @onclick="@(() => _activeTab = "dashboard")">
         <RadzenIcon Icon="dashboard" />
         <span class="rail-label">Dashboard</span>
       </button>
       <button type="button" class="rail-tab @(IsTab("contacts") ? "active" : "")"
               @onclick="@(() => _activeTab = "contacts")">
         <RadzenIcon Icon="contacts" />
         <span class="rail-label">Contacts</span>
       </button>
       <button type="button" class="rail-tab @(IsTab("history") ? "active" : "")"
               @onclick="@(() => _activeTab = "history")">
         <RadzenIcon Icon="history" />
         <span class="rail-label">Call History</span>
       </button>
     </div>
     <div style="overflow: hidden; min-height: 0;">
       @if (_activeTab == "dashboard") { <PhoneDashboardPanel ... /> }
       else if (_activeTab == "contacts") { <PhoneContactsPanel ... /> }
       else if (_activeTab == "history") { <PhoneHistoryPanel ... /> }
     </div>
   </div>
   ```

3. Add the new state field at the top of `@code`:
   ```csharp
   private string _activeTab = "dashboard";
   private bool _devTrayExpanded = false;
   private bool IsTab(string t) => _activeTab == t;
   ```

4. Delete the obsolete `_selectedTab` int.

5. **Delete from `PhonePage.razor.css`** everything that is now in `design-system.css §28`
   (all the `::deep` rules currently in the file). Keep only the top-level `.phone-page`
   selector if there's any page-specific override left — there shouldn't be.

6. Decide whether to extract the three tab panels into separate `.razor` files now or inline
   them. **Recommendation: extract** — `PhonePage.razor` becomes a 60-line shell, and the
   three panels become independently testable components.

**Acceptance:**
- [ ] `PhonePage.razor` is < 100 lines (everything else moved to children).
- [ ] No `::deep` selectors remain in `PhonePage.razor.css`.
- [ ] Tab switching is instant and preserves the SignalR subscriptions in `OnInitializedAsync`
      (they are page-level, not tab-level).

---

## P0·3 — Build the Phone Status Hero

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/Components/Shared/PhoneStatusHero.razor` (new)

**Steps:**

1. Create the component with these parameters:
   ```csharp
   [Parameter] public PhoneCallStateDto CallState { get; set; } = new();
   [Parameter] public string CallSource { get; set; } = "Rotary Phone";  // or "Bluetooth HFP"
   [Parameter] public EventCallback OnAnswer { get; set; }
   [Parameter] public EventCallback OnHangup { get; set; }
   [Parameter] public EventCallback OnDial { get; set; }
   ```

2. Compute the state colour:
   ```csharp
   private (string color, string glow, string glowBg) StateColors() => CallState.CallState switch
   {
     "Ringing" => ("var(--signal-amber)", "rgba(240,168,48,0.50)", "rgba(240,168,48,0.15)"),
     "InCall"  => ("var(--signal-green)", "rgba(74,222,128,0.45)", "rgba(74,222,128,0.12)"),
     "Dialing" => ("var(--signal-blue)",  "rgba(96,165,250,0.45)", "rgba(96,165,250,0.12)"),
     _         => ("var(--text-medium)",  "transparent",            "transparent"),
   };
   ```

3. Layout — match `phone-dashboard.jsx`'s `PhoneStatusHero` component (lines ~115–230):
   - `.hero` wrapper with `--hero-state-color / --hero-state-glow / --hero-glow-color` set
     inline from `StateColors()`.
   - `.hero-glow` background div.
   - `.hero-body` containing:
     - `.hero-top` with state label + source tag.
     - `.hero-state` with the call state in upper-case. Add `ring-pulse` class when
       `CallState.CallState == "Ringing"`.
     - `.hero-meta` when there is a number, otherwise `.hero-empty`.
     - `.hero-actions` with state-contextual buttons.

4. Map the action buttons by state — copy the conditional rendering from the prototype:
   | State | Buttons |
   |---|---|
   | Ringing | Answer (green) / Reject (red) / Silence (ghost) |
   | InCall  | Hang Up (red) / Mute (ghost) / Keypad (ghost) / Move to Soundbar (ghost) |
   | Dialing | Cancel (red) / Speaker (ghost) |
   | Idle    | New Call (default) / Pick Contact (ghost) |

5. Wire the Answer / Hang Up / Cancel buttons to `PhoneApi.AnswerAsync`, `HangupAsync`, etc.
   The existing `PhonePage.razor` does not have these — extend `PhoneApiService` if needed.
   **If the API surface is missing, gate behind an `EventCallback` parameter for now and TODO
   the API addition.**

6. Mount in `PhoneDashboardPanel.razor`:
   ```razor
   <PhoneStatusHero CallState="@_callState"
                    CallSource="@(IsBluetoothActive ? "Bluetooth HFP" : "Rotary Phone")"
                    OnAnswer="@AnswerCallAsync"
                    OnHangup="@HangupCallAsync"
                    OnDial="@OpenDialerAsync" />
   ```

**Acceptance:**
- [ ] Cycling through `Idle → Ringing → InCall → Dialing` via the dev tray flips the Hero
      colour and the action button set.
- [ ] Ringing pulses; other states are static.
- [ ] Duration shows during `InCall` only, formatted via `Durations.FormatTrack(...)` from
      the existing `Radio.Web.Formatting` namespace.

---

## P0·4 — Build the Dashboard's right column (System Status + Call Path)

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/Components/Shared/PhoneSystemStatusCard.razor` (new)
- `src/Radio.Web/Components/Shared/PhoneCallPathCard.razor` (new)

**Steps:**

1. **`PhoneSystemStatusCard.razor`** — parameter `PhoneSystemStatusDto Status`. Render a
   `.card.accent-green` with title row + 4 `.status-row` rows (Platform / Bluetooth / SIP
   Device / HT801 ATA). Reference: `phone-dashboard.jsx`'s `SystemStatusCard`.

2. **`PhoneCallPathCard.razor`** — parameters:
   ```csharp
   [Parameter] public string ActiveMode { get; set; } = "";
   [Parameter] public bool BridgeConnected { get; set; }
   [Parameter] public string? ExtensionVersion { get; set; }
   [Parameter] public bool TrunkRegistered { get; set; }
   [Parameter] public EventCallback<string> OnSwitchMode { get; set; }
   [Parameter] public EventCallback OnReregisterTrunk { get; set; }
   ```
   Render a `.card.accent-cyan` with:
   - `.callpath-row` containing "Active Mode" label + `.mode-selector` segmented control.
   - Two `.connector-row` blocks (Chrome Extension, SIP Trunk).
   Reference: `phone-dashboard.jsx`'s `CallPathCard`.

3. Mount both in `PhoneDashboardPanel.razor` inside the right column:
   ```razor
   <div style="display: flex; flex-direction: column; gap: 12px; grid-column: 2;
               grid-row: 1 / 3; min-height: 0; overflow: hidden;">
     <PhoneSystemStatusCard Status="@_systemStatus" />
     <PhoneCallPathCard ActiveMode="@_gvActiveMode"
                        BridgeConnected="@_gvBridgeConnected"
                        ExtensionVersion="@_gvExtensionVersion"
                        TrunkRegistered="@_gvTrunkRegistered"
                        OnSwitchMode="@SwitchModeAsync"
                        OnReregisterTrunk="@ReregisterTrunkAsync" />
   </div>
   ```

**Acceptance:**
- [ ] System Status compact card replaces the wide bar; all four indicators visible without
      truncation.
- [ ] Mode selector segmented buttons swap active state on click and persist via
      `GvBridgeApi.SetAdapterModeAsync`.
- [ ] Pill styles consistent with `.pill` definitions (mono caps + leading dot).

---

## P0·5 — Build the Dev Tray

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/Components/Shared/PhoneDevTray.razor` (new)

**Steps:**

1. Parameters:
   ```csharp
   [Parameter] public bool Expanded { get; set; }
   [Parameter] public EventCallback<bool> ExpandedChanged { get; set; }
   [Parameter] public string CallState { get; set; } = "Idle";
   [Parameter] public EventCallback<bool> OnSimulateHook { get; set; }     // true = lift
   [Parameter] public EventCallback OnSimulateIncoming { get; set; }
   [Parameter] public EventCallback<string> OnSimulateDial { get; set; }
   ```

2. Render `.dev-drawer.collapsed` or `.dev-drawer.expanded` based on `Expanded`. Header is
   always rendered; body only when expanded. Reference: `phone-dashboard.jsx`'s `DevDrawer`.

3. Keep an `[ if (Expanded) ]` guard on the body, OR use CSS `max-height` transition with
   `overflow: hidden`. The CSS transition is smoother; pick that one.

4. The three dev sections (Handset, Network, Dialer) call the same `PhoneApi` methods the
   current `PhonePage.razor` already calls. **Do not change the API methods.**

5. Mount in `PhoneDashboardPanel.razor`:
   ```razor
   <div style="grid-column: 1; grid-row: 2;">
     <PhoneDevTray Expanded="@_devTrayExpanded"
                   ExpandedChanged="@((bool v) => _devTrayExpanded = v)"
                   CallState="@_callState.CallState"
                   OnSimulateHook="@SimulateHookAsync"
                   OnSimulateIncoming="@SimulateIncomingCallAsync"
                   OnSimulateDial="@SimulateDialAsync" />
   </div>
   ```

**Acceptance:**
- [ ] Default state collapsed (44px tall).
- [ ] Click anywhere on the header expands to ~240px.
- [ ] Collapsed state shows a pulsing amber dot to telegraph "there is something here".
- [ ] Simulate buttons functionally identical to the current page — no API regressions.

---

## P0·6 — Build the Contacts panel

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/Components/Pages/PhoneContactsPanel.razor` (new) — or wherever you decided to
  put the tab panels in P0·2.

**Steps:**

1. Layout: `.contacts` grid (`1fr 320px`, 12px gap, 14px padding).

2. Left column — the list:
   - Replace the current `RadzenButton` "Sync from Phone" dropdown with the prototype's
     toolbar: `.search-wrap` input + count badge on the left, two `.btn`s on the right.
   - **Replace `RadzenDataGrid` with a flat list.** The grid was overkill for ~50 rows and
     it dragged in scrollbars that fight the kiosk's no-scroll requirement. Use a plain
     scrollable `<div class="contact-rows">` with grid-row children.
   - Reuse the existing `MergedContact` record + `RebuildMergedContacts()` method as-is.

3. Right column — sync card + detail card. Both are simple `.card` panels with the same
   data already loaded in `PhonePage.razor`. Reference: `phone-contacts.jsx`.

4. Wire `OpenAddContactDialog`, `OpenEditContactDialog`, `DeleteContactAsync` — these all
   exist; just call them from the new buttons.

5. The Sync dropdown — keep the current behaviour where clicking "Sync from Phone" opens a
   small device picker. **Or** simplify to: if a phone is connected, the button syncs from
   it directly; if more than one paired device exists, fall back to the dropdown. The
   prototype assumes the simpler one-tap path. Pick whichever matches the user's existing
   habit — confirm before changing.

**Acceptance:**
- [ ] Search filters the list client-side, no API round-trip per keystroke.
- [ ] Selecting a row populates the right detail panel; deselecting returns the empty state.
- [ ] PBAP-sourced contacts show only the Call action; manual contacts show Call + Edit +
      Delete. Mirror existing source-based gating.

---

## P0·7 — Build the Call History panel

**Status:** `[PENDING]`
**Files:**
- `src/Radio.Web/Components/Pages/PhoneHistoryPanel.razor` (new)

**Steps:**

1. Layout: `.history-page` grid (`1fr 320px`, 12px gap, 14px padding).

2. Left column — filter row + headers + rows:
   - Filter pills (All / Incoming / Outgoing / Missed) with counts.
   - Replace the current basic list with a 6-column row layout (see README section "Call
     History tab" for the exact columns).
   - "Clear History" stays on the right of the filter row.

3. Right column — three stat tiles:
   - Total Calls · 30 days (LED-style amber).
   - Missed (LED-style red).
   - Top Caller (`.card.accent-amber` with avatar + name + frequency sub-line).

   The aggregates can be computed client-side from `_callHistory` — no new server call.
   Wrap the computation in a `[CallHistoryStats Stats]` record so the markup stays clean.

4. Filter state lives in the panel's `@code`; reset to "all" on tab switch is unnecessary —
   preserve across tab switches.

**Acceptance:**
- [ ] Filter pills update both the visible list AND the counts (count never changes; it's
      derived from the full history, not the filtered set — visual consistency).
- [ ] Direction icons coloured per the README table (green/blue/red).
- [ ] Stat tiles update reactively when `OnCallHistoryUpdated` SignalR event fires.

---

## P1 — Opportunistic consistency fixes (optional)

These are explicitly **out of scope** for the Phone redesign PR but are noted for follow-up
once that PR lands. **Do not bundle them in.**

1. Generalise `.status-row` + `.pill` (now defined in `§28`) into `§13` next to other shared
   list components. Update `BluetoothPage.razor`'s "Adapter Status" card to use them — it
   currently hand-rolls the same layout.
2. Migrate `SystemConfigPage.razor`'s System Stats tab gauge cards to use the same
   `.card.accent-cyan` chrome as the Phone redesign's right column. They're currently
   ad-hoc `RadzenCard`s with inline styling.
3. The top-tab variant in `phone-dashboard.jsx` (`.top-tabs / .top-tab`) is included in the
   prototype as a comparison. It is **not** the canonical implementation — do not port it.

---

## Build / verify checklist

- [ ] `dotnet build` clean, no new warnings.
- [ ] `dotnet test` green.
- [ ] Manual UAT at native 1920×720:
  - Dashboard fits, dev tray collapsed by default.
  - Dev tray expanded does not push the right column off-screen.
  - All four call states render correctly (use the dev tray to walk through them).
  - Contacts tab list scrolls vertically, never horizontally; no clipped action buttons.
  - History tab filter pills + stats reactivity.
  - Tab switch between Dashboard / Contacts / Call History preserves SignalR connections.
- [ ] Take a fresh screenshot into `screenshots/phone.png` so the next design pass starts
      from an updated baseline.

---

## Risks / open questions

- **API surface for Answer / Hang Up:** the current `PhoneApi.cs` has `SimulateHookAsync` and
  the equivalents for dev controls, but does not expose dedicated `AnswerAsync` / `HangupAsync`
  endpoints. The Hero buttons either need new API methods, or the existing simulate handlers
  do double duty. **Confirm with the user before adding API methods.**
- **"Move to Soundbar" button in InCall:** this corresponds to a not-yet-built feature
  (transferring an active call from the Bluetooth HFP path to the local speaker output). If
  the feature is not on the roadmap, render the button greyed out (`disabled`) with a
  tooltip "Coming soon" rather than removing it — the design contemplates it as a future
  primitive.
- **PBAP contacts as read-only:** the prototype treats PBAP rows as read-only (Call action
  only). Verify this matches the live behaviour — `PhonePage.razor` already gates Edit /
  Delete on `c.Source == "Manual"`, so this should be a no-op.
