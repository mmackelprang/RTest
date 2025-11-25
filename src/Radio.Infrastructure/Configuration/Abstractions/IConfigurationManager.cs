namespace Radio.Infrastructure.Configuration.Abstractions;

using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// High-level configuration management interface that orchestrates
/// stores, secrets, and backup operations.
/// </summary>
public interface IConfigurationManager
{
  /// <summary>Gets an existing configuration store by ID.</summary>
  Task<IConfigurationStore> GetStoreAsync(string storeId, CancellationToken ct = default);

  /// <summary>Creates a new configuration store.</summary>
  Task<IConfigurationStore> CreateStoreAsync(string storeId, CancellationToken ct = default);

  /// <summary>Lists all configuration stores.</summary>
  Task<IReadOnlyList<ConfigurationFile>> ListStoresAsync(CancellationToken ct = default);

  /// <summary>Deletes a configuration store.</summary>
  Task<bool> DeleteStoreAsync(string storeId, CancellationToken ct = default);

  /// <summary>Gets a typed value from a specific store.</summary>
  Task<T?> GetValueAsync<T>(string storeId, string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <summary>Sets a typed value in a specific store.</summary>
  Task SetValueAsync<T>(string storeId, string key, T value, CancellationToken ct = default);

  /// <summary>Deletes a value from a specific store.</summary>
  Task<bool> DeleteValueAsync(string storeId, string key, CancellationToken ct = default);

  /// <summary>Creates a new secret and stores its tag reference in the specified store and key.</summary>
  Task<string> CreateSecretAsync(string storeId, string key, string secretValue, CancellationToken ct = default);

  /// <summary>Updates an existing secret value by its tag identifier.</summary>
  Task<bool> UpdateSecretAsync(string tag, string newValue, CancellationToken ct = default);

  /// <summary>Gets the backup service for this configuration manager.</summary>
  IConfigurationBackupService Backup { get; }

  /// <summary>Gets the current store type being used.</summary>
  ConfigurationStoreType CurrentStoreType { get; }
}
