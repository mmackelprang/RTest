# Task Plan: Extract Independent NuGet Packages from Radio Console

## Goal
Extract standalone, reusable libraries from the Radio Console monorepo into publishable NuGet packages. Six candidates identified across SDR, audio analysis, fingerprinting, configuration, metrics, and domain core.

## Current Phase
Planning complete — pending user decision on execution order

## Phases

| # | Phase | Status | Effort | Notes |
|---|-------|--------|--------|-------|
| 0 | Local NuGet feed infrastructure | pending | Low | nuget.config, CI pack step, deploy restore, Directory.Build.props packaging defaults |
| 1 | RTLSDRCore | pending | Low | Already has full NuGet metadata, 75 tests, zero internal deps |
| 2 | Radio.AudioAnalysis | pending | Medium | Move from tests/ to src/, add ~15 unit tests, NuGet metadata |
| 3 | Metrics library | pending | Low-Med | 99% generic, ~1,500 LOC, only needs DatabasePath decoupling |
| 4 | Configuration library | pending | Medium | 90% generic, ~35 files, remove 2 Radio-specific files + 1 interface extraction |
| 5 | Fingerprinting library | pending | Med-High | 65-70% extractable, needs IAudioSampleProvider abstraction + DatabasePathResolver decoupling |
| 6 | Radio.Core | pending | Medium | Add NuGet metadata, XML docs, README — but may be internal-only |

---

## Phase 0: Local NuGet Feed Infrastructure

**Goal:** Set up a local NuGet package feed so extracted packages can be consumed by Radio.Console projects during build, CI, and deploy — without publishing to NuGet.org.

### 0A. Local feed directory
- [ ] Create `packages/` directory at repo root for local .nupkg storage
- [ ] Add `packages/` to `.gitignore` (binary artifacts, not source)

### 0B. `nuget.config` at repo root
- [ ] Create `nuget.config` with two sources:
  - `local` → `./packages` (local feed, highest priority)
  - `nuget.org` → `https://api.nuget.org/v3/index.json` (public fallback)
- [ ] Verify `dotnet restore` picks up local feed

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="./packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

### 0C. Pack + push workflow for extracted packages
- [ ] Add a `pack-local.ps1` (or shell script) that:
  1. Runs `dotnet pack` on each extractable project
  2. Copies .nupkg files to `packages/` directory
  3. Optionally bumps version (or uses `--version-suffix`)
- [ ] Convention: packages use SemVer, pre-release suffix for local dev (`1.0.0-local.1`)

### 0D. CI integration
- [ ] Update `.github/workflows/build.yml` to:
  1. Pack extractable projects after build
  2. Run `dotnet restore` with local feed (already automatic via `nuget.config`)
  3. Optionally upload .nupkg as build artifacts (for traceability)
- [ ] Ensure CI `dotnet restore` respects `nuget.config` (it does by default)

### 0E. Deploy script integration
- [ ] Verify `Deploy-ToLinux.ps1` `dotnet restore` step picks up `nuget.config`
  - Current: `dotnet restore ... --runtime $Runtime` — should auto-detect nuget.config
- [ ] If packages/ directory is empty (first clone), restore still works (all deps on nuget.org)

### 0F. Directory.Build.props packaging defaults
- [ ] Add shared packaging properties for extractable projects:
  - `Authors`, `Copyright`, `PackageLicenseExpression` (MIT)
  - `RepositoryUrl`, `RepositoryType`
  - `GenerateDocumentationFile` (already true)
- [ ] Each extractable project overrides `PackageId`, `Description`, `Version`

### Promotion path (future)
When ready to publish publicly:
1. Create GitHub Actions workflow with `dotnet nuget push` to NuGet.org
2. Or push to GitHub Packages as intermediate step (private, version browsing)
3. Remove `local` source from `nuget.config` once packages are on public feed

---

## Phase 1: RTLSDRCore — Publish to NuGet

**Readiness: 8/10 — Publish-ready today**

### Current State
- `src/RTLSDRCore/RTLSDRCore.csproj` — Full NuGet metadata (PackageId, Version 1.0.0, MIT license, README, symbols)
- 75 unit tests in `tests/RTLSDRCore.Tests/`
- Zero internal project references (depends only on Serilog 4.0.0)
- `src/RTLSDRCore/README.md` already exists

### Tasks
- [ ] Verify `dotnet pack src/RTLSDRCore --configuration Release` produces clean .nupkg
- [ ] Review README for external consumers (not just internal docs)
- [ ] Verify all public API has XML documentation
- [ ] Test: create throwaway console project, add local .nupkg, verify standalone
- [ ] Decide: NuGet.org or GitHub Packages first?

### Public API
- `RadioReceiver` — Main class (factory: `CreateWithMockDevice()`, `CreateWithFirstAvailableDevice()`)
- `IRadioControl` — Tuning, scanning, band selection
- `ISdrDevice` / `MockSdrDevice` / `RtlSdrDevice` — Hardware abstraction
- Demodulators: AM, FM, SSB + StereoFmDecoder, RdsDecoder
- Models: `BandType`, `ModulationType`, `RadioState`, `AudioFormat`, `BandPresets`

---

## Phase 2: Radio.AudioAnalysis — Extract & Publish

**Readiness: 5/10 — Needs relocation + unit tests**

### Current State
- Lives in `tests/Radio.AudioAnalysis/` with `IsPackable=false`
- 7 source files, ~1,400 LOC, zero dependencies
- No dedicated unit tests (only indirect via integration tests)

### Tasks
- [ ] Move from `tests/Radio.AudioAnalysis/` to `src/Radio.AudioAnalysis/`
- [ ] Update all project references
- [ ] Remove `IsPackable=false`, add NuGet metadata
- [ ] Write ~15-20 unit tests (cross-correlation, THD, silence detection, WAV round-trip)
- [ ] Add README with examples
- [ ] Add XML documentation on public methods

### Public API
- `WaveformComparison` — Cross-correlation, time alignment, distortion detection
- `FrequencyAnalysis` — THD measurement, Goertzel algorithm
- `SilenceDetector` — Zero runs, repeated samples, clipping
- `WavFileHelper` — WAV I/O (16/24/32-bit PCM)
- `DistortionEvent`, `DistortionReport`, `ComparisonOptions`

---

## Phase 3: Metrics Library

**Readiness: 8/10 — 99% generic, minimal decoupling needed**

### Current State
- ~1,500 LOC across 5 implementation files + 3 core model/interface files
- IMetricsCollector (Increment/Gauge) + IMetricsReader (history/snapshots/aggregate/keys)
- SQLite storage with 3-resolution tables (Minute/Hour/Day) + buffered writes
- MetricsRollupService: automatic aggregation + configurable retention policies
- Zero Radio-specific logic in any metrics code

### Decoupling Required
- MetricsDbContext uses `GetConfigurationDatabasePath()` → add explicit `MetricsOptions.DatabasePath`
- SystemMonitorService (OS-specific: CPU/memory/disk/temp) → exclude or make optional

### Tasks
- [ ] Add `DatabasePath` property to `MetricsOptions`, update `MetricsDbContext` to use it
- [ ] Extract core files into standalone project structure
- [ ] Decide: include SystemMonitorService as optional or exclude
- [ ] Add NuGet metadata, README with usage examples
- [ ] Verify tests pass in isolation

### Public API
- `IMetricsCollector` — `Increment(key, amount, tags?)`, `Gauge(key, value, tags?)`
- `IMetricsReader` — `GetHistoryAsync()`, `GetCurrentSnapshotsAsync()`, `GetAggregateAsync()`, `ListMetricKeysAsync()`
- `MetricPoint`, `MetricType`, `MetricResolution` — Domain models
- `MetricsOptions` — Configuration
- `AddMetrics(IServiceCollection, IConfiguration)` — DI extension

---

## Phase 4: Configuration Library

**Readiness: 7/10 — 90% generic, clean separation**

### Current State
- 35 C# files across 8 folders (Abstractions, Models, Stores, Secrets, Bridge, Backup, Services, Options)
- Dual backend: JSON + SQLite stores, switchable via config
- Encrypted secrets with `${secret:identifier}` tag substitution (Data Protection API)
- Bridge to .NET IConfiguration/IOptionsMonitor pipeline (real-time config changes from UI)
- Backup/restore via ZIP with manifests

### Decoupling Required
- Remove 2 Radio-specific files: `DeviceOptionsResolver`, `PreferencesPersistenceService` (keep in Radio.Infrastructure)
- Replace `DatabasePathResolver` with `IDatabasePathProvider` interface or `Func<string>`

### Tasks
- [ ] Create `IDatabasePathProvider` interface
- [ ] Remove/exclude Radio-specific files from package
- [ ] Extract into standalone project structure
- [ ] Add NuGet metadata, README with examples
- [ ] Verify Bridge (SqliteConfigurationProvider → IOptionsMonitor) works in isolation

### Public API
- `IConfigurationStore` — CRUD on key-value config entries (JSON + SQLite impls)
- `ISecretsProvider` — Encrypted secret resolution with `${secret:id}` substitution
- `IConfigurationManager` — High-level orchestration
- `IConfigurationBackupService` — ZIP-based backup/restore/import/export
- Bridge: `SqliteConfigurationProvider` + `ConfigStoreChangeNotifier` → .NET IConfiguration pipeline
- `AddManagedConfiguration(IServiceCollection, IConfiguration)` — DI extension

---

## Phase 5: Fingerprinting / Audio Recognition Library

**Readiness: 6/10 — 65-70% extractable, needs one interface abstraction**

### Current State
- ~3,000 LOC total, ~2,000 extractable
- SongRecRecognitionService: wraps songrec binary (subprocess), zero Radio deps
- MetadataLookupService: MusicBrainz + Cover Art Archive HTTP queries, zero Radio deps
- SQLite repositories for fingerprint cache + track metadata
- BackgroundIdentificationService: duplicate suppression, song change detection, status tracking

### Decoupling Required
- `IAudioSampleProvider`: strip Radio-specific properties (`PlaySource` enum, `SourceFilePath`, `NeedsFingerprintingLookup`) into a Radio adapter
- `DatabasePathResolver` → `Func<string>` or options pattern
- **NOT extractable**: `SoundFlowAudioTap` (~270 LOC) — bridge to SoundFlow engine, stays in Radio.Infrastructure

### Tasks
- [ ] Slim down `IAudioSampleProvider` to generic interface (CaptureAsync, IsActive, SourceName)
- [ ] Create `RadioAudioSampleProviderAdapter` in Radio.Infrastructure wrapping SoundFlowAudioTap
- [ ] Replace `DatabasePathResolver` with options pattern
- [ ] Extract into standalone project structure
- [ ] Verify ~8 existing test files work in isolation
- [ ] Add NuGet metadata, README with examples
- [ ] Document: requires `songrec` binary at runtime

### Public API
- `ISongRecRecognitionService` — Recognize audio → `TrackMetadata`
- `IMetadataLookupService` — MusicBrainz/Cover Art Archive text search
- `BackgroundIdentificationService` — Hosted service with duplicate suppression + song change events
- `IAudioSampleProvider` — Generic audio capture abstraction (consumer-implemented)
- `IFingerprintCacheRepository`, `ITrackMetadataRepository` — SQLite persistence
- Models: `TrackMetadata`, `AudioSampleBuffer`, `FingerprintData`, `CachedFingerprint`
- Events: `TrackIdentifiedEventArgs`, `SongChangedEventArgs`

---

## Phase 6: Radio.Core — Package for Internal Structure

**Readiness: 6/10 — Useful for internal package management, questionable external value**

### Current State
- 84 files, ~90+ public types, depends only on Microsoft.Extensions.*
- 37 unit tests

### Open Question
Radio.Core is domain-specific to the Radio Console application. Unlike the other packages (SDR, audio analysis, metrics, config, fingerprinting), it doesn't solve a general problem. Options:
- **A)** Publish externally — useful if someone wants to build a compatible Radio Console implementation
- **B)** Internal package only — add NuGet metadata for internal dependency management but don't publish
- **C)** Skip — not worth the versioning overhead for a monorepo

### Tasks (if proceeding)
- [ ] Add NuGet metadata
- [ ] Audit XML documentation coverage
- [ ] Write README explaining domain model
- [ ] Decide A/B/C above

---

## Key Decisions
| Decision | Rationale | Date |
|----------|-----------|------|
| Pending | | |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none yet) | | |
