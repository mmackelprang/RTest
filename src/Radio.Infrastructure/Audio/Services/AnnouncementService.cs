using System.Runtime.CompilerServices;
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
/// Overlapping announcements are arbitrated by priority (AUD-73); see <see cref="BecomeActive"/>. An
/// announcement that is stopped or replaced returns <see cref="AnnouncementOutcome.Interrupted"/>.
/// </remarks>
public class AnnouncementService : IAnnouncementService
{
  private readonly ILogger<AnnouncementService> _logger;
  private readonly ITTSFactory _ttsFactory;
  private readonly IDuckingService _duckingService;
  private readonly AudioFileEventSourceFactory _audioFileFactory;
  private readonly object _lock = new();

  // One entry per announcement call that has become active and not yet returned; guarded by _lock.
  // More than one is present only while a lower-priority announcement plays alongside a higher one.
  private readonly List<Registration> _active = [];
  private long _arrivalCounter;

  // Bumped by StopAsync. A call registered under an older value was asked for before the stop and must
  // not start playing after it: this is what lets a stop reach a call still synthesising or ducking.
  private long _stopGeneration;

  // Sources whose cleanup has started. StopAsync and the announcing call can both reach the same
  // source; the second to arrive must not stop or dispose it again. Weak, so nothing is retained.
  private readonly ConditionalWeakTable<IEventAudioSource, object> _cleanedUp = new();

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

    // AUD-73. The arrival number is taken before any await, so "newer" means "asked for later", not
    // "finished synthesising first": a long message whose TTS takes longer must not cut off a short
    // one of the same priority requested after it. The CTS is linked to the caller's token and is the
    // token this call actually waits on; StopAsync and any announcement that outranks this one cancel it.
    using var announcementCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var registration = NewRegistration(announcementCts, priority);
    var token = announcementCts.Token;

    IEventAudioSource? ttsSource = null;
    try
    {
      // Create TTS event source
      ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: token);

      // Checked before ducking as well as in BecomeActive: an announcement that will not play must not
      // raise DuckingStateChanged(Started), which EventPlaybackService reads as a preemption signal.
      if (IsSuperseded(registration))
      {
        _logger.LogInformation("Announcement not played: superseded by a newer one of the same priority, or stopped");
        return AnnouncementOutcome.Interrupted;
      }

      _duckingService.SetPriority(ttsSource, priority);

      // Duck BEFORE taking over. The announcement being replaced releases its own duck registration
      // as it unwinds; had that release landed before this one registered, the active-event count
      // would touch zero and the music would swell up and back down between the two voices (the
      // swell AUD-74 removed). Registered first, the count goes 1 -> 2 -> 1. The caller's token, not
      // this call's: a replacement landing mid-fade must not leave the fade half-applied, because the
      // replacing announcement joins an already-ducked state and applies no fade of its own. The
      // finally below reverses a completed fade either way.
      await _duckingService.StartDuckingAsync(ttsSource, cancellationToken);

      token.ThrowIfCancellationRequested();
      if (!BecomeActive(registration, ttsSource))
      {
        _logger.LogInformation("Announcement not played: superseded by a newer one of the same priority, or stopped");
        return AnnouncementOutcome.Interrupted;
      }

      // The REASON is kept, not just the fact of completion (TTS-2). A source that fails to start
      // raises PlaybackCompleted(Error) — TTSEventSource does so synchronously inside PlayAsync when
      // there is no playback device — and treating every completion as success is how a failed
      // announcement came back as 200 "Announcement played". RunContinuationsAsynchronously because
      // the token registration below can complete it on another announcement's thread, and this
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
      // or a replacing announcement's. Any other OperationCanceledException — above all
      // TaskCanceledException from TTSFactory's HttpClient timeout — is a failure and falls through to
      // the arm below, logged at Error (TTS-2 review).
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogDebug("Announcement cancelled");
      }
      else
      {
        _logger.LogInformation("Announcement interrupted: replaced by another announcement, or stopped");
      }
      return AnnouncementOutcome.Interrupted;
    }
    catch (ObjectDisposedException) when (token.IsCancellationRequested)
    {
      // StopAsync stopped and disposed the source between BecomeActive and PlayAsync. That is a stop,
      // not a failure.
      _logger.LogInformation("Announcement interrupted: stopped as it was about to play");
      return AnnouncementOutcome.Interrupted;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during announcement");
      return AnnouncementOutcome.Failed;
    }
    finally
    {
      // Unregistered FIRST: cleanup awaits the duck's release fade, and a call that has stopped
      // speaking must not go on superseding other announcements for that long.
      Unregister(registration);
      if (ttsSource != null)
      {
        await CleanupSourceAsync(ttsSource);
      }
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

    // AUD-73: the same arbitration as AnnounceAsync, with ONE registration for both phases. It stays
    // registered between them, so StopAsync or a replacing announcement still reaches this call while
    // the name is being synthesised after the ring.
    using var announcementCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var registration = NewRegistration(announcementCts, priority);
    var token = announcementCts.Token;

    IEventAudioSource? soundSource = null;
    IEventAudioSource? ttsSource = null;
    try
    {
      // Phase 1: Play the sound file with ducking
      soundSource = await _audioFileFactory.CreateFromFileAsync(soundPath, token);
      if (IsSuperseded(registration))
      {
        _logger.LogInformation("Sound + announcement not played: superseded by a newer one of the same priority, or stopped");
        return;
      }

      _duckingService.SetPriority(soundSource, priority);

      // Duck before taking over, for the reason given in AnnounceAsync.
      await _duckingService.StartDuckingAsync(soundSource, cancellationToken);

      token.ThrowIfCancellationRequested();
      if (!BecomeActive(registration, soundSource))
      {
        _logger.LogInformation("Sound + announcement not played: superseded by a newer one of the same priority, or stopped");
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
      SetRegisteredSource(registration, null);
      soundSource = null;

      // Phase 2: Play the TTS announcement. ⚠ The sound's duck was released just above, before this
      // one registers, so the music starts to come back up between the ring and the name. That
      // predates AUD-73 and is not fixed here.
      ttsSource = await _ttsFactory.CreateAsync(message, cancellationToken: token);
      _duckingService.SetPriority(ttsSource, priority);

      await _duckingService.StartDuckingAsync(ttsSource, cancellationToken);

      token.ThrowIfCancellationRequested();
      if (!BecomeActive(registration, ttsSource))
      {
        _logger.LogInformation("Announcement after sound not played: superseded by a newer one of the same priority, or stopped");
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
      // Unregistered first, for the reason given in AnnounceAsync.
      Unregister(registration);
      if (soundSource != null)
      {
        await CleanupSourceAsync(soundSource);
      }
      if (ttsSource != null)
      {
        await CleanupSourceAsync(ttsSource);
      }
    }
  }

  /// <inheritdoc />
  /// <remarks>
  /// Stops every announcement asked for before this call, including any still synthesising or
  /// ducking: those are refused when they try to become active (<c>_stopGeneration</c>). Every
  /// announcement currently playing is cancelled and removed; its source is stopped here unless its
  /// own call has already begun that cleanup, in which case that call finishes it. Every
  /// announcement is stopped, not only the one a caller started: a phone hang-up also silences a
  /// doorbell playing alongside.
  /// </remarks>
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    (CancellationTokenSource Cts, IEventAudioSource? Source)[] stopping;
    lock (_lock)
    {
      _stopGeneration++;
      stopping = [.. _active.Select(r => (r.Cts, r.Source))];
      foreach (var r in _active)
      {
        r.Removed = true;
      }
      _active.Clear();
    }

    foreach (var (cts, source) in stopping)
    {
      CancelQuietly(cts);
      if (source != null)
      {
        await CleanupSourceAsync(source);
      }
    }

    _logger.LogDebug("Announcement stopped");
  }

  /// <summary>
  /// Makes <paramref name="registration"/> active with <paramref name="source"/> as what it is playing,
  /// and cancels every active announcement it outranks. Returns false, changing nothing, when a NEWER
  /// announcement of the SAME priority is already active: this one was superseded before it spoke.
  /// </summary>
  /// <remarks>
  /// <para>
  /// AUD-73. Before this, the single-slot "cancel" cancelled a token that no playback path waited on, so
  /// a second announcement played on top of the first. Measured on the box 2026-09-30 on
  /// <c>af9bc2b</c>: two requests 1.5 s apart both returned "completed", with two events ducked at once
  /// for 1.7 s.
  /// </para>
  /// <para>
  /// The rules, with "higher" meaning a larger priority number:
  /// a higher-priority announcement replaces a lower one; at equal priority the one requested later
  /// replaces the earlier (ADR-029 §6.2 rule 1's "replace", applied to announcements); a
  /// lower-priority announcement never cuts off a higher one — it plays alongside it, which is what
  /// every overlapping announcement did before AUD-73, so the one case this change does not decide is
  /// left as it was. A replaced call unwinds through its own cleanup (stop ducking, stop, dispose).
  /// </para>
  /// </remarks>
  private bool BecomeActive(Registration registration, IEventAudioSource source)
  {
    List<Registration> replaced = [];
    lock (_lock)
    {
      // Removed: replaced or stopped between its last check and here (the cancel runs after the lock is
      // released, so the token may not show it yet). IsSupersededLocked also covers a StopAsync that
      // ran after this call was asked for.
      if (registration.Removed || IsSupersededLocked(registration))
      {
        return false;
      }

      foreach (var other in _active)
      {
        if (!ReferenceEquals(other, registration) && Outranks(registration, other))
        {
          replaced.Add(other);
        }
      }

      foreach (var other in replaced)
      {
        other.Removed = true;
        _active.Remove(other);
      }
      registration.Source = source;
      if (!_active.Contains(registration))
      {
        _active.Add(registration);
      }
    }

    // Outside the lock: Cancel runs each replaced call's token callbacks on this thread.
    foreach (var other in replaced)
    {
      CancelQuietly(other.Cts);
    }
    return true;
  }

  private bool IsSuperseded(Registration registration)
  {
    lock (_lock)
    {
      return IsSupersededLocked(registration);
    }
  }

  private bool IsSupersededLocked(Registration registration) =>
    registration.StopGeneration != _stopGeneration
    || _active.Any(other => !ReferenceEquals(other, registration)
      && other.Priority == registration.Priority
      && other.Arrival > registration.Arrival);

  private static bool Outranks(Registration a, Registration b) =>
    a.Priority > b.Priority || (a.Priority == b.Priority && a.Arrival > b.Arrival);

  private void SetRegisteredSource(Registration registration, IEventAudioSource? source)
  {
    lock (_lock)
    {
      registration.Source = source;
    }
  }

  private void Unregister(Registration registration)
  {
    lock (_lock)
    {
      registration.Removed = true;
      _active.Remove(registration);
      registration.Source = null;
    }
  }

  private Registration NewRegistration(CancellationTokenSource cts, int priority)
  {
    lock (_lock)
    {
      return new Registration(cts, ++_arrivalCounter, priority, _stopGeneration);
    }
  }

  /// <summary>
  /// Cancels a CTS taken from a registration. Its owner disposes it when its call returns, and that
  /// can happen between reading the registration and this call if the announcement ended on its own
  /// at the same moment. Nothing is left to stop then, so the disposal is not an error.
  /// </summary>
  private static void CancelQuietly(CancellationTokenSource cts)
  {
    try
    {
      cts.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // Already finished and disposed by its owner; see the summary.
    }
  }

  /// <summary>
  /// Stops ducking for, stops and disposes <paramref name="source"/> — once. A second call for the
  /// same source (StopAsync and the announcing call's own cleanup, racing) returns immediately.
  /// </summary>
  private async Task CleanupSourceAsync(IEventAudioSource source)
  {
    if (!_cleanedUp.TryAdd(source, new object()))
    {
      return;
    }

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

  /// <summary>One announcement call's claim on the speaker. Mutable fields are guarded by _lock.</summary>
  private sealed class Registration(CancellationTokenSource cts, long arrival, int priority, long stopGeneration)
  {
    public CancellationTokenSource Cts { get; } = cts;
    public long Arrival { get; } = arrival;
    public int Priority { get; } = priority;
    public long StopGeneration { get; } = stopGeneration;
    public IEventAudioSource? Source { get; set; }

    /// <summary>Out of <c>_active</c> for good: replaced, stopped or finished. Never re-added.</summary>
    public bool Removed { get; set; }
  }
}
