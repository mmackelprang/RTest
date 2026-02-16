# Findings & Decisions

## Session: 2026-02-15 — Project Reconciliation

### Document Staleness Assessment

| Document | Last Updated | Staleness |
|----------|-------------|-----------|
| PLAN.md | 2025-12-02 | **Very stale** — Phase 3 "In Progress", Phase 9 "Not Started", Phases 10-12 "Not Started" |
| README.md | ~2026-01 | Slightly stale — says "All 13 phases finished!" but Phases 10-12 only partial |
| CLAUDE.md | ~2025-12 | Stale — says "Phase 9 - pending", missing BT/Cast/deployment info |
| FUTURE-WORK.md | 2026-02-13 | **Current** — accurately reflects 6 deferred items |
| WORK-LOG.md | 2026-02-15 | **Current** — comprehensive session history |
| DECISION-LOG.md | 2026-02-15 | **Current** — 21 ADRs documented |
| task_plan.md | 2026-02-14 | Stale — Phase 7 shown as "NEW" but all items completed |

### Features Built But Not In Original PLAN.md

These were developed after the plan was written (2025-12-02) and represent significant additions:

1. **RTL-SDR Software-Defined Radio** (PR #103, Dec 2025) — Full SDR source with frequency tuning, scanning, AGC, band selection. 27K LOC.
2. **Audio Fingerprinting** (PR #171-172, Feb 2026) — Native fpcalc, AcoustID lookup, auto-skip, MusicBrainz metadata.
3. **Bluetooth A2DP Audio Input** (PR #174-197, Feb 2026) — Full pipeline: Linux BlueZ D-Bus, Windows WinRT, AVRCP metadata/volume/next/prev, album art cache.
4. **Dual-Service Deployment** (PR #196, Feb 2026) — Separate radio-api + radio-web systemd services, Pi deployment scripts.
5. **Play History & Analytics** (PR #129+, Dec 2025) — Play history tracking, search, MusicBrainz enrichment.
6. **Google Cast Improvements** (PR #194+, Feb 2026) — StreamType.Live, LAME flush fix, reader lag burst, idle recovery, pause/resume.
7. **Device Filtering & Friendly Names** (PR #198, Feb 2026) — Regex-based hidden patterns, ordered friendly name mappings.
8. **Local Output Muting for Cast** (PR #198, Feb 2026) — Confirmed via SoundFlow decompilation that modifiers run before volume.

### Actual Phase Completion Status

| Original Phase | PLAN.md Says | Reality |
|---------------|-------------|---------|
| 0 Setup | Completed | Completed |
| 1 Configuration | Completed | Completed |
| 2 Core Audio | Completed | Completed |
| 3 Primary Sources | In Progress | **Completed** — all 6 source types + SDR + BT |
| 4 Event Sources | Completed | Completed |
| 5 Ducking | Completed | Completed |
| 6 Outputs | Completed | Completed + Cast improvements |
| 7 Visualization | Completed | Completed |
| 8 API & SignalR | Completed | Completed — 16 controllers, 126+ endpoints |
| 9 Blazor UI | Not Started | **Completed** — 12 pages, shared components, MudBlazor M3 |
| 10 Testing | Not Started | **Substantially complete** — 1,189 tests, 7 projects, 0 E2E |
| 11 Documentation | Not Started | **Partially complete** — design docs, WORK-LOG, DECISION-LOG, no user guide |
| 12 Deployment | Not Started | **Substantially complete** — dual-service, Pi scripts, tested on hardware |

### Test Coverage

| Project | Tests |
|---------|-------|
| Radio.Infrastructure.Tests | ~689 |
| Radio.API.Tests | 202 |
| Radio.Web.Tests | ~120 |
| RTLSDRCore.Tests | ~125 |
| Radio.IntegrationTests | ~86 |
| Radio.Core.Tests | 35 |
| Radio.Web.E2ETests | 0 (infrastructure exists) |
| **Total** | **~1,257** |

### Known TODOs in Codebase

1. `FileBrowser.cs:413-415` — SoundFlow metadata gaps (genre/year/track#) — possibly stale
2. `TTSFactory.cs:91` — TTS cache not implemented — documented in FUTURE-WORK.md
3. `RadioController.cs:845` — Device switching not implemented — documented in FUTURE-WORK.md

Zero `NotImplementedException` instances in src/.

### Remaining Work Categories

**A. Documentation Updates (PLAN.md, README.md, CLAUDE.md):**
- PLAN.md needs phases 3, 9-12 status updated + post-plan features added
- README.md needs post-plan features, realistic phase status
- CLAUDE.md needs Phase 9 fix, BT/Cast/deployment mentions

**B. Pi Verification (from task_plan.md Phase 5.2):**
- Restart preference restore
- Volume persistence across restart
- Fingerprint skip after identification
- Cast latency measurement
- Sample drop rate after fixes

**C. Known Bugs (awaiting Pi re-test after PR #198):**
- Cast pause/resume — fixed, needs verification
- BT progress bar — fixed, needs verification
- BT next/previous — logging improved, depends on phone AVRCP
- Device filtering — implemented, needs Pi verification
- Album art proxy (Web 5002 → API 5000) — untested

**D. Deferred Features (FUTURE-WORK.md):**
- Kiosk mode — medium priority, infrastructure ready
- TTS cache — low priority
- Windows AVRCP volume — low priority (dev-only)
- Radio device switching — low priority
- FileBrowser metadata gaps — low priority, possibly stale

**E. Testing Gaps:**
- E2E tests — 0 written (Playwright infrastructure exists)
- No coverage report generated

---

## Session: 2026-02-15 — Phase 9 Pi Verification

### Bugs Found & Fixed

**Volume/Mute Persistence (3 bugs, 1 root cause chain):**

1. **AudioController bypasses AudioManager** — `POST /api/audio` sets `mixer.MasterVolume` directly, bypassing `AudioManager.MasterVolume` setter which triggers `ScheduleVolumePersist()`. Fix: Route through AudioManager when available.

2. **PreferencesPersistenceService overwrites runtime values** — Every 30s, reads stale `IOptionsMonitor<AudioPreferences>` (from appsettings.json defaults) and writes to SQLite, overwriting AudioManager's correctly persisted values. Fix: Removed AudioPreferences from periodic save; AudioManager handles its own persistence with debounced writes.

3. **ConfigurationStoreFactory.StoreExistsAsync path mismatch** — `StoreExistsAsync()` uses `Path.Combine(_options.BasePath, _options.SqliteFileName)` → `./config/configuration.db`, but `CreateSqliteStore()` uses `_pathResolver.GetConfigurationDatabasePath()` → `./data/config/configuration.db`. Store lookup always fails, falling back to defaults. Fix: Use `_pathResolver` in `StoreExistsAsync` too.

4. **AudioManager.InitializeAsync() never called on startup** — `AudioEngineInitializationService` had `_audioManager` but never called `InitializeAsync()`, so `RestoreVolumePreferences()` never ran. Fix: Call `_audioManager.InitializeAsync()` in startup sequence.

**Deploy Script:**

5. **`rsync --delete` wipes appsettings.Production.json** — Deploy script uses `--delete` flag which removes Pi-specific config. Fix: Added `--exclude='appsettings.Production.json'` and auto-deploy from `deploy/raspberry-pi/` if missing.

### Pi Verification Results

| Feature | Status | Notes |
|---------|--------|-------|
| Device filtering | ✅ | 17 → 7 devices with friendly names |
| Cast pause/resume | ✅ | Position freezes/advances correctly |
| Cast auto-recovery | ✅ | INTERRUPTED → Buffering → Playing on track skip |
| Local mute while casting | ✅ | Cast streams, modifier active (modifierCount=1) |
| Volume persistence | ✅ | Set 0.42 → restart → restored 0.42 |
| Mute persistence | ✅ | Set True → restart → restored True |
| Fingerprint ID | ✅ | blink-182 "All the Small Things" 100% from cache |
| Play history | ✅ | 21 entries with metadata + cover art |
| Album art proxy | ✅ | Web 5002 → API 5000 both HTTP 200 |
| File playback | ✅ | Queue, play, pause, resume, next all working |
| BT next/previous | Deferred | No BT device connected during test |
| BT progress bar | Deferred | No BT device connected during test |
| Cast latency | Deferred | No precision measurement tool |

---

## Previous Session Findings

### Session: 2026-02-14 — Pi Hardware Testing

(see previous entries preserved below)

#### Root Causes Found on Pi

- Race condition in BT audio capture — two handlers, 0-timeout semaphore → 30s timeout + cache fix
- Device switch orphans source components → PlaybackDeviceSwitched event fix
- MiniAudio default device = null sink → preference persistence fix

#### Pi Audio Configuration
- Card 0 = bcm2835 Headphones (3.5mm jack)
- ALSA volume: -6.64 dB (90%), not muted
- `.asoundrc`: `pcm.!default` → `hw:0`

### Session: 2026-02-13 — Cast Audio & BT Fixes

- Cast audio: LAME Flush() killed HTTP chunked response
- Metrics transaction crash: SQLite connection mismatch
- BT album art: scoped service resolved via IServiceScopeFactory
- BT capture bridge: BufferSamples → AddSamples
