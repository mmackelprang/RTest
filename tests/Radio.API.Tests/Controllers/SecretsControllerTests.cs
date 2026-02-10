using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the SecretsController.
/// </summary>
public class SecretsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public SecretsControllerTests(WebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task GetSectionSecrets_ReturnsOk_ForValidSection()
  {
    var response = await _client.GetAsync("/api/secrets/tts");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var data = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(data);
    Assert.True(data.ContainsKey("GoogleAPIKey"));
    Assert.True(data.ContainsKey("AzureAPIKey"));
    Assert.True(data.ContainsKey("AzureRegion"));
  }

  [Fact]
  public async Task GetSectionSecrets_ReturnsBadRequest_ForUnknownSection()
  {
    var response = await _client.GetAsync("/api/secrets/nonexistent");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task PostSectionSecrets_StoresAndRetrieves_TtsSecrets()
  {
    // Arrange - store a test key
    var data = new Dictionary<string, string>
    {
      ["GoogleAPIKey"] = "test-google-key-12345"
    };

    // Act - store
    var postResponse = await _client.PostAsJsonAsync("/api/secrets/tts", data);
    Assert.True(postResponse.IsSuccessStatusCode);

    // Act - retrieve raw
    var getResponse = await _client.GetAsync("/api/secrets/tts?raw=true");
    Assert.True(getResponse.IsSuccessStatusCode);

    var result = await getResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(result);
    Assert.Equal("test-google-key-12345", result!["GoogleAPIKey"]);
  }

  [Fact]
  public async Task PostSectionSecrets_ReturnsBadRequest_ForUnknownSection()
  {
    var data = new Dictionary<string, string> { ["Key"] = "val" };
    var response = await _client.PostAsJsonAsync("/api/secrets/nonexistent", data);
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task DeleteSectionSecrets_ReturnsOk()
  {
    // Arrange - store something first
    var data = new Dictionary<string, string> { ["ApiKey"] = "delete-me" };
    await _client.PostAsJsonAsync("/api/secrets/acoustid", data);

    // Act
    var response = await _client.DeleteAsync("/api/secrets/acoustid");
    Assert.True(response.IsSuccessStatusCode);

    // Verify deleted
    var getResponse = await _client.GetAsync("/api/secrets/acoustid?raw=true");
    var result = await getResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.NotNull(result);
    Assert.Equal("", result!["ApiKey"]);
  }

  [Fact]
  public async Task DeleteSectionSecrets_ReturnsBadRequest_ForUnknownSection()
  {
    var response = await _client.DeleteAsync("/api/secrets/nonexistent");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ListTags_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/secrets/tags");
    Assert.True(response.IsSuccessStatusCode);

    var tags = await response.Content.ReadFromJsonAsync<List<string>>();
    Assert.NotNull(tags);
  }

  [Fact]
  public async Task GetSectionSecrets_MasksValues_ByDefault()
  {
    // Arrange - store a key long enough to be masked
    var data = new Dictionary<string, string> { ["GoogleAPIKey"] = "abcdefghijklmnop" };
    await _client.PostAsJsonAsync("/api/secrets/tts", data);

    // Act - get without raw=true
    var response = await _client.GetAsync("/api/secrets/tts");
    var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

    Assert.NotNull(result);
    // Masked value should contain "..."
    Assert.Contains("...", result!["GoogleAPIKey"]);
    Assert.NotEqual("abcdefghijklmnop", result["GoogleAPIKey"]);
  }
}
