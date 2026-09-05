using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Service for playing TTS announcements with audio ducking.
/// Shared by phone call integration and notification endpoints.
/// Creates TTS via ITTSFactory, manages ducking via IDuckingService,
/// and can chain a sound file before the TTS announcement.
/// </summary>
public class AnnouncementService : IAnnouncementService
{
  private readonly ILogger<AnnouncementService> _logger;
  private readonly ITTSFactory _ttsFactory;
  private readonly IDuckingService _duckingService;
  private readonly AudioFileEventSourceFactory _audioFileFactory;
  private readonly object _lock = new();
  private IEventAudioSource? _activeSource;
  private CancellationTokenSource? _activeCts;

  public AnnouncementService(
    ILogger<AnnouncementService> logger,
    ITTSFactory ttsFactory,
    IDuckingService duckingService,
    AudioFileEventSourceFactory audioFileFactory)
  {
    _logger = logger;
    _ttsFactory = ttsFactory;
    _duckingService = duckingService;
    _audioFileFactory = audioFileFactory;
  }

  /// <inheritdoc />
  public async Task AnnounceAsync(string message, int priority = 5, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(message);
    priority = Math.Clamp(priority, 1, 10);

    _logger.LogInformation("Announcing: {Message} (priority {Priority})",
      LogSafeText.For(message), priority);

    IEventAudioSource? ttsSource = null;
    try
    {
      // Create TTS event source
      ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: cancellationToken);
      _duckingService.SetPriority(ttsSource, priority);

      SetActiveSource(ttsSource, cancellationToken);

      // Start ducking, play TTS, stop ducking
      await _duckingService.StartDuckingAsync(ttsSource, cancellationToken);

      var completionTcs = new TaskCompletionSource<bool>();
      ttsSource.PlaybackCompleted += (_, _) => completionTcs.TrySetResult(true);

      await ttsSource.PlayAsync(cancellationToken);

      // Wait for playback to complete or cancellation
      using var reg = cancellationToken.Register(() => completionTcs.TrySetCanceled());
      await completionTcs.Task;

      _logger.LogDebug("Announcement playback completed");
    }
    catch (OperationCanceledException)
    {
      _logger.LogDebug("Announcement cancelled");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during announcement");
    }
    finally
    {
      if (ttsSource != null)
      {
        await CleanupSourceAsync(ttsSource);
      }
      ClearActiveSource(ttsSource);
    }
  }

  /// <inheritdoc />
  public async Task PlaySoundWithAnnouncementAsync(
    string soundPath, string message, int priority = 5, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(soundPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(message);
    priority = Math.Clamp(priority, 1, 10);

    // soundPath stays as-is: it is a server-side file path chosen by config, not user text.
    _logger.LogInformation("Playing sound {Sound} then announcing: {Message} (priority {Priority})",
      soundPath, LogSafeText.For(message), priority);

    IEventAudioSource? soundSource = null;
    IEventAudioSource? ttsSource = null;
    try
    {
      // Phase 1: Play the sound file with ducking
      soundSource = await _audioFileFactory.CreateFromFileAsync(soundPath, cancellationToken);
      _duckingService.SetPriority(soundSource, priority);

      SetActiveSource(soundSource, cancellationToken);

      await _duckingService.StartDuckingAsync(soundSource, cancellationToken);

      var soundTcs = new TaskCompletionSource<bool>();
      soundSource.PlaybackCompleted += (_, _) => soundTcs.TrySetResult(true);

      await soundSource.PlayAsync(cancellationToken);

      using (var reg = cancellationToken.Register(() => soundTcs.TrySetCanceled()))
      {
        await soundTcs.Task;
      }

      await CleanupSourceAsync(soundSource);
      ClearActiveSource(soundSource);
      soundSource = null;

      // Phase 2: Play the TTS announcement (ducking still active from same priority level)
      ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: cancellationToken);
      _duckingService.SetPriority(ttsSource, priority);

      SetActiveSource(ttsSource, cancellationToken);

      await _duckingService.StartDuckingAsync(ttsSource, cancellationToken);

      var ttsTcs = new TaskCompletionSource<bool>();
      ttsSource.PlaybackCompleted += (_, _) => ttsTcs.TrySetResult(true);

      await ttsSource.PlayAsync(cancellationToken);

      using (var reg = cancellationToken.Register(() => ttsTcs.TrySetCanceled()))
      {
        await ttsTcs.Task;
      }

      _logger.LogDebug("Sound + announcement playback completed");
    }
    catch (OperationCanceledException)
    {
      _logger.LogDebug("Sound + announcement cancelled");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during sound + announcement");
    }
    finally
    {
      if (soundSource != null)
      {
        await CleanupSourceAsync(soundSource);
      }
      if (ttsSource != null)
      {
        await CleanupSourceAsync(ttsSource);
      }
      ClearActiveSource(ttsSource ?? soundSource);
    }
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    IEventAudioSource? source;
    CancellationTokenSource? cts;

    lock (_lock)
    {
      source = _activeSource;
      cts = _activeCts;
    }

    if (cts != null)
    {
      await cts.CancelAsync();
    }

    if (source != null)
    {
      await CleanupSourceAsync(source);
      ClearActiveSource(source);
    }

    _logger.LogDebug("Announcement stopped");
  }

  private void SetActiveSource(IEventAudioSource source, CancellationToken externalToken)
  {
    lock (_lock)
    {
      _activeCts?.Cancel();
      _activeCts?.Dispose();
      _activeCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
      _activeSource = source;
    }
  }

  private void ClearActiveSource(IEventAudioSource? expected)
  {
    lock (_lock)
    {
      if (_activeSource == expected)
      {
        _activeSource = null;
        _activeCts?.Dispose();
        _activeCts = null;
      }
    }
  }

  private async Task CleanupSourceAsync(IEventAudioSource source)
  {
    try
    {
      await _duckingService.StopDuckingAsync(source);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping ducking for announcement");
    }

    try
    {
      await source.StopAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping announcement source");
    }

    try
    {
      await source.DisposeAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error disposing announcement source");
    }
  }
}
