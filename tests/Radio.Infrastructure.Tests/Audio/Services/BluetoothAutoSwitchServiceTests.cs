using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
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
    public float? DeviceVolume => null;
    public bool IsReconnecting => false;
    public BluetoothDisconnectReason? LastDisconnectReason => null;
    public BluetoothPipelineStatus PipelineStatus => BluetoothPipelineStatus.Healthy;

    public int CaptureNodeAvailableSubscriberCount { get; private set; }

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
    public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected
    { add { } remove { } }
    public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered
    { add { } remove { } }
    public event EventHandler? CaptureStreamRecovered { add { } remove { } }
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
      => Task.FromResult<object?>(null);
    public void StopAudioCapture() { }
    public Task<bool> IsCaptureNodeAvailableAsync(string deviceAddress, CancellationToken cancellationToken = default)
      => ProbeHandler?.Invoke(deviceAddress, cancellationToken) ?? Task.FromResult(IsCaptureNodeAvailable);
    public Task SetDeviceVolumeAsync(float volume) => Task.CompletedTask;
    public Task NextTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PreviousTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void CancelReconnection() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void SimulateDeviceConnected(string address)
    {
      var device = new BluetoothDeviceInfo
      {
        Address = address,
        Name = "Test Phone",
        IsPaired = true,
        IsConnected = true
      };
      ConnectedDevice = device;
      DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
    }

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
    public int SwitchCount { get; set; }
    public TaskCompletionSource Switched { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    int maxWaitMs = 1000,
    bool autoSwitchEnabled = true,
    IMetricsCollector? metrics = null)
  {
    var optionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    optionsMock.Setup(o => o.CurrentValue).Returns(new BluetoothOptions
    {
      EnableOnStartup = true,
      AutoSwitchOnConnect = autoSwitchEnabled,
      AutoSwitchProbeWindowMs = probeMs,
      AutoSwitchMaxWaitMs = maxWaitMs
    });

    return new BluetoothAutoSwitchService(
      Mock.Of<ILogger<BluetoothAutoSwitchService>>(),
      bt,
      optionsMock.Object,
      () => audioMock.Object,
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
    using var svc = CreateService(bt, audioMock, probeMs: 60000, maxWaitMs: 60000);

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
    // maxWaitMs is far longer than any rendezvous: the deferred wait must end on the event.
    using var svc = CreateService(bt, audioMock, probeMs: 50, maxWaitMs: 60000, metrics: metrics.Mock.Object);

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
    using var svc = CreateService(bt, audioMock, probeMs: 50, maxWaitMs: 60000, metrics: metrics.Mock.Object);

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
    using var svc = CreateService(bt, audioMock, probeMs: 60000, maxWaitMs: 60000, metrics: metrics.Mock.Object);

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
    using var svc = CreateService(bt, audioMock, probeMs: 50, maxWaitMs: 60000, metrics: metrics.Mock.Object);

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
    using var svc = CreateService(bt, audioMock, probeMs: 60000, maxWaitMs: 60000);

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
    using var svc = CreateService(bt, audioMock, probeMs: 50, maxWaitMs: 60000, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Deferred.Task.WaitAsync(Rendezvous);

    audioMock.Setup(m => m.ActiveSource).Returns(activeSource.Object);
    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.Equal(0, counters.SwitchCount);
  }

  [Fact]
  public async Task NodeNeverArrives_TimesOutWithoutSwitch()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var metrics = new MetricSignals();
    using var svc = CreateService(bt, audioMock, probeMs: 50, maxWaitMs: 100, metrics: metrics.Mock.Object);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await metrics.Abandoned.Task.WaitAsync(Rendezvous);
    await bt.AllUnsubscribed.Task.WaitAsync(Rendezvous);

    Assert.False(counters.SwitchedToBluetooth);
    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task AlreadyActiveBluetoothSource_SkipsSwitch()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters, AudioSourceType.Bluetooth);
    using var svc = CreateService(bt, audioMock, probeMs: 1000, maxWaitMs: 30000);

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
    using var svc = CreateService(bt, audioMock, probeMs: 1000, maxWaitMs: 30000, autoSwitchEnabled: false);

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
