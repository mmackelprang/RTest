using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.IntegrationTests.Audio;

/// <summary>
/// AUD-96, end to end across the two layers it spans: the real <see cref="AudioStateUpdateService"/>
/// queue check polling a real <see cref="FilePlayerAudioSource"/>. With File Player active and its queue
/// unchanged, a pass of the 500 ms poller must make no tag read and no stat — on the appliance that read
/// was ~140 CIFS opens per pass over the box's only network link.
/// </summary>
/// <remarks>
/// Hermetic (no network, no device), so deliberately NOT <c>Category=Integration</c>: CI's filter would
/// skip it. ⚠ No wall clock — the poller's check is invoked directly, and the reader is awaited through
/// <c>WhenQueueMetadataIdleAsync</c> before anything is counted.
/// </remarks>
public sealed class QueuePollerFileAccessTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"QueuePoller_{Guid.NewGuid():N}");
  private readonly ConcurrentDictionary<string, int> _reads = new();
  private int _stats;
  private FilePlayerAudioSource? _source;

  public QueuePollerFileAccessTests() => Directory.CreateDirectory(_dir);

  public void Dispose()
  {
    _source?.WhenQueueMetadataIdleAsync().Wait(TimeSpan.FromSeconds(30));
    try
    {
      Directory.Delete(_dir, recursive: true);
    }
    catch (IOException)
    {
      // Best effort for a temp directory.
    }
  }

  private sealed class CountingProxy : IClientProxy
  {
    public int Sends;

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
      if (method == "QueueChanged")
      {
        Interlocked.Increment(ref Sends);
      }
      return Task.CompletedTask;
    }
  }

  private FilePlayerAudioSource CreateFilePlayer()
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.Setup(o => o.CurrentValue).Returns(new FilePlayerOptions { RootDirectory = "", SupportedExtensions = [".mp3"] });
    var preferences = new Mock<IOptionsMonitor<FilePlayerPreferences>>();
    preferences.Setup(o => o.CurrentValue).Returns(new FilePlayerPreferences());

    _source = new FilePlayerAudioSource(
      new Mock<ILogger<FilePlayerAudioSource>>().Object, options.Object, preferences.Object, _dir)
    {
      QueueMetadataReader = path =>
      {
        _reads.AddOrUpdate(path, 1, (_, n) => n + 1);
        return new QueueItemMetadata("Tagged " + Path.GetFileNameWithoutExtension(path), "Artist", "Album", null, null);
      },
      QueueFileStat = path =>
      {
        Interlocked.Increment(ref _stats);
        return new QueueFileStamp(4, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
      },
    };
    return _source;
  }

  private static (AudioStateUpdateService Service, CountingProxy Proxy) CreatePoller()
  {
    var proxy = new CountingProxy();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy);
    clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);
    var hub = new Mock<IHubContext<AudioStateHub>>();
    hub.SetupGet(h => h.Clients).Returns(clients.Object);

    var services = new ServiceCollection();
    services.AddSingleton(new Mock<IAudioManager>().Object);
    var poller = new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hub.Object,
      services.BuildServiceProvider(),
      new ConfigurationBuilder().Build());
    return (poller, proxy);
  }

  private static Task PollQueueAsync(AudioStateUpdateService poller, IAudioSource source)
  {
    MethodInfo? check = typeof(AudioStateUpdateService).GetMethod(
      "CheckQueueAsync", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(check);
    return (Task)check!.Invoke(poller, [source, CancellationToken.None])!;
  }

  [Fact]
  public async Task WithFilePlayerActiveAndAnUnchangedQueue_APollerPassReadsNoFile()
  {
    for (int i = 0; i < 12; i++)
    {
      File.WriteAllText(Path.Combine(_dir, $"track{i:D2}.mp3"), "test");
    }
    FilePlayerAudioSource source = CreateFilePlayer();
    await source.LoadDirectoryAsync("");
    await source.WhenQueueMetadataIdleAsync();
    var (poller, proxy) = CreatePoller();

    // The first pass broadcasts the (already warm) queue.
    await PollQueueAsync(poller, source);
    await source.WhenQueueMetadataIdleAsync();
    int readsAfterFirstPass = _reads.Values.Sum();
    int statsAfterFirstPass = _stats;
    Assert.Equal(12, readsAfterFirstPass);
    Assert.Equal(1, proxy.Sends);

    // Twenty more passes over an unchanged queue: ten seconds of the real poller.
    for (int pass = 0; pass < 20; pass++)
    {
      await PollQueueAsync(poller, source);
    }
    await source.WhenQueueMetadataIdleAsync();

    Assert.Equal(readsAfterFirstPass, _reads.Values.Sum());
    Assert.Equal(statsAfterFirstPass, _stats);
    Assert.Equal(1, proxy.Sends);
    poller.Dispose();
  }

  [Fact]
  public async Task ATrackAddedWhileFilePlayerPlays_IsBroadcastOnceAsAPlaceholder_ThenOnceFilledIn()
  {
    File.WriteAllText(Path.Combine(_dir, "a.mp3"), "test");
    File.WriteAllText(Path.Combine(_dir, "b.mp3"), "test");
    FilePlayerAudioSource source = CreateFilePlayer();
    await source.LoadPlaylistAsync(["a.mp3"]);
    await source.WhenQueueMetadataIdleAsync();
    var (poller, proxy) = CreatePoller();
    await PollQueueAsync(poller, source);
    Assert.Equal(1, proxy.Sends);

    // Hold the reader so the poller sees the new row before its tags are read.
    using var gate = new ManualResetEventSlim(false);
    Func<string, QueueItemMetadata> real = source.QueueMetadataReader;
    source.QueueMetadataReader = path =>
    {
      gate.Wait();
      return real(path);
    };
    await source.AddToQueueAsync("b.mp3");
    await PollQueueAsync(poller, source);
    Assert.Equal(2, proxy.Sends);
    Assert.Equal("b", (await source.GetFullPlaylistAsync())[1].Title);

    gate.Set();
    await source.WhenQueueMetadataIdleAsync();
    await PollQueueAsync(poller, source);
    await PollQueueAsync(poller, source);

    Assert.Equal(3, proxy.Sends);
    Assert.Equal("Tagged b", (await source.GetFullPlaylistAsync())[1].Title);
    poller.Dispose();
  }
}
