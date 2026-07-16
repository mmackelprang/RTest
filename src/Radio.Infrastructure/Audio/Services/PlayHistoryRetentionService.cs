using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Background service that periodically prunes play-history entries older than the
/// configured retention window, keeping the PlayHistory table — and any query over it —
/// bounded over long uptimes. Modeled on <c>MetricsRollupService</c>'s schedule/retry
/// loop. Because this is a singleton hosted service and <see cref="IPlayHistoryRepository"/>
/// is registered scoped, it resolves the repository through a per-run DI scope.
/// </summary>
public sealed class PlayHistoryRetentionService : BackgroundService
{
  private readonly ILogger<PlayHistoryRetentionService> _logger;
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly IOptions<PlayHistoryOptions> _options;

  public PlayHistoryRetentionService(
    ILogger<PlayHistoryRetentionService> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<PlayHistoryOptions> options)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    _options = options ?? throw new ArgumentNullException(nameof(options));
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var options = _options.Value;

    if (!options.RetentionEnabled)
    {
      _logger.LogInformation("Play history retention is disabled; retention service will not run");
      return;
    }

    var retentionDays = Math.Max(1, options.RetentionDays);
    var intervalHours = Math.Max(1, options.PruneIntervalHours);

    _logger.LogInformation(
      "Play history retention service started (retain {Days}d, prune every {Hours}h)",
      retentionDays, intervalHours);

    // Small startup delay so the prune doesn't compete with app/DB initialization.
    try
    {
      await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PruneOnceAsync(retentionDays, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during play history retention prune");
      }

      try
      {
        await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }

    _logger.LogInformation("Play history retention service stopped");
  }

  /// <summary>
  /// Runs a single retention prune pass against a freshly-scoped repository.
  /// </summary>
  private async Task PruneOnceAsync(int retentionDays, CancellationToken ct)
  {
    var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

    using var scope = _scopeFactory.CreateScope();
    var repository = scope.ServiceProvider.GetRequiredService<IPlayHistoryRepository>();

    var deleted = await repository.PruneOlderThanAsync(cutoff, ct);
    _logger.LogInformation(
      "Play history retention prune complete: {Deleted} entries older than {Cutoff:o} removed",
      deleted, cutoff);
  }
}
