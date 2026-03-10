using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Audio.Fingerprinting.Data;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// SQLite-backed repository for caching discovered Cast devices.
/// </summary>
public sealed class CastDeviceCacheRepository
{
  private readonly FingerprintDbContext _dbContext;
  private readonly ILogger<CastDeviceCacheRepository> _logger;

  public CastDeviceCacheRepository(
    FingerprintDbContext dbContext,
    ILogger<CastDeviceCacheRepository> logger)
  {
    _dbContext = dbContext;
    _logger = logger;
  }

  /// <summary>
  /// Gets all cached devices.
  /// </summary>
  public async Task<Dictionary<string, CachedCastDevice>> GetAllAsync(CancellationToken ct = default)
  {
    var result = new Dictionary<string, CachedCastDevice>();
    try
    {
      var conn = await _dbContext.GetConnectionAsync(ct);
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT Id, FriendlyName, IpAddress, Port, Model, LastSeenAt FROM CastDeviceCache";

      using var reader = await cmd.ExecuteReaderAsync(ct);
      while (await reader.ReadAsync(ct))
      {
        var id = reader.GetString(0);
        var device = new ChromecastDeviceInfo
        {
          Id = id,
          FriendlyName = reader.GetString(1),
          IpAddress = reader.GetString(2),
          Port = reader.GetInt32(3),
          Model = reader.GetString(4)
        };
        var lastSeen = DateTime.Parse(reader.GetString(5));
        result[id] = new CachedCastDevice { Device = device, LastSeen = lastSeen };
      }

      _logger.LogDebug("Loaded {Count} cached Cast devices from SQLite", result.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to load Cast device cache from SQLite");
    }

    return result;
  }

  /// <summary>
  /// Saves or updates a cached device.
  /// </summary>
  public async Task UpsertAsync(CachedCastDevice cached, CancellationToken ct = default)
  {
    try
    {
      var conn = await _dbContext.GetConnectionAsync(ct);
      using var cmd = conn.CreateCommand();
      cmd.CommandText = """
        INSERT OR REPLACE INTO CastDeviceCache (Id, FriendlyName, IpAddress, Port, Model, LastSeenAt)
        VALUES (@Id, @FriendlyName, @IpAddress, @Port, @Model, @LastSeenAt)
        """;
      cmd.Parameters.AddWithValue("@Id", cached.Device.Id);
      cmd.Parameters.AddWithValue("@FriendlyName", cached.Device.FriendlyName);
      cmd.Parameters.AddWithValue("@IpAddress", cached.Device.IpAddress);
      cmd.Parameters.AddWithValue("@Port", cached.Device.Port);
      cmd.Parameters.AddWithValue("@Model", cached.Device.Model);
      cmd.Parameters.AddWithValue("@LastSeenAt", cached.LastSeen.ToString("O"));

      await cmd.ExecuteNonQueryAsync(ct);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to upsert Cast device {Id}", cached.Device.Id);
    }
  }

  /// <summary>
  /// Removes stale devices older than the specified cutoff.
  /// </summary>
  public async Task RemoveStaleAsync(DateTime cutoff, CancellationToken ct = default)
  {
    try
    {
      var conn = await _dbContext.GetConnectionAsync(ct);
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "DELETE FROM CastDeviceCache WHERE LastSeenAt < @Cutoff";
      cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("O"));

      var deleted = await cmd.ExecuteNonQueryAsync(ct);
      if (deleted > 0)
      {
        _logger.LogDebug("Removed {Count} stale Cast devices from cache", deleted);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to remove stale Cast devices");
    }
  }

  /// <summary>
  /// Saves all devices in bulk (replaces entire cache).
  /// </summary>
  public async Task SaveAllAsync(Dictionary<string, CachedCastDevice> cache, CancellationToken ct = default)
  {
    foreach (var entry in cache.Values)
    {
      await UpsertAsync(entry, ct);
    }
  }
}
