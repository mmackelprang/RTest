namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents an entry in the play history.
/// </summary>
public sealed record PlayHistoryEntry
{
  /// <summary>Gets the unique identifier for this history entry.</summary>
  public required string Id { get; init; }

  /// <summary>Gets the track metadata ID if identified (optional).</summary>
  public string? TrackMetadataId { get; init; }

  /// <summary>Gets the fingerprint ID (optional).</summary>
  public string? FingerprintId { get; init; }

  /// <summary>Gets the associated track metadata (navigation property, not stored).</summary>
  public TrackMetadata? Track { get; init; }

  /// <summary>Gets when this track was played.</summary>
  public required DateTime PlayedAt { get; init; }

  /// <summary>Gets when this track stopped playing (set by song change detection).</summary>
  public DateTime? EndedAt { get; init; }

  /// <summary>Gets the audio source type.</summary>
  public required PlaySource Source { get; init; }

  /// <summary>
  /// Gets where the entry's TITLE came from (FileTag, Avrcp, Fingerprinting, etc.).
  /// </summary>
  /// <remarks>
  /// Since AUD-19 this is the title's provenance only. An identification that landed but whose
  /// title lost to the source's (the per-field rule) leaves it at the source's value. Whether an
  /// identification landed is <see cref="IdentificationConfidence"/>, which PlayHistoryTracker
  /// sets only from one — not <see cref="WasIdentified"/>, which is also true for a row that has
  /// real AVRCP or tag metadata and was never fingerprinted. Rows written before AUD-19 say Fingerprinting for
  /// any identification, whatever the title's origin. Other fields can have a different
  /// provenance from the title's — an AVRCP title with fingerprint-filled cover art is normal.
  /// </remarks>
  public MetadataSource? MetadataSource { get; init; }

  /// <summary>Gets additional source details (e.g., station name, file path).</summary>
  public string? SourceDetails { get; init; }

  /// <summary>Gets how long the track played in seconds (optional).</summary>
  public int? DurationSeconds { get; init; }

  /// <summary>Gets the identification confidence (0.0 to 1.0, optional).</summary>
  public double? IdentificationConfidence { get; init; }

  /// <summary>Gets whether this track was successfully identified.</summary>
  public required bool WasIdentified { get; init; }
}

/// <summary>
/// Specifies the audio source type for play history.
/// </summary>
public enum PlaySource
{
  /// <summary>Vinyl turntable source.</summary>
  Vinyl,

  /// <summary>Radio stream source.</summary>
  Radio,

  /// <summary>Local audio file source.</summary>
  File,

  /// <summary>Generic USB audio source.</summary>
  GenericUSB,

  /// <summary>Bluetooth audio source.</summary>
  Bluetooth
}
