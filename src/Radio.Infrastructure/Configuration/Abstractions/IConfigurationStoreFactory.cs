namespace Radio.Infrastructure.Configuration.Abstractions;

using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Factory for creating and managing configuration stores.
/// </summary>
public interface IConfigurationStoreFactory
{
  /// <summary>Creates or opens an existing configuration store.</summary>
  Task<IConfigurationStore> CreateStoreAsync(string storeId, ConfigurationStoreType storeType, CancellationToken ct = default);

  /// <summary>Lists all available stores of the specified type.</summary>
  Task<IReadOnlyList<string>> ListStoresAsync(ConfigurationStoreType storeType, CancellationToken ct = default);

  /// <summary>Deletes a store (file or table).</summary>
  Task<bool> DeleteStoreAsync(string storeId, ConfigurationStoreType storeType, CancellationToken ct = default);

  /// <summary>Checks if a store exists.</summary>
  Task<bool> StoreExistsAsync(string storeId, ConfigurationStoreType storeType, CancellationToken ct = default);
}
