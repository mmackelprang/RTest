# Spotify Queue Clearing Fix Summary

**Date:** 2026-01-07
**Issue:** `System.NotSupportedException` when attempting to clear the queue while Spotify is the active source.
**Impact:** Prevented playing albums or playlists from the Spotify UI, as the "Play Album" action clears the queue first.

## Analysis
- The Spotify Web API does not support a "clear queue" operation.
- The `SpotifyAudioSource.ClearQueueAsync` method correctly threw `NotSupportedException`.
- However, the `QueueController` (and the Web UI) expects `ClearQueue` to succeed as a preparatory step for playback.
- Because the exception was unhandled in a way that permitted flow continuation, the API returned 500, causing the frontend to abort the playback sequence.

## Resolution
- Modified `SpotifyAudioSource.ClearQueueAsync` to log a warning and return `Task.CompletedTask` instead of throwing.
- This effectively treats "Clear Queue" as a "best effort" (or no-op) for Spotify.
- Note: When playing a Context (Album/Playlist) via Spotify API, the queue is replaced anyway, so the explicit clear was technically redundant for this use case.

## Verification
- Created unit test `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/SpotifyQueueTests.cs`.
- Verified that `ClearQueueAsync` no longer throws and logs a warning.

## Affected Files
- `src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs`
- `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/SpotifyQueueTests.cs` (New)
