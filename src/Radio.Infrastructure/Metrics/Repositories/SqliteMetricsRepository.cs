namespace Radio.Infrastructure.Metrics.Repositories;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Core.Metrics;
using Radio.Infrastructure.Metrics.Data;
using System.Data.Common;

/// <summary>
/// Repository for storing and retrieving metrics from SQLite.
/// </summary>
public sealed class SqliteMetricsRepository : IMetricsReader
{
  private readonly ILogger<SqliteMetricsRepository> _logger;
  private readonly MetricsDbContext _dbContext;
  private readonly SemaphoreSlim _transactionLock = new(1, 1);
  private SqliteTransaction? _currentTransaction;

  public SqliteMetricsRepository(
    ILogger<SqliteMetricsRepository> logger,
    MetricsDbContext dbContext)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
  }

  /// <summary>
  /// Saves a batch of metric buckets to the database.
  /// Upserts data into the appropriate resolution table.
  /// </summary>
  public async Task SaveBucketsAsync(
    string key,
    MetricType metricType,
    string? unit,
    MetricResolution resolution,
    IEnumerable<MetricBucket> buckets,
    CancellationToken ct = default)
  {
    if (!buckets.Any())
    {
      return;
    }

    var tableName = resolution switch
    {
      MetricResolution.Minute => "MetricData_Minute",
      MetricResolution.Hour => "MetricData_Hour",
      MetricResolution.Day => "MetricData_Day",
      _ => throw new ArgumentException($"Invalid resolution: {resolution}")
    };

    // Acquire lock to prevent concurrent transactions on the same connection
    await _transactionLock.WaitAsync(ct);
    try
    {
      // Check if connection is still open before proceeding
      try
      {
        if (_dbContext.Connection.State != System.Data.ConnectionState.Open)
        {
          _logger.LogDebug("Database connection is not open, skipping save for {Key}", key);
          return;
        }
      }
      catch (ObjectDisposedException)
      {
        _logger.LogDebug("Database connection disposed, skipping save for {Key}", key);
        return;
      }

      var existingTransaction = _currentTransaction;
      var transactionOwner = existingTransaction == null;
      SqliteTransaction transaction;
      try
      {
        transaction = existingTransaction ?? (await _dbContext.Connection.BeginTransactionAsync(ct) as SqliteTransaction)!;
      }
      catch (ObjectDisposedException)
      {
        _logger.LogDebug("Database connection disposed during transaction begin, skipping save for {Key}", key);
        return;
      }

      if (transactionOwner)
      {
        _currentTransaction = transaction;
      }

      try
      {
        // Resolve metric definition inside the transaction lock and pass the
        // transaction so the command is associated with the same connection/
        // transaction pair.  Previously this was called outside the lock, which
        // caused "transaction not associated with this command" errors when a
        // concurrent flush had already begun a transaction on the shared
        // connection.
        var metricId = await _dbContext.GetOrCreateMetricDefinitionIdAsync(
          key,
          (int)metricType,
          unit,
          transaction,
          ct);

        // Reuse a single command for all buckets — avoids N command/parameter
        // allocations per flush cycle.
        await using var cmd = _dbContext.Connection.CreateCommand();
        cmd.Transaction = transaction;

        cmd.CommandText = $@"
          INSERT INTO {tableName}
            (MetricId, Timestamp, ValueSum, ValueCount, ValueMin, ValueMax, ValueLast)
          VALUES
            (@MetricId, @Timestamp, @ValueSum, @ValueCount, @ValueMin, @ValueMax, @ValueLast)
          ON CONFLICT(MetricId, Timestamp) DO UPDATE SET
            ValueSum = ValueSum + @ValueSum,
            ValueCount = ValueCount + @ValueCount,
            ValueMin = MIN(ValueMin, @ValueMin),
            ValueMax = MAX(ValueMax, @ValueMax),
            ValueLast = @ValueLast";

        var pMetricId = cmd.Parameters.Add("@MetricId", SqliteType.Integer);
        var pTimestamp = cmd.Parameters.Add("@Timestamp", SqliteType.Integer);
        var pValueSum = cmd.Parameters.Add("@ValueSum", SqliteType.Real);
        var pValueCount = cmd.Parameters.Add("@ValueCount", SqliteType.Integer);
        var pValueMin = cmd.Parameters.Add("@ValueMin", SqliteType.Real);
        var pValueMax = cmd.Parameters.Add("@ValueMax", SqliteType.Real);
        var pValueLast = cmd.Parameters.Add("@ValueLast", SqliteType.Real);

        foreach (var bucket in buckets)
        {
          pMetricId.Value = metricId;
          pTimestamp.Value = bucket.Timestamp;
          pValueSum.Value = bucket.ValueSum;
          pValueCount.Value = bucket.ValueCount;
          pValueMin.Value = bucket.ValueMin ?? (object)DBNull.Value;
          pValueMax.Value = bucket.ValueMax ?? (object)DBNull.Value;
          pValueLast.Value = bucket.ValueLast ?? (object)DBNull.Value;

          await cmd.ExecuteNonQueryAsync(ct);
        }

        if (transactionOwner)
        {
          await transaction.CommitAsync(ct);
          _logger.LogDebug("Saved {Count} buckets for metric {Key} at {Resolution} resolution",
            buckets.Count(), key, resolution);
        }
      }
      catch (ObjectDisposedException)
      {
        // Connection disposed during operation - log and skip
        _logger.LogDebug("Database connection disposed during save operation for {Key}", key);
        _currentTransaction = null;
        return;
      }
      catch (Exception ex)
      {
        if (transactionOwner)
        {
          try
          {
            await transaction.RollbackAsync(ct);
          }
          catch (ObjectDisposedException)
          {
            // Ignore - connection already disposed
          }
        }
        _logger.LogError(ex, "Failed to save metric buckets for {Key}", key);
        throw;
      }
      finally
      {
        if (transactionOwner)
        {
          try
          {
            await transaction.DisposeAsync();
          }
          catch (ObjectDisposedException)
          {
            // Ignore - already disposed
          }
          _currentTransaction = null;
        }
      }
    }
    finally
    {
      _transactionLock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<MetricPoint>> GetHistoryAsync(
    string key,
    DateTimeOffset start,
    DateTimeOffset end,
    MetricResolution resolution = MetricResolution.Minute,
    IDictionary<string, string>? tags = null,
    CancellationToken ct = default)
  {
    var tableName = resolution switch
    {
      MetricResolution.Minute => "MetricData_Minute",
      MetricResolution.Hour => "MetricData_Hour",
      MetricResolution.Day => "MetricData_Day",
      _ => throw new ArgumentException($"Invalid resolution: {resolution}")
    };

    var startUnix = start.ToUnixTimeSeconds();
    var endUnix = end.ToUnixTimeSeconds();

    // Use independent read connection to avoid contention on the shared write connection
    await using var readConn = _dbContext.CreateReadConnection();
    await using var cmd = readConn.CreateCommand();
    cmd.CommandText = $@"
      SELECT
        md.Key,
        m.Timestamp,
        m.ValueSum,
        m.ValueCount,
        m.ValueMin,
        m.ValueMax,
        m.ValueLast
      FROM {tableName} m
      INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
      WHERE md.Key = @Key
        AND m.Timestamp >= @Start
        AND m.Timestamp <= @End
      ORDER BY m.Timestamp ASC";

    cmd.Parameters.AddWithValue("@Key", key);
    cmd.Parameters.AddWithValue("@Start", startUnix);
    cmd.Parameters.AddWithValue("@End", endUnix);

    var points = new List<MetricPoint>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    
    while (await reader.ReadAsync(ct))
    {
      var timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));
      var valueSum = reader.GetDouble(2);
      var valueCount = reader.GetInt32(3);
      var avgValue = valueCount > 0 ? valueSum / valueCount : valueSum;

      points.Add(new MetricPoint
      {
        Key = reader.GetString(0),
        Timestamp = timestamp,
        Value = avgValue,
        Count = valueCount,
        Min = reader.IsDBNull(4) ? null : reader.GetDouble(4),
        Max = reader.IsDBNull(5) ? null : reader.GetDouble(5),
        Last = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        Tags = tags as IReadOnlyDictionary<string, string>
      });
    }

    _logger.LogDebug("Retrieved {Count} data points for {Key} from {Start} to {End}",
      points.Count, key, start, end);

    return points;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyDictionary<string, double>> GetCurrentSnapshotsAsync(
    IEnumerable<string> keys,
    CancellationToken ct = default)
  {
    var keyList = keys.ToList();
    if (keyList.Count == 0)
      return new Dictionary<string, double>();

    // For small key lists, run individual queries concurrently is fine.
    // Each GetAggregateAsync uses its own read connection.
    var tasks = keyList.Select(async key =>
    {
      var value = await GetAggregateAsync(key, ct);
      return (key, value);
    });

    var results = await Task.WhenAll(tasks);

    var result = new Dictionary<string, double>();
    foreach (var (key, value) in results)
    {
      if (value.HasValue)
        result[key] = value.Value;
    }

    return result;
  }

  /// <inheritdoc/>
  public async Task<double?> GetAggregateAsync(string key, CancellationToken ct = default)
  {
    // Use independent read connection to avoid contention on the shared write connection
    await using var readConn = _dbContext.CreateReadConnection();
    await using var cmd = readConn.CreateCommand();
    
    // First, get the metric type to determine aggregation strategy
    cmd.CommandText = "SELECT Type FROM MetricDefinitions WHERE Key = @Key";
    cmd.Parameters.AddWithValue("@Key", key);
    
    var typeResult = await cmd.ExecuteScalarAsync(ct);
    if (typeResult == null)
    {
      return null;
    }

    var metricType = (MetricType)Convert.ToInt32(typeResult);

    if (metricType == MetricType.Counter)
    {
      // For counters, sum all values across all resolutions
      cmd.CommandText = @"
        SELECT COALESCE(SUM(ValueSum), 0) as Total
        FROM (
          SELECT ValueSum FROM MetricData_Minute m
          INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
          WHERE md.Key = @Key
          UNION ALL
          SELECT ValueSum FROM MetricData_Hour m
          INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
          WHERE md.Key = @Key
          UNION ALL
          SELECT ValueSum FROM MetricData_Day m
          INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
          WHERE md.Key = @Key
        )";
    }
    else
    {
      // For gauges, get the most recent value
      cmd.CommandText = @"
        SELECT ValueLast
        FROM (
          SELECT ValueLast, Timestamp FROM MetricData_Minute m
          INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
          WHERE md.Key = @Key
          UNION ALL
          SELECT ValueLast, Timestamp FROM MetricData_Hour m
          INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
          WHERE md.Key = @Key
          UNION ALL
          SELECT ValueLast, Timestamp FROM MetricData_Day m
          INNER JOIN MetricDefinitions md ON m.MetricId = md.Id
          WHERE md.Key = @Key
        )
        ORDER BY Timestamp DESC
        LIMIT 1";
    }

    var result = await cmd.ExecuteScalarAsync(ct);
    if (result == null || result == DBNull.Value)
    {
      return null;
    }

    return Convert.ToDouble(result);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<string>> ListMetricKeysAsync(CancellationToken ct = default)
  {
    // Use independent read connection to avoid contention on the shared write connection
    await using var readConn = _dbContext.CreateReadConnection();
    await using var cmd = readConn.CreateCommand();
    cmd.CommandText = "SELECT Key FROM MetricDefinitions ORDER BY Key";

    var keys = new List<string>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    
    while (await reader.ReadAsync(ct))
    {
      keys.Add(reader.GetString(0));
    }

    return keys;
  }

  /// <summary>
  /// Aggregates minute data into hour buckets.
  /// </summary>
  public async Task RollupMinuteToHourAsync(DateTimeOffset cutoffTime, CancellationToken ct = default)
  {
    var cutoffUnix = cutoffTime.ToUnixTimeSeconds();

    // Acquire lock to prevent concurrent transactions on the same connection
    await _transactionLock.WaitAsync(ct);
    try
    {
      var existingTransaction = _currentTransaction;
      var transactionOwner = existingTransaction == null;
      var transaction = existingTransaction ?? (await _dbContext.Connection.BeginTransactionAsync(ct) as SqliteTransaction)!;
      if (transactionOwner)
      {
        _currentTransaction = transaction;
      }

      try
      {
        await using var cmd = _dbContext.Connection.CreateCommand();
        cmd.Transaction = transaction;

        // Aggregate minute data into hours
        cmd.CommandText = @"
          INSERT INTO MetricData_Hour (MetricId, Timestamp, ValueSum, ValueCount, ValueMin, ValueMax, ValueLast)
          SELECT
            MetricId,
            (Timestamp / 3600) * 3600 as HourTimestamp,
            SUM(ValueSum) as ValueSum,
            SUM(ValueCount) as ValueCount,
            MIN(ValueMin) as ValueMin,
            MAX(ValueMax) as ValueMax,
            MAX(ValueLast) as ValueLast
          FROM MetricData_Minute
          WHERE Timestamp < @Cutoff
          GROUP BY MetricId, HourTimestamp
          ON CONFLICT(MetricId, Timestamp) DO UPDATE SET
            ValueSum = ValueSum + excluded.ValueSum,
            ValueCount = ValueCount + excluded.ValueCount,
            ValueMin = MIN(ValueMin, excluded.ValueMin),
            ValueMax = MAX(ValueMax, excluded.ValueMax),
            ValueLast = excluded.ValueLast";

        cmd.Parameters.AddWithValue("@Cutoff", cutoffUnix);
        var aggregated = await cmd.ExecuteNonQueryAsync(ct);

        // Delete old minute data
        cmd.CommandText = "DELETE FROM MetricData_Minute WHERE Timestamp < @Cutoff";
        var deleted = await cmd.ExecuteNonQueryAsync(ct);

        if (transactionOwner)
        {
          await transaction.CommitAsync(ct);
          _logger.LogInformation("Rolled up {Aggregated} minute records into hours, deleted {Deleted} old records",
            aggregated, deleted);
        }
      }
      catch (Exception ex)
      {
        if (transactionOwner)
        {
          await transaction.RollbackAsync(ct);
        }
        _logger.LogError(ex, "Failed to rollup minute data to hours");
        throw;
      }
      finally
      {
        if (transactionOwner)
        {
          await transaction.DisposeAsync();
          _currentTransaction = null;
        }
      }
    }
    finally
    {
      _transactionLock.Release();
    }
  }

  /// <summary>
  /// Aggregates hour data into day buckets.
  /// </summary>
  public async Task RollupHourToDayAsync(DateTimeOffset cutoffTime, CancellationToken ct = default)
  {
    var cutoffUnix = cutoffTime.ToUnixTimeSeconds();

    // Acquire lock to prevent concurrent transactions on the same connection
    await _transactionLock.WaitAsync(ct);
    try
    {
      var existingTransaction = _currentTransaction;
      var transactionOwner = existingTransaction == null;
      var transaction = existingTransaction ?? (await _dbContext.Connection.BeginTransactionAsync(ct) as SqliteTransaction)!;
      if (transactionOwner)
      {
        _currentTransaction = transaction;
      }

      try
      {
        await using var cmd = _dbContext.Connection.CreateCommand();
        cmd.Transaction = transaction;

        // Aggregate hour data into days
        cmd.CommandText = @"
          INSERT INTO MetricData_Day (MetricId, Timestamp, ValueSum, ValueCount, ValueMin, ValueMax, ValueLast)
          SELECT
            MetricId,
            (Timestamp / 86400) * 86400 as DayTimestamp,
            SUM(ValueSum) as ValueSum,
            SUM(ValueCount) as ValueCount,
            MIN(ValueMin) as ValueMin,
            MAX(ValueMax) as ValueMax,
            MAX(ValueLast) as ValueLast
          FROM MetricData_Hour
          WHERE Timestamp < @Cutoff
          GROUP BY MetricId, DayTimestamp
          ON CONFLICT(MetricId, Timestamp) DO UPDATE SET
            ValueSum = ValueSum + excluded.ValueSum,
            ValueCount = ValueCount + excluded.ValueCount,
            ValueMin = MIN(ValueMin, excluded.ValueMin),
            ValueMax = MAX(ValueMax, excluded.ValueMax),
            ValueLast = excluded.ValueLast";

        cmd.Parameters.AddWithValue("@Cutoff", cutoffUnix);
        var aggregated = await cmd.ExecuteNonQueryAsync(ct);

        // Delete old hour data
        cmd.CommandText = "DELETE FROM MetricData_Hour WHERE Timestamp < @Cutoff";
        var deleted = await cmd.ExecuteNonQueryAsync(ct);

        if (transactionOwner)
        {
          await transaction.CommitAsync(ct);
          _logger.LogInformation("Rolled up {Aggregated} hour records into days, deleted {Deleted} old records",
            aggregated, deleted);
        }
      }
      catch (Exception ex)
      {
        if (transactionOwner)
        {
          await transaction.RollbackAsync(ct);
        }
        _logger.LogError(ex, "Failed to rollup hour data to days");
        throw;
      }
      finally
      {
        if (transactionOwner)
        {
          await transaction.DisposeAsync();
          _currentTransaction = null;
        }
      }
    }
    finally
    {
      _transactionLock.Release();
    }
  }

  /// <summary>
  /// Deletes old data based on retention policy.
  /// </summary>
  public async Task PruneOldDataAsync(
    MetricResolution resolution,
    DateTimeOffset cutoffTime,
    CancellationToken ct = default)
  {
    var tableName = resolution switch
    {
      MetricResolution.Minute => "MetricData_Minute",
      MetricResolution.Hour => "MetricData_Hour",
      MetricResolution.Day => "MetricData_Day",
      _ => throw new ArgumentException($"Invalid resolution: {resolution}")
    };

    var cutoffUnix = cutoffTime.ToUnixTimeSeconds();

    // Acquire lock to prevent concurrent transactions on the same connection
    await _transactionLock.WaitAsync(ct);
    try
    {
      var existingTransaction = _currentTransaction;
      var transactionOwner = existingTransaction == null;
      var transaction = existingTransaction ?? (await _dbContext.Connection.BeginTransactionAsync(ct) as SqliteTransaction)!;
      if (transactionOwner)
      {
        _currentTransaction = transaction;
      }

      try
      {
        await using var cmd = _dbContext.Connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"DELETE FROM {tableName} WHERE Timestamp < @Cutoff";
        cmd.Parameters.AddWithValue("@Cutoff", cutoffUnix);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        
        if (transactionOwner)
        {
          await transaction.CommitAsync(ct);

          if (deleted > 0)
          {
            _logger.LogInformation("Pruned {Count} old records from {Table}", deleted, tableName);
          }
        }
      }
      catch (Exception ex)
      {
        if (transactionOwner)
        {
          await transaction.RollbackAsync(ct);
        }
        _logger.LogError(ex, "Failed to prune old data from {Table}", tableName);
        throw;
      }
      finally
      {
        if (transactionOwner)
        {
          await transaction.DisposeAsync();
          _currentTransaction = null;
        }
      }
    }
    finally
    {
      _transactionLock.Release();
    }
  }
}

/// <summary>
/// Represents a time bucket of aggregated metric data.
/// </summary>
public sealed record MetricBucket
{
  public required long Timestamp { get; init; }
  public required double ValueSum { get; init; }
  public required int ValueCount { get; init; }
  public double? ValueMin { get; init; }
  public double? ValueMax { get; init; }
  public double? ValueLast { get; init; }
}