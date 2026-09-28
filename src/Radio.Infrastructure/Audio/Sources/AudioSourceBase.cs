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
  private readonly object _stateLock = new();
  private long _stateVersion;

  // True only while the current Error is one a PlayAsync call declined to overwrite (TTS-5). Before
  // TTS-5 such an Error was always replaced by Playing, so the next PlayAsync retried. Keeping the
  // Error must not also turn it into the "initialization failed" early return below, or a source
  // whose play failed once — FilePlayer's AutoSkipToNextAsync sets Error after too many skips —
  // would ignore every later Play press. Any state change clears it.
  private bool _errorKeptFromPlay;
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
    protected set => TrySetState(value, requireUnchangedSince: null);
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

    // Check if initialization failed. An Error that an earlier PlayAsync declined to overwrite is
    // NOT an initialization failure and is retried, exactly as it was when that Error used to be
    // overwritten with Playing (see _errorKeptFromPlay).
    bool errorKeptFromPlay;
    lock (_stateLock)
    {
      errorKeptFromPlay = _errorKeptFromPlay;
    }
    if (State == AudioSourceState.Error && !errorKeptFromPlay)
    {
      return;
    }

    var versionBeforePlay = CurrentStateVersion();
    await PlayCoreAsync(cancellationToken);
    PromoteToPlayingUnlessTerminatedSince(versionBeforePlay);
  }

  /// <summary>
  /// Reads the state-change counter that <see cref="PromoteToPlayingUnlessTerminatedSince"/>
  /// compares against. Take it immediately before calling the core method.
  /// </summary>
  protected long CurrentStateVersion()
  {
    lock (_stateLock)
    {
      return _stateVersion;
    }
  }

  /// <summary>
  /// Sets <see cref="AudioSourceState.Playing"/> after a core start (PlayCoreAsync, ResumeCoreAsync)
  /// returns — UNLESS the state was moved to Stopped, Error or Disposed while it ran (TTS-5).
  /// </summary>
  /// <param name="versionBeforeCore">The value of <see cref="CurrentStateVersion"/> taken just
  /// before the core method was called.</param>
  /// <remarks>
  /// ⚠ The unconditional <c>State = Playing</c> this replaces overwrote a terminal state that the
  /// core method had already reached. TTSEventSource and AudioFileEventSource return from
  /// PlayCoreAsync after starting a background task, and that task runs synchronously up to its first
  /// real await — so a playback service that fails without awaiting (no playback device) sets Error
  /// and raises PlaybackCompleted(Error) before PlayCoreAsync has even returned. The source then
  /// reported Playing until a caller stopped it, and SourcesController's TTS preview never does. It is
  /// the #469 shape (a state assignment that silently defeats the state another path set), one layer
  /// up, in the base classes — which is why ResumeAsync in PrimaryAudioSourceBase and
  /// EventAudioSourceBase uses this too: for USB and TestTone sources ResumeCoreAsync IS
  /// PlayCoreAsync.
  ///
  /// "Since" is decided by a version counter, not by reading the state: a source replayed from
  /// Stopped must still become Playing when the core method leaves the state alone. Only the three
  /// terminal states are protected; a non-terminal state left by the core method (e.g. Ready) is
  /// overwritten exactly as before. The check and the write share <c>_stateLock</c>, so the STORED
  /// state is never lost between them: a background task that terminates concurrently either lands
  /// first and is kept, or lands after Playing and wins. ⚠ StateChanged NOTIFICATIONS are raised
  /// after the lock is released and are not ordered across threads, so a subscriber can in a narrow
  /// race see Playing as the last event while State reads Error — as it could before TTS-5, when the
  /// setter took no lock at all.
  /// </remarks>
  protected void PromoteToPlayingUnlessTerminatedSince(long versionBeforeCore)
  {
    TrySetState(AudioSourceState.Playing, requireUnchangedSince: versionBeforeCore);
  }

  /// <summary>
  /// The single state-write path. With <paramref name="requireUnchangedSince"/> null this is a plain
  /// assignment (the <see cref="State"/> setter). With a version, the write is skipped when the state
  /// has changed since that version AND now reads Stopped, Error or Disposed.
  /// </summary>
  private void TrySetState(AudioSourceState value, long? requireUnchangedSince)
  {
    AudioSourceState previousState;
    AudioSourceState? declinedOver = null;
    lock (_stateLock)
    {
      if (requireUnchangedSince is { } since
          && _stateVersion != since
          && _state is AudioSourceState.Stopped or AudioSourceState.Error or AudioSourceState.Disposed)
      {
        declinedOver = _state;
        _errorKeptFromPlay = _state == AudioSourceState.Error;
        previousState = _state;
      }
      else
      {
        if (_state == value)
        {
          return;
        }

        previousState = _state;
        _state = value;
        _stateVersion++;
        _errorKeptFromPlay = false;
      }
    }

    // Logged and raised outside the lock: handlers may read State or call back into this source.
    if (declinedOver is { } kept)
    {
      _logger.LogDebug(
        "Audio source {Id} reached {State} while starting; not promoting it to {Requested}", Id, kept, value);
      return;
    }

    LogStateChange(previousState, value);
    OnStateChanged(previousState, value);
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
  ///
  /// ⚠ "Null-guarded and idempotent" is a CONTRACT ON IMPLEMENTORS, not a description of what the
  /// base class enforces, and AudioFileEventSource broke it until PHN-10: its StopCoreAsync was
  /// ACTIVITY-guarded on a private bool that the caller's own cancellation cleared first, so the
  /// only call that detaches a source from the SoundFlow mixer never ran. That is PR #469's shape
  /// one layer down — not State, but a private mirror of it, which is worse because this paragraph
  /// reads as covering it.
  ///
  /// ⛔ NOTHING CHECKS THE CONTRACT ABOVE, and this paragraph is not a claim that something now
  /// does. EventSourceStopIsNotActivityGuardedLintTests reds on the one SHAPE that broke it — a
  /// playback-service stop gated on a private bool — over a single directory, and it verifies
  /// neither null-guarding nor idempotency of anything. Read its remarks for what it cannot see
  /// before treating a green run as cover. An implementor that detaches by some other route, or
  /// that guards on a stale mirror reached through a local or a property, satisfies the lint and
  /// still breaks this contract.
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
