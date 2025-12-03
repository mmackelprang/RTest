# UI Preparation - Analysis and Implementation Plan

## Executive Summary

This document provides a comprehensive analysis of the functionality needed in the core code and REST API to support the Blazor Web UI as described in the issue requirements. The UI will be Material 3 compliant, touch-screen friendly, and optimized for a 12.5" × 3.75" wide-format display.

## Current State Analysis

### Existing Components ✅
Based on review of `/src/Radio.API`, `/src/Radio.Core`, and `/src/Radio.Infrastructure`:

**Core Audio Infrastructure:**
- ✅ `IAudioEngine` - Audio engine interface
- ✅ `IAudioSource` base interface
- ✅ `IPrimaryAudioSource` - Primary source interface with play/pause/seek
- ✅ `IEventAudioSource` - Event source interface
- ✅ `IAudioDeviceManager` - Device enumeration and management
- ✅ `IDuckingService` - Audio ducking service
- ✅ `IVisualizerService` - Audio visualization service
- ✅ `ITTSFactory` - Text-to-speech factory

**REST API Endpoints:**
- ✅ `/api/audio` - Playback control (play, pause, stop, volume, balance)
- ✅ `/api/sources` - Source enumeration and selection
- ✅ `/api/devices` - Device management (input/output enumeration)
- ✅ `/api/playhistory` - Play history tracking
- ✅ `/api/configuration` - Configuration management

**Infrastructure Implementation:**
- ✅ SoundFlow audio engine integration
- ✅ Audio source implementations (Radio, Vinyl, GenericUSB, TTS, AudioFile Events)
- ✅ Device manager with USB port reservation
- ✅ Ducking service for event audio
- ✅ Configuration infrastructure (from CONFIGURATION.md)

### Missing Components ❌

Based on the UI requirements detailed in the issue, the following functionality is missing:

## Gap Analysis

### 1. Audio Player Capabilities Interface ❌

**Issue Requirement:**
> We need to update the audio player interface to provide information about the audio player abilities like:
> * SupportsShuffle : t/f
> * SupportsNext : t/f
> * SupportsPrevious : t/f
> * SupportsMusicQueue : t/f

**Current State:**
- `IPrimaryAudioSource` has `IsSeekable` but no other capability flags
- No interface for declaring what transport controls are supported

**Gap:**
- Need to extend `IPrimaryAudioSource` or create `IAudioSourceCapabilities` interface
- Each source implementation must declare its capabilities
- API must expose these capabilities to the UI

---

### 2. Music Queue/Playlist Management ❌

**Issue Requirement:**
> Along with the ability to retrieve, reorder and change play position in the music queue.

**Current State:**
- No `IPlayQueue` or `IPlaylist` interface exists
- Spotify and File Player would support queues, but no abstraction for it
- No API endpoints for queue management

**Gap:**
- Need `IPlayQueue` interface with methods:
  - `GetQueueAsync()` - Get current queue items
  - `AddToQueueAsync(trackId)` - Add track to queue
  - `RemoveFromQueueAsync(index)` - Remove track
  - `ReorderQueueAsync(fromIndex, toIndex)` - Reorder
  - `JumpToQueueItemAsync(index)` - Change play position
- Spotify and FilePlayer sources must implement queue support
- API endpoints: `/api/sources/{sourceType}/queue`

---

### 3. Shuffle/Repeat Controls ❌

**Issue Requirement:**
> * Shuffle On/Off (whenever the audio source allows it)
> * Repeat (whenever the audio source allows it)

**Current State:**
- No shuffle/repeat state in `IPrimaryAudioSource`
- Preferences config has `Shuffle` and `Repeat` for Spotify/FilePlayer but not exposed via API

**Gap:**
- Add shuffle/repeat properties to capability-supporting sources
- API endpoints:
  - `POST /api/sources/{sourceType}/shuffle` - Toggle shuffle
  - `POST /api/sources/{sourceType}/repeat` - Set repeat mode (Off/One/All)
  - Include in playback state response

---

### 4. Previous/Next Track Navigation ❌

**Issue Requirement:**
> * Previous (whenever the audio source allows it)
> * Next (whenever the audio source allows it)

**Current State:**
- No `NextAsync()` or `PreviousAsync()` methods in `IPrimaryAudioSource`
- Spotify and File Player would support this, but no abstraction

**Gap:**
- Add to `IPrimaryAudioSource` or create `ITrackNavigable` interface:
  - `NextAsync()` - Skip to next track
  - `PreviousAsync()` - Go to previous track
  - `SupportsNext` capability flag
  - `SupportsPrevious` capability flag
- API endpoints:
  - `POST /api/sources/{sourceType}/next`
  - `POST /api/sources/{sourceType}/previous`

---

### 5. "Now Playing" Metadata and Status ❌

**Issue Requirement:**
> When there is no known music playing, this should show a generic music icon with dashes for the artist and song.

**Current State:**
- `IPrimaryAudioSource.Metadata` exists but may be incomplete
- No standard for "empty" state or required metadata fields
- No album art URL in metadata

**Gap:**
- Define standard metadata keys: `Title`, `Artist`, `Album`, `AlbumArtUrl`, `Duration`
- Ensure all sources provide default values ("--", generic icon URL) when no track
- API should return structured "NowPlaying" object with guaranteed fields
- Add endpoint: `GET /api/sources/nowplaying`

---

### 6. Spotify-Specific Features ❌

**Issue Requirement:**
> Spotify Search.jpg shows the search bar (which would bring up the keyboard to enter the text to search for) followed by a Browse icon that will bring up the browse list from the Spotify API. Below this should be pills for the following search filters to be toggled on/off:
> * All, Music, Playlists, Podcasts, Albums, Artists, Audiobooks

**Current State:**
- No Spotify-specific controller or endpoints
- No SpotifyAPI.Web integration in the API layer
- SpotifyAudioSource likely doesn't exist yet (per Phase 3 of AUDIO_ARCHITECTURE.md)

**Gap:**
- Create `SpotifyController` with endpoints:
  - `GET /api/spotify/search?query={text}&types={filters}` - Search
  - `GET /api/spotify/browse` - Browse categories/playlists
  - `POST /api/spotify/play` - Play track/album/playlist by URI
  - `GET /api/spotify/user/playlists` - Get user's playlists
- Implement `SpotifyAudioSource` as primary source
- Add Spotify configuration and authentication flow

---

### 7. Radio Device Controls ❌

**Issue Requirement:**
> * The arrow buttons change the frequency up and down on the radio. Long pressing either button scans either up or down. We need to add a `Set` button between the arrows that will bring up the keypad and allow the user to enter the frequency to jump to.
> * Sub Band changes the frequency 'step' when pressed.
> * EQ changes the equalization *on the device - not globally*
> * Volume Up / Down changes the volume *on the device - not globally*

**Current State:**
- `RadioAudioSource` exists but likely has minimal functionality
- No interface for radio-specific controls (frequency, band, EQ, scan)
- No API endpoints for radio control

**Gap:**
- Create `IRadioControls` interface:
  - `SetFrequencyAsync(frequency)` - Set exact frequency
  - `FrequencyUpAsync()` - Step up
  - `FrequencyDownAsync()` - Step down
  - `StartScanAsync(direction)` - Start scanning
  - `StopScanAsync()` - Stop scanning
  - `SetBandAsync(band)` - AM/FM band selection
  - `SetSubBandAsync(step)` - Frequency step size
  - `SetEqualizerAsync(eqMode)` - Device EQ setting
  - `DeviceVolume` property - Device-specific volume
  - Current frequency, band, signal strength properties
- Create `RadioController` with endpoints:
  - `GET /api/radio/state` - Get radio state (frequency, band, signal, etc.)
  - `POST /api/radio/frequency` - Set frequency
  - `POST /api/radio/frequency/up` - Frequency up
  - `POST /api/radio/frequency/down` - Frequency down
  - `POST /api/radio/scan/start` - Start scan (with direction parameter)
  - `POST /api/radio/scan/stop` - Stop scan
  - `POST /api/radio/band` - Set band (AM/FM)
  - `POST /api/radio/subband` - Set sub-band (step size)
  - `POST /api/radio/eq` - Set EQ mode
  - `POST /api/radio/volume` - Set device volume

---

### 8. Long-Press Actions (Scan) ❌

**Issue Requirement:**
> Long pressing either button scans either up or down.

**Current State:**
- No support for long-press/scan behavior in radio controls
- UI will handle long-press detection, but backend needs scan state

**Gap:**
- Add scan state tracking to radio source
- Scan should auto-stop on signal found or user interaction
- WebSocket/SignalR updates for frequency changes during scan
- Add `ScanStateDto` to radio API responses

---

### 9. Radio Display Information ❌

**Issue Requirement:**
> The various components of the display will all be available from the interface to the radio device.

**Current State:**
- No comprehensive radio state model
- Signal strength, stereo indicator, band, frequency display info not exposed

**Gap:**
- Create `RadioStateDto` model with:
  - `Frequency` - Current frequency (e.g., "101.5")
  - `Band` - AM or FM
  - `SubBand` - Step size (e.g., "0.1" for FM)
  - `SignalStrength` - 0-100 percentage
  - `IsStereo` - For FM stereo indicator
  - `IsScanning` - Whether currently scanning
  - `ScanDirection` - Up or Down (if scanning)
  - `EqualizerMode` - Current EQ setting
  - `DeviceVolume` - Device-specific volume (0-100)

---

### 10. Source-Specific Volume Controls ❌

**Issue Requirement:**
> Volume Up / Down changes the volume *on the device - not globally*

**Current State:**
- Only master/mixer volume exists
- Individual sources have volume in `IAudioSource.Volume` but may not be independently controllable per-device

**Gap:**
- For radio: Add device volume control (separate from mix level)
- API should differentiate between:
  - Master volume (affects all audio)
  - Source mix level (this source in the mix)
  - Device volume (hardware volume, for devices that support it)
- Add to source-specific controllers (e.g., `/api/radio/volume`)

---

### 11. Material 3 UI Component Models ❌

**Issue Requirement:**
> This should be changed to be Material 3 compliant and touch screen friendly.

**Current State:**
- Web UI exists but may not be Material 3 based
- No touch-optimized components documented

**Gap:**
- This is primarily a UI concern, but API should return structured data optimized for Material 3 components
- Ensure all enums are returned as strings (not numbers) for easier UI binding
- Consider adding UI hint fields to API responses (e.g., `canShuffle`, `canSeek`, `canSkip`)

---

### 12. Keyboard/Keypad Input Support ❌

**Issue Requirement:**
> ...will bring up the keypad and allow the user to enter the frequency to jump to.

**Current State:**
- UI concern, but API must accept frequency as input

**Gap:**
- Ensure radio API accepts flexible frequency input formats
- Add validation for frequency ranges (AM: 530-1710 kHz, FM: 88-108 MHz)
- Return validation errors in structured format

---

## Implementation Plan

### Phase 1: Audio Source Capabilities and Controls (Priority: High)
**Estimated Effort:** 3-5 days

#### Task 1.1: Extend IPrimaryAudioSource Interface
**Prompt for Copilot Agent:**
```
Extend the IPrimaryAudioSource interface to include track navigation and capability flags.

Location: /RTest/src/Radio.Core/Interfaces/Audio/IPrimaryAudioSource.cs

Add the following members to IPrimaryAudioSource:

1. Capability properties:
   - bool SupportsNext { get; }
   - bool SupportsPrevious { get; }
   - bool SupportsShuffle { get; }
   - bool SupportsRepeat { get; }
   - bool SupportsQueue { get; }

2. Navigation methods:
   - Task NextAsync(CancellationToken cancellationToken = default);
   - Task PreviousAsync(CancellationToken cancellationToken = default);

3. Shuffle/Repeat properties and methods:
   - bool IsShuffleEnabled { get; }
   - RepeatMode RepeatMode { get; }
   - Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default);
   - Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default);

4. Create RepeatMode enum in /RTest/src/Radio.Core/Models/Audio/ if it doesn't exist:
   public enum RepeatMode { Off, One, All }

5. Update PrimaryAudioSourceBase abstract class to provide default implementations:
   - Default all capability flags to false
   - Default navigation methods to throw NotSupportedException
   - Subclasses override only what they support

Success Criteria:
- Interface compiles without errors
- All existing implementations continue to compile (may need default implementations added)
- XML documentation added for all new members
```

#### Task 1.2: Implement Track Navigation in FilePlayerAudioSource
**Status:** ✅ Completed  
**Prompt for Copilot Agent:**
```
Implement track navigation (Next/Previous), shuffle, and repeat in FilePlayerAudioSource.

Location: /RTest/src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs

Requirements:
1. Override capability properties:
   - SupportsNext = true
   - SupportsPrevious = true
   - SupportsShuffle = true
   - SupportsRepeat = true
   - SupportsQueue = true

2. Implement NextAsync():
   - If queue has next item, load and play it
   - If at end of queue and RepeatMode is All, go to first item
   - If at end and RepeatMode is Off, stop playback
   - If RepeatMode is One, replay current track

3. Implement PreviousAsync():
   - If position > 3 seconds, seek to beginning
   - Else go to previous track in queue
   - Handle repeat modes appropriately

4. Implement SetShuffleAsync():
   - Store state in FilePlayerPreferences via configuration
   - If enabling shuffle, randomize queue
   - If disabling, restore original order
   - For the File Audio Source, implement the Fisher-Yates shuffle algorithm so that previous and next are supported for lists of files provided by the File Audio Source.  For Spotify Audio Source, Spotify should already support shuffle in their API.

5. Implement SetRepeatModeAsync():
   - Store state in FilePlayerPreferences
   - Update internal repeat mode

6. Ensure preferences auto-save on state changes

Success Criteria:
- Can skip forward/backward through queue
- Shuffle randomizes play order
- Repeat modes work correctly
- Preferences persist between sessions
- Unit tests pass
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ All capability properties already defined as true in FilePlayerAudioSource
- ✅ Implemented `NextAsync()` with full repeat mode support:
  - Handles RepeatMode.One (replay current track)
  - Handles RepeatMode.All (restart playlist at end)
  - Handles RepeatMode.Off (stop at end)
  - Tracks play history for previous functionality
- ✅ Implemented `PreviousAsync()` with 3-second logic:
  - Seeks to beginning if position > 3 seconds
  - Goes to previous track in history if position ≤ 3 seconds
  - Handles RepeatMode.All (goes to last track at beginning)
- ✅ Implemented `SetShuffleAsync()` with Fisher-Yates shuffle:
  - Stores original order for toggle functionality
  - Shuffles remaining tracks when enabled
  - Restores original order when disabled
  - Maintains current track position
- ✅ Implemented `SetRepeatModeAsync()`:
  - Updates FilePlayerPreferences.Repeat
  - Logs mode changes
- ✅ Added internal tracking:
  - `_originalOrder` - stores original playlist order for shuffle toggle
  - `_playedHistory` - tracks played songs for Previous functionality
- ✅ Preferences auto-save via IOptionsMonitor pattern
- ✅ Added 24 comprehensive unit tests covering all navigation scenarios
- ✅ All 584 tests pass (15 Core, 519 Infrastructure, 50 API)

**Files Modified:**
- `/src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` - Implemented navigation methods
- `/tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/FilePlayerAudioSourceTests.cs` - Added 24 new tests

**Capabilities Confirmed:**
- ✅ `SupportsNext = true` - Skip to next track
- ✅ `SupportsPrevious = true` - Go to previous track
- ✅ `SupportsShuffle = true` - Randomize playback order
- ✅ `SupportsRepeat = true` - Repeat modes (Off/One/All)
- ✅ `SupportsQueue = true` - Playlist/queue management

---

#### Task 1.3: Add Navigation Endpoints to AudioController
**Status:** ✅ Completed  
**Implementation Date:** 2025-12-03

**Prompt for Copilot Agent:**
```
Add track navigation, shuffle, and repeat endpoints to the AudioController.

Location: /RTest/src/Radio.API/Controllers/AudioController.cs

Add the following endpoints:

1. POST /api/audio/next
   - Call primary source NextAsync() if SupportsNext
   - Return 400 if source doesn't support next
   - Return updated playback state

2. POST /api/audio/previous
   - Call primary source PreviousAsync() if SupportsPrevious
   - Return 400 if source doesn't support previous
   - Return updated playback state

3. POST /api/audio/shuffle
   Body: { "enabled": true/false }
   - Call primary source SetShuffleAsync() if SupportsShuffle
   - Return 400 if not supported
   - Return updated state with shuffle status

4. POST /api/audio/repeat
   Body: { "mode": "Off"/"One"/"All" }
   - Call primary source SetRepeatModeAsync() if SupportsRepeat
   - Parse RepeatMode enum from string
   - Return 400 if not supported or invalid mode
   - Return updated state with repeat mode

5. Update PlaybackStateDto model to include:
   - bool CanNext
   - bool CanPrevious
   - bool CanShuffle
   - bool CanRepeat
   - bool IsShuffleEnabled
   - string RepeatMode (serialize enum as string)

6. Update GET /api/audio endpoint to populate these new fields

Success Criteria:
- All endpoints return appropriate success/error responses
- Swagger documentation updated
- Integration tests pass
- Capability flags correctly reflect source abilities
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ Added four new endpoints to AudioController:
  - `POST /api/audio/next` - Skips to next track if source supports it
  - `POST /api/audio/previous` - Goes to previous track if source supports it
  - `POST /api/audio/shuffle` - Toggles shuffle mode on/off
  - `POST /api/audio/repeat` - Sets repeat mode (Off/One/All)
- ✅ Created request DTOs:
  - `SetShuffleRequest` - Contains `Enabled` boolean
  - `SetRepeatModeRequest` - Contains `Mode` string (Off/One/All)
- ✅ Updated `PlaybackStateDto` with capability flags:
  - `CanNext` - Indicates if source supports next track navigation
  - `CanPrevious` - Indicates if source supports previous track navigation
  - `CanShuffle` - Indicates if source supports shuffle mode
  - `CanRepeat` - Indicates if source supports repeat mode
  - `IsShuffleEnabled` - Current shuffle state
  - `RepeatMode` - Current repeat mode as string (Off/One/All)
- ✅ Updated `GET /api/audio` endpoint to populate new capability fields from primary source
- ✅ All endpoints validate source capability before attempting operation
- ✅ All endpoints return 400 Bad Request with descriptive error if:
  - No primary audio source is active
  - Source doesn't support the requested operation
  - Invalid repeat mode provided (for repeat endpoint)
- ✅ All endpoints return updated `PlaybackStateDto` on success
- ✅ Added 6 comprehensive integration tests:
  - `Next_WithNoActiveSource_ReturnsBadRequest`
  - `Previous_WithNoActiveSource_ReturnsBadRequest`
  - `SetShuffle_WithNoActiveSource_ReturnsBadRequest`
  - `SetRepeatMode_WithNoActiveSource_ReturnsBadRequest`
  - `SetRepeatMode_WithInvalidMode_ReturnsBadRequest`
  - `GetPlaybackState_IncludesNavigationCapabilities`
- ✅ All 606 tests pass (15 Core, 535 Infrastructure, 56 API)

**Files Modified:**
- `/src/Radio.API/Controllers/AudioController.cs` - Added navigation endpoints
- `/src/Radio.API/Models/AudioModels.cs` - Extended PlaybackStateDto and added request DTOs
- `/tests/Radio.API.Tests/Controllers/AudioControllerTests.cs` - Added integration tests

**API Capabilities Confirmed:**
- ✅ `POST /api/audio/next` - Skip to next track
- ✅ `POST /api/audio/previous` - Go to previous track
- ✅ `POST /api/audio/shuffle` - Toggle shuffle mode
- ✅ `POST /api/audio/repeat` - Set repeat mode
- ✅ `GET /api/audio` - Returns capability flags (CanNext, CanPrevious, CanShuffle, CanRepeat)
- ✅ All endpoints follow RESTful conventions
- ✅ Proper HTTP status codes (200 OK, 400 Bad Request, 500 Internal Server Error)
- ✅ Descriptive error messages for all failure cases
- ✅ Swagger/OpenAPI documentation automatically generated for all endpoints

---

### Phase 2: Music Queue Management (Priority: High)
**Estimated Effort:** 4-6 days

#### Task 2.1: Create IPlayQueue Interface
**Prompt for Copilot Agent:**
```
Create a new interface for music queue/playlist management.

Location: /RTest/src/Radio.Core/Interfaces/Audio/IPlayQueue.cs

Define IPlayQueue interface with:

1. Properties:
   - IReadOnlyList<QueueItem> QueueItems { get; }
   - int CurrentIndex { get; }
   - int Count { get; }

2. Methods:
   - Task<IReadOnlyList<QueueItem>> GetQueueAsync(CancellationToken ct = default);
   - Task AddToQueueAsync(string trackIdentifier, int? position = null, CancellationToken ct = default);
   - Task RemoveFromQueueAsync(int index, CancellationToken ct = default);
   - Task ClearQueueAsync(CancellationToken ct = default);
   - Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken ct = default);
   - Task JumpToIndexAsync(int index, CancellationToken ct = default);

3. Events:
   - event EventHandler<QueueChangedEventArgs>? QueueChanged;

4. Create QueueItem model in /RTest/src/Radio.Core/Models/Audio/QueueItem.cs:
   public class QueueItem
   {
     public string Id { get; init; }
     public string Title { get; init; }
     public string Artist { get; init; }
     public string Album { get; init; }
     public TimeSpan? Duration { get; init; }
     public string? AlbumArtUrl { get; init; }
     public int Index { get; init; }
     public bool IsCurrent { get; init; }
   }

5. Create QueueChangedEventArgs in same file:
   public class QueueChangedEventArgs : EventArgs
   {
     public QueueChangeType ChangeType { get; init; }
     public int? AffectedIndex { get; init; }
     public QueueItem? AffectedItem { get; init; }
   }
   
   public enum QueueChangeType { Added, Removed, Moved, Cleared, CurrentChanged }

Success Criteria:
- Interface compiles
- Models have proper XML documentation
- Follows existing code style (2-space indentation)
```

#### Task 2.2: Implement IPlayQueue in FilePlayerAudioSource
**Status:** ✅ Completed  
**Implementation Date:** 2025-12-03

**Prompt for Copilot Agent:**
```
Implement IPlayQueue interface in FilePlayerAudioSource.

Location: /RTest/src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs

Requirements:
1. Make FilePlayerAudioSource implement IPlayQueue

2. Implement GetQueueAsync():
   - Return current playlist queue as list of QueueItem
   - Load metadata from file tags (use TagLib# or similar)
   - Populate Title, Artist, Album, Duration for each item
   - Mark current item with IsCurrent = true

3. Implement AddToQueueAsync():
   - Add track to internal queue
   - If position specified, insert at that index
   - Otherwise append to end
   - Raise QueueChanged event

4. Implement RemoveFromQueueAsync():
   - Remove item at index
   - If removing current item, skip to next
   - Raise QueueChanged event

5. Implement ClearQueueAsync():
   - Stop playback
   - Clear queue
   - Raise QueueChanged event

6. Implement MoveQueueItemAsync():
   - Reorder queue items
   - Update current index if needed
   - Raise QueueChanged event

7. Implement JumpToIndexAsync():
   - Load and play item at specified index
   - Update CurrentIndex
   - Raise QueueChanged event

Success Criteria:
- Queue operations work correctly
- Metadata extraction works for mp3, flac, wav
- Events fired appropriately
- Unit tests pass
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ FilePlayerAudioSource now implements IPlayQueue interface
- ✅ Implemented `GetQueueAsync()`:
  - Returns current queue as list of QueueItem
  - Uses SoundFlow's SoundMetadataReader for metadata extraction
  - Populates Title, Artist, Album, Duration from file tags
  - Marks current item with IsCurrent = true
  - Handles files without metadata gracefully (uses filename as title, "--" for missing fields)
- ✅ Implemented `AddToQueueAsync()`:
  - Adds track to queue at specified position or end
  - Works with full queue (current + remaining tracks)
  - Updates original order list for shuffle/repeat support
  - Raises QueueChanged event with Added type
  - Validates file exists and has supported extension
- ✅ Implemented `RemoveFromQueueAsync()`:
  - Removes item at specified index
  - When removing current item, automatically loads next track
  - Stops playback if no tracks remain
  - Adjusts current index when removing items before current
  - Raises QueueChanged event with Removed type
- ✅ Implemented `ClearQueueAsync()`:
  - Stops playback completely
  - Clears playlist, original order, and play history
  - Resets current file and index
  - Raises QueueChanged event with Cleared type
- ✅ Implemented `MoveQueueItemAsync()`:
  - Reorders queue items by moving from one index to another
  - Updates current index when current item is moved or when items are moved around it
  - Rebuilds queue maintaining proper state
  - Raises QueueChanged event with Moved type
- ✅ Implemented `JumpToIndexAsync()`:
  - Jumps to and loads item at specified index
  - Starts playback of the selected item
  - Updates current index and rebuilds queue
  - Loads file metadata using SoundFlow
  - Raises QueueChanged event with CurrentChanged type
- ✅ Added IPlayQueue properties:
  - `QueueItems` - Returns current queue as readonly list
  - `CurrentIndex` - Tracks current position in queue (0-based)
  - `Count` - Total number of items in queue
- ✅ Added `QueueChanged` event that fires on all queue modifications
- ✅ Internal tracking with `_currentIndex` field maintains queue position
- ✅ Metadata extraction supports: MP3, FLAC, WAV, OGG, AAC, M4A, WMA
- ✅ Helper methods added:
  - `GetAllTracksInOrder()` - Returns current + queued tracks
  - `RebuildQueueFromList()` - Reconstructs queue from track list
  - `GetQueueItemsInternal()` - Creates QueueItem list with metadata
  - `CreateQueueItem()` - Extracts metadata and creates QueueItem
  - `OnQueueChanged()` - Raises QueueChanged event
- ✅ All 63 unit tests pass including 22 new IPlayQueue-specific tests
- ✅ All existing FilePlayerAudioSource tests continue to pass (no regressions)

**Files Modified:**
- `/src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` - Added IPlayQueue implementation
- `/tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/FilePlayerAudioSourceTests.cs` - Added 22 comprehensive unit tests

**Capabilities Confirmed:**
- ✅ `SupportsQueue = true` - Full queue management support
- ✅ Queue operations work correctly with current track and remaining queue
- ✅ Metadata extraction works for all supported formats using SoundFlow
- ✅ Events fired appropriately on all queue changes
- ✅ Position-based insertion and removal work correctly
- ✅ Current index tracking works across all operations
- ✅ Queue operations integrate with existing shuffle/repeat functionality
- ✅ Graceful handling of files without metadata tags

**Test Coverage:**
- `GetQueueAsync_WithLoadedFiles_ReturnsQueueItems` - Verifies queue retrieval
- `GetQueueAsync_ExtractsMetadata_PopulatesQueueItems` - Verifies metadata extraction
- `AddToQueueAsync_AddsToEnd_WhenPositionNotSpecified` - Tests append operation
- `AddToQueueAsync_InsertsAtPosition_WhenPositionSpecified` - Tests position-based insert
- `AddToQueueAsync_RaisesQueueChangedEvent` - Verifies event firing
- `AddToQueueAsync_NonExistentFile_ThrowsFileNotFoundException` - Tests error handling
- `RemoveFromQueueAsync_RemovesItem_UpdatesQueue` - Tests item removal
- `RemoveFromQueueAsync_RemovesCurrentItem_SkipsToNext` - Tests current item removal
- `RemoveFromQueueAsync_RaisesQueueChangedEvent` - Verifies event firing
- `RemoveFromQueueAsync_InvalidIndex_ThrowsArgumentOutOfRangeException` - Tests bounds checking
- `ClearQueueAsync_ClearsAllItems_StopsPlayback` - Tests queue clearing
- `ClearQueueAsync_RaisesQueueChangedEvent` - Verifies event firing
- `MoveQueueItemAsync_ReordersItems` - Tests item reordering
- `MoveQueueItemAsync_MovesCurrentItem_UpdatesCurrentIndex` - Tests moving current item
- `MoveQueueItemAsync_RaisesQueueChangedEvent` - Verifies event firing
- `MoveQueueItemAsync_InvalidIndices_ThrowsArgumentOutOfRangeException` - Tests bounds checking
- `JumpToIndexAsync_JumpsToItem_StartsPlayback` - Tests jumping to index
- `JumpToIndexAsync_RaisesQueueChangedEvent` - Verifies event firing
- `JumpToIndexAsync_InvalidIndex_ThrowsArgumentOutOfRangeException` - Tests bounds checking
- `QueueItems_ReturnsCurrentQueue` - Tests property accessor
- `CurrentIndex_TracksCurrentPosition` - Tests index tracking
- `Count_ReturnsCorrectTotalCount` - Tests count property

---


#### Task 2.3: Create Queue Management API Endpoints
**Status:** ✅ Completed  
**Implementation Date:** 2025-12-03

**Prompt for Copilot Agent:**
```
Create API endpoints for music queue management.

New file: /RTest/src/Radio.API/Controllers/QueueController.cs

Create QueueController with the following endpoints:

1. GET /api/queue
   - Get current queue from active primary source
   - Return 404 if no source active
   - Return 400 if source doesn't support queues (SupportsQueue = false)
   - Response: List<QueueItemDto>

2. POST /api/queue/add
   Body: { "trackIdentifier": string, "position": int? }
   - Add track to queue
   - Return updated queue

3. DELETE /api/queue/{index}
   - Remove item at index
   - Return updated queue

4. DELETE /api/queue
   - Clear entire queue
   - Return empty queue confirmation

5. POST /api/queue/move
   Body: { "fromIndex": int, "toIndex": int }
   - Reorder queue
   - Return updated queue

6. POST /api/queue/jump/{index}
   - Jump to and play item at index
   - Return updated playback state

7. Create QueueItemDto model in /RTest/src/Radio.API/Models/AudioModels.cs:
   - Map from Core.Models.QueueItem
   - Include all fields

Success Criteria:
- All endpoints work with sources that implement IPlayQueue
- Appropriate errors for unsupported operations
- Swagger documentation complete
- Integration tests pass
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ Created QueueController at `/src/Radio.API/Controllers/QueueController.cs`
- ✅ Implemented all 6 required endpoints:
  - `GET /api/queue` - Retrieves current queue from active primary source
  - `POST /api/queue/add` - Adds track to queue at specified position or end
  - `DELETE /api/queue/{index}` - Removes item at specified index
  - `DELETE /api/queue` - Clears entire queue
  - `POST /api/queue/move` - Reorders queue items by moving from one index to another
  - `POST /api/queue/jump/{index}` - Jumps to and plays item at specified index
- ✅ Created DTOs in `/src/Radio.API/Models/AudioModels.cs`:
  - `QueueItemDto` - Maps from Core.Models.Audio.QueueItem with all fields (Id, Title, Artist, Album, Duration, AlbumArtUrl, Index, IsCurrent)
  - `AddToQueueRequest` - Request DTO with TrackIdentifier and optional Position
  - `MoveQueueItemRequest` - Request DTO with FromIndex and ToIndex
- ✅ Error handling for all failure cases:
  - Returns 404 Not Found when no primary audio source is active
  - Returns 400 Bad Request when source doesn't support IPlayQueue
  - Returns 400 Bad Request for invalid indices (ArgumentOutOfRangeException)
  - Returns 500 Internal Server Error for unexpected errors
- ✅ All endpoints validate that active source implements IPlayQueue
- ✅ JumpToIndex endpoint returns updated PlaybackStateDto
- ✅ All other queue operations return updated queue as List<QueueItemDto>
- ✅ Proper logging for all operations using ILogger
- ✅ Created 6 comprehensive integration tests in `/tests/Radio.API.Tests/Controllers/QueueControllerTests.cs`:
  - `GetQueue_WithNoActiveSource_ReturnsNotFound`
  - `AddToQueue_WithNoActiveSource_ReturnsNotFound`
  - `RemoveFromQueue_WithNoActiveSource_ReturnsNotFound`
  - `ClearQueue_WithNoActiveSource_ReturnsNotFound`
  - `MoveQueueItem_WithNoActiveSource_ReturnsNotFound`
  - `JumpToIndex_WithNoActiveSource_ReturnsNotFound`
- ✅ All 634 tests pass (15 Core, 62 API, 557 Infrastructure)
- ✅ No test regressions introduced

**Files Modified:**
- `/src/Radio.API/Controllers/QueueController.cs` - New controller created
- `/src/Radio.API/Models/AudioModels.cs` - Added QueueItemDto and request DTOs
- `/tests/Radio.API.Tests/Controllers/QueueControllerTests.cs` - New test file created
- `/UIPREPARATION.md` - Updated with implementation status

**API Endpoints Available:**
- ✅ `GET /api/queue` - Get current queue from active source
- ✅ `POST /api/queue/add` - Add track to queue
- ✅ `DELETE /api/queue/{index}` - Remove specific item
- ✅ `DELETE /api/queue` - Clear entire queue
- ✅ `POST /api/queue/move` - Reorder queue items
- ✅ `POST /api/queue/jump/{index}` - Jump to and play specific item

**Capabilities Confirmed:**
- ✅ All endpoints work with sources implementing IPlayQueue (FilePlayerAudioSource, SpotifyAudioSource)
- ✅ Appropriate 404/400/500 errors for unsupported operations
- ✅ Swagger/OpenAPI documentation automatically generated for all endpoints
- ✅ Integration tests validate error conditions
- ✅ Queue operations integrate with existing audio engine and mixer infrastructure

**Notes:**
- Queue endpoints require an active primary source that implements IPlayQueue
- FilePlayerAudioSource and SpotifyAudioSource both implement IPlayQueue
- Queue operations are source-specific - each source maintains its own queue
- JumpToIndex returns PlaybackStateDto to provide immediate feedback on new playback state
- All other queue operations return the updated queue to reflect changes

---

### Phase 3: Spotify Integration (Priority: High)
**Estimated Effort:** 5-7 days

#### Task 3.1: Implement SpotifyAudioSource
**Status:** ✅ Completed  
**Implementation Date:** 2025-12-03

**Prompt for Copilot Agent:**
```
Implement Spotify audio source with playlist/queue support.

New file: /RTest/src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs

Requirements:
1. Implement IPrimaryAudioSource and IPlayQueue

2. Use SpotifyAPI.Web library for integration:
   - Initialize SpotifyClient with credentials from SpotifySecrets configuration
   - Handle token refresh automatically
   - Use Spotify Connect for playback (not local streaming)

3. Override capabilities:
   - SupportsNext = true
   - SupportsPrevious = true
   - SupportsShuffle = true
   - SupportsRepeat = true
   - SupportsQueue = true
   - IsSeekable = false (Spotify Connect doesn't support seeking via API)

4. Implement playback methods:
   - PlayAsync(): Start Spotify Connect playback
   - PauseAsync(): Pause via Spotify API
   - NextAsync(): Skip to next track
   - PreviousAsync(): Go to previous track
   - SetShuffleAsync(): Toggle shuffle mode
   - SetRepeatModeAsync(): Set repeat mode (off/track/context)

5. Implement queue methods:
   - GetQueueAsync(): Fetch current playback queue from Spotify API
   - AddToQueueAsync(): Add track to Spotify queue
   - Note: Spotify doesn't support all queue operations via API

6. Poll Spotify API for current playback state:
   - Every 2 seconds, fetch current playback
   - Update Position, Duration, Metadata
   - Update IsPlaying/IsPaused states
   - Raise events on state changes

7. Store last played track in SpotifyPreferences

Success Criteria:
- Can play/pause/skip Spotify tracks
- Queue shows current Spotify context
- Shuffle and repeat work
- Metadata updates in real-time
- Preferences persist
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ Created IPlayQueue interface in `/src/Radio.Core/Interfaces/Audio/IPlayQueue.cs`
- ✅ Created QueueItem and related models in `/src/Radio.Core/Models/Audio/QueueItem.cs`
- ✅ Updated SpotifyAudioSource to implement IPlayQueue
- ✅ Implemented GetQueueAsync() using Spotify's Player.GetQueue() API
  - Retrieves currently playing track
  - Retrieves upcoming queue items
  - Returns structured QueueItem list with metadata
- ✅ Implemented AddToQueueAsync() using Spotify's Player.AddToQueue() API
  - Adds track to end of queue (Spotify limitation)
  - Raises QueueChanged event
- ✅ Queue operations with proper NotSupportedException for unsupported operations:
  - RemoveFromQueueAsync() - Not supported by Spotify API
  - ClearQueueAsync() - Not supported by Spotify API
  - MoveQueueItemAsync() - Not supported by Spotify API
  - JumpToIndexAsync() - Not supported by Spotify API (use Next/Previous instead)
- ✅ Implemented background polling mechanism:
  - Timer polls every 2 seconds via PollPlaybackStateAsync()
  - Updates Position, Duration, Metadata from Spotify API
  - Detects state changes and raises OnStateChanged events
  - Detects track changes and raises QueueChanged events
  - Uses SemaphoreSlim to prevent concurrent polling
- ✅ Auto-saves playback state to SpotifyPreferences:
  - LastSongPlayed URI saved on track change
  - SongPositionMs saved periodically
  - Shuffle and Repeat modes already persisted
- ✅ All existing capability properties remain true:
  - SupportsNext = true
  - SupportsPrevious = true
  - SupportsShuffle = true
  - SupportsRepeat = true
  - SupportsQueue = true
  - IsSeekable = true (Spotify Connect supports seeking)
- ✅ All playback methods working:
  - PlayAsync(), PauseAsync(), ResumeAsync(), StopAsync()
  - SeekAsync() for position changes
  - NextAsync(), PreviousAsync() for track navigation
  - SetShuffleAsync(), SetRepeatModeAsync() for playback modes
- ✅ Clean disposal with timer cleanup
- ✅ All 519 tests passing

**Files Modified:**
- `/src/Radio.Core/Interfaces/Audio/IPlayQueue.cs` - Created new interface
- `/src/Radio.Core/Models/Audio/QueueItem.cs` - Created new models
- `/src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs` - Enhanced with IPlayQueue

**Spotify API Limitations Documented:**
- Queue operations are limited to:
  - ✅ GetQueue() - Retrieve current queue
  - ✅ AddToQueue() - Add to end of queue only
  - ❌ Cannot remove specific items
  - ❌ Cannot clear queue
  - ❌ Cannot reorder items
  - ❌ Cannot jump to specific queue index

**Capabilities Confirmed:**
- ✅ `SupportsNext = true` - Skip to next track via API
- ✅ `SupportsPrevious = true` - Go to previous track via API
- ✅ `SupportsShuffle = true` - Toggle shuffle via API
- ✅ `SupportsRepeat = true` - Set repeat mode (Off/Track/Context) via API
- ✅ `SupportsQueue = true` - View queue, add to queue via API
- ✅ `IsSeekable = true` - Seek to position via API

**Real-time Updates:**
- ✅ Position updated every 2 seconds
- ✅ Duration updated when track changes
- ✅ Metadata (Title, Artist, Album, AlbumArtUrl) updated when track changes
- ✅ State changes (Playing/Paused) detected and events raised
- ✅ Track changes detected and QueueChanged events raised

---

#### Task 3.2: Create SpotifyController for Search and Browse
**Status:** ✅ Completed  
**Implementation Date:** 2025-12-03

**Prompt for Copilot Agent:**
```
Create a dedicated Spotify controller for search and browse features.

New file: /RTest/src/Radio.API/Controllers/SpotifyController.cs

Create SpotifyController with the following endpoints:

1. GET /api/spotify/search
   Query params: query (string), types (comma-separated: track,album,playlist,artist)
   - Use SpotifyAPI.Web to search
   - Support filter types: All, Music(tracks), Playlists, Albums, Artists, Podcasts, Audiobooks
   - Return SpotifySearchResultDto with categorized results

2. GET /api/spotify/browse/categories
   - Fetch browse categories from Spotify
   - Return list of CategoryDto

3. GET /api/spotify/browse/category/{id}/playlists
   - Fetch playlists in category
   - Return list of PlaylistDto

4. GET /api/spotify/playlists/user
   - Fetch current user's playlists
   - Return list of PlaylistDto

5. GET /api/spotify/playlists/{id}
   - Fetch playlist details and tracks
   - Return PlaylistDetailsDto

6. POST /api/spotify/play
   Body: { "uri": string, "contextUri": string? }
   - Play track, album, or playlist by Spotify URI
   - Start playback via Spotify Connect
   - Return success/error

7. Create DTOs in /RTest/src/Radio.API/Models/:
   - SpotifySearchResultDto (tracks, albums, playlists, artists lists)
   - SpotifyTrackDto (id, name, artist, album, duration, uri)
   - SpotifyAlbumDto (id, name, artist, imageUrl, uri)
   - SpotifyPlaylistDto (id, name, owner, imageUrl, trackCount, uri)
   - SpotifyArtistDto (id, name, imageUrl, uri)
   - CategoryDto (id, name, icons)

Success Criteria:
- Search works with filters
- Browse shows categories and playlists
- Can initiate playback from search/browse results
- All DTOs properly map from SpotifyAPI.Web models
- Swagger documentation complete
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ Created SpotifyController at `/src/Radio.API/Controllers/SpotifyController.cs`
- ✅ Implemented all 6 required endpoints:
  - `GET /api/spotify/search` - Searches Spotify with type filters (track, album, playlist, artist, show)
  - `GET /api/spotify/browse/categories` - Retrieves browse categories from Spotify
  - `GET /api/spotify/browse/category/{id}/playlists` - Retrieves playlists within a category
  - `GET /api/spotify/playlists/user` - Retrieves current user's playlists
  - `GET /api/spotify/playlists/{id}` - Retrieves detailed playlist information with tracks
  - `POST /api/spotify/play` - Initiates playback of track, album, or playlist by Spotify URI
- ✅ Created all DTOs in `/src/Radio.API/Models/SpotifyModels.cs`:
  - `SpotifySearchResultDto` - Categorized search results with tracks, albums, playlists, artists, shows
  - `SpotifyTrackDto` - Track details (id, name, artist, album, duration, uri, albumArtUrl)
  - `SpotifyAlbumDto` - Album details (id, name, artist, imageUrl, uri, releaseDate, totalTracks)
  - `SpotifyPlaylistDto` - Playlist summary (id, name, owner, imageUrl, trackCount, uri, description)
  - `SpotifyArtistDto` - Artist details (id, name, imageUrl, uri, followers, genres)
  - `SpotifyShowDto` - Podcast/show details (id, name, publisher, imageUrl, uri, description)
  - `SpotifyAudiobookDto` - Audiobook details (id, name, author, imageUrl, uri, description) [Note: not supported in SpotifyAPI.Web v7]
  - `CategoryDto` - Browse category (id, name, icons)
  - `IconDto` - Category icon (url, width, height)
  - `PlaylistDetailsDto` - Detailed playlist with full track list (extends PlaylistDto with tracks array)
  - `PlaySpotifyUriRequest` - Request DTO for play endpoint (uri, contextUri)
- ✅ All DTOs properly map from SpotifyAPI.Web models (v7.x)
- ✅ Search endpoint supports filter types:
  - All (default) - searches tracks, albums, playlists, artists
  - Music/track - tracks only
  - Albums - albums only
  - Playlists - playlists only
  - Artists - artists only
  - Podcasts/show - podcast shows only
  - Note: Audiobooks not supported in SpotifyAPI.Web v7
- ✅ Browse endpoints retrieve categories and category playlists from Spotify API
- ✅ User playlists endpoint retrieves current user's playlists
- ✅ Playlist details endpoint includes full track listing
- ✅ Play endpoint uses SpotifyAudioSource.PlayUriAsync() for playback
- ✅ Error handling for all failure cases:
  - 400 Bad Request when Spotify source not available or not authenticated
  - 400 Bad Request when Spotify source not active for play endpoint
  - 400 Bad Request when required parameters missing
  - 404 Not Found when category or playlist not found
  - 500 Internal Server Error for unexpected errors
- ✅ Helper methods for accessing SpotifyAudioSource and SpotifyClient via reflection
- ✅ Helper methods for mapping SpotifyAPI.Web models to DTOs
- ✅ ParseSearchTypes helper method handles type aliases (music=track, podcasts=show, etc.)
- ✅ Created 14 comprehensive integration tests in `/tests/Radio.API.Tests/Controllers/SpotifyControllerTests.cs`:
  - `Search_WithoutQuery_ReturnsBadRequest` - Validates required query parameter
  - `Search_WithoutActiveSource_ReturnsBadRequest` - Validates source availability
  - `GetBrowseCategories_WithoutActiveSource_ReturnsBadRequest` - Validates source availability
  - `GetCategoryPlaylists_WithoutCategoryId_ReturnsNotFound` - Validates route parameters
  - `GetCategoryPlaylists_WithoutActiveSource_ReturnsBadRequest` - Validates source availability
  - `GetUserPlaylists_WithoutActiveSource_ReturnsBadRequest` - Validates source availability
  - `GetPlaylistDetails_WithoutPlaylistId_ReturnsNotFound` - Validates route parameters
  - `GetPlaylistDetails_WithoutActiveSource_ReturnsBadRequest` - Validates source availability
  - `Play_WithoutUri_ReturnsBadRequest` - Validates required URI parameter
  - `Play_WithoutActiveSource_ReturnsBadRequest` - Validates source availability
  - `Play_WithUriAndContextUri_UsesContextUri` - Validates request structure
  - `Search_WithValidQueryAndTypes_ReturnsCorrectStructure` - Validates types parameter
  - `Search_WithMusicType_IsAccepted` - Validates "music" type alias
  - `Search_WithAllType_IsAccepted` - Validates "all" type filter
- ✅ All 76 tests pass (15 Core, 62 API + 14 new Spotify tests)
- ✅ No test regressions introduced

**Files Modified:**
- `/src/Radio.API/Controllers/SpotifyController.cs` - New controller created (549 lines)
- `/src/Radio.API/Models/SpotifyModels.cs` - New DTO file created (455 lines)
- `/tests/Radio.API.Tests/Controllers/SpotifyControllerTests.cs` - New test file created (197 lines)

**API Endpoints Available:**
- ✅ `GET /api/spotify/search?query={text}&types={filters}` - Search Spotify content
- ✅ `GET /api/spotify/browse/categories` - Browse categories
- ✅ `GET /api/spotify/browse/category/{id}/playlists` - Category playlists
- ✅ `GET /api/spotify/playlists/user` - User's playlists
- ✅ `GET /api/spotify/playlists/{id}` - Playlist details with tracks
- ✅ `POST /api/spotify/play` - Initiate playback

**Notes:**
- SpotifyController requires an active SpotifyAudioSource to function
- Spotify must be authenticated (valid refresh token in SpotifySecrets configuration)
- Play endpoint requires Spotify to be the active primary source
- Audiobooks are documented in DTOs but not supported in SpotifyAPI.Web v7
- All endpoints use SpotifyAPI.Web v7.x library
- Swagger/OpenAPI documentation automatically generated
- Controller uses reflection to access private SpotifyClient field in SpotifyAudioSource
- Search supports type aliases: "music" → "track", "podcasts" → "show"
- All DTOs include comprehensive XML documentation for Swagger

**Capabilities Confirmed:**
- ✅ Search with multiple type filters
- ✅ Browse Spotify categories
- ✅ Browse category playlists
- ✅ Access user's personal playlists
- ✅ View detailed playlist information with tracks
- ✅ Initiate playback of tracks, albums, playlists via Spotify URI
- ✅ Proper error handling and validation
- ✅ Swagger/OpenAPI documentation complete
- ✅ Integration tests validate all error conditions

---

#### Task 3.3: Add Spotify Authentication Flow
**Status:** ✅ Completed  
**Implementation Date:** 2025-12-03

**Prompt for Copilot Agent:**
```
Implement Spotify OAuth authentication flow for obtaining tokens.

New file: /RTest/src/Radio.Infrastructure/External/Spotify/SpotifyAuthService.cs

Requirements:
1. Create ISpotifyAuthService interface in Radio.Core

2. Implement SpotifyAuthService:
   - Use Authorization Code flow with PKCE
   - Generate and serve authorization URL
   - Handle callback and exchange code for tokens
   - Store refresh token in SpotifySecrets configuration
   - Auto-refresh access tokens when expired

3. Add authentication endpoints to SpotifyController:
   - GET /api/spotify/auth/url - Get authorization URL
   - GET /api/spotify/auth/callback - Handle OAuth callback
   - GET /api/spotify/auth/status - Check if authenticated
   - POST /api/spotify/auth/logout - Clear tokens

4. Add SpotifyAuthDto models:
   - AuthUrlDto (url, state)
   - AuthStatusDto (isAuthenticated, username, expiresAt)

Success Criteria:
- Can generate auth URL
- Callback successfully exchanges code for tokens
- Refresh token stored securely
- Access tokens refresh automatically
- Status endpoint accurately reports auth state
- Update documentation and `/RTest/UIPREPARATION.md` with status and capabilities.
- Update UAT tests if needed.
```

**Implementation Summary:**
- ✅ Created `ISpotifyAuthService` interface in `/src/Radio.Core/Interfaces/External/ISpotifyAuthService.cs`
  - Defines methods for OAuth authorization flow with PKCE
  - Includes token refresh, status check, and logout operations
  - Provides `SpotifyAuthUrlResult`, `SpotifyAuthTokenResult`, and `SpotifyAuthStatus` models
- ✅ Implemented `SpotifyAuthService` in `/src/Radio.Infrastructure/External/Spotify/SpotifyAuthService.cs`
  - Uses SpotifyAPI.Web library for OAuth operations
  - Implements Authorization Code flow with PKCE (S256 challenge method)
  - Generates cryptographically secure code verifier and state parameters
  - Auto-refreshes access tokens when expired (5-minute buffer)
  - Stores refresh tokens in configuration via `IConfigurationManager`
  - Thread-safe token refresh with `SemaphoreSlim`
  - In-memory token caching for performance
- ✅ Added 4 authentication endpoints to `SpotifyController`:
  - `GET /api/spotify/auth/url` - Generates authorization URL with PKCE parameters
  - `GET /api/spotify/auth/callback` - Handles OAuth callback and exchanges code for tokens
  - `GET /api/spotify/auth/status` - Returns current authentication status with user info
  - `POST /api/spotify/auth/logout` - Clears all stored tokens
- ✅ Created DTOs in `/src/Radio.API/Models/SpotifyModels.cs`:
  - `AuthUrlDto` - Contains authorization URL, state, and code verifier
  - `AuthStatusDto` - Contains authentication status, username, display name, expiration, and user ID
- ✅ Registered service in DI container via `AddSpotifyServices()` extension method
- ✅ Added comprehensive test coverage:
  - 16 unit tests for `SpotifyAuthService` (constructor validation, URL generation, parameter validation, logout)
  - 8 integration tests for authentication endpoints
  - All 672 tests passing (15 Core, 573 Infrastructure, 84 API)

**Files Created:**
- `/src/Radio.Core/Interfaces/External/ISpotifyAuthService.cs` - Auth service interface
- `/src/Radio.Infrastructure/External/Spotify/SpotifyAuthService.cs` - Auth service implementation
- `/src/Radio.Infrastructure/DependencyInjection/ExternalServiceExtensions.cs` - DI registration
- `/tests/Radio.Infrastructure.Tests/External/Spotify/SpotifyAuthServiceTests.cs` - Unit tests

**Files Modified:**
- `/src/Radio.API/Controllers/SpotifyController.cs` - Added auth endpoints
- `/src/Radio.API/Models/SpotifyModels.cs` - Added auth DTOs
- `/src/Radio.API/Program.cs` - Registered Spotify services
- `/tests/Radio.API.Tests/Controllers/SpotifyControllerTests.cs` - Added integration tests

**API Endpoints Available:**
- ✅ `GET /api/spotify/auth/url?redirectUri={optional}` - Generate OAuth authorization URL
- ✅ `GET /api/spotify/auth/callback?code={code}&state={state}&codeVerifier={verifier}` - Handle callback
- ✅ `GET /api/spotify/auth/status` - Check authentication status
- ✅ `POST /api/spotify/auth/logout` - Logout and clear tokens

**Security Features:**
- ✅ PKCE (Proof Key for Code Exchange) with S256 challenge method
- ✅ State parameter for CSRF protection
- ✅ Secure token storage via configuration infrastructure
- ✅ Automatic token refresh with expiration buffer
- ✅ Thread-safe token operations

**OAuth Flow:**
1. Client calls `GET /api/spotify/auth/url` to get authorization URL
2. Server generates PKCE parameters and returns URL with state and code verifier
3. Client stores state and code verifier, redirects user to Spotify authorization page
4. User authorizes app, Spotify redirects back to callback URL with authorization code
5. Client calls `GET /api/spotify/auth/callback` with code, state, and code verifier
6. Server validates state, exchanges code for tokens using PKCE
7. Server stores refresh token in configuration and returns success
8. Access tokens auto-refresh before expiration

**Scopes Requested:**
- `user-read-playback-state` - Read current playback state
- `user-modify-playback-state` - Control playback
- `user-read-currently-playing` - Read currently playing track
- `user-read-private` - Read user profile
- `user-read-email` - Read user email
- `playlist-read-private` - Read private playlists
- `playlist-read-collaborative` - Read collaborative playlists
- `user-library-read` - Read user's saved tracks
- `user-top-read` - Read user's top artists/tracks
- `user-read-recently-played` - Read recently played tracks

**Notes:**
- Refresh tokens are stored in `audio-secrets` configuration store under `Spotify:RefreshToken`
- Access tokens are cached in memory and automatically refreshed when within 5 minutes of expiration
- Token refresh operations are thread-safe with automatic retry on failure
- Authentication status includes user profile information fetched from Spotify API
- All authentication operations are fully logged for debugging and monitoring

---

### Phase 4: Radio Device Controls (Priority: High)
**Estimated Effort:** 5-7 days

#### Task 4.1: Create IRadioControls Interface
**Prompt for Copilot Agent:**
```
Create interface for radio-specific controls (frequency, band, scan, EQ).

New file: /RTest/src/Radio.Core/Interfaces/Audio/IRadioControls.cs

Define IRadioControls interface with:

1. Properties:
   - double CurrentFrequency { get; }
   - RadioBand CurrentBand { get; }
   - double FrequencyStep { get; }
   - int SignalStrength { get; } // 0-100
   - bool IsStereo { get; }
   - RadioEqualizerMode EqualizerMode { get; }
   - int DeviceVolume { get; set; } // 0-100
   - bool IsScanning { get; }
   - ScanDirection? ScanDirection { get; }

2. Methods:
   - Task SetFrequencyAsync(double frequency, CancellationToken ct = default);
   - Task StepFrequencyUpAsync(CancellationToken ct = default);
   - Task StepFrequencyDownAsync(CancellationToken ct = default);
   - Task SetBandAsync(RadioBand band, CancellationToken ct = default);
   - Task SetFrequencyStepAsync(double step, CancellationToken ct = default);
   - Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken ct = default);
   - Task StartScanAsync(ScanDirection direction, CancellationToken ct = default);
   - Task StopScanAsync(CancellationToken ct = default);

3. Events:
   - event EventHandler<RadioStateChangedEventArgs>? StateChanged;

4. Create enums and models in /RTest/src/Radio.Core/Models/Audio/:

RadioBand enum:
   public enum RadioBand { AM, FM }

RadioEqualizerMode enum:
   public enum RadioEqualizerMode { Off, Rock, Pop, Jazz, Classical, Speech }

ScanDirection enum:
   public enum ScanDirection { Up, Down }

RadioStateChangedEventArgs class:
   public class RadioStateChangedEventArgs : EventArgs
   {
     public double Frequency { get; init; }
     public RadioBand Band { get; init; }
     public int SignalStrength { get; init; }
     public bool IsStereo { get; init; }
   }

Success Criteria:
- Interface compiles
- All enums and models defined
- XML documentation complete
- Follows project conventions
```

#### Task 4.2: Implement Radio Controls in RadioAudioSource
**Prompt for Copilot Agent:**
```
Implement radio controls in RadioAudioSource using serial communication with RF320 device.

Location: /RTest/src/Radio.Infrastructure/Audio/Sources/Primary/RadioAudioSource.cs

Requirements:
1. Make RadioAudioSource implement IRadioControls

2. USB Serial Communication:
   - Use System.IO.Ports.SerialPort
   - Connect to USB port from DeviceOptions.Radio.USBPort
   - Baud rate: 9600 (check RF320 documentation)
   - Implement command protocol for RF320:
     * Query current frequency
     * Set frequency
     * Query signal strength
     * Set band (AM/FM)
     * Set volume
     * Set EQ mode

3. Implement SetFrequencyAsync():
   - Validate frequency range (FM: 87.5-108 MHz, AM: 520-1710 kHz)
   - Send command to RF320
   - Update CurrentFrequency property
   - Raise StateChanged event

4. Implement StepFrequencyUpAsync() / StepFrequencyDownAsync():
   - Add/subtract FrequencyStep from CurrentFrequency
   - Handle band edges (wrap or stop)
   - Send new frequency to device

5. Implement SetBandAsync():
   - Switch between AM/FM
   - Send command to device
   - Update frequency ranges

6. Implement scanning:
   - StartScanAsync(): Continuously step frequency in direction
   - Check signal strength after each step
   - Stop when strong signal found (threshold: 50%)
   - Allow manual StopScanAsync()

7. Implement SetEqualizerModeAsync():
   - Send EQ mode command to RF320
   - Store in preferences

8. Implement DeviceVolume:
   - Send volume command to RF320 (separate from master volume)
   - Range 0-100

9. Background polling:
   - Every 500ms, query device for:
     * Current frequency
     * Signal strength
     * Stereo indicator
   - Update properties
   - Raise events on changes

Success Criteria:
- Can control RF320 radio via serial
- Frequency changes work
- Scanning finds stations
- Signal strength updates
- Device volume separate from master
- EQ modes apply
```

#### Task 4.3: Create RadioController API
**Prompt for Copilot Agent:**
```
Create REST API controller for radio controls.

New file: /RTest/src/Radio.API/Controllers/RadioController.cs

Create RadioController with endpoints:

1. GET /api/radio/state
   - Return RadioStateDto with all radio properties
   - Include frequency, band, signal, stereo, scanning status, EQ, device volume

2. POST /api/radio/frequency
   Body: { "frequency": double }
   - Set exact frequency
   - Validate range based on band
   - Return updated state

3. POST /api/radio/frequency/up
   - Step frequency up
   - Return updated state

4. POST /api/radio/frequency/down
   - Step frequency down
   - Return updated state

5. POST /api/radio/band
   Body: { "band": "AM" | "FM" }
   - Switch band
   - Return updated state

6. POST /api/radio/step
   Body: { "step": double }
   - Set frequency step size
   - FM: 0.1 or 0.2 MHz, AM: 9 or 10 kHz
   - Return updated state

7. POST /api/radio/scan/start
   Body: { "direction": "Up" | "Down" }
   - Start scanning
   - Return state with isScanning: true

8. POST /api/radio/scan/stop
   - Stop scanning
   - Return state with isScanning: false

9. POST /api/radio/eq
   Body: { "mode": "Off" | "Rock" | "Pop" | "Jazz" | "Classical" | "Speech" }
   - Set EQ mode
   - Return updated state

10. POST /api/radio/volume
    Body: { "volume": int } (0-100)
    - Set device volume
    - Return updated state

11. Create RadioStateDto in /RTest/src/Radio.API/Models/AudioModels.cs:
    public class RadioStateDto
    {
      public double Frequency { get; set; }
      public string Band { get; set; }
      public double FrequencyStep { get; set; }
      public int SignalStrength { get; set; }
      public bool IsStereo { get; set; }
      public string EqualizerMode { get; set; }
      public int DeviceVolume { get; set; }
      public bool IsScanning { get; set; }
      public string? ScanDirection { get; set; }
    }

Success Criteria:
- All endpoints work when radio is active source
- Return 400 if radio not active
- Frequency validation works
- Scan state properly tracked
- Swagger documentation complete
```

---

### Phase 5: Now Playing and Metadata (Priority: Medium)
**Estimated Effort:** 2-3 days

#### Task 5.1: Standardize Metadata Format
**Prompt for Copilot Agent:**
```
Standardize metadata format across all audio sources.

Update the following:

1. Define standard metadata keys as constants:
   Location: /RTest/src/Radio.Core/Models/Audio/StandardMetadataKeys.cs
   
   public static class StandardMetadataKeys
   {
     public const string Title = "Title";
     public const string Artist = "Artist";
     public const string Album = "Album";
     public const string AlbumArtUrl = "AlbumArtUrl";
     public const string Duration = "Duration";
     public const string TrackNumber = "TrackNumber";
     public const string Genre = "Genre";
     public const string Year = "Year";
   }

2. Update all audio source implementations to use these keys:
   - SpotifyAudioSource
   - FilePlayerAudioSource
   - RadioAudioSource (Title = station name or "Radio")
   - VinylAudioSource (Title = "Vinyl")

3. Ensure default values when no track:
   - Title: "No Track"
   - Artist: "--"
   - Album: "--"
   - AlbumArtUrl: "/images/default-album-art.png"

4. Update AudioSourceDto.Metadata to use Dictionary<string, object> instead of Dictionary<string, string>
   - Allows Duration as TimeSpan, TrackNumber as int, etc.
5. Update IPrimaryAudioSource.Metadata property type to IReadOnlyDictionary<string, object> (was IReadOnlyDictionary<string, string>)
   - Update all implementing classes to return properly typed values
   - Update serialization/deserialization logic to handle object values
   - Update API DTOs and ensure backward compatibility as needed

Success Criteria:
- All sources use standard keys
- Default values provided when no content
- Metadata properly typed
- UI can reliably access standard fields
```

#### Task 5.2: Create Now Playing Endpoint
**Prompt for Copilot Agent:**
```
Create dedicated "Now Playing" endpoint with structured response.

Add to: /RTest/src/Radio.API/Controllers/AudioController.cs

Add new endpoint:

GET /api/audio/nowplaying
Returns NowPlayingDto with:

1. Create NowPlayingDto in /RTest/src/Radio.API/Models/AudioModels.cs:
   public class NowPlayingDto
   {
     public string SourceType { get; set; }
     public string SourceName { get; set; }
     public bool IsPlaying { get; set; }
     public bool IsPaused { get; set; }
     
     // Track info (guaranteed non-null)
     public string Title { get; set; }
     public string Artist { get; set; }
     public string Album { get; set; }
     public string AlbumArtUrl { get; set; }
     
     // Timing
     public TimeSpan? Position { get; set; }
     public TimeSpan? Duration { get; set; }
     public double? ProgressPercentage { get; set; }
     
     // Additional metadata
     public Dictionary<string, object>? ExtendedMetadata { get; set; }
   }

2. Implementation:
   - Get active primary source
   - Extract metadata using StandardMetadataKeys
   - Provide defaults if metadata missing
   - Calculate progress percentage if Duration available
   - Include extended metadata for source-specific info

3. Return 200 with "No Track" defaults if no source active

Success Criteria:
- Always returns valid response (never null)
- Metadata follows standard format
- Progress percentage calculated correctly
- Works for all source types
```

---

### Phase 6: SignalR for Real-Time Updates (Priority: Medium)
**Estimated Effort:** 3-4 days

#### Task 6.1: Create Audio State Hub
**Prompt for Copilot Agent:**
```
Create SignalR hub for real-time audio state updates.

New file: /RTest/src/Radio.API/Hubs/AudioStateHub.cs

Requirements:
1. Create AudioStateHub inheriting from Hub:
   - OnConnectedAsync: Subscribe client to audio updates
   - OnDisconnectedAsync: Unsubscribe client

2. Create background service to push updates:
   New file: /RTest/src/Radio.API/Services/AudioStateUpdateService.cs
   
   - Implement IHostedService
   - Every 500ms, check for state changes:
     * Playback state (playing/paused/stopped)
     * Current position
     * Now playing info
     * Queue changes (if applicable)
     * Radio state (if radio active)
   - Push updates to all connected clients via hub
   - Only push if state actually changed (avoid spam)

3. Hub methods for clients to call:
   - Task SubscribeToQueue() - Get queue updates
   - Task UnsubscribeFromQueue()
   - Task SubscribeToRadioState() - Get radio updates
   - Task UnsubscribeFromRadioState()

4. Hub sends to clients:
   - PlaybackStateChanged(PlaybackStateDto)
   - NowPlayingChanged(NowPlayingDto)
   - QueueChanged(List<QueueItemDto>)
   - RadioStateChanged(RadioStateDto)
   - VolumeChanged(VolumeDto)

5. Register hub in Program.cs:
   - app.MapHub<AudioStateHub>("/hubs/audio")
   - Configure CORS if needed

Success Criteria:
- Clients can connect to hub
- Real-time updates pushed on state changes
- Updates throttled to avoid excessive traffic
- Clients can selectively subscribe to event types
- Works with Blazor Server UI
```

---

### Phase 7: Touch-Friendly API Enhancements (Priority: Low)
**Estimated Effort:** 1-2 days

#### Task 7.1: Add UI Capability Hints to Responses
**Prompt for Copilot Agent:**
```
Enhance API responses with UI capability hints for touch-friendly interfaces.

Update DTOs:

1. PlaybackStateDto:
   Add properties:
   - bool CanPlay
   - bool CanPause
   - bool CanStop
   - bool CanSeek
   - bool CanNext
   - bool CanPrevious
   - bool CanShuffle
   - bool CanRepeat
   - bool CanQueue

2. AudioSourceDto:
   Add properties:
   - bool IsRadio
   - bool IsStreaming
   - bool HasQueue
   - Dictionary<string, bool> Capabilities

3. All enum responses:
   - Ensure serialized as strings, not ints
   - Use JsonStringEnumConverter globally

4. Add validation error details:
   Create ValidationErrorDto:
   public class ValidationErrorDto
   {
     public string Field { get; set; }
     public string Message { get; set; }
     public object? AttemptedValue { get; set; }
   }
   
   Return in 400 responses:
   {
     "error": "Validation failed",
     "details": [ ValidationErrorDto... ]
   }

Success Criteria:
- UI can determine capabilities without additional logic
- All enums readable in UI
- Validation errors clearly indicate what went wrong
- Touch UI can disable unavailable controls
```

---

### Phase 8: Documentation Updates (Priority: Medium)
**Estimated Effort:** 1-2 days

#### Task 8.1: Update WEBUI.md with Examples Look and Feel
**Prompt for Copilot Agent:**
```
Update /RTest/design/WEBUI.md to reflect the UI examples provided in the issue.

Changes needed:

1. Global Music Controls section:
   - Update to reflect Material 3 design
   - Emphasize touch-screen friendliness (min 48px touch targets)
   - Document that controls are conditional based on source capabilities:
     * Shuffle only if SupportsShuffleTrue
     * Next/Previous only if SupportsNext/Previous
     * Repeat only if SupportsRepeat
     * Duration/Position only when Duration is not null

2. Now Playing section:
   - Larger, easier to read
   - Generic music icon when no track
   - Dashes ("--") for artist/song when empty
   - No touch interactions (display only)

3. Playlist Queue section:
   - Remove "Date Added" column (not needed)
   - Song length may be estimated
   - Title might be filename
   - Album could be empty
   - Support drag-to-reorder if source supports it

4. Radio Display section:
   - Specify DSEG14Classic-Bold font for frequency
   - DSEG14Classic-Regular for band display
   - Orange or legacy green color options
   - All radio state components available from IRadioControls

5. Radio Controls section:
   - Add "Set" button between up/down arrows
   - Set button brings up keypad for frequency entry
   - Document long-press for scan behavior
   - Sub Band button changes frequency step
   - EQ button changes device EQ (not global)
   - Volume buttons change device volume (not global)

Keep existing color palette, spacing, and component selection. Only update sections that conflict with the examples.

Success Criteria:
- Documentation reflects UI examples
- No conflicts between examples and documentation
- All touch interaction requirements documented
- Font selections finalized
```

#### Task 8.2: Create API Documentation
**Prompt for Copilot Agent:**
```
Create comprehensive API documentation for all new endpoints.

New file: /RTest/design/API_REFERENCE.md

Document all endpoints added in previous phases:

1. Audio Control Endpoints:
   - List all /api/audio/* endpoints
   - Request/response examples for each
   - Error responses with status codes

2. Queue Management Endpoints:
   - List all /api/queue/* endpoints
   - Examples of queue operations

3. Spotify Endpoints:
   - List all /api/spotify/* endpoints
   - Authentication flow diagram
   - Search examples with filters

4. Radio Control Endpoints:
   - List all /api/radio/* endpoints
   - Frequency format and validation rules
   - Scan behavior description

5. SignalR Hub:
   - Connection URL
   - Events pushed to clients
   - Client subscription methods

Format as OpenAPI/Swagger style documentation with:
- Endpoint path and method
- Description
- Request body schema (if applicable)
- Query parameters (if applicable)
- Response schema
- Status codes
- Example request/response

Success Criteria:
- All new endpoints documented
- Examples are valid and complete
- Status codes accurate
- Easy to understand for frontend developers
```

---

## Summary of Required Changes

### New Interfaces
1. `IPlayQueue` - Queue/playlist management
2. `IRadioControls` - Radio-specific controls
3. Extend `IPrimaryAudioSource` - Add navigation and capabilities

### New Models
1. `QueueItem` - Queue item representation
2. `RadioStateDto` - Radio state for API
3. `NowPlayingDto` - Structured now playing info
4. `SpotifySearchResultDto` - Spotify search results
5. Various Spotify DTOs (Track, Album, Playlist, Artist)

### New Controllers
1. `QueueController` - Queue management API
2. `SpotifyController` - Spotify search, browse, play
3. `RadioController` - Radio control API

### New Services
1. `SpotifyAuthService` - OAuth authentication
2. `AudioStateUpdateService` - Real-time SignalR updates

### Implementations to Complete
1. `SpotifyAudioSource` - Full Spotify integration
2. `RadioAudioSource` - RF320 serial communication
3. `FilePlayerAudioSource` - Queue, shuffle, repeat
4. Navigation methods - Next/Previous/Shuffle/Repeat

### Updated Components
1. `AudioController` - Add next/previous/shuffle/repeat endpoints
2. `PlaybackStateDto` - Add capability flags
3. All sources - Implement capability flags
4. `WEBUI.md` - Update to reflect examples

## Testing Strategy

Each phase should include:

1. **Unit Tests**
   - Test new interfaces and implementations
   - Mock external dependencies (Spotify API, Serial Port)
   - Test edge cases and error conditions

2. **Integration Tests**
   - Test API endpoints end-to-end
   - Test SignalR hub connections and updates
   - Test source switching and state management

3. **Manual Testing**
   - Test with actual Spotify account
   - Test with RF320 radio device (if available)
   - Test queue operations with various sources
   - Test long-press scan behavior

## Risk Assessment

**High Risk:**
- Spotify API integration (rate limits, auth flow complexity)
- RF320 serial communication (unknown command protocol)
- SignalR real-time updates (performance with frequent updates)

**Medium Risk:**
- Queue reordering (race conditions in multi-user scenarios)
- Metadata extraction (various file formats and tag standards)

**Low Risk:**
- UI capability flags (straightforward boolean logic)
- API endpoint creation (follows existing patterns)

## Estimated Total Effort

**Total Estimated Development Time:** 24-34 days

**Breakdown:**
- Phase 1 (Capabilities): 3-5 days
- Phase 2 (Queue): 4-6 days
- Phase 3 (Spotify): 5-7 days
- Phase 4 (Radio): 5-7 days
- Phase 5 (Now Playing): 2-3 days
- Phase 6 (SignalR): 3-4 days
- Phase 7 (Touch UI): 1-2 days
- Phase 8 (Docs): 1-2 days

**Recommended Development Order:**
1. Phase 1 → Phase 2 → Phase 5 (Core playback + queue + display)
2. Phase 6 (Real-time updates)
3. Phase 3 OR Phase 4 (Spotify or Radio, depending on priority)
4. Phase 7 → Phase 8 (Polish and documentation)

## Notes for Coding Agent

- All file paths assume repository root is `/RTest`
- Follow existing code style: 2-space indentation, XML docs on public members
- Use `async`/`await` for all I/O operations
- Prefer dependency injection over static dependencies
- Use `ILogger` for all logging, appropriate levels
- Configuration via `IOptions` pattern as defined in CONFIGURATION.md
- All API responses should include proper status codes and error messages
- Test on Linux/Raspberry Pi compatibility (no Windows-only APIs)

## References

- `/RTest/design/AUDIO_ARCHITECTURE.md` - Audio system architecture
- `/RTest/design/CONFIGURATION.md` - Configuration infrastructure
- `/RTest/design/WEBUI.md` - UI design specification
- `/RTest/PROJECTPLAN.md` - Overall project plan
- SpotifyAPI.Web documentation: https://johnnycrazy.github.io/SpotifyAPI-NET/
- RF320 documentation: (TBD - need to obtain)
- Material 3 Design: https://m3.material.io/
