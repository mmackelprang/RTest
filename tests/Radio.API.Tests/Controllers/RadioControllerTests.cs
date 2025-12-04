using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the RadioController.
/// </summary>
public class RadioControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public RadioControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetRadioState_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/state");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetFrequency_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetFrequencyRequest
    {
      Frequency = 101.5
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/frequency", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task StepFrequencyUp_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/radio/frequency/up", null);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task StepFrequencyDown_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/radio/frequency/down", null);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetBand_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetBandRequest
    {
      Band = "FM"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/band", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetBand_WithInvalidBand_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetBandRequest
    {
      Band = "InvalidBand"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/band", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Invalid band", content);
  }

  [Fact]
  public async Task SetFrequencyStep_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetFrequencyStepRequest
    {
      Step = 0.1
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/step", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task StartScan_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new StartScanRequest
    {
      Direction = "Up"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/scan/start", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task StartScan_WithInvalidDirection_ReturnsBadRequest()
  {
    // Arrange
    var request = new StartScanRequest
    {
      Direction = "InvalidDirection"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/scan/start", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Invalid scan direction", content);
  }

  [Fact]
  public async Task StopScan_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/radio/scan/stop", null);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetEqualizerMode_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetEqualizerModeRequest
    {
      Mode = "Rock"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/eq", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetEqualizerMode_WithInvalidMode_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetEqualizerModeRequest
    {
      Mode = "InvalidMode"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/eq", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Invalid equalizer mode", content);
  }

  [Fact]
  public async Task SetDeviceVolume_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetDeviceVolumeRequest
    {
      Volume = 50
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/volume", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetDeviceVolume_WithInvalidVolume_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetDeviceVolumeRequest
    {
      Volume = 150 // Invalid - should be 0-100
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/volume", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Volume must be between 0 and 100", content);
  }

  [Fact]
  public async Task SetDeviceVolume_WithNegativeVolume_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetDeviceVolumeRequest
    {
      Volume = -10 // Invalid - should be 0-100
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/volume", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Volume must be between 0 and 100", content);
  }
}
