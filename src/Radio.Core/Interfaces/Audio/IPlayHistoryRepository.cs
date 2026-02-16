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
  /// Gets play history entries for a specific source type.
  /// </summary>
  /// <param name="source">The source type to filter by.</param>
  /// <param name="count">The number of entries to retrieve.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of play history entries for the specified source.</returns>
  Task<IReadOnlyList<PlayHistoryEntry>> GetBySourceAsync(
    PlaySource source,
    int count = 20,
    CancellationToken ct = default);

  /// <summary>
  /// Checks if a track with the same title and artist was recently played.
  /// Used to prevent duplicate entries in quick succession.
  /// </summary>
  /// <param name="title">The track title.</param>
  /// <param name="artist">The artist name.</param>
  /// <param name="withinMinutes">Time window in minutes to check for duplicates.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if a matching entry exists within the time window.</returns>
  Task<bool> ExistsRecentlyPlayedAsync(
    string title,
    string artist,
    int withinMinutes = 5,
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

  /// <summary>
  /// Updates an existing play history entry with new metadata.
  /// Used when fingerprinting identifies a track after playback started.
  /// </summary>
  /// <param name="entry">The updated history entry.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if updated, false if not found.</returns>
  Task<bool> UpdateAsync(PlayHistoryEntry entry, CancellationToken ct = default);

  /// <summary>
  /// Gets the most recent unidentified play history entry for a source.
  /// Used to find entries that can be updated with fingerprinting results.
  /// </summary>
  /// <param name="source">The audio source type.</param>
  /// <param name="withinMinutes">Time window to look back.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The most recent unidentified entry, or null if not found.</returns>
  Task<PlayHistoryEntry?> GetRecentUnidentifiedAsync(
    PlaySource source,
    int withinMinutes = 5,
    CancellationToken ct = default);

  /// <summary>
  /// Finalizes a play history entry by setting its end timestamp.
  /// Calculates DurationSeconds from PlayedAt to endedAt.
  /// Used by song change detection to close out a previous entry.
  /// </summary>
  /// <param name="id">The history entry ID to finalize.</param>
  /// <param name="endedAt">When the song stopped playing.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>True if finalized, false if not found.</returns>
  Task<bool> FinalizeEntryAsync(string id, DateTime endedAt, CancellationToken ct = default);

  /// <summary>
  /// Searches play history entries by title, artist, or album.
  /// </summary>
  /// <param name="searchTerm">The search term to match against title, artist, or album.</param>
  /// <param name="limit">Maximum number of results to return.</param>
  /// <param name="offset">Number of results to skip for pagination.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Paginated search results with total count.</returns>
  Task<(IReadOnlyList<PlayHistoryEntry> Items, int TotalCount)> SearchAsync(
    string searchTerm,
    int? limit = null,
    int? offset = null,
    CancellationToken ct = default);
}
