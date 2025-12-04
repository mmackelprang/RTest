namespace Radio.Core.Tests.Metrics;

using Radio.Core.Metrics;
using Xunit;

public class MetricResolutionTests
{
  [Fact]
  public void MetricResolution_Minute_HasCorrectValue()
  {
    // Arrange & Act
    var minute = MetricResolution.Minute;

    // Assert
    Assert.Equal(0, (int)minute);
  }

  [Fact]
  public void MetricResolution_Hour_HasCorrectValue()
  {
    // Arrange & Act
    var hour = MetricResolution.Hour;

    // Assert
    Assert.Equal(1, (int)hour);
  }

  [Fact]
  public void MetricResolution_Day_HasCorrectValue()
  {
    // Arrange & Act
    var day = MetricResolution.Day;

    // Assert
    Assert.Equal(2, (int)day);
  }
}
