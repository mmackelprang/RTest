namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for database storage across the application.
/// Provides a unified way to configure all SQLite database locations.
/// </summary>
public sealed class DatabaseOptions
{
  /// <summary>
  /// Configuration section name for binding.
  /// </summary>
  public const string SectionName = "Database";

  /// <summary>
  /// Root directory for all database files.
  /// All SQLite databases will be stored within this directory or its subdirectories.
  /// Default: ./data
  /// </summary>
  public string RootPath { get; set; } = "./data";

  /// <summary>
  /// Subdirectory within RootPath for configuration database.
  /// Default: config (results in ./data/config/configuration.db)
  /// </summary>
  public string ConfigurationSubdirectory { get; set; } = "config";

  /// <summary>
  /// Filename for the configuration database.
  /// Default: configuration.db
  /// </summary>
  public string ConfigurationFileName { get; set; } = "configuration.db";

  /// <summary>
  /// Subdirectory within RootPath for fingerprinting database.
  /// Default: fingerprints (results in ./data/fingerprints/fingerprints.db)
  /// </summary>
  public string FingerprintingSubdirectory { get; set; } = "fingerprints";

  /// <summary>
  /// Filename for the fingerprinting database.
  /// Default: fingerprints.db
  /// </summary>
  public string FingerprintingFileName { get; set; } = "fingerprints.db";

  /// <summary>
  /// Subdirectory within RootPath for database backups.
  /// Default: backups (results in ./data/backups)
  /// </summary>
  public string BackupSubdirectory { get; set; } = "backups";

  /// <summary>
  /// Number of days to retain database backups.
  /// Default: 30 days
  /// </summary>
  public int BackupRetentionDays { get; set; } = 30;

  /// <summary>
  /// Gets the full path to the configuration database.
  /// Metrics are also stored in this database.
  /// </summary>
  public string GetConfigurationDatabasePath()
  {
    return Path.Combine(RootPath, ConfigurationSubdirectory, ConfigurationFileName);
  }

  /// <summary>
  /// Gets the full path to the fingerprinting database.
  /// </summary>
  public string GetFingerprintingDatabasePath()
  {
    return Path.Combine(RootPath, FingerprintingSubdirectory, FingerprintingFileName);
  }

  /// <summary>
  /// Gets the full path to the backup directory.
  /// </summary>
  public string GetBackupPath()
  {
    return Path.Combine(RootPath, BackupSubdirectory);
  }

  /// <summary>
  /// Gets all database file paths in the system.
  /// </summary>
  public IReadOnlyList<string> GetAllDatabasePaths()
  {
    return new[]
    {
      GetConfigurationDatabasePath(),
      GetFingerprintingDatabasePath()
    };
  }
}
