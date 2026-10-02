using System.Net;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radzen.Blazor;
using Radio.Web.Components.Dialogs;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Dialogs;

/// <summary>
/// UI-32: the file browser's folder mode, behind "Add folder". It lists only folders, counts what the shown folder
/// would add, offers "Include subfolders" (on by default), queues the tracks in order in batches, and closes with
/// a <see cref="FolderAddResult"/>. Driven against a canned API (<see cref="RoutedApiHandler"/>).
/// </summary>
public class FileBrowserDialogFolderModeTests : TestContext
{
  private const string Library = "/mnt/nas_media/Music";
  private const string Abba = "/mnt/nas_media/Music/ABBA";
  private readonly RoutedApiHandler _api = new();

  public FileBrowserDialogFolderModeTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();
    Services.AddSingleton(_ => new FileApiService(NewClient(), NullLogger<FileApiService>.Instance));
    Services.AddSingleton(_ => new ConfigurationApiService(NewClient(), NullLogger<ConfigurationApiService>.Instance));

    _api.Get("/api/files/bookmarks", new[] { new BookmarkDto(Library, "NAS Music Library", "music", true) });
    _api.Get("/api/files/drives", Array.Empty<DriveInfoDto>());
  }

  private HttpClient NewClient() =>
    new(_api, disposeHandler: false) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

  /// <summary>The library root lists ABBA and one loose file; any later listing (ABBA itself) lists two albums.</summary>
  private void StubBrowsing()
  {
    _api.Get("/api/files", new FileListDto(Library, new List<FileItemDto>
    {
      new("ABBA", Abba, true, null, null, null, null),
      new("loose.mp3", Library + "/loose.mp3", false, 1, "3:00", null, null),
    }));
    _api.OnRequest(HttpMethod.Get, "/api/files", () => _api.Get("/api/files", new FileListDto(Abba, new List<FileItemDto>
    {
      new("Arrival", Abba + "/Arrival", true, null, null, null, null),
      new("Gold", Abba + "/Gold", true, null, null, null, null),
    })));
  }

  private static FolderTracksDto Listing(string folder, string name, int topLevel, int total, bool truncated = false,
    int subfolders = 2, int skippedUnreadable = 0) => new()
  {
    FolderPath = folder,
    FolderName = name,
    IncludeSubfolders = true,
    Paths = Enumerable.Range(1, total).Select(i => $"{folder}/{(i <= topLevel ? "" : "Gold/")}{i:D2}.mp3").ToList(),
    TopLevelCount = topLevel,
    SubfolderCount = subfolders,
    Truncated = truncated,
    MaxTracks = 500,
    SkippedUnreadable = skippedUnreadable,
  };

  private void StubQueueAccepts() =>
    _api.Route(HttpMethod.Post, "/api/files/queue", HttpStatusCode.OK,
      JsonSerializer.Serialize(new { success = true, message = "ok", addedCount = 20, failedCount = 0, failedPaths = Array.Empty<string>() }));

  private IRenderedComponent<FileBrowserDialog> RenderFolderMode() =>
    RenderComponent<FileBrowserDialog>(p => p
      .Add(d => d.SelectFolder, true)
      .Add(d => d.PreferredBookmarkTag, "music"));

  private static string AddButtonText(IRenderedComponent<FileBrowserDialog> cut) =>
    cut.Find("button.folder-pick-add").TextContent.Trim();

  private static bool AddDisabled(IRenderedComponent<FileBrowserDialog> cut) =>
    cut.Find("button.folder-pick-add").HasAttribute("disabled");

  private void OpenAbba(IRenderedComponent<FileBrowserDialog> cut)
  {
    cut.WaitForAssertion(() => cut.FindAll(".file-item").Count.Should().BeGreaterThan(0), TimeSpan.FromSeconds(10));
    cut.FindAll(".file-item").Single(e => e.TextContent.Contains("ABBA")).Click();
    cut.WaitForAssertion(() => cut.Markup.Should().Contain("Gold"), TimeSpan.FromSeconds(10));
  }

  [Fact]
  public void ListsOnlyFolders_AndAsksForTheShownFolderWithSubfolders()
  {
    StubBrowsing();
    _api.Get("/api/files/folder-tracks", Listing(Library, "Music", topLevel: 1, total: 120));

    var cut = RenderFolderMode();

    cut.WaitForAssertion(() => cut.FindAll(".file-item").Count.Should().Be(1), TimeSpan.FromSeconds(10));
    cut.Find(".file-item").TextContent.Should().Contain("ABBA");
    cut.Markup.Should().NotContain("loose.mp3", "file rows would look tappable in folder mode and do nothing");
    cut.FindAll("button[title='Add to Queue']").Count.Should().Be(0);

    cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Path == "/api/files/folder-tracks"),
      TimeSpan.FromSeconds(10));
    var query = Uri.UnescapeDataString(_api.Requests.Last(r => r.Path == "/api/files/folder-tracks").Query);
    query.Should().Contain("includeSubfolders=true").And.Contain($"path={Library}");
  }

  [Fact]
  public void AtTheLibraryRoot_TheButtonSaysAddThisFolder_WithTheCount()
  {
    StubBrowsing();
    _api.Get("/api/files/folder-tracks", Listing(Library, "Music", topLevel: 1, total: 120));

    var cut = RenderFolderMode();

    cut.WaitForAssertion(() => AddButtonText(cut).Should().Be("Add this folder (120)"), TimeSpan.FromSeconds(10));
    cut.Find(".folder-pick-summary").TextContent.Should().Be("1 track here · 120 including subfolders");
  }

  [Fact]
  public void InsideAFolder_TheButtonNamesIt_AndTheSwitchStartsOn()
  {
    StubBrowsing();
    _api.Get("/api/files/folder-tracks", Listing(Abba, "ABBA", topLevel: 0, total: 60));
    var cut = RenderFolderMode();

    OpenAbba(cut);

    cut.WaitForAssertion(() => AddButtonText(cut).Should().Be("Add \"ABBA\" (60)"), TimeSpan.FromSeconds(10));
    cut.Find(".folder-pick-summary").TextContent.Should().Be("0 tracks here · 60 including subfolders");
    AddDisabled(cut).Should().BeFalse();
    var lastQuery = Uri.UnescapeDataString(_api.Requests.Last(r => r.Path == "/api/files/folder-tracks").Query);
    lastQuery.Should().Contain($"path={Abba}");
  }

  [Fact]
  public void SubfoldersOff_WithNothingAtTheTopLevel_SaysSo_AndDisablesAdd()
  {
    StubBrowsing();
    _api.Get("/api/files/folder-tracks", Listing(Abba, "ABBA", topLevel: 0, total: 60));
    var cut = RenderFolderMode();
    OpenAbba(cut);
    cut.WaitForAssertion(() => AddButtonText(cut).Should().Be("Add \"ABBA\" (60)"), TimeSpan.FromSeconds(10));

    cut.Find(".folder-pick-switch .rz-switch").Click();

    cut.Find(".folder-pick-summary").TextContent.Should()
      .Be("No tracks directly in ABBA. Turn on Include subfolders to add 60.");
    AddDisabled(cut).Should().BeTrue();
  }

  [Fact]
  public async Task SubfoldersOff_AddsOnlyTheTopLevelTracks()
  {
    StubBrowsing();
    StubQueueAccepts();
    _api.Get("/api/files/folder-tracks", Listing(Abba, "ABBA", topLevel: 3, total: 60));
    var (cut, closed) = OpenThroughDialogService();
    OpenAbba(cut);
    cut.WaitForAssertion(() => AddButtonText(cut).Should().Be("Add \"ABBA\" (60)"), TimeSpan.FromSeconds(10));

    cut.Find(".folder-pick-switch .rz-switch").Click();
    AddButtonText(cut).Should().Be("Add \"ABBA\" (3)");
    cut.Find("button.folder-pick-add").Click();

    ((object?)await closed.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeOfType<FolderAddResult>();
    var posted = QueuedBatches();
    posted.Should().HaveCount(1);
    posted[0].Should().Equal($"{Abba}/01.mp3", $"{Abba}/02.mp3", $"{Abba}/03.mp3");
  }

  [Fact]
  public async Task Add_QueuesEveryTrackInOrder_InBatchesOfTwenty_ThenClosesWithTheResult()
  {
    StubBrowsing();
    StubQueueAccepts();
    var listing = Listing(Abba, "ABBA", topLevel: 0, total: 45, skippedUnreadable: 2);
    _api.Get("/api/files/folder-tracks", listing);
    var (cut, closed) = OpenThroughDialogService();
    OpenAbba(cut);
    cut.WaitForAssertion(() => AddDisabled(cut).Should().BeFalse(), TimeSpan.FromSeconds(10));

    cut.Find("button.folder-pick-add").Click();

    object? result = await closed.WaitAsync(TimeSpan.FromSeconds(10));
    var batches = QueuedBatches();
    batches.Select(b => b.Count).Should().Equal(20, 20, 5);
    batches.SelectMany(b => b).Should().Equal(listing.Paths, "the queue gets the listing's order");
    // The canned API reports 20 added per batch, so the 5-path batch over-reports; the result counts what the
    // API said it added. Skipped carries the 2 the listing could not read.
    result.Should().BeOfType<FolderAddResult>().Which.Should().Be(new FolderAddResult("ABBA", 60, 45, 2, null));
  }

  [Fact]
  public void OverTheCap_OffersTheFirstMax()
  {
    StubBrowsing();
    _api.Get("/api/files/folder-tracks", Listing(Library, "Music", topLevel: 0, total: 500, truncated: true));

    var cut = RenderFolderMode();

    cut.WaitForAssertion(() => AddButtonText(cut).Should().Be("Add first 500"), TimeSpan.FromSeconds(10));
    cut.Find(".folder-pick-summary").TextContent.Should()
      .Be("More than 500 tracks here. Only the first 500 will be added.");
  }

  [Fact]
  public void AListingError_IsShown_AndAddStaysDisabled()
  {
    StubBrowsing();
    _api.Route(HttpMethod.Get, "/api/files/folder-tracks", HttpStatusCode.GatewayTimeout,
      JsonSerializer.Serialize(new { error = "Reading the folder took longer than 20 seconds. Try a smaller folder." }));

    var cut = RenderFolderMode();

    cut.WaitForAssertion(() => cut.Find(".folder-pick-summary").TextContent.Should()
      .Be("Reading the folder took longer than 20 seconds. Try a smaller folder."), TimeSpan.FromSeconds(10));
    AddDisabled(cut).Should().BeTrue();
  }

  [Fact]
  public void AnEmptyFolder_SaysSo()
  {
    StubBrowsing();
    _api.Get("/api/files/folder-tracks", Listing(Library, "Music", topLevel: 0, total: 0, subfolders: 0));

    var cut = RenderFolderMode();

    cut.WaitForAssertion(() => cut.Find(".folder-pick-summary").TextContent.Should()
      .Be("No playable tracks in Music"), TimeSpan.FromSeconds(10));
    AddDisabled(cut).Should().BeTrue();
  }

  [Fact]
  public void FileMode_IsUnchanged_FileRowsShow_AndThereIsNoFolderFooter()
  {
    StubBrowsing();

    var cut = RenderComponent<FileBrowserDialog>(p => p
      .Add(d => d.AllowMultiSelect, true)
      .Add(d => d.AllowAddToQueue, true)
      .Add(d => d.PreferredBookmarkTag, "music"));

    cut.WaitForAssertion(() => cut.Markup.Should().Contain("loose.mp3"), TimeSpan.FromSeconds(10));
    cut.FindAll(".folder-pick-footer").Count.Should().Be(0);
    cut.FindAll(".folder-pick-summary").Count.Should().Be(0);
    _api.Requests.Should().NotContain(r => r.Path == "/api/files/folder-tracks");
  }

  /// <summary>
  /// Opens the dialog the way the queue panel does — through <see cref="DialogService"/>, into a rendered
  /// <see cref="RadzenDialog"/> host — so <c>DialogService.Close</c> has a dialog to close and the returned task
  /// completes with what it closed with.
  /// </summary>
  private (IRenderedComponent<FileBrowserDialog> Cut, Task<dynamic> Closed) OpenThroughDialogService()
  {
    var host = RenderComponent<RadzenDialog>();
    var dialogs = Services.GetRequiredService<DialogService>();
    Task<dynamic> closed = null!;
    host.InvokeAsync(() => closed = dialogs.OpenAsync<FileBrowserDialog>("Add folder",
      new Dictionary<string, object> { { "SelectFolder", true }, { "PreferredBookmarkTag", "music" } }));
    host.WaitForAssertion(() => host.FindComponents<FileBrowserDialog>().Should().HaveCount(1), TimeSpan.FromSeconds(10));
    return (host.FindComponent<FileBrowserDialog>(), closed);
  }

  private List<List<string>> QueuedBatches() =>
    _api.Requests
      .Where(r => r.Method == HttpMethod.Post && r.Path == "/api/files/queue")
      .Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("paths").EnumerateArray()
        .Select(e => e.GetString()!).ToList())
      .ToList();
}
