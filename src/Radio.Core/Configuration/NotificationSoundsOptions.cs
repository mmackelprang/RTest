namespace Radio.Core.Configuration;

/// <summary>
/// Where the notification sounds live. Loaded from the 'NotificationSounds' configuration section.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="FilePlayerOptions.RootDirectory"/>. On the appliance that
/// root is the NAS music library, and touching a NAS that cannot be reached blocks for seconds.
/// </remarks>
public class NotificationSoundsOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "NotificationSounds";

  /// <summary>
  /// Gets or sets the directory holding the sounds. A relative path resolves against the process's
  /// working directory.
  /// </summary>
  public string Directory { get; set; } = "media/sounds";
}
