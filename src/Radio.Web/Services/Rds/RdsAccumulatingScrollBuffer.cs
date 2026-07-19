namespace Radio.Web.Services.Rds;

/// <summary>
/// Client-side rolling buffer for RDS RadioText (RT) chunks.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the previous "latest confirmed RT message replaces the prior in
/// place" behaviour with an accumulating ticker. Each new confirmed RT chunk
/// is appended to a rolling buffer (separator-joined) up to
/// <see cref="MaxChars"/>. Internally the buffer is chunk-aware: when it
/// would overflow, the oldest WHOLE chunks are evicted from the front rather
/// than trimming characters mid-chunk — a mid-chunk trim left a permanently
/// cut-off fragment at the head once the buffer reached its cap (every
/// steady-state append trimmed the head mid-word), which read as "data not
/// placed correctly" on the kiosk.
/// </para>
/// <para>
/// Dedup and replacement rules (all against the most recent chunk):
/// verbatim repeats are dropped; a chunk that merely EXTENDS the last one
/// (the decoder confirmed a partial prefix, then the complete message)
/// REPLACES it in place; a chunk that is a shorter prefix of the last one is
/// dropped as stale; a same-length chunk differing in only a few characters
/// (the decoder corrected a CRC-aliased corruption) also replaces in place.
/// The same chunk re-entering after others have intervened IS appended,
/// since that is part of the rolling history the user wants to see.
/// </para>
/// <para>
/// Tuning to a different station — detected as any of band, frequency (within
/// 0.001 Hz, mirroring <c>HasRadioStateChanged</c>), or non-null RDS PI code
/// changing — clears the buffer AND the dedup tracker so the first RT message
/// on the new station is never silently dropped. PI is intentionally
/// "non-null AND changed": a transient null-during-tune does not trigger a
/// reset because the station hasn't actually changed.
/// </para>
/// <para>
/// Thread-affinity: Razor component lifecycle is single-threaded per circuit;
/// the component owns one buffer instance and mutates it from the SignalR
/// handler thread (already marshalled onto the renderer via
/// <c>InvokeAsync(StateHasChanged)</c>). No internal locking.
/// </para>
/// </remarks>
public sealed class RdsAccumulatingScrollBuffer
{
  /// <summary>
  /// Tolerance for frequency-equality comparison, in hertz. Mirrors the
  /// tolerance used by <c>AudioStateUpdateService.HasRadioStateChanged</c> so
  /// floating-point round-trip jitter alone never triggers a buffer reset.
  /// </summary>
  private const double FrequencyEpsilonHz = 0.001;

  // Whole chunks in arrival order; Text is their separator-join, cached.
  private readonly List<string> _chunks = new();
  private string _cachedText = string.Empty;

  // Dedup / replacement tracker — the most recent chunk AS APPENDED (before
  // any oversize truncation), so a decoder re-fire of the same oversize chunk
  // still dedups even though the stored chunk is its truncated tail.
  private string? _lastAppendedChunk;

  // Station-identity tracker — populated by ResetOnTuneChange. Sentinel:
  // _hasSeenStation == false means we haven't seen any tune yet, so the very
  // first call seeds the tracker without clearing (no buffer to clear, and the
  // very first RT chunk after page load is fresh by definition).
  private bool _hasSeenStation;
  private string? _lastBand;
  private double _lastFrequency;
  private ushort? _lastPi;

  /// <summary>
  /// Construct a buffer with the given size cap and inter-chunk separator.
  /// </summary>
  /// <param name="maxChars">
  /// Hard cap on the buffer length. Clamped to a minimum of 8 to keep edge-
  /// case truncation logic well-defined; the System Config UI enforces a
  /// 64–2048 floor/ceiling.
  /// </param>
  /// <param name="separator">
  /// String inserted between accumulated chunks. Empty / null is normalised to
  /// a single space so the buffer never welds two chunks into one unreadable
  /// run.
  /// </param>
  public RdsAccumulatingScrollBuffer(int maxChars, string separator)
  {
    MaxChars = Math.Max(8, maxChars);
    Separator = string.IsNullOrEmpty(separator) ? " " : separator;
  }

  /// <summary>Hard cap on buffer length, in chars.</summary>
  public int MaxChars { get; }

  /// <summary>Inter-chunk separator (always non-null, always non-empty).</summary>
  public string Separator { get; }

  /// <summary>Current buffer contents (empty string when nothing has been appended).</summary>
  public string Text => _cachedText;

  /// <summary>
  /// Number of whole chunks currently held. Exposed for the leak-bound tests
  /// (chunk count must stay bounded alongside <see cref="Text"/> length).
  /// </summary>
  public int ChunkCount => _chunks.Count;

  /// <summary>
  /// Append a new RT chunk, applying the dedup / replacement rules described
  /// in the class remarks.
  /// </summary>
  /// <remarks>
  /// Trims the input first — the decoder already trims when exposing
  /// <c>RadioText</c>, but defensive trim costs nothing and protects against
  /// future decoder changes.
  /// </remarks>
  public void AppendChunk(string? newChunk)
  {
    if (string.IsNullOrWhiteSpace(newChunk))
    {
      return;
    }

    var trimmed = newChunk.Trim();

    if (_lastAppendedChunk != null && _chunks.Count > 0)
    {
      // Verbatim repeat (decoder re-fires the same string when the station
      // holds RT across many cycles) → no-op.
      if (string.Equals(trimmed, _lastAppendedChunk, StringComparison.Ordinal))
      {
        return;
      }

      // Shorter prefix of what we already appended → a stale partial from the
      // decoder (post-hardening this is rare, but cheap insurance) → no-op.
      if (_lastAppendedChunk.StartsWith(trimmed, StringComparison.Ordinal))
      {
        return;
      }

      // Extension of the last chunk (partial confirmed first, then the
      // complete message) or a same-length minor correction (CRC-aliased
      // corruption fixed on the next confirmation) → replace in place, so the
      // ticker shows one clean copy instead of "GivJ It Away • Give It Away".
      var extendsLast = trimmed.StartsWith(_lastAppendedChunk, StringComparison.Ordinal);
      var correctsLast = IsMinorCorrection(_lastAppendedChunk, trimmed);
      if (extendsLast || correctsLast)
      {
        _chunks[^1] = trimmed;
        _lastAppendedChunk = trimmed;
        EnforceCap();
        RebuildText();
        return;
      }
    }

    _chunks.Add(trimmed);
    _lastAppendedChunk = trimmed;
    EnforceCap();
    RebuildText();
  }

  /// <summary>
  /// Clear the buffer and dedup tracker when the station identity changes.
  /// </summary>
  /// <remarks>
  /// "Station identity" = the tuple (Band, Frequency, RdsPi). Any one
  /// differing from the last seen value (with PI requiring non-null-and-
  /// changed) signals a station change. See §6.b of HANDOFF-rds-accumulating-
  /// scroll for the rationale on each signal.
  /// </remarks>
  public void ResetOnTuneChange(string? band, double frequency, ushort? rdsPi)
  {
    if (!_hasSeenStation)
    {
      // First observation seeds the tracker; nothing to clear yet.
      _hasSeenStation = true;
      _lastBand = band;
      _lastFrequency = frequency;
      _lastPi = rdsPi;
      return;
    }

    var bandChanged = !string.Equals(band, _lastBand, StringComparison.OrdinalIgnoreCase);
    var freqChanged = Math.Abs(frequency - _lastFrequency) > FrequencyEpsilonHz;
    // PI is "non-null AND changed": a transient null-during-tune does not
    // trigger a reset. Once we've seen a PI, swapping to a different non-null
    // PI is the strongest "you tuned away" signal RDS offers.
    var piChanged = rdsPi.HasValue && _lastPi.HasValue && rdsPi.Value != _lastPi.Value;
    // First non-null PI after a null is also informative — a station that
    // re-acquired lock with a different PI is a station change. (Same PI
    // re-acquiring is handled by the equal-value branch falling through.)
    var piNowAcquired = rdsPi.HasValue && !_lastPi.HasValue;

    if (bandChanged || freqChanged || piChanged || piNowAcquired)
    {
      _chunks.Clear();
      _cachedText = string.Empty;
      _lastAppendedChunk = null;
    }

    _lastBand = band;
    _lastFrequency = frequency;
    // Only persist non-null PI values — a transient null does NOT invalidate
    // the last known PI, otherwise the very next non-null re-acquire would
    // falsely trip the piNowAcquired branch.
    if (rdsPi.HasValue)
    {
      _lastPi = rdsPi;
    }
  }

  /// <summary>
  /// Reset the buffer and identity tracker unconditionally. Used by component
  /// disposal / explicit "clear" affordances. Not part of the normal lifecycle.
  /// </summary>
  public void Clear()
  {
    _chunks.Clear();
    _cachedText = string.Empty;
    _lastAppendedChunk = null;
    _hasSeenStation = false;
    _lastBand = null;
    _lastFrequency = 0;
    _lastPi = null;
  }

  // ─── Internals ─────────────────────────────────────────────────────────────

  /// <summary>
  /// True when <paramref name="candidate"/> looks like a corrected re-send of
  /// <paramref name="previous"/>: same length, and only a small number of
  /// positions differ. Production examples: "GivJ It Away" → "Give It Away"
  /// (1 diff), "SimoA -FMadonna…" → "Simon - Madonna…" (3 diffs). The budget
  /// (max(2, length/8)) is far below what two genuinely different rotation
  /// messages of coincidentally equal length would produce.
  /// </summary>
  private static bool IsMinorCorrection(string previous, string candidate)
  {
    if (previous.Length != candidate.Length)
    {
      return false;
    }

    // Short strings don't carry enough signal to distinguish "corrected
    // re-send" from "different message that happens to be the same length" —
    // real RT corrections show up on full sentence-length messages.
    if (previous.Length < 16)
    {
      return false;
    }

    var budget = Math.Max(2, previous.Length / 8);
    var diffs = 0;
    for (var i = 0; i < previous.Length; i++)
    {
      if (previous[i] != candidate[i] && ++diffs > budget)
      {
        return false;
      }
    }

    // Zero diffs would mean verbatim-equal, which the caller already handled.
    return diffs > 0;
  }

  /// <summary>
  /// Evict whole chunks from the front until the separator-joined length fits
  /// <see cref="MaxChars"/>. The newest chunk is never evicted; if it alone
  /// exceeds the cap (HANDOFF §6.d), it is truncated to its LAST MaxChars
  /// characters — most-recent text wins.
  /// </summary>
  private void EnforceCap()
  {
    while (_chunks.Count > 1 && JoinedLength() > MaxChars)
    {
      _chunks.RemoveAt(0);
    }

    if (_chunks.Count == 1 && _chunks[0].Length > MaxChars)
    {
      _chunks[0] = TakeLastCharsSafe(_chunks[0], MaxChars);
    }
  }

  private int JoinedLength()
  {
    var length = _chunks.Count > 1 ? Separator.Length * (_chunks.Count - 1) : 0;
    foreach (var chunk in _chunks)
    {
      length += chunk.Length;
    }
    return length;
  }

  private void RebuildText()
  {
    _cachedText = string.Join(Separator, _chunks);
  }

  /// <summary>
  /// Take the last <paramref name="count"/> chars without splitting a
  /// surrogate pair. RDS is ASCII in practice; RDS+ extensions could surface
  /// astral-plane characters — cheap insurance.
  /// </summary>
  private static string TakeLastCharsSafe(string source, int count)
  {
    if (count >= source.Length)
    {
      return source;
    }
    var start = source.Length - count;
    // If we'd start on a low surrogate, the preceding high surrogate is
    // outside the window — drop one more char to keep the pair intact.
    if (start > 0 && char.IsLowSurrogate(source[start]))
    {
      start++;
    }
    return source.Substring(start);
  }
}
