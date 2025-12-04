using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Audio.Fingerprinting.Data;

/// <summary>
/// Manages the SQLite database connection for fingerprinting data.
/// </summary>
public sealed class FingerprintDbContext : IAsyncDisposable
{
  private readonly ILogger<FingerprintDbContext> _logger;
  private readonly FingerprintingOptions _options;
  private readonly DatabasePathResolver? _pathResolver;
  private readonly SemaphoreSlim _initLock = new(1, 1);
  private SqliteConnection? _connection;
  private bool _initialized;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="FingerprintDbContext"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The fingerprinting options.</param>
  /// <param name="pathResolver">Optional database path resolver for unified path management.</param>
  public FingerprintDbContext(
    ILogger<FingerprintDbContext> logger,
    IOptions<FingerprintingOptions> options,
    DatabasePathResolver? pathResolver = null)
  {
    _logger = logger;
    _options = options.Value;
    _pathResolver = pathResolver;
  }

  /// <summary>
  /// Initializes the database connection and creates tables if needed.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  public async Task InitializeAsync(CancellationToken ct = default)
  {
    if (_initialized) return;

    await _initLock.WaitAsync(ct);
    try
    {
      if (_initialized) return;

      var dbPath = _pathResolver?.GetFingerprintingDatabasePath(_options.DatabasePath)
        ?? _options.DatabasePath;
      var directory = Path.GetDirectoryName(dbPath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
        _logger.LogInformation("Created fingerprint database directory: {Path}", directory);
      }

      var connectionString = $"Data Source={dbPath}";
      _connection = new SqliteConnection(connectionString);
      await _connection.OpenAsync(ct);

      _logger.LogInformation("Connected to fingerprint database: {Path}", dbPath);

      await CreateTablesAsync(ct);
      await MigrateSchemaAsync(ct);
      _initialized = true;
    }
    finally
    {
      _initLock.Release();
    }
  }

  /// <summary>
  /// Gets the database connection, initializing if needed.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The SQLite connection.</returns>
  public async Task<SqliteConnection> GetConnectionAsync(CancellationToken ct = default)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);

    if (!_initialized)
    {
      await InitializeAsync(ct);
    }

    return _connection!;
  }

  private async Task CreateTablesAsync(CancellationToken ct)
  {
    var createTablesSql = """
      -- Fingerprint cache table
      CREATE TABLE IF NOT EXISTS FingerprintCache (
        Id TEXT PRIMARY KEY,
        ChromaprintHash TEXT NOT NULL UNIQUE,
        Duration INTEGER NOT NULL,
        AcoustId TEXT,
        MusicBrainzRecordingId TEXT,
        CreatedAt TEXT NOT NULL,
        LastMatchedAt TEXT,
        MatchCount INTEGER DEFAULT 0
      );

      -- Track metadata table
      CREATE TABLE IF NOT EXISTS TrackMetadata (
        Id TEXT PRIMARY KEY,
        FingerprintId TEXT,
        Title TEXT NOT NULL,
        Artist TEXT NOT NULL,
        Album TEXT,
        AlbumArtist TEXT,
        TrackNumber INTEGER,
        DiscNumber INTEGER,
        ReleaseYear INTEGER,
        Genre TEXT,
        MusicBrainzArtistId TEXT,
        MusicBrainzReleaseId TEXT,
        MusicBrainzRecordingId TEXT,
        CoverArtUrl TEXT,
        Source TEXT NOT NULL,
        CreatedAt TEXT NOT NULL,
        UpdatedAt TEXT NOT NULL,
        FOREIGN KEY (FingerprintId) REFERENCES FingerprintCache(Id)
      );

      -- Play history table
      CREATE TABLE IF NOT EXISTS PlayHistory (
        Id TEXT PRIMARY KEY,
        TrackMetadataId TEXT,
        FingerprintId TEXT,
        PlayedAt TEXT NOT NULL,
        Source TEXT NOT NULL,
        MetadataSource TEXT,
        SourceDetails TEXT,
        Duration INTEGER,
        IdentificationConfidence REAL,
        WasIdentified INTEGER NOT NULL,
        FOREIGN KEY (TrackMetadataId) REFERENCES TrackMetadata(Id),
        FOREIGN KEY (FingerprintId) REFERENCES FingerprintCache(Id)
      );

      -- Indexes for performance
      CREATE INDEX IF NOT EXISTS IX_FingerprintCache_ChromaprintHash 
        ON FingerprintCache(ChromaprintHash);
      CREATE INDEX IF NOT EXISTS IX_FingerprintCache_AcoustId 
        ON FingerprintCache(AcoustId);
      CREATE INDEX IF NOT EXISTS IX_TrackMetadata_FingerprintId 
        ON TrackMetadata(FingerprintId);
      CREATE INDEX IF NOT EXISTS IX_TrackMetadata_Artist 
        ON TrackMetadata(Artist);
      CREATE INDEX IF NOT EXISTS IX_TrackMetadata_Title 
        ON TrackMetadata(Title);
      CREATE INDEX IF NOT EXISTS IX_PlayHistory_PlayedAt 
        ON PlayHistory(PlayedAt);
      CREATE INDEX IF NOT EXISTS IX_PlayHistory_TrackMetadataId 
        ON PlayHistory(TrackMetadataId);
      CREATE INDEX IF NOT EXISTS IX_PlayHistory_Source
        ON PlayHistory(Source);
      """;

    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = createTablesSql;
    await cmd.ExecuteNonQueryAsync(ct);

    _logger.LogDebug("Fingerprint database tables created/verified");
  }

  private async Task MigrateSchemaAsync(CancellationToken ct)
  {
    // Add MetadataSource column if it doesn't exist (migration for existing databases)
    var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('PlayHistory') WHERE name='MetadataSource'";
    using var checkCmd = _connection!.CreateCommand();
    checkCmd.CommandText = checkColumnSql;
    var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

    if (!columnExists)
    {
      var alterSql = "ALTER TABLE PlayHistory ADD COLUMN MetadataSource TEXT";
      using var alterCmd = _connection.CreateCommand();
      alterCmd.CommandText = alterSql;
      await alterCmd.ExecuteNonQueryAsync(ct);
      _logger.LogInformation("Added MetadataSource column to PlayHistory table");
    }
  }

  /// <inheritdoc/>
  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    if (_connection != null)
    {
      await _connection.CloseAsync();
      await _connection.DisposeAsync();
      _connection = null;
    }

    _initLock.Dispose();
    _logger.LogDebug("FingerprintDbContext disposed");
  }
}
