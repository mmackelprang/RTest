# Findings & Decisions

## Album Art Pipeline Investigation

### Known Facts
- Fingerprinting pipeline correctly identifies tracks (AcoustID → MusicBrainz)
- MusicBrainz enrichment reports CoverArt=True
- No cover art URL appears in Web UI or Cast metadata
- Logs show no cover art URL being set or fetched

### Pipeline to Trace
1. MusicBrainz enrichment sets CoverArt=True → where is the URL fetched?
2. Cover Art Archive lookup → does it happen?
3. TrackMetadata model → does it have a CoverArtUrl property?
4. AudioStateUpdateService → does it broadcast cover art URL via SignalR?
5. NowPlayingPanel → does it read and render the cover art URL?
6. GoogleCastOutput → does it include cover art in media metadata?

---
*Update after every 2 view/search operations*
