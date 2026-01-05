# System Configuration Documentation

This document provides a comprehensive reference for all Configuration, Preferences, and Secrets used in the Radio Console application.

---

## Quick Start Guides

### For Development (Windows/Mac/Linux)

1. **Prerequisites**
   - .NET 8 SDK or later
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

   # Run Web UI (default: http://localhost:5001)
   dotnet run --project src/Radio.Web
   ```

### For Production (Raspberry Pi / Linux)

1. **System Prerequisites**
   ```bash
   sudo apt update
   sudo apt install -y dotnet-sdk-8.0 sqlite3 espeak-ng
   ```

2. **Clone and Build**
   ```bash
   git clone <repository-url>
   cd RadioConsole
   dotnet restore
   dotnet publish -c Release -o /opt/radio-console
   ```

3. **Create Data Directories**
   ```bash
   sudo mkdir -p /opt/radio-console/data/{config,metrics,fingerprints,backups}
   sudo mkdir -p /opt/radio-console/logs
   sudo chown -R radio:radio /opt/radio-console
   ```

4. **Configure Production Settings**
   - Copy `appsettings.json` to `/opt/radio-console/`
   - Set `DefaultStoreType` to `Sqlite` for better performance
   - Configure paths to use absolute paths (e.g., `/opt/radio-console/data`)

5. **Setup Systemd Service** (see [Service Setup](#systemd-service-setup))

---

## Configuration File Location

**Primary Configuration File:** `src/Radio.API/appsettings.json`

The Radio Console application uses a **consolidated configuration approach** where all application settings are defined in a single `appsettings.json` file located in the Radio.API project. This design choice provides several benefits:

- **Single Source of Truth**: All configuration sections (Database, ManagedConfiguration, Metrics, Fingerprinting, AudioEngine, Serilog) are in one location
- **Simplified Deployment**: Only one configuration file needs to be managed and deployed
- **Easier Maintenance**: Configuration changes are made in a single, well-organized file
- **Environment Overrides**: Standard ASP.NET Core configuration layering allows for `appsettings.Development.json`, `appsettings.Production.json`, and environment variables to override settings as needed

**Reference Example:** See `design/appsettings.example.json` for a complete template with all available configuration options.

---

## Secrets Setup

### Required Secrets for Full Functionality

The following secrets are required for various features:

1. **Spotify Integration** (Required for Spotify audio source)
   - `spotify_clientid`
   - `spotify_clientsecret`
   - `spotify_refreshtoken`

2. **Google Cloud Text-to-Speech** (Optional - for cloud TTS)
   - `google_tts_key`

3. **Azure Speech Services** (Optional - for cloud TTS)
   - `azure_tts_key`
   - `azure_tts_region`

4. **AcoustID Fingerprinting** (Optional - for music identification)
   - `acoustid_apikey`

### How to Configure Secrets

#### Method 1: Using Configuration Manager Tool

```bash
cd tools/Radio.Tools.ConfigurationManager
dotnet run

# Follow prompts to:
# 1. Select "Manage Secrets"
# 2. Create new secret with identifier and value
# 3. The tool will generate a secret tag like ${secret:spotify_clientid_abc123}
```

#### Method 2: Direct Configuration File

1. Create or edit `src/Radio.API/appsettings.json`
2. Add secret references in configuration:

```json
{
  "Spotify": {
    "ClientID": "${secret:spotify_clientid}",
    "ClientSecret": "${secret:spotify_clientsecret}",
    "RefreshToken": "${secret:spotify_refreshtoken}"
  },
  "TTS": {
    "GoogleAPIKey": "${secret:google_tts_key}",
    "AzureAPIKey": "${secret:azure_tts_key}",
    "AzureRegion": "${secret:azure_tts_region}"
  },
  "Fingerprinting": {
    "AcoustId": {
      "ApiKey": "${secret:acoustid_apikey}"
    }
  }
}
```

3. Create secrets file at `config/secrets.json` (for JSON store) or in SQLite database

#### Method 3: Environment Variables (Production)

For production deployments, you can use environment variables:

```bash
export SPOTIFY__CLIENTID="your_client_id"
export SPOTIFY__CLIENTSECRET="your_client_secret"
export SPOTIFY__REFRESHTOKEN="your_refresh_token"
```

### Getting API Keys

#### Spotify Setup

1. Go to [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
2. Click "Create an App"
3. Note the **Client ID** and **Client Secret**
4. Add `http://localhost:5000/callback` to Redirect URIs
5. Get a refresh token using the Authorization Code Flow:
   ```bash
   # Use tools/spotify-auth-helper.sh or follow Spotify OAuth docs
   ```

#### Google Cloud TTS Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Enable "Cloud Text-to-Speech API"
3. Create credentials (API Key)
4. Copy the API key

#### Azure Speech Setup

1. Go to [Azure Portal](https://portal.azure.com/)
2. Create a "Speech Service" resource
3. Note the **Key** and **Region** from the resource

#### AcoustID Setup

1. Go to [AcoustID Applications](https://acoustid.org/new-application)
2. Register a new application
3. Copy the API key

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

### eSpeak-NG (Offline, Default)

**Prerequisites:**
```bash
# Raspberry Pi / Linux
sudo apt install espeak-ng

# Verify installation
espeak-ng --version
```

**Configuration:**
```json
{
  "TTS": {
    "DefaultEngine": "ESpeak",
    "ESpeakPath": "espeak-ng",
    "DefaultVoice": "en",
    "DefaultSpeed": 1.0,
    "DefaultPitch": 1.0
  }
}
```

**No additional setup required** - works offline immediately.

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

Audio fingerprinting identifies songs playing on Radio or Vinyl sources using AcoustID and MusicBrainz.

### Prerequisites

1. AcoustID account and API key (see [Getting API Keys](#getting-api-keys))
2. Internet connection for lookups

### Configuration

```json
{
  "Fingerprinting": {
    "Enabled": true,
    "SampleDurationSeconds": 15,
    "IdentificationIntervalSeconds": 30,
    "MinimumConfidenceThreshold": 0.5,
    "DuplicateSuppressionMinutes": 5,
    "DatabasePath": "./data/fingerprints/fingerprints.db",
    "AcoustId": {
      "ApiKey": "${secret:acoustid_apikey}",
      "BaseUrl": "https://api.acoustid.org/v2",
      "MaxRequestsPerSecond": 3,
      "TimeoutSeconds": 10
    },
    "MusicBrainz": {
      "BaseUrl": "https://musicbrainz.org/ws/2",
      "ApplicationName": "RadioConsole",
      "ApplicationVersion": "1.0.0",
      "ContactEmail": "your-email@example.com",
      "MaxRequestsPerSecond": 1,
      "TimeoutSeconds": 10
    }
  }
}
```

### Create Secret

```bash
cd tools/Radio.Tools.ConfigurationManager
dotnet run
# Create secret: acoustid_apikey = <your-api-key>
```

### Rate Limiting

- **AcoustID**: 3 requests/second (free tier limit)
- **MusicBrainz**: 1 request/second (anonymous limit)

The system automatically respects these limits.

---

## Systemd Service Setup

For production Linux deployments, create a systemd service:

### Create Service File

Create `/etc/systemd/system/radio-console-api.service`:

```ini
[Unit]
Description=Radio Console API
After=network.target

[Service]
Type=notify
User=radio
Group=radio
WorkingDirectory=/opt/radio-console
ExecStart=/usr/bin/dotnet /opt/radio-console/Radio.API.dll
Restart=always
RestartSec=10
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="DOTNET_PRINT_TELEMETRY_MESSAGE=false"

[Install]
WantedBy=multi-user.target
```

Create `/etc/systemd/system/radio-console-web.service`:

```ini
[Unit]
Description=Radio Console Web UI
After=network.target radio-console-api.service

[Service]
Type=notify
User=radio
Group=radio
WorkingDirectory=/opt/radio-console
ExecStart=/usr/bin/dotnet /opt/radio-console/Radio.Web.dll
Restart=always
RestartSec=10
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="DOTNET_PRINT_TELEMETRY_MESSAGE=false"

[Install]
WantedBy=multi-user.target
```

### Enable and Start Services

```bash
# Create user
sudo useradd -r -s /bin/false radio

# Set permissions
sudo chown -R radio:radio /opt/radio-console

# Reload systemd
sudo systemctl daemon-reload

# Enable services
sudo systemctl enable radio-console-api
sudo systemctl enable radio-console-web

# Start services
sudo systemctl start radio-console-api
sudo systemctl start radio-console-web

# Check status
sudo systemctl status radio-console-api
sudo systemctl status radio-console-web
```

### Manage Services

```bash
# View logs
sudo journalctl -u radio-console-api -f
sudo journalctl -u radio-console-web -f

# Stop services
sudo systemctl stop radio-console-api radio-console-web

# Restart services
sudo systemctl restart radio-console-api radio-console-web
```

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

### Audio

**Section Name:** `Audio`  
**Source File:** `src/Radio.Core/Configuration/AudioOptions.cs`  
**Description:** Configuration options for the audio system including ducking behavior.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultSource` | `string` | `Spotify` | Default primary audio source name |
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
| `Radio.USBPort` | `string` | `/dev/ttyUSB0` | USB port path for the radio device (Raddy RF320) |
| `Vinyl.USBPort` | `string` | `/dev/ttyUSB1` | USB port path for the vinyl turntable device |
| `Cast.DefaultDevice` | `string` | `""` | Default Chromecast device name |
| `Spotify.Mode` | `SpotifyMode` | `Integrated` | Spotify integration mode (RemoteControl or Integrated) |
| `Spotify.LibrespotPath` | `string` | `/usr/bin/librespot` | Path to the librespot executable (used when Mode is Integrated) |

**Spotify Mode Options:**
- **RemoteControl**: Uses Spotify Connect API (no audio data flows through app)
- **Integrated**: Manages librespot process and captures audio via pipe

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
| `DefaultEngine` | `string` | `ESpeak` | Default TTS engine to use |
| `DefaultVoice` | `string` | `en` | Default voice identifier |
| `DefaultPitch` | `float` | `1.0` | Default pitch (0.5 to 2.0, 1.0 = normal) |
| `DefaultSpeed` | `float` | `1.0` | Default speaking speed (0.5 to 2.0, 1.0 = normal) |
| `ESpeakPath` | `string` | `espeak-ng` | Path to the espeak-ng executable |
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

---

### Fingerprinting

**Section Name:** `Fingerprinting`  
**Source File:** `src/Radio.Core/Configuration/FingerprintingOptions.cs`  
**Description:** Configuration options for the audio fingerprinting system.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable or disable automatic fingerprinting |
| `SampleDurationSeconds` | `int` | `15` | Duration of audio to capture for fingerprinting (seconds) |
| `IdentificationIntervalSeconds` | `int` | `30` | Interval between identification attempts (seconds) |
| `MinimumConfidenceThreshold` | `double` | `0.5` | Minimum confidence threshold for accepting a match (0.0 to 1.0) |
| `DuplicateSuppressionMinutes` | `int` | `5` | Minutes to suppress duplicate identifications of the same track |
| `DatabasePath` | `string` | `./data/fingerprints.db` | SQLite database path for fingerprint cache |
| `AcoustId.ApiKey` | `string` | `""` | AcoustID API key (register at https://acoustid.org/new-application) |
| `AcoustId.BaseUrl` | `string` | `https://api.acoustid.org/v2` | AcoustID API base URL |
| `AcoustId.MaxRequestsPerSecond` | `int` | `3` | Maximum requests per second (AcoustID limit is 3) |
| `AcoustId.TimeoutSeconds` | `int` | `10` | Request timeout in seconds |
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
| `DefaultDevice` | `string` | `RTLSDRCore` | Default radio device type (RTLSDRCore or RF320) |
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
| `CurrentSource` | `string` | `Spotify` | Currently selected audio source |
| `MasterVolume` | `int` | `75` | Master volume level (0-100) |

---

### SpotifyPreferences

**Section Name:** `SpotifyPreferences`  
**Source File:** `src/Radio.Core/Configuration/AudioPreferences.cs`  
**Description:** User preferences for Spotify playback.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LastSongPlayed` | `string` | `""` | URI of the last song played |
| `SongPositionMs` | `long` | `0` | Last song position in milliseconds |
| `Shuffle` | `bool` | `false` | Whether shuffle mode is enabled |
| `Repeat` | `RepeatMode` | `Off` | Repeat mode |

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

---

### GenericSourcePreferences

**Section Name:** `GenericSourcePreferences`  
**Source File:** `src/Radio.Core/Configuration/AudioPreferences.cs`  
**Description:** User preferences for the generic USB source.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `USBPort` | `string` | `""` | USB port for the generic source |

---

### TTSPreferences

**Section Name:** `TTSPreferences`  
**Source File:** `src/Radio.Core/Configuration/TTSPreferences.cs`  
**Description:** User preferences for Text-to-Speech.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LastEngine` | `string` | `ESpeak` | Last used TTS engine |
| `LastVoice` | `string` | `en-US-Standard-A` | Last used voice identifier |
| `LastPitch` | `float` | `1.0` | Last used pitch setting |

---

## Secrets

Secrets contain sensitive data such as API keys and tokens. They are stored encrypted using the Data Protection API and referenced in configuration via secret tags (`${secret:identifier}`).

### Spotify Secrets

**Section Name:** `Spotify`  
**Source File:** `src/Radio.Core/Configuration/SpotifySecrets.cs`  
**Description:** Spotify API credentials (resolved from secret tags).

| Property | Type | Description |
|----------|------|-------------|
| `ClientID` | `string` | Spotify Client ID |
| `ClientSecret` | `string` | Spotify Client Secret |
| `RefreshToken` | `string` | Spotify Refresh Token for authorization |

---

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
  "Spotify": {
    "ClientID": "${secret:spotify_clientid_abc123}",
    "ClientSecret": "${secret:spotify_secret_def456}"
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
| `ESpeak` | Local eSpeak-NG engine |
| `Google` | Google Cloud Text-to-Speech |
| `Azure` | Azure Cognitive Services Speech |

### RadioBand
| Value | Description |
|-------|-------------|
| `AM` | Amplitude Modulation (520-1710 kHz) |
| `FM` | Frequency Modulation (87.5-108 MHz) |
| `WB` | Weather Band |
| `VHF` | Very High Frequency |
| `SW` | Shortwave |

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

See [API Reference](design/API_REFERENCE.md) for detailed API documentation.

---

*Last Updated: 2025-12-31*

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