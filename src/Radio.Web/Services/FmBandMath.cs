using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// Pure arithmetic behind the visualizer's BAND view (AUD-76): the FM plot range, the US FM channel
/// plan, display normalisation of the band map's relative levels (any band), and the FM tap-to-tune
/// rule. Since AUD-91 the axis and tap arithmetic is <see cref="BandAxis"/>'s, and the FM members here
/// delegate to <see cref="BandAxis.Fm"/>. Kept out of the component so every rule is unit-tested
/// without a renderer.
/// </summary>
public static class FmBandMath
{
  /// <summary>Left edge of the plot, in Hz (87.5 MHz).</summary>
  public const long PlotMinHz = 87_500_000;

  /// <summary>Right edge of the plot, in Hz (108.0 MHz).</summary>
  public const long PlotMaxHz = 108_000_000;

  /// <summary>Lowest US FM channel, in Hz (87.9 MHz).</summary>
  public const long FirstChannelHz = 87_900_000;

  /// <summary>Highest US FM channel, in Hz (107.9 MHz).</summary>
  public const long LastChannelHz = 107_900_000;

  /// <summary>US FM channel spacing, in Hz (the odd tenths: 87.9, 88.1, …).</summary>
  public const long ChannelSpacingHz = 200_000;

  /// <summary>How far either side of a tap a peak may be and still win the tap, in Hz (±0.4 MHz).</summary>
  public const long SnapWindowHz = 400_000;

  /// <summary>
  /// How far above the map's noise estimate (its median level) a local maximum must stand to count
  /// as a peak, in dB. The levels are relative, not calibrated, so only differences within one map
  /// mean anything; 6 dB is a factor of four in power, well clear of channel-to-channel noise.
  /// </summary>
  public const double PeakProminenceDb = 6.0;

  /// <summary>
  /// Smallest level span the display stretches to full height, in dB. Without it a map of nothing
  /// but noise (a dead antenna, say) would be stretched until its 1–2 dB ripple looked like stations.
  /// </summary>
  public const double MinDisplaySpanDb = 10.0;

  /// <summary>
  /// Percentile of the map's levels drawn as the baseline (0 height). A low percentile rather than
  /// the minimum, so one unusually quiet channel does not lift every other channel off the floor.
  /// </summary>
  public const double DisplayFloorPercentile = 0.10;

  /// <summary>Converts a frequency to its horizontal position on the plot, 0 (87.5) to 1 (108.0).</summary>
  public static double HzToFraction(double hz) => BandAxis.Fm.HzToFraction(hz);

  /// <summary>Converts a horizontal plot position (clamped to 0–1) to a frequency in Hz.</summary>
  public static double FractionToHz(double fraction) => BandAxis.Fm.FractionToHz(fraction);

  /// <summary>
  /// The US FM channel (87.9 … 107.9 MHz, 200 kHz apart) nearest <paramref name="hz"/>, clamped to
  /// the band's first and last channels. A frequency exactly between two channels rounds up.
  /// </summary>
  public static long NearestChannelHz(double hz) => BandAxis.Fm.NearestChannelHz(hz);

  /// <summary>
  /// The frequency a tap at <paramref name="tappedHz"/> tunes to: the strongest <em>peak</em> within
  /// ±<see cref="SnapWindowHz"/>, else the nearest channel. With no map at all it is the nearest channel.
  /// </summary>
  /// <remarks>
  /// The rule is <see cref="BandAxis.ResolveTapTarget"/> on <see cref="BandAxis.Fm"/>, whose snap window
  /// (two channel spacings) is <see cref="SnapWindowHz"/>. A channel is a peak when its level is at least
  /// that of both neighbours in the map (a missing neighbour at the band edge counts as lower) <b>and</b>
  /// at least <see cref="PeakProminenceDb"/> above the map's median level. Two peaks in the window: the
  /// stronger wins; equal levels: the one nearer the tap. The neighbour rule also absorbs
  /// adjacent-channel aliasing (AUD-76 PR 1, M3): a shadow 200 kHz from a strong station is lower than
  /// the station, so it is never the peak.
  /// </remarks>
  public static long ResolveTapTarget(double tappedHz, IReadOnlyList<BandMapChannelDto>? channels) =>
    BandAxis.Fm.ResolveTapTarget(tappedHz, channels);

  /// <summary>
  /// Maps the map's relative levels to display heights, 0 to 1, in the order given. The
  /// <see cref="DisplayFloorPercentile"/> level maps to 0 and the strongest channel to 1, over a span of
  /// at least <see cref="MinDisplaySpanDb"/>; values outside are clamped.
  /// </summary>
  public static double[] NormalizeLevels(IReadOnlyList<BandMapChannelDto> channels)
  {
    if (channels.Count == 0)
    {
      return [];
    }

    double[] levels = channels.Select(c => (double)c.LevelDbfs).ToArray();
    double floor = Percentile(levels, DisplayFloorPercentile);
    double span = Math.Max(levels.Max() - floor, MinDisplaySpanDb);
    return levels.Select(l => Math.Clamp((l - floor) / span, 0.0, 1.0)).ToArray();
  }

  /// <summary>Median of <paramref name="values"/> (mean of the middle two for an even count); 0 when empty.</summary>
  public static double Median(IEnumerable<double> values)
  {
    double[] sorted = values.OrderBy(v => v).ToArray();
    if (sorted.Length == 0)
    {
      return 0;
    }

    int mid = sorted.Length / 2;
    return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
  }

  /// <summary>Nearest-rank-below percentile, <paramref name="p"/> in 0–1; 0 when empty.</summary>
  public static double Percentile(IReadOnlyList<double> values, double p)
  {
    if (values.Count == 0)
    {
      return 0;
    }

    double[] sorted = values.OrderBy(v => v).ToArray();
    int index = (int)Math.Floor(Math.Clamp(p, 0.0, 1.0) * (sorted.Length - 1));
    return sorted[index];
  }
}
