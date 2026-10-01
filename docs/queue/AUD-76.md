# `AUD-76` — a touchable FM band map replaces the "Fall" visualizer; "VU" goes

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2 (after 2f; not GA-blocking unless the owner promotes it).** Filed 2026-09-29. Design settled in conversation the same day.

## What the owner asked

> *"An option to show a touchable spectrogram for the radio band currently selected in the audio visualization panel and allow the user to 'rough' select the radio frequency by touching the area showing a signal."* Then: *"Replace the 'FALL' visualization with the radio band spectrum ('BAND'). This could be always on … the first few minutes it might have no data until we had scanned the airwaves, then periodically rescan. … the 'VU' visualization … it's not used enough, so I'm ok with removing that also."*

## What the hardware allows (measured in code 2026-09-29)

- **The live RF view is ±120 kHz, about one FM channel.** `RadioReceiver.CalculateRates` (`src/RTLSDRCore/RadioReceiver.cs:907-936`) captures at 240 kHz for FM and AM on purpose, to avoid IQ decimation. There is no FFT over IQ anywhere; the only RF measure is a wideband RMS per block (`:1031-1044`).
- **FM is 20.5 MHz wide; one RTL-SDR captures at most ~2.4 MHz.** A whole-band picture needs hopping, and on one dongle every hop moves the audio.
- **The box already drops IQ batches at 240 kHz** (`DSP processing queue full — dropping IQ batch`, `radio-api` file sink, 2026-09-28). A live wideband view is not free.
- **When the radio is not the active source, the dongle is idle:** `SDRRadioAudioSource.StopCoreAsync` shuts the receiver down (`SDRRadioAudioSource.cs:1015-1040`). That is what makes periodic rescans silent.
- AM/shortwave: no direct-sampling support, so FM is the target. **Measure AM reception on the box before promising anything for AM.**
- To build on: canvas visualizer (`visualizer.js`), 20 fps `/hubs/visualization`, the tuner-stepping logic in `ScanInternal` (`RadioReceiver.cs:388-490`), `SetFrequency` while streaming (`:279`, `:353`).

## The spec (shape A: a band map, not a live waterfall)

1. **Sweep.** Step the FM channels and record a per-channel signal level. US FM uses only odd tenths (87.9 … 107.9 MHz: 101 channels), so roughly 10–15 s a sweep. Per-channel level from an FFT of the IQ block centred on the channel (the current wideband RMS is too coarse). Store per band with a timestamp.
2. **When sweeps run.**
   - Radio **not** the active source (dongle idle) → sweep on a timer, silently. First sweep shortly after startup; the view shows *"No scan yet"* until then.
   - Radio **is** playing → only on the owner's **Scan** tap or **while the console is asleep**: mute, sweep with a *"Scanning… 12 s"* overlay, return to the original station.
   - The owner switching to Radio mid-sweep cancels it immediately and hands over the dongle. A sweep must never hold the device against the radio source.
3. **The BAND view** replaces **Fall** (the audio spectrogram) in the visualizer mode picker. It is **always available**, whatever the source. Signal across 87.5–108 MHz, markers for the current station and presets, and the map's age (*"scanned 3 h ago"*).
4. **Touch.** A tap snaps to the **strongest peak within ~±0.4 MHz**, else the nearest channel, and tunes there (switching to the radio source if it is not active). The knob fine-tunes.
5. **Labels:** presets only in the first cut; RDS names cannot be decoded during a fast sweep.
6. **Remove VU.** The visualizer's default mode becomes **Spectrum** (VU was the default, `VisualizerPanel.razor:138`).
7. Two PRs: (a) sweep service + stored map + API; (b) BAND view, touch-to-tune, Fall/VU removal.

Shape B (a live ±1.2 MHz waterfall at 2.4 MS/s) is deliberately deferred until A proves useful and the dropped-batch warnings are understood.

## Verification

Sweep: unit tests over channel stepping, cancellation on radio activation, and the "radio playing → no timer sweep" rule, with a fake device. On the box: a map whose peaks match known local stations; switching to Radio mid-sweep plays within a second; no new dropped-batch warnings during normal listening. Touch: a tap on a peak tunes to that station.

## PR 1 — the channel sweep, stored map and API (Builder, 2026-09-30)

**What shipped** (branch `feat/aud-76-band-sweep`):

- **Measurement** — `src/RTLSDRCore/Sweep/`: `ChannelPowerMeter` (Hann-windowed 2048-point FFT, mean bin power over 8–80 kHz either side of the tuned centre; the DC spike is excluded; the value is a *relative* dB figure, not calibrated), `FmChannelPlan` (87.9 … 107.9 MHz, 101 channels), `BandSweeper` (tune → one discarded settling read → one measured read, per channel; 16384 samples at 240 kS/s).
- **Two sweep paths.** *Idle* — the radio does not hold the dongle: `BandMapService` opens its own `RtlSdrDevice` (`DeviceSweepTuner`, fixed 28 dB manual gain) and sweeps silently. *Live* — the receiver is running: `RadioReceiver.SweepChannels` hops the running device with a sweep-only mute (the user's mute is never written), an IQ tap, and the same gain; `FrequencyChanged` never fires and `CurrentFrequency` never moves; the `finally` retunes to `_currentFrequencyHz` (so a user tune mid-sweep wins) and restores the gain mode.
- **Device ownership** — `SdrDeviceGate`: `SDRRadioAudioSource.StartupAsync` claims the dongle *before* the receiver opens it, cancelling any idle sweep and waiting (bounded, 3 s, then `Error` log) until the sweep has closed its device. Startup failures close the device before the gate is released; dispose shuts the receiver down first.
- **When sweeps run** (`BandMapService`): timer — first evaluation 120 s after start, then every 60 min when the map is stale. Dongle idle → idle sweep. Radio holds it → the timer sweeps **only while `ISleepService.IsSleeping`** (audio parked) and records `skipped / radio-playing` otherwise; `IsSleepScreenVisible` (the screensaver, audio still playing) is deliberately *not* asleep. A sleep-triggered sweep is cancelled at the next channel once `IsSleeping` reads false, and `ResumeAsync` cancels it too. `POST /api/radio/bandmap/scan` sweeps on request — idle path when free, live path (muted ~15 s, returns to the station) when the radio is playing. One sweep at a time. User tune/band/seek-scan/gain changes cancel a live sweep.
- **Storage / API** — `<Database:RootPath>/bandmap/fm.json` (`/opt/radio-console/data/bandmap/fm.json` on the box), written temp-then-rename. `GET /api/radio/bandmap` (`channels: []` and `scannedAtUtc: null` before the first sweep — PR 2 shows *"No scan yet"*), `POST /api/radio/bandmap/scan` (202 started / already running; 409/503 when impossible). Options: `BandMap` section of `appsettings.json`.

**Pre-merge review** (hostile, session model): no HIGH. Fixed: M1 (two gate tests raced the real 3 s timeout → never-advanced `FakeTimeProvider`), M2 (gate could be released with the device still open after a failed start or a dispose), M4 (the live path could measure a block read across a hop → the first block after each hop is dropped before the settling read), M5 (a sleep sweep was not reliably cancelled on wake → the run checks `IsSleeping` after every channel), and L1–L5, L7, L8, L10–L12. Not fixed: M3 — see below; L6 (the gate is one flag, correct while there is one radio source — documented), L9 (signal-strength gauges keep firing during a live sweep), L13 (log volume — about six Information lines per idle sweep).

⚠ **M3, open until measured on the box: adjacent-channel aliasing.** At 240 kS/s a station 200 kHz away aliases into the measured window, attenuated only by the RTL2832U's decimation filter, so a strong station may cast "shadow" peaks at ±0.2 MHz. PR 2's tap snaps to the strongest peak within ±0.4 MHz, which absorbs it for tuning; the map's appearance is what the box measurement decides.
