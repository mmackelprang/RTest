using Radio.Core.Models.Audio;

namespace Radio.Core.Events;

/// <summary>
/// Event arguments for when a track is identified via fingerprinting.
/// </summary>
public class TrackIdentifiedEventArgs : EventArgs
{
  /// <summary>
  /// Initializes a new instance of the <see cref="TrackIdentifiedEventArgs"/> class.
  /// </summary>
  /// <param name="track">The identified track metadata.</param>
  /// <param name="confidence">The confidence level of the identification.</param>
  /// <param name="captureStartedAt">When (UTC) the audio sample behind this identification began to be captured, if known.</param>
  public TrackIdentifiedEventArgs(TrackMetadata track, double confidence, DateTime? captureStartedAt = null)
  {
    Track = track ?? throw new ArgumentNullException(nameof(track));
    Confidence = confidence;
    CaptureStartedAt = captureStartedAt;
    IdentifiedAt = DateTime.UtcNow;
  }

  /// <summary>Gets the identified track metadata.</summary>
  public TrackMetadata Track { get; }

  /// <summary>Gets the confidence level of the identification (0.0 to 1.0).</summary>
  public double Confidence { get; }

  /// <summary>Gets when the track was identified.</summary>
  public DateTime IdentifiedAt { get; }

  /// <summary>
  /// Gets when (UTC) capture of the audio sample behind this identification was started, or <c>null</c>
  /// when the raiser did not record it.
  /// </summary>
  /// <remarks>
  /// AUD-33: capture plus recognition takes ~15 s, so a result can arrive after the listener skipped to
  /// another track. Sources compare this against when their current track started (see
  /// <see cref="WasCapturedBefore"/>) and drop a result that describes the previous track.
  /// ⚠ The boundary is approximate at sub-second scale: the tap starts reading a fraction of a second
  /// behind the live write position and skips silent chunks. Harmless against ~15 s samples.
  /// </remarks>
  public DateTime? CaptureStartedAt { get; }

  /// <summary>
  /// True when this identification's sample began capturing strictly before <paramref name="instantUtc"/>
  /// — i.e. it was (at least partly) sampled from whatever was playing before then. False when either
  /// time is unknown: an identification with no capture time is never treated as stale.
  /// </summary>
  public bool WasCapturedBefore(DateTime? instantUtc) =>
    CaptureStartedAt.HasValue && instantUtc.HasValue && CaptureStartedAt.Value < instantUtc.Value;
}
