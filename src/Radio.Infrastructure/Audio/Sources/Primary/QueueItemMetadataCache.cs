using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// What a queue row shows for one file: the fields <c>FilePlayerAudioSource</c> used to re-read from
/// the file on every queue request (AUD-96).
/// </summary>
/// <param name="Title">Title tag, else the file name without its extension.</param>
/// <param name="Artist">Artist tag, else <c>"--"</c>.</param>
/// <param name="Album">Album tag, else <c>"--"</c>.</param>
/// <param name="Duration">Duration when the file could be read.</param>
/// <param name="AlbumArtUrl">Album-art cache URL of the embedded picture, when there is one.</param>
internal sealed record QueueItemMetadata(
  string Title,
  string Artist,
  string Album,
  TimeSpan? Duration,
  string? AlbumArtUrl)
{
  /// <summary>
  /// What a row shows before its file has been read, and for a file that cannot be read: exactly the
  /// defaults the per-request read used when the tag reader returned <c>null</c>.
  /// </summary>
  public static QueueItemMetadata Placeholder(string filePath) =>
    new(Path.GetFileNameWithoutExtension(filePath), "--", "--", null, null);
}

/// <summary>
/// A file's size and last-write time, which is what decides whether a cached entry is still the file's.
/// </summary>
internal readonly record struct QueueFileStamp(long Length, DateTime LastWriteUtc);

/// <summary>
/// Per-path cache of queue-row metadata, filled off the request path by one background reader (AUD-96).
/// </summary>
/// <remarks>
/// <para>
/// ⚠ Why this exists, measured on the appliance 2026-10-02: the play queue lives on a CIFS share over the
/// box's only management link (WiFi), and every queue read re-read every queued file — tags, a TagLib
/// duration read and an embedded-art read, about three opens per file. With 47 tracks queued,
/// <c>GET /api/queue/full</c> took 1.4–4.7 s, and the API's 500 ms state poller did the same read on every
/// pass: 406 CIFS opens and ~100 MB read per 10 s while File Player played.
/// </para>
/// <para>
/// <see cref="GetOrSchedule"/> never touches a file. A miss returns <see cref="QueueItemMetadata.Placeholder"/>
/// and queues the path for the reader. <see cref="Version"/> tells a reader of the queue that rows changed;
/// it is published in batches (see <see cref="PublishEvery"/>), not per row, because every step of it is
/// a <c>QueueChanged</c> broadcast and a queue re-read by every open panel.
/// </para>
/// <para>
/// Staleness: an entry is keyed by path and stamped with the file's size and last-write time. It is
/// re-validated — one <c>stat</c>, and a re-read only when the stamp differs — when its path is
/// <see cref="Revalidate">revalidated</see>, which the file player does when a path is enqueued and when
/// it becomes the current track. A file re-tagged in place while it sits in the queue keeps its old row
/// until then. Two exceptions are re-read even with an unchanged stamp: a row whose read failed (a share
/// hiccup must not pin a placeholder for the life of the process), and a row older than
/// <see cref="MaxAge"/> — the album-art cache deletes art it has not been asked to save for 7 days, and
/// re-reading is what renews it.
/// </para>
/// <para>
/// One reader, sequential, deliberately: the point is to take load off the share, and a burst of parallel
/// opens on enqueue would trade a continuous load for a spiky one.
/// </para>
/// </remarks>
internal sealed class QueueItemMetadataCache : IDisposable
{
  /// <summary>
  /// Once the cache holds more than this many paths, <see cref="TrimTo"/> drops every path not still
  /// queued. A total-count threshold, not a size the cache is held to.
  /// </summary>
  internal const int MaxEntries = 2048;

  /// <summary>Rows changed before <see cref="Version"/> is published while the reader is still busy.</summary>
  internal const int PublishEvery = 16;

  /// <summary>
  /// A row older than this is re-read the next time it is asked for, even if its file is unchanged. Well
  /// inside <c>AlbumArtCacheService</c>'s 7-day expiry, whose clock a re-read (a <c>Save</c>) resets.
  /// </summary>
  internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

  // Verified: the row is the file's as of Stamp. False when the read failed for a file that exists, so the
  // next revalidation reads it again instead of trusting the stamp.
  private sealed record Entry(QueueItemMetadata Metadata, QueueFileStamp? Stamp, bool Verified, DateTimeOffset ReadAt);

  private readonly Func<string, QueueItemMetadata> _read;
  private readonly Func<string, QueueFileStamp?> _stat;
  private readonly ILogger _logger;
  private readonly TimeProvider _time;
  private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<string, bool> _pending = new(StringComparer.Ordinal);
  private readonly Channel<string> _work =
    Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
  private readonly CancellationTokenSource _cts = new();
  private readonly object _idleLock = new();
  private int _outstanding;
  private TaskCompletionSource _idle = NewCompletedIdle();
  private long _version;
  private int _unpublished; // reader thread only
  private Task? _worker;
  private bool _disposed;

  /// <summary>
  /// Creates the cache. Nothing is read until a path is scheduled.
  /// </summary>
  /// <param name="read">Reads one file's row. Called only on the background reader. Returns
  /// <see cref="QueueItemMetadata.Placeholder"/> for a file it cannot read; an exception is treated the
  /// same way.</param>
  /// <param name="stat">The file's stamp, or <c>null</c> when it does not exist. Called only on the reader.</param>
  /// <param name="logger">Logger. Debug only: on the appliance, log volume correlates with audible distortion.</param>
  /// <param name="timeProvider">Clock for <see cref="MaxAge"/>; <see cref="TimeProvider.System"/> by default.</param>
  public QueueItemMetadataCache(
    Func<string, QueueItemMetadata> read,
    Func<string, QueueFileStamp?> stat,
    ILogger logger,
    TimeProvider? timeProvider = null)
  {
    _read = read;
    _stat = stat;
    _logger = logger;
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>
  /// Advances when stored rows have changed (a first read, or a re-read that found different tags) — once
  /// per <see cref="PublishEvery"/> changed rows while the reader is busy, and once when it goes idle with
  /// changes not yet published. Never advances for a re-validation that found the file unchanged.
  /// </summary>
  public long Version => Interlocked.Read(ref _version);

  /// <summary>Number of cached paths.</summary>
  public int Count => _entries.Count;

  /// <summary>Whether <see cref="TrimTo"/> would drop anything — so a caller can skip building its list.</summary>
  public bool NeedsTrim => _entries.Count > MaxEntries;

  /// <summary>
  /// The cached row for <paramref name="filePath"/>, or — on a miss — its placeholder, with the path
  /// queued for the reader. A row past <see cref="MaxAge"/> is returned as it is and queued for a re-read.
  /// Never touches the file.
  /// </summary>
  public QueueItemMetadata GetOrSchedule(string filePath)
  {
    if (_entries.TryGetValue(filePath, out Entry? entry))
    {
      if (IsAged(entry))
      {
        Enqueue(filePath);
      }
      return entry.Metadata;
    }

    Enqueue(filePath);
    return QueueItemMetadata.Placeholder(filePath);
  }

  /// <summary>
  /// Queues each path for the reader: a path never seen is read; a cached path is re-read if its size or
  /// last-write time changed, its last read failed, or it is past <see cref="MaxAge"/>. Returns at once —
  /// the work happens on the background reader.
  /// </summary>
  public void Revalidate(IEnumerable<string> filePaths)
  {
    foreach (string path in filePaths)
    {
      Enqueue(path);
    }
  }

  /// <summary>
  /// Drops every entry whose path is not in <paramref name="keep"/>, once the cache holds more than
  /// <see cref="MaxEntries"/>. A dropped path that comes back is simply read again.
  /// </summary>
  public void TrimTo(IReadOnlyCollection<string> keep)
  {
    if (!NeedsTrim)
    {
      return;
    }

    HashSet<string> keepSet = new(keep, StringComparer.Ordinal);
    foreach (string path in _entries.Keys)
    {
      if (!keepSet.Contains(path))
      {
        _entries.TryRemove(path, out _);
      }
    }
  }

  /// <summary>
  /// Completes when every path queued so far has been processed, or the cache is disposed. Used by tests
  /// as a rendezvous, and by a playlist save to wait (bounded) for rows it is about to store.
  /// </summary>
  internal Task WhenIdleAsync()
  {
    lock (_idleLock)
    {
      return _idle.Task;
    }
  }

  private bool IsAged(Entry entry) => _time.GetUtcNow() - entry.ReadAt > MaxAge;

  private void Enqueue(string filePath)
  {
    // One queued read per path at a time. A request for a path already queued is satisfied by the
    // queued read, which has not started yet and so will see the file as it is when it runs.
    if (!_pending.TryAdd(filePath, true))
    {
      return;
    }

    lock (_idleLock)
    {
      if (_disposed)
      {
        _pending.TryRemove(filePath, out _);
        return;
      }

      if (_outstanding++ == 0)
      {
        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      }

      // Started on first use rather than in the constructor, so a source that is never queried never
      // owns a thread-pool loop.
      _worker ??= Task.Run(RunAsync);

      // Inside the lock, so Dispose cannot complete the channel between the count and the write.
      _work.Writer.TryWrite(filePath);
    }
  }

  private async Task RunAsync()
  {
    try
    {
      await foreach (string path in _work.Reader.ReadAllAsync(_cts.Token))
      {
        // ReadAllAsync checks the token only while waiting for more work, not between items it already
        // holds — so without this a disposed cache would go on reading the whole backlog.
        if (_cts.IsCancellationRequested)
        {
          break;
        }

        // Removed BEFORE processing, so a revalidation requested while this read runs queues a second
        // pass rather than being swallowed by one that may already have stat'ed the old file.
        _pending.TryRemove(path, out _);
        try
        {
          Process(path);
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Queue metadata read failed for {File}", path);
        }
        finally
        {
          MarkDone();
        }
      }
    }
    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
    {
      // Disposed while waiting for work.
    }
    finally
    {
      // Only reached once disposed. Paths still queued are abandoned unread; the read that was in progress
      // (if any) has finished, so no file is open. Release anyone waiting.
      lock (_idleLock)
      {
        _outstanding = 0;
        _idle.TrySetResult();
      }
    }
  }

  private void Process(string path)
  {
    QueueFileStamp? stamp = _stat(path);

    if (_entries.TryGetValue(path, out Entry? existing)
        && existing.Verified
        && existing.Stamp == stamp
        && !IsAged(existing))
    {
      // Same size and last-write time (or still missing), read successfully, and recently: the cached
      // row is the file's. No read.
      return;
    }

    QueueItemMetadata metadata;
    bool verified;
    if (stamp == null)
    {
      // Missing: the placeholder IS the verified answer until the file appears (a stamp then differs).
      metadata = QueueItemMetadata.Placeholder(path);
      verified = true;
    }
    else
    {
      try
      {
        metadata = _read(path);
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Could not read queue metadata from {File}; showing its file name", path);
        metadata = QueueItemMetadata.Placeholder(path);
      }

      // A file that exists but read as nothing at all is most likely a failed read (on the appliance, a
      // CIFS hiccup), so it is not trusted: the next revalidation reads it again. A genuinely untagged
      // file normally still yields a duration and so does not land here.
      verified = metadata != QueueItemMetadata.Placeholder(path);
    }

    _entries[path] = new Entry(metadata, stamp, verified, _time.GetUtcNow());
    if (existing == null || existing.Metadata != metadata)
    {
      if (++_unpublished >= PublishEvery)
      {
        Publish();
      }
    }
  }

  private void Publish()
  {
    _unpublished = 0;
    Interlocked.Increment(ref _version);
  }

  private void MarkDone()
  {
    lock (_idleLock)
    {
      if (_outstanding > 0 && --_outstanding == 0)
      {
        // Idle: publish what the batch changed before anyone waiting is released, so a waiter that
        // then reads Version sees it.
        if (_unpublished > 0)
        {
          Publish();
        }
        _idle.TrySetResult();
      }
    }
  }

  private static TaskCompletionSource NewCompletedIdle()
  {
    TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    done.TrySetResult();
    return done;
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    lock (_idleLock)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      _work.Writer.TryComplete();
    }

    _cts.Cancel();

    // _cts is deliberately not disposed: the reader may still be finishing a read and then checks it, and
    // a CancellationTokenSource with no timer holds nothing that needs releasing. The read in progress (if
    // any) runs to completion; nothing queued after it is read; WhenIdleAsync completes once it has.
  }
}
