using System.Globalization;
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>A labelled tick on the BAND view's frequency axis.</summary>
/// <param name="Hz">The tick's frequency, in Hz.</param>
/// <param name="Label">The label drawn under it: kHz on AM, MHz otherwise, without a unit.</param>
public sealed record BandAxisTick(double Hz, string Label);

/// <summary>
/// The frequency axis and channel grid of one band's map in the visualizer's BAND view (AUD-91): the
/// plotted range, the channel grid, the tap-to-tune snap, and the axis ticks. The general form of
/// <see cref="FmBandMath"/>'s FM arithmetic, which now delegates here through <see cref="Fm"/>.
/// </summary>
/// <param name="Band">Band code (<c>FM</c>, <c>AM</c>, <c>SW</c>, <c>AIR</c>, <c>WB</c>, <c>VHF</c>).</param>
/// <param name="DisplayMinHz">Left edge of the plot, in Hz.</param>
/// <param name="DisplayMaxHz">Right edge of the plot, in Hz.</param>
/// <param name="FirstChannelHz">First channel of the grid, in Hz.</param>
/// <param name="LastChannelHz">Last channel of the grid, in Hz.</param>
/// <param name="ChannelSpacingHz">Channel spacing, in Hz.</param>
public sealed record BandAxis(
  string Band, long DisplayMinHz, long DisplayMaxHz, long FirstChannelHz, long LastChannelHz, long ChannelSpacingHz)
{
  /// <summary>Most labelled ticks a non-FM axis gets. FM keeps its six fixed labels.</summary>
  public const int MaxTicks = 6;

  /// <summary>The FM axis exactly as AUD-76 shipped it: 87.5–108 MHz, channels 87.9–107.9 at 200 kHz.</summary>
  public static readonly BandAxis Fm = new(
    "FM", FmBandMath.PlotMinHz, FmBandMath.PlotMaxHz, FmBandMath.FirstChannelHz, FmBandMath.LastChannelHz,
    FmBandMath.ChannelSpacingHz);

  // FM's axis labels, unchanged from AUD-76 (VisualizerPanel's former BandAxisMhz).
  private static readonly int[] FmTickMhz = [88, 92, 96, 100, 104, 108];

  /// <summary>
  /// The axis a map response describes. FM always gets <see cref="Fm"/>, whatever the response says, so
  /// the FM view cannot drift from AUD-76. A response with no usable axis (an API from before AUD-91
  /// sends none, and only ever describes FM) also gets <see cref="Fm"/>.
  /// </summary>
  public static BandAxis FromMap(BandMapResponseDto? map)
  {
    if (map == null || IsFm(map.Band))
    {
      return Fm;
    }

    bool usable = map.DisplayMaxHz > map.DisplayMinHz
      && map.ChannelSpacingHz > 0
      && map.LastChannelHz >= map.FirstChannelHz;
    return usable
      ? new BandAxis(map.Band, map.DisplayMinHz, map.DisplayMaxHz, map.FirstChannelHz, map.LastChannelHz, map.ChannelSpacingHz)
      : Fm;
  }

  /// <summary>True when <paramref name="band"/> is FM (case-insensitive).</summary>
  public static bool IsFm(string? band) => string.Equals(band, "FM", StringComparison.OrdinalIgnoreCase);

  /// <summary>True on AM, whose axis is labelled in kHz.</summary>
  public bool IsKhz => string.Equals(Band, "AM", StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// How far either side of a tap a peak may be and still win the tap, in Hz: two channel spacings
  /// (±0.4 MHz on FM, ±50 kHz on WB and AIR, ±25 kHz on VHF).
  /// </summary>
  public long SnapWindowHz => 2 * ChannelSpacingHz;

  /// <summary>The plot's width in channel spacings (102.5 on FM). A drawn bar is half a spacing wide.</summary>
  public double ChannelsAcross => (double)(DisplayMaxHz - DisplayMinHz) / ChannelSpacingHz;

  /// <summary>Converts a frequency to its horizontal position on the plot, 0 (left edge) to 1 (right edge).</summary>
  public double HzToFraction(double hz) => (hz - DisplayMinHz) / (DisplayMaxHz - DisplayMinHz);

  /// <summary>Converts a horizontal plot position (clamped to 0–1) to a frequency in Hz.</summary>
  public double FractionToHz(double fraction)
  {
    double f = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0.0, 1.0);
    return DisplayMinHz + (f * (DisplayMaxHz - DisplayMinHz));
  }

  /// <summary>True when <paramref name="hz"/> is on the plot (edges included).</summary>
  public bool Contains(double hz) => hz >= DisplayMinHz && hz <= DisplayMaxHz;

  /// <summary>
  /// The grid channel nearest <paramref name="hz"/>, clamped to the first and last channels. A
  /// frequency exactly between two channels rounds up.
  /// </summary>
  public long NearestChannelHz(double hz)
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
  /// neighbour at the band edge counts as lower) <b>and</b> at least <see cref="FmBandMath.PeakProminenceDb"/>
  /// above the map's median level. Two peaks in the window: the stronger wins; equal levels: the one
  /// nearer the tap.
  /// </remarks>
  public long ResolveTapTarget(double tappedHz, IReadOnlyList<BandMapChannelDto>? channels)
  {
    long fallback = NearestChannelHz(tappedHz);
    if (channels == null || channels.Count == 0)
    {
      return fallback;
    }

    BandMapChannelDto[] sorted = channels.OrderBy(c => c.FrequencyHz).ToArray();
    double threshold = FmBandMath.Median(sorted.Select(c => (double)c.LevelDbfs)) + FmBandMath.PeakProminenceDb;

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
  /// The axis's labelled ticks, ascending; the gridlines are drawn at the same frequencies. FM: 88,
  /// 92, 96, 100, 104 and 108 MHz, as AUD-76 shipped. Any other band: the densest "nice" step
  /// (1, 2, 2.5 or 5 × 10^k, in kHz on AM and MHz otherwise) that puts at most <see cref="MaxTicks"/>
  /// ticks on the plot, labelled with just enough decimals for the step (0.05 MHz → <c>162.45</c>).
  /// </summary>
  public IReadOnlyList<BandAxisTick> Ticks()
  {
    if (IsFm(Band))
    {
      return FmTickMhz
        .Select(mhz => new BandAxisTick(mhz * 1_000_000.0, mhz.ToString(CultureInfo.InvariantCulture)))
        .ToArray();
    }

    // decimal, so that 162.45 / 0.05 is exactly 3249 and a tick on the plot edge is not lost to
    // binary rounding.
    decimal unit = IsKhz ? 1_000m : 1_000_000m;
    decimal min = DisplayMinHz / unit;
    decimal max = DisplayMaxHz / unit;
    decimal span = max - min;
    if (span <= 0)
    {
      return [];
    }

    int k = (int)Math.Floor(Math.Log10((double)span)) - 2;
    decimal[] multipliers = [1m, 2m, 2.5m, 5m];
    for (int attempt = 0; attempt < 12; attempt++, k++)
    {
      decimal power = PowerOfTen(k);
      foreach (decimal m in multipliers)
      {
        decimal step = m * power;
        decimal firstIndex = Math.Ceiling(min / step);
        decimal lastIndex = Math.Floor(max / step);
        decimal count = lastIndex - firstIndex + 1;
        if (count >= 1 && count <= MaxTicks)
        {
          int decimals = DecimalsOf(step);
          List<BandAxisTick> ticks = new();
          for (decimal i = firstIndex; i <= lastIndex; i++)
          {
            decimal value = i * step;
            ticks.Add(new BandAxisTick(
              (double)(value * unit), value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)));
          }

          return ticks;
        }
      }
    }

    return [];
  }

  private static decimal PowerOfTen(int k)
  {
    decimal result = 1m;
    for (int i = 0; i < Math.Abs(k); i++)
    {
      result = k > 0 ? result * 10m : result / 10m;
    }

    return result;
  }

  // Digits after the point in the step's shortest form: 5 → 0, 0.5 → 1, 0.05 → 2, 0.025 → 3.
  private static int DecimalsOf(decimal step)
  {
    int decimals = 0;
    while (decimal.Truncate(step) != step && decimals < 9)
    {
      step *= 10m;
      decimals++;
    }

    return decimals;
  }
}
