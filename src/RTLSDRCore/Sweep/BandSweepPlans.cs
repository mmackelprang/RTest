using System.Globalization;
using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
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
/// WB, AIR and VHF are narrowband: several channels are measured from one capture. Consecutive
/// channels are grouped so that every channel's measurement window lies within
/// <see cref="NarrowbandMaxOffsetHz"/> of the tuned centre, and the centre sits half a channel
/// spacing away from every channel in the group, so no channel is measured at DC.
/// </para>
/// <para>
/// AM, SW and custom bands have no plan. See <see cref="UnavailableReason"/>.
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
  /// The sweep plan for <paramref name="band"/>, or null when <see cref="UnavailableReason"/> is not null.
  /// </summary>
  /// <param name="band">The band.</param>
  /// <param name="currentFrequencyHz">
  /// The radio's frequency. Used only by VHF, whose plan is a ±1 MHz window around it: centred on
  /// the nearest 12.5 kHz grid point and moved, not shrunk, to stay inside the band. When null or
  /// outside the band, the window is the band's lowest 2 MHz (30 to 32 MHz).
  /// </param>
  public static BandSweepPlan? For(BandType band, long? currentFrequencyHz)
  {
    if (UnavailableReason(band) != null)
    {
      return null;
    }

    return band switch
    {
      BandType.FM => CreateFm(),
      BandType.Weather => CreateWeather(),
      BandType.Aircraft => CreateAircraft(),
      BandType.VHF => CreateVhf(currentFrequencyHz),
      _ => null,
    };
  }

  /// <summary>
  /// Why <paramref name="band"/> cannot be swept, as a sentence for the owner, or null when it can.
  /// A preset band can be swept only when its whole range is within
  /// <see cref="RtlSdrDevice.TunerMinFrequencyHz"/> to <see cref="RtlSdrDevice.TunerMaxFrequencyHz"/>.
  /// </summary>
  public static string? UnavailableReason(BandType band)
  {
    if (band == BandType.Custom || !Enum.IsDefined(band))
    {
      return "This band has no channel plan and cannot be scanned.";
    }

    RadioBand preset = BandPresets.GetBand(band);
    long min = preset.MinFrequencyHz;
    long max = preset.MaxFrequencyHz;
    string range = FormatRange(min, max);
    string limit = FormatMegahertz(RtlSdrDevice.TunerMinFrequencyHz);

    if (max < RtlSdrDevice.TunerMinFrequencyHz)
    {
      return $"The {preset.Name} band ({range}) is below this tuner's {limit} lower limit and cannot be scanned.";
    }

    if (min < RtlSdrDevice.TunerMinFrequencyHz)
    {
      // More than half of the band below the floor reads as "mostly".
      bool mostly = RtlSdrDevice.TunerMinFrequencyHz - min > max - RtlSdrDevice.TunerMinFrequencyHz;
      return $"The {preset.Name} band ({range}) is {(mostly ? "mostly" : "partly")} below this tuner's {limit} lower limit and cannot be scanned.";
    }

    if (max > RtlSdrDevice.TunerMaxFrequencyHz)
    {
      return $"The {preset.Name} band ({range}) extends above this tuner's {FormatMegahertz(RtlSdrDevice.TunerMaxFrequencyHz)} upper limit and cannot be scanned.";
    }

    return null;
  }

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

  private static string FormatRange(long minHz, long maxHz) =>
    maxHz < 3_000_000
      ? string.Format(CultureInfo.InvariantCulture, "{0:#,0}–{1:#,0} kHz", minHz / 1_000.0, maxHz / 1_000.0)
      : string.Format(CultureInfo.InvariantCulture, "{0:0.###}–{1:0.###} MHz", minHz / 1_000_000.0, maxHz / 1_000_000.0);

  private static string FormatMegahertz(long hz) =>
    string.Format(CultureInfo.InvariantCulture, "{0:0.###} MHz", hz / 1_000_000.0);
}
