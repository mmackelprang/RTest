using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Metrics;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Handles automatic switching to Bluetooth source when a device connects,
/// and pre-warming Bluetooth on startup if configured.
/// Extracted from AudioManager to separate Bluetooth orchestration concerns.
///
/// Plan B (BT autoswitch gate) — `OnBluetoothDeviceConnected` is gated on the
/// presence of the PipeWire BT capture node. The handler first probes
/// <see cref="IBluetoothService.IsCaptureNodeAvailableAsync"/> for up to
/// <see cref="BluetoothOptions.AutoSwitchProbeWindowMs"/>; if the node is
/// already present we switch immediately. Otherwise we subscribe to
/// <see cref="IBluetoothService.CaptureNodeAvailable"/> for up to
/// <see cref="BluetoothOptions.AutoSwitchMaxWaitMs"/>, switching when the
/// node finally appears (or abandoning the switch on timeout).
/// </summary>
public class BluetoothAutoSwitchService : IDisposable
{
  private readonly ILogger<BluetoothAutoSwitchService> _logger;
  private readonly IBluetoothService _bluetoothService;
  private readonly IOptionsMonitor<BluetoothOptions> _bluetoothOptions;
  private readonly Func<IAudioManager> _getAudioManager;
  private readonly IMetricsCollector? _metricsCollector;
  private bool _disposed;

  public BluetoothAutoSwitchService(
    ILogger<BluetoothAutoSwitchService> logger,
    IBluetoothService bluetoothService,
    IOptionsMonitor<BluetoothOptions> bluetoothOptions,
    Func<IAudioManager> getAudioManager,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _bluetoothService = bluetoothService;
    _bluetoothOptions = bluetoothOptions;
    _getAudioManager = getAudioManager;
    _metricsCollector = metricsCollector;

    _bluetoothService.DeviceConnected += OnBluetoothDeviceConnected;
  }

  /// <summary>
  /// Pre-warms the Bluetooth source if EnableOnStartup is configured.
  /// Called during AudioManager initialization.
  /// </summary>
  public async Task PreWarmBluetoothAsync(CancellationToken cancellationToken = default)
  {
    if (!_bluetoothOptions.CurrentValue.EnableOnStartup)
    {
      return;
    }

    _logger.LogInformation("Pre-initializing Bluetooth source (EnableOnStartup=true)");
    var audioManager = _getAudioManager();
    await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: false, cancellationToken);
  }

  /// <summary>
  /// Auto-switch to Bluetooth when a device connects — gated on capture-node availability.
  /// </summary>
  private async void OnBluetoothDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    try
    {
      var opts = _bluetoothOptions.CurrentValue;
      if (!opts.AutoSwitchOnConnect)
      {
        return;
      }

      if (!_bluetoothService.IsAvailable)
      {
        _logger.LogWarning("Bluetooth auto-switch skipped; adapter not available");
        return;
      }

      var audioManager = _getAudioManager();
      if (audioManager.ActiveSource?.Type == AudioSourceType.Bluetooth)
      {
        _logger.LogDebug("Bluetooth device connected but Bluetooth is already the active source; skipping switch");
        return;
      }

      // Short-bounded probe for PW capture node presence — typical happy path.
      using var probeCts = new CancellationTokenSource(opts.AutoSwitchProbeWindowMs);
      var nodeReady = await ProbeForNodeAsync(e.Device.Address, probeCts.Token);
      if (nodeReady)
      {
        _logger.LogInformation(
          "BT auto-switch: PW node ready, switching immediately for {Address}",
          e.Device.Address);
        await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
        return;
      }

      // Node not ready inside probe window → defer to event subscription.
      _logger.LogInformation(
        "BT auto-switch: PW node not ready for {Address} after {Probe}ms; subscribing to CaptureNodeAvailable (max wait {Max}ms)",
        e.Device.Address, opts.AutoSwitchProbeWindowMs, opts.AutoSwitchMaxWaitMs);
      _metricsCollector?.Increment("bluetooth.autoswitch_deferred_total");
      await WaitForNodeOrTimeoutAsync(e.Device.Address, opts.AutoSwitchMaxWaitMs);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to auto-switch to Bluetooth after device connect {Device}", e.Device.Address);
    }
  }

  /// <summary>
  /// Probes <see cref="IBluetoothService.IsCaptureNodeAvailableAsync"/> every 500 ms
  /// until either the node appears or the supplied token is cancelled.
  /// </summary>
  private async Task<bool> ProbeForNodeAsync(string address, CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      if (await _bluetoothService.IsCaptureNodeAvailableAsync(address, ct))
      {
        return true;
      }
      try
      {
        await Task.Delay(500, ct);
      }
      catch (OperationCanceledException)
      {
        return false;
      }
    }
    return false;
  }

  /// <summary>
  /// Subscribes to <see cref="IBluetoothService.CaptureNodeAvailable"/> for the given
  /// device address with a hard timeout. On success, performs the deferred source switch.
  /// On timeout, increments <c>bluetooth.autoswitch_abandoned_total</c> and logs.
  /// </summary>
  private async Task WaitForNodeOrTimeoutAsync(string address, int timeoutMs)
  {
    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    EventHandler<CaptureNodeAvailableEventArgs>? handler = null;
    handler = (_, evt) =>
    {
      if (string.Equals(evt.DeviceAddress, address, StringComparison.OrdinalIgnoreCase))
      {
        tcs.TrySetResult(true);
      }
    };
    _bluetoothService.CaptureNodeAvailable += handler;
    try
    {
      var timeoutTask = Task.Delay(timeoutMs);
      var winner = await Task.WhenAny(tcs.Task, timeoutTask);
      if (winner == tcs.Task)
      {
        _logger.LogInformation("BT auto-switch: PW node arrived for {Address}, switching", address);
        var audioManager = _getAudioManager();
        if (audioManager.ActiveSource?.Type != AudioSourceType.Bluetooth)
        {
          await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
        }
      }
      else
      {
        _logger.LogWarning(
          "BT auto-switch: PW node did not appear within {Max}ms for {Address}; abandoning switch",
          timeoutMs, address);
        _metricsCollector?.Increment("bluetooth.autoswitch_abandoned_total");
      }
    }
    finally
    {
      _bluetoothService.CaptureNodeAvailable -= handler;
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    _bluetoothService.DeviceConnected -= OnBluetoothDeviceConnected;
  }
}
