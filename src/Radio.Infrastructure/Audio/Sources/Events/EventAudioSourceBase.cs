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

  /// <summary>
  /// Logs state changes at Debug level for event sources.
  /// </summary>
  protected override void LogStateChange(AudioSourceState previousState, AudioSourceState newState)
  {
    Logger.LogDebug("Event audio source {Id} state changed from {PreviousState} to {NewState}",
      Id, previousState, newState);
  }
}
