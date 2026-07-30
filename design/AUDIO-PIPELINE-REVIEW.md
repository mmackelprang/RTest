# Audio Pipeline Deep Review — Sources, Engine, Bluetooth, Google Cast

**Date:** 2026-07-26
**Scope:** End-to-end audio data flow: MP3/file, USB capture, RTL-SDR, and Bluetooth sources; the SoundFlow engine wrapper and tap/streaming layer; the HTTP stream and Google Cast outputs. Framework fitness assessment (SoundFlow vs. alternatives). Efficiency/memory/CPU focus on the Bluetooth capture path and both Cast streaming modes.
**Target hardware note:** Production now targets the x64 Ubuntu box (`radio`, N100-class), not the Raspberry Pi. CPU-cost judgments below are calibrated to that; several docs and deploy configs still describe the Pi (see Hygiene findings).

---

## 1. Executive Summary

The pipeline is architecturally sound and unusually well instrumented for a hobby-scale system: push-model sources bridge into SoundFlow's pull-model graph through a single generic ring-buffer component, every stage carries counters/underrun/contention telemetry, and hard-won lessons (GC storms, native engine churn → SIGSEGV, PipeWire frame misalignment) are both fixed and documented. The Bluetooth path in particular has matured into the best-engineered source: a native `pw_stream` capture with optional SCHED_FIFO and a libsamplerate variable-rate resampler for clock skew.

The most significant issues found, in order of impact:

1. **The production Cast transport (DirectChannel) ships raw PCM as Base64 inside JSON over the Cast control channel** — ~2.2 Mbps sustained on a message bus designed for control traffic, ~1 MB/s of managed string garbage in a process that elsewhere fights for GC quiet, and heavy per-message work on the receiver SoC. It achieves its latency goal, but the same custom receiver could take a binary WebSocket (or encoded-audio) feed at a fraction of the cost.
2. **The Kestrel `/stream/audio` endpoint is dead** — the registered middleware reads from the legacy single-reader path that unconditionally returns 0 bytes; clients connect and receive nothing while the loop spins. The real stream lives on the separate `HttpListener` server on :8080.
3. **File-player seek is display-only and position is a wall-clock estimate**, although the SoundFlow version in use (`1.*` → 1.4.1) exposes `SoundPlayerBase.Seek()/Time/Duration`. Comments claiming the API doesn't exist are stale.
4. **The RTL-SDR ingest allocates a fresh 128 KB `IqSample[]` per USB read** (LOH-sized, ~100–150/s at typical rates ≈ 15–19 MB/s of Large-Object-Heap garbage), directly contradicting the GC discipline the DSP thread itself documents.
5. **The audio callback is not lock-free** and all four mixer modifiers process per-sample through virtual calls (plus two `Interlocked` ops per sample in the taps), when SoundFlow offers a block-level `Process(Span<float>)` override.

None of these are stop-ship on the N100; all are cheap-to-moderate fixes. SoundFlow remains a defensible framework choice — the recommendation is to stay on it, adopt the newer APIs it already ships, and keep the `IAudioEngine` seam clean so a Linux-native (PipeWire) backend remains a future option rather than a rewrite.

---

## 2. Pipeline Map (as-built)

```
MP3/File   FilePlayerAudioSource → PlaybackService.PlayFileAsync
           → File.OpenRead → StreamDataProvider → SoundPlayer ─┐
USB/Vinyl  capture device (own MiniAudioEngine!) → OnAudioProcessed
           → BufferedSoundGenerator<float> ───────────────────┤
RTL-SDR    rtlsdr_read_sync → IqSample[] (alloc/batch) → BlockingCollection
           → DSP thread (decim → demod → stereo/RDS → de-emph → decim)
           → AudioDataAvailable (reused buffer) → BufferedSoundGenerator ──┤
Bluetooth  bluez_input node → PipeWireNativeStream (pw_stream, S16LE@48k)
           → S16→F32 (scalar) → [libsamplerate ~1.00025] 
           → BufferedSoundGenerator<float> ───────────────────┤
                                                              ▼
                     PlaybackDevice.MasterMixer (SoundFlow, F32 interleaved)
                     modifiers per sample: Balance → Limiter →
                     FingerprintTap (2048-batch → ThreadPool) →
                     VisualizationTap (2048-batch → ThreadPool)
                     [device Volume applied AFTER modifiers — ADR-012]
                        │                        │
                        ▼                        ▼
                  Local speakers        TappedOutputStream
                                        (F32→S16, 5 s ring, one lock,
                                         N reader cursors, silence synth)
                              ┌──────────────┼───────────────────┐
                              ▼              ▼                   ▼
                    HttpListener :8080   Fingerprinting     DirectCastStreamingService
                    /stream/audio (WAV)  (SongRec etc.)     PCM → Base64 → JSON →
                    /mp3 (per-client                        Cast control channel →
                    LAME encoder) ←── Cast HttpMp3 mode     custom receiver (prod mode)
```

Kestrel also registers `AudioStreamMiddleware` at `/stream/audio` (port 5000) — see finding C1: it is non-functional.

---

## 3. Things Done Well

These are worth calling out because they should be *preserved* through any refactor:

- **`BufferedSoundGenerator<T>` as the single push→pull bridge** (`src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs`). Bulk span copies with wrap handling, pre-fill cushion, per-type overflow strategy, and an exemplary telemetry set: callback-interval min/max, missed-deadline counting *correlated with cached GC counts* (sampled off the hot path via `Volatile`), lock-contention timing that only pays for a `Stopwatch` when actually contended, and journald-aware log throttling. This is real-time-adjacent engineering discipline rarely seen in application code.
- **Clock-skew handling done right on Bluetooth.** The evolution is documented and visible in code: time-domain duplication with cosine crossfade (Path C) superseded by a libsamplerate variable-rate SRC (Path D, `SrcVariableResampler`) with the legacy compensator explicitly disabled to avoid double correction (`disableDriftCompensation`). Textbook.
- **Native PipeWire capture** (`PipeWireNativeStream`) replacing the `pw-record` subprocess: direct `pw_stream` with pinned callback delegates, frame-boundary truncation to prevent L/R channel shift on lossy BT delivery, `ArrayPool` for the conversion buffer, opt-in SCHED_FIFO with a correct "apply on the callback thread" placement, and a watchdog (`MillisecondsSinceLastOnProcess`) feeding a deduplicated self-healing path. The `pw-record` fallback is retained for missing native helper libs.
- **RTLSDRCore DSP chain**: dedicated consumer thread over a bounded `BlockingCollection` (USB reads never blocked by DSP), pre-allocated demod/decim/stereo buffers with the 131 GB-of-garbage GC-storm postmortem written into the comments (`RadioReceiver.cs:1146`), squelch that emits *silence buffers* instead of starving the downstream ring, and RDS decoding that snapshots PLL state pre-decode.
- **GC awareness at the engine level**: `GCLatencyMode.SustainedLowLatency` during engine lifetime, GC-count caching for callbacks, allocation-free flush work items (`FlushWorkItem : IThreadPoolWorkItem` instead of closures).
- **Operational maturity**: stalled-generator flow monitor with recovery events, diagnostics snapshots at every layer, metrics with delta-reporting discipline, Cast device cache with TCP reachability probes and session-expiry recovery, the ADR log (including decompiling SoundFlow to verify modifier/volume ordering, ADR-012), and `design/AUDIO-DATAFLOW.md` with a stage-by-stage latency budget.
- **Sensible streaming ergonomics**: frame-aligned reader lag so new Cast/HTTP clients get an instant pre-buffered burst (with the byte-shift corruption fix documented), real-time pacing to stop Chrome buffer bloat, CORS for the Cast receiver, LAME flush semantics handled correctly (no `lame_encode_flush` mid-stream).
- **Clean layering**: `Radio.Core` interfaces have no dependencies; sources are uniformly `IAudioSource` + `SoundComponent`; the extracted NuGet packages (RTLSDRCore, AudioAnalysis, etc.) keep the DSP testable. 1,400+ tests.

---

## 4. Findings

Severity: **[A]** correctness / user-audible, **[B]** efficiency (CPU/memory/bandwidth), **[C]** robustness/hygiene. Each finding includes a remedy.

### 4.1 Engine & tap layer

**A1. Kestrel `/stream/audio` endpoint streams nothing.**
`AudioStreamMiddleware` (registered in `Program.cs:149`) calls `audioEngine.GetMixedOutputStream()` and loops on `Read`, but `TappedOutputStream.Read` is the legacy single-reader path that hard-returns 0 (`TappedOutputStream.cs:357-361`). Every client that hits port 5000's documented `/stream/audio` gets headers and then an infinite 10 ms-delay loop server-side; the working endpoints are on the separate :8080 listener. *Remedy:* either delete the middleware and fix the docs, or make it call `CreateStreamReader(clientId)` like every other consumer. Deleting is preferable — two HTTP stacks serving the same stream is one too many (see C7).

**A2. Tap batches can be silently dropped.**
`BufferedTapModifier.ProcessSample` (`BufferedTapModifier.cs:63-91`): when the 2048-sample buffer fills while the *previous* flush is still executing on the ThreadPool (`_flushInProgress == true`), the entire new batch is discarded — a ~21 ms hole in the HTTP/Cast/fingerprint stream that local speakers never hear. Under ThreadPool starvation (e.g., heavy Blazor/API load) this produces intermittent Cast glitches that are extremely hard to attribute. Samples arriving while `index >= _bufferSize` are likewise dropped. *Remedy:* double-buffer swap (write into A while B flushes, swap atomically) or a small SPSC queue of batches; count drops in metrics either way.

**A3. Ring readers have no overrun detection.**
`TappedOutputStream.ReadForReader` computes availability as `(write - read + size) % size` (`TappedOutputStream.cs:172`). A reader stalled longer than the 5 s ring (Cast device on congested Wi-Fi, paused HTTP client) doesn't error — its cursor is silently lapped and the modulo arithmetic makes it appear nearly caught-up, so it resumes reading *torn* audio mid-buffer. *Remedy:* track a monotonic total-written counter per ring and total-consumed per reader; on lap, jump the reader to `write - lagBytes`, increment an `overrun` metric, and log once.

**B1. All four modifiers process per-sample; block API unused.**
`BalanceModifier`, `LimiterModifier`, and both taps override only `ProcessSample(float,int)` — 96 k virtual calls/s per modifier at 48 kHz stereo, plus one `Interlocked.Increment` per sample per tap (≈192 k interlocked ops/s) on the audio callback thread. SoundFlow (≥ the 1.4.1 the wildcard resolves to) exposes `SoundModifier.Process(Span<float> buffer, int channels)` for exactly this. *Remedy:* override `Process` in all four: balance = two per-block gain reads + vectorizable multiply; limiter = branchy but vectorizable (`TensorPrimitives`); taps = bulk `CopyTo` + one index update per block. This removes the per-sample interlocked traffic entirely. On the N100 this is a modest win (a few % of a core); it also shrinks callback jitter, which is the metric the codebase actually cares about.

**B2. The audio callback takes locks shared with arbitrary threads.**
`BufferedSoundGenerator.GenerateAudio` and `AddSamples` share `_bufferLock`; producers run on PipeWire/USB/DSP threads, the consumer is the MiniAudio callback. The code *measures* contention (good) rather than eliminating it. Worst case is the `Block` overflow strategy, where `AddSamples` does `Monitor.Wait` inside the lock the callback needs. With one producer and one consumer per generator this is a classic SPSC case. *Remedy:* volatile head/tail SPSC ring (no lock on either side); keep the lock only for `ClearBuffer`/diagnostics via a generation counter. Priority: medium — measured contention is currently low, but this is the structural fix that makes the rest of the RT story honest. At minimum, never use `Block` strategy on mixer-attached generators.

**B3. Float→S16 and S16→float conversions are scalar loops.**
`TappedOutputStream.WriteSamplesLinear` (per-sample clamp+scale under the ring lock) and `PipeWireNativeStream.OnProcess` (per-sample divide). Trivially vectorizable on x64 (`TensorPrimitives`/`Vector<short>` widen/narrow); also worth moving the tap's conversion *outside* the lock (convert into a scratch, memcpy under lock). Minor CPU, but it shortens the lock hold that readers contend on.

**C1. `AudioEngineOptions.BufferSize` is never applied.**
It is logged and exported as a gauge (`SoundFlowAudioEngine.cs:383,545`) but `InitializePlaybackDevice(deviceInfo, _audioFormat)` is called without a `DeviceConfig`, so MiniAudio's default period is in effect. The "1,024 samples = 21.3 ms" figures in `AUDIO-DATAFLOW.md` are an assumption, not a configuration. *Remedy:* pass a `DeviceConfig` with the configured period, or delete the option; either way stop reporting an unapplied value.

**C2. Manual device switching predates SoundFlow's native API.**
`SwitchPlaybackDevice` does stop → dispose → init → re-attach modifiers → restart → event → re-attach components (`SoundFlowAudioEngine.cs:747-819`, `SoundFlowPlaybackService.OnPlaybackDeviceSwitched`). SoundFlow 1.4.x has `MiniAudioEngine.SwitchDevice(AudioPlaybackDevice, DeviceInfo, DeviceConfig)` which preserves the device object (and everything attached to it). *Remedy:* adopt `SwitchDevice`; the whole re-attach dance and its `PlaybackDeviceSwitched` event likely disappear.

**C3. Silence synthesis inside the ring reader breeds compensating logic.**
`ReadForReader` fabricates zeroed PCM when a reader is caught up (`TappedOutputStream.cs:176-184`), so "no data" is indistinguishable from data. Three separate layers now re-derive real time to compensate: the reader's own `ReadAsync` delay, `HttpStreamOutput`'s sent-bytes-vs-wall-clock throttle, and `DirectCastStreamingService`'s stopwatch pacing. The Cast buffer-bloat bug this caused was patched with the ahead-cap. *Remedy (design):* return 0 on empty; give one shared component (a paced reader wrapper) the job of real-time cadence + keep-alive silence. Not urgent — the current stack works — but this is where the next timing bug will come from.

### 4.2 Sources

**A4. File seek is cosmetic; position is an estimate.**
`FilePlayerAudioSource.SeekCoreAsync` sets `_position` and returns (`FilePlayerAudioSource.cs:897-909`); nothing repositions the decoder, yet `IsSeekable => true` and the UI (and the persisted-position restore, `_pendingSeekMs`) believe it. Position advances by literally `+1 s` per monitor tick (`MonitorPlaybackAsync`, `:782-838`), so pause/resume timing skew accumulates and track-end detection (`_position >= _duration`) fires early or late. `SoundFlowPlaybackService.GetPosition` returns `TimeSpan.Zero` with a comment that the API doesn't exist (`SoundFlowPlaybackService.cs:714-725`) — but `SoundPlayerBase.Seek(TimeSpan)`, `.Time`, and `.Duration` all exist in the resolved SoundFlow 1.4.1. *Remedy:* plumb `player.Seek()`/`player.Time` through the playback service; use SoundFlow's `PlaybackEnded`/state rather than the wall-clock estimate for auto-advance. This turns three fragile mechanisms into one real one.

**B4. Every track is opened and decoded twice, on two engines.**
`LoadCurrentFileAsync` opens the file into a `ChunkedDataProvider` on FilePlayer's own private `MiniAudioEngine` (`FilePlayerAudioSource.cs:1835-1853`) — and that provider is never played; playback then calls `PlayFileAsync`, which opens the same file again into a `StreamDataProvider` on the main engine (`SoundFlowPlaybackService.cs:133-143`). Cost: an extra decoder init + file handle + native engine per track-load, for at most duration/metadata that TagLib already provides. *Remedy:* delete the private engine + provider from FilePlayer (metadata comes from TagLib; duration can come from `player.Duration` post-load), or conversely play the `ChunkedDataProvider` (pre-buffered decode) and drop the second open. Also audit `PlayFileAsync`'s `FileStream` ownership on the Stop path — the stream is only explicitly disposed on the error path (`:180`); confirm `SoundPlayer.Dispose()` disposes the provider/stream chain in 1.4.1, else this leaks one handle per track.

**B5. RTL-SDR ingest allocates a Large-Object-Heap array per USB read.**
`RtlSdrDevice.ConvertToIqSamples` news an `IqSample[16384]` = 131,072 B (> 85 KB ⇒ LOH) per `rtlsdr_read_sync` (`RtlSdrDevice.cs:497-516`), handed through `SamplesAvailable` → `_processingQueue` and dropped after `ProcessSamples`. At 1.9–2.4 MS/s that's ~115–146 LOH allocations/s ≈ **15–19 MB/s of LOH garbage**, forcing recurring Gen2/LOH collections — precisely the pauses `SustainedLowLatency` and the callback GC-correlation telemetry exist to avoid, and ironic given the fix documented at `RadioReceiver.cs:1146` cured the same disease downstream. *Remedy:* pool the batches (rent from an `IqSample[]` pool sized to the read length; return after `ProcessSamples`), or convert into per-consumer reusable buffers with an explicit ownership handoff. This is the single highest-leverage GC fix in the repo.

**B6. Each USB source spins up its own `MiniAudioEngine`.**
`USBAudioSourceBase.InitializeUSBCaptureAsync` creates a private engine for capture (`USBAudioSourceBase.cs:180`); Windows BT loopback does the same (`WindowsBluetoothService.cs:855`). With the playback engine and FilePlayer's metadata engine, a steady-state process can host 3–4 native miniaudio contexts (each probing JACK/Pulse/ALSA backends), and the repo's own comments record that engine create/dispose churn corrupts the native allocator after ~300 cycles. `MiniAudioEngine.InitializeCaptureDevice` (and `InitializeFullDuplexDevice`) work on the shared engine. *Remedy:* route all capture through the one shared engine (the device manager already has `SetSharedEngine` precedent); reserve separate engines for genuinely separate concerns.

**C4. Dead/na**ï**ve SDR bridge classes invite misuse.**
`SDRSoundGenerator` (per-sample `Queue<float>` enqueue/dequeue under a lock on the audio callback — the exact anti-pattern `BufferedSoundGenerator` replaced) and `SDRAudioDataProvider` (clones every event array; silently discards the *remainder of partially-consumed chunks*, `SDRAudioDataProvider.cs:139-151`) are referenced by nothing in production wiring. *Remedy:* delete both (git history preserves them).

### 4.3 Bluetooth path (requested focus)

Verdict first: **this is the strongest path in the system.** Capture is native, conversions are bounded, buffering is single-copy into the ring, clock skew is handled by a proper SRC, and failure modes have watchdogs. Remaining findings are second-order:

**B7. Two resample stages are active on a typical A2DP session.**
Phones commonly deliver 44.1 kHz A2DP; the `pw_stream` requests S16LE@48 k, so PipeWire resamples 44.1→48, then `SrcVariableResampler` stretches ~250 ppm for clock skew. Both are cheap on the N100 (SINC_FASTEST at 48 k stereo is ~1–2% of a core; PipeWire's resampler similar), so this is *acceptable* — but if you ever want it back: capture at the node's native rate and fold the 44.1→48 conversion into the variable SRC by centering the ratio at 48/44.1 instead of 1.0. One stage, same latency, same code path.

**B8. Per-callback marshaling and copies in `OnProcess`.**
Four `Marshal.PtrToStructure<T>` calls per callback (`PipeWireNativeStream.cs:388-406`) — replace with `Unsafe.Read<T>`/pointer casts over the (blittable) structs; and the resampler path does convert→SRC→`AddSamples`, i.e., two managed copies where a "lease the generator's write window" API would allow one. Both micro; list them as cleanup for when the file is next touched. The scalar S16→F32 loop is covered by B3.

**C5. Recovery machinery is layered enough to thunder.**
Three independent retry/recovery loops can trigger around one event: `SearchForCaptureDeviceAsync` (20×1 s), `BluetoothAudioSource.RetryCaptureInBackgroundAsync` (12×10 s), and the watchdog/stall path funneling into `StopCoreAsync→PlayCoreAsync` (guarded by `_recoveryInProgress`). The interlocks look correct, but the interaction surface is large and only partially deduplicated (the `_captureDeviceLock` 30 s wait covers the service layer). *Remedy:* no code change urgently needed; consider consolidating retries into one state machine when next in this area, and add a metric for "recoveries per hour" so regressions surface.

**C6. `BluetoothAudioSource.InitializeAsync` disposes shared service on source dispose.**
`DisposeAsyncCore` calls `_bluetoothService.StopAsync()` **and** `DisposeAsync()` on a DI-owned singleton service (`BluetoothAudioSource.cs:327-328`) — fine if the source uniquely owns the service, surprising otherwise. Verify ownership intent; DI containers disposing the same instance later is a double-dispose class of bug.

### 4.4 Google Cast path (requested focus)

Context: two modes exist. `HttpMp3` (default in `appsettings.json`) loads the :8080 LAME MP3 URL on the default/styled receiver. `DirectChannel` — **the mode both production configs deploy** (`deploy/debian-x64/appsettings.Production.json:20`, `deploy/raspberry-pi/...:27`) — pushes audio over a custom Cast namespace to the custom receiver app (`567E3DBA`) to cut latency from 4–10 s to <1 s.

**B9. DirectChannel transport: Base64 PCM in JSON on the control channel.** (`DirectCastStreamingService.cs:317-502`)
Per 100 ms chunk: 19,200 B PCM → `Convert.ToBase64String` (25,600-char string ≈ 51 KB UTF-16) → `JsonSerializer.Serialize` into a second ~52 KB string → UTF-8 bytes → SharpCaster protobuf frame → TLS. Steady state:
- **Bandwidth:** ~2.1–2.3 Mbps on the wire — ~7× the HttpMp3 mode (320 kbps) and ~16× an Opus stream, over the Cast *virtual connection* intended for control messages (64 KB hard cap per message; heartbeat PING/PONG shares it). Sustained media on this channel is the classic cause of "receiver randomly drops the session" bugs on congested Wi-Fi.
- **Allocations:** ~1 MB/s of short-lived strings/buffers in the API process — measurable Gen0 pressure in the process whose callback telemetry counts GC-correlated deadline misses.
- **Receiver cost:** 10–20×/s `JSON.parse` + `atob` + Int16→Float32 in JS on a low-end Cast SoC.
- **Doc drift:** the class remarks still describe the previous design ("encodes each chunk as MP3 via LAME … MSE audio/mpeg SourceBuffer") while the code sends `fmt:"pcm"` to Web Audio; `WavChunkEncoder` (the design before that) is now referenced by nothing.

*Remedies, in order of preference:*
1. **Keep the custom receiver, change the transport:** have the receiver open a plain WebSocket (or `fetch` of a chunked HTTP stream) back to the API and push **binary** frames — PCM if you want zero codec latency, or Opus/MP3 CBR for ~7–16× bandwidth reduction. This deletes Base64+JSON on both ends, moves media off the control channel (heartbeats become reliable), and keeps the <1 s latency since the receiver's buffer policy is yours. The existing message bus stays for config/ping/metadata — which it's actually good at.
2. If the message bus must remain the transport, send *encoded* audio (Opus at 128–192 kbps ≈ 1.6–2.4 KB per 100 ms chunk): ~10× smaller messages, ~10× less string garbage, receiver decodes natively via Web Audio `decodeAudioData`/MSE.
3. Whatever transport: pre-size and pool the chunk buffers (`ArrayPool<byte>` + `Convert.TryToBase64Chars` into a pooled char buffer + `Utf8JsonWriter` into a pooled `ArrayBufferWriter`) — this alone removes most of the 1 MB/s churn with zero protocol change.

**B10. HttpMp3 mode: metadata updates restart the stream.**
`UpdateNowPlayingMetadataAsync` debounces 3 s then issues a full `LoadAsync` (`GoogleCastOutput.cs:943-1041`) because the default receiver can only change metadata via LOAD — each track change interrupts audio (the "garbled audio on rapid changes" the debounce comment describes). But production runs the **custom** receiver, which already receives arbitrary JSON on the custom namespace — *send metadata as a message and never reload media.* One small receiver-side handler eliminates the reload, the debounce machinery, and the session-expiry recovery it drags in. (Keep the reload path only for the true default-receiver fallback.)

**B11. Per-client LAME encoder on the :8080 MP3 endpoint.**
Each MP3 client constructs its own `LameMP3FileWriter` (`HttpStreamOutput.cs:374-394`) — N clients ⇒ N encoders encoding identical PCM. Fine at 1–2 listeners; if multi-room/multi-client is ever a goal, encode once into a shared MP3-frame ring and fan out bytes per client (also gives all clients identical frame timing). Note `mp3Writer.Write` + `Flush` are synchronous on the handler task — acceptable at this scale, but the write loop holds a ThreadPool thread per client during network pushback.

**C7. Two HTTP servers, one broken endpoint, reflection into SharpCaster.**
(a) The standalone `HttpListener` on :8080 duplicates what Kestrel (port 5000) already is — one server would simplify URLs, TLS posture, and the Windows URL-ACL note in the code; the middleware bug (A1) exists precisely because the two stacks drifted. (b) `RegisterCustomChannel` injects the DirectChannel into SharpCaster's non-public `Channels` property via reflection with array-type reconstruction (`GoogleCastOutput.cs:695-748`) — it works today and is guarded, but any SharpCaster upgrade can break casting at runtime, silently. Pin the SharpCaster version (it is pinned — 3.0.0 ✓), add a startup assertion test that the reflection path still finds the property, and consider upstreaming a `RegisterChannel` API.

### 4.5 Hygiene / docs

- **Target-hardware drift:** `CLAUDE.md` ("Target Platform: Raspberry Pi 5"), the Cross-Platform Requirements section, and `deploy/raspberry-pi/` all describe the retired Pi target; production is the x64 Ubuntu `radio` box. Update docs; decide whether ARM64 support is still a build gate or can be dropped from constraints.
- **Stale comments** (each cost this review real time, and will cost the next reader too): DirectChannel MP3/LAME remarks (`DirectCastStreamingService.cs:17-38`), "Position tracking not available in current SoundFlow API" (`SoundFlowPlaybackService.cs:721`), `SearchForCaptureDeviceAsync`'s pw-record comment block above native-stream code (`LinuxBluetoothService.cs:1285-1288`), `AUDIO-DATAFLOW.md` Stage 5/6 describing HttpMp3 as *the* Cast path with no DirectChannel section.
- **Dead code:** `SDRSoundGenerator`, `SDRAudioDataProvider`, `WavChunkEncoder`, the legacy `Read`/`Available` members of `TappedOutputStream`, and `AudioStreamMiddleware` (pending A1 decision).
- **`SoundFlow` version floats (`1.*`).** For the component that owns the audio callback, an unpinned minor bump is a silent behavior change (and 1.x is a young, single-maintainer project). Pin the exact version (as done for SharpCaster and SQLitePCLRaw) and upgrade deliberately — especially since adopting 1.4.x APIs (Seek/Time, `Process(Span)`, `SwitchDevice`) is recommended above.
- **`SoundFlowMasterMixer` naming:** it is a volume/balance/mute state holder + source registry; the actual mixer is SoundFlow's. Rename (e.g., `MasterAudioState`) or fold into the engine wrapper when convenient.

---

## 5. Framework Assessment: SoundFlow

**ADR-001 (Nov 2025)** chose SoundFlow (MiniAudio backend) over NAudio (Windows-only device I/O), PortAudio (raw interop, no graph), and SDL2 (no mixer). That reasoning was and remains sound; nothing found in this review says "wrong framework." What the codebase *does* show is where SoundFlow's envelope ends, because everything past it was built by hand:

| Need | SoundFlow provides | Project built |
|---|---|---|
| Mixing graph, device I/O, decoding | ✓ (miniaudio: mp3/flac/wav/vorbis) | — |
| Push-model live sources (BT/SDR/USB) | pull-only `SoundComponent` | `BufferedSoundGenerator` + drift comp + SRC |
| Multi-consumer output tap | per-sample modifiers | `BufferedTapModifier` + `TappedOutputStream` |
| Encode + network streaming | — | LAME + `HttpListener` server + pacing |
| Cast transport | — | SharpCaster + custom receiver + DirectChannel |
| Cross-process audio (BT via BlueZ) | — | native `pw_stream` interop |

Alternatives, judged against what this project actually needed:

- **GStreamer (gstreamer-sharp):** the only alternative that would have absorbed most of the hand-built column — `uridecodebin` (files), `pipewiresrc` (BT/USB capture *with* rate negotiation), `audiomixer`, `tee` (taps), `lamemp3enc`/`opusenc`, `souphttpsink`/`hlssink`/WebRTC for delivery, all with buffer/clock management. Cost: GLib mainloop integration in .NET, rougher bindings, heavyweight dependency, much harder Windows dev story, and far less control than the team has exercised (e.g., Path D would have been a pipeline property, but the RDS/PLL work would still be custom). A reasonable road not taken; not worth migrating a working system for.
- **PipeWire-native graph (Linux-only):** now that production is a single Ubuntu box already running PipeWire — and the codebase already speaks native `pw_stream` for BT capture — a design where playback is also a `pw_stream`, mixing/resampling happen in the PipeWire graph, and taps are loopback/monitor nodes is genuinely attractive: one clock domain, no drift compensation, no second audio stack. It forfeits the Windows dev loop, which CLAUDE.md still lists as a requirement. Keep it as the *contingency architecture*, not a plan.
- **NAudio / CSCore / Bass / LibVLCSharp:** respectively Windows-bound for device I/O, unmaintained, commercially licensed, and playback-oriented (no graph taps). None fit better today.
- **Raw miniaudio P/Invoke:** would trade SoundFlow's component model for full control of the duplex/callback config; given how thin the actually-used SoundFlow surface is (engine, device, mixer, `SoundComponent`, modifiers), this is less absurd than it sounds — but it re-implements decoding plumbing for no user-visible gain.

**Recommendation: stay on SoundFlow.** Concretely: pin the version; adopt the 1.4.x APIs the code predates (`Seek`/`Time`, block `Process`, `SwitchDevice`, shared-engine capture devices, possibly `DeviceConfig` for period control); keep `IAudioEngine`/`IAudioSource` as the hard seam it already is (ADR-004 explicitly reserved the right to swap); and treat the PipeWire-native option as the documented fallback if SoundFlow stagnates or the callback-jitter budget ever tightens beyond what B1/B2 buy back.

---

## 6. Prioritized Remediation Plan

| # | Item | Findings | Effort | Payoff |
|---|------|----------|--------|--------|
| 1 | Fix or delete Kestrel `/stream/audio` middleware + docs | A1 | XS | Removes a dead documented endpoint |
| 2 | Real seek + position via `SoundPlayerBase.Seek/Time`; state-driven track-end | A4 | S | Working seek; accurate progress; correct auto-advance |
| 3 | Pool RTL-SDR IQ batches (kill 15–19 MB/s LOH churn) | B5 | S | Removes recurring Gen2 pauses while SDR plays |
| 4 | Tap flush double-buffering + drop counter; reader overrun detection | A2, A3 | S–M | Eliminates silent stream gaps and lap corruption |
| 5 | DirectChannel: pooled buffers now; binary WS / encoded-audio transport next | B9 | S (pool) / M (transport) | ~1 MB/s less garbage now; ~7–16× less bandwidth + stable heartbeats after |
| 6 | Custom-receiver metadata messages instead of media reload (HttpMp3) | B10 | S | No audio interruption on track change |
| 7 | Block-mode modifiers (`Process(Span)`), vectorized conversions | B1, B3 | M | Lower callback cost/jitter; removes 192 k interlocked/s |
| 8 | One shared MiniAudioEngine (USB capture, FilePlayer); drop double file open | B4, B6 | M | Fewer native contexts; less churn risk (SIGSEGV class) |
| 9 | SPSC rework of `BufferedSoundGenerator`; ban `Block` on mixer path | B2 | M | Lock-free callback; structural RT-safety |
| 10 | Hygiene: dead code, stale comments, pin SoundFlow, `SwitchDevice`, docs (incl. x64 target) | C1–C7, 4.5 | S | Review speed; upgrade safety; truthful docs |

Suggested sequencing: 1–4 are independent and safe now; 5's pooling step is safe now with the transport change behind a receiver update; 7–9 deserve a perf branch with the existing callback-telemetry as the before/after harness (it's already ideal for this).

---

## 7. Overall Suggestions

1. **Adopt an explicit RT-audio policy** and write it into CLAUDE.md: *no locks, no allocations, no syscalls, no unbounded work on `GenerateAudio`/`Process` paths.* The codebase already measures violations better than most products; the policy turns those metrics into gates instead of dashboards.
2. **Spend the Cast budget on the custom receiver you already own.** Most Cast pain (latency, metadata reloads, transport overhead) traces to treating the receiver as immutable. It isn't — it's yours. Binary transport + metadata messages make both Cast modes converge into one good one.
3. **Make the GC lesson systemic.** The project has now hit the same class of bug three times (131 GB DSP garbage, journald log-storm feedback, and the still-live SDR LOH churn). A tiny allocation test — run each source for 60 s under `dotnet-counters`, assert Gen2/LOH deltas ≈ 0 — would catch the next one in CI instead of in a two-day soak.
4. **Trim the speculative/legacy layers as you touch them** (dead SDR bridges, WavChunkEncoder, legacy tap reader, pw-record fallback once native has months of soak). Every retired path shrinks the recovery-interaction matrix (C5) that is currently the hardest thing to reason about.
5. **Keep the ADR discipline going** — it materially accelerated this review — and add two entries: the DirectChannel transport decision (with its bandwidth/GC trade-offs made explicit) and the x64-Ubuntu retarget, so the docs stop describing hardware you no longer deploy to.

---

*Method note: this review was produced by reading the full hot-path source of the engine wrapper, tap/streaming layer, all four source families, the PipeWire/BlueZ interop, both Cast modes, and the HTTP output; SoundFlow 1.4.1's public API surface was verified against the published package (XML docs) rather than assumptions. Line references are to the branch state at the time of review.*
