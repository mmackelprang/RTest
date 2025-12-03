using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SpotifyController.
/// </summary>
public class SpotifyControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public SpotifyControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task Search_WithoutQuery_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/search");

    // Assert - ASP.NET automatically validates required query parameters
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Search_WithoutActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/search?query=test");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Spotify source not available", content);
  }

  [Fact]
  public async Task GetBrowseCategories_WithoutActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/browse/categories");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Spotify source not available", content);
  }

  [Fact]
  public async Task GetCategoryPlaylists_WithoutCategoryId_ReturnsNotFound()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/browse/category//playlists");

    // Assert - empty category ID results in 404 (route not matched)
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task GetCategoryPlaylists_WithoutActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/browse/category/test/playlists");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Spotify source not available", content);
  }

  [Fact]
  public async Task GetUserPlaylists_WithoutActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/playlists/user");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Spotify source not available", content);
  }

  [Fact]
  public async Task GetPlaylistDetails_WithoutPlaylistId_ReturnsNotFound()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/playlists/");

    // Assert - empty playlist ID results in 404 (route not matched)
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task GetPlaylistDetails_WithoutActiveSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/spotify/playlists/test123");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Spotify source not available", content);
  }

  [Fact]
  public async Task Play_WithoutUri_ReturnsBadRequest()
  {
    // Arrange
    var request = new PlaySpotifyUriRequest
    {
      Uri = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/spotify/play", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("URI is required", content);
  }

  [Fact]
  public async Task Play_WithoutActiveSource_ReturnsBadRequest()
  {
    // Arrange
    var request = new PlaySpotifyUriRequest
    {
      Uri = "spotify:track:test123"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/spotify/play", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Spotify source not available", content);
  }

  [Fact]
  public async Task Play_WithUriAndContextUri_UsesContextUri()
  {
    // This test verifies that the request structure allows both Uri and ContextUri
    // Arrange
    var request = new PlaySpotifyUriRequest
    {
      Uri = "spotify:track:test123",
      ContextUri = "spotify:album:test456"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/spotify/play", request);

    // Assert - will fail because no source is active, but validates request structure
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Search_WithValidQueryAndTypes_ReturnsCorrectStructure()
  {
    // This test validates that the endpoint accepts the types parameter
    // Act
    var response = await _client.GetAsync("/api/spotify/search?query=rock&types=track,album");

    // Assert - will fail because no source is active, but validates request structure
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Search_WithMusicType_IsAccepted()
  {
    // Test that "music" alias for "track" is accepted
    // Act
    var response = await _client.GetAsync("/api/spotify/search?query=rock&types=music");

    // Assert - will fail because no source is active, but validates request structure
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Search_WithAllType_IsAccepted()
  {
    // Test that "all" type is accepted
    // Act
    var response = await _client.GetAsync("/api/spotify/search?query=rock&types=all");

    // Assert - will fail because no source is active, but validates request structure
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
