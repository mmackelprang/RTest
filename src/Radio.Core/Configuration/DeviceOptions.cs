namespace Radio.Core.Configuration;

using Radio.Core.Models.Audio;

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

  /// <summary>
  /// Gets or sets the Spotify device options.
  /// </summary>
  public SpotifyDeviceOptions Spotify { get; set; } = new();
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

/// <summary>
/// Configuration options for Spotify audio integration.
/// </summary>
public class SpotifyDeviceOptions
{
  /// <summary>
  /// Gets or sets the Spotify integration mode.
  /// RemoteControl: Uses Spotify Connect API (no audio data flows through app).
  /// Loopback: Captures audio from Spotify client via virtual/loopback device.
  /// Integrated: Manages librespot process and captures audio via pipe.
  /// </summary>
  public SpotifyMode Mode { get; set; } = SpotifyMode.Loopback;

  /// <summary>
  /// Gets or sets the loopback/virtual audio device name for audio capture.
  /// Windows: "CABLE Output" (VB-Audio Virtual Cable), "Stereo Mix"
  /// Linux: "hw:Loopback,0,0" (ALSA loopback)
  /// Only used when Mode is Loopback.
  /// </summary>
  public string LoopbackDeviceName { get; set; } = "CABLE Output";

  /// <summary>
  /// Gets or sets the path to the librespot executable.
  /// Used when Mode is Integrated.
  /// Example: "/usr/bin/librespot" (Linux) or "C:\\librespot\\librespot.exe" (Windows)
  /// </summary>
  public string LibrespotPath { get; set; } = "/usr/bin/librespot";
}
