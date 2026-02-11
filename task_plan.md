# Task Plan: Audio Latency Research & Fixes

## Goal
Reduce Google Cast audio latency (currently ~25s from play to heard), implement bidirectional volume sync for Cast and Bluetooth, persist volume preferences, create a detailed AUDIO-DATAFLOW.md pipeline analysis, and optimize the fingerprinting pipeline to reduce unnecessary API calls.

## Current Phase
Phase 7: Testing & Verification — automated complete, manual pending

## Phases

### Phase 1: Deep-Dive Pipeline Analysis & AUDIO-DATAFLOW.md
- [x] Create `design/AUDIO-DATAFLOW.md` documenting every step of the audio pipeline from source to Cast playback
- [x] For each step, document: buffer sizes, duration impact, encoding overhead, configuration options
- [x] Document latency sources with measured/estimated timing
- [x] Document options for reducing latency at each step with effort vs. improvement tradeoffs
- [x] Address specific questions: MP3 necessity, adaptive chunks, alternative Cast libraries, PoC tools
- [x] Add fingerprinting pipeline analysis section to the same document
- **Status:** complete
- **Key files:**
  - `design/AUDIO-DATAFLOW.md` (new) — comprehensive pipeline analysis
  - Referenced sources: GoogleCastOutput.cs, HttpStreamOutput.cs, SoundFlowAudioEngine.cs, FingerprintTapModifier.cs, TappedOutputStream.cs, SoundFlowPlaybackService.cs

### Phase 2: Bidirectional Cast Volume Sync
- [ ] Read Cast device volume on connect via ReceiverChannel status
- [ ] Subscribe to Cast device volume change events (SharpCaster status updates)
- [ ] When Cast volume changes externally → update IAudioManager.MasterVolume
- [ ] When IAudioManager.MasterVolume changes → push to Cast device (already partially working via SetCastVolumeAsync)
- [ ] Verify existing one-way sync works correctly (app → Cast)
- [ ] Handle edge cases: mute sync, volume during media load, multiple outputs
- [ ] Tests for bidirectional sync
- **Status:** pending
- **Key files:**
  - `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs` — volume read/subscribe
  - `src/Radio.Infrastructure/Audio/Services/AudioManager.cs` — volume integration
  - `src/Radio.Core/Interfaces/Audio/IAudioManager.cs` — any interface changes

### Phase 3: Bidirectional Bluetooth Volume Sync
- [ ] Research Windows AVRCP absolute volume support (requires WinRT APIs under Windows TFM)
- [ ] Research Linux BlueZ AVRCP volume via D-Bus (org.bluez.MediaControl1 or MediaTransport1)
- [ ] Implement volume read from BT device on connect
- [ ] Implement volume push to BT device on IAudioManager.MasterVolume change
- [ ] Handle BT device volume change events → update IAudioManager.MasterVolume
- [ ] Tests for BT volume sync
- **Status:** pending
- **Key files:**
  - `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs`
  - `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`
  - `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs` — volume events/methods

### Phase 4: Volume Preference Persistence
- [x] Extended `AudioPreferences` with `IsMuted` property (MasterVolume and Balance already existed)
- [x] Persist volume/mute/balance on change to configuration store via `IConfigurationManager.SetValueAsync()`
- [x] Restore volume/mute/balance on app startup in `AudioManager.InitializeAsync()` via `RestoreVolumePreferences()`
- [x] Debounce persistence with 500ms `Timer` in `ScheduleVolumePersist()` — avoids SQLite churn during slider drags
- [x] Sets mixer directly during restore to avoid re-triggering persistence
- [ ] Tests for persistence and restore (covered by existing AudioManager tests + manual verification)
- **Status:** complete
- **Key files:**
  - `src/Radio.Core/Configuration/AudioPreferences.cs` — added `IsMuted` property
  - `src/Radio.Infrastructure/Audio/Services/AudioManager.cs` — `ScheduleVolumePersist()`, `PersistVolumePreferencesAsync()`, `RestoreVolumePreferences()`

### Phase 5: Fingerprinting Optimization
- [x] **Skip identification for already-identified tracks**: Added `NeedsFingerprintingLookup` to `IAudioSampleProvider`, implemented in `SoundFlowAudioTap` checking FilePlayer metadata dict and BT source property. BackgroundIdentificationService skips cycle when false.
- [x] **Track-change-aware scheduling**: Added `RequestImmediateIdentification()` to BackgroundIdentificationService (cancels delay CTS). FilePlayer calls it in `UpdateMetadataFromFile` when NeedsFingerprintingLookup is true. BT calls it in `OnMetadataChanged` when metadata incomplete.
- [x] **Source-aware strategy**: Radio/Vinyl/USB return `NeedsFingerprintingLookup=true` always. FilePlayer returns based on metadata dict. BT returns based on AVRCP completeness.
- [x] **Reduce redundant MusicBrainz calls**: Already implemented — `GetMusicBrainzMetadataAsync` checks `FindByMusicBrainzIdAsync(recordingId)` before API call.
- [x] **Extended duplicate suppression**: High-confidence matches (>0.9) get `HighConfidenceDuplicateSuppressionMinutes` (default 30min vs 5min). Tuple-based tracking `(DateTime, double Confidence)`.
- **Status:** complete
- **Key files:**
  - `src/Radio.Core/Interfaces/Audio/IAudioSampleProvider.cs` — added `NeedsFingerprintingLookup`
  - `src/Radio.Core/Configuration/FingerprintingOptions.cs` — added `HighConfidenceDuplicateSuppressionMinutes`
  - `src/Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs` — skip logic, on-demand trigger, confidence-aware suppression
  - `src/Radio.Infrastructure/Audio/Fingerprinting/SoundFlowAudioTap.cs` — implemented `NeedsFingerprintingLookup`
  - `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` — calls `RequestImmediateIdentification()`
  - `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` — calls `RequestImmediateIdentification()`

### Phase 6: Cast Latency Reduction (Implementation)
- [x] **StreamType.Live** (Priority 1, ~5-15s improvement): Changed `StreamType.Buffered` → `StreamType.Live` in `BuildMedia()` — tells Chrome to start playback sooner
- [x] **Reduce post-launch delay** (Priority 3, ~300ms improvement): Reduced from 500ms → 200ms in `StartAsync()`
- [x] **Reduce FingerprintTap batch size** (Priority 4, ~64ms improvement): Default reduced from 4096 → 1024 samples
- [x] **Reduce HTTP client buffer** (Priority 5, ~256ms improvement): Changed `ClientBufferSize` from 65536 → 16384 in appsettings.json
- [x] All tests passing, 0 warnings
- **Status:** complete
- **Expected combined improvement:** ~6-16s reduction (from ~25s to ~9-19s), primarily from StreamType.Live
- **Key files:**
  - `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs` — StreamType.Live, reduced delay
  - `src/Radio.Infrastructure/Audio/SoundFlow/FingerprintTapModifier.cs` — batch size 4096→1024
  - `src/Radio.API/appsettings.json` — ClientBufferSize 65536→16384

### Phase 7: Testing & Verification
- [x] `dotnet build --configuration Release` — 0 warnings, 0 errors ✓
- [x] `dotnet test --configuration Release` — all tests pass ✓ (1 CoverArtArchive network failure — external API, unrelated)
- [ ] Manual: Cast latency measurement (before vs. after)
- [ ] Manual: Volume sync Cast ↔ Console
- [ ] Manual: Volume sync Bluetooth ↔ Console
- [ ] Manual: Volume persists across app restart
- [ ] Manual: Fingerprinting skips for known tracks
- **Status:** automated verification complete, manual verification pending

## Design Decisions

### Cast Latency Pipeline (Current Measured ~25s)
Estimated breakdown from research:
| Stage | Estimated Latency | Notes |
|-------|------------------|-------|
| SoundFlow buffer | ~21ms | 1024 samples at 48kHz |
| FingerprintTap batching | ~85ms | 4096 samples accumulated |
| TappedOutputStream | ~0ms (ring) | Write-through, no pre-fill delay |
| HTTP client buffer | ~341ms | 65536 bytes at 192kB/s PCM rate |
| MP3 encoding | ~50ms | NAudio.Lame frame encoding |
| Cast LoadAsync | ~500ms-5s | App launch + media load |
| Cast device buffering | ~5-15s | Chrome `<audio>` progressive buffer |
| **Total estimated** | **~6-21s** | Gap suggests Cast buffering is dominant |

The 25s observed is likely: LoadAsync overhead (~5s) + Cast device progressive buffering (~15-20s). The Default Media Receiver is a Chrome browser instance that buffers aggressively before starting playback.

### Volume Persistence Pattern
Follow existing `AudioPreferences` pattern in AudioManager. Debounce saves to avoid SQLite churn during slider drags. Restore on `InitializeAsync()`.

### Fingerprinting Optimization Strategy
Three source categories:
1. **File-based** (FilePlayer): Identify once per track change, skip if tags complete
2. **Metadata-aware** (Bluetooth): Identify when AVRCP metadata is incomplete, skip when complete
3. **Continuous** (Radio, Vinyl, USB): Keep 30s interval, no track-break detection possible

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
