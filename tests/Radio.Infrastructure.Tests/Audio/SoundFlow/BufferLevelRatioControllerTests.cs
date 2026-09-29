using Radio.Infrastructure.Audio.SoundFlow;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// AUD-15: closed-loop control of the BT input resampler ratio, holding the playback buffer at a target.
/// </summary>
public class BufferLevelRatioControllerTests
{
  private const double BaseRatio = 1.00025;
  private const int SamplesPerSecond = 96_000;   // 48 kHz stereo
  private const int Target = 9_600;              // 100 ms

  private static BufferLevelRatioController Create() => new(BaseRatio, Target, SamplesPerSecond);

  private static double Ppm(double ratio) => (ratio / BaseRatio - 1.0) * 1e6;

  [Fact]
  public void BeforeAnIntervalElapses_ReturnsNull()
  {
    var controller = Create();

    Assert.Null(controller.Observe(0, 0.0));
    Assert.Null(controller.Observe(0, 0.5));
    Assert.Equal(BaseRatio, controller.Ratio);
  }

  [Fact]
  public void BufferBelowTarget_RaisesTheRatio()
  {
    var controller = Create();
    controller.Observe(0, 0.0);

    var ratio = controller.Observe(0, 1.0);

    Assert.NotNull(ratio);
    Assert.True(ratio > BaseRatio);
  }

  [Fact]
  public void BufferAboveTarget_LowersTheRatio()
  {
    var controller = Create();
    controller.Observe(Target * 3, 0.0);

    var ratio = controller.Observe(Target * 3, 1.0);

    Assert.True(ratio < BaseRatio);
  }

  [Fact]
  public void AtTarget_TheRatioStaysAtTheBase()
  {
    var controller = Create();
    controller.Observe(Target, 0.0);

    Assert.Equal(BaseRatio, controller.Observe(Target, 1.0)!.Value, 12);
  }

  [Fact]
  public void EachUpdate_MovesAtMostTwentyPpm()
  {
    var controller = Create();
    controller.Observe(0, 0.0);

    var first = controller.Observe(0, 1.0)!.Value;
    var second = controller.Observe(0, 2.0)!.Value;

    Assert.InRange(Ppm(first), 0, 20.0001);
    Assert.InRange(Ppm(second) - Ppm(first), 0, 20.0001);
  }

  [Fact]
  public void AUsefulCorrection_NeverExceedsFiveHundredPpm()
  {
    var controller = Create();
    var t = 0.0;
    for (var i = 0; i < 500; i++, t += 1.0)
    {
      controller.Observe(0, t);
    }

    Assert.InRange(Ppm(controller.Ratio), 499.0, 500.0001);
  }

  [Fact]
  public void SkipWindow_LeavesTheRatioAlone()
  {
    var controller = Create();
    controller.Observe(0, 0.0);
    controller.Observe(0, 1.0);
    var before = controller.Ratio;

    controller.SkipWindow();

    Assert.Null(controller.Observe(0, 1.5));
    Assert.Equal(before, controller.Ratio);
  }

  /// <summary>
  /// A plant with a 100 ppm clock error the static ratio does not know about — the shape measured on the
  /// box 2026-09-29. Without control the buffer drains; with it, the level settles near the target and
  /// never runs dry.
  /// </summary>
  [Fact]
  public void WithAHundredPpmResidualError_TheBufferSettlesAtTargetAndNeverRunsDry()
  {
    var controller = Create();
    const double dt = 0.0107;                                   // one PipeWire quantum
    const double consumerPerSecond = SamplesPerSecond;
    var producerInputPerSecond = SamplesPerSecond / BaseRatio * (1 - 100e-6);  // 100 ppm slow
    double level = Target;
    var minLevel = level;

    for (var t = 0.0; t < 1800; t += dt)                        // 30 minutes
    {
      level += (producerInputPerSecond * controller.Ratio - consumerPerSecond) * dt;
      minLevel = Math.Min(minLevel, level);
      controller.Observe((int)level, t);
    }

    Assert.True(minLevel > 0, $"buffer ran dry (min {minLevel:F0} samples)");
    Assert.InRange(level, Target * 0.8, Target * 1.2);
  }
}
