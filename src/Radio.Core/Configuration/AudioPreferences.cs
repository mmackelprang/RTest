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
  public string CurrentSource { get; set; } = "Radio";

  /// <summary>
  /// Gets or sets the currently selected audio output device ID.
  /// Empty string means use default device.
  /// </summary>
  public string CurrentOutput { get; set; } = "";

  /// <summary>
  /// Gets or sets the master volume level (0-100).
  /// </summary>
  public int MasterVolume { get; set; } = 75;

  /// <summary>
  /// Gets or sets whether audio is muted.
  /// </summary>
  public bool IsMuted { get; set; } = false;

  /// <summary>
  /// Gets or sets the audio balance (-100 to 100, where 0 is center).
  /// </summary>
  public int Balance { get; set; } = 0;

  /// <summary>
  /// Gets or sets the currently selected audio input device ID (for recording/vinyl sources).
  /// Empty string means use default device.
  /// </summary>
  public string CurrentInput { get; set; } = "";

  /// <summary>
  /// Gets or sets the default Google Cast device ID.
  /// When set, auto-connects to this device when Cast output is selected.
  /// </summary>
  public string DefaultCastDeviceId { get; set; } = "";

  /// <summary>
  /// Gets or sets the friendly name of the default Cast device.
  /// Stored alongside ID to avoid DB lookups during device enumeration.
  /// </summary>
  public string DefaultCastDeviceName { get; set; } = "";
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

  /// <summary>
  /// Gets or sets the persisted queue items (file paths in order).
  /// Used to restore the queue on application restart.
  /// </summary>
  public List<string> QueueItems { get; set; } = new();

  /// <summary>
  /// Gets or sets the current index in the queue.
  /// Used to restore playback position in the queue.
  /// </summary>
  public int CurrentQueueIndex { get; set; } = -1;
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


