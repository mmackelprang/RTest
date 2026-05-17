using System.Globalization;
using Radio.Web.Components.Shared;

namespace Radio.Web.Formatting;

/// <summary>
/// Enumeration of value units the metrics/status surfaces understand.
/// Used together with <see cref="UnitsFormatter.Format(double, Units)"/>.
/// </summary>
public enum Units
{
  /// <summary>Percentage value (0-100). Renders with a trailing <c>%</c>.</summary>
  Percent,
  /// <summary>Megabytes. Renders with a trailing <c> MB</c>.</summary>
  Megabytes,
  /// <summary>Milliseconds. Auto-promotes to seconds when ≥ 1000.</summary>
  Milliseconds,
  /// <summary>Unitless count. Thousands-separated.</summary>
  Count,
  /// <summary>Per-minute rate (events/min).</summary>
  PerMinute,
  /// <summary>Hertz. Delegates to <see cref="FrequencyFormatter.FormatStep(double)"/>.</summary>
  Frequency,
  /// <summary>Decibels (signed). Renders with a trailing <c> dB</c>.</summary>
  Decibels,
  /// <summary>Bare integer (no unit suffix).</summary>
  Bare,
}

/// <summary>
/// Static facade for <see cref="Units"/> value formatting.
/// Named <c>UnitsFormatter</c> (not <c>Units</c>) to avoid clashing with the enum name.
/// </summary>
public static class UnitsFormatter
{
  /// <summary>
  /// Formats a numeric value with the appropriate suffix and rounding rules.
  /// See individual enum members for exact behaviour.
  /// </summary>
  public static string Format(double value, Units unit)
  {
    var inv = CultureInfo.InvariantCulture;

    return unit switch
    {
      Units.Percent => value < 10
        ? value.ToString("0.0", inv) + "%"
        : value.ToString("0", inv) + "%",

      Units.Megabytes => value.ToString("0", inv) + " MB",

      Units.Milliseconds => value >= 1000
        ? (value / 1000.0).ToString("0.0", inv) + " s"
        : value.ToString("0", inv) + " ms",

      Units.Count => value.ToString("N0", inv),

      Units.PerMinute => value.ToString("0.0", inv) + "/min",

      // FrequencyFormatter.FormatStep already produces a unit-suffixed display string
      // and handles the Hz → MHz/kHz boundary.
      Units.Frequency => FrequencyFormatter.FormatStep(value),

      Units.Decibels => value.ToString("0", inv) + " dB",

      Units.Bare => value.ToString("0", inv),

      _ => value.ToString("0", inv),
    };
  }
}
