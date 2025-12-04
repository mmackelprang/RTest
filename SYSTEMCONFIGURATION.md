# System Configuration Documentation

This document provides a comprehensive reference for all Configuration, Preferences, and Secrets used in the Radio Console application.

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

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable or disable metrics collection |
| `FlushIntervalSeconds` | `int` | `60` | Interval in seconds for flushing buffered metrics to disk |
| `DatabasePath` | `string` | `./data/metrics.db` | SQLite database path for metrics storage |
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

### Devices

**Section Name:** `Devices`  
**Source File:** `src/Radio.Core/Configuration/DeviceOptions.cs`  
**Description:** Configuration options for audio device settings.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Radio.USBPort` | `string` | `/dev/ttyUSB0` | USB port path for the radio device (Raddy RF320) |
| `Vinyl.USBPort` | `string` | `/dev/ttyUSB1` | USB port path for the vinyl turntable device |
| `Cast.DefaultDevice` | `string` | `""` | Default Chromecast device name |

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

---

*Last Updated: 2025-12-04*