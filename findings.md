# Findings & Decisions

## Session: 2026-02-24 — Phase F Planning & Research

### 1. API SEGV Crash Analysis (F.1)
- API service (radio-api) crashes with SIGSEGV (signal 11) every ~28 minutes
- 28 crashes recorded in one day — systemd auto-restarts both services
- Correlates with fingerprint capture cycles (~56 captures = ~28min)
- Likely in native code: MiniAudio/SoundFlow or fpcalc subprocess
- Investigation needed: core dumps, memory leak analysis in TappedOutputStream/FingerprintTapModifier

### 2. Log Noise Sources (F.2)
Three major noise sources identified:

| Source | Rate | Fix |
|--------|------|-----|
| ALSA/JACK/PulseAudio stderr spam | ~9,360 lines/hour | ~/.asoundrc to disable unused plugins |
| Fingerprint "no match" cycle | ~480 lines/hour | Reduce to DBG for radio/BT (live radio rarely matches AcoustID) |
| DirectCast chunk logging | ~36,000 lines/hour when casting | Reduce per-chunk logging to DBG |

Additional noise: TaskCanceledException in VisualizerPanel (expected on navigation), periodic "Broadcast NowPlayingChanged", album art URL null→... on page init.

### 3. FM Stereo Feasibility (F.6)
- Current WFM demod outputs mono (duplicated to L+R)
- 240 kHz demod rate already preserves the stereo subcarrier — info is there but discarded
- Implementation: ~200-300 lines new DSP code in `RTLSDRCore/DSP/StereoFmDecoder`
- Reuses existing DSP primitives: LowPassFilter, DeEmphasisFilter, AudioDecimator
- Steps: 19kHz pilot detection → 38kHz carrier recovery → L+R/L-R extraction → matrix decode

### 4. Codebase Locations for Phase F Work
- Visualizer: `src/Radio.Web/wwwroot/js/visualizer.js` lines 290-390 (waveform drawing)
- Fingerprinting: `src/Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs`
- NowPlaying: `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` lines 10-46
- RadioControl: `src/Radio.Web/Components/Shared/RadioControlPanel.razor`
- Volume pipeline: `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs`

---

## Session: 2026-02-22 — Queue Add-to-Queue Fix (PR #234)

### Queue/Playlist Button Fix
- Buttons animated (CSS :active) but handlers didn't fire on user's browsers
- Root cause: `Blazor.start()` called before JS dependencies loaded (autostart=false mode)
- Fixed: Moved Blazor.start() after all script tags, fixed reconnect dialog ID
- Added diagnostic logging and 10s timeouts to queue/playlist handlers
- Also fixed false-positive queue success (API returned addedCount:0 with HTTP 200)

---

## Session: Next — Media Setup, Dual Output Bug, Architecture Cleanup

### 1. Dual Audio Output Bug — Preliminary Analysis

**Problem:** When Cast connects, audio plays on BOTH local speakers AND Cast device.

**Root cause (from code review):**
In `DevicesController.cs`, the Cast connect handler:
1. Calls `ActivateOutputAsync(_castOutput)` — starts Cast
2. Calls `ActivateOutputAsync(_httpOutput)` — starts HTTP stream
3. Calls `_audioEngine.SetLocalOutputMuted(true)` — mutes local

The mute call may only set a flag without actually stopping the audio pipeline from feeding
the local playback device. The audio mixer continues sending samples to all attached outputs.

**Key question:** Does `SetLocalOutputMuted(true)` actually silence the SoundFlow playback device,
or does the audio pipeline continue rendering to the local device regardless?

**DirectChannel mode doesn't need HTTP stream** — it sends raw PCM directly via Cast namespace.
The HTTP stream output is only needed for HttpMp3 mode.

---

## Session: 2026-02-20 — Cast Drift Testing & Ping Endpoint

### Cast Latency Results (Ubuntu x64 → Office speaker)
- Transit delay: avg 87ms, min 53ms, max 1676ms
- Buffer-ahead: steady at 3.0s cap (drift protection working)
- Audio quality: clean, constant ~3s delay, no drift, no stutter
- Receiver v10 drops ~1 chunk/43s to maintain cap

### SharpCaster Channel Registration
- No public `RegisterChannel()` in SharpCaster v3.0.0
- Channels property is private `IEnumerable<IChromecastChannel>` backed by array
- Reflection-based injection works (replace array with new one including custom channel)
- Channel registered (7 total), pings sent, but **pong still not received**
- Root cause unknown — may be deeper Cast SDK routing issue

### CDP Workaround
Chrome DevTools Protocol at `http://<cast-ip>:9222/json` provides reliable metrics access.
`Runtime.evaluate` reads receiver JavaScript globals directly. More reliable than SharpCaster messaging.

---

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
1. ~~`StandardErrorPriority=debug`~~ — FAILED: noise comes through stdout, not stderr
2. **Syslog level prefix (IMPLEMENTED)** — `SystemdConsoleFormatter` prefixes Serilog lines with `<N>`, combined with `SyslogLevelPrefix=true` + `SyslogLevel=debug` in service file. Unprefixed C library noise defaults to debug. Verified working.
3. **ALSA config** (`defaults.namehint.!pulse = off`) — partial, only suppresses some probing
4. **MiniAudio backend filtering** — SoundFlow may not expose this API
5. **`StandardError=null`** — aggressive, loses ALL stderr (not recommended)

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
