namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for audio device settings.
/// Loaded from the 'Devices' configuration section.
/// </summary>
/// <remarks>
/// Deployed configuration may still carry a <c>Devices:Radio</c> entry from the removed RF320 USB
/// radio (in <c>appsettings.Production.json</c>, and as <c>devices:radio</c> / <c>devices:Radio</c>
/// rows in the SQLite config store). This type has no property for it, and the default
/// configuration binder ignores keys with no matching property, so those entries are inert.
/// </remarks>
public class DeviceOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Devices";

  /// <summary>
  /// Gets or sets the vinyl device options.
  /// </summary>
  public VinylDeviceOptions Vinyl { get; set; } = new();

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


