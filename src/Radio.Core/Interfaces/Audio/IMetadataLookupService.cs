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
  /// <returns>The lookup result, or null if no match found.</returns>
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
}
