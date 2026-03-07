using System.Net;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for AudioDiagnosticsController.
/// Tests diagnostic pipeline info and capture endpoints.
/// </summary>
public class AudioDiagnosticsControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public AudioDiagnosticsControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task GetAudioPipelineDiagnostics_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/diagnostics/audio-pipeline");

    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("engineState", content);
    Assert.Contains("engineReady", content);
  }

  [Fact]
  public async Task GetAudioPipelineDiagnostics_ContainsPipelineInfo()
  {
    var response = await _client.GetAsync("/api/diagnostics/audio-pipeline");

    Assert.True(response.IsSuccessStatusCode);

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("timestamp", content);
  }
}
