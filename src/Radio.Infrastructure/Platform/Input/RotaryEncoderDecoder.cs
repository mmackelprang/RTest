namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Decodes RotaryUsb Input Report <c>0x01</c> and turns the device's free-running movement
/// accumulators into per-report deltas.
///
/// <para>
/// Separated from <see cref="HidRotaryEncoderService"/> so the wire protocol can be tested without
/// hardware. The protocol is the part with the sharp edges; the I/O around it is ordinary.
/// </para>
///
/// <para>
/// Verified against the live device's HID report descriptor (2026-09-02) and
/// <c>RotaryUsb/docs/INTEGRATION.md</c> §4. All values little-endian. Offsets below are
/// <i>payload</i> offsets — the buffer index is offset + 1, because byte 0 is the report ID.
/// </para>
/// </summary>
internal sealed class RotaryEncoderDecoder
{
  public const byte ReportIdPositions = 0x01;

  /// <summary>Payload size of the current firmware's report 0x01 (movement accumulators present).</summary>
  public const int PositionPayloadSize = 36;

  /// <summary>Payload size of pre-accumulator firmware. Positions and buttons parse identically.</summary>
  public const int LegacyPositionPayloadSize = 21;

  public const int EncoderCount = 4;

  private readonly int[] _movementLast = new int[EncoderCount];
  private readonly bool[] _buttonStates = new bool[EncoderCount];
  private readonly int[] _positions = new int[EncoderCount];

  /// <summary>
  /// Per-encoder movement since the previous report. Valid only when <see cref="Decode"/> returned
  /// true. Entries are 0 where the encoder did not move.
  /// </summary>
  public int[] Deltas { get; } = new int[EncoderCount];

  /// <summary>
  /// Per-encoder button transitions for the last decoded report: null where unchanged, otherwise
  /// the new pressed state.
  /// </summary>
  public bool?[] ButtonChanges { get; } = new bool?[EncoderCount];

  /// <summary>True when this firmware sends the movement accumulators.</summary>
  public bool HasMovement { get; private set; }

  /// <summary>True once the first report of the current connection has been absorbed.</summary>
  public bool IsBaselined { get; private set; }

  /// <summary>
  /// Resets per-connection state. <b>Must be called on every connect and reconnect.</b>
  ///
  /// <para>
  /// The device's movement accumulator is a running total since power-on that keeps accruing while
  /// nothing is listening. Differencing a fresh sample against a value remembered from before a
  /// disconnect delivers the entire outage as one delta — on the volume knob. So the first report
  /// after a connect is a <b>baseline, not an input</b>. Designer's test, verbatim: <i>"Turn a knob
  /// ~50 detents while unplugged, then replug: volume does not jump."</i>
  /// </para>
  ///
  /// <para>
  /// <paramref name="reportLength"/> is the device's max input report length <b>including</b> the
  /// report ID. Movement support is detected from it rather than from a version field, because the
  /// protocol has none.
  /// </para>
  /// </summary>
  public void BeginConnection(int reportLength)
  {
    HasMovement = reportLength >= PositionPayloadSize + 1;
    IsBaselined = false;
    Array.Clear(_movementLast);
    Array.Clear(_positions);
    Array.Clear(_buttonStates);
  }

  /// <summary>Latest clamped position for an encoder, in device units.</summary>
  public int GetPosition(int encoderIndex) => _positions[encoderIndex];

  /// <summary>
  /// Decodes one report. Returns false for anything that is not a positions report — reports 0x02
  /// (config) and 0x04 (diagnostics) arrive on the same endpoint and are ENC-2's business.
  /// </summary>
  public bool Decode(byte[] data, int bytesRead)
  {
    Array.Clear(Deltas);
    Array.Clear(ButtonChanges);

    if (data is null || bytesRead < LegacyPositionPayloadSize + 1 || data[0] != ReportIdPositions)
    {
      return false;
    }

    // Payload 0-15: clamped positions.
    for (int i = 0; i < EncoderCount; i++)
    {
      _positions[i] = BitConverter.ToInt32(data, 1 + (i * 4));
    }

    // Payload 16: button bitmask, bit n = encoder n.
    byte buttonByte = data[17];
    for (int i = 0; i < EncoderCount; i++)
    {
      bool isPressed = (buttonByte & (1 << i)) != 0;
      if (isPressed != _buttonStates[i])
      {
        _buttonStates[i] = isPressed;
        ButtonChanges[i] = isPressed;
      }
    }

    // Payload 20-35: movement accumulators. Absent on pre-accumulator firmware, where everything
    // above still parsed correctly.
    if (!HasMovement || bytesRead < PositionPayloadSize + 1)
    {
      return true;
    }

    for (int i = 0; i < EncoderCount; i++)
    {
      int current = BitConverter.ToInt32(data, 21 + (i * 4));

      if (IsBaselined)
      {
        // unchecked: the accumulator wraps at 32 bits by design rather than saturating, and
        // two's-complement subtraction gives the correct signed delta straight across the wrap.
        // A saturating design would have frozen the control after ~119 hours of fast spinning.
        Deltas[i] = unchecked(current - _movementLast[i]);
      }

      _movementLast[i] = current;
    }

    IsBaselined = true;
    return true;
  }
}
