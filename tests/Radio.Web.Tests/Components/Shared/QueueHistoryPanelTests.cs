using System.Net;
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
/// invariants), expanded by PR 5 to cover the queue-split layout (handoff §P1·4),
/// and reshaped by UI-17: the panel is now the whole Home centre, with one tab strip
/// whose tabs depend on the active source (queue, history, radio controls, the
/// Bluetooth connect panel), a Stats chip for the history stats, and Up Next gone.
///
/// Every API client is backed by one <see cref="RoutedApiHandler"/>. Unstubbed
/// endpoints answer 404, which the clients treat as "nothing there", so a test that
/// stubs nothing sees the panel as it is with no source, no queue and no history.
///
/// </summary>
public class QueueHistoryPanelTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;
  private readonly RoutedApiHandler _api = new();

  private HttpClient NewClient() =>
    new(_api, disposeHandler: false) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

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

    // UI-17: every API client talks to one canned API. An empty table answers 404, which the
    // clients treat as "nothing there" — the same state the old hermetic, unaddressed clients left
    // the panel in — so tests stub only the endpoints their assertion depends on.
    Services.AddSingleton(_ => new QueueApiService(NewClient(), NullLogger<QueueApiService>.Instance));
    Services.AddSingleton(_ => new PlayHistoryApiService(NewClient(), NullLogger<PlayHistoryApiService>.Instance));
    Services.AddSingleton(_ => new AudioApiService(NewClient(), NullLogger<AudioApiService>.Instance));
    Services.AddSingleton(_ => new FileApiService(NewClient(), NullLogger<FileApiService>.Instance));
    Services.AddSingleton(_ => new PlaylistApiService(NewClient(), NullLogger<PlaylistApiService>.Instance));
    Services.AddSingleton(_ => new SourcesApiService(NewClient(), NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(_ => new ConfigurationApiService(NewClient(), NullLogger<ConfigurationApiService>.Instance));
    Services.AddSingleton(_ => new BluetoothApiService(NewClient(), NullLogger<BluetoothApiService>.Instance));
    Services.AddSingleton(_ => new RadioApiService(NewClient(), NullLogger<RadioApiService>.Instance));

    // The Radio tab hosts RadioControlPanel, which reads these options.
    Services.AddOptions<RdsScrollOptions>();

    Services.AddScoped<CentrePanelViewService>();

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

  // ── PR 5 queue-split layout, as reshaped by UI-17 ──

  [Fact]
  public void HistoryView_StatsChipOff_HasNoContextColumn_ListTakesTheWidth()
  {
    // UI-17 point 1: with the Stats chip off (the default) the History view has no right column.
    var cut = RenderComponent<QueueHistoryPanel>();
    WaitForInit(cut);
    cut.FindAll(".queue-split").Count.Should().Be(1);
    cut.FindAll(".queue-split-list-col").Count.Should().Be(1);
    cut.FindAll(".queue-split-context-col").Count.Should().Be(0);
    cut.FindAll(".history-stats-tile").Count.Should().Be(0);
  }

  [Fact]
  public void Kebab_ShowsOnlyOnTheQueueView()
  {
    // UI-17 point 3: the kebab (Add Files / Load Playlist / Clear All) goes with the Queue view.
    var cut = RenderOn("FilePlayer");
    cut.FindAll(".queue-split-kebab").Count.Should().Be(0, "File Player with an empty queue opens on History");

    ClickTab(cut, "Queue");
    cut.FindAll(".queue-split-kebab").Count.Should().Be(1);
  }

  [Fact]
  public void QueueView_RightColumn_HasQueueTotalAndSave_ButNoUpNext()
  {
    // UI-17 point 2: Queue Total and Save as playlist stay; Up Next is gone (it repeated the list).
    var cut = RenderOn("FilePlayer");
    ClickTab(cut, "Queue");
    cut.FindAll(".queue-total-tile").Count.Should().Be(1);
    cut.FindAll(".up-next-tile").Count.Should().Be(0);
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
    var cut = RenderOn("FilePlayer");
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
  // The fixture stubs no queue, so _totalRuntime / _queueItems stay at their
  // initial values; we set them via reflection to drive the conditional
  // ends-prediction branch. File Player is the source because the Queue tab
  // only exists there (UI-17).

  [Fact]
  public void QueueHistoryPanel_EndsTile_12HourFormat_RendersAmOrPmSuffix()
  {
    Services.Configure<DisplayOptions>(o => o.TimeFormat = "12h");

    var cut = RenderOn("FilePlayer");
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

    var cut = RenderOn("FilePlayer");
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
  public void QueueHistoryPanel_OpeningKebab_ShowsAddFilesAndClearAllItems()
  {
    var cut = RenderOn("FilePlayer");
    ClickTab(cut, "Queue");
    cut.Find(".queue-split-kebab").Click();
    cut.Markup.Should().Contain("Add Files");
    cut.Markup.Should().Contain("Load Playlist");
    cut.Markup.Should().Contain("Clear All");
  }

  [Fact]
  public void SwitchingFromQueueToHistory_SwapsRightColumnContent()
  {
    // Queue view → its tiles; History view with the chip off → no right column at all; with the chip
    // on → the stats tile.
    var cut = RenderOn("FilePlayer");
    ClickTab(cut, "Queue");
    cut.FindAll(".queue-total-tile").Count.Should().Be(1);
    cut.FindAll(".history-stats-tile").Count.Should().Be(0);

    ClickTab(cut, "History");
    cut.FindAll(".queue-total-tile").Count.Should().Be(0);
    cut.FindAll(".save-playlist-tile").Count.Should().Be(0);
    cut.FindAll(".history-stats-tile").Count.Should().Be(0);

    cut.Find(".centre-stats-chip").Click();
    cut.FindAll(".history-stats-tile").Count.Should().Be(1);
  }

  [Fact]
  public void QueueHistoryPanel_HistoryStatsTile_HasTotalPlaysTopTrackTopArtistRows()
  {
    var cut = RenderComponent<QueueHistoryPanel>();
    WaitForInit(cut);
    cut.Find(".centre-stats-chip").Click();

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
  /// with no source stubbed, the panel opens on History (UI-17 default for
  /// "no source"), so the queue rows would never render.
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
    // a later default-tab pass (source change) does not snap us back.
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
    WaitForInit(cut);
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

  // ── UI-17: the centre panel shows what each source needs ──────────────────────────────────────
  //
  // Every render below waits for OnInitializedAsync to finish before asserting (WaitForInit): the
  // panel starts on History and only moves to its source's default after its API reads, so an
  // assertion made too early would see History for every source and pass the History rows by
  // accident.
  //
  // The Bluetooth tests drive RefreshBluetoothStatusAsync — the method the 5 s poll calls — rather
  // than waiting for the poll. The poll can still tick during a slow test; that is harmless, because
  // a tick reads the same canned status the test just set and applies the same transition, and a
  // transition is applied once (it is keyed on the connected state CHANGING).

  private const string BtAddress = "AA:BB:CC:DD:EE:01";

  private void StubSource(string? sourceType)
  {
    if (sourceType == null)
    {
      _api.Route(HttpMethod.Get, "/api/sources/primary", HttpStatusCode.NotFound, null);
      return;
    }
    _api.Get("/api/sources/primary", new AudioSourceDto
    {
      Id = sourceType.ToLowerInvariant(),
      Name = sourceType,
      Type = sourceType,
      Category = "Primary",
      State = "Active",
    });
  }

  private void StubQueue(int count)
  {
    List<QueueItemDto> items = Enumerable.Range(0, count)
      .Select(i => new QueueItemDto(i, $"Track {i}", "Artist", "Album", "3:00", false, "Upcoming", i))
      .ToList();
    _api.Get("/api/queue/full", items);
  }

  private void StubBluetooth(bool connected)
  {
    BluetoothDeviceDto phone = new()
    {
      Address = BtAddress,
      Name = "Test Phone",
      IsPaired = true,
      IsConnected = connected,
    };
    _api.Get("/api/bluetooth/status", new BluetoothStatusDto
    {
      IsAvailable = true,
      State = "On",
      ConnectedDevice = connected ? phone : null,
      PairedDevices = [phone],
    });
  }

  private IRenderedComponent<QueueHistoryPanel> RenderOn(
    string? sourceType, int queueCount = 0, bool bluetoothConnected = false)
  {
    StubSource(sourceType);
    StubQueue(queueCount);
    StubBluetooth(bluetoothConnected);
    IRenderedComponent<QueueHistoryPanel> cut = RenderComponent<QueueHistoryPanel>();
    WaitForInit(cut);
    return cut;
  }

  /// <summary>
  /// Waits until OnInitializedAsync has reached its last step (the history read), by which point the
  /// default tab and any pending view request have been applied, then for the render that follows.
  /// </summary>
  private void WaitForInit(IRenderedComponent<QueueHistoryPanel> cut)
  {
    cut.WaitForAssertion(
      () => _api.Requests.Should().Contain(r => r.Path == "/api/playhistory/statistics"),
      TimeSpan.FromSeconds(10));
    cut.WaitForAssertion(() => cut.FindAll(".centre-panel").Count.Should().Be(1), TimeSpan.FromSeconds(10));
  }

  /// <summary>Which view the body shows, read from the DOM.</summary>
  private static string ActiveView(IRenderedComponent<QueueHistoryPanel> cut)
  {
    if (cut.FindAll(".rcp-root").Count > 0)
    {
      return "Radio";
    }
    if (cut.FindAll(".bt-connect-panel").Count > 0)
    {
      return "Bluetooth";
    }
    if (cut.FindAll(".queue-total-tile").Count > 0)
    {
      return "Queue";
    }
    if (cut.FindAll(".queue-split").Count > 0)
    {
      return "History";
    }
    return "?";
  }

  private static List<string> TabLabels(IRenderedComponent<QueueHistoryPanel> cut) =>
    cut.FindAll(".queue-split-tab").Select(t => t.TextContent.Trim()).ToList();

  private static string? ActiveTabLabel(IRenderedComponent<QueueHistoryPanel> cut) =>
    cut.FindAll(".queue-split-tab.is-active").Select(t => t.TextContent.Trim()).SingleOrDefault();

  private static void AssertView(IRenderedComponent<QueueHistoryPanel> cut, string view) =>
    cut.WaitForAssertion(() => ActiveView(cut).Should().Be(view), TimeSpan.FromSeconds(10));

  private Task FireSourceChangedAsync(IRenderedComponent<QueueHistoryPanel> cut)
  {
    AudioStateHubService hub = Services.GetRequiredService<AudioStateHubService>();
    return cut.InvokeAsync(() => HubEventFire.FireAsync(hub, "SourceChanged"));
  }

  private static Task RefreshBluetoothAsync(IRenderedComponent<QueueHistoryPanel> cut) =>
    cut.InvokeAsync(() => cut.Instance.RefreshBluetoothStatusAsync());

  // --- pure rules ---

  [Theory]
  [InlineData("FilePlayer", 0, false, QueueHistoryPanel.TabHistory)]
  [InlineData("FilePlayer", 3, false, QueueHistoryPanel.TabQueue)]
  [InlineData("Vinyl", 3, false, QueueHistoryPanel.TabHistory)]
  [InlineData("GenericUSB", 3, false, QueueHistoryPanel.TabHistory)]
  [InlineData("TestTone", 3, false, QueueHistoryPanel.TabHistory)]
  [InlineData("RTLSDRCore", 3, false, QueueHistoryPanel.TabRadio)]
  [InlineData("Radio", 0, false, QueueHistoryPanel.TabRadio)]
  [InlineData("RF320", 0, false, QueueHistoryPanel.TabRadio)]
  [InlineData("Bluetooth", 3, false, QueueHistoryPanel.TabBluetooth)]
  [InlineData("Bluetooth", 3, true, QueueHistoryPanel.TabHistory)]
  [InlineData(null, 3, false, QueueHistoryPanel.TabHistory)]
  public void DefaultTabFor_FollowsTheSourceMatrix(string? source, int queueCount, bool btConnected, int expected)
  {
    QueueHistoryPanel.DefaultTabFor(source, queueCount, btConnected).Should().Be(expected);
  }

  // --- the rendered matrix ---

  [Theory]
  [InlineData("FilePlayer", 0, false, "History", new[] { "Queue · 0", "History" })]
  [InlineData("FilePlayer", 2, false, "Queue", new[] { "Queue · 2", "History" })]
  [InlineData("Vinyl", 2, false, "History", new string[0])]
  [InlineData("GenericUSB", 2, false, "History", new string[0])]
  [InlineData("TestTone", 2, false, "History", new string[0])]
  [InlineData("RTLSDRCore", 2, false, "Radio", new[] { "Radio", "History" })]
  [InlineData("Bluetooth", 2, false, "Bluetooth", new[] { "Connect", "History" })]
  [InlineData("Bluetooth", 2, true, "History", new[] { "History", "Devices" })]
  public void EachSource_OpensOnItsDefaultView_WithItsTabs(
    string source, int queueCount, bool btConnected, string expectedView, string[] expectedTabs)
  {
    var cut = RenderOn(source, queueCount, btConnected);

    AssertView(cut, expectedView);
    TabLabels(cut).Should().Equal(expectedTabs);
  }

  [Theory]
  [InlineData("Vinyl", false)]
  [InlineData("GenericUSB", false)]
  [InlineData("TestTone", false)]
  [InlineData("RTLSDRCore", false)]
  [InlineData("Bluetooth", false)]
  [InlineData("Bluetooth", true)]
  public void NonFilePlayerSources_HaveNoQueueTab_AndNoKebab(string source, bool btConnected)
  {
    // A queue exists (3 tracks) so a Queue tab would have something to show — and still must not.
    var cut = RenderOn(source, queueCount: 3, bluetoothConnected: btConnected);

    TabLabels(cut).Should().NotContain(label => label.StartsWith("Queue"));
    cut.FindAll(".queue-split-kebab").Count.Should().Be(0);
  }

  // --- override rule ---

  [Fact]
  public async Task TappedTab_SticksAcrossARepeatedSourceChanged_ButNotAcrossASourceSwitch()
  {
    var cut = RenderOn("RTLSDRCore");
    AssertView(cut, "Radio");

    ClickTab(cut, "History");
    AssertView(cut, "History");

    // SourceChanged for the SAME source (it fires twice per real switch) must not undo the tap.
    await FireSourceChangedAsync(cut);
    AssertView(cut, "History");

    // A real switch clears the override: away to Vinyl, then back — radio opens on Radio again.
    StubSource("Vinyl");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().BeEmpty();

    StubSource("RTLSDRCore");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "Radio");
  }

  [Fact]
  public async Task SwitchingToFilePlayer_OpensOnQueueWhenItHasTracks()
  {
    var cut = RenderOn("Vinyl", queueCount: 2);
    AssertView(cut, "History");

    StubSource("FilePlayer");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "Queue");
    ActiveTabLabel(cut).Should().Be("Queue · 2");
  }

  // --- Stats chip ---

  [Fact]
  public void StatsChip_IsOffByDefault()
  {
    var cut = RenderOn("Vinyl");

    var chip = cut.Find(".centre-stats-chip");
    chip.GetAttribute("aria-pressed").Should().Be("false");
    chip.ClassList.Should().NotContain("is-on");
    cut.FindAll(".history-stats-tile").Count.Should().Be(0);
  }

  [Fact]
  public void StatsChip_Toggling_PersistsTheValueToTheConfigStore()
  {
    _api.Post("/api/configuration/" + QueueHistoryPanel.UiPreferencesSection);
    var cut = RenderOn("Vinyl");

    cut.Find(".centre-stats-chip").Click();
    cut.WaitForAssertion(() => _api.Requests.Should().Contain(r =>
      r.Method == HttpMethod.Post
      && r.Path == "/api/configuration/ui.centrePanel"
      && r.Body != null && r.Body.Contains("\"showStats\":true")), TimeSpan.FromSeconds(10));
    cut.FindAll(".history-stats-tile").Count.Should().Be(1);
    cut.Find(".centre-stats-chip").GetAttribute("aria-pressed").Should().Be("true");

    cut.Find(".centre-stats-chip").Click();
    cut.WaitForAssertion(() => _api.Requests.Should().Contain(r =>
      r.Method == HttpMethod.Post
      && r.Path == "/api/configuration/ui.centrePanel"
      && r.Body != null && r.Body.Contains("\"showStats\":false")), TimeSpan.FromSeconds(10));
    cut.FindAll(".history-stats-tile").Count.Should().Be(0);
  }

  [Fact]
  public void StatsChip_PersistedOn_IsRestoredOnLoad()
  {
    _api.Get("/api/configuration/ui.centrePanel", new Dictionary<string, object> { ["showStats"] = true });
    var cut = RenderOn("Vinyl");

    cut.Find(".centre-stats-chip").GetAttribute("aria-pressed").Should().Be("true");
    cut.FindAll(".history-stats-tile").Count.Should().Be(1);
  }

  [Fact]
  public void StatsChip_ShowsOnlyOnTheHistoryView()
  {
    var cut = RenderOn("RTLSDRCore");
    AssertView(cut, "Radio");
    cut.FindAll(".centre-stats-chip").Count.Should().Be(0);

    ClickTab(cut, "History");
    cut.FindAll(".centre-stats-chip").Count.Should().Be(1);
  }

  // --- Queue nav pill / radio chevron (CentrePanelViewService) ---

  [Fact]
  public async Task QueuePill_OnANonFilePlayerSource_ShowsTheQueueView_WithoutSwitchingSource_AndHidesTheKebab()
  {
    var cut = RenderOn("RTLSDRCore", queueCount: 2);
    AssertView(cut, "Radio");

    CentrePanelViewService views = Services.GetRequiredService<CentrePanelViewService>();
    await cut.InvokeAsync(() => views.RequestViewAsync(CentrePanelView.Queue));

    AssertView(cut, "Queue");
    TabLabels(cut).Should().Equal("Radio", "History", "Queue · 2");
    // Add Files / Load Playlist make the API switch the source to File Player, so the kebab is
    // File Player only; Save as playlist does not switch source and stays.
    cut.FindAll(".queue-split-kebab").Count.Should().Be(0);
    cut.FindAll(".save-playlist-tile").Count.Should().Be(1);
    views.PendingView.Should().BeNull("the mounted panel consumed the request");
    _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.Path == "/api/sources",
      "viewing the queue must not switch source");

    // Leaving the queue view by its History tab drops the transient Queue tab.
    ClickTab(cut, "History");
    TabLabels(cut).Should().Equal("Radio", "History");
  }

  [Fact]
  public async Task QueuePill_TappedOnAnotherPage_IsCollectedWhenThePanelMounts()
  {
    // MainLayout navigates to "/" and then requests the view; if no panel is mounted yet the request
    // waits in the service.
    CentrePanelViewService views = Services.GetRequiredService<CentrePanelViewService>();
    await views.RequestViewAsync(CentrePanelView.Queue);

    var cut = RenderOn("Vinyl", queueCount: 1);

    AssertView(cut, "Queue");
    TabLabels(cut).Should().Equal("History", "Queue · 1");
    cut.FindAll(".queue-split-kebab").Count.Should().Be(0);
    views.PendingView.Should().BeNull();
  }

  [Fact]
  public async Task RadioChevron_JumpsBackToTheRadioTab()
  {
    var cut = RenderOn("RTLSDRCore");
    ClickTab(cut, "History");
    AssertView(cut, "History");

    CentrePanelViewService views = Services.GetRequiredService<CentrePanelViewService>();
    await cut.InvokeAsync(() => views.RequestViewAsync(CentrePanelView.Radio));

    AssertView(cut, "Radio");
  }

  // --- Bluetooth auto-switch ---

  [Fact]
  public async Task Bluetooth_DeviceConnecting_WhileOnConnect_SwitchesToHistory_AndBackOnDisconnect()
  {
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);
    AssertView(cut, "Bluetooth");
    ActiveTabLabel(cut).Should().Be("Connect");

    StubBluetooth(connected: true);
    await RefreshBluetoothAsync(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().Equal("History", "Devices");

    StubBluetooth(connected: false);
    await RefreshBluetoothAsync(cut);
    AssertView(cut, "Bluetooth");
    TabLabels(cut).Should().Equal("Connect", "History");
  }

  [Fact]
  public async Task Bluetooth_AutoSwitch_RespectsATappedTab()
  {
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);

    // The owner taps away and back: now on Connect by choice.
    ClickTab(cut, "History");
    ClickTab(cut, "Connect");
    AssertView(cut, "Bluetooth");

    StubBluetooth(connected: true);
    await RefreshBluetoothAsync(cut);
    AssertView(cut, "Bluetooth");
    ActiveTabLabel(cut).Should().Be("Devices", "the same panel, relabelled, and not auto-switched away");

    // And with History chosen by tap, a disconnect does not pull the panel to Connect.
    ClickTab(cut, "History");
    StubBluetooth(connected: false);
    await RefreshBluetoothAsync(cut);
    AssertView(cut, "History");
  }

  [Fact]
  public async Task Bluetooth_UnreadableStatus_DoesNotFlipTheTab()
  {
    var cut = RenderOn("Bluetooth", bluetoothConnected: true);
    AssertView(cut, "History");

    _api.Route(HttpMethod.Get, "/api/bluetooth/status", HttpStatusCode.InternalServerError, null);
    await RefreshBluetoothAsync(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().Equal("History", "Devices");
  }

  [Fact]
  public void BluetoothConnectPanel_ConnectButton_CallsTheExistingConnectEndpoint_ThenAutoSwitches()
  {
    _api.Post("/api/bluetooth/connect");
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);
    AssertView(cut, "Bluetooth");
    cut.Markup.Should().Contain("Test Phone");

    // The server will report the phone connected once the connect call has gone through.
    StubBluetooth(connected: true);
    cut.FindAll(".bt-device-action").Single(b => b.TextContent.Trim() == "Connect").Click();

    cut.WaitForAssertion(() => _api.Requests.Should().Contain(r =>
      r.Method == HttpMethod.Post && r.Path == "/api/bluetooth/connect"
      && r.Body != null && r.Body.Contains(BtAddress)), TimeSpan.FromSeconds(10));
    AssertView(cut, "History");
  }

  [Fact]
  public void BluetoothConnectPanel_LinksToTheBluetoothPageForForgetAndAdapterDetails()
  {
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);
    cut.Find("a.bt-connect-settings-link").GetAttribute("href").Should().Be("/bluetooth");
  }

  // --- review fixes: an unreadable source is not a source change ---

  private void StubSourceUnreadable() =>
    _api.Route(HttpMethod.Get, "/api/sources/primary", HttpStatusCode.InternalServerError, null);

  private static Task PollAsync(IRenderedComponent<QueueHistoryPanel> cut) =>
    cut.InvokeAsync(() => cut.Instance.PollTickAsync());

  [Fact]
  public async Task SourceReadFailure_KeepsTheSourceTabsAndTheOverride_AndThePollReReadsIt()
  {
    var cut = RenderOn("RTLSDRCore");
    ClickTab(cut, "History");

    StubSourceUnreadable();
    await FireSourceChangedAsync(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().Equal("Radio", "History");

    // The API is back and the source never changed: the poll's re-read must keep the tapped tab.
    StubSource("RTLSDRCore");
    await PollAsync(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().Equal("Radio", "History");
    _api.Requests.Count(r => r.Path == "/api/sources/primary").Should().BeGreaterThanOrEqualTo(3,
      "mount, the failed event read, and the poll's re-read");
  }

  [Fact]
  public async Task SourceReadFailure_ThenARealChange_IsAppliedByThePoll()
  {
    var cut = RenderOn("RTLSDRCore");
    ClickTab(cut, "History");

    StubSourceUnreadable();
    await FireSourceChangedAsync(cut);

    StubSource("Bluetooth");
    StubBluetooth(connected: false);
    await PollAsync(cut);
    AssertView(cut, "Bluetooth");
    TabLabels(cut).Should().Equal("Connect", "History");
  }

  [Fact]
  public async Task SourceUnreadableOnFirstMount_IsRetriedByThePoll()
  {
    StubSourceUnreadable();
    StubQueue(0);
    var cut = RenderComponent<QueueHistoryPanel>();
    WaitForInit(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().BeEmpty();

    StubSource("RTLSDRCore");
    await PollAsync(cut);
    AssertView(cut, "Radio");
  }

  [Fact]
  public async Task NoPrimarySource_404_IsARealChange()
  {
    var cut = RenderOn("RTLSDRCore");
    ClickTab(cut, "History");

    StubSource(null); // 404: the API says there is no primary source
    await FireSourceChangedAsync(cut);
    AssertView(cut, "History");
    TabLabels(cut).Should().BeEmpty();

    StubSource("RTLSDRCore");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "Radio");
  }

  [Fact]
  public async Task Poll_WithTheSourceReadAndNotBluetooth_MakesNoRequests()
  {
    var cut = RenderOn("Vinyl");
    int before = _api.Requests.Count;

    await PollAsync(cut);

    _api.Requests.Count.Should().Be(before);
  }

  // --- review fixes: a scan the Connect panel started is stopped when it goes away ---

  private void StartScan(IRenderedComponent<QueueHistoryPanel> cut)
  {
    cut.Find(".bt-scan-button").Click();
    cut.WaitForAssertion(() => _api.Requests.Should().Contain(r =>
      r.Method == HttpMethod.Post && r.Path == "/api/bluetooth/discovery/start"), TimeSpan.FromSeconds(10));
  }

  private bool StopRequested() =>
    _api.Requests.Any(r => r.Method == HttpMethod.Post && r.Path == "/api/bluetooth/discovery/stop");

  [Fact]
  public void BluetoothScan_StartedByThePanel_IsStoppedWhenTheTabChanges()
  {
    _api.Post("/api/bluetooth/discovery/start").Post("/api/bluetooth/discovery/stop");
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);
    StartScan(cut);
    StopRequested().Should().BeFalse();

    ClickTab(cut, "History");
    cut.WaitForAssertion(() => StopRequested().Should().BeTrue(), TimeSpan.FromSeconds(10));
  }

  [Fact]
  public async Task BluetoothScan_StartedByThePanel_IsStoppedWhenTheSourceChanges()
  {
    _api.Post("/api/bluetooth/discovery/start").Post("/api/bluetooth/discovery/stop");
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);
    StartScan(cut);

    StubSource("Vinyl");
    await FireSourceChangedAsync(cut);
    cut.WaitForAssertion(() => StopRequested().Should().BeTrue(), TimeSpan.FromSeconds(10));
  }

  [Fact]
  public void BluetoothPanel_WithNoScanStarted_DoesNotStopDiscoveryWhenItGoesAway()
  {
    var cut = RenderOn("Bluetooth", bluetoothConnected: false);
    ClickTab(cut, "History");
    AssertView(cut, "History");

    StopRequested().Should().BeFalse("a scan started elsewhere (the /bluetooth page) is not this panel's to stop");
  }

  // --- review fixes: tab strip semantics ---

  [Fact]
  public void HistoryOnlySource_RendersNoTablist_AndNoTabpanelRole()
  {
    var cut = RenderOn("Vinyl");

    cut.FindAll("[role=tablist]").Count.Should().Be(0);
    cut.FindAll("[role=tabpanel]").Count.Should().Be(0);
    cut.FindAll(".centre-stats-chip").Count.Should().Be(1, "the chip still shows, outside any tablist");
  }

  [Fact]
  public void Tablist_HoldsOnlyTabs_AndTheBodyIsItsLabelledTabpanel()
  {
    var cut = RenderOn("FilePlayer", queueCount: 2);

    var tablist = cut.Find("[role=tablist]");
    tablist.Children.Should().OnlyContain(e => e.GetAttribute("role") == "tab");
    tablist.QuerySelectorAll(".queue-split-kebab, .centre-stats-chip").Length.Should().Be(0);
    cut.FindAll(".queue-split-kebab").Count.Should().Be(1, "File Player's queue view keeps its kebab, beside the tablist");

    var panel = cut.Find("[role=tabpanel]");
    var active = cut.Find(".queue-split-tab.is-active");
    panel.GetAttribute("aria-labelledby").Should().Be(active.Id);
    active.GetAttribute("aria-controls").Should().Be(panel.Id);
  }
}
