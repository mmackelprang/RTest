# Radio Console Deployment Guide

## Prerequisites

### Hardware
- **Raspberry Pi 5** (ARM64) with 4GB+ RAM, or any **Debian/Ubuntu x64** machine
- Audio output: USB DAC, HDMI, or 3.5mm jack
- Optional: USB Bluetooth adapter (for A2DP sink), USB RTL-SDR dongle (for radio)
- Network connection (for Google Cast discovery, TTS, fingerprinting)

### Software
- Raspberry Pi OS Bookworm (64-bit) or Debian 12 / Ubuntu 22.04+
- Root/sudo access for initial setup

### Display (for touchscreen UI)
- 12.5" x 3.75" touchscreen at 1920x576 resolution (designed for this specific display)
- Or any browser at http://host:5000 for remote access

## Quick Start

### 1. Build on Development Machine

```bash
# From the repository root
cd deploy/common

# Build for Raspberry Pi
./publish.sh arm64

# Or build for Debian x64
./publish.sh x64

# Or build for both
./publish.sh all
```

Output goes to `publish/linux-arm64/` or `publish/linux-x64/`.

### 2. Run Setup on Target

```bash
# Copy setup script and published files to the target
scp -r deploy/raspberry-pi/setup.sh pi@<ip>:~/
scp -r publish/linux-arm64/* pi@<ip>:/tmp/radio-console/

# SSH into the target
ssh pi@<ip>

# Run setup
sudo ~/setup.sh

# Copy application files
sudo cp -r /tmp/radio-console/* /opt/radio-console/
sudo chown -R radio:radio /opt/radio-console
sudo chmod +x /opt/radio-console/Radio.API
```

### 3. Start the Service

```bash
sudo systemctl start radio-console
sudo systemctl status radio-console
```

### 4. Access the UI

Open a browser to `http://<target-ip>:5000`

## Configuration

### appsettings.Production.json

The application reads configuration from `appsettings.json` and `appsettings.Production.json` (if present). Key settings:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5000" }
    }
  },
  "Database": {
    "Configuration": "./data/config/configuration.db",
    "Metrics": "./data/metrics/metrics.db",
    "Fingerprints": "./data/fingerprints/fingerprints.db",
    "Secrets": "./data/secrets/secrets.db"
  },
  "Audio": {
    "DefaultSource": "Radio",
    "SampleRate": 48000,
    "Channels": 2,
    "BufferSize": 4096
  }
}
```

### Data Directories

All persistent data is stored under `./data/`:

| Directory | Purpose |
|---|---|
| `data/config/` | Configuration database (SQLite) |
| `data/metrics/` | Performance metrics database |
| `data/fingerprints/` | Audio fingerprint cache, TTS voice cache |
| `data/secrets/` | Encrypted secrets database |
| `data/albumart/` | Cached album art files (content-addressed) |
| `data/backups/` | Configuration backups |
| `logs/` | Application logs |

### Port Configuration

| Port | Service |
|---|---|
| 5000 | HTTP (Web UI + API + SignalR) |

To change the port, edit `ASPNETCORE_URLS` in the systemd service file or `appsettings.Production.json`.

## Service Management

```bash
# Start/stop/restart
sudo systemctl start radio-console
sudo systemctl stop radio-console
sudo systemctl restart radio-console

# View status
sudo systemctl status radio-console

# View logs (live)
sudo journalctl -u radio-console -f

# View recent logs
sudo journalctl -u radio-console --since "1 hour ago"

# Enable/disable auto-start on boot
sudo systemctl enable radio-console
sudo systemctl disable radio-console
```

## Updating

```bash
# 1. Build new version on dev machine
cd deploy/common && ./publish.sh arm64

# 2. Stop the service on target
ssh pi@<ip> "sudo systemctl stop radio-console"

# 3. Copy new files (preserves data directories)
scp -r publish/linux-arm64/* pi@<ip>:/tmp/radio-update/
ssh pi@<ip> "sudo rsync -av --exclude='data' --exclude='logs' /tmp/radio-update/ /opt/radio-console/"
ssh pi@<ip> "sudo chown -R radio:radio /opt/radio-console && sudo chmod +x /opt/radio-console/Radio.API"

# 4. Start the service
ssh pi@<ip> "sudo systemctl start radio-console"
```

## Troubleshooting

### Application won't start

```bash
# Check logs for startup errors
sudo journalctl -u radio-console -n 50

# Run manually to see console output
sudo -u radio /opt/radio-console/Radio.API
```

### No audio output

```bash
# List audio devices
aplay -l

# Check PulseAudio is running
pulseaudio --check && echo "Running" || echo "Not running"

# Start PulseAudio for the radio user
sudo -u radio pulseaudio --start

# Test audio output
speaker-test -t wav -c 2
```

### Bluetooth not working

```bash
# Check Bluetooth service
sudo systemctl status bluetooth

# Scan for devices
bluetoothctl scan on

# Check the radio user is in bluetooth group
groups radio
```

### Google Cast devices not found

```bash
# Check Avahi/mDNS is running
sudo systemctl status avahi-daemon

# Test mDNS discovery
avahi-browse -a -t

# Check firewall isn't blocking mDNS (port 5353/UDP)
sudo iptables -L -n | grep 5353
```

### Audio fingerprinting not working

```bash
# Check fpcalc is available
/opt/radio-console/tools/fpcalc/fpcalc -version

# Or use system fpcalc
which fpcalc && fpcalc -version

# Test with a sample file
fpcalc -length 15 /path/to/audio/file.mp3
```

### RTL-SDR radio not working

```bash
# Check USB device is detected
lsusb | grep -i rtl

# Install RTL-SDR tools if needed
sudo apt install rtl-sdr

# Test RTL-SDR
rtl_test -t

# Blacklist kernel DVB driver (conflicts with RTL-SDR)
echo "blacklist dvb_usb_rtl28xxu" | sudo tee /etc/modprobe.d/blacklist-rtl.conf
sudo modprobe -r dvb_usb_rtl28xxu
```

### Database issues

```bash
# Check database files exist and are writable
ls -la /opt/radio-console/data/*/

# Backup all databases
cp /opt/radio-console/data/config/configuration.db /opt/radio-console/data/backups/
cp /opt/radio-console/data/fingerprints/fingerprints.db /opt/radio-console/data/backups/

# Reset configuration (start fresh)
rm /opt/radio-console/data/config/configuration.db
sudo systemctl restart radio-console
```

### Permission issues

```bash
# Fix ownership
sudo chown -R radio:radio /opt/radio-console

# Fix executable permission
sudo chmod +x /opt/radio-console/Radio.API

# Check SELinux/AppArmor (if enabled)
sudo aa-status 2>/dev/null || sudo sestatus 2>/dev/null
```
