using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for looking up track metadata and cover art.
/// </summary>
public interface IMetadataLookupService
{
  /// <summary>
  /// Searches for cover art by track title, artist, and optional album name
  /// using MusicBrainz text search and the Cover Art Archive.
  /// </summary>
  /// <param name="title">The track title.</param>
  /// <param name="artist">The artist name.</param>
  /// <param name="album">Optional album name for more precise matching.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The cover art URL, or null if not found.</returns>
  Task<string?> SearchCoverArtByTextAsync(
    string title, string artist, string? album = null,
    CancellationToken ct = default);

  /// <summary>
  /// Gets cover art directly from the Cover Art Archive by MusicBrainz release ID.
  /// Faster than text search when the release ID is already known (e.g., from fingerprinting).
  /// </summary>
  /// <param name="releaseId">The MusicBrainz release ID.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The cover art URL, or null if not found.</returns>
  Task<string?> GetCoverArtByReleaseIdAsync(
    string releaseId,
    CancellationToken ct = default);
}
