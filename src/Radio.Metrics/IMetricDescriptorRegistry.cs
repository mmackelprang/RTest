namespace Radio.Metrics;

/// <summary>
/// Read-only registry of <see cref="MetricDescriptor"/> entries. Dashboards
/// consume this surface to render unit-aware tiles without resorting to
/// key-pattern heuristics (e.g. <c>"system.memory_usage_mb"</c> → MB,
/// <c>"audio.latency_ms"</c> → ms).
/// <para>
/// Producers register their authoritative units via
/// <see cref="IMetricDescriptorRegistry.Register"/>; consumers look up units
/// via <see cref="IMetricDescriptorRegistry.GetByKey"/>. When a key has no
/// descriptor, dashboards fall back to whatever default the consumer chooses
/// (typically <see cref="MetricUnit.Bare"/> or a residual heuristic for keys
/// that haven't been migrated yet).
/// </para>
/// </summary>
public interface IMetricDescriptorRegistry
{
  /// <summary>
  /// All registered descriptors, in insertion order. Multiple registrations
  /// for the same <see cref="MetricDescriptor.Key"/> are deduplicated — the
  /// most recent registration wins. Returns an empty list when no producer
  /// has registered any descriptors yet.
  /// </summary>
  IReadOnlyList<MetricDescriptor> All { get; }

  /// <summary>
  /// Returns the descriptor for <paramref name="key"/> if one is registered,
  /// otherwise <c>null</c>. Lookup is case-sensitive and does not consult
  /// the underlying key-pattern heuristic — callers must decide their own
  /// fallback behavior.
  /// </summary>
  /// <param name="key">The metric key (e.g. <c>"system.memory_usage_mb"</c>).</param>
  MetricDescriptor? GetByKey(string key);

  /// <summary>
  /// Adds or replaces the descriptor for <see cref="MetricDescriptor.Key"/>.
  /// </summary>
  /// <param name="descriptor">The descriptor to register.</param>
  void Register(MetricDescriptor descriptor);
}
