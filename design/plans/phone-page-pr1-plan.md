# Phone Page PR-1: Shell + Dashboard Redesign

> **Scope:** P0-1 (CSS selectors) + P0-2 (left-rail shell) + P0-3 (Phone Status Hero) +
> P0-5 (Dev Tray) + right-column passthrough for existing System Status / Call Path markup.
>
> **Follows:** Design handoff at `docs/design-handoffs/design_handoff_phone_page/`
>
> **Deferred to PR-2:** P0-4 (System Status card + Call Path card restyled to compact form,
> needs Phase B fields deployed to RotaryPhone)
>
> **Deferred to PR-3:** P0-6 (Contacts) + P0-7 (History)
>
> **Last updated:** 2026-05-24

---

## Overview

This plan restructures the Phone page from a Radzen top-tab layout into the design system's
left-rail tab pattern, introduces the Phone Status Hero component on the Dashboard tab, and
adds the collapsible Dev Tray. The right column of the Dashboard carries forward the
*existing* System Status and Call Path markup inside the new grid layout -- it is NOT
restyled to the handoff's compact cards yet (that is PR-2).

### Files touched (summary)

| Action  | File |
|---------|------|
| Modify  | `src/Radio.Web/wwwroot/css/design-system.css` |
| Modify  | `src/Radio.Web/Components/Pages/PhonePage.razor` |
| Rewrite | `src/Radio.Web/Components/Pages/PhonePage.razor.css` |
| Create  | `src/Radio.Web/Components/Pages/PhoneDashboardPanel.razor` |
| Create  | `src/Radio.Web/Components/Shared/PhoneStatusHero.razor` |
| Create  | `src/Radio.Web/Components/Shared/PhoneDevTray.razor` |
| Create  | `src/Radio.Web/Components/Pages/PhoneContactsPanel.razor` (stub) |
| Create  | `src/Radio.Web/Components/Pages/PhoneHistoryPanel.razor` (stub) |
| Modify  | `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs` |

---

## Task 1: Add Phone CSS selectors to `design-system.css` (P0-1)

**File:** `src/Radio.Web/wwwroot/css/design-system.css`

**What to do:**

Append a new section header and selectors after line 4891 (end of file). The section number
is `§Ph` (not `§28` -- that is already taken by the Visualizer Panel section). Use
`§Ph Phone Page Surface` as the section label.

**Selectors to add (lifted verbatim from `design_handoff_phone_page/styles.css`):**

```css
/* === §Ph  Phone Page Surface ================================================ */

/* -- Shell & Tab Rail -------------------------------------------------------- */
.phone-shell { ... }                   /* lines 202-207 of styles.css */
.tab-rail { ... }                      /* lines 209-215 */
.rail-heading { ... }                  /* lines 218-224 */
.rail-tab { ... }                      /* lines 227-239 */
.rail-tab .icon { ... }               /* line 240 */
.rail-tab .rail-label { ... }         /* lines 241-244 */
.rail-tab:hover { ... }               /* line 245 */
.rail-tab.active { ... }              /* lines 246-250 */
.rail-tab.active::before { ... }      /* lines 251-258 */
.tab-pane { ... }                      /* lines 284-289 */
.dashboard { ... }                     /* lines 292-300 */

/* -- Cards (phone-specific, no collision with Radzen cards) ------------------ */
.card { ... }                          /* lines 303-312 */
.card.accent-green { ... }            /* line 314 */
.card.accent-cyan { ... }             /* line 315 */
.card.accent-amber { ... }            /* line 316 */
.card.accent-blue { ... }             /* line 317 */
.card-title { ... }                    /* lines 319-329 */
.card-title .title-text { ... }       /* line 331 */
.card-title.title-green .title-text   /* line 332 */
.card-title.title-cyan .title-text    /* line 333 */
.card-title.title-amber .title-text   /* line 334 */
.card-title.title-blue .title-text    /* line 335 */

/* -- Phone Status Hero ------------------------------------------------------- */
.hero { ... }                          /* lines 338-349 */
.hero-glow { ... }                     /* lines 350-355 */
.hero-body { ... }                     /* lines 356-363 */
.hero-top { ... }                      /* lines 365-370 */
.hero-state-label { ... }             /* lines 372-377 */
.hero-source-tag { ... }              /* lines 379-386 */
.hero-source-tag .dot { ... }         /* lines 387-391 */
.hero-state { ... }                    /* lines 393-403 */
.hero-meta { ... }                     /* lines 405-410 */
.hero-meta-row { ... }                /* lines 411-413 */
.hero-icon { ... }                     /* lines 414-421 */
.hero-number { ... }                   /* lines 422-429 */
.hero-name { ... }                     /* lines 430-434 */
.hero-duration { ... }                 /* lines 435-444 */
.hero-actions { ... }                  /* lines 446-450 */
.hero-empty { ... }                    /* lines 452-459 */

/* -- Phone Action Buttons ---------------------------------------------------- */
.phone-btn { ... }                     /* lines 462-477 */
.phone-btn .icon { ... }              /* line 478 */
.phone-btn:hover { ... }              /* line 479 */
.phone-btn:active { ... }             /* line 480 */
.phone-btn.btn-answer { ... }         /* lines 482-486 */
.phone-btn.btn-answer:hover { ... }   /* line 487 */
.phone-btn.btn-hangup { ... }         /* lines 489-493 */
.phone-btn.btn-hangup:hover { ... }   /* line 494 */
.phone-btn.btn-ghost { ... }          /* lines 496-500 */
.phone-btn.btn-ghost:hover { ... }    /* line 501 */
.phone-btn:disabled { ... }           /* (add: opacity 0.35, cursor not-allowed) */

/* -- Status mini-rows (compact system-status list) --------------------------- */
.status-list { ... }                   /* lines 504-507 */
.status-row { ... }                    /* lines 508-516 */
.status-row .lbl { ... }              /* lines 517-523 */
.status-row .val { ... }              /* lines 524-531 */

/* -- Pill badges ------------------------------------------------------------- */
.pill { ... }                          /* lines 532-541 */
.pill::before { ... }                  /* lines 542-548 */
.pill.green { ... }                    /* line 549 */
.pill.red { ... }                      /* line 550 */
.pill.amber { ... }                    /* line 551 */
.pill.blue { ... }                     /* line 552 */
.pill.cyan { ... }                     /* line 553 */
.pill.gray { ... }                     /* line 554 */
.pill.gray::before { ... }            /* line 555 */

/* -- Call Path card internals ------------------------------------------------ */
.callpath { ... }                      /* lines 558-562 */
.callpath-row { ... }                  /* lines 563-565 */
.callpath-row .sub { ... }            /* lines 566-572 */
.mode-selector { ... }                 /* lines 574-583 */
.mode-btn { ... }                      /* lines 584-596 */
.mode-btn .icon { ... }               /* line 597 */
.mode-btn:hover { ... }               /* line 598 */
.mode-btn.active { ... }              /* lines 599-603 */
.connector-row { ... }                 /* lines 605-612 */
.connector-row .conn-meta { ... }     /* lines 613-618 */
.connector-row .conn-name { ... }     /* lines 619-621 */
.connector-row .conn-name .sub { ... }/* lines 622-629 */
.conn-icon { ... }                     /* lines 631-639 */
.connector-row .right-cluster { ... } /* lines 640-642 */
.link-btn { ... }                      /* lines 644-655 */

/* -- Dev Drawer (phone-specific) --------------------------------------------- */
.phone-dev-drawer { ... }             /* lines 658-667, rename .dev-drawer -> .phone-dev-drawer */
.phone-dev-drawer.collapsed { ... }   /* line 668 */
.phone-dev-drawer.expanded { ... }    /* line 669 */
.phone-dev-header { ... }             /* lines 671-679, rename .dev-header -> .phone-dev-header */
.phone-dev-header:hover { ... }       /* line 680 */
.phone-dev-header .left { ... }       /* lines 681-689 */
.phone-dev-header .left .icon { ... } /* line 690 */
.phone-dev-header .right { ... }      /* lines 691-697 */
.phone-dev-body { ... }               /* lines 698-703, rename .dev-body -> .phone-dev-body */
.phone-dev-section { ... }            /* line 704, rename to avoid collision */
.phone-dev-label { ... }              /* lines 705-711, rename */
.phone-dev-buttons { ... }            /* line 712, rename */

/* -- Phone form controls (small utility buttons and inputs) ------------------ */
.phone-btn-sm { ... }                  /* lines 714-729, rename .btn -> .phone-btn-sm */
.phone-btn-sm:hover { ... }           /* line 728 */
.phone-btn-sm:disabled { ... }        /* line 729 */
.phone-btn-sm .icon { ... }           /* line 725 */
.phone-btn-sm.btn-success { ... }     /* line 731 */
.phone-btn-sm.btn-danger { ... }      /* line 732 */
.phone-btn-sm.btn-warn { ... }        /* line 733 */
.phone-input { ... }                   /* lines 736-748, rename .input -> .phone-input */
.phone-input:focus { ... }            /* line 749 */

/* -- Animations -------------------------------------------------------------- */
@keyframes ringPulse { ... }           /* lines 1096-1099 */
.ring-pulse { ... }                    /* line 1100 */
@keyframes phoneDotPulse { ... }       /* lines 1102-1105, rename to avoid collision */
.phone-dot-pulse { ... }              /* line 1106, rename */
```

**Naming collision avoidance (CRITICAL):**

The handoff's `styles.css` uses generic names that collide with existing design-system or
Radzen selectors. Rename these:

| Prototype name | Production name | Reason |
|---|---|---|
| `.dev-drawer` | `.phone-dev-drawer` | Existing `DevTray.razor` uses `.dev-tray` class in `§Q` |
| `.dev-header` | `.phone-dev-header` | Same collision namespace |
| `.dev-body` | `.phone-dev-body` | Same |
| `.dev-section` | `.phone-dev-section` | `.dev-section` used in PhonePage.razor.css currently |
| `.dev-label` | `.phone-dev-label` | Same |
| `.dev-buttons` | `.phone-dev-buttons` | Same |
| `.btn` | `.phone-btn-sm` | Generic `.btn` collides with everything |
| `.input` | `.phone-input` | Generic `.input` collides with Radzen |
| `@keyframes dotPulse` | `@keyframes phoneDotPulse` | `dotPulse` might collide later |
| `.dot-pulse` | `.phone-dot-pulse` | Same |

**Selectors to SKIP (already exist in design-system.css):**

- All `:root` token declarations (all `--surface-*`, `--accent-*`, `--signal-*`, `--text-*`,
  `--font-*`, `--topbar-height`, `--content-height`) -- in `§2`
- `*, *::before, *::after { box-sizing }` -- in `§3`
- `html, body` base styles -- in `§3`
- `.cluster`, `.cluster-label`, `.cluster-value`, `.cluster-swatch` -- in `§5`
- `.nav-pill`, `.nav-badge`, `.nav-pill-label` -- in `§7`
- `.source-bubble` and children -- in `§27`
- `.content-area` -- in `§9`
- `.topbar`, `.topbar-primary`, `.topbar-sources` -- in `§5`
- `.top-tabs`, `.top-tab` -- skip entirely (not the canonical implementation per README)
- `.spacer`, `.row`, `.col`, utility classes -- skip (too generic, use inline styles)
- `.placeholder-page` -- skip (not used in PR-1)
- `.np-dock-spacer` -- skip (not phone-related)

**Estimated size:** ~380 new lines appended to design-system.css.

**Acceptance criteria:**
- [ ] `design-system.css` has a `§Ph Phone Page Surface` section at the end
- [ ] No duplicate selectors with existing sections
- [ ] All renamed selectors use the `phone-` prefix where noted above
- [ ] `dotnet build` succeeds with no warnings
- [ ] Visual regression: Home, Devices, Bluetooth pages render unchanged

**Dependencies:** None (this is the first task).

---

## Task 2: Restructure PhonePage.razor into the left-rail shell (P0-2)

**Files:**
- `src/Radio.Web/Components/Pages/PhonePage.razor` (modify)
- `src/Radio.Web/Components/Pages/PhonePage.razor.css` (rewrite)
- `src/Radio.Web/Components/Pages/PhoneDashboardPanel.razor` (create)
- `src/Radio.Web/Components/Pages/PhoneContactsPanel.razor` (create stub)
- `src/Radio.Web/Components/Pages/PhoneHistoryPanel.razor` (create stub)

**Dependencies:** Task 1 (CSS selectors must exist).

### 2A. Create stub tab panels

Create three new files as placeholders for the tab panel content. The Dashboard panel will
be fully built in Tasks 3-5. Contacts and History panels are stubs for PR-1 -- they just
render the CURRENT markup from PhonePage.razor moved verbatim into the new component.

#### `PhoneContactsPanel.razor` (stub, ~120 lines)

```
src/Radio.Web/Components/Pages/PhoneContactsPanel.razor
```

Parameters:
```csharp
[Parameter] public List<MergedContact>? MergedContacts { get; set; }
[Parameter] public PbapDeviceSyncInfoDto? ConnectedDeviceSyncInfo { get; set; }
[Parameter] public BluetoothStatusDto? BtStatus { get; set; }
[Parameter] public bool IsSyncing { get; set; }
[Parameter] public bool ShowSyncDropdown { get; set; }
[Parameter] public EventCallback<bool> ShowSyncDropdownChanged { get; set; }
[Parameter] public EventCallback<string> OnSyncFromDevice { get; set; }
[Parameter] public EventCallback OnAddContact { get; set; }
[Parameter] public EventCallback<ContactDto> OnEditContact { get; set; }
[Parameter] public EventCallback<ContactDto> OnDeleteContact { get; set; }
[Parameter] public List<ContactDto>? Contacts { get; set; }
```

Body: Move the entire `<RadzenTabsItem Text="Contacts">` body from PhonePage.razor into this
component verbatim. Keep the `RadzenDataGrid`, the sync dropdown, and the sync status bar.
The `MergedContact` record definition stays in PhonePage.razor (or moves to a shared
location -- keep in PhonePage.razor for now since PR-3 will refactor contacts).

Note: The `MergedContact` record is currently `private` in PhonePage.razor's `@code` block.
To share it with the panel, either:
- Move it to `Radio.Web.Models` namespace in `ApiModels.cs` (preferred), OR
- Make it `internal` and keep it in PhonePage.razor

**Decision: Move `MergedContact` record to `ApiModels.cs`** as a public record so both
PhonePage and PhoneContactsPanel can reference it. Add after the `PbapContactDto` class:

```csharp
public record MergedContact(string? Id, string Name, string Phone, string? Email, string Source);
```

#### `PhoneHistoryPanel.razor` (stub, ~60 lines)

```
src/Radio.Web/Components/Pages/PhoneHistoryPanel.razor
```

Parameters:
```csharp
[Parameter] public List<CallHistoryEntryDto>? CallHistory { get; set; }
[Parameter] public EventCallback OnClearHistory { get; set; }
```

Body: Move the entire `<RadzenTabsItem Text="Call History">` body from PhonePage.razor
verbatim. Include the `GetCallDirectionIcon` and `GetCallDirectionColor` helpers as
`private static` methods in the panel (they are currently `private static` in
PhonePage.razor and can be duplicated without issue -- they are pure functions).

#### `PhoneDashboardPanel.razor` (~50 lines initial, grows in Tasks 3-5)

```
src/Radio.Web/Components/Pages/PhoneDashboardPanel.razor
```

Parameters (full set -- some used in later tasks):
```csharp
[Parameter] public PhoneCallStateDto CallState { get; set; } = new();
[Parameter] public PhoneSystemStatusDto? SystemStatus { get; set; }
[Parameter] public bool IsAvailable { get; set; }
[Parameter] public string GvActiveMode { get; set; } = "";
[Parameter] public bool GvBridgeAvailable { get; set; }
[Parameter] public bool GvTrunkRegistered { get; set; }
[Parameter] public bool SwitchingMode { get; set; }
[Parameter] public bool DevTrayExpanded { get; set; }
[Parameter] public EventCallback<bool> DevTrayExpandedChanged { get; set; }
[Parameter] public string DialDigits { get; set; } = "";
[Parameter] public EventCallback<string> DialDigitsChanged { get; set; }
[Parameter] public EventCallback<string> OnSwitchMode { get; set; }
[Parameter] public EventCallback OnReregisterTrunk { get; set; }
[Parameter] public EventCallback<bool> OnSimulateHook { get; set; }
[Parameter] public EventCallback OnSimulateIncoming { get; set; }
[Parameter] public EventCallback OnSimulateDial { get; set; }
[Parameter] public List<MergedContact>? Contacts { get; set; }
```

Initial body -- the dashboard grid layout:
```razor
<div class="dashboard">
  @* Left column, row 1: Hero (Task 3) *@
  <PhoneStatusHero CallState="CallState"
                   CallSource="@(CallState.CallState is "InCall" or "Dialing" ? "Bluetooth HFP" : "Rotary Phone")"
                   Contacts="Contacts"
                   OnAnswer="@(() => OnSimulateHook.InvokeAsync(true))"
                   OnHangup="@(() => OnSimulateHook.InvokeAsync(false))" />

  @* Right column, spans both rows: existing System Status + Call Path (passthrough) *@
  <div style="display: flex; flex-direction: column; gap: 12px; grid-column: 2;
              grid-row: 1 / 3; min-height: 0; overflow: auto;">
    @* --- PR-1: existing markup, NOT restyled to compact cards yet --- *@
    <div class="card accent-green">
      <div class="card-title title-green">
        <span class="title-text">System Status</span>
        <span class="pill @(IsAvailable ? "green" : "red")">@(IsAvailable ? "Healthy" : "Offline")</span>
      </div>
      <div class="status-list">
        <div class="status-row">
          <span class="lbl">Platform</span>
          <span class="val">@(SystemStatus?.Platform ?? "Unknown")</span>
          <span class="pill gray">@(SystemStatus?.IsRaspberryPi == true ? "Pi" : "x64")</span>
        </div>
        <div class="status-row">
          <span class="lbl">Bluetooth</span>
          <span class="val">@(SystemStatus?.BluetoothDeviceAddress ?? "--")</span>
          <span class="pill @(SystemStatus?.BluetoothConnected == true ? "green" : "red")">
            @(SystemStatus?.BluetoothConnected == true ? "Connected" : "Disconnected")
          </span>
        </div>
        <div class="status-row">
          <span class="lbl">SIP Device</span>
          <span class="val">@(SystemStatus?.SipListenAddress ?? "--"):@(SystemStatus?.SipPort)</span>
          <span class="pill @(SystemStatus?.SipListening == true ? "green" : "red")">
            @(SystemStatus?.SipListening == true ? "Listening" : "Offline")
          </span>
        </div>
        <div class="status-row">
          <span class="lbl">HT801 ATA</span>
          <span class="val">@(SystemStatus?.Ht801IpAddress ?? "--")</span>
          <span class="pill @(SystemStatus?.Ht801Reachable == true ? "green" : "red")">
            @(SystemStatus?.Ht801Reachable == true ? "Online" : "Offline")
          </span>
        </div>
      </div>
    </div>

    <div class="card accent-cyan">
      <div class="card-title title-cyan">
        <span class="title-text">Call Path</span>
      </div>
      <div class="callpath-row">
        <span class="sub">Active Mode</span>
        <div class="mode-selector">
          <button type="button" class="mode-btn @(GvActiveMode == "BluetoothHfp" ? "active" : "")"
                  @onclick="@(() => OnSwitchMode.InvokeAsync("BluetoothHfp"))"
                  disabled="@SwitchingMode">
            <RadzenIcon Icon="bluetooth" /> Bluetooth
          </button>
          <button type="button" class="mode-btn @(GvActiveMode == "SipTrunk" ? "active" : "")"
                  @onclick="@(() => OnSwitchMode.InvokeAsync("SipTrunk"))"
                  disabled="@SwitchingMode">
            <RadzenIcon Icon="settings_phone" /> SIP Trunk
          </button>
          <button type="button" class="mode-btn @(GvActiveMode == "GVApi" ? "active" : "")"
                  @onclick="@(() => OnSwitchMode.InvokeAsync("GVApi"))"
                  disabled="@SwitchingMode">
            <RadzenIcon Icon="language" /> GV API
          </button>
        </div>
      </div>
      <div class="connector-row">
        <div class="conn-meta">
          <span class="conn-icon"><RadzenIcon Icon="language" /></span>
          <div class="conn-name">
            <span>GV API</span>
            <span class="sub">@(GvBridgeAvailable ? "SIP-WSS Bridge" : "Bridge")</span>
          </div>
        </div>
        <div class="right-cluster">
          <span class="pill @(GvBridgeAvailable ? "green" : "red")">
            @(GvBridgeAvailable ? "Available" : "Unavailable")
          </span>
        </div>
      </div>
      <div class="connector-row">
        <div class="conn-meta">
          <span class="conn-icon"><RadzenIcon Icon="settings_phone" /></span>
          <div class="conn-name">
            <span>SIP Trunk</span>
            <span class="sub">voip.ms &middot; 5060</span>
          </div>
        </div>
        <div class="right-cluster">
          <span class="pill @(GvTrunkRegistered ? "green" : "red")">
            @(GvTrunkRegistered ? "Registered" : "Unregistered")
          </span>
          @if (!GvTrunkRegistered)
          {
            <button type="button" class="link-btn" @onclick="OnReregisterTrunk">Re-register</button>
          }
        </div>
      </div>
    </div>
  </div>

  @* Left column, row 2: Dev Tray (Task 5) *@
  <div style="grid-column: 1; grid-row: 2;">
    <PhoneDevTray Expanded="DevTrayExpanded"
                  ExpandedChanged="DevTrayExpandedChanged"
                  CallState="@CallState.CallState"
                  DialDigits="DialDigits"
                  DialDigitsChanged="DialDigitsChanged"
                  OnSimulateHook="OnSimulateHook"
                  OnSimulateIncoming="OnSimulateIncoming"
                  OnSimulateDial="OnSimulateDial" />
  </div>
</div>
```

**Design note on right column:** The right column in PR-1 uses the new `.card`, `.card-title`,
`.status-list`, `.status-row`, `.pill`, `.callpath-row`, `.mode-selector`, `.mode-btn`,
`.connector-row`, and `.link-btn` selectors from Task 1. This means the right column IS
restyled to the handoff's visual language, but uses only the fields currently available in
`PhoneSystemStatusDto` and `GvBridgeStatusDto`. PR-2 will add the Phase B fields
(`sipRegistered`, `cookiesValid`, audio bridge diagnostics, HT801 diagnostics) and may
further refine the card content.

**Important note on GV mode values:** The current PhonePage uses `"GVApi"` as the mode
string (see line 89-91 of PhonePage.razor). The prototype uses `"GVBrowser"`. Keep using
`"GVApi"` since that's what the RotaryPhone API actually accepts. The button label reads
"GV API" which matches.

### 2B. Rewrite PhonePage.razor as a thin shell

Reduce `PhonePage.razor` from 797 lines to ~200 lines. It becomes:
- The `@page "/phone"` route
- The `@inject` directives (unchanged)
- The left-rail shell markup with tab buttons
- The `@if` switch rendering one of three panels
- The `@code` block retaining all state fields, `OnInitializedAsync`, `RefreshAllAsync`,
  `PollStatusAsync`, all SignalR handlers, all API methods, `RebuildMergedContacts`,
  `Dispose`, and helper methods -- but NOT the inline markup for each tab

Shell markup:
```razor
@page "/phone"
@inject PhoneApiService PhoneApi
@inject PhoneHubService PhoneHub
@inject GvBridgeApiService GvBridgeApi
@inject GvTrunkApiService GvTrunkApi
@inject GvTrunkHubService GvTrunkHub
@inject NotificationService NotificationService
@inject PbapApiService PbapApi
@inject BluetoothApiService BluetoothApi
@implements IDisposable

<PageTitle>Phone - Radio Console</PageTitle>

<div class="phone-shell">
  <div class="tab-rail">
    <span class="rail-heading">Phone</span>
    <button type="button" class="rail-tab @(IsTab("dashboard") ? "active" : "")"
            @onclick='@(() => _activeTab = "dashboard")'>
      <RadzenIcon Icon="dashboard" />
      <span class="rail-label">Dashboard</span>
    </button>
    <button type="button" class="rail-tab @(IsTab("contacts") ? "active" : "")"
            @onclick='@(() => _activeTab = "contacts")'>
      <RadzenIcon Icon="contacts" />
      <span class="rail-label">Contacts</span>
    </button>
    <button type="button" class="rail-tab @(IsTab("history") ? "active" : "")"
            @onclick='@(() => _activeTab = "history")'>
      <RadzenIcon Icon="history" />
      <span class="rail-label">Call History</span>
    </button>
  </div>

  <div style="overflow: hidden; min-height: 0;">
    @if (_activeTab == "dashboard")
    {
      <PhoneDashboardPanel CallState="_callState"
                           SystemStatus="_systemStatus"
                           IsAvailable="_isAvailable"
                           GvActiveMode="_gvActiveMode"
                           GvBridgeAvailable="_gvBridgeAvailable"
                           GvTrunkRegistered="_gvTrunkRegistered"
                           SwitchingMode="_switchingMode"
                           DevTrayExpanded="_devTrayExpanded"
                           DevTrayExpandedChanged="@((bool v) => { _devTrayExpanded = v; StateHasChanged(); })"
                           DialDigits="_dialDigits"
                           DialDigitsChanged="@((string v) => { _dialDigits = v; StateHasChanged(); })"
                           OnSwitchMode="SwitchModeAsync"
                           OnReregisterTrunk="ReregisterTrunkAsync"
                           OnSimulateHook="SimulateHookAsync"
                           OnSimulateIncoming="SimulateIncomingCallAsync"
                           OnSimulateDial="SimulateDialStringAsync"
                           Contacts="MergedContacts" />
    }
    else if (_activeTab == "contacts")
    {
      <PhoneContactsPanel ... />
    }
    else if (_activeTab == "history")
    {
      <PhoneHistoryPanel CallHistory="_callHistory"
                         OnClearHistory="ClearCallHistoryAsync" />
    }
  </div>
</div>
```

**Code block changes:**
- Remove `private int _selectedTab;`
- Add `private string _activeTab = "dashboard";`
- Add `private bool _devTrayExpanded;`
- Add `private bool IsTab(string t) => _activeTab == t;`
- Add `SimulateDialStringAsync(string digits)` overload that accepts the digits parameter
  from the Dev Tray's `OnSimulateDial` callback (current `SimulateDialAsync` reads from
  `_dialDigits` field; the new version takes the parameter directly):
  ```csharp
  private async Task SimulateDialStringAsync(string digits)
  {
    if (!string.IsNullOrWhiteSpace(digits))
    {
      await PhoneApi.SimulateDialAsync(digits);
      _dialDigits = "";
    }
  }
  ```
- Keep existing `SimulateDialAsync` for backward compat (used by nobody after refactor, but
  harmless to keep)
- Move `MergedContact` record to `ApiModels.cs` (see 2A above)
- Remove inline `MergedContact` record definition from `@code`

### 2C. Rewrite PhonePage.razor.css

Delete all existing content (all `::deep` selectors referencing Radzen component internals
are obsolete). Replace with minimal page-level styles:

```css
/* PhonePage shell -- all visual styles are in design-system.css §Ph.
   This file only handles CSS isolation for page-level layout. */
```

If any page-specific override is needed (unlikely), add it here. Otherwise the file can be
essentially empty -- all styling comes from the global `.phone-shell`, `.tab-rail`, etc.
selectors in design-system.css.

**Estimated sizes:**
- PhonePage.razor: ~200 lines (was 797)
- PhonePage.razor.css: ~5 lines (was 162)
- PhoneDashboardPanel.razor: ~150 lines initially (grows to ~180 after Tasks 3-5)
- PhoneContactsPanel.razor: ~120 lines (verbatim move)
- PhoneHistoryPanel.razor: ~60 lines (verbatim move)
- ApiModels.cs: +1 line (MergedContact record)

**Acceptance criteria:**
- [ ] PhonePage.razor is under 220 lines
- [ ] Tab switching between Dashboard / Contacts / History works
- [ ] SignalR subscriptions remain at the page level (not tab-level), so switching tabs does
      NOT cause reconnection
- [ ] `dotnet build` clean
- [ ] No `::deep` selectors remain in PhonePage.razor.css

---

## Task 3: Build the Phone Status Hero component (P0-3)

**File:** `src/Radio.Web/Components/Shared/PhoneStatusHero.razor` (new)

**Dependencies:** Task 1 (CSS), Task 2 (dashboard panel mounts it).

### Parameters

```csharp
[Parameter] public PhoneCallStateDto CallState { get; set; } = new();
[Parameter] public string CallSource { get; set; } = "Rotary Phone";
[Parameter] public EventCallback OnAnswer { get; set; }
[Parameter] public EventCallback OnHangup { get; set; }
[Parameter] public List<MergedContact>? Contacts { get; set; }
```

### State color computation

```csharp
private (string color, string glow, string glowBg) StateColors => CallState.CallState switch
{
  "Ringing" => ("var(--signal-amber)", "rgba(240,168,48,0.50)", "rgba(240,168,48,0.15)"),
  "InCall"  => ("var(--signal-green)", "rgba(74,222,128,0.45)", "rgba(74,222,128,0.12)"),
  "Dialing" => ("var(--signal-blue)",  "rgba(96,165,250,0.45)", "rgba(96,165,250,0.12)"),
  _         => ("var(--text-medium)",  "transparent",            "transparent"),
};
```

### State label mapping

```csharp
private string StateLabel => CallState.CallState switch
{
  "Ringing" => "Incoming Call",
  "InCall"  => "Active Call",
  "Dialing" => "Dialing Out",
  _         => "Awaiting Call",
};
```

### Active phone number

```csharp
private string? ActiveNumber => CallState.CallState switch
{
  "Ringing" => CallState.IncomingNumber,
  "InCall"  => CallState.IncomingNumber ?? CallState.DialedNumber,
  "Dialing" => CallState.DialedNumber,
  _         => null,
};
```

### Caller name lookup (client-side contact match)

```csharp
private string? CallerName
{
  get
  {
    var number = ActiveNumber;
    if (string.IsNullOrEmpty(number) || Contacts == null) return null;
    // Normalize: strip non-digit chars, take last 10 digits for suffix match
    var digits = new string(number.Where(char.IsDigit).ToArray());
    var suffix = digits.Length > 10 ? digits[^10..] : digits;
    if (suffix.Length < 7) return null; // too short for reliable match
    return Contacts.FirstOrDefault(c =>
    {
      var cDigits = new string(c.Phone.Where(char.IsDigit).ToArray());
      var cSuffix = cDigits.Length > 10 ? cDigits[^10..] : cDigits;
      return cSuffix == suffix;
    })?.Name;
  }
}
```

### InCall duration timer (client-side)

```csharp
private DateTime? _inCallStartUtc;
private System.Threading.Timer? _durationTimer;
private TimeSpan _inCallDuration;

protected override void OnParametersSet()
{
  if (CallState.CallState == "InCall" && _inCallStartUtc == null)
  {
    _inCallStartUtc = DateTime.UtcNow;
    _durationTimer = new System.Threading.Timer(_ =>
    {
      _inCallDuration = DateTime.UtcNow - _inCallStartUtc.Value;
      InvokeAsync(StateHasChanged);
    }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
  }
  else if (CallState.CallState != "InCall")
  {
    _durationTimer?.Dispose();
    _durationTimer = null;
    _inCallStartUtc = null;
    _inCallDuration = TimeSpan.Zero;
  }
}

private string FormattedDuration =>
  _inCallDuration.TotalHours >= 1
    ? _inCallDuration.ToString(@"hh\:mm\:ss")
    : _inCallDuration.ToString(@"mm\:ss");

public void Dispose()
{
  _durationTimer?.Dispose();
}
```

The component must `@implements IDisposable` to clean up the timer.

### Hero icon per state

```csharp
private string HeroIcon => CallState.CallState switch
{
  "Ringing" => "call_received",
  "Dialing" => "call_made",
  "InCall"  => "phone_in_talk",
  _         => "phone",
};
```

### Markup structure

```razor
<div class="hero"
     style="--hero-state-color: @(StateColors.color);
            --hero-state-glow: @(StateColors.glow);
            --hero-glow-color: @(StateColors.glowBg);">
  <div class="hero-glow"></div>
  <div class="hero-body">

    @* Top row: state label + source tag *@
    <div class="hero-top">
      <span class="hero-state-label">@StateLabel</span>
      <span class="hero-source-tag">
        <span class="dot" style="background: @(StateColors.color);
              box-shadow: 0 0 6px @(StateColors.glow);"></span>
        via @CallSource
      </span>
    </div>

    @* Big LED state word *@
    <div class="hero-state @(CallState.CallState == "Ringing" ? "ring-pulse" : "")"
         style="color: @(StateColors.color);
                text-shadow: 0 0 20px @(StateColors.glow);">
      @CallState.CallState.ToUpperInvariant()
    </div>

    @* Caller info or empty state *@
    @if (ActiveNumber != null)
    {
      <div class="hero-meta">
        <div class="hero-meta-row">
          <span class="hero-icon"
                style="background: color-mix(in oklab, @(StateColors.color) 18%, transparent);
                       color: @(StateColors.color);">
            <RadzenIcon Icon="@HeroIcon" />
          </span>
          <div style="display: flex; flex-direction: column; gap: 2px;">
            <span class="hero-number">@ActiveNumber</span>
            @if (CallerName != null)
            {
              <span class="hero-name">@CallerName</span>
            }
          </div>
          @if (CallState.CallState == "InCall")
          {
            <span class="hero-duration">@FormattedDuration</span>
          }
        </div>
      </div>
    }
    else
    {
      <div class="hero-empty">
        <RadzenIcon Icon="phone" />
        Lift the handset to place a call, or wait for an incoming ring.
      </div>
    }

    @* Action buttons -- contextual to state *@
    <div class="hero-actions">
      @switch (CallState.CallState)
      {
        case "Ringing":
          <button type="button" class="phone-btn btn-answer" @onclick="OnAnswer">
            <RadzenIcon Icon="phone" /> Answer
          </button>
          <button type="button" class="phone-btn btn-hangup" disabled
                  title="Physical handset only">
            <RadzenIcon Icon="call_end" /> Reject
          </button>
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="ring_volume" /> Silence
          </button>
          break;

        case "InCall":
          <button type="button" class="phone-btn btn-hangup" @onclick="OnHangup">
            <RadzenIcon Icon="call_end" /> Hang Up
          </button>
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="mic" /> Mute
          </button>
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="dialpad" /> Keypad
          </button>
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="swap_horiz" /> Move to Soundbar
          </button>
          break;

        case "Dialing":
          <button type="button" class="phone-btn btn-hangup" @onclick="OnHangup">
            <RadzenIcon Icon="call_end" /> Cancel
          </button>
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="speaker" /> Speaker
          </button>
          break;

        default: // Idle
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="dialpad" /> New Call
          </button>
          <button type="button" class="phone-btn btn-ghost" disabled
                  title="Not yet implemented">
            <RadzenIcon Icon="contacts" /> Pick Contact
          </button>
          break;
      }
    </div>

  </div>
</div>
```

### Key design decisions applied

1. **Answer button** wires to `OnAnswer` which maps to `SimulateHookAsync(true)` (lifting
   handset IS answering). This is the existing mechanism.
2. **Hang Up / Cancel** wires to `OnHangup` which maps to `SimulateHookAsync(false)`.
3. **Reject button** renders as disabled with tooltip "Physical handset only" -- no
   `POST /api/phone/reject` endpoint exists.
4. **Mute, Keypad, Silence, Speaker, Move-to-Soundbar, New Call, Pick Contact** -- all
   rendered as disabled ghost buttons with "Not yet implemented" tooltip for v1.
5. **CallerName** -- client-side contact lookup using last-10-digit suffix match against
   the merged contacts list.
6. **InCall duration** -- tracked client-side with a 1-second `System.Threading.Timer`.
   Reset when state transitions away from InCall.

**Estimated size:** ~180 lines (.razor) + ~30 lines (@code).

**Acceptance criteria:**
- [ ] Cycling Idle -> Ringing -> InCall -> Dialing (via Dev Tray) flips Hero color and
      action button set per the screenshots
- [ ] RINGING state word pulses via `ring-pulse` animation
- [ ] InCall shows a ticking duration counter in amber LED font
- [ ] Caller name appears when the incoming/dialed number matches a contact (last-10
      suffix match)
- [ ] Disabled buttons show as ghost with tooltip
- [ ] `dotnet build` clean

---

## Task 4: Build the Phone Dev Tray component (P0-5)

**File:** `src/Radio.Web/Components/Shared/PhoneDevTray.razor` (new)

**Dependencies:** Task 1 (CSS), Task 2 (dashboard panel mounts it).

### Parameters

```csharp
[Parameter] public bool Expanded { get; set; }
[Parameter] public EventCallback<bool> ExpandedChanged { get; set; }
[Parameter] public string CallState { get; set; } = "Idle";
[Parameter] public string DialDigits { get; set; } = "";
[Parameter] public EventCallback<string> DialDigitsChanged { get; set; }
[Parameter] public EventCallback<bool> OnSimulateHook { get; set; }
[Parameter] public EventCallback OnSimulateIncoming { get; set; }
[Parameter] public EventCallback OnSimulateDial { get; set; }
```

### Markup

```razor
<div class="phone-dev-drawer @(Expanded ? "expanded" : "collapsed")">
  <div class="phone-dev-header"
       @onclick="@(() => ExpandedChanged.InvokeAsync(!Expanded))"
       role="button"
       aria-expanded="@Expanded">
    <div class="left">
      <RadzenIcon Icon="settings" /> Dev Tray &middot; Simulate Hardware Events
    </div>
    <div class="right">
      <span style="display: inline-flex; align-items: center; gap: 8px;">
        @if (!Expanded)
        {
          <span class="phone-dot-pulse"
                style="width: 6px; height: 6px; border-radius: 50%;
                       background: var(--signal-amber); display: inline-block;"></span>
        }
        @(Expanded ? "Click to collapse" : "Click to expand")
        <RadzenIcon Icon="expand_more"
                    Style="@(Expanded ? "transform: rotate(180deg);" : "")" />
      </span>
    </div>
  </div>

  @if (Expanded)
  {
    <div class="phone-dev-body">
      <div class="phone-dev-section">
        <span class="phone-dev-label">Handset</span>
        <div class="phone-dev-buttons">
          <button type="button" class="phone-btn-sm btn-success"
                  disabled="@(CallState == "InCall")"
                  @onclick="@(() => OnSimulateHook.InvokeAsync(true))">
            <RadzenIcon Icon="phone" /> Lift
          </button>
          <button type="button" class="phone-btn-sm btn-danger"
                  disabled="@(CallState == "Idle")"
                  @onclick="@(() => OnSimulateHook.InvokeAsync(false))">
            <RadzenIcon Icon="call_end" /> Drop
          </button>
        </div>
      </div>

      <div class="phone-dev-section">
        <span class="phone-dev-label">Network</span>
        <div class="phone-dev-buttons">
          <button type="button" class="phone-btn-sm btn-warn"
                  @onclick="OnSimulateIncoming">
            <RadzenIcon Icon="ring_volume" /> Incoming Call
          </button>
        </div>
      </div>

      <div class="phone-dev-section">
        <span class="phone-dev-label">Dialer</span>
        <div class="phone-dev-buttons">
          <input class="phone-input"
                 placeholder="Digits"
                 value="@DialDigits"
                 @oninput="@(e => DialDigitsChanged.InvokeAsync(e.Value?.ToString() ?? ""))"
                 style="flex: 1; min-width: 120px;" />
          <button type="button" class="phone-btn-sm"
                  disabled="@string.IsNullOrWhiteSpace(DialDigits)"
                  @onclick="OnSimulateDial">
            <RadzenIcon Icon="dialpad" /> Dial
          </button>
        </div>
      </div>
    </div>
  }
</div>
```

### Collapse/expand behavior

The CSS `max-height` transition on `.phone-dev-drawer.collapsed` (44px) and
`.phone-dev-drawer.expanded` (240px) provides a smooth 200ms ease animation. The body
content is conditionally rendered with `@if (Expanded)` -- this is acceptable because the
transition is on `max-height` of the outer container (the visual slide happens regardless of
whether inner content is rendered or hidden). The 200ms is fast enough that the content
pop-in is not perceptible.

**Estimated size:** ~80 lines.

**Acceptance criteria:**
- [ ] Default state: collapsed, 44px tall, amber pulsing dot visible
- [ ] Click header: expands to ~240px, shows 3-column grid of simulate controls
- [ ] Click header again: collapses back
- [ ] Simulate Lift -> state goes to Dialing
- [ ] Simulate Drop -> state goes to Idle
- [ ] Simulate Incoming Call -> state goes to Ringing
- [ ] Enter digits + click Dial -> digits sent, input cleared
- [ ] Lift button disabled when InCall; Drop button disabled when Idle
- [ ] Functionally identical to current dev controls -- no API regressions
- [ ] `dotnet build` clean

---

## Task 5: Update tests

**File:** `tests/Radio.Web.Tests/Components/Pages/PhonePageTests.cs` (modify)

**Dependencies:** Tasks 2-4 (all components must compile).

### Changes needed

The existing tests verify specific markup strings that will change:

| Test | Current assertion | New assertion |
|---|---|---|
| `PhonePage_Renders_WithTabs` | Checks "Dashboard", "Contacts", "Call History" text | Still passes -- tab rail buttons contain these strings |
| `PhonePage_Renders_SystemStatusSection` | Checks "SYSTEM STATUS", "BLUETOOTH", "SIP DEVICE", "HT801 ATA" | Change to check "System Status" (card-title case), "Bluetooth", "SIP Device", "HT801 ATA" (status-row labels are now `lbl` class with different casing -- check lowercase versions) |
| `PhonePage_Renders_PhoneStatusSection` | Checks "CURRENT STATUS" | Change to check "Awaiting Call" or "IDLE" (Hero renders these instead) |
| `PhonePage_Renders_DeveloperControls` | Checks "DEV CONTROLS" | Change to check "Dev Tray" (new header text) |
| `PhonePage_Renders_CallPathSection` | Checks "CALL PATH", "GV API", "SIP TRUNK" | Change to check "Call Path", "GV API", "SIP Trunk" |
| `PhonePage_ContactsTab_Renders_*` | Tab not initially active; checks tab header exists | Unchanged -- tab header text still in rail |

Updated test expectations:

```csharp
[Fact]
public void PhonePage_Renders_WithTabs()
{
  var cut = RenderComponent<PhonePage>();
  Assert.Contains("Dashboard", cut.Markup);
  Assert.Contains("Contacts", cut.Markup);
  Assert.Contains("Call History", cut.Markup);
}

[Fact]
public void PhonePage_Renders_SystemStatusSection()
{
  var cut = RenderComponent<PhonePage>();
  Assert.Contains("System Status", cut.Markup);
  // Status row labels are now lowercase mono text
  Assert.Contains("Bluetooth", cut.Markup);
  Assert.Contains("SIP Device", cut.Markup);
  Assert.Contains("HT801 ATA", cut.Markup);
}

[Fact]
public void PhonePage_Renders_HeroIdleState()
{
  var cut = RenderComponent<PhonePage>();
  Assert.Contains("Awaiting Call", cut.Markup);
  Assert.Contains("IDLE", cut.Markup);
}

[Fact]
public void PhonePage_Renders_DevTray()
{
  var cut = RenderComponent<PhonePage>();
  // Dev tray is collapsed by default; header text is always visible
  Assert.Contains("Dev Tray", cut.Markup);
  Assert.Contains("Simulate Hardware Events", cut.Markup);
}

[Fact]
public void PhonePage_Renders_CallPathSection()
{
  var cut = RenderComponent<PhonePage>();
  Assert.Contains("Call Path", cut.Markup);
  Assert.Contains("GV API", cut.Markup);
  Assert.Contains("SIP Trunk", cut.Markup);
}
```

### New tests to add

```csharp
[Fact]
public void PhonePage_TabRail_DefaultsToDashboard()
{
  var cut = RenderComponent<PhonePage>();
  // Dashboard tab should have the "active" class
  var dashButton = cut.FindAll("button.rail-tab").FirstOrDefault(b => b.TextContent.Contains("Dashboard"));
  Assert.NotNull(dashButton);
  Assert.Contains("active", dashButton.ClassList);
}

[Fact]
public void PhonePage_HeroShowsEmptyStateHint_WhenIdle()
{
  var cut = RenderComponent<PhonePage>();
  Assert.Contains("Lift the handset to place a call", cut.Markup);
}

[Fact]
public void PhonePage_DevTray_CollapsedByDefault()
{
  var cut = RenderComponent<PhonePage>();
  Assert.Contains("Click to expand", cut.Markup);
  Assert.DoesNotContain("Handset", cut.Markup); // body not rendered when collapsed
}
```

**Estimated changes:** ~40 lines modified, ~25 lines added.

**Acceptance criteria:**
- [ ] All 8-10 tests in `PhonePageTests.cs` pass
- [ ] `dotnet test tests/Radio.Web.Tests` green (full suite)
- [ ] No other test files need changes (PhoneApiService tests are unaffected)

---

## Task 6: Build verification

**Dependencies:** All previous tasks.

### Build commands

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

### Manual verification checklist (UAT at 1920x720)

- [ ] Navigate to `/phone` -- left rail visible with three tabs
- [ ] Dashboard tab active by default
- [ ] Hero shows "IDLE" in `--text-medium` color, empty-state hint visible
- [ ] Click "Contacts" tab -- contacts panel renders (stub: existing RadzenDataGrid)
- [ ] Click "Call History" tab -- history panel renders (stub: existing list)
- [ ] Click "Dashboard" tab -- returns to dashboard
- [ ] Dev Tray collapsed at bottom of left column (44px)
- [ ] Click Dev Tray header -- expands, shows Handset / Network / Dialer sections
- [ ] Click "Incoming Call" button -- Hero transitions to RINGING:
  - State word pulses amber
  - Action buttons: Answer (green), Reject (disabled), Silence (disabled)
- [ ] Click "Lift" in dev tray -- Hero transitions to INCALL:
  - State word green, static
  - Duration counter starts ticking
  - Action buttons: Hang Up (red), Mute (disabled), Keypad (disabled), Move to Soundbar (disabled)
- [ ] Click "Drop" in dev tray -- Hero transitions to IDLE
- [ ] Right column: System Status card with 4 status rows + pills
- [ ] Right column: Call Path card with mode selector + 2 connector rows
- [ ] Mode selector buttons highlight active mode
- [ ] SIP Trunk "Re-register" link-btn visible when unregistered
- [ ] No vertical scrollbar on Dashboard at default state
- [ ] Tab switching preserves SignalR connections (verify in browser devtools Network tab)

---

## Implementation order

```
Task 1 (CSS)     -- no dependencies, builds first
    |
    v
Task 2 (Shell)   -- depends on Task 1 CSS classes
    |
    +---> Task 3 (Hero)      -- depends on Task 2 mounting it
    +---> Task 4 (Dev Tray)  -- depends on Task 2 mounting it
    |
    v
Task 5 (Tests)   -- depends on Tasks 2-4 compiling
    |
    v
Task 6 (Verify)  -- depends on everything
```

Tasks 3 and 4 can be implemented in parallel (independent components). However, the
dashboard panel markup in Task 2 references both components, so either:
- Implement Tasks 2-4 together as one commit, OR
- Create Task 3 and Task 4 as empty stubs first, then fill them in

**Recommended approach:** Implement in 3 commits:
1. **Commit 1:** Task 1 (CSS only)
2. **Commit 2:** Tasks 2 + 3 + 4 together (shell + hero + dev tray + stub panels)
3. **Commit 3:** Task 5 (test updates)

---

## Risk register

| Risk | Mitigation |
|---|---|
| **E1: `.card` selector too generic** | Verify no Radzen component uses `.card` natively. Grep confirmed: Radzen uses `.rz-card` internally. The bare `.card` is safe. |
| **E2: Dev Tray name collision** | Renamed all dev-drawer selectors to `phone-dev-*` prefix to avoid collision with existing `DevTray.razor` component's `§Q` selectors. |
| **E3: `color-mix()` browser support** | `color-mix(in oklab, ...)` is used for the Hero icon background. Supported in all evergreen browsers since 2023. The kiosk runs Chromium -- safe. |
| **E4: RadzenIcon inside native buttons** | RadzenIcon renders a `<i>` tag with Material Symbols class. Wrapping it in a native `<button>` (not RadzenButton) works fine -- just need the icon class to be in scope (it is, via Radzen's global CSS). |
| **E5: Contacts/History stubs preserve scroll** | The stubs carry forward the current markup verbatim, so RadzenDataGrid and the history list behave identically. No visual regression. |
| **E6: CallState string matching** | RotaryPhone emits `"Idle"`, `"Ringing"`, `"InCall"`, `"Dialing"` as `enum.ToString()` values. The Hero's `switch` matches these exactly. If RotaryPhone ever changes casing, the fallback arm (`_`) catches it gracefully as Idle. |
| **E7: Timer disposal on tab switch** | The Hero's duration timer is disposed via `IDisposable` on the component. When the user switches away from Dashboard tab, Blazor disposes the Hero component, stopping the timer. No memory leak. |
