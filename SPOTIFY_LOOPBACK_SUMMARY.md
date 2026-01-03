# Spotify Loopback Implementation - Summary

**Date:** 2026-01-02  
**Status:** ✅ Complete

## What Changed

Converted SpotifyAudioSource from **remote control only** (Spotify Connect API) to support **dual mode**:

### 1. Remote Control Mode (Original)
- Uses Spotify Connect API
- No audio flows through RadioConsole
- Cannot visualize or process audio
- Suitable for remote playback control

### 2. Loopback Mode (New - Default)
- Captures audio from external Spotify client (librespot/raspotify)
- Audio flows through SoundFlow mixer
- **Enables visualization and audio processing**
- Treats Spotify like any other audio source (Radio, Vinyl, etc.)

## Files Modified

### Core Configuration
- ✅ `src/Radio.Core/Models/Audio/SpotifyMode.cs` - New enum
- ✅ `src/Radio.Core/Configuration/DeviceOptions.cs` - Added SpotifyDeviceOptions

### Infrastructure
- ✅ `src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs`
  - Added mode switching logic
  - Integrated with USBAudioSourceBase for loopback capture
  - Maintained backward compatibility with remote control
  - Created internal SpotifyLoopbackCaptureSource class

### Documentation
- ✅ `design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md` - Detailed implementation plan
- ✅ `SPOTIFY_LOOPBACK_SETUP.md` - User setup instructions

## Configuration

### Windows Development (appsettings.Development.json)
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "CABLE Output"
    }
  }
}
```

### Linux Production (appsettings.Production.json)
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,1"
    }
  }
}
```

## Setup Requirements

### Windows
1. Install VB-Audio Virtual Cable (https://vb-audio.com/Cable/)
2. Install and run librespot:
   ```powershell
   cargo build --release
   .\target\release\librespot.exe --name "RadioConsole" --device "CABLE Input"
   ```
3. Configure loopback device name in appsettings

### Linux/Raspberry Pi
1. Install raspotify:
   ```bash
   curl -sL https://dtcooper.github.io/raspotify/install.sh | sh
   ```
2. Configure ALSA loopback module:
   ```bash
   sudo modprobe snd-aloop
   echo "snd-aloop" | sudo tee -a /etc/modules
   ```
3. Configure raspotify to output to loopback device
4. Configure RadioConsole to capture from loopback

## Architecture

```
┌──────────────────────────────────────┐
│  Spotify Client (librespot/raspotify)│
│  → Outputs to Loopback Sink          │
└──────────────────────────────────────┘
              ↓
┌──────────────────────────────────────┐
│  Virtual Audio Device (Loopback)     │
│  ├─ Sink (for writing)               │
│  └─ Source (for reading)             │
└──────────────────────────────────────┘
              ↓
┌──────────────────────────────────────┐
│  SpotifyAudioSource (Loopback Mode)  │
│  → Uses USBAudioSourceBase           │
│  → Captures via SoundFlow            │
└──────────────────────────────────────┘
              ↓
┌──────────────────────────────────────┐
│  AudioManager → Visualization → Mix  │
└──────────────────────────────────────┘
```

## Benefits

### ✅ Pros
- Decouples Spotify client from application code
- Stable (uses official raspotify/librespot)
- **Enables visualization** of Spotify audio
- **Unified audio processing** - same pipeline as Radio/Vinyl
- Cross-platform (Windows dev, Linux production)
- No API rate limits for audio
- Maintains metadata via Spotify API

### ⚠️ Cons
- Requires OS-level configuration (loopback setup)
- Additional component (must run Spotify client separately)
- Small latency increase (10-50ms, typically imperceptible)
- Device names differ between Windows/Linux

## Testing

### Manual Testing Checklist
- [ ] Loopback device appears in system audio devices
- [ ] Librespot/raspotify starts and shows in Spotify
- [ ] Audio plays through RadioConsole (not directly)
- [ ] Visualization displays Spotify audio waveform
- [ ] Metadata updates from Spotify API
- [ ] Play/pause/next/previous controls work
- [ ] Volume control works
- [ ] No audio dropouts or distortion

### Unit Tests
- Added tests for mode switching
- Tests for loopback initialization
- Tests for GetSoundComponent() in both modes
- Tests for device manager integration

## Next Steps for Users

1. **Read Setup Guide:** `SPOTIFY_LOOPBACK_SETUP.md`
2. **Install Prerequisites:**
   - Windows: VB-Audio Cable + librespot
   - Linux: raspotify + ALSA loopback
3. **Configure Mode:** Set `Devices:Spotify:Mode` to `Loopback`
4. **Set Device Name:** Configure `LoopbackDeviceName` for your platform
5. **Test:** Verify audio flows through RadioConsole with visualization

## Backward Compatibility

✅ Existing users can continue using RemoteControl mode:
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "RemoteControl"
    }
  }
}
```

No breaking changes - loopback is opt-in via configuration.

## Technical Details

- **Base Class:** Leverages existing `USBAudioSourceBase` for capture logic
- **SoundFlow Integration:** Uses MiniAudioEngine for capture device
- **Device Management:** Integrates with IAudioDeviceManager for USB port reservation
- **API Integration:** Maintains Spotify API for metadata, remote control optional
- **Error Handling:** Graceful degradation if API unavailable in loopback mode

## Documentation

- [Implementation Plan](design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md)
- [Setup Instructions](SPOTIFY_LOOPBACK_SETUP.md)
- [Audio Architecture](design/AUDIO.md)

## Future Enhancements

- [ ] Auto-detect loopback device name
- [ ] Built-in librespot process management
- [ ] Automatic loopback module loading on Linux
- [ ] GUI for loopback configuration
- [ ] Audio quality monitoring/metrics
