namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents the equalizer preset mode for the radio device.
/// </summary>
public enum RadioEqualizerMode
{
  /// <summary>
  /// No equalization applied - flat response.
  /// </summary>
  Off,

  /// <summary>
  /// Pop music preset with balanced frequency response.
  /// </summary>
  Pop,

  /// <summary>
  /// Rock music preset with enhanced bass and treble.
  /// </summary>
  Rock,

  /// <summary>
  /// Country music preset optimized for vocals and acoustic instruments.
  /// </summary>
  Country,

  /// <summary>
  /// Classical music preset with natural, wide frequency range.
  /// </summary>
  Classical,

  /// <summary>
  /// Jazz music preset.
  /// </summary>
  Jazz,

  /// <summary>
  /// Normal equalization preset.
  /// </summary>
  Normal
}
