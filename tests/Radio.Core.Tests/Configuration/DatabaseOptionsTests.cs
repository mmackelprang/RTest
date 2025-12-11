namespace Radio.Core.Tests.Configuration;

using Radio.Core.Configuration;

/// <summary>
/// Tests for DatabaseOptions configuration class.
/// </summary>
public class DatabaseOptionsTests
{
  [Fact]
  public void GetConfigurationDatabasePath_ReturnsCorrectPath()
  {
    // Arrange
    var options = new DatabaseOptions
    {
      RootPath = "./data",
      ConfigurationSubdirectory = "config",
      ConfigurationFileName = "configuration.db"
    };

    // Act
    var path = options.GetConfigurationDatabasePath();

    // Assert
    Assert.Equal(Path.Combine("./data", "config", "configuration.db"), path);
  }

  [Fact]
  public void GetFingerprintingDatabasePath_ReturnsCorrectPath()
  {
    // Arrange
    var options = new DatabaseOptions
    {
      RootPath = "./data",
      FingerprintingSubdirectory = "fingerprints",
      FingerprintingFileName = "fingerprints.db"
    };

    // Act
    var path = options.GetFingerprintingDatabasePath();

    // Assert
    Assert.Equal(Path.Combine("./data", "fingerprints", "fingerprints.db"), path);
  }

  [Fact]
  public void GetBackupPath_ReturnsCorrectPath()
  {
    // Arrange
    var options = new DatabaseOptions
    {
      RootPath = "./data",
      BackupSubdirectory = "backups"
    };

    // Act
    var path = options.GetBackupPath();

    // Assert
    Assert.Equal(Path.Combine("./data", "backups"), path);
  }

  [Fact]
  public void GetAllDatabasePaths_ReturnsTwoPaths()
  {
    // Arrange
    var options = new DatabaseOptions
    {
      RootPath = "./data"
    };

    // Act
    var paths = options.GetAllDatabasePaths();

    // Assert
    // Metrics are stored in the configuration database, so we only have 2 separate database files
    Assert.Equal(2, paths.Count);
    Assert.Contains(paths, p => p.Contains("configuration.db"));
    Assert.Contains(paths, p => p.Contains("fingerprints.db"));
  }

  [Fact]
  public void DefaultValues_AreSet()
  {
    // Arrange & Act
    var options = new DatabaseOptions();

    // Assert
    Assert.Equal("./data", options.RootPath);
    Assert.Equal("config", options.ConfigurationSubdirectory);
    Assert.Equal("configuration.db", options.ConfigurationFileName);
    Assert.Equal("fingerprints", options.FingerprintingSubdirectory);
    Assert.Equal("fingerprints.db", options.FingerprintingFileName);
    Assert.Equal("backups", options.BackupSubdirectory);
    Assert.Equal(30, options.BackupRetentionDays);
  }
}
