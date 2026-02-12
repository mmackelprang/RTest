using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the TTS voice cache and favorites repository.
/// </summary>
public sealed class SqliteTTSVoiceRepository : ITTSVoiceRepository
{
  private readonly ILogger<SqliteTTSVoiceRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  public SqliteTTSVoiceRepository(
    ILogger<SqliteTTSVoiceRepository> logger,
    FingerprintDbContext dbContext)
  {
    _logger = logger;
    _dbContext = dbContext;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<TTSVoiceInfo>> GetCachedVoicesAsync(
    TTSEngine engine, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    var engineStr = engine.ToString();

    // LEFT JOIN with favorites to get IsFavorite flag in one query
    var sql = """
      SELECT c.VoiceId, c.Name, c.Language, c.Gender, c.PriceTier,
             CASE WHEN f.Id IS NOT NULL THEN 1 ELSE 0 END AS IsFavorite
      FROM TTSVoiceCache c
      LEFT JOIN TTSVoiceFavorites f ON c.Engine = f.Engine AND c.VoiceId = f.VoiceId
      WHERE c.Engine = @Engine
      ORDER BY c.Name
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engineStr);

    var voices = new List<TTSVoiceInfo>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
      voices.Add(new TTSVoiceInfo
      {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Language = reader.GetString(2),
        Gender = Enum.TryParse<TTSVoiceGender>(reader.GetString(3), out var g) ? g : TTSVoiceGender.Neutral,
        PriceTier = reader.GetString(4),
        IsFavorite = reader.GetInt32(5) == 1
      });
    }

    _logger.LogDebug("Retrieved {Count} cached voices for {Engine}", voices.Count, engine);
    return voices;
  }

  /// <inheritdoc/>
  public async Task ReplaceCachedVoicesAsync(
    TTSEngine engine,
    IReadOnlyList<TTSVoiceInfo> voices,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    var engineStr = engine.ToString();
    var now = DateTime.UtcNow.ToString("O");

    await using var transaction = await conn.BeginTransactionAsync(ct);
    try
    {
      // Delete existing cache for this engine
      await using var deleteCmd = conn.CreateCommand();
      deleteCmd.CommandText = "DELETE FROM TTSVoiceCache WHERE Engine = @Engine";
      deleteCmd.Parameters.AddWithValue("@Engine", engineStr);
      deleteCmd.Transaction = (SqliteTransaction)transaction;
      await deleteCmd.ExecuteNonQueryAsync(ct);

      // Insert new voices
      foreach (var voice in voices)
      {
        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
          INSERT INTO TTSVoiceCache (Engine, VoiceId, Name, Language, Gender, PriceTier, LastUpdated)
          VALUES (@Engine, @VoiceId, @Name, @Language, @Gender, @PriceTier, @LastUpdated)
          """;
        insertCmd.Parameters.AddWithValue("@Engine", engineStr);
        insertCmd.Parameters.AddWithValue("@VoiceId", voice.Id);
        insertCmd.Parameters.AddWithValue("@Name", voice.Name);
        insertCmd.Parameters.AddWithValue("@Language", voice.Language);
        insertCmd.Parameters.AddWithValue("@Gender", voice.Gender.ToString());
        insertCmd.Parameters.AddWithValue("@PriceTier", voice.PriceTier);
        insertCmd.Parameters.AddWithValue("@LastUpdated", now);
        insertCmd.Transaction = (SqliteTransaction)transaction;
        await insertCmd.ExecuteNonQueryAsync(ct);
      }

      await transaction.CommitAsync(ct);
      _logger.LogInformation("Cached {Count} voices for {Engine}", voices.Count, engine);
    }
    catch
    {
      await transaction.RollbackAsync(ct);
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task AddFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      INSERT OR IGNORE INTO TTSVoiceFavorites (Engine, VoiceId, AddedAt)
      VALUES (@Engine, @VoiceId, @AddedAt)
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engine.ToString());
    cmd.Parameters.AddWithValue("@VoiceId", voiceId);
    cmd.Parameters.AddWithValue("@AddedAt", DateTime.UtcNow.ToString("O"));
    await cmd.ExecuteNonQueryAsync(ct);

    _logger.LogDebug("Added favorite voice {VoiceId} for {Engine}", voiceId, engine);
  }

  /// <inheritdoc/>
  public async Task RemoveFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = "DELETE FROM TTSVoiceFavorites WHERE Engine = @Engine AND VoiceId = @VoiceId";

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engine.ToString());
    cmd.Parameters.AddWithValue("@VoiceId", voiceId);
    await cmd.ExecuteNonQueryAsync(ct);

    _logger.LogDebug("Removed favorite voice {VoiceId} for {Engine}", voiceId, engine);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlySet<string>> GetFavoriteIdsAsync(
    TTSEngine engine, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = "SELECT VoiceId FROM TTSVoiceFavorites WHERE Engine = @Engine";

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engine.ToString());

    var favorites = new HashSet<string>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
      favorites.Add(reader.GetString(0));
    }

    return favorites;
  }
}
