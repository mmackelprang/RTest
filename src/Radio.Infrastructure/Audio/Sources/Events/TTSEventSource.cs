using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

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
  private readonly SoundFlowPlaybackService? _playbackService;
  private CancellationTokenSource? _playbackCts;
  private Task? _playbackMonitorTask;
  private volatile bool _isPaused;

  /// <summary>
  /// Initializes a new instance of the <see cref="TTSEventSource"/> class.
  /// </summary>
  /// <param name="text">The text that was converted to speech.</param>
  /// <param name="parameters">The TTS parameters used for generation.</param>
  /// <param name="audioStream">The generated audio stream.</param>
  /// <param name="duration">The duration of the audio.</param>
  /// <param name="logger">The logger instance.</param>
  /// <param name="playbackService">Optional playback service for audio output.</param>
  internal TTSEventSource(
    string text,
    TTSParameters parameters,
    Stream audioStream,
    TimeSpan duration,
    ILogger<TTSEventSource> logger,
    SoundFlowPlaybackService? playbackService = null)
    : base(logger)
  {
    _text = text;
    _parameters = parameters;
    _audioStream = audioStream;
    _duration = duration;
    _playbackService = playbackService;

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
    // Return the audio stream for use by external components
    return _audioStream;
  }

  /// <inheritdoc/>
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    try
    {
      // Reset stream position if possible
      if (_audioStream.CanSeek)
      {
        _audioStream.Position = 0;
      }

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
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    Logger.LogDebug("Playing TTS audio: {Text}", _text);

    try
    {
      // Reset stream position before playback
      if (_audioStream.CanSeek)
      {
        _audioStream.Position = 0;
      }

      // Start playback through SoundFlow if available
      if (_playbackService != null)
      {
        // Start playback asynchronously and monitor completion
        _playbackMonitorTask = StartPlaybackWithMonitoringAsync(_playbackCts.Token);
      }
      else
      {
        // Fallback: simulate playback by waiting for the duration
        Logger.LogWarning("No playback service available, simulating TTS playback duration");
        _playbackMonitorTask = SimulatePlaybackAsync(_playbackCts.Token);
      }
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error during TTS playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }

    return Task.CompletedTask;
  }

  private async Task StartPlaybackWithMonitoringAsync(CancellationToken cancellationToken)
  {
    try
    {
      // Start playback through SoundFlow
      var success = await _playbackService!.PlayStreamAsync(
        Id,
        _audioStream,
        Volume,
        cancellationToken);

      if (!success)
      {
        Logger.LogError("Failed to start TTS playback through SoundFlow");
        State = AudioSourceState.Error;
        OnPlaybackCompleted(PlaybackCompletionReason.Error,
          new InvalidOperationException("Failed to start audio playback"));
        return;
      }

      Logger.LogDebug("TTS playback started, monitoring for completion");

      // Monitor playback until completion or cancellation
      // Poll the playback service to check if still playing
      var checkInterval = TimeSpan.FromMilliseconds(100);
      var elapsed = TimeSpan.Zero;

      while (!cancellationToken.IsCancellationRequested)
      {
        if (_isPaused)
        {
          // A paused player reports IsPlaying == false and accrues no audio. Neither the
          // completion check nor the duration safety net applies while paused.
          await Task.Delay(checkInterval, cancellationToken);
          continue;
        }

        // Check if playback is still active
        if (!_playbackService.IsPlaying(Id))
        {
          // Playback finished naturally
          Logger.LogDebug("TTS playback completed naturally after {Elapsed}", elapsed);
          State = AudioSourceState.Stopped;
          OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
          return;
        }

        // Safety check: if we've exceeded expected duration by 50%, stop
        if (elapsed > _duration + TimeSpan.FromSeconds(_duration.TotalSeconds * 0.5 + 1))
        {
          Logger.LogWarning("TTS playback exceeded expected duration, stopping");
          await _playbackService.StopAsync(Id, cancellationToken);
          State = AudioSourceState.Stopped;
          OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
          return;
        }

        await Task.Delay(checkInterval, cancellationToken);
        elapsed += checkInterval;
      }
    }
    catch (OperationCanceledException)
    {
      // Playback was stopped via cancellation
      Logger.LogDebug("TTS playback cancelled");
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error monitoring TTS playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }
  }

  private async Task SimulatePlaybackAsync(CancellationToken cancellationToken)
  {
    try
    {
      await Task.Delay(_duration, cancellationToken);
      if (!cancellationToken.IsCancellationRequested)
      {
        State = AudioSourceState.Stopped;
        OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
      }
    }
    catch (OperationCanceledException)
    {
      // Playback was stopped
    }
  }

  // Seek is deliberately not overridden. IsSeekable stays false from the base, so SeekAsync
  // throws NotSupportedException: seeking inside a spoken message has no user value
  // (ADR-029 §8.3), and a no-op that reported success would be a lie.

  /// <inheritdoc/>
  /// <remarks>
  /// The _isPaused flag is not decoration. StartPlaybackWithMonitoringAsync treats
  /// !IsPlaying(Id) as natural completion, and SoundFlowPlaybackService.IsPlaying is
  /// player.State == PlaybackState.Playing - which a PAUSED player fails. Without this flag a
  /// pause would raise PlaybackCompleted(EndOfContent) and drive the source to Stopped.
  /// </remarks>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    _isPaused = true;
    _playbackService?.Pause(Id);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    _isPaused = false;
    _playbackService?.Resume(Id);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Stopping TTS playback");

    // Cancel the playback monitoring
    _playbackCts?.Cancel();

    // Stop playback through SoundFlow
    if (_playbackService != null)
    {
      try
      {
        await _playbackService.StopAsync(Id, cancellationToken);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "Error stopping TTS playback through SoundFlow");
      }
    }

    // Wait for the monitoring task to complete
    if (_playbackMonitorTask != null)
    {
      try
      {
        await _playbackMonitorTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
      }
      catch (TimeoutException)
      {
        Logger.LogWarning("Playback monitor task did not complete within timeout");
      }
      catch (OperationCanceledException)
      {
        // Expected when cancellation occurs
      }
    }

    OnPlaybackCompleted(PlaybackCompletionReason.UserStopped);
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    Logger.LogDebug("Disposing TTS event source");

    // Stop playback if still running
    if (_playbackService != null && _playbackService.IsPlaying(Id))
    {
      try
      {
        await _playbackService.StopAsync(Id);
      }
      catch (Exception ex)
      {
        Logger.LogWarning(ex, "Error stopping playback during disposal");
      }
    }

    _playbackCts?.Cancel();
    _playbackCts?.Dispose();
    _audioStream.Dispose();
  }

  /// <inheritdoc/>
  protected override void OnVolumeChanged(float volume)
  {
    Logger.LogDebug("TTS volume changed to {Volume}", volume);

    // Apply volume to the playback service
    if (_playbackService != null)
    {
      _playbackService.SetVolume(Id, volume);
    }
  }
}
