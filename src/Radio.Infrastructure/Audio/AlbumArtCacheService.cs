using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Radio.Infrastructure.Audio;

/// <summary>
/// Caches album art images on disk with content-addressed filenames and a 7-day TTL.
/// Provides HTTP-serveable paths for use by Cast devices and the Web UI.
/// Storage: ./data/albumart/
/// </summary>
public sealed class AlbumArtCacheService : IDisposable
{
  private readonly ILogger<AlbumArtCacheService> _logger;
  private readonly string _cacheDir;
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private readonly Timer _cleanupTimer;
  private readonly HttpClient _httpClient;
  private bool _disposed;

  private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
  private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

  public AlbumArtCacheService(ILogger<AlbumArtCacheService> logger)
  {
    _logger = logger;
    _cacheDir = Path.Combine(".", "data", "albumart");
    Directory.CreateDirectory(_cacheDir);

    _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RadioConsole/1.0");

    // Run cleanup on a timer, starting after 1 minute
    _cleanupTimer = new Timer(_ => CleanupExpired(), null, TimeSpan.FromMinutes(1), CleanupInterval);

    _logger.LogInformation("Album art cache initialized at {CacheDir}", _cacheDir);
  }

  /// <summary>
  /// Saves image bytes to the cache. Returns the relative URL path (/api/albumart/{hash}.{ext}).
  /// Content-addressed: identical images produce the same filename (dedup).
  /// </summary>
  public async Task<string> SaveAsync(byte[] imageData, string mimeType)
  {
    var ext = GetExtensionFromMime(mimeType);
    var hash = ComputeHash(imageData);
    var filename = $"{hash}.{ext}";
    var filePath = Path.Combine(_cacheDir, filename);

    await _writeLock.WaitAsync();
    try
    {
      if (File.Exists(filePath))
      {
        // Dedup: refresh TTL by touching last write time
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        _logger.LogDebug("Album art cache hit (refreshed TTL): {Filename}", filename);
      }
      else
      {
        await File.WriteAllBytesAsync(filePath, imageData);
        _logger.LogDebug("Album art cached: {Filename} ({Bytes} bytes)", filename, imageData.Length);
      }
    }
    finally
    {
      _writeLock.Release();
    }

    return $"/api/albumart/{filename}";
  }

  /// <summary>
  /// Saves image bytes to the cache synchronously. Returns the relative URL path (/api/albumart/{hash}.{ext}).
  /// Content-addressed: identical images produce the same filename (dedup).
  /// </summary>
  public string Save(byte[] imageData, string mimeType)
  {
    var ext = GetExtensionFromMime(mimeType);
    var hash = ComputeHash(imageData);
    var filename = $"{hash}.{ext}";
    var filePath = Path.Combine(_cacheDir, filename);

    _writeLock.Wait();
    try
    {
      if (File.Exists(filePath))
      {
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        _logger.LogDebug("Album art cache hit (refreshed TTL): {Filename}", filename);
      }
      else
      {
        File.WriteAllBytes(filePath, imageData);
        _logger.LogDebug("Album art cached: {Filename} ({Bytes} bytes)", filename, imageData.Length);
      }
    }
    finally
    {
      _writeLock.Release();
    }

    return $"/api/albumart/{filename}";
  }

  /// <summary>
  /// Downloads an image from a URL and caches it. Returns the relative URL path or null on failure.
  /// </summary>
  public async Task<string?> SaveFromUrlAsync(string url)
  {
    try
    {
      using var response = await _httpClient.GetAsync(url);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogDebug("Failed to download album art from {Url}: HTTP {StatusCode}", url, (int)response.StatusCode);
        return null;
      }

      var bytes = await response.Content.ReadAsByteArrayAsync();
      if (bytes.Length == 0)
        return null;

      // Detect MIME from Content-Type header or magic bytes
      var contentType = response.Content.Headers.ContentType?.MediaType;
      var mime = contentType ?? DetectMimeFromBytes(bytes) ?? "image/jpeg";

      return await SaveAsync(bytes, mime);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to download and cache album art from {Url}", url);
      return null;
    }
  }

  /// <summary>
  /// Gets the full file path for a cached filename, or null if not found.
  /// </summary>
  public string? GetFilePath(string filename)
  {
    // Sanitize: only allow alphanumeric, dash, underscore, dot
    if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
      return null;

    // Prevent path traversal
    if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
      return null;

    var filePath = Path.Combine(_cacheDir, filename);
    return File.Exists(filePath) ? filePath : null;
  }

  /// <summary>
  /// Removes cached files older than the TTL (7 days).
  /// </summary>
  public void CleanupExpired()
  {
    try
    {
      if (!Directory.Exists(_cacheDir))
        return;

      var cutoff = DateTime.UtcNow - Ttl;
      var removed = 0;

      foreach (var file in Directory.EnumerateFiles(_cacheDir))
      {
        var info = new FileInfo(file);
        if (info.LastWriteTimeUtc < cutoff)
        {
          try
          {
            info.Delete();
            removed++;
          }
          catch (Exception ex)
          {
            _logger.LogDebug(ex, "Failed to delete expired album art: {File}", file);
          }
        }
      }

      if (removed > 0)
      {
        _logger.LogInformation("Album art cache cleanup: removed {Count} expired files", removed);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error during album art cache cleanup");
    }
  }

  private static string ComputeHash(byte[] data)
  {
    var hashBytes = SHA256.HashData(data);
    // First 16 hex chars (8 bytes) — enough for dedup, short filenames
    return Convert.ToHexString(hashBytes, 0, 8).ToLowerInvariant();
  }

  /// <summary>
  /// Detects MIME type from magic bytes (PNG vs JPEG).
  /// </summary>
  internal static string? DetectMimeFromBytes(byte[] data)
  {
    if (data.Length < 4)
      return null;

    // PNG: 0x89 0x50 0x4E 0x47
    if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
      return "image/png";

    // JPEG: 0xFF 0xD8
    if (data[0] == 0xFF && data[1] == 0xD8)
      return "image/jpeg";

    // WebP: RIFF....WEBP
    if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
        && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
      return "image/webp";

    return null;
  }

  private static string GetExtensionFromMime(string mimeType)
  {
    return mimeType switch
    {
      "image/png" => "png",
      "image/webp" => "webp",
      _ => "jpg" // Default to JPEG
    };
  }

  public static string GetContentTypeFromExtension(string extension)
  {
    return extension.ToLowerInvariant() switch
    {
      "png" => "image/png",
      "webp" => "image/webp",
      "jpg" or "jpeg" => "image/jpeg",
      _ => "application/octet-stream"
    };
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _cleanupTimer.Dispose();
    _httpClient.Dispose();
    _writeLock.Dispose();
  }
}
