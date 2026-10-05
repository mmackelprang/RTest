# Radio Console Deployment Guide

## Overview

**Grandpa Anderson's Console Radio Remade** — a modern audio command center that restores
vintage console radio functionality: FM/AM radio over an RTL-SDR dongle, vinyl (phono) and USB
line-in capture, a local/NAS file player, Bluetooth A2DP audio reception, text-to-speech
announcements, Google Cast output, song identification (SongRec), and a Blazor Server UI.

**Target platform:** Intel N100 mini-PC (`x86_64`), Ubuntu with GNOME 46 on Wayland, deployed
with `deploy/Deploy-ToLinux.ps1` (defaults `-TargetHost radio -Runtime linux-x64`). A Raspberry
Pi 5 (`linux-arm64`, via `Deploy-ToPi.ps1`) is a build target that has not been tested on
hardware; see `docs/known-issues-and-future-work.md`. The Pi-specific sections below are kept
for reference and are unverified.
**Stack:** .NET 10, ASP.NET Core, Blazor Server, SoundFlow (MiniAudio), PipeWire, BlueZ 5

For the condensed, current overview of the appliance see [`docs/deployment.md`](../docs/deployment.md).

## Architecture

The application runs as two separate systemd services:

```
/opt/radio-console/
  api/              ← Radio.API binaries (audio engine, REST, SignalR)
  web/              ← Radio.Web binaries (Blazor Server UI)
  data/             ← Shared data (config, metrics, fingerprints, secrets, albumart, backups)
  logs/             ← Shared logs
  tools/            ← optional helper binaries
```

| Service | Port | Purpose |
|---|---|---|
| `radio-api.service` | 5000 | REST API, SignalR hubs, audio engine, Bluetooth, Cast |
| `radio-web.service` | 5002 | Blazor Server UI, depends on radio-api |

`radio-web` has `Requires=radio-api.service` — stopping the API automatically stops the Web UI.
Both services use `WorkingDirectory=/opt/radio-console` so relative paths (`./data`, `logs/`) resolve to the shared directories.

### CPU affinity layout (added 2026-05-22, Plan D)

Both production deployment targets (Ubuntu N100 `radio` and Raspberry Pi 5 `piradio`) have
4 cores. The systemd units split them to keep audio paths isolated from the rest of the
host workload:

| Cores | Tenants |
|---|---|
| `0,1` | OS + `systemd-journald` + `sshd` + `radio-web` |
| `2,3` | `radio-api` (BT capture, Cast encode, all audio paths) |

`radio-api` also gets `Nice=-5`, `IOSchedulingClass=2 IOSchedulingPriority=2` (best-effort
near-realtime), `LimitNICE=-5:0`, `LimitRTPRIO=99`, and `LimitMEMLOCK=infinity`. The RT
limit allows the optional `BluetoothOptions.UseRealtimeCaptureThread` flag (default off)
to bump the PipeWire capture thread to `SCHED_FIFO` priority 50 via `pthread_setschedparam`.

Verify after deploy:

```bash
ssh mmack@radio "taskset -p \$(pgrep radio-api)"   # mask 0xC (cores 2,3)
ssh mmack@radio "taskset -p \$(pgrep radio-web)"   # mask 0x3 (cores 0,1)
ssh mmack@radio "ps -o pid,nice,comm \$(pgrep radio-api)"   # NI=-5
ssh mmack@radio "sudo ionice -p \$(pgrep radio-api)"        # best-effort: prio 2
ssh mmack@radio "cat /proc/\$(pgrep radio-api)/limits | grep RTPRIO"   # Max=99
```

If RotaryPhone is ever co-located on the same host (it is not currently — RotaryPhone runs
as a separate Ubuntu service and is consumed by Radio.Web via REST), document its affinity
layout in `D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md` to avoid
double-pinning the audio cores.

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
                    │             Song identification (SongRec → MusicBrainz)
                    │             Volume/Balance control
                    ▼
              ALSA output         (headphones / USB DAC)
              HTTP stream         (for Google Cast)
```

Other audio sources (Radio/SDR, vinyl, USB line-in, File Player, TTS) feed directly into the
SoundFlow mixer without the null sink intermediary.

## Prerequisites

### Hardware

| Component | Required? | Purpose |
|---|---|---|
| Intel N100 mini-PC (`x86_64`) | Yes | Application host (a Raspberry Pi 5 is an untested ARM64 build target) |
| Audio output (3.5mm, USB DAC, or HDMI) | Yes | Audio playback |
| Network connection (Ethernet or WiFi) | Yes | Google Cast discovery, API access, song identification |
| USB RTL-SDR dongle | Optional | FM/AM radio reception via SDR |
| USB Bluetooth adapter | Recommended | Avoids WiFi/BT coexistence interference on combo chips (see Audio Quality Tuning) |
| 1920x720 touch panel | Optional | Designed display, driven by a kiosk Chrome; any browser works too |

### Software

- Ubuntu with GNOME on Wayland (the deployed appliance); other Debian-based distros should work
- Root/sudo access for initial setup
- Internet access during setup (for package installation)

## Quick Start

### 1. Run Setup on the Box

```bash
# Clone the repository (or copy the deploy directory)
git clone <this repository> ~/RTest
cd ~/RTest

# Run the setup script (x64 appliance)
sudo deploy/debian-x64/setup.sh
# Kiosk: deploy/debian-x64/kiosk/setup-kiosk.sh
```

A bare box is rebuilt from [`deploy/provision/README.md`](provision/README.md).
`deploy/raspberry-pi/setup.sh` is the untested Pi equivalent.

The setup script installs all system dependencies, creates the application user,
configures PipeWire/WirePlumber for Bluetooth A2DP sink, and installs both systemd services.

### 2. Build and Deploy from the dev machine (Windows or Linux)

Both deploy scripts run under PowerShell 7 (`pwsh`) on either host. Until 2026-09-28 they were
Windows-only in practice, because every repo path in them was spelled with a backslash — a
separator on Windows and an ordinary filename character on Linux. They now build paths with
`Join-Path`, so the same invocation works from a Linux dev box with `pwsh`, the .NET 10 SDK,
`ssh`/`scp` and (optionally) `rsync` installed.

```powershell
# One-command build and deploy from your dev machine (defaults: -TargetHost radio -Runtime linux-x64)
./deploy/Deploy-ToLinux.ps1

# Box-side SHA check only: no build, stop or sync
./deploy/Deploy-ToLinux.ps1 -VerifyOnly

# Untested Raspberry Pi target (linux-arm64)
./deploy/Deploy-ToPi.ps1
```

Or build manually:

```bash
# From the repository root (on dev machine)
cd deploy/common
./publish.sh x64          # arm64 for the untested Pi target

# Copy to the box
# The --exclude is not optional. Without it, --delete removes api/appsettings.Production.json
# (nothing in the publish output replaces it) and overwrites web/appsettings.Production.json
# with the tracked stub from src/Radio.Web/. Both deploy scripts pass it; so must you.
rsync -avz --delete --exclude='appsettings.Production.json' publish/linux-x64/api/ mmack@radio:/opt/radio-console/api/
rsync -avz --delete --exclude='appsettings.Production.json' publish/linux-x64/web/ mmack@radio:/opt/radio-console/web/
ssh mmack@radio "sudo chmod +x /opt/radio-console/api/Radio.API /opt/radio-console/web/Radio.Web"
```

⚠ **This manual route has no seed step.** The scripts pair the `--exclude` with a block that
creates the overlay when a directory has none; the commands above only *preserve* one that
already exists. On a freshly provisioned box, copy
`deploy/debian-x64/appsettings.Production.json` into `api/` and `web/` yourself.

#### How files reach the target, and what happens when that fails

`Deploy-ToLinux.ps1` chooses its transport in **step 1.5, before anything is stopped, and prints
the choice with its reason** (since `OPS-12`/`OPS-13`, 2026-09-28). `-Transport rsync|scp` makes it
explicit; the default `auto` is **scp on a Windows host** (the only transport measured working
there — `OPS-12`) and **rsync on any other host that has it on PATH**. Whichever was chosen is then
*proven* by a pre-flight — an `rsync -n` dry run of the real API sync, or a one-byte `scp` probe —
together with `ssh` reachability and the box-side prerequisites (`rsync`, `curl`, passwordless
`sudo`). A pre-flight failure exits 1 with **nothing stopped**, and names the other transport to try.
⚠ *Previously* `Get-Command rsync` decided silently inside step 3, after the services were already
down, which is how a transport error became an outage on 2026-09-09. Both routes still stage into
`/tmp` and are then moved into place.

**Verification reads the box, not the dev host's DNS.** Step 4's SHA check runs
`curl http://localhost:<port>/api/health/version` *over ssh* for both services, so it has the same
answer on every dev host — including one where `radio` only resolves through `~/.ssh/config`. On
2026-09-28 the old `Invoke-RestMethod` form failed a name lookup ten times after a deploy that had
succeeded, exited 1, and left the kiosk stopped (`OPS-13`). Three verdicts now: **verified** →
relaunch the kiosk, exit 0; **unreachable** → relaunch the kiosk *and* exit 1 (the instrument failed,
not necessarily the deploy; read the SHA by hand); **mismatch** → exit 1 with the kiosk deliberately
left down. A service that fails to come up also relaunches the kiosk before exiting 1 — a panel
showing "cannot connect" can be read from across the room, a dark one cannot.

**`-VerifyOnly`** runs that instrument on its own: no build, no stop, no sync. It prints both
services' running commit against the local HEAD and the kiosk's liveness, and exits 0 only on a
match. Use it before a human runs UAT (CLAUDE.md: *merged is not deployed*).

⚠ **The fallback is not exotic. On a Windows dev box without `rsync` installed it is the only path
every deploy takes** — measured on this repo's dev machine 2026-09-05, where `rsync` is absent
entirely. Check the line above before assuming a deploy went over rsync.

⚠ **On a Linux dev host the opposite holds: `rsync` is normally installed, so the rsync branch is
the one that runs.** `OPS-12` records that this branch had never executed before 2026-09-09 and
that its first attempt, from Windows, failed for three reasons that are all artifacts of the MSYS2
toolchain (`D:` read as a hostname, MSYS rsync unable to drive Windows OpenSSH, a separate
`known_hosts`). None of those exist on Linux, where rsync and ssh are native and share `~/.ssh`,
and the transport was proven from a Linux host on 2026-09-28 the way that row prescribes — an
`rsync -n` dry run to a scratch path on `radio`, exit 0, with the services untouched. ⚠ The row's
*other* finding is host-independent and still open: the script stops both services in step 2
before it has proven in step 3 that it can reach the target at all, so any transport failure is an
outage on every host. Do the dry run before the first deploy from a new machine.

Since `OPS-9`, **a failed transfer stops the deploy** on both routes: the script captures each
`ssh`/`scp`/`rsync` exit code at its call site and exits with `API sync failed!` or
`Web sync failed!`. Previously the fallback tested the exit code of the tidy-up command that ran
*after* the transfer — which returns 0 regardless — so a transfer that moved nothing was reported
as a successful deploy. If a deploy now stops where it used to pass, that is the guard working;
check connectivity and disk space on the target before re-running.

### 3. Start the Services

```bash
sudo systemctl start radio-api radio-web
sudo systemctl status radio-api radio-web
```

### 4. Access

- **API:** `http://radio:5000` (Scalar API reference at `/scalar/v1`, OpenAPI JSON at `/openapi/v1.json`)
- **Web UI:** `http://radio:5002`

### 5. Verifying the Deployed Commit

Both deploy scripts (`Deploy-ToLinux.ps1` and `deploy-to-pi.sh`) bake the local
git HEAD into the API assembly via `-p:SourceRevisionId=<sha>`, then after the
services come up they `curl http://<host>:5000/api/health/version` and fail the
deploy if the returned `gitSha` doesn't match what was just built.

You can also check manually at any time:

```bash
curl -s http://radio:5000/api/health/version | jq
# {
#   "gitSha": "abc123...",
#   "gitShaShort": "abc123f",
#   "informationalVersion": "1.0.0+abc123...",
#   "assemblyVersion": "1.0.0.0",
#   "buildTimestampUtc": "2026-05-20T18:42:11Z",
#   "assemblyName": "Radio.API"
# }

# Compare against local HEAD
git rev-parse HEAD
```

If `gitSha` is `"unknown"`, the binary was built outside a git checkout (e.g.
from a tarball) — rebuild from a clone to get a real SHA.

## Cross-Compilation Note

On Windows, Radio.Infrastructure multi-targets (`net10.0` and `net10.0-windows10.0.19041.0`),
and Radio.API/Radio.Web target the Windows TFM. When cross-compiling from Windows for Linux,
you **must** pass `-f net10.0` to override the conditional Windows TFM:

```bash
dotnet publish ... --runtime linux-x64 -f net10.0
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
| `songrec` | Shazam-compatible song recognition (`ppa:marin-m/songrec`) |

SongRec is the only recognizer, for every source. The app shells out to `songrec`, then looks up
metadata and cover art on MusicBrainz / Cover Art Archive. No API key is needed. Install:

```bash
sudo add-apt-repository ppa:marin-m/songrec
sudo apt install songrec
```

The binary is found on `PATH` unless `Fingerprinting:SongRec:SongRecPath` in `appsettings.json`
names it:

```json
{
  "Fingerprinting": {
    "SongRec": {
      "SongRecPath": ""
    }
  }
}
```

The earlier fpcalc/AcoustID pipeline was removed. `deploy/debian-x64/setup.sh` still installs
`fpcalc`; nothing uses it.

### Network Discovery

| Package | Purpose |
|---|---|
| `avahi-daemon` | mDNS/DNS-SD for Google Cast device discovery |
| `avahi-utils` | `avahi-browse` CLI for diagnostics |

### .NET Runtime

| Package | Purpose |
|---|---|
| `aspnetcore-runtime-10.0` | ASP.NET Core runtime (if not using self-contained publish) |

Self-contained publishes bundle the runtime, so this is only needed for
framework-dependent deployments (`Deploy-ToLinux.ps1 -Quick`).

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

## Audio Quality Tuning (Bluetooth)

When receiving Bluetooth A2DP audio, several system-level settings significantly
affect audio quality. These are especially important on systems with combo
WiFi+Bluetooth chips (e.g., Intel AX201).

### USB Bluetooth Adapter (Recommended)

Combo WiFi+BT chips share a single antenna, causing WiFi/BT coexistence
interference. This manifests as silence gaps in the A2DP audio stream — the phone
sends audio correctly, but BT packets are dropped at the radio layer. A dedicated
USB Bluetooth adapter physically separates BT from WiFi, eliminating this issue.

**Tested adapter:** TP-Link UB500 (Realtek BT 5.1, USB, aptX HD capable)

When using a USB BT adapter alongside an onboard combo chip, disable the onboard
BT to prevent conflicts:

```bash
# Install udev rule to disable Intel AX201 BT on boot
sudo cp deploy/common/99-disable-intel-bt.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules

# Verify after reboot: hci0 should not appear, only hci1 (USB adapter)
hciconfig -a
```

**Note:** The udev rule matches Intel AX201 by USB vendor:product ID (`8087:0033`).
For other combo chips, find the USB ID with `lsusb | grep -i bluetooth` and update
the rule accordingly.

### CPU Governor

Set CPU governor to `performance` to prevent frequency scaling from adding latency
to the audio pipeline:

```bash
sudo cp deploy/common/radio-performance.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now radio-performance.service
```

### WiFi Power Management

Disable WiFi power management to prevent bursty radio activity that interferes
with BT reception:

```bash
sudo cp deploy/common/99-wifi-power-save-off /etc/NetworkManager/dispatcher.d/
sudo chmod +x /etc/NetworkManager/dispatcher.d/99-wifi-power-save-off

# Apply immediately
sudo iwconfig wlp0s20f3 power off
```

### PipeWire Quantum Tuning

MiniAudio requests a 5ms buffer (240 samples at 48kHz). PipeWire rounds this down
to 128 samples (2.67ms), which can cause ALSA output xruns. Set a minimum quantum
of 512 (10.67ms) to match the BT transport quantum:

```bash
mkdir -p ~/.config/pipewire/pipewire.conf.d
cat > ~/.config/pipewire/pipewire.conf.d/99-radio-quantum.conf << 'EOF'
context.properties = {
    default.clock.min-quantum = 512
}
EOF
systemctl --user restart pipewire
```

### PipeWire Version

Ubuntu 24.04 ships PipeWire 1.0.5. For better BT audio stability, upgrade to the
latest available version via the upstream PPA:

```bash
sudo add-apt-repository -y ppa:pipewire-debian/pipewire-upstream
sudo apt-get update
sudo apt-get upgrade -y pipewire pipewire-pulse wireplumber libspa-0.2-bluetooth
systemctl --user restart pipewire wireplumber
```

### Summary Checklist

| Setting | How | Persists? |
|---|---|---|
| USB BT adapter | Plug in USB adapter, install udev rule to disable onboard BT | Yes (udev rule) |
| CPU governor=performance | `radio-performance.service` | Yes (systemd) |
| WiFi PM off | NetworkManager dispatcher script | Yes (dispatcher) |
| PipeWire min-quantum=512 | `~/.config/pipewire/pipewire.conf.d/99-radio-quantum.conf` | Yes (user config) |
| PipeWire upgrade | PPA `ppa:pipewire-debian/pipewire-upstream` | Yes (apt) |

## Configuration

### appsettings.Production.json

Both services read `appsettings.Production.json` from their respective binary directories.
The API settings are the primary configuration; the Web settings mainly configure the
API connection URL.

**Seeding is per service directory, and an existing overlay is never overwritten.** When
`Deploy-ToLinux.ps1` or `deploy-to-pi.sh` finds no `appsettings.Production.json` in `api/` or
in `web/`, it seeds that directory from `deploy/<target>/appsettings.Production.json`; a
directory that already has one is left byte-for-byte alone, and the deploy prints
`present — left alone` for it. The two directories are decided independently, so provisioning
a box by hand-placing only one of the two overlays is safe.

Both scripts also pass `--exclude='appsettings.Production.json'` to the `rsync --delete` that
moves the binaries into place. The exclude and the seed are a **matched pair**: the exclude
preserves an overlay that exists, the seed creates one that does not. A script carrying only
the exclude would leave a fresh box with no overlay at all.

> `deploy-to-pi.sh` gained both halves in `OPS-8`. Before that it had **neither**, and
> destroyed the operator's config on *every* run: `api/`'s overlay was deleted outright
> (nothing in the publish output replaces it) and `web/`'s was silently overwritten by the
> tracked stub in `src/Radio.Web/`. The deletion was the louder half; the replacement was the
> one that survived a casual look, because the file was still present and still well-formed.

If the presence check itself cannot be completed, the deploy **aborts instead of seeding** —
guessing would risk overwriting the very file this guard protects. `ssh` reports its own
transport errors as exit 255, which a bare `test -f` cannot tell apart from "file absent".

> The two scripts are **not** equally strict here, and `deploy-to-pi.sh` is the stricter one.
> `Deploy-ToLinux.ps1` infers absence from a destination's *name not appearing* in the probe's
> output, so a remote that answers with contaminated or empty stdout — an unterminated shell
> banner, CRLF line endings, a remote-side error masked by the probe's own `exit 0` — reads as
> "absent" and seeds over a present overlay. `deploy-to-pi.sh` requires an explicit
> `PRESENT:<dir>` or `ABSENT:<dir>` verdict per destination and aborts when it gets neither.
> Not theoretical: the bash script's own omission-form probe was measured destroying a live
> overlay before `OPS-8` replaced it. Porting the verdict form back to the PowerShell is filed
> as `docs/known-issues-and-future-work.md` §28, not yet queued.

> ⚠ **One seed file serves both directories.** `deploy/debian-x64/appsettings.Production.json`
> holds API-shaped keys (`AudioOutput`, `Devices`, `FilePlayer`, `Diagnostics`,
> `Fingerprinting`), and no `ApiBaseUrl` or `Kestrel`. A `web/` directory seeded from it is
> inert today — but note *why*, because the obvious reason is wrong: `Radio.Web` **does** bind
> `Devices` (`Program.cs:565` → `DevicesOptions.SectionName`). It is inert because
> `DevicesOptions` reads only `Devices:Aliases`, which the seed does not supply, and per-key
> merge leaves the base `Aliases` map intact. `AudioOutput` is the section `Radio.Web` genuinely
> does not bind. This is what you will find if you open a freshly seeded `web/` overlay to add
> `RotaryPhone:Gv:AuthKey`. It also means the seeded file now counts as "present" forever, so
> that path will not be seeded again. Adding a web-bound key to the shared seed would silently
> start landing it in `api/` too — splitting the seed per service is the fix if that day comes.

> ⚠ **The Pi's seed is a different file with a much smaller surface.**
> `deploy/raspberry-pi/appsettings.Production.json` holds a **single** top-level key,
> `AudioOutput` — Pi-specific device-display patterns plus `Local.PreferredDeviceId:
> "playback-12"` and `GoogleCast.StreamingMode: "DirectChannel"`. It has no `Devices`,
> `Kestrel`, `Logging`, `Fingerprinting`, and **no `RotaryPhone` section**, so nothing seeded
> from it ever writes an empty `AuthKey`.
>
> ⚠ **Since `OPS-8`, a fresh Pi deployed by `deploy-to-pi.sh` gets an `api/` overlay where it
> previously had none** — and that overlay pins `PreferredDeviceId` to `playback-12` and the
> Cast mode to `DirectChannel`, where the built-in defaults are `""` (auto-select) and
> `HttpMp3`. If `playback-12` is not the intended output on that particular Pi, audio will
> route to the wrong device until the overlay is edited. This is convergent rather than novel:
> `Deploy-ToPi.ps1` → `Deploy-ToLinux.ps1 -Runtime linux-arm64` already resolves `$configDir`
> to `raspberry-pi` and seeds the same file. The bash script is catching up to its twin.

> Before `OPS-7` it was not. A single `test -f` on `api/` gated the copy into **both**
> directories, so a box with a `web/` overlay and no `api/` one had its web file
> overwritten by the seed — which deleted `RotaryPhone:Gv:AuthKey`, since the tracked seed
> carries no `RotaryPhone` section, and brought the service up with inter-service auth
> silently off. The same guard also meant a box with only an `api/` overlay never received
> a `web/` one at all.

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
    "SongRec": { "SongRecPath": "" }
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
| `TTS:GoogleApiKey` | Google Cloud TTS | https://console.cloud.google.com |

Secrets are encrypted with a machine-specific key. If migrating from another machine,
re-enter secrets on the new box — they won't decrypt across machines.

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
# Check SongRec is installed and on PATH
songrec --version

# Test recognition against a file
songrec audio-file-to-recognized-song /path/to/audio.mp3
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
| Song identification | songrec | `songrec` (`ppa:marin-m/songrec`) | Shazam-compatible recognition |
| SDR radio | librtlsdr | `librtlsdr-dev` | RTL-SDR USB dongle driver |
| Cast discovery | Avahi | `avahi-daemon avahi-utils` | mDNS for Google Cast |
| D-Bus | libdbus | (system default) | BlueZ IPC via Tmds.DBus |
| SQLite | sqlite3 | (bundled in .NET) | Config, metrics, fingerprint DBs |
| .NET runtime | aspnetcore 10.0 | Self-contained or `aspnetcore-runtime-10.0` | Application runtime |
