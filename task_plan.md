# Task Plan: Audio Pipeline Stability & Fingerprinting

## Goal
Stabilize audio pipeline (reduce GC pressure, fix distortion), enable Shazam-based fingerprinting for all sources, and fix config/state bugs preventing correct operation.

## Current Phase
Phase 5 — Complete. Ready for new task.

## Phases

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | Distortion Analysis & Research | complete | Extracted 151 markers, correlated with logs, ranked root causes |
| 2 | GC Pressure Reduction | complete | 6 changes: lock-free taps, pre-alloc buffers, SustainedLowLatency, Span conversions |
| 3 | Instrumentation & Logging Fixes | complete | Callback timing, limiter stats, throttled per-miss logging (broke journald feedback loop) |
| 4 | Shazam for All Sources | complete | UI toggle, FingerprintingOptions pass-through, config store sync, BT state race fix |
| 5 | Play History Fixes | complete | Off-by-one dedup, stale album art leak across BT song changes |

## Key Findings

1. **62-70% of missed deadlines correlate with GC activity** — Gen0 collections during audio callback
2. **journald feedback loop** — per-miss LogWarning overwhelmed journald (91.7% CPU) → memory pressure → forced Gen2 GC → more misses
3. **SSH activity on Ubuntu correlates with distortion** — log tailing and DB queries compete with audio pipeline on N100
4. **Config store desync** — UI writes to SQLite, IOptions reads from appsettings.json. Multiple bugs caused by this.
5. **BT source state race** — PlaybackStatusChanged fires during StartAsync() before State=Ready, so Playing transition skipped
6. **Source state "Ready" anomaly** (finding #6 from analysis) — explained by the race condition in finding #5

## Research Items

### R3: Reduce BT Audio Pre-fill Buffer Delay

**Priority:** High — UX regression, noticeable lag when skipping songs
**Status:** Complete (PR #295)

**Resolution:** The pre-fill buffer (0.5s) was fine — the real problem was stale audio sitting in the buffer during song transitions. Added `ClearAudioBuffer()` calls in `OnMetadataChanged`, `NextAsync`, and `PreviousAsync` to flush stale samples immediately on song change. New audio starts within ~100-200ms (PipeWire delivery latency) instead of draining 0.8-2.0s of old audio.

### R4: BT Active Reconnection (Auto-Connect on Proximity)

**Priority:** Medium — UX improvement, phone should auto-connect like a car stereo
**Status:** Research needed

**Problem:** When the phone leaves and re-enters Bluetooth range, it doesn't automatically reconnect to the radio. The user must manually connect from the phone's BT settings. Cars handle this seamlessly by actively polling for known devices.

**Current state:**
- Phone (Pixel 8 Pro) is paired, bonded, and trusted in BlueZ
- BlueZ accepts incoming connections automatically (pairing agent auto-accept is enabled)
- Phone does NOT auto-connect to `Grandpas Radio` when in range (unlike car stereos)
- The radio doesn't actively try to connect to known devices either
- Need the radio to actively initiate reconnection so the phone connects seamlessly

**Approach options:**
1. **BlueZ-side active reconnect** — Periodically run `bluetoothctl connect <address>` for trusted/paired devices that aren't connected. Simple, proven approach used by many Linux car-head-unit projects.
2. **D-Bus `Device1.Connect()`** — Same as option 1 but via D-Bus API instead of shelling out. Cleaner integration with existing `LinuxBluetoothService`.
3. **BlueZ `AutoConnect` property** — Some BlueZ versions support `AutoConnect=true` on `Device1`. May already work but needs testing.
4. **Systemd timer + script** — Lightweight external approach, decoupled from the app. Less integrated but simpler.

**Investigation:**
1. Check if BlueZ `AutoConnect` property is available and what it does on our version
2. Test `bluetoothctl connect D4:3A:2C:64:87:9E` when phone is in range but disconnected — does it work?
3. Decide polling interval (every 15-30s seems standard for car systems)
4. Handle edge cases: phone intentionally disconnected by user, multiple paired devices, phone connected to another audio sink
5. Consider adding a config toggle (`AutoReconnect: true/false`) so user can disable

**Key files:**
- `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` — D-Bus BT management
- `src/Radio.Core/Configuration/BluetoothOptions.cs` — BT config options

### R2: Performance & Memory Deep Dive (API + Web)

**Priority:** High — Ubuntu N100 has limited resources; memory pressure causes GC-induced audio distortion
**Status:** Complete (PR pending)

**Goal:** Expert-level performance analysis of both Radio.API and Radio.Web projects. Identify memory allocation hotspots, GC pressure sources, unnecessary object retention, and CPU inefficiencies. Produce actionable optimization recommendations.

**Scope:**
1. **Radio.API (port 5000)** — Audio engine, BT service, SignalR hubs, REST controllers, fingerprinting, config stores, hosted services
2. **Radio.Web (port 5002)** — Blazor Server UI, SignalR client, MudBlazor components, API client services

**Analysis areas:**
- **Memory allocations** — Hot-path allocations (per-request, per-callback, per-frame), closure captures, LINQ in tight loops, string interpolation in log calls, unnecessary boxing
- **Object lifetime & retention** — Singleton services holding references longer than needed, event handler leaks, disposable resources not disposed, large object heap (LOH) pressure
- **GC pressure** — Gen0/Gen1 churn from short-lived allocations, Gen2/LOH from large buffers, finalizer queue pressure
- **SignalR efficiency** — Message serialization overhead, broadcast frequency (visualization at 20fps), connection management, hub method granularity
- **HTTP client usage** — HttpClient lifecycle, connection pooling, DNS refresh, response buffering
- **Blazor Server specifics** — Circuit memory (per-connection state), component render frequency, JS interop overhead, unnecessary re-renders, timer-driven updates
- **DI container** — Scoped vs singleton lifetime mismatches, transient services that should be pooled, heavy constructor injection chains
- **Async patterns** — Unnecessary async state machines, fire-and-forget without error handling, sync-over-async (.GetAwaiter().GetResult())
- **Caching** — Missing caches (repeated DB/API calls), oversized caches, cache invalidation gaps
- **Startup cost** — Service initialization order, lazy vs eager loading, cold-start allocations

**Approach:**
1. Static analysis — Read key files, identify anti-patterns, review DI registrations
2. Runtime profiling (if needed) — `dotnet-counters`, `dotnet-trace`, `dotnet-dump` on Ubuntu
3. Categorize findings by impact (High/Medium/Low) and effort
4. Produce prioritized optimization list

**Deliverable:** Findings in `findings.md`, prioritized implementation plan with phases

### R1: Unify Config Systems (SQLite vs appsettings.json)

**Priority:** High — has caused multiple bugs
**Status:** Research needed

**Problem:** The app has two independent config systems:
1. `.NET IConfiguration` pipeline — reads from `appsettings.json` at startup, feeds `IOptions<T>`
2. `IConfigurationManager` (SQLite store) — UI config pages read/write here at runtime

When the UI saves a config change (e.g. `UseShazamForAllSources` toggle), it writes to SQLite but `IOptions<T>` still holds the stale `appsettings.json` value. This has caused:
- BT fingerprinting not running despite user enabling the toggle
- Likely other settings silently ignoring UI changes until restart

**Options to research:**
1. **Drop SQLite, use JSON config only** — simpler, `IOptionsMonitor<T>` auto-reloads on file change. Downside: harder to manage via API/UI (must write JSON files).
2. **Bridge SQLite → IConfiguration** — implement `IConfigurationProvider` backed by SQLite. Config changes flow to `IOptionsMonitor<T>` via change tokens. Architectural but most correct.
3. **Drop IOptions<T>, use IConfigurationManager everywhere** — all config reads go through the store. Downside: lose type-safe options binding, need to convert from key-value strings.
4. **Hybrid: Save to both stores simultaneously** — API controller saves to SQLite AND updates `IConfiguration`/JSON. `IOptionsMonitor<T>` picks up changes. Fragile synchronization.

**Current workaround:** `AudioSourceFactory.SyncFingerprintingOptionsFromStore()` manually reads from SQLite and patches the `IOptions<T>` value on source creation. This is a band-aid.

## Key Decisions
| Decision | Rationale | Date |
|----------|-----------|------|
| Research first, instrument second | User wants to understand before fixing — previous attempts at blind fixes didn't resolve | 2026-03-05 |
| Throttle per-miss logging to 5s | High-freq logging caused journald feedback loop (91.7% CPU) | 2026-03-05 |
| Config unification research needed | SQLite vs appsettings.json duplication has caused multiple bugs — needs architectural decision | 2026-03-05 |
| Fix BT state race in InitializeAsync | PlaybackStatusChanged fires before State=Ready; check metadata after init completes | 2026-03-05 |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| CS0169 unused field `_lastReportTicks` | 1 | Removed unused field from LimiterModifier |
| CS0252 reference comparison on object | 1 | Cast `pbStatus` to `(string)` for value comparison |
| API 404 on source switch | 1 | Correct API is `POST /api/sources` with JSON body, not `POST /api/sources/switch/Radio` |
