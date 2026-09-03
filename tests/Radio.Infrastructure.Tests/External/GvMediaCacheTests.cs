using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

/// <summary>
/// Cache behaviour for ADR-029 D3 §5.3. These are not performance tests: the cache is blackout
/// mitigation, so "a hit never touches the network" and "the cap really deletes" are correctness.
/// </summary>
public sealed class GvMediaCacheTests : IDisposable
{
  private readonly string _dir = Path.Combine(
    Path.GetTempPath(), "gvmedia-tests-" + Guid.NewGuid().ToString("n"));

  private GvMediaCache CreateCache(int capMegabytes, int maxPlaybackSeconds = 300)
  {
    var options = new GvMediaOptions
    {
      CacheDirectory = _dir,
      CacheMaxMegabytes = capMegabytes,
      MaxPlaybackSeconds = maxPlaybackSeconds
    };
    return new GvMediaCache(
      NullLogger<GvMediaCache>.Instance,
      new StaticOptionsMonitor<GvMediaOptions>(options));
  }

  public void Dispose()
  {
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  [Fact]
  public void FileNameFor_HashesRatherThanUsingTheIdVerbatim()
  {
    // A Windows reserved device name is allow-list-clean under ValidateMediaId but is not a
    // creatable filename. Hashing is what makes that irrelevant.
    var name = GvMediaCache.FileNameFor("CON");

    Assert.DoesNotContain("CON", name, StringComparison.OrdinalIgnoreCase);
    Assert.EndsWith(".mp3", name, StringComparison.Ordinal);
    Assert.Equal(36, name.Length); // 32 hex + ".mp3"
  }

  [Fact]
  public void FileNameFor_DistinguishesIdsDifferingOnlyByCase()
  {
    Assert.NotEqual(GvMediaCache.FileNameFor("abc"), GvMediaCache.FileNameFor("ABC"));
  }

  [Fact]
  public async Task WriteThenTryGet_ReturnsTheSamePath()
  {
    var cache = CreateCache(capMegabytes: 50);

    var written = await cache.WriteAsync("vm-1", new byte[1024], CancellationToken.None);
    var found = cache.TryGetPath("vm-1");

    Assert.NotNull(found);
    Assert.Equal(Path.GetFullPath(written), Path.GetFullPath(found!));
  }

  [Fact]
  public async Task TryGetPath_AlwaysMisses_WhenTheCapIsZero()
  {
    // ADR-029 ⟨A1·2⟩: 0 is a no-cache path. The file is still written — playback needs a path —
    // but it is never served back, so replay goes to the network and is exposed to the blackout.
    var cache = CreateCache(capMegabytes: 0);

    var written = await cache.WriteAsync("vm-1", new byte[1024], CancellationToken.None);

    Assert.True(File.Exists(written));
    Assert.Null(cache.TryGetPath("vm-1"));
  }

  [Fact]
  public async Task AFailedWrite_LeavesNoServableEntry_RatherThanAPoisoned0ByteOne()
  {
    // The shape being guarded: writing the final path directly means FileMode.Create truncates
    // first, so a write that dies part-way leaves a 0-byte file at the path TryGetPath serves — and
    // since every hit touches LastWriteTimeUtc, that entry is also the LAST thing eviction would
    // reclaim. It poisons the cache permanently and defends itself while doing it.
    //
    // Squatting a DIRECTORY on the staging path is what makes the failure deterministic: the very
    // first write into it throws, with no clock and nothing to wait for. Under the old code — which
    // never touched ".tmp" — nothing throws at all and this test fails on the missing exception.
    var cache = CreateCache(capMegabytes: 50);
    Directory.CreateDirectory(_dir);
    var finalPath = Path.Combine(_dir, GvMediaCache.FileNameFor("vm-1"));
    Directory.CreateDirectory(finalPath + ".tmp");

    // The exception type is the platform's business (Windows and Linux map "that is a directory"
    // differently); what this test pins is the state left behind.
    await Assert.ThrowsAnyAsync<Exception>(
      () => cache.WriteAsync("vm-1", new byte[1024], CancellationToken.None));

    Assert.False(File.Exists(finalPath));
    Assert.Null(cache.TryGetPath("vm-1"));
  }

  [Fact]
  public async Task ACancelledWrite_LeavesNoServableEntry()
  {
    // PR 3 passes HttpContext.RequestAborted, so a kiosk reload cancels a write in flight. This
    // pins the outer contract only: an already-cancelled token is refused at the write lock, before
    // the filesystem is touched at all. The staging behaviour that makes a part-way failure safe is
    // pinned by the test above.
    var cache = CreateCache(capMegabytes: 50);
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => cache.WriteAsync("vm-1", new byte[1024], cts.Token));

    Assert.Null(cache.TryGetPath("vm-1"));
    Assert.False(File.Exists(Path.Combine(_dir, GvMediaCache.FileNameFor("vm-1"))));
  }

  [Fact]
  public async Task WriteAsync_LeavesNoStagingFileBehindOnSuccess()
  {
    // The staging file is an implementation detail everywhere except here: if it survived a
    // successful write it would count against the cap forever, as an entry nothing can ever serve.
    var cache = CreateCache(capMegabytes: 50);

    var written = await cache.WriteAsync("vm-1", new byte[1024], CancellationToken.None);

    Assert.True(File.Exists(written));
    Assert.Empty(Directory.EnumerateFiles(_dir, "*.tmp"));
  }

  [Fact]
  public void EvictToCap_ReclaimsAnOrphanedStagingFile()
  {
    // The deliberate consequence of enumerating the directory unfiltered: a ".tmp" left by a
    // crashed run is an ordinary eviction candidate, so it cannot occupy the cap forever. Safe
    // because reclamation only ever runs while this provider's write lock is held, so no live
    // ".tmp" can be visible to it — see GvMediaCache.WriteAsync's remarks.
    Directory.CreateDirectory(_dir);
    var orphan = WriteFile("orphan.mp3.tmp", 4096, DateTime.UtcNow.AddHours(-1));

    GvMediaCache.EvictToCap(_dir, maxBytes: 1, protectedPath: null, NullLogger.Instance);

    Assert.False(File.Exists(orphan));
  }

  [Fact]
  public void EvictToCap_DeletesOldestFirstUntilItFits()
  {
    Directory.CreateDirectory(_dir);
    var oldest = WriteFile("a.mp3", 400, DateTime.UtcNow.AddMinutes(-30));
    var middle = WriteFile("b.mp3", 400, DateTime.UtcNow.AddMinutes(-20));
    var newest = WriteFile("c.mp3", 400, DateTime.UtcNow.AddMinutes(-10));

    GvMediaCache.EvictToCap(_dir, maxBytes: 900, protectedPath: null, NullLogger.Instance);

    Assert.False(File.Exists(oldest));
    Assert.True(File.Exists(middle));
    Assert.True(File.Exists(newest));
  }

  [Fact]
  public void EvictToCap_ActuallyDeletesFromDisk()
  {
    // ADR-029 §5.3 is explicit that eviction must really delete, because the cost being accepted is
    // private audio at rest.
    Directory.CreateDirectory(_dir);
    WriteFile("a.mp3", 4096, DateTime.UtcNow.AddHours(-1));

    GvMediaCache.EvictToCap(_dir, maxBytes: 1, protectedPath: null, NullLogger.Instance);

    Assert.Empty(Directory.EnumerateFiles(_dir));
  }

  [Fact]
  public void EvictToCap_NeverDeletesTheProtectedEntry_EvenWhenItAloneExceedsTheCap()
  {
    // The stated, bounded cap violation. Deleting the file the caller is about to play would turn a
    // successful fetch into an unplayable one.
    Directory.CreateDirectory(_dir);
    var inFlight = WriteFile("new.mp3", 4096, DateTime.UtcNow);

    GvMediaCache.EvictToCap(_dir, maxBytes: 1, protectedPath: inFlight, NullLogger.Instance);

    Assert.True(File.Exists(inFlight));
  }

  [Fact]
  public async Task WriteAsync_ProtectsTheNewEntry_WhenCacheDirectoryIsRelative()
  {
    // Task 4's most likely defect: CacheDirectory defaults to a RELATIVE path, and the protection
    // check compares against Path.GetFullPath. If the two sides are rooted differently the file
    // just written is evicted and the fetch silently becomes unplayable.
    //
    // ⚠ The sizes matter and are not arbitrary. The new entry ALONE must exceed the 1 MB cap, so
    // that evicting the older file is not enough to satisfy the cap and the evictor is forced to
    // consider — and skip — the protected entry. With a smaller new entry the loop would break
    // after the first delete and the assertion would pass even with the protection check broken.
    var relative = Path.Combine(".", "gvmedia-rel-" + Guid.NewGuid().ToString("n"));
    try
    {
      var options = new GvMediaOptions { CacheDirectory = relative, CacheMaxMegabytes = 1 };
      var cache = new GvMediaCache(
        NullLogger<GvMediaCache>.Instance, new StaticOptionsMonitor<GvMediaOptions>(options));

      Directory.CreateDirectory(relative);
      File.WriteAllBytes(Path.Combine(relative, "old.mp3"), new byte[900 * 1024]);
      File.SetLastWriteTimeUtc(Path.Combine(relative, "old.mp3"), DateTime.UtcNow.AddHours(-1));

      var written = await cache.WriteAsync("vm-1", new byte[1536 * 1024], CancellationToken.None);

      Assert.True(File.Exists(written));
      // The older entry was still reclaimed — the protection is scoped to the entry in flight.
      Assert.False(File.Exists(Path.Combine(relative, "old.mp3")));
    }
    finally
    {
      if (Directory.Exists(relative))
      {
        Directory.Delete(relative, recursive: true);
      }
    }
  }

  [Fact]
  public void SweepOlderThan_RemovesExpired_AndKeepsRecentAndProtected()
  {
    Directory.CreateDirectory(_dir);
    var stale = WriteFile("stale.mp3", 100, DateTime.UtcNow.AddHours(-2));
    var fresh = WriteFile("fresh.mp3", 100, DateTime.UtcNow);
    var inFlight = WriteFile("inflight.mp3", 100, DateTime.UtcNow.AddHours(-2));

    GvMediaCache.SweepOlderThan(_dir, TimeSpan.FromMinutes(10), inFlight, NullLogger.Instance);

    Assert.False(File.Exists(stale));
    Assert.True(File.Exists(fresh));
    Assert.True(File.Exists(inFlight));
  }

  [Fact]
  public async Task WriteAsync_ProtectsTheNewEntryFromTheSweep_WhenCacheDirectoryIsRelative()
  {
    // The SweepOlderThan twin of the eviction test above. Its protected-path comparison is the one
    // whose left-hand side comes straight from Directory.EnumerateFiles, so it is the side that
    // needs normalising when CacheDirectory is relative — which it is by default.
    //
    // MaxPlaybackSeconds = 1 makes the sweep window max(60s, 2s) = 60s, so a two-hour-old entry is
    // expired and the entry just written is not — but the write path stamps nothing, so the only
    // thing keeping the protected entry alive if the clock were against it is the protection check.
    var relative = Path.Combine(".", "gvmedia-rel-" + Guid.NewGuid().ToString("n"));
    try
    {
      var options = new GvMediaOptions
      {
        CacheDirectory = relative,
        CacheMaxMegabytes = 0,
        MaxPlaybackSeconds = 1
      };
      var cache = new GvMediaCache(
        NullLogger<GvMediaCache>.Instance, new StaticOptionsMonitor<GvMediaOptions>(options));

      Directory.CreateDirectory(relative);
      File.WriteAllBytes(Path.Combine(relative, "old.mp3"), new byte[64]);
      File.SetLastWriteTimeUtc(Path.Combine(relative, "old.mp3"), DateTime.UtcNow.AddHours(-2));

      var written = await cache.WriteAsync("vm-1", new byte[64], CancellationToken.None);
      // Back-date the entry in flight so the sweep would delete it if the protection did not match.
      File.SetLastWriteTimeUtc(written, DateTime.UtcNow.AddHours(-2));
      GvMediaCache.SweepOlderThan(
        relative, TimeSpan.FromSeconds(60), written, NullLogger.Instance);

      Assert.True(File.Exists(written));
      Assert.False(File.Exists(Path.Combine(relative, "old.mp3")));
    }
    finally
    {
      if (Directory.Exists(relative))
      {
        Directory.Delete(relative, recursive: true);
      }
    }
  }

  private string WriteFile(string name, int bytes, DateTime lastWriteUtc)
  {
    var path = Path.Combine(_dir, name);
    File.WriteAllBytes(path, new byte[bytes]);
    File.SetLastWriteTimeUtc(path, lastWriteUtc);
    return Path.GetFullPath(path);
  }
}

/// <summary>
/// Minimal IOptionsMonitor over a fixed value. The repo has no shared helper for this — every test
/// that needs one builds it inline (see ActiveSourceAccessorRegistrationTests' BuildOptionsMonitor).
/// </summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
  public StaticOptionsMonitor(T value) => CurrentValue = value;

  public T CurrentValue { get; }

  public T Get(string? name) => CurrentValue;

  public IDisposable? OnChange(Action<T, string?> listener) => null;
}
