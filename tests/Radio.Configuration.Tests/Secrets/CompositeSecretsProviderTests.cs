namespace Radio.Configuration.Tests.Secrets;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Configuration.Models;
using Radio.Configuration.Secrets;

/// <summary>
/// Tests for CompositeSecretsProvider (SQLite primary + JSON fallback).
/// </summary>
public class CompositeSecretsProviderTests : IAsyncDisposable
{
  private readonly string _testDirectory;
  private readonly SqliteSecretsProvider _sqliteProvider;
  private readonly JsonSecretsProvider _jsonProvider;
  private readonly CompositeSecretsProvider _composite;

  public CompositeSecretsProviderTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"CompositeSecretsTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);

    var dataProtection = DataProtectionProvider.Create("TestApp");

    var configOptions1 = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
    {
      SecretsDatabasePath = Path.Combine(_testDirectory, "secrets.db")
    });

    _sqliteProvider = new SqliteSecretsProvider(
      configOptions1,
      dataProtection,
      NullLogger<SqliteSecretsProvider>.Instance);

    var configOptions = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
    {
      BasePath = _testDirectory,
      SecretsFileName = "secrets"
    });
    _jsonProvider = new JsonSecretsProvider(
      configOptions,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    _composite = new CompositeSecretsProvider(
      _sqliteProvider,
      _jsonProvider,
      NullLogger<CompositeSecretsProvider>.Instance);
  }

  public async ValueTask DisposeAsync()
  {
    await _composite.DisposeAsync();
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, recursive: true);
      }
    }
    catch { }
  }

  [Fact]
  public async Task GetSecret_ReturnsSqliteValue_WhenPresentInSqlite()
  {
    // Arrange
    await _sqliteProvider.SetSecretAsync("key1", "sqlite-value");

    // Act
    var result = await _composite.GetSecretAsync("key1");

    // Assert
    Assert.Equal("sqlite-value", result);
  }

  [Fact]
  public async Task GetSecret_FallsBackToJson_WhenNotInSqlite()
  {
    // Arrange
    await _jsonProvider.SetSecretAsync("json-key", "json-value");

    // Act
    var result = await _composite.GetSecretAsync("json-key");

    // Assert
    Assert.Equal("json-value", result);
  }

  [Fact]
  public async Task GetSecret_MigratesJsonToSqlite_OnFallback()
  {
    // Arrange
    await _jsonProvider.SetSecretAsync("migrate-key", "migrate-value");

    // Act — first read triggers migration
    await _composite.GetSecretAsync("migrate-key");

    // Assert — now available directly in SQLite
    var sqliteValue = await _sqliteProvider.GetSecretAsync("migrate-key");
    Assert.Equal("migrate-value", sqliteValue);
  }

  [Fact]
  public async Task GetSecret_PrefersSqlite_OverJson()
  {
    // Arrange — same key in both
    await _sqliteProvider.SetSecretAsync("both-key", "sqlite-wins");
    await _jsonProvider.SetSecretAsync("both-key", "json-loses");

    // Act
    var result = await _composite.GetSecretAsync("both-key");

    // Assert
    Assert.Equal("sqlite-wins", result);
  }

  [Fact]
  public async Task GetSecret_ReturnsNull_WhenNotInEither()
  {
    var result = await _composite.GetSecretAsync("nonexistent");
    Assert.Null(result);
  }

  [Fact]
  public async Task SetSecret_WritesToSqliteOnly()
  {
    // Act
    await _composite.SetSecretAsync("write-key", "write-value");

    // Assert
    var sqliteValue = await _sqliteProvider.GetSecretAsync("write-key");
    var jsonValue = await _jsonProvider.GetSecretAsync("write-key");
    Assert.Equal("write-value", sqliteValue);
    Assert.Null(jsonValue);
  }

  [Fact]
  public async Task DeleteSecret_DeletesFromBothProviders()
  {
    // Arrange
    await _sqliteProvider.SetSecretAsync("del-key", "val");
    await _jsonProvider.SetSecretAsync("del-key", "val");

    // Act
    var deleted = await _composite.DeleteSecretAsync("del-key");

    // Assert
    Assert.True(deleted);
    Assert.Null(await _sqliteProvider.GetSecretAsync("del-key"));
    Assert.Null(await _jsonProvider.GetSecretAsync("del-key"));
  }

  [Fact]
  public async Task ListTags_ReturnsUnionOfBothProviders()
  {
    // Arrange
    await _sqliteProvider.SetSecretAsync("sql-only", "v1");
    await _jsonProvider.SetSecretAsync("json-only", "v2");
    await _sqliteProvider.SetSecretAsync("shared", "v3");
    await _jsonProvider.SetSecretAsync("shared", "v3");

    // Act
    var tags = await _composite.ListTagsAsync();

    // Assert
    Assert.Contains("sql-only", tags);
    Assert.Contains("json-only", tags);
    Assert.Contains("shared", tags);
    Assert.Equal(3, tags.Count); // deduplicated
  }

  [Fact]
  public async Task ResolveTagsAsync_ResolvesFromBothProviders()
  {
    // Arrange
    await _sqliteProvider.SetSecretAsync("db_user", "admin");
    await _jsonProvider.SetSecretAsync("db_pass", "secret");

    // Act
    var resolved = await _composite.ResolveTagsAsync("User=${secret:db_user}, Pass=${secret:db_pass}");

    // Assert
    Assert.Equal("User=admin, Pass=secret", resolved);
  }

  [Fact]
  public async Task ResolveTagsAsync_PreservesUnknownTags()
  {
    var resolved = await _composite.ResolveTagsAsync("${secret:unknown}");
    Assert.Equal("${secret:unknown}", resolved);
  }

  [Fact]
  public void GenerateTag_DelegatesToSqliteProvider()
  {
    var tag1 = _composite.GenerateTag();
    var tag2 = _composite.GenerateTag();
    Assert.NotEqual(tag1, tag2);
  }

  [Fact]
  public void ContainsSecretTag_DetectsTagPatterns()
  {
    Assert.True(_composite.ContainsSecretTag("${secret:abc123}"));
    Assert.False(_composite.ContainsSecretTag("plain value"));
  }
}
