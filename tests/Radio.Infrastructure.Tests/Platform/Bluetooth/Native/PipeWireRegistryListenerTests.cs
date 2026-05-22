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
}
