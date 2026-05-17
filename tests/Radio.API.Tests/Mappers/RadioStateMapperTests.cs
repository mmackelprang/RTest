using Radio.API.Mappers;

namespace Radio.API.Tests.Mappers;

/// <summary>
/// Locks down the signal-meter projection math introduced in PR 1 of the
/// Radio Controller Polish arc: clamp at the API boundary, surface
/// overdrive via a separate <c>Clip</c> flag, and linear-fit raw percent →
/// dBu in the [-60, 0] band.
/// </summary>
public class RadioStateMapperTests
{
  [Theory]
  [InlineData(null, null)]
  [InlineData(0, 0)]
  [InlineData(50, 50)]
  [InlineData(100, 100)]
  [InlineData(101, 100)]     // 1% overshoot clamped
  [InlineData(118, 100)]     // historical worst-case from logs
  [InlineData(-5, 0)]        // negative-clipping clamp
  public void ClampSignalPercent_ReturnsExpected(int? raw, int? expected)
  {
    Assert.Equal(expected, RadioStateMapper.ClampSignalPercent(raw));
  }

  [Theory]
  [InlineData(null, false)]
  [InlineData(0, false)]
  [InlineData(99, false)]
  [InlineData(100, false)]
  [InlineData(101, true)]
  [InlineData(118, true)]
  public void IsClipping_TriggersOnlyAbove100(int? raw, bool expected)
  {
    Assert.Equal(expected, RadioStateMapper.IsClipping(raw));
  }

  [Theory]
  [InlineData(null, -60.0)]
  [InlineData(0, -60.0)]
  [InlineData(50, -30.0)]
  [InlineData(100, 0.0)]
  [InlineData(118, 0.0)]     // overdrive saturates at 0 dBu — IsClipping carries the rest
  public void SignalToDbu_LinearFit(int? raw, double expected)
  {
    Assert.Equal(expected, RadioStateMapper.SignalToDbu(raw), precision: 3);
  }

  [Theory]
  [InlineData(0, -60.0)]
  [InlineData(50, -30.0)]
  [InlineData(100, 0.0)]
  [InlineData(200, 0.0)]
  [InlineData(-10, -60.0)]
  public void PercentToDbu_LinearFitWithClamp(int percent, double expected)
  {
    Assert.Equal(expected, RadioStateMapper.PercentToDbu(percent), precision: 3);
  }

  [Fact]
  public void SignalMinDbu_AndMax_AreNoiseFloorAndFullScale()
  {
    Assert.Equal(-60.0, RadioStateMapper.SignalMinDbu);
    Assert.Equal(0.0, RadioStateMapper.SignalMaxDbu);
  }
}
