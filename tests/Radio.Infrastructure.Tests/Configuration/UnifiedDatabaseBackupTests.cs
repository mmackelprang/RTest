namespace Radio.Infrastructure.Tests.Configuration;

using System.IO.Compression;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Backup;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Secrets;
using Radio.Infrastructure.Configuration.Stores;

/// <summary>
/// Tests for unified database backup service.
/// </summary>
public class UnifiedDatabaseBackupTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly DatabaseOptions _databaseOptions;
  private readonly ConfigurationOptions _configOptions;
  private readonly MetricsOptions _metricsOptions;
  private readonly FingerprintingOptions _fingerprintingOptions;
  private readonly DatabasePathResolver _pathResolver;
  private readonly UnifiedDatabaseBackupService _backupService;

  public UnifiedDatabaseBackupTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"UnifiedBackupTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);

    _databaseOptions = new DatabaseOptions
    {
      RootPath = _testDirectory,
      ConfigurationSubdirectory = "config",
      ConfigurationFileName = "configuration.db",
      MetricsSubdirectory = "metrics",
      MetricsFileName = "metrics.db",
      FingerprintingSubdirectory = "fingerprints",
      FingerprintingFileName = "fingerprints.db",
      BackupSubdirectory = "backups"
    };

    _configOptions = new ConfigurationOptions();
    _metricsOptions = new MetricsOptions();
    _fingerprintingOptions = new FingerprintingOptions();

    _pathResolver = new DatabasePathResolver(Options.Create(_databaseOptions));

    _backupService = new UnifiedDatabaseBackupService(
      Options.Create(_databaseOptions),
      _pathResolver,
      NullLogger<UnifiedDatabaseBackupService>.Instance);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, recursive: true);
      }
    }
    catch
    {
      // Ignore cleanup errors
    }
  }

  [Fact]
  public async Task CreateFullBackupAsync_CreatesBackupFile()
  {
    // Arrange - Create some dummy database files
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());
    CreateDummyDatabase(_pathResolver.GetMetricsDatabasePath());
    CreateDummyDatabase(_pathResolver.GetFingerprintingDatabasePath());

    // Act
    var backup = await _backupService.CreateFullBackupAsync("Test unified backup");

    // Assert
    Assert.NotNull(backup);
    Assert.StartsWith("unified_", backup.BackupId);
    Assert.Equal("Test unified backup", backup.Description);
    Assert.True(File.Exists(backup.FilePath));
    Assert.True(backup.SizeBytes > 0);
    Assert.Equal(3, backup.IncludedDatabases.Count);
    Assert.Contains("configuration.db", backup.IncludedDatabases);
    Assert.Contains("metrics.db", backup.IncludedDatabases);
    Assert.Contains("fingerprints.db", backup.IncludedDatabases);
  }

  [Fact]
  public async Task CreateFullBackupAsync_WithPartialDatabases_BacksUpOnlyExisting()
  {
    // Arrange - Create only one database
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());

    // Act
    var backup = await _backupService.CreateFullBackupAsync();

    // Assert
    Assert.NotNull(backup);
    Assert.Single(backup.IncludedDatabases);
    Assert.Contains("configuration.db", backup.IncludedDatabases);
  }

  [Fact]
  public async Task CreateFullBackupAsync_IncludesManifest()
  {
    // Arrange
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());

    // Act
    var backup = await _backupService.CreateFullBackupAsync("Manifest test");

    // Assert
    using var archive = ZipFile.OpenRead(backup.FilePath);
    var manifestEntry = archive.GetEntry("manifest.json");
    Assert.NotNull(manifestEntry);
  }

  [Fact]
  public async Task CreateFullBackupAsync_IncludesReadme()
  {
    // Arrange
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());

    // Act
    var backup = await _backupService.CreateFullBackupAsync("Readme test");

    // Assert
    using var archive = ZipFile.OpenRead(backup.FilePath);
    var readmeEntry = archive.GetEntry("README.txt");
    Assert.NotNull(readmeEntry);
  }

  [Fact]
  public async Task RestoreBackupAsync_RestoresDatabases()
  {
    // Arrange - Create databases and backup
    var configPath = _pathResolver.GetConfigurationDatabasePath();
    var metricsPath = _pathResolver.GetMetricsDatabasePath();
    CreateDummyDatabase(configPath);
    CreateDummyDatabase(metricsPath);

    var backup = await _backupService.CreateFullBackupAsync();

    // Delete the databases
    File.Delete(configPath);
    File.Delete(metricsPath);

    // Act
    await _backupService.RestoreBackupAsync(backup.BackupId, overwrite: true);

    // Assert
    Assert.True(File.Exists(configPath));
    Assert.True(File.Exists(metricsPath));
  }

  [Fact]
  public async Task RestoreBackupAsync_WithoutOverwrite_ThrowsIfDatabaseExists()
  {
    // Arrange
    var configPath = _pathResolver.GetConfigurationDatabasePath();
    CreateDummyDatabase(configPath);
    var backup = await _backupService.CreateFullBackupAsync();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => _backupService.RestoreBackupAsync(backup.BackupId, overwrite: false));
  }

  [Fact]
  public async Task ListBackupsAsync_ReturnsAllBackups()
  {
    // Arrange
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());
    var backup1 = await _backupService.CreateFullBackupAsync("First");
    await Task.Delay(100); // Ensure different timestamps
    var backup2 = await _backupService.CreateFullBackupAsync("Second");

    // Act
    var backups = await _backupService.ListBackupsAsync();

    // Assert
    Assert.Equal(2, backups.Count);
    Assert.Contains(backups, b => b.BackupId == backup1.BackupId);
    Assert.Contains(backups, b => b.BackupId == backup2.BackupId);
    // Should be ordered by creation date descending
    Assert.Equal(backup2.BackupId, backups[0].BackupId);
  }

  [Fact]
  public async Task DeleteBackupAsync_DeletesBackup()
  {
    // Arrange
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());
    var backup = await _backupService.CreateFullBackupAsync();

    // Act
    var result = await _backupService.DeleteBackupAsync(backup.BackupId);

    // Assert
    Assert.True(result);
    Assert.False(File.Exists(backup.FilePath));
  }

  [Fact]
  public async Task ExportBackupAsync_ExportsBackupToStream()
  {
    // Arrange
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());
    var backup = await _backupService.CreateFullBackupAsync();

    // Act
    using var stream = new MemoryStream();
    await _backupService.ExportBackupAsync(backup.BackupId, stream);

    // Assert
    Assert.True(stream.Length > 0);
    // The stream length should be close to backup size (may differ slightly due to buffering)
    Assert.InRange(stream.Length, backup.SizeBytes - 1000, backup.SizeBytes + 1000);
  }

  [Fact]
  public async Task ImportBackupAsync_ImportsBackupFromStream()
  {
    // Arrange
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());
    var originalBackup = await _backupService.CreateFullBackupAsync();

    using var exportStream = new MemoryStream();
    await _backupService.ExportBackupAsync(originalBackup.BackupId, exportStream);
    exportStream.Position = 0;

    // Delete original backup
    await _backupService.DeleteBackupAsync(originalBackup.BackupId);

    // Act
    var importedBackup = await _backupService.ImportBackupAsync(exportStream);

    // Assert
    Assert.NotNull(importedBackup);
    Assert.True(File.Exists(importedBackup.FilePath));
  }

  [Fact]
  public async Task CleanupOldBackupsAsync_DeletesOldBackups()
  {
    // Arrange - Create a backup
    CreateDummyDatabase(_pathResolver.GetConfigurationDatabasePath());
    var backup = await _backupService.CreateFullBackupAsync();

    // Create new service with zero retention to make backup old
    var zeroRetentionOptions = new DatabaseOptions
    {
      RootPath = _databaseOptions.RootPath,
      ConfigurationSubdirectory = _databaseOptions.ConfigurationSubdirectory,
      ConfigurationFileName = _databaseOptions.ConfigurationFileName,
      MetricsSubdirectory = _databaseOptions.MetricsSubdirectory,
      MetricsFileName = _databaseOptions.MetricsFileName,
      FingerprintingSubdirectory = _databaseOptions.FingerprintingSubdirectory,
      FingerprintingFileName = _databaseOptions.FingerprintingFileName,
      BackupSubdirectory = _databaseOptions.BackupSubdirectory,
      BackupRetentionDays = 0  // Zero retention to make all backups old
    };
    var zeroRetentionPathResolver = new DatabasePathResolver(Options.Create(zeroRetentionOptions));
    var zeroRetentionService = new UnifiedDatabaseBackupService(
      Options.Create(zeroRetentionOptions),
      zeroRetentionPathResolver,
      NullLogger<UnifiedDatabaseBackupService>.Instance);

    // Act
    var deletedCount = await zeroRetentionService.CleanupOldBackupsAsync();

    // Assert
    Assert.Equal(1, deletedCount);
    Assert.False(File.Exists(backup.FilePath));
  }

  private void CreateDummyDatabase(string path)
  {
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    // Create a simple SQLite database file
    var connectionString = $"Data Source={path}";
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "CREATE TABLE Test (Id INTEGER PRIMARY KEY, Value TEXT)";
    cmd.ExecuteNonQuery();
  }
}
