# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
├── tests/                      # 7 xUnit test projects (~1,416 tests)
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

Dual-service architecture on Raspberry Pi:
- `radio-api.service` - Radio.API on port 5000 (audio engine, BT, all hardware)
- `radio-web.service` - Radio.Web on port 5002 (Blazor UI, depends on API)
- Shared: `/opt/radio-console/{api,web,data,logs}`

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
