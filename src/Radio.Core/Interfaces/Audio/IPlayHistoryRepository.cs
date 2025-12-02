using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Repository for play history operations.
/// </summary>
public interface IPlayHistoryRepository
{
  /// <summary>
  /// Records a play history entry.
  /// </summary>
  /// <param name="entry">The history entry to record.</param>
  /// <param name="ct">Cancellation token.</param>
  Task RecordPlayAsync(PlayHistoryEntry entry, CancellationToken ct = default);

  /// <summary>
  /// Gets the most recent play history entries.
  /// </summary>
  /// <param name="count">The number of entries to retrieve.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of recent play history entries.</returns>
  Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(
    int count = 20,
    CancellationToken ct = default);

  /// <summary>
  /// Gets play history entries within a date range.
  /// </summary>
  /// <param name="start">The start date/time.</param>
  /// <param name="end">The end date/time.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of play history entries within the range.</returns>
  Task<IReadOnlyList<PlayHistoryEntry>> GetByDateRangeAsync(
    DateTime start,
    DateTime end,
    CancellationToken ct = default);

  /// <summary>
  /// Gets play statistics for the history.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Play statistics.</returns>
  Task<PlayStatistics> GetStatisticsAsync(CancellationToken ct = default);

  /// <summary>
  /// Gets a specific play history entry by ID.
  /// </summary>
  /// <param name="id">The history entry ID.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The history entry, or null if not found.</returns>
  Task<PlayHistoryEntry?> GetByIdAsync(string id, CancellationToken ct = default);

  /// <summary>
  /// Deletes a play history entry.
  /// </summary>
  /// <param name="id">The history entry ID to delete.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if deleted, false if not found.</returns>
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
