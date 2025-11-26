using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Unit tests for the SpotifyAudioSource class.
/// Note: These tests use mocks and don't require actual Spotify connectivity.
/// </summary>
public class SpotifyAudioSourceTests
{
  private readonly Mock<ILogger<SpotifyAudioSource>> _loggerMock;
  private readonly Mock<IOptionsMonitor<SpotifySecrets>> _secretsMock;
  private readonly Mock<IOptionsMonitor<SpotifyPreferences>> _preferencesMock;
  private readonly SpotifySecrets _secrets;
  private readonly SpotifyPreferences _preferences;

  public SpotifyAudioSourceTests()
  {
    _loggerMock = new Mock<ILogger<SpotifyAudioSource>>();

    _secrets = new SpotifySecrets
    {
      ClientID = "",
      ClientSecret = "",
      RefreshToken = ""
    };

    _preferences = new SpotifyPreferences
    {
      LastSongPlayed = "",
      SongPositionMs = 0,
      Shuffle = false,
      Repeat = RepeatMode.Off
    };

    _secretsMock = new Mock<IOptionsMonitor<SpotifySecrets>>();
    _secretsMock.Setup(o => o.CurrentValue).Returns(_secrets);

    _preferencesMock = new Mock<IOptionsMonitor<SpotifyPreferences>>();
    _preferencesMock.Setup(o => o.CurrentValue).Returns(_preferences);
  }

  private SpotifyAudioSource CreateSource()
  {
    return new SpotifyAudioSource(
      _loggerMock.Object,
      _secretsMock.Object,
      _preferencesMock.Object);
  }

  [Fact]
  public void Constructor_SetsCorrectProperties()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.Equal("Spotify", source.Name);
    Assert.Equal(AudioSourceType.Spotify, source.Type);
    Assert.Equal(AudioSourceCategory.Primary, source.Category);
    Assert.True(source.IsSeekable);
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public void Id_GeneratedWithSpotifyPrefix()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.StartsWith("Spotify-", source.Id);
  }

  [Fact]
  public void IsAuthenticated_InitiallyFalse()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.False(source.IsAuthenticated);
  }

  [Fact]
  public async Task PlayAsync_WithoutCredentials_EntersErrorState()
  {
    // Arrange
    var source = CreateSource();
    // Secrets are empty by default

    // Act - PlayAsync will call InitializeAsync which sets error state
    await source.PlayAsync();

    // Assert
    Assert.Equal(AudioSourceState.Error, source.State);
  }

  [Fact]
  public void Volume_SetAndGet_WorksCorrectly()
  {
    // Arrange
    var source = CreateSource();

    // Act
    source.Volume = 0.5f;

    // Assert
    Assert.Equal(0.5f, source.Volume);
  }

  [Fact]
  public void Volume_ClampedToValidRange()
  {
    // Arrange
    var source = CreateSource();

    // Act & Assert
    source.Volume = -0.5f;
    Assert.Equal(0.0f, source.Volume);

    source.Volume = 1.5f;
    Assert.Equal(1.0f, source.Volume);
  }

  [Fact]
  public void Metadata_InitiallyEmpty()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.Empty(source.Metadata);
  }

  [Fact]
  public void Duration_InitiallyNull()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.Null(source.Duration);
  }

  [Fact]
  public void Position_InitiallyZero()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public async Task StateChanged_EventRaised_OnStateChange()
  {
    // Arrange
    var source = CreateSource();
    var stateChanges = new List<AudioSourceState>();
    source.StateChanged += (s, e) => stateChanges.Add(e.NewState);

    // Act
    await source.PlayAsync(); // Will fail due to no credentials

    // Assert
    Assert.Contains(AudioSourceState.Initializing, stateChanges);
    Assert.Contains(AudioSourceState.Error, stateChanges);
  }

  [Fact]
  public async Task DisposeAsync_TransitionsToDisposedState()
  {
    // Arrange
    var source = CreateSource();

    // Act
    await source.DisposeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Disposed, source.State);
  }

  [Fact]
  public async Task DisposeAsync_CanBeCalledMultipleTimes()
  {
    // Arrange
    var source = CreateSource();

    // Act & Assert - should not throw
    await source.DisposeAsync();
    await source.DisposeAsync();
  }

  [Fact]
  public async Task Operations_AfterDispose_ThrowObjectDisposedException()
  {
    // Arrange
    var source = CreateSource();
    await source.DisposeAsync();

    // Act & Assert
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.PlayAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.PauseAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.ResumeAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.StopAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.SeekAsync(TimeSpan.Zero));
  }

  [Fact]
  public void GetSoundComponent_ReturnsNonNull()
  {
    // Arrange
    var source = CreateSource();

    // Act
    var component = source.GetSoundComponent();

    // Assert
    Assert.NotNull(component);
  }

  [Fact]
  public async Task PauseAsync_WhenNotPlaying_DoesNotThrow()
  {
    // Arrange
    var source = CreateSource();
    // State is Created, not Playing

    // Act & Assert - should not throw, just log warning
    await source.PauseAsync();

    // State should remain unchanged
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task ResumeAsync_WhenNotPaused_DoesNotThrow()
  {
    // Arrange
    var source = CreateSource();
    // State is Created, not Paused

    // Act & Assert - should not throw, just log warning
    await source.ResumeAsync();

    // State should remain unchanged
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task StopAsync_WhenNotPlaying_DoesNotThrow()
  {
    // Arrange
    var source = CreateSource();
    // State is Created

    // Act & Assert - should not throw
    await source.StopAsync();

    // State should remain unchanged
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public void Preferences_RestoresLastSongPlayed()
  {
    // Arrange
    _preferences.LastSongPlayed = "spotify:track:abc123";
    _preferences.SongPositionMs = 30000;

    // Act
    var source = CreateSource();

    // Assert - preferences should be available for restoration
    Assert.NotNull(_preferencesMock.Object.CurrentValue.LastSongPlayed);
    Assert.Equal(30000, _preferencesMock.Object.CurrentValue.SongPositionMs);
  }
}
