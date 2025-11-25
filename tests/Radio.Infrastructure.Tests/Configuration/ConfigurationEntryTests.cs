namespace Radio.Infrastructure.Tests.Configuration;

using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Tests for the ConfigurationEntry record.
/// </summary>
public class ConfigurationEntryTests
{
  [Fact]
  public void Record_Equality_WorksCorrectly()
  {
    // Arrange
    var entry1 = new ConfigurationEntry
    {
      Key = "TestKey",
      Value = "TestValue",
      RawValue = null,
      ContainsSecret = false
    };

    var entry2 = new ConfigurationEntry
    {
      Key = "TestKey",
      Value = "TestValue",
      RawValue = null,
      ContainsSecret = false
    };

    // Act & Assert
    Assert.Equal(entry1, entry2);
    Assert.True(entry1 == entry2);
    Assert.Equal(entry1.GetHashCode(), entry2.GetHashCode());
  }

  [Fact]
  public void Record_Inequality_WorksCorrectly()
  {
    // Arrange
    var entry1 = new ConfigurationEntry
    {
      Key = "TestKey1",
      Value = "TestValue",
      RawValue = null,
      ContainsSecret = false
    };

    var entry2 = new ConfigurationEntry
    {
      Key = "TestKey2",
      Value = "TestValue",
      RawValue = null,
      ContainsSecret = false
    };

    // Act & Assert
    Assert.NotEqual(entry1, entry2);
    Assert.False(entry1 == entry2);
  }

  [Fact]
  public void Record_WithExpression_CreatesModifiedCopy()
  {
    // Arrange
    var original = new ConfigurationEntry
    {
      Key = "TestKey",
      Value = "OriginalValue",
      RawValue = null,
      ContainsSecret = false,
      Description = "Test description"
    };

    // Act
    var modified = original with { Value = "ModifiedValue" };

    // Assert
    Assert.Equal("TestKey", modified.Key);
    Assert.Equal("ModifiedValue", modified.Value);
    Assert.Equal("Test description", modified.Description);
    Assert.Equal("OriginalValue", original.Value); // Original unchanged
  }

  [Fact]
  public void Record_WithSecretProperties_StoresCorrectly()
  {
    // Arrange
    var entry = new ConfigurationEntry
    {
      Key = "Database:Password",
      Value = "actual-password",
      RawValue = "${secret:db_pwd_123}",
      ContainsSecret = true,
      LastModified = DateTimeOffset.UtcNow
    };

    // Assert
    Assert.Equal("Database:Password", entry.Key);
    Assert.Equal("actual-password", entry.Value);
    Assert.Equal("${secret:db_pwd_123}", entry.RawValue);
    Assert.True(entry.ContainsSecret);
    Assert.NotNull(entry.LastModified);
  }

  [Fact]
  public void Record_AllPropertiesSet_StoresCorrectly()
  {
    // Arrange
    var now = DateTimeOffset.UtcNow;
    var entry = new ConfigurationEntry
    {
      Key = "App:Setting",
      Value = "resolved-value",
      RawValue = "${secret:tag}",
      ContainsSecret = true,
      LastModified = now,
      Description = "Application setting"
    };

    // Assert
    Assert.Equal("App:Setting", entry.Key);
    Assert.Equal("resolved-value", entry.Value);
    Assert.Equal("${secret:tag}", entry.RawValue);
    Assert.True(entry.ContainsSecret);
    Assert.Equal(now, entry.LastModified);
    Assert.Equal("Application setting", entry.Description);
  }
}
