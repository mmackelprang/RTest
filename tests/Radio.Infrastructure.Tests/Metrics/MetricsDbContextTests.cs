namespace Radio.Infrastructure.Tests.Metrics;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Metrics.Data;
using Xunit;

public class MetricsDbContextTests : IAsyncLifetime
{
  private readonly string _testDbPath;
  private readonly MetricsDbContext _dbContext;

  public MetricsDbContextTests()
  {
    _testDbPath = Path.Combine(Path.GetTempPath(), $"test_metrics_{Guid.NewGuid()}.db");
    var options = Options.Create(new MetricsOptions
    {
      DatabasePath = _testDbPath,
      Enabled = true
    });

    _dbContext = new MetricsDbContext(NullLogger<MetricsDbContext>.Instance, options);
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
  public async Task InitializeAsync_CreatesDatabase()
  {
    // Assert
    Assert.True(File.Exists(_testDbPath));
  }

  [Fact]
  public async Task GetOrCreateMetricDefinitionIdAsync_CreatesNewDefinition()
  {
    // Act
    var id = await _dbContext.GetOrCreateMetricDefinitionIdAsync(
      "test.counter",
      0,
      "count",
      CancellationToken.None);

    // Assert
    Assert.True(id > 0);
  }

  [Fact]
  public async Task GetOrCreateMetricDefinitionIdAsync_ReturnsExistingId()
  {
    // Arrange
    var key = "test.gauge";
    var id1 = await _dbContext.GetOrCreateMetricDefinitionIdAsync(
      key,
      1,
      "MB",
      CancellationToken.None);

    // Act
    var id2 = await _dbContext.GetOrCreateMetricDefinitionIdAsync(
      key,
      1,
      "MB",
      CancellationToken.None);

    // Assert
    Assert.Equal(id1, id2);
  }
}
