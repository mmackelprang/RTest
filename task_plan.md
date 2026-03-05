# Task Plan

## Goal
Build a polished audio console with reliable song identification, rich metadata display, and smooth UX. SongRec (Shazam) is the primary recognition engine for live sources; AcoustID for local files only. RDS provides station identity for FM radio. Modern observability dashboard for system health. Responsive touch UX.

## Current Phase
Phase 10 — Smart Fingerprinting (pending)

## Phases

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | Audio preprocessing for vinyl fingerprinting | complete | High-pass filter deployed, tested — AcoustID still can't match vinyl |
| 2 | SongRec integration (Shazam) | complete | Deployed + vinyl tested. Uses `recognize --json`. Identified Cars tracks from vinyl in ~1s. |
| 3 | ACRCloud integration | cancelled | SongRec covers all use cases; ACRCloud 5K/month limit unnecessary |
| 4 | RDS station name for FM radio | complete | Station name, PTY/genre on RadioControlPanel. Biphase decoding, Costas loop, CRC sync. PR #284 merged. |
| 5 | SongRec as primary recognition engine | complete | SongRec primary for live sources, album art caching, exponential backoff. PR #285 merged. |
| 6 | Play history enhancements | complete | Radio context, album art, source icons. PR #287 merged. |
| 7 | UX polish | complete | Virtual keyboard auto-show for dialogs, RDS preset auto-naming. PR #288 merged. |
| 8 | Session persistence | complete | Source auto-activation, file player queue/position restore. PR #288 merged. |
| 9 | Metrics dashboard redesign | complete | Grafana-inspired layout: hero stat cards, canvas time-series chart, collapsible categories. PR #290 merged. |
| 10 | Smart fingerprinting (remove AcoustID) | pending | Event-driven recognition for BT/File; remove AcoustID; SongRec-only for all sources |
| 11 | Radio preset naming format | complete | `{Band} {CallSign} {Frequency}` format. PR #289 merged. |
| 12 | Numeric keypad mode | complete | Auto-detect numeric inputs, compact 4×4 numpad. PR #289 merged. |
| 13 | AB13X USB input debug | pending | Investigate why USB audio input stopped working |
| 14 | Audio distortion / CPU investigation | pending | Diagnose periodic distortion correlated with UI/metrics loading |

---

## Phase 1: Audio Preprocessing for Vinyl Fingerprinting

**Status**: complete

**Goal**: Add a high-pass filter to audio samples before fingerprinting to remove turntable rumble, then test if Chromaprint/AcoustID match rate improves for vinyl.

**Result**: High-pass filter deployed and tested. AcoustID still can't match vinyl — the algorithm fundamentally struggles with live/degraded audio. Validated that an alternative recognizer (SongRec) is needed.

---

## Phase 2: SongRec Integration (Shazam)

**Status**: complete

**Goal**: Add SongRec as an alternative fingerprinting service for sources where AcoustID struggles.

**Result**: SongRec deployed, working across vinyl and radio sources. Uses `recognize --json` command. Identifies tracks from vinyl in ~1 second. Currently wired as fallback after AcoustID.

---

## Phase 3: ACRCloud Integration

**Status**: cancelled

SongRec covers all recognition use cases effectively. ACRCloud's 5K/month limit adds complexity without benefit.

---

## Phase 4: RDS Metadata for FM Radio

**Status**: complete (PR #284)

Built full RDS decoder in RTLSDRCore: 57 kHz BPF → Costas loop BPSK demod → biphase (Manchester) decoding → CRC block sync → Group 0A/2A parsing. Displays station name and PTY/genre on RadioControlPanel. Verified on WFJA 105.5 FM.

---

## Phase 5: SongRec as Primary Recognition Engine

**Status**: complete (PR #285)

**Goal**: Make SongRec the primary song identification path for all live audio sources. Add album art fetching. Add exponential backoff for rate limit resilience.

**Result**: SongRec is now the sole recognizer for live sources (radio, vinyl, BT, USB), bypassing Chromaprint/AcoustID entirely. AcoustID preserved for file sources. Album art cached locally from Shazam CDN. Exponential backoff (15/30/60/120s) on consecutive failures. Identification interval lowered to 15s, sample duration 15s — total cycle ~31s. Verified on vinyl: "Candy-O", "Heartbeat City", "Touch and Go" by The Cars all identified correctly.

---

## Phase 6: Play History Enhancements

**Status**: complete (PR #287)

**Goal**: Enrich play history entries with contextual information and album art.

**Result**: Album art thumbnails displayed in history panel. Source icons (Radio/Vinyl/BT/File/USB) shown as fallback when no art available. Radio context string includes band/frequency/station name.

---

## Phase 7: UX Polish

**Status**: complete (PR #288)

**Goal**: Fix touch keyboard issues and improve radio preset naming.

**Result**:
- Virtual keyboard auto-shows when any input inside a MudBlazor dialog receives focus (`focusin` listener). Auto-hides on `focusout`. Z-index 10001 (above dialog overlay). Dialog paper shifts up 150px when keyboard opens via `body.keyboard-active` CSS class.
- Fixed missing `<script type="module">` tag for `virtual-keyboard.js` in `App.razor`.
- Fixed CSS descendant selector (`.mud-overlay .mud-paper` not direct child `>`).
- Radio preset save dialog pre-fills with RDS station name when available (e.g., "WFJA"), falls back to "FM - 105.5 MHz" format.

---

## Phase 8: Session Persistence

**Status**: complete (PR #288)

**Goal**: Remember audio state across restarts so the system resumes where it left off.

**Key finding**: Most state was already persisted — radio frequency/band, volume, output device, Cast, file player queue. The only gap was source auto-activation on startup.

**Result**:
- `AudioEngineInitializationService` now activates persisted source after volume restore: output device → volume/gain → source activation → BT pre-warm.
- New `ActivatePersistedSourceAsync()` reads `AudioPreferences.CurrentSource`, calls `AudioManager.GetOrCreateSourceAsync()`. Falls back to Radio on invalid/missing value.
- File player: added `_pendingSeekMs` field to restore playback position on first play after queue restoration.
- Verified: FilePlayer activated from preferences on restart (was defaulting to Radio). Volume restored before source starts.

---

## Phase 9: Metrics Dashboard Redesign

**Status**: complete (PR #290)

**Goal**: Transform the metrics page from a flat list of cards into a modern, Grafana-inspired observability dashboard with time-series charts, stat cards with sparklines, and intuitive drill-down.

**Result**:
- Full-width layout with hero stat cards (auto-selected by pattern: cpu, memory, buffer, underrun, error, active)
- Canvas time-series chart via `metricsChart.js` ES module (area fill, min/max band, hover tooltip, threshold lines, gridlines)
- Collapsible category sections with compact metric rows, inline sparklines, and trend indicators
- 5 time ranges (5m/1h/24h/7d/30d) with resolution mapping to 3-tier SQLite rollup
- Threshold coloring (green/amber/red) with `invertAbove` flag for gauge vs counter metrics
- Client-side aggregate computation (Count, Avg, Min, Max, StdDev) — API aggregate endpoint returns raw double
- Auto-refresh every 10s, time range preference persisted
- Documentation updated in `design/METRICS.md`

---

## Phase 10: Smart Fingerprinting (Remove AcoustID)

**Status**: pending

**Goal**: Remove AcoustID/Chromaprint dependency entirely. Use SongRec for all recognition. Make fingerprinting event-driven for sources that have song boundaries (BT AVRCP, FilePlayer) — capture immediately on song start, stop once identified.

**Current State**:
- AcoustID used only for file sources (via `GenerateFingerprintFromFileAsync`)
- SongRec used for all live sources (radio, vinyl, BT, USB)
- Polling every 15s regardless of song state
- BT already has `RequestImmediateIdentification()` on AVRCP metadata
- FilePlayer already has `RequestImmediateIdentification()` on track load
- `fpcalc` binary still required for Chromaprint fingerprint generation

**Event-Driven Design**:
```
BT song starts → AVRCP metadata arrives
  → If title+artist present: skip fingerprinting, just fetch cover art
  → If incomplete: start SongRec capture immediately → identify → stop

FilePlayer song starts → ID3/metadata read
  → If complete metadata: skip fingerprinting, just fetch cover art
  → If missing: SongRec capture → identify → stop

Radio/Vinyl/USB → no song boundaries → keep polling (SongRec, 15s cycle)
```

**Steps**:
1. Remove AcoustID client, Chromaprint service, fpcalc dependency
2. Route file source recognition through SongRec (capture from mixer, not file-based fingerprint)
3. Add `IdentificationComplete` flag per song — once identified, stop capturing until next song change
4. For BT: if AVRCP provides full metadata, skip SongRec entirely — just do cover art lookup
5. For FilePlayer: if ID3 tags have title+artist, skip SongRec — just do cover art lookup
6. For radio/vinyl/USB: keep current polling but add "identified" suppression (don't re-identify same song)
7. Remove `fpcalc` from deployment, update tests
8. Clean up unused interfaces, DTOs, config options

**Files to modify**:
- `BackgroundIdentificationService.cs` — event-driven logic, remove AcoustID path
- `ChromaprintFingerprintService.cs` — delete or gut (remove fpcalc invocation)
- `MetadataLookupService.cs` — remove AcoustID lookup, simplify to cache + MusicBrainz cover art
- `AcoustIdClient.cs` — delete
- `FingerprintingOptions.cs` — remove AcoustID-related options
- `FingerprintingServiceExtensions.cs` — remove AcoustID DI registrations
- Tests: update/remove AcoustID-specific tests

**Validation**:
- BT with AVRCP: metadata appears instantly, no SongRec call, cover art fetched
- BT without AVRCP: SongRec identifies within ~20s of song start
- FilePlayer with ID3 tags: metadata from tags, cover art fetched, no SongRec
- FilePlayer without tags: SongRec identifies after playback starts
- Radio/Vinyl: SongRec identifies, stops re-identifying same song
- No `fpcalc` binary needed on deployment target
- All tests pass

---

## Phase 11: Radio Preset Naming Format

**Status**: complete (PR #289)

**Goal**: Change preset auto-naming from `{RdsStationName}` or `{Band} - {Frequency}` to `{Band} {CallSign} {Frequency}`.

**Current Code** (`RadioControlPanel.razor:1089-1098`):
```csharp
_presetName = !string.IsNullOrEmpty(_radioState.RdsStationName)
  ? _radioState.RdsStationName
  : $"{_radioState.Band} - {FormatFrequency(_radioState.Frequency, _radioState.Band)}";
```

**New Format**:
```csharp
// With RDS:  "FM WFJA 105.5 MHz"
// Without:   "FM 105.5 MHz"
_presetName = !string.IsNullOrEmpty(_radioState.RdsStationName)
  ? $"{_radioState.Band} {_radioState.RdsStationName} {FormatFrequency(_radioState.Frequency, _radioState.Band)}"
  : $"{_radioState.Band} {FormatFrequency(_radioState.Frequency, _radioState.Band)}";
```

**Steps**:
1. Update `OpenSavePresetDialog()` in `RadioControlPanel.razor`
2. Test with RDS station (e.g., "FM WFJA 105.5 MHz")
3. Test without RDS (e.g., "FM 105.5 MHz")

---

## Phase 12: Numeric Keypad Mode

**Status**: complete (PR #289)

**Goal**: Add a compact numeric keypad layout for number-only inputs (frequency entry, etc.) instead of showing the full QWERTY keyboard.

**Current State**: Single QWERTY layout for all inputs. No `inputmode` detection. Keyboard is 5 rows × 10+ keys.

**Design**:
```
┌─────────────────────┐
│  7    8    9   ⌫    │
│  4    5    6   .    │
│  1    2    3   -    │
│  0    00   ←→  Enter│
└─────────────────────┘
```

**Detection Logic**: Show numeric keypad when:
- Input has `type="number"` or `inputmode="numeric"` or `inputmode="decimal"`
- Input has `data-keyboard="numeric"` attribute (explicit opt-in)
- Otherwise show full QWERTY

**Steps**:
1. Add numeric keypad layout definition in `virtual-keyboard.js`
2. Add input type detection in `show()` method
3. Add CSS for compact keypad layout (fewer, larger keys)
4. Add `data-keyboard="numeric"` to frequency input in RadioControlPanel
5. Test: frequency dialog shows numpad, preset name shows QWERTY

**Validation**:
- Tap frequency input → compact numpad appears
- Tap preset name input → full QWERTY appears
- Numpad keys are larger (easier touch targets)
- Decimal point available on numpad for frequency entry

---

## Phase 13: AB13X USB Input Debug

**Status**: pending

**Goal**: Diagnose and fix why the AB13X USB audio input is no longer playing audio.

**Investigation Plan**:
1. SSH to Ubuntu, check if AB13X device is physically connected and recognized:
   - `lsusb | grep -i audio` or `AB13X`
   - `arecord -l` to list ALSA capture devices
   - `pw-cli list-objects | grep AB13X` to check PipeWire nodes
2. Check configuration:
   - `appsettings.Production.json` → `Devices:Radio:USBPort` or `Devices:GenericUSB`
   - Config store (SQLite) for persisted device config
3. Check logs for USB source initialization:
   - `journalctl -u radio-api | grep -i "USB\|AB13X\|capture"`
   - Look for "Could not find USB capture device" warning
4. Check if device name changed (PipeWire may report different name than MiniAudio)
5. Verify sample rate matching (48kHz capture ↔ 48kHz playback)
6. Test manual capture: `pw-record --target=<node-id> test.wav` to verify device works

**Potential Causes**:
- Device not configured in `appsettings.Production.json`
- Device name changed after PipeWire update
- USB device physically disconnected or hub issue
- Another process claimed exclusive access
- Sample rate mismatch after engine change

---

## Phase 14: Audio Distortion / CPU Investigation

**Status**: pending

**Goal**: Diagnose periodic audio distortion that correlates with CPU-intensive UI operations (loading Web UI, loading metrics page).

**Hypothesis**: CPU spikes from Blazor Server rendering or metrics DB queries starve the audio callback thread or ThreadPool, causing buffer underruns or lock contention.

**Evidence**:
- Distortion heard when starting Web UI (Blazor circuit init, component rendering)
- Distortion heard when loading metrics page (DB queries, card rendering)
- Shorter bursts than general distortion → suggests transient CPU spikes

**Investigation Plan**:
1. **Correlate distortion with metrics**: Monitor `audio.buffer.fill_percent` and `audio.buffer.underruns` while triggering UI load
2. **CPU profiling**: `top -p <pid>` or `htop` during UI load to see CPU spike
3. **ThreadPool starvation**: Check `ThreadPool.PendingWorkItemCount` during load — if high, async flushes from audio taps may be delayed
4. **PipeWire xruns**: `journalctl -u pipewire | grep xrun` during distortion events
5. **Buffer underrun logs**: Watch for "Buffer underrun" warnings in radio-api logs during UI load
6. **Isolate**: Does distortion happen with radio source only, or all sources?

**Potential Mitigations** (to test after diagnosis):
- Increase `BufferedSoundGenerator` ring buffer from 2.0s to 4.0s
- Increase PipeWire quantum from 512 to 1024 (adds latency but more headroom)
- Move metrics DB queries to a background thread with lower priority
- Add `ThreadPool.SetMinThreads()` to ensure minimum available threads
- Offload Blazor SSR to separate process (unlikely to be worth it)
- Add audio thread priority boosting (`nice -20` or SCHED_FIFO)

**Validation**:
- Load metrics page → no audible distortion
- Start Web UI → no audible distortion
- Sustained audio playback during heavy UI interaction → clean audio

---

## Decisions Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Start with audio preprocessing (Phase 1) | Cheapest test — if high-pass filter fixes vinyl AcoustID, we may not need SongRec/ACRCloud for vinyl at all | 2026-03-04 |
| SongRec before ACRCloud (Phase 2 before 3) | SongRec has no call limits; ACRCloud is limited to 5K/month. Test free option first. | 2026-03-04 |
| ACRCloud cancelled | SongRec working well for vinyl/radio song ID (~1s recognition). ACRCloud 5K/month not needed. | 2026-03-04 |
| RDS scope narrowed to station name only | SongRec handles song identification. RDS only needed for station name (PS) display on RadioControlPanel. | 2026-03-04 |
| No pitch correction for vinyl | User's direct-drive turntable doesn't have wow/flutter issues | 2026-03-04 |
| High-pass at ~80Hz for vinyl preprocessing | Spectrum shows strong sub-100Hz rumble energy even during silence | 2026-03-04 |
| SongRec as primary for live sources | AcoustID/Chromaprint designed for clean file matching; SongRec/Shazam designed for real-world recognition. SongRec proven across all live source types. | 2026-03-04 |
| Poll-based song boundary detection | Detect song changes when SongRec returns a different result. No complex audio analysis (silence detection, spectral flux) — unreliable across source types and not worth complexity. | 2026-03-04 |
| Strategy-per-source architecture cancelled | Simple if/else on source type sufficient. SongRec for live, AcoustID for files. No need for pluggable strategy framework. | 2026-03-04 |
| Album art: SongRec first, MusicBrainz fallback | SongRec/Shazam often returns cover art URLs. MusicBrainz/Cover Art Archive as fallback. Cache locally. | 2026-03-04 |

## Errors Encountered

| Error | Resolution | Phase |
|-------|------------|-------|
| | | |
