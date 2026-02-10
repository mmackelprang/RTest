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
  public void GetSecretsDatabasePath_ReturnsCorrectPath()
  {
    // Arrange
    var options = new DatabaseOptions
    {
      RootPath = "./data",
      SecretsSubdirectory = "secrets",
      SecretsFileName = "secrets.db"
    };

    // Act
    var path = options.GetSecretsDatabasePath();

    // Assert
    Assert.Equal(Path.Combine("./data", "secrets", "secrets.db"), path);
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
  public void GetAllDatabasePaths_ReturnsThreePaths()
  {
    // Arrange
    var options = new DatabaseOptions
    {
      RootPath = "./data"
    };

    // Act
    var paths = options.GetAllDatabasePaths();

    // Assert
    Assert.Equal(3, paths.Count);
    Assert.Contains(paths, p => p.Contains("configuration.db"));
    Assert.Contains(paths, p => p.Contains("fingerprints.db"));
    Assert.Contains(paths, p => p.Contains("secrets.db"));
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
    Assert.Equal("secrets", options.SecretsSubdirectory);
    Assert.Equal("secrets.db", options.SecretsFileName);
    Assert.Equal("backups", options.BackupSubdirectory);
    Assert.Equal(30, options.BackupRetentionDays);
  }
}
