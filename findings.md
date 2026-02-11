# Findings & Decisions

## Known Bugs (deferred — will fix soon)

### Bug 1: Album Art Fails to Load in Web UI
- **Symptom**: `Album art image failed to load: /api/albumart/xxx.png` in NowPlayingPanel
- **Root cause**: Album art URLs are relative (`/api/albumart/...`), browser resolves against Web server (port 5002), but files are cached by API server (port 5000)
- **Partial fix committed**: Web server proxies `/api/albumart/{filename}` to API server via `IHttpClientFactory("AlbumArtProxy")` — needs testing
- **Files**: `src/Radio.Web/Program.cs` (MapGet proxy endpoint)

### Bug 2: Play History Not Recording (may be fixed)
- **Symptom**: No play history entries for Bluetooth audio
- **Root cause**: `OnPlaybackStatusChanged` in BluetoothAudioSource had `IsAudioManagedByPlatform` guard — SMTC events weren't mirrored to source state when loopback enabled
- **Fix committed**: Removed the guard so SMTC events mirror state in both modes — needs testing
- **Files**: `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs`

### Bug 3: No Visualization Data (may be fixed)
- **Symptom**: Visualizer panel empty for Bluetooth audio
- **Root cause**: `PlayComponentAsync` didn't add `VisualizationTapModifier` (only `PlayFileAsync` did)
- **Fix committed**: Added tap modifier creation in `PlayComponentAsync` matching `PlayFileAsync` pattern — needs testing
- **Files**: `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs`

## Audio Pipeline Latency Findings

### Cast Audio Pipeline — Buffer Stages
| Stage | Component | Buffer Size | Duration | File |
|-------|-----------|------------|----------|------|
| 1 | SoundFlow MasterMixer | 1,024 samples | ~21ms | AudioEngineOptions.cs |
| 2 | FingerprintTapModifier | 4,096 samples | ~85ms | FingerprintTapModifier.cs:33 |
| 3 | TappedOutputStream ring | 960,000 bytes (5s) | write-through | TappedOutputStream.cs:42 |
| 4 | HTTP client buffer | 65,536 bytes | ~341ms | AudioOutputOptions.cs:147 |
| 5 | MP3 encoding (NAudio.Lame) | per-frame | ~50ms | HttpStreamOutput.cs:370 |
| 6 | Cast LoadAsync | SharpCaster 30s internal | ~500ms-5s | GoogleCastOutput.cs:554 |
| 7 | Cast device buffering | Chrome `<audio>` | ~5-15s | Chrome browser internal |

**25s observed = LoadAsync (~5s) + Chrome progressive buffering (~15-20s)**
The Default Media Receiver uses Chrome's `<audio>` element which buffers aggressively before starting playback.

### Volume Control — Current State
- **MasterVolume**: Passthrough to SoundFlow MasterMixer (AudioManager.cs:119-137)
- **Cast → app**: SetCastVolumeAsync exists (GoogleCastOutput.cs:926-946), one-way app→device only
- **Cast ← app**: No readback of Cast device volume. No subscription to volume change events.
- **Bluetooth**: No volume control in either Windows or Linux service
- **Persistence**: None. Volume lost on restart. AudioPreferences only stores CurrentSource.

### Fingerprinting — Current Behavior
- **Cycle**: Every 30s (BackgroundIdentificationService.cs:18)
- **Capture**: 15s audio for live sources, full-file for FilePlayer
- **API calls**: 1 (AcoustID only on miss) or 3 (AcoustID + MusicBrainz + CoverArt on match)
- **Dedup**: 5-minute suppression by "Title|Artist" key
- **Cache**: SQLite FingerprintCache + TrackMetadata tables
- **No track-change awareness**: Runs on blind interval regardless of whether track changed
- **NeedsFingerprintingLookup**: Set per source, but fingerprint cycle ignores it

## Previous Findings (Bluetooth Enablement — all resolved)

### Audio Capture Pipeline (RESOLVED)
- `SoundFlowDeviceManager.FindCaptureDeviceByName()` was returning string instead of AudioCaptureDevice — fixed
- Windows/Linux platform services now return proper capture devices
- WASAPI loopback capture added as alternative path for Windows

### Architecture Notes
- Radio.API (port 5000) and Radio.Web (port 5002) are separate processes
- Web communicates with API via HTTP clients configured in Program.cs
- SignalR hubs are hosted in API; Web connects as a client
- Both processes share `./data/` directory when run from same working directory
- Album art files stored in `./data/albumart/` by AlbumArtCacheService (API process)
