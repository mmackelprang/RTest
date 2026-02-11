# Audio Dataflow & Latency Analysis

## Overview

This document traces every audio sample from source to output, documenting buffer sizes, latency contributions, encoding overhead, and optimization opportunities. It covers three parallel pipelines: local playback, Google Cast streaming, and the fingerprinting/metadata system.

## Audio Format Constants

| Parameter | Value | Notes |
|-----------|-------|-------|
| Sample Rate | 48,000 Hz | Configurable via `AudioEngine:SampleRate` |
| Channels | 2 (stereo) | Configurable via `AudioEngine:Channels` |
| Engine Sample Format | 32-bit float | SoundFlow internal format |
| Stream Sample Format | 16-bit PCM | Converted in TappedOutputStream |
| PCM Byte Rate | 192,000 bytes/s | 48000 * 2ch * 2 bytes |
| Engine Buffer Size | 1,024 samples | ~21.3ms per callback |

---

## Pipeline Diagram

```
Audio Sources
  ├── FilePlayerAudioSource (SoundPlayer → StreamDataProvider → file)
  ├── SDRRadioAudioSource (BufferedSoundGenerator<float> → RTL-SDR USB)
  ├── BluetoothAudioSource (BufferedSoundGenerator<float> → WASAPI loopback or capture device)
  ├── VinylAudioSource (BufferedSoundGenerator<short> → line-in capture)
  └── GenericUSBAudioSource (BufferedSoundGenerator<short> → USB capture)
            │
            ▼
  ┌─────────────────────────────────────────────────────────┐
  │  PlaybackDevice.MasterMixer                             │
  │  (SoundFlow Mixer — mixes all active sources)           │
  │                                                         │
  │  Modifiers (processed in order per sample):             │
  │    1. BalanceModifier — stereo L/R gain                 │
  │    2. FingerprintTapModifier — copies samples to ring   │
  │       buffer (TappedOutputStream) for HTTP streaming    │
  │       and fingerprinting                                │
  └─────────────────────────────────────────────────────────┘
            │                         │
            ▼                         ▼
   Local Playback Device     TappedOutputStream (ring buffer)
   (speakers/headphones)              │
                              ┌───────┴───────┐
                              ▼               ▼
                      HTTP Stream         Fingerprint
                      Clients             Sample Provider
                         │                     │
                    ┌────┴────┐          BackgroundIdentificationService
                    ▼         ▼                │
                Raw WAV    MP3 (LAME)    fpcalc → AcoustID → MusicBrainz
                clients    Cast device         │
                                         TrackIdentified event
                                               │
                                         AudioManager → PlayHistory DB
                                               │
                                         AudioStateUpdateService
                                               │
                                         SignalR → Web UI
                                               │
                                         Cast Metadata Update
```

---

## Stage-by-Stage Latency Breakdown

### Stage 1: Audio Source → MasterMixer

| Source Type | Buffer | Latency | File |
|-------------|--------|---------|------|
| FilePlayer (SoundPlayer) | 1,024 samples | ~21ms | SoundFlowPlaybackService.cs |
| SDR Radio (BufferedSoundGenerator) | Varies (USB transfer) | ~21ms + USB | SDRRadioAudioSource.cs |
| Bluetooth (BufferedSoundGenerator) | Varies (WASAPI/capture) | ~21ms + BT | BluetoothAudioSource.cs |
| Vinyl/USB (BufferedSoundGenerator) | Varies (capture) | ~21ms | Capture device dependent |

**Details:**
- SoundFlow's MiniAudioEngine runs an audio callback at `BufferSize` intervals (1,024 samples = 21.3ms at 48kHz)
- Each callback: MasterMixer reads `BufferSize` samples from each active source, mixes them, then runs modifiers
- Source generators are polled for exactly `BufferSize` samples per callback
- Configuration: `AudioEngine:BufferSize` (default 1024)

### Stage 2: BalanceModifier

| Parameter | Value | Latency |
|-----------|-------|---------|
| Processing | Per-sample multiplication | ~0ms (inline) |
| Channels affected | L=0, R=1 | Gain from SoundFlowMasterMixer |

**Details:**
- Reads `GetLeftChannelGain()` / `GetRightChannelGain()` from master mixer per sample
- Purely computational — no buffering, no copies
- File: `BalanceModifier.cs`

### Stage 3: FingerprintTapModifier → TappedOutputStream

| Parameter | Value | Latency |
|-----------|-------|---------|
| Internal buffer | 4,096 float samples | ~85ms accumulation |
| Flush frequency | Every 4,096 samples | ~4 times per engine buffer period |
| Float→PCM conversion | Per sample in WriteFromEngine | Inline |
| Lock contention | One lock per batch write | Negligible |

**Details:**
- `ProcessSample()` is called per-sample, per-channel by SoundFlow
- Accumulates samples into `_sampleBuffer[4096]`
- When full: copies array, calls `_audioEngine.WriteToOutputTap(samplesForTap)`
- `WriteToOutputTap()` → `TappedOutputStream.WriteFromEngine()`:
  - Clamps float to [-1, 1]
  - Converts to 16-bit PCM (`short`)
  - Writes 2 bytes per sample (little-endian) to ring buffer
  - Advances `_writePosition` (wraps at buffer end)
- Ring buffer size: `48000 * 2ch * 2bytes * 5s = 960,000 bytes` (~5 seconds)
- Configuration: `AudioEngine:OutputBufferSizeSeconds` (default 5), FingerprintTapModifier buffer size (hardcoded 4096)
- Files: `FingerprintTapModifier.cs:33-37`, `TappedOutputStream.cs:35-43`

**Latency impact:**
- The 4,096-sample batch adds up to 85ms of latency before data reaches TappedOutputStream
- Once written, data is immediately available to all readers
- Per-sample lock in ProcessSample is a performance concern — discussed in Optimization section

### Stage 4: TappedOutputStream → HTTP Stream Clients

| Parameter | Value | Latency |
|-----------|-------|---------|
| Ring buffer | 960,000 bytes (5s) | Write-through (0ms added) |
| Reader model | Per-client independent cursor | No reader blocks another |
| Client read buffer | 65,536 bytes | Up to ~341ms accumulation |
| Read polling | 10ms sleep on empty | Up to 10ms idle delay |

**Details:**
- Each HTTP client gets a `TappedOutputStreamReader` via `CreateReader()`
- Reader starts at current write position (only receives new data)
- `ReadForReader()`: copies bytes from ring to client buffer, advances reader position
- HttpStreamOutput read loop: `audioStream.ReadAsync(buffer[65536])` → write to response
- If 0 bytes available, sleeps 10ms and retries
- Configuration: `AudioOutput:HttpStream:ClientBufferSize` (default 65536)
- Files: `TappedOutputStream.cs:114-136`, `HttpStreamOutput.cs:389-396`

**Latency impact:**
- Ring buffer is write-through — no intentional pre-fill delay
- Client buffer of 64KB = 341ms at 192KB/s PCM rate
- But `ReadAsync` returns whatever is available (may be less than 64KB)
- Real delay depends on how fast the client consumes vs. how fast data arrives

### Stage 5: MP3 Encoding (Cast path only)

| Parameter | Value | Latency |
|-----------|-------|---------|
| Encoder | NAudio.Lame (LAME MP3) | ~26ms per frame |
| Bitrate | 192 kbps CBR | Fixed |
| Frame size | 1,152 samples @ 48kHz | ~24ms per frame |
| Encoding overhead | ~2ms per frame | CPU-dependent |

**Details:**
- Only for `/stream/audio/mp3` endpoint (used by Google Cast)
- `LameMP3FileWriter` wraps the HTTP response output stream
- PCM data written via `mp3Writer.Write(buffer, 0, bytesRead)`
- LAME accumulates 1,152 samples (one MP3 frame), encodes, writes frame to stream
- At 192 kbps CBR: each frame is 576 bytes output
- Configuration: Bitrate hardcoded at 192 in `HttpStreamOutput.cs:370`
- Files: `HttpStreamOutput.cs:363-383`, `HttpStreamOutput.cs:426-429`

**Latency impact:**
- MP3 frame buffering adds ~24ms latency per frame
- LAME encoder look-ahead adds ~576 samples (~12ms) additional delay
- Total MP3 encoding latency: ~36ms
- MP3 format is REQUIRED for Cast — Chrome's `<audio>` element cannot handle chunked WAV

### Stage 6: Google Cast LoadAsync

| Parameter | Value | Latency |
|-----------|-------|---------|
| App launch | CC1AD845 (Default Media Receiver) | 500ms-3s |
| Post-launch delay | `Task.Delay(500)` | 500ms (hardcoded) |
| LoadAsync timeout | 5s initial + 30s background | SharpCaster internal |
| StreamType | Buffered | Chrome decides buffer strategy |

**Details:**
- `StartAsync()` in GoogleCastOutput:
  1. Launches Default Media Receiver app (`CC1AD845`) — 500ms-3s
  2. Waits 500ms for app initialization — `Task.Delay(500)` at line 518
  3. Builds `Media` object with MP3 stream URL + metadata
  4. Calls `mediaChannel.LoadAsync(media, true)` — autoplay=true
  5. If LoadAsync doesn't complete in 5s, monitors in background
- SharpCaster's internal `DoNotReturnOnLoading` flag means LoadAsync waits up to 30s
- Files: `GoogleCastOutput.cs:494-605`

**Latency impact:**
- This is a one-time cost per Cast session start, NOT per-audio-sample
- App launch: 500ms-3s (depends on Cast device state)
- Mandatory delay: 500ms
- LoadAsync: 500ms-5s typical
- **Total session start: 1.5s-8.5s**

### Stage 7: Cast Device Progressive Buffering

| Parameter | Estimated Value | Notes |
|-----------|----------------|-------|
| Chrome `<audio>` initial buffer | 5-15s | Depends on Cast device |
| Network RTT | 1-5ms | LAN only |
| MP3 frame parsing | <1ms | Trivial |

**Details:**
- The Default Media Receiver is a Chrome browser instance running on the Cast device
- Chrome's `<audio>` element buffers progressively before starting playback
- For `StreamType.Buffered`, Chrome waits until it has "enough" data
- Google does not document the exact buffering strategy
- Empirically observed: 10-20 seconds of audio buffered before first playback
- This is the DOMINANT latency source in the Cast pipeline

**Measured total Cast latency: ~25 seconds**
- Session start (Stages 6): ~5s
- Chrome buffering (Stage 7): ~15-20s
- Pipeline buffering (Stages 3-5): ~0.5s

---

## Local Playback Latency

For comparison, the local playback path has minimal latency:

| Stage | Latency |
|-------|---------|
| Source → MasterMixer | ~21ms (1024 samples) |
| BalanceModifier | ~0ms |
| FingerprintTapModifier | ~0ms (passthrough) |
| MasterMixer → DAC | ~21ms (1024 samples) |
| DAC → speaker | ~1-5ms |
| **Total** | **~43-47ms** |

---

## Fingerprinting Pipeline

### Cycle Timing

| Parameter | Value | Config Key |
|-----------|-------|------------|
| Startup delay | 5 seconds | Hardcoded |
| Cycle interval | 30 seconds | `Fingerprinting:IdentificationIntervalSeconds` |
| Sample duration | 15 seconds | `Fingerprinting:SampleDurationSeconds` |
| Duplicate suppression | 5 minutes | `Fingerprinting:DuplicateSuppressionMinutes` |
| Min confidence | 0.5 | `Fingerprinting:MinimumConfidenceThreshold` |

### Cycle Breakdown

```
Every 30 seconds:
  1. Check enabled + active source (1ms)
  2. Branch:
     a. File source: call fpcalc on file directly (100-500ms)
     b. Live source: capture 15s audio from tap (15,000ms real-time)
  3. Generate fingerprint via fpcalc (200-500ms)
  4. Check local cache (SQLite) (5-10ms)
  5. If cache miss:
     a. AcoustID lookup (500-2000ms, network)
     b. If match: MusicBrainz lookup (500-2000ms, network)
     c. If match: Cover Art Archive lookup (500-2000ms, network)
  6. Store in cache (5-10ms)
  7. Fire TrackIdentified event (1ms)
  8. Duplicate suppression check (1ms)
```

### API Call Analysis

| Scenario | API Calls | Network Time |
|----------|-----------|-------------|
| Cache hit | 0 | 0ms |
| AcoustID miss | 1 | 500-2000ms |
| Full identification | 3 (AcoustID + MusicBrainz + CoverArt) | 1500-6000ms |

### Current Inefficiencies

1. **Blind 30s interval**: Runs regardless of track change — wastes API calls on same track
2. **File sources re-fingerprint**: Even when file has complete ID3 tags (title+artist+album+art)
3. **No track-change detection**: FilePlayer could trigger on track change instead of timer
4. **Bluetooth metadata ignored**: AVRCP may provide complete metadata, making fingerprinting unnecessary
5. **`NeedsFingerprintingLookup` not checked**: Sources set this flag but `BackgroundIdentificationService` ignores it
6. **Dedup only 5 minutes**: High-confidence matches could be suppressed much longer

### Optimization Recommendations (Phase 5)

| Optimization | Saves | Effort |
|-------------|-------|--------|
| Skip if complete metadata (title+artist+album) | 50-80% of API calls for file/BT | Low |
| Trigger on track change (FilePlayer) | All redundant same-track cycles | Medium |
| Trigger on AVRCP metadata change (BT) | Redundant cycles | Medium |
| Check `NeedsFingerprintingLookup` flag | Avoids cycles for identified tracks | Low |
| Extend dedup to 30min for >0.9 confidence | Reduces repeat lookups | Low |
| Local recording-ID cache (skip MusicBrainz if seen) | 1 API call per known recording | Low |

---

## Volume Control Pipeline

### Current State

```
User adjusts volume
  → IAudioManager.MasterVolume (AudioManager.cs)
    → SoundFlowMasterMixer.Volume
      → SoundFlowAudioEngine.OnMasterVolumeChanged()
        → playbackDevice.MasterMixer.Volume = GetEffectiveVolume()

For Cast (one-way, app → device):
  → AudioOutputBase.Volume setter
    → OnVolumeChanged() → SetCastVolumeAsync()
      → ReceiverChannel.SetVolume(volume)
```

### Missing Bidirectional Sync

**Cast → App (not implemented):**
- SharpCaster's `ReceiverChannel` emits status events including volume
- Need to subscribe to `ReceiverChannel.StatusChanged` or poll `GetStatus()`
- When Cast volume changes externally (Google Home app, voice), update `IAudioManager.MasterVolume`

**Bluetooth ↔ App (not implemented):**
- Windows: AVRCP absolute volume via WinRT `AudioPlaybackConnection` or `MediaTransportControls`
- Linux: BlueZ `org.bluez.MediaTransport1.Volume` property via D-Bus
- Bidirectional: both read on connect and subscribe to changes

### Volume Persistence (not implemented)
- No volume persistence — resets to defaults on restart
- Pattern: follow existing `AudioPreferences` in AudioManager
- Debounce saves (500ms idle) to avoid SQLite churn during slider drags

---

## Cast Latency Optimization Options

### Option 1: Reduce Chrome Buffering (StreamType.Live)

**What:** Change `StreamType.Buffered` to `StreamType.Live` in `BuildMedia()`
**Expected improvement:** Potentially 5-15s — Chrome may start playback sooner
**Effort:** Trivial (1 line change)
**Risk:** Low — may not work (Chrome may ignore hint for HTTP streams)
**File:** `GoogleCastOutput.cs:848` — `StreamType = StreamType.Buffered`

### Option 2: Reduce FingerprintTap Batch Size

**What:** Reduce from 4,096 to 1,024 samples
**Expected improvement:** ~64ms (85ms → 21ms)
**Effort:** Trivial (1 constant change)
**Risk:** More frequent lock acquisitions in TappedOutputStream, slightly more overhead
**File:** `FingerprintTapModifier.cs:37` — `bufferSize = 4096`

### Option 3: Reduce HTTP Client Buffer

**What:** Reduce from 65,536 to 16,384 bytes
**Expected improvement:** ~256ms (341ms → 85ms)
**Effort:** Trivial (config change)
**Risk:** More frequent HTTP writes, slightly more TCP overhead
**Config:** `AudioOutput:HttpStream:ClientBufferSize`

### Option 4: Pre-warm HTTP Stream Before Cast Load

**What:** Start HTTP stream server and verify data flowing before calling `LoadAsync`
**Expected improvement:** 0-2s — eliminates initial empty-buffer stalls
**Effort:** Low — add an HTTP self-check before LoadAsync
**Risk:** Negligible
**File:** `GoogleCastOutput.cs:520-565`

### Option 5: Remove Post-Launch Delay

**What:** Remove or reduce the 500ms `Task.Delay` after app launch
**Expected improvement:** 500ms
**Effort:** Trivial
**Risk:** May cause `LoadAsync` to fail on slower Cast devices
**File:** `GoogleCastOutput.cs:518`

### Option 6: Alternative Cast Receiver App

**What:** Build a Custom Receiver that uses `<audio>` with `preload="none"` and minimal buffering
**Expected improvement:** 10-20s — eliminates Chrome's aggressive buffering
**Effort:** High — requires hosting a web app, registering with Google
**Risk:** Maintenance burden, Cast SDK certification

### Option 7: Switch to StreamType.Live + Opus/WebM

**What:** Use Opus codec in WebM container instead of MP3, with `StreamType.Live`
**Expected improvement:** Potentially significant — Opus designed for low-latency
**Effort:** High — need Opus encoder, WebM muxer, verify Cast compatibility
**Risk:** Cast Default Media Receiver may not support Opus/WebM well

### Option 8: PoC Latency Measurement Tool

**What:** Create `tools/Radio.Tools.CastLatencyTest` that:
  1. Starts HTTP stream with a known signal (sine wave burst)
  2. Loads media on Cast device
  3. Captures Cast device audio via loopback
  4. Measures time delta between signal generation and reception
**Expected improvement:** 0 (diagnostic only, but enables measuring other improvements)
**Effort:** Medium
**Risk:** None

### Recommended Priority (effort vs. impact)

| Priority | Option | Expected Gain | Effort |
|----------|--------|--------------|--------|
| 1 | StreamType.Live | 5-15s | Trivial |
| 2 | Pre-warm HTTP stream | 0-2s | Low |
| 3 | Remove/reduce post-launch delay | 500ms | Trivial |
| 4 | Reduce FingerprintTap batch | 64ms | Trivial |
| 5 | Reduce HTTP client buffer | 256ms | Trivial |
| 6 | Custom Cast receiver | 10-20s | High |

Options 1-5 combined should reduce Cast latency from ~25s to ~5-10s with minimal code changes. Option 1 (StreamType.Live) is the single highest-impact change to try first.

---

## SignalR State Broadcasting

### AudioStateUpdateService Poll Cycle

| Parameter | Value |
|-----------|-------|
| Poll interval | 500ms |
| Change detection | Cached last state, diff comparison |
| Float tolerance | 0.001 |

### Broadcast Channels

| Event | Group | Triggered By |
|-------|-------|-------------|
| PlaybackStateChanged | All clients | Play/pause/stop/volume/position change |
| NowPlayingChanged | All clients | Track change, metadata update |
| QueueChanged | "Queue" group | Queue add/remove/reorder |
| RadioStateChanged | "RadioState" group | Frequency/band/signal change |
| VolumeChanged | All clients | Volume/mute/balance change |

### Metadata → Cast Flow

When `NowPlayingChanged` fires and Cast is streaming:
1. AudioStateUpdateService detects metadata change (500ms poll)
2. Resolves album art URL to absolute (relative `/api/albumart/X` → `http://192.168.x.x:5000/api/albumart/X`)
3. Calls `GoogleCastOutput.UpdateNowPlayingMetadataAsync()`
4. Which calls `mediaChannel.LoadAsync(media, true)` — reloads media with new metadata
5. Cast device reconnects to same stream URL (no audio interruption for HTTP streams)

**Latency:** 500ms (poll) + 500-2000ms (LoadAsync) = 1-2.5s for metadata to appear on Cast UI

---

## Configuration Reference

All configurable values that affect latency:

```json
{
  "AudioEngine": {
    "SampleRate": 48000,
    "Channels": 2,
    "BufferSize": 1024,
    "OutputBufferSizeSeconds": 5
  },
  "AudioOutput": {
    "HttpStream": {
      "Port": 8080,
      "EndpointPath": "/stream/audio",
      "ClientBufferSize": 65536,
      "SampleRate": 48000,
      "Channels": 2,
      "BitsPerSample": 16
    },
    "GoogleCast": {
      "Enabled": false,
      "DiscoveryTimeoutSeconds": 10,
      "DefaultVolume": 0.7,
      "ReconnectDelaySeconds": 5
    }
  },
  "Fingerprinting": {
    "IdentificationIntervalSeconds": 30,
    "SampleDurationSeconds": 15,
    "DuplicateSuppressionMinutes": 5,
    "MinimumConfidenceThreshold": 0.5
  }
}
```

### Hardcoded Values (require code changes)

| Value | Location | Current | Description |
|-------|----------|---------|-------------|
| FingerprintTap batch size | `FingerprintTapModifier.cs:37` | 4,096 samples | Buffer before writing to ring |
| MP3 bitrate | `HttpStreamOutput.cs:370` | 192 kbps CBR | LAME encoder bitrate |
| Cast StreamType | `GoogleCastOutput.cs:848` | Buffered | Chrome buffering strategy |
| Cast post-launch delay | `GoogleCastOutput.cs:518` | 500ms | Delay after app launch |
| Cast LoadAsync timeout | `GoogleCastOutput.cs:557` | 5s | Initial response timeout |
| State update interval | `AudioStateUpdateService.cs` | 500ms | SignalR poll cycle |
| Fingerprint startup delay | `BackgroundIdentificationService.cs` | 5s | Initial wait |
| HTTP empty-read sleep | `HttpStreamOutput.cs:410` | 10ms | Polling interval |
