using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Text-to-Speech event audio source.
/// Generates speech audio from text using a TTS engine.
/// </summary>
public class TTSEventSource : EventAudioSourceBase
{
  private readonly string _text;
  private readonly TTSParameters _parameters;
  private readonly Stream _audioStream;
  private readonly TimeSpan _duration;
  private readonly string _name;
  private CancellationTokenSource? _playbackCts;

  /// <summary>
  /// Initializes a new instance of the <see cref="TTSEventSource"/> class.
  /// </summary>
  /// <param name="text">The text that was converted to speech.</param>
  /// <param name="parameters">The TTS parameters used for generation.</param>
  /// <param name="audioStream">The generated audio stream.</param>
  /// <param name="duration">The duration of the audio.</param>
  /// <param name="logger">The logger instance.</param>
  internal TTSEventSource(
    string text,
    TTSParameters parameters,
    Stream audioStream,
    TimeSpan duration,
    ILogger<TTSEventSource> logger)
    : base(logger)
  {
    _text = text;
    _parameters = parameters;
    _audioStream = audioStream;
    _duration = duration;

    // Create a truncated name for display
    var truncatedText = text.Length > 50 ? text[..47] + "..." : text;
    _name = $"TTS: {truncatedText}";
  }

  /// <inheritdoc/>
  public override string Name => _name;

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.TTS;

  /// <inheritdoc/>
  public override TimeSpan Duration => _duration;

  /// <summary>
  /// Gets the original text that was converted to speech.
  /// </summary>
  public string Text => _text;

  /// <summary>
  /// Gets the TTS parameters used for generation.
  /// </summary>
  public TTSParameters Parameters => _parameters;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    // In a full implementation, this would return the SoundFlow node
    // For now, return the audio stream
    return _audioStream;
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    try
    {
      // Reset stream position if possible
      if (_audioStream.CanSeek)
      {
        _audioStream.Position = 0;
      }

      // In a full implementation, we would create a SoundFlow audio node here
      // For now, we just mark as ready
      State = AudioSourceState.Ready;
      Logger.LogInformation("TTS event source initialized: {Text}", _text);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize TTS event source");
      State = AudioSourceState.Error;
      throw;
    }
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    Logger.LogDebug("Playing TTS audio: {Text}", _text);

    try
    {
      // In a full implementation, this would start playback through SoundFlow
      // For now, simulate playback by waiting for the duration
      // (In production, actual audio playback would occur here)

      // Start a task that will signal completion after the duration
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(_duration, _playbackCts.Token);
          if (!_playbackCts.IsCancellationRequested)
          {
            OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
          }
        }
        catch (OperationCanceledException)
        {
          // Playback was stopped
        }
      }, _playbackCts.Token);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error during TTS playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }
  }

  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Stopping TTS playback");
    _playbackCts?.Cancel();
    OnPlaybackCompleted(PlaybackCompletionReason.UserStopped);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override ValueTask DisposeAsyncCore()
  {
    Logger.LogDebug("Disposing TTS event source");
    _playbackCts?.Cancel();
    _playbackCts?.Dispose();
    _audioStream.Dispose();
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc/>
  protected override void OnVolumeChanged(float volume)
  {
    // In a full implementation, apply volume to the sound component
    Logger.LogDebug("TTS volume changed to {Volume}", volume);
  }
}
