using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Platform.Bluetooth;
using Radio.Infrastructure.Platform.Bluetooth.Native;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Tmds.DBus;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// AUD-30 + AUD-31: BlueZ device objects under an adapter other than ours are ignored.
/// </summary>
/// <remarks>
/// <para>
/// The box has two adapters and <c>hci1</c> belongs to RotaryPhone. On 2026-09-25 RotaryPhone
/// refusing the owner's Pixel on <c>hci1</c> was followed, 318 and 347 ms later, by our
/// <c>Bluetooth device disconnected … reason="Unknown"</c> and a torn-down A2DP stream on <c>hci0</c>
/// (docs/queue/AUD-30.md, "CONFIRMED"). The cause: device enumeration, InterfacesAdded and
/// InterfacesRemoved accepted a <c>Device1</c> under any adapter.
/// </para>
/// <para>
/// These drive the REAL <see cref="LinuxBluetoothService"/> handlers — the methods the Tmds.DBus
/// signal subscriptions invoke — with the same capture-stream seam <c>CaptureNodeFollowTests</c> uses.
/// Nothing here waits on a clock (CLAUDE.md § Test Timing): every handler under test runs
/// synchronously on the calling thread.
/// </para>
/// <para>
/// ⚠ What these cannot show: that BlueZ emits these signals for hci1 objects in the order and shape
/// modelled here. That was measured on the box, and is re-checked at the cabinet (AUD-30 §
/// Verification). Also NOT covered, because each sits behind a live D-Bus call: the gate inside
/// <c>WatchDevicePropertiesAsync</c> (it returns before the gate when there is no connection), the
/// pre-existing-connection check and the media-object scan in <c>StartAsync</c>/<c>CheckForMediaPlayersAsync</c>,
/// and adapter re-selection on a stop/start. Those gates are defence in depth behind the ones tested
/// here — a foreign path never reaches the cache, and a foreign Connected=false is refused by the
/// handler itself — but deleting any one of them alone would fail no test.
/// </para>
/// </remarks>
public class AdapterScopingTests
{
  private const string Mac = "B0:D5:FB:D2:0D:68";
  private const string Hci0 = "/org/bluez/hci0";
  private const string OurPath = "/org/bluez/hci0/dev_B0_D5_FB_D2_0D_68";
  private const string ForeignPath = "/org/bluez/hci1/dev_B0_D5_FB_D2_0D_68";
  private const string NodeName = "bluez_input.B0_D5_FB_D2_0D_68.2";

  private static readonly AudioFormat Format =
    new() { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };

  // --- The pure predicates ----------------------------------------------------------------------

  [Theory]
  [InlineData(OurPath, Hci0, true)]
  [InlineData(OurPath + "/player0", Hci0, true)]                              // device child (MediaPlayer1)
  [InlineData(OurPath + "/sep1/fd0", Hci0, true)]                             // device child (MediaTransport1)
  [InlineData(ForeignPath, Hci0, false)]                                      // RotaryPhone's adapter
  [InlineData("/org/bluez/hci10/dev_B0_D5_FB_D2_0D_68", "/org/bluez/hci1", false)] // prefix, not a segment
  [InlineData("/org/bluez/hci1/dev_B0_D5_FB_D2_0D_68", "/org/bluez/hci1", true)]
  [InlineData("/org/bluez/hci1/dev_B0_D5_FB_D2_0D_68", "/org/bluez/hci10", false)]
  [InlineData(Hci0, Hci0, false)]                                             // the adapter itself
  [InlineData(Hci0 + "/", Hci0, false)]                                       // nothing below the slash
  [InlineData(OurPath, Hci0 + "/", true)]                                     // trailing slash on the adapter
  [InlineData("/org/bluez/HCI0/dev_B0_D5_FB_D2_0D_68", Hci0, false)]          // D-Bus paths are case-sensitive
  [InlineData(OurPath, null, false)]                                          // no adapter selected: nothing is ours
  [InlineData(OurPath, "", false)]
  [InlineData(null, Hci0, false)]
  public void IsObjectUnderAdapter_Table(string? objectPath, string? adapterPath, bool expected)
  {
    Assert.Equal(expected, LinuxBluetoothService.IsObjectUnderAdapter(objectPath, adapterPath));
  }

  [Theory]
  [InlineData("/org/bluez/hci1", "hci1", true)]
  [InlineData("/org/bluez/hci10", "hci1", false)]   // the StartsWith this replaced said true
  [InlineData("/org/bluez/hci0", "HCI0", true)]     // configured name is hand-typed
  [InlineData("/org/bluez/hci1", "hci0", false)]
  public void IsConfiguredAdapterPath_Table(string adapterObjectPath, string adapterName, bool expected)
  {
    Assert.Equal(expected, LinuxBluetoothService.IsConfiguredAdapterPath(adapterObjectPath, adapterName));
  }

  // --- Handler level: RotaryPhone's hci1 object for OUR phone ----------------------------------------

  [Fact]
  public void ForeignDeviceAdded_IsNotCached_AndRaisesNothing()
  {
    var h = new Harness();

    h.Service.OnInterfaceAdded(DeviceAdded(ForeignPath, connected: false));

    Assert.Equal(new[] { OurPath }, h.Service.CachedDevicePathsForTests());
    Assert.Empty(h.Discovered);
    Assert.Empty(h.Connected);
  }

  [Fact]
  public void StartupEnumeration_SkipsForeignDevices_AndCachesOurs()
  {
    // radio-api starting while RotaryPhone already holds an hci1 object for our phone.
    var h = new Harness(primeConnected: false);
    const string other = "/org/bluez/hci0/dev_D4_3A_2C_64_87_9E";
    var snapshot = new Dictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>();
    foreach (var (path, ifaces) in new[]
    {
      DeviceAdded(ForeignPath, connected: true),
      DeviceAdded(other, connected: false, address: "D4:3A:2C:64:87:9E"),
    })
    {
      snapshot[path] = ifaces;
    }

    h.Service.IngestExistingDevices(snapshot);

    Assert.Equal(new[] { other }, h.Service.CachedDevicePathsForTests());
    Assert.Empty(h.Connected);
    Assert.Null(h.Service.ConnectedDevice);
    Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("already connected", StringComparison.Ordinal));
  }

  [Fact]
  public void ForeignConnectedFalse_DoesNotRaiseDisconnected_OrStopCapture()
  {
    // The measured sequence: the hci1 object exists, then RotaryPhone refuses it and BlueZ flips
    // that object's Connected to false — while our hci0 link keeps streaming.
    var h = new Harness();
    h.BindCapture();

    h.Service.OnInterfaceAdded(DeviceAdded(ForeignPath, connected: false));
    h.Service.OnDeviceConnectedChanged(new ObjectPath(ForeignPath), connected: false);

    Assert.Empty(h.Disconnected);
    var stream = Assert.Single(h.Streams);
    Assert.False(stream.Disposed, "a foreign-adapter disconnect must not stop our capture");
    Assert.Equal(BluetoothPipelineStatus.Healthy, h.Service.PipelineStatus);
    Assert.Equal(Mac, h.Service.ConnectedDevice?.Address);
    Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("disconnected", StringComparison.Ordinal));
  }

  [Fact]
  public void ForeignConnectedFalse_IsIgnoredByTheHandlerItself_EvenIfCached()
  {
    // The handler's own gate, independent of the gate in front of the cache: a foreign entry is put
    // where the pre-AUD-30 code would have put it, and its Connected=false must still not reach the
    // teardown.
    var h = new Harness();
    h.BindCapture();
    h.Service.PrimeCachedDeviceForTests(ForeignPath, ForeignDevice(connected: true));

    h.Service.OnDeviceConnectedChanged(new ObjectPath(ForeignPath), connected: false);

    Assert.Empty(h.Disconnected);
    Assert.False(Assert.Single(h.Streams).Disposed);
  }

  [Fact]
  public void ForeignDeviceAddedThenRemoved_LeavesNoTrace()
  {
    // The signal sequence end to end. Guarded by the InterfacesAdded gate (the foreign object never
    // reaches the cache, so there is nothing to evict); the Removed gate on its own is pinned by
    // ForeignDeviceRemoved_IsIgnoredByTheHandlerItself_EvenIfCached below.
    var h = new Harness();

    h.Service.OnInterfaceAdded(DeviceAdded(ForeignPath, connected: false));
    h.Service.OnInterfaceRemoved((new ObjectPath(ForeignPath), new[] { "org.bluez.Device1" }));

    Assert.Equal(new[] { OurPath }, h.Service.CachedDevicePathsForTests());
    Assert.Equal(Mac, h.Service.ConnectedDevice?.Address);
    Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("removed from BlueZ", StringComparison.Ordinal));
  }

  [Fact]
  public void ForeignDeviceRemoved_IsIgnoredByTheHandlerItself_EvenIfCached()
  {
    // AUD-31's observed line: a cached hci1 entry for our phone removed -> "removed from BlueZ:
    // Pixel … evicted from cache" while hci0 still had it paired and connected.
    var h = new Harness();
    h.Service.PrimeCachedDeviceForTests(ForeignPath, ForeignDevice(connected: false));

    h.Service.OnInterfaceRemoved((new ObjectPath(ForeignPath), new[] { "org.bluez.Device1" }));

    Assert.Contains(OurPath, h.Service.CachedDevicePathsForTests());
    Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("removed from BlueZ", StringComparison.Ordinal));
  }

  [Fact]
  public void ForeignObjects_AreLoggedOnce_AtDebug_AndNeverHigher()
  {
    var h = new Harness();

    for (var i = 0; i < 3; i++)
    {
      h.Service.OnInterfaceAdded(DeviceAdded(ForeignPath, connected: false));
      h.Service.OnDeviceConnectedChanged(new ObjectPath(ForeignPath), connected: false);
      h.Service.OnInterfaceRemoved((new ObjectPath(ForeignPath), new[] { "org.bluez.Device1" }));
    }

    var mentions = h.Log.Entries.Where(e => e.Message.Contains(ForeignPath, StringComparison.Ordinal)).ToList();
    var only = Assert.Single(mentions);
    Assert.Equal(LogLevel.Debug, only.Level);
  }

  [Fact]
  public void ForeignMediaTransportAndPlayer_AreIgnored()
  {
    // RotaryPhone's HFP transport / an AVRCP player under hci1 must not be attached. Attaching needs
    // D-Bus, so the observable here is the one-shot ignore line naming the path.
    var h = new Harness();
    var transport = ForeignPath + "/sep1/fd0";

    h.Service.OnInterfaceAdded((new ObjectPath(transport), new Dictionary<string, IDictionary<string, object>>
    {
      ["org.bluez.MediaTransport1"] = new Dictionary<string, object>(),
    }));

    Assert.Contains(h.Log.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains(transport, StringComparison.Ordinal));
  }

  // --- Handler level: our own adapter is unchanged --------------------------------------------------

  [Fact]
  public void OurDeviceConnectedFalse_StillTearsDown_AndLogsItsPath()
  {
    var h = new Harness();
    h.BindCapture();

    h.Service.OnDeviceConnectedChanged(new ObjectPath(OurPath), connected: false);

    var evt = Assert.Single(h.Disconnected);
    Assert.Equal(Mac, evt.Device.Address);
    Assert.Equal(BluetoothDisconnectReason.Unknown, evt.Reason);
    Assert.True(Assert.Single(h.Streams).Disposed);
    Assert.Contains(h.Log.Entries, e =>
      e.Level == LogLevel.Information &&
      e.Message.Contains("Bluetooth device disconnected", StringComparison.Ordinal) &&
      e.Message.Contains(OurPath, StringComparison.Ordinal));
  }

  [Fact]
  public void OurDeviceRemoved_StillEvicts_AndLogsItsPath()
  {
    var h = new Harness();

    h.Service.OnInterfaceRemoved((new ObjectPath(OurPath), new[] { "org.bluez.Device1" }));

    Assert.Empty(h.Service.CachedDevicePathsForTests());
    Assert.Contains(h.Log.Entries, e =>
      e.Level == LogLevel.Information &&
      e.Message.Contains("removed from BlueZ", StringComparison.Ordinal) &&
      e.Message.Contains(OurPath, StringComparison.Ordinal));
  }

  [Fact]
  public void OurAdapterDeviceAdded_IsCached_AndDiscovered()
  {
    var h = new Harness();
    const string other = "/org/bluez/hci0/dev_D4_3A_2C_64_87_9E";

    h.Service.OnInterfaceAdded(DeviceAdded(other, connected: false, address: "D4:3A:2C:64:87:9E"));

    Assert.Contains(other, h.Service.CachedDevicePathsForTests());
    Assert.Single(h.Discovered);
  }

  // --- Edge cases: segment boundary, and no adapter at all ------------------------------------------

  [Fact]
  public void SelectedHci1_AcceptsHci1_ButNotHci10()
  {
    var h = new Harness(adapterPath: "/org/bluez/hci1", primeConnected: false);
    const string hci10 = "/org/bluez/hci10/dev_B0_D5_FB_D2_0D_68";

    h.Service.OnInterfaceAdded(DeviceAdded(hci10, connected: false));
    h.Service.OnInterfaceAdded(DeviceAdded(ForeignPath, connected: false));

    Assert.Equal(new[] { ForeignPath }, h.Service.CachedDevicePathsForTests());
  }

  [Fact]
  public void NoAdapterSelected_AcceptsNoDeviceObject()
  {
    var h = new Harness(adapterPath: null, primeConnected: false);

    h.Service.OnInterfaceAdded(DeviceAdded(OurPath, connected: false));

    Assert.Empty(h.Service.CachedDevicePathsForTests());
    Assert.Empty(h.Discovered);
  }

  // --- Plumbing -------------------------------------------------------------------------------------

  private static BluetoothDeviceInfo ForeignDevice(bool connected) => new()
  {
    Address = Mac, Name = "Pixel 10 Pro XL", IsPaired = true, IsConnected = connected,
  };

  private static (ObjectPath, IDictionary<string, IDictionary<string, object>>) DeviceAdded(
    string path, bool connected, string address = Mac) =>
    (new ObjectPath(path), new Dictionary<string, IDictionary<string, object>>
    {
      ["org.bluez.Device1"] = new Dictionary<string, object>
      {
        ["Address"] = address,
        ["Name"] = "Pixel 10 Pro XL",
        ["Paired"] = true,
        ["Connected"] = connected,
      },
    });

  private sealed class FakeStream : IBtCaptureStream
  {
    public FakeStream(uint serial) { TargetNodeSerial = serial; }
    public uint TargetNodeSerial { get; }
    public bool Disposed;
    public void Start() { }
    public long MillisecondsSinceLastOnProcess() => 0;
    public void Dispose() => Disposed = true;
  }

  private sealed class Harness
  {
    public LinuxBluetoothService Service { get; }
    public ListLogger Log { get; } = new();
    public List<FakeStream> Streams { get; } = new();
    public List<BluetoothDeviceDiscoveredEventArgs> Discovered { get; } = new();
    public List<BluetoothDeviceConnectedEventArgs> Connected { get; } = new();
    public List<BluetoothDeviceDisconnectedEventArgs> Disconnected { get; } = new();

    public Harness(string? adapterPath = Hci0, bool primeConnected = true)
    {
      // AutoReconnect off: the hci0 disconnect test would otherwise start a real reconnection loop.
      Service = new LinuxBluetoothService(Log, Options.Create(new BluetoothOptions { AutoReconnect = false }));
      Service.CaptureStreamFactory = (serial, _, _) =>
      {
        var s = new FakeStream(serial);
        Streams.Add(s);
        return s;
      };
      Service.PostBindMaintenanceOverride = (_, _, _) => { };
      Service.DeviceDiscovered += (_, e) => Discovered.Add(e);
      Service.DeviceConnected += (_, e) => Connected.Add(e);
      Service.DeviceDisconnected += (_, e) => Disconnected.Add(e);

      Service.SetSelectedAdapterPathForTests(adapterPath);
      if (primeConnected)
      {
        Service.PrimeConnectedDeviceForTests(new BluetoothDeviceInfo
        {
          Address = Mac, Name = "Pixel 10 Pro XL", IsPaired = true, IsConnected = true,
        });
      }
    }

    public void BindCapture() =>
      Assert.True(Service.BindNativeCaptureForTests(
        new BufferedSoundGenerator<float>(null!, Format, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance),
        Format, NodeName, 58968));
  }

  internal sealed class ListLogger : ILogger
  {
    private readonly List<(LogLevel Level, string Message)> _entries = new();

    // A snapshot: fire-and-forget work inside the service may still be logging while a test reads.
    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
      get { lock (_entries) { return _entries.ToList(); } }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      lock (_entries)
      {
        _entries.Add((logLevel, formatter(state, exception)));
      }
    }
  }
}
