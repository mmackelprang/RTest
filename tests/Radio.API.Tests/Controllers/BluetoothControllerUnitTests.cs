using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Controllers;

public class BluetoothControllerUnitTests
{
  private readonly Mock<IBluetoothService> _serviceMock = new();
  private readonly Mock<ILogger<BluetoothController>> _loggerMock = new();

  private BluetoothController CreateController()
  {
    return new BluetoothController(_loggerMock.Object, _serviceMock.Object);
  }

  [Fact]
  public void GetStatus_ReturnsMappedStatus()
  {
    var device = new BluetoothDeviceInfo
    {
      Address = "00:11:22:33:44:55",
      Name = "Phone",
      IsPaired = true,
      IsConnected = true,
      LastConnected = DateTime.UtcNow
    };

    _serviceMock.SetupGet(s => s.IsAvailable).Returns(true);
    _serviceMock.SetupGet(s => s.State).Returns(BluetoothAdapterState.On);
    _serviceMock.SetupGet(s => s.ConnectedDevice).Returns(device);
    _serviceMock.SetupGet(s => s.PairedDevices).Returns(new List<BluetoothDeviceInfo> { device });

    var controller = CreateController();

    var result = controller.GetStatus();

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var dto = Assert.IsType<BluetoothStatusDto>(ok.Value);
    Assert.True(dto.IsAvailable);
    Assert.Equal(BluetoothAdapterState.On.ToString(), dto.State);
    Assert.NotNull(dto.ConnectedDevice);
    Assert.Single(dto.PairedDevices);
  }

  [Fact]
  public async Task Start_WhenUnavailable_ReturnsBadRequest()
  {
    _serviceMock.SetupGet(s => s.IsAvailable).Returns(false);

    var controller = CreateController();
    var response = await controller.StartAsync(new BluetoothStartRequest());

    Assert.IsType<BadRequestObjectResult>(response.Result);
  }

  [Fact]
  public async Task Start_WhenAvailable_Success()
  {
    _serviceMock.SetupGet(s => s.IsAvailable).Returns(true);
    _serviceMock.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    _serviceMock.SetupGet(s => s.State).Returns(BluetoothAdapterState.On);
    _serviceMock.SetupGet(s => s.PairedDevices).Returns(Array.Empty<BluetoothDeviceInfo>());

    var controller = CreateController();
    var response = await controller.StartAsync(new BluetoothStartRequest { DeviceName = "Test" });

    Assert.IsType<OkObjectResult>(response.Result);
  }

  [Fact]
  public async Task Start_WhenServiceFails_Returns500()
  {
    _serviceMock.SetupGet(s => s.IsAvailable).Returns(true);
    _serviceMock.Setup(s => s.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

    var controller = CreateController();
    var response = await controller.StartAsync(new BluetoothStartRequest { DeviceName = "Test" });

    var result = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(500, result.StatusCode);
  }

  [Fact]
  public async Task Pair_WithMissingAddress_ReturnsBadRequest()
  {
    var controller = CreateController();
    var response = await controller.PairAsync(new BluetoothDeviceRequest { DeviceAddress = "" });
    Assert.IsType<BadRequestObjectResult>(response.Result);
  }

  [Fact]
  public async Task Pair_WhenServiceFails_Returns500()
  {
    _serviceMock.Setup(s => s.PairDeviceAsync("addr", It.IsAny<CancellationToken>())).ReturnsAsync(false);
    var controller = CreateController();
    var response = await controller.PairAsync(new BluetoothDeviceRequest { DeviceAddress = "addr" });
    var result = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(500, result.StatusCode);
  }

  [Fact]
  public async Task Pair_WhenServiceSucceeds_ReturnsOk()
  {
    _serviceMock.Setup(s => s.PairDeviceAsync("addr", It.IsAny<CancellationToken>())).ReturnsAsync(true);
    _serviceMock.SetupGet(s => s.State).Returns(BluetoothAdapterState.On);
    _serviceMock.SetupGet(s => s.PairedDevices).Returns(Array.Empty<BluetoothDeviceInfo>());

    var controller = CreateController();
    var response = await controller.PairAsync(new BluetoothDeviceRequest { DeviceAddress = "addr" });
    Assert.IsType<OkObjectResult>(response.Result);
  }

  [Fact]
  public async Task Unpair_WhenServiceFails_Returns500()
  {
    _serviceMock.Setup(s => s.UnpairDeviceAsync("addr", It.IsAny<CancellationToken>())).ReturnsAsync(false);
    var controller = CreateController();
    var response = await controller.UnpairAsync(new BluetoothDeviceRequest { DeviceAddress = "addr" });
    var result = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(500, result.StatusCode);
  }

  [Fact]
  public async Task Accept_WhenServiceFails_Returns500()
  {
    _serviceMock.Setup(s => s.AcceptConnectionAsync("addr", It.IsAny<CancellationToken>())).ReturnsAsync(false);
    var controller = CreateController();
    var response = await controller.AcceptAsync(new BluetoothDeviceRequest { DeviceAddress = "addr" });
    var result = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(500, result.StatusCode);
  }

  [Fact]
  public async Task Disconnect_CallsService_ReturnsOk()
  {
    _serviceMock.Setup(s => s.DisconnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    var controller = CreateController();
    var response = await controller.DisconnectAsync();

    Assert.IsType<OkObjectResult>(response.Result);
    _serviceMock.Verify(s => s.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task Stop_CallsService_ReturnsOk()
  {
    _serviceMock.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    _serviceMock.SetupGet(s => s.State).Returns(BluetoothAdapterState.Off);
    _serviceMock.SetupGet(s => s.PairedDevices).Returns(Array.Empty<BluetoothDeviceInfo>());

    var controller = CreateController();
    var response = await controller.StopAsync();

    Assert.IsType<OkObjectResult>(response.Result);
    _serviceMock.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task DiscoveryStart_ReturnsAccepted()
  {
    _serviceMock.Setup(s => s.StartDiscoveryAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    var controller = CreateController();
    var response = await controller.StartDiscoveryAsync();

    Assert.IsType<AcceptedResult>(response);
  }

  [Fact]
  public async Task DiscoveryStop_ReturnsAccepted()
  {
    _serviceMock.Setup(s => s.StopDiscoveryAsync()).Returns(Task.CompletedTask);

    var controller = CreateController();
    var response = await controller.StopDiscoveryAsync();

    Assert.IsType<AcceptedResult>(response);
  }

  [Fact]
  public void GetStatus_WhenPairedDevicesNull_ReturnsEmptyList()
  {
    _serviceMock.SetupGet(s => s.PairedDevices).Returns((IReadOnlyList<BluetoothDeviceInfo>?)null!);

    var controller = CreateController();
    var result = controller.GetStatus();

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var dto = Assert.IsType<BluetoothStatusDto>(ok.Value);
    Assert.Empty(dto.PairedDevices);
  }
}
