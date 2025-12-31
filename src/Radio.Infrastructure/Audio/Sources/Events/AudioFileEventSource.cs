using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Sources.Events;

/// <summary>
/// Audio file event source for notifications, doorbell sounds, etc.
/// Plays a single audio file as an ephemeral event using SoundFlow.
/// </summary>
public class AudioFileEventSource : EventAudioSourceBase
{
  private readonly string _filePath;
  private readonly TimeSpan _duration;
  private readonly string _name;
  private readonly SoundFlowPlaybackService? _playbackService;
  private Stream? _audioStream;
  private CancellationTokenSource? _playbackCts;
  private Task? _playbackTask;
  private string? _playbackId;
  private bool _isPlaybackActive;

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioFileEventSource"/> class.
  /// </summary>
  /// <param name="filePath">The path to the audio file.</param>
  /// <param name="duration">The duration of the audio file.</param>
  /// <param name="logger">The logger instance.</param>
  /// <param name="playbackService">Optional SoundFlow playback service.</param>
  public AudioFileEventSource(
    string filePath,
    TimeSpan duration,
    ILogger<AudioFileEventSource> logger,
    SoundFlowPlaybackService? playbackService = null)
    : base(logger)
  {
    _filePath = filePath;
    _duration = duration;
    _name = $"Event: {Path.GetFileName(filePath)}";
    _playbackService = playbackService;
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="AudioFileEventSource"/> class
  /// with a pre-loaded audio stream.
  /// </summary>
  /// <param name="name">The display name for the event.</param>
  /// <param name="audioStream">The pre-loaded audio stream.</param>
  /// <param name="duration">The duration of the audio.</param>
  /// <param name="logger">The logger instance.</param>
  /// <param name="playbackService">Optional SoundFlow playback service.</param>
  public AudioFileEventSource(
    string name,
    Stream audioStream,
    TimeSpan duration,
    ILogger<AudioFileEventSource> logger,
    SoundFlowPlaybackService? playbackService = null)
    : base(logger)
  {
    _filePath = string.Empty;
    _audioStream = audioStream;
    _duration = duration;
    _name = $"Event: {name}";
    _playbackService = playbackService;
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
    // Return the playback ID for mixer integration, or fallback to stream/path
    if (!string.IsNullOrEmpty(_playbackId))
    {
      return _playbackId;
    }
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

      // Generate a unique playback ID
      _playbackId = $"audio-event-{Guid.NewGuid():N}";

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
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
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

      // Start playback through SoundFlow if available
      if (_playbackService != null && _playbackId != null)
      {
        _playbackTask = PlayWithSoundFlowAsync(_playbackCts.Token);
      }
      else
      {
        // Fallback: simulate playback by waiting for the duration
        Logger.LogDebug("SoundFlow playback service not available, using simulation");
        _playbackTask = PlaybackLoopAsync(_playbackCts.Token);
      }
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error during audio file event playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }

    return Task.CompletedTask;
  }

  private async Task PlayWithSoundFlowAsync(CancellationToken cancellationToken)
  {
    try
    {
      bool success;
      if (!string.IsNullOrEmpty(_filePath))
      {
        // Play from file path
        success = await _playbackService!.PlayFileAsync(
          _playbackId!,
          _filePath,
          Volume,
          cancellationToken);
      }
      else if (_audioStream != null)
      {
        // Play from stream
        success = await _playbackService!.PlayStreamAsync(
          _playbackId!,
          _audioStream,
          Volume,
          cancellationToken);
      }
      else
      {
        Logger.LogError("No audio file or stream available for playback");
        State = AudioSourceState.Error;
        OnPlaybackCompleted(PlaybackCompletionReason.Error, new InvalidOperationException("No audio source"));
        return;
      }

      if (!success)
      {
        Logger.LogError("Failed to start SoundFlow playback");
        State = AudioSourceState.Error;
        OnPlaybackCompleted(PlaybackCompletionReason.Error, new InvalidOperationException("Playback failed"));
        return;
      }

      _isPlaybackActive = true;

      // Wait for playback to complete (based on duration)
      // In a full implementation, we would listen for playback end events from SoundFlow
      await Task.Delay(_duration, cancellationToken);

      if (!cancellationToken.IsCancellationRequested)
      {
        _isPlaybackActive = false;
        State = AudioSourceState.Stopped;
        OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
      }
    }
    catch (OperationCanceledException)
    {
      // Playback was stopped
      _isPlaybackActive = false;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error during SoundFlow playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }
  }

  private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
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

  /// <inheritdoc/>
  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Stopping audio file event playback");
    _playbackCts?.Cancel();

    // Stop SoundFlow playback if active
    if (_playbackService != null && _playbackId != null && _isPlaybackActive)
    {
      await _playbackService.StopAsync(_playbackId, cancellationToken);
      _isPlaybackActive = false;
    }

    // Wait for the playback task to complete
    if (_playbackTask != null)
    {
      try
      {
        await _playbackTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
      }
      catch (TimeoutException)
      {
        Logger.LogWarning("Playback task did not complete within timeout");
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
    Logger.LogDebug("Disposing audio file event source");
    _playbackCts?.Cancel();
    _playbackCts?.Dispose();

    // Stop SoundFlow playback if active
    if (_playbackService != null && _playbackId != null && _isPlaybackActive)
    {
      await _playbackService.StopAsync(_playbackId);
      _isPlaybackActive = false;
    }

    _audioStream?.Dispose();
  }

  /// <inheritdoc/>
  protected override void OnVolumeChanged(float volume)
  {
    // Apply volume to SoundFlow playback if active
    if (_playbackService != null && _playbackId != null && _isPlaybackActive)
    {
      _playbackService.SetVolume(_playbackId, volume);
      Logger.LogDebug("Audio file event volume changed to {Volume}", volume);
    }
    else
    {
      Logger.LogDebug("Audio file event volume changed to {Volume} (not yet playing)", volume);
    }
  }
}
