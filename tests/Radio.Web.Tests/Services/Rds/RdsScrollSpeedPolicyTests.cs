using FluentAssertions;
using Radio.Web.Services.Rds;

namespace Radio.Web.Tests.Services.Rds;

/// <summary>
/// Unit tests for <see cref="RdsScrollSpeedPolicy"/> — the modest catch-up
/// boost that keeps the ticker's newest-chunk latency bounded when the
/// buffer backlog is large ("scroll rate must keep up with the incoming RDS
/// data rate").
/// </summary>
public class RdsScrollSpeedPolicyTests
{
  [Fact]
  public void BelowThreshold_ReturnsBaseSpeed()
  {
    // 128 / 256 = 50% fill — comfortably under the 75% threshold.
    RdsScrollSpeedPolicy.EffectiveSpeed(40, 128, 256).Should().Be(40);
  }

  [Fact]
  public void AtThreshold_ReturnsBaseSpeed()
  {
    // Exactly 75% is NOT above the threshold — boost engages beyond it.
    RdsScrollSpeedPolicy.EffectiveSpeed(40, 192, 256).Should().Be(40);
  }

  [Fact]
  public void AboveThreshold_AppliesCatchUpFactor()
  {
    RdsScrollSpeedPolicy.EffectiveSpeed(40, 250, 256).Should().Be(60,
      "40 px/s × 1.5 catch-up = 60 px/s — news-ticker pace, still readable");
  }

  [Fact]
  public void EmptyBuffer_ReturnsBaseSpeed()
  {
    RdsScrollSpeedPolicy.EffectiveSpeed(40, 0, 256).Should().Be(40);
  }

  [Fact]
  public void DegenerateMaxChars_ReturnsBaseSpeed()
  {
    // Defensive — a zero/negative cap must never divide-by-zero or boost.
    RdsScrollSpeedPolicy.EffectiveSpeed(40, 100, 0).Should().Be(40);
  }

  [Fact]
  public void BoostScalesWithConfiguredSpeed()
  {
    RdsScrollSpeedPolicy.EffectiveSpeed(80, 250, 256).Should().Be(120,
      "the boost is a factor of the user's configured speed, not a fixed value");
  }
}
