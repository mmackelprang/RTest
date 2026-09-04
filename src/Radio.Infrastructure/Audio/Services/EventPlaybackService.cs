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
/// mixer itself, and AddSource only mutates a bookkeeping list. SourcesController calls it and
/// never removes, which is where its per-play leak comes from.
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

  // Serialises the transitions that install or tear down a playback. Async because teardown awaits
  // StopDuckingAsync / StopAsync / DisposeAsync.
  //
  // ⚠ The PlaybackCompleted handler must NEVER wait on this. That event is raised from inside
  // StopCoreAsync, which this service calls while holding the gate — so a handler that waited here
  // would deadlock on a non-reentrant semaphore. The handler instead claims the terminal flag and
  // returns; see OnSourceCompleted.
  private readonly SemaphoreSlim _gate = new(1, 1);

  // Guards the two fields below only. Never held across an await.
  private readonly object _stateLock = new();
  private Playback? _current;
  private EventPlaybackSnapshot? _snapshot;

  private bool _disposed;

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
    ObjectDisposedException.ThrowIf(_disposed, this);

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
    ObjectDisposedException.ThrowIf(_disposed, this);

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
  public async Task<bool> SeekAsync(
    string playbackId, TimeSpan position, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || !source.IsSeekable)
    {
      // Reported as false rather than by letting EventAudioSourceBase.SeekAsync throw
      // NotSupportedException: "this cannot scrub" is an ordinary answer, not an exception. The
      // return is narrower than "the audio moved" and the interface's remarks say exactly why.
      return false;
    }

    await source.SeekAsync(position, cancellationToken);
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
    ObjectDisposedException.ThrowIf(_disposed, this);

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
    ObjectDisposedException.ThrowIf(_disposed, this);

    var playback = Resolve(playbackId);
    if (playback?.Source is not { } source || source.State != AudioSourceState.Paused)
    {
      return false;
    }

    await source.ResumeAsync(cancellationToken);
    PublishNonTerminal(playback, EventPlaybackState.Playing);
    return true;
  }

  /// <summary>Cancels anything in flight and releases the gate.</summary>
  /// <remarks>
  /// Cancel rather than tear down: Dispose is synchronous, teardown is not, and the acquisition
  /// task's own catch handles the cancellation. What this guarantees is that no fetch or synthesis
  /// keeps running after the container has gone.
  ///
  /// ⚠ A background completion or failure that reaches _gate after this has disposed it observes
  /// an ObjectDisposedException, which both of those paths catch by name. The transport methods
  /// cannot: ObjectDisposedException.ThrowIf rejects them before they touch the gate, which is the
  /// correct answer for a call made against a disposed service.
  /// </remarks>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    Playback? playback;
    lock (_stateLock)
    {
      playback = _current;
      _current = null;
    }

    playback?.Cancel();
    _gate.Dispose();
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

      token.ThrowIfCancellationRequested();

      playback.Source = source;
      source.PlaybackCompleted += (_, e) => OnSourceCompleted(playback, e);

      _duckingService.SetPriority(source, request.Priority);
      await _duckingService.StartDuckingAsync(source, token);

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
      _logger.LogDebug("Attended playback {Id} cancelled during acquisition", playback.Id);
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
    // "parameters?.X ?? opts.X", and every one of those four ?? is lifted by the null-conditional
    // on the OBJECT — so they fire only when parameters itself is null. TWO of the four fields
    // still carry the trap that follows from that: Speed and Pitch are non-nullable with a 1.0f
    // initializer, so any non-null TTSParameters silently pins them to the TYPE's default rather
    // than to configuration. Engine and Voice are nullable now and do fall back correctly, so this
    // filling them is belt-and-braces rather than load-bearing — but passing null instead would be
    // correct only until VoiceId is set, which is the trap re-armed, so it is never passed.
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
      catch (ObjectDisposedException)
      {
        // The container went away underneath us. Nothing to publish to.
      }
      catch (Exception ex)
      {
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
    catch (ObjectDisposedException)
    {
      // Disposed mid-failure. Nothing to publish to.
    }
  }

  /// <summary>
  /// Stops ducking, stops the source and disposes it. Every step is independently guarded: this
  /// runs on the failure path too, where any of them may already be in a bad state, and a throw
  /// here would leave the seam holding a playback it can never clear.
  /// </summary>
  /// <remarks>
  /// The caller must have claimed the terminal flag first. That is what keeps source.StopAsync's
  /// UserStopped event — raised synchronously from inside this call — from re-entering _gate.
  /// </remarks>
  private async Task TearDownAsync(Playback playback)
  {
    playback.Cancel();

    if (playback.Source is not { } source)
    {
      return;
    }

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
    private readonly CancellationTokenSource _cts = new();
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
    public IEventAudioSource? Source { get; set; }
    public TimeSpan? ReportedDuration { get; set; }
    public CancellationToken Token => _cts.Token;

    /// <summary>True once any terminal transition has been claimed. Never goes back to false.</summary>
    public bool IsTerminal => Volatile.Read(ref _terminal) != 0;

    /// <summary>
    /// True for the FIRST caller only. Every terminal transition — natural completion, user stop,
    /// replacement, failure — goes through this, so a playback ends exactly once no matter how many
    /// PlaybackCompleted events its source raises.
    /// </summary>
    public bool ClaimTerminal() => Interlocked.CompareExchange(ref _terminal, 1, 0) == 0;

    public void Cancel()
    {
      try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already cancelled and disposed */ }
    }
  }
}
