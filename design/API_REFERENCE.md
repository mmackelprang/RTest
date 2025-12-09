# Radio Console API Reference

**Version:** 1.0  
**Base URL:** `http://localhost:5000` (or configured host)  
**Content-Type:** `application/json`

This document provides comprehensive API documentation for the Radio Console REST API and SignalR hubs. All endpoints follow RESTful conventions and return JSON responses.

---

## Table of Contents

1. [Audio Control Endpoints](#audio-control-endpoints)
2. [Queue Management Endpoints](#queue-management-endpoints)
3. [Spotify Endpoints](#spotify-endpoints)
4. [File Management Endpoints](#file-management-endpoints)
5. [Radio Control Endpoints](#radio-control-endpoints)
6. [Sources Management Endpoints](#sources-management-endpoints)
7. [Device Management Endpoints](#device-management-endpoints)
8. [Metrics Endpoints](#metrics-endpoints)
9. [Play History Endpoints](#play-history-endpoints)
10. [Configuration Endpoints](#configuration-endpoints)
11. [System Management Endpoints](#system-management-endpoints)
12. [SignalR Hubs](#signalr-hubs)
13. [Common Response Codes](#common-response-codes)
14. [Error Response Format](#error-response-format)

---

## Audio Control Endpoints

Base path: `/api/audio`

These endpoints control audio playback, volume, and transport controls (play, pause, next, previous, shuffle, repeat).

### GET /api/audio

Gets the current playback state including active source, position, duration, volume, and capability flags.

**Response:** 200 OK

```json
{
  "isPlaying": true,
  "isPaused": false,
  "volume": 0.75,
  "isMuted": false,
  "balance": 0.0,
  "position": "00:02:34",
  "duration": "00:04:15",
  "canPlay": true,
  "canPause": true,
  "canStop": true,
  "canSeek": true,
  "canNext": true,
  "canPrevious": true,
  "canShuffle": true,
  "canRepeat": true,
  "canQueue": true,
  "isShuffleEnabled": false,
  "repeatMode": "Off",
  "activeSource": {
    "id": "spotify-1",
    "name": "Spotify",
    "type": "Spotify",
    "category": "Primary",
    "state": "Playing",
    "volume": 1.0,
    "metadata": {
      "Title": "Bohemian Rhapsody",
      "Artist": "Queen",
      "Album": "A Night at the Opera",
      "AlbumArtUrl": "https://i.scdn.co/image/..."
    }
  },
  "duckingState": {
    "isDucking": false,
    "duckLevel": 1.0,
    "activeEventCount": 0
  }
}
```

**Error Responses:**
- `500 Internal Server Error` - Failed to get playback state

---

### POST /api/audio

Updates the playback state (play, pause, stop, volume, etc.).

**Request Body:**

```json
{
  "action": "play",
  "volume": 0.75,
  "isMuted": false,
  "balance": 0.0
}
```

**Fields:**
- `action` (optional): `"play"`, `"pause"`, `"stop"`, `"resume"`
- `volume` (optional): Volume level 0.0-1.0
- `isMuted` (optional): Mute state boolean
- `balance` (optional): Stereo balance -1.0 (left) to 1.0 (right)

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `400 Bad Request` - Invalid action or parameter values
- `500 Internal Server Error` - Failed to update playback state

---

### POST /api/audio/start

Starts audio playback.

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `500 Internal Server Error` - Failed to start playback

---

### POST /api/audio/stop

Stops audio playback and resets position.

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `500 Internal Server Error` - Failed to stop playback

---

### GET /api/audio/volume

Gets the current master volume level.

**Response:** 200 OK

```json
{
  "volume": 0.75,
  "isMuted": false
}
```

---

### POST /api/audio/volume/{volume}

Sets the master volume level.

**Path Parameters:**
- `volume` (float): Volume level 0.0-1.0

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `400 Bad Request` - Invalid volume value (must be 0.0-1.0)
- `500 Internal Server Error` - Failed to set volume

**Example:**
```bash
POST /api/audio/volume/0.75
```

---

### POST /api/audio/mute

Toggles mute state.

**Request Body:**

```json
{
  "muted": true
}
```

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `500 Internal Server Error` - Failed to toggle mute

---

### POST /api/audio/next

Skips to the next track (if source supports navigation).

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `400 Bad Request` - No active primary source or source doesn't support next
- `500 Internal Server Error` - Failed to skip to next track

---

### POST /api/audio/previous

Goes to the previous track or restarts current track (if source supports navigation).

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `400 Bad Request` - No active primary source or source doesn't support previous
- `500 Internal Server Error` - Failed to go to previous track

---

### POST /api/audio/shuffle

Toggles shuffle mode on/off.

**Request Body:**

```json
{
  "enabled": true
}
```

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `400 Bad Request` - No active primary source or source doesn't support shuffle
- `500 Internal Server Error` - Failed to set shuffle mode

---

### POST /api/audio/repeat

Sets the repeat mode.

**Request Body:**

```json
{
  "mode": "All"
}
```

**Fields:**
- `mode`: `"Off"`, `"One"`, or `"All"`

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `400 Bad Request` - No active primary source, source doesn't support repeat, or invalid mode
- `500 Internal Server Error` - Failed to set repeat mode

---

### GET /api/audio/nowplaying

Gets structured "Now Playing" information with guaranteed non-null fields.

**Response:** 200 OK

```json
{
  "sourceType": "Spotify",
  "sourceName": "Spotify",
  "isPlaying": true,
  "isPaused": false,
  "title": "Bohemian Rhapsody",
  "artist": "Queen",
  "album": "A Night at the Opera",
  "albumArtUrl": "https://i.scdn.co/image/...",
  "position": "00:02:34",
  "duration": "00:05:55",
  "progressPercentage": 43.5,
  "extendedMetadata": {
    "Genre": "Rock",
    "Year": "1975",
    "TrackNumber": "11"
  }
}
```

**When no track is playing:**

```json
{
  "sourceType": "None",
  "sourceName": "No Source",
  "isPlaying": false,
  "isPaused": false,
  "title": "No Track",
  "artist": "--",
  "album": "--",
  "albumArtUrl": "/images/default-album-art.png",
  "position": null,
  "duration": null,
  "progressPercentage": null,
  "extendedMetadata": null
}
```

---

## Queue Management Endpoints

Base path: `/api/queue`

Manage music queue/playlist for sources that support queue functionality (Spotify, FilePlayer).

### GET /api/queue

Gets the current queue from the active primary source.

**Response:** 200 OK

```json
[
  {
    "id": "spotify:track:abc123",
    "title": "Bohemian Rhapsody",
    "artist": "Queen",
    "album": "A Night at the Opera",
    "duration": "00:05:55",
    "albumArtUrl": "https://i.scdn.co/image/...",
    "index": 0,
    "isCurrent": true
  },
  {
    "id": "spotify:track:def456",
    "title": "Don't Stop Me Now",
    "artist": "Queen",
    "album": "Jazz",
    "duration": "00:03:29",
    "albumArtUrl": "https://i.scdn.co/image/...",
    "index": 1,
    "isCurrent": false
  }
]
```

**Error Responses:**
- `404 Not Found` - No primary audio source is active
- `400 Bad Request` - Active source doesn't support queue (SupportsQueue = false)
- `500 Internal Server Error` - Failed to get queue

---

### POST /api/queue/add

Adds a track to the queue.

**Request Body:**

```json
{
  "trackIdentifier": "spotify:track:abc123",
  "position": 5
}
```

**Fields:**
- `trackIdentifier` (required): Track URI or file path
- `position` (optional): Insert position (0-based index). If omitted, appends to end.

**Response:** 200 OK - Returns updated queue array

**Error Responses:**
- `404 Not Found` - No primary audio source is active
- `400 Bad Request` - Active source doesn't support queue or invalid track identifier
- `500 Internal Server Error` - Failed to add to queue

---

### DELETE /api/queue/{index}

Removes an item from the queue at the specified index.

**Path Parameters:**
- `index` (int): Zero-based index of item to remove

**Response:** 200 OK - Returns updated queue array

**Error Responses:**
- `404 Not Found` - No primary audio source is active
- `400 Bad Request` - Active source doesn't support queue or invalid index
- `500 Internal Server Error` - Failed to remove from queue

**Example:**
```bash
DELETE /api/queue/2
```

---

### DELETE /api/queue

Clears the entire queue.

**Response:** 200 OK - Returns empty queue array `[]`

**Error Responses:**
- `404 Not Found` - No primary audio source is active
- `400 Bad Request` - Active source doesn't support queue
- `500 Internal Server Error` - Failed to clear queue

---

### POST /api/queue/move

Reorders queue items by moving an item from one position to another.

**Request Body:**

```json
{
  "fromIndex": 3,
  "toIndex": 1
}
```

**Fields:**
- `fromIndex` (required): Current zero-based index of item
- `toIndex` (required): Target zero-based index

**Response:** 200 OK - Returns updated queue array

**Error Responses:**
- `404 Not Found` - No primary audio source is active
- `400 Bad Request` - Active source doesn't support queue or invalid indices
- `500 Internal Server Error` - Failed to reorder queue

---

### POST /api/queue/jump/{index}

Jumps to and plays the item at the specified index.

**Path Parameters:**
- `index` (int): Zero-based index of item to jump to

**Response:** 200 OK - Returns updated `PlaybackStateDto`

**Error Responses:**
- `404 Not Found` - No primary audio source is active
- `400 Bad Request` - Active source doesn't support queue or invalid index
- `500 Internal Server Error` - Failed to jump to queue item

**Example:**
```bash
POST /api/queue/jump/5
```

---

## Spotify Endpoints

Base path: `/api/spotify`

Search, browse, and play Spotify content. Requires Spotify authentication.

### Authentication Flow

1. **GET /api/spotify/auth/url** - Get authorization URL
2. User authorizes app in browser
3. **GET /api/spotify/auth/callback** - Exchange code for tokens
4. **GET /api/spotify/auth/status** - Check authentication status

### GET /api/spotify/auth/url

Generates Spotify OAuth authorization URL with PKCE parameters.

**Query Parameters:**
- `redirectUri` (optional): OAuth redirect URI (defaults to configured value)

**Response:** 200 OK

```json
{
  "url": "https://accounts.spotify.com/authorize?client_id=...&response_type=code&redirect_uri=...&scope=...&code_challenge=...&code_challenge_method=S256&state=...",
  "state": "random-state-value",
  "codeVerifier": "random-code-verifier"
}
```

**Note:** Client must store `state` and `codeVerifier` for the callback.

---

### GET /api/spotify/auth/callback

Handles OAuth callback and exchanges authorization code for tokens.

**Query Parameters:**
- `code` (required): Authorization code from Spotify
- `state` (required): State parameter for CSRF protection
- `codeVerifier` (required): PKCE code verifier

**Response:** 200 OK

```json
{
  "success": true,
  "message": "Successfully authenticated with Spotify"
}
```

**Error Responses:**
- `400 Bad Request` - Missing parameters or invalid code/state
- `500 Internal Server Error` - Failed to exchange code for tokens

---

### GET /api/spotify/auth/status

Checks current Spotify authentication status.

**Response:** 200 OK

```json
{
  "isAuthenticated": true,
  "username": "user@example.com",
  "displayName": "John Doe",
  "expiresAt": "2024-12-04T22:30:00Z",
  "userId": "spotify_user_id"
}
```

**When not authenticated:**

```json
{
  "isAuthenticated": false,
  "username": null,
  "displayName": null,
  "expiresAt": null,
  "userId": null
}
```

---

### POST /api/spotify/auth/logout

Clears stored Spotify authentication tokens.

**Response:** 200 OK

```json
{
  "success": true,
  "message": "Successfully logged out of Spotify"
}
```

---

### GET /api/spotify/search

Searches Spotify for tracks, albums, playlists, artists, or podcasts.

**Query Parameters:**
- `query` (required): Search query string
- `types` (optional): Comma-separated list of types to search. Options: `track`, `album`, `playlist`, `artist`, `show`, `all` (default), `music` (alias for track)

**Response:** 200 OK

```json
{
  "tracks": [
    {
      "id": "abc123",
      "name": "Bohemian Rhapsody",
      "artist": "Queen",
      "album": "A Night at the Opera",
      "duration": "00:05:55",
      "uri": "spotify:track:abc123",
      "albumArtUrl": "https://i.scdn.co/image/..."
    }
  ],
  "albums": [
    {
      "id": "def456",
      "name": "A Night at the Opera",
      "artist": "Queen",
      "imageUrl": "https://i.scdn.co/image/...",
      "uri": "spotify:album:def456",
      "releaseDate": "1975-11-21",
      "totalTracks": 12
    }
  ],
  "playlists": [
    {
      "id": "ghi789",
      "name": "Rock Classics",
      "owner": "Spotify",
      "imageUrl": "https://i.scdn.co/image/...",
      "trackCount": 100,
      "uri": "spotify:playlist:ghi789",
      "description": "The greatest rock songs of all time"
    }
  ],
  "artists": [
    {
      "id": "jkl012",
      "name": "Queen",
      "imageUrl": "https://i.scdn.co/image/...",
      "uri": "spotify:artist:jkl012",
      "followers": 28000000,
      "genres": ["rock", "classic rock", "glam rock"]
    }
  ],
  "shows": []
}
```

**Error Responses:**
- `400 Bad Request` - Missing query parameter or Spotify not available/authenticated
- `500 Internal Server Error` - Search failed

**Example:**
```bash
GET /api/spotify/search?query=bohemian%20rhapsody&types=track,album
```

---

### GET /api/spotify/browse/categories

Gets Spotify browse categories.

**Response:** 200 OK

```json
[
  {
    "id": "toplists",
    "name": "Top Lists",
    "icons": [
      {
        "url": "https://t.scdn.co/media/derived/toplists_11160599e6a04ac5d6f2757f5511778f_0_0_275_275.jpg",
        "width": 275,
        "height": 275
      }
    ]
  },
  {
    "id": "mood",
    "name": "Mood",
    "icons": [
      {
        "url": "https://t.scdn.co/media/original/mood-274x274.jpg",
        "width": 274,
        "height": 274
      }
    ]
  }
]
```

**Error Responses:**
- `400 Bad Request` - Spotify not available or not authenticated
- `500 Internal Server Error` - Failed to get categories

---

### GET /api/spotify/browse/category/{id}/playlists

Gets playlists within a specific category.

**Path Parameters:**
- `id` (string): Category ID

**Response:** 200 OK - Returns array of `SpotifyPlaylistDto`

**Error Responses:**
- `404 Not Found` - Category not found
- `400 Bad Request` - Spotify not available or not authenticated
- `500 Internal Server Error` - Failed to get playlists

**Example:**
```bash
GET /api/spotify/browse/category/toplists/playlists
```

---

### GET /api/spotify/playlists/user

Gets current user's Spotify playlists.

**Response:** 200 OK - Returns array of `SpotifyPlaylistDto`

**Error Responses:**
- `400 Bad Request` - Spotify not available or not authenticated
- `500 Internal Server Error` - Failed to get user playlists

---

### GET /api/spotify/playlists/{id}

Gets detailed information about a specific playlist including tracks.

**Path Parameters:**
- `id` (string): Playlist ID

**Response:** 200 OK

```json
{
  "id": "abc123",
  "name": "Rock Classics",
  "owner": "Spotify",
  "imageUrl": "https://i.scdn.co/image/...",
  "trackCount": 100,
  "uri": "spotify:playlist:abc123",
  "description": "The greatest rock songs of all time",
  "tracks": [
    {
      "id": "track1",
      "name": "Bohemian Rhapsody",
      "artist": "Queen",
      "album": "A Night at the Opera",
      "duration": "00:05:55",
      "uri": "spotify:track:track1",
      "albumArtUrl": "https://i.scdn.co/image/..."
    }
  ]
}
```

**Error Responses:**
- `404 Not Found` - Playlist not found
- `400 Bad Request` - Spotify not available or not authenticated
- `500 Internal Server Error` - Failed to get playlist details

---

### POST /api/spotify/play

Initiates playback of a Spotify track, album, or playlist.

**Request Body:**

```json
{
  "uri": "spotify:track:abc123",
  "contextUri": "spotify:album:def456"
}
```

**Fields:**
- `uri` (required): Spotify URI to play
- `contextUri` (optional): Context URI (album/playlist)

**Response:** 200 OK

```json
{
  "success": true,
  "message": "Playback started"
}
```

**Error Responses:**
- `400 Bad Request` - Missing URI, Spotify not available, not authenticated, or not active source
- `500 Internal Server Error` - Failed to start playback

---

## File Management Endpoints

Base path: `/api/files`

Browse and manage audio files for the File Player source. Allows listing files, playing files, and adding files to the playback queue.

### GET /api/files

Lists audio files in the specified directory. Supports optional path and recursive parameters.

**Query Parameters:**
- `path` (optional): Path relative to the configured root directory. Empty or null returns files from the root directory.
- `recursive` (optional): Boolean. If true, searches subdirectories recursively. Default is false.

**Response:** 200 OK

```json
[
  {
    "path": "music/song1.mp3",
    "fileName": "song1.mp3",
    "extension": ".mp3",
    "sizeBytes": 5242880,
    "createdAt": "2025-01-01T12:00:00Z",
    "lastModifiedAt": "2025-01-01T12:00:00Z",
    "title": "Song Title",
    "artist": "Artist Name",
    "album": "Album Name",
    "duration": "00:03:45",
    "trackNumber": 1,
    "genre": "Rock",
    "year": 2024
  },
  {
    "path": "music/song2.flac",
    "fileName": "song2.flac",
    "extension": ".flac",
    "sizeBytes": 31457280,
    "createdAt": "2025-01-02T14:30:00Z",
    "lastModifiedAt": "2025-01-02T14:30:00Z",
    "title": "Another Song",
    "artist": "Another Artist",
    "album": "Another Album",
    "duration": "00:04:22",
    "trackNumber": null,
    "genre": null,
    "year": null
  }
]
```

**Supported Audio Formats:**
- `.mp3` - MP3
- `.flac` - FLAC
- `.wav` - WAV
- `.ogg` - Ogg Vorbis
- `.aac` - AAC
- `.m4a` - M4A
- `.wma` - Windows Media Audio

**Metadata:**
- Metadata (title, artist, album, duration) is extracted using SoundFlow's metadata reader
- If metadata is unavailable, `title` defaults to the filename without extension
- Other metadata fields will be `null` if not available

**Error Responses:**
- `500 Internal Server Error` - Failed to list audio files

**Example Requests:**

```bash
# List files in root directory
GET /api/files

# List files in subdirectory
GET /api/files?path=music/rock

# List files recursively
GET /api/files?recursive=true

# List files in subdirectory recursively
GET /api/files?path=music&recursive=true
```

---

### POST /api/files/play

Plays a specific audio file. If the File Player source is not currently active, returns an error.

**Request Body:**

```json
{
  "path": "music/song1.mp3"
}
```

**Response:** 200 OK

```json
{
  "success": true,
  "message": "File is now playing",
  "filePath": "music/song1.mp3",
  "fileName": "song1.mp3",
  "title": "Song Title",
  "artist": "Artist Name",
  "album": "Album Name",
  "duration": "00:03:45"
}
```

**Error Responses:**
- `400 Bad Request` - File path is required or file not found/not supported
- `500 Internal Server Error` - Failed to activate File Player source or failed to play audio file

**Notes:**
- The file must exist in the configured file player root directory
- The File Player source must be the currently active source
- If the source is not active, you must first switch to the File Player source via the Sources API

---

### POST /api/files/queue

Adds one or more audio files to the playback queue. If the File Player source is not currently active, returns an error.

**Request Body:**

```json
{
  "paths": [
    "music/song1.mp3",
    "music/song2.flac",
    "music/folder/song3.wav"
  ]
}
```

**Response:** 200 OK

```json
{
  "success": true,
  "message": "Added 2 file(s) to queue",
  "addedCount": 2,
  "failedCount": 1,
  "failedPaths": [
    "music/folder/song3.wav"
  ]
}
```

**Error Responses:**
- `400 Bad Request` - At least one file path is required or File Player source does not support queue operations
- `500 Internal Server Error` - Failed to activate File Player source or failed to add files to queue

**Notes:**
- Each file is validated before adding to the queue
- Files that don't exist or aren't supported audio formats are skipped
- The response indicates how many files were successfully added and which ones failed
- The File Player source must be the currently active source
- The File Player source must support queue operations

---

## Radio Control Endpoints

Base path: `/api/radio`

Control radio frequency, band, scanning, EQ, and device volume. Only works when Radio is the active source.

### Frequency Ranges
- **FM:** 87.5 - 108.0 MHz (in steps of 0.1 or 0.2 MHz)
- **AM:** 520 - 1710 kHz (in steps of 9 or 10 kHz)
- **WB (Weather Band):** Device-specific
- **VHF:** Device-specific
- **SW (Shortwave):** Device-specific

### GET /api/radio/state

Gets the current state of the radio device.

**Response:** 200 OK

```json
{
  "frequency": 101500000,
  "band": "FM",
  "frequencyStep": 100000,
  "signalStrength": 85,
  "isStereo": true,
  "equalizerMode": "Rock",
  "deviceVolume": 75,
  "isScanning": false,
  "scanDirection": null,
  "autoGainEnabled": false,
  "gain": 20.0,
  "isRunning": true
}
```

**Fields:**
- `frequency`: Current frequency in Hertz (Hz)
- `band`: Current band (AM, FM, WB, VHF, SW)
- `frequencyStep`: Frequency step size in Hz
- `signalStrength`: Signal quality (0-100%)
- `isStereo`: Stereo indicator (FM only)
- `equalizerMode`: Current EQ preset
- `deviceVolume`: Device-specific volume (0-100)
- `isScanning`: Whether currently scanning
- `scanDirection`: Scan direction (Up/Down) or null
- `autoGainEnabled`: AGC state (RTLSDRCore only)
- `gain`: Manual gain in dB (RTLSDRCore only, when AGC off)
- `isRunning`: Receiver running state (RTLSDRCore only)

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to get radio state

---

### POST /api/radio/frequency

Sets the radio frequency to a specific value.

**Request Body:**

```json
{
  "frequency": 101.5
}
```

**Fields:**
- `frequency` (required): Frequency in MHz (FM) or kHz (AM)

**Validation:**
- FM: 87.5 - 108.0 MHz
- AM: 520 - 1710 kHz

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio not active, invalid frequency, or out of range
- `500 Internal Server Error` - Failed to set frequency

---

### POST /api/radio/frequency/up

Steps the radio frequency up by one step (based on current sub-band setting).

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to step frequency up

---

### POST /api/radio/frequency/down

Steps the radio frequency down by one step (based on current sub-band setting).

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to step frequency down

---

### POST /api/radio/band

Switches the radio band (AM/FM/WB/VHF/SW).

**Request Body:**

```json
{
  "band": "FM"
}
```

**Fields:**
- `band` (required): One of `"AM"`, `"FM"`, `"WB"`, `"VHF"`, `"SW"` (case-insensitive)

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio not active or invalid band value
- `500 Internal Server Error` - Failed to set band

---

### POST /api/radio/step

Sets the frequency step size (sub-band).

**Request Body:**

```json
{
  "step": 0.2
}
```

**Fields:**
- `step` (required): Step size in MHz (FM: 0.1 or 0.2) or kHz (AM: 9 or 10)

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio not active or invalid step value
- `500 Internal Server Error` - Failed to set frequency step

---

### POST /api/radio/scan/start

Starts scanning for stations in the specified direction.

**Request Body:**

```json
{
  "direction": "Up"
}
```

**Fields:**
- `direction` (required): `"Up"` or `"Down"` (case-insensitive)

**Behavior:**
- Continuously steps frequency in the specified direction
- Stops automatically when a strong signal is found (threshold: 50%)
- Can be manually stopped with `/api/radio/scan/stop`

**Response:** 200 OK - Returns updated `RadioStateDto` with `isScanning: true`

**Error Responses:**
- `400 Bad Request` - Radio not active or invalid direction
- `500 Internal Server Error` - Failed to start scan

---

### POST /api/radio/scan/stop

Stops the current scanning operation.

**Response:** 200 OK - Returns updated `RadioStateDto` with `isScanning: false`

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to stop scan

---

### POST /api/radio/eq

Sets the radio device equalizer mode.

**Request Body:**

```json
{
  "mode": "Rock"
}
```

**Fields:**
- `mode` (required): One of `"Off"`, `"Pop"`, `"Rock"`, `"Country"`, `"Classical"` (case-insensitive)

**Note:** This sets the EQ on the radio device hardware, not the global audio system EQ.

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio not active or invalid mode value
- `500 Internal Server Error` - Failed to set equalizer mode

---

### POST /api/radio/volume

Sets the radio device volume (separate from master volume).

**Request Body:**

```json
{
  "volume": 75
}
```

**Fields:**
- `volume` (required): Volume level 0-100

**Note:** This adjusts the volume on the RF320 radio device hardware, separate from the master/mixer volume.

**Response:** 200 OK - Returns updated `RadioStateDto`

**Error Responses:**
- `400 Bad Request` - Radio not active or volume out of range (0-100)
- `500 Internal Server Error` - Failed to set device volume

---

## Radio Presets Endpoints

Base path: `/api/radio/presets`

Manage saved radio station presets. Presets allow users to save their favorite radio stations for quick access.

### Features
- **Maximum Presets:** 50 presets can be saved
- **Collision Detection:** Duplicate presets (same band and frequency) are prevented
- **Custom Names:** Users can provide custom names, or the system generates a default name in the format `{Band} - {Frequency}`

### GET /api/radio/presets

Gets all saved radio presets.

**Response:** 200 OK

```json
[
  {
    "id": "a1b2c3d4",
    "name": "My Favorite Station",
    "band": "FM",
    "frequency": 101.5,
    "createdAt": "2025-12-04T12:00:00Z"
  },
  {
    "id": "e5f6g7h8",
    "name": "AM - 1010",
    "band": "AM",
    "frequency": 1010,
    "createdAt": "2025-12-04T12:15:00Z"
  }
]
```

**Error Responses:**
- `500 Internal Server Error` - Failed to retrieve presets

---

### POST /api/radio/presets

Creates a new radio preset.

**Request Body:**

```json
{
  "name": "My Favorite Station",
  "band": "FM",
  "frequency": 101.5
}
```

**Fields:**
- `name` (optional): Display name for the preset. If not provided, generates default name like "FM - 101.5"
- `band` (required): One of `"AM"`, `"FM"`, `"WB"`, `"VHF"`, `"SW"` (case-insensitive)
- `frequency` (required): Station frequency (must be greater than 0)

**Response:** 201 Created

```json
{
  "id": "a1b2c3d4",
  "name": "My Favorite Station",
  "band": "FM",
  "frequency": 101.5,
  "createdAt": "2025-12-04T12:00:00Z"
}
```

**Error Responses:**
- `400 Bad Request` - Invalid band, zero/negative frequency, or validation error
- `409 Conflict` - A preset with the same band and frequency already exists
- `507 Insufficient Storage` - Maximum of 50 presets reached
- `500 Internal Server Error` - Failed to create preset

**Example Error Responses:**

```json
// Invalid band
{
  "error": "Invalid band: XYZ. Valid values are: AM, FM, WB, VHF, SW"
}

// Frequency validation
{
  "error": "Frequency must be greater than 0"
}

// Collision (409)
{
  "error": "A preset already exists for FM - 101.5: My Favorite Station"
}

// Maximum reached (507)
{
  "error": "Maximum of 50 presets reached. Please delete an existing preset first."
}
```

---

### DELETE /api/radio/presets/{id}

Deletes a radio preset by ID.

**Path Parameters:**
- `id` (required): The preset ID to delete

**Response:** 204 No Content

**Error Responses:**
- `404 Not Found` - Preset with the specified ID not found
- `500 Internal Server Error` - Failed to delete preset

**Example Error Response:**

```json
{
  "error": "Preset with ID 'nonexistent-id' not found"
}
```

---

### POST /api/radio/gain

Sets the manual gain value in dB. Only works when automatic gain control is disabled (RTLSDRCore only).

**Request Body:**

```json
{
  "gain": 20.0
}
```

**Fields:**
- `gain` (required): Gain value in dB

**Response:** 200 OK

```json
{
  "frequency": 101500000,
  "band": "FM",
  "frequencyStep": 100000,
  "signalStrength": 85,
  "isStereo": true,
  "equalizerMode": "Off",
  "deviceVolume": 75,
  "isScanning": false,
  "scanDirection": null,
  "autoGainEnabled": false,
  "gain": 20.0,
  "isRunning": true
}
```

**Error Responses:**
- `400 Bad Request` - Radio is not the active source or automatic gain is enabled
- `500 Internal Server Error` - Failed to set gain

**Notes:**
- RTLSDRCore only - RF320 does not support gain control
- Manual gain can only be set when AutoGainEnabled is false
- Use `/api/radio/gain/auto` to toggle automatic gain control

---

### POST /api/radio/gain/auto

Toggles automatic gain control on or off (RTLSDRCore only).

**Request Body:**

```json
{
  "enabled": true
}
```

**Fields:**
- `enabled` (required): true to enable AGC, false to disable

**Response:** 200 OK

```json
{
  "frequency": 101500000,
  "band": "FM",
  "frequencyStep": 100000,
  "signalStrength": 85,
  "isStereo": true,
  "equalizerMode": "Off",
  "deviceVolume": 75,
  "isScanning": false,
  "scanDirection": null,
  "autoGainEnabled": true,
  "gain": 0.0,
  "isRunning": true
}
```

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to set automatic gain control

**Notes:**
- RTLSDRCore only - RF320 does not support gain control
- When AGC is enabled, manual gain control is disabled
- Gain value is ignored when AGC is enabled

---

### GET /api/radio/power

Gets the power state of the radio receiver (RTLSDRCore only).

**Response:** 200 OK

```json
true
```

**Returns:**
- Boolean: `true` if powered on, `false` if powered off

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to get power state

**Notes:**
- RTLSDRCore only - RF320 uses physical power button

---

### POST /api/radio/power/toggle

Toggles the power state of the radio receiver (RTLSDRCore only).

**Response:** 200 OK

```json
false
```

**Returns:**
- Boolean: New power state (`true` for on, `false` for off)

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to toggle power state

**Notes:**
- RTLSDRCore only - RF320 uses physical power button
- Toggles between on/off states
- Returns the new power state after toggle

---

### POST /api/radio/startup

Starts the radio receiver (RTLSDRCore only).

**Response:** 200 OK

```json
true
```

**Returns:**
- Boolean: `true` if startup succeeded, `false` otherwise

**Error Responses:**
- `400 Bad Request` - Radio is not the active source or failed to start
- `500 Internal Server Error` - Failed to start radio receiver

**Notes:**
- RTLSDRCore only - initializes RadioReceiver hardware
- Required before radio can receive signals
- Check `IsRunning` property in radio state to verify status

---

### POST /api/radio/shutdown

Shuts down the radio receiver (RTLSDRCore only).

**Response:** 204 No Content

**Error Responses:**
- `400 Bad Request` - Radio is not the active source
- `500 Internal Server Error` - Failed to shut down radio receiver

**Notes:**
- RTLSDRCore only - cleanly stops receiver and releases resources
- Stops signal reception
- Does not unload the radio source from audio engine

---

## Radio Device Factory Endpoints

Base path: `/api/radio/devices`

Enumerate and select radio device types (RTLSDRCore vs RF320).

### GET /api/radio/devices

Lists all available radio device types with their capabilities.

**Response:** 200 OK

```json
{
  "devices": [
    {
      "deviceType": "RTLSDRCore",
      "name": "RTL-SDR Software Defined Radio",
      "description": "Full software control via RTL-SDR USB dongle",
      "capabilities": {
        "supportsSoftwareControl": true,
        "supportsFrequencyControl": true,
        "supportsBandSwitching": true,
        "supportsScanning": true,
        "supportsGainControl": true,
        "supportsEqualizer": false,
        "supportsDeviceVolume": false
      }
    },
    {
      "deviceType": "RF320",
      "name": "RF320 Bluetooth/USB Radio",
      "description": "Bluetooth control with USB audio output",
      "capabilities": {
        "supportsSoftwareControl": false,
        "supportsFrequencyControl": false,
        "supportsBandSwitching": false,
        "supportsScanning": false,
        "supportsGainControl": false,
        "supportsEqualizer": true,
        "supportsDeviceVolume": true
      }
    }
  ]
}
```

**Error Responses:**
- `500 Internal Server Error` - Failed to retrieve device list

---

### GET /api/radio/devices/default

Gets the default radio device type from configuration.

**Response:** 200 OK

```json
{
  "deviceType": "RTLSDRCore",
  "name": "RTL-SDR Software Defined Radio",
  "description": "Full software control via RTL-SDR USB dongle",
  "capabilities": {
    "supportsSoftwareControl": true,
    "supportsFrequencyControl": true,
    "supportsBandSwitching": true,
    "supportsScanning": true,
    "supportsGainControl": true,
    "supportsEqualizer": false,
    "supportsDeviceVolume": false
  }
}
```

**Error Responses:**
- `404 Not Found` - Default device type not configured or invalid
- `500 Internal Server Error` - Failed to retrieve default device

---

### GET /api/radio/devices/current

Gets the currently active radio device type.

**Response:** 200 OK

```json
{
  "deviceType": "RTLSDRCore",
  "name": "RTL-SDR Software Defined Radio",
  "description": "Full software control via RTL-SDR USB dongle",
  "capabilities": {
    "supportsSoftwareControl": true,
    "supportsFrequencyControl": true,
    "supportsBandSwitching": true,
    "supportsScanning": true,
    "supportsGainControl": true,
    "supportsEqualizer": false,
    "supportsDeviceVolume": false
  }
}
```

**Error Responses:**
- `400 Bad Request` - No radio source is currently active
- `500 Internal Server Error` - Failed to retrieve current device

---

### POST /api/radio/devices/select

Selects a radio device type (framework in place for future AudioManager integration).

**Request Body:**

```json
{
  "deviceType": "RTLSDRCore"
}
```

**Fields:**
- `deviceType` (required): Device type identifier ("RTLSDRCore" or "RF320")

**Response:** 200 OK

```json
{
  "deviceType": "RTLSDRCore",
  "name": "RTL-SDR Software Defined Radio",
  "description": "Full software control via RTL-SDR USB dongle",
  "capabilities": {
    "supportsSoftwareControl": true,
    "supportsFrequencyControl": true,
    "supportsBandSwitching": true,
    "supportsScanning": true,
    "supportsGainControl": true,
    "supportsEqualizer": false,
    "supportsDeviceVolume": false
  }
}
```

**Error Responses:**
- `400 Bad Request` - Empty or invalid device type
- `500 Internal Server Error` - Failed to select device

**Notes:**
- Framework endpoint for future device switching
- Actual device switching requires AudioManager integration
- Returns selected device capabilities

---

## Radio Device Comparison

| Feature | RTLSDRCore (SDR) | RF320 (Bluetooth/USB) |
|---------|------------------|----------------------|
| Software Frequency Control | ✅ Full range | ❌ Hardware only |
| Band Switching | ✅ Software | ❌ Physical button |
| Scanning | ✅ Automated | ❌ Physical button |
| Gain Control (AGC/Manual) | ✅ Yes | ❌ N/A |
| Power Management | ✅ Software | ❌ Physical button |
| Lifecycle (Startup/Shutdown) | ✅ Software | ❌ N/A |
| Equalizer | ❌ No hardware EQ | ✅ Hardware EQ |
| Device Volume | ❌ Software only | ✅ Hardware volume |
| Audio Output | ✅ USB via SoundFlow | ✅ USB via SoundFlow |

---

## Sources Management Endpoints

Base path: `/api/sources`

Enumerate and control audio sources.

### GET /api/sources

Gets all available audio sources.

**Response:** 200 OK

```json
[
  {
    "id": "spotify-1",
    "name": "Spotify",
    "type": "Spotify",
    "category": "Primary",
    "state": "Ready",
    "volume": 1.0,
    "metadata": {}
  },
  {
    "id": "radio-1",
    "name": "Radio",
    "type": "Radio",
    "category": "Primary",
    "state": "Ready",
    "volume": 1.0,
    "metadata": {}
  }
]
```

---

### GET /api/sources/active

Gets all currently active sources (primary + any active events).

**Response:** 200 OK - Returns array of `AudioSourceDto`

---

### GET /api/sources/primary

Gets the currently active primary source.

**Response:** 200 OK - Returns single `AudioSourceDto` or `null`

---

### POST /api/sources

Activates (switches to) a specific audio source.

**Request Body:**

```json
{
  "sourceType": "Spotify",
  "configuration": {}
}
```

**Fields:**
- `sourceType` (required): Source type to activate - `"Spotify"`, `"Radio"`, `"Vinyl"`, `"FilePlayer"`, or `"GenericUSB"`
- `configuration` (optional): Source-specific configuration parameters (reserved for future use)

**Response:** 200 OK

```json
{
  "id": "spotify-1",
  "name": "Spotify",
  "type": "Spotify",
  "category": "Primary",
  "state": "Playing",
  "volume": 1.0,
  "metadata": {
    "Title": "Bohemian Rhapsody",
    "Artist": "Queen"
  }
}
```

**Error Responses:**
- `400 Bad Request` - Invalid source type or source not available/configured
- `500 Internal Server Error` - Failed to switch to source
- `501 Not Implemented` - Audio manager not available

**Notes:**
- Requires IAudioManager to be available for source switching
- Only one primary source can be active at a time
- Switching sources will stop playback on the previous source
- If the requested source type is not available, returns list of available sources in error

**Example:**

```bash
POST /api/sources
Content-Type: application/json

{
  "sourceType": "Radio"
}
```

**Example Error Response (source not available):**

```json
{
  "error": "Source type Radio is not available or not configured",
  "availableSources": ["Spotify", "FilePlayer"]
}
```

---

### GET /api/sources/events

Gets all available event audio sources.

**Response:** 200 OK - Returns array of event source information

---

## Device Management Endpoints

Base path: `/api/devices`

Enumerate and manage audio input/output devices.

### GET /api/devices/output

Gets all available audio output devices.

**Response:** 200 OK

```json
[
  {
    "id": "default",
    "name": "Default Audio Device",
    "type": "Output",
    "isDefault": true,
    "maxChannels": 2,
    "supportedSampleRates": [44100, 48000],
    "alsaDeviceId": "hw:0,0",
    "usbPort": null,
    "isUSBDevice": false
  }
]
```

---

### GET /api/devices/input

Gets all available audio input devices (USB audio, etc.).

**Response:** 200 OK - Returns array of `AudioDeviceInfo`

---

### GET /api/devices/output/default

Gets the current default output device.

**Response:** 200 OK - Returns single `AudioDeviceInfo`

---

### POST /api/devices/output

Sets the preferred output device.

**Request Body:**

```json
{
  "deviceId": "hw:1,0"
}
```

**Response:** 200 OK

---

### POST /api/devices/refresh

Manually refreshes the device list (hot-plug check).

**Response:** 200 OK

---

### GET /api/devices/usb/reservations

Gets current USB port reservations.

**Response:** 200 OK

```json
{
  "/dev/ttyUSB0": "radio-1",
  "/dev/ttyUSB1": "vinyl-1"
}
```

---

### GET /api/devices/usb/check

Checks if a specific USB port is in use.

**Query Parameters:**
- `port` (required): USB port path (e.g., `/dev/ttyUSB0`)

**Response:** 200 OK

```json
{
  "inUse": true,
  "sourceId": "radio-1"
}
```

---

## Metrics Endpoints

Base path: `/api/metrics`

Access time-series metrics and analytics data.

### GET /api/metrics/history

Gets historical time-series data for a metric.

**Query Parameters:**
- `key` (required): Metric key (e.g., `"audio.songs_played"`)
- `start` (required): Start timestamp (ISO 8601)
- `end` (required): End timestamp (ISO 8601)
- `resolution` (optional): Time bucket resolution - `"Minute"`, `"Hour"`, or `"Day"` (default: `"Minute"`)

**Response:** 200 OK

```json
[
  {
    "timestamp": "2024-12-04T20:00:00Z",
    "value": 15.5,
    "count": 10,
    "min": 10.0,
    "max": 25.0,
    "tags": {}
  },
  {
    "timestamp": "2024-12-04T20:01:00Z",
    "value": 18.2,
    "count": 12,
    "min": 12.0,
    "max": 28.0,
    "tags": {}
  }
]
```

**Error Responses:**
- `400 Bad Request` - Missing key, start >= end
- `500 Internal Server Error` - Failed to retrieve history

**Example:**
```bash
GET /api/metrics/history?key=audio.songs_played&start=2024-12-04T00:00:00Z&end=2024-12-04T23:59:59Z&resolution=Hour
```

---

### GET /api/metrics/snapshots

Gets current/aggregate values for one or more metrics.

**Query Parameters:**
- `keys` (required): Comma-separated list of metric keys

**Response:** 200 OK

```json
{
  "audio.songs_played": 1523,
  "audio.total_playtime_seconds": 45678,
  "system.cpu_usage_percent": 23.5
}
```

**Behavior:**
- **Counters:** Returns total sum across all time periods
- **Gauges:** Returns the most recent value

**Error Responses:**
- `400 Bad Request` - No keys provided
- `500 Internal Server Error` - Failed to retrieve snapshots

**Example:**
```bash
GET /api/metrics/snapshots?keys=audio.songs_played,audio.total_playtime_seconds
```

---

### GET /api/metrics/aggregate

Gets aggregate statistics for a metric over a time range.

**Query Parameters:**
- `key` (required): Metric key
- `start` (required): Start timestamp (ISO 8601)
- `end` (required): End timestamp (ISO 8601)

**Response:** 200 OK

```json
{
  "count": 150,
  "sum": 2250.5,
  "average": 15.0,
  "min": 5.2,
  "max": 45.8,
  "stdDev": 8.3
}
```

---

### GET /api/metrics/keys

Gets all available metric keys.

**Response:** 200 OK

```json
[
  "audio.songs_played",
  "audio.total_playtime_seconds",
  "audio.volume_changes",
  "system.cpu_usage_percent",
  "system.memory_usage_mb",
  "network.bytes_sent",
  "network.bytes_received"
]
```

---

## Play History Endpoints

Base path: `/api/playhistory`

Track and query play history.

### GET /api/playhistory

Gets recent play history.

**Query Parameters:**
- `limit` (optional): Maximum number of records to return (default: 50)
- `offset` (optional): Number of records to skip (default: 0)

**Response:** 200 OK - Returns array of play history records

---

### GET /api/playhistory/range

Gets play history within a specific time range.

**Query Parameters:**
- `start` (required): Start timestamp (ISO 8601)
- `end` (required): End timestamp (ISO 8601)

**Response:** 200 OK

---

### GET /api/playhistory/today

Gets play history for today.

**Response:** 200 OK

---

### GET /api/playhistory/source/{source}

Gets play history for a specific source type.

**Path Parameters:**
- `source` (string): Source type (e.g., `"Spotify"`, `"Radio"`)

**Response:** 200 OK

---

### GET /api/playhistory/{id}

Gets a specific play history record by ID.

**Path Parameters:**
- `id` (string): Play history record ID

**Response:** 200 OK

---

### GET /api/playhistory/statistics

Gets aggregated statistics about play history.

**Response:** 200 OK

```json
{
  "totalSongsPlayed": 1523,
  "totalPlaytimeSeconds": 45678,
  "mostPlayedSource": "Spotify",
  "mostPlayedTrack": {
    "title": "Bohemian Rhapsody",
    "artist": "Queen",
    "playCount": 47
  }
}
```

---

### POST /api/playhistory

Adds a play history record.

**Request Body:**

```json
{
  "sourceType": "Spotify",
  "trackTitle": "Bohemian Rhapsody",
  "artist": "Queen",
  "album": "A Night at the Opera",
  "duration": "00:05:55",
  "playedAt": "2024-12-04T20:30:00Z"
}
```

**Response:** 201 Created

---

### DELETE /api/playhistory/{id}

Deletes a play history record.

**Path Parameters:**
- `id` (string): Play history record ID

**Response:** 204 No Content

---

## Configuration Endpoints

Base path: `/api/configuration`

Access and modify system configuration.

### GET /api/configuration

Gets all configuration sections.

**Response:** 200 OK

---

### GET /api/configuration/audio

Gets audio-specific configuration.

**Response:** 200 OK

```json
{
  "defaultSource": "Spotify",
  "duckingPercentage": 20,
  "duckingPolicy": "FadeSmooth",
  "duckingAttackMs": 100,
  "duckingReleaseMs": 500
}
```

---

### GET /api/configuration/visualizer

Gets visualizer configuration.

**Response:** 200 OK

---

### GET /api/configuration/output

Gets output configuration.

**Response:** 200 OK

---

### POST /api/configuration

Updates configuration values.

**Request Body:**

```json
{
  "section": "audio",
  "key": "duckingPercentage",
  "value": "25"
}
```

**Fields:**
- `section` (required): Configuration section name (e.g., "audio", "visualizer", "output")
- `key` (required): Configuration key to update
- `value` (required): New value for the configuration key

**Response:** 200 OK

```json
{
  "message": "Configuration updated successfully",
  "section": "audio",
  "key": "duckingPercentage",
  "value": "25"
}
```

**Error Responses:**
- `400 Bad Request` - Missing section or key, or invalid value
- `500 Internal Server Error` - Failed to update configuration
- `501 Not Implemented` - Configuration manager not available

**Notes:**
- Requires the managed configuration system (IConfigurationManager) to be available
- Changes are persisted to the configuration store
- Some configuration changes may require application restart to take effect

---

## System Management Endpoints

Base path: `/api/system`

Monitor system health, resource usage, and access system logs.

### GET /api/system/stats

Gets comprehensive system statistics including resource usage and application state.

**Response:** 200 OK

```json
{
  "cpuUsagePercent": 23.45,
  "ramUsageMb": 156.78,
  "diskUsagePercent": 45.2,
  "threadCount": 42,
  "appUptime": "2d 5h 32m",
  "systemUptime": "15d 7h 14m",
  "audioEngineState": "Active - Spotify (Playing)",
  "systemTemperature": "45.3°C"
}
```

**Fields:**
- `cpuUsagePercent`: CPU usage percentage (0-100)
- `ramUsageMb`: RAM usage in megabytes
- `diskUsagePercent`: Disk usage percentage (0-100)
- `threadCount`: Number of active threads
- `appUptime`: Application uptime in human-readable format
- `systemUptime`: System uptime in human-readable format
- `audioEngineState`: Current audio engine state and active source
- `systemTemperature`: System temperature in Celsius (shows "N/A" if not available)

**Error Responses:**
- `500 Internal Server Error` - Failed to retrieve system stats

**Notes:**
- CPU usage is measured over a short sampling period (~100ms)
- Temperature reading is only available on Linux systems (reads from `/sys/class/thermal/thermal_zone0/temp`)
- On non-Linux systems or when temperature sensor is unavailable, returns "N/A"

**Example:**
```bash
GET /api/system/stats
```

---

### GET /api/system/logs

Gets system logs with optional filtering by level, count, and age.

**Query Parameters:**
- `level` (optional): Log level filter - `"info"`, `"warning"`, or `"error"`. Default is `"warning"`.
- `limit` (optional): Maximum number of log entries to return (1-10000). Default is 100.
- `maxAgeMinutes` (optional): Maximum age of logs in minutes. If not specified, no age filtering is applied.

**Response:** 200 OK

```json
{
  "logs": [
    {
      "timestamp": "2024-12-04T22:15:30Z",
      "level": "Warning",
      "message": "Audio source transition delayed",
      "exception": null,
      "sourceContext": "Radio.Infrastructure.Audio.AudioManager"
    },
    {
      "timestamp": "2024-12-04T22:10:15Z",
      "level": "Error",
      "message": "Failed to connect to Spotify",
      "exception": "System.Net.Http.HttpRequestException: Connection timeout...",
      "sourceContext": "Radio.Infrastructure.External.Spotify.SpotifyClient"
    }
  ],
  "totalCount": 2,
  "filters": {
    "level": "warning",
    "limit": 100,
    "maxAgeMinutes": null
  }
}
```

**Error Responses:**
- `400 Bad Request` - Invalid log level or limit out of range
- `500 Internal Server Error` - Failed to retrieve logs

**Valid Log Levels:**
- `info` - Informational messages and above (Info, Warning, Error)
- `warning` - Warning messages and above (Warning, Error)
- `error` - Error messages only

**Notes:**
- Log retrieval requires Serilog file sink configuration
- Logs are returned in reverse chronological order (newest first)
- If no logs match the criteria, returns an empty array

**Example Requests:**

```bash
# Get last 100 warning and error logs (default)
GET /api/system/logs

# Get last 50 error logs
GET /api/system/logs?level=error&limit=50

# Get info logs from last 30 minutes
GET /api/system/logs?level=info&maxAgeMinutes=30

# Get last 200 warning logs from last hour
GET /api/system/logs?level=warning&limit=200&maxAgeMinutes=60
```

---

## SignalR Hubs

### Audio State Hub

**Connection URL:** `/hubs/audio`

Provides real-time audio state updates to connected clients.

#### Connection

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/audio")
  .withAutomaticReconnect()
  .build();

await connection.start();
```

#### Hub Methods (Client → Server)

##### SubscribeToQueue()

Subscribes the connection to queue update events.

```javascript
await connection.invoke("SubscribeToQueue");
```

##### UnsubscribeFromQueue()

Unsubscribes from queue update events.

```javascript
await connection.invoke("UnsubscribeFromQueue");
```

##### SubscribeToRadioState()

Subscribes to radio state update events.

```javascript
await connection.invoke("SubscribeToRadioState");
```

##### UnsubscribeFromRadioState()

Unsubscribes from radio state update events.

```javascript
await connection.invoke("UnsubscribeFromRadioState");
```

#### Events Pushed to Clients (Server → Client)

##### PlaybackStateChanged

Fired when playback state changes (playing, paused, volume, etc.).

**Broadcast:** All clients

```javascript
connection.on("PlaybackStateChanged", (state) => {
  console.log("Playback state:", state);
  // state is PlaybackStateDto
});
```

**Payload:** `PlaybackStateDto` (see GET /api/audio response)

**Frequency:** Updates every 500ms when state changes

---

##### NowPlayingChanged

Fired when currently playing track changes.

**Broadcast:** All clients

```javascript
connection.on("NowPlayingChanged", (nowPlaying) => {
  console.log("Now playing:", nowPlaying.title, "by", nowPlaying.artist);
  // nowPlaying is NowPlayingDto
});
```

**Payload:** `NowPlayingDto` (see GET /api/audio/nowplaying response)

**Frequency:** Updates when track changes

---

##### QueueChanged

Fired when the queue is modified.

**Broadcast:** Only to clients subscribed via `SubscribeToQueue()`

```javascript
connection.on("QueueChanged", (queue) => {
  console.log("Queue updated:", queue.length, "items");
  // queue is array of QueueItemDto
});
```

**Payload:** Array of `QueueItemDto` (see GET /api/queue response)

**Frequency:** Updates immediately when queue is modified

---

##### RadioStateChanged

Fired when radio state changes (frequency, signal strength, etc.).

**Broadcast:** Only to clients subscribed via `SubscribeToRadioState()`

```javascript
connection.on("RadioStateChanged", (radioState) => {
  console.log("Radio frequency:", radioState.frequency, radioState.band);
  // radioState is RadioStateDto
});
```

**Payload:** `RadioStateDto` (see GET /api/radio/state response)

**Frequency:** Updates every 500ms when radio state changes

---

##### VolumeChanged

Fired when master volume or mute state changes.

**Broadcast:** All clients

```javascript
connection.on("VolumeChanged", (volume) => {
  console.log("Volume:", volume.volume, "Muted:", volume.isMuted);
  // volume is VolumeDto
});
```

**Payload:**

```json
{
  "volume": 0.75,
  "isMuted": false,
  "balance": 0.0
}
```

**Frequency:** Updates immediately when volume changes

---

#### Complete Client Example

```javascript
// Connect to hub
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/audio")
  .withAutomaticReconnect()
  .build();

// Subscribe to all events
connection.on("PlaybackStateChanged", (state) => {
  updatePlaybackUI(state);
});

connection.on("NowPlayingChanged", (nowPlaying) => {
  updateNowPlayingDisplay(nowPlaying);
});

connection.on("VolumeChanged", (volume) => {
  updateVolumeSlider(volume.volume);
});

// Start connection
await connection.start();
console.log("Connected to AudioStateHub");

// Optionally subscribe to queue updates
await connection.invoke("SubscribeToQueue");
connection.on("QueueChanged", (queue) => {
  updateQueueList(queue);
});

// Optionally subscribe to radio updates (if radio is active)
await connection.invoke("SubscribeToRadioState");
connection.on("RadioStateChanged", (radioState) => {
  updateRadioDisplay(radioState);
});
```

---

### Audio Visualization Hub

**Connection URL:** `/hubs/visualization`

Provides real-time audio visualization data (spectrum, waveform, VU meter).

**Note:** Documentation for this hub is available in the visualization-specific API reference.

---

## Common Response Codes

| Code | Meaning | Usage |
|------|---------|-------|
| 200 | OK | Successful GET or POST request |
| 201 | Created | Resource successfully created |
| 204 | No Content | Successful DELETE request |
| 400 | Bad Request | Invalid parameters, missing required fields, or operation not supported |
| 404 | Not Found | Resource or endpoint not found |
| 500 | Internal Server Error | Unexpected server error |

---

## Error Response Format

All error responses follow a consistent format:

```json
{
  "error": "Human-readable error message",
  "details": "Optional detailed error information"
}
```

**Examples:**

```json
{
  "error": "Radio is not the active source"
}
```

```json
{
  "error": "Invalid frequency",
  "details": "Frequency must be between 87.5 and 108.0 MHz for FM band"
}
```

```json
{
  "error": "Validation failed",
  "details": [
    {
      "field": "frequency",
      "message": "Frequency is required",
      "attemptedValue": null
    }
  ]
}
```

---

## Graphical Display Examples

### Metrics Visualization

The metrics endpoints are designed to support graphical displays. Here are recommended approaches:

#### Time-Series Line Chart

Use `/api/metrics/history` with appropriate resolution:

```javascript
// Get hourly CPU usage for the last 24 hours
const response = await fetch(
  '/api/metrics/history?key=system.cpu_usage_percent' +
  '&start=2024-12-03T20:00:00Z' +
  '&end=2024-12-04T20:00:00Z' +
  '&resolution=Hour'
);
const data = await response.json();

// data is array of { timestamp, value, count, min, max }
// Plot as line chart with timestamp on X-axis, value on Y-axis
```

**Recommended Chart Libraries:**
- Chart.js
- Recharts (React)
- ApexCharts
- D3.js

---

#### Real-Time Gauge/Counter

Use `/api/metrics/snapshots` for current values:

```javascript
// Get current songs played count
const response = await fetch(
  '/api/metrics/snapshots?keys=audio.songs_played'
);
const data = await response.json();

// Display as counter: data["audio.songs_played"]
// Update periodically (e.g., every 10 seconds)
```

---

#### Bar Chart (Aggregate Statistics)

Use `/api/metrics/aggregate` for statistical summaries:

```javascript
// Get playtime statistics for the last week
const response = await fetch(
  '/api/metrics/aggregate?key=audio.total_playtime_seconds' +
  '&start=2024-11-27T00:00:00Z' +
  '&end=2024-12-04T23:59:59Z'
);
const data = await response.json();

// data: { count, sum, average, min, max, stdDev }
// Display as bar chart or summary cards
```

---

#### Multi-Metric Dashboard

Combine multiple endpoints for comprehensive dashboards:

```javascript
// 1. Get current snapshot values
const snapshots = await fetch(
  '/api/metrics/snapshots?keys=audio.songs_played,system.cpu_usage_percent,system.memory_usage_mb'
);

// 2. Get time-series for trending
const history = await fetch(
  '/api/metrics/history?key=audio.songs_played&start=...&end=...&resolution=Hour'
);

// Display:
// - Snapshots as KPI cards (large numbers)
// - History as trend line charts
// - Aggregate stats as summary tables
```

---

## Notes for Frontend Developers

### General Guidelines

1. **Always check capability flags** before showing/enabling controls:
   - Check `canNext`, `canPrevious`, `canShuffle`, etc. from `PlaybackStateDto`
   - Hide or disable controls when capability is false

2. **Use SignalR for real-time updates** instead of polling:
   - Connect to `/hubs/audio` on page load
   - Subscribe to relevant events
   - Update UI reactively when events fire

3. **Handle errors gracefully:**
   - Check response status codes
   - Display user-friendly error messages
   - Retry failed requests with exponential backoff

4. **Source-specific features:**
   - Queue operations only work with Spotify and FilePlayer
   - Radio controls only work when Radio is active
   - Check `activeSource.type` to conditionally show UI

5. **Validation:**
   - Validate inputs client-side before sending requests
   - Use provided ranges (e.g., volume 0.0-1.0, radio frequency ranges)

### TypeScript Interfaces

Consider defining these interfaces for type safety:

```typescript
interface PlaybackStateDto {
  isPlaying: boolean;
  isPaused: boolean;
  volume: number;
  isMuted: boolean;
  balance: number;
  position?: string;
  duration?: string;
  canPlay: boolean;
  canPause: boolean;
  canStop: boolean;
  canSeek: boolean;
  canNext: boolean;
  canPrevious: boolean;
  canShuffle: boolean;
  canRepeat: boolean;
  canQueue: boolean;
  isShuffleEnabled: boolean;
  repeatMode: string;
  activeSource?: AudioSourceDto;
  duckingState: DuckingStateDto;
}

interface QueueItemDto {
  id: string;
  title: string;
  artist: string;
  album: string;
  duration?: string;
  albumArtUrl?: string;
  index: number;
  isCurrent: boolean;
}

interface RadioStateDto {
  frequency: number;
  band: string;
  frequencyStep: number;
  signalStrength: number;
  isStereo: boolean;
  equalizerMode: string;
  deviceVolume: number;
  isScanning: boolean;
  scanDirection?: string;
}
```

---

## Changelog

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2024-12-04 | Initial API documentation |

---

## Support

For issues, questions, or feature requests, please refer to the project repository or contact the development team.
