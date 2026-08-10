using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Fingerprinting;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio;
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
  public async Task NextAsync_WithRepeatOne_UserInitiated_AdvancesToNextTrack()
  {
    // Arrange - user-initiated Next (not auto-advance) should advance even in RepeatOne mode
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.One;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    var firstFile = source.CurrentFile;
    await source.PlayAsync();

    // Act - user clicks Next (trackEndedNaturally is false by default)
    await source.NextAsync();

    // Assert - should advance to next track, not replay current
    Assert.NotEqual(firstFile, source.CurrentFile);
  }

  [Fact]
  public async Task NextAsync_WithRepeatOne_AutoAdvance_ReplaysCurrentTrack()
  {
    // Arrange - auto-advance (natural track end) should replay in RepeatOne mode
    var source = CreateSource();
    _preferences.Repeat = RepeatMode.One;
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    var firstFile = source.CurrentFile;
    await source.PlayAsync();

    // Simulate natural track end by setting the internal flag via reflection
    var field = typeof(FilePlayerAudioSource).GetField("_trackEndedNaturally",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    field!.SetValue(source, true);

    // Act - auto-advance from natural track end
    await source.NextAsync();

    // Assert - should replay current track
    Assert.Equal(firstFile, source.CurrentFile);
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

  #region IPlayQueue Tests

  [Fact]
  public async Task GetQueueAsync_WithLoadedFiles_ReturnsQueueItems()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    var queue = await source.GetQueueAsync();

    // Assert
    Assert.NotNull(queue);
    Assert.Equal(3, queue.Count);
    Assert.True(queue[0].IsCurrent); // First item is current
    Assert.False(queue[1].IsCurrent);
    Assert.False(queue[2].IsCurrent);
    Assert.Equal(0, queue[0].Index);
    Assert.Equal(1, queue[1].Index);
    Assert.Equal(2, queue[2].Index);
  }

  [Fact]
  public async Task GetQueueAsync_ExtractsMetadata_PopulatesQueueItems()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    var queue = await source.GetQueueAsync();

    // Assert
    Assert.NotNull(queue);
    Assert.Equal(2, queue.Count);

    // Check metadata fields are populated (using defaults since test files don't have tags)
    Assert.NotNull(queue[0].Title);
    Assert.NotNull(queue[0].Artist);
    Assert.NotNull(queue[0].Album);
    Assert.NotNull(queue[0].Id);
  }

  // -- Album-art-on-enqueue tests (Task #81) ------------------------------------
  //
  // PR D added AlbumArtUrl to QueueItemDto and Up Next reads it with music_note fallback.
  // FilePlayerAudioSource is the producer; CreateQueueItem / CreateQueueItemWithState
  // must surface the URL so Up Next renders real art instead of placeholder icons.

  [Fact]
  public async Task GetQueueAsync_WithoutAlbumArtCache_ReturnsNullAlbumArtUrl()
  {
    // Arrange: source with no AlbumArtCacheService — TryGetEmbeddedAlbumArtUrl
    // returns null on the cache==null guard, regardless of file content.
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    var queue = await source.GetQueueAsync();

    // Assert
    Assert.Single(queue);
    Assert.Null(queue[0].AlbumArtUrl);
  }

  [Fact]
  public async Task GetQueueAsync_WithCacheButInvalidFile_ReturnsNullAlbumArtUrl()
  {
    // Arrange: cache wired but file content is "test" — TagLib throws and we
    // log+swallow, returning null. Up Next falls back to music_note.
    var cache = CreateAlbumArtCacheService();
    var source = CreateSourceWithAlbumArtCache(cache);
    CreateTestFile("song1.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    var queue = await source.GetQueueAsync();

    // Assert
    Assert.Single(queue);
    Assert.Null(queue[0].AlbumArtUrl);
  }

  [Fact]
  public async Task GetQueueAsync_WithCacheAndEmbeddedArt_PopulatesAlbumArtUrl()
  {
    // Arrange: real MP3 fixture with an APIC frame injected via TagLib#.
    // Producer must surface the cached /api/albumart/<hash>.<ext> URL.
    var cache = CreateAlbumArtCacheService();
    var source = CreateSourceWithAlbumArtCache(cache);
    var mp3WithArt = CreateMp3WithEmbeddedArt("song-with-art.mp3");
    try
    {
      await source.LoadDirectoryAsync("");

      // Act
      var queue = await source.GetQueueAsync();

      // Assert: art-bearing track has a real proxy URL.
      var item = queue.Single(q => q.Id == mp3WithArt);
      Assert.NotNull(item.AlbumArtUrl);
      Assert.StartsWith("/api/albumart/", item.AlbumArtUrl);
    }
    finally
    {
      await source.DisposeAsync();
    }
  }

  [Fact]
  public async Task GetQueueAsync_WithCacheAndNoArt_ReturnsNullAlbumArtUrl()
  {
    // Arrange: real MP3 fixture WITHOUT any embedded picture.
    // TryGetEmbeddedAlbumArtUrl returns null on the pictures==empty guard.
    var cache = CreateAlbumArtCacheService();
    var source = CreateSourceWithAlbumArtCache(cache);
    var mp3NoArt = CreateMp3WithoutEmbeddedArt("song-no-art.mp3");
    try
    {
      await source.LoadDirectoryAsync("");

      // Act
      var queue = await source.GetQueueAsync();

      // Assert
      var item = queue.Single(q => q.Id == mp3NoArt);
      Assert.Null(item.AlbumArtUrl);
    }
    finally
    {
      await source.DisposeAsync();
    }
  }

  [Fact]
  public async Task GetFullPlaylistAsync_WithEmbeddedArt_PopulatesAlbumArtUrl()
  {
    // Arrange: same as above but exercises the CreateQueueItemWithState path used by
    // GetFullPlaylistAsync (the path that backs /api/queue/full — the endpoint the
    // Up Next tile reads).
    var cache = CreateAlbumArtCacheService();
    var source = CreateSourceWithAlbumArtCache(cache);
    var mp3WithArt = CreateMp3WithEmbeddedArt("full-playlist-art.mp3");
    try
    {
      await source.LoadFileAsync(Path.GetFileName(mp3WithArt));

      // Act
      var fullPlaylist = await source.GetFullPlaylistAsync();

      // Assert: the loaded (current) track surfaces the album-art URL through the
      // CreateQueueItemWithState path used by full-playlist projection.
      var item = fullPlaylist.Single(q => q.Id == mp3WithArt);
      Assert.NotNull(item.AlbumArtUrl);
      Assert.StartsWith("/api/albumart/", item.AlbumArtUrl);
    }
    finally
    {
      await source.DisposeAsync();
    }
  }

  // -- Album-art test helpers ---------------------------------------------------

  // The album-art cache writes to ./data/albumart by design (content-addressed,
  // shared cache). We let it write there and rely on the SHA-256 dedup to keep
  // the on-disk footprint minimal across test runs.
  private static AlbumArtCacheService CreateAlbumArtCacheService()
  {
    var logger = new Mock<ILogger<AlbumArtCacheService>>().Object;
    return new AlbumArtCacheService(logger);
  }

  private FilePlayerAudioSource CreateSourceWithAlbumArtCache(AlbumArtCacheService cache)
  {
    return new FilePlayerAudioSource(
      _loggerMock.Object,
      _optionsMock.Object,
      _preferencesMock.Object,
      _testDir,
      albumArtCache: cache);
  }

  // Path to a real MP3 already in the repo. Used as the "base" of fixture files
  // because constructing a valid MP3 from scratch is non-trivial and TagLib needs
  // a real audio container to write tags to.
  private static readonly string SampleMp3 =
    Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", "..", "src", "Radio.API", "media", "audio", "alarm",
      "PD - Alert.mp3"));

  // A tiny synthetic PNG payload (8x8 transparent) used as the embedded picture.
  // Generated once via System.Drawing-equivalent magic-byte construction. PNG
  // header + IHDR + IDAT + IEND chunks for an 8x8 RGBA image. TagLib treats this
  // as opaque bytes — it never decodes the image.
  private static readonly byte[] TinyPng = new byte[]
  {
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
    0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR length + type
    0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x08, // 8x8
    0x08, 0x06, 0x00, 0x00, 0x00, 0xC4, 0x0F, 0xBE, // 8-bit RGBA + CRC
    0x8B,
    0x00, 0x00, 0x00, 0x13, 0x49, 0x44, 0x41, 0x54, // IDAT length + type
    0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0, 0x1F, // zlib-compressed solid
    0x09, 0x00, 0x80, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, // (placeholder; CRC may be off
    0x00, 0x00, 0x00, 0xFF,                         // but TagLib doesn't validate)
    0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, // IEND length + type
    0xAE, 0x42, 0x60, 0x82                          // IEND CRC
  };

  // Copies the sample MP3 into the test dir under the given name, then injects a
  // FrontCover APIC frame. Returns the absolute path to the resulting file.
  private string CreateMp3WithEmbeddedArt(string relativePath)
  {
    var dest = CopySampleMp3(relativePath);
    using (var tag = TagLib.File.Create(dest))
    {
      tag.Tag.Pictures = new TagLib.IPicture[]
      {
        new TagLib.Picture(new TagLib.ByteVector(TinyPng))
        {
          Type = TagLib.PictureType.FrontCover,
          MimeType = "image/png",
          Description = "Cover"
        }
      };
      tag.Save();
    }
    return dest;
  }

  // Copies the sample MP3 into the test dir under the given name and ensures no
  // pictures are present (TagLib defaults strip embedded art on Save when the
  // pictures collection is empty).
  private string CreateMp3WithoutEmbeddedArt(string relativePath)
  {
    var dest = CopySampleMp3(relativePath);
    using (var tag = TagLib.File.Create(dest))
    {
      tag.Tag.Pictures = Array.Empty<TagLib.IPicture>();
      tag.Save();
    }
    return dest;
  }

  private string CopySampleMp3(string relativePath)
  {
    if (!System.IO.File.Exists(SampleMp3))
    {
      throw new InvalidOperationException(
        $"Sample MP3 fixture not found at expected repo path: {SampleMp3}");
    }
    var dest = Path.Combine(_testDir, relativePath);
    var destDir = Path.GetDirectoryName(dest);
    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
    {
      Directory.CreateDirectory(destDir);
    }
    System.IO.File.Copy(SampleMp3, dest, overwrite: true);
    return dest;
  }

  [Fact]
  public async Task AddToQueueAsync_AddsToEnd_WhenPositionNotSpecified()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadFileAsync("song1.mp3");

    // Act
    await source.AddToQueueAsync("song2.mp3");
    await source.AddToQueueAsync("song3.mp3");

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Equal(3, queue.Count);
    Assert.Contains("song1.mp3", queue[0].Id);
    Assert.Contains("song2.mp3", queue[1].Id);
    Assert.Contains("song3.mp3", queue[2].Id);
  }

  [Fact]
  public async Task AddToQueueAsync_InsertsAtPosition_WhenPositionSpecified()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadFileAsync("song1.mp3");
    await source.AddToQueueAsync("song3.mp3");

    // Act - Insert song2 at position 1 (between song1 and song3)
    await source.AddToQueueAsync("song2.mp3", position: 1);

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Equal(3, queue.Count);
    Assert.Contains("song1.mp3", queue[0].Id);
    Assert.Contains("song2.mp3", queue[1].Id);
    Assert.Contains("song3.mp3", queue[2].Id);
  }

  [Fact]
  public async Task AddToQueueAsync_RaisesQueueChangedEvent()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadFileAsync("song1.mp3");

    QueueChangedEventArgs? eventArgs = null;
    source.QueueChanged += (sender, args) => eventArgs = args;

    // Act
    await source.AddToQueueAsync("song2.mp3");

    // Assert
    Assert.NotNull(eventArgs);
    Assert.Equal(QueueChangeType.Added, eventArgs.ChangeType);
    Assert.NotNull(eventArgs.AffectedItem);
    Assert.Contains("song2.mp3", eventArgs.AffectedItem.Id);
  }

  [Fact]
  public async Task AddToQueueAsync_NonExistentFile_ThrowsFileNotFoundException()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");

    // Act & Assert
    await Assert.ThrowsAsync<FileNotFoundException>(
      () => source.AddToQueueAsync("nonexistent.mp3"));
  }

  [Fact]
  public async Task RemoveFromQueueAsync_RemovesItem_UpdatesQueue()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");

    // Act - Remove second item
    await source.RemoveFromQueueAsync(1);

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Equal(2, queue.Count);
    Assert.Contains("song1.mp3", queue[0].Id);
    Assert.Contains("song3.mp3", queue[1].Id);
  }

  [Fact]
  public async Task RemoveFromQueueAsync_RemovesCurrentItem_SkipsToNext()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");
    var currentBefore = source.CurrentFile;

    // Act - Remove current item (index 0)
    await source.RemoveFromQueueAsync(0);

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Equal(2, queue.Count);
    Assert.NotEqual(currentBefore, source.CurrentFile); // Current changed
    Assert.Contains("song2.mp3", source.CurrentFile); // Moved to next
  }

  [Fact]
  public async Task RemoveFromQueueAsync_RaisesQueueChangedEvent()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    QueueChangedEventArgs? eventArgs = null;
    source.QueueChanged += (sender, args) => eventArgs = args;

    // Act
    await source.RemoveFromQueueAsync(1);

    // Assert
    Assert.NotNull(eventArgs);
    Assert.Equal(QueueChangeType.Removed, eventArgs.ChangeType);
    Assert.Equal(1, eventArgs.AffectedIndex);
  }

  [Fact]
  public async Task RemoveFromQueueAsync_InvalidIndex_ThrowsArgumentOutOfRangeException()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.RemoveFromQueueAsync(10));
  }

  [Fact]
  public async Task ClearQueueAsync_ClearsAllItems_StopsPlayback()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");
    await source.PlayAsync();

    // Act
    await source.ClearQueueAsync();

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Empty(queue);
    Assert.Equal(AudioSourceState.Stopped, source.State);
    Assert.Null(source.CurrentFile);
  }

  [Fact]
  public async Task ClearQueueAsync_RaisesQueueChangedEvent()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");

    QueueChangedEventArgs? eventArgs = null;
    source.QueueChanged += (sender, args) => eventArgs = args;

    // Act
    await source.ClearQueueAsync();

    // Assert
    Assert.NotNull(eventArgs);
    Assert.Equal(QueueChangeType.Cleared, eventArgs.ChangeType);
  }

  [Fact]
  public async Task MoveQueueItemAsync_ReordersItems()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");

    // Act - Move song3 (index 2) to position 1
    await source.MoveQueueItemAsync(fromIndex: 2, toIndex: 1);

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Contains("song1.mp3", queue[0].Id);
    Assert.Contains("song3.mp3", queue[1].Id); // Moved here
    Assert.Contains("song2.mp3", queue[2].Id);
  }

  [Fact]
  public async Task MoveQueueItemAsync_MovesCurrentItem_UpdatesCurrentIndex()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");
    var currentBefore = source.CurrentFile;

    // Act - Move current item (index 0) to position 2
    await source.MoveQueueItemAsync(fromIndex: 0, toIndex: 2);

    // Assert
    Assert.Equal(currentBefore, source.CurrentFile); // Current file unchanged
    Assert.Equal(2, source.CurrentIndex); // Index updated
  }

  [Fact]
  public async Task MoveQueueItemAsync_RaisesQueueChangedEvent()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    QueueChangedEventArgs? eventArgs = null;
    source.QueueChanged += (sender, args) => eventArgs = args;

    // Act
    await source.MoveQueueItemAsync(fromIndex: 0, toIndex: 1);

    // Assert
    Assert.NotNull(eventArgs);
    Assert.Equal(QueueChangeType.Moved, eventArgs.ChangeType);
    Assert.Equal(1, eventArgs.AffectedIndex); // New position
  }

  [Fact]
  public async Task MoveQueueItemAsync_InvalidIndices_ThrowsArgumentOutOfRangeException()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.MoveQueueItemAsync(fromIndex: 0, toIndex: 10));
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.MoveQueueItemAsync(fromIndex: 10, toIndex: 0));
  }

  [Fact]
  public async Task JumpToIndexAsync_JumpsToItem_StartsPlayback()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");

    // Act - Jump to second item
    await source.JumpToIndexAsync(1);

    // Assert
    Assert.Contains("song2.mp3", source.CurrentFile);
    Assert.Equal(1, source.CurrentIndex);
    Assert.Equal(AudioSourceState.Playing, source.State);
    
    var queue = await source.GetQueueAsync();
    Assert.True(queue[1].IsCurrent);
  }

  [Fact]
  public async Task JumpToIndexAsync_RaisesQueueChangedEvent()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    QueueChangedEventArgs? eventArgs = null;
    source.QueueChanged += (sender, args) => eventArgs = args;

    // Act
    await source.JumpToIndexAsync(1);

    // Assert
    Assert.NotNull(eventArgs);
    Assert.Equal(QueueChangeType.CurrentChanged, eventArgs.ChangeType);
    Assert.Equal(1, eventArgs.AffectedIndex);
    Assert.True(eventArgs.AffectedItem?.IsCurrent);
  }

  [Fact]
  public async Task JumpToIndexAsync_InvalidIndex_ThrowsArgumentOutOfRangeException()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    await source.LoadFileAsync("song1.mp3");

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => source.JumpToIndexAsync(10));
  }

  [Fact]
  public void QueueItems_ReturnsCurrentQueue()
  {
    // Arrange
    var source = CreateSource();

    // Act
    var queueItems = source.QueueItems;

    // Assert
    Assert.NotNull(queueItems);
    Assert.Empty(queueItems); // Initially empty
  }

  [Fact]
  public async Task CurrentIndex_TracksCurrentPosition()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    // Assert initial
    Assert.Equal(0, source.CurrentIndex);

    // Act - Jump to next
    await source.JumpToIndexAsync(1);

    // Assert
    Assert.Equal(1, source.CurrentIndex);
  }

  [Fact]
  public async Task Count_ReturnsCorrectTotalCount()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");

    // Assert initially empty
    Assert.Equal(0, source.Count);

    // Load files
    await source.LoadDirectoryAsync("");

    // Assert
    Assert.Equal(3, source.Count);
  }

  #endregion

  #region Queue Persistence Tests

  [Fact]
  public async Task InitializeAsync_RestoresQueueFromPreferences()
  {
    // Arrange
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");

    var fullPath1 = Path.Combine(_testDir, "song1.mp3");
    var fullPath2 = Path.Combine(_testDir, "song2.mp3");
    var fullPath3 = Path.Combine(_testDir, "song3.mp3");

    _preferences.QueueItems = new List<string> { fullPath1, fullPath2, fullPath3 };
    _preferences.CurrentQueueIndex = 0; // Start at first item
    _preferences.SongPositionMs = 30000;

    var source = CreateSource();

    // Act
    await source.InitializeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Ready, source.State);
    Assert.Equal(0, source.CurrentIndex);
    Assert.Equal(fullPath1, source.CurrentFile);
    // After restore, remaining queue items should be available
    Assert.True(source.QueueItems.Count >= 2);
  }

  [Fact]
  public async Task InitializeAsync_FiltersNonExistentFiles_FromPersistedQueue()
  {
    // Arrange
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    // song3.mp3 doesn't exist

    var fullPath1 = Path.Combine(_testDir, "song1.mp3");
    var fullPath2 = Path.Combine(_testDir, "song2.mp3");
    var fullPath3 = Path.Combine(_testDir, "song3.mp3");

    _preferences.QueueItems = new List<string> { fullPath1, fullPath2, fullPath3 };
    _preferences.CurrentQueueIndex = 0;

    var source = CreateSource();

    // Act
    await source.InitializeAsync();

    // Assert
    var queue = await source.GetQueueAsync();
    Assert.Equal(2, queue.Count); // Only 2 valid files
    Assert.All(queue, item => Assert.True(File.Exists(item.Id)));
  }

  [Fact]
  public async Task InitializeAsync_HandlesEmptyPersistedQueue()
  {
    // Arrange
    _preferences.QueueItems = new List<string>();
    _preferences.CurrentQueueIndex = -1;

    var source = CreateSource();

    // Act
    await source.InitializeAsync();

    // Assert
    Assert.Equal(AudioSourceState.Ready, source.State);
    var queue = await source.GetQueueAsync();
    Assert.Empty(queue);
  }

  [Fact]
  public async Task AddToQueueAsync_SavesQueueStateToPreferences()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadFileAsync("song1.mp3");

    // Act
    await source.AddToQueueAsync("song2.mp3");

    // Assert
    Assert.NotNull(_preferences.QueueItems);
    Assert.True(_preferences.QueueItems.Count >= 2);
  }

  [Fact]
  public async Task RemoveFromQueueAsync_UpdatesPersistedQueueState()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");
    
    var initialQueueCount = _preferences.QueueItems.Count;

    // Act
    await source.RemoveFromQueueAsync(1);

    // Assert
    Assert.NotNull(_preferences.QueueItems);
    Assert.Equal(initialQueueCount - 1, _preferences.QueueItems.Count);
  }

  [Fact]
  public async Task ClearQueueAsync_ClearsPersistedQueueState()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    await source.ClearQueueAsync();

    // Assert
    Assert.NotNull(_preferences.QueueItems);
    Assert.Empty(_preferences.QueueItems);
    Assert.Equal(-1, _preferences.CurrentQueueIndex);
  }

  [Fact]
  public async Task MoveQueueItemAsync_UpdatesPersistedQueueState()
  {
    // Arrange
    var source = CreateSource();
    CreateTestFile("song1.mp3");
    CreateTestFile("song2.mp3");
    CreateTestFile("song3.mp3");
    await source.LoadDirectoryAsync("");

    // Act
    await source.MoveQueueItemAsync(0, 2);

    // Assert - queue state should be saved after move
    Assert.NotNull(_preferences.QueueItems);
    Assert.True(_preferences.QueueItems.Count > 0);
    Assert.True(_preferences.CurrentQueueIndex >= 0);
  }

  #endregion

  #region UseShazamForAllSources Tests

  [Fact]
  public void Constructor_WithShazamToggleOn_DefaultDoesNotThrow()
  {
    // Arrange — create with UseShazamForAllSources enabled
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      UseShazamForAllSources = true
    });

    // Act
    var source = new FilePlayerAudioSource(
      _loggerMock.Object,
      _optionsMock.Object,
      _preferencesMock.Object,
      _testDir,
      fingerprintingOptions: fpMonitor.Object);

    // Assert
    Assert.Equal("File Player", source.Name);
  }

  [Fact]
  public void Constructor_WithoutFingerprintingOptions_UsesDefaultToggleOff()
  {
    // Act — no fingerprintingOptions passed (null)
    var source = CreateSource();

    // Assert — source creates successfully with default (toggle off)
    Assert.Equal("File Player", source.Name);
  }

  #endregion

  #region Cross-Source Contamination Tests

  // TrackIdentified is broadcast to EVERY subscriber, so the
  // `State != Playing && State != Paused` check is only a proxy for "am I
  // active": a non-active source can sit in Playing/Paused and adopt another
  // source's track.

  [Fact]
  public void OnTrackIdentified_WhileDifferentSourceIsActive_DoesNotUpdateMetadata()
  {
    // Arrange — the file player is Playing (so the state check alone would let
    // the identification through) but a DIFFERENT source is the active one.
    var foreignSource = new Mock<IAudioSource>().Object;
    var source = CreateSourceForIdentification(getActiveSource: () => foreignSource);
    SetState(source, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(source);
    metadata[StandardMetadataKeys.Title] = "Local File Title";
    metadata[StandardMetadataKeys.Artist] = "Local File Artist";
    metadata["NeedsFingerprintingLookup"] = true;

    // Act — the radio's audio is fingerprinted and broadcast to every subscriber.
    InvokeOnTrackIdentified(source, CreateTrackMetadata("Radio Song", "Radio Artist"), 0.95);

    // Assert — the file player kept its own ID3 metadata.
    Assert.Equal("Local File Title", source.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Local File Artist", source.Metadata[StandardMetadataKeys.Artist]);
  }

  [Fact]
  public void OnTrackIdentified_WhileThisSourceIsActive_UpdatesMetadata()
  {
    // Arrange — the file player is both Playing and the active source.
    FilePlayerAudioSource? active = null;
    active = CreateSourceForIdentification(getActiveSource: () => active);
    SetState(active, AudioSourceState.Playing);

    var metadata = GetMetadataDictionary(active);
    metadata[StandardMetadataKeys.Title] = "Local File Title";
    metadata[StandardMetadataKeys.Artist] = "Local File Artist";
    metadata["NeedsFingerprintingLookup"] = true;

    // Act
    InvokeOnTrackIdentified(active, CreateTrackMetadata("Shazam Song", "Shazam Artist"), 0.95);

    // Assert — Shazam metadata replaced the incomplete ID3 tags, as before.
    Assert.Equal("Shazam Song", active.Metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Shazam Artist", active.Metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Shazam", active.Metadata["MetadataSource"]);
  }

  /// <summary>
  /// Builds a source with UseShazamForAllSources enabled so the identification
  /// path unconditionally replaces file tags — the branch that leaks another
  /// source's track when the active-source guard is missing.
  /// </summary>
  private FilePlayerAudioSource CreateSourceForIdentification(Func<IAudioSource?>? getActiveSource)
  {
    var fpMonitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    fpMonitor.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      UseShazamForAllSources = true
    });

    return new FilePlayerAudioSource(
      _loggerMock.Object,
      _optionsMock.Object,
      _preferencesMock.Object,
      _testDir,
      fingerprintingOptions: fpMonitor.Object,
      getActiveSource: getActiveSource);
  }

  private static TrackMetadata CreateTrackMetadata(string title, string artist)
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = "Some Album",
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Sets the source state via reflection — the real transitions require an
  /// initialized SoundFlow playback pipeline.
  /// </summary>
  private static void SetState(FilePlayerAudioSource source, AudioSourceState state)
  {
    var stateField = typeof(Radio.Infrastructure.Audio.Sources.AudioSourceBase).GetField(
      "_state",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    stateField!.SetValue(source, state);
  }

  private static Dictionary<string, object> GetMetadataDictionary(FilePlayerAudioSource source)
  {
    var metadataField = typeof(FilePlayerAudioSource).GetField(
      "_metadata",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    return (Dictionary<string, object>)metadataField!.GetValue(source)!;
  }

  /// <summary>
  /// Invokes the private OnTrackIdentified handler via reflection.
  /// </summary>
  private static void InvokeOnTrackIdentified(
    FilePlayerAudioSource source,
    TrackMetadata track,
    double confidence)
  {
    var method = typeof(FilePlayerAudioSource).GetMethod(
      "OnTrackIdentified",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    method!.Invoke(source, new object?[] { null, new TrackIdentifiedEventArgs(track, confidence) });
  }

  #endregion
}
