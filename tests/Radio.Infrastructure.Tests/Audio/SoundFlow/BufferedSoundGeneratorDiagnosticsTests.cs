using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Infrastructure.Audio.SoundFlow;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// LOG-7: <see cref="BufferedSoundGenerator{T}"/>'s render callback records, and a timer reports.
/// </summary>
/// <remarks>
/// Every emission is driven by <see cref="FakeTimeProvider.Advance"/>, whose timer callbacks run
/// synchronously on the advancing thread — so no assertion here waits on a clock (see CLAUDE.md, Test
/// Timing). The render callback is invoked directly on the test thread, so "the callback never touched
/// the logger" is an exact count, not a sampled one.
/// </remarks>
public class BufferedSoundGeneratorDiagnosticsTests
{
  private static readonly AudioFormat Stereo48k = new() { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 };

  private sealed class Harness : BufferedSoundGenerator<float>
  {
    public Harness(ILogger logger, TimeProvider time, Radio.Metrics.IMetricsCollector? metrics = null)
      : base(new Mock<AudioEngine>().Object, Stereo48k, logger, metricsCollector: metrics, timeProvider: time) { }

    public void Render(int samples) => GenerateAudio(new float[samples], Format.Channels);
  }

  private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
  private readonly RecordingLogger _log = new();

  // Underrun lines only: the test's own Render calls are milliseconds apart against a sub-millisecond
  // quantum, so they also register as missed deadlines, which can add their own Warning.
  private List<(LogLevel Level, string Template, string Message)> Warnings() =>
    _log.Entries.Where(e => e.Template.StartsWith("⚠️ Buffer underrun", StringComparison.Ordinal)).ToList();

  private Harness Create()
  {
    var generator = new Harness(_log, _time);
    _log.Clear(); // drop the constructor's "created" line
    return generator;
  }

  [Fact]
  public void RenderCallback_NeverTouchesTheLogger_ThroughUnderrunsAndCompensation()
  {
    using var generator = Create();

    // Underruns: a little data, then many under-filled reads.
    generator.AddSamples(new float[4]);
    for (var i = 0; i < 200; i++)
    {
      generator.Render(512);
    }

    // Compensation: a draining buffer below the 15 % threshold, read in full each time.
    generator.AddSamples(new float[8192]);
    for (var i = 0; i < 10; i++)
    {
      generator.Render(512);
    }

    Assert.Equal(0, _log.LogCalls);
    Assert.Equal(0, _log.IsEnabledCalls);
  }

  [Fact]
  public void UnderrunWarning_IsWrittenByTheTimer_WithTheOriginalTemplate()
  {
    using var generator = Create();
    generator.AddSamples(new float[4]);
    for (var i = 0; i < 5; i++)
    {
      generator.Render(8); // 4 + 8 + 8 + 8 + 8 = 36 zero samples over 5 underruns
    }
    Assert.Empty(_log.Entries);

    _time.Advance(TimeSpan.FromSeconds(1));

    var entry = Assert.Single(Warnings());
    Assert.Equal(LogLevel.Warning, entry.Level);
    Assert.Equal(
      "⚠️ Buffer underrun ({Type}): {Count} underruns, {Deficit} zero samples in last {Interval:F1}s " +
      "(buffer: {Buffered}/{Capacity}, total underruns: {TotalUnderruns})", entry.Template);
    Assert.StartsWith("⚠️ Buffer underrun (Single): 5 underruns, 36 zero samples in last 0.0s (buffer: 0/384000, total underruns: 5)",
      entry.Message);

    // The next window: a new count, the interval since the previous line, and the running total.
    generator.Render(8);
    _time.Advance(TimeSpan.FromSeconds(1));
    Assert.Equal("⚠️ Buffer underrun (Single): 1 underruns, 8 zero samples in last 1.0s (buffer: 0/384000, total underruns: 6)",
      Warnings()[1].Message);

    // No underrun since: no line.
    _time.Advance(TimeSpan.FromSeconds(3));
    Assert.Equal(2, Warnings().Count);
  }

  [Fact]
  public void CompensationLine_IsWrittenByTheTimer_AtMostEveryFiveSeconds()
  {
    using var generator = Create();
    generator.AddSamples(new float[8192]);
    for (var i = 0; i < 10; i++)
    {
      generator.Render(512);
    }

    _time.Advance(TimeSpan.FromSeconds(1));
    var first = Assert.Single(_log.Entries, e => e.Template.StartsWith("🔄 Clock drift compensation", StringComparison.Ordinal));
    Assert.Equal(LogLevel.Information, first.Level);
    Assert.Matches(@"^🔄 Clock drift compensation \(Single\): \d+ events, \d+ duplicated samples in last 0\.0s", first.Message);

    // More compensation, but under 5 s since the last line: nothing until the fifth second.
    generator.AddSamples(new float[4096]);
    for (var i = 0; i < 6; i++)
    {
      generator.Render(512);
    }
    for (var s = 0; s < 4; s++)
    {
      _time.Advance(TimeSpan.FromSeconds(1));
    }
    Assert.Single(_log.Entries, e => e.Template.StartsWith("🔄", StringComparison.Ordinal));
    _time.Advance(TimeSpan.FromSeconds(1));
    Assert.Equal(2, _log.Entries.Count(e => e.Template.StartsWith("🔄", StringComparison.Ordinal)));
  }

  [Fact]
  public void StatsLines_AreNotBuilt_WhenDebugIsOff()
  {
    using var generator = Create();
    generator.AddSamples(new float[1024]);
    generator.Render(512);

    _log.DebugEnabled = false;
    _time.Advance(TimeSpan.FromSeconds(10));
    // Not merely filtered out by the logger: never handed to it. LoggerExtensions.LogDebug calls Log
    // unconditionally, allocating its params array first, so only the IsEnabled guard prevents that.
    Assert.Equal(0, _log.DebugLogCalls);

    _log.DebugEnabled = true;
    _time.Advance(TimeSpan.FromSeconds(10));
    Assert.Equal(2, _log.Entries.Count(e => e.Level == LogLevel.Debug));
  }

  [Fact]
  public void TimingGauges_ReportTheWindow_NotTheResetValue()
  {
    // Before LOG-7 the gauges were read after the window had been reset, so max interval and max
    // execution always reported 0.
    var metrics = new Mock<Radio.Metrics.IMetricsCollector>();
    using (var generator = new Harness(_log, _time, metrics.Object))
    {
      generator.AddSamples(new float[4096]);
      generator.Render(512);
      Thread.SpinWait(1000); // any non-zero gap between the two callbacks
      generator.Render(512);

      _time.Advance(TimeSpan.FromSeconds(10));

      metrics.Verify(m => m.Gauge("audio.callback.max_interval_ms",
        It.Is<double>(v => v > 0), It.IsAny<IDictionary<string, string>>()), Times.Once);
      metrics.Verify(m => m.Gauge("audio.callback.max_execution_ms",
        It.Is<double>(v => v > 0), It.IsAny<IDictionary<string, string>>()), Times.Once);
    }
  }

  [Theory]
  [InlineData(999, 1000, true)]   // a tick measured a hair short of the period still counts
  [InlineData(500, 1000, true)]
  [InlineData(499, 1000, false)]
  [InlineData(4999, 5000, true)]
  [InlineData(3000, 5000, false)]
  public void IsDue_ToleratesHalfATickOfJitter(int elapsedMs, int intervalMs, bool expected)
  {
    Assert.Equal(expected, BufferedSoundGenerator<float>.IsDue(
      TimeSpan.FromMilliseconds(elapsedMs), TimeSpan.FromMilliseconds(intervalMs)));
  }

  [Fact]
  public void Stats_WhenNoCallbackRanSinceTheLastReport_ReportAnEmptyWindow()
  {
    // A generator that is no longer being pulled must not keep re-publishing its last window.
    var metrics = new Mock<Radio.Metrics.IMetricsCollector>();
    using var generator = new Harness(_log, _time, metrics.Object);
    generator.AddSamples(new float[4096]);
    generator.Render(512);
    Thread.SpinWait(1000);
    generator.Render(512);

    _time.Advance(TimeSpan.FromSeconds(10)); // window with callbacks
    _time.Advance(TimeSpan.FromSeconds(10)); // no callbacks since

    metrics.Verify(m => m.Gauge("audio.callback.max_interval_ms",
      It.Is<double>(v => v > 0), It.IsAny<IDictionary<string, string>>()), Times.Once);
    metrics.Verify(m => m.Gauge("audio.callback.max_interval_ms",
      0, It.IsAny<IDictionary<string, string>>()), Times.Once);
  }

  [Fact]
  public void MissedDeadlineWarning_IsWrittenByTheTimer_WithItsTemplate()
  {
    using var generator = Create();
    generator.AddSamples(new float[64]);
    _time.Advance(TimeSpan.FromSeconds(1)); // first stats tick samples the GC counts
    GC.Collect();
    _time.Advance(TimeSpan.FromSeconds(10)); // re-sample after the collection
    _log.Clear();

    // The quantum for 8 samples is ~0.08 ms; a spin between callbacks is a "missed deadline", and the
    // GC counts moved since the previous callback's snapshot, so it is GC-correlated.
    generator.Render(8);
    Thread.SpinWait(20_000);
    generator.Render(8);
    _time.Advance(TimeSpan.FromSeconds(1));

    var miss = Assert.Single(_log.Entries, e => e.Template.StartsWith("🔬 Missed callback deadline", StringComparison.Ordinal));
    Assert.Equal(LogLevel.Warning, miss.Level);
    Assert.Equal("🔬 Missed callback deadline ({Interval:F1}ms) with GC activity: Gen0 +{G0}, Gen1 +{G1}, Gen2 +{G2}",
      miss.Template);
    // Not tested here: that a second miss inside the 5 s throttle is discarded rather than deferred.
    // A second GC-correlated miss needs a GC-count re-sample (10 s cadence), which always lands outside
    // the throttle, so this harness cannot produce one.
  }

  [Fact]
  public void DisposedGenerator_ReportsNothing()
  {
    var generator = Create();
    generator.AddSamples(new float[4]);
    generator.Render(8);
    generator.Dispose();
    _log.Clear(); // the "disposed" summary line

    _time.Advance(TimeSpan.FromSeconds(30));

    Assert.Empty(_log.Entries);
  }

  [Fact]
  public void UndisposedGenerator_IsStillCollectable()
  {
    // The diagnostics timer must hold the generator weakly; otherwise an undisposed generator — and
    // its timer — would live forever.
    var weak = CreateAndAbandon(TimeProvider.System);

    for (var i = 0; i < 3 && weak.IsAlive; i++)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }

    Assert.False(weak.IsAlive);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference CreateAndAbandon(TimeProvider time)
  {
    var generator = new Harness(new RecordingLogger(), time);
    generator.AddSamples(new float[16]);
    return new WeakReference(generator);
  }

  private sealed class RecordingLogger : ILogger
  {
    private readonly object _sync = new();
    private readonly List<(LogLevel Level, string Template, string Message)> _entries = new();
    private int _logCalls;
    private int _debugLogCalls;
    private int _isEnabledCalls;

    public bool DebugEnabled { get; set; } = true;

    public int LogCalls { get { lock (_sync) { return _logCalls; } } }

    public int DebugLogCalls { get { lock (_sync) { return _debugLogCalls; } } }

    public int IsEnabledCalls { get { lock (_sync) { return _isEnabledCalls; } } }

    public IReadOnlyList<(LogLevel Level, string Template, string Message)> Entries
    {
      get { lock (_sync) { return _entries.ToList(); } }
    }

    public void Clear()
    {
      lock (_sync) { _entries.Clear(); _logCalls = 0; _debugLogCalls = 0; _isEnabledCalls = 0; }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
      lock (_sync) { _isEnabledCalls++; }
      return logLevel != LogLevel.Debug || DebugEnabled;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      var template = state is IReadOnlyList<KeyValuePair<string, object?>> kvs
        ? kvs.FirstOrDefault(kv => kv.Key == "{OriginalFormat}").Value as string ?? string.Empty
        : string.Empty;
      lock (_sync)
      {
        _logCalls++;
        if (logLevel == LogLevel.Debug)
        {
          _debugLogCalls++;
        }
        if (logLevel != LogLevel.Debug || DebugEnabled)
        {
          _entries.Add((logLevel, template, formatter(state, exception)));
        }
      }
    }
  }
}
