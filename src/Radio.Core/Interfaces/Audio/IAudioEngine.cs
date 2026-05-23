namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Core audio engine interface wrapping SoundFlow functionality.
/// Manages the audio graph, device connection, and real-time audio processing.
/// </summary>
public interface IAudioEngine : IAsyncDisposable
{
  /// <summary>
  /// Initializes the audio engine with configured settings.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task InitializeAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Starts the audio processing.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops the audio processing.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the master mixer node for connecting audio sources.
  /// </summary>
  /// <returns>The master mixer interface.</returns>
  IMasterMixer GetMasterMixer();

  /// <summary>
  /// Gets a stream of the mixed audio output for streaming/recording.
  /// This stream is used by the Local Stream Server for Chromecast integration.
  /// </summary>
  /// <returns>A stream containing real mixed audio data.</returns>
  Stream GetMixedOutputStream();

  /// <summary>
  /// Creates an independent stream reader with its own read cursor.
  /// Multiple readers can consume the same audio data without interfering.
  /// </summary>
  /// <param name="readerId">A unique identifier for this reader.</param>
  /// <param name="lagSeconds">Optional lag in seconds behind the write position.
  /// When specified, overrides the default <c>StreamReaderLagSeconds</c> from options.
  /// Use larger values for Cast clients that need an initial burst of pre-buffered data.</param>
  /// <returns>A stream with an independent read position.</returns>
  Stream CreateStreamReader(string readerId, double? lagSeconds = null);

  /// <summary>
  /// Gets the current state of the audio engine.
  /// </summary>
  AudioEngineState State { get; }

  /// <summary>
  /// Gets whether the audio engine is initialized and ready for use.
  /// </summary>
  bool IsReady { get; }

  /// <summary>
  /// Event raised when the engine state changes.
  /// </summary>
  event EventHandler<AudioEngineStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Event raised when an audio device is added or removed (hot-plug).
  /// </summary>
  event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

  /// <summary>
  /// Gets whether the local playback device is muted (e.g. when casting to an external device).
  /// </summary>
  bool IsLocalOutputMuted { get; }

  /// <summary>
  /// Mutes or unmutes the local playback device independently of the master volume.
  /// Used to silence local speakers when audio is being streamed to an external device (e.g. Cast).
  /// The audio pipeline (including taps for HTTP streaming) continues to process audio normally.
  /// </summary>
  /// <param name="muted">True to mute local output, false to restore volume.</param>
  void SetLocalOutputMuted(bool muted);

  /// <summary>
  /// Gets the id of the currently active output ("google-cast", "http-stream",
  /// or a local device id). Null until the first activation completes.
  /// </summary>
  string? ActiveOutputId { get; }

  /// <summary>
  /// Atomically switches the active audio output. Exactly one output is active
  /// at any time: either a local playback device (identified by its MiniAudio
  /// device id), or one of the virtual outputs "google-cast" / "http-stream".
  /// Activates the requested output, deactivates the others, sets local-output
  /// mute appropriately, and persists the choice to AudioPreferences:CurrentOutput.
  /// </summary>
  /// <param name="outputId">"google-cast", "http-stream", or a local device id.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <remarks>
  /// Callers must not re-enter this method from within an output's StartAsync/StopAsync
  /// callbacks — the gate holds a SemaphoreSlim and will deadlock under reentrancy.
  /// </remarks>
  Task SetActiveOutputAsync(string outputId, CancellationToken cancellationToken = default);
}

/// <summary>
/// States the audio engine can be in.
/// </summary>
public enum AudioEngineState
{
  /// <summary>Engine has not been initialized.</summary>
  Uninitialized,

  /// <summary>Engine is initializing.</summary>
  Initializing,

  /// <summary>Engine is ready but not running.</summary>
  Ready,

  /// <summary>Engine is actively processing audio.</summary>
  Running,

  /// <summary>Engine is stopping.</summary>
  Stopping,

  /// <summary>Engine encountered an error.</summary>
  Error,

  /// <summary>Engine has been disposed.</summary>
  Disposed
}

/// <summary>
/// Event arguments for audio engine state changes.
/// </summary>
public class AudioEngineStateChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the previous state of the engine.
  /// </summary>
  public AudioEngineState PreviousState { get; init; }

  /// <summary>
  /// Gets the new state of the engine.
  /// </summary>
  public AudioEngineState NewState { get; init; }
}

/// <summary>
/// Event arguments for audio device changes.
/// </summary>
public class AudioDeviceChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the type of change that occurred.
  /// </summary>
  public DeviceChangeType ChangeType { get; init; }

  /// <summary>
  /// Gets the device that was added or removed.
  /// </summary>
  public AudioDeviceInfo? Device { get; init; }
}

/// <summary>
/// Types of device changes.
/// </summary>
public enum DeviceChangeType
{
  /// <summary>A new device was connected.</summary>
  Added,

  /// <summary>A device was disconnected.</summary>
  Removed,

  /// <summary>The default device changed.</summary>
  DefaultChanged
}

/// <summary>
/// Master audio mixer that combines all audio sources.
/// </summary>
public interface IMasterMixer
{
  /// <summary>
  /// Gets or sets the master volume level (0.0 to 1.0).
  /// </summary>
  float MasterVolume { get; set; }

  /// <summary>
  /// Gets or sets the stereo balance (-1.0 left to 1.0 right).
  /// </summary>
  float Balance { get; set; }

  /// <summary>
  /// Gets or sets whether the master output is muted.
  /// </summary>
  bool IsMuted { get; set; }

  /// <summary>
  /// Adds an audio source to the mixer.
  /// </summary>
  /// <param name="source">The source to add.</param>
  void AddSource(IAudioSource source);

  /// <summary>
  /// Removes an audio source from the mixer.
  /// </summary>
  /// <param name="source">The source to remove.</param>
  void RemoveSource(IAudioSource source);

  /// <summary>
  /// Gets all currently active audio sources.
  /// </summary>
  /// <returns>A read-only list of active sources.</returns>
  IReadOnlyList<IAudioSource> GetActiveSources();
}
