using Microsoft.Extensions.Logging;
using Moq;
using Radio.API.Services;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Services;

public class AudioEngineInitializationServiceTests
{
  private readonly Mock<ILogger<AudioEngineInitializationService>> _loggerMock;
  private readonly Mock<IAudioEngine> _audioEngineMock;
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock;
  private readonly Mock<IServiceProvider> _serviceProviderMock;

  public AudioEngineInitializationServiceTests()
  {
    _loggerMock = new Mock<ILogger<AudioEngineInitializationService>>();
    _audioEngineMock = new Mock<IAudioEngine>();
    _deviceManagerMock = new Mock<IAudioDeviceManager>();
    _serviceProviderMock = new Mock<IServiceProvider>();
  }

  private AudioEngineInitializationService CreateService(IAudioManager? audioManager = null)
  {
    _serviceProviderMock
      .Setup(x => x.GetService(typeof(IAudioManager)))
      .Returns(audioManager);

    return new AudioEngineInitializationService(
      _loggerMock.Object,
      _audioEngineMock.Object,
      _deviceManagerMock.Object,
      _serviceProviderMock.Object);
  }

  [Fact]
  public async Task StartAsync_InitializesAndStartsAudioEngine()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    _deviceManagerMock
      .Setup(x => x.GetInputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    // Act
    await service.StartAsync(cancellationToken);

    // Assert
    _audioEngineMock.Verify(x => x.InitializeAsync(cancellationToken), Times.Once);
    _audioEngineMock.Verify(x => x.StartAsync(cancellationToken), Times.Once);
  }

  [Fact]
  public async Task StartAsync_EnumeratesDevices()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    var outputDevices = new List<AudioDeviceInfo>
    {
      new AudioDeviceInfo { Id = "output1", Name = "Speaker", Type = AudioDeviceType.Output, IsDefault = true }
    };

    var inputDevices = new List<AudioDeviceInfo>
    {
      new AudioDeviceInfo { Id = "input1", Name = "Microphone", Type = AudioDeviceType.Input, IsDefault = false }
    };

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(cancellationToken))
      .ReturnsAsync(outputDevices);

    _deviceManagerMock
      .Setup(x => x.GetInputDevicesAsync(cancellationToken))
      .ReturnsAsync(inputDevices);

    // Act
    await service.StartAsync(cancellationToken);

    // Assert
    _deviceManagerMock.Verify(x => x.GetOutputDevicesAsync(cancellationToken), Times.Once);
    _deviceManagerMock.Verify(x => x.GetInputDevicesAsync(cancellationToken), Times.Once);
  }

  [Fact]
  public async Task StartAsync_LogsDeviceDetails()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    var outputDevices = new List<AudioDeviceInfo>
    {
      new AudioDeviceInfo { Id = "output1", Name = "Speaker", Type = AudioDeviceType.Output, IsDefault = true },
      new AudioDeviceInfo { Id = "output2", Name = "Headphones", Type = AudioDeviceType.Output, IsDefault = false }
    };

    var inputDevices = new List<AudioDeviceInfo>
    {
      new AudioDeviceInfo { Id = "input1", Name = "Microphone", Type = AudioDeviceType.Input, IsDefault = false }
    };

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(cancellationToken))
      .ReturnsAsync(outputDevices);

    _deviceManagerMock
      .Setup(x => x.GetInputDevicesAsync(cancellationToken))
      .ReturnsAsync(inputDevices);

    // Act
    await service.StartAsync(cancellationToken);

    // Assert - Verify logging occurred
    _loggerMock.Verify(
      x => x.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Found 2 output devices and 1 input devices")),
        null,
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_DoesNotThrowOnAudioEngineFailure()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    _audioEngineMock
      .Setup(x => x.InitializeAsync(cancellationToken))
      .ThrowsAsync(new InvalidOperationException("Audio engine failed"));

    // Act
    var exception = await Record.ExceptionAsync(async () => await service.StartAsync(cancellationToken));

    // Assert
    Assert.Null(exception);
    _loggerMock.Verify(
      x => x.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to initialize audio engine")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_WithAudioManager_LogsAvailability()
  {
    // Arrange
    var audioManagerMock = new Mock<IAudioManager>();
    var service = CreateService(audioManagerMock.Object);
    var cancellationToken = CancellationToken.None;

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    _deviceManagerMock
      .Setup(x => x.GetInputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    // Act
    await service.StartAsync(cancellationToken);

    // Assert
    _loggerMock.Verify(
      x => x.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Audio manager available")),
        null,
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
  }

  [Fact]
  public async Task StopAsync_StopsAudioEngineWhenRunning()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    _audioEngineMock
      .Setup(x => x.State)
      .Returns(Radio.Core.Interfaces.Audio.AudioEngineState.Running);

    // Act
    await service.StopAsync(cancellationToken);

    // Assert
    _audioEngineMock.Verify(x => x.StopAsync(cancellationToken), Times.Once);
  }

  [Fact]
  public async Task StopAsync_DoesNotStopAudioEngineWhenNotRunning()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    _audioEngineMock
      .Setup(x => x.State)
      .Returns(Radio.Core.Interfaces.Audio.AudioEngineState.Ready);

    // Act
    await service.StopAsync(cancellationToken);

    // Assert
    _audioEngineMock.Verify(x => x.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task StopAsync_DoesNotThrowOnError()
  {
    // Arrange
    var service = CreateService();
    var cancellationToken = CancellationToken.None;

    _audioEngineMock
      .Setup(x => x.State)
      .Returns(Radio.Core.Interfaces.Audio.AudioEngineState.Running);

    _audioEngineMock
      .Setup(x => x.StopAsync(cancellationToken))
      .ThrowsAsync(new InvalidOperationException("Stop failed"));

    // Act
    var exception = await Record.ExceptionAsync(async () => await service.StopAsync(cancellationToken));

    // Assert
    Assert.Null(exception);
    _loggerMock.Verify(
      x => x.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error stopping audio engine")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_CancellationToken_IsPassedToMethods()
  {
    // Arrange
    var service = CreateService();
    using var cts = new CancellationTokenSource();
    var cancellationToken = cts.Token;

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    _deviceManagerMock
      .Setup(x => x.GetInputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    // Act
    await service.StartAsync(cancellationToken);

    // Assert
    _audioEngineMock.Verify(x => x.InitializeAsync(cancellationToken), Times.Once);
    _audioEngineMock.Verify(x => x.StartAsync(cancellationToken), Times.Once);
    _deviceManagerMock.Verify(x => x.GetOutputDevicesAsync(cancellationToken), Times.Once);
    _deviceManagerMock.Verify(x => x.GetInputDevicesAsync(cancellationToken), Times.Once);
  }
}
