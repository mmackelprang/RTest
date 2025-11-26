using Microsoft.AspNetCore.Mvc.Testing;

namespace Radio.API.Tests;

/// <summary>
/// Integration tests for Radio.API project.
/// Tests the API startup and basic endpoint functionality.
/// </summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public ApiTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  [Fact]
  public async Task ApiApplication_StartsSuccessfully()
  {
    // Arrange
    var client = _factory.CreateClient();

    // Act - try to access the swagger endpoint (should redirect or be available in development)
    // This verifies the application starts without exceptions
    var response = await client.GetAsync("/swagger/index.html");

    // Assert - We expect either success (200) or redirect (3xx) depending on environment
    // The main thing is that the app didn't crash on startup
    Assert.True(
      response.IsSuccessStatusCode || (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
      $"Expected success or redirect, but got {response.StatusCode}");
  }

  [Fact]
  public async Task ApiApplication_ReturnsNotFoundForUnknownRoute()
  {
    // Arrange
    var client = _factory.CreateClient();

    // Act
    var response = await client.GetAsync("/api/nonexistent");

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public void PlaceholderTest_ApiProjectConfigured()
  {
    // This test verifies the test project is correctly configured
    Assert.True(true);
  }
}
