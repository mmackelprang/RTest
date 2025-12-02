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
  private readonly FingerprintingOptions _options;
  private readonly FingerprintDbContext _dbContext;
  private readonly SqlitePlayHistoryRepository _repository;
  private readonly string _testDbPath;

  public SqlitePlayHistoryRepositoryTests()
  {
    _loggerMock = new Mock<ILogger<SqlitePlayHistoryRepository>>();
    _dbLoggerMock = new Mock<ILogger<FingerprintDbContext>>();

    _testDbPath = Path.Combine(Path.GetTempPath(), $"test-history-{Guid.NewGuid()}.db");
    _options = new FingerprintingOptions
    {
      DatabasePath = _testDbPath
    };

    var optionsMock = new Mock<IOptions<FingerprintingOptions>>();
    optionsMock.Setup(o => o.Value).Returns(_options);

    _dbContext = new FingerprintDbContext(_dbLoggerMock.Object, optionsMock.Object);
    _repository = new SqlitePlayHistoryRepository(_loggerMock.Object, _dbContext);
  }

  public async Task InitializeAsync()
  {
    await _dbContext.InitializeAsync();
  }

  public async Task DisposeAsync()
  {
    await _dbContext.DisposeAsync();
    if (File.Exists(_testDbPath))
    {
      File.Delete(_testDbPath);
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
