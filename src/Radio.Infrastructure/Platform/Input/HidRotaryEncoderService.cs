using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Reads rotary encoder events from a Raspberry Pi Pico via USB HID.
/// The Pico sends 8-byte reports: bytes 1-4 are signed encoder deltas,
/// byte 5 is a button bitmask (bit N = encoder N button state).
/// </summary>
public class HidRotaryEncoderService : IRotaryEncoderService
{
  private readonly ILogger<HidRotaryEncoderService> _logger;
  private readonly IOptionsMonitor<RotaryEncoderOptions> _options;
  private CancellationTokenSource? _cts;
  private Task? _readTask;
  private bool _isConnected;
  private bool _disposed;

  // Track previous button states for edge detection
  private readonly bool[] _previousButtonStates = new bool[4];

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
    if (_disposed) throw new ObjectDisposedException(nameof(HidRotaryEncoderService));

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

        await ReadFromDeviceAsync(stream, cancellationToken);
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

  private async Task ReadFromDeviceAsync(HidStream stream, CancellationToken cancellationToken)
  {
    var buffer = new byte[8];
    var pollInterval = _options.CurrentValue.PollIntervalMs;

    while (!cancellationToken.IsCancellationRequested)
    {
      int bytesRead;
      try
      {
        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
      }
      catch (IOException)
      {
        // Device disconnected
        return;
      }

      if (bytesRead < 6) continue;

      ParseReport(buffer);

      if (pollInterval > 0)
      {
        await Task.Delay(pollInterval, cancellationToken);
      }
    }
  }

  private void ParseReport(byte[] data)
  {
    // Bytes 1-4: signed encoder deltas (sbyte per encoder)
    for (int i = 0; i < 4; i++)
    {
      var delta = (sbyte)data[i + 1];
      if (delta != 0)
      {
        EncoderTurned?.Invoke(this, new EncoderTurnedEventArgs
        {
          EncoderIndex = i,
          Delta = delta
        });
      }
    }

    // Byte 5: button bitmask (bit N = encoder N button)
    byte buttonByte = data[5];
    for (int i = 0; i < 4; i++)
    {
      bool isPressed = (buttonByte & (1 << i)) != 0;
      if (isPressed != _previousButtonStates[i])
      {
        _previousButtonStates[i] = isPressed;
        ButtonPressed?.Invoke(this, new EncoderButtonEventArgs
        {
          EncoderIndex = i,
          IsPressed = isPressed
        });
      }
    }
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
    if (_disposed) return;
    _disposed = true;
    _cts?.Cancel();
    _cts?.Dispose();
    GC.SuppressFinalize(this);
  }
}
