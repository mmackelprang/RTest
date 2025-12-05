namespace Radio.API.Models;

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
