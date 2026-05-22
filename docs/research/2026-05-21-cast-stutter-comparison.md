# Cast stutter comparison — RTest vs custom-CAF-receiver reference apps

**Status**: research framework — *empty cells, structure only*. Each cell is filled in during the research execution pass.

**Author**: Mark + Claude (brainstorming pass 2026-05-21)

**Motivation**: RTest's Google Cast output produces periodic audible stutters on the kiosk. The user reports never having heard the same behavior from reference apps (TuneIn, Plex) using *custom CAF receiver pages* for continuous-audio cast. Anecdotal — but a strong-enough signal to investigate the architectural differences. Goal: understand what those reference apps do differently, so the cost of any future RTest change is informed rather than guessed.

**Explicit non-goal**: no RTest implementation work falls out of this document. The "things RTest could try" section is *research output*, not a plan or commitment. A separate plan would consume that list later if and when the team chose to act on any item.

---

## 1. Scope

### In scope
- Custom-CAF-receiver casting of *continuous* audio (long-form playback — internet radio, full-album streams, queued playlists).
- Stutter-avoidance strategies — what each system does so the receiver's playback buffer never runs empty.
- Both RTest cast modes:
  - **HttpMp3** — `ApplicationId = "CC1AD845"` (Google's Default Media Receiver). Radio.API serves `/stream/audio/mp3` (LAME-encoded, `StreamType.Live`). Receiver pulls.
  - **DirectChannel** — `ApplicationId = "567E3DBA"` (custom). Base64-encoded WAV chunks (~100 ms each) pushed over the Cast custom message bus to `receiver.html`. Receiver plays via Web Audio API.

### Out of scope
- Spotify Connect (different architecture — Chromecast runs a native Spotify app and pulls from Spotify's CDN; no sender-side audio pipeline exists, so the comparison axis isn't meaningful).
- Video casting (different decoder paths, different framing constraints).
- Cast SDK *API surface* review (we care about what gets transmitted and how it's consumed, not "which SDK method to call").
- Sender-side audio production upstream of `IAudioOutput` (SoundFlow, PipeWire, sources) — already studied in prior debug sessions, not relitigated here unless evidence implicates it.

---

## 2. Reference systems

Four columns in every matrix in this document:

| Column key | System | Why included |
|---|---|---|
| **RTest-HM** | RTest HttpMp3 mode | RTest's default — closer to "how most Cast audio works" |
| **RTest-DC** | RTest DirectChannel mode | RTest's experimental mode — pure custom-receiver, push protocol |
| **TuneIn** | TuneIn Radio Cast app | Closest real-world analog: continuous internet radio with custom CAF receiver |
| **Plex** | Plex (audio cast via Plex receiver) | Public engineering blog detail, custom CAF receiver, mixed continuous + queued |

---

## 3. Data collection methodology

Each filled cell carries an evidence tag so the reader knows what they're looking at:

| Tag | Means | How obtained |
|---|---|---|
| `[source-walked]` | We read the code | RTest: open in tree. TuneIn / Plex: `chrome://inspect` while cast is active → attach DevTools to the live receiver page → inspect the minified-but-readable receiver JS, watch `MediaSource` / `AudioContext` state, breakpoint on chunk handlers. |
| `[doc-cited]` | We have a public reference | Google CAF receiver docs (StreamManager, MediaInformation, segment loaders), Plex engineering blog posts, public Cast SDK reference, Web Audio API + MSE specs. Cite the URL in the cell. |
| `[inferred-from-behavior]` | We're reasoning from observable signals | Wall-clock chunk-arrival cadence, MSE buffered-range readout via DevTools, audio output spectral analysis under known-good vs stutter conditions. Lower confidence; explicit. |

Findings without an evidence tag should not appear in the filled doc.

### Tools needed for the research execution pass
- A Chromecast on the LAN (we have at least one — `_defaultCastDevice` in RTest's config).
- TuneIn + Plex installed on a controller device (phone or tablet) able to initiate a cast session.
- Chrome with developer tools on a workstation that can reach the Chromecast's debug port (Chromecast exposes a port on the local network when "Send a Bug Report" diagnostic mode is enabled, or — easier — use the Chrome `chrome://inspect` Devices tab with the Chromecast's IP).
- Optional: Wireshark with mDNS + Cast protocol decoders, for the inferred-from-behavior tier.

### Counter-discipline
Receivers can change between releases. Date-stamp every receiver-walked finding (`[source-walked, TuneIn receiver build 2026-05-21]`) so a future reader knows whether the evidence is still current.

---

## 4. Failure-mode catalog (the diagnostic spine)

Seven modes, each independently capable of producing the *click-pause-resume* the user hears. The matrix below is filled per system × mode during the research pass.

### Modes

| # | Mode | Mechanism | Audible signature |
|---|------|-----------|---|
| **FM1** | Receiver jitter-buffer underrun | Audio leaves the receiver's playback queue faster than new chunks arrive; buffer hits zero | Brief silences (50–300 ms). Cadenced. Most common Cast stutter. |
| **FM2** | Sender pipeline jitter | Sender's encode → transmit pipeline blocks momentarily (GC, lock contention, PipeWire quantum stall, slow `HttpResponse.WriteAsync`) | Same audible result as FM1 but root cause is upstream; correlates with sender-side load |
| **FM3** | Clock drift / resampling | Sender's nominal sample rate doesn't match receiver's; buffer slowly drains or overflows over minutes/hours | Drift — stutter rate increases with session length. Sometimes micro pitch artifacts |
| **FM4** | Receiver scheduling slips | Receiver JS schedules audio via `AudioBufferSourceNode.start(when)` or MSE append; main thread is busy at that moment (GC, large message decode, DOM, image load) | Sharp clicks at scheduled-buffer boundaries; often correlates with track-change / metadata events |
| **FM5** | Network / transport jitter | TCP head-of-line on Cast bus, WiFi retransmits, mDNS contention | Bursty stutters, sometimes 1+ second; correlates with other LAN activity |
| **FM6** | Codec / format boundary glitches | A chunk straddles an MP3 frame, AAC ADTS sync, or PCM block boundary in a way the decoder can't seamlessly join | Periodic pops at the chunk interval (every 100 ms in DC mode; every ~46 ms per MP3 frame in HM mode) |
| **FM7** | Receiver app lifecycle event | Chromecast OS evicts/restarts the receiver under memory pressure or backgrounding; custom-HTML receivers especially vulnerable | Long gap (1+ s) followed by playback resuming. Rare but real. |

### Failure-mode matrix (to be filled)

For each cell:

- **Exposure** — Y/N + one-line "this system is vulnerable because…"
- **Mitigation** — concrete: chunk size, buffer depth, codec, scheduling approach
- **Evidence** — `[source-walked]`, `[doc-cited]`, or `[inferred-from-behavior]`

| Mode | RTest-HM | RTest-DC | TuneIn | Plex |
|---|---|---|---|---|
| FM1 — Receiver underrun | **Exposure:** Y — `StreamType.Live` means receiver cannot seek/refill; depends entirely on sender pacing. **Mitigation:** Receiver forces play() at 3.0 s of buffered audio (server pre-burst, then real-time +10 s cap). Server CBR 320 kbps MP3 supplies a steady byte rate; ring-buffer reader emits 4 KB silence (~21 ms) when no real audio is available so the HTTP stream never goes dry. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L404-L422,L429-L443; docs/receiver.html:L50-L84; src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs:L174-L184] | **Exposure:** Y — receiver pre-buffers only `BUFFER_BEFORE_PLAY = 3` chunks (~300 ms at 100 ms chunks) before starting Web Audio scheduling; once playing, any sender stall drains the AudioBuffer queue silently because there is no continuation buffer in the receiver beyond what has been scheduled. **Mitigation:** Sender reader lag default 1.0 s of ring-buffer history; receiver `MAX_BUFFER_AHEAD = 3.0 s` cap (drops chunks above that). Empty audio engine returns 4 KB silence per `ReadForReader` call so the streaming loop never blocks. [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L41-L42,L253-L260; src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L253-L272; src/Radio.Core/Configuration/AudioOutputOptions.cs:L180-L194] | _to fill_ | _to fill_ |
| FM2 — Sender pipeline jitter | **Exposure:** Y — single-threaded per-client loop in `HandleClientAsync`: blocking PCM read, then synchronous LAME `mp3Writer.Write` (encode), then `OutputStream.Flush()`, then `Task.Delay` for pacing. Any LAME GC pause or socket Flush stall blocks the next read. No producer/consumer split. **Mitigation:** Real-time pacing only delays after `aheadSec > 10.0 s`, so up to 10 s of slack absorbs jitter; ring buffer holds 5 s of audio at the engine side. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L396-L491; src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs:L46-L56] | **Exposure:** Y (high) — `StreamingLoopAsync` awaits `_channel.SendMessageAsync` for every chunk before reading/encoding the next one. SharpCaster's `SendAsync` is a TCP write on the Cast control socket; if that socket blocks (LAN congestion, Cast device GC), encoding stops and the receiver's 3 s buffer-ahead cap drains. JSON serialization + Base64 (~25.7 KB per 100 ms chunk) happens on the same task. **Mitigation:** Per-chunk pacing via `Stopwatch` enforces "no faster than chunkMs"; send errors logged but loop just continues with a 100 ms delay. No queue, no parallelism. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L317-L490,L439-L457; src/Radio.Infrastructure/Audio/Outputs/DirectCastAudioChannel.cs:L48-L51] | _to fill_ | _to fill_ |
| FM3 — Clock drift / resampling | **Exposure:** Y — sender's PCM source rate is 48 000 Hz (audio engine); MP3 encoded at the same rate. Pacing uses wall-clock `DateTime.UtcNow` vs accumulated PCM bytes, no PTS in the stream. Default Media Receiver runs its own audio clock and resamples on output. Drift across hours possible; no PTS-aware sync. **Mitigation:** None in code beyond the 10 s server-side cushion and receiver's natural elastic buffering (~3 s); receiver does not signal back drift. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L415-L481; src/Radio.Core/Configuration/AudioOutputOptions.cs:L222-L235] | **Exposure:** Y (explicit and acknowledged) — receiver comment "sender's wall clock and the AudioContext hardware clock drift at ~2.3 ms/sec" without correction. Sender's `Stopwatch` pacing vs receiver's `audioCtx.currentTime` are independent oscillators. **Mitigation:** Receiver drops whole chunks when `bufferAhead > MAX_BUFFER_AHEAD = 3.0 s` (no resampling, just discard) — produces an audible jump every ~22 minutes at 2.3 ms/sec drift. Sender does not resample either. `transitDelay` and `bufferAhead` are reported back via pong but not used to feedback-correct rate. [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L146-L159,L65-L77,L226-L233; src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L357-L410] | _to fill_ | _to fill_ |
| FM4 — Receiver scheduling slips | **Exposure:** N for this mode — the HM receiver is the Default Media Receiver / `cast-media-player` element; scheduling is handled by Chrome's `<video>`/`<audio>` element pipeline, which is C++ and decoupled from JS main thread. Our custom `receiver.html` only adds a `setInterval(500 ms)` `media.play()` force-loop until buffered ≥ 3.0 s. **Mitigation:** Once `play()` succeeds the JS loop exits; no per-chunk JS scheduling. [source-walked, sha=3b06f79, docs/receiver.html:L50-L88] | **Exposure:** Y (high) — every chunk creates a new `AudioBufferSourceNode` and calls `source.start(nextPlayTime)` with absolute scheduling against `audioCtx.currentTime`. Decode (Int16 → Float32 conversion via for-loop over `view.getInt16` for every sample) runs on the JS main thread. If the main thread is busy when a message arrives (e.g., another message decode, status update DOM write at L347), `nextPlayTime` may already be in the past → branch at L141 resets to `now + 0.02` and the audio queue has a gap. **Mitigation:** Buffer-ahead of 3 s gives slack; status DOM updates only every 100 received chunks (L266-L278); no other heavy work. [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L107-L195,L141-L144,L162-L170] | _to fill_ | _to fill_ |
| FM5 — Network / transport jitter | **Exposure:** Medium — Cast device pulls MP3 over a dedicated TCP `GET /stream/audio/mp3` connection on port 8080, separate from the Cast control channel. WiFi retransmits or LAN HoL block only the audio fetch. CORS preflight (OPTIONS) handled before stream open. **Mitigation:** Chunked transfer with `KeepAlive=true`, `Accept-Ranges: none`, `Cache-Control: no-cache`; CBR 320 kbps keeps bandwidth predictable (~40 KB/s) so jitter is small relative to LAN capacity. Receiver's 3 s buffer absorbs typical WiFi reorder. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L312-L361,L378-L394] | **Exposure:** Y (high) — all DirectChannel traffic (audio, config, ping/pong) plus all other SharpCaster control traffic (volume, media status, receiver status) share the **same TLS Cast control TCP socket**. A 200 KB metadata blob (album-art URL update via `LoadMediaWithRecoveryAsync`) head-of-line-blocks audio chunks behind it; AVRCP volume sync events arrive on the same socket. **Mitigation:** None. There is no second transport. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastAudioChannel.cs:L23-L51; src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:L646-L656,L774-L807,L1382-L1430] | _to fill_ | _to fill_ |
| FM6 — Codec / boundary glitches | **Exposure:** Y — LAME runs CBR (`Math.Clamp(_options.Mp3Bitrate, 128, 320)`, default 320 kbps) with no `mp3Writer.Flush()` ever called (preserves bit reservoir across chunks). MP3 frames at 48 kHz are 1152 samples = 24 ms. Underlying `OutputStream.Flush()` flushes TCP only, not the encoder. PCM read sizes are `ClientBufferSize = 65536` bytes (~170 ms) which does not align to MP3 frame boundaries, but the LAME stream is continuous so frames straddle write boundaries cleanly. **Mitigation:** Persistent LAME instance across the client lifetime; reader lag 5 s aligned to `frameSize = channels * bytesPerSample = 4` bytes to avoid byte-shift. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L374-L394,L458-L465; src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs:L116-L142] | **Exposure:** Low — sender ships raw Int16LE PCM (no codec); each 100 ms chunk is 19 200 bytes (4800 stereo samples) exactly divisible by `frameSize = 4`, so no straddling. Receiver concatenates by `nextPlayTime += audioBuffer.duration` — sample-accurate as long as chunks are contiguous in sequence. **Mitigation:** Sequence-gap warnings (L240-L242) but no insertion/skip on gap; dropped chunks cause an explicit gap of `chunkMs` in the audio queue (no PLC). [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L317-L335,L437-L450; docs/receiver-direct-channel.html:L107-L170,L240-L243] | _to fill_ | _to fill_ |
| FM7 — Receiver lifecycle | **Exposure:** Y — uses Google's Default Media Receiver `CC1AD845` by default (but `appsettings.json` actually sets `567E3DBA` — custom receiver). Custom `receiver.html` calls `context.start({ disableIdleTimeout: true })`. Sender has `IsCastSessionExpired` recovery: detects `INVALID_MEDIA_SESSION_ID` / `No running applications` / `session not found` and calls `LaunchApplicationAsync` again then re-loads media (one retry). **Mitigation:** Auto-recovery is async via `LoadMediaWithRecoveryAsync`; `disableIdleTimeout: true` prevents Cast OS from evicting on idle. Memory pressure eviction not handled — would surface as connection loss. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:L994-L1062; docs/receiver.html:L109-L112; src/Radio.API/appsettings.json:L125] | **Exposure:** Y — custom receiver `receiver-direct-channel.html` sets `options.disableIdleTimeout = true`. On receiver crash, sender keeps sending and Cast SDK errors get logged at L478-L486; no automatic relaunch path for DirectChannel mode in `StartAsync` (only `StartDirectChannelAsync` fall-through to HttpMp3 on missing transport ID, no mid-session recovery). **Mitigation:** `disableIdleTimeout: true`; AudioContext recreated on first chunk via `ensureAudioContext()`. No memory-pressure handling; receiver accumulates `chunksScheduled` counter forever (no leak in audio nodes because they GC after `start()` returns and source plays). [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L352-L358; src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L473-L489; src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:L596-L638] | _to fill_ | _to fill_ |

---

## 5. Pipeline table (the apples-to-apples reference)

Ten rows × the same four columns. Each cell with an evidence tag.

| Row | What it captures | RTest-HM | RTest-DC | TuneIn | Plex |
|---|---|---|---|---|---|
| Source format | Samples the sender starts with (PCM s16le 48 kHz stereo? 24-bit float?) | PCM s16le, 48 000 Hz, 2 ch (192 000 B/s) — engine writes float32 to ring buffer, ring buffer converts to s16le on read. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs:L258-L290; src/Radio.API/appsettings.json:L136-L140] | PCM s16le, 48 000 Hz, 2 ch — identical source path; ring buffer reader returns the same bytes. Receiver overrides its `AudioContext` sample rate from `msg.sr` per chunk. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L321-L326,L440-L450; docs/receiver-direct-channel.html:L82-L96,L221-L223] | _to fill_ | _to fill_ |
| Codec / container | MP3 / AAC / Opus / WAV / raw PCM; bitrate; CBR vs VBR | MP3 CBR 320 kbps default (configurable 128–320), encoded by NAudio.Lame `LameMP3FileWriter`. Container: bare MP3 frames over HTTP chunked transfer (no MP4 / no ID3). MP3 frame at 48 kHz = 1152 samples = 24 ms. No `mp3Writer.Flush()` (preserves bit reservoir). [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L374-L394,L458-L465; src/Radio.Core/Configuration/AudioOutputOptions.cs:L246-L253] | Raw PCM s16le wrapped in a JSON envelope: `{ type:"audio", data:<base64>, seq, fmt:"pcm", sr, ch, ts }`. **No MP3, no WAV header on the wire** — receiver builds an `AudioBuffer` from raw Int16 samples directly. (Class comments and `WavChunkEncoder` reference an older WAV-based scheme; current implementation does not use them.) [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L436-L450; docs/receiver-direct-channel.html:L107-L137; src/Radio.Infrastructure/Audio/Outputs/WavChunkEncoder.cs:L1-L62 (unused at runtime)] | _to fill_ | _to fill_ |
| Chunk size & cadence | "100 ms WAV every 100 ms" vs "2 s OGG every 2 s" vs "no chunking — open HTTP stream" | Open HTTP byte stream; no application-level chunking. Server-side read buffer = `ClientBufferSize = 65 536 B` (~341 ms of 48 kHz s16le stereo PCM in, ~1.64 s of 320 kbps MP3 out per read). After initial burst the server paces to "no more than 10.0 s ahead of real time" before delaying. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L404-L422,L467-L481; src/Radio.Core/Configuration/AudioOutputOptions.cs:L245] | 100 ms per chunk default (clamped 50–200 ms in `DirectChannelChunkSizeMs`). At 100 ms: 19 200 B PCM → ~25 700 B Base64 in JSON envelope = 10 messages/sec. Real-time pacing via `Stopwatch` enforces "no faster than chunkMs per chunk". [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L321-L333,L402-L410; src/Radio.Core/Configuration/AudioOutputOptions.cs:L158-L167] | _to fill_ | _to fill_ |
| Transport | HTTP byte stream / Cast message bus / MSE-fed Range requests / HLS playlist polling | HTTP/1.1 `GET` to `http://<LAN-ip>:8080/stream/audio/mp3`, chunked transfer, `KeepAlive=true`, `Accept-Ranges: none`. Server uses raw `HttpListener` (not Kestrel) on port 8080, separate from API on 5000. CORS `*` for the Default Media Receiver origin. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L100-L137,L312-L361] | Cast custom message bus (Sharpcaster `ChromecastChannel` over the same TLS Cast control TCP socket) on namespace `urn:x-cast:com.radioconsole.audio`. JSON messages via `SendAsync` → per-message TCP write. Channel registered by reflection into SharpCaster's internal channel list. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastAudioChannel.cs:L23-L62; src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:L621-L638,L691-L748] | _to fill_ | _to fill_ |
| Receiver decoder API | `<audio>` element / Media Source Extensions (MSE) / Web Audio API (`AudioBufferSourceNode`) / native CAF `PlayerManager` | Native CAF `PlayerManager` driving the `<cast-media-player>` element (which hosts a `<video>` or `<audio>` element). MP3 decoded by Chrome's media pipeline (C++). [source-walked, sha=3b06f79, docs/receiver.html:L25-L88; src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:L1169-L1202] | Web Audio API — for every chunk: `AudioContext.createBuffer(2, samplesPerChannel, sr)` → manual de-interleave Int16 → Float32 in JS → `AudioBufferSourceNode.start(nextPlayTime)`. CAF `PlayerManager` is **not** used for media; only `CastReceiverContext` for the custom message bus. [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L82-L195,L213-L264] | _to fill_ | _to fill_ |
| Buffer target depth | Seconds the receiver tries to keep queued before playing | Server-side: pre-burst the ring-buffer reader 5.0 s behind write position, then pace to ≤ 10.0 s ahead of real time. Receiver-side: force play() when `media.buffered.end(0) >= 3.0 s`. So initial latency ≈ 3 s; steady-state Chrome-buffered depth grows toward 10 s (the server cap). [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L399-L422; docs/receiver.html:L50-L84] | Pre-play buffer: `BUFFER_BEFORE_PLAY = 3` chunks = 300 ms (configurable via `config` message). Steady-state target: 0–3.0 s (`MAX_BUFFER_AHEAD`, configurable). Sender reader lag default 1.0 s of historical PCM. [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L41-L42,L253-L260,L150-L159; src/Radio.Core/Configuration/AudioOutputOptions.cs:L176-L194] | _to fill_ | _to fill_ |
| Adaptive behavior | Does buffer grow under jitter? Bitrate switch on bandwidth drop? | None — CBR bitrate fixed at config time, no ABR / no HLS, no bandwidth probing. Buffer in the receiver can grow with jitter (no upper bound on the receiver side beyond Chrome's internal media-element heuristics); server caps growth at 10 s ahead by inserting `Task.Delay`. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L378-L394,L467-L481] | None — fixed chunk size, fixed PCM rate, no bitrate adaptation. Drift protection drops whole chunks when buffer-ahead exceeds 3.0 s rather than shrinking the rate; sender does not throttle on receiver feedback. [source-walked, sha=3b06f79, docs/receiver-direct-channel.html:L146-L159; src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L317-L502] | _to fill_ | _to fill_ |
| Clock sync model | Sender-clock master / receiver-clock master / NTP-aligned timestamps / no sync | Effectively sender-clock master via real-time `Stopwatch`-equivalent pacing (`DateTime.UtcNow` math: `sentAudioSec - elapsedSec`); no PTS embedded in MP3 frames; receiver runs Chrome's audio-element clock independently. No sync feedback loop. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L413-L481] | Sender-clock master: `Stopwatch` paces `chunksSinceStart * chunkMs`. Each message carries `ts = UnixTimeMs` (sender wall clock); receiver computes `transitDelay = Date.now() - msg.ts` for telemetry only (reported in pong) — not used to correct rate. AudioContext clock is the receiver master for playback; drift compensated only by chunk-drop. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L357-L410,L439-L450; docs/receiver-direct-channel.html:L65-L77,L141-L159,L226-L233,L311-L344] | _to fill_ | _to fill_ |
| Backpressure | Receiver tells sender to slow down? Or sender just blasts? | Implicit backpressure via TCP — if receiver stops reading, `OutputStream.WriteAsync` blocks and the sender loop stalls naturally. No application-level signal. Server has its own real-time governor (10 s ahead cap) so it does not "blast" in practice. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs:L458-L505] | None at the application level — sender awaits per-chunk `SendMessageAsync` ACK (TCP write completion), which is the only backpressure. Receiver telemetry (`bufferAhead` in pong) is informational only; sender never reads it. Receiver compensates unilaterally by dropping chunks. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L453-L489; docs/receiver-direct-channel.html:L146-L159,L311-L344] | _to fill_ | _to fill_ |
| Metadata channel | Separate from audio path or interleaved? Frequency? | Separate — audio is HTTP/1.1 on port 8080; metadata is a SharpCaster `MediaChannel.LoadAsync(media, true)` over the TLS Cast control socket (different transport). Updates debounced 3 s and sent only on track change. Album art is an absolute URL inside the metadata payload, not a binary blob. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:L933-L988,L994-L1031,L1169-L1202] | Interleaved — `type:"audio"`, `type:"config"`, `type:"ping"`, `type:"pong"`, `type:"stop"` all share the **same** custom Cast namespace on the **same** TLS socket. Album-art metadata still goes via `MediaChannel.LoadAsync` (separate channel, same socket) when `UpdateNowPlayingMetadataAsync` runs. Frequency: 10 audio msgs/sec + occasional config/ping. [source-walked, sha=3b06f79, src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs:L119-L138,L156-L180,L336-L352,L437-L457; docs/receiver-direct-channel.html:L213-L344] | _to fill_ | _to fill_ |

---

## 6. Findings synthesis (to be written after the matrices are filled)

A short prose section (~500 words) that reads across rows and columns of the two matrices and pulls out 3–5 *patterns* — not prescriptions. Examples of the *kind* of finding expected (not actual content, just shape):

- "Every reference system uses MSE rather than Web Audio API for continuous-audio cast; only RTest DirectChannel uses Web Audio with manual scheduling."
- "Buffer depths cluster around 4–8 seconds across reference systems; RTest's 100 ms chunk cadence with no jitter buffer is an outlier by an order of magnitude."
- "Reference systems all separate the metadata channel from the audio channel (so a 200 KB album-art payload can't head-of-line-block 100 ms of audio); RTest DirectChannel interleaves them on the same custom message bus."

Patterns. Not prescriptions. The "what to do about it" goes in §7.

---

## 7. Speculative — things RTest could try (research output, not a roadmap)

Five to eight entries, each in this shape:

> **Idea — \<short name\>**
> **Addresses**: FM1, FM5 (the failure modes this would mitigate)
> **What changes in RTest**: brief, concrete; sender? receiver? both? what code area?
> **Scope**: rough sense of how much code (e.g., "~50 LOC in receiver.html, no protocol change")
> **Risk / trade-off**: what we'd give up to get the mitigation (latency, complexity, etc.)
> **Confidence**: how strongly the reference systems' behavior suggests this is the right move (high / medium / low)

Each idea explicitly **is not a commitment**. A future plan would consume any one of these and turn it into real work via the normal Builder/queue flow.

---

## 8. Out-of-band notes

- TuneIn's receiver build number / Plex's receiver build number will be captured the moment we attach to each. Receiver code changes; this doc's findings are dated.
- If the inspection effort reveals that TuneIn or Plex has switched to a *native* CAF target (no HTML receiver), the column is dropped and replaced with the next-best candidate (Pandora, SoundCloud, NPR One).
- If RTest's stutter is reproducible *only on HttpMp3 mode* or *only on DirectChannel mode*, the failure-mode matrix for the un-affected mode is filled as "not applicable to symptom" rather than left blank — useful negative evidence.

### Source-walk discrepancies vs framework assumptions (sha=3b06f79)

These were noticed while filling RTest-HM / RTest-DC and may affect interpretation:

- **DC mode does NOT send WAV-base64** despite the framework description, the class-level XML comments in `DirectCastStreamingService.cs` (which mention "encodes each chunk as MP3 via LAME"), the `DirectCastAudioChannel` XML comment (mentions "Base64-encoded WAV"), and the existence of `WavChunkEncoder.cs`. The actual `StreamingLoopAsync` at L317-L502 ships **raw Int16LE PCM** in a JSON envelope; `WavChunkEncoder` is dead code at the current SHA. The DC receiver (`docs/receiver-direct-channel.html`, v11, "raw PCM Int16LE") confirms this.
- **HM mode AppId in `appsettings.json` is `567E3DBA`** (the *custom* receiver), not `CC1AD845` (Google's Default Media Receiver). The `GoogleCastOutputOptions` default value is still `CC1AD845`, but the deployed config overrides it. This means even HM mode currently launches the custom `receiver.html` and relies on its `setMessageInterceptor(LOAD)` to coerce `StreamType.LIVE` and the `setInterval(500ms)` force-play loop. The framework's assumption that "Chromecast's Default Media Receiver plays it" is currently false in production config.
- **There is no `StreamController` in Radio.API.** The MP3 endpoint `/stream/audio/mp3` is served by `HttpStreamOutput`'s own `HttpListener` on port 8080, not by Kestrel on port 5000. CORS, chunked transfer, and the LAME pipeline all live in `HttpStreamOutput.HandleClientAsync`.
- **`docs/receiver.html` has a JS bug**: the `setMessageInterceptor` callback is missing its closing `})` for the interceptor lambda — the `return request;` and `playerManager.addEventListener(...)` calls fall outside the lambda. This may or may not affect runtime behavior depending on how `setMessageInterceptor` defaults when no return value is provided. Worth verifying in DevTools during the runtime pass.
- **MP3 frame size at 48 kHz is 1152 samples = 24 ms**, not the "~26 ms" / "~46 ms per MP3 frame" referenced in §4 FM6 framing-question guidance. (26 ms is the 44.1 kHz frame size, 46 ms is double-frame.) Doesn't change the qualitative finding but worth knowing.
- Reader lag for HM is documented at 5.0 s in code (`lagSeconds = isMp3Endpoint ? 5.0 : null`) — this is *historical* PCM the reader gets immediately on connect to provide the initial 3 s burst to the Cast device. It is not "buffer depth" in the queueing-theory sense.

---

## Execution checklist (for the research pass that fills this doc)

- [ ] Verify `chrome://inspect` reach to the Chromecast on the LAN
- [ ] Cast from TuneIn, attach DevTools, capture receiver build number + URL
- [ ] Walk TuneIn receiver: decoder, buffer depth, chunk handling — fill TuneIn column of both matrices
- [ ] Cast from Plex, repeat
- [ ] Walk RTest-HM (`/stream/audio/mp3` server + Default Media Receiver behavior via DevTools)
- [ ] Walk RTest-DC (`receiver.html` source + `DirectCastStreamingService.cs` sender)
- [ ] Fill failure-mode matrix
- [ ] Fill pipeline table
- [ ] Write §6 synthesis
- [ ] Draft §7 speculative ideas
- [ ] Spec self-review (placeholders / contradictions / scope) — fix inline
- [ ] Surface to user for review
