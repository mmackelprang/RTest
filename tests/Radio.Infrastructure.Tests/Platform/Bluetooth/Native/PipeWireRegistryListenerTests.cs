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
