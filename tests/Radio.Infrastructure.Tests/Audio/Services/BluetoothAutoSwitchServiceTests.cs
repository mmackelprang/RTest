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

    // TEST-10: what the service did, as events a test can await instead of sleeping and assuming.
    private readonly TaskCompletionSource _subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _unsubscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the service first subscribes to CaptureNodeAvailable.</summary>
    public Task SubscribedTask => _subscribed.Task;

    /// <summary>Completes when the service has removed its last CaptureNodeAvailable handler — which it
    /// does in a <c>finally</c>, after it has decided whether to switch.</summary>
    public Task UnsubscribedTask => _unsubscribed.Task;

    public bool EverSubscribed => _subscribed.Task.IsCompleted;

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
          lock (_captureHandlers)
          {
            _captureHandlers.Add(value);
            CaptureNodeAvailableSubscriberCount = _captureHandlers.Count;
          }
          _subscribed.TrySetResult();
        }
      }
      remove
      {
        if (value == null)
        {
          return;
        }
        bool nowEmpty;
        lock (_captureHandlers)
        {
          if (!_captureHandlers.Remove(value))
          {
            return;
          }
          CaptureNodeAvailableSubscriberCount = _captureHandlers.Count;
          nowEmpty = _captureHandlers.Count == 0;
        }
        if (nowEmpty)
        {
          _unsubscribed.TrySetResult();
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
      => Task.FromResult(IsCaptureNodeAvailable);
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
      EventHandler<CaptureNodeAvailableEventArgs>[] handlers;
      lock (_captureHandlers)
      {
        handlers = _captureHandlers.ToArray();
      }
      foreach (var h in handlers)
      {
        h(this, args);
      }
    }
  }

  private sealed class FakeAudioManagerCounters
  {
    private readonly TaskCompletionSource _switched = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool GetOrCreateCalled { get; set; }
    public bool SwitchedToBluetooth => _switched.Task.IsCompleted;

    /// <summary>Completes when the service asks to switch to Bluetooth (TEST-10).</summary>
    public Task SwitchedToBluetoothTask => _switched.Task;

    public void MarkSwitchedToBluetooth() => _switched.TrySetResult();
  }

  private static BluetoothAutoSwitchService CreateService(
    FakeBluetoothService bt,
    Mock<IAudioManager> audioMock,
    int probeMs = 200,
    int maxWaitMs = 1000,
    bool autoSwitchEnabled = true)
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
      metricsCollector: null);
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
          counters.MarkSwitchedToBluetooth();
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
  //
  // TEST-10: none of these tests sleeps and then assumes the service has done something. The service's
  // handler is `async void` and runs its own probe-window and max-wait timers; a test that slept for a
  // fixed time and then asserted raced those timers, and lost under load (NodeArrivesAfterProbe failed a
  // real merge gate). Instead each test waits on something the service itself does — subscribing to
  // CaptureNodeAvailable, unsubscribing (which it does in a `finally`, after its decision), or switching —
  // with a generous timeout that is only a safety net. See CLAUDE.md § Test Timing.
  //
  // The skip tests need no wait at all: the handler returns before its first `await` on every skip path,
  // so the decision is complete by the time SimulateDeviceConnected returns.

  /// <summary>Upper bound on any wait below. Never the thing a test is timing.</summary>
  private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(30);

  [Fact]
  public async Task NodeReadyInsideProbeWindow_SwitchesImmediately()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 1000, maxWaitMs: 30000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    await counters.SwitchedToBluetoothTask.WaitAsync(SafetyNet);

    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
    Assert.False(bt.EverSubscribed);
  }

  [Fact]
  public async Task NodeArrivesAfterProbe_SwitchesViaEvent()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 200, maxWaitMs: 60000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    // The probe window has expired and the service has fallen back to the event — observed, not assumed.
    await bt.SubscribedTask.WaitAsync(SafetyNet);
    Assert.False(counters.SwitchedToBluetooth);

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    await counters.SwitchedToBluetoothTask.WaitAsync(SafetyNet);
    await bt.UnsubscribedTask.WaitAsync(SafetyNet);

    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task NodeNeverArrives_TimesOutWithoutSwitch()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 100, maxWaitMs: 300);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    // The service unsubscribes in a `finally` after its max-wait decision, so once it has unsubscribed
    // the decision is final — however long the timers took to fire under load.
    await bt.SubscribedTask.WaitAsync(SafetyNet);
    await bt.UnsubscribedTask.WaitAsync(SafetyNet);

    Assert.False(counters.SwitchedToBluetooth);
    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public void AlreadyActiveBluetoothSource_SkipsSwitch()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters, AudioSourceType.Bluetooth);
    using var svc = CreateService(bt, audioMock, probeMs: 1000, maxWaitMs: 30000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    Assert.False(counters.GetOrCreateCalled);
  }

  [Fact]
  public void AutoSwitchDisabled_SkipsEntirely()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 1000, maxWaitMs: 30000, autoSwitchEnabled: false);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    Assert.False(counters.GetOrCreateCalled);
  }

  // ---- Existing safety nets ----

  [Fact]
  public void OnBluetoothDeviceConnected_Skips_WhenAdapterUnavailable()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true, IsAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    Assert.False(counters.GetOrCreateCalled);
  }

  [Fact]
  public void Dispose_UnsubscribesFromEvent()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = true };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    var svc = CreateService(bt, audioMock);

    svc.Dispose();
    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    // No handler remains to run. (Were one still subscribed, it would reach GetOrCreateSourceAsync
    // synchronously here: the fake's probe and the mock's switch both complete without yielding.)
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
