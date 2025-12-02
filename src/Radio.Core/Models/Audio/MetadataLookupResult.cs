namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents the result of a metadata lookup for an audio fingerprint.
/// </summary>
public sealed record MetadataLookupResult
{
  /// <summary>Gets whether a match was found.</summary>
  public required bool IsMatch { get; init; }

  /// <summary>Gets the confidence level of the match (0.0 to 1.0).</summary>
  public required double Confidence { get; init; }

  /// <summary>Gets the AcoustID identifier (optional).</summary>
  public string? AcoustId { get; init; }

  /// <summary>Gets the MusicBrainz recording ID (optional).</summary>
  public string? MusicBrainzRecordingId { get; init; }

  /// <summary>Gets the track metadata if found (optional).</summary>
  public TrackMetadata? Metadata { get; init; }

  /// <summary>Gets the source of the lookup result.</summary>
  public required LookupSource Source { get; init; }
}

/// <summary>
/// Specifies the source of a metadata lookup result.
/// </summary>
public enum LookupSource
{
  /// <summary>Result came from local cache.</summary>
  Cache,

  /// <summary>Result came from AcoustID API.</summary>
  AcoustID,

  /// <summary>Metadata was manually assigned.</summary>
  Manual
}
