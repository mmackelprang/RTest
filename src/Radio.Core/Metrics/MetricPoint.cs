namespace Radio.Core.Metrics;

/// <summary>
/// Represents a single data point in a time-series metric.
/// Used for reading metric history.
/// </summary>
public sealed record MetricPoint
{
  /// <summary>
  /// The metric key (e.g., "audio.songs_played").
  /// </summary>
  public required string Key { get; init; }

  /// <summary>
  /// The timestamp for this data point (start of the time bucket).
  /// </summary>
  public required DateTimeOffset Timestamp { get; init; }

  /// <summary>
  /// The primary value for this data point.
  /// For Counter: the delta (change) in this bucket.
  /// For Gauge: the average value in this bucket.
  /// </summary>
  public required double Value { get; init; }

  /// <summary>
  /// The number of samples aggregated into this data point.
  /// </summary>
  public int Count { get; init; }

  /// <summary>
  /// The minimum value in this bucket (Gauge only).
  /// </summary>
  public double? Min { get; init; }

  /// <summary>
  /// The maximum value in this bucket (Gauge only).
  /// </summary>
  public double? Max { get; init; }

  /// <summary>
  /// The last recorded value in this bucket (Gauge only).
  /// </summary>
  public double? Last { get; init; }

  /// <summary>
  /// Optional tags associated with this metric.
  /// Example: { "provider": "azure", "region": "us-east" }
  /// </summary>
  public IReadOnlyDictionary<string, string>? Tags { get; init; }
}
