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
    Assert.InRange(above - s0, 0f, 0.01f * (1f - s0) / (1f - m0) + 1e-4f);
  }

  [Theory]
  [InlineData(0.5f, 0.3f)]
  [InlineData(0.3f, 0.6f)]
  [InlineData(0.995f, 0.4f)]
  [InlineData(0.005f, 0.4f)]
  public void ZeroIsSilent_FullIsFull_AndTheCurveIsMonotonic(float m0, float s0)
  {
    Assert.Equal(0f, CastConsoleVolumeCurve.Map(0f, m0, s0), 4);
    if (m0 < CastConsoleVolumeCurve.HighAnchorLimit)
    {
      Assert.Equal(1f, CastConsoleVolumeCurve.Map(1f, m0, s0), 4);
    }

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
  public void ANearSilentConsoleAnchor_UsesTheIdentity()
  {
    Assert.Equal(0.6f, CastConsoleVolumeCurve.Map(0.6f, 0.01f, 0.8f), 4);
    Assert.Equal(0.6f, CastConsoleVolumeCurve.Map(0.6f, 0f, 0.8f), 4);
  }

  [Fact]
  public void ANearFullConsoleAnchor_UsesOnlyTheLowerSegment()
  {
    Assert.Equal(0.25f, CastConsoleVolumeCurve.Map(0.5f, 0.995f, 0.4975f), 4);
    Assert.Equal(0.4975f, CastConsoleVolumeCurve.Map(0.995f, 0.995f, 0.4975f), 4);
    // Above the anchor it keeps the same slope (capped at 1), not a vertical step to full.
    Assert.Equal(0.5f, CastConsoleVolumeCurve.Map(1f, 0.995f, 0.4975f), 3);
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
