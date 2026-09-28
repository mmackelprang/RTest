using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Radio.API.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using ILogger = Serilog.ILogger;

namespace Radio.API.Tests.Logging;

/// <summary>
/// LOG-8: the per-line rate limit, through the real <see cref="ApiLoggerConfiguration.Build"/> pipeline.
/// Time is a <see cref="FakeTimeProvider"/>, so window boundaries are crossed by advancing, never by
/// waiting.
/// </summary>
public class LogRateLimiterTests
{
  private readonly FakeTimeProvider _time = new();
  private readonly Collector _sink = new();

  private (Logger Logger, LogRateLimiter Limiter) Build()
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Serilog:MinimumLevel:Default"] = "Information",
    }).Build();
    var limiter = new LogRateLimiter(_time);
    var logger = ApiLoggerConfiguration.Build(config, LogLevelSwitches.FromConfiguration(config), limiter)
      .WriteTo.Sink(_sink)
      .CreateLogger();
    return (logger, limiter);
  }

  private static ILogger From(ILogger logger, string source) =>
    logger.ForContext(Constants.SourceContextPropertyName, source);

  [Fact]
  public void RunawayLine_IsCutToTheBudget_AndTheExcessIsCounted()
  {
    var (logger, limiter) = Build();
    using (logger)
    {
      // The resampler's old failure mode: hundreds per second from one call site.
      var resampler = From(logger, "Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService");
      for (var i = 0; i < 350; i++)
      {
        resampler.Information("src_process failed: {Err}", "bad ratio");
      }

      Assert.Equal(LogRateLimiter.Budget, _sink.Count);
      var drained = Assert.Single(limiter.DrainSuppressed());
      Assert.Equal(350 - LogRateLimiter.Budget, drained.Count);
      Assert.Equal("src_process failed: {Err}", drained.Template);
      Assert.Empty(limiter.DrainSuppressed()); // drained means reset
    }
  }

  [Fact]
  public void TheBusiestMeasuredLegitimateLine_IsNeverSuppressed()
  {
    // 97/min — "DSP processing queue full", the box's busiest legitimate line on 2026-09-27.
    var (logger, limiter) = Build();
    using (logger)
    {
      var receiver = From(logger, "RTLSDRCore.RadioReceiver");
      for (var minute = 0; minute < 3; minute++)
      {
        for (var i = 0; i < 97; i++)
        {
          receiver.Warning("DSP processing queue full — dropping IQ batch");
        }
        _time.Advance(LogRateLimiter.Window);
      }

      Assert.Equal(3 * 97, _sink.Count);
      Assert.Empty(limiter.DrainSuppressed());
    }
  }

  [Fact]
  public void Budget_RefillsEachWindow()
  {
    var (logger, _) = Build();
    using (logger)
    {
      var source = From(logger, "Radio.X");
      for (var i = 0; i < 200; i++)
      {
        source.Information("same line");
      }
      _time.Advance(LogRateLimiter.Window);
      for (var i = 0; i < 200; i++)
      {
        source.Information("same line");
      }

      Assert.Equal(2 * LogRateLimiter.Budget, _sink.Count);
    }
  }

  [Fact]
  public void Budgets_AreIndependentPerSourceAndPerTemplate()
  {
    var (logger, _) = Build();
    using (logger)
    {
      for (var i = 0; i < 200; i++)
      {
        From(logger, "Radio.A").Information("line one");
        From(logger, "Radio.B").Information("line one");
        From(logger, "Radio.A").Information("line two");
      }

      Assert.Equal(3 * LogRateLimiter.Budget, _sink.Count);
    }
  }

  [Fact]
  public void Fatal_AndTheReportersOwnLines_AreNeverSuppressed()
  {
    var (logger, _) = Build();
    using (logger)
    {
      for (var i = 0; i < 200; i++)
      {
        From(logger, "Radio.X").Fatal("dying");
        From(logger, LogRateLimitReporter.SourceContext).Warning("LOG-8: summary");
      }

      Assert.Equal(400, _sink.Count);
    }
  }

  [Fact]
  public void Reporter_WritesOneCountedWarningPerSuppressedLine()
  {
    var (logger, limiter) = Build();
    using (logger)
    {
      for (var i = 0; i < 130; i++)
      {
        From(logger, "Radio.X").Information("noisy {N}", i);
      }
    }

    var log = new CapturingLogger();
    new LogRateLimitReporter(limiter, log, _time).ReportOnce();

    var entry = Assert.Single(log.Messages);
    Assert.Equal("LOG-8: rate limit suppressed 10 events from Radio.X in the last minute: noisy {N}", entry);
  }

  private sealed class Collector : ILogEventSink
  {
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Emit(LogEvent logEvent) => Interlocked.Increment(ref _count);
  }

  private sealed class CapturingLogger : ILogger<LogRateLimitReporter>
  {
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
      Exception? exception, Func<TState, Exception?, string> formatter)
    {
      Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, logLevel);
      Messages.Add(formatter(state, exception));
    }
  }
}
