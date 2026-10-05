# Direct Cast Channel Audio Streaming

## Overview

Direct Cast Channel streaming is an experimental alternative to the standard HTTP-based
Cast audio pipeline. Instead of encoding audio as MP3, serving it over HTTP, and having the
Cast device fetch it, this mode sends raw audio chunks **directly over the Cast protocol's
custom message bus**. The receiver decodes and plays them using the Web Audio API.

### Standard HTTP MP3 Pipeline (default)

```
Audio Engine → TappedOutputStream → MP3 Encode (LAME) → HTTP Server (port 8080)
                                                              ↑
                                         Cast device fetches via HTTP GET
                                              (4-10s end-to-end latency)
```

### Direct Channel Pipeline (experimental)

```
Audio Engine → TappedOutputStream → WAV Header + PCM → Base64 → JSON message
                                                              ↓
                                   Cast device receives via custom message bus
                                       Web Audio API decodes and plays
                                         (target: <1s end-to-end latency)
```

## Why This Exists

The HTTP MP3 path introduces 4-10 seconds of latency due to:
1. **MP3 encoding** - LAME encoder introduces frame-level latency
2. **HTTP buffering** - Chrome's media element buffers several seconds before playing
3. **MP3 decoding** - Frame reassembly on the receiver adds delay
4. **No back-pressure** - The Cast device fetches at its own pace

Direct Channel eliminates all of these by sending audio synchronously over the same
WebSocket-based Cast protocol connection that's already established for control messages.

## Audio Math

The audio engine outputs **48kHz, 16-bit, stereo PCM** = **192,000 bytes/second**.

| Chunk Size | PCM Bytes | Base64 Size | Messages/sec | Within 64KB Limit |
|------------|-----------|-------------|--------------|-------------------|
| 50ms       | 9,600     | ~12.8KB     | 20           | Yes               |
| 100ms      | 19,200    | ~25.6KB     | 10           | Yes               |
| 200ms      | 38,400    | ~51.2KB     | 5            | Yes               |

**Default: 100ms chunks** — good balance of latency vs. overhead. All sizes are well
within the Cast protocol's 64KB message limit.

### Bandwidth

At 100ms chunks: 25.6KB * 10/sec = **256 KB/sec** (~2 Mbps) over the Cast TLS connection.
Compare to the HTTP MP3 path at 192kbps = 24 KB/sec. Direct Channel uses ~10x more
bandwidth but stays well within typical Wi-Fi capacity.

## Protocol

### Message Format (Sender → Receiver)

Audio chunks are sent as JSON on a custom namespace:

```json
{
  "type": "audio",
  "data": "<base64-encoded-WAV>",
  "seq": 42
}
```

- `type`: Always `"audio"` for audio data
- `data`: Complete, self-contained WAV file (44-byte header + PCM data), Base64-encoded.
  Each chunk is independently decodable by `AudioContext.decodeAudioData()`.
- `seq`: Monotonically increasing sequence number for gap detection

### Control Messages

```json
{ "type": "stop" }     // Stop playback and reset scheduler
{ "type": "ping" }     // Request status from receiver
```

### Status Response (Receiver → Sender)

```json
{
  "type": "pong",
  "chunksReceived": 1000,
  "chunksPlayed": 998,
  "currentTime": 45.123,
  "nextPlayTime": 45.223,
  "state": "running"
}
```

Note: In SharpCaster v3.0.0, `RegisterChannel()` is not available, so the sender
cannot receive messages back from the receiver. The receiver-side status is primarily
for debugging via Chrome DevTools on the Cast device. If bidirectional communication
is needed, a newer version of SharpCaster or a custom transport layer would be required.

## Architecture

### Components

```
┌─────────────────────────────────────────────────────────────┐
│  Radio.API (Host)                                           │
│                                                             │
│  AudioEngineInitializationService                           │
│    └── If DirectChannel: castOutput.SetAudioEngine(engine)  │
│                                                             │
│  DevicesController                                          │
│    └── ConnectToCastDevice: branches on StreamingMode       │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Radio.Infrastructure                                       │
│                                                             │
│  GoogleCastOutput                                           │
│    ├── StartAsync() branches on StreamingMode:              │
│    │   ├── "HttpMp3"  → LoadMediaOnCastAsync() [existing]   │
│    │   └── "DirectChannel" → StartDirectChannelAsync()      │
│    │       ├── Creates DirectCastAudioChannel               │
│    │       ├── Creates DirectCastStreamingService            │
│    │       └── Starts background streaming loop             │
│    └── StopAsync() / DisposeAsync() → cleanup               │
│                                                             │
│  DirectCastAudioChannel : ChromecastChannel                 │
│    └── SendMessageAsync(payload, transportId)               │
│        → ChromecastChannel.SendAsync(payload, destinationId)│
│                                                             │
│  DirectCastStreamingService                                 │
│    └── StreamingLoopAsync():                                │
│        1. Read PCM from TappedOutputStreamReader            │
│        2. WavChunkEncoder.Encode() → WAV bytes              │
│        3. Base64 encode → JSON message                      │
│        4. DirectCastAudioChannel.SendMessageAsync()         │
│                                                             │
│  WavChunkEncoder (static)                                   │
│    └── Encode(pcmData, length, sampleRate, channels)        │
│        → 44-byte RIFF/WAVE header + PCM data                │
└─────────────────────────────────────────────────────────────┘
                            │
                Cast Protocol (TLS WebSocket)
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Custom Cast Receiver (docs/receiver-direct-channel.html)   │
│                                                             │
│  1. context.addCustomMessageListener(namespace, handler)    │
│  2. Base64 decode → ArrayBuffer                             │
│  3. audioCtx.decodeAudioData() → AudioBuffer                │
│  4. BufferSourceNode.start(nextPlayTime) → gapless playback │
│                                                             │
│  Scheduling:                                                │
│    nextPlayTime tracks the end of the last scheduled buffer │
│    Each new buffer starts exactly where the previous ended  │
│    If nextPlayTime < now (underrun), reset with 50ms buffer │
└─────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

1. **WAV, not raw PCM**: Each chunk includes a WAV header so the receiver can use
   `decodeAudioData()` without needing to know the audio format a priori. The 44-byte
   overhead per chunk is negligible (~0.2% of a 100ms chunk).

2. **Self-contained chunks**: Each message is independently decodable. No state
   carries between chunks. This makes the protocol resilient to dropped messages.

3. **Sequence numbers**: Enable gap detection on the receiver side. If `seq` jumps,
   the receiver logs a warning. Future enhancement: request retransmission.

4. **Reader pacing**: `TappedOutputStreamReader.ReadAsync()` naturally paces to
   real-time when no data is available. The streaming loop doesn't need its own
   timer — it reads as fast as audio is produced.

5. **Graceful fallback**: If `SetAudioEngine()` wasn't called or the transport ID
   is unavailable, `StartDirectChannelAsync()` falls back to the HTTP MP3 path
   automatically.

## Configuration

In `appsettings.json` under `AudioOutput.GoogleCast`:

```json
{
  "AudioOutput": {
    "GoogleCast": {
      "StreamingMode": "DirectChannel",
      "DirectChannelChunkSizeMs": 100,
      "DirectChannelNamespace": "urn:x-cast:com.radioconsole.audio",
      "ApplicationId": "<your-custom-receiver-app-id>"
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `StreamingMode` | `"HttpMp3"` | `"HttpMp3"` or `"DirectChannel"` |
| `DirectChannelChunkSizeMs` | `100` | Chunk size (50-200ms). Lower = less latency, more overhead |
| `DirectChannelNamespace` | `"urn:x-cast:com.radioconsole.audio"` | Must match the receiver |
| `ApplicationId` | `"CC1AD845"` | Must be a custom receiver app ID for DirectChannel |

### Setting Up the Custom Receiver

See **[docs/direct-channel-setup-guide.md](../docs/direct-channel-setup-guide.md)**
for complete step-by-step instructions including:
- Hosting the receiver HTML over HTTPS
- Registering the app on the Google Cast Developer Console
- Registering Cast devices for development
- Configuring Radio Console
- Verifying the pipeline end-to-end
- Troubleshooting common issues

## SharpCaster Integration Notes

This implementation uses **SharpCaster v3.0.0** for the Cast protocol. Key API details:

### Custom Channel Pattern

```csharp
// Create a custom channel by extending ChromecastChannel
public class DirectCastAudioChannel : ChromecastChannel
{
    public DirectCastAudioChannel(string ns, ILogger logger)
        : base(ns, logger) { }

    public async Task SendMessageAsync(string payload, string destinationId)
    {
        await SendAsync(payload, destinationId);
    }

    public override void OnMessageReceived(string messagePayload, string type)
    {
        // Handle responses from the receiver
    }
}
```

### Wiring the Channel

```csharp
// After LaunchApplicationAsync, get the transport ID
var status = await client.LaunchApplicationAsync(appId);
var transportId = status?.Application?.TransportId;

// Create and wire the channel
var channel = new DirectCastAudioChannel(namespace, logger);
channel.Client = client;  // Public setter on ChromecastChannel

// Send messages
await channel.SendMessageAsync(jsonPayload, transportId);
```

### Important: `RegisterChannel()` is Not Available

SharpCaster v3.0.0's README documents `client.RegisterChannel()` but the method
doesn't exist in the actual NuGet package. Setting `channel.Client = client` directly
enables sending. However, **receiving messages is not supported** without channel
registration — the client doesn't know to route incoming messages on our namespace
to our channel's `OnMessageReceived()`.

For this use case, unidirectional sending (audio chunks from sender to receiver) is
sufficient. Diagnostics are handled via API logs and the Cast device's Chrome DevTools.

## Receiver Design (Web Audio API)

The receiver (`docs/receiver-direct-channel.html`) uses the **Cast Application Framework
(CAF)** with a purely custom message listener — no `<cast-media-player>` element.

### Gapless Playback Scheduling

```
Time →  ──────────────────────────────────────────────
        [Chunk 1: 0.100s][Chunk 2: 0.100s][Chunk 3: 0.100s]...

nextPlayTime:  0.05     0.15     0.25     0.35
               ↑ initial 50ms buffer
```

Each `AudioBufferSourceNode.start(nextPlayTime)` is scheduled at exactly the end
of the previous buffer. The Web Audio API's sample-accurate scheduling ensures
seamless transitions between chunks.

### Buffer Underrun Recovery

If `nextPlayTime < audioCtx.currentTime` (playback caught up to the schedule):
1. Log a warning
2. Reset `nextPlayTime = now + 0.05` (50ms cushion)
3. Continue scheduling from the new position

This causes a brief audio glitch but quickly recovers. Common causes:
- Network congestion delaying message delivery
- Garbage collection pauses on the Cast device
- CPU contention during `decodeAudioData()`

### AudioContext Auto-Resume

Chrome's autoplay policy may start the `AudioContext` in a `suspended` state.
The receiver calls `audioCtx.resume()` on the first received chunk. CAF receivers
typically don't have autoplay restrictions, but the check is defensive.

## Diagnostics

### Sender-Side Logging

The `DirectCastStreamingService` logs:
- **Startup**: chunk size, namespace, target message rate
- **Every 100 chunks**: total sent, total bytes, error count
- **Errors**: first 3 errors logged individually, then every 50th

### API Diagnostics Endpoint

`GET /api/devices/cast/diagnostics` returns:
```json
{
  "cast": {
    "state": "Streaming",
    "connectedDevice": "Living Room Speaker"
  }
}
```

Future enhancement: add DirectChannel-specific stats (chunks/sec, latency, errors).

### Receiver-Side Debugging

Connect to the Cast device's debug port via Chrome:
1. Navigate to `chrome://inspect/#devices`
2. Find the Cast device and click "inspect"
3. Console shows chunk decode times, scheduling info, and underrun warnings

## Risks and Limitations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Cast protocol can't handle 10-20 msgs/sec of ~25KB | High | Configurable chunk size; default HTTP mode as fallback |
| `decodeAudioData()` latency per chunk | Medium | WAV is trivial to decode; monitor via receiver logs |
| Smart speakers: limited Web Audio API | Medium | Test on target devices; HTTP mode as fallback |
| No bidirectional communication | Low | Sufficient for audio; use API logs for diagnostics |
| Wi-Fi congestion at 2 Mbps sustained | Low | Well within typical bandwidth; Cast already uses Wi-Fi |

## Testing

### Unit Tests

Existing tests continue to pass — the DirectChannel code path is only activated when
`StreamingMode` is set to `"DirectChannel"`, and the default is `"HttpMp3"`.

### Manual Testing Procedure

1. Register a custom receiver app at https://cast.google.com/publish/
2. Host `docs/receiver-direct-channel.html` at the registered URL
3. Configure `appsettings.json`:
   ```json
   "StreamingMode": "DirectChannel",
   "ApplicationId": "<your-app-id>"
   ```
4. Deploy to the target machine: `./deploy/Deploy-ToLinux.ps1`
5. Connect to a Cast device via the Web UI or API
6. Verify audio plays through the Cast device
7. Measure latency by playing a distinctive sound and timing the Cast output
8. Monitor logs for chunk send rate and errors
9. Switch back to `"HttpMp3"` and verify existing path still works

### Latency Measurement

Compare end-to-end latency between modes:
1. Play a click/beep sound file
2. Record both local output and Cast output simultaneously
3. Measure the time offset in an audio editor (e.g., Audacity)

Expected results:
- **HTTP MP3**: 4-10 seconds
- **Direct Channel**: 0.2-1.0 seconds (theoretical)

## Future Enhancements

- **Opus encoding**: Replace WAV with Opus for ~10x compression, reducing bandwidth
  from ~256 KB/sec to ~25 KB/sec while maintaining quality
- **Adaptive chunk sizing**: Dynamically adjust chunk size based on observed latency
  and message delivery rate
- **Bidirectional communication**: Use a newer SharpCaster version or implement custom
  message routing to receive status updates from the receiver
- **Buffer management**: Implement a small circular buffer on the receiver to absorb
  jitter without increasing baseline latency
- **Multiple receiver support**: Send audio to multiple Cast devices simultaneously
  via the same channel
