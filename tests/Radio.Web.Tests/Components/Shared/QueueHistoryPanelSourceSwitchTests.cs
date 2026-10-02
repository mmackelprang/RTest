using System.Net;
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
/// UI-35: a source switch moves the centre panel at once. Owner, 2026-10-02: <i>"switching between audio
/// sources takes quite a while to update the UI ... It used to feel quite a bit snappier."</i> The panel
/// used to await <c>GET /api/queue/full</c> (1.4–4.7 s on the appliance) before applying the new
/// source's tab, on every SourceChanged — twice per switch, and for every source.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from <see cref="QueueHistoryPanelTests"/> (whose set-up it copies) so this row's tests do
/// not collide with concurrent edits to that file.
/// </para>
/// <para>
/// ⚠ No wall clock is raced. "Before the queue read completes" is made a fact rather than a timing:
/// the read is HELD by <see cref="RoutedApiHandler.Hold"/> and released only after the assertion. Under
/// the old ordering the tab cannot change while the read is held, so those assertions fail (by
/// <c>WaitForAssertion</c>'s bound) instead of passing by luck.
/// </para>
/// </remarks>
public class QueueHistoryPanelSourceSwitchTests : TestContext
{
  private const string QueuePath = "/api/queue/full";
  private readonly RoutedApiHandler _api = new();

  private HttpClient NewClient() =>
    new(_api, disposeHandler: false) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

  public QueueHistoryPanelSourceSwitchTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build());
    Services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();
    Services.Configure<DisplayOptions>(_ => { });

    Services.AddSingleton(_ => new QueueApiService(NewClient(), NullLogger<QueueApiService>.Instance));
    Services.AddSingleton(_ => new PlayHistoryApiService(NewClient(), NullLogger<PlayHistoryApiService>.Instance));
    Services.AddSingleton(_ => new AudioApiService(NewClient(), NullLogger<AudioApiService>.Instance));
    Services.AddSingleton(_ => new FileApiService(NewClient(), NullLogger<FileApiService>.Instance));
    Services.AddSingleton(_ => new PlaylistApiService(NewClient(), NullLogger<PlaylistApiService>.Instance));
    Services.AddSingleton(_ => new SourcesApiService(NewClient(), NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(_ => new ConfigurationApiService(NewClient(), NullLogger<ConfigurationApiService>.Instance));
    Services.AddSingleton(_ => new BluetoothApiService(NewClient(), NullLogger<BluetoothApiService>.Instance));
    Services.AddSingleton(_ => new RadioApiService(NewClient(), NullLogger<RadioApiService>.Instance));
    Services.AddOptions<RdsScrollOptions>();
    Services.AddSingleton<EncoderHudService>();
    Services.AddScoped<CentrePanelViewService>();
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance,
      sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton<QueuePersistenceService>();
  }

  private void StubSource(string sourceType) =>
    _api.Get("/api/sources/primary", new AudioSourceDto
    {
      Id = sourceType.ToLowerInvariant(),
      Name = sourceType,
      Type = sourceType,
      Category = "Primary",
      State = "Active",
    });

  private void StubQueue(int count) =>
    _api.Get(QueuePath, Enumerable.Range(0, count)
      .Select(i => new QueueItemDto(i, $"Track {i}", "Artist", "Album", "3:00", false, "Upcoming", i))
      .ToList());

  private IRenderedComponent<QueueHistoryPanel> RenderOn(string sourceType, int queueCount)
  {
    StubSource(sourceType);
    StubQueue(queueCount);
    _api.Get("/api/bluetooth/status", new BluetoothStatusDto { IsAvailable = true, State = "On" });
    IRenderedComponent<QueueHistoryPanel> cut = RenderComponent<QueueHistoryPanel>();
    cut.WaitForAssertion(
      () => _api.Requests.Should().Contain(r => r.Path == "/api/playhistory/statistics"),
      TimeSpan.FromSeconds(10));
    // The mount reads the queue once (awaited, before its tab). Rendezvous on it before counting requests.
    cut.WaitForAssertion(() => QueueReads().Should().Be(1), TimeSpan.FromSeconds(10));
    return cut;
  }

  private int QueueReads() => _api.Requests.Count(r => r.Path == QueuePath);

  private int SourceReads() => _api.Requests.Count(r => r.Path == "/api/sources/primary");

  private Task FireSourceChangedAsync(IRenderedComponent<QueueHistoryPanel> cut)
  {
    AudioStateHubService hub = Services.GetRequiredService<AudioStateHubService>();
    return cut.InvokeAsync(() => HubEventFire.FireAsync(hub, "SourceChanged"));
  }

  private static string ActiveView(IRenderedComponent<QueueHistoryPanel> cut)
  {
    if (cut.FindAll(".rcp-root").Count > 0)
    {
      return "Radio";
    }
    if (cut.FindAll(".queue-total-tile").Count > 0)
    {
      return "Queue";
    }
    return cut.FindAll(".queue-split").Count > 0 ? "History" : "?";
  }

  private static void AssertView(IRenderedComponent<QueueHistoryPanel> cut, string view) =>
    cut.WaitForAssertion(() => ActiveView(cut).Should().Be(view), TimeSpan.FromSeconds(10));

  private static void ClickTab(IRenderedComponent<QueueHistoryPanel> cut, string labelPrefix) =>
    cut.FindAll(".queue-split-tab")
      .First(t => t.TextContent.TrimStart().StartsWith(labelPrefix, StringComparison.Ordinal))
      .Click();

  /// <summary>
  /// ⭐ The owner's case: File Player → Radio. The Radio tab shows while the queue read the switch
  /// started is still held open, and the SourceChanged dispatch itself has already returned.
  /// </summary>
  [Fact]
  public async Task SwitchingAwayFromFilePlayer_ShowsTheNewTab_WhileTheQueueReadIsStillOpen()
  {
    var cut = RenderOn("FilePlayer", queueCount: 2);
    AssertView(cut, "Queue");

    TaskCompletionSource release = _api.Hold(HttpMethod.Get, QueuePath);
    StubSource("RTLSDRCore");
    Task fired = FireSourceChangedAsync(cut);

    cut.WaitForAssertion(() => QueueReads().Should().Be(2), TimeSpan.FromSeconds(10));
    AssertView(cut, "Radio");
    // The read is still held, so this completes only if the subscriber does not wait for it. Bounded so
    // the old ordering fails here rather than hanging.
    await fired.WaitAsync(TimeSpan.FromSeconds(10));

    release.SetResult();
    await fired;
    AssertView(cut, "Radio");
  }

  [Fact]
  public async Task SwitchingToFilePlayer_ShowsItsQueueTab_WhileTheQueueReadIsStillOpen()
  {
    var cut = RenderOn("Vinyl", queueCount: 2);
    AssertView(cut, "History");

    TaskCompletionSource release = _api.Hold(HttpMethod.Get, QueuePath);
    StubSource("FilePlayer");
    Task fired = FireSourceChangedAsync(cut);

    cut.WaitForAssertion(() => QueueReads().Should().Be(2), TimeSpan.FromSeconds(10));
    AssertView(cut, "Queue");

    release.SetResult();
    await fired;
    AssertView(cut, "Queue");
  }

  [Fact]
  public async Task ARepeatedSourceChangedForTheSameType_ReadsTheSourceButNotTheQueue()
  {
    var cut = RenderOn("FilePlayer", queueCount: 2);
    int sourceReads = SourceReads();

    await FireSourceChangedAsync(cut);
    await FireSourceChangedAsync(cut);

    SourceReads().Should().Be(sourceReads + 2);
    QueueReads().Should().Be(1, "only the mount read the queue");
    AssertView(cut, "Queue");
  }

  /// <summary>
  /// Review: a queue change made while the source could not be read may have been missed, so the read
  /// that recovers re-reads the queue — once — even though the source type did not change.
  /// </summary>
  [Fact]
  public async Task RecoveringFromAFailedSourceRead_ReReadsTheQueueOnce()
  {
    var cut = RenderOn("FilePlayer", queueCount: 2);

    _api.Route(HttpMethod.Get, "/api/sources/primary", HttpStatusCode.InternalServerError, null);
    await FireSourceChangedAsync(cut);
    QueueReads().Should().Be(1, "a failed source read reads nothing else");

    StubQueue(3);
    StubSource("FilePlayer");
    await cut.InvokeAsync(() => cut.Instance.PollTickAsync());
    cut.WaitForAssertion(() => QueueReads().Should().Be(2), TimeSpan.FromSeconds(10));
    cut.WaitForAssertion(() => cut.Markup.Should().Contain("Queue · 3"), TimeSpan.FromSeconds(10));

    await FireSourceChangedAsync(cut);
    QueueReads().Should().Be(2, "once recovered, a repeated SourceChanged reads only the source");
  }

  [Fact]
  public async Task ASwitchBetweenTwoSourcesWithNoQueueView_DoesNotReadTheQueue()
  {
    var cut = RenderOn("RTLSDRCore", queueCount: 2);

    StubSource("Vinyl");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "History");
    StubSource("RTLSDRCore");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "Radio");

    QueueReads().Should().Be(1, "neither Radio nor Vinyl shows the queue by default");
  }

  /// <summary>
  /// File Player's default depends on the queue's length, which the switch no longer waits for: the
  /// tab is chosen from the count already held, then re-chosen when the fresh count lands.
  /// </summary>
  [Fact]
  public async Task FilePlayersDefault_IsReappliedWhenTheFreshQueueCountLands()
  {
    var cut = RenderOn("Vinyl", queueCount: 0);

    TaskCompletionSource release = _api.Hold(HttpMethod.Get, QueuePath);
    StubQueue(3);
    StubSource("FilePlayer");
    Task fired = FireSourceChangedAsync(cut);
    AssertView(cut, "History"); // the held count is 0
    // The dispatch has returned with the read still held: the Queue tab below can only come from the
    // background read's re-apply, not from a SyncSourceAsync that waited for the read.
    await fired.WaitAsync(TimeSpan.FromSeconds(10));
    AssertView(cut, "History");

    release.SetResult();
    AssertView(cut, "Queue");
  }

  [Fact]
  public async Task AViewTappedBeforeTheQueueCountLands_IsNotOverriddenByIt()
  {
    var cut = RenderOn("Vinyl", queueCount: 3);

    TaskCompletionSource release = _api.Hold(HttpMethod.Get, QueuePath);
    StubSource("FilePlayer");
    Task fired = FireSourceChangedAsync(cut);
    AssertView(cut, "Queue");
    ClickTab(cut, "History");
    AssertView(cut, "History");

    release.SetResult();
    await fired;
    cut.WaitForAssertion(() => cut.Markup.Should().Contain("Queue · 3"), TimeSpan.FromSeconds(10));
    AssertView(cut, "History");
  }

  [Fact]
  public async Task AQueueReadLandingAfterASwitchAway_LeavesTheNewSourcesTabAlone()
  {
    var cut = RenderOn("Vinyl", queueCount: 0);

    // Switch to File Player (queue read held), then away and back before it lands.
    TaskCompletionSource first = _api.Hold(HttpMethod.Get, QueuePath);
    StubSource("FilePlayer");
    Task toFilePlayer = FireSourceChangedAsync(cut);
    cut.WaitForAssertion(() => QueueReads().Should().Be(2), TimeSpan.FromSeconds(10));

    StubSource("RTLSDRCore");
    await FireSourceChangedAsync(cut);
    AssertView(cut, "Radio");

    StubQueue(3);
    first.SetResult();
    await toFilePlayer;
    cut.WaitForAssertion(() => QueueReads().Should().Be(3), TimeSpan.FromSeconds(10));
    AssertView(cut, "Radio");
  }
}
