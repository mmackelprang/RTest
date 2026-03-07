using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Manages audio source lifecycle: creation, switching, caching, and stopping.
/// </summary>
public interface IAudioSourceManager
{
  /// <summary>
  /// Gets the currently active primary audio source.
  /// </summary>
  IAudioSource? ActiveSource { get; }

  /// <summary>
  /// Switches to a new primary audio source.
  /// </summary>
  /// <param name="source">The audio source to switch to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  Task SwitchSourceAsync(IAudioSource source, CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets or creates an audio source of the specified type and optionally switches to it.
  /// </summary>
  /// <param name="sourceType">The type of audio source to get or create.</param>
  /// <param name="switchToSource">If true, switches to the source after getting/creating it.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The audio source, or null if the type is not supported.</returns>
  Task<IAudioSource?> GetOrCreateSourceAsync(
    AudioSourceType sourceType,
    bool switchToSource = true,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops all audio playback.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets a previously created and cached source by type, without switching to it.
  /// Returns null if the source type has never been created.
  /// </summary>
  IAudioSource? GetCachedSource(AudioSourceType sourceType);
}
