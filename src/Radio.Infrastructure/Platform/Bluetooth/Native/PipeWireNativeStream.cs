#if !WINDOWS_TARGET
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Audio.SoundFlow;
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
internal sealed class PipeWireNativeStream : IBtCaptureStream
{
  private static bool _pwInitialized;
  private static readonly object InitLock = new();

  private readonly uint _targetNodeId;
  private readonly int _sampleRate;
  private readonly int _channels;
  private readonly AudioDataCallback _onAudioData;
  private readonly ILogger _logger;
  private readonly bool _useRealtime;
  private readonly int _rtPriority;

  // Variable-rate resampler (Path D — docs/plans/2026-05-22-bt-input-resampler.md).
  // When non-null, OnProcess routes the input samples through libsamplerate
  // before forwarding to _onAudioData, eliminating the time-domain duplication
  // that BufferedSoundGenerator.CompensateClockDrift would otherwise apply.
  // Owned by this stream — disposed in Dispose(). Only touched from the
  // PipeWire thread loop callback, so no additional synchronization is needed.
  private readonly SrcVariableResampler? _resampler;
  private readonly float[]? _resampleOutputBuffer;

  private IntPtr _threadLoop;
  private IntPtr _stream;
  private PwStreamEvents _events;
  private GCHandle _eventsHandle;
  private GCHandle _selfHandle;
  private bool _disposed;
  private int _disposedFlag;

  // Instrumentation: OnProcess delivery timing
  private long _lastOnProcessTimestamp;
  private double _maxOnProcessIntervalMs;
  private double _minOnProcessIntervalMs = double.MaxValue;
  private long _onProcessCount;
  private long _onProcessBurstCount; // intervals < 1ms (burst delivery)
  private double _maxOnProcessExecutionMs;
  private DateTime _lastOnProcessLogTime;

  // SCHED_FIFO bump (Plan D, feature-flagged). Applied once on the first
  // OnProcess callback so we mutate the thread that actually drives the audio
  // pipeline, not the managed thread that constructed us.
  private bool _rtPriorityApplied;

  /// <summary>
  /// Wall-clock-equivalent stopwatch timestamp of the most recent OnProcess callback.
  /// Zero if OnProcess has not fired yet. Used by BluetoothCaptureWatchdog to detect
  /// FM-BT-3 silent quiescence. Safe to read from any thread.
  /// </summary>
  public long LastOnProcessTimestamp => Volatile.Read(ref _lastOnProcessTimestamp);

  /// <summary>
  /// Returns the elapsed milliseconds since the last OnProcess callback, or
  /// <see cref="long.MaxValue"/> if no callback has fired yet.
  /// </summary>
  public long MillisecondsSinceLastOnProcess()
  {
    var last = LastOnProcessTimestamp;
    if (last == 0)
    {
      return long.MaxValue;
    }
    return (long)((Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency * 1000.0);
  }

  // Pinned delegate references to prevent GC collection during native callbacks
  private readonly ProcessDelegate _processDelegate;
  // AUD-11 C-166: state_changed was declared in PwStreamEvents from PR #262 and never assigned, so the
  // stream has never been able to report PW_STREAM_STATE_ERROR — including the error that
  // node.dont-reconnect is expected to produce when the target is destroyed. Same GC-lifetime rule as
  // _processDelegate: Marshal.GetFunctionPointerForDelegate only stays valid while the delegate object
  // is reachable.
  private readonly PwStreamStateChangedDelegate _stateChangedDelegate;

  private int _state = (int)PwStreamState.Unconnected;
  private string? _lastError;

  /// <summary>
  /// The stream's most recent PipeWire state. <see cref="PwStreamState.Unconnected"/> until the first
  /// state_changed callback. Safe to read from any thread.
  /// </summary>
  public PwStreamState State => (PwStreamState)Volatile.Read(ref _state);

  /// <summary>
  /// PipeWire's own error string from the most recent state_changed callback, if that callback was a
  /// transition into <see cref="PwStreamState.Error"/>; null otherwise. Safe to read from any thread.
  /// </summary>
  public string? LastError => Volatile.Read(ref _lastError);

  /// <summary>The intended capture target (an <c>object.serial</c>), as passed to the constructor.</summary>
  public uint TargetNodeSerial => _targetNodeId;

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
  /// <param name="useRealtime">
  /// When true, the OnProcess thread is bumped to SCHED_FIFO priority
  /// <paramref name="rtPriority"/> on the first callback. Feature-flagged via
  /// <see cref="Radio.Core.Configuration.BluetoothOptions.UseRealtimeCaptureThread"/>.
  /// </param>
  /// <param name="rtPriority">
  /// SCHED_FIFO priority to apply when <paramref name="useRealtime"/> is true.
  /// Requires the host systemd unit to allow real-time scheduling.
  /// </param>
  /// <param name="useResampler">
  /// When true, captured samples are routed through a libsamplerate
  /// variable-rate resampler before delivery, eliminating BT-vs-speaker
  /// clock-skew time-domain duplication. See Path D in
  /// <c>docs/plans/2026-05-22-bt-input-resampler.md</c>. Feature-flagged via
  /// <c>BluetoothOptions.UseInputResampler</c>.
  /// </param>
  /// <param name="initialResamplerRatio">
  /// Initial conversion ratio (output_rate / input_rate). Measured ~1.00025
  /// (250 ppm consumer-faster) on the Ubuntu N100 + Pixel-class phone combo.
  /// Ignored when <paramref name="useResampler"/> is false.
  /// </param>
  public PipeWireNativeStream(
    uint targetNodeId, int sampleRate, int channels,
    AudioDataCallback onAudioData, ILogger logger,
    bool useRealtime = false, int rtPriority = 50,
    bool useResampler = false, double initialResamplerRatio = 1.0)
  {
    // AUD-11 C-163: 0 is not a valid object.serial. Accepting it writes `target.object = 0`, which
    // resolves to nothing. The caller is expected to have refused this already
    // (LinuxBluetoothService.StartCaptureSubprocess); this guard exists so a FUTURE caller cannot
    // reintroduce the same silent substitution. It is the FIRST statement on purpose: EnsurePwInit()
    // below is a native call, and a guard placed after it could not be unit-tested without libpipewire.
    ArgumentOutOfRangeException.ThrowIfZero(targetNodeId);

    _targetNodeId = targetNodeId;
    _sampleRate = sampleRate;
    _channels = channels;
    _onAudioData = onAudioData;
    _logger = logger;
    _useRealtime = useRealtime;
    _rtPriority = rtPriority;

    if (useResampler)
    {
      _resampler = new SrcVariableResampler(
        logger, channels, initialResamplerRatio, SrcQuality.SincFastest);

      // Resampler output buffer. Sized generously: PipeWire BT buffers are
      // typically ~1024-2048 samples per OnProcess; ratio is ~1.00025 so
      // worst-case stretch is well under 1 % extra. 8192 floats (~85 ms of
      // stereo 48 kHz audio) is several × the largest plausible single
      // callback, with headroom for SINC filter startup transients.
      _resampleOutputBuffer = new float[8192];
    }

    // Pin delegates to prevent GC
    _processDelegate = OnProcess;
    _stateChangedDelegate = OnStateChanged;

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
      FreeHandles(); // L4: a failed Start must not leave GCHandles rooting this stream
      throw new InvalidOperationException("Failed to create PipeWire thread loop");
    }

    var loop = pw_thread_loop_get_loop(_threadLoop);

    // Build stream events struct — must remain at a stable pinned address
    // because PipeWire keeps a pointer to it for the lifetime of the stream.
    _events = new PwStreamEvents
    {
      Version = PW_STREAM_EVENTS_VERSION,
      StateChanged = Marshal.GetFunctionPointerForDelegate(_stateChangedDelegate),
      Process = Marshal.GetFunctionPointerForDelegate(_processDelegate)
    };
    _eventsHandle = GCHandle.Alloc(_events, GCHandleType.Pinned);

    // Create stream with properties targeting our node (AUD-11: see BuildStreamProperties).
    var propsStr = BuildStreamProperties(_targetNodeId);
    var props = pw_properties_new_string(propsStr);

    _stream = pw_stream_new_simple(
      loop, "radio-bt-stream", props,
      _eventsHandle.AddrOfPinnedObject(), GCHandle.ToIntPtr(_selfHandle));

    if (_stream == IntPtr.Zero)
    {
      pw_thread_loop_destroy(_threadLoop);
      _threadLoop = IntPtr.Zero;
      FreeHandles(); // L4: a failed Start must not leave GCHandles rooting this stream
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
        FreeHandles(); // L4: a failed Start must not leave GCHandles rooting this stream
        throw new InvalidOperationException("Failed to build SPA format pod");
      }

      // Connect the stream
      var paramPods = new[] { podBuffer };
      // Use PW_ID_ANY so PipeWire resolves target from the target.object property.
      // ⛔ AUD-11: this stays. It is not the cause of the wrong-jack binding — see plan AUD-11 C-161.
      // The refusal lives in node.dont-reconnect, in BuildStreamProperties.
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
        FreeHandles(); // L4: a failed Start must not leave GCHandles rooting this stream
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
  /// Builds the <c>pw_properties</c> string for the capture stream.
  /// </summary>
  /// <remarks>
  /// ⚠ Extracted as a pure static so AUD-11's targeting contract can be pinned by a unit test with no
  /// PipeWire daemon and no native library — the same reason
  /// <c>LinuxBluetoothService.ParsePwCliOutputForBtNode</c> is a static.
  ///
  /// ⚠ node.dont-reconnect = true IS THE AUD-11 FIX and it is the only token here that refuses
  /// anything. pipewire-props(7): "When the node has a target configured and the target is destroyed,
  /// destroy the node as well. This property also inhibits that the node is moved to another
  /// sink/source." Without it, node.autoconnect = true is an instruction with no failure mode: when
  /// target.object cannot be resolved the session manager satisfies it with the DEFAULT source, which
  /// on the appliance is alsa_input.pci-0000_00_1f.3.analog-stereo — the unplugged line-in. Measured
  /// live 2026-09-06 and again, at 1 Hz, 2026-09-25 (docs/queue/AUD-10.md).
  ///
  /// ⚠ AUD-10: dont-reconnect does NOT stop us following the node across a pause. The A2DP node is
  /// destroyed on pause and recreated on resume under a NEW object.serial, which this stream's
  /// target.object can never match. Following it is done by starting a NEW stream against the new
  /// serial (LinuxBluetoothService.RebindParkedCaptureAsync), never by re-linking this one.
  ///
  /// ⚠ node.autoconnect STAYS. Removing it instead was considered and rejected without measurement:
  /// this box's WirePlumber graph is hand-tuned (deploy/common/41-disable-bt-input-restore-target.lua)
  /// and it is not established that the stream would connect at all without it. Plan AUD-11 §6.2 holds
  /// that option in reserve.
  ///
  /// ⚠ targetNodeId is an object.serial, NOT a registry global id. pipewire-props(7) gives
  /// target.object as &lt;node.name|object.serial&gt;, and says the DEPRECATED node.target is the one
  /// that took an object.id. On the appliance they are different numbers (id 76 / serial 58968).
  ///
  /// ⛔ Do NOT respond to a targeting problem by passing targetNodeId to pw_stream_connect's targetId
  /// argument. That parameter means node id, and handing it a serial is the bug PR #262 (commit
  /// 5353f020a) already paid to remove. See plan AUD-11 C-161.
  /// </remarks>
  internal static string BuildStreamProperties(uint targetNodeId) =>
    "{ media.type = Audio media.category = Capture media.role = Music "
    + "node.autoconnect = true node.dont-reconnect = true "
    + $"target.object = {targetNodeId} }}";

  /// <summary>
  /// Fires on the PipeWire thread loop for every stream state transition.
  /// </summary>
  /// <remarks>
  /// ⚠ Must not throw and must not block — this runs on the loop that also drives OnProcess.
  /// ⚠ AUD-11: an ERROR transition is the loud form of the wrong-jack defect. With
  /// node.dont-reconnect = true, a destroyed target is expected to arrive here rather than being
  /// silently satisfied by the default source. It is not the only guard, because whether the
  /// property is honoured is WirePlumber's decision; the registry teardown and the peer audit in
  /// LinuxBluetoothService are the independent ones.
  /// </remarks>
  private static void OnStateChanged(IntPtr userData, int oldState, int newState, IntPtr error)
  {
    if (userData == IntPtr.Zero)
    {
      return;
    }

    PipeWireNativeStream? self;
    try
    {
      self = GCHandle.FromIntPtr(userData).Target as PipeWireNativeStream;
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
      var message = error == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(error);
      Volatile.Write(ref self._state, newState);
      Volatile.Write(ref self._lastError, newState == (int)PwStreamState.Error ? message : null);

      if (newState == (int)PwStreamState.Error)
      {
        // Warning, so it reaches `journalctl -u radio-api` — Radio.API's console sink is restricted to
        // Warning and above (LOG-11) and under systemd the console IS the journal.
        self._logger.LogWarning(
          "PipeWire stream error: {Old} -> {New} for target.object {Serial}: {Error}",
          (PwStreamState)oldState, (PwStreamState)newState, self._targetNodeId,
          message ?? "(no message)");
      }
      else
      {
        // Information: file sink only. Deliberately quiet — log volume on this box correlates with
        // audible audio distortion, and healthy transitions are a handful per capture session.
        self._logger.LogInformation(
          "PipeWire stream state: {Old} -> {New} (target.object {Serial})",
          (PwStreamState)oldState, (PwStreamState)newState, self._targetNodeId);
      }
    }
    catch
    {
      // Must not throw on the PipeWire thread loop.
    }
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

    FreeHandles();

    _logger.LogInformation("PipeWire native stream stopped");
  }

  /// <summary>
  /// Frees the pinned events struct and the self handle. Called by <see cref="Stop"/> and by every
  /// failure path in <see cref="Start"/> — those zero <c>_threadLoop</c>, so a later Stop() returns
  /// early and would otherwise leave both handles (and this object) rooted for the process lifetime.
  /// </summary>
  private void FreeHandles()
  {
    if (_eventsHandle.IsAllocated)
    {
      _eventsHandle.Free();
    }
    if (_selfHandle.IsAllocated)
    {
      _selfHandle.Free();
    }
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

    // Apply SCHED_FIFO priority on the first callback (feature-flagged via
    // BluetoothOptions.UseRealtimeCaptureThread). This must run on the
    // PipeWire thread that actually drives OnProcess, not the managed thread
    // that constructed the stream — that's why it lives here rather than in
    // Start(). Idempotent guard ensures one call per stream lifetime.
    if (!self._rtPriorityApplied && self._useRealtime)
    {
      self._rtPriorityApplied = true;
      var param = new SchedParam { sched_priority = self._rtPriority };
      var result = pthread_setschedparam(pthread_self(), SCHED_FIFO, ref param);
      if (result != 0)
      {
        // EPERM (1) is the common failure when systemd LimitRTPRIO is too low.
        self._logger.LogWarning(
          "pthread_setschedparam(SCHED_FIFO, {Prio}) failed: errno={Errno}. " +
          "Verify radio-api.service has LimitRTPRIO>={Prio}.",
          self._rtPriority, Marshal.GetLastPInvokeError(), self._rtPriority);
      }
      else
      {
        self._logger.LogInformation(
          "PipeWire capture thread bumped to SCHED_FIFO priority {Prio}",
          self._rtPriority);
      }
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

        if (self._resampler != null && self._resampleOutputBuffer != null)
        {
          // Path D: route input through libsamplerate variable-rate SRC,
          // stretching the BT-clock stream to match the consumer clock.
          // The output buffer is sized for ~85 ms of audio (8192 floats
          // stereo 48 kHz); single OnProcess buffers are well under that.
          var inputSpan = floatSamples.AsSpan(0, sampleCount);
          var outputSpan = self._resampleOutputBuffer.AsSpan();
          var framesOut = self._resampler.Process(inputSpan, outputSpan);
          var samplesOut = framesOut * self._channels;
          if (samplesOut > 0)
          {
            self._onAudioData(self._resampleOutputBuffer, samplesOut);
          }
        }
        else
        {
          // Direct path: resampler disabled, BufferedSoundGenerator's
          // CompensateClockDrift handles skew via time-domain duplication.
          self._onAudioData(floatSamples, sampleCount);
        }
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
    // Atomic: two teardown paths can race to dispose the same stream (AUD-10 — see
    // LinuxBluetoothService.StopCaptureSubprocess), and Stop() twice would stop and destroy the
    // PipeWire thread loop twice.
    if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
    {
      return;
    }
    _disposed = true;
    Stop();
    // Resampler dispose AFTER Stop() so the PW thread loop is no longer
    // calling OnProcess by the time we free the native SRC state.
    _resampler?.Dispose();
  }
}
#endif
