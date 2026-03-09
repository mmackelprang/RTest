using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Metrics;

namespace Radio.Infrastructure.Platform.Bluetooth;

/// <summary>
/// Manages exponential-backoff reconnection attempts to a Bluetooth device
/// after an unexpected disconnect. Testable: accepts delegates for connect
/// and connection-check so it can be unit-tested without BlueZ.
/// </summary>
internal sealed class BluetoothReconnectionLoop : IDisposable
{
  private readonly ILogger _logger;
  private readonly BluetoothOptions _options;
  private readonly Func<string, CancellationToken, Task<bool>> _connectFunc;
  private readonly Func<bool> _isDeviceConnected;
  private readonly IMetricsCollector? _metricsCollector;
  private CancellationTokenSource? _cts;
  private Task? _loopTask;
  private bool _disposed;

  public BluetoothReconnectionLoop(
    ILogger logger,
    BluetoothOptions options,
    Func<string, CancellationToken, Task<bool>> connectFunc,
    Func<bool> isDeviceConnected,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _options = options;
    _connectFunc = connectFunc;
    _isDeviceConnected = isDeviceConnected;
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Starts a reconnection loop for the given device address.
  /// Cancels any prior loop before starting.
  /// </summary>
  public void Start(string deviceAddress)
  {
    Cancel();
    _cts = new CancellationTokenSource();
    var token = _cts.Token;
    _loopTask = Task.Run(() => RunLoopAsync(deviceAddress, token), token);
  }

  /// <summary>Cancels any active reconnection loop.</summary>
  public void Cancel()
  {
    var cts = _cts;
    _cts = null;
    _loopTask = null;
    if (cts != null)
    {
      cts.Cancel();
      cts.Dispose();
    }
  }

  /// <summary>Whether a reconnection loop is currently active.</summary>
  public bool IsActive => _loopTask != null && !_loopTask.IsCompleted;

  /// <summary>
  /// Pure function: calculates the backoff delay for a given attempt number.
  /// Delay = min(base * 2^(attempt-1), max).
  /// </summary>
  public static TimeSpan CalculateBackoffDelay(int attempt, int baseMs, int maxMs)
  {
    if (attempt <= 0) attempt = 1;
    var delayMs = baseMs * (1 << Math.Min(attempt - 1, 30)); // prevent overflow
    if (delayMs < 0 || delayMs > maxMs) delayMs = maxMs; // cap
    return TimeSpan.FromMilliseconds(delayMs);
  }

  private async Task RunLoopAsync(string deviceAddress, CancellationToken ct)
  {
    _logger.LogInformation("Starting reconnection loop for {Address} (max {Max} attempts)",
      deviceAddress, _options.MaxReconnectAttempts);

    for (int attempt = 1; attempt <= _options.MaxReconnectAttempts; attempt++)
    {
      ct.ThrowIfCancellationRequested();

      // If another device connected in the meantime, stop trying
      if (_isDeviceConnected())
      {
        _logger.LogInformation("Device already connected — stopping reconnection loop");
        return;
      }

      var delay = CalculateBackoffDelay(attempt, _options.ReconnectBaseDelayMs, _options.ReconnectMaxDelayMs);
      _logger.LogInformation("Reconnect attempt {Attempt}/{Max} for {Address} in {Delay}ms",
        attempt, _options.MaxReconnectAttempts, deviceAddress, (int)delay.TotalMilliseconds);

      try
      {
        await Task.Delay(delay, ct);
      }
      catch (OperationCanceledException)
      {
        _logger.LogDebug("Reconnection loop cancelled during delay");
        return;
      }

      // Re-check after delay
      if (_isDeviceConnected())
      {
        _logger.LogInformation("Device connected during backoff — stopping reconnection loop");
        return;
      }

      try
      {
        _metricsCollector?.Increment("bluetooth.reconnect_attempts_total");
        var success = await _connectFunc(deviceAddress, ct);
        if (success)
        {
          _metricsCollector?.Increment("bluetooth.reconnect_success_total");
          _logger.LogInformation("Reconnected to {Address} on attempt {Attempt}", deviceAddress, attempt);
          return;
        }
      }
      catch (OperationCanceledException)
      {
        _logger.LogDebug("Reconnection loop cancelled during connect");
        return;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Reconnect attempt {Attempt} failed for {Address}", attempt, deviceAddress);
      }
    }

    _metricsCollector?.Increment("bluetooth.reconnect_exhausted_total");
    _logger.LogWarning("Reconnection exhausted after {Max} attempts for {Address}",
      _options.MaxReconnectAttempts, deviceAddress);
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Cancel();
  }
}
