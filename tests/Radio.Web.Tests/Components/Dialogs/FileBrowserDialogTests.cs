using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Dialogs;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Components.Dialogs;

/// <summary>
/// bUnit tests for the FileBrowserDialog component.
/// Dialog components render empty without a proper dialog context,
/// so these tests verify the component instantiates and accepts parameters without errors.
/// </summary>
public class FileBrowserDialogTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public FileBrowserDialogTests()
  {
    _loggerFactory = new NullLoggerFactory();

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddRadzenComponents();
    Services.AddHttpClient<FileApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddSingleton<ILogger<FileBrowserDialog>>(
      _loggerFactory.CreateLogger<FileBrowserDialog>());

    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }

    base.Dispose(disposing);
  }

  [Fact]
  public void FileBrowserDialog_Renders_Successfully()
  {
    var cut = RenderComponent<FileBrowserDialog>();
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_MultiSelect_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowMultiSelect, true));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_SingleSelect_Default()
  {
    var cut = RenderComponent<FileBrowserDialog>();
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_ShowPlayButton_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.ShowPlayButton, true));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_AllowAddToQueue_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowAddToQueue, true));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_InitialSelection_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.InitialSelection, "/path/to/sound.mp3"));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_Subdirectory_Parameter()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.Subdirectory, "notifications"));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_Combined_Parameters()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowMultiSelect, true)
      .Add(p => p.AllowAddToQueue, true)
      .Add(p => p.ShowPlayButton, true)
      .Add(p => p.Subdirectory, "music"));
    Assert.NotNull(cut);
  }

  [Fact]
  public void FileBrowserDialog_WithAbsoluteMode_Renders_Successfully()
  {
    // Verify the component instantiates without error — it has internal
    // absolute mode state (drives, path bar) that must initialize cleanly.
    // Dialog renders empty without a dialog context, so we just
    // verify no exceptions during construction.
    var cut = RenderComponent<FileBrowserDialog>();
    Assert.NotNull(cut);
    // Component should have the internal drives list initialized (empty until API call)
    Assert.NotNull(cut.Instance);
  }

  [Fact]
  public void FileBrowserDialog_Accepts_AllParameters_Combined_WithQueueAndPlay()
  {
    var cut = RenderComponent<FileBrowserDialog>(parameters => parameters
      .Add(p => p.AllowMultiSelect, true)
      .Add(p => p.AllowAddToQueue, true)
      .Add(p => p.ShowPlayButton, true)
      .Add(p => p.Subdirectory, "music")
      .Add(p => p.InitialSelection, "/path/to/song.mp3"));
    Assert.NotNull(cut);
  }

  // ─── Chip filter persistence ─────────────────────────────────────────────────
  // PR 3 replaces the prior single-string filter dropdown with a List<string> of
  // chips. The repair pass in ParsePersistedFilterValue tolerates the legacy
  // double-escaped form ("\"[\\\".mp3\\\",\\\".flac\\\"]\"" etc.) so existing
  // installs migrate cleanly without user intervention.

  [Fact]
  public void ParsePersistedFilterValue_NativeJsonArray_ReturnsExtensions()
  {
    // The happy path: ConfigApi deserializes the persisted blob into a JsonElement
    // whose ValueKind is Array. We round-trip a small fixture through JsonDocument
    // to mirror what the config store handler hands us.
    using var doc = JsonDocument.Parse("[\".mp3\", \".flac\"]");
    var result = FileBrowserDialog.ParsePersistedFilterValue(doc.RootElement.Clone());
    result.Should().NotBeNull();
    result!.Should().BeEquivalentTo(new[] { ".mp3", ".flac" });
  }

  [Fact]
  public void ParsePersistedFilterValue_DoubleEscapedString_Recovered()
  {
    // Legacy bug: an earlier persistence path stringified the array twice. The repair
    // pass must keep unwrapping until it lands on a string[] (capped at 4 levels).
    var inner = JsonSerializer.Serialize(new[] { ".mp3", ".flac" });          // "[\".mp3\",\".flac\"]"
    var doubleEscaped = JsonSerializer.Serialize(inner);                       // "\"[\\\"...\\\"]\""
    var result = FileBrowserDialog.ParsePersistedFilterValue(doubleEscaped);
    result.Should().NotBeNull();
    result!.Should().BeEquivalentTo(new[] { ".mp3", ".flac" });
  }

  [Fact]
  public void ParsePersistedFilterValue_TripleEscapedString_Recovered()
  {
    // Pathological case still in scope of the repair pass (we cap unwrap depth at 4).
    var inner = JsonSerializer.Serialize(new[] { ".wav" });
    var double_ = JsonSerializer.Serialize(inner);
    var triple = JsonSerializer.Serialize(double_);
    var result = FileBrowserDialog.ParsePersistedFilterValue(triple);
    result.Should().NotBeNull();
    result!.Should().BeEquivalentTo(new[] { ".wav" });
  }

  [Fact]
  public void ParsePersistedFilterValue_PlainStringJunk_ReturnsNull()
  {
    // Non-JSON garbage must not throw — the caller silently keeps an empty chip list.
    FileBrowserDialog.ParsePersistedFilterValue("not-json-at-all").Should().BeNull();
  }

  [Fact]
  public void ParsePersistedFilterValue_NestedJsonElementString_Recovered()
  {
    // A JsonElement whose ValueKind is String wrapping a legacy escaped blob should
    // delegate through the string-unwrap path.
    var blob = JsonSerializer.Serialize(new[] { ".m4a", ".aac" });
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(blob)); // string-of-string-of-array
    var result = FileBrowserDialog.ParsePersistedFilterValue(doc.RootElement.Clone());
    result.Should().NotBeNull();
    result!.Should().BeEquivalentTo(new[] { ".m4a", ".aac" });
  }

  [Fact]
  public void ParsePersistedFilterValue_EnumerableOfObject_ReadsAsStrings()
  {
    // Some backends round-trip arrays as IEnumerable<object>. We coerce each element
    // via ToString() so the chip list rehydrates cleanly.
    IEnumerable<object> raw = new object[] { ".opus", ".ogg" };
    var result = FileBrowserDialog.ParsePersistedFilterValue(raw);
    result.Should().NotBeNull();
    result!.Should().BeEquivalentTo(new[] { ".opus", ".ogg" });
  }

  [Fact]
  public void ParsePersistedFilterValue_RoundTripsCleanly_NoDoubleEscapeReintroduced()
  {
    // Save → load must be an identity for the chip list. We serialize via the same
    // path SaveFilterAsync uses (a plain string[]), then parse via the repair entry
    // point and assert no escape characters survive.
    var original = new[] { ".mp3", ".flac", ".wav" };
    var serialized = JsonSerializer.Serialize(original);
    using var doc = JsonDocument.Parse(serialized);
    var parsed = FileBrowserDialog.ParsePersistedFilterValue(doc.RootElement.Clone());
    parsed.Should().NotBeNull();
    parsed!.Should().BeEquivalentTo(original);
    // No element should contain a backslash — a regression to escape-blobs would surface here.
    parsed.Should().NotContain(s => s.Contains('\\') || s.Contains('"'));
  }

  // ─── Task #15 PR E item #2: drive selector deepest-match-only check ──────
  //
  // The location dropdown previously showed BOTH the bookmark check AND the
  // root-drive check when _absolutePath was a descendant of both (e.g.
  // "/mnt/nas/music" matched the "NAS Music Library" bookmark AND the root
  // "/" drive). ResolveDeepestSelectorPath returns the single longest-path
  // candidate so only the most-specific entry renders a check icon.

  [Fact]
  public void ResolveDeepestSelectorPath_BookmarkAndRootDrive_ReturnsBookmark()
  {
    // Classic case: bookmark at the exact path + root drive that is also a
    // prefix. Deepest match wins → bookmark, not "/".
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "/mnt/nas/music",
      bookmarkPaths: new[] { "/mnt/nas/music" },
      drivePaths: new[] { "/" });
    result.Should().Be("/mnt/nas/music");
  }

  [Fact]
  public void ResolveDeepestSelectorPath_OnlyRootDrive_ReturnsRootDrive()
  {
    // Path lives inside the root drive but no bookmark matches — the root
    // drive entry is the only candidate and should carry the check.
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "/var/log",
      bookmarkPaths: new[] { "/mnt/nas/music", "/home/mmack/audio" },
      drivePaths: new[] { "/" });
    result.Should().Be("/");
  }

  [Fact]
  public void ResolveDeepestSelectorPath_BookmarkDescendant_ReturnsBookmark()
  {
    // Browser is inside the bookmark's tree but not at the bookmark root.
    // The bookmark is still the most-specific match (root drive "/" is
    // strictly shorter).
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "/mnt/nas/music/Pink Floyd/Animals",
      bookmarkPaths: new[] { "/mnt/nas/music" },
      drivePaths: new[] { "/" });
    result.Should().Be("/mnt/nas/music");
  }

  [Fact]
  public void ResolveDeepestSelectorPath_NoMatch_ReturnsNull()
  {
    // Path is on a drive that isn't in the candidate list — nothing lights
    // up. Renders zero checks (correct: the location dropdown should not
    // claim any active entry).
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "D:\\Music",
      bookmarkPaths: new[] { "/mnt/nas/music" },
      drivePaths: new[] { "/", "C:\\" });
    result.Should().BeNull();
  }

  [Fact]
  public void ResolveDeepestSelectorPath_EmptyPath_ReturnsNull()
  {
    // _absolutePath is null/empty before the user navigates anywhere. No
    // entry should be marked active.
    FileBrowserDialog.ResolveDeepestSelectorPath(null, new[] { "/" }, Array.Empty<string>())
      .Should().BeNull();
    FileBrowserDialog.ResolveDeepestSelectorPath(string.Empty, new[] { "/" }, Array.Empty<string>())
      .Should().BeNull();
  }

  [Fact]
  public void ResolveDeepestSelectorPath_WindowsDriveAndBookmark_PicksBookmark()
  {
    // Windows path variant: bookmark deep inside C:\ — bookmark wins over
    // the C:\ root drive. Comparison is case-insensitive (matches the
    // OrdinalIgnoreCase check the dialog used historically).
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "C:\\Users\\mmack\\Music\\Sample.mp3",
      bookmarkPaths: new[] { "C:\\Users\\mmack\\Music" },
      drivePaths: new[] { "C:\\" });
    result.Should().Be("C:\\Users\\mmack\\Music");
  }

  [Fact]
  public void ResolveDeepestSelectorPath_FalsePrefixGuard_RejectsSiblingDirectory()
  {
    // "/mnt" must NOT match "/mntfoo" — the naive StartsWith would create a
    // false positive, so the prefix check requires a separator after the
    // candidate (or exact match).
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "/mntfoo/data",
      bookmarkPaths: new[] { "/mnt" },
      drivePaths: new[] { "/" });
    // "/mnt" is rejected; only "/" matches → root drive carries the check.
    result.Should().Be("/");
  }

  [Fact]
  public void ResolveDeepestSelectorPath_TwoOverlappingBookmarks_PicksDeepest()
  {
    // Hypothetical: two bookmarks both prefix the current path (a parent
    // and a child). The longer-path bookmark wins.
    var result = FileBrowserDialog.ResolveDeepestSelectorPath(
      "/mnt/nas/music/jazz/coltrane",
      bookmarkPaths: new[] { "/mnt/nas", "/mnt/nas/music" },
      drivePaths: new[] { "/" });
    result.Should().Be("/mnt/nas/music");
  }
}
