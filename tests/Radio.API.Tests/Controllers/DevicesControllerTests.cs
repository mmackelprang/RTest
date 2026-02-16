using System.Net.Http.Json;
using Radio.API.Tests.TestSupport;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the DevicesController.
/// </summary>
public class DevicesControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public DevicesControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetOutputDevices_ReturnsDeviceList()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/output");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var devices = await response.Content.ReadFromJsonAsync<List<AudioDeviceDto>>();
    Assert.NotNull(devices);
  }

  [Fact]
  public async Task GetInputDevices_ReturnsDeviceList()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/input");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var devices = await response.Content.ReadFromJsonAsync<List<AudioDeviceDto>>();
    Assert.NotNull(devices);
  }

  [Fact]
  public async Task GetDefaultOutputDevice_ReturnsDeviceOrNotFound()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/output/default");

    // Assert - Either success with device or 404 if no default
    Assert.True(
      response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound,
      $"Expected success or NotFound, got {response.StatusCode}");
  }

  [Fact]
  public async Task SetOutputDevice_WithEmptyDeviceId_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetOutputDeviceRequest
    {
      DeviceId = ""
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/devices/output", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SetOutputDevice_WithUnknownDevice_ReturnsNotFound()
  {
    // Arrange
    var request = new SetOutputDeviceRequest
    {
      DeviceId = "unknown-device-id-that-does-not-exist"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/devices/output", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task RefreshDevices_ReturnsOk()
  {
    // Act
    var response = await _client.PostAsync("/api/devices/refresh", null);

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("refreshed", content, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task GetUSBReservations_ReturnsReservationInfo()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/usb/reservations");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    // Should contain common USB port paths
    Assert.Contains("/dev/ttyUSB", content);
  }

  [Fact]
  public async Task CheckUSBPort_WithPort_ReturnsStatus()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/usb/check?port=/dev/ttyUSB0");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("port", content);
    Assert.Contains("isInUse", content);
  }

  [Fact]
  public async Task CheckUSBPort_WithoutPort_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/usb/check");

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task GetDeviceDisplay_ReturnsDeviceList()
  {
    // Act
    var response = await _client.GetAsync("/api/devices/display");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var devices = await response.Content.ReadFromJsonAsync<List<DeviceDisplayInfoDto>>();
    Assert.NotNull(devices);
  }

  [Fact]
  public async Task SetDeviceVisibility_WithEmptyRawName_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetDeviceVisibilityRequest { RawName = "", Visible = false };

    // Act
    var response = await _client.PutAsJsonAsync("/api/devices/display/visibility", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SetDeviceFriendlyName_WithEmptyRawName_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetDeviceFriendlyNameRequest { RawName = "", FriendlyName = "Test" };

    // Act
    var response = await _client.PutAsJsonAsync("/api/devices/display/name", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task SetDeviceFriendlyName_WithEmptyFriendlyName_ReturnsBadRequest()
  {
    // Arrange
    var request = new SetDeviceFriendlyNameRequest { RawName = "TestDevice", FriendlyName = "" };

    // Act
    var response = await _client.PutAsJsonAsync("/api/devices/display/name", request);

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ClearDeviceFriendlyName_WithoutRawName_ReturnsBadRequest()
  {
    // Act
    var response = await _client.DeleteAsync("/api/devices/display/name");

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
  }
}
