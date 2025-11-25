namespace Radio.Infrastructure.Configuration.Exceptions;

/// <summary>
/// Exception thrown when a configuration entry is not found.
/// </summary>
public class ConfigurationEntryNotFoundException : ConfigurationStoreException
{
  /// <summary>Gets the key that was not found.</summary>
  public string Key { get; }

  /// <summary>
  /// Initializes a new instance of the ConfigurationEntryNotFoundException class.
  /// </summary>
  public ConfigurationEntryNotFoundException(string key)
    : base($"Configuration entry with key '{key}' was not found.")
  {
    Key = key;
  }

  /// <summary>
  /// Initializes a new instance of the ConfigurationEntryNotFoundException class.
  /// </summary>
  public ConfigurationEntryNotFoundException(string key, string storeId)
    : base($"Configuration entry with key '{key}' was not found in store '{storeId}'.", storeId)
  {
    Key = key;
  }
}
