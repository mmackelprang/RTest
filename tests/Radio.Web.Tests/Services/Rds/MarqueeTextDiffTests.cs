using FluentAssertions;
using Radio.Web.Services.Rds;

namespace Radio.Web.Tests.Services.Rds;

/// <summary>
/// Unit tests for <see cref="MarqueeTextDiff"/> — the transition classifier
/// that lets <c>RdsScrollMarquee</c> preserve its scroll offset across
/// buffer updates (the "jerk" fix). The trim math here is the load-bearing
/// piece of "trim only what is off-screen, adjust the transform by the
/// trimmed width, visible position unchanged".
/// </summary>
public class MarqueeTextDiffTests
{
  // ─── Unchanged / empty transitions ───────────────────────────────────────

  [Fact]
  public void IdenticalTexts_AreUnchanged()
  {
    MarqueeTextDiff.Compute("WUNC News", "WUNC News")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Unchanged, 0));
  }

  [Fact]
  public void BothEmpty_IsUnchanged()
  {
    MarqueeTextDiff.Compute(null, string.Empty)
      .Should().Be(new MarqueeDiff(MarqueeTransition.Unchanged, 0));
  }

  [Fact]
  public void EmptyToText_IsReset()
  {
    MarqueeTextDiff.Compute(string.Empty, "First chunk")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Reset, 0));
  }

  [Fact]
  public void TextToEmpty_IsReset()
  {
    MarqueeTextDiff.Compute("Last chunk", null)
      .Should().Be(new MarqueeDiff(MarqueeTransition.Reset, 0));
  }

  // ─── Continuation (append / front-trim) ──────────────────────────────────

  [Fact]
  public void PureAppend_IsContinuation_WithZeroTrim()
  {
    MarqueeTextDiff.Compute("WUNC News", "WUNC News • Morning Edition")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Continuation, 0));
  }

  [Fact]
  public void FrontTrimPlusAppend_ReportsExactTrimmedCount()
  {
    // Whole-chunk eviction dropped "Old • " (6 chars) while a new chunk
    // landed at the tail — the offset compensation needs exactly 6.
    MarqueeTextDiff.Compute("Old • Morning Edition", "Morning Edition • NPR News")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Continuation, 6));
  }

  [Fact]
  public void PureFrontTrim_NoAppend_IsContinuation()
  {
    MarqueeTextDiff.Compute("Old • Morning Edition", "Morning Edition")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Continuation, 6));
  }

  [Fact]
  public void PrefixExtensionReplacement_IsContinuation()
  {
    // Buffer replaced its last chunk in place ("Simo" → full message): the
    // old text is a strict prefix of the new one.
    MarqueeTextDiff.Compute(
        "Earlier • Simo",
        "Earlier • Simon - Madonna :: Material Girl")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Continuation, 0));
  }

  [Fact]
  public void AmbiguousAlignment_PicksSmallestTrim()
  {
    // "ABAB" → "ABABAB" aligns at k=0 (append "AB") and k=2; the smallest
    // trim is the true one (pure append) and also the safest visually.
    MarqueeTextDiff.Compute("ABAB", "ABABAB")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Continuation, 0));
  }

  [Fact]
  public void CoincidentalTinyOverlap_DoesNotCountAsContinuation()
  {
    // Old tail "s" == new head "s" — a 1-char overlap must not classify a
    // full text replacement as a near-total front-trim.
    MarqueeTextDiff.Compute("Traffic and weather updates", "sunny skies ahead all weekend")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Reset, 0));
  }

  // ─── In-place swap (rolling PS / corrections) ────────────────────────────

  [Fact]
  public void SameLengthDifferentHead_IsInPlaceSwap()
  {
    // Rolling-PS page swap: PS is always exactly 8 chars, so the track
    // length is preserved — keep the offset, swap the glyphs.
    MarqueeTextDiff.Compute(
        "EAGLES97 • Hotel California",
        "CLASSICS • Hotel California")
      .Should().Be(new MarqueeDiff(MarqueeTransition.InPlaceSwap, 0));
  }

  [Fact]
  public void SameLengthMidStringCorrection_IsInPlaceSwap()
  {
    MarqueeTextDiff.Compute(
        "RDS PS • GivJ It Away",
        "RDS PS • Give It Away")
      .Should().Be(new MarqueeDiff(MarqueeTransition.InPlaceSwap, 0));
  }

  // ─── Reset (unrelated text) ──────────────────────────────────────────────

  [Fact]
  public void UnrelatedTextDifferentLength_IsReset()
  {
    MarqueeTextDiff.Compute("WUNC News • Morning Edition", "Completely new station")
      .Should().Be(new MarqueeDiff(MarqueeTransition.Reset, 0));
  }
}
