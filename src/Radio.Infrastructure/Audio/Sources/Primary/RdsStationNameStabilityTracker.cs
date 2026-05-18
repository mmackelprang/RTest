using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Tracks an RDS Program-Service (PS) signal and emits a "stable" value
/// using a <b>lock-and-hold</b> strategy. Used by the SDR radio source to
/// filter rolling-PS artifacts before exposing
/// <c>IRadioControl.RdsStationNameStable</c> on the wire.
/// <para>
/// <b>Algorithm (Task #80 v3)</b>: accumulate PS samples for a configurable
/// lock window (default 30 seconds) after the first observation. Once
/// both the minimum-sample count and the lock window have elapsed,
/// compute the best candidate using a histogram + call-sign-shape
/// tiebreaker, then <i>freeze</i> that value. Subsequent <see cref="Observe"/>
/// calls become no-ops; the tracker returns the locked value forever
/// until <see cref="Reset"/> is called on a frequency change.
/// </para>
/// <para>
/// <b>Why lock-and-hold</b>: live UAT of the v2 sliding-window approach
/// failed because real broadcasters' rotations often don't include the
/// FCC call sign at all (WSMW broadcasts SIMON / 98.7 / 336 — never
/// "WSMW"). The call-sign-shape boost can't help when the call sign
/// isn't in the data. But Tester directly observed during v2 UAT that
/// during initial RDS lock (T+15-50s after tune), the histogram
/// algorithm DOES stabilize correctly on both WUNC and WSMW; it just
/// drifts as lock-time samples age out of the 60-second window. The
/// fix: stop adapting once a consensus exists.
/// </para>
/// <para>
/// <b>Trade-off accepted</b>: if a station legitimately rebrands its PS
/// during the day (rare), the tracker won't pick up the new value until
/// the next frequency tune. This is the explicit cost of the
/// lock-and-hold approach and matches the user-facing intent — "save a
/// preset with the station's identity, not the current playing track."
/// </para>
/// <para>
/// <b>Pre-lock behavior</b>: while still accumulating, <see cref="Stable"/>
/// returns the current transient best-effort consensus (allows partial
/// UI updates such as preset save) once <see cref="MinSamples"/> has been
/// reached. Post-lock, the same getter returns the frozen value.
/// </para>
/// </summary>
public sealed class RdsStationNameStabilityTracker
{
  /// <summary>
  /// Minimum elapsed time (since the first sample) before the tracker
  /// locks. Default 30s, matching the T+15-50s window in which Tester
  /// observed correct consensus on both WUNC and WSMW.
  /// </summary>
  public TimeSpan LockAfter { get; }

  /// <summary>
  /// Extra score added to a PS value's occurrence count when it contains
  /// a North-American call-sign-shape token (3-4 contiguous uppercase
  /// letters starting with K or W). Used as a tiebreaker during the
  /// lock window — when both call-sign and rotation-fragment values
  /// appear before lock (exactly what Tester observed on WUNC), the
  /// call-sign-shaped value scores higher and wins.
  /// </summary>
  public int CallSignBoost { get; }

  /// <summary>
  /// Minimum number of samples required before the tracker can lock or
  /// expose a transient pre-lock consensus. Prevents flapping during
  /// initial RDS lock when only 1-2 samples have been observed.
  /// </summary>
  public int MinSamples { get; }

  // Pattern: a word boundary, then 3-4 contiguous uppercase letters
  // beginning with K or W. Matches North American FM/AM call signs
  // (KEXP, WUNC, WSMW, KQED, ...). Word-boundary anchors avoid matching
  // inside longer noise tokens (e.g. "BWKUNC" won't match WKUNC; "WUNCC"
  // won't match WUNC because the trailing C is a word character).
  private static readonly Regex CallSignPattern = new(
    @"\b[KW][A-Z]{2,3}\b",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private readonly TimeProvider _timeProvider;
  private readonly object _lock = new();
  private readonly List<string> _preLockSamples = new();
  private DateTimeOffset? _firstSampleTime;
  private string? _lockedValue;

  /// <summary>
  /// Creates a tracker with the given tunables. Defaults are tuned for
  /// North-American FM rolling-PS broadcasters at the ~10 Hz RDS PS
  /// frame rate.
  /// </summary>
  /// <param name="lockAfter">
  /// Lock window. Default 30s. Configurable so the value can be tuned
  /// later without an API break. Must be positive.
  /// </param>
  /// <param name="callSignBoost">
  /// Score bonus when the PS value contains a call-sign-shape token.
  /// Default 10. Must be non-negative.
  /// </param>
  /// <param name="minSamples">
  /// Minimum samples required before lock or pre-lock consensus exposes
  /// a value. Default 5. Must be ≥ 1.
  /// </param>
  /// <param name="timeProvider">
  /// Time source. Tests pass a <c>FakeTimeProvider</c> to drive the lock
  /// window deterministically. Defaults to
  /// <see cref="TimeProvider.System"/>.
  /// </param>
  public RdsStationNameStabilityTracker(
    TimeSpan? lockAfter = null,
    int callSignBoost = 10,
    int minSamples = 5,
    TimeProvider? timeProvider = null)
  {
    var window = lockAfter ?? TimeSpan.FromSeconds(30);
    if (window <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(lockAfter), "LockAfter must be > 0");
    }
    if (callSignBoost < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(callSignBoost), "CallSignBoost must be ≥ 0");
    }
    if (minSamples < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(minSamples), "MinSamples must be ≥ 1");
    }

    LockAfter = window;
    CallSignBoost = callSignBoost;
    MinSamples = minSamples;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <summary>
  /// Pushes a new PS sample into the tracker. Null / empty / whitespace
  /// samples are ignored. Non-null samples are trimmed before scoring.
  /// Once the tracker has locked, this method is a no-op — call
  /// <see cref="Reset"/> (e.g. on frequency change) to allow relocking.
  /// Returns the current <see cref="Stable"/> value after the sample is
  /// incorporated (transient pre-lock consensus, locked value, or null).
  /// </summary>
  public string? Observe(string? sample)
  {
    if (string.IsNullOrWhiteSpace(sample))
    {
      // Return the current stable value without mutating state.
      return ReadStable();
    }

    var trimmed = sample.Trim();

    lock (_lock)
    {
      // Already locked — the whole point of v3: ignore everything until Reset.
      if (_lockedValue != null) return _lockedValue;

      var now = _timeProvider.GetUtcNow();
      _firstSampleTime ??= now;
      _preLockSamples.Add(trimmed);

      // Lock when both conditions hold: enough samples AND lock-after elapsed.
      if (_preLockSamples.Count >= MinSamples &&
          now - _firstSampleTime.Value >= LockAfter)
      {
        _lockedValue = ComputeBestCandidate_NoLock();
        _preLockSamples.Clear();
        _preLockSamples.TrimExcess();
        return _lockedValue;
      }

      return ComputeStable_NoLock();
    }
  }

  /// <summary>
  /// Current stable PS value. Pre-lock: best-effort transient consensus
  /// once <see cref="MinSamples"/> is reached, or <c>null</c>. Post-lock:
  /// the frozen value, which never changes until <see cref="Reset"/>.
  /// </summary>
  public string? Stable => ReadStable();

  /// <summary>
  /// Clears the tracker — drops the locked value and pre-lock samples.
  /// Use after a frequency/band change so the previous station's
  /// consensus doesn't leak across tunes.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      _preLockSamples.Clear();
      _preLockSamples.TrimExcess();
      _firstSampleTime = null;
      _lockedValue = null;
    }
  }

  private string? ReadStable()
  {
    lock (_lock)
    {
      if (_lockedValue != null) return _lockedValue;
      return ComputeStable_NoLock();
    }
  }

  private string? ComputeStable_NoLock()
  {
    if (_preLockSamples.Count < MinSamples) return null;
    return ComputeBestCandidate_NoLock();
  }

  private string? ComputeBestCandidate_NoLock()
  {
    if (_preLockSamples.Count == 0) return null;

    // Histogram + call-sign-shape tiebreaker. Manual aggregation avoids
    // a LINQ allocation chain on the hot path. The histogram is bounded
    // by the number of unique PS values in the lock window — typically
    // 1 (static) to ~10 (rolling) — so a flat dictionary scan stays
    // trivially small.
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var ps in _preLockSamples)
    {
      counts.TryGetValue(ps, out var current);
      counts[ps] = current + 1;
    }

    string? bestPs = null;
    var bestScore = int.MinValue;
    foreach (var (ps, count) in counts)
    {
      var score = count + (CallSignPattern.IsMatch(ps) ? CallSignBoost : 0);
      if (score > bestScore)
      {
        bestScore = score;
        bestPs = ps;
      }
    }

    return bestPs;
  }
}
