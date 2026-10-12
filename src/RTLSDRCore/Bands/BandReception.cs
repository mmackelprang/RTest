using System.Globalization;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore.Bands;

/// <summary>
/// Whether a device can receive a preset band, and why not when it cannot (AUD-102). One function
/// for every surface that asks: the band list's <c>IsReceivable</c> / <c>UnavailableReason</c>
/// (<c>RadioBandService</c>) and the band map's reason (<see cref="Sweep.BandSweepPlans.UnavailableReason"/>,
/// which <c>BandMapService</c> calls with the same device's range since AUD-103), so the panel and
/// the BAND view cannot word the same limit differently.
/// </summary>
public static class BandReception
{
  /// <summary>
  /// Why a device whose receivable range is <paramref name="deviceMinHz"/> to
  /// <paramref name="deviceMaxHz"/> cannot receive <paramref name="band"/>, as a sentence for the
  /// owner, or null when the band's whole range is inside the device's. A band that is only partly
  /// inside the range is not receivable.
  /// </summary>
  /// <param name="band">The band. <see cref="BandType.Custom"/> and undefined values get a fixed sentence.</param>
  /// <param name="deviceMinHz">
  /// The device's lowest receivable frequency, in Hz. For an RTL-SDR this is
  /// <see cref="DeviceInfo.MinFrequencyHz"/>: 100 kHz with HF direct sampling, 24 MHz without (AUD-99).
  /// </param>
  /// <param name="deviceMaxHz">The device's highest receivable frequency, in Hz.</param>
  /// <param name="cannot">
  /// The end of the sentence, after "cannot be": <c>received</c> for the band list, <c>scanned</c>
  /// for the band map.
  /// </param>
  /// <returns>
  /// For example <c>The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit and
  /// cannot be received.</c>, or null.
  /// </returns>
  public static string? UnavailableReason(BandType band, long deviceMinHz, long deviceMaxHz, string cannot = "received")
  {
    if (band == BandType.Custom || !Enum.IsDefined(band))
    {
      return $"This band has no channel plan and cannot be {cannot}.";
    }

    RadioBand preset = BandPresets.GetBand(band);
    long min = preset.MinFrequencyHz;
    long max = preset.MaxFrequencyHz;
    string range = FormatRange(min, max);
    string limit = FormatLimit(deviceMinHz);

    if (max < deviceMinHz)
    {
      return $"The {preset.Name} band ({range}) is below this tuner's {limit} lower limit and cannot be {cannot}.";
    }

    if (min < deviceMinHz)
    {
      // More than half of the band below the floor reads as "mostly".
      bool mostly = deviceMinHz - min > max - deviceMinHz;
      return $"The {preset.Name} band ({range}) is {(mostly ? "mostly" : "partly")} below this tuner's {limit} lower limit and cannot be {cannot}.";
    }

    if (max > deviceMaxHz)
    {
      return $"The {preset.Name} band ({range}) extends above this tuner's {FormatLimit(deviceMaxHz)} upper limit and cannot be {cannot}.";
    }

    return null;
  }

  // Kept as the band map has always printed it: kHz with a thousands separator below 3 MHz,
  // otherwise MHz with up to three decimals.
  private static string FormatRange(long minHz, long maxHz) =>
    maxHz < 3_000_000
      ? string.Format(CultureInfo.InvariantCulture, "{0:#,0}–{1:#,0} kHz", minHz / 1_000.0, maxHz / 1_000.0)
      : string.Format(CultureInfo.InvariantCulture, "{0:0.###}–{1:0.###} MHz", minHz / 1_000_000.0, maxHz / 1_000_000.0);

  private static string FormatLimit(long hz) =>
    hz < 1_000_000
      ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} kHz", hz / 1_000.0)
      : string.Format(CultureInfo.InvariantCulture, "{0:0.###} MHz", hz / 1_000_000.0);
}
