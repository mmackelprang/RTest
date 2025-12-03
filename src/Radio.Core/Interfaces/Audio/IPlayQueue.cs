using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for audio sources that support playlist or queue management.
/// Provides methods to retrieve, modify, and navigate a playback queue.
/// </summary>
public interface IPlayQueue
{
  /// <summary>
  /// Gets the current queue items in order.
  /// </summary>
  IReadOnlyList<QueueItem> QueueItems { get; }

  /// <summary>
  /// Gets the zero-based index of the currently playing item in the queue.
  /// Returns -1 if no item is currently playing.
  /// </summary>
  int CurrentIndex { get; }

  /// <summary>
  /// Gets the total number of items in the queue.
  /// </summary>
  int Count { get; }

  /// <summary>
  /// Retrieves the current playback queue.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation that returns the queue items.</returns>
  Task<IReadOnlyList<QueueItem>> GetQueueAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Adds a track to the queue.
  /// </summary>
  /// <param name="trackIdentifier">The identifier of the track to add (e.g., URI, file path).</param>
  /// <param name="position">Optional position to insert at. If null, adds to the end.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task AddToQueueAsync(string trackIdentifier, int? position = null, CancellationToken cancellationToken = default);

  /// <summary>
  /// Removes an item from the queue at the specified index.
  /// </summary>
  /// <param name="index">The zero-based index of the item to remove.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task RemoveFromQueueAsync(int index, CancellationToken cancellationToken = default);

  /// <summary>
  /// Clears all items from the queue.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task ClearQueueAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Moves a queue item from one position to another.
  /// </summary>
  /// <param name="fromIndex">The zero-based index of the item to move.</param>
  /// <param name="toIndex">The zero-based index to move the item to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default);

  /// <summary>
  /// Jumps to and plays the item at the specified index in the queue.
  /// </summary>
  /// <param name="index">The zero-based index of the item to play.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task JumpToIndexAsync(int index, CancellationToken cancellationToken = default);

  /// <summary>
  /// Event raised when the queue changes.
  /// </summary>
  event EventHandler<QueueChangedEventArgs>? QueueChanged;
}
