# Cast Latency Comparison — Survey of Low-Latency Audio Casting

**Date:** 2026-05-23
**Author:** Research agent (read-only investigation)
**Scope:** Survey of how seven modern audio-casting implementations achieve low end-to-end latency, with applicability to Radio Console's current Sharpcaster-based Chromecast pipeline.

---

## 1. Executive Summary

Radio Console's current ~2-3 second cast latency is in the same ballpark as Apple AirPlay 2 "Realtime" mode (~2s) and the Default Media Receiver consuming an HTTP MP3 stream, but it is **roughly an order of magnitude higher than what is achievable on a LAN**. The state of the art for music-grade multi-room casting falls into three latency bands:

- **Sub-100ms band:** Native Web Audio scheduling with PCM/Opus over a custom WebSocket or WebRTC channel; Roon RAAT; Sonos SonosNet (~75ms). Requires custom receiver, PTP-class clock sync, or proprietary mesh.
- **100-500ms band:** Snapcast (configurable 500-1000ms default, demonstrated 450ms with FLAC@1ms chunks); AirPlay 2 "Buffered Audio" mode (~500ms); WebRTC over LAN.
- **1-5s band:** AirPlay 2 "Realtime" mode (~2s), LL-HLS (2-5s), default-tuned Snapcast (1s), CMAF (~3s), Radio Console today (~2-3s).

The single biggest finding: **the receiver's `bufferBeforePlay: 3` chunks × 100ms = 300ms startup buffer plus the `lagSeconds: 1.0` reader lag plus `maxBufferAhead: 3s` together account for ~4.3s of the worst-case latency in DirectChannel mode**, and the LAME encoder + MP3 frame buffering plus 5s HTTP reader lag account for the HTTP mode's latency. The most promising avenues, in order of estimated payoff vs. effort, are: (a) tightening DirectChannel buffer parameters (cheap, big win), (b) replacing PCM-over-CastMessage with Opus-over-WebSocket from the receiver page (medium effort, sub-300ms achievable), and (c) reserving WebRTC for a future "low-latency mode" toggle.

---

## 2. Latency Breakdown of Current Implementation

This is a best-effort decomposition of where the observed 2-3s end-to-end latency comes from. Items marked **(measured)** come from code constants; **(estimated)** are extrapolated from public knowledge or rough Web Audio behavior.

### DirectChannel mode (App ID `567E3DBA`, raw PCM over `urn:x-cast:com.radioconsole.audio`)

| Stage | Latency | Source |
|-------|---------|--------|
| SoundFlow MasterMixer + TappedOutputStream commit | ~20-50ms | (estimated; MiniAudio default period ~10-20ms × 2-3 periods) |
| `DirectChannelReaderLagSeconds = 1.0f` | **1000ms** | (measured) `DirectCastStreamingService.cs:254` |
| `DirectChannelChunkSizeMs` accumulation (100ms default) | 100ms | (measured) `DirectCastStreamingService.cs:321` |
| Base64 encode + JSON serialize + CastMessage send | ~5-15ms | (estimated; ~25KB Base64 payload per chunk over WS) |
| WiFi RTT over Cast control channel | 5-30ms | (estimated; typical residential WiFi) |
| Cast receiver `bufferBeforePlay = 3` chunks | **300ms** | (measured) `ApiModels.cs:419` |
| Web Audio `AudioContext.baseLatency` + scheduler look-ahead | 50-100ms | (estimated; Chromecast Web Audio output) |
| `maxBufferAhead = 3.0s` headroom (can be drained at any time) | up to 3000ms | (measured) `ApiModels.cs:418` |
| **Floor (typical observed once stable)** | **~1.5-2.0s** | sum minus the maxBufferAhead drain |
| **Ceiling (worst case, full backpressure)** | **~4.5s** | with maxBufferAhead engaged |

The dominant terms are the reader lag (1s) and the maxBufferAhead (up to 3s). The `bufferBeforePlay` of 3 chunks is a one-time startup penalty.

### HttpMp3 mode (App ID `CC1AD845`, MP3 stream over HTTP)

| Stage | Latency | Source |
|-------|---------|--------|
| `lagSeconds = 5.0` reader lag for MP3 endpoint | **5000ms** | (measured) `HttpStreamOutput.cs` |
| LAME encoder bit-reservoir + frame buffering (no Flush) | ~100-200ms | (estimated; ~26ms MP3 frame × ~5-8 frames) |
| HTTP fetch / TCP buffering on receiver | 100-500ms | (estimated; Default Media Receiver behavior) |
| Default Media Receiver decoder pre-roll | ~500-1000ms | (estimated; Google's receiver buffers conservatively for `StreamType.Live`) |
| **Total typical** | **~6-7s** | dominated by the deliberate 5s lag (designed for jitter robustness) |

The 5s HTTP lag is intentional jitter insurance against client-side stalls; the Default Media Receiver itself is also known to buffer 5-10s for live streams ([pychromecast issue #356](https://github.com/home-assistant-libs/pychromecast/issues/356)). HTTP mode is not the right target for low-latency work.

---

## 3. Per-Implementation Summary

### 3.1 Snapcast

**What it does:** Open-source server reads PCM in fixed-size chunks (default 26ms), encodes (PCM/Opus/FLAC/Ogg Vorbis), sends over TCP to clients. Each client runs continuous time-sync with the server (Time message exchange computes one-way offsets via `(latency_c2s - latency_s2c) / 2`), buffers received chunks, and plays at `server_time + bufferMs`. Drift is corrected by playing fractionally faster/slower (sample removal/duplication, kept under 0.2ms deviation).

**Achieved latency:** Default `buffer = 1000ms` total end-to-end. A reported real-world configuration with FLAC, 11kHz mono, 5ms buffer + 1ms chunks achieved **~450ms** between mic input and remote speaker output ([snapcast/snapcast#663](https://github.com/snapcast/snapcast/issues/663)). FLAC requires ~26ms chunks for stable encoding; PCM and Opus can go lower.

**Why it works:** (1) Chunks are tiny (20-26ms), so quantization is small. (2) The server pushes — no client request RTT in the steady state. (3) Time sync is bidirectional and continuous, so each client knows the exact play-out moment relative to the server clock. (4) Drift compensation is in-band (sample-rate stretch), not via re-buffering.

**Applicability to Radio Console:** Highly relevant as a design template — but Snapcast targets dedicated clients (snapclient binary), not Chromecast receivers. The Snapcast protocol couldn't run unmodified on a Cast receiver (no raw TCP from JS in a sandboxed receiver page), but its **chunk-size + time-sync + drift-compensation pattern** maps directly onto a custom Cast receiver. The 26ms chunk size is ~4x finer than our 100ms; the 1000ms default buffer is conservative and exists because the project assumes WiFi multi-client with weakest-link semantics.

**Sources:** [snapcast/snapcast README](https://github.com/snapcast/snapcast), [binary protocol doc](https://github.com/badaix/snapcast/blob/master/doc/binary_protocol.md), [Latency and Buffers discussion #743](https://github.com/snapcast/snapcast/discussions/743), [latency improvement issue #663](https://github.com/snapcast/snapcast/issues/663), [chunk_ms issue #1197](https://github.com/badaix/snapcast/issues/1197).

### 3.2 Apple AirPlay 2

**What it does:** AirPlay 2 sources negotiate one of two stream modes with the receiver:

- **Realtime stream:** ALAC at CD quality (16/44.1), played to a tight schedule for live-source latency. Latency ~2s.
- **Buffered Audio stream:** AAC at 44.1kHz, the player downloads large parts of the track ahead and the receiver plays them on a synchronized schedule. Latency ~500ms or less.

Sync is performed via IEEE 1588 PTP on ports 319/320. RTP audio packets carry timestamps; periodic "Sync" packets at 1 Hz correlate RTP timestamps to the PTP-disciplined clock. Multi-room sync derives from common PTP wall-clock.

**Achieved latency:** ~500ms (Buffered mode) to ~2s (Realtime mode). The "trick" for low latency is **knowing the full content ahead of time** — Buffered mode is essentially "predictive caching with a synchronized play head," which doesn't apply to live-mixed audio (Radio Console's case).

**Applicability to Radio Console:** Limited at the protocol level (Chromecast doesn't speak AirPlay), but the architectural insight is important: **for live audio you cannot use the "Buffered" trick**, and AirPlay 2's Realtime mode is essentially in the same 2s ballpark we're in. The PTP sync mechanism would only matter if we ever needed cross-device sync (multi-Cast group); for a single Cast endpoint, network RTT alone is the floor.

**Sources:** [shairport-sync discussion #1461](https://github.com/mikebrady/shairport-sync/discussions/1461), [Volumio shairport-sync AIRPLAY2.md](https://github.com/volumio/shairport-sync/blob/master/AIRPLAY2.md), [Unofficial AirPlay 2 spec](https://emanuelecozzi.net/docs/airplay2/rtsp/), [openairplay/airplay2-receiver](https://github.com/openairplay/airplay2-receiver).

### 3.3 Native Chromecast Audio receivers (YouTube Music, Spotify Cast)

**What they do:** These first-party apps use the **Default Media Receiver** (CC1AD845) or their own CAF Web Receiver. They feed a manifest URL (HLS/DASH) and let the receiver pull. The receiver app under the hood is Shaka Player wrapped in CAF, with `autoPauseDuration`, `autoResumeDuration`, `autoResumeNumberOfSegments`, and `segmentRequestRetryLimit` (default 3) governing buffer behavior. Google has not published exact defaults.

**Achieved latency:** Reports vary widely. MP3 from a URL starts in ~1s once buffer fills ([pychromecast #356](https://github.com/home-assistant-libs/pychromecast/issues/356)). Live HLS can stall at 20-60s during BUFFERING. Optimal-network audio can hit <100ms steady-state ([dev forums note](https://developers.google.com/cast/docs/audio)) but this is once playing — startup is much larger.

**Why it (sometimes) works:** First-party apps use **HLS/DASH manifests with short segments and known durations** — the receiver can start playback as soon as it has one segment + look-ahead. Live-streaming-style content (`StreamType.Live`) deliberately buffers more to ride out network hiccups.

**Applicability to Radio Console:** The MSE-based receivers have a **hard constraint**: "The Web Receiver Player does not support segments shorter than 0.1 seconds" ([Cast streaming protocols doc](https://developers.google.com/cast/docs/media/streaming_protocols)). This applies to HLS/DASH manifests. Our DirectChannel approach **already bypasses MSE** by pushing PCM straight to Web Audio — that's actually the right architecture, just over-buffered.

**Sources:** [Cast streaming protocols](https://developers.google.com/cast/docs/media/streaming_protocols), [PlaybackConfig reference](https://developers.google.com/cast/docs/reference/web_receiver/cast.framework.PlaybackConfig), [Audio Devices guide](https://developers.google.com/cast/docs/audio), [SamDel/ChromeCast-Desktop-Audio-Streamer #27](https://github.com/SamDel/ChromeCast-Desktop-Audio-Streamer/issues/27).

### 3.4 WebRTC for audio streaming

**What it does:** Peer-to-peer (or via TURN/SFU) UDP transport carrying Opus at 48kHz stereo. Opus default frame is 20ms (algorithmic delay ~26.5ms total); CELT mode drops to 2.5ms algorithmic. Jitter buffer is adaptive (typically 30-100ms). Built on RTP/RTCP with congestion control (TWCC/GCC). Encryption via DTLS-SRTP is mandatory.

**Achieved latency:** Sub-500ms on LAN; commonly cited "ultra-low latency under 500ms without manual configuration" with Opus. Voice apps (Meet, Zoom) achieve 100-200ms.

**Why it works:** UDP avoids head-of-line blocking, the jitter buffer is small and adaptive, Opus has tiny frames, the codec is integrated end-to-end without separate encode/transport/decode stages.

**Applicability to Radio Console — major caveats:**

- **Cast receiver pages run inside a sandboxed Chromium**. WebRTC `RTCPeerConnection` is available in the receiver browser. There is no public-facing block on opening a PeerConnection from a receiver app.
- **NAT traversal is not an issue** on a LAN (host candidates work).
- **Signaling has to ride the existing Cast custom channel** (the `urn:x-cast:com.radioconsole.audio` namespace) to exchange SDP and ICE candidates. This is doable but adds development cost.
- **.NET SDK support is the real friction point**: there is no first-party Microsoft WebRTC SDK for .NET 10 cross-platform. Options include [Microsoft.MixedReality.WebRTC](https://github.com/microsoft/MixedReality-WebRTC) (archived 2022, last update for .NET 5), [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) (active, supports Opus, runs on Linux ARM64), or wrapping `libwebrtc` via P/Invoke (heavy).

**Sources:** [getstream WebRTC codecs](https://getstream.io/resources/projects/webrtc/advanced/codecs/), [Opus Wikipedia](https://en.wikipedia.org/wiki/Opus_(audio_format)), [WebRTC Opus stereo issue #63](https://github.com/w3c/webrtc-extensions/issues/63), [cloudinary LL-HLS vs WebRTC](https://cloudinary.com/guides/live-streaming-video/low-latency-hls-ll-hls-cmaf-and-webrtc-which-is-best).

### 3.5 DASH / HLS Low-Latency (LL-HLS)

**What it does:** Extends HLS with "partial segments" (parts) addressed by `EXT-X-PART` tags within each segment. Apple recommends 200-400ms parts inside 4-6s segments. `EXT-X-PRELOAD-HINT` lets the player request the next part before it's fully written. CMAF chunked encoding on the encoder side makes parts available with sub-second freshness. Uses HTTP/1.1 or HTTP/2 (HTTP/2 push was originally required, then dropped).

**Achieved latency:** Apple's target is 2-5s glass-to-glass for video. Lower bound is constrained by part duration (must be ≥100ms on Cast Web Receiver) × the small look-ahead buffer the player keeps.

**Why it works:** Tiny parts + preload hints let the player start playback before a full segment is written; manifest delta updates keep the playlist transmission cheap.

**Applicability to Radio Console:** Marginal. LL-HLS is designed for **scalable live broadcast** through CDNs, not for low-latency point-to-point on a LAN. We'd have to (a) write an LL-HLS packager that emits partial M4S segments from the PCM stream, (b) serve it from the existing HTTP endpoint, and (c) target a manifest URL via the Default Media Receiver — and the latency floor would still be 2-5s, which is **no improvement over today's HTTP MP3 path**. Skip.

**Sources:** [Apple LL-HLS doc](https://developer.apple.com/documentation/http-live-streaming/enabling-low-latency-http-live-streaming-hls), [AWS LL-HLS explainer](https://aws.amazon.com/blogs/media/alhls-apple-low-latency-http-live-streaming-explained/), [WWDC19 502](https://developer.apple.com/videos/play/wwdc2019/502/).

### 3.6 Roon (RAAT) / Squeezebox (SlimProto) / Logitech Media Server

**What they do:**

- **Roon RAAT:** Proprietary "push" protocol over TCP. Server pushes audio + control + sync metadata; receiver buffers and clocks the audio (the DAC can be clock master). Supports bit-perfect up to 32/768 PCM and DSD. Multi-room sync via "Group Synchronization."
- **SlimProto (LMS/squeezelite):** Server distributes audio chunks; players buffer until an output threshold then start playing. LMS provides "master" timestamps; players adjust playback to track. Sync adjustment is configurable (e.g., 10-100ms). Ethernet sync is tight; WiFi sync degrades with signal quality.

**Achieved latency:** RAAT advertises "no stupid 2s delays when touching transport controls" — sub-second response is typical. Squeezebox multi-room sync is in the 10-100ms tolerance band; absolute end-to-end latency is buffer-dependent and typically 200-500ms.

**Why it works:** Both protocols are **server-push** (not client-pull like HTTP), both let the **endpoint own the clock**, and both push timing metadata in-band so the receiver knows exactly when to play each sample. Roon additionally allows the DAC to be clock-master, which is critical for bit-perfect audiophile work but irrelevant to our Cast scenario.

**Applicability to Radio Console:** Architectural lessons more than direct reuse. The Cast receiver page **cannot** be the clock master (the host PipeWire sink is). But the Snapcast-style approach already captures the essential ideas (push + time-sync + drift comp). Roon/Squeezebox don't run on Chromecast receivers, so we can't reuse them directly.

**Sources:** [Roon KB on RAAT](https://kb.roonlabs.com/RAAT), [music-assistant RAAT discussion #5133](https://github.com/orgs/music-assistant/discussions/5133), [music-assistant SlimProto discussion #1123](https://github.com/orgs/music-assistant/discussions/1123), [Squeezebox sync wiki](https://wiki.lyrion.org/index.php/Synchronization).

### 3.7 Buffer Strategy Theory — Theoretical Lower Bound

To set realistic targets, here's the lower-bound stack for **any** custom Cast receiver delivering PCM/Opus to Web Audio:

| Component | Minimum | Notes |
|-----------|---------|-------|
| Source-side tap → encode → send | 10-30ms | One audio period in the SoundFlow mixer (MiniAudio default ~10ms). Cannot meaningfully reduce. |
| WiFi RTT (control + data) | 2-10ms | LAN 5GHz WiFi. Wired Cast (Ethernet adapter) gets 1-2ms. |
| WebSocket / CastMessage send + receive | 5-15ms | JSON parse, Base64 decode if PCM. Opus binary frames would be much smaller. |
| Receiver-side jitter buffer (minimum survivable) | 20-50ms | One missed packet of headroom; smaller risks audible dropouts. |
| Web Audio `baseLatency` + AudioWorklet 128-frame buffer | ~3ms | At 48kHz, 128 frames = 2.67ms. Production stable ≥10ms. |
| Web Audio output device buffer (Chromecast platform) | 20-60ms | OS audio HAL on Chromecast hardware; not user-configurable. |
| **Theoretical minimum** | **~60-170ms** | LAN, single device, custom receiver, Opus over WebSocket. |
| **Practical floor (Snapcast-style, FLAC)** | **~250-500ms** | What's been measured in the wild on similar hardware. |

The Web Audio API spec gives `AudioContext.outputLatency` as an introspection point, but it can't be lowered below platform minimums. On Chromecast hardware specifically, the OS audio HAL contribution is opaque and probably 30-60ms.

**Sources:** [W3C Web Audio API 1.1](https://www.w3.org/TR/webaudio-1.1/), [padenot Web Audio perf notes](https://padenot.github.io/web-audio-perf/), [AudioWorklet MDN](https://developer.mozilla.org/en-US/docs/Web/API/AudioWorklet), [Opus Wikipedia](https://en.wikipedia.org/wiki/Opus_(audio_format)).

---

## 4. Comparison Table

```
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Implementation       | Latency  | Protocol   | License     | Codec(s)        | Portable to .NET?    |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Snapcast             | 500-1000 | TCP custom | GPL-3.0     | PCM/Opus/FLAC   | Server in C++; could |
|                      | ms       | binary     |             | /OGG            | re-impl protocol; no |
|                      |          |            |             |                 | Cast receiver client |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| AirPlay 2 Realtime   | ~2000 ms | RTP +      | Apple       | ALAC            | No (Apple-only on    |
|                      |          | PTP        | proprietary |                 | receiver side)       |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| AirPlay 2 Buffered   | ~500 ms  | RTP +      | Apple       | AAC             | No, and only works   |
|                      |          | PTP        | proprietary |                 | for pre-recorded     |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Native Chromecast    | 1-10 s   | HLS/DASH   | Google      | MP3/AAC/Opus    | Already in use; high |
| (Default Receiver)   | (start)  | over HTTPS | proprietary |                 | latency for live     |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| WebRTC (P2P, LAN)    | 100-     | UDP / RTP  | BSD (libs)  | Opus            | Yes via SIPSorcery   |
|                      | 500 ms   |            |             |                 | (.NET, Linux/ARM64)  |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| LL-HLS               | 2-5 s    | HTTPS +    | Apple spec; | AAC/Opus in     | Yes, but no benefit  |
|                      |          | CMAF       | open impls  | CMAF            | over current HTTP    |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Roon RAAT            | <1 s     | TCP custom | Roon Labs   | PCM/DSD         | No (closed)          |
|                      |          |            | proprietary |                 |                      |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| SlimProto (LMS)      | ~200-    | TCP custom | GPL         | FLAC/PCM/MP3    | Server in Perl;      |
|                      | 500 ms   |            |             |                 | no Cast receiver     |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Sonos SonosNet       | ~75 ms   | Mesh WiFi  | Proprietary | proprietary     | No                   |
|                      |          |            |             |                 |                      |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Radio Console today  | 1.5-     | CastMsg WS | MIT (own)   | Raw PCM Int16LE | n/a (current impl)   |
| (DirectChannel)      | 4.5 s    |            |             |                 |                      |
+----------------------+----------+------------+-------------+-----------------+----------------------+
| Radio Console today  | ~6-7 s   | HTTP MP3   | MIT (own)   | MP3 (LAME)      | n/a (current impl)   |
| (HttpMp3)            |          |            |             |                 |                      |
+----------------------+----------+------------+-------------+-----------------+----------------------+
```

---

## 5. Recommendations

Ranked by ratio of latency reduction to implementation effort. **No code changes are proposed here — this is just a research output.**

### Recommendation 1: Tighten DirectChannel buffer parameters (TRY FIRST)

- **Estimated reduction:** From ~1.5-4.5s down to ~400-700ms steady-state.
- **Effort:** ~1 hour of config tuning + receiver-side testing. No new code.
- **Risk:** Low. Risk is audio dropouts on weak WiFi, which is recoverable by re-tuning.
- **Where it lands:**
  - `src/Radio.Web/Models/ApiModels.cs` (and the mirror in `src/Radio.API/Models/ConfigurationModels.cs`): drop `DirectChannelMaxBufferAhead` from 3.0f to ~0.5f, `DirectChannelBufferBeforePlay` from 3 to 1-2, `DirectChannelReaderLagSeconds` from 1.0f to 0.15-0.25f.
  - `src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:254` — the `Math.Clamp` floor is 0.1f, so 0.15f is allowed.
  - Receiver-side: in the custom CAF receiver app (likely in `direct-cast-channel.md`-referenced JS), reduce `bufferBeforePlay` to 1, drop chunk size from 100ms to 50ms (the code allows 50-200ms).
- **Rationale:** The 1s reader lag and 3s maxBufferAhead are conservative defaults. With chunk size 50ms, bufferBeforePlay 1, lag 0.2s, maxBufferAhead 0.5s, the math gives ~50+50+200+500 = 800ms ceiling — and the network is fast enough on a LAN to never engage the maxBufferAhead in steady state.

### Recommendation 2: Switch DirectChannel payload from raw PCM to Opus

- **Estimated reduction:** Modest direct latency win (~20-50ms from smaller payloads), but **enables** Recommendation 1 to be pushed further by reducing Cast control-channel bandwidth pressure (raw PCM is 192KB/s; Opus at 128kbps is 16KB/s, a 12x reduction).
- **Effort:** Medium. Need an Opus encoder in .NET (Concentus is pure-managed C# port; OpusDotNet wraps libopus) and the receiver must decode (libopusjs or native browser MediaSource Opus). Concentus is the safer choice for Linux ARM64 portability.
- **Risk:** Medium. Opus decoder bugs on the Chromecast browser are possible. Test on both 1st-gen and 4th-gen Chromecast Audio + Nest Hub.
- **Where it lands:**
  - `src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:317-502` — replace the `Convert.ToBase64String(pcmBuffer)` path with `OpusEncoder.Encode(pcmBuffer)` + base64.
  - Add Opus encoder dependency to `src/Radio.Infrastructure/Radio.Infrastructure.csproj`.
  - Receiver app: add libopusjs (~200KB asm.js) for decoding, or use a `MediaSource` SourceBuffer for `audio/ogg; codecs=opus` if supported.
- **Rationale:** Smaller payloads mean less WS pressure, less Base64 expansion, less risk of head-of-line blocking on the Cast control channel.

### Recommendation 3: Add a dedicated WebSocket from receiver to host for audio

- **Estimated reduction:** Sub-300ms achievable.
- **Effort:** Medium-high. The Cast custom message channel rides on Cast's own WebSocket and has framing/throughput limits (64KB messages, shared with control). Opening a **second** WebSocket from the receiver page directly to the Radio.API host (e.g., on the existing 5000 port or a dedicated 5003) for audio-only would bypass the Cast channel entirely.
- **Risk:** Medium. The receiver's local IP discovery is tricky — we'd send the host IP+port to the receiver via the existing Cast control channel, then it dials back. mDNS / local network access permissions on the Cast device may bite.
- **Where it lands:**
  - New `src/Radio.API/Hubs/CastAudioHub.cs` (SignalR or raw WebSocket) on the API project, port-exposed in `Program.cs`.
  - `src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs` could be re-purposed or a sibling `DirectCastWebSocketStreamingService.cs` created.
  - Receiver app: opens `new WebSocket('ws://<host>:5003/audio')` on launch.
- **Rationale:** Decouples the audio data plane from the Cast control plane, allowing higher throughput, lower per-message overhead, and use of binary frames (no Base64).

### Recommendation 4: WebRTC mode (experimental "low-latency" toggle)

- **Estimated reduction:** Floor of ~100-200ms on LAN.
- **Effort:** High. SIPSorcery on the .NET side, signaling over the existing Cast channel, careful handling of jitter/drift, and the Cast receiver browser must support WebRTC sender-only flow.
- **Risk:** High. WebRTC is opinionated about congestion control and may compete with the Cast control channel for the same WiFi airtime. The Cast device's WebRTC implementation may have quirks (e.g., no stereo Opus by default — requires SDP munging with `stereo=1`).
- **Where it lands:**
  - New `src/Radio.Infrastructure/Audio/Outputs/WebRtcCastStreamingService.cs`.
  - New SignalR/WS endpoint for signaling: `src/Radio.API/Hubs/CastWebRtcSignalingHub.cs`.
  - SIPSorcery NuGet ref in `src/Radio.Infrastructure/Radio.Infrastructure.csproj`.
- **Rationale:** Best theoretical floor, but high engineering cost. Only worth pursuing if Recommendations 1-3 don't get latency low enough.

### Recommendation 5: Continuous time-sync + drift compensation (long-term)

- **Estimated reduction:** Indirect — doesn't lower latency by itself, but lets us **safely lower** the buffer parameters in Rec. 1 without risking dropouts during long-running streams.
- **Effort:** Medium-high.
- **Risk:** Medium.
- **Where it lands:**
  - Receiver-side: pong messages already exist (`DirectCastStreamingService.cs:195`). Extend to bidirectional time-sync (server sends timestamp T_s, receiver echoes T_s + receive_local_time, server computes one-way offset). Drift compensation via fractional resampling in the receiver (Web Audio `playbackRate` adjustment on AudioBufferSourceNode at 0.999-1.001 range).
- **Rationale:** Snapcast's secret sauce. Without it, any low-latency mode is one bad WiFi packet away from a buffer underrun.

---

## 6. Out of Scope / Further Research

Items I could not pin down without source-code access or vendor contact:

1. **Exact Chromecast OS audio HAL latency.** Google does not publish this. The 20-60ms estimate above is from comparable Linux ALSA setups. A real measurement would require a loopback test (cast a known audio impulse, mic-record at the speaker, measure delay) on the specific Chromecast hardware the user owns.
2. **CAF receiver internal jitter buffer when MSE is bypassed.** The MSE path is documented (2MB buffer max, 100ms segment min). Our DirectChannel path bypasses MSE and uses Web Audio directly, so MSE limits don't apply — but the Web Audio scheduler's own jitter tolerance on Cast hardware is not documented.
3. **Whether SIPSorcery can actually open a WebRTC PeerConnection to a Cast receiver browser** without an intermediate STUN/TURN server when both are on the same LAN. This needs a prototype.
4. **AirPlay 2 PTP behavior.** Public docs are partial; full PTP integration in shairport-sync is reportedly incomplete. Not blocking for Radio Console.
5. **Whether the Cast receiver's WebSocket-from-CastMessage really has a 64KB hard limit or just a soft recommendation.** Different sources say different things; in practice 25KB chunks have worked fine for Radio Console.
6. **How `StreamType.Live` actually affects the Default Media Receiver's internal buffer sizing** — Google doesn't expose tunables for this.
7. **Whether using `audio/aac` or `audio/opus` in a MediaSource SourceBuffer on the Cast receiver would meaningfully outperform our current Web Audio Int16LE path.** Worth a quick experiment but unlikely to win — Web Audio scheduling is already as direct as it gets.

---

## Sources

- [snapcast/snapcast README and project](https://github.com/snapcast/snapcast)
- [Snapcast binary protocol doc](https://github.com/badaix/snapcast/blob/master/doc/binary_protocol.md)
- [Snapcast Latency and Buffers discussion #743](https://github.com/snapcast/snapcast/discussions/743)
- [Snapcast latency improvement issue #663](https://github.com/snapcast/snapcast/issues/663)
- [Snapcast chunk_ms issue #1197](https://github.com/badaix/snapcast/issues/1197)
- [Music Assistant Snapcast provider docs](https://www.music-assistant.io/player-support/snapcast/)
- [shairport-sync AIRPLAY2.md (Volumio fork)](https://github.com/volumio/shairport-sync/blob/master/AIRPLAY2.md)
- [shairport-sync discussion #1461 on stereo latency](https://github.com/mikebrady/shairport-sync/discussions/1461)
- [Unofficial AirPlay 2 RTSP spec](https://emanuelecozzi.net/docs/airplay2/rtsp/)
- [openairplay/airplay2-receiver](https://github.com/openairplay/airplay2-receiver)
- [Time Synchronization - Unofficial AirPlay spec](https://openairplay.github.io/airplay-spec/screen_mirroring/time_synchronization.html)
- [Apple LL-HLS documentation](https://developer.apple.com/documentation/http-live-streaming/enabling-low-latency-http-live-streaming-hls)
- [AWS LL-HLS explainer](https://aws.amazon.com/blogs/media/alhls-apple-low-latency-http-live-streaming-explained/)
- [WWDC19 Session 502 - Introducing Low-Latency HLS](https://developer.apple.com/videos/play/wwdc2019/502/)
- [WWDC20 Session 10228 - What's new in Low-Latency HLS](https://developer.apple.com/videos/play/wwdc2020/10228/)
- [Cloudinary LL-HLS vs CMAF vs WebRTC comparison](https://cloudinary.com/guides/live-streaming-video/low-latency-hls-ll-hls-cmaf-and-webrtc-which-is-best)
- [Google Cast streaming protocols doc](https://developers.google.com/cast/docs/media/streaming_protocols)
- [Google Cast Web Receiver PlaybackConfig reference](https://developers.google.com/cast/docs/reference/web_receiver/cast.framework.PlaybackConfig)
- [Google Cast Audio Devices guide](https://developers.google.com/cast/docs/audio)
- [pychromecast live streaming buffer issue #356](https://github.com/home-assistant-libs/pychromecast/issues/356)
- [SamDel/ChromeCast-Desktop-Audio-Streamer latency issue #27](https://github.com/SamDel/ChromeCast-Desktop-Audio-Streamer/issues/27)
- [getstream WebRTC codecs guide](https://getstream.io/resources/projects/webrtc/advanced/codecs/)
- [Opus codec Wikipedia](https://en.wikipedia.org/wiki/Opus_(audio_format))
- [WebRTC stereo Opus issue #63](https://github.com/w3c/webrtc-extensions/issues/63)
- [MDN WebRTC codecs](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/WebRTC_codecs)
- [Roon RAAT KB](https://kb.roonlabs.com/RAAT)
- [Music Assistant RAAT discussion #5133](https://github.com/orgs/music-assistant/discussions/5133)
- [Music Assistant SlimProto discussion #1123](https://github.com/orgs/music-assistant/discussions/1123)
- [Squeezebox synchronization wiki](https://wiki.lyrion.org/index.php/Synchronization)
- [W3C Web Audio API 1.1](https://www.w3.org/TR/webaudio-1.1/)
- [Padenot Web Audio performance notes](https://padenot.github.io/web-audio-perf/)
- [AudioWorklet MDN](https://developer.mozilla.org/en-US/docs/Web/API/AudioWorklet)
- [SonosNet architecture overview](https://proprietarywireless.com/media-connectivity/sonosnet/)
- [SIPSorcery (.NET WebRTC library)](https://github.com/sipsorcery-org/sipsorcery)
