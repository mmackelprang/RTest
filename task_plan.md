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
**Status:** complete ✅

Implemented in PR #243:
- [x] Desktop shortcuts: Radio Console (Chrome kiosk), Exit Browser, Shutdown System
- [x] GNOME autostart with 5s delay, auto-login, screen blanking disabled
- [x] `setup-kiosk.sh` installer with service user switch (radio → mmack for PipeWire)
- [x] Viewport updated from 1920x576 to 1920x720 to match Ubuntu display
- [x] `radio-refresh-browser` helper using Chrome DevTools Protocol (Wayland-compatible)
- [x] `unclutter` for hiding idle mouse cursor

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
**Status:** complete ✅

Fixed in PRs #242 (fix: Bluetooth audio capture and album art lookup):
1. **Audio capture** — Replaced failing `arecord -D bt_capture` (ALSA bridge never configured) with `pw-record --target` for direct PipeWire capture from `bluez_input.*` nodes. Added `FindPipeWireBluetoothNodeAsync()` for dynamic node discovery with retry logic.
2. **Album art** — Stripped streaming title suffixes (e.g., "- 2010 Remaster", "(Deluxe Edition)") before MusicBrainz search. Now tries multiple recordings/releases (limit 5) instead of just the first match.
3. **Visualization** — Confirmed working when BT audio is playing via pw-record capture pipeline.
4. **Systemd** — Relaxed `ProtectHome`/`ProtectSystem` for PipeWire socket access. New `radio-pipewire-access.service` grants ACL on `/run/user/1000`. Added `After=/Wants=` ordering in `radio-api.service`.
5. **Code review fixes** — Wrapped `async void OnDeviceDisconnected` in top-level try/catch, removed stale XML doc, fixed systemd ordering race.

### F.12 UI/UX Audit & Polish 🟢
**Status:** complete ✅

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

### F.13 Bluetooth Audio Buffering Artifacts 🔴
**Status:** complete ✅

Four root causes identified and fixed:
1. **No buffer pre-fill** — BufferedSoundGenerator started empty, causing 88+ underruns on startup. Fixed: added `PreFillSilence(0.5f)` in both capture paths (matching SDR pattern).
2. **Pipe buffering** — `pw-record` used full stdout buffering when piped (~64KB bursts), starving the ring buffer. Fixed: wrapped with `stdbuf -o0` for unbuffered output.
3. **Byte alignment corruption** — `ReadAsync` could return odd byte counts, shifting S16_LE sample alignment. Fixed: added `pendingByte` tracking to maintain 2-byte alignment across reads.
4. **Duplicate generators race** — `RouteCaptureThroughMixerAsync()` could be called concurrently from DeviceConnected event and PlayCoreAsync, creating two generators in the mixer. Fixed: added `SemaphoreSlim _routeLock`.

### F.14 Audio Source Exclusivity & State Preservation 🔴
**Status:** complete ✅ (PR #245)

**Problem:** Multiple audio sources can play simultaneously — user observed overlapping audio from two inputs. Only one primary source should ever send audio to the output at a time (Audio Events are the exception — they are designed to duck/overlay and must ALWAYS be allowed).

**Requirements:**
1. **Single active source:** Switching to a new input source must stop/pause/mute the previous source before the new one begins producing audio.
2. **State preservation:** Sources must NOT be disposed on switch-away. Each source retains its state:
   - **Radio/SDR:** Keep current station, band, frequency
   - **File Player:** Keep playlist, current track position
   - **Bluetooth:** Keep connection state, AVRCP metadata
   - **Vinyl/Phono:** No state to preserve (pass-through)
   - **Generic USB:** No state to preserve (pass-through)
3. **Pause/Resume for controllable sources:**
   - **File Player:** Pause on switch-away, resume on switch-back
   - **Bluetooth:** Pause (AVRCP) on switch-away, play (AVRCP) on switch-back
   - **Radio, Vinyl, USB:** Cannot pause — just disconnect from mixer
4. **Queue/playlist auto-switch:** Loading a playlist is allowed without switching to File Player. But when a track is selected and begins playing, the system must auto-pause/mute the current source and switch to File Player.
5. **Audio Events (TTS, sound effects):** These are special-case overlay sources that duck the primary source volume. They must ALWAYS be allowed to play regardless of which primary source is active. Do not block or mute events.

**Investigation approach:**
- [ ] Audit `IAudioEngine.SwitchSourceAsync()` / `AudioManager` for proper old-source teardown
- [ ] Check if mixer removes old source generator before adding new one
- [ ] Verify `SoundFlowPlaybackService.PlayComponentAsync()` can't produce parallel generators
- [ ] Add guard: if source is already active on mixer, skip re-add
- [ ] Test: switch Radio → BT → File → Radio rapidly, confirm no overlap
- [ ] Document source lifecycle contract for future source implementations

### F.15 Audio Output Exclusivity 🔴
**Status:** complete ✅ (verified correct, no changes needed — PR #245)

**Problem:** When switching to Google Cast output, audio continued playing from the local soundbar simultaneously. Only the selected output should receive audio.

**Requirements:**
1. Switching output must mute/disconnect all other outputs before activating the new one
2. Local output must be muted when Cast is active (this was fixed on Pi previously — verify the fix applies to Ubuntu)
3. Cast disconnect must restore local output
4. HTTP stream output: decide if it should be exclusive or parallel (it's typically used for monitoring)

**Investigation approach:**
- [ ] Review `SetLocalOutputMuted(true)` in Cast connect flow — verify it's called on Ubuntu
- [ ] Check if `SwitchPlaybackDevice()` properly tears down old output
- [ ] Review the Pi fix for this same issue and confirm it's in the Ubuntu code path
- [ ] Test: Local → Cast → Local, confirm no dual-output at any point
- [ ] Document the output switching contract

### F.16 Ubuntu Boot Persistence & Shutdown UI 🟡
**Status:** complete ✅ (PR #245 — Shutdown/Restart buttons added to System page)

**Requirements:**
1. **Configuration survives reboot:** Verify `appsettings.Production.json`, PipeWire defaults (`pactl set-default-sink`), GNOME settings (auto-login, screen blanking), and kiosk autostart all persist.
2. **Web UI loads on startup:** Confirm `radio-kiosk-autostart.desktop` in `~/.config/autostart/` launches Chromium in kiosk mode after auto-login. Test by rebooting.
3. **Shutdown button on System page:** Add a new section or buttons to the System Stats tab:
   - **"Exit Web UI"** — Stops the `radio-web` service (or closes the browser kiosk)
   - **"Shutdown System"** — Calls `systemctl poweroff` (with confirmation dialog)
   - Both need confirmation dialogs ("Are you sure?")

**Implementation approach:**
- [ ] Add shutdown/restart API endpoints to Radio.API (or use direct SSH/systemd calls from Web)
- [ ] Add UI buttons with MudDialog confirmation to SystemConfigPage.razor
- [ ] Verify all kiosk setup from F.7 survives a full reboot cycle
- [ ] Test: reboot → auto-login → browser kiosk → Web UI loads → all settings intact

### F.17 Audio Level Analysis — Expert Research 🟡
**Status:** complete ✅ (research complete — PR #245 session)

**Problem:** Different audio sources produce very different volume levels:
- **Bluetooth audio is noticeably quiet** through local output
- **BUT the same Bluetooth audio is NOT quiet through Google Cast** — suggesting the issue is in the local playback path, not the BT capture itself
- Radio SDR has its own AGC toggle in the Web UI
- Vinyl/Phono levels vary depending on cartridge and preamp
- File player levels depend on mastering

**Key observation:** If BT audio is quiet locally but normal via Cast, the problem is likely in the local playback path AFTER the mixer (volume scaling, output device gain, PipeWire routing) rather than in the BT capture or mixer input.

**Research questions:**
1. Where in the audio pipeline does the volume difference originate? (capture → mixer → modifiers → output)
2. Is the per-source gain offset (F.4) the right approach, or is there a better solution?
3. Could PipeWire node volume/gain be different for local vs Cast output paths?
4. Is the Radio AGC toggle interacting with our gain system?
5. Should we implement proper broadcast-style loudness normalization (EBU R128 / ITU-R BS.1770)?
6. What do professional streaming audio systems do for multi-source level matching?

**Approach:** Deep research and analysis FIRST. Present findings and options to user before making any code changes. Examine the full signal chain from capture to output for each source type.

### F.19 Topbar Icon Size Increase (~20%) & UI Rebalance 🟡
**Status:** complete ✅ (PR #245)

**Problem:** Topbar icons are still a little small for comfortable touch screen usage on the kiosk (1920x720). They were enlarged in F.12 (Source/Output to Medium, Nav to Large) but need another ~20% bump for reliable finger-tap targeting.

**Requirements:**
1. Increase topbar source/output/nav icons by ~20% (may need custom CSS sizing beyond MudBlazor's Size enum)
2. Rebalance surrounding UI elements to accommodate larger icons — route group padding, topbar height, clock size, separators, spacing
3. Ensure the topbar still fits within 1920px width without overflow or wrapping
4. Maintain the "Command Surface" broadcast console aesthetic — larger icons should feel intentional, not cramped
5. Verify content area height adjusts if topbar height changes

**Approach:** Use frontend-design skill to holistically adjust the topbar proportions and ensure downstream layout (content-area, panels) remains balanced.

### F.18 Bluetooth Album Art: Prefer BT Metadata Over Online Lookup 🟡
**Status:** pending

**Problem:** Bluetooth connections used to provide album art directly via AVRCP metadata, but the current code falls back to MusicBrainz lookup based on song/artist/album name. BT-provided album art is almost always higher quality (the source app sends the actual cover art) compared to MusicBrainz which may return lower-res or mismatched art.

**Requirements:**
1. **First** attempt to retrieve album art from the BT AVRCP metadata (image property on the media player interface)
2. **Only if BT art is unavailable**, fall back to MusicBrainz/Cover Art Archive online lookup
3. Cache BT-provided art the same way online art is cached (in `data/albumart/`)

**Investigation approach:**
- [ ] Check BlueZ D-Bus `org.bluez.MediaPlayer1` for image/art property availability
- [ ] Review current album art flow in `BluetoothAudioSource` and `AlbumArtService`
- [ ] Determine if AVRCP art comes as a file path, URL, or binary blob
- [ ] Wire BT art into the existing album art pipeline with priority over online lookup

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
| BT play history recording | **Verified** | Working with pw-record capture (PR #242) |
| BT visualization data | **Verified** | Working with pw-record capture (PR #242) |
| JsonException deserializing RadioDeviceOptionsDto | New — Minor | Config `devices.radio` section doesn't match DTO shape |
| Multiple audio sources playing simultaneously | New — F.14 | Observed overlapping inputs; source switch doesn't mute previous |
| Cast + local output playing simultaneously | New — F.15 | Selecting Cast output didn't mute local soundbar on Ubuntu |

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
