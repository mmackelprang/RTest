namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for audio output destinations.
/// Represents a target for sending mixed audio data (local speakers, Chromecast, etc.).
/// </summary>
public interface IAudioOutput : IAsyncDisposable
{
  /// <summary>
  /// Gets the unique identifier for this audio output.
  /// </summary>
  string Id { get; }

  /// <summary>
  /// Gets the human-readable name of the audio output.
  /// </summary>
  string Name { get; }

  /// <summary>
  /// Gets the type of audio output.
  /// </summary>
  AudioOutputType Type { get; }

  /// <summary>
  /// Gets the current state of the audio output.
  /// </summary>
  AudioOutputState State { get; }

  /// <summary>
  /// Gets or sets the volume level for this output (0.0 to 1.0).
  /// </summary>
  float Volume { get; set; }

  /// <summary>
  /// Gets or sets whether this output is muted.
  /// </summary>
  bool IsMuted { get; set; }

  /// <summary>
  /// Gets whether this output is enabled and receiving audio.
  /// </summary>
  bool IsEnabled { get; }

  /// <summary>
  /// Initializes the audio output.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task InitializeAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Starts sending audio to this output.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops sending audio to this output.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Event raised when the output state changes.
  /// </summary>
  event EventHandler<AudioOutputStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Types of audio outputs supported by the system.
/// </summary>
public enum AudioOutputType
{
  /// <summary>Local audio device output (ALSA, default speakers).</summary>
  Local,

  /// <summary>Google Chromecast output.</summary>
  GoogleCast,

  /// <summary>HTTP audio stream output.</summary>
  HttpStream
}

/// <summary>
/// States an audio output can be in.
/// </summary>
public enum AudioOutputState
{
  /// <summary>Output has been created but not initialized.</summary>
  Created,

  /// <summary>Output is initializing.</summary>
  Initializing,

  /// <summary>Output is ready but not streaming.</summary>
  Ready,

  /// <summary>Output is connecting to the target device.</summary>
  Connecting,

  /// <summary>Output is actively streaming audio.</summary>
  Streaming,

  /// <summary>Output is stopping.</summary>
  Stopping,

  /// <summary>Output has stopped.</summary>
  Stopped,

  /// <summary>Output encountered an error.</summary>
  Error,

  /// <summary>Output has been disposed.</summary>
  Disposed
}

/// <summary>
/// Event arguments for audio output state changes.
/// </summary>
public class AudioOutputStateChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the previous state of the audio output.
  /// </summary>
  public AudioOutputState PreviousState { get; init; }

  /// <summary>
  /// Gets the new state of the audio output.
  /// </summary>
  public AudioOutputState NewState { get; init; }

  /// <summary>
  /// Gets the ID of the audio output that changed state.
  /// </summary>
  public required string OutputId { get; init; }

  /// <summary>
  /// Gets an optional error message if the state is Error.
  /// </summary>
  public string? ErrorMessage { get; init; }
}
