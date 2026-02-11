# Task Plan: UI Fixes & UAT Prep

## Goal
Fix Web UI issues, improve Material 3 compliance and touch-friendliness, clean up dead code/logging, fix play history display, simplify navigation by merging/removing redundant pages, enhance metrics with filtering and sparklines, verify device defaults and store management, and create deployment scripts for Raspberry Pi and x64 Debian.

## Current Phase
Phase 0: Planning

## Phases

### Phase 1: Quick Fixes & Dead Code Cleanup
- [ ] Remove commented-out `Console.WriteLine` and unused timing variables from `VisualizationTapModifier.cs` (lines 64-70)
- [ ] Remove balance control from `NowPlayingPanel.razor` (lines 108-122) — balance stays "centered" permanently
- [ ] Set balance to 0.0 in `AudioManager.RestoreVolumePreferences()` to ensure centered on startup regardless of stored value
- [ ] Fix source color mapping in `QueueHistoryPanel.razor` (lines 283-292): remove "Spotify", change "FilePlayer" to "File", add "Bluetooth" → Color.Secondary, add "Vinyl" → Color.Success
- **Status:** pending
- **Key files:**
  - `src/Radio.Infrastructure/Audio/SoundFlow/VisualizationTapModifier.cs`
  - `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`
  - `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`
  - `src/Radio.Infrastructure/Audio/Services/AudioManager.cs`

### Phase 2: Title Bar — Mute & Volume Only
- [ ] Strip mini-player from `MainLayout.razor` (lines 84-154): remove album art, track title/artist, transport controls (prev/pause/next)
- [ ] Keep only mute button + volume slider in the title bar center section
- [ ] Wire mute/volume to match NowPlayingPanel behavior exactly: same API calls (`AudioApi.SetVolumeAsync`, `AudioApi.ToggleMuteAsync`), same SignalR event subscriptions (`PlaybackStateChanged` → refresh volume/mute state)
- [ ] Ensure bidirectional sync: volume change in title bar reflects in NowPlayingPanel and vice versa (already happens via SignalR, but verify)
- [ ] Increase touch target size for mute button to 48dp minimum (M3 compliance)
- **Status:** pending
- **Key files:**
  - `src/Radio.Web/Components/Layout/MainLayout.razor` (lines 84-154, 732-768)

### Phase 3: Play History Display Fix
- [ ] Fix `QueueHistoryPanel.razor` Recent Plays to display: **Song Name**, **Artist**, **Song Length** (duration), **Source chip** (device type)
- [ ] Investigate why Bluetooth entries show stream URL instead of song metadata — trace `PlayHistoryEntryDto.Track?.Title` for BT source entries. Root cause is likely `AudioStateUpdateService` recording the stream URL as the title when no AVRCP metadata is available
- [ ] Fix play history recording to use AVRCP metadata (Title/Artist) when available, fall back to source type name ("Bluetooth Audio") instead of stream URL
- [ ] Update `PlayHistoryEntryDto` display in QueueHistoryPanel: line 1 = Title (bold), line 2 = "Artist · Duration", right side = Source chip
- [ ] Fix full PlayHistoryPage source color mapping to match QueueHistoryPanel fixes
- **Status:** pending
- **Key files:**
  - `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`
  - `src/Radio.Web/Components/Pages/PlayHistoryPage.razor`
  - `src/Radio.API/Services/AudioStateUpdateService.cs` (play history recording)
  - `src/Radio.Web/Models/ApiModels.cs` (PlayHistoryEntryDto)

### Phase 4: Merge Files into Queue & Remove Redundant Pages
- [ ] **Queue Page Enhancement**: Add a tabbed or collapsible file browser section to `QueuePage.razor` — import the file browsing functionality from `FileBrowserPage.razor` (drive selector, breadcrumb nav, custom path, search/filter, virtual keyboard)
- [ ] Layout: Queue list on top (or left), file browser on bottom (or right) with a toggle/tab
- [ ] Migrate all `FileBrowserPage.razor` state management, API calls, and UI into `QueuePage.razor`
- [ ] **Remove Files page**: Delete `FileBrowserPage.razor`, remove `/files` route, remove `_showFilesNav` conditional and folder icon from `MainLayout.razor`
- [ ] **Remove Visualizer page**: Delete `VisualizerPage.razor`, remove `/visualizer` route and icon from `MainLayout.razor` (embedded visualizer on Home is sufficient)
- [ ] Update "Add Files to Queue" button to trigger inline file browser instead of dialog
- [ ] Verify all file browser API service calls still work from new location
- **Status:** pending
- **Key files:**
  - `src/Radio.Web/Components/Pages/QueuePage.razor`
  - `src/Radio.Web/Components/Pages/FileBrowserPage.razor` (source, then DELETE)
  - `src/Radio.Web/Components/Pages/VisualizerPage.razor` (DELETE)
  - `src/Radio.Web/Components/Layout/MainLayout.razor` (nav cleanup)

### Phase 5: Metrics Page — Filtering & Sparklines
- [ ] Add category filter chips (API, Audio, Bluetooth, Library, Radio, System, TTS, UI, WebSocket) at the top — filter grid to selected category
- [ ] Fix "Memory Usage Mb" formatting bug — the `FormatMetricValue` method (line 333-350) incorrectly applies percentage format to MB values. Add specific handling for "memory" metrics.
- [ ] Add sparklines to each metric card: small inline SVG or canvas showing the last N data points. Use the existing `MetricsApi.GetMetricAggregateAsync()` to fetch time-series data for each visible metric.
- [ ] Consider using MudBlazor `MudSparkLine` component if available, or a lightweight canvas-based sparkline
- [ ] Improve card visual hierarchy: larger values, better category labels, M3 tonal card backgrounds
- **Status:** pending
- **Key files:**
  - `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor`
  - `src/Radio.Web/Services/ApiClients/MetricsApiService.cs`

### Phase 6: Device Defaults & Store Management Verification
- [ ] **Device defaults on startup**: Trace the startup code path to verify that persisted default output device and Cast device are actually used when the app starts. Check `AudioManager.InitializeAsync()` and `AudioEngineInitializationService`.
- [ ] **Input device default**: Add "Set as Default" action to input devices on DeviceManagementPage (currently read-only)
- [ ] **Store Management verification**: Test JSON export, DB backup export, and DB backup import with the current multi-database architecture (configuration.db, secrets.db, fingerprints.db, metrics.db). Ensure the Store Management UI handles or at least acknowledges all databases.
- [ ] Document any gaps in store management (e.g., does export include secrets.db? fingerprints.db?)
- **Status:** pending
- **Key files:**
  - `src/Radio.Web/Components/Pages/DeviceManagementPage.razor`
  - `src/Radio.Web/Components/Pages/SystemConfigPage.razor` (Tab 7: Store Management)
  - `src/Radio.API/Services/AudioEngineInitializationService.cs`
  - `src/Radio.Infrastructure/Audio/Services/AudioManager.cs`

### Phase 7: Material 3 Theme Polish
- [ ] **MudBlazor theme**: Update `MudThemeProvider` palette for M3 dark theme — surface tonal elevation (surface = #1C1B1F, surfaceContainer = #211F26, etc.), primary seed color, on-surface text colors
- [ ] **Touch targets**: Audit all interactive elements (buttons, sliders, icons) and ensure minimum 48dp touch targets. Add padding where needed.
- [ ] **Button variants**: Replace text-only action buttons with M3 filled-tonal buttons for primary actions, outlined for secondary
- [ ] **App bar**: Increase title bar height from 60px to 64dp (M3 standard). Ensure nav icons have proper padding.
- [ ] **Chips**: Update source type chips to use M3 tonal chip style (filled with secondary-container color)
- [ ] **Elevation**: Apply M3 surface tint to MudPaper components — higher elevation = lighter tint
- **Status:** pending
- **Key files:**
  - `src/Radio.Web/Components/Layout/MainLayout.razor` (theme provider)
  - `src/Radio.Web/wwwroot/css/` (global styles)
  - All Razor components with inline styles

### Phase 8: Deployment Scripts (Raspberry Pi & Debian x64)
- [ ] Create `deploy/raspberry-pi/setup.sh`:
  - Install .NET 8 runtime (ARM64)
  - Install system dependencies: `libmp3lame-dev`, `libasound2-dev`, `avahi-daemon` (mDNS for Cast discovery), `bluez` (Bluetooth), `pulseaudio` or `pipewire`
  - Download/extract fpcalc ARM64 binary to `tools/fpcalc/`
  - Create systemd service files for Radio.API and Radio.Web
  - Configure `netsh`-equivalent port permissions (iptables/firewall rules)
  - Create data directories (`./data/config`, `./data/metrics`, `./data/fingerprints`, `./data/secrets`, `./data/albumart`, `./data/backups`)
  - Set up HTTP URL reservation (Kestrel doesn't need this on Linux, but firewall rules)
  - Build and publish self-contained for `linux-arm64`
- [ ] Create `deploy/debian-x64/setup.sh`:
  - Same as RPi but targeting `linux-x64` runtime
  - Install .NET 8 runtime (x64)
  - Same system dependencies
  - fpcalc x64 binary
  - systemd service files
- [ ] Create `deploy/DEPLOYMENT.md`:
  - Prerequisites (hardware, OS, network)
  - Step-by-step instructions for both platforms
  - Configuration (appsettings.json customization for each environment)
  - Troubleshooting (common issues: port conflicts, audio devices, Bluetooth pairing, Cast discovery)
  - How to update/upgrade
- [ ] Create `deploy/common/radio-api.service` and `radio-web.service` (systemd unit files)
- [ ] Create `deploy/common/publish.sh` — cross-compile helper (builds for target platform from Windows dev box)
- **Status:** pending
- **Key files (all new):**
  - `deploy/raspberry-pi/setup.sh`
  - `deploy/debian-x64/setup.sh`
  - `deploy/common/radio-api.service`
  - `deploy/common/radio-web.service`
  - `deploy/common/publish.sh`
  - `deploy/DEPLOYMENT.md`

### Phase 9: Build Verification & Test
- [ ] `dotnet build --configuration Release` — 0 warnings
- [ ] `dotnet test --configuration Release` — all tests pass
- [ ] Manual smoke test: Home page, play history, queue with file browser, metrics, devices, system/store management
- [ ] Test deployment scripts on actual Raspberry Pi (if available) or in Docker ARM64 emulation
- **Status:** pending

## Design Decisions

### Balance Control Removal
Rather than hiding the balance control behind a settings menu, we permanently remove it from the Now Playing panel. Balance is always centered (0.0). The `BalanceModifier` remains in the audio pipeline but is never exposed in the UI. If balance control is needed in the future, it can be re-added to a Settings/Audio page.

### Title Bar Simplification
The title bar serves as a persistent quick-access control. With the full Now Playing panel always visible on the home page, the title bar doesn't need duplicate transport controls or track info. Mute + volume is the most-used quick control and keeps the bar clean.

### Queue/Files Merge Strategy
The Queue page becomes the single destination for both queue management and file browsing. Use a two-section layout:
- **Top**: Current queue (existing functionality)
- **Bottom**: File browser (migrated from FileBrowserPage) with a collapsible section or tab

This eliminates the confusing conditional Files nav icon and gives users a unified experience.

### Material 3 Approach
MudBlazor v6+ supports custom theming. We'll define a M3-compliant dark theme palette using the MudBlazor `MudTheme` API rather than custom CSS where possible. The primary color will shift from pure cyan to a M3-harmonized teal/cyan seed color with proper tonal palette generation.

### Deployment Architecture
Both RPi and Debian deployments use:
- Self-contained .NET 8 publish (no runtime install needed on target — simplifies deployment)
- systemd for process management (auto-restart, logging)
- Single `setup.sh` that handles all dependencies and configuration
- `publish.sh` on the dev box for cross-compilation

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
