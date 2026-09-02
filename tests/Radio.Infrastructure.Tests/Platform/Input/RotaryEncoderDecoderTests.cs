using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Wire-protocol tests for <see cref="RotaryEncoderDecoder"/> (ENC-1).
///
/// <para>
/// The shipped decoder read an 8-byte report with <c>sbyte</c> deltas. The device sends 37 bytes
/// with <c>int32</c> positions and free-running accumulators, so every value the old parser produced
/// was garbage and every UX behaviour built on it was built on garbage. These tests pin the real
/// layout, verified against the live device's HID report descriptor on 2026-09-02.
/// </para>
/// </summary>
public class RotaryEncoderDecoderTests
{
  private const int ReportLength = RotaryEncoderDecoder.PositionPayloadSize + 1;   // 37

  /// <summary>Builds a report 0x01 with the given positions, button mask and movement values.</summary>
  private static byte[] Report(int[]? positions = null, byte buttons = 0, int[]? movement = null)
  {
    var data = new byte[ReportLength];
    data[0] = RotaryEncoderDecoder.ReportIdPositions;

    positions ??= new int[4];
    movement ??= new int[4];

    for (int i = 0; i < 4; i++)
    {
      BitConverter.GetBytes(positions[i]).CopyTo(data, 1 + (i * 4));    // payload 0-15
      BitConverter.GetBytes(movement[i]).CopyTo(data, 21 + (i * 4));    // payload 20-35
    }

    data[17] = buttons;                                                 // payload 16
    return data;
  }

  private static RotaryEncoderDecoder Connected()
  {
    var decoder = new RotaryEncoderDecoder();
    decoder.BeginConnection(ReportLength);
    return decoder;
  }

  [Fact]
  public void Decode_ReadsPositionsFromPayloadOffsetZero()
  {
    var decoder = Connected();

    Assert.True(decoder.Decode(Report(positions: [1, -2, 100000, int.MinValue]), ReportLength));

    Assert.Equal(1, decoder.GetPosition(0));
    Assert.Equal(-2, decoder.GetPosition(1));
    Assert.Equal(100000, decoder.GetPosition(2));
    Assert.Equal(int.MinValue, decoder.GetPosition(3));
  }

  [Fact]
  public void Decode_ReadsButtonBitmaskFromPayloadOffsetSixteen()
  {
    var decoder = Connected();

    // Encoders 0 and 3 pressed.
    decoder.Decode(Report(buttons: 0b1001), ReportLength);

    Assert.True(decoder.ButtonChanges[0]);
    Assert.Null(decoder.ButtonChanges[1]);
    Assert.Null(decoder.ButtonChanges[2]);
    Assert.True(decoder.ButtonChanges[3]);
  }

  [Fact]
  public void Decode_ReportsButtonEdgesOnly_NotLevels()
  {
    var decoder = Connected();

    decoder.Decode(Report(buttons: 0b0001), ReportLength);
    Assert.True(decoder.ButtonChanges[0]);

    // Same mask again: held, not re-pressed.
    decoder.Decode(Report(buttons: 0b0001), ReportLength);
    Assert.Null(decoder.ButtonChanges[0]);

    decoder.Decode(Report(buttons: 0b0000), ReportLength);
    Assert.False(decoder.ButtonChanges[0]);
  }

  [Fact]
  public void Decode_FirstReportAfterConnect_IsABaselineNotAnInput()
  {
    // The single most important behaviour in this file. The accumulator is a running total since
    // power-on, so the first sample carries the device's whole history — which is not input.
    var decoder = Connected();

    decoder.Decode(Report(movement: [5000, 5000, 5000, 5000]), ReportLength);

    Assert.All(decoder.Deltas, d => Assert.Equal(0, d));
    Assert.True(decoder.IsBaselined);
  }

  [Fact]
  public void Decode_SecondReport_YieldsTheDifference()
  {
    var decoder = Connected();

    decoder.Decode(Report(movement: [100, 100, 100, 100]), ReportLength);
    decoder.Decode(Report(movement: [103, 98, 100, 100]), ReportLength);

    Assert.Equal(3, decoder.Deltas[0]);
    Assert.Equal(-2, decoder.Deltas[1]);
    Assert.Equal(0, decoder.Deltas[2]);
  }

  [Fact]
  public void Decode_AfterReconnect_DoesNotReplayMovementMadeWhileDisconnected()
  {
    // Designer's test, verbatim: "Turn a knob ~50 detents while unplugged, then replug: volume does
    // not jump." Diff-against-last-remembered-value is the obvious way to write this decoder and it
    // is wrong — it delivers an entire outage as one delta, on the volume knob.
    var decoder = Connected();

    decoder.Decode(Report(movement: [1000, 0, 0, 0]), ReportLength);   // baseline
    decoder.Decode(Report(movement: [1002, 0, 0, 0]), ReportLength);   // two real detents
    Assert.Equal(2, decoder.Deltas[0]);

    // Unplugged. The device keeps counting: ~50 detents go by unobserved.
    decoder.BeginConnection(ReportLength);

    decoder.Decode(Report(movement: [1052, 0, 0, 0]), ReportLength);

    Assert.Equal(0, decoder.Deltas[0]);   // the outage is absorbed, not replayed

    decoder.Decode(Report(movement: [1053, 0, 0, 0]), ReportLength);
    Assert.Equal(1, decoder.Deltas[0]);   // and normal service resumes
  }

  [Fact]
  public void Decode_AccumulatorWrap_ProducesTheSmallDelta_NotFourBillion()
  {
    // The accumulator wraps at 32 bits by design rather than saturating. Checked arithmetic would
    // overflow; naive widening would report ~4.29e9 and slam the volume.
    var decoder = Connected();

    decoder.Decode(Report(movement: [int.MaxValue - 1, 0, 0, 0]), ReportLength);
    decoder.Decode(Report(movement: [unchecked(int.MaxValue + 3), 0, 0, 0]), ReportLength);

    Assert.Equal(4, decoder.Deltas[0]);
  }

  [Fact]
  public void Decode_LegacyReport_ParsesPositionsAndButtons_ButYieldsNoMovement()
  {
    // Pre-accumulator firmware sends a 22-byte report. Payload bytes 0-17 are identical, so
    // positions and buttons must still work rather than the decoder refusing the device.
    var decoder = new RotaryEncoderDecoder();
    decoder.BeginConnection(RotaryEncoderDecoder.LegacyPositionPayloadSize + 1);

    Assert.False(decoder.HasMovement);

    var legacy = new byte[RotaryEncoderDecoder.LegacyPositionPayloadSize + 1];
    legacy[0] = RotaryEncoderDecoder.ReportIdPositions;
    BitConverter.GetBytes(42).CopyTo(legacy, 1);
    legacy[17] = 0b0010;

    Assert.True(decoder.Decode(legacy, legacy.Length));
    Assert.Equal(42, decoder.GetPosition(0));
    Assert.True(decoder.ButtonChanges[1]);
    Assert.All(decoder.Deltas, d => Assert.Equal(0, d));
  }

  [Theory]
  [InlineData(0x02)]   // configuration
  [InlineData(0x03)]   // command
  [InlineData(0x04)]   // diagnostics
  public void Decode_IgnoresOtherReportIds(byte reportId)
  {
    // 0x02 and 0x04 arrive on the same endpoint. Treating them as positions would decode config
    // bytes as coordinates.
    var decoder = Connected();
    var data = Report(movement: [9999, 9999, 9999, 9999]);
    data[0] = reportId;

    Assert.False(decoder.Decode(data, data.Length));
    Assert.False(decoder.IsBaselined);
  }

  [Fact]
  public void Decode_RejectsShortReport()
  {
    var decoder = Connected();
    Assert.False(decoder.Decode(new byte[] { RotaryEncoderDecoder.ReportIdPositions, 0, 0 }, 3));
  }
}
