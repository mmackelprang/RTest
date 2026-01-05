using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Configuration.Stores;

/// <summary>
/// SQLite-based implementation of configuration store
/// </summary>
public class SqliteConfigurationStore : ConfigurationStore
{
  private readonly string _connectionString;
  private readonly string _tableName;
  private SqliteConnection? _connection;
  private bool _isInitialized;
  private readonly SemaphoreSlim _initLock = new(1, 1);

  /// <summary>
  /// Initializes a new instance of the <see cref="SqliteConfigurationStore"/> class.
  /// </summary>
  /// <param name="storeId">Unique identifier for this configuration store</param>
  /// <param name="connectionString">SQLite connection string</param>
  /// <param name="tableName">Name of the table to use for configuration storage</param>
  /// <param name="logger">Logger instance</param>
  public SqliteConfigurationStore(
    string storeId,
    string connectionString,
    string tableName,
    ILogger<SqliteConfigurationStore> logger)
    : base(storeId, logger)
  {
    _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
  }

  /// <summary>
  /// Ensures the store is initialized with a database connection and table
  /// </summary>
  private async Task EnsureInitializedAsync(CancellationToken ct = default)
  {
    if (_isInitialized)
      return;

    await _initLock.WaitAsync(ct);
    try
    {
      if (_isInitialized)
        return;

      _connection = new SqliteConnection(_connectionString);
      await _connection.OpenAsync(ct);

      // Create table if it doesn't exist
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

      _isInitialized = true;
      Logger.LogDebug("Initialized SQLite configuration store {StoreId} with table {TableName}", StoreId, _tableName);
    }
    finally
    {
      _initLock.Release();
    }
  }

  /// <inheritdoc/>
  public override async Task<ConfigurationEntry?> GetEntryAsync(string key, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"SELECT Key, Value, Description, LastModified FROM {_tableName} WHERE Key = @Key";

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Key", key);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (await reader.ReadAsync(ct))
    {
      return new ConfigurationEntry
      {
        Key = reader.GetString(0),
        Value = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        LastModified = DateTimeOffset.Parse(reader.GetString(3))
      };
    }

    return null;
  }

  /// <inheritdoc/>
  public override async Task<IEnumerable<ConfigurationEntry>> GetAllEntriesAsync(CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"SELECT Key, Value, Description, LastModified FROM {_tableName}";
    var entries = new List<ConfigurationEntry>();

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      entries.Add(new ConfigurationEntry
      {
        Key = reader.GetString(0),
        Value = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        LastModified = DateTimeOffset.Parse(reader.GetString(3))
      });
    }

    return entries;
  }

  /// <inheritdoc/>
  public override async Task SetEntryAsync(ConfigurationEntry entry, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var now = DateTimeOffset.UtcNow.ToString("O");
    var sql = $@"
      INSERT INTO {_tableName} (Key, Value, Description, LastModified)
      VALUES (@Key, @Value, @Description, @LastModified)
      ON CONFLICT(Key) DO UPDATE SET
        Value = @Value,
        Description = @Description,
        LastModified = @LastModified";

    await using var cmd = _connection!.CreateCommand();
    cmd.Parameters.AddWithValue("@Key", entry.Key);
    cmd.Parameters.AddWithValue("@Value", entry.RawValue ?? entry.Value);
    cmd.Parameters.AddWithValue("@Description", (object?)entry.Description ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@LastModified", now);

    await cmd.ExecuteNonQueryAsync(ct);

    Logger.LogDebug("Set entry {Key} in store {StoreId}", entry.Key, StoreId);
  }

  /// <inheritdoc/>
  public override async Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var existingTransaction = _connection!.Transaction;
    var transactionOwner = existingTransaction == null;
    
    var transaction = existingTransaction ?? await _connection!.BeginTransactionAsync(ct);
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
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Key", entry.Key);
        cmd.Parameters.AddWithValue("@Value", entry.RawValue ?? entry.Value);
        cmd.Parameters.AddWithValue("@Description", (object?)entry.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LastModified", now);

        await cmd.ExecuteNonQueryAsync(ct);
      }

      if (transactionOwner)
      {
        await transaction.CommitAsync(ct);
      }
      
      Logger.LogDebug("Set multiple entries in store {StoreId}", StoreId);
    }
    catch
    {
      if (transactionOwner)
      {
        await transaction.RollbackAsync(ct);
      }
      throw;
    }
    finally
    {
      if (transactionOwner)
      {
        await transaction.DisposeAsync();
      }
    }
  }

  /// <inheritdoc/>
  public override async Task DeleteEntryAsync(string key, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"DELETE FROM {_tableName} WHERE Key = @Key";

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Key", key);

    await cmd.ExecuteNonQueryAsync(ct);

    Logger.LogDebug("Deleted entry {Key} from store {StoreId}", key, StoreId);
  }

  /// <inheritdoc/>
  public override async Task ClearAsync(CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"DELETE FROM {_tableName}";

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;

    await cmd.ExecuteNonQueryAsync(ct);

    Logger.LogDebug("Cleared all entries from store {StoreId}", StoreId);
  }

  /// <inheritdoc/>
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _connection?.Dispose();
      _initLock.Dispose();
    }

    base.Dispose(disposing);
  }

  /// <inheritdoc/>
  public override async ValueTask DisposeAsync()
  {
    if (_connection != null)
    {
      await _connection.DisposeAsync();
    }

    _initLock.Dispose();

    await base.DisposeAsync();
  }
}
