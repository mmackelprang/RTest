namespace Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// Provides unified backup and restore capabilities for all SQLite databases in the system.
/// This includes configuration, metrics, and fingerprinting databases.
/// </summary>
public interface IUnifiedDatabaseBackupService
{
  /// <summary>
  /// Creates a complete backup of all SQLite databases in the system.
  /// The backup is stored as a single compressed archive file with timestamp.
  /// </summary>
  /// <param name="description">Optional description for the backup.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Metadata about the created backup.</returns>
  Task<UnifiedBackupMetadata> CreateFullBackupAsync(string? description = null, CancellationToken ct = default);

  /// <summary>
  /// Restores all databases from a unified backup.
  /// </summary>
  /// <param name="backupId">The backup identifier.</param>
  /// <param name="overwrite">If true, overwrites existing databases; otherwise throws if databases exist.</param>
  /// <param name="ct">Cancellation token.</param>
  Task RestoreBackupAsync(string backupId, bool overwrite = false, CancellationToken ct = default);

  /// <summary>
  /// Lists all available unified backups.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>List of backup metadata, ordered by creation date descending.</returns>
  Task<IReadOnlyList<UnifiedBackupMetadata>> ListBackupsAsync(CancellationToken ct = default);

  /// <summary>
  /// Deletes a unified backup.
  /// </summary>
  /// <param name="backupId">The backup identifier to delete.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if the backup was deleted; false if it didn't exist.</returns>
  Task<bool> DeleteBackupAsync(string backupId, CancellationToken ct = default);

  /// <summary>
  /// Exports a unified backup to a stream (for download).
  /// </summary>
  /// <param name="backupId">The backup identifier to export.</param>
  /// <param name="destination">The destination stream.</param>
  /// <param name="ct">Cancellation token.</param>
  Task ExportBackupAsync(string backupId, Stream destination, CancellationToken ct = default);

  /// <summary>
  /// Imports a unified backup from a stream (for upload).
  /// </summary>
  /// <param name="source">The source stream containing the backup.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Metadata about the imported backup.</returns>
  Task<UnifiedBackupMetadata> ImportBackupAsync(Stream source, CancellationToken ct = default);

  /// <summary>
  /// Cleans up old backups based on the retention policy.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Number of backups deleted.</returns>
  Task<int> CleanupOldBackupsAsync(CancellationToken ct = default);
}

/// <summary>
/// Metadata about a unified database backup.
/// </summary>
public sealed record UnifiedBackupMetadata
{
  /// <summary>Unique identifier for the backup.</summary>
  public required string BackupId { get; init; }

  /// <summary>When the backup was created.</summary>
  public required DateTimeOffset CreatedAt { get; init; }

  /// <summary>Optional description of the backup.</summary>
  public string? Description { get; init; }

  /// <summary>Size of the backup file in bytes.</summary>
  public required long SizeBytes { get; init; }

  /// <summary>File path to the backup.</summary>
  public required string FilePath { get; init; }

  /// <summary>List of databases included in the backup.</summary>
  public required IReadOnlyList<string> IncludedDatabases { get; init; }

  /// <summary>Whether the backup includes encrypted secrets.</summary>
  public bool IncludesSecrets { get; init; }
}
