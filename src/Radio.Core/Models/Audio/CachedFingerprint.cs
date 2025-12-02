namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents a fingerprint stored in the local cache with associated metadata.
/// </summary>
public sealed record CachedFingerprint
{
  /// <summary>Gets the unique identifier for this cached fingerprint.</summary>
  public required string Id { get; init; }

  /// <summary>Gets the base64-encoded Chromaprint hash.</summary>
  public required string ChromaprintHash { get; init; }

  /// <summary>Gets the duration of the audio in seconds.</summary>
  public required int DurationSeconds { get; init; }

  /// <summary>Gets the AcoustID identifier if matched (optional).</summary>
  public string? AcoustId { get; init; }

  /// <summary>Gets the MusicBrainz recording ID if matched (optional).</summary>
  public string? MusicBrainzRecordingId { get; init; }

  /// <summary>Gets when this fingerprint was first created.</summary>
  public required DateTime CreatedAt { get; init; }

  /// <summary>Gets when this fingerprint was last matched (optional).</summary>
  public DateTime? LastMatchedAt { get; init; }

  /// <summary>Gets the number of times this fingerprint has been matched.</summary>
  public int MatchCount { get; init; }

  /// <summary>Gets the associated track metadata if available (optional).</summary>
  public TrackMetadata? Metadata { get; init; }
}
