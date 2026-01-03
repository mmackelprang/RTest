# Spotify Loopback Setup Instructions

## Overview

This guide explains how to configure Spotify loopback mode on Windows for development and Raspberry Pi for production.

## Windows Setup (Development)

### Option 1: VB-Audio Virtual Cable (Recommended)

**VB-Audio Virtual Cable** provides an isolated virtual audio device specifically for routing audio between applications.

#### 1. Install VB-Audio Virtual Cable

1. Download from: https://vb-audio.com/Cable/
2. Extract the ZIP file
3. Right-click `VBCABLE_Setup_x64.exe` → Run as Administrator
4. Follow installation wizard
5. Restart computer when prompted

#### 2. Verify Installation

1. Right-click speaker icon in system tray → Open Sound settings
2. Go to "Sound Control Panel" → "Recording" tab
3. You should see "CABLE Output (VB-Audio Virtual Cable)"
4. Go to "Playback" tab
5. You should see "CABLE Input (VB-Audio Virtual Cable)"

#### 3. Install Spotify Connect Client (librespot)

```powershell
# Install Rust toolchain (if not already installed)
winget install Rustlang.Rust.GNU

# Clone librespot
git clone https://github.com/librespot-org/librespot.git
cd librespot

# Build release version
cargo build --release

# The executable will be at: target\release\librespot.exe
```

#### 4. Run Librespot with VB-Cable Output

```powershell
# Navigate to librespot directory
cd path\to\librespot

# Run librespot pointing to CABLE Input (the virtual playback device)
.\target\release\librespot.exe `
  --name "RadioConsole" `
  --backend rodio `
  --device "CABLE Input (VB-Audio Virtual Cable)" `
  --bitrate 320 `
  --initial-volume 75
```

**Note:** Keep this PowerShell window open while using Spotify.

#### 5. Configure RadioConsole

Edit `appsettings.Development.json`:

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

#### 6. Test the Setup

1. Start RadioConsole application
2. Open Spotify app on your phone or desktop
3. Click "Connect to a device" → Select "RadioConsole"
4. Play a song
5. Verify:
   - Audio plays through RadioConsole
   - Visualization displays audio data
   - No audio plays directly from Spotify client

### Option 2: Windows Stereo Mix

**Stereo Mix** captures ALL system audio. Less ideal but requires no additional software.

#### 1. Enable Stereo Mix

1. Right-click speaker icon → Open Sound settings
2. Click "Sound Control Panel"
3. Go to "Recording" tab
4. Right-click empty space → "Show Disabled Devices"
5. Find "Stereo Mix"
6. Right-click → Enable
7. Right-click → "Set as Default Device"

#### 2. Configure RadioConsole

Edit `appsettings.Development.json`:

```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "Stereo Mix"
    }
  }
}
```

**Warning:** Stereo Mix will capture ALL system sounds (notifications, alerts, other apps).

---

## Linux/Raspberry Pi Setup (Production)

### 1. Install Raspotify

```bash
# Add raspotify repository
curl -sL https://dtcooper.github.io/raspotify/install.sh | sh

# Verify installation
systemctl status raspotify
```

### 2. Configure ALSA Loopback Module

```bash
# Load loopback module
sudo modprobe snd-aloop

# Make it permanent
echo "snd-aloop" | sudo tee -a /etc/modules

# Verify loopback device
aplay -l | grep -i loopback
# Should show: card X: Loopback [Loopback], device 0: Loopback PCM [Loopback PCM]
```

### 3. Configure Raspotify

Edit raspotify configuration:

```bash
sudo nano /etc/raspotify/conf
```

Add/modify these lines:

```bash
# Device name shown in Spotify
LIBRESPOT_NAME="RadioConsole"

# Audio backend (ALSA)
LIBRESPOT_BACKEND="alsa"

# Output to loopback device (subdevice 0 = playback, 1 = capture)
LIBRESPOT_DEVICE="hw:Loopback,0,0"

# High quality audio
LIBRESPOT_BITRATE="320"

# Initial volume
LIBRESPOT_INITIAL_VOLUME="75"

# Device type
LIBRESPOT_DEVICE_TYPE="speaker"
```

Save and restart raspotify:

```bash
sudo systemctl restart raspotify
sudo systemctl status raspotify
```

### 4. Configure RadioConsole

Edit `appsettings.Production.json`:

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

**Note:** We use subdevice 1 for capture (0 is for playback by raspotify).

### 5. Test ALSA Loopback

```bash
# Terminal 1: Play audio to loopback
speaker-test -D hw:Loopback,0,0 -c 2

# Terminal 2: Capture from loopback
arecord -D hw:Loopback,0,1 -f cd test.wav

# Stop after a few seconds (Ctrl+C), then play back
aplay test.wav
```

---

## Troubleshooting

### Windows: Librespot Not Showing in Spotify

**Symptoms:** "RadioConsole" device doesn't appear in Spotify app

**Solutions:**
1. Check firewall: Allow librespot.exe through Windows Defender
2. Verify network: Ensure PC and phone are on same network
3. Check process: `Get-Process librespot` in PowerShell
4. Restart librespot with verbose logging:
   ```powershell
   .\librespot.exe --name "RadioConsole" --backend rodio --device "CABLE Input" --verbose
   ```

### Windows: No Audio in RadioConsole

**Symptoms:** Spotify plays but RadioConsole doesn't capture audio

**Solutions:**
1. Verify loopback device in Sound settings:
   - Recording tab → "CABLE Output" should show green bars when Spotify plays
2. Check RadioConsole device name matches configuration
3. Restart RadioConsole after changing configuration
4. Verify SoundFlow can see the device:
   ```csharp
   // Check logs for "Available capture devices:"
   ```

### Linux: Raspotify Won't Start

**Symptoms:** `systemctl status raspotify` shows failed

**Solutions:**
1. Check configuration:
   ```bash
   sudo raspotify --check-config
   ```
2. Verify loopback module:
   ```bash
   lsmod | grep snd_aloop
   ```
3. Check logs:
   ```bash
   sudo journalctl -u raspotify -f
   ```
4. Try manual start:
   ```bash
   sudo systemctl stop raspotify
   librespot --name "RadioConsole" --device "hw:Loopback,0,0" --backend alsa
   ```

### Linux: No Audio Captured

**Symptoms:** Raspotify plays but RadioConsole doesn't receive audio

**Solutions:**
1. Verify loopback routing:
   ```bash
   # Play sine wave to loopback
   speaker-test -D hw:Loopback,0,0 -c 2 -t sine
   
   # In another terminal, record from loopback
   arecord -D hw:Loopback,0,1 -f cd -d 5 test.wav
   
   # Play back to verify
   aplay test.wav
   ```
2. Check RadioConsole device configuration matches loopback subdevice
3. Verify permissions:
   ```bash
   sudo usermod -a -G audio $USER
   ```

### Audio Quality Issues

**Symptoms:** Audio sounds distorted, choppy, or has dropouts

**Solutions:**
1. Increase buffer size in SoundFlow initialization
2. Verify sample rate matching (44100 or 48000 Hz)
3. Check CPU usage: `top` or Task Manager
4. Reduce bitrate in librespot/raspotify (try 160 or 96)

---

## Configuration Reference

### Windows Development

```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "CABLE Output"
    }
  },
  "Spotify": {
    "ClientID": "${secret:spotify_client_id}",
    "ClientSecret": "${secret:spotify_client_secret}",
    "RefreshToken": "${secret:spotify_refresh_token}"
  }
}
```

### Linux Production

```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,1"
    }
  },
  "Spotify": {
    "ClientID": "${secret:spotify_client_id}",
    "ClientSecret": "${secret:spotify_client_secret}",
    "RefreshToken": "${secret:spotify_refresh_token}"
  }
}
```

### Switching to Remote Control Mode

If you prefer the original remote control behavior (no visualization):

```json
{
  "Devices": {
    "Spotify": {
      "Mode": "RemoteControl"
    }
  }
}
```

---

## Performance Notes

### Latency
- Typical loopback latency: 10-50ms
- Not noticeable for casual listening
- May be noticeable for rhythm games or live performance

### CPU Usage
- Loopback mode: +5-10% CPU vs RemoteControl
- Negligible on Raspberry Pi 5
- May be more significant on older Pi models

### Audio Quality
- No quality loss with loopback at 320kbps
- Bit-perfect audio routing
- Sample rate: 44.1kHz or 48kHz (configurable)

---

## Next Steps

1. **Setup Complete**: Verify Spotify appears in device list and plays through RadioConsole
2. **Test Visualization**: Check that spectrum analyzer shows Spotify audio
3. **Configure Secrets**: Set up Spotify API credentials for metadata
4. **Test All Features**: Play/pause, next/previous, shuffle, repeat
5. **Production Deployment**: Follow Linux setup for Raspberry Pi

For issues, check logs in `logs/` directory or enable verbose logging in configuration.
