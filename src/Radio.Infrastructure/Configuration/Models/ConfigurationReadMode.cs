namespace Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Specifies how configuration values should be read.
/// </summary>
public enum ConfigurationReadMode
{
  /// <summary>
  /// Returns values with secret tags resolved to actual secret values.
  /// This is the default mode for normal application use.
  /// </summary>
  Resolved = 0,

  /// <summary>
  /// Returns raw values with secret tags intact (e.g., "${secret:abc123}").
  /// Use this mode for UI configuration management.
  /// </summary>
  Raw = 1
}
