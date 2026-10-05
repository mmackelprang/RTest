# Radio Console

**Grandpa Anderson's Console Radio, remade.** A vintage console radio cabinet restored as a modern audio
command center. The original functions (radio and vinyl) still work. Bluetooth audio, a file player, Google
Cast output, smart-home announcements, song recognition and a rotary-phone integration have been added. The
console is driven from a 1920x720 touch panel and four rotary knobs in the cabinet's front panel.

## Features

**Audio sources**
- **Radio.** An RTL-SDR software-defined radio (`RTLSDRCore`) covering FM and NOAA weather band. It supports
  tuning, seek and scan, presets, RDS station names and text, and a swept band map that scan shares. AM and
  shortwave wait on tuner hardware (see [known issues](docs/known-issues-and-future-work.md)).
- **Bluetooth A2DP.** The console is a Bluetooth speaker for a phone. AVRCP supplies track metadata and
  transport controls, and on Linux keeps the volume in sync (the Windows volume sync is a stub). BlueZ over
  D-Bus runs on Linux, WinRT on Windows.
- **File player.** Plays local or NAS music folders. It has a persistent queue, shuffle and repeat, saved
  playlists, and adds whole folders.
- **Vinyl and generic USB capture.** A USB phono preamp or any USB audio input.

**Events and announcements**
- Text-to-speech (Google Cloud or Azure) and audio-file events, from a REST announcement API for smart-home
  triggers.
- Priority-based ducking (1–10): background audio fades under an announcement and comes back afterwards.

**Outputs**
- Local speakers through SoundFlow (MiniAudio, PipeWire on Linux).
- Google Cast: an HTTP MP3 stream, or a direct Cast-channel receiver
  ([`docs/receiver-direct-channel.html`](docs/receiver-direct-channel.html)). A dropped speaker reconnects
  automatically.
- A raw PCM and MP3 HTTP stream at `/stream/audio`.

**Recognition and history**
- Song recognition with SongRec (the Shazam algorithm), plus album art from MusicBrainz and the Cover Art
  Archive.
- Play history with search.

**Console UI** (Blazor Server, Radzen)
- Home, Radio, Bluetooth, Devices, Phone, History, Diagnostics and System pages, laid out for a 1920x720 panel
  running a kiosk browser.
- Real-time visualizers (spectrum, levels, waveform and the radio band map) over SignalR.
- Sleep mode shows a clock with current conditions and a forecast (US National Weather Service), and can power the panel down (off by default; set `Sleep:PanelOffAfterMinutes`).

**Cabinet hardware**
- One Raspberry Pi Pico USB HID device with four knobs for Volume, Source, Presets and Tuning. They are detected on plug-in and drive an
  on-screen HUD.
- Rotary-phone integration with the companion RotaryPhone service: an incoming-call banner with the caller's
  name, call announcements, and Google Voice voicemail and texts.

**Operations**
- Configuration in JSON or SQLite stores, with encrypted secrets (`${secret:id}`) and backup and restore.
- Metrics with SQLite rollups, and a diagnostics dashboard.
- Log levels change at runtime without a restart (`/api/system/logging/levels`).
- OpenAPI with a Scalar UI at `/scalar/v1`.

## Hardware

The deployed appliance is an **Intel N100 mini-PC (x86_64) running Ubuntu with GNOME on Wayland**. It is built
into the cabinet with a 1920x720 touch panel, an RTL-SDR dongle, a USB Bluetooth adapter, a USB phono input and
the four encoders. A Raspberry Pi 5 (`linux-arm64`) is a build target that has **not been tested on hardware**; see
[known issues](docs/known-issues-and-future-work.md). Development works
on Windows or Linux.

## Architecture

The solution is layered. `Radio.Core` holds the domain interfaces and models. `Radio.Infrastructure` wraps the
SoundFlow audio engine and supplies the sources, outputs, Bluetooth, Cast and platform integrations. `Radio.API`
is the REST and SignalR service that owns all audio hardware. `Radio.Web` is the Blazor Server UI, which talks to
the API over HTTP and SignalR. Five reusable libraries are packable as NuGet packages: `RTLSDRCore`,
`Radio.AudioAnalysis`, `Radio.Metrics`, `Radio.Configuration` and `Radio.Fingerprinting`. Every source feeds one
master mixer, and its output is tapped for local playback, the Cast stream and the visualizers. See
[docs/architecture.md](docs/architecture.md).

## Quick start

Prerequisites: the .NET 10 SDK (pinned in `global.json`). For Cast MP3 streaming on Linux, also install
`libmp3lame` (`sudo apt install libmp3lame-dev`).

```bash
# Build (Release; warnings are counted against a baseline, see CONTRIBUTING.md)
dotnet build RadioConsole.sln -c Release

# Test: redirect to a file and read the exit code; do not pipe into tail
dotnet test RadioConsole.sln -c Release > test.log 2>&1; echo "exit=$?"

# Run the API (http://localhost:5000, API docs at /scalar/v1)
dotnet run --project src/Radio.API

# Run the web UI (http://localhost:5002)
dotnet run --project src/Radio.Web
```

## Deployment

Two systemd services (`radio-api` on port 5000, `radio-web` on port 5002) are deployed from a dev host with
PowerShell 7:

```powershell
./deploy/Deploy-ToLinux.ps1              # the x64 appliance (defaults: -TargetHost radio -Runtime linux-x64)
./deploy/Deploy-ToPi.ps1                 # Raspberry Pi (linux-arm64)
./deploy/Deploy-ToLinux.ps1 -VerifyOnly  # check that the deployed build matches, without deploying
```

See [docs/deployment.md](docs/deployment.md).

## Documentation

| Doc | What it covers |
|---|---|
| [docs/README.md](docs/README.md) | Index of all current docs |
| [docs/architecture.md](docs/architecture.md) | Layers, the audio pipeline and data flow |
| [docs/configuration.md](docs/configuration.md) | Configuration, preferences and secrets reference |
| [docs/api.md](docs/api.md) | REST and SignalR reference (Scalar at `/scalar/v1` is the live source) |
| [docs/deployment.md](docs/deployment.md) | The appliance, services, kiosk, deploy and verification |
| [docs/integrations.md](docs/integrations.md) | Rotary encoders, the phone and the announcement API |
| [docs/testing.md](docs/testing.md) | Test projects and conventions |
| [docs/known-issues-and-future-work.md](docs/known-issues-and-future-work.md) | Open items and stubbed features |
| [docs/decisions/](docs/decisions/) | ADRs and the decision log |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to build, test and submit changes |
| [CHANGELOG.md](CHANGELOG.md) | Release notes |
| [archive/](archive/) | Historical plans, handoffs and UAT records (not maintained) |

## License

[Apache License 2.0](LICENSE).
