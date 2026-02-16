using Radio.Core.Models.Audio;

namespace Radio.Core.Events;

/// <summary>
/// Event arguments for when a song change is detected via fingerprinting.
/// Raised when the currently-playing track differs from the previously-identified track.
/// </summary>
public class SongChangedEventArgs : EventArgs
{
  /// <summary>
  /// Initializes a new instance of the <see cref="SongChangedEventArgs"/> class.
  /// </summary>
  /// <param name="previousTrack">The previously-identified track (null if first identification).</param>
  /// <param name="newTrack">The newly-identified track.</param>
  /// <param name="confidence">The confidence of the new identification.</param>
  public SongChangedEventArgs(TrackMetadata? previousTrack, TrackMetadata newTrack, double confidence)
  {
    PreviousTrack = previousTrack;
    NewTrack = newTrack ?? throw new ArgumentNullException(nameof(newTrack));
    Confidence = confidence;
    DetectedAt = DateTime.UtcNow;
  }

  /// <summary>Gets the previously-identified track (null if this is the first identification).</summary>
  public TrackMetadata? PreviousTrack { get; }

  /// <summary>Gets the newly-identified track.</summary>
  public TrackMetadata NewTrack { get; }

  /// <summary>Gets the confidence of the new identification (0.0 to 1.0).</summary>
  public double Confidence { get; }

  /// <summary>Gets when the song change was detected.</summary>
  public DateTime DetectedAt { get; }
}
