# Work Log

Running log of development sessions, organized chronologically. Each entry captures what was done, key files changed, and any issues encountered. Updated by Claude Code at the end of each session.

---

## 2025-11-25 — Project Setup (Phases 0-1)

**PRs:** #2, #4, #13, #14, #16

- Created project plan with phased development approach
- Set up solution structure: Radio.Core, Radio.Infrastructure, Radio.API, Radio.Web
- Implemented Phase 1 configuration infrastructure (SQLite + JSON dual stores)
- Added ConfigurationManager CLI tool with Spectre.Console interactive menu
- CI/CD with GitHub Actions, CodeQL security scanning

**Key files:** `RadioConsole.sln`, all project scaffolding, `CLAUDE.md`

---

## 2025-11-26 — Core Engine & Audio Sources (Phases 2-5)

**PRs:** #17, #18, #20, #21, #22, #24

- **Phase 2:** Core audio engine with SoundFlow/MiniAudio integration — `SoundFlowAudioEngine`, `SoundFlowMasterMixer`, `TappedOutputStream`
- **Phase 3:** Primary audio sources — `FilePlayerAudioSource`, `RadioAudioSource`, `SpotifyAudioSource`
- **Phase 4:** Event audio sources — TTS (Google, Azure), audio file events
- **Phase 5:** Ducking & priority system — `IDuckingService`, fade policies
- Audio UAT testing tool for manual verification

**Key files:** `SoundFlowAudioEngine.cs`, `SoundFlowPlaybackService.cs`, `FilePlayerAudioSource.cs`, `TappedOutputStream.cs`

---

## 2025-12-03 — 2025-12-05 — API Layer & Queue System

**PRs:** #70-79, #83-86, #88-94, #96, #98-99

- Queue management API and `IPlayQueue` interface
- Spotify controller with search, browse, playback, OAuth PKCE
- Radio controller for RF320 device control
- Now Playing endpoint with structured metadata
- SignalR hub for real-time audio state broadcasting
- Metrics infrastructure with SQLite persistence
- Serilog file sink and system log retrieval API
- API codebase refactor — eliminated duplication, split God class

**Key files:** `AudioController.cs`, `SpotifyController.cs`, `RadioController.cs`, `DevicesController.cs`

---

## 2025-12-09 — 2025-12-12 — RTL-SDR & Web UI Foundation

**PRs:** #103, #105, #107, #110, #112, #114-128

- RTL-SDR audio streaming integration with `SDRRadioAudioSource`
- `RadioFactory` for device type selection (SDR vs RF320)
- Complete Web UI Phases 1-12: Navigation, Playback, Queue, Spotify Browse, File Browser, Radio Controls, System Config, Metrics Dashboard, Audio Visualization, Device Management, Play History
- MudBlazor Material 3 theme, LED fonts, 85/86 API endpoints wired
- bUnit testing infrastructure (35+ tests initially)

**Key files:** All `Radio.Web/Components/` pages, `RTLSDRCore/`

---

## 2025-12-19 — 2025-12-30 — UI Polish & Event Sources

**PRs:** #129-140

- Play History & Analytics UI
- User preference persistence via Configuration REST API
- Queue drag-drop, page transitions, log export
- Event Sources UI (TTS and File audio events)
- Audio engine initialization and startup preferences
- Google TTS voice validation

**Key files:** `Radio.Web/Components/Pages/`, various shared components

---

## 2025-12-31 — 2026-01-05 — Database Integration & Spotify

**PRs:** #142-164

- Database integration, SoundFlow playback improvements
- Configuration UI: device options, preferences, secrets management
- E2E UAT testing framework (43 tests)
- Librespot integration for native Spotify audio streaming
- Phase 12 Material 3 design system
- Queue/preferences persistence, file browser, virtual keyboard

**Key files:** `SpotifyAudioSource.cs`, various configuration stores

---

## 2026-01-07 — UAT Fixes

**PR:** #166

- Audio source switching fixes
- Fingerprinting observability improvements
- UI bug fixes from UAT testing

---

## 2026-02-06 — Fingerprinting & BT Planning

**PRs:** #171, #172

- `FpcalcUtility` for streamed audio fingerprinting (replaced AcoustID.NET — incompatible fingerprints)
- Bluetooth audio input implementation plan

**Key files:** `FpcalcUtility.cs`, `FingerprintTapModifier.cs`

---

## 2026-02-10 — Bluetooth Audio Pipeline

**PRs:** #174, #176

- Bluetooth A2DP audio pipeline: `LinuxBluetoothService`, `WindowsBluetoothService`, `BluetoothAudioSource`
- WASAPI loopback capture for Windows BT audio
- Album art file cache (`AlbumArtCacheService`)
- Cast audio streaming and pipeline optimizations
- Full playlist queue panel with state tracking and auto-skip
- Audio latency research and fixes

**Key files:** `LinuxBluetoothService.cs`, `WindowsBluetoothService.cs`, `BluetoothAudioSource.cs`, `GoogleCastOutput.cs`

---

## 2026-02-11 — Pi Deployment & Initial Testing

**PRs:** #177, #179

- Pi deployment scripts (`deploy-to-pi.sh`, `Deploy-ToPi.ps1`)
- Network binding fixes, missing file fixes
- Removed planning-with-files plugin

---

## 2026-02-12 — Raspberry Pi Debugging Marathon

**PRs:** #178, #180-193

A rapid-fire debugging session deploying and testing on physical Raspberry Pi hardware. 16 PRs in one day fixing issues discovered during real hardware testing:

1. **#178** — Missing `SqliteTTSVoiceRepository` implementation
2. **#180** — Embedded album art extraction via TagLib
3. **#181** — API test timeouts on Pi (use `CustomWebApplicationFactory`)
4. **#182** — D-Bus connection type for BT agent registration (system bus, not session)
5. **#183** — Google TTS `modelName` field required for newer voices
6. **#184** — Pi log noise from drive access and missing media directories
7. **#185** — Audio pipeline metrics + Cast MP3 on ARM64 (NAudio.Lame)
8. **#186** — BT connection drops, playlist race condition, enum serialization
9. **#187** — BluezAgent D-Bus methods not exported (A2DP authorization failures)
10. **#188** — Concurrent `MiniAudioEngine` crash during BT capture search
11. **#189** — Cast disconnect errors, BT event flooding/deduplication
12. **#190** — WirePlumber seat monitoring + BT capture routing for A2DP
13. **#191** — BT SBC codec pinning, visualization tap on MasterMixer, metrics concurrent read crash
14. **#192** — BT capture bridge via `BufferedSoundGenerator`, DI factory fixes
15. **#193** — Metrics flush crash from SQLite transaction/connection mismatch

**Key files changed:** `LinuxBluetoothService.cs`, `SoundFlowDeviceManager.cs`, `SoundFlowPlaybackService.cs`, `GoogleCastOutput.cs`, `HttpStreamOutput.cs`, metrics stores

---

## 2026-02-13 — Cast Streaming & BT UX (Phases 1-4)

**PRs:** #194, #195, #196

### Cast Audio Streaming
- **#194** — Cast audio stops immediately. Root cause: `TappedOutputStream` readers start at current write position with no buffered data. Fix: `CreateReader(readerId, lagBytes)` for immediate burst.
- Also: LAME `Flush()` writes end-of-stream data, killing Cast connection. Fix: flush HTTP output stream only.
- `StreamType.Buffered` vs `StreamType.Live` investigation: Live is correct for infinite streams; Buffered causes Chrome to download ~64KB and go FINISHED.
- CC1AD845 receiver app needs 2-3s to initialize — `LoadAsync` right after `LaunchApplicationAsync` silently fails.

### BT UX Improvements (#195)
- BT progress bar (AVRCP position/duration)
- BT next/prev buttons via `IMediaPlayer1` D-Bus
- BT album art download and cache
- BT play history with real AVRCP metadata
- AVRCP bidirectional volume sync (Linux)
- Cast idle session recovery
- Fingerprint skip after identification

### Dual-Service Deployment (#196)
- Split `radio-console.service` into `radio-api.service` + `radio-web.service`
- Directory layout: `/opt/radio-console/{api,web,data,logs}`
- Cross-compilation with `-f net8.0` for Linux ARM64

**Key files:** `TappedOutputStream.cs`, `GoogleCastOutput.cs`, `HttpStreamOutput.cs`, `BluetoothAudioSource.cs`, deploy scripts

---

## 2026-02-14 — Pi Hardware Verification

**PR:** #197

BT audio pipeline debugging on physical Pi:
1. `arecord` subprocess confirmed capturing real audio (strace: non-zero 24KB writes)
2. Serilog `Default: Warning` was hiding all audio pipeline logs — added `Radio: Information` override
3. Race condition in `GetAudioCaptureDeviceAsync` — two concurrent handlers with 0-timeout semaphore. Fixed with 30s timeout + `_activeGenerator` cache.
4. `SwitchPlaybackDevice` orphaned source components. Fixed with `PlaybackDeviceSwitched` event + subscriber re-attachment in `SoundFlowPlaybackService`.
5. Set device to playback-12 (bcm2835 Headphones), confirmed audio through soundbar

**Verified on Pi:** BT connect → arecord → generator → mixer → playback in <1s, AVRCP metadata flowing, volume sync at 68%, play history updated, audio output to soundbar via 3.5mm.

---

## 2026-02-15 — Phase 7: Audio Output UX

**PR:** #198 (pending review)

### 7.3 Cast Pause/Resume (fixed)
- `AudioController` called `PlayAsync()` for paused sources — for `FilePlayerAudioSource`, `PlayCoreAsync()` stops the current player and creates a new one from scratch. Fixed to call `ResumeAsync()` when source state is Paused.
- `TappedOutputStream.ReadForReader()` returned 0 bytes when empty, causing Cast HTTP stream to stall. Now returns PCM silence (zeroed bytes) to keep streams alive during pause.

### 7.2 Cast Mute Local Output (implemented)
- Decompiled SoundFlow DLL to confirm `SoundComponent.Process()` applies modifiers BEFORE volume. This means setting `MasterMixer.Volume = 0` silences local speakers while audio taps (HTTP/Cast, visualization, fingerprinting) still receive full-volume data.
- Added `IAudioEngine.SetLocalOutputMuted(bool)` — sets `_localOutputMuted` flag, updates playback device volume.
- DevicesController: mutes local on Cast selection, unmutes on local/HTTP selection.

### 7.4 BT Progress Bar (fixed)
- `NowPlayingPanel.razor` required both position AND duration to show progress. BT sources often have position but no duration. Now: seekable slider for seekable sources with duration, read-only progress bar for non-seekable sources with duration, elapsed time only when no duration available.

### 7.1 Device Filtering & Friendly Names (implemented)
- Added `DeviceDisplayOptions` config section: `HiddenDevicePatterns` (regex, default hides PulseAudio monitors) and `FriendlyNames` (substring → display name mapping).
- Applied in `SoundFlowDeviceManager.EnumerateDevices()` during device enumeration.

### 7.5 BT Next/Previous (logging improved)
- Code path is correctly wired: `AudioController` → `BluetoothAudioSource` → `LinuxBluetoothService` → `IMediaPlayer1.NextAsync()/PreviousAsync()`. Actual behavior depends on phone's AVRCP support. Upgraded logging from Debug to Warning with D-Bus path info.

**Key files:** `AudioController.cs`, `DevicesController.cs`, `SoundFlowAudioEngine.cs`, `TappedOutputStream.cs`, `NowPlayingPanel.razor`, `SoundFlowDeviceManager.cs`, `AudioOutputOptions.cs`, `IAudioEngine.cs`

---

<!-- NEW SESSION ENTRIES GO ABOVE THIS LINE -->
