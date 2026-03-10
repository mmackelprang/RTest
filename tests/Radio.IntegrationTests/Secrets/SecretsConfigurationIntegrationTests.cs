using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Configuration.Models;
using Radio.Configuration.Secrets;

namespace Radio.IntegrationTests.Secrets;

/// <summary>
/// Integration tests for secrets configuration system.
/// Tests the complete secrets flow in isolation without the full application host.
/// </summary>
public class SecretsConfigurationIntegrationTests : IDisposable
{
  private readonly string _tempDirectory;

  public SecretsConfigurationIntegrationTests()
  {
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"SecretsIntegrationTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDirectory);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_tempDirectory))
      {
        Directory.Delete(_tempDirectory, recursive: true);
      }
    }
    catch
    {
      // Ignore cleanup errors
    }
  }

  private string TempDirectory => _tempDirectory;

  [Fact]
  public void SecretTag_TryParse_ValidFormat_ReturnsTrue()
  {
    // Arrange
    var input = "${secret:my-api-key}";

    // Act
    var result = SecretTag.TryParse(input, out var tag);

    // Assert
    Assert.True(result);
    Assert.NotNull(tag);
    Assert.Equal("my-api-key", tag!.Identifier);
    Assert.Equal("${secret:my-api-key}", tag.Tag);
  }

  [Theory]
  [InlineData("${secret:abc123}", true, "abc123")]
  [InlineData("${secret:my-api-key}", true, "my-api-key")]
  [InlineData("${secret:db_password_1}", true, "db_password_1")]
  [InlineData("normal value", false, null)]
  [InlineData("${secret:}", false, null)]
  [InlineData("${secret:has spaces}", false, null)]
  [InlineData("", false, null)]
  [InlineData(null, false, null)]
  public void SecretTag_TryParse_VariousFormats_ReturnsExpected(
    string? input,
    bool expectedResult,
    string? expectedIdentifier)
  {
    // Act
    var result = SecretTag.TryParse(input, out var tag);

    // Assert
    Assert.Equal(expectedResult, result);
    if (expectedResult)
    {
      Assert.NotNull(tag);
      Assert.Equal(expectedIdentifier, tag!.Identifier);
    }
    else
    {
      Assert.Null(tag);
    }
  }

  [Fact]
  public async Task JsonSecretsProvider_StoreAndRetrieve_RoundTrips()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    var secretValue = "super-secret-api-key-12345";
    var tag = provider.GenerateTag("test_secret");

    // Act
    await provider.SetSecretAsync(tag, secretValue);
    var retrieved = await provider.GetSecretAsync(tag);

    // Assert
    Assert.Equal(secretValue, retrieved);
  }

  [Fact]
  public async Task SecretsProvider_ResolveTagsAsync_ResolvesMultipleTags()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets-resolve",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    // Store some secrets
    await provider.SetSecretAsync("user", "admin");
    await provider.SetSecretAsync("pass", "secret123");
    await provider.SetSecretAsync("host", "localhost");

    var input = "Server=${secret:host};User=${secret:user};Password=${secret:pass}";

    // Act
    var resolved = await provider.ResolveTagsAsync(input);

    // Assert
    Assert.Equal("Server=localhost;User=admin;Password=secret123", resolved);
  }

  [Fact]
  public async Task SecretsProvider_ResolveTagsAsync_PreservesUnknownTags()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets-unknown",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    var input = "${secret:unknown-tag}";

    // Act
    var resolved = await provider.ResolveTagsAsync(input);

    // Assert
    Assert.Equal("${secret:unknown-tag}", resolved);
  }

  [Fact]
  public async Task SqliteSecretsProvider_StoreAndRetrieve_RoundTrips()
  {
    // Arrange
    var configOptions = Options.Create(new Radio.Configuration.Models.ConfigurationOptions
    {
      SecretsDatabasePath = Path.Combine(TempDirectory, "test-secrets.db")
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new SqliteSecretsProvider(
      configOptions,
      dataProtection,
      NullLogger<SqliteSecretsProvider>.Instance);

    var secretValue = "sensitive-password-value";
    var tag = "encrypted-secret";

    // Act
    await provider.SetSecretAsync(tag, secretValue);
    var retrieved = await provider.GetSecretAsync(tag);

    // Assert - value is correctly stored and retrieved
    Assert.Equal(secretValue, retrieved);

    // Verify the database file exists
    var dbPath = configOptions.Value.GetSecretsDatabasePath();
    Assert.True(File.Exists(dbPath), "SQLite database file should exist");
  }

  [Fact]
  public async Task SecretsProvider_GenerateTag_CreatesUniqueIdentifiers()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets-unique",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    // Act
    var tags = new HashSet<string>();
    for (int i = 0; i < 100; i++)
    {
      tags.Add(provider.GenerateTag());
    }

    // Assert - all tags should be unique
    Assert.Equal(100, tags.Count);
  }

  [Fact]
  public async Task SecretsProvider_ListTagsAsync_ReturnsAllStoredTags()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets-list",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    await provider.SetSecretAsync("secret-one", "value1");
    await provider.SetSecretAsync("secret-two", "value2");
    await provider.SetSecretAsync("secret-three", "value3");

    // Act
    var tags = await provider.ListTagsAsync();

    // Assert
    Assert.Equal(3, tags.Count);
    Assert.Contains("secret-one", tags);
    Assert.Contains("secret-two", tags);
    Assert.Contains("secret-three", tags);
  }

  [Fact]
  public async Task SecretsProvider_DeleteSecretAsync_RemovesSecret()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets-delete",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    await provider.SetSecretAsync("to-delete", "temporary-value");

    // Act
    var deleted = await provider.DeleteSecretAsync("to-delete");
    var retrieved = await provider.GetSecretAsync("to-delete");

    // Assert
    Assert.True(deleted);
    Assert.Null(retrieved);
  }

  [Fact]
  public void SecretsProvider_ContainsSecretTag_DetectsTags()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");
    var provider = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);

    // Act & Assert
    Assert.True(provider.ContainsSecretTag("${secret:mykey}"));
    Assert.True(provider.ContainsSecretTag("prefix ${secret:mykey} suffix"));
    Assert.False(provider.ContainsSecretTag("plain text"));
    Assert.False(provider.ContainsSecretTag("${notasecret:key}"));
  }

  [Fact]
  public async Task SecretsProvider_Persistence_SurvivesProviderRestart()
  {
    // Arrange
    var options = Options.Create(new ConfigurationOptions
    {
      BasePath = TempDirectory,
      SecretsFileName = "test-secrets-persist",
      SqliteFileName = "secrets.db"
    });
    var dataProtection = DataProtectionProvider.Create("IntegrationTests");

    // Store a secret with first provider instance
    var provider1 = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);
    await provider1.SetSecretAsync("persistent-secret", "persistent-value");

    // Act - Create new provider instance (simulating restart)
    var provider2 = new JsonSecretsProvider(
      options,
      dataProtection,
      NullLogger<JsonSecretsProvider>.Instance);
    var retrieved = await provider2.GetSecretAsync("persistent-secret");

    // Assert
    Assert.Equal("persistent-value", retrieved);
  }
}
