namespace Radio.Infrastructure.Tests.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.Configuration.Secrets;

/// <summary>
/// Tests for secrets provider implementations.
/// </summary>
public class SecretsProviderTests : IDisposable
{
  private readonly string _testDirectory;
  private readonly ConfigurationOptions _options;
  private readonly IDataProtectionProvider _dataProtection;

  public SecretsProviderTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"SecretsTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);

    _options = new ConfigurationOptions
    {
      BasePath = _testDirectory,
      SecretsFileName = "secrets",
      SqliteFileName = "secrets.db"
    };

    _dataProtection = DataProtectionProvider.Create("TestApp");
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
  public async Task JsonSecretsProvider_SetAndGet_WorksCorrectly()
  {
    // Arrange
    var provider = CreateJsonProvider();
    var tag = "test-secret-1";
    var value = "super-secret-value";

    // Act
    await provider.SetSecretAsync(tag, value);
    var retrieved = await provider.GetSecretAsync(tag);

    // Assert
    Assert.Equal(value, retrieved);
  }

  [Fact]
  public async Task JsonSecretsProvider_GetNonExistent_ReturnsNull()
  {
    // Arrange
    var provider = CreateJsonProvider();

    // Act
    var result = await provider.GetSecretAsync("non-existent");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task JsonSecretsProvider_DeleteSecret_RemovesSecret()
  {
    // Arrange
    var provider = CreateJsonProvider();
    await provider.SetSecretAsync("to-delete", "value");

    // Act
    var deleted = await provider.DeleteSecretAsync("to-delete");
    var result = await provider.GetSecretAsync("to-delete");

    // Assert
    Assert.True(deleted);
    Assert.Null(result);
  }

  [Fact]
  public async Task JsonSecretsProvider_ListTags_ReturnsAllTags()
  {
    // Arrange
    var provider = CreateJsonProvider();
    await provider.SetSecretAsync("tag1", "value1");
    await provider.SetSecretAsync("tag2", "value2");
    await provider.SetSecretAsync("tag3", "value3");

    // Act
    var tags = await provider.ListTagsAsync();

    // Assert
    Assert.Equal(3, tags.Count);
    Assert.Contains("tag1", tags);
    Assert.Contains("tag2", tags);
    Assert.Contains("tag3", tags);
  }

  [Fact]
  public void JsonSecretsProvider_GenerateTag_CreatesUniqueTag()
  {
    // Arrange
    var provider = CreateJsonProvider();

    // Act
    var tag1 = provider.GenerateTag();
    var tag2 = provider.GenerateTag();

    // Assert
    Assert.NotEqual(tag1, tag2);
    Assert.Equal(12, tag1.Length); // GUID substring
    Assert.Equal(12, tag2.Length);
  }

  [Fact]
  public void JsonSecretsProvider_GenerateTagWithHint_IncludesHint()
  {
    // Arrange
    var provider = CreateJsonProvider();

    // Act
    var tag = provider.GenerateTag("Database_Password");

    // Assert
    Assert.StartsWith("Database_Password_", tag);
  }

  [Fact]
  public void JsonSecretsProvider_ContainsSecretTag_DetectsTag()
  {
    // Arrange
    var provider = CreateJsonProvider();

    // Act & Assert
    Assert.True(provider.ContainsSecretTag("${secret:abc123}"));
    Assert.False(provider.ContainsSecretTag("plain value"));
    Assert.False(provider.ContainsSecretTag("${invalid}"));
  }

  [Fact]
  public async Task JsonSecretsProvider_ResolveTagsAsync_ResolvesSecrets()
  {
    // Arrange
    var provider = CreateJsonProvider();
    await provider.SetSecretAsync("user", "admin");
    await provider.SetSecretAsync("pass", "secret123");

    var value = "User: ${secret:user}, Pass: ${secret:pass}";

    // Act
    var resolved = await provider.ResolveTagsAsync(value);

    // Assert
    Assert.Equal("User: admin, Pass: secret123", resolved);
  }

  [Fact]
  public async Task JsonSecretsProvider_ResolveTagsAsync_PreservesUnknownTags()
  {
    // Arrange
    var provider = CreateJsonProvider();
    var value = "${secret:unknown}";

    // Act
    var resolved = await provider.ResolveTagsAsync(value);

    // Assert
    Assert.Equal("${secret:unknown}", resolved);
  }

  [Fact]
  public async Task SqliteSecretsProvider_SetAndGet_WorksCorrectly()
  {
    // Arrange
    var provider = CreateSqliteProvider();
    var tag = "sqlite-secret";
    var value = "sqlite-value";

    // Act
    await provider.SetSecretAsync(tag, value);
    var retrieved = await provider.GetSecretAsync(tag);

    // Assert
    Assert.Equal(value, retrieved);
  }

  [Fact]
  public async Task SqliteSecretsProvider_DeleteSecret_RemovesSecret()
  {
    // Arrange
    var provider = CreateSqliteProvider();
    await provider.SetSecretAsync("sqlite-delete", "value");

    // Act
    var deleted = await provider.DeleteSecretAsync("sqlite-delete");
    var result = await provider.GetSecretAsync("sqlite-delete");

    // Assert
    Assert.True(deleted);
    Assert.Null(result);
  }

  [Fact]
  public async Task SqliteSecretsProvider_ListTags_ReturnsAllTags()
  {
    // Arrange
    var provider = CreateSqliteProvider();
    await provider.SetSecretAsync("sql1", "value1");
    await provider.SetSecretAsync("sql2", "value2");

    // Act
    var tags = await provider.ListTagsAsync();

    // Assert
    Assert.Contains("sql1", tags);
    Assert.Contains("sql2", tags);
  }

  [Fact]
  public async Task JsonSecretsProvider_UpdateSecret_OverwritesValue()
  {
    // Arrange
    var provider = CreateJsonProvider();
    await provider.SetSecretAsync("update-test", "original");

    // Act
    await provider.SetSecretAsync("update-test", "updated");
    var result = await provider.GetSecretAsync("update-test");

    // Assert
    Assert.Equal("updated", result);
  }

  [Fact]
  public async Task JsonSecretsProvider_PersistsToFile()
  {
    // Arrange
    var provider1 = CreateJsonProvider();
    await provider1.SetSecretAsync("persist-test", "persist-value");

    // Act - Create new instance that loads from file
    var provider2 = CreateJsonProvider();
    var result = await provider2.GetSecretAsync("persist-test");

    // Assert
    Assert.Equal("persist-value", result);
  }

  private JsonSecretsProvider CreateJsonProvider()
  {
    return new JsonSecretsProvider(
      Options.Create(_options),
      _dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);
  }

  private SqliteSecretsProvider CreateSqliteProvider()
  {
    return new SqliteSecretsProvider(
      Options.Create(_options),
      _dataProtection,
      NullLogger<SqliteSecretsProvider>.Instance);
  }
}
