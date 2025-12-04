namespace Radio.Core.Tests.Metrics;

using Radio.Core.Metrics;
using Xunit;

public class MetricTypeTests
{
  [Fact]
  public void MetricType_Counter_HasCorrectValue()
  {
    // Arrange & Act
    var counter = MetricType.Counter;

    // Assert
    Assert.Equal(0, (int)counter);
  }

  [Fact]
  public void MetricType_Gauge_HasCorrectValue()
  {
    // Arrange & Act
    var gauge = MetricType.Gauge;

    // Assert
    Assert.Equal(1, (int)gauge);
  }
}
