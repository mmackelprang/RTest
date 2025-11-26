namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the audio system.
/// Loaded from the 'Audio' configuration section.
/// </summary>
public class AudioOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Audio";

  /// <summary>
  /// Gets or sets the default primary audio source name.
  /// </summary>
  public string DefaultSource { get; set; } = "Spotify";

  /// <summary>
  /// Gets or sets the volume percentage when primary source is ducked (0-100).
  /// </summary>
  public int DuckingPercentage { get; set; } = 20;

  /// <summary>
  /// Gets or sets the ducking transition policy.
  /// </summary>
  public DuckingPolicy DuckingPolicy { get; set; } = DuckingPolicy.FadeSmooth;

  /// <summary>
  /// Gets or sets the ducking attack time in milliseconds.
  /// </summary>
  public int DuckingAttackMs { get; set; } = 100;

  /// <summary>
  /// Gets or sets the ducking release time in milliseconds.
  /// </summary>
  public int DuckingReleaseMs { get; set; } = 500;
}

/// <summary>
/// Ducking transition policies for audio priority.
/// </summary>
public enum DuckingPolicy
{
  /// <summary>Smooth fade transition.</summary>
  FadeSmooth,

  /// <summary>Quick fade transition.</summary>
  FadeQuick,

  /// <summary>Instant volume change.</summary>
  Instant
}
