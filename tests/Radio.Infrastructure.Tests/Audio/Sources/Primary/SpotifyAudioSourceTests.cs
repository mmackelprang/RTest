using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

public class SpotifyAudioSourceTests
{
    private readonly Mock<ILogger<SpotifyAudioSource>> _loggerMock;
    private readonly Mock<IOptionsMonitor<SpotifySecrets>> _secretsMock;
    private readonly Mock<IOptionsMonitor<SpotifyPreferences>> _preferencesMock;
    private readonly Mock<IOptionsMonitor<DeviceOptions>> _deviceOptionsMock;
    private readonly SpotifyAudioSource _audioSource;

    public SpotifyAudioSourceTests()
    {
        _loggerMock = new Mock<ILogger<SpotifyAudioSource>>();
        _secretsMock = new Mock<IOptionsMonitor<SpotifySecrets>>();
        _preferencesMock = new Mock<IOptionsMonitor<SpotifyPreferences>>();
        _deviceOptionsMock = new Mock<IOptionsMonitor<DeviceOptions>>();

        _secretsMock.Setup(o => o.CurrentValue).Returns(new SpotifySecrets
        {
            ClientID = "test-client-id",
            ClientSecret = "test-client-secret"
        });

        _preferencesMock.Setup(o => o.CurrentValue).Returns(new SpotifyPreferences
        {
            LastSongPlayed = "",
            Shuffle = false,
            Repeat = RepeatMode.Off
        });

        _deviceOptionsMock.Setup(o => o.CurrentValue).Returns(new DeviceOptions
        {
            Spotify = new SpotifyDeviceOptions
            {
                Mode = SpotifyMode.RemoteControl
            }
        });

        _audioSource = new SpotifyAudioSource(
            _loggerMock.Object,
            _secretsMock.Object,
            _preferencesMock.Object,
            _deviceOptionsMock.Object,
            null,
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
    public void SupportsNext_ShouldReturnTrue()
    {
        Assert.True(_audioSource.SupportsNext);
    }

    [Fact]
    public void SupportsPrevious_ShouldReturnTrue()
    {
        Assert.True(_audioSource.SupportsPrevious);
    }

    [Fact]
    public void SupportsShuffle_ShouldReturnTrue()
    {
        Assert.True(_audioSource.SupportsShuffle);
    }

    [Fact]
    public void SupportsRepeat_ShouldReturnTrue()
    {
        Assert.True(_audioSource.SupportsRepeat);
    }

    [Fact]
    public void SupportsQueue_ShouldReturnTrue()
    {
        Assert.True(_audioSource.SupportsQueue);
    }
}
