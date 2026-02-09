# Findings & Decisions

## Album Art Pipeline Investigation

### Root Cause: FilePlayerAudioSource NeedsFingerprintingLookup Guard

**The album art update in `OnTrackIdentified()` was gated behind the `NeedsFingerprintingLookup` flag.**

In `UpdateMetadataFromFile()` (line 1587-1595):
```csharp
bool hasIncompleteMetadata =
  _metadata[StandardMetadataKeys.Artist].Equals(StandardMetadataKeys.DefaultArtist) ||
  _metadata[StandardMetadataKeys.Album].Equals(StandardMetadataKeys.DefaultAlbum);
```

- Files with complete ID3 tags (artist + album) → `NeedsFingerprintingLookup` never set
- `OnTrackIdentified()` early-returns at the guard → album art never applied
- SoundFlow's `SoundMetadataReader` does NOT extract embedded album art
- So `AlbumArtUrl` is ALWAYS the default for file sources, but the guard prevents the update

### Why It Worked for SDR/USB Sources
- `SDRRadioAudioSource.OnTrackIdentified()` has NO `NeedsFingerprintingLookup` guard
- `USBAudioSourceBase.OnTrackIdentified()` has NO `NeedsFingerprintingLookup` guard
- Both always update album art from fingerprinting

### Fix Applied
Moved the album art update BEFORE the `NeedsFingerprintingLookup` guard in `FilePlayerAudioSource.OnTrackIdentified()`:
- Album art is now always updated from fingerprinting when current art is the default
- Title/artist/album updates still require `NeedsFingerprintingLookup` (files with incomplete tags)
- This matches the behavior of SDR and USB sources

### Pipeline Verification (All Links Working)
1. ✅ MetadataLookupService → AcoustID → MusicBrainz → Cover Art Archive
2. ✅ Cover Art Archive returns thumbnail URLs (confirmed by `CoverArt=True` in logs)
3. ✅ TrackMetadata.CoverArtUrl populated
4. ✅ BackgroundIdentificationService fires TrackIdentified event
5. ❌ **FilePlayerAudioSource.OnTrackIdentified() — early return (FIXED)**
6. ✅ AudioStateUpdateService detects change, broadcasts via SignalR
7. ✅ AudioStateUpdateService pushes to Cast via PushMetadataToCastAsync()
8. ✅ NowPlayingPanel renders cover art as CSS background-image
9. ✅ GoogleCastOutput.BuildMedia() includes Images[] for http:// URLs

### Cast Metadata Flow
- `AudioStateUpdateService.PushMetadataToCastAsync()` sends `AlbumArtUrl` to Cast
- `GoogleCastOutput.BuildMedia()` validates URL starts with "http" before including
- Cover Art Archive URLs (`https://coverartarchive.org/...`) pass validation
- LoadAsync reloads media with updated metadata

## Key Files
| File | Purpose |
|------|---------|
| `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` | **FIXED** — album art update moved before NeedsFingerprintingLookup guard |
| `src/Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs` | Cover Art Archive lookup (working correctly) |
| `src/Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs` | Fires TrackIdentified event (working correctly) |
| `src/Radio.API/Services/AudioStateUpdateService.cs` | Broadcasts metadata + pushes to Cast (working correctly) |
| `src/Radio.Web/Components/Shared/NowPlayingPanel.razor` | Renders album art as background-image (working correctly) |
| `src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs` | Cast media metadata with album art (working correctly) |
