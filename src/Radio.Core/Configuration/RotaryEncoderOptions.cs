namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for rotary encoder hardware input (KY-040 encoders on Pico via USB HID).
/// Loaded from the 'RotaryEncoder' configuration section.
/// </summary>
public class RotaryEncoderOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "RotaryEncoder";

  /// <summary>Enable/disable rotary encoder input.</summary>
  public bool Enabled { get; set; } = false;

  /// <summary>USB HID Vendor ID for the Pico encoder device.</summary>
  public int VendorId { get; set; } = 0xCAFE;

  /// <summary>USB HID Product ID for the Pico encoder device.</summary>
  public int ProductId { get; set; } = 0x4005;

  /// <summary>Optional device path override (auto-detected if empty).</summary>
  public string DevicePath { get; set; } = "";

  /// <summary>HID report polling interval in milliseconds.</summary>
  public int PollIntervalMs { get; set; } = 10;

  /// <summary>Volume change per encoder step (percentage, 0-100).</summary>
  public int VolumeStepPercent { get; set; } = 2;

  /// <summary>Delay before attempting device reconnection in milliseconds.</summary>
  public int ReconnectDelayMs { get; set; } = 2000;
}
