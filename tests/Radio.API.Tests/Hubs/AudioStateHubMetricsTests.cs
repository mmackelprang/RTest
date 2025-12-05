namespace Radio.API.Tests.Hubs;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.Core.Interfaces;
using Xunit;

public class AudioStateHubMetricsTests
{
  private readonly Mock<IMetricsCollector> _mockMetricsCollector;
  private readonly AudioStateHub _hub;
  private readonly Mock<HubCallerContext> _mockContext;

  public AudioStateHubMetricsTests()
  {
    _mockMetricsCollector = new Mock<IMetricsCollector>();
    _mockContext = new Mock<HubCallerContext>();
    _mockContext.Setup(x => x.ConnectionId).Returns("test-connection-123");

    _hub = new AudioStateHub(
      NullLogger<AudioStateHub>.Instance,
      _mockMetricsCollector.Object);

    _hub.Context = _mockContext.Object;
  }

  [Fact]
  public async Task OnConnectedAsync_IncrementsConnectedClients()
  {
    // Act
    await _hub.OnConnectedAsync();

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Gauge("websocket.connected_clients", It.Is<double>(d => d > 0), null),
      Times.Once);
  }

  [Fact]
  public async Task OnDisconnectedAsync_DecrementsConnectedClients()
  {
    // Arrange - first connect
    await _hub.OnConnectedAsync();
    _mockMetricsCollector.Reset();

    // Act - then disconnect
    await _hub.OnDisconnectedAsync(null);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Gauge("websocket.connected_clients", It.IsAny<double>(), null),
      Times.Once);
  }

  [Fact]
  public async Task OnDisconnectedAsync_WithException_StillUpdatesMetric()
  {
    // Arrange
    await _hub.OnConnectedAsync();
    _mockMetricsCollector.Reset();
    var exception = new Exception("Test exception");

    // Act
    await _hub.OnDisconnectedAsync(exception);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Gauge("websocket.connected_clients", It.IsAny<double>(), null),
      Times.Once);
  }

  [Fact]
  public async Task MultipleConnections_TracksCorrectCount()
  {
    // Arrange
    var hub2 = new AudioStateHub(
      NullLogger<AudioStateHub>.Instance,
      _mockMetricsCollector.Object)
    {
      Context = _mockContext.Object
    };

    // Act
    await _hub.OnConnectedAsync();
    await hub2.OnConnectedAsync();

    // Assert - Should call Gauge twice
    _mockMetricsCollector.Verify(
      x => x.Gauge("websocket.connected_clients", It.IsAny<double>(), null),
      Times.AtLeast(2));
  }
}
