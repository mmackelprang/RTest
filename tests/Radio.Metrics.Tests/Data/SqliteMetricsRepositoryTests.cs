namespace Radio.Metrics.Tests.Data;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Metrics;
using Radio.Metrics.Data;
using Radio.Metrics.Repositories;
using Xunit;

public class SqliteMetricsRepositoryTests : IAsyncLifetime
{
  private readonly string _testDbPath;
  private readonly MetricsDbContext _dbContext;
  private readonly SqliteMetricsRepository _repository;

  public SqliteMetricsRepositoryTests()
  {
    _testDbPath = Path.Combine(Path.GetTempPath(), $"test_metrics_repo_{Guid.NewGuid()}.db");
    var metricsOptions = Options.Create(new MetricsOptions
    {
      DatabasePath = _testDbPath
    });

    _dbContext = new MetricsDbContext(NullLogger<MetricsDbContext>.Instance, metricsOptions);
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

  // ─── UI-2: GetWindowSummariesAsync ─────────────────────────────────────────────────────────

  private static MetricBucket Bucket(long ts, double sum, int count, double? min = null, double? max = null) =>
    new() { Timestamp = ts, ValueSum = sum, ValueCount = count, ValueMin = min, ValueMax = max, ValueLast = max };

  [Fact]
  public async Task GetWindowSummariesAsync_ReducesEveryMetricInTheWindow()
  {
    var t0 = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    // A counter with three buckets in the window and one before it.
    await _repository.SaveBucketsAsync("test.underruns", MetricType.Counter, "count", MetricResolution.Minute,
      [Bucket(t0 - 3600, 100, 1), Bucket(t0, 2, 2), Bucket(t0 + 60, 3, 1), Bucket(t0 + 120, 5, 5)]);

    // A gauge whose newest bucket averages 30 (60 / 2) while older ones sit much higher.
    await _repository.SaveBucketsAsync("test.cpu_percent", MetricType.Gauge, "percent", MetricResolution.Minute,
      [Bucket(t0, 180, 2, 80, 100), Bucket(t0 + 60, 60, 2, 20, 40)]);

    // A metric whose only data is outside the window: absent from the result, not zero.
    await _repository.SaveBucketsAsync("test.stale", MetricType.Gauge, "bare", MetricResolution.Minute,
      [Bucket(t0 - 7200, 1, 1)]);

    var result = await _repository.GetWindowSummariesAsync(
      DateTimeOffset.FromUnixTimeSeconds(t0 - 60),
      DateTimeOffset.FromUnixTimeSeconds(t0 + 600),
      MetricResolution.Minute);

    Assert.Equal(["test.cpu_percent", "test.underruns"], result.Select(r => r.Key));

    var counter = result.Single(r => r.Key == "test.underruns");
    Assert.Equal(MetricType.Counter, counter.Type);
    Assert.Equal(10, counter.Sum);                 // 2 + 3 + 5; the 100 before the window is excluded
    Assert.Equal(8, counter.SampleCount);
    Assert.Equal(3, counter.BucketCount);
    Assert.Equal(1, counter.LatestAverage);        // newest bucket: 5 / 5
    Assert.Equal(t0 + 120, counter.LatestTimestamp.ToUnixTimeSeconds());

    var gauge = result.Single(r => r.Key == "test.cpu_percent");
    Assert.Equal(MetricType.Gauge, gauge.Type);
    Assert.Equal(30, gauge.LatestAverage);
    Assert.Equal(20, gauge.Min);
    Assert.Equal(100, gauge.Max);
    Assert.Equal(2, gauge.BucketCount);
  }

  [Fact]
  public async Task GetWindowSummariesAsync_ReadsOnlyTheRequestedResolution()
  {
    var t0 = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    await _repository.SaveBucketsAsync("test.hourly", MetricType.Counter, "count", MetricResolution.Hour,
      [Bucket(t0, 4, 1)]);

    var from = DateTimeOffset.FromUnixTimeSeconds(t0 - 60);
    var to = DateTimeOffset.FromUnixTimeSeconds(t0 + 60);

    Assert.Empty(await _repository.GetWindowSummariesAsync(from, to, MetricResolution.Minute));
    Assert.Equal(4, Assert.Single(await _repository.GetWindowSummariesAsync(from, to, MetricResolution.Hour)).Sum);
  }

  [Fact]
  public async Task GetWindowSummariesAsync_ReturnsEmpty_WhenNoData()
  {
    var now = DateTimeOffset.UtcNow;
    Assert.Empty(await _repository.GetWindowSummariesAsync(now.AddHours(-1), now));
  }
}
