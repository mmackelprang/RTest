using System.Globalization;

namespace Radio.Web.Formatting;

/// <summary>
/// Text shown over the visualizer's BAND view (AUD-76; per band since AUD-91): the map's age, the sweep
/// progress, and frequencies. Invariant culture throughout, matching <see cref="Timestamps"/>.
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
  /// When <paramref name="otherBand"/> is given — a sweep of a band other than the one shown (AUD-91) —
  /// it is named: <c>Scanning AIR… 12 s</c>.
  /// </summary>
  public static string FormatScanning(double? estimatedSecondsRemaining, double progress, string? otherBand = null)
  {
    string scanning = string.IsNullOrEmpty(otherBand) ? "Scanning…" : $"Scanning {otherBand}…";
    if (estimatedSecondsRemaining is double r && !double.IsNaN(r))
    {
      return string.Create(CultureInfo.InvariantCulture, $"{scanning} {(long)Math.Ceiling(Math.Max(0, r))} s");
    }

    if (progress > 0)
    {
      return string.Create(CultureInfo.InvariantCulture, $"{scanning} {Math.Round(Math.Clamp(progress, 0, 1) * 100):0}%");
    }

    return scanning;
  }

  /// <summary>An FM frequency in MHz with one decimal, e.g. <c>99.5</c>.</summary>
  public static string FormatMhz(double hz) =>
    (hz / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture);

  /// <summary>
  /// A frequency as the BAND view names it for <paramref name="band"/>, without a unit (AUD-91): FM in MHz
  /// with one decimal (<c>99.5</c>, as AUD-76), AM in whole kHz (<c>1010</c>), and every other band in MHz
  /// with three decimals (<c>162.475</c>), enough for a 12.5 kHz grid to the nearest kHz.
  /// </summary>
  public static string FormatFrequency(double hz, string? band)
  {
    if (string.Equals(band, "AM", StringComparison.OrdinalIgnoreCase))
    {
      return (hz / 1_000.0).ToString("0", CultureInfo.InvariantCulture);
    }

    return string.Equals(band, "FM", StringComparison.OrdinalIgnoreCase)
      ? FormatMhz(hz)
      : (hz / 1_000_000.0).ToString("0.000", CultureInfo.InvariantCulture);
  }
}
