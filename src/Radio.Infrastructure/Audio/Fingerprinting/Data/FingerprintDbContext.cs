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
  private readonly DatabasePathResolver _pathResolver;
  private readonly SemaphoreSlim _initLock = new(1, 1);
  private SqliteConnection? _connection;
  private bool _initialized;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the <see cref="FingerprintDbContext"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="pathResolver">Database path resolver for unified path management.</param>
  public FingerprintDbContext(
    ILogger<FingerprintDbContext> logger,
    DatabasePathResolver pathResolver)
  {
    _logger = logger;
    _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
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

      var dbPath = _pathResolver.GetFingerprintingDatabasePath();
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

      -- Radio presets table
      CREATE TABLE IF NOT EXISTS RadioPresets (
        Id TEXT PRIMARY KEY,
        Name TEXT NOT NULL,
        Band TEXT NOT NULL,
        Frequency REAL NOT NULL,
        CreatedAt TEXT NOT NULL,
        LastModifiedAt TEXT NOT NULL
      );

      -- Audio files table for FileBrowser tracking
      CREATE TABLE IF NOT EXISTS AudioFiles (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Path TEXT NOT NULL UNIQUE,
        FileName TEXT NOT NULL,
        Extension TEXT NOT NULL,
        SizeBytes INTEGER NOT NULL,
        CreatedAt TEXT NOT NULL,
        LastModifiedAt TEXT NOT NULL,
        Title TEXT,
        Artist TEXT,
        Album TEXT,
        Duration INTEGER,
        TrackNumber INTEGER,
        Genre TEXT,
        Year INTEGER,
        ScannedAt TEXT NOT NULL
      );

      -- Cast device cache table
      CREATE TABLE IF NOT EXISTS CastDeviceCache (
        Id TEXT PRIMARY KEY,
        FriendlyName TEXT NOT NULL,
        IpAddress TEXT NOT NULL,
        Port INTEGER NOT NULL,
        Model TEXT NOT NULL,
        LastSeenAt TEXT NOT NULL
      );

      -- Playlists table
      CREATE TABLE IF NOT EXISTS Playlists (
        Id TEXT PRIMARY KEY,
        Name TEXT NOT NULL,
        Description TEXT,
        CreatedAt TEXT NOT NULL,
        ModifiedAt TEXT NOT NULL,
        ItemCount INTEGER NOT NULL DEFAULT 0
      );

      -- Playlist items table
      CREATE TABLE IF NOT EXISTS PlaylistItems (
        Id TEXT PRIMARY KEY,
        PlaylistId TEXT NOT NULL,
        Position INTEGER NOT NULL,
        FilePath TEXT NOT NULL,
        Title TEXT,
        Artist TEXT,
        Album TEXT,
        DurationMs INTEGER,
        FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id)
      );

      -- TTS voice cache table
      CREATE TABLE IF NOT EXISTS TTSVoiceCache (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Engine TEXT NOT NULL,
        VoiceId TEXT NOT NULL,
        Name TEXT NOT NULL,
        Language TEXT NOT NULL,
        Gender TEXT NOT NULL,
        PriceTier TEXT NOT NULL,
        LastUpdated TEXT NOT NULL,
        UNIQUE(Engine, VoiceId)
      );

      -- TTS voice favorites table
      CREATE TABLE IF NOT EXISTS TTSVoiceFavorites (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Engine TEXT NOT NULL,
        VoiceId TEXT NOT NULL,
        AddedAt TEXT NOT NULL,
        UNIQUE(Engine, VoiceId)
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
      CREATE INDEX IF NOT EXISTS IX_RadioPresets_Band_Frequency
        ON RadioPresets(Band, Frequency);
      CREATE INDEX IF NOT EXISTS IX_AudioFiles_Path
        ON AudioFiles(Path);
      CREATE INDEX IF NOT EXISTS IX_PlaylistItems_PlaylistId
        ON PlaylistItems(PlaylistId);
      CREATE INDEX IF NOT EXISTS IX_TTSVoiceCache_Engine
        ON TTSVoiceCache(Engine);
      CREATE INDEX IF NOT EXISTS IX_TTSVoiceFavorites_Engine
        ON TTSVoiceFavorites(Engine);
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
