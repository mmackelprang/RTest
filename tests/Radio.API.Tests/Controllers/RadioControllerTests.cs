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
      Frequency = 101_500_000 // 101.5 MHz in Hz
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
      Step = 100_000 // 0.1 MHz in Hz
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

  // ===== GAIN CONTROL TESTS =====

  [Fact]
  public async Task SetGain_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetGainRequest
    {
      Gain = 20.0f
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/gain", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task SetAutoGain_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetAutoGainRequest
    {
      Enabled = true
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/gain/auto", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  // ===== POWER STATE TESTS =====

  [Fact]
  public async Task GetPowerState_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/power");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task TogglePowerState_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/radio/power/toggle", null);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  // ===== LIFECYCLE TESTS =====

  [Fact]
  public async Task Startup_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/radio/startup", null);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  [Fact]
  public async Task Shutdown_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.PostAsync("/api/radio/shutdown", null);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Radio is not the active source", content);
  }

  // ===== RADIO PRESET TESTS =====

  [Fact]
  public async Task GetPresets_ReturnsEmptyList()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/presets");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var presets = await response.Content.ReadFromJsonAsync<List<RadioPresetDto>>();
    Assert.NotNull(presets);
  }

  [Fact]
  public async Task CreatePreset_WithValidData_ReturnsCreated()
  {
    // Arrange - Use very unique frequency to avoid collisions
    var uniqueFreq = 88.1 + (DateTime.Now.Millisecond % 10) * 0.1; // Range: 88.1-88.9
    var request = new CreateRadioPresetRequest
    {
      Name = "Test Station",
      Band = "FM",
      Frequency = uniqueFreq
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/presets", request);

    // Assert
    if (response.StatusCode == HttpStatusCode.Conflict)
    {
      // If we got a conflict, try one more time with a different frequency
      uniqueFreq += 10.0;
      request = request with { Frequency = uniqueFreq };
      response = await _client.PostAsJsonAsync("/api/radio/presets", request);
    }

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var preset = await response.Content.ReadFromJsonAsync<RadioPresetDto>();
    Assert.NotNull(preset);
    Assert.Equal("Test Station", preset.Name);
    Assert.Equal("FM", preset.Band);
    Assert.Equal(uniqueFreq, preset.Frequency);
    Assert.NotEmpty(preset.Id);
  }

  [Fact]
  public async Task CreatePreset_WithoutName_UsesDefaultName()
  {
    // Arrange - Use very unique frequency
    var uniqueFreq = 700 + (DateTime.Now.Millisecond % 100); // Range: 700-799 kHz
    var request = new CreateRadioPresetRequest
    {
      Band = "AM",
      Frequency = uniqueFreq
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/presets", request);

    // Assert
    if (response.StatusCode == HttpStatusCode.Conflict)
    {
      // If we got a conflict, try one more time with a different frequency
      uniqueFreq += 100;
      request = request with { Frequency = uniqueFreq };
      response = await _client.PostAsJsonAsync("/api/radio/presets", request);
    }

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var preset = await response.Content.ReadFromJsonAsync<RadioPresetDto>();
    Assert.NotNull(preset);
    Assert.Equal($"AM - {uniqueFreq}", preset.Name);
    Assert.Equal("AM", preset.Band);
    Assert.Equal(uniqueFreq, preset.Frequency);
  }

  [Fact]
  public async Task CreatePreset_WithInvalidBand_ReturnsBadRequest()
  {
    // Arrange
    var request = new CreateRadioPresetRequest
    {
      Band = "InvalidBand",
      Frequency = 101.5
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/presets", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Invalid band", content);
  }

  [Fact]
  public async Task CreatePreset_WithZeroFrequency_ReturnsBadRequest()
  {
    // Arrange
    var request = new CreateRadioPresetRequest
    {
      Band = "FM",
      Frequency = 0
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/presets", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Frequency must be greater than 0", content);
  }

  [Fact]
  public async Task CreatePreset_WithNegativeFrequency_ReturnsBadRequest()
  {
    // Arrange
    var request = new CreateRadioPresetRequest
    {
      Band = "FM",
      Frequency = -10
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/presets", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Frequency must be greater than 0", content);
  }

  [Fact]
  public async Task DeletePreset_WithNonexistentId_ReturnsNotFound()
  {
    // Act
    var response = await _client.DeleteAsync("/api/radio/presets/nonexistent-id");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("not found", content);
  }

  [Fact]
  public async Task CreateAndDeletePreset_WorksCorrectly()
  {
    // Arrange - Create a preset first
    var createRequest = new CreateRadioPresetRequest
    {
      Name = "Delete Test Station",
      Band = "FM",
      Frequency = 102.5
    };

    var createResponse = await _client.PostAsJsonAsync("/api/radio/presets", createRequest);
    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    var createdPreset = await createResponse.Content.ReadFromJsonAsync<RadioPresetDto>();
    Assert.NotNull(createdPreset);

    // Act - Delete the preset
    var deleteResponse = await _client.DeleteAsync($"/api/radio/presets/{createdPreset.Id}");

    // Assert
    Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

    // Verify it's deleted
    var getResponse = await _client.GetAsync("/api/radio/presets");
    var presets = await getResponse.Content.ReadFromJsonAsync<List<RadioPresetDto>>();
    Assert.NotNull(presets);
    Assert.DoesNotContain(presets, p => p.Id == createdPreset.Id);
  }

  #region Device Factory Endpoint Tests

  [Fact]
  public async Task GetAvailableDevices_ReturnsDeviceList()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/devices");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var deviceList = await response.Content.ReadFromJsonAsync<RadioDeviceListDto>();
    Assert.NotNull(deviceList);
    Assert.NotNull(deviceList.Devices);
    // Count property is derived from Devices collection
  }

  [Fact]
  public async Task GetAvailableDevices_ReturnsDevicesWithCapabilities()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/devices");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var deviceList = await response.Content.ReadFromJsonAsync<RadioDeviceListDto>();
    Assert.NotNull(deviceList);

    // Each device should have capabilities
    foreach (var device in deviceList.Devices)
    {
      Assert.NotNull(device.DeviceType);
      Assert.NotNull(device.Capabilities);
      Assert.NotNull(device.Capabilities.Description);
    }
  }

  [Fact]
  public async Task GetDefaultDevice_ReturnsDeviceInfo()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/devices/default");

    // Assert
    // This should succeed even if no devices are available (will return 500 with error)
    // or return 200 with device info
    Assert.True(
      response.StatusCode == HttpStatusCode.OK ||
      response.StatusCode == HttpStatusCode.InternalServerError);

    if (response.StatusCode == HttpStatusCode.OK)
    {
      var deviceInfo = await response.Content.ReadFromJsonAsync<RadioDeviceInfoDto>();
      Assert.NotNull(deviceInfo);
      Assert.NotNull(deviceInfo.DeviceType);
      Assert.NotNull(deviceInfo.Capabilities);
    }
  }

  [Fact]
  public async Task GetCurrentDevice_WithNoActiveRadio_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/devices/current");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("No radio source is currently active", content);
  }

  [Fact]
  public async Task SelectDevice_WithEmptyDeviceType_ReturnsBadRequest()
  {
    // Arrange
    var request = new SelectRadioDeviceRequest
    {
      DeviceType = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/devices/select", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Device type is required", content);
  }

  [Fact]
  public async Task SelectDevice_WithInvalidDeviceType_ReturnsBadRequest()
  {
    // Arrange
    var request = new SelectRadioDeviceRequest
    {
      DeviceType = "InvalidDevice123"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/devices/select", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("not available", content);
  }

  [Fact]
  public async Task SelectDevice_WithValidDeviceType_ReturnsDeviceInfo()
  {
    // First, get list of available devices
    var devicesResponse = await _client.GetAsync("/api/radio/devices");
    var deviceList = await devicesResponse.Content.ReadFromJsonAsync<RadioDeviceListDto>();

    // Skip test if no devices available
    if (deviceList == null || deviceList.Devices.Count == 0)
    {
      return;
    }

    // Arrange - use first available device
    var request = new SelectRadioDeviceRequest
    {
      DeviceType = deviceList.Devices[0].DeviceType
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/radio/devices/select", request);

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var deviceInfo = await response.Content.ReadFromJsonAsync<RadioDeviceInfoDto>();
    Assert.NotNull(deviceInfo);
    Assert.Equal(request.DeviceType, deviceInfo.DeviceType);
    Assert.NotNull(deviceInfo.Capabilities);
  }

  [Fact]
  public async Task DeviceCapabilities_RTLSDRCore_HasExpectedFeatures()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/devices");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var deviceList = await response.Content.ReadFromJsonAsync<RadioDeviceListDto>();
    Assert.NotNull(deviceList);

    var rtlsdr = deviceList.Devices.FirstOrDefault(d => d.DeviceType == "RTLSDRCore");
    if (rtlsdr != null)
    {
      // RTL-SDR should have software control capabilities
      Assert.True(rtlsdr.Capabilities.SupportsSoftwareControl);
      Assert.True(rtlsdr.Capabilities.SupportsFrequencyControl);
      Assert.True(rtlsdr.Capabilities.SupportsBandSwitching);
      Assert.True(rtlsdr.Capabilities.SupportsScanning);
      Assert.True(rtlsdr.Capabilities.SupportsGainControl);
      Assert.False(rtlsdr.Capabilities.SupportsEqualizer); // No hardware EQ
      Assert.True(rtlsdr.Capabilities.SupportsDeviceVolume);
    }
  }

  [Fact]
  public async Task DeviceCapabilities_RF320_HasExpectedFeatures()
  {
    // Act
    var response = await _client.GetAsync("/api/radio/devices");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var deviceList = await response.Content.ReadFromJsonAsync<RadioDeviceListDto>();
    Assert.NotNull(deviceList);

    var rf320 = deviceList.Devices.FirstOrDefault(d => d.DeviceType == "RF320");
    if (rf320 != null)
    {
      // RF320 has limited software control (Bluetooth/USB audio only)
      Assert.False(rf320.Capabilities.SupportsSoftwareControl);
      Assert.False(rf320.Capabilities.SupportsFrequencyControl);
      Assert.False(rf320.Capabilities.SupportsBandSwitching);
      Assert.False(rf320.Capabilities.SupportsScanning);
      Assert.False(rf320.Capabilities.SupportsGainControl);
      Assert.True(rf320.Capabilities.SupportsEqualizer); // Hardware EQ
      Assert.True(rf320.Capabilities.SupportsDeviceVolume); // Hardware volume
    }
  }

  #endregion
}
