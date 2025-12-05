using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Base abstract class for primary audio sources.
/// Provides common functionality for state management and events.
/// </summary>
public abstract class PrimaryAudioSourceBase : IPrimaryAudioSource
{
  private readonly ILogger _logger;
  private readonly IMetricsCollector? _metricsCollector;
  private AudioSourceState _state = AudioSourceState.Created;
  private float _volume = 1.0f;
  private bool _disposed;
  private string? _id;

  /// <summary>
  /// Initializes a new instance of the <see cref="PrimaryAudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking playback metrics.</param>
  protected PrimaryAudioSourceBase(ILogger logger, IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Gets the metrics collector for derived classes.
  /// </summary>
  protected IMetricsCollector? MetricsCollector => _metricsCollector;

  /// <inheritdoc/>
  public string Id => _id ??= $"{Type}-{Guid.NewGuid():N}";

  /// <inheritdoc/>
  public abstract string Name { get; }

  /// <inheritdoc/>
  public abstract AudioSourceType Type { get; }

  /// <inheritdoc/>
  public AudioSourceCategory Category => AudioSourceCategory.Primary;

  /// <inheritdoc/>
  public AudioSourceState State
  {
    get => _state;
    protected set
    {
      if (_state == value) return;
      var previousState = _state;
      _state = value;
      _logger.LogDebug("Audio source {Id} state changed from {PreviousState} to {NewState}",
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
  public abstract TimeSpan? Duration { get; }

  /// <inheritdoc/>
  public abstract TimeSpan Position { get; }

  /// <inheritdoc/>
  public abstract bool IsSeekable { get; }

  /// <inheritdoc/>
  public abstract IReadOnlyDictionary<string, object> Metadata { get; }

  // Capability properties - default to false, subclasses override as needed

  /// <inheritdoc/>
  public virtual bool SupportsNext => false;

  /// <inheritdoc/>
  public virtual bool SupportsPrevious => false;

  /// <inheritdoc/>
  public virtual bool SupportsShuffle => false;

  /// <inheritdoc/>
  public virtual bool SupportsRepeat => false;

  /// <inheritdoc/>
  public virtual bool SupportsQueue => false;

  // Shuffle and Repeat properties - default values

  /// <inheritdoc/>
  public virtual bool IsShuffleEnabled => false;

  /// <inheritdoc/>
  public virtual RepeatMode RepeatMode => RepeatMode.Off;

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
  public virtual async Task PauseAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Playing)
    {
      _logger.LogWarning("Cannot pause {SourceId} - not playing (state: {State})", Id, State);
      return;
    }

    await PauseCoreAsync(cancellationToken);
    State = AudioSourceState.Paused;
  }

  /// <inheritdoc/>
  public virtual async Task ResumeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Paused)
    {
      _logger.LogWarning("Cannot resume {SourceId} - not paused (state: {State})", Id, State);
      return;
    }

    await ResumeCoreAsync(cancellationToken);
    State = AudioSourceState.Playing;
  }

  /// <inheritdoc/>
  public virtual async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    await StopCoreAsync(cancellationToken);
    State = AudioSourceState.Stopped;
  }

  /// <inheritdoc/>
  public virtual async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!IsSeekable)
    {
      throw new NotSupportedException($"Audio source {Id} does not support seeking.");
    }

    await SeekCoreAsync(position, cancellationToken);
  }

  /// <inheritdoc/>
  public virtual Task NextAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsNext)
    {
      throw new NotSupportedException($"Audio source {Id} does not support skipping to next track.");
    }
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public virtual Task PreviousAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsPrevious)
    {
      throw new NotSupportedException($"Audio source {Id} does not support going to previous track.");
    }
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public virtual Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsShuffle)
    {
      throw new NotSupportedException($"Audio source {Id} does not support shuffle mode.");
    }
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public virtual Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsRepeat)
    {
      throw new NotSupportedException($"Audio source {Id} does not support repeat mode.");
    }
    return Task.CompletedTask;
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
  /// Core implementation for pausing playback.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected abstract Task PauseCoreAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Core implementation for resuming playback.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected abstract Task ResumeCoreAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Core implementation for stopping playback.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected abstract Task StopCoreAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Core implementation for seeking.
  /// </summary>
  /// <param name="position">The position to seek to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected virtual Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    throw new NotSupportedException($"Audio source {Id} does not support seeking.");
  }

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
    // Track metrics for natural completion
    if (reason == PlaybackCompletionReason.EndOfContent)
    {
      _metricsCollector?.Increment("audio.songs_played_total");
    }

    PlaybackCompleted?.Invoke(this, new AudioSourceCompletedEventArgs
    {
      SourceId = Id,
      Reason = reason,
      Error = error
    });
  }

  /// <summary>
  /// Tracks that a track was skipped (for metrics).
  /// Should be called by derived classes when implementing NextAsync if a track is being skipped during playback.
  /// </summary>
  protected void TrackSkipped()
  {
    _metricsCollector?.Increment("audio.songs_skipped");
  }

  /// <summary>
  /// Tracks that a playback error occurred (for metrics).
  /// Should be called by derived classes when handling playback exceptions.
  /// </summary>
  protected void TrackPlaybackError()
  {
    _metricsCollector?.Increment("audio.playback_errors");
  }

  /// <summary>
  /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
  /// </summary>
  protected void ThrowIfDisposed()
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(GetType().Name);
    }
  }

  /// <summary>
  /// Gets the logger for this instance.
  /// </summary>
  protected ILogger Logger => _logger;
}
