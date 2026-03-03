using Radio.Infrastructure.Audio.SoundFlow;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

public class LimiterModifierTests
{
  private readonly LimiterModifier _limiter = new();

  [Theory]
  [InlineData(0.0f)]
  [InlineData(0.5f)]
  [InlineData(0.89f)]
  [InlineData(-0.5f)]
  [InlineData(-0.89f)]
  public void BelowThreshold_PassesThrough(float input)
  {
    var output = _limiter.ProcessSample(input, channel: 0);
    Assert.Equal(input, output, precision: 6);
  }

  [Fact]
  public void AtThreshold_IsContinuous()
  {
    // Just below threshold should pass through; just above should be very close
    var threshold = LimiterModifier.DefaultThreshold;
    var below = _limiter.ProcessSample(threshold - 0.001f, channel: 0);
    var above = _limiter.ProcessSample(threshold + 0.001f, channel: 0);

    // The discontinuity should be negligible (< 0.002)
    Assert.True(MathF.Abs(above - below) < 0.002f,
      $"Discontinuity at threshold: below={below:F6}, above={above:F6}, delta={MathF.Abs(above - below):F6}");
  }

  [Theory]
  [InlineData(0.95f)]
  [InlineData(1.0f)]
  [InlineData(1.5f)]
  [InlineData(2.0f)]
  public void AboveThreshold_LimitedAtOrBelowOne(float input)
  {
    var output = _limiter.ProcessSample(input, channel: 0);
    Assert.True(output <= 1.0f, $"Expected output <= 1.0 for input {input}, got {output}");
    Assert.True(output > LimiterModifier.DefaultThreshold,
      $"Expected output > threshold for input {input}, got {output}");
  }

  [Fact]
  public void ExtremeInput_OutputVeryCloseToOne()
  {
    var output = _limiter.ProcessSample(10.0f, channel: 0);
    Assert.True(output > 0.99f, $"Expected output > 0.99 for input 10.0, got {output}");
    // tanh saturates to 1.0 in float32 for large inputs — that's correct behavior
    Assert.True(output <= 1.0f, $"Expected output <= 1.0 for input 10.0, got {output}");
  }

  [Theory]
  [InlineData(0.95f)]
  [InlineData(1.5f)]
  [InlineData(10.0f)]
  public void NegativeSamples_SymmetricWithPositive(float magnitude)
  {
    var positive = _limiter.ProcessSample(magnitude, channel: 0);
    var negative = _limiter.ProcessSample(-magnitude, channel: 0);

    Assert.Equal(positive, -negative, precision: 6);
  }

  [Fact]
  public void Name_IsLimiter()
  {
    Assert.Equal("Limiter", _limiter.Name);
  }
}
