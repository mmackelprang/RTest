# Radio Console Deployment Guide

## Overview

**Grandpa Anderson's Console Radio Remade** — a modern audio command center for Raspberry Pi
that restores vintage console radio functionality with Bluetooth A2DP audio reception,
internet radio (RTL-SDR), Spotify, Google Cast, audio fingerprinting, and a Blazor Server UI.

**Target platform:** Raspberry Pi 5 (ARM64, Raspberry Pi OS Bookworm 64-bit)
**Stack:** .NET 8, ASP.NET Core, Blazor Server, SoundFlow (MiniAudio), PipeWire, BlueZ 5

## Architecture

The application runs as two separate systemd services:

```
/opt/radio-console/
  api/              ← Radio.API binaries (audio engine, REST, SignalR)
  web/              ← Radio.Web binaries (Blazor Server UI)
  data/             ← Shared data (config, metrics, fingerprints, secrets, albumart, backups)
  logs/             ← Shared logs
  tools/            ← fpcalc, etc.
```

| Service | Port | Purpose |
|---|---|---|
| `radio-api.service` | 5000 | REST API, SignalR hubs, audio engine, Bluetooth, Cast |
| `radio-web.service` | 5002 | Blazor Server UI, depends on radio-api |

`radio-web` has `Requires=radio-api.service` — stopping the API automatically stops the Web UI.
Both services use `WorkingDirectory=/opt/radio-console` so relative paths (`./data`, `logs/`) resolve to the shared directories.

### Audio Pipeline

```
Phone (A2DP) ──► BlueZ ──► PipeWire bluez_input
                                    │
                    ┌───────────────┘
                    ▼
              bt_capture          (PipeWire null sink)
                    │
              bt_capture.monitor  (PulseAudio capture source)
                    │
              MiniAudio capture   (SoundFlow AudioCaptureDevice)
                    │
              SoundFlow Mixer ──► Visualization (FFT/Levels)
                    │             Fingerprinting (fpcalc → AcoustID)
                    │             Volume/Balance control
                    ▼
              ALSA output         (headphones / USB DAC)
              HTTP stream         (for Google Cast)
```

Other audio sources (Radio/SDR, Spotify, File Player, TTS) feed directly into the
SoundFlow mixer without the null sink intermediary.

## Prerequisites

### Hardware

| Component | Required? | Purpose |
|---|---|---|
| Raspberry Pi 5 (4GB+) | Yes | Application host |
| Audio output (3.5mm, USB DAC, or HDMI) | Yes | Audio playback |
| Network connection (Ethernet or WiFi) | Yes | Google Cast discovery, API access, fingerprinting |
| USB RTL-SDR dongle | Optional | FM/AM radio reception via SDR |
| USB Bluetooth adapter | Optional | Only if built-in BT is insufficient |
| 12.5" x 3.75" touchscreen (1920x576) | Optional | Designed display; any browser works too |

### Software

- Raspberry Pi OS **Bookworm** (64-bit) — other Debian 12+ distros should work
- Root/sudo access for initial setup
- Internet access during setup (for package installation)

## Quick Start

### 1. Run Setup on the Pi

```bash
# Clone the repository (or copy the deploy directory)
git clone https://github.com/youruser/RTest.git ~/RTest
cd ~/RTest

# Run the setup script
sudo deploy/raspberry-pi/setup.sh
```

The setup script installs all system dependencies, creates the application user,
configures PipeWire/WirePlumber for Bluetooth A2DP sink, and installs both systemd services.

### 2. Build and Deploy from Windows

```powershell
# One-command build and deploy from your Windows dev machine
.\deploy\Deploy-ToPi.ps1 -PiHost piradio -PiUser mmack

# Or with log tailing
.\deploy\Deploy-ToPi.ps1 -PiHost piradio -PiUser mmack -Logs
```

Or build manually:

```bash
# From the repository root (on dev machine)
cd deploy/common
./publish.sh arm64

# Copy to Pi
rsync -avz --delete publish/linux-arm64/api/ mmack@piradio:/opt/radio-console/api/
rsync -avz --delete publish/linux-arm64/web/ mmack@piradio:/opt/radio-console/web/
ssh mmack@piradio "sudo chown -R radio:radio /opt/radio-console && sudo chmod +x /opt/radio-console/api/Radio.API /opt/radio-console/web/Radio.Web"
```

### 3. Start the Services

```bash
sudo systemctl start radio-api radio-web
sudo systemctl status radio-api radio-web
```

### 4. Access

- **API:** `http://piradio:5000` (Swagger at `/swagger`)
- **Web UI:** `http://piradio:5002`

## Cross-Compilation Note

Radio.Infrastructure multi-targets (`net8.0` and `net8.0-windows10.0.19041.0`). When
cross-compiling from Windows for Linux, you **must** pass `-f net8.0` to override the
conditional Windows TFM:

```bash
dotnet publish ... --runtime linux-arm64 -f net8.0
```

The deploy scripts handle this automatically.

## System Dependencies

The `setup.sh` script installs these automatically. Listed here for reference and
manual setup.

### Core Audio

| Package | Purpose |
|---|---|
| `pipewire` | Audio server (replaces PulseAudio) |
| `pipewire-pulse` | PulseAudio compatibility layer |
| `wireplumber` | PipeWire session/policy manager |
| `libspa-0.2-bluetooth` | PipeWire Bluetooth codec plugins (SBC, LDAC, aptX, opus) |
| `libasound2-dev` | ALSA audio library (SoundFlow/MiniAudio dependency) |
| `libmp3lame-dev` | LAME MP3 encoder (HTTP streaming to Google Cast) |

### Bluetooth

| Package | Purpose |
|---|---|
| `bluez` | BlueZ Bluetooth stack (v5.82+) |

PipeWire+WirePlumber handle the Bluetooth audio profile integration through
`libspa-0.2-bluetooth`. No separate `pulseaudio-module-bluetooth` is needed.

### RTL-SDR Radio (optional)

| Package | Purpose |
|---|---|
| `librtlsdr-dev` | RTL-SDR native library (P/Invoke target for `RtlSdrDevice.cs`) |
| `rtl-sdr` | CLI tools (`rtl_test`, `rtl_fm`) for diagnostics |

The DVB kernel driver conflicts with the RTL-SDR userspace driver. The setup script
blacklists it:

```bash
echo "blacklist dvb_usb_rtl28xxu" | sudo tee /etc/modprobe.d/blacklist-rtl.conf
sudo modprobe -r dvb_usb_rtl28xxu
```

### Audio Fingerprinting

| Package | Purpose |
|---|---|
| `libchromaprint-tools` | `fpcalc` binary for Chromaprint audio fingerprinting |

The app shells out to `fpcalc` to generate audio fingerprints, then queries the
AcoustID API to identify tracks. Path configured in `appsettings.json`:

```json
{
  "Fingerprinting": {
    "FpcalcPath": "/usr/bin/fpcalc"
  }
}
```

An AcoustID API key is required. Register at https://acoustid.org/new-application
and set the key via the System Config page or API:

```bash
curl -X POST http://piradio:5000/api/configuration/secrets \
  -H "Content-Type: application/json" \
  -d '{"key": "Fingerprinting:AcoustId:ApiKey", "value": "your-api-key"}'
```

### Network Discovery

| Package | Purpose |
|---|---|
| `avahi-daemon` | mDNS/DNS-SD for Google Cast device discovery |
| `avahi-utils` | `avahi-browse` CLI for diagnostics |

### .NET Runtime

| Package | Purpose |
|---|---|
| `aspnetcore-runtime-8.0` | ASP.NET Core runtime (if not using self-contained publish) |

Self-contained publishes bundle the runtime, so this is only needed for
framework-dependent deployments (`Deploy-ToPi.ps1 -Quick`).

## Bluetooth A2DP Sink Configuration

The Pi needs three config files to act as a Bluetooth audio receiver. The setup
script installs these automatically; this section explains what they do.

### 1. WirePlumber: Enable A2DP Sink Role + Disable Seat Monitoring

**File:** `~/.config/wireplumber/wireplumber.conf.d/50-bluez-a2dp-sink.conf`

```
wireplumber.profiles = {
    main = {
        monitor.bluez.seat-monitoring = disabled
    }
}

monitor.bluez.properties = {
    bluez5.roles = [ a2dp_sink, a2dp_source ]
    bluez5.codecs = [ sbc, sbc_xq ]
    bluez5.enable-sbc-xq = true
    bluez5.hfphsp-backend = "native"
}
```

This config does three things:

1. **Disables seat monitoring.** WirePlumber's `bluez.lua` monitor has a logind-based
   seat monitoring feature that only creates the BlueZ monitor when the seat state is
   "active". On Raspberry Pi OS, LightDM crash-cycles cause logind to report "online"
   (not "active"), which prevents the BlueZ monitor from initializing — no Audio Sink
   UUID, no A2DP, no Bluetooth audio. Disabling seat monitoring ensures the BlueZ
   monitor starts unconditionally regardless of seat/display manager state.

2. **Registers A2DP sink + source roles.** Tells WirePlumber's BlueZ SPA plugin to
   register both **sink** (receive audio from phones) and **source** (send audio to
   speakers) A2DP roles. Without this, the Pi only acts as a source and phones can't
   stream audio to it.

3. **Pins codecs to SBC/SBC-XQ.** Without codec pinning, phones may negotiate
   vendor-specific codecs (AAC, LDAC, aptX) that complete AVDTP negotiation but leave
   the transport stuck in "idle" state — AVDTP START is rejected with "Bad State (49)".
   Pinning to SBC avoids this and provides reliable A2DP streaming.

After applying, verify with `bluetoothctl show` — you should see:
```
UUID: Audio Sink    (0000110b-...)
UUID: Audio Source  (0000110a-...)
```

### 2. PipeWire: Virtual Null Sink for BT Capture

**File:** `~/.config/pipewire/pipewire.conf.d/bt-capture-sink.conf`

```
context.objects = [
    {
        factory = adapter
        args = {
            factory.name    = support.null-audio-sink
            node.name       = "bt_capture"
            node.description = "Bluetooth Audio Capture"
            media.class     = Audio/Sink
            object.linger   = true
            audio.position  = [ FL FR ]
            audio.rate      = 48000
            monitor.channel-volumes = true
            monitor.passthrough     = true
            priority.session = 0
            priority.driver  = 0
            node.passive     = true
        }
    }
]
```

Creates a persistent virtual audio sink called `bt_capture`. Its monitor source
(`bt_capture.monitor`) appears as a PulseAudio capture device that MiniAudio/SoundFlow
can read from.

**Why a null sink?** When a phone streams A2DP audio to the Pi, PipeWire creates a
`bluez_input` stream and routes it directly to the default audio output. This bypasses
our application entirely — no visualization, no fingerprinting, no volume control.
The null sink intercepts BT audio so it flows through our app's pipeline instead.

### 3. WirePlumber: Route BT Input to Null Sink

**File:** `~/.config/wireplumber/wireplumber.conf.d/51-bt-capture-routing.conf`

```
monitor.bluez.rules = [
    {
        matches = [
            {
                node.name = "~bluez_input.*"
            }
        ]
        actions = {
            update-props = {
                node.target = "bt_capture"
            }
        }
    }
]
```

Routes all `bluez_input.*` streams (A2DP audio from connected phones) to the
`bt_capture` null sink instead of the default hardware output. The application
captures from `bt_capture.monitor`, processes through SoundFlow, and outputs to
the real hardware sink.

**Important:** The WirePlumber property is `node.target` (not `target.node`). The
`find-defined-target` linking policy checks `node.target` in node properties for
string-based node name matching. Using the wrong property silently fails.

### 4. WirePlumber Seat Monitoring (Background)

WirePlumber's BlueZ monitor (`bluez.lua`) has a logind-based seat monitoring
feature. When enabled, it only creates the BlueZ audio monitor when the desktop
session's seat state is "active". On Raspberry Pi OS with LightDM, logind D-Bus
activation timeouts cause LightDM to crash-cycle, which makes the seat state
oscillate between "online" and "active". This causes WirePlumber to repeatedly
destroy and recreate the BlueZ monitor, resulting in A2DP endpoint cycling and
Bluetooth connections dropping every ~50 seconds.

The fix is `monitor.bluez.seat-monitoring = disabled` in the WirePlumber profile
(included in `50-bluez-a2dp-sink.conf` above). This makes the BlueZ monitor start
unconditionally, regardless of seat state or display manager health. LightDM can
remain enabled and running normally.

### Verifying BT Audio Setup

After setup, restart PipeWire/WirePlumber:

```bash
systemctl --user restart pipewire wireplumber
```

Check the null sink exists:

```bash
wpctl status
# Should show under Sinks:
#   32. Bluetooth Audio Capture  [vol: 1.00]

pactl list short sources
# Should show:
#   bt_capture.monitor  PipeWire  float32le 2ch 48000Hz
```

Check A2DP UUIDs (may take ~30 seconds after WirePlumber starts):

```bash
bluetoothctl show | grep "Audio"
# Audio Sink    (0000110b-...)  ← Pi can RECEIVE audio
# Audio Source  (0000110a-...)  ← Pi can SEND audio
```

## Configuration

### appsettings.Production.json

Both services read `appsettings.Production.json` from their respective binary directories.
The API settings are the primary configuration; the Web settings mainly configure the
API connection URL.

**API** (`/opt/radio-console/api/appsettings.Production.json`):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5000" }
    }
  },
  "Fingerprinting": {
    "FpcalcPath": "/usr/bin/fpcalc"
  }
}
```

**Web** (`/opt/radio-console/web/appsettings.Production.json`):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5002" }
    }
  },
  "ApiBaseUrl": "http://localhost:5000"
}
```

### Data Directories

All persistent data is stored under `./data/` (relative to the working directory `/opt/radio-console`):

| Directory | Purpose |
|---|---|
| `data/config/` | Configuration database (SQLite) — audio preferences, radio presets |
| `data/metrics/` | Performance metrics database |
| `data/fingerprints/` | Audio fingerprint cache, track metadata, play history |
| `data/secrets/` | Encrypted secrets database (API keys) |
| `data/albumart/` | Cached album art files (content-addressed SHA256) |
| `data/backups/` | Timestamped database backups |
| `logs/` | Application logs (daily rotation) |

### Ports

| Port | Service |
|---|---|
| 5000 | Radio.API — REST API, SignalR hubs, audio stream |
| 5002 | Radio.Web — Blazor Server UI |

### Secrets

API keys are stored encrypted in `data/secrets/secrets.db`. Set them via the
System Config page or API:

| Secret | Purpose | Registration |
|---|---|---|
| `Fingerprinting:AcoustId:ApiKey` | Audio fingerprint identification | https://acoustid.org/new-application |
| `Spotify:ClientId` | Spotify playback | https://developer.spotify.com |
| `Spotify:ClientSecret` | Spotify playback | (same) |
| `TTS:GoogleApiKey` | Google Cloud TTS | https://console.cloud.google.com |

Secrets are encrypted with a machine-specific key. If migrating from another machine,
re-enter secrets on the Pi — they won't decrypt across machines.

## Service Management

```bash
# Start/stop/restart both services
sudo systemctl start radio-api radio-web
sudo systemctl stop radio-web radio-api
sudo systemctl restart radio-api radio-web

# View status
sudo systemctl status radio-api radio-web

# View logs (live, both services interleaved)
sudo journalctl -u radio-api -u radio-web -f

# View logs for a single service
sudo journalctl -u radio-api -f
sudo journalctl -u radio-web -f

# View recent logs
sudo journalctl -u radio-api -u radio-web --since "1 hour ago"

# Enable/disable auto-start on boot
sudo systemctl enable radio-api radio-web
sudo systemctl disable radio-web radio-api
```

**Note:** When stopping, stop `radio-web` first (or let systemd handle it — stopping
`radio-api` automatically stops `radio-web` due to the `Requires=` dependency).

## Development Workflow

### Recommended: Build on Windows, Deploy via SCP

```powershell
# Single command build + deploy + restart (both services)
.\deploy\Deploy-ToPi.ps1

# Deploy without restarting
.\deploy\Deploy-ToPi.ps1 -NoRestart

# Deploy and tail logs
.\deploy\Deploy-ToPi.ps1 -Logs

# Framework-dependent (smaller, needs .NET runtime on Pi)
.\deploy\Deploy-ToPi.ps1 -Quick

# Override Pi host/user
.\deploy\Deploy-ToPi.ps1 -PiHost 192.168.86.44 -PiUser mmack
```

### Alternative: Git Pull + dotnet run on Pi

Useful for quick debugging when the .NET SDK is installed on the Pi:

```bash
ssh mmack@piradio
cd ~/RTest

# Stop services first
sudo systemctl stop radio-web radio-api

# Run API
dotnet run --project src/Radio.API

# In another terminal, run Web
dotnet run --project src/Radio.Web
```

Build time: ~60-90s on Pi 5 vs ~10s on Windows desktop.

### SSH Key Setup (one-time)

```powershell
ssh-keygen -t ed25519
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh mmack@piradio "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

### Tail Logs While Developing

Keep a terminal open:

```bash
ssh mmack@piradio "journalctl -u radio-api -u radio-web -f"
```

### Run Manually (instead of systemd)

```bash
sudo systemctl stop radio-web radio-api
cd /opt/radio-console

# Terminal 1: API
sudo -u radio ./api/Radio.API

# Terminal 2: Web
sudo -u radio ASPNETCORE_URLS=http://0.0.0.0:5002 ApiBaseUrl=http://localhost:5000 ./web/Radio.Web
```

## Migrating Data to the Pi

### What to Migrate

| File | Contains | Required? |
|---|---|---|
| `data/config/configuration.db` | Audio prefs, radio presets, config | Yes |
| `data/secrets/secrets.db` | Encrypted API keys | Re-enter on Pi instead |
| `data/fingerprints/fingerprints.db` | Fingerprint cache, play history | Optional |
| `data/albumart/` | Cached album art | Optional |
| `data/metrics/metrics.db` | Performance metrics | Optional |

### Migration Steps

```powershell
# Stop the services on Pi
ssh mmack@piradio "sudo systemctl stop radio-web radio-api 2>/dev/null; true"

# Ensure data dirs exist
ssh mmack@piradio "sudo mkdir -p /opt/radio-console/data/{config,secrets,fingerprints,albumart,metrics,backups} && sudo chown -R radio:radio /opt/radio-console/data"

# Copy databases
scp data/config/configuration.db mmack@piradio:/tmp/config.db
ssh mmack@piradio "sudo cp /tmp/config.db /opt/radio-console/data/config/configuration.db && sudo chown radio:radio /opt/radio-console/data/config/configuration.db && rm /tmp/config.db"

# Start the services
ssh mmack@piradio "sudo systemctl start radio-api radio-web"
```

Secrets are machine-specific — re-enter API keys on the Pi via the System Config page.

## Troubleshooting

### No audio output

```bash
# List audio devices
aplay -l

# Check PipeWire is running
systemctl --user status pipewire pipewire-pulse wireplumber

# Test audio
speaker-test -t wav -c 2

# Check default sink
wpctl status
```

### Bluetooth not working

```bash
# Check adapter status
bluetoothctl show

# Power on if needed
bluetoothctl power on

# Check A2DP UUIDs (wait ~30s after WirePlumber starts)
bluetoothctl show | grep "Audio"

# List paired devices
bluetoothctl devices

# Check BlueZ logs
journalctl -u bluetooth -f
```

### BT connects but no audio through app

```bash
# Verify bt_capture null sink exists
wpctl status | grep -A5 Sinks
pactl list short sources | grep bt_capture

# When BT audio is playing, check routing
wpctl status | grep -A10 Streams
# Should show: bluez_input.* → bt_capture (not headphones)

# Check app sees the capture device (in app logs)
journalctl -u radio-api | grep -i "capture device"
```

### BT connection drops

```bash
# Check if WirePlumber is cycling (endpoints unregister/re-register)
journalctl -u bluetooth -f | grep "Endpoint"
# If endpoints register/unregister every ~50s, seat monitoring may be active.
# Verify it's disabled:
#   Check 50-bluez-a2dp-sink.conf has monitor.bluez.seat-monitoring = disabled

# Check seat state (should be irrelevant if seat monitoring is disabled)
loginctl show-seat seat0 -p ActiveState

# Remove stale/offline paired devices that cause reconnect loops
bluetoothctl devices
bluetoothctl remove <address-of-offline-device>

# Check interference: only one A2DP connection at a time
```

### Google Cast devices not found

```bash
# Check Avahi/mDNS
sudo systemctl status avahi-daemon
avahi-browse -a -t

# Check firewall (port 5353/UDP for mDNS)
sudo iptables -L -n | grep 5353
```

### Audio fingerprinting not working

```bash
# Check fpcalc
fpcalc -version
# or
/opt/radio-console/tools/fpcalc/fpcalc -version

# Test with a file
fpcalc -length 15 /path/to/audio.mp3

# Check AcoustID API key is set
curl http://piradio:5000/api/configuration/secrets
```

### RTL-SDR radio not working

```bash
# Check USB device
lsusb | grep -i rtl

# Check kernel driver blacklist
cat /etc/modprobe.d/blacklist-rtl.conf

# Test RTL-SDR
rtl_test -t
```

### Database issues

```bash
# Check database files
ls -la /opt/radio-console/data/*/

# Reset configuration (start fresh)
sudo systemctl stop radio-web radio-api
rm /opt/radio-console/data/config/configuration.db
sudo systemctl start radio-api radio-web
```

### Permission issues

```bash
sudo chown -R radio:radio /opt/radio-console
sudo chmod +x /opt/radio-console/api/Radio.API /opt/radio-console/web/Radio.Web
```

### Web UI can't connect to API

```bash
# Check API is running
curl http://localhost:5000/api/audio

# Check Web service environment
systemctl show radio-web | grep Environment
# Should include ApiBaseUrl=http://localhost:5000

# Check Web logs
journalctl -u radio-web -n 20
```

## Native Dependencies Summary

| Category | Package / Binary | apt Package | Purpose |
|---|---|---|---|
| Audio engine | libminiudio (bundled) | `libasound2-dev` | SoundFlow audio I/O via ALSA |
| MP3 encoding | libmp3lame | `libmp3lame-dev` | HTTP streaming to Google Cast |
| Audio server | PipeWire | `pipewire pipewire-pulse wireplumber` | Audio routing, BT audio |
| BT codecs | SPA bluez5 | `libspa-0.2-bluetooth` | SBC, LDAC, aptX, opus BT codecs |
| Bluetooth | BlueZ | `bluez` | BT stack, A2DP, AVRCP |
| Fingerprinting | fpcalc | `libchromaprint-tools` | Audio fingerprint generation |
| SDR radio | librtlsdr | `librtlsdr-dev` | RTL-SDR USB dongle driver |
| Cast discovery | Avahi | `avahi-daemon avahi-utils` | mDNS for Google Cast |
| D-Bus | libdbus | (system default) | BlueZ IPC via Tmds.DBus |
| SQLite | sqlite3 | (bundled in .NET) | Config, metrics, fingerprint DBs |
| .NET runtime | aspnetcore 8.0 | Self-contained or `aspnetcore-runtime-8.0` | Application runtime |
