namespace Radio.API.Models;

/// <summary>
/// Represents an audio source.
/// </summary>
public class AudioSourceDto
{
  /// <summary>
  /// Gets or sets the unique identifier.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the source type.
  /// </summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the source category (Primary or Event).
  /// </summary>
  public string Category { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the current state.
  /// </summary>
  public string State { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the volume level.
  /// </summary>
  public float Volume { get; set; }

  /// <summary>
  /// Gets or sets whether seeking is supported.
  /// </summary>
  public bool IsSeekable { get; set; }

  /// <summary>
  /// Gets or sets the metadata about current content.
  /// Values are typed objects (e.g., TimeSpan for Duration, int for TrackNumber).
  /// </summary>
  public Dictionary<string, object>? Metadata { get; set; }

  /// <summary>
  /// Gets or sets whether this source is a radio tuner.
  /// </summary>
  public bool IsRadio { get; set; }

  /// <summary>
  /// Gets or sets whether this source is a streaming source (e.g., Spotify, internet radio).
  /// </summary>
  public bool IsStreaming { get; set; }

  /// <summary>
  /// Gets or sets whether this source has a playback queue.
  /// </summary>
  public bool HasQueue { get; set; }

  /// <summary>
  /// Gets or sets a dictionary of additional capabilities for this source.
  /// Keys are capability names (e.g., "SupportsNext", "SupportsEqualizer") and values indicate whether supported.
  /// </summary>
  public Dictionary<string, bool>? Capabilities { get; set; }
}

/// <summary>
/// Request to select or switch audio source.
/// </summary>
public class SelectSourceRequest
{
  /// <summary>
  /// Gets or sets the source type to activate.
  /// </summary>
  public string SourceType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets optional source-specific configuration.
  /// </summary>
  public Dictionary<string, string>? Configuration { get; set; }
}

/// <summary>
/// Represents available audio sources list.
/// </summary>
public class AvailableSourcesDto
{
  /// <summary>
  /// Gets or sets the list of available primary sources.
  /// </summary>
  public List<string> PrimarySources { get; set; } = [];

  /// <summary>
  /// Gets or sets the currently active source type.
  /// </summary>
  public string? ActiveSourceType { get; set; }

  /// <summary>
  /// Gets or sets the active sources in the mixer.
  /// </summary>
  public List<AudioSourceDto> ActiveSources { get; set; } = [];
}

/// <summary>
/// Represents an audio device.
/// </summary>
public class AudioDeviceDto
{
  /// <summary>
  /// Gets or sets the device ID.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device type.
  /// </summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether this device is currently in use.
  /// </summary>
  public bool IsActive { get; set; }

  /// <summary>
  /// Gets or sets whether this device is the default device.
  /// </summary>
  public bool IsDefault { get; set; }

  /// <summary>
  /// Gets or sets whether this device is a USB device.
  /// </summary>
  public bool IsUSBDevice { get; set; }

  /// <summary>
  /// Gets or sets the USB port path if applicable.
  /// </summary>
  public string? USBPort { get; set; }

  /// <summary>
  /// Gets or sets the maximum channels supported.
  /// </summary>
  public int MaxChannels { get; set; }

  /// <summary>
  /// Gets or sets supported sample rates.
  /// </summary>
  public int[]? SupportedSampleRates { get; set; }

  /// <summary>
  /// Gets or sets additional device properties.
  /// </summary>
  public Dictionary<string, string>? Properties { get; set; }
}

/// <summary>
/// Request to set the output device.
/// </summary>
public class SetOutputDeviceRequest
{
  /// <summary>
  /// Gets or sets the device ID to use.
  /// </summary>
  public string DeviceId { get; set; } = string.Empty;
}

/// <summary>
/// Request to set shuffle state.
/// </summary>
public class SetShuffleRequest
{
  /// <summary>
  /// Gets or sets whether shuffle should be enabled.
  /// </summary>
  public bool Enabled { get; set; }
}

/// <summary>
/// Request to set repeat mode.
/// </summary>
public class SetRepeatModeRequest
{
  /// <summary>
  /// Gets or sets the repeat mode (Off, One, All).
  /// </summary>
  public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Represents the ducking state.
/// </summary>
public class DuckingStateDto
{
  /// <summary>
  /// Gets or sets whether ducking is active.
  /// </summary>
  public bool IsDucking { get; set; }

  /// <summary>
  /// Gets or sets the current duck level (0-100).
  /// </summary>
  public float DuckLevel { get; set; }

  /// <summary>
  /// Gets or sets the number of active event sources.
  /// </summary>
  public int ActiveEventCount { get; set; }
}
