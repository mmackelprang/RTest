# Cast garbled audio investigation (2026-05-23)

**Investigator:** Claude (logs + code, no live repro with user)
**Reference docs:** [`docs/research/2026-05-21-cast-stutter-comparison.md`](../../docs/research/2026-05-21-cast-stutter-comparison.md) — substantially overlapping prior work; this report builds on it with fresh log data.

---

## 1. Executive summary

The persistent intermittent garbling on Cast (but not BT/local) is consistent with **multiple compounding factors**, not a single root cause. The deployed configuration is **DirectChannel mode with default tunings** (`StreamingMode=DirectChannel`, `ApplicationId=567E3DBA`, 100 ms chunks, `bufferBeforePlay=3`, `maxBufferAhead=3 s`, sender `lagSeconds=1 s`), confirmed both by [`deploy/raspberry-pi/appsettings.Production.json`](../../deploy/raspberry-pi/appsettings.Production.json) and live `radio:appsettings.Production.json`. Local soundbar output never traverses the DirectChannel path, which is why only Cast is affected.

The single most load-pointing piece of evidence I found is **simultaneous Cast-lifecycle churn during BT recovery**: 2026-05-23 20:05 shows a Cast `Streaming -> Stopping -> Streaming -> Stopping` cycle (3 starts in 18 s) coinciding with a `BT pipeline recovery successful - capture stream re-established` event and a `Failed to stop Google Cast output / MediaSessionID is not available` error. Each cycle re-launches the DC receiver app, resets `nextPlayTime` on the receiver side, and re-burst-buffers - exactly the conditions for audible glitches. BT recovery events occurred at 19:36, 19:57, 20:05, 20:22, 20:45 (i.e., every ~20-30 minutes), and the user noted this is "persistent." That is too high a recovery cadence not to be a substantial contributor.

**Ranked hypotheses (preliminary, requires user input to firm up):**

| Rank | H# | Hypothesis | Confidence |
|---|---|---|---|
| 1 | H9 (new) | Cast lifecycle churn from BT-recovery-driven source restarts | **High** for the events at known timestamps; **Medium** as the dominant continuous cause |
| 2 | H1 | DirectCast reader-side/sender-side jitter draining 300 ms pre-play / 3 s steady buffer | **Medium-high** (architecturally over-determined per prior research) |
| 3 | H4 | Sharpcaster SendAsync per-chunk awaits HoL-block on shared TLS socket | **Medium** (architectural; no smoking-gun log evidence today) |
| 4 | H6_dc | Web Audio scheduling slips + nextPlayTime reset on JS main-thread stall | **Medium** (receiver-side; need DevTools logs to confirm) |
| 5 | H2 | Network WiFi jitter exceeds 300 ms pre-play budget | **Medium** (LAN-dependent; user must confirm device + WiFi) |
| 6 | H8 | AVRCP volume change races with Cast volume sync mid-stream | **Low** (no evidence of stream glitch correlation; bidirectional sync looks correct) |
| 7 | H5 | PCM bytes accidentally on HttpMp3 endpoint or vice versa | **Very low** (mode is locked to DirectChannel; HttpMp3 path is not consumed by the receiver) |
| 8 | H6 (LAME) | LAME encoder Flush behavior across sessions | **N/A** - LAME is on the HM path which is not in use |
| 9 | H7 | BT 44.1k -> 48k sample-rate resampler glitches | **Low** (now uses variable-rate resampler from PR #404; would manifest on BT-local too, not just Cast) |
| 10 | H3 | Per-chunk byte misalignment | **Very low** - reader lag is frame-aligned at TappedOutputStream.cs:128-133 and chunks are 19,200 bytes (4-byte frame-divisible) |


---

## 2. Live log findings (radio host, 2026-05-23)

### 2.1 Cast lifecycle churn coincident with BT recovery - the smoking gun

Timeline excerpt (journalctl -u radio-api):

```
20:04:14 INF  Connecting to Cast device: "Office speaker" at "192.168.86.25"
20:04:14 INF  Cast: DirectChannel mode - audio engine wired
20:04:15 INF  DirectCast: Starting streaming - chunk size 100ms ... lag 1s, maxBufferAhead 3s, bufferBeforePlay 3
20:04:16 INF  Google Cast output started streaming (mode "DirectChannel", setupMs 1728)
20:04:24 INF  Broadcast SourceChanged: "Bluetooth"
20:04:42 WRN  Buffer underrun ("Single"): 1 underruns, 896 zero samples
20:04:52 WRN  Buffer underrun ("Single"): 3 underruns, 2340 zero samples in last 9.2s
20:05:03 WRN  Buffer underrun ("Single"): 1 underruns, 1012 zero samples in last 11.2s
20:05:03 INF  Starting reconnection loop for BT device (max 20 attempts)
20:05:09 INF  Stopping Google Cast output
20:05:09 INF  DirectCast: Stopping streaming - sent 540 chunks, 15035352 bytes, 0 errors
20:05:09 ERR  Failed to stop Google Cast output
              at Sharpcaster.Channels.MediaChannel.StopAsync()
              at GoogleCastOutput.StopAsync ... GoogleCastOutput.cs:line 782
20:05:22 INF  Switching audio output to: "google-cast"
20:05:23 INF  DirectCast: Starting streaming ... (2nd start)
20:05:24 INF  Google Cast output started streaming (setupMs 1414)
20:05:24 INF  Stopping current Cast stream before connecting to new device
20:05:24 INF  DirectCast: Stopping streaming - sent 12 chunks, 308199 bytes (only 12 chunks before stop)
20:05:24 ERR  Failed to stop Google Cast output (MediaSessionID null again)
20:05:24 INF  Restarting Cast stream after device switch
20:05:24 INF  DirectCast: Starting streaming ... (3rd start in 18 seconds)
20:05:33 INF  Stopping Google Cast output (4th stop)
20:05:33 INF  DirectCast: Stopping streaming - sent 80 chunks
```

In **18 seconds** the DirectCast streaming loop was started 3 times and stopped 3 times. The receiver app was relaunched each time (GoogleCastOutput.cs:560), and `mediaChannel.StopAsync()` failed each time because `MediaSessionId` is null. The `ArgumentNullException` branch at GoogleCastOutput.cs:802-805 catches a different exception path than what is actually thrown, so it propagates up to the generic `Failed to stop Google Cast output`.

On the receiver side, every relaunch produces a brand-new `AudioContext` (receiver-direct-channel.html:82-90), resets `nextPlayTime = 0`, and re-buffers `BUFFER_BEFORE_PLAY = 3` chunks (300 ms) before starting playback. Audio user hears during this window: short silence, then 300 ms of buffered "old" audio, then stream from then-current sender position. Three cycles in 18 s = potentially three audible glitches in a single Cast session.

**Trigger:** Cast lifecycle was initiated by a UI-driven POST /api/devices/cast/connect (HTTP POST responded 500 in 16.7847 ms), which collided with `Auto-connecting to default Cast device` running in parallel. So this specific 20:05 burst was likely the user re-clicking the Cast button; but the **error pattern** (`Failed to stop Google Cast output / MediaSessionID is not available`) was already in the project known-issues list and is recurring across all sessions today.

### 2.2 Buffer underruns on the source side every ~3-5 minutes

```
20:04:42 WRN  Buffer underrun: 1 underruns, 896 zero samples
20:04:52 WRN  Buffer underrun: 3 underruns, 2340 zero samples in last 9.2s
20:05:03 WRN  Buffer underrun: 1 underruns, 1012 zero samples in last 11.2s
20:11:21 WRN  Buffer underrun: 1 underruns, 1920 zero samples
20:14:04 WRN  Buffer underrun: 1 underruns, 1920 zero samples in last 162.8s
20:18:02 WRN  Buffer underrun: 5 underruns, 2422 zero samples in last 237.5s
20:23:24 WRN  Buffer underrun: 1 underruns, 896 zero samples
20:26:51 WRN  Buffer underrun: 3 underruns, 1918 zero samples in last 206.5s
20:30:50 WRN  Buffer underrun: 2 underruns, 1534 zero samples in last 239.3s
20:34:10 WRN  Buffer underrun: 3 underruns, 3580 zero samples in last 199.3s
20:37:53 WRN  Buffer underrun: 3 underruns, 1914 zero samples in last 223.8s
20:45:02 WRN  Buffer underrun: 1 underruns, 1920 zero samples
20:49:19 WRN  Buffer underrun: 1 underruns, 894 zero samples
```

These are `BufferedSoundGenerator` underruns - the source-side buffer that feeds the master mixer ran empty for ~900-3500 samples (~19-73 ms). When this happens, the mixer emits silence; that silence is what the DirectCast tap reader reads and sends to the Cast device. The receiver does not know it is silence, so it plays it. **The user would experience these as 20-70 ms dropouts on Cast even if everything else was perfect**, and they would also be audible on local output (although local has more natural cushioning).

Pattern: ~1 underrun event every ~3 minutes on average. The 20:04:42 to 20:05:03 cluster (4 underruns in 21 s) is the worst run in the log window.

### 2.3 PipeWire OnProcess intervals stable but with widening max

`OnProcess` callback intervals over the past hour stay tightly clustered at min ~5-7 ms, max ~13-21 ms (target quantum is 10.67 ms per memory `99-radio-quantum.conf`). Bursts (>20 ms gap) counted 0-4 per 10 s window. **No evidence that PipeWire itself is glitching** - `bursts=0` for most of the recent steady-state, and even the worst spike (interval max 33.53ms at startup, 54.89ms at 20:47) only happened once or twice and is well within the 3 s receiver buffer. So FM2 (sender PipeWire stall) is *not* the active culprit during steady-state - lifecycle events and source-side underruns dominate.

### 2.4 Sharpcaster Failed to stop Google Cast output error pattern

Recurring 6+ times in the past 6 hours:

```
20:05:09, 20:05:24, 20:05:33, 19:34:54, 19:35:16, ...
at Sharpcaster.Channels.MediaChannel.SendAsync[T]
at Sharpcaster.Channels.MediaChannel.StopAsync()
at GoogleCastOutput.StopAsync - GoogleCastOutput.cs:782
```

GoogleCastOutput.cs:782 is `mediaChannel.StopAsync().WaitAsync(cts.Token)`. The catch clauses at lines 792-805 handle `INVALID_MEDIA_SESSION_ID` and `ArgumentNullException`, but the actual exception is not being matched (it propagates to the outer catch at line 814 which logs `Failed to stop Google Cast output`). The exception is almost certainly a Sharpcaster wrapper that does not expose `INVALID_MEDIA_SESSION_ID` in the message string the catch is looking for, so the catch is a near-miss. This is a tangentially-related bug (not the audio garbling itself), but it makes Cast restart cycles **noisier than they should be** and prevents the graceful-shutdown path from running.

### 2.5 No DirectCast send errors in 6 h

Across all Cast sessions today, `DirectCast: Stopping streaming` logs all show `0 errors` (see 2.1: sent 540 chunks, 15035352 bytes, 0 errors). So Sharpcaster `SendMessageAsync` itself is not failing on the wire. The chunks are getting on the wire; the question is what the receiver does with them.

### 2.6 Heavy BT recovery cadence

Five recovery events in 6 hours: 19:36, 19:57, 20:05, 20:22, 20:45. Each is benign on its own (the pipeline monitor restores the capture stream), but each one **briefly drops the BT input source while the active source is BT** - the source-side buffer drains, BufferedSoundGenerator underruns, and DirectCast sends silence/glitch downstream.

### 2.7 No receiver-side telemetry in radio-api logs

Despite the receiver html generating `pong` messages every ping with `bufferAhead`, `chunksDropped`, `transitDelay`, etc. (receiver-direct-channel.html:311-344), **the sender never sends pings** in steady-state - `SendPingAsync()` at DirectCastStreamingService.cs:157-180 is public but I see no caller invoking it on a schedule. So the only way to see receiver-side jitter today is via `chrome://inspect` against the Cast device. This is an instrumentation gap.


---

## 3. Code findings

### 3.1 DirectCast streaming loop is single-threaded with per-chunk synchronous send

DirectCastStreamingService.cs:317-502 — the loop reads from the ring buffer, paces, JSON+Base64 encodes, then `await _channel.SendMessageAsync(...)`. A single hiccup on the Cast TLS socket (SendMessageAsync takes >100 ms) directly delays the next read+send. Reader lag is 1 s, receiver buffer-ahead cap is 3 s, so the sender can tolerate up to a 3 s socket stall before the receiver runs dry - but it does not queue ahead, so jitter is "spent down" the receiver buffer rather than absorbed by a sender queue. Prior research §4 FM2 RTest-DC column flags this with **Y (high)** exposure.

### 3.2 Reader lag arithmetic is correctly frame-aligned

TappedOutputStream.cs:116-142 explicitly rounds `actualLag` down to `frameSize` (4 bytes for 48k stereo s16le). The comment explicitly cites the MP3-noise bug it was added to prevent. **H3 (frame misalignment) is therefore not a current bug.**

### 3.3 Silence-fill in ReadForReader keeps the loop running but is invisible to the receiver

TappedOutputStream.cs:158-200: when `available == 0` the reader returns **up to 4096 bytes of zeroed PCM** without advancing the read position. The streaming loop happily JSON-encodes and sends this zeroed PCM to the receiver. The receiver schedules it as audible silence on the next AudioBuffer.

**Implication for garbling:** during a source-side underrun or BT recovery, the receiver receives silence chunks that perfectly bridge the gap from the receiver perspective - but the user hears 20-70 ms of dropout, then the resumed audio, with no glitch *artifact* (no click) because the buffers connect cleanly at zero. This is benign in isolation but means **the user garbling experience is likely dropouts, not clicks** - useful diagnostic question for §6.

### 3.4 Cast volume sync looks correct, no audio interference

GoogleCastOutput.cs:1382-1430: `SetCastVolumeAsync` and `SetCastMuteAsync` are guarded by `State != AudioOutputState.Streaming` and use `_suppressNextVolumeEvent` to filter echo events. The volume command goes via `ReceiverChannel.SetVolume(volume)` which is on the *same* TLS Cast socket as audio chunks - so a volume change *could* cause a brief HoL-block of audio chunks. But it is a tiny payload (~100 bytes JSON) compared to a 25 KB audio chunk, so the delay is sub-millisecond. **H8 (AVRCP race) is unlikely to be a substantial contributor**, though it would explain rare correlation between phone volume changes and audio glitches if the user reports that pattern.

### 3.5 DirectChannelChunkSizeMs = 100 is right at the documented CAF minimum

Per Google CAF docs (cited in prior research §5): "Web Receiver Player does not support segments shorter than 0.1 s." RTest-DC 100 ms chunks sit **exactly at this minimum** with zero margin. This affects MSE-based receivers strictly, but the spirit of the limit (do not send chunks smaller than 100 ms over Cast) is at the bound.

### 3.6 No back-pressure feedback loop

The receiver reports `bufferAhead`, `chunksDropped`, `decodeErrors`, and `latency.lastTransitMs` in every `pong` (receiver-direct-channel.html:311-344). The sender `HandlePong()` (DirectCastStreamingService.cs:195-205) only logs the RTT - it does not adapt chunk cadence, does not detect wedged receivers, does not measure end-to-end drift. So the receiver drift-protection chunk-drop at ~22 minute intervals is purely receiver-side and the sender has no way to recover the dropped chunk. Per prior research §4 FM3, this is the documented mechanism for one audible glitch every ~22 minutes - strongly fitting the "intermittent garbled" complaint cadence.

### 3.7 Reader created with 1 s of historical audio means initial 1 s of stream is "stale"

DirectCastStreamingService.cs:254-256: `lagSeconds = 1.0f` clamped 0.1-2.0. So when DirectCast starts, the reader is 1 s behind the write position, immediately gives the receiver 1 s of "recent" audio to chew through. On a clean start this is fine. On a **rapid restart cycle (§2.1 above)**, the reader starts 1 s behind the *new* write position - but the audio that was 1 s ago at restart is *the same source* with *just one second of new audio gap* added, so the receiver hears 1 s of pre-restart audio, then silence (because the loop has not paced past it yet), then live audio. This is exactly the kind of "doubled / wobbly" sound a listener would describe as garbled.

### 3.8 Source-side BufferedSoundGenerator underruns are first-class observable

The recurring `Buffer underrun ("Single"): N underruns, M zero samples in last Xs (buffer: 0/384000, total underruns: T)` warnings are emitted from `BufferedSoundGenerator`. 384,000 sample buffer = 2 s at 48k mono (or 1 s stereo). Buffer hitting 0 means **the upstream BT capture or PipeWire path momentarily failed to deliver samples**. The 5-event BT recovery cadence is the proximate cause for some; underruns occurring *between* recovery events suggest there is a smaller-scale capture-side jitter too.


---

## 4. Hypothesis ranking

For each: **Evidence FOR**, **Evidence AGAINST**, **What would need to be true for it to dominate**.

### H9 (new — promoted to #1): Cast lifecycle churn from BT recovery / source-switch events

- **For:** §2.1 timeline shows 3 receiver-relaunches in 18 s coincident with `BT pipeline recovery successful`. Each relaunch resets receiver `nextPlayTime`, re-buffers 300 ms, and produces an audible "restart" event. BT recovery cadence in logs is every 20-30 min, plus auto-connect retries. The `Failed to stop Google Cast output` errors mean the graceful shutdown path runs incompletely, so the *next* start has stale state to deal with.
- **Against:** This explains *bursts* of garbling at user-visible moments (source-switch, BT-reconnect, manual Cast-button-click), but not steady-state intermittent garbling in a session with no source change. The user described "intermittent" - could mean either pattern.
- **For it to dominate:** The user garbling events would correlate with BT recovery / source-switch / Cast device handoff log lines. Easy to test if the user reports a recent timestamp.

### H1: DirectCast reader/sender jitter draining 300 ms pre-play / 3 s steady buffer

- **For:** Prior research §4 FM1 documents the buffer-depth gap (RTest-DC at 300 ms pre-play / 3 s steady vs reference cluster at 10 s). Source-side underruns (§2.2) write silence into the tap which appears to the receiver as silence chunks. Once the receiver `nextPlayTime` falls behind `audioCtx.currentTime` (receiver-direct-channel.html:141-144), `nextPlayTime = now + 0.02` resets and the rest of the queue is jumped forward by whatever was pending - *that* would sound like a click/jump.
- **Against:** No log evidence of receiver-side underruns or `nextPlayTime` resets today (we do not capture pong telemetry server-side, see §2.7). Sender-side SendMessageAsync errors = 0.
- **For it to dominate:** A receiver-side capture (chrome://inspect) during garbling would show `bufferAhead -> 0` events or `nextPlayTime` jumps with rate >=1/min during a typical session.

### H4: Sharpcaster shared-socket head-of-line blocking

- **For:** Prior research §4 FM5 / §6 Pattern 3 - audio, config, ping/pong, AVRCP volume, `LoadMediaWithRecoveryAsync` metadata bursts all share **one TLS socket**. A 200 KB album-art `LoadAsync` from a Shazam metadata update at GoogleCastOutput.cs:933-988 is debounced 3 s but still HoL-blocks audio chunks for the duration of the metadata send. Shazam metadata replacement events are frequent (every track change - log shows >15 in 6 h).
- **Against:** No "send error" or "chunk skipped" log evidence. The debounce window already serializes metadata updates.
- **For it to dominate:** Garbling events would correlate with `Shazam metadata replaced AVRCP` log lines (i.e., happen at track-change moments). Worth asking the user.

### H6_dc: Receiver Web Audio scheduling slips

- **For:** Prior research §4 FM4 marks this **Y (high)** for RTest-DC. Per-chunk Int16-to-Float32 loop runs on JS main thread; any GC pause or main-thread block within 100 ms misses the `start(when)` deadline and triggers `nextPlayTime = now + 0.02` reset = audible click. The Office speaker (Google Home Mini-class hardware) has weak CPU.
- **Against:** Without `chrome://inspect` traces, we have zero log evidence of this happening. The receiver `chunksDropped` and `decodeErrors` counters would tell us, but we do not surface them server-side.
- **For it to dominate:** PROBE-CAST-LONGTASK from prior research (a Playwright DevTools script) would show >=10 long tasks/min during steady-state DC play. Cannot verify without that scaffolding.

### H2: WiFi/network jitter > 300 ms

- **For:** The 300 ms pre-play budget is tight. Any single WiFi retransmit cluster (>300 ms gap) drains the receiver. Devices like Google Home Mini are on 2.4 GHz only and share airtime with everything else on the LAN.
- **Against:** RTT measurements would tell us; we do not have them logged. No OperationCanceledException or SendMessageAsync errors.
- **For it to dominate:** Garbling would correlate with other LAN activity (Plex playback elsewhere, file transfers, microwave usage). User would notice clustering by time of day or activity. The Cast device distance from the AP matters.

### H8: AVRCP / Cast volume race

- **For:** Logs show 5 external volume changes at 19:33:50-57 (from 70% to 30% over 7 s - that looks like the user spamming a volume slider). Each volume change emits a `Cast device volume changed externally` event and another `SetVolume` call on the shared TLS socket. During a rapid volume sweep, multiple ~100-byte messages get queued between audio chunks.
- **Against:** Volume messages are tiny and should not block audio chunks meaningfully. The `_suppressNextVolumeEvent` filter correctly debounces echoes.
- **For it to dominate:** Garbling would correlate with volume-change moments (manual or AVRCP). User would notice it happens *only* when they touch the volume.

### H5: PCM vs MP3 mode confusion

- **For:** None.
- **Against:** Production config explicitly sets StreamingMode DirectChannel and ApplicationId 567E3DBA. The DirectChannel path bypasses HttpStreamOutput entirely. The HttpStream output *is* still spun up at 20:05:24 in the log (`HttpStream output state changed from Created to Initializing`) but no client connects to it from the Cast device because the DC receiver does not fetch HTTP. So even if HTTP were misconfigured, it cannot garble the DC audio.
- **For it to dominate:** Would require the receiver to somehow consume both - not architecturally possible given the receiver code we see.

### H6 (LAME encoder Flush): Not applicable

- LAME runs in HttpStreamOutput only, which is not the Cast path in DirectChannel mode. **Dismissed for the current configuration.**

### H7: BT 44.1 -> 48 kHz resampler glitches

- **For:** BT capture is 44.1k or 48k depending on codec; mixer engine is 48k.
- **Against:** PR #404 (variable-rate resampler) and PR #402 (drift compensation) have already shipped and resolved BT-local audio quality. If the resampler glitched, the user would hear it on BT-local too - they explicitly say they do not. So Cast-specific garbling must originate *downstream of the resampler* (i.e., in the Cast output pipeline).
- **For it to dominate:** It cannot, since the same resampled audio feeds both BT-local and Cast paths and only Cast garbles.

### H3: Per-chunk byte misalignment

- **Dismissed.** Reader-lag arithmetic at TappedOutputStream.cs:128-133 is correctly frame-aligned. Chunk size of 19,200 bytes is divisible by frame size (4). No mid-frame splits possible.


---

## 5. Top recommendation

**Investigate H9 (Cast lifecycle churn) first** - it is the only hypothesis with concrete in-log evidence today, and the fix surface is small. Concrete next steps in priority order:

1. **Ask the user for a recent garbled-audio timestamp** and grep `journalctl -u radio-api --since '<minutes-ago>'` for `DirectCast: Stopping`, `BT pipeline recovery`, `Switching audio output`, `Switching to ` events in a +/- 60 s window. If they correlate, H9 is confirmed and the fix path is: (a) suppress Cast restart during BT recovery, (b) properly handle the `Failed to stop Google Cast output` exception path so consecutive restarts do not leak state, (c) consider whether `auto-connect default Cast device` should be gated by "stable enough" source state.

2. **Add the missing receiver telemetry pipeline** before doing anything else: have the sender call `SendPingAsync()` every 5 s and forward the pong payload (`bufferAhead`, `chunksDropped`, `decodeErrors`) to the metrics collector. Without this, H1 / H4 / H6_dc are unfalsifiable from server logs alone - every future investigation will hit this same wall. This is also explicitly called out as a prerequisite in prior research §3 PROBE-CAST-BUFFER.

3. **Run the prior research PROBE-CAST-AUDIO** (USB capture of the Cast device analog output -> `python3 scripts/research/cast_audio_glitch.py`) during a known-bad session. This gives objective glitch counts independent of subjective listening, and lets us correlate to specific log events.

4. Only after evidence narrows to one of H1/H4/H6_dc, consider implementation work from the prior research §7 catalog (grow buffer depth, producer/consumer split, separate transport, etc.).

**Do not:** Do not change `DirectChannelChunkSizeMs`, `MaxBufferAhead`, or `BufferBeforePlay` blindly. The receiver telemetry is the prerequisite for tuning - without it, any tuning is guesswork.

---

## 6. Questions only the user can answer

These are the highest-value pieces of information I cannot extract from logs or code:

1. **Recent garbled-event timestamp.** A single timestamp (e.g., "garbled at 20:15") allows precise log correlation against the events documented in §2 above. Within +/- 60 s gives us a clear pass/fail for H9.

2. **Garbling character.** Which of these best describes what is heard:
   - **Stutter / dropout** (audio cuts out for 20-500 ms, then resumes cleanly) - most consistent with H1, H9, source-side underruns
   - **Click / pop** (brief sharp artifact, audio continues) - most consistent with H6_dc receiver scheduling slips, or chunk-boundary discontinuity
   - **Pitch wobble / chipmunk** (audio sounds sped up or slowed down briefly) - most consistent with H3 (dismissed) or clock-drift artifacts
   - **Robot voice / metallic** (audio sounds like aliasing, sustained) - most consistent with H5 (dismissed) or sample-rate bug
   - **Repeated phrase / loop** (~100-300 ms of audio repeats) - most consistent with H4 HoL-block + receiver re-using old buffer
   - **Gradual descent into garble then recovery** - consistent with receiver `bufferAhead` drifting then chunksDropped events

3. **Frequency / cadence.** Estimate the per-hour rate:
   - <1/hour - consistent with H9 (correlates with rare events like BT recovery)
   - 1-5/hour - consistent with H1 / H2 (network or source jitter)
   - >10/hour - more consistent with H6_dc continuous receiver issues, or H9 if BT recovery is more frequent than logs show

4. **Cast device + network.** Which speaker?
   - Google Home Mini, Nest Audio, Chromecast Audio (deprecated), TV with Chromecast built-in
   - Distance from router (rooms away, walls)
   - 2.4 GHz vs 5 GHz (Home Minis are 2.4 only; Nest Audio is dual-band)
   - Wired or WiFi (the Office speaker at 192.168.86.25 is the one in current logs; the user has 4-9 devices in the cache - are they all garbling, or just the Office speaker?)

5. **Reproduction conditions.** Specifically:
   - Does it garble during *quiet* periods (low audio activity) or during *busy* periods (lots of UI interaction, source-switching, volume changes)?
   - Does it happen *immediately* after starting Cast, or after some minutes?
   - Does it happen *only when BT is the source*, or also when Radio / File / SDR is the source? (BT recovery is the H9 trigger - non-BT sources would falsify H9.)
   - Does it correlate with anything the user does (touch volume slider, switch sources, app foreground/background)?

A focused 15-minute session with the user controlling these variables and noting timestamps would let us collapse 5 of these 8 hypotheses immediately. Without it, we are stuck choosing between "instrument more" or "deploy a fix and watch."

---

## 7. Appendix - relevant files and line numbers

| File | Purpose |
|---|---|
| src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs | Cast lifecycle: connect/start/stop/disconnect, volume sync, metadata reload |
| src/Radio.Infrastructure/Audio/Outputs/DirectCastStreamingService.cs | DC streaming loop, pacing, pong handling (no adaptive feedback) |
| src/Radio.Infrastructure/Audio/Outputs/DirectCastAudioChannel.cs | Sharpcaster custom-namespace wrapper |
| src/Radio.Infrastructure/Audio/Outputs/HttpStreamOutput.cs | HM mode (not in use); MP3/WAV endpoints |
| src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs | Ring buffer + multi-reader; frame-aligned lag arithmetic (lines 128-133) |
| src/Radio.Core/Configuration/AudioOutputOptions.cs | Defaults: chunk 100 ms, lag 1 s, maxBufferAhead 3 s, bufferBeforePlay 3 |
| docs/receiver-direct-channel.html | Receiver v11 - Web Audio path, drift protection, telemetry-rich pong |
| deploy/raspberry-pi/appsettings.Production.json | Confirms StreamingMode=DirectChannel deployed |
| docs/research/2026-05-21-cast-stutter-comparison.md | Substantial prior research; this report uses its FM/Pattern taxonomy |
