using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Infrastructure.Platform.Bluetooth.Native;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Native;

/// <summary>
/// Lightweight, native-free assertions about
/// <see cref="PipeWireRegistryListener"/>'s lifecycle.
///
/// The listener's primary path (P/Invoke into libpipewire + libpw_helper) is
/// covered by Plan E Task 7's 60-cycle pair/unpair harness on the live host.
/// These local tests lock in two properties that
/// <c>LinuxBluetoothService.EnsureRescanLoopRunning</c> relies on:
///
/// 1. A freshly-constructed listener reports <c>IsHealthy = false</c> until
///    <c>Start()</c> succeeds — so the Plan B fallback scrape can take over
///    when the listener has never come up (e.g., missing <c>libpw_helper.so</c>
///    symbol on a partially-deployed host).
/// 2. <c>Dispose()</c> is safe on an un-started instance and idempotent
///    across repeated calls — required because the LinuxBluetoothService
///    constructor doesn't yet hold a DBus connection, so we can't try-start
///    the listener until <c>StartAsync</c> succeeds.
/// </summary>
public class PipeWireRegistryListenerTests
{
  [Fact]
  public void NewListener_IsNotHealthy_UntilStartSucceeds()
  {
    using var listener = new PipeWireRegistryListener(NullLogger.Instance);

    // Pre-Start: must report unhealthy so the Plan B fallback scrape runs.
    Assert.False(listener.IsHealthy);
  }

  [Fact]
  public void Dispose_BeforeStart_DoesNotThrow()
  {
    var listener = new PipeWireRegistryListener(NullLogger.Instance);
    listener.Dispose();

    // Still reports unhealthy after disposal.
    Assert.False(listener.IsHealthy);
  }

  [Fact]
  public void Dispose_Idempotent()
  {
    var listener = new PipeWireRegistryListener(NullLogger.Instance);
    listener.Dispose();
    listener.Dispose();

    Assert.False(listener.IsHealthy);
  }

  [Fact]
  public void Start_AfterDispose_Throws()
  {
    var listener = new PipeWireRegistryListener(NullLogger.Instance);
    listener.Dispose();

    Assert.Throws<ObjectDisposedException>(() => listener.Start());
  }

  // --- AUD-10 T2: the serial published is object.serial, never the registry id ---------------
  //
  // The id/serial pairs are the ones measured on the appliance (docs/queue/AUD-10.md 2026-09-25):
  // id=76 / serial=58968 and id=71 / serial=58921. The native OnGlobal reads node.name and
  // object.serial from the spa_dict and hands both to ClassifyNodeGlobal; what OnGlobal does with
  // the result (log a Warning and skip the raise on BtCaptureNodeWithoutSerial) is two lines that
  // need libpipewire to run and are NOT covered here.

  [Theory]
  [InlineData(76u, "58968")]
  [InlineData(71u, "58921")]
  public void ClassifyNodeGlobal_MeasuredIdAndSerial_CarriesTheSerialNotTheId(uint id, string serial)
  {
    var kind = PipeWireRegistryListener.ClassifyNodeGlobal(
      id, "bluez_input.B0_D5_FB_D2_0D_68.2", serial, out var args);

    Assert.Equal(BtNodeGlobalKind.BtCaptureNode, kind);
    Assert.NotNull(args);
    Assert.Equal(uint.Parse(serial), args!.ObjectSerial);
    Assert.Equal(id, args.Id);
    Assert.NotEqual(args.Id, args.ObjectSerial);
    Assert.Equal("B0:D5:FB:D2:0D:68", args.DeviceAddress);
    Assert.Equal("bluez_input.B0_D5_FB_D2_0D_68.2", args.NodeName);
  }

  [Theory]
  [InlineData(null)]       // property absent
  [InlineData("")]
  [InlineData("0")]        // not a valid serial
  [InlineData("-1")]
  [InlineData("abc")]
  [InlineData(" 58968")]   // not silently trimmed into a number
  public void ClassifyNodeGlobal_MissingOrUnusableSerial_IsReportedAndNeverFallsBackToTheId(string? serial)
  {
    var kind = PipeWireRegistryListener.ClassifyNodeGlobal(
      76u, "bluez_input.B0_D5_FB_D2_0D_68.2", serial, out var args);

    Assert.Equal(BtNodeGlobalKind.BtCaptureNodeWithoutSerial, kind);
    // Args still come back (so the node's REMOVAL can be reported), but with serial 0 — and the
    // id is not smuggled in as the serial.
    Assert.NotNull(args);
    Assert.Equal(0u, args!.ObjectSerial);
    Assert.Equal(76u, args.Id);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("alsa_input.pci-0000_00_1f.3.analog-stereo")]
  [InlineData("radio-bt-stream")]
  public void ClassifyNodeGlobal_NotABtCaptureNode_IsIgnored(string? nodeName)
  {
    var kind = PipeWireRegistryListener.ClassifyNodeGlobal(76u, nodeName, "58968", out var args);

    Assert.Equal(BtNodeGlobalKind.NotBtCaptureNode, kind);
    Assert.Null(args);
  }

  /// <summary>
  /// Regression guard: <c>pw_core_get_registry</c> is declared <c>static inline</c>
  /// in <c>pipewire/core.h</c> (it expands the <c>spa_interface_call_res</c>
  /// vtable-dispatch macro), so no real symbol of that name exists in
  /// <c>libpipewire-0.3.so</c>. Binding the DllImport against <c>pipewire-0.3</c>
  /// throws <see cref="EntryPointNotFoundException"/> at first call and forces
  /// the registry listener back onto the periodic <c>pw-cli</c> scrape fallback.
  /// The fix routes the call through <c>libpw_helper</c>'s
  /// <c>pw_helper_core_get_registry</c> wrapper. This test locks that wiring in
  /// so an unwitting "cleanup" doesn't move the binding back to PipeWireLib.
  /// </summary>
  [Fact]
  public void PwCoreGetRegistry_IsBoundToHelperLibrary_NotPipeWireLib()
  {
    var method = typeof(PipeWireNative).GetMethod(
      "pw_core_get_registry",
      BindingFlags.Public | BindingFlags.Static)!;
    Assert.NotNull(method);

    var dllImport = method.GetCustomAttribute<DllImportAttribute>()!;
    Assert.NotNull(dllImport);

    // Must be bound through the helper shared library (which exports a real
    // pw_helper_core_get_registry symbol that wraps the static-inline call).
    Assert.Equal("pw_helper", dllImport.Value);
    Assert.Equal("pw_helper_core_get_registry", dllImport.EntryPoint);
  }
}
