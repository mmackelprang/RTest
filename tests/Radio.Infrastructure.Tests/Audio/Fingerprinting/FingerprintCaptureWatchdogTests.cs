using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;
using Radio.Metrics;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// AUD-18: the watchdog's state machine in isolation. Windows are counted, not timed — every window is one
/// <see cref="FingerprintCaptureWatchdog.RecordWindow"/> call, and the clock is a
/// <see cref="FakeTimeProvider"/> advanced by exactly one window per call, so the outage durations in the
/// log lines are exact.
/// </summary>
public class FingerprintCaptureWatchdogTests
{
  private const int N = FingerprintCaptureWatchdog.TripAfterConsecutiveEmptyWindows;
  private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);

  private readonly List<(LogLevel Level, string Message)> _logs = new();
  private readonly Mock<IMetricsCollector> _metrics = new();
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero));
  private readonly FingerprintCaptureWatchdog _watchdog;

  public FingerprintCaptureWatchdogTests()
  {
    _watchdog = new FingerprintCaptureWatchdog(new CapturingLogger(_logs), _metrics.Object, _clock);
  }

  private void Record(CaptureWindowOutcome outcome, int times = 1)
  {
    for (var i = 0; i < times; i++)
    {
      _clock.Advance(Window);
      _watchdog.RecordWindow(outcome, "SDR Radio", PlaySource.Radio, Window);
    }
  }

  private int Warnings => _logs.Count(l => l.Level == LogLevel.Warning);
  private int Informations => _logs.Count(l => l.Level == LogLevel.Information);

  private void VerifyTrips(int times) =>
    _metrics.Verify(m => m.Increment(
        "fingerprint.capture_starvation_trips", 1,
        It.Is<IDictionary<string, string>>(t => t["source"] == "radio")),
      Times.Exactly(times));

  [Fact]
  public void OneWindowShortOfTheThreshold_DoesNotTrip()
  {
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N - 1);

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_logs);
    VerifyTrips(0);
  }

  [Fact]
  public void TheNthConsecutiveEmptyWindow_TripsOnce_WithOneWarningAndOneMetric()
  {
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N);

    Assert.True(_watchdog.IsLatched);
    var warning = Assert.Single(_logs);
    Assert.Equal(LogLevel.Warning, warning.Level);
    Assert.Contains("SDR Radio", warning.Message);
    Assert.Contains($"{N} consecutive windows", warning.Message);
    Assert.Contains("5m 0s", warning.Message); // 20 × 15 s, measured from the start of the first window
    VerifyTrips(1);
  }

  [Fact]
  public void WhileLatched_FurtherEmptyWindows_LogAndCountNothing()
  {
    // An 11-hour outage at 4 windows a minute is ~2,700 windows; a few hundred make the point.
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N + 300);

    Assert.True(_watchdog.IsLatched);
    Assert.Equal(LogLevel.Warning, Assert.Single(_logs).Level);
    VerifyTrips(1);
  }

  [Fact]
  public void AudioAfterATrip_LogsOneRecoveryLineWithTheOutageDuration_AndUnlatches()
  {
    Record(CaptureWindowOutcome.EmptyWhilePlaying, 240);
    Record(CaptureWindowOutcome.Audio);

    Assert.False(_watchdog.IsLatched);
    var recovery = Assert.Single(_logs, l => l.Level == LogLevel.Information);
    // 240 empty windows plus the recovering one = 241 × 15 s = 1 h 0 m 15 s since the run began.
    Assert.Contains("1h 0m", recovery.Message);
    Assert.Contains("240 empty capture windows", recovery.Message);

    // Recovery is said once: further audio is ordinary.
    Record(CaptureWindowOutcome.Audio, 5);
    Assert.Equal(1, Informations);
  }

  [Fact]
  public void AudioWithoutATrip_LogsNothing()
  {
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N - 1);
    Record(CaptureWindowOutcome.Audio);

    Assert.Empty(_logs);
    Assert.False(_watchdog.IsLatched);
  }

  [Fact]
  public void IntermittentEmptyWindows_NeverTrip()
  {
    for (var i = 0; i < 10; i++)
    {
      Record(CaptureWindowOutcome.EmptyWhilePlaying, N - 1);
      Record(CaptureWindowOutcome.Audio);
    }

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_logs);
    VerifyTrips(0);
  }

  [Fact]
  public void AWindowThatWasNotPlaying_BreaksAnUnlatchedRun()
  {
    // A pause (or a parked Bluetooth capture) between two short runs: neither run is sustained.
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N - 1);
    Record(CaptureWindowOutcome.EmptyNotPlaying);
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N - 1);

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_logs);
  }

  [Fact]
  public void WindowsThatWereNotPlaying_NeverTrip_HoweverMany()
  {
    Record(CaptureWindowOutcome.EmptyNotPlaying, N * 10);

    Assert.False(_watchdog.IsLatched);
    Assert.Empty(_logs);
  }

  [Fact]
  public void WhileLatched_AWindowThatWasNotPlaying_DoesNotCountAsRecovery()
  {
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N);
    Record(CaptureWindowOutcome.EmptyNotPlaying, 5);

    Assert.True(_watchdog.IsLatched);
    Assert.Equal(0, Informations);
  }

  [Fact]
  public void AfterRecovery_ANewSustainedRun_TripsAgain()
  {
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N);
    Record(CaptureWindowOutcome.Audio);
    Record(CaptureWindowOutcome.EmptyWhilePlaying, N);

    Assert.True(_watchdog.IsLatched);
    Assert.Equal(2, Warnings);
    VerifyTrips(2);
  }

  private sealed class CapturingLogger(List<(LogLevel Level, string Message)> sink)
    : ILogger<FingerprintCaptureWatchdog>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      lock (sink)
      {
        sink.Add((logLevel, formatter(state, exception)));
      }
    }
  }
}
