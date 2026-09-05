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
  /// Clock for the max-duration cap. Injectable so a test can advance it rather than wait on it —
  /// CLAUDE.md § Test Timing's named idiom, and the reason FakeTimeProvider is already referenced by
  /// Radio.Infrastructure.Tests.
  /// </summary>
  /// <remarks>
  /// ⚠ PHN-1d deliberately did NOT take this dependency, and its C-44 says why: that PR added no
  /// timer, so the thing a test had to synchronise on was a dispatch (PreemptionTail), not a clock.
  /// This PR adds a real timer, so the idiom now applies. Both are true; neither supersedes the other.
  /// </remarks>
  private readonly TimeProvider _timeProvider;

  /// <summary>
  /// Upper clamp for <c>GvMedia:MaxPlaybackSeconds</c> — 24 hours. Not a policy about how long a
  /// voicemail may be; a guard so an absurd value cannot make <c>TimeProvider.CreateTimer</c> throw
  /// and take every attended playback down with it. See <see cref="ArmDurationCap"/>.
  /// </summary>
  private const int MaxCapSeconds = 86_400;

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

  private volatile Task _preemptionTail = Task.CompletedTask;

  /// <summary>
  /// Test seam: the tail of the most recently DISPATCHED preemption, or a completed task if none has
  /// been dispatched yet.
  /// </summary>
  /// <remarks>
  /// ⚠ This exists so tests can synchronise on the OBSERVATION rather than on elapsed time.
  /// OnDuckingStateChanged decides synchronously on the raising thread and then DISPATCHES the stop,
  /// so a test asserting straight after raising the event would be racing that dispatch. For the
  /// positive case PlaybackChanged is already a rendezvous; for the NEGATIVE case — "a priority-5
  /// source changed nothing" — there is no event to wait for, and the only alternatives are a sleep
  /// (forbidden by CLAUDE.md § Test Timing, and the reason TEST-4 exists) or a poll that starvation
  /// can only weaken.
  ///
  /// PR 4 adds no timer, so the house TimeProvider/FakeTimeProvider idiom does not apply here: what is
  /// asynchronous is a Task.Run, not a clock.
  ///
  /// ⚠ A decision to do NOTHING leaves this unchanged rather than resetting it to a completed task —
  /// an earlier summary here claimed the opposite, and no code assigned it. Unchanged is also the more
  /// useful contract: a test that preempts and then raises something sub-threshold keeps its
  /// rendezvous instead of silently losing it.
  ///
  /// Last-writer-wins under no lock. Two concurrent preemptions would leave one tail unobserved, which
  /// costs a test its rendezvous and costs production nothing — the work is already dispatched and is
  /// idempotent through Playback.ClaimTerminal.
  /// </remarks>
  internal Task PreemptionTail => _preemptionTail;

  /// <summary>Creates the service.</summary>
  /// <param name="logger">The logger.</param>
  /// <param name="gvMediaOptions">GvMedia options — Enabled and MaxSpeechChars are read here.</param>
  /// <param name="ttsOptions">TTS options — all four synthesis parameters and the timeout.</param>
  /// <param name="ttsFactory">The synthesis factory for the Speech arm.</param>
  /// <param name="fileFactory">The event-source factory for the RemoteMedia arm.</param>
  /// <param name="duckingService">Ducking, wired exactly as AnnouncementService wires it.</param>
  /// <param name="gvMediaClient">The server-side media fetcher.</param>
  /// <param name="timeProvider">
  /// Clock for the max-duration cap. Trailing and optional so the container and
  /// <c>EventPlaybackServiceTests.CreateService</c> both keep working with no registration;
  /// <see cref="TimeProvider.System"/> is the production value.
  /// </param>
  public EventPlaybackService(
    ILogger<EventPlaybackService> logger,
    IOptionsMonitor<GvMediaOptions> gvMediaOptions,
    IOptionsMonitor<TTSOptions> ttsOptions,
    ITTSFactory ttsFactory,
    AudioFileEventSourceFactory fileFactory,
    IDuckingService duckingService,
    GvMediaClient gvMediaClient,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _gvMediaOptions = gvMediaOptions;
    _ttsOptions = ttsOptions;
    _ttsFactory = ttsFactory;
    _fileFactory = fileFactory;
    _duckingService = duckingService;
    _gvMediaClient = gvMediaClient;
    _timeProvider = timeProvider ?? TimeProvider.System;

    // ADR-029 D5 §6.3. Subscribed here rather than lazily: both this service and DuckingService are
    // registered singleton (AddEventPlayback, AddSoundFlowAudio), so the subscription lives for the
    // process and Dispose is the only place it is removed.
    //
    // ⚠ This singleton is built at HOST START, not lazily, and the comment that used to sit here said
    // the opposite. It was true until PHN-1e: AudioStateUpdateService now resolves
    // IEventPlaybackService in its own constructor and is registered AddHostedService, so this
    // subscription is live from boot rather than from the first POST to /api/audio/events.
    //
    // That is the better direction, and the consequence is worth seeing rather than discovering:
    // GvMediaClient, AudioFileEventSourceFactory, ITTSFactory and IDuckingService are all now
    // constructed at startup, so a resolution failure in any of them is a service that will not start
    // — visible — rather than a 500 on the first voicemail.
    //
    // ⚠ NOTHING IN THE SUITE COVERS THAT, and an earlier draft of this comment claimed
    // EventPlaybackRegistrationTests did. It does not, twice over: it registers FAKES for three of
    // the four named above (ITTSFactory, IDuckingService, AudioFileEventSourceFactory) and its own
    // class remark says it therefore proves nothing about AddSoundFlowAudio registering them, and it
    // builds a bare ServiceCollection rather than a host, so it says nothing about WHEN anything is
    // constructed. The API-container route is closed too: AudioStateUpdateService is registered
    // AddHostedService (Program.cs), and CustomWebApplicationFactory removes every IHostedService
    // descriptor — so no test host ever constructs the subscriber that makes this eager. Boot order
    // here is a box observation, not a test.
    _duckingService.DuckingStateChanged += OnDuckingStateChanged;
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

    _duckingService.DuckingStateChanged -= OnDuckingStateChanged;

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

      // ⭐ D28's wait. See WaitForClearAirAsync's remarks for why it is here and nowhere else.
      try
      {
        await WaitForClearAirAsync(playback, token);
      }
      catch (TimeoutException ex)
      {
        // D28's staleness bound. Thirty seconds is longer than any notification this box makes, so a
        // wait that reaches it means the blocker was not what we thought.
        //
        // Failed is the honest state — it never produced sound — and failing is acceptable HERE and
        // only here, precisely because by then the user has watched a visible Waiting state. That is
        // what makes this different from the bare refusal D28 rejected.
        //
        // ⚠ HANDLED AT THE WAIT'S OWN CALL SITE rather than in the catch chain at the bottom of this
        // method, and that is a correctness requirement rather than a preference. AcquireSpeechAsync
        // ALSO throws TimeoutException — for TTS:GenerationTimeoutSeconds — and it does so from the
        // switch above, inside the same outer try. A `catch (TimeoutException)` down there would
        // therefore report every hung synthesis as "WaitExpired" instead of "SpeechSynthesisFailed".
        // Here the acquisition switch has already returned, and waiter.Task.WaitAsync is the only
        // thing in WaitForClearAirAsync that can throw this, so the reason cannot be misattributed.
        //
        // ⚠ C-57, and the ORDER IS NOT WHAT CARRIES IT — an earlier revision of this comment said
        // "Disposed FIRST, for the C-57 reason", implying a dependency that does not exist.
        // DisposeOrphanAsync disposes the local `source` directly and never consults
        // ClaimSourceForRelease, so it works the same either side of FailAsync. What carries C-57 is
        // that it is called AT ALL: FailAsync's TearDownAsync reaches ClaimSourceForRelease, which
        // answers null for a playback that never adopted, so nothing on that path can release this
        // source. Delete this line and the RemoteMedia arm leaks an open FileStream over the cached
        // recording for the life of the process.
        await DisposeOrphanAsync(playback, source);
        await FailAsync(playback, "WaitExpired", ex);
        return;
      }
      catch
      {
        // ⚠ C-57. The source is acquired and NOT adopted, so none of the catches below can release
        // it: TearDownAsync and FailAsync both go through ClaimSourceForRelease, which answers null
        // for a playback that never adopted. Before this row the only await between acquisition and
        // TryAdopt was _gate.WaitAsync(CancellationToken.None), which cannot throw — so no exit
        // existed here and none was guarded. The wait adds two (the staleness bound and a cancel),
        // and without this the RemoteMedia arm leaks an open FileStream over the cached recording for
        // the life of the process, which on Windows also stops GvMediaCache's evictor reclaiming that
        // entry.
        //
        // DisposeOrphanAsync is the right tool and already exists: this source was never ducked and
        // never played, so there is nothing to stop. A later TearDownAsync finds null and does
        // nothing, so there is no double-dispose.
        await DisposeOrphanAsync(playback, source);
        throw;
      }

      // ⚠ From here to Publish(Playing) runs under _gate, and PR 4 is what makes that necessary.
      // ReleaseSourceAsync — the only thing that stops ducking, stops the source and disposes it — has
      // six callers. Four hold _gate: StopAsync, StartAsync's replacement arm, OnSourceCompleted's
      // dispatched task, and FailAsync. Holding it here makes "tear this playback down" and "start its
      // audio" MUTUALLY EXCLUSIVE against those four rather than merely ordered.
      //
      // ⚠ TWO do not, and naming only one of them would be the overclaim this file keeps warning
      // about: Dispose (which claims the source through ClaimSourceForRelease instead), and the
      // catch (OperationCanceledException) below — which calls TearDownAsync after the finally has
      // already released the gate. Neither can race THIS block: the catch is the same task, reached
      // only after the block has exited, and Dispose is container shutdown.
      //
      // What that closes: PR 3 narrowed the ducking-to-play window with the IsTerminal re-check below,
      // but a re-check is not a lock. A preemption landing between that check and PlayAsync could still
      // complete a whole teardown — stop ducking, stop the source, publish Stopped — and PlayAsync
      // would then start audio on a source the seam has already forgotten, in the window before
      // DisposeAsync. AudioSourceBase.PlayAsync only refuses once _disposed is set; between StopAsync
      // and DisposeAsync the state reads Stopped and PlayCoreAsync runs. That sound has no playbackId,
      // so no route, no chip and no later preemption can address it: it plays to the end, over the
      // announcement that preempted it. It is the worst outcome available in this PR.
      //
      // ⚠ Stated precisely rather than overclaimed: Dispose does NOT take _gate (it claims the source
      // through ClaimSourceForRelease instead), so container shutdown remains outside this exclusion.
      // The re-check below is what still covers that, and it is why it stays.
      //
      // The cost, and it is a real one: a stop arriving while this holds the gate is delayed by one
      // ducking attack fade (Audio:DuckingAttackMs, 100 ms shipped) plus one PlayAsync — which starts
      // playback and returns rather than awaiting completion. So a preemption landing mid-tail lets a
      // short burst of audio out before stopping it, where before it could suppress the start
      // entirely. A brief blip that stops is strictly better than audio that nothing can stop.
      //
      // CancellationToken.None on the wait, matching OnSourceCompleted: acquiring the gate must not be
      // abandoned half-way. The cancellation that matters is checked inside it.
      await _gate.WaitAsync(CancellationToken.None);
      try
      {
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

        // Both happen BEFORE ducking starts, because StartDuckingAsync now raises DuckingStateChanged
        // for this very source and OnDuckingStateChanged therefore runs synchronously on this thread.
        //
        // ⚠ Only ONE of the two orderings is load-bearing for preemption, and the first draft of this
        // comment claimed both were. ADOPTION is: the handler compares TriggeringSource against
        // _current.Source, so adopting after ducking would let a playback at Priority >= 8 preempt
        // itself — moving TryAdopt below reds two tests. SetPriority is NOT: with the entry missing the
        // handler reads the category default 8, clears the threshold, and is then turned away by the
        // same identity check, so moving it below changes nothing and reds no test. It stays here
        // because the RECORDED priority has to be right for everything that reads it later —
        // GetActiveEventsByPriority, and PHN-1f's queue — not because it guards this rule.
        _duckingService.SetPriority(source, request.Priority);
        await _duckingService.StartDuckingAsync(source, token);

        // ⚠ Nothing is checked here about OTHER active sources, and since PHN-1f that is because the
        // check has already HAPPENED rather than because it is missing. This seam no longer mixes: a
        // playback starting while a source at or above GvMedia:PreemptAtPriority is already sounding
        // WAITS for it and then plays (owner decision D28), and the wait is WaitForClearAirAsync,
        // called above — after acquisition and BEFORE _gate.
        //
        // ⚠ Deliberately not moved down here. §0.2 forbids waiting inside the gate: holding it across
        // a wait bounded by GvMedia:MaxQueuedWaitSeconds would block StopAsync, the replacement arm
        // and OnSourceCompleted for the whole of that wait, so the user's own Stop button would do
        // nothing until the blocker finished — which is the shape D28 exists to avoid.
        //
        // ⛔ And still do not "fix" this by refusing the start. A refusal was put to the owner and
        // rejected; deferring is the answer, and deferring is what the wait does.
        //
        // Still re-checked between ducking and audio. Under the gate this is closed against StopAsync
        // and the replacement arm; against Dispose, which does not take the gate, it remains the
        // narrowing check PR 3 wrote it as. Throwing hands it to the catch below, which releases what
        // this now owns.
        if (playback.IsTerminal || token.IsCancellationRequested)
        {
          throw new OperationCanceledException(token);
        }

        await source.PlayAsync(token);

        // ADR-029 D7 §7.1 — THE guarantee, and the only stop condition that needs no client at all.
        // ⚠ Armed HERE, inside _gate and after PlayAsync returned, for two reasons. The gate is what
        // makes it impossible to arm a cap on a playback a preemption has already torn down (PHN-1d
        // §5 flags exactly this). And "at most one timer exists" then follows from D5 rule 1 — one
        // attended playback at a time — rather than from bookkeeping this class would have to keep.
        ArmDurationCap(playback);

        // Guarded rather than published unconditionally: a source can fail synchronously inside
        // PlayAsync — AudioFileEventSource.PlayCoreAsync catches and raises Error completion on the
        // calling thread — so the terminal transition may already be claimed by the time control
        // returns here. See PublishNonTerminal.
        PublishNonTerminal(playback, EventPlaybackState.Playing);
      }
      finally
      {
        _gate.Release();
      }
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
  /// would overwrite Completed with Stopped and — since PHN-1e wired the hub subscriber —
  /// broadcast a transition that did not happen.
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

  // ── preemption (ADR-029 D5) ───────────────────────────────────────

  /// <summary>
  /// True while some event source at or above <paramref name="threshold"/> is in the ducking set.
  /// </summary>
  /// <remarks>
  /// ⚠ This gives <see cref="IDuckingService.GetActiveEventsByPriority"/> its FIRST non-test caller
  /// since it was written — which PHN-1d C-42 predicted would be the queue, and it was.
  ///
  /// ⚠ No exclusion for OUR OWN source is needed, and one is deliberately NOT written. Both call sites
  /// — <see cref="WaitForClearAirAsync"/> and <see cref="TryWakeWaitingPlayback"/> — ask on behalf of a
  /// playback that has not reached StartDuckingAsync, which happens strictly after the wait and under
  /// _gate; so THAT playback's own source has never joined the set at the moment the question is
  /// asked. A guard for a state that cannot occur reads as evidence that it can.
  /// APlaybackAtPriorityEightDoesNotBlockItself pins it.
  ///
  /// ⚠ SCOPED PRECISELY, because the earlier wording — "the attended source is not in the set when it
  /// is asked" — claimed more than that, and two states falsify the broader reading:
  ///
  /// (a) A PREVIOUS attended playback's source can still be in the set while it is being torn down.
  ///     StopDuckingAsync removes it only inside ReleaseSourceAsync, so between a replacement (or a
  ///     natural end whose OnSourceCompleted task has not yet taken the gate) and that removal there
  ///     is a real window in which the new playback asks this question and finds the old source.
  ///
  /// (b) This runs on a raising thread from TryWakeWaitingPlayback, so a concurrent raise can evaluate
  ///     it after ANOTHER thread's wake has already let the waiting playback resume, take the gate and
  ///     reach StartDuckingAsync — at which point that playback's own source is in the set.
  ///
  /// Neither weakens the conclusion. In (a) the source found is one that is genuinely still sounding,
  /// so waiting for it is the rule working rather than a playback blocking itself. In (b) the answer
  /// is only ever used to decide whether to call the idempotent TryWake, and the playback has already
  /// been woken — a redundant "still blocked" costs nothing.
  ///
  /// ⚠ GetPriority is called here rather than read from event args because this is a question about
  /// the CURRENT SET, not about one transition — there are no args. The fade-window race the args
  /// exist to close does not apply: these sources are resident in the set, not arriving or leaving.
  /// </remarks>
  private bool IsBlockedByAHigherPrioritySource(int threshold) =>
    _duckingService.GetActiveEventsByPriority()
      .Any(s => _duckingService.GetPriority(s) >= threshold);

  /// <summary>
  /// ⭐ Owner decision D28: waits for the air to clear before the acquisition tail starts audio.
  /// Returns as soon as nothing at or above GvMedia:PreemptAtPriority is in the ducking set.
  /// </summary>
  /// <remarks>
  /// ⚠ Called AFTER acquisition and BEFORE _gate. Both halves matter and neither is arbitrary.
  ///
  /// After acquisition, so the audio is ready the instant the room goes quiet and an acquisition
  /// FAILURE surfaces at once rather than after thirty seconds of Waiting — "wait, then fail" being a
  /// strictly worse version of the shape D28 rejected.
  ///
  /// Before the gate, because holding _gate across a wait this long would block StopAsync, the
  /// replacement arm and OnSourceCompleted for its whole length — the user's own Stop button would do
  /// nothing until the blocker finished.
  ///
  /// ⚠ THE CALLER OWNS THE SOURCE ACROSS THIS CALL AND MUST DISPOSE IT IF THIS THROWS. Nothing has
  /// been adopted yet, so TearDownAsync and FailAsync both reach ClaimSourceForRelease, which answers
  /// null. See AcquireAndPlayAsync's guard, and plan PHN-1f C-57.
  ///
  /// ⚠ Which is also why the log lines below need no guard of their own, and the shape was checked
  /// rather than assumed. ILogger.Log rethrows provider exceptions, so a failing sink throws from
  /// them — but both sit INSIDE the try whose finally runs EndWait, and this whole call sits inside
  /// AcquireAndPlayAsync's `catch { await DisposeOrphanAsync(...); throw; }`. A throwing sink
  /// therefore costs the line and the playback, never the source. <see cref="DisposeOrphanAsync"/>
  /// is where the same shape was wrong.
  /// </remarks>
  private async Task WaitForClearAirAsync(Playback playback, CancellationToken token)
  {
    var gv = _gvMediaOptions.CurrentValue;

    // Evaluated BEFORE anything is armed or published, so the overwhelmingly common case — a quiet
    // room — walks an empty list, allocates nothing, and puts no extra message on the wire. Trap 5 is
    // about churn on an N100, and a queue that broadcast a Waiting nobody waited for would be churn.
    if (!IsBlockedByAHigherPrioritySource(gv.PreemptAtPriority))
    {
      return;
    }

    var waiter = playback.BeginWait();
    try
    {
      // ⚠ RE-CHECKED AFTER ARMING, and this closes a real missed-wake race rather than being belt and
      // braces. TryWakeWaitingPlayback asks "is anything waiting?" before it touches the ducking set —
      // it has to, because it runs on the raising thread for every announcement on this box. So a
      // blocker ending between the check above and BeginWait finds nothing waiting, wakes nothing, and
      // parks this playback until WaitExpired FOR A ROOM THAT IS ALREADY QUIET — which is D28's
      // rejected option delivered thirty seconds late, the exact outcome this row exists to prevent.
      // Arm, then re-check. The wake is idempotent, so a redundant TrySetResult costs nothing.
      if (!IsBlockedByAHigherPrioritySource(gv.PreemptAtPriority))
      {
        return;
      }

      // Information rather than Warning: this is the feature working, not a fault. Since LOG-11 it
      // lands in the file sink rather than the journal, which is where "why did the voicemail take a
      // moment" is diagnosed from. Source ids only — never a media id and never request text
      // (PHN-1b §0.3 ④).
      _logger.LogInformation(
        "Attended playback {Id} is waiting: a source at or above GvMedia:PreemptAtPriority "
        + "({Threshold}) is already sounding (owner decision D28)",
        playback.Id, gv.PreemptAtPriority);

      PublishNonTerminal(playback, EventPlaybackState.Waiting);

      // ⭐ ONE call is the wake, the staleness bound AND the cancel. A one-shot timer, not a poll and
      // not a tick — trap 5 forbids both. It takes the TimeProvider PHN-1e injected, so
      // FakeTimeProvider.Advance produces WaitExpired deterministically with no Task.Delay anywhere
      // near an assertion (CLAUDE.md § Test Timing).
      //
      // Clamped at 1 for the reason GvMediaOptions.MaxQueuedWaitSeconds gives: a 0 meaning "never
      // wait" would resolve to mixing, which is the option D28 rejected.
      await waiter.Task.WaitAsync(
        TimeSpan.FromSeconds(Math.Max(1, gv.MaxQueuedWaitSeconds)), _timeProvider, token);

      _logger.LogInformation("Attended playback {Id} stopped waiting; the air is clear", playback.Id);
    }
    finally
    {
      playback.EndWait();
    }
  }

  /// <summary>
  /// Re-evaluates whether a waiting playback can proceed, and releases it if so.
  /// </summary>
  /// <remarks>
  /// ⚠ A STATE re-evaluation, not an edge, and that is deliberate. An edge would have to be right
  /// about which transitions can unblock a wait; a state re-evaluation is idempotent, cannot be
  /// desynchronised by a missed raise, and — the part that matters — uses the SAME predicate that
  /// decided to wait, so "blocked" has exactly one definition in this file.
  ///
  /// ⚠ The "is anything waiting" guard comes FIRST, and it is a trap-5 requirement rather than a
  /// micro-optimisation: this runs on the raising thread for EVERY ducking transition on the box,
  /// including every announcement with no attended playback anywhere near it. Without the guard each
  /// one would walk the ducking set and call GetPriority per member, on an N100 where churn is
  /// audible. The race that guard creates is closed by WaitForClearAirAsync's re-check (C-66).
  ///
  /// ⚠ It never touches a source, never takes _gate and never starts audio. The acquisition task that
  /// was already running resumes, takes _gate, and runs PR 3's tail unchanged — so there is no second
  /// entry point into that tail and none of PHN-1d Task 5's properties has to be re-established.
  /// </remarks>
  private void TryWakeWaitingPlayback()
  {
    Playback? waiting;
    lock (_stateLock)
    {
      waiting = _current;
    }

    if (waiting is null || !waiting.IsWaiting)
    {
      return;
    }

    if (IsBlockedByAHigherPrioritySource(_gvMediaOptions.CurrentValue.PreemptAtPriority))
    {
      return;
    }

    waiting.TryWake();
  }

  /// <summary>
  /// ADR-029 D5 §6.2 rule 2: a source starting at or above GvMedia:PreemptAtPriority stops attended
  /// playback outright — unless that playback is WAITING, which is not attended playback in flight.
  /// </summary>
  /// <remarks>
  /// It STOPS rather than pausing. Resuming a voicemail mid-word twenty seconds after a phone call is
  /// worse than restarting it, and the recording is replayable at zero cost — it is a local cached
  /// file. The UI returns to an idle, replayable state (ADR-029 §12 item 4).
  ///
  /// ⚠ Three things in this method are load-bearing and none of them is obvious:
  ///
  /// (1) Only a Started transition is acted on. Since PHN-1f DuckingService raises for every source
  ///     that LEAVES as well — Ended, with IsDucking carrying the true aggregate — and
  ///     StopAllDuckingAsync raises AllCleared with a NULL TriggeringSource. Neither is a start, so
  ///     neither can preempt. ⚠ They are not ignored, though: TryWakeWaitingPlayback runs above this
  ///     test, on every raise in both directions, because a source leaving is precisely what a D28
  ///     wait is waiting for.
  ///
  /// (2) The priority is READ FROM THE ARGS, captured by DuckingService inside the lock that added the
  ///     source. It is not resolved here and it is not resolved on the dispatched task, and the
  ///     difference is a real bug rather than a style choice.
  ///
  ///     Every caller does SetPriority(source, p) immediately before StartDuckingAsync(source), and
  ///     DuckingService.StopDuckingAsync deletes that entry before IT raises — so any subscriber that
  ///     resolves the priority for itself can read the category default 8 for a source whose caller
  ///     had explicitly claimed 3.
  ///
  ///     ⚠ Reading it synchronously on the raising thread was NOT enough, which is what PHN-1d found:
  ///     DuckingService raises the TRANSITION event after awaiting ApplyFadeAsync
  ///     (Audio:DuckingAttackMs, 100 ms shipped), so a StopDuckingAsync for that same source landing
  ///     inside the fade deletes the entry BEFORE this handler ever runs. PHN-1d could only narrow
  ///     that with an ActiveEventCount == 0 guard, whose own residual was that a second still-ducking
  ///     source made the count non-zero and the guard silent. PHN-1f closed it at the source: the
  ///     capture happens before the fade, so there is nothing left to race. The guard and the
  ///     GetPriority call are both GONE.
  ///
  /// (3) The stop is DISPATCHED, never awaited here. Two reasons, and the second one is the one that
  ///     is easy to state too strongly, so it is stated exactly.
  ///
  ///     First: this runs on the thread inside DuckingService.StartDuckingAsync — on the live path
  ///     that is AnnouncementService's, mid-announcement, reached from
  ///     POST /api/notifications/announce. Awaiting StopAsync would block the doorbell for the length
  ///     of our whole teardown, which includes DuckingService's 500 ms release fade.
  ///
  ///     Second: StopAsync takes _gate, and since PHN-1d the acquisition tail holds _gate across the
  ///     very StartDuckingAsync call that raises this event. ⚠ That is NOT a live deadlock today, and
  ///     claiming it would be the overclaim this repo keeps shipping: the raise arriving on a thread
  ///     that already holds _gate is our OWN source's, and the identity check below returns before the
  ///     dispatch is reached. Every other reachable raise arrives on a foreign thread, where awaiting
  ///     would block rather than deadlock. What dispatching buys is that neither of those stays true
  ///     by accident — an awaiting handler becomes a hard deadlock the first time any raise reaches
  ///     this point from a gate-holding thread, and rule 1 and the identity check are the only two
  ///     things standing between here and that. OnSourceCompleted is written the same way, for the
  ///     same reason, and its remark says so.
  /// </remarks>
  private void OnDuckingStateChanged(object? sender, DuckingStateChangedEventArgs e)
  {
    // ⭐ FIRST, and on EVERY raise in both directions — including StopAllDuckingAsync's, which carries
    // a null TriggeringSource and clears the whole set, and is therefore one of the strongest reasons
    // a wait should end. See TryWakeWaitingPlayback: it returns before touching the ducking set when
    // nothing is waiting, which is the overwhelmingly common case.
    TryWakeWaitingPlayback();

    if (e.Transition != DuckingSourceTransition.Started || e.TriggeringSource is not { } trigger)
    {
      return;
    }

    // ⚠ READ FROM THE ARGS, captured by DuckingService inside the lock that ADDED the entry and before
    // the attack fade. PHN-1d resolved this with a synchronous GetPriority and had to guard the fade
    // window with an ActiveEventCount == 0 check; both the call and the guard are GONE, and this is
    // why. The guard's own acknowledged residual — "if some OTHER source is still ducking, the count
    // is non-zero and this guard does not fire" — is closed by the same change rather than narrowed.
    var priority = e.TriggeringSourcePriority;

    var threshold = _gvMediaOptions.CurrentValue.PreemptAtPriority;
    if (priority < threshold)
    {
      // ADR-029 §6.2 rule 3: sub-threshold events keep MIXING, exactly as they do today over TTS
      // announcements. This row does not fix that; the fix would be a queue across every caller of
      // IAnnouncementService, and it is separate work with its own risk.
      return;
    }

    Playback? victim;
    lock (_stateLock)
    {
      victim = _current;
    }

    // Three reasons to do nothing here, and the middle one is the newest.
    //
    // (a) Nothing is in the slot at all.
    //
    // (b) ⭐ THE VICTIM IS WAITING. ADR-029 §6.2 rule 2 stops an IN-FLIGHT attended playback, and a
    //     playback parked in WaitForClearAirAsync is by definition not in flight — which is exactly
    //     what IEventPlaybackService.Current's own remark now says about the Waiting state ("It is not
    //     in flight and it is not finished"). It has adopted no source and is producing no audio, so
    //     stopping it prevents no overlap; and because TryWakeWaitingPlayback is a STATE
    //     re-evaluation rather than an edge, the playback simply keeps waiting for this new blocker
    //     too and plays when the air is clear.
    //
    //     Without this clause the guard falls through, because victim.Source is null for the WHOLE of
    //     a wait — _source is assigned only in TryAdopt, after the wait and after _gate — so
    //     ReferenceEquals(null, trigger) is false and a real StopAsync is dispatched. The user presses
    //     play behind a doorbell, watches Waiting, and the next unprioritised announcement (8 is
    //     DuckingService.DefaultEventPriority and the shipped PreemptAtPriority) destroys it:
    //     Waiting → Stopped, no sound, no reason given. That is the outcome D28 exists to remove,
    //     delivered after a visible wait.
    //
    //     ⚠ Two things this clause does NOT do, stated because both are easy to assume it does.
    //     ① It does not restart GvMedia:MaxQueuedWaitSeconds. That bound runs from the ORIGINAL arm,
    //        so a long chain of announcements still expires as WaitExpired — the designed staleness
    //        bound, not a hole in this clause.
    //     ② It does not cover the window between WaitForClearAirAsync's predicate deciding to wait and
    //        BeginWait arming the waiter: IsWaiting is still false there, and a raise landing inside it
    //        preempts the playback exactly as it preempts a Preparing one. C-66's re-check closes the
    //        missed-WAKE race in that window; nothing closes this one, and nothing needs to — a
    //        preemption of a not-yet-waiting playback is the Preparing rule doing its job.
    //
    // (c) The trigger IS our own source — never preempt ourselves. StartDuckingAsync raises for the
    //     ATTENDED source too, and EventPlaybackRequest.Priority accepts 1-10, so a caller posting
    //     Priority 8 would otherwise stop its own playback the instant it started ducking. Compared by
    //     REFERENCE on the instance this service holds: three id spaces meet in this file and only the
    //     instance is unambiguous.
    //
    // ⛔ Do NOT collapse (b) and (c) into `victim.Source is null`. That would also stop preempting a
    // PREPARING playback, which is deliberate, shipped, tested behaviour —
    // PreemptingAPreparingPlaybackCancelsAcquisitionAndDisposesWhatItAcquired pins it. Only Waiting is
    // excluded.
    //
    // Source is read OUTSIDE _stateLock deliberately. It is guarded by Playback's own _sourceLock, and
    // nesting that inside _stateLock would introduce a lock ordering this file does not otherwise have.
    // Nothing is lost by reading it a moment later: the decision is addressed by id below, and
    // StopAsync re-checks the id under the gate.
    if (victim is null || victim.IsWaiting || ReferenceEquals(victim.Source, trigger))
    {
      return;
    }

    // Warning, not Information: since LOG-11 the journal carries Warning and above, and "the voicemail
    // stopped by itself" is exactly what an operator diagnoses from the box. Source ids only — never a
    // media id and never request text (PHN-1b §0.3 ④).
    _logger.LogWarning(
      "Attended playback {Id} preempted: source '{SourceId}' started at priority {Priority}, "
      + "at or above GvMedia:PreemptAtPriority ({Threshold})",
      victim.Id, trigger.Id, priority, threshold);

    // Addressed BY ID, captured now. If a replacing StartAsync wins the race the id no longer matches
    // _current and StopAsync is a no-op — which is right: that playback started AFTER the preempting
    // source, so "a source starts" never applied to it. That case is now covered from the other side:
    // since PHN-1f a playback starting under a live >= 8 source WAITS for it rather than mixing
    // (WaitForClearAirAsync, owner decision D28), so the replacing playback publishes Waiting instead
    // of sounding over the announcement. APlaybackStartedUnderAHigherPrioritySourceWaitsAndThenPlays
    // is what pins it; it is the renamed successor of the characterization test that pinned the mixing.
    var victimId = victim.Id;
    _preemptionTail = Task.Run(
      async () =>
      {
        try
        {
          await StopAsync(victimId);
        }
        catch (ObjectDisposedException)
        {
          // The container went away underneath us. Nothing left to stop.
        }
        catch (Exception ex)
        {
          // An unobserved faulted task is a process-level hazard on this box.
          _logger.LogWarning(ex, "Error preempting attended playback {Id}", victimId);
        }
      },
      CancellationToken.None);
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
    // The single funnel for stopping and disposing a source — six callers reach it through
    // TearDownAsync or Dispose — so the single place the cap is disarmed. A ten-second voicemail must
    // not leave a five-minute timer alive behind it.
    playback.DisarmDurationCap();

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
  /// Arms the hard max-duration cap on a playback that has just started producing audio.
  /// </summary>
  /// <remarks>
  /// ⚠ This is NOT CancelAfter on playback.Token, which is what PHN-1c §5 and PHN-1d §5 both
  /// prescribe — and the difference is the whole feature rather than a detail. The token IS observed
  /// after acquisition returns, and that is exactly why cancelling it is the wrong instrument rather
  /// than a merely inert one. AudioSourceBase.PlayAsync forwards it to PlayCoreAsync, and BOTH event
  /// sources build a linked CTS over it there (AudioFileEventSource.PlayCoreAsync,
  /// TTSEventSource.PlayCoreAsync) and await their completion delay on that linked token. So a
  /// cancellation reaches the source — but what it reaches is the COMPLETION path, not the audio.
  /// AudioFileEventSource.PlayWithSoundFlowAsync awaits AwaitCompletionAsync on that token and gates
  /// OnPlaybackCompleted(EndOfContent) behind !IsCancellationRequested; the OperationCanceledException
  /// arm below it only clears _isPlaybackActive. Nothing on that path stops the player — in
  /// AudioFileEventSource only StopCoreAsync and DisposeAsyncCore call
  /// SoundFlowPlaybackService.StopAsync.
  ///
  /// ⚠ TTSEventSource is shaped the same way but is NOT quite the same claim, and the difference was
  /// missed once: it has a THIRD caller, the over-duration safety net in
  /// StartPlaybackWithMonitoringAsync. That one is inside a while (!token.IsCancellationRequested)
  /// loop, so a cancellation exits the loop before it — which is why the conclusion below is the
  /// same for both sources. Stated rather than smoothed over, because "X is the only code that does
  /// Y" is the exact comment shape CLAUDE.md § Pre-Merge Review says this repo gets wrong.
  ///
  /// So a CancelAfter cap at 300 s would leave the audio sounding AND suppress the EndOfContent that
  /// would otherwise have ended it — strictly worse than doing nothing, for something the ADR calls
  /// "the guarantee".
  ///
  /// What actually stops audio is TearDownAsync -> ReleaseSourceAsync, and StopAsync is the public
  /// door to it. Hence a timer whose callback dispatches a stop.
  ///
  /// ⚠ DISPATCHED, never awaited, for OnSourceCompleted's reason: StopAsync takes _gate, and the
  /// callback arrives on a timer thread that must not be parked for the length of a teardown
  /// (ducking release fade included). Idempotence is free — StopAsync resolves by id and
  /// ClaimTerminal admits exactly one terminal transition — so a cap racing a natural end is a
  /// no-op.
  ///
  /// ⚠ CLAMPED AT BOTH ENDS, and each end is load-bearing for a different reason.
  ///
  /// The LOWER clamp is the one with a policy behind it: there is NO off switch, deliberately.
  /// ADR-029 §7.1 calls this the guarantee that everything else is a latency improvement on, and
  /// GvMediaOptions.PreemptAtPriority is this arc's worked example (plan PHN-1d C-43) of a knob that
  /// silently disables a feature while leaving it looking intact. A 0 here means one second, not
  /// "never".
  ///
  /// The UPPER clamp is a crash fix. TimeProvider.CreateTimer rejects a due time above
  /// 0xFFFFFFFE ms (~49.7 days), so an absurd MaxPlaybackSeconds threw ArgumentOutOfRangeException
  /// from HERE — after PlayAsync had returned — landing in AcquireAndPlayAsync's general catch and
  /// failing EVERY attended playback immediately after it started, under a generic failure reason.
  /// One config value, feature dead, no diagnosis. The two neighbouring readers of this same option
  /// already defend their own width (GvMediaCache's Math.Max(60L, …), GvMediaClient's
  /// (long)Math.Max(1, …)); this one now does too. FakeTimeProvider does NOT enforce the same bound,
  /// so no unit test can find this — which is why it is written down here.
  ///
  /// ⚠ WALL-CLOCK FROM PlayAsync, not playing time. PauseAsync neither disarms nor re-arms, so a
  /// playback paused for five minutes is capped having sounded ten seconds. Correct for a guarantee
  /// whose whole point is that it needs no client cooperation — but PR 6 ships the transport that
  /// makes pause reachable, so it is named here rather than rediscovered there.
  /// </remarks>
  private void ArmDurationCap(Playback playback)
  {
    var seconds = Math.Clamp(_gvMediaOptions.CurrentValue.MaxPlaybackSeconds, 1, MaxCapSeconds);
    var playbackId = playback.Id;

    playback.ArmDurationCap(_timeProvider, TimeSpan.FromSeconds(seconds), () =>
    {
      // ⚠ THE WHOLE BODY IS GUARDED, and it is the callback boundary that makes that necessary
      // rather than tidiness. This lambda IS the TimerCallback: System.Threading.Timer does not wrap
      // it, so it runs directly on a thread-pool thread where an unhandled exception TERMINATES THE
      // PROCESS. The try below used to start inside the dispatched Task.Run, leaving the LogWarning
      // and the Task.Run scheduling outside it — and ILogger.Log aggregates and RETHROWS provider
      // exceptions, so a failing Serilog file sink (or a log written after CloseAndFlush during a
      // shutdown racing an armed cap) escaped straight into the runtime. Every sibling background
      // entry point in this file is already guarded for this reason; this one was the exception.
      try
      {
        // Warning, not Information: since LOG-11 the journal carries Warning and above, and "the
        // voicemail stopped by itself after five minutes" is exactly what an operator diagnoses from
        // the box. Ids only — never a media id and never request text (PHN-1b §0.3 ④).
        //
        // ⚠ "dispatching a stop", NOT "stopping it". This line is emitted UNCONDITIONALLY, before the
        // dispatch below and without reading StopAsync's bool result — so it is a record that the
        // timer fired, not a claim that anything was stopped. A playback that ended naturally races
        // this in TWO windows, not one: between the timer becoming due and this callback running, and
        // also while this callback is ALREADY RUNNING, because DisarmDurationCap disposes the timer
        // without waiting for an in-flight callback (deliberately — blocking there would park a
        // teardown on a timer thread). Either way StopAsync returns false having touched nothing, and
        // an operator reading journald sees a WRN about a cap that capped nothing.
        //
        // ⚠ Do NOT restructure this to log on the result. ANaturalEndDisarmsTheDurationCap asserts on
        // the ABSENCE of this line, and it is the only assertion in that test that can fail — a
        // StopCalls assertion there passes whether or not the disarm happened (measured: 62/62 still
        // passed with the disarm deleted). Moving this below the dispatch, or behind the result,
        // destroys that falsifiability.
        //
        // ⚠ WHY that StopCalls assertion cannot fail, corrected: a natural end has already set
        // _current = null in OnSourceCompleted, so StopAsync returns false at the FIRST clause of its
        // `playback is null || playback.Id != playbackId || !playback.ClaimTerminal()` guard and never
        // reaches the source. An earlier revision of this comment credited ClaimTerminal, which is
        // never evaluated on that path — and is a mutating CompareExchange rather than an idempotent
        // read, so it would not have been "idempotence" even if it were.
        _logger.LogWarning(
          "Attended playback {Id} reached GvMedia:MaxPlaybackSeconds ({Seconds}s); dispatching a stop",
          playbackId, seconds);

        _ = Task.Run(
          async () =>
          {
            try
            {
              await StopAsync(playbackId);
            }
            catch (ObjectDisposedException)
            {
              // The container went away underneath the timer. Nothing left to stop.
            }
            catch (Exception ex)
            {
              // An unobserved faulted task is a process-level hazard on this box.
              _logger.LogWarning(ex, "Error stopping capped attended playback {Id}", playbackId);
            }
          },
          CancellationToken.None);
      }
      catch (Exception ex)
      {
        // Best-effort: if the logger itself is what threw, this is expected to do nothing.
        try
        {
          _logger.LogWarning(ex, "Duration-cap callback failed for attended playback {Id}", playbackId);
        }
        catch
        {
          // Nothing left to report with. Swallowing beats killing the process.
        }
      }
    });
  }

  /// <summary>
  /// Releases a source the playback refused to adopt, because it had already ended.
  /// </summary>
  /// <remarks>
  /// Disposal only, never StopAsync: this source was never ducked and never played, so there is
  /// nothing to stop. Guarded the way TearDownAsync guards its steps, because this runs on a
  /// background task where an escaping exception is a process-level hazard.
  ///
  /// ⚠ THE DISPOSAL COMES FIRST AND THE LOG LINE SECOND, and the order is load-bearing rather than
  /// stylistic. ILogger.Log aggregates and RETHROWS provider exceptions — the same fact
  /// <see cref="ArmDurationCap"/>'s remark is built on — so with the log above the try, a failing
  /// Serilog file sink (or a log written after CloseAndFlush during shutdown) skipped the disposal
  /// entirely and re-labelled the failure as something else. Skipping the disposal defeats C-57, which
  /// is the only reason this method is called from the wait's guard at all: on the RemoteMedia arm it
  /// would leak an open FileStream over the cached recording for the life of the process, and on
  /// Windows stop GvMediaCache evicting that entry. A throwing sink now costs the log line and not the
  /// disposal. It still escapes this method, where AcquireAndPlayAsync's general catch turns it into a
  /// Failed snapshot — which is a worse REASON on the snapshot, and not a leak.
  /// </remarks>
  private async Task DisposeOrphanAsync(Playback playback, IEventAudioSource source)
  {
    try { await source.DisposeAsync(); }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error disposing an unadopted source for {Id}", playback.Id);
    }

    _logger.LogDebug(
      "Attended playback {Id} ended while its audio was still being acquired; released it",
      playback.Id);
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
  /// Publishes a NON-terminal state — Waiting, Playing or Paused — for a playback that has not ended.
  /// </summary>
  /// <remarks>
  /// ⚠ The terminal check and the store happen under ONE _stateLock, and that is what makes this
  /// correct rather than merely likely. A source can fail synchronously inside PlayAsync
  /// (AudioFileEventSource.PlayCoreAsync catches and raises Error completion on the calling
  /// thread), so a terminal transition can already be claimed by the time the acquisition path says
  /// "Playing". Publishing it anyway would report audio that never started, and since PHN-1e wired
  /// the hub subscriber it would broadcast a Failed → Playing transition that did not happen. Every
  /// terminal publish stores under the same lock, so the two orderings that remain — this one
  /// first, or skipped entirely — are both honest.
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
  /// PHN-1e connected this to /hubs/audio: AudioStateUpdateService subscribes to PlaybackChanged in
  /// its constructor and broadcasts "EventPlaybackChanged" from OnEventPlaybackChanged. It was
  /// already raised before that subscriber existed, because the IEventPlaybackService contract
  /// requires it and because a broadcast bolted on later would be a second place transitions are
  /// decided.
  ///
  /// ⚠ READ THIS BEFORE ADDING A SUBSCRIBER. Every terminal call site of this method — StartAsync's
  /// replacement arm, StopAsync, OnSourceCompleted and FailAsync — invokes it WHILE HOLDING _gate,
  /// so a subscriber runs on a thread that already owns a non-reentrant semaphore. A hub broadcast
  /// that only serialises and sends is fine, and that is exactly the shape the one shipped subscriber
  /// has (AudioStateUpdateService.OnEventPlaybackChanged: SendAsync and a LogDebug, nothing else — its
  /// own remark says so). A subscriber that re-enters this seam — StopAsync, StartAsync, or anything
  /// that awaits something which does — DEADLOCKS, in exactly the way OnSourceCompleted is written to
  /// avoid. Still flagged rather than fixed: restructuring the publishes to happen outside the gate is
  /// a real change to the ordering guarantees above, and the shipped subscriber does not force it.
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
    private ITimer? _capTimer;

    // D28's wait. Non-null only between the moment acquisition decides the air is not clear and the
    // moment it stops waiting.
    //
    // ⚠ On Playback rather than on the service, deliberately (plan PHN-1f C-65). A service field
    // works today only because a replacing StartAsync cancels the displaced playback synchronously
    // before the replacement can arm its own — true, and one refactor away from not being. Here the
    // wake reads _current under _stateLock and can only ever wake THAT playback's waiter, so "the
    // waiting playback IS _current" is structural rather than incidental.
    private TaskCompletionSource? _waiter;

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

    /// <summary>True while this playback is parked waiting for the air to clear.</summary>
    public bool IsWaiting => Volatile.Read(ref _waiter) is not null;

    /// <summary>Arms the wait and returns the waiter to await.</summary>
    /// <remarks>
    /// ⚠ RunContinuationsAsynchronously is load-bearing, and the overclaim is the trap here so it is
    /// stated exactly. Without it TrySetResult runs the continuation INLINE on the thread that raised
    /// DuckingStateChanged, and that continuation's next acts are a log line and _gate.WaitAsync.
    /// TheWakeDoesNotStartAudioOnTheRaisingThread is what holds it, by the mutation its comment names.
    ///
    /// ⚠ AN EARLIER REVISION OF THIS REMARK SAID A GATE-HOLDING RAISER WAS "ONE REFACTOR AWAY". It is
    /// not. It exists today, unconditionally, on two paths in this very file:
    ///   • the acquisition tail awaits <c>_duckingService.StartDuckingAsync</c> WHILE HOLDING _gate;
    ///   • <see cref="ReleaseSourceAsync"/> awaits <c>_duckingService.StopDuckingAsync</c>, and is
    ///     reached from <see cref="TearDownAsync"/>, four of whose callers hold the gate (StopAsync,
    ///     StartAsync's replacement arm, OnSourceCompleted's dispatched task, FailAsync).
    /// PreemptionIsDispatched_TheRaisingThreadIsNotHeldForTheTeardown says the same from the other
    /// side, and has all along.
    ///
    /// ⚠ And such a raise CAN find a WAITING playback in _current. Worked example, every step of it
    /// reachable today: playback A's source ends naturally, so OnSourceCompleted claims A's terminal
    /// flag and DISPATCHES a gate-taking task; a replacing StartAsync wins the gate first, finds
    /// replaced.ClaimTerminal() already claimed and therefore tears nothing down, installs B as
    /// _current and releases; B's acquisition reaches Waiting; then OnSourceCompleted's task takes the
    /// gate and tears A down — and A's StopDuckingAsync raises from inside the gate, with B waiting.
    ///
    /// So "not a deadlock today" survives, but NOT for the reason that used to be given here. It
    /// survives because SemaphoreSlim.WaitAsync SUSPENDS rather than blocks: an inline continuation
    /// reaching <c>await _gate.WaitAsync(...)</c> on a thread that already holds the gate simply
    /// yields, control returns to the raiser, and the continuation resumes on the pool once the holder
    /// releases. That, and not the absence of a gate-holding raiser, is the actual guarantee.
    ///
    /// What the flag buys is therefore narrower, and still worth having: the raising thread — on the
    /// live path AnnouncementService's, mid-announcement, inside POST /api/notifications/announce —
    /// never executes ANY of the waiting playback's tail, not even the log write that precedes the
    /// gate acquisition, and it keeps that true if anything synchronously blocking is ever added to
    /// that path. OnSourceCompleted and the preemption dispatch are written from the same reasoning,
    /// and their remarks say so.
    /// </remarks>
    public TaskCompletionSource BeginWait()
    {
      var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      Volatile.Write(ref _waiter, waiter);
      return waiter;
    }

    /// <summary>Disarms the wait. Called from the waiter's own finally, so it always runs.</summary>
    public void EndWait() => Volatile.Write(ref _waiter, null);

    /// <summary>Releases a wait if one is armed. Idempotent, and safe from any thread.</summary>
    public bool TryWake() => Volatile.Read(ref _waiter)?.TrySetResult() ?? false;

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

    /// <summary>
    /// Arms the hard max-duration cap on this playback (ADR-029 D7 §7.1).
    /// </summary>
    /// <remarks>
    /// Idempotent: a second arm disposes the first timer, so a re-arm can never leave two running.
    /// Guarded by _sourceLock rather than by a lock of its own — the callback takes no lock at all,
    /// so reusing it introduces no ordering this class does not already have.
    /// </remarks>
    public void ArmDurationCap(TimeProvider timeProvider, TimeSpan after, Action onExpired)
    {
      lock (_sourceLock)
      {
        _capTimer?.Dispose();
        _capTimer = timeProvider.CreateTimer(_ => onExpired(), null, after, Timeout.InfiniteTimeSpan);
      }
    }

    /// <summary>Disarms the cap. Safe when it was never armed, and safe to call twice.</summary>
    /// <remarks>
    /// ⚠ ITimer.Dispose does NOT wait for a callback already running, and it deliberately is not
    /// made to: the callback only dispatches StopAsync(Id), which is idempotent through
    /// ClaimTerminal, so a cap firing at the same instant as a natural end is a no-op rather than a
    /// double stop. Blocking here would mean parking a teardown on a timer thread.
    /// </remarks>
    public void DisarmDurationCap()
    {
      lock (_sourceLock)
      {
        _capTimer?.Dispose();
        _capTimer = null;
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
