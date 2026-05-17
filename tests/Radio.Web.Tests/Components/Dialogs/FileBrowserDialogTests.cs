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
}
