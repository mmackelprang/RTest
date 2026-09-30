namespace Radio.Metrics;

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
  /// Summarises every metric in a time window, in one database query.
  /// </summary>
  /// <remarks>
  /// UI-2. This is the bounded replacement for calling <see cref="GetHistoryAsync"/> once per key
  /// (the Metrics page did so up to 40 times per refresh). A <b>counter</b> with no bucket in the
  /// window is reported with a zero sum — it counted nothing, which is the healthy reading for errors
  /// and underruns. A <b>gauge</b> with no bucket is absent, because a zero would be a claim about a
  /// value nothing measured.
  /// </remarks>
  /// <param name="start">Start of the window (inclusive).</param>
  /// <param name="end">End of the window (inclusive).</param>
  /// <param name="resolution">Which bucket table to read.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>One summary per metric with data in the window, ordered by key.</returns>
  Task<IReadOnlyList<MetricWindowSummary>> GetWindowSummariesAsync(
    DateTimeOffset start,
    DateTimeOffset end,
    MetricResolution resolution = MetricResolution.Minute,
    CancellationToken ct = default);

  /// <summary>
  /// Lists all available metric keys in the system.
  /// </summary>
  /// <param name="ct">Cancellation token</param>
  /// <returns>List of metric keys</returns>
  Task<IReadOnlyList<string>> ListMetricKeysAsync(CancellationToken ct = default);
}
