namespace Radio.Core.Tests.Configuration;

using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

/// <summary>
/// Tests for DatabasePathResolver class.
/// </summary>
public class DatabasePathResolverTests
{
  [Fact]
  public void GetConfigurationDatabasePath_ReturnsConfiguredPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./newdata",
      ConfigurationSubdirectory = "cfg",
      ConfigurationFileName = "config.db"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var path = resolver.GetConfigurationDatabasePath();

    // Assert
    Assert.Contains("newdata", path);
    Assert.Contains("cfg", path);
    Assert.Contains("config.db", path);
  }

  [Fact]
  public void GetMetricsDatabasePath_ReturnsConfiguredPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./newdata",
      MetricsSubdirectory = "met",
      MetricsFileName = "metrics.db"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var path = resolver.GetMetricsDatabasePath();

    // Assert
    Assert.Contains("newdata", path);
    Assert.Contains("met", path);
    Assert.Contains("metrics.db", path);
  }

  [Fact]
  public void GetFingerprintingDatabasePath_ReturnsConfiguredPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./newdata",
      FingerprintingSubdirectory = "fp",
      FingerprintingFileName = "fingerprints.db"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var path = resolver.GetFingerprintingDatabasePath();

    // Assert
    Assert.Contains("newdata", path);
    Assert.Contains("fp", path);
    Assert.Contains("fingerprints.db", path);
  }

  [Fact]
  public void GetBackupPath_ReturnsConfiguredPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./newdata",
      BackupSubdirectory = "bak"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var path = resolver.GetBackupPath();

    // Assert
    Assert.Contains("newdata", path);
    Assert.Contains("bak", path);
  }

  [Fact]
  public void GetAllDatabasePaths_ReturnsThreePaths()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions();
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var paths = resolver.GetAllDatabasePaths();

    // Assert
    Assert.Equal(3, paths.Count);
    Assert.All(paths, p => Assert.False(string.IsNullOrWhiteSpace(p)));
  }

  [Fact]
  public void GetAllDatabasePaths_ContainsAllExpectedDatabases()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./data"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var paths = resolver.GetAllDatabasePaths();

    // Assert
    Assert.Contains(paths, p => p.Contains("configuration.db"));
    Assert.Contains(paths, p => p.Contains("metrics.db"));
    Assert.Contains(paths, p => p.Contains("fingerprints.db"));
  }
}
