using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Interface for persisting and restoring queue state.
/// </summary>
public interface IQueuePersistenceService
{
  /// <summary>
  /// Saves the current queue state for a specific source.
  /// </summary>
  /// <param name="sourceType">The source type (e.g., "FilePlayer", "Spotify").</param>
  /// <param name="queueState">The queue state to save.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task SaveQueueStateAsync(string sourceType, QueueState queueState, CancellationToken cancellationToken = default);

  /// <summary>
  /// Loads the saved queue state for a specific source.
  /// </summary>
  /// <param name="sourceType">The source type (e.g., "FilePlayer", "Spotify").</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The saved queue state, or null if no state exists.</returns>
  Task<QueueState?> LoadQueueStateAsync(string sourceType, CancellationToken cancellationToken = default);

  /// <summary>
  /// Clears the saved queue state for a specific source.
  /// </summary>
  /// <param name="sourceType">The source type (e.g., "FilePlayer", "Spotify").</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  Task ClearQueueStateAsync(string sourceType, CancellationToken cancellationToken = default);
}
