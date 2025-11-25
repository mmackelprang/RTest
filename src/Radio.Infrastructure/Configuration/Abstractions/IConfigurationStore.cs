namespace Radio.Infrastructure.Configuration.Abstractions;

using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Represents a backing store for configuration data (JSON file or SQLite table).
/// Provides CRUD operations for configuration entries.
/// </summary>
public interface IConfigurationStore
{
  /// <summary>Gets the unique identifier for this store (filename or table name).</summary>
  string StoreId { get; }

  /// <summary>Gets the store type (Json or Sqlite).</summary>
  ConfigurationStoreType StoreType { get; }

  /// <summary>Gets a single configuration entry by key.</summary>
  Task<ConfigurationEntry?> GetEntryAsync(string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <summary>Gets all configuration entries from the store.</summary>
  Task<IReadOnlyList<ConfigurationEntry>> GetAllEntriesAsync(ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <summary>Gets all entries with keys starting with the specified section prefix.</summary>
  Task<IReadOnlyList<ConfigurationEntry>> GetEntriesBySectionAsync(string sectionPrefix, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default);

  /// <summary>Sets a single configuration entry.</summary>
  Task SetEntryAsync(string key, string value, CancellationToken ct = default);

  /// <summary>Sets multiple configuration entries.</summary>
  Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default);

  /// <summary>Deletes a configuration entry by key.</summary>
  Task<bool> DeleteEntryAsync(string key, CancellationToken ct = default);

  /// <summary>Checks if an entry with the specified key exists.</summary>
  Task<bool> ExistsAsync(string key, CancellationToken ct = default);

  /// <summary>Persists any pending changes to the backing store.</summary>
  Task<bool> SaveAsync(CancellationToken ct = default);

  /// <summary>Reloads data from the backing store, discarding any pending changes.</summary>
  Task ReloadAsync(CancellationToken ct = default);
}
