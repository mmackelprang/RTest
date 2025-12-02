using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the ConfigurationController.
/// </summary>
public class ConfigurationControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public ConfigurationControllerTests(WebApplicationFactory<Program> factory)
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
  public async Task UpdateConfiguration_WithValidRequest_ReturnsNotImplemented()
  {
    // Arrange - Configuration updates require IConfigurationManager integration
    var request = new UpdateConfigurationRequest
    {
      Section = "Audio",
      Key = "DuckingPercentage",
      Value = "30"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/configuration", request);

    // Assert - Expect 501 until configuration persistence is fully implemented
    Assert.Equal(System.Net.HttpStatusCode.NotImplemented, response.StatusCode);
  }
}
