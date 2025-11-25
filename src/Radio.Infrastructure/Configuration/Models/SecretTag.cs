namespace Radio.Infrastructure.Configuration.Models;

using System.Text.RegularExpressions;

/// <summary>
/// Represents a secret tag reference in configuration values.
/// Format: ${secret:identifier}
/// </summary>
public sealed partial record SecretTag
{
  /// <summary>The prefix for all secret tags.</summary>
  public const string TagPrefix = "${secret:";

  /// <summary>The suffix for all secret tags.</summary>
  public const string TagSuffix = "}";

  [GeneratedRegex(@"\$\{secret:([a-zA-Z0-9_-]+)\}", RegexOptions.Compiled)]
  private static partial Regex TagPatternRegex();

  /// <summary>The full tag string (e.g., "${secret:abc123}").</summary>
  public required string Tag { get; init; }

  /// <summary>The identifier portion only (e.g., "abc123").</summary>
  public required string Identifier { get; init; }

  /// <summary>Creates a SecretTag from an identifier.</summary>
  public static SecretTag Create(string identifier) => new()
  {
    Tag = $"{TagPrefix}{identifier}{TagSuffix}",
    Identifier = identifier
  };

  /// <summary>Attempts to parse a secret tag from a string.</summary>
  public static bool TryParse(string? value, out SecretTag? tag)
  {
    tag = null;
    if (string.IsNullOrEmpty(value))
      return false;

    var match = TagPatternRegex().Match(value);
    if (!match.Success)
      return false;

    tag = new SecretTag
    {
      Tag = match.Value,
      Identifier = match.Groups[1].Value
    };
    return true;
  }

  /// <summary>Extracts all secret tags from a string.</summary>
  public static IEnumerable<SecretTag> ExtractAll(string? value)
  {
    if (string.IsNullOrEmpty(value))
      yield break;

    foreach (Match match in TagPatternRegex().Matches(value))
    {
      yield return new SecretTag
      {
        Tag = match.Value,
        Identifier = match.Groups[1].Value
      };
    }
  }

  /// <summary>Checks if a string contains any secret tags.</summary>
  public static bool ContainsTag(string? value) =>
    !string.IsNullOrEmpty(value) && TagPatternRegex().IsMatch(value);
}
