#!/bin/bash
# Spotify Loopback Setup Script for Linux/Raspberry Pi
# This script helps automate the setup of Spotify loopback mode

set -e

DEVICE_NAME="RadioConsole"
LOOPBACK_DEVICE="hw:Loopback,0,0"
BITRATE="320"
INITIAL_VOLUME="75"

echo "========================================"
echo "Spotify Loopback Setup for RadioConsole"
echo "========================================"
echo ""

# Check if running as root
if [ "$EUID" -eq 0 ]; then 
  echo "❌ Please do not run this script as root"
  echo "   Run without sudo, you will be prompted when needed"
  exit 1
fi

# Check if running on Raspberry Pi
echo "[1/6] Detecting system..."
if grep -q "Raspberry Pi" /proc/cpuinfo 2>/dev/null; then
  echo "  ✅ Raspberry Pi detected"
  IS_RPI=true
else
  echo "  ℹ️  Not a Raspberry Pi (continuing anyway)"
  IS_RPI=false
fi

# Install raspotify
echo "[2/6] Installing raspotify..."
if command -v raspotify &> /dev/null; then
  echo "  ✅ raspotify is already installed"
else
  echo "  Installing raspotify..."
  curl -sL https://dtcooper.github.io/raspotify/install.sh | sh
  
  if [ $? -eq 0 ]; then
    echo "  ✅ raspotify installed successfully"
  else
    echo "  ❌ Failed to install raspotify"
    exit 1
  fi
fi

# Configure ALSA loopback module
echo "[3/6] Configuring ALSA loopback module..."
if lsmod | grep -q snd_aloop; then
  echo "  ✅ ALSA loopback module is loaded"
else
  echo "  Loading ALSA loopback module..."
  sudo modprobe snd-aloop
  
  if lsmod | grep -q snd_aloop; then
    echo "  ✅ ALSA loopback module loaded"
  else
    echo "  ❌ Failed to load ALSA loopback module"
    exit 1
  fi
fi

# Make loopback module load at boot
echo "  Making loopback module persistent..."
if grep -q "snd-aloop" /etc/modules 2>/dev/null; then
  echo "  ✅ Loopback module already in /etc/modules"
else
  echo "snd-aloop" | sudo tee -a /etc/modules > /dev/null
  echo "  ✅ Added snd-aloop to /etc/modules"
fi

# Verify loopback device
echo "  Verifying loopback device..."
if aplay -l | grep -q Loopback; then
  echo "  ✅ Loopback device found:"
  aplay -l | grep -A 2 Loopback | sed 's/^/     /'
else
  echo "  ❌ Loopback device not found"
  exit 1
fi

# Configure raspotify
echo "[4/6] Configuring raspotify..."
RASPOTIFY_CONF="/etc/raspotify/conf"

if [ -f "$RASPOTIFY_CONF" ]; then
  echo "  Backing up existing configuration..."
  sudo cp "$RASPOTIFY_CONF" "$RASPOTIFY_CONF.backup-$(date +%Y%m%d-%H%M%S)"
fi

echo "  Writing raspotify configuration..."
sudo tee "$RASPOTIFY_CONF" > /dev/null << EOF
# Raspotify configuration for RadioConsole Loopback

# Device name shown in Spotify
LIBRESPOT_NAME="$DEVICE_NAME"

# Audio backend
LIBRESPOT_BACKEND="alsa"

# Output to loopback device
LIBRESPOT_DEVICE="$LOOPBACK_DEVICE"

# High quality audio
LIBRESPOT_BITRATE="$BITRATE"

# Initial volume
LIBRESPOT_INITIAL_VOLUME="$INITIAL_VOLUME"

# Device type
LIBRESPOT_DEVICE_TYPE="speaker"

# Enable autoplay
LIBRESPOT_AUTOPLAY="true"

# Disable audio cache to save SD card
LIBRESPOT_DISABLE_AUDIO_CACHE="true"
EOF

echo "  ✅ Raspotify configuration written"

# Restart raspotify
echo "  Restarting raspotify service..."
sudo systemctl restart raspotify

sleep 2

if sudo systemctl is-active --quiet raspotify; then
  echo "  ✅ Raspotify is running"
else
  echo "  ❌ Raspotify failed to start"
  echo "     Check logs with: sudo journalctl -u raspotify -f"
  exit 1
fi

# Generate RadioConsole configuration
echo "[5/6] Generating RadioConsole configuration..."

CONFIG_PATH="./appsettings.Production.Spotify.json"

cat > "$CONFIG_PATH" << EOF
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,1"
    }
  },
  "Spotify": {
    "ClientID": "\${secret:spotify_client_id}",
    "ClientSecret": "\${secret:spotify_client_secret}",
    "RefreshToken": "\${secret:spotify_refresh_token}"
  }
}
EOF

echo "  ✅ Configuration saved to: $CONFIG_PATH"

# Test loopback
echo "[6/6] Testing loopback functionality..."
echo "  This will play a test tone to the loopback device for 3 seconds"
read -p "  Run loopback test? (y/n) " -n 1 -r
echo

if [[ $REPLY =~ ^[Yy]$ ]]; then
  echo "  Starting test tone on loopback..."
  
  # Play test tone in background
  speaker-test -D hw:Loopback,0,0 -c 2 -t sine -f 440 > /dev/null 2>&1 &
  TEST_PID=$!
  
  sleep 1
  
  # Record from loopback
  echo "  Recording from loopback..."
  arecord -D hw:Loopback,0,1 -f cd -d 2 /tmp/loopback-test.wav > /dev/null 2>&1
  
  # Stop test tone
  kill $TEST_PID 2>/dev/null || true
  
  # Check if recording succeeded
  if [ -f /tmp/loopback-test.wav ] && [ -s /tmp/loopback-test.wav ]; then
    echo "  ✅ Loopback test successful!"
    echo "     Recorded file: /tmp/loopback-test.wav"
    
    read -p "  Play back the recording? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
      aplay /tmp/loopback-test.wav
    fi
  else
    echo "  ❌ Loopback test failed"
    echo "     Recording file is empty or missing"
  fi
else
  echo "  ⏭️  Skipped loopback test"
fi

echo ""
echo "========================================"
echo "Setup Complete! 🎉"
echo "========================================"
echo ""
echo "Next steps:"
echo "  1. Verify raspotify status:"
echo "     sudo systemctl status raspotify"
echo ""
echo "  2. Check raspotify logs:"
echo "     sudo journalctl -u raspotify -f"
echo ""
echo "  3. Copy $CONFIG_PATH to your RadioConsole"
echo "     configuration directory"
echo ""
echo "  4. Start RadioConsole and select Spotify source"
echo ""
echo "  5. Open Spotify app and connect to '$DEVICE_NAME'"
echo ""
echo "  6. Play a song and verify visualization works"
echo ""
echo "Troubleshooting:"
echo "  - View this script's actions: cat $0"
echo "  - Read full guide: SPOTIFY_LOOPBACK_SETUP.md"
echo "  - Check logs: sudo journalctl -u raspotify -f"
echo ""
echo "To revert changes:"
echo "  sudo systemctl stop raspotify"
echo "  sudo apt remove raspotify"
echo "  sudo rmmod snd-aloop"
echo ""
echo "========================================"
