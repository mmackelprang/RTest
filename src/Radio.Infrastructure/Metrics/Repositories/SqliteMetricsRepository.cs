namespace Radio.Infrastructure.Metrics.Repositories;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using Radio.Core.Metrics;
using Radio.Infrastructure.Metrics.Data;

/// <summary>
/// Repository for storing and retrieving metrics from SQLite.
/// </summary>
public sealed class SqliteMetricsRepository : IMetricsReader
{
  private readonly ILogger<SqliteMetricsRepository> _logger;
  private readonly MetricsDbContext _dbContext;

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

    var metricId = await _dbContext.GetOrCreateMetricDefinitionIdAsync(
      key,
      (int)metricType,
      unit,
      ct);

    var tableName = resolution switch
    {
      MetricResolution.Minute => "MetricData_Minute",
      MetricResolution.Hour => "MetricData_Hour",
      MetricResolution.Day => "MetricData_Day",
      _ => throw new ArgumentException($"Invalid resolution: {resolution}")
    };

    await using var transaction = _dbContext.Connection.BeginTransaction();
    try
    {
      foreach (var bucket in buckets)
      {
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

        cmd.Parameters.AddWithValue("@MetricId", metricId);
        cmd.Parameters.AddWithValue("@Timestamp", bucket.Timestamp);
        cmd.Parameters.AddWithValue("@ValueSum", bucket.ValueSum);
        cmd.Parameters.AddWithValue("@ValueCount", bucket.ValueCount);
        cmd.Parameters.AddWithValue("@ValueMin", bucket.ValueMin ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ValueMax", bucket.ValueMax ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ValueLast", bucket.ValueLast ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
      }

      await transaction.CommitAsync(ct);
      _logger.LogDebug("Saved {Count} buckets for metric {Key} at {Resolution} resolution",
        buckets.Count(), key, resolution);
    }
    catch (Exception ex)
    {
      await transaction.RollbackAsync(ct);
      _logger.LogError(ex, "Failed to save metric buckets for {Key}", key);
      throw;
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

    await using var cmd = _dbContext.Connection.CreateCommand();
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
    var result = new Dictionary<string, double>();

    foreach (var key in keys)
    {
      var value = await GetAggregateAsync(key, ct);
      if (value.HasValue)
      {
        result[key] = value.Value;
      }
    }

    return result;
  }

  /// <inheritdoc/>
  public async Task<double?> GetAggregateAsync(string key, CancellationToken ct = default)
  {
    // Query across all resolutions and sum/aggregate
    await using var cmd = _dbContext.Connection.CreateCommand();
    
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
    await using var cmd = _dbContext.Connection.CreateCommand();
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

    await using var transaction = _dbContext.Connection.BeginTransaction();
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

      await transaction.CommitAsync(ct);
      _logger.LogInformation("Rolled up {Aggregated} minute records into hours, deleted {Deleted} old records",
        aggregated, deleted);
    }
    catch (Exception ex)
    {
      await transaction.RollbackAsync(ct);
      _logger.LogError(ex, "Failed to rollup minute data to hours");
      throw;
    }
  }

  /// <summary>
  /// Aggregates hour data into day buckets.
  /// </summary>
  public async Task RollupHourToDayAsync(DateTimeOffset cutoffTime, CancellationToken ct = default)
  {
    var cutoffUnix = cutoffTime.ToUnixTimeSeconds();

    await using var transaction = _dbContext.Connection.BeginTransaction();
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

      await transaction.CommitAsync(ct);
      _logger.LogInformation("Rolled up {Aggregated} hour records into days, deleted {Deleted} old records",
        aggregated, deleted);
    }
    catch (Exception ex)
    {
      await transaction.RollbackAsync(ct);
      _logger.LogError(ex, "Failed to rollup hour data to days");
      throw;
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

    await using var cmd = _dbContext.Connection.CreateCommand();
    cmd.CommandText = $"DELETE FROM {tableName} WHERE Timestamp < @Cutoff";
    cmd.Parameters.AddWithValue("@Cutoff", cutoffUnix);

    var deleted = await cmd.ExecuteNonQueryAsync(ct);
    if (deleted > 0)
    {
      _logger.LogInformation("Pruned {Count} old records from {Table}", deleted, tableName);
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
