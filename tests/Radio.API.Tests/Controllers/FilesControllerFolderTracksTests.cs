using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Xunit;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// UI-32: <c>GET /api/files/folder-tracks</c> — the listing behind "Add folder". Driven through the controller with a
/// real <see cref="FileBrowser"/> over a temporary tree, so the guard under test is the one that ships.
/// </summary>
public sealed class FilesControllerFolderTracksTests : IDisposable
{
  private readonly string _root;
  private readonly string _media;
  private FilePlayerOptions _options;

  public FilesControllerFolderTracksTests()
  {
    _root = Path.Combine(Path.GetTempPath(), $"radio-ui32-api-{Guid.NewGuid():N}");
    _media = Path.Combine(_root, "media");
    Directory.CreateDirectory(_media);
    // An absolute root: the controller's own IsPathAllowed resolves a relative root against the process's current
    // directory, the FileBrowser against its rootDir — an absolute root means both agree, as on the box.
    _options = new FilePlayerOptions { RootDirectory = _media };
  }

  public void Dispose()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, true);
    }
  }

  [Fact]
  public async Task Recursive_ReturnsEveryTrackInQueueOrder()
  {
    Touch("ABBA/Gold/02 - b.mp3");
    Touch("ABBA/Gold/01 - a.mp3");
    Touch("ABBA/Arrival/01 - c.mp3");

    var dto = Ok(await Controller().ListFolderTracks(Path.Combine(_media, "ABBA"), includeSubfolders: true));

    Assert.Equal("ABBA", dto.FolderName);
    Assert.True(dto.IncludeSubfolders);
    Assert.Equal(new[] { "01 - c.mp3", "01 - a.mp3", "02 - b.mp3" }, dto.Paths.Select(Path.GetFileName));
    Assert.Equal(0, dto.TopLevelCount);
    Assert.Equal(2, dto.SubfolderCount);
  }

  [Fact]
  public async Task NonRecursive_ReturnsOnlyTopLevel()
  {
    Touch("Album/1.mp3");
    Touch("Album/Bonus/2.mp3");

    var dto = Ok(await Controller().ListFolderTracks(Path.Combine(_media, "Album"), includeSubfolders: false));

    Assert.Equal(new[] { "1.mp3" }, dto.Paths.Select(Path.GetFileName));
    Assert.Equal(1, dto.SubfolderCount);
    Assert.False(dto.IncludeSubfolders);
  }

  [Fact]
  public async Task RelativePath_ResolvesUnderTheMediaRoot()
  {
    Touch("Album/1.mp3");

    var dto = Ok(await Controller().ListFolderTracks("Album", includeSubfolders: false));

    Assert.Single(dto.Paths);
    Assert.StartsWith(_media, dto.Paths[0]);
  }

  [Fact]
  public async Task EmptyPath_ListsTheMediaRoot()
  {
    Touch("root.mp3");

    var dto = Ok(await Controller().ListFolderTracks(null, includeSubfolders: false));

    Assert.Equal("media", dto.FolderName);
    Assert.Single(dto.Paths);
  }

  [Fact]
  public async Task OverTheConfiguredCap_ReturnsTheFirstMax_AndTruncated()
  {
    for (var i = 1; i <= 4; i++)
    {
      Touch($"Big/{i}.mp3");
    }
    _options.MaxFolderTracks = 3;

    var dto = Ok(await Controller().ListFolderTracks(Path.Combine(_media, "Big"), includeSubfolders: true));

    Assert.True(dto.Truncated);
    Assert.Equal(3, dto.MaxTracks);
    Assert.Equal(new[] { "1.mp3", "2.mp3", "3.mp3" }, dto.Paths.Select(Path.GetFileName));
  }

  [Theory]
  [InlineData("../outside")]
  [InlineData("Album/../../outside")]
  public async Task RelativeTraversal_Is400(string path)
  {
    Touch("Album/1.mp3");
    TouchAbsolute(Path.Combine(_root, "outside", "secret.mp3"));

    var result = await Controller().ListFolderTracks(path, includeSubfolders: true);

    Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
  }

  [Fact]
  public async Task AbsoluteTraversal_Is400()
  {
    TouchAbsolute(Path.Combine(_root, "outside", "secret.mp3"));
    var sneaky = _media + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "outside";

    var result = await Controller().ListFolderTracks(sneaky, includeSubfolders: true);

    Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
  }

  [Fact]
  public async Task AbsoluteOutOfRoot_Is400()
  {
    var outside = Path.Combine(_root, "outside");
    TouchAbsolute(Path.Combine(outside, "secret.mp3"));

    var result = await Controller().ListFolderTracks(outside, includeSubfolders: true);

    Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
  }

  [Fact]
  public async Task ABookmarkOutsideTheRoot_IsAllowed()
  {
    var nas = Path.Combine(_root, "nas");
    TouchAbsolute(Path.Combine(nas, "Artist", "1.mp3"));
    _options.BookmarkedPaths = [new BookmarkedPath { Path = nas, Label = "NAS", Tag = "music" }];

    var dto = Ok(await Controller().ListFolderTracks(Path.Combine(nas, "Artist"), includeSubfolders: false));

    Assert.Single(dto.Paths);
  }

  [Fact]
  public async Task MissingFolder_Is404()
  {
    var result = await Controller().ListFolderTracks(Path.Combine(_media, "nope"), includeSubfolders: true);

    Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
  }

  [Fact]
  public async Task WalkPastTheTimeout_Is504_WithAMessage()
  {
    // A browser that only finishes when cancelled: the controller's timeout is what cancels it.
    var slow = new Mock<IFileBrowser>();
    slow.Setup(b => b.ListFolderTracksAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .Returns(async (string? _, bool _, int _, CancellationToken ct) =>
      {
        await Task.Delay(Timeout.Infinite, ct);
        return new FolderTrackListing { FolderPath = "never" };
      });
    _options.FolderScanTimeoutSeconds = 1;

    var result = await Controller(slow.Object).ListFolderTracks("Album", includeSubfolders: true);

    var obj = Assert.IsType<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status504GatewayTimeout, obj.StatusCode);
    Assert.Contains("took longer than 1 seconds", obj.Value!.ToString());
  }

  [Fact]
  public async Task ClientDisconnect_IsNotReportedAsATimeout()
  {
    var slow = new Mock<IFileBrowser>();
    slow.Setup(b => b.ListFolderTracksAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .Returns(async (string? _, bool _, int _, CancellationToken ct) =>
      {
        await Task.Delay(Timeout.Infinite, ct);
        return new FolderTrackListing { FolderPath = "never" };
      });
    using var aborted = new CancellationTokenSource();
    aborted.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => Controller(slow.Object).ListFolderTracks("Album", true, aborted.Token));
  }

  [Theory]
  [InlineData("/mnt/nas_media/Music/ABBA", "ABBA")]
  [InlineData("/mnt/nas_media/Music/ABBA/", "ABBA")]
  [InlineData("/", "/")]
  public void FolderDisplayName_IsTheLastSegment(string path, string expected)
  {
    Assert.Equal(expected, FilesController.FolderDisplayName(path));
  }

  private FilesController Controller(IFileBrowser? browser = null)
  {
    var monitor = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    monitor.Setup(m => m.CurrentValue).Returns(() => _options);
    browser ??= new FileBrowser(NullLogger<FileBrowser>.Instance, monitor.Object, _root);
    return new FilesController(
      NullLogger<FilesController>.Instance,
      browser,
      Mock.Of<IAudioEngine>(),
      monitor.Object);
  }

  private static FolderTracksDto Ok(IActionResult result)
  {
    var ok = Assert.IsType<OkObjectResult>(result);
    return Assert.IsType<FolderTracksDto>(ok.Value);
  }

  private static int? StatusOf(IActionResult result) => result switch
  {
    ObjectResult o => o.StatusCode,
    StatusCodeResult s => s.StatusCode,
    _ => null
  };

  private void Touch(string relativeToMedia) =>
    TouchAbsolute(Path.Combine(_media, relativeToMedia.Replace('/', Path.DirectorySeparatorChar)));

  private static void TouchAbsolute(string fullPath)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllBytes(fullPath, new byte[] { 0x49, 0x44, 0x33, 0x03 });
  }
}
