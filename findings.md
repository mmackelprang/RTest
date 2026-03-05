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

## Strategy Per Source Type (Updated)

| Source | Primary | Fallback | Notes |
|--------|---------|----------|-------|
| File | ID3 tags (embedded) | SongRec (if tags incomplete) | AcoustID being removed |
| Bluetooth | AVRCP metadata | SongRec (if AVRCP incomplete) | Event-driven on AVRCP arrival |
| FM Radio | SongRec (polling) | — | RDS provides station name only |
| Vinyl | SongRec (polling) | — | High-pass filter before capture |
| Generic USB | SongRec (polling) | — | Same as vinyl approach |

## Metrics Dashboard Architecture

### Current State
- `MetricsDashboardPage.razor`: flat grid of cards, custom SVG sparklines (~200×24px)
- Left panel: time range toggles (1h/24h/7d), category filter chips
- Right panel: metric cards (MudGrid xs=6 sm=4 md=3), click → aggregate stats
- No real charting library — hand-rolled SVG `<path>` for sparklines
- Data: SQLite with 3-tier rollup (minute/hour/day), retention 2h/48h/365d

### Charting Options
1. **MudBlazor `MudTimeSeriesChart`** — built-in, no new dependencies, Material Design styled
2. **BlazorChartjs (Chart.js wrapper)** — more features (zoom, tooltips, annotations), but adds JS dependency
3. **Custom SVG** — current approach, maximally lightweight but limited

### API Endpoints
- `GET /api/metrics/history` → time-series data (key, start, end, resolution)
- `GET /api/metrics/snapshots` → current values for batch of keys
- `GET /api/metrics/aggregate` → stats (count, avg, min, max, stddev)
- `GET /api/metrics/keys` → available metric names

### Data Resolution
- Minute data: 120 minutes retention → for 5m/1h views
- Hour data: 48 hours retention → for 24h view
- Day data: 365 days retention → for 7d/30d views

### Design References
- [Grafana dashboard best practices](https://grafana.com/docs/grafana/latest/visualizations/dashboards/build-dashboards/best-practices/) — F-pattern, <12 panels, progressive disclosure
- [Grafana stat panels](https://grafana.com/docs/grafana/latest/panels-visualizations/visualizations/stat/) — large value + sparkline + threshold colors
- [MudBlazor TimeSeriesChart](https://mudblazor.com/components/timeserieschart) — built-in Blazor chart component

## Audio Distortion / CPU Analysis

### Threading Model
- Audio callback thread: MiniAudio, runs every 5.3ms (512 samples @ 48kHz)
- Per-sample processing: 96k ops/sec on audio thread (Balance → Limiter → FingerprintTap → VizTap → OutputTap)
- Each modifier holds a per-sample lock briefly (~1μs)
- ThreadPool handles tap buffer flushes and FFT computation
- Risk: ThreadPool starvation delays flushes → lock contention on audio thread

### Known Buffer Settings
| Component | Size | Impact |
|-----------|------|--------|
| MiniAudio period | 512 samples (5.3ms) | Callback frequency |
| PipeWire quantum | 512 (10.67ms, tuned) | Min graph unit |
| BufferedSoundGenerator | 2.0s max | Source clock drift absorption |
| Fingerprint/Viz tap | 2048 samples (~42ms) | Lock batch size |
| OutputTap ring buffer | 2-5s | HTTP/Cast readers |

### Distortion Correlation with UI Load
- Blazor Server uses SignalR (WebSocket) — rendering happens server-side, serialized DOM diffs sent to client
- Loading metrics page: multiple HTTP calls to API (keys, snapshots, history for each metric)
- API serves metrics from SQLite — may hold write lock during flush, blocking reads
- Hypothesis: CPU spike from rendering + DB I/O starves ThreadPool → audio tap flushes delayed → callback stalls

## USB Audio (AB13X) Architecture

### Device Selection Flow
1. Config: `Devices:Radio:USBPort` or `Devices:GenericUSB:USBPort` = "AB13X"
2. `USBAudioSourceBase.InitializeUSBCaptureAsync("AB13X")`
3. Searches MiniAudio `CaptureDevices` for name containing "AB13X" (case-insensitive substring)
4. If not found → falls back to first available capture device (with warning log)
5. Capture format matched to playback engine (48kHz)

### Potential Failure Points
- Device not in `appsettings.Production.json` (overwritten on deploy)
- PipeWire reports different device name than expected
- USB hub power issue / device disconnected
- Another source already reserved the USB port
