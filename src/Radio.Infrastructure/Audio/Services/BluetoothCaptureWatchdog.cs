using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
#if !WINDOWS_TARGET
using Radio.Infrastructure.Platform.Bluetooth;
#endif

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Periodic watchdog that detects FM-BT-3 — silent OnProcess quiescence on a
/// long-running PipeWire BT capture stream. When the active stream's
/// <c>MillisecondsSinceLastOnProcess</c> exceeds
/// <see cref="BluetoothOptions.OnProcessStallThresholdMs"/> for
/// <see cref="BluetoothOptions.ConsecutiveStalledChecks"/> consecutive ticks,
/// raises <c>CaptureStreamStalled</c> on the Bluetooth service so the existing
/// recovery interlock in <c>BluetoothAudioSource</c> can dedup with downstream
/// generator-stall recovery.
/// </summary>
/// <remarks>
/// Linux-only. On Windows the watchdog compiles as a no-op (no Linux service to
/// observe).
/// </remarks>
internal sealed class BluetoothCaptureWatchdog : BackgroundService
{
  private readonly ILogger<BluetoothCaptureWatchdog> _logger;
#if !WINDOWS_TARGET
  private readonly IOptionsMonitor<BluetoothOptions> _options;
  private readonly LinuxBluetoothService? _linuxService;
  private int _consecutiveStalledChecks;

  public BluetoothCaptureWatchdog(
    ILogger<BluetoothCaptureWatchdog> logger,
    IOptionsMonitor<BluetoothOptions> options,
    LinuxBluetoothService? linuxService = null)
  {
    _logger = logger;
    _options = options;
    _linuxService = linuxService;
  }

  /// <summary>
  /// Exposed for unit tests via <c>InternalsVisibleTo</c> for
  /// <c>Radio.Infrastructure.Tests</c>. Production code observes the watchdog's
  /// effect via the <see cref="LinuxBluetoothService.CaptureStreamStalled"/>
  /// event, not this counter.
  /// </summary>
  internal int ConsecutiveStalledChecksForTest => _consecutiveStalledChecks;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_linuxService == null)
    {
      _logger.LogInformation(
        "BluetoothCaptureWatchdog: no Linux BT service available, watchdog disabled");
      return;
    }

    _logger.LogInformation(
      "BluetoothCaptureWatchdog: starting (threshold={Threshold}ms, tick={Tick}ms, consecutive={N})",
      _options.CurrentValue.OnProcessStallThresholdMs,
      _options.CurrentValue.WatchdogTickIntervalMs,
      _options.CurrentValue.ConsecutiveStalledChecks);

    while (!stoppingToken.IsCancellationRequested)
    {
      var opts = _options.CurrentValue;
      var tickMs = Math.Max(100, opts.WatchdogTickIntervalMs);

      try
      {
        if (opts.OnProcessStallThresholdMs <= 0)
        {
          // Watchdog disabled by config — sleep and re-check
          await Task.Delay(tickMs, stoppingToken).ConfigureAwait(false);
          continue;
        }

        var snapshot = _linuxService.GetCaptureStreamSnapshot();
        if (snapshot == null)
        {
          // No active native capture stream — reset the consecutive counter.
          _consecutiveStalledChecks = 0;
        }
        else if (snapshot.Value.ElapsedMs >= opts.OnProcessStallThresholdMs)
        {
          _consecutiveStalledChecks++;
          if (_consecutiveStalledChecks >= opts.ConsecutiveStalledChecks)
          {
            _linuxService.RaiseCaptureStreamStalled(
              snapshot.Value.Address,
              snapshot.Value.ElapsedMs,
              _consecutiveStalledChecks);
            // Reset so we don't re-fire every tick after the first detection;
            // BluetoothAudioSource will run a full StopCore/PlayCore which
            // rebuilds the native stream and restarts the OnProcess clock.
            _consecutiveStalledChecks = 0;
          }
        }
        else
        {
          _consecutiveStalledChecks = 0;
        }

        await Task.Delay(tickMs, stoppingToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "BluetoothCaptureWatchdog: unhandled tick exception");
        try
        {
          await Task.Delay(tickMs, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          break;
        }
      }
    }

    _logger.LogInformation("BluetoothCaptureWatchdog: stopped");
  }
#else
  // Windows: no Linux BT service exists, watchdog is a no-op for API symmetry.
  public BluetoothCaptureWatchdog(
    ILogger<BluetoothCaptureWatchdog> logger,
    IOptionsMonitor<BluetoothOptions> options)
  {
    _logger = logger;
    _ = options;
  }

  protected override Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation(
      "BluetoothCaptureWatchdog: Windows build, watchdog disabled (Linux-only feature)");
    return Task.CompletedTask;
  }
#endif
}
