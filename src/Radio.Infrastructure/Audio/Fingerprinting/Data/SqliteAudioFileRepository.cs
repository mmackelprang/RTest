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

    // Use ternary for simpler assignment
    var sql = recursive
      ? """
        SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
               Title, Artist, Album, Duration, TrackNumber, Genre, Year
        FROM AudioFiles
        WHERE Path LIKE @DirPattern
        ORDER BY Path
        """
      : """
        SELECT Path, FileName, Extension, SizeBytes, CreatedAt, LastModifiedAt,
               Title, Artist, Album, Duration, TrackNumber, Genre, Year
        FROM AudioFiles
        WHERE Path LIKE @DirPattern AND Path NOT LIKE @SubDirPattern
        ORDER BY Path
        """;

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

      // Reuse a single command and update parameters for each file to reduce allocations
      await using var cmd = conn.CreateCommand();
      cmd.CommandText = sql;
      cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;

      // Pre-create parameters once
      var pathParam = cmd.Parameters.Add("@Path", Microsoft.Data.Sqlite.SqliteType.Text);
      var fileNameParam = cmd.Parameters.Add("@FileName", Microsoft.Data.Sqlite.SqliteType.Text);
      var extensionParam = cmd.Parameters.Add("@Extension", Microsoft.Data.Sqlite.SqliteType.Text);
      var sizeBytesParam = cmd.Parameters.Add("@SizeBytes", Microsoft.Data.Sqlite.SqliteType.Integer);
      var createdAtParam = cmd.Parameters.Add("@CreatedAt", Microsoft.Data.Sqlite.SqliteType.Text);
      var lastModifiedAtParam = cmd.Parameters.Add("@LastModifiedAt", Microsoft.Data.Sqlite.SqliteType.Text);
      var titleParam = cmd.Parameters.Add("@Title", Microsoft.Data.Sqlite.SqliteType.Text);
      var artistParam = cmd.Parameters.Add("@Artist", Microsoft.Data.Sqlite.SqliteType.Text);
      var albumParam = cmd.Parameters.Add("@Album", Microsoft.Data.Sqlite.SqliteType.Text);
      var durationParam = cmd.Parameters.Add("@Duration", Microsoft.Data.Sqlite.SqliteType.Integer);
      var trackNumberParam = cmd.Parameters.Add("@TrackNumber", Microsoft.Data.Sqlite.SqliteType.Integer);
      var genreParam = cmd.Parameters.Add("@Genre", Microsoft.Data.Sqlite.SqliteType.Text);
      var yearParam = cmd.Parameters.Add("@Year", Microsoft.Data.Sqlite.SqliteType.Integer);
      var scannedAtParam = cmd.Parameters.Add("@ScannedAt", Microsoft.Data.Sqlite.SqliteType.Text);

      foreach (var file in files)
      {
        ct.ThrowIfCancellationRequested();

        // Update parameter values
        pathParam.Value = file.Path;
        fileNameParam.Value = file.FileName;
        extensionParam.Value = file.Extension;
        sizeBytesParam.Value = file.SizeBytes;
        createdAtParam.Value = file.CreatedAt.ToString("O");
        lastModifiedAtParam.Value = file.LastModifiedAt.ToString("O");
        titleParam.Value = (object?)file.Title ?? DBNull.Value;
        artistParam.Value = (object?)file.Artist ?? DBNull.Value;
        albumParam.Value = (object?)file.Album ?? DBNull.Value;
        durationParam.Value = file.Duration.HasValue ? (object)file.Duration.Value.TotalMilliseconds : DBNull.Value;
        trackNumberParam.Value = (object?)file.TrackNumber ?? DBNull.Value;
        genreParam.Value = (object?)file.Genre ?? DBNull.Value;
        yearParam.Value = (object?)file.Year ?? DBNull.Value;
        scannedAtParam.Value = DateTimeOffset.UtcNow.ToString("O");

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
      ct.ThrowIfCancellationRequested();

      var totalRemoved = 0;

      // SQLite has a parameter limit of ~999, so batch deletes for large collections
      const int batchSize = 500;
      for (var batchStart = 0; batchStart < stalePaths.Count; batchStart += batchSize)
      {
        var batch = stalePaths.Skip(batchStart).Take(batchSize).ToList();

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;

        var parameterNames = new List<string>(batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
          var parameterName = $"@p{i}";
          parameterNames.Add(parameterName);
          cmd.Parameters.AddWithValue(parameterName, batch[i]);
        }

        cmd.CommandText = $"DELETE FROM AudioFiles WHERE Path IN ({string.Join(",", parameterNames)})";

        totalRemoved += await cmd.ExecuteNonQueryAsync(ct);
      }

      await transaction.CommitAsync(ct);
      _logger.LogInformation("Removed {Count} stale audio files from directory: {Directory}", totalRemoved, directoryPath);

      return totalRemoved;
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
    var filesList = currentFiles.ToList();

    if (filesList.Count == 0)
    {
      return Array.Empty<AudioFileInfo>();
    }

    var allResults = new List<AudioFileInfo>();

    // SQLite has a parameter limit of ~999, so batch queries for large collections
    // Each file needs 2 parameters (path and modified), so use batch size of 250
    const int batchSize = 250;

    for (var batchStart = 0; batchStart < filesList.Count; batchStart += batchSize)
    {
      var batch = filesList.Skip(batchStart).Take(batchSize).ToList();

      await using var cmd = conn.CreateCommand();

      for (var i = 0; i < batch.Count; i++)
      {
        var (path, lastModified) = batch[i];
        cmd.Parameters.AddWithValue($"@p{i}", path);
        cmd.Parameters.AddWithValue($"@m{i}", lastModified.ToString("O"));
      }

      // Build VALUES clause for the expected file list
      var valuesClauses = batch.Select((_, i) => $"(@p{i}, @m{i})");

      cmd.CommandText = $"""
        WITH ExpectedFiles(Path, ExpectedModified) AS (
          VALUES {string.Join(", ", valuesClauses)}
        )
        SELECT af.Path, af.FileName, af.Extension, af.SizeBytes, af.CreatedAt, af.LastModifiedAt,
               af.Title, af.Artist, af.Album, af.Duration, af.TrackNumber, af.Genre, af.Year
        FROM AudioFiles af
        INNER JOIN ExpectedFiles ef ON af.Path = ef.Path
        WHERE af.LastModifiedAt != ef.ExpectedModified
        """;

      var batchResults = await ReadAudioFileListAsync(cmd, ct);
      allResults.AddRange(batchResults);
    }

    return allResults;
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
