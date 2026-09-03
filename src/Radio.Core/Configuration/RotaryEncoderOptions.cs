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

  /// <summary>
  /// <b>Volume points per device unit of movement</b> (percentage points of full scale, 0-100).
  ///
  /// <para>
  /// The device has already applied its own <c>step_size</c> and acceleration tier by the time a
  /// movement reaches the host, so this multiplies <i>units the device computed</i>, not detents.
  /// At 1 the two quantities are the same thing — one unit is one point — which is what makes a
  /// tier multiplier readable as points per detent with no further arithmetic.
  /// </para>
  ///
  /// <para>
  /// ENC-20 lowered it from 2. Paired with <c>step_size 2</c> on the VOLUME channel it made a
  /// single base detent move volume by 4 points, and it invited every downstream comment to count
  /// device units as though they were already points.
  /// </para>
  /// </summary>
  public int VolumeStepPercent { get; set; } = 1;

  /// <summary>Delay before attempting device reconnection in milliseconds.</summary>
  public int ReconnectDelayMs { get; set; } = 2000;
}
