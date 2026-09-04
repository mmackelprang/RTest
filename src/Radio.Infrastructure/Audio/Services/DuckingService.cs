using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Service that manages audio ducking when event sources play.
/// Implements volume reduction of primary audio sources when higher-priority
/// event audio (such as TTS announcements or notifications) is playing.
/// </summary>
public class DuckingService : IDuckingService
{
  private readonly ILogger<DuckingService> _logger;
  private readonly IOptionsMonitor<AudioOptions> _audioOptions;
  private readonly IMasterMixer _masterMixer;
  private readonly object _lock = new();

  private readonly Dictionary<string, IEventAudioSource> _activeEvents = new();
  private readonly Dictionary<string, int> _sourcePriorities = new();
  private float _currentDuckLevel = 100f; // 100% = full volume
  private bool _isDucking;
  private CancellationTokenSource? _fadeTokenSource;
  private bool _disposed;

  /// <summary>
  /// Default priority for event sources.
  /// </summary>
  public const int DefaultEventPriority = 8;

  /// <summary>
  /// Default priority for primary sources.
  /// </summary>
  public const int DefaultPrimaryPriority = 3;

  /// <summary>
  /// Initializes a new instance of the <see cref="DuckingService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="audioOptions">The audio options.</param>
  /// <param name="masterMixer">The master mixer for volume control.</param>
  public DuckingService(
    ILogger<DuckingService> logger,
    IOptionsMonitor<AudioOptions> audioOptions,
    IMasterMixer masterMixer)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));
    _masterMixer = masterMixer ?? throw new ArgumentNullException(nameof(masterMixer));
  }

  /// <inheritdoc />
  public float CurrentDuckLevel
  {
    get
    {
      lock (_lock)
      {
        return _currentDuckLevel;
      }
    }
  }

  /// <inheritdoc />
  public bool IsDucking
  {
    get
    {
      lock (_lock)
      {
        return _isDucking;
      }
    }
  }

  /// <inheritdoc />
  public int ActiveEventCount
  {
    get
    {
      lock (_lock)
      {
        return _activeEvents.Count;
      }
    }
  }

  /// <inheritdoc />
  public event EventHandler<DuckingStateChangedEventArgs>? DuckingStateChanged;

  /// <inheritdoc />
  public event EventHandler<DuckingLevelChangedEventArgs>? DuckingLevelChanged;

  /// <inheritdoc />
  /// <remarks>
  /// ⚠ DuckingStateChanged is raised for EVERY source that joins the ducking set, not only for the
  /// one that caused the fade. That is ADR-029 D5 §6.3, and it is what makes priority load-bearing:
  /// EventPlaybackService subscribes and stops attended playback when a source at or above
  /// GvMedia:PreemptAtPriority starts. Before this change a second concurrent event reached only a
  /// LogDebug, so nothing downstream could ever learn that it had started.
  ///
  /// The ADR's wording is "on every call"; this raises on every call that ADDS a source. A repeat call
  /// for a source already in the set is not a start — nothing joins, the level does not move — and
  /// raising for it would fan an event out to AudioManager, which writes an Information line per raise,
  /// on a box where avoidable churn is audible (PHN arc breakdown, trap 5).
  ///
  /// ⚠ Ordering is NOT the order of starts. The transition raise happens after ApplyFadeAsync, which
  /// awaits for Audio:DuckingAttackMs; a second source arriving inside that window is announced first.
  /// Each raise carries its own TriggeringSource, so a subscriber that reads that field rather than
  /// assuming sequence is unaffected. Do not "fix" this by moving the transition raise ahead of the
  /// fade: AudioManager's log line would then claim a duck level the fade has not reached.
  /// </remarks>
  public async Task StartDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(eventSource);
    ObjectDisposedException.ThrowIf(_disposed, this);

    var options = _audioOptions.CurrentValue;
    bool needsTransition;
    bool wasNewlyAdded;
    int activeCount;

    lock (_lock)
    {
      needsTransition = !_isDucking;

      wasNewlyAdded = !_activeEvents.ContainsKey(eventSource.Id);
      if (wasNewlyAdded)
      {
        _activeEvents[eventSource.Id] = eventSource;
      }

      activeCount = _activeEvents.Count;

      if (!_isDucking)
      {
        _isDucking = true;
      }
    }

    if (wasNewlyAdded)
    {
      _logger.LogDebug(
        "Added event source '{SourceId}' to ducking queue. Active events: {Count}",
        eventSource.Id, activeCount);
    }

    if (needsTransition)
    {
      var targetLevel = options.DuckingPercentage;
      var attackMs = options.DuckingAttackMs;

      _logger.LogInformation(
        "Starting ducking: target level {TargetLevel}%, attack time {AttackMs}ms, policy {Policy}",
        targetLevel, attackMs, options.DuckingPolicy);

      await ApplyFadeAsync(targetLevel, attackMs, options.DuckingPolicy, eventSource, cancellationToken);
    }

    // needsTransition implies wasNewlyAdded in every state reachable today — _activeEvents is
    // non-empty only while _isDucking is true, and StopAllDuckingAsync clears both together. The
    // disjunction is written out anyway so that a state where they diverge still announces the
    // transition.
    if (needsTransition || wasNewlyAdded)
    {
      RaiseDuckingStateChanged(true, eventSource);
    }
    else
    {
      _logger.LogDebug(
        "Event source '{SourceId}' was already in the ducking queue; nothing started. Active events: {Count}",
        eventSource.Id, activeCount);
    }
  }

  /// <inheritdoc />
  public async Task StopDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(eventSource);
    ObjectDisposedException.ThrowIf(_disposed, this);

    var options = _audioOptions.CurrentValue;
    bool needsRestore;
    int remainingEvents;

    lock (_lock)
    {
      // Remove from active events
      _activeEvents.Remove(eventSource.Id);

      // Also drop any per-source priority override. Callers (e.g. AnnouncementService)
      // call SetPriority(source) with a fresh GUID id for every TTS/notification before
      // StartDuckingAsync, so without this removal _sourcePriorities would grow by one
      // entry per announcement forever — a slow but unbounded memory leak.
      _sourcePriorities.Remove(eventSource.Id);

      remainingEvents = _activeEvents.Count;

      // Only restore if no other events are active
      needsRestore = _isDucking && remainingEvents == 0;

      if (needsRestore)
      {
        _isDucking = false;
      }
    }

    _logger.LogDebug(
      "Removed event source '{SourceId}' from ducking queue. Remaining events: {Count}",
      eventSource.Id, remainingEvents);

    if (needsRestore)
    {
      var releaseMs = options.DuckingReleaseMs;

      _logger.LogInformation(
        "Stopping ducking: releasing to 100%, release time {ReleaseMs}ms, policy {Policy}",
        releaseMs, options.DuckingPolicy);

      await ApplyFadeAsync(100f, releaseMs, options.DuckingPolicy, eventSource, cancellationToken);

      RaiseDuckingStateChanged(false, eventSource);
    }
  }

  /// <inheritdoc />
  public async Task StopAllDuckingAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    List<IEventAudioSource> eventsCopy;
    lock (_lock)
    {
      eventsCopy = _activeEvents.Values.ToList();
      _activeEvents.Clear();
      _isDucking = false;
    }

    _logger.LogInformation(
      "Force stopping all ducking. Cleared {Count} active events.",
      eventsCopy.Count);

    // Instantly restore full volume
    await ApplyFadeAsync(100f, 0, DuckingPolicy.Instant, null, cancellationToken);

    RaiseDuckingStateChanged(false, null);
  }

  /// <inheritdoc />
  public int GetPriority(IAudioSource source)
  {
    ArgumentNullException.ThrowIfNull(source);

    lock (_lock)
    {
      if (_sourcePriorities.TryGetValue(source.Id, out var priority))
      {
        return priority;
      }
    }

    // Return default priority based on category
    return source.Category == AudioSourceCategory.Event
      ? DefaultEventPriority
      : DefaultPrimaryPriority;
  }

  /// <inheritdoc />
  public void SetPriority(IAudioSource source, int priority)
  {
    ArgumentNullException.ThrowIfNull(source);

    if (priority < 1 || priority > 10)
    {
      throw new ArgumentOutOfRangeException(
        nameof(priority),
        priority,
        "Priority must be between 1 and 10.");
    }

    lock (_lock)
    {
      _sourcePriorities[source.Id] = priority;
    }

    _logger.LogDebug(
      "Set priority {Priority} for source '{SourceId}'",
      priority, source.Id);
  }

  /// <inheritdoc />
  public IReadOnlyList<IEventAudioSource> GetActiveEventsByPriority()
  {
    lock (_lock)
    {
      return _activeEvents.Values
        .OrderByDescending(e => GetPriority(e))
        .ThenBy(e => e.Id) // Stable sort by ID for same priority
        .ToList();
    }
  }

  /// <summary>
  /// Applies a fade transition to the target duck level.
  /// </summary>
  private async Task ApplyFadeAsync(
    float targetLevel,
    int durationMs,
    DuckingPolicy policy,
    IEventAudioSource? triggeringSource,
    CancellationToken cancellationToken)
  {
    // Cancel any existing fade operation
    _fadeTokenSource?.Cancel();
    _fadeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var fadeToken = _fadeTokenSource.Token;

    float startLevel;
    lock (_lock)
    {
      startLevel = _currentDuckLevel;
    }

    // Handle instant transition
    if (policy == DuckingPolicy.Instant || durationMs <= 0)
    {
      SetDuckLevelInternal(targetLevel, true);
      return;
    }

    // Calculate fade parameters based on policy
    var (actualDurationMs, stepCount) = CalculateFadeParameters(policy, durationMs, startLevel, targetLevel);

    if (stepCount <= 1)
    {
      SetDuckLevelInternal(targetLevel, true);
      return;
    }

    var stepDurationMs = actualDurationMs / stepCount;
    var levelDelta = (targetLevel - startLevel) / stepCount;

    _logger.LogDebug(
      "Starting fade from {Start}% to {Target}% over {Duration}ms ({Steps} steps)",
      startLevel, targetLevel, actualDurationMs, stepCount);

    // Perform the fade
    for (int i = 1; i <= stepCount; i++)
    {
      if (fadeToken.IsCancellationRequested)
      {
        _logger.LogDebug("Fade cancelled at step {Step}/{Total}", i, stepCount);
        break;
      }

      var newLevel = startLevel + (levelDelta * i);
      var isComplete = i == stepCount;

      // Ensure final step hits exact target
      if (isComplete)
      {
        newLevel = targetLevel;
      }

      SetDuckLevelInternal(newLevel, isComplete);

      if (!isComplete)
      {
        try
        {
          await Task.Delay(stepDurationMs, fadeToken);
        }
        catch (TaskCanceledException)
        {
          _logger.LogDebug("Fade delay cancelled at step {Step}/{Total}", i, stepCount);
          break;
        }
      }
    }
  }

  /// <summary>
  /// Calculates fade parameters based on the ducking policy.
  /// </summary>
  private static (int durationMs, int stepCount) CalculateFadeParameters(
    DuckingPolicy policy,
    int requestedDurationMs,
    float startLevel,
    float targetLevel)
  {
    var levelDifference = Math.Abs(targetLevel - startLevel);

    // No change needed
    if (levelDifference < 0.1f)
    {
      return (0, 0);
    }

    switch (policy)
    {
      case DuckingPolicy.FadeSmooth:
        // Smooth fade: more steps for smoother transition
        // Target ~60 fps equivalent for smoothness
        var smoothSteps = Math.Max(5, (int)(requestedDurationMs / 16));
        return (requestedDurationMs, smoothSteps);

      case DuckingPolicy.FadeQuick:
        // Quick fade: fewer steps, faster transitions
        var quickDuration = Math.Max(50, requestedDurationMs / 2);
        var quickSteps = Math.Max(3, quickDuration / 25);
        return (quickDuration, quickSteps);

      case DuckingPolicy.Instant:
      default:
        return (0, 1);
    }
  }

  /// <summary>
  /// Sets the duck level internally and raises level changed events.
  /// </summary>
  private void SetDuckLevelInternal(float level, bool transitionComplete)
  {
    float previousLevel;
    lock (_lock)
    {
      previousLevel = _currentDuckLevel;
      _currentDuckLevel = Math.Clamp(level, 0f, 100f);
    }

    // Note: The DuckingService does not directly modify the mixer volume.
    // Instead, it emits DuckingLevelChanged events that allow consuming code
    // (such as an AudioManager or similar orchestration layer) to apply the
    // volume changes to primary sources as needed. This separation of concerns
    // allows for more flexible volume management strategies.

    _logger.LogTrace(
      "Duck level changed from {Previous:F1}% to {Current:F1}%",
      previousLevel, level);

    RaiseDuckingLevelChanged(previousLevel, level, transitionComplete);
  }

  /// <summary>
  /// Raises the DuckingStateChanged event.
  /// </summary>
  /// <remarks>
  /// ⚠ Guarded because ADR-029 D5 makes this event load-bearing: EventPlaybackService subscribes to
  /// it to preempt attended playback, and it is the first subscriber that can throw. Unguarded, that
  /// exception propagates out of StartDuckingAsync into whichever event path called it. Traced against
  /// the tree: AnnounceAsync, PlaySoundWithAnnouncementAsync and EventPlaybackService.AcquireAndPlayAsync
  /// all catch and then restore ducking, so the cost is NOT stuck ducking — it is a silently swallowed
  /// announcement that POST /api/notifications/announce still reports as 200.
  ///
  /// This catches; it does not resume the invocation list. A handler that throws still prevents the
  /// handlers registered after it from running. That is accepted for two subscribers; anything more
  /// would want a GetInvocationList loop and a reason.
  ///
  /// RaiseDuckingLevelChanged is deliberately NOT given the same guard: it gains no new subscriber in
  /// this PR and it fires once per fade step, so a try inside that loop would buy nothing that exists.
  /// </remarks>
  private void RaiseDuckingStateChanged(bool isDucking, IEventAudioSource? triggeringSource)
  {
    var args = new DuckingStateChangedEventArgs
    {
      IsDucking = isDucking,
      TriggeringSource = triggeringSource,
      DuckLevel = CurrentDuckLevel,
      ActiveEventCount = ActiveEventCount
    };

    try
    {
      DuckingStateChanged?.Invoke(this, args);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "A DuckingStateChanged subscriber threw (isDucking={IsDucking}, source='{SourceId}'). "
        + "Ducking state is unaffected; the subscriber's work did not happen.",
        isDucking,
        triggeringSource?.Id ?? "<none>");
    }
  }

  /// <summary>
  /// Raises the DuckingLevelChanged event.
  /// </summary>
  private void RaiseDuckingLevelChanged(float previousLevel, float newLevel, bool transitionComplete)
  {
    var args = new DuckingLevelChangedEventArgs
    {
      PreviousLevel = previousLevel,
      NewLevel = newLevel,
      TransitionComplete = transitionComplete
    };

    DuckingLevelChanged?.Invoke(this, args);
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;

    _fadeTokenSource?.Cancel();
    _fadeTokenSource?.Dispose();
    _fadeTokenSource = null;

    lock (_lock)
    {
      _activeEvents.Clear();
      _sourcePriorities.Clear();
    }

    _logger.LogDebug("DuckingService disposed");
  }
}
