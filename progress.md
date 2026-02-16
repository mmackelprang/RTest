# Progress Log

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
- Added song change detection to `BackgroundIdentificationService`:
  - Tracks `_lastIdentification` (trackKey, metadata, timestamp)
  - Compares each new identification with previous
  - Respects `MinimumSecondsBetweenSongChanges` (default: 20s) to prevent rapid-fire events
  - `ResetSongChangeState()` for source changes
- `AudioManager.OnSongChanged` handler:
  - Finalizes previous play history entry (sets EndedAt)
  - Creates new entry for newly-identified song
  - Resets song change state on source switch
- Added `EndedAt` to API DTO (`PlayHistoryEntryDto`) and Web DTO
- Updated controller mapping
- Added 8 new tests (4 repository + 4 song change detection)
- Build: 0 warnings, all 1313 tests pass

---

## Session: 2026-02-13 — Cast Audio & BT Fixes

### Pre-planning fixes completed:
- PR #192 (merged): BT capture bridge, DI factories, visualization tap, codec pinning, album art fix
- PR #193 (merged): Metrics transaction/connection mismatch fix
- PR #194 (merged): Cast audio — reader lag, LAME Flush fix, reduced tap latency

### Planning phase:
- Wrote 5-phase plan (see task_plan.md)
- Phases 1-4 implemented and tested (1266 tests pass, 0 warnings)

### Phase 1-4 Implementation:
- All code changes committed as PR #195 (merged)

---

## Session: 2026-02-14 — Pi Hardware Testing

### Dual-service deployment:
- PR #196: Split radio-console.service into radio-api + radio-web
- PipeWire/WirePlumber BT A2DP sink config
- ALSA direct hardware access for radio system user

### Pi debugging — BT audio pipeline:
1. Discovered arecord subprocess runs and captures real audio data (strace confirmed non-zero 24KB writes)
2. Found Serilog `Default: Warning` was hiding all audio pipeline logs — added `Radio: Information` override
3. Identified race condition in `GetAudioCaptureDeviceAsync` — two concurrent handlers with 0-timeout semaphore
4. Fixed with 30s timeout + `_activeGenerator` cache
5. Discovered `SwitchPlaybackDevice` bug — orphans source components after device switch
6. Fixed with `PlaybackDeviceSwitched` event + subscriber re-attachment in `SoundFlowPlaybackService`
7. Set device to playback-12 (bcm2835 Headphones), confirmed audio plays through soundbar
8. All fixes committed as PR #197

### Verified on Pi:
- [x] BT connect → arecord → generator → mixer → playback in <1 second
- [x] AVRCP metadata flowing (title/artist)
- [x] AVRCP volume sync (68% from phone)
- [x] Play history updated with real track info
- [x] Audio output to soundbar via 3.5mm jack
- [x] Device preference saved to config store
