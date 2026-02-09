# Task Plan: Album Art Not Displaying in Now Playing Panel or Google Cast

## Goal
Fix album art not appearing in:
1. The Now Playing panel (should replace default note icon as background)
2. Google Cast metadata (visible in Google Home app)

Fingerprinting pipeline works correctly — metadata is identified, MusicBrainz confirms CoverArt=True, but the cover art URL never reaches the Web UI or Cast output.

## Current Phase
Phase 1: Requirements & Discovery

## Phases

### Phase 1: Requirements & Discovery
- [ ] Trace cover art URL resolution after MusicBrainz enrichment
- [ ] Trace how metadata (including cover art) flows to SignalR/AudioState
- [ ] Check NowPlayingPanel rendering of album art
- [ ] Check GoogleCastOutput metadata handling
- [ ] Document findings
- **Status:** in_progress

### Phase 2: Root Cause Analysis
- [ ] Identify where the cover art URL is lost or never set
- [ ] Determine fix approach for Web UI
- [ ] Determine fix approach for Cast metadata
- **Status:** pending

### Phase 3: Implementation
- [ ] Fix cover art pipeline (Web UI)
- [ ] Fix cover art pipeline (Cast metadata)
- **Status:** pending

### Phase 4: Testing & Verification
- [ ] Build passes (0 warnings)
- [ ] Existing tests pass
- [ ] Manual UAT: album art appears in Now Playing
- [ ] Manual UAT: album art appears in Google Home app
- **Status:** pending

### Phase 5: Delivery
- [ ] Review all changes
- [ ] Commit when user requests
- **Status:** pending

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| (none yet) | | |

## Files Modified
| File | Changes |
|------|---------|
| (none yet) | |
