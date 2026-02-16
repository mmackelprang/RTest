# Task Plan: Architecture Review & Final Integrations

## Goal
Clean up the codebase (remove Spotify, reduce Cast latency), prepare the fingerprinting/history subsystems for continuous audio sources, then manually integrate Phonograph, RTL-SDR, and Generic USB sources one at a time.

## Current Phase
Phase 11 — Architecture Cleanup & Integration Prep

---

## Completed Phases (summary)

All prior phases (0-10) are **COMPLETE**. See git history for details:
- Phases 0-9: Full system build (audio engine, sources, outputs, API, UI)
- Post-plan: RTL-SDR, fingerprinting, BT A2DP, Cast fixes, deployment, Pi testing
- Phases 1-10 (task_plan): Bug fixes, BT UX, volume, Cast, Pi verification, docs, E2E tests

---

## Phase 11: Architecture Cleanup & Integration Prep 🔄

### 11.1 Remove Spotify Code
**Priority:** High — removes ~2,500 lines of dead code before any new work

**Delete entire directories/files:**
- [ ] `SpotifyLoopback/` directory (Program.cs, SmartSpotifyDevice.cs, AudioDeviceManager.cs, LibrespotManager.cs, README.md)
- [ ] `scripts/Setup-SpotifyLoopback.ps1`
- [ ] `scripts/Test-SpotifyLoopback.ps1`
- [ ] `scripts/Quick-SpotifyCheck.ps1`
- [ ] `scripts/setup-spotify-loopback.sh`
- [ ] `scripts/test-spotify-loopback.bat`
- [ ] `scripts/appsettings.Development.Spotify.json`
- [ ] `src/Radio.API/appsettings.Development.Spotify.json`
- [ ] `src/Radio.API/appsettings.Production.Spotify.json`
- [ ] `publish/api/appsettings.Development.Spotify.json`
- [ ] `archive/SPOTIFY_INTEGRATED_SETUP.md`
- [ ] `archive/SPOTIFY_INTEGRATED_IMPLEMENTATION_SUMMARY.md`
- [ ] `archive/SPOTIFY_PLAYBACK_FIX_SUMMARY.md`
- [ ] `archive/SPOTIFY_QUEUE_FIX_SUMMARY.md`

**Clean references from mixed files:**
- [ ] `src/Radio.API/appsettings.json` — Remove `Devices.Spotify` section and `Spotify` section
- [ ] `src/Radio.API/Controllers/ConfigurationController.cs` — Remove `"spotify:"` secret masking
- [ ] `README.md` — Replace "Spotify" with "Bluetooth" in overview
- [ ] `CLAUDE.md` — Same update
- [ ] `design/AUDIO.md` — Remove Spotify sections, SpotifySecrets, SpotifyPreferences
- [ ] Doc comments in API DTOs (NowPlayingDto, PlayHistoryModels, AudioSourceDtos) — remove "Spotify" examples
- [ ] `PLAN.md` — Update phase descriptions

**Delete dead DI code:**
- [ ] `src/Radio.Infrastructure/DependencyInjection/ExternalServiceExtensions.cs` (empty file)

**Verification:** `dotnet build --configuration Release` (0 warnings), `dotnet test` (all pass)

### 11.2 Cast Latency Reduction
**Priority:** High — currently 25 seconds, goal is <10 seconds

**Root cause analysis (see findings.md for full detail):**
The 25-second delay breaks down as:
- 2s CC1AD845 initialization delay (hardcoded)
- 8s first LoadAsync timeout (often fails on cold start)
- 3s retry delay
- 2s second LoadAsync
- 5-8s Cast device internal MP3 buffering
- 2s ring buffer accumulation

**Implementation plan:**

A. **Pre-stream the HTTP endpoint before Cast connects** (biggest win)
- Start HttpStreamOutput and begin accumulating MP3 data BEFORE calling LoadAsync
- When Cast's HTTP GET arrives, the server already has a buffer of MP3 frames to burst
- This eliminates the ~5-8s Cast prefill wait since data is immediately available

B. **Reduce connection ceremony delays:**
- [ ] GoogleCastOutput.cs:540 — Reduce CC1AD845 delay from 2000ms to 1000ms
- [ ] GoogleCastOutput.cs:921 — Reduce retry delay from 3000ms to 1000ms
- [ ] GoogleCastOutput.cs:904 — Reduce first LoadAsync timeout from 8s to 5s
- [ ] Add readiness probe: poll ReceiverChannel status before first LoadAsync

C. **Implement MP3 pre-buffer for Cast:**
- [ ] In HttpStreamOutput, when Cast client connects on `/stream/audio/mp3`:
  - Maintain a rolling 3-second MP3 pre-buffer (FIFO of recent MP3 frames)
  - On new client connect, immediately write the pre-buffer before switching to real-time
  - This gives Cast ~3s of audio instantly, drastically reducing time-to-first-audio

D. **Reduce ring buffer overhead:**
- [ ] Reduce `OutputBufferSizeSeconds` from 2.0 to 1.0 in appsettings.json
- [ ] Reduce `StreamReaderLagSeconds` from 0.5 to 0.2
- [ ] These are safe — fingerprinting captures its own 15s independently

**Expected result:** Connection ceremony ~4s + Cast prefill ~3-4s = **7-8 seconds total**

### 11.3 Continuous Source Fingerprinting (Song Change Detection)
**Priority:** High — required before Phonograph/SDR/USB integration

**Problem:** Current system creates ONE play history entry per source session. For continuous sources (vinyl, radio, USB), different songs play back-to-back with no discrete events. Only fingerprinting can detect the boundary.

**Design: Fingerprint-driven song change detection**

The `BackgroundIdentificationService` already runs every 30s and identifies tracks. Extend it to:

1. **Track last identification per source:**
   ```csharp
   private (string TrackKey, DateTime IdentifiedAt)? _lastIdentification;
   ```

2. **Detect song transitions:**
   - When a new fingerprint resolves to a DIFFERENT `{title}|{artist}` than `_lastIdentification`:
     - Raise a new `SongChanged` event (distinct from `TrackIdentified`)
     - Include both old and new track metadata
   - Update `_lastIdentification`

3. **AudioManager handles `SongChanged`:**
   - Finalize the current play history entry (set end timestamp, duration)
   - Create a NEW play history entry for the new song
   - Update `_currentPlayHistoryEntryId`
   - Emit metrics: `fingerprint.song_change_detected`

4. **PlayHistoryEntry changes:**
   - [ ] Add `EndedAt` (DateTime?) field — marks when song ended
   - [ ] Add `DurationSeconds` (int?) field — calculated from PlayedAt to EndedAt
   - [ ] Update `IPlayHistoryRepository.FinalizeEntryAsync(id, endedAt)` method

5. **Configuration:**
   - [ ] Add `MinimumSecondsBetweenSongChanges` (default: 20) to `FingerprintingOptions`
   - [ ] Prevents rapid-fire entry creation from noisy fingerprints at song boundaries

**Verification:** Unit tests for song change detection, integration test for entry creation

### 11.4 Minor Architecture Cleanup
**Priority:** Low — nice-to-have improvements, only if time permits

- [ ] Extract `PlayHistoryTracker` from AudioManager (reduce god-class tendency)
- [ ] Review AudioManager constructor (18 params) — consider bundling optional deps
- [ ] Remove any remaining TODO comments that reference completed work

---

## Phase 12: Phonograph Integration (Manual, on Pi)
**Status:** Pending Phase 11

### 12.1 Hardware Connection
- [ ] Connect USB turntable to Pi
- [ ] Verify device appears in `arecord -l` / ALSA device list
- [ ] Configure `Devices.Vinyl.USBPort` in appsettings.Production.json
- [ ] Deploy updated config

### 12.2 Audio Pipeline Verification
- [ ] Switch to Vinyl source via API
- [ ] Verify capture device found and audio flowing
- [ ] Verify audio plays through local speakers
- [ ] Verify audio streams to Cast device
- [ ] Check visualization (FFT, levels) shows activity

### 12.3 Fingerprinting Verification
- [ ] Play a known record
- [ ] Verify fingerprinting identifies tracks (15s sample + AcoustID lookup)
- [ ] Verify song change detection creates new play history entries
- [ ] Verify album art appears in UI
- [ ] Test edge case: needle lift between songs (silence → should not create entry)

### 12.4 Bug Fixes
- [ ] Address any issues found during testing

---

## Phase 13: RTL-SDR Radio Validation (Manual, on Pi)
**Status:** Pending Phase 12

### 13.1 Hardware Connection
- [ ] Connect RTL-SDR USB dongle to Pi
- [ ] Verify device appears and RTL-SDR libraries can access it
- [ ] Configure radio presets for local FM stations

### 13.2 Audio Pipeline Verification
- [ ] Switch to Radio source via API
- [ ] Tune to a known FM station
- [ ] Verify audio plays through local speakers and Cast
- [ ] Verify visualization shows activity

### 13.3 Fingerprinting & History
- [ ] Verify continuous fingerprinting identifies songs on FM radio
- [ ] Verify song change detection works (new entries for new songs)
- [ ] Verify duplicate suppression prevents re-identifying same song repeatedly

### 13.4 SDR-Specific Features
- [ ] Frequency scanning
- [ ] AGC behavior
- [ ] Band switching (FM/AM/SW)
- [ ] Preset save/load

---

## Phase 14: Generic USB Audio Validation (Manual, on Pi)
**Status:** Pending Phase 13

### 14.1 Hardware Connection
- [ ] Connect second USB audio device to Pi
- [ ] Verify device appears in device list
- [ ] Select via GenericUSB source API

### 14.2 Audio Pipeline Verification
- [ ] Verify capture and playback
- [ ] Verify Cast streaming
- [ ] Verify fingerprinting and song change detection

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
| Fingerprint-driven song change | Only reliable signal for continuous audio sources |
| MP3 pre-buffer for Cast | Gives Cast immediate audio data, reducing time-to-first-audio |
| Remove Spotify before integration | Clean slate, removes dead code confusion |

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
