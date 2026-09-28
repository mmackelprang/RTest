using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Radio.API.Logging;

/// <summary>
/// LOG-8: a backstop against any single log line flooding the sinks. Each (SourceContext, message
/// template) pair may emit at most <see cref="Budget"/> events per <see cref="Window"/>; the rest are
/// dropped and <em>counted</em>, and <see cref="LogRateLimitReporter"/> writes one Warning per suppressed
/// pair per minute saying how many were dropped.
/// </summary>
/// <remarks>
/// <para>
/// It is a backstop, not the fix. Serilog evaluates filters on the calling thread <em>after</em> the
/// event has been built, so an audio-thread call site still pays for its own event — the fix for a hot
/// call site is to stop logging there (LOG-6/7, and <c>SrcVariableResampler</c>'s counter in this row).
/// What the filter bounds is what reaches the sinks: the file, and journald via the Warning-level
/// console sink — on a box where log volume is an audio problem.
/// </para>
/// <para>
/// The budget is sized from measurement, not taste: the busiest legitimate line on the box
/// (<c>RTLSDRCore.RadioReceiver</c> "DSP processing queue full", 2026-09-27 file sink) peaked at 97 per
/// minute. 120 per minute leaves that untouched and cuts a runaway such as the resampler's old
/// ~94–350 per <em>second</em> to 2 per second, plus one summary line.
/// </para>
/// <para>
/// <see cref="LogEventLevel.Fatal"/> is never suppressed, nor is the reporter's own summary (or it could
/// suppress the evidence of suppression). Keys are a message template by reference plus the source
/// string; a code path that builds templates dynamically (an interpolated string passed as a template)
/// would mint a key per message, so the table is cleared if it ever grows past
/// <see cref="MaxKeys"/> — losing only in-flight counts, never events.
/// </para>
/// </remarks>
public sealed class LogRateLimiter : ILogEventFilter
{
  /// <summary>Events allowed per key per window.</summary>
  public const int Budget = 120;

  /// <summary>The window the budget applies to.</summary>
  public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

  internal const int MaxKeys = 10_000;

  private readonly TimeProvider _time;
  private readonly long _windowTicks;
  private readonly ConcurrentDictionary<Key, Bucket> _buckets = new();
  private int _keyCount;

  public LogRateLimiter(TimeProvider? timeProvider = null)
  {
    _time = timeProvider ?? TimeProvider.System;
    _windowTicks = (long)(Window.TotalSeconds * _time.TimestampFrequency);
  }

  /// <inheritdoc />
  public bool IsEnabled(LogEvent logEvent)
  {
    if (logEvent.Level == LogEventLevel.Fatal)
    {
      return true;
    }

    var source = logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
      && value is ScalarValue { Value: string s } ? s : null;
    if (source == LogRateLimitReporter.SourceContext)
    {
      return true;
    }

    var key = new Key(source, logEvent.MessageTemplate);
    if (!_buckets.TryGetValue(key, out var bucket))
    {
      // ConcurrentDictionary.Count takes every internal lock, so the size is tracked separately.
      if (Interlocked.Increment(ref _keyCount) > MaxKeys)
      {
        _buckets.Clear();
        Interlocked.Exchange(ref _keyCount, 1);
      }
      bucket = _buckets.GetOrAdd(key, static _ => new Bucket());
    }
    var now = _time.GetTimestamp();
    var start = Volatile.Read(ref bucket.WindowStart);
    if (now - start >= _windowTicks
        && Interlocked.CompareExchange(ref bucket.WindowStart, now, start) == start)
    {
      // This thread won the roll-over. A concurrent increment may land in either window: harmless.
      Interlocked.Exchange(ref bucket.Count, 0);
    }

    if (Interlocked.Increment(ref bucket.Count) <= Budget)
    {
      return true;
    }

    Interlocked.Increment(ref bucket.Suppressed);
    return false;
  }

  /// <summary>
  /// Takes and resets the suppressed counts: (source, template text, count) for every key that dropped
  /// anything since the last call.
  /// </summary>
  public IReadOnlyList<(string? Source, string Template, long Count)> DrainSuppressed()
  {
    var drained = new List<(string?, string, long)>();
    foreach (var (key, bucket) in _buckets)
    {
      var count = Interlocked.Exchange(ref bucket.Suppressed, 0);
      if (count > 0)
      {
        drained.Add((key.Source, key.Template.Text, count));
      }
    }
    return drained;
  }

  private sealed class Bucket
  {
    public long WindowStart;
    public int Count;
    public long Suppressed;
  }

  // Template by reference: Serilog's parser cache hands back the same MessageTemplate instance for the
  // same text, and a reference comparison avoids hashing the template string on every event. A cache
  // miss only splits one line's budget across two keys.
  private readonly struct Key : IEquatable<Key>
  {
    public Key(string? source, MessageTemplate template)
    {
      Source = source;
      Template = template;
    }

    public string? Source { get; }

    public MessageTemplate Template { get; }

    public bool Equals(Key other) =>
      ReferenceEquals(Template, other.Template) && string.Equals(Source, other.Source, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Key other && Equals(other);

    public override int GetHashCode() =>
      HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Template), Source);
  }
}

/// <summary>
/// LOG-8: once a minute, writes one Warning per (source, template) the <see cref="LogRateLimiter"/>
/// suppressed, with the count — so a suppressed flood is visible as a number rather than as silence.
/// </summary>
public sealed class LogRateLimitReporter : BackgroundService
{
  /// <summary>This reporter's SourceContext; exempt from the limiter.</summary>
  public static readonly string SourceContext = typeof(LogRateLimitReporter).FullName!;

  private readonly LogRateLimiter _limiter;
  private readonly ILogger<LogRateLimitReporter> _logger;
  private readonly TimeProvider _time;

  public LogRateLimitReporter(LogRateLimiter limiter, ILogger<LogRateLimitReporter> logger, TimeProvider? timeProvider = null)
  {
    _limiter = limiter;
    _logger = logger;
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>Writes the summaries for everything suppressed since the last call. Internal for tests.</summary>
  internal void ReportOnce()
  {
    foreach (var (source, template, count) in _limiter.DrainSuppressed())
    {
      // The template is a code constant (not rendered values), so it carries no caller data.
      _logger.LogWarning(
        "LOG-8: rate limit suppressed {Count} events from {Source} in the last minute: {Template}",
        count, source ?? "(no source)", template);
    }
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(LogRateLimiter.Window, _time);
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
      {
        ReportOnce();
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // Shutting down.
    }
    ReportOnce();
  }
}
