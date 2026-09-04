using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// The one attended playback (ADR-029 D1, D2, D3).
///
/// <para>
/// Shaped after <see cref="AnnouncementService"/>, which is the only non-defective event path in
/// this tree, with a state machine added. Like it, this NEVER calls IMasterMixer.AddSource: audio
/// reaches the speakers because SoundFlowPlaybackService adds a component to the playback device's
/// mixer itself, and AddSource only mutates a bookkeeping list. SourcesController.PlayFileEvent adds
/// on every play and removes only on its OWN failure path (SourcesController.cs:730, when
/// PlayFileAsync returns false) — so a play that SUCCEEDS leaves the entry behind for good, which is
/// where its per-play leak comes from. "Never removes" would be the tidier sentence and it is not
/// true.
/// </para>
///
/// <para>
/// ⚠ Unlike AnnouncementService, this does NOT await playback inside the call. StartAsync accepts,
/// mints an id, publishes a Preparing snapshot and returns; acquisition and playback run on a task
/// that outlives the HTTP response (ADR-029 §3.3 specifies 202). Everything about the cancellation
/// model follows from that — see StartAsync.
/// </para>
/// </summary>
public sealed class EventPlaybackService : IEventPlaybackService, IDisposable
{
  /// <summary>
  /// Prefix for the id this service mints. Deliberately a THIRD id space, not a reuse of either
  /// existing one: AudioFileEventSource carries IAudioSource.Id ("AudioFileEvent-…") AND a private
  /// _playbackId ("audio-event-…") that are not equal, while TTSEventSource uses Id for both. A
  /// cancel-by-id built on either would silently fail for one arm (ADR-029 §3.3). This service owns
  /// "evp-…", resolves it to a source instance, and then only ever calls interface methods on that
  /// instance — so the divergence is invisible here, and a log line carrying "evp-" can only have
  /// come from this seam.
  /// </summary>
  private const string PlaybackIdPrefix = "evp-";

  private readonly ILogger<EventPlaybackService> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _gvMediaOptions;
  private readonly IOptionsMonitor<TTSOptions> _ttsOptions;
  private readonly ITTSFactory _ttsFactory;
  private readonly AudioFileEventSourceFactory _fileFactory;
  private readonly IDuckingService _duckingService;
  private readonly GvMediaClient _gvMediaClient;

  /// <summary>
  /// How long <see cref="Dispose"/> will block waiting for an already-playing source to release.
  /// Bounded so a wedged source delays shutdown rather than preventing it.
  /// </summary>
  private static readonly TimeSpan DisposeReleaseTimeout = TimeSpan.FromSeconds(5);

  // Serialises the transitions that install or tear down a playback. Async because teardown awaits
  // StopDuckingAsync / StopAsync / DisposeAsync.
  //
  // ⚠ The PlaybackCompleted handler must NEVER wait on this. That event is raised from inside
  // StopCoreAsync, which this service calls while holding the gate — so a handler that waited here
  // would deadlock on a non-reentrant semaphore. The handler instead claims the terminal flag and
  // returns; see OnSourceCompleted. FakeEventSource.StopAsync raises UserStopped inline for exactly
  // this reason, so the suite exercises the re-entrancy rather than only documenting it.
  //
  // ⚠ DELIBERATELY NEVER DISPOSED, and that is a fix rather than an oversight. SemaphoreSlim.Dispose
  // releases only the AvailableWaitHandle, which nothing here ever touches — but it also drops the
  // async waiter queue WITHOUT completing it, so a task already parked in WaitAsync is stranded and
  // its TearDownAsync never runs. Not disposing means every parked waiter is eventually released by
  // whoever holds the gate (every holder releases in a finally), and it also means _gate.Release()
  // cannot throw ObjectDisposedException out of an HTTP call that raced Dispose.
  private readonly SemaphoreSlim _gate = new(1, 1);

  // Guards the two fields below only. Never held across an await.
  private readonly object _stateLock = new();
  private Playback? _current;
  private EventPlaybackSnapshot? _snapshot;

  // 0 or 1. An int rather than a bool because Dispose claims it with Interlocked.Exchange: a
  // check-then-set on a bool would let two disposers both run the release path below.
  private int _disposedFlag;

  /// <summary>Creates the service.</summary>
  /// <param name="logger">The logger.</param>
  /// <param name="gvMediaOptions">GvMedia options — Enabled and MaxSpeechChars are read here.</param>
  /// <param name="ttsOptions">TTS options — all four synthesis parameters and the timeout.</param>
  /// <param name="ttsFactory">The synthesis factory for the Speech arm.</param>
  /// <param name="fileFactory">The event-source factory for the RemoteMedia arm.</param>
  /// <param name="duckingService">Ducking, wired exactly as AnnouncementService wires it.</param>
  /// <param name="gvMediaClient">The server-side media fetcher.</param>
  public EventPlaybackService(
    ILogger<EventPlaybackService> logger,
    IOptionsMonitor<GvMediaOptions> gvMediaOptions,
    IOptionsMonitor<TTSOptions> ttsOptions,
    ITTSFactory ttsFactory,
    AudioFileEventSourceFactory fileFactory,
    IDuckingService duckingService,
    GvMediaClient gvMediaClient)
  {
    _logger = logger;
    _gvMediaOptions = gvMediaOptions;
    _ttsOptions = ttsOptions;
    _ttsFactory = ttsFactory;
    _fileFactory = fileFactory;
    _duckingService = duckingService;
    _gvMediaClient = gvMediaClient;
  }

  /// <inheritdoc />
  public EventPlaybackSnapshot? Current
  {
    get
    {
      lock (_stateLock)
      {
        return _snapshot;
      }
    }
  }

  /// <inheritdoc />
  public event EventHandler<EventPlaybackSnapshot>? PlaybackChanged;

  /// <inheritdoc />
  public async Task<EventPlaybackSnapshot> StartAsync(
    EventPlaybackRequest request, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ObjectDisposedException.ThrowIf(IsDisposed, this);

    var gv = _gvMediaOptions.CurrentValue;

    var rejection = request.Validate(gv.MaxSpeechChars);
    if (rejection != EventPlaybackRejection.None)
    {
      throw new EventPlaybackRejectedException(rejection);
    }

    // The ONE failure knowable without touching the network, so it is answered synchronously rather
    // than accepted and then failed on a channel the caller may not be watching. Every other
    // GvMediaFailure becomes a FailureReason on a Failed snapshot — see AcquireRemoteMediaAsync.
    if (request.Kind == EventPlaybackKind.RemoteMedia && !gv.Enabled)
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Disabled, "GvMedia is disabled; refusing to accept a RemoteMedia playback.");
    }

    var playback = new Playback(
      PlaybackIdPrefix + Guid.NewGuid().ToString("N"), request.Kind, request.Label);

    await _gate.WaitAsync(cancellationToken);
    try
    {
      // One audio engine, one set of speakers, so one attended playback (ADR-029 D6 §8.1). This is
      // NOT D5's priority rule — "a source of priority >= 8 preempts attended playback" is PR 4 and
      // nothing here reads GvMedia:PreemptAtPriority.
      var replaced = _current;
      if (replaced is not null && replaced.ClaimTerminal())
      {
        _logger.LogInformation(
          "Attended playback {NewId} replaces {OldId}", playback.Id, replaced.Id);
        await TearDownAsync(replaced);
        Publish(SnapshotOf(replaced, EventPlaybackState.Stopped, failureReason: null));
      }

      var accepted = SnapshotOf(playback, EventPlaybackState.Preparing, failureReason: null);
      lock (_stateLock)
      {
        _current = playback;
        _snapshot = accepted;
      }

      // ⚠ Published BEFORE the acquisition task is started, and the order is not cosmetic.
      // AcquireAndPlayAsync does not take _gate, so holding it here does not serialise it: with a
      // fast acquisition — a cache hit, or a synthesis that returns immediately — the background
      // task could publish Playing first, and since both publishes take _stateLock the LATER
      // writer wins. Current would then report Preparing for a playback already producing audio,
      // and a subscriber would see Playing followed by Preparing: a transition that never
      // happened. The window is only the instructions between queueing the work item and
      // publishing, and was not reproduced on a dev machine; publishing first closes it anyway,
      // because it costs nothing and the alternative depends on that window staying small.
      Publish(accepted);

      // ⚠ playback.Token, NOT cancellationToken. cancellationToken is the CONTROLLER's, which on
      // the HTTP path is HttpContext.RequestAborted — scoped to the request, on a context that is
      // pooled and reset once the response completes. Acquisition outlives the 202 response by
      // design, so linking them would cancel every fetch the instant it was accepted. The
      // cancellation that actually exists here is StopAsync, a replacing StartAsync, and Dispose.
      // EventPlaybackServiceTests.AcquisitionSurvivesCancellationOfTheStartToken keeps this true.
      _ = Task.Run(
        () => AcquireAndPlayAsync(playback, request, playback.Token), CancellationToken.None);

      return accepted;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(IsDisposed, this);

    await _gate.WaitAsync(cancellationToken);
    try
    {
      var playback = _current;
      if (playback is null || playback.Id != playbackId || !playback.ClaimTerminal())
      {
        return false;
      }

      await TearDownAsync(playback);
      lock (_stateLock)
      {
        _current = null;
      }
      Publish(SnapshotOf(playback, EventPlaybackState.Stopped, failureReason: null));
      return true;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  /// <remarks>
  /// ⚠ ACCEPTED RACE, stated rather than locked away — and it applies equally to
  /// <see cref="PauseAsync"/> and <see cref="ResumeAsync"/>. None of the three takes _gate, so a
  /// stop or a replacement landing between the source.State read below and the call that follows it
  /// reaches a source that has just been disposed, which surfaces as ObjectDisposedException and, on
  /// the HTTP path, as a 500. The window is the few instructions between those two statements, there
  /// is one user in front of one console, and the alternative is holding a semaphore across an HTTP
  /// handler that can await ducking and teardown — which is worse than the imprecision it buys. The
  /// same trade is recorded on EventPlaybackController.Transport for the 404/409 race.
  /// </remarks>
  public async Task<bool> SeekAsync(
    string playbackId, TimeSpan position, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(IsDisposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || !source.IsSeekable)
    {
      // Reported as false rather than by letting EventAudioSourceBase.SeekAsync throw
      // NotSupportedException: "this cannot scrub" is an ordinary answer, not an exception. The
      // return is narrower than "the audio moved" and the interface's remarks say exactly why.
      return false;
    }

    try
    {
      await source.SeekAsync(position, cancellationToken);
    }
    catch (ArgumentOutOfRangeException)
    {
      // ⚠ A seek PAST THE END, reported the same way "this cannot scrub" already is. The controller
      // range-checks only for negative/NaN/infinite, so a position inside [0, ∞) but beyond the
      // content reaches AudioFileEventSource.SeekCoreAsync, which throws
      // ArgumentOutOfRangeException — and Radio.API registers neither UseExceptionHandler nor
      // AddProblemDetails, so that escaped as a bare 500. It is most reachable exactly where the
      // scrubber is least trustworthy: when the provider reported duration 0 and the factory had to
      // estimate one from file size, the UI's idea of "the end" and the source's do not agree.
      // False here becomes a clean 409 with reason "NotSeekable", which is the honest answer.
      return false;
    }

    PublishNonTerminal(
      playback,
      source.State == AudioSourceState.Paused
        ? EventPlaybackState.Paused
        : EventPlaybackState.Playing);
    return true;
  }

  /// <inheritdoc />
  public async Task<bool> PauseAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(IsDisposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || source.State != AudioSourceState.Playing)
    {
      return false;
    }

    await source.PauseAsync(cancellationToken);
    PublishNonTerminal(playback, EventPlaybackState.Paused);
    return true;
  }

  /// <inheritdoc />
  public async Task<bool> ResumeAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(IsDisposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || source.State != AudioSourceState.Paused)
    {
      return false;
    }

    await source.ResumeAsync(cancellationToken);
    PublishNonTerminal(playback, EventPlaybackState.Playing);
    return true;
  }

  /// <summary>True once <see cref="Dispose"/> has claimed this instance.</summary>
  private bool IsDisposed => Volatile.Read(ref _disposedFlag) != 0;

  /// <summary>Cancels anything in flight and releases a source that is already playing.</summary>
  /// <remarks>
  /// Two different jobs, because there are two different states a playback can be in.
  ///
  /// While ACQUISITION is still in flight, cancelling is the whole answer: the acquisition task's
  /// own catch runs, and <see cref="Playback.TryAdopt"/> refuses whatever the fetch or the synthesis
  /// then produces, so that path disposes it.
  ///
  /// Once acquisition has RETURNED, nobody else will ever run teardown for that playback —
  /// AcquireAndPlayAsync has finished, and no completion is coming from a source the container is
  /// about to abandon. So this claims the source itself and releases it here. Until PHN-1c's review
  /// that case leaked: StopDuckingAsync and DisposeAsync never ran, and on the RemoteMedia arm an
  /// AudioFileEventSource's FileStream over the cached recording was left to the finalizer, which on
  /// Windows also blocks GvMediaCache from evicting that entry.
  ///
  /// ⚠ The release BLOCKS, and is bounded at <see cref="DisposeReleaseTimeout"/>. Dispose is
  /// synchronous and the release is not; Task.Run puts it on the thread pool so it cannot deadlock
  /// against a captured synchronization context, and the bound means a wedged source delays shutdown
  /// rather than preventing it. No snapshot is published — there is nothing left to publish to.
  ///
  /// ⚠ _gate is NOT disposed; see its declaration. What this guarantees is only that no fetch or
  /// synthesis keeps running after the container has gone, and that an adopted source is released. A
  /// gated operation already in flight is allowed to finish rather than being stranded, and a
  /// transport call made AFTER this is rejected by ObjectDisposedException.ThrowIf before it ever
  /// reaches the gate.
  /// </remarks>
  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
    {
      return;
    }

    Playback? playback;
    lock (_stateLock)
    {
      playback = _current;
      _current = null;
    }

    if (playback is null)
    {
      return;
    }

    // Claimed so a completion racing this cannot also tear down. Cancel first regardless: that is
    // what stops an acquisition that has not returned yet.
    playback.ClaimTerminal();
    playback.Cancel();

    if (playback.ClaimSourceForRelease() is not { } source)
    {
      // Either acquisition never handed one over — in which case that claim has just closed
      // adoption, so the acquisition path disposes what it is about to produce — or another
      // terminal path released it already.
      return;
    }

    try
    {
      if (!Task.Run(() => ReleaseSourceAsync(playback, source)).Wait(DisposeReleaseTimeout))
      {
        _logger.LogWarning(
          "Attended playback {Id} did not release within {Timeout}; abandoning it at shutdown",
          playback.Id, DisposeReleaseTimeout);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error releasing attended playback {Id} at shutdown", playback.Id);
    }
  }

  // ── acquisition ─────────────────────────────────────────────────────────

  private async Task AcquireAndPlayAsync(
    Playback playback, EventPlaybackRequest request, CancellationToken token)
  {
    try
    {
      IEventAudioSource source;
      switch (request.Kind)
      {
        case EventPlaybackKind.RemoteMedia:
          source = await AcquireRemoteMediaAsync(playback, request, token);
          break;
        case EventPlaybackKind.Speech:
          source = await AcquireSpeechAsync(playback, request, token);
          break;
        default:
          // Unreachable: Validate rejected every other value before the playback was minted.
          throw new InvalidOperationException($"Unhandled kind {request.Kind}.");
      }

      // The source handover is an ATOMIC check-and-assign, and that is a leak fix rather than
      // tidiness. Every terminal caller claims the flag and then tears down, and teardown can only
      // release a source the playback already OWNS - which it does not for the whole of
      // acquisition. So a stop, a replacement or a disposal landing here used to leave the source
      // acquisition then produced with nobody to dispose it: on the RemoteMedia arm an
      // AudioFileEventSource holding an open FileStream over the cached recording, which on Windows
      // also stops GvMediaCache's evictor reclaiming that entry. TryAdopt refusing is how this path
      // learns that it owns the disposal instead.
      if (!playback.TryAdopt(source, token))
      {
        await DisposeOrphanAsync(playback, source);
        return;
      }

      source.PlaybackCompleted += (_, e) => OnSourceCompleted(playback, e);

      _duckingService.SetPriority(source, request.Priority);
      await _duckingService.StartDuckingAsync(source, token);

      // Re-checked between ducking and audio, deliberately rather than argued benign. A terminal
      // transition can land in the window between those two awaits, and PR 4's preemption path
      // lands on exactly this window - so a preempted playback must not still start producing
      // sound. Throwing hands it to the catch below, which releases what this now owns.
      if (playback.IsTerminal || token.IsCancellationRequested)
      {
        throw new OperationCanceledException(token);
      }

      await source.PlayAsync(token);

      // Guarded rather than published unconditionally: a source can fail synchronously inside
      // PlayAsync — AudioFileEventSource.PlayCoreAsync catches and raises Error completion on the
      // calling thread — so the terminal transition may already be claimed by the time control
      // returns here. See PublishNonTerminal.
      PublishNonTerminal(playback, EventPlaybackState.Playing);
    }
    catch (OperationCanceledException)
    {
      // Stop, replacement or shutdown. The transition was already published by whoever cancelled,
      // or is about to be; claiming the flag here only stops a late failure from overwriting it.
      playback.ClaimTerminal();
      _logger.LogDebug("Attended playback {Id} cancelled before it could play", playback.Id);

      // And release whatever it is holding. Once-only through ClaimSourceForRelease, so a concurrent
      // teardown under the gate — or Dispose, which claims the same way — cannot double-release, and
      // whichever of them gets there second finds null and does nothing.
      await TearDownAsync(playback);
    }
    catch (GvMediaUnavailableException ex)
    {
      await FailAsync(playback, "Media" + ex.Reason, ex);
    }
    catch (Exception ex)
    {
      await FailAsync(playback, FailureReasonFor(request.Kind), ex);
    }
  }

  private async Task<IEventAudioSource> AcquireRemoteMediaAsync(
    Playback playback, EventPlaybackRequest request, CancellationToken token)
  {
    // Validate guaranteed both of these on the RemoteMedia arm.
    var mediaId = request.MediaId!;
    var masked = GvMediaCache.MaskFor(mediaId);

    _logger.LogInformation("Attended playback {Id}: acquiring {MaskedId}", playback.Id, masked);

    var path = await _gvMediaClient.GetVoicemailFileAsync(mediaId, token);

    // DurationSeconds == 0 means UNKNOWN (ADR-022 §4.2, ADR-029 §4.1). The SOURCE still needs a
    // number — its completion is driven by one — so the factory estimates in that case; the
    // SNAPSHOT reports null, so the UI renders an indeterminate bar rather than a confident lie.
    var authoritative = request.DurationSeconds is > 0
      ? TimeSpan.FromSeconds(request.DurationSeconds.Value)
      : (TimeSpan?)null;
    playback.ReportedDuration = authoritative;

    // GetFullPath because GvMedia:CacheDirectory ships as the relative "./data/gvmedia": an absolute
    // path is what CreateFromAbsolutePathAsync requires, and it keeps the path unambiguous in a log.
    return await _fileFactory.CreateFromAbsolutePathAsync(
      Path.GetFullPath(path), authoritative, token);
  }

  private async Task<IEventAudioSource> AcquireSpeechAsync(
    Playback playback, EventPlaybackRequest request, CancellationToken token)
  {
    var tts = _ttsOptions.CurrentValue;

    // ⚠ ALL FOUR fields, filled explicitly from configuration. TTSFactory resolves each one as
    // "parameters?.X ?? opts.X", and TWO of those four ?? — Speed and Pitch — are lifted by the
    // null-conditional on the OBJECT, so they fire only when parameters itself is null. TWO of the
    // four fields still carry the trap that follows from that: Speed and Pitch are non-nullable with
    // a 1.0f initializer, so any non-null TTSParameters silently pins them to the TYPE's default
    // rather than to configuration. Engine and Voice are nullable since TTS-9 and their ?? DOES
    // fire, so filling them here is belt-and-braces rather than load-bearing — but passing null
    // instead would be correct only until VoiceId is set, which is the trap re-armed, so it is never
    // passed. See design/FUTURE-WORK.md § "TTS seam" item 1.
    var parameters = new TTSParameters
    {
      Engine = ResolveEngine(request.Engine, tts.DefaultEngine),
      Voice = request.VoiceId ?? tts.DefaultVoice,
      Speed = tts.DefaultSpeed,
      Pitch = tts.DefaultPitch
    };

    // ⚠ Never log request.Text. For the Speech arm it is an SMS body — private content by exactly
    // the standard the media-id masking rule protects. Length and engine, nothing else.
    _logger.LogInformation(
      "Attended playback {Id}: synthesising {Chars} characters with {Engine}",
      playback.Id, request.Text!.Length, parameters.Engine);

    // ⚠ The first reader TTSOptions.GenerationTimeoutSeconds has ever had. Nothing in
    // src/Radio.Infrastructure read it: TTSFactory awaits its cloud calls on the caller's token and
    // no other bound, so an unbounded synthesis would park this seam in Preparing with no route
    // that clears it. Bounding it HERE rather than in TTSFactory keeps a live shared path with two
    // other callers out of this PR.
    //
    // The residual, stated rather than hidden: cancelling mid-synthesis aborts the awaited HTTP
    // call to the cloud engine and nothing else — there is no local process to orphan, both
    // remaining engines being network services.
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, tts.GenerationTimeoutSeconds)));

    try
    {
      return await _ttsFactory.CreateAsync(request.Text!, parameters, timeout.Token);
    }
    catch (OperationCanceledException) when (!token.IsCancellationRequested)
    {
      throw new TimeoutException(
        $"TTS synthesis exceeded TTS:GenerationTimeoutSeconds ({tts.GenerationTimeoutSeconds}s).");
    }
  }

  /// <summary>
  /// Resolves the engine to synthesise with: the request's override when it named one, otherwise
  /// TTS:DefaultEngine.
  /// </summary>
  /// <remarks>
  /// MIRRORS the rules of TTSFactory.ParseEngine — case-insensitive name match, Enum.IsDefined so a
  /// numeric string cannot smuggle in an undefined value, and an InvalidOperationException rather
  /// than a fallback for an unset or unrecognised name — rather than sharing them. It is duplicated
  /// because ParseEngine is private on TTSFactory, which is a live shared path this PR is forbidden
  /// to touch; the tests pin both behaviours rather than asserting the two implementations are
  /// identical, which nothing here can check.
  ///
  /// The throw is reachable only through a misconfigured TTS:DefaultEngine, not through a caller:
  /// EventPlaybackRequest.Validate refuses an unresolvable request Engine with
  /// EventPlaybackRejection.UnknownEngine before a playback is minted. When it does fire it
  /// surfaces through AcquireSpeechAsync's generic catch as a Failed snapshot reading
  /// "SpeechSynthesisFailed" — synthesis being the only gate (ADR-029 §14 Q10).
  /// </remarks>
  /// <param name="requested">The request's engine override, or null/blank for the configured one.</param>
  /// <param name="configuredDefault">TTS:DefaultEngine.</param>
  /// <returns>The engine to synthesise with.</returns>
  /// <exception cref="InvalidOperationException">
  /// No engine is configured, or the name does not resolve to a defined TTSEngine.
  /// </exception>
  internal static TTSEngine ResolveEngine(string? requested, string configuredDefault)
  {
    var name = string.IsNullOrWhiteSpace(requested) ? configuredDefault : requested;

    if (string.IsNullOrWhiteSpace(name))
    {
      throw new InvalidOperationException(
        "No TTS engine is configured. Set 'TTS:DefaultEngine' to one of: Google, Azure.");
    }

    if (!Enum.TryParse<TTSEngine>(name, ignoreCase: true, out var engine) || !Enum.IsDefined(engine))
    {
      throw new InvalidOperationException(
        $"Unknown TTS engine '{name}'. Valid engines are: Google, Azure.");
    }

    return engine;
  }

  private static string FailureReasonFor(EventPlaybackKind kind) =>
    kind == EventPlaybackKind.Speech ? "SpeechSynthesisFailed" : "MediaAcquisitionFailed";

  // ── completion and teardown ─────────────────────────────────────────────

  /// <summary>
  /// Handles PlaybackCompleted from the source.
  /// </summary>
  /// <remarks>
  /// ⚠ This must never wait on _gate. It is raised from inside StopCoreAsync, which TearDownAsync
  /// calls while holding the gate, so waiting here would deadlock a non-reentrant semaphore.
  ///
  /// ⚠ And it must be once-only. BOTH event sources raise completion from two independent places —
  /// EndOfContent from their monitor and UserStopped from StopCoreAsync — and
  /// AudioSourceBase.StopAsync short-circuits only on Created or Disposed, never on Stopped. So
  /// teardown after a natural end raises a SECOND event. AnnouncementService is immune by accident
  /// (TrySetResult discards it); this holds a state machine and is not, so an unguarded handler
  /// would overwrite Completed with Stopped and — from PR 5 — broadcast a transition that did not
  /// happen.
  /// </remarks>
  private void OnSourceCompleted(Playback playback, AudioSourceCompletedEventArgs e)
  {
    if (!playback.ClaimTerminal())
    {
      return;
    }

    var state = e.Reason switch
    {
      PlaybackCompletionReason.EndOfContent => EventPlaybackState.Completed,
      PlaybackCompletionReason.Error => EventPlaybackState.Failed,
      _ => EventPlaybackState.Stopped
    };
    var reason = e.Reason == PlaybackCompletionReason.Error ? "PlaybackError" : null;

    _ = Task.Run(async () =>
    {
      try
      {
        await _gate.WaitAsync();
        try
        {
          await TearDownAsync(playback);
          lock (_stateLock)
          {
            if (ReferenceEquals(_current, playback))
            {
              _current = null;
            }
          }
          Publish(SnapshotOf(playback, state, reason));
        }
        finally
        {
          _gate.Release();
        }
      }
      catch (Exception ex)
      {
        // ⚠ There used to be an ObjectDisposedException arm here, describing a container that went
        // away underneath this task. It cannot happen any more and so it is gone: _gate is
        // deliberately never disposed, so a waiter parked here is released by whoever holds the gate
        // rather than being stranded. This general arm stays because the task is fire-and-forget and
        // an escaping exception would be unobserved.
        _logger.LogWarning(ex, "Error finalising attended playback {Id}", playback.Id);
      }
    }, CancellationToken.None);
  }

  private async Task FailAsync(Playback playback, string failureReason, Exception ex)
  {
    if (!playback.ClaimTerminal())
    {
      return;
    }

    // Warning, not Error: since LOG-11 the journal carries Warning and above, and a failed voicemail
    // is exactly what an operator diagnoses from the box. The exception is logged, so the rule that
    // no raw media id may reach an exception MESSAGE is what keeps this line clean — GvMediaClient
    // and EventPlaybackRejectedException both hold to it.
    _logger.LogWarning(ex, "Attended playback {Id} failed: {Reason}", playback.Id, failureReason);

    try
    {
      await _gate.WaitAsync();
      try
      {
        await TearDownAsync(playback);
        lock (_stateLock)
        {
          if (ReferenceEquals(_current, playback))
          {
            _current = null;
          }
        }
        Publish(SnapshotOf(playback, EventPlaybackState.Failed, failureReason));
      }
      finally
      {
        _gate.Release();
      }
    }
    catch (Exception publishFailure)
    {
      // ⚠ Same change as OnSourceCompleted's: the ObjectDisposedException arm that used to sit here
      // described a disposed _gate, which is no longer a state that exists. Kept as a general arm
      // because FailAsync is reached from a fire-and-forget task, where an escaping exception is
      // unobserved rather than fatal-but-visible.
      _logger.LogWarning(
        publishFailure, "Error publishing the failure of attended playback {Id}", playback.Id);
    }
  }

  /// <summary>
  /// Cancels the playback and releases whatever source it owns, through
  /// <see cref="ReleaseSourceAsync"/>. Every step there is independently guarded: this runs on the
  /// failure path too, where any of them may already be in a bad state, and a throw here would leave
  /// the seam holding a playback it can never clear.
  /// </summary>
  /// <remarks>
  /// The caller must have claimed the terminal flag first. That is what keeps source.StopAsync's
  /// UserStopped event — raised synchronously from inside this call — from re-entering _gate.
  /// </remarks>
  private async Task TearDownAsync(Playback playback)
  {
    playback.Cancel();

    if (playback.ClaimSourceForRelease() is not { } source)
    {
      // Either another terminal path already released it, or acquisition never handed one over -
      // in which case ClaimSourceForRelease has just closed adoption, so acquisition disposes
      // whatever it is about to produce rather than dropping it.
      return;
    }

    await ReleaseSourceAsync(playback, source);
  }

  /// <summary>
  /// Stops ducking, stops the source and disposes it. Every step is independently guarded.
  /// </summary>
  /// <remarks>
  /// Split out of <see cref="TearDownAsync"/> so <see cref="Dispose"/> can run the same three steps
  /// against a source it claimed itself. The caller has already taken ownership through
  /// <see cref="Playback.ClaimSourceForRelease"/>, so this is reached at most once per source.
  /// </remarks>
  private async Task ReleaseSourceAsync(Playback playback, IEventAudioSource source)
  {
    try { await _duckingService.StopDuckingAsync(source); }
    catch (Exception ex) { _logger.LogWarning(ex, "Error stopping ducking for {Id}", playback.Id); }

    try { await source.StopAsync(); }
    catch (Exception ex) { _logger.LogWarning(ex, "Error stopping source for {Id}", playback.Id); }

    // Disposal releases the FileStream AudioFileEventSource opened over the cached recording, which
    // is what lets GvMediaCache evict it later. On Linux an unlink would succeed regardless; on
    // Windows the FileShare.Read handle would make File.Delete throw, and the evictor logs and
    // continues — so a leaked handle there costs cap accuracy, not correctness.
    try { await source.DisposeAsync(); }
    catch (Exception ex) { _logger.LogWarning(ex, "Error disposing source for {Id}", playback.Id); }
  }

  /// <summary>
  /// Releases a source the playback refused to adopt, because it had already ended.
  /// </summary>
  /// <remarks>
  /// Disposal only, never StopAsync: this source was never ducked and never played, so there is
  /// nothing to stop. Guarded the way TearDownAsync guards its steps, because this runs on a
  /// background task where an escaping exception is a process-level hazard.
  /// </remarks>
  private async Task DisposeOrphanAsync(Playback playback, IEventAudioSource source)
  {
    _logger.LogDebug(
      "Attended playback {Id} ended while its audio was still being acquired; releasing it",
      playback.Id);

    try { await source.DisposeAsync(); }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error disposing an unadopted source for {Id}", playback.Id);
    }
  }

  // ── snapshots ───────────────────────────────────────────────────────────

  private Playback? Resolve(string playbackId)
  {
    lock (_stateLock)
    {
      return _current is { } p && p.Id == playbackId ? p : null;
    }
  }

  /// <summary>
  /// Mints a snapshot. Position and Duration are read from the source at the instant of minting —
  /// the snapshot is an ANCHOR, not a tick (ADR-029 §8.2).
  /// </summary>
  /// <remarks>
  /// ⚠ For a Speech playback PositionAtBroadcast is ALWAYS TimeSpan.Zero, for the whole playback.
  /// EventAudioSourceBase.Position defaults to zero and TTSEventSource deliberately does not
  /// override it, whereas AudioFileEventSource does. That is not a defect this snapshot hides — it
  /// is what the source reports — but nothing in this PR may claim otherwise.
  ///
  /// Duration differs by arm and both are honest about what they are. RemoteMedia reports the
  /// provider's authoritative value, or NULL when the provider said 0 (unknown). Speech reports the
  /// source's own duration, which is an ESTIMATE of the synthesised audio — the only value that
  /// exists, and the one the source's completion is driven by.
  /// </remarks>
  private static EventPlaybackSnapshot SnapshotOf(
    Playback playback, EventPlaybackState state, string? failureReason)
  {
    var source = playback.Source;
    var duration = state == EventPlaybackState.Preparing
      ? null
      : playback.Kind == EventPlaybackKind.Speech
        ? source?.Duration
        : playback.ReportedDuration;

    return new EventPlaybackSnapshot(
      playback.Id,
      playback.Kind,
      playback.Label,
      state,
      duration,
      source?.Position ?? TimeSpan.Zero,
      DateTimeOffset.UtcNow,
      failureReason);
  }

  /// <summary>
  /// Publishes a NON-terminal state — Playing or Paused — for a playback that has not ended.
  /// </summary>
  /// <remarks>
  /// ⚠ The terminal check and the store happen under ONE _stateLock, and that is what makes this
  /// correct rather than merely likely. A source can fail synchronously inside PlayAsync
  /// (AudioFileEventSource.PlayCoreAsync catches and raises Error completion on the calling
  /// thread), so a terminal transition can already be claimed by the time the acquisition path says
  /// "Playing". Publishing it anyway would report audio that never started, and from PR 5 would
  /// broadcast a Failed → Playing transition that did not happen. Every terminal publish stores
  /// under the same lock, so the two orderings that remain — this one first, or skipped entirely —
  /// are both honest.
  /// </remarks>
  private void PublishNonTerminal(Playback playback, EventPlaybackState state)
  {
    var snapshot = SnapshotOf(playback, state, failureReason: null);

    lock (_stateLock)
    {
      if (playback.IsTerminal)
      {
        return;
      }
      if (_current is null || _current.Id == snapshot.Id)
      {
        _snapshot = snapshot;
      }
    }

    Raise(snapshot);
  }

  /// <summary>
  /// Stores the snapshot and raises PlaybackChanged.
  /// </summary>
  /// <remarks>
  /// ⚠ A late snapshot must not resurrect a replaced playback. Only the CURRENT playback's snapshot
  /// is stored; a snapshot for one that has already been replaced is raised (so a subscriber sees
  /// the stop) but not retained (so Current keeps describing the playback that is actually in
  /// flight).
  ///
  /// Nothing subscribes to PlaybackChanged in this PR — PR 5 is what connects it to /hubs/audio. It
  /// is raised now because the IEventPlaybackService contract requires it and because a broadcast
  /// bolted on later would be a second place transitions are decided.
  ///
  /// ⚠ FOR PR 5, BEFORE YOU SUBSCRIBE. Every terminal call site of this method — StartAsync's
  /// replacement arm, StopAsync, OnSourceCompleted and FailAsync — invokes it WHILE HOLDING _gate,
  /// so a subscriber runs on a thread that already owns a non-reentrant semaphore. A hub broadcast
  /// that only serialises and sends is fine. A subscriber that re-enters this seam — StopAsync,
  /// StartAsync, or anything that awaits something which does — DEADLOCKS, in exactly the way
  /// OnSourceCompleted is written to avoid. This is flagged rather than fixed here: restructuring
  /// the publishes to happen outside the gate is a real change to the ordering guarantees above, and
  /// it belongs to the PR that first has a subscriber to test it with.
  /// </remarks>
  private void Publish(EventPlaybackSnapshot snapshot)
  {
    lock (_stateLock)
    {
      if (_current is null || _current.Id == snapshot.Id)
      {
        _snapshot = snapshot;
      }
    }

    Raise(snapshot);
  }

  private void Raise(EventPlaybackSnapshot snapshot)
  {
    try
    {
      PlaybackChanged?.Invoke(this, snapshot);
    }
    catch (Exception ex)
    {
      // A subscriber that throws must not take the playback down with it.
      _logger.LogWarning(ex, "A PlaybackChanged subscriber threw for {Id}", snapshot.Id);
    }
  }

  /// <summary>One in-flight attended playback. At most one exists at a time (ADR-029 D6 §8.1).</summary>
  private sealed class Playback
  {
    /// <summary>
    /// The playback's own cancellation, cancelled by every terminal path.
    /// </summary>
    /// <remarks>
    /// ⚠ DELIBERATELY NEVER DISPOSED, and the reason is not laziness. AcquireAndPlayAsync holds this
    /// token by value for the whole of acquisition, and AcquireSpeechAsync builds a LINKED source
    /// from it — CancellationTokenSource.CreateLinkedTokenSource registers a callback on the source,
    /// which throws ObjectDisposedException if the source has been disposed. Disposing here would
    /// therefore trade a leaked registration list for an exception on a background task, at a moment
    /// (shutdown, or a stop landing mid-synthesis) when there is nothing left to report it to. The
    /// cost of not disposing is one finalizable registration list per playback, on a seam that holds
    /// at most one playback at a time.
    /// </remarks>
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sourceLock = new();
    private IEventAudioSource? _source;
    private bool _released;
    private int _terminal;

    public Playback(string id, EventPlaybackKind kind, string? label)
    {
      Id = id;
      Kind = kind;
      Label = label;
    }

    public string Id { get; }
    public EventPlaybackKind Kind { get; }
    public string? Label { get; }
    public TimeSpan? ReportedDuration { get; set; }
    public CancellationToken Token => _cts.Token;

    /// <summary>The acquired source, or null while acquisition is still in flight.</summary>
    /// <remarks>
    /// Still readable after release, deliberately: the terminal snapshot is minted AFTER teardown
    /// and reads Duration and Position from here, so nulling it would make a completed speech
    /// playback report no duration at all.
    /// </remarks>
    public IEventAudioSource? Source
    {
      get { lock (_sourceLock) { return _source; } }
    }

    /// <summary>True once any terminal transition has been claimed. Never goes back to false.</summary>
    public bool IsTerminal => Volatile.Read(ref _terminal) != 0;

    /// <summary>
    /// True for the FIRST caller only. Every terminal transition — natural completion, user stop,
    /// replacement, failure — goes through this, so a playback ends exactly once no matter how many
    /// PlaybackCompleted events its source raises.
    /// </summary>
    public bool ClaimTerminal() => Interlocked.CompareExchange(ref _terminal, 1, 0) == 0;

    /// <summary>
    /// Hands a freshly acquired source to the playback, and refuses if the playback has ended.
    /// </summary>
    /// <remarks>
    /// The check and the assignment are ONE atomic step, against the same lock
    /// <see cref="ClaimSourceForRelease"/> takes, and that is the whole of the fix. Whichever side
    /// wins, exactly one owns the disposal: if a terminal transition gets there first this returns
    /// false and the acquisition path disposes what it is holding, and if this gets there first
    /// teardown finds a non-null source and releases it.
    ///
    /// The token is checked as well as the terminal flag because the flag is not the only signal a
    /// playback has ended: cancellation can arrive on its own. ⚠ Scoped precisely, because the
    /// earlier wording ("a check written against the flag alone would still leak at container
    /// shutdown") claimed more than this method can deliver: everything here is about the window
    /// where acquisition is STILL IN FLIGHT. A source that has already been adopted is past this
    /// method entirely, and releasing THAT one at shutdown is <see cref="Dispose"/>'s job — which is
    /// why Dispose claims the source itself rather than relying on this refusal.
    /// </remarks>
    public bool TryAdopt(IEventAudioSource source, CancellationToken token)
    {
      lock (_sourceLock)
      {
        if (_released || IsTerminal || token.IsCancellationRequested)
        {
          return false;
        }
        _source = source;
        return true;
      }
    }

    /// <summary>
    /// Takes responsibility for releasing the source. Returns it to the FIRST caller only, and null
    /// to everyone after - including when acquisition has not handed one over yet, which it then
    /// permanently prevents, so the source can never end up with no owner at all.
    /// </summary>
    public IEventAudioSource? ClaimSourceForRelease()
    {
      lock (_sourceLock)
      {
        if (_released)
        {
          return null;
        }
        _released = true;
        return _source;
      }
    }

    /// <summary>Cancels the playback's token. Idempotent; safe on an already-cancelled playback.</summary>
    /// <remarks>
    /// ⚠ No ObjectDisposedException guard, deliberately. There used to be one, commented "already
    /// cancelled and disposed" — a state nothing in this file can produce, because <c>_cts</c> is
    /// never disposed. A catch for an impossible exception reads as evidence that the exception is
    /// possible, which is the way the next reader gets misled.
    /// </remarks>
    public void Cancel() => _cts.Cancel();
  }
}
