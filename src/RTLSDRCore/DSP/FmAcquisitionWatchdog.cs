namespace RTLSDRCore.DSP;

/// <summary>Why an FM acquisition window was opened.</summary>
internal enum FmAcquisitionKind
{
  /// <summary>A new WFM chain: band change into FM, receiver start-up, or a watchdog recovery step.</summary>
  Start,

  /// <summary>A retune inside FM (a new tune generation on the same chain).</summary>
  Retune,
}

/// <summary>What the receiver should do after a block, as decided by <see cref="FmAcquisitionWatchdog.Observe"/>.</summary>
internal enum FmWatchdogAction
{
  /// <summary>Nothing.</summary>
  None,

  /// <summary>An FM start reached its outcome; log it (Information). No recovery.</summary>
  ReportOutcome,

  /// <summary>Recovery step 1: re-run the band setup without closing the device.</summary>
  RerunBandSetup,

  /// <summary>Recovery step 2: close and reopen the device.</summary>
  ReopenDevice,

  /// <summary>Both steps were tried, or the rate limit is reached; log once and do nothing more.</summary>
  GiveUp,
}

/// <summary>The outcome of one closed acquisition window.</summary>
/// <param name="Kind">Why the window was opened.</param>
/// <param name="Reason">The receiver's reason for a <see cref="FmAcquisitionKind.Start"/> (band change, start-up, watchdog step).</param>
/// <param name="FrequencyHz">The frequency the window watched.</param>
/// <param name="PilotAfterSamples">IQ samples until the 19 kHz pilot was first detected; null when it never was.</param>
/// <param name="RdsAfterSamples">IQ samples until RDS block sync was first reported; null when it never was.</param>
/// <param name="WindowSamples">The IQ samples the window covered when it closed.</param>
/// <param name="MeanSignal">The mean wideband signal strength over the window.</param>
/// <param name="KnownLocked">Whether this frequency had a pilot or RDS earlier in this process, before this window.</param>
/// <param name="Action">What the receiver should do.</param>
/// <param name="RecoveryStep">The episode's recovery step after this decision (0 when none has been taken).</param>
internal sealed record FmAcquisitionOutcome(
  FmAcquisitionKind Kind,
  string Reason,
  long FrequencyHz,
  long? PilotAfterSamples,
  long? RdsAfterSamples,
  long WindowSamples,
  float MeanSignal,
  bool KnownLocked,
  FmWatchdogAction Action,
  int RecoveryStep)
{
  /// <summary>Neither a pilot nor RDS sync was seen in the window.</summary>
  public bool Missed => PilotAfterSamples is null && RdsAfterSamples is null;
}

/// <summary>
/// Watches every FM acquisition for a 19 kHz pilot and RDS block sync, and decides when the receiver
/// should try to recover one that streams IQ but decodes neither (AUD-106).
/// </summary>
/// <remarks>
/// <para>
/// <b>Driven by the DSP thread, counted in samples, not timed.</b> <see cref="Observe"/> is called once
/// per demodulated WFM block with the block's IQ sample count. A window closes after
/// <see cref="WindowSamples"/> IQ samples (3 s at 240 kS/s), or as soon as both the pilot and RDS
/// have been seen. Blocks the receiver does not demodulate (squelched, muted, a band sweep) are never
/// observed, so they neither advance a window nor count as a miss. A retune (a new tune generation)
/// closes the open window without a verdict and opens a new one, so a seek passing through a station
/// never completes a window there. Once a window has closed, nothing more is judged on that tune:
/// steady listening is not watched, so a station that fades or drops its pilot mid-programme cannot
/// trigger a recovery that would interrupt it. The observed fault was an acquisition fault (the
/// pilot never appeared after a band change), which is what this watches.
/// </para>
/// <para>
/// <b>The trigger key.</b> Recovery is asked for only when all three hold at the end of a window:
/// (1) neither the pilot nor RDS was seen; (2) the mean wideband signal was at least
/// <see cref="MinSignal"/>; and (3) this frequency was <i>locked</i> earlier in this process: RDS block
/// sync, or a pilot the receiver judged locked (in phase with the PLL, not just energy; see
/// <c>RadioReceiver.IsPilotLocked</c>). "Pilot seen" alone is not enough to record a frequency as
/// locked, because the stereo detector is an energy detector that reads noise as a pilot (measured on
/// the box, `AUD-109`). Condition (3) is what keeps a genuinely mono station without RDS from ever
/// triggering a recovery: it is never recorded as locked, so a miss on it is only reported. The cost
/// is that a fault on the first FM acquisition of a frequency after a restart is reported, not
/// recovered. The observed fault (2026-10-12, 97.745 MHz after AM) was on a station with RDS that had
/// been received earlier in the same process.
/// </para>
/// <para>
/// Under the receiver's defaults condition (2) adds little: blocks below the squelch threshold (0.1,
/// the same value) are never demodulated, so never observed. It matters when the squelch is set
/// lower than <see cref="MinSignal"/>.
/// </para>
/// <para>
/// <b>Escalation, rate-limited, never looping.</b> A triggering miss opens an <i>episode</i>. Its
/// first trigger asks for <see cref="FmWatchdogAction.RerunBandSetup"/>, the next (on the start the
/// recovery itself produces) for <see cref="FmWatchdogAction.ReopenDevice"/>, and the next for
/// <see cref="FmWatchdogAction.GiveUp"/>, after which the episode asks for nothing more. An episode
/// ends on a healthy acquisition (pilot or RDS seen) or on an FM start the watchdog did not cause
/// (see <see cref="BeginStart"/>); retunes inside FM do not end it. At most <see cref="MaxEpisodesPerHour"/>
/// episodes may start in any hour by <see cref="Clock"/>; past that a trigger is answered with
/// <see cref="FmWatchdogAction.GiveUp"/> once and then nothing.
/// </para>
/// <para>
/// Not thread-safe: <see cref="Observe"/> and <see cref="BeginStart"/> run on the DSP thread only.
/// </para>
/// </remarks>
internal sealed class FmAcquisitionWatchdog
{
  private readonly HashSet<long> _knownLocked = new();
  private readonly Queue<DateTimeOffset> _episodeStarts = new();

  private bool _windowOpen;
  private FmAcquisitionKind _kind;
  private string _reason = string.Empty;
  private long _frequencyHz;
  private int _generation = int.MinValue;
  private long _samples;
  private long? _pilotAt;
  private long? _rdsAt;
  private double _signalSum;
  private long _signalBlocks;
  private bool _knownBefore;
  private bool _lockEvidence;
  private int _lockedRun;

  private int _episodeStep;
  private bool _episodeGaveUp;

  /// <summary>IQ samples a window covers before it is judged: 3 s at 240 kS/s by default.</summary>
  public long WindowSamples { get; set; } = 720_000;

  /// <summary>
  /// The mean wideband signal strength (the receiver's <c>SignalStrength</c> scale) a miss needs to
  /// trigger recovery. 0.1, the receiver's default squelch threshold (see the remarks).
  /// </summary>
  public float MinSignal { get; set; } = 0.1f;

  /// <summary>
  /// Consecutive blocks with a locked pilot that record a frequency as locked when RDS is not seen:
  /// 8 (about 0.55 s at 240 kS/s), so that one block's chance agreement on noise cannot.
  /// </summary>
  public int LockedBlocksRequired { get; set; } = 8;

  /// <summary>The most recovery episodes that may start in any hour.</summary>
  public int MaxEpisodesPerHour { get; set; } = 3;

  /// <summary>The clock the episode rate limit reads.</summary>
  public TimeProvider Clock { get; set; } = TimeProvider.System;

  /// <summary>The current episode's recovery step: 0 for none, 1 after a re-run, 2 after a reopen, 3 after giving up.</summary>
  public int EpisodeStep => _episodeStep;

  /// <summary>Whether <paramref name="frequencyHz"/> has been locked (RDS, or a locked pilot) in this process.</summary>
  public bool IsKnownLocked(long frequencyHz) => _knownLocked.Contains(frequencyHz);

  /// <summary>
  /// Opens a <see cref="FmAcquisitionKind.Start"/> window: called when the receiver builds a new WFM
  /// chain. Any open window is closed without a verdict.
  /// </summary>
  /// <param name="frequencyHz">The receiver's frequency.</param>
  /// <param name="generation">The tune generation of the first block on the new chain.</param>
  /// <param name="reason">Why the chain was built, for the log.</param>
  /// <param name="byRecovery">
  /// True when the chain was built by a recovery step this watchdog asked for. Any other start (a
  /// band change into FM, a receiver start) ends the current episode, so a person re-selecting FM
  /// after the watchdog gave up gets one more episode, still subject to the hourly limit.
  /// </param>
  public void BeginStart(long frequencyHz, int generation, string reason, bool byRecovery)
  {
    if (!byRecovery)
    {
      _episodeStep = 0;
      _episodeGaveUp = false;
    }

    Open(FmAcquisitionKind.Start, reason, frequencyHz, generation);
  }

  /// <summary>
  /// Records one demodulated WFM block and returns the outcome when this block closes a window.
  /// </summary>
  /// <param name="generation">The tune generation the block was stamped with.</param>
  /// <param name="frequencyHz">The receiver's frequency.</param>
  /// <param name="iqSamples">The block's IQ sample count.</param>
  /// <param name="pilot">Whether the stereo decoder reports a pilot after this block.</param>
  /// <param name="rdsSynced">Whether the RDS decoder reports block sync after this block.</param>
  /// <param name="signal">The block's wideband signal strength.</param>
  /// <param name="pilotLocked">
  /// Whether the receiver judges the pilot locked after this block (stronger evidence than
  /// <paramref name="pilot"/>; see the remarks). <see cref="LockedBlocksRequired"/> consecutive
  /// locked blocks within the window count as lock evidence.
  /// </param>
  /// <returns>The closed window's outcome, or null when no window closed.</returns>
  public FmAcquisitionOutcome? Observe(int generation, long frequencyHz, int iqSamples, bool pilot, bool rdsSynced, float signal,
    bool pilotLocked)
  {
    if (generation != _generation || frequencyHz != _frequencyHz)
    {
      // A retune: watch the new station's acquisition. (A closed window on the same tune is not
      // reopened: only acquisitions are judged, never steady listening; see the remarks.)
      Open(FmAcquisitionKind.Retune, "retune", frequencyHz, generation);
    }
    else if (!_windowOpen)
    {
      return null;
    }

    _samples += iqSamples;
    _signalSum += signal;
    _signalBlocks++;
    if (pilot && _pilotAt is null)
    {
      _pilotAt = _samples;
    }
    if (rdsSynced && _rdsAt is null)
    {
      _rdsAt = _samples;
    }
    _lockedRun = pilotLocked ? _lockedRun + 1 : 0;
    _lockEvidence |= _rdsAt is not null || _lockedRun >= LockedBlocksRequired;

    bool both = _pilotAt is not null && _rdsAt is not null;
    if (!both && _samples < WindowSamples)
    {
      return null;
    }

    return Close();
  }

  private FmAcquisitionOutcome? Close()
  {
    _windowOpen = false;
    float meanSignal = _signalBlocks > 0 ? (float)(_signalSum / _signalBlocks) : 0f;
    bool missed = _pilotAt is null && _rdsAt is null;

    FmWatchdogAction action = FmWatchdogAction.None;
    if (_lockEvidence)
    {
      _knownLocked.Add(_frequencyHz);
    }

    if (!missed)
    {
      // A healthy acquisition ends the episode.
      _episodeStep = 0;
      _episodeGaveUp = false;
    }
    else if (_knownBefore && meanSignal >= MinSignal)
    {
      action = NextRecoveryAction();
    }

    if (action == FmWatchdogAction.None && _kind == FmAcquisitionKind.Start)
    {
      action = FmWatchdogAction.ReportOutcome;
    }

    if (action == FmWatchdogAction.None)
    {
      return null;
    }

    return new FmAcquisitionOutcome(_kind, _reason, _frequencyHz, _pilotAt, _rdsAt, _samples, meanSignal,
      _knownBefore, action, _episodeStep);
  }

  private FmWatchdogAction NextRecoveryAction()
  {
    if (_episodeGaveUp)
    {
      // Said once already; a trigger after giving up asks for nothing (reported if it was a start).
      return FmWatchdogAction.None;
    }

    if (_episodeStep == 0)
    {
      DateTimeOffset now = Clock.GetUtcNow();
      while (_episodeStarts.Count > 0 && now - _episodeStarts.Peek() >= TimeSpan.FromHours(1))
      {
        _episodeStarts.Dequeue();
      }

      if (_episodeStarts.Count >= MaxEpisodesPerHour)
      {
        _episodeGaveUp = true;
        _episodeStep = 3;
        return FmWatchdogAction.GiveUp;
      }

      _episodeStarts.Enqueue(now);
    }

    _episodeStep++;
    switch (_episodeStep)
    {
      case 1:
        return FmWatchdogAction.RerunBandSetup;
      case 2:
        return FmWatchdogAction.ReopenDevice;
      default:
        _episodeGaveUp = true;
        _episodeStep = 3;
        return FmWatchdogAction.GiveUp;
    }
  }

  private void Open(FmAcquisitionKind kind, string reason, long frequencyHz, int generation)
  {
    _windowOpen = true;
    _kind = kind;
    _reason = reason;
    _frequencyHz = frequencyHz;
    _generation = generation;
    _samples = 0;
    _pilotAt = null;
    _rdsAt = null;
    _signalSum = 0;
    _signalBlocks = 0;
    _knownBefore = _knownLocked.Contains(frequencyHz);
    _lockEvidence = false;
    _lockedRun = 0;
  }
}
