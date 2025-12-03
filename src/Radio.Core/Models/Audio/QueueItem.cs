namespace Radio.Core.Models.Audio;

/// <summary>
/// Represents an item in a playback queue.
/// </summary>
public sealed record QueueItem
{
  /// <summary>
  /// Gets the unique identifier for this queue item.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets the title of the track.
  /// </summary>
  public required string Title { get; init; }

  /// <summary>
  /// Gets the artist name(s).
  /// </summary>
  public required string Artist { get; init; }

  /// <summary>
  /// Gets the album name.
  /// </summary>
  public required string Album { get; init; }

  /// <summary>
  /// Gets the duration of the track, if available.
  /// </summary>
  public TimeSpan? Duration { get; init; }

  /// <summary>
  /// Gets the URL to the album art, if available.
  /// </summary>
  public string? AlbumArtUrl { get; init; }

  /// <summary>
  /// Gets the zero-based index of this item in the queue.
  /// </summary>
  public int Index { get; init; }

  /// <summary>
  /// Gets a value indicating whether this is the currently playing item.
  /// </summary>
  public bool IsCurrent { get; init; }
}

/// <summary>
/// Event arguments for queue change events.
/// </summary>
public sealed class QueueChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the type of change that occurred.
  /// </summary>
  public required QueueChangeType ChangeType { get; init; }

  /// <summary>
  /// Gets the index affected by the change, if applicable.
  /// </summary>
  public int? AffectedIndex { get; init; }

  /// <summary>
  /// Gets the queue item that was affected, if applicable.
  /// </summary>
  public QueueItem? AffectedItem { get; init; }
}

/// <summary>
/// Types of queue changes.
/// </summary>
public enum QueueChangeType
{
  /// <summary>An item was added to the queue.</summary>
  Added,

  /// <summary>An item was removed from the queue.</summary>
  Removed,

  /// <summary>An item was moved within the queue.</summary>
  Moved,

  /// <summary>The entire queue was cleared.</summary>
  Cleared,

  /// <summary>The current playing item changed.</summary>
  CurrentChanged
}
