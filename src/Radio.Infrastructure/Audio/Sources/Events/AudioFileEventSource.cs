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
  private CancellationTokenSource _transportCts = new();
  private readonly object _transportLock = new();

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

  /// <inheritdoc/>
  /// <remarks>
  /// Read from the player rather than tracked here, so it stays correct across a seek. Falls back
  /// to zero when there is no player — which is also the state before playback starts.
  /// </remarks>
  public override TimeSpan Position =>
    _playbackService is not null && _playbackId is not null
      ? _playbackService.GetPosition(_playbackId) ?? TimeSpan.Zero
      : TimeSpan.Zero;

  /// <inheritdoc/>
  /// <remarks>
  /// True only on the file-path arm with a live playback service. The stream constructor is
  /// excluded deliberately: SoundFlow's StreamDataProvider is built over whatever stream it is
  /// handed, and a non-seekable stream would make Seek report false at runtime. Claiming
  /// IsSeekable and then failing is worse than reporting false.
  /// </remarks>
  public override bool IsSeekable =>
    _playbackService is not null
    && _playbackId is not null
    && !string.IsNullOrEmpty(_filePath);

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
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
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

      // Wait for the content to finish. Position-driven and re-armed by transport, so a seek or
      // a pause does not make completion fire at the wrong time (ADR-029 §14 Q4).
      await AwaitCompletionAsync(cancellationToken);

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
  protected override Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    if (position < TimeSpan.Zero || position > _duration)
    {
      throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
    }

    var moved = _playbackService!.Seek(_playbackId!, position);
    if (!moved)
    {
      Logger.LogWarning(
        "Seek to {Position} was refused by the player for {Name}", position, _name);
    }

    // Re-arm the completion wait either way: on a refusal nothing moved, so recomputing simply
    // restores the same deadline.
    SignalTransportChange();
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (_playbackService is not null && _playbackId is not null)
    {
      _playbackService.Pause(_playbackId);
    }

    // The completion wait must stop counting: a paused source consumes no audio, so it has no
    // deadline. Without this, a pause longer than the remaining audio fires EndOfContent on a
    // source that is silent and unfinished.
    SignalTransportChange();
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    if (_playbackService is not null && _playbackId is not null)
    {
      _playbackService.Resume(_playbackId);
    }

    SignalTransportChange();
    return Task.CompletedTask;
  }

  /// <summary>
  /// Cancels the current completion wait so it recomputes its deadline from the player's real
  /// position. Called by every transport override. Not a timer and not a poll - it fires only
  /// on a user action.
  /// </summary>
  private void SignalTransportChange()
  {
    CancellationTokenSource previous;
    lock (_transportLock)
    {
      previous = _transportCts;
      _transportCts = new CancellationTokenSource();
    }

    previous.Cancel();
    previous.Dispose();
  }

  /// <summary>
  /// Waits until the content is finished, re-arming whenever transport moves.
  ///
  /// Replaces a single wall-clock Task.Delay(_duration): that delay was correct only for a
  /// playback that is never sought and never paused, which is exactly what ADR-029 stops being
  /// true. If GetPosition yields nothing, remaining falls back to the full duration and the
  /// behaviour is identical to the delay it replaces.
  /// </summary>
  /// <param name="cancellationToken">Cancels the wait outright, e.g. on stop or dispose.</param>
  /// <returns>A task that completes when the content is finished.</returns>
  private async Task AwaitCompletionAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      CancellationTokenSource transport;
      lock (_transportLock)
      {
        transport = _transportCts;
      }

      TimeSpan remaining;
      if (State == AudioSourceState.Paused)
      {
        // No deadline while paused - wait for the next transport event instead.
        remaining = Timeout.InfiniteTimeSpan;
      }
      else
      {
        var position = _playbackService?.GetPosition(_playbackId!) ?? TimeSpan.Zero;
        remaining = _duration - position;
        if (remaining <= TimeSpan.Zero)
        {
          return;
        }
      }

      using var linked =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transport.Token);
      try
      {
        await Task.Delay(remaining, linked.Token);
        return;
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        // A transport event landed. Recompute and wait again.
      }
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
