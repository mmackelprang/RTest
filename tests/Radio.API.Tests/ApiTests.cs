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

  // OPS-14: the API docs are served outside Development too. This factory runs the host in the
  // "Testing" environment, so these fail if the mapping is put back behind IsDevelopment().
  [Fact]
  public async Task OpenApiDocument_IsServedOutsideDevelopment()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/openapi/v1.json");

    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("\"openapi\"", body);
    Assert.Contains("/api/audio", body);
  }

  [Fact]
  public async Task ScalarReference_IsServedOutsideDevelopment()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/scalar/v1");

    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
  }

  [Fact]
  public void PlaceholderTest_ApiProjectConfigured()
  {
    // This test verifies the test project is correctly configured
    Assert.True(true);
  }
}
