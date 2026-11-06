using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Metrics;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// Tests for <see cref="BluetoothAutoSwitchService"/> covering both the pre-warm path
/// and the gated auto-switch logic introduced in Plan B (BT autoswitch gate).
/// </summary>
public class BluetoothAutoSwitchServiceTests
{
  // Fake driver replaces Moq for the gating flow because we need to
  // (a) tally subscriber count on the CaptureNodeAvailable event and
  // (b) deterministically raise the event mid-test.
  private sealed class FakeBluetoothService : IBluetoothService
  {
    public bool IsAvailable { get; set; } = true;
    public BluetoothAdapterState State => BluetoothAdapterState.On;
    public IReadOnlyList<BluetoothDeviceInfo> PairedDevices => Array.Empty<BluetoothDeviceInfo>();
    public IReadOnlyList<BluetoothDeviceInfo> DiscoveredDevices => Array.Empty<BluetoothDeviceInfo>();
    public bool IsDiscovering => false;
    public BluetoothDeviceInfo? ConnectedDevice { get; set; }
    public bool IsAudioManagedByPlatform => false;
    public bool IsCaptureNodeAvailable { get; set; }

    /// <summary>
    /// False by default, so a test that says nothing about the device gets the path for one not
    /// known to send audio: the takeover waits for the node.
    /// </summary>
    public bool IsAudioSourceDevice { get; set; }
    public float? DeviceVolume => null;
    public bool IsReconnecting => false;
    public BluetoothDisconnectReason? LastDisconnectReason => null;
    public BluetoothPipelineStatus PipelineStatus => BluetoothPipelineStatus.Healthy;

    public int CaptureNodeAvailableSubscriberCount { get; private set; }

    /// <summary>How many times something asked for the capture to be bound.</summary>
    public int CaptureRequests { get; private set; }

    /// <summary>Replaces the default answer, a capture bound at once.</summary>
    public Func<CancellationToken, Task<object?>>? CaptureHandler { get; set; }

    /// <summary>
    /// Replaces the canned <see cref="IsCaptureNodeAvailable"/> answer, so a test can make the
    /// probe do what the real one does: stay inside a <c>pw-cli</c> read, and throw when its
    /// token fires.
    /// </summary>
    public Func<string, CancellationToken, Task<bool>>? ProbeHandler { get; set; }

    /// <summary>Completes when the last CaptureNodeAvailable subscriber leaves.</summary>
    public TaskCompletionSource AllUnsubscribed { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged
    { add { } remove { } }
    public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
    public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered
    { add { } remove { } }
    public event EventHandler? CaptureStreamRecovered;
    public event EventHandler<CaptureTargetLostEventArgs>? CaptureTargetLost { add { } remove { } }
    public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged
    { add { } remove { } }
    public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged
    { add { } remove { } }
    public event EventHandler<TimeSpan>? PositionChanged { add { } remove { } }
    public event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged
    { add { } remove { } }

    private readonly List<EventHandler<CaptureNodeAvailableEventArgs>> _captureHandlers = new();
    public event EventHandler<CaptureNodeAvailableEventArgs>? CaptureNodeAvailable
    {
      add
      {
        if (value != null)
        {
          _captureHandlers.Add(value);
          CaptureNodeAvailableSubscriberCount = _captureHandlers.Count;
        }
      }
      remove
      {
        if (value != null && _captureHandlers.Remove(value))
        {
          CaptureNodeAvailableSubscriberCount = _captureHandlers.Count;
          if (_captureHandlers.Count == 0)
          {
            AllUnsubscribed.TrySetResult();
          }
        }
      }
    }

    // Codec observability — added by PR #389 (codec observability); the FakeBluetoothService
    // is unused by codec-related code paths, so a permanent null-impl is correct here.
    public event EventHandler<A2dpCodecChangedEventArgs>? A2dpCodecChanged { add { } remove { } }
    public Task<A2dpCodecInfo?> GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct = default)
      => Task.FromResult<A2dpCodecInfo?>(null);

    // Capture-stream stall — added by PR #390 (capture watchdog); the autoswitch tests do not
    // exercise watchdog behavior, so a permanent never-raises stub is correct here.
    public event EventHandler<CaptureStreamStalledEventArgs>? CaptureStreamStalled { add { } remove { } }

    public Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
      => Task.FromResult(true);
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StartDiscoveryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopDiscoveryAsync() => Task.CompletedTask;
    public Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => Task.FromResult(true);
    public Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => Task.FromResult(true);
    public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => Task.FromResult(true);
    public Task<bool> ConnectAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => Task.FromResult(true);
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => Task.CompletedTask;
    public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
    {
      CaptureRequests++;
      return CaptureHandler?.Invoke(cancellationToken) ?? Task.FromResult<object?>(new object());
    }
    public void StopAudioCapture() { }
    public Task<bool> IsCaptureNodeAvailableAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => ProbeHandler?.Invoke(deviceAddress, cancellationToken) ?? Task.FromResult(IsCaptureNodeAvailable);
    public Task<bool> IsAudioSourceDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => Task.FromResult(IsAudioSourceDevice);
    public Task SetDeviceVolumeAsync(float volume) => Task.CompletedTask;
    public Task NextTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PreviousTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void CancelReconnection() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void SimulateDeviceConnected(string address, bool alreadyConnected = false)
    {
      var device = new BluetoothDeviceInfo
      {
        Address = address,
        Name = "Test Phone",
        IsPaired = true,
        IsConnected = true
      };
      ConnectedDevice = device;
      DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs
      {
        Device = device,
        AlreadyConnected = alreadyConnected
      });
    }

    public void SimulateDeviceDisconnected(
      string address,
      bool userInitiated = false,
      BluetoothDisconnectReason reason = BluetoothDisconnectReason.Unknown)
    {
      var device = new BluetoothDeviceInfo { Address = address, Name = "Test Phone", IsPaired = true };
      ConnectedDevice = null;
      DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs
      {
        Device = device,
        UserInitiated = userInitiated,
        Reason = reason
      });
    }

    public void RaiseCaptureStreamRecovered() => CaptureStreamRecovered?.Invoke(this, EventArgs.Empty);

    public void RaiseCaptureNodeAvailable(string address)
    {
      var args = new CaptureNodeAvailableEventArgs { DeviceAddress = address, PipeWireSerial = 0 };
      foreach (var h in _captureHandlers.ToArray())
      {
        h(this, args);
      }
    }
  }

  private sealed class FakeAudioManagerCounters
  {
    public bool GetOrCreateCalled { get; set; }
    public bool SwitchedToBluetooth { get; set; }
    public int SwitchCount
    {
      get => Volatile.Read(ref _switchCount);
      set
      {
        Volatile.Write(ref _switchCount, value);
        if (value >= 2)
        {
          _second.TrySetResult();
        }
      }
    }
    private int _switchCount;
    private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Switched { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes on the second switch, the only count these tests wait for beyond the first.</summary>
    public Task AtLeastSwitches(int count) =>
      count == 2 ? _second.Task : throw new ArgumentOutOfRangeException(nameof(count));
  }

  /// <summary>
  /// Records the service's deferred/abandoned metrics as signals. These tests wait on what the
  /// service reports rather than on elapsed time (<c>TEST-10</c>; <c>CLAUDE.md</c> § Test Timing).
  /// </summary>
  private sealed class MetricSignals
  {
    public Mock<IMetricsCollector> Mock { get; } = new();
    public TaskCompletionSource Deferred { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Abandoned { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource TookOver { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MetricSignals()
    {
      Mock.Setup(m => m.Increment(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<IDictionary<string, string>?>()))
        .Callback((string key, double _, IDictionary<string, string>? _) =>
        {
          if (key == "bluetooth.autoswitch_deferred_total")
          {
            Deferred.TrySetResult();
          }
          else if (key == "bluetooth.autoswitch_abandoned_total")
          {
            Abandoned.TrySetResult();
          }
          else if (key == "bluetooth.autoswitch_takeover_total")
          {
            TookOver.TrySetResult();
          }
        });
    }
  }

  // Bounds a rendezvous that should complete almost at once. It only decides how long a BROKEN
  // build takes to fail; a passing run never waits it out, so it can be generous.
  private static readonly TimeSpan Rendezvous = TimeSpan.FromSeconds(10);

  private static BluetoothAutoSwitchService CreateService(
    FakeBluetoothService bt,
    Mock<IAudioManager> audioMock,
    int probeMs = 200,
    bool autoSwitchEnabled = true,
    IMetricsCollector? metrics = null,
    int settleMs = 0,
    TimeProvider? timeProvider = null)
  {
    var optionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    optionsMock.Setup(o => o.CurrentValue).Returns(new BluetoothOptions
    {
      EnableOnStartup = true,
      AutoSwitchOnConnect = autoSwitchEnabled,
      AutoSwitchProbeWindowMs = probeMs,
      AutoSwitchConnectSettleMs = settleMs,
      AutoSwitchSameConnectionWindowMs = 60000
    });

    return new BluetoothAutoSwitchService(
      Mock.Of<ILogger<BluetoothAutoSwitchService>>(),
      bt,
      optionsMock.Object,
      () => audioMock.Object,
      timeProvider: timeProvider,
      metricsCollector: metrics);
  }

  private static Mock<IAudioManager> MakeAudioMock(FakeAudioManagerCounters counters, AudioSourceType? activeType = null)
  {
    var mock = new Mock<IAudioManager>();
    if (activeType.HasValue)
    {
      var src = new Mock<IAudioSource>();
      src.Setup(s => s.Type).Returns(activeType.Value);
      mock.Setup(m => m.ActiveSource).Returns(src.Object);
    }
    else
    {
      mock.Setup(m => m.ActiveSource).Returns((IAudioSource?)null);
    }
    mock.Setup(m => m.GetOrCreateSourceAsync(It.IsAny<AudioSourceType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
      .Callback((AudioSourceType t, bool switchTo, CancellationToken _) =>
      {
        counters.GetOrCreateCalled = true;
        if (t == AudioSourceType.Bluetooth && switchTo)
        {
          counters.SwitchedToBluetooth = true;
          counters.SwitchCount++;
          counters.Switched.TrySetResult();
        }
      })
      .ReturnsAsync((IAudioSource?)null);
    return mock;
  }

  // ---- PreWarm ----

  [Fact]
  public async Task PreWarmBluetoothAsync_CreatesSourceWithoutSwitch_WhenEnabled()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock);

    await svc.PreWarmBluetoothAsync();

    audioMock.Verify(m => m.GetOrCreateSourceAsync(
      AudioSourceType.Bluetooth,
      false,
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task PreWarmBluetoothAsync_DoesNothing_WhenDisabled()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var optionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    optionsMock.Setup(o => o.CurrentValue).Returns(new BluetoothOptions { EnableOnStartup = false });
    using var svc = new BluetoothAutoSwitchService(
      Mock.Of<ILogger<BluetoothAutoSwitchService>>(),
      bt,
      optionsMock.Object,
      () => audioMock.Object);

    await svc.PreWarmBluetoothAsync();

    audioMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  // ---- Auto-switch gating (Plan B) ----

  [Fact]
  public async Task NodeReadyInsideProbeWindow_SwitchesImmediately()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.True(counters.SwitchedToBluetooth);
    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task NodeArrivesAfterProbe_SwitchesViaEvent()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    // The probe window has expired once the service reports the deferral (TEST-10).
    await metrics.Deferred.Task.WaitAsync(Rendezvous);
    Assert.False(counters.SwitchedToBluetooth);
    Assert.Equal(1, bt.CaptureNodeAvailableSubscriberCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task ProbeCancelledInsidePwCli_FallsBackToTheEventWait()
  {
    // The real probe sits in a pw-cli read that honours the token, so a window expiring there
    // brings OperationCanceledException out of the probe call itself.
    var bt = new FakeBluetoothService
    {
      ProbeHandler = async (_, ct) =>
      {
        await Task.Delay(Timeout.Infinite, ct);
        return false;
      }
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.True(counters.SwitchedToBluetooth);
  }

  [Fact]
  public async Task NodeArrivesDuringProbe_SwitchesWithoutWaitingOutTheFallback()
  {
    // The node appears while a probe is in flight, and that probe's answer predates it.
    // Nothing raises the event again, so missing it costs the whole 60 s fallback.
    var bt = new FakeBluetoothService();
    bt.ProbeHandler = (address, _) =>
    {
      bt.RaiseCaptureNodeAvailable(address);
      return Task.FromResult(false);
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    // A window longer than the rendezvous: the event has to end the probe, not the window.
    using var svc = CreateService(bt, audioMock, probeMs: 60000, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.True(counters.SwitchedToBluetooth);
    Assert.False(metrics.Deferred.Task.IsCompleted, "A node found during the probe is not a deferral");
  }

  [Fact]
  public async Task NodeEventForAnotherDevice_DoesNotEndTheWait()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    bt.RaiseCaptureNodeAvailable("11:22:33:44:55:66");
    // Bounded negative check: starvation can only weaken it, never fail it (CLAUDE.md § Test Timing).
    await Task.Delay(100);
    Assert.False(counters.SwitchedToBluetooth);

    bt.RaiseCaptureNodeAvailable("aa:bb:cc:dd:ee:ff"); // address match ignores case
    await counters.Switched.Task.WaitAsync(Rendezvous);
  }

  [Fact]
  public async Task BluetoothBecomesActiveDuringProbe_DoesNotSwitchAgain()
  {
    // Seen on the box 2026-11-05: the Bluetooth source was picked by hand within seconds of the
    // phone connecting. A probe that started before that must not run a second switch after it.
    var activeSource = new Mock<IAudioSource>();
    activeSource.Setup(s => s.Type).Returns(AudioSourceType.Bluetooth);
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var bt = new FakeBluetoothService();
    bt.ProbeHandler = (_, _) =>
    {
      audioMock.Setup(m => m.ActiveSource).Returns(activeSource.Object);
      return Task.FromResult(true);
    };
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task BluetoothBecomesActiveDuringDeferredWait_DoesNotSwitchAgain()
  {
    var activeSource = new Mock<IAudioSource>();
    activeSource.Setup(s => s.Type).Returns(AudioSourceType.Bluetooth);
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var bt = new FakeBluetoothService();
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    audioMock.Setup(m => m.ActiveSource).Returns(activeSource.Object);
    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  // ---- The owner's ruling, 2026-11-06: "bluetooth should pause the current stream whenever it
  // connects." A connected phone takes over when its audio is ready, however long that takes. ----

  [Fact]
  public async Task NodeArrivesLongAfterConnect_StillSwitches()
  {
    // The connect-idle case: the phone connects, sits idle, and is played later. There is no
    // deadline any more, so the only things that can end this wait are the node and the device.
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    // Bounded on purpose, and safe under load: starvation can only make it a weaker check, never a
    // failing one (CLAUDE.md § Test Timing). It catches a short deadline, not a long one: what stops
    // a 60 s timer coming back is that the wait in the service takes no timeout at all.
    await Task.Delay(300);
    Assert.False(metrics.Abandoned.Task.IsCompleted, "A connected phone must not be given up on");
    Assert.Equal(1, bt.CaptureNodeAvailableSubscriberCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task DeviceDisconnects_StopsWaiting_AndALateNodeDoesNotSwitch()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    bt.SimulateDeviceDisconnected("aa:bb:cc:dd:ee:ff"); // address match ignores case
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task DeviceDisconnectsDuringProbe_StopsTheProbe_WithoutWaitingOutItsWindow()
  {
    // A disconnect mid-probe must cancel the pw-cli read, not leave it to run out the window: a
    // flapping phone would otherwise stack probes up on a box that distorts under subprocess churn.
    var probing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var bt = new FakeBluetoothService
    {
      ProbeHandler = async (_, ct) =>
      {
        probing.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        return true;
      }
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    // A window longer than the rendezvous, so only the disconnect can end this probe.
    using var svc = CreateService(bt, audioMock, probeMs: 60000, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await probing.Task.WaitAsync(Rendezvous);

    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
    Assert.False(metrics.Deferred.Task.IsCompleted, "A device that left is not a deferral");
  }

  [Fact]
  public async Task RepeatedConnectForTheSameDevice_LeavesOneWaiter()
  {
    // L2: a repeated connect (a flap with no disconnect seen, or a duplicated event) must not stack
    // a second subscription, a second round of pw-cli probes, or a second switch.
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    bt.SimulateDeviceConnected("aa:bb:cc:dd:ee:ff");
    Assert.Equal(1, bt.CaptureNodeAvailableSubscriberCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task ReconnectAfterADisconnect_WaitsAgain()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);
    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");

    // Connecting straight back must not be swallowed by the session that is still winding down.
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);

    // The first waiter's cleanup must not remove the second one's entry, or the next repeated
    // connect would stack a waiter beside it. Bounded so that cleanup has run: starvation can only
    // weaken this check (CLAUDE.md § Test Timing).
    await Task.Delay(50);
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    Assert.Equal(1, bt.CaptureNodeAvailableSubscriberCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task Dispose_EndsTheWait()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    svc.Dispose();
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task AutoSwitchTurnedOffDuringTheWait_DoesNotSwitch()
  {
    // The wait can now outlast any setting change, so the option is read when the node arrives.
    var options = new BluetoothOptions { AutoSwitchOnConnect = true, AutoSwitchProbeWindowMs = 50 };
    var optionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    optionsMock.Setup(o => o.CurrentValue).Returns(options);
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = new BluetoothAutoSwitchService(
      Mock.Of<ILogger<BluetoothAutoSwitchService>>(), bt, optionsMock.Object, () => audioMock.Object,
      metricsCollector: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    options.AutoSwitchOnConnect = false;
    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task HandPickedSourceBeforeTheNodeArrives_IsStillReplaced()
  {
    // Explicitly the ruling: "Should a late Bluetooth auto-switch replace a source you picked? Yes."
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters, AudioSourceType.Bluetooth);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    // Bluetooth is active as the phone connects, so there is nothing to switch yet...
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    // ...then the owner picks the radio while the phone sits idle, and presses play on the phone.
    var radio = new Mock<IAudioSource>();
    radio.Setup(s => s.Type).Returns(AudioSourceType.Radio);
    audioMock.Setup(m => m.ActiveSource).Returns(radio.Object);
    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task OneTakeoverPerConnection_ALaterNodeLeavesAHandPickedSourceAlone()
  {
    // The node comes and goes with every pause on the phone (AUD-10). Only the first one of a
    // connection takes over; after that the owner's own choice of source stands.
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");

    Assert.Equal(1, counters.SwitchCount);
    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task CaptureIsBound_BeforeTheOldSourceIsStopped()
  {
    // An unbound A2DP node lives about 4 s (AUD-10), and stopping FM can itself block for about
    // that long: RadioReceiver.Shutdown joins for up to 2 s, then RtlSdrDevice.StopStreaming waits
    // for up to 2 s. So the node is bound first, which the switch's own stop cannot then outlast.
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var requestsAtSwitch = -1;
    audioMock.Setup(m => m.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, true, It.IsAny<CancellationToken>()))
      .Callback(() =>
      {
        requestsAtSwitch = bt.CaptureRequests;
        counters.Switched.TrySetResult();
      })
      .ReturnsAsync((IAudioSource?)null);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, requestsAtSwitch);
  }

  [Fact]
  public async Task NodeGoneBeforeItCouldBeBound_LeavesTheRadio_AndTheNextNodeSwitches()
  {
    // A notification sound: the node appears and goes before anything binds it. Stopping the radio
    // then would leave silence with no audio to replace it, against what the trigger is for.
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var bindAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bt.CaptureHandler = _ =>
    {
      if (bt.CaptureRequests == 1)
      {
        bindAttempted.TrySetResult();
        return Task.FromResult<object?>(null);
      }
      return Task.FromResult<object?>(new object());
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await bindAttempted.Task.WaitAsync(Rendezvous);

    // Bounded negative check: starvation can only weaken it (CLAUDE.md § Test Timing).
    await Task.Delay(100);
    Assert.Equal(0, counters.SwitchCount);
    Assert.Equal(1, bt.CaptureNodeAvailableSubscriberCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task NodeThatArrivedByEventAndCouldNotBeBound_WaitsForTheNextOne_RatherThanSearchingAgain()
  {
    // Here the first node came by event, so that signal is spent. Waiting on it again would return at
    // once and turn a failed bind into 20 s searches back to back for as long as the phone stays.
    var bt = new FakeBluetoothService();
    bt.CaptureHandler = _ => Task.FromResult(bt.CaptureRequests == 1 ? null : new object());
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);
    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");

    // Bounded negative check: starvation can only weaken it (CLAUDE.md § Test Timing).
    await Task.Delay(100);
    Assert.Equal(1, bt.CaptureRequests);
    Assert.Equal(0, counters.SwitchCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(2, bt.CaptureRequests);
  }

  [Fact]
  public async Task NodeEventDuringABindThatFails_IsNotLost()
  {
    // The bind can take 20 s, and nothing raises the event again for a node that stays up.
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    bt.CaptureHandler = _ =>
    {
      if (bt.CaptureRequests == 1)
      {
        bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
        return Task.FromResult<object?>(null);
      }
      return Task.FromResult<object?>(new object());
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task BindThatFailsAndIsThenLandedByThePipelineMonitor_StillSwitches()
  {
    // The monitor binds a node without any node event, and a bound node stays up. Without this the
    // phone would stream into a generator nothing reads, with the radio still playing.
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var bindFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bt.CaptureHandler = _ =>
    {
      if (bt.CaptureRequests == 1)
      {
        bindFailed.TrySetResult();
        return Task.FromResult<object?>(null);
      }
      return Task.FromResult<object?>(new object());
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await bindFailed.Task.WaitAsync(Rendezvous);
    await Task.Delay(50); // so the wait is armed; starvation only makes the check weaker

    bt.RaiseCaptureStreamRecovered();
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task BindThatThrows_LeavesTheRadio_AndTheNextNodeStillSwitches()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var bindThrew = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bt.CaptureHandler = _ =>
    {
      if (bt.CaptureRequests == 1)
      {
        bindThrew.TrySetResult();
        throw new InvalidOperationException("teardown failed");
      }
      return Task.FromResult<object?>(new object());
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await bindThrew.Task.WaitAsync(Rendezvous);
    await Task.Delay(50); // so the wait is armed; starvation only makes the check weaker
    Assert.Equal(0, counters.SwitchCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task DeviceDisconnectsDuringTheBind_CancelsIt_AndDoesNotSwitch()
  {
    // The bind can sit in a 20 s search. A phone that leaves meanwhile must end it, not stop the radio.
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var binding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bt.CaptureHandler = async ct =>
    {
      binding.TrySetResult();
      await Task.Delay(Timeout.Infinite, ct);
      return new object();
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 60000, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await binding.Task.WaitAsync(Rendezvous);

    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task DeviceDisconnectsAsTheBindReturns_DoesNotSwitch()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    bt.CaptureHandler = _ =>
    {
      bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");
      return Task.FromResult<object?>(new object());
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 60000, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task TakingOverFromAFile_KeepsItsPlace()
  {
    // The whole of "pause" for the one source that has a place to keep. See
    // FilePlayerAudioSourceTests for what the file player does with it.
    var dir = Path.Combine(Path.GetTempPath(), $"BtTakeover_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
      File.WriteAllText(Path.Combine(dir, "test.mp3"), "test");
      var file = BuildFilePlayer(dir);
      await file.LoadFileAsync("test.mp3");
      await file.PlayAsync();
      await file.SeekAsync(TimeSpan.FromMinutes(2));

      var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
      var counters = new FakeAudioManagerCounters();
      var audioMock = MakeAudioMock(counters);
      audioMock.Setup(m => m.ActiveSource).Returns(file);
      using var svc = CreateService(bt, audioMock, probeMs: 60000);

      bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
      await counters.Switched.Task.WaitAsync(Rendezvous);

      // What AudioManager.SwitchSourceAsync does to the old source, then the owner pressing play.
      await file.StopAsync();
      await file.PlayAsync();

      Assert.Equal(TimeSpan.FromMinutes(2), file.Position);
      await file.WhenQueueMetadataIdleAsync();
      await file.DisposeAsync();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static FilePlayerAudioSource BuildFilePlayer(string rootDirectory)
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.Setup(o => o.CurrentValue).Returns(new FilePlayerOptions
    {
      RootDirectory = "",
      SupportedExtensions = [".mp3"]
    });
    var preferences = new Mock<IOptionsMonitor<FilePlayerPreferences>>();
    preferences.Setup(o => o.CurrentValue).Returns(new FilePlayerPreferences());
    return new FilePlayerAudioSource(
      Mock.Of<ILogger<FilePlayerAudioSource>>(), options.Object, preferences.Object, rootDirectory);
  }

  // ---- Owner's ruling, 2026-11-06: "Should Bluetooth take over the instant a phone connects?" -
  // "Yes". The source stops at the connect, with nothing yet playing on the phone. ----

  [Fact]
  public async Task AudioSourcePhone_TakesOverAtTheConnect_WithNoNodeYet()
  {
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true, IsCaptureNodeAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 60000, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
    Assert.False(metrics.Deferred.Task.IsCompleted, "Nothing waits for the node on this path");
    // No node exists to bind: the Bluetooth source binds it when it appears.
    Assert.Equal(0, bt.CaptureRequests);
  }

  [Fact]
  public async Task AudioSourcePhone_ThatDropsWithinTheSettle_LeavesTheRadio()
  {
    // A failed connect: the link comes up and falls straight back down. M3.
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, metrics: metrics.Mock.Object, settleMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task DeviceThatDoesNotSendAudio_LeavesTheRadio_UntilANodeProvesOtherwise()
  {
    // M3: DeviceConnected reports the link, which a keyboard, a speaker or a call profile can open.
    // A phone paired moments ago reads the same here, so a node still takes over for it.
    var bt = new FakeBluetoothService { IsAudioSourceDevice = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);
    Assert.Equal(0, counters.SwitchCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task ConnectionAlreadyUpAtStartup_IsNotAConnect()
  {
    // A link already up when the service starts, as after a crash, and the owner may be on a source
    // picked since. It takes over if the phone plays, but nothing connected just now.
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF", alreadyConnected: true);
    await metrics.Deferred.Task.WaitAsync(Rendezvous);
    Assert.Equal(0, counters.SwitchCount);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);
  }

  [Fact]
  public async Task PhoneBackWithinTheWindow_IsTheSameConnection_SoAPickedSourceStays()
  {
    // A PBAP sync's OBEX teardown can drop the link and reconnect it 3 s later, and a flap looks
    // the same. Neither is the phone arriving, so neither may undo a source picked since.
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, metrics: metrics.Mock.Object, timeProvider: time);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.TookOver.Task.WaitAsync(Rendezvous);

    // PBAP's teardown reports LocalHost.
    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF", reason: BluetoothDisconnectReason.LocalHost);
    time.Advance(TimeSpan.FromSeconds(59));
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");

    // A flap reports a timeout, and a second one is still inside the window of its own disconnect.
    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF", reason: BluetoothDisconnectReason.Timeout);
    time.Advance(TimeSpan.FromSeconds(59));
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    // Bounded negative check: starvation can only weaken it (CLAUDE.md § Test Timing).
    await Task.Delay(100);
    Assert.Equal(1, counters.SwitchCount);
    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task PhoneBackAfterTheWindow_IsANewConnection()
  {
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, metrics: metrics.Mock.Object, timeProvider: time);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.TookOver.Task.WaitAsync(Rendezvous);

    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");
    time.Advance(TimeSpan.FromSeconds(61));
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    await counters.AtLeastSwitches(2).WaitAsync(Rendezvous);
    Assert.Equal(2, counters.SwitchCount);
  }

  [Theory]
  [InlineData(true, BluetoothDisconnectReason.LocalHost)] // the console's own Disconnect
  [InlineData(false, BluetoothDisconnectReason.Remote)]   // Bluetooth turned off on the phone
  public async Task PhoneBackAfterAUserDisconnect_IsANewConnection_HoweverSoon(
    bool userInitiated, BluetoothDisconnectReason reason)
  {
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, metrics: metrics.Mock.Object, timeProvider: time);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.TookOver.Task.WaitAsync(Rendezvous);

    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF", userInitiated, reason);
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    await counters.AtLeastSwitches(2).WaitAsync(Rendezvous);
  }

  [Fact]
  public async Task ADisconnectThatNeverArrived_CannotHoldBackTheNextConnection()
  {
    // Eviction of a Device1 raises no DeviceDisconnected, so "still connected" cannot be assumed.
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, metrics: metrics.Mock.Object, timeProvider: time);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.TookOver.Task.WaitAsync(Rendezvous);

    time.Advance(TimeSpan.FromSeconds(61));
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    await counters.AtLeastSwitches(2).WaitAsync(Rendezvous);
  }

  [Fact]
  public async Task PhoneAlreadyStreaming_IsBound_BeforeTheOldSourceIsStopped()
  {
    // The takeover starts after the settle, so a playing phone has a node by then, and FM's stop can
    // last as long as an unbound node does.
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true, IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var requestsAtSwitch = -1;
    audioMock.Setup(m => m.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, true, It.IsAny<CancellationToken>()))
      .Callback(() =>
      {
        requestsAtSwitch = bt.CaptureRequests;
        counters.Switched.TrySetResult();
      })
      .ReturnsAsync((IAudioSource?)null);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, requestsAtSwitch);
  }

  [Fact]
  public async Task NodeThatAppearsDuringTheOldSourcesStop_IsBoundAtOnce()
  {
    // The Bluetooth source is not active yet, so it leaves this node alone.
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true };
    var bound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bt.CaptureHandler = _ =>
    {
      bound.TrySetResult();
      return Task.FromResult<object?>(new object());
    };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var stopping = new TaskCompletionSource<IAudioSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var stopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    audioMock.Setup(m => m.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, true, It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        stopStarted.TrySetResult();
        return stopping.Task;
      });
    using var svc = CreateService(bt, audioMock);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await stopStarted.Task.WaitAsync(Rendezvous);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await bound.Task.WaitAsync(Rendezvous);

    stopping.SetResult(null);
  }

  [Fact]
  public async Task AFailedBindAtTheConnect_StillTakesOver()
  {
    // The connect decided this. The node path leaves the radio on when a bind fails, because there
    // the node was its only evidence; here the source then binds what the phone sends next.
    var bt = new FakeBluetoothService { IsAudioSourceDevice = true, IsCaptureNodeAvailable = true };
    bt.CaptureHandler = _ => Task.FromResult<object?>(null);
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.Switched.Task.WaitAsync(Rendezvous);

    Assert.Equal(1, counters.SwitchCount);
  }

  [Fact]
  public async Task ADeviceThatLeftBeforeTakingOver_GetsNoSameConnectionTreatment()
  {
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, metrics: metrics.Mock.Object, timeProvider: time);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);
    bt.SimulateDeviceDisconnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);

    bt.IsAudioSourceDevice = true;
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    await counters.Switched.Task.WaitAsync(Rendezvous);
  }

  // ---- Owner's ruling, 2026-11-06: "Select FM Radio (last station played) if no BT available after
  // restart from playing BT audio." ----

  [Fact]
  public async Task RestoredBluetooth_WithNoPhone_StandsInTheRadio_WithoutActivatingBluetooth()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock);

    await svc.ActivateRestoredBluetoothAsync();

    // Created, so the adapter is up and a phone can connect, but never active: an active Bluetooth
    // source with no phone is what reported isPlaying:true.
    audioMock.Verify(m => m.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, false, It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal(0, counters.SwitchCount);
    audioMock.Verify(m => m.ActivateStandInSourceAsync(AudioSourceType.Radio, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task RestoredBluetooth_WithThePhoneThere_StaysOnBluetooth()
  {
    // The phone is only known once the adapter has started, so it appears during the create.
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    audioMock.Setup(m => m.GetOrCreateSourceAsync(AudioSourceType.Bluetooth, false, It.IsAny<CancellationToken>()))
      .Callback(() => bt.ConnectedDevice = new BluetoothDeviceInfo { Address = "AA:BB:CC:DD:EE:FF", Name = "Test Phone" })
      .ReturnsAsync((IAudioSource?)null);
    using var svc = CreateService(bt, audioMock);

    await svc.ActivateRestoredBluetoothAsync();

    Assert.Equal(1, counters.SwitchCount);
    audioMock.Verify(m => m.ActivateStandInSourceAsync(It.IsAny<AudioSourceType>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task AlreadyActiveBluetoothSource_SkipsSwitch()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters, AudioSourceType.Bluetooth);
    using var svc = CreateService(bt, audioMock, probeMs: 1000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await Task.Delay(200);

    Assert.False(counters.GetOrCreateCalled);
  }

  [Fact]
  public async Task AutoSwitchDisabled_SkipsEntirely()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 1000, autoSwitchEnabled: false);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await Task.Delay(200);

    Assert.False(counters.GetOrCreateCalled);
  }

  // ---- Existing safety nets ----

  [Fact]
  public async Task OnBluetoothDeviceConnected_Skips_WhenAdapterUnavailable()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true, IsAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await Task.Delay(200);

    Assert.False(counters.GetOrCreateCalled);
  }

  [Fact]
  public async Task Dispose_UnsubscribesFromEvent()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var svc = CreateService(bt, audioMock);

    svc.Dispose();
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await Task.Delay(150);

    // No GetOrCreate calls should happen after dispose — handler was unsubscribed.
    audioMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public void Dispose_IsSafeToCallTwice()
  {
    var bt = new FakeBluetoothService();
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var svc = CreateService(bt, audioMock);
    svc.Dispose();
    svc.Dispose();
  }
}
