namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents fingerprint data generated from an audio sample.
/// </summary>
public sealed record FingerprintData
{
  /// <summary>Gets the unique identifier for this fingerprint.</summary>
  public required string Id { get; init; }

  /// <summary>Gets the base64-encoded Chromaprint hash.</summary>
  public required string ChromaprintHash { get; init; }

  /// <summary>Gets the duration of the audio sample in seconds.</summary>
  public required int DurationSeconds { get; init; }

  /// <summary>Gets when this fingerprint was generated.</summary>
  public required DateTime GeneratedAt { get; init; }

  /// <summary>Gets the source path or name of the audio (optional).</summary>
  public string? SourcePath { get; init; }
}
