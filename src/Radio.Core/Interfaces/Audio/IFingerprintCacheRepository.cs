using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Repository for fingerprint cache operations.
/// </summary>
public interface IFingerprintCacheRepository
{
  /// <summary>
  /// Finds a cached fingerprint by its Chromaprint hash.
  /// </summary>
  /// <param name="chromaprintHash">The Chromaprint hash to search for.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The cached fingerprint, or null if not found.</returns>
  Task<CachedFingerprint?> FindByHashAsync(
    string chromaprintHash,
    CancellationToken ct = default);

  /// <summary>
  /// Stores a fingerprint in the cache with optional metadata.
  /// </summary>
  /// <param name="fingerprint">The fingerprint data to store.</param>
  /// <param name="metadata">Optional track metadata to associate.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The cached fingerprint record.</returns>
  Task<CachedFingerprint> StoreAsync(
    FingerprintData fingerprint,
    TrackMetadata? metadata,
    CancellationToken ct = default);

  /// <summary>
  /// Updates the last matched timestamp and increments the match count.
  /// </summary>
  /// <param name="id">The fingerprint ID.</param>
  /// <param name="ct">Cancellation token.</param>
  Task UpdateLastMatchedAsync(
    string id,
    CancellationToken ct = default);

  /// <summary>
  /// Gets the total count of cached fingerprints.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The count of cached fingerprints.</returns>
  Task<int> GetCacheCountAsync(CancellationToken ct = default);

  /// <summary>
  /// Gets all cached fingerprints with optional pagination.
  /// </summary>
  /// <param name="page">The page number (1-based).</param>
  /// <param name="pageSize">The page size.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of cached fingerprints.</returns>
  Task<IReadOnlyList<CachedFingerprint>> GetAllAsync(
    int page = 1,
    int pageSize = 50,
    CancellationToken ct = default);

  /// <summary>
  /// Deletes a cached fingerprint by ID.
  /// </summary>
  /// <param name="id">The fingerprint ID to delete.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if deleted, false if not found.</returns>
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
