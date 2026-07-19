namespace Radio.Web.Services.Rds;

/// <summary>
/// How the marquee track text changed between two renders — drives which
/// JS-interop call <c>RdsScrollMarquee</c> makes so the scroll position is
/// preserved wherever that is visually meaningful.
/// </summary>
public enum MarqueeTransition
{
  /// <summary>Texts are identical — no interop call needed.</summary>
  Unchanged,

  /// <summary>
  /// The new text is the old text with zero or more characters trimmed from
  /// the front (whole-chunk buffer eviction) and/or appended at the end
  /// (new RT chunk, prefix-extension replacement). The scroll offset is
  /// preserved, compensated by the trimmed width.
  /// </summary>
  Continuation,

  /// <summary>
  /// Same length, different content — an in-place substitution such as a
  /// rolling-PS page swap at the head of the track ("{PS} • {RT}") or a
  /// minor-correction chunk replacement. The scroll offset is kept as-is;
  /// the glyphs simply change under it.
  /// </summary>
  InPlaceSwap,

  /// <summary>
  /// Unrelated text (station change, options rebuild) — restart the scroll
  /// from the home position.
  /// </summary>
  Reset,
}

/// <summary>Result of <see cref="MarqueeTextDiff.Compute"/>.</summary>
/// <param name="Transition">The classified transition.</param>
/// <param name="TrimmedCharCount">
/// For <see cref="MarqueeTransition.Continuation"/>: the number of characters
/// removed from the FRONT of the old text. The JS engine multiplies by the
/// measured per-character width (the track is monospace) and shifts the
/// scroll offset left by that many pixels so the visible glyphs do not move.
/// Zero for all other transitions.
/// </param>
public readonly record struct MarqueeDiff(MarqueeTransition Transition, int TrimmedCharCount);

/// <summary>
/// Classifies how the marquee track text changed between two renders.
/// </summary>
/// <remarks>
/// The accumulating RT buffer only ever mutates as front-trim + tail-append
/// (plus in-place chunk replacement), so a changed text almost always aligns
/// as "some suffix of the old text is the head of the new text". Finding the
/// smallest front-trim that aligns gives the JS engine exactly the pixel
/// compensation it needs to keep the visible glyphs stationary across the
/// re-render — the fix for the user-visible "jerk" (the old CSS keyframe
/// animation reinterpreted its elapsed fraction against the new text width
/// and duration on every append, snapping the track many characters at
/// once).
/// </remarks>
public static class MarqueeTextDiff
{
  /// <summary>
  /// Minimum number of old-text characters that must survive at the head of
  /// the new text for a Continuation match. Guards against a coincidental
  /// 1-2 char alignment classifying a genuine text replacement as a huge
  /// front-trim.
  /// </summary>
  private const int MinRetainedChars = 4;

  public static MarqueeDiff Compute(string? oldText, string? newText)
  {
    var oldT = oldText ?? string.Empty;
    var newT = newText ?? string.Empty;

    if (string.Equals(oldT, newT, StringComparison.Ordinal))
    {
      return new MarqueeDiff(MarqueeTransition.Unchanged, 0);
    }

    if (oldT.Length == 0 || newT.Length == 0)
    {
      return new MarqueeDiff(MarqueeTransition.Reset, 0);
    }

    // Smallest k such that oldT[k..] is the head of newT. k = 0 is the pure
    // append; k > 0 means k chars were evicted from the front. Retained
    // suffix must be meaningful (>= MinRetainedChars) so a lucky one-char
    // overlap doesn't masquerade as a continuation. O(n²) worst case with
    // n <= buffer cap (~256 + PS) — runs once per RT update (seconds apart).
    var minRetained = Math.Min(MinRetainedChars, oldT.Length);
    var maxTrim = oldT.Length - minRetained;
    for (var k = 0; k <= maxTrim; k++)
    {
      var retained = oldT.Length - k;
      if (newT.Length >= retained
          && string.CompareOrdinal(newT, 0, oldT, k, retained) == 0)
      {
        return new MarqueeDiff(MarqueeTransition.Continuation, k);
      }
    }

    // No continuation alignment. Same length ⇒ in-place substitution (e.g. a
    // rolling-PS page swap: PS is always exactly 8 chars, so the track length
    // is unchanged and keeping the offset swaps the glyphs without a jump).
    if (oldT.Length == newT.Length)
    {
      return new MarqueeDiff(MarqueeTransition.InPlaceSwap, 0);
    }

    return new MarqueeDiff(MarqueeTransition.Reset, 0);
  }
}
