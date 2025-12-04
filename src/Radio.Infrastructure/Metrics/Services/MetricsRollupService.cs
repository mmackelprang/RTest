namespace Radio.Infrastructure.Metrics.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Metrics;
using Radio.Infrastructure.Metrics.Repositories;

/// <summary>
/// Background service that rolls up metrics data from higher to lower resolutions
/// and prunes old data based on retention policies.
/// Runs periodically to aggregate Minute -> Hour -> Day and delete expired data.
/// </summary>
public sealed class MetricsRollupService : BackgroundService
{
  private readonly ILogger<MetricsRollupService> _logger;
  private readonly MetricsOptions _options;
  private readonly SqliteMetricsRepository _repository;

  public MetricsRollupService(
    ILogger<MetricsRollupService> logger,
    IOptions<MetricsOptions> options,
    SqliteMetricsRepository repository)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    _repository = repository ?? throw new ArgumentNullException(nameof(repository));
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.Enabled)
    {
      _logger.LogInformation("Metrics collection is disabled, skipping rollup service");
      return;
    }

    _logger.LogInformation("Metrics rollup service started. Running every {Interval} minutes",
      _options.RollupIntervalMinutes);

    // Wait a bit before first run to allow system to initialize
    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PerformRollupAndPruneAsync(stoppingToken);

        // Wait for next run
        await Task.Delay(
          TimeSpan.FromMinutes(_options.RollupIntervalMinutes),
          stoppingToken);
      }
      catch (OperationCanceledException)
      {
        // Expected when stopping
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in metrics rollup service");
        
        // Wait a bit before retrying
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
      }
    }

    _logger.LogInformation("Metrics rollup service stopped");
  }

  /// <summary>
  /// Performs the complete rollup and pruning operation.
  /// </summary>
  private async Task PerformRollupAndPruneAsync(CancellationToken ct)
  {
    _logger.LogDebug("Starting metrics rollup and prune operation");

    var now = DateTimeOffset.UtcNow;

    try
    {
      // Step 1: Roll up minute data to hours (data older than retention period)
      var minuteCutoff = now.AddMinutes(-_options.RetentionMinuteData);
      await _repository.RollupMinuteToHourAsync(minuteCutoff, ct);

      // Step 2: Roll up hour data to days (data older than retention period)
      var hourCutoff = now.AddHours(-_options.RetentionHourData);
      await _repository.RollupHourToDayAsync(hourCutoff, ct);

      // Step 3: Prune old minute data (should already be rolled up, but clean up any stragglers)
      await _repository.PruneOldDataAsync(
        MetricResolution.Minute,
        minuteCutoff,
        ct);

      // Step 4: Prune old hour data (should already be rolled up, but clean up any stragglers)
      await _repository.PruneOldDataAsync(
        MetricResolution.Hour,
        hourCutoff,
        ct);

      // Step 5: Prune old day data (based on retention policy)
      var dayCutoff = now.AddDays(-_options.RetentionDayData);
      await _repository.PruneOldDataAsync(
        MetricResolution.Day,
        dayCutoff,
        ct);

      _logger.LogInformation("Metrics rollup and prune operation completed successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to complete metrics rollup and prune operation");
      throw;
    }
  }
}
