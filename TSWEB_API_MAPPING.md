# TypeScript Web UI to C# API Mapping

## Executive Summary

This document provides a comprehensive comparison between the TypeScript web UI API specification (`tsweb/api-spec.yaml`) and the current C# implementation across Radio.API, Radio.Core, and Radio.Infrastructure projects. The goal is to create a roadmap for unifying the API surface to support both the planned Blazor web app and any external integrations.

**Date:** December 4, 2024  
**Status:** Initial Analysis  
**Target:** Normalize API endpoints for Blazor Web App development

---

## API Compatibility Matrix

### Legend
- ✅ **Fully Compatible**: Endpoint exists with matching parameters and structure
- ⚠️ **Partial Match**: Endpoint exists but with different parameters or structure
- ❌ **Missing in C#**: Endpoint defined in TypeScript spec but not implemented in C#
- 🔵 **C# Only**: Endpoint exists in C# but not defined in TypeScript spec
- 🟡 **SignalR/Hub**: Real-time functionality using SignalR instead of REST

---

## 1. Playback Control APIs

### 1.1 Playback State Management

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /playback/state` | `GET /api/audio` | ⚠️ | C# uses `/api/audio` instead of `/api/playback/state`. Returns PlaybackStateDto with more detailed information including capabilities |
| `PUT /playback/state` | `POST /api/audio` | ⚠️ | C# uses POST instead of PUT and has more detailed request model (UpdatePlaybackRequest) |
| `POST /playback/play` | `POST /api/audio` with `Action=Play` | ⚠️ | C# uses unified endpoint with Action parameter instead of separate play endpoint |
| `POST /playback/pause` | `POST /api/audio` with `Action=Pause` | ⚠️ | C# uses unified endpoint with Action parameter instead of separate pause endpoint |
| `POST /playback/next` | `POST /api/audio/next` | ✅ | Fully compatible |
| `POST /playback/previous` | `POST /api/audio/previous` | ✅ | Fully compatible |
| `POST /playback/seek` | `POST /api/audio` with `Action=Seek, SeekPosition` | ⚠️ | C# uses unified endpoint instead of separate seek endpoint |
| `PUT /playback/volume` | `POST /api/audio/volume/{volume}` | ⚠️ | C# uses POST with path parameter instead of PUT with body |

**Additional C# Endpoints Not in TypeScript Spec:**
- 🔵 `POST /api/audio/start` - Start audio engine
- 🔵 `POST /api/audio/stop` - Stop audio engine
- 🔵 `GET /api/audio/volume` - Get volume separately
- 🔵 `POST /api/audio/mute` - Toggle mute
- 🔵 `POST /api/audio/shuffle` - Set shuffle mode
- 🔵 `POST /api/audio/repeat` - Set repeat mode
- 🔵 `GET /api/audio/nowplaying` - Get now playing with detailed metadata

### 1.2 Recommendations for Normalization

**Option A: Expand C# to Match TypeScript (Recommended)**
- Add separate endpoints for play, pause, seek at `/api/playback/*` routes
- Add `/api/playback/state` as alias or replacement for `/api/audio`
- Change volume endpoint to accept JSON body instead of path parameter
- Keep the unified `/api/audio` POST endpoint for backward compatibility

**Option B: Update TypeScript to Match C# Implementation**
- Consolidate to single `/api/audio` POST endpoint with action parameter
- Use `/api/audio/next`, `/api/audio/previous` pattern
- This would simplify the API but diverge from RESTful conventions

**Recommended Approach: Option A** - The TypeScript spec follows better REST conventions with specific endpoints for each action. We should add REST-style endpoints to C# while maintaining the unified POST endpoint for internal use.

---

## 2. Audio Source Management APIs

### 2.1 Source Selection

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /sources/active` | `GET /api/sources/primary` | ⚠️ | C# endpoint name differs but functionality matches |
| `PUT /sources/active` | `POST /api/sources` | ⚠️ | C# uses POST instead of PUT and returns 501 (not yet implemented) |
| `GET /sources/available` | `GET /api/sources` | ✅ | C# returns AvailableSourcesDto with both available and active sources |

**Additional C# Endpoints Not in TypeScript Spec:**
- 🔵 `GET /api/sources/active` - Get all active sources (primary + event sources)
- 🔵 `GET /api/sources/events` - Get event sources only

### 2.2 Recommendations for Normalization

**Action Required:**
1. Complete implementation of `POST /api/sources` (currently returns 501)
2. Add alias routes to match TypeScript naming:
   - `GET /api/sources/active` → return just the active primary source
   - `PUT /api/sources/active` → alias for `POST /api/sources`
3. TypeScript spec should document separate `/sources/primary` and `/sources/events` endpoints

---

## 3. Radio Control APIs

### 3.1 Radio Operations

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `POST /radio/tune` | `POST /api/radio/frequency` | ⚠️ | Different endpoint path; C# requires SetFrequencyRequest with just frequency, TS requires both frequency and band |
| `GET /radio/state` | `GET /api/radio/state` | ✅ | Fully compatible |
| `POST /radio/scan` | `POST /api/radio/scan/start` | ⚠️ | C# has more specific path and request structure |
| `GET /radio/presets` | ❌ | ❌ | Not implemented in C# |
| `POST /radio/presets` | ❌ | ❌ | Not implemented in C# |
| `DELETE /radio/presets/{id}` | ❌ | ❌ | Not implemented in C# |

**Additional C# Endpoints Not in TypeScript Spec:**
- 🔵 `POST /api/radio/frequency/up` - Step frequency up
- 🔵 `POST /api/radio/frequency/down` - Step frequency down
- 🔵 `POST /api/radio/band` - Set band separately
- 🔵 `POST /api/radio/step` - Set frequency step size
- 🔵 `POST /api/radio/scan/stop` - Stop scanning
- 🔵 `POST /api/radio/eq` - Set equalizer mode
- 🔵 `POST /api/radio/volume` - Set device-specific volume

### 3.2 Recommendations for Normalization

**Critical Missing Feature:**
- **Radio Presets** - The entire preset management system is missing from C#. This requires:
  1. Data model for radio presets (RadioPreset class)
  2. Repository interface (IRadioPresetRepository)
  3. Implementation (JSON/SQLite)
  4. Controller endpoints matching TypeScript spec

**Action Required:**
1. Implement complete radio preset system
2. Add `/api/radio/tune` endpoint that accepts both frequency and band (create TuneRequest DTO)
3. Simplify `/api/radio/scan` to match TypeScript (merge start/stop into single endpoint with optional action)
4. Add TypeScript spec entries for frequency/up, frequency/down, band, step, eq, volume endpoints

---

## 4. Spotify Integration APIs

### 4.1 Spotify Operations

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /spotify/auth` | `GET /api/spotify/auth/status` | ⚠️ | Different endpoint path, compatible response |
| `GET /spotify/search` | `GET /api/spotify/search` | ✅ | Fully compatible with query parameters |
| `POST /spotify/play` | `POST /api/spotify/play` | ✅ | Fully compatible |
| `GET /spotify/playlists` | `GET /api/spotify/playlists/user` | ⚠️ | C# path is more specific |

**Additional C# Endpoints Not in TypeScript Spec:**
- 🔵 `GET /api/spotify/auth/url` - Get OAuth authorization URL
- 🔵 `GET /api/spotify/auth/callback` - Handle OAuth callback
- 🔵 `POST /api/spotify/auth/logout` - Logout from Spotify
- 🔵 `GET /api/spotify/browse/categories` - Get browse categories
- 🔵 `GET /api/spotify/browse/category/{id}/playlists` - Get category playlists
- 🔵 `GET /api/spotify/playlists/{id}` - Get playlist details

### 4.2 Recommendations for Normalization

**Action Required:**
1. Add authentication endpoints to TypeScript spec (auth/url, auth/callback, auth/logout, auth/status)
2. Add browse and playlist detail endpoints to TypeScript spec
3. Change C# `/api/spotify/playlists/user` to `/api/spotify/playlists` for simplicity, or add alias
4. TypeScript spec should clarify that `GET /spotify/auth` returns authentication status

---

## 5. File Management APIs

### 5.1 File Operations

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /files` | ❌ | ❌ | Not implemented in C# |
| `POST /files/play` | ❌ | ❌ | Not implemented in C# |
| `POST /files/queue` | ❌ | ❌ | Not implemented in C# |

### 5.2 Recommendations for Normalization

**Critical Missing Feature:**
- **File Management System** - Entire file browsing and playback system is missing from C#. This requires:
  1. File source implementation (likely part of FilePlayerAudioSource)
  2. File browser service to scan directories
  3. Controller endpoints for:
     - `GET /api/files` - List audio files with optional path and recursive parameters
     - `POST /api/files/play` - Play a specific file
     - `POST /api/files/queue` - Add files to queue

**Action Required:**
1. Implement IFileBrowser service in Radio.Core
2. Implement file listing in Radio.Infrastructure
3. Create FilesController in Radio.API with all three endpoints
4. Add support for multiple audio formats (MP3, FLAC, WAV, etc.)

---

## 6. Metadata APIs

### 6.1 Metadata Operations

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /metadata/current` | `GET /api/audio/nowplaying` | ⚠️ | C# uses different path but similar functionality |
| `GET /metadata/history` | `GET /api/playhistory` | ⚠️ | C# uses different path and has more sophisticated query options |

**Additional C# Endpoints Not in TypeScript Spec:**
- 🔵 `GET /api/playhistory/range` - Get history by date range
- 🔵 `GET /api/playhistory/today` - Get today's history
- 🔵 `GET /api/playhistory/source/{source}` - Get history by source
- 🔵 `GET /api/playhistory/{id}` - Get specific entry
- 🔵 `GET /api/playhistory/statistics` - Get play statistics
- 🔵 `POST /api/playhistory` - Record a play entry
- 🔵 `DELETE /api/playhistory/{id}` - Delete entry

### 6.2 Recommendations for Normalization

**Action Required:**
1. Add `/api/metadata/current` as alias for `/api/audio/nowplaying`
2. Add `/api/metadata/history` as alias for `/api/playhistory` with limit parameter
3. Add all C# play history endpoints to TypeScript spec for full feature parity
4. Consider renaming C# controller from PlayHistoryController to MetadataController to match TypeScript naming

---

## 7. System Management APIs

### 7.1 System Operations

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /system/stats` | ❌ | ❌ | Not implemented in C# |
| `GET /system/config` | `GET /api/configuration` | ⚠️ | Different path, similar functionality |
| `PUT /system/config` | `POST /api/configuration` | ⚠️ | C# uses POST instead of PUT and returns 501 (not yet implemented) |
| `GET /system/logs` | ❌ | ❌ | Not implemented in C# |
| `GET /system/metrics` | `GET /api/metrics/history` | ⚠️ | Different path and parameters |

**Additional C# Endpoints Not in TypeScript Spec:**
- 🔵 `GET /api/configuration/audio` - Get audio config only
- 🔵 `GET /api/configuration/visualizer` - Get visualizer config only
- 🔵 `GET /api/configuration/output` - Get output config only
- 🔵 `GET /api/metrics/snapshots` - Get current metric snapshots
- 🔵 `GET /api/metrics/aggregate` - Get aggregate value for metric
- 🔵 `GET /api/metrics/keys` - List all metric keys

### 7.2 Recommendations for Normalization

**Action Required:**
1. **System Stats** - Implement `/api/system/stats` endpoint returning:
   - CPU usage
   - RAM usage
   - Thread count
   - Uptime
   - Audio engine state
2. **System Logs** - Implement `/api/system/logs` endpoint with:
   - Level filtering (info, warning, error)
   - Limit parameter
   - Real-time log access (consider SignalR for streaming)
3. Add `/api/system/config` and `/api/system/metrics` as aliases to configuration and metrics endpoints
4. Complete configuration update implementation (currently returns 501)
5. Add all C# configuration and metrics endpoints to TypeScript spec

---

## 8. Visualization APIs

### 8.1 Visualization Data

| TypeScript Endpoint | C# Implementation | Status | Notes |
|---------------------|-------------------|--------|-------|
| `GET /visualization/data` | 🟡 SignalR Hub | 🟡 | C# implements this via AudioVisualizationHub SignalR hub instead of REST |

**C# SignalR Implementation:**
- 🟡 `AudioVisualizationHub` at `/hubs/visualization`
  - `GetSpectrum()` - Get spectrum data
  - `GetLevels()` - Get level data  
  - `GetWaveform()` - Get waveform data
  - `GetVisualization()` - Get all data combined
  - `SubscribeToSpectrum()` - Real-time spectrum updates
  - `SubscribeToLevels()` - Real-time level updates
  - `SubscribeToWaveform()` - Real-time waveform updates
  - `SubscribeToAll()` - Subscribe to all updates

### 8.2 Recommendations for Normalization

**Action Required:**
1. Add REST endpoint `GET /api/visualization/data` for snapshot access (useful for polling clients)
2. Keep SignalR hub for real-time streaming (preferred for web UI)
3. Update TypeScript spec to document both REST and WebSocket/SignalR options
4. TypeScript implementation should prefer SignalR/WebSocket for real-time data

---

## 9. Queue Management APIs

### 9.1 Queue Operations (Not in TypeScript Spec)

**C# Endpoints Not in TypeScript Spec:**
- 🔵 `GET /api/queue` - Get current queue
- 🔵 `POST /api/queue/add` - Add to queue
- 🔵 `DELETE /api/queue/{index}` - Remove from queue
- 🔵 `DELETE /api/queue` - Clear queue
- 🔵 `POST /api/queue/move` - Move queue item
- 🔵 `POST /api/queue/jump/{index}` - Jump to index

**SignalR Implementation:**
- 🟡 `AudioStateHub` at `/hubs/state`
  - `SubscribeToQueue()` - Real-time queue updates
  - `SubscribeToRadioState()` - Real-time radio state updates

### 9.2 Recommendations for Normalization

**Action Required:**
1. Add complete queue management section to TypeScript spec:
   - `GET /queue` - Get current queue
   - `POST /queue` - Add to queue
   - `DELETE /queue/{index}` - Remove from queue
   - `DELETE /queue` - Clear queue
   - `POST /queue/move` - Reorder queue
   - `POST /queue/jump/{index}` - Jump to position
2. Document AudioStateHub for real-time updates

---

## 10. Device Management APIs (Not in TypeScript Spec)

### 10.1 Device Operations

**C# Endpoints Not in TypeScript Spec:**
- 🔵 `GET /api/devices/output` - Get output devices
- 🔵 `GET /api/devices/input` - Get input devices
- 🔵 `GET /api/devices/output/default` - Get default output device
- 🔵 `POST /api/devices/output` - Set output device
- 🔵 `POST /api/devices/refresh` - Refresh device list
- 🔵 `GET /api/devices/usb/reservations` - Get USB port reservations
- 🔵 `GET /api/devices/usb/check` - Check USB port status

### 10.2 Recommendations for Normalization

**Action Required:**
1. Add complete device management section to TypeScript spec
2. This is essential for Blazor UI to allow users to select audio output devices
3. USB management is critical for radio and vinyl sources on Raspberry Pi

---

## Summary of Required Changes

### HIGH PRIORITY - Missing Features in C#

1. **Radio Presets System** ❌
   - Data model, repository, controller implementation
   - Essential for user experience
   
2. **File Management System** ❌
   - File browser, file player integration
   - Essential for local music playback

3. **System Stats Endpoint** ❌
   - CPU, RAM, uptime monitoring
   - Important for system health monitoring

4. **System Logs Endpoint** ❌
   - Log viewing and filtering
   - Important for troubleshooting

5. **Source Switching** ⚠️
   - Complete implementation (currently returns 501)
   - Critical for switching between Spotify/Radio/Files/Vinyl

6. **Configuration Updates** ⚠️
   - Complete implementation (currently returns 501)
   - Important for runtime configuration

### MEDIUM PRIORITY - API Inconsistencies

1. **Endpoint Path Normalization**
   - Add `/api/playback/*` routes matching TypeScript spec
   - Add `/api/sources/active` matching TypeScript conventions
   - Add `/api/metadata/*` aliases for consistency
   - Add `/api/system/*` aliases for consistency

2. **HTTP Method Standardization**
   - Change volume endpoint from path parameter to body parameter
   - Use PUT for update operations where appropriate
   - Use POST for action operations consistently

3. **Add Missing TypeScript Spec Entries**
   - Document all C# authentication endpoints
   - Document queue management endpoints
   - Document device management endpoints
   - Document advanced configuration endpoints
   - Document SignalR hub operations

### LOW PRIORITY - Enhancements

1. **Separate Action Endpoints**
   - Add individual /play, /pause endpoints while keeping unified endpoint
   - Improves REST compliance and clarity

2. **Documentation Improvements**
   - Add OpenAPI/Swagger documentation to C# API
   - Generate TypeScript client from OpenAPI spec
   - Ensure parameter descriptions match

---

## Implementation Roadmap

### Phase 1: Critical Missing Features (Weeks 1-2)
1. Implement Radio Preset System
   - RadioPreset model
   - IRadioPresetRepository interface
   - JSON/SQLite implementations
   - Controller endpoints
2. Implement File Management System
   - IFileBrowser service
   - FilesController
   - File format support
3. Complete Source Switching implementation
4. Complete Configuration Update implementation

### Phase 2: System Monitoring (Week 3)
1. Implement System Stats endpoint
2. Implement System Logs endpoint
3. Add health check endpoints

### Phase 3: API Normalization (Week 4)
1. Add REST-style playback endpoints
2. Add endpoint aliases for consistency
3. Standardize HTTP methods and parameters
4. Update TypeScript spec with all C# endpoints

### Phase 4: Documentation (Week 5)
1. Add OpenAPI/Swagger to C# API
2. Generate TypeScript client
3. Update API documentation
4. Create integration tests

### Phase 5: Blazor Integration (Week 6+)
1. Build Blazor UI components using normalized API
2. Test all endpoints
3. Performance optimization
4. Production deployment

---

## Conclusion

The C# API implementation is quite advanced with excellent support for:
- Audio playback and control
- Spotify integration with OAuth
- Radio control
- Queue management
- Metrics and configuration
- Real-time visualization via SignalR

However, several critical features from the TypeScript spec are missing:
- Radio presets
- File management
- System monitoring (stats, logs)

The biggest gaps are in user-facing features (presets, file browsing) rather than core audio functionality. The API structure is solid but needs normalization for REST conventions and consistency with the TypeScript spec.

**Recommended Approach:**
1. Prioritize implementing missing critical features (presets, files, system monitoring)
2. Complete partially implemented features (source switching, configuration updates)
3. Add endpoint aliases for TypeScript compatibility without breaking existing C# clients
4. Update TypeScript spec to include all C#-specific features
5. Use this normalized API for the Blazor web app development

This approach ensures both the TypeScript and Blazor implementations can share a common, well-documented API surface while maintaining backward compatibility with any existing clients.
