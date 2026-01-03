namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents the persisted state of a playback queue.
/// </summary>
public class QueueState
{
  /// <summary>
  /// Gets or sets the source type (e.g., "FilePlayer", "Spotify").
  /// </summary>
  public required string SourceType { get; set; }

  /// <summary>
  /// Gets or sets the list of track identifiers in the queue.
  /// For FilePlayer, these are file paths. For Spotify, these are URIs.
  /// </summary>
  public required List<string> QueueItems { get; set; } = new();

  /// <summary>
  /// Gets or sets the current playing index in the queue.
  /// -1 if no item is currently playing.
  /// </summary>
  public int CurrentIndex { get; set; } = -1;

  /// <summary>
  /// Gets or sets whether shuffle mode is enabled.
  /// </summary>
  public bool IsShuffleEnabled { get; set; }

  /// <summary>
  /// Gets or sets the repeat mode.
  /// </summary>
  public RepeatMode RepeatMode { get; set; }

  /// <summary>
  /// Gets or sets the timestamp when this state was saved.
  /// </summary>
  public DateTime SavedAt { get; set; } = DateTime.UtcNow;

  /// <summary>
  /// Gets or sets optional metadata about the queue (e.g., playlist name).
  /// </summary>
  public Dictionary<string, string>? Metadata { get; set; }
}
