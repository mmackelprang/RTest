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
**Status:** pending

Currently no UI shows fingerprint activity. User needs to see:
- Whether fingerprinting is active/idle
- Current identification status (listening, analyzing, matched, no match)
- Last match result + confidence
- Whether the service is healthy

**Approach ideas (research needed):**
- Small status indicator on the Now Playing panel (icon + tooltip)
- Or a subtle status chip/badge near the transport controls
- BackgroundIdentificationService already exposes `TrackIdentified` and `SongChanged` events
- Need to add a new SignalR event for fingerprint progress/status (capturing, querying, result)

### F.4 Volume Normalization Across Sources 🟢
**Status:** pending

File source needs soundbar volume raised significantly vs. radio which is much louder.

**Options (expert analysis needed):**
1. **Per-source gain offset** — Store a gain multiplier per AudioSourceType in config, apply in the mixer before master volume
2. **ReplayGain** — Read ReplayGain tags from files, normalize to -14 LUFS or similar target
3. **Automatic Gain Control (AGC)** — A new SoundModifier that maintains target RMS level with configurable attack/release
4. **Manual per-source volume** — UI slider per source in Settings, simplest approach
5. **Loudness normalization modifier** — EBU R128 / ITU-R BS.1770 loudness measurement + gain adjustment

Recommendation: Start with option 1 (per-source gain offset with UI control) — simplest, most predictable. Add option 3 later if needed.

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

---

## Phase D: Hardware Integrations (carry-forward) — Partial ✅

### D.1 Phonograph (USB Turntable) ⏸️
- [ ] Connect USB turntable to Ubuntu
- [ ] Verify USB audio device appears in device list
- [ ] Test VinylAudioSource playback

### D.3 Generic USB Audio ✅ (verified)
- [x] Test generic USB audio input — playback, volume, source switching, visualization all working
- [x] Stop/start bug found & fixed (PR #235) — endpoints now control active source, not just engine
- [ ] Verify hot-plug detection (deferred — requires physical unplug/replug)

---

## Known Issues (carry-forward)

| Issue | Status | Notes |
|-------|--------|-------|
| API SEGV every ~28 min | **New — Critical** | Native crash in MiniAudio/SoundFlow, correlates with fingerprint cycles |
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
