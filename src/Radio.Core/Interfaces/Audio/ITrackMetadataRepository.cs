using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Repository for track metadata operations.
/// </summary>
public interface ITrackMetadataRepository
{
  /// <summary>
  /// Gets track metadata by ID.
  /// </summary>
  /// <param name="id">The metadata ID.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The track metadata, or null if not found.</returns>
  Task<TrackMetadata?> GetByIdAsync(string id, CancellationToken ct = default);

  /// <summary>
  /// Gets track metadata by fingerprint ID.
  /// </summary>
  /// <param name="fingerprintId">The fingerprint ID.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The track metadata, or null if not found.</returns>
  Task<TrackMetadata?> GetByFingerprintIdAsync(
    string fingerprintId,
    CancellationToken ct = default);

  /// <summary>
  /// Stores or updates track metadata.
  /// </summary>
  /// <param name="metadata">The metadata to store.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The stored metadata.</returns>
  Task<TrackMetadata> StoreAsync(
    TrackMetadata metadata,
    CancellationToken ct = default);

  /// <summary>
  /// Searches for tracks by title, artist, or album.
  /// </summary>
  /// <param name="query">The search query.</param>
  /// <param name="limit">Maximum number of results.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of matching tracks.</returns>
  Task<IReadOnlyList<TrackMetadata>> SearchAsync(
    string query,
    int limit = 20,
    CancellationToken ct = default);
}
