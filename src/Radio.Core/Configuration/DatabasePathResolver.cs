namespace Radio.Core.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Resolves database paths with support for both new unified DatabaseOptions
/// and legacy individual option configurations for backward compatibility.
/// </summary>
public sealed class DatabasePathResolver
{
  private readonly DatabaseOptions _databaseOptions;

  /// <summary>
  /// Initializes a new instance of the DatabasePathResolver class.
  /// </summary>
  /// <param name="databaseOptions">The unified database options.</param>
  public DatabasePathResolver(IOptions<DatabaseOptions> databaseOptions)
  {
    ArgumentNullException.ThrowIfNull(databaseOptions);
    _databaseOptions = databaseOptions.Value;
  }

  /// <summary>
  /// Gets the resolved configuration database path.
  /// Falls back to legacy ConfigurationOptions if DatabaseOptions is not configured.
  /// </summary>
  public string GetConfigurationDatabasePath(string? legacyBasePath = null, string? legacyFileName = null)
  {
    // If legacy values are provided and differ from defaults, use them
    if (!string.IsNullOrEmpty(legacyBasePath) && !string.IsNullOrEmpty(legacyFileName))
    {
      var legacyPath = Path.Combine(legacyBasePath, legacyFileName);
      // Only use legacy if it's explicitly configured (not default)
      if (legacyBasePath != "./config" || legacyFileName != "configuration.db")
      {
        return Path.GetFullPath(legacyPath);
      }
    }

    return Path.GetFullPath(_databaseOptions.GetConfigurationDatabasePath());
  }

  /// <summary>
  /// Gets the resolved metrics database path.
  /// Falls back to legacy MetricsOptions if DatabaseOptions is not configured.
  /// </summary>
  public string GetMetricsDatabasePath(string? legacyDatabasePath = null)
  {
    // If legacy value is provided and differs from default, use it
    if (!string.IsNullOrEmpty(legacyDatabasePath) && legacyDatabasePath != "./data/metrics.db")
    {
      return Path.GetFullPath(legacyDatabasePath);
    }

    return Path.GetFullPath(_databaseOptions.GetMetricsDatabasePath());
  }

  /// <summary>
  /// Gets the resolved fingerprinting database path.
  /// Falls back to legacy FingerprintingOptions if DatabaseOptions is not configured.
  /// </summary>
  public string GetFingerprintingDatabasePath(string? legacyDatabasePath = null)
  {
    // If legacy value is provided and differs from default, use it
    if (!string.IsNullOrEmpty(legacyDatabasePath) && legacyDatabasePath != "./data/fingerprints.db")
    {
      return Path.GetFullPath(legacyDatabasePath);
    }

    return Path.GetFullPath(_databaseOptions.GetFingerprintingDatabasePath());
  }

  /// <summary>
  /// Gets the resolved backup directory path.
  /// </summary>
  public string GetBackupPath(string? legacyBackupPath = null)
  {
    // If legacy value is provided and differs from default, use it
    if (!string.IsNullOrEmpty(legacyBackupPath) && legacyBackupPath != "./config/backups")
    {
      return Path.GetFullPath(legacyBackupPath);
    }

    return Path.GetFullPath(_databaseOptions.GetBackupPath());
  }

  /// <summary>
  /// Gets all database file paths in the system.
  /// </summary>
  public IReadOnlyList<string> GetAllDatabasePaths(
    string? legacyConfigBasePath = null,
    string? legacyConfigFileName = null,
    string? legacyMetricsPath = null,
    string? legacyFingerprintingPath = null)
  {
    return new[]
    {
      GetConfigurationDatabasePath(legacyConfigBasePath, legacyConfigFileName),
      GetMetricsDatabasePath(legacyMetricsPath),
      GetFingerprintingDatabasePath(legacyFingerprintingPath)
    };
  }
}
