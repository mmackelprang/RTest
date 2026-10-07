namespace Radio.Core.Models.Audio;

/// <summary>How one SongRec (Shazam) recognition attempt ended.</summary>
public enum SongRecOutcome
{
  /// <summary>SongRec ran cleanly and Shazam named a track.</summary>
  Match,

  /// <summary>SongRec ran cleanly and Shazam did not recognise the audio.</summary>
  NoMatch,

  /// <summary>
  /// The attempt failed: the process could not be started, timed out, exited non-zero, or printed output
  /// that could not be parsed. Not a statement about the audio — the caller backs off on these.
  /// </summary>
  Error
}

/// <summary>
/// The result of one SongRec recognition attempt. Separates a clean "no match" from a failure, which the
/// call policy treats differently (a failure backs off; a no-match does not).
/// </summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Track">The recognised track; non-null only when <paramref name="Outcome"/> is <see cref="SongRecOutcome.Match"/>.</param>
/// <param name="Error">A short description of the failure; set only when <paramref name="Outcome"/> is <see cref="SongRecOutcome.Error"/>.</param>
public sealed record SongRecRecognitionResult(SongRecOutcome Outcome, TrackMetadata? Track = null, string? Error = null)
{
  /// <summary>A clean no-match.</summary>
  public static SongRecRecognitionResult NoMatch { get; } = new(SongRecOutcome.NoMatch);

  /// <summary>A match for <paramref name="track"/>.</summary>
  public static SongRecRecognitionResult Matched(TrackMetadata track) => new(SongRecOutcome.Match, track);

  /// <summary>A failed attempt, described by <paramref name="error"/>.</summary>
  public static SongRecRecognitionResult Failed(string error) => new(SongRecOutcome.Error, null, error);
}
