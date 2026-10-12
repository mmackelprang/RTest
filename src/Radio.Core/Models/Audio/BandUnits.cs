using System.Globalization;

namespace Radio.Core.Models.Audio;

/// <summary>
/// Which unit a band's frequencies are shown in (AUD-102): kHz on AM and SW, MHz on every other band.
/// The one rule behind every frequency the UI shows (the panel and Radio page readouts, both
/// frequency-entry dialogs, the band list's ranges, the band map text and axis, the encoder cards and
/// preset names), so no two surfaces can disagree about it.
/// </summary>
/// <remarks>
/// SW is in kHz because its channels are 5 kHz apart (and 2.5 or 1 kHz steps are allowed): in MHz with
/// two decimals 5.900, 5.905 and 5.910 MHz printed as two values, and broadcast schedules list SW in
/// kHz.
/// </remarks>
public static class BandUnits
{
  /// <summary>True when <paramref name="band"/> (a band code: <c>AM</c>, <c>SW</c>, ...) is shown in kHz. Case-insensitive; null is false.</summary>
  public static bool IsKhz(string? band) =>
    string.Equals(band, "AM", StringComparison.OrdinalIgnoreCase)
    || string.Equals(band, "SW", StringComparison.OrdinalIgnoreCase);

  /// <summary>True when <paramref name="band"/> is shown in kHz: <see cref="RadioBand.AM"/> and <see cref="RadioBand.SW"/>.</summary>
  public static bool IsKhz(RadioBand band) => band is RadioBand.AM or RadioBand.SW;

  /// <summary>
  /// A frequency in kHz without a unit: whole kHz, plus one decimal only when the frequency needs it
  /// (<c>5905</c>, <c>5907.5</c> for a 2.5 kHz SW step).
  /// </summary>
  public static string FormatKhz(double hz) =>
    (hz / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture);

  /// <summary>
  /// A frequency with its unit for <paramref name="band"/>: <c>5905 kHz</c> and <c>1010 kHz</c> on
  /// SW and AM; on every other band <see cref="Frequency.ToDisplayString"/>'s MHz form
  /// (<c>101.5 MHz</c>), unchanged.
  /// </summary>
  public static string Format(RadioBand band, Frequency frequency) =>
    IsKhz(band) ? $"{FormatKhz(frequency.Hertz)} kHz" : frequency.ToDisplayString();
}
