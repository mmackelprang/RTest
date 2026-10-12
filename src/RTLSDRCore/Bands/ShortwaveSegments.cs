namespace RTLSDRCore.Bands;

/// <summary>One HF broadcasting segment of the shortwave band (AUD-103).</summary>
/// <param name="Name">The segment's metre-band name, e.g. <c>31 m</c>.</param>
/// <param name="MinHz">Lower edge, in Hz. On the 5 kHz broadcast grid.</param>
/// <param name="MaxHz">Upper edge, in Hz. On the 5 kHz broadcast grid.</param>
public sealed record ShortwaveSegment(string Name, long MinHz, long MaxHz)
{
  /// <summary>True when <paramref name="frequencyHz"/> is from <see cref="MinHz"/> to <see cref="MaxHz"/> inclusive.</summary>
  public bool Contains(long frequencyHz) => frequencyHz >= MinHz && frequencyHz <= MaxHz;

  /// <summary>Distance from <paramref name="frequencyHz"/> to the segment, in Hz: 0 inside it, else to the nearer edge.</summary>
  public long DistanceTo(long frequencyHz) =>
    frequencyHz < MinHz ? MinHz - frequencyHz
    : frequencyHz > MaxHz ? frequencyHz - MaxHz
    : 0;
}

/// <summary>
/// The fixed table of HF broadcasting segments the shortwave band map sweeps one at a time (AUD-103):
/// the whole of 1.711–30 MHz at 5 kHz is about 5,700 channels, too many to sweep and to show.
/// </summary>
/// <remarks>
/// <para>
/// Source: the HF bands allocated to the broadcasting service in the ITU Radio Regulations,
/// Article 5 (Table of Frequency Allocations), with the later expansion bands (for example
/// 5900–5950, 9400–9500 and 15600–15800 kHz) and the 7 MHz realignment included, as the
/// metre-band edges are commonly tabulated. Typed from that tabulation, not checked line by line
/// against the Regulations' text, which is not in this repository. The tropical segments (120, 90
/// and 60 m) and 75 m are not broadcasting allocations in every ITU Region; the widest edges are
/// used, because sweeping a few extra channels costs nothing and a station outside the edges would
/// be missed.
/// </para>
/// <para>
/// Edges, in kHz: 120 m 2300–2495, 90 m 3200–3400, 75 m 3900–4000, 60 m 4750–5060,
/// 49 m 5900–6200, 41 m 7200–7450, 31 m 9400–9900, 25 m 11600–12100, 22 m 13570–13870,
/// 19 m 15100–15800, 16 m 17480–17900, 15 m 18900–19020, 13 m 21450–21850, 11 m 25670–26100.
/// </para>
/// </remarks>
public static class ShortwaveSegments
{
  /// <summary>Every segment, ascending by frequency. They do not overlap.</summary>
  public static IReadOnlyList<ShortwaveSegment> All { get; } = new ShortwaveSegment[]
  {
    new("120 m", 2_300_000, 2_495_000),
    new("90 m", 3_200_000, 3_400_000),
    new("75 m", 3_900_000, 4_000_000),
    new("60 m", 4_750_000, 5_060_000),
    new("49 m", 5_900_000, 6_200_000),
    new("41 m", 7_200_000, 7_450_000),
    new("31 m", 9_400_000, 9_900_000),
    new("25 m", 11_600_000, 12_100_000),
    new("22 m", 13_570_000, 13_870_000),
    new("19 m", 15_100_000, 15_800_000),
    new("16 m", 17_480_000, 17_900_000),
    new("15 m", 18_900_000, 19_020_000),
    new("13 m", 21_450_000, 21_850_000),
    new("11 m", 25_670_000, 26_100_000),
  };

  /// <summary>
  /// The segment containing <paramref name="frequencyHz"/>, or else the one whose nearer edge is
  /// closest to it. Between two segments at exactly equal distance, the lower one.
  /// </summary>
  /// <param name="frequencyHz">A frequency, in Hz. Any value; one outside the shortwave band gets the nearest segment too.</param>
  public static ShortwaveSegment Nearest(long frequencyHz)
  {
    ShortwaveSegment best = All[0];
    long bestDistance = best.DistanceTo(frequencyHz);
    for (int i = 1; i < All.Count; i++)
    {
      long distance = All[i].DistanceTo(frequencyHz);
      // Strictly less: an equal distance keeps the lower segment found first.
      if (distance < bestDistance)
      {
        best = All[i];
        bestDistance = distance;
      }
    }

    return best;
  }

  /// <summary>
  /// The segment whose edges are exactly <paramref name="minHz"/> and <paramref name="maxHz"/>, or
  /// null when none is. A shortwave band map records its segment's edges as its range, so this names
  /// the segment a stored map shows.
  /// </summary>
  public static ShortwaveSegment? WithEdges(long minHz, long maxHz) =>
    All.FirstOrDefault(s => s.MinHz == minHz && s.MaxHz == maxHz);
}
