# Findings & Decisions

## Session: 2026-02-16 — Architecture Review & Integration Prep

### 1. Audio Source Class Hierarchy

```
IAudioSource (interface)
└── IPrimaryAudioSource (interface)
    └── PrimaryAudioSourceBase (abstract)        ~380 lines
        ├── USBAudioSourceBase (abstract)         ~416 lines
        │   ├── VinylAudioSource                   ~57 lines (thin wrapper)
        │   ├── GenericUSBAudioSource             ~141 lines
        │   └── RadioAudioSource                   (RF320 serial)
        ├── SDRRadioAudioSource                    (RTL-SDR)
        ├── FilePlayerAudioSource                  (file playback)
        └── BluetoothAudioSource                   (BT A2DP)

IEventAudioSource (interface)
└── EventAudioSourceBase (abstract)
    ├── TTSEventSource
    └── AudioFileEventSource
```

**Key finding:** `USBAudioSourceBase` already extracts common USB capture logic. Vinyl is a 57-line thin wrapper. GenericUSB adds device selection. The hierarchy is already well-factored for USB input sources.

### 2. Spotify Code Inventory (for removal)

**Spotify-only files to DELETE entirely:**
- `SpotifyLoopback/` directory (4 .cs files + README.md)
- `scripts/Setup-SpotifyLoopback.ps1`
- `scripts/Test-SpotifyLoopback.ps1`
- `scripts/Quick-SpotifyCheck.ps1`
- `scripts/setup-spotify-loopback.sh`
- `scripts/test-spotify-loopback.bat`
- `scripts/appsettings.Development.Spotify.json`
- `src/Radio.API/appsettings.Development.Spotify.json`
- `src/Radio.API/appsettings.Production.Spotify.json`
- `archive/SPOTIFY_INTEGRATED_SETUP.md`
- `archive/SPOTIFY_INTEGRATED_IMPLEMENTATION_SUMMARY.md`
- `archive/SPOTIFY_PLAYBACK_FIX_SUMMARY.md`
- `archive/SPOTIFY_QUEUE_FIX_SUMMARY.md`
- `publish/api/appsettings.Development.Spotify.json`

**Mixed files needing Spotify references removed:**
- `src/Radio.API/appsettings.json` — Remove `Devices.Spotify` (lines 49-52) and `Spotify` section (lines 158-162)
- `src/Radio.API/Controllers/ConfigurationController.cs` — Remove `"spotify:"` secret masking check
- `README.md` — Update project overview to remove Spotify mention
- `CLAUDE.md` — Update overview
- `PLAN.md` — Update phase descriptions
- `design/AUDIO.md` — Remove Spotify references throughout
- Various doc comment references (NowPlayingDto, PlayHistoryModels, AudioSourceDtos)

**IMPORTANT:** `AudioSourceType` enum does NOT include Spotify — it was never implemented as a source type. No `SpotifyAudioSource` class exists in src/. The `PlaySource` enum may have had Spotify but was already removed (code comment confirms: "Skip rows with removed source types (e.g., Spotify)").

### 3. Cast Buffering Analysis (25-second delay)

**Buffer chain from source to Cast device:**

| Stage | Buffer Size | Duration | Cumulative |
|-------|------------|----------|------------|
| FingerprintTapModifier | 2048 samples | ~21ms | 21ms |
| TappedOutputStream ring buffer | 2 seconds config | 2,000ms | 2,021ms |
| StreamReaderLag prefill | 0.5 seconds | 500ms | (included in ring) |
| LAME MP3 frame | 1152 samples | ~24ms | 2,045ms |
| HTTP chunked write | immediate | ~0ms | 2,045ms |

**Explicit delays in Cast connection path:**

| Location | Delay | Purpose |
|----------|-------|---------|
| GoogleCastOutput.cs:540 | `Task.Delay(2000)` | CC1AD845 initialization wait |
| GoogleCastOutput.cs:921 | `Task.Delay(3000)` | Retry after first load fails |
| GoogleCastOutput.cs:690 | `Task.Delay(500)` | Reconnect delay |
| GoogleCastOutput.cs:713 | `Task.Delay(3000)` | Idle recovery delay |
| GoogleCastOutput.cs:827 | `Task.Delay(500)` | Stop delay |

**Root cause breakdown of ~25s:**
1. LaunchApplicationAsync + 2s delay = ~3s
2. First LoadAsync attempt (often fails) + 8s timeout = ~8s worst case
3. 3s retry delay = 3s
4. Second LoadAsync = ~2s
5. Cast device internal buffering before playing = ~5-8s
6. Ring buffer accumulation = ~2s
7. **Total: ~23-26s** — matches observation

**Key insight:** The Cast device itself needs several seconds of MP3 data before it starts playback. Chrome's media player buffers data before rendering. We can't eliminate that, but we CAN:
1. Reduce connection ceremony delays
2. Pre-fill the HTTP stream with data before LoadAsync
3. Use a "burst" mode that writes data faster than real-time initially

### 4. Fingerprinting & Play History for Continuous Sources

**Current behavior:**
- Fingerprinting runs every 30s, captures 15s samples
- `AudioManager.RecordPlayStartAsync()` creates ONE entry when source starts Playing
- `AudioManager.OnTrackIdentified()` updates the CURRENT entry (finds most recent unidentified)
- **Problem:** For continuous sources (Vinyl, Radio, USB), only ONE history entry per session

**What needs to change for continuous sources:**
1. When fingerprinting identifies a DIFFERENT track than the current one → create a NEW play history entry
2. Track "last identified fingerprint hash" per source to detect transitions
3. Close/finalize the previous entry with an end timestamp
4. Create the new entry with the newly identified metadata

**Current duplicate suppression:**
- 5 minutes for normal matches, 30 minutes for high-confidence (>0.9)
- Keyed on `{title}|{artist}`
- A genuinely different song passes suppression naturally

### 5. Dead Code Found

- `ExternalServiceExtensions.cs` — Empty file, can delete
- `SpotifyLoopback/` — Entire directory is dead code
- Various Spotify config/scripts — Dead code

### 6. Architecture Improvement Opportunities

**A. AudioManager constructor bloat:** 18 parameters (6 optional). Consider:
- Extract a `AudioManagerOptions` record to bundle related options
- Or accept `IServiceProvider` for optional deps (trade-off: service locator)

**B. Play history recording is scattered:**
- `RecordPlayStartAsync` in AudioManager
- `OnTrackIdentified` handler in AudioManager
- `OnBluetoothMetadataChanged` handler in AudioManager
- Could extract a `PlayHistoryTracker` service

**C. Source creation in AudioManager.GetOrCreateSourceAsync:**
- Large switch statement creating sources inline
- Could move to a `IAudioSourceFactory` with registered creators

---

## Previous Sessions (preserved)

### Session: 2026-02-15 — Project Reconciliation
(see git history for full content)

### Session: 2026-02-15 — Phase 9 Pi Verification
(see git history for full content)

### Session: 2026-02-14 — Pi Hardware Testing
(see git history for full content)

### Session: 2026-02-13 — Cast Audio & BT Fixes
(see git history for full content)
