# Task Plan: Album Art Not Displaying in Now Playing Panel or Google Cast

## Goal
Fix album art not appearing in:
1. The Now Playing panel (should replace default note icon as background)
2. Google Cast metadata (visible in Google Home app)

Fingerprinting pipeline works correctly — metadata is identified, MusicBrainz confirms CoverArt=True, but the cover art URL never reaches the Web UI or Cast output.

## Current Phase
Phase 5: Delivery (COMPLETE)

## Phases

### Phase 1: Requirements & Discovery
- [x] Trace cover art URL resolution after MusicBrainz enrichment
- [x] Trace how metadata (including cover art) flows to SignalR/AudioState
- [x] Check NowPlayingPanel rendering of album art
- [x] Check GoogleCastOutput metadata handling
- [x] Document findings
- **Status:** complete

### Phase 2: Root Cause Analysis
- [x] Root Cause 1: FilePlayer NeedsFingerprintingLookup guard blocks album art for files with complete tags
- [x] Root Cause 2: Cached metadata from earlier sessions has null CoverArtUrl (never re-enriched)
- [x] Determine fix approach for Web UI
- [x] Determine fix approach for Cast metadata
- **Status:** complete

### Phase 3: Implementation
- [x] Fix 1: Move album art update before NeedsFingerprintingLookup guard in FilePlayerAudioSource
- [x] Fix 2: Add cache re-enrichment in MetadataLookupService for missing CoverArtUrl
- [x] Add UpdateCoverArtUrlAsync to ITrackMetadataRepository + SqliteTrackMetadataRepository
- [x] Add CoverArtUrl to identification log in BackgroundIdentificationService
- [x] Integration tests: CoverArtPipelineIntegrationTests (2 tests)
- **Status:** complete

### Phase 4: Testing & Verification
- [x] `dotnet build --configuration Release` — 0 warnings
- [x] Infrastructure tests: 643/643 pass
- [x] API tests: 190/190 pass
- [x] Integration tests: 2/2 pass (Cover Art Archive pipeline verified)
- [x] Manual UAT: album art appears in Now Playing panel
- [x] Manual UAT: album art appears in Google Home app
- [x] Manual UAT: log shows "Album art URL set for..." after identification
- **Status:** complete

### Phase 5: Delivery
- [x] Review all changes
- [x] Verified by user
- **Status:** complete

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Move album art before NeedsFingerprintingLookup | SoundFlow doesn't extract embedded art, so fingerprinting is the only path |
| Cache re-enrichment for missing CoverArtUrl | Tracks cached before Cover Art Archive code have null URLs |
| Persist re-enriched URL to SQLite | Avoid re-fetching on every cache hit |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none) | | |

## Files Modified
| File | Changes |
|------|---------|
| `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` | Move album art update before NeedsFingerprintingLookup guard |
| `src/Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs` | Cache re-enrichment for missing CoverArtUrl |
| `src/Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs` | Add CoverArtUrl to identification log |
| `src/Radio.Core/Interfaces/Audio/ITrackMetadataRepository.cs` | Add UpdateCoverArtUrlAsync |
| `src/Radio.Infrastructure/Audio/Fingerprinting/Data/SqliteTrackMetadataRepository.cs` | Implement UpdateCoverArtUrlAsync |
| `tests/Radio.IntegrationTests/Fingerprinting/CoverArtPipelineIntegrationTests.cs` | NEW: 2 integration tests for Cover Art Archive pipeline |
