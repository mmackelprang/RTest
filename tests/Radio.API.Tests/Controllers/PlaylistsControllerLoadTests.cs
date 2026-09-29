using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Xunit;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// <c>POST /api/playlists/{id}/load</c> checks the files before touching the queue. A playlist whose files had all
/// moved used to clear the queue and then add nothing.
/// </summary>
public class PlaylistsControllerLoadTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"playlist-load-{Guid.NewGuid()}");
  private readonly Mock<IPlaylistRepository> _repo = new();
  private readonly Mock<IPlayQueue> _queue;
  private readonly PlaylistsController _controller;

  public PlaylistsControllerLoadTests()
  {
    Directory.CreateDirectory(_dir);
    var source = new Mock<IAudioSource>();
    _queue = source.As<IPlayQueue>();
    var manager = new Mock<IAudioManager>();
    manager.Setup(m => m.ActiveSource).Returns(source.Object);
    _controller = new PlaylistsController(_repo.Object, Mock.Of<IAudioEngine>(),
      NullLogger<PlaylistsController>.Instance, manager.Object);
  }

  public void Dispose() => Directory.Delete(_dir, true);

  [Fact]
  public async Task Load_AllFilesMissing_LeavesTheQueueUntouchedAndReportsZeroLoaded()
  {
    SetupPlaylist("/mnt/nas/music/a.mp3", "/mnt/nas/music/b.mp3");

    var body = await LoadAsync();

    _queue.Verify(q => q.ClearQueueAsync(It.IsAny<CancellationToken>()), Times.Never);
    _queue.Verify(q => q.AddToQueueAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    Assert.Equal(0, body.GetProperty("loaded").GetInt32());
    Assert.Equal(2, body.GetProperty("skipped").GetInt32());
  }

  [Fact]
  public async Task Load_SomeFilesMissing_ReplacesTheQueueWithTheFilesThatExist()
  {
    var present = Path.Combine(_dir, "present.mp3");
    File.WriteAllBytes(present, [0]);
    SetupPlaylist("/mnt/nas/music/gone.mp3", present);

    var body = await LoadAsync();

    _queue.Verify(q => q.ClearQueueAsync(It.IsAny<CancellationToken>()), Times.Once);
    _queue.Verify(q => q.AddToQueueAsync(present, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    _queue.Verify(q => q.AddToQueueAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal(1, body.GetProperty("loaded").GetInt32());
    Assert.Equal(1, body.GetProperty("skipped").GetInt32());
  }

  private void SetupPlaylist(params string[] paths)
  {
    var playlist = new Playlist { Id = "p1", Name = "Test", ItemCount = paths.Length };
    var items = paths.Select((p, i) => new PlaylistItem { Id = $"i{i}", PlaylistId = "p1", Position = i, FilePath = p }).ToList();
    _repo.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync((playlist, items));
  }

  private async Task<JsonElement> LoadAsync()
  {
    var result = Assert.IsType<OkObjectResult>(await _controller.Load("p1", CancellationToken.None));
    return JsonSerializer.SerializeToElement(result.Value);
  }
}
