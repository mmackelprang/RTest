using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for looking up track metadata from fingerprints.
/// </summary>
public interface IMetadataLookupService
{
  /// <summary>
  /// Looks up metadata for a fingerprint.
  /// Checks local cache first, then external services (AcoustID, MusicBrainz).
  /// </summary>
  /// <param name="fingerprint">The fingerprint to look up.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The lookup result containing match status and the valid fingerprint ID.</returns>
  Task<MetadataLookupResult?> LookupAsync(
    FingerprintData fingerprint,
    CancellationToken ct = default);

  /// <summary>
  /// Gets track metadata from MusicBrainz by recording ID.
  /// </summary>
  /// <param name="recordingId">The MusicBrainz recording ID.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The track metadata, or null if not found.</returns>
  Task<TrackMetadata?> GetMusicBrainzMetadataAsync(
    string recordingId,
    CancellationToken ct = default);

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
}
