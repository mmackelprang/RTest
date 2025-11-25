namespace Radio.Infrastructure.Tests.Configuration;

using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Tests for the SecretTag model class.
/// </summary>
public class SecretTagTests
{
  [Theory]
  [InlineData("${secret:abc123}", true, "abc123")]
  [InlineData("${secret:my-api-key}", true, "my-api-key")]
  [InlineData("${secret:db_password_1}", true, "db_password_1")]
  [InlineData("normal value", false, null)]
  [InlineData("${secret:}", false, null)]
  [InlineData("${secret:has spaces}", false, null)]
  [InlineData("${secret:special!chars}", false, null)]
  [InlineData("", false, null)]
  [InlineData(null, false, null)]
  public void TryParse_VariousInputs_ReturnsExpected(string? input, bool shouldParse, string? expectedId)
  {
    // Act
    var result = SecretTag.TryParse(input, out var tag);

    // Assert
    Assert.Equal(shouldParse, result);
    if (shouldParse)
    {
      Assert.NotNull(tag);
      Assert.Equal(expectedId, tag!.Identifier);
    }
    else
    {
      Assert.Null(tag);
    }
  }

  [Fact]
  public void ExtractAll_MultipleTagsInString_ReturnsAllTags()
  {
    // Arrange
    var input = "User: ${secret:user123}, Password: ${secret:pass456}, API: ${secret:api-key}";

    // Act
    var tags = SecretTag.ExtractAll(input).ToList();

    // Assert
    Assert.Equal(3, tags.Count);
    Assert.Contains(tags, t => t.Identifier == "user123");
    Assert.Contains(tags, t => t.Identifier == "pass456");
    Assert.Contains(tags, t => t.Identifier == "api-key");
  }

  [Fact]
  public void ExtractAll_NoTags_ReturnsEmpty()
  {
    // Arrange
    var input = "This is a normal string without any secrets";

    // Act
    var tags = SecretTag.ExtractAll(input).ToList();

    // Assert
    Assert.Empty(tags);
  }

  [Fact]
  public void ExtractAll_NullInput_ReturnsEmpty()
  {
    // Act
    var tags = SecretTag.ExtractAll(null).ToList();

    // Assert
    Assert.Empty(tags);
  }

  [Fact]
  public void ExtractAll_EmptyInput_ReturnsEmpty()
  {
    // Act
    var tags = SecretTag.ExtractAll(string.Empty).ToList();

    // Assert
    Assert.Empty(tags);
  }

  [Fact]
  public void Create_ValidIdentifier_CreatesProperTag()
  {
    // Arrange
    var identifier = "my_secret_123";

    // Act
    var tag = SecretTag.Create(identifier);

    // Assert
    Assert.Equal(identifier, tag.Identifier);
    Assert.Equal("${secret:my_secret_123}", tag.Tag);
  }

  [Fact]
  public void ContainsTag_StringWithTag_ReturnsTrue()
  {
    // Arrange
    var input = "Connection string with ${secret:password} embedded";

    // Act
    var result = SecretTag.ContainsTag(input);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public void ContainsTag_StringWithoutTag_ReturnsFalse()
  {
    // Arrange
    var input = "Normal configuration value without secrets";

    // Act
    var result = SecretTag.ContainsTag(input);

    // Assert
    Assert.False(result);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  public void ContainsTag_NullOrEmpty_ReturnsFalse(string? input)
  {
    // Act
    var result = SecretTag.ContainsTag(input);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void TagPrefix_HasCorrectValue()
  {
    Assert.Equal("${secret:", SecretTag.TagPrefix);
  }

  [Fact]
  public void TagSuffix_HasCorrectValue()
  {
    Assert.Equal("}", SecretTag.TagSuffix);
  }

  [Fact]
  public void ExtractAll_DuplicateTags_ReturnsAllOccurrences()
  {
    // Arrange
    var input = "${secret:same} and ${secret:same} again";

    // Act
    var tags = SecretTag.ExtractAll(input).ToList();

    // Assert
    Assert.Equal(2, tags.Count);
    Assert.All(tags, t => Assert.Equal("same", t.Identifier));
  }
}
