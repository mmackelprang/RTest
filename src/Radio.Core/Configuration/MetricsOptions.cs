namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the metrics collection system.
/// </summary>
public sealed class MetricsOptions
{
  /// <summary>
  /// Configuration section name for binding.
  /// </summary>
  public const string SectionName = "Metrics";

  /// <summary>
  /// Enable or disable metrics collection.
  /// Default: true
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Interval in seconds for flushing buffered metrics to disk.
  /// Default: 60 seconds
  /// </summary>
  public int FlushIntervalSeconds { get; set; } = 60;

  /// <summary>
  /// SQLite database path for metrics storage.
  /// DEPRECATED: Use DatabaseOptions.GetMetricsDatabasePath() instead for new deployments.
  /// This is maintained for backward compatibility.
  /// Default: ./data/metrics.db
  /// </summary>
  public string DatabasePath { get; set; } = "./data/metrics.db";

  /// <summary>
  /// Minutes to retain minute-resolution data.
  /// Default: 120 minutes (2 hours)
  /// </summary>
  public int RetentionMinuteData { get; set; } = 120;

  /// <summary>
  /// Hours to retain hour-resolution data.
  /// Default: 48 hours (2 days)
  /// </summary>
  public int RetentionHourData { get; set; } = 48;

  /// <summary>
  /// Days to retain day-resolution data.
  /// Default: 365 days (1 year)
  /// </summary>
  public int RetentionDayData { get; set; } = 365;

  /// <summary>
  /// Interval in minutes for running rollup/pruning operations.
  /// Default: 60 minutes (1 hour)
  /// </summary>
  public int RollupIntervalMinutes { get; set; } = 60;
}
