using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// Pure arithmetic behind the visualizer's BAND view (AUD-76): the plotted frequency range, the US FM
/// channel plan, display normalisation of the band map's relative levels, and the tap-to-tune rule.
/// Kept out of the component so every rule here is unit-tested without a renderer.
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
  public static double HzToFraction(double hz) => (hz - PlotMinHz) / (PlotMaxHz - PlotMinHz);

  /// <summary>Converts a horizontal plot position (clamped to 0–1) to a frequency in Hz.</summary>
  public static double FractionToHz(double fraction)
  {
    double f = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0.0, 1.0);
    return PlotMinHz + (f * (PlotMaxHz - PlotMinHz));
  }

  /// <summary>
  /// The US FM channel (87.9 … 107.9 MHz, 200 kHz apart) nearest <paramref name="hz"/>, clamped to
  /// the band's first and last channels. A frequency exactly between two channels rounds up.
  /// </summary>
  public static long NearestChannelHz(double hz)
  {
    double steps = Math.Round((hz - FirstChannelHz) / ChannelSpacingHz, MidpointRounding.AwayFromZero);
    long maxSteps = (LastChannelHz - FirstChannelHz) / ChannelSpacingHz;
    long clamped = Math.Clamp((long)steps, 0, maxSteps);
    return FirstChannelHz + (clamped * ChannelSpacingHz);
  }

  /// <summary>
  /// The frequency a tap at <paramref name="tappedHz"/> tunes to: the strongest <em>peak</em> within
  /// ±<see cref="SnapWindowHz"/>, else the nearest channel. With no map at all it is the nearest channel.
  /// </summary>
  /// <remarks>
  /// A channel is a peak when its level is at least that of both neighbours in the map (a missing
  /// neighbour at the band edge counts as lower) <b>and</b> at least <see cref="PeakProminenceDb"/>
  /// above the map's median level. Two peaks in the window: the stronger wins; equal levels: the one
  /// nearer the tap. The neighbour rule also absorbs adjacent-channel aliasing (AUD-76 PR 1, M3):
  /// a shadow 200 kHz from a strong station is lower than the station, so it is never the peak.
  /// </remarks>
  public static long ResolveTapTarget(double tappedHz, IReadOnlyList<BandMapChannelDto>? channels)
  {
    long fallback = NearestChannelHz(tappedHz);
    if (channels == null || channels.Count == 0)
    {
      return fallback;
    }

    BandMapChannelDto[] sorted = channels.OrderBy(c => c.FrequencyHz).ToArray();
    double threshold = Median(sorted.Select(c => (double)c.LevelDbfs)) + PeakProminenceDb;

    BandMapChannelDto? best = null;
    for (int i = 0; i < sorted.Length; i++)
    {
      BandMapChannelDto c = sorted[i];
      if (Math.Abs(c.FrequencyHz - tappedHz) > SnapWindowHz + 0.5)
      {
        continue;
      }

      double left = i > 0 ? sorted[i - 1].LevelDbfs : double.NegativeInfinity;
      double right = i < sorted.Length - 1 ? sorted[i + 1].LevelDbfs : double.NegativeInfinity;
      bool isPeak = c.LevelDbfs >= left && c.LevelDbfs >= right && c.LevelDbfs >= threshold;
      if (!isPeak)
      {
        continue;
      }

      if (best == null
        || c.LevelDbfs > best.LevelDbfs
        || (c.LevelDbfs == best.LevelDbfs && Math.Abs(c.FrequencyHz - tappedHz) < Math.Abs(best.FrequencyHz - tappedHz)))
      {
        best = c;
      }
    }

    return best?.FrequencyHz ?? fallback;
  }

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
