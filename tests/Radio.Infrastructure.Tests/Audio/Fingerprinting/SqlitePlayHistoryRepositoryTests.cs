using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// Unit tests for the SqlitePlayHistoryRepository class.
/// </summary>
public class SqlitePlayHistoryRepositoryTests : IAsyncLifetime
{
  private readonly Mock<ILogger<SqlitePlayHistoryRepository>> _loggerMock;
  private readonly Mock<ILogger<FingerprintDbContext>> _dbLoggerMock;
  private readonly Mock<ILogger<SqliteTrackMetadataRepository>> _metadataLoggerMock;
  private readonly FingerprintingOptions _options;
  private readonly FingerprintDbContext _dbContext;
  private readonly SqlitePlayHistoryRepository _repository;
  private readonly SqliteTrackMetadataRepository _metadataRepository;
  private readonly string _testDbPath;

  public SqlitePlayHistoryRepositoryTests()
  {
    _loggerMock = new Mock<ILogger<SqlitePlayHistoryRepository>>();
    _dbLoggerMock = new Mock<ILogger<FingerprintDbContext>>();
    _metadataLoggerMock = new Mock<ILogger<SqliteTrackMetadataRepository>>();

    _testDbPath = Path.Combine(Path.GetTempPath(), $"test-history-{Guid.NewGuid()}.db");
    _options = new FingerprintingOptions
    {
      DatabasePath = _testDbPath
    };

    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = Path.GetDirectoryName(_testDbPath)!,
      FingerprintingSubdirectory = "",
      FingerprintingFileName = Path.GetFileName(_testDbPath)
    });
    var pathResolver = new DatabasePathResolver(databaseOptions);

    _dbContext = new FingerprintDbContext(_dbLoggerMock.Object, pathResolver);
    _repository = new SqlitePlayHistoryRepository(_loggerMock.Object, _dbContext);
    _metadataRepository = new SqliteTrackMetadataRepository(_metadataLoggerMock.Object, _dbContext);
  }

  public async Task InitializeAsync()
  {
    await _dbContext.InitializeAsync();
  }

  public async Task DisposeAsync()
  {
    await _dbContext.DisposeAsync();
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    if (File.Exists(_testDbPath))
    {
      try
      {
        File.Delete(_testDbPath);
      }
      catch (IOException)
      {
        await Task.Delay(50);
        if (File.Exists(_testDbPath))
        {
          File.Delete(_testDbPath);
        }
      }
    }
  }

  [Fact]
  public async Task RecordPlayAsync_CreatesHistoryEntry()
  {
    // Arrange
    var entry = CreateTestHistoryEntry();

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(entry.Id, recorded.Id);
    Assert.Equal(entry.Source, recorded.Source);
    Assert.Equal(entry.WasIdentified, recorded.WasIdentified);
  }

  [Fact]
  public async Task RecordPlayAsync_WithMetadataSource_PersistsCorrectly()
  {
    // Arrange
    var entry = CreateTestHistoryEntry() with
    {
      MetadataSource = MetadataSource.Spotify
    };

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(MetadataSource.Spotify, recorded.MetadataSource);
  }

  [Fact]
  public async Task RecordPlayAsync_WithFileTagMetadataSource_PersistsCorrectly()
  {
    // Arrange
    var entry = CreateTestHistoryEntry() with
    {
      MetadataSource = MetadataSource.FileTag
    };

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(MetadataSource.FileTag, recorded.MetadataSource);
  }

  [Fact]
  public async Task RecordPlayAsync_WithFingerprintingMetadataSource_PersistsCorrectly()
  {
    // Arrange
    var entry = CreateTestHistoryEntry() with
    {
      MetadataSource = MetadataSource.Fingerprinting
    };

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(MetadataSource.Fingerprinting, recorded.MetadataSource);
  }

  [Fact]
  public async Task GetRecentAsync_ReturnsLatestEntries()
  {
    // Arrange
    var entries = new List<PlayHistoryEntry>();
    for (int i = 0; i < 5; i++)
    {
      var entry = CreateTestHistoryEntry();
      entry = entry with { PlayedAt = DateTime.UtcNow.AddMinutes(-i) };
      entries.Add(entry);
      await _repository.RecordPlayAsync(entry);
    }

    // Act
    var recent = await _repository.GetRecentAsync(3);

    // Assert
    Assert.Equal(3, recent.Count);
    // Verify they're in descending order by PlayedAt
    Assert.True(recent[0].PlayedAt >= recent[1].PlayedAt);
    Assert.True(recent[1].PlayedAt >= recent[2].PlayedAt);
  }

  [Fact]
  public async Task GetByDateRangeAsync_ReturnsEntriesInRange()
  {
    // Arrange
    var now = DateTime.UtcNow;
    var entry1 = CreateTestHistoryEntry() with { PlayedAt = now.AddHours(-1) };
    var entry2 = CreateTestHistoryEntry() with { PlayedAt = now.AddHours(-2) };
    var entry3 = CreateTestHistoryEntry() with { PlayedAt = now.AddHours(-5) };

    await _repository.RecordPlayAsync(entry1);
    await _repository.RecordPlayAsync(entry2);
    await _repository.RecordPlayAsync(entry3);

    // Act
    var results = await _repository.GetByDateRangeAsync(
      now.AddHours(-3),
      now);

    // Assert
    Assert.Equal(2, results.Count);
  }

  [Fact]
  public async Task GetBySourceAsync_ReturnsEntriesForSpecificSource()
  {
    // Arrange
    var vinylEntry1 = CreateTestHistoryEntry() with { Source = PlaySource.Vinyl };
    var vinylEntry2 = CreateTestHistoryEntry() with { Source = PlaySource.Vinyl };
    var radioEntry = CreateTestHistoryEntry() with { Source = PlaySource.Radio };
    var spotifyEntry = CreateTestHistoryEntry() with { Source = PlaySource.Spotify };

    await _repository.RecordPlayAsync(vinylEntry1);
    await _repository.RecordPlayAsync(vinylEntry2);
    await _repository.RecordPlayAsync(radioEntry);
    await _repository.RecordPlayAsync(spotifyEntry);

    // Act
    var vinylResults = await _repository.GetBySourceAsync(PlaySource.Vinyl, 10);
    var radioResults = await _repository.GetBySourceAsync(PlaySource.Radio, 10);
    var spotifyResults = await _repository.GetBySourceAsync(PlaySource.Spotify, 10);

    // Assert
    Assert.Equal(2, vinylResults.Count);
    Assert.All(vinylResults, e => Assert.Equal(PlaySource.Vinyl, e.Source));
    Assert.Single(radioResults);
    Assert.Single(spotifyResults);
    Assert.Equal(PlaySource.Spotify, spotifyResults[0].Source);
  }

  [Fact]
  public async Task ExistsRecentlyPlayedAsync_WithRecentMatch_ReturnsTrue()
  {
    // Arrange
    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Test Song",
      Artist = "Test Artist",
      Source = MetadataSource.Spotify,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata);

    var entry = CreateTestHistoryEntry() with
    {
      TrackMetadataId = metadata.Id,
      PlayedAt = DateTime.UtcNow.AddMinutes(-2)
    };
    await _repository.RecordPlayAsync(entry);

    // Act
    var exists = await _repository.ExistsRecentlyPlayedAsync("Test Song", "Test Artist", 5);

    // Assert
    Assert.True(exists);
  }

  [Fact]
  public async Task ExistsRecentlyPlayedAsync_WithOldMatch_ReturnsFalse()
  {
    // Arrange
    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Old Song",
      Artist = "Old Artist",
      Source = MetadataSource.Spotify,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata);

    var entry = CreateTestHistoryEntry() with
    {
      TrackMetadataId = metadata.Id,
      PlayedAt = DateTime.UtcNow.AddMinutes(-10)
    };
    await _repository.RecordPlayAsync(entry);

    // Act
    var exists = await _repository.ExistsRecentlyPlayedAsync("Old Song", "Old Artist", 5);

    // Assert
    Assert.False(exists);
  }

  [Fact]
  public async Task ExistsRecentlyPlayedAsync_WithNoMatch_ReturnsFalse()
  {
    // Act
    var exists = await _repository.ExistsRecentlyPlayedAsync("Nonexistent Song", "Unknown Artist", 5);

    // Assert
    Assert.False(exists);
  }

  [Fact]
  public async Task GetStatisticsAsync_WithEmptyDatabase_ReturnsZeroStatistics()
  {
    // Act - Call GetStatisticsAsync on empty database
    var stats = await _repository.GetStatisticsAsync();

    // Assert - Should handle NULL aggregates and return zeros, not throw
    Assert.Equal(0, stats.TotalPlays);
    Assert.Equal(0, stats.IdentifiedPlays);
    Assert.Equal(0, stats.UnidentifiedPlays);
    Assert.Empty(stats.PlaysBySource);
    Assert.Empty(stats.TopArtists);
    Assert.Empty(stats.TopTracks);
  }

  [Fact]
  public async Task GetStatisticsAsync_ReturnsCorrectStatistics()
  {
    // Arrange
    var entry1 = CreateTestHistoryEntry() with
    {
      WasIdentified = true,
      Source = PlaySource.Vinyl
    };
    var entry2 = CreateTestHistoryEntry() with
    {
      WasIdentified = false,
      Source = PlaySource.Radio
    };
    var entry3 = CreateTestHistoryEntry() with
    {
      WasIdentified = true,
      Source = PlaySource.Vinyl
    };

    await _repository.RecordPlayAsync(entry1);
    await _repository.RecordPlayAsync(entry2);
    await _repository.RecordPlayAsync(entry3);

    // Act
    var stats = await _repository.GetStatisticsAsync();

    // Assert
    Assert.Equal(3, stats.TotalPlays);
    Assert.Equal(2, stats.IdentifiedPlays);
    Assert.Equal(1, stats.UnidentifiedPlays);
    Assert.Equal(2, stats.PlaysBySource[PlaySource.Vinyl]);
    Assert.Equal(1, stats.PlaysBySource[PlaySource.Radio]);
  }

  [Fact]
  public async Task DeleteAsync_ExistingEntry_DeletesAndReturnsTrue()
  {
    // Arrange
    var entry = CreateTestHistoryEntry();
    await _repository.RecordPlayAsync(entry);

    // Act
    var deleted = await _repository.DeleteAsync(entry.Id);
    var found = await _repository.GetByIdAsync(entry.Id);

    // Assert
    Assert.True(deleted);
    Assert.Null(found);
  }

  [Fact]
  public async Task DeleteAsync_NonExistingEntry_ReturnsFalse()
  {
    // Act
    var deleted = await _repository.DeleteAsync("nonexistent-id");

    // Assert
    Assert.False(deleted);
  }

  [Fact]
  public async Task GetByIdAsync_NonExistingId_ReturnsNull()
  {
    // Act
    var result = await _repository.GetByIdAsync("nonexistent-id");

    // Assert
    Assert.Null(result);
  }

  private static PlayHistoryEntry CreateTestHistoryEntry()
  {
    return new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Vinyl,
      SourceDetails = "Test Source",
      DurationSeconds = 15,
      IdentificationConfidence = 0.85,
      WasIdentified = true
    };
  }
}
