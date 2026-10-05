# RTL-SDR Audio Pipeline Fix - Debugging Documentation

**Date:** 2026-01-05
**Status:** Resolved
**Applies to:** SpotifyAudioSource, any future raw PCM audio source

## Problem Summary

The SDR Radio source was activating and receiving radio signals, but no audio was being output to SoundFlow. The logs showed `received=0, output=0` samples despite the RTL-SDR hardware reporting successful reception.

## Root Cause Analysis

### Issue 1: Stream-based approach doesn't work for raw PCM

The initial implementation used `SDRAudioStream` (a .NET `Stream` subclass) with SoundFlow's `StreamDataProvider`. This failed because:

```
System.NotSupportedException: No registered codec factory could decode the provided stream.
```

**Why:** `StreamDataProvider` expects **encoded audio** (MP3, WAV, etc.) and uses codec factories to decode it. Raw PCM float samples are already decoded and don't need/can't use a codec.

### Issue 2: Position setter exception

Even after attempting to use Stream, the `StreamDataProvider` constructor tried to set `Position = 0`, which threw:

```
System.NotSupportedException: Cannot seek in live SDR audio stream
```

## Solution

Created `SDRSoundGenerator` - a custom `SoundComponent` that:

1. **Extends `SoundFlow.Abstracts.SoundComponent`** - This is a SoundFlow audio graph node
2. **Implements `GenerateAudio(Span<float> buffer, int channels)`** - Called by SoundFlow's audio thread to pull samples
3. **Buffers incoming samples** from `RadioReceiver.AudioDataAvailable` events
4. **Directly outputs raw PCM floats** to SoundFlow without any codec/decoding

### Key Files Changed

| File | Change |
|------|--------|
| `SDRSoundGenerator.cs` (NEW) | Custom SoundComponent for raw PCM audio |
| `SoundFlowPlaybackService.cs` | Added `PlayComponentAsync()` for SoundComponents, `GetUnderlyingEngine()`, `GetAudioFormat()` |
| `SDRRadioAudioSource.cs` | Uses SDRSoundGenerator instead of SDRAudioStream |
| `SDRAudioStream.cs` | No longer used (can be deleted) |

## Architecture Pattern for Raw PCM Audio Sources

For any audio source that provides raw PCM samples (not encoded audio files):

```
┌─────────────────────┐
│   Audio Source      │ (e.g., RTL-SDR, Librespot, microphone)
│ Push-based events   │
└─────────┬───────────┘
          │ AudioDataAvailable / AudioDataReceived events
          ▼
┌─────────────────────┐
│  Custom Buffer      │ Queue<float> or similar
│  (thread-safe)      │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  SoundComponent     │ Extends SoundFlow.Abstracts.SoundComponent
│  GenerateAudio()    │ Pulls from buffer, fills SoundFlow's Span<float>
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  SoundFlow Mixer    │ Via PlayComponentAsync()
│  (MasterMixer)      │
└─────────────────────┘
```

### SoundComponent Constructor Requirements

```csharp
public class MyAudioGenerator : SoundComponent
{
    public MyAudioGenerator(
        AudioEngine engine,      // From playbackService.GetUnderlyingEngine()
        AudioFormat format,      // From playbackService.GetAudioFormat()
        IMyAudioSource source,   // Your audio source
        ILogger logger)
        : base(engine, format)   // MUST call base constructor
    {
        // Subscribe to audio events from source
    }

    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        // Pull samples from your buffer, write to buffer
        // Fill any remaining space with silence (zeros)
    }
}
```

### Adding to Mixer

```csharp
var engine = _playbackService.GetUnderlyingEngine();
var format = _playbackService.GetAudioFormat();
var generator = new MyAudioGenerator(engine, format, mySource, logger);
await _playbackService.PlayComponentAsync(sourceId, generator, volume);
```

## Common Mistakes to Avoid

1. **Don't use `StreamDataProvider` for raw PCM** - It's for encoded audio only
2. **Don't use `PlayStreamAsync` for raw PCM** - It wraps StreamDataProvider
3. **Always pass AudioEngine and AudioFormat to SoundComponent** - Required by base class
4. **Handle buffer underruns** - Fill remaining buffer with silence (zeros)
5. **Use thread-safe collections** - Audio events and GenerateAudio run on different threads

## Applying to SpotifyAudioSource (LibrespotAudioDataProvider)

The current `LibrespotAudioDataProvider` does NOT implement `ISoundDataProvider` and is NOT integrated with SoundFlow's audio graph. To fix:

1. Create `LibrespotSoundGenerator` extending `SoundComponent`
2. Buffer samples from `LibrespotManager.AudioDataReceived` events
3. Implement `GenerateAudio()` to pull from buffer
4. Use `PlayComponentAsync()` instead of current approach

### Key Difference from SDR

- SDR audio: 32-bit float samples at 48kHz
- Librespot audio: 16-bit integer samples at 44.1kHz

May need sample rate conversion and format conversion (int16 → float32) in the generator.

## Test Commands

```bash
# Start API
cd D:/prj/rtest/rtest && dotnet run --project src/Radio.API

# Activate Radio source
curl -X POST -H "Content-Type: application/json" \
  -d '{"sourceType":"Radio"}' http://localhost:5000/api/sources

# Expected log output showing audio flowing:
# [DBG] SDR audio: received=426405, output=426405, dropped=0, buffered=0
```

## Success Indicators

- `received > 0` - Audio source is providing samples
- `output > 0` - SoundFlow is consuming samples
- `dropped = 0` - No buffer overflow
- `buffered ≈ 0` - Real-time consumption (low latency)

---

# Fingerprinting Audio Capture Fix

**Date:** 2026-01-05
**Status:** Resolved
**Applies to:** BackgroundIdentificationService, SoundFlowAudioTap

## Problem Summary

The fingerprinting service was unable to capture audio from the mixed output. Logs showed:
```
No audio data captured after 15005ms and 136 read attempts
```

This occurred even when SDR audio was flowing correctly:
```
SDR audio: received=1279542, output=1279542, dropped=0, buffered=0
```

## Root Cause Analysis

The `TappedOutputStream` was created during engine initialization but **nothing was writing audio data to it**:

1. `TappedOutputStream` has a `WriteFromEngine(float[] samples)` method
2. `SoundFlowAudioEngine.WriteToOutputTap()` calls this method
3. **But `WriteToOutputTap()` was never called** by anything in the audio pipeline

The audio path was:
```
Audio Sources → MasterMixer → Playback Device
                              (audio goes to speakers)

TappedOutputStream ← (nothing writes here)
```

## Solution

Created `FingerprintTapModifier` - a `SoundModifier` that:

1. **Extends `SoundFlow.Abstracts.SoundModifier`** - Intercepts audio samples
2. **Implements `ProcessSample(float sample, int channel)`** - Called for each audio sample
3. **Buffers samples and writes to output tap** - Via `_audioEngine.WriteToOutputTap()`
4. **Added to MasterMixer during engine initialization** - Captures all mixed audio

### Key Files Changed

| File | Change |
|------|--------|
| `FingerprintTapModifier.cs` (NEW) | SoundModifier that captures mixed audio |
| `SoundFlowAudioEngine.cs` | Added `_fingerprintTap` field, added modifier to MasterMixer |

### FingerprintTapModifier Pattern

```csharp
public class FingerprintTapModifier : SoundModifier
{
    private readonly SoundFlowAudioEngine _audioEngine;
    private readonly float[] _sampleBuffer;
    private int _bufferIndex;

    public override float ProcessSample(float sample, int channel)
    {
        // Buffer samples
        _sampleBuffer[_bufferIndex++] = sample;

        // When buffer is full, write to output tap
        if (_bufferIndex >= _bufferSize)
        {
            _audioEngine.WriteToOutputTap(_sampleBuffer);
            _bufferIndex = 0;
        }

        // Pass through unchanged
        return sample;
    }
}
```

### Integration in Audio Engine

```csharp
// In SoundFlowAudioEngine.InitializeAsync()
_outputTap = new TappedOutputStream(...);

if (_playbackDevice != null)
{
    _fingerprintTap = new FingerprintTapModifier(this, _logger);
    _playbackDevice.MasterMixer.AddModifier(_fingerprintTap);
}
```

## Audio Pipeline After Fix

```
Audio Sources → MasterMixer → FingerprintTapModifier → Playback Device
                                      ↓
                              WriteToOutputTap()
                                      ↓
                              TappedOutputStream → Fingerprinting Service
```

## Test Commands

```bash
# Start API and wait for fingerprinting cycle
dotnet run --project src/Radio.API

# Look for success indicators:
# [INF] Fingerprint tap modifier added to MasterMixer
# [DBG] FingerprintTap: 4096 samples processed, writing 4096 to tap
# [INF] ✓ Successfully captured 1440000 samples (15.00s, 100% of requested)
# [INF] Generated fingerprint ... for 15s of audio
```

## Success Indicators

- `FingerprintTap: N samples processed` - Modifier is processing audio
- `Successfully captured X samples` - Audio tap is providing data
- `Generated fingerprint` - Fingerprinting service is working
