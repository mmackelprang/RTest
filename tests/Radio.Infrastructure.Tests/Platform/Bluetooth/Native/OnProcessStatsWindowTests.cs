using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Platform.Bluetooth.Native;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Native;

/// <summary>
/// LOG-6: the OnProcess statistics are recorded on the audio thread and emitted from the watchdog.
/// These tests pin the emission contract the research tooling depends on — same text, 10 s cadence,
/// and silence when no callback arrived (which is how <c>bt_stall_detect.py</c> sees a stall).
/// </summary>
/// <remarks>
/// Time is passed in as Stopwatch timestamps, so nothing here reads a clock: no test can race one.
/// ⚠ These tests are single-threaded. They pin the ordering of the snapshot/reset handshake (one test
/// replays a mid-emission callback deterministically), but they do not exercise real concurrent
/// interleavings or the memory-model reasoning in the class remarks.
/// </remarks>
public class OnProcessStatsWindowTests
{
  private static readonly long Second = Stopwatch.Frequency;

  // Arbitrary non-zero origin: EmitIfDue treats a zero "last emit" timestamp as "never emitted".
  private const long T0 = 1_000_000;

  [Fact]
  public void NoCallbacks_EmitsNothing()
  {
    var window = new OnProcessStatsWindow();
    var log = new CapturingLogger();

    Assert.False(window.EmitIfDue(log, T0));
    Assert.Empty(log.Entries);
  }

  [Fact]
  public void FirstEmission_UsesTheOriginalTemplateAndWindowValues()
  {
    var window = new OnProcessStatsWindow();
    var log = new CapturingLogger();

    window.RecordCallback(-1);   // first callback: no interval
    window.RecordExecution(0.4);
    window.RecordCallback(10.5);
    window.RecordExecution(1.25);
    window.RecordCallback(0.5);  // a burst (< 1 ms)

    Assert.True(window.EmitIfDue(log, T0));

    var entry = Assert.Single(log.Entries);
    Assert.Equal(LogLevel.Information, entry.Level);
    Assert.Equal(
      "🔬 PipeWire OnProcess: count={Count}, interval min={Min:F2}ms max={Max:F2}ms, " +
      "bursts={Bursts}, execution max={Exec:F2}ms",
      entry.Template);
    Assert.Equal("🔬 PipeWire OnProcess: count=3, interval min=0.50ms max=10.50ms, bursts=1, execution max=1.25ms",
      entry.Message);
  }

  [Fact]
  public void SecondEmission_WaitsTenSeconds_AndResetsTheWindowButNotTheTotals()
  {
    var window = new OnProcessStatsWindow();
    var log = new CapturingLogger();
    window.RecordCallback(-1);
    window.RecordCallback(40.0);
    Assert.True(window.EmitIfDue(log, T0));

    window.RecordCallback(5.0);   // this callback also performs the requested window reset
    window.RecordExecution(0.3);

    Assert.False(window.EmitIfDue(log, T0 + 9 * Second));
    Assert.True(window.EmitIfDue(log, T0 + 10 * Second));

    Assert.Equal(2, log.Entries.Count);
    // count is cumulative (3); min/max are this window's only interval (5.00), not the 40 ms of the
    // previous window.
    Assert.Equal("🔬 PipeWire OnProcess: count=3, interval min=5.00ms max=5.00ms, bursts=0, execution max=0.30ms",
      log.Entries[1].Message);
  }

  [Fact]
  public void NoCallbackSinceLastEmission_StaysSilent_SoSilenceStillMeansStalled()
  {
    var window = new OnProcessStatsWindow();
    var log = new CapturingLogger();
    window.RecordCallback(-1);
    Assert.True(window.EmitIfDue(log, T0));

    // A stalled stream: the watchdog keeps ticking, the callback does not.
    for (var t = 2; t <= 60; t += 2)
    {
      Assert.False(window.EmitIfDue(log, T0 + t * Second));
    }

    Assert.Single(log.Entries);
  }

  [Fact]
  public void CallbackArrivingWhileTheEmitterLogs_BelongsToTheNextWindow()
  {
    // The emitter snapshots, requests the reset, and only then logs. A callback that lands during
    // the logging call therefore performs the reset and starts the next window — it is not merged into
    // the window just reported, and not wiped by a later reset. Driven single-threaded: the logger
    // itself plays the concurrent callback.
    var window = new OnProcessStatsWindow();
    window.RecordCallback(-1);
    window.RecordCallback(40.0);
    var log = new CapturingLogger { OnLog = () => window.RecordCallback(5.0) };

    Assert.True(window.EmitIfDue(log, T0));
    log.OnLog = null;
    Assert.True(window.EmitIfDue(log, T0 + 10 * Second));

    Assert.Equal("🔬 PipeWire OnProcess: count=3, interval min=5.00ms max=5.00ms, bursts=0, execution max=0.00ms",
      log.Entries[1].Message);
  }

  [Theory]
  [InlineData(true, 0, LogLevel.Information, "PipeWire capture thread bumped to SCHED_FIFO priority 50")]
  [InlineData(false, 1, LogLevel.Warning,
    "pthread_setschedparam(SCHED_FIFO, 50) failed: errno=1. Verify radio-api.service has LimitRTPRIO>=50.")]
  public void RealtimeOutcome_IsLoggedOnce_FromTheEmitter(bool applied, int errno, LogLevel level, string message)
  {
    var window = new OnProcessStatsWindow();
    var log = new CapturingLogger();

    window.RecordRealtimeResult(applied, errno, priority: 50);
    window.EmitIfDue(log, T0);
    window.EmitIfDue(log, T0 + 20 * Second);

    var entry = Assert.Single(log.Entries);
    Assert.Equal(level, entry.Level);
    Assert.Equal(message, entry.Message);
  }

  private sealed class CapturingLogger : ILogger
  {
    public List<(LogLevel Level, string Template, string Message)> Entries { get; } = new();

    /// <summary>Invoked inside Log — lets a test act as a callback that runs mid-emission.</summary>
    public Action? OnLog { get; set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      var template = state is IReadOnlyList<KeyValuePair<string, object?>> kvs
        ? kvs.FirstOrDefault(kv => kv.Key == "{OriginalFormat}").Value as string ?? string.Empty
        : string.Empty;
      Entries.Add((logLevel, template, formatter(state, exception)));
      OnLog?.Invoke();
    }
  }
}
