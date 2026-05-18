using System.Net.Http.Json;
using Radio.API.Models;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the ConfigurationController.
/// </summary>
public class ConfigurationControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public ConfigurationControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetConfiguration_ReturnsFullConfiguration()
  {
    // Act
    var response = await _client.GetAsync("/api/configuration");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var config = await response.Content.ReadFromJsonAsync<ConfigurationSettingsDto>();
    Assert.NotNull(config);
    Assert.NotNull(config.Audio);
    Assert.NotNull(config.Visualizer);
    Assert.NotNull(config.Output);
  }

  [Fact]
  public async Task GetAudioConfiguration_ReturnsAudioSettings()
  {
    // Act
    var response = await _client.GetAsync("/api/configuration/audio");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var config = await response.Content.ReadFromJsonAsync<AudioConfigurationDto>();
    Assert.NotNull(config);
    Assert.NotEmpty(config.DefaultSource);
    Assert.InRange(config.DuckingPercentage, 0, 100);
    Assert.NotEmpty(config.DuckingPolicy);
  }

  [Fact]
  public async Task GetVisualizerConfiguration_ReturnsVisualizerSettings()
  {
    // Act
    var response = await _client.GetAsync("/api/configuration/visualizer");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var config = await response.Content.ReadFromJsonAsync<VisualizerConfigurationDto>();
    Assert.NotNull(config);
    Assert.True(config.FFTSize > 0);
    Assert.True(config.WaveformSampleCount > 0);
  }

  [Fact]
  public async Task GetOutputConfiguration_ReturnsOutputSettings()
  {
    // Act
    var response = await _client.GetAsync("/api/configuration/output");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var config = await response.Content.ReadFromJsonAsync<OutputConfigurationDto>();
    Assert.NotNull(config);
    Assert.NotNull(config.Local);
    Assert.NotNull(config.HttpStream);
    Assert.NotNull(config.GoogleCast);
  }

  [Fact]
  public async Task UpdateConfiguration_WithEmptySection_ReturnsBadRequest()
  {
    // Arrange
    var request = new UpdateConfigurationRequest
    {
      Section = "",
      Key = "SomeKey",
      Value = "SomeValue"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/configuration", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task UpdateConfiguration_WithEmptyKey_ReturnsBadRequest()
  {
    // Arrange
    var request = new UpdateConfigurationRequest
    {
      Section = "Audio",
      Key = "",
      Value = "SomeValue"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/configuration", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task UpdateConfiguration_WithValidRequest_ReturnsOkOrNotImplemented()
  {
    // Arrange - Configuration updates with IConfigurationManager integration
    var request = new UpdateConfigurationRequest
    {
      Section = "Audio",
      Key = "DuckingPercentage",
      Value = "30"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/configuration", request);

    // Assert - Should return 200 OK if ConfigurationManager is available,
    // or 501 if not yet integrated
    Assert.True(
      response.StatusCode == System.Net.HttpStatusCode.OK ||
      response.StatusCode == System.Net.HttpStatusCode.NotImplemented,
      $"Expected OK or NotImplemented, got {response.StatusCode}");
  }

  // PR D #30 — ScanStopThreshold range validation.
  [Fact]
  public async Task UpdateRadioSection_ScanStopThresholdAbove100_ReturnsBadRequest()
  {
    // Arrange — POST /api/configuration/radio with ScanStopThreshold = 150
    var sectionPayload = new Dictionary<string, object>
    {
      ["ScanStopThreshold"] = 150,
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/configuration/radio", sectionPayload);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("ScanStopThreshold", body);
  }

  [Fact]
  public async Task UpdateRadioSection_ScanStopThresholdNegative_ReturnsBadRequest()
  {
    var sectionPayload = new Dictionary<string, object>
    {
      ["ScanStopThreshold"] = -5,
    };

    var response = await _client.PostAsJsonAsync("/api/configuration/radio", sectionPayload);

    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task UpdateRadioSection_ScanStopThresholdInRange_DoesNotReturn400()
  {
    var sectionPayload = new Dictionary<string, object>
    {
      ["ScanStopThreshold"] = 75,
    };

    var response = await _client.PostAsJsonAsync("/api/configuration/radio", sectionPayload);

    // Acceptable: 200 OK (write succeeded) or 5xx (configuration manager
    // unavailable in this test host). The case we MUST NOT see is 400.
    Assert.NotEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }
}
