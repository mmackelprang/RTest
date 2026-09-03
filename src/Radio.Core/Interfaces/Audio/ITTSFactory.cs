namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Factory for creating Text-to-Speech audio from text.
/// Supports the cloud TTS engines (Google, Azure).
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
  /// Gets the available voices for a specific TTS engine from the local cache.
  /// Returns an empty list if no voices have been cached yet.
  /// </summary>
  /// <param name="engine">The TTS engine to query.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of available voices for the engine, sorted by favorites then price tier.</returns>
  Task<IReadOnlyList<TTSVoiceInfo>> GetVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Fetches voices from the cloud API and stores them in the local cache.
  /// </summary>
  /// <param name="engine">The TTS engine to refresh voices for.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The number of voices discovered and cached.</returns>
  Task<int> RefreshVoicesAsync(
    TTSEngine engine,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Marks a voice as a favorite.
  /// </summary>
  Task SetVoiceFavoriteAsync(
    TTSEngine engine,
    string voiceId,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Removes a voice from favorites.
  /// </summary>
  Task RemoveVoiceFavoriteAsync(
    TTSEngine engine,
    string voiceId,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// TTS engines supported by the system.
/// </summary>
/// <remarks>
/// The members are numbered explicitly from 1 so zero — and therefore <c>default(TTSEngine)</c> —
/// is not a defined member: an engine that was never set is a value <c>Enum.IsDefined</c> returns
/// <see langword="false"/> for, rather than silently being a real engine. Do not renumber from 0.
/// </remarks>
public enum TTSEngine
{
  /// <summary>Google Cloud Text-to-Speech API.</summary>
  Google = 1,

  /// <summary>Azure Cognitive Services Speech.</summary>
  Azure = 2
}

/// <summary>
/// Parameters for TTS audio generation.
/// </summary>
public record TTSParameters
{
  /// <summary>
  /// Gets or sets the TTS engine to use. <see langword="null"/> leaves the engine unspecified;
  /// <c>CreateAsync</c> then falls back to the configured <c>TTS:DefaultEngine</c>.
  /// </summary>
  public TTSEngine? Engine { get; init; }

  /// <summary>
  /// Gets or sets the voice identifier. <see langword="null"/> leaves the voice unspecified;
  /// <c>CreateAsync</c> then falls back to the configured <c>TTS:DefaultVoice</c>.
  /// </summary>
  public string? Voice { get; init; }

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

  /// <summary>
  /// Gets whether this voice is a user favorite.
  /// </summary>
  public bool IsFavorite { get; init; }

  /// <summary>
  /// Gets the pricing tier (e.g., "Standard", "WaveNet", "Neural2", "Studio", "Neural").
  /// </summary>
  public string PriceTier { get; init; } = "Standard";
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
