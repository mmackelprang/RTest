namespace Radio.API.Tests.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Middleware;
using Radio.Core.Interfaces;
using Xunit;

public class ApiMetricsMiddlewareTests
{
  private readonly Mock<IMetricsCollector> _mockMetricsCollector;
  private readonly Mock<RequestDelegate> _mockNext;
  private readonly ApiMetricsMiddleware _middleware;

  public ApiMetricsMiddlewareTests()
  {
    _mockMetricsCollector = new Mock<IMetricsCollector>();
    _mockNext = new Mock<RequestDelegate>();
    
    _middleware = new ApiMetricsMiddleware(
      _mockNext.Object,
      _mockMetricsCollector.Object,
      NullLogger<ApiMetricsMiddleware>.Instance);
  }

  [Fact]
  public async Task InvokeAsync_IncrementsApiRequestsTotal()
  {
    // Arrange
    var context = new DefaultHttpContext();

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Increment("api.requests_total", 1.0, null),
      Times.Once);
  }

  [Fact]
  public async Task InvokeAsync_CallsNextMiddleware()
  {
    // Arrange
    var context = new DefaultHttpContext();

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockNext.Verify(x => x(context), Times.Once);
  }

  [Fact]
  public async Task InvokeAsync_WithoutMetricsCollector_StillCallsNext()
  {
    // Arrange
    var context = new DefaultHttpContext();
    var middleware = new ApiMetricsMiddleware(
      _mockNext.Object,
      null,
      NullLogger<ApiMetricsMiddleware>.Instance);

    // Act
    await middleware.InvokeAsync(context);

    // Assert
    _mockNext.Verify(x => x(context), Times.Once);
  }

  [Fact]
  public async Task InvokeAsync_MultipleRequests_TracksEachRequest()
  {
    // Arrange
    var context = new DefaultHttpContext();

    // Act
    await _middleware.InvokeAsync(context);
    await _middleware.InvokeAsync(context);
    await _middleware.InvokeAsync(context);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Increment("api.requests_total", 1.0, null),
      Times.Exactly(3));
  }
}
