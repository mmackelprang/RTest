namespace Radio.Infrastructure.Tests.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Secrets;
using Radio.Infrastructure.Configuration.Stores;

/// <summary>
/// Tests for configuration store implementations.
/// </summary>
public class ConfigurationStoreTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly ISecretsProvider _mockSecretsProvider;
  private readonly ConfigurationOptions _options;

  public ConfigurationStoreTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"ConfigStoreTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);

    _options = new ConfigurationOptions
    {
      BasePath = _testDirectory,
      JsonExtension = ".json",
      SqliteFileName = "test.db",
      SecretsFileName = "secrets"
    };

    // Create a simple mock secrets provider that doesn't resolve anything
    var optionsMock = Options.Create(_options);
    var dataProtectionProvider = DataProtectionProvider.Create("TestApp");
    var logger = NullLogger<JsonSecretsProvider>.Instance;
    _mockSecretsProvider = new JsonSecretsProvider(optionsMock, dataProtectionProvider, logger);
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
  public async Task JsonStore_SetAndGetEntry_WorksCorrectly()
  {
    // Arrange
    var store = CreateJsonStore("test-store");

    // Act
    await store.SetEntryAsync("TestKey", "TestValue");
    var entry = await store.GetEntryAsync("TestKey");

    // Assert
    Assert.NotNull(entry);
    Assert.Equal("TestKey", entry.Key);
    Assert.Equal("TestValue", entry.Value);
  }

  [Fact]
  public async Task JsonStore_GetAllEntries_ReturnsAllEntries()
  {
    // Arrange
    var store = CreateJsonStore("test-store-all");
    await store.SetEntryAsync("Key1", "Value1");
    await store.SetEntryAsync("Key2", "Value2");
    await store.SetEntryAsync("Key3", "Value3");

    // Act
    var entries = await store.GetAllEntriesAsync();

    // Assert
    Assert.Equal(3, entries.Count);
    Assert.Contains(entries, e => e.Key == "Key1" && e.Value == "Value1");
    Assert.Contains(entries, e => e.Key == "Key2" && e.Value == "Value2");
    Assert.Contains(entries, e => e.Key == "Key3" && e.Value == "Value3");
  }

  [Fact]
  public async Task JsonStore_GetEntriesBySection_FiltersByPrefix()
  {
    // Arrange
    var store = CreateJsonStore("test-store-section");
    await store.SetEntryAsync("App:Setting1", "Value1");
    await store.SetEntryAsync("App:Setting2", "Value2");
    await store.SetEntryAsync("Database:Connection", "Value3");

    // Act
    var entries = await store.GetEntriesBySectionAsync("App");

    // Assert
    Assert.Equal(2, entries.Count);
    Assert.Contains(entries, e => e.Key == "App:Setting1");
    Assert.Contains(entries, e => e.Key == "App:Setting2");
    Assert.DoesNotContain(entries, e => e.Key == "Database:Connection");
  }

  [Fact]
  public async Task JsonStore_DeleteEntry_RemovesEntry()
  {
    // Arrange
    var store = CreateJsonStore("test-store-delete");
    await store.SetEntryAsync("KeyToDelete", "Value");

    // Act
    var deleted = await store.DeleteEntryAsync("KeyToDelete");
    var entry = await store.GetEntryAsync("KeyToDelete");

    // Assert
    Assert.True(deleted);
    Assert.Null(entry);
  }

  [Fact]
  public async Task JsonStore_DeleteNonExistent_ReturnsFalse()
  {
    // Arrange
    var store = CreateJsonStore("test-store-delete-ne");

    // Act
    var deleted = await store.DeleteEntryAsync("NonExistentKey");

    // Assert
    Assert.False(deleted);
  }

  [Fact]
  public async Task JsonStore_ExistsAsync_ReturnsCorrectly()
  {
    // Arrange
    var store = CreateJsonStore("test-store-exists");
    await store.SetEntryAsync("ExistingKey", "Value");

    // Act & Assert
    Assert.True(await store.ExistsAsync("ExistingKey"));
    Assert.False(await store.ExistsAsync("NonExistingKey"));
  }

  [Fact]
  public async Task JsonStore_SetEntries_SetsMultipleEntries()
  {
    // Arrange
    var store = CreateJsonStore("test-store-multiple");
    var entries = new[]
    {
      new ConfigurationEntry { Key = "Batch1", Value = "Value1" },
      new ConfigurationEntry { Key = "Batch2", Value = "Value2" }
    };

    // Act
    await store.SetEntriesAsync(entries);
    var all = await store.GetAllEntriesAsync();

    // Assert
    Assert.Equal(2, all.Count);
    Assert.Contains(all, e => e.Key == "Batch1");
    Assert.Contains(all, e => e.Key == "Batch2");
  }

  [Fact]
  public async Task JsonStore_ReloadAsync_ReloadsFromFile()
  {
    // Arrange
    var filePath = Path.Combine(_testDirectory, "reload-test.json");
    var store1 = new JsonConfigurationStore(
      "reload-test",
      filePath,
      _mockSecretsProvider,
      NullLogger<JsonConfigurationStore>.Instance);

    await store1.SetEntryAsync("Original", "Value1");

    // Modify file externally
    var store2 = new JsonConfigurationStore(
      "reload-test",
      filePath,
      _mockSecretsProvider,
      NullLogger<JsonConfigurationStore>.Instance);
    await store2.SetEntryAsync("External", "Value2");

    // Act
    await store1.ReloadAsync();
    var entry = await store1.GetEntryAsync("External");

    // Assert
    Assert.NotNull(entry);
    Assert.Equal("Value2", entry.Value);
  }

  [Fact]
  public async Task SqliteStore_SetAndGetEntry_WorksCorrectly()
  {
    // Arrange
    var store = CreateSqliteStore("sqlite-test");

    // Act
    await store.SetEntryAsync("SqlKey", "SqlValue");
    var entry = await store.GetEntryAsync("SqlKey");

    // Assert
    Assert.NotNull(entry);
    Assert.Equal("SqlKey", entry.Key);
    Assert.Equal("SqlValue", entry.Value);
  }

  [Fact]
  public async Task SqliteStore_GetAllEntries_ReturnsAllEntries()
  {
    // Arrange
    var store = CreateSqliteStore("sqlite-all");
    await store.SetEntryAsync("SqlKey1", "SqlValue1");
    await store.SetEntryAsync("SqlKey2", "SqlValue2");

    // Act
    var entries = await store.GetAllEntriesAsync();

    // Assert
    Assert.Equal(2, entries.Count);
  }

  [Fact]
  public async Task SqliteStore_DeleteEntry_RemovesEntry()
  {
    // Arrange
    var store = CreateSqliteStore("sqlite-delete");
    await store.SetEntryAsync("KeyToDelete", "Value");

    // Act
    var deleted = await store.DeleteEntryAsync("KeyToDelete");
    var entry = await store.GetEntryAsync("KeyToDelete");

    // Assert
    Assert.True(deleted);
    Assert.Null(entry);
  }

  [Fact]
  public async Task JsonStore_RawMode_PreservesSecretTags()
  {
    // Arrange
    var store = CreateJsonStore("test-raw-mode");
    var tagValue = "${secret:test123}";
    await store.SetEntryAsync("SecretKey", tagValue);

    // Act
    var rawEntry = await store.GetEntryAsync("SecretKey", ConfigurationReadMode.Raw);

    // Assert
    Assert.NotNull(rawEntry);
    Assert.Equal(tagValue, rawEntry.Value);
    Assert.True(rawEntry.ContainsSecret);
  }

  [Fact]
  public async Task SqliteStore_RawMode_PreservesSecretTags()
  {
    // Arrange
    var store = CreateSqliteStore("sqlite-raw-mode");
    var tagValue = "${secret:test456}";
    await store.SetEntryAsync("SecretKey", tagValue);

    // Act
    var rawEntry = await store.GetEntryAsync("SecretKey", ConfigurationReadMode.Raw);

    // Assert
    Assert.NotNull(rawEntry);
    Assert.Equal(tagValue, rawEntry.Value);
    Assert.True(rawEntry.ContainsSecret);
  }

  private JsonConfigurationStore CreateJsonStore(string storeId)
  {
    var filePath = Path.Combine(_testDirectory, $"{storeId}.json");
    return new JsonConfigurationStore(
      storeId,
      filePath,
      _mockSecretsProvider,
      NullLogger<JsonConfigurationStore>.Instance);
  }

  private SqliteConfigurationStore CreateSqliteStore(string storeId)
  {
    var dbPath = Path.Combine(_testDirectory, "test.db");
    var connectionString = $"Data Source={dbPath}";
    return new SqliteConfigurationStore(
      storeId,
      connectionString,
      _mockSecretsProvider,
      NullLogger<SqliteConfigurationStore>.Instance);
  }
}
