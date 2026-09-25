using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Platform.Bluetooth;
using Radio.Infrastructure.Platform.Bluetooth.Native;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// AUD-10 + AUD-11: the capture stream follows the BT node across a pause/resume, and is torn down
/// (not re-linked elsewhere) when the node goes. Plan AUD-10 §5, T2-T5; plan AUD-11 §4.4.
/// </summary>
/// <remarks>
/// <para>
/// These drive the REAL <see cref="LinuxBluetoothService"/> through its registry event handlers —
/// the same methods <c>PipeWireRegistryListener</c> invokes — with two seams: a fake capture stream
/// (a real <c>PipeWireNativeStream</c> calls pw_init) and a no-op post-bind maintenance (it shells
/// out to pw-link/wpctl 1.5 s later). The ids and serials are the ones measured on the appliance on
/// 2026-09-25 (docs/queue/AUD-10.md): node <c>bluez_input.B0_D5_FB_D2_0D_68.2</c>, registry id 76,
/// serial 58968 before the pause and 59112 after.
/// </para>
/// <para>
/// ⚠ CLAUDE.md § Test Timing: nothing here sleeps. Registry work runs on an ordered thread-pool
/// queue; tests rendezvous with it through <c>WhenRegistryWorkIdleAsync()</c>, and T4 holds the
/// fake stream's Start() on a gate so the overlap it tests is forced rather than hoped for.
/// </para>
/// <para>
/// ⚠ What these cannot show: that starting a stream against the new serial inside the ~4 s window
/// makes BlueZ's transport go <c>active</c> and audio return. That is PipeWire's behaviour, and it
/// is only observable at the cabinet (plan AUD-10 §5.1, §7).
/// </para>
/// </remarks>
public class CaptureNodeFollowTests
{
  private const string Mac = "B0:D5:FB:D2:0D:68";
  private const string NodeName = "bluez_input.B0_D5_FB_D2_0D_68.2";
  private const string OtherMac = "D4:3A:2C:64:87:9E";

  private static readonly AudioFormat Format =
    new() { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };

  private sealed class FakeStream : IBtCaptureStream
  {
    private readonly Func<Task>? _startGate;
    public FakeStream(uint serial, Func<Task>? startGate) { TargetNodeSerial = serial; _startGate = startGate; }
    public uint TargetNodeSerial { get; }
    public int StartCount;
    public bool Disposed;
    public void Start()
    {
      Interlocked.Increment(ref StartCount);
      _startGate?.Invoke().GetAwaiter().GetResult();
    }
    public long MillisecondsSinceLastOnProcess() => 0;
    public void Dispose() => Disposed = true;
  }

  private sealed class Harness
  {
    public LinuxBluetoothService Service { get; }
    public List<FakeStream> Streams { get; } = new();
    public BufferedSoundGenerator<float> Generator { get; }
    public Func<Task>? StartGate { get; set; }
    public List<CaptureTargetLostEventArgs> Lost { get; } = new();
    public List<CaptureNodeAvailableEventArgs> Available { get; } = new();

    public Harness(string connectedMac = Mac)
    {
      Service = new LinuxBluetoothService(
        NullLogger.Instance, Options.Create(new BluetoothOptions()));
      Service.CaptureStreamFactory = (serial, _, _) =>
      {
        var s = new FakeStream(serial, StartGate);
        lock (Streams) { Streams.Add(s); }
        return s;
      };
      Service.PostBindMaintenanceOverride = (_, _, _) => { };
      Service.CaptureTargetLost += (_, e) => { lock (Lost) { Lost.Add(e); } };
      Service.CaptureNodeAvailable += (_, e) => { lock (Available) { Available.Add(e); } };
      Service.PrimeConnectedDeviceForTests(new BluetoothDeviceInfo
      {
        Address = connectedMac, Name = "Pixel", IsPaired = true, IsConnected = true,
      });
      Generator = new BufferedSoundGenerator<float>(null!, Format, NullLogger.Instance);
    }

    public int StartedStreams { get { lock (Streams) { return Streams.Sum(s => s.StartCount); } } }

    public void BindInitial(int serial = 58968) =>
      Assert.True(Service.BindNativeCaptureForTests(Generator, Format, NodeName, serial));

    public static BtNodeRegistryEventArgs Node(uint id, uint serial, string mac = Mac) => new()
    {
      Id = id,
      ObjectSerial = serial,
      NodeName = $"bluez_input.{mac.Replace(':', '_')}.2",
      DeviceAddress = mac,
    };
  }

  // --- The teardown predicate (plan AUD-11 §4.4 option 1) -----------------------------------------

  [Theory]
  [InlineData(Mac, Mac, true, true)]                        // ours, running -> tear down
  [InlineData(Mac, Mac, false, false)]                      // ours, no stream -> nothing
  [InlineData(Mac, OtherMac, true, false)]                  // another device's node -> must NOT kill healthy playback
  [InlineData(null, Mac, true, false)]                      // nothing connected
  [InlineData("b0:d5:fb:d2:0d:68", Mac, true, true)]        // case differs (BlueZ mixed, PipeWire upper)
  public void ShouldTearDownForRemovedNode_Table(
    string? connected, string removed, bool streamRunning, bool expected)
  {
    Assert.Equal(expected,
      LinuxBluetoothService.ShouldTearDownForRemovedNode(connected, removed, streamRunning));
  }

  // --- T2, second half: the event publishes the serial, not the registry id -----------------------

  [Fact]
  public async Task NodeAppeared_PublishesObjectSerial_NotTheRegistryId()
  {
    var h = new Harness();

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    var evt = Assert.Single(h.Available);
    Assert.Equal(58968, evt.PipeWireSerial);
    Assert.Equal(Mac, evt.DeviceAddress);
  }

  // --- T3: disappear -> parked, appear -> re-bound against the NEW serial --------------------------

  [Fact]
  public async Task NodeDisappeared_TearsDownAndParks_WithNoStream()
  {
    var h = new Harness();
    h.BindInitial();
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.True(h.Streams[0].Disposed, "the stream bound to the vanished node must be torn down");
    Assert.Equal(BluetoothPipelineStatus.WaitingForCaptureNode, h.Service.PipelineStatus);
    Assert.Null(h.Service.GetCaptureStreamSnapshot());
    var lost = Assert.Single(h.Lost);
    Assert.Equal(CaptureTargetLostReason.NodeRemoved, lost.Reason);
  }

  [Fact]
  public async Task NodeAppearedForSameDevice_WhileParked_RebindsAgainstTheNewSerial()
  {
    var h = new Harness();
    h.BindInitial(58968);

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Equal(2, h.Streams.Count);
    Assert.Equal(59112u, h.Streams[1].TargetNodeSerial);
    Assert.Equal(1, h.Streams[1].StartCount);
    Assert.False(h.Streams[1].Disposed);
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
  }

  [Fact]
  public async Task NodeRemovedAndReaddedUnderTheSameRegistryId_InOneBurst_StillEndsBound()
  {
    // PipeWire reuses registry ids. Both events are queued before either runs, so this pins that
    // the work queue preserves EVENT ORDER: remove(76) must act before add(76).
    var h = new Harness();
    h.BindInitial(58968);

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 76, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
    Assert.Equal(59112u, h.Streams[^1].TargetNodeSerial);
    Assert.True(h.Streams[0].Disposed);
  }

  [Fact]
  public async Task RemovalOfAnotherNodeForTheSameDevice_LeavesTheBoundStreamAlone()
  {
    // Pre-merge review M1: the teardown used to match by MAC only.
    var h = new Harness();
    h.BindInitial(58968);

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 99, serial: 60000));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.False(h.Streams[0].Disposed);
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
  }

  [Fact]
  public async Task LateRemovalOfTheReplacedNode_DoesNotTearDownTheReboundStream()
  {
    // Pre-merge review M1: a stale removal of the node we already moved off.
    var h = new Harness();
    h.BindInitial(58968);
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 77, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.False(h.Streams[^1].Disposed);
    Assert.Equal(59112u, h.Streams[^1].TargetNodeSerial);
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
  }

  [Fact]
  public async Task ReplacementAppearingBeforeTheOldNodeIsRemoved_IsStillBound()
  {
    // Pre-merge review M2: appear(new) runs while the old stream is still bound and does nothing;
    // then remove(old) parks. No second NodeAppeared will come, so the park must bind the
    // replacement that is already live.
    var h = new Harness();
    h.BindInitial(58968);

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
    Assert.Equal(59112u, h.Streams[^1].TargetNodeSerial);
    Assert.Equal(1, h.Streams.Count(s => !s.Disposed));
  }

  [Fact]
  public async Task Park_MarksTheGeneratorsProducerParked_AndRebindClearsIt()
  {
    // Pre-merge review H1: the parked generator is pulled dry for the whole pause; its underrun
    // Warning must be off while parked and back on once re-bound.
    var h = new Harness();
    h.BindInitial(58968);

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();
    Assert.True(h.Generator.ProducerParked);

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();
    Assert.False(h.Generator.ProducerParked);
  }

  [Fact]
  public async Task NodeAppearedForAnotherDevice_WhileParked_DoesNothing()
  {
    var h = new Harness();
    h.BindInitial();
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 90, serial: 60000, mac: OtherMac));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Single(h.Streams);
    Assert.Equal(BluetoothPipelineStatus.WaitingForCaptureNode, h.Service.PipelineStatus);
  }

  [Fact]
  public async Task NodeDisappearedForAnotherDevice_LeavesTheHealthyStreamAlone()
  {
    var h = new Harness();
    h.BindInitial();

    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 90, serial: 60000, mac: OtherMac));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.False(h.Streams[0].Disposed);
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
    Assert.Empty(h.Lost);
  }

  [Fact]
  public async Task NodeAppeared_WithNothingParked_StartsNothing()
  {
    // A first connect: the node appears before anything has bound. The acquisition path owns that.
    var h = new Harness();

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Empty(h.Streams);
  }

  [Fact]
  public async Task RepeatedPauseResume_RebindsEachTime_AndLeavesExactlyOneLiveStream()
  {
    // Pause is routine; a re-bind that works once and leaks is not a fix (plan AUD-10 §5.1 step 5).
    var h = new Harness();
    h.BindInitial(58968);
    uint serial = 58968;
    for (var i = 0; i < 3; i++)
    {
      // Real pauses and resumes are seconds apart: let each event's work finish before the next.
      h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: serial));
      await h.Service.WhenRegistryWorkIdleAsync();
      Assert.Equal(BluetoothPipelineStatus.WaitingForCaptureNode, h.Service.PipelineStatus);
      serial += 100;
      h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 76, serial: serial));
      await h.Service.WhenRegistryWorkIdleAsync();
    }

    Assert.Equal(4, h.Streams.Count);
    Assert.Equal(1, h.Streams.Count(s => !s.Disposed));
    Assert.Equal(serial, h.Streams.Single(s => !s.Disposed).TargetNodeSerial);
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
  }

  // --- T4: idempotence under a forced overlap -------------------------------------------------------

  [Fact]
  public async Task ConcurrentAppearAndMonitorProbe_StartExactlyOneStream()
  {
    var h = new Harness();
    h.BindInitial(58968);
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    // Hold the first re-bind INSIDE Start() — i.e. inside _captureDeviceLock — until the second
    // contender (the pipeline monitor's probe) has been issued. Counted, not timed.
    var inStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    h.StartGate = () => { inStart.TrySetResult(); return release.Task; };
    h.Service.NodeLookupOverride = (_, _) =>
      Task.FromResult<(string?, int, int)>((NodeName, 81, 59112));

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await inStart.Task.WaitAsync(TimeSpan.FromSeconds(10));

    var probe = h.Service.TryRebindParkedFromProbeAsync(CancellationToken.None);
    release.SetResult();
    var probeStarted = await probe.WaitAsync(TimeSpan.FromSeconds(10));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.False(probeStarted, "the second contender must find the capture already bound");
    Assert.Equal(2, h.StartedStreams); // the initial bind + exactly one re-bind
    Assert.Equal(1, h.Streams.Count(s => !s.Disposed));
  }

  [Fact]
  public async Task MonitorProbe_AloneRebindsAParkedCapture()
  {
    // The backstop for a missed NodeAppeared (or a WrongPeerBound park, where no event will come).
    var h = new Harness();
    h.BindInitial(58968);
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();
    h.Service.NodeLookupOverride = (_, _) =>
      Task.FromResult<(string?, int, int)>((NodeName, 81, 59112));

    Assert.True(await h.Service.TryRebindParkedFromProbeAsync(CancellationToken.None));
    Assert.Equal(59112u, h.Streams[^1].TargetNodeSerial);
  }

  [Fact]
  public async Task MonitorProbe_NodeStillAbsent_StaysParked()
  {
    var h = new Harness();
    h.BindInitial();
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();
    h.Service.NodeLookupOverride = (_, _) => Task.FromResult<(string?, int, int)>((null, 0, 0));

    Assert.False(await h.Service.TryRebindParkedFromProbeAsync(CancellationToken.None));
    Assert.Equal(BluetoothPipelineStatus.WaitingForCaptureNode, h.Service.PipelineStatus);
  }

  // --- T5: intentional stop wins ---------------------------------------------------------------------

  [Fact]
  public async Task NodeAppeared_AfterIntentionalStop_DoesNothing()
  {
    var h = new Harness();
    h.BindInitial();
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    h.Service.StopAudioCapture(); // the user switched sources while the phone was paused

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Single(h.Streams);
    Assert.Equal(BluetoothPipelineStatus.Degraded, h.Service.PipelineStatus);
  }

  [Fact]
  public async Task NodeAppeared_WhenTheParkedGeneratorWasDisposed_DoesNothing()
  {
    var h = new Harness();
    h.BindInitial();
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();

    h.Generator.Dispose(); // BluetoothAudioSource removed it from the mixer

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.Single(h.Streams);
  }

  [Fact]
  public async Task RebindThatFailsToStart_StaysParked()
  {
    var h = new Harness();
    h.BindInitial();
    h.Service.OnRegistryNodeDisappeared(null, Harness.Node(id: 76, serial: 58968));
    await h.Service.WhenRegistryWorkIdleAsync();
    h.StartGate = () => throw new InvalidOperationException("pw_stream_connect failed: -2");

    h.Service.OnRegistryNodeAppeared(null, Harness.Node(id: 81, serial: 59112));
    await h.Service.WhenRegistryWorkIdleAsync();

    Assert.True(h.Streams[^1].Disposed);
    Assert.Equal(BluetoothPipelineStatus.WaitingForCaptureNode, h.Service.PipelineStatus);
  }
}
