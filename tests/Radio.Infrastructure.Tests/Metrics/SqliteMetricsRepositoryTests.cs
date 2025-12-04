namespace Radio.Infrastructure.Tests.Metrics;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Metrics;
using Radio.Infrastructure.Metrics.Data;
using Radio.Infrastructure.Metrics.Repositories;
using Xunit;

public class SqliteMetricsRepositoryTests : IAsyncLifetime
{
  private readonly string _testDbPath;
  private readonly MetricsDbContext _dbContext;
  private readonly SqliteMetricsRepository _repository;

  public SqliteMetricsRepositoryTests()
  {
    _testDbPath = Path.Combine(Path.GetTempPath(), $"test_metrics_repo_{Guid.NewGuid()}.db");
    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = Path.GetDirectoryName(_testDbPath)!,
      MetricsSubdirectory = "",
      MetricsFileName = Path.GetFileName(_testDbPath)
    });
    var pathResolver = new DatabasePathResolver(databaseOptions);

    _dbContext = new MetricsDbContext(NullLogger<MetricsDbContext>.Instance, pathResolver);
    _repository = new SqliteMetricsRepository(
      NullLogger<SqliteMetricsRepository>.Instance,
      _dbContext);
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
  public async Task SaveBucketsAsync_SavesCounterMetric()
  {
    // Arrange
    var key = "test.counter";
    var buckets = new[]
    {
      new MetricBucket
      {
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ValueSum = 10.0,
        ValueCount = 1
      }
    };

    // Act
    await _repository.SaveBucketsAsync(
      key,
      MetricType.Counter,
      "count",
      MetricResolution.Minute,
      buckets,
      CancellationToken.None);

    // Assert - should not throw
  }

  [Fact]
  public async Task SaveBucketsAsync_SavesGaugeMetric()
  {
    // Arrange
    var key = "test.gauge";
    var buckets = new[]
    {
      new MetricBucket
      {
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ValueSum = 100.0,
        ValueCount = 10,
        ValueMin = 50.0,
        ValueMax = 150.0,
        ValueLast = 120.0
      }
    };

    // Act
    await _repository.SaveBucketsAsync(
      key,
      MetricType.Gauge,
      "MB",
      MetricResolution.Minute,
      buckets,
      CancellationToken.None);

    // Assert - should not throw
  }

  [Fact]
  public async Task GetHistoryAsync_ReturnsEmptyList_WhenNoData()
  {
    // Act
    var history = await _repository.GetHistoryAsync(
      "nonexistent.metric",
      DateTimeOffset.UtcNow.AddHours(-1),
      DateTimeOffset.UtcNow,
      MetricResolution.Minute,
      null,
      CancellationToken.None);

    // Assert
    Assert.Empty(history);
  }

  [Fact]
  public async Task GetHistoryAsync_ReturnsData_WhenExists()
  {
    // Arrange
    var key = "test.history";
    var now = DateTimeOffset.UtcNow;
    var timestamp = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset);
    
    var buckets = new[]
    {
      new MetricBucket
      {
        Timestamp = timestamp.ToUnixTimeSeconds(),
        ValueSum = 42.0,
        ValueCount = 1
      }
    };

    await _repository.SaveBucketsAsync(
      key,
      MetricType.Counter,
      "count",
      MetricResolution.Minute,
      buckets,
      CancellationToken.None);

    // Act
    var history = await _repository.GetHistoryAsync(
      key,
      timestamp.AddMinutes(-1),
      timestamp.AddMinutes(1),
      MetricResolution.Minute,
      null,
      CancellationToken.None);

    // Assert
    Assert.NotEmpty(history);
    Assert.Single(history);
    Assert.Equal(key, history[0].Key);
    Assert.Equal(42.0, history[0].Value);
  }

  [Fact]
  public async Task ListMetricKeysAsync_ReturnsEmptyList_Initially()
  {
    // Act
    var keys = await _repository.ListMetricKeysAsync(CancellationToken.None);

    // Assert
    Assert.Empty(keys);
  }

  [Fact]
  public async Task ListMetricKeysAsync_ReturnsKeys_AfterSaving()
  {
    // Arrange
    var key = "test.list";
    var buckets = new[]
    {
      new MetricBucket
      {
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ValueSum = 1.0,
        ValueCount = 1
      }
    };

    await _repository.SaveBucketsAsync(
      key,
      MetricType.Counter,
      null,
      MetricResolution.Minute,
      buckets,
      CancellationToken.None);

    // Act
    var keys = await _repository.ListMetricKeysAsync(CancellationToken.None);

    // Assert
    Assert.NotEmpty(keys);
    Assert.Contains(key, keys);
  }
}
