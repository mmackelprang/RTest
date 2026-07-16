using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Abstractions;

namespace Radio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the play history repository.
/// </summary>
public sealed class SqlitePlayHistoryRepository : IPlayHistoryRepository
{
  private readonly ILogger<SqlitePlayHistoryRepository> _logger;
  private readonly IFingerprintDataConnection _dataConnection;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqlitePlayHistoryRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dataConnection">The fingerprint data connection.</param>
  public SqlitePlayHistoryRepository(
    ILogger<SqlitePlayHistoryRepository> logger,
    IFingerprintDataConnection dataConnection)
  {
    _logger = logger;
    _dataConnection = dataConnection;
  }

  /// <inheritdoc/>
  public async Task RecordPlayAsync(PlayHistoryEntry entry, CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    var sql = """
      INSERT INTO PlayHistory (Id, TrackMetadataId, FingerprintId, PlayedAt, EndedAt, Source, MetadataSource, SourceDetails, Duration, IdentificationConfidence, WasIdentified)
      VALUES (@Id, @TrackMetadataId, @FingerprintId, @PlayedAt, @EndedAt, @Source, @MetadataSource, @SourceDetails, @Duration, @Confidence, @WasIdentified)
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", entry.Id);
    cmd.Parameters.AddWithValue("@TrackMetadataId", (object?)entry.TrackMetadataId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@FingerprintId", (object?)entry.FingerprintId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@PlayedAt", entry.PlayedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@EndedAt", entry.EndedAt.HasValue ? entry.EndedAt.Value.ToString("O") : (object)DBNull.Value);
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
    var conn = await _dataConnection.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.EndedAt, h.Source, h.MetadataSource, h.SourceDetails,
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

  /// <summary>
  /// Default cap applied to <see cref="GetByDateRangeAsync"/> when the caller does not
  /// supply an explicit limit. Prevents a wide date range from materializing the whole
  /// table (which at scale is a multi-tens-of-MB transient allocation / OOM risk).
  /// </summary>
  private const int DefaultDateRangeLimit = 1000;

  /// <inheritdoc/>
  public async Task<IReadOnlyList<PlayHistoryEntry>> GetByDateRangeAsync(
    DateTime start,
    DateTime end,
    int? limit = null,
    int? offset = null,
    CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    // LIMIT/OFFSET are pushed into SQL (same pattern as SearchAsync) so the database —
    // not process memory — bounds the result set. A null limit falls back to a bounded
    // default rather than an unbounded scan of the range.
    var effectiveLimit = limit is > 0 ? limit.Value : DefaultDateRangeLimit;
    var effectiveOffset = offset is > 0 ? offset.Value : 0;

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.EndedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE h.PlayedAt >= @Start AND h.PlayedAt <= @End
      ORDER BY h.PlayedAt DESC
      LIMIT @Limit OFFSET @Offset
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Start", start.ToString("O"));
    cmd.Parameters.AddWithValue("@End", end.ToString("O"));
    cmd.Parameters.AddWithValue("@Limit", effectiveLimit);
    cmd.Parameters.AddWithValue("@Offset", effectiveOffset);

    return await ReadPlayHistoryListAsync(cmd, ct);
  }

  /// <inheritdoc/>
  public async Task<int> GetCountByDateRangeAsync(
    DateTime start,
    DateTime end,
    CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
      SELECT COUNT(*)
      FROM PlayHistory h
      WHERE h.PlayedAt >= @Start AND h.PlayedAt <= @End
      """;
    cmd.Parameters.AddWithValue("@Start", start.ToString("O"));
    cmd.Parameters.AddWithValue("@End", end.ToString("O"));

    return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
  }

  /// <inheritdoc/>
  public async Task<int> PruneOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM PlayHistory WHERE PlayedAt < @Cutoff";
    cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("O"));

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogInformation(
        "Pruned {Count} play history entries older than {Cutoff}", rowsAffected, cutoff);
    }

    return rowsAffected;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<PlayHistoryEntry>> GetBySourceAsync(
    PlaySource source,
    int count = 20,
    CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.EndedAt, h.Source, h.MetadataSource, h.SourceDetails,
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
    var conn = await _dataConnection.GetConnectionAsync(ct);

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
    var conn = await _dataConnection.GetConnectionAsync(ct);

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
        // SQLite returns 64-bit integers for COUNT/SUM - guard for NULL first
        totalPlays = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetInt64(0));
        identifiedPlays = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetInt64(1));
        unidentifiedPlays = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetInt64(2));
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
        if (!Enum.TryParse<PlaySource>(reader.GetString(0), out var source))
        {
          continue; // Skip rows with unknown source types
        }
        // SQLite COUNT returns 64-bit integer, guard for NULL
        playsBySource[source] = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetInt64(1));
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
          // SQLite COUNT returns 64-bit integer, guard for NULL
          PlayCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetInt64(1))
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
          // SQLite COUNT returns 64-bit integer, guard for NULL
          PlayCount = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetInt64(2))
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
    var conn = await _dataConnection.GetConnectionAsync(ct);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.EndedAt, h.Source, h.MetadataSource, h.SourceDetails,
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
    var conn = await _dataConnection.GetConnectionAsync(ct);

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

  /// <inheritdoc/>
  public async Task<bool> UpdateAsync(PlayHistoryEntry entry, CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    var sql = """
      UPDATE PlayHistory
      SET TrackMetadataId = @TrackMetadataId,
          FingerprintId = @FingerprintId,
          EndedAt = @EndedAt,
          MetadataSource = @MetadataSource,
          SourceDetails = @SourceDetails,
          Duration = @Duration,
          IdentificationConfidence = @Confidence,
          WasIdentified = @WasIdentified
      WHERE Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", entry.Id);
    cmd.Parameters.AddWithValue("@TrackMetadataId", (object?)entry.TrackMetadataId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@FingerprintId", (object?)entry.FingerprintId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@EndedAt", entry.EndedAt.HasValue ? entry.EndedAt.Value.ToString("O") : (object)DBNull.Value);
    cmd.Parameters.AddWithValue("@MetadataSource", entry.MetadataSource?.ToString() ?? (object)DBNull.Value);
    cmd.Parameters.AddWithValue("@SourceDetails", (object?)entry.SourceDetails ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Duration", (object?)entry.DurationSeconds ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Confidence", (object?)entry.IdentificationConfidence ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@WasIdentified", entry.WasIdentified ? 1 : 0);

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Updated play history entry {Id}", entry.Id);
    }

    return rowsAffected > 0;
  }

  /// <inheritdoc/>
  public async Task<PlayHistoryEntry?> GetRecentUnidentifiedAsync(
    PlaySource source,
    int withinMinutes = 5,
    CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    var cutoffTime = DateTime.UtcNow.AddMinutes(-withinMinutes);

    var sql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.EndedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE h.Source = @Source
        AND h.WasIdentified = 0
        AND h.PlayedAt >= @CutoffTime
      ORDER BY h.PlayedAt DESC
      LIMIT 1
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Source", source.ToString());
    cmd.Parameters.AddWithValue("@CutoffTime", cutoffTime.ToString("O"));

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct))
    {
      return null;
    }

    return MapToPlayHistoryEntry(reader);
  }

  /// <inheritdoc/>
  public async Task<bool> FinalizeEntryAsync(string id, DateTime endedAt, CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);

    // Set EndedAt and calculate DurationSeconds from PlayedAt to endedAt
    var sql = """
      UPDATE PlayHistory
      SET EndedAt = @EndedAt,
          Duration = CAST((julianday(@EndedAt) - julianday(PlayedAt)) * 86400 AS INTEGER)
      WHERE Id = @Id
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Id", id);
    cmd.Parameters.AddWithValue("@EndedAt", endedAt.ToString("O"));

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Finalized play history entry {Id} (ended at {EndedAt})", id, endedAt);
    }

    return rowsAffected > 0;
  }

  /// <inheritdoc/>
  public async Task<int> CloseOrphanedEntriesAsync(TimeSpan olderThan, CancellationToken ct = default)
  {
    var conn = await _dataConnection.GetConnectionAsync(ct);
    var cutoff = DateTime.UtcNow - olderThan;

    // Close orphaned entries: EndedAt IS NULL and PlayedAt is older than the cutoff.
    // If Duration is already set (e.g. from AVRCP), compute EndedAt from PlayedAt + Duration.
    // Otherwise, use the cutoff time as EndedAt and back-calculate Duration.
    var sql = """
      UPDATE PlayHistory
      SET EndedAt = CASE
            WHEN Duration IS NOT NULL AND Duration > 0
              THEN datetime(PlayedAt, '+' || Duration || ' seconds')
            ELSE @Cutoff
          END,
          Duration = CASE
            WHEN Duration IS NOT NULL AND Duration > 0
              THEN Duration
            ELSE CAST((julianday(@Cutoff) - julianday(PlayedAt)) * 86400 AS INTEGER)
          END
      WHERE EndedAt IS NULL
        AND PlayedAt < @Cutoff
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("O"));

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogInformation("Closed {Count} orphaned play history entries (older than {Cutoff})",
        rowsAffected, cutoff);
    }

    return rowsAffected;
  }

  /// <inheritdoc/>
  public async Task<(IReadOnlyList<PlayHistoryEntry> Items, int TotalCount)> SearchAsync(
    string searchTerm,
    int? limit = null,
    int? offset = null,
    CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(searchTerm);

    var conn = await _dataConnection.GetConnectionAsync(ct);
    var searchPattern = $"%{searchTerm}%";

    // Get total count
    var countSql = """
      SELECT COUNT(*) FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE m.Title LIKE @Search COLLATE NOCASE
         OR m.Artist LIKE @Search COLLATE NOCASE
         OR m.Album LIKE @Search COLLATE NOCASE
         OR h.SourceDetails LIKE @Search COLLATE NOCASE
      """;

    await using var countCmd = conn.CreateCommand();
    countCmd.CommandText = countSql;
    countCmd.Parameters.AddWithValue("@Search", searchPattern);

    var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

    // Get items with pagination
    var itemsSql = """
      SELECT h.Id, h.TrackMetadataId, h.FingerprintId, h.PlayedAt, h.EndedAt, h.Source, h.MetadataSource, h.SourceDetails,
             h.Duration, h.IdentificationConfidence, h.WasIdentified,
             m.Title, m.Artist, m.Album, m.AlbumArtist, m.CoverArtUrl
      FROM PlayHistory h
      LEFT JOIN TrackMetadata m ON h.TrackMetadataId = m.Id
      WHERE m.Title LIKE @Search COLLATE NOCASE
         OR m.Artist LIKE @Search COLLATE NOCASE
         OR m.Album LIKE @Search COLLATE NOCASE
         OR h.SourceDetails LIKE @Search COLLATE NOCASE
      ORDER BY h.PlayedAt DESC
      LIMIT @Limit OFFSET @Offset
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = itemsSql;
    cmd.Parameters.AddWithValue("@Search", searchPattern);
    cmd.Parameters.AddWithValue("@Limit", limit ?? 50);
    cmd.Parameters.AddWithValue("@Offset", offset ?? 0);

    var items = await ReadPlayHistoryListAsync(cmd, ct);

    _logger.LogDebug("Search for '{SearchTerm}' returned {Count} of {Total} results",
      searchTerm, items.Count, totalCount);

    return (items, totalCount);
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
        Source = MetadataSource.Fingerprinting,
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

    var endedAtOrdinal = reader.GetOrdinal("EndedAt");
    DateTime? endedAt = reader.IsDBNull(endedAtOrdinal) ? null : DateTime.Parse(reader.GetString(endedAtOrdinal));

    return new PlayHistoryEntry
    {
      Id = reader.GetString(reader.GetOrdinal("Id")),
      TrackMetadataId = reader.IsDBNull(reader.GetOrdinal("TrackMetadataId"))
        ? null : reader.GetString(reader.GetOrdinal("TrackMetadataId")),
      FingerprintId = reader.IsDBNull(reader.GetOrdinal("FingerprintId"))
        ? null : reader.GetString(reader.GetOrdinal("FingerprintId")),
      PlayedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("PlayedAt"))),
      EndedAt = endedAt,
      Source = Enum.TryParse<PlaySource>(reader.GetString(reader.GetOrdinal("Source")), out var parsedPlaySource)
        ? parsedPlaySource : PlaySource.File,
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
