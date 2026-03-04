# Comprehensive Architectural Review

> **Last updated:** 2026-03-04 — PR #273 (high) + PR #274 (medium) + PR #275 (medium)

## HIGH Priority

---

### 1. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Security
- **Location:** `src/Radio.API/Controllers/SecretsController.cs:46-67`
- **Issue:** The `GET /api/secrets/{section}?raw=true` endpoint returns unmasked plaintext secrets (API keys for Google TTS, Azure TTS, AcoustID) with no authentication or authorization. Anyone on the network can retrieve all secrets.
- **Recommendation:** Remove the `?raw=true` option entirely, or gate it behind authentication. At minimum, add `[Authorize]` to this controller. Consider removing the raw endpoint and only allowing secret writes.

---

### 2. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Security
- **Location:** `src/Radio.API/Controllers/FilesController.cs:55-63, 130-237`
- **Issue:** The `absolutePath` query parameter on `GET /api/files` is passed directly to `Directory.Exists()`, `Directory.GetDirectories()`, and `Directory.GetFiles()` with zero path validation. An attacker can enumerate the entire filesystem (`/etc`, `/home`, `C:\Windows\System32`).
- **Recommendation:** Whitelist allowed base directories (e.g., the configured media root). Reject any `absolutePath` that does not start with an allowed prefix. Add `Path.GetFullPath()` + prefix check to prevent `..` traversal.

---

### 3. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Security
- **Location:** `src/Radio.API/Controllers/FilesController.cs:266-323`; `src/Radio.Infrastructure/Audio/Services/FileBrowser.cs:300-317`
- **Issue:** The `PlayFile` endpoint accepts an arbitrary file path from the POST body and passes it to `LoadFileAsync()`. The `GetFullPath` helper uses `Path.Combine` without `..` traversal protection. An attacker can play any audio file on the system or probe for file existence.
- **Recommendation:** Validate that resolved paths fall within the configured media root using `Path.GetFullPath()` + `StartsWith()` check. Reject paths containing `..` segments.

---

### 4. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Security
- **Location:** `src/Radio.Web/Components/Layout/MainLayout.razor:215`
- **Issue:** `eval()` is used to set a global JS variable: `await JSRuntime.InvokeVoidAsync("eval", $"window.radioApiBaseUrl = '{apiBaseUrl}'")`. Since `apiBaseUrl` comes from `IConfiguration` and the configuration API is unauthenticated, an attacker could inject JavaScript via a chained attack (write malicious config → trigger UI reload).
- **Recommendation:** Replace `eval()` with a safe JS interop call, e.g., `await JSRuntime.InvokeVoidAsync("Object.assign", DotNetObjectReference.Create(...))` or write a tiny JS function that accepts the URL as a parameter and sets the global.

---

### 5. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Error Handling
- **Location:** `src/Radio.Web/Components/Pages/RadioPage.razor` (8 instances: lines ~258, ~316, ~399, ~530, ~547, ~563, ~580, ~597)
- **Issue:** Eight separate `catch (Exception) { // Silently fail }` blocks swallow all exceptions including unexpected ones (NullReferenceException, InvalidOperationException, etc.) with no logging. Bugs in radio control logic will be invisible.
- **Recommendation:** At minimum, add `Logger.LogWarning(ex, "...")` to each catch block. Better: extract a shared error-handling helper that logs and optionally shows a Snackbar notification.

---

### 6. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Error Handling
- **Location:** `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:142, 436`; `src/Radio.Web/Components/Pages/MetricsDashboardPage.razor:218, 228, 254`
- **Issue:** Bare `catch { }` blocks (no exception type, no body) silently swallow all exceptions including critical ones. The Cast disconnect failure at line 142 can mask network issues; the metrics page catches at lines 218/228/254 hide data loading failures.
- **Recommendation:** Replace bare `catch { }` with typed catches (`catch (Exception ex)`) and add at minimum `_logger.LogDebug(ex, "...")`. For Cast disconnect, log at Warning level since it indicates a network state problem.

---

### 7. [PRIORITY: High] [DIFFICULTY: Easy] --- DONE (PR #273)
- **Category:** Dead Code
- **Location:** `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs:498-527` (TryRecoverPlaybackDevice)
- **Issue:** `TryRecoverPlaybackDevice()` re-attaches Balance, Limiter, and FingerprintTap modifiers but does NOT re-attach `_visualizationTap`. `SwitchPlaybackDevice()` (lines 594-645) correctly attaches all four. After a device recovery, visualization will silently stop working.
- **Recommendation:** Add the `_visualizationTap` re-attachment block to `TryRecoverPlaybackDevice()`, matching the pattern in `SwitchPlaybackDevice()`. Better: extract a shared `ReattachModifiers()` helper (see item #19).

---

### 8. [PRIORITY: High] [DIFFICULTY: Medium]
- **Category:** Security
- **Location:** `src/Radio.API/` (all 18 controllers); `src/Radio.API/Program.cs`
- **Issue:** No authentication or authorization is configured. All endpoints are publicly accessible to anyone on the network, including secrets retrieval, configuration writes, file browsing, audio control, and system administration. No rate limiting exists.
- **Recommendation:** Implement API key authentication (simplest for a single-user appliance) or JWT bearer tokens. Add `[Authorize]` to sensitive controllers (Secrets, Configuration, System, Files). Add rate limiting middleware for discovery/scan endpoints.

---

### 9. [PRIORITY: High] [DIFFICULTY: Medium] --- DONE (PR #273)
- **Category:** Duplication / Base Class
- **Location:** `src/Radio.Infrastructure/Audio/Sources/Primary/PrimaryAudioSourceBase.cs` and `src/Radio.Infrastructure/Audio/Sources/Events/EventAudioSourceBase.cs`
- **Issue:** ~13 methods/properties are duplicated nearly verbatim across both base classes: `Id`, `State`, `Volume`, `PlayAsync`, `StopAsync`, `DisposeAsync`, `InitializeAsync`, `OnStateChanged`, `OnPlaybackCompleted`, `ThrowIfDisposed`, etc. Any bug fix must be applied in two places. The `ThrowIfDisposed` implementations even use different patterns (`ObjectDisposedException.ThrowIf` vs manual throw).
- **Recommendation:** Extract a common `AudioSourceBase` abstract class containing shared state management, volume, disposal, and event patterns. `PrimaryAudioSourceBase` inherits from it and adds pause/resume/seek/capability methods. `EventAudioSourceBase` inherits directly with no additions.

---

### 10. [PRIORITY: High] [DIFFICULTY: Medium] --- DONE (PR #273)
- **Category:** Concurrency
- **Location:** `src/Radio.Infrastructure/Audio/Services/AudioManager.cs:319-328`
- **Issue:** `GetOrCreateSourceAsync` calls `SwitchSourceAsync` while holding `_createLock` (line 328), creating a lock ordering of `_createLock → _switchLock`. But the main code path at line 388 intentionally calls `SwitchSourceAsync` *outside* `_createLock` with a comment noting deadlock avoidance. If two threads hit the cache-after-lock path vs. a direct `SwitchSourceAsync`, the inconsistent ordering can deadlock.
- **Recommendation:** Move the `SwitchSourceAsync` call at line 328 outside the `_createLock` scope, matching the pattern at line 388. Store the cached source reference, release `_createLock`, then call `SwitchSourceAsync`.

---

### 11. [PRIORITY: High] [DIFFICULTY: Medium] --- DONE (PR #273)
- **Category:** Error Handling / Concurrency
- **Location:** Multiple files in `src/Radio.Infrastructure/` — 19+ instances of `_ =` fire-and-forget async in infrastructure code (see list below)
- **Issue:** Fire-and-forget async calls (`_ = SomeMethodAsync()`) silently lose exceptions. Key instances: `GoogleCastOutput.cs:118,125` (Cast volume/mute), `LinuxBluetoothService.cs:338,382,387,392` (BT device watching), `BluetoothAudioSource.cs:296,452,783` (audio capture), `SoundFlowPlaybackService.cs:693` (stop), `AudioPreferencePersistence.cs:53,274` (persist). A failed Cast volume set or BT capture acquisition will never be noticed.
- **Recommendation:** Create a `SafeFireAndForget(Task task, ILogger logger, string context)` extension method that logs exceptions. Replace all `_ = MethodAsync()` with `MethodAsync().SafeFireAndForget(_logger, "context")`. For critical operations (Cast, BT capture), consider awaiting with a timeout instead.

---

### 12. [PRIORITY: High] [DIFFICULTY: Medium]
- **Category:** Testing
- **Location:** `src/Radio.Infrastructure/Audio/Services/AudioManager.cs`
- **Issue:** `AudioManager` is the central orchestrator for source switching, volume control, gain management, and ducking coordination. It has no direct unit tests despite being the most critical service in the system. The lock ordering bug (item #10) would have been caught by concurrency tests.
- **Recommendation:** Create `tests/Radio.Infrastructure.Tests/Audio/Services/AudioManagerTests.cs` covering: source switching (including concurrent switches), volume/balance/mute operations, source gain offsets, cached source retrieval, and error handling during source creation.

---

### 13. [PRIORITY: High] [DIFFICULTY: Medium]
- **Category:** Testing
- **Location:** `src/Radio.API/Controllers/` — 5 controllers have no tests
- **Issue:** `IntegrationsController`, `NotificationsController`, `PlaylistsController`, `RadioBandsController`, and `AudioDiagnosticsController` have no corresponding test files. These endpoints handle hardware integration, user playlists, and system diagnostics.
- **Recommendation:** Create test files for each missing controller. Priority order: PlaylistsController (user data), IntegrationsController (hardware), then the others. Use the existing `CustomWebApplicationFactory` pattern from other controller tests.

---

### 14. [PRIORITY: High] [DIFFICULTY: Hard]
- **Category:** Architecture / Separation of Concerns
- **Location:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (1185 lines); `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` (760 lines); `src/Radio.Web/Components/Layout/MainLayout.razor` (790 lines)
- **Issue:** Three components exceed 750 lines each, mixing multiple responsibilities. `RadioControlPanel` combines tuner display, frequency input dialogs, preset management, SDR controls, and 856+ lines of scoped CSS. `NowPlayingPanel` combines album art, fingerprint status, transport controls, volume, and gain popover. `MainLayout` manages source routing, output routing, Cast device selection, clock, navigation, and sleep mode.
- **Recommendation:** Extract subcomponents: `RadioTunerDisplay`, `RadioPresetBank`, `RadioFrequencyDialog` from RadioControlPanel; `TransportControls`, `GainPopover`, `FingerprintBadge` from NowPlayingPanel; `SourceRouting`, `OutputRouting` from MainLayout. Target <300 lines per component.

---

## MEDIUM Priority

---

### 15. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Duplication
- **Location:** `src/Radio.Web/Components/Layout/MainLayout.razor:517-525` and `src/Radio.Web/Components/Shared/NowPlayingPanel.razor:520-528`
- **Issue:** Source icon mapping (`GetSourceIcon` / `GetFpSourceIcon`) is duplicated with subtle inconsistencies: MainLayout handles `"FilePlayer"` while NowPlayingPanel uses `"File"`; MainLayout handles `"RTLSDRCore" or "RF320"` while NowPlayingPanel only handles `"Radio"`. A third copy exists at MainLayout:528-536 for CSS data attributes.
- **Recommendation:** Create `Components/Shared/SourceTypeHelper.cs` with static methods `GetIcon(string sourceType)` and `GetDataAttribute(string sourceType)`. Normalize source type strings in one place.

---

### 16. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Duplication
- **Location:** `src/Radio.Web/Components/Pages/RadioPage.razor:603-674` and `src/Radio.Web/Components/Shared/RadioControlPanel.razor:1119-1177`
- **Issue:** Five frequency-related methods are duplicated verbatim between these two files: `FormatFrequency()`, `FormatStep()`, `GetDialogStep()`, `GetMinFrequency()`, `GetMaxFrequency()`. The RadioPage versions have inline comments; the RadioControlPanel versions are compressed. Both do identical Hz→MHz/kHz conversions.
- **Recommendation:** Create `Components/Shared/FrequencyFormatter.cs` with static methods for all frequency formatting/conversion. Both components call the shared class.

---

### 17. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Observability
- **Location:** `src/Radio.API/Middleware/ApiMetricsMiddleware.cs:34-41`
- **Issue:** The metrics middleware only increments a single `api.requests_total` counter. It tracks no request latency, no error rates, no per-endpoint breakdown, no concurrent request count, and no exception tracking. This provides almost zero operational insight.
- **Recommendation:** Add `Stopwatch` around `_next(context)` for latency, check `context.Response.StatusCode` for error counting, wrap in try/catch for exception counting, and include path tags for per-endpoint breakdown.

---

### 18. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Observability / Logging
- **Location:** `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs:348,319`; `src/Radio.API/Controllers/FilesController.cs:68,108,136,221`; `src/Radio.API/Controllers/QueueController.cs:83,117,155,186,225,293`
- **Issue:** Periodic operational telemetry and routine per-request API logs use `LogInformation` instead of `LogDebug`. The buffer stats log fires every 10 seconds during playback; file browse and queue CRUD logs fire on every user interaction. This creates excessive log noise in production.
- **Recommendation:** Change periodic telemetry (`BufferedSoundGenerator` stats, clock drift) and routine CRUD logs (file browse, queue operations) to `LogDebug`. Keep `LogInformation` for significant state changes (source switches, device changes, errors).

---

### 19. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #273, completed as part of item #7)
- **Category:** Duplication
- **Location:** `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs:268-292` (InitializeAsync), `498-527` (TryRecoverPlaybackDevice), `594-645` (SwitchPlaybackDevice)
- **Issue:** Modifier re-attachment logic (create-or-reuse then AddModifier for Balance, Limiter, FingerprintTap, VisualizationTap) is duplicated across three methods. The `TryRecoverPlaybackDevice` copy is missing `_visualizationTap` (bug, see item #7).
- **Recommendation:** Extract `private void AttachModifiersToDevice(AudioPlaybackDevice device)` that handles all four modifiers with the create-or-reuse pattern. Call from all three locations.

---

### 20. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Readability / Magic Strings
- **Location:** `src/Radio.Web/Components/Layout/MainLayout.razor` (source types: "Radio", "RTLSDRCore", "RF320", "Bluetooth", etc.); `src/Radio.Web/Components/Shared/RadioControlPanel.razor` (band types: "FM", "AM", "AIR", "SW", "WB", "VHF"); `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor` (queue states: "Current", "Played")
- **Issue:** String literals for source types, radio bands, and queue item states are scattered across multiple Razor components with no central definition. Typos would cause silent failures (wrong icon, wrong formatting).
- **Recommendation:** Create `Radio.Web/Constants/SourceTypes.cs`, `RadioBands.cs`, and `QueueItemStates.cs` with `const string` fields. Reference these constants from all components.

---

### 21. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Dependencies
- **Location:** `tests/*.csproj` files across 7 test projects
- **Issue:** xUnit version is inconsistent across test projects: 2.5.3, 2.6.3, and 2.9.3 appear in different `.csproj` files. This can cause subtle test behavior differences and makes dependency management harder.
- **Recommendation:** Consolidate all test projects to xUnit 2.9.3 (latest). Use `Directory.Build.props` to centralize the version.

---

### 22. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #275)
- **Category:** Performance
- **Location:** `src/Radio.Infrastructure/Audio/Services/AudioPreferencePersistence.cs:239-250`
- **Issue:** `RestoreSourceGainOffsets()` executes a synchronous blocking DB query (`store.GetEntryAsync(key).GetAwaiter().GetResult()`) inside a foreach loop over all `AudioSourceType` enum values. This is N sequential blocking queries during startup.
- **Recommendation:** Add a `GetEntriesByPrefixAsync("AudioPreferences:SourceGain")` method to the config store and use it to batch-read all gain values in a single query. Make the method async and await it from the caller.

---

### 23. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Error Handling
- **Location:** `src/Radio.API/Services/PhoneCallIntegrationService.cs:54-68`; `src/Radio.API/Services/RotaryEncoderHostedService.cs:43-53`
- **Issue:** Both hosted services exit `ExecuteAsync()` permanently if their initial `StartAsync()` call throws. If the RotaryPhone hub or HID device is temporarily unavailable at startup, the integration silently never starts with no retry.
- **Recommendation:** Add a retry loop with exponential backoff (e.g., 5s, 15s, 30s, 60s) around the start call, similar to the pattern in `AudioStateUpdateService` which retries on error with a 1-second backoff.

---

### 24. [PRIORITY: Medium] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Concurrency
- **Location:** Multiple files — 18 `async void` event handlers (e.g., `AudioStateUpdateService.cs:678,692,707,755,769,816,829`; `PlayHistoryTracker.cs:76,421,475,549`; `BluetoothAudioSource.cs:521`)
- **Issue:** `async void` event handlers that throw unhandled exceptions will crash the entire process. While using `async void` for event handlers is the accepted .NET pattern, each handler must have a top-level try/catch to prevent process termination.
- **Recommendation:** Audit all 18 `async void` methods to verify each has a top-level `try { ... } catch (Exception ex) { _logger.LogError(ex, "..."); }` wrapping the entire body. Add missing try/catch blocks.

---

### 25. [PRIORITY: Medium] [DIFFICULTY: Medium] --- DONE (PR #274)
- **Category:** Duplication / Base Class
- **Location:** `src/Radio.Infrastructure/Audio/SoundFlow/FingerprintTapModifier.cs:21-25,60-85,90-144,153-173,178-185` and `src/Radio.Infrastructure/Audio/SoundFlow/VisualizationTapModifier.cs:16-21,44-67,72-89,99-119,124-131`
- **Issue:** Both tap modifiers implement an identical double-buffered, lock-protected, ThreadPool-offloaded sample processing pattern. The field declarations, `ProcessSample` buffering logic, `ThreadPool.QueueUserWorkItem` dispatch, `Flush()`, and `Reset()` methods are character-for-character identical. Only the flush target differs (`WriteToOutputTap` vs `ProcessSamples`).
- **Recommendation:** Extract an abstract `BufferedTapModifier : SoundModifier` base class with a virtual `OnFlushBuffer(float[] buffer, int count)` method. Both modifiers inherit and override only the flush target. This eliminates ~120 lines of duplication.

---

### 26. [PRIORITY: Medium] [DIFFICULTY: Medium]
- **Category:** Architecture / Separation of Concerns
- **Location:** `src/Radio.Core/Interfaces/Audio/IAudioManager.cs`
- **Issue:** `IAudioManager` is a "God Interface" with 40+ members mixing source switching, engine lifecycle, ducking coordination, per-source gain control, and master volume/balance/mute. This makes it difficult to mock in tests and violates Interface Segregation Principle.
- **Recommendation:** Split into focused interfaces: `IAudioSourceManager` (source switching, creation, caching), `IAudioMixerControl` (volume, balance, mute, gain), and keep `IAudioManager` as a facade that implements both for backward compatibility.

---

### 27. [PRIORITY: Medium] [DIFFICULTY: Medium] --- DONE (PR #275)
- **Category:** Performance
- **Location:** `src/Radio.Web/Components/Shared/NowPlayingPanel.razor:328-341` (5-second timer); SignalR subscription at lines ~290-310
- **Issue:** `NowPlayingPanel` both polls playback state every 5 seconds via a timer AND subscribes to SignalR `PlaybackStateChanged` events. This creates redundant API calls when SignalR is connected and working properly.
- **Recommendation:** Remove the polling timer. Use SignalR exclusively for state updates. Add a "disconnected" visual indicator when the SignalR connection is lost, and only fall back to polling during disconnection.

---

### 28. [PRIORITY: Medium] [DIFFICULTY: Medium] --- DONE (PR #274)
- **Category:** Performance
- **Location:** `src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs:173-178`
- **Issue:** `ReadForReader()` copies data byte-by-byte from the ring buffer. For HTTP audio streaming reads of 4096+ bytes, this is significantly slower than bulk copy. This runs on every HTTP stream client read request.
- **Recommendation:** Replace the byte-by-byte loop with `Buffer.BlockCopy` or `Span<byte>` bulk copies, handling the ring buffer wrap-around with at most two copy operations (before wrap + after wrap).

---

### 29. [PRIORITY: Medium] [DIFFICULTY: Medium] --- DONE (PR #275)
- **Category:** Performance
- **Location:** `src/Radio.Web/Components/Shared/QueueHistoryPanel.razor`
- **Issue:** The queue list renders all items without virtualization. Queues can contain 1000+ items, and each render cycle processes the entire list. No `MudVirtualize` or similar virtual scrolling is used.
- **Recommendation:** Replace the queue item list with `MudVirtualize<QueueItemDto>` for queues exceeding ~50 items. This renders only visible items and dramatically improves render performance for large playlists.

---

### 30. [PRIORITY: Medium] [DIFFICULTY: Medium]
- **Category:** Error Handling
- **Location:** `src/Radio.Web/` (all API service clients in `Services/`)
- **Issue:** All 14 API client services return `null` on any exception (logged but silent in UI). Components receiving `null` show empty states with no distinction between "no data" and "API error". No Snackbar notifications, no retry buttons, no error badges.
- **Recommendation:** Create a `Result<T>` wrapper type with `Success`, `Error(message)`, and `Disconnected` states. Update API services to return `Result<T>`. Update components to show error states with retry buttons when appropriate.

---

### 31. [PRIORITY: Medium] [DIFFICULTY: Medium] --- DONE (PR #275)
- **Category:** Performance
- **Location:** `src/Radio.Infrastructure/Metrics/Repositories/SqliteMetricsRepository.cs:257-273`
- **Issue:** `GetCurrentSnapshotsAsync()` iterates over each metric key and calls `GetAggregateAsync()` individually, each executing 2 SQL queries. For N keys, this is 2N database round-trips. `SaveBucketsAsync()` (lines 104-129) creates a new `SqliteCommand` per bucket in a loop.
- **Recommendation:** Consolidate `GetCurrentSnapshotsAsync` into a single `SELECT ... WHERE Key IN (...)` query. For `SaveBucketsAsync`, reuse a prepared statement across iterations or use `INSERT ... VALUES (...), (...), (...)` batch syntax.

---

### 32. [PRIORITY: Medium] [DIFFICULTY: Medium]
- **Category:** Testing
- **Location:** `src/Radio.Web/` — no unit or bUnit tests for several pages
- **Issue:** `BluetoothPage` and `DiagnosticPage` have no component tests. The Web project overall has ~40 tests but the critical `MainLayout`, `RadioControlPanel`, and `NowPlayingPanel` components have limited coverage for their complex state management and event handling.
- **Recommendation:** Add bUnit tests for BluetoothPage and DiagnosticPage. Add targeted tests for MainLayout source/output routing state transitions and RadioControlPanel frequency conversion logic.

---

### 33. [PRIORITY: Medium] [DIFFICULTY: Medium]
- **Category:** Testing
- **Location:** `src/Radio.Infrastructure/Audio/Services/` — multiple services untested
- **Issue:** `AnnouncementService`, `AudioSourceFactory`, `AudioFileEventSourceFactory`, `VisualizationModeService`, `PlayHistoryTracker`, and `AccurateDurationReader` have no direct unit tests. `PlayHistoryTracker` has complex state machine logic for song change detection that is particularly test-worthy.
- **Recommendation:** Prioritize `PlayHistoryTracker` tests (song change detection state machine), then `AudioSourceFactory` (factory logic with device resolution), then `AnnouncementService` (ducking coordination).

---

### 34. [PRIORITY: Medium] [DIFFICULTY: Medium]
- **Category:** Architecture
- **Location:** `src/Radio.Core/Models/Audio/` — `AudioSourceState` enum and `AudioOutputState` enum
- **Issue:** `AudioSourceState` and `AudioOutputState` share 7 of their values (Created, Initializing, Ready, Stopping, Stopped, Error, Disposed). The duplication means state transition logic is implemented twice with potential for divergence.
- **Recommendation:** Extract a common `AudioComponentState` enum for shared states. If source-specific states (Playing, Paused) and output-specific states (Connecting, Streaming) are needed, use separate extension enums or a composite pattern.

---

### 35. [PRIORITY: Medium] [DIFFICULTY: Medium]
- **Category:** Separation of Concerns
- **Location:** `src/Radio.API/Controllers/SystemController.cs`; `src/Radio.API/Controllers/ConfigurationController.cs:239-261`
- **Issue:** `SystemController` contains CPU usage caching logic, log file path resolution, regex-based log parsing, and temperature zone reading — all business logic that belongs in an Infrastructure service. `ConfigurationController` has conditional store selection logic (lines 239-261) that should be in a configuration management service.
- **Recommendation:** Extract `SystemInfoService` (CPU, memory, disk, temperature, log parsing) and `ConfigurationManagementService` (store selection, reconciliation) into Infrastructure. Controllers should only delegate and map responses.

---

### 36. [PRIORITY: Medium] [DIFFICULTY: Hard]
- **Category:** Architecture / State Management
- **Location:** `src/Radio.Web/Components/Layout/MainLayout.razor` (8 state fields); `src/Radio.Web/Services/RadioPanelToggleService.cs`; scattered state across `NowPlayingPanel`, `RadioControlPanel`, `QueueHistoryPanel`
- **Issue:** Audio state (active source, selected output, volume, mute, Cast connection) is scattered across multiple components with no centralized store. MainLayout alone has 8 state fields (`_availableSources`, `_selectedSourceId`, `_availableOutputs`, `_selectedOutputId`, `_defaultCastDevice`, `_isCastConnecting`, `_showCastDropdown`, etc.). `RadioPanelToggleService` is a singleton just for panel visibility. State synchronization bugs are inevitable.
- **Recommendation:** Create a centralized `AudioStateStore` service (similar to Fluxor or a simple observable store pattern) that holds all audio-related state. Components subscribe to specific state slices. SignalR events update the store, and the store notifies subscribers. This eliminates duplicate state and sync bugs.

---

## LOW Priority

---

### 37. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Dead Code
- **Location:** `src/Radio.Infrastructure/Audio/Sources/Primary/PrimaryAudioSourceBase.cs:197-201, 208-212, 219-223, 229-234`
- **Issue:** Four virtual methods (`NextAsync`, `PreviousAsync`, `SetShuffleAsync`, `SetRepeatModeAsync`) throw `NotSupportedException` if the capability property is false, then have an unreachable `return Task.CompletedTask` after the throw. While technically a valid pattern for subclasses that override, the structure is confusing — the throw and return serve different code paths but read as sequential.
- **Recommendation:** Add a comment above each `return Task.CompletedTask` clarifying it's the default for subclasses that support the capability but don't need custom logic. Or restructure: `if (!Supports) throw; return await CoreAsync();` with a virtual `CoreAsync` that returns `Task.CompletedTask`.

---

### 38. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Readability / Magic Numbers
- **Location:** `src/Radio.Infrastructure/Audio/SoundFlow/FingerprintTapModifier.cs:53-54` (buffer size 4096); `VisualizationTapModifier.cs:37-38` (buffer size 2048); `BufferedSoundGenerator.cs:101-102` (drift thresholds 15%, 25%); `TappedOutputStream.cs:47` (bytes per sample = 2)
- **Issue:** Buffer sizes, drift thresholds, and format constants are hardcoded as magic numbers throughout the audio pipeline. Tuning requires finding and modifying scattered literals.
- **Recommendation:** Define named constants: `const int FingerprintBufferSamples = 4096`, `const int VisualizationBufferSamples = 2048`, `const float DriftWarningThreshold = 0.15f`, `const float DriftCriticalThreshold = 0.25f`, `const int PcmBytesPerSample = 2`.

---

### 39. [PRIORITY: Low] [DIFFICULTY: Easy] --- DONE (PR #274)
- **Category:** Observability
- **Location:** `src/Radio.Infrastructure/External/PhoneContactLookupService.cs:52`
- **Issue:** `_logger.LogInformation("Resolved {PhoneNumber} -> {Name}")` logs caller phone numbers and names at Information level. This is personally identifiable information (PII) appearing in production logs.
- **Recommendation:** Change to `LogDebug` and consider masking: `"Resolved {PhoneNumber} -> {Name}"` → `"Resolved ***{LastFour} -> {Name}"` with partial phone number masking.

---

### 40. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Configuration
- **Location:** `src/Radio.API/Program.cs`; `src/Radio.Web/Program.cs`
- **Issue:** SignalR hub paths (`/hubs/audio`, `/hubs/visualization`) and stream paths (`/stream/audio`, `/stream/audio/mp3`) are hardcoded string literals in both projects. If a path changes, both projects must be updated.
- **Recommendation:** Define hub/stream paths as constants in `Radio.Core` (e.g., `HubPaths.Audio`, `HubPaths.Visualization`, `StreamPaths.RawAudio`, `StreamPaths.Mp3Audio`) and reference from both projects.

---

### 41. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Error Handling
- **Location:** `src/Radio.API/Hubs/AudioStateHub.cs:20-21`; `src/Radio.API/Hubs/AudioVisualizationHub.cs:22-23`
- **Issue:** The static `_connectedClients` counter can go negative if `OnDisconnectedAsync` is called without a matching `OnConnectedAsync` (e.g., during abnormal shutdown). No lower-bound clamp exists.
- **Recommendation:** Add `_connectedClients = Math.Max(0, _connectedClients - 1)` in the decrement path. Consider replacing `lock` + manual increment with `Interlocked.Increment`/`Interlocked.Decrement` since the lock also protects a metrics gauge update.

---

### 42. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Documentation
- **Location:** `src/Radio.Web/Services/` (all 14 API client services); `src/Radio.Web/Services/RadioPanelToggleService.cs`; `src/Radio.Web/Services/DeviceDisplayStateService.cs`
- **Issue:** Web service classes have no XML doc comments on public methods. `RadioPanelToggleService` and `DeviceDisplayStateService` have no comments at all. A new developer cannot understand the purpose of these services without reading their implementation.
- **Recommendation:** Add `/// <summary>` comments to all public methods in Web services. At minimum, document the purpose and return values of each API client method.

---

### 43. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Consistency
- **Location:** `src/Radio.Web/Components/` — various components
- **Issue:** Some components implement `IDisposable`, others implement `IAsyncDisposable`. Some unsubscribe from events in Dispose, others forget. Some use try/catch in event handlers, others don't. No base component class enforces cleanup patterns.
- **Recommendation:** Standardize on `IAsyncDisposable` for all components that subscribe to events or hold timers. Consider a `RadioComponentBase : ComponentBase, IAsyncDisposable` base class with a virtual `OnDisposeAsync()` and automatic event unsubscription tracking.

---

### 44. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Dependencies
- **Location:** `src/Radio.Infrastructure/Audio/Services/AlbumArtCacheService.cs:127` (registered as concrete type)
- **Issue:** `AlbumArtCacheService` is registered as `AddSingleton<AlbumArtCacheService>()` without an interface, breaking the Dependency Inversion Principle. All other services in the audio pipeline use interface-based registration.
- **Recommendation:** Extract `IAlbumArtCacheService` interface and register via `AddSingleton<IAlbumArtCacheService, AlbumArtCacheService>()`. This enables testing with mocks and maintains consistency.

---

### 45. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Performance
- **Location:** `src/Radio.Infrastructure/Audio/Services/AudioPreferencePersistence.cs:81-88`
- **Issue:** `RestoreVolumePreferences()` is synchronous but calls three `GetAwaiter().GetResult()` blocking calls sequentially during startup. While this runs once, it sets a bad pattern and blocks the thread pool thread.
- **Recommendation:** Make the method `async Task RestoreVolumePreferencesAsync()` and `await` the config store calls. Update the caller to await it. This aligns with the rest of the codebase's async patterns.

---

### 46. [PRIORITY: Low] [DIFFICULTY: Easy]
- **Category:** Dead Code / Bloat
- **Location:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor` — 856+ lines of scoped CSS
- **Issue:** The component has more CSS than C#/Razor code. Much of the scoped CSS could be extracted to a shared stylesheet or use existing design system classes from `design-system.css`. The sheer volume makes the component file unwieldy.
- **Recommendation:** Extract the scoped CSS to `RadioControlPanel.razor.css` (Blazor CSS isolation file) or factor common patterns into `design-system.css` utility classes. Keep only component-specific overrides in scoped styles.

---

### 47. [PRIORITY: Low] [DIFFICULTY: Medium]
- **Category:** Architecture
- **Location:** `src/Radio.Core/Interfaces/Audio/IAudioSource.cs` — `GetSoundComponent()` returns `object`
- **Issue:** `IAudioSource.GetSoundComponent()` returns an untyped `object` (a SoundFlow component). Consumers must cast to the expected type, which is fragile and undiscoverable. The Core layer leaks an implicit dependency on SoundFlow's type system.
- **Recommendation:** Define an `ISoundComponent` marker interface in Core that SoundFlow components implement via adapter. Return `ISoundComponent` instead of `object`. This makes the contract explicit and type-safe.

---

### 48. [PRIORITY: Low] [DIFFICULTY: Medium]
- **Category:** Configuration
- **Location:** `src/Radio.Core/Configuration/` — 20 option POCOs
- **Issue:** Configuration option classes have no default values defined in Core. Defaults are scattered across `appsettings.json` files and Infrastructure code. If `appsettings.json` is missing a section, the application may get unexpected `null` or `0` values.
- **Recommendation:** Add sensible defaults directly in the option POCOs (e.g., `public int SampleRate { get; set; } = 48000;`, `public float DuckingPercentage { get; set; } = 0.3f;`). This makes the system resilient to missing configuration and self-documenting.

---

### 49. [PRIORITY: Low] [DIFFICULTY: Medium]
- **Category:** Observability
- **Location:** `src/Radio.API/` — no health check endpoint
- **Issue:** There is no `/health` or `/ready` endpoint. The systemd services have no way to verify the API is healthy beyond checking if the process is running. If the audio engine crashes internally but the HTTP server stays up, the system appears healthy when it isn't.
- **Recommendation:** Add ASP.NET Core health checks: `builder.Services.AddHealthChecks().AddCheck<AudioEngineHealthCheck>("audio-engine")`. Map to `/health`. Configure systemd to poll it.

---

### 50. [PRIORITY: Low] [DIFFICULTY: Medium]
- **Category:** Architecture
- **Location:** `src/Radio.Core/Models/Audio/RadioPreset.cs` — `Frequency` property is `double`; `src/Radio.Core/Interfaces/Audio/IRadioControl.cs` — `CurrentFrequency` is `Frequency` struct
- **Issue:** `RadioPreset.Frequency` is a raw `double` (ambiguous unit — MHz? kHz? Hz?) while `IRadioControl.CurrentFrequency` uses the type-safe `Frequency` struct that stores Hz internally. This dual representation invites unit conversion bugs.
- **Recommendation:** Change `RadioPreset.Frequency` to use the `Frequency` struct. Update serialization and database schema accordingly. The `Frequency` struct already has `FromMegahertz()` and `FromKilohertz()` factory methods for backward-compatible deserialization.

---

### 51. [PRIORITY: Low] [DIFFICULTY: Medium]
- **Category:** Testing
- **Location:** `tests/` — hardcoded test constants across projects
- **Issue:** Sample rates (48000), frequencies (440Hz), buffer sizes, and other test constants are hardcoded independently in each test file. No shared test constants exist.
- **Recommendation:** Create `tests/Radio.TestCommon/TestAudioConstants.cs` (or add to an existing shared project) with `const int SampleRate = 48000`, `const double TestFrequencyHz = 440.0`, etc. Reference from all test projects.

---

### 52. [PRIORITY: Low] [DIFFICULTY: Medium]
- **Category:** Performance
- **Location:** `src/Radio.Web/Components/Shared/RadioControlPanel.razor` — `OnInitializedAsync`; `src/Radio.Web/Components/Layout/MainLayout.razor` — `OnInitializedAsync`
- **Issue:** Both components make 3+ sequential API calls during initialization (e.g., `LoadRadioStateAsync()` → `LoadPresetsAsync()` → `LoadBandsAsync()`). These are independent operations that could run concurrently.
- **Recommendation:** Use `await Task.WhenAll(LoadRadioStateAsync(), LoadPresetsAsync(), LoadBandsAsync())` for independent initialization calls. This reduces perceived startup latency by running API calls in parallel.

---

### 53. [PRIORITY: Low] [DIFFICULTY: Hard]
- **Category:** Architecture
- **Location:** `src/Radio.Infrastructure/Audio/` — no documented lock ordering
- **Issue:** The audio subsystem has 25+ lock objects across ~15 files (see: `_switchLock`, `_createLock` in AudioManager; `_stateLock` in AudioEngine; `_playersLock` in PlaybackService; `_sourcesLock` in MasterMixer; `_bufferLock` in BufferedSoundGenerator; etc.). There is no documented lock hierarchy or ordering convention. The existing deadlock risk in AudioManager (item #10) demonstrates the practical danger.
- **Recommendation:** Document a lock ordering hierarchy in `design/AUDIO_ARCHITECTURE.md` (e.g., "Engine locks → Manager locks → Source locks → Buffer locks"). Add comments at each lock declaration specifying its position in the hierarchy. Consider using `System.Threading.Lock` (if on .NET 9+) or a debug-mode lock ordering validator.
