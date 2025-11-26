# Radio Console

Grandpa Anderson's Console Radio Remade - A modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet.

## Overview

This project restores the original function (Radio/Vinyl) while adding modern capabilities (Spotify, Streaming, Smart Home Events, and Chromecast Audio).

## Current Status

| Phase | Status | Description |
|-------|--------|-------------|
| 0 - Project Setup | ✅ Completed | Solution structure, CI/CD pipeline |
| 1 - Configuration | ✅ Completed | JSON/SQLite stores, secrets management, backup/restore |
| 2 - Core Audio | ✅ Completed | SoundFlow integration, audio engine, device manager, master mixer |
| 3 - Audio Sources | ⬜ Not Started | Spotify, Radio, Vinyl, File Player |
| 4 - Event Sources | ⬜ Not Started | TTS, Audio File Events |
| 5 - Ducking | ⬜ Not Started | Priority-based audio ducking |
| 6 - Outputs | ⬜ Not Started | Local audio, Chromecast |
| 7 - Visualization | ⬜ Not Started | Spectrum, VU meters |
| 8 - API | ⬜ Not Started | REST endpoints, SignalR |
| 9 - UI | ⬜ Not Started | Blazor components |

## Technical Architecture

| Component | Technology |
|-----------|------------|
| Hardware | Raspberry Pi 5 (Raspberry Pi OS / Linux) |
| Framework | .NET 8+ (C#) |
| Audio Engine | [SoundFlow](https://github.com/lsxprime/SoundFlow) |
| UI | Blazor Server |
| API | ASP.NET Core Web API |
| Real-time | SignalR |
| Database | Repository Pattern (SQLite / JSON) |
| Logging | Serilog |
| Testing | xUnit |

## Project Structure

```
RadioConsole/
├── src/
│   ├── Radio.Core/          # Core interfaces, models, and domain logic
│   │   ├── Interfaces/Audio/  # IAudioEngine, IAudioDeviceManager, etc.
│   │   ├── Configuration/     # AudioEngineOptions
│   │   └── Exceptions/        # AudioDeviceConflictException
│   ├── Radio.Infrastructure/ # Audio management, configuration, external integrations
│   │   ├── Audio/SoundFlow/   # SoundFlow audio engine implementation (Phase 2)
│   │   │   ├── SoundFlowAudioEngine.cs
│   │   │   ├── SoundFlowMasterMixer.cs
│   │   │   ├── SoundFlowDeviceManager.cs
│   │   │   └── TappedOutputStream.cs
│   │   ├── Configuration/     # Configuration infrastructure (Phase 1)
│   │   │   ├── Abstractions/  # IConfigurationStore, ISecretsProvider, etc.
│   │   │   ├── Models/        # ConfigurationEntry, SecretTag, BackupMetadata
│   │   │   ├── Stores/        # JsonConfigurationStore, SqliteConfigurationStore
│   │   │   ├── Secrets/       # JsonSecretsProvider, SqliteSecretsProvider
│   │   │   ├── Backup/        # ConfigurationBackupService
│   │   │   └── Services/      # ConfigurationManager
│   │   └── DependencyInjection/ # Service registration extensions
│   ├── Radio.API/           # REST API and SignalR hubs
│   └── Radio.Web/           # Blazor Server UI
├── tests/
│   ├── Radio.Core.Tests/
│   ├── Radio.Infrastructure.Tests/
│   │   ├── Configuration/   # Tests for configuration infrastructure
│   │   └── Audio/           # Tests for audio engine components
│   └── Radio.API.Tests/
├── design/                   # Design documents
└── scripts/                  # Deployment and utility scripts
```

## Configuration System

The configuration infrastructure (Phase 1) provides:

- **Dual backing stores**: JSON files and SQLite database
- **Secrets management**: Tag-based substitution (`${secret:identifier}`)
- **Encrypted storage**: Secrets encrypted at rest using Data Protection API
- **Backup/restore**: Full configuration backup and restore capabilities
- **DI integration**: Easy registration via `AddManagedConfiguration()`

### Usage Example

```csharp
// Register services
services.AddManagedConfiguration(configuration);

// Use the configuration manager
var configManager = serviceProvider.GetRequiredService<IConfigurationManager>();

// Create and use a store
var store = await configManager.CreateStoreAsync("my-settings");
await store.SetEntryAsync("AppName", "My App");

// Create a secret
var secretTag = await configManager.CreateSecretAsync("my-settings", "ApiKey", "secret-value");
// Store now contains: ApiKey = ${secret:abc123}

// Read with secret resolution
var value = await configManager.GetValueAsync<string>("my-settings", "ApiKey");
// Returns: "secret-value"
```

## Audio System

The audio system (Phase 2) provides:

- **SoundFlow Integration**: Cross-platform audio engine using MiniAudio backend
- **Device Management**: Enumeration of ALSA/USB audio devices with hot-plug detection
- **Master Mixer**: Volume, balance, and mute controls with source management
- **Tapped Output Stream**: Ring buffer for streaming audio to Chromecast/HTTP clients
- **USB Port Management**: Conflict detection and reservation system for USB audio sources

### Usage Example

```csharp
// Register services
services.AddSoundFlowAudio(configuration);

// Get the audio engine
var audioEngine = serviceProvider.GetRequiredService<IAudioEngine>();

// Initialize and start
await audioEngine.InitializeAsync();
await audioEngine.StartAsync();

// Get the master mixer
var mixer = audioEngine.GetMasterMixer();
mixer.MasterVolume = 0.75f;
mixer.Balance = 0f; // Center

// Get the device manager
var deviceManager = serviceProvider.GetRequiredService<IAudioDeviceManager>();
var devices = await deviceManager.GetOutputDevicesAsync();

// Get the tapped output stream for streaming
var outputStream = audioEngine.GetMixedOutputStream();
```

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- For Raspberry Pi deployment: Raspberry Pi OS with .NET runtime

### Building

```bash
dotnet restore
dotnet build --configuration Release
```

### Running Tests

```bash
dotnet test --configuration Release
```

### Running the Applications

```bash
# Run the API
dotnet run --project src/Radio.API

# Run the Web UI
dotnet run --project src/Radio.Web
```

## Design Documents

- [Project Plan](PROJECTPLAN.md) - High-level project overview
- [Development Plan](PLAN.md) - Detailed development phases
- [Audio Architecture](design/AUDIO.md) - Audio system design
- [Configuration](design/CONFIGURATION.md) - Configuration infrastructure
- [Web UI](design/WEBUI.md) - UI design specifications

## License

See [LICENSE](LICENSE) file for details.

