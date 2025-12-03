using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the QueueController.
/// </summary>
public class QueueControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public QueueControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetQueue_WithNoActiveSource_ReturnsNotFound()
  {
    // Act
    var response = await _client.GetAsync("/api/queue");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task AddToQueue_WithNoActiveSource_ReturnsNotFound()
  {
    // Arrange
    var request = new AddToQueueRequest
    {
      TrackIdentifier = "/path/to/test.mp3"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/queue/add", request);

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task RemoveFromQueue_WithNoActiveSource_ReturnsNotFound()
  {
    // Act
    var response = await _client.DeleteAsync("/api/queue/0");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task ClearQueue_WithNoActiveSource_ReturnsNotFound()
  {
    // Act
    var response = await _client.DeleteAsync("/api/queue");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task MoveQueueItem_WithNoActiveSource_ReturnsNotFound()
  {
    // Arrange
    var request = new MoveQueueItemRequest
    {
      FromIndex = 0,
      ToIndex = 1
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/queue/move", request);

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

  [Fact]
  public async Task JumpToIndex_WithNoActiveSource_ReturnsNotFound()
  {
    // Act
    var response = await _client.PostAsync("/api/queue/jump/0", null);

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No primary audio source is active", content);
  }

}
