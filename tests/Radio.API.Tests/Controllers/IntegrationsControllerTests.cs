using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for IntegrationsController.
/// Tests rotary encoder and phone integration status endpoints.
/// </summary>
public class IntegrationsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public IntegrationsControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task GetEncoderStatus_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/integrations/encoder/status");

    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("enabled", content, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task GetPhoneStatus_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/integrations/phone/status");

    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("enabled", content, StringComparison.OrdinalIgnoreCase);
  }
}
