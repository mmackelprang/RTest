using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore.Sweep;

/// <summary>
/// The band sweep plan for each band (AUD-91), and why a band has none.
/// </summary>
/// <remarks>
/// <para>
/// FM keeps the AUD-76 sweep: one tune per channel, centred on it, measured from 8 to 80 kHz
/// either side of the centre.
/// </para>
/// <para>
/// WB, AIR, VHF, AM and SW are narrowband: several channels are measured from one capture.
/// Consecutive channels are grouped so that every channel's measurement window lies within
/// <see cref="NarrowbandMaxOffsetHz"/> of the tuned centre, and the centre sits at least half a
/// channel spacing from every channel in the group, so no channel is measured at DC.
/// </para>
/// <para>
/// AM (AUD-103) is the whole band, 530 to 1710 kHz on the 10 kHz grid. SW is one broadcast segment
/// at a time (<see cref="ShortwaveSegments"/>), at 5 kHz: the one containing, or nearest to, the
/// radio's frequency. Like VHF's window it follows the radio (<see cref="FollowsRadio"/>).
/// </para>
/// <para>
/// Custom bands have no plan. Whether a device can sweep a band is a separate question, answered
/// by <see cref="UnavailableReason"/> from that device's range.
/// </para>
/// </remarks>
public static class BandSweepPlans
{
  /// <summary>FM per-channel window: bins up to this offset from the tuned centre are measured, in Hz.</summary>
  public const int FmHalfWindowHz = 80_000;

  /// <summary>FM DC exclusion, in Hz.</summary>
  public const int FmDcExcludeHz = 8_000;

  /// <summary>Narrowband per-channel window: bins within this distance of a channel centre are measured, in Hz.</summary>
  public const int NarrowbandHalfWindowHz = 4_000;

  /// <summary>Narrowband DC exclusion, in Hz.</summary>
  public const int NarrowbandDcExcludeHz = 1_000;

  /// <summary>
  /// Largest distance from the tuned centre that any narrowband channel's window may reach, in Hz.
  /// Below the ±120 kHz Nyquist limit of <see cref="DeviceSweepTuner.SweepSampleRate"/>.
  /// </summary>
  public const int NarrowbandMaxOffsetHz = 100_000;

  /// <summary>Half-width of the VHF window around the radio's frequency, in Hz.</summary>
  public const long VhfWindowHalfWidthHz = 1_000_000;

  /// <summary>Channel spacing of the WB and AIR plans, in Hz.</summary>
  public const long WbAirChannelSpacingHz = 25_000;

  /// <summary>Channel spacing of the VHF plan, in Hz.</summary>
  public const long VhfChannelSpacingHz = 12_500;

  /// <summary>Channel spacing of the AM plan (the US 10 kHz grid), in Hz.</summary>
  public const long AmChannelSpacingHz = 10_000;

  /// <summary>Channel spacing of the SW plan (the HF broadcast grid), in Hz.</summary>
  public const long SwChannelSpacingHz = 5_000;

  private static readonly (BandType Type, string Code)[] Codes =
  {
    (BandType.AM, "AM"),
    (BandType.FM, "FM"),
    (BandType.Shortwave, "SW"),
    (BandType.Aircraft, "AIR"),
    (BandType.Weather, "WB"),
    (BandType.VHF, "VHF"),
  };

  /// <summary>The band codes <see cref="TryParseBandCode"/> accepts, in <see cref="BandPresets.AllBands"/> order.</summary>
  public static IReadOnlyList<string> BandCodes { get; } = Codes.Select(c => c.Code).ToArray();

  /// <summary>The band code for <paramref name="band"/>: <c>AM</c>, <c>FM</c>, <c>SW</c>, <c>AIR</c>, <c>WB</c>, <c>VHF</c>.</summary>
  /// <exception cref="ArgumentException"><paramref name="band"/> is <see cref="BandType.Custom"/> or not a defined value.</exception>
  public static string BandCode(BandType band)
  {
    foreach ((BandType type, string code) in Codes)
    {
      if (type == band)
      {
        return code;
      }
    }
    throw new ArgumentException($"Band {band} has no band code.", nameof(band));
  }

  /// <summary>Parses a band code case-insensitively. Numeric strings are rejected.</summary>
  /// <param name="code">The code, e.g. <c>"wb"</c>.</param>
  /// <param name="band">The band, when the result is true.</param>
  public static bool TryParseBandCode(string? code, out BandType band)
  {
    if (code != null)
    {
      foreach ((BandType type, string known) in Codes)
      {
        if (string.Equals(known, code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
          band = type;
          return true;
        }
      }
    }
    band = default;
    return false;
  }

  /// <summary>
  /// The sweep plan for <paramref name="band"/>: one for every preset band, null for
  /// <see cref="BandType.Custom"/> and undefined values. It does not ask whether a device can sweep
  /// it; <see cref="UnavailableReason"/> does.
  /// </summary>
  /// <param name="band">The band.</param>
  /// <param name="currentFrequencyHz">
  /// The radio's frequency. Used only by the plans that follow the radio (<see cref="FollowsRadio"/>).
  /// VHF's plan is a ±1 MHz window around it: centred on the nearest 12.5 kHz grid point and moved,
  /// not shrunk, to stay inside the band; when null or outside the band, the window is the band's
  /// lowest 2 MHz (30 to 32 MHz). SW's plan is the broadcast segment containing it, or else the
  /// nearest one (<see cref="ShortwaveSegments.Nearest"/>); when null or outside the band, the
  /// segment nearest the band's lower edge (120 m).
  /// </param>
  public static BandSweepPlan? For(BandType band, long? currentFrequencyHz) => band switch
  {
    BandType.FM => CreateFm(),
    BandType.Weather => CreateWeather(),
    BandType.Aircraft => CreateAircraft(),
    BandType.VHF => CreateVhf(currentFrequencyHz),
    BandType.AM => CreateAm(),
    BandType.Shortwave => CreateShortwave(currentFrequencyHz),
    _ => null,
  };

  /// <summary>
  /// True for the bands whose plan is a window that follows the radio's frequency rather than the
  /// whole band: VHF and SW. A stored map of such a band records which window it swept.
  /// </summary>
  public static bool FollowsRadio(BandType band) => band is BandType.VHF or BandType.Shortwave;

  /// <summary>
  /// Why a device whose receivable range is <paramref name="deviceMinHz"/> to
  /// <paramref name="deviceMaxHz"/> cannot sweep <paramref name="band"/>, as a sentence for the
  /// owner, or null when it can: when the band's whole range is inside the device's.
  /// </summary>
  /// <remarks>
  /// The sentence comes from <see cref="BandReception.UnavailableReason"/>, the function the band
  /// list's receivable flag uses (AUD-102), ending "cannot be scanned". AUD-103: the caller passes
  /// the device's range (an RTL-SDR with HF direct sampling reaches 100 kHz, one without it 24 MHz).
  /// </remarks>
  /// <param name="band">The band. <see cref="BandType.Custom"/> always gets a sentence.</param>
  /// <param name="deviceMinHz">The device's lowest receivable frequency, in Hz.</param>
  /// <param name="deviceMaxHz">The device's highest receivable frequency, in Hz.</param>
  public static string? UnavailableReason(BandType band, long deviceMinHz, long deviceMaxHz) =>
    BandReception.UnavailableReason(band, deviceMinHz, deviceMaxHz, cannot: "scanned");

  /// <summary>
  /// Groups a uniform channel grid into narrowband tunes: groups of up to the largest count whose
  /// windows stay within <see cref="NarrowbandMaxOffsetHz"/> of the centre (rounded down to an
  /// even count). An even group is centred midway between its first and last channel; an odd group
  /// half a spacing above its middle channel. Either way the nearest channel is half a spacing from
  /// the centre.
  /// </summary>
  /// <param name="band">Band code.</param>
  /// <param name="firstChannelHz">First channel centre, in Hz.</param>
  /// <param name="channelCount">Number of channels.</param>
  /// <param name="spacingHz">Channel spacing, in Hz.</param>
  /// <param name="displayMinHz">Lower edge of the display axis, in Hz.</param>
  /// <param name="displayMaxHz">Upper edge of the display axis, in Hz.</param>
  public static BandSweepPlan CreateNarrowband(
    string band, long firstChannelHz, int channelCount, long spacingHz, long displayMinHz, long displayMaxHz)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingHz);

    int groupSize = NarrowbandGroupSize(spacingHz);
    List<SweepTune> tunes = new();
    for (int start = 0; start < channelCount; start += groupSize)
    {
      int count = Math.Min(groupSize, channelCount - start);
      long[] channels = new long[count];
      for (int i = 0; i < count; i++)
      {
        channels[i] = firstChannelHz + (start + i) * spacingHz;
      }

      long centre = count % 2 == 0
        ? (channels[0] + channels[^1]) / 2
        : channels[count / 2] + spacingHz / 2;
      tunes.Add(new SweepTune(centre, channels));
    }

    return new BandSweepPlan(
      band, tunes, spacingHz, displayMinHz, displayMaxHz,
      NarrowbandHalfWindowHz, NarrowbandDcExcludeHz, DeviceSweepTuner.SweepSampleRate);
  }

  /// <summary>
  /// Channels per narrowband tune for <paramref name="spacingHz"/>: the largest even n with
  /// <c>(n − 1) · spacing / 2 + <see cref="NarrowbandHalfWindowHz"/> ≤ <see cref="NarrowbandMaxOffsetHz"/></c>,
  /// or 1 when no even n fits. 8 at 25 kHz, 16 at 12.5 kHz.
  /// </summary>
  public static int NarrowbandGroupSize(long spacingHz)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingHz);
    long budget = 2L * (NarrowbandMaxOffsetHz - NarrowbandHalfWindowHz);
    long n = budget / spacingHz + 1;
    n -= n % 2;
    return n < 2 ? 1 : (int)n;
  }

  private static BandSweepPlan CreateFm()
  {
    SweepTune[] tunes = FmChannelPlan.Channels.Select(hz => new SweepTune(hz, new[] { hz })).ToArray();
    return new BandSweepPlan(
      "FM", tunes, FmChannelPlan.ChannelSpacingHz, FmChannelPlan.DisplayMinHz, FmChannelPlan.DisplayMaxHz,
      FmHalfWindowHz, FmDcExcludeHz, DeviceSweepTuner.SweepSampleRate);
  }

  // The seven NOAA Weather Radio channels, 162.400 to 162.550 MHz.
  private static BandSweepPlan CreateWeather()
  {
    RadioBand preset = BandPresets.Weather;
    int count = (int)((preset.MaxFrequencyHz - preset.MinFrequencyHz) / WbAirChannelSpacingHz) + 1;
    long first = preset.MinFrequencyHz;
    long last = first + (count - 1) * WbAirChannelSpacingHz;
    return CreateNarrowband("WB", first, count, WbAirChannelSpacingHz,
      first - WbAirChannelSpacingHz / 2, last + WbAirChannelSpacingHz / 2);
  }

  // US 25 kHz air-band channels, 108.000 to 137.000 MHz.
  private static BandSweepPlan CreateAircraft()
  {
    RadioBand preset = BandPresets.Aircraft;
    int count = (int)((preset.MaxFrequencyHz - preset.MinFrequencyHz) / WbAirChannelSpacingHz) + 1;
    return CreateNarrowband("AIR", preset.MinFrequencyHz, count, WbAirChannelSpacingHz,
      preset.MinFrequencyHz, preset.MaxFrequencyHz);
  }

  private static BandSweepPlan CreateVhf(long? currentFrequencyHz)
  {
    RadioBand preset = BandPresets.Vhf;
    long lowestCentre = preset.MinFrequencyHz + VhfWindowHalfWidthHz;
    long highestCentre = preset.MaxFrequencyHz - VhfWindowHalfWidthHz;

    long centre = currentFrequencyHz is long hz && preset.ContainsFrequency(hz)
      ? (long)Math.Round((double)hz / VhfChannelSpacingHz, MidpointRounding.AwayFromZero) * VhfChannelSpacingHz
      : lowestCentre;
    centre = Math.Clamp(centre, lowestCentre, highestCentre);

    long first = centre - VhfWindowHalfWidthHz;
    long last = centre + VhfWindowHalfWidthHz;
    int count = (int)((last - first) / VhfChannelSpacingHz) + 1;
    return CreateNarrowband("VHF", first, count, VhfChannelSpacingHz, first, last);
  }

  // AUD-103: 530 to 1710 kHz at 10 kHz, 119 channels (20 per tune at this spacing, so 6 tunes). The
  // axis is the band's own range, as AIR's is.
  private static BandSweepPlan CreateAm()
  {
    RadioBand preset = BandPresets.AmBroadcast;
    int count = (int)((preset.MaxFrequencyHz - preset.MinFrequencyHz) / AmChannelSpacingHz) + 1;
    return CreateNarrowband("AM", preset.MinFrequencyHz, count, AmChannelSpacingHz,
      preset.MinFrequencyHz, preset.MaxFrequencyHz);
  }

  // AUD-103: one broadcast segment at 5 kHz (38 channels per tune), chosen by the radio's frequency
  // as VHF's window is. The axis is the segment's edges, which are on the 5 kHz grid, so the first
  // and last channels are the edges themselves.
  private static BandSweepPlan CreateShortwave(long? currentFrequencyHz)
  {
    RadioBand preset = BandPresets.Shortwave;
    long reference = currentFrequencyHz is long hz && preset.ContainsFrequency(hz) ? hz : preset.MinFrequencyHz;
    ShortwaveSegment segment = ShortwaveSegments.Nearest(reference);
    int count = (int)((segment.MaxHz - segment.MinHz) / SwChannelSpacingHz) + 1;
    return CreateNarrowband("SW", segment.MinHz, count, SwChannelSpacingHz, segment.MinHz, segment.MaxHz);
  }
}
