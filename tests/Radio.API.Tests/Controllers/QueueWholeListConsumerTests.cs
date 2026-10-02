using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-98: the API consumers that must see the WHOLE File Player list — played tracks included — rather than
/// <see cref="IPlayQueue.GetQueueAsync"/>'s current + upcoming. The two reads are given different contents
/// here, so a consumer reading the wrong one fails.
/// </summary>
public class QueueWholeListConsumerTests
{
  private readonly Mock<IPlayQueue> _queue;
  private readonly Mock<IAudioManager> _manager = new();
  private readonly Mock<IPlaylistRepository> _repo = new();

  private static readonly QueueItem[] WholeList =
  [
    Item("/music/a.mp3", "A", QueueItemState.Played),
    Item("/music/b.mp3", "B", QueueItemState.Played),
    Item("/music/c.mp3", "C", QueueItemState.Current),
    Item("/music/d.mp3", "D", QueueItemState.Upcoming),
  ];

  public QueueWholeListConsumerTests()
  {
    var source = new Mock<IAudioSource>();
    _queue = source.As<IPlayQueue>();
    _queue.Setup(q => q.GetFullPlaylistAsync(It.IsAny<CancellationToken>())).ReturnsAsync(WholeList);
    _queue.Setup(q => q.GetQueueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(WholeList.Skip(2).ToList());
    _manager.Setup(m => m.ActiveSource).Returns(source.Object);
  }

  private static QueueItem Item(string path, string title, QueueItemState state) => new()
  {
    Id = path,
    Title = title,
    Artist = "Artist",
    Album = "Album",
    Duration = TimeSpan.FromSeconds(180),
    State = state,
    IsCurrent = state == QueueItemState.Current,
  };

  [Fact]
  public async Task SaveAsPlaylist_StoresEveryTrack_PlayedOnesFirst_InTheOrderThePanelShows()
  {
    IReadOnlyList<PlaylistItem>? stored = null;
    _repo
      .Setup(r => r.CreateAsync("Mix", null, It.IsAny<IReadOnlyList<PlaylistItem>>(), It.IsAny<CancellationToken>()))
      .Callback<string, string?, IReadOnlyList<PlaylistItem>, CancellationToken>((_, _, items, _) => stored = items)
      .ReturnsAsync(new Playlist { Id = "p1", Name = "Mix", ItemCount = 4 });
    var controller = new PlaylistsController(
      _repo.Object, Mock.Of<IAudioEngine>(), NullLogger<PlaylistsController>.Instance, _manager.Object);

    var result = await controller.Create(new CreatePlaylistRequest { Name = "Mix" }, CancellationToken.None);

    Assert.IsType<CreatedAtActionResult>(result.Result);
    Assert.NotNull(stored);
    Assert.Equal(WholeList.Select(i => i.Id), stored!.Select(i => i.FilePath));
    Assert.Equal(new[] { 0, 1, 2, 3 }, stored.Select(i => i.Position));
    Assert.Equal(new[] { "A", "B", "C", "D" }, stored.Select(i => i.Title));
  }

  [Fact]
  public async Task Contains_FindsATrackThatHasAlreadyPlayed()
  {
    var controller = new QueueController(
      NullLogger<QueueController>.Instance, Mock.Of<IAudioEngine>(), _manager.Object);

    var result = await controller.ContainsTrack("/music/a.mp3");

    Assert.True(Assert.IsType<OkObjectResult>(result.Result).Value as bool?);
  }
}
