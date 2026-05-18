using System.Text.RegularExpressions;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Tracks an RDS Program-Service (PS) signal and emits a "stable" value
/// by scoring candidates in a sliding time-window. Used by the SDR radio
/// source to filter rolling-PS artifacts before exposing
/// <c>IRadioControl.RdsStationNameStable</c> on the wire.
/// <para>
/// <b>Algorithm (Task #80 v2)</b>: keep all PS samples observed in the
/// last <see cref="WindowDuration"/>, group by value, score each unique
/// value as <c>occurrence_count + (matches_call_sign_shape ? CallSignBoost : 0)</c>,
/// and expose the highest-scoring value as <see cref="Stable"/>.
/// </para>
/// <para>
/// <b>Why not "N consecutive identical samples"</b>: rolling-PS broadcasters
/// hold each fragment of the rotation for 30-50 consecutive frames at the
/// ~10 Hz decode rate. Any small N is trivially satisfied by every
/// fragment — including song titles ("TOO HOT"), program titles ("On
/// Point"), and phone-number digits ("336") — and whichever fragment
/// happens to be in the buffer when the consumer reads gets promoted.
/// The frequency-window + shape boost picks the call-sign-shaped
/// fragment in a rotation, which is the value listeners actually want
/// (the station identifier, not the show name or song title).
/// </para>
/// <para>
/// <b>Live behavior</b> on previously-failing rotations:
/// <list type="bullet">
///   <item><b>WUNC 91.5</b>: <c>"ON WUNC"</c> wins over <c>"On Point"</c>
///     because <c>WUNC</c> matches the call-sign-shape pattern.</item>
///   <item><b>WSMW 97.745</b>: <c>"WSMW"</c> wins over <c>"TOO HOT"</c>,
///     <c>"KOOL"</c>, <c>"THE GANG"</c>, <c>"SIMON"</c>, <c>"336"</c> —
///     for the same reason.</item>
///   <item><b>KEXP-FM</b> (static-PS): wins trivially as the single
///     histogram entry.</item>
///   <item><b>Noise-only / international</b>: falls back to
///     most-frequent value; not perfect but no worse than prior
///     behavior.</item>
/// </list>
/// </para>
/// <para>
/// <b>Future enhancement</b> (not in this revision): cross-reference
/// with RBDS Annex D PI-code &#8594; call-sign decode table for
/// deterministic disambiguation when no rotation entry matches the
/// shape heuristic.
/// </para>
/// </summary>
public sealed class RdsStationNameStabilityTracker
{
  /// <summary>
  /// Sliding window duration. Samples observed before
  /// <c>now - WindowDuration</c> are evicted on each
  /// <see cref="Observe"/> / <see cref="Stable"/> access.
  /// </summary>
  public TimeSpan WindowDuration { get; }

  /// <summary>
  /// Extra score added to a PS value's occurrence count when it contains
  /// a North-American call-sign-shape token (3-4 contiguous uppercase
  /// letters starting with K or W). Must be large enough to flip a tie
  /// between equal-occurrence rotation fragments — 10 covers the typical
  /// case where 4-5 fragments each appear ~30-50 times in a 60s window.
  /// </summary>
  public int CallSignBoost { get; }

  /// <summary>
  /// Minimum number of samples required in the window before any
  /// <see cref="Stable"/> value is exposed. Prevents flapping during
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
  private readonly List<(DateTimeOffset Time, string Ps)> _samples = new();

  /// <summary>
  /// Creates a tracker with the given tunables. All have defaults
  /// chosen for North-American FM rolling-PS broadcasters at the
  /// ~10 Hz RDS PS frame rate.
  /// </summary>
  /// <param name="windowDuration">
  /// Sliding window. Default 60s. Longer windows give more stable
  /// consensus at the cost of slower adaptation to a real station
  /// change (mitigated by calling <see cref="Reset"/> on frequency
  /// tune). Must be positive.
  /// </param>
  /// <param name="callSignBoost">
  /// Score bonus when the PS value contains a call-sign-shape token.
  /// Default 10. Must be non-negative.
  /// </param>
  /// <param name="minSamples">
  /// Minimum samples in the window before <see cref="Stable"/>
  /// exposes a value. Default 5. Must be ≥ 1.
  /// </param>
  /// <param name="timeProvider">
  /// Time source. Tests pass a <c>FakeTimeProvider</c> to drive
  /// window-eviction deterministically. Defaults to
  /// <see cref="TimeProvider.System"/>.
  /// </param>
  public RdsStationNameStabilityTracker(
    TimeSpan? windowDuration = null,
    int callSignBoost = 10,
    int minSamples = 5,
    TimeProvider? timeProvider = null)
  {
    var window = windowDuration ?? TimeSpan.FromSeconds(60);
    if (window <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(windowDuration), "WindowDuration must be > 0");
    }
    if (callSignBoost < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(callSignBoost), "CallSignBoost must be ≥ 0");
    }
    if (minSamples < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(minSamples), "MinSamples must be ≥ 1");
    }

    WindowDuration = window;
    CallSignBoost = callSignBoost;
    MinSamples = minSamples;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <summary>
  /// Pushes a new PS sample into the tracker. Null / empty / whitespace
  /// samples are ignored (an RDS dropout shouldn't poison the histogram).
  /// Non-null samples are trimmed before scoring. Returns the current
  /// stable value after this sample is incorporated (null when below
  /// <see cref="MinSamples"/>).
  /// </summary>
  public string? Observe(string? sample)
  {
    if (string.IsNullOrWhiteSpace(sample))
    {
      // Return the current stable value without mutating state.
      return ComputeStable();
    }

    var trimmed = sample.Trim();

    lock (_lock)
    {
      var now = _timeProvider.GetUtcNow();
      _samples.Add((now, trimmed));
      EvictExpired_NoLock(now);
      return ComputeStable_NoLock();
    }
  }

  /// <summary>
  /// Current stable PS value, or <c>null</c> when the window holds
  /// fewer than <see cref="MinSamples"/> samples. Window eviction runs
  /// on read so a long gap of silence eventually drains the histogram.
  /// </summary>
  public string? Stable => ComputeStable();

  /// <summary>
  /// Clears the tracker. Use after a frequency/band change so the
  /// previous station's histogram doesn't leak across tunes.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      _samples.Clear();
    }
  }

  private string? ComputeStable()
  {
    lock (_lock)
    {
      EvictExpired_NoLock(_timeProvider.GetUtcNow());
      return ComputeStable_NoLock();
    }
  }

  private string? ComputeStable_NoLock()
  {
    if (_samples.Count < MinSamples)
    {
      return null;
    }

    // Group by ordinal PS value, score, return highest-scoring.
    // Tie-break is undefined but stable enough in practice — when two
    // values tie on score, GroupBy preserves first-occurrence order.
    string? bestPs = null;
    var bestScore = int.MinValue;

    // Manual aggregation avoids a LINQ allocation chain on the hot path.
    // The histogram is bounded by the number of unique PS values in a
    // 60s window — typically 1 (static) to ~10 (rolling) — so a flat
    // dictionary scan stays trivially small.
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var (_, ps) in _samples)
    {
      counts.TryGetValue(ps, out var current);
      counts[ps] = current + 1;
    }

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

  private void EvictExpired_NoLock(DateTimeOffset now)
  {
    var cutoff = now - WindowDuration;
    // Samples are appended in non-decreasing time order, so the
    // expired prefix is contiguous. Walk from the front until we hit a
    // surviving sample, then drop the prefix in one RemoveRange.
    var expired = 0;
    while (expired < _samples.Count && _samples[expired].Time < cutoff)
    {
      expired++;
    }
    if (expired > 0)
    {
      _samples.RemoveRange(0, expired);
    }
  }
}
