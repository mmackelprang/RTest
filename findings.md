# Findings: Audio Distortion Investigation

## Data Summary

- **151 distortion markers** across ~90 minutes (14:05 - 15:42), roughly one every 36 seconds
- **All markers: isClipping=False** — peaks at -6.5 to -11 dBFS, not a clipping issue
- **BT gain = 1.9** (96% of events), masterVolume = 0.64 — moderate levels
- **Source state split**: 79 "Playing" / 76 "Ready" — source reports Ready half the time despite audio flowing
- **Multiple service restarts** in the window (our deploys): PIDs 339597, 344843, 349341, 351164
- **No app-level errors correlate** — no exceptions, no buffer drops/underruns/compensations in the dense distortion window

## Key System-Level Events

### PipeWire-Pulse Overruns (14:01:32)
```
mod.protocol-pulse: [Radio.API] overrun recover read:180480 avail:26368 max:15360 skip:22528
mod.protocol-pulse: [Radio.API] overrun recover read:249088 avail:25344 max:15360 skip:21504
```
MiniAudio output client had overrun — ALSA output buffer emptied because MiniAudio callback wasn't served fast enough. Only 2 events logged, but PipeWire-pulse may only log coalesced events, not every occurrence.

### BufferedSoundGenerator Stats (PID 344843, ~48 min session)
```
received=72,892,416  output=72,733,440  dropped=0  compensated=0
Gap: 158,976 samples = 1.66s accumulated in buffer
```
BT clock ~0.035s/min faster than ALSA clock (~2.1s/hour drift). Buffer grows monotonically. No drift compensation fires (compensation only triggers when buffer is BELOW 15% — i.e., draining — but ours is growing).

### BT Audio Format
```
Node: bluez_input.D4_3A_2C_64_87_9E.2
Active: S24LE, 48000Hz, 2ch
Our stream requests: S16LE, 48000Hz, 2ch
```
Sample rate matches (48kHz→48kHz). PipeWire converts S24LE→S16LE. This conversion is handled natively by PipeWire and shouldn't cause issues.

## Buffer Configuration
- maxBufferSeconds = 4.0 → maxBufferSamples = 384,000
- PreFill = 1.5s of silence (144,000 samples = 37.5% of buffer)
- DriftCompensationThreshold = 15% (57,600 samples) — only fires when buffer is LOW
- DriftCompensationTarget = 25% (96,000 samples)
- OverflowStrategy = DropOldest

## The received > output Problem

The buffer grows because BT clock > ALSA clock. Eventually (after ~72 min from pre-fill):
1. Buffer hits 100% capacity (384,000 samples)
2. `AddSamples()` triggers `DropOldest` — advances read pointer, drops oldest audio
3. Consumer suddenly reads audio that's discontinuous — **audible glitch**
4. Buffer briefly has space, fills again, drops again

**But**: In PID 344843 session, the buffer accumulated only 1.66s over 48 min. Starting from 1.5s pre-fill, total = 3.16s. Buffer capacity = 4s. It didn't overflow during the session. **Yet distortion was frequent throughout.**

## What We Don't Know (Gaps)

| Unknown | Why It Matters |
|---------|----------------|
| MiniAudio output callback timing | Are there scheduling delays causing ALSA xruns? |
| .NET GC pause frequency/duration | GC could stall the audio callback thread |
| Lock contention on `_bufferLock` | Both AddSamples (PipeWire thread) and GenerateAudio (MiniAudio thread) compete for this lock |
| PipeWire graph scheduling | Is the BT node delivering data in steady quanta or bursty? |
| BT A2DP packet loss | Wi-Fi coexistence? Codec negotiation changes? |
| Actual audio waveform during distortion | Repeated samples? Dropped samples? Corrupted data? |
| Whether MiniAudio is hitting ALSA xruns silently | MiniAudio may recover internally without logging |

## Possible Root Causes (Ranked by Likelihood)

### 1. MiniAudio/ALSA Output Xruns (HIGH)
The PipeWire-pulse overrun logs confirm the MiniAudio output stream isn't being served fast enough at least sometimes. MiniAudio's callback requests audio → SoundFlow calls `GenerateAudio()` on all components (modifiers, sources) → if ANY step is slow, the ALSA buffer underruns.

The audio pipeline runs: Sources → MasterMixer → Balance → Limiter → FingerprintTap → VisualizationTap → PlaybackDevice. Each modifier's `Process()` is called synchronously in the callback. If the chain takes longer than one quantum (~10.67ms at 512 samples), it's a missed deadline.

### 2. Lock Contention Between Producer and Consumer (HIGH)
`AddSamples()` and `GenerateAudio()` both take `_bufferLock`. If PipeWire delivers a burst of data while `GenerateAudio` is mid-read (or vice versa), one blocks the other. The PipeWire OnProcess callback runs on the PipeWire thread loop — blocking it stalls the entire PipeWire graph.

### 3. .NET GC Pauses (MEDIUM)
Gen2 garbage collection can pause all threads for 10-50ms. At 48kHz with 512-sample quantum (10.67ms period), even a 10ms pause causes a missed callback. The buffer should absorb this, but if GC coincides with the callback, the consumer misses a period.

### 4. BT A2DP Transport Jitter (MEDIUM)
BT A2DP delivers audio in packets (~128-512 samples). If packets arrive late (interference, scheduling), the BufferedSoundGenerator temporarily starves. The 1.5s pre-fill should absorb this, but sustained jitter could eat into the cushion.

### 5. DropOldest Buffer Overflow (LOWER — not yet overflowing)
With 2.1s/hour drift, overflow takes ~72 min from startup. Most sessions were shorter. But in longer sessions, this WILL become an issue — the buffer silently drops samples at the read position, causing a discontinuity.

## Source State "Ready" Anomaly

76 of 155 markers show source state = "Ready" (not "Playing"). This is suspicious — audio is clearly flowing (levels show -20dB signal), yet the source reports Ready. This could indicate a state machine bug where the source doesn't transition to Playing, or the state reporting is async and stale.
