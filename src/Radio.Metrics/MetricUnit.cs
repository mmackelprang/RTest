namespace Radio.Metrics;

/// <summary>
/// Server-side mirror of the consumer-facing display unit enum.
/// <para>
/// Read by dashboards and UI surfaces to pick a unit-aware formatter
/// for each metric. The default for any metric that has not been
/// described is <see cref="Bare"/>.
/// </para>
/// <para>
/// Values are deliberately aligned (name + intent) with the
/// <c>Radio.Web.Formatting.Units</c> enum so that the wire format and
/// the dashboard formatter stay in lockstep. If a new entry is added
/// here, mirror it in the Web formatting layer.
/// </para>
/// </summary>
public enum MetricUnit
{
  /// <summary>Percentage value (0-100). Renders with a trailing <c>%</c>.</summary>
  Percent = 0,

  /// <summary>Megabytes. Renders with a trailing <c> MB</c>.</summary>
  Megabytes = 1,

  /// <summary>Milliseconds. Auto-promotes to seconds when at or above 1000.</summary>
  Milliseconds = 2,

  /// <summary>Unitless count. Thousands-separated.</summary>
  Count = 3,

  /// <summary>Per-minute rate (events/min).</summary>
  PerMinute = 4,

  /// <summary>Hertz (Hz / kHz / MHz auto-scaling).</summary>
  Frequency = 5,

  /// <summary>Decibels (signed). Renders with a trailing <c> dB</c>.</summary>
  Decibels = 6,

  /// <summary>Bare integer — no unit suffix.</summary>
  Bare = 7,
}
