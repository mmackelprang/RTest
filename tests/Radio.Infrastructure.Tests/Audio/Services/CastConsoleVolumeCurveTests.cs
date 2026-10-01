using Radio.Infrastructure.Audio.Services;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-81: the console-to-speaker curve. Pure function, no timing.
/// </summary>
public class CastConsoleVolumeCurveTests
{
  public static TheoryData<float> Levels => new() { 0f, 0.05f, 0.2f, 0.37f, 0.5f, 0.8f, 0.99f, 1f };

  [Theory]
  [MemberData(nameof(Levels))]
  public void WhenTheAnchorIsOnTheDiagonal_TheCurveIsTheIdentity(float console)
  {
    Assert.Equal(console, CastConsoleVolumeCurve.Map(console, 0.4f, 0.4f), 4);
    Assert.Equal(console, CastConsoleVolumeCurve.Map(console, 0.75f, 0.75f), 4);
  }

  [Theory]
  [InlineData(0.5f, 0.3f)]
  [InlineData(0.3f, 0.6f)]
  [InlineData(0.75f, 0.12f)]
  public void TheCurvePassesThroughTheAnchor_AndIsContinuousThere(float m0, float s0)
  {
    Assert.Equal(s0, CastConsoleVolumeCurve.Map(m0, m0, s0), 4);

    // One slider step either side moves the speaker by one step times the segment's slope —
    // proportional to the step, so there is no jump at the anchor.
    var below = CastConsoleVolumeCurve.Map(m0 - 0.01f, m0, s0);
    var above = CastConsoleVolumeCurve.Map(m0 + 0.01f, m0, s0);
    Assert.InRange(s0 - below, 0f, 0.01f * s0 / m0 + 1e-4f);
    var upperSlope = Math.Min((1f - s0) / (1f - m0), CastConsoleVolumeCurve.MaxUpperSlope);
    Assert.InRange(above - s0, 0f, 0.01f * upperSlope + 1e-4f);
  }

  [Theory]
  [InlineData(0.5f, 0.3f)]
  [InlineData(0.3f, 0.6f)]
  [InlineData(0.995f, 0.4f)]
  [InlineData(0.98f, 0.2f)]
  [InlineData(1f, 0.4f)]
  [InlineData(0.005f, 0.4f)]
  public void ZeroIsSilent_TheCurveIsMonotonic_AndFullIsReachedOnlyWhereTheSlopeCapDoesNotBind(float m0, float s0)
  {
    Assert.Equal(0f, CastConsoleVolumeCurve.Map(0f, m0, s0), 4);

    // Console 100 %: full volume when the uncapped upper slope is within the cap (or the anchor
    // is degenerate and the identity applies); otherwise s0 + 3 * (1 - m0) — the honest cost of
    // the cap. A change on the speaker itself re-anchors.
    var expectedAtFull = m0 <= CastConsoleVolumeCurve.LowAnchorLimit
      || (m0 < 1f && (1f - s0) / (1f - m0) <= CastConsoleVolumeCurve.MaxUpperSlope)
      ? 1f
      : s0 + CastConsoleVolumeCurve.MaxUpperSlope * (1f - m0);
    Assert.Equal(expectedAtFull, CastConsoleVolumeCurve.Map(1f, m0, s0), 4);

    var previous = -1f;
    for (var i = 0; i <= 100; i++)
    {
      var s = CastConsoleVolumeCurve.Map(i / 100f, m0, s0);
      Assert.InRange(s, 0f, 1f);
      Assert.True(s >= previous, $"not monotonic at {i}%");
      previous = s;
    }
  }

  [Fact]
  public void ANearFullConsoleAnchor_CapsTheUpperSlope_SoOneDetentIsNotAJump()
  {
    // Uncapped, (1 - 0.2) / (1 - 0.98) = 40: one detent would be 40 points.
    Assert.Equal(0.23f, CastConsoleVolumeCurve.Map(0.99f, 0.98f, 0.2f), 4);
    Assert.Equal(0.26f, CastConsoleVolumeCurve.Map(1f, 0.98f, 0.2f), 4);

    // The lower segment is unchanged by the cap.
    Assert.Equal(0.1f, CastConsoleVolumeCurve.Map(0.49f, 0.98f, 0.2f), 4);
  }

  [Fact]
  public void AFullConsoleAnchor_IsHandled_WithoutADivisionByZero()
  {
    Assert.Equal(0.4f, CastConsoleVolumeCurve.Map(1f, 1f, 0.4f), 4);
    Assert.Equal(0.2f, CastConsoleVolumeCurve.Map(0.5f, 1f, 0.4f), 4);
    Assert.Equal(1f, CastConsoleVolumeCurve.Map(1f, 1f, 1f), 4);
  }

  [Theory]
  [InlineData(0.5f, 0.3f)]
  [InlineData(0.75f, 0.12f)]
  [InlineData(0.9f, 0.05f)]
  [InlineData(0.98f, 0.2f)]
  [InlineData(0.995f, 0.4f)]
  public void AboveTheAnchor_NoConsoleStepRaisesTheSpeakerByMoreThanThreeTimesItsSize(float m0, float s0)
  {
    var start = (int)MathF.Ceiling(m0 * 1000f);
    var previous = CastConsoleVolumeCurve.Map(start / 1000f, m0, s0);
    for (var i = start + 1; i <= 1000; i++)
    {
      var s = CastConsoleVolumeCurve.Map(i / 1000f, m0, s0);
      Assert.True(s - previous <= CastConsoleVolumeCurve.MaxUpperSlope * 0.001f + 1e-5f,
        $"step at {i / 10f}% raised the speaker by {s - previous:F4}");
      previous = s;
    }
  }

  [Fact]
  public void ANearSilentConsoleAnchor_UsesTheIdentity()
  {
    Assert.Equal(0.6f, CastConsoleVolumeCurve.Map(0.6f, 0.01f, 0.8f), 4);
    Assert.Equal(0.6f, CastConsoleVolumeCurve.Map(0.6f, 0f, 0.8f), 4);
  }

  [Fact]
  public void AnUnknownSpeakerLevel_UsesTheIdentity()
  {
    Assert.Equal(0.42f, CastConsoleVolumeCurve.Map(0.42f, 0.5f, float.NaN), 4);
  }

  [Fact]
  public void TheTwoSegments_AreTheSpecifiedLines()
  {
    // m0 = 0.5, s0 = 0.3: below, s = 0.3 * m / 0.5; above, s = 0.3 + 0.7 * (m - 0.5) / 0.5.
    Assert.Equal(0.15f, CastConsoleVolumeCurve.Map(0.25f, 0.5f, 0.3f), 4);
    Assert.Equal(0.65f, CastConsoleVolumeCurve.Map(0.75f, 0.5f, 0.3f), 4);
  }
}
