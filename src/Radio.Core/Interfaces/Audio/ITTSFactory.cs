namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Factory for creating Text-to-Speech audio from text.
/// Supports multiple TTS engines including offline (eSpeak) and cloud (Google, Azure).
/// </summary>
public interface ITTSFactory
{
  /// <summary>
  /// Gets the list of available TTS engines.
  /// </summary>
  IReadOnlyList<TTSEngineInfo> AvailableEngines { get; }

  /// <summary>
  /// Creates a TTS event audio source from the specified text.
  /// </summary>
  /// <param name="text">The text to convert to speech.</param>
  /// <param name="parameters">Optional TTS parameters (engine, voice, speed, pitch).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An event audio source containing the synthesized speech.</returns>
  Task<IEventAudioSource> CreateAsync(
    string text,
    TTSParameters? parameters = null,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the available voices for a specific TTS engine.
  /// </summary>
  /// <param name="engine">The TTS engine to query.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of available voices for the engine.</returns>
  Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// TTS engines supported by the system.
/// </summary>
public enum TTSEngine
{
  /// <summary>eSpeak-ng - Offline, open source speech synthesizer.</summary>
  ESpeak,

  /// <summary>Google Cloud Text-to-Speech API.</summary>
  Google,

  /// <summary>Azure Cognitive Services Speech.</summary>
  Azure
}

/// <summary>
/// Parameters for TTS audio generation.
/// </summary>
public record TTSParameters
{
  /// <summary>
  /// Gets or sets the TTS engine to use.
  /// </summary>
  public TTSEngine Engine { get; init; } = TTSEngine.ESpeak;

  /// <summary>
  /// Gets or sets the voice identifier.
  /// </summary>
  public string Voice { get; init; } = "en";

  /// <summary>
  /// Gets or sets the speaking rate (0.5 to 2.0, 1.0 = normal).
  /// </summary>
  public float Speed { get; init; } = 1.0f;

  /// <summary>
  /// Gets or sets the pitch adjustment (0.5 to 2.0, 1.0 = normal).
  /// </summary>
  public float Pitch { get; init; } = 1.0f;
}

/// <summary>
/// Information about a TTS engine.
/// </summary>
public record TTSEngineInfo
{
  /// <summary>
  /// Gets the TTS engine type.
  /// </summary>
  public required TTSEngine Engine { get; init; }

  /// <summary>
  /// Gets the human-readable name of the engine.
  /// </summary>
  public required string Name { get; init; }

  /// <summary>
  /// Gets whether the engine is currently available (installed/configured).
  /// </summary>
  public required bool IsAvailable { get; init; }

  /// <summary>
  /// Gets whether the engine requires an API key.
  /// </summary>
  public bool RequiresApiKey { get; init; }

  /// <summary>
  /// Gets whether the engine works offline.
  /// </summary>
  public bool IsOffline { get; init; }
}

/// <summary>
/// Information about a TTS voice.
/// </summary>
public record TTSVoiceInfo
{
  /// <summary>
  /// Gets the unique voice identifier.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets the human-readable voice name.
  /// </summary>
  public required string Name { get; init; }

  /// <summary>
  /// Gets the language code (e.g., "en-US").
  /// </summary>
  public required string Language { get; init; }

  /// <summary>
  /// Gets the voice gender.
  /// </summary>
  public required TTSVoiceGender Gender { get; init; }
}

/// <summary>
/// Voice gender options.
/// </summary>
public enum TTSVoiceGender
{
  /// <summary>Male voice.</summary>
  Male,

  /// <summary>Female voice.</summary>
  Female,

  /// <summary>Neutral or unspecified gender.</summary>
  Neutral
}
