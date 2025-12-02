using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SourcesController.
/// </summary>
public class SourcesControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public SourcesControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetSources_ReturnsAvailableSources()
  {
    // Act
    var response = await _client.GetAsync("/api/sources");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var sources = await response.Content.ReadFromJsonAsync<AvailableSourcesDto>();
    Assert.NotNull(sources);
    Assert.NotNull(sources.PrimarySources);
    Assert.NotEmpty(sources.PrimarySources);
    Assert.Contains("Spotify", sources.PrimarySources);
    Assert.Contains("Radio", sources.PrimarySources);
    Assert.Contains("Vinyl", sources.PrimarySources);
    Assert.Contains("FilePlayer", sources.PrimarySources);
    Assert.Contains("GenericUSB", sources.PrimarySources);
  }

  [Fact]
  public async Task GetActiveSources_ReturnsEmptyOrSourceList()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/active");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var sources = await response.Content.ReadFromJsonAsync<List<AudioSourceDto>>();
    Assert.NotNull(sources);
  }

  [Fact]
  public async Task GetEventSources_ReturnsEmptyOrSourceList()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/events");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var sources = await response.Content.ReadFromJsonAsync<List<AudioSourceDto>>();
    Assert.NotNull(sources);
  }

  [Fact]
  public async Task GetPrimarySource_WhenNoSourceActive_Returns404()
  {
    // Act
    var response = await _client.GetAsync("/api/sources/primary");

    // Assert
    // Expect 404 when no primary source is active
    Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task SelectSource_WithEmptySourceType_ReturnsBadRequest()
  {
    // Arrange
    var request = new SelectSourceRequest
    {
      SourceType = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SelectSource_WithInvalidSourceType_ReturnsBadRequest()
  {
    // Arrange
    var request = new SelectSourceRequest
    {
      SourceType = "InvalidSource"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SelectSource_WithValidSourceType_ReturnsNotImplemented()
  {
    // Arrange - Source switching requires Phase 3 completion
    var request = new SelectSourceRequest
    {
      SourceType = "Spotify"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/sources", request);

    // Assert - Expect 501 until Phase 3 is completed
    Assert.Equal(System.Net.HttpStatusCode.NotImplemented, response.StatusCode);
  }
}
