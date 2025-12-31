using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// SQLite implementation of the audio file repository.
/// Used by FileBrowser to track scanned files and detect changes.
/// </summary>
public sealed class SqliteAudioFileRepository : IAudioFileRepository
{
  private readonly ILogger<SqliteAudioFileRepository> _logger;
  private readonly FingerprintDbContext _dbContext;

  /// <summary>
  /// Initializes a new instance of the <see cref="SqliteAudioFileRepository"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="dbContext">The database context.</param>
  public SqliteAudioFileRepository(
    ILogger<SqliteAudioFileRepository> logger,
    FingerprintDbContext dbContext)
  {
    _logger = logger;
    _dbContext = dbContext;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<AudioFileInfo>> GetAllAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
             Title, Artist, Album, Duration, TrackNumber, Genre, Year
      FROM AudioFiles
      ORDER BY Path
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;

    return await ReadAudioFileListAsync(cmd, ct);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<AudioFileInfo>> GetByDirectoryAsync(
    string directoryPath,
    bool recursive = false,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    string sql;
    if (recursive)
    {
      sql = """
        SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
               Title, Artist, Album, Duration, TrackNumber, Genre, Year
        FROM AudioFiles
        WHERE Path LIKE @DirPattern
        ORDER BY Path
        """;
    }
    else
    {
      // For non-recursive, match files where the directory part equals the specified directory
      sql = """
        SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
               Title, Artist, Album, Duration, TrackNumber, Genre, Year
        FROM AudioFiles
        WHERE Path LIKE @DirPattern AND Path NOT LIKE @SubDirPattern
        ORDER BY Path
        """;
    }

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;

    // Normalize path separator for pattern matching
    var normalizedPath = directoryPath.Replace('\\', '/').TrimEnd('/');
    cmd.Parameters.AddWithValue("@DirPattern", normalizedPath + "/%");

    if (!recursive)
    {
      cmd.Parameters.AddWithValue("@SubDirPattern", normalizedPath + "/%/%");
    }

    return await ReadAudioFileListAsync(cmd, ct);
  }

  /// <inheritdoc/>
  public async Task<AudioFileInfo?> GetByPathAsync(string path, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
             Title, Artist, Album, Duration, TrackNumber, Genre, Year
      FROM AudioFiles
      WHERE Path = @Path
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("@Path", path);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct))
    {
      return null;
    }

    return MapToAudioFileInfo(reader);
  }

  /// <inheritdoc/>
  public async Task UpsertAsync(AudioFileInfo file, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    var sql = """
      INSERT INTO AudioFiles (Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt, Title, Artist, Album, Duration, TrackNumber, Genre, Year, ScannedAt)
      VALUES (@Path, @FileName, @Extension, @SizeBytes, @CreatedAt, @LastModifiedAt, @Title, @Artist, @Album, @Duration, @TrackNumber, @Genre, @Year, @ScannedAt)
      ON CONFLICT(Path) DO UPDATE SET
        FileName = excluded.FileName,
        Extension = excluded.Extension,
        SizeBytes = excluded.SizeBytes,
        CreatedAt = excluded.CreatedAt,
        LastModifiedAt = excluded.LastModifiedAt,
        Title = excluded.Title,
        Artist = excluded.Artist,
        Album = excluded.Album,
        Duration = excluded.Duration,
        TrackNumber = excluded.TrackNumber,
        Genre = excluded.Genre,
        Year = excluded.Year,
        ScannedAt = excluded.ScannedAt
      """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    AddAudioFileParameters(cmd, file);

    await cmd.ExecuteNonQueryAsync(ct);
    _logger.LogDebug("Upserted audio file: {Path}", file.Path);
  }

  /// <inheritdoc/>
  public async Task UpsertBatchAsync(IEnumerable<AudioFileInfo> files, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    await using var transaction = await conn.BeginTransactionAsync(ct);

    try
    {
      var sql = """
        INSERT INTO AudioFiles (Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt, Title, Artist, Album, Duration, TrackNumber, Genre, Year, ScannedAt)
        VALUES (@Path, @FileName, @Extension, @SizeBytes, @CreatedAt, @LastModifiedAt, @Title, @Artist, @Album, @Duration, @TrackNumber, @Genre, @Year, @ScannedAt)
        ON CONFLICT(Path) DO UPDATE SET
          FileName = excluded.FileName,
          Extension = excluded.Extension,
          SizeBytes = excluded.SizeBytes,
          CreatedAt = excluded.CreatedAt,
          LastModifiedAt = excluded.LastModifiedAt,
          Title = excluded.Title,
          Artist = excluded.Artist,
          Album = excluded.Album,
          Duration = excluded.Duration,
          TrackNumber = excluded.TrackNumber,
          Genre = excluded.Genre,
          Year = excluded.Year,
          ScannedAt = excluded.ScannedAt
        """;

      foreach (var file in files)
      {
        ct.ThrowIfCancellationRequested();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        AddAudioFileParameters(cmd, file);

        await cmd.ExecuteNonQueryAsync(ct);
      }

      await transaction.CommitAsync(ct);
      _logger.LogDebug("Upserted batch of audio files");
    }
    catch
    {
      await transaction.RollbackAsync(ct);
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task<bool> RemoveAsync(string path, CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM AudioFiles WHERE Path = @Path";
    cmd.Parameters.AddWithValue("@Path", path);

    var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
    if (rowsAffected > 0)
    {
      _logger.LogDebug("Removed audio file: {Path}", path);
    }

    return rowsAffected > 0;
  }

  /// <inheritdoc/>
  public async Task<int> RemoveStaleAsync(
    string directoryPath,
    IEnumerable<string> currentPaths,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    var currentPathSet = currentPaths.ToHashSet();

    // First get all files in the directory from the database
    var existingFiles = await GetByDirectoryAsync(directoryPath, recursive: true, ct);

    var stalePaths = existingFiles
      .Where(f => !currentPathSet.Contains(f.Path))
      .Select(f => f.Path)
      .ToList();

    if (stalePaths.Count == 0)
    {
      return 0;
    }

    await using var transaction = await conn.BeginTransactionAsync(ct);

    try
    {
      var removedCount = 0;
      foreach (var stalePath in stalePaths)
      {
        ct.ThrowIfCancellationRequested();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AudioFiles WHERE Path = @Path";
        cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        cmd.Parameters.AddWithValue("@Path", stalePath);

        removedCount += await cmd.ExecuteNonQueryAsync(ct);
      }

      await transaction.CommitAsync(ct);
      _logger.LogInformation("Removed {Count} stale audio files from directory: {Directory}", removedCount, directoryPath);

      return removedCount;
    }
    catch
    {
      await transaction.RollbackAsync(ct);
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task<int> GetCountAsync(CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM AudioFiles";

    var result = await cmd.ExecuteScalarAsync(ct);
    return Convert.ToInt32(result);
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<AudioFileInfo>> GetStaleMetadataAsync(
    IEnumerable<(string Path, DateTimeOffset LastModified)> currentFiles,
    CancellationToken ct = default)
  {
    var conn = await _dbContext.GetConnectionAsync(ct);
    var staleFiles = new List<AudioFileInfo>();

    foreach (var (path, lastModified) in currentFiles)
    {
      ct.ThrowIfCancellationRequested();

      var sql = """
        SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
               Title, Artist, Album, Duration, TrackNumber, Genre, Year
        FROM AudioFiles
        WHERE Path = @Path AND LastModifiedAt != @LastModified
        """;

      await using var cmd = conn.CreateCommand();
      cmd.CommandText = sql;
      cmd.Parameters.AddWithValue("@Path", path);
      cmd.Parameters.AddWithValue("@LastModified", lastModified.ToString("O"));

      await using var reader = await cmd.ExecuteReaderAsync(ct);
      if (await reader.ReadAsync(ct))
      {
        staleFiles.Add(MapToAudioFileInfo(reader));
      }
    }

    return staleFiles;
  }

  private static async Task<IReadOnlyList<AudioFileInfo>> ReadAudioFileListAsync(
    Microsoft.Data.Sqlite.SqliteCommand cmd,
    CancellationToken ct)
  {
    var results = new List<AudioFileInfo>();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
      results.Add(MapToAudioFileInfo(reader));
    }

    return results;
  }

  private static AudioFileInfo MapToAudioFileInfo(Microsoft.Data.Sqlite.SqliteDataReader reader)
  {
    TimeSpan? duration = null;
    var durationOrdinal = reader.GetOrdinal("Duration");
    if (!reader.IsDBNull(durationOrdinal))
    {
      duration = TimeSpan.FromMilliseconds(reader.GetInt64(durationOrdinal));
    }

    return new AudioFileInfo
    {
      Path = reader.GetString(reader.GetOrdinal("Path")),
      FileName = reader.GetString(reader.GetOrdinal("FileName")),
      Extension = reader.GetString(reader.GetOrdinal("Extension")),
      SizeBytes = reader.GetInt64(reader.GetOrdinal("SizeBytes")),
      CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
      LastModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastModifiedAt"))),
      Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? null : reader.GetString(reader.GetOrdinal("Title")),
      Artist = reader.IsDBNull(reader.GetOrdinal("Artist")) ? null : reader.GetString(reader.GetOrdinal("Artist")),
      Album = reader.IsDBNull(reader.GetOrdinal("Album")) ? null : reader.GetString(reader.GetOrdinal("Album")),
      Duration = duration,
      TrackNumber = reader.IsDBNull(reader.GetOrdinal("TrackNumber")) ? null : reader.GetInt32(reader.GetOrdinal("TrackNumber")),
      Genre = reader.IsDBNull(reader.GetOrdinal("Genre")) ? null : reader.GetString(reader.GetOrdinal("Genre")),
      Year = reader.IsDBNull(reader.GetOrdinal("Year")) ? null : reader.GetInt32(reader.GetOrdinal("Year"))
    };
  }

  private static void AddAudioFileParameters(Microsoft.Data.Sqlite.SqliteCommand cmd, AudioFileInfo file)
  {
    cmd.Parameters.AddWithValue("@Path", file.Path);
    cmd.Parameters.AddWithValue("@FileName", file.FileName);
    cmd.Parameters.AddWithValue("@Extension", file.Extension);
    cmd.Parameters.AddWithValue("@SizeBytes", file.SizeBytes);
    cmd.Parameters.AddWithValue("@CreatedAt", file.CreatedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@LastModifiedAt", file.LastModifiedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@Title", (object?)file.Title ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Artist", (object?)file.Artist ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Album", (object?)file.Album ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Duration", file.Duration.HasValue ? (object)file.Duration.Value.TotalMilliseconds : DBNull.Value);
    cmd.Parameters.AddWithValue("@TrackNumber", (object?)file.TrackNumber ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Genre", (object?)file.Genre ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Year", (object?)file.Year ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ScannedAt", DateTimeOffset.UtcNow.ToString("O"));
  }
}
