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
  public async Task StartDuckingAsync(IEventAudioSource eventSource, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(eventSource);
    ObjectDisposedException.ThrowIf(_disposed, this);

    var options = _audioOptions.CurrentValue;
    bool needsTransition;
    bool wasAlreadyDucking;

    lock (_lock)
    {
      wasAlreadyDucking = _isDucking;
      needsTransition = !_isDucking;

      // Add to active events
      if (!_activeEvents.ContainsKey(eventSource.Id))
      {
        _activeEvents[eventSource.Id] = eventSource;
        _logger.LogDebug(
          "Added event source '{SourceId}' to ducking queue. Active events: {Count}",
          eventSource.Id, _activeEvents.Count);
      }

      if (!_isDucking)
      {
        _isDucking = true;
      }
    }

    if (needsTransition)
    {
      var targetLevel = options.DuckingPercentage;
      var attackMs = options.DuckingAttackMs;

      _logger.LogInformation(
        "Starting ducking: target level {TargetLevel}%, attack time {AttackMs}ms, policy {Policy}",
        targetLevel, attackMs, options.DuckingPolicy);

      await ApplyFadeAsync(targetLevel, attackMs, options.DuckingPolicy, eventSource, cancellationToken);

      RaiseDuckingStateChanged(true, eventSource);
    }
    else if (wasAlreadyDucking)
    {
      _logger.LogDebug(
        "Already ducking for source '{SourceId}'. Active events: {Count}",
        eventSource.Id, _activeEvents.Count);
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
  private void RaiseDuckingStateChanged(bool isDucking, IEventAudioSource? triggeringSource)
  {
    var args = new DuckingStateChangedEventArgs
    {
      IsDucking = isDucking,
      TriggeringSource = triggeringSource,
      DuckLevel = CurrentDuckLevel,
      ActiveEventCount = ActiveEventCount
    };

    DuckingStateChanged?.Invoke(this, args);
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
