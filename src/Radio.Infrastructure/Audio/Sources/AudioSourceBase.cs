using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources;

/// <summary>
/// Base abstract class for all audio sources (primary and event).
/// Provides common state management, lifecycle methods, and event infrastructure.
/// </summary>
public abstract class AudioSourceBase : IAudioSource, IAsyncDisposable
{
  private readonly ILogger _logger;
  private AudioSourceState _state = AudioSourceState.Created;
  private float _volume = 1.0f;
  private bool _disposed;
  private string? _id;

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  protected AudioSourceBase(ILogger logger)
  {
    _logger = logger;
  }

  /// <inheritdoc/>
  public string Id => _id ??= $"{Type}-{Guid.NewGuid():N}";

  /// <inheritdoc/>
  public abstract string Name { get; }

  /// <inheritdoc/>
  public abstract AudioSourceType Type { get; }

  /// <inheritdoc/>
  public abstract AudioSourceCategory Category { get; }

  /// <inheritdoc/>
  public AudioSourceState State
  {
    get => _state;
    protected set
    {
      if (_state == value)
      {
        return;
      }
      var previousState = _state;
      _state = value;
      LogStateChange(previousState, value);
      OnStateChanged(previousState, value);
    }
  }

  /// <inheritdoc/>
  public float Volume
  {
    get => _volume;
    set
    {
      _volume = Math.Clamp(value, 0.0f, 1.0f);
      OnVolumeChanged(_volume);
    }
  }

  /// <inheritdoc/>
  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Raised when playback completes (end of content, error, or user stop).
  /// </summary>
  public event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  /// <inheritdoc/>
  public abstract object GetSoundComponent();

  /// <summary>
  /// Starts playback. Auto-initializes if source is in Created state.
  /// </summary>
  public virtual async Task PlayAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State == AudioSourceState.Created)
    {
      await InitializeAsync(cancellationToken);
    }

    // Check if initialization failed
    if (State == AudioSourceState.Error)
    {
      return;
    }

    await PlayCoreAsync(cancellationToken);
    State = AudioSourceState.Playing;
  }

  /// <summary>
  /// Stops playback and tears down this source's audio components.
  ///
  /// Teardown is deliberately NOT gated on <see cref="State"/> being
  /// Playing/Paused. A source can hold an attached, audible sound component
  /// while its state reads Ready/Stopped/Error: components are attached by
  /// async connect, late-acquire, and stall-recovery paths that are decoupled
  /// from the state machine, and external events can move the state
  /// independently of what is actually wired into the mixer. Gating teardown
  /// on the state flag meant those sources kept producing audio after a
  /// switch-away, because <c>StopCoreAsync</c> — the only code that detaches
  /// the component — was skipped.
  ///
  /// Only Created is skipped — nothing has been built yet, so there is genuinely
  /// nothing to detach. Every <c>StopCoreAsync</c> implementation is null-guarded
  /// and idempotent, so running it from any other state is a no-op when nothing
  /// is attached. (The Disposed arm is belt-and-braces: <see cref="ThrowIfDisposed"/>
  /// runs first, so a disposed source throws rather than reaching it.)
  /// </summary>
  public virtual async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State is AudioSourceState.Created or AudioSourceState.Disposed)
    {
      return;
    }

    await StopCoreAsync(cancellationToken);
    State = AudioSourceState.Stopped;
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    await DisposeAsyncCore();
    State = AudioSourceState.Disposed;
    _disposed = true;
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Initializes the audio source. Override in derived classes for source-specific setup.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public virtual Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    State = AudioSourceState.Initializing;
    return Task.CompletedTask;
  }

  /// <summary>
  /// Core implementation for starting playback.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected abstract Task PlayCoreAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Core implementation for stopping playback.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected abstract Task StopCoreAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Core implementation for async disposal.
  /// </summary>
  /// <returns>A task representing the async operation.</returns>
  protected virtual ValueTask DisposeAsyncCore()
  {
    return ValueTask.CompletedTask;
  }

  /// <summary>
  /// Called when the volume changes. Override to apply volume to the sound component.
  /// </summary>
  /// <param name="volume">The new volume level (0.0 to 1.0).</param>
  protected virtual void OnVolumeChanged(float volume)
  {
  }

  /// <summary>
  /// Logs a state change. Override to change log level or message format.
  /// Default logs at Debug level.
  /// </summary>
  /// <param name="previousState">The previous state.</param>
  /// <param name="newState">The new state.</param>
  protected virtual void LogStateChange(AudioSourceState previousState, AudioSourceState newState)
  {
    _logger.LogDebug("Audio source {Id} state changed from {PreviousState} to {NewState}",
      Id, previousState, newState);
  }

  /// <summary>
  /// Raises the <see cref="StateChanged"/> event.
  /// </summary>
  /// <param name="previousState">The previous state.</param>
  /// <param name="newState">The new state.</param>
  protected virtual void OnStateChanged(AudioSourceState previousState, AudioSourceState newState)
  {
    StateChanged?.Invoke(this, new AudioSourceStateChangedEventArgs
    {
      PreviousState = previousState,
      NewState = newState,
      SourceId = Id
    });
  }

  /// <summary>
  /// Raises the <see cref="PlaybackCompleted"/> event.
  /// </summary>
  /// <param name="reason">The reason for completion.</param>
  /// <param name="error">Any error that occurred, if applicable.</param>
  protected virtual void OnPlaybackCompleted(PlaybackCompletionReason reason, Exception? error = null)
  {
    PlaybackCompleted?.Invoke(this, new AudioSourceCompletedEventArgs
    {
      SourceId = Id,
      Reason = reason,
      Error = error
    });
  }

  /// <summary>
  /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
  /// </summary>
  protected void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <summary>
  /// Gets the logger for this instance.
  /// </summary>
  protected ILogger Logger => _logger;
}
