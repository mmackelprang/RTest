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
| 3 - Audio Sources | ✅ Completed | Spotify, Radio, Vinyl, File Player |
| 4 - Event Sources | ✅ Completed | TTS (eSpeak/Google/Azure), Audio File Events |
| 5 - Ducking | ✅ Completed | Priority-based audio ducking with configurable fade policies |
| 6 - Outputs | ✅ Completed | Local audio, Chromecast (SharpCaster), HTTP streaming |
| 7 - Visualization | ✅ Completed | Spectrum analyzer (FFT), VU meters, waveform display |
| 8 - API | ✅ Completed | REST controllers, SignalR hub, audio streaming, Swagger |
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
│   │   ├── Interfaces/Audio/  # IAudioEngine, IAudioDeviceManager, IDuckingService, IVisualizerService, etc.
│   │   ├── Configuration/     # AudioEngineOptions, AudioOptions, TTSOptions, VisualizerOptions
│   │   └── Exceptions/        # AudioDeviceConflictException
│   ├── Radio.Infrastructure/ # Audio management, configuration, external integrations
│   │   ├── Audio/SoundFlow/   # SoundFlow audio engine implementation (Phase 2)
│   │   │   ├── SoundFlowAudioEngine.cs
│   │   │   ├── SoundFlowMasterMixer.cs
│   │   │   ├── SoundFlowDeviceManager.cs
│   │   │   └── TappedOutputStream.cs
│   │   ├── Audio/Sources/Events/  # Event audio sources (Phase 4)
│   │   │   ├── EventAudioSourceBase.cs
│   │   │   ├── TTSEventSource.cs
│   │   │   └── AudioFileEventSource.cs
│   │   ├── Audio/Services/    # Audio services (Phase 4, 5)
│   │   │   ├── TTSFactory.cs
│   │   │   ├── AudioFileEventSourceFactory.cs
│   │   │   └── DuckingService.cs    # Phase 5 - Ducking & Priority
│   │   ├── Audio/Visualization/  # Audio visualization (Phase 7)
│   │   │   ├── VisualizerService.cs  # Main visualizer service
│   │   │   ├── SpectrumAnalyzer.cs   # FFT-based spectrum analysis
│   │   │   ├── LevelMeter.cs         # Peak/RMS level metering
│   │   │   └── WaveformAnalyzer.cs   # Time-domain waveform buffer
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
│   │   └── Audio/           # Tests for audio engine, event sources, and visualization
│   └── Radio.API.Tests/
├── tools/
│   ├── Radio.Tools.AudioUAT/  # Audio UAT testing tool with Phase 2, 3, 4 tests
│   └── Radio.Tools.ConfigurationManager/  # Configuration management tool
├── design/                   # Design documents
└── scripts/                  # Deployment and utility scripts
```

## Configuration System

The configuration infrastructure (Phase 1) provides:

- **Dual backing stores**: JSON files and SQLite database
- **Secrets management**: Tag-based substitution (`${secret:identifier}`)
- **Encrypted storage**: Secrets encrypted at rest using Data Protection API
- **Backup/restore**: Full configuration backup and restore capabilities
- **Unified database paths**: Centralized path management for all SQLite databases
- **Unified backup system**: Single-operation backup of all databases
- **DI integration**: Easy registration via `AddManagedConfiguration()`

### Database Configuration

All SQLite databases (configuration, metrics, fingerprinting) can now be configured through a unified `Database` section:

```json
{
  "Database": {
    "RootPath": "./data",
    "ConfigurationSubdirectory": "config",
    "MetricsSubdirectory": "metrics",
    "FingerprintingSubdirectory": "fingerprints",
    "BackupSubdirectory": "backups",
    "BackupRetentionDays": 30
  }
}
```

This places all databases under a consistent directory structure:
- Configuration: `./data/config/configuration.db`
- Metrics: `./data/metrics/metrics.db`
- Fingerprinting: `./data/fingerprints/fingerprints.db`
- Backups: `./data/backups/`

**Note**: Legacy configuration paths are still supported for backward compatibility.

### Unified Database Backup

The unified backup system backs up all SQLite databases in a single operation:

```csharp
// Create backup of all databases
var backupService = serviceProvider.GetRequiredService<IUnifiedDatabaseBackupService>();
var backup = await backupService.CreateFullBackupAsync("Daily backup");

// Backup file: ./data/backups/unified_20231204_143022_a1b2c3.dbbackup
Console.WriteLine($"Created backup: {backup.BackupId}");
Console.WriteLine($"Included databases: {string.Join(", ", backup.IncludedDatabases)}");

// Restore from backup
await backupService.RestoreBackupAsync(backup.BackupId, overwrite: true);

// Automatic cleanup of old backups
var deleted = await backupService.CleanupOldBackupsAsync();
```

For detailed information, see [Database Configuration](design/DATABASE_CONFIGURATION.md).

### Configuration Usage Example

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

## Event Audio Sources (Phase 4)

The event audio system provides ephemeral audio sources for notifications, announcements, and chimes:

- **IEventAudioSource**: Interface for one-shot audio playback with auto-disposal
- **ITTSFactory**: Factory for creating TTS audio with multiple engine support
- **TTSEventSource**: Text-to-Speech audio from eSpeak, Google, or Azure engines
- **AudioFileEventSource**: Play notification sounds and audio file events

### TTS Engines Supported

| Engine | Type | Requirements |
|--------|------|--------------|
| eSpeak-ng | Offline | `espeak-ng` installed |
| Google Cloud TTS | Cloud | API key in secrets |
| Azure Speech | Cloud | API key and region in secrets |

### Usage Example

```csharp
// Register services
services.AddEventAudioSources(configuration);

// Create TTS audio
var ttsFactory = serviceProvider.GetRequiredService<ITTSFactory>();
var ttsSource = await ttsFactory.CreateAsync("Hello, this is a test announcement");
await ttsSource.PlayAsync();
// Auto-disposes when playback completes

// Create audio file event
var audioFactory = serviceProvider.GetRequiredService<AudioFileEventSourceFactory>();
var eventSource = await audioFactory.CreateFromFileAsync("notifications/doorbell.wav");
eventSource.PlaybackCompleted += (_, _) => Console.WriteLine("Doorbell played!");
await eventSource.PlayAsync();
```

## Ducking & Priority System (Phase 5)

The ducking system automatically reduces the volume of background audio when higher-priority event audio plays:

- **IDuckingService**: Service for managing audio ducking with configurable policies
- **Priority-based mixing**: Sources assigned priorities 1-10 (higher = more important)
- **Configurable fade policies**: FadeSmooth, FadeQuick, or Instant transitions
- **Nested event handling**: Proper volume restoration when multiple events overlap

### Ducking Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| DuckingPercentage | 20 | Volume % when ducked (20 = -14dB) |
| DuckingAttackMs | 100 | Fade-down time in milliseconds |
| DuckingReleaseMs | 500 | Fade-up time in milliseconds |
| DuckingPolicy | FadeSmooth | Transition type |

### Usage Example

```csharp
// Register services (included in AddSoundFlowAudio)
services.AddSoundFlowAudio(configuration);

// Get the ducking service
var duckingService = serviceProvider.GetRequiredService<IDuckingService>();

// Set priority for a source
duckingService.SetPriority(ttsSource, 9);  // High priority

// Start ducking when event plays
await duckingService.StartDuckingAsync(eventSource);

// Stop ducking when event completes
await duckingService.StopDuckingAsync(eventSource);

// Check ducking state
Console.WriteLine($"Is ducking: {duckingService.IsDucking}");
Console.WriteLine($"Duck level: {duckingService.CurrentDuckLevel}%");
```

## Audio Outputs (Phase 6)

The audio outputs system provides multi-device output support with local speakers and network streaming:

- **IAudioOutput**: Common interface for all audio output types
- **LocalAudioOutput**: Routes audio to local ALSA/default speakers with device selection
- **GoogleCastOutput**: Streams audio to Chromecast devices via SharpCaster
- **HttpStreamOutput**: HTTP server for streaming audio to web clients

### Supported Output Types

| Output Type | Description | Use Case |
|-------------|-------------|----------|
| Local | ALSA/default device output | Built-in speakers, USB DACs |
| GoogleCast | Chromecast streaming | Living room speakers, multi-room |
| HttpStream | HTTP audio server | Custom clients, Chromecast source |

### Configuration

```json
{
  "AudioOutput": {
    "Local": {
      "Enabled": true,
      "PreferredDeviceId": "",
      "DefaultVolume": 0.8
    },
    "GoogleCast": {
      "Enabled": false,
      "DiscoveryTimeoutSeconds": 10,
      "DefaultVolume": 0.7
    },
    "HttpStream": {
      "Enabled": true,
      "Port": 8080,
      "EndpointPath": "/stream/audio",
      "SampleRate": 48000,
      "Channels": 2
    }
  }
}
```

### Usage Example

```csharp
// Register services (included in AddSoundFlowAudio)
services.AddSoundFlowAudio(configuration);

// Get the local output
var localOutput = serviceProvider.GetRequiredService<LocalAudioOutput>();
await localOutput.InitializeAsync();
await localOutput.StartAsync();

// Get the Chromecast output
var castOutput = serviceProvider.GetRequiredService<GoogleCastOutput>();
await castOutput.InitializeAsync();
var devices = await castOutput.DiscoverDevicesAsync();
if (devices.Any())
{
  await castOutput.ConnectAsync(devices.First());
  castOutput.SetStreamUrl("http://192.168.1.50:8080/stream/audio");
  await castOutput.StartAsync();
}

// Get the HTTP stream output
var httpOutput = serviceProvider.GetRequiredService<HttpStreamOutput>();
await httpOutput.InitializeAsync();
await httpOutput.StartAsync();
Console.WriteLine($"Stream URL: {httpOutput.StreamUrl}");
```

## Audio Visualization (Phase 7)

The visualization system provides real-time audio analysis for UI displays:

- **IVisualizerService**: Unified service for all visualization types
- **SpectrumAnalyzer**: FFT-based frequency analysis for spectrum displays
- **LevelMeter**: Peak and RMS level metering for VU meters
- **WaveformAnalyzer**: Time-domain sample buffering for waveform displays

### Visualization Types

| Type | Description | Use Case |
|------|-------------|----------|
| Spectrum | FFT frequency bins (magnitude per frequency) | Spectrum analyzer display |
| Level | Peak/RMS measurements with decay | VU meters, level bars |
| Waveform | Time-domain sample buffer | Oscilloscope display |

### Configuration

```json
{
  "Visualizer": {
    "FFTSize": 2048,
    "WaveformSampleCount": 512,
    "PeakHoldTimeMs": 1000,
    "PeakDecayRate": 0.95,
    "RmsSmoothing": 0.3,
    "ApplyWindowFunction": true,
    "SpectrumSmoothing": 0.5
  }
}
```

### Usage Example

```csharp
// Register services (included in AddSoundFlowAudio)
services.AddSoundFlowAudio(configuration);

// Get the visualizer service
var visualizer = serviceProvider.GetRequiredService<IVisualizerService>();

// Process audio samples (called from audio callback)
visualizer.ProcessSamples(samples);

// Get spectrum data for display
var spectrum = visualizer.GetSpectrumData();
Console.WriteLine($"Spectrum bins: {spectrum.BinCount}");
Console.WriteLine($"Max frequency: {spectrum.MaxFrequency} Hz");
// spectrum.Magnitudes contains values 0.0-1.0 for each frequency bin

// Get level data for VU meters
var levels = visualizer.GetLevelData();
Console.WriteLine($"Left peak: {levels.LeftPeakDb:F1} dB");
Console.WriteLine($"Right peak: {levels.RightPeakDb:F1} dB");
Console.WriteLine($"Clipping: {levels.IsClipping}");

// Get waveform data for display
var waveform = visualizer.GetWaveformData();
Console.WriteLine($"Sample count: {waveform.SampleCount}");
Console.WriteLine($"Duration: {waveform.Duration.TotalMilliseconds:F0} ms");
// waveform.LeftSamples/RightSamples contain values -1.0 to 1.0

// Reset visualization when changing sources
visualizer.Reset();
```

### Data Models

**SpectrumData**: FFT frequency analysis results
- `Magnitudes`: Array of magnitude values (0.0-1.0) per frequency bin
- `Frequencies`: Array of frequency values (Hz) per bin
- `BinCount`: Number of frequency bins (FFTSize / 2)
- `FrequencyResolution`: Hz per bin (SampleRate / FFTSize)

**LevelData**: Audio level measurements
- `LeftPeak`/`RightPeak`: Peak levels (0.0-1.0)
- `LeftRms`/`RightRms`: RMS levels (0.0-1.0)
- `LeftPeakDb`/`RightPeakDb`: Peak levels in dBFS
- `IsClipping`: True if audio is at or near maximum level

**WaveformData**: Time-domain sample buffer
- `LeftSamples`/`RightSamples`: Sample arrays (-1.0 to 1.0)
- `SampleCount`: Number of samples per channel
- `Duration`: Time span represented by the buffer

## API & SignalR Integration (Phase 8)

The API layer provides REST endpoints and real-time communication for external clients:

- **REST Controllers**: Complete CRUD operations for audio, sources, devices, and configuration
- **SignalR Hub**: Real-time visualization data broadcasting at 30fps
- **Audio Streaming**: HTTP PCM audio stream for Chromecast and web clients
- **Swagger Documentation**: Interactive API documentation at `/swagger`

### REST API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/audio` | GET | Get current playback state |
| `/api/audio` | POST | Update playback (play/pause/stop/volume) |
| `/api/audio/volume/{value}` | POST | Set volume (0.0-1.0) |
| `/api/audio/mute` | POST | Toggle mute |
| `/api/sources` | GET | List available audio sources |
| `/api/sources/active` | GET | Get active sources |
| `/api/sources` | POST | Switch audio source |
| `/api/devices/output` | GET | List output devices |
| `/api/devices/input` | GET | List input devices |
| `/api/configuration` | GET | Get all settings |
| `/api/configuration/audio` | GET | Get audio settings |
| `/api/configuration/visualizer` | GET | Get visualizer settings |

### SignalR Hub

The `AudioVisualizationHub` at `/hubs/visualization` provides real-time audio data:

```javascript
// Connect to the hub
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/visualization")
    .build();

// Subscribe to visualization data
await connection.invoke("SubscribeToAll");

// Receive spectrum updates
connection.on("ReceiveSpectrum", (data) => {
    // data.magnitudes - array of frequency bin values
    // data.frequencies - array of frequency values in Hz
    // data.binCount - number of frequency bins
    updateSpectrumDisplay(data);
});

// Receive level updates
connection.on("ReceiveLevels", (data) => {
    // data.leftPeak, data.rightPeak - peak levels
    // data.leftRms, data.rightRms - RMS levels
    // data.isClipping - clipping indicator
    updateVUMeter(data);
});

// Receive waveform updates
connection.on("ReceiveWaveform", (data) => {
    // data.leftSamples, data.rightSamples - sample arrays
    // data.sampleCount - number of samples
    updateWaveformDisplay(data);
});
```

### Audio Stream Endpoint

The audio stream middleware provides PCM audio at `/stream/audio`:

- **Format**: 16-bit PCM, stereo, 48kHz
- **Content-Type**: `audio/L16;rate=48000;channels=2`
- **Use Case**: Chromecast streaming, web audio players

```javascript
// Connect to audio stream
const audio = new Audio('/stream/audio');
audio.play();
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
- [Database Configuration](design/DATABASE_CONFIGURATION.md) - Unified database paths and backup system
- [Web UI](design/WEBUI.md) - UI design specifications

## License

See [LICENSE](LICENSE) file for details.

