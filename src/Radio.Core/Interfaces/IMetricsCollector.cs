namespace Radio.Core.Interfaces;

using Radio.Core.Metrics;

/// <summary>
/// Interface for collecting metrics in the application.
/// Implementations buffer metrics in memory and flush periodically to storage.
/// </summary>
public interface IMetricsCollector
{
  /// <summary>
  /// Increments a counter metric by the specified value.
  /// Use for monotonically increasing values like requests, errors, items processed.
  /// </summary>
  /// <param name="key">The metric key (e.g., "audio.songs_played")</param>
  /// <param name="value">The value to increment by (default: 1)</param>
  /// <param name="tags">Optional tags for dimensional metrics</param>
  void Increment(string key, double value = 1.0, IDictionary<string, string>? tags = null);

  /// <summary>
  /// Records a gauge metric value.
  /// Use for values that can go up or down like memory usage, temperature, queue depth.
  /// </summary>
  /// <param name="key">The metric key (e.g., "system.memory_usage_mb")</param>
  /// <param name="value">The current value to record</param>
  /// <param name="tags">Optional tags for dimensional metrics</param>
  void Gauge(string key, double value, IDictionary<string, string>? tags = null);

  /// <summary>
  /// Gets the current number of buffered metrics waiting to be flushed.
  /// </summary>
  int BufferedCount { get; }
}
