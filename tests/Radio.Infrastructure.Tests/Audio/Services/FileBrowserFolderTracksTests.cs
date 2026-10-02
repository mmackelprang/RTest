using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// UI-32 "Add folder": <see cref="FileBrowser.ListFolderTracksAsync"/> and the walk behind it — queue order,
/// subfolders on and off, the cap, the allowed-directory guard (lexical and through links), and what is skipped.
/// Every test runs on a real temporary directory tree.
/// </summary>
public sealed class FileBrowserFolderTracksTests : IDisposable
{
  private readonly string _root;
  private readonly string _media;

  public FileBrowserFolderTracksTests()
  {
    _root = Path.Combine(Path.GetTempPath(), $"radio-ui32-{Guid.NewGuid():N}");
    _media = Path.Combine(_root, "media");
    Directory.CreateDirectory(_media);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_root))
      {
        Directory.Delete(_root, true);
      }
    }
    catch (IOException)
    {
      // A link the test could not remove; the temp directory is cleaned eventually.
    }
  }

  // ── Order ──

  [Fact]
  public async Task NonRecursive_ReturnsOnlyTheFolderOwnFiles_InNaturalOrder()
  {
    Touch("Album/10 - Ten.mp3");
    Touch("Album/2 - Two.flac");
    Touch("Album/1 - One.mp3");
    Touch("Album/Bonus/1 - Hidden track.mp3");

    var listing = await Browser().ListFolderTracksAsync("Album", includeSubfolders: false, maxTracks: 500);

    Assert.Equal(new[] { "1 - One.mp3", "2 - Two.flac", "10 - Ten.mp3" }, listing.Paths.Select(Path.GetFileName));
    Assert.Equal(3, listing.TopLevelCount);
    Assert.Equal(1, listing.SubfolderCount);
    Assert.False(listing.IncludeSubfolders);
    Assert.False(listing.Truncated);
  }

  [Fact]
  public async Task Recursive_QueuesTheFolderOwnFilesFirst_ThenEachSubfolderWholeInNaturalOrder()
  {
    Touch("Artist/intro.mp3");
    Touch("Artist/Disc 10/01 - a.mp3");
    Touch("Artist/Disc 2/02 - b.mp3");
    Touch("Artist/Disc 2/01 - a.mp3");
    Touch("Artist/Disc 2/Extras/01 - x.mp3");
    Touch("Artist/Disc 3/01 - a.mp3");

    var listing = await Browser().ListFolderTracksAsync("Artist", includeSubfolders: true, maxTracks: 500);

    var relative = listing.Paths.Select(p => Path.GetRelativePath(Path.Combine(_media, "Artist"), p).Replace('\\', '/'));
    Assert.Equal(new[]
    {
      "intro.mp3",
      "Disc 2/01 - a.mp3",
      "Disc 2/02 - b.mp3",
      "Disc 2/Extras/01 - x.mp3",
      "Disc 3/01 - a.mp3",
      "Disc 10/01 - a.mp3"
    }, relative);
    Assert.Equal(1, listing.TopLevelCount);
    Assert.Equal(3, listing.SubfolderCount);
  }

  [Fact]
  public async Task ArtistFolderWithAlbumsOnly_NonRecursive_IsEmptyButReportsSubfolders()
  {
    // Measured on the box: every artist folder holds 0 top-level files; the tracks are in album folders.
    Touch("ABBA/Gold/01 - Dancing Queen.mp3");
    Touch("ABBA/Arrival/01 - When I Kissed The Teacher.mp3");

    var listing = await Browser().ListFolderTracksAsync("ABBA", includeSubfolders: false, maxTracks: 500);

    Assert.Empty(listing.Paths);
    Assert.Equal(2, listing.SubfolderCount);
  }

  [Fact]
  public async Task ReturnsFullPaths_AndSkipsUnsupportedFiles()
  {
    Touch("Album/01.mp3");
    Touch("Album/cover.jpg");
    Touch("Album/desktop.ini");
    Touch("Album/02.MP3");

    var listing = await Browser().ListFolderTracksAsync("Album", includeSubfolders: false, maxTracks: 500);

    Assert.Equal(2, listing.Paths.Count);
    Assert.All(listing.Paths, p => Assert.True(Path.IsPathRooted(p)));
    Assert.All(listing.Paths, p => Assert.True(File.Exists(p)));
  }

  [Fact]
  public async Task NullPath_ListsTheMediaRoot()
  {
    Touch("root.mp3");

    var listing = await Browser().ListFolderTracksAsync(null, includeSubfolders: false, maxTracks: 500);

    Assert.Equal(Path.TrimEndingDirectorySeparator(_media), listing.FolderPath);
    Assert.Single(listing.Paths);
  }

  // ── Cap ──

  [Fact]
  public async Task OverTheCap_ReturnsTheFirstMaxInQueueOrder_AndMarksTruncated()
  {
    for (var i = 1; i <= 7; i++)
    {
      Touch($"Big/{i:D2}.mp3");
    }

    var listing = await Browser().ListFolderTracksAsync("Big", includeSubfolders: false, maxTracks: 5);

    Assert.True(listing.Truncated);
    Assert.Equal(5, listing.MaxTracks);
    Assert.Equal(new[] { "01.mp3", "02.mp3", "03.mp3", "04.mp3", "05.mp3" }, listing.Paths.Select(Path.GetFileName));
  }

  [Fact]
  public async Task ExactlyAtTheCap_IsNotTruncated()
  {
    for (var i = 1; i <= 5; i++)
    {
      Touch($"Five/{i}.mp3");
    }

    var listing = await Browser().ListFolderTracksAsync("Five", includeSubfolders: false, maxTracks: 5);

    Assert.False(listing.Truncated);
    Assert.Equal(5, listing.Paths.Count);
  }

  [Fact]
  public async Task CapReachedInASubfolder_StopsTheWalkThere()
  {
    Touch("Artist/A/1.mp3");
    Touch("Artist/A/2.mp3");
    Touch("Artist/B/1.mp3");

    var listing = await Browser().ListFolderTracksAsync("Artist", includeSubfolders: true, maxTracks: 2);

    Assert.True(listing.Truncated);
    Assert.All(listing.Paths, p => Assert.Contains($"{Path.DirectorySeparatorChar}A{Path.DirectorySeparatorChar}", p));
  }

  [Fact]
  public async Task CapBelowOne_IsRejected()
  {
    Touch("Album/1.mp3");

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => Browser().ListFolderTracksAsync("Album", false, maxTracks: 0));
  }

  // ── Guard ──

  [Fact]
  public async Task RelativeTraversalOutOfTheRoot_IsRejected()
  {
    TouchAbsolute(Path.Combine(_root, "outside", "secret.mp3"));

    await Assert.ThrowsAsync<UnauthorizedAccessException>(
      () => Browser().ListFolderTracksAsync("../outside", true, 500));
  }

  [Fact]
  public async Task AbsoluteFolderOutsideEveryAllowedDirectory_IsRejected()
  {
    var outside = Path.Combine(_root, "outside");
    TouchAbsolute(Path.Combine(outside, "secret.mp3"));

    await Assert.ThrowsAsync<UnauthorizedAccessException>(
      () => Browser().ListFolderTracksAsync(outside, true, 500));
  }

  [Fact]
  public async Task SiblingSharingTheRootPrefix_IsRejected()
  {
    // "<tmp>/media" must not admit "<tmp>/mediax".
    var sibling = Path.Combine(_root, "mediax");
    TouchAbsolute(Path.Combine(sibling, "a.mp3"));

    await Assert.ThrowsAsync<UnauthorizedAccessException>(
      () => Browser().ListFolderTracksAsync(sibling, true, 500));
  }

  [Fact]
  public async Task AbsoluteFolderInsideABookmark_IsAllowed()
  {
    var nas = Path.Combine(_root, "nas");
    TouchAbsolute(Path.Combine(nas, "Artist", "Album", "1.mp3"));
    var browser = Browser(new FilePlayerOptions
    {
      RootDirectory = "media",
      BookmarkedPaths = [new BookmarkedPath { Path = nas, Label = "NAS", Tag = "music" }]
    });

    var listing = await browser.ListFolderTracksAsync(Path.Combine(nas, "Artist"), true, 500);

    Assert.Single(listing.Paths);
  }

  [Fact]
  public async Task AbsoluteFolderInsideAnAllowedBrowseDirectory_IsAllowed()
  {
    var usb = Path.Combine(_root, "usb");
    TouchAbsolute(Path.Combine(usb, "1.mp3"));
    var browser = Browser(new FilePlayerOptions { RootDirectory = "media", AllowedBrowseDirectories = [usb] });

    var listing = await browser.ListFolderTracksAsync(usb, false, 500);

    Assert.Single(listing.Paths);
  }

  [Fact]
  public async Task MissingFolder_ThrowsDirectoryNotFound()
  {
    await Assert.ThrowsAsync<DirectoryNotFoundException>(
      () => Browser().ListFolderTracksAsync("nope", true, 500));
  }

  [SkippableFact]
  public async Task LinkedFolderInsideTheRootPointingOutside_IsRejected()
  {
    // Passes the lexical check (the link sits inside the root) — only the link-resolving check catches it.
    var outside = Path.Combine(_root, "outside");
    TouchAbsolute(Path.Combine(outside, "secret.mp3"));
    var link = Path.Combine(_media, "escape");
    Skip.IfNot(TryCreateDirectoryLink(link, outside), "this machine cannot create directory symlinks");

    await Assert.ThrowsAsync<UnauthorizedAccessException>(
      () => Browser().ListFolderTracksAsync("escape", true, 500));
  }

  [SkippableFact]
  public async Task FolderBelowALinkedAncestor_PointingOutside_IsRejected()
  {
    var outside = Path.Combine(_root, "outside");
    TouchAbsolute(Path.Combine(outside, "Deep", "secret.mp3"));
    var link = Path.Combine(_media, "escape");
    Skip.IfNot(TryCreateDirectoryLink(link, outside), "this machine cannot create directory symlinks");

    await Assert.ThrowsAsync<UnauthorizedAccessException>(
      () => Browser().ListFolderTracksAsync("escape/Deep", true, 500));
  }

  [SkippableFact]
  public async Task LinksInsideTheWalk_AreNeverFollowedOrQueued_AndAreCounted()
  {
    Touch("Artist/Album/1.mp3");
    var outside = Path.Combine(_root, "outside");
    TouchAbsolute(Path.Combine(outside, "secret.mp3"));
    Skip.IfNot(TryCreateDirectoryLink(Path.Combine(_media, "Artist", "Escape"), outside),
      "this machine cannot create directory symlinks");
    // A cycle: following it would loop forever.
    Skip.IfNot(TryCreateDirectoryLink(Path.Combine(_media, "Artist", "Album", "Loop"), Path.Combine(_media, "Artist")),
      "this machine cannot create directory symlinks");

    var listing = await Browser().ListFolderTracksAsync("Artist", true, 500);

    Assert.Equal(new[] { "1.mp3" }, listing.Paths.Select(Path.GetFileName));
    Assert.Equal(2, listing.SkippedLinks);
  }

  [SkippableFact]
  public async Task RootThatIsItselfALink_IsAllowed_WhenTheChosenFolderResolvesInsideIt()
  {
    // A media root configured through a link (e.g. /opt/radio-console/media -> /mnt/nas) must keep working: the
    // allowed directories are compared by their real paths too.
    var realLibrary = Path.Combine(_root, "real-library");
    TouchAbsolute(Path.Combine(realLibrary, "Artist", "1.mp3"));
    var linkedRoot = Path.Combine(_root, "linked-root");
    Skip.IfNot(TryCreateDirectoryLink(linkedRoot, realLibrary), "this machine cannot create directory symlinks");
    var browser = Browser(new FilePlayerOptions { RootDirectory = linkedRoot });

    var listing = await browser.ListFolderTracksAsync("Artist", false, 500);

    Assert.Single(listing.Paths);
  }

  // ── Skipped entries ──

  [Fact]
  public void UnreadableFiles_AreSkippedAndCounted()
  {
    var good = Touch("Album/1.mp3");
    var bad = Touch("Album/2.mp3");

    var listing = FileBrowser.WalkFolder(Path.Combine(_media, "Album"), false, 500,
      p => p.EndsWith(".mp3"), p => p != bad, CancellationToken.None);

    Assert.Equal(new[] { good }, listing.Paths);
    Assert.Equal(1, listing.SkippedUnreadable);
  }

  [Fact]
  public void HiddenFiles_AreIgnored()
  {
    Touch("Album/1.mp3");
    var hidden = Touch("Album/._1.mp3");
    File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

    var listing = FileBrowser.WalkFolder(Path.Combine(_media, "Album"), false, 500,
      p => p.EndsWith(".mp3"), _ => true, CancellationToken.None);

    Assert.Equal(new[] { "1.mp3" }, listing.Paths.Select(Path.GetFileName));
  }

  [Fact]
  public void CancelledToken_StopsTheWalk()
  {
    Touch("Album/1.mp3");
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.ThrowsAny<OperationCanceledException>(() => FileBrowser.WalkFolder(
      Path.Combine(_media, "Album"), true, 500, _ => true, _ => true, cts.Token));
  }

  [Fact]
  public async Task TheDefaultReadabilityProbe_AcceptsAnOrdinaryFile()
  {
    // The production probe opens the file; an ordinary file must pass it (UnreadableFiles_AreSkippedAndCounted
    // covers the rejection side through the injected probe).
    Touch("Album/1.mp3");

    var listing = await Browser().ListFolderTracksAsync("Album", false, 500);

    Assert.Equal(0, listing.SkippedUnreadable);
    Assert.Single(listing.Paths);
  }

  // ── Review fixes ──

  [Fact]
  public async Task AWalkBlockedInsideOneCall_DoesNotHoldTheCaller_OnceCancelled()
  {
    // A stale CIFS mount or a FIFO named *.mp3 blocks inside open() and never reaches a cancellation check. The
    // caller (the request) must still be released when its token is cancelled. Synchronised on the probe being
    // entered, not on elapsed time; the 10 s bound only fails if the release never happens.
    Touch("Album/1.mp3");
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var browser = Browser();
    browser.ReadProbe = _ =>
    {
      entered.Set();
      release.Wait();
      return true;
    };
    using var cts = new CancellationTokenSource();

    try
    {
      var listing = browser.ListFolderTracksAsync("Album", false, 500, checkReadable: true, cts.Token);
      Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the walk never reached the probe");
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listing.WaitAsync(TimeSpan.FromSeconds(10)));
    }
    finally
    {
      release.Set();
    }
  }

  [Fact]
  public async Task CheckReadableFalse_SkipsTheProbe()
  {
    Touch("Album/1.mp3");
    Touch("Album/2.mp3");
    var browser = Browser();
    var probed = 0;
    browser.ReadProbe = _ =>
    {
      Interlocked.Increment(ref probed);
      return false;
    };

    var counted = await browser.ListFolderTracksAsync("Album", false, 500, checkReadable: false);
    var checkedListing = await browser.ListFolderTracksAsync("Album", false, 500, checkReadable: true);

    Assert.Equal(2, counted.Paths.Count);
    Assert.Equal(0, counted.SkippedUnreadable);
    Assert.Empty(checkedListing.Paths);
    Assert.Equal(2, checkedListing.SkippedUnreadable);
    Assert.Equal(2, probed);
  }

  [SkippableFact]
  public void ResolveRealPath_DotDotAfterALink_ClimbsOutOfTheLinksTarget()
  {
    // media/l -> "x/..", and media/x -> <root>/outside/a/b. The kernel resolves media/l to outside/a/b/.. =
    // outside/a. A lexical normalisation of the target ("x/.." -> "") would wrongly answer "media".
    var deep = Path.Combine(_root, "outside", "a", "b");
    Directory.CreateDirectory(deep);
    Skip.IfNot(TryCreateDirectoryLink(Path.Combine(_media, "x"), deep), "this machine cannot create directory symlinks");
    Skip.IfNot(TryCreateDirectoryLink(Path.Combine(_media, "l"), "x" + Path.DirectorySeparatorChar + ".."),
      "this machine cannot create directory symlinks");

    var real = FileBrowser.ResolveRealPath(Path.Combine(_media, "l"));

    Assert.Equal(Path.Combine(_root, "outside", "a"), real, ignoreCase: OperatingSystem.IsWindows());
  }

  [SkippableFact]
  public async Task OnLinux_TheRealPathCheck_IsCaseSensitive()
  {
    // "media" is the root; "MEDIA" is a different folder on Linux. The older lexical check ignores case and admits
    // it; the real-path check must not.
    Skip.If(OperatingSystem.IsWindows(), "Windows paths are case-insensitive");
    var other = Path.Combine(_root, "MEDIA");
    TouchAbsolute(Path.Combine(other, "secret.mp3"));

    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Browser().ListFolderTracksAsync(other, false, 500));
  }

  // ── Helpers ──

  private FileBrowser Browser(FilePlayerOptions? options = null)
  {
    var monitor = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    monitor.Setup(m => m.CurrentValue).Returns(options ?? new FilePlayerOptions { RootDirectory = "media" });
    return new FileBrowser(NullLogger<FileBrowser>.Instance, monitor.Object, _root);
  }

  private string Touch(string relativeToMedia) =>
    TouchAbsolute(Path.Combine(_media, relativeToMedia.Replace('/', Path.DirectorySeparatorChar)));

  private static string TouchAbsolute(string fullPath)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllBytes(fullPath, new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00 });
    return fullPath;
  }

  private static bool TryCreateDirectoryLink(string link, string target)
  {
    try
    {
      Directory.CreateSymbolicLink(link, target);
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
    {
      return false;
    }
  }
}
