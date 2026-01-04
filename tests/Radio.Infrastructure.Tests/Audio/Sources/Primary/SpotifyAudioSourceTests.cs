using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Audio.Sources.Primary;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.Sources.Primary;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

public class SpotifyAudioSourceTests
{
    private readonly Mock<ILogger<SpotifyAudioSource>> _loggerMock;
    private readonly Mock<IOptionsMonitor<DeviceOptions>> _deviceOptionsMock;
    private readonly Mock<ISpotifyControllerFactory> _spotifyControllerFactoryMock;
    private readonly Mock<ISpotifyController> _spotifyControllerMock;
    private readonly SpotifyAudioSource _audioSource;

    public SpotifyAudioSourceTests()
    {
        _loggerMock = new Mock<ILogger<SpotifyAudioSource>>();
        _spotifyControllerFactoryMock = new Mock<ISpotifyControllerFactory>();
        _spotifyControllerMock = new Mock<ISpotifyController>();
        
        _spotifyControllerFactoryMock
            .Setup(f => f.CreateController(It.IsAny<SpotifyMode>(), It.IsAny<IAudioDeviceManager?>()))
            .Returns(_spotifyControllerMock.Object);

        _deviceOptionsMock = new Mock<IOptionsMonitor<DeviceOptions>>();
        _deviceOptionsMock.Setup(o => o.CurrentValue).Returns(new DeviceOptions
        {
          Spotify = new SpotifyDeviceOptions
          {
            Mode = SpotifyMode.RemoteControl
          }
        });

        _audioSource = new SpotifyAudioSource(
            _loggerMock.Object,
            _deviceOptionsMock.Object,
            _spotifyControllerFactoryMock.Object,
            null);
    }

    [Fact]
    public void Constructor_ShouldInitialize()
    {
        Assert.NotNull(_audioSource);
    }

    [Fact]
    public void Name_ShouldReturnSpotify()
    {
        Assert.Equal("Spotify", _audioSource.Name);
    }

    [Fact]
    public void Type_ShouldReturnCorrectType()
    {
        Assert.Equal(AudioSourceType.Spotify, _audioSource.Type);
    }

    [Fact]
    public async Task InitializeAsync_ShouldInitializeController()
    {
        await _audioSource.InitializeAsync(CancellationToken.None);

        _spotifyControllerMock.Verify(c => c.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldStartPlayback()
    {
        await _audioSource.StartAsync(CancellationToken.None);

        _spotifyControllerMock.Verify(c => c.StartPlaybackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldStopPlayback()
    {
        await _audioSource.StopAsync(CancellationToken.None);

        _spotifyControllerMock.Verify(c => c.StopPlaybackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PauseAsync_ShouldPausePlayback()
    {
        await _audioSource.PauseAsync(CancellationToken.None);

        _spotifyControllerMock.Verify(c => c.PausePlaybackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResumeAsync_ShouldResumePlayback()
    {
        await _audioSource.ResumeAsync(CancellationToken.None);

        _spotifyControllerMock.Verify(c => c.ResumePlaybackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeController()
    {
        await _audioSource.DisposeAsync();

        _spotifyControllerMock.Verify(c => c.DisposeAsync(), Times.Once);
    }
}
