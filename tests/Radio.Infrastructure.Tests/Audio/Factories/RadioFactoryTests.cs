using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Factories;

namespace Radio.Infrastructure.Tests.Audio.Factories;

/// <summary>
/// Unit tests for RadioFactory.
/// Tests device creation, availability checking, and default device selection.
/// </summary>
public class RadioFactoryTests
{
  private readonly Mock<ILogger<RadioFactory>> _loggerMock;
  private readonly Mock<ILoggerFactory> _loggerFactoryMock;
  private readonly Mock<IOptionsMonitor<DeviceOptions>> _deviceOptionsMock;
  private readonly Mock<IOptionsMonitor<RadioOptions>> _radioOptionsMock;
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock;
  private readonly Mock<IConfiguration> _configurationMock;
  private readonly DeviceOptions _deviceOptions;
  private readonly RadioOptions _radioOptions;

  public RadioFactoryTests()
  {
    _loggerMock = new Mock<ILogger<RadioFactory>>();
    _loggerFactoryMock = new Mock<ILoggerFactory>();
    _deviceManagerMock = new Mock<IAudioDeviceManager>();
    _configurationMock = new Mock<IConfiguration>();

    _deviceOptions = new DeviceOptions
    {
      Radio = new RadioDeviceOptions { USBPort = "/dev/ttyUSB0" },
      Vinyl = new VinylDeviceOptions { USBPort = "/dev/ttyUSB1" }
    };

    _radioOptions = new RadioOptions
    {
      DefaultDevice = "RTLSDRCore",
      DefaultDeviceVolume = 50
    };

    _deviceOptionsMock = new Mock<IOptionsMonitor<DeviceOptions>>();
    _deviceOptionsMock.Setup(o => o.CurrentValue).Returns(_deviceOptions);

    _radioOptionsMock = new Mock<IOptionsMonitor<RadioOptions>>();
    _radioOptionsMock.Setup(o => o.CurrentValue).Returns(_radioOptions);

    // Setup logger factory to return mock loggers
    _loggerFactoryMock
      .Setup(f => f.CreateLogger(It.IsAny<string>()))
      .Returns(new Mock<ILogger>().Object);
  }

  #region Constructor Tests

  [Fact]
  public void Constructor_InitializesCorrectly()
  {
    // Act
    var factory = CreateFactory();

    // Assert
    Assert.NotNull(factory);
  }

  #endregion

  #region IsDeviceAvailable Tests

  [Fact]
  public void IsDeviceAvailable_ReturnsTrue_ForRF320()
  {
    // Arrange
    var factory = CreateFactory();
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse(It.IsAny<string>())).Returns(false);

    // Act
    var isAvailable = factory.IsDeviceAvailable(RadioFactory.DeviceTypes.RF320);

    // Assert
    Assert.True(isAvailable);
  }

  [Fact]
  public void IsDeviceAvailable_ReturnsFalse_WhenUSBPortInUse()
  {
    // Arrange
    var factory = CreateFactory();
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse("/dev/ttyUSB0")).Returns(true);

    // Act
    var isAvailable = factory.IsDeviceAvailable(RadioFactory.DeviceTypes.RF320);

    // Assert
    Assert.False(isAvailable);
  }

  [Fact]
  public void IsDeviceAvailable_ReturnsFalse_ForInvalidDeviceType()
  {
    // Arrange
    var factory = CreateFactory();

    // Act
    var isAvailable = factory.IsDeviceAvailable("InvalidDevice");

    // Assert
    Assert.False(isAvailable);
  }

  #endregion

  #region CreateRadioSource Tests

  [Fact]
  public void CreateRadioSource_CreatesRF320Source_WhenRequested()
  {
    // Arrange
    var factory = CreateFactory();
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse(It.IsAny<string>())).Returns(false);

    // Act
    var source = factory.CreateRadioSource(RadioFactory.DeviceTypes.RF320);

    // Assert
    Assert.NotNull(source);
    Assert.Equal("Radio (RF320)", source.Name);
  }

  [Fact]
  public void CreateRadioSource_ThrowsException_WhenDeviceTypeInvalid()
  {
    // Arrange
    var factory = CreateFactory();

    // Act & Assert
    var exception = Assert.Throws<ArgumentException>(() =>
      factory.CreateRadioSource("InvalidDevice"));
    Assert.Contains("Unknown device type", exception.Message);
  }

  [Fact]
  public void CreateRadioSource_ThrowsException_WhenDeviceTypeNull()
  {
    // Arrange
    var factory = CreateFactory();

    // Act & Assert
    var exception = Assert.Throws<ArgumentException>(() =>
      factory.CreateRadioSource(null!));
    Assert.Contains("Device type cannot be null or empty", exception.Message);
  }

  #endregion

  #region Helper Methods

  private RadioFactory CreateFactory()
  {
    return new RadioFactory(
      _loggerMock.Object,
      _loggerFactoryMock.Object,
      _deviceOptionsMock.Object,
      _radioOptionsMock.Object,
      _deviceManagerMock.Object,
      _configurationMock.Object);
  }

  #endregion
}
