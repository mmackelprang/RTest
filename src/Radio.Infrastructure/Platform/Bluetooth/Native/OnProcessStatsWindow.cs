using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// LOG-6: delivery-timing statistics for the PipeWire capture callback, recorded on the audio thread
/// and <em>emitted</em> from somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <c>PipeWireNativeStream.OnProcess</c> used to call <c>LogInformation</c> itself every 10 s, which
/// meant a params-array allocation, a <c>DateTime.UtcNow</c> read on every callback, and whatever the
/// logging pipeline does synchronously (level checks, enrichment, the async sink's queue) — all on the
/// thread that feeds the BT audio. ⚠ It is also the hard prerequisite of <c>LOG-10</c> (O4): promoting
/// that thread to <c>SCHED_FIFO</c> while it still logs risks a priority-inversion hang on a box
/// reachable only by SSH.
/// </para>
/// <para>
/// So the split is: the <c>Record*</c> methods are called on the callback thread and do only plain
/// field arithmetic — no logger, no lock, no allocation, no clock read beyond the
/// <see cref="Stopwatch"/> timestamp the caller already took. <see cref="EmitIfDue"/> is called from
/// <c>BluetoothCaptureWatchdog</c>'s existing tick (2 s) and does all the logging.
/// </para>
/// <para>
/// <b>Cross-thread contract.</b> Exactly one thread records (the PipeWire loop) and one emits (the
/// watchdog). The emitter reads the window's fields without synchronization — 64-bit reads are atomic on
/// the x64 and ARM64 targets — and then <em>requests</em> a window reset rather than performing it; the
/// recorder performs the reset at the start of its next callback. The consequence, stated rather than
/// hidden: a callback that lands between the emitter's read and that reset is counted in the
/// cumulative totals but its interval and execution time are dropped from both windows. That is at most
/// one callback per 10 s window, in a diagnostic.
/// </para>
/// <para>
/// ⚠ <b>The emitted line is a liveness heartbeat as well as a statistic.</b>
/// <c>scripts/research/bt_stall_detect.py</c> reports a stall when <c>PipeWire OnProcess</c> goes silent
/// during capture. The line is therefore emitted only when at least one callback arrived since the last
/// one — a stalled stream stays silent here exactly as it did when the callback logged for itself.
/// </para>
/// </remarks>
internal sealed class OnProcessStatsWindow
{
  /// <summary>How often the statistics line is emitted, as before LOG-6.</summary>
  internal static readonly TimeSpan EmitInterval = TimeSpan.FromSeconds(10);

  // ── Recorder side: written only by the callback thread. ─────────────────────────────────────────
  private long _count;          // cumulative callbacks
  private long _bursts;         // cumulative intervals < 1 ms
  private double _minIntervalMs = double.MaxValue;
  private double _maxIntervalMs;
  private double _maxExecutionMs;
  private int _realtimeResult;  // 0 = not attempted, 1 = applied, 2 = failed
  private int _realtimeErrno;
  private int _realtimePriority;

  // Set by the emitter, cleared by the recorder once it has reset the window.
  private int _resetRequested;

  // ── Emitter side: touched only by the emitting thread. ──────────────────────────────────────────
  private long _countAtLastEmit;
  private long _lastEmitTimestamp;
  private bool _realtimeResultLogged;

  /// <summary>Cumulative callback count. Safe to read from any thread.</summary>
  public long Count => Interlocked.Read(ref _count);

  /// <summary>
  /// Callback thread: one callback started, <paramref name="intervalMs"/> after the previous one (pass a
  /// negative value for the first callback, which has no interval).
  /// </summary>
  public void RecordCallback(double intervalMs)
  {
    if (Volatile.Read(ref _resetRequested) != 0)
    {
      _minIntervalMs = double.MaxValue;
      _maxIntervalMs = 0;
      _maxExecutionMs = 0;
      Volatile.Write(ref _resetRequested, 0);
    }

    if (intervalMs >= 0)
    {
      if (intervalMs > _maxIntervalMs)
      {
        _maxIntervalMs = intervalMs;
      }
      if (intervalMs < _minIntervalMs)
      {
        _minIntervalMs = intervalMs;
      }
      if (intervalMs < 1.0)
      {
        _bursts++;
      }
    }

    // Volatile so the emitter's "did anything arrive" comparison sees a current value.
    Volatile.Write(ref _count, _count + 1);
  }

  /// <summary>Callback thread: how long one callback took.</summary>
  public void RecordExecution(double executionMs)
  {
    if (executionMs > _maxExecutionMs)
    {
      _maxExecutionMs = executionMs;
    }
  }

  /// <summary>
  /// Callback thread: the outcome of the one-time <c>SCHED_FIFO</c> request. The errno must be read by
  /// the caller on the same thread, immediately after the call.
  /// </summary>
  public void RecordRealtimeResult(bool applied, int errno, int priority)
  {
    _realtimeErrno = errno;
    _realtimePriority = priority;
    Volatile.Write(ref _realtimeResult, applied ? 1 : 2);
  }

  /// <summary>
  /// Emitter thread: logs the SCHED_FIFO outcome once when it becomes known, and the statistics line
  /// when <see cref="EmitInterval"/> has passed since the last one <em>and</em> at least one callback
  /// arrived in between. Returns true if the statistics line was written.
  /// </summary>
  /// <param name="logger">The stream's logger — kept so the line's SourceContext is unchanged.</param>
  /// <param name="nowTimestamp">A <see cref="Stopwatch.GetTimestamp"/> value.</param>
  public bool EmitIfDue(ILogger logger, long nowTimestamp)
  {
    var realtime = Volatile.Read(ref _realtimeResult);
    if (realtime != 0 && !_realtimeResultLogged)
    {
      _realtimeResultLogged = true;
      if (realtime == 2)
      {
        // EPERM (1) is the common failure when systemd LimitRTPRIO is too low.
        logger.LogWarning(
          "pthread_setschedparam(SCHED_FIFO, {Prio}) failed: errno={Errno}. " +
          "Verify radio-api.service has LimitRTPRIO>={Prio}.",
          _realtimePriority, _realtimeErrno, _realtimePriority);
      }
      else
      {
        logger.LogInformation(
          "PipeWire capture thread bumped to SCHED_FIFO priority {Prio}",
          _realtimePriority);
      }
    }

    var count = Volatile.Read(ref _count);
    if (count == _countAtLastEmit)
    {
      // Nothing arrived since the last line: stay silent, so silence still means "stalled".
      return false;
    }

    if (_lastEmitTimestamp != 0
        && (nowTimestamp - _lastEmitTimestamp) < (long)(EmitInterval.TotalSeconds * Stopwatch.Frequency))
    {
      return false;
    }

    var min = _minIntervalMs;
    logger.LogInformation(
      "🔬 PipeWire OnProcess: count={Count}, interval min={Min:F2}ms max={Max:F2}ms, " +
      "bursts={Bursts}, execution max={Exec:F2}ms",
      count,
      min == double.MaxValue ? 0 : min,
      _maxIntervalMs, Volatile.Read(ref _bursts),
      _maxExecutionMs);

    _countAtLastEmit = count;
    _lastEmitTimestamp = nowTimestamp;
    Volatile.Write(ref _resetRequested, 1);
    return true;
  }
}
