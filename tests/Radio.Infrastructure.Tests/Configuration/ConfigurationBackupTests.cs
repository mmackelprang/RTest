namespace Radio.Infrastructure.Tests.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Infrastructure.Configuration.Backup;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Secrets;
using Radio.Infrastructure.Configuration.Stores;

/// <summary>
/// Tests for configuration backup service.
/// </summary>
public class ConfigurationBackupTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly ConfigurationOptions _options;
  private readonly ConfigurationStoreFactory _storeFactory;
  private readonly ConfigurationBackupService _backupService;

  public ConfigurationBackupTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"BackupTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);

    _options = new ConfigurationOptions
    {
      BasePath = _testDirectory,
      BackupPath = Path.Combine(_testDirectory, "backups"),
      JsonExtension = ".json",
      SecretsFileName = "secrets",
      SqliteFileName = "config.db",
      DefaultStoreType = ConfigurationStoreType.Json,
      AutoSave = true
    };

    var optionsMock = Options.Create(_options);
    var dataProtection = DataProtectionProvider.Create("TestApp");
    
    var databaseOptions = Options.Create(new Radio.Core.Configuration.DatabaseOptions
    {
      RootPath = _testDirectory,
      ConfigurationSubdirectory = "",
      ConfigurationFileName = "config.db",
      BackupSubdirectory = "backups"
    });
    var pathResolver = new Radio.Core.Configuration.DatabasePathResolver(databaseOptions);
    
    var secretsProvider = new JsonSecretsProvider(optionsMock, dataProtection, NullLogger<JsonSecretsProvider>.Instance);

    _storeFactory = new ConfigurationStoreFactory(
      optionsMock,
      secretsProvider,
      NullLoggerFactory.Instance,
      pathResolver);

    _backupService = new ConfigurationBackupService(
      optionsMock,
      _storeFactory,
      NullLogger<ConfigurationBackupService>.Instance);
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
  public async Task CreateBackupAsync_CreatesBackupFile()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("backup-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("Key1", "Value1");
    await store.SetEntryAsync("Key2", "Value2");

    // Act
    var backup = await _backupService.CreateBackupAsync("backup-test", ConfigurationStoreType.Json, "Test backup");

    // Assert
    Assert.NotNull(backup);
    Assert.Equal("backup-test", backup.StoreId);
    Assert.Equal(ConfigurationStoreType.Json, backup.StoreType);
    Assert.Equal("Test backup", backup.Description);
    Assert.True(File.Exists(backup.FilePath));
    Assert.True(backup.SizeBytes > 0);
  }

  [Fact]
  public async Task RestoreBackupAsync_RestoresData()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("restore-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("Original", "OriginalValue");

    var backup = await _backupService.CreateBackupAsync("restore-test", ConfigurationStoreType.Json);

    // Modify the store
    await store.SetEntryAsync("Original", "ModifiedValue");
    await store.SetEntryAsync("New", "NewValue");

    // Act
    await _backupService.RestoreBackupAsync(backup.BackupId, overwrite: true);

    // Assert
    var restoredStore = await _storeFactory.CreateStoreAsync("restore-test", ConfigurationStoreType.Json);
    await restoredStore.ReloadAsync();
    var entry = await restoredStore.GetEntryAsync("Original");

    Assert.NotNull(entry);
    Assert.Equal("OriginalValue", entry.Value);
  }

  [Fact]
  public async Task ListBackupsAsync_ReturnsAllBackups()
  {
    // Arrange
    var store1 = await _storeFactory.CreateStoreAsync("list-test-1", ConfigurationStoreType.Json);
    await store1.SetEntryAsync("Key", "Value");

    var store2 = await _storeFactory.CreateStoreAsync("list-test-2", ConfigurationStoreType.Json);
    await store2.SetEntryAsync("Key", "Value");

    await _backupService.CreateBackupAsync("list-test-1", ConfigurationStoreType.Json);
    await _backupService.CreateBackupAsync("list-test-2", ConfigurationStoreType.Json);

    // Act
    var allBackups = await _backupService.ListBackupsAsync();
    var filteredBackups = await _backupService.ListBackupsAsync("list-test-1");

    // Assert
    Assert.True(allBackups.Count >= 2);
    Assert.Single(filteredBackups);
    Assert.Equal("list-test-1", filteredBackups[0].StoreId);
  }

  [Fact]
  public async Task DeleteBackupAsync_RemovesBackup()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("delete-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("Key", "Value");

    var backup = await _backupService.CreateBackupAsync("delete-test", ConfigurationStoreType.Json);
    Assert.True(File.Exists(backup.FilePath));

    // Act
    var deleted = await _backupService.DeleteBackupAsync(backup.BackupId);

    // Assert
    Assert.True(deleted);
    Assert.False(File.Exists(backup.FilePath));
  }

  [Fact]
  public async Task DeleteBackupAsync_NonExistent_ReturnsFalse()
  {
    // Act
    var deleted = await _backupService.DeleteBackupAsync("non-existent-backup");

    // Assert
    Assert.False(deleted);
  }

  [Fact]
  public async Task ExportBackupAsync_ExportsToStream()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("export-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("Key", "Value");

    var backup = await _backupService.CreateBackupAsync("export-test", ConfigurationStoreType.Json);

    // Act
    using var stream = new MemoryStream();
    await _backupService.ExportBackupAsync(backup.BackupId, stream);

    // Assert
    Assert.True(stream.Length > 0);
  }

  [Fact]
  public async Task ImportBackupAsync_ImportsFromStream()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("import-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("Key", "Value");

    var originalBackup = await _backupService.CreateBackupAsync("import-test", ConfigurationStoreType.Json);

    // Export to memory stream
    using var exportStream = new MemoryStream();
    await _backupService.ExportBackupAsync(originalBackup.BackupId, exportStream);
    exportStream.Position = 0;

    // Delete original backup
    await _backupService.DeleteBackupAsync(originalBackup.BackupId);

    // Act
    var importedBackup = await _backupService.ImportBackupAsync(exportStream);

    // Assert
    Assert.Equal(originalBackup.StoreId, importedBackup.StoreId);
    Assert.True(File.Exists(importedBackup.FilePath));
  }

  [Fact]
  public async Task CreateBackupAsync_WithSecrets_SetsIncludesSecretsFlag()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("secrets-backup-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("PlainKey", "PlainValue");
    await store.SetEntryAsync("SecretKey", "${secret:test123}");

    // Act
    var backup = await _backupService.CreateBackupAsync("secrets-backup-test", ConfigurationStoreType.Json);

    // Assert
    Assert.True(backup.IncludesSecrets);
  }

  [Fact]
  public async Task RestoreBackupAsync_WithoutOverwrite_ThrowsForExisting()
  {
    // Arrange
    var store = await _storeFactory.CreateStoreAsync("overwrite-test", ConfigurationStoreType.Json);
    await store.SetEntryAsync("Key", "Value");

    var backup = await _backupService.CreateBackupAsync("overwrite-test", ConfigurationStoreType.Json);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
      await _backupService.RestoreBackupAsync(backup.BackupId, overwrite: false);
    });
  }
}
