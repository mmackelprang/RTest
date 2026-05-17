namespace Radio.Metrics;

/// <summary>
/// Optional metadata describing a metric — its display unit, category,
/// and threshold band — used by dashboards to render unit-aware tiles.
/// <para>
/// Descriptors are purely declarative; the collector pipeline does not
/// require them. When absent, dashboards fall back to key-pattern
/// heuristics for unit selection. The descriptor surface exists so
/// that producers can opt in to authoritative units (e.g. fixing the
/// "Memory Usage Mb" tile that previously rendered as a percentage).
/// </para>
/// </summary>
public sealed record MetricDescriptor
{
  /// <summary>
  /// The metric key (e.g. <c>"system.memory_usage_mb"</c>).
  /// </summary>
  public required string Key { get; init; }

  /// <summary>
  /// Display unit used by dashboards to pick a formatter. Defaults to
  /// <see cref="MetricUnit.Bare"/> for unknown values.
  /// </summary>
  public MetricUnit Unit { get; init; } = MetricUnit.Bare;

  /// <summary>
  /// Optional category label used to group tiles in the dashboard
  /// (e.g. <c>"System | Memory"</c>). When null, the dashboard
  /// derives a category from the leading dot-segment of the key.
  /// </summary>
  public string? Category { get; init; }

  /// <summary>
  /// Optional warn threshold. Values at or above this render the tile
  /// in the amber signal color. Interpretation is unit-specific; see
  /// dashboard rules for inverted-above metrics (e.g. buffer fill).
  /// </summary>
  public double? Warn { get; init; }

  /// <summary>
  /// Optional critical threshold. Values at or above this render the
  /// tile in the red signal color. See <see cref="Warn"/> for
  /// inversion rules.
  /// </summary>
  public double? Critical { get; init; }

  /// <summary>
  /// Human-readable short name for the metric. Optional; dashboards
  /// fall back to a title-cased form of the trailing key segment.
  /// </summary>
  public string? DisplayName { get; init; }
}
