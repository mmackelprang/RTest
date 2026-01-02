namespace Radio.API.Models;

/// <summary>
/// Configuration settings response.
/// </summary>
public class ConfigurationSettingsDto
{
  /// <summary>
  /// Gets or sets the audio configuration.
  /// </summary>
  public AudioConfigurationDto Audio { get; set; } = new();

  /// <summary>
  /// Gets or sets the visualizer configuration.
  /// </summary>
  public VisualizerConfigurationDto Visualizer { get; set; } = new();

  /// <summary>
  /// Gets or sets the output configuration.
  /// </summary>
  public OutputConfigurationDto Output { get; set; } = new();
}

/// <summary>
/// Audio system configuration.
/// </summary>
public class AudioConfigurationDto
{
  /// <summary>
  /// Gets or sets the default source.
  /// </summary>
  public string DefaultSource { get; set; } = "Spotify";

  /// <summary>
  /// Gets or sets the ducking percentage.
  /// </summary>
  public int DuckingPercentage { get; set; } = 20;

  /// <summary>
  /// Gets or sets the ducking policy.
  /// </summary>
  public string DuckingPolicy { get; set; } = "FadeSmooth";

  /// <summary>
  /// Gets or sets the ducking attack time in milliseconds.
  /// </summary>
  public int DuckingAttackMs { get; set; } = 100;

  /// <summary>
  /// Gets or sets the ducking release time in milliseconds.
  /// </summary>
  public int DuckingReleaseMs { get; set; } = 500;
}

/// <summary>
/// Visualizer configuration.
/// </summary>
public class VisualizerConfigurationDto
{
  /// <summary>
  /// Gets or sets the FFT size.
  /// </summary>
  public int FFTSize { get; set; } = 2048;

  /// <summary>
  /// Gets or sets the waveform sample count.
  /// </summary>
  public int WaveformSampleCount { get; set; } = 512;

  /// <summary>
  /// Gets or sets the peak hold time in milliseconds.
  /// </summary>
  public int PeakHoldTimeMs { get; set; } = 1000;

  /// <summary>
  /// Gets or sets whether to apply window function.
  /// </summary>
  public bool ApplyWindowFunction { get; set; } = true;

  /// <summary>
  /// Gets or sets the spectrum smoothing factor.
  /// </summary>
  public float SpectrumSmoothing { get; set; } = 0.5f;
}

/// <summary>
/// Output configuration.
/// </summary>
public class OutputConfigurationDto
{
  /// <summary>
  /// Gets or sets local output settings.
  /// </summary>
  public LocalOutputSettingsDto Local { get; set; } = new();

  /// <summary>
  /// Gets or sets HTTP stream output settings.
  /// </summary>
  public HttpStreamSettingsDto HttpStream { get; set; } = new();

  /// <summary>
  /// Gets or sets Google Cast output settings.
  /// </summary>
  public GoogleCastSettingsDto GoogleCast { get; set; } = new();
}

/// <summary>
/// Local output settings.
/// </summary>
public class LocalOutputSettingsDto
{
  /// <summary>
  /// Gets or sets whether local output is enabled.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Gets or sets the preferred device ID.
  /// </summary>
  public string PreferredDeviceId { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the default volume.
  /// </summary>
  public float DefaultVolume { get; set; } = 0.8f;
}

/// <summary>
/// HTTP stream output settings.
/// </summary>
public class HttpStreamSettingsDto
{
  /// <summary>
  /// Gets or sets whether HTTP streaming is enabled.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Gets or sets the stream port.
  /// </summary>
  public int Port { get; set; } = 8080;

  /// <summary>
  /// Gets or sets the endpoint path.
  /// </summary>
  public string EndpointPath { get; set; } = "/stream/audio";

  /// <summary>
  /// Gets or sets the sample rate.
  /// </summary>
  public int SampleRate { get; set; } = 48000;

  /// <summary>
  /// Gets or sets the number of channels.
  /// </summary>
  public int Channels { get; set; } = 2;
}

/// <summary>
/// Google Cast output settings.
/// </summary>
public class GoogleCastSettingsDto
{
  /// <summary>
  /// Gets or sets whether Google Cast is enabled.
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>
  /// Gets or sets the discovery timeout in seconds.
  /// </summary>
  public int DiscoveryTimeoutSeconds { get; set; } = 10;

  /// <summary>
  /// Gets or sets the default volume.
  /// </summary>
  public float DefaultVolume { get; set; } = 0.7f;
}

/// <summary>
/// Request to update configuration.
/// </summary>
public class UpdateConfigurationRequest
{
  /// <summary>
  /// Gets or sets the section to update.
  /// </summary>
  public string Section { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the key to update.
  /// </summary>
  public string Key { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the value to set.
  /// </summary>
  public string Value { get; set; } = string.Empty;
}

// ==================== Device Configuration DTOs ====================

/// <summary>
/// Device configuration options for USB ports and Chromecast.
/// </summary>
public class DeviceOptionsDto
{
  /// <summary>
  /// Gets or sets the radio device options.
  /// </summary>
  public RadioDeviceOptionsDto Radio { get; set; } = new();

  /// <summary>
  /// Gets or sets the vinyl device options.
  /// </summary>
  public VinylDeviceOptionsDto Vinyl { get; set; } = new();

  /// <summary>
  /// Gets or sets the cast device options.
  /// </summary>
  public CastDeviceOptionsDto Cast { get; set; } = new();
}

/// <summary>
/// Radio device USB port configuration.
/// </summary>
public class RadioDeviceOptionsDto
{
  /// <summary>
  /// Gets or sets the USB port path for the radio device.
  /// </summary>
  public string USBPort { get; set; } = "/dev/ttyUSB0";
}

/// <summary>
/// Vinyl turntable device USB port configuration.
/// </summary>
public class VinylDeviceOptionsDto
{
  /// <summary>
  /// Gets or sets the USB port path for the vinyl device.
  /// </summary>
  public string USBPort { get; set; } = "/dev/ttyUSB1";
}

/// <summary>
/// Chromecast device configuration.
/// </summary>
public class CastDeviceOptionsDto
{
  /// <summary>
  /// Gets or sets the default Chromecast device name.
  /// </summary>
  public string DefaultDevice { get; set; } = "";
}

// ==================== Preferences DTOs (Phase 2) ====================

/// <summary>
/// User preferences for audio playback.
/// </summary>
public class AudioPreferencesDto
{
  /// <summary>
  /// Gets or sets the currently selected audio source.
  /// </summary>
  public string CurrentSource { get; set; } = "Spotify";

  /// <summary>
  /// Gets or sets the currently selected audio output device ID.
  /// </summary>
  public string CurrentOutput { get; set; } = "";

  /// <summary>
  /// Gets or sets the master volume level (0-100).
  /// </summary>
  public int MasterVolume { get; set; } = 75;
}

/// <summary>
/// User preferences for Spotify playback.
/// </summary>
public class SpotifyPreferencesDto
{
  /// <summary>
  /// Gets or sets the URI of the last song played.
  /// </summary>
  public string LastSongPlayed { get; set; } = "";

  /// <summary>
  /// Gets or sets the last song position in milliseconds.
  /// </summary>
  public long SongPositionMs { get; set; } = 0;

  /// <summary>
  /// Gets or sets whether shuffle mode is enabled.
  /// </summary>
  public bool Shuffle { get; set; } = false;

  /// <summary>
  /// Gets or sets the repeat mode (Off, One, All).
  /// </summary>
  public string Repeat { get; set; } = "Off";
}

/// <summary>
/// User preferences for the file player.
/// </summary>
public class FilePlayerPreferencesDto
{
  /// <summary>
  /// Gets or sets the path of the last song played.
  /// </summary>
  public string LastSongPlayed { get; set; } = "";

  /// <summary>
  /// Gets or sets the last song position in milliseconds.
  /// </summary>
  public long SongPositionMs { get; set; } = 0;

  /// <summary>
  /// Gets or sets whether shuffle mode is enabled.
  /// </summary>
  public bool Shuffle { get; set; } = false;

  /// <summary>
  /// Gets or sets the repeat mode (Off, One, All).
  /// </summary>
  public string Repeat { get; set; } = "Off";
}

/// <summary>
/// User preferences for the radio tuner.
/// </summary>
public class RadioPreferencesDto
{
  /// <summary>
  /// Gets or sets the last tuned frequency.
  /// </summary>
  public float LastFrequency { get; set; } = 101.1f;

  /// <summary>
  /// Gets or sets the last band (AM, FM, WB, VHF, SW).
  /// </summary>
  public string LastBand { get; set; } = "FM";

  /// <summary>
  /// Gets or sets the last EQ mode.
  /// </summary>
  public string LastEQMode { get; set; } = "";
}

/// <summary>
/// User preferences for the generic USB source.
/// </summary>
public class GenericSourcePreferencesDto
{
  /// <summary>
  /// Gets or sets the USB port for the generic source.
  /// </summary>
  public string USBPort { get; set; } = "";
}

/// <summary>
/// Spotify API credentials (secrets).
/// </summary>
public class SpotifySecretsDto
{
  /// <summary>
  /// Gets or sets the Spotify Client ID.
  /// </summary>
  public string ClientID { get; set; } = "";

  /// <summary>
  /// Gets or sets the Spotify Client Secret.
  /// </summary>
  public string ClientSecret { get; set; } = "";

  /// <summary>
  /// Gets or sets the Spotify Refresh Token.
  /// </summary>
  public string RefreshToken { get; set; } = "";
}

/// <summary>
/// TTS service API credentials (secrets).
/// </summary>
public class TTSSecretsDto
{
  /// <summary>
  /// Gets or sets the Google Cloud Text-to-Speech API key.
  /// </summary>
  public string GoogleAPIKey { get; set; } = "";

  /// <summary>
  /// Gets or sets the Azure Cognitive Services Speech API key.
  /// </summary>
  public string AzureAPIKey { get; set; } = "";

  /// <summary>
  /// Gets or sets the Azure region for Speech Services.
  /// </summary>
  public string AzureRegion { get; set; } = "";
}

/// <summary>
/// AcoustID API credentials (secrets).
/// </summary>
public class AcoustIdSecretDto
{
  /// <summary>
  /// Gets or sets the AcoustID API key.
  /// </summary>
  public string ApiKey { get; set; } = "";
}

// ========== Phase 5: Configuration Store Management DTOs ==========

/// <summary>
/// Configuration store metadata information.
/// </summary>
public class ConfigurationStoreInfoDto
{
  /// <summary>
  /// Gets or sets the store type (JSON or SQLite).
  /// </summary>
  public string StoreType { get; set; } = "";

  /// <summary>
  /// Gets or sets the full file path to the store.
  /// </summary>
  public string Location { get; set; } = "";

  /// <summary>
  /// Gets or sets the size of the store in bytes.
  /// </summary>
  public long SizeBytes { get; set; }

  /// <summary>
  /// Gets or sets the last modified timestamp.
  /// </summary>
  public DateTime? LastModified { get; set; }

  /// <summary>
  /// Gets or sets the number of configuration entries in the store.
  /// </summary>
  public int EntryCount { get; set; }
}

/// <summary>
/// Comparison result between JSON and SQLite configuration stores.
/// </summary>
public class ConfigurationComparisonDto
{
  /// <summary>
  /// Gets or sets the number of entries in the JSON store.
  /// </summary>
  public int JsonEntryCount { get; set; }

  /// <summary>
  /// Gets or sets the number of entries in the SQLite store.
  /// </summary>
  public int SqliteEntryCount { get; set; }

  /// <summary>
  /// Gets or sets the list of differences between stores.
  /// </summary>
  public List<ConfigurationDifferenceDto> Differences { get; set; } = new();
}

/// <summary>
/// A single configuration difference between stores.
/// </summary>
public class ConfigurationDifferenceDto
{
  /// <summary>
  /// Gets or sets the configuration key.
  /// </summary>
  public string Key { get; set; } = "";

  /// <summary>
  /// Gets or sets the value in the JSON store (null if not present).
  /// </summary>
  public string? JsonValue { get; set; }

  /// <summary>
  /// Gets or sets the value in the SQLite store (null if not present).
  /// </summary>
  public string? SqliteValue { get; set; }

  /// <summary>
  /// Gets or sets the difference status.
  /// Values: OnlyInJson, OnlyInSqlite, Different, Same
  /// </summary>
  public string Status { get; set; } = "";
}

/// <summary>
/// Request to reconcile configuration between stores.
/// </summary>
public class ReconcileConfigurationRequestDto
{
  /// <summary>
  /// Gets or sets the source store (json or sqlite).
  /// </summary>
  public string SourceStore { get; set; } = "";

  /// <summary>
  /// Gets or sets the target store (json or sqlite).
  /// </summary>
  public string TargetStore { get; set; } = "";

  /// <summary>
  /// Gets or sets the list of configuration keys to copy.
  /// </summary>
  public List<string> Keys { get; set; } = new();
}
