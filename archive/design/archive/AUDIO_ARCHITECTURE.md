# Radio Console SoundFlow Audio Implementation Specification

## Project: Grandpa Anderson's Console Radio Remade

A modern audio command center hosted on a Raspberry Pi 5, encased in a vintage console radio cabinet. The software restores the original function (Radio/Vinyl) while adding modern capabilities (Spotify, Streaming, Smart Home Events, and Chromecast Audio).

---

## Executive Summary

This document provides a complete specification and phased implementation plan for reimplementing the Radio Console audio system using SoundFlow as the primary audio processing library. The implementation leverages the configuration infrastructure defined in `design/CONFIGURATION.md` and follows Clean Architecture principles.

### Technical Stack

| Component | Technology |
|-----------|------------|
| Hardware | Raspberry Pi 5 (Raspberry Pi OS / Linux) |
| Framework | .NET 8+ (C#) |
| Audio Engine | [SoundFlow](https://github.com/lsxprime/soundflow-docs/) |
| UI | Blazor Server (Material 3) |
| API | ASP.NET Core Web API |
| Real-time | SignalR |
| Streaming | HTTP Audio Stream Server |
| Database | Repository Pattern (SQLite / JSON) |
| Logging | Serilog (Console + File) |
| Configuration | Infrastructure from `design/CONFIGURATION.md` |

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Single ASP.NET Core Host                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  Blazor Server  │  Web API  │  SignalR Hub  │  Local Stream Server          │
├─────────────────────────────────────────────────────────────────────────────┤
│                          Radio.Core                                          │
│              (Interfaces, Entities, Domain Logic, Enums)                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                       Radio.Infrastructure                                   │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                    Audio Management (SoundFlow)                        │  │
│  │  ┌─────────────┬─────────────┬──────────────┬──────────────────────┐  │  │
│  │  │AudioManager │DuckingService│VisualizerSvc │ StreamOutputService  │  │  │
│  │  │(MixerNode)  │(Priority)    │(FFT/Levels)  │ (HTTP/Cast)          │  │  │
│  │  └─────────────┴─────────────┴──────────────┴──────────────────────┘  │  │
│  ├───────────────────────────────────────────────────────────────────────┤  │
│  │                    Configuration Infrastructure                        │  │
│  │           (From design/CONFIGURATION.md - SQLite/JSON)                 │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Audio Source Hierarchy

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AudioManager                                  │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │              Primary Audio Source (One Active)                  │ │
│  │  ┌──────────┬──────────┬──────────┬──────────┬──────────────┐  │ │
│  │  │ Spotify  │ Radio    │ Vinyl    │ File     │ Generic USB  │  │ │
│  │  │ (Stream) │ (RF320)  │ (USB)    │ Player   │ (SoundFlow)  │  │ │
│  │  └──────────┴──────────┴──────────┴──────────┴──────────────┘  │ │
│  └────────────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │          Event Sources (Ephemeral, Interrupt Primary)          │ │
│  │  ┌──────────────┬─────────────────┬─────────────────────────┐  │ │
│  │  │ TTS Event    │ Audio File Event│ Future Event Generators │  │ │
│  │  │ (eSpeak/     │ (Doorbell,      │ (Broadcasts, Alarms,    │  │ │
│  │  │  Cloud TTS)  │  Notifications) │  Wyze Doorbell, etc.)   │  │ │
│  │  └──────────────┴─────────────────┴─────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │                    Ducking System                               │ │
│  │  When Event plays → Fade Primary to configurable % → Restore   │ │
│  └────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Configuration Requirements

All configuration follows the infrastructure defined in `design/CONFIGURATION.md`. The `RootDir` configuration parameter is the base path for all file resources.

### Audio Configuration Stores

| Store Name | Type | Contents |
|------------|------|----------|
| `audio-config` | Configuration | Static settings (USB ports, default sources, TTS engines) |
| `audio-secrets` | Secrets | API keys (Spotify, Google TTS, Azure TTS) |
| `audio-preferences` | Preferences | User state (current source, volume, last played) |

### Configuration Keys

```yaml
# audio-config store
Audio:
  DefaultSource: "Spotify"           # Default primary audio source
  DuckingPercentage: 20              # Volume % when ducked (0-100)
  DuckingPolicy: "FadeSmooth"        # FadeSmooth, FadeQuick, Instant
  DuckingAttackMs: 100               # Fade-in time for ducking
  DuckingReleaseMs: 500              # Fade-out time after event ends

Devices:
  Radio:
    USBPort: "/dev/ttyUSB0"          # Raddy RF320 USB port
  Vinyl:
    USBPort: "/dev/ttyUSB1"          # Turntable USB port
  Cast:
    DefaultDevice: ""                 # Default Chromecast device name

FilePlayer:
  RootDirectory: "${RootDir}/media/audio"

TTS:
  DefaultEngine: "ESpeak"            # ESpeak, Google, Azure
  DefaultVoice: "en-US-Standard-A"
  DefaultPitch: 1.0
  DefaultSpeed: 1.0

# audio-secrets store (all use secret tags)
Spotify:
  ClientID: "${secret:spotify_client_id}"
  ClientSecret: "${secret:spotify_client_secret}"
  RefreshToken: "${secret:spotify_refresh_token}"

TTS:
  GoogleAPIKey: "${secret:google_tts_key}"
  AzureAPIKey: "${secret:azure_tts_key}"

# audio-preferences store
Audio:
  CurrentSource: "Spotify"
  MasterVolume: 75

Spotify:
  LastSongPlayed: ""
  SongPosition: 0
  Shuffle: false
  Repeat: "Off"

FilePlayer:
  LastSongPlayed: ""
  SongPosition: 0
  Shuffle: false
  Repeat: "Off"

Generic:
  USBPort: ""

TTS:
  LastEngine: "ESpeak"
  LastVoice: "en-US-Standard-A"
  LastPitch: 1.0

Cast:
  CurrentDevice: ""
```

---

## Phase 0: Project Setup & Repository Structure
**Duration:** 1-2 days  
**Risk Level:** Low  
**Priority:** Critical

### Objectives
1. Create project structure following Clean Architecture
2. Set up solution with proper project references
3. Configure CI/CD pipeline
4. Establish configuration infrastructure integration

### Agent Prompt

```markdown
## Task: Initialize Radio Console SoundFlow Project

Create a new .NET 8 solution for the Radio Console project with SoundFlow integration. This will be a clean implementation following Clean Architecture principles.

### Repository Structure to Create

```
RadioConsole/
├── .github/
│   ├── copilot-instructions.md
│   └── workflows/
│       ├── build.yml
│       └── test.yml
├── src/
│   ├── Radio.Core/
│   │   ├── Radio.Core.csproj
│   │   ├── Interfaces/
│   │   │   ├── Audio/
│   │   │   │   ├── IAudioManager.cs
│   │   │   │   ├── IAudioSource.cs
│   │   │   │   ├── IPrimaryAudioSource.cs
│   │   │   │   ├── IEventAudioSource.cs
│   │   │   │   ├── IAudioDeviceManager.cs
│   │   │   │   ├── IAudioOutput.cs
│   │   │   │   ├── IDuckingService.cs
│   │   │   │   ├── IVisualizerService.cs
│   │   │   │   └── ITTSFactory.cs
│   │   │   ├── Services/
│   │   │   └── Repositories/
│   │   ├── Models/
│   │   │   └── Audio/
│   │   ├── Enums/
│   │   │   └── Audio/
│   │   ├── Events/
│   │   │   └── Audio/
│   │   └── Exceptions/
│   ├── Radio.Infrastructure/
│   │   ├── Radio.Infrastructure.csproj
│   │   ├── Audio/
│   │   │   ├── SoundFlow/
│   │   │   │   ├── SoundFlowAudioEngine.cs
│   │   │   │   ├── SoundFlowDeviceManager.cs
│   │   │   │   └── SoundFlowMixer.cs
│   │   │   ├── Sources/
│   │   │   │   ├── Primary/
│   │   │   │   │   ├── SpotifyAudioSource.cs
│   │   │   │   │   ├── RadioAudioSource.cs
│   │   │   │   │   ├── VinylAudioSource.cs
│   │   │   │   │   ├── FilePlayerAudioSource.cs
│   │   │   │   │   └── GenericUSBAudioSource.cs
│   │   │   │   └── Events/
│   │   │   │       ├── TTSEventSource.cs
│   │   │   │       └── AudioFileEventSource.cs
│   │   │   ├── Outputs/
│   │   │   │   ├── LocalAudioOutput.cs
│   │   │   │   └── GoogleCastOutput.cs
│   │   │   ├── Services/
│   │   │   │   ├── AudioManager.cs
│   │   │   │   ├── DuckingService.cs
│   │   │   │   ├── VisualizerService.cs
│   │   │   │   └── TTSFactory.cs
│   │   │   └── Visualization/
│   │   │       ├── SpectrumAnalyzer.cs
│   │   │       ├── LevelMeter.cs
│   │   │       └── WaveformAnalyzer.cs
│   │   ├── Configuration/
│   │   │   └── (From design/CONFIGURATION.md implementation)
│   │   ├── External/
│   │   │   ├── Spotify/
│   │   │   └── GoogleCast/
│   │   └── DependencyInjection/
│   │       └── AudioServiceExtensions.cs
│   ├── Radio.API/
│   │   ├── Radio.API.csproj
│   │   ├── Controllers/
│   │   │   ├── AudioController.cs
│   │   │   ├── SourcesController.cs
│   │   │   ├── DevicesController.cs
│   │   │   └── VisualizationController.cs
│   │   ├── Hubs/
│   │   │   └── AudioVisualizationHub.cs
│   │   ├── Streaming/
│   │   │   └── AudioStreamMiddleware.cs
│   │   └── Program.cs
│   └── Radio.Web/
│       ├── Radio.Web.csproj
│       ├── Components/
│       │   ├── Audio/
│       │   │   ├── SourceSelector.razor
│       │   │   ├── VolumeControl.razor
│       │   │   ├── SpectrumVisualizer.razor
│       │   │   └── NowPlaying.razor
│       │   └── Layout/
│       └── wwwroot/
├── tests/
│   ├── Radio.Core.Tests/
│   ├── Radio.Infrastructure.Tests/
│   │   └── Audio/
│   └── Radio.API.Tests/
├── design/
│   ├── AUDIO.md (this file)
│   └── CONFIGURATION.md
├── scripts/
│   ├── setup-pi.sh
│   └── deploy.sh
├── RadioConsole.sln
├── Directory.Build.props
├── Directory.Packages.props
├── README.md
└── PROJECT_STATUS.md
```

### Project Dependencies

**Radio.Core.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**Radio.Infrastructure.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SoundFlow" Version="1.*" />
    <PackageReference Include="SoundFlow.Backends.MiniAudio" Version="1.*" />
    <PackageReference Include="SpotifyAPI.Web" Version="7.*" />
    <PackageReference Include="SharpCaster" Version="2.*" />
    <PackageReference Include="Serilog" Version="4.*" />
    <PackageReference Include="Serilog.Extensions.Logging" Version="8.*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Radio.Core\Radio.Core.csproj" />
  </ItemGroup>
</Project>
```

**Radio.API.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.*" />
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Radio.Core\Radio.Core.csproj" />
    <ProjectReference Include="..\Radio.Infrastructure\Radio.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

### Directory.Build.props
```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

### GitHub Actions Workflow (build.yml)
```yaml
name: Build and Test

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore --configuration Release
      - name: Test
        run: dotnet test --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage"
```

### PROJECT_STATUS.md Template
```markdown
# Radio Console SoundFlow - Project Status

## Current Phase: Phase 0 - Project Setup
**Last Updated:** [DATE]
**Status:** In Progress

## Phases Overview

| Phase | Name | Status | Start Date | End Date |
|-------|------|--------|------------|----------|
| 0 | Project Setup | 🟡 In Progress | - | - |
| 1 | Configuration Integration | ⚪ Not Started | - | - |
| 2 | Core Audio Engine | ⚪ Not Started | - | - |
| 3 | Primary Audio Sources | ⚪ Not Started | - | - |
| 4 | Event Audio Sources | ⚪ Not Started | - | - |
| 5 | Ducking & Priority | ⚪ Not Started | - | - |
| 6 | Audio Outputs | ⚪ Not Started | - | - |
| 7 | Visualization & Monitoring | ⚪ Not Started | - | - |
| 8 | API & SignalR Integration | ⚪ Not Started | - | - |
| 9 | Blazor UI Components | ⚪ Not Started | - | - |
| 10 | Testing & Quality | ⚪ Not Started | - | - |
| 11 | Deployment & Optimization | ⚪ Not Started | - | - |
```

### Requirements
1. Create all project files with proper namespaces matching folder structure
2. Ensure solution builds successfully on both Windows and Linux
3. All projects should use 2-space indentation
4. Include XML documentation comments on all public members
5. Create placeholder interfaces in Core project for future implementation
6. Set up .editorconfig for code style consistency
7. All file paths should be relative to `RootDir` configuration (except web assets)

### Success Criteria
- [ ] Solution builds without errors
- [ ] All projects properly reference each other
- [ ] CI/CD pipeline runs successfully
- [ ] PROJECT_STATUS.md is initialized
```

---

## Phase 1: Configuration Infrastructure Integration
**Duration:** 2-3 days  
**Risk Level:** Low  
**Priority:** Critical  
**Dependencies:** Phase 0

### Objectives
1. Implement configuration infrastructure from `design/CONFIGURATION.md`
2. Create audio-specific configuration stores
3. Set up secrets management for API keys
4. Integrate with Microsoft.Extensions.Configuration

### Agent Prompt

```markdown
## Task: Implement Configuration Infrastructure for Audio System

Implement the configuration infrastructure as specified in `design/CONFIGURATION.md` and create audio-specific configuration stores.

### Reference
Review the complete specification in `design/CONFIGURATION.md` for:
- IConfigurationStore interface and implementations
- ISecretsProvider for API key management
- IConfigurationManager for high-level access
- Backup/Restore capabilities

### Files to Create

#### 1. Create Audio Configuration Options

**src/Radio.Core/Configuration/AudioOptions.cs**
```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the audio system.
/// Loaded from the 'audio-config' configuration store.
/// </summary>
public class AudioOptions
{
  public const string SectionName = "Audio";

  /// <summary>Default primary audio source name.</summary>
  public string DefaultSource { get; set; } = "Spotify";

  /// <summary>Volume percentage when primary source is ducked (0-100).</summary>
  public int DuckingPercentage { get; set; } = 20;

  /// <summary>Ducking transition policy.</summary>
  public DuckingPolicy DuckingPolicy { get; set; } = DuckingPolicy.FadeSmooth;

  /// <summary>Ducking attack time in milliseconds.</summary>
  public int DuckingAttackMs { get; set; } = 100;

  /// <summary>Ducking release time in milliseconds.</summary>
  public int DuckingReleaseMs { get; set; } = 500;
}

public enum DuckingPolicy
{
  FadeSmooth,
  FadeQuick,
  Instant
}
```

**src/Radio.Core/Configuration/DeviceOptions.cs**
```csharp
namespace Radio.Core.Configuration;

public class DeviceOptions
{
  public const string SectionName = "Devices";

  public RadioDeviceOptions Radio { get; set; } = new();
  public VinylDeviceOptions Vinyl { get; set; } = new();
  public CastDeviceOptions Cast { get; set; } = new();
}

public class RadioDeviceOptions
{
  public string USBPort { get; set; } = "/dev/ttyUSB0";
}

public class VinylDeviceOptions
{
  public string USBPort { get; set; } = "/dev/ttyUSB1";
}

public class CastDeviceOptions
{
  public string DefaultDevice { get; set; } = "";
}
```

**src/Radio.Core/Configuration/FilePlayerOptions.cs**
```csharp
namespace Radio.Core.Configuration;

public class FilePlayerOptions
{
  public const string SectionName = "FilePlayer";

  /// <summary>Root directory for audio files (relative to RootDir).</summary>
  public string RootDirectory { get; set; } = "media/audio";
}
```

**src/Radio.Core/Configuration/TTSOptions.cs**
```csharp
namespace Radio.Core.Configuration;

public class TTSOptions
{
  public const string SectionName = "TTS";

  public string DefaultEngine { get; set; } = "ESpeak";
  public string DefaultVoice { get; set; } = "en-US-Standard-A";
  public float DefaultPitch { get; set; } = 1.0f;
  public float DefaultSpeed { get; set; } = 1.0f;
}

public enum TTSEngine
{
  ESpeak,
  Google,
  Azure
}
```

**src/Radio.Core/Configuration/SpotifySecrets.cs**
```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// Spotify API credentials.
/// All values are resolved from secret tags.
/// </summary>
public class SpotifySecrets
{
  public const string SectionName = "Spotify";

  public string ClientID { get; set; } = "";
  public string ClientSecret { get; set; } = "";
  public string RefreshToken { get; set; } = "";
}
```

#### 2. Create Audio Preferences Model

**src/Radio.Core/Configuration/AudioPreferences.cs**
```csharp
namespace Radio.Core.Configuration;

/// <summary>
/// User preferences for audio playback.
/// Persisted via IOptions pattern to 'audio-preferences' store.
/// </summary>
public class AudioPreferences
{
  public const string SectionName = "Audio";

  public string CurrentSource { get; set; } = "Spotify";
  public int MasterVolume { get; set; } = 75;
}

public class SpotifyPreferences
{
  public const string SectionName = "Spotify";

  public string LastSongPlayed { get; set; } = "";
  public long SongPositionMs { get; set; } = 0;
  public bool Shuffle { get; set; } = false;
  public RepeatMode Repeat { get; set; } = RepeatMode.Off;
}

public class FilePlayerPreferences
{
  public const string SectionName = "FilePlayer";

  public string LastSongPlayed { get; set; } = "";
  public long SongPositionMs { get; set; } = 0;
  public bool Shuffle { get; set; } = false;
  public RepeatMode Repeat { get; set; } = RepeatMode.Off;
}

public class TTSPreferences
{
  public const string SectionName = "TTS";

  public string LastEngine { get; set; } = "ESpeak";
  public string LastVoice { get; set; } = "en-US-Standard-A";
  public float LastPitch { get; set; } = 1.0f;
}

public class CastPreferences
{
  public const string SectionName = "Cast";

  public string CurrentDevice { get; set; } = "";
}

public class GenericSourcePreferences
{
  public const string SectionName = "Generic";

  public string USBPort { get; set; } = "";
}

public enum RepeatMode
{
  Off,
  One,
  All
}
```

#### 3. Implement Configuration Store Registration

**src/Radio.Infrastructure/DependencyInjection/ConfigurationExtensions.cs**
```csharp
namespace Radio.Infrastructure.DependencyInjection;

public static class ConfigurationExtensions
{
  /// <summary>
  /// Registers the audio configuration infrastructure.
  /// </summary>
  public static IServiceCollection AddAudioConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Register configuration stores
    // Implementation follows design/CONFIGURATION.md
    
    // Bind configuration sections
    services.Configure<AudioOptions>(configuration.GetSection(AudioOptions.SectionName));
    services.Configure<DeviceOptions>(configuration.GetSection(DeviceOptions.SectionName));
    services.Configure<FilePlayerOptions>(configuration.GetSection(FilePlayerOptions.SectionName));
    services.Configure<TTSOptions>(configuration.GetSection(TTSOptions.SectionName));
    
    // Bind secrets (resolved from secret tags)
    services.Configure<SpotifySecrets>(configuration.GetSection(SpotifySecrets.SectionName));
    
    // Register preference services with auto-save
    // These use the UserPreferencesService pattern from CONFIGURATION.md
    services.AddSingleton<IOptionsMonitor<AudioPreferences>>(sp => /* implementation */);
    services.AddSingleton<IOptionsMonitor<SpotifyPreferences>>(sp => /* implementation */);
    services.AddSingleton<IOptionsMonitor<FilePlayerPreferences>>(sp => /* implementation */);
    
    return services;
  }
}
```

### Implementation Requirements

1. Follow the complete interface specifications from `design/CONFIGURATION.md`:
   - `IConfigurationStore` with Raw/Resolved read modes
   - `ISecretsProvider` with tag-based substitution
   - `IConfigurationManager` for high-level access
   - `IConfigurationBackupService` for backup/restore

2. Create three configuration stores:
   - `audio-config` (static configuration)
   - `audio-secrets` (API keys via secret tags)
   - `audio-preferences` (user preferences with auto-save)

3. Ensure all file paths are resolved relative to `RootDir`:
   ```csharp
   var fullPath = Path.Combine(rootDir, options.RootDirectory);
   ```

4. Support hot-swap between SQLite and JSON backing stores

### Unit Tests Required

1. **ConfigurationStoreTests.cs**
   - Test store creation for both JSON and SQLite
   - Test CRUD operations on entries
   - Test secret tag resolution
   - Test Raw vs Resolved read modes

2. **AudioOptionsTests.cs**
   - Test binding from configuration
   - Test default values
   - Test validation

### Success Criteria
- [ ] All configuration stores created and accessible
- [ ] Secret tags resolve correctly for API keys
- [ ] Preferences auto-save on change
- [ ] RootDir paths resolve correctly
- [ ] Hot-swap between SQLite and JSON works
```

---

## Phase 2: Core Audio Engine (SoundFlow)
**Duration:** 5-7 days  
**Risk Level:** Low  
**Priority:** Critical  
**Dependencies:** Phase 1

### Objectives
1. Initialize SoundFlow AudioEngine with MiniAudio backend
2. Implement device enumeration for ALSA/USB devices
3. Create master output node with mixed audio stream
4. Support hot-plug detection for USB audio devices

### Agent Prompt

```markdown
## Task: Implement Core SoundFlow Audio Engine

Implement the foundation audio engine using the SoundFlow library. All audio components should leverage SoundFlow as the primary audio processing library.

### Reference: SoundFlow Documentation
- GitHub: https://github.com/lsxprime/soundflow-docs/
- Uses MiniAudio backend for cross-platform audio

### Files to Create

#### 1. src/Radio.Core/Interfaces/Audio/IAudioEngine.cs
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Core audio engine interface wrapping SoundFlow functionality.
/// Manages the audio graph, device connection, and real-time audio processing.
/// </summary>
public interface IAudioEngine : IAsyncDisposable
{
  /// <summary>Initialize the audio engine with configured settings.</summary>
  Task InitializeAsync(CancellationToken cancellationToken = default);

  /// <summary>Start audio processing.</summary>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>Stop audio processing.</summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>Get the master mixer node for connecting audio sources.</summary>
  IMasterMixer GetMasterMixer();

  /// <summary>Get a stream of the mixed audio output for streaming/recording.</summary>
  /// <remarks>
  /// This stream is used by the Local Stream Server for Chromecast integration.
  /// Returns real audio data, not placeholder.
  /// </remarks>
  Stream GetMixedOutputStream();

  /// <summary>Current engine state.</summary>
  AudioEngineState State { get; }

  /// <summary>Whether the engine is initialized and ready.</summary>
  bool IsReady { get; }

  /// <summary>Event raised when engine state changes.</summary>
  event EventHandler<AudioEngineStateChangedEventArgs>? StateChanged;

  /// <summary>Event raised when audio device changes (hot-plug).</summary>
  event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;
}

public enum AudioEngineState
{
  Uninitialized,
  Initializing,
  Ready,
  Running,
  Stopping,
  Error,
  Disposed
}
```

#### 2. src/Radio.Core/Interfaces/Audio/IAudioDeviceManager.cs
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for enumerating system ALSA/PulseAudio/USB devices.
/// Leverages SoundFlow's device enumeration capabilities.
/// </summary>
public interface IAudioDeviceManager
{
  /// <summary>Get all available output devices.</summary>
  Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default);

  /// <summary>Get all available input devices (USB audio, etc.).</summary>
  Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default);

  /// <summary>Get the current default output device.</summary>
  Task<AudioDeviceInfo?> GetDefaultOutputDeviceAsync(CancellationToken cancellationToken = default);

  /// <summary>Set the preferred output device.</summary>
  Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

  /// <summary>Check if a specific USB port is in use by another source.</summary>
  bool IsUSBPortInUse(string usbPort);

  /// <summary>Reserve a USB port for a source.</summary>
  void ReserveUSBPort(string usbPort, string sourceId);

  /// <summary>Release a USB port reservation.</summary>
  void ReleaseUSBPort(string usbPort);

  /// <summary>Refresh the device list (manual hot-plug check).</summary>
  Task RefreshDevicesAsync(CancellationToken cancellationToken = default);

  /// <summary>Event raised when devices change.</summary>
  event EventHandler<AudioDeviceChangedEventArgs>? DevicesChanged;
}

public record AudioDeviceInfo
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required AudioDeviceType Type { get; init; }
  public bool IsDefault { get; init; }
  public int MaxChannels { get; init; }
  public int[] SupportedSampleRates { get; init; } = [];
  public string? AlsaDeviceId { get; init; }
  public string? USBPort { get; init; }
  public bool IsUSBDevice { get; init; }
}

public enum AudioDeviceType
{
  Output,
  Input,
  Duplex
}
```

#### 3. src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs

```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// SoundFlow audio engine implementation.
/// Manages the audio graph, device connection, and real-time processing.
/// </summary>
public class SoundFlowAudioEngine : IAudioEngine
{
  private readonly ILogger<SoundFlowAudioEngine> _logger;
  private readonly AudioOptions _options;
  private readonly IConfigurationManager _configManager;
  
  private AudioEngine? _engine;
  private AudioPlaybackDevice? _playbackDevice;
  private TappedOutputStream? _outputTap;
  private Timer? _hotPlugTimer;
  private AudioEngineState _state = AudioEngineState.Uninitialized;

  // Implementation requirements:
  // 1. Initialize MiniAudioEngine from SoundFlow
  // 2. Configure sample rate (48kHz), buffer size, channels from options
  // 3. Handle ALSA device selection on Linux (Raspberry Pi)
  // 4. Create TappedOutputStream for real mixed audio capture
  // 5. Implement hot-plug detection with configurable polling interval
  // 6. Proper error handling and state management
  // 7. Thread-safe operations
}

/// <summary>
/// Stream that captures audio data from SoundFlow output for streaming.
/// Uses a lock-free ring buffer for real-time audio capture.
/// </summary>
internal class TappedOutputStream : Stream
{
  private readonly RingBuffer<byte> _buffer;
  private readonly int _sampleRate;
  private readonly int _channels;
  private readonly int _bytesPerSample;

  public TappedOutputStream(int sampleRate = 48000, int channels = 2, int bufferSizeSeconds = 5)
  {
    _sampleRate = sampleRate;
    _channels = channels;
    _bytesPerSample = 2; // 16-bit PCM
    
    var bufferSize = sampleRate * channels * _bytesPerSample * bufferSizeSeconds;
    _buffer = new RingBuffer<byte>(bufferSize);
  }

  /// <summary>
  /// Called by SoundFlow to write processed audio data.
  /// </summary>
  public void WriteFromEngine(float[] samples)
  {
    // Convert float samples to 16-bit PCM bytes
    // Write to ring buffer (non-blocking)
  }

  public override int Read(byte[] buffer, int offset, int count)
  {
    // Read from ring buffer (non-blocking, return 0 if empty)
    return _buffer.Read(buffer, offset, count);
  }

  // Implement other Stream members...
}
```

#### 4. src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowDeviceManager.cs

```csharp
namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Device manager using SoundFlow for enumeration.
/// Tracks USB port reservations to prevent conflicts.
/// </summary>
public class SoundFlowDeviceManager : IAudioDeviceManager
{
  private readonly ILogger<SoundFlowDeviceManager> _logger;
  private readonly AudioEngine _engine;
  private readonly Dictionary<string, string> _usbPortReservations = new();
  private readonly object _reservationLock = new();
  private IReadOnlyList<AudioDeviceInfo>? _cachedOutputDevices;
  private IReadOnlyList<AudioDeviceInfo>? _cachedInputDevices;

  public bool IsUSBPortInUse(string usbPort)
  {
    lock (_reservationLock)
    {
      return _usbPortReservations.ContainsKey(usbPort);
    }
  }

  public void ReserveUSBPort(string usbPort, string sourceId)
  {
    lock (_reservationLock)
    {
      if (_usbPortReservations.ContainsKey(usbPort))
      {
        throw new AudioDeviceConflictException(
          $"USB port '{usbPort}' is already in use by source '{_usbPortReservations[usbPort]}'");
      }
      _usbPortReservations[usbPort] = sourceId;
    }
  }

  public void ReleaseUSBPort(string usbPort)
  {
    lock (_reservationLock)
    {
      _usbPortReservations.Remove(usbPort);
    }
  }

  // Enumerate devices using SoundFlow's PlaybackDevices and CaptureDevices
  // Map to AudioDeviceInfo with ALSA device IDs and USB port detection
}
```

### Unit Tests Required

1. **SoundFlowAudioEngineTests.cs**
   - Test initialization and state transitions
   - Test device enumeration
   - Test mixed output stream produces data
   - Test hot-plug detection
   - Test proper disposal

2. **SoundFlowDeviceManagerTests.cs**
   - Test USB port reservation/release
   - Test conflict detection
   - Test device caching

### Success Criteria
- [ ] Audio engine initializes on Raspberry Pi (ALSA)
- [ ] Device enumeration returns USB audio devices
- [ ] Mixed output stream captures real audio data
- [ ] Hot-plug detection works for USB devices
- [ ] USB port conflict detection works
- [ ] All unit tests pass
```

---

## Phase 3: Primary Audio Sources
**Duration:** 7-10 days  
**Risk Level:** Medium  
**Priority:** High  
**Dependencies:** Phase 2

### Objectives
1. Implement base primary audio source interface
2. Create Spotify integration with SpotifyAPI-NET
3. Implement USB audio sources (Radio, Vinyl, Generic)
4. Create File Player with directory support

### Agent Prompt

```markdown
## Task: Implement Primary Audio Sources

Create the primary audio source implementations. These are the main audio sources that play continuously (one active at a time).

### Audio Source Hierarchy

Primary sources are mutually exclusive - only one can be active at a time:
- **Spotify**: Streaming via SpotifyAPI-NET
- **Raddy RF320 Radio**: USB audio input
- **Vinyl Turntable**: USB audio input
- **Audio File Player**: Local files/directories
- **Generic USB**: User-selected USB audio device

### Files to Create

#### 1. src/Radio.Core/Interfaces/Audio/IAudioSource.cs
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Base interface for all audio sources.
/// </summary>
public interface IAudioSource : IAsyncDisposable
{
  /// <summary>Unique identifier for this source instance.</summary>
  string Id { get; }

  /// <summary>Human-readable name of the source.</summary>
  string Name { get; }

  /// <summary>Type of audio source.</summary>
  AudioSourceType Type { get; }

  /// <summary>Whether this is a primary or event source.</summary>
  AudioSourceCategory Category { get; }

  /// <summary>Current playback state.</summary>
  AudioSourceState State { get; }

  /// <summary>Volume level (0.0 to 1.0).</summary>
  float Volume { get; set; }

  /// <summary>Get the underlying SoundFlow component for mixer connection.</summary>
  object GetSoundComponent();

  /// <summary>Event raised when state changes.</summary>
  event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;
}

public enum AudioSourceType
{
  Spotify,
  Radio,
  Vinyl,
  FilePlayer,
  GenericUSB,
  TTS,
  AudioFileEvent
}

public enum AudioSourceCategory
{
  Primary,
  Event
}

public enum AudioSourceState
{
  Created,
  Initializing,
  Ready,
  Playing,
  Paused,
  Stopped,
  Error,
  Disposed
}
```

#### 2. src/Radio.Core/Interfaces/Audio/IPrimaryAudioSource.cs
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for primary audio sources (continuous playback).
/// Only one primary source can be active at a time.
/// </summary>
public interface IPrimaryAudioSource : IAudioSource
{
  /// <summary>Duration of current content (null for streams).</summary>
  TimeSpan? Duration { get; }

  /// <summary>Current playback position.</summary>
  TimeSpan Position { get; }

  /// <summary>Whether seeking is supported.</summary>
  bool IsSeekable { get; }

  /// <summary>Start playback.</summary>
  Task PlayAsync(CancellationToken cancellationToken = default);

  /// <summary>Pause playback.</summary>
  Task PauseAsync(CancellationToken cancellationToken = default);

  /// <summary>Resume from pause.</summary>
  Task ResumeAsync(CancellationToken cancellationToken = default);

  /// <summary>Stop playback and reset position.</summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>Seek to position (if seekable).</summary>
  Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

  /// <summary>Event raised when playback completes (for finite sources).</summary>
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  /// <summary>Metadata about current playing content.</summary>
  IReadOnlyDictionary<string, string> Metadata { get; }
}
```

#### 3. src/Radio.Infrastructure/Audio/Sources/Primary/SpotifyAudioSource.cs

```csharp
using SpotifyAPI.Web;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Spotify audio source using SpotifyAPI-NET.
/// Streams audio from Spotify Connect.
/// </summary>
public class SpotifyAudioSource : IPrimaryAudioSource
{
  private readonly ILogger<SpotifyAudioSource> _logger;
  private readonly IOptionsMonitor<SpotifySecrets> _secrets;
  private readonly IOptionsMonitor<SpotifyPreferences> _preferences;
  private readonly IConfigurationManager _configManager;
  
  private SpotifyClient? _client;
  private string? _currentTrackUri;

  public AudioSourceType Type => AudioSourceType.Spotify;
  public AudioSourceCategory Category => AudioSourceCategory.Primary;

  // Configuration from secrets store:
  // - ClientID, ClientSecret, RefreshToken
  
  // Preferences (auto-saved):
  // - LastSongPlayed, SongPosition, Shuffle, Repeat

  public async Task InitializeAsync(CancellationToken ct = default)
  {
    // 1. Get credentials from secrets
    var secrets = _secrets.CurrentValue;
    
    // 2. Create SpotifyClient with refresh token auth
    var config = SpotifyClientConfig.CreateDefault()
      .WithAuthenticator(new AuthorizationCodeAuthenticator(
        secrets.ClientID,
        secrets.ClientSecret,
        new AuthorizationCodeTokenResponse { RefreshToken = secrets.RefreshToken }
      ));
    
    _client = new SpotifyClient(config);
    
    // 3. Restore last playback state from preferences
    var prefs = _preferences.CurrentValue;
    if (!string.IsNullOrEmpty(prefs.LastSongPlayed))
    {
      _currentTrackUri = prefs.LastSongPlayed;
    }
  }

  public async Task PlayAsync(CancellationToken ct = default)
  {
    // Start playback via Spotify API
    // Update preferences with current track
  }

  // Additional implementation...
}
```

#### 4. src/Radio.Infrastructure/Audio/Sources/Primary/RadioAudioSource.cs

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Raddy RF320 USB Radio audio source.
/// Captures audio from USB audio input.
/// </summary>
public class RadioAudioSource : IPrimaryAudioSource
{
  private readonly ILogger<RadioAudioSource> _logger;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IAudioEngine _engine;

  public AudioSourceType Type => AudioSourceType.Radio;
  public AudioSourceCategory Category => AudioSourceCategory.Primary;
  public bool IsSeekable => false; // Live input

  // Configuration:
  // - USBPort from DeviceOptions.Radio.USBPort

  public async Task InitializeAsync(CancellationToken ct = default)
  {
    var usbPort = _deviceOptions.CurrentValue.Radio.USBPort;
    
    // Check if USB port is available
    if (_deviceManager.IsUSBPortInUse(usbPort))
    {
      throw new AudioDeviceConflictException(
        $"USB port '{usbPort}' is already in use by another source");
    }
    
    // Reserve the USB port
    _deviceManager.ReserveUSBPort(usbPort, Id);
    
    // Create SoundFlow audio capture for USB input
    // Connect to mixer
  }

  protected override void Dispose(bool disposing)
  {
    // Release USB port reservation
    _deviceManager.ReleaseUSBPort(_deviceOptions.CurrentValue.Radio.USBPort);
    base.Dispose(disposing);
  }
}
```

#### 5. src/Radio.Infrastructure/Audio/Sources/Primary/VinylAudioSource.cs

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Vinyl turntable USB audio source.
/// Captures audio from USB audio input.
/// </summary>
public class VinylAudioSource : IPrimaryAudioSource
{
  // Similar to RadioAudioSource but uses DeviceOptions.Vinyl.USBPort
  // May include RIAA equalization for phono preamp if needed
}
```

#### 6. src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Audio file player source.
/// Supports single file, file list, or directory playback.
/// </summary>
public class FilePlayerAudioSource : IPrimaryAudioSource
{
  private readonly ILogger<FilePlayerAudioSource> _logger;
  private readonly IOptionsMonitor<FilePlayerOptions> _options;
  private readonly IOptionsMonitor<FilePlayerPreferences> _preferences;
  private readonly IConfigurationManager _configManager;
  private readonly string _rootDir;
  
  private Queue<string> _playlist = new();
  private string? _currentFile;

  public AudioSourceType Type => AudioSourceType.FilePlayer;
  public AudioSourceCategory Category => AudioSourceCategory.Primary;
  public bool IsSeekable => true;

  // Configuration:
  // - RootDirectory from FilePlayerOptions (relative to RootDir)
  
  // Preferences (auto-saved):
  // - LastSongPlayed, SongPosition, Shuffle, Repeat

  /// <summary>
  /// Load a single file for playback.
  /// </summary>
  public async Task LoadFileAsync(string filePath, CancellationToken ct = default)
  {
    var fullPath = Path.Combine(_rootDir, _options.CurrentValue.RootDirectory, filePath);
    if (!File.Exists(fullPath))
    {
      throw new FileNotFoundException($"Audio file not found: {fullPath}");
    }
    
    _playlist.Clear();
    _playlist.Enqueue(fullPath);
    await LoadCurrentFileAsync(ct);
  }

  /// <summary>
  /// Load a directory for playback.
  /// </summary>
  public async Task LoadDirectoryAsync(string directoryPath, CancellationToken ct = default)
  {
    var fullPath = Path.Combine(_rootDir, _options.CurrentValue.RootDirectory, directoryPath);
    var audioFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories)
      .Where(f => IsAudioFile(f))
      .ToList();
    
    if (_preferences.CurrentValue.Shuffle)
    {
      audioFiles = audioFiles.OrderBy(_ => Random.Shared.Next()).ToList();
    }
    
    _playlist = new Queue<string>(audioFiles);
    await LoadCurrentFileAsync(ct);
  }

  private static bool IsAudioFile(string path)
  {
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext is ".mp3" or ".flac" or ".wav" or ".ogg" or ".aac" or ".m4a" or ".wma";
  }
}
```

#### 7. src/Radio.Infrastructure/Audio/Sources/Primary/GenericUSBAudioSource.cs

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Generic USB audio source selected from SoundFlow device list.
/// </summary>
public class GenericUSBAudioSource : IPrimaryAudioSource
{
  private readonly ILogger<GenericUSBAudioSource> _logger;
  private readonly IOptionsMonitor<GenericSourcePreferences> _preferences;
  private readonly IAudioDeviceManager _deviceManager;

  public AudioSourceType Type => AudioSourceType.GenericUSB;
  public AudioSourceCategory Category => AudioSourceCategory.Primary;
  public bool IsSeekable => false; // Live input

  // Preferences:
  // - USBPort (user-selected)

  public async Task InitializeAsync(string usbPort, CancellationToken ct = default)
  {
    // Check if USB port is available
    if (_deviceManager.IsUSBPortInUse(usbPort))
    {
      throw new AudioDeviceConflictException(
        $"USB port '{usbPort}' is already in use by another source. " +
        "Please select a different device or stop the conflicting source.");
    }
    
    // Reserve and connect
    _deviceManager.ReserveUSBPort(usbPort, Id);
    
    // Save to preferences for next session
    await UpdatePreferencesAsync(p => p.USBPort = usbPort);
  }
}
```

### Unit Tests Required

1. **SpotifyAudioSourceTests.cs**
   - Test initialization with valid/invalid credentials
   - Test playback state transitions
   - Test preferences auto-save

2. **USBAudioSourceTests.cs**
   - Test USB port reservation
   - Test conflict detection
   - Test cleanup on disposal

3. **FilePlayerAudioSourceTests.cs**
   - Test file loading
   - Test directory scanning
   - Test shuffle/repeat modes
   - Test seek operations

### Success Criteria
- [ ] All primary sources initialize correctly
- [ ] USB port conflicts are detected and reported
- [ ] Preferences auto-save on state changes
- [ ] Only one primary source active at a time
- [ ] File player supports all specified audio formats
```

---

## Phase 4: Event Audio Sources
**Duration:** 4-5 days  
**Risk Level:** Low  
**Priority:** High  
**Dependencies:** Phase 2

### Objectives
1. Implement event audio source interface
2. Create TTS Event source with multiple engines
3. Implement Audio File Event source
4. Set up TTS Factory with engine selection

### Agent Prompt

```markdown
## Task: Implement Event Audio Sources

Create ephemeral audio sources for events (TTS announcements, notifications, doorbell sounds). These are short, interrupt the primary source via ducking, and auto-dispose when complete.

### Event Source Behavior
- Ephemeral: Created, plays once, then disposed
- Triggers ducking of primary source
- Higher priority than primary sources
- Auto-cleanup when playback completes

### Files to Create

#### 1. src/Radio.Core/Interfaces/Audio/IEventAudioSource.cs
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for event audio sources (ephemeral, interrupt primary).
/// Events trigger ducking of the primary source.
/// </summary>
public interface IEventAudioSource : IAudioSource
{
  /// <summary>Duration of the event audio.</summary>
  TimeSpan Duration { get; }

  /// <summary>Play the event audio (one-shot).</summary>
  Task PlayAsync(CancellationToken cancellationToken = default);

  /// <summary>Stop playback immediately.</summary>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>Event raised when playback completes.</summary>
  event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;
}
```

#### 2. src/Radio.Core/Interfaces/Audio/ITTSFactory.cs
```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Factory for creating TTS audio from text.
/// Supports multiple TTS engines.
/// </summary>
public interface ITTSFactory
{
  /// <summary>Available TTS engines.</summary>
  IReadOnlyList<TTSEngineInfo> AvailableEngines { get; }

  /// <summary>Create TTS audio with specified parameters.</summary>
  Task<IEventAudioSource> CreateAsync(
    string text,
    TTSParameters? parameters = null,
    CancellationToken cancellationToken = default);

  /// <summary>Get available voices for an engine.</summary>
  Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default);
}

public record TTSParameters
{
  public TTSEngine Engine { get; init; } = TTSEngine.ESpeak;
  public string Voice { get; init; } = "en-US-Standard-A";
  public float Speed { get; init; } = 1.0f;
  public float Pitch { get; init; } = 1.0f;
}

public record TTSEngineInfo
{
  public required TTSEngine Engine { get; init; }
  public required string Name { get; init; }
  public required bool IsAvailable { get; init; }
  public bool RequiresApiKey { get; init; }
}

public record TTSVoiceInfo
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required string Language { get; init; }
  public required TTSVoiceGender Gender { get; init; }
}

public enum TTSVoiceGender
{
  Male,
  Female,
  Neutral
}
```

#### 3. src/Radio.Infrastructure/Audio/Sources/Events/TTSEventSource.cs
```csharp
namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Text-to-Speech event audio source.
/// Generates speech audio from text using configured TTS engine.
/// </summary>
public class TTSEventSource : IEventAudioSource
{
  private readonly ILogger<TTSEventSource> _logger;
  private readonly string _text;
  private readonly TTSParameters _parameters;
  private readonly Stream _audioStream;

  public AudioSourceType Type => AudioSourceType.TTS;
  public AudioSourceCategory Category => AudioSourceCategory.Event;

  internal TTSEventSource(
    string text,
    TTSParameters parameters,
    Stream audioStream,
    ILogger<TTSEventSource> logger)
  {
    _text = text;
    _parameters = parameters;
    _audioStream = audioStream;
    _logger = logger;
    
    Id = $"tts-{Guid.NewGuid():N}";
    Name = $"TTS: {text.Substring(0, Math.Min(50, text.Length))}...";
  }

  public async Task PlayAsync(CancellationToken ct = default)
  {
    // Connect audio stream to SoundFlow
    // Play through mixer
    // Raise PlaybackCompleted when done
  }
}
```

#### 4. src/Radio.Infrastructure/Audio/Services/TTSFactory.cs
```csharp
namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Factory for creating TTS audio sources.
/// Supports eSpeak (offline), Google Cloud TTS, and Azure TTS.
/// </summary>
public class TTSFactory : ITTSFactory
{
  private readonly ILogger<TTSFactory> _logger;
  private readonly IOptionsMonitor<TTSOptions> _options;
  private readonly IOptionsMonitor<TTSPreferences> _preferences;
  private readonly IConfigurationManager _configManager;

  // Secrets from configuration:
  // - GoogleAPIKey for Google Cloud TTS
  // - AzureAPIKey for Azure TTS

  public async Task<IEventAudioSource> CreateAsync(
    string text,
    TTSParameters? parameters = null,
    CancellationToken ct = default)
  {
    var opts = _options.CurrentValue;
    var prefs = _preferences.CurrentValue;

    // Use provided parameters or fall back to defaults/preferences
    var engine = parameters?.Engine ?? Enum.Parse<TTSEngine>(prefs.LastEngine);
    var voice = parameters?.Voice ?? prefs.LastVoice;
    var speed = parameters?.Speed ?? opts.DefaultSpeed;
    var pitch = parameters?.Pitch ?? prefs.LastPitch;

    // Generate audio based on engine
    Stream audioStream = engine switch
    {
      TTSEngine.ESpeak => await GenerateESpeakAsync(text, voice, speed, pitch, ct),
      TTSEngine.Google => await GenerateGoogleTTSAsync(text, voice, speed, pitch, ct),
      TTSEngine.Azure => await GenerateAzureTTSAsync(text, voice, speed, pitch, ct),
      _ => throw new NotSupportedException($"TTS engine '{engine}' is not supported")
    };

    // Update preferences with last used settings
    await UpdatePreferencesAsync(p =>
    {
      p.LastEngine = engine.ToString();
      p.LastVoice = voice;
      p.LastPitch = pitch;
    });

    return new TTSEventSource(text, parameters ?? new(), audioStream, _logger);
  }

  private async Task<Stream> GenerateESpeakAsync(
    string text, string voice, float speed, float pitch, CancellationToken ct)
  {
    // Use espeak-ng command line or library
    // espeak-ng is available on Raspberry Pi OS
    var process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = "espeak-ng",
        Arguments = $"-v {voice} -s {(int)(175 * speed)} -p {(int)(50 * pitch)} --stdout",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
      }
    };
    
    process.Start();
    await process.StandardInput.WriteAsync(text);
    process.StandardInput.Close();
    
    var ms = new MemoryStream();
    await process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
    await process.WaitForExitAsync(ct);
    
    ms.Position = 0;
    return ms;
  }

  private async Task<Stream> GenerateGoogleTTSAsync(
    string text, string voice, float speed, float pitch, CancellationToken ct)
  {
    // Use Google Cloud Text-to-Speech API
    // Requires GoogleAPIKey from secrets
    var apiKey = await _configManager.GetValueAsync<string>("audio-secrets", "TTS:GoogleAPIKey");
    
    // Implementation using HttpClient to Google TTS API
    throw new NotImplementedException("Google TTS integration");
  }

  private async Task<Stream> GenerateAzureTTSAsync(
    string text, string voice, float speed, float pitch, CancellationToken ct)
  {
    // Use Azure Cognitive Services Speech
    // Requires AzureAPIKey from secrets
    var apiKey = await _configManager.GetValueAsync<string>("audio-secrets", "TTS:AzureAPIKey");
    
    // Implementation using Azure Speech SDK
    throw new NotImplementedException("Azure TTS integration");
  }
}
```

#### 5. src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs
```csharp
namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Audio file event source for notifications, doorbell, etc.
/// </summary
