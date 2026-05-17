using Radio.API.Mappers;
using Radio.API.Models;

namespace Radio.API.Tests.Mappers;

/// <summary>
/// Locks down the raw-confidence → <see cref="ConfidenceBucket"/> projection
/// introduced in PR 2 of the Radio Controller Polish arc. The threshold table
/// is the API surface for fingerprint match strength — drift here would push
/// raw percentages back through to the UI in disguise.
///
/// Thresholds (server-side, applied at the API boundary):
/// <list type="bullet">
///   <item><description>Strong   — score ≥ 0.90</description></item>
///   <item><description>Likely   — 0.80 ≤ score &lt; 0.90</description></item>
///   <item><description>Possible — 0.60 ≤ score &lt; 0.80</description></item>
///   <item><description>None     — no match OR score &lt; 0.60</description></item>
/// </list>
/// </summary>
public class AudioDtoMapperTests
{
  [Theory]
  [InlineData(0.95, ConfidenceBucket.Strong)]
  [InlineData(0.90, ConfidenceBucket.Strong)]
  [InlineData(0.89999, ConfidenceBucket.Likely)]
  [InlineData(0.85, ConfidenceBucket.Likely)]
  [InlineData(0.80, ConfidenceBucket.Likely)]
  [InlineData(0.79999, ConfidenceBucket.Possible)]
  [InlineData(0.70, ConfidenceBucket.Possible)]
  [InlineData(0.60, ConfidenceBucket.Possible)]
  [InlineData(0.59999, ConfidenceBucket.None)]
  [InlineData(0.50, ConfidenceBucket.None)]
  [InlineData(0.0, ConfidenceBucket.None)]
  public void ToConfidenceBucket_FoldsRawScoreIntoBand(double rawScore, ConfidenceBucket expected)
  {
    Assert.Equal(expected, AudioDtoMapper.ToConfidenceBucket(isMatch: true, rawConfidence: rawScore));
  }

  [Fact]
  public void ToConfidenceBucket_NoMatch_AlwaysReturnsNone()
  {
    // A fingerprint event that produced no match must surface as None on the
    // wire even when there's a stray legacy confidence value sitting on the
    // server-side record — the wire shape must mirror the IsMatch flag.
    Assert.Equal(ConfidenceBucket.None,
      AudioDtoMapper.ToConfidenceBucket(isMatch: false, rawConfidence: 0.95));
    Assert.Equal(ConfidenceBucket.None,
      AudioDtoMapper.ToConfidenceBucket(isMatch: false, rawConfidence: null));
  }

  [Fact]
  public void ToConfidenceBucket_NullRawConfidence_ReturnsNone()
  {
    // A match flagged true but with no raw score (a pipeline edge case) must
    // also surface as None — the UI's pip widget needs an unambiguous band.
    Assert.Equal(ConfidenceBucket.None,
      AudioDtoMapper.ToConfidenceBucket(isMatch: true, rawConfidence: null));
  }
}
