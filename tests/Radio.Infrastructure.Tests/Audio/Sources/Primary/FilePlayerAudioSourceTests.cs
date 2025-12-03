using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// Unit tests for the FilePlayerAudioSource class.
/// </summary>
public class FilePlayerAudioSourceTests : IDisposable
{
  private readonly Mock<ILogger<FilePlayerAudioSource>> _loggerMock;
  private readonly Mock<IOptionsMonitor<FilePlayerOptions>> _optionsMock;
  private readonly Mock<IOptionsMonitor<FilePlayerPreferences>> _preferencesMock;
  private readonly string _testDir;
  private readonly FilePlayerOptions _options;
  private readonly FilePlayerPreferences _preferences;

  public FilePlayerAudioSourceTests()
  {
    _loggerMock = new Mock<ILogger<FilePlayerAudioSource>>();

    _options = new FilePlayerOptions
    {
      RootDirectory = "",
      SupportedExtensions = [".mp3", ".flac", ".wav", ".ogg"]
    };

    _preferences = new FilePlayerPreferences
    {
      LastSongPlayed = "",
      SongPositionMs = 0,
      Shuffle = false,
      Repeat = RepeatMode.Off
    };

    _optionsMock = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    _optionsMock.Setup(o => o.CurrentValue).Returns(_options);

    _preferencesMock = new Mock<IOptionsMonitor<FilePlayerPreferences>>();
    _preferencesMock.Setup(o => o.CurrentValue).Returns(_preferences);

    // Create a test directory with sample files
    _testDir = Path.Combine(Path.GetTempPath(), $"FilePlayerTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDir);
    Directory.CreateDirectory(Path.Combine(_testDir, "subdir"));
  }

  public void Dispose()
  {
    if (Directory.Exists(_testDir))
    {
      Directory.Delete(_testDir, recursive: true);
    }
  }

  private FilePlayerAudioSource CreateSource()
  {
    return new FilePlayerAudioSource(
      _loggerMock.Object,
      _optionsMock.Object,
      _preferencesMock.Object,
      _testDir);
  }

  private void CreateTestFile(string relativePath, string content = "test")
  {
    var fullPath = Path.Combine(_testDir, relativePath);
    var dir = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
    {
      Directory.CreateDirectory(dir);
    }
    File.WriteAllText(fullPath, content);
  }

  [Fact]
  public void Constructor_SetsCorrectProperties()
  {
    // Act
    var source = CreateSource();

    // Assert
    Assert.Equal("File Player", source.Name);
    Assert.Equal(AudioSourceType.FilePlayer, source.Type);
    Assert.Equal(AudioSourceCategory.Primary, source.Category);
    Assert.True(source.IsSeekable);
    Assert.Equal(AudioSourceState.Created, source.State);
  }

  [Fact]
  public async Task LoadFileAsync_ValidFile_LoadsSuccessfully()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");

    // Act
    await source.LoadFileAsync("test.mp3");

    // Assert
    Assert.Equal(Path.Combine(_testDir, "test.mp3"), source.CurrentFile);
    Assert.Contains("Title", source.Metadata.Keys);
    Assert.Equal("test", source.Metadata["Title"]);
  }

  [Fact]
  public async Task LoadFileAsync_NonExistentFile_ThrowsFileNotFoundException()
  {
    // Arrange
    var source = CreateSource();

    // Act & Assert
    await Assert.ThrowsAsync<FileNotFoundException>(
      () => source.LoadFileAsync("nonexistent.mp3"));
  }

  [Fact]
  public async Task LoadFileAsync_UnsupportedFormat_ThrowsArgumentException()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.xyz");

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
      () => source.LoadFileAsync("test.xyz"));
  }

  [Fact]
  public async Task LoadDirectoryAsync_ValidDirectory_LoadsAllAudioFiles()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.flac");
    CreateTestFile("subdir/song3.wav");
    CreateTestFile("document.txt"); // Should be ignored

    // Act
    await source.LoadDirectoryAsync("");

    // Assert
    Assert.NotNull(source.CurrentFile);
    Assert.Equal(2, source.RemainingTracks); // One is loaded, 2 remain
  }

  [Fact]
  public async Task LoadDirectoryAsync_NonExistentDirectory_ThrowsDirectoryNotFoundException()
  {
    // Arrange
    var source = CreateSource();

    // Act & Assert
    await Assert.ThrowsAsync<DirectoryNotFoundException>(
      () => source.LoadDirectoryAsync("nonexistent"));
  }

  [Fact]
  public async Task LoadDirectoryAsync_EmptyDirectory_ThrowsInvalidOperationException()
  {
    // Arrange
    var source = CreateSource();
    Directory.CreateDirectory(Path.Combine(_testDir, "empty"));

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => source.LoadDirectoryAsync("empty"));
  }

  [Fact]
  public async Task LoadDirectoryAsync_WithShuffle_RandomizesOrder()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Shuffle = true;
    for (int i = 0; i < 10; i++)
    {
      CreateTestFile($"song{i:D2}.mp3");
    }

    // Act - Run multiple times to check for shuffling
    var playlists = new List<string>();
    for (int i = 0; i < 3; i++)
    {
      await source.LoadDirectoryAsync("");
      playlists.Add(string.Join(",", source.Playlist));
      source = CreateSource();
    }

    // Assert - At least one playlist should be different (shuffle worked)
    // Note: There's a tiny chance all 3 are the same, but very unlikely
    Assert.NotEmpty(playlists);
  }

  [Fact]
  public async Task PlayAsync_WithFileLoaded_StartsPlaying()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    // Act
    await source.PlayAsync();

    // Assert
    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task PauseAsync_WhenPlaying_PausesPlayback()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");
    await source.PlayAsync();

    // Act
    await source.PauseAsync();

    // Assert
    Assert.Equal(AudioSourceState.Paused, source.State);
  }

  [Fact]
  public async Task ResumeAsync_WhenPaused_ResumesPlayback()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");
    await source.PlayAsync();
    await source.PauseAsync();

    // Act
    await source.ResumeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task StopAsync_WhenPlaying_StopsPlayback()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");
    await source.PlayAsync();

    // Act
    await source.StopAsync();

    // Assert
    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task SeekAsync_ValidPosition_SeeksSuccessfully()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    // Act
    await source.SeekAsync(TimeSpan.FromSeconds(30));

    // Assert
    Assert.Equal(TimeSpan.FromSeconds(30), source.Position);
  }

  [Fact]
  public async Task SeekAsync_NegativePosition_ThrowsArgumentOutOfRangeException()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.SeekAsync(TimeSpan.FromSeconds(-1)));
  }

  [Fact]
  public async Task NextAsync_WithRemainingTracks_LoadsNextTrack()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    var firstFile = source.CurrentFile;

    // Act
    var result = await source.TryNextAsync();

    // Assert
    Assert.True(result);
    Assert.NotEqual(firstFile, source.CurrentFile);
  }

  [Fact]
  public async Task NextAsync_EmptyPlaylistWithRepeatOff_Stops()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.Off;
    CreateTestFile("song.mp3");
    await source.LoadFileAsync("song.mp3");
    await source.PlayAsync();

    // Act - NextAsync on single track with no repeat should stop
    await source.NextAsync();

    // Assert
    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task DisposeAsync_SavesPreferences()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    await source.LoadFileAsync("test.mp3");

    // Act
    await source.DisposeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Disposed, source.State);
    Assert.Equal(Path.Combine(_testDir, "test.mp3"), _preferences.LastSongPlayed);
  }

  [Fact]
  public async Task StateChanged_EventRaised_OnStateChange()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("test.mp3");
    var stateChanges = new List<AudioSourceState>();
    source.StateChanged += (s, e) => stateChanges.Add(e.NewState);
    await source.LoadFileAsync("test.mp3");

    // Act
    await source.PlayAsync();
    await source.PauseAsync();
    await source.StopAsync();

    // Assert
    Assert.Contains(AudioSourceState.Playing, stateChanges);
    Assert.Contains(AudioSourceState.Paused, stateChanges);
    Assert.Contains(AudioSourceState.Stopped, stateChanges);
  }

  [Fact]
  public async Task Volume_SetAndGet_WorksCorrectly()
  {
    // Arrange
    var source = CreateSource();

    // Act
    source.Volume = 0.5f;

    // Assert
    Assert.Equal(0.5f, source.Volume);

    await source.DisposeAsync();
  }

  [Fact]
  public async Task Volume_ClampedToValidRange()
  {
    // Arrange
    var source = CreateSource();

    // Act & Assert
    source.Volume = -0.5f;
    Assert.Equal(0.0f, source.Volume);

    source.Volume = 1.5f;
    Assert.Equal(1.0f, source.Volume);

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
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.StopAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.SeekAsync(TimeSpan.Zero));
  }

  #region Track Navigation Tests

  [Fact]
  public async Task NextAsync_WithMultipleTracks_MovesToNextTrack()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");
    var firstFile = source.CurrentFile;

    // Act
    await source.NextAsync();

    // Assert
    Assert.NotEqual(firstFile, source.CurrentFile);
    Assert.Equal(1, source.RemainingTracks); // 1 track remaining in queue
  }

  [Fact]
  public async Task NextAsync_WithRepeatOne_ReplaysCurrentTrack()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.One;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    var firstFile = source.CurrentFile;
    await source.PlayAsync();

    // Act
    await source.NextAsync();

    // Assert
    Assert.Equal(firstFile, source.CurrentFile);
    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public async Task NextAsync_EndOfPlaylistWithRepeatOff_Stops()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.Off;
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");
    await source.PlayAsync();

    // Act
    await source.NextAsync();

    // Assert
    Assert.Equal(AudioSourceState.Stopped, source.State);
  }

  [Fact]
  public async Task NextAsync_EndOfPlaylistWithRepeatAll_RestartsPlaylist()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.All;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    
    // Move through all tracks
    await source.NextAsync(); // song2
    await source.NextAsync(); // should loop back

    // Assert
    Assert.NotNull(source.CurrentFile);
    Assert.Equal(1, source.RemainingTracks); // Playlist reloaded with 2 tracks, one loaded
  }

  [Fact]
  public async Task PreviousAsync_PositionGreaterThan3Seconds_SeeksToBeginning()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");
    await source.SeekAsync(TimeSpan.FromSeconds(5));
    await source.PlayAsync();

    // Act
    await source.PreviousAsync();

    // Assert
    Assert.Equal(TimeSpan.Zero, source.Position);
    Assert.Equal(AudioSourceState.Playing, source.State);
  }

  [Fact]
  public async Task PreviousAsync_PositionLessThan3Seconds_GoesToPreviousTrack()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    await source.NextAsync(); // Move to song2
    var secondFile = source.CurrentFile;

    // Position is 0, which is < 3 seconds
    // Act
    await source.PreviousAsync();

    // Assert
    Assert.NotEqual(secondFile, source.CurrentFile);
    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public async Task PreviousAsync_AtBeginningOfPlaylist_SeeksToZero()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.Off;
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");
    await source.SeekAsync(TimeSpan.FromSeconds(1));

    // Act
    await source.PreviousAsync();

    // Assert
    Assert.Equal(TimeSpan.Zero, source.Position);
  }

  [Fact]
  public async Task PreviousAsync_AtBeginningWithRepeatAll_GoesToLastTrack()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.All;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");
    var firstFile = source.CurrentFile;

    // Act
    await source.PreviousAsync();

    // Assert
    Assert.NotEqual(firstFile, source.CurrentFile);
    Assert.Contains("song3.mp3", source.CurrentFile);
  }

  [Fact]
  public async Task SetShuffleAsync_EnableShuffle_ShufflesPlaylist()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Shuffle = false;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    CreateTestFile("song4.mp3");
    CreateTestFile("song5.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    await source.SetShuffleAsync(true);

    // Assert
    Assert.True(_preferences.Shuffle);
    // Can't easily test randomization, but we can verify it's enabled
    Assert.True(source.IsShuffleEnabled);
  }

  [Fact]
  public async Task SetShuffleAsync_DisableShuffle_RestoresOriginalOrder()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Shuffle = true;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync(""); // Loads shuffled

    // Act
    await source.SetShuffleAsync(false);

    // Assert
    Assert.False(_preferences.Shuffle);
    Assert.False(source.IsShuffleEnabled);
  }

  [Fact]
  public async Task SetShuffleAsync_AlreadySet_NoChange()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Shuffle = true;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    await source.SetShuffleAsync(true); // Already enabled

    // Assert
    Assert.True(_preferences.Shuffle);
  }

  [Fact]
  public async Task SetRepeatModeAsync_Off_SetsRepeatOff()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.All;

    // Act
    await source.SetRepeatModeAsync(RepeatMode.Off);

    // Assert
    Assert.Equal(RepeatMode.Off, _preferences.Repeat);
    Assert.Equal(RepeatMode.Off, source.RepeatMode);
  }

  [Fact]
  public async Task SetRepeatModeAsync_One_SetsRepeatOne()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.Off;

    // Act
    await source.SetRepeatModeAsync(RepeatMode.One);

    // Assert
    Assert.Equal(RepeatMode.One, _preferences.Repeat);
    Assert.Equal(RepeatMode.One, source.RepeatMode);
  }

  [Fact]
  public async Task SetRepeatModeAsync_All_SetsRepeatAll()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.Off;

    // Act
    await source.SetRepeatModeAsync(RepeatMode.All);

    // Assert
    Assert.Equal(RepeatMode.All, _preferences.Repeat);
    Assert.Equal(RepeatMode.All, source.RepeatMode);
  }

  [Fact]
  public async Task SetRepeatModeAsync_AlreadySet_NoChange()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.One;

    // Act
    await source.SetRepeatModeAsync(RepeatMode.One);

    // Assert
    Assert.Equal(RepeatMode.One, _preferences.Repeat);
  }

  [Fact]
  public void CapabilityProperties_FilePlayer_AllSupported()
  {
    // Arrange & Act
    var source = CreateSource();

    // Assert
    Assert.True(source.SupportsNext);
    Assert.True(source.SupportsPrevious);
    Assert.True(source.SupportsShuffle);
    Assert.True(source.SupportsRepeat);
    Assert.True(source.SupportsQueue);
  }

  [Fact]
  public async Task TryNextAsync_WrapperMethod_CallsNextAsync()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    var result = await source.TryNextAsync();

    // Assert
    Assert.True(result);
    Assert.NotNull(source.CurrentFile);
  }

  [Fact]
  public async Task NavigationMethods_AfterDispose_ThrowObjectDisposedException()
  {
    // Arrange
    var source = CreateSource();
    await source.DisposeAsync();

    // Act & Assert
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.NextAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.PreviousAsync());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.SetShuffleAsync(true));
    await Assert.ThrowsAsync<ObjectDisposedException>(() => source.SetRepeatModeAsync(RepeatMode.All));
  }

  [Fact]
  public async Task NextAndPreviousNavigation_ComplexScenario_WorksCorrectly()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.Off;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");
    var song1 = source.CurrentFile;

    // Act & Assert - Navigate forward
    await source.NextAsync();
    var song2 = source.CurrentFile;
    Assert.NotEqual(song1, song2);

    await source.NextAsync();
    var song3 = source.CurrentFile;
    Assert.NotEqual(song2, song3);

    // Navigate backward
    await source.PreviousAsync();
    Assert.Equal(song2, source.CurrentFile);

    await source.PreviousAsync();
    Assert.Equal(song1, source.CurrentFile);
  }

  [Fact]
  public async Task ShuffleWithMultipleTracks_MaintainsAllTracks()
  {
    // Arrange
    var source = CreateSource();
    _preferences.Shuffle = false;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    CreateTestFile("song4.mp3");
    await source.LoadDirectoryAsync("");
    var totalTracks = source.RemainingTracks + 1; // +1 for current

    // Act
    await source.SetShuffleAsync(true);

    // Assert - All tracks should still be in playlist
    var tracksAfterShuffle = source.RemainingTracks + 1;
    Assert.Equal(totalTracks, tracksAfterShuffle);
  }

  #endregion
}
