using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class AudioPreferencePersistenceTests : IDisposable
{
  private readonly Mock<ILogger<AudioPreferencePersistence>> _loggerMock;
  private readonly Mock<IAudioEngine> _audioEngineMock;
  private readonly Mock<IOptionsMonitor<AudioPreferences>> _audioPreferencesMock;
  private readonly Mock<IConfigurationManager> _configManagerMock;
  private readonly Mock<IMasterMixer> _mixerMock;
  private readonly Mock<IConfigurationStore> _storeMock;
  private readonly AudioPreferencePersistence _sut;

  public AudioPreferencePersistenceTests()
  {
    _loggerMock = new Mock<ILogger<AudioPreferencePersistence>>();
    _audioEngineMock = new Mock<IAudioEngine>();
    _audioPreferencesMock = new Mock<IOptionsMonitor<AudioPreferences>>();
    _configManagerMock = new Mock<IConfigurationManager>();
    _mixerMock = new Mock<IMasterMixer>();
    _storeMock = new Mock<IConfigurationStore>();

    _audioEngineMock.Setup(e => e.GetMasterMixer()).Returns(_mixerMock.Object);
    _audioPreferencesMock.Setup(o => o.CurrentValue).Returns(new AudioPreferences
    {
      MasterVolume = 75,
      IsMuted = false,
      Balance = 0
    });
    _configManagerMock.Setup(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Json);
    _configManagerMock.Setup(m => m.GetStoreAsync("config", It.IsAny<CancellationToken>()))
      .ReturnsAsync(_storeMock.Object);

    _sut = new AudioPreferencePersistence(
      _loggerMock.Object,
      _audioEngineMock.Object,
      _audioPreferencesMock.Object,
      _configManagerMock.Object);
  }

  public void Dispose()
  {
    _sut.Dispose();
  }

  [Fact]
  public async Task RestoreVolumePreferencesAsync_SetsVolumeFromConfigStore()
  {
    // Arrange — stored volume is 50%
    _storeMock.Setup(s => s.GetEntryAsync("AudioPreferences:MasterVolume", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ConfigurationEntry { Key = "AudioPreferences:MasterVolume", Value = "50" });
    _storeMock.Setup(s => s.GetEntryAsync("AudioPreferences:IsMuted", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ConfigurationEntry { Key = "AudioPreferences:IsMuted", Value = "False" });

    // Act
    await _sut.RestoreVolumePreferencesAsync();

    // Assert
    _mixerMock.VerifySet(m => m.MasterVolume = 0.5f, Times.Once);
    _mixerMock.VerifySet(m => m.IsMuted = false, Times.Once);
    _mixerMock.VerifySet(m => m.Balance = 0f, Times.Once);
  }

  [Fact]
  public async Task RestoreVolumePreferencesAsync_RestoresMutedState()
  {
    // Arrange
    _storeMock.Setup(s => s.GetEntryAsync("AudioPreferences:MasterVolume", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ConfigurationEntry { Key = "AudioPreferences:MasterVolume", Value = "30" });
    _storeMock.Setup(s => s.GetEntryAsync("AudioPreferences:IsMuted", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ConfigurationEntry { Key = "AudioPreferences:IsMuted", Value = "True" });

    // Act
    await _sut.RestoreVolumePreferencesAsync();

    // Assert
    _mixerMock.VerifySet(m => m.MasterVolume = 0.3f, Times.Once);
    _mixerMock.VerifySet(m => m.IsMuted = true, Times.Once);
  }

  [Fact]
  public async Task RestoreVolumePreferencesAsync_FallsBackToOptionsDefaults_WhenConfigStoreEmpty()
  {
    // Arrange — no entries in config store
    _storeMock.Setup(s => s.GetEntryAsync(It.IsAny<string>(), It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((ConfigurationEntry?)null);

    // Act
    await _sut.RestoreVolumePreferencesAsync();

    // Assert — uses defaults from AudioPreferences (75%, not muted)
    _mixerMock.VerifySet(m => m.MasterVolume = 0.75f, Times.Once);
    _mixerMock.VerifySet(m => m.IsMuted = false, Times.Once);
  }

  [Fact]
  public async Task RestoreVolumePreferencesAsync_HandlesNoConfigManager()
  {
    // Arrange — create instance without config manager
    var sut = new AudioPreferencePersistence(
      _loggerMock.Object,
      _audioEngineMock.Object,
      _audioPreferencesMock.Object,
      configurationManager: null);

    // Act — should not throw
    await sut.RestoreVolumePreferencesAsync();

    // Assert — uses options defaults
    _mixerMock.VerifySet(m => m.MasterVolume = 0.75f, Times.Once);
    sut.Dispose();
  }

  [Fact]
  public async Task RestoreVolumePreferencesAsync_HandlesConfigStoreException()
  {
    // Arrange
    _configManagerMock.Setup(m => m.GetStoreAsync("config", It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("Store not found"));

    // Act — should not throw
    await _sut.RestoreVolumePreferencesAsync();

    // Assert — falls back to options defaults
    _mixerMock.VerifySet(m => m.MasterVolume = 0.75f, Times.Once);
  }

  [Fact]
  public async Task PersistSourcePreferenceAsync_WritesToConfigStore()
  {
    // Act
    await _sut.PersistSourcePreferenceAsync(AudioSourceType.Bluetooth);

    // Assert
    _configManagerMock.Verify(m => m.SetValueAsync(
      "config",
      "AudioPreferences:CurrentSource",
      "Bluetooth",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task PersistSourcePreferenceAsync_UsesSqliteStoreId_WhenConfigured()
  {
    // Arrange
    _configManagerMock.Setup(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);

    // Act
    await _sut.PersistSourcePreferenceAsync(AudioSourceType.Radio);

    // Assert
    _configManagerMock.Verify(m => m.SetValueAsync(
      "sqlite",
      "AudioPreferences:CurrentSource",
      "Radio",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task PersistSourcePreferenceAsync_SkipsGracefully_WhenNoConfigManager()
  {
    // Arrange
    var sut = new AudioPreferencePersistence(
      _loggerMock.Object,
      _audioEngineMock.Object,
      _audioPreferencesMock.Object,
      configurationManager: null);

    // Act — should not throw
    await sut.PersistSourcePreferenceAsync(AudioSourceType.Radio);

    // Assert — no config manager calls
    _configManagerMock.Verify(m => m.SetValueAsync(
      It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    sut.Dispose();
  }

  [Fact]
  public async Task PersistSourcePreferenceAsync_HandlesException()
  {
    // Arrange
    _configManagerMock.Setup(m => m.SetValueAsync(
      It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("Write failed"));

    // Act — should not throw
    await _sut.PersistSourcePreferenceAsync(AudioSourceType.FilePlayer);
  }

  [Fact]
  public void ScheduleVolumePersist_DoesNotThrow_WhenNoConfigManager()
  {
    // Arrange
    var sut = new AudioPreferencePersistence(
      _loggerMock.Object,
      _audioEngineMock.Object,
      _audioPreferencesMock.Object,
      configurationManager: null);

    // Act — should be a no-op
    sut.ScheduleVolumePersist();

    sut.Dispose();
  }

  [Fact]
  public async Task ScheduleVolumePersist_PersistsAfterDebounce()
  {
    // Arrange
    _mixerMock.Setup(m => m.MasterVolume).Returns(0.6f);
    _mixerMock.Setup(m => m.IsMuted).Returns(false);
    _mixerMock.Setup(m => m.Balance).Returns(0f);

    // Act
    _sut.ScheduleVolumePersist();

    // Wait for the 500ms debounce timer to fire
    await Task.Delay(800);

    // Assert — volume preferences were persisted
    _configManagerMock.Verify(m => m.SetValueAsync(
      "config",
      "AudioPreferences:MasterVolume",
      "60",
      It.IsAny<CancellationToken>()), Times.Once);
    _configManagerMock.Verify(m => m.SetValueAsync(
      "config",
      "AudioPreferences:IsMuted",
      "False",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ScheduleVolumePersist_Debounces_MultipleCalls()
  {
    // Arrange
    _mixerMock.Setup(m => m.MasterVolume).Returns(0.8f);
    _mixerMock.Setup(m => m.IsMuted).Returns(false);
    _mixerMock.Setup(m => m.Balance).Returns(0f);

    // Act — rapid calls within 500ms should only result in one persist
    _sut.ScheduleVolumePersist();
    await Task.Delay(100);
    _sut.ScheduleVolumePersist();
    await Task.Delay(100);
    _sut.ScheduleVolumePersist();

    // Wait for debounce to settle (500ms timer + margin for slow CI runners)
    await Task.Delay(2000);

    // Assert — only one persist call
    _configManagerMock.Verify(m => m.SetValueAsync(
      "config",
      "AudioPreferences:MasterVolume",
      It.IsAny<string>(),
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public void Dispose_DisposesTimer()
  {
    // Arrange — schedule a persist to create a timer
    _sut.ScheduleVolumePersist();

    // Act — should not throw
    _sut.Dispose();

    // Assert — second dispose should be safe
    _sut.Dispose();
  }
}
