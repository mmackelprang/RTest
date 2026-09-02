using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Encodes and decodes the RotaryUsb configuration report (Input/Output Report <c>0x02</c>, 106-byte
/// payload) and builds the two-byte command report (<c>0x03</c>).
///
/// <para>
/// Verified against the live device's HID report descriptor (2026-09-02: report <c>0x02</c> is 106 bytes
/// in and 106 out, report <c>0x03</c> is 2 bytes out) and <c>RotaryUsb/docs/INTEGRATION.md</c> §4. All
/// values little-endian. Offsets below are <b>payload</b> offsets — the buffer index is offset + 1,
/// because byte 0 carries the report ID.
/// </para>
///
/// <para>
/// Separated from the I/O for the same reason as <see cref="RotaryEncoderDecoder"/>: the wire format is
/// the part with sharp edges, and it can be round-tripped in a unit test without hardware.
/// </para>
/// </summary>
internal static class RotaryEncoderConfigCodec
{
  public const byte ReportIdConfig = 0x02;
  public const byte ReportIdCommand = 0x03;

  /// <summary>Payload size of the configuration report.</summary>
  public const int ConfigPayloadSize = 106;

  /// <summary>Payload size of the command report.</summary>
  public const int CommandPayloadSize = 2;

  private const int GlobalHeaderSize = 2;      // version + global flags
  private const int EncoderBlockSize = 26;

  /// <summary>Bit 0 of the global flags byte: 0 = 4 steps/detent, 1 = 2 steps/detent.</summary>
  private const byte TwoStepsPerDetentFlag = 0b1;

  /// <summary>
  /// Serialises <paramref name="config"/> into a full report buffer — report ID followed by the
  /// 106-byte payload, ready to write.
  /// </summary>
  public static byte[] Encode(RotaryEncoderDeviceConfig config)
  {
    ArgumentNullException.ThrowIfNull(config);
    ValidateShape(config);

    var report = new byte[ConfigPayloadSize + 1];
    report[0] = ReportIdConfig;

    Span<byte> payload = report.AsSpan(1);
    payload[0] = config.Version;

    // Only bit 0 is defined; the rest stay zero rather than carrying whatever we were handed.
    payload[1] = config.StepsPerDetent == 2 ? TwoStepsPerDetentFlag : (byte)0;

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      RotaryEncoderChannelConfig channel = config.Encoders[i];
      Span<byte> block = payload.Slice(GlobalHeaderSize + (i * EncoderBlockSize), EncoderBlockSize);

      BitConverter.TryWriteBytes(block[..4], channel.MinValue);
      BitConverter.TryWriteBytes(block.Slice(4, 4), channel.MaxValue);
      BitConverter.TryWriteBytes(block.Slice(8, 4), channel.StepSize);
      block[12] = channel.Wrap ? (byte)1 : (byte)0;
      block[13] = channel.Reverse ? (byte)1 : (byte)0;

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        RotaryEncoderAccelerationTier tier = channel.Tiers[t];
        BitConverter.TryWriteBytes(block.Slice(14 + (t * 4), 2), tier.ThresholdMs);
        BitConverter.TryWriteBytes(block.Slice(16 + (t * 4), 2), tier.Multiplier);
      }
    }

    return report;
  }

  /// <summary>
  /// Parses a configuration report the device sent back. Returns false for anything that is not a
  /// complete <c>0x02</c> report.
  /// </summary>
  /// <remarks>
  /// A read-back is not optional politeness: the device <b>silently rejects</b> bad config, so a write
  /// that appeared to succeed and did not is indistinguishable from one that worked until the volume
  /// knob behaves like it is on factory tiers.
  /// </remarks>
  public static bool TryDecode(byte[] report, int bytesRead, out RotaryEncoderDeviceConfig config)
  {
    config = new RotaryEncoderDeviceConfig();

    if (report is null || bytesRead < ConfigPayloadSize + 1 || report[0] != ReportIdConfig)
    {
      return false;
    }

    ReadOnlySpan<byte> payload = report.AsSpan(1, ConfigPayloadSize);

    config.Version = payload[0];
    config.StepsPerDetent = (payload[1] & TwoStepsPerDetentFlag) != 0 ? 2 : 4;

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      ReadOnlySpan<byte> block = payload.Slice(GlobalHeaderSize + (i * EncoderBlockSize), EncoderBlockSize);

      var channel = new RotaryEncoderChannelConfig
      {
        MinValue = BitConverter.ToInt32(block[..4]),
        MaxValue = BitConverter.ToInt32(block.Slice(4, 4)),
        StepSize = BitConverter.ToInt32(block.Slice(8, 4)),
        Wrap = block[12] != 0,
        Reverse = block[13] != 0,
      };

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        channel.Tiers[t] = new RotaryEncoderAccelerationTier
        {
          ThresholdMs = BitConverter.ToUInt16(block.Slice(14 + (t * 4), 2)),
          Multiplier = BitConverter.ToUInt16(block.Slice(16 + (t * 4), 2)),
        };
      }

      config.Encoders[i] = channel;
    }

    return true;
  }

  /// <summary>Builds the two-byte command report. Byte 1 is reserved and sent as <c>0x00</c>.</summary>
  public static byte[] EncodeCommand(RotaryEncoderCommand command) =>
    [ReportIdCommand, (byte)command, 0x00];

  /// <summary>
  /// Compares a pushed config against what the device reported back, ignoring nothing.
  ///
  /// <para>
  /// Deliberately not <c>Equals</c>: this is the verification step of a push, and the caller wants a
  /// yes/no about the bytes that were actually accepted, not structural equality of two objects that
  /// might differ in ways the wire cannot express.
  /// </para>
  /// </summary>
  public static bool Matches(RotaryEncoderDeviceConfig pushed, RotaryEncoderDeviceConfig readBack)
  {
    ArgumentNullException.ThrowIfNull(pushed);
    ArgumentNullException.ThrowIfNull(readBack);

    return Encode(pushed).AsSpan().SequenceEqual(Encode(readBack));
  }

  private static void ValidateShape(RotaryEncoderDeviceConfig config)
  {
    if (config.Encoders is null || config.Encoders.Length != RotaryEncoderDeviceConfig.EncoderCount)
    {
      throw new ArgumentException(
        $"Config must carry exactly {RotaryEncoderDeviceConfig.EncoderCount} encoder blocks.",
        nameof(config));
    }

    for (int i = 0; i < config.Encoders.Length; i++)
    {
      RotaryEncoderChannelConfig? channel = config.Encoders[i];
      if (channel?.Tiers is null || channel.Tiers.Length != RotaryEncoderDeviceConfig.TiersPerEncoder)
      {
        throw new ArgumentException(
          $"Encoder {i} must carry exactly {RotaryEncoderDeviceConfig.TiersPerEncoder} acceleration tiers.",
          nameof(config));
      }
    }
  }
}
