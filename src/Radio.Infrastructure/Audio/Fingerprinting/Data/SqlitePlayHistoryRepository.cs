using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the play history repository.
/// </summary>
public sealed class SqlitePlayHistoryRepository : IPlayHistoryRepository
{
  private readonly ILogger<SqlitePlayHistoryRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqlitePlayHistoryRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dbContext">The database context.</param>
  public SqlitePlayHistoryRepository(
    ILogger<SqlitePlayHistoryRepository> logger,
    FingerprintDbContext dbContext)
  {
    _logger = logger;
    _dbContext = dbContext;
  }

  /// <inheritdoc/>
  public async Task RecordPlayAsync(PlayHistoryEntry entry, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      INSERT INTO PlayHistory (Id, TrackMetadataId, FingerprintId, PlayedAt, Source, MetadataSource, SourceDetails, Duration, IdentificationConfidence, WasIdentified)
      VALUES (@Id, @TrackMetadataId, @FingerprintId, @PlayedAt, @Source, @MetadataSource, @SourceDetails, @Duration, @Confidence, @WasIdentified)
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", entry.Id);
    cmd.Parameters.AddWithValue("@TrackMetadataId", (object?)entry.TrackMetadataId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@FingerprintId", (object?)entry.FingerprintId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@PlayedAt", entry.PlayedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@Source", entry.Source.ToString());
    cmd.Parameters.AddWithValue("@MetadataSource", entry.MetadataSource?.ToString() ?? (object)DBNull.Value);
    cmd.Parameters.AddWithValue("@SourceDetails", (object?)entry.SourceDetails ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Duration", (object?)entry.DurationSeconds ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Confidence", (object?)entry.IdentificationConfidence ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@WasIdentified", entry.WasIdentified ? 1 : 0);

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogDebug("Recorded play history entry {Id}", entry.Id);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(
    int count = 20,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      ORDER BY h.PlayedAt DESC
      LIMIT @Count
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Count", count);

    return await ReadPlayHistoryListAsync(cmd, ct);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<PlayHistoryEntry>> GetByDateRangeAsync(
    DateTime start,
    DateTime end,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE h.PlayedAt >= @Start AND h.PlayedAt <= @End
      ORDER BY h.PlayedAt DESC
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Start", start.ToString("O"));
    cmd.Parameters.AddWithValue("@End", end.ToString("O"));

    return await ReadPlayHistoryListAsync(cmd, ct);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<PlayHistoryEntry>> GetBySourceAsync(
    PlaySource source,
    int count = 20,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE h.Source = @Source
      ORDER BY h.PlayedAt DESC
      LIMIT @Count
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Source", source.ToString());
    cmd.Parameters.AddWithValue("@Count", count);

    return await ReadPlayHistoryListAsync(cmd, ct);
  }

  /// <inheritdoc/>
  public async Task<bool> ExistsRecentlyPlayedAsync(
    string title,
    string artist,
    int withinMinutes = 5,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var cutoffTime = DateTime.UtcNow.AddMinutes(-withinMinutes);

    var sql = """
      SELECT COUNT(*) FROM PlayHistory h
      JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE m.Title = @Title AND m.Artist = @Artist AND h.PlayedAt >= @CutoffTime
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Title", title);
    cmd.Parameters.AddWithValue("@Artist", artist);
    cmd.Parameters.AddWithValue("@CutoffTime", cutoffTime.ToString("O"));

    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    return count > 0;
  }

  /// <inheritdoc/>
  public async Task<PlayStatistics> GetStatisticsAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    // Get total counts
    await using var countCmd = conn.CreateCommand();
    countCmd.CommandText = """
      SELECT 
        COUNT(*) as Total,
        SUM(CASE WHEN WasIdentified = 1 THEN 1 ELSE 0 END) as Identified,
        SUM(CASE WHEN WasIdentified = 0 THEN 1 ELSE 0 END) as Unidentified
      FROM PlayHistory
      """;

    int totalPlays = 0, identifiedPlays = 0, unidentifiedPlays = 0;
    await using (var reader = await countCmd.ExecuteReaderAsync(ct))
    {
      if (await reader.ReadAsync(ct))
      {
        totalPlays = reader.GetInt32(0);
        identifiedPlays = reader.GetInt32(1);
        unidentifiedPlays = reader.GetInt32(2);
      }
    }

    // Get plays by source
    var playsBySource = new Dictionary<PlaySource, int>();
    await using var sourceCmd = conn.CreateCommand();
    sourceCmd.CommandText = """
      SELECT Source, COUNT(*) as Count
      FROM PlayHistory
      GROUP BY Source
      """;

    await using (var reader = await sourceCmd.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
      {
        var source = Enum.Parse<PlaySource>(reader.GetString(0));
        playsBySource[source] = reader.GetInt32(1);
      }
    }

    // Get top artists
    var topArtists = new List<ArtistPlayCount>();
    await using var artistCmd = conn.CreateCommand();
    artistCmd.CommandText = """
      SELECT m.Artist, COUNT(*) as PlayCount
      FROM PlayHistory h
      JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      GROUP BY m.Artist
      ORDER BY PlayCount DESC
      LIMIT 10
      """;

    await using (var reader = await artistCmd.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
      {
        topArtists.Add(new ArtistPlayCount
        {
          Artist = reader.GetString(0),
          PlayCount = reader.GetInt32(1)
        });
      }
    }

    // Get top tracks
    var topTracks = new List<TrackPlayCount>();
    await using var trackCmd = conn.CreateCommand();
    trackCmd.CommandText = """
      SELECT m.Title, m.Artist, COUNT(*) as PlayCount
      FROM PlayHistory h
      JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      GROUP BY m.Title, m.Artist
      ORDER BY PlayCount DESC
      LIMIT 10
      """;

    await using (var reader = await trackCmd.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
      {
        topTracks.Add(new TrackPlayCount
        {
          Title = reader.GetString(0),
          Artist = reader.GetString(1),
          PlayCount = reader.GetInt32(2)
        });
      }
    }

    return new PlayStatistics
    {
      TotalPlays = totalPlays,
      IdentifiedPlays = identifiedPlays,
      UnidentifiedPlays = unidentifiedPlays,
      PlaysBySource = playsBySource,
      TopArtists = topArtists,
      TopTracks = topTracks
    };
  }

  /// <inheritdoc/>
  public async Task<PlayHistoryEntry?> GetByIdAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE h.Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", id);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct))
    {
      return null;
    }

    return MapToPlayHistoryEntry(reader);
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM PlayHistory WHERE Id = @Id";
    cmd.Parameters.AddWithValue("@Id", id);

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Deleted play history entry {Id}", id);
    }

    return rowsAffected > 0;
  }

  private static async Task<IReadOnlyList<PlayHistoryEntry>> ReadPlayHistoryListAsync(
    Microsoft.Data.Sqlite.SqliteCommand cmd,
    CancellationToken ct)
  {
    var results = new List<PlayHistoryEntry>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      results.Add(MapToPlayHistoryEntry(reader));
    }

    return results;
  }

  private static PlayHistoryEntry MapToPlayHistoryEntry(Microsoft.Data.Sqlite.SqliteDataReader reader)
  {
    TrackMetadata? track = null;

    // Check if we have track metadata (joined data)
    if (!reader.IsDBNull(reader.GetOrdinal("Title")))
    {
      track = new TrackMetadata
      {
        Id = reader.GetString(reader.GetOrdinal("TrackMetadataId")),
        Title = reader.GetString(reader.GetOrdinal("Title")),
        Artist = reader.GetString(reader.GetOrdinal("Artist")),
        Album = reader.IsDBNull(reader.GetOrdinal("Album"))
          ? null : reader.GetString(reader.GetOrdinal("Album")),
        AlbumArtist = reader.IsDBNull(reader.GetOrdinal("AlbumArtist"))
          ? null : reader.GetString(reader.GetOrdinal("AlbumArtist")),
        CoverArtUrl = reader.IsDBNull(reader.GetOrdinal("CoverArtUrl"))
          ? null : reader.GetString(reader.GetOrdinal("CoverArtUrl")),
        Source = MetadataSource.AcoustID,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };
    }

    MetadataSource? metadataSource = null;
    var metadataSourceOrdinal = reader.GetOrdinal("MetadataSource");
    if (!reader.IsDBNull(metadataSourceOrdinal))
    {
      var metadataSourceStr = reader.GetString(metadataSourceOrdinal);
      if (Enum.TryParse<MetadataSource>(metadataSourceStr, out var parsedSource))
      {
        metadataSource = parsedSource;
      }
    }

    return new PlayHistoryEntry
    {
      Id = reader.GetString(reader.GetOrdinal("Id")),
      TrackMetadataId = reader.IsDBNull(reader.GetOrdinal("TrackMetadataId"))
        ? null : reader.GetString(reader.GetOrdinal("TrackMetadataId")),
      FingerprintId = reader.IsDBNull(reader.GetOrdinal("FingerprintId"))
        ? null : reader.GetString(reader.GetOrdinal("FingerprintId")),
      PlayedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("PlayedAt"))),
      Source = Enum.Parse<PlaySource>(reader.GetString(reader.GetOrdinal("Source"))),
      MetadataSource = metadataSource,
      SourceDetails = reader.IsDBNull(reader.GetOrdinal("SourceDetails"))
        ? null : reader.GetString(reader.GetOrdinal("SourceDetails")),
      DurationSeconds = reader.IsDBNull(reader.GetOrdinal("Duration"))
        ? null : reader.GetInt32(reader.GetOrdinal("Duration")),
      IdentificationConfidence = reader.IsDBNull(reader.GetOrdinal("IdentificationConfidence"))
        ? null : reader.GetDouble(reader.GetOrdinal("IdentificationConfidence")),
      WasIdentified = reader.GetInt32(reader.GetOrdinal("WasIdentified")) == 1,
      Track = track
    };
  }
}
