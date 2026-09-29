using Microsoft.Extensions.Hosting;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Finalizes <see cref="PlayHistoryTracker"/>'s in-flight entry when the host stops (AUD-78).
/// </summary>
/// <remarks>
/// Hosted services are stopped before the root service provider is disposed, so
/// <see cref="StopAsync"/> can still create the scope the repository lives in. The tracker's own
/// <see cref="PlayHistoryTracker.Dispose"/> runs during container disposal, when it cannot.
/// </remarks>
public sealed class PlayHistoryShutdownFinalizer : IHostedService
{
  private readonly PlayHistoryTracker _tracker;

  public PlayHistoryShutdownFinalizer(PlayHistoryTracker tracker)
  {
    _tracker = tracker;
  }

  /// <inheritdoc/>
  public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  /// <inheritdoc/>
  public Task StopAsync(CancellationToken cancellationToken) =>
    _tracker.FinalizeInFlightEntryAsync(cancellationToken);
}
