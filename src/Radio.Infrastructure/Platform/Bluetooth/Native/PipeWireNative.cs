#if !WINDOWS_TARGET
using System;
using System.Runtime.InteropServices;

namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// P/Invoke bindings for libpipewire-0.3 and the pw_helper C library.
/// All native calls use the PipeWire thread loop for synchronisation.
/// </summary>
internal static class PipeWireNative
{
  private const string PipeWireLib = "pipewire-0.3";
  private const string HelperLib = "pw_helper";

  // --- pw_init / deinit ---

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_init(IntPtr argc, IntPtr argv);

  // --- pw_thread_loop ---

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_thread_loop_new(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr props);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_thread_loop_start(IntPtr loop);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_thread_loop_stop(IntPtr loop);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_thread_loop_destroy(IntPtr loop);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_thread_loop_lock(IntPtr loop);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_thread_loop_unlock(IntPtr loop);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_thread_loop_get_loop(IntPtr loop);

  // --- pw_stream ---

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_stream_new_simple(
    IntPtr loop,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    IntPtr props,
    IntPtr events,
    IntPtr userData);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_stream_connect(
    IntPtr stream, PwDirection direction,
    uint targetId, PwStreamFlags flags,
    IntPtr[] paramPods, uint nParams);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_stream_disconnect(IntPtr stream);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_stream_destroy(IntPtr stream);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_stream_dequeue_buffer(IntPtr stream);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_stream_queue_buffer(IntPtr stream, IntPtr buffer);

  // --- pw_properties ---

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_properties_new_string(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_properties_free(IntPtr props);

  // --- pw_context / pw_core / pw_registry (Plan E: event subscription) ---
  //
  // Registry API: `pw_context_new(loop, props, sz) → pw_context *`;
  // `pw_context_connect(ctx, props, sz) → pw_core *`;
  // `pw_core_get_registry(core, version, sz) → pw_registry *`;
  // `pw_proxy_add_listener(registry, &hook, &events_struct, user_data)`.
  // Each pw_thread_loop owns its own context/core/registry chain.

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_context_new(IntPtr loop, IntPtr props, UIntPtr userDataSize);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_context_connect(IntPtr context, IntPtr props, UIntPtr userDataSize);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_context_destroy(IntPtr context);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_core_get_registry(IntPtr core, uint version, UIntPtr userDataSize);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_core_disconnect(IntPtr core);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_proxy_add_listener(IntPtr proxy, IntPtr hook, IntPtr events, IntPtr data);

  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern void pw_proxy_destroy(IntPtr proxy);

  public const uint PW_VERSION_REGISTRY = 3;
  public const uint PW_VERSION_REGISTRY_EVENTS = 0;

  // spa_hook is a small struct the caller owns (see <spa/utils/hook.h>).
  // Layout: struct spa_list link (2 pointers) + void *cb + void *removed + uint32_t pad.
  // 24 bytes is a safe upper bound on 64-bit; PipeWire only writes through the pointer
  // we pass, never reads it after add_listener for our purposes.
  public const int SpaHookSize = 64;

  /// <summary>
  /// PipeWire registry global() callback. Fires when a new global object
  /// appears in the registry. Properties are exposed as a spa_dict* and
  /// must be read via <c>pw_helper_spa_dict_lookup</c>.
  /// </summary>
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public delegate void PwRegistryGlobalDelegate(
    IntPtr userData, uint id, uint permissions,
    [MarshalAs(UnmanagedType.LPStr)] string type, uint version,
    IntPtr props);

  /// <summary>
  /// PipeWire registry global_remove() callback. Fires when a global is removed.
  /// </summary>
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public delegate void PwRegistryGlobalRemoveDelegate(IntPtr userData, uint id);

  /// <summary>
  /// Matches struct pw_registry_events (PipeWire 0.3.x).
  /// Version 0: { version, global, global_remove }.
  /// Must be pinned for the lifetime of the listener (the pointer is captured
  /// by pw_proxy_add_listener and dereferenced from the PipeWire thread loop).
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct PwRegistryEvents
  {
    public uint Version;
    public IntPtr Global;        // PwRegistryGlobalDelegate function pointer
    public IntPtr GlobalRemove;  // PwRegistryGlobalRemoveDelegate function pointer
  }

  // --- Helper library (pod builder + spa_dict lookup) ---

  [DllImport(HelperLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_helper_build_s16le_format_pod(
    IntPtr buffer, int bufferSize, int rate, int channels);

  /// <summary>
  /// Returns the value (as a C string pointer) for the given key in a spa_dict,
  /// or IntPtr.Zero if the dict is null or the key is missing. The returned
  /// pointer remains valid only for the duration of the callback that exposed
  /// the spa_dict; callers must copy it before returning.
  /// </summary>
  [DllImport(HelperLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr pw_helper_spa_dict_lookup(
    IntPtr dict, [MarshalAs(UnmanagedType.LPStr)] string key);

  // --- Enums ---

  public enum PwDirection : uint
  {
    Input = 0,
    Output = 1
  }

  [Flags]
  public enum PwStreamFlags : uint
  {
    None = 0,
    Autoconnect = 1 << 0,
    MapBuffers = 1 << 2,
    RtProcess = 1 << 4,
  }

  // --- Structs ---

  /// <summary>
  /// Matches struct pw_stream_events. Only the fields we use are declared;
  /// padding ensures correct offsets for the C ABI.
  /// Version 2 (PipeWire 0.3.x): version, destroy, state_changed, ..., process.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct PwStreamEvents
  {
    public uint Version;
    public IntPtr Destroy;        // void (*destroy)(void *data)
    public IntPtr StateChanged;   // void (*state_changed)(void *data, ...)
    public IntPtr Control;        // void (*control_info)(void *data, ...)
    public IntPtr IoChanged;      // void (*io_changed)(void *data, ...)
    public IntPtr ParamChanged;   // void (*param_changed)(void *data, ...)
    public IntPtr AddBuffer;      // void (*add_buffer)(void *data, ...)
    public IntPtr RemoveBuffer;   // void (*remove_buffer)(void *data, ...)
    public IntPtr Process;        // void (*process)(void *data)
    public IntPtr Drained;        // void (*drained)(void *data)
    public IntPtr Command;        // void (*command)(void *data, ...)
    public IntPtr TriggerDone;    // void (*trigger_done)(void *data)
  }

  /// <summary>
  /// Matches struct pw_buffer. Contains a pointer to struct spa_buffer.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct PwBuffer
  {
    public IntPtr Buffer;    // struct spa_buffer*
    public IntPtr UserData;
    public ulong Size;
    public ulong Requested;
  }

  /// <summary>
  /// Matches struct spa_buffer (first two fields only).
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct SpaBuffer
  {
    public uint NMetas;
    public uint NDatas;
    public IntPtr Metas;  // struct spa_meta*
    public IntPtr Datas;  // struct spa_data*
  }

  /// <summary>
  /// Matches struct spa_chunk.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct SpaChunk
  {
    public uint Offset;
    public uint Size;
    public int Stride;
  }

  /// <summary>
  /// Matches struct spa_data.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct SpaData
  {
    public uint Type;
    public uint Flags;
    public long Fd;        // int64_t in the ABI
    public uint MapOffset;
    public uint MaxSize;
    public IntPtr Data;    // void*
    public IntPtr Chunk;   // struct spa_chunk*
  }

  public const uint PW_STREAM_EVENTS_VERSION = 2;

  // --- POSIX thread scheduling (Plan D, feature-flagged via
  //     BluetoothOptions.UseRealtimeCaptureThread) ---
  //
  // glibc 2.34+ folded libpthread into libc. We can no longer rely on
  // "libpthread.so.0" being present on every distro (notably Pi OS Bookworm
  // has it, but newer Ubuntu/Debian ship libc-only). pthread_self and
  // pthread_setschedparam are now exported from libc, so DllImport against
  // "libc" works on every distro we ship to.

  [StructLayout(LayoutKind.Sequential)]
  public struct SchedParam
  {
    public int sched_priority;
  }

  public const int SCHED_FIFO = 1;

  [DllImport("libc", EntryPoint = "pthread_self")]
  public static extern IntPtr pthread_self();

  [DllImport("libc", EntryPoint = "pthread_setschedparam", SetLastError = true)]
  public static extern int pthread_setschedparam(IntPtr thread, int policy, ref SchedParam param);
}
#endif
