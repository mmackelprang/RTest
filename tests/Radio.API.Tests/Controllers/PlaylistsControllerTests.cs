using System.Net;
using System.Net.Http.Json;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for PlaylistsController.
/// Tests playlist CRUD operations via HTTP endpoints.
/// </summary>
public class PlaylistsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public PlaylistsControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task GetAll_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/playlists");

    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
  }

  [Fact]
  public async Task GetById_WithNonExistentId_ReturnsNotFound()
  {
    var response = await _client.GetAsync("/api/playlists/nonexistent-id");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Create_WithEmptyName_ReturnsBadRequest()
  {
    var request = new { Name = "", Description = "test" };

    var response = await _client.PostAsJsonAsync("/api/playlists", request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Create_WithWhitespaceName_ReturnsBadRequest()
  {
    var request = new { Name = "   ", Description = "test" };

    var response = await _client.PostAsJsonAsync("/api/playlists", request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Delete_WithNonExistentId_ReturnsNotFound()
  {
    var response = await _client.DeleteAsync("/api/playlists/nonexistent-id");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Load_WithNonExistentId_ReturnsNotFound()
  {
    var response = await _client.PostAsync("/api/playlists/nonexistent-id/load", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}
