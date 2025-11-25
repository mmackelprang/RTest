namespace Radio.Infrastructure.Configuration.Abstractions;

using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Provides backup and restore capabilities for configuration stores.
/// </summary>
public interface IConfigurationBackupService
{
  /// <summary>Creates a backup of the specified store.</summary>
  Task<BackupMetadata> CreateBackupAsync(string storeId, ConfigurationStoreType storeType, string? description = null, CancellationToken ct = default);

  /// <summary>Creates a backup of all stores.</summary>
  Task<IReadOnlyList<BackupMetadata>> CreateFullBackupAsync(string? description = null, CancellationToken ct = default);

  /// <summary>Restores a store from a backup.</summary>
  Task RestoreBackupAsync(string backupId, bool overwrite = false, CancellationToken ct = default);

  /// <summary>Lists all available backups.</summary>
  Task<IReadOnlyList<BackupMetadata>> ListBackupsAsync(string? storeId = null, CancellationToken ct = default);

  /// <summary>Deletes a backup.</summary>
  Task<bool> DeleteBackupAsync(string backupId, CancellationToken ct = default);

  /// <summary>Exports a backup to a stream (for download).</summary>
  Task ExportBackupAsync(string backupId, Stream destination, CancellationToken ct = default);

  /// <summary>Imports a backup from a stream (for upload).</summary>
  Task<BackupMetadata> ImportBackupAsync(Stream source, CancellationToken ct = default);
}
