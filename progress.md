# Progress Log

## Session: 2026-02-11 — UI Fixes & UAT Prep

### Phase 1: Quick Fixes & Dead Code Cleanup [DONE]
- Removed dead timing/logging code from VisualizationTapModifier.cs
- Removed balance control from NowPlayingPanel.razor
- Forced balance=0 in AudioManager.RestoreVolumePreferences()
- Fixed source color mapping in QueueHistoryPanel (removed Spotify, FilePlayer→File, added Bluetooth/Vinyl)

### Phase 2: Title Bar — Mute & Volume Only [DONE]
- Stripped mini-player from MainLayout.razor (album art, track info, transport controls)
- Kept only mute button + volume slider with 48dp touch targets
- Removed _isPlaying, _nowPlayingTitle, _nowPlayingArtist, _nowPlayingAlbumArt fields
- Removed nav icons: /files, /visualizer

### Phase 3: Play History Display Fix [DONE]
- Updated QueueHistoryPanel history display: bold title, "Artist · Duration" format
- Added FormatDuration helper method
- Verified PlayHistoryPage already correct

### Phase 4: Merge Files into Queue & Remove Pages [DONE]
- Rewrote QueuePage.razor with combined queue management + file browser toggle
- Deleted FileBrowserPage.razor and VisualizerPage.razor
- Deleted VisualizerPageTests.cs
- Fixed MUD0002 error: Dense="true" → Margin="Margin.Dense" on MudTextField

### Phase 5: Metrics Page — Filtering & Sparklines [DONE]
- Rewrote MetricsDashboardPage.razor with category filter chips
- Fixed FormatMetricValue bug (memory metrics showed as %)
- Added SVG sparklines via inline path rendering
- Parallel loading of sparkline data for up to 20 metrics

### Phase 6: Device Defaults & Store Management [DONE]
- Fixed IsActive on AudioDeviceDto — now shows which output device is active
- Added GetSelectedOutputDeviceId()/GetSelectedInputDeviceId() to IAudioDeviceManager
- Added SetInputDeviceAsync to IAudioDeviceManager + SoundFlowDeviceManager
- Added POST /api/devices/input endpoint
- Added "Set as Default" button for input devices on DeviceManagementPage
- Verified Store Management tab already has full UI (info, import/export, comparison)

### Phase 7: Material 3 Theme Polish [DONE]
- Updated MudTheme dark palette with M3 surface tonal elevation colors
- Changed app bar height 60px → 64px (M3 standard), content 516px → 512px
- Updated CSS variables to M3 color tokens (oklch → hex for reliability)
- Added M3 surface tint elevation CSS rules (.mud-elevation-0 through -4)
- Cleaned up Spotify references from touch-targets.css

### Phase 8: Deployment Scripts [DONE]
- Created deploy/ directory structure (common/, raspberry-pi/, debian-x64/)
- radio-console.service: systemd unit with security hardening
- publish.sh: cross-compile helper (arm64/x64/all)
- setup.sh for both Raspberry Pi and Debian x64
- DEPLOYMENT.md: full guide with prerequisites, configuration, troubleshooting

### Phase 9: Build Verification & Test [DONE]
- Build: 0 warnings, 0 errors
- All 7 test projects pass (RTLSDRCore: 125, Core: 35, Infra: 689, API: 202, Integration: 83+3 skipped, Web: 120, Web.E2E: 6)
- Fixed 4 test failures from UI changes: balance→volume, AddFiles→BrowseFiles, TimeRange button labels
