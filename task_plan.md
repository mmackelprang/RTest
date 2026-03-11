# Task Plan: UI Migration (MudBlazor → Radzen) + BT Disconnect UX

## Goal
1. Replace MudBlazor with Radzen Blazor components, adopting Radzen's Material Design dark theme as the primary aesthetic (dropping the custom color scheme)
2. Improve BT disconnect UX: distinguish phone-side manual disconnect from signal loss to avoid unwanted auto-reconnect

## Decision Log
| Decision | Rationale |
|----------|-----------|
| Full Radzen migration (not dual-stack) | Avoids bundle bloat from running both libraries |
| Use Radzen "material-dark" theme | User prefers Material Design look; drop custom cyan/amber scheme |
| Keep custom canvas visualizers | Radzen has no real-time audio visualization; keep custom JS |
| Keep DSEG LED clock font | Distinctive design element, works with any component library |
| BT: Use BlueZ mgmt disconnect reason | Most reliable way to distinguish remote-user vs signal-loss disconnect |

---

## Phase 1: Scaffolding & Infrastructure `status: complete`
**Estimated: 2-3 hours**

### 1.1 Package swap
- [ ] Remove `MudBlazor` from `Radio.Web.csproj`
- [ ] Add `Radzen.Blazor` package
- [ ] Verify restore + build

### 1.2 Service registration (Program.cs)
- [ ] Replace `builder.Services.AddMudServices(...)` with `builder.Services.AddRadzenComponents()`
- [ ] Remove MudBlazor snackbar configuration block

### 1.3 Imports (_Imports.razor)
- [ ] Replace `@using MudBlazor` with `@using Radzen` and `@using Radzen.Blazor`

### 1.4 Theme & Providers (MainLayout.razor)
- [ ] Remove `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`
- [ ] Add `<RadzenComponents />` (provides Dialog, Notification, ContextMenu, Tooltip)
- [ ] Add `<RadzenTheme Theme="material-dark" />` in App.razor head
- [ ] Add Radzen JS script tag in App.razor
- [ ] Remove `_customTheme` MudTheme object (128-165) — Radzen theme replaces it
- [ ] Remove `Color _distortionMarkerColor` MudBlazor enum — switch to string-based Radzen colors

### 1.5 CSS cleanup (design-system.css)
- [ ] Remove all ~40 `.mud-` prefixed CSS rules
- [ ] Keep design tokens (CSS custom properties), DSEG fonts, layout utilities, touch targets
- [ ] Update CSS custom property values to match Radzen material-dark palette if needed
- [ ] Add any `.rz-` overrides for Radzen component styling

### 1.6 JS cleanup
- [ ] Update `virtual-keyboard.js`: replace 3 `.mud-*` selectors with Radzen equivalents

**Files modified:** `Radio.Web.csproj`, `Program.cs`, `_Imports.razor`, `MainLayout.razor`, `App.razor`, `design-system.css`, `virtual-keyboard.js`

**Gate:** `dotnet build --configuration Release` succeeds (components will be broken, but infrastructure compiles)

---

## Phase 2: Component Migration Reference

### MudBlazor → Radzen Mapping (44 component types)

| MudBlazor | Count | Radzen Equivalent | Notes |
|-----------|-------|--------------------|-------|
| `MudItem` | 175 | `RadzenColumn` | Inside `RadzenRow` |
| `MudText` | 152 | `<span>`/`<p>` + CSS | Or RadzenText if needed |
| `MudButton` | 93 | `RadzenButton` | `ButtonStyle` enum replaces `Variant` |
| `MudSelectItem` | 64 | Items within `RadzenDropDown` | Template-based |
| `MudProgressCircular` | 58 | `RadzenProgressBarCircular` | |
| `MudNumericField` | 57 | `RadzenNumeric` | |
| `MudIcon` | 49 | `RadzenIcon` | Icon names differ |
| `MudIconButton` | 47 | `RadzenButton Icon="..." ButtonStyle="ButtonStyle.Light"` | No separate icon-button |
| `MudStack` | 45 | `<div style="display:flex">` | No direct equivalent |
| `MudTh`/`MudTd` | 84 | `RadzenDataGridColumn` `<Template>` | Column-based not row-based |
| `MudTextField` | 36 | `RadzenTextBox` | |
| `MudTabPanel` | 34 | `RadzenTabsItem` | Inside `RadzenTabs` |
| `MudChip` | 33 | `RadzenBadge` or styled `RadzenButton` | |
| `MudGrid` | 31 | `RadzenRow` | |
| `MudAlert` | 26 | `RadzenAlert` | |
| `MudSelect` | 19 | `RadzenDropDown` | |
| `MudPaper` | 16 | `RadzenCard` | |
| `MudCheckBox` | 16 | `RadzenCheckBox` | |
| `MudCard*` | 29 | `RadzenCard` | Flatten Card/CardContent/CardHeader/CardActions |
| `MudProgressLinear` | 10 | `RadzenProgressBar` | |
| `MudTable` | 9 | `RadzenDataGrid` | Major API difference — biggest effort |
| `MudDialog` | 8 | `DialogService` / `RadzenDialog` | |
| `MudSlider` | 6 | `RadzenSlider` | |
| `MudSwitch` | 5 | `RadzenSwitch` | |
| `MudSpacer` | 5 | CSS `flex: 1` | |
| `MudSimpleTable` | 4 | `<table>` or `RadzenDataGrid` | |
| `MudExpansionPanel(s)` | 4 | `RadzenAccordion` + `RadzenAccordionItem` | |
| `MudDivider` | 3 | `<hr>` or CSS border | |
| `MudOverlay` | 2 | CSS overlay | |
| `MudVirtualize` | 1 | `RadzenDataGrid` virtual scroll | |
| `MudPagination` | 1 | `RadzenDataGrid` built-in paging | |
| `MudDatePicker` | 1 | `RadzenDatePicker` | |
| `MudFileUpload` | 1 | `RadzenUpload` | |
| `MudBadge` | 1 | `RadzenBadge` | |
| `MudList(Item)` | 2 | `RadzenListBox` | |

### Service Injection Mapping
| MudBlazor | Radzen | Files affected |
|-----------|--------|----------------|
| `@inject ISnackbar Snackbar` | `@inject NotificationService Notification` | 10 files |
| `Snackbar.Add(msg, Severity.X)` | `Notification.Notify(NotificationSeverity.X, summary, detail)` | ~30 call sites |
| `@inject IDialogService DialogService` | `@inject DialogService DialogService` | 2 files |
| `DialogService.ShowAsync<T>(...)` | `DialogService.OpenAsync<T>(...)` | ~5 call sites |
| `Color.Primary`, `Color.Error`, etc. | String constants or Radzen enums | ~294 references |
| `Icons.Material.Filled.*` | `"material-icons mi-*"` or Radzen icon names | ~190 references |

---

## Phase 3: Shared Components Migration `status: complete`
**Estimated: 4-5 hours**

Migrate bottom-up: shared components before pages.

### 3.1 CastDeviceDropdown.razor (~150 lines)
- [ ] Replace custom popover with `RadzenDropDown` or `RadzenPopup`
- [ ] Add keyboard navigation
- [ ] Add ARIA accessibility

### 3.2 NowPlayingPanel.razor (~250 lines)
- [ ] Replace `MudText`, `MudIcon`, `MudButton`, `MudChip`
- [ ] Replace `MudProgressLinear` (progress bar)
- [ ] Keep album art blur/gradient (pure CSS)

### 3.3 VisualizerPanel.razor (~200 lines)
- [ ] Replace `MudSelect`, `MudSelectItem` (mode selector)
- [ ] Keep canvas initialization (custom JS — no Radzen equivalent)

### 3.4 QueueHistoryPanel.razor (~400 lines)
- [ ] Replace `MudTabPanel` with `RadzenTabs`/`RadzenTabsItem`
- [ ] Replace `MudVirtualize` with `RadzenDataGrid` virtual scroll or Radzen `Virtualize`
- [ ] Replace `ISnackbar` with `NotificationService`
- [ ] Replace `IDialogService` calls for playlist dialogs

### 3.5 RadioControlPanel.razor (~300 lines)
- [ ] Replace `MudText`, `MudButton`, `MudIconButton`, `MudSlider`, `MudSwitch`
- [ ] Replace `MudSelect` for band/step selectors
- [ ] Keep vintage CSS aesthetic (not MudBlazor-dependent)

### 3.6 Dialog components (3 files)
- [ ] `FileBrowserDialog.razor` — Replace MudDialog, MudTextField, MudSelect, MudCheckBox, MudIconButton
- [ ] `SavePlaylistDialog.razor` — Replace MudDialog, MudTextField
- [ ] `LoadPlaylistDialog.razor` — Replace MudDialog, MudList

**Gate:** All shared components render correctly with Radzen; build succeeds

---

## Phase 4: Page Migration `status: complete`
**Estimated: 12-15 hours**

Migrate pages from simplest to most complex.

### 4.1 Home.razor (simplest — orchestrator only)
- [ ] Replace `MudGrid`/`MudItem` layout with `RadzenRow`/`RadzenColumn`
- [ ] Shared components already migrated in Phase 3

### 4.2 RadioPage.razor
- [ ] Replace `MudGrid`, `MudText`, `MudButton`, `MudDialog` (frequency input)
- [ ] Replace `MudSelect` for SDR controls
- [ ] Replace `MudSlider` for gain control

### 4.3 BluetoothPage.razor
- [ ] Replace `MudTable` (paired devices list) → `RadzenDataGrid`
- [ ] Replace `MudButton`, `MudChip`, `MudProgressCircular`
- [ ] Replace `ISnackbar` calls

### 4.4 MetricsDashboardPage.razor
- [ ] Replace `MudGrid`, `MudCard`, `MudExpansionPanels`
- [ ] Replace `MudProgressCircular`, `MudNumericField`
- [ ] Keep custom canvas chart (metricsChart.js)
- [ ] FUTURE: Consider `RadzenChart` for historical metrics

### 4.5 PlayHistoryPage.razor
- [ ] Replace `MudTable` + `MudPagination` → `RadzenDataGrid` with built-in paging, sorting, filtering
- [ ] Replace sidebar filter form: `MudDatePicker`, `MudSelect`, `MudTextField`
- [ ] Replace `MudDialog` for track details/delete confirmation
- [ ] Replace `ISnackbar` calls

### 4.6 DeviceManagementPage.razor
- [ ] Replace 5 `MudTable` instances → `RadzenDataGrid`
- [ ] Replace `MudTabPanel` (5 tabs) → `RadzenTabs`
- [ ] Replace inline editing (MudTextField in table) → RadzenDataGrid inline edit
- [ ] Replace `ISnackbar` calls

### 4.7 SystemConfigPage.razor (~3,000 lines — largest)
- [ ] Replace `MudTabPanel` (8+ tabs) → `RadzenTabs`
- [ ] Replace ~57 `MudNumericField` instances → `RadzenNumeric`
- [ ] Replace ~19 `MudSelect` instances → `RadzenDropDown`
- [ ] Replace ~36 `MudTextField` instances → `RadzenTextBox`
- [ ] Replace `MudAlert`, `MudSwitch`, `MudCheckBox`
- [ ] Replace `MudFileUpload` → `RadzenUpload`
- [ ] Replace `ISnackbar` + `IDialogService` calls
- [ ] Bookmark management UI: Replace MudButton, MudTextField, MudSelect

**Gate:** All pages render; `dotnet build --configuration Release` 0 warnings 0 errors

---

## Phase 5: Icon & Enum Migration `status: complete` (done inline with Phases 3-4)
**Estimated: 2-3 hours**

### 5.1 Icons (~190 references)
- [ ] Create icon mapping helper: `Icons.Material.Filled.X` → Radzen Material icon string
- [ ] Bulk replace across all files
- [ ] Verify all icons render correctly

### 5.2 Color enums (~294 references)
- [ ] Map `Color.Primary` → Radzen color approach (CSS class or `ButtonStyle`)
- [ ] Bulk replace across all files

### 5.3 Typography (~106 references)
- [ ] Map `Typo.h6` → HTML elements with CSS classes
- [ ] Bulk replace

### 5.4 Size/Variant enums (~35 references)
- [ ] Map `Size.Small/Medium/Large` → Radzen sizing
- [ ] Map `Variant.Filled/Outlined/Text` → `ButtonStyle` or `Variant` enum

---

## Phase 6: Tests & Validation `status: complete`
**Estimated: 3-5 hours**

### 6.1 Test updates (11 files, 116 tests)
- [ ] Replace `Services.AddMudServices()` with `Services.AddRadzenComponents()` (or equivalent for bUnit)
- [ ] Replace `Mock<ISnackbar>` → `Mock<NotificationService>` or real registration
- [ ] Replace `Mock<IDialogService>` → Radzen `DialogService` equivalent
- [ ] Update `JSRuntimeMode.Loose` setup if needed
- [ ] Replace MudBlazor-specific markup assertions (e.g., `Find(".mud-table")`)

### 6.2 Build verification
- [ ] `dotnet build --configuration Release` — 0 warnings, 0 errors
- [ ] `dotnet test tests/Radio.Web.Tests --configuration Release` — 116/116 pass

### 6.3 Visual validation
- [ ] Deploy to Ubuntu (`./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`)
- [ ] Verify all 11 pages render correctly on 1920×720 kiosk display
- [ ] Verify touch targets (≥48px)
- [ ] Verify dark theme consistency
- [ ] Verify real-time visualizer still works (canvas JS untouched)

---

## Phase 7: BT Disconnect Reason Detection `status: pending`
**Estimated: 4-6 hours**

### Current State
The existing code tracks `_userInitiatedDisconnect` flag — but this only catches disconnects initiated from the Radio UI. When the phone user disconnects from their phone's BT settings, BlueZ sees `Connected=false` with no way to distinguish from signal loss, so auto-reconnect starts unwantedly.

### 7.1 Research: BlueZ mgmt disconnect reason
- [ ] Check if `org.bluez.Device1` emits `DisconnectReason` in property changes (BlueZ 5.66+)
- [ ] Check `org.freedesktop.DBus.Properties.PropertiesChanged` for additional metadata
- [ ] Test on Ubuntu: monitor `dbus-monitor --system "interface='org.bluez.Device1'"` during phone-side disconnect vs range loss
- [ ] Check HCI event codes: `btmon` output for `HCI_Disconn_Complete` reason byte

### 7.2 Implementation Option A: HCI Event Monitoring
- [ ] Parse `btmon` or `/sys/kernel/debug/bluetooth/hci0/` for disconnect reason codes
- [ ] Reason `0x13` = Remote User Terminated → suppress reconnect
- [ ] Reason `0x08` = Connection Timeout → allow reconnect
- [ ] Add `DisconnectReason` property to `BluetoothDeviceDisconnectedEventArgs`

### 7.3 Implementation Option B: BlueZ Management Socket
- [ ] Use BlueZ mgmt protocol via D-Bus or raw socket
- [ ] Monitor `Device Disconnected` management event (reason byte: `0x03` = remote host terminated)
- [ ] Map reason to `UserInitiated` flag in disconnect handler

### 7.4 Implementation Option C: Grace Period + Rejection Detection
- [ ] After unexpected disconnect, wait 5-10 seconds before first reconnect attempt
- [ ] If `ConnectAsync` fails with "Connection refused" or "rejected" → phone-side disconnect, stop reconnecting
- [ ] If `ConnectAsync` fails with timeout → out of range, continue reconnecting
- [ ] This is the simplest approach and doesn't require low-level BT access

### 7.5 UI Integration
- [ ] Show disconnect reason in BluetoothPage status
- [ ] Add "Reconnecting..." indicator with attempt count
- [ ] Add "Stop Reconnecting" button to manually cancel reconnection loop
- [ ] If remote-user disconnect detected, show "Device disconnected by user" instead of "Connection lost"

### 7.6 Tests
- [ ] Unit test `BluetoothReconnectionLoop` with new reason-based suppression
- [ ] Unit test disconnect reason parsing
- [ ] Integration test on Ubuntu with actual BT device

**Files modified:** `LinuxBluetoothService.cs`, `BluetoothReconnectionLoop.cs`, `IBluetoothService.cs` (event args), `BluetoothPage.razor`, `BluetoothController.cs`

---

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none yet) | | |
