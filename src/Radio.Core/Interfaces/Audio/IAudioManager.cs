namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Facade interface for managing audio sources, playback, and mixer settings.
/// Extends <see cref="IAudioSourceManager"/> and <see cref="IAudioMixerControl"/>
/// for consumers that need the full API. Prefer the sub-interfaces when only
/// source management or mixer control is needed.
/// </summary>
public interface IAudioManager : IAudioSourceManager, IAudioMixerControl, IAsyncDisposable
{
  /// <summary>
  /// Gets the audio engine instance.
  /// </summary>
  IAudioEngine Engine { get; }

  /// <summary>
  /// Initializes the audio manager and underlying engine.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  Task InitializeAsync(CancellationToken cancellationToken = default);
}
