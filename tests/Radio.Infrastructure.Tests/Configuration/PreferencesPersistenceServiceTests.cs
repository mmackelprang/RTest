using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Services;

namespace Radio.Infrastructure.Tests.Configuration;

/// <summary>
/// Unit tests for the PreferencesPersistenceService class.
/// </summary>
public class PreferencesPersistenceServiceTests : IDisposable
{
  private readonly Mock<ILogger<PreferencesPersistenceService>> _loggerMock;
  private readonly Mock<IOptionsMonitor<AudioPreferences>> _audioPrefsMock;
  private readonly Mock<IOptionsMonitor<FilePlayerPreferences>> _filePlayerPrefsMock;
  private readonly Mock<IOptionsMonitor<SpotifyPreferences>> _spotifyPrefsMock;
  private readonly Mock<IOptionsMonitor<TTSPreferences>> _ttsPrefsMock;
  private readonly Mock<IOptionsMonitor<RadioPreferences>> _radioPrefsMock;
  private readonly Mock<IOptionsMonitor<GenericSourcePreferences>> _genericPrefsMock;
  private readonly Mock<IConfigurationManager> _configManagerMock;
  private readonly Mock<IHostApplicationLifetime> _lifetimeMock;
  private readonly Mock<IConfigurationStore> _storeMock;
  private readonly CancellationTokenSource _cts;

  public PreferencesPersistenceServiceTests()
  {
    _loggerMock = new Mock<ILogger<PreferencesPersistenceService>>();
    _audioPrefsMock = new Mock<IOptionsMonitor<AudioPreferences>>();
    _filePlayerPrefsMock = new Mock<IOptionsMonitor<FilePlayerPreferences>>();
    _spotifyPrefsMock = new Mock<IOptionsMonitor<SpotifyPreferences>>();
    _ttsPrefsMock = new Mock<IOptionsMonitor<TTSPreferences>>();
    _radioPrefsMock = new Mock<IOptionsMonitor<RadioPreferences>>();
    _genericPrefsMock = new Mock<IOptionsMonitor<GenericSourcePreferences>>();
    _configManagerMock = new Mock<IConfigurationManager>();
    _lifetimeMock = new Mock<IHostApplicationLifetime>();
    _storeMock = new Mock<IConfigurationStore>();
    _cts = new CancellationTokenSource();

    // Setup default preferences
    _audioPrefsMock.Setup(o => o.CurrentValue).Returns(new AudioPreferences());
    _filePlayerPrefsMock.Setup(o => o.CurrentValue).Returns(new FilePlayerPreferences());
    _spotifyPrefsMock.Setup(o => o.CurrentValue).Returns(new SpotifyPreferences());
    _ttsPrefsMock.Setup(o => o.CurrentValue).Returns(new TTSPreferences());
    _radioPrefsMock.Setup(o => o.CurrentValue).Returns(new RadioPreferences());
    _genericPrefsMock.Setup(o => o.CurrentValue).Returns(new GenericSourcePreferences());

    // Setup configuration manager
    _configManagerMock.Setup(m => m.CurrentStoreType).Returns(ConfigurationStoreType.Json);
    _configManagerMock.Setup(m => m.GetStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(_storeMock.Object);
    _configManagerMock.Setup(m => m.CreateStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(_storeMock.Object);

    // Setup store
    _storeMock.Setup(s => s.SetEntriesAsync(It.IsAny<IEnumerable<ConfigurationEntry>>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
    _storeMock.Setup(s => s.SaveAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);

    // Setup lifetime
    _lifetimeMock.Setup(l => l.ApplicationStopping).Returns(_cts.Token);
  }

  public void Dispose()
  {
    _cts.Cancel();
    _cts.Dispose();
  }

  private PreferencesPersistenceService CreateService()
  {
    return new PreferencesPersistenceService(
      _loggerMock.Object,
      _audioPrefsMock.Object,
      _filePlayerPrefsMock.Object,
      _spotifyPrefsMock.Object,
      _ttsPrefsMock.Object,
      _radioPrefsMock.Object,
      _genericPrefsMock.Object,
      _configManagerMock.Object,
      _lifetimeMock.Object);
  }

  [Fact]
  public async Task StartAsync_StartsService_Successfully()
  {
    // Arrange
    var service = CreateService();

    // Act
    await service.StartAsync(CancellationToken.None);

    // Assert
    // Service should start without throwing
    Assert.True(true);
  }

  [Fact]
  public async Task StopAsync_SavesPreferences_BeforeStopping()
  {
    // Arrange
    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _storeMock.Verify(s => s.SetEntriesAsync(
      It.IsAny<IEnumerable<ConfigurationEntry>>(),
      It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    _storeMock.Verify(s => s.SaveAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task SavePreferences_CallsConfigurationStore()
  {
    // Arrange
    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Give it a moment to start
    await Task.Delay(100);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _configManagerMock.Verify(m => m.GetStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    _storeMock.Verify(s => s.SaveAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task SavePreferences_SerializesAudioPreferences()
  {
    // Arrange
    var audioPrefs = new AudioPreferences
    {
      CurrentSource = "FilePlayer",
      CurrentOutput = "Speaker1",
      MasterVolume = 75,
      Balance = 10,
      CurrentInput = "Microphone1"
    };
    _audioPrefsMock.Setup(o => o.CurrentValue).Returns(audioPrefs);

    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _storeMock.Verify(s => s.SetEntriesAsync(
      It.Is<IEnumerable<ConfigurationEntry>>(entries =>
        entries.Any(e => e.Key.StartsWith("AudioPreferences:"))),
      It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task SavePreferences_SerializesFilePlayerPreferences()
  {
    // Arrange
    var filePlayerPrefs = new FilePlayerPreferences
    {
      Shuffle = true,
      Repeat = RepeatMode.All,
      QueueItems = new List<string> { "/path/song1.mp3", "/path/song2.mp3" },
      CurrentQueueIndex = 1
    };
    _filePlayerPrefsMock.Setup(o => o.CurrentValue).Returns(filePlayerPrefs);

    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _storeMock.Verify(s => s.SetEntriesAsync(
      It.Is<IEnumerable<ConfigurationEntry>>(entries =>
        entries.Any(e => e.Key.StartsWith("FilePlayerPreferences:"))),
      It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task SavePreferences_SerializesSpotifyPreferences()
  {
    // Arrange
    var spotifyPrefs = new SpotifyPreferences
    {
      LastSearchQuery = "test query",
      LastSearchTimestamp = DateTime.UtcNow,
      Shuffle = false,
      Repeat = RepeatMode.One
    };
    _spotifyPrefsMock.Setup(o => o.CurrentValue).Returns(spotifyPrefs);

    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _storeMock.Verify(s => s.SetEntriesAsync(
      It.Is<IEnumerable<ConfigurationEntry>>(entries =>
        entries.Any(e => e.Key.StartsWith("SpotifyPreferences:"))),
      It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task SavePreferences_HandlesStoreCreation_WhenStoreNotFound()
  {
    // Arrange
    _configManagerMock.Setup(m => m.GetStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new FileNotFoundException());
    _configManagerMock.Setup(m => m.CreateStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(_storeMock.Object);

    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _configManagerMock.Verify(m => m.CreateStoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task SavePreferences_ContinuesOnError_DoesNotThrow()
  {
    // Arrange
    _storeMock.Setup(s => s.SaveAsync(It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("Test error"));

    var service = CreateService();
    await service.StartAsync(CancellationToken.None);

    // Act & Assert - should not throw
    await service.StopAsync(CancellationToken.None);
  }
}
