# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Cross-Service Boundary (IMPORTANT)

This service shares the Ubuntu box (`radio`) with RotaryPhone. **Read before any BT/audio work:**

**`D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`** — Defines which BT adapter, profiles, and WirePlumber configs each service owns. Violating these boundaries will break the other service's audio.

Key rules:
- Radio Console owns **TP-Link UB500** (`hci0`, `78:20:51:F5:FB:A7`) for music/A2DP
- RotaryPhone owns **Intel AX201** (`hci1`, `10:91:D1:FE:00:46`) for voice/HFP
- Radio Console manages all `/etc/wireplumber/bluetooth.lua.d/` configs
- Always `bluetoothctl select 78:20:51:F5:FB:A7` before any bluetoothctl commands
- If you need to change any boundary, update the boundary doc first

To request changes from the RotaryPhone session, update the boundary doc's Change Log and optionally create a prompt file at `D:\prj\RotaryPhone\docs\prompts/`. See the boundary doc's "Passing Work Between Sessions" section for the full protocol.

## Project Overview

**Grandpa Anderson's Console Radio Remade** - A modern audio command center restoring vintage console radio functionality with modern capabilities (Bluetooth A2DP, streaming, smart home events, Chromecast audio).

**Target Platform:** Raspberry Pi 5 (Linux) with Windows development support
**Stack:** .NET 10, ASP.NET Core, Blazor Server, SoundFlow audio engine, SQLite/JSON config

## Build & Test Commands

```bash
# Build
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release --verbosity normal

# Run single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Run API server (Swagger at http://localhost:5000/swagger)
dotnet run --project src/Radio.API

# Run Web UI (http://localhost:5002)
dotnet run --project src/Radio.Web

# Run audio UAT tool
dotnet run --project tools/Radio.Tools.AudioUAT

# Deploy to Pi (from Windows)
./deploy/Deploy-ToPi.ps1 -PiHost piradio -PiUser radio
```

## Solution Structure

```
RadioConsole.sln
├── src/Radio.Core              # Domain interfaces, models, events (no dependencies)
├── src/Radio.Infrastructure    # Audio engine, BT, Cast, sources, outputs, DI wiring
│   ├── Audio/SoundFlow/        # Engine, mixer, device manager, tapped output stream
│   ├── Audio/Sources/          # Primary (Radio, BT, File, Vinyl, USB) + Event (TTS, AudioFile)
│   ├── Audio/Outputs/          # Local, GoogleCast, HttpStream
│   ├── Audio/Fingerprinting/   # SoundFlowAudioTap, FingerprintDbContext (12-table SQLite)
│   ├── Platform/Bluetooth/     # Linux (BlueZ D-Bus) + Windows (WinRT)
│   └── Configuration/          # DeviceOptionsResolver, PreferencesPersistence (Radio-specific)
├── src/Radio.Configuration     # Standalone NuGet: JSON/SQLite stores, secrets, backup, bridge
├── src/Radio.Fingerprinting    # Standalone NuGet: SongRec, MusicBrainz, background ID, repos
├── src/Radio.Metrics           # Standalone NuGet: time-series metrics collection + SQLite storage
├── src/Radio.AudioAnalysis     # Standalone NuGet: waveform comparison, THD, silence detection
├── src/RTLSDRCore              # Standalone NuGet: RTL-SDR software-defined radio library
├── src/Radio.API               # REST controllers, SignalR hubs, middleware
├── src/Radio.Web               # Blazor Server UI (MudBlazor Material 3)
├── tests/                      # 10 xUnit test projects (~1,416 tests)
│   ├── Radio.Metrics.Tests         # Metrics package tests
│   ├── Radio.Configuration.Tests   # Configuration package tests
│   ├── Radio.Fingerprinting.Tests  # Fingerprinting package tests
│   ├── RTLSDRCore.Tests            # RTL-SDR package tests
│   ├── Radio.AudioAnalysis.Tests   # Audio analysis package tests
│   ├── Radio.Core.Tests            # Core domain tests
│   ├── Radio.Infrastructure.Tests  # Infrastructure integration tests
│   ├── Radio.API.Tests             # API controller tests
│   ├── Radio.Web.Tests             # Web UI component tests
│   └── Radio.IntegrationTests      # Cross-cutting integration tests
├── tools/                      # AudioUAT, ConfigurationManager CLIs
├── deploy/                     # Pi deployment scripts, systemd services
├── design/                     # Architecture docs, decision log, work log
└── RaddyRF320BT/               # Git submodule for vintage radio protocol
```

## Architecture

**Layered Architecture:**
- **Core** - Pure domain (interfaces: IAudioEngine, IAudioSource, IConfigurationStore, IBluetoothService, etc.)
- **Extracted Libraries** - Standalone NuGet packages: Configuration, Fingerprinting, Metrics, AudioAnalysis, RTLSDRCore
- **Infrastructure** - SoundFlow wrapper, device management, outputs (Local/Cast/HTTP), sources (Radio/SDR/File/BT/TTS), Bluetooth (Linux BlueZ + Windows WinRT), DI wiring
- **API** - REST endpoints under `/api/*`, SignalR hubs at `/hubs/visualization` and `/hubs/audio`
- **Web** - Blazor Server UI, 12 pages, shared components, SignalR client

**Key Patterns:**
- Constructor-based dependency injection
- Dual config stores (SQLite/JSON) switchable via appsettings.json
- Encrypted secrets with tag substitution: `${secret:identifier}`
- Audio ducking with priority system (1-10 scale)
- Multi-target framework: `net10.0` (Linux) + `net10.0-windows10.0.19041.0` (WinRT BT)
- Extracted libraries packable as NuGet (`pack-local.ps1`)

**Audio Pipeline:**
```
Sources (Radio/SDR/File/BT/TTS) → Master Mixer → Modifiers (Balance, FingerprintTap, Viz)
                                                ↓
                                     Playback Device (local speakers)
                                     TappedOutputStream → HTTP Stream → Google Cast
                                     TappedOutputStream → Visualization (FFT/Levels/Waveform)
```

## API Endpoints

- `/api/audio` - Volume, mute, playback state, now playing
- `/api/sources` - Switch audio sources, TTS engines/voices
- `/api/devices` - Enumerate input/output devices, Cast discovery/connection
- `/api/radio` - Tuner controls, presets, band selection
- `/api/bluetooth` - BT pairing, discovery, AVRCP controls
- `/api/queue` - Queue management, reordering
- `/api/files` - File browsing, playback
- `/api/playhistory` - Play history with search
- `/api/configuration` - Config CRUD, import/export
- `/stream/audio` - Raw PCM audio stream (16-bit, stereo, 48kHz)
- `/stream/audio/mp3` - MP3 stream (for Google Cast)

## Deployment

**Use `mmack@radio` for SSH from WSL. Do NOT use the bare IP.**

```bash
ssh mmack@radio
```

`radio` resolves fine from WSL and is the working form (verified 2026-08-10). The bare IP
**fails** — `mmack@192.168.86.50` gives `Permission denied (publickey,password)`.

*Why*, so this stops getting rediscovered: `~/.ssh/config` has a `Host radio radio.local` block
that supplies `IdentityFile ~/.ssh/id_ed25519_radio` together with `IdentitiesOnly yes`.
Connecting by IP does not match that block, so the correct key is never offered and
`IdentitiesOnly` suppresses every other key — hence the instant rejection. The IP
(`192.168.86.50`) is still accurate as *reference* information (`curl`, browser, and
`ssh -i ~/.ssh/id_ed25519_radio mmack@192.168.86.50` all work), it just must not be the
default form for SSH.

> An earlier revision of this note claimed the opposite — that `radio`/`piradio` do not resolve
> from WSL and the IP must be used. That was wrong and cost several sessions time. Six
> independent checks confirm `mmack@radio` works.

The in-app service URLs (`http://radio:5004`, etc.) resolve *on* the box and should not be changed.

Dual-service architecture on Raspberry Pi:
- `radio-api.service` - Radio.API on port 5000 (audio engine, BT, all hardware)
- `radio-web.service` - Radio.Web on port 5002 (Blazor UI, depends on API)
- Shared: `/opt/radio-console/{api,web,data,logs}`

**Verifying a deploy actually landed.** `Deploy-ToLinux.ps1` verifies `radio-api` by SHA against
`/api/health/version`, but for `radio-web` it only checks `systemctl is-active` — so a **stale web
binary passes verification silently** (this is the gap OPS-1 closes). Interim check: grep the deployed
binary for a symbol that exists only on the branch under test.

```bash
grep -ac RetryOpenThreadAsync /opt/radio-console/web/Radio.Web   # non-zero → the new binary is live
```

## Cross-Platform Requirements

Code must run on Raspberry Pi (Linux). Avoid:
- Windows-only APIs (WPF, WinForms) outside `#if WINDOWS_TARGET` guards
- Platform-specific paths without abstraction
- Libraries without Linux/ARM64 support

Use: System.Device.Gpio, SoundFlow (MiniAudio), cross-platform .NET APIs
Exception: WinRT BT APIs and NAudio WASAPI are Windows-only behind conditional compilation.

## Code Style

- 2-space indentation (EditorConfig enforced)
- File-scoped namespaces
- Nullable reference types enabled
- Warnings as errors in Release builds
- Comment internal logic, edge cases, protocol details
- Explicit type annotations preferred

## Database Paths

Configured via `appsettings.json` Database section:
- Configuration: `./data/config/configuration.db`
- Metrics: `./data/metrics/metrics.db`
- Fingerprints: `./data/fingerprints/fingerprints.db`
- Backups: `./data/backups/`
- Logs: `./logs/`
- Album art cache: `./data/albumart/`
