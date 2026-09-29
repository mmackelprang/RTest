namespace Radio.Metrics;

/// <summary>
/// One metric's data summarised over a time window — the row shape returned by
/// <see cref="IMetricsReader.GetWindowSummariesAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// UI-2. The Settings → Diagnostics tiles need one number per metric for a chosen window. They used
/// to get it by pulling each metric's full history one request at a time (capped at 40) and reducing
/// it in the browser circuit, on top of a snapshot call that ran two queries per key. This record is
/// the same reduction done once, in SQL, for every metric at once.
/// </para>
/// <para>
/// Which field a tile shows depends on <see cref="Type"/>: a counter's window total is
/// <see cref="Sum"/>; a gauge's current reading is <see cref="LatestAverage"/>, the average of the
/// newest bucket — exactly what the old per-key reduction picked (<c>history.Last().Value</c>).
/// </para>
/// </remarks>
public sealed record MetricWindowSummary
{
  /// <summary>The metric key (e.g. <c>"audio.buffer.underruns"</c>).</summary>
  public required string Key { get; init; }

  /// <summary>Counter or gauge, as recorded in <c>MetricDefinitions</c>.</summary>
  public required MetricType Type { get; init; }

  /// <summary>Sum of <c>ValueSum</c> over every bucket in the window. For a counter, the window total.</summary>
  public required double Sum { get; init; }

  /// <summary>Total samples aggregated across the window's buckets.</summary>
  public required long SampleCount { get; init; }

  /// <summary>Smallest bucket minimum in the window, or null when no bucket recorded one.</summary>
  public double? Min { get; init; }

  /// <summary>Largest bucket maximum in the window, or null when no bucket recorded one.</summary>
  public double? Max { get; init; }

  /// <summary>
  /// Average of the newest bucket in the window (<c>ValueSum / ValueCount</c>, or <c>ValueSum</c>
  /// when the count is zero — the same rule <see cref="IMetricsReader.GetHistoryAsync"/> applies).
  /// </summary>
  public required double LatestAverage { get; init; }

  /// <summary>Start of the newest bucket in the window; null for a counter with no bucket in it.</summary>
  public DateTimeOffset? LatestTimestamp { get; init; }

  /// <summary>How many buckets fell inside the window. Zero only for an idle counter.</summary>
  public required int BucketCount { get; init; }
}
