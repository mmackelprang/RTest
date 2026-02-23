namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Factory for creating audio sources by type.
/// Encapsulates all source-specific dependencies and configuration.
/// </summary>
public interface IAudioSourceFactory
{
  /// <summary>
  /// Creates an audio source for the specified type.
  /// </summary>
  /// <param name="sourceType">The type of audio source to create.</param>
  /// <returns>The created audio source.</returns>
  /// <exception cref="InvalidOperationException">Thrown when the source cannot be created (e.g., missing configuration).</exception>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when the source type is not supported.</exception>
  IAudioSource CreateSource(AudioSourceType sourceType);
}
