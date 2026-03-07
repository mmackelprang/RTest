using System.Net;
using System.Net.Http.Json;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for NotificationsController.
/// Tests TTS announcement endpoint.
/// </summary>
public class NotificationsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public NotificationsControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task Announce_WithValidMessage_ReturnsOk()
  {
    var request = new { Message = "Test announcement", Priority = 5 };

    var response = await _client.PostAsJsonAsync("/api/notifications/announce", request);

    // Should succeed (announcement is fire-and-forget)
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
  }

  [Fact]
  public async Task Announce_WithEmptyMessage_ReturnsBadRequest()
  {
    var request = new { Message = "", Priority = 5 };

    var response = await _client.PostAsJsonAsync("/api/notifications/announce", request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
