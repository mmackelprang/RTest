namespace Radio.Infrastructure.Configuration.Secrets;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Stores secrets in a SQLite database table with encryption.
/// </summary>
public sealed class SqliteSecretsProvider : SecretsProviderBase, IAsyncDisposable
{
  private const string TableName = "Secrets";

  private readonly string _connectionString;
  private readonly ILogger<SqliteSecretsProvider> _logger;
  private readonly DatabasePathResolver _pathResolver;
  private readonly SemaphoreSlim _lock = new(1, 1);

  private SqliteConnection? _connection;
  private bool _tableCreated;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the SqliteSecretsProvider class.
  /// </summary>
  public SqliteSecretsProvider(
    DatabasePathResolver pathResolver,
    IDataProtectionProvider dataProtection,
    ILogger<SqliteSecretsProvider> logger)
    : base(dataProtection, logger)
  {
    ArgumentNullException.ThrowIfNull(pathResolver);

    _pathResolver = pathResolver;
    _logger = logger;

    var dbPath = _pathResolver.GetConfigurationDatabasePath();
    _connectionString = $"Data Source={dbPath}";
  }

  /// <inheritdoc/>
  public override async Task<string?> GetSecretAsync(string tag, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"SELECT Value FROM {TableName} WHERE Tag = @Tag";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Tag", tag);

    var result = await cmd.ExecuteScalarAsync(ct);
    if (result is string encrypted)
    {
      // Update last accessed time
      await UpdateLastAccessedAsync(tag, ct);
      return Decrypt(encrypted);
    }
    return null;
  }

  /// <inheritdoc/>
  public override async Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var encrypted = Encrypt(value);
    var now = DateTimeOffset.UtcNow.ToString("O");

    var sql = $@"
      INSERT INTO {TableName} (Tag, Value, CreatedAt, LastAccessedAt)
      VALUES (@Tag, @Value, @CreatedAt, @LastAccessedAt)
      ON CONFLICT(Tag) DO UPDATE SET
        Value = @Value,
        LastAccessedAt = @LastAccessedAt";

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Tag", tag);
    cmd.Parameters.AddWithValue("@Value", encrypted);
    cmd.Parameters.AddWithValue("@CreatedAt", now);
    cmd.Parameters.AddWithValue("@LastAccessedAt", now);

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogInformation("Secret stored with tag: {Tag}", tag);
    return tag;
  }

  /// <inheritdoc/>
  public override async Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var sql = $"DELETE FROM {TableName} WHERE Tag = @Tag";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Tag", tag);

    var deleted = await cmd.ExecuteNonQueryAsync(ct);
    if (deleted > 0)
    {
      _logger.LogInformation("Secret deleted: {Tag}", tag);
      return true;
    }
    return false;
  }

  /// <inheritdoc/>
  public override async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default)
  {
    await EnsureInitializedAsync(ct);

    var tags = new List<string>();
    var sql = $"SELECT Tag FROM {TableName}";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      tags.Add(reader.GetString(0));
    }
    return tags;
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
      CREATE TABLE IF NOT EXISTS {TableName} (
        Tag TEXT PRIMARY KEY,
        Value TEXT NOT NULL,
        CreatedAt TEXT NOT NULL,
        LastAccessedAt TEXT NOT NULL
      )";

    await using var cmd = _connection.CreateCommand();
    cmd.CommandText = createTableSql;
    await cmd.ExecuteNonQueryAsync(ct);

    _tableCreated = true;
    _logger.LogDebug("SQLite secrets provider initialized at {Path}", dbPath);
  }

  private async Task UpdateLastAccessedAsync(string tag, CancellationToken ct)
  {
    var sql = $"UPDATE {TableName} SET LastAccessedAt = @LastAccessedAt WHERE Tag = @Tag";
    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Tag", tag);
    cmd.Parameters.AddWithValue("@LastAccessedAt", DateTimeOffset.UtcNow.ToString("O"));
    await cmd.ExecuteNonQueryAsync(ct);
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
