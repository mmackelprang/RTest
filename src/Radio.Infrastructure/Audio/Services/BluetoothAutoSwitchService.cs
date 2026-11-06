using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
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
/// until the node appears or that device disconnects.
///
/// ⭐ Owner's ruling, 2026-11-06: "bluetooth should pause the current stream whenever it
/// connects", and a late switch replaces a source picked by hand. So there is no deadline on
/// the wait. The trigger stays the node, the moment the phone has audio, and not the connect:
/// a phone coming into range with nothing playing must not silence the radio.
///
/// Each connection takes over once. The node comes and goes with every pause on the phone
/// (AUD-10), so a source the owner picks after the takeover stays picked until the phone
/// next reconnects.
/// </summary>
public class BluetoothAutoSwitchService : IDisposable
{
  private readonly ILogger<BluetoothAutoSwitchService> _logger;
  private readonly IBluetoothService _bluetoothService;
  private readonly IOptionsMonitor<BluetoothOptions> _bluetoothOptions;
  private readonly Func<IAudioManager> _getAudioManager;
  private readonly IMetricsCollector? _metricsCollector;

  // The connected devices still waiting to take over, each with the token that ends its wait.
  // At most one entry per address, which is what bounds the waiters: a repeated connect finds the
  // entry and leaves, and a disconnect removes it. Guarded by _waitersLock, as is _disposed.
  private readonly Dictionary<string, CancellationTokenSource> _waiters = new(StringComparer.OrdinalIgnoreCase);
  private readonly object _waitersLock = new();
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
    _bluetoothService.DeviceDisconnected += OnBluetoothDeviceDisconnected;
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
  /// <remarks>
  /// Bluetooth already being active does not end this early: the owner can still pick another
  /// source while the phone sits idle, and the ruling is that the phone then takes over.
  /// </remarks>
  private async void OnBluetoothDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    var address = e.Device.Address;
    CancellationTokenSource? waiter = null;
    try
    {
      if (!_bluetoothOptions.CurrentValue.AutoSwitchOnConnect)
      {
        return;
      }

      if (!_bluetoothService.IsAvailable)
      {
        _logger.LogWarning("Bluetooth auto-switch skipped; adapter not available");
        return;
      }

      lock (_waitersLock)
      {
        if (_disposed || _waiters.ContainsKey(address))
        {
          _logger.LogDebug("BT auto-switch: already waiting for {Address}", address);
          return;
        }
        waiter = new CancellationTokenSource();
        _waiters[address] = waiter;
      }

      await SwitchWhenNodeIsReadyAsync(address, waiter.Token);
    }
    catch (OperationCanceledException) when (waiter?.IsCancellationRequested == true)
    {
      _logger.LogInformation("BT auto-switch: {Address} left before it had audio; nothing to switch", address);
      _metricsCollector?.Increment("bluetooth.autoswitch_abandoned_total");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to auto-switch to Bluetooth after device connect {Device}", address);
    }
    finally
    {
      if (waiter != null)
      {
        lock (_waitersLock)
        {
          // A disconnect may already have removed this entry, and a reconnect replaced it.
          if (_waiters.TryGetValue(address, out var current) && ReferenceEquals(current, waiter))
          {
            _waiters.Remove(address);
          }
        }
        waiter.Dispose();
      }
    }
  }

  /// <summary>
  /// A device that has gone cannot take over: ends its wait, and cancels a probe in flight.
  /// </summary>
  private void OnBluetoothDeviceDisconnected(object? sender, BluetoothDeviceDisconnectedEventArgs e)
  {
    CancellationTokenSource? waiter;
    lock (_waitersLock)
    {
      if (!_waiters.Remove(e.Device.Address, out waiter))
      {
        return;
      }
    }
    Cancel(waiter);
  }

  /// <summary>
  /// The waiter's own task disposes its source, and can do so between the lookup and this call.
  /// </summary>
  private static void Cancel(CancellationTokenSource waiter)
  {
    try
    {
      waiter.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // That task had already finished, so there is nothing left to end.
    }
  }

  /// <summary>
  /// Switches to Bluetooth once the capture node is confirmed: by the bounded probe, or by
  /// <see cref="IBluetoothService.CaptureNodeAvailable"/>, however long that takes. The subscription
  /// goes in before the first probe and stays for both phases, so a node that appears between a
  /// probe's read and the end of the probe window still counts.
  /// </summary>
  /// <param name="address">The connected device.</param>
  /// <param name="deviceLeft">Cancelled when the device disconnects, which ends the wait with an
  /// <see cref="OperationCanceledException"/>.</param>
  private async Task SwitchWhenNodeIsReadyAsync(string address, CancellationToken deviceLeft)
  {
    // RunContinuationsAsynchronously, so the PipeWire registry thread raising the node event does
    // not run the switch.
    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    var nodeArrived = NewSignal();
    using var leaving = deviceLeft.Register(() => Volatile.Read(ref nodeArrived).TrySetCanceled(deviceLeft));
    EventHandler<CaptureNodeAvailableEventArgs> handler = (_, evt) =>
    {
      if (string.Equals(evt.DeviceAddress, address, StringComparison.OrdinalIgnoreCase))
      {
        Volatile.Read(ref nodeArrived).TrySetResult();
      }
    };

    // The pipeline monitor can bind a node with no node event, and a bound node stays up, so this is
    // the only signal there will be until the phone next pauses.
    EventHandler recovered = (_, _) => Volatile.Read(ref nodeArrived).TrySetResult();

    _bluetoothService.CaptureNodeAvailable += handler;
    _bluetoothService.CaptureStreamRecovered += recovered;
    try
    {
      var probeWindowMs = _bluetoothOptions.CurrentValue.AutoSwitchProbeWindowMs;
      using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(deviceLeft);
      probeCts.CancelAfter(probeWindowMs);
      var nodeFound = await ProbeForNodeAsync(address, nodeArrived.Task, probeCts.Token);
      deviceLeft.ThrowIfCancellationRequested();

      if (!nodeFound)
      {
        _logger.LogInformation(
          "BT auto-switch: PW node not ready for {Address} after {Probe}ms; waiting for CaptureNodeAvailable until it disconnects",
          address, probeWindowMs);
        _metricsCollector?.Increment("bluetooth.autoswitch_deferred_total");

        await nodeArrived.Task;
      }

      while (true)
      {
        // A new signal before each bind, so one that arrives while a bind runs and fails is kept.
        // Interlocked for the full fence: the disconnect's callback reads this after setting the
        // token, and this reads the token after the swap, so one of the two sees the other.
        var next = NewSignal();
        Interlocked.Exchange(ref nodeArrived, next);
        deviceLeft.ThrowIfCancellationRequested();

        if (await SwitchIfStillNeededAsync(address, deviceLeft))
        {
          return;
        }

        // Waits for a signal, not for another probe: a node the bind could not take while it was
        // present must not turn into a search that repeats for as long as the node stays.
        _logger.LogInformation(
          "BT auto-switch: no capture could be bound for {Address}; leaving the current source and waiting for its next node",
          address);
        await next.Task;
      }
    }
    finally
    {
      _bluetoothService.CaptureNodeAvailable -= handler;
      _bluetoothService.CaptureStreamRecovered -= recovered;
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
  /// case is skipped: another source selected meanwhile is still replaced. The option is read again
  /// here because the wait can outlast it being turned off.
  /// </summary>
  /// <returns>False when no capture could be bound, so the current source was left playing.</returns>
  private async Task<bool> SwitchIfStillNeededAsync(string address, CancellationToken deviceLeft)
  {
    if (!_bluetoothOptions.CurrentValue.AutoSwitchOnConnect)
    {
      _logger.LogInformation("BT auto-switch: PW node ready for {Address}, but auto-switch is now off", address);
      return true;
    }

    var audioManager = _getAudioManager();
    if (audioManager.ActiveSource?.Type == AudioSourceType.Bluetooth)
    {
      _logger.LogInformation(
        "BT auto-switch: PW node ready for {Address}; Bluetooth is already the active source",
        address);
      return true;
    }

    if (!await BindCaptureBeforeTheSwitchAsync(deviceLeft))
    {
      return false;
    }
    deviceLeft.ThrowIfCancellationRequested();

    _logger.LogInformation("BT auto-switch: PW node ready, switching to Bluetooth for {Address}", address);
    (audioManager.ActiveSource as PrimaryAudioSourceBase)?.KeepPlaceOnNextStop();
    await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
    return true;
  }

  /// <summary>
  /// Binds the node before the switch stops the old source. This is the point of no return for the
  /// old source, for two reasons. A node that went before it could be bound (a notification sound, or
  /// a phone that left) means there is no audio to replace it with, so the switch does not happen.
  /// And the switch's own stop can outlive the node: the phone gives up on an unbound A2DP node after
  /// about 4 s (AUD-10), and stopping FM can block for about that long by itself, because
  /// RadioReceiver.Shutdown joins its processing thread for up to 2 s and then
  /// RtlSdrDevice.StopStreaming waits for up to 2 s more.
  /// </summary>
  /// <remarks>
  /// This does not put anything in the mixer. The stream feeds a generator that nothing plays until
  /// the switch starts the Bluetooth source, which picks up this capture and routes it. If the node
  /// goes during the old source's stop, the source searches for a capture of its own instead.
  /// </remarks>
  /// <returns>Whether a capture is bound, or the platform routes the audio itself.</returns>
  private async Task<bool> BindCaptureBeforeTheSwitchAsync(CancellationToken deviceLeft)
  {
    if (_bluetoothService.IsAudioManagedByPlatform)
    {
      return true;
    }

    try
    {
      return await _bluetoothService.GetAudioCaptureDeviceAsync(deviceLeft) != null;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "BT auto-switch: binding the capture failed; leaving the current source");
      return false;
    }
  }

  public void Dispose()
  {
    List<CancellationTokenSource> waiters;
    lock (_waitersLock)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      waiters = _waiters.Values.ToList();
      _waiters.Clear();
    }

    _bluetoothService.DeviceConnected -= OnBluetoothDeviceConnected;
    _bluetoothService.DeviceDisconnected -= OnBluetoothDeviceDisconnected;

    foreach (var waiter in waiters)
    {
      Cancel(waiter);
    }
  }
}
