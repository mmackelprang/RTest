namespace Radio.Infrastructure.Configuration.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Exceptions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// High-level configuration management that orchestrates stores, secrets, and backup operations.
/// </summary>
public sealed class ConfigurationManager : IConfigurationManager
{
  private readonly ConfigurationOptions _options;
  private readonly IConfigurationStoreFactory _storeFactory;
  private readonly ISecretsProvider _secretsProvider;
  private readonly IConfigurationBackupService _backupService;
  private readonly ILogger<ConfigurationManager> _logger;
  private readonly JsonSerializerOptions _jsonOptions;

  /// <inheritdoc/>
  public IConfigurationBackupService Backup => _backupService;

  /// <inheritdoc/>
  public ConfigurationStoreType CurrentStoreType => _options.DefaultStoreType;

  /// <summary>
  /// Initializes a new instance of the ConfigurationManager class.
  /// </summary>
  public ConfigurationManager(
    IOptions<ConfigurationOptions> options,
    IConfigurationStoreFactory storeFactory,
    ISecretsProvider secretsProvider,
    IConfigurationBackupService backupService,
    ILogger<ConfigurationManager> logger)
  {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(storeFactory);
    ArgumentNullException.ThrowIfNull(secretsProvider);
    ArgumentNullException.ThrowIfNull(backupService);
    ArgumentNullException.ThrowIfNull(logger);

    _options = options.Value;
    _storeFactory = storeFactory;
    _secretsProvider = secretsProvider;
    _backupService = backupService;
    _logger = logger;
    _jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true
    };
  }

  /// <inheritdoc/>
  public async Task<IConfigurationStore> GetStoreAsync(string storeId, CancellationToken ct = default)
  {
    var exists = await _storeFactory.StoreExistsAsync(storeId, CurrentStoreType, ct);
    if (!exists)
    {
      throw new ConfigurationStoreException($"Configuration store '{storeId}' not found.", storeId);
    }

    return await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);
  }

  /// <inheritdoc/>
  public async Task<IConfigurationStore> CreateStoreAsync(string storeId, CancellationToken ct = default)
  {
    var store = await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);
    await store.SaveAsync(ct);
    _logger.LogInformation("Created configuration store: {StoreId}", storeId);
    return store;
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<ConfigurationFile>> ListStoresAsync(CancellationToken ct = default)
  {
    var storeIds = await _storeFactory.ListStoresAsync(CurrentStoreType, ct);
    var files = new List<ConfigurationFile>();

    var basePath = Path.GetFullPath(_options.BasePath);

    foreach (var storeId in storeIds)
    {
      var store = await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);
      var entries = await store.GetAllEntriesAsync(ConfigurationReadMode.Raw, ct);

      string path;
      long sizeBytes;
      DateTimeOffset createdAt;
      DateTimeOffset lastModified;

      if (CurrentStoreType == ConfigurationStoreType.Json)
      {
        var filePath = Path.Combine(basePath, $"{storeId}{_options.JsonExtension}");
        path = filePath;

        if (File.Exists(filePath))
        {
          var fileInfo = new FileInfo(filePath);
          sizeBytes = fileInfo.Length;
          createdAt = fileInfo.CreationTimeUtc;
          lastModified = fileInfo.LastWriteTimeUtc;
        }
        else
        {
          sizeBytes = 0;
          createdAt = DateTimeOffset.UtcNow;
          lastModified = DateTimeOffset.UtcNow;
        }
      }
      else
      {
        var dbPath = Path.Combine(basePath, _options.SqliteFileName);
        path = $"sqlite:{dbPath}:{storeId}";

        if (File.Exists(dbPath))
        {
          var fileInfo = new FileInfo(dbPath);
          sizeBytes = fileInfo.Length;
          createdAt = fileInfo.CreationTimeUtc;
          lastModified = fileInfo.LastWriteTimeUtc;
        }
        else
        {
          sizeBytes = 0;
          createdAt = DateTimeOffset.UtcNow;
          lastModified = DateTimeOffset.UtcNow;
        }
      }

      files.Add(new ConfigurationFile
      {
        StoreId = storeId,
        StoreType = CurrentStoreType,
        Path = path,
        EntryCount = entries.Count,
        SizeBytes = sizeBytes,
        CreatedAt = createdAt,
        LastModifiedAt = lastModified
      });
    }

    return files;
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteStoreAsync(string storeId, CancellationToken ct = default)
  {
    var deleted = await _storeFactory.DeleteStoreAsync(storeId, CurrentStoreType, ct);
    if (deleted)
    {
      _logger.LogInformation("Deleted configuration store: {StoreId}", storeId);
    }
    return deleted;
  }

  /// <inheritdoc/>
  public async Task<T?> GetValueAsync<T>(string storeId, string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    var store = await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);
    var entry = await store.GetEntryAsync(key, mode, ct);

    if (entry == null)
      return default;

    if (typeof(T) == typeof(string))
    {
      return (T)(object)entry.Value;
    }

    try
    {
      return JsonSerializer.Deserialize<T>(entry.Value, _jsonOptions);
    }
    catch (JsonException ex)
    {
      _logger.LogWarning(ex, "Failed to deserialize value for key {Key} as {Type}", key, typeof(T).Name);
      return default;
    }
  }

  /// <inheritdoc/>
  public async Task SetValueAsync<T>(string storeId, string key, T value, CancellationToken ct = default)
  {
    var store = await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);

    string stringValue;
    if (value is string str)
    {
      stringValue = str;
    }
    else
    {
      stringValue = JsonSerializer.Serialize(value, _jsonOptions);
    }

    await store.SetEntryAsync(key, stringValue, ct);
    _logger.LogDebug("Set value for key {Key} in store {StoreId}", key, storeId);
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteValueAsync(string storeId, string key, CancellationToken ct = default)
  {
    var store = await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);
    var deleted = await store.DeleteEntryAsync(key, ct);

    if (deleted)
    {
      _logger.LogDebug("Deleted value for key {Key} from store {StoreId}", key, storeId);
    }

    return deleted;
  }

  /// <inheritdoc/>
  public async Task<string> CreateSecretAsync(string storeId, string key, string secretValue, CancellationToken ct = default)
  {
    // Generate tag with hint from key
    var hint = key.Replace(":", "_").Replace(".", "_");
    var tagIdentifier = _secretsProvider.GenerateTag(hint);

    // Store the secret
    await _secretsProvider.SetSecretAsync(tagIdentifier, secretValue, ct);

    // Create the tag reference
    var tagReference = $"{SecretTag.TagPrefix}{tagIdentifier}{SecretTag.TagSuffix}";

    // Store the tag reference in the configuration store
    var store = await _storeFactory.CreateStoreAsync(storeId, CurrentStoreType, ct);
    await store.SetEntryAsync(key, tagReference, ct);

    _logger.LogInformation("Created secret for key {Key} in store {StoreId} with tag {Tag}", key, storeId, tagIdentifier);
    return tagReference;
  }

  /// <inheritdoc/>
  public async Task<bool> UpdateSecretAsync(string tag, string newValue, CancellationToken ct = default)
  {
    // Check if the secret exists
    var existingValue = await _secretsProvider.GetSecretAsync(tag, ct);
    if (existingValue == null)
    {
      _logger.LogWarning("Secret with tag {Tag} not found", tag);
      return false;
    }

    // Update the secret
    await _secretsProvider.SetSecretAsync(tag, newValue, ct);
    _logger.LogInformation("Updated secret with tag {Tag}", tag);
    return true;
  }
}
