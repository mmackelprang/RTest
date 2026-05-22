#if !WINDOWS_TARGET
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static Radio.Infrastructure.Platform.Bluetooth.Native.PipeWireNative;

namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// Event args for <see cref="PipeWireRegistryListener.NodeAppeared"/> /
/// <see cref="PipeWireRegistryListener.NodeDisappeared"/>.
/// </summary>
internal sealed class BtNodeRegistryEventArgs : EventArgs
{
  /// <summary>PipeWire registry global id (also used as object.serial for streams).</summary>
  public required uint Id { get; init; }

  /// <summary>Colon-separated upper-case BT address (e.g., "AA:BB:CC:DD:EE:FF").</summary>
  public required string DeviceAddress { get; init; }
}

/// <summary>
/// Subscribes to the PipeWire registry for global add/remove events,
/// filters BT A2DP source nodes (<c>bluez_input.&lt;MAC&gt;.a2dp-source</c>)
/// via <see cref="PipeWireRegistryFilter"/>, and forwards them as managed
/// events. Replaces the <c>pw-cli list-objects</c> text-scrape used by
/// Plan B's periodic re-scan loop with an event-driven path.
///
/// Runs its own <c>pw_thread_loop</c> separate from the capture stream's
/// loop (in <see cref="PipeWireNativeStream"/>) so the registry subscription
/// survives capture stream restarts. <c>pw_init</c> is guarded against
/// double-init via <see cref="EnsurePwInit"/>.
///
/// Lifecycle: construct → <see cref="Start"/> → consume events → <see cref="Dispose"/>.
/// If <see cref="Start"/> fails to bring up the chain, <see cref="IsHealthy"/>
/// remains <c>false</c> and the caller (LinuxBluetoothService) falls back
/// to the periodic <c>pw-cli</c> scrape loop from Plan B.
/// </summary>
internal sealed class PipeWireRegistryListener : IDisposable
{
  // Guard pw_init() against double-init when PipeWireNativeStream has already
  // initialised. Mirrors the pattern in PipeWireNativeStream.EnsurePwInit.
  private static bool s_pwInitialized;
  private static readonly object s_initLock = new();

  private readonly ILogger _logger;

  private IntPtr _threadLoop;
  private IntPtr _context;
  private IntPtr _core;
  private IntPtr _registry;
  private IntPtr _hook;         // pinned spa_hook buffer
  private GCHandle _eventsHandle;
  private GCHandle _selfHandle;
  private PwRegistryEvents _events;

  // Pinned delegate references to prevent GC collection during native callbacks.
  // Must outlive the registry proxy (PipeWire dereferences the function pointers
  // through the PwRegistryEvents struct on every global / global_remove).
  private readonly PwRegistryGlobalDelegate _globalDelegate;
  private readonly PwRegistryGlobalRemoveDelegate _globalRemoveDelegate;

  private bool _disposed;

  // Map of registry-global-id → BT address. Populated by global(); consulted
  // by global_remove() to surface the address with the disappearance event.
  // ConcurrentDictionary because callbacks fire on the pw_thread_loop while
  // managed code may be reading on other threads (defensive — current
  // consumers only touch via the event handlers).
  private readonly ConcurrentDictionary<uint, string> _idToAddress = new();

  /// <summary>True after <see cref="Start"/> brings up context/core/registry
  /// successfully and pw_proxy_add_listener has been invoked. False if
  /// the listener has not been started, failed to start, or has been
  /// disposed. Consumers gate the fallback periodic scrape on this.
  /// </summary>
  public bool IsHealthy { get; private set; }

  /// <summary>Raised when a BT A2DP source node appears in the registry.
  /// Fires on the pw_thread_loop thread; consumers should marshal heavy
  /// work onto the thread pool if needed.</summary>
  public event EventHandler<BtNodeRegistryEventArgs>? NodeAppeared;

  /// <summary>Raised when a previously-known BT capture node is removed.</summary>
  public event EventHandler<BtNodeRegistryEventArgs>? NodeDisappeared;

  public PipeWireRegistryListener(ILogger logger)
  {
    _logger = logger;
    // Capture delegate instances so the GC keeps them alive for the lifetime
    // of this listener. Marshal.GetFunctionPointerForDelegate only keeps the
    // function pointer valid as long as the delegate object itself is reachable.
    _globalDelegate = OnGlobal;
    _globalRemoveDelegate = OnGlobalRemove;
  }

  private static void EnsurePwInit()
  {
    lock (s_initLock)
    {
      if (!s_pwInitialized)
      {
        pw_init(IntPtr.Zero, IntPtr.Zero);
        s_pwInitialized = true;
      }
    }
  }

  /// <summary>
  /// Brings up the registry subscription. Idempotent — calling on a healthy
  /// listener is a no-op. On failure, logs a warning, leaves
  /// <see cref="IsHealthy"/> false, and cleans up any partial state so the
  /// caller can rely on the fallback scrape path.
  /// </summary>
  public void Start()
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(nameof(PipeWireRegistryListener));
    }
    if (IsHealthy)
    {
      return;
    }

    EnsurePwInit();

    try
    {
      _threadLoop = pw_thread_loop_new("radio-bt-registry", IntPtr.Zero);
      if (_threadLoop == IntPtr.Zero)
      {
        throw new InvalidOperationException("pw_thread_loop_new failed");
      }

      var loop = pw_thread_loop_get_loop(_threadLoop);

      _context = pw_context_new(loop, IntPtr.Zero, UIntPtr.Zero);
      if (_context == IntPtr.Zero)
      {
        throw new InvalidOperationException("pw_context_new failed");
      }

      _core = pw_context_connect(_context, IntPtr.Zero, UIntPtr.Zero);
      if (_core == IntPtr.Zero)
      {
        throw new InvalidOperationException("pw_context_connect failed");
      }

      _registry = pw_core_get_registry(_core, PW_VERSION_REGISTRY, UIntPtr.Zero);
      if (_registry == IntPtr.Zero)
      {
        throw new InvalidOperationException("pw_core_get_registry failed");
      }

      // Keep a GCHandle to this so the native callback can find us.
      _selfHandle = GCHandle.Alloc(this);

      // Build the events struct — must remain at a stable pinned address
      // because PipeWire keeps a pointer to it for the lifetime of the listener.
      _events = new PwRegistryEvents
      {
        Version = PW_VERSION_REGISTRY_EVENTS,
        Global = Marshal.GetFunctionPointerForDelegate(_globalDelegate),
        GlobalRemove = Marshal.GetFunctionPointerForDelegate(_globalRemoveDelegate),
      };
      _eventsHandle = GCHandle.Alloc(_events, GCHandleType.Pinned);

      _hook = Marshal.AllocHGlobal(SpaHookSize);
      // Zero the hook buffer — PipeWire initialises the list-link pointers
      // through pw_proxy_add_listener but a clean zero start is paranoia-safe.
      for (var i = 0; i < SpaHookSize; i++)
      {
        Marshal.WriteByte(_hook, i, 0);
      }

      pw_proxy_add_listener(
        _registry,
        _hook,
        _eventsHandle.AddrOfPinnedObject(),
        GCHandle.ToIntPtr(_selfHandle));

      var startResult = pw_thread_loop_start(_threadLoop);
      if (startResult < 0)
      {
        throw new InvalidOperationException($"pw_thread_loop_start failed: {startResult}");
      }

      IsHealthy = true;
      _logger.LogInformation("PipeWireRegistryListener started");
    }
    catch (DllNotFoundException ex)
    {
      _logger.LogWarning(
        ex,
        "PipeWireRegistryListener: native library missing (libpipewire-0.3 or libpw_helper); "
        + "falling back to periodic pw-cli scrape");
      IsHealthy = false;
      Cleanup();
    }
    catch (EntryPointNotFoundException ex)
    {
      _logger.LogWarning(
        ex,
        "PipeWireRegistryListener: required native symbol missing "
        + "(rebuild libpw_helper.so?); falling back to periodic pw-cli scrape");
      IsHealthy = false;
      Cleanup();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "PipeWireRegistryListener failed to start; falling back to periodic pw-cli scrape");
      IsHealthy = false;
      Cleanup();
    }
  }

  /// <summary>
  /// Fires on the pw_thread_loop for every global added to the registry.
  /// We filter for BT A2DP source nodes and surface them as
  /// <see cref="NodeAppeared"/>. All other globals (devices, ports, links,
  /// non-BT nodes) are ignored.
  /// </summary>
  private static void OnGlobal(
    IntPtr userData, uint id, uint permissions,
    string type, uint version, IntPtr props)
  {
    if (userData == IntPtr.Zero)
    {
      return;
    }

    PipeWireRegistryListener? self;
    try
    {
      self = GCHandle.FromIntPtr(userData).Target as PipeWireRegistryListener;
    }
    catch
    {
      return;
    }
    if (self == null)
    {
      return;
    }

    try
    {
      // We only care about Node objects — devices, ports, links, factories etc. skip.
      if (type != "PipeWire:Interface:Node")
      {
        return;
      }

      // Read node.name from the spa_dict via the helper.
      var nameOrNull = ReadSpaDictKey(props, "node.name");
      if (nameOrNull == null)
      {
        return;
      }

      if (!PipeWireRegistryFilter.TryExtractBtCaptureAddress(nameOrNull, out var address))
      {
        return;
      }

      self._idToAddress[id] = address;
      self._logger.LogInformation(
        "PW registry: BT node appeared id={Id} address={Address}",
        id, address);

      self.NodeAppeared?.Invoke(self, new BtNodeRegistryEventArgs
      {
        Id = id,
        DeviceAddress = address,
      });
    }
    catch (Exception ex)
    {
      // Must not throw on the PipeWire thread loop.
      self._logger.LogDebug(ex, "PipeWireRegistryListener.OnGlobal: handler threw");
    }
  }

  /// <summary>
  /// Fires on the pw_thread_loop for every global removed from the registry.
  /// We only forward removals for ids we previously identified as BT nodes;
  /// the rest are silently ignored.
  /// </summary>
  private static void OnGlobalRemove(IntPtr userData, uint id)
  {
    if (userData == IntPtr.Zero)
    {
      return;
    }

    PipeWireRegistryListener? self;
    try
    {
      self = GCHandle.FromIntPtr(userData).Target as PipeWireRegistryListener;
    }
    catch
    {
      return;
    }
    if (self == null)
    {
      return;
    }

    try
    {
      if (!self._idToAddress.TryRemove(id, out var address))
      {
        return;
      }

      self._logger.LogInformation(
        "PW registry: BT node disappeared id={Id} address={Address}",
        id, address);

      self.NodeDisappeared?.Invoke(self, new BtNodeRegistryEventArgs
      {
        Id = id,
        DeviceAddress = address,
      });
    }
    catch (Exception ex)
    {
      self._logger.LogDebug(ex, "PipeWireRegistryListener.OnGlobalRemove: handler threw");
    }
  }

  private static string? ReadSpaDictKey(IntPtr dictPtr, string key)
  {
    if (dictPtr == IntPtr.Zero)
    {
      return null;
    }
    var resultPtr = pw_helper_spa_dict_lookup(dictPtr, key);
    return resultPtr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(resultPtr);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    Cleanup();
  }

  private void Cleanup()
  {
    // Tear down the PipeWire chain under the thread loop lock so we don't
    // race with a callback in flight. Order: registry → core → context →
    // stop/destroy the loop → free the events/hook/self handles last.
    if (_threadLoop != IntPtr.Zero)
    {
      try
      {
        pw_thread_loop_lock(_threadLoop);
        try
        {
          if (_registry != IntPtr.Zero)
          {
            pw_proxy_destroy(_registry);
            _registry = IntPtr.Zero;
          }
          if (_core != IntPtr.Zero)
          {
            pw_core_disconnect(_core);
            _core = IntPtr.Zero;
          }
          if (_context != IntPtr.Zero)
          {
            pw_context_destroy(_context);
            _context = IntPtr.Zero;
          }
        }
        finally
        {
          pw_thread_loop_unlock(_threadLoop);
        }

        pw_thread_loop_stop(_threadLoop);
        pw_thread_loop_destroy(_threadLoop);
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "PipeWireRegistryListener cleanup partially failed");
      }
      _threadLoop = IntPtr.Zero;
    }

    if (_hook != IntPtr.Zero)
    {
      Marshal.FreeHGlobal(_hook);
      _hook = IntPtr.Zero;
    }
    if (_eventsHandle.IsAllocated)
    {
      _eventsHandle.Free();
    }
    if (_selfHandle.IsAllocated)
    {
      _selfHandle.Free();
    }
    _idToAddress.Clear();
    IsHealthy = false;
  }
}
#endif
