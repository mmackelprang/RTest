using System.Globalization;

namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents a saved radio station preset.
/// </summary>
public sealed record RadioPreset
{
  /// <summary>
  /// Unique identifier for the preset.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Display name for the preset. Defaults to "{Band} - {Frequency}".
  /// </summary>
  public required string Name { get; init; }

  /// <summary>
  /// The radio band (AM, FM, WB, VHF, SW).
  /// </summary>
  public required RadioBand Band { get; init; }

  /// <summary>
  /// The frequency of the station, in hertz.
  /// </summary>
  public required double Frequency { get; init; }

  /// <summary>
  /// When this preset was created.
  /// </summary>
  public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

  /// <summary>
  /// When this preset was last modified.
  /// </summary>
  public DateTimeOffset LastModifiedAt { get; init; } = DateTimeOffset.UtcNow;

  /// <summary>
  /// Generates a default name for a preset based on band and frequency, in the band's display unit
  /// without the unit (AUD-102): kHz on AM and SW (<see cref="BandUnits.IsKhz(RadioBand)"/>), MHz
  /// otherwise.
  /// </summary>
  /// <param name="band">The radio band.</param>
  /// <param name="frequencyHz">The frequency in hertz, as <see cref="Frequency"/> stores it.</param>
  /// <returns>
  /// <c>AM - 1010</c>, <c>SW - 5905</c> (<c>SW - 5907.5</c> off the 5 kHz grid), <c>FM - 101.5</c>,
  /// and MHz with three or more decimals as needed on WB, AIR and VHF (<c>WB - 162.475</c>,
  /// <c>VHF - 146.5125</c>).
  /// </returns>
  public static string GetDefaultName(RadioBand band, double frequencyHz)
  {
    if (BandUnits.IsKhz(band))
    {
      return $"{band} - {BandUnits.FormatKhz(frequencyHz)}";
    }

    double mhz = frequencyHz / 1_000_000.0;
    string value = band == RadioBand.FM
      ? mhz.ToString("0.0", CultureInfo.InvariantCulture)
      : mhz.ToString("0.000###", CultureInfo.InvariantCulture);
    return $"{band} - {value}";
  }

  /// <summary>
  /// Creates a unique key for comparing presets (band + frequency combination).
  /// </summary>
  public string GetUniqueKey() => $"{Band}_{Frequency:F3}";
}
