namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Core interface for managing audio sources and playback.
/// Coordinates the audio engine, mixer, and audio sources.
/// </summary>
public interface IAudioManager : IAsyncDisposable
{
  /// <summary>
  /// Gets the audio engine instance.
  /// </summary>
  IAudioEngine Engine { get; }

  /// <summary>
  /// Gets the currently active primary audio source.
  /// </summary>
  IAudioSource? ActiveSource { get; }

  /// <summary>
  /// Initializes the audio manager and underlying engine.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task InitializeAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Switches to a new primary audio source.
  /// </summary>
  /// <param name="source">The audio source to switch to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
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
  /// <returns>A task representing the async operation.</returns>
  Task StopAsync(CancellationToken cancellationToken = default);

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
