using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-96: the per-path queue metadata cache. Every read and stat goes through counting fakes, so each
/// assertion is about how many times the file would have been touched.
/// </summary>
/// <remarks>
/// ⚠ No wall clock (CLAUDE.md § Test Timing). Every test that asserts on what the background reader has
/// done first awaits <c>WhenIdleAsync</c> — a completion rendezvous on the reader itself — and the one
/// test that needs the reader held mid-read parks it on a gate the test releases.
/// </remarks>
public class QueueItemMetadataCacheTests
{
  /// <summary>A file system the test owns: stamps it can change, reads it counts.</summary>
  private sealed class FakeFiles
  {
    public ConcurrentDictionary<string, QueueFileStamp?> Stamps { get; } = new();
    public ConcurrentDictionary<string, int> Reads { get; } = new();
    public ConcurrentDictionary<string, int> Stats { get; } = new();
    public ConcurrentDictionary<string, string> Titles { get; } = new();
    public Func<string, QueueItemMetadata>? ReadOverride { get; set; }

    public QueueItemMetadataCache NewCache(TimeProvider? time = null) => new(Read, Stat, NullLogger.Instance, time);

    public void Add(string path, string title, long length = 100)
    {
      Stamps[path] = new QueueFileStamp(length, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
      Titles[path] = title;
    }

    public int ReadsOf(string path) => Reads.GetValueOrDefault(path);

    private QueueItemMetadata Read(string path)
    {
      Reads.AddOrUpdate(path, 1, (_, n) => n + 1);
      if (ReadOverride != null)
      {
        return ReadOverride(path);
      }
      return new QueueItemMetadata(Titles[path], "Artist", "Album", TimeSpan.FromMinutes(3), "/api/albumart/x.jpg");
    }

    private QueueFileStamp? Stat(string path)
    {
      Stats.AddOrUpdate(path, 1, (_, n) => n + 1);
      return Stamps.GetValueOrDefault(path);
    }
  }

  [Fact]
  public async Task AMiss_ReturnsThePlaceholderAtOnce_AndTheReaderFillsIt_Once()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "Song A");
    using var cache = files.NewCache();

    QueueItemMetadata first = cache.GetOrSchedule("/m/a.mp3");
    Assert.Equal(QueueItemMetadata.Placeholder("/m/a.mp3"), first);
    Assert.Equal("a", first.Title);
    Assert.Equal("--", first.Artist);
    Assert.Null(first.AlbumArtUrl);

    await cache.WhenIdleAsync();
    for (int i = 0; i < 20; i++)
    {
      Assert.Equal("Song A", cache.GetOrSchedule("/m/a.mp3").Title);
    }

    await cache.WhenIdleAsync();
    Assert.Equal(1, files.ReadsOf("/m/a.mp3"));
    Assert.Equal(1, files.Stats.GetValueOrDefault("/m/a.mp3"));
  }

  [Fact]
  public async Task RequestsWhileAReadIsQueued_AreServedByThatOneRead()
  {
    var files = new FakeFiles();
    files.Add("/m/gate.mp3", "Gate");
    files.Add("/m/b.mp3", "Song B");
    using var gate = new ManualResetEventSlim(false);
    using var parked = new ManualResetEventSlim(false);
    files.ReadOverride = path =>
    {
      if (path == "/m/gate.mp3")
      {
        parked.Set();
        gate.Wait();
      }
      return new QueueItemMetadata(files.Titles[path], "A", "B", null, null);
    };
    using var cache = files.NewCache();

    // Park the single reader inside the first read, so "b" stays queued while it is asked for.
    cache.GetOrSchedule("/m/gate.mp3");
    Assert.True(parked.Wait(TimeSpan.FromSeconds(30)), "the reader never started");
    for (int i = 0; i < 10; i++)
    {
      Assert.Equal("b", cache.GetOrSchedule("/m/b.mp3").Title);
    }

    gate.Set();
    await cache.WhenIdleAsync();

    Assert.Equal(1, files.ReadsOf("/m/b.mp3"));
    // One queued pass, not ten: without the de-duplication every request would queue its own stat.
    Assert.Equal(1, files.Stats["/m/b.mp3"]);
    Assert.Equal("Song B", cache.GetOrSchedule("/m/b.mp3").Title);
  }

  [Fact]
  public async Task Revalidate_AnUnchangedFile_StatsButDoesNotReRead_AndTheVersionStays()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "Song A");
    using var cache = files.NewCache();
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();
    long version = cache.Version;

    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();

    Assert.Equal(1, files.ReadsOf("/m/a.mp3"));
    Assert.Equal(2, files.Stats["/m/a.mp3"]);
    Assert.Equal(version, cache.Version);
  }

  [Fact]
  public async Task Revalidate_AChangedFile_ReReadsIt_AndAdvancesTheVersion()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "Old Title");
    using var cache = files.NewCache();
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();
    long version = cache.Version;

    // Re-tagged in place: new size, new title.
    files.Add("/m/a.mp3", "New Title", length: 200);
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();

    Assert.Equal(2, files.ReadsOf("/m/a.mp3"));
    Assert.Equal("New Title", cache.GetOrSchedule("/m/a.mp3").Title);
    Assert.True(cache.Version > version);
  }

  [Fact]
  public async Task Revalidate_AChangedStampWithTheSameTags_ReReadsButDoesNotAdvanceTheVersion()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "Same");
    using var cache = files.NewCache();
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();
    long version = cache.Version;

    files.Add("/m/a.mp3", "Same", length: 999);
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();

    Assert.Equal(2, files.ReadsOf("/m/a.mp3"));
    Assert.Equal(version, cache.Version);
  }

  [Fact]
  public async Task AMissingFile_IsCachedAsItsPlaceholder_WithoutARead_AndIsNotAskedForAgain()
  {
    var files = new FakeFiles();
    using var cache = files.NewCache();

    cache.GetOrSchedule("/m/gone.mp3");
    await cache.WhenIdleAsync();
    for (int i = 0; i < 5; i++)
    {
      Assert.Equal("gone", cache.GetOrSchedule("/m/gone.mp3").Title);
    }
    await cache.WhenIdleAsync();

    Assert.Equal(0, files.ReadsOf("/m/gone.mp3"));
    Assert.Equal(1, files.Stats["/m/gone.mp3"]);
  }

  [Fact]
  public async Task AReaderThatThrows_StoresThePlaceholder_AndIsNotRetriedOnEveryRequest()
  {
    var files = new FakeFiles();
    files.Add("/m/bad.mp3", "never");
    files.ReadOverride = _ => throw new IOException("corrupt");
    using var cache = files.NewCache();

    cache.GetOrSchedule("/m/bad.mp3");
    await cache.WhenIdleAsync();
    for (int i = 0; i < 5; i++)
    {
      Assert.Equal("bad", cache.GetOrSchedule("/m/bad.mp3").Title);
    }
    await cache.WhenIdleAsync();

    Assert.Equal(1, files.ReadsOf("/m/bad.mp3"));
  }

  /// <summary>
  /// Review M1: a read that failed on a share hiccup must not pin the placeholder for the life of the
  /// process just because the file's stamp has not changed. The next revalidation reads it again.
  /// </summary>
  [Fact]
  public async Task AFailedRead_IsReadAgainOnTheNextRevalidation_ThoughTheStampIsUnchanged()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "Song A");
    bool shareDown = true;
    files.ReadOverride = path => shareDown
      ? QueueItemMetadata.Placeholder(path)
      : new QueueItemMetadata(files.Titles[path], "Artist", "Album", null, null);
    using var cache = files.NewCache();
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();
    Assert.Equal("a", cache.GetOrSchedule("/m/a.mp3").Title);

    shareDown = false;
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();

    Assert.Equal(2, files.ReadsOf("/m/a.mp3"));
    Assert.Equal("Song A", cache.GetOrSchedule("/m/a.mp3").Title);
  }

  /// <summary>
  /// Review M2: the album-art cache deletes art not saved for 7 days, and the per-request read used to
  /// re-save every queued track's art continuously. A row past MaxAge is re-read when next asked for.
  /// </summary>
  [Fact]
  public async Task ARowPastMaxAge_IsReReadWhenNextAskedFor_AndNotBefore()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "Song A");
    var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
    using var cache = files.NewCache(time);
    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();

    time.Advance(QueueItemMetadataCache.MaxAge - TimeSpan.FromMinutes(1));
    cache.GetOrSchedule("/m/a.mp3");
    await cache.WhenIdleAsync();
    Assert.Equal(1, files.ReadsOf("/m/a.mp3"));

    time.Advance(TimeSpan.FromMinutes(2));
    Assert.Equal("Song A", cache.GetOrSchedule("/m/a.mp3").Title);
    await cache.WhenIdleAsync();
    Assert.Equal(2, files.ReadsOf("/m/a.mp3"));
  }

  /// <summary>
  /// Review M3: every step of the version is a QueueChanged broadcast and a queue re-read per open panel,
  /// so fills are published in batches — every PublishEvery changed rows, and once more when idle.
  /// </summary>
  [Fact]
  public async Task TheVersion_IsPublishedPerBatchOfFills_NotPerFill()
  {
    var files = new FakeFiles();
    int fills = QueueItemMetadataCache.PublishEvery + 4;
    List<string> paths = Enumerable.Range(0, fills).Select(i => $"/m/{i}.mp3").ToList();
    foreach (string p in paths)
    {
      files.Add(p, p);
    }
    using var cache = files.NewCache();
    long start = cache.Version;

    cache.Revalidate(paths);
    await cache.WhenIdleAsync();

    // One at PublishEvery, one for the remaining four when the reader went idle.
    Assert.Equal(start + 2, cache.Version);
  }

  [Fact]
  public async Task TheVersion_IsPublishedWhenTheReaderGoesIdle()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "A");
    using var cache = files.NewCache();
    long start = cache.Version;

    cache.Revalidate(["/m/a.mp3"]);
    await cache.WhenIdleAsync();

    Assert.Equal(start + 1, cache.Version);
  }

  [Fact]
  public async Task TrimTo_DropsPathsOutsideTheQueue_OnlyOnceTheBoundIsExceeded()
  {
    var files = new FakeFiles();
    var paths = Enumerable.Range(0, QueueItemMetadataCache.MaxEntries + 1).Select(i => $"/m/{i}.mp3").ToList();
    foreach (string p in paths)
    {
      files.Add(p, p);
    }
    using var cache = files.NewCache();
    cache.Revalidate(paths.Take(QueueItemMetadataCache.MaxEntries));
    await cache.WhenIdleAsync();

    cache.TrimTo(["/m/0.mp3"]);
    Assert.Equal(QueueItemMetadataCache.MaxEntries, cache.Count);

    cache.Revalidate([paths[^1]]);
    await cache.WhenIdleAsync();
    cache.TrimTo(["/m/0.mp3", paths[^1]]);

    Assert.Equal(2, cache.Count);
    Assert.Equal("/m/0.mp3", cache.GetOrSchedule("/m/0.mp3").Title);
  }

  /// <summary>Review M5: Dispose abandons the backlog; only the read already in progress finishes.</summary>
  [Fact]
  public async Task Dispose_AbandonsTheBacklog_AfterTheReadInProgress()
  {
    var files = new FakeFiles();
    List<string> paths = Enumerable.Range(0, 6).Select(i => $"/m/{i}.mp3").ToList();
    foreach (string p in paths)
    {
      files.Add(p, p);
    }
    using var gate = new ManualResetEventSlim(false);
    using var parked = new ManualResetEventSlim(false);
    files.ReadOverride = path =>
    {
      parked.Set();
      gate.Wait();
      return new QueueItemMetadata(path, "A", "B", null, null);
    };
    var cache = files.NewCache();
    try
    {
      cache.Revalidate(paths);
      Assert.True(parked.Wait(TimeSpan.FromSeconds(30)), "the reader never started");
      cache.Dispose();
    }
    finally
    {
      gate.Set();
    }

    await cache.WhenIdleAsync();
    Assert.Equal(1, files.Reads.Values.Sum());
  }

  [Fact]
  public async Task AfterDispose_NothingIsScheduled_AndWhenIdleCompletes()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "A");
    var cache = files.NewCache();
    cache.Dispose();

    Assert.Equal("a", cache.GetOrSchedule("/m/a.mp3").Title);
    await cache.WhenIdleAsync();

    Assert.Equal(0, files.ReadsOf("/m/a.mp3"));
  }
}
