# Progress Log

## Session: 2026-02-10 — Audio Latency Research & Fixes

### Branch: `audio-latency-research-and-fixes` (from `bluetooth-streaming-audio`)

### Phase 2: Bidirectional Cast Volume Sync (complete)
- Decompiled SharpCaster 3.0.0 with ilspycmd, discovered `ReceiverChannel.ReceiverStatusChanged` event
- GoogleCastOutput: Added `SubscribeToReceiverStatus()`, `SyncInitialVolumeAsync()`, `OnReceiverStatusChanged()`
- Echo prevention: `_suppressNextVolumeEvent` flag set before SetVolume/SetMute, cleared in event handler
- AudioStateUpdateService: subscribes to `CastVolumeChanged`, syncs volume/mute back to IAudioManager

### Phase 3: Bidirectional Bluetooth Volume Sync (complete)
- Added `VolumeChanged` event, `DeviceVolume` property, `SetDeviceVolumeAsync()` to IBluetoothService
- Stub implementations in Windows/Linux/Mock/Null services (events suppress CS0067 warning)
- Infrastructure ready for AVRCP volume when platform support is implemented

### Phase 4: Volume Preference Persistence (complete)
- Extended AudioPreferences with `IsMuted` property (MasterVolume and Balance already existed)
- AudioManager: `ScheduleVolumePersist()` debounces at 500ms, `PersistVolumePreferencesAsync()` writes to config store
- `RestoreVolumePreferences()` in `InitializeAsync()` sets mixer directly (avoids re-persistence trigger)
- Float↔int conversion: MasterVolume 0.0-1.0↔0-100, Balance -1.0..1.0↔-100..100

### Phase 5: Fingerprinting Optimization (complete)
- Added `NeedsFingerprintingLookup` to IAudioSampleProvider, implemented in SoundFlowAudioTap
- BackgroundIdentificationService skips cycle when source has complete metadata
- `RequestImmediateIdentification()` cancels delay CTS for on-demand identification
- FilePlayer calls it on track change (in UpdateMetadataFromFile), BT on incomplete metadata
- Extended duplicate suppression: >0.9 confidence → 30min window (vs 5min default)

### Phase 6: Cast Latency Reduction (complete)
- StreamType.Buffered → StreamType.Live in GoogleCastOutput (biggest impact, ~5-15s)
- Post-launch delay 500ms → 200ms
- FingerprintTap batch 4096 → 1024 samples (85ms → 21ms)
- HTTP client buffer 65536 → 16384 bytes (341ms → 85ms)

### Phase 7: Automated Verification (complete)
- Build: 0 warnings, 0 errors
- Tests: Core 35, RTLSDRCore 125, E2E 6, Infra 689, API 202, Web 130, Integration 82+3 skipped (1 CoverArtArchive network failure — external API unreachable)

### Phase 1: Deep-Dive Pipeline Analysis (complete)
- Created branch `audio-latency-research-and-fixes`
- Deep exploration of Cast audio pipeline: GoogleCastOutput, HttpStreamOutput, SoundFlowAudioEngine, FingerprintTapModifier, TappedOutputStream
- Deep exploration of volume control: AudioManager passthrough to SoundFlow, one-way Cast sync exists, no BT volume, no persistence
- Deep exploration of fingerprinting: 30s interval, 15s capture, dedup suppression, SQLite cache, 1-3 API calls per cycle
- Updated task_plan.md with 7 phases
- Created `design/AUDIO-DATAFLOW.md` — comprehensive pipeline analysis document covering:
  - 7-stage Cast latency breakdown (total ~25s, Chrome buffering dominant at 15-20s)
  - Local playback latency (~43-47ms)
  - Fingerprinting cycle analysis with API call counts and optimization recommendations
  - Volume control pipeline gaps (no bidirectional sync, no persistence)
  - 8 Cast latency optimization options ranked by effort vs. impact
  - StreamType.Live is #1 recommendation (trivial change, potentially 5-15s improvement)
  - Configuration reference (all configurable and hardcoded latency-affecting values)

---

## Session: 2026-02-10 (previous)

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

### Session: 2026-02-10 (continued) — WASAPI Loopback + Album Art

#### Committed: `5eaf0c9` on `bluetooth-streaming-audio`
- **WASAPI Loopback Capture**: WasapiLoopbackCaptureSource, WindowsBluetoothService integration, BluetoothAudioSource generator path, EnableLoopbackCapture config
- **Album Art File Cache**: AlbumArtCacheService (SHA256, 7-day TTL), AlbumArtController, WindowsMediaSessionWatcher saves to file cache
- **Bug fixes**: VisualizationTapModifier in PlayComponentAsync, SMTC state mirroring without IsAudioManagedByPlatform guard, Cast URL resolution
- **Web proxy**: Album art proxy endpoint in Radio.Web Program.cs
- **Tests**: AlbumArtCacheServiceTests (14), AlbumArtControllerTests (4), WasapiLoopbackTests (8)
- **Build**: 0 warnings, 0 errors. Tests: API 202, Infra 689, Web 130 (7 flaky), Integration 82+3 skipped

#### Known bugs (deferred):
1. Album art proxy untested (connection refused on direct API URL)
2. Play history fix needs verification
3. Visualization fix needs verification

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
