# Project Status

**Last Updated:** 2026-02-06

## Current Focus
- **Phase 3:** Primary Audio Sources (Radio Tuner Integration)
- **Phase 9:** Web UI Components (Visualizer, Radio Page)
- **Pending:** Bluetooth Audio Input Implementation (9 phases planned)

## Recent Achievements
- **Bluetooth Planning:**
  - Created comprehensive implementation plan for Bluetooth A2DP sink support
  - Documented 9-phase approach spanning 24-35 days
  - Platform abstraction via IBluetoothService (Linux BlueZ, Windows 32feet.NET)
  - Full audio pipeline integration planned
- **Radio Tuner:**
  - Implemented `RadioBandService` to expose RTLSDRCore presets.
  - Updated `RadioPage.razor` to use dynamic band data from the API.
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

## Bluetooth Implementation Phases (Planned)

| Phase | Status | Description |
|-------|--------|-------------|
| 1 - Core Architecture | 📋 Planned | Interfaces, enums, config classes (2-3 days) |
| 2 - Platform Implementations | 📋 Planned | Linux BlueZ + Windows APIs (5-7 days) |
| 3 - Audio Source | 📋 Planned | BluetoothAudioSource implementation (3-4 days) |
| 4 - Audio Manager Integration | 📋 Planned | Source registration (2-3 days) |
| 5 - API & Control | 📋 Planned | REST endpoints, SignalR (3-4 days) |
| 6 - Configuration | 📋 Planned | Settings persistence (2 days) |
| 7 - Verification | 📋 Planned | Pipeline integration + UAT tests (1-2 days) |
| 8 - Testing | 📋 Planned | Unit, integration, platform tests (4-5 days) |
| 9 - Documentation | 📋 Planned | User guides, API docs (2-3 days) |

**Bluetooth Timeline**: 24-35 days (≈3.5-5 weeks) after plan approval

## Next Steps
- Review and approve Bluetooth implementation plan
- Continue Web UI implementation.
- Complete Spotify source integration.
- Verify Visualizer functionality in the browser.
