# Findings

## .NET 10 Migration Research

### Release Status
- **.NET 10 GA**: November 11, 2025 (LTS through November 14, 2028)
- **.NET 8 EOL**: November 10, 2026 (~8 months remaining)
- **Tooling**: Visual Studio 2026 or Rider 2025.3+ required

### C# 14 Language Features (applicable to this codebase)

| Feature | Use Case in Project | Impact |
|---------|-------------------|--------|
| `field` keyword | Volume, balance, gain properties with validation | Eliminates explicit backing fields |
| Null-conditional assignment | Event handlers, nullable reference handling | Cleaner null guards |
| `params ReadOnlySpan<T>` | Audio pipeline hot-path methods | Zero-allocation params |
| Implicit Span conversions | Buffer method signatures (`float[]` ↔ `Span<float>`) | Simpler signatures |
| Extension members | Utility/extension classes → extension properties | Cleaner API surface |
| Partial constructors | Blazor code-behind patterns | Source gen support |

### Breaking Changes (NET 8 → NET 10)

| Change | Impact | Action |
|--------|--------|--------|
| Swashbuckle/OpenAPI v2.3 changes | Swagger at `/swagger` | Migrate to built-in OpenAPI |
| `System.Linq.Async` removed | If used anywhere | Replace with `System.Linq.AsyncEnumerable` |
| `WebHostBuilder` obsolete | Likely not used (modern hosting) | Verify |
| `WithOpenApi()` deprecated | If used in minimal APIs | Update |
| Cookie auth no longer redirects for API | Positive change for REST API | Verify |
| Container images → Ubuntu base | If Docker used | Update base images |

### Runtime Benefits (automatic, no code change)
- **GC DATAS**: Auto-tunes heap size based on app behavior (memory reduction)
- **Stack-allocated small arrays**: Zero GC for arrays that don't escape method
- **JIT cascading inlining**: Better devirtualization, 15-30% perf on AVX-512 hardware
- **ARM64 write barrier optimization**: Benefits Pi deployment

### NuGet Packages to Audit
- SoundFlow (audio engine, MiniAudio native interop)
- SharpCaster 3.0.0 (Google Cast)
- MudBlazor (Blazor UI framework)
- Tmds.DBus (BlueZ D-Bus)
- Serilog + sinks
- NAudio (Windows BT sender)

---

## Kiosk UI Blanking — Root Cause Analysis

### The Sleep Chain
```
idle-dimmer.js (30 min idle)
  → enterSleep('idle')
    → blazorRef.invokeMethodAsync('OnJsSleepRequested', true)
      → SystemApi.SetSleepAsync(true)
        → POST /api/system/sleep
          → SleepService.EnterSleepAsync()
            → primary.PauseAsync()   ← STOPS MUSIC
            → _audioManager.IsMuted = true  ← MUTES
```

### Contributing Factors
1. **Sleep system pauses audio BY DESIGN** — the sleep feature was built to stop everything, not just dim the screen
2. **Chrome timer throttling** — missing `--disable-background-timer-throttling` flag; CSS overlay may make Chrome think page is "hidden", throttling JS timers to 1/min, breaking SignalR keepalive
3. **DPMS not disabled** — `setup-kiosk.sh` disables GNOME screensaver but NOT X11 DPMS
4. **Blazor circuit timeout** — client retries only 30x (60s), but Chrome throttling means retries are slow; server kills connection after 30s without keepalive
5. **No systemd watchdog** — hung processes not detected

### Key Files
| File | Role |
|------|------|
| `src/Radio.Web/wwwroot/js/idle-dimmer.js` | Idle detection, dim/sleep timers |
| `src/Radio.Web/Components/Layout/MainLayout.razor` | `OnJsSleepRequested` JSInvokable |
| `src/Radio.API/Services/SleepService.cs` | `EnterSleepAsync` pauses + mutes |
| `deploy/debian-x64/kiosk/radio-console.desktop` | Chrome launch flags |
| `deploy/debian-x64/kiosk/setup-kiosk.sh` | GNOME settings, missing DPMS |
| `src/Radio.Web/Components/App.razor` | Blazor circuit reconnection config |
| `src/Radio.API/Program.cs` | SignalR server configuration |

---

## Audio Distortion — Previous Research Summary

### What We Know
- **151 distortion markers** across ~90 min BT playback — roughly one every 36s
- **Not clipping** — peaks at -6.5 to -11 dBFS, all `isClipping=False`
- **No app-level errors correlate** — no exceptions, no buffer drops/underruns/compensations
- **BT clock drift**: ~0.035s/min faster than ALSA clock (~2.1s/hour)
- **PipeWire-pulse overrun events** logged — MiniAudio not serviced fast enough
- **BT format**: S24LE 48kHz 2ch from PipeWire, converted to S16LE for the app's stream

### Ranked Root Causes
1. **MiniAudio/ALSA Output Xruns (HIGH)** — modifier chain must complete within quantum (~10.67ms)
2. **Lock Contention (HIGH)** — `AddSamples()` (PipeWire thread) and `GenerateAudio()` (MiniAudio thread) contend for `_bufferLock`
3. **.NET GC Pauses (MEDIUM)** — Gen2 can pause 10-50ms, exceeding quantum
4. **BT A2DP Transport Jitter (MEDIUM)** — irregular packet arrival
5. **DropOldest Buffer Overflow (LOWER)** — drift causes overflow after ~72 min

### Critical Unknown
**Actual audio waveform during distortion was NEVER captured.** Unknown whether it's:
- Repeated samples (underrun fill)
- Dropped samples (overflow/skip)
- Zero-insertion (silence gaps)
- Byte-shift corruption

### Existing Infrastructure
- `BufferedSoundGenerator` has extensive instrumentation (callback timing, lock contention, GC correlation)
- `FmAudioDropoutDiagnosticTests` simulate producer/consumer on independent clocks
- `AudioTestHelpers` generates diagnostic tones and has WAV writer
- `BtSender` sends known 200Hz/300Hz tone over BT
- **MISSING**: Capture points for actual waveform data, input/output comparison, automated distortion detection

---

## Comprehensive Review Items

| # | Category | Priority | Difficulty | Summary |
|---|----------|----------|------------|---------|
| 12 | Testing | High | Medium | `AudioManager` has zero unit tests |
| 13 | Testing | High | Medium | 5 API controllers have no tests |
| 14 | Architecture | High | Hard | 3 Blazor components >750 lines each |
| 26 | Architecture | Medium | Medium | `IAudioManager` is 40+ member god interface |
| 30 | Error Handling | Medium | Medium | All 14 Web API clients silently return null on errors |
| 34 | Architecture | Medium | Medium | `AudioSourceState`/`AudioOutputState` duplicate 7 values |
| 36 | State Mgmt | Medium | Hard | Audio state scattered across components, no centralized store |
