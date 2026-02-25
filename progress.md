# Progress Log

## Session: 2026-02-24 (continued) — Fingerprint Fixes, Panako Research, UI Fixes

### Completed:
- [x] F.1b: Ring buffer capture fix — sync Read() → async ReadAsync() + skip zero-only chunks
- [x] F.1b: Audio normalization for fingerprinting — peak normalization for quiet post-mixer audio
- [x] F.1b: Multi-duration fallback for live sources — tries 180/210/240/270/300s if initial lookup fails
- [x] Vinyl fingerprinting investigation — confirmed AcoustID cannot match vinyl (Chromaprint hash is correct at 820+ chars but pitch/speed variance prevents matching)
- [x] Panako research spike — comprehensive report at `.research/panako/PANAKO-RESEARCH.md`
- [x] Captured vinyl audio samples (3x 30s recordings of "Twilight" by ELO)
- [x] F.10: Preset save fix — added ISnackbar feedback, replaced silent catch blocks with proper error logging
- [x] F.10: Error handling — replaced all empty `catch { }` blocks in RadioControlPanel with proper `Logger.LogError`
- [x] F.9: Queue auto-advance investigation — added diagnostic logging to MonitorPlaybackAsync and NextAsync, wrapped NextAsync call in try/catch for better error visibility
- [x] Sticky radio preferences — confirmed already implemented (previous session): PreferencesPersistenceService reads live radio state, SDRRadioAudioSource restores on startup

### Panako Research Key Finding:
Panako handles pitch shift +/-10% and time-stretch +/-16% — far exceeding vinyl variation (0.1-3%). Uses spectral peak landmarks with relative frequency ratios. AGPL-3.0 license, Java, self-hosted LMDB database. Recommendation: hybrid AcoustID (digital) + Panako (analog) pipeline. Est. 5-9 days to integrate.

---

## Session: 2026-02-24 — Phase F Planning & Initial Work

### Plan:
- Phase F: Stability, UX & Features — 10 work items (F.1-F.10)
- Priority: F.1 (SEGV crash) is Critical, F.2 (log noise) is important, rest are features

### Completed:
- [x] Updated task_plan.md with Phase F (10 items) from user's 12 work requests
- [x] Research: API SEGV crash analysis (28 crashes/day, ~28min interval)
- [x] Research: Log noise sources identified (3 major: ALSA, fingerprint, DirectCast)
- [x] Research: FM stereo feasibility confirmed (~200-300 lines new DSP code)
- [x] Research: Codebase locations mapped for all Phase F items
- [x] Updated findings.md with research results

### Completed (implementation):
- [x] F.2: Log noise reduction — DirectCast chunk logging → DBG, NowPlayingChanged → DBG, album art URL → DBG, VisualizerPanel TaskCanceledException silently caught
- [x] F.5: Waveform visualizer — vertical bars from origin, cyan (positive) / amber (negative)
- [x] F.8: Now Playing panel — large album art (340px max) with gradient-overlaid metadata, source chip in top-right
- [x] F.9 (partial): Shuffle/Repeat indicators — active state has cyan background + border, RepeatOne icon for "One" mode
- [x] F.10 (partial): AGC toggle alignment — `.mud-switch { align-items: center }` in SDR controls

---

## Session: 2026-02-22 — Queue Add-to-Queue Fix (PR #234)

### Completed:
- [x] Fixed Blazor.start() ordering in App.razor (JS deps must load before circuit start)
- [x] Fixed reconnect dialog ID (components-reconnect-modal, not blazor-reconnect-dialog)
- [x] Added diagnostic logging + timeouts to queue/playlist handlers
- [x] Fixed false-positive queue success (check addedCount, not just HTTP 200)
- [x] Added DisconnectedCircuitRetentionPeriod = 10min for kiosk reliability
- [x] Deployed, tested, created PR #234, merged to main

---

## Previous Session — Cast Latency Configurable (PR #233)

### Completed:
- [x] Made DirectChannel Cast latency parameters configurable from UI
- [x] Three new settings: MaxBufferAhead, BufferBeforePlay, ReaderLagSeconds
- [x] Sender sends config message to receiver on connect
- [x] Receiver handles config message (mutable defaults)
- [x] UI controls added to Configuration page
- [x] Created PR #233, merged to main

---

## Next Session — Media Setup, Dual Output Bug, Architecture Cleanup

### Plan:
- Phase A: Ubuntu media & data setup (copy files, test playback + fingerprinting)
- Phase B: Fix dual audio output bug (local + Cast playing simultaneously)
- Phase C: Architecture cleanup (extract PlayHistoryTracker, review AudioManager)
- Phase D: Hardware integrations (deferred, on Ubuntu)

### Status: Not started

## Session: 2026-02-20 — Cast Drift Testing & Ping Endpoint

### Context:
- Resumed from crashed session on `feature/cast-latency-measurement` branch
- v10 receiver (drift protection) was uncommitted — recovered and committed

### Completed:
- [x] Committed v10 receiver drift protection (buffer-ahead cap at 3s, drops ~1 chunk/43s)
- [x] Pushed and created PR #217, merged to main as `1c2b03e`
- [x] GitHub Pages confirmed serving v10 receiver
- [x] Created `feature/cast-drift-testing` branch
- [x] Deployed to Pi, verified DirectChannel streaming works (116 chunks, 0 errors)
- [x] Discovered Pi config had `StreamingMode: "HttpMp3"` — fixed to `DirectChannel`
- [x] Added `POST /api/devices/cast/ping` endpoint to DevicesController (triggers ping/pong, returns RTT + pong JSON with latency metrics)
- [x] Deployed ping endpoint to Pi

### Ubuntu x64 testing: COMPLETE
- [x] Deployed to Ubuntu x64 (`mmack@radio`) with `Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`
- [x] Set Ubuntu config to `StreamingMode: DirectChannel`, `ApplicationId: 567E3DBA`
- [x] Start playback + Cast connect on Ubuntu
- [x] Got v10 metrics via CDP (transit delay avg 87ms, buffer-ahead 3.0s steady)
- [x] Drift protection verified working — buffer-ahead constant at 3.0s cap
- [x] Audio quality confirmed clean — constant ~3s delay, no drift, no stutter

### Key findings:
- Connect endpoint ignores `streamingMode` in request body — reads from `appsettings.json` config only
- Pi config was stale (HttpMp3) — must update config AND restart service
- Deploy script overwrites appsettings.json — use appsettings.Production.json for per-machine overrides
- SharpCaster channel registration via reflection works (array replacement), but pong messages still not received — needs deeper investigation
- Receiver double-counts messages (20/sec vs sender 10/sec) — cosmetic, doesn't affect audio
- CDP (Chrome DevTools Protocol) works reliably for reading receiver metrics as a workaround
- **Audio plays on BOTH local output AND Cast** — this is a bug for normal use, but useful for testing latency

---

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

### Phase B status: COMPLETE
- [x] Initial approach (StandardErrorPriority) failed — noise comes through stdout, not stderr
- [x] Created `SystemdConsoleFormatter` — prefixes Serilog lines with `<N>` syslog priority
- [x] Service file: `SyslogLevelPrefix=true` + `SyslogLevel=debug`
- [x] Verified on Ubuntu: `journalctl -p info` shows ONLY app logs, zero ALSA noise

### Phase C status: COMPLETE
- [x] C.2: Deploy script parameterized — `Deploy-ToLinux.ps1` with `-Runtime` param
- [x] C.2: `Deploy-ToPi.ps1` converted to thin wrapper
- [x] C.2: Production config template for debian-x64
- [x] C.3: Setup script run on Ubuntu — all packages, radio user, fpcalc, services installed
- [x] C.4: Application deployed and running on `radio:5000` / `radio:5002`
- [x] C.4: API responds (200 on /api/audio), Web UI loads (200)
- [x] C.4: ALSA log filtering verified working

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
