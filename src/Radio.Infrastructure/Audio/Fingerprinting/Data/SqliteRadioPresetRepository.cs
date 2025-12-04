using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the radio preset repository.
/// </summary>
public sealed class SqliteRadioPresetRepository : IRadioPresetRepository
{
  private readonly ILogger<SqliteRadioPresetRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqliteRadioPresetRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dbContext">The database context.</param>
  public SqliteRadioPresetRepository(
    ILogger<SqliteRadioPresetRepository> logger,
    FingerprintDbContext dbContext)
  {
    _logger = logger;
    _dbContext = dbContext;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<RadioPreset>> GetAllAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Id, Name, Band, Frequency, CreatedAt, LastModifiedAt
      FROM RadioPresets
      ORDER BY Name
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;

    var presets = new List<RadioPreset>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
      presets.Add(MapFromReader(reader));
    }

    return presets;
  }

  /// <inheritdoc/>
  public async Task<RadioPreset?> GetByIdAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Id, Name, Band, Frequency, CreatedAt, LastModifiedAt
      FROM RadioPresets
      WHERE Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", id);

    await using var reader = await cmd.ExecuteReaderAsync(ct);

    if (await reader.ReadAsync(ct))
    {
      return MapFromReader(reader);
    }

    return null;
  }

  /// <inheritdoc/>
  public async Task<RadioPreset?> GetByBandAndFrequencyAsync(RadioBand band, double frequency, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Id, Name, Band, Frequency, CreatedAt, LastModifiedAt
      FROM RadioPresets
      WHERE Band = @Band AND ABS(Frequency - @Frequency) < 0.001
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Band", band.ToString());
    cmd.Parameters.AddWithValue("@Frequency", frequency);

    await using var reader = await cmd.ExecuteReaderAsync(ct);

    if (await reader.ReadAsync(ct))
    {
      return MapFromReader(reader);
    }

    return null;
  }

  /// <inheritdoc/>
  public async Task AddAsync(RadioPreset preset, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      INSERT INTO RadioPresets (Id, Name, Band, Frequency, CreatedAt, LastModifiedAt)
      VALUES (@Id, @Name, @Band, @Frequency, @CreatedAt, @LastModifiedAt)
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", preset.Id);
    cmd.Parameters.AddWithValue("@Name", preset.Name);
    cmd.Parameters.AddWithValue("@Band", preset.Band.ToString());
    cmd.Parameters.AddWithValue("@Frequency", preset.Frequency);
    cmd.Parameters.AddWithValue("@CreatedAt", preset.CreatedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@LastModifiedAt", preset.LastModifiedAt.ToString("O"));

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogDebug("Added radio preset {Id}: {Name} ({Band} - {Frequency})", 
      preset.Id, preset.Name, preset.Band, preset.Frequency);
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      DELETE FROM RadioPresets
      WHERE Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", id);

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    var deleted = rowsAffected > 0;

    if (deleted)
    {
      _logger.LogDebug("Deleted radio preset {Id}", id);
    }
    else
    {
      _logger.LogDebug("Radio preset {Id} not found for deletion", id);
    }

    return deleted;
  }

  /// <inheritdoc/>
  public async Task<int> GetCountAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = "SELECT COUNT(*) FROM RadioPresets";

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;

    var result = await cmd.ExecuteScalarAsync(ct);
    return result != null ? Convert.ToInt32(result) : 0;
  }

  private static RadioPreset MapFromReader(SqliteDataReader reader)
  {
    return new RadioPreset
    {
      Id = reader.GetString(0),
      Name = reader.GetString(1),
      Band = Enum.Parse<RadioBand>(reader.GetString(2)),
      Frequency = reader.GetDouble(3),
      CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
      LastModifiedAt = DateTimeOffset.Parse(reader.GetString(5))
    };
  }
}
