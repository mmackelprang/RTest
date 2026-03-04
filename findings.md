# Findings

## Current Fingerprinting Architecture

### Pipeline
```
Sources (Radio/SDR/File/BT/USB)
  → FingerprintTapModifier (captures float samples from mixer)
  → SoundFlowAudioTap (15s capture, silence detection, normalization)
  → ChromaprintFingerprintService (writes WAV, invokes fpcalc binary)
  → MetadataLookupService (cache → AcoustID → MusicBrainz → Cover Art Archive)
  → TrackMetadata (Title, Artist, Album, CoverArtUrl)
  → UI (SignalR events, /api/audio/fingerprint/status)
```

### Key Files
- `Radio.Infrastructure/Audio/Fingerprinting/ChromaprintFingerprintService.cs` — fpcalc invocation, WAV generation, normalization
- `Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs` — 15s interval loop, per-source logic, song change detection
- `Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs` — Cache → AcoustID → MusicBrainz chain
- `Radio.Infrastructure/Audio/Fingerprinting/SoundFlowAudioTap.cs` — Audio capture, silence detection
- `Radio.Infrastructure/Audio/SoundFlow/FingerprintTapModifier.cs` — Mixer-level audio tap
- `Radio.Infrastructure/Audio/Fingerprinting/AcoustIdClient.cs` — HTTP client for AcoustID API
- `Radio.Core/Configuration/FingerprintingOptions.cs` — All config options

### Source-Specific Behavior Today
- **File sources**: Fingerprints the actual file directly (bypasses capture). Works well.
- **Live sources (Radio, Vinyl, BT, USB)**: Captures 15s of mixer output, normalizes, runs through fpcalc. AcoustID often fails to match because captured segment duration doesn't match full track. System retries with fallback durations (180s, 210s, 240s, 270s, 300s) — inaccurate.
- **Bluetooth**: Can get AVRCP metadata (title/artist) directly. Falls back to fingerprinting if AVRCP unavailable.

### Current Limitations
1. **Vinyl**: Surface noise, turntable rumble (confirmed via spectrum — strong sub-100Hz energy even with no music), and EQ differences confuse Chromaprint
2. **FM Radio**: No RDS metadata extraction despite having RTL-SDR hardware. Relies entirely on acoustic fingerprinting, which is unreliable for live radio
3. **No audio preprocessing**: Raw mixer output goes straight to fpcalc with only amplitude normalization
4. **Single strategy**: All sources use the same Chromaprint/AcoustID pipeline. No way to swap in ACRCloud, SongRec, or RDS per source type

## Vinyl Low-Frequency Noise (Observed)
- Spectrum analyzer shows tall bars at sub-100Hz even with no music playing
- Likely turntable rumble (motor/bearing resonance) — common even on direct-drive
- This energy will pollute Chromaprint fingerprints
- Fix: High-pass filter (80-100Hz cutoff) on audio samples before passing to fpcalc
- Note: User's direct-drive turntable should NOT have wow/flutter issues, so pitch correction is unnecessary

## Alternative Metadata Sources

### 1. RDS (Radio Data System) for FM Radio
- **redsea** (github.com/windytan/redsea): Decodes RDS from RTL-SDR raw IQ stream
- Provides: Station name (PS), Radio Text (RT) — often contains song title/artist
- Primary metadata source for FM; fingerprinting as fallback
- Need to check if RTLSDRCore exposes raw IQ or RDS data

### 2. ACRCloud (acrcloud.com)
- Cloud-based audio recognition service
- Handles degraded audio (vinyl, radio) natively
- User has API key + usage token
- **Limitation: 5,000 calls/month** — must be used judiciously (not every 15s)
- Best as fallback when Chromaprint/AcoustID fails

### 3. SongRec (github.com/marin-m/SongRec)
- Open-source Shazam client (unofficial API)
- Handles degraded audio well (designed for noisy environments)
- No call limits (unofficial, personal use OK per user)
- Linux binary available, can be invoked like fpcalc
- Good for vinyl and radio where AcoustID struggles

## Strategy Per Source Type

| Source | Primary | Fallback | Notes |
|--------|---------|----------|-------|
| File | Chromaprint/AcoustID (file) | — | Already works well |
| Bluetooth | AVRCP metadata | Chromaprint/AcoustID | Already implemented |
| FM Radio | RDS via redsea | ACRCloud or SongRec | RDS is free, instant |
| Vinyl | Chromaprint + high-pass filter | SongRec or ACRCloud | Clean audio first |
| Generic USB | Chromaprint/AcoustID | SongRec or ACRCloud | Same as vinyl approach |
