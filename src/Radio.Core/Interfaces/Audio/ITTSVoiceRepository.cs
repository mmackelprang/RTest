namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Repository for TTS voice cache and favorites persistence.
/// </summary>
public interface ITTSVoiceRepository
{
  /// <summary>
  /// Gets all cached voices for an engine, with favorite status populated.
  /// </summary>
  Task<IReadOnlyList<TTSVoiceInfo>> GetCachedVoicesAsync(
    TTSEngine engine, CancellationToken ct = default);

  /// <summary>
  /// Replaces all cached voices for an engine with a fresh set.
  /// </summary>
  Task ReplaceCachedVoicesAsync(
    TTSEngine engine,
    IReadOnlyList<TTSVoiceInfo> voices,
    CancellationToken ct = default);

  /// <summary>
  /// Adds a voice to the favorites list.
  /// </summary>
  Task AddFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken ct = default);

  /// <summary>
  /// Removes a voice from the favorites list.
  /// </summary>
  Task RemoveFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken ct = default);

  /// <summary>
  /// Gets all favorite voice IDs for an engine.
  /// </summary>
  Task<IReadOnlySet<string>> GetFavoriteIdsAsync(
    TTSEngine engine, CancellationToken ct = default);
}
