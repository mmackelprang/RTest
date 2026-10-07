using Microsoft.Extensions.Logging;
using Radio.Core.Models.Audio;

namespace Radio.Fingerprinting.Services;

/// <summary>What <see cref="FingerprintCallPolicy.Decide"/> tells the identification loop to do.</summary>
internal enum CallDecisionKind
{
  /// <summary>Capture now and send the capture to SongRec.</summary>
  CallNow,

  /// <summary>An attempt is wanted, but not before <see cref="CallDecision.NotBefore"/>.</summary>
  Wait,

  /// <summary>The source's own metadata is complete and SongRec has not matched this track: no attempt is wanted.</summary>
  NotNeeded
}

/// <summary>Why a <see cref="CallDecisionKind.Wait"/> decision is waiting — the latest of the three constraints.</summary>
internal enum CallBlocker
{
  /// <summary>Not waiting.</summary>
  None,

  /// <summary>The source's schedule: the next attempt is not due yet.</summary>
  Schedule,

  /// <summary>Backing off after consecutive SongRec failures.</summary>
  ErrorBackoff,

  /// <summary><see cref="FingerprintingOptions.MaxCallsPerHour"/> calls have been made in the last 60 minutes.</summary>
  HourlyCap
}

/// <summary>How an attempt the policy allowed ended, as far as the schedule is concerned.</summary>
internal enum CallOutcome
{
  /// <summary>SongRec ran cleanly and matched.</summary>
  Match,

  /// <summary>SongRec ran cleanly without a match.</summary>
  NoMatch,

  /// <summary>SongRec failed (timeout, non-zero exit, unparsable output). Counts toward the back-off.</summary>
  Error,

  /// <summary>Audio was captured but SongRec was not called (it is not available). Advances the schedule only.</summary>
  NotCalled
}

/// <summary>
/// What the policy schedules against: one source, and — for a known-start source — one track on it. A change
/// of any field starts a new schedule.
/// </summary>
/// <param name="SourceType">The active source's type.</param>
/// <param name="SourceName">The active source's name.</param>
/// <param name="TrackStartedUtc">
/// Known-start sources only: when the current track started or the source last became active, whichever is
/// later. Always null for an unknown-start source, so its schedule survives across songs.
/// </param>
internal sealed record CallSegment(PlaySource SourceType, string SourceName, DateTime? TrackStartedUtc)
{
  /// <summary>Whether this is a known-start source (see <see cref="FingerprintCallPolicy.IsKnownStart"/>).</summary>
  public bool KnownStart => FingerprintCallPolicy.IsKnownStart(SourceType);
}

/// <summary>What the identification loop read from the audio tap before asking the policy.</summary>
/// <param name="SourceType">The active source's type.</param>
/// <param name="SourceName">The active source's name.</param>
/// <param name="TrackStartedUtc">The tap's <c>CurrentTrackStartedUtc</c>; ignored for unknown-start sources.</param>
/// <param name="NeedsLookup">The tap's <c>NeedsFingerprintingLookup</c>.</param>
internal readonly record struct SourceSnapshot(
  PlaySource SourceType, string SourceName, DateTime? TrackStartedUtc, bool NeedsLookup);

/// <summary>The policy's answer for one loop iteration.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Segment">The schedule the decision belongs to; pass it back to <see cref="FingerprintCallPolicy.RecordOutcome"/>.</param>
/// <param name="NotBefore">For <see cref="CallDecisionKind.Wait"/>: the earliest instant an attempt may start.</param>
/// <param name="Blocker">For <see cref="CallDecisionKind.Wait"/>: which constraint sets <paramref name="NotBefore"/>.</param>
internal readonly record struct CallDecision(
  CallDecisionKind Kind, CallSegment Segment, DateTimeOffset? NotBefore, CallBlocker Blocker);

/// <summary>
/// Decides when the identification loop may capture audio and call SongRec (Shazam): the owner's call
/// policy of 2026-10-07 (unknown-start schedule revised the same day to a 13 s capture every 15 s), written to stop the appliance
/// over-calling Shazam (measured ~235 calls/hour on radio, nonstop, with no limiter at all).
/// </summary>
/// <remarks>
/// <para>
/// <b>Known-start sources</b> (file player, Bluetooth) know when each track begins. A track is sent to
/// SongRec only while the source reports its metadata incomplete (title, artist or album art missing): the
/// first attempt starts <see cref="FingerprintingOptions.KnownStartFirstCallDelaySeconds"/> after the track
/// does; a no-match is retried after <see cref="FingerprintingOptions.KnownStartFirstRetryDelaySeconds"/>,
/// then every <see cref="FingerprintingOptions.KnownStartRetryIntervalSeconds"/>; once SongRec has matched
/// the track it is validated every <see cref="FingerprintingOptions.KnownStartValidationIntervalSeconds"/>
/// until the track changes, even though the match has usually completed the metadata by then.
/// </para>
/// <para>
/// <b>Unknown-start sources</b> (radio, vinyl, USB, anything else) get an attempt every
/// <see cref="FingerprintingOptions.UnknownStartIntervalSeconds"/> whether or not the last one matched. A
/// new source, <see cref="RequestImmediate"/> (a re-tune), or a capture that found only silence (nothing is
/// recorded, so the attempt stays due) makes the next attempt immediate — and because every recorded attempt
/// sets the next due time from its own start, that immediate attempt restarts the schedule instead of adding
/// a call on top of it. At the 15 s default that holds sustained use to ~240 calls/hour, the cap's budget.
/// </para>
/// <para>
/// Every interval is measured from the start of one attempt's capture to the start of the next. Two global
/// limits apply on top, to every source: at most <see cref="FingerprintingOptions.MaxCallsPerHour"/> SongRec
/// calls in any rolling hour, and an exponential back-off after consecutive SongRec failures.
/// </para>
/// <para>
/// The policy holds no clock: every method takes <c>now</c>, so tests drive it with explicit instants. It
/// is called from the identification loop and, for <see cref="RequestImmediate"/> and
/// <see cref="ResetSegment"/>, from other threads; every member takes the same lock.
/// </para>
/// </remarks>
internal sealed class FingerprintCallPolicy
{
  private static readonly TimeSpan CapWindow = TimeSpan.FromHours(1);

  private readonly Func<FingerprintingOptions> _options;
  private readonly ILogger _logger;
  private readonly object _lock = new();

  // Rolling-hour cap: start times of the SongRec calls in the last hour, oldest first.
  private readonly Queue<DateTimeOffset> _callTimes = new();
  private bool _capEpisodeOpen;

  // Failure back-off.
  private int _consecutiveErrors;
  private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;
  private bool _errorWarningLogged;

  // The current schedule.
  private CallSegment? _segment;
  private DateTimeOffset _nextDue;
  private int _noMatchesInSegment;
  private bool _matchedInSegment;
  private bool _immediateRequested;

  /// <summary>Creates a policy that reads its settings from <paramref name="options"/> on every decision.</summary>
  /// <param name="options">The current options; called on every decision so config-store changes apply live.</param>
  /// <param name="logger">Receives the once-per-episode Warnings (cap reached, repeated failures).</param>
  public FingerprintCallPolicy(Func<FingerprintingOptions> options, ILogger logger)
  {
    _options = options;
    _logger = logger;
  }

  /// <summary>
  /// True for sources that know when each track starts: the file player and Bluetooth. Everything else —
  /// radio, vinyl, generic USB, and any future source — is unknown-start.
  /// </summary>
  public static bool IsKnownStart(PlaySource sourceType) =>
    sourceType is PlaySource.File or PlaySource.Bluetooth;

  /// <summary>Consecutive SongRec failures since the last clean call.</summary>
  public int ConsecutiveErrors
  {
    get
    {
      lock (_lock)
      {
        return _consecutiveErrors;
      }
    }
  }

  /// <summary>SongRec calls recorded in the 60 minutes before <paramref name="now"/>.</summary>
  public int CallsInLastHour(DateTimeOffset now)
  {
    lock (_lock)
    {
      PruneCallTimes(now);
      return _callTimes.Count;
    }
  }

  /// <summary>
  /// Makes the next attempt for an unknown-start source immediate (still subject to the cap and the
  /// back-off). Used for a re-tune. Has no effect on a known-start source, whose schedule is anchored to
  /// the track's start; the flag is consumed by the next <see cref="Decide"/> either way.
  /// </summary>
  public void RequestImmediate()
  {
    lock (_lock)
    {
      _immediateRequested = true;
    }
  }

  /// <summary>
  /// Forgets the current schedule, so the next <see cref="Decide"/> starts a new one even if the source's
  /// identity has not changed — the audio manager switching away from a source and back to it. The cap and
  /// the failure back-off are global and are not reset.
  /// </summary>
  public void ResetSegment()
  {
    lock (_lock)
    {
      _segment = null;
    }
  }

  /// <summary>Decides whether an attempt may start at <paramref name="now"/> for the source described by <paramref name="source"/>.</summary>
  public CallDecision Decide(SourceSnapshot source, DateTimeOffset now)
  {
    var settings = Settings.From(_options());

    lock (_lock)
    {
      var knownStart = IsKnownStart(source.SourceType);
      var segment = new CallSegment(source.SourceType, source.SourceName, knownStart ? source.TrackStartedUtc : null);
      if (segment != _segment)
      {
        StartSegment(segment, now, settings);
      }

      var immediate = _immediateRequested;
      _immediateRequested = false;
      if (immediate && !knownStart && _nextDue > now)
      {
        _nextDue = now;
      }

      // A recognizer match keeps the validation schedule running even though the match has usually
      // completed the metadata (and so cleared the source's lookup flag). Before any match, the source's
      // own flag decides.
      if (!source.NeedsLookup && !(knownStart && _matchedInSegment))
      {
        return new CallDecision(CallDecisionKind.NotNeeded, segment, null, CallBlocker.None);
      }

      var due = _nextDue;
      var blocker = CallBlocker.Schedule;
      if (_backoffUntil > due)
      {
        due = _backoffUntil;
        blocker = CallBlocker.ErrorBackoff;
      }

      var capFreeAt = CapFreeAt(now, settings.MaxCallsPerHour);
      if (capFreeAt > due)
      {
        due = capFreeAt;
        blocker = CallBlocker.HourlyCap;
      }

      if (due <= now)
      {
        return new CallDecision(CallDecisionKind.CallNow, segment, null, CallBlocker.None);
      }

      if (blocker == CallBlocker.HourlyCap && !_capEpisodeOpen)
      {
        _capEpisodeOpen = true;
        _logger.LogWarning(
          "SongRec call cap reached: {Calls} calls in the last hour (MaxCallsPerHour {Max}); identification pauses until {FreeAt:u}",
          _callTimes.Count, settings.MaxCallsPerHour, capFreeAt.UtcDateTime);
      }

      return new CallDecision(CallDecisionKind.Wait, segment, due, blocker);
    }
  }

  /// <summary>Records that a SongRec process is being started at <paramref name="now"/>; counts toward the hourly cap.</summary>
  public void RecordCallStarted(DateTimeOffset now)
  {
    var settings = Settings.From(_options());

    lock (_lock)
    {
      PruneCallTimes(now);
      var callsBefore = _callTimes.Count;
      _callTimes.Enqueue(now);

      // The episode ends when demand has dropped below the cap, not merely when one slot ages out and is
      // used at once: under sustained demand that happens on every call, and closing (and so re-opening,
      // and re-warning) each time would log one Warning per call.
      if (_capEpisodeOpen && callsBefore <= Math.Max(0, settings.MaxCallsPerHour - 2))
      {
        _capEpisodeOpen = false;
        _logger.LogInformation("SongRec call cap no longer binding ({Calls} calls in the last hour)", callsBefore + 1);
      }
    }
  }

  /// <summary>
  /// Records how an attempt the policy allowed ended, and schedules the next one.
  /// </summary>
  /// <param name="segment">The <see cref="CallDecision.Segment"/> the attempt was decided for. When the
  /// schedule has moved on since (a new track or source), only the global failure accounting is updated.</param>
  /// <param name="captureStartedAt">When the attempt's capture began — the anchor for start-to-start intervals.</param>
  /// <param name="outcome">How it ended.</param>
  /// <param name="now">The current instant; the failure back-off is measured from here.</param>
  /// <param name="error">For <see cref="CallOutcome.Error"/>: what failed, for the Warning.</param>
  public void RecordOutcome(
    CallSegment segment, DateTimeOffset captureStartedAt, CallOutcome outcome, DateTimeOffset now, string? error = null)
  {
    var settings = Settings.From(_options());

    lock (_lock)
    {
      if (outcome == CallOutcome.Error)
      {
        _consecutiveErrors++;
        var backoff = BackoffFor(_consecutiveErrors, settings);
        _backoffUntil = now + backoff;
        _logger.LogDebug(
          "SongRec failure {Count} in a row ({Error}); backing off {Backoff}s",
          _consecutiveErrors, error, backoff.TotalSeconds);

        if (_consecutiveErrors >= settings.ErrorWarnThreshold && !_errorWarningLogged)
        {
          _errorWarningLogged = true;
          _logger.LogWarning(
            "SongRec has failed {Count} times in a row (last: {Error}); backing off up to {MaxBackoff}s between attempts. Possible Shazam throttling or ban",
            _consecutiveErrors, error, settings.ErrorBackoffMax.TotalSeconds);
        }
      }
      else if (outcome is CallOutcome.Match or CallOutcome.NoMatch)
      {
        if (_consecutiveErrors > 0 && _errorWarningLogged)
        {
          _logger.LogInformation("SongRec recovered after {Count} consecutive failures", _consecutiveErrors);
        }
        _consecutiveErrors = 0;
        _errorWarningLogged = false;
        _backoffUntil = DateTimeOffset.MinValue;
      }

      if (segment != _segment)
      {
        return;
      }

      if (!segment.KnownStart)
      {
        _nextDue = captureStartedAt + settings.UnknownStartInterval;
        return;
      }

      switch (outcome)
      {
        case CallOutcome.Match:
          _matchedInSegment = true;
          break;
        case CallOutcome.NoMatch:
          _noMatchesInSegment++;
          break;
      }

      _nextDue = captureStartedAt + KnownStartIntervalAfterAttempt(settings);
    }
  }

  // Interval to the next known-start attempt, given the segment's counters after the attempt just recorded.
  // A failure or an uncalled attempt does not count as a no-match; it repeats the interval that was in force.
  private TimeSpan KnownStartIntervalAfterAttempt(Settings settings)
  {
    if (_matchedInSegment)
    {
      return settings.KnownStartValidationInterval;
    }

    return _noMatchesInSegment <= 1 ? settings.KnownStartFirstRetryDelay : settings.KnownStartRetryInterval;
  }

  private void StartSegment(CallSegment segment, DateTimeOffset now, Settings settings)
  {
    _segment = segment;
    _noMatchesInSegment = 0;
    _matchedInSegment = false;

    if (!segment.KnownStart)
    {
      _nextDue = now;
      return;
    }

    // Anchor to the track's own start, so a loop that noticed the track late does not add its lateness to
    // the delay. A track that started long ago (the service has just started) is simply due now.
    var anchor = segment.TrackStartedUtc is { } started
      ? new DateTimeOffset(DateTime.SpecifyKind(started, DateTimeKind.Utc))
      : now;
    _nextDue = anchor + settings.KnownStartFirstCallDelay;
  }

  // The earliest instant a call fits under the cap: now when under it, otherwise when enough of the oldest
  // calls have aged out of the window (more than one if MaxCallsPerHour was lowered at runtime).
  private DateTimeOffset CapFreeAt(DateTimeOffset now, int maxCallsPerHour)
  {
    PruneCallTimes(now);
    var excess = _callTimes.Count - maxCallsPerHour;
    if (excess < 0)
    {
      return now;
    }

    return _callTimes.ElementAt(excess) + CapWindow;
  }

  private void PruneCallTimes(DateTimeOffset now)
  {
    var cutoff = now - CapWindow;
    while (_callTimes.Count > 0 && _callTimes.Peek() <= cutoff)
    {
      _callTimes.Dequeue();
    }
  }

  private static TimeSpan BackoffFor(int consecutiveErrors, Settings settings)
  {
    // initial × 2^(n-1), computed in seconds as a double so a long outage cannot overflow, then capped.
    var seconds = settings.ErrorBackoffInitial.TotalSeconds * Math.Pow(2, Math.Min(consecutiveErrors - 1, 30));
    return TimeSpan.FromSeconds(Math.Min(seconds, settings.ErrorBackoffMax.TotalSeconds));
  }

  /// <summary>The option values, clamped to their documented minimums.</summary>
  private readonly record struct Settings(
    TimeSpan KnownStartFirstCallDelay,
    TimeSpan KnownStartFirstRetryDelay,
    TimeSpan KnownStartRetryInterval,
    TimeSpan KnownStartValidationInterval,
    TimeSpan UnknownStartInterval,
    int MaxCallsPerHour,
    TimeSpan ErrorBackoffInitial,
    TimeSpan ErrorBackoffMax,
    int ErrorWarnThreshold)
  {
    public static Settings From(FingerprintingOptions o)
    {
      var initial = Math.Max(1, o.ErrorBackoffInitialSeconds);
      return new Settings(
        TimeSpan.FromSeconds(Math.Max(0, o.KnownStartFirstCallDelaySeconds)),
        TimeSpan.FromSeconds(Math.Max(1, o.KnownStartFirstRetryDelaySeconds)),
        TimeSpan.FromSeconds(Math.Max(1, o.KnownStartRetryIntervalSeconds)),
        TimeSpan.FromSeconds(Math.Max(1, o.KnownStartValidationIntervalSeconds)),
        TimeSpan.FromSeconds(Math.Max(1, o.UnknownStartIntervalSeconds)),
        Math.Max(1, o.MaxCallsPerHour),
        TimeSpan.FromSeconds(initial),
        TimeSpan.FromSeconds(Math.Max(initial, o.ErrorBackoffMaxSeconds)),
        Math.Max(1, o.ErrorWarnThreshold));
    }
  }
}
