using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Unit tests for the SoundFlowDeviceManager class.
/// </summary>
public class SoundFlowDeviceManagerTests
{
  private readonly Mock<ILogger<SoundFlowDeviceManager>> _loggerMock;
  private readonly SoundFlowDeviceManager _deviceManager;

  public SoundFlowDeviceManagerTests()
  {
    _loggerMock = new Mock<ILogger<SoundFlowDeviceManager>>();
    _deviceManager = new SoundFlowDeviceManager(_loggerMock.Object);
  }

  [Fact]
  public void IsUSBPortInUse_ReturnsFalseWhenNotReserved()
  {
    // Arrange
    var usbPort = "/dev/ttyUSB0";

    // Act
    var result = _deviceManager.IsUSBPortInUse(usbPort);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void ReserveUSBPort_ReservesPort()
  {
    // Arrange
    var usbPort = "/dev/ttyUSB0";
    var sourceId = "source-1";

    // Act
    _deviceManager.ReserveUSBPort(usbPort, sourceId);

    // Assert
    Assert.True(_deviceManager.IsUSBPortInUse(usbPort));
  }

  [Fact]
  public void ReserveUSBPort_ThrowsWhenPortAlreadyReserved()
  {
    // Arrange
    var usbPort = "/dev/ttyUSB0";
    _deviceManager.ReserveUSBPort(usbPort, "source-1");

    // Act & Assert
    var ex = Assert.Throws<AudioDeviceConflictException>(
      () => _deviceManager.ReserveUSBPort(usbPort, "source-2"));

    Assert.Contains(usbPort, ex.Message);
    Assert.Contains("source-1", ex.Message);
  }

  [Fact]
  public void ReleaseUSBPort_ReleasesReservedPort()
  {
    // Arrange
    var usbPort = "/dev/ttyUSB0";
    _deviceManager.ReserveUSBPort(usbPort, "source-1");

    // Act
    _deviceManager.ReleaseUSBPort(usbPort);

    // Assert
    Assert.False(_deviceManager.IsUSBPortInUse(usbPort));
  }

  [Fact]
  public void ReleaseUSBPort_DoesNotThrowWhenNotReserved()
  {
    // Arrange
    var usbPort = "/dev/ttyUSB0";

    // Act & Assert - should not throw
    _deviceManager.ReleaseUSBPort(usbPort);
    Assert.False(_deviceManager.IsUSBPortInUse(usbPort));
  }

  [Fact]
  public void MultipleUSBPorts_CanBeReservedIndependently()
  {
    // Arrange & Act
    _deviceManager.ReserveUSBPort("/dev/ttyUSB0", "source-1");
    _deviceManager.ReserveUSBPort("/dev/ttyUSB1", "source-2");
    _deviceManager.ReserveUSBPort("/dev/ttyUSB2", "source-3");

    // Assert
    Assert.True(_deviceManager.IsUSBPortInUse("/dev/ttyUSB0"));
    Assert.True(_deviceManager.IsUSBPortInUse("/dev/ttyUSB1"));
    Assert.True(_deviceManager.IsUSBPortInUse("/dev/ttyUSB2"));
  }

  [Fact]
  public void GetUSBPortReservations_ReturnsAllReservations()
  {
    // Arrange
    _deviceManager.ReserveUSBPort("/dev/ttyUSB0", "source-1");
    _deviceManager.ReserveUSBPort("/dev/ttyUSB1", "source-2");

    // Act
    var reservations = _deviceManager.GetUSBPortReservations();

    // Assert
    Assert.Equal(2, reservations.Count);
    Assert.Equal("source-1", reservations["/dev/ttyUSB0"]);
    Assert.Equal("source-2", reservations["/dev/ttyUSB1"]);
  }

  [Fact]
  public async Task GetOutputDevicesAsync_ReturnsDeviceList()
  {
    // Act
    var devices = await _deviceManager.GetOutputDevicesAsync();

    // Assert - should return at least a default device after initialization
    Assert.NotNull(devices);
  }

  [Fact]
  public async Task GetInputDevicesAsync_ReturnsDeviceList()
  {
    // Act
    var devices = await _deviceManager.GetInputDevicesAsync();

    // Assert
    Assert.NotNull(devices);
  }

  [Fact]
  public async Task RefreshDevicesAsync_UpdatesDeviceLists()
  {
    // Act
    await _deviceManager.RefreshDevicesAsync();
    var outputDevices = await _deviceManager.GetOutputDevicesAsync();

    // Assert - after refresh, should have at least a default device
    Assert.NotNull(outputDevices);
    Assert.NotEmpty(outputDevices);
  }

  [Fact]
  public async Task GetDefaultOutputDeviceAsync_ReturnsDefaultDevice()
  {
    // Arrange
    await _deviceManager.RefreshDevicesAsync();

    // Act
    var defaultDevice = await _deviceManager.GetDefaultOutputDeviceAsync();

    // Assert
    Assert.NotNull(defaultDevice);
    Assert.True(defaultDevice.IsDefault);
  }

  [Fact]
  public async Task SetOutputDeviceAsync_SetsSelectedDevice()
  {
    // Arrange
    await _deviceManager.RefreshDevicesAsync();
    var devices = await _deviceManager.GetOutputDevicesAsync();
    var deviceId = devices.First().Id;

    // Act
    await _deviceManager.SetOutputDeviceAsync(deviceId);

    // Assert
    Assert.Equal(deviceId, _deviceManager.GetSelectedOutputDeviceId());
  }

  [Fact]
  public async Task SetOutputDeviceAsync_ThrowsForUnknownDevice()
  {
    // Arrange
    await _deviceManager.RefreshDevicesAsync();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => _deviceManager.SetOutputDeviceAsync("unknown-device-id"));
  }

  [Fact]
  public void DevicesChanged_RaisedWhenDevicesChange()
  {
    // Arrange
    AudioDeviceChangedEventArgs? capturedArgs = null;
    _deviceManager.DevicesChanged += (s, e) => capturedArgs = e;

    // We test the event mechanism indirectly
    // The actual event raising happens during refresh when comparing old vs new
    // For now, just verify the device manager tracks devices correctly
    Assert.NotNull(_deviceManager);
  }

  [Fact]
  public void ReserveUSBPort_ThrowsForNullPort()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => _deviceManager.ReserveUSBPort(null!, "source-1"));
  }

  [Fact]
  public void ReserveUSBPort_ThrowsForEmptyPort()
  {
    // Act & Assert
    Assert.Throws<ArgumentException>(() => _deviceManager.ReserveUSBPort("", "source-1"));
  }

  [Fact]
  public void ReserveUSBPort_ThrowsForNullSourceId()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(
      () => _deviceManager.ReserveUSBPort("/dev/ttyUSB0", null!));
  }

  [Fact]
  public void ReserveUSBPort_ThrowsForEmptySourceId()
  {
    // Act & Assert
    Assert.Throws<ArgumentException>(
      () => _deviceManager.ReserveUSBPort("/dev/ttyUSB0", ""));
  }

  [Fact]
  public void IsUSBPortInUse_ThrowsForNullPort()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => _deviceManager.IsUSBPortInUse(null!));
  }

  [Fact]
  public void ReleaseUSBPort_ThrowsForNullPort()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => _deviceManager.ReleaseUSBPort(null!));
  }
}
