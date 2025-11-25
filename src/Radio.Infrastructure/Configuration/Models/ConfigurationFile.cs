namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Represents metadata about a configuration store (file or table).
/// </summary>
public sealed record ConfigurationFile
{
  /// <summary>Unique identifier for this store.</summary>
  public required string StoreId { get; init; }

  /// <summary>The backing store type.</summary>
  public required ConfigurationStoreType StoreType { get; init; }

  /// <summary>Physical path (file path or "table:name").</summary>
  public required string Path { get; init; }

  /// <summary>Number of configuration entries in this store.</summary>
  public int EntryCount { get; init; }

  /// <summary>Size of the store in bytes.</summary>
  public long SizeBytes { get; init; }

  /// <summary>When this store was created.</summary>
  public DateTimeOffset CreatedAt { get; init; }

  /// <summary>When this store was last modified.</summary>
  public DateTimeOffset LastModifiedAt { get; init; }
}
