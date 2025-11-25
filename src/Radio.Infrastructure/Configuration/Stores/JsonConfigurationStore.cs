namespace Radio.Infrastructure.Configuration.Stores;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// JSON file-based configuration store implementation.
/// </summary>
public sealed class JsonConfigurationStore : IConfigurationStore, IDisposable
{
  private readonly string _filePath;
  private readonly ISecretsProvider _secretsProvider;
  private readonly ILogger<JsonConfigurationStore> _logger;
  private readonly bool _autoSave;
  private readonly SemaphoreSlim _lock = new(1, 1);
  private readonly JsonSerializerOptions _jsonOptions;

  private Dictionary<string, StoredEntry> _entries = new();
  private bool _isLoaded;
  private bool _isDirty;
  private bool _disposed;

  /// <inheritdoc/>
  public string StoreId { get; }

  /// <inheritdoc/>
  public ConfigurationStoreType StoreType => ConfigurationStoreType.Json;

  /// <summary>
  /// Initializes a new instance of the JsonConfigurationStore class.
  /// </summary>
  public JsonConfigurationStore(
    string storeId,
    string filePath,
    ISecretsProvider secretsProvider,
    ILogger<JsonConfigurationStore> logger,
    bool autoSave = true)
  {
    StoreId = storeId ?? throw new ArgumentNullException(nameof(storeId));
    _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    _secretsProvider = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _autoSave = autoSave;
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
  }

  /// <inheritdoc/>
  public async Task<ConfigurationEntry?> GetEntryAsync(string key, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      if (!_entries.TryGetValue(key, out var stored))
        return null;

      return await CreateEntryAsync(key, stored, mode, ct);
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<ConfigurationEntry>> GetAllEntriesAsync(ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      var entries = new List<ConfigurationEntry>();
      foreach (var kvp in _entries)
      {
        entries.Add(await CreateEntryAsync(kvp.Key, kvp.Value, mode, ct));
      }
      return entries;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<IReadOnlyList<ConfigurationEntry>> GetEntriesBySectionAsync(string sectionPrefix, ConfigurationReadMode mode = ConfigurationReadMode.Resolved, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      var prefix = sectionPrefix.EndsWith(':') ? sectionPrefix : $"{sectionPrefix}:";
      var entries = new List<ConfigurationEntry>();

      foreach (var kvp in _entries.Where(e => e.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
      {
        entries.Add(await CreateEntryAsync(kvp.Key, kvp.Value, mode, ct));
      }
      return entries;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task SetEntryAsync(string key, string value, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      _entries[key] = new StoredEntry
      {
        Value = value,
        LastModified = DateTimeOffset.UtcNow
      };
      _isDirty = true;

      if (_autoSave)
      {
        await SaveInternalAsync(ct);
      }
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task SetEntriesAsync(IEnumerable<ConfigurationEntry> entries, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      foreach (var entry in entries)
      {
        _entries[entry.Key] = new StoredEntry
        {
          Value = entry.RawValue ?? entry.Value,
          Description = entry.Description,
          LastModified = DateTimeOffset.UtcNow
        };
      }
      _isDirty = true;

      if (_autoSave)
      {
        await SaveInternalAsync(ct);
      }
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<bool> DeleteEntryAsync(string key, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      if (_entries.Remove(key))
      {
        _isDirty = true;
        if (_autoSave)
        {
          await SaveInternalAsync(ct);
        }
        return true;
      }
      return false;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      return _entries.ContainsKey(key);
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<bool> SaveAsync(CancellationToken ct = default)
  {
    await _lock.WaitAsync(ct);
    try
    {
      if (!_isDirty)
        return true;

      await SaveInternalAsync(ct);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save configuration store: {StoreId}", StoreId);
      return false;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task ReloadAsync(CancellationToken ct = default)
  {
    await _lock.WaitAsync(ct);
    try
    {
      await LoadAsync(ct);
      _isDirty = false;
    }
    finally
    {
      _lock.Release();
    }
  }

  private async Task EnsureLoadedAsync(CancellationToken ct)
  {
    if (_isLoaded) return;

    await _lock.WaitAsync(ct);
    try
    {
      if (_isLoaded) return;
      await LoadAsync(ct);
      _isLoaded = true;
    }
    finally
    {
      _lock.Release();
    }
  }

  private async Task LoadAsync(CancellationToken ct)
  {
    if (!File.Exists(_filePath))
    {
      _entries = new Dictionary<string, StoredEntry>();
      _logger.LogDebug("Configuration file not found, starting with empty store: {Path}", _filePath);
      return;
    }

    try
    {
      var json = await File.ReadAllTextAsync(_filePath, ct);
      var data = JsonSerializer.Deserialize<StoreFile>(json, _jsonOptions);
      _entries = data?.Entries ?? new Dictionary<string, StoredEntry>();
      _logger.LogDebug("Loaded {Count} entries from {Path}", _entries.Count, _filePath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load configuration file: {Path}", _filePath);
      _entries = new Dictionary<string, StoredEntry>();
    }
  }

  private async Task SaveInternalAsync(CancellationToken ct)
  {
    var directory = Path.GetDirectoryName(_filePath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var data = new StoreFile
    {
      Version = 1,
      LastModified = DateTimeOffset.UtcNow,
      Entries = _entries
    };

    // Atomic write using temp file
    var tempPath = _filePath + ".tmp";
    var json = JsonSerializer.Serialize(data, _jsonOptions);
    await File.WriteAllTextAsync(tempPath, json, ct);
    File.Move(tempPath, _filePath, overwrite: true);

    _isDirty = false;
    _logger.LogDebug("Saved {Count} entries to {Path}", _entries.Count, _filePath);
  }

  private async Task<ConfigurationEntry> CreateEntryAsync(string key, StoredEntry stored, ConfigurationReadMode mode, CancellationToken ct)
  {
    var rawValue = stored.Value;
    var containsSecret = _secretsProvider.ContainsSecretTag(rawValue);

    string resolvedValue;
    if (mode == ConfigurationReadMode.Resolved && containsSecret)
    {
      resolvedValue = await _secretsProvider.ResolveTagsAsync(rawValue, ct);
    }
    else
    {
      resolvedValue = rawValue;
    }

    return new ConfigurationEntry
    {
      Key = key,
      Value = resolvedValue,
      RawValue = containsSecret ? rawValue : null,
      ContainsSecret = containsSecret,
      LastModified = stored.LastModified,
      Description = stored.Description
    };
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed) return;
    _lock.Dispose();
    _disposed = true;
  }

  private sealed record StoreFile
  {
    public int Version { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public Dictionary<string, StoredEntry> Entries { get; init; } = new();
  }

  private sealed record StoredEntry
  {
    public required string Value { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? LastModified { get; init; }
  }
}
