using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;

namespace Radio.IntegrationTests.PlayHistory;

/// <summary>
/// Integration tests for play history recording functionality.
/// Tests the complete play history flow including storage and retrieval.
/// </summary>
public class PlayHistoryRecordingIntegrationTests : IAsyncLifetime
{
  private readonly string _tempDirectory;
  private readonly Mock<ILogger<SqlitePlayHistoryRepository>> _loggerMock;
  private readonly Mock<ILogger<FingerprintDbContext>> _dbLoggerMock;
  private readonly Mock<ILogger<SqliteTrackMetadataRepository>> _metadataLoggerMock;
  private FingerprintDbContext _dbContext = null!;
  private SqlitePlayHistoryRepository _repository = null!;
  private SqliteTrackMetadataRepository _metadataRepository = null!;
  private string _testDbPath = null!;

  public PlayHistoryRecordingIntegrationTests()
  {
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"PlayHistoryTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDirectory);

    _loggerMock = new Mock<ILogger<SqlitePlayHistoryRepository>>();
    _dbLoggerMock = new Mock<ILogger<FingerprintDbContext>>();
    _metadataLoggerMock = new Mock<ILogger<SqliteTrackMetadataRepository>>();
  }

  public async Task InitializeAsync()
  {
    _testDbPath = Path.Combine(_tempDirectory, "test-history.db");
    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = _tempDirectory,
      FingerprintingSubdirectory = "",
      FingerprintingFileName = "test-history.db"
    });
    var pathResolver = new DatabasePathResolver(databaseOptions);

    _dbContext = new FingerprintDbContext(_dbLoggerMock.Object, pathResolver);
    _repository = new SqlitePlayHistoryRepository(_loggerMock.Object, _dbContext);
    _metadataRepository = new SqliteTrackMetadataRepository(_metadataLoggerMock.Object, _dbContext);

    await _dbContext.InitializeAsync();
  }

  public async Task DisposeAsync()
  {
    await _dbContext.DisposeAsync();
    SqliteConnection.ClearAllPools();

    try
    {
      if (Directory.Exists(_tempDirectory))
      {
        Directory.Delete(_tempDirectory, recursive: true);
      }
    }
    catch (IOException)
    {
      // Ignore cleanup errors
    }
  }

  [Fact]
  public async Task FilePlayerSource_OnPlay_RecordsHistoryEntry()
  {
    // Arrange - Simulate what happens when a file starts playing
    var entry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.File,
      SourceDetails = "/music/test-song.mp3",
      WasIdentified = false
    };

    // Act - Record the play start
    await _repository.RecordPlayAsync(entry);

    // Assert - Entry should exist in the database
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(entry.Id, recorded.Id);
    Assert.Equal(PlaySource.File, recorded.Source);
    Assert.Equal("/music/test-song.mp3", recorded.SourceDetails);
    Assert.False(recorded.WasIdentified);
  }

  [Fact]
  public async Task PlayHistoryEntry_WithMetadata_StoresCorrectSource()
  {
    // Arrange - Create track metadata first
    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Test Song",
      Artist = "Test Artist",
      Album = "Test Album",
      Source = MetadataSource.FileTag,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata);

    // Create play history entry with metadata reference
    var entry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = metadata.Id,
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.File,
      MetadataSource = MetadataSource.FileTag,
      SourceDetails = "File: Test Song - Test Artist",
      WasIdentified = true,
      IdentificationConfidence = 1.0
    };

    // Act
    await _repository.RecordPlayAsync(entry);

    // Assert
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.Equal(MetadataSource.FileTag, recorded.MetadataSource);
    Assert.Equal(metadata.Id, recorded.TrackMetadataId);
    Assert.True(recorded.WasIdentified);
  }

  [Fact]
  public async Task PlayHistory_UpdateWithFingerprinting_UpdatesEntry()
  {
    // Arrange - First, record an unidentified play
    var entry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Radio,
      SourceDetails = "FM 101.5 MHz",
      WasIdentified = false
    };
    await _repository.RecordPlayAsync(entry);

    // Create track metadata from fingerprinting
    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Identified Song",
      Artist = "Identified Artist",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata);

    // Act - Update the entry with fingerprinting results
    var updatedEntry = entry with
    {
      TrackMetadataId = metadata.Id,
      MetadataSource = MetadataSource.Fingerprinting,
      WasIdentified = true,
      IdentificationConfidence = 0.92
    };
    var updated = await _repository.UpdateAsync(updatedEntry);

    // Assert
    Assert.True(updated);
    var recorded = await _repository.GetByIdAsync(entry.Id);
    Assert.NotNull(recorded);
    Assert.True(recorded.WasIdentified);
    Assert.Equal(MetadataSource.Fingerprinting, recorded.MetadataSource);
    Assert.Equal(0.92, recorded.IdentificationConfidence);
    Assert.Equal(metadata.Id, recorded.TrackMetadataId);
  }

  [Fact]
  public async Task GetRecentUnidentified_FindsEntries()
  {
    // Arrange - Create several entries with different identification states
    var identifiedEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddMinutes(-1),
      Source = PlaySource.File,
      WasIdentified = true
    };
    await _repository.RecordPlayAsync(identifiedEntry);

    var unidentifiedEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddMinutes(-2),
      Source = PlaySource.File,
      WasIdentified = false
    };
    await _repository.RecordPlayAsync(unidentifiedEntry);

    var oldUnidentifiedEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddMinutes(-10),
      Source = PlaySource.File,
      WasIdentified = false
    };
    await _repository.RecordPlayAsync(oldUnidentifiedEntry);

    // Act
    var recent = await _repository.GetRecentUnidentifiedAsync(PlaySource.File, withinMinutes: 5);

    // Assert - Should find the recent unidentified entry but not the old one
    Assert.NotNull(recent);
    Assert.Equal(unidentifiedEntry.Id, recent.Id);
    Assert.False(recent.WasIdentified);
  }

  [Fact]
  public async Task GetBySourceAsync_FiltersCorrectly()
  {
    // Arrange - Create entries from different sources
    var fileEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.File,
      WasIdentified = false
    };
    await _repository.RecordPlayAsync(fileEntry);

    var radioEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Radio,
      WasIdentified = false
    };
    await _repository.RecordPlayAsync(radioEntry);

    var vinylEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Vinyl,
      WasIdentified = true
    };
    await _repository.RecordPlayAsync(vinylEntry);

    // Act
    var fileResults = await _repository.GetBySourceAsync(PlaySource.File, 10);
    var radioResults = await _repository.GetBySourceAsync(PlaySource.Radio, 10);
    var vinylResults = await _repository.GetBySourceAsync(PlaySource.Vinyl, 10);

    // Assert
    Assert.Single(fileResults);
    Assert.Equal(fileEntry.Id, fileResults[0].Id);

    Assert.Single(radioResults);
    Assert.Equal(radioEntry.Id, radioResults[0].Id);

    Assert.Single(vinylResults);
    Assert.Equal(vinylEntry.Id, vinylResults[0].Id);
  }

  [Fact]
  public async Task SearchAsync_FindsMatchingEntries()
  {
    // Arrange - Create track metadata
    var metadata1 = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Bohemian Rhapsody",
      Artist = "Queen",
      Album = "A Night at the Opera",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata1);

    var metadata2 = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Another One Bites the Dust",
      Artist = "Queen",
      Album = "The Game",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata2);

    var metadata3 = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Stairway to Heaven",
      Artist = "Led Zeppelin",
      Album = "Led Zeppelin IV",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata3);

    // Create history entries
    var entry1 = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = metadata1.Id,
      PlayedAt = DateTime.UtcNow.AddHours(-1),
      Source = PlaySource.Radio,
      WasIdentified = true
    };
    await _repository.RecordPlayAsync(entry1);

    var entry2 = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = metadata2.Id,
      PlayedAt = DateTime.UtcNow.AddHours(-2),
      Source = PlaySource.Radio,
      WasIdentified = true
    };
    await _repository.RecordPlayAsync(entry2);

    var entry3 = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = metadata3.Id,
      PlayedAt = DateTime.UtcNow.AddHours(-3),
      Source = PlaySource.Radio,
      WasIdentified = true
    };
    await _repository.RecordPlayAsync(entry3);

    // Act - Search for "Queen"
    var (queenResults, queenTotal) = await _repository.SearchAsync("Queen");

    // Assert
    Assert.Equal(2, queenTotal);
    Assert.Equal(2, queenResults.Count);

    // Act - Search for "Bohemian"
    var (bohemianResults, bohemianTotal) = await _repository.SearchAsync("Bohemian");

    // Assert
    Assert.Equal(1, bohemianTotal);
    Assert.Single(bohemianResults);
    Assert.Equal(entry1.Id, bohemianResults[0].Id);
  }

  [Fact]
  public async Task GetStatisticsAsync_ReturnsAccurateStats()
  {
    // Arrange
    await _repository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddHours(-1),
      Source = PlaySource.File,
      WasIdentified = true
    });

    await _repository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddHours(-2),
      Source = PlaySource.Radio,
      WasIdentified = false
    });

    await _repository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddHours(-3),
      Source = PlaySource.Vinyl,
      WasIdentified = true
    });

    await _repository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddHours(-4),
      Source = PlaySource.File,
      WasIdentified = false
    });

    // Act
    var stats = await _repository.GetStatisticsAsync();

    // Assert
    Assert.Equal(4, stats.TotalPlays);
    Assert.Equal(2, stats.IdentifiedPlays);
    Assert.Equal(2, stats.UnidentifiedPlays);
    Assert.Equal(2, stats.PlaysBySource[PlaySource.File]);
    Assert.Equal(1, stats.PlaysBySource[PlaySource.Radio]);
    Assert.Equal(1, stats.PlaysBySource[PlaySource.Vinyl]);
  }

  [Fact]
  public async Task DeleteAsync_RemovesEntry()
  {
    // Arrange
    var entry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.File,
      WasIdentified = false
    };
    await _repository.RecordPlayAsync(entry);

    // Act
    var deleted = await _repository.DeleteAsync(entry.Id);

    // Assert
    Assert.True(deleted);
    var found = await _repository.GetByIdAsync(entry.Id);
    Assert.Null(found);
  }
}
