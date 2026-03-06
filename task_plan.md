# Task Plan: Next Priorities — Kiosk Fix, Audio Debug, .NET 10, Architecture, Metrics

## Goal
Fix kiosk sleep-stops-music bug, build automated audio distortion debugging infrastructure, migrate to .NET 10 LTS + C# 14, address architecture/testing items from comprehensive review, and enhance the metrics dashboard with customizable hero cards and new metrics.

## Current Phase
Phase 3 — in_progress

## Phases

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | Kiosk Stability Fix | complete | PR #303 — decoupled screen blank from audio pause, Chrome flags, DPMS, SignalR timeouts |
| 2 | Audio Distortion — Automated Test Infrastructure | complete | PR #304 — Radio.AudioAnalysis lib, CaptureSession, DiagnosticCaptureService, 30+ tests, API endpoints verified on Ubuntu |
| 3 | Audio Distortion — Root Cause Investigation & Fix | in_progress | Using Phase 2 infra to identify and fix distortion |
| 4 | .NET 10 Migration + C# 14 Adoption | pending | TFM update, breaking changes, NuGet updates, language features |
| 5 | Core Architecture Refactoring | pending | #26 IAudioManager split, #34 enum consolidation, #14 component decomposition, #36 state store |
| 6 | Test Coverage + Web Error Handling | pending | #12 AudioManager tests, #13 missing controller tests, #30 Result<T> |
| 7 | Metrics — Customizable Hero Cards + CPU Usage | pending | Dismissable/addable hero cards (max 10), persisted selection, CPU usage % metric |

---

## Phase 1: Kiosk Stability Fix

**Root cause identified:** `idle-dimmer.js` calls `SleepService.EnterSleepAsync()` after 30 min idle, which pauses audio + mutes. This is by design but wrong for a radio.

### 1A. Decouple screen dim from audio pause
- `idle-dimmer.js`: idle-triggered sleep should ONLY do visual overlay (CSS black screen), NOT call `OnJsSleepRequested(true)`
- Reserve `OnJsSleepRequested(true)` for explicit user sleep button only
- Add new concept: "screen blank" (visual only) vs "deep sleep" (pauses audio)
- Files: `src/Radio.Web/wwwroot/js/idle-dimmer.js`, `src/Radio.Web/Components/Layout/MainLayout.razor`

### 1B. Chrome anti-throttling flags
- Add flags to prevent Chrome from throttling JS timers when page is "hidden" behind CSS overlay
- `--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`
- Files: `deploy/debian-x64/kiosk/radio-console.desktop`, `deploy/debian-x64/kiosk/radio-kiosk-autostart.desktop`

### 1C. Disable DPMS at X11 level
- Add `xset s off && xset -dpms && xset s noblank` to kiosk setup
- Files: `deploy/debian-x64/kiosk/setup-kiosk.sh`

### 1D. Increase SignalR timeouts for kiosk reliability
- Server: `ClientTimeoutInterval = 2min`, `KeepAliveInterval = 30s`
- Client: `maxRetries: 300` (10 min window, matching server `DisconnectedCircuitRetentionPeriod`)
- Files: `src/Radio.API/Program.cs`, `src/Radio.Web/Components/App.razor`

### 1E. Tests
- Unit test for sleep vs screen-blank behavior distinction
- Verify idle-dimmer does NOT trigger audio pause

---

## Phase 2: Audio Distortion — Automated Test Infrastructure

**Goal:** Build end-to-end automated test that sends known audio through BT pipeline, captures output, and compares — no human in the loop.

### 2A. BtSender lifecycle management
- Create `AudioDistortionTestRunner` tool/service that can:
  - Start BtSender programmatically (or use API to play a known WAV file)
  - Send a well-known diagnostic tone (200Hz L / 300Hz R, already in AudioTestHelpers)
  - Control duration and lifecycle
- Since BtSender is Windows-only, consider alternative: play a known WAV via `FilePlayerAudioSource` as a reference baseline first, then extend to BT path

### 2B. Audio capture at pipeline stages
- Add capture points to record raw audio at key stages:
  1. **Input**: samples arriving at `BufferedSoundGenerator.AddSamples()` (producer side)
  2. **Output**: samples leaving `BufferedSoundGenerator.GenerateAudio()` (consumer side)
  3. **Post-mixer**: samples after `SoundFlowMasterMixer`
  4. **Post-modifiers**: samples after modifier chain (final output)
- Each capture writes a WAV file with timestamps for offline analysis
- Capture triggered by API endpoint or CLI flag, bounded duration (e.g., 30s)

### 2C. Waveform comparison tool
- Compare captured input vs output WAV files:
  - Cross-correlation to detect time offset
  - Sample-by-sample diff to identify exact distortion type:
    - Repeated samples (buffer underrun fill)
    - Dropped samples (buffer overflow/skip)
    - Zero-insertion (silence gaps)
    - Byte-shift/corruption
    - Amplitude distortion (clipping, gain errors)
  - Frequency domain analysis (FFT of known tone, check for harmonic distortion)
- Output: distortion report with timestamps, type, severity

### 2D. Automated distortion detection test
- xUnit integration test that:
  1. Starts the audio engine with a known WAV file source
  2. Captures output for N seconds
  3. Compares input/output
  4. Fails if distortion exceeds threshold
- Can run in CI (no hardware needed for file-based test)
- BT variant requires hardware but follows same pattern

### 2E. FmAudioDropoutDiagnosticTests extensions
- Extend existing `FmAudioDropoutDiagnosticTests` with BT-specific scenarios:
  - BT clock drift simulation (0.035s/min faster)
  - Lock contention under high-frequency AddSamples calls
  - GC pause injection during callback

---

## Phase 3: Audio Distortion — Root Cause Investigation & Fix

**Goal:** Use Phase 2 infrastructure to identify the specific distortion pattern and fix it.

### 3A. Run automated tests, collect baseline
- File source baseline (no BT): should be clean
- BT source test (via BtSender or simulated): identify distortion pattern

### 3B. Analyze distortion type
- From capture comparison: is it repeated samples, dropped samples, zero-insertion, or corruption?
- This answers the critical unknown from findings.md

### 3C. Fix based on findings
- If **lock contention**: migrate to lock-free ring buffer (CAS-based) or `Channel<T>`
- If **GC pauses**: pin audio buffers, use `GC.TryStartNoGCRegion` around critical sections
- If **buffer overflow (DropOldest)**: implement drift compensation (adaptive resampling or periodic sample skip/insert)
- If **MiniAudio underrun**: increase quantum or pre-compute modifier results

### 3D. Verify fix with automated tests
- Re-run Phase 2 tests, confirm distortion eliminated
- Long-duration soak test (1+ hour)

---

## Phase 4: .NET 10 Migration + C# 14 Adoption

**.NET 10 released November 11, 2025 — LTS through November 14, 2028**

### 4A. Preparation
- Install .NET 10 SDK alongside .NET 8
- Audit NuGet dependencies: `dotnet list package --outdated`
- Check critical packages: SoundFlow, SharpCaster, MudBlazor, Tmds.DBus, Serilog
- Check `upgrade-assistant` tool applicability

### 4B. Project file updates
- Update all `.csproj` TFMs: `net8.0` → `net10.0`, `net8.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0`
- Update `global.json` SDK version
- Update NuGet packages to .NET 10-compatible versions
- Update CI: `.github/workflows/build.yml` to use .NET 10 SDK

### 4C. Breaking changes
- Replace Swashbuckle with built-in OpenAPI if applicable
- Remove `System.Linq.Async` if used (replaced by `System.Linq.AsyncEnumerable`)
- Fix any obsolete API warnings (`WebHostBuilder`, `WithOpenApi()`, etc.)
- Review cookie auth behavior for API endpoints

### 4D. Test & deploy
- `dotnet build --configuration Release` — 0 warnings
- `dotnet test --configuration Release` — all tests pass
- Deploy to Ubuntu, test audio pipeline, BT, PipeWire, Cast
- Deploy to Pi, verify ARM64 P/Invoke (libpw_helper.so, MiniAudio, fpcalc)

### 4E. C# 14 language features adoption
- `field` keyword: properties with validation/transformation (volume, balance, gain clamping)
- Null-conditional assignment: `handler?.Volume = newVolume`
- `params ReadOnlySpan<T>`: hot-path audio methods to reduce GC pressure
- Implicit Span conversions: audio buffer method signatures
- Extension members: evaluate for utility/extension classes
- Apply incrementally as files are touched

### 4F. Runtime benefits (automatic)
- GC DATAS (Dynamic Adaptation To Application Sizes) — auto-tunes heap, reduces memory
- Stack-allocated small arrays — reduces Gen0 pressure
- JIT improvements — better inlining, devirtualization
- ARM64 write barrier optimization (Pi deployment)

---

## Phase 5: Core Architecture Refactoring

### 5A. #26: IAudioManager interface segregation
- Split 40+ member `IAudioManager` into:
  - `IAudioSourceManager` — source switching, creation, caching
  - `IAudioMixerControl` — volume, balance, mute, gain
  - `IAudioManager` remains as facade implementing both (backward compat)
- Files: `src/Radio.Core/Interfaces/Audio/IAudioManager.cs`, `AudioManager.cs`, all consumers

### 5B. #34: Enum consolidation
- Extract shared states into `AudioComponentState` base enum
- `AudioSourceState` and `AudioOutputState` extend/compose with component-specific states
- Files: `src/Radio.Core/Models/Audio/AudioSourceState.cs`, `AudioOutputState.cs`

### 5C. #14: Blazor component decomposition
- `RadioControlPanel.razor` (1185 lines) → `RadioTunerDisplay`, `RadioPresetBank`, `RadioFrequencyDialog`
- `NowPlayingPanel.razor` (760 lines) → `TransportControls`, `GainPopover`, `FingerprintBadge`
- `MainLayout.razor` (790 lines) → `SourceRouting`, `OutputRouting`
- Target: <300 lines per component

### 5D. #36: Centralized audio state store
- Create `AudioStateStore` service (observable store pattern)
- Holds: active source, selected output, volume, mute, Cast state, panel visibility
- Components subscribe to state slices
- SignalR events update store → store notifies subscribers
- Replaces scattered state across MainLayout (8 fields), NowPlayingPanel, RadioControlPanel
- Files: new `src/Radio.Web/Services/AudioStateStore.cs`, update all consuming components

---

## Phase 6: Test Coverage + Web Error Handling

### 6A. #12: AudioManager unit tests
- Create `tests/Radio.Infrastructure.Tests/Audio/Services/AudioManagerTests.cs`
- Cover: source switching (including concurrent), volume/balance/mute, source gain offsets, cached source retrieval, error handling
- Use existing `CustomWebApplicationFactory` pattern

### 6B. #13: Missing controller tests
- `IntegrationsController` tests
- `NotificationsController` tests
- `PlaylistsController` tests (priority — user data)
- `RadioBandsController` tests
- `AudioDiagnosticsController` tests

### 6C. #30: Web API client error handling
- Create `Result<T>` wrapper with `Success`, `Error(message)`, `Disconnected` states
- Update all 14 API client services in `src/Radio.Web/Services/` to return `Result<T>`
- Update components to show error states with retry buttons (MudBlazor Snackbar)

---

## Phase 7: Metrics — Customizable Hero Cards + CPU Usage

### 7A. Customizable hero cards on metrics dashboard

**Current state:** Hero cards are auto-selected by matching metric keys against a hardcoded `_heroPatterns` array (max 6). No user customization.

**Goal:** Let users customize which metrics appear as hero cards:

- **Max 10 hero cards** in the existing row (horizontal scroll if needed on narrow viewports)
- **Dismiss any card**: Close icon (X) in the upper-right corner of each hero card
- **Add new cards**: When fewer than 10 hero cards are shown, allow promoting any metric from the category table below:
  - Option A: Add a `+` icon in each metric row in the table
  - Option B: Long-touch on a metric row to promote it
  - Recommend **Option A** (visible `+` icon) — more discoverable, works with mouse and touch
- **Persist selection** across runs via the configuration store (`ui.metrics.hero_cards` key in SQLite config)
- **Default hero cards**: On first load (no persisted selection), use the current `_heroPatterns` auto-selection as initial defaults

**Implementation:**
- `MetricsDashboardPage.razor`:
  - Replace `_heroPatterns` static array with a mutable `_heroCardKeys` list loaded from config
  - Add dismiss button (MudIconButton with Close icon) to each hero card
  - Add "promote to hero" button in each metric row (MudIconButton with Add icon, hidden when 10 cards already shown)
  - On add/remove: save updated list to config via `POST /api/configuration/ui` (existing config endpoint)
  - On page load: read persisted list from config; if empty, fall back to current pattern-match defaults
- `MetricsApiService.cs`: Add methods to load/save hero card preferences (or use existing `ConfigurationApiService`)
- Threshold coloring and sparklines continue to work unchanged — they key off the metric key, not the hero selection

**Files:**
- `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor` — hero card rendering, add/remove logic, config persistence
- Possibly `src/Radio.Web/Services/ApiClients/MetricsApiService.cs` or `ConfigurationApiService.cs` — preference load/save

### 7B. Add CPU Usage % metric

**Current state:** `SystemController.GetCachedCpuUsageAsync()` computes CPU usage % per-request for the `/api/system/stats` endpoint, but it is NOT recorded in the metrics pipeline. `SystemMonitorService` collects memory, disk, db size, and CPU temp — but not CPU usage.

**Goal:** Record `system.cpu_usage_percent` as a gauge metric so it appears on the metrics dashboard with history, sparkline, and threshold coloring.

**Implementation:**
- `SystemMonitorService.cs`: Add CPU usage % calculation (same approach as SystemController — measure process CPU over a short window) and call `_metricsCollector.Gauge("system.cpu_usage_percent", cpuPercent)` in the existing `CollectMetricsAsync()` method
- The metric will automatically:
  - Appear in the `System` category section on the dashboard
  - Match `_heroPatterns` `"cpu"` pattern (so it shows as a hero card by default)
  - Use the existing `cpu_usage` threshold (warn=80%, crit=95%) for coloring
  - Have proper formatting via the existing `"cpu" + "usage"` → percentage format rule

**Files:**
- `src/Radio.Infrastructure/Metrics/Services/SystemMonitorService.cs` — add CPU usage collection

---

## Key Decisions
| Decision | Rationale | Date |
|----------|-----------|------|
| Pending | | |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none yet) | | |
