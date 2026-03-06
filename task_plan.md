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
**Status:** Research complete

**Problem:** When the phone leaves and re-enters Bluetooth range, it doesn't automatically reconnect to the radio. The user must manually connect from the phone's BT settings. Cars handle this seamlessly by actively polling for known devices.

#### Current Infrastructure (from code analysis)

**What exists:**
- `IBluetoothService` has `DeviceConnected`/`DeviceDisconnected` events, `AcceptConnectionAsync()`, discovery, pair/unpair
- `LinuxBluetoothService` (1,660 lines) talks to BlueZ via Tmds.DBus — manages IAdapter1, IDevice1, IMediaPlayer1, IMediaTransport1
- `BluetoothAutoSwitchService` already auto-switches audio source on `DeviceConnected` event
- `BluetoothPreferences.TrustedDevices` list exists but is **unused** — perfect hook for auto-reconnect target list
- `BluetoothOptions` has `AutoAcceptConnections`, `AutoSwitchOnConnect`, `EnableOnStartup` — but no reconnection options

**Key gap:** `IBluetoothService` exposes `DisconnectAsync()` but **NOT `ConnectAsync()`**. The D-Bus `IDevice1.ConnectAsync()` method is available in `BluezInterfaces.cs` but unreachable from the service contract.

**What happens on disconnect today:**
1. `WatchDevicePropertiesAsync` detects `Connected=false`
2. Fires `DeviceDisconnected` event
3. Audio capture stops, source goes to Ready state
4. System waits passively — no reconnection attempt

#### Recommended Approach: D-Bus Device1.Connect() with Exponential Backoff

**Option 2 (D-Bus direct) is the clear winner** — cleanest integration, no subprocess overhead, consistent with existing code patterns.

**Implementation plan:**

1. **Add `ConnectAsync(string address)` to `IBluetoothService`**
   - Linux: call `IDevice1.ConnectAsync()` via D-Bus (already available in BluezInterfaces.cs)
   - Windows: `BluetoothClient` connect or stub (Windows BT is secondary platform)

2. **Add reconnection config to `BluetoothOptions`:**
   ```csharp
   public bool AutoReconnect { get; set; } = true;
   public int ReconnectBaseDelayMs { get; set; } = 5000;    // 5s initial
   public int ReconnectMaxDelayMs { get; set; } = 60000;    // 60s cap
   public int MaxReconnectAttempts { get; set; } = 0;       // 0 = infinite
   ```

3. **Add reconnection logic to `LinuxBluetoothService`:**
   - On `DeviceDisconnected`: if device is in `TrustedDevices` (or `PairedDevices` if `AutoReconnect=true`), start background reconnection loop
   - Exponential backoff: 5s → 10s → 20s → 40s → 60s (capped)
   - Follow existing `PhoneCallClient` retry pattern (already in codebase)
   - Cancel reconnection if: user explicitly disconnects, device is unpaired, or new device connects

4. **Distinguish user-initiated vs unexpected disconnect:**
   - Add `_userInitiatedDisconnect` flag set by `DisconnectAsync()`
   - Only auto-reconnect on unexpected disconnects (range loss, interference)
   - Reset flag on next manual connect

5. **Wire into existing `BluetoothAutoSwitchService`:**
   - Reconnection succeeds → `DeviceConnected` fires → auto-switch already handles source activation
   - No new service needed; reconnection loop lives in `LinuxBluetoothService` itself

#### Edge Cases

| Scenario | Handling |
|----------|----------|
| User intentionally disconnects | `_userInitiatedDisconnect` flag — skip reconnection |
| Phone connected to car instead | `ConnectAsync()` will fail (device busy) — backoff continues, eventually succeeds when phone leaves car |
| Multiple paired devices | Reconnect only the last-connected device (`BluetoothPreferences.LastConnectedDevice`) |
| BT adapter powered off | Cancel all reconnection tasks on adapter state change |
| App restart while phone in range | `StartAsync()` checks `LastConnectedDevice`, attempts connect once after adapter warmup |

#### Metrics to Add

- `bluetooth.reconnect_attempts` (counter, tags: device, result=success/fail)
- `bluetooth.reconnect_backoff_seconds` (gauge, current backoff delay)

#### Files to Modify

| File | Change |
|------|--------|
| `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs` | Add `ConnectAsync(string address)` |
| `src/Radio.Core/Configuration/BluetoothOptions.cs` | Add reconnection options |
| `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` | Implement ConnectAsync + reconnection loop |
| `src/Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs` | Stub ConnectAsync |
| `appsettings.json` | Add default reconnection config |
| Tests: `BluetoothAutoSwitchServiceTests.cs`, new `BluetoothReconnectionTests.cs` | Unit tests |

#### Estimated Effort

Small-medium. Core change is ~100-150 lines in LinuxBluetoothService (connect method + reconnection loop + cancellation). Interface change is 1 line. Config is 4 properties. Most complexity is in edge-case handling.

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
**Status:** Research complete

**Problem:** The app has two independent config systems:
1. `.NET IConfiguration` pipeline — reads from `appsettings.json` at startup, feeds `IOptions<T>`
2. `IConfigurationManager` (SQLite store) — UI config pages read/write here at runtime

When the UI saves a config change (e.g. `UseShazamForAllSources` toggle), it writes to SQLite but `IOptions<T>` still holds the stale `appsettings.json` value. This has caused:
- BT fingerprinting not running despite user enabling the toggle
- Likely other settings silently ignoring UI changes until restart

#### Current Architecture (from code analysis)

**IOptions<T> side (20+ options classes):**
- Bound in `AudioServiceExtensions.AddSoundFlowAudio()` via `services.Configure<T>(configuration.GetSection(...))`
- All 20+ options classes in `src/Radio.Core/Configuration/` (AudioEngineOptions, BluetoothOptions, FingerprintingOptions, etc.)
- Secret resolution via `SecretResolvingPostConfigureOptions<T>` post-configure hook (startup only)
- Consumed by singletons: AudioSourceFactory, BackgroundIdentificationService, SoundFlowAudioEngine, etc.
- **Frozen at startup** — `IOptions<T>.Value` never changes; `IOptionsMonitor<T>.CurrentValue` only changes if underlying `IConfiguration` source changes

**Config Store side (SQLite/JSON):**
- `RadioConfigurationManager` → `IConfigurationStoreFactory` → `SqliteConfigurationStore` or `JsonConfigurationStore`
- SQLite table per store: `Config_<storeId>` with `Key TEXT, Value TEXT, Description TEXT, LastModified TEXT`
- All values stored as strings (JSON-serialized for complex types)
- Keys use section prefix: `"fingerprinting:useShazamForAllSources"`, `"audio:defaultSource"`, etc.
- `PreferencesPersistenceService` (hosted service) saves preferences to store every 30s
- `ConfigurationController` has 14 endpoints — UI saves via `POST /api/configuration/{section}`

**Existing workarounds (2 known):**
1. `AudioSourceFactory.SyncFingerprintingOptionsFromStore()` — mutates `IOptions<FingerprintingOptions>.Value` directly (fragile, sync-over-async)
2. `DeviceOptionsResolver` — reads store, returns merged DeviceOptions instance (better pattern but manual per-class)

**Data flow of a UI config change:**
```
UI toggle → ConfigurationApiService.UpdateConfigurationAsync()
         → POST /api/configuration/fingerprinting { "useShazamForAllSources": true }
         → ConfigurationController.UpdateConfigurationSection()
         → IConfigurationManager.SetValueAsync("sqlite", "fingerprinting:useShazamForAllSources", "true")
         → SQLite write ✓
         → IOptions<FingerprintingOptions>.Value.UseShazamForAllSources = false ← STALE
```

#### Option Analysis

**Option 1: Drop SQLite, use JSON config only**
- `IOptionsMonitor<T>` auto-reloads when JSON file changes (`reloadOnChange: true`)
- UI saves would write JSON files directly → change tokens fire → IOptionsMonitor updates
- **Pros:** Simplest, leverages built-in .NET infra, eliminates dual-store entirely
- **Cons:** Loses SQLite benefits (atomic writes, concurrent access, backup/restore infra, secrets table). JSON file writes aren't atomic (power loss = corrupt config). Existing backup/export/import system built around SQLite.
- **Verdict:** Too disruptive — would need to rewrite backup, secrets, and config management infra

**Option 2: Bridge SQLite → IConfiguration (custom IConfigurationProvider)** ⭐ RECOMMENDED
- Implement `SqliteConfigurationProvider : ConfigurationProvider` + `SqliteConfigurationSource : IConfigurationSource`
- Register in `ConfigurationBuilder` before options binding
- On `SetValueAsync()`: write to SQLite AND call `provider.Set()` + `OnReload()` → triggers `IOptionsMonitor<T>` change tokens
- **Pros:** Most correct .NET pattern. All existing `IOptions<T>` consumers automatically get updates. No workarounds needed. Secret resolution can hook into change tokens too.
- **Cons:** Moderate implementation effort. Need to map SQLite flat keys to IConfiguration hierarchy (already using `section:key` format, which is IConfiguration's native format).
- **Verdict:** Best long-term solution. The key format in SQLite (`fingerprinting:useShazamForAllSources`) already matches IConfiguration's colon-delimited path convention.

**Option 3: Drop IOptions<T>, use IConfigurationManager everywhere**
- Replace all `IOptions<T>` / `IOptionsMonitor<T>` injections with store reads
- **Pros:** Single source of truth
- **Cons:** Loses type-safe binding, compile-time checking, and the entire ASP.NET Core options pattern. Every consumer needs async store access. Massive refactor across 50+ files.
- **Verdict:** Too invasive, loses too many .NET conventions

**Option 4: Hybrid — save to both stores**
- `ConfigurationController.UpdateConfigurationSection()` writes to SQLite AND updates a JSON file → `IOptionsMonitor` picks up changes
- **Pros:** Minimal code change (just add JSON write in controller)
- **Cons:** Two sources of truth that can diverge. JSON file is a shadow copy. Restart loads from appsettings.json (not the shadow JSON). Fragile.
- **Verdict:** Band-aid, not a real fix

#### Recommended Implementation: Option 2

**Phase 1: Core bridge (eliminates the problem)**
1. Create `SqliteConfigurationProvider` extending `ConfigurationProvider`
   - `Load()`: read all entries from SQLite, populate `Data` dictionary
   - `Set(string key, string? value)`: write to SQLite + update `Data` + call `OnReload()`
2. Create `SqliteConfigurationSource` implementing `IConfigurationSource`
3. Register in `Program.cs`: `builder.Configuration.AddSqliteConfigStore(connectionString)`
4. Ensure it's added AFTER `appsettings.json` so SQLite values override JSON defaults

**Phase 2: Cleanup workarounds**
1. Remove `AudioSourceFactory.SyncFingerprintingOptionsFromStore()` — no longer needed
2. Remove `DeviceOptionsResolver` — `IOptionsMonitor<DeviceOptions>` works directly
3. Switch any `IOptions<T>` to `IOptionsMonitor<T>` where runtime updates are needed

**Phase 3: Bidirectional sync (optional)**
1. When `ConfigurationController.UpdateConfigurationSection()` writes to store, the bridge provider automatically notifies `IOptionsMonitor`
2. When `appsettings.json` changes on disk (deploy), the standard JSON provider fires, but SQLite overrides take precedence (later in chain)

#### Key Files to Modify

| File | Change |
|------|--------|
| New: `SqliteConfigurationProvider.cs` | Custom IConfigurationProvider backed by SQLite |
| New: `SqliteConfigurationSource.cs` | IConfigurationSource factory |
| New: `ConfigurationBuilderExtensions.cs` | `AddSqliteConfigStore()` extension method |
| `src/Radio.API/Program.cs` | Register SQLite config source |
| `src/Radio.Infrastructure/Configuration/Stores/SqliteConfigurationStore.cs` | Add change notification support |
| `src/Radio.Infrastructure/Audio/Services/AudioSourceFactory.cs` | Remove SyncFingerprintingOptionsFromStore workaround |
| `src/Radio.Infrastructure/Configuration/DeviceOptionsResolver.cs` | Remove (replaced by IOptionsMonitor) |
| `src/Radio.API/Controllers/ConfigurationController.cs` | SetValueAsync now triggers IOptionsMonitor via bridge |
| Tests: new `SqliteConfigurationProviderTests.cs` | Verify bridge behavior |

#### Estimated Effort

Medium. Core bridge is ~150-200 lines (provider + source + extension). Cleanup removes more code than it adds. Main risk is ensuring key format compatibility between SQLite store keys and IConfiguration paths (should be 1:1 since both use colon-delimited format).

#### Migration Notes

- Existing SQLite data is preserved — the bridge reads from the same `Config_sqlite` table
- `appsettings.json` remains the source of defaults for new installs
- SQLite values override JSON values (later in configuration chain wins)
- No data migration needed — key format already matches

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
