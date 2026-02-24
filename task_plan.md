# Task Plan: Radio Console — Ongoing Development

## Goal
Continue feature development, hardware integration testing, and bug fixes for the Radio Console project on Ubuntu x64 and Raspberry Pi targets.

## Current Phase
All planned phases complete. Remaining work: hardware-dependent items (D.1, D.3) and BT verification.

---

## Completed (prior sessions)

All prior phases are **COMPLETE**. See git history and previous plan for details:
- Phases 0–11.3: Full system build (audio engine, sources, outputs, API, UI, song change detection)
- Phase A (prev): Cast design evaluation (WebSocket, pre-loaded sounds)
- Phase B (prev): ALSA log noise suppression (SystemdConsoleFormatter + syslog levels)
- Phase C (prev): Ubuntu x64 target setup (deploy script, setup script, app deployed, Cast verified)
- Cast drift testing: v10 receiver drift protection (PR #217), ping endpoint + channel registration (PR #218)
- Cast latency verified: Transit avg 87ms, buffer-ahead steady 3.0s, no drift, no stutter
- Dual output bug fix: Cast connect now calls SetLocalOutputMuted(true), added disconnect endpoint (PR #220)
- Architecture cleanup — AudioManager reduced from 18 params / ~1200 lines to 6 params / 472 lines:
  - Extract PlayHistoryTracker (PR #221)
  - Extract AudioSourceFactory (PR #222)
  - Extract AudioPreferencePersistence + BluetoothAutoSwitchService, delete dead RestoreLastSourceAsync (PR #223)
- Volume persistence verified on Ubuntu + Pi (survives service restarts)
- Inline radio control panel with broadcast console styling (PR #228)
- FM audio dropout fix — squelch silence delivery (PR #229)
- FM audio dropout fix — async DSP processing + startup reorder (PR #230)
- Radio scan fix — no USB restart during frequency changes (PR #231)
- Hidden devices persistence — load from config store on startup (PR #232)
- Ubuntu media & data setup verified (Phase A)

---

## Phase A: Ubuntu Media & Data Setup ✅

### A.1 Copy Test Media Files ✅
- [x] 28 media files already present at `/opt/radio-console/media/audio/`
- [x] File browser API (`GET /api/files`) lists all files with metadata (artist, album, duration)

### A.2 Verify File Playback ✅
- [x] `POST /api/files/play` starts playback — verified with "All The Small Things" (Blink-182) and "Here's To The Night" (Eve 6)
- [x] Audio output working (local speakers)
- [ ] Cast output with file playback — not retested this session (Cast verified previously with SDR)

### A.3 Verify Fingerprinting ✅
- [x] `fpcalc` v1.5.1 installed and working on Ubuntu
- [x] Fingerprint identification working — "All the Small Things" identified at 100% confidence (from cache)
- [x] AcoustID lookup working — "Here's To The Night" fingerprinted via AcoustID
- [x] 358 fingerprint entries in SQLite `FingerprintCache` table
- [x] Play history recording working — entries logged with metadata source (FileTag, Fingerprinting)

---

## Phase B: Dual Audio Output Bug Fix ✅

Fixed in PR #220. Cast connect now calls `SetLocalOutputMuted(true)`, added `POST /api/devices/cast/disconnect` endpoint that restores local output.

---

## Phase C: Architecture Cleanup ✅

Completed across PRs #221–#223. AudioManager reduced from 18 constructor params / ~1200 lines to 6 params / 472 lines.

- **PR #221**: Extracted `PlayHistoryTracker` — subscribes to source state changes, fingerprint identification, and BT AVRCP metadata
- **PR #222**: Extracted `AudioSourceFactory` — encapsulates all source-creation dependencies
- **PR #223**: Extracted `AudioPreferencePersistence` (debounced volume/source saves) and `BluetoothAutoSwitchService` (auto-switch on connect, startup pre-warm). Deleted dead `RestoreLastSourceAsync`. Removed redundant `_bluetoothService.DisposeAsync()` from AudioManager.

AudioManager now has 6 constructor params: `ILogger`, `IAudioEngine`, `IAudioSourceFactory`, `BackgroundIdentificationService?`, `AudioPreferencePersistence?`, `PlayHistoryTracker?`

---

## Phase D: Hardware Integrations (on Ubuntu) — Partial ✅

### D.1 Phonograph (USB Turntable) ⏸️
- [ ] Connect USB turntable to Ubuntu
- [ ] Verify USB audio device appears in device list
- [ ] Test VinylAudioSource playback

### D.2 RTL-SDR Radio ✅
- [x] Connect RTL-SDR dongle to Ubuntu
- [x] Test SDR audio source — FM playback working, zero-dropout audio
- [x] Fix FM audio dropouts — squelch silence delivery (PR #229)
- [x] Fix FM audio dropouts — async DSP queue decouples USB reads from processing (PR #230)
- [x] Verified 5+ minutes continuous FM at 98 MHz with zero buffer underruns

### D.3 Generic USB Audio ⏸️
- [ ] Test generic USB audio input
- [ ] Verify hot-plug detection

---

## Phase D.5: Radio Control Panel UI ✅

- **PR #228**: Inline radio control panel with broadcast console "Command Surface" styling
  - Band selector, DSEG frequency display, signal strength bar, tuning/scan controls, AGC controls, presets sidebar
  - Touch-first kiosk design (1920x576px fixed viewport)

---

## Phase E: Radio Scan Fix & UI Improvements ✅

### E.1 Fix Scan Buttons ✅ (PR #231)
- [x] Root cause: `SetFrequencyInternal` stopped/restarted USB streaming per frequency step (~300ms overhead)
- [x] Fix: removed USB stop/restart (`rtlsdr_set_center_freq` is safe while streaming), increased dwell to 150ms
- [x] Verified: scan up from 88 MHz found 90.1 MHz, scan down from 107 MHz found 106.9 MHz

### E.2 Frequency Updates During Scan ✅ (PR #231)
- [x] `AudioStateUpdateService` polls every 500ms and broadcasts `RadioStateChanged` via SignalR when frequency changes
- [x] RadioControlPanel DSEG display updates live during scan
- [x] Scan stops and holds on station when signal strength exceeds threshold

### E.3 Hidden Devices Persistence ✅ (PR #232)
- [x] Root cause: `SoundFlowDeviceManager` only loaded from `IOptionsMonitor` (appsettings.json), not SQLite config store
- [x] Fix: `LoadDisplaySettingsFromStoreAsync()` called from `AudioEngineInitializationService.StartAsync`

---

## Known Issues (carry-forward)

| Issue | Status | Notes |
|-------|--------|-------|
| Scan buttons not working | **Resolved** | Fixed in PR #231 — no USB restart during freq changes |
| Scan frequency not updating in UI | **Resolved** | Working via SignalR (PR #231) |
| Hidden devices not persisting | **Resolved** | Load from config store on startup (PR #232) |
| Dual audio output (local + Cast) | **Resolved** | Fixed in PR #220 |
| Volume persistence | **Resolved** | Verified on Ubuntu + Pi (PR #223) |
| FM audio dropouts | **Resolved** | Async DSP queue + startup reorder (PRs #229, #230) |
| Pong not received via SharpCaster | Deferred | Channel registration works but pong never arrives; CDP workaround reliable |
| Receiver double-counts messages | Cosmetic | 20/sec vs sender 10/sec after CDP reload; doesn't affect audio |
| Album art proxy untested | Deferred | Web port 5002 → API port 5000 |
| BT play history recording | Needs verification | Fix committed, untested on hardware |
| BT visualization data | Needs verification | Fix committed, untested on hardware |

## Design Decisions
| Decision | Rationale |
|----------|-----------|
| Async DSP processing queue | USB read loop blocked by synchronous DSP caused 3.6% throughput deficit; BlockingCollection + dedicated thread eliminates it |
| 1s pre-fill silence cushion | RTL-SDR USB device takes ~1s to start delivering data; pre-fill keeps mixer fed during startup |
| Keep MP3 for Cast streaming | WAV requires Content-Length, incompatible with live streams |
| DirectChannel for primary Cast | Raw PCM → Base64 → JSON eliminates MP3 encode/decode gaps |
| StandardErrorPriority=debug | Cleanest way to suppress C library noise without losing it |
| Parameterize deploy script | Single script for both Pi (arm64) and Ubuntu (x64) targets |
| Use appsettings.Production.json | Deploy script overwrites appsettings.json; Production file survives deploys |
| CDP for Cast metrics | SharpCaster pong unreliable; Chrome DevTools Protocol reads receiver globals directly |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| FM audio chronic underruns (3.6% silence) | Pre-fill buffer only | Insufficient — buffer drained after ~24s due to synchronous DSP overhead |
| FM audio chronic underruns (3.6% silence) | Async DSP queue + startup reorder | **Fixed** — zero underruns over 5+ min (PR #230) |
