using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-96: queue rows are filled by a background reader, so a row not read yet carries placeholder
/// metadata. Saving the queue as a playlist stores that metadata for good, so it waits (bounded) for the
/// reader before it reads the queue.
/// </summary>
public class PlaylistsControllerCreateMetadataTests
{
  private readonly Mock<IPlaylistRepository> _repo = new();
  private readonly Mock<IPlayQueue> _queue;
  private readonly PlaylistsController _controller;
  private IReadOnlyList<PlaylistItem>? _stored;
  private bool _metadataSettled;

  public PlaylistsControllerCreateMetadataTests()
  {
    var source = new Mock<IAudioSource>();
    _queue = source.As<IPlayQueue>();
    var manager = new Mock<IAudioManager>();
    manager.Setup(m => m.ActiveSource).Returns(source.Object);
    _controller = new PlaylistsController(_repo.Object, Mock.Of<IAudioEngine>(),
      NullLogger<PlaylistsController>.Instance, manager.Object);

    // The queue answers with real tags only once the wait has let the reader finish — the shape of the
    // race on the appliance, made deterministic.
    _queue.Setup(q => q.GetQueueAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(() => (IReadOnlyList<QueueItem>)
      [
        new QueueItem
        {
          Id = "/mnt/nas/a.mp3",
          Title = _metadataSettled ? "Song A" : "a",
          Artist = _metadataSettled ? "Artist A" : "--",
          Album = "--",
        },
      ]);
    _repo.Setup(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(),
        It.IsAny<IReadOnlyList<PlaylistItem>>(), It.IsAny<CancellationToken>()))
      .Callback<string, string?, IReadOnlyList<PlaylistItem>, CancellationToken>((_, _, items, _) => _stored = items)
      .ReturnsAsync(new Playlist { Id = "p1", Name = "Mix" });
  }

  [Fact]
  public async Task Create_WaitsForTheQueueMetadata_BeforeItReadsTheQueue()
  {
    _queue.Setup(q => q.WaitForQueueMetadataAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
      .Callback(() => _metadataSettled = true)
      .ReturnsAsync(true);

    await _controller.Create(new CreatePlaylistRequest { Name = "Mix" }, CancellationToken.None);

    PlaylistItem item = Assert.Single(_stored!);
    Assert.Equal("Song A", item.Title);
    Assert.Equal("Artist A", item.Artist);
    _queue.Verify(q => q.WaitForQueueMetadataAsync(PlaylistsController.PlaylistMetadataWait, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task Create_StillSaves_WhenTheWaitTimesOut()
  {
    _queue.Setup(q => q.WaitForQueueMetadataAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);

    await _controller.Create(new CreatePlaylistRequest { Name = "Mix" }, CancellationToken.None);

    Assert.Equal("a", Assert.Single(_stored!).Title);
  }
}
