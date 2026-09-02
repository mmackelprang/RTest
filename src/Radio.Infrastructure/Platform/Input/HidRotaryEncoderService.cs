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
/// change, so the read blocks until the next report rather than polling. An idle device is expected
/// to produce nothing for long stretches and must never be treated as a disconnect.
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

  /// <summary>
  /// True once the device has been opened at least once in this process. ENC-0's notification policy
  /// is asymmetric — absent-at-boot and dropped-mid-session are the same <c>IsConnected=false</c> and
  /// are not the same event — so the transition has to carry this.
  /// </summary>
  private bool _everConnected;

  // Guards the absent-device log so it fires once per absence rather than once per rescan.
  private bool _announcedAbsence;

  // Same, for the permissions case — which is permanent until someone changes the system, so it
  // would otherwise log a warning and a stack trace every couple of seconds indefinitely.
  private bool _announcedUnauthorized;

  /// <summary>
  /// Upper bound on the absent-device rescan interval. Presence is discovered by enumerating HID
  /// devices, and doing that every <c>ReconnectDelayMs</c> forever against a device that may simply
  /// not be installed is the "reconnect thrash" ENC-0 rules out. Backing off to this cap keeps a
  /// permanently-absent encoder cheap while still noticing a plug-in within a few seconds.
  /// </summary>
  private const int MaxAbsentRescanMs = 15000;


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
    int absentRescanMs = Math.Max(opts.ReconnectDelayMs, 250);

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
            RaiseConnectionChanged(false);
          }
          else if (!_announcedAbsence)
          {
            // Announce absence exactly once per absence, not once per rescan. A device that is
            // simply not installed must not produce a log line every couple of seconds forever.
            _announcedAbsence = true;
            _logger.LogInformation(
              "No rotary encoder found (VID=0x{VID:X4}, PID=0x{PID:X4}); will keep watching",
              opts.VendorId, opts.ProductId);
            RaiseConnectionChanged(false);
          }

          await Task.Delay(absentRescanMs, cancellationToken);
          absentRescanMs = Math.Min(absentRescanMs * 2, MaxAbsentRescanMs);
          continue;
        }

        // Found one: reset the backoff so a device that drops and returns is picked up promptly.
        absentRescanMs = opts.ReconnectDelayMs;
        _announcedAbsence = false;
        _announcedUnauthorized = false;

        stream = device.Open();
        // Infinite is correct for an event-driven device that is silent at rest: the read simply
        // waits for the next report rather than waking to discover nothing happened.
        //
        // ⚠ The consequence, recorded rather than papered over: cancellation cannot interrupt an
        // in-flight read, so StopAsync falls back to its 5 s abandon path on a quiet device. A
        // finite timeout would fix that, but only if HidSharp surfaces the expiry as
        // TimeoutException — and if it surfaces it as IOException instead, the handler below would
        // read it as a disconnect and tear the connection down once per timeout, forever. That is
        // not verifiable from here, and it is ENC-0's territory (disappears-mid-session handling),
        // so this keeps the behaviour that is known to work.
        stream.ReadTimeout = Timeout.Infinite;

        if (!_isConnected)
        {
          _isConnected = true;
          _everConnected = true;
          _logger.LogInformation("Encoder device connected: {Device}", device.GetProductName());
          RaiseConnectionChanged(true);
        }

        await ReadFromDeviceAsync(device, stream, cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex) when (IsPermissionDenied(ex))
      {
        // ENC-0a. This is a PERMANENT condition, not a transient one, and treating it as transient
        // is what made it expensive: hidraw nodes are created root-owned 0600, radio-api runs
        // unprivileged, and the device is present and enumerable — so the reader found it, failed to
        // open it, and retried every couple of seconds forever, each attempt logging a warning and a
        // stack trace. On a box where log volume correlates with audible distortion that is not a
        // cosmetic problem.
        //
        // Announced once with the remedy, then rescanned at the slowest interval. Retrying at all
        // still matters: a udev rule applied while the service is running fixes this without a
        // restart, and the operator should not have to know to restart it.
        if (!_announcedUnauthorized)
        {
          _announcedUnauthorized = true;
          _logger.LogError(ex,
            "Encoder found but not permitted to open it. This is a permissions problem, not a " +
            "hardware one — install deploy/common/99-rotaryusb-encoder.rules into " +
            "/etc/udev/rules.d/, then 'udevadm control --reload-rules && udevadm trigger " +
            "--subsystem-match=hidraw'. Rechecking every {Seconds}s until it is fixed.",
            MaxAbsentRescanMs / 1000);
        }

        if (_isConnected)
        {
          _isConnected = false;
          RaiseConnectionChanged(false);
        }

        absentRescanMs = MaxAbsentRescanMs;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Encoder device error, will reconnect");

        if (_isConnected)
        {
          _isConnected = false;
          RaiseConnectionChanged(false);
        }
      }
      finally
      {
        stream?.Dispose();
      }

      if (!cancellationToken.IsCancellationRequested)
      {
        await Task.Delay(absentRescanMs, cancellationToken);
      }
    }

    if (_isConnected)
    {
      _isConnected = false;
      RaiseConnectionChanged(false);
    }
  }

  /// <summary>
  /// True when the device was found but could not be opened for permissions reasons.
  ///
  /// <para>
  /// ⚠ Matched partly by type <i>name</i>, which is deliberate rather than lazy: HidSharp 2.1.0
  /// declares <c>DeviceUnauthorizedAccessException</c> as <b>internal</b>, so it cannot be caught by
  /// type from outside the assembly. The BCL check is tried first and the name check is the fallback,
  /// so this keeps working if a later HidSharp makes the type public or changes its hierarchy.
  /// </para>
  /// </summary>
  private static bool IsPermissionDenied(Exception ex) =>
    ex is UnauthorizedAccessException ||
    ex.GetType().Name == "DeviceUnauthorizedAccessException";

  private void RaiseConnectionChanged(bool isConnected)
  {
    ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs
    {
      IsConnected = isConnected,
      WasEverConnected = _everConnected,
    });
  }

  private async Task ReadFromDeviceAsync(HidDevice device, HidStream stream, CancellationToken cancellationToken)
  {
    // Size the buffer from the device rather than a constant: report 0x04 (diagnostics) is 56 bytes
    // and larger than the 37-byte positions report, so a 37-byte buffer would truncate it and, on
    // some backends, error rather than skip.
    // Two different lengths, and conflating them defeats the feature detection. The DEVICE's
    // reported length is what says whether this firmware sends movement accumulators; the BUFFER is
    // floored to the positions-report size so a short-report device cannot cause an undersized
    // buffer. Passing the floored value to BeginConnection would make `>= 37` true by construction
    // and report movement support for firmware that has none.
    int deviceReportLength = device.GetMaxInputReportLength();
    var buffer = new byte[Math.Max(deviceReportLength, RotaryEncoderDecoder.PositionPayloadSize + 1)];

    // Re-baseline lives here, not at the call site, so a reconnect inside the read loop cannot skip
    // it. See RotaryEncoderDecoder.BeginConnection for why that matters.
    _decoder.BeginConnection(deviceReportLength);

    _logger.LogInformation(
      "Encoder report length {Length} bytes (movement accumulators: {HasMovement})",
      deviceReportLength, _decoder.HasMovement);

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
