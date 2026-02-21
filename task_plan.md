# Task Plan: Next Session — Media Setup, Dual Output Bug, Architecture Cleanup

## Goal
Complete Ubuntu x64 setup (media + fingerprinting), fix the dual audio output bug where audio plays on both local speakers and Cast simultaneously, and begin architecture cleanup.

## Current Phase
Phase A — Ubuntu Media & Data Setup

---

## Completed (prior sessions)

All prior phases are **COMPLETE**. See git history and previous plan for details:
- Phases 0–11.3: Full system build (audio engine, sources, outputs, API, UI, song change detection)
- Phase A (prev): Cast design evaluation (WebSocket, pre-loaded sounds)
- Phase B (prev): ALSA log noise suppression (SystemdConsoleFormatter + syslog levels)
- Phase C (prev): Ubuntu x64 target setup (deploy script, setup script, app deployed, Cast verified)
- Cast drift testing: v10 receiver drift protection (PR #217), ping endpoint + channel registration (PR #218)
- Cast latency verified: Transit avg 87ms, buffer-ahead steady 3.0s, no drift, no stutter

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

## Phase B: Dual Audio Output Bug Fix 🔄

### B.1 Root Cause Analysis
**Priority:** High — audio should only play on the selected output

**Problem:** When Cast connects, audio plays on BOTH local speakers AND Cast device simultaneously.
This is because the audio pipeline continues feeding the local playback device even when Cast is active.

**Key files to investigate:**
- `src/Radio.API/Controllers/DevicesController.cs` — output switching logic (Cast connect handler)
- `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs` — Cast startup/shutdown
- `src/Radio.Infrastructure/Audio/Outputs/LocalAudioOutput.cs` — local output muting
- `src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs` — HTTP stream activation
- `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs` — engine output management
- `src/Radio.Infrastructure/Audio/AudioManager.cs` — output orchestration

**Current behavior (from DevicesController Cast connect):**
1. `ActivateOutputAsync(_castOutput)` — starts Cast
2. `ActivateOutputAsync(_httpOutput)` — starts HTTP stream (needed for HttpMp3 mode)
3. `_audioEngine.SetLocalOutputMuted(true)` — mutes local output

**Expected behavior:**
- **DirectChannel mode**: Only Cast output active, local muted, HTTP stream NOT needed
- **HttpMp3 mode**: Cast + HTTP stream active, local muted
- **Local mode**: Only local output active, Cast and HTTP stream stopped

**Tasks:**
- [ ] Read and trace the full output switching flow
- [ ] Determine if `SetLocalOutputMuted(true)` actually silences local speakers (or just sets a flag)
- [ ] Check if DirectChannel mode still needs HTTP stream active
- [ ] Implement proper output exclusivity — when Cast connects, fully stop local output (not just mute)
- [ ] When Cast disconnects, restore local output
- [ ] Add tests for output switching behavior

### B.2 Verify Fix
**Tasks:**
- [ ] Build and run all tests
- [ ] Deploy to Ubuntu
- [ ] Start playback → connect Cast → verify ONLY Cast plays
- [ ] Disconnect Cast → verify local speakers resume
- [ ] Test both DirectChannel and HttpMp3 modes

---

## Phase C: Architecture Cleanup ⏸️

### C.1 Extract PlayHistoryTracker from AudioManager
**Priority:** Medium — AudioManager has too many responsibilities

AudioManager's constructor currently takes 18 parameters. PlayHistory tracking can be extracted
into a dedicated service that subscribes to audio events.

**Tasks:**
- [ ] Create `PlayHistoryTracker` class that handles play history recording
- [ ] Move play history logic out of AudioManager
- [ ] Wire up via DI
- [ ] Verify all play history tests still pass

### C.2 Review AudioManager Constructor
**Priority:** Low — depends on C.1

**Tasks:**
- [ ] After C.1, review remaining constructor parameters
- [ ] Identify any other responsibilities that could be extracted
- [ ] Remove completed TODO comments from codebase

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
| Dual audio output (local + Cast) | **Phase B** | Audio plays on both outputs simultaneously |
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
