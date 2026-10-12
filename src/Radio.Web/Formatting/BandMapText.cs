using System.Globalization;
using Radio.Core.Models.Audio;

namespace Radio.Web.Formatting;

/// <summary>
/// Text shown with the visualizer's RADIO (formerly BAND) view (AUD-76; per band since AUD-91; UI-31): the
/// map's age, the sweep progress, plotted ranges, and frequencies. Invariant culture throughout, matching <see cref="Timestamps"/>.
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
  /// The sweep status (UI-31: "Discover", not "Scan", so it is not mistaken for the radio panel's seek
  /// SCAN buttons): <c>Discovering… 12 s</c> from the API's estimate (rounded up), else
  /// <c>Discovering… 40%</c> from the progress fraction, else <c>Discovering…</c> before either is known.
  /// When <paramref name="otherBand"/> is given — a sweep of a band other than the one shown (AUD-91) —
  /// it is named: <c>Discovering AIR… 12 s</c>.
  /// </summary>
  public static string FormatDiscovering(double? estimatedSecondsRemaining, double progress, string? otherBand = null)
  {
    string discovering = string.IsNullOrEmpty(otherBand) ? "Discovering…" : $"Discovering {otherBand}…";
    if (estimatedSecondsRemaining is double r && !double.IsNaN(r))
    {
      return string.Create(CultureInfo.InvariantCulture, $"{discovering} {(long)Math.Ceiling(Math.Max(0, r))} s");
    }

    if (progress > 0)
    {
      return string.Create(CultureInfo.InvariantCulture, $"{discovering} {Math.Round(Math.Clamp(progress, 0, 1) * 100):0}%");
    }

    return discovering;
  }

  /// <summary>
  /// A plotted range as the strip under the map names it (UI-31), with an en dash and the unit: AM and
  /// SW in kHz (<c>530–1710 kHz</c>, <c>1711–30000 kHz</c>; <see cref="BandUnits.IsKhz(string?)"/>,
  /// AUD-102), every other band in MHz with at most two decimals
  /// (<c>87.5–108 MHz</c>, <c>145.52–147.52 MHz</c>). Used for VHF's window, and for any band whose
  /// nominal range (the control panel's band list) could not be read. The same shape as the API's
  /// <c>RadioBandService.FormatRange</c>, which composes that list's <c>Range</c>; keep the two alike.
  /// </summary>
  public static string FormatRange(long minHz, long maxHz, string? band)
  {
    if (BandUnits.IsKhz(band))
    {
      return $"{BandUnits.FormatKhz(minHz)}–{BandUnits.FormatKhz(maxHz)} kHz";
    }

    return string.Create(CultureInfo.InvariantCulture, $"{minHz / 1_000_000.0:0.##}–{maxHz / 1_000_000.0:0.##} MHz");
  }

  /// <summary>An FM frequency in MHz with one decimal, e.g. <c>99.5</c>.</summary>
  public static string FormatMhz(double hz) =>
    (hz / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture);

  /// <summary>
  /// A frequency as the BAND view names it for <paramref name="band"/>, without a unit (AUD-91): FM in MHz
  /// with one decimal (<c>99.5</c>, as AUD-76), AM and SW in kHz (<c>1010</c>, <c>5905</c>, and
  /// <c>5907.5</c> off the 5 kHz grid; AUD-102), and every other band in MHz
  /// with at least three decimals and as many more as the frequency needs, down to the hertz
  /// (<c>162.475</c>, <c>118.100</c>, and <c>162.4125</c> for a channel of VHF's 12.5 kHz grid).
  /// </summary>
  public static string FormatFrequency(double hz, string? band)
  {
    if (BandUnits.IsKhz(band))
    {
      return BandUnits.FormatKhz(hz);
    }

    return string.Equals(band, "FM", StringComparison.OrdinalIgnoreCase)
      ? FormatMhz(hz)
      : (hz / 1_000_000.0).ToString("0.000###", CultureInfo.InvariantCulture);
  }

  /// <summary>
  /// A frequency and its band, as the tune messages name a station: <c>99.5 FM</c> on FM, as AUD-76
  /// shipped it; elsewhere with the unit, <c>162.475 MHz WB</c>, <c>1010 kHz AM</c>, <c>5905 kHz SW</c>.
  /// </summary>
  public static string FormatStation(double hz, string band)
  {
    if (string.Equals(band, "FM", StringComparison.OrdinalIgnoreCase))
    {
      return $"{FormatMhz(hz)} {band}";
    }

    string unit = BandUnits.IsKhz(band) ? "kHz" : "MHz";
    return $"{FormatFrequency(hz, band)} {unit} {band}";
  }
}
