namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service that manages audio ducking when event sources play.
/// Ducking reduces the volume of primary audio sources when higher-priority
/// event audio (such as TTS announcements or notifications) is playing.
/// </summary>
public interface IDuckingService : IDisposable
{
  /// <summary>
  /// Gets the current duck level as a percentage (0-100).
  /// 100 means full volume, lower values indicate ducking is active.
  /// </summary>
  float CurrentDuckLevel { get; }

  /// <summary>
  /// Gets whether ducking is currently active.
  /// </summary>
  bool IsDucking { get; }

  /// <summary>
  /// Gets the number of active event sources currently causing ducking.
  /// </summary>
  int ActiveEventCount { get; }

  /// <summary>
  /// Starts ducking for the specified event source.
  /// This reduces the volume of primary audio sources according to configuration.
  /// </summary>
  /// <param name="eventSource">The event audio source that triggers ducking.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StartDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops ducking for the specified event source.
  /// If no other event sources are active, the primary audio volume will be restored.
  /// </summary>
  /// <param name="eventSource">The event audio source that stops ducking.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StopDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default);

  /// <summary>
  /// Forces an immediate stop to all ducking and restores full volume.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task StopAllDuckingAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the priority of an audio source.
  /// Higher values indicate higher priority.
  /// </summary>
  /// <param name="source">The audio source.</param>
  /// <returns>The priority level (1-10, where 10 is highest priority).</returns>
  int GetPriority(IAudioSource source);

  /// <summary>
  /// Sets the priority of an audio source.
  /// </summary>
  /// <param name="source">The audio source.</param>
  /// <param name="priority">The priority level (1-10, where 10 is highest priority).</param>
  void SetPriority(IAudioSource source, int priority);

  /// <summary>
  /// Gets all currently active event sources sorted by priority (highest first).
  /// </summary>
  /// <returns>A read-only list of active event sources.</returns>
  IReadOnlyList<IEventAudioSource> GetActiveEventsByPriority();

  /// <summary>
  /// Event raised when the duck state changes.
  /// </summary>
  event EventHandler<DuckingStateChangedEventArgs>? DuckingStateChanged;

  /// <summary>
  /// Event raised when the duck level changes during a fade transition.
  /// </summary>
  event EventHandler<DuckingLevelChangedEventArgs>? DuckingLevelChanged;
}

/// <summary>
/// Event arguments for ducking state changes.
/// </summary>
public class DuckingStateChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets whether ducking is now active.
  /// </summary>
  public bool IsDucking { get; init; }

  /// <summary>
  /// Gets the event source that triggered the state change.
  /// </summary>
  public IEventAudioSource? TriggeringSource { get; init; }

  /// <summary>
  /// Gets the current duck level as a percentage (0-100).
  /// </summary>
  public float DuckLevel { get; init; }

  /// <summary>
  /// Gets the number of active event sources.
  /// </summary>
  public int ActiveEventCount { get; init; }
}

/// <summary>
/// Event arguments for ducking level changes during fade transitions.
/// </summary>
public class DuckingLevelChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the previous duck level as a percentage (0-100).
  /// </summary>
  public float PreviousLevel { get; init; }

  /// <summary>
  /// Gets the new duck level as a percentage (0-100).
  /// </summary>
  public float NewLevel { get; init; }

  /// <summary>
  /// Gets whether the fade transition is complete.
  /// </summary>
  public bool TransitionComplete { get; init; }
}
