using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Radio.API.Health;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Health;

public class BluetoothHealthCheckTests
{
  private readonly Mock<IBluetoothService> _mockBtService = new();

  private BluetoothHealthCheck CreateSut() => new(_mockBtService.Object);

  [Fact]
  public async Task Returns_Healthy_When_Pipeline_Healthy()
  {
    _mockBtService.Setup(s => s.PipelineStatus).Returns(BluetoothPipelineStatus.Healthy);
    _mockBtService.Setup(s => s.ConnectedDevice).Returns(
      new BluetoothDeviceInfo { Name = "Pixel 8 Pro", Address = "D4:3A:2C:64:87:9E", IsConnected = true });

    var result = await CreateSut().CheckHealthAsync(
      new HealthCheckContext { Registration = new HealthCheckRegistration("bt", CreateSut(), null, null) });

    Assert.Equal(HealthStatus.Healthy, result.Status);
  }

  [Fact]
  public async Task Returns_Degraded_When_No_Device_Connected()
  {
    _mockBtService.Setup(s => s.PipelineStatus).Returns(BluetoothPipelineStatus.Degraded);
    _mockBtService.Setup(s => s.ConnectedDevice).Returns((BluetoothDeviceInfo?)null);

    var result = await CreateSut().CheckHealthAsync(
      new HealthCheckContext { Registration = new HealthCheckRegistration("bt", CreateSut(), null, null) });

    Assert.Equal(HealthStatus.Degraded, result.Status);
  }

  [Fact]
  public async Task Returns_Unhealthy_When_Pipeline_Broken()
  {
    _mockBtService.Setup(s => s.PipelineStatus).Returns(BluetoothPipelineStatus.Broken);
    _mockBtService.Setup(s => s.ConnectedDevice).Returns(
      new BluetoothDeviceInfo { Name = "Pixel 8 Pro", Address = "D4:3A:2C:64:87:9E", IsConnected = true });

    var result = await CreateSut().CheckHealthAsync(
      new HealthCheckContext { Registration = new HealthCheckRegistration("bt", CreateSut(), null, null) });

    Assert.Equal(HealthStatus.Unhealthy, result.Status);
  }

  [Fact]
  public async Task Returns_Healthy_When_Inactive()
  {
    _mockBtService.Setup(s => s.PipelineStatus).Returns(BluetoothPipelineStatus.Inactive);

    var result = await CreateSut().CheckHealthAsync(
      new HealthCheckContext { Registration = new HealthCheckRegistration("bt", CreateSut(), null, null) });

    Assert.Equal(HealthStatus.Healthy, result.Status);
  }

  [Fact]
  public async Task Reports_Connected_Device_In_Data()
  {
    _mockBtService.Setup(s => s.PipelineStatus).Returns(BluetoothPipelineStatus.Healthy);
    _mockBtService.Setup(s => s.ConnectedDevice).Returns(
      new BluetoothDeviceInfo { Name = "Pixel 8 Pro", Address = "D4:3A:2C:64:87:9E", IsConnected = true });

    var result = await CreateSut().CheckHealthAsync(
      new HealthCheckContext { Registration = new HealthCheckRegistration("bt", CreateSut(), null, null) });

    Assert.Equal("Pixel 8 Pro (D4:3A:2C:64:87:9E)", result.Data["connectedDevice"]);
  }
}
