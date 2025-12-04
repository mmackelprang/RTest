namespace Radio.Core.Tests.Metrics;

using Radio.Core.Metrics;
using Xunit;

public class MetricPointTests
{
  [Fact]
  public void MetricPoint_CanBeCreated_WithRequiredProperties()
  {
    // Arrange
    var key = "test.metric";
    var timestamp = DateTimeOffset.UtcNow;
    var value = 42.5;

    // Act
    var point = new MetricPoint
    {
      Key = key,
      Timestamp = timestamp,
      Value = value
    };

    // Assert
    Assert.Equal(key, point.Key);
    Assert.Equal(timestamp, point.Timestamp);
    Assert.Equal(value, point.Value);
    Assert.Equal(0, point.Count);
    Assert.Null(point.Min);
    Assert.Null(point.Max);
    Assert.Null(point.Last);
    Assert.Null(point.Tags);
  }

  [Fact]
  public void MetricPoint_CanBeCreated_WithAllProperties()
  {
    // Arrange
    var key = "test.gauge";
    var timestamp = DateTimeOffset.UtcNow;
    var value = 100.0;
    var count = 10;
    var min = 50.0;
    var max = 150.0;
    var last = 120.0;
    var tags = new Dictionary<string, string>
    {
      { "env", "test" },
      { "region", "us-east" }
    };

    // Act
    var point = new MetricPoint
    {
      Key = key,
      Timestamp = timestamp,
      Value = value,
      Count = count,
      Min = min,
      Max = max,
      Last = last,
      Tags = tags
    };

    // Assert
    Assert.Equal(key, point.Key);
    Assert.Equal(timestamp, point.Timestamp);
    Assert.Equal(value, point.Value);
    Assert.Equal(count, point.Count);
    Assert.Equal(min, point.Min);
    Assert.Equal(max, point.Max);
    Assert.Equal(last, point.Last);
    Assert.NotNull(point.Tags);
    Assert.Equal(2, point.Tags.Count);
    Assert.Equal("test", point.Tags["env"]);
    Assert.Equal("us-east", point.Tags["region"]);
  }

  [Fact]
  public void MetricPoint_IsRecord_SupportsEquality()
  {
    // Arrange
    var timestamp = DateTimeOffset.UtcNow;
    var point1 = new MetricPoint
    {
      Key = "test.metric",
      Timestamp = timestamp,
      Value = 42.0
    };
    var point2 = new MetricPoint
    {
      Key = "test.metric",
      Timestamp = timestamp,
      Value = 42.0
    };
    var point3 = new MetricPoint
    {
      Key = "test.metric",
      Timestamp = timestamp,
      Value = 43.0
    };

    // Act & Assert
    Assert.Equal(point1, point2);
    Assert.NotEqual(point1, point3);
  }
}
