using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
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

    public QueueItemMetadataCache NewCache() => new(Read, Stat, NullLogger.Instance);

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

  [Fact]
  public async Task TheVersion_AdvancesOncePerFill()
  {
    var files = new FakeFiles();
    files.Add("/m/a.mp3", "A");
    files.Add("/m/b.mp3", "B");
    using var cache = files.NewCache();
    long start = cache.Version;

    cache.Revalidate(["/m/a.mp3", "/m/b.mp3"]);
    await cache.WhenIdleAsync();

    Assert.Equal(start + 2, cache.Version);
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
