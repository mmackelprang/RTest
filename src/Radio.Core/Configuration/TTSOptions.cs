namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the Text-to-Speech system.
/// Loaded from the 'TTS' configuration section.
/// </summary>
public class TTSOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "TTS";

  /// <summary>
  /// Gets or sets the default TTS engine to use ("Google" or "Azure").
  /// Empty means no engine is configured, and TTS generation fails with an explicit error
  /// rather than picking one.
  /// </summary>
  public string DefaultEngine { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the default voice identifier, in the selected engine's own format
  /// (for example "en-US-Standard-A" for Google). Empty means no voice is configured, and
  /// TTS generation fails with an explicit error rather than picking one.
  /// </summary>
  public string DefaultVoice { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the default pitch (0.5 to 2.0, 1.0 = normal).
  /// </summary>
  public float DefaultPitch { get; set; } = 1.0f;

  /// <summary>
  /// Gets or sets the default speaking speed (0.5 to 2.0, 1.0 = normal).
  /// </summary>
  public float DefaultSpeed { get; set; } = 1.0f;

  /// <summary>
  /// Gets or sets the timeout in seconds for TTS generation.
  /// </summary>
  public int GenerationTimeoutSeconds { get; set; } = 30;
}
