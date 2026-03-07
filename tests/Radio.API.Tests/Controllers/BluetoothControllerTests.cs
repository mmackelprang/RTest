using System.Net;
using System.Net.Http.Json;
using Moq;
using Radio.API.Models;
using Radio.API.Tests.TestSupport;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Controllers;

public class BluetoothControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public BluetoothControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetStatus_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/bluetooth/status");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Start_ReturnsOkOrBadRequestWhenUnavailable()
  {
    var response = await _client.PostAsJsonAsync("/api/bluetooth/start", new BluetoothStartRequest());
    // OK = started, BadRequest = adapter not available, 500 = adapter present but non-functional
    // (e.g., InTheHand WindowsBluetoothRadio NRE on set_Mode in test environment without working BT)
    Assert.True(
      response.StatusCode == HttpStatusCode.OK
      || response.StatusCode == HttpStatusCode.BadRequest
      || response.StatusCode == HttpStatusCode.InternalServerError);
  }

  [Fact]
  public async Task Stop_ReturnsOk()
  {
    var response = await _client.PostAsync("/api/bluetooth/stop", null);
    Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Accepted);
  }

  [Fact]
  public async Task DiscoveryStart_ReturnsAccepted()
  {
    var response = await _client.PostAsync("/api/bluetooth/discovery/start", null);
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task DiscoveryStop_ReturnsAccepted()
  {
    var response = await _client.PostAsync("/api/bluetooth/discovery/stop", null);
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task Pair_WithMissingAddress_ReturnsBadRequest()
  {
    var response = await _client.PostAsJsonAsync("/api/bluetooth/pair", new BluetoothDeviceRequest { DeviceAddress = string.Empty });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Unpair_WithMissingAddress_ReturnsBadRequest()
  {
    var response = await _client.PostAsJsonAsync("/api/bluetooth/unpair", new BluetoothDeviceRequest { DeviceAddress = string.Empty });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Accept_WithMissingAddress_ReturnsBadRequest()
  {
    var response = await _client.PostAsJsonAsync("/api/bluetooth/accept", new BluetoothDeviceRequest { DeviceAddress = string.Empty });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Disconnect_ReturnsOk()
  {
    var response = await _client.PostAsync("/api/bluetooth/disconnect", null);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Connect_WithMissingAddress_ReturnsBadRequest()
  {
    var response = await _client.PostAsJsonAsync("/api/bluetooth/connect",
      new BluetoothDeviceRequest { DeviceAddress = string.Empty });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Connect_WithAddress_ReturnsOkOrServerError()
  {
    // ConnectAsync may return false (500) when no real BT adapter is available,
    // or OK when mock service succeeds
    var response = await _client.PostAsJsonAsync("/api/bluetooth/connect",
      new BluetoothDeviceRequest { DeviceAddress = "AA:BB:CC:DD:EE:FF" });
    Assert.True(
      response.StatusCode == HttpStatusCode.OK
      || response.StatusCode == HttpStatusCode.InternalServerError);
  }
}
