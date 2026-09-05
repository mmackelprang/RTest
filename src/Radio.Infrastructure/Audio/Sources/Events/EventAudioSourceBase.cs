using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Base abstract class for event audio sources.
/// Extends <see cref="AudioSourceBase"/> with event-specific category and log level.
/// </summary>
public abstract class EventAudioSourceBase : AudioSourceBase, IEventAudioSource
{
  /// <summary>
  /// Initializes a new instance of the <see cref="EventAudioSourceBase"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  protected EventAudioSourceBase(ILogger logger) : base(logger)
  {
  }

  /// <inheritdoc/>
  public override AudioSourceCategory Category => AudioSourceCategory.Event;

  /// <inheritdoc/>
  public abstract TimeSpan Duration { get; }

  /// <inheritdoc/>
  /// <remarks>
  /// Defaults to zero. An implementer that can report a real position overrides this — both shipped
  /// event sources now do, AudioFileEventSource and, since PHN-2, TTSEventSource.
  /// </remarks>
  public virtual TimeSpan Position => TimeSpan.Zero;

  /// <inheritdoc/>
  /// <remarks>
  /// Defaults to FALSE, and the default is the honest one: SeekAsync throws NotSupportedException
  /// unless an implementer both overrides this to true AND overrides SeekCoreAsync. A source that
  /// claims IsSeekable without repositioning any audio is the exact defect CLAUDE.md's pre-merge
  /// rule exists for.
  /// </remarks>
  public virtual bool IsSeekable => false;

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
  public virtual async Task SeekAsync(
    TimeSpan position,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (!IsSeekable)
    {
      throw new NotSupportedException($"Audio source {Id} does not support seeking.");
    }

    await SeekCoreAsync(position, cancellationToken);
  }

  /// <summary>
  /// Implementation hook for <see cref="PauseAsync"/>. Called only when the source is Playing.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected virtual Task PauseCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  /// <summary>
  /// Implementation hook for <see cref="ResumeAsync"/>. Called only when the source is Paused.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected virtual Task ResumeCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  /// <summary>
  /// Implementation hook for <see cref="SeekAsync"/>. Called only when <see cref="IsSeekable"/>.
  /// </summary>
  /// <param name="position">The position to seek to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  protected virtual Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
    => throw new NotSupportedException($"Audio source {Id} does not support seeking.");

  /// <summary>
  /// Logs state changes at Debug level for event sources.
  /// </summary>
  protected override void LogStateChange(AudioSourceState previousState, AudioSourceState newState)
  {
    Logger.LogDebug("Event audio source {Id} state changed from {PreviousState} to {NewState}",
      Id, previousState, newState);
  }
}
