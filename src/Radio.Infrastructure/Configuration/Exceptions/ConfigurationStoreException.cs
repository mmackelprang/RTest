namespace Radio.Infrastructure.Configuration.Exceptions;

/// <summary>
/// Base exception for configuration store operations.
/// </summary>
public class ConfigurationStoreException : Exception
{
  /// <summary>Gets the store ID where the error occurred.</summary>
  public string? StoreId { get; }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreException class.
  /// </summary>
  public ConfigurationStoreException(string message)
    : base(message)
  {
  }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreException class.
  /// </summary>
  public ConfigurationStoreException(string message, string storeId)
    : base(message)
  {
    StoreId = storeId;
  }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreException class.
  /// </summary>
  public ConfigurationStoreException(string message, Exception innerException)
    : base(message, innerException)
  {
  }

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreException class.
  /// </summary>
  public ConfigurationStoreException(string message, string storeId, Exception innerException)
    : base(message, innerException)
  {
    StoreId = storeId;
  }
}
