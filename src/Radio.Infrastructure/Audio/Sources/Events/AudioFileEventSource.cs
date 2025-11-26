using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Audio file event source for notifications, doorbell sounds, etc.
/// Plays a single audio file as an ephemeral event.
/// </summary>
public class AudioFileEventSource : EventAudioSourceBase
{
  private readonly string _filePath;
  private readonly TimeSpan _duration;
  private readonly string _name;
  private Stream? _audioStream;
  private CancellationTokenSource? _playbackCts;

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioFileEventSource"/> class.
  /// </summary>
  /// <param name="filePath">The path to the audio file.</param>
  /// <param name="duration">The duration of the audio file.</param>
  /// <param name="logger">The logger instance.</param>
  public AudioFileEventSource(
    string filePath,
    TimeSpan duration,
    ILogger<AudioFileEventSource> logger)
    : base(logger)
  {
    _filePath = filePath;
    _duration = duration;
    _name = $"Event: {Path.GetFileName(filePath)}";
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioFileEventSource"/> class
  /// with a pre-loaded audio stream.
  /// </summary>
  /// <param name="name">The display name for the event.</param>
  /// <param name="audioStream">The pre-loaded audio stream.</param>
  /// <param name="duration">The duration of the audio.</param>
  /// <param name="logger">The logger instance.</param>
  public AudioFileEventSource(
    string name,
    Stream audioStream,
    TimeSpan duration,
    ILogger<AudioFileEventSource> logger)
    : base(logger)
  {
    _filePath = string.Empty;
    _audioStream = audioStream;
    _duration = duration;
    _name = $"Event: {name}";
  }

  /// <inheritdoc/>
  public override string Name => _name;

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.AudioFileEvent;

  /// <inheritdoc/>
  public override TimeSpan Duration => _duration;

  /// <summary>
  /// Gets the path to the audio file.
  /// </summary>
  public string FilePath => _filePath;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    // In a full implementation, this would return the SoundFlow node
    // For now, return the audio stream or a placeholder object
    return _audioStream ?? (object)_filePath;
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    try
    {
      // Load the file if not already loaded as a stream
      if (_audioStream == null && !string.IsNullOrEmpty(_filePath))
      {
        if (!File.Exists(_filePath))
        {
          throw new FileNotFoundException($"Audio file not found: {_filePath}");
        }

        _audioStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Logger.LogDebug("Loaded audio file: {FilePath}", _filePath);
      }

      // In a full implementation, we would create a SoundFlow audio node here
      State = AudioSourceState.Ready;
      Logger.LogInformation("Audio file event source initialized: {Name}", _name);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize audio file event source");
      State = AudioSourceState.Error;
      throw;
    }
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    Logger.LogDebug("Playing audio file event: {Name}", _name);

    try
    {
      // Reset stream position if possible
      if (_audioStream?.CanSeek == true)
      {
        _audioStream.Position = 0;
      }

      // In a full implementation, this would start playback through SoundFlow
      // For now, simulate playback by waiting for the duration

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
      Logger.LogError(ex, "Error during audio file event playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }
  }

  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Stopping audio file event playback");
    _playbackCts?.Cancel();
    OnPlaybackCompleted(PlaybackCompletionReason.UserStopped);
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override ValueTask DisposeAsyncCore()
  {
    Logger.LogDebug("Disposing audio file event source");
    _playbackCts?.Cancel();
    _playbackCts?.Dispose();
    _audioStream?.Dispose();
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc/>
  protected override void OnVolumeChanged(float volume)
  {
    // In a full implementation, apply volume to the sound component
    Logger.LogDebug("Audio file event volume changed to {Volume}", volume);
  }
}
