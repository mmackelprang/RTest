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
/// presence of the PipeWire BT capture node. The handler subscribes to
/// <see cref="IBluetoothService.CaptureNodeAvailable"/>, then probes
/// <see cref="IBluetoothService.IsCaptureNodeAvailableAsync"/> for up to
/// <see cref="BluetoothOptions.AutoSwitchProbeWindowMs"/>; if the node is
/// already present we switch immediately. Otherwise it keeps waiting on the event
/// for up to <see cref="BluetoothOptions.AutoSwitchMaxWaitMs"/>, switching when the
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

      await SwitchWhenNodeIsReadyAsync(e.Device.Address, opts);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to auto-switch to Bluetooth after device connect {Device}", e.Device.Address);
    }
  }

  /// <summary>
  /// Switches to Bluetooth once the capture node is confirmed: by the bounded probe, or by
  /// <see cref="IBluetoothService.CaptureNodeAvailable"/> up to the maximum wait. The subscription
  /// goes in before the first probe and stays for both phases, so a node that appears between a
  /// probe's read and the end of the probe window still counts.
  /// </summary>
  private async Task SwitchWhenNodeIsReadyAsync(string address, BluetoothOptions opts)
  {
    var nodeArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    EventHandler<CaptureNodeAvailableEventArgs> handler = (_, evt) =>
    {
      if (string.Equals(evt.DeviceAddress, address, StringComparison.OrdinalIgnoreCase))
      {
        nodeArrived.TrySetResult();
      }
    };

    _bluetoothService.CaptureNodeAvailable += handler;
    try
    {
      using var probeCts = new CancellationTokenSource(opts.AutoSwitchProbeWindowMs);
      if (await ProbeForNodeAsync(address, nodeArrived.Task, probeCts.Token))
      {
        await SwitchIfStillNeededAsync(address);
        return;
      }

      _logger.LogInformation(
        "BT auto-switch: PW node not ready for {Address} after {Probe}ms; waiting for CaptureNodeAvailable (max wait {Max}ms)",
        address, opts.AutoSwitchProbeWindowMs, opts.AutoSwitchMaxWaitMs);
      _metricsCollector?.Increment("bluetooth.autoswitch_deferred_total");

      var winner = await Task.WhenAny(nodeArrived.Task, Task.Delay(opts.AutoSwitchMaxWaitMs));
      if (winner == nodeArrived.Task)
      {
        await SwitchIfStillNeededAsync(address);
      }
      else
      {
        _logger.LogWarning(
          "BT auto-switch: PW node did not appear within {Max}ms for {Address}; abandoning switch",
          opts.AutoSwitchMaxWaitMs, address);
        _metricsCollector?.Increment("bluetooth.autoswitch_abandoned_total");
      }
    }
    finally
    {
      _bluetoothService.CaptureNodeAvailable -= handler;
    }
  }

  /// <summary>
  /// Probes <see cref="IBluetoothService.IsCaptureNodeAvailableAsync"/> every 500 ms until the node
  /// is found, <paramref name="nodeArrived"/> completes, or the token is cancelled. Returns whether
  /// the node is known to exist.
  /// </summary>
  private async Task<bool> ProbeForNodeAsync(string address, Task nodeArrived, CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested && !nodeArrived.IsCompleted)
      {
        if (await _bluetoothService.IsCaptureNodeAvailableAsync(address, ct))
        {
          return true;
        }
        await Task.WhenAny(nodeArrived, Task.Delay(500, ct));
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // The window can expire inside the probe's pw-cli read as well as in the delay. Both are the
      // ordinary "not ready yet" outcome: the caller's deferred wait is written for this case.
    }
    return nodeArrived.IsCompleted;
  }

  /// <summary>
  /// Bluetooth may have been selected by hand while the probe or the wait was running. Only that
  /// case is skipped: another source selected meanwhile is still replaced.
  /// </summary>
  private async Task SwitchIfStillNeededAsync(string address)
  {
    var audioManager = _getAudioManager();
    if (audioManager.ActiveSource?.Type == AudioSourceType.Bluetooth)
    {
      _logger.LogInformation(
        "BT auto-switch: PW node ready for {Address}; Bluetooth is already the active source",
        address);
      return;
    }

    _logger.LogInformation("BT auto-switch: PW node ready, switching to Bluetooth for {Address}", address);
    await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
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
