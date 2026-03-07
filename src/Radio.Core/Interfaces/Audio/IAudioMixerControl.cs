using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Controls audio mixer settings: master volume, mute, balance, and per-source gain offsets.
/// </summary>
public interface IAudioMixerControl
{
  /// <summary>
  /// Gets or sets the master volume level (0.0 to 1.0).
  /// </summary>
  float MasterVolume { get; set; }

  /// <summary>
  /// Gets or sets whether master audio is muted.
  /// </summary>
  bool IsMuted { get; set; }

  /// <summary>
  /// Gets or sets the stereo balance (-1.0 = full left, 0.0 = center, 1.0 = full right).
  /// </summary>
  float Balance { get; set; }

  /// <summary>
  /// Gets the gain offset for a specific source type (linear multiplier, default 1.0).
  /// </summary>
  float GetSourceGain(AudioSourceType sourceType);

  /// <summary>
  /// Sets the gain offset for a specific source type (linear multiplier 0.0-2.0).
  /// If the source type matches the active source, updates live playback immediately.
  /// </summary>
  void SetSourceGain(AudioSourceType sourceType, float gain);

  /// <summary>
  /// Gets all per-source gain offsets as a dictionary.
  /// </summary>
  Dictionary<string, float> GetAllSourceGains();
}
