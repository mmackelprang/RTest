using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-96 at the file player: queue rows are served from the metadata cache, each queued path is read
/// once, and <see cref="IPlayQueue.QueueVersion"/> is a change signal that touches no file.
/// </summary>
/// <remarks>
/// The reader and stat seams count every call, so "the poller touches no file" is asserted as a count of
/// zero rather than inferred from timing. ⚠ No wall clock: each assertion about the reader follows
/// <c>WhenQueueMetadataIdleAsync</c>, a completion rendezvous on the reader.
/// </remarks>
public sealed class FilePlayerQueueMetadataTests : IDisposable
{
  private readonly string _dir;
  private readonly FilePlayerPreferences _preferences = new();
  private readonly List<FilePlayerAudioSource> _sources = new();
  private readonly ConcurrentDictionary<string, int> _reads = new();
  private readonly ConcurrentDictionary<string, int> _stats = new();
  private readonly ConcurrentDictionary<string, long> _lengths = new();

  public FilePlayerQueueMetadataTests()
  {
    _dir = Path.Combine(Path.GetTempPath(), $"FilePlayerQueueMetadata_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_dir);
  }

  public void Dispose()
  {
    foreach (FilePlayerAudioSource source in _sources)
    {
      if (!source.WhenQueueMetadataIdleAsync().Wait(TimeSpan.FromSeconds(30)))
      {
        throw new TimeoutException("The queue metadata reader did not go idle");
      }
    }
    try
    {
      Directory.Delete(_dir, recursive: true);
    }
    catch (IOException)
    {
      // The audio engine may still hold the current file on some hosts; a temp dir is not worth failing for.
    }
  }

  private int TotalReads => _reads.Values.Sum();

  private int TotalStats => _stats.Values.Sum();

  private FilePlayerAudioSource CreateSource()
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.Setup(o => o.CurrentValue).Returns(new FilePlayerOptions
    {
      RootDirectory = "",
      SupportedExtensions = [".mp3"]
    });
    var preferences = new Mock<IOptionsMonitor<FilePlayerPreferences>>();
    preferences.Setup(o => o.CurrentValue).Returns(_preferences);

    var source = new FilePlayerAudioSource(
      new Mock<ILogger<FilePlayerAudioSource>>().Object,
      options.Object,
      preferences.Object,
      _dir)
    {
      QueueMetadataReader = path =>
      {
        _reads.AddOrUpdate(path, 1, (_, n) => n + 1);
        string name = Path.GetFileNameWithoutExtension(path);
        return new QueueItemMetadata($"Tagged {name}", "Tagged Artist", "Tagged Album", TimeSpan.FromSeconds(200), $"/api/albumart/{name}.jpg");
      },
      QueueFileStat = path =>
      {
        _stats.AddOrUpdate(path, 1, (_, n) => n + 1);
        return File.Exists(path)
          ? new QueueFileStamp(_lengths.GetValueOrDefault(path, 4), new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc))
          : null;
      },
    };
    _sources.Add(source);
    return source;
  }

  private string CreateFiles(params string[] names)
  {
    foreach (string name in names)
    {
      File.WriteAllText(Path.Combine(_dir, name), "test");
    }
    return _dir;
  }

  private string FullPath(string name) => Path.Combine(_dir, name);

  [Fact]
  public async Task EachQueuedPath_IsReadOnce_HoweverOftenTheQueueIsRead()
  {
    CreateFiles("a.mp3", "b.mp3", "c.mp3");
    var source = CreateSource();
    await source.LoadDirectoryAsync("");
    await source.WhenQueueMetadataIdleAsync();

    for (int i = 0; i < 25; i++)
    {
      await source.GetFullPlaylistAsync();
      await source.GetQueueAsync();
    }
    await source.WhenQueueMetadataIdleAsync();

    Assert.Equal(3, _reads.Count);
    Assert.All(_reads.Values, n => Assert.Equal(1, n));
  }

  [Fact]
  public async Task QueueRows_CarryTheCachedMetadata()
  {
    CreateFiles("a.mp3", "b.mp3");
    var source = CreateSource();
    await source.LoadDirectoryAsync("");
    await source.WhenQueueMetadataIdleAsync();

    IReadOnlyList<QueueItem> full = await source.GetFullPlaylistAsync();

    QueueItem b = full.Single(i => i.Id == FullPath("b.mp3"));
    Assert.Equal("Tagged b", b.Title);
    Assert.Equal("Tagged Artist", b.Artist);
    Assert.Equal("Tagged Album", b.Album);
    Assert.Equal(TimeSpan.FromSeconds(200), b.Duration);
    Assert.Equal("/api/albumart/b.jpg", b.AlbumArtUrl);
  }

  /// <summary>
  /// ⭐ The poller's whole job, minus the hub: read QueueVersion every pass, and the full playlist only
  /// when it moved. With the queue unchanged, a hundred passes make no read and no stat.
  /// </summary>
  [Fact]
  public async Task WithAnUnchangedQueue_ThePollersSignalTouchesNoFile()
  {
    CreateFiles("a.mp3", "b.mp3", "c.mp3");
    var source = CreateSource();
    await source.LoadDirectoryAsync("");
    await source.WhenQueueMetadataIdleAsync();
    long version = source.QueueVersion;
    int reads = TotalReads;
    int stats = TotalStats;

    for (int pass = 0; pass < 100; pass++)
    {
      Assert.Equal(version, source.QueueVersion);
    }
    await source.WhenQueueMetadataIdleAsync();

    Assert.Equal(reads, TotalReads);
    Assert.Equal(stats, TotalStats);
  }

  [Fact]
  public async Task TheVersion_MovesWhenARowIsFilled()
  {
    CreateFiles("a.mp3");
    var source = CreateSource();
    using var gate = new ManualResetEventSlim(false);
    Func<string, QueueItemMetadata> real = source.QueueMetadataReader;
    source.QueueMetadataReader = path =>
    {
      gate.Wait();
      return real(path);
    };

    await source.LoadDirectoryAsync("");
    // Before the read: the row is the placeholder, served without waiting for the file.
    IReadOnlyList<QueueItem> before = await source.GetFullPlaylistAsync();
    Assert.Equal("a", Assert.Single(before).Title);
    Assert.Equal("--", before[0].Artist);
    long versionBefore = source.QueueVersion;

    gate.Set();
    await source.WhenQueueMetadataIdleAsync();

    Assert.NotEqual(versionBefore, source.QueueVersion);
    Assert.Equal("Tagged a", Assert.Single(await source.GetFullPlaylistAsync()).Title);
  }

  [Fact]
  public async Task TheVersion_MovesWhenTheQueueChanges()
  {
    CreateFiles("a.mp3", "b.mp3", "c.mp3", "d.mp3");
    var source = CreateSource();
    await source.LoadPlaylistAsync(["a.mp3", "b.mp3", "c.mp3"]);
    await source.WhenQueueMetadataIdleAsync();
    var seen = new HashSet<long> { source.QueueVersion };

    await source.AddToQueueAsync("d.mp3");
    await source.WhenQueueMetadataIdleAsync();
    Assert.True(seen.Add(source.QueueVersion), "adding a track must move the version");

    await source.MoveQueueItemAsync(1, 2);
    Assert.True(seen.Add(source.QueueVersion), "reordering must move the version");

    await source.RemoveFromQueueAsync(3);
    Assert.True(seen.Add(source.QueueVersion), "removing a track must move the version");
  }

  [Fact]
  public async Task ReAddingAChangedFile_ReReadsIt_AndAnUnchangedOne_DoesNot()
  {
    CreateFiles("a.mp3", "b.mp3");
    var source = CreateSource();
    await source.LoadPlaylistAsync(["a.mp3"]);
    await source.WhenQueueMetadataIdleAsync();

    await source.AddToQueueAsync("a.mp3");
    await source.WhenQueueMetadataIdleAsync();
    Assert.Equal(1, _reads[FullPath("a.mp3")]);

    _lengths[FullPath("a.mp3")] = 999; // re-tagged in place
    await source.AddToQueueAsync("a.mp3");
    await source.WhenQueueMetadataIdleAsync();
    Assert.Equal(2, _reads[FullPath("a.mp3")]);
  }

  [Fact]
  public async Task RestoringAQueue_WarmsEveryRestoredRow()
  {
    CreateFiles("a.mp3", "b.mp3", "c.mp3");
    _preferences.QueueItems = [FullPath("a.mp3"), FullPath("b.mp3"), FullPath("c.mp3")];
    _preferences.CurrentQueueIndex = 1;
    var source = CreateSource();

    await source.InitializeAsync();
    await source.WhenQueueMetadataIdleAsync();

    Assert.Equal(3, _reads.Count);
    IReadOnlyList<QueueItem> full = await source.GetFullPlaylistAsync();
    Assert.All(full, item => Assert.StartsWith("Tagged ", item.Title));
    await source.WhenQueueMetadataIdleAsync();
    Assert.Equal(3, TotalReads);
  }
}
