using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.Fingerprinting;
using Radio.Fingerprinting.Abstractions;
using Radio.Fingerprinting.Data;

namespace Radio.Fingerprinting.Tests.Data;

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
      MetadataSource = MetadataSource.Manual
    };

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(MetadataSource.Manual, recorded.MetadataSource);
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
    var fileEntry = CreateTestHistoryEntry() with { Source = PlaySource.File };

    await _repository.RecordPlayAsync(vinylEntry1);
    await _repository.RecordPlayAsync(vinylEntry2);
    await _repository.RecordPlayAsync(radioEntry);
    await _repository.RecordPlayAsync(fileEntry);

    // Act
    var vinylResults = await _repository.GetBySourceAsync(PlaySource.Vinyl, 10);
    var radioResults = await _repository.GetBySourceAsync(PlaySource.Radio, 10);
    var fileResults = await _repository.GetBySourceAsync(PlaySource.File, 10);

    // Assert
    Assert.Equal(2, vinylResults.Count);
    Assert.All(vinylResults, e => Assert.Equal(PlaySource.Vinyl, e.Source));
    Assert.Single(radioResults);
    Assert.Single(fileResults);
    Assert.Equal(PlaySource.File, fileResults[0].Source);
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
      Source = MetadataSource.Manual,
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
      Source = MetadataSource.Manual,
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

  [Fact]
  public async Task FinalizeEntryAsync_SetsEndedAtAndDuration()
  {
    // Arrange
    var playedAt = DateTime.UtcNow.AddMinutes(-5);
    var entry = CreateTestHistoryEntry() with
    {
      PlayedAt = playedAt,
      DurationSeconds = null
    };
    await _repository.RecordPlayAsync(entry);

    var endedAt = DateTime.UtcNow;

    // Act
    var result = await _repository.FinalizeEntryAsync(entry.Id, endedAt);

    // Assert
    Assert.True(result);
    var finalized = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(finalized);
    Assert.NotNull(finalized.EndedAt);
    Assert.NotNull(finalized.DurationSeconds);
    // Duration should be approximately 5 minutes (300 seconds)
    Assert.InRange(finalized.DurationSeconds.Value, 295, 305);
  }

  [Fact]
  public async Task FinalizeEntryAsync_NonExistingId_ReturnsFalse()
  {
    // Act
    var result = await _repository.FinalizeEntryAsync("nonexistent-id", DateTime.UtcNow);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task RecordPlayAsync_WithEndedAt_PersistsCorrectly()
  {
    // Arrange
    var endedAt = DateTime.UtcNow.AddMinutes(3);
    var entry = CreateTestHistoryEntry() with
    {
      EndedAt = endedAt
    };

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.NotNull(recorded.EndedAt);
    // Compare UTC values within 1 second tolerance (datetime serialization precision)
    Assert.InRange((recorded.EndedAt.Value.ToUniversalTime() - endedAt).TotalSeconds, -1, 1);
  }

  [Fact]
  public async Task RecordPlayAsync_WithoutEndedAt_PersistsNull()
  {
    // Arrange
    var entry = CreateTestHistoryEntry();

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Null(recorded.EndedAt);
  }

  [Fact]
  public async Task CloseOrphanedEntriesAsync_ClosesOldOrphanedEntries()
  {
    // Arrange — create an orphaned entry (no EndedAt) from 10 minutes ago
    var entry = CreateTestHistoryEntry() with
    {
      PlayedAt = DateTime.UtcNow.AddMinutes(-10),
      EndedAt = null,
      DurationSeconds = null
    };
    await _repository.RecordPlayAsync(entry);

    // Act — close entries older than 2 minutes
    var closed = await _repository.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2));

    // Assert
    Assert.Equal(1, closed);
    var updated = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(updated);
    Assert.NotNull(updated.EndedAt);
    Assert.NotNull(updated.DurationSeconds);
    Assert.True(updated.DurationSeconds > 0);
  }

  [Fact]
  public async Task CloseOrphanedEntriesAsync_PreservesDurationWhenAlreadySet()
  {
    // Arrange — orphaned entry with Duration already set (e.g. from AVRCP)
    var entry = CreateTestHistoryEntry() with
    {
      PlayedAt = DateTime.UtcNow.AddMinutes(-10),
      EndedAt = null,
      DurationSeconds = 180 // 3 minutes from AVRCP
    };
    await _repository.RecordPlayAsync(entry);

    // Act
    var closed = await _repository.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2));

    // Assert — Duration should be preserved at 180
    Assert.Equal(1, closed);
    var updated = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(updated);
    Assert.NotNull(updated.EndedAt);
    Assert.Equal(180, updated.DurationSeconds);
  }

  [Fact]
  public async Task CloseOrphanedEntriesAsync_DoesNotCloseRecentEntries()
  {
    // Arrange — orphaned entry from 30 seconds ago (within threshold)
    var entry = CreateTestHistoryEntry() with
    {
      PlayedAt = DateTime.UtcNow.AddSeconds(-30),
      EndedAt = null,
      DurationSeconds = null
    };
    await _repository.RecordPlayAsync(entry);

    // Act — close entries older than 2 minutes
    var closed = await _repository.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2));

    // Assert — should NOT close recent entries
    Assert.Equal(0, closed);
    var unchanged = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(unchanged);
    Assert.Null(unchanged.EndedAt);
  }

  [Fact]
  public async Task CloseOrphanedEntriesAsync_DoesNotCloseAlreadyFinalizedEntries()
  {
    // Arrange — entry that already has EndedAt set
    var entry = CreateTestHistoryEntry() with
    {
      PlayedAt = DateTime.UtcNow.AddMinutes(-10),
      EndedAt = DateTime.UtcNow.AddMinutes(-5),
      DurationSeconds = 300
    };
    await _repository.RecordPlayAsync(entry);

    // Act
    var closed = await _repository.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2));

    // Assert — already-finalized entries should not be touched
    Assert.Equal(0, closed);
  }

  [Fact]
  public async Task CloseOrphanedEntriesAsync_ReturnsZeroWhenNoOrphans()
  {
    // Act — nothing in the database
    var closed = await _repository.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2));

    // Assert
    Assert.Equal(0, closed);
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
