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
/// <remarks>
/// One announcement speaks at a time: a newer one replaces the one speaking, which returns
/// <see cref="AnnouncementOutcome.Interrupted"/> (AUD-73). See <see cref="BecomeActive"/>.
/// </remarks>
public class AnnouncementService : IAnnouncementService
{
  private readonly ILogger<AnnouncementService> _logger;
  private readonly ITTSFactory _ttsFactory;
  private readonly IDuckingService _duckingService;
  private readonly AudioFileEventSourceFactory _audioFileFactory;
  private readonly object _lock = new();

  // The announcement currently speaking, the CancellationTokenSource its own AnnounceAsync /
  // PlaySoundWithAnnouncementAsync call waits on, and the order in which that call ARRIVED. The three
  // fields are guarded by _lock. The CTS is owned (created and disposed) by the call that registered
  // it; other code only cancels it (AUD-73).
  private IEventAudioSource? _activeSource;
  private CancellationTokenSource? _activeCts;
  private long _activeArrival;
  private long _arrivalCounter;

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
  public async Task<AnnouncementOutcome> AnnounceAsync(string message, int priority = 5, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(message);
    priority = Math.Clamp(priority, 1, 10);

    _logger.LogInformation("Announcing: {Message} (priority {Priority})",
      LogSafeText.For(message), priority);

    // AUD-73: a newer announcement replaces this one. The arrival number is taken before any await,
    // so "newer" means "asked for later", not "finished synthesising first": a long message whose TTS
    // takes longer must not cut off a short one requested after it.
    var arrival = Interlocked.Increment(ref _arrivalCounter);

    // Linked to the caller's token, and the token this call actually waits on. It is cancelled by the
    // caller, by StopAsync, or by a newer announcement taking over (BecomeActive).
    using var announcementCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var token = announcementCts.Token;

    IEventAudioSource? ttsSource = null;
    try
    {
      // Create TTS event source
      ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: token);
      _duckingService.SetPriority(ttsSource, priority);

      // Duck BEFORE taking over. The announcement being replaced releases its own duck registration
      // as it unwinds; had that release landed before this one registered, the active-event count
      // would touch zero and the music would swell up and back down between the two voices (the
      // swell AUD-74 removed). Registered first, the count goes 1 -> 2 -> 1.
      await _duckingService.StartDuckingAsync(ttsSource, token);

      if (!BecomeActive(ttsSource, announcementCts, arrival))
      {
        _logger.LogInformation("Announcement not played: a newer announcement is already speaking");
        return AnnouncementOutcome.Interrupted;
      }

      // The REASON is kept, not just the fact of completion (TTS-2). A source that fails to start
      // raises PlaybackCompleted(Error) — TTSEventSource does so synchronously inside PlayAsync when
      // there is no playback device — and treating every completion as success is how a failed
      // announcement came back as 200 "Announcement played". RunContinuationsAsynchronously because
      // the token registration below can complete it on a newer announcement's thread, and this
      // method's unwinding must not run there.
      var completionTcs = new TaskCompletionSource<PlaybackCompletionReason>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      ttsSource.PlaybackCompleted += (_, e) => completionTcs.TrySetResult(e.Reason);

      await ttsSource.PlayAsync(token);

      // Wait for playback to complete or cancellation
      using var reg = token.Register(() => completionTcs.TrySetCanceled());
      var reason = await completionTcs.Task;

      var outcome = reason switch
      {
        PlaybackCompletionReason.EndOfContent => AnnouncementOutcome.Completed,
        PlaybackCompletionReason.Error => AnnouncementOutcome.Failed,
        _ => AnnouncementOutcome.Interrupted
      };

      if (outcome == AnnouncementOutcome.Failed)
      {
        // Warning, not Error: the cause was already logged at Error by whichever layer failed.
        _logger.LogWarning("Announcement playback failed to start or finish");
      }
      else
      {
        _logger.LogDebug("Announcement playback ended: {Outcome}", outcome);
      }

      return outcome;
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
      // Only a cancellation of THIS announcement's token is an interruption: the caller's, StopAsync's
      // or a newer announcement's. Any other OperationCanceledException — above all
      // TaskCanceledException from TTSFactory's HttpClient timeout — is a failure and falls through to
      // the arm below, logged at Error (TTS-2 review).
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogDebug("Announcement cancelled");
      }
      else
      {
        _logger.LogInformation("Announcement interrupted by a newer announcement or a stop");
      }
      return AnnouncementOutcome.Interrupted;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during announcement");
      return AnnouncementOutcome.Failed;
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

    // soundPath stays as-is: it is a server-side file path chosen by config, not user text. Its
    // single quotes stay too — a path can contain spaces, and the quotes are what show where it
    // ends. The quotes around {Message} correctly did NOT come back: a token is not a
    // human-readable string, and framing it as one invites reading it as the message.
    _logger.LogInformation("Playing sound '{Sound}' then announcing: {Message} (priority {Priority})",
      soundPath, LogSafeText.For(message), priority);

    // AUD-73: the same replaced-by-newer rule as AnnounceAsync, with one arrival and one token for both
    // phases. The ring and the name that follows it are one announcement.
    var arrival = Interlocked.Increment(ref _arrivalCounter);
    using var announcementCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var token = announcementCts.Token;

    IEventAudioSource? soundSource = null;
    IEventAudioSource? ttsSource = null;
    try
    {
      // Phase 1: Play the sound file with ducking
      soundSource = await _audioFileFactory.CreateFromFileAsync(soundPath, token);
      _duckingService.SetPriority(soundSource, priority);

      // Duck before taking over, for the reason given in AnnounceAsync.
      await _duckingService.StartDuckingAsync(soundSource, token);

      if (!BecomeActive(soundSource, announcementCts, arrival))
      {
        _logger.LogInformation("Sound + announcement not played: a newer announcement is already speaking");
        return;
      }

      var soundTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      soundSource.PlaybackCompleted += (_, _) => soundTcs.TrySetResult(true);

      await soundSource.PlayAsync(token);

      using (var reg = token.Register(() => soundTcs.TrySetCanceled()))
      {
        await soundTcs.Task;
      }

      await CleanupSourceAsync(soundSource);
      ClearActiveSource(soundSource);
      soundSource = null;

      // Phase 2: Play the TTS announcement (ducking still active from same priority level)
      ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: token);
      _duckingService.SetPriority(ttsSource, priority);

      await _duckingService.StartDuckingAsync(ttsSource, token);

      if (!BecomeActive(ttsSource, announcementCts, arrival))
      {
        _logger.LogInformation("Announcement after sound not played: a newer announcement is already speaking");
        return;
      }

      var ttsTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      ttsSource.PlaybackCompleted += (_, _) => ttsTcs.TrySetResult(true);

      await ttsSource.PlayAsync(token);

      using (var reg = token.Register(() => ttsTcs.TrySetCanceled()))
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

    // Wakes the announcing call, which then unwinds and runs its own cleanup as well.
    CancelQuietly(cts);

    if (source != null)
    {
      await CleanupSourceAsync(source);
      ClearActiveSource(source);
    }

    _logger.LogDebug("Announcement stopped");
  }

  /// <summary>
  /// Makes <paramref name="source"/> the active announcement and cancels the one it replaces, unless
  /// the active announcement ARRIVED later than this one. In that case nothing changes and this
  /// returns false: the caller was superseded before it spoke and must not play.
  /// </summary>
  /// <remarks>
  /// AUD-73. Before this, the method cancelled a token that no playback path waited on, so a second
  /// announcement played on top of the first. Measured on the box 2026-09-30 on <c>af9bc2b</c>: two
  /// requests 1.5 s apart both returned "completed", with two events ducked at once for 1.7 s. The
  /// cancelled token is now the one the replaced call is awaiting, and that call unwinds through its
  /// own cleanup (stop ducking, stop, dispose). Priority is not consulted: the newest announcement
  /// wins, the same replace rule ADR-029 §6.2 set for attended playback.
  /// </remarks>
  private bool BecomeActive(IEventAudioSource source, CancellationTokenSource cts, long arrival)
  {
    CancellationTokenSource? replaced = null;
    lock (_lock)
    {
      if (_activeSource != null && _activeArrival > arrival)
      {
        return false;
      }

      // Phase 2 of PlaySoundWithAnnouncementAsync registers again under its own CTS; it must not
      // cancel itself.
      if (!ReferenceEquals(_activeCts, cts))
      {
        replaced = _activeCts;
      }

      _activeSource = source;
      _activeCts = cts;
      _activeArrival = arrival;
    }

    // Outside the lock: Cancel runs the replaced call's token callbacks on this thread.
    CancelQuietly(replaced);
    return true;
  }

  private void ClearActiveSource(IEventAudioSource? expected)
  {
    lock (_lock)
    {
      if (expected != null && _activeSource == expected)
      {
        _activeSource = null;
        _activeCts = null;
        _activeArrival = 0;
      }
    }
  }

  /// <summary>
  /// Cancels a CTS captured from <c>_activeCts</c>. Its owner disposes it when its call returns, and
  /// that can happen between the capture and this call if the announcement ended on its own at the
  /// same moment. Nothing is left to stop then, so the disposal is not an error.
  /// </summary>
  private static void CancelQuietly(CancellationTokenSource? cts)
  {
    if (cts == null)
    {
      return;
    }

    try
    {
      cts.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // Already finished and disposed by its owner; see the summary.
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
