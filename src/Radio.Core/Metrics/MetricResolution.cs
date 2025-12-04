namespace Radio.Core.Metrics;

/// <summary>
/// Defines the time resolution for metric data aggregation.
/// </summary>
public enum MetricResolution
{
  /// <summary>
  /// 1-minute resolution buckets.
  /// High resolution, short retention period.
  /// </summary>
  Minute = 0,

  /// <summary>
  /// 1-hour resolution buckets.
  /// Medium resolution, medium retention period.
  /// </summary>
  Hour = 1,

  /// <summary>
  /// 1-day resolution buckets.
  /// Low resolution, long retention period.
  /// </summary>
  Day = 2
}
