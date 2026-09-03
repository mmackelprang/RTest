namespace Radio.Core.Configuration;

/// <summary>
/// User preferences for TTS playback.
/// Persisted to the 'audio-preferences' store.
/// </summary>
/// <remarks>
/// <see cref="LastEngine"/> and <see cref="LastVoice"/> are written by
/// <c>PreferencesPersistenceService</c>; no code reads them back, and nothing parses
/// <see cref="LastEngine"/> into a <c>TTSEngine</c>. A value already stored by an earlier build
/// therefore binds harmlessly and cannot select an engine, which is why removing eSpeak needed no
/// config-store migration.
/// </remarks>
public class TTSPreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "TTS";

  /// <summary>
  /// Gets or sets the last used TTS engine.
  /// </summary>
  public string LastEngine { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the last used voice.
  /// </summary>
  public string LastVoice { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the last used pitch value.
  /// </summary>
  public float LastPitch { get; set; } = 1.0f;

  /// <summary>
  /// Gets or sets the last used speed value.
  /// </summary>
  public float LastSpeed { get; set; } = 1.0f;
}
