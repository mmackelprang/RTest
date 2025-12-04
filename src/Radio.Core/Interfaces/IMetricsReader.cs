namespace Radio.Core.Interfaces;

using Radio.Core.Metrics;

/// <summary>
/// Interface for reading historical metrics data.
/// </summary>
public interface IMetricsReader
{
  /// <summary>
  /// Retrieves time-series data for a specific metric over a date range.
  /// </summary>
  /// <param name="key">The metric key (e.g., "audio.songs_played")</param>
  /// <param name="start">Start of the time range</param>
  /// <param name="end">End of the time range</param>
  /// <param name="resolution">The time bucket resolution</param>
  /// <param name="tags">Optional tag filters</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>List of metric data points</returns>
  Task<IReadOnlyList<MetricPoint>> GetHistoryAsync(
    string key,
    DateTimeOffset start,
    DateTimeOffset end,
    MetricResolution resolution = MetricResolution.Minute,
    IDictionary<string, string>? tags = null,
    CancellationToken ct = default);

  /// <summary>
  /// Gets a snapshot of current/aggregate values for multiple metrics.
  /// For counters: returns total sum across all resolutions.
  /// For gauges: returns the most recent value.
  /// </summary>
  /// <param name="keys">The metric keys to retrieve</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>Dictionary of metric keys to their current values</returns>
  Task<IReadOnlyDictionary<string, double>> GetCurrentSnapshotsAsync(
    IEnumerable<string> keys,
    CancellationToken ct = default);

  /// <summary>
  /// Gets the aggregate value for a single metric.
  /// For counters: returns total sum across all resolutions.
  /// For gauges: returns the most recent value.
  /// </summary>
  /// <param name="key">The metric key</param>
  /// <param name="ct">Cancellation token</param>
  /// <returns>The aggregate value, or null if not found</returns>
  Task<double?> GetAggregateAsync(string key, CancellationToken ct = default);

  /// <summary>
  /// Lists all available metric keys in the system.
  /// </summary>
  /// <param name="ct">Cancellation token</param>
  /// <returns>List of metric keys</returns>
  Task<IReadOnlyList<string>> ListMetricKeysAsync(CancellationToken ct = default);
}
