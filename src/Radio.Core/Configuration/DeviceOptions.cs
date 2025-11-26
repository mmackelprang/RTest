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

  /// <summary>
  /// Gets or sets the cast device options.
  /// </summary>
  public CastDeviceOptions Cast { get; set; } = new();
}

/// <summary>
/// Configuration options for the radio USB device (Raddy RF320).
/// </summary>
public class RadioDeviceOptions
{
  /// <summary>
  /// Gets or sets the USB port path for the radio device.
  /// </summary>
  public string USBPort { get; set; } = "/dev/ttyUSB0";
}

/// <summary>
/// Configuration options for the vinyl turntable USB device.
/// </summary>
public class VinylDeviceOptions
{
  /// <summary>
  /// Gets or sets the USB port path for the vinyl device.
  /// </summary>
  public string USBPort { get; set; } = "/dev/ttyUSB1";
}

/// <summary>
/// Configuration options for Chromecast audio output.
/// </summary>
public class CastDeviceOptions
{
  /// <summary>
  /// Gets or sets the default Chromecast device name.
  /// </summary>
  public string DefaultDevice { get; set; } = "";
}
