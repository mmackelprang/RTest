namespace Radio.Core.Interfaces;

/// <summary>
/// Service for caching album art images on disk.
/// Content-addressed: identical images produce the same filename (dedup).
/// </summary>
public interface IAlbumArtCacheService
{
  /// <summary>
  /// Saves image bytes to the cache. Returns the relative URL path (/api/albumart/{hash}.{ext}).
  /// </summary>
  Task<string> SaveAsync(byte[] imageData, string mimeType);

  /// <summary>
  /// Saves image bytes to the cache synchronously. Returns the relative URL path.
  /// </summary>
  string Save(byte[] imageData, string mimeType);

  /// <summary>
  /// Downloads an image from a URL and caches it. Returns the relative URL path or null on failure.
  /// </summary>
  Task<string?> SaveFromUrlAsync(string url);

  /// <summary>
  /// Gets the full file path for a cached filename, or null if not found.
  /// </summary>
  string? GetFilePath(string filename);

  /// <summary>
  /// Removes cached files older than the TTL.
  /// </summary>
  void CleanupExpired();
}
