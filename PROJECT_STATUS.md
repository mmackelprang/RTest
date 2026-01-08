# Project Status

**Last Updated:** 2025-12-19

## Current Focus
- **Phase 3:** Primary Audio Sources (Radio Tuner Integration)
- **Phase 9:** Web UI Components (Visualizer, Radio Page)

## Recent Achievements
- **Radio Tuner:**
  - Implemented `RadioBandService` to expose RTLSDRCore presets.
  - Updated `RadioPage.razor` to use dynamic band data from the API.
  - Implemented smart frequency clamping when switching bands (moves to closest edge).
  - Enhanced Radio UI frequency formatting (AM uses kHz, others use MHz with 3 decimals).
  - Added support for AM/FM specific step sizes in UI and ensured API updates step size on band change.
  - Fixed `System.NotSupportedException` in Spotify Audio Source queue clearing.
  - Fixed `ArgumentException` for AIR band in SDR source mapping.
  - Fixed Play History JSON deserialization error (Model mismatch).
  - Implemented optimistic UI updates for Radio band and step changes to improve responsiveness.
- **Audio Fingerprinting:**
  - Fixed SQLite `FOREIGN KEY` constraint violation in `BackgroundIdentificationService`.
  - Updated `MetadataLookupService` to return correct `FingerprintId`.
- **Web UI:**
  - Fixed JavaScript syntax error in `visualizer.js` (Unexpected token '}').
  - Fixed Play History "Bad Request" error when filtering by "All" sources.
  - Corrected "FilePlayer" source filter to "File" in Play History page.
  - Fixed Secrets retrieval by implementing `SecretResolvingPostConfigureOptions`.
  - Fixed Balance Slider 400 Bad Request by making `PlaybackAction` nullable in API.
  - Fixed Start/Pause button issues by using correct `UpdatePlaybackRequest` actions ("Play"/"Pause").
  - Enabled `JsonStringEnumConverter` in API to handle string enums correctly.
- **Maintenance:**
  - Fixed build error in `SpotifyAudioSourceTests`.
  - Cleaned up obsolete `SPOTIFY_LOOPBACK` documentation.

## Next Steps
- Continue Web UI implementation.
- Complete Spotify source integration.
- Verify Visualizer functionality in the browser.
