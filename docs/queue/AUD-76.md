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

⚠ **M3, open until measured on the box: adjacent-channel aliasing.** At 240 kS/s a station 200 kHz away aliases into the measured window, attenuated only by the RTL2832U's decimation filter, so a strong station may cast "shadow" peaks at ±0.2 MHz. PR 2's tap snaps to the strongest peak within ±0.4 MHz, which absorbs it for tuning; the map's appearance is what the box measurement decides. *(Measured after deploy, below.)*

### PR 1 — box UAT evidence (Builder, 2026-09-30, deployed `6c33ec3`)

Recorded by the PR 1 Builder after the deploy; copied here by the PR 2 Builder.

- **Timer while the radio plays:** the 120 s evaluation logged `skipped, reason: radio-playing`.
- **Live scan (radio playing):** 101 channels in **21.0 s**; returned to **92.3** with RDS **WKRR**; volume **0.3** and muted **unchanged**; the `dropping IQ batch` count was **31 → 31** (no new drops).
- **Idle scan (FilePlayer active, dongle idle):** 101 channels in **18.9 s**, agreeing with the live scan within ~1–2 dB. Strongest: **100.1 (−28.9)**, **101.1 (−30.3)**, then 98.5, 99.5, 98.7, 97.7, 105.1.
- **Presets against the map:** 92.3, 105.1, 97.7 (stored as 97.745), 105.5 and 106.9 are local peaks **5–21 dB** above their neighbours. 91.5 is above the floor but not always a peak.
- **Switching to Radio mid idle sweep:** `POST /api/sources {Radio}` returned 200 in **0.75 s**; gate claim at 20:51:53.916; sweep `cancelled (radio-claimed)` at 20:51:53.980; the radio played 92.3, signal 100, RDS WKRR; the previous map was kept.
- **M3 aliasing, measured:** channels 0.2 MHz from a strong station read ~20–30 dB below it but up to ~9 dB above the floor — visible as shoulders on the map, and absorbed for tuning by the ±0.4 MHz snap and its neighbour rule.
- **Sleep-triggered sweep:** unit-tested only; not exercised on the box.
- **Found:** after a sweep `/api/radio/state` reports `gain: 28` with `autoGain: true` (it was 0 before). Believed cosmetic — `rtlsdr_get_tuner_gain` returning the last manual value — but **not measured**. Filed as [`AUD-90`](AUD-90.md).

## PR 2 — the BAND view, touch-to-tune, Fall/VU removal (Builder, 2026-09-30)

**What shipped** (branch `feat/aud-76-band-view`):

- **Picker** — `VisualizerPanel.razor`: Wave / Spectrum / **BAND** / Ring / Phase; BAND sits where Fall was. VU and Fall are gone from the enum, the picker, the hub switches and `visualizer.js` (`drawVUMeter`, `drawMeter`, `updateDynamicScaling`, `drawSpectrogram`, their state). The level hub stream itself stays — `GainControlPopover` uses it. Default mode **Spectrum**. Saved preference `Spectrogram` → BAND; `VUMeter`, unparseable or numeric → Spectrum (`ParseSavedMode`).
- **Data** — BAND subscribes to no hub stream. It reads `GET /api/radio/bandmap`, `/api/radio/state` (null when radio is not the active source → no station marker) and the presets, on a one-shot timer re-armed after each refresh: **1 s while sweeping, 30 s otherwise**, on an injectable `TimeProvider` (`Clock` parameter). Leaving BAND or disposing stops it. Presets are re-read only on the idle cadence.
- **Display** — `visualizer.drawBandMap`: levels normalised in C# (`FmBandMath.NormalizeLevels`: 10th percentile → 0, max → 1, over at least 10 dB so a noise-only map is not stretched into fake stations), filled trace plus a bar per channel, the station in `--source-radio`, preset ticks with labels in two lanes (a label that would overlap in both is skipped), colours read from the design tokens. The MHz strip (88 … 108) is positioned by the same fraction the canvas plots. Text — *No scan yet*, *scanned 3 h ago*, *Scanning… 12 s* (falls back to %), tune/scan feedback — and the **Scan** button (≥ 48 px, disabled while sweeping, shows the API's 409/503 reason) are markup over the canvas.
- **Touch** — a `pointerup` on the canvas reports x / width to `[JSInvokable] OnBandTap`. `FmBandMath.ResolveTapTarget`: the strongest **peak** within ±0.4 MHz — a channel ≥ both neighbours **and** ≥ the map's median + 6 dB — else the nearest odd-tenth channel (87.9–107.9, clamped); no map → nearest channel. The neighbour rule also means an M3 alias shadow never beats its station. Then: radio not active → `SwitchSourceAsync("Radio")` **before** `SetFrequency`; radio on another band → `SetBand("FM")` first. Feedback *Tuning 99.5 FM*.

**Pre-merge review** (hostile, session model): no HIGH. Fixed:
- **M1** — a saved BAND preference that loaded after the first render left tap-to-tune unregistered, and the default mode's hub stream subscribed. `StartBandAsync` now registers the handler, and a late preference moves the subscription.
- **M2** — with the API down mid-sweep, BAND polled every second and logged Error for each one. A failed map read now drops polling to the idle cadence, and BAND's state and preset reads log at Debug.
- **M3** — a tap during a seek scan was overridden a moment later. The scan is now stopped first.
- **M4** — "`/api/radio/state` null = radio not active" was false (null on any failure), and a transient failure re-selected an already-playing radio. The new `ReadStateAsync` separates 400 from everything else; a tap switches source only on 400, and on an unreadable state it does nothing and says *"The radio API is not reachable"*.
- **Low** — dropped the redundant `SetBand("FM")`, which restarted the stream twice; `SetFrequency` already selects the band. Also: discard stale overlapping refreshes; clear messages at their 4 s lifetime; never register the tap handler outside BAND; fixed the misplaced `<summary>`.
- **Tests** — added four bUnit tests and one `FmBandMath` test (the alias shadow with the station *outside* the window). Each new test was mutation-checked: removing its behaviour fails it.
- **Not covered by a test** — M1's ordering. It would need a delayed-response seam in `RoutedApiHandler`.

**Polish pass** (session model): no HIGH. Fixed:
- A tap inside the 64 px status/Scan bar fell through to the canvas and retuned the radio; `visualizer.js` now ignores taps in the bar.
- The Scan button now matches `.bt-scan-button` (outlined, faded when disabled), but is 48 px tall.
- The countdown no longer pulses to 30% opacity.
- The empty-state subtitle uses `--text-medium`.
- Only the message is a live region.
- The canvas has tap styling and an accessible name.
- The station caret no longer overprints a lane-2 label.

Declined:
- House-style units (`5m ago`, `12s`): the spec quotes *"scanned 3 h ago"* and *"Scanning… 12 s"*.
- `BAND` casing: the owner's name; it renders uppercase like the other tabs.

Recorded, not changed: the 64 px reserve duplicated in CSS and JS; long API errors truncate at one line; errors render uppercase; no keyboard tuning path; no way to cancel a sweep from BAND.

**Rendered before merge** — the branch's `Radio.Web`, run on the dev host against the box's API (`ApiBaseUrl` from an environment variable), Playwright at 1920×720 against the real `fm.json`:
- The map draws.
- The MHz labels sit within 0.01 px of the canvas gridlines.
- Scan is 96×48.
- The status text sits inside the 64 px bar, clear of the label lanes.
- The console shows no errors.
- The initial HTML contains no visualizer markup: `Routes` is `prerender: false`, so nothing runs twice.

**Agent UAT on the box** (Builder, 2026-09-30). The branch build `9ee77af` was deployed and verified on both services (kiosk live). The run used Playwright at 1920×720 against `http://radio:5002` in a separate browser. The owner's state was recorded first: SDR radio 92.3 FM (WKRR), volume 0.3, muted, output Soundbar, visualizer preference `Spectrum`.
- **Picker:** Wave / Spectrum / BAND / Ring / Phase. VU and Fall are gone.
- **Map:** BAND draws the stored `fm.json` with the status *"scanned 29 min ago"*. The 92.3 station line and caret show, preset labels sit in two lanes, and the WB preset is excluded.
- **Scan, radio playing:**
  - The countdown ran *Scanning… 25 s* → *1 s*, with Scan disabled throughout.
  - The live sweep took 101 channels in 20.8 s, `result: completed`.
  - It returned to **92.3 WKRR** with volume 0.3 and muted unchanged.
  - The status then read *"scanned just now"*.
  - Two live scans ran (a scripted tap landed on the Scan button), and both returned cleanly.
  - Strongest: 101.1 (−29.2), 100.1 (−29.7), 99.5, 98.5, 97.7, 105.1, 104.1, 101.5 — the same stations as PR 1's sweeps.
- **Tap near a peak:** a tap at 100.0 MHz showed *"Tuning 100.1 FM"*, and `/api/radio/state` moved to **100100000**. The message cleared after its lifetime.
- **Tap from another source:**
  - Switched to File Player (`/api/radio/state` → 400).
  - Reloaded the page; BAND opened from the *saved* preference, which is M1's path.
  - Tapped 92.3: the source became **Radio, Playing, 92.3 WKRR**, still muted at 0.3.
  - No station marker shows while File Player is active.
- **Taps inside the status/Scan bar:** taps at 96.0, 99.5, 103.0 and 92.3 MHz (y +10, +30, +60, +63 px) left the frequency unchanged. A control tap at +70 px tuned (*"Tuning 92.3 FM"*).
- **Console:** no errors.
- **Box logs and state:**
  - `dropping IQ batch` went 31 → 36. All five drops came at 21:19:43–45, the post-deploy start-up; there were none during the scans or tunes.
  - `NRestarts` 0 / 0.
  - Visualizer preference restored to `Spectrum`, and the owner's state is as recorded.
- **Seen, PR 1 side, not fixed here:** a live sweep can log `No audio samples captured after 15020ms` (radio-api, WARN, journald) as it ends — the sweep-only mute starves the capture check. Once in two live sweeps.
- **`AUD-90` reproduced:** after the live sweep the radio panel's AGC readout shows **28.0 dB** with AUTO on. It went back to 0 after a source switch.

**Owner checks left:** touch feel on the panel (a finger, not a mouse: snap, the dead band at the top, accidental taps), and by ear: a requested live scan is silent and the station returns cleanly.
