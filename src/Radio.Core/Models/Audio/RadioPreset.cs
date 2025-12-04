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
  /// The frequency of the station.
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
  /// Generates a default name for a preset based on band and frequency.
  /// </summary>
  /// <param name="band">The radio band.</param>
  /// <param name="frequency">The frequency.</param>
  /// <returns>A formatted name like "FM - 101.5" or "AM - 1010".</returns>
  public static string GetDefaultName(RadioBand band, double frequency)
  {
    return band switch
    {
      RadioBand.AM => $"AM - {frequency:F0}",
      RadioBand.FM => $"FM - {frequency:F1}",
      RadioBand.WB => $"WB - {frequency:F2}",
      RadioBand.VHF => $"VHF - {frequency:F2}",
      RadioBand.SW => $"SW - {frequency:F2}",
      _ => $"{band} - {frequency}"
    };
  }

  /// <summary>
  /// Creates a unique key for comparing presets (band + frequency combination).
  /// </summary>
  public string GetUniqueKey() => $"{Band}_{Frequency:F3}";
}
