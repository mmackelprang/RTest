using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Metrics;

namespace Radio.Fingerprinting.Services;

/// <summary>
/// Background service that identifies audio from the active source using SongRec.
/// All source types (radio, file, vinyl, USB, Bluetooth) use the same SongRec capture-and-recognize path;
/// when an attempt may start is decided by <see cref="FingerprintCallPolicy"/> (per-source schedules, an
/// hourly cap, and a failure back-off).
/// Exposes real-time status via <see cref="GetStatus"/> and <see cref="StatusChanged"/>.
/// </summary>
public class BackgroundIdentificationService : BackgroundService
{
  private readonly ILogger<BackgroundIdentificationService> _logger;
  private readonly IServiceProvider _serviceProvider;
  private readonly IOptionsMonitor<FingerprintingOptions> _optionsMonitor;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly FingerprintCaptureWatchdog? _captureWatchdog;

  /// <summary>Current fingerprinting options (live from IOptionsMonitor).</summary>
  private FingerprintingOptions _options => _optionsMonitor.CurrentValue;

  // Track recent identifications for duplicate suppression (key → (timestamp, confidence))
  private readonly ConcurrentDictionary<string, (DateTime Timestamp, double Confidence)> _recentIdentifications = new();

  // Song change detection: track last identified track to detect transitions
  private (string TrackKey, TrackMetadata Track, DateTime IdentifiedAt)? _lastIdentification;
  private DateTime _lastSongChangeAt = DateTime.MinValue;

  // On-demand identification trigger — cancels the current delay to identify immediately
  private CancellationTokenSource? _delayCts;

  // The owner's SongRec call policy (2026-10-07): per-source schedules, the hourly cap, failure back-off.
  private readonly FingerprintCallPolicy _policy;

  // Known-start sources (file, Bluetooth): the track key last raised for the current track (segment). A
  // validation naming that same song again is not raised — unless the source still reports that it needs a
  // lookup (see ShouldRaise). Touched on the loop thread and by ForgetRecentIdentification, so guarded.
  private readonly object _raisedLock = new();
  private string? _lastRaisedKeyInSegment;
  private CallSegment? _raisedSegment;

  // Logged once per transition to "not available", instead of once per capture.
  private bool _songRecUnavailableLogged;

  // --- Fingerprint status tracking ---
  private readonly object _statusLock = new();
  private FingerprintPhase _currentPhase = FingerprintPhase.Idle;
  private string? _lastError;
  private string? _currentSourceName;

  // Cached status snapshot — only rebuilt when _eventsVersion changes.
  // GetStatus() is called every ~3s by the API; caching avoids rebuilding
  // 40 record copies + List + ReadOnlyCollection on every call.
  private long _eventsVersion;
  private FingerprintStatusSnapshot? _cachedSnapshot;
  private long _cachedSnapshotVersion = -1;

  // Event log — circular buffer of recent events, capped at MaxRecentEvents
  private const int MaxRecentEvents = 40;
  private readonly List<FingerprintEventRecord> _recentEvents = new(MaxRecentEvents + 1);
  private FingerprintEventRecord? _currentEvent;

  // Rate tracking — timestamps within the rolling window used to compute per-minute rates
  private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(5);
  private readonly List<DateTime> _fingerprintTimestamps = new();
  private readonly List<DateTime> _metadataCallTimestamps = new();

  /// <summary>
  /// Event raised when a track is identified.
  /// </summary>
  public event EventHandler<TrackIdentifiedEventArgs>? TrackIdentified;

  /// <summary>
  /// Event raised when a song change is detected (different track identified than previous).
  /// </summary>
  public event EventHandler<SongChangedEventArgs>? SongChanged;

  /// <summary>
  /// Event raised when the fingerprint status changes (phase, event log, rates).
  /// </summary>
  public event EventHandler<FingerprintStatusSnapshot>? StatusChanged;

  /// <summary>
  /// Initializes a new instance of the <see cref="BackgroundIdentificationService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="serviceProvider">The service provider for resolving scoped services.</param>
  /// <param name="options">The fingerprinting options.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  /// <param name="captureWatchdog">
  /// Optional AUD-18 watchdog. While it is latched, this service's per-cycle "no audio samples" Warning is
  /// logged at Debug; the watchdog has already said it once.
  /// </param>
  public BackgroundIdentificationService(
    ILogger<BackgroundIdentificationService> logger,
    IServiceProvider serviceProvider,
    IOptionsMonitor<FingerprintingOptions> options,
    IMetricsCollector? metricsCollector = null,
    FingerprintCaptureWatchdog? captureWatchdog = null)
  {
    _logger = logger;
    _serviceProvider = serviceProvider;
    _optionsMonitor = options;
    _metricsCollector = metricsCollector;
    _captureWatchdog = captureWatchdog;
    _policy = new FingerprintCallPolicy(() => _optionsMonitor.CurrentValue, logger);
  }

  /// <summary>
  /// The clock the call policy schedules against and the loop waits on. Tests substitute a fake. Event
  /// timestamps the sources compare against their own <see cref="DateTime.UtcNow"/> stamps
  /// (<see cref="TrackIdentifiedEventArgs.CaptureStartedAt"/>) stay on the system clock.
  /// </summary>
  internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

  /// <summary>Test seam (kind A — visibility): the call policy this service schedules with.</summary>
  internal FingerprintCallPolicy CallPolicyForTesting => _policy;

  /// <summary>
  /// Returns the current fingerprint identification status snapshot.
  /// Uses a cached snapshot that is only rebuilt when events change.
  /// </summary>
  public FingerprintStatusSnapshot GetStatus()
  {
    lock (_statusLock)
    {
      if (_cachedSnapshot != null && _cachedSnapshotVersion == _eventsVersion)
      {
        // Snapshot is still valid — only refresh rate counters (cheap)
        PruneRateTimestamps();
        return _cachedSnapshot with
        {
          Phase = _currentPhase,
          FingerprintsPerMinute = ComputeRate(_fingerprintTimestamps),
          MetadataCallsPerMinute = ComputeRate(_metadataCallTimestamps),
          LastError = _lastError
        };
      }

      PruneRateTimestamps();
      _cachedSnapshot = new FingerprintStatusSnapshot
      {
        Phase = _currentPhase,
        IsEnabled = _options.Enabled,
        FingerprintsPerMinute = ComputeRate(_fingerprintTimestamps),
        MetadataCallsPerMinute = ComputeRate(_metadataCallTimestamps),
        RecentEvents = _recentEvents.Select(e => e with { }).ToList().AsReadOnly(),
        LastError = _lastError
      };
      _cachedSnapshotVersion = _eventsVersion;
      return _cachedSnapshot;
    }
  }

  /// <summary>
  /// Wakes the loop so it re-reads the active source now instead of at the end of its current wait.
  /// Called by sources when a track changes, new incomplete metadata arrives, or the radio is re-tuned.
  /// </summary>
  /// <remarks>
  /// This does not by itself bypass the call policy. For an unknown-start source (radio, vinyl, USB) it
  /// makes the next attempt due now; for a known-start source (file, Bluetooth) the new track's own
  /// schedule applies — its first attempt is <see cref="FingerprintingOptions.KnownStartFirstCallDelaySeconds"/>
  /// after the track started. The hourly cap and the failure back-off apply either way.
  /// </remarks>
  public void RequestImmediateIdentification()
  {
    _logger.LogDebug("Immediate identification requested");
    _policy.RequestImmediate();
    WakeLoop();
  }

  // Cancels the loop's current wait, if it is in one.
  private void WakeLoop()
  {
    try
    {
      _delayCts?.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // Timer already disposed, ignore
    }
  }

  /// <summary>
  /// Internal test hook: raises the <see cref="TrackIdentified"/> event without
  /// running a real SongRec identification cycle. Used by Infrastructure.Tests
  /// to verify downstream subscribers (e.g., BluetoothAudioSource's
  /// OnTrackIdentified handler) without spinning up the full audio pipeline.
  /// </summary>
  internal void RaiseTrackIdentifiedForTesting(TrackIdentifiedEventArgs e)
  {
    TrackIdentified?.Invoke(this, e);
  }

  /// <summary>
  /// Internal test hook: raises the <see cref="SongChanged"/> event without running an
  /// identification cycle. Used by Infrastructure.Tests to drive PlayHistoryTracker's
  /// song-change handler (AUD-19).
  /// </summary>
  internal void RaiseSongChangedForTesting(SongChangedEventArgs e)
  {
    SongChanged?.Invoke(this, e);
  }

  /// <summary>
  /// Internal test hook: runs exactly one identification cycle, without <c>ExecuteAsync</c>'s start-up delay
  /// or its idle wait. Returns true when the cycle captured audio.
  /// </summary>
  internal async Task<bool> RunOneCycleForTestingAsync(CancellationToken ct = default) =>
    (await IdentifyCurrentAudioAsync(ct)).Captured;

  /// <summary>
  /// Internal test hook: runs exactly one identification cycle and returns its full result, including
  /// whether SongRec was called and when the policy next allows an attempt.
  /// </summary>
  internal Task<CycleResult> RunOneCycleWithResultForTestingAsync(CancellationToken ct = default) =>
    IdentifyCurrentAudioAsync(ct);

  /// <summary>What one identification cycle did.</summary>
  /// <param name="Captured">Audio was captured (a capture that returned no samples does not count).</param>
  /// <param name="CalledRecognizer">A SongRec process was started.</param>
  /// <param name="NextAttemptAt">When the call policy said the next attempt may start, if it said to wait.</param>
  internal readonly record struct CycleResult(bool Captured, bool CalledRecognizer, DateTimeOffset? NextAttemptAt)
  {
    /// <summary>Nothing was captured and no wait time is known: poll again after the idle interval.</summary>
    public static CycleResult Idle => new(false, false, null);
  }

  /// <inheritdoc/>
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.Enabled)
    {
      _logger.LogInformation("Audio fingerprinting is disabled");
      return;
    }

    _logger.LogInformation(
      "Background identification service started (sample duration: {Duration}s, idle poll: {IdlePollMs}ms)",
      _options.SampleDurationSeconds,
      Math.Max(100, _options.IdlePollIntervalMs));

    // Initial delay to let the audio engine initialize
    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
      var cycle = CycleResult.Idle;
      try
      {
        cycle = await IdentifyCurrentAudioAsync(stoppingToken);

        // Clean up old entries from duplicate suppression cache
        CleanupRecentIdentifications();
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during audio identification");
        UpdatePhase(FingerprintPhase.Error, ex.Message);
      }

      try
      {
        // Only on a change: this runs on every idle poll, and each UpdatePhase raises StatusChanged. Error
        // is held while SongRec failures are being backed off, so the status reads "unavailable" for as long
        // as that is true; the next attempt's Capturing replaces it.
        var phase = CurrentPhase;
        if (phase != FingerprintPhase.Idle
            && !(phase == FingerprintPhase.Error && _policy.IsBackingOff(TimeProvider.GetUtcNow())))
        {
          UpdatePhase(FingerprintPhase.Idle);
        }

        if (cycle.CalledRecognizer)
        {
          // Ask the policy straight away: it now holds this attempt's outcome and knows the next due time.
          continue;
        }

        // AUD-35: a cycle that captured nothing may have returned without awaiting anything, so going
        // straight round again was a synchronous tight loop — one core at 99.9 % on the appliance,
        // indefinitely. The 100 ms floor keeps a mistaken 0 from recreating most of the old cost.
        //
        // The same wait covers an attempt the call policy has scheduled for later: the loop sleeps until it
        // is due, but never longer than the idle interval, so it re-reads the source at least that often
        // (a track or source change re-plans the schedule without needing a wake-up).
        // RequestImmediateIdentification cancels the wait.
        var delay = TimeSpan.FromMilliseconds(Math.Max(100, _options.IdlePollIntervalMs));
        if (cycle.NextAttemptAt is { } nextAttemptAt)
        {
          var untilDue = nextAttemptAt - TimeProvider.GetUtcNow();
          if (untilDue < delay)
          {
            delay = untilDue > TimeSpan.Zero ? untilDue : TimeSpan.Zero;
          }
        }

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _delayCts = delayCts;
        await Task.Delay(delay, TimeProvider, delayCts.Token);
        _delayCts = null;
      }
      catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
      {
        _logger.LogDebug("Identification wait interrupted by immediate request");
        _delayCts = null;
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }

    _logger.LogInformation("Background identification service stopped");
  }

  /// <summary>
  /// One pass of the loop: asks the call policy whether an attempt is due for the active source and, if so,
  /// captures audio and sends it to SongRec.
  /// </summary>
  /// <returns>
  /// What the cycle did. A cycle that did not start SongRec must be followed by a wait (AUD-35) — until
  /// <see cref="CycleResult.NextAttemptAt"/> when the policy named one, otherwise the idle interval.
  /// </returns>
  private async Task<CycleResult> IdentifyCurrentAudioAsync(CancellationToken ct)
  {
    _logger.LogDebug("Starting identification cycle");
    var cycleStartTime = DateTime.UtcNow;

    // Resolve services from scope
    using var scope = _serviceProvider.CreateScope();
    var audioTap = scope.ServiceProvider.GetService<IAudioSampleProvider>();

    if (audioTap == null)
    {
      _logger.LogWarning("Audio sample provider not available for fingerprinting");
      return CycleResult.Idle;
    }

    // Check if source is active and needs fingerprinting
    if (!audioTap.IsActive)
    {
      _logger.LogDebug("Audio source not active, skipping identification");
      return CycleResult.Idle;
    }

    var snapshot = new SourceSnapshot(
      audioTap.SourceType,
      audioTap.SourceName ?? audioTap.SourceType.ToString(),
      audioTap.CurrentTrackStartedUtc,
      audioTap.NeedsFingerprintingLookup);
    var decision = _policy.Decide(snapshot, TimeProvider.GetUtcNow());

    if (decision.Kind == CallDecisionKind.NotNeeded)
    {
      _logger.LogDebug("Source {SourceType} does not need fingerprinting, skipping", audioTap.SourceType);
      return CycleResult.Idle;
    }

    if (decision.Kind == CallDecisionKind.Wait)
    {
      _logger.LogDebug("Next identification attempt for {SourceType} not before {NotBefore:u} ({Blocker})",
        audioTap.SourceType, decision.NotBefore?.UtcDateTime, decision.Blocker);
      return new CycleResult(false, false, decision.NotBefore);
    }

    _logger.LogDebug("Audio source active: {SourceType} - {SourceName}", audioTap.SourceType, audioTap.SourceName);

    // Start or continue an event record for this audio segment
    var sourceName = snapshot.SourceName;
    var sourceType = audioTap.SourceType.ToString();
    EnsureCurrentEvent(sourceName, sourceType);
    UpdatePhase(FingerprintPhase.Capturing);

    var sourceTag = audioTap.SourceType.ToString().ToLowerInvariant();
    _metricsCollector?.Increment("fingerprint.identification_attempts", 1,
      new Dictionary<string, string> { ["source"] = sourceTag });

    // Unified path: Capture audio → SongRec recognition. Two clocks on purpose: the policy's (the start of
    // this attempt, for start-to-start intervals) and the system clock the sources' AUD-33 stale-result
    // checks compare against. They are the same clock outside tests.
    var attemptStartedAt = TimeProvider.GetUtcNow();
    var captureStartTime = DateTime.UtcNow;
    var sampleDuration = TimeSpan.FromSeconds(_options.SampleDurationSeconds);
    _logger.LogDebug("Capturing {Duration}s of audio for SongRec", sampleDuration.TotalSeconds);

    var samples = await audioTap.CaptureAsync(sampleDuration, ct);
    var captureElapsed = (DateTime.UtcNow - captureStartTime).TotalMilliseconds;

    _metricsCollector?.Gauge("fingerprint.capture_latency_ms", captureElapsed);
    RecordFingerprintGenerated();

    if (samples == null)
    {
      // AUD-18: the tap has already reported the window to the watchdog by the time it returns, so a latch
      // set by this very window demotes this line too. Other null returns (silence, errors) are unaffected
      // unless the watchdog happens to be latched when they occur.
      var level = _captureWatchdog?.IsLatched == true ? LogLevel.Debug : LogLevel.Warning;
      _logger.Log(level, "No audio samples captured after {Elapsed}ms", captureElapsed);
      UpdatePhase(FingerprintPhase.Error, "No audio samples captured");
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
      // Nothing is recorded with the policy: the attempt stays due, so audio returning after silence is
      // tried again on the next pass rather than after a full interval.
      return CycleResult.Idle;
    }

    _logger.LogDebug("Captured {SampleCount} audio samples in {Elapsed}ms", samples.Samples.Length, captureElapsed);

    // SongRec recognition
    var songRec = scope.ServiceProvider.GetService<ISongRecRecognitionService>();
    if (songRec is not { IsAvailable: true })
    {
      // Once per transition: this used to be a Warning on every capture.
      if (!_songRecUnavailableLogged)
      {
        _songRecUnavailableLogged = true;
        _logger.LogWarning("SongRec not available for identification");
      }
      UpdatePhase(FingerprintPhase.NoMatch);
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
      _policy.RecordOutcome(decision.Segment, attemptStartedAt, CallOutcome.NotCalled, TimeProvider.GetUtcNow());
      return new CycleResult(true, false, null);
    }
    _songRecUnavailableLogged = false;

    UpdatePhase(FingerprintPhase.Querying);
    var lookupStartTime = DateTime.UtcNow;
    double lookupElapsed = 0;

    MetadataLookupResult? result = null;
    var outcome = CallOutcome.Error;
    string? failure = null;

    try
    {
      RecordMetadataCall();
      // Stamped at the attempt's start, the instant the schedule is measured from: stamped after the 13 s
      // capture, a 15 s schedule at the default 240/hour cap would be held ~13 s every hour and log a cap
      // Warning on ordinary listening.
      _policy.RecordCallStarted(attemptStartedAt);
      var recognition = await songRec.RecognizeAsync(samples, ct);
      lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;

      _metricsCollector?.Gauge("fingerprint.songrec_latency_ms", lookupElapsed);

      if (recognition.Outcome == SongRecOutcome.Error)
      {
        failure = recognition.Error ?? "unknown SongRec failure";
        _logger.LogDebug("SongRec recognition failed: {Error}", failure);
        _metricsCollector?.Increment("fingerprint.identification_failures", 1,
          new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
      }
      else if (recognition is { Outcome: SongRecOutcome.Match, Track: { } songRecMetadata })
      {
        outcome = CallOutcome.Match;
        // Cache album art from Shazam CDN to serve locally
        if (!string.IsNullOrEmpty(songRecMetadata.CoverArtUrl))
        {
          try
          {
            var albumArtCache = scope.ServiceProvider.GetService<IAlbumArtCacheService>();
            if (albumArtCache != null)
            {
              var localPath = await albumArtCache.SaveFromUrlAsync(songRecMetadata.CoverArtUrl);
              if (localPath != null)
              {
                songRecMetadata = songRecMetadata with { CoverArtUrl = localPath };
              }
            }
          }
          catch (Exception ex)
          {
            _logger.LogDebug(ex, "Failed to cache album art from {Url}, keeping external URL",
              songRecMetadata.CoverArtUrl);
          }
        }

        result = new MetadataLookupResult
        {
          IsMatch = true,
          Confidence = 0.8, // SongRec doesn't provide a numeric confidence score
          FingerprintId = Guid.NewGuid().ToString(),
          Metadata = songRecMetadata,
          Source = LookupSource.SongRec
        };

        _metricsCollector?.Increment("fingerprint.identification_successes", 1,
          new Dictionary<string, string> { ["source"] = sourceTag });
      }
      else
      {
        outcome = CallOutcome.NoMatch;
        // LOG-12: Debug — the one no-match line kept (SongRecRecognitionService's is Trace).
        _logger.LogDebug("SongRec returned no match");
        _metricsCollector?.Increment("fingerprint.identification_failures", 1,
          new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "no_match" });
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      // An implementation that throws instead of returning SongRecOutcome.Error is still a failure.
      lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;
      failure = ex.Message;
      _logger.LogDebug(ex, "SongRec recognition threw");
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
    }

    _policy.RecordOutcome(decision.Segment, attemptStartedAt, outcome, TimeProvider.GetUtcNow(), failure);
    _metricsCollector?.Gauge("fingerprint.consecutive_failures", _policy.ConsecutiveErrors);

    // Update event record with result
    if (result?.IsMatch == true && result.Metadata != null)
    {
      var trackKey = TrackKey(result.Metadata);
      UpdateCurrentEventMatch(result.Metadata, result.Confidence);
      UpdatePhase(FingerprintPhase.Matched);

      if (!ShouldRaise(decision.Segment, trackKey, audioTap))
      {
        _logger.LogDebug("Suppressing duplicate identification: {Title} by {Artist}",
          result.Metadata.Title, result.Metadata.Artist);
        _metricsCollector?.Increment("fingerprint.duplicate_suppressions");
        return new CycleResult(true, true, null);
      }

      MarkAsRecentlyIdentified(trackKey, result.Confidence);

      // Song change detection: compare with last identification
      DetectSongChange(trackKey, result.Metadata, result.Confidence, sourceTag);
    }
    else if (outcome == CallOutcome.Error)
    {
      // A failure is not a statement about the audio, so it is not counted as a no-match row; the Error
      // phase also makes the next attempt start a fresh event record (EnsureCurrentEvent).
      UpdatePhase(FingerprintPhase.Error, failure);
    }
    else
    {
      UpdateCurrentEventNoMatch();
      UpdatePhase(FingerprintPhase.NoMatch);
    }

    // Raise event for UI updates and play history updates
    if (result?.IsMatch == true && result.Metadata != null)
    {
      _logger.LogInformation(
        "Identified track: '{Title}' by '{Artist}' (confidence: {Confidence:P0}, source: {Source}, coverArt: {CoverArtUrl})",
        result.Metadata.Title, result.Metadata.Artist, result.Confidence,
        result.Source, result.Metadata.CoverArtUrl ?? "(none)");

      // AUD-33: carry the capture start so a source can drop a result sampled from a track the
      // listener has since skipped away from.
      TrackIdentified?.Invoke(
        this, new TrackIdentifiedEventArgs(result.Metadata, result.Confidence, captureStartTime));
    }
    else
    {
      _logger.LogDebug("Track not identified via fingerprinting");
    }

    var totalElapsed = (DateTime.UtcNow - cycleStartTime).TotalMilliseconds;
    _logger.LogDebug("Identification cycle completed in {TotalElapsed}ms (lookup: {Lookup}ms)",
      totalElapsed, lookupElapsed);
    return new CycleResult(true, true, null);
  }

  /// <summary>
  /// Whether a match should be raised as <see cref="TrackIdentified"/>, or suppressed as a duplicate.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Unknown-start sources (radio, vinyl, USB) keep the time-based window
  /// (<see cref="FingerprintingOptions.DuplicateSuppressionMinutes"/>): the same song comes round on every
  /// attempt while it plays, and raising each one would only repeat itself.
  /// </para>
  /// <para>
  /// Known-start sources (file, Bluetooth) are scoped to the track instead. Every new track raises its first
  /// match, and a later match is suppressed only when it names the song last raised for this track AND the
  /// source no longer reports needing a lookup. Before the call policy these sources used the time window
  /// too, and it was a loop: replaying a file whose song had been identified less than five minutes
  /// earlier had its match suppressed, so the file player never saw it, never cleared its lookup flag and
  /// (without embedded art) never got its art back — and the loop re-called SongRec every cycle for the
  /// rest of the window (estimated from the code at ~15-18 extra calls per such track).
  /// </para>
  /// </remarks>
  private bool ShouldRaise(CallSegment segment, string trackKey, IAudioSampleProvider tap)
  {
    if (!segment.KnownStart)
    {
      return !IsDuplicateIdentification(trackKey);
    }

    var sourceStillNeedsLookup = tap.NeedsFingerprintingLookup;
    lock (_raisedLock)
    {
      if (segment != _raisedSegment)
      {
        _raisedSegment = segment;
        _lastRaisedKeyInSegment = null;
      }

      if (trackKey == _lastRaisedKeyInSegment && !sourceStillNeedsLookup)
      {
        return false;
      }

      _lastRaisedKeyInSegment = trackKey;
      return true;
    }
  }

  /// <summary>
  /// Resets the song change detection state, and the call schedule, for a source switch. Wakes the loop so
  /// the new source is planned for now rather than at the end of the old source's wait.
  /// </summary>
  /// <remarks>
  /// The hourly cap and the SongRec failure back-off are deliberately NOT reset: they protect the Shazam
  /// account, not a source, and switching sources must not be a way round them. (Before the call policy
  /// this reset the failure count.)
  /// </remarks>
  public void ResetSongChangeState()
  {
    _lastIdentification = null;
    _lastSongChangeAt = DateTime.MinValue;
    _policy.ResetSegment();
    _logger.LogDebug("Song change detection state reset");
    WakeLoop();
  }

  private FingerprintPhase CurrentPhase
  {
    get
    {
      lock (_statusLock)
      {
        return _currentPhase;
      }
    }
  }

  // --- Status tracking helpers ---

  private void UpdatePhase(FingerprintPhase phase, string? error = null)
  {
    lock (_statusLock)
    {
      _currentPhase = phase;
      if (error != null)
      {
        _lastError = error;
      }
      if (_currentEvent != null)
      {
        _currentEvent.Phase = phase;
        _currentEvent.Timestamp = DateTime.UtcNow;
      }
      _eventsVersion++;
    }
    FireStatusChanged();
  }

  /// <summary>
  /// Ensures a current event record exists for the given source.
  /// Only starts a new record when the source changes or after an error;
  /// same-source match/no-match results aggregate into the existing record.
  /// </summary>
  internal void EnsureCurrentEvent(string sourceName, string sourceType = "")
  {
    lock (_statusLock)
    {
      // Start a new record only if: no current event, source changed, or error (terminal)
      if (_currentEvent == null || _currentSourceName != sourceName ||
          _currentEvent.Phase == FingerprintPhase.Error)
      {
        _currentEvent = new FingerprintEventRecord { AudioSource = sourceName, SourceType = sourceType };
        _currentSourceName = sourceName;
        _recentEvents.Add(_currentEvent);
        if (_recentEvents.Count > MaxRecentEvents)
        {
          _recentEvents.RemoveAt(0);
        }
        _eventsVersion++;
      }
    }
  }

  /// <summary>
  /// Updates the current event record with a successful match.
  /// Same source + same title → Count++; different title or was no-match → new row.
  /// </summary>
  internal void UpdateCurrentEventMatch(TrackMetadata metadata, double confidence)
  {
    lock (_statusLock)
    {
      if (_currentEvent == null)
      {
        return;
      }

      _eventsVersion++;

      // Aggregate if same source, same title, and already a match row
      if (_currentEvent.IsMatch && _currentEvent.Title == metadata.Title)
      {
        _currentEvent.Count++;
        _currentEvent.LastConfidence = confidence;
        _currentEvent.HasAlbumArt = !string.IsNullOrEmpty(metadata.CoverArtUrl);
        _currentEvent.Timestamp = DateTime.UtcNow;
        return;
      }

      // Fresh event from EnsureCurrentEvent (Count=0) → convert in place
      if (_currentEvent.Count == 0)
      {
        _currentEvent.IsMatch = true;
        _currentEvent.Count = 1;
        _currentEvent.LastConfidence = confidence;
        _currentEvent.Title = metadata.Title;
        _currentEvent.Artist = metadata.Artist;
        _currentEvent.Album = metadata.Album;
        _currentEvent.HasAlbumArt = !string.IsNullOrEmpty(metadata.CoverArtUrl);
        _currentEvent.Timestamp = DateTime.UtcNow;
        return;
      }

      // Different title or was a no-match row with data → new record
      _currentEvent = new FingerprintEventRecord
      {
        AudioSource = _currentSourceName ?? "Unknown",
        SourceType = _currentEvent.SourceType,
        IsMatch = true,
        Count = 1,
        LastConfidence = confidence,
        Title = metadata.Title,
        Artist = metadata.Artist,
        Album = metadata.Album,
        HasAlbumArt = !string.IsNullOrEmpty(metadata.CoverArtUrl),
        Timestamp = DateTime.UtcNow
      };
      _recentEvents.Add(_currentEvent);
      if (_recentEvents.Count > MaxRecentEvents)
      {
        _recentEvents.RemoveAt(0);
      }
    }
  }

  /// <summary>
  /// Updates the current event record with a no-match result.
  /// Current is not a match row → Count++; otherwise → new row.
  /// </summary>
  internal void UpdateCurrentEventNoMatch()
  {
    lock (_statusLock)
    {
      if (_currentEvent == null)
      {
        return;
      }

      _eventsVersion++;

      // Aggregate into existing no-match row (or fresh empty row from EnsureCurrentEvent)
      if (!_currentEvent.IsMatch)
      {
        _currentEvent.Count++;
        _currentEvent.Timestamp = DateTime.UtcNow;
        return;
      }

      // Was a match row → start new no-match record
      _currentEvent = new FingerprintEventRecord
      {
        AudioSource = _currentSourceName ?? "Unknown",
        SourceType = _currentEvent.SourceType,
        IsMatch = false,
        Count = 1,
        Timestamp = DateTime.UtcNow
      };
      _recentEvents.Add(_currentEvent);
      if (_recentEvents.Count > MaxRecentEvents)
      {
        _recentEvents.RemoveAt(0);
      }
    }
  }

  private void RecordFingerprintGenerated()
  {
    lock (_statusLock)
    {
      _fingerprintTimestamps.Add(DateTime.UtcNow);
    }
  }

  private void RecordMetadataCall()
  {
    lock (_statusLock)
    {
      _metadataCallTimestamps.Add(DateTime.UtcNow);
    }
  }

  private void PruneRateTimestamps()
  {
    var cutoff = DateTime.UtcNow - RateWindow;
    _fingerprintTimestamps.RemoveAll(t => t < cutoff);
    _metadataCallTimestamps.RemoveAll(t => t < cutoff);
  }

  private static double ComputeRate(List<DateTime> timestamps)
  {
    if (timestamps.Count == 0)
    {
      return 0;
    }
    var oldest = timestamps[0];
    var elapsed = (DateTime.UtcNow - oldest).TotalMinutes;
    return elapsed > 0 ? timestamps.Count / elapsed : 0;
  }

  private void FireStatusChanged()
  {
    try
    {
      StatusChanged?.Invoke(this, GetStatus());
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error firing StatusChanged event");
    }
  }

  // --- Existing helpers ---

  private void DetectSongChange(string trackKey, TrackMetadata newTrack, double confidence, string sourceTag)
  {
    var now = DateTime.UtcNow;

    if (_lastIdentification == null)
    {
      // First identification — record it, no song change event
      _lastIdentification = (trackKey, newTrack, now);
      _logger.LogDebug("First identification recorded: '{Title}' by '{Artist}'",
        newTrack.Title, newTrack.Artist);
      return;
    }

    // Same track — update timestamp, no song change
    if (trackKey == _lastIdentification.Value.TrackKey)
    {
      _lastIdentification = (_lastIdentification.Value.TrackKey, _lastIdentification.Value.Track, now);
      return;
    }

    // Different track detected — check minimum interval to prevent rapid-fire events
    var secondsSinceLastChange = (now - _lastSongChangeAt).TotalSeconds;
    if (secondsSinceLastChange < _options.MinimumSecondsBetweenSongChanges)
    {
      _logger.LogDebug(
        "Song change suppressed (only {Seconds:F0}s since last change, minimum is {Min}s): '{OldTitle}' → '{NewTitle}'",
        secondsSinceLastChange, _options.MinimumSecondsBetweenSongChanges,
        _lastIdentification.Value.Track.Title, newTrack.Title);
      return;
    }

    // Song change confirmed!
    var previousTrack = _lastIdentification.Value.Track;
    _lastIdentification = (trackKey, newTrack, now);
    _lastSongChangeAt = now;

    _logger.LogInformation(
      "Song change detected: '{OldTitle}' by '{OldArtist}' -> '{NewTitle}' by '{NewArtist}' (confidence: {Confidence:P0})",
      previousTrack.Title, previousTrack.Artist,
      newTrack.Title, newTrack.Artist, confidence);

    _metricsCollector?.Increment("fingerprint.song_changes", 1,
      new Dictionary<string, string> { ["source"] = sourceTag });

    SongChanged?.Invoke(this, new SongChangedEventArgs(previousTrack, newTrack, confidence));
  }

  /// <summary>
  /// Removes <paramref name="track"/> from duplicate suppression, so the next identification of it is
  /// raised rather than suppressed.
  /// </summary>
  /// <remarks>
  /// AUD-33: a result is marked as recently identified BEFORE <see cref="TrackIdentified"/> is raised.
  /// A source that then drops it as stale (sampled before its current track started) calls this, or the
  /// song it dropped would be suppressed for the whole window — even when that song is the one now
  /// playing (a capture straddling the skip, or Bluetooth AVRCP metadata arriving after the audio).
  /// </remarks>
  public void ForgetRecentIdentification(TrackMetadata track)
  {
    var key = TrackKey(track);
    _recentIdentifications.TryRemove(key, out _);

    // The known-start (per-track) suppression in ShouldRaise is marked before the raise in the same way.
    lock (_raisedLock)
    {
      if (_lastRaisedKeyInSegment == key)
      {
        _lastRaisedKeyInSegment = null;
      }
    }
  }

  /// <summary>Test seam (kind B — injection): writes the suppression entry a real identification cycle
  /// writes. A real cycle CAN be driven (<c>BackgroundIdentificationServiceCaptureTimeTests</c> does, with a
  /// mock tap and SongRec), but not paused between the mark and the raise, and each run pays the service's
  /// fixed 5 s start-up delay — so source tests that need "marked, then raised" state write it here.</summary>
  internal void MarkAsRecentlyIdentifiedForTesting(TrackMetadata track, double confidence) =>
    MarkAsRecentlyIdentified(TrackKey(track), confidence);

  /// <summary>Test seam (kind A — visibility): whether duplicate suppression would block <paramref name="track"/>.</summary>
  internal bool IsSuppressedAsDuplicateForTesting(TrackMetadata track) =>
    IsDuplicateIdentification(TrackKey(track));

  private static string TrackKey(TrackMetadata track) => $"{track.Title}|{track.Artist}";

  private bool IsDuplicateIdentification(string trackKey)
  {
    if (_recentIdentifications.TryGetValue(trackKey, out var entry))
    {
      var elapsed = DateTime.UtcNow - entry.Timestamp;
      // High-confidence matches get a longer suppression window
      var suppressionMinutes = entry.Confidence > 0.9
        ? _options.HighConfidenceDuplicateSuppressionMinutes
        : _options.DuplicateSuppressionMinutes;
      return elapsed.TotalMinutes < suppressionMinutes;
    }

    return false;
  }

  private void MarkAsRecentlyIdentified(string trackKey, double confidence)
  {
    _recentIdentifications[trackKey] = (DateTime.UtcNow, confidence);
  }

  private void CleanupRecentIdentifications()
  {
    var maxSuppression = Math.Max(
      _options.DuplicateSuppressionMinutes,
      _options.HighConfidenceDuplicateSuppressionMinutes);
    var cutoff = DateTime.UtcNow.AddMinutes(-maxSuppression * 2);
    var keysToRemove = _recentIdentifications
      .Where(kvp => kvp.Value.Timestamp < cutoff)
      .Select(kvp => kvp.Key)
      .ToList();

    foreach (var key in keysToRemove)
    {
      _recentIdentifications.TryRemove(key, out _);
    }
  }
}
