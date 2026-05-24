namespace Radio.Web.Services.Rds;

/// <summary>
/// Client-side rolling buffer for RDS RadioText (RT) chunks.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the previous "latest confirmed RT message replaces the prior in
/// place" behaviour with an accumulating ticker. Each new confirmed RT chunk
/// is appended to a rolling buffer (separator-joined) up to
/// <see cref="MaxChars"/>; when the buffer would overflow, the oldest
/// characters drop from the front on a whole-char boundary until it fits.
/// </para>
/// <para>
/// Verbatim duplicate chunks are dropped on append (a station holding the same
/// RT for many decoder cycles would otherwise grow the buffer indefinitely);
/// the same chunk re-entering after others have intervened IS appended, since
/// that is part of the rolling history the user wants to see.
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

  private string _buffer = string.Empty;
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
  public string Text => _buffer;

  /// <summary>
  /// Append a new RT chunk to the buffer if it is non-empty and not a verbatim
  /// repeat of the most-recently-appended chunk.
  /// </summary>
  /// <remarks>
  /// Trims the input first — the decoder already trims via <c>.Trim()</c> when
  /// exposing <c>RadioText</c>, but defensive trim costs nothing and protects
  /// against future decoder changes.
  /// </remarks>
  public void AppendChunk(string? newChunk)
  {
    if (string.IsNullOrWhiteSpace(newChunk))
    {
      return;
    }

    var trimmed = newChunk.Trim();

    // Dedup verbatim repeat (decoder re-fires the same string when the
    // station holds RT across many cycles).
    if (string.Equals(trimmed, _lastAppendedChunk, StringComparison.Ordinal))
    {
      return;
    }

    // Edge case: single chunk longer than MaxChars. Keep the last MaxChars
    // characters (most-recent text wins) and replace the entire buffer.
    if (trimmed.Length >= MaxChars)
    {
      _buffer = TakeLastCharsSafe(trimmed, MaxChars);
      _lastAppendedChunk = trimmed;
      return;
    }

    // Normal append path. Compute the would-be new length including the
    // separator (only inserted when the buffer already has content).
    var prefix = _buffer.Length == 0 ? string.Empty : Separator;
    var addedLength = prefix.Length + trimmed.Length;

    if (_buffer.Length + addedLength <= MaxChars)
    {
      _buffer = _buffer + prefix + trimmed;
      _lastAppendedChunk = trimmed;
      return;
    }

    // Overflow path: drop from the front until the new total fits, then strip
    // any leading separator fragment so the buffer never starts with the
    // separator mid-character.
    var overflow = (_buffer.Length + addedLength) - MaxChars;
    var dropped = DropFrontCharsSafe(_buffer, overflow);
    dropped = StripLeadingSeparator(dropped);

    // After the strip, the buffer may be short enough that no separator is
    // needed for the new chunk; mirror the empty-buffer branch.
    var newPrefix = dropped.Length == 0 ? string.Empty : Separator;
    _buffer = dropped + newPrefix + trimmed;

    // Final safety net — if a fat separator and a long chunk together still
    // exceed MaxChars (cannot in practice given the upstream MaxChars >=
    // chunk-length guard above, but cheap insurance), tail-trim.
    if (_buffer.Length > MaxChars)
    {
      _buffer = TakeLastCharsSafe(_buffer, MaxChars);
    }

    _lastAppendedChunk = trimmed;
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
      _buffer = string.Empty;
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
    _buffer = string.Empty;
    _lastAppendedChunk = null;
    _hasSeenStation = false;
    _lastBand = null;
    _lastFrequency = 0;
    _lastPi = null;
  }

  // ─── Surrogate-pair-safe helpers ───────────────────────────────────────
  //
  // RDS is ASCII in practice; RDS+ extensions and a hypothetical future
  // UTF-8-on-the-wire variant could surface astral plane characters. Cheap
  // insurance: if the boundary would split a surrogate pair, nudge it one
  // char in the safe direction. Cost is one extra Char.IsHighSurrogate /
  // IsLowSurrogate test per call.

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

  private static string DropFrontCharsSafe(string source, int dropCount)
  {
    if (dropCount <= 0)
    {
      return source;
    }
    if (dropCount >= source.Length)
    {
      return string.Empty;
    }
    var start = dropCount;
    if (char.IsLowSurrogate(source[start]))
    {
      // Dropping landed mid-pair — drop one more so the next char is the
      // start of a valid grapheme (or just a normal BMP char).
      start++;
    }
    return source.Substring(start);
  }

  private string StripLeadingSeparator(string source)
  {
    // After dropping front chars we may have left a partial-or-whole separator
    // at the head. Strip a full separator if present, otherwise nibble any
    // leading whitespace so the buffer never looks like " WUNC News" or
    // " • WUNC News".
    while (source.StartsWith(Separator, StringComparison.Ordinal))
    {
      source = source.Substring(Separator.Length);
    }
    return source.TrimStart();
  }
}
