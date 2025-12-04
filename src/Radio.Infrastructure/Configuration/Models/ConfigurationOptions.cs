namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Configuration options for the managed configuration system.
/// </summary>
public sealed class ConfigurationOptions
{
  /// <summary>Configuration section name for binding.</summary>
  public const string SectionName = "ManagedConfiguration";

  /// <summary>Default backing store type.</summary>
  public ConfigurationStoreType DefaultStoreType { get; set; } = ConfigurationStoreType.Json;

  /// <summary>
  /// Base path for configuration files.
  /// </summary>
  public string BasePath { get; set; } = "./config";

  /// <summary>File extension for JSON configuration files.</summary>
  public string JsonExtension { get; set; } = ".json";

  /// <summary>
  /// SQLite database filename.
  /// </summary>
  public string SqliteFileName { get; set; } = "configuration.db";

  /// <summary>Secrets storage filename (extension added based on store type).</summary>
  public string SecretsFileName { get; set; } = "secrets";

  /// <summary>
  /// Path for backup files.
  /// </summary>
  public string BackupPath { get; set; } = "./config/backups";

  /// <summary>Whether to auto-save changes.</summary>
  public bool AutoSave { get; set; } = true;

  /// <summary>Number of days to retain backups.</summary>
  public int BackupRetentionDays { get; set; } = 30;

  /// <summary>Debounce delay for auto-save in milliseconds.</summary>
  public int AutoSaveDebounceMs { get; set; } = 5000;
}
