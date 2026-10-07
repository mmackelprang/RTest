# System Configuration Documentation

This document provides a comprehensive reference for all Configuration, Preferences, and Secrets used in the Radio Console application.

---

## Quick Start Guides

### For Development (Windows/Mac/Linux)

1. **Prerequisites**
   - .NET 10 SDK (pinned in `global.json`)
   - SQLite (optional, can use JSON files)
   - Git

2. **Initial Setup**
   ```bash
   git clone <repository-url>
   cd RadioConsole
   dotnet restore
   dotnet build
   ```

3. **Configuration**
   - Configuration is stored in `src/Radio.API/appsettings.json`
   - For development, create `appsettings.Development.json` to override settings
   - Secrets are stored encrypted - see [Secrets Setup](#secrets-setup) below

4. **Running the Application**
   ```bash
   # Run API (default: http://localhost:5000)
   dotnet run --project src/Radio.API

   # Run Web UI (default: http://localhost:5002)
   dotnet run --project src/Radio.Web
   ```

### For Production

Production runs on an Intel N100 (`x86_64`) Ubuntu box as two systemd services, `radio-api` (port 5000) and
`radio-web` (port 5002), installed under `/opt/radio-console` and deployed with `deploy/Deploy-ToLinux.ps1`.
See [`deployment.md`](deployment.md) for the overview and [`deploy/DEPLOYMENT.md`](../deploy/DEPLOYMENT.md)
for the step-by-step guide. Per-machine settings go in `appsettings.Production.json`.

---

## Configuration File Location

**Primary Configuration File:** `src/Radio.API/appsettings.json`

The Radio Console application uses a **consolidated configuration approach** where all application settings are defined in a single `appsettings.json` file located in the Radio.API project. This design choice provides several benefits:

- **Single Source of Truth**: All configuration sections (Database, ManagedConfiguration, Metrics, Fingerprinting, AudioEngine, Serilog) are in one location
- **Simplified Deployment**: Only one configuration file needs to be managed and deployed
- **Easier Maintenance**: Configuration changes are made in a single, well-organized file
- **Environment Overrides**: Standard ASP.NET Core configuration layering allows for `appsettings.Development.json`, `appsettings.Production.json`, and environment variables to override settings as needed

**Reference Example:** See `docs/appsettings.example.json` for a complete template with all available configuration options.

---

## Secrets Setup

Secrets are stored encrypted in the configuration store and referenced from configuration with a tag of the
form `${secret:identifier}` (`src/Radio.Configuration/Models/SecretTag.cs`). A value that still contains an
unresolved tag is treated as "not configured".

### Secrets in use

Only the cloud text-to-speech engines need secrets. Everything else (SongRec fingerprinting, MusicBrainz,
Cover Art Archive, NWS weather) uses no API key.

| Secret | Setting | Needed for |
|---|---|---|
| `tts_google_api_key` | `TTS:GoogleAPIKey` | Google Cloud Text-to-Speech (optional) |
| `tts_azure_api_key` | `TTS:AzureAPIKey` | Azure Speech (optional) |

`TTS:AzureRegion` (default `eastus`) is a plain setting, not a secret. With neither key configured, the
cloud TTS engines are unavailable and announcements use the local engine.

### How to configure secrets

#### Method 1: Configuration Manager tool

```bash
cd tools/Radio.Tools.ConfigurationManager
dotnet run
# Choose "Manage Secrets", then create the secret with the identifier from the table above.
```

#### Method 2: Configuration file

`src/Radio.API/appsettings.json` already references the tags:

```json
{
  "TTS": {
    "GoogleAPIKey": "${secret:tts_google_api_key}",
    "AzureAPIKey": "${secret:tts_azure_api_key}",
    "AzureRegion": "eastus"
  }
}
```

Store the secret values in the active configuration store (SQLite in production). Put per-machine overrides
in `appsettings.Production.json`, never in `appsettings.json`, which a deploy overwrites.

#### Method 3: Environment variables

Standard ASP.NET Core overrides work, for example `TTS__GoogleAPIKey=...` and `TTS__AzureAPIKey=...` in the
`radio-api` service environment.

### Getting API keys

#### Google Cloud TTS

1. In the [Google Cloud Console](https://console.cloud.google.com/), enable the Cloud Text-to-Speech API.
2. Create an API key and store it as `tts_google_api_key`.

#### Azure Speech

1. In the [Azure Portal](https://portal.azure.com/), create a Speech resource.
2. Store its key as `tts_azure_api_key` and set `TTS:AzureRegion` to its region.

---

## SQLite Setup for Production

### Why SQLite for Production?

- **Performance**: Faster than JSON files for frequent reads/writes
- **Reliability**: ACID compliance, better concurrency handling
- **Backup**: Single file backup for entire configuration
- **Querying**: Easier to query and manage data

### Configuration for SQLite

In `appsettings.json`, set:

```json
{
  "ManagedConfiguration": {
    "DefaultStoreType": "Sqlite",
    "BasePath": "/opt/radio-console/data/config",
    "SqliteFileName": "configuration.db",
    "BackupPath": "/opt/radio-console/data/backups",
    "BackupRetentionDays": 30
  },
  "Database": {
    "RootPath": "/opt/radio-console/data",
    "ConfigurationSubdirectory": "config",
    "MetricsSubdirectory": "metrics",
    "FingerprintingSubdirectory": "fingerprints",
    "BackupSubdirectory": "backups"
  }
}
```

### Database Files

The following SQLite databases are created:

- `/opt/radio-console/data/config/configuration.db` - App configuration and secrets
- `/opt/radio-console/data/metrics/metrics.db` - Performance metrics
- `/opt/radio-console/data/fingerprints/fingerprints.db` - Audio fingerprint cache

### Backup and Restore

#### Automatic Backups

Backups are created automatically and stored in the backups directory. Old backups are automatically cleaned up based on `BackupRetentionDays`.

#### Manual Backup

```bash
# Using the configuration manager tool
cd tools/Radio.Tools.ConfigurationManager
dotnet run
# Select "Backup Configuration"

# Or copy database files directly
cp /opt/radio-console/data/config/configuration.db \
   /opt/radio-console/data/backups/configuration-$(date +%Y%m%d).db
```

#### Restore from Backup

```bash
# Stop the service
sudo systemctl stop radio-console

# Restore database
cp /opt/radio-console/data/backups/configuration-20231205.db \
   /opt/radio-console/data/config/configuration.db

# Start the service
sudo systemctl start radio-console
```

---

## Text-to-Speech (TTS) Setup

Two engines are supported, **Google Cloud TTS** and **Azure Speech**. Both are cloud services: each needs an API key and network access to reach it.

`TTS:DefaultEngine` and `TTS:DefaultVoice` have **no built-in default** - they are empty unless you configure them. An unset or unrecognised engine or voice makes TTS generation fail with an explicit error naming the valid engines, rather than silently picking one. The shipped `src/Radio.API/appsettings.json` sets them to `Google` and `en-US-Standard-A`, which is where the working values on a normal install come from.

**Note (2026-09-03, `TTS-9`):** the offline eSpeak-NG engine was removed, along with its `espeak-ng` apt prerequisite and its `ESpeakPath` setting. It had interpolated a caller-supplied voice identifier into an `espeak-ng` command line reachable from `POST /api/sources/events/tts` (`SEC-4`); with `espeak-ng -w <path>` that was an arbitrary file write as the account owning `/opt/radio-console`, so the engine was deleted rather than sanitised. eSpeak-NG was the only engine that worked without network, so **there is now no TTS at all when the network is down.** That is an accepted trade-off (decision `D26`): announcements are triggered by smart-home events, and if the network is down those events are not arriving either.

### Google Cloud TTS (Cloud, High Quality)

**Prerequisites:**
1. Google Cloud account with billing enabled
2. Text-to-Speech API enabled
3. API key created (see [Getting API Keys](#getting-api-keys))

**Configuration:**
```json
{
  "TTS": {
    "DefaultEngine": "Google",
    "GoogleAPIKey": "${secret:google_tts_key}",
    "DefaultVoice": "en-US-Standard-A",
    "DefaultSpeed": 1.0,
    "DefaultPitch": 1.0
  }
}
```

**Create Secret:**
```bash
cd tools/Radio.Tools.ConfigurationManager
dotnet run
# Create secret: google_tts_key = <your-api-key>
```

### Azure Speech (Cloud, High Quality)

**Prerequisites:**
1. Azure account with active subscription
2. Speech Service resource created
3. API key and region noted (see [Getting API Keys](#getting-api-keys))

**Configuration:**
```json
{
  "TTS": {
    "DefaultEngine": "Azure",
    "AzureAPIKey": "${secret:azure_tts_key}",
    "AzureRegion": "${secret:azure_tts_region}",
    "DefaultVoice": "en-US-JennyNeural",
    "DefaultSpeed": 1.0,
    "DefaultPitch": 1.0
  }
}
```

**Create Secrets:**
```bash
cd tools/Radio.Tools.ConfigurationManager
dotnet run
# Create secret: azure_tts_key = <your-api-key>
# Create secret: azure_tts_region = <your-region> (e.g., "eastus")
```

---

## Fingerprinting Setup

Song identification uses SongRec (a Shazam-compatible recognizer) for every source, then MusicBrainz and the
Cover Art Archive for metadata and album art. No API key is required.

### Prerequisites

1. `songrec` installed on the host (`sudo add-apt-repository ppa:marin-m/songrec && sudo apt install songrec`)
2. Internet connection for lookups

### Configuration

The keys live under `Fingerprinting` in `src/Radio.API/appsettings.json`; `Fingerprinting:SongRec:SongRecPath`
names the binary when it is not on `PATH`, and `Fingerprinting:MusicBrainz:ContactEmail` identifies the
application to MusicBrainz. See [Fingerprinting](#fingerprinting) below for the full option list.

### Rate Limiting

- **MusicBrainz**: 1 request/second (anonymous limit)

The system automatically respects this limit.

---

## Systemd Service Setup

The unit files are tracked in `deploy/common/` (`radio-api.service`, `radio-web.service`) and installed by the
setup scripts. See [`deployment.md`](deployment.md) and [`deploy/DEPLOYMENT.md`](../deploy/DEPLOYMENT.md) §
Service Management rather than writing units by hand.

---

## Table of Contents

- [Configuration Options](#configuration-options)
- [Preferences](#preferences)
- [Secrets](#secrets)
- [Configuration Files](#configuration-files)
- [Enumerations](#enumerations)

---

## Configuration Options

Configuration options are static settings that define application behavior. They are typically loaded at startup and bound via the `IOptions<T>` pattern.

### ManagedConfiguration

**Section Name:** `ManagedConfiguration`  
**Source File:** `src/Radio.Infrastructure/Configuration/Models/ConfigurationOptions.cs`  
**Description:** Configuration options for the managed configuration system itself.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultStoreType` | `ConfigurationStoreType` | `Json` | Default backing store type (`Json` or `Sqlite`) |
| `BasePath` | `string` | `./config` | Base path for configuration files |
| `JsonExtension` | `string` | `.json` | File extension for JSON configuration files |
| `SqliteFileName` | `string` | `configuration.db` | SQLite database filename |
| `SecretsFileName` | `string` | `secrets` | Secrets storage filename (extension added based on store type) |
| `BackupPath` | `string` | `./config/backups` | Path for backup files |
| `AutoSave` | `bool` | `true` | Whether to auto-save changes |
| `BackupRetentionDays` | `int` | `30` | Number of days to retain backups |
| `AutoSaveDebounceMs` | `int` | `5000` | Debounce delay for auto-save in milliseconds |

---

### Metrics

**Section Name:** `Metrics`  
**Source File:** `src/Radio.Core/Configuration/MetricsOptions.cs`  
**Description:** Configuration options for the metrics collection system.

**Note:** Metrics data is now stored in the configuration database (`configuration.db`) rather than a separate metrics database. This consolidates storage and reduces the number of database files.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable or disable metrics collection |
| `FlushIntervalSeconds` | `int` | `60` | Interval in seconds for flushing buffered metrics to disk |
| `RetentionMinuteData` | `int` | `120` | Minutes to retain minute-resolution data (2 hours default) |
| `RetentionHourData` | `int` | `48` | Hours to retain hour-resolution data (48 hours default) |
| `RetentionDayData` | `int` | `365` | Days to retain day-resolution data (1 year default) |
| `RollupIntervalMinutes` | `int` | `60` | Interval in minutes for running rollup/pruning operations |

---

### AudioEngine

**Section Name:** `AudioEngine`
**Source File:** `src/Radio.Core/Configuration/AudioEngineOptions.cs`
**Description:** Configuration options for the SoundFlow audio engine.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SampleRate` | `int` | `48000` | Sample rate in Hz |
| `Channels` | `int` | `2` | Number of audio channels (stereo) |
| `BufferSize` | `int` | `1024` | Buffer size in samples |
| `HotPlugIntervalSeconds` | `int` | `5` | Hot-plug detection interval in seconds |
| `OutputBufferSizeSeconds` | `int` | `5` | Ring buffer size for output stream in seconds |
| `EnableHotPlugDetection` | `bool` | `true` | Whether hot-plug detection is enabled |

---

### Database

**Section Name:** `Database`
**Source File:** `src/Radio.Core/Configuration/DatabaseOptions.cs`
**Description:** Unified configuration for all SQLite database locations. Provides root path and per-database subdirectory/filename settings, plus helper methods to resolve full paths.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RootPath` | `string` | `./data` | Root directory for all database files |
| `ConfigurationSubdirectory` | `string` | `config` | Subdirectory for configuration database (→ `./data/config/`) |
| `ConfigurationFileName` | `string` | `configuration.db` | Configuration database filename |
| `FingerprintingSubdirectory` | `string` | `fingerprints` | Subdirectory for fingerprinting database (→ `./data/fingerprints/`) |
| `FingerprintingFileName` | `string` | `fingerprints.db` | Fingerprinting database filename |
| `SecretsSubdirectory` | `string` | `secrets` | Subdirectory for secrets database (→ `./data/secrets/`) |
| `SecretsFileName` | `string` | `secrets.db` | Secrets database filename |
| `BackupSubdirectory` | `string` | `backups` | Subdirectory for database backups (→ `./data/backups/`) |
| `BackupRetentionDays` | `int` | `30` | Number of days to retain database backups |

**Helper Methods:**
- `GetConfigurationDatabasePath()` → `{RootPath}/{ConfigurationSubdirectory}/{ConfigurationFileName}`
- `GetFingerprintingDatabasePath()` → `{RootPath}/{FingerprintingSubdirectory}/{FingerprintingFileName}`
- `GetSecretsDatabasePath()` → `{RootPath}/{SecretsSubdirectory}/{SecretsFileName}`
- `GetBackupPath()` → `{RootPath}/{BackupSubdirectory}`
- `GetAllDatabasePaths()` → returns all three database paths

**Note:** Metrics data is stored in the configuration database (`configuration.db`), not a separate file.

---

### Bluetooth

**Section Name:** `Bluetooth`
**Source File:** `src/Radio.Core/Configuration/BluetoothOptions.cs`
**Description:** Configuration options for Bluetooth audio input (A2DP sink).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DeviceName` | `string` | `Radio Console` | Device name advertised to Bluetooth clients |
| `AutoAcceptConnections` | `bool` | `true` | Automatically accept incoming connection requests |
| `RequirePairing` | `bool` | `false` | Require pairing before connecting |
| `Enabled` | `bool` | `true` | Master enable/disable switch for Bluetooth |
| `EnableOnStartup` | `bool` | `true` | Enable Bluetooth on application startup |
| `AutoSwitchOnConnect` | `bool` | `true` | Automatically switch to Bluetooth source when a device connects |
| `AudioQuality` | `BluetoothAudioQuality` | `High` | Audio quality setting (`Standard` = 44.1kHz, `High` = 48kHz) |
| `EnableA2dpSink` | `bool` | `true` | Enable A2DP sink to receive audio from phone (Windows: requires 10 2004+ and MSIX identity) |
| `EnableMediaSessionMonitoring` | `bool` | `true` | Enable SMTC monitoring for AVRCP-equivalent metadata (Windows only) |
| `EnableLoopbackCapture` | `bool` | `true` | Enable WASAPI loopback capture to route BT audio through SoundFlow pipeline (Windows only). When enabled, BT audio goes through Cast, visualization, modifiers. When false, platform manages audio directly. |

**Example:**
```json
{
  "Bluetooth": {
    "DeviceName": "Grandpa's Radio",
    "AutoAcceptConnections": true,
    "RequirePairing": false,
    "Enabled": true,
    "EnableOnStartup": true,
    "AutoSwitchOnConnect": true,
    "AudioQuality": "High",
    "EnableA2dpSink": true,
    "EnableMediaSessionMonitoring": true,
    "EnableLoopbackCapture": true
  }
}
```

---

### Audio

**Section Name:** `Audio`  
**Source File:** `src/Radio.Core/Configuration/AudioOptions.cs`  
**Description:** Configuration options for the audio system including ducking behavior.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultSource` | `string` | `FilePlayer` | Default primary audio source name |
| `DuckingPercentage` | `int` | `20` | Volume percentage when primary source is ducked (0-100) |
| `DuckingPolicy` | `DuckingPolicy` | `FadeSmooth` | Ducking transition policy |
| `DuckingAttackMs` | `int` | `100` | Ducking attack time in milliseconds |
| `DuckingReleaseMs` | `int` | `500` | Ducking release time in milliseconds |

---

### Serilog Logging

**Section Name:** `Serilog`  
**Source File:** `src/Radio.API/appsettings.json`  
**Description:** Configuration for application logging using Serilog with Console and File sinks.

#### Overview

The Radio Console API uses Serilog for structured logging with two configured sinks:
- **Console Sink**: Outputs logs to the console for real-time monitoring
- **File Sink**: Persists logs to disk for diagnostics and historical analysis

**Note:** The `appsettings.json` file contains all application configuration sections (Database, ManagedConfiguration, Metrics, Fingerprinting, AudioEngine, and Serilog) in a single consolidated file for easier management and deployment. This eliminates the need for multiple configuration files and simplifies the deployment process.

#### Configuration Structure

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.File", "Serilog.Sinks.Console" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "./logs/radio-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext" ]
  }
}
```

#### File Sink Configuration

| Property | Value | Description |
|----------|-------|-------------|
| `path` | `./logs/radio-.txt` | Log file path pattern. Date will be inserted before `.txt` (e.g., `radio-20231205.txt`) |
| `rollingInterval` | `Day` | Logs are rotated daily |
| `retainedFileCountLimit` | `7` | Keep logs for the last 7 days |
| `outputTemplate` | Custom format | Structured format for parsing by the System Log API |

#### Output Template Format

The output template is specifically designed to be parseable by the System Log API:

```
{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}
```

Example log entry:
```
2023-12-05 13:26:45.123 +00:00 [INF] [Radio.API.Controllers.SystemController] Log retrieval requested with level=info, limit=100
```

#### Log Levels

- **Verbose**: Detailed diagnostic information (not typically used in production)
- **Debug**: Internal system events useful for debugging
- **Information** (Default): General informational messages about application flow
- **Warning**: Potentially harmful situations that don't prevent operation
- **Error**: Error events that might still allow the application to continue
- **Fatal**: Very severe error events that lead to application termination

#### Log File Location

Log files are stored in the `./logs` directory relative to the application's working directory:
- **Development**: `<project-root>/logs/`
- **Production (Linux/Pi)**: Ensure the application user has write permissions to the logs directory

**Important for Linux/Pi deployments:**
- Create the logs directory with appropriate permissions: `mkdir -p ./logs && chmod 755 ./logs`
- Ensure the user running the application has write access
- Consider log rotation and disk space monitoring

#### System Log API

The REST API provides a `/api/system/logs` endpoint to retrieve and filter logs programmatically.

**Endpoint:** `GET /api/system/logs`

**Query Parameters:**
- `level` (string, default: "warning"): Minimum log level to return (info, warning, error)
- `limit` (int, default: 100): Maximum number of log entries to return (1-10000)
- `maxAgeMinutes` (int, optional): Only return logs within this many minutes from now

**Example Requests:**
```bash
# Get recent warnings and errors
GET /api/system/logs?level=warning&limit=50

# Get all info-level logs from the last hour
GET /api/system/logs?level=info&limit=200&maxAgeMinutes=60

# Get only errors
GET /api/system/logs?level=error&limit=100
```

**Response Format:**
```json
{
  "logs": [
    {
      "timestamp": "2023-12-05T13:26:45.123Z",
      "level": "INF",
      "message": "Log retrieval requested...",
      "exception": null,
      "sourceContext": "Radio.API.Controllers.SystemController"
    }
  ],
  "totalCount": 1,
  "filters": {
    "level": "info",
    "limit": 100,
    "maxAgeMinutes": null
  }
}
```

#### Behavioral Changes from File Sink

When the file sink is configured:
- **Durability**: Logs persist across application restarts and crashes
- **Daily Rotation**: New log file created each day (e.g., `radio-20231205.txt`, `radio-20231206.txt`)
- **Retention**: Logs older than 7 days are automatically deleted
- **Disk Usage**: Monitor disk space; each day's logs can vary based on activity
- **Performance**: Minimal impact; file writes are buffered and asynchronous

#### Best Practices

1. **Log Level**: Use `Information` level in production to balance detail with volume
2. **Disk Space**: Monitor the `./logs` directory on resource-constrained devices (e.g., Raspberry Pi)
3. **Permissions**: Verify write permissions before deployment
4. **Diagnostics**: Use the `/api/system/logs` endpoint for remote diagnostics instead of SSH access
5. **Avoid Duplicates**: Configuration is defined in `appsettings.json` only; do not add additional sinks in code

---

### Devices

**Section Name:** `Devices`  
**Source File:** `src/Radio.Core/Configuration/DeviceOptions.cs`  
**Description:** Configuration options for audio device settings.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Vinyl.USBPort` | `string` | `/dev/ttyUSB1` | USB port path for the vinyl turntable device |
| `Cast.DefaultDevice` | `string` | `""` | Default Chromecast device name |

A `Devices:Radio` entry (the RF320 USB radio's port, removed by `AUD-16`) may still exist in a
deployed `appsettings.Production.json` or config store. It is still loaded into configuration (and
`GET /api/configuration/devices` still returns it), but it matches no property, so it is ignored.

---

### FilePlayer

**Section Name:** `FilePlayer`  
**Source File:** `src/Radio.Core/Configuration/FilePlayerOptions.cs`  
**Description:** Configuration options for the file player audio source.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RootDirectory` | `string` | `media/audio` | Root directory for audio files (relative to RootDir) |
| `SupportedExtensions` | `string[]` | `.mp3, .flac, .wav, .ogg, .aac, .m4a, .wma` | Supported audio file extensions |

---

### TTS

**Section Name:** `TTS`  
**Source File:** `src/Radio.Core/Configuration/TTSOptions.cs`  
**Description:** Configuration options for the Text-to-Speech system.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultEngine` | `string` | *(empty)* | Default TTS engine to use: `Google` or `Azure`. Empty or unrecognised makes TTS generation fail with an explicit error rather than picking an engine. |
| `DefaultVoice` | `string` | *(empty)* | Default voice identifier, in the selected engine's own format (e.g. `en-US-Standard-A`). Empty makes TTS generation fail with an explicit error. |
| `DefaultPitch` | `float` | `1.0` | Default pitch (0.5 to 2.0, 1.0 = normal) |
| `DefaultSpeed` | `float` | `1.0` | Default speaking speed (0.5 to 2.0, 1.0 = normal) |
| `GenerationTimeoutSeconds` | `int` | `30` | Timeout in seconds for TTS generation |

---

### Visualizer

**Section Name:** `Visualizer`  
**Source File:** `src/Radio.Core/Configuration/VisualizerOptions.cs`  
**Description:** Configuration options for the audio visualizer service.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FFTSize` | `int` | `2048` | FFT size for spectrum analysis. Must be a power of 2 (e.g., 256, 512, 1024, 2048, 4096). Larger values provide better frequency resolution but slower updates. |
| `WaveformSampleCount` | `int` | `512` | Number of waveform samples to keep in the buffer |
| `PeakHoldTimeMs` | `int` | `1000` | Peak hold time in milliseconds for level metering. Peaks will be held at their maximum value for this duration before decaying. |
| `PeakDecayRate` | `float` | `0.95` | Peak decay rate per second (0.0 to 1.0). Higher values cause faster decay after peak hold expires. |
| `RmsSmoothing` | `float` | `0.3` | RMS smoothing factor (0.0 to 1.0). Higher values provide smoother, more stable RMS readings. |
| `ApplyWindowFunction` | `bool` | `true` | Whether to apply windowing to FFT input (Hann window) |
| `MinFrequency` | `float` | `20` | Minimum frequency to display in spectrum analysis (Hz) |
| `MaxFrequency` | `float` | `20000` | Maximum frequency to display in spectrum analysis (Hz) |
| `SpectrumSmoothing` | `float` | `0.5` | Spectrum smoothing factor (0.0 to 1.0). Higher values provide smoother spectrum display. |
| `SpectrumFloorDbfs` | `float` | `-65` | Tilted level (dBFS) that the spectrum/ring draw as an empty bar. Applies live, no restart. |
| `SpectrumCeilingDbfs` | `float` | `-25` | Tilted level (dBFS) that draws as a full bar. Must be above the floor, else both revert to defaults. Narrower window = more motion and colour. |
| `SpectrumCurve` | `float` | `1.5` | Exponent on bar height after the dB window. >1 adds contrast (shortens quieter bars); 1 = plain dB. Must be > 0. |
| `SpectrumTiltDbPerOctave` | `float` | `3` | Gain per octave about 1 kHz compensating music's fall-off with frequency (keeps bass from dominating). 0 disables. |

`MinFrequency`/`MaxFrequency` are not used by the spectrum/ring bands, which span a fixed 40 Hz – 16 kHz.

The four `Spectrum*` values are tuned through the config store (e.g. `POST /api/configuration/Visualizer`)
and apply on the next frame; the Settings UI does not expose them. `POST /api/configuration/Visualizer`
rejects a value that is not a finite number (400). If one arrives by another route, the spectrum stream
falls back to its default scale (one Warning logged) instead of stopping, but radio-api binds these options
at start-up, so a non-numeric value would still stop it starting — fix such a value before restarting.
Out-of-range combinations (ceiling not above floor, curve ≤ 0) are accepted and fall back to defaults.

---

### Fingerprinting

**Section Name:** `Fingerprinting`
**Source File:** `src/Radio.Fingerprinting/FingerprintingOptions.cs`
**Description:** Configuration options for the audio fingerprinting system, including the SongRec (Shazam)
call policy. Every call-policy interval is measured start-to-start (from the start of one attempt's capture
to the start of the next), and every value is read live, so a config-store change applies to the next
scheduling decision without a restart. Values below a setting's minimum are clamped up to it. How the
policy behaves: [fingerprinting.md § Call policy](fingerprinting.md#call-policy).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable or disable automatic fingerprinting |
| `SampleDurationSeconds` | `int` | `13` | Duration of audio to capture for each attempt (seconds). With ~2 s for the SongRec call this fits the 15 s unknown-start interval. ⚠ The appliance's SQLite store holds `fingerprinting:sampleDurationSeconds = 15`, which outranks this default until it is changed there. |
| `KnownStartFirstCallDelaySeconds` | `int` | `5` | File player / Bluetooth: seconds after the track starts before its first attempt (only while title, artist or album art is missing). Min 0. |
| `KnownStartFirstRetryDelaySeconds` | `int` | `30` | File player / Bluetooth: seconds from the first attempt to the retry after it found no match. Min 1. |
| `KnownStartRetryIntervalSeconds` | `int` | `60` | File player / Bluetooth: seconds between later retries after no-matches. Min 1. |
| `KnownStartValidationIntervalSeconds` | `int` | `60` | File player / Bluetooth: once SongRec has matched the track, seconds between validation calls until the track changes. Min 1. |
| `UnknownStartIntervalSeconds` | `int` | `15` | Radio / vinyl / USB / other: seconds between attempts, matched or not (~240/hour). A re-tune, source switch or return from silence makes the next attempt immediate and restarts the schedule from it (no extra calls on top). Min 1. |
| `MaxCallsPerHour` | `int` | `240` | Hard cap on SongRec calls in any rolling 60 minutes, across all sources. One Warning per exhaustion episode. Min 1. |
| `ErrorBackoffInitialSeconds` | `int` | `30` | Back-off after a SongRec failure (timeout, non-zero exit, unparsable output), doubling per consecutive failure. No SongRec process runs while backing off. Reset by the next clean call. Min 1. |
| `ErrorBackoffMaxSeconds` | `int` | `600` | Ceiling for the failure back-off. Never below `ErrorBackoffInitialSeconds`. |
| `ErrorWarnThreshold` | `int` | `5` | Consecutive SongRec failures after which one Warning is logged (possible Shazam throttling or ban). Min 1. |
| `IdlePollIntervalMs` | `int` | `1000` | Longest the identification loop waits before re-reading the active source when it is not capturing (min 100). |
| `UseShazamForAllSources` | `bool` | `false` | **Not read since the call policy.** Kept (not renamed) because the appliance's config store and `appsettings.Production.json` set it (AUD-1). Bluetooth album art no longer depends on it: missing art now counts as missing metadata. |
| `MinimumConfidenceThreshold` | `double` | `0.5` | Minimum confidence threshold for accepting a match (0.0 to 1.0) |
| `DuplicateSuppressionMinutes` | `int` | `5` | Minutes to suppress duplicate identifications of the same track |
| `HighConfidenceDuplicateSuppressionMinutes` | `int` | `30` | Minutes to suppress duplicates for high-confidence matches (score > 0.9) |
| `MinimumSecondsBetweenSongChanges` | `int` | `20` | Minimum seconds between song change events. Prevents rapid-fire entry creation from noisy fingerprints at song boundaries. |
| `DatabasePath` | `string` | `./data/fingerprints.db` | SQLite database path for fingerprint cache |
| `MusicBrainz.BaseUrl` | `string` | `https://musicbrainz.org/ws/2` | MusicBrainz API base URL |
| `MusicBrainz.ApplicationName` | `string` | `RadioConsole` | Application name for User-Agent header |
| `MusicBrainz.ApplicationVersion` | `string` | `1.0.0` | Application version for User-Agent header |
| `MusicBrainz.ContactEmail` | `string` | `""` | Contact email for User-Agent header |
| `MusicBrainz.MaxRequestsPerSecond` | `int` | `1` | Maximum requests per second (MusicBrainz limit is 1 for anonymous) |
| `MusicBrainz.TimeoutSeconds` | `int` | `10` | Request timeout in seconds |

---

### AudioOutput

**Section Name:** `AudioOutput`  
**Source File:** `src/Radio.Core/Configuration/AudioOutputOptions.cs`  
**Description:** Configuration options for audio outputs.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Local.Enabled` | `bool` | `true` | Whether the local output is enabled by default |
| `Local.PreferredDeviceId` | `string` | `""` | Preferred device ID for local output. If empty, uses the system default device. |
| `Local.DefaultVolume` | `float` | `0.8` | Default volume level (0.0 to 1.0) |
| `GoogleCast.Enabled` | `bool` | `false` | Whether Google Cast output is enabled |
| `GoogleCast.DiscoveryTimeoutSeconds` | `int` | `10` | Discovery timeout in seconds |
| `GoogleCast.PreferredDeviceName` | `string` | `""` | Preferred cast device name. If empty, uses the first discovered device. |
| `GoogleCast.DefaultVolume` | `float` | `0.7` | Default volume level for cast (0.0 to 1.0) |
| `GoogleCast.AutoReconnect` | `bool` | `true` | Whether to automatically reconnect on disconnect |
| `GoogleCast.ReconnectDelaySeconds` | `int` | `5` | Reconnect delay in seconds |
| `HttpStream.Enabled` | `bool` | `true` | Whether the HTTP stream output is enabled |
| `HttpStream.Port` | `int` | `8080` | HTTP stream server port |
| `HttpStream.EndpointPath` | `string` | `/stream/audio` | Stream endpoint path |
| `HttpStream.ContentType` | `string` | `audio/wav` | Audio format for the stream |
| `HttpStream.SampleRate` | `int` | `48000` | Sample rate for the stream |
| `HttpStream.Channels` | `int` | `2` | Number of channels for the stream |
| `HttpStream.BitsPerSample` | `int` | `16` | Bits per sample for the stream |
| `HttpStream.MaxConcurrentClients` | `int` | `10` | Maximum number of concurrent clients |
| `HttpStream.ClientBufferSize` | `int` | `65536` | Buffer size in bytes for each client |

---

### Radio

**Section Name:** `Radio`  
**Source File:** `src/Radio.Core/Configuration/RadioOptions.cs`  
**Description:** Configuration options for radio functionality including default frequencies, scan settings, and device parameters.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultDevice` | `string` | `RTLSDRCore` | Default radio device type. `RTLSDRCore` is the only supported value |
| `DefaultFMFrequencyMHz` | `double` | `101.5` | Default FM frequency in MHz |
| `DefaultAMFrequencyKHz` | `double` | `1000.0` | Default AM frequency in kHz |
| `DefaultFMStepMHz` | `double` | `0.1` | Default FM frequency step in MHz (typical: 0.1 or 0.2) |
| `DefaultAMStepKHz` | `double` | `10.0` | Default AM frequency step in kHz (typical: 9 or 10) |
| `MinFMFrequencyMHz` | `double` | `87.5` | Minimum FM frequency in MHz |
| `MaxFMFrequencyMHz` | `double` | `108.0` | Maximum FM frequency in MHz |
| `MinAMFrequencyKHz` | `double` | `520.0` | Minimum AM frequency in kHz |
| `MaxAMFrequencyKHz` | `double` | `1710.0` | Maximum AM frequency in kHz |
| `ScanStopThreshold` | `int` | `50` | Signal strength threshold for scan stop (0-100) |
| `ScanStepDelayMs` | `int` | `100` | Time to wait between frequency steps during scanning (milliseconds) |
| `DefaultDeviceVolume` | `int` | `50` | Default device volume (0-100) |

---

### SystemManagement

**Section Name:** `SystemManagement`  
**Source File:** N/A (Not yet implemented as a formal options class)  
**Description:** Configuration options for system management and logging.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Logs.DefaultLevel` | `string` | `warning` | Default log level filter for `/api/system/logs` endpoint |
| `Logs.DefaultLimit` | `int` | `100` | Default maximum number of log entries to return |
| `Logs.MaxLimit` | `int` | `10000` | Maximum allowed limit for log retrieval |
| `Logs.FilePath` | `string` | `logs/radio-.txt` | Path template for Serilog file sink logs |
| `Stats.CpuSampleDurationMs` | `int` | `100` | Duration to sample CPU usage in milliseconds |
| `Stats.TemperaturePath` | `string` | `/sys/class/thermal/thermal_zone0/temp` | Linux path to CPU temperature sensor |

**Notes:**
- Log retrieval from `/api/system/logs` requires Serilog file sink to be configured
- The temperature path is only applicable on Linux systems (Raspberry Pi)
- Log file path supports Serilog rolling file syntax with date placeholders

**Example Serilog Configuration:**

```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/radio-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

---

## Preferences

Preferences are user-modifiable settings that are persisted and auto-saved on change.

### AudioPreferences

**Section Name:** `AudioPreferences`
**Source File:** `src/Radio.Core/Configuration/AudioPreferences.cs`
**Description:** User preferences for audio playback.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CurrentSource` | `string` | `Radio` | Currently selected audio source |
| `CurrentOutput` | `string` | `""` | Currently selected audio output device ID. Empty = system default. |
| `MasterVolume` | `int` | `75` | Master volume level (0-100) |
| `IsMuted` | `bool` | `false` | Whether audio is muted |
| `Balance` | `int` | `0` | Audio balance (-100 to 100, where 0 is center) |
| `CurrentInput` | `string` | `""` | Currently selected audio input device ID (for recording/vinyl). Empty = system default. |
| `DefaultCastDeviceId` | `string` | `""` | Default Google Cast device ID. When set, auto-connects when Cast output is selected. |
| `DefaultCastDeviceName` | `string` | `""` | Friendly name of the default Cast device. Stored alongside ID to avoid DB lookups. |
| `SourceGainOffsets` | `Dictionary<string, float>` | `{}` | Per-source gain offsets (linear multiplier 0.0–2.0, where 1.0 = unity/0dB). Compensates for volume differences between sources. Key format: `AudioSourceType.ToString()` (e.g., `"Radio"`, `"FilePlayer"`, `"Bluetooth"`). |

---

### FilePlayerPreferences

**Section Name:** `FilePlayerPreferences`
**Source File:** `src/Radio.Core/Configuration/AudioPreferences.cs`
**Description:** User preferences for the file player.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LastSongPlayed` | `string` | `""` | Path of the last song played |
| `SongPositionMs` | `long` | `0` | Last song position in milliseconds |
| `Shuffle` | `bool` | `false` | Whether shuffle mode is enabled |
| `Repeat` | `RepeatMode` | `Off` | Repeat mode |
| `QueueItems` | `List<string>` | `[]` | Persisted queue items (file paths in order). Used to restore the queue on application restart. |
| `CurrentQueueIndex` | `int` | `-1` | Current index in the queue. Used to restore playback position. -1 = no active item. |

---

### GenericSourcePreferences

**Section Name:** `GenericSourcePreferences`  
**Source File:** `src/Radio.Core/Configuration/AudioPreferences.cs`  
**Description:** User preferences for the generic USB source.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `USBPort` | `string` | `""` | USB port for the generic source |

---

### BluetoothPreferences

**Section Name:** `BluetoothPreferences`
**Source File:** `src/Radio.Core/Configuration/BluetoothPreferences.cs`
**Description:** User and device preference data for Bluetooth audio. Tracks paired/trusted devices and last connection.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LastConnectedDevice` | `string?` | `null` | Address of the last connected Bluetooth device |
| `PairedDevices` | `List<string>` | `[]` | List of paired device addresses |
| `TrustedDevices` | `List<string>` | `[]` | Device trust list — auto-connect without prompt |

---

### RadioPreferences

**Section Name:** `Radio`
**Source File:** `src/Radio.Core/Configuration/RadioPreferences.cs`
**Description:** User preferences for radio playback. Persists last-used tuner settings so the radio resumes where the user left off.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LastBand` | `string` | `FM` | Last selected radio band (FM, AM, WB, VHF, SW) |
| `LastFrequency` | `double` | `101.5` | Last tuned frequency in MHz (FM) or kHz (AM/other bands) |
| `LastFrequencyStep` | `double` | `0.1` | Last frequency step size in MHz (FM) or kHz (AM) |
| `LastDeviceVolume` | `int` | `50` | Last device volume (0-100) |
| `LastEqualizerMode` | `string` | `Off` | Last equalizer mode |
| `LastDeviceType` | `string` | `RTLSDRCore` | Last selected radio device type |

---

### TTSPreferences

**Section Name:** `TTSPreferences`
**Source File:** `src/Radio.Core/Configuration/TTSPreferences.cs`
**Description:** User preferences for Text-to-Speech.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LastEngine` | `string` | *(empty)* | Last used TTS engine |
| `LastVoice` | `string` | *(empty)* | Last used voice identifier |
| `LastPitch` | `float` | `1.0` | Last used pitch setting |

---

## Secrets

Secrets contain sensitive data such as API keys and tokens. They are stored encrypted using the Data Protection API and referenced in configuration via secret tags (`${secret:identifier}`).

### TTS Secrets

**Section Name:** `TTSSecrets`  
**Source File:** `src/Radio.Core/Configuration/TTSSecrets.cs`  
**Description:** API credentials for cloud TTS services (resolved from secret tags).

| Property | Type | Description |
|----------|------|-------------|
| `GoogleAPIKey` | `string` | Google Cloud Text-to-Speech API key |
| `AzureAPIKey` | `string` | Azure Cognitive Services Speech API key |
| `AzureRegion` | `string` | Azure region for Speech service |

---

## Configuration Files

### tools/Radio.Tools.ConfigurationManager/appsettings.json

```json
{
  "ManagedConfiguration": {
    "DefaultStoreType": "Json",
    "BasePath": "./config",
    "JsonExtension": ".json",
    "SqliteFileName": "configuration.db",
    "SecretsFileName": "secrets",
    "BackupPath": "./config/backups",
    "AutoSave": true,
    "BackupRetentionDays": 30
  }
}
```

### tools/Radio.Tools.AudioUAT/appsettings.json

```json
{
  "AudioEngine": {
    "SampleRate": 48000,
    "Channels": 2,
    "BufferSize": 1024,
    "HotPlugIntervalSeconds": 5,
    "OutputBufferSizeSeconds": 5,
    "EnableHotPlugDetection": true
  }
}
```

---

## Secret Tag Format

Secrets are referenced in configuration values using the tag format:

```
${secret:identifier}
```

Example:
```json
{
  "TTS": {
    "GoogleAPIKey": "${secret:tts_google_api_key}",
    "AzureAPIKey": "${secret:tts_azure_api_key}"
  }
}
```

---

## Enumerations

### ConfigurationStoreType
| Value | Description |
|-------|-------------|
| `Json` | JSON file-based storage |
| `Sqlite` | SQLite database storage |

### DuckingPolicy
| Value | Description |
|-------|-------------|
| `FadeSmooth` | Smooth fade transition |
| `FadeQuick` | Quick fade transition |
| `Instant` | Instant volume change |

### RepeatMode
| Value | Description |
|-------|-------------|
| `Off` | No repeat |
| `One` | Repeat the current track |
| `All` | Repeat the entire playlist |

### TTSEngine
| Value | Description |
|-------|-------------|
| `Google` | Google Cloud Text-to-Speech |
| `Azure` | Azure Cognitive Services Speech |

### BluetoothAudioQuality
| Value | Description |
|-------|-------------|
| `Standard` | Standard quality (44.1kHz) |
| `High` | High quality (48kHz) |

### RadioBand
| Value | Description |
|-------|-------------|
| `AM` | Amplitude Modulation (520-1710 kHz) |
| `FM` | Frequency Modulation (87.5-108 MHz) |
| `WB` | Weather Band |
| `VHF` | Very High Frequency |
| `SW` | Shortwave |

### RadioEqualizerMode
| Value | Description |
|-------|-------------|
| `Off` | No equalization — flat response |
| `Pop` | Pop music preset with balanced frequency response |
| `Rock` | Rock music preset with enhanced bass and treble |
| `Country` | Country music preset optimized for vocals and acoustic instruments |
| `Classical` | Classical music preset with natural, wide frequency range |
| `Jazz` | Jazz music preset |
| `Normal` | Normal equalization preset |

---

## Radio Presets

Radio presets are saved radio station configurations that allow users to quickly tune to their favorite stations. They are stored in the SQLite database (in the `RadioPresets` table) alongside other audio data.

### Features
- **Maximum Presets:** 50 presets can be saved
- **Collision Detection:** Duplicate presets (same band and frequency) are prevented
- **Custom Names:** Users can provide custom names, or the system generates a default name in the format `{Band} - {Frequency}`
- **Persistence:** Presets are stored in the database and persist across application restarts

### Database Schema
The `RadioPresets` table includes the following fields:
- `Id` (TEXT PRIMARY KEY): Unique identifier for the preset
- `Name` (TEXT NOT NULL): Display name for the preset
- `Band` (TEXT NOT NULL): Radio band (AM, FM, WB, VHF, SW)
- `Frequency` (REAL NOT NULL): Station frequency
- `CreatedAt` (TEXT NOT NULL): ISO 8601 timestamp when preset was created
- `LastModifiedAt` (TEXT NOT NULL): ISO 8601 timestamp when preset was last modified

### REST API Endpoints
- `GET /api/radio/presets`: Retrieve all saved presets
- `POST /api/radio/presets`: Create a new preset
- `DELETE /api/radio/presets/{id}`: Delete a preset by ID

See [API Reference](api.md) for detailed API documentation.

---

*Last Updated: 2026-03-02*

---

## New Configuration Items (Code Cleanup Phase)

The following configuration items were added or updated as part of the Code Cleanup and Production Readiness implementation:

### AudioFiles Database Table (Phase 1)

Audio file metadata is now stored in the fingerprint database for tracking changes and deduplication.

**Table:** `AudioFiles`  
**Location:** `fingerprints.db` (part of FingerprintDbContext)

| Column | Type | Description |
|--------|------|-------------|
| `Id` | INTEGER PRIMARY KEY | Unique identifier |
| `Path` | TEXT NOT NULL UNIQUE | Full path to the audio file |
| `FileName` | TEXT NOT NULL | File name only |
| `Extension` | TEXT NOT NULL | File extension |
| `SizeBytes` | INTEGER NOT NULL | File size in bytes |
| `CreatedAt` | TEXT NOT NULL | File creation timestamp (ISO 8601) |
| `LastModifiedAt` | TEXT NOT NULL | File last modified timestamp |
| `Title` | TEXT | Track title (from metadata) |
| `Artist` | TEXT | Artist name (from metadata) |
| `Album` | TEXT | Album name (from metadata) |
| `Duration` | INTEGER | Duration in milliseconds |
| `TrackNumber` | INTEGER | Track number |
| `Genre` | TEXT | Genre |
| `Year` | INTEGER | Release year |
| `ScannedAt` | TEXT NOT NULL | When the file was last scanned |

### Azure TTS Voice Caching (Phase 7)

Azure TTS voices are now fetched from the Azure Speech REST API with a 24-hour cache to reduce API calls.

**Behavior:**
- First request to get voices will call the Azure API
- Results are cached for 24 hours
- If Azure credentials are not configured, defaults to a hardcoded list of common neural voices
- If the API call fails, falls back to defaults

**Required Secrets:**
- `azure_tts_key`: Azure Cognitive Services Speech API key
- `azure_tts_region`: Azure region (e.g., "eastus")

### RTL-SDR Device Caching (Phase 6)

RTL-SDR device enumeration now uses a 30-second cache to reduce repeated device queries.

**Behavior:**
- Devices are enumerated on first request
- Cache expires after 30 seconds
- Call `RadioFactory.InvalidateDeviceCache()` to force re-enumeration
- Always includes a mock device for development/testing

### SoundFlow Playback Integration (Phases 2, 8)

Audio playback now uses the SoundFlow engine via `SoundFlowPlaybackService`.

**Components Updated:**
- `AudioFileEventSource`: Event sounds play through SoundFlow
- `FilePlayerAudioSource`: Music files play through SoundFlow
- `AudioManager`: Now accepts `SoundFlowPlaybackService` for source creation

**Configuration:**
No additional configuration required - SoundFlow uses the AudioEngine configuration options.