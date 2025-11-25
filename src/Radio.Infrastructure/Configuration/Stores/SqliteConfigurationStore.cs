namespace Radio.Infrastructure.Configuration.Stores;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// SQLite database-based configuration store implementation.
/// </summary>
public sealed class SqliteConfigurationStore : IConfigurationStore, IAsyncDisposable
{
  private readonly string _connectionString;
  private readonly string _tableName;
  private readonly ISecretsProvider _secretsProvider;
  private readonly ILogger<SqliteConfigurationStore> _logger;
  private readonly SemaphoreSlim _lock = new(1, 1);

  private SqliteConnection? _connection;
  private bool _tableCreated;
  private bool _disposed;

  /// <inheritdoc/>
  public string StoreId { get; }

  /// <inheritdoc/>
  public ConfigurationStoreType StoreType => ConfigurationStoreType.Sqlite;

  /// <summary>
  /// Initializes a new instance of the SqliteConfigurationStore class.
  /// </summary>
  public SqliteConfigurationStore(
    string storeId,
    string connectionString,
    ISecretsProvider secretsProvider,
    ILogger<SqliteConfigurationStore> logger)
  {
    StoreId = storeId ?? throw new ArgumentNullException(nameof(storeId));
    _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    _secretsProvider = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Use storeId as table name (sanitized)
    _tableName = $"Config_{SanitizeTableName(storeId)}";
  }

  /// <inheritdoc/>
  public async Task<ConfigurationEntry?> GetEntryAsync(string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"SELECT Value, Description, LastModified FROM {_tableName} WHERE Key = @Key";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Key", key);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (await reader.ReadAsync(ct))
    {
      return await CreateEntryAsync(
        key,
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
        mode,
        ct);
    }
    return null;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<ConfigurationEntry>> GetAllEntriesAsync(ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var entries = new List<ConfigurationEntry>();
    var sql = $"SELECT Key, Value, Description, LastModified FROM {_tableName}";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      entries.Add(await CreateEntryAsync(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
        mode,
        ct));
    }
    return entries;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<ConfigurationEntry>> GetEntriesBySectionAsync(string sectionPrefix, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var prefix = sectionPrefix.EndsWith(':') ? sectionPrefix : $"{sectionPrefix}:";
    var entries = new List<ConfigurationEntry>();

    var sql = $"SELECT Key, Value, Description, LastModified FROM {_tableName} WHERE Key LIKE @Prefix";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Prefix", $"{prefix}%");

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      entries.Add(await CreateEntryAsync(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
        mode,
        ct));
    }
    return entries;
  }

  /// <inheritdoc/>
  public async Task SetEntryAsync(string key, string value, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var now = DateTimeOffset.UtcNow.ToString("O");
    var sql = $@"
      INSERT INTO {_tableName} (Key, Value, LastModified)
      VALUES (@Key, @Value, @LastModified)
      ON CONFLICT(Key) DO UPDATE SET
        Value = @Value,
        LastModified = @LastModified";

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Key", key);
    cmd.Parameters.AddWithValue("@Value", value);
    cmd.Parameters.AddWithValue("@LastModified", now);

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogDebug("Set entry {Key} in store {StoreId}", key, StoreId);
  }

  /// <inheritdoc/>
  public async Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    await using var transaction = await _connection!.BeginTransactionAsync(ct);
    try
    {
      foreach (var entry in entries)
      {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var sql = $@"
          INSERT INTO {_tableName} (Key, Value, Description, LastModified)
          VALUES (@Key, @Value, @Description, @LastModified)
          ON CONFLICT(Key) DO UPDATE SET
            Value = @Value,
            Description = @Description,
            LastModified = @LastModified";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Key", entry.Key);
        cmd.Parameters.AddWithValue("@Value", entry.RawValue ?? entry.Value);
        cmd.Parameters.AddWithValue("@Description", (object?)entry.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LastModified", now);

        await cmd.ExecuteNonQueryAsync(ct);
      }

      await transaction.CommitAsync(ct);
      _logger.LogDebug("Set multiple entries in store {StoreId}", StoreId);
    }
    catch
    {
      await transaction.RollbackAsync(ct);
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteEntryAsync(string key, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"DELETE FROM {_tableName} WHERE Key = @Key";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Key", key);

    var deleted = await cmd.ExecuteNonQueryAsync(ct);
    if (deleted > 0)
    {
      _logger.LogDebug("Deleted entry {Key} from store {StoreId}", key, StoreId);
      return true;
    }
    return false;
  }

  /// <inheritdoc/>
  public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"SELECT 1 FROM {_tableName} WHERE Key = @Key LIMIT 1";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Key", key);

    var result = await cmd.ExecuteScalarAsync(ct);
    return result != null;
  }

  /// <inheritdoc/>
  public Task<bool> SaveAsync(CancellationToken ct = default)
  {
    // SQLite auto-commits, so nothing to do
    return Task.FromResult(true);
  }

  /// <inheritdoc/>
  public async Task ReloadAsync(CancellationToken ct = default)
  {
    // Close and reopen connection to pick up any external changes
    if (_connection != null)
    {
      await _connection.CloseAsync();
      await _connection.OpenAsync(ct);
    }
  }

  private async Task EnsureInitializedAsync(CancellationToken ct)
  {
    if (_tableCreated && _connection?.State == System.Data.ConnectionState.Open)
      return;

    await _lock.WaitAsync(ct);
    try
    {
      if (_tableCreated && _connection?.State == System.Data.ConnectionState.Open)
        return;

      await InitializeAsync(ct);
    }
    finally
    {
      _lock.Release();
    }
  }

  private async Task InitializeAsync(CancellationToken ct)
  {
    // Ensure directory exists
    var builder = new SqliteConnectionStringBuilder(_connectionString);
    var dbPath = builder.DataSource;
    var directory = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    _connection = new SqliteConnection(_connectionString);
    await _connection.OpenAsync(ct);

    var createTableSql = $@"
      CREATE TABLE IF NOT EXISTS {_tableName} (
        Key TEXT PRIMARY KEY,
        Value TEXT NOT NULL,
        Description TEXT,
        LastModified TEXT NOT NULL
      )";

    await using var cmd = _connection.CreateCommand();
    cmd.CommandText = createTableSql;
    await cmd.ExecuteNonQueryAsync(ct);

    _tableCreated = true;
    _logger.LogDebug("SQLite configuration store initialized: {StoreId}", StoreId);
  }

  private async Task<ConfigurationEntry> CreateEntryAsync(
    string key,
    string rawValue,
    string? description,
    DateTimeOffset? lastModified,
    ConfigurationReadMode mode,
    CancellationToken ct)
  {
    var containsSecret = _secretsProvider.ContainsSecretTag(rawValue);

    string resolvedValue;
    if (mode == ConfigurationReadMode.Resolved && containsSecret)
    {
      resolvedValue = await _secretsProvider.ResolveTagsAsync(rawValue, ct);
    }
    else
    {
      resolvedValue = rawValue;
    }

    return new ConfigurationEntry
    {
      Key = key,
      Value = resolvedValue,
      RawValue = containsSecret ? rawValue : null,
      ContainsSecret = containsSecret,
      LastModified = lastModified,
      Description = description
    };
  }

  private static string SanitizeTableName(string name)
  {
    // Only allow alphanumeric and underscore
    return new string(name
      .Replace("-", "_")
      .Replace(".", "_")
      .Where(c => char.IsLetterOrDigit(c) || c == '_')
      .ToArray());
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;

    if (_connection != null)
    {
      await _connection.DisposeAsync();
    }

    _lock.Dispose();
    _disposed = true;
  }
}
