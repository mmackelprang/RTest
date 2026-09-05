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

/// <summary>What happened to the ducking set, as distinct from what the aggregate state now is.</summary>
/// <remarks>
/// ⚠ This exists because <see cref="DuckingStateChangedEventArgs.IsDucking"/> answers a DIFFERENT
/// question, and overloading it is what makes the obvious implementations wrong. IsDucking is the
/// AGGREGATE — "is anything ducking" — and AudioManager keys ClearDuckingMultiplier off its false
/// edge. A source LEAVING while others remain is an <see cref="Ended"/> transition with IsDucking
/// still TRUE, and the two facts must be separately expressible or one of them has to lie.
///
/// ⚠ <see cref="Started"/> is 0, so it is the value an args object gets when nothing sets this
/// field. That is why AudioManager consults this only to choose a LOG LINE and never to decide
/// whether to clear the ducking multiplier — see its handler, and plan PHN-1f C-58.
/// </remarks>
public enum DuckingSourceTransition
{
  /// <summary>A source joined the ducking set.</summary>
  Started = 0,

  /// <summary>A source left the ducking set. Others may remain — read IsDucking for that.</summary>
  Ended = 1,

  /// <summary>
  /// Every source was cleared at once (<see cref="IDuckingService.StopAllDuckingAsync"/>).
  /// TriggeringSource is null.
  /// </summary>
  AllCleared = 2
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

  /// <summary>What happened to the set. See <see cref="DuckingSourceTransition"/>.</summary>
  public DuckingSourceTransition Transition { get; init; }

  /// <summary>
  /// The triggering source's priority, CAPTURED AT RAISE TIME, or 0 when there is no triggering
  /// source.
  /// </summary>
  /// <remarks>
  /// ⚠ Captured rather than looked up, and that is the entire point of the field. A subscriber that
  /// calls <see cref="IDuckingService.GetPriority"/> for itself races the ducking service, which
  /// DELETES the override before it raises — so the answer for a source that has just left is the
  /// category default 8 for an announcement whose caller explicitly claimed 3. The same is true on
  /// the START path, because the transition raise happens after the attack fade: a stop landing
  /// inside that ~100 ms deletes the entry first. PHN-1d had to guard that with an ActiveEventCount
  /// check and could only narrow it; this closes it.
  /// </remarks>
  public int TriggeringSourcePriority { get; init; }
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
