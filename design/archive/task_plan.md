# Task Plan: Extract Radio.Fingerprinting NuGet Package (Phase 5)

## Goal
Extract fingerprinting implementation code from Radio.Infrastructure into a standalone `Radio.Fingerprinting` NuGet package. The package depends on Radio.Core for shared models/interfaces and Radio.Metrics for optional metrics.

## Current Phase
Phase 6: complete

## Architecture Decision
**Option B: Implementation extraction with Radio.Core dependency.**
- Models, interfaces, events STAY in Radio.Core (shared domain types used by 75+ files)
- Only implementation files + FingerprintingOptions move to Radio.Fingerprinting
- ~20 files need namespace updates (vs 75+ for full extraction)
- Radio.Fingerprinting depends on Radio.Core + Radio.Metrics

## Phases

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | Create project + move files | complete | csproj, moved 7 files, created IFingerprintDataConnection |
| 2 | Update namespaces + references | complete | ~25 consumer files updated with using additions |
| 3 | DI split (library vs Infrastructure wrapper) | complete | FingerprintingServiceExtensions updated |
| 4 | Build + fix errors | complete | 0 errors, 1 pre-existing warning |
| 5 | Run tests + verify | complete | All tests pass (1 pre-existing flaky: WaveformComparisonTests) |
| 6 | NuGet metadata + pack | complete | pack-local.ps1 updated, README.md created, .nupkg builds |

---

## Phase 1: Create Project + Move Files

### 1A. Create `src/Radio.Fingerprinting/Radio.Fingerprinting.csproj`
- Target: net10.0
- Dependencies: Radio.Core, Radio.Metrics, Microsoft.Data.Sqlite, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options
- NuGet metadata, InternalsVisibleTo for test projects

### 1B. Create `IFingerprintDataConnection` abstraction
- Single method: `Task<SqliteConnection> GetConnectionAsync(CancellationToken ct = default)`
- FingerprintDbContext implements this interface (in Radio.Infrastructure)

### 1C. Move files (7 files)
| Source | Destination | New Namespace |
|--------|------------|---------------|
| Radio.Core/Configuration/FingerprintingOptions.cs | Radio.Fingerprinting/FingerprintingOptions.cs | Radio.Fingerprinting |
| Radio.Infrastructure/.../BackgroundIdentificationService.cs | Radio.Fingerprinting/Services/ | Radio.Fingerprinting.Services |
| Radio.Infrastructure/.../MetadataLookupService.cs | Radio.Fingerprinting/Services/ | Radio.Fingerprinting.Services |
| Radio.Infrastructure/.../SongRecRecognitionService.cs | Radio.Fingerprinting/Services/ | Radio.Fingerprinting.Services |
| Radio.Infrastructure/.../SqliteFingerprintCacheRepository.cs | Radio.Fingerprinting/Data/ | Radio.Fingerprinting.Data |
| Radio.Infrastructure/.../SqliteTrackMetadataRepository.cs | Radio.Fingerprinting/Data/ | Radio.Fingerprinting.Data |
| Radio.Infrastructure/.../SqlitePlayHistoryRepository.cs | Radio.Fingerprinting/Data/ | Radio.Fingerprinting.Data |

### 1D. Update moved files
- Change namespace declarations
- Change repository constructors: FingerprintDbContext -> IFingerprintDataConnection
- Update using statements

---

## Phase 2: Update Namespaces + References
~20 consumer files need using statement updates + project reference additions.

## Phase 3: DI Split
Library DI registers extracted services. Infrastructure wrapper calls library, adds Radio-specific services.

## Phase 4: Build + Fix Errors
Iterative compile-fix cycle.

## Phase 5: Run Tests + Verify
All ~1416 tests must pass.

## Phase 6: NuGet Metadata + Pack
pack-local.ps1, README.

## Key Decisions
| Decision | Rationale | Date |
|----------|-----------|------|
| Option B: Depend on Radio.Core | Models used by 75+ files; moving them causes massive churn | 2026-03-09 |
| Move FingerprintingOptions only | Library-specific config, not shared domain | 2026-03-09 |
| IFingerprintDataConnection | Decouple repos from FingerprintDbContext (12-table monolith) | 2026-03-09 |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Python script placed usings inside method bodies | 1 | Manual fix: remove misplaced lines, add usings at file top |
| Missing `using Radio.Fingerprinting;` in 6 test files | 1 | Added using to each file for FingerprintingOptions |
| CoverArtPipelineIntegrationTests missing Services using | 1 | Added `using Radio.Fingerprinting.Services;` |
