using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="QueueHistoryPanel"/>.
///
/// Originally introduced by PR 3 (now-playing row treatment + duration formatting
/// invariants), expanded by PR 5 to cover the queue-split layout (handoff §P1·4):
/// the panel now renders a two-column shape (list 1.6 / context 1) with a tab
/// strip up top, a Queue Total LED tile, an Up Next tile, a Save-as-Playlist CTA,
/// and a kebab menu replacing the inline ADD / CLEAR header buttons.
///
/// Per the FileBrowserDialog test pattern, the QueueApi / HistoryApi clients return
/// null when no server is running, so RefreshQueueAsync sets <c>_queueItems</c> to
/// empty and the queue grid is hidden. Empty-state assertions are sufficient to lock
/// the structural contract; richer interaction tests belong in an E2E run.
/// </summary>
public class QueueHistoryPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public QueueHistoryPanelTests()
  {
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    _loggerFactory = new NullLoggerFactory();

    JSInterop.Mode = JSRuntimeMode.Loose;

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

    Services.AddRadzenComponents();

    // QueueHistoryPanel injects IOptionsMonitor<DisplayOptions> for the
    // "ends ~" prediction's wall-clock formatting. The default (24h, no
    // seconds) matches the historical hardcoded "HH:mm" behaviour so
    // pre-existing assertions in this fixture continue to hold.
    Services.Configure<DisplayOptions>(_ => { });

    Services.AddHttpClient<QueueApiService>();
    Services.AddHttpClient<PlayHistoryApiService>();
    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<FileApiService>();
    Services.AddHttpClient<PlaylistApiService>();
    Services.AddHttpClient<SourcesApiService>();
    Services.AddHttpClient<ConfigurationApiService>(); // dependency of QueuePersistenceService

    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()
      )
    );

    Services.AddSingleton<QueuePersistenceService>();
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
  public void QueueHistoryPanel_Renders_Without_Errors()
  {
    var cut = RenderComponent<QueueHistoryPanel>();
    Assert.NotNull(cut);
  }

  [Fact]
  public void QueueHistoryPanel_DoesNotEmit_FractionalSecondsInMarkup()
  {
    // Defense in depth: the panel never renders raw TimeSpan.ToString() output. Even with
    // an empty queue / history (the case here, no API server running), the rendered HTML
    // must not contain a "00:00:00.0000000"-style artefact. This catches accidental future
    // regressions that introduce ToString() back into the templates.
    var cut = RenderComponent<QueueHistoryPanel>();
    Assert.DoesNotMatch(@"\d+:\d{2}:\d{2}\.\d{4,}", cut.Markup);
  }

  [Fact]
  public void SumQueueRuntime_EmptyList_ReturnsZero()
  {
    QueueHistoryPanel.SumQueueRuntime(Array.Empty<QueueItemDto>())
      .Should().Be(TimeSpan.Zero);
  }

  [Fact]
  public void SumQueueRuntime_MixedShortFormat_AccumulatesSeconds()
  {
    // "m:ss" rows sum into the total. 3:00 + 4:30 = 7:30.
    var items = new[]
    {
      MakeItem("3:00"),
      MakeItem("4:30"),
    };
    var total = QueueHistoryPanel.SumQueueRuntime(items);
    total.Should().Be(TimeSpan.FromSeconds(180 + 270));
  }

  [Fact]
  public void SumQueueRuntime_LongFormat_AccumulatesHours()
  {
    // "h:mm:ss" rows are passed through directly.
    var items = new[]
    {
      MakeItem("1:30:00"),
      MakeItem("0:15:30"),
    };
    var total = QueueHistoryPanel.SumQueueRuntime(items);
    total.Should().Be(TimeSpan.FromMinutes(90 + 15) + TimeSpan.FromSeconds(30));
  }

  [Fact]
  public void SumQueueRuntime_NullOrEmptyDurations_ContributeZero()
  {
    // Empty or null Duration strings must not throw and must not poison the running total.
    var items = new[]
    {
      MakeItem(""),
      MakeItem(null),
      MakeItem("3:00"),
    };
    QueueHistoryPanel.SumQueueRuntime(items).Should().Be(TimeSpan.FromMinutes(3));
  }

  [Fact]
  public void SumQueueRuntime_UnparseableDuration_Skipped()
  {
    // A future server bug emitting a non-numeric duration must not crash the UI.
    var items = new[]
    {
      MakeItem("not-a-time"),
      MakeItem("2:15"),
    };
    QueueHistoryPanel.SumQueueRuntime(items)
      .Should().Be(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(15));
  }

  // ── PR 5: queue-split layout assertions ──

  [Fact]
  public void QueueHistoryPanel_RendersSplitLayout_WithListAndContextColumns()
  {
    // The two-column split (handoff §P1·4) replaces the single RadzenTabs body.
    // Both columns must be in the DOM regardless of API availability.
    var cut = RenderComponent<QueueHistoryPanel>();
    cut.FindAll(".queue-split").Count.Should().Be(1);
    cut.FindAll(".queue-split-list-col").Count.Should().Be(1);
    cut.FindAll(".queue-split-context-col").Count.Should().Be(1);
  }

  [Fact]
  public void QueueHistoryPanel_TabStrip_RendersQueueAndHistoryPills()
  {
    // Default state shows Queue and History tabs. The Radio pill is conditional
    // on the active source being a radio family member, which isn't available
    // here without an API, so it should NOT render.
    var cut = RenderComponent<QueueHistoryPanel>();
    var tabs = cut.FindAll(".queue-split-tab");
    tabs.Count.Should().BeGreaterThanOrEqualTo(2);

    var tabLabels = tabs.Select(t => t.TextContent.Trim()).ToList();
    tabLabels.Should().Contain(s => s.StartsWith("Queue"));
    tabLabels.Should().Contain("History");
  }

  [Fact]
  public void QueueHistoryPanel_TabStrip_RendersKebabButton()
  {
    // The ADD / CLEAR header buttons move into a kebab menu (handoff §P1·4 step 4).
    // The kebab affordance itself is always present; its menu opens on click.
    var cut = RenderComponent<QueueHistoryPanel>();
    cut.FindAll(".queue-split-kebab").Count.Should().Be(1);
  }

  [Fact]
  public void QueueHistoryPanel_QueueTab_RightColumn_ShowsAllThreeTiles()
  {
    // The right column on the Queue tab carries the Queue Total LED, Up Next
    // thumbs, and Save-as-Playlist CTA. The Save CTA is disabled when the
    // queue is empty. (The component's default tab resolves to History when
    // there is no API server because SetDefaultTabForSourceAsync's catch-all
    // picks History — so we click into Queue explicitly first.)
    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "Queue");
    cut.FindAll(".queue-total-tile").Count.Should().Be(1);
    cut.FindAll(".up-next-tile").Count.Should().Be(1);
    var savePlaylist = cut.FindAll(".save-playlist-tile");
    savePlaylist.Count.Should().Be(1);
    savePlaylist[0].HasAttribute("disabled").Should().BeTrue(
      "the Save-as-playlist CTA is disabled when the queue is empty");
  }

  [Fact]
  public void QueueHistoryPanel_QueueTotalTile_ShowsLedValueAndSubLine()
  {
    // The LED value is the only place in the right column using --font-led
    // amber — locked here so a future refactor of design tokens trips this test.
    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "Queue");
    var ledValue = cut.Find(".queue-total-tile-value");
    ledValue.TextContent.Trim().Should().NotBeEmpty();
    cut.FindAll(".queue-total-tile-sub").Count.Should().Be(1);
  }

  // ─── Configurable time format (HANDOFF-configurable-time-format.md §3.4) ──
  //
  // The queue total tile renders "ends ~HH:mm" when _totalRuntime > 0. Per
  // the handoff, the 12h/24h flip is honored (consistent with the topbar
  // Time cluster), but seconds are ALWAYS suppressed regardless of the
  // global ShowSeconds setting because :ss precision on a forward-looking
  // track-total estimate is meaningless.
  //
  // The fixture has no API server so _totalRuntime / _queueItems stay at
  // their initial values; we set them via reflection to drive the conditional
  // ends-prediction branch.

  [Fact]
  public void QueueHistoryPanel_EndsTile_12HourFormat_RendersAmOrPmSuffix()
  {
    Services.Configure<DisplayOptions>(o => o.TimeFormat = "12h");

    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "Queue");

    InjectQueueRuntime(cut, TimeSpan.FromMinutes(30), itemCount: 5);

    var sub = cut.Find(".queue-total-tile-sub").TextContent;
    // 12h with allowSeconds: false → "ends ~h:mm tt" — the suffix is the
    // single load-bearing visual change vs the default 24h "HH:mm" form.
    sub.Should().MatchRegex(@"ends ~\d{1,2}:[0-5]\d (AM|PM)",
      "12h queue ends-prediction must render h:mm tt with uppercase AM/PM");
  }

  [Fact]
  public void QueueHistoryPanel_EndsTile_24hWithSeconds_StillSuppressesSeconds()
  {
    // Global ShowSeconds = true must NOT bleed into the queue prediction —
    // the call site passes allowSeconds: false so seconds stay suppressed
    // regardless. Asserting this guards against accidental future drift
    // toward "ends ~15:45:22" which would be a meaningless precision claim.
    Services.Configure<DisplayOptions>(o =>
    {
      o.TimeFormat = "24h";
      o.ShowSeconds = true;
    });

    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "Queue");

    InjectQueueRuntime(cut, TimeSpan.FromMinutes(30), itemCount: 5);

    var sub = cut.Find(".queue-total-tile-sub").TextContent;
    sub.Should().MatchRegex(@"ends ~[0-2]\d:[0-5]\d(\s|$|·)",
      "queue ends-prediction must NEVER render :ss even when ShowSeconds is on");
    sub.Should().NotMatchRegex(@"ends ~[0-2]\d:[0-5]\d:[0-5]\d",
      "the allowSeconds:false override must suppress the seconds component");
  }

  /// <summary>
  /// Pokes the panel's <c>_totalRuntime</c> and <c>_queueItems</c> fields via
  /// reflection so the conditional ends-prediction branch fires without
  /// requiring an API server. Mirrors the reflection pattern already used in
  /// <see cref="Pages.SleepTests"/> for the clock-tick test.
  /// </summary>
  private static void InjectQueueRuntime(IRenderedComponent<QueueHistoryPanel> cut, TimeSpan runtime, int itemCount)
  {
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var instance = cut.Instance;

    var totalRuntimeField = typeof(QueueHistoryPanel).GetField("_totalRuntime", flags);
    totalRuntimeField!.SetValue(instance, runtime);

    var queueItemsField = typeof(QueueHistoryPanel).GetField("_queueItems", flags);
    // Use a Duration that parses cleanly through SumQueueRuntime if recomputed
    // — though SumQueueRuntime is not re-invoked here, we keep the field shape
    // self-consistent in case a future refactor adds a re-sum step.
    var items = Enumerable.Range(0, itemCount)
      .Select(_ => MakeItem("6:00"))
      .ToList();
    queueItemsField!.SetValue(instance, items);

    var stateHasChanged = typeof(Microsoft.AspNetCore.Components.ComponentBase).GetMethod(
      "StateHasChanged", flags);
    cut.InvokeAsync(() => stateHasChanged!.Invoke(instance, null)).GetAwaiter().GetResult();
  }

  [Fact]
  public void QueueHistoryPanel_UpNextTile_EmptyState_ShowsPlaceholder()
  {
    // With an empty queue, the Up Next tile renders a friendly placeholder
    // instead of stub rows — keeps the right column from feeling broken.
    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "Queue");
    cut.FindAll(".up-next-empty").Count.Should().Be(1);
  }

  [Fact]
  public void QueueHistoryPanel_OpeningKebab_ShowsAddFilesAndClearAllItems()
  {
    // Click the kebab — the menu fly-out should list Add Files and Clear All
    // (per the handoff §P1·4 spec, those move out of the inline header strip).
    var cut = RenderComponent<QueueHistoryPanel>();
    cut.Find(".queue-split-kebab").Click();
    cut.Markup.Should().Contain("Add Files");
    cut.Markup.Should().Contain("Clear All");
  }

  [Fact]
  public void QueueHistoryPanel_SwitchingToHistoryTab_SwapsRightColumnContent()
  {
    // Clicking the History tab moves _activeTab → TabHistory; the queue-total /
    // up-next / save-cta tiles are replaced by the history-stats tile inside the
    // same right-column flex container (it is NOT remounted, just its children
    // change). We assert the tile-presence transition.
    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "Queue");
    cut.FindAll(".queue-total-tile").Count.Should().Be(1);
    cut.FindAll(".history-stats-tile").Count.Should().Be(0);

    // Now click History.
    ClickTab(cut, "History");
    cut.FindAll(".queue-total-tile").Count.Should().Be(0);
    cut.FindAll(".up-next-tile").Count.Should().Be(0);
    cut.FindAll(".history-stats-tile").Count.Should().Be(1);
  }

  [Fact]
  public void QueueHistoryPanel_HistoryStatsTile_HasTotalPlaysTopTrackTopArtistRows()
  {
    var cut = RenderComponent<QueueHistoryPanel>();
    ClickTab(cut, "History");

    var labels = cut.FindAll(".history-stats-label")
      .Select(e => e.TextContent.Trim())
      .ToList();
    labels.Should().Contain("Total Plays");
    labels.Should().Contain("Top Track");
    labels.Should().Contain("Top Artist");
  }

  /// <summary>
  /// Click the tab whose visible label starts with the given prefix
  /// (e.g. "Queue" matches "Queue · 0" + spillover when the count is set).
  /// </summary>
  private static void ClickTab(IRenderedComponent<QueueHistoryPanel> cut, string labelPrefix)
  {
    var tab = cut.FindAll(".queue-split-tab")
      .FirstOrDefault(t => t.TextContent.TrimStart().StartsWith(labelPrefix, StringComparison.Ordinal));
    tab.Should().NotBeNull(
      $"the {labelPrefix} tab must exist in the tab strip");
    tab!.Click();
  }

  private static QueueItemDto MakeItem(string? duration) => new(
    Index: 0,
    Title: "T",
    Artist: "A",
    Album: "Al",
    Duration: duration,
    IsCurrent: false,
    State: "Upcoming",
    FullPlaylistIndex: 0);

  // ── Task #15 PR B (handoff item #5): currently-playing row visual ──
  //
  // Reach into _queueItems directly (the production path is RefreshQueueAsync
  // hitting the API, but the test fixture deliberately has no API server).
  // Seeding the field + re-rendering exercises the same template branch the
  // live UI lights up — the `queue-row-current` class + ▶ glyph + amber
  // styling driven by `State == "Current"`.

  /// <summary>
  /// Reflectively poke the panel into a "queue has one currently-playing
  /// track" state AND force the Queue tab active. Mirrors the
  /// SetRecognitionState pattern in NowPlayingPanelTests — same idea,
  /// different field set. The active-tab flip is the load-bearing piece:
  /// without an API server, <c>SetDefaultTabForSourceAsync</c> drops the
  /// component on the History tab (the catch branch), so the queue rows
  /// would never render.
  /// </summary>
  private static void SeedQueueItems(
    IRenderedComponent<QueueHistoryPanel> cut,
    IEnumerable<QueueItemDto> items)
  {
    var instance = cut.Instance;
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
    typeof(QueueHistoryPanel).GetField("_queueItems", flags)!
      .SetValue(instance, items.ToList());
    // Force the Queue tab + mark the user-override flag so OnParametersSet /
    // any subsequent SetDefaultTabForSourceAsync don't snap us back.
    typeof(QueueHistoryPanel).GetField("_activeTab", flags)!
      .SetValue(instance, 0 /* TabQueue */);
    typeof(QueueHistoryPanel).GetField("_userOverrodeTab", flags)!
      .SetValue(instance, true);
    cut.Render();
  }

  [Fact]
  public void QueueHistoryPanel_CurrentlyPlayingRow_HasAmberBorderAndPlayGlyph()
  {
    // Build a queue with one Current track + one upcoming. The Virtualize block
    // renders both rows; only the Current row carries the `queue-row-current`
    // class (which paints the amber border per design-system.css) AND swaps the
    // slot index for the ▶ glyph. The other row renders its FullPlaylistIndex.
    var items = new[]
    {
      new QueueItemDto(
        Index: 0,
        Title: "Now Playing Track",
        Artist: "Some Artist",
        Album: "Some Album",
        Duration: "3:42",
        IsCurrent: true,
        State: "Current",
        FullPlaylistIndex: 0),
      new QueueItemDto(
        Index: 1,
        Title: "Upcoming Track",
        Artist: "Other Artist",
        Album: "Other Album",
        Duration: "4:10",
        IsCurrent: false,
        State: "Upcoming",
        FullPlaylistIndex: 1),
    };

    var cut = RenderComponent<QueueHistoryPanel>();
    SeedQueueItems(cut, items);

    // Exactly one currently-playing row, carrying both the class and the glyph.
    var currentRows = cut.FindAll(".queue-row-current");
    currentRows.Count.Should().Be(1, "exactly one queue row must carry the current-row class");

    // The ▶ glyph (U+25B6) lives inside the .queue-row-glyph span on the
    // current row only. The other row renders its slot number instead.
    var glyphs = cut.FindAll(".queue-row-glyph");
    glyphs.Count.Should().Be(1, "the play-glyph is unique to the currently-playing row");
    glyphs[0].TextContent.Trim().Should().Be("▶");
  }
}
