namespace Radio.API.Models;

/// <summary>
/// Represents volume information for audio playback.
/// </summary>
public class VolumeDto
{
  /// <summary>
  /// Gets or sets the master volume (0.0 to 1.0).
  /// </summary>
  public float Volume { get; set; }

  /// <summary>
  /// Gets or sets whether audio is muted.
  /// </summary>
  public bool IsMuted { get; set; }

  /// <summary>
  /// Gets or sets the stereo balance (-1.0 left to 1.0 right).
  /// </summary>
  public float Balance { get; set; }
}
