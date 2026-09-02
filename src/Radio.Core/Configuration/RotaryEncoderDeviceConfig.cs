namespace Radio.Core.Configuration;

/// <summary>
/// One acceleration tier. "Turn faster than <see cref="ThresholdMs"/> between detents and each detent
/// moves <c>step_size × multiplier</c>."
/// </summary>
/// <remarks>A tier with <see cref="ThresholdMs"/> of 0 is disabled.</remarks>
public sealed class RotaryEncoderAccelerationTier
{
  /// <summary>Inter-detent interval below which this tier engages, in milliseconds. 0 disables it.</summary>
  public ushort ThresholdMs { get; set; }

  /// <summary>Multiplier applied to <see cref="RotaryEncoderChannelConfig.StepSize"/> in this tier.</summary>
  public ushort Multiplier { get; set; }
}

/// <summary>
/// Per-encoder device configuration — one 26-byte block of the device's 106-byte config report.
/// </summary>
public sealed class RotaryEncoderChannelConfig
{
  /// <summary>Lower bound of the clamped position.</summary>
  public int MinValue { get; set; }

  /// <summary>Upper bound of the clamped position.</summary>
  public int MaxValue { get; set; }

  /// <summary>Position change per detent, before any acceleration multiplier.</summary>
  public int StepSize { get; set; } = 1;

  /// <summary>When true, position wraps at the bounds instead of clamping. Movement is unaffected.</summary>
  public bool Wrap { get; set; }

  /// <summary>When true, the device inverts the direction. Movement respects this too, so its sign
  /// always agrees with position.</summary>
  public bool Reverse { get; set; }

  /// <summary>The three acceleration tiers, in device order. Always exactly three.</summary>
  public RotaryEncoderAccelerationTier[] Tiers { get; set; } =
  [
    new RotaryEncoderAccelerationTier(),
    new RotaryEncoderAccelerationTier(),
    new RotaryEncoderAccelerationTier(),
  ];
}

/// <summary>
/// The device's full configuration — Input/Output Report <c>0x02</c>, 106 bytes.
///
/// <para>
/// ENC-2. There was no host→device config path at all before this: <see cref="RotaryEncoderOptions"/>
/// has eight flat fields and cannot express per-encoder bounds or acceleration tiers, so the device ran
/// on whatever was in its flash — which on a fresh, reset or replacement Pico is factory defaults.
/// </para>
///
/// <para>
/// ⚠ <b>Bad config is silently rejected by the device.</b> A write that appears to succeed and did not
/// is how the volume knob ends up on factory tiers, so a push is only complete once it has been read
/// back and compared. That loop is <c>ENC-11</c>; this type and its codec are the transport it needs.
/// </para>
/// </summary>
public sealed class RotaryEncoderDeviceConfig
{
  /// <summary>Number of encoders the protocol carries.</summary>
  public const int EncoderCount = 4;

  /// <summary>Acceleration tiers per encoder.</summary>
  public const int TiersPerEncoder = 3;

  /// <summary>The only config version this codec understands.</summary>
  public const byte SupportedVersion = 0x01;

  /// <summary>Config version byte. Currently <c>0x01</c>.</summary>
  public byte Version { get; set; } = SupportedVersion;

  /// <summary>
  /// Steps the decoder takes per detent — 4 or 2, carried as bit 0 of the global flags byte
  /// (<c>0</c> = 4, <c>1</c> = 2).
  /// </summary>
  /// <remarks>
  /// This is the value the firmware is asked to use. What it is <i>actually</i> using is reported
  /// separately in diagnostics report <c>0x04</c>, which is the field to compare against when
  /// confirming a write took effect.
  /// </remarks>
  public int StepsPerDetent { get; set; } = 4;

  /// <summary>Per-encoder blocks, in device order. Always exactly <see cref="EncoderCount"/>.</summary>
  public RotaryEncoderChannelConfig[] Encoders { get; set; } =
  [
    new RotaryEncoderChannelConfig(),
    new RotaryEncoderChannelConfig(),
    new RotaryEncoderChannelConfig(),
    new RotaryEncoderChannelConfig(),
  ];
}

/// <summary>
/// Output Report <c>0x03</c> commands. Byte 0 is the command; byte 1 is reserved and must be <c>0x00</c>.
/// </summary>
public enum RotaryEncoderCommand : byte
{
  /// <summary>Persist the current config to flash.</summary>
  SaveConfig = 0x01,

  /// <summary>Factory config <b>and</b> reset positions. Movement accumulators are untouched.</summary>
  ResetDefaults = 0x02,

  /// <summary>
  /// Set every position to its <c>min_value</c>. Movement accumulators are untouched.
  ///
  /// <para>
  /// ⚠ This is the <b>only</b> host-side position control the protocol offers — there is no
  /// set-position command. That is why accumulator semantics are forced by the protocol rather than
  /// merely preferred, and why this is the documented recovery for a knob whose reported values look
  /// wrong.
  /// </para>
  /// </summary>
  ResetPositions = 0x03,

  /// <summary>Ask the device to emit one Input Report <c>0x02</c> carrying its live config.</summary>
  ReadConfig = 0x04,

  /// <summary>Zero every counter in diagnostics report <c>0x04</c>.</summary>
  ResetDiagnostics = 0x05,
}
