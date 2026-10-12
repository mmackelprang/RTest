using RTLSDRCore.Enums;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Bands;

/// <summary>
/// Which bands are received by HF direct sampling (AUD-99): AM and Shortwave, and no other. Every
/// path that opens or tunes the device (the radio, the live band sweep, the idle band sweep) asks
/// this one class, so they cannot disagree.
/// </summary>
public static class HfBands
{
  /// <summary>True for <see cref="BandType.AM"/> and <see cref="BandType.Shortwave"/>; false for every other band, custom included.</summary>
  /// <param name="band">The band.</param>
  public static bool Requires(BandType band) => band is BandType.AM or BandType.Shortwave;

  /// <summary>
  /// True when the band of <paramref name="plan"/> (its <see cref="BandSweepPlan.Band"/> code)
  /// requires HF direct sampling. A code that is not a band code is treated as not HF.
  /// </summary>
  /// <param name="plan">A band sweep plan.</param>
  public static bool Requires(BandSweepPlan plan)
  {
    ArgumentNullException.ThrowIfNull(plan);
    return BandSweepPlans.TryParseBandCode(plan.Band, out BandType band) && Requires(band);
  }

  /// <summary>
  /// True when the preset band containing <paramref name="frequencyHz"/>
  /// (<see cref="BandPresets.FindBandForFrequency"/>) requires HF direct sampling; false when no
  /// preset band contains it.
  /// </summary>
  /// <param name="frequencyHz">A frequency, in Hz.</param>
  public static bool RequiresFrequency(long frequencyHz) =>
    BandPresets.FindBandForFrequency(frequencyHz) is { } band && Requires(band.Type);
}
