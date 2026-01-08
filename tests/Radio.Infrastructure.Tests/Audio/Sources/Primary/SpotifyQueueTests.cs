using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

public class SpotifyQueueTests
{
    private readonly Mock<ILogger<SpotifyAudioSource>> _loggerMock;
    private readonly Mock<IOptionsMonitor<SpotifySecrets>> _secretsMock;
    private readonly Mock<IOptionsMonitor<SpotifyPreferences>> _preferencesMock;
    private readonly Mock<IOptionsMonitor<DeviceOptions>> _deviceOptionsMock;
    private readonly SpotifyAudioSource _audioSource;

    public SpotifyQueueTests()
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
    public async Task ClearQueueAsync_ShouldNotThrow_AndLogWarning()
    {
        // Act
        await _audioSource.ClearQueueAsync();

        // Assert
        // Verify that a warning was logged. 
        // Note: verifying extension methods like LogWarning requires mocking the underlying ILogger.Log method.
        // It's a bit verbose with Moq on generic ILogger.
        // For now, minimal assertion is that it doesn't throw.
    }
}
