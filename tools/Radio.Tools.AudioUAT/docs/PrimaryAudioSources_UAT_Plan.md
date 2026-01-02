# Primary Audio Sources UAT Integration Tests - Implementation Plan

## Document Overview

This document provides a detailed, phased plan for implementing robust User Acceptance Testing (UAT) integration tests for Primary Audio Sources in the Radio Console system. The tests will validate FilePlayer, Radio (RTL-SDR), and Spotify audio sources through the existing API, ensuring data integrity and proper functionality.

---

## 1. Objective

**Primary Goal**: Debug and validate data issues from Primary Audio Sources by creating comprehensive UAT tests in `tools/Radio.Tools.AudioUAT`.

**Success Criteria**:
- All Primary Audio Sources can be switched and verified programmatically
- Audio playback, transport controls, and metadata are validated end-to-end
- Tests use the actual API (no mocks) to catch real integration issues
- Detailed logging and metrics capture all test execution details
- Tests can run both interactively and in automated CI/CD pipelines

---

## 2. Infrastructure Requirements

### 2.1 API Integration
- [ ] Tests MUST use the Radio.API as-is without modifications
- [ ] Tests communicate with the API via HTTP REST endpoints
- [ ] All test configuration uses the same configuration system as the main application
- [ ] No mocking of audio components - tests validate real audio paths

### 2.2 API Lifecycle Management
- [ ] Add a shutdown endpoint to Radio.API for clean test termination:
  - [ ] Create `POST /api/system/shutdown` endpoint
  - [ ] Endpoint performs graceful shutdown of all audio sources
  - [ ] Endpoint disposes SoundFlow resources properly
  - [ ] Returns confirmation before shutdown begins
- [ ] Alternative: Command-line shutdown mechanism via signal handling

### 2.3 Configuration Alignment
- [ ] Test runner uses **exact same** configuration as Radio.API:
  - [ ] Shared `appsettings.json` or configuration path
  - [ ] Same secrets provider (SQLite or JSON)
  - [ ] Same database connections
  - [ ] Same file paths (media, configuration, etc.)
- [ ] Only difference: Logging configuration for test debugging:
  - [ ] More verbose logging (Debug/Trace level)
  - [ ] Separate log files for test runs
  - [ ] Structured logging for test metrics

### 2.4 Logging and Metrics
- [ ] Configure Serilog with detailed sinks:
  - [ ] Console sink with structured output
  - [ ] File sink: `./logs/uat-{Date}.log`
  - [ ] Separate file: `./logs/uat-metrics-{Date}.log` for metrics
- [ ] Capture for each test:
  - [ ] Test ID, name, start/end timestamps
  - [ ] API request/response payloads
  - [ ] Audio state transitions
  - [ ] Error details with stack traces
  - [ ] Performance metrics (latency, throughput)

### 2.5 Test Execution Modes
- [ ] **Interactive Mode**: User observes and confirms audio output
- [ ] **Automated Mode**: Programmatic validation with metrics
- [ ] **CI/CD Mode**: Headless execution with result artifacts

### 2.6 Prerequisites Check
- [ ] Verify Radio.API is running and accessible
- [ ] Verify audio output device is available
- [ ] Verify test media files exist at expected paths
- [ ] For Radio tests: Verify RTL-SDR device is connected
- [ ] For Spotify tests: Verify authentication credentials are available

---

## 3. Phase 1: FilePlayer Primary Audio Source Tests

### Phase 1 Overview
Validate the FilePlayer audio source with a specific set of test files, ensuring queue management, playback controls, and metadata accuracy.

### 3.1 Test Prerequisites
- [ ] Verify test files exist:
  - [ ] `src/Radio.API/media/audio/testdata/SheriYoureMyHoneyBunchSugarPlumRingtone.mp3`
  - [ ] `src/Radio.API/media/audio/music/02 We're Ready.mp3`
  - [ ] `src/Radio.API/media/audio/music/Hear What They Say.mp3`
- [ ] Verify API endpoints are available:
  - [ ] `GET /api/audio/sources` - List available sources
  - [ ] `POST /api/audio/sources/switch` - Switch active source
  - [ ] `GET /api/audio/queue` - Get current queue
  - [ ] `POST /api/audio/queue/add` - Add files to queue
  - [ ] `POST /api/audio/queue/clear` - Clear queue
  - [ ] `GET /api/audio/playback` - Get playback state
  - [ ] `POST /api/audio/playback/play` - Start playback
  - [ ] `POST /api/audio/playback/pause` - Pause playback
  - [ ] `POST /api/audio/playback/stop` - Stop playback
  - [ ] `POST /api/audio/playback/next` - Next track
  - [ ] `POST /api/audio/playback/previous` - Previous track
  - [ ] `POST /api/audio/volume` - Set volume
  - [ ] `GET /api/audio/nowplaying` - Get current track metadata

### 3.2 Test Cases

#### Test P1-001: Switch to FilePlayer Source
**Objective**: Verify the API can switch the active audio source to FilePlayer.

**Steps**:
- [ ] Call `GET /api/audio/sources` to get list of available sources
- [ ] Verify `FilePlayer` is in the list of sources
- [ ] Call `POST /api/audio/sources/switch` with `{"sourceType": "FilePlayer"}`
- [ ] Verify response indicates success (HTTP 200)
- [ ] Call `GET /api/audio/sources` again to confirm active source is `FilePlayer`

**Expected Result**:
- [ ] Active source successfully switches to FilePlayer
- [ ] No errors in API logs
- [ ] Source state is persisted

**Pass/Fail Criteria**:
- ✅ Pass: FilePlayer is active source after switch
- ❌ Fail: Switch fails, or active source is not FilePlayer

---

#### Test P1-002: Queue Test Files
**Objective**: Add the three specific test files to the FilePlayer queue.

**Steps**:
- [ ] Call `POST /api/audio/queue/clear` to start with empty queue
- [ ] Call `POST /api/audio/queue/add` with:
  ```json
  {
    "files": [
      "testdata/SheriYoureMyHoneyBunchSugarPlumRingtone.mp3",
      "music/02 We're Ready.mp3",
      "music/Hear What They Say.mp3"
    ]
  }
  ```
- [ ] Call `GET /api/audio/queue` to retrieve current queue
- [ ] Verify queue contains exactly 3 items
- [ ] Verify item order matches the order added
- [ ] Verify file paths match expected values

**Expected Result**:
- [ ] Queue contains all 3 files in correct order
- [ ] Each queue item has valid metadata (title, artist, duration if available)
- [ ] No duplicate entries

**Pass/Fail Criteria**:
- ✅ Pass: Queue has exactly 3 items in correct order with correct paths
- ❌ Fail: Queue is missing items, items are in wrong order, or paths are incorrect

---

#### Test P1-003: Verify Queue Integrity
**Objective**: Ensure queue state is consistent and persisted.

**Steps**:
- [ ] From previous test, verify queue has 3 items
- [ ] Call `GET /api/audio/queue` multiple times
- [ ] Verify queue state is consistent across calls
- [ ] Verify queue items have consistent IDs
- [ ] Verify no items are lost or duplicated

**Expected Result**:
- [ ] Queue state remains consistent
- [ ] Item IDs are stable
- [ ] Metadata remains intact

**Pass/Fail Criteria**:
- ✅ Pass: Queue is stable and consistent across multiple reads
- ❌ Fail: Queue state changes unexpectedly or items are lost

---

#### Test P1-004: Start Playback
**Objective**: Verify playback starts and audio is output to SoundFlow.

**Steps**:
- [ ] Call `GET /api/audio/playback` to verify initial state (should be Stopped)
- [ ] Call `POST /api/audio/playback/play` to start playback
- [ ] Verify response indicates success
- [ ] Call `GET /api/audio/playback` to confirm state is now Playing
- [ ] Monitor logs for SoundFlow audio processing events
- [ ] **(Interactive)** Ask user to confirm audio is heard from physical output

**Expected Result**:
- [ ] Playback state transitions to Playing
- [ ] Audio is output through SoundFlow engine
- [ ] First track in queue begins playing
- [ ] Physical audio output produces sound (interactive confirmation)
- [ ] Logs show audio buffer processing

**Pass/Fail Criteria**:
- ✅ Pass: Playback state is Playing, logs show audio processing, user confirms audio output
- ❌ Fail: Playback state does not change, no audio output, or errors in logs

---

#### Test P1-005: Verify Physical Audio Output
**Objective**: Confirm audio is actually being output to the physical device.

**Steps**:
- [ ] Ensure playback is active (from P1-004)
- [ ] Check SoundFlow output stream is active
- [ ] Verify audio device is receiving data
- [ ] **(Interactive)** User confirms audio is audible through speakers/headphones
- [ ] Monitor audio levels in logs/metrics

**Expected Result**:
- [ ] SoundFlow reports active output stream
- [ ] Audio device shows active state
- [ ] User confirms audible audio
- [ ] No buffer underruns or audio glitches reported

**Pass/Fail Criteria**:
- ✅ Pass: Audio is physically audible and logs confirm output stream activity
- ❌ Fail: No audio heard, or logs show output stream errors

---

#### Test P1-006: Stop Playback
**Objective**: Verify playback can be stopped cleanly.

**Steps**:
- [ ] Ensure playback is active
- [ ] Call `POST /api/audio/playback/stop`
- [ ] Verify response indicates success
- [ ] Call `GET /api/audio/playback` to confirm state is now Stopped
- [ ] Verify SoundFlow output stream is stopped or silent
- [ ] **(Interactive)** User confirms audio has stopped

**Expected Result**:
- [ ] Playback state transitions to Stopped
- [ ] Audio output ceases
- [ ] No errors in stopping playback
- [ ] Resources are released properly

**Pass/Fail Criteria**:
- ✅ Pass: Playback state is Stopped, audio output has stopped, no errors
- ❌ Fail: Playback continues, state does not change, or errors occur

---

#### Test P1-007: Start/Stop Cycle
**Objective**: Verify Start/Stop functionality works repeatedly without issues.

**Steps**:
- [ ] For i = 1 to 3:
  - [ ] Call `POST /api/audio/playback/play`
  - [ ] Wait 2 seconds
  - [ ] Verify playback state is Playing
  - [ ] Call `POST /api/audio/playback/stop`
  - [ ] Wait 1 second
  - [ ] Verify playback state is Stopped
- [ ] Check logs for any errors or warnings during cycles

**Expected Result**:
- [ ] All 3 start/stop cycles complete successfully
- [ ] No state inconsistencies
- [ ] No resource leaks (check logs)
- [ ] Audio starts and stops cleanly each time

**Pass/Fail Criteria**:
- ✅ Pass: All 3 cycles complete without errors, state transitions are clean
- ❌ Fail: Any cycle fails, state becomes inconsistent, or errors occur

---

#### Test P1-008: Volume Control
**Objective**: Verify volume changes affect the actual output level.

**Steps**:
- [ ] Start playback (if not already playing)
- [ ] Call `POST /api/audio/volume` with `{"volume": 100}`
- [ ] Wait 2 seconds, **(Interactive)** user notes volume level
- [ ] Call `POST /api/audio/volume` with `{"volume": 50}`
- [ ] Wait 2 seconds, **(Interactive)** user confirms volume is lower
- [ ] Call `POST /api/audio/volume` with `{"volume": 25}`
- [ ] Wait 2 seconds, **(Interactive)** user confirms volume is even lower
- [ ] Call `POST /api/audio/volume` with `{"volume": 75}` to restore
- [ ] Check logs for volume change events

**Expected Result**:
- [ ] Volume changes take effect immediately
- [ ] User confirms audible difference at each level
- [ ] No audio glitches or pops during volume changes
- [ ] Logs confirm volume changes

**Pass/Fail Criteria**:
- ✅ Pass: User confirms volume changes are audible and smooth
- ❌ Fail: Volume does not change, or audio glitches occur during changes

---

#### Test P1-009: Next Track Navigation
**Objective**: Verify the "next" command advances to the next track in the queue.

**Steps**:
- [ ] Start playback (should be on first track)
- [ ] Call `GET /api/audio/nowplaying` to verify current track (should be "SheriYoureMyHoneyBunchSugarPlumRingtone.mp3")
- [ ] Call `POST /api/audio/playback/next`
- [ ] Wait 1 second for track change
- [ ] Call `GET /api/audio/nowplaying` to verify new track (should be "02 We're Ready.mp3")
- [ ] Verify playback continues on new track
- [ ] Call `POST /api/audio/playback/next` again
- [ ] Wait 1 second
- [ ] Call `GET /api/audio/nowplaying` to verify track (should be "Hear What They Say.mp3")

**Expected Result**:
- [ ] Next command successfully advances to next track
- [ ] Playback continues without interruption
- [ ] Now Playing metadata updates correctly
- [ ] Track order matches queue order

**Pass/Fail Criteria**:
- ✅ Pass: Next command advances to correct next track, metadata is accurate
- ❌ Fail: Wrong track plays, playback stops, or metadata is incorrect

---

#### Test P1-010: Previous Track Navigation
**Objective**: Verify the "previous" command goes back to the previous track.

**Steps**:
- [ ] Ensure playback is on third track (from P1-009)
- [ ] Call `GET /api/audio/nowplaying` to confirm (should be "Hear What They Say.mp3")
- [ ] Call `POST /api/audio/playback/previous`
- [ ] Wait 1 second
- [ ] Call `GET /api/audio/nowplaying` to verify track (should be "02 We're Ready.mp3")
- [ ] Call `POST /api/audio/playback/previous` again
- [ ] Wait 1 second
- [ ] Call `GET /api/audio/nowplaying` to verify track (should be "SheriYoureMyHoneyBunchSugarPlumRingtone.mp3")

**Expected Result**:
- [ ] Previous command successfully goes back to previous track
- [ ] Playback continues without interruption
- [ ] Now Playing metadata updates correctly
- [ ] Track order is correct in reverse direction

**Pass/Fail Criteria**:
- ✅ Pass: Previous command goes to correct previous track, metadata is accurate
- ❌ Fail: Wrong track plays, playback stops, or metadata is incorrect

---

#### Test P1-011: Metadata Accuracy
**Objective**: Verify "Now Playing" endpoint returns accurate metadata for each track.

**Steps**:
- [ ] Navigate to first track and start playback
- [ ] For each track in queue:
  - [ ] Call `GET /api/audio/nowplaying`
  - [ ] Verify response contains:
    - [ ] Track title (matches file name or ID3 tag)
    - [ ] File path (matches queued path)
    - [ ] Duration (if available)
    - [ ] Playback position
    - [ ] Playback state (Playing)
  - [ ] Call `POST /api/audio/playback/next` to advance to next track
  - [ ] Wait 1 second
- [ ] Compare metadata against actual file properties

**Expected Result**:
- [ ] Metadata is present for all tracks
- [ ] Metadata values are accurate
- [ ] Playback position updates during playback
- [ ] No missing or null fields (except optional ones)

**Pass/Fail Criteria**:
- ✅ Pass: All metadata fields are accurate for all tracks
- ❌ Fail: Metadata is missing, incorrect, or inconsistent

---

### Phase 1 Summary
After completing all Phase 1 tests:
- [ ] All 11 tests pass
- [ ] FilePlayer source is fully validated
- [ ] Queue management works correctly
- [ ] Transport controls function properly
- [ ] Volume control affects output
- [ ] Metadata is accurate
- [ ] No critical errors in logs

---

## 4. Phase 2: Radio Primary Audio Source Tests

### Phase 2 Overview
Validate the Radio audio source (RTL-SDR) including tuning, band switching, scanning, and audio output. Note: This phase requires an RTL-SDR device to be connected.

### 4.1 Test Prerequisites
- [ ] **Critical**: RTL-SDR device must be connected to the system
  - [ ] If device is not detected, tests should fail gracefully with clear error message
  - [ ] Verify device at expected USB port or path
- [ ] Verify Radio source is available in sources list
- [ ] Verify API endpoints:
  - [ ] `POST /api/audio/sources/switch` - Switch to Radio
  - [ ] `POST /api/radio/band` - Set band (AM/FM/Shortwave)
  - [ ] `POST /api/radio/frequency` - Set frequency
  - [ ] `POST /api/radio/tune/up` - Tune frequency up
  - [ ] `POST /api/radio/tune/down` - Tune frequency down
  - [ ] `POST /api/radio/scan/up` - Scan up for station
  - [ ] `POST /api/radio/scan/down` - Scan down for station
  - [ ] `GET /api/audio/nowplaying` - Get station info

### 4.2 Test Cases

#### Test P2-001: RTL-SDR Device Detection (Pre-requisite)
**Objective**: Verify RTL-SDR device is installed and accessible.

**Steps**:
- [ ] Check if RTL-SDR device is connected:
  - [ ] On Linux: Check for `/dev/bus/usb/` entries or `lsusb` output
  - [ ] Verify device is not in use by another process
- [ ] Attempt to initialize Radio source
- [ ] If device not found, fail all Radio tests with clear message

**Expected Result**:
- [ ] RTL-SDR device is detected
- [ ] Device is accessible and not locked
- [ ] Radio source can initialize

**Pass/Fail Criteria**:
- ✅ Pass: RTL-SDR device detected and accessible
- ❌ Fail: Device not found - Skip all remaining Radio tests with clear message

---

#### Test P2-002: Switch to Radio Source
**Objective**: Verify API can switch to Radio as the active audio source.

**Steps**:
- [ ] Call `POST /api/audio/sources/switch` with `{"sourceType": "Radio"}`
- [ ] Verify response indicates success
- [ ] Call `GET /api/audio/sources` to confirm active source is Radio
- [ ] Check logs for Radio source initialization

**Expected Result**:
- [ ] Active source switches to Radio
- [ ] RTL-SDR device initializes without errors
- [ ] Source is ready to receive tuning commands

**Pass/Fail Criteria**:
- ✅ Pass: Radio source is active and initialized
- ❌ Fail: Switch fails or Radio cannot initialize

---

#### Test P2-003: Verify Audio Output (Static/Noise)
**Objective**: Confirm audio output is working (static or noise is acceptable for untuned radio).

**Steps**:
- [ ] Ensure Radio source is active
- [ ] Start playback: `POST /api/audio/playback/play`
- [ ] Verify playback state is Playing
- [ ] **(Interactive)** User confirms audio output (static/noise is expected if not tuned)
- [ ] Check SoundFlow logs for active audio stream
- [ ] Monitor audio levels to confirm data is flowing

**Expected Result**:
- [ ] Audio output is active
- [ ] User hears static/noise from speakers
- [ ] SoundFlow reports active audio stream
- [ ] No errors in audio pipeline

**Pass/Fail Criteria**:
- ✅ Pass: Audio output confirmed (static/noise acceptable)
- ❌ Fail: No audio output or errors in audio pipeline

---

#### Test P2-004: FM Band Selection
**Objective**: Verify switching to FM band sets correct frequency range.

**Steps**:
- [ ] Call `POST /api/radio/band` with `{"band": "FM"}`
- [ ] Verify response indicates success
- [ ] Check that frequency is set within FM range (87.5 - 108.0 MHz)
- [ ] Verify logs show band change to FM

**Expected Result**:
- [ ] Band switches to FM
- [ ] Frequency is within FM range
- [ ] RTL-SDR tunes to FM frequency

**Pass/Fail Criteria**:
- ✅ Pass: Band is FM, frequency in correct range
- ❌ Fail: Band does not switch or frequency out of range

---

#### Test P2-005: AM Band Selection
**Objective**: Verify switching to AM band sets correct frequency range.

**Steps**:
- [ ] Call `POST /api/radio/band` with `{"band": "AM"}`
- [ ] Verify response indicates success
- [ ] Check that frequency is set within AM range (530 - 1710 kHz)
- [ ] Verify logs show band change to AM

**Expected Result**:
- [ ] Band switches to AM
- [ ] Frequency is within AM range
- [ ] RTL-SDR tunes to AM frequency

**Pass/Fail Criteria**:
- ✅ Pass: Band is AM, frequency in correct range
- ❌ Fail: Band does not switch or frequency out of range

---

#### Test P2-006: Shortwave Band Selection
**Objective**: Verify switching to Shortwave band sets correct frequency range.

**Steps**:
- [ ] Call `POST /api/radio/band` with `{"band": "Shortwave"}`
- [ ] Verify response indicates success
- [ ] Check that frequency is set within Shortwave range (typically 3 - 30 MHz)
- [ ] Verify logs show band change to Shortwave

**Expected Result**:
- [ ] Band switches to Shortwave
- [ ] Frequency is within Shortwave range
- [ ] RTL-SDR tunes to Shortwave frequency

**Pass/Fail Criteria**:
- ✅ Pass: Band is Shortwave, frequency in correct range
- ❌ Fail: Band does not switch or frequency out of range

---

#### Test P2-007: Frequency Tuning Up
**Objective**: Verify "Tune Up" increases the frequency.

**Steps**:
- [ ] Switch to FM band (if not already)
- [ ] Set initial frequency: `POST /api/radio/frequency` with `{"frequency": 95.5}`
- [ ] Wait 1 second
- [ ] Note current frequency
- [ ] Call `POST /api/radio/tune/up`
- [ ] Wait 1 second
- [ ] Get current frequency
- [ ] Verify frequency has increased (e.g., to 95.6 or next step)

**Expected Result**:
- [ ] Frequency increases by expected step (e.g., 0.1 MHz for FM)
- [ ] Audio output reflects new frequency
- [ ] No errors in tuning

**Pass/Fail Criteria**:
- ✅ Pass: Frequency increases correctly
- ❌ Fail: Frequency does not change or changes incorrectly

---

#### Test P2-008: Frequency Tuning Down
**Objective**: Verify "Tune Down" decreases the frequency.

**Steps**:
- [ ] From previous test, note current frequency
- [ ] Call `POST /api/radio/tune/down`
- [ ] Wait 1 second
- [ ] Get current frequency
- [ ] Verify frequency has decreased (should return to original or lower)

**Expected Result**:
- [ ] Frequency decreases by expected step
- [ ] Audio output reflects new frequency
- [ ] No errors in tuning

**Pass/Fail Criteria**:
- ✅ Pass: Frequency decreases correctly
- ❌ Fail: Frequency does not change or changes incorrectly

---

#### Test P2-009: Direct Frequency Setting
**Objective**: Verify setting a specific frequency directly.

**Steps**:
- [ ] Call `POST /api/radio/frequency` with `{"frequency": 100.0}` (100.0 MHz FM)
- [ ] Verify response indicates success
- [ ] Get current frequency to confirm
- [ ] Verify RTL-SDR is tuned to 100.0 MHz

**Expected Result**:
- [ ] Frequency is set to exactly 100.0 MHz
- [ ] Audio output changes to new frequency
- [ ] No tuning errors

**Pass/Fail Criteria**:
- ✅ Pass: Frequency is set correctly to 100.0 MHz
- ❌ Fail: Frequency is not set correctly or errors occur

---

#### Test P2-010: Scan Up Functionality
**Objective**: Verify "Scan Up" finds the next station in FM band.

**Steps**:
- [ ] Ensure in FM band
- [ ] Set starting frequency: `POST /api/radio/frequency` with `{"frequency": 98.0}`
- [ ] Call `POST /api/radio/scan/up`
- [ ] Wait for scan to complete (timeout 30 seconds)
- [ ] Verify scan found a station (frequency changed)
- [ ] Verify new frequency is higher than 98.0 MHz
- [ ] **(Interactive)** User confirms clearer audio (less static) indicates station found

**Expected Result**:
- [ ] Scan completes and finds a station
- [ ] Frequency is higher than starting point
- [ ] Audio quality improves (station signal detected)
- [ ] Scan stops automatically when station found

**Pass/Fail Criteria**:
- ✅ Pass: Scan finds a station with better signal near or above 100 MHz
- ❌ Fail: Scan times out, does not change frequency, or finds no station

---

#### Test P2-011: Scan Down Functionality
**Objective**: Verify "Scan Down" finds the previous station in FM band.

**Steps**:
- [ ] From previous test, note current frequency (should be from scan up)
- [ ] Call `POST /api/radio/scan/down`
- [ ] Wait for scan to complete (timeout 30 seconds)
- [ ] Verify scan found a station
- [ ] Verify new frequency is lower than starting point
- [ ] **(Interactive)** User confirms audio quality indicates station

**Expected Result**:
- [ ] Scan completes and finds a station
- [ ] Frequency is lower than starting point
- [ ] Audio quality improves (station signal detected)
- [ ] Scan stops automatically when station found

**Pass/Fail Criteria**:
- ✅ Pass: Scan finds a station with better signal at lower frequency
- ❌ Fail: Scan times out, does not change frequency, or finds no station

---

#### Test P2-012: Now Playing Metadata (Empty Expected)
**Objective**: Verify "Now Playing" returns empty or minimal data for Radio (unless fingerprinting is active).

**Steps**:
- [ ] While tuned to a station, call `GET /api/audio/nowplaying`
- [ ] Verify response structure:
  - [ ] If fingerprinting disabled: Expect empty or generic metadata
  - [ ] If fingerprinting enabled: May have song title/artist
- [ ] Verify no errors in metadata retrieval
- [ ] Check that frequency is reported

**Expected Result**:
- [ ] Metadata endpoint responds successfully
- [ ] If fingerprinting off: Metadata is empty or shows "Radio - [frequency]"
- [ ] If fingerprinting on: May show detected song info
- [ ] No errors or crashes

**Pass/Fail Criteria**:
- ✅ Pass: Metadata endpoint works, data matches expected state (empty unless fingerprinting)
- ❌ Fail: Endpoint errors or returns invalid data

---

### Phase 2 Summary
After completing all Phase 2 tests:
- [ ] All 12 tests pass (or P2-001 fails and rest are skipped)
- [ ] Radio source is fully validated
- [ ] RTL-SDR device is working
- [ ] Band switching works for AM/FM/Shortwave
- [ ] Frequency tuning (up/down/direct) works
- [ ] Scanning finds stations
- [ ] Audio output is confirmed
- [ ] No critical errors in logs

---

## 5. Phase 3: Spotify Primary Audio Source Tests

### Phase 3 Overview
Validate the Spotify audio source including search, playback, transport controls, and volume/balance adjustments.

### 5.1 Test Prerequisites
- [ ] Spotify source is available in sources list
- [ ] Spotify authentication credentials are configured
- [ ] Spotify API access is working
- [ ] Verify API endpoints:
  - [ ] `POST /api/audio/sources/switch` - Switch to Spotify
  - [ ] `POST /api/spotify/search` - Search for tracks/artists
  - [ ] `POST /api/spotify/play` - Play a specific track
  - [ ] `POST /api/audio/playback/play` - Resume playback
  - [ ] `POST /api/audio/playback/pause` - Pause playback
  - [ ] `POST /api/audio/playback/next` - Next track
  - [ ] `POST /api/audio/playback/previous` - Previous track
  - [ ] `POST /api/audio/volume` - Set volume
  - [ ] `POST /api/audio/balance` - Set balance (if supported)
  - [ ] `GET /api/audio/nowplaying` - Get current track info

### 5.2 Test Cases

#### Test P3-001: Switch to Spotify Source
**Objective**: Verify API can switch to Spotify as the active audio source.

**Steps**:
- [ ] Call `POST /api/audio/sources/switch` with `{"sourceType": "Spotify"}`
- [ ] Verify response indicates success
- [ ] Call `GET /api/audio/sources` to confirm active source is Spotify
- [ ] Check logs for Spotify source initialization
- [ ] Verify Spotify authentication state

**Expected Result**:
- [ ] Active source switches to Spotify
- [ ] Spotify connection initializes without errors
- [ ] Authentication is valid
- [ ] Source is ready for search/playback

**Pass/Fail Criteria**:
- ✅ Pass: Spotify source is active and authenticated
- ❌ Fail: Switch fails, authentication fails, or Spotify cannot initialize

---

#### Test P3-002: Search for Artist
**Objective**: Verify searching for "the cars" returns results including "The Cars" artist.

**Steps**:
- [ ] Call `POST /api/spotify/search` with:
  ```json
  {
    "query": "the cars",
    "type": "artist"
  }
  ```
- [ ] Verify response contains search results
- [ ] Check that "The Cars" (classic rock band) is in the results
- [ ] Verify results include artist ID, name, and image URL

**Expected Result**:
- [ ] Search returns results
- [ ] "The Cars" artist is found in results
- [ ] Artist data includes ID and name
- [ ] No search errors

**Pass/Fail Criteria**:
- ✅ Pass: Search returns "The Cars" artist with valid data
- ❌ Fail: Search fails, returns no results, or "The Cars" not found

---

#### Test P3-003: Search for Tracks
**Objective**: Verify searching for "the cars" returns track results.

**Steps**:
- [ ] Call `POST /api/spotify/search` with:
  ```json
  {
    "query": "the cars",
    "type": "track"
  }
  ```
- [ ] Verify response contains track results
- [ ] Check that popular tracks by "The Cars" are in results
- [ ] Verify each track has ID, title, artist, and album

**Expected Result**:
- [ ] Search returns track results
- [ ] Tracks by "The Cars" are included
- [ ] Track metadata is complete
- [ ] No search errors

**Pass/Fail Criteria**:
- ✅ Pass: Search returns tracks by "The Cars" with complete metadata
- ❌ Fail: Search fails, returns no tracks, or metadata is incomplete

---

#### Test P3-004: Play Specific Track ("Immortals" by Fall Out Boy)
**Objective**: Verify selecting and playing a specific track works.

**Steps**:
- [ ] Search for "Immortals Fall Out Boy"
- [ ] Call `POST /api/spotify/search` with:
  ```json
  {
    "query": "Immortals Fall Out Boy",
    "type": "track"
  }
  ```
- [ ] Extract track ID for "Immortals" from results
- [ ] Call `POST /api/spotify/play` with:
  ```json
  {
    "trackId": "<extracted_track_id>"
  }
  ```
- [ ] Verify playback starts
- [ ] Call `GET /api/audio/playback` to confirm state is Playing
- [ ] Call `GET /api/audio/nowplaying` to verify track info

**Expected Result**:
- [ ] Track "Immortals" is found in search
- [ ] Playback starts successfully
- [ ] Now Playing shows "Immortals" by "Fall Out Boy"
- [ ] Audio is output through SoundFlow

**Pass/Fail Criteria**:
- ✅ Pass: "Immortals" plays, metadata is correct, audio output confirmed
- ❌ Fail: Track not found, playback fails, wrong track plays, or no audio

---

#### Test P3-005: Verify Spotify Audio Output
**Objective**: Confirm audio is output through SoundFlow to physical device.

**Steps**:
- [ ] Ensure "Immortals" is playing (from P3-004)
- [ ] Verify SoundFlow output stream is active
- [ ] Check audio levels in logs
- [ ] **(Interactive)** User confirms audio is heard from speakers
- [ ] Verify no buffer underruns or audio glitches

**Expected Result**:
- [ ] Audio is output through SoundFlow
- [ ] User hears the track playing
- [ ] Audio quality is good (no glitches)
- [ ] Logs confirm active audio stream

**Pass/Fail Criteria**:
- ✅ Pass: User confirms audio output, logs show active stream
- ❌ Fail: No audio heard or stream errors

---

#### Test P3-006: Pause Playback
**Objective**: Verify pause command stops audio playback.

**Steps**:
- [ ] Ensure playback is active
- [ ] Call `POST /api/audio/playback/pause`
- [ ] Verify response indicates success
- [ ] Call `GET /api/audio/playback` to confirm state is Paused
- [ ] **(Interactive)** User confirms audio has paused
- [ ] Verify playback position is maintained

**Expected Result**:
- [ ] Playback state transitions to Paused
- [ ] Audio output stops
- [ ] Playback position is saved
- [ ] No errors in pause

**Pass/Fail Criteria**:
- ✅ Pass: Playback pauses, state is Paused, position maintained
- ❌ Fail: Playback continues, state incorrect, or errors occur

---

#### Test P3-007: Resume Playback
**Objective**: Verify play command resumes paused playback.

**Steps**:
- [ ] Ensure playback is paused (from P3-006)
- [ ] Call `POST /api/audio/playback/play`
- [ ] Verify response indicates success
- [ ] Call `GET /api/audio/playback` to confirm state is Playing
- [ ] **(Interactive)** User confirms audio has resumed
- [ ] Verify playback continues from saved position

**Expected Result**:
- [ ] Playback state transitions to Playing
- [ ] Audio output resumes
- [ ] Playback continues from where it was paused
- [ ] No audio glitches on resume

**Pass/Fail Criteria**:
- ✅ Pass: Playback resumes from saved position, audio output confirmed
- ❌ Fail: Playback does not resume or starts from beginning

---

#### Test P3-008: Next Track
**Objective**: Verify next command advances to the next track.

**Steps**:
- [ ] Ensure playback is active
- [ ] Note current track from `GET /api/audio/nowplaying`
- [ ] Call `POST /api/audio/playback/next`
- [ ] Wait 2 seconds for track change
- [ ] Call `GET /api/audio/nowplaying` to verify new track
- [ ] Verify new track is different from previous track
- [ ] **(Interactive)** User confirms a new song is playing

**Expected Result**:
- [ ] Next command successfully advances to next track
- [ ] Now Playing metadata updates
- [ ] Audio continues playing new track
- [ ] No interruption or errors

**Pass/Fail Criteria**:
- ✅ Pass: Next track plays, metadata updates correctly
- ❌ Fail: Same track continues, playback stops, or errors occur

---

#### Test P3-009: Previous Track
**Objective**: Verify previous command goes back to the previous track.

**Steps**:
- [ ] Ensure playback is active
- [ ] Call `POST /api/audio/playback/previous`
- [ ] Wait 2 seconds for track change
- [ ] Call `GET /api/audio/nowplaying` to verify track changed
- [ ] Verify track is different (or same track restarted if at beginning)
- [ ] **(Interactive)** User confirms track changed or restarted

**Expected Result**:
- [ ] Previous command successfully goes to previous track or restarts current track
- [ ] Now Playing metadata updates
- [ ] Audio continues playing
- [ ] No interruption or errors

**Pass/Fail Criteria**:
- ✅ Pass: Previous track plays or current restarts, metadata updates correctly
- ❌ Fail: Command fails, playback stops, or errors occur

---

#### Test P3-010: Volume Control via SoundFlow
**Objective**: Verify volume changes affect Spotify playback output level.

**Steps**:
- [ ] Ensure playback is active
- [ ] Call `POST /api/audio/volume` with `{"volume": 100}`
- [ ] Wait 2 seconds, **(Interactive)** user notes volume level
- [ ] Call `POST /api/audio/volume` with `{"volume": 50}`
- [ ] Wait 2 seconds, **(Interactive)** user confirms volume is lower
- [ ] Call `POST /api/audio/volume` with `{"volume": 25}`
- [ ] Wait 2 seconds, **(Interactive)** user confirms volume is even lower
- [ ] Call `POST /api/audio/volume` with `{"volume": 75}` to restore

**Expected Result**:
- [ ] Volume changes take effect immediately
- [ ] User confirms audible difference at each level
- [ ] No audio glitches during volume changes
- [ ] Logs confirm volume adjustments

**Pass/Fail Criteria**:
- ✅ Pass: Volume changes are audible and smooth via SoundFlow
- ❌ Fail: Volume does not change or audio glitches occur

---

#### Test P3-011: Balance Control (if supported)
**Objective**: Verify balance control affects left/right channel output.

**Steps**:
- [ ] Ensure playback is active
- [ ] Call `POST /api/audio/balance` with `{"balance": 0}` (center)
- [ ] Wait 1 second
- [ ] Call `POST /api/audio/balance` with `{"balance": -50}` (left bias)
- [ ] Wait 2 seconds, **(Interactive)** user confirms audio is louder on left
- [ ] Call `POST /api/audio/balance` with `{"balance": 50}` (right bias)
- [ ] Wait 2 seconds, **(Interactive)** user confirms audio is louder on right
- [ ] Call `POST /api/audio/balance` with `{"balance": 0}` to restore center

**Expected Result**:
- [ ] Balance changes take effect
- [ ] User confirms audio shift to left and right
- [ ] No audio glitches during balance changes
- [ ] If balance not supported, endpoint returns appropriate error

**Pass/Fail Criteria**:
- ✅ Pass: Balance changes are audible (or endpoint clearly indicates not supported)
- ❌ Fail: Balance changes fail unexpectedly or cause errors

---

#### Test P3-012: Now Playing Metadata Accuracy
**Objective**: Verify Now Playing returns accurate Spotify metadata.

**Steps**:
- [ ] Ensure a track is playing
- [ ] Call `GET /api/audio/nowplaying`
- [ ] Verify response contains:
  - [ ] Track title
  - [ ] Artist name(s)
  - [ ] Album name
  - [ ] Album art URL
  - [ ] Track duration
  - [ ] Current playback position
  - [ ] Spotify track ID or URI
- [ ] Verify metadata matches the track playing

**Expected Result**:
- [ ] All metadata fields are present and accurate
- [ ] Album art URL is valid
- [ ] Playback position updates over time
- [ ] No null or missing required fields

**Pass/Fail Criteria**:
- ✅ Pass: Metadata is complete and accurate for playing track
- ❌ Fail: Metadata is missing, incorrect, or incomplete

---

### Phase 3 Summary
After completing all Phase 3 tests:
- [ ] All 12 tests pass
- [ ] Spotify source is fully validated
- [ ] Search functionality works
- [ ] Playback controls (play/pause/next/prev) work
- [ ] Volume and balance controls affect output
- [ ] Metadata is accurate and complete
- [ ] No critical errors in logs

---

## 6. Test Execution and Reporting

### 6.1 Test Execution Checklist
- [ ] Set up test environment (Radio.API running, configuration aligned)
- [ ] Run Phase 1: FilePlayer Tests
  - [ ] Record results for each test
  - [ ] Capture logs and metrics
  - [ ] Note any failures or issues
- [ ] Run Phase 2: Radio Tests
  - [ ] Verify RTL-SDR device first
  - [ ] Record results for each test
  - [ ] Capture logs and metrics
  - [ ] Note any failures or issues
- [ ] Run Phase 3: Spotify Tests
  - [ ] Verify Spotify authentication
  - [ ] Record results for each test
  - [ ] Capture logs and metrics
  - [ ] Note any failures or issues
- [ ] Generate test report with all results
- [ ] Archive logs and metrics for review

### 6.2 Test Report Format
Each test report should include:
- Test ID and name
- Pass/Fail result
- Execution timestamp
- Duration
- Logs (excerpt or link)
- Screenshots (if interactive)
- Metrics captured
- Notes and observations
- Issues found (with severity)

### 6.3 Success Metrics
- **Overall Pass Rate**: At least 90% of tests pass
- **Critical Tests**: All critical path tests must pass (switch source, playback, audio output)
- **No Critical Errors**: Zero critical errors in logs during test execution
- **Performance**: API response times under 500ms for most endpoints
- **Audio Quality**: No glitches, underruns, or quality issues reported

---

## 7. Test Implementation Notes

### 7.1 Test Runner Integration
Tests will be implemented in the existing `Radio.Tools.AudioUAT` framework:
- Add a new Phase: `Phase3` (or use existing Phase3 if renumbering)
- Implement `PrimaryAudioSourceIntegrationTests.cs`
- Use `HttpClient` to call Radio.API endpoints
- Capture responses and validate
- Log all actions and results

### 7.2 Configuration
Add test-specific configuration to `appsettings.json`:
```json
{
  "UAT": {
    "ApiBaseUrl": "http://localhost:5000",
    "TestTimeout": 30,
    "InteractiveMode": true,
    "TestFiles": {
      "FilePlayer": [
        "testdata/SheriYoureMyHoneyBunchSugarPlumRingtone.mp3",
        "music/02 We're Ready.mp3",
        "music/Hear What They Say.mp3"
      ]
    },
    "Spotify": {
      "TestQuery": "the cars",
      "TestTrack": "Immortals Fall Out Boy"
    },
    "Radio": {
      "RequireDevice": true,
      "TestFrequency": 100.0
    }
  }
}
```

### 7.3 Interactive Confirmations
For interactive tests:
- Display clear prompt to user
- Wait for Y/N input
- Record user response in test result
- Include user feedback in report

Example:
```
🎵 Please listen to the audio output.
❓ Do you hear audio playing from the speakers? (Y/N):
```

### 7.4 Error Handling
- Capture all exceptions with full stack traces
- Log API errors with request/response details
- On test failure, continue to next test (don't abort entire phase)
- Mark dependent tests as skipped if prerequisite fails

---

## 8. Appendix: API Endpoint Reference

### 8.1 Common Endpoints
- `GET /api/audio/sources` - List all available audio sources
- `POST /api/audio/sources/switch` - Switch active source
- `GET /api/audio/playback` - Get current playback state
- `POST /api/audio/playback/play` - Start or resume playback
- `POST /api/audio/playback/pause` - Pause playback
- `POST /api/audio/playback/stop` - Stop playback
- `POST /api/audio/playback/next` - Next track
- `POST /api/audio/playback/previous` - Previous track
- `POST /api/audio/volume` - Set volume (0-100)
- `POST /api/audio/balance` - Set balance (-100 to 100)
- `GET /api/audio/nowplaying` - Get current track metadata

### 8.2 FilePlayer Endpoints
- `GET /api/audio/queue` - Get current queue
- `POST /api/audio/queue/add` - Add files to queue
- `POST /api/audio/queue/clear` - Clear queue
- `POST /api/audio/queue/remove` - Remove item from queue

### 8.3 Radio Endpoints
- `POST /api/radio/band` - Set band (AM/FM/Shortwave)
- `POST /api/radio/frequency` - Set frequency
- `POST /api/radio/tune/up` - Tune frequency up
- `POST /api/radio/tune/down` - Tune frequency down
- `POST /api/radio/scan/up` - Scan up for station
- `POST /api/radio/scan/down` - Scan down for station

### 8.4 Spotify Endpoints
- `POST /api/spotify/search` - Search tracks/artists/albums
- `POST /api/spotify/play` - Play specific track by ID
- `GET /api/spotify/playlists` - Get user playlists
- `POST /api/spotify/playlist/play` - Play a playlist

### 8.5 System Endpoints
- `POST /api/system/shutdown` - Gracefully shutdown the API (to be implemented)

---

## 9. Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-01 | GitHub Copilot | Initial detailed UAT plan for Primary Audio Sources |

---

## 10. References

- `/tools/AUDIO_UAT.md` - Overall Audio UAT Tool documentation
- `/design/AUDIO.md` - Audio architecture and design specifications
- `/archive/PROJECTPLAN.md` - Project context and overview
- Radio.Tools.AudioUAT implementation in `tools/Radio.Tools.AudioUAT/`

---

**End of Document**
