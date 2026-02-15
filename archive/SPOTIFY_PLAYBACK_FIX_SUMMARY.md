# Spotify Playback Premature Stop Fix

## Issue
Spotify playback would start correctly but stop after approximately 1.5 seconds, with logs indicating "No more tracks in queue".
This was caused by `LibrespotManager` reading the audio data from the `librespot` process much faster than real-time speed. The `BufferedSoundGenerator` (configured with a 2-second buffer) would overflow and drop the **oldest** samples. By the time `librespot` finished sending the entire song (in ~1.5s), the buffer only contained the last 2 seconds of audio. The playback would then drain this short buffer and stop.

## Solution
We implemented a backpressure mechanism to throttling the reading speed of `LibrespotManager` to match the playback speed.

### Changes
1.  **BufferedSoundGenerator.cs**: 
    - Added `BufferOverflowStrategy` enum with `DropOldest` and `Block` options.
    - Updated `AddSamples` to support blocking (`Monitor.Wait`) when the buffer is full if the strategy is `Block`.
    - Updated `Dispose` and `ClearBuffer` to pulse waiting threads to prevent deadlocks.
    - Preserved `DropOldest` as the default to ensure SDR (Radio) functionality remains unaffected.

2.  **SpotifyAudioSource.cs (SpotifyIntegratedAudioSource)**:
    - Initialized `BufferedSoundGenerator` with `BufferOverflowStrategy.Block`.
    - Moved `LibrespotManager.AudioDataReceived` event subscription from `InitializeAsync` to `PlayCoreAsync`.
    - Removed event subscription in `StopCoreAsync` to ensure `LibrespotManager` is not blocked when playback is stopped.
    - Called `ClearBuffer()` in `StopCoreAsync` to release any threads blocked on writing to the buffer.

## Result
`librespot` is now forced to pace its output to match the playback consumption rate. This ensures the entire song is played effectively, and the process responds correctly to Stop commands without deadlocking.
