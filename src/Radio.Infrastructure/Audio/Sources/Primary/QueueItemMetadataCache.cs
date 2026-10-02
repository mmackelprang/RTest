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
/// and queues the path for the reader; when the reader stores a result that differs from what was there,
/// <see cref="Version"/> advances, which is how a reader of the queue learns to fetch it again.
/// </para>
/// <para>
/// Staleness: an entry is keyed by path and stamped with the file's size and last-write time. It is
/// re-validated — one <c>stat</c>, and a re-read only when the stamp differs — when its path is
/// <see cref="Revalidate">revalidated</see>, which the file player does when a path is enqueued and when
/// it becomes the current track. Nothing re-validates on a timer: a file re-tagged in place while it sits
/// in the queue keeps its old row until it is enqueued again or played.
/// </para>
/// <para>
/// One reader, sequential, deliberately: the point is to take load off the share, and a burst of parallel
/// opens on enqueue would trade a continuous load for a spiky one.
/// </para>
/// </remarks>
internal sealed class QueueItemMetadataCache : IDisposable
{
  /// <summary>Entries kept beyond the live queue before <see cref="TrimTo"/> drops the rest.</summary>
  internal const int MaxEntries = 2048;

  private sealed record Entry(QueueItemMetadata Metadata, QueueFileStamp? Stamp);

  private readonly Func<string, QueueItemMetadata> _read;
  private readonly Func<string, QueueFileStamp?> _stat;
  private readonly ILogger _logger;
  private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<string, bool> _pending = new(StringComparer.Ordinal);
  private readonly Channel<string> _work =
    Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
  private readonly CancellationTokenSource _cts = new();
  private readonly object _idleLock = new();
  private int _outstanding;
  private TaskCompletionSource _idle = NewCompletedIdle();
  private long _version;
  private Task? _worker;
  private bool _disposed;

  /// <summary>
  /// Creates the cache. Nothing is read until a path is scheduled.
  /// </summary>
  /// <param name="read">Reads one file's row. Called only on the background reader. Must not throw for an
  /// unreadable file — return <see cref="QueueItemMetadata.Placeholder"/> instead (an exception is caught,
  /// logged at Debug, and stored as a placeholder so the path is not re-read on every request).</param>
  /// <param name="stat">The file's stamp, or <c>null</c> when it does not exist. Called only on the reader.</param>
  /// <param name="logger">Logger. Debug only: on the appliance, log volume correlates with audible distortion.</param>
  public QueueItemMetadataCache(
    Func<string, QueueItemMetadata> read,
    Func<string, QueueFileStamp?> stat,
    ILogger logger)
  {
    _read = read;
    _stat = stat;
    _logger = logger;
  }

  /// <summary>
  /// Advances whenever a stored row changes (a first read, or a re-read that found different tags).
  /// Never advances for a re-validation that found the file unchanged.
  /// </summary>
  public long Version => Interlocked.Read(ref _version);

  /// <summary>Number of cached paths.</summary>
  public int Count => _entries.Count;

  /// <summary>
  /// The cached row for <paramref name="filePath"/>, or — on a miss — its placeholder, with the path
  /// queued for the reader. Never touches the file.
  /// </summary>
  public QueueItemMetadata GetOrSchedule(string filePath)
  {
    if (_entries.TryGetValue(filePath, out Entry? entry))
    {
      return entry.Metadata;
    }

    Enqueue(filePath);
    return QueueItemMetadata.Placeholder(filePath);
  }

  /// <summary>
  /// Queues each path for the reader: a path never seen is read; a cached path is re-read only if its
  /// size or last-write time changed. Returns at once — the work happens on the background reader.
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
    if (_entries.Count <= MaxEntries)
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
  /// Completes when every path queued so far has been processed. A test rendezvous — production code
  /// never waits on the reader.
  /// </summary>
  internal Task WhenIdleAsync()
  {
    lock (_idleLock)
    {
      return _idle.Task;
    }
  }

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
      // Disposed.
    }
    finally
    {
      // The reader is gone (disposed): nothing still queued will be processed, and no file is open any
      // more. Release anyone waiting — which is only ever a test.
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

    if (_entries.TryGetValue(path, out Entry? existing) && existing.Stamp == stamp)
    {
      // Same size and last-write time (or still missing): the cached row is the file's. No read.
      return;
    }

    QueueItemMetadata metadata;
    if (stamp == null)
    {
      metadata = QueueItemMetadata.Placeholder(path);
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
    }

    _entries[path] = new Entry(metadata, stamp);
    if (existing == null || existing.Metadata != metadata)
    {
      Interlocked.Increment(ref _version);
    }
  }

  private void MarkDone()
  {
    lock (_idleLock)
    {
      if (_outstanding > 0 && --_outstanding == 0)
      {
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

    // _cts is deliberately not disposed: a reader that is still finishing reads its token, and a
    // CancellationTokenSource with no timer holds nothing that needs releasing. A read already in
    // progress runs to completion; WhenIdleAsync completes once it has.
  }
}
