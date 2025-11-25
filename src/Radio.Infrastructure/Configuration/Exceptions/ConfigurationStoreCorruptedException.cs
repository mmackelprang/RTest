namespace Radio.Infrastructure.Configuration.Exceptions;

/// <summary>
/// Exception thrown when a configuration store is corrupted or invalid.
/// </summary>
public class ConfigurationStoreCorruptedException : ConfigurationStoreException
{
  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreCorruptedException class.
  /// </summary>
  public ConfigurationStoreCorruptedException(string storeId)
    : base($"Configuration store '{storeId}' is corrupted or invalid.", storeId)
  {
  }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreCorruptedException class.
  /// </summary>
  public ConfigurationStoreCorruptedException(string storeId, Exception innerException)
    : base($"Configuration store '{storeId}' is corrupted or invalid.", storeId, innerException)
  {
  }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreCorruptedException class.
  /// </summary>
  public ConfigurationStoreCorruptedException(string message, string storeId, Exception innerException)
    : base(message, storeId, innerException)
  {
  }
}
