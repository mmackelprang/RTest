namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for audio device settings.
/// Loaded from the 'Devices' configuration section.
/// </summary>
public class DeviceOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Devices";

  /// <summary>
  /// Gets or sets the radio device options.
  /// </summary>
  public RadioDeviceOptions Radio { get; set; } = new();

  /// <summary>
  /// Gets or sets the vinyl device options.
  /// </summary>
  public VinylDeviceOptions Vinyl { get; set; } = new();

}

/// <summary>
/// Configuration options for the radio USB device (Raddy RF320).
/// </summary>
public class RadioDeviceOptions
{
  /// <summary>
  /// Gets or sets the USB audio device name pattern for the radio.
  /// Matched as a case-insensitive substring against SoundFlow capture device names.
  /// Example: "AB13X" matches "AB13X USB Audio".
  /// </summary>
  public string USBPort { get; set; } = "";
}

/// <summary>
/// Configuration options for the vinyl turntable USB device.
/// </summary>
public class VinylDeviceOptions
{
  /// <summary>
  /// Gets or sets the USB audio device name pattern for the turntable.
  /// Matched as a case-insensitive substring against SoundFlow capture device names.
  /// </summary>
  public string USBPort { get; set; } = "";
}


