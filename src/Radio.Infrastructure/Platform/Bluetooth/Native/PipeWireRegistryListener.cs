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
  /// <summary>
  /// PipeWire registry global id.
  /// </summary>
  /// <remarks>
  /// ⛔ This is NOT an object.serial and must NOT be used as target.object. An earlier version of this
  /// comment said "also used as object.serial for streams", which is false: pipewire-props(7) gives
  /// target.object as &lt;node.name|object.serial&gt;, and says the DEPRECATED node.target is the one
  /// that took an object.id. A global id is reused after its object is destroyed; a serial is not.
  /// Measured on the appliance, from our own log: id=71 / serial=58921 and id=76 / serial=58968
  /// (docs/queue/AUD-10.md, 2026-09-25). The serial is <see cref="ObjectSerial"/>. This id is only
  /// good for things that genuinely take a global id — <c>wpctl set-volume</c>, and matching the
  /// later <c>global_remove</c>.
  /// </remarks>
  public required uint Id { get; init; }

  /// <summary>
  /// The node's <c>object.serial</c>, read from the registry global's own properties — the value
  /// <c>PipeWireNativeStream</c> needs as <c>target.object</c>.
  /// </summary>
  /// <remarks>
  /// On <see cref="PipeWireRegistryListener.NodeAppeared"/> this is always a real, positive serial:
  /// a node whose serial is absent or unparseable is logged and NOT raised (never substituted with
  /// <see cref="Id"/>). On <see cref="PipeWireRegistryListener.NodeDisappeared"/> it is the serial
  /// recorded at appearance, or 0 if that node appeared without one.
  /// </remarks>
  public required uint ObjectSerial { get; init; }

  /// <summary>The node's <c>node.name</c>, e.g. <c>bluez_input.B0_D5_FB_D2_0D_68.2</c>.</summary>
  public required string NodeName { get; init; }

  /// <summary>Colon-separated upper-case BT address (e.g., "AA:BB:CC:DD:EE:FF").</summary>
  public required string DeviceAddress { get; init; }
}

/// <summary>What <see cref="PipeWireRegistryListener.ClassifyNodeGlobal"/> decided about a Node global.</summary>
internal enum BtNodeGlobalKind
{
  /// <summary>Not a BT capture node. Ignore it.</summary>
  NotBtCaptureNode,

  /// <summary>A BT capture node with a usable <c>object.serial</c>. Raise NodeAppeared.</summary>
  BtCaptureNode,

  /// <summary>
  /// A BT capture node whose <c>object.serial</c> is absent or unparseable. Warn and do NOT raise —
  /// publishing it would hand a consumer a serial we do not have.
  /// </summary>
  BtCaptureNodeWithoutSerial,
}

/// <summary>
/// Subscribes to the PipeWire registry for global add/remove events,
/// filters BT capture nodes (<c>bluez_input.&lt;MAC&gt;[.&lt;suffix&gt;]</c>)
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

  // Map of registry-global-id → the BT node it named. Populated by global(); consulted
  // by global_remove() to surface the address (and the serial/name recorded at appearance)
  // with the disappearance event. ConcurrentDictionary because callbacks fire on the
  // pw_thread_loop while managed code may be reading on other threads (defensive — current
  // consumers only touch via the event handlers).
  //
  // ⚠ AUD-10: a node that appears WITHOUT a readable object.serial is still recorded here, so
  // its removal is still reported — a capture stream bound to it through the pw-cli scrape
  // must still be torn down when it goes. Only its APPEARANCE is suppressed.
  private readonly ConcurrentDictionary<uint, (string Address, uint Serial, string NodeName)> _idToNode = new();

  /// <summary>True after <see cref="Start"/> brings up context/core/registry
  /// successfully and pw_proxy_add_object_listener has been invoked. ⚠ True says the
  /// subscription was REQUESTED, not that any event has ever arrived — until AUD-10 it was true
  /// for a listener that could not receive one. False if
  /// the listener has not been started, failed to start, or has been
  /// disposed. Consumers gate the fallback periodic scrape on this.
  /// </summary>
  public bool IsHealthy { get; private set; }

  /// <summary>Raised when a BT capture node with a readable <c>object.serial</c> appears in the
  /// registry. Fires on the pw_thread_loop thread; consumers should marshal heavy
  /// work onto the thread pool if needed.</summary>
  public event EventHandler<BtNodeRegistryEventArgs>? NodeAppeared;

  /// <summary>Raised when a previously-known BT capture node is removed. Fires on the
  /// pw_thread_loop thread, like <see cref="NodeAppeared"/>.</summary>
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
      // through pw_proxy_add_object_listener but a clean zero start is paranoia-safe.
      for (var i = 0; i < SpaHookSize; i++)
      {
        Marshal.WriteByte(_hook, i, 0);
      }

      // ⛔ OBJECT listener, not pw_proxy_add_listener — see the declaration in PipeWireNative.
      // Until AUD-10 this was pw_proxy_add_listener, which registers proxy events, so the
      // registry never delivered a single global and Start() still reported healthy.
      pw_proxy_add_object_listener(
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
        "PipeWireRegistryListener: native library {Library} not found "
        + "(check ldconfig cache for libpipewire-0.3 and libpw_helper); "
        + "falling back to periodic pw-cli scrape",
        ex.Message);
      IsHealthy = false;
      Cleanup();
    }
    catch (EntryPointNotFoundException ex)
    {
      // Log the actual missing symbol + library — the previous message blamed
      // libpw_helper.so for every entry-point miss, which masked the real cause
      // (pw_core_get_registry is static inline in libpipewire-0.3 headers, so
      // it has no real symbol; we bind it through libpw_helper now — see
      // PipeWireNative.pw_core_get_registry).
      _logger.LogWarning(
        ex,
        "PipeWireRegistryListener: required native entry point missing: {Detail}; "
        + "falling back to periodic pw-cli scrape",
        ex.Message);
      IsHealthy = false;
      Cleanup();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "PipeWireRegistryListener failed to start ({ExceptionType}: {Message}); "
        + "falling back to periodic pw-cli scrape",
        ex.GetType().Name, ex.Message);
      IsHealthy = false;
      Cleanup();
    }
  }

  /// <summary>
  /// Decides what a registry Node global is, from its <c>node.name</c> and <c>object.serial</c>
  /// properties. Pure — extracted from <see cref="OnGlobal"/> so the decision can be pinned by a
  /// unit test with no PipeWire daemon (the same reason <see cref="PipeWireRegistryFilter"/> is
  /// a static).
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-10 §1.2: the serial comes from the global's OWN <c>object.serial</c> property and from
  /// nowhere else. The registry id is a different number on this box (id 76 / serial 58968), and
  /// the previous wiring published the id as the serial. ⛔ If the property is missing or does not
  /// parse as a positive integer the result is <see cref="BtNodeGlobalKind.BtCaptureNodeWithoutSerial"/>
  /// — never a fallback to <paramref name="id"/>.
  /// </remarks>
  internal static BtNodeGlobalKind ClassifyNodeGlobal(
    uint id, string? nodeName, string? objectSerial, out BtNodeRegistryEventArgs? args)
  {
    args = null;
    if (nodeName == null
      || !PipeWireRegistryFilter.TryExtractBtCaptureAddress(nodeName, out var address))
    {
      return BtNodeGlobalKind.NotBtCaptureNode;
    }

    if (!uint.TryParse(objectSerial, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var serial)
      || serial == 0)
    {
      args = new BtNodeRegistryEventArgs
      {
        Id = id,
        ObjectSerial = 0,
        NodeName = nodeName,
        DeviceAddress = address,
      };
      return BtNodeGlobalKind.BtCaptureNodeWithoutSerial;
    }

    args = new BtNodeRegistryEventArgs
    {
      Id = id,
      ObjectSerial = serial,
      NodeName = nodeName,
      DeviceAddress = address,
    };
    return BtNodeGlobalKind.BtCaptureNode;
  }

  /// <summary>
  /// Fires on the pw_thread_loop for every global added to the registry.
  /// We filter for BT capture nodes and surface them as
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

      // Cheap name check first so object.serial is only read for our nodes.
      if (!PipeWireRegistryFilter.TryExtractBtCaptureAddress(nameOrNull, out _))
      {
        return;
      }

      var serialOrNull = ReadSpaDictKey(props, "object.serial");
      var kind = ClassifyNodeGlobal(id, nameOrNull, serialOrNull, out var args);
      if (args == null)
      {
        return;
      }

      // Recorded whether or not the serial was readable — see _idToNode's remarks.
      self._idToNode[id] = (args.DeviceAddress, args.ObjectSerial, args.NodeName);

      if (kind == BtNodeGlobalKind.BtCaptureNodeWithoutSerial)
      {
        // Warning: this is a node we would have bound to, and we are refusing to publish it
        // because the one number a stream needs is missing. Should never happen on a stock
        // PipeWire (object.serial is a standard global property); if it does, it is the
        // explanation for "the resume never re-bound".
        self._logger.LogWarning(
          "PW registry: BT node {Node} appeared (id={Id}) with no usable object.serial "
          + "(value: {Serial}) — NOT raising NodeAppeared; a registry id is not a serial. See AUD-10.",
          args.NodeName, id, serialOrNull ?? "(absent)");
        return;
      }

      self._logger.LogInformation(
        "PW registry: BT node appeared id={Id} serial={Serial} name={Node} address={Address}",
        id, args.ObjectSerial, args.NodeName, args.DeviceAddress);

      self.NodeAppeared?.Invoke(self, args);
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
      if (!self._idToNode.TryRemove(id, out var node))
      {
        return;
      }

      self._logger.LogInformation(
        "PW registry: BT node disappeared id={Id} serial={Serial} name={Node} address={Address}",
        id, node.Serial, node.NodeName, node.Address);

      self.NodeDisappeared?.Invoke(self, new BtNodeRegistryEventArgs
      {
        Id = id,
        ObjectSerial = node.Serial,
        NodeName = node.NodeName,
        DeviceAddress = node.Address,
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
    _idToNode.Clear();
    IsHealthy = false;
  }
}
#endif
