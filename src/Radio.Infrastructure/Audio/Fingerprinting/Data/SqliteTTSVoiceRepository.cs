using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the TTS voice repository.
/// Manages voice cache and favorites for Text-to-Speech engines.
/// </summary>
public sealed class SqliteTTSVoiceRepository : ITTSVoiceRepository
{
  private readonly ILogger<SqliteTTSVoiceRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqliteTTSVoiceRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dbContext">The database context.</param>
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

    var sql = """
      SELECT v.VoiceId, v.Name, v.Language, v.Gender, v.PriceTier,
             CASE WHEN f.VoiceId IS NOT NULL THEN 1 ELSE 0 END AS IsFavorite
      FROM TTSVoiceCache v
      LEFT JOIN TTSVoiceFavorites f ON v.Engine = f.Engine AND v.VoiceId = f.VoiceId
      WHERE v.Engine = @Engine
      ORDER BY IsFavorite DESC, v.PriceTier, v.Name
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engine.ToString());

    var voices = new List<TTSVoiceInfo>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
      voices.Add(new TTSVoiceInfo
      {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Language = reader.GetString(2),
        Gender = Enum.Parse<TTSVoiceGender>(reader.GetString(3)),
        PriceTier = reader.GetString(4),
        IsFavorite = reader.GetInt32(5) == 1
      });
    }

    _logger.LogDebug("Retrieved {Count} cached voices for engine {Engine}", voices.Count, engine);
    return voices;
  }

  /// <inheritdoc/>
  public async Task ReplaceCachedVoicesAsync(
    TTSEngine engine,
    IReadOnlyList<TTSVoiceInfo> voices,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    using var transaction = conn.BeginTransaction();

    try
    {
      // Delete existing cached voices for this engine
      await using (var deleteCmd = conn.CreateCommand())
      {
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = "DELETE FROM TTSVoiceCache WHERE Engine = @Engine";
        deleteCmd.Parameters.AddWithValue("@Engine", engine.ToString());
        await deleteCmd.ExecuteNonQueryAsync(ct);
      }

      // Insert new voices
      var now = DateTime.UtcNow.ToString("O");
      foreach (var voice in voices)
      {
        await using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = """
          INSERT INTO TTSVoiceCache (Engine, VoiceId, Name, Language, Gender, PriceTier, LastUpdated)
          VALUES (@Engine, @VoiceId, @Name, @Language, @Gender, @PriceTier, @LastUpdated)
          """;
        insertCmd.Parameters.AddWithValue("@Engine", engine.ToString());
        insertCmd.Parameters.AddWithValue("@VoiceId", voice.Id);
        insertCmd.Parameters.AddWithValue("@Name", voice.Name);
        insertCmd.Parameters.AddWithValue("@Language", voice.Language);
        insertCmd.Parameters.AddWithValue("@Gender", voice.Gender.ToString());
        insertCmd.Parameters.AddWithValue("@PriceTier", voice.PriceTier);
        insertCmd.Parameters.AddWithValue("@LastUpdated", now);
        await insertCmd.ExecuteNonQueryAsync(ct);
      }

      transaction.Commit();
      _logger.LogInformation("Replaced cached voices for engine {Engine} with {Count} voices", engine, voices.Count);
    }
    catch
    {
      transaction.Rollback();
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

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Added voice {VoiceId} to favorites for engine {Engine}", voiceId, engine);
    }
  }

  /// <inheritdoc/>
  public async Task RemoveFavoriteAsync(
    TTSEngine engine, string voiceId, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      DELETE FROM TTSVoiceFavorites
      WHERE Engine = @Engine AND VoiceId = @VoiceId
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engine.ToString());
    cmd.Parameters.AddWithValue("@VoiceId", voiceId);

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Removed voice {VoiceId} from favorites for engine {Engine}", voiceId, engine);
    }
  }

  /// <inheritdoc/>
  public async Task<IReadOnlySet<string>> GetFavoriteIdsAsync(
    TTSEngine engine, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT VoiceId
      FROM TTSVoiceFavorites
      WHERE Engine = @Engine
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Engine", engine.ToString());

    var favoriteIds = new HashSet<string>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
      favoriteIds.Add(reader.GetString(0));
    }

    _logger.LogDebug("Retrieved {Count} favorite voice IDs for engine {Engine}", favoriteIds.Count, engine);
    return favoriteIds;
  }
}
