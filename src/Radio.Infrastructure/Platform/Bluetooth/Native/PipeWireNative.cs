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

  // --- Helper library (pod builder) ---

  [DllImport(HelperLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern int pw_helper_build_s16le_format_pod(
    IntPtr buffer, int bufferSize, int rate, int channels);

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
}
#endif
