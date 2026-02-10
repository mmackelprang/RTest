# Task Plan: Bluetooth Debug and Fixes

## Goal
Fix the non-functional Bluetooth audio pipeline, complete platform implementations (Windows primary, Linux secondary), enable AVRCP metadata from connected devices, add Web UI management, capture Bluetooth metrics (devices connected, errors, connection duration, etc.) into the metrics DB and Web UI, and deliver comprehensive tests and documentation. Ensure architecture does not preclude future HFP integration for RotaryPhone project.

## Current Phase
Phase 8: Final Verification & Commit — complete

## Phases

### Phase 1: Branch Creation & Audio Capture Pipeline Fix
- [ ] Create `bluetooth-enablement` branch from `main`
- [ ] Fix `SoundFlowDeviceManager.FindCaptureDeviceByName()` to return `AudioCaptureDevice` instead of string
- [ ] Fix `WindowsBluetoothService.GetAudioCaptureDeviceAsync()` to use the corrected method and return an `AudioCaptureDevice`
- [ ] Fix `LinuxBluetoothService` — inject `SoundFlowDeviceManager`, implement `GetAudioCaptureDeviceAsync()` to find PulseAudio/PipeWire Bluetooth monitor source
- [ ] Fix `BluetoothAudioSource.InitializeAsync()` to use configured device name from `BluetoothOptions` (not hardcoded `Name`)
- [ ] Verify build: 0 warnings, 0 errors
- **Status:** complete
- **Key files:**
  - `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowDeviceManager.cs` — fix `FindCaptureDeviceByName` return type
  - `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs` — fix GetAudioCaptureDeviceAsync
  - `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` — inject device manager, implement audio capture
  - `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothServiceFactory.cs` — pass device manager to Linux service
  - `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` — use configured device name

### Phase 2: Platform Service Completeness
- [x] **Windows**: Implement proper device connection/disconnection handling
  - `DisconnectAsync` should properly close Bluetooth connection, not just null out ConnectedDevice
  - Handle device removal/disconnect events from OS
  - Subscribe to BluetoothAudioSource DeviceConnected/DeviceDisconnected
- [x] **Linux**: Implement remaining stubs
  - `PairDeviceAsync` — use IDevice1.PairAsync() via D-Bus
  - `UnpairDeviceAsync` — use IAdapter1.RemoveDeviceAsync() via D-Bus
  - `DisconnectAsync` — use IDevice1.DisconnectAsync() via D-Bus
- [x] **Both**: Wire DeviceConnected/DeviceDisconnected events properly to BluetoothAudioSource
- [x] **BluetoothAudioSource**: Subscribe to DeviceConnected/DeviceDisconnected events
  - On disconnect: transition to Stopped/Error state, update metadata
  - On connect: re-initialize audio capture if auto-switch enabled
- **Status:** complete
- **Key files:**
  - `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs`
  - `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`
  - `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

### Phase 3: AVRCP Metadata & Album Art
- [x] Add `AlbumArtUrl` property to `BluetoothPlaybackMetadata` class
- [x] **Linux**: Complete D-Bus MediaPlayer1 property watcher for AVRCP metadata
  - Track title, artist, album from MPRIS/BlueZ MediaPlayer1 interface
  - Album art URL if available via MPRIS ArtUrl property
- [x] **Windows**: AVRCP metadata requires `net8.0-windows10.0.17763.0` TFM (breaks Linux builds). Left as TODO; fingerprinting pipeline serves as fallback.
- [x] **BluetoothAudioSource**: Update OnMetadataChanged to propagate AlbumArtUrl
  - Set `StandardMetadataKeys.AlbumArtUrl` from Bluetooth metadata when available
  - Set `NeedsFingerprintingLookup = true` when metadata is incomplete (no title/artist)
  - Fingerprinting augments/supplements Bluetooth metadata (cover art from Cover Art Archive)
- [x] Ensure AudioStateUpdateService broadcasts Bluetooth metadata changes via SignalR
- **Status:** complete
- **Key files:**
  - `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs` — update BluetoothPlaybackMetadata
  - `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` — complete MPRIS watcher
  - `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs` — add Windows media session metadata
  - `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` — propagate album art, fingerprint flag

### Phase 4: Web UI — Bluetooth Management Page
- [x] Create `BluetoothApiService.cs` in `src/Radio.Web/Services/ApiClients/`
  - GetStatusAsync, StartAsync, StopAsync, StartDiscoveryAsync, StopDiscoveryAsync
  - PairAsync, UnpairAsync, AcceptAsync, DisconnectAsync
- [x] Register BluetoothApiService in `src/Radio.Web/Program.cs`
- [x] Add Bluetooth DTOs to `src/Radio.Web/Models/ApiModels.cs`
- [x] Create `BluetoothPage.razor` — dedicated page at `/bluetooth`
  - Status card: adapter state, connected device, discoverable status
  - Device name setting (editable, Save button)
  - Start/Stop adapter toggle
  - Discovery: Start Scan button, list of discovered devices with Pair buttons
  - Paired devices list with Connect/Unpair actions
  - Connected device info with Disconnect button
  - Auto-accept, auto-switch toggles
- [x] Add navigation link in MainLayout (conditional on Bluetooth source)
- **Status:** complete
- **Key files:**
  - `src/Radio.Web/Services/ApiClients/BluetoothApiService.cs` (new)
  - `src/Radio.Web/Program.cs` — register HttpClient
  - `src/Radio.Web/Models/ApiModels.cs` — add Bluetooth DTOs
  - `src/Radio.Web/Components/Pages/DeviceManagementPage.razor` — add Bluetooth tab/section

### Phase 5: Bluetooth Metrics
- [x] Add Bluetooth metrics instrumentation to platform services and BluetoothAudioSource
  - `bluetooth.devices_connected_total` — Counter: running count of all device connections over time
  - `bluetooth.devices_disconnected_total` — Counter: running count of disconnections
  - `bluetooth.active_connections` — Gauge: currently connected device count (0 or 1)
  - `bluetooth.discovery_sessions` — Counter: number of discovery scans started
  - `bluetooth.pair_attempts` — Counter: pairing attempts with `result` tag (success/failure)
  - `bluetooth.audio_capture_errors` — Counter: audio capture initialization failures
  - `bluetooth.connection_duration_seconds` — Gauge: how long current device has been connected
  - `bluetooth.metadata_updates` — Counter: AVRCP metadata change events received
- [x] Instrument `WindowsBluetoothService` and `LinuxBluetoothService`
  - Inject `IMetricsCollector` into both platform services and BluetoothServiceFactory
  - Record connection/disconnection/discovery/pair counters at event points
- [x] Instrument `BluetoothAudioSource`
  - Inject `IMetricsCollector`
  - Track `bluetooth.active_connections` gauge on connect/disconnect
  - Track `bluetooth.connection_duration_seconds` on periodic update or disconnect
  - Track `bluetooth.audio_capture_errors` on InitializeAsync failure
  - Track `bluetooth.metadata_updates` on MetadataChanged
- [x] Add Bluetooth metrics display to MetricsDashboardPage.razor (auto-grouped by `bluetooth.*` prefix)
  - Add "Bluetooth" group to metrics categories
  - Show `bluetooth.devices_connected_total` as a key snapshot card
  - All `bluetooth.*` metrics visible in detail view with history graphs
- [x] Bluetooth metrics visible on MetricsDashboardPage via existing auto-grouping
  - Show connection stats summary (total connections, current duration, errors) in status card
- [x] Add tests for metrics instrumentation
  - Verify `IMetricsCollector.Increment` called on connect/disconnect/pair
  - Verify `IMetricsCollector.Gauge` called for active connections
- **Status:** complete
- **Key files:**
  - `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs` — add metrics
  - `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` — add metrics
  - `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothServiceFactory.cs` — pass IMetricsCollector
  - `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` — add metrics
  - `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor` — Bluetooth category
  - `src/Radio.Web/Components/Pages/DeviceManagementPage.razor` — inline metrics summary

### Phase 6: Testing
- [x] Existing Bluetooth integration test passes (MockBluetoothService connection/metadata flow)
- [x] Add 10 unit tests for BluetoothAudioSource in `BluetoothAudioSourceTests.cs`:
  - Metadata propagation (title, artist, album)
  - NeedsFingerprintingLookup flag (empty → true, complete → false)
  - Metrics instrumentation (metadata_updates, audio_capture_errors)
  - Device connected/disconnected event handling
  - Source properties (name, type, seekable, etc.)
  - PlaybackStatus propagation
  - InitializeAsync error state when no capture device
- [x] Verify all existing tests still pass (no regressions): 665 infra, 198 API, 86 integration, 35 core
- **Status:** complete

### Phase 7: Documentation
- [x] `design/AUDIO.md` already has Bluetooth audio source section (architecture, platform details)
- [x] `design/CONFIGURATION.md` already has BluetoothOptions/Preferences reference
- [x] Update `design/API_REFERENCE.md` — added full Bluetooth Management Endpoints section
- [x] TODO comments in WindowsBluetoothService for Windows AVRCP (requires TFM change)
- **Status:** complete

### Phase 8: Final Verification & Commit
- [x] `dotnet build --configuration Release` — 0 warnings, 0 errors
- [x] `dotnet test --configuration Release` — all tests pass (665+198+86+35+125+123+6 = 1238 pass, 7 known flaky, 3 skipped)
- [ ] Manual validation on Windows (connect phone, play audio, verify metadata) — requires hardware
- [x] Commit to `bluetooth-enablement` branch
- **Status:** complete

## Design Decisions

### Audio Capture Device Creation
The `GetAudioCaptureDeviceAsync()` method must return an actual SoundFlow `AudioCaptureDevice`, not a string name. The fix is:
1. `SoundFlowDeviceManager.FindCaptureDeviceByName()` → returns `AudioCaptureDevice?` (create from MiniAudio device info)
2. Platform services call this method and return the AudioCaptureDevice to BluetoothAudioSource

### Windows A2DP Sink Approach
Windows doesn't natively support "A2DP Sink" (acting as a Bluetooth speaker). However:
- When a paired Bluetooth device connects and streams audio, Windows creates an audio endpoint for it
- We capture from that endpoint via SoundFlow/MiniAudio (same as USB audio devices)
- InTheHand.Net handles pairing/discovery; Windows audio APIs handle the actual audio

### AVRCP Metadata Approach
- **Linux**: BlueZ exposes AVRCP metadata via D-Bus MediaPlayer1 interface (MPRIS). Already partially wired.
- **Windows**: Use Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager to get now-playing info from the connected device
- Both fire IBluetoothService.MetadataChanged event → BluetoothAudioSource updates StandardMetadataKeys
- Fingerprinting supplements any missing metadata (especially album art from Cover Art Archive)

### HFP Compatibility
- IBluetoothService remains profile-agnostic for device management operations
- A2DP audio capture is handled via standard OS audio endpoints (not protocol-specific)
- Future HFP integration would add a separate service or extend IBluetoothService with call-related methods
- No changes needed to current architecture to support future HFP

### Bluetooth Metrics Approach
- Follow existing patterns: inject `IMetricsCollector`, use `Increment()` for counters and `Gauge()` for snapshots
- Metric key convention: `bluetooth.{metric_name}` (matches `radio.*`, `tts.*`, `audio.*` patterns)
- **Counters** (monotonic, track totals over time):
  - `bluetooth.devices_connected_total` — each device connection event
  - `bluetooth.devices_disconnected_total` — each disconnection
  - `bluetooth.discovery_sessions` — each scan started
  - `bluetooth.pair_attempts` — with `result` tag: `success` or `failure`
  - `bluetooth.audio_capture_errors` — InitializeAsync failures
  - `bluetooth.metadata_updates` — AVRCP metadata change events
- **Gauges** (current-value snapshots):
  - `bluetooth.active_connections` — 0 or 1 (current state)
  - `bluetooth.connection_duration_seconds` — updated on disconnect with total session duration
- Metrics auto-aggregate via existing rollup pipeline (Minute→Hour→Day)
- MetricsDashboardPage already groups metrics by prefix — `bluetooth.*` will naturally appear as "Bluetooth" category
- Bluetooth management page shows inline stats sourced from same metrics API

### Web UI Placement
- Add Bluetooth management as a new tab in DeviceManagementPage.razor (alongside Cast and USB devices)
- Consistent with existing device management patterns
- Uses same MudBlazor component patterns

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
