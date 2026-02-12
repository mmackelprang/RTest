using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests;

/// <summary>
/// Integration tests for Radio.API project.
/// Tests the API startup and basic endpoint functionality.
/// </summary>
public class ApiTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;

  public ApiTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  [Fact]
  public async Task ApiApplication_StartsSuccessfully()
  {
    // Arrange
    var client = _factory.CreateClient();

    // Act - hit a known API endpoint to verify the application starts without exceptions
    var response = await client.GetAsync("/api/audio");

    // Assert - the main thing is that the app didn't crash on startup
    Assert.True(response.IsSuccessStatusCode,
      $"Expected success, but got {response.StatusCode}");
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
