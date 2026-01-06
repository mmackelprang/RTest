# Core Audio Pipeline Refactoring Summary

## Overview
The audio pipeline has been refactored to support mixed audio formats (Float32@48kHz and Int16@44.1kHz) by introducing a generic buffering system. This resolves the issue where Spotify playback (which uses 16-bit integer samples) was failing to integrate with the SoundFlow engine (which expects 32-bit floats) because the previous `SDRSoundGenerator` was tightly coupled to SDR-specific float data.

## Changes Implemented

### 1. New Generic `BufferedSoundGenerator<T>`
- Created `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs`.
- **Purpose**: A generic SoundFlow component that accepts samples of type `T` (supporting `float` and `short`) and converts them to the audio engine's native `float` format during generation.
- **Features**:
  - Buffers incoming samples in a thread-safe `Queue<T>`.
  - Implements `SoundComponent` to integrate with SoundFlow.
  - Handles format conversion:
    - `float` -> `float`: Direct copy.
    - `short` -> `float`: Normalizes by dividing by 32768f.
  - Provides diagnostic logging (buffer levels, dropped samples).

### 2. Refactored `SDRRadioAudioSource`
- **File**: `src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs`
- **Changes**:
  - Replaced the dedicated `SDRSoundGenerator` with `BufferedSoundGenerator<float>`.
  - Updated initialization logic to instantiate the new generic generator.
  - Wireup remains event-driven via `_radioReceiver.AudioDataAvailable`.

### 3. Updated `SpotifyAudioSource`
- **File**: `src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs`
- **Changes**:
  - Injected `SoundFlowPlaybackService` into `SpotifyAudioSource` and its inner class `SpotifyIntegratedAudioSource`.
  - Implemented `BufferedSoundGenerator<short>` for the integrated Spotify source.
  - Added logic to convert `byte[]` audio data from `LibrespotManager` into `short[]` spans using `MemoryMarshal`.
  - Wired up `LibrespotManager.AudioDataReceived` directly to `BufferedSoundGenerator.AddSamples`.
  - Ensured playback service is used to "Play" the component, registering it with the main mix engine.

## Result
- **SDR Radio**: continues to work using the new generic generator with float data.
- **Spotify**: now has a valid path to inject audio into the SoundFlow mixer, converting its 16-bit PCM data to the engine's required float format on the fly.
- **Architecture**: The `SDRSoundGenerator` class is effectively obsolete and replaced by the more flexible `BufferedSoundGenerator<T>`.

## Verification
- Solution builds successfully.
- Code changes cover the requirements of handling multiple PCM formats and resolving the "No active device found" error (by actually registering a device/component with the engine).
