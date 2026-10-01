using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
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
  private readonly Mock<IOptionsMonitor<RadioOptions>> _radioOptionsMock;
  private readonly Mock<IConfiguration> _configurationMock;
  private readonly RadioOptions _radioOptions;

  public RadioFactoryTests()
  {
    _loggerMock = new Mock<ILogger<RadioFactory>>();
    _loggerFactoryMock = new Mock<ILoggerFactory>();
    _configurationMock = new Mock<IConfiguration>();

    _radioOptions = new RadioOptions
    {
      DefaultDevice = "RTLSDRCore",
      DefaultDeviceVolume = 50
    };

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
  public void IsDeviceAvailable_ReturnsFalse_ForInvalidDeviceType()
  {
    // Arrange
    var factory = CreateFactory();

    // Act
    var isAvailable = factory.IsDeviceAvailable("InvalidDevice");

    // Assert
    Assert.False(isAvailable);
  }

  /// <summary>
  /// The RF320 USB radio was removed (AUD-16). Its old device-type string is now just another
  /// unknown type: never available, whatever the configuration says.
  /// </summary>
  [Fact]
  public void IsDeviceAvailable_ReturnsFalse_ForRemovedRf320DeviceType()
  {
    var factory = CreateFactory();

    Assert.False(factory.IsDeviceAvailable("RF320"));
  }

  #endregion

  #region CreateRadioSource Tests

  [Fact]
  public void CreateRadioSource_ThrowsException_WhenDeviceTypeInvalid()
  {
    // Arrange
    var factory = CreateFactory();

    // Act & Assert
    var exception = Assert.Throws<ArgumentException>(() =>
      factory.CreateRadioSource("InvalidDevice"));
    Assert.Contains("Unsupported radio device type", exception.Message);
  }

  /// <summary>
  /// A request for the removed RF320 is rejected exactly like any other unknown device type.
  /// </summary>
  [Fact]
  public void CreateRadioSource_RejectsRemovedRf320DeviceType_LikeAnyUnknownType()
  {
    var factory = CreateFactory();

    var exception = Assert.Throws<ArgumentException>(() => factory.CreateRadioSource("RF320"));
    Assert.Contains("Unsupported radio device type: RF320", exception.Message);
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
      _radioOptionsMock.Object,
      _configurationMock.Object);
  }

  #endregion
}
