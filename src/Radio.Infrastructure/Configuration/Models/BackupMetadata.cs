namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Metadata about a configuration backup.
/// </summary>
public sealed record BackupMetadata
{
  /// <summary>Unique identifier for this backup.</summary>
  public required string BackupId { get; init; }

  /// <summary>The store that was backed up.</summary>
  public required string StoreId { get; init; }

  /// <summary>Original store type.</summary>
  public required ConfigurationStoreType StoreType { get; init; }

  /// <summary>When the backup was created.</summary>
  public required DateTimeOffset CreatedAt { get; init; }

  /// <summary>Optional description of the backup.</summary>
  public string? Description { get; init; }

  /// <summary>Size of the backup file in bytes.</summary>
  public required long SizeBytes { get; init; }

  /// <summary>Path to the backup file.</summary>
  public required string FilePath { get; init; }

  /// <summary>Whether this backup includes secrets.</summary>
  public bool IncludesSecrets { get; init; }
}
