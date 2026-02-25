# Task Plan: Radio Console — Ongoing Development

## Goal
Continue feature development, hardware integration testing, and bug fixes for the Radio Console project on Ubuntu x64 and Raspberry Pi targets.

## Current Phase
Phase F — New feature work and bug fixes across 12 work items.

---

## Completed (prior sessions)

All prior phases are **COMPLETE**. See git history and previous plan for details:
- Phases 0–11.3, A–E: Full system build, Cast streaming, architecture cleanup, radio scan fix, hidden devices
- Cast latency configurable from UI (PR #233)
- Queue add-to-queue reliability fix (PR #234)

---

## Phase F: Stability, UX & Features

### F.1 Investigate API SEGV Crash (Critical) 🔴
**Status:** complete ✅

Root cause: MiniAudio callback racing with SoundFlow dispose during fingerprint tap reader creation. Fixed by adding dispose guards and ensuring ring buffer readers don't outlive the engine. Service running with 0 crashes after fix deployed (PR #237).

### F.1b Fingerprint Capture Pipeline Fixes 🟡
**Status:** complete ✅

Three bugs found and fixed in the fingerprinting pipeline:
1. **Ring buffer capture bug** — `SoundFlowAudioTap.CaptureAsync()` used sync `Read()` which filled 99% of buffer with silence in milliseconds. Fixed: use `ReadAsync()` + skip zero-only chunks.
2. **Audio normalization** — Added peak normalization for quiet audio (<-6dB) before fingerprinting, since tap captures post-volume audio.
3. **Multi-duration fallback** — For live sources, retry AcoustID with common song durations (180-300s) if initial lookup fails.

Note: Vinyl fingerprinting still doesn't match AcoustID due to analog audio differences. Panako research spike **complete** — see `.research/panako/PANAKO-RESEARCH.md`. Recommendation: adopt Panako as secondary local fingerprint engine for analog sources (vinyl, FM radio). Estimated 5-9 days to integrate.

### F.2 Log Noise Reduction 🟡
**Status:** complete ✅

Three major noise sources need attention:

| Source | Rate | Fix |
|--------|------|-----|
| ALSA/JACK/PulseAudio stderr spam | ~9,360 lines/hour | Create `~/.asoundrc` to disable unused plugins, or `ALSA_CARD` env var |
| Fingerprint "no match" cycle | ~480 lines/hour | Reduce to DBG for radio/BT sources (live radio rarely matches AcoustID) |
| DirectCast chunk logging | ~36,000 lines/hour when casting | Reduce per-chunk logging to DBG |

Also fix:
- [x] `TaskCanceledException` in VisualizerPanel logged as ERR → silently caught (expected on navigation)
- [x] `Broadcast NowPlayingChanged` periodic logs → reduced to DBG
- [x] Album art URL changed `null → ...` on every page init → reduced to DBG
- [x] DirectCast per-chunk diagnostics → reduced to DBG
- [x] DirectCast silence transition logging → reduced to DBG

### F.3 Fingerprint Status UI 🟢
**Status:** complete ✅

Added fingerprint status badge on NowPlayingPanel with detail panel showing event log. Full stack: Core model (FingerprintStatusSnapshot), Infrastructure state tracking in BackgroundIdentificationService (phase transitions, event log aggregation, rolling rate counters), API endpoint + SignalR broadcast, Web badge + detail panel. Code review fixes: deep-copy snapshot records, ComputeRate edge case, extracted DTO mapper. Source name now shows actual source (RTL-SDR, FilePlayer, etc.) instead of "SoundFlow Output". No-match periods show as separate rows after a matched song. 12 unit tests. PRs #239, #240.

### F.4 Volume Normalization Across Sources 🟢
**Status:** complete ✅

Implemented in two phases:
1. **Per-source gain offsets** (PR #241) — Manual gain multiplier per AudioSourceType stored in SQLite, applied via `SoundFlowPlaybackService.SetGainOffset()`. UI sliders in Settings page and NowPlayingPanel popover with fine-adjustment arrows (±0.05).
2. **Auto RMS learning** (PR #241) — `SourceLevelLearningService` background service polls `IVisualizerService.GetLevelData().MonoRms` every 3s, computes EMA per source, auto-applies gain to normalize all sources to -18 dBFS target. Hybrid auto/manual mode: user slider changes switch to manual, "Reset to Auto" reverts. Clamp boundary protection prevents feedback loop at gain limits (0.1x–2.0x). Learned RMS persists to SQLite across restarts.

### F.5 Waveform Visualizer: Draw From Origin 🟢
**Status:** complete ✅

Current waveform draws point-to-point lines. Change to:
- Draw vertical lines from y=0 (origin) to sample level
- Positive samples (above 0): one color (e.g., accent-primary / cyan)
- Negative samples (below 0): different color (e.g., signal-amber / warm)
- Apply to both L and R channels

**File:** `src/Radio.Web/wwwroot/js/visualizer.js` lines 290-390

### F.6 FM Stereo via RTL-SDR 🟢
**Status:** complete ✅ (PR #237)

Current WFM demod outputs mono (duplicated to L+R). The 240 kHz demod rate already preserves the stereo subcarrier — the information is there but discarded.

**Implementation plan:**
1. New class `StereoFmDecoder` in `RTLSDRCore/DSP/`:
   - 19 kHz bandpass filter for pilot detection
   - PLL or frequency doubler for 38 kHz carrier recovery
   - L+R extraction (15 kHz LPF on composite)
   - L-R extraction (multiply by 38 kHz carrier, 15 kHz LPF)
   - Matrix decode: L = (L+R) + (L-R), R = (L+R) - (L-R)
   - Two independent de-emphasis filters
2. Modify `RadioReceiver.ProcessSamples()` for stereo WFM output
3. Remove mono-to-stereo duplication in `SDRRadioAudioSource.OnAudioDataAvailable()`
4. Update `AudioFormat` to 2 channels for WFM

Existing DSP primitives (LowPassFilter, DeEmphasisFilter, AudioDecimator) are reusable. ~200-300 lines new code.

### F.7 Ubuntu Kiosk Setup 🟢
**Status:** pending

Set up via SSH MCP on Ubuntu (`mmack@radio`):
- [ ] Desktop shortcut/icon to launch browser in kiosk mode (`chromium --kiosk http://localhost:5002`)
- [ ] System menu entries: "Exit Browser" (kill chromium), "Shutdown System" (sudo shutdown)
- [ ] Touchscreen is already installed

### F.8 Now Playing Panel: Larger Album Art 🟢
**Status:** complete ✅

Current: 180x180px album art centered with title/artist above.

Target: Album art nearly panel-width. Song/Album/Artist metadata overlaid on art with semi-transparent background for readability.

**File:** `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` lines 10-46

### F.9 Queue Auto-Advance + Shuffle/Repeat Indicators 🟡
**Status:** partial ✅ (indicators done, auto-advance improved)

Two issues:
1. **Auto-advance investigation:** ✅ Root cause analyzed: `MonitorPlaybackAsync` detects EOF via position tracking (`_position >= _duration`) or `IsPlaying()` fallback. Added diagnostic logging for both paths and wrapped `NextAsync` call in try/catch to prevent silent failures during auto-advance. The logic is structurally correct — will test on hardware to confirm.
2. **Shuffle/Repeat visual state unclear:** ✅ Active state now has cyan tinted background + border. Repeat-One mode shows dedicated RepeatOne icon.

### F.10 Radio Management UI Fixes 🟡
**Status:** partial ✅ (AGC + step size + band labels + preset save done, scan behavior pending)

Five issues:
1. **Step size not changeable** — ✅ Fixed: Clickable step label cycles through per-band AllowedStepSizes (PR #235)
2. **Scan behavior wrong** — Backend scan logic is correct (continuous scan, 2s dwell on signal, wrap around, auto-stop). UI needs visual indicator for signal pause state. Pending.
3. **Preset save button broken** — ✅ Fixed: Added proper error handling with ISnackbar feedback. Silent `catch { }` was swallowing errors. Now logs errors and shows toast notification on success/failure.
4. **AGC toggle text alignment** — ✅ Fixed: `.rcp-sdr-controls .mud-switch { align-items: center; }`
5. **Band labels overflow** — ✅ Fixed: Use short type codes (AM, FM, SW, AIR, WB, VHF) with full name as tooltip (PR #235)

**File:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor`

### F.11 Bluetooth Stability Investigation 🔴
**Status:** pending

Bluetooth connects to `Grandpas Radio` from phone, but playback is unreliable:
1. **Album art missing** — Song title & artist usually arrive via AVRCP, but album art rarely comes through. Investigate: is the art URL provided by AVRCP metadata? Does the proxy endpoint work? Is the Web UI requesting it?
2. **Audio often missing** — After connecting and starting Spotify playback, audio and visualization frequently don't appear. Sometimes works, sometimes doesn't. Investigate: is A2DP stream being received? Is `BluetoothAudioSource` activating? Is the audio pipeline routing correctly? Check logs for errors during BT playback attempts.
3. **Visualization missing** — When audio doesn't play, visualization is also absent (expected if no audio). But verify visualization works when audio IS playing.

**Investigation approach:**
- [ ] Deploy, connect BT, play Spotify, capture full logs
- [ ] Check AVRCP metadata fields (title, artist, album, art URL)
- [ ] Check A2DP audio stream reception and routing
- [ ] Check `BluetoothAudioSource` state transitions
- [ ] Verify BT play history recording (fix committed, untested)
- [ ] Verify BT visualization data (fix committed, untested)

### F.12 UI/UX Audit & Polish 🟢
**Status:** pending

Comprehensive audit of all pages for consistency with the modern, clean touch-screen kiosk aesthetic. Fix layout overflows, color mismatches, and improve information density.

**Pages to audit:**

1. **Queue page** — Audit overall aesthetic consistency with Home page. The `LOAD` file browser dialog appears at bottom of screen — colors and button designs should match Home page palette. Improve file browser UX.

2. **Bluetooth page** — Audit all sections (discovery, paired devices, connected device, AVRCP controls) for UX consistency and potential improvements.

3. **Google Cast dialog** — The "Select Google Cast" dialog doesn't match the overall system UX. Audit for consistency and improvements.

4. **Devices page** — General audit for aesthetic consistency.

5. **History page** — Left panel has a horizontal scrollbar that shouldn't be there. Fix layout overflow.

6. **System page** — Left-hand selections use a scrollbar where there appears to be enough space to fit all content without scrolling. `Secrets` sub-page: `Save Secrets` / `Clear All` buttons run off the page despite sufficient real estate. Secret text boxes use entire screen width unnecessarily.

7. **Configuration pages** — Several pages run past the left border. Too many configuration types across the top tabs — find a better way to present this list. Audit all sub-pages for layout overflow.

8. **System > Store Management** — Messy layout when user clicks `Refresh`. Improve the refresh/loading state UX.

**Approach:** Examine each page in the browser, identify issues, fix styling/layout while maintaining existing functionality. Carry forward the "Command Surface" dark-mode broadcast console aesthetic with CSS variables already defined.

---

## Phase D: Hardware Integrations (carry-forward) — Partial ✅

### D.1 Phonograph (USB Turntable) ✅
- [x] Connect USB turntable to Ubuntu
- [x] Verify USB audio device appears in device list
- [x] Test VinylAudioSource playback

### D.3 Generic USB Audio ✅ (verified)
- [x] Test generic USB audio input — playback, volume, source switching, visualization all working
- [x] Stop/start bug found & fixed (PR #235) — endpoints now control active source, not just engine
- [ ] Verify hot-plug detection (deferred — requires physical unplug/replug)

---

## Known Issues (carry-forward)

| Issue | Status | Notes |
|-------|--------|-------|
| API SEGV every ~28 min | **Resolved** | PR #237 — dispose guards + ring buffer reader lifetime fix |
| Queue add-to-queue reliability | **Resolved** | PR #234 — response parsing, timeouts, Blazor reconnection |
| Cast latency configurable | **Resolved** | PR #233 — DirectChannel buffer params in UI |
| Pong not received via SharpCaster | Deferred | CDP workaround reliable |
| Receiver double-counts messages | Cosmetic | 20/sec vs sender 10/sec after CDP reload |
| BT play history recording | Needs verification | Fix committed, untested on hardware |
| BT visualization data | Needs verification | Fix committed, untested on hardware |
| JsonException deserializing RadioDeviceOptionsDto | New — Minor | Config `devices.radio` section doesn't match DTO shape |

## Design Decisions
| Decision | Rationale |
|----------|-----------|
| Async DSP processing queue | USB read loop blocked by synchronous DSP caused 3.6% throughput deficit |
| DirectChannel for primary Cast | Raw PCM → Base64 → JSON eliminates MP3 encode/decode gaps |
| Per-source gain offset for volume normalization | Simplest, most predictable approach; AGC can be added later |
| Native stereo FM decoder | DSP primitives already exist in RTLSDRCore; no external library needed |
| Use appsettings.Production.json | Deploy script overwrites appsettings.json; Production file survives deploys |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| FM audio chronic underruns (3.6% silence) | Async DSP queue + startup reorder | **Fixed** (PR #230) |
| API SEGV every ~28 min | Under investigation | Correlates with fingerprint capture cycles |
