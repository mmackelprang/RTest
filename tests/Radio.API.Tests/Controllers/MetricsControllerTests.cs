namespace Radio.API.Tests.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Interfaces;
using Radio.Core.Metrics;
using Xunit;

public class MetricsControllerTests
{
  private readonly Mock<IMetricsReader> _mockMetricsReader;
  private readonly Mock<IMetricsCollector> _mockMetricsCollector;
  private readonly MetricsController _controller;

  public MetricsControllerTests()
  {
    _mockMetricsReader = new Mock<IMetricsReader>();
    _mockMetricsCollector = new Mock<IMetricsCollector>();
    _controller = new MetricsController(
      NullLogger<MetricsController>.Instance,
      _mockMetricsReader.Object,
      _mockMetricsCollector.Object);
  }

  [Fact]
  public void RecordUIEvent_WithValidEvent_RecordsMetric()
  {
    // Arrange
    var request = new UIEventRequest
    {
      EventName = "button_clicks",
      Tags = new Dictionary<string, string>
      {
        ["button"] = "play",
        ["screen"] = "main"
      }
    };

    // Act
    var result = _controller.RecordUIEvent(request);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);

    _mockMetricsCollector.Verify(
      x => x.Increment(
        "ui.button_clicks",
        1.0,
        It.Is<IDictionary<string, string>>(d =>
          d["button"] == "play" && d["screen"] == "main")),
      Times.Once);
  }

  [Fact]
  public void RecordUIEvent_WithNullRequest_ReturnsBadRequest()
  {
    // Act
    var result = _controller.RecordUIEvent(null!);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    Assert.NotNull(badRequestResult.Value);
  }

  [Fact]
  public void RecordUIEvent_WithEmptyEventName_ReturnsBadRequest()
  {
    // Arrange
    var request = new UIEventRequest
    {
      EventName = "",
      Tags = null
    };

    // Act
    var result = _controller.RecordUIEvent(request);

    // Assert
    Assert.IsType<BadRequestObjectResult>(result);
  }

  [Fact]
  public void RecordUIEvent_WithNoTags_RecordsMetricWithoutTags()
  {
    // Arrange
    var request = new UIEventRequest
    {
      EventName = "page_view",
      Tags = null
    };

    // Act
    var result = _controller.RecordUIEvent(request);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);

    _mockMetricsCollector.Verify(
      x => x.Increment("ui.page_view", 1.0, null),
      Times.Once);
  }

  [Fact]
  public void RecordUIEvent_NormalizesEventName()
  {
    // Arrange
    var request = new UIEventRequest
    {
      EventName = "Play Clicked",  // Should be normalized to "play_clicked"
      Tags = null
    };

    // Act
    var result = _controller.RecordUIEvent(request);

    // Assert
    Assert.IsType<OkObjectResult>(result);

    _mockMetricsCollector.Verify(
      x => x.Increment("ui.play_clicked", 1.0, null),
      Times.Once);
  }

  [Fact]
  public void RecordUIEvent_WithoutMetricsCollector_StillSucceeds()
  {
    // Arrange
    var controller = new MetricsController(
      NullLogger<MetricsController>.Instance,
      _mockMetricsReader.Object,
      null);

    var request = new UIEventRequest
    {
      EventName = "test_event",
      Tags = null
    };

    // Act
    var result = controller.RecordUIEvent(request);

    // Assert - should not throw and return success
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);
  }

  [Fact]
  public async Task GetHistory_WithValidParams_ReturnsData()
  {
    // Arrange
    var key = "audio.songs_played_total";
    var start = DateTimeOffset.UtcNow.AddHours(-1);
    var end = DateTimeOffset.UtcNow;
    var expectedData = new List<MetricPoint>
    {
      new() { Key = key, Timestamp = start, Value = 10.0 },
      new() { Key = key, Timestamp = end, Value = 20.0 }
    };

    _mockMetricsReader
      .Setup(x => x.GetHistoryAsync(key, start, end, MetricResolution.Minute, null, It.IsAny<CancellationToken>()))
      .ReturnsAsync(expectedData);

    // Act
    var result = await _controller.GetHistory(key, start, end, MetricResolution.Minute);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var data = Assert.IsAssignableFrom<IReadOnlyList<MetricPoint>>(okResult.Value);
    Assert.Equal(2, data.Count);
  }

  [Fact]
  public async Task GetSnapshots_WithValidKeys_ReturnsData()
  {
    // Arrange
    var keys = "audio.songs_played_total,system.cpu_temp_celsius";
    var expectedData = new Dictionary<string, double>
    {
      ["audio.songs_played_total"] = 100.0,
      ["system.cpu_temp_celsius"] = 45.5
    };

    _mockMetricsReader
      .Setup(x => x.GetCurrentSnapshotsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(expectedData);

    // Act
    var result = await _controller.GetSnapshots(keys);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, double>>(okResult.Value);
    Assert.Equal(2, data.Count);
  }

  [Fact]
  public async Task GetAggregate_WithValidKey_ReturnsValue()
  {
    // Arrange
    var key = "audio.songs_played_total";
    var expectedValue = 42.0;

    _mockMetricsReader
      .Setup(x => x.GetAggregateAsync(key, It.IsAny<CancellationToken>()))
      .ReturnsAsync(expectedValue);

    // Act
    var result = await _controller.GetAggregate(key);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(expectedValue, okResult.Value);
  }
}
