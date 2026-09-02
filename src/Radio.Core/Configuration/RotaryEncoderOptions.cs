namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for rotary encoder hardware input (KY-040 encoders on Pico via USB HID).
/// Loaded from the 'RotaryEncoder' configuration section.
/// </summary>
public class RotaryEncoderOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "RotaryEncoder";

  /// <summary>
  /// Escape hatch for disabling encoder input entirely.
  ///
  /// <para>
  /// ENC-0 changed both the default and the meaning. This used to be a gate that had to be opened
  /// before the subsystem would run; now <b>presence decides</b>, and this exists only so a
  /// misbehaving encoder can be switched off without crawling behind the furniture.
  /// </para>
  ///
  /// <para>
  /// When false, presence detection is <b>silent about everything</b> — no status, no badge, no
  /// toast. The owner turned the knobs off deliberately and must not be nagged about the consequence.
  /// </para>
  /// </summary>
  public bool Enabled { get; set; } = true;

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
