using System.Globalization;

namespace Radio.Web.Formatting;

/// <summary>
/// Text shown over the visualizer's BAND view (AUD-76): the map's age, the sweep progress, and FM
/// frequencies. Invariant culture throughout, matching <see cref="Timestamps"/>.
/// </summary>
public static class BandMapText
{
  /// <summary>
  /// The age of the band map in coarse units: <c>just now</c> under a minute, then <c>N min ago</c>,
  /// <c>N h ago</c> and <c>N d ago</c>, each truncated (59 min 59 s is <c>59 min ago</c>). Null when
  /// there is no map; a negative age (clock skew) reads as <c>just now</c>.
  /// </summary>
  public static string? FormatAge(double? ageSeconds)
  {
    if (ageSeconds is not double s || double.IsNaN(s))
    {
      return null;
    }

    if (s < 60)
    {
      return "just now";
    }

    if (s < 3600)
    {
      return string.Create(CultureInfo.InvariantCulture, $"{(long)(s / 60)} min ago");
    }

    if (s < 86400)
    {
      return string.Create(CultureInfo.InvariantCulture, $"{(long)(s / 3600)} h ago");
    }

    return string.Create(CultureInfo.InvariantCulture, $"{(long)(s / 86400)} d ago");
  }

  /// <summary>
  /// The sweep overlay: <c>Scanning… 12 s</c> from the API's estimate (rounded up), else
  /// <c>Scanning… 40%</c> from the progress fraction, else <c>Scanning…</c> before either is known.
  /// </summary>
  public static string FormatScanning(double? estimatedSecondsRemaining, double progress)
  {
    if (estimatedSecondsRemaining is double r && !double.IsNaN(r))
    {
      return string.Create(CultureInfo.InvariantCulture, $"Scanning… {(long)Math.Ceiling(Math.Max(0, r))} s");
    }

    if (progress > 0)
    {
      return string.Create(CultureInfo.InvariantCulture, $"Scanning… {Math.Round(Math.Clamp(progress, 0, 1) * 100):0}%");
    }

    return "Scanning…";
  }

  /// <summary>An FM frequency in MHz with one decimal, e.g. <c>99.5</c>.</summary>
  public static string FormatMhz(double hz) =>
    (hz / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture);
}
