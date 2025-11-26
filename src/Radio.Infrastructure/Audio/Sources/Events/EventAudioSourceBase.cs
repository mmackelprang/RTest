using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Base abstract class for event audio sources.
/// Provides common functionality for state management and events.
/// </summary>
public abstract class EventAudioSourceBase : IEventAudioSource
{
  private readonly ILogger _logger;
  private AudioSourceState _state = AudioSourceState.Created;
  private float _volume = 1.0f;
  private bool _disposed;
  private string? _id;

  /// <summary>
  /// Initializes a new instance of the <see cref="EventAudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  protected EventAudioSourceBase(ILogger logger)
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
  public AudioSourceCategory Category => AudioSourceCategory.Event;

  /// <inheritdoc/>
  public AudioSourceState State
  {
    get => _state;
    protected set
    {
      if (_state == value) return;
      var previousState = _state;
      _state = value;
      _logger.LogDebug("Event audio source {Id} state changed from {PreviousState} to {NewState}",
        Id, previousState, value);
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
  public abstract TimeSpan Duration { get; }

  /// <inheritdoc/>
  public event EventHandler<AudioSourceStateChangedEventArgs>? StateChanged;

  /// <inheritdoc/>
  public event EventHandler<AudioSourceCompletedEventArgs>? PlaybackCompleted;

  /// <inheritdoc/>
  public abstract object GetSoundComponent();

  /// <inheritdoc/>
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

  /// <inheritdoc/>
  public virtual async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Playing)
    {
      return;
    }

    await StopCoreAsync(cancellationToken);
    State = AudioSourceState.Stopped;
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;

    await DisposeAsyncCore();
    State = AudioSourceState.Disposed;
    _disposed = true;
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Initializes the audio source.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected virtual Task InitializeAsync(CancellationToken cancellationToken = default)
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
