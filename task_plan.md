# Task Plan

## Goal
Test and validate alternative metadata identification approaches for non-file audio sources (vinyl, FM radio). Improve fingerprinting accuracy with audio preprocessing, add RDS metadata for FM, and integrate alternative recognition services (ACRCloud, SongRec) as fallbacks.

## Current Phase
Phase 2 — SongRec Integration (complete, needs deployment + vinyl testing)

## Phases

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | Audio preprocessing for vinyl fingerprinting | complete | High-pass filter deployed, tested — AcoustID still can't match vinyl |
| 2 | SongRec integration (Shazam) | complete | Fallback recognizer wired into BackgroundIdentificationService |
| 3 | ACRCloud integration | pending | Cloud fallback, 5K/month limit |
| 4 | RDS metadata for FM radio | pending | redsea + RTL-SDR investigation |
| 5 | Strategy-per-source architecture | pending | Pluggable identification per source type |
| 6 | Fingerprint UI enhancements | pending | Source type display, color-coded results |

---

## Phase 1: Audio Preprocessing for Vinyl Fingerprinting

**Goal**: Add a high-pass filter to audio samples before fingerprinting to remove turntable rumble, then test if Chromaprint/AcoustID match rate improves for vinyl.

**Steps**:
1. Add high-pass filter (Butterworth 2nd-order, ~80Hz cutoff) in `ChromaprintFingerprintService` applied to samples before WAV generation — only for non-file sources
2. Deploy and test with vinyl playing known tracks
3. Compare match rates: before (current) vs after (filtered)
4. If significantly improved, ship it. If still poor, Phase 2 becomes higher priority.

**Key insight**: Spectrum analyzer confirms strong sub-100Hz energy from vinyl even with silence. This rumble pollutes Chromaprint hashes.

**Files to modify**:
- `Radio.Infrastructure/Audio/Fingerprinting/ChromaprintFingerprintService.cs` — add filtering step
- Possibly `SoundFlowAudioTap.cs` — if filtering should happen at capture time

**Validation**:
- Play 5 known vinyl tracks, check if AcoustID matches
- Compare fingerprint hash lengths (short = bad, 800+ chars = good)
- Check logs for match confidence scores

---

## Phase 2: SongRec Integration (Shazam)

**Goal**: Add SongRec as an alternative fingerprinting service for sources where AcoustID struggles.

**Steps**:
1. Research SongRec CLI interface — how to invoke, what audio format it needs, what it returns
2. Install SongRec on Ubuntu deployment target
3. Create `SongRecFingerprintService` implementing a new `IAlternativeLookupService` interface
4. Wire into `BackgroundIdentificationService` as fallback when AcoustID returns no match
5. Test with vinyl and radio sources

**Key facts**:
- SongRec is a CLI tool (like fpcalc) — can be invoked as a subprocess
- Handles noisy/degraded audio natively (Shazam's algorithm is designed for it)
- No API call limits (unofficial Shazam client, personal use)
- Available on Linux (apt install or build from source)

**Validation**:
- Play 5 known vinyl tracks that AcoustID fails to match
- Play FM radio and check recognition rate
- Measure latency (SongRec vs AcoustID)

---

## Phase 3: ACRCloud Integration

**Goal**: Add ACRCloud as a cloud-based recognition fallback with rate limiting.

**Steps**:
1. Research ACRCloud API — REST endpoint, auth, audio format requirements
2. Create `ACRCloudLookupService` with rate limiting (budget: ~160 calls/day from 5K/month)
3. Store API key in secrets store (existing `${secret:...}` infrastructure)
4. Wire as final fallback after AcoustID + SongRec fail
5. Add rate tracking to fingerprint status UI

**Key constraints**:
- 5,000 calls/month — must be used sparingly
- Only call when other methods fail AND source is actively playing
- Longer duplicate suppression window for ACRCloud matches (save quota)

**Validation**:
- Test with tracks that AcoustID and SongRec both miss
- Verify rate limiting works (never exceeds budget)
- Check that secrets store handles ACRCloud credentials

---

## Phase 4: RDS Metadata for FM Radio

**Goal**: Extract Radio Data System (RDS) metadata from FM broadcasts as primary metadata source for radio.

**Steps**:
1. Investigate if RTLSDRCore exposes raw IQ data or RDS decoding
2. Research redsea CLI — can it read from RTL-SDR while our app is using it?
3. If RTLSDRCore has RDS: add RDS text parsing to SDRRadioAudioSource
4. If not: evaluate running redsea as a sidecar process with shared SDR access
5. Parse RDS Radio Text (RT) for "Artist - Title" patterns
6. Use RDS as primary metadata, fingerprinting as fallback for stations without RDS

**Key challenge**: RTL-SDR is a single-user device. If our app is using it for audio demodulation, redsea can't access it simultaneously unless RTLSDRCore exposes RDS data.

**Validation**:
- Tune to FM station known to broadcast RDS
- Verify station name (PS) and radio text (RT) are captured
- Check if song title/artist appears in RT field

---

## Phase 5: Strategy-Per-Source Architecture

**Goal**: Refactor identification pipeline to support pluggable strategies per source type.

**Steps**:
1. Define `IIdentificationStrategy` interface with `IdentifyAsync(AudioSampleBuffer, AudioSourceType)` method
2. Create strategy implementations: `ChromaprintStrategy`, `SongRecStrategy`, `ACRCloudStrategy`, `RDSStrategy`
3. Create `IdentificationStrategyResolver` that selects strategy chain per source type
4. Refactor `BackgroundIdentificationService` to use strategy resolver instead of hardcoded Chromaprint path
5. Make strategy chains configurable via `appsettings.json`

**Default chains**:
- File: Chromaprint/AcoustID only
- Bluetooth: AVRCP → Chromaprint/AcoustID
- FM Radio: RDS → SongRec → ACRCloud
- Vinyl: Chromaprint+filter → SongRec → ACRCloud
- Generic USB: Chromaprint → SongRec → ACRCloud

---

## Phase 6: Fingerprint UI Enhancements

**Goal**: Update fingerprint status UI to show identification method and color-code results.

**Steps**:
1. Add `IdentificationMethod` field to status events (Chromaprint, SongRec, ACRCloud, RDS, AVRCP)
2. Color-code results: green = matched, orange = no match, red = error
3. Show which strategy was used for each identification attempt
4. Show rate limits for ACRCloud (calls used / remaining this month)

---

## Decisions Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Start with audio preprocessing (Phase 1) | Cheapest test — if high-pass filter fixes vinyl AcoustID, we may not need SongRec/ACRCloud for vinyl at all | 2026-03-04 |
| SongRec before ACRCloud (Phase 2 before 3) | SongRec has no call limits; ACRCloud is limited to 5K/month. Test free option first. | 2026-03-04 |
| RDS after SongRec (Phase 4) | RDS requires RTL-SDR investigation and may have device sharing constraints. SongRec is a simpler integration. | 2026-03-04 |
| No pitch correction for vinyl | User's direct-drive turntable doesn't have wow/flutter issues | 2026-03-04 |
| High-pass at ~80Hz for vinyl preprocessing | Spectrum shows strong sub-100Hz rumble energy even during silence | 2026-03-04 |

## Errors Encountered

| Error | Resolution | Phase |
|-------|------------|-------|
| | | |
