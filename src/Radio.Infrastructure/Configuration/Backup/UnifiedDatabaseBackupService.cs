namespace Radio.Infrastructure.Configuration.Backup;

using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;

/// <summary>
/// Provides unified backup and restore capabilities for all SQLite databases.
/// Creates single archive files containing configuration, metrics, and fingerprinting databases.
/// </summary>
public sealed class UnifiedDatabaseBackupService : IUnifiedDatabaseBackupService
{
  private const string BackupExtension = ".dbbackup";
  private const string ManifestFileName = "manifest.json";
  private const string DatabasesFolder = "databases";
  private const string ReadmeFileName = "README.txt";

  private readonly DatabaseOptions _databaseOptions;
  private readonly DatabasePathResolver _pathResolver;
  private readonly ILogger<UnifiedDatabaseBackupService> _logger;
  private readonly JsonSerializerOptions _jsonOptions;

  private readonly IOptions<Radio.Infrastructure.Configuration.Models.ConfigurationOptions> _configOptions;
  private readonly IOptions<MetricsOptions> _metricsOptions;
  private readonly IOptions<FingerprintingOptions> _fingerprintingOptions;

  /// <summary>
  /// Initializes a new instance of the UnifiedDatabaseBackupService class.
  /// </summary>
  public UnifiedDatabaseBackupService(
    IOptions<DatabaseOptions> databaseOptions,
    DatabasePathResolver pathResolver,
    IOptions<Radio.Infrastructure.Configuration.Models.ConfigurationOptions> configOptions,
    IOptions<MetricsOptions> metricsOptions,
    IOptions<FingerprintingOptions> fingerprintingOptions,
    ILogger<UnifiedDatabaseBackupService> logger)
  {
    ArgumentNullException.ThrowIfNull(databaseOptions);
    ArgumentNullException.ThrowIfNull(pathResolver);
    ArgumentNullException.ThrowIfNull(logger);

    _databaseOptions = databaseOptions.Value;
    _pathResolver = pathResolver;
    _configOptions = configOptions;
    _metricsOptions = metricsOptions;
    _fingerprintingOptions = fingerprintingOptions;
    _logger = logger;
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
  }

  /// <inheritdoc/>
  public async Task<UnifiedBackupMetadata> CreateFullBackupAsync(string? description = null, CancellationToken ct = default)
  {
    var backupId = GenerateBackupId();
    var backupPath = GetBackupFilePath(backupId);

    EnsureBackupDirectoryExists();

    // Get all database paths
    var dbPaths = GetAllDatabasePaths();
    var includedDatabases = new List<string>();
    var includesSecrets = false;

    _logger.LogInformation("Creating unified database backup {BackupId}", backupId);

    await using var fileStream = File.Create(backupPath);
    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

    // Backup each database that exists
    foreach (var (dbPath, dbName) in dbPaths)
    {
      if (File.Exists(dbPath))
      {
        var entryName = $"{DatabasesFolder}/{dbName}";
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        
        await using var entryStream = entry.Open();
        await using var dbStream = File.OpenRead(dbPath);
        await dbStream.CopyToAsync(entryStream, ct);

        includedDatabases.Add(dbName);
        _logger.LogDebug("Added database {DbName} to backup", dbName);

        // Check if this is the configuration database (contains secrets)
        if (dbName.Contains("configuration", StringComparison.OrdinalIgnoreCase))
        {
          includesSecrets = true;
        }
      }
      else
      {
        _logger.LogWarning("Database file not found, skipping: {Path}", dbPath);
      }
    }

    // Create manifest
    var manifest = new BackupManifest
    {
      Version = 1,
      BackupId = backupId,
      CreatedAt = DateTimeOffset.UtcNow,
      Description = description,
      IncludedDatabases = includedDatabases,
      IncludesSecrets = includesSecrets
    };

    var manifestEntry = archive.CreateEntry(ManifestFileName);
    await using (var manifestStream = manifestEntry.Open())
    {
      await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);
    }

    // Create README
    var readmeEntry = archive.CreateEntry(ReadmeFileName);
    await using (var readmeStream = readmeEntry.Open())
    await using (var writer = new StreamWriter(readmeStream))
    {
      await writer.WriteLineAsync("Radio Console - Unified Database Backup");
      await writer.WriteLineAsync($"Created: {manifest.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
      await writer.WriteLineAsync($"Backup ID: {backupId}");
      await writer.WriteLineAsync();
      await writer.WriteLineAsync("Included Databases:");
      foreach (var db in includedDatabases)
      {
        await writer.WriteLineAsync($"  - {db}");
      }
      if (includesSecrets)
      {
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("WARNING: This backup contains encrypted secrets.");
        await writer.WriteLineAsync("Keep this file secure and encrypted when storing or transmitting.");
      }
      if (!string.IsNullOrWhiteSpace(description))
      {
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"Description: {description}");
      }
    }

    var fileInfo = new FileInfo(backupPath);
    var metadata = new UnifiedBackupMetadata
    {
      BackupId = backupId,
      CreatedAt = manifest.CreatedAt,
      Description = description,
      SizeBytes = fileInfo.Length,
      FilePath = backupPath,
      IncludedDatabases = includedDatabases,
      IncludesSecrets = includesSecrets
    };

    _logger.LogInformation(
      "Created unified backup {BackupId} with {Count} databases ({Size} bytes)",
      backupId, includedDatabases.Count, fileInfo.Length);

    return metadata;
  }

  /// <inheritdoc/>
  public async Task RestoreBackupAsync(string backupId, bool overwrite = false, CancellationToken ct = default)
  {
    var backupPath = GetBackupFilePath(backupId);
    if (!File.Exists(backupPath))
    {
      throw new FileNotFoundException($"Backup file not found: {backupId}", backupPath);
    }

    _logger.LogInformation("Restoring unified backup {BackupId}", backupId);

    await using var fileStream = File.OpenRead(backupPath);
    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

    // Read manifest
    var manifestEntry = archive.GetEntry(ManifestFileName);
    if (manifestEntry == null)
    {
      throw new InvalidOperationException("Invalid backup file: missing manifest");
    }

    BackupManifest manifest;
    await using (var manifestStream = manifestEntry.Open())
    {
      manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, _jsonOptions, ct)
        ?? throw new InvalidOperationException("Invalid backup file: corrupt manifest");
    }

    // Get current database paths
    var dbPaths = GetAllDatabasePaths().ToDictionary(x => x.dbName, x => x.dbPath);

    // Restore each database
    foreach (var dbName in manifest.IncludedDatabases)
    {
      var entryName = $"{DatabasesFolder}/{dbName}";
      var dbEntry = archive.GetEntry(entryName);
      if (dbEntry == null)
      {
        _logger.LogWarning("Database {DbName} listed in manifest but not found in archive", dbName);
        continue;
      }

      if (!dbPaths.TryGetValue(dbName, out var targetPath))
      {
        _logger.LogWarning("Unknown database {DbName}, skipping restore", dbName);
        continue;
      }

      // Check if database exists
      if (File.Exists(targetPath) && !overwrite)
      {
        throw new InvalidOperationException(
          $"Database '{dbName}' already exists at {targetPath}. Use overwrite=true to replace.");
      }

      // Ensure directory exists
      var directory = Path.GetDirectoryName(targetPath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      // Restore database file
      await using var dbStream = dbEntry.Open();
      await using var targetStream = File.Create(targetPath);
      await dbStream.CopyToAsync(targetStream, ct);

      _logger.LogInformation("Restored database {DbName} to {Path}", dbName, targetPath);
    }

    _logger.LogInformation("Completed restore of backup {BackupId}", backupId);
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<UnifiedBackupMetadata>> ListBackupsAsync(CancellationToken ct = default)
  {
    var backupPath = _pathResolver.GetBackupPath(_configOptions.Value.BackupPath);
    if (!Directory.Exists(backupPath))
    {
      return Task.FromResult<IReadOnlyList<UnifiedBackupMetadata>>(Array.Empty<UnifiedBackupMetadata>());
    }

    var backups = new List<UnifiedBackupMetadata>();
    var files = Directory.GetFiles(backupPath, $"*{BackupExtension}");

    foreach (var file in files)
    {
      try
      {
        var metadata = ReadBackupMetadata(file);
        backups.Add(metadata);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to read backup metadata from {File}", file);
      }
    }

    return Task.FromResult<IReadOnlyList<UnifiedBackupMetadata>>(
      backups.OrderByDescending(b => b.CreatedAt).ToList());
  }

  /// <inheritdoc/>
  public Task<bool> DeleteBackupAsync(string backupId, CancellationToken ct = default)
  {
    var backupPath = GetBackupFilePath(backupId);
    if (File.Exists(backupPath))
    {
      File.Delete(backupPath);
      _logger.LogInformation("Deleted backup {BackupId}", backupId);
      return Task.FromResult(true);
    }
    return Task.FromResult(false);
  }

  /// <inheritdoc/>
  public async Task ExportBackupAsync(string backupId, Stream destination, CancellationToken ct = default)
  {
    var backupPath = GetBackupFilePath(backupId);
    if (!File.Exists(backupPath))
    {
      throw new FileNotFoundException($"Backup file not found: {backupId}", backupPath);
    }

    await using var fileStream = File.OpenRead(backupPath);
    await fileStream.CopyToAsync(destination, ct);
    _logger.LogInformation("Exported backup {BackupId}", backupId);
  }

  /// <inheritdoc/>
  public async Task<UnifiedBackupMetadata> ImportBackupAsync(Stream source, CancellationToken ct = default)
  {
    EnsureBackupDirectoryExists();

    // Read to temp file first to validate
    var tempPath = Path.GetTempFileName();
    try
    {
      await using (var tempStream = File.Create(tempPath))
      {
        await source.CopyToAsync(tempStream, ct);
      }

      // Validate and extract metadata
      var metadata = ReadBackupMetadata(tempPath);

      // Move to backup directory
      var finalPath = GetBackupFilePath(metadata.BackupId);
      File.Move(tempPath, finalPath, overwrite: true);

      var fileInfo = new FileInfo(finalPath);
      var result = metadata with
      {
        FilePath = finalPath,
        SizeBytes = fileInfo.Length
      };

      _logger.LogInformation("Imported backup {BackupId}", result.BackupId);
      return result;
    }
    finally
    {
      if (File.Exists(tempPath))
      {
        File.Delete(tempPath);
      }
    }
  }

  /// <inheritdoc/>
  public Task<int> CleanupOldBackupsAsync(CancellationToken ct = default)
  {
    var backupPath = _pathResolver.GetBackupPath(_configOptions.Value.BackupPath);
    if (!Directory.Exists(backupPath))
    {
      return Task.FromResult(0);
    }

    var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_databaseOptions.BackupRetentionDays);
    var files = Directory.GetFiles(backupPath, $"*{BackupExtension}");
    var deletedCount = 0;

    foreach (var file in files)
    {
      try
      {
        var metadata = ReadBackupMetadata(file);
        if (metadata.CreatedAt < cutoffDate)
        {
          File.Delete(file);
          deletedCount++;
          _logger.LogInformation(
            "Deleted old backup {BackupId} (created {Created})",
            metadata.BackupId, metadata.CreatedAt);
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to process backup file {File}", file);
      }
    }

    if (deletedCount > 0)
    {
      _logger.LogInformation("Cleaned up {Count} old backups", deletedCount);
    }

    return Task.FromResult(deletedCount);
  }

  private string GenerateBackupId()
  {
    var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
    var shortGuid = Guid.NewGuid().ToString("N")[..6];
    return $"unified_{timestamp}_{shortGuid}";
  }

  private string GetBackupFilePath(string backupId)
  {
    var backupPath = _pathResolver.GetBackupPath(_configOptions.Value.BackupPath);
    return Path.Combine(backupPath, $"{backupId}{BackupExtension}");
  }

  private void EnsureBackupDirectoryExists()
  {
    var backupPath = _pathResolver.GetBackupPath(_configOptions.Value.BackupPath);
    if (!Directory.Exists(backupPath))
    {
      Directory.CreateDirectory(backupPath);
      _logger.LogInformation("Created backup directory: {Path}", backupPath);
    }
  }

  private List<(string dbPath, string dbName)> GetAllDatabasePaths()
  {
    var configPath = _pathResolver.GetConfigurationDatabasePath(
      _configOptions.Value.BasePath,
      _configOptions.Value.SqliteFileName);
    var metricsPath = _pathResolver.GetMetricsDatabasePath(_metricsOptions.Value.DatabasePath);
    var fingerprintingPath = _pathResolver.GetFingerprintingDatabasePath(_fingerprintingOptions.Value.DatabasePath);

    return new List<(string, string)>
    {
      (configPath, "configuration.db"),
      (metricsPath, "metrics.db"),
      (fingerprintingPath, "fingerprints.db")
    };
  }

  private UnifiedBackupMetadata ReadBackupMetadata(string filePath)
  {
    using var fileStream = File.OpenRead(filePath);
    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

    var manifestEntry = archive.GetEntry(ManifestFileName);
    if (manifestEntry == null)
    {
      throw new InvalidOperationException("Invalid backup file: missing manifest");
    }

    using var manifestStream = manifestEntry.Open();
    var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestStream, _jsonOptions)
      ?? throw new InvalidOperationException("Invalid backup file: corrupt manifest");

    var fileInfo = new FileInfo(filePath);
    return new UnifiedBackupMetadata
    {
      BackupId = manifest.BackupId,
      CreatedAt = manifest.CreatedAt,
      Description = manifest.Description,
      SizeBytes = fileInfo.Length,
      FilePath = filePath,
      IncludedDatabases = manifest.IncludedDatabases,
      IncludesSecrets = manifest.IncludesSecrets
    };
  }

  private sealed record BackupManifest
  {
    public int Version { get; init; }
    public required string BackupId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> IncludedDatabases { get; init; }
    public bool IncludesSecrets { get; init; }
  }
}
