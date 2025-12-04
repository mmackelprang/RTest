namespace Radio.API.Models;

/// <summary>
/// Represents the current playback state.
/// </summary>
public class PlaybackStateDto
{
  /// <summary>
  /// Gets or sets whether audio is currently playing.
  /// </summary>
  public bool IsPlaying { get; set; }

  /// <summary>
  /// Gets or sets whether playback is paused.
  /// </summary>
  public bool IsPaused { get; set; }

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

  /// <summary>
  /// Gets or sets the current position in the track (if applicable).
  /// </summary>
  public TimeSpan? Position { get; set; }

  /// <summary>
  /// Gets or sets the duration of the current track (if applicable).
  /// </summary>
  public TimeSpan? Duration { get; set; }

  /// <summary>
  /// Gets or sets the active audio source information.
  /// </summary>
  public AudioSourceDto? ActiveSource { get; set; }

  /// <summary>
  /// Gets or sets the current ducking state.
  /// </summary>
  public DuckingStateDto? DuckingState { get; set; }

  /// <summary>
  /// Gets or sets whether the source can skip to next track.
  /// </summary>
  public bool CanNext { get; set; }

  /// <summary>
  /// Gets or sets whether the source can go to previous track.
  /// </summary>
  public bool CanPrevious { get; set; }

  /// <summary>
  /// Gets or sets whether the source can toggle shuffle.
  /// </summary>
  public bool CanShuffle { get; set; }

  /// <summary>
  /// Gets or sets whether the source can change repeat mode.
  /// </summary>
  public bool CanRepeat { get; set; }

  /// <summary>
  /// Gets or sets whether shuffle is currently enabled.
  /// </summary>
  public bool IsShuffleEnabled { get; set; }

  /// <summary>
  /// Gets or sets the current repeat mode (Off, One, All).
  /// </summary>
  public string? RepeatMode { get; set; }
}

/// <summary>
/// Request to update playback state.
/// </summary>
public class UpdatePlaybackRequest
{
  /// <summary>
  /// Gets or sets the action to perform.
  /// </summary>
  public PlaybackAction Action { get; set; }

  /// <summary>
  /// Gets or sets the volume (0.0 to 1.0), if changing volume.
  /// </summary>
  public float? Volume { get; set; }

  /// <summary>
  /// Gets or sets the balance (-1.0 left to 1.0 right), if changing balance.
  /// </summary>
  public float? Balance { get; set; }

  /// <summary>
  /// Gets or sets whether to mute/unmute.
  /// </summary>
  public bool? IsMuted { get; set; }

  /// <summary>
  /// Gets or sets the position to seek to (if seeking).
  /// </summary>
  public TimeSpan? SeekPosition { get; set; }
}

/// <summary>
/// Playback control actions.
/// </summary>
public enum PlaybackAction
{
  /// <summary>No action, just update properties.</summary>
  None,

  /// <summary>Start playback.</summary>
  Play,

  /// <summary>Pause playback.</summary>
  Pause,

  /// <summary>Stop playback.</summary>
  Stop,

  /// <summary>Seek to a position.</summary>
  Seek
}

/// <summary>
/// Represents an audio source.
/// </summary>
public class AudioSourceDto
{
  /// <summary>
  /// Gets or sets the unique identifier.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the source type.
  /// </summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the source category (Primary or Event).
  /// </summary>
  public string Category { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the current state.
  /// </summary>
  public string State { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the volume level.
  /// </summary>
  public float Volume { get; set; }

  /// <summary>
  /// Gets or sets whether seeking is supported.
  /// </summary>
  public bool IsSeekable { get; set; }

  /// <summary>
  /// Gets or sets the metadata about current content.
  /// Values are typed objects (e.g., TimeSpan for Duration, int for TrackNumber).
  /// </summary>
  public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Request to select or switch audio source.
/// </summary>
public class SelectSourceRequest
{
  /// <summary>
  /// Gets or sets the source type to activate.
  /// </summary>
  public string SourceType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets optional source-specific configuration.
  /// </summary>
  public Dictionary<string, string>? Configuration { get; set; }
}

/// <summary>
/// Represents the ducking state.
/// </summary>
public class DuckingStateDto
{
  /// <summary>
  /// Gets or sets whether ducking is active.
  /// </summary>
  public bool IsDucking { get; set; }

  /// <summary>
  /// Gets or sets the current duck level (0-100).
  /// </summary>
  public float DuckLevel { get; set; }

  /// <summary>
  /// Gets or sets the number of active event sources.
  /// </summary>
  public int ActiveEventCount { get; set; }
}

/// <summary>
/// Represents an audio device.
/// </summary>
public class AudioDeviceDto
{
  /// <summary>
  /// Gets or sets the device identifier.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device name.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device type (Input, Output, Duplex).
  /// </summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether this is the default device.
  /// </summary>
  public bool IsDefault { get; set; }

  /// <summary>
  /// Gets or sets whether this is a USB device.
  /// </summary>
  public bool IsUSBDevice { get; set; }

  /// <summary>
  /// Gets or sets the USB port path (if applicable).
  /// </summary>
  public string? USBPort { get; set; }

  /// <summary>
  /// Gets or sets the maximum channels supported.
  /// </summary>
  public int MaxChannels { get; set; }

  /// <summary>
  /// Gets or sets supported sample rates.
  /// </summary>
  public int[]? SupportedSampleRates { get; set; }
}

/// <summary>
/// Request to set the output device.
/// </summary>
public class SetOutputDeviceRequest
{
  /// <summary>
  /// Gets or sets the device ID to use.
  /// </summary>
  public string DeviceId { get; set; } = string.Empty;
}

/// <summary>
/// Represents available audio sources list.
/// </summary>
public class AvailableSourcesDto
{
  /// <summary>
  /// Gets or sets the list of available primary sources.
  /// </summary>
  public List<string> PrimarySources { get; set; } = [];

  /// <summary>
  /// Gets or sets the currently active source type.
  /// </summary>
  public string? ActiveSourceType { get; set; }

  /// <summary>
  /// Gets or sets the active sources in the mixer.
  /// </summary>
  public List<AudioSourceDto> ActiveSources { get; set; } = [];
}

/// <summary>
/// Request to set shuffle state.
/// </summary>
public class SetShuffleRequest
{
  /// <summary>
  /// Gets or sets whether shuffle should be enabled.
  /// </summary>
  public bool Enabled { get; set; }
}

/// <summary>
/// Request to set repeat mode.
/// </summary>
public class SetRepeatModeRequest
{
  /// <summary>
  /// Gets or sets the repeat mode (Off, One, All).
  /// </summary>
  public string Mode { get; set; } = "Off";
}

/// <summary>
/// Represents an item in a playback queue.
/// </summary>
public class QueueItemDto
{
  /// <summary>
  /// Gets or sets the unique identifier for this queue item.
  /// </summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the title of the track.
  /// </summary>
  public string Title { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the artist name(s).
  /// </summary>
  public string Artist { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the album name.
  /// </summary>
  public string Album { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the duration of the track, if available.
  /// </summary>
  public TimeSpan? Duration { get; set; }

  /// <summary>
  /// Gets or sets the URL to the album art, if available.
  /// </summary>
  public string? AlbumArtUrl { get; set; }

  /// <summary>
  /// Gets or sets the zero-based index of this item in the queue.
  /// </summary>
  public int Index { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether this is the currently playing item.
  /// </summary>
  public bool IsCurrent { get; set; }
}

/// <summary>
/// Request to add a track to the queue.
/// </summary>
public class AddToQueueRequest
{
  /// <summary>
  /// Gets or sets the track identifier (e.g., URI, file path).
  /// </summary>
  public string TrackIdentifier { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the optional position to insert at. If null, adds to the end.
  /// </summary>
  public int? Position { get; set; }
}

/// <summary>
/// Request to move a queue item.
/// </summary>
public class MoveQueueItemRequest
{
  /// <summary>
  /// Gets or sets the zero-based index of the item to move.
  /// </summary>
  public int FromIndex { get; set; }

  /// <summary>
  /// Gets or sets the zero-based index to move the item to.
  /// </summary>
  public int ToIndex { get; set; }
}

/// <summary>
/// Represents the current state of a radio device.
/// </summary>
public class RadioStateDto
{
  /// <summary>
  /// Gets or sets the current frequency in MHz (FM) or kHz (AM).
  /// </summary>
  public double Frequency { get; set; }

  /// <summary>
  /// Gets or sets the current band (AM, FM, etc.).
  /// </summary>
  public string Band { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the frequency step size in MHz (FM) or kHz (AM).
  /// </summary>
  public double FrequencyStep { get; set; }

  /// <summary>
  /// Gets or sets the signal strength as a percentage (0-100).
  /// </summary>
  public int SignalStrength { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the radio is receiving a stereo signal.
  /// </summary>
  public bool IsStereo { get; set; }

  /// <summary>
  /// Gets or sets the current equalizer mode.
  /// </summary>
  public string EqualizerMode { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the device-specific volume (0-100).
  /// </summary>
  public int DeviceVolume { get; set; }

  /// <summary>
  /// Gets or sets a value indicating whether the radio is currently scanning.
  /// </summary>
  public bool IsScanning { get; set; }

  /// <summary>
  /// Gets or sets the scan direction if scanning; otherwise, null.
  /// </summary>
  public string? ScanDirection { get; set; }
}

/// <summary>
/// Request to set radio frequency.
/// </summary>
public class SetFrequencyRequest
{
  /// <summary>
  /// Gets or sets the frequency to tune to in MHz (FM) or kHz (AM).
  /// </summary>
  public double Frequency { get; set; }
}

/// <summary>
/// Request to set radio band.
/// </summary>
public class SetBandRequest
{
  /// <summary>
  /// Gets or sets the band to switch to (AM, FM, etc.).
  /// </summary>
  public string Band { get; set; } = string.Empty;
}

/// <summary>
/// Request to set frequency step size.
/// </summary>
public class SetFrequencyStepRequest
{
  /// <summary>
  /// Gets or sets the step size in MHz (FM) or kHz (AM).
  /// </summary>
  public double Step { get; set; }
}

/// <summary>
/// Request to start scanning.
/// </summary>
public class StartScanRequest
{
  /// <summary>
  /// Gets or sets the scan direction (Up or Down).
  /// </summary>
  public string Direction { get; set; } = string.Empty;
}

/// <summary>
/// Request to set equalizer mode.
/// </summary>
public class SetEqualizerModeRequest
{
  /// <summary>
  /// Gets or sets the equalizer mode.
  /// </summary>
  public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Request to set device volume.
/// </summary>
public class SetDeviceVolumeRequest
{
  /// <summary>
  /// Gets or sets the volume level (0-100).
  /// </summary>
  public int Volume { get; set; }
}

/// <summary>
/// Represents the current "Now Playing" information with structured track data.
/// This DTO always returns valid content with defaults when no track is available.
/// </summary>
public class NowPlayingDto
{
  /// <summary>
  /// Gets or sets the type of the audio source (e.g., "Spotify", "Radio", "FilePlayer").
  /// </summary>
  public string SourceType { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the display name of the audio source.
  /// </summary>
  public string SourceName { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets whether audio is currently playing.
  /// </summary>
  public bool IsPlaying { get; set; }

  /// <summary>
  /// Gets or sets whether playback is paused.
  /// </summary>
  public bool IsPaused { get; set; }

  /// <summary>
  /// Gets or sets the track title. Never null; defaults to "No Track" when empty.
  /// </summary>
  public string Title { get; set; } = "No Track";

  /// <summary>
  /// Gets or sets the artist name. Never null; defaults to "--" when empty.
  /// </summary>
  public string Artist { get; set; } = "--";

  /// <summary>
  /// Gets or sets the album name. Never null; defaults to "--" when empty.
  /// </summary>
  public string Album { get; set; } = "--";

  /// <summary>
  /// Gets or sets the URL to the album art. Never null; defaults to generic icon URL when empty.
  /// </summary>
  public string AlbumArtUrl { get; set; } = "/images/default-album-art.png";

  /// <summary>
  /// Gets or sets the current playback position, if available.
  /// </summary>
  public TimeSpan? Position { get; set; }

  /// <summary>
  /// Gets or sets the track duration, if available.
  /// </summary>
  public TimeSpan? Duration { get; set; }

  /// <summary>
  /// Gets or sets the playback progress as a percentage (0-100), if duration is available.
  /// </summary>
  public double? ProgressPercentage { get; set; }

  /// <summary>
  /// Gets or sets additional metadata specific to the source or track.
  /// May include genre, year, bitrate, or other source-specific information.
  /// </summary>
  public Dictionary<string, object>? ExtendedMetadata { get; set; }
}

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
