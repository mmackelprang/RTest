# Task Plan: Project Completion & Polish

## Goal
Reconcile all project documents, complete remaining verification, fix known issues, and polish for "done" state.

## Current Phase
Phase 8 — Documentation & Polish (reconciliation complete, ready for decisions)

---

## Completed Phases (Original PLAN.md Phases 0-9)

All core development phases are **COMPLETE**:
- Phase 0-2: Setup, Configuration, Core Audio Engine
- Phase 3: Primary Sources — Spotify, Radio (RF320 + SDR), Vinyl, File Player, Bluetooth A2DP, Generic USB
- Phase 4-5: Event Sources (TTS, Audio File), Ducking & Priority
- Phase 6: Audio Outputs — Local, Google Cast, HTTP Stream
- Phase 7: Visualization — FFT Spectrum, VU Meters, Waveform
- Phase 8: API — 16 controllers, 126+ endpoints, SignalR hubs
- Phase 9: Blazor UI — 12 pages, MudBlazor Material 3, shared components

## Completed Phases (Post-Plan Work)

- **RTL-SDR Radio** — Full SDR source with tuning, scanning, AGC, presets
- **Audio Fingerprinting** — Native fpcalc, AcoustID, auto-skip, MusicBrainz
- **Bluetooth A2DP** — Linux BlueZ D-Bus + Windows WinRT, AVRCP metadata/volume/controls, album art
- **Google Cast Fixes** — StreamType.Live, LAME flush fix, pause/resume, local mute, idle recovery
- **Dual-Service Deployment** — radio-api + radio-web systemd services, Pi scripts
- **Play History & Analytics** — Tracking, search, MusicBrainz enrichment
- **Device UX** — Filtering, friendly names, Cast auto-connect
- **Pi Hardware Testing** — 16 debugging PRs (#178-198), confirmed audio pipeline working

## Completed Phases (task_plan.md Phases 1-7)

- Phase 1: Quick Bug Fixes ✅
- Phase 2: BT UX Improvements ✅
- Phase 3: Volume Control Unification ✅
- Phase 4: Cast Latency Reduction ✅
- Phase 5: Pi Verification (partial — see Phase 9) ✅
- Phase 6: Bugs from Pi Testing ✅
- Phase 7: Audio Output UX & Cast Bugs ✅ (PR #198)

---

## Phase 8: Documentation & Polish 🆕

### 8.1 Update PLAN.md
- [ ] Mark Phases 3, 9 as Completed
- [ ] Mark Phases 10-12 with actual status (substantially complete)
- [ ] Add post-plan features section (BT, SDR, Fingerprinting, Cast, Deployment)
- [ ] Update Progress Tracking table with dates
- [ ] Update "Last Updated" date

### 8.2 Update CLAUDE.md
- [ ] Fix "Phase 9 - pending" → completed
- [ ] Add Bluetooth A2DP to solution structure
- [ ] Add dual-service deployment info
- [ ] Add deploy commands

### 8.3 Update README.md
- [ ] Add post-plan features (BT, SDR, Fingerprinting, Cast)
- [ ] Fix "All 13 phases finished!" — Phases 10-12 are partial
- [ ] Add deployment architecture section
- [ ] Update test counts

### 8.4 Investigate FileBrowser metadata TODO
- [ ] Check if `formatInfo.Tags.Genre/Year/TrackNumber` work in current SoundFlow version
- [ ] If so, fix FileBrowser.cs and remove the stale TODO
- [ ] If not, leave documented in FUTURE-WORK.md

---

## Phase 9: Pi Verification (Remaining) 🔄

These require physical Pi hardware and can't be done from Windows dev:

### 9.1 Verify PR #198 fixes on Pi
- [ ] Deploy PR #198 to Pi
- [ ] Cast pause/resume — fixed, verify on Pi
- [ ] BT progress bar — fixed, verify on Pi
- [ ] Device filtering — verify hidden devices don't appear
- [ ] Local mute when casting — verify silence on local, audio on Cast

### 9.2 Original verification items (from Phase 5.2)
- [ ] Restart preference restore (playback-12 auto-selected on reboot)
- [ ] Volume persistence across restart
- [ ] Fingerprint skip after identification
- [ ] Cast latency measurement
- [ ] Sample drop rate after fixes (was 81%)

### 9.3 Untested features
- [ ] Album art proxy (Web port 5002 → API port 5000)
- [ ] BT next/previous — depends on phone AVRCP support

---

## Phase 10: Final Polish (Optional)

### 10.1 Kiosk Mode
- Priority: Medium — needed for final console radio experience
- Deferred until Pi verification complete
- Implementation plan in FUTURE-WORK.md

### 10.2 E2E Tests
- Priority: Low — 1,257 unit/integration tests exist
- Playwright infrastructure in Radio.Web.E2ETests
- Write 5-10 critical path E2E tests

### 10.3 Low-Priority Deferred Items
- TTS audio cache
- Windows AVRCP volume sync (dev-only)
- Radio device switching API

---

## Deferred (not in scope)
- RF320 software control — permanent hardware limitation
- Direct pipe Cast architecture — current HTTP streaming works
- ALSA device enumeration noise — cosmetic

---

## Design Decisions
| Decision | Rationale |
|----------|-----------|
| Group by similarity, not priority | Reduces context switching |
| arecord subprocess for BT capture | MiniAudio ALSA capture stalls with PipeWire pulse plugin |
| PlaybackDeviceSwitched event | Decouples engine from playback service |
| Device visibility in config store | User-configurable per deployment |
| Ordered List for FriendlyNames | Dictionary enumeration order not guaranteed |
| ReadAsync pacing for silence | Prevents tight-loop CPU spin when no audio data |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Race condition: zero-timeout semaphore | Two handlers contend | 30s timeout + generator cache |
| Device switch orphans generators | Modifiers-only re-attach | PlaybackDeviceSwitched event |
| Serilog Warning default | Audio logs silenced | Override `Radio: Information` |
| MiniAudio defaults to null sink | playback-0 = Discard | Persist preference to config store |
| Silence spinning in TappedOutputStream | ReadForReader returns non-zero | ReadAsync override with pacing |
| FriendlyNames non-deterministic order | Dictionary enumeration | Changed to List<DeviceNameMapping> |
| Missing DI registration | Action overload missing AudioOutputOptions | Added Configure<AudioOutputOptions> |
