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
  /// raising for it would fan an event out to AudioManager, which writes an Information line per raise
  /// once a playback service is attached (it returns early before that), on a box where avoidable churn
  /// is audible (PHN arc breakdown, trap 5).
  ///
  /// ⚠ Ordering is NOT the order of starts. The transition raise happens after ApplyFadeAsync, which
  /// awaits for Audio:DuckingAttackMs; a second source arriving inside that window is announced first.
  /// Each raise carries its own TriggeringSource, so a subscriber that reads that field rather than
  /// assuming sequence is unaffected.
  ///
  /// ⚠ Two consequences of that ordering, both real, neither a reason to reorder:
  ///
  /// (a) A source announced from inside the first source's fade carries a MID-FADE DuckLevel, so
  ///     AudioManager already logs "Ducking started: duckLevel=NN%" for a level the fade has not
  ///     reached. An earlier draft of this remark offered exactly that as the reason not to move the
  ///     transition raise ahead of the fade — which was inconsistent, because the raise this change
  ///     adds already does it. The honest reason not to move it is narrower: the transition raise is
  ///     the one AudioManager's "ducking started" line is keyed to, and firing it before the fade would
  ///     make the level wrong on the path where it is currently right.
  ///
  /// (b) A stop for the SAME source landing inside that fade deletes its priority override before this
  ///     raise fires. That USED to mean a subscriber resolving the priority for itself read the
  ///     category default, and EventPlaybackService.OnDuckingStateChanged could only narrow it with an
  ///     ActiveEventCount guard. PHN-1f closed it instead: the args now carry
  ///     DuckingStateChangedEventArgs.TriggeringSourcePriority, captured inside the lock that ADDS the
  ///     source and therefore before this fade, so there is nothing left to race. The ActiveEventCount
  ///     guard is GONE, not narrowed, and so is the subscriber's own GetPriority call.
  /// </remarks>
  public async Task StartDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(eventSource);
    ObjectDisposedException.ThrowIf(_disposed, this);

    var options = _audioOptions.CurrentValue;
    bool needsTransition;
    bool wasNewlyAdded;
    int activeCount;
    int priorityAtStart;

    lock (_lock)
    {
      needsTransition = !_isDucking;

      wasNewlyAdded = !_activeEvents.ContainsKey(eventSource.Id);
      if (wasNewlyAdded)
      {
        _activeEvents[eventSource.Id] = eventSource;
      }

      // ⚠ CAPTURED HERE, inside the lock that adds the entry and BEFORE ApplyFadeAsync — and that
      // ordering is the whole fix for PHN-1d's fade window. The transition raise below happens AFTER
      // the attack fade (Audio:DuckingAttackMs, 100 ms shipped), so a StopDuckingAsync for THIS source
      // landing inside it deletes the override first, and a subscriber resolving the priority at raise
      // time reads the category default 8 for an announcement that explicitly claimed 3. Reading it
      // here means there is nothing left to race.
      //
      // GetPriority re-enters _lock; Monitor is reentrant and GetActiveEventsByPriority already relies
      // on exactly that.
      priorityAtStart = GetPriority(eventSource);

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
      RaiseDuckingStateChanged(
        true, eventSource, DuckingSourceTransition.Started, priorityAtStart);
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
    bool wasPresent;
    int remainingEvents;
    int priorityBeforeRemoval;

    lock (_lock)
    {
      // ⚠ CAPTURED BEFORE THE REMOVALS, in the same lock that performs them. That is the whole of the
      // capture: below in this same lock, _sourcePriorities.Remove deletes the override, and every
      // subscriber that resolved the priority for itself after that point read the category default 8.
      priorityBeforeRemoval = GetPriority(eventSource);

      // Remove from active events. ⚠ The bool is KEPT — see the raise at the bottom of this method,
      // which must not announce a departure that did not happen.
      wasPresent = _activeEvents.Remove(eventSource.Id);

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

    if (wasPresent)
    {
      _logger.LogDebug(
        "Removed event source '{SourceId}' from ducking queue. Remaining events: {Count}",
        eventSource.Id, remainingEvents);
    }
    else
    {
      // Said accurately rather than "Removed": nothing was. Mirrors the else-arm StartDuckingAsync
      // already has for a repeat start.
      _logger.LogDebug(
        "Event source '{SourceId}' was not in the ducking queue; nothing removed. Remaining events: {Count}",
        eventSource.Id, remainingEvents);
    }

    if (needsRestore)
    {
      var releaseMs = options.DuckingReleaseMs;

      _logger.LogInformation(
        "Stopping ducking: releasing to 100%, release time {ReleaseMs}ms, policy {Policy}",
        releaseMs, options.DuckingPolicy);

      await ApplyFadeAsync(100f, releaseMs, options.DuckingPolicy, eventSource, cancellationToken);
    }

    // ⚠ RAISED FOR EVERY SOURCE THAT LEAVES, not only when the set empties. This is the mirror of what
    // PHN-1d did for StartDuckingAsync and it is here for the same reason: a subscriber cannot act on
    // a source ending if it is never told one did. Before this line moved, a priority-8 blocker ending
    // while a priority-5 announcement continued produced NO RAISE AT ALL — so EventPlaybackService's
    // D28 queue would never have been woken and would have expired as Failed/"WaitExpired", which is
    // D28's rejected option delivered thirty seconds late.
    //
    // ⚠ …AND ONLY THOSE. `wasPresent` is what makes "every source that LEAVES" literal, and it is a
    // correctness guard rather than tidiness. A stop for a source that is NOT in the set is reachable:
    // AnnouncementService.CleanupSourceAsync calls StopDuckingAsync unconditionally, so a second stop
    // — or a stop for a source that never started — arrives here, removes nothing, and restores
    // nothing (needsRestore is false, because the set is either already empty with _isDucking false or
    // still non-empty). Raising anyway would emit IsDucking:true alongside ActiveEventCount:0 and
    // DuckLevel:100, which is a shape this tree has never emitted: before PHN-1f the raise lived
    // inside `if (needsRestore)`, so a redundant stop raised nothing at all. This restores exactly
    // that, for exactly those calls.
    //
    // The disjunction is written out rather than collapsed to `wasPresent`, the same way
    // StartDuckingAsync writes `needsTransition || wasNewlyAdded`: needsRestore implies wasPresent in
    // every state reachable today — _activeEvents is non-empty only while _isDucking is true, and
    // StopAllDuckingAsync clears both together — but a state where they diverge should still announce
    // the restore rather than swallow it.
    //
    // ⚠ IsDucking is the aggregate AS IT STOOD INSIDE THE LOCK ABOVE: false exactly on the removal
    // that emptied the set, which is what needsRestore means, and true while others remain. It is a
    // SNAPSHOT, not a live read — a StartDuckingAsync landing after that lock is not reflected in it.
    // That is the same pre-existing looseness the ActiveEventCount field has from the other side,
    // since that one IS read live inside RaiseDuckingStateChanged. What the snapshot buys is the thing
    // AudioManager.ClearDuckingMultiplier depends on: raising IsDucking:false while other sources
    // remain would restore the radio to full volume MID-ANNOUNCEMENT, and that hazard is why this
    // needed the Transition field before it could be done at all.
    //
    // ⚠ PLACED AFTER the fade block, not inside it, so the emptying case still raises AFTER
    // ApplyFadeAsync — byte-identical timing to what AudioManager's "Ducking ended" line has always
    // had. Only the non-emptying case is new, and it has no fade to wait for.
    if (wasPresent || needsRestore)
    {
      RaiseDuckingStateChanged(
        !needsRestore, eventSource, DuckingSourceTransition.Ended, priorityBeforeRemoval);
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

    RaiseDuckingStateChanged(false, null, DuckingSourceTransition.AllCleared, 0);
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
  /// ⚠ Guarded because ADR-029 D5 makes this event load-bearing: EventPlaybackService subscribes to it
  /// to preempt attended playback, and it is the first subscriber that DOES WORK the caller depends on.
  /// (Not the first that can throw — AudioManager's handler calls ClearDuckingMultiplier on the
  /// IsDucking:false arm, and SoundFlowPlaybackService.ClearDuckingMultiplier opens with
  /// ThrowIfDisposed, which is reachable at shutdown. The stronger sentence was the wrong one.)
  /// Unguarded, that exception propagates out of StartDuckingAsync into whichever event path called it.
  /// Traced against the tree: AnnounceAsync, PlaySoundWithAnnouncementAsync and
  /// EventPlaybackService.AcquireAndPlayAsync all catch and then restore ducking, so the cost is NOT
  /// stuck ducking — it is an announcement that never plays while POST /api/notifications/announce
  /// still reports 200. Silent to the CALLER, not to the log: AnnounceAsync logs it at Error, which
  /// survives the LOG-11 journal filter.
  ///
  /// This catches; it does not resume the invocation list. A handler that throws still prevents the
  /// handlers registered after it from running. That is accepted for two subscribers; anything more
  /// would want a GetInvocationList loop and a reason.
  ///
  /// RaiseDuckingLevelChanged is deliberately NOT given the same guard: it gains no new subscriber in
  /// this PR and it fires once per fade step, so a try inside that loop would buy nothing that exists.
  /// </remarks>
  private void RaiseDuckingStateChanged(
    bool isDucking,
    IEventAudioSource? triggeringSource,
    DuckingSourceTransition transition,
    int triggeringSourcePriority)
  {
    var args = new DuckingStateChangedEventArgs
    {
      IsDucking = isDucking,
      TriggeringSource = triggeringSource,
      DuckLevel = CurrentDuckLevel,
      ActiveEventCount = ActiveEventCount,
      Transition = transition,
      TriggeringSourcePriority = triggeringSourcePriority
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
