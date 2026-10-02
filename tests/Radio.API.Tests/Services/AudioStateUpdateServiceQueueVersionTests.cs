using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-96: <c>CheckQueueAsync</c> reads <see cref="IPlayQueue.QueueVersion"/> on every pass and the full
/// playlist only when it moved, and a row whose metadata changed (a placeholder filled in by the file
/// player's background reader) is broadcast even though no id, index or state changed.
/// </summary>
/// <remarks>
/// ⚠ No wall clock: each Check*Async is invoked directly by reflection and awaited, matching
/// <see cref="AudioStateUpdateServiceCacheOrderingTests"/>.
/// </remarks>
public class AudioStateUpdateServiceQueueVersionTests
{
  private sealed class RecordingClientProxy : IClientProxy
  {
    public List<string> Methods { get; } = [];

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
      Methods.Add(method);
      cancellationToken.ThrowIfCancellationRequested();
      return Task.CompletedTask;
    }
  }

  private static (AudioStateUpdateService Service, RecordingClientProxy Proxy) CreateService()
  {
    var proxy = new RecordingClientProxy();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy);
    clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);
    var hubContext = new Mock<IHubContext<AudioStateHub>>();
    hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

    var collection = new ServiceCollection();
    collection.AddSingleton(new Mock<IAudioManager>().Object);

    var service = new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContext.Object,
      collection.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
    return (service, proxy);
  }

  private static Task CheckQueueAsync(AudioStateUpdateService svc, IAudioSource source, CancellationToken token)
  {
    MethodInfo? m = typeof(AudioStateUpdateService).GetMethod(
      "CheckQueueAsync", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(m);
    return (Task)m!.Invoke(svc, [source, token])!;
  }

  private static QueueItem Item(string id, int index, string title = "Track") => new()
  {
    Id = id,
    Title = title,
    Artist = "Artist",
    Album = "Album",
    Index = index,
    FullPlaylistIndex = index,
  };

  /// <summary>A File Player double whose version and playlist the test sets, counting full reads.</summary>
  private sealed class QueueDouble
  {
    public long Version { get; set; } = 7;
    public IReadOnlyList<QueueItem> Playlist { get; set; } = [Item("a", 0), Item("b", 1)];
    public int FullReads { get; private set; }
    public IAudioSource Source { get; }

    public QueueDouble()
    {
      var mock = new Mock<IAudioSource>();
      mock.SetupGet(s => s.Type).Returns(AudioSourceType.FilePlayer);
      var queue = mock.As<IPlayQueue>();
      queue.SetupGet(q => q.QueueVersion).Returns(() => Version);
      queue.Setup(q => q.GetFullPlaylistAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          FullReads++;
          return Playlist;
        });
      Source = mock.Object;
    }
  }

  [Fact]
  public async Task AnUnchangedVersion_SkipsTheFullPlaylistRead_OnEveryLaterPass()
  {
    var (svc, proxy) = CreateService();
    var queue = new QueueDouble();

    for (int pass = 0; pass < 20; pass++)
    {
      await CheckQueueAsync(svc, queue.Source, CancellationToken.None);
    }

    Assert.Equal(1, queue.FullReads);
    Assert.Equal(["QueueChanged"], proxy.Methods);
    svc.Dispose();
  }

  [Fact]
  public async Task AMovedVersionWithTheSameRows_ReadsOnce_SendsNothing_AndRemembersTheVersion()
  {
    var (svc, proxy) = CreateService();
    var queue = new QueueDouble();
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    queue.Version = 8;
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    Assert.Equal(2, queue.FullReads);
    Assert.Equal(["QueueChanged"], proxy.Methods);
    svc.Dispose();
  }

  [Fact]
  public async Task AFilledInTitle_IsBroadcast_ThoughNoIdIndexOrStateChanged()
  {
    var (svc, proxy) = CreateService();
    var queue = new QueueDouble { Playlist = [Item("/m/a.mp3", 0, title: "a")] };
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    queue.Version = 8;
    queue.Playlist = [Item("/m/a.mp3", 0, title: "Song A")];
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    Assert.Equal(["QueueChanged", "QueueChanged"], proxy.Methods);
    svc.Dispose();
  }

  [Fact]
  public async Task AFilledInAlbumArt_IsBroadcast()
  {
    var (svc, proxy) = CreateService();
    var queue = new QueueDouble { Playlist = [Item("/m/a.mp3", 0)] };
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    queue.Version = 8;
    queue.Playlist = [Item("/m/a.mp3", 0) with { AlbumArtUrl = "/api/albumart/a.jpg" }];
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    Assert.Equal(["QueueChanged", "QueueChanged"], proxy.Methods);
    svc.Dispose();
  }

  /// <summary>
  /// UI-13's ordering, for the new field: a send that faults must not advance the version, or the next
  /// pass would skip the read and the delta would never be retried.
  /// </summary>
  [Fact]
  public async Task ACancelledSend_DoesNotAdvanceTheVersion()
  {
    var (svc, proxy) = CreateService();
    var queue = new QueueDouble();
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => CheckQueueAsync(svc, queue.Source, cancelled.Token));
    await CheckQueueAsync(svc, queue.Source, CancellationToken.None);

    Assert.Equal(2, queue.FullReads);
    Assert.Equal(["QueueChanged", "QueueChanged"], proxy.Methods);
    svc.Dispose();
  }
}
