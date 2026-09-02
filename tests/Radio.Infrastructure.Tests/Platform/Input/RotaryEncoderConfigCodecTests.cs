using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Wire-format tests for the RotaryUsb configuration report (ENC-2).
///
/// <para>
/// These matter more than round-tripping usually does. The device <b>silently rejects</b> bad config —
/// a write that appeared to succeed and did not is indistinguishable from one that worked, until the
/// volume knob behaves as though it is on factory tiers. The byte layout has to be right by
/// construction, because the failure mode gives no signal.
/// </para>
/// </summary>
public class RotaryEncoderConfigCodecTests
{
  private static RotaryEncoderDeviceConfig SampleConfig()
  {
    var config = new RotaryEncoderDeviceConfig { Version = 1, StepsPerDetent = 4 };

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      config.Encoders[i] = new RotaryEncoderChannelConfig
      {
        MinValue = i * -100,
        MaxValue = 1000 + i,
        StepSize = i + 1,
        Wrap = i % 2 == 0,
        Reverse = i % 3 == 0,
        Tiers =
        [
          new RotaryEncoderAccelerationTier { ThresholdMs = (ushort)(50 + i), Multiplier = 2 },
          new RotaryEncoderAccelerationTier { ThresholdMs = (ushort)(25 + i), Multiplier = 5 },
          new RotaryEncoderAccelerationTier { ThresholdMs = 0, Multiplier = 0 },
        ],
      };
    }

    return config;
  }

  [Fact]
  public void Encode_ProducesAReportOfExactlyTheDeclaredSize()
  {
    // The descriptor declares 106 bytes for report 0x02. A short or long buffer is rejected by the
    // device without explanation.
    byte[] report = RotaryEncoderConfigCodec.Encode(SampleConfig());

    Assert.Equal(RotaryEncoderConfigCodec.ConfigPayloadSize + 1, report.Length);
    Assert.Equal(RotaryEncoderConfigCodec.ReportIdConfig, report[0]);
  }

  [Fact]
  public void EncodeThenDecode_RoundTripsEveryField()
  {
    RotaryEncoderDeviceConfig original = SampleConfig();

    byte[] report = RotaryEncoderConfigCodec.Encode(original);
    Assert.True(RotaryEncoderConfigCodec.TryDecode(report, report.Length, out var decoded));

    Assert.Equal(original.Version, decoded.Version);
    Assert.Equal(original.StepsPerDetent, decoded.StepsPerDetent);

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      Assert.Equal(original.Encoders[i].MinValue, decoded.Encoders[i].MinValue);
      Assert.Equal(original.Encoders[i].MaxValue, decoded.Encoders[i].MaxValue);
      Assert.Equal(original.Encoders[i].StepSize, decoded.Encoders[i].StepSize);
      Assert.Equal(original.Encoders[i].Wrap, decoded.Encoders[i].Wrap);
      Assert.Equal(original.Encoders[i].Reverse, decoded.Encoders[i].Reverse);

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        Assert.Equal(original.Encoders[i].Tiers[t].ThresholdMs, decoded.Encoders[i].Tiers[t].ThresholdMs);
        Assert.Equal(original.Encoders[i].Tiers[t].Multiplier, decoded.Encoders[i].Tiers[t].Multiplier);
      }
    }
  }

  [Fact]
  public void Encode_PlacesFieldsAtTheDocumentedOffsets()
  {
    // Pins the layout against INTEGRATION.md section 4 rather than only against itself: a round-trip
    // test passes just as happily if encode and decode are wrong in the same way.
    var config = new RotaryEncoderDeviceConfig { Version = 0x01, StepsPerDetent = 4 };
    config.Encoders[1] = new RotaryEncoderChannelConfig
    {
      MinValue = 0x11223344,
      MaxValue = 0x55667788,
      StepSize = 0x0A0B0C0D,
      Wrap = true,
      Reverse = false,
      Tiers =
      [
        new RotaryEncoderAccelerationTier { ThresholdMs = 0x1234, Multiplier = 0x5678 },
        new RotaryEncoderAccelerationTier(),
        new RotaryEncoderAccelerationTier(),
      ],
    };

    byte[] r = RotaryEncoderConfigCodec.Encode(config);
    ReadOnlySpan<byte> payload = r.AsSpan(1);

    Assert.Equal(0x01, payload[0]);                      // version
    Assert.Equal(0x00, payload[1]);                      // global flags, bit0 clear = 4 steps/detent

    // Encoder 2's block starts at payload offset 28 (2 header + 1 * 26).
    ReadOnlySpan<byte> block = payload.Slice(28, 26);
    Assert.Equal(0x11223344, BitConverter.ToInt32(block[..4]));
    Assert.Equal(0x55667788, BitConverter.ToInt32(block.Slice(4, 4)));
    Assert.Equal(0x0A0B0C0D, BitConverter.ToInt32(block.Slice(8, 4)));
    Assert.Equal(1, block[12]);                                       // wrap
    Assert.Equal(0, block[13]);                                       // reverse
    Assert.Equal(0x1234, BitConverter.ToUInt16(block.Slice(14, 2)));  // tier 1 threshold
    Assert.Equal(0x5678, BitConverter.ToUInt16(block.Slice(16, 2)));  // tier 1 multiplier
  }

  [Theory]
  [InlineData(4, 0b0)]
  [InlineData(2, 0b1)]
  public void Encode_MapsStepsPerDetentOntoBitZeroOfTheGlobalFlags(int stepsPerDetent, byte expectedFlags)
  {
    var config = new RotaryEncoderDeviceConfig { StepsPerDetent = stepsPerDetent };

    byte[] r = RotaryEncoderConfigCodec.Encode(config);

    Assert.Equal(expectedFlags, r[2]);   // payload offset 1
  }

  [Fact]
  public void TryDecode_RejectsWrongReportIdAndShortBuffers()
  {
    byte[] good = RotaryEncoderConfigCodec.Encode(SampleConfig());

    var wrongId = (byte[])good.Clone();
    wrongId[0] = 0x04;                       // diagnostics shares the endpoint
    Assert.False(RotaryEncoderConfigCodec.TryDecode(wrongId, wrongId.Length, out _));

    Assert.False(RotaryEncoderConfigCodec.TryDecode(good, good.Length - 1, out _));
    Assert.False(RotaryEncoderConfigCodec.TryDecode(null!, 107, out _));
  }

  [Fact]
  public void Matches_IsTrueOnlyWhenTheEncodedBytesAgree()
  {
    RotaryEncoderDeviceConfig pushed = SampleConfig();
    RotaryEncoderDeviceConfig same = SampleConfig();
    Assert.True(RotaryEncoderConfigCodec.Matches(pushed, same));

    RotaryEncoderDeviceConfig drifted = SampleConfig();
    drifted.Encoders[2].Tiers[0].Multiplier = 99;
    Assert.False(RotaryEncoderConfigCodec.Matches(pushed, drifted));
  }

  [Theory]
  [InlineData(RotaryEncoderCommand.SaveConfig, 0x01)]
  [InlineData(RotaryEncoderCommand.ResetDefaults, 0x02)]
  [InlineData(RotaryEncoderCommand.ResetPositions, 0x03)]
  [InlineData(RotaryEncoderCommand.ReadConfig, 0x04)]
  [InlineData(RotaryEncoderCommand.ResetDiagnostics, 0x05)]
  public void EncodeCommand_ProducesReportIdCommandAndReservedZero(RotaryEncoderCommand cmd, byte code)
  {
    byte[] r = RotaryEncoderConfigCodec.EncodeCommand(cmd);

    Assert.Equal(3, r.Length);
    Assert.Equal(RotaryEncoderConfigCodec.ReportIdCommand, r[0]);
    Assert.Equal(code, r[1]);
    Assert.Equal(0x00, r[2]);            // reserved - the doc requires it be sent as zero
  }

  [Fact]
  public void Encode_Throws_WhenTheShapeCannotFitTheWire()
  {
    var wrongEncoderCount = new RotaryEncoderDeviceConfig
    {
      Encoders = [new RotaryEncoderChannelConfig()],
    };
    Assert.Throws<ArgumentException>(() => RotaryEncoderConfigCodec.Encode(wrongEncoderCount));

    var wrongTierCount = new RotaryEncoderDeviceConfig();
    wrongTierCount.Encoders[0].Tiers = [new RotaryEncoderAccelerationTier()];
    Assert.Throws<ArgumentException>(() => RotaryEncoderConfigCodec.Encode(wrongTierCount));
  }
}
