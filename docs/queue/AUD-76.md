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
