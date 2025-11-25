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
  /// Gets the audio device manager.
  /// </summary>
  IAudioDeviceManager DeviceManager { get; }

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
}
