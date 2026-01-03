# Spotify Loopback - Quick Start

## TL;DR

Spotify now captures audio via loopback device (like Radio/Vinyl) instead of remote control, enabling **visualization** and **unified audio processing**.

## Quick Setup - Windows

```powershell
# 1. Download & Install VB-Audio Cable
# https://vb-audio.com/Cable/

# 2. Install Rust (if needed)
winget install Rustlang.Rust.GNU

# 3. Clone & build librespot
git clone https://github.com/librespot-org/librespot.git
cd librespot
cargo build --release

# 4. Run librespot
.\target\release\librespot.exe --name "RadioConsole" --device "CABLE Input"

# 5. Configure RadioConsole (appsettings.Development.json)
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "CABLE Output"
    }
  }
}
```

## Quick Setup - Raspberry Pi

```bash
# 1. Install raspotify
curl -sL https://dtcooper.github.io/raspotify/install.sh | sh

# 2. Load ALSA loopback
sudo modprobe snd-aloop
echo "snd-aloop" | sudo tee -a /etc/modules

# 3. Configure raspotify (/etc/raspotify/conf)
LIBRESPOT_NAME="RadioConsole"
LIBRESPOT_BACKEND="alsa"
LIBRESPOT_DEVICE="hw:Loopback,0,0"

# 4. Restart
sudo systemctl restart raspotify

# 5. Configure RadioConsole (appsettings.Production.json)
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,1"
    }
  }
}
```

## Configuration Options

### Loopback Mode (Default - Enables Visualization)
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

### Remote Control Mode (Original Behavior)
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "RemoteControl"
    }
  }
}
```

## Verify It Works

1. ✅ Start librespot/raspotify
2. ✅ "RadioConsole" appears in Spotify device list
3. ✅ Play a song in Spotify
4. ✅ Audio plays through RadioConsole (not directly)
5. ✅ **Visualization shows waveform** (key feature!)

## Troubleshooting

### Windows: No device in Spotify
```powershell
# Check firewall, restart librespot with --verbose
.\librespot.exe --name "RadioConsole" --device "CABLE Input" --verbose
```

### Windows: No audio captured
- Check Sound settings → Recording → "CABLE Output" shows green bars
- Verify device name matches config exactly
- Restart RadioConsole

### Linux: Raspotify won't start
```bash
sudo journalctl -u raspotify -f
lsmod | grep snd_aloop  # Should show loopback module
```

### Linux: No audio captured
```bash
# Test loopback manually
speaker-test -D hw:Loopback,0,0 -c 2 &
arecord -D hw:Loopback,0,1 -f cd -d 5 test.wav
aplay test.wav  # Should hear speaker-test audio
```

## Key Files Changed

- `src/Radio.Core/Models/Audio/SpotifyMode.cs` - New enum
- `src/Radio.Core/Configuration/DeviceOptions.cs` - Added Spotify config
- `src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs` - Dual mode support

## Full Documentation

- **Setup Guide:** `SPOTIFY_LOOPBACK_SETUP.md` (detailed instructions)
- **Implementation:** `design/SPOTIFY_LOOPBACK_IMPLEMENTATION.md` (technical details)
- **Summary:** `SPOTIFY_LOOPBACK_SUMMARY.md` (what changed)

## Why Loopback?

- ✅ Audio flows through SoundFlow mixer
- ✅ **Enables visualization** (waveform, spectrum)
- ✅ Same processing as Radio/Vinyl sources
- ✅ Stable (uses official Spotify clients)
- ✅ No API rate limits for audio
- ⚠️ Requires OS setup (one-time)
- ⚠️ Small latency (10-50ms, imperceptible)

---

**Need help?** Check `SPOTIFY_LOOPBACK_SETUP.md` for detailed troubleshooting.
