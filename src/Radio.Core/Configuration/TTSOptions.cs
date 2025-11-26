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
  /// Gets or sets the default TTS engine to use.
  /// </summary>
  public string DefaultEngine { get; set; } = "ESpeak";

  /// <summary>
  /// Gets or sets the default voice identifier.
  /// </summary>
  public string DefaultVoice { get; set; } = "en";

  /// <summary>
  /// Gets or sets the default pitch (0.5 to 2.0, 1.0 = normal).
  /// </summary>
  public float DefaultPitch { get; set; } = 1.0f;

  /// <summary>
  /// Gets or sets the default speaking speed (0.5 to 2.0, 1.0 = normal).
  /// </summary>
  public float DefaultSpeed { get; set; } = 1.0f;

  /// <summary>
  /// Gets or sets the path to the espeak-ng executable (for eSpeak engine).
  /// </summary>
  public string ESpeakPath { get; set; } = "espeak-ng";

  /// <summary>
  /// Gets or sets the timeout in seconds for TTS generation.
  /// </summary>
  public int GenerationTimeoutSeconds { get; set; } = 30;
}
