# Task Plan: Session 2026-02-19 — Cast Polish, Log Hygiene, Ubuntu Target Setup

## Goal
Evaluate Cast latency improvements from discussion, suppress ALSA log noise, and prepare the new x64 Ubuntu machine as the final deployment target for Grandpa's Radio.

## Current Phase
Phase A — Cast Design Evaluation & Quick Fixes (this session)

---

## Completed Phases (summary)

All prior phases (0-11.3) are **COMPLETE**. See git history for details:
- Phases 0-10: Full system build (audio engine, sources, outputs, API, UI)
- Phase 11.1: Spotify removal
- Phase 11.2: Cast latency reduction (custom receiver, throttling, debounce, buffer depth)
- Phase 11.3: Continuous source fingerprinting (song change detection)

---

## Phase A: Cast Design Evaluation ✅

### A.1 Evaluate WebSocket + Web Audio API Approach
**Priority:** Research — informs future Cast architecture

**Context from `Google-Cast-Latency-Discussion.md`:**
The discussion describes a fundamentally different approach to Cast audio:
- **Current approach:** HTTP progressive MP3 stream → Chrome `<audio>` element (via CAF)
  - Latency: ~3-10s (after our recent fixes)
  - Advantage: Simple, uses standard Cast media playback
- **WebSocket approach:** WebSocket + Web Audio API (`AudioContext.decodeAudioData`)
  - Latency: Sub-500ms theoretically
  - Disadvantage: Complex, requires full custom receiver rewrite

**Key insight from the discussion:**
> PCM WAV format "should" work with `decodeAudioData()` — but this is via WebSocket,
> NOT via Chrome's `<audio>` element. The `<audio>` element still can't handle chunked WAV.

**WAV via standard Cast (tested/confirmed):**
- ❌ Our `/stream/audio` WAV endpoint returns `200 audio/wav` but uses chunked transfer encoding
- ❌ WAV requires Content-Length (seekable file format), incompatible with infinite live streams
- ❌ Chrome's `<audio>` element cannot play chunked WAV streams
- ✅ MP3 is frame-based and inherently supports progressive streaming
- **Conclusion: MP3 remains the correct choice for standard Cast media loading**

**WebSocket approach evaluation:**
- Would replace CAF's `<audio>` element with raw `AudioContext` playback
- Each PCM chunk wrapped in a 44-byte WAV header, sent via WebSocket
- Receiver decodes with `decodeAudioData()` and schedules via `BufferSource.start()`
- Requires: WebSocket server in C#, complete receiver rewrite, chunk scheduling logic
- **Verdict: Document as future option in FUTURE-WORK.md, don't implement now**
  - Current ~3-10s latency is acceptable for music playback
  - Sub-500ms latency only matters for intercom/doorbell use cases
  - Significant complexity vs marginal benefit for current use case

**Tasks:**
- [x] Read and analyze the discussion document
- [x] Test WAV endpoint accessibility (200 OK, audio/wav)
- [x] Confirm WAV cannot work with standard Cast media loading
- [x] Document WebSocket approach in `design/FUTURE-WORK.md`
- [x] Test mixed content (ws:// and wss://) — both BLOCKED by Chrome on Cast
- [x] Revert test commits from main, clean up receiver.html

### A.2 Pre-loaded Event Sounds (from discussion)
**Priority:** Low — document for future

The discussion also describes pre-loading sound files on the receiver for instant playback via custom namespace messages. This maps well to our TTS/AudioFile event sources.

**Tasks:**
- [x] Document pre-loaded sounds approach in `design/FUTURE-WORK.md`

---

## Phase B: ALSA Log Noise Suppression ✅ (service file updated, deploy pending)

### B.1 Systemd Service — Stderr Priority Demotion
**Priority:** High — logs are currently 99% noise

**Problem:** MiniAudio probes JACK, PulseAudio, OSS, and ALSA dmix backends every ~5 seconds.
This produces ~16 lines of stderr per probe cycle, completely drowning real application logs.

**Root cause:** These messages come from C libraries (libasound, libjack) writing to stderr.
They're NOT from our application's Serilog logging. Systemd captures both stderr and stdout
and mixes them in the journal at the same priority level.

**Fix:** Add `StandardErrorPriority=debug` to the systemd service file. This demotes
all C library stderr messages to debug level, while application logs (via Serilog → stdout)
remain at info/notice level.

**Tasks:**
- [x] Update `deploy/common/radio-api.service`:
  - Add `StandardOutput=journal`
  - Add `StandardError=journal`
  - Add `StandardErrorPriority=debug` (demotes ALSA noise to debug level)
- [ ] Deploy updated service file to Pi and verify:
  - `journalctl -u radio-api -p info -f` shows only app logs
  - `journalctl -u radio-api -p debug -f` shows everything (if needed)
- [ ] Apply same fix to new Ubuntu machine service file (will happen automatically via C.3 setup)

**Verification:**
```bash
# Should show ONLY application logs (no ALSA/JACK noise):
journalctl -u radio-api -p info --since "1 minute ago"

# Should show everything including ALSA noise (for debugging):
journalctl -u radio-api -p debug --since "1 minute ago"
```

---

## Phase C: Ubuntu x64 Target Setup 🔄 (script ready, manual sudo required)

### C.1 Machine Profile
**Host:** `mmack@radio`
**Specs:** Intel N100 (4 cores), 3.6GB RAM, 116GB NVMe, Ubuntu 24.04 LTS
**.NET:** 8.0 SDK already installed (also has .NET 10 SDK)

### C.2 Parameterize Deploy Script
**Priority:** High — needed before any deployment

Current `Deploy-ToPi.ps1` hardcodes `linux-arm64`. Need to support both targets.

**Tasks:**
- [x] Add `-Runtime` parameter (default: `linux-arm64`, validates: `linux-arm64`/`linux-x64`)
- [x] Add `-TargetHost` alias for `-PiHost` (backward compat via `[Alias]`)
- [x] Update publish directories to use dynamic RID: `publish/$Runtime/{api,web}`
- [x] Update restore commands to use dynamic RID
- [x] Created `Deploy-ToLinux.ps1` as main script, `Deploy-ToPi.ps1` is thin wrapper
- [x] Add Production config deployment for x64 (`deploy/debian-x64/appsettings.Production.json`)

### C.3 Run Setup Script on Ubuntu Machine
**Priority:** High

The `deploy/debian-x64/setup.sh` already exists and handles:
- System packages (libasound2-dev, libmp3lame-dev, bluez, pulseaudio, avahi)
- Application user creation (radio)
- Directory structure (/opt/radio-console/*)
- ALSA config (.asoundrc)
- fpcalc installation
- systemd service installation

**Tasks:**
- [x] Copy setup script + service files to Ubuntu machine (`/tmp/radio-deploy/`)
- [ ] Run setup script via SSH (requires sudo password — user must run manually)
- [ ] Verify all packages installed correctly
- [ ] Verify `radio` user created with correct groups
- [ ] Verify systemd services installed and enabled
- [ ] ALSA log suppression already included in service file (Phase B fix)
- [ ] Check audio devices: `aplay -l`, `arecord -l`
- [ ] Check Bluetooth status: `bluetoothctl show`

### C.4 Deploy Application
**Priority:** High — follows C.2 + C.3

**Tasks:**
- [ ] Build for linux-x64 using updated deploy script
- [ ] Deploy to `mmack@radio:/opt/radio-console`
- [ ] Create `appsettings.Production.json` for Ubuntu target
- [ ] Start services and verify:
  - [ ] radio-api starts and initializes audio engine
  - [ ] radio-web starts and connects to API
  - [ ] API responds at `http://radio:5000/swagger`
  - [ ] Web UI loads at `http://radio:5002`
  - [ ] Cast device discovery works
  - [ ] Audio output works (local speakers)
- [ ] Update MEMORY.md with new machine SSH info

### C.5 Media & Data Setup
**Priority:** Medium — needed for testing

**Tasks:**
- [ ] Copy test media files to `/opt/radio-console/media/audio/`
- [ ] Verify file playback works
- [ ] Verify fingerprinting works (fpcalc + AcoustID)

---

## Phase D: Remaining from Previous Plan (deferred)

### D.1 Phase 11.4 — Minor Architecture Cleanup
- [ ] Extract `PlayHistoryTracker` from AudioManager
- [ ] Review AudioManager constructor (18 params)
- [ ] Remove completed TODO comments

### D.2 Phases 12-14 — Hardware Integrations (on new Ubuntu machine)
- Phase 12: Phonograph (USB turntable)
- Phase 13: RTL-SDR Radio
- Phase 14: Generic USB Audio
These will be done on the new Ubuntu x64 machine once it's set up.

---

## Design Decisions
| Decision | Rationale |
|----------|-----------|
| Keep MP3 for Cast streaming | WAV requires Content-Length, incompatible with live streams |
| Defer WebSocket Cast approach | Sub-500ms latency not needed for music; significant complexity |
| StandardErrorPriority=debug | Cleanest way to suppress C library noise without losing it |
| Parameterize deploy script | Single script for both Pi (arm64) and Ubuntu (x64) targets |
| Ubuntu uses PulseAudio | Simpler than Pi's PipeWire stack; standard for desktop Ubuntu |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Cast 18s rebuffering | maxAheadSeconds=3 too tight | Increased to 10, lag to 5s |
| WAV chunked to Cast | Chrome can't handle chunked WAV | Confirmed MP3 is correct |
