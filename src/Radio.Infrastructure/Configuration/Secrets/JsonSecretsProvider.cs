namespace Radio.Infrastructure.Configuration.Secrets;

using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Infrastructure.Configuration.Models;

/// <summary>
/// Stores secrets in an encrypted JSON file.
/// </summary>
public sealed class JsonSecretsProvider : SecretsProviderBase, IDisposable
{
  private readonly string _filePath;
  private readonly ILogger<JsonSecretsProvider> _logger;
  private readonly SemaphoreSlim _lock = new(1, 1);
  private readonly JsonSerializerOptions _jsonOptions;

  private Dictionary<string, string> _secrets = new();
  private bool _isLoaded;
  private bool _disposed;

  /// <summary>
  /// Initializes a new instance of the JsonSecretsProvider class.
  /// </summary>
  public JsonSecretsProvider(
    IOptions<ConfigurationOptions> options,
    IDataProtectionProvider dataProtection,
    ILogger<JsonSecretsProvider> logger)
    : base(dataProtection, logger)
  {
    ArgumentNullException.ThrowIfNull(options);

    var opts = options.Value;
    var basePath = Path.GetFullPath(opts.BasePath);
    _filePath = Path.Combine(basePath, $"{opts.SecretsFileName}.json");

    _logger = logger;
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
  }

  /// <inheritdoc/>
  public override async Task<string?> GetSecretAsync(string tag, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      if (_secrets.TryGetValue(tag, out var encrypted))
      {
        return Decrypt(encrypted);
      }
      return null;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public override async Task<string> SetSecretAsync(string tag, string value, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      var encrypted = Encrypt(value);
      _secrets[tag] = encrypted;
      await SaveAsync(ct);
      _logger.LogInformation("Secret stored with tag: {Tag}", tag);
      return tag;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc/>
  public override async Task<bool> DeleteSecretAsync(string tag, CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      if (_secrets.Remove(tag))
      {
        await SaveAsync(ct);
        _logger.LogInformation("Secret deleted: {Tag}", tag);
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
  public override async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default)
  {
    await EnsureLoadedAsync(ct);
    await _lock.WaitAsync(ct);
    try
    {
      return _secrets.Keys.ToList();
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
      _secrets = new Dictionary<string, string>();
      return;
    }

    try
    {
      var json = await File.ReadAllTextAsync(_filePath, ct);
      var data = JsonSerializer.Deserialize<SecretsFile>(json, _jsonOptions);
      _secrets = data?.Secrets ?? new Dictionary<string, string>();
      _logger.LogDebug("Loaded {Count} secrets from {Path}", _secrets.Count, _filePath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load secrets file: {Path}", _filePath);
      _secrets = new Dictionary<string, string>();
    }
  }

  private async Task SaveAsync(CancellationToken ct)
  {
    var directory = Path.GetDirectoryName(_filePath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var data = new SecretsFile
    {
      Version = 1,
      LastModified = DateTimeOffset.UtcNow,
      Secrets = _secrets
    };

    // Atomic write using temp file
    var tempPath = _filePath + ".tmp";
    var json = JsonSerializer.Serialize(data, _jsonOptions);
    await File.WriteAllTextAsync(tempPath, json, ct);
    File.Move(tempPath, _filePath, overwrite: true);
    _logger.LogDebug("Saved {Count} secrets to {Path}", _secrets.Count, _filePath);
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    if (_disposed) return;
    _lock.Dispose();
    _disposed = true;
  }

  private sealed record SecretsFile
  {
    public int Version { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public Dictionary<string, string> Secrets { get; init; } = new();
  }
}
