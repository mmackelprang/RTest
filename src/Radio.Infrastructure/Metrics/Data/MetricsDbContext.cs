namespace Radio.Infrastructure.Metrics.Data;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using System.Collections.Concurrent;

/// <summary>
/// Database context for metrics storage using SQLite.
/// Manages the connection and schema for metrics tables.
/// </summary>
public sealed class MetricsDbContext : IAsyncDisposable
{
  private readonly ILogger<MetricsDbContext> _logger;
  private readonly MetricsOptions _options;
  private readonly SemaphoreSlim _initLock = new(1, 1);
  private readonly ConcurrentDictionary<string, int> _metricDefinitionCache = new();
  
  private SqliteConnection? _connection;
  private bool _isInitialized;

  public MetricsDbContext(ILogger<MetricsDbContext> logger, IOptions<MetricsOptions> options)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
  }

  /// <summary>
  /// Initializes the database schema if not already created.
  /// </summary>
  public async Task InitializeAsync(CancellationToken ct = default)
  {
    if (_isInitialized)
    {
      return;
    }

    await _initLock.WaitAsync(ct);
    try
    {
      if (_isInitialized)
      {
        return;
      }

      // Ensure the directory exists
      var dbPath = Path.GetFullPath(_options.DatabasePath);
      var directory = Path.GetDirectoryName(dbPath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      // Create connection
      var connectionString = $"Data Source={dbPath}";
      _connection = new SqliteConnection(connectionString);
      await _connection.OpenAsync(ct);

      // Create schema
      await CreateSchemaAsync(ct);

      // Load metric definitions cache
      await LoadMetricDefinitionsCacheAsync(ct);

      _isInitialized = true;
      _logger.LogInformation("Metrics database initialized at {Path}", dbPath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize metrics database");
      throw;
    }
    finally
    {
      _initLock.Release();
    }
  }

  /// <summary>
  /// Gets the database connection.
  /// </summary>
  public SqliteConnection Connection
  {
    get
    {
      if (!_isInitialized || _connection == null)
      {
        throw new InvalidOperationException("MetricsDbContext is not initialized. Call InitializeAsync first.");
      }
      return _connection;
    }
  }

  /// <summary>
  /// Gets or creates a metric definition ID.
  /// Uses an in-memory cache to avoid repeated database lookups.
  /// </summary>
  public async Task<int> GetOrCreateMetricDefinitionIdAsync(
    string key,
    int type,
    string? unit,
    CancellationToken ct = default)
  {
    // Check cache first
    if (_metricDefinitionCache.TryGetValue(key, out var cachedId))
    {
      return cachedId;
    }

    // Query database
    await using var cmd = Connection.CreateCommand();
    cmd.CommandText = "SELECT Id FROM MetricDefinitions WHERE Key = @Key";
    cmd.Parameters.AddWithValue("@Key", key);

    var result = await cmd.ExecuteScalarAsync(ct);
    if (result != null)
    {
      var id = Convert.ToInt32(result);
      _metricDefinitionCache.TryAdd(key, id);
      return id;
    }

    // Insert new definition
    cmd.CommandText = @"
      INSERT INTO MetricDefinitions (Key, Type, Unit)
      VALUES (@Key, @Type, @Unit)
      RETURNING Id";
    cmd.Parameters.Clear();
    cmd.Parameters.AddWithValue("@Key", key);
    cmd.Parameters.AddWithValue("@Type", type);
    cmd.Parameters.AddWithValue("@Unit", unit ?? (object)DBNull.Value);

    result = await cmd.ExecuteScalarAsync(ct);
    var newId = Convert.ToInt32(result!);
    _metricDefinitionCache.TryAdd(key, newId);

    _logger.LogDebug("Created metric definition: {Key} (ID: {Id})", key, newId);
    return newId;
  }

  private async Task CreateSchemaAsync(CancellationToken ct)
  {
    await using var cmd = Connection.CreateCommand();

    // MetricDefinitions table
    cmd.CommandText = @"
      CREATE TABLE IF NOT EXISTS MetricDefinitions (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Key TEXT NOT NULL UNIQUE,
        Type INTEGER NOT NULL,
        Unit TEXT
      );
      CREATE INDEX IF NOT EXISTS idx_metric_key ON MetricDefinitions(Key);";
    await cmd.ExecuteNonQueryAsync(ct);

    // MetricData_Minute table
    cmd.CommandText = @"
      CREATE TABLE IF NOT EXISTS MetricData_Minute (
        MetricId INTEGER NOT NULL,
        Timestamp INTEGER NOT NULL,
        ValueSum REAL NOT NULL,
        ValueCount INTEGER NOT NULL,
        ValueMin REAL,
        ValueMax REAL,
        ValueLast REAL,
        PRIMARY KEY (MetricId, Timestamp),
        FOREIGN KEY (MetricId) REFERENCES MetricDefinitions(Id)
      );
      CREATE INDEX IF NOT EXISTS idx_minute_timestamp ON MetricData_Minute(Timestamp);";
    await cmd.ExecuteNonQueryAsync(ct);

    // MetricData_Hour table
    cmd.CommandText = @"
      CREATE TABLE IF NOT EXISTS MetricData_Hour (
        MetricId INTEGER NOT NULL,
        Timestamp INTEGER NOT NULL,
        ValueSum REAL NOT NULL,
        ValueCount INTEGER NOT NULL,
        ValueMin REAL,
        ValueMax REAL,
        ValueLast REAL,
        PRIMARY KEY (MetricId, Timestamp),
        FOREIGN KEY (MetricId) REFERENCES MetricDefinitions(Id)
      );
      CREATE INDEX IF NOT EXISTS idx_hour_timestamp ON MetricData_Hour(Timestamp);";
    await cmd.ExecuteNonQueryAsync(ct);

    // MetricData_Day table
    cmd.CommandText = @"
      CREATE TABLE IF NOT EXISTS MetricData_Day (
        MetricId INTEGER NOT NULL,
        Timestamp INTEGER NOT NULL,
        ValueSum REAL NOT NULL,
        ValueCount INTEGER NOT NULL,
        ValueMin REAL,
        ValueMax REAL,
        ValueLast REAL,
        PRIMARY KEY (MetricId, Timestamp),
        FOREIGN KEY (MetricId) REFERENCES MetricDefinitions(Id)
      );
      CREATE INDEX IF NOT EXISTS idx_day_timestamp ON MetricData_Day(Timestamp);";
    await cmd.ExecuteNonQueryAsync(ct);

    _logger.LogDebug("Metrics database schema created");
  }

  private async Task LoadMetricDefinitionsCacheAsync(CancellationToken ct)
  {
    await using var cmd = Connection.CreateCommand();
    cmd.CommandText = "SELECT Id, Key FROM MetricDefinitions";

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      var id = reader.GetInt32(0);
      var key = reader.GetString(1);
      _metricDefinitionCache.TryAdd(key, id);
    }

    _logger.LogDebug("Loaded {Count} metric definitions into cache", _metricDefinitionCache.Count);
  }

  public async ValueTask DisposeAsync()
  {
    if (_connection != null)
    {
      await _connection.DisposeAsync();
      _connection = null;
    }

    _initLock.Dispose();
    _metricDefinitionCache.Clear();
  }
}
