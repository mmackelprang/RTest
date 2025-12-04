namespace Radio.Infrastructure.Configuration.Stores;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Factory for creating and managing configuration stores.
/// </summary>
public sealed class ConfigurationStoreFactory : IConfigurationStoreFactory
{
  private readonly ConfigurationOptions _options;
  private readonly ISecretsProvider _secretsProvider;
  private readonly ILoggerFactory _loggerFactory;
  private readonly DatabasePathResolver? _pathResolver;
  private readonly ConcurrentDictionary<string, IConfigurationStore> _storeCache = new();

  /// <summary>
  /// Initializes a new instance of the ConfigurationStoreFactory class.
  /// </summary>
  public ConfigurationStoreFactory(
    IOptions<ConfigurationOptions> options,
    ISecretsProvider secretsProvider,
    ILoggerFactory loggerFactory,
    DatabasePathResolver? pathResolver = null)
  {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(secretsProvider);
    ArgumentNullException.ThrowIfNull(loggerFactory);

    _options = options.Value;
    _secretsProvider = secretsProvider;
    _loggerFactory = loggerFactory;
    _pathResolver = pathResolver;
  }

  /// <inheritdoc/>
  public Task<IConfigurationStore> CreateStoreAsync(string storeId, ConfigurationStoreType storeType, CancellationToken ct = default)
  {
    var cacheKey = $"{storeType}:{storeId}";

    var store = _storeCache.GetOrAdd(cacheKey, _ =>
    {
      return storeType switch
      {
        ConfigurationStoreType.Json => CreateJsonStore(storeId),
        ConfigurationStoreType.Sqlite => CreateSqliteStore(storeId),
        _ => throw new ArgumentOutOfRangeException(nameof(storeType), storeType, "Unknown store type")
      };
    });

    return Task.FromResult(store);
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<string>> ListStoresAsync(ConfigurationStoreType storeType, CancellationToken ct = default)
  {
    var basePath = Path.GetFullPath(_options.BasePath);

    if (storeType == ConfigurationStoreType.Json)
    {
      return Task.FromResult<IReadOnlyList<string>>(ListJsonStores(basePath));
    }
    else
    {
      return Task.FromResult<IReadOnlyList<string>>(ListSqliteStores(basePath));
    }
  }

  /// <inheritdoc/>
  public Task<bool> DeleteStoreAsync(string storeId, ConfigurationStoreType storeType, CancellationToken ct = default)
  {
    var cacheKey = $"{storeType}:{storeId}";
    _storeCache.TryRemove(cacheKey, out _);

    var basePath = Path.GetFullPath(_options.BasePath);

    if (storeType == ConfigurationStoreType.Json)
    {
      var filePath = Path.Combine(basePath, $"{storeId}{_options.JsonExtension}");
      if (File.Exists(filePath))
      {
        File.Delete(filePath);
        return Task.FromResult(true);
      }
    }
    // Note: SQLite stores are in a single database file, so we don't delete the whole DB
    // We would need to drop the table instead, which is a more complex operation

    return Task.FromResult(false);
  }

  /// <inheritdoc/>
  public Task<bool> StoreExistsAsync(string storeId, ConfigurationStoreType storeType, CancellationToken ct = default)
  {
    var basePath = Path.GetFullPath(_options.BasePath);

    if (storeType == ConfigurationStoreType.Json)
    {
      var filePath = Path.Combine(basePath, $"{storeId}{_options.JsonExtension}");
      return Task.FromResult(File.Exists(filePath));
    }
    else
    {
      // For SQLite, check if the database file exists
      var dbPath = Path.Combine(basePath, _options.SqliteFileName);
      return Task.FromResult(File.Exists(dbPath));
    }
  }

  private JsonConfigurationStore CreateJsonStore(string storeId)
  {
    var basePath = Path.GetFullPath(_options.BasePath);
    var filePath = Path.Combine(basePath, $"{storeId}{_options.JsonExtension}");

    return new JsonConfigurationStore(
      storeId,
      filePath,
      _secretsProvider,
      _loggerFactory.CreateLogger<JsonConfigurationStore>(),
      _options.AutoSave);
  }

  private SqliteConfigurationStore CreateSqliteStore(string storeId)
  {
    string dbPath;
    if (_pathResolver != null)
    {
      dbPath = _pathResolver.GetConfigurationDatabasePath(_options.BasePath, _options.SqliteFileName);
    }
    else
    {
      var basePath = Path.GetFullPath(_options.BasePath);
      dbPath = Path.Combine(basePath, _options.SqliteFileName);
    }
    
    var connectionString = $"Data Source={dbPath}";

    return new SqliteConfigurationStore(
      storeId,
      connectionString,
      _secretsProvider,
      _loggerFactory.CreateLogger<SqliteConfigurationStore>());
  }

  private List<string> ListJsonStores(string basePath)
  {
    if (!Directory.Exists(basePath))
      return new List<string>();

    var files = Directory.GetFiles(basePath, $"*{_options.JsonExtension}");
    var storeIds = new List<string>();

    foreach (var file in files)
    {
      var fileName = Path.GetFileNameWithoutExtension(file);
      // Exclude secrets file
      if (!fileName.Equals(_options.SecretsFileName, StringComparison.OrdinalIgnoreCase))
      {
        storeIds.Add(fileName);
      }
    }

    return storeIds;
  }

  private List<string> ListSqliteStores(string basePath)
  {
    var storeIds = new List<string>();
    var dbPath = Path.Combine(basePath, _options.SqliteFileName);

    if (!File.Exists(dbPath))
    {
      return storeIds;
    }

    var connectionString = $"Data Source={dbPath}";
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
    connection.Open();

    // Query sqlite_master for configuration tables
    // Our tables are named with pattern: Config_{storeId}
    const string sql = @"
      SELECT name FROM sqlite_master
      WHERE type = 'table'
        AND name LIKE 'Config_%'
        AND name NOT LIKE 'sqlite_%'";

    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
      var tableName = reader.GetString(0);
      // Extract storeId from table name (remove "Config_" prefix)
      if (tableName.StartsWith("Config_", StringComparison.Ordinal))
      {
        var storeId = tableName["Config_".Length..];
        storeIds.Add(storeId);
      }
    }

    return storeIds;
  }
}
