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
  /// Rock music preset with enhanced bass and treble.
  /// </summary>
  Rock,

  /// <summary>
  /// Pop music preset with balanced frequency response.
  /// </summary>
  Pop,

  /// <summary>
  /// Jazz preset optimized for acoustic instruments.
  /// </summary>
  Jazz,

  /// <summary>
  /// Classical music preset with natural, wide frequency range.
  /// </summary>
  Classical,

  /// <summary>
  /// Speech preset optimized for voice clarity.
  /// </summary>
  Speech
}
