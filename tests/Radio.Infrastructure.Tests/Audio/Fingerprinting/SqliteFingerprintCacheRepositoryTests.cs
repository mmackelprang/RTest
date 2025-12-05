using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// Unit tests for the SqliteFingerprintCacheRepository class.
/// </summary>
public class SqliteFingerprintCacheRepositoryTests : IAsyncLifetime
{
  private readonly Mock<ILogger<SqliteFingerprintCacheRepository>> _loggerMock;
  private readonly Mock<ILogger<FingerprintDbContext>> _dbLoggerMock;
  private readonly FingerprintingOptions _options;
  private readonly FingerprintDbContext _dbContext;
  private readonly SqliteFingerprintCacheRepository _repository;
  private readonly string _testDbPath;

  public SqliteFingerprintCacheRepositoryTests()
  {
    _loggerMock = new Mock<ILogger<SqliteFingerprintCacheRepository>>();
    _dbLoggerMock = new Mock<ILogger<FingerprintDbContext>>();

    _testDbPath = Path.Combine(Path.GetTempPath(), $"test-fingerprints-{Guid.NewGuid()}.db");
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
    _repository = new SqliteFingerprintCacheRepository(_loggerMock.Object, _dbContext);
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
  public async Task StoreAsync_NewFingerprint_CreatesRecord()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();

    // Act
    var result = await _repository.StoreAsync(fingerprint, null);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(fingerprint.ChromaprintHash, result.ChromaprintHash);
    Assert.Equal(fingerprint.DurationSeconds, result.DurationSeconds);
    Assert.Equal(0, result.MatchCount);
  }

  [Fact]
  public async Task StoreAsync_WithMetadata_CreatesRecordWithMetadata()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();
    var metadata = CreateTestMetadata();

    // Act
    var result = await _repository.StoreAsync(fingerprint, metadata);

    // Assert
    Assert.NotNull(result);
    Assert.NotNull(result.Metadata);
    Assert.Equal(metadata.Title, result.Metadata!.Title);
    Assert.Equal(metadata.Artist, result.Metadata.Artist);
  }

  [Fact]
  public async Task FindByHashAsync_ExistingHash_ReturnsRecord()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();
    await _repository.StoreAsync(fingerprint, null);

    // Act
    var result = await _repository.FindByHashAsync(fingerprint.ChromaprintHash);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(fingerprint.ChromaprintHash, result.ChromaprintHash);
  }

  [Fact]
  public async Task FindByHashAsync_NonExistingHash_ReturnsNull()
  {
    // Act
    var result = await _repository.FindByHashAsync("nonexistent-hash");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task UpdateLastMatchedAsync_UpdatesTimestampAndCount()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();
    var stored = await _repository.StoreAsync(fingerprint, null);
    Assert.Equal(0, stored.MatchCount);

    // Act
    await _repository.UpdateLastMatchedAsync(stored.Id);
    var updated = await _repository.FindByHashAsync(fingerprint.ChromaprintHash);

    // Assert
    Assert.NotNull(updated);
    Assert.Equal(1, updated.MatchCount);
    Assert.NotNull(updated.LastMatchedAt);
  }

  [Fact]
  public async Task GetCacheCountAsync_ReturnsCorrectCount()
  {
    // Arrange
    var fp1 = CreateTestFingerprint("hash1");
    var fp2 = CreateTestFingerprint("hash2");
    var fp3 = CreateTestFingerprint("hash3");

    await _repository.StoreAsync(fp1, null);
    await _repository.StoreAsync(fp2, null);
    await _repository.StoreAsync(fp3, null);

    // Act
    var count = await _repository.GetCacheCountAsync();

    // Assert
    Assert.Equal(3, count);
  }

  [Fact]
  public async Task GetAllAsync_ReturnsPaginatedResults()
  {
    // Arrange
    for (int i = 0; i < 10; i++)
    {
      await _repository.StoreAsync(CreateTestFingerprint($"hash-{i}"), null);
    }

    // Act
    var page1 = await _repository.GetAllAsync(page: 1, pageSize: 5);
    var page2 = await _repository.GetAllAsync(page: 2, pageSize: 5);

    // Assert
    Assert.Equal(5, page1.Count);
    Assert.Equal(5, page2.Count);
  }

  [Fact]
  public async Task DeleteAsync_ExistingRecord_DeletesAndReturnsTrue()
  {
    // Arrange
    var fingerprint = CreateTestFingerprint();
    var stored = await _repository.StoreAsync(fingerprint, null);

    // Act
    var deleted = await _repository.DeleteAsync(stored.Id);
    var found = await _repository.FindByHashAsync(fingerprint.ChromaprintHash);

    // Assert
    Assert.True(deleted);
    Assert.Null(found);
  }

  [Fact]
  public async Task DeleteAsync_NonExistingRecord_ReturnsFalse()
  {
    // Act
    var deleted = await _repository.DeleteAsync("nonexistent-id");

    // Assert
    Assert.False(deleted);
  }

  private static FingerprintData CreateTestFingerprint(string? hash = null)
  {
    return new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = hash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
      DurationSeconds = 15,
      GeneratedAt = DateTime.UtcNow
    };
  }

  private static TrackMetadata CreateTestMetadata()
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Test Song",
      Artist = "Test Artist",
      Album = "Test Album",
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
}
