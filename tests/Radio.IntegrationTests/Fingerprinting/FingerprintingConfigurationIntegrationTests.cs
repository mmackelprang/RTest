using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.IntegrationTests.TestSupport;

namespace Radio.IntegrationTests.Fingerprinting;

/// <summary>
/// Integration tests for fingerprinting system configuration and database setup.
/// </summary>
public class FingerprintingConfigurationIntegrationTests : IAsyncLifetime
{
  private readonly string _tempDirectory;
  private readonly Mock<ILogger<FingerprintDbContext>> _dbLoggerMock;
  private readonly Mock<ILogger<SqliteFingerprintCacheRepository>> _cacheLoggerMock;
  private readonly Mock<ILogger<SqliteTrackMetadataRepository>> _metadataLoggerMock;
  private FingerprintDbContext _dbContext = null!;
  private SqliteFingerprintCacheRepository _cacheRepository = null!;
  private SqliteTrackMetadataRepository _metadataRepository = null!;

  public FingerprintingConfigurationIntegrationTests()
  {
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"FingerprintTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDirectory);

    _dbLoggerMock = new Mock<ILogger<FingerprintDbContext>>();
    _cacheLoggerMock = new Mock<ILogger<SqliteFingerprintCacheRepository>>();
    _metadataLoggerMock = new Mock<ILogger<SqliteTrackMetadataRepository>>();
  }

  public async Task InitializeAsync()
  {
    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = _tempDirectory,
      FingerprintingSubdirectory = "",
      FingerprintingFileName = "fingerprints.db"
    });
    var pathResolver = new DatabasePathResolver(databaseOptions);

    _dbContext = new FingerprintDbContext(_dbLoggerMock.Object, pathResolver);
    await _dbContext.InitializeAsync();

    _cacheRepository = new SqliteFingerprintCacheRepository(_cacheLoggerMock.Object, _dbContext);
    _metadataRepository = new SqliteTrackMetadataRepository(_metadataLoggerMock.Object, _dbContext);
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
  public async Task FingerprintDbContext_Initialize_CreatesTables()
  {
    // Arrange - tables should already be created in InitializeAsync

    // Act - Get a connection and verify tables exist
    var connection = await _dbContext.GetConnectionAsync();

    using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
      SELECT name FROM sqlite_master
      WHERE type='table'
      ORDER BY name";
    var tables = new List<string>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      tables.Add(reader.GetString(0));
    }

    // Assert - verify core tables exist
    Assert.Contains("FingerprintCache", tables);
    Assert.Contains("TrackMetadata", tables);
    Assert.Contains("PlayHistory", tables);
    Assert.Contains("RadioPresets", tables);
    Assert.Contains("AudioFiles", tables);
  }

  [Fact]
  public async Task FingerprintDbContext_Initialize_CreatesIndexes()
  {
    // Arrange
    var connection = await _dbContext.GetConnectionAsync();

    // Act
    using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
      SELECT name FROM sqlite_master
      WHERE type='index' AND name LIKE 'IX_%'
      ORDER BY name";
    var indexes = new List<string>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      indexes.Add(reader.GetString(0));
    }

    // Assert - verify key indexes exist
    Assert.Contains("IX_FingerprintCache_ChromaprintHash", indexes);
    Assert.Contains("IX_TrackMetadata_Artist", indexes);
    Assert.Contains("IX_PlayHistory_PlayedAt", indexes);
  }

  [Fact]
  public async Task FingerprintCacheRepository_StoreAndRetrieve_Works()
  {
    // Arrange
    var fingerprintId = Guid.NewGuid().ToString();
    var chromaprintHash = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

    var fingerprintData = new FingerprintData
    {
      Id = fingerprintId,
      ChromaprintHash = chromaprintHash,
      DurationSeconds = 30,
      GeneratedAt = DateTime.UtcNow
    };

    // Act - Store fingerprint
    var stored = await _cacheRepository.StoreAsync(fingerprintData, metadata: null);

    // Retrieve by hash
    var byHash = await _cacheRepository.FindByHashAsync(chromaprintHash);

    // Assert
    Assert.NotNull(stored);
    Assert.Equal(fingerprintId, stored.Id);
    Assert.Equal(chromaprintHash, stored.ChromaprintHash);
    Assert.Equal(30, stored.DurationSeconds);

    Assert.NotNull(byHash);
    Assert.Equal(fingerprintId, byHash.Id);
  }

  [Fact]
  public async Task TrackMetadataRepository_StoreAndSearch_Works()
  {
    // Arrange
    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Bohemian Rhapsody",
      Artist = "Queen",
      Album = "A Night at the Opera",
      ReleaseYear = 1975,
      Genre = "Rock",
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    // Act - Store
    await _metadataRepository.StoreAsync(metadata);

    // Retrieve by ID
    var byId = await _metadataRepository.GetByIdAsync(metadata.Id);

    // Search by artist
    var searchResults = await _metadataRepository.SearchAsync("Queen", limit: 10);

    // Assert
    Assert.NotNull(byId);
    Assert.Equal("Bohemian Rhapsody", byId.Title);
    Assert.Equal("Queen", byId.Artist);
    Assert.Equal(1975, byId.ReleaseYear);

    Assert.NotEmpty(searchResults);
    Assert.Contains(searchResults, m => m.Title == "Bohemian Rhapsody");
  }

  [Fact]
  public async Task TrackMetadataRepository_StoreWithFingerprint_LinksProperly()
  {
    // Arrange - First store a fingerprint
    var fingerprintId = Guid.NewGuid().ToString();
    var chromaprintHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

    var fingerprintData = new FingerprintData
    {
      Id = fingerprintId,
      ChromaprintHash = chromaprintHash,
      DurationSeconds = 30,
      GeneratedAt = DateTime.UtcNow
    };

    await _cacheRepository.StoreAsync(fingerprintData, metadata: null);

    // Create metadata linked to fingerprint
    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      FingerprintId = fingerprintId,
      Title = "Test Track",
      Artist = "Test Artist",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    // Act
    await _metadataRepository.StoreAsync(metadata);
    var retrieved = await _metadataRepository.GetByIdAsync(metadata.Id);

    // Assert
    Assert.NotNull(retrieved);
    Assert.Equal(fingerprintId, retrieved.FingerprintId);
  }

  [Fact]
  public async Task MockAudioSampleProvider_CaptureAudio_ProducesValidData()
  {
    // Arrange
    var audioProvider = new MockAudioSampleProvider();
    audioProvider.SetActive(true, "TestAudio", PlaySource.File);

    // Act - Capture audio samples
    var samples = await audioProvider.CaptureAsync(TimeSpan.FromSeconds(10));

    // Assert
    Assert.NotNull(samples);
    Assert.Equal(10, samples.Duration.TotalSeconds);
    Assert.True(samples.Samples.Length > 0);
  }

  [Fact]
  public async Task MockMetadataLookupService_CoverArtSearch_ReturnsNull()
  {
    // Arrange
    var lookupService = new MockMetadataLookupService();

    // Act
    var result = await lookupService.SearchCoverArtByTextAsync("Test", "Artist");

    // Assert — mock returns null by default
    Assert.Null(result);
  }

  [Fact]
  public async Task MockMetadataLookupService_CoverArtSearch_ReturnsConfiguredUrl()
  {
    // Arrange
    var lookupService = new MockMetadataLookupService();
    lookupService.CoverArtUrl = "https://example.com/cover.jpg";

    // Act
    var result = await lookupService.SearchCoverArtByTextAsync("Test", "Artist");

    // Assert
    Assert.Equal("https://example.com/cover.jpg", result);
  }

  [Fact]
  public async Task FingerprintDbContext_MultipleInitializeCalls_OnlyInitializesOnce()
  {
    // Act - Call initialize multiple times
    await _dbContext.InitializeAsync();
    await _dbContext.InitializeAsync();
    await _dbContext.InitializeAsync();

    // Assert - Should not throw and should work correctly
    var connection = await _dbContext.GetConnectionAsync();
    Assert.NotNull(connection);
  }

  [Fact]
  public async Task TrackMetadataRepository_AllMetadataSources_PersistCorrectly()
  {
    // Arrange & Act - Store metadata with each source type
    var sources = new[]
    {
      MetadataSource.Shazam,
      MetadataSource.Manual,
      MetadataSource.FileTag,
      MetadataSource.Fingerprinting
    };

    foreach (var source in sources)
    {
      var metadata = new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = $"Track from {source}",
        Artist = "Test Artist",
        Source = source,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };
      await _metadataRepository.StoreAsync(metadata);

      var retrieved = await _metadataRepository.GetByIdAsync(metadata.Id);
      Assert.NotNull(retrieved);
      Assert.Equal(source, retrieved.Source);
    }
  }
}
