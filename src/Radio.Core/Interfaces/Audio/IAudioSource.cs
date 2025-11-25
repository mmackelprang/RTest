namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Base interface for all audio sources in the Radio Console system.
/// Audio sources provide audio data to be played through the mixer.
/// </summary>
public interface IAudioSource : IAsyncDisposable
{
  /// <summary>
  /// Gets the unique identifier for this audio source instance.
  /// </summary>
  string Id { get; }

  /// <summary>
  /// Gets the human-readable name of the audio source.
  /// </summary>
  string Name { get; }

  /// <summary>
  /// Gets the type of audio source.
  /// </summary>
  AudioSourceType Type { get; }

  /// <summary>
  /// Gets whether this is a primary or event audio source.
  /// </summary>
  AudioSourceCategory Category { get; }

  /// <summary>
  /// Gets the current playback state of the source.
  /// </summary>
  AudioSourceState State { get; }

  /// <summary>
  /// Gets or sets the volume level for this source (0.0 to 1.0).
  /// </summary>
  float Volume { get; set; }

  /// <summary>
  /// Gets the underlying SoundFlow component for mixer connection.
  /// </summary>
  /// <returns>The SoundFlow audio component.</returns>
  object GetSoundComponent();

  /// <summary>
  /// Event raised when the audio source state changes.
  /// </summary>
  event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Types of audio sources supported by the system.
/// </summary>
public enum AudioSourceType
{
  /// <summary>Spotify streaming audio.</summary>
  Spotify,

  /// <summary>Radio USB input (Raddy RF320).</summary>
  Radio,

  /// <summary>Vinyl turntable USB input.</summary>
  Vinyl,

  /// <summary>Local file player.</summary>
  FilePlayer,

  /// <summary>Generic USB audio input.</summary>
  GenericUSB,

  /// <summary>Text-to-speech event audio.</summary>
  TTS,

  /// <summary>Audio file event (notifications, doorbell, etc.).</summary>
  AudioFileEvent
}

/// <summary>
/// Categories of audio sources.
/// </summary>
public enum AudioSourceCategory
{
  /// <summary>Primary audio sources - only one can be active at a time.</summary>
  Primary,

  /// <summary>Event audio sources - ephemeral, can interrupt primary sources.</summary>
  Event
}

/// <summary>
/// States an audio source can be in.
/// </summary>
public enum AudioSourceState
{
  /// <summary>Source has been created but not initialized.</summary>
  Created,

  /// <summary>Source is initializing.</summary>
  Initializing,

  /// <summary>Source is ready but not playing.</summary>
  Ready,

  /// <summary>Source is actively playing audio.</summary>
  Playing,

  /// <summary>Source is paused.</summary>
  Paused,

  /// <summary>Source has stopped.</summary>
  Stopped,

  /// <summary>Source encountered an error.</summary>
  Error,

  /// <summary>Source has been disposed.</summary>
  Disposed
}

/// <summary>
/// Event arguments for audio source state changes.
/// </summary>
public class AudioSourceStateChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the previous state of the audio source.
  /// </summary>
  public AudioSourceState PreviousState { get; init; }

  /// <summary>
  /// Gets the new state of the audio source.
  /// </summary>
  public AudioSourceState NewState { get; init; }

  /// <summary>
  /// Gets the ID of the audio source that changed state.
  /// </summary>
  public required string SourceId { get; init; }
}
