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
  public TrackIdentifiedEventArgs(TrackMetadata track, double confidence)
  {
    Track = track ?? throw new ArgumentNullException(nameof(track));
    Confidence = confidence;
    IdentifiedAt = DateTime.UtcNow;
  }

  /// <summary>Gets the identified track metadata.</summary>
  public TrackMetadata Track { get; }

  /// <summary>Gets the confidence level of the identification (0.0 to 1.0).</summary>
  public double Confidence { get; }

  /// <summary>Gets when the track was identified.</summary>
  public DateTime IdentifiedAt { get; }
}
