# Progress Log

## Session: 2026-02-10

### Phase 1: Audio Capture Pipeline Fix (complete)
- Fixed `SoundFlowDeviceManager.FindCaptureDeviceByName` return type: `object?` → `DeviceInfo?`
- WindowsBluetoothService: Rewrote `GetAudioCaptureDeviceAsync` with 3-strategy device search + MiniAudioEngine lifecycle
- LinuxBluetoothService: Injected SoundFlowDeviceManager, implemented audio capture with bluez/device name/bluetooth strategies
- BluetoothAudioSource: Added `IOptionsMonitor<BluetoothOptions>`, `NeedsFingerprintingLookup`, device event handlers

### Phase 2: Platform Service Completeness (complete)
- Windows: Changed DeviceConnected/DeviceDisconnected from `{ add {} remove {} }` to proper events
- Windows: Added `CheckForConnectionChanges()` polling for connect/disconnect/switch detection
- Windows: DisconnectAsync now fires events and recreates BluetoothClient
- Linux: Implemented PairDeviceAsync, UnpairDeviceAsync, DisconnectAsync via D-Bus
- Linux: Added WatchDevicePropertiesAsync for connection state tracking

### Phase 3: AVRCP Metadata & Album Art (complete)
- Added `AlbumArtUrl` to `BluetoothPlaybackMetadata`
- Linux: MPRIS watcher extracts ArtUrl/mpris:artUrl from track properties
- Windows: AVRCP requires Windows-specific TFM — left as TODO, fingerprinting is fallback
- BluetoothAudioSource: Propagates AlbumArtUrl, sets NeedsFingerprintingLookup

### Phase 4: Web UI (complete)
- Created `BluetoothApiService.cs` with 8 API methods
- Added `BluetoothStatusDto`, `BluetoothDeviceDto` to ApiModels.cs
- Registered HttpClient in Program.cs
- Created `BluetoothPage.razor` at `/bluetooth` with full management UI
- Added conditional Bluetooth nav icon in MainLayout

### Phase 5: Bluetooth Metrics (complete)
- Injected `IMetricsCollector` into WindowsBluetoothService, LinuxBluetoothService via BluetoothServiceFactory
- 8 metrics instrumented: devices_connected_total, devices_disconnected_total, active_connections, discovery_sessions, pair_attempts, audio_capture_errors, connection_duration_seconds, metadata_updates
- MetricsDashboardPage auto-groups `bluetooth.*` as "Bluetooth" category

### Phase 6: Testing (complete)
- Created `BluetoothAudioSourceTests.cs` with 10 unit tests (all pass)
- Added `SimulateDisconnection` helper to MockBluetoothService
- Full suite: 665 infra (+10 new), 198 API, 86 integration, 35 core — all pass

### Phase 7: Documentation (complete)
- Added Bluetooth Management Endpoints section to API_REFERENCE.md (8 endpoints documented)
- AUDIO.md and CONFIGURATION.md already had Bluetooth sections

### Phase 8: Final Verification (complete)
- Build: 0 warnings, 0 errors
- Tests: 1238 pass, 7 known flaky, 3 skipped

### Files Changed
| File | Action |
|------|--------|
| `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowDeviceManager.cs` | Modified — FindCaptureDeviceByName return type |
| `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs` | Modified — audio capture, events, connection tracking, metrics |
| `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` | Modified — audio capture, pair/unpair/disconnect, D-Bus watcher, metrics |
| `src/Radio.Infrastructure/Platform/Bluetooth/BluetoothServiceFactory.cs` | Modified — pass IMetricsCollector |
| `src/Radio.Infrastructure/Platform/Bluetooth/MockBluetoothService.cs` | Modified — added SimulateDisconnection |
| `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` | Modified — options, events, metadata, fingerprinting, metrics |
| `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs` | Modified — AlbumArtUrl on BluetoothPlaybackMetadata |
| `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs` | Modified — pass IMetricsCollector to factory |
| `src/Radio.Web/Services/ApiClients/BluetoothApiService.cs` | **New** — Bluetooth API client |
| `src/Radio.Web/Models/ApiModels.cs` | Modified — Bluetooth DTOs |
| `src/Radio.Web/Program.cs` | Modified — register BluetoothApiService |
| `src/Radio.Web/Components/Pages/BluetoothPage.razor` | **New** — Bluetooth management page |
| `src/Radio.Web/Components/Layout/MainLayout.razor` | Modified — Bluetooth nav icon |
| `tests/Radio.Infrastructure.Tests/Audio/BluetoothAudioSourceTests.cs` | **New** — 10 unit tests |
| `design/API_REFERENCE.md` | Modified — Bluetooth endpoints section |
