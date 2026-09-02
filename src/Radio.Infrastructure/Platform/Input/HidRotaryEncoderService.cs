using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Reads rotary encoder events from a RotaryUsb device (VID 0xCAFE / PID 0x4005) over USB HID.
///
/// <para>
/// <b>Wire protocol (Input Report 0x01, 36-byte payload).</b> Verified against the live device's HID
/// report descriptor on 2026-09-02 and against <c>RotaryUsb/docs/INTEGRATION.md</c> §4. All values
/// little-endian; offsets are <i>payload</i> offsets, so the buffer index is offset + 1 because the
/// report ID occupies byte 0.
/// </para>
///
/// <list type="table">
///   <item><term>0–15</term><description>int32 × 4 — clamped positions</description></item>
///   <item><term>16</term><description>uint8 — button bitmask, bit <i>n</i> = encoder <i>n</i></description></item>
///   <item><term>17</term><description>uint8 — active acceleration tiers, 2 bits per encoder</description></item>
///   <item><term>18–19</term><description>reserved</description></item>
///   <item><term>20–35</term><description>int32 × 4 — free-running movement accumulators</description></item>
/// </list>
///
/// <para>
/// <b>What the previous implementation got wrong, so it is not reintroduced.</b> It read an 8-byte
/// report and took bytes 1–4 as <c>sbyte</c> deltas and byte 5 as the button mask. The device sends
/// none of that: the report is 37 bytes including the ID, deltas do not appear on the wire at all,
/// and the button mask is at payload 16. Every UX behaviour built on those values was being built on
/// garbage.
/// </para>
///
/// <para>
/// <b>Movement is an odometer, not a delta — and this is the dangerous part.</b> It is a running
/// total since power-on that keeps accruing while nothing is listening. Differencing it against the
/// last value seen <i>before</i> a disconnect would deliver an entire outage as one delta, on the
/// volume knob. So the first report after every connect is a <b>baseline, not an input</b>: recorded
/// and discarded. Designer's test for it, verbatim: <i>"Turn a knob ~50 detents while unplugged,
/// then replug: volume does not jump."</i>
/// </para>
///
/// <para>
/// The accumulator <b>wraps</b> at 32 bits rather than saturating, so the subtraction is
/// <c>unchecked</c> — two's-complement gives the correct signed delta straight across the boundary.
/// A saturating design would have frozen the control after ~119 hours of continuous fast spinning.
/// </para>
///
/// <para>
/// <b>Silence is the idle state, not a fault.</b> The device transmits only when the report contents
/// change. A read that returns nothing means nobody is turning a knob; it must never be treated as a
/// disconnect.
/// </para>
///
/// <para>
/// Movement already includes acceleration (<c>step_size × tier_multiplier</c>) and respects the
/// per-encoder <c>reverse</c> flag, so the host does not reimplement tier logic or direction. Detent
/// density is firmware-owned. ⚠ For a <i>bounded</i> control the device's own clamped
/// <c>position</c> is the better input — see <see cref="GetPosition"/> and the note on
/// <see cref="EncoderTurnedEventArgs"/> consumers in ENC-3/ENC-7.
/// </para>
/// </summary>
public class HidRotaryEncoderService : IRotaryEncoderService
{
  private readonly ILogger<HidRotaryEncoderService> _logger;
  private readonly IOptionsMonitor<RotaryEncoderOptions> _options;
  private CancellationTokenSource? _cts;
  private Task? _readTask;
  private bool _isConnected;
  private bool _disposed;

  // The wire protocol lives in RotaryEncoderDecoder so it can be tested without hardware.
  private readonly RotaryEncoderDecoder _decoder = new();

  public HidRotaryEncoderService(
    ILogger<HidRotaryEncoderService> logger,
    IOptionsMonitor<RotaryEncoderOptions> options)
  {
    _logger = logger;
    _options = options;
  }

  /// <inheritdoc />
  public bool IsConnected => _isConnected;

  /// <inheritdoc />
  public event EventHandler<EncoderTurnedEventArgs>? EncoderTurned;

  /// <inheritdoc />
  public event EventHandler<EncoderButtonEventArgs>? ButtonPressed;

  /// <inheritdoc />
  public event EventHandler<EncoderConnectionEventArgs>? ConnectionChanged;

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(nameof(HidRotaryEncoderService));
    }

    _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _readTask = ReadLoopAsync(_cts.Token);
    _logger.LogInformation("Rotary encoder service started");
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (_cts != null)
    {
      await _cts.CancelAsync();
    }

    if (_readTask != null)
    {
      try
      {
        await _readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
      }
      catch (TimeoutException)
      {
        _logger.LogWarning("Encoder read loop did not stop within timeout");
      }
      catch (OperationCanceledException) { }
    }

    _logger.LogInformation("Rotary encoder service stopped");
  }

  private async Task ReadLoopAsync(CancellationToken cancellationToken)
  {
    var opts = _options.CurrentValue;

    while (!cancellationToken.IsCancellationRequested)
    {
      HidDevice? device = null;
      HidStream? stream = null;

      try
      {
        device = FindDevice(opts);
        if (device == null)
        {
          if (_isConnected)
          {
            _isConnected = false;
            ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs { IsConnected = false });
          }

          await Task.Delay(opts.ReconnectDelayMs, cancellationToken);
          continue;
        }

        stream = device.Open();
        stream.ReadTimeout = Timeout.Infinite;

        if (!_isConnected)
        {
          _isConnected = true;
          _logger.LogInformation("Encoder device connected: {Device}", device.GetProductName());
          ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs { IsConnected = true });
        }

        await ReadFromDeviceAsync(device, stream, cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Encoder device error, will reconnect");

        if (_isConnected)
        {
          _isConnected = false;
          ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs { IsConnected = false });
        }
      }
      finally
      {
        stream?.Dispose();
      }

      if (!cancellationToken.IsCancellationRequested)
      {
        await Task.Delay(opts.ReconnectDelayMs, cancellationToken);
      }
    }

    if (_isConnected)
    {
      _isConnected = false;
      ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs { IsConnected = false });
    }
  }

  private async Task ReadFromDeviceAsync(HidDevice device, HidStream stream, CancellationToken cancellationToken)
  {
    // Size the buffer from the device rather than a constant: report 0x04 (diagnostics) is 56 bytes
    // and larger than the 37-byte positions report, so a 37-byte buffer would truncate it and, on
    // some backends, error rather than skip.
    int reportLength = Math.Max(
      device.GetMaxInputReportLength(),
      RotaryEncoderDecoder.PositionPayloadSize + 1);
    var buffer = new byte[reportLength];

    // Re-baseline lives here, not at the call site, so a reconnect inside the read loop cannot skip
    // it. See RotaryEncoderDecoder.BeginConnection for why that matters.
    _decoder.BeginConnection(reportLength);

    _logger.LogInformation(
      "Encoder report length {Length} bytes (movement accumulators: {HasMovement})",
      reportLength, _decoder.HasMovement);

    while (!cancellationToken.IsCancellationRequested)
    {
      int bytesRead;
      try
      {
        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
      }
      catch (IOException)
      {
        // Device disconnected.
        return;
      }
      catch (TimeoutException)
      {
        // An idle device is silent by design — it transmits only when a report's contents change.
        // A timeout is the normal resting state, not a fault, and must not drop the connection.
        continue;
      }

      ParseReport(buffer, bytesRead);
    }
  }

  private void ParseReport(byte[] data, int bytesRead)
  {
    if (!_decoder.Decode(data, bytesRead))
    {
      return;
    }

    for (int i = 0; i < RotaryEncoderDecoder.EncoderCount; i++)
    {
      bool? pressed = _decoder.ButtonChanges[i];
      if (pressed.HasValue)
      {
        ButtonPressed?.Invoke(this, new EncoderButtonEventArgs
        {
          EncoderIndex = i,
          IsPressed = pressed.Value
        });
      }
    }

    for (int i = 0; i < RotaryEncoderDecoder.EncoderCount; i++)
    {
      int delta = _decoder.Deltas[i];
      if (delta != 0)
      {
        EncoderTurned?.Invoke(this, new EncoderTurnedEventArgs
        {
          EncoderIndex = i,
          Delta = delta
        });
      }
    }
  }

  /// <summary>
  /// Latest clamped position for an encoder, in device units.
  ///
  /// <para>
  /// Prefer this over accumulating <see cref="EncoderTurned"/> deltas for a <b>bounded</b> control
  /// such as volume: the device owns the range and clamping at the ends is the wanted behaviour.
  /// Deltas are the right input where the host owns the range, such as tuning.
  /// </para>
  /// </summary>
  public int GetPosition(int encoderIndex)
  {
    if (encoderIndex < 0 || encoderIndex >= RotaryEncoderDecoder.EncoderCount)
    {
      throw new ArgumentOutOfRangeException(nameof(encoderIndex));
    }

    return _decoder.GetPosition(encoderIndex);
  }

  private HidDevice? FindDevice(RotaryEncoderOptions opts)
  {
    try
    {
      // If a specific device path is configured, use it directly
      if (!string.IsNullOrEmpty(opts.DevicePath))
      {
        return DeviceList.Local.GetHidDevices()
          .FirstOrDefault(d => d.DevicePath == opts.DevicePath);
      }

      // Otherwise find by VID/PID
      return DeviceList.Local.GetHidDevices(opts.VendorId, opts.ProductId)
        .FirstOrDefault();
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error enumerating HID devices");
      return null;
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    _cts?.Cancel();
    _cts?.Dispose();
    GC.SuppressFinalize(this);
  }
}
