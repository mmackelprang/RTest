# Progress Log

## Session: 2026-03-05 (late — R1 + R4 research)

### R4: BT Active Reconnection — Research Complete

- [x] Analyzed LinuxBluetoothService.cs (1,660 lines) — full D-Bus integration via Tmds.DBus
- [x] Found key gap: IBluetoothService has DisconnectAsync() but NOT ConnectAsync()
- [x] D-Bus IDevice1.ConnectAsync() exists in BluezInterfaces.cs but is unreachable from service contract
- [x] Identified BluetoothPreferences.TrustedDevices list — exists but unused (perfect reconnect target)
- [x] Analyzed BluetoothAutoSwitchService — already handles source activation on DeviceConnected event
- [x] Reviewed PhoneCallClient retry pattern — exponential backoff already in codebase, reusable
- [x] Documented recommended approach: D-Bus Device1.Connect() + exponential backoff (5s→60s cap)
- [x] Documented edge cases: user-initiated disconnect, phone at car, multiple devices, adapter off
- [x] Updated task_plan.md R4 section with full findings

### R1: Unify Config Systems — Research Complete

- [x] Analyzed all 20+ IOptions<T> classes and their binding in AudioServiceExtensions.cs
- [x] Traced full data flow: UI toggle → ConfigurationApiService → ConfigurationController → SQLite store (IOptions stays stale)
- [x] Analyzed SqliteConfigurationStore.cs — flat key-value with section:key format (matches IConfiguration convention)
- [x] Analyzed JsonConfigurationStore.cs — file-per-store with in-memory dictionary
- [x] Analyzed both existing workarounds: SyncFingerprintingOptionsFromStore (mutates IOptions) and DeviceOptionsResolver (reads store)
- [x] Analyzed PreferencesPersistenceService — saves preferences every 30s, special handling for AudioPreferences
- [x] Analyzed ConfigurationController — 14 endpoints, UpdateConfigurationSection writes to store only
- [x] Analyzed SecretResolvingPostConfigureOptions — post-configure hook for secret tag resolution
- [x] Evaluated 4 options: Drop SQLite, Bridge SQLite→IConfiguration, Drop IOptions, Hybrid
- [x] Recommended Option 2: Custom SqliteConfigurationProvider bridging SQLite into IConfiguration pipeline
- [x] Key insight: SQLite key format ("section:key") already matches IConfiguration's colon-delimited paths — migration is natural
- [x] Updated task_plan.md R1 section with full findings and implementation plan

## Session: 2026-03-05 (R2 perf optimization)

### R2: Performance & Memory Deep Dive — Implementation

All 4 phases implemented, build passes (0 warnings), all 1,406 tests pass.

**Phase 1: Audio Thread GC Pressure**
- [x] 1A: SoundFlowAudioTap — ArrayPool for large buffers, reusable chunk buffer, deferred float[] until after silence check
- [x] 1B: BufferedSoundGenerator — GC.CollectionCount moved to LogStats timer, audio callback uses Volatile.Read
- [x] 1C: TappedOutputStream — Two-chunk linear write replaces per-sample modulo (192K→94 ops/sec)

**Phase 2: Redundant Network & Serialization**
- [x] 2A: AudioStateUpdateService — Lightweight snapshot comparison before DTO construction
- [x] 2B: NowPlayingPanel — Poll interval 10s→60s, skip if SignalR event within 30s
- [x] 2C: AudioStateHubService + NowPlayingPanel — Typed SignalR payloads (NowPlayingDto, VolumeDto) used directly

**Phase 3: UI Render Efficiency**
- [x] 3A: NowPlayingPanel — Gain debounce timer reused via .Change() instead of dispose+recreate
- [x] 3B: RadioControlPanel — Presets sorted on load, not per render
- [x] 3C: NowPlayingPanel — Reversed fingerprint events cached in field
- [x] 3D: PlayHistoryTracker — Static readonly fallback string arrays

**Phase 4: Background Service Efficiency**
- [x] 4A: BackgroundIdentificationService — Version-tracked cached status snapshot

**Files modified (9 + 1 test fix):**
- `SoundFlowAudioTap.cs`, `BufferedSoundGenerator.cs`, `TappedOutputStream.cs`
- `AudioStateUpdateService.cs`, `NowPlayingPanel.razor`, `AudioStateHubService.cs`, `MainLayout.razor`
- `RadioControlPanel.razor`, `PlayHistoryTracker.cs`, `BackgroundIdentificationService.cs`
- `AudioStateHubServiceTests.cs` (updated for typed event signatures)

### Key Files Modified
- 10 production files, 1 test file

### Test Results
- `dotnet build --configuration Release` — 0 warnings, 0 errors
- `dotnet test --configuration Release` — 1,406 tests passed across 7 projects

---

## Session: 2026-03-05 (continued)

### Completed This Session

- [x] Investigated why BT fingerprinting wasn't running despite UseShazamForAllSources=true
- [x] Root cause: IOptions<T> reads from appsettings.json (false), UI toggle writes to SQLite (true)
- [x] Fix: Added `SyncFingerprintingOptionsFromStore()` in AudioSourceFactory
- [x] Committed `4235978`, pushed, deployed — BT fingerprinting confirmed working
- [x] Found second bug: BT source stays in Ready state (not Playing) after service restart
- [x] Root cause: PlaybackStatusChanged fires during StartAsync() before State=Ready
- [x] Fix: Check PlaybackStatus metadata after InitializeAsync sets State=Ready
- [x] Committed `5562fcd`, pushed to main, deployed — BT state transition confirmed working
- [x] Merged PR #294 (Shazam for all sources, GC pressure fixes, instrumentation)
- [x] Added config unification research item (R1) to task_plan.md
- [x] Added distortion/SSH correlation and config store duplication notes to MEMORY.md

### Commits This Session
- `4235978` fix: Sync UseShazamForAllSources from config store to fix BT fingerprinting
- `831c005` docs: Update investigation findings, progress, and config unification research item
- `5562fcd` fix: Race condition where BT source stays Ready when phone is already playing

### Key Files Modified
- `src/Radio.Infrastructure/Audio/Services/AudioSourceFactory.cs` — SyncFingerprintingOptionsFromStore()
- `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` — InitializeAsync race fix

### Test Results
- `dotnet build --configuration Release` — 0 warnings
- `dotnet test --configuration Release` — 75 tests passed, 3 skipped
- BT fingerprinting confirmed working on Ubuntu deploy
- BT state transition to Playing confirmed working after restart

## Session: 2026-03-05 (earlier)

### Phase 1: Analysis & Research

- [x] Extracted 151 AUDIO_DISTORTION_MARKER events from journalctl (14:05-15:42)
- [x] Mapped events to service PIDs / restart times (4 service instances in window)
- [x] Verified no clipping (all isClipping=False, peaks -6 to -11 dBFS)
- [x] Found PipeWire-pulse overrun events at 14:01:32 (MiniAudio output xrun)
- [x] Analyzed BufferedSoundGenerator stats — received > output (growing), no drops/compensations
- [x] Checked BT format: S24LE 48kHz 2ch (PipeWire converts to S16LE for our stream)
- [x] Read BufferedSoundGenerator code: lock contention between AddSamples + GenerateAudio
- [x] Read PipeWireNativeStream code: OnProcess on PW thread, Marshal.ReadInt16 per-sample conversion
- [x] Noted source state anomaly: 49% "Ready" during active audio playback
- [x] Wrote findings.md with ranked possible root causes

### Phase 2-3: GC Pressure + Instrumentation (previous context window)

- [x] Lock-free BufferedTapModifier (eliminated 192K lock ops/sec)
- [x] Pre-allocated mono buffer in VisualizerService
- [x] Cached metrics tags in BufferedSoundGenerator
- [x] GC SustainedLowLatency mode in SoundFlowAudioEngine
- [x] Removed DateTime.UtcNow from TappedOutputStream hot path
- [x] Span-based S16→float conversion in PipeWireNativeStream
- [x] Callback timing instrumentation in BufferedSoundGenerator
- [x] Limiter engagement instrumentation in LimiterModifier
- [x] Throttled per-miss logging (fixed journald feedback loop at 91.7% CPU)
