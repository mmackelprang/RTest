using Radio.Core.Interfaces.Audio;

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
  /// Gets or sets whether this source is a streaming source (e.g., internet radio, Bluetooth).
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
  /// Gets or sets the available Bluetooth devices.
  /// </summary>
  public List<BluetoothDeviceDto> BluetoothDevices { get; set; } = [];

  /// <summary>
  /// Gets or sets the discovered Bluetooth devices (during discovery).
  /// </summary>
  public List<BluetoothDeviceDto> DiscoveredBluetoothDevices { get; set; } = [];

  /// <summary>
  /// Gets or sets Bluetooth configuration metadata.
  /// </summary>
  public BluetoothSourceInfoDto? Bluetooth { get; set; }

  /// <summary>
  /// Gets or sets the currently active source type.
  /// </summary>
  public string? ActiveSourceType { get; set; }

  /// <summary>
  /// Gets or sets the active sources in the mixer.
  /// </summary>
  public List<AudioSourceDto> ActiveSources { get; set; } = [];
}

public class BluetoothSourceInfoDto
{
  public bool IsDiscovering { get; set; }
  public bool AutoSwitchOnConnect { get; set; }
  public bool RequirePairing { get; set; }
  public string DeviceName { get; set; } = string.Empty;
  public string AudioQuality { get; set; } = string.Empty;
}

/// <summary>
/// Bluetooth device DTO.
/// </summary>
public class BluetoothDeviceDto
{
  public string Address { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsPaired { get; set; }
  public bool IsConnected { get; set; }
  public DateTime? LastConnected { get; set; }
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
  /// Gets or sets the device type (Input, Output, Duplex).
  /// </summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether this is the default device.
  /// </summary>
  public bool IsDefault { get; set; }

  /// <summary>
  /// Gets or sets whether this device is currently active (playing audio).
  /// </summary>
  public bool IsActive { get; set; }

  /// <summary>
  /// Gets or sets whether this is a USB device.
  /// </summary>
  public bool IsUSBDevice { get; set; }

  /// <summary>
  /// Gets or sets the USB port path (if applicable).
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

/// <summary>
/// Represents a USB port and its reservation status.
/// </summary>
public class UsbPortDto
{
  /// <summary>
  /// Gets or sets the USB port identifier.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the human-readable port name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether this port is currently reserved by a source.
  /// </summary>
  public bool IsReserved { get; set; }

  /// <summary>
  /// Gets or sets the source ID that has reserved this port, if any.
  /// </summary>
  public string? ReservedBy { get; set; }
}

/// <summary>
/// Information about a TTS engine.
/// </summary>
public class TTSEngineInfoDto
{
  /// <summary>
  /// Gets or sets the engine identifier (Google, Azure).
  /// </summary>
  public string Engine { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the human-readable name of the engine.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether the engine is currently available.
  /// </summary>
  public bool IsAvailable { get; set; }

  /// <summary>
  /// Gets or sets whether the engine requires an API key.
  /// </summary>
  public bool RequiresApiKey { get; set; }

  /// <summary>
  /// Gets or sets whether the engine works offline.
  /// </summary>
  public bool IsOffline { get; set; }
}

/// <summary>
/// Information about a TTS voice.
/// </summary>
public class TTSVoiceInfoDto
{
  /// <summary>
  /// Gets or sets the unique voice identifier.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the human-readable voice name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the language code (e.g., "en-US").
  /// </summary>
  public string Language { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the voice gender (Male, Female, Neutral).
  /// </summary>
  public string Gender { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether this voice is a user favorite.
  /// </summary>
  public bool IsFavorite { get; set; }

  /// <summary>
  /// Gets or sets the pricing tier (e.g., "Standard", "WaveNet", "Neural").
  /// </summary>
  public string PriceTier { get; set; } = "Standard";
}

/// <summary>
/// Request to play TTS event.
/// </summary>
public class PlayTTSRequest
{
  /// <summary>
  /// Gets or sets the text to speak.
  /// </summary>
  public string Text { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the TTS engine to use (optional).
  /// </summary>
  public string? Engine { get; set; }

  /// <summary>
  /// Gets or sets the voice identifier (optional).
  /// </summary>
  public string? Voice { get; set; }

  /// <summary>
  /// Gets or sets the speaking rate (0.5-2.0, optional).
  /// </summary>
  public float? Speed { get; set; }

  /// <summary>
  /// Gets or sets the pitch adjustment (0.5-2.0, optional).
  /// </summary>
  public float? Pitch { get; set; }
}

/// <summary>
/// Request to play audio file event.
/// </summary>
public class PlayFileEventRequest
{
  /// <summary>
  /// Gets or sets the absolute file path to play.
  /// Must be an absolute path (e.g., "D:/audio/sound.mp3").
  /// Use the GET /api/sources/events/sounds endpoint to get available files with absolute paths.
  /// </summary>
  public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// Represents information about a notification sound file.
/// </summary>
public class NotificationSoundDto
{
  /// <summary>
  /// Gets or sets the file name (without path).
  /// </summary>
  public string FileName { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the absolute file path.
  /// This path can be passed directly to the POST /api/sources/events/file endpoint.
  /// </summary>
  public string FilePath { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the file size in bytes.
  /// </summary>
  public long FileSize { get; set; }
}

/// <summary>
/// Represents a Google Cast device.
/// </summary>
public class CastDeviceDto
{
  /// <summary>
  /// Gets or sets the unique device ID.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the friendly name of the device.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the IP address of the device.
  /// </summary>
  public string IpAddress { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the port number.
  /// </summary>
  public int Port { get; set; }

  /// <summary>
  /// Gets or sets the device model.
  /// </summary>
  public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Display metadata for a device, including hidden/visible state and friendly name overrides.
/// </summary>
public class DeviceDisplayInfoDto
{
  public string Id { get; set; } = string.Empty;
  public string RawName { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public bool IsHidden { get; set; }
  public string? FriendlyNameOverride { get; set; }
  public string Type { get; set; } = string.Empty;
  public bool IsDefault { get; set; }
  public bool IsUSBDevice { get; set; }
}

/// <summary>
/// Request to set device visibility.
/// </summary>
public class SetDeviceVisibilityRequest
{
  public string RawName { get; set; } = string.Empty;
  public bool Visible { get; set; }
}

/// <summary>
/// Request to set a device friendly name.
/// </summary>
public class SetDeviceFriendlyNameRequest
{
  public string RawName { get; set; } = string.Empty;
  public string FriendlyName { get; set; } = string.Empty;
}

/// <summary>
/// Request to connect to a Cast device.
/// </summary>
public class ConnectCastDeviceRequest
{
  /// <summary>
  /// Gets or sets the device ID.
  /// </summary>
  public string DeviceId { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device name.
  /// </summary>
  public string? Name { get; set; }

  /// <summary>
  /// Gets or sets the IP address of the device.
  /// </summary>
  public string IpAddress { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the port number.
  /// </summary>
  public int? Port { get; set; }

  /// <summary>
  /// Gets or sets the device model.
  /// </summary>
  public string? Model { get; set; }
}
