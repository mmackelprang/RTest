using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class BluetoothAutoSwitchServiceTests : IDisposable
{
  private readonly Mock<ILogger<BluetoothAutoSwitchService>> _loggerMock;
  private readonly Mock<IBluetoothService> _bluetoothServiceMock;
  private readonly Mock<IOptionsMonitor<BluetoothOptions>> _bluetoothOptionsMock;
  private readonly Mock<IAudioManager> _audioManagerMock;
  private readonly BluetoothOptions _options;
  private readonly BluetoothAutoSwitchService _sut;

  public BluetoothAutoSwitchServiceTests()
  {
    _loggerMock = new Mock<ILogger<BluetoothAutoSwitchService>>();
    _bluetoothServiceMock = new Mock<IBluetoothService>();
    _bluetoothOptionsMock = new Mock<IOptionsMonitor<BluetoothOptions>>();
    _audioManagerMock = new Mock<IAudioManager>();

    _options = new BluetoothOptions
    {
      EnableOnStartup = true,
      AutoSwitchOnConnect = true
    };
    _bluetoothOptionsMock.Setup(o => o.CurrentValue).Returns(_options);
    _bluetoothServiceMock.Setup(s => s.IsAvailable).Returns(true);

    _sut = new BluetoothAutoSwitchService(
      _loggerMock.Object,
      _bluetoothServiceMock.Object,
      _bluetoothOptionsMock.Object,
      () => _audioManagerMock.Object);
  }

  public void Dispose()
  {
    _sut.Dispose();
  }

  [Fact]
  public async Task PreWarmBluetoothAsync_CreatesSourceWithoutSwitch_WhenEnabled()
  {
    // Act
    await _sut.PreWarmBluetoothAsync();

    // Assert
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      AudioSourceType.Bluetooth,
      false,
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task PreWarmBluetoothAsync_DoesNothing_WhenDisabled()
  {
    // Arrange
    _options.EnableOnStartup = false;

    // Act
    await _sut.PreWarmBluetoothAsync();

    // Assert
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task OnBluetoothDeviceConnected_SwitchesToBluetooth_WhenAutoSwitchEnabled()
  {
    // Arrange
    _audioManagerMock.Setup(m => m.ActiveSource).Returns((IAudioSource?)null);

    // Act — raise the event
    _bluetoothServiceMock.Raise(s => s.DeviceConnected += null,
      _bluetoothServiceMock.Object,
      new BluetoothDeviceConnectedEventArgs
      {
        Device = new BluetoothDeviceInfo
        {
          Address = "AA:BB:CC:DD:EE:FF",
          Name = "Test Phone"
        }
      });

    // Allow async event handler to complete
    await Task.Delay(100);

    // Assert
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      AudioSourceType.Bluetooth,
      true,
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task OnBluetoothDeviceConnected_Skips_WhenAutoSwitchDisabled()
  {
    // Arrange
    _options.AutoSwitchOnConnect = false;

    // Act
    _bluetoothServiceMock.Raise(s => s.DeviceConnected += null,
      _bluetoothServiceMock.Object,
      new BluetoothDeviceConnectedEventArgs
      {
        Device = new BluetoothDeviceInfo
        {
          Address = "AA:BB:CC:DD:EE:FF",
          Name = "Test Phone"
        }
      });

    await Task.Delay(100);

    // Assert
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task OnBluetoothDeviceConnected_Skips_WhenAdapterUnavailable()
  {
    // Arrange
    _bluetoothServiceMock.Setup(s => s.IsAvailable).Returns(false);

    // Act
    _bluetoothServiceMock.Raise(s => s.DeviceConnected += null,
      _bluetoothServiceMock.Object,
      new BluetoothDeviceConnectedEventArgs
      {
        Device = new BluetoothDeviceInfo
        {
          Address = "AA:BB:CC:DD:EE:FF",
          Name = "Test Phone"
        }
      });

    await Task.Delay(100);

    // Assert
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task OnBluetoothDeviceConnected_Skips_WhenAlreadyOnBluetooth()
  {
    // Arrange
    var btSourceMock = new Mock<IAudioSource>();
    btSourceMock.Setup(s => s.Type).Returns(AudioSourceType.Bluetooth);
    _audioManagerMock.Setup(m => m.ActiveSource).Returns(btSourceMock.Object);

    // Act
    _bluetoothServiceMock.Raise(s => s.DeviceConnected += null,
      _bluetoothServiceMock.Object,
      new BluetoothDeviceConnectedEventArgs
      {
        Device = new BluetoothDeviceInfo
        {
          Address = "AA:BB:CC:DD:EE:FF",
          Name = "Test Phone"
        }
      });

    await Task.Delay(100);

    // Assert
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public void Dispose_UnsubscribesFromEvent()
  {
    // Act
    _sut.Dispose();

    // Raise event after dispose — should not trigger any action
    _bluetoothServiceMock.Raise(s => s.DeviceConnected += null,
      _bluetoothServiceMock.Object,
      new BluetoothDeviceConnectedEventArgs
      {
        Device = new BluetoothDeviceInfo
        {
          Address = "AA:BB:CC:DD:EE:FF",
          Name = "Test Phone"
        }
      });

    // Assert — no calls since handler was unsubscribed
    _audioManagerMock.Verify(m => m.GetOrCreateSourceAsync(
      It.IsAny<AudioSourceType>(),
      It.IsAny<bool>(),
      It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public void Dispose_IsSafeToCallTwice()
  {
    _sut.Dispose();
    _sut.Dispose(); // Should not throw
  }
}
