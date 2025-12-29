# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Grandpa Anderson's Console Radio Remade** - A modern audio command center restoring vintage console radio functionality with modern capabilities (Spotify, streaming, smart home events, Chromecast audio).

**Target Platform:** Raspberry Pi 5 (Linux) with Windows development support
**Stack:** .NET 8+, ASP.NET Core, Blazor Server, SoundFlow audio engine, SQLite/JSON config

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

# Run audio UAT tool
dotnet run --project tools/Radio.Tools.AudioUAT
```

## Solution Structure

```
RadioConsole.sln
├── src/Radio.Core           # Domain interfaces, models, events (no dependencies)
├── src/Radio.Infrastructure # Audio engine, config stores, external integrations
├── src/Radio.API            # REST controllers, SignalR hubs, middleware
├── src/Radio.Web            # Blazor Server UI (Phase 9 - pending)
├── tests/                   # xUnit test projects
├── tools/                   # AudioUAT, ConfigurationManager CLIs
├── design/                  # Architecture docs (AUDIO.md, CONFIGURATION.md, etc.)
└── RaddyRF320BT/            # Git submodule for vintage radio protocol
```

## Architecture

**Layered Architecture:**
- **Core** - Pure domain (24 interfaces: IAudioEngine, IAudioSource, IConfigurationStore, etc.)
- **Infrastructure** - SoundFlow wrapper, device management, outputs (Local/Chromecast/HTTP), sources (Radio/Spotify/File/TTS)
- **API** - REST endpoints under `/api/*`, SignalR hubs at `/hubs/visualization` and `/hubs/audio`

**Key Patterns:**
- Constructor-based dependency injection
- Dual config stores (SQLite/JSON) switchable via appsettings.json
- Encrypted secrets with tag substitution: `${secret:identifier}`
- Audio ducking with priority system (1-10 scale)

**Audio Pipeline:**
```
Sources (Radio/Spotify/File/TTS) → Master Mixer → Outputs (Local/Chromecast/HTTP Stream)
                                        ↓
                              Visualization (FFT/Levels/Waveform)
```

## API Endpoints

- `/api/audio` - Volume, mute, playback state
- `/api/sources` - Switch audio sources
- `/api/devices` - Enumerate input/output devices
- `/api/radio` - Tuner controls
- `/api/spotify` - Spotify integration
- `/stream/audio` - Raw PCM audio stream (16-bit, stereo, 48kHz)

## Cross-Platform Requirements

Code must run on Raspberry Pi (Linux). Avoid:
- Windows-only APIs (WPF, WinForms)
- Platform-specific paths without abstraction
- Libraries without Linux/ARM64 support

Use: System.Device.Gpio, SoundFlow (MiniAudio), cross-platform .NET APIs

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
