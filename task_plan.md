# Task Plan: Next Session — Media Setup, Dual Output Bug, Architecture Cleanup

## Goal
Complete Ubuntu x64 setup (media + fingerprinting), fix the dual audio output bug where audio plays on both local speakers and Cast simultaneously, and begin architecture cleanup.

## Current Phase
Phase A — Ubuntu Media & Data Setup (Phases B and C complete)

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

---

## Phase A: Ubuntu Media & Data Setup ⏸️

### A.1 Copy Test Media Files
**Priority:** High — needed for playback and fingerprinting verification

**Tasks:**
- [ ] Identify test media files on Pi or local machine
- [ ] Copy media files to Ubuntu at `/opt/radio-console/media/audio/`
- [ ] Verify file browser API endpoint lists the files

### A.2 Verify File Playback
**Priority:** High

**Tasks:**
- [ ] Start file playback via API on Ubuntu
- [ ] Verify audio output works (local speakers)
- [ ] Verify Cast output works with file playback

### A.3 Verify Fingerprinting
**Priority:** Medium

**Tasks:**
- [ ] Confirm `fpcalc` is installed and working on Ubuntu (`fpcalc --version`)
- [ ] Trigger fingerprint identification on a playing file
- [ ] Verify AcoustID lookup returns correct results
- [ ] Check fingerprint database entries in SQLite

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

## Phase D: Hardware Integrations (on Ubuntu) ⏸️

### D.1 Phonograph (USB Turntable)
- [ ] Connect USB turntable to Ubuntu
- [ ] Verify USB audio device appears in device list
- [ ] Test VinylAudioSource playback

### D.2 RTL-SDR Radio
- [ ] Connect RTL-SDR dongle to Ubuntu
- [ ] Install rtl_fm and dependencies
- [ ] Test SDR audio source

### D.3 Generic USB Audio
- [ ] Test generic USB audio input
- [ ] Verify hot-plug detection

---

## Known Issues (carry-forward)

| Issue | Status | Notes |
|-------|--------|-------|
| Dual audio output (local + Cast) | **Resolved** | Fixed in PR #220 |
| Volume persistence | **Resolved** | Verified on Ubuntu + Pi (PR #223) |
| Pong not received via SharpCaster | Deferred | Channel registration works but pong never arrives; CDP workaround reliable |
| Receiver double-counts messages | Cosmetic | 20/sec vs sender 10/sec after CDP reload; doesn't affect audio |
| Album art proxy untested | Deferred | Web port 5002 → API port 5000 |
| BT play history recording | Needs verification | Fix committed, untested on hardware |
| BT visualization data | Needs verification | Fix committed, untested on hardware |

## Design Decisions
| Decision | Rationale |
|----------|-----------|
| Keep MP3 for Cast streaming | WAV requires Content-Length, incompatible with live streams |
| DirectChannel for primary Cast | Raw PCM → Base64 → JSON eliminates MP3 encode/decode gaps |
| StandardErrorPriority=debug | Cleanest way to suppress C library noise without losing it |
| Parameterize deploy script | Single script for both Pi (arm64) and Ubuntu (x64) targets |
| Use appsettings.Production.json | Deploy script overwrites appsettings.json; Production file survives deploys |
| CDP for Cast metrics | SharpCaster pong unreliable; Chrome DevTools Protocol reads receiver globals directly |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none yet) | | |
