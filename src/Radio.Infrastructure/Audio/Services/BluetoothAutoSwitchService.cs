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
/// ⭐ Owner's ruling, 2026-11-06: Bluetooth takes over the instant a phone connects, with the cost
/// read first: the radio goes quiet when a phone connects with nothing playing. So the source stops
/// at the connect, and <see cref="BluetoothAudioSource"/> waits, active, for the phone's audio.
///
/// ⚠ <c>DeviceConnected</c> reports the link, and a keyboard, a speaker or RotaryPhone's HFP on this
/// adapter can open that (M3). So a connect only takes over when the link holds for
/// <see cref="BluetoothOptions.AutoSwitchConnectSettleMs"/> and the device is known to send audio
/// (<see cref="IBluetoothService.IsAudioSourceDeviceAsync"/>). A device not known to, which includes
/// a phone paired moments ago and a link already up at startup, falls back to the capture node: the
/// handler subscribes to <see cref="IBluetoothService.CaptureNodeAvailable"/>, probes
/// <see cref="IBluetoothService.IsCaptureNodeAvailableAsync"/> for up to
/// <see cref="BluetoothOptions.AutoSwitchProbeWindowMs"/>, then keeps waiting on the event until the
/// node appears or that device disconnects.
///
/// Each connection takes over once, so a source the owner picks afterwards stays picked. A phone back
/// within <see cref="BluetoothOptions.AutoSwitchSameConnectionWindowMs"/> counts as the same connection:
/// a PBAP sync can drop the link and reconnect it seconds later, and neither that nor a flap is the
/// phone arriving. A disconnect made on the console or on the phone ends the connection at once.
/// </summary>
public class BluetoothAutoSwitchService : IDisposable
{
  private readonly ILogger<BluetoothAutoSwitchService> _logger;
  private readonly IBluetoothService _bluetoothService;
  private readonly IOptionsMonitor<BluetoothOptions> _bluetoothOptions;
  private readonly Func<IAudioManager> _getAudioManager;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly TimeProvider _timeProvider;

  // The connected devices still waiting to take over, each with the token that ends its wait.
  // At most one entry per address, which is what bounds the waiters: a repeated connect finds the
  // entry and leaves, and a disconnect removes it. Guarded by _waitersLock, as is _disposed.
  private readonly Dictionary<string, CancellationTokenSource> _waiters = new(StringComparer.OrdinalIgnoreCase);
  private readonly object _waitersLock = new();

  // The connections that have settled, by address, with when they did or when the device last left.
  // Settled includes the cases with nothing to switch: Bluetooth already active, or the option off.
  // A time rather than a "still connected" flag, because a disconnect can go unreported (evicting a
  // Device1 raises none), and a flag would hold back every later connection. One per paired device at
  // most. Guarded by _waitersLock.
  private readonly Dictionary<string, DateTimeOffset> _settled = new(StringComparer.OrdinalIgnoreCase);
  private bool _disposed;

  public BluetoothAutoSwitchService(
    ILogger<BluetoothAutoSwitchService> logger,
    IBluetoothService bluetoothService,
    IOptionsMonitor<BluetoothOptions> bluetoothOptions,
    Func<IAudioManager> getAudioManager,
    IMetricsCollector? metricsCollector = null,
    TimeProvider? timeProvider = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
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
  /// Starts on the source a restart restored as Bluetooth. Owner's ruling, 2026-11-06: "Select FM
  /// Radio (last station played) if no BT available after restart from playing BT audio."
  /// </summary>
  /// <remarks>
  /// There is no grace period for the phone beyond the adapter's own start, which is when a link
  /// that is already up is seen. A phone that arrives later takes over anyway, so
  /// waiting would only buy a few seconds of FM not being heard, at the price of silence on every
  /// start with the phone away.
  ///
  /// The radio is a stand-in, so the persisted source stays Bluetooth until the owner picks another
  /// one. An active Bluetooth source with no phone is what used to report <c>isPlaying:true</c>; here
  /// it is created, which starts the adapter, and never activated.
  /// </remarks>
  public async Task ActivateRestoredBluetoothAsync(CancellationToken cancellationToken = default)
  {
    var audioManager = _getAudioManager();
    await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: false, cancellationToken);

    if (_bluetoothService.ConnectedDevice != null)
    {
      await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true, cancellationToken);
      return;
    }

    _logger.LogInformation("Restored source is Bluetooth and no phone is connected; starting the radio instead");
    var radio = await audioManager.ActivateStandInSourceAsync(AudioSourceType.Radio, cancellationToken);
    if (radio == null)
    {
      _logger.LogWarning(
        "No radio to stand in for Bluetooth (is the SDR connected?); starting with nothing playing until a phone connects");
    }
  }

  /// <summary>
  /// Takes over for a device that connects. See the class summary for the two paths.
  /// </summary>
  /// <remarks>
  /// A link already up at startup never takes the at-the-connect path: nothing connected just now,
  /// and the owner may be on a source picked since.
  /// </remarks>
  private async void OnBluetoothDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    var address = e.Device.Address;
    CancellationTokenSource? waiter = null;
    var settled = false;
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
        if (IsSameConnection(address))
        {
          _logger.LogInformation("BT auto-switch: {Address} is back on the same connection; leaving the source", address);
          return;
        }
        waiter = new CancellationTokenSource();
        _waiters[address] = waiter;
      }

      if (!e.AlreadyConnected && await HoldsAsAudioSourceAsync(address, waiter.Token))
      {
        await TakeOverAtTheConnectAsync(address, waiter.Token);
      }
      else
      {
        await SwitchWhenNodeIsReadyAsync(address, waiter.Token);
      }

      lock (_waitersLock)
      {
        _settled[address] = _timeProvider.GetUtcNow();
      }
      settled = true;
    }
    catch (OperationCanceledException) when (waiter?.IsCancellationRequested == true)
    {
      _logger.LogInformation("BT auto-switch: {Address} left before taking over; nothing to switch", address);
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

    // After the finally: this says the connection is fully settled, waiter included. It counts
    // connections that settled with nothing to switch as well.
    if (settled)
    {
      _metricsCollector?.Increment("bluetooth.autoswitch_settled_total");
    }
  }

  /// <summary>
  /// Whether a device connecting now continues a connection that has already taken over. Caller
  /// holds <see cref="_waitersLock"/>.
  /// </summary>
  private bool IsSameConnection(string address)
  {
    if (!_settled.TryGetValue(address, out var lastSeen))
    {
      return false;
    }

    var window = TimeSpan.FromMilliseconds(_bluetoothOptions.CurrentValue.AutoSwitchSameConnectionWindowMs);
    if (_timeProvider.GetUtcNow() - lastSeen <= window)
    {
      return true;
    }

    _settled.Remove(address);
    return false;
  }

  /// <summary>
  /// Switches for a phone the connect has decided on. A bind that fails does not leave the old source
  /// playing, as it does on the node path.
  /// </summary>
  /// <remarks>
  /// The phone may already be streaming: the settle has passed, so its node can exist, and the old
  /// source's stop can last as long as an unbound node does. So a node that exists now is bound first,
  /// and one that appears during the stop is bound as it appears. The Bluetooth source leaves a node
  /// alone until it is active, which it is not until the stop has finished.
  /// </remarks>
  private async Task TakeOverAtTheConnectAsync(string address, CancellationToken deviceLeft)
  {
    // Before the bind: with no switch to follow, a stream started here would hold the node with
    // nothing to route it. An active Bluetooth source binds its own.
    if (!IsSwitchStillNeeded(address))
    {
      return;
    }

    EventHandler<CaptureNodeAvailableEventArgs> bindOnNode = (_, evt) =>
    {
      if (string.Equals(evt.DeviceAddress, address, StringComparison.OrdinalIgnoreCase))
      {
        // Off the PipeWire registry thread, as for the Bluetooth source's own handler.
        _ = Task.Run(() => BindNodeDuringTheStopAsync(deviceLeft));
      }
    };

    _bluetoothService.CaptureNodeAvailable += bindOnNode;
    try
    {
      if (await _bluetoothService.IsCaptureNodeAvailableAsync(address, deviceLeft))
      {
        await BindCaptureBeforeTheSwitchAsync(deviceLeft);
      }
      await SwitchIfStillNeededAsync(address, deviceLeft, bindFirst: false);
    }
    finally
    {
      _bluetoothService.CaptureNodeAvailable -= bindOnNode;
    }
  }

  private async Task BindNodeDuringTheStopAsync(CancellationToken deviceLeft)
  {
    try
    {
      await BindCaptureBeforeTheSwitchAsync(deviceLeft);
    }
    catch (OperationCanceledException)
    {
      // The phone left. The takeover's own wait reports that.
    }
  }

  /// <summary>
  /// Whether the link held through the settle and the device is known to send audio. The settle is
  /// what keeps a connect that drops at once from stopping the radio: the disconnect cancels it.
  /// </summary>
  private async Task<bool> HoldsAsAudioSourceAsync(string address, CancellationToken deviceLeft)
  {
    var settleMs = _bluetoothOptions.CurrentValue.AutoSwitchConnectSettleMs;
    if (settleMs > 0)
    {
      await Task.Delay(TimeSpan.FromMilliseconds(settleMs), _timeProvider, deviceLeft);
    }
    return await _bluetoothService.IsAudioSourceDeviceAsync(address, deviceLeft);
  }

  /// <summary>
  /// A device that has gone cannot take over: ends its wait, and cancels a probe in flight. One that
  /// already settled starts the window in which it counts as the same connection, unless the user
  /// disconnected it, on the console or by the phone ending the link (<c>Remote</c>), which ends the
  /// connection whatever follows. A flap reports a timeout. PBAP's teardown is expected to report
  /// LocalHost, which is not measured: if a phone ends that link itself, it reads as Remote.
  /// </summary>
  private void OnBluetoothDeviceDisconnected(object? sender, BluetoothDeviceDisconnectedEventArgs e)
  {
    CancellationTokenSource? waiter;
    lock (_waitersLock)
    {
      if (_settled.ContainsKey(e.Device.Address))
      {
        if (e.UserInitiated || e.Reason == BluetoothDisconnectReason.Remote)
        {
          _settled.Remove(e.Device.Address);
        }
        else
        {
          _settled[e.Device.Address] = _timeProvider.GetUtcNow();
        }
      }

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

        if (await SwitchIfStillNeededAsync(address, deviceLeft, bindFirst: true))
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
  /// <param name="address">The connected device.</param>
  /// <param name="deviceLeft">Cancelled when the device disconnects.</param>
  /// <param name="bindFirst">Bind the capture before stopping the old source, for a node that exists
  /// now and could otherwise expire during the stop.</param>
  /// <returns>False when no capture could be bound, so the current source was left playing.</returns>
  private async Task<bool> SwitchIfStillNeededAsync(string address, CancellationToken deviceLeft, bool bindFirst)
  {
    if (!IsSwitchStillNeeded(address))
    {
      return true;
    }

    if (bindFirst && !await BindCaptureBeforeTheSwitchAsync(deviceLeft))
    {
      return false;
    }
    deviceLeft.ThrowIfCancellationRequested();

    _logger.LogInformation("BT auto-switch: switching to Bluetooth for {Address}", address);
    var audioManager = _getAudioManager();
    (audioManager.ActiveSource as PrimaryAudioSourceBase)?.KeepPlaceOnNextStop();
    await audioManager.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, switchToSource: true);
    return true;
  }

  private bool IsSwitchStillNeeded(string address)
  {
    if (!_bluetoothOptions.CurrentValue.AutoSwitchOnConnect)
    {
      _logger.LogInformation("BT auto-switch: nothing to do for {Address}, auto-switch is now off", address);
      return false;
    }

    if (_getAudioManager().ActiveSource?.Type == AudioSourceType.Bluetooth)
    {
      _logger.LogInformation(
        "BT auto-switch: nothing to do for {Address}, Bluetooth is already the active source",
        address);
      return false;
    }

    return true;
  }

  /// <summary>
  /// Binds the node before the switch stops the old source. On the node path this decides the switch:
  /// a node that went before it could be bound (a notification sound, or a phone that left) means
  /// there is no audio to replace the old source with. On both paths the switch's own stop can
  /// otherwise outlive the node: the phone gives up on an unbound A2DP node after
  /// about 4 s (AUD-10), and stopping FM can block for about that long by itself, because
  /// RadioReceiver.Shutdown joins its processing thread for up to 2 s and then
  /// RtlSdrDevice.StopStreaming waits for up to 2 s more.
  /// </summary>
  /// <remarks>
  /// This does not put anything in the mixer. The stream feeds a generator that nothing plays until
  /// the switch starts the Bluetooth source, which picks up this capture and routes it. If the node
  /// goes during the old source's stop, the source binds the node that replaces it, when one appears.
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
      _logger.LogWarning(ex, "BT auto-switch: binding the capture failed");
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
