namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Specifies the backing store type for configuration data.
/// </summary>
public enum ConfigurationStoreType
{
  /// <summary>JSON file-based storage.</summary>
  Json = 0,

  /// <summary>SQLite database storage.</summary>
  Sqlite = 1
}
