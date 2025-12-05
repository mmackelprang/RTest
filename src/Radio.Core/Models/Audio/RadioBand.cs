namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents the radio frequency band.
/// </summary>
public enum RadioBand
{
  /// <summary>
  /// Amplitude Modulation band (typically 520-1710 kHz).
  /// </summary>
  AM,

  /// <summary>
  /// Frequency Modulation band (typically 87.5-108 MHz).
  /// </summary>
  FM,

  /// <summary>
  /// Weather Band for NOAA weather radio (typically 162.400-162.550 MHz).
  /// </summary>
  WB,

  /// <summary>
  /// Very High Frequency band (typically 30-300 MHz).
  /// </summary>
  VHF,

  /// <summary>
  /// Shortwave band (typically 1.6-30 MHz).
  /// </summary>
  SW,

  /// <summary>
  /// Air band (typically 118-137 MHz).
  /// </summary>
  AIR
}
