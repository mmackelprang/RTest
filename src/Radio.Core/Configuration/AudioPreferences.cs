using Radio.Core.Models.Audio;

namespace Radio.Core.Configuration;

/// <summary>
/// User preferences for audio playback.
/// Persisted and auto-saved on change.
/// </summary>
public class AudioPreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "AudioPreferences";

  /// <summary>
  /// Gets or sets the currently selected audio source.
  /// </summary>
  public string CurrentSource { get; set; } = "Spotify";

  /// <summary>
  /// Gets or sets the master volume level (0-100).
  /// </summary>
  public int MasterVolume { get; set; } = 75;
}

/// <summary>
/// User preferences for Spotify playback.
/// </summary>
public class SpotifyPreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "SpotifyPreferences";

  /// <summary>
  /// Gets or sets the URI of the last song played.
  /// </summary>
  public string LastSongPlayed { get; set; } = "";

  /// <summary>
  /// Gets or sets the last song position in milliseconds.
  /// </summary>
  public long SongPositionMs { get; set; } = 0;

  /// <summary>
  /// Gets or sets whether shuffle mode is enabled.
  /// </summary>
  public bool Shuffle { get; set; } = false;

  /// <summary>
  /// Gets or sets the repeat mode.
  /// </summary>
  public RepeatMode Repeat { get; set; } = RepeatMode.Off;
}

/// <summary>
/// User preferences for the file player.
/// </summary>
public class FilePlayerPreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "FilePlayerPreferences";

  /// <summary>
  /// Gets or sets the path of the last song played.
  /// </summary>
  public string LastSongPlayed { get; set; } = "";

  /// <summary>
  /// Gets or sets the last song position in milliseconds.
  /// </summary>
  public long SongPositionMs { get; set; } = 0;

  /// <summary>
  /// Gets or sets whether shuffle mode is enabled.
  /// </summary>
  public bool Shuffle { get; set; } = false;

  /// <summary>
  /// Gets or sets the repeat mode.
  /// </summary>
  public RepeatMode Repeat { get; set; } = RepeatMode.Off;
}

/// <summary>
/// User preferences for the generic USB source.
/// </summary>
public class GenericSourcePreferences
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "GenericSourcePreferences";

  /// <summary>
  /// Gets or sets the USB port for the generic source.
  /// </summary>
  public string USBPort { get; set; } = "";
}


