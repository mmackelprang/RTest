# Progress Log

## Session: 2026-03-09 (NuGet Extraction Planning)

### Research Completed
- [x] Analyzed all src/ and tests/ projects for extraction candidates
- [x] Reviewed dependency graphs, public API surfaces, test coverage
- [x] Identified 3 candidates: RTLSDRCore (ready), Radio.AudioAnalysis (needs work), Radio.Core (needs metadata)
- [x] Created 3-phase task plan
- [x] Also completed BT single-device exclusivity (PR #322) and task plan status update

### Planning Files
- [x] `task_plan.md` — 3 phases for NuGet extraction
- [x] `findings.md` — Candidate analysis summary
- [x] `progress.md` — This file

---

## Session: 2026-03-06 (Phase 3 — Root Cause + BT Transport Fix)

### BT Transport Dropout Investigation & Fix
- [x] Merged PR #305 (frame alignment fix)
- [x] Deployed and ran 10x60s baseline captures (Intel AX201): 9/10 perfect correlation
- [x] Investigated PipeWire/BT transport layer distortion — user hearing pops despite correct software processing
- [x] Identified root cause: Intel AX201 combo WiFi+BT chip coexistence interference (WiFi `Invalid misc` counter: 961,752)
- [x] Applied quick fixes (CPU governor=performance, WiFi PM=off) — didn't help (coexistence is RF-level)
- [x] Switched to TP-Link UB500 USB BT adapter — dramatic improvement: 6/10 runs with ZERO silence gaps
- [x] Upgraded PipeWire 1.0.5 → 1.0.7 via upstream PPA
- [x] Made all settings permanent: systemd service, NetworkManager dispatcher, udev rule
- [x] Disabled Intel AX201 BT permanently via udev rule
- [x] Updated deployment docs (DEPLOYMENT.md, setup.sh) with audio quality tuning section
- [x] Added deploy config files: `radio-performance.service`, `99-wifi-power-save-off`, `99-disable-intel-bt.rules`
- [x] Restarted radio-web service (was dead after deployment)

### Files Modified/Created
- `deploy/DEPLOYMENT.md` — added Audio Quality Tuning section, updated hardware table
- `deploy/debian-x64/setup.sh` — added audio quality tuning step (CPU governor, WiFi PM, Intel BT disable)
- `deploy/common/radio-performance.service` — new systemd service
- `deploy/common/99-wifi-power-save-off` — new NetworkManager dispatcher script
- `deploy/common/99-disable-intel-bt.rules` — new udev rule
- `findings.md`, `progress.md`

---

### Root Cause Identified & Fixed
- [x] Deployed Phase 2 capture infrastructure to Ubuntu
- [x] Captured 60s BT audio at 3 pipeline stages (generator-input, generator-output, post-modifiers)
- [x] Built waveform analysis test (`AnalyzeBtCapture_InputVsOutput`) with silence gap deep analysis, channel swap detection, windowed correlation
- [x] **ROOT CAUSE**: `PipeWireNativeStream.OnProcess` converts S16LE→float without stereo frame alignment. PipeWire BT transport delivers non-frame-aligned chunks during packet loss → odd sample count → L/R channel swap for ALL subsequent audio
- [x] **FIX**: Frame-align `sampleCount` in `PipeWireNativeStream.cs` + defense-in-depth in `BufferedSoundGenerator.AddSamples()`
- [x] Deployed fix to Ubuntu, captured 60s post-fix audio
- [x] Verified fix: Output→PostModifiers correlation went from 0.40 → 1.000000 (perfect)
- [x] Sample delta improved: -32,640 → +1,024

### Files Modified
- `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs` — frame alignment in OnProcess
- `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs` — defense-in-depth frame alignment in AddSamples
- `tests/Radio.Infrastructure.Tests/Audio/Diagnostics/WaveformComparisonTests.cs` — capture analysis test
- `findings.md`, `progress.md`, `task_plan.md`

---

## Session: 2026-03-06 (Phase 2 — Test Infrastructure)

### Implementation Complete (PR #304, merged)
- [x] `Radio.AudioAnalysis` shared library (8 files)
- [x] Capture infrastructure (4 files in `Audio/Diagnostics/`)
- [x] `BufferedSoundGenerator` diagnostic hooks
- [x] API capture endpoints
- [x] CI-safe distortion tests + capture session tests + waveform comparison tests
- [x] All 1416 tests passing
- [x] Code review: fixed `IsCapturing` race condition

---

## Session: 2026-03-06 (Planning)

### Research Completed
- [x] .NET 10 GA status confirmed (Nov 11, 2025), LTS through Nov 14, 2028
- [x] C# 14 features catalogued: `field` keyword, null-conditional assignment, `params ReadOnlySpan<T>`, implicit Span conversions, extension members
- [x] Breaking changes audited: Swashbuckle → built-in OpenAPI, `System.Linq.Async` removal, `WebHostBuilder` obsolete
- [x] Kiosk UI blanking root cause identified: `SleepService.EnterSleepAsync()` pauses audio + mutes by design after 30 min idle
- [x] Chrome timer throttling risk identified: missing `--disable-background-timer-throttling` flag
- [x] DPMS not disabled in kiosk setup (only GNOME screensaver)
- [x] Blazor circuit timeout insufficient for kiosk (30 retries = 60s, vs 10 min retention)
- [x] BtSender tool analyzed: Windows-only, 200Hz/300Hz diagnostic tone, WASAPI output, auto-reconnect
- [x] Audio distortion findings reviewed: 151 markers, no waveform capture, ranked root causes
- [x] Comprehensive review items #12, #13, #14, #26, #30, #34, #36 extracted and summarized
- [x] Created 6-phase task plan covering all priorities

### Planning Files
- [x] `task_plan.md` — 6 phases with detailed sub-tasks
- [x] `findings.md` — .NET 10, kiosk root cause, audio distortion, review items
- [x] `progress.md` — this file

---

## Previous Sessions (archived from earlier task plan)

### Session: 2026-03-05 (R1 + R4 research)
- R4 BT Auto-Reconnection — research complete, implemented in PR #299
- R1 Config Unification — research complete, implemented in PR #298

### Session: 2026-03-05 (R2 perf optimization)
- R2 Performance Deep Dive — 10 production files optimized (PR #296)
- GC pressure reduction, lock-free taps, render efficiency, background service optimization

### Session: 2026-03-05 (earlier)
- 151 distortion markers analyzed
- GC/instrumentation improvements
- BT state race fix, fingerprinting sync fix
