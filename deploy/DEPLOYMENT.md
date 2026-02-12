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

## Development Workflow: Building and Testing on the Pi

### Overview

The recommended workflow is: **develop on Windows, cross-compile, push directly to Pi via SCP**.
This is faster than git-pull-and-build-on-Pi because the Pi 5's ARM64 CPU is significantly
slower at .NET compilation than a desktop machine, and self-contained publishes avoid needing
the .NET SDK installed on the Pi at all.

### Workflow Comparison

| Approach | Build time | Requires SDK on Pi | Iteration speed |
|---|---|---|---|
| **SCP deploy script (recommended)** | ~10s on dev PC | No | Fastest — one command |
| Git pull + `dotnet build` on Pi | ~60-90s on Pi 5 | Yes (.NET 8 SDK) | Slow, needs SDK |
| Git pull + pre-built artifacts | ~10s on dev PC | No | Medium — two steps |

### Option A: Direct SCP Deploy (Recommended)

Use the `deploy-to-pi.sh` script for single-command build-and-deploy:

```bash
# First time: set your Pi's IP (or add to ~/.bashrc)
export PI_HOST=192.168.1.100
export PI_USER=pi

# Build, push, and restart in one command
./deploy/deploy-to-pi.sh

# Deploy without restarting (for inspecting the build first)
./deploy/deploy-to-pi.sh --no-restart

# Deploy and tail logs immediately
./deploy/deploy-to-pi.sh --logs
```

What the script does:
1. Cross-compiles for `linux-arm64` with `dotnet publish`
2. Stops the service on the Pi via SSH
3. Uses `rsync` over SSH to sync only changed files (preserves `data/` and `logs/`)
4. Fixes ownership/permissions
5. Restarts the service
6. Optionally tails `journalctl` so you see startup output

**SSH key setup** (do this once so you're not prompted for passwords):

```bash
# Generate key if you don't have one
ssh-keygen -t ed25519

# Copy to Pi
ssh-copy-id pi@192.168.1.100
```

### Option B: Git Pull on Pi

If you prefer using Git as the transfer mechanism (useful if you want the Pi to have
the full repo for debugging):

```bash
# One-time setup on the Pi
sudo apt install -y dotnet-sdk-8.0
cd ~ && git clone https://github.com/youruser/RTest.git
cd RTest

# Each iteration
git pull
dotnet build --configuration Release
dotnet run --project src/Radio.API
```

This is slower but gives you the ability to edit and test small fixes directly on the Pi.
Build times are ~60-90 seconds on Pi 5 vs ~10 seconds on a modern desktop.

### Option C: Hybrid — Git Pull Pre-Built Artifacts

Build on Windows and commit the publish output to a separate branch or use GitHub Actions
to produce artifacts. This works but adds complexity and bloats the repo.

### Running Tests on the Pi

Some tests require real hardware (audio devices, Bluetooth, RTL-SDR) that only exist on
the Pi. To run tests:

```bash
# Run all tests (requires .NET SDK on Pi)
dotnet test --configuration Release --verbosity normal

# Run only integration tests (most likely to differ on Pi)
dotnet test tests/Radio.IntegrationTests --configuration Release --verbosity normal

# Run a specific test
dotnet test --filter "FullyQualifiedName~BluetoothServiceTests" --configuration Release

# Run tests without building (if you built recently)
dotnet test --configuration Release --no-build
```

**Tests that behave differently on Pi:**
- Audio device enumeration — real ALSA/PulseAudio devices instead of mock
- Bluetooth A2DP sink — requires BlueZ and a BT adapter
- RTL-SDR radio tests — require a plugged-in USB dongle
- Google Cast discovery — requires mDNS on the local network
- Audio fingerprinting — `fpcalc` binary must match the ARM64 platform

**Hardware-dependent tests are skipped** by default when the required device isn't present
(check for `[Fact(Skip = ...)]` or `Assert.Skip` patterns).

### Quick Iteration Tips

**Tail logs on Pi while developing on Windows** (keep a terminal open):

```bash
ssh pi@192.168.1.100 "journalctl -u radio-console -f"
```

**Run the app manually** (instead of via systemd) for faster iteration and console output:

```bash
ssh pi@192.168.1.100
sudo systemctl stop radio-console
cd /opt/radio-console
sudo -u radio ./Radio.API
# Ctrl+C to stop, re-deploy, repeat
```

**Test a single component** without full deploy — publish just the DLL:

```bash
# Faster than full self-contained publish for quick checks
dotnet publish src/Radio.API -c Release -r linux-arm64 --no-self-contained -o publish/quick
rsync -avz publish/quick/ pi@192.168.1.100:/opt/radio-console/
```

Note: `--no-self-contained` requires the .NET 8 runtime on the Pi (`sudo apt install dotnet-runtime-8.0`),
but the transfer is much smaller (~20MB vs ~100MB).

## Updating (Production)

For production updates (when the Pi is deployed as the radio appliance):

```bash
# Single-command deploy
./deploy/deploy-to-pi.sh

# Or manual steps:
cd deploy/common && ./publish.sh arm64
ssh pi@<ip> "sudo systemctl stop radio-console"
rsync -avz --exclude='data' --exclude='logs' publish/linux-arm64/ pi@<ip>:/opt/radio-console/
ssh pi@<ip> "sudo chown -R radio:radio /opt/radio-console && sudo chmod +x /opt/radio-console/Radio.API"
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
