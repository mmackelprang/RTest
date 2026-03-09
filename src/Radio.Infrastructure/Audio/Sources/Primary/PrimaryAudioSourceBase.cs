using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Metrics;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Base abstract class for primary audio sources.
/// Extends <see cref="AudioSourceBase"/> with pause/resume, seeking, track navigation,
/// shuffle/repeat support, and playback metrics.
/// </summary>
public abstract class PrimaryAudioSourceBase : AudioSourceBase, IPrimaryAudioSource
{
  private readonly IMetricsCollector? _metricsCollector;

  /// <summary>
  /// Initializes a new instance of the <see cref="PrimaryAudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking playback metrics.</param>
  protected PrimaryAudioSourceBase(ILogger logger, IMetricsCollector? metricsCollector = null)
    : base(logger)
  {
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Gets the metrics collector for derived classes.
  /// </summary>
  protected IMetricsCollector? MetricsCollector => _metricsCollector;

  /// <inheritdoc/>
  public override AudioSourceCategory Category => AudioSourceCategory.Primary;

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

  /// <summary>
  /// Logs state changes at Information level for primary sources.
  /// </summary>
  protected override void LogStateChange(AudioSourceState previousState, AudioSourceState newState)
  {
    Logger.LogInformation("Audio source {Id} state changed from {PreviousState} to {NewState}",
      Id, previousState, newState);
  }

  /// <inheritdoc/>
  public virtual async Task PauseAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (State != AudioSourceState.Playing)
    {
      Logger.LogWarning("Cannot pause {SourceId} - not playing (state: {State})", Id, State);
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
      Logger.LogWarning("Cannot resume {SourceId} - not paused (state: {State})", Id, State);
      return;
    }

    await ResumeCoreAsync(cancellationToken);
    State = AudioSourceState.Playing;
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
  /// <remarks>
  /// Subclasses that set <see cref="SupportsNext"/> to true should override this method.
  /// The default implementation is a no-op for sources that declare support but need no custom logic.
  /// </remarks>
  public virtual Task NextAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsNext)
      throw new NotSupportedException($"Audio source {Id} does not support skipping to next track.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Subclasses that set <see cref="SupportsPrevious"/> to true should override this method.
  /// The default implementation is a no-op for sources that declare support but need no custom logic.
  /// </remarks>
  public virtual Task PreviousAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsPrevious)
      throw new NotSupportedException($"Audio source {Id} does not support going to previous track.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Subclasses that set <see cref="SupportsShuffle"/> to true should override this method.
  /// The default implementation is a no-op for sources that declare support but need no custom logic.
  /// </remarks>
  public virtual Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsShuffle)
      throw new NotSupportedException($"Audio source {Id} does not support shuffle mode.");
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Subclasses that set <see cref="SupportsRepeat"/> to true should override this method.
  /// The default implementation is a no-op for sources that declare support but need no custom logic.
  /// </remarks>
  public virtual Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!SupportsRepeat)
      throw new NotSupportedException($"Audio source {Id} does not support repeat mode.");
    return Task.CompletedTask;
  }

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
  /// Raises the <see cref="AudioSourceBase.PlaybackCompleted"/> event
  /// and tracks metrics for natural completion.
  /// </summary>
  protected override void OnPlaybackCompleted(PlaybackCompletionReason reason, Exception? error = null)
  {
    // Track metrics for natural completion
    if (reason == PlaybackCompletionReason.EndOfContent)
    {
      _metricsCollector?.Increment("audio.songs_played_total");
    }

    base.OnPlaybackCompleted(reason, error);
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
}
