using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Services;

public class AudioEngineInitializationServiceTests
{
  private readonly Mock<ILogger<AudioEngineInitializationService>> _loggerMock;
  private readonly Mock<IAudioEngine> _audioEngineMock;
  private readonly Mock<IAudioDeviceManager> _deviceManagerMock;
  private readonly Mock<IServiceProvider> _serviceProviderMock;
  private readonly Mock<IOptionsMonitor<AudioPreferences>> _audioPreferencesMock;
  private readonly Mock<IMasterMixer> _masterMixerMock;
  private readonly Mock<IOptions<BluetoothOptions>> _bluetoothOptionsMock;
  private readonly Mock<IOptions<AudioOutputOptions>> _audioOutputOptionsMock;

  public AudioEngineInitializationServiceTests()
  {
    _loggerMock = new Mock<ILogger<AudioEngineInitializationService>>();
    _audioEngineMock = new Mock<IAudioEngine>();
    _deviceManagerMock = new Mock<IAudioDeviceManager>();
    _serviceProviderMock = new Mock<IServiceProvider>();
    _audioPreferencesMock = new Mock<IOptionsMonitor<AudioPreferences>>();
    _masterMixerMock = new Mock<IMasterMixer>();
    _bluetoothOptionsMock = new Mock<IOptions<BluetoothOptions>>();
    _audioOutputOptionsMock = new Mock<IOptions<AudioOutputOptions>>();

    // Setup default audio preferences
    _audioPreferencesMock
      .Setup(x => x.CurrentValue)
      .Returns(new AudioPreferences
      {
        CurrentSource = "Radio",
        CurrentOutput = "",
        MasterVolume = 75
      });

    // Setup default Bluetooth options (disabled for tests)
    _bluetoothOptionsMock
      .Setup(x => x.Value)
      .Returns(new BluetoothOptions { Enabled = false, EnableOnStartup = false });

    // Setup default audio output options
    _audioOutputOptionsMock
      .Setup(x => x.Value)
      .Returns(new AudioOutputOptions());
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
      _audioPreferencesMock.Object,
      _masterMixerMock.Object,
      _bluetoothOptionsMock.Object,
      _audioOutputOptionsMock.Object,
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
  public async Task StartAsync_AppliesAudioPreferences()
  {
    // Arrange
    var audioManagerMock = new Mock<IAudioManager>();
    audioManagerMock
      .Setup(x => x.GetOrCreateSourceAsync(It.IsAny<AudioSourceType>(), true, It.IsAny<CancellationToken>()))
      .ReturnsAsync((IAudioSource?)null);

    var service = CreateService(audioManagerMock.Object);
    var cancellationToken = CancellationToken.None;

    var outputDevices = new List<AudioDeviceInfo>
    {
      new AudioDeviceInfo { Id = "output1", Name = "Speaker", Type = AudioDeviceType.Output, IsDefault = true }
    };

    _deviceManagerMock
      .Setup(x => x.GetOutputDevicesAsync(cancellationToken))
      .ReturnsAsync(outputDevices);

    _deviceManagerMock
      .Setup(x => x.GetInputDevicesAsync(cancellationToken))
      .ReturnsAsync(new List<AudioDeviceInfo>());

    // Act
    await service.StartAsync(cancellationToken);

    // Assert - Verify persisted source activation was attempted
    _loggerMock.Verify(
      x => x.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Activating persisted source")),
        null,
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);

    audioManagerMock.Verify(
      x => x.GetOrCreateSourceAsync(AudioSourceType.Radio, true, cancellationToken),
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
