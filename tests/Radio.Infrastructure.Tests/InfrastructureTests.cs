using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Secrets;
using Radio.Infrastructure.Configuration.Stores;

namespace Radio.Infrastructure.Tests;

/// <summary>
/// Tests for Radio.Infrastructure components.
/// These tests cover the implementations of infrastructure services.
/// </summary>
public class InfrastructureTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly ConfigurationOptions _options;
  private readonly DatabasePathResolver _pathResolver;

  public InfrastructureTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"InfraTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);

    _options = new ConfigurationOptions
    {
      BasePath = _testDirectory,
      JsonExtension = ".json",
      SqliteFileName = "test.db",
      SecretsFileName = "secrets"
    };
    
    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = _testDirectory,
      ConfigurationSubdirectory = "",
      ConfigurationFileName = "test.db"
    });
    _pathResolver = new DatabasePathResolver(databaseOptions);
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
  public void PlaceholderTest_InfrastructureProjectConfigured()
  {
    // This test verifies the test project is correctly configured
    Assert.True(true);
  }

  [Fact]
  public async Task ConfigurationStoreFactory_ListSqliteStores_ReturnsEmptyForMissingDatabase()
  {
    // Arrange
    var optionsMock = Options.Create(_options);
    var dataProtectionProvider = DataProtectionProvider.Create("TestApp");
    var logger = NullLogger<JsonSecretsProvider>.Instance;
    var secretsProvider = new JsonSecretsProvider(optionsMock, dataProtectionProvider, logger);
    var factory = new ConfigurationStoreFactory(optionsMock, secretsProvider, NullLoggerFactory.Instance, _pathResolver);

    // Act - Database doesn't exist yet
    var stores = await factory.ListStoresAsync(ConfigurationStoreType.Sqlite);

    // Assert
    Assert.Empty(stores);
  }

  [Fact]
  public async Task ConfigurationStoreFactory_ListSqliteStores_ReturnsCreatedStores()
  {
    // Arrange
    var optionsMock = Options.Create(_options);
    var dataProtectionProvider = DataProtectionProvider.Create("TestApp");
    var logger = NullLogger<JsonSecretsProvider>.Instance;
    var secretsProvider = new JsonSecretsProvider(optionsMock, dataProtectionProvider, logger);
    var factory = new ConfigurationStoreFactory(optionsMock, secretsProvider, NullLoggerFactory.Instance, _pathResolver);

    // Create a SQLite store to establish the database and table
    var store = await factory.CreateStoreAsync("test-config", ConfigurationStoreType.Sqlite);
    await store.SetEntryAsync("test-key", "test-value");

    // Act
    var stores = await factory.ListStoresAsync(ConfigurationStoreType.Sqlite);

    // Assert
    Assert.NotEmpty(stores);
    Assert.Contains("test_config", stores); // Note: hyphens are converted to underscores
  }

  [Fact]
  public async Task ConfigurationStoreFactory_ListJsonStores_ReturnsCreatedStores()
  {
    // Arrange
    var optionsMock = Options.Create(_options);
    var dataProtectionProvider = DataProtectionProvider.Create("TestApp");
    var logger = NullLogger<JsonSecretsProvider>.Instance;
    var secretsProvider = new JsonSecretsProvider(optionsMock, dataProtectionProvider, logger);
    var factory = new ConfigurationStoreFactory(optionsMock, secretsProvider, NullLoggerFactory.Instance, _pathResolver);

    // Create a JSON store
    var store = await factory.CreateStoreAsync("my-json-store", ConfigurationStoreType.Json);
    await store.SetEntryAsync("key1", "value1");
    await store.SaveAsync();

    // Act
    var stores = await factory.ListStoresAsync(ConfigurationStoreType.Json);

    // Assert
    Assert.NotEmpty(stores);
    Assert.Contains("my-json-store", stores);
  }

  [Fact]
  public void TTSEngineInfo_HasCorrectProperties()
  {
    // Test that TTSEngineInfo record works correctly
    var engineInfo = new TTSEngineInfo
    {
      Engine = TTSEngine.ESpeak,
      Name = "eSpeak-ng",
      IsAvailable = true,
      RequiresApiKey = false,
      IsOffline = true
    };

    Assert.Equal(TTSEngine.ESpeak, engineInfo.Engine);
    Assert.Equal("eSpeak-ng", engineInfo.Name);
    Assert.True(engineInfo.IsAvailable);
    Assert.False(engineInfo.RequiresApiKey);
    Assert.True(engineInfo.IsOffline);
  }

  [Fact]
  public void TTSVoiceInfo_HasCorrectProperties()
  {
    // Test that TTSVoiceInfo record works correctly
    var voiceInfo = new TTSVoiceInfo
    {
      Id = "en-US-Standard-A",
      Name = "English US Standard A",
      Language = "en-US",
      Gender = TTSVoiceGender.Male
    };

    Assert.Equal("en-US-Standard-A", voiceInfo.Id);
    Assert.Equal("English US Standard A", voiceInfo.Name);
    Assert.Equal("en-US", voiceInfo.Language);
    Assert.Equal(TTSVoiceGender.Male, voiceInfo.Gender);
  }

  [Fact]
  public void TTSParameters_HasDefaultValues()
  {
    // Test that TTSParameters has sensible defaults
    var parameters = new TTSParameters();

    Assert.Equal(TTSEngine.ESpeak, parameters.Engine);
    Assert.Equal("en", parameters.Voice);
    Assert.Equal(1.0f, parameters.Speed);
    Assert.Equal(1.0f, parameters.Pitch);
  }

  [Fact]
  public void TTSParameters_CanBeCustomized()
  {
    var parameters = new TTSParameters
    {
      Engine = TTSEngine.Google,
      Voice = "en-GB-Standard-B",
      Speed = 1.5f,
      Pitch = 0.8f
    };

    Assert.Equal(TTSEngine.Google, parameters.Engine);
    Assert.Equal("en-GB-Standard-B", parameters.Voice);
    Assert.Equal(1.5f, parameters.Speed);
    Assert.Equal(0.8f, parameters.Pitch);
  }
}
