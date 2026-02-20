# Progress Log

## Session: 2026-02-19 — Cast Polish, Log Hygiene, Ubuntu Setup

### Pre-planning work:
- Continued Cast latency optimization from previous session
- Increased `maxAheadSeconds` 3→10 and `lagSeconds` 3→5 in HttpStreamOutput
- Increased metadata debounce 1.5s→3s in GoogleCastOutput
- Added `lagSeconds` parameter to `IAudioEngine.CreateStreamReader`
- Verified on Pi: zero rebuffering after initial play (previously 18s gap)
- Committed and pushed: `fix: Increase Cast buffer depth and initial burst to eliminate rebuffering`

### Research completed:
- Read `Google-Cast-Latency-Discussion.md` — WebSocket + Web Audio API approach
- Confirmed WAV cannot work with standard Cast media loading (chunked encoding issue)
- Analyzed ALSA log noise: ~192 lines/min from MiniAudio backend probing
- Profiled new Ubuntu x64 machine: Intel N100, Ubuntu 24.04, .NET 8 SDK installed
- Reviewed deploy scripts and setup scripts for x64 adaptation

### Planning:
- Updated task_plan.md with Phases A-D
- Updated findings.md with Cast analysis, ALSA analysis, Ubuntu profile
- Feature branch rule saved to MEMORY.md

### Phase A status: COMPLETE
- [x] A.1: WebSocket analysis complete — defer to FUTURE-WORK.md
- [x] A.1b: Mixed content testing — ws:// BLOCKED, wss:// (self-signed) BLOCKED
- [x] A.1c: Documented WebSocket approach in FUTURE-WORK.md section 7
- [x] A.1d: Reverted test commits from main, pushed cleanup
- [x] A.2: Documented pre-loaded sounds approach in FUTURE-WORK.md section 8

### Phase B status: COMPLETE (service file updated)
- [x] Added `StandardErrorPriority=debug` to `radio-api.service`
- Will take effect on next deployment to both Pi and Ubuntu

### Phase C status: In progress (blocked on sudo)
- [x] C.2: Deploy script parameterized — `Deploy-ToLinux.ps1` with `-Runtime` param
- [x] C.2: `Deploy-ToPi.ps1` converted to thin wrapper
- [x] C.2: Production config template for debian-x64
- [x] C.3: Setup files copied to Ubuntu machine at `/tmp/radio-deploy/`
- [ ] C.3: User needs to run `sudo /tmp/radio-deploy/debian-x64/setup.sh` manually (password required)
- [ ] C.4: Deploy application (after setup completes)

### Phase D status: Deferred

---

## Session: 2026-02-16 — Architecture Review & Integration Planning

### Research completed:
- 5 parallel research agents explored: Spotify code inventory, audio source architecture, Cast streaming pipeline, fingerprinting/history subsystems, DI/services/config
- Read all audio source base classes, Cast output, HTTP stream output, fingerprinting service

### Key findings:
1. **Audio source hierarchy already well-factored** — `USBAudioSourceBase` handles USB capture, VinylAudioSource is just 57 lines. No base class extraction needed.
2. **Spotify was never fully implemented** — No `SpotifyAudioSource` class, no enum value. Just config, scripts, loopback POC, and docs. Safe to delete entirely.
3. **Cast 25s delay fully mapped** — 5s connection ceremony + 8s first load timeout + 3s retry + 5-8s device buffering. Pre-buffer approach identified as biggest win.
4. **Continuous source gap confirmed** — Only ONE play history entry per session for continuous sources. Need fingerprint-driven song change detection.

### Plan written:
- Phase 11: Spotify removal, Cast latency, continuous source fingerprinting, minor cleanup
- Phase 12-14: Manual Pi integrations (Phonograph, RTL-SDR, Generic USB)
- All detailed in task_plan.md with specific files, line numbers, and verification steps

### Phase 11.1 completed:
- Deleted 14 Spotify files/directories (~2,500 lines)
- Cleaned 15 mixed files (appsettings, controllers, DTOs, CSS, tests, docs)
- Zero Spotify references remain in `src/`
- Build: 0 warnings, all 1305 tests pass

### Phase 11.2 completed:
- Reduced connection ceremony delays (init 2s→1s, timeout 8s→5s, retry 3s→1s)
- Reduced ring buffer overhead (OutputBufferSizeSeconds 2→1, StreamReaderLagSeconds 0.5→0.2)
- Made Cast Application ID configurable via `GoogleCastOutputOptions.ApplicationId`
- Created Custom Web Receiver (`deploy/cast-receiver/receiver.html`) with CAF low-latency config
- Documented Custom Receiver registration in `design/FUTURE-WORK.md`
- MP3 pre-buffer (original Part B/C) deemed unnecessary: StreamReaderLagSeconds + Custom Receiver watermarks achieve same goal
- Build: 0 warnings, all 1305 tests pass

### Phase 11.3 completed:
- Added `EndedAt` field to `PlayHistoryEntry` model
- Added `FinalizeEntryAsync` to `IPlayHistoryRepository` (sets EndedAt, calculates DurationSeconds)
- Implemented in `SqlitePlayHistoryRepository` with SQLite julianday() duration calculation
- Added `EndedAt` column migration in `FingerprintDbContext`
- Updated all 6 SELECT queries, INSERT, UPDATE, and mapper to include EndedAt
- Created `SongChangedEventArgs` event class
- Added song change detection to `BackgroundIdentificationService`
- `AudioManager.OnSongChanged` handler
- Added 8 new tests (4 repository + 4 song change detection)
- Build: 0 warnings, all 1313 tests pass

---

## Session: 2026-02-13 — Cast Audio & BT Fixes
(see git history for full content)

## Session: 2026-02-14 — Pi Hardware Testing
(see git history for full content)
