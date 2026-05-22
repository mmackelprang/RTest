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

    public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged
    { add { } remove { } }
    public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
    public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected
    { add { } remove { } }
    public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered
    { add { } remove { } }
    public event EventHandler? CaptureStreamRecovered { add { } remove { } }
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
        }
      }
    }

    // Codec observability — added by PR #389 (codec observability); the FakeBluetoothService
    // is unused by codec-related code paths, so a permanent null-impl is correct here.
    public event EventHandler<A2dpCodecChangedEventArgs>? A2dpCodecChanged { add { } remove { } }
    public Task<A2dpCodecInfo?> GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct = default)
      => Task.FromResult<A2dpCodecInfo?>(null);

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
          counters.SwitchedToBluetooth = true;
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
    using var svc = CreateService(bt, audioMock, probeMs: 1000, maxWaitMs: 30000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
    // Allow async-void handler to complete its probe — probe returns true on the first
    // iteration because IsCaptureNodeAvailable is set.
    await Task.Delay(150);

    Assert.True(counters.SwitchedToBluetooth);
    Assert.Equal(0, bt.CaptureNodeAvailableSubscriberCount);
  }

  [Fact]
  public async Task NodeArrivesAfterProbe_SwitchesViaEvent()
  {
    var bt = new FakeBluetoothService { IsCaptureNodeAvailable = false };
    var counters = new FakeAudioManagerCounters();
    var audioMock = MakeAudioMock(counters);
    using var svc = CreateService(bt, audioMock, probeMs: 200, maxWaitMs: 5000);

    bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");

    // Wait long enough for the probe window to expire and the event subscription to be active.
    await Task.Delay(500);
    Assert.False(counters.SwitchedToBluetooth);
    Assert.True(bt.CaptureNodeAvailableSubscriberCount >= 1, "Expected event subscription active");

    bt.RaiseCaptureNodeAvailable("AA:BB:CC:DD:EE:FF");
    // Allow async continuation to complete the switch.
    await Task.Delay(150);

    Assert.True(counters.SwitchedToBluetooth);
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
    // Wait for probe (100ms) + timeout (300ms) + slack.
    await Task.Delay(700);

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
