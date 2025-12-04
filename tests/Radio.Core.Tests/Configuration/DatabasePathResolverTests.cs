namespace Radio.Core.Tests.Configuration;

using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

/// <summary>
/// Tests for DatabasePathResolver class.
/// </summary>
public class DatabasePathResolverTests
{
  [Fact]
  public void GetConfigurationDatabasePath_WithoutLegacy_UsesNewPath()
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
  public void GetConfigurationDatabasePath_WithLegacyNonDefault_UsesLegacyPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./data"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var path = resolver.GetConfigurationDatabasePath("./legacy", "old.db");

    // Assert
    Assert.Contains("legacy", path);
    Assert.Contains("old.db", path);
  }

  [Fact]
  public void GetConfigurationDatabasePath_WithLegacyDefault_UsesNewPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions
    {
      RootPath = "./data"
    };
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act - using default legacy values
    var path = resolver.GetConfigurationDatabasePath("./config", "configuration.db");

    // Assert - should use new path since legacy is default
    Assert.Contains("data", path);
  }

  [Fact]
  public void GetMetricsDatabasePath_WithoutLegacy_UsesNewPath()
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
  public void GetMetricsDatabasePath_WithLegacyNonDefault_UsesLegacyPath()
  {
    // Arrange
    var databaseOptions = new DatabaseOptions();
    var resolver = new DatabasePathResolver(Options.Create(databaseOptions));

    // Act
    var path = resolver.GetMetricsDatabasePath("./custom/metrics.db");

    // Assert
    Assert.Contains("custom", path);
  }

  [Fact]
  public void GetFingerprintingDatabasePath_WithoutLegacy_UsesNewPath()
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
  public void GetBackupPath_WithoutLegacy_UsesNewPath()
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
}
