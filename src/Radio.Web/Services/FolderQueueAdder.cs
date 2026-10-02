using Radzen;

namespace Radio.Web.Services;

/// <summary>
/// What an "Add folder" run did (UI-32): the dialog closes with it and the queue panel turns it into a toast.
/// </summary>
/// <param name="FolderName">The folder's display name.</param>
/// <param name="Added">Tracks the radio accepted.</param>
/// <param name="Requested">Tracks the dialog tried to add.</param>
/// <param name="Skipped">Tracks left out: unreadable when listed, or refused by the radio when added.</param>
/// <param name="Error">Why the run stopped early, or null when it finished.</param>
public sealed record FolderAddResult(string FolderName, int Added, int Requested, int Skipped, string? Error);

/// <summary>
/// Adds a folder's tracks to the queue in batches through <c>POST /api/files/queue</c> (UI-32).
/// </summary>
/// <remarks>
/// <para>Why batches: the API reads each track's tags (and embedded art) as it queues it, and on the box's NAS cold
/// I/O is slow — reading the first 256 KB of each of one artist's 68 files took 12.5 s (~184 ms a file, measured
/// 2026-10-02; that is raw reads, the nearest measurement to a tag read). The Web's HttpClient gives up after 30 s,
/// so a 500-track folder in one request would time out partway with no way to tell how far it got. Twenty per
/// request keeps each call to a few seconds at that rate and lets the dialog show "Adding 40 of 68…".</para>
/// <para>Order is preserved: batches are sent one after another, each awaited, in the listing's order.</para>
/// </remarks>
public static class FolderQueueAdder
{
  /// <summary>Paths per request.</summary>
  public const int BatchSize = 20;

  /// <summary>The result of <see cref="AddInBatchesAsync"/>.</summary>
  /// <param name="Added">Paths the API reported added.</param>
  /// <param name="Failed">Paths in completed batches the API did not add.</param>
  /// <param name="Completed">Every batch was sent.</param>
  /// <param name="Error">The error that stopped the run; null when it completed or was cancelled.</param>
  public sealed record Outcome(int Added, int Failed, bool Completed, string? Error);

  /// <summary>
  /// Sends <paramref name="paths"/> to <paramref name="addBatch"/> <paramref name="batchSize"/> at a time, in order,
  /// reporting the number of paths sent so far after each batch. Stops at the first batch of which nothing was
  /// added (the API refused or could not be reached), and before the next batch once
  /// <paramref name="cancellationToken"/> is cancelled.
  /// </summary>
  public static async Task<Outcome> AddInBatchesAsync(
    IReadOnlyList<string> paths,
    int batchSize,
    Func<List<string>, CancellationToken, Task<(bool Success, int AddedCount, string? Error)>> addBatch,
    Func<int, Task> onProgress,
    CancellationToken cancellationToken)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

    var added = 0;
    var failed = 0;
    var sent = 0;
    for (var start = 0; start < paths.Count; start += batchSize)
    {
      if (cancellationToken.IsCancellationRequested)
      {
        return new Outcome(added, failed, false, null);
      }

      var batch = paths.Skip(start).Take(batchSize).ToList();
      (bool Success, int AddedCount, string? Error) result;
      try
      {
        result = await addBatch(batch, cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        return new Outcome(added, failed, false, null);
      }

      if (!result.Success && result.AddedCount == 0)
      {
        return new Outcome(added, failed, false, result.Error ?? "The radio did not accept the tracks");
      }

      added += result.AddedCount;
      failed += Math.Max(0, batch.Count - result.AddedCount);
      sent += batch.Count;
      await onProgress(sent);
    }

    return new Outcome(added, failed, true, null);
  }

  /// <summary>The toast for a finished run: severity and text.</summary>
  public static (NotificationSeverity Severity, string Summary, string Detail) Describe(FolderAddResult result)
  {
    var name = result.FolderName;
    if (result.Error != null)
    {
      return result.Added == 0
        ? (NotificationSeverity.Error, "Nothing added", $"Couldn't add {name}. Nothing was added. ({result.Error})")
        : (NotificationSeverity.Warning, "Stopped early",
          $"Added {result.Added} of {result.Requested} tracks from {name} before an error. ({result.Error})");
    }

    if (result.Added == 0)
    {
      return (NotificationSeverity.Warning, "Nothing added", $"No playable tracks in {name}");
    }

    var tracks = result.Added == 1 ? "track" : "tracks";
    return result.Skipped > 0
      ? (NotificationSeverity.Success, "Folder added",
        $"Added {result.Added} {tracks} from {name} · {result.Skipped} skipped (unsupported or unreadable)")
      : (NotificationSeverity.Success, "Folder added", $"Added {result.Added} {tracks} from {name}");
  }
}
