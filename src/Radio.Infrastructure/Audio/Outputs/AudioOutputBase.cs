using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Base class for audio output implementations providing common functionality
/// for state management, volume control, and disposal.
/// </summary>
public abstract class AudioOutputBase : IAudioOutput
{
  private readonly object _stateLock = new();
  private AudioOutputState _state = AudioOutputState.Created;
  private float _volume;
  private bool _isMuted;

  /// <summary>
  /// Gets the logger instance for this output.
  /// </summary>
  protected abstract ILogger Logger { get; }

  /// <summary>
  /// Gets or sets whether this output is enabled.
  /// </summary>
  protected bool IsEnabledInternal { get; set; }

  /// <summary>
  /// Gets or sets whether this output has been disposed.
  /// </summary>
  protected bool IsDisposed { get; set; }

  /// <inheritdoc />
  public string Id { get; }

  /// <inheritdoc />
  public string Name { get; protected set; }

  /// <inheritdoc />
  public abstract AudioOutputType Type { get; }

  /// <inheritdoc />
  public AudioOutputState State
  {
    get
    {
      lock (_stateLock)
      {
        return _state;
      }
    }
    protected set
    {
      AudioOutputState previousState;
      lock (_stateLock)
      {
        previousState = _state;
        _state = value;
      }

      if (previousState != value)
      {
        Logger.LogInformation(
          "{OutputType} output state changed from {PreviousState} to {NewState}",
          Type, previousState, value);

        OnStateChanged(previousState, value);
      }
    }
  }

  /// <inheritdoc />
  public float Volume
  {
    get => _volume;
    set
    {
      var clamped = Math.Clamp(value, 0f, 1f);
      if (Math.Abs(_volume - clamped) > 0.0001f)
      {
        _volume = clamped;
        Logger.LogDebug("{OutputType} output volume set to {Volume:P0}", Type, _volume);
        OnVolumeChanged(_volume);
      }
    }
  }

  /// <inheritdoc />
  public bool IsMuted
  {
    get => _isMuted;
    set
    {
      if (_isMuted != value)
      {
        _isMuted = value;
        Logger.LogDebug("{OutputType} output mute set to {IsMuted}", Type, _isMuted);
        OnMuteChanged(_isMuted);
      }
    }
  }

  /// <inheritdoc />
  public bool IsEnabled => IsEnabledInternal;

  /// <inheritdoc />
  public event EventHandler<AudioOutputStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioOutputBase"/> class.
  /// </summary>
  /// <param name="idPrefix">The prefix for the unique ID (e.g., "local-output", "cast-output").</param>
  /// <param name="defaultName">The default name for this output.</param>
  /// <param name="defaultVolume">The default volume level (0.0 to 1.0).</param>
  /// <param name="enabled">Whether the output is enabled by default.</param>
  protected AudioOutputBase(string idPrefix, string defaultName, float defaultVolume, bool enabled)
  {
    Id = $"{idPrefix}-{Guid.NewGuid():N}";
    Name = defaultName;
    _volume = Math.Clamp(defaultVolume, 0f, 1f);
    IsEnabledInternal = enabled;
  }

  /// <inheritdoc />
  public abstract Task InitializeAsync(CancellationToken cancellationToken = default);

  /// <inheritdoc />
  public abstract Task StartAsync(CancellationToken cancellationToken = default);

  /// <inheritdoc />
  public abstract Task StopAsync(CancellationToken cancellationToken = default);

  /// <inheritdoc />
  public abstract ValueTask DisposeAsync();

  /// <summary>
  /// Called when the state changes. Raises the StateChanged event.
  /// </summary>
  /// <param name="previousState">The previous state.</param>
  /// <param name="newState">The new state.</param>
  /// <param name="errorMessage">Optional error message if transitioning to error state.</param>
  protected virtual void OnStateChanged(AudioOutputState previousState, AudioOutputState newState, string? errorMessage = null)
  {
    StateChanged?.Invoke(this, new AudioOutputStateChangedEventArgs
    {
      PreviousState = previousState,
      NewState = newState,
      OutputId = Id,
      ErrorMessage = errorMessage
    });
  }

  /// <summary>
  /// Called when the volume changes. Override to apply volume to the output device.
  /// </summary>
  /// <param name="volume">The new volume level (0.0 to 1.0).</param>
  protected virtual void OnVolumeChanged(float volume)
  {
    // Override in derived classes to apply volume to device
  }

  /// <summary>
  /// Called when the mute state changes. Override to apply mute to the output device.
  /// </summary>
  /// <param name="muted">Whether the output is muted.</param>
  protected virtual void OnMuteChanged(bool muted)
  {
    // Override in derived classes to apply mute to device
  }

  /// <summary>
  /// Throws an ObjectDisposedException if this output has been disposed.
  /// </summary>
  protected void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(IsDisposed, this);
  }

  /// <summary>
  /// Validates that the output can be initialized (must be in Created or Error state).
  /// </summary>
  /// <exception cref="InvalidOperationException">Thrown if the output cannot be initialized.</exception>
  protected void ValidateCanInitialize()
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Created && State != AudioOutputState.Error)
    {
      throw new InvalidOperationException(
        $"Cannot initialize output in state {State}. Output must be in Created or Error state.");
    }
  }

  /// <summary>
  /// Validates that the output can be started (must be in Ready or Stopped state).
  /// </summary>
  /// <exception cref="InvalidOperationException">Thrown if the output cannot be started.</exception>
  protected void ValidateCanStart()
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot start output in state {State}. Output must be in Ready or Stopped state.");
    }
  }

  /// <summary>
  /// Checks if stop is valid and logs a warning if not streaming.
  /// </summary>
  /// <returns>True if the output is streaming and can be stopped, false otherwise.</returns>
  protected bool ValidateCanStop()
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Streaming)
    {
      Logger.LogWarning("Stop requested but output is not streaming (state: {State})", State);
      return false;
    }

    return true;
  }

  /// <summary>
  /// Gets the effective volume considering the mute state.
  /// </summary>
  /// <returns>0 if muted, otherwise the current volume level.</returns>
  public float GetEffectiveVolume()
  {
    return _isMuted ? 0f : _volume;
  }

  /// <summary>
  /// Performs common disposal tasks. Call this from derived class DisposeAsync.
  /// </summary>
  protected void DisposeBase()
  {
    IsDisposed = true;
    IsEnabledInternal = false;
    State = AudioOutputState.Disposed;
    Logger.LogInformation("{OutputType} output disposed", Type);
  }
}
