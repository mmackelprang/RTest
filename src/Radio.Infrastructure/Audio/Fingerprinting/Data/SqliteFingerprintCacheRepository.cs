using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the fingerprint cache repository.
/// </summary>
public sealed class SqliteFingerprintCacheRepository : IFingerprintCacheRepository
{
  private readonly ILogger<SqliteFingerprintCacheRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqliteFingerprintCacheRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dbContext">The database context.</param>
  public SqliteFingerprintCacheRepository(
    ILogger<SqliteFingerprintCacheRepository> logger,
    FingerprintDbContext dbContext)
  {
    _logger = logger;
    _dbContext = dbContext;
  }

  /// <inheritdoc/>
  public async Task<CachedFingerprint?> FindByHashAsync(
    string chromaprintHash,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT f.Id, f.ChromaprintHash, f.Duration, f.AcoustId, f.MusicBrainzRecordingId,
             f.CreatedAt, f.LastMatchedAt, f.MatchCount,
             m.Id AS MetadataId, m.Title, m.Artist, m.Album, m.AlbumArtist, m.TrackNumber,
             m.DiscNumber, m.ReleaseYear, m.Genre, m.MusicBrainzArtistId, m.MusicBrainzReleaseId,
             m.MusicBrainzRecordingId AS MetadataMbRecordingId, m.CoverArtUrl, m.Source,
             m.CreatedAt AS MetadataCreatedAt, m.UpdatedAt AS MetadataUpdatedAt
      FROM FingerprintCache f
      LEFT JOIN TrackMetadata m ON f.Id = m.FingerprintId
      WHERE f.ChromaprintHash = @Hash
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Hash", chromaprintHash);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct))
    {
      return null;
    }

    return MapToCachedFingerprint(reader);
  }

  /// <inheritdoc/>
  public async Task<CachedFingerprint> StoreAsync(
    FingerprintData fingerprint,
    TrackMetadata? metadata,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var now = DateTime.UtcNow.ToString("O");

    // Insert fingerprint
    var insertFingerprintSql = """
      INSERT INTO FingerprintCache (Id, ChromaprintHash, Duration, AcoustId, MusicBrainzRecordingId, CreatedAt, MatchCount)
      VALUES (@Id, @Hash, @Duration, @AcoustId, @MbRecordingId, @CreatedAt, 0)
      ON CONFLICT(ChromaprintHash) DO UPDATE SET
        AcoustId = COALESCE(excluded.AcoustId, AcoustId),
        MusicBrainzRecordingId = COALESCE(excluded.MusicBrainzRecordingId, MusicBrainzRecordingId)
      RETURNING Id
      """;

    await using var insertCmd = conn.CreateCommand();
    insertCmd.CommandText = insertFingerprintSql;
    insertCmd.Parameters.AddWithValue("@Id", fingerprint.Id);
    insertCmd.Parameters.AddWithValue("@Hash", fingerprint.ChromaprintHash);
    insertCmd.Parameters.AddWithValue("@Duration", fingerprint.DurationSeconds);
    insertCmd.Parameters.AddWithValue("@AcoustId", (object?)null ?? DBNull.Value);
    insertCmd.Parameters.AddWithValue("@MbRecordingId", (object?)null ?? DBNull.Value);
    insertCmd.Parameters.AddWithValue("@CreatedAt", now);

    var fingerprintId = (string?)await insertCmd.ExecuteScalarAsync(ct) ?? fingerprint.Id;

    // Insert metadata if provided
    if (metadata != null)
    {
      var insertMetadataSql = """
        INSERT INTO TrackMetadata (Id, FingerprintId, Title, Artist, Album, AlbumArtist,
          TrackNumber, DiscNumber, ReleaseYear, Genre, MusicBrainzArtistId, MusicBrainzReleaseId,
          MusicBrainzRecordingId, CoverArtUrl, Source, CreatedAt, UpdatedAt)
        VALUES (@Id, @FingerprintId, @Title, @Artist, @Album, @AlbumArtist,
          @TrackNumber, @DiscNumber, @ReleaseYear, @Genre, @MbArtistId, @MbReleaseId,
          @MbRecordingId, @CoverArtUrl, @Source, @CreatedAt, @UpdatedAt)
        ON CONFLICT(Id) DO UPDATE SET
          Title = excluded.Title, Artist = excluded.Artist, Album = excluded.Album,
          UpdatedAt = excluded.UpdatedAt
        """;

      await using var metadataCmd = conn.CreateCommand();
      metadataCmd.CommandText = insertMetadataSql;
      metadataCmd.Parameters.AddWithValue("@Id", metadata.Id);
      metadataCmd.Parameters.AddWithValue("@FingerprintId", fingerprintId);
      metadataCmd.Parameters.AddWithValue("@Title", metadata.Title);
      metadataCmd.Parameters.AddWithValue("@Artist", metadata.Artist);
      metadataCmd.Parameters.AddWithValue("@Album", (object?)metadata.Album ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@AlbumArtist", (object?)metadata.AlbumArtist ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@TrackNumber", (object?)metadata.TrackNumber ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@DiscNumber", (object?)metadata.DiscNumber ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@ReleaseYear", (object?)metadata.ReleaseYear ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@Genre", (object?)metadata.Genre ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@MbArtistId", (object?)metadata.MusicBrainzArtistId ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@MbReleaseId", (object?)metadata.MusicBrainzReleaseId ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@MbRecordingId", (object?)metadata.MusicBrainzRecordingId ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@CoverArtUrl", (object?)metadata.CoverArtUrl ?? DBNull.Value);
      metadataCmd.Parameters.AddWithValue("@Source", metadata.Source.ToString());
      metadataCmd.Parameters.AddWithValue("@CreatedAt", metadata.CreatedAt.ToString("O"));
      metadataCmd.Parameters.AddWithValue("@UpdatedAt", metadata.UpdatedAt.ToString("O"));

      await metadataCmd.ExecuteNonQueryAsync(ct);
    }

    _logger.LogDebug("Stored fingerprint {Id} in cache", fingerprintId);

    return new CachedFingerprint
    {
      Id = fingerprintId,
      ChromaprintHash = fingerprint.ChromaprintHash,
      DurationSeconds = fingerprint.DurationSeconds,
      CreatedAt = DateTime.Parse(now),
      MatchCount = 0,
      Metadata = metadata
    };
  }

  /// <inheritdoc/>
  public async Task UpdateLastMatchedAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      UPDATE FingerprintCache
      SET LastMatchedAt = @LastMatchedAt, MatchCount = MatchCount + 1
      WHERE Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", id);
    cmd.Parameters.AddWithValue("@LastMatchedAt", DateTime.UtcNow.ToString("O"));

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogDebug("Updated last matched for fingerprint {Id}", id);
  }

  /// <inheritdoc/>
  public async Task<int> GetCacheCountAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM FingerprintCache";

    var result = await cmd.ExecuteScalarAsync(ct);
    return Convert.ToInt32(result);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<CachedFingerprint>> GetAllAsync(
    int page = 1,
    int pageSize = 50,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var offset = (page - 1) * pageSize;
    var sql = """
      SELECT f.Id, f.ChromaprintHash, f.Duration, f.AcoustId, f.MusicBrainzRecordingId,
             f.CreatedAt, f.LastMatchedAt, f.MatchCount,
             m.Id AS MetadataId, m.Title, m.Artist, m.Album, m.AlbumArtist, m.TrackNumber,
             m.DiscNumber, m.ReleaseYear, m.Genre, m.MusicBrainzArtistId, m.MusicBrainzReleaseId,
             m.MusicBrainzRecordingId AS MetadataMbRecordingId, m.CoverArtUrl, m.Source,
             m.CreatedAt AS MetadataCreatedAt, m.UpdatedAt AS MetadataUpdatedAt
      FROM FingerprintCache f
      LEFT JOIN TrackMetadata m ON f.Id = m.FingerprintId
      ORDER BY f.CreatedAt DESC
      LIMIT @Limit OFFSET @Offset
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Limit", pageSize);
    cmd.Parameters.AddWithValue("@Offset", offset);

    var results = new List<CachedFingerprint>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      results.Add(MapToCachedFingerprint(reader));
    }

    return results;
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    // Delete metadata first (foreign key)
    await using var deleteMetadataCmd = conn.CreateCommand();
    deleteMetadataCmd.CommandText = "DELETE FROM TrackMetadata WHERE FingerprintId = @Id";
    deleteMetadataCmd.Parameters.AddWithValue("@Id", id);
    await deleteMetadataCmd.ExecuteNonQueryAsync(ct);

    // Delete fingerprint
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM FingerprintCache WHERE Id = @Id";
    cmd.Parameters.AddWithValue("@Id", id);

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Deleted fingerprint {Id} from cache", id);
    }

    return rowsAffected > 0;
  }

  private static CachedFingerprint MapToCachedFingerprint(SqliteDataReader reader)
  {
    TrackMetadata? metadata = null;

    if (!reader.IsDBNull(reader.GetOrdinal("MetadataId")))
    {
      metadata = new TrackMetadata
      {
        Id = reader.GetString(reader.GetOrdinal("MetadataId")),
        FingerprintId = reader.GetString(reader.GetOrdinal("Id")),
        Title = reader.GetString(reader.GetOrdinal("Title")),
        Artist = reader.GetString(reader.GetOrdinal("Artist")),
        Album = reader.IsDBNull(reader.GetOrdinal("Album"))
          ? null : reader.GetString(reader.GetOrdinal("Album")),
        AlbumArtist = reader.IsDBNull(reader.GetOrdinal("AlbumArtist"))
          ? null : reader.GetString(reader.GetOrdinal("AlbumArtist")),
        TrackNumber = reader.IsDBNull(reader.GetOrdinal("TrackNumber"))
          ? null : reader.GetInt32(reader.GetOrdinal("TrackNumber")),
        DiscNumber = reader.IsDBNull(reader.GetOrdinal("DiscNumber"))
          ? null : reader.GetInt32(reader.GetOrdinal("DiscNumber")),
        ReleaseYear = reader.IsDBNull(reader.GetOrdinal("ReleaseYear"))
          ? null : reader.GetInt32(reader.GetOrdinal("ReleaseYear")),
        Genre = reader.IsDBNull(reader.GetOrdinal("Genre"))
          ? null : reader.GetString(reader.GetOrdinal("Genre")),
        MusicBrainzArtistId = reader.IsDBNull(reader.GetOrdinal("MusicBrainzArtistId"))
          ? null : reader.GetString(reader.GetOrdinal("MusicBrainzArtistId")),
        MusicBrainzReleaseId = reader.IsDBNull(reader.GetOrdinal("MusicBrainzReleaseId"))
          ? null : reader.GetString(reader.GetOrdinal("MusicBrainzReleaseId")),
        MusicBrainzRecordingId = reader.IsDBNull(reader.GetOrdinal("MetadataMbRecordingId"))
          ? null : reader.GetString(reader.GetOrdinal("MetadataMbRecordingId")),
        CoverArtUrl = reader.IsDBNull(reader.GetOrdinal("CoverArtUrl"))
          ? null : reader.GetString(reader.GetOrdinal("CoverArtUrl")),
        Source = Enum.Parse<MetadataSource>(reader.GetString(reader.GetOrdinal("Source"))),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("MetadataCreatedAt"))),
        UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("MetadataUpdatedAt")))
      };
    }

    return new CachedFingerprint
    {
      Id = reader.GetString(reader.GetOrdinal("Id")),
      ChromaprintHash = reader.GetString(reader.GetOrdinal("ChromaprintHash")),
      DurationSeconds = reader.GetInt32(reader.GetOrdinal("Duration")),
      AcoustId = reader.IsDBNull(reader.GetOrdinal("AcoustId"))
        ? null : reader.GetString(reader.GetOrdinal("AcoustId")),
      MusicBrainzRecordingId = reader.IsDBNull(reader.GetOrdinal("MusicBrainzRecordingId"))
        ? null : reader.GetString(reader.GetOrdinal("MusicBrainzRecordingId")),
      CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
      LastMatchedAt = reader.IsDBNull(reader.GetOrdinal("LastMatchedAt"))
        ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastMatchedAt"))),
      MatchCount = reader.GetInt32(reader.GetOrdinal("MatchCount")),
      Metadata = metadata
    };
  }
}
