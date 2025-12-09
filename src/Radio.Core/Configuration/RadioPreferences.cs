using Radio.Core.Models.Audio;

namespace Radio.Core.Configuration;

/// <summary>
/// User preferences for radio playback.
/// Persisted via IOptions pattern to 'audio-preferences' store.
/// </summary>
public class RadioPreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Radio";

  /// <summary>
  /// Gets or sets the last selected radio band.
  /// </summary>
  public string LastBand { get; set; } = RadioBand.FM.ToString();

  /// <summary>
  /// Gets or sets the last tuned frequency in MHz (for FM) or kHz (for AM/other bands).
  /// </summary>
  public double LastFrequency { get; set; } = 101.5;

  /// <summary>
  /// Gets or sets the last frequency step size in MHz (for FM) or kHz (for AM).
  /// </summary>
  public double LastFrequencyStep { get; set; } = 0.1;

  /// <summary>
  /// Gets or sets the last device volume (0-100).
  /// </summary>
  public int LastDeviceVolume { get; set; } = 50;

  /// <summary>
  /// Gets or sets the last equalizer mode.
  /// </summary>
  public string LastEqualizerMode { get; set; } = RadioEqualizerMode.Off.ToString();

  /// <summary>
  /// Gets or sets the last selected radio device type.
  /// </summary>
  public string LastDeviceType { get; set; } = "RTLSDRCore";
}
