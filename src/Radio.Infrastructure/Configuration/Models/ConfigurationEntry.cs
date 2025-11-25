namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Represents a single configuration key-value pair with metadata.
/// </summary>
public sealed record ConfigurationEntry
{
  /// <summary>The configuration key (supports section notation with ':').</summary>
  public required string Key { get; init; }

  /// <summary>The configuration value (resolved if secrets were substituted).</summary>
  public required string Value { get; init; }

  /// <summary>Original value with secret tags intact (null if same as Value).</summary>
  public string? RawValue { get; init; }

  /// <summary>Indicates whether this entry contains or contained a secret tag.</summary>
  public bool ContainsSecret { get; init; }

  /// <summary>When this entry was last modified.</summary>
  public DateTimeOffset? LastModified { get; init; }

  /// <summary>Optional description for documentation purposes.</summary>
  public string? Description { get; init; }
}
