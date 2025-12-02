using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the track metadata repository.
/// </summary>
public sealed class SqliteTrackMetadataRepository : ITrackMetadataRepository
{
  private readonly ILogger<SqliteTrackMetadataRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqliteTrackMetadataRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dbContext">The database context.</param>
  public SqliteTrackMetadataRepository(
    ILogger<SqliteTrackMetadataRepository> logger,
    FingerprintDbContext dbContext)
  {
    _logger = logger;
    _dbContext = dbContext;
  }

  /// <inheritdoc/>
  public async Task<TrackMetadata?> GetByIdAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Id, FingerprintId, Title, Artist, Album, AlbumArtist, TrackNumber,
             DiscNumber, ReleaseYear, Genre, MusicBrainzArtistId, MusicBrainzReleaseId,
             MusicBrainzRecordingId, CoverArtUrl, Source, CreatedAt, UpdatedAt
      FROM TrackMetadata
      WHERE Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", id);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct))
    {
      return null;
    }

    return MapToTrackMetadata(reader);
  }

  /// <inheritdoc/>
  public async Task<TrackMetadata?> GetByFingerprintIdAsync(
    string fingerprintId,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Id, FingerprintId, Title, Artist, Album, AlbumArtist, TrackNumber,
             DiscNumber, ReleaseYear, Genre, MusicBrainzArtistId, MusicBrainzReleaseId,
             MusicBrainzRecordingId, CoverArtUrl, Source, CreatedAt, UpdatedAt
      FROM TrackMetadata
      WHERE FingerprintId = @FingerprintId
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@FingerprintId", fingerprintId);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct))
    {
      return null;
    }

    return MapToTrackMetadata(reader);
  }

  /// <inheritdoc/>
  public async Task<TrackMetadata> StoreAsync(
    TrackMetadata metadata,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var now = DateTime.UtcNow.ToString("O");
    var sql = """
      INSERT INTO TrackMetadata (Id, FingerprintId, Title, Artist, Album, AlbumArtist,
        TrackNumber, DiscNumber, ReleaseYear, Genre, MusicBrainzArtistId, MusicBrainzReleaseId,
        MusicBrainzRecordingId, CoverArtUrl, Source, CreatedAt, UpdatedAt)
      VALUES (@Id, @FingerprintId, @Title, @Artist, @Album, @AlbumArtist,
        @TrackNumber, @DiscNumber, @ReleaseYear, @Genre, @MbArtistId, @MbReleaseId,
        @MbRecordingId, @CoverArtUrl, @Source, @CreatedAt, @UpdatedAt)
      ON CONFLICT(Id) DO UPDATE SET
        Title = excluded.Title,
        Artist = excluded.Artist,
        Album = excluded.Album,
        AlbumArtist = excluded.AlbumArtist,
        TrackNumber = excluded.TrackNumber,
        DiscNumber = excluded.DiscNumber,
        ReleaseYear = excluded.ReleaseYear,
        Genre = excluded.Genre,
        MusicBrainzArtistId = excluded.MusicBrainzArtistId,
        MusicBrainzReleaseId = excluded.MusicBrainzReleaseId,
        MusicBrainzRecordingId = excluded.MusicBrainzRecordingId,
        CoverArtUrl = excluded.CoverArtUrl,
        UpdatedAt = excluded.UpdatedAt
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", metadata.Id);
    cmd.Parameters.AddWithValue("@FingerprintId", (object?)metadata.FingerprintId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Title", metadata.Title);
    cmd.Parameters.AddWithValue("@Artist", metadata.Artist);
    cmd.Parameters.AddWithValue("@Album", (object?)metadata.Album ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@AlbumArtist", (object?)metadata.AlbumArtist ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@TrackNumber", (object?)metadata.TrackNumber ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@DiscNumber", (object?)metadata.DiscNumber ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ReleaseYear", (object?)metadata.ReleaseYear ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Genre", (object?)metadata.Genre ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@MbArtistId", (object?)metadata.MusicBrainzArtistId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@MbReleaseId", (object?)metadata.MusicBrainzReleaseId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@MbRecordingId", (object?)metadata.MusicBrainzRecordingId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@CoverArtUrl", (object?)metadata.CoverArtUrl ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Source", metadata.Source.ToString());
    cmd.Parameters.AddWithValue("@CreatedAt", metadata.CreatedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@UpdatedAt", now);

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogDebug("Stored track metadata {Id}: {Title} by {Artist}", metadata.Id, metadata.Title, metadata.Artist);

    return metadata with { UpdatedAt = DateTime.Parse(now) };
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<TrackMetadata>> SearchAsync(
    string query,
    int limit = 20,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var searchPattern = $"%{query}%";
    var sql = """
      SELECT Id, FingerprintId, Title, Artist, Album, AlbumArtist, TrackNumber,
             DiscNumber, ReleaseYear, Genre, MusicBrainzArtistId, MusicBrainzReleaseId,
             MusicBrainzRecordingId, CoverArtUrl, Source, CreatedAt, UpdatedAt
      FROM TrackMetadata
      WHERE Title LIKE @Query OR Artist LIKE @Query OR Album LIKE @Query
      ORDER BY Artist, Title
      LIMIT @Limit
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Query", searchPattern);
    cmd.Parameters.AddWithValue("@Limit", limit);

    var results = new List<TrackMetadata>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      results.Add(MapToTrackMetadata(reader));
    }

    return results;
  }

  private static TrackMetadata MapToTrackMetadata(Microsoft.Data.Sqlite.SqliteDataReader reader)
  {
    return new TrackMetadata
    {
      Id = reader.GetString(reader.GetOrdinal("Id")),
      FingerprintId = reader.IsDBNull(reader.GetOrdinal("FingerprintId"))
        ? null : reader.GetString(reader.GetOrdinal("FingerprintId")),
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
      MusicBrainzRecordingId = reader.IsDBNull(reader.GetOrdinal("MusicBrainzRecordingId"))
        ? null : reader.GetString(reader.GetOrdinal("MusicBrainzRecordingId")),
      CoverArtUrl = reader.IsDBNull(reader.GetOrdinal("CoverArtUrl"))
        ? null : reader.GetString(reader.GetOrdinal("CoverArtUrl")),
      Source = Enum.Parse<MetadataSource>(reader.GetString(reader.GetOrdinal("Source"))),
      CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
      UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
    };
  }
}
