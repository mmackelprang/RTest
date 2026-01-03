# Spotify Loopback Feature - README Addition

## 🎵 Spotify Integration Update

**Spotify now supports dual operation modes:**

### 1. Loopback Mode (Default - New! 🎉)
- Captures audio from external Spotify client (librespot/raspotify) via virtual loopback device
- **Enables visualization** - spectrum analyzer, waveform, VU meters
- **Unified audio processing** - same pipeline as Radio/Vinyl/File sources
- Audio flows through SoundFlow mixer
- Requires one-time OS-level loopback setup

### 2. Remote Control Mode (Original)
- Uses Spotify Connect API for playback control
- No audio flows through application
- Cannot visualize or process audio
- Simpler setup, no loopback required

## Quick Setup

### Windows Development
```powershell
# 1. Install VB-Audio Virtual Cable
# Download: https://vb-audio.com/Cable/

# 2. Run librespot
.\librespot.exe --name "RadioConsole" --device "CABLE Input"

# 3. Configure (appsettings.Development.json)
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "CABLE Output"
    }
  }
}
```

### Linux/Raspberry Pi Production
```bash
# 1. Install raspotify
curl -sL https://dtcooper.github.io/raspotify/install.sh | sh

# 2. Setup ALSA loopback
sudo modprobe snd-aloop
echo "snd-aloop" | sudo tee -a /etc/modules

# 3. Configure (/etc/raspotify/conf)
LIBRESPOT_DEVICE="hw:Loopback,0,0"

# 4. Configure (appsettings.Production.json)
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,1"
    }
  }
}
```

## Documentation

- 📘 **Quick Start:** [SPOTIFY_LOOPBACK_QUICKSTART.md](SPOTIFY_LOOPBACK_QUICKSTART.md)
- 📗 **Full Setup Guide:** [SPOTIFY_LOOPBACK_SETUP.md](SPOTIFY_LOOPBACK_SETUP.md)
- 📙 **Implementation Details:** [design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md](design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md)
- 📕 **Summary:** [SPOTIFY_LOOPBACK_SUMMARY.md](SPOTIFY_LOOPBACK_SUMMARY.md)

## Benefits

✅ **Visualization enabled** - See Spotify audio in real-time  
✅ **Unified processing** - Same audio pipeline as other sources  
✅ **Stable** - Uses official Spotify clients (raspotify/librespot)  
✅ **Cross-platform** - Windows (dev) and Linux (production)  
✅ **Backward compatible** - Can still use RemoteControl mode  

⚠️ **Setup required** - One-time OS configuration for loopback device  
⚠️ **Additional process** - Must run Spotify client separately  

## Audio Architecture with Loopback

```
Spotify App (Phone/Desktop)
    ↓ (Spotify Connect)
librespot/raspotify
    ↓ (Audio Output)
Virtual Loopback Device
    ↓ (Audio Capture)
SpotifyAudioSource → SoundFlow Mixer → Visualization → Speakers/Cast
```

---

**Add this section to README.md under "Audio Sources" or create a new "Spotify Configuration" section.**
