using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Models.Audio;
using Radio.Core.Interfaces.Audio;
using Radio.Fingerprinting.Services;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// AUD-18: <see cref="BackgroundIdentificationService"/>'s twin of the tap's per-window Warning drops to Debug
/// while the capture watchdog is latched, and is a Warning otherwise. Runs one cycle through the internal
/// hook, so there is no start-up delay and no clock involved.
/// </summary>
public class BackgroundIdentificationServiceCaptureWatchdogTests
{
  private readonly List<(LogLevel Level, string Message)> _logs = new();
  private readonly FingerprintCaptureWatchdog _watchdog =
    new(new Mock<ILogger<FingerprintCaptureWatchdog>>().Object);

  private BackgroundIdentificationService CreateService(FingerprintCaptureWatchdog? watchdog)
  {
    var services = new ServiceCollection();
    services.AddSingleton<IAudioSampleProvider>(new EmptyTap());
    var options = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    options.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions { Enabled = true, SampleDurationSeconds = 1 });

    return new BackgroundIdentificationService(
      new CapturingLogger(_logs), services.BuildServiceProvider(), options.Object, null, watchdog);
  }

  private LogLevel NoSamplesLevel() =>
    Assert.Single(_logs, l => l.Message.StartsWith("No audio samples captured")).Level;

  private void TripWatchdog()
  {
    for (var i = 0; i < FingerprintCaptureWatchdog.TripAfterConsecutiveEmptyWindows; i++)
    {
      _watchdog.RecordWindow(
        CaptureWindowOutcome.EmptyWhilePlaying, "SDR Radio", PlaySource.Radio, TimeSpan.FromSeconds(15));
    }
    Assert.True(_watchdog.IsLatched);
  }

  [Fact]
  public async Task NoSamples_WatchdogNotLatched_IsAWarning()
  {
    using var service = CreateService(_watchdog);

    Assert.False(await service.RunOneCycleForTestingAsync());

    Assert.Equal(LogLevel.Warning, NoSamplesLevel());
  }

  [Fact]
  public async Task NoSamples_WatchdogLatched_IsDemotedToDebug()
  {
    TripWatchdog();
    using var service = CreateService(_watchdog);

    Assert.False(await service.RunOneCycleForTestingAsync());

    Assert.Equal(LogLevel.Debug, NoSamplesLevel());
  }

  [Fact]
  public async Task NoSamples_WithoutAWatchdog_IsAWarning()
  {
    using var service = CreateService(null);

    Assert.False(await service.RunOneCycleForTestingAsync());

    Assert.Equal(LogLevel.Warning, NoSamplesLevel());
  }

  /// <summary>Active, wants a lookup, and captures nothing — what the tap returns for a zero-byte window.</summary>
  private sealed class EmptyTap : IAudioSampleProvider
  {
    public bool IsActive => true;
    public string SourceName => "SDR Radio";
    public PlaySource SourceType => PlaySource.Radio;
    public string? SourceFilePath => null;
    public bool NeedsFingerprintingLookup => true;
    public Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default) =>
      Task.FromResult<AudioSampleBuffer?>(null);
  }

  private sealed class CapturingLogger(List<(LogLevel Level, string Message)> sink)
    : ILogger<BackgroundIdentificationService>
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
