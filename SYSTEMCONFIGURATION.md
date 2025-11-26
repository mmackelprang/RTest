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

*Last Updated: 2025-11-26*