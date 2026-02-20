# Findings & Decisions

## Session: 2026-02-19 — Cast Polish, Log Hygiene, Ubuntu Setup

### 1. Google Cast Latency Discussion Analysis

**Document reviewed:** `Google-Cast-Latency-Discussion.md`

Two approaches described:
1. **WebSocket + Web Audio API** — Custom receiver connects back to C# app via WebSocket, receives PCM chunks wrapped in WAV headers (44 bytes + PCM data), decodes via `AudioContext.decodeAudioData()`, and schedules playback via `BufferSource.start()`. Claims sub-500ms latency.

2. **Pre-loaded Event Sounds** — Receiver pre-loads static audio files into memory, C# app sends custom namespace messages to trigger instant playback.

**Our current approach vs WebSocket:**

| Aspect | Current (HTTP MP3) | WebSocket + Web Audio |
|--------|-------------------|----------------------|
| Latency | ~3-10s | Sub-500ms (claimed) |
| Complexity | Moderate (LAME encoding, throttle) | High (WebSocket server, AudioContext scheduling, chunk management) |
| Receiver | CAF standard receiver (minor customization) | Fully custom receiver (no CAF media player) |
| Protocol | HTTP progressive download | WebSocket binary frames |
| Audio format | MP3 (frame-based, streaming-friendly) | PCM wrapped in WAV headers (per-chunk) |
| Buffering | Chrome controls buffering | App controls buffering (jitter buffer) |
| Error recovery | Chrome handles reconnection | Must implement reconnection logic |

**Key clarification on WAV:**
- The discussion mentions WAV works, but specifically via `decodeAudioData()` in JavaScript, NOT via Chrome's `<audio>` element
- `decodeAudioData()` accepts any decodable format (WAV, MP3, OGG, etc.) and produces raw PCM
- This is a fundamentally different path than loading media into `<audio>` tag
- Our existing `/stream/audio` WAV endpoint uses chunked transfer encoding with `int.MaxValue` in the WAV header — Chrome's `<audio>` element rejects this

**Verdict:** Current MP3 approach is correct for standard Cast. WebSocket approach is valid but high-effort for marginal benefit in our use case (music playback, not intercom). Document in FUTURE-WORK.md.

### 1b. WebSocket Mixed Content Testing (2026-02-19)

Tested whether the HTTPS-served Cast receiver can connect back to the C# app via WebSocket:

| Test | Protocol | Target | Result |
|------|----------|--------|--------|
| 1 | `ws://` (plain) | `ws://192.168.86.44:9999/ws-mixed-content-test` | **BLOCKED** — No TCP connection in 120s |
| 2 | `wss://` (self-signed cert) | `wss://192.168.86.44:9999/wss-self-signed-test` | **BLOCKED** — No TLS handshake in 120s |

**Method:** TCP/TLS listener on Pi port 9999, WebSocket probe in `docs/receiver.html` triggered on Cast device load. Both tests showed zero incoming connections.

**Conclusion:** The WebSocket approach from `Google-Cast-Latency-Discussion.md` is **not viable** with a standard HTTPS-served receiver. Chrome on Cast devices (Chrome 92+) enforces strict mixed content policy — even `wss://` with self-signed certs is rejected without attempting connection. Documented in `design/FUTURE-WORK.md` section 7 with workaround options.

### 2. ALSA Log Noise Analysis

**Volume:** Every 5 seconds, MiniAudio probes unused audio backends. Each probe produces ~16 lines of stderr:
```
Cannot connect to server socket err = No such file or directory  (JACK)
Cannot connect to server request channel                        (JACK)
jack server is not running or cannot be started                 (JACK)
JackShmReadWritePtr::~JackShmReadWritePtr - Init not done...   (JACK x2)
ALSA lib pcm_oss.c:404: Cannot open device /dev/dsp            (OSS)
ALSA lib pulse.c:242: PulseAudio: Unable to connect             (PulseAudio)
ALSA lib pcm_dmix.c:1000: unable to open slave                 (dmix x2)
```
That's **~192 lines per minute** of pure noise.

**Source:** These come from C libraries (libasound2, libjack) writing to stderr/fd2. Not from .NET/Serilog. Serilog writes structured logs to stdout which journald captures at info priority.

**Solution options ranked:**
1. **`StandardErrorPriority=debug`** in systemd service (RECOMMENDED) — demotes stderr to debug level, `journalctl -p info` filters it out. Zero code changes.
2. **ALSA config** (`defaults.namehint.!pulse = off`) — partial, only suppresses some probing
3. **MiniAudio backend filtering** — SoundFlow may not expose this API
4. **`StandardError=null`** — aggressive, loses ALL stderr (not recommended)

### 3. Ubuntu x64 Machine Profile

**Host:** `mmack@radio`
- **CPU:** Intel N100 (4 cores, x86_64)
- **RAM:** 3.6 GB
- **Storage:** 116GB NVMe (98GB free)
- **OS:** Ubuntu 24.04.4 LTS (kernel 6.17.0-14)
- **.NET:** SDK 8.0.124 + 10.0.103 already installed
- **Audio:** PulseAudio (standard Ubuntu desktop setup)

**Deployment differences from Pi:**

| Aspect | Raspberry Pi | Ubuntu x64 |
|--------|-------------|------------|
| Architecture | ARM64 | x86_64 |
| .NET RID | linux-arm64 | linux-x64 |
| Audio stack | PipeWire + WirePlumber | PulseAudio |
| BT routing | Complex (null sink, TCP, WirePlumber rules) | Standard (pulseaudio-module-bluetooth) |
| SSH target | `mmack@piradio` | `mmack@radio` |
| Setup script | `deploy/raspberry-pi/setup.sh` | `deploy/debian-x64/setup.sh` |

**Deploy script changes needed:**
- `Deploy-ToPi.ps1` hardcodes `linux-arm64` — needs `-Runtime` parameter
- Production config will differ (different audio device card numbers, etc.)

---

## Previous Sessions (preserved)

### Session: 2026-02-16 — Architecture Review & Integration Prep
(see git history for full content)

### Session: 2026-02-15 — Project Reconciliation
(see git history for full content)

### Session: 2026-02-15 — Phase 9 Pi Verification
(see git history for full content)

### Session: 2026-02-14 — Pi Hardware Testing
(see git history for full content)

### Session: 2026-02-13 — Cast Audio & BT Fixes
(see git history for full content)
