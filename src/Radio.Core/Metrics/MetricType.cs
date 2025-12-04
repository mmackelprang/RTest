namespace Radio.Core.Metrics;

/// <summary>
/// Defines the type of metric being collected.
/// </summary>
public enum MetricType
{
  /// <summary>
  /// A counter metric that tracks monotonically increasing values.
  /// Values represent deltas (changes per bucket).
  /// Examples: songs_played, requests_total, errors_count
  /// </summary>
  Counter = 0,

  /// <summary>
  /// A gauge metric that tracks variable values over time.
  /// Values are stored as snapshots with Min/Max/Avg/Last per bucket.
  /// Examples: disk_usage, memory_usage, cpu_temperature
  /// </summary>
  Gauge = 1
}
