# Database Configuration and Unified Backup System

## Overview

The Radio Console application uses multiple SQLite databases for different subsystems:
- **Configuration Database**: Stores application configuration and encrypted secrets
- **Metrics Database**: Stores performance metrics and monitoring data
- **Fingerprinting Database**: Stores audio fingerprints, track metadata, and play history

This document describes the unified database configuration system that provides:
- **Centralized path management** for all databases
- **Backward compatibility** with existing configurations
- **Unified backup and restore** for all databases
- **Consistent directory structure** across deployments

---

## Database Locations

### New Unified Configuration (Recommended)

All databases are now configurable through a single `Database` section in `appsettings.json`:

```json
{
  "Database": {
    "RootPath": "./data",
    "ConfigurationSubdirectory": "config",
    "ConfigurationFileName": "configuration.db",
    "MetricsSubdirectory": "metrics",
    "MetricsFileName": "metrics.db",
    "FingerprintingSubdirectory": "fingerprints",
    "FingerprintingFileName": "fingerprints.db",
    "BackupSubdirectory": "backups",
    "BackupRetentionDays": 30
  }
}
```

With these settings, the databases will be located at:
- Configuration: `./data/config/configuration.db`
- Metrics: `./data/metrics/metrics.db`
- Fingerprinting: `./data/fingerprints/fingerprints.db`
- Backups: `./data/backups/`

### Legacy Configuration (Still Supported)

For backward compatibility, the old configuration format is still supported:

```json
{
  "ManagedConfiguration": {
    "BasePath": "./config",
    "SqliteFileName": "configuration.db",
    "BackupPath": "./config/backups"
  },
  "Metrics": {
    "DatabasePath": "./data/metrics.db"
  },
  "Fingerprinting": {
    "DatabasePath": "./data/fingerprints.db"
  }
}
```

**Note**: If you specify non-default values in the legacy configuration, those will take precedence over the new unified configuration. This ensures existing deployments continue to work without changes.

---

## Unified Backup System

### Overview

The `IUnifiedDatabaseBackupService` provides automated backup and restore functionality for **all** SQLite databases in a single operation. This ensures consistent state across the entire application.

### Creating a Backup

Backups can be created programmatically:

```csharp
public class MyService
{
  private readonly IUnifiedDatabaseBackupService _backupService;

  public MyService(IUnifiedDatabaseBackupService backupService)
  {
    _backupService = backupService;
  }

  public async Task CreateBackup()
  {
    // Create a backup of all databases
    var backup = await _backupService.CreateFullBackupAsync("Daily backup");
    
    Console.WriteLine($"Backup created: {backup.BackupId}");
    Console.WriteLine($"Size: {backup.SizeBytes / 1024} KB");
    Console.WriteLine($"Databases included: {string.Join(", ", backup.IncludedDatabases)}");
  }
}
```

### Backup File Format

Backups are stored as compressed ZIP archives with the `.dbbackup` extension. Each backup contains:

1. **databases/** folder - Contains all SQLite database files
   - `configuration.db`
   - `metrics.db`
   - `fingerprints.db`

2. **manifest.json** - Backup metadata
   ```json
   {
     "version": 1,
     "backupId": "unified_20231204_143022_a1b2c3",
     "createdAt": "2023-12-04T14:30:22Z",
     "description": "Daily backup",
     "includedDatabases": ["configuration.db", "metrics.db", "fingerprints.db"],
     "includesSecrets": true
   }
   ```

3. **README.txt** - Human-readable backup information

### Backup Naming Convention

Backups are named with the pattern: `unified_YYYYMMDD_HHMMSS_XXXXXX.dbbackup`

Example: `unified_20231204_143022_a1b2c3.dbbackup`

### Restoring from Backup

```csharp
public async Task RestoreFromBackup(string backupId)
{
  // Restore all databases from a backup
  // Use overwrite: true to replace existing databases
  await _backupService.RestoreBackupAsync(backupId, overwrite: true);
  
  Console.WriteLine("All databases restored successfully");
}
```

### Listing Available Backups

```csharp
public async Task ListBackups()
{
  var backups = await _backupService.ListBackupsAsync();
  
  foreach (var backup in backups)
  {
    Console.WriteLine($"ID: {backup.BackupId}");
    Console.WriteLine($"Created: {backup.CreatedAt}");
    Console.WriteLine($"Description: {backup.Description}");
    Console.WriteLine($"Size: {backup.SizeBytes / 1024} KB");
    Console.WriteLine($"Includes secrets: {backup.IncludesSecrets}");
    Console.WriteLine();
  }
}
```

### Automatic Cleanup

Old backups are automatically cleaned up based on the `BackupRetentionDays` setting:

```csharp
public async Task CleanupOldBackups()
{
  // Deletes backups older than BackupRetentionDays
  var deletedCount = await _backupService.CleanupOldBackupsAsync();
  
  Console.WriteLine($"Deleted {deletedCount} old backups");
}
```

### Exporting and Importing Backups

Backups can be exported for download or imported from external sources:

```csharp
// Export a backup to download
public async Task ExportBackup(string backupId, Stream outputStream)
{
  await _backupService.ExportBackupAsync(backupId, outputStream);
}

// Import a backup from upload
public async Task<UnifiedBackupMetadata> ImportBackup(Stream inputStream)
{
  var backup = await _backupService.ImportBackupAsync(inputStream);
  return backup;
}
```

---

## Security Considerations

### Encrypted Secrets

The configuration database contains encrypted secrets (API keys, passwords, etc.). When creating backups:

1. Secrets remain encrypted within the backup
2. The `IncludesSecrets` flag is set to `true` in the manifest
3. The README.txt file includes a security warning

**Important**: Always store backup files securely and encrypt them when transmitting or storing externally.

### Access Control

Ensure that:
- Database files have appropriate file system permissions
- Backup directory is protected from unauthorized access
- Production backups are stored in secure, encrypted storage

---

## Deployment Considerations

### Directory Structure

Recommended production directory structure:

```
/var/radioconsole/
├── data/
│   ├── config/
│   │   └── configuration.db
│   ├── metrics/
│   │   └── metrics.db
│   ├── fingerprints/
│   │   └── fingerprints.db
│   └── backups/
│       ├── unified_20231204_143022_a1b2c3.dbbackup
│       └── unified_20231205_143022_b2c3d4.dbbackup
└── logs/
```

### Backup Strategy

Recommended backup approach:

1. **Automated Daily Backups**: Schedule daily backups using a background service
2. **Pre-Update Backups**: Create a backup before any application updates
3. **Off-Site Storage**: Copy backups to remote/cloud storage
4. **Retention Policy**: Keep last 30 days locally, archive older backups off-site
5. **Testing**: Regularly test restore procedures

### Example Background Service

```csharp
public class DatabaseBackupService : BackgroundService
{
  private readonly IUnifiedDatabaseBackupService _backupService;
  private readonly ILogger<DatabaseBackupService> _logger;
  private readonly TimeSpan _interval = TimeSpan.FromHours(24);

  public DatabaseBackupService(
    IUnifiedDatabaseBackupService backupService,
    ILogger<DatabaseBackupService> logger)
  {
    _backupService = backupService;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        // Create daily backup
        var backup = await _backupService.CreateFullBackupAsync(
          $"Automated backup at {DateTime.UtcNow:yyyy-MM-dd}",
          stoppingToken);
        
        _logger.LogInformation(
          "Created backup {BackupId} ({Size} bytes)",
          backup.BackupId,
          backup.SizeBytes);

        // Clean up old backups
        var deleted = await _backupService.CleanupOldBackupsAsync(stoppingToken);
        if (deleted > 0)
        {
          _logger.LogInformation("Cleaned up {Count} old backups", deleted);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to create automated backup");
      }

      await Task.Delay(_interval, stoppingToken);
    }
  }
}
```

---

## Migration Guide

### Migrating from Legacy Configuration

To migrate to the new unified database configuration:

1. **Update appsettings.json**:
   ```json
   {
     "Database": {
       "RootPath": "./data"
     }
   }
   ```

2. **Move Database Files** (if needed):
   ```bash
   # Create new directory structure
   mkdir -p ./data/config
   mkdir -p ./data/metrics
   mkdir -p ./data/fingerprints
   mkdir -p ./data/backups

   # Move existing databases
   mv ./config/configuration.db ./data/config/
   mv ./data/metrics.db ./data/metrics/
   mv ./data/fingerprints.db ./data/fingerprints/

   # Move existing backups
   mv ./config/backups/* ./data/backups/
   ```

3. **Remove Legacy Configuration**:
   - You can remove the old `ManagedConfiguration.BasePath`, `Metrics.DatabasePath`, and `Fingerprinting.DatabasePath` settings
   - Or keep them for rollback capability

4. **Test the Application**:
   - Verify all databases are accessible
   - Create a test backup
   - Verify backup contents

### No-Downtime Migration

For production systems, use this approach:

1. Keep legacy configuration in place
2. Add new `Database` configuration
3. Verify application starts and works correctly
4. Create a unified backup as verification
5. Gradually move databases to new locations during maintenance window
6. Update configuration to use new paths
7. Remove legacy configuration after successful migration

---

## Troubleshooting

### Database Not Found

**Problem**: Application can't find database files after configuration change.

**Solution**: 
1. Check that `Database.RootPath` is correct
2. Verify database files exist at expected locations
3. Check file permissions
4. Review application logs for specific path being used

### Backup Fails

**Problem**: Backup creation fails with permission error.

**Solution**:
1. Ensure backup directory exists and is writable
2. Check disk space
3. Verify database files are not locked by other processes

### Restore Fails

**Problem**: Restore operation fails or produces corrupt databases.

**Solution**:
1. Verify backup file integrity (not truncated or corrupted)
2. Ensure no other processes are accessing the database files
3. Try restoring to a different location first as a test
4. Check application logs for specific error messages

---

## API Reference

### DatabaseOptions

Configuration class for database path management.

**Properties**:
- `RootPath` (string): Root directory for all databases (default: "./data")
- `ConfigurationSubdirectory` (string): Subdirectory for configuration database (default: "config")
- `ConfigurationFileName` (string): Configuration database filename (default: "configuration.db")
- `MetricsSubdirectory` (string): Subdirectory for metrics database (default: "metrics")
- `MetricsFileName` (string): Metrics database filename (default: "metrics.db")
- `FingerprintingSubdirectory` (string): Subdirectory for fingerprinting database (default: "fingerprints")
- `FingerprintingFileName` (string): Fingerprinting database filename (default: "fingerprints.db")
- `BackupSubdirectory` (string): Subdirectory for backups (default: "backups")
- `BackupRetentionDays` (int): Number of days to retain backups (default: 30)

**Methods**:
- `GetConfigurationDatabasePath()`: Returns full path to configuration database
- `GetMetricsDatabasePath()`: Returns full path to metrics database
- `GetFingerprintingDatabasePath()`: Returns full path to fingerprinting database
- `GetBackupPath()`: Returns full path to backup directory
- `GetAllDatabasePaths()`: Returns array of all database paths

### IUnifiedDatabaseBackupService

Interface for unified database backup operations.

**Methods**:
- `CreateFullBackupAsync(description?, ct)`: Creates backup of all databases
- `RestoreBackupAsync(backupId, overwrite, ct)`: Restores databases from backup
- `ListBackupsAsync(ct)`: Lists all available backups
- `DeleteBackupAsync(backupId, ct)`: Deletes a backup
- `ExportBackupAsync(backupId, destination, ct)`: Exports backup to stream
- `ImportBackupAsync(source, ct)`: Imports backup from stream
- `CleanupOldBackupsAsync(ct)`: Deletes backups older than retention period

### UnifiedBackupMetadata

Information about a backup.

**Properties**:
- `BackupId` (string): Unique backup identifier
- `CreatedAt` (DateTimeOffset): When backup was created
- `Description` (string?): Optional description
- `SizeBytes` (long): Size of backup file in bytes
- `FilePath` (string): Path to backup file
- `IncludedDatabases` (IReadOnlyList<string>): List of databases in backup
- `IncludesSecrets` (bool): Whether backup contains encrypted secrets

---

## See Also

- [CONFIGURATION.md](./CONFIGURATION.md) - General configuration infrastructure
- [METRICS.md](./METRICS.md) - Metrics system documentation
- [SOUNDFINGERPRINTING.md](./SOUNDFINGERPRINTING.md) - Audio fingerprinting documentation
