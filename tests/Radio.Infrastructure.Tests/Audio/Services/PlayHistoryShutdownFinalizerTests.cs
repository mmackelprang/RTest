using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-78: the in-flight play history entry is finalized from a hosted service's StopAsync, while
/// the container can still create a scope — not from <see cref="PlayHistoryTracker.Dispose"/>,
/// which runs during container disposal and used to throw <see cref="ObjectDisposedException"/>
/// (logged as a Warning with a stack trace on every radio-api stop).
/// </summary>
public class PlayHistoryShutdownFinalizerTests
{
  private readonly Mock<IPlayHistoryRepository> _repo = new();
  private readonly Mock<IBluetoothService> _bluetooth = new();
  private readonly Mock<IAudioSource> _activeSource = new();
  private readonly CapturingLoggerProvider _logs = new();
  private readonly List<string> _finalizedIds = [];
  private string? _recordedId;

  public PlayHistoryShutdownFinalizerTests()
  {
    _activeSource.SetupGet(s => s.Type).Returns(AudioSourceType.Bluetooth);
    _repo.Setup(r => r.ExistsRecentlyPlayedAsync(It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);
    _repo.Setup(r => r.RecordPlayAsync(It.IsAny<PlayHistoryEntry>(), It.IsAny<CancellationToken>()))
      .Callback<PlayHistoryEntry, CancellationToken>((e, _) => _recordedId = e.Id)
      .Returns(Task.CompletedTask);
    _repo.Setup(r => r.FinalizeEntryAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
        It.IsAny<CancellationToken>()))
      .Callback<string, DateTime, CancellationToken>((id, _, _) => _finalizedIds.Add(id))
      .ReturnsAsync(true);
  }

  private ServiceProvider BuildProvider()
  {
    ServiceCollection services = new();
    services.AddLogging(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
    services.AddSingleton(_repo.Object);
    services.AddSingleton(_bluetooth.Object);
    // Same factory shape as AudioServiceExtensions.
    services.AddSingleton<PlayHistoryTracker>(sp => new PlayHistoryTracker(
      sp.GetRequiredService<ILogger<PlayHistoryTracker>>(),
      sp.GetRequiredService<IServiceScopeFactory>(),
      () => _activeSource.Object,
      sp.GetRequiredService<IBluetoothService>()));
    services.AddHostedService<PlayHistoryShutdownFinalizer>();
    return services.BuildServiceProvider();
  }

  /// <summary>Creates an in-flight entry by raising a real AVRCP metadata event.</summary>
  private void StartBluetoothTrack(ServiceProvider provider)
  {
    // Resolving the hosted services constructs the tracker, as host start does.
    _ = provider.GetServices<IHostedService>().ToList();
    _bluetooth.Raise(b => b.MetadataChanged += null, _bluetooth.Object,
      new BluetoothPlaybackMetadata { Title = "Song", Artist = "Band" });
    // Every mock completes synchronously, so the async-void handler has finished by here.
    Assert.NotNull(_recordedId);
  }

  [Fact]
  public async Task HostStop_FinalizesInFlightEntry_AndContainerDisposalLogsNoWarning()
  {
    ServiceProvider provider = BuildProvider();
    StartBluetoothTrack(provider);

    // Host shutdown order: hosted services stop, then the container is disposed.
    foreach (IHostedService hosted in provider.GetServices<IHostedService>())
    {
      await hosted.StopAsync(CancellationToken.None);
    }
    Assert.Equal([_recordedId!], _finalizedIds);

    await provider.DisposeAsync();

    Assert.Equal([_recordedId!], _finalizedIds);
    Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning);
  }

  [Fact]
  public async Task ContainerDisposal_WithoutHostStop_DoesNotTouchTheRepository()
  {
    // Crash/kill-like path: StopAsync never ran. Dispose must not try to reach the database
    // (it cannot — the container is already disposed); startup orphan cleanup owns this case.
    ServiceProvider provider = BuildProvider();
    StartBluetoothTrack(provider);

    await provider.DisposeAsync();

    Assert.Empty(_finalizedIds);
    Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Warning);
  }

  [Fact]
  public async Task HostStop_WithNoInFlightEntry_DoesNothing()
  {
    ServiceProvider provider = BuildProvider();
    _ = provider.GetServices<IHostedService>().ToList();

    foreach (IHostedService hosted in provider.GetServices<IHostedService>())
    {
      await hosted.StopAsync(CancellationToken.None);
    }
    await provider.DisposeAsync();

    Assert.Empty(_finalizedIds);
  }

  [Fact]
  public async Task HostStop_RepositoryFailure_IsLoggedAndSwallowed()
  {
    _repo.Setup(r => r.FinalizeEntryAsync(It.IsAny<string>(), It.IsAny<DateTime>(),
        It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("db gone"));
    ServiceProvider provider = BuildProvider();
    StartBluetoothTrack(provider);

    foreach (IHostedService hosted in provider.GetServices<IHostedService>())
    {
      await hosted.StopAsync(CancellationToken.None);
    }
    await provider.DisposeAsync();

    Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning
      && e.Message.Contains("Failed to finalize play history entry"));
  }

  private sealed record LogEntry(LogLevel Level, string Message);

  private sealed class CapturingLoggerProvider : ILoggerProvider
  {
    public List<LogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<LogEntry> entries) : ILogger
    {
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
      {
        lock (entries)
        {
          entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
      }
    }
  }
}
