namespace Radio.Infrastructure.Configuration.Backup;

using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Provides backup and restore capabilities for configuration stores.
/// </summary>
public sealed class ConfigurationBackupService : IConfigurationBackupService
{
  private const string BackupExtension = ".radiobak";
  private const string ManifestFileName = "manifest.json";
  private const string StoresFolder = "stores";

  private readonly ConfigurationOptions _options;
  private readonly IConfigurationStoreFactory _storeFactory;
  private readonly ILogger<ConfigurationBackupService> _logger;
  private readonly JsonSerializerOptions _jsonOptions;

  /// <summary>
  /// Initializes a new instance of the ConfigurationBackupService class.
  /// </summary>
  public ConfigurationBackupService(
    IOptions<ConfigurationOptions> options,
    IConfigurationStoreFactory storeFactory,
    ILogger<ConfigurationBackupService> logger)
  {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(storeFactory);
    ArgumentNullException.ThrowIfNull(logger);

    _options = options.Value;
    _storeFactory = storeFactory;
    _logger = logger;
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
  }

  /// <inheritdoc/>
  public async Task<BackupMetadata> CreateBackupAsync(string storeId, ConfigurationStoreType storeType, string? description = null, CancellationToken ct = default)
  {
    var backupId = GenerateBackupId(storeId);
    var backupPath = GetBackupFilePath(backupId);

    EnsureBackupDirectoryExists();

    var store = await _storeFactory.CreateStoreAsync(storeId, storeType, ct);
    var entries = await store.GetAllEntriesAsync(ConfigurationReadMode.Raw, ct);

    await using var fileStream = File.Create(backupPath);
    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

    // Add entries as JSON
    var storeEntry = archive.CreateEntry($"{StoresFolder}/{storeId}.json");
    await using (var entryStream = storeEntry.Open())
    {
      await JsonSerializer.SerializeAsync(entryStream, entries, _jsonOptions, ct);
    }

    // Create manifest
    var manifest = new BackupManifest
    {
      Version = 1,
      BackupId = backupId,
      StoreId = storeId,
      StoreType = storeType,
      CreatedAt = DateTimeOffset.UtcNow,
      Description = description,
      IncludesSecrets = entries.Any(e => e.ContainsSecret)
    };

    var manifestEntry = archive.CreateEntry(ManifestFileName);
    await using (var manifestStream = manifestEntry.Open())
    {
      await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);
    }

    var fileInfo = new FileInfo(backupPath);
    var metadata = new BackupMetadata
    {
      BackupId = backupId,
      StoreId = storeId,
      StoreType = storeType,
      CreatedAt = manifest.CreatedAt,
      Description = description,
      SizeBytes = fileInfo.Length,
      FilePath = backupPath,
      IncludesSecrets = manifest.IncludesSecrets
    };

    _logger.LogInformation("Created backup {BackupId} for store {StoreId}", backupId, storeId);
    return metadata;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<BackupMetadata>> CreateFullBackupAsync(string? description = null, CancellationToken ct = default)
  {
    var backups = new List<BackupMetadata>();
    var storeType = _options.DefaultStoreType;

    var storeIds = await _storeFactory.ListStoresAsync(storeType, ct);
    foreach (var storeId in storeIds)
    {
      var backup = await CreateBackupAsync(storeId, storeType, description, ct);
      backups.Add(backup);
    }

    _logger.LogInformation("Created full backup with {Count} stores", backups.Count);
    return backups;
  }

  /// <inheritdoc/>
  public async Task RestoreBackupAsync(string backupId, bool overwrite = false, CancellationToken ct = default)
  {
    var backupPath = GetBackupFilePath(backupId);
    if (!File.Exists(backupPath))
    {
      throw new FileNotFoundException($"Backup file not found: {backupId}", backupPath);
    }

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

    // Check if store exists
    var storeExists = await _storeFactory.StoreExistsAsync(manifest.StoreId, manifest.StoreType, ct);
    if (storeExists && !overwrite)
    {
      throw new InvalidOperationException($"Store '{manifest.StoreId}' already exists. Use overwrite=true to replace.");
    }

    // Read store data
    var storeEntry = archive.GetEntry($"{StoresFolder}/{manifest.StoreId}.json");
    if (storeEntry == null)
    {
      throw new InvalidOperationException("Invalid backup file: missing store data");
    }

    List<ConfigurationEntry> entries;
    await using (var storeStream = storeEntry.Open())
    {
      entries = await JsonSerializer.DeserializeAsync<List<ConfigurationEntry>>(storeStream, _jsonOptions, ct)
        ?? new List<ConfigurationEntry>();
    }

    // Restore to store
    var store = await _storeFactory.CreateStoreAsync(manifest.StoreId, manifest.StoreType, ct);
    await store.SetEntriesAsync(entries, ct);
    await store.SaveAsync(ct);

    _logger.LogInformation("Restored backup {BackupId} to store {StoreId}", backupId, manifest.StoreId);
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<BackupMetadata>> ListBackupsAsync(string? storeId = null, CancellationToken ct = default)
  {
    var backupPath = Path.GetFullPath(_options.BackupPath);
    if (!Directory.Exists(backupPath))
    {
      return Task.FromResult<IReadOnlyList<BackupMetadata>>(Array.Empty<BackupMetadata>());
    }

    var backups = new List<BackupMetadata>();
    var files = Directory.GetFiles(backupPath, $"*{BackupExtension}");

    foreach (var file in files)
    {
      try
      {
        var metadata = ReadBackupMetadata(file);
        if (storeId == null || metadata.StoreId == storeId)
        {
          backups.Add(metadata);
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to read backup metadata from {File}", file);
      }
    }

    return Task.FromResult<IReadOnlyList<BackupMetadata>>(backups.OrderByDescending(b => b.CreatedAt).ToList());
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
  public async Task<BackupMetadata> ImportBackupAsync(Stream source, CancellationToken ct = default)
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

  private string GenerateBackupId(string storeId)
  {
    var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
    var shortGuid = Guid.NewGuid().ToString("N")[..6];
    return $"{storeId}_{timestamp}_{shortGuid}";
  }

  private string GetBackupFilePath(string backupId)
  {
    var backupPath = Path.GetFullPath(_options.BackupPath);
    return Path.Combine(backupPath, $"{backupId}{BackupExtension}");
  }

  private void EnsureBackupDirectoryExists()
  {
    var backupPath = Path.GetFullPath(_options.BackupPath);
    if (!Directory.Exists(backupPath))
    {
      Directory.CreateDirectory(backupPath);
    }
  }

  private BackupMetadata ReadBackupMetadata(string filePath)
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
    return new BackupMetadata
    {
      BackupId = manifest.BackupId,
      StoreId = manifest.StoreId,
      StoreType = manifest.StoreType,
      CreatedAt = manifest.CreatedAt,
      Description = manifest.Description,
      SizeBytes = fileInfo.Length,
      FilePath = filePath,
      IncludesSecrets = manifest.IncludesSecrets
    };
  }

  private sealed record BackupManifest
  {
    public int Version { get; init; }
    public required string BackupId { get; init; }
    public required string StoreId { get; init; }
    public required ConfigurationStoreType StoreType { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Description { get; init; }
    public bool IncludesSecrets { get; init; }
  }
}
