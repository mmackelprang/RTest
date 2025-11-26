namespace Radio.Core.Configuration;

/// <summary>
/// User preferences for TTS playback.
/// Persisted to the 'audio-preferences' store.
/// </summary>
public class TTSPreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "TTS";

  /// <summary>
  /// Gets or sets the last used TTS engine.
  /// </summary>
  public string LastEngine { get; set; } = "ESpeak";

  /// <summary>
  /// Gets or sets the last used voice.
  /// </summary>
  public string LastVoice { get; set; } = "en";

  /// <summary>
  /// Gets or sets the last used pitch value.
  /// </summary>
  public float LastPitch { get; set; } = 1.0f;

  /// <summary>
  /// Gets or sets the last used speed value.
  /// </summary>
  public float LastSpeed { get; set; } = 1.0f;
}
