#if !WINDOWS_TARGET
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static Radio.Infrastructure.Platform.Bluetooth.Native.PipeWireNative;

namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// Delegate for receiving audio sample data from the PipeWire stream.
/// Uses float[] + count instead of ReadOnlySpan because Span is a ref struct
/// and cannot be used as a generic type argument for Action.
/// </summary>
internal delegate void AudioDataCallback(float[] samples, int count);

/// <summary>
/// Managed wrapper around a PipeWire capture stream that reads S16_LE audio
/// from a target node and delivers float samples via a callback.
///
/// Replaces the pw-record subprocess + pw-link link management with a single
/// native stream that PipeWire connects directly to the target node.
/// </summary>
internal sealed class PipeWireNativeStream : IDisposable
{
  private static bool _pwInitialized;
  private static readonly object InitLock = new();

  private readonly uint _targetNodeId;
  private readonly int _sampleRate;
  private readonly int _channels;
  private readonly AudioDataCallback _onAudioData;
  private readonly ILogger _logger;

  private IntPtr _threadLoop;
  private IntPtr _stream;
  private PwStreamEvents _events;
  private GCHandle _eventsHandle;
  private GCHandle _selfHandle;
  private bool _disposed;

  // Instrumentation: OnProcess delivery timing
  private long _lastOnProcessTimestamp;
  private double _maxOnProcessIntervalMs;
  private double _minOnProcessIntervalMs = double.MaxValue;
  private long _onProcessCount;
  private long _onProcessBurstCount; // intervals < 1ms (burst delivery)
  private double _maxOnProcessExecutionMs;
  private DateTime _lastOnProcessLogTime;

  // Pinned delegate references to prevent GC collection during native callbacks
  private readonly ProcessDelegate _processDelegate;

  // Native callback signature: void process(void* userData)
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate void ProcessDelegate(IntPtr userData);

  /// <summary>
  /// Creates a PipeWire capture stream targeting the given node.
  /// </summary>
  /// <param name="targetNodeId">PipeWire object serial of the node to capture from.</param>
  /// <param name="sampleRate">Sample rate (e.g. 48000).</param>
  /// <param name="channels">Channel count (e.g. 2).</param>
  /// <param name="onAudioData">Called on the PipeWire thread with float samples in [-1,1].</param>
  /// <param name="logger">Logger instance.</param>
  public PipeWireNativeStream(
    uint targetNodeId, int sampleRate, int channels,
    AudioDataCallback onAudioData, ILogger logger)
  {
    _targetNodeId = targetNodeId;
    _sampleRate = sampleRate;
    _channels = channels;
    _onAudioData = onAudioData;
    _logger = logger;

    // Pin delegate to prevent GC
    _processDelegate = OnProcess;

    EnsurePwInit();
  }

  private static void EnsurePwInit()
  {
    lock (InitLock)
    {
      if (!_pwInitialized)
      {
        pw_init(IntPtr.Zero, IntPtr.Zero);
        _pwInitialized = true;
      }
    }
  }

  /// <summary>
  /// Starts the capture stream. Audio data will be delivered via the callback.
  /// </summary>
  public void Start()
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(nameof(PipeWireNativeStream));
    }

    // Keep a GCHandle to this so the native callback can find us
    _selfHandle = GCHandle.Alloc(this);

    // Create thread loop
    _threadLoop = pw_thread_loop_new("radio-bt-capture", IntPtr.Zero);
    if (_threadLoop == IntPtr.Zero)
    {
      throw new InvalidOperationException("Failed to create PipeWire thread loop");
    }

    var loop = pw_thread_loop_get_loop(_threadLoop);

    // Build stream events struct — must remain at a stable pinned address
    // because PipeWire keeps a pointer to it for the lifetime of the stream.
    _events = new PwStreamEvents
    {
      Version = PW_STREAM_EVENTS_VERSION,
      Process = Marshal.GetFunctionPointerForDelegate(_processDelegate)
    };
    _eventsHandle = GCHandle.Alloc(_events, GCHandleType.Pinned);

    // Create stream with properties targeting our node
    var propsStr = $"{{ media.type = Audio media.category = Capture media.role = Music node.autoconnect = true target.object = {_targetNodeId} }}";
    var props = pw_properties_new_string(propsStr);

    _stream = pw_stream_new_simple(
      loop, "radio-bt-stream", props,
      _eventsHandle.AddrOfPinnedObject(), GCHandle.ToIntPtr(_selfHandle));

    if (_stream == IntPtr.Zero)
    {
      pw_thread_loop_destroy(_threadLoop);
      _threadLoop = IntPtr.Zero;
      throw new InvalidOperationException("Failed to create PipeWire stream");
    }

    // Build the format pod
    var podBuffer = Marshal.AllocHGlobal(256);
    try
    {
      var podSize = pw_helper_build_s16le_format_pod(podBuffer, 256, _sampleRate, _channels);
      if (podSize <= 0)
      {
        pw_stream_destroy(_stream);
        pw_thread_loop_destroy(_threadLoop);
        _stream = IntPtr.Zero;
        _threadLoop = IntPtr.Zero;
        throw new InvalidOperationException("Failed to build SPA format pod");
      }

      // Connect the stream
      var paramPods = new[] { podBuffer };
      // Use PW_ID_ANY so PipeWire resolves target from the target.object property
      const uint PW_ID_ANY = 0xffffffff;
      var result = pw_stream_connect(
        _stream, PwDirection.Input, PW_ID_ANY,
        PwStreamFlags.Autoconnect | PwStreamFlags.MapBuffers,
        paramPods, 1);

      if (result < 0)
      {
        pw_stream_destroy(_stream);
        pw_thread_loop_destroy(_threadLoop);
        _stream = IntPtr.Zero;
        _threadLoop = IntPtr.Zero;
        throw new InvalidOperationException($"pw_stream_connect failed: {result}");
      }
    }
    finally
    {
      Marshal.FreeHGlobal(podBuffer);
    }

    // Start the thread loop
    pw_thread_loop_start(_threadLoop);

    _logger.LogInformation(
      "PipeWire native stream started (target node {NodeId}, {Rate}Hz, {Ch}ch)",
      _targetNodeId, _sampleRate, _channels);
  }

  /// <summary>
  /// Stops the capture stream and releases all native resources.
  /// </summary>
  public void Stop()
  {
    if (_threadLoop == IntPtr.Zero)
    {
      return;
    }

    pw_thread_loop_lock(_threadLoop);
    try
    {
      if (_stream != IntPtr.Zero)
      {
        pw_stream_disconnect(_stream);
        pw_stream_destroy(_stream);
        _stream = IntPtr.Zero;
      }
    }
    finally
    {
      pw_thread_loop_unlock(_threadLoop);
    }

    pw_thread_loop_stop(_threadLoop);
    pw_thread_loop_destroy(_threadLoop);
    _threadLoop = IntPtr.Zero;

    if (_eventsHandle.IsAllocated)
    {
      _eventsHandle.Free();
    }
    if (_selfHandle.IsAllocated)
    {
      _selfHandle.Free();
    }

    _logger.LogInformation("PipeWire native stream stopped");
  }

  /// <summary>
  /// Called on the PipeWire thread loop when audio data is available.
  /// Dequeues the buffer, converts S16LE to float, and invokes the callback.
  /// </summary>
  private static void OnProcess(IntPtr userData)
  {
    if (userData == IntPtr.Zero)
    {
      return;
    }

    PipeWireNativeStream? self;
    try
    {
      var handle = GCHandle.FromIntPtr(userData);
      self = handle.Target as PipeWireNativeStream;
    }
    catch
    {
      return;
    }

    if (self == null || self._stream == IntPtr.Zero)
    {
      return;
    }

    var processStart = Stopwatch.GetTimestamp();

    // Track delivery interval
    if (self._lastOnProcessTimestamp > 0)
    {
      var intervalMs = (double)(processStart - self._lastOnProcessTimestamp)
        / Stopwatch.Frequency * 1000.0;
      if (intervalMs > self._maxOnProcessIntervalMs)
      {
        self._maxOnProcessIntervalMs = intervalMs;
      }
      if (intervalMs < self._minOnProcessIntervalMs)
      {
        self._minOnProcessIntervalMs = intervalMs;
      }
      if (intervalMs < 1.0)
      {
        self._onProcessBurstCount++;
      }
    }
    self._lastOnProcessTimestamp = processStart;
    self._onProcessCount++;

    var pwBufPtr = pw_stream_dequeue_buffer(self._stream);
    if (pwBufPtr == IntPtr.Zero)
    {
      return;
    }

    try
    {
      var pwBuf = Marshal.PtrToStructure<PwBuffer>(pwBufPtr);
      if (pwBuf.Buffer == IntPtr.Zero)
      {
        return;
      }

      var spaBuf = Marshal.PtrToStructure<SpaBuffer>(pwBuf.Buffer);
      if (spaBuf.NDatas == 0 || spaBuf.Datas == IntPtr.Zero)
      {
        return;
      }

      var spaData = Marshal.PtrToStructure<SpaData>(spaBuf.Datas);
      if (spaData.Data == IntPtr.Zero || spaData.Chunk == IntPtr.Zero)
      {
        return;
      }

      var chunk = Marshal.PtrToStructure<SpaChunk>(spaData.Chunk);
      if (chunk.Size == 0)
      {
        return;
      }

      // S16_LE: 2 bytes per sample, stereo = 4 bytes per frame.
      // PipeWire BT transport can deliver non-frame-aligned chunks during
      // packet loss/gaps. An odd sample count shifts L↔R channels for all
      // subsequent audio, causing audible distortion. Round down to frame
      // boundary (channels samples per frame) to prevent misalignment.
      var totalBytes = (int)chunk.Size;
      var sampleCount = totalBytes / 2;
      sampleCount = sampleCount / self._channels * self._channels; // frame-align

      if (sampleCount <= 0)
      {
        return;
      }

      // Convert S16_LE to float [-1.0, 1.0]
      // Use unsafe Span cast instead of per-sample Marshal.ReadInt16 to
      // eliminate ~1024 P/Invoke calls per callback
      var dataPtr = IntPtr.Add(spaData.Data, (int)chunk.Offset);
      var floatSamples = ArrayPool<float>.Shared.Rent(sampleCount);
      try
      {
        unsafe
        {
          var s16Span = new ReadOnlySpan<short>((void*)dataPtr, sampleCount);
          for (var i = 0; i < sampleCount; i++)
          {
            floatSamples[i] = s16Span[i] / 32768f;
          }
        }

        self._onAudioData(floatSamples, sampleCount);
      }
      finally
      {
        ArrayPool<float>.Shared.Return(floatSamples);
      }
    }
    catch
    {
      // Must not throw on the PipeWire thread loop
    }
    finally
    {
      pw_stream_queue_buffer(self._stream, pwBufPtr);

      var execMs = (double)(Stopwatch.GetTimestamp() - processStart) / Stopwatch.Frequency * 1000.0;
      if (execMs > self._maxOnProcessExecutionMs)
      {
        self._maxOnProcessExecutionMs = execMs;
      }
    }

    // Log OnProcess stats every 10 seconds
    var now = DateTime.UtcNow;
    if ((now - self._lastOnProcessLogTime).TotalSeconds >= 10 && self._onProcessCount > 0)
    {
      self._logger.LogInformation(
        "🔬 PipeWire OnProcess: count={Count}, interval min={Min:F2}ms max={Max:F2}ms, " +
        "bursts={Bursts}, execution max={Exec:F2}ms",
        self._onProcessCount,
        self._minOnProcessIntervalMs == double.MaxValue ? 0 : self._minOnProcessIntervalMs,
        self._maxOnProcessIntervalMs, self._onProcessBurstCount,
        self._maxOnProcessExecutionMs);
      // Reset per-window
      self._maxOnProcessIntervalMs = 0;
      self._minOnProcessIntervalMs = double.MaxValue;
      self._maxOnProcessExecutionMs = 0;
      self._lastOnProcessLogTime = now;
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    Stop();
  }
}
#endif
