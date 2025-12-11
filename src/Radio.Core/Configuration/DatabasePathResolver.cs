namespace Radio.Core.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Resolves database paths using the unified DatabaseOptions configuration.
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
  /// Metrics are also stored in this database.
  /// </summary>
  public string GetConfigurationDatabasePath()
  {
    return Path.GetFullPath(_databaseOptions.GetConfigurationDatabasePath());
  }

  /// <summary>
  /// Gets the resolved fingerprinting database path.
  /// </summary>
  public string GetFingerprintingDatabasePath()
  {
    return Path.GetFullPath(_databaseOptions.GetFingerprintingDatabasePath());
  }

  /// <summary>
  /// Gets the resolved backup directory path.
  /// </summary>
  public string GetBackupPath()
  {
    return Path.GetFullPath(_databaseOptions.GetBackupPath());
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
