using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="DevTray"/> developer surface (handoff
/// §P2·2, PR 6 of the design tightening arc).
///
/// The tray is normally hidden and revealed by a 3-tap gesture on the
/// invisible hit area left of the Home pill in <c>MainLayout</c>. We render the
/// component directly with <c>IsOpen=true</c> and assert:
///
/// <list type="bullet">
///   <item>Five action cards (Mark distortion, Dump audio frame, Download logs, Fingerprint events, and
///         LOG-5's Verbose logs) and two read-only readings (Updates, Engine state) exist, and since
///         UI-25 the two kinds are rendered as different elements with different affordances.</item>
///   <item>The "Updates" card reflects the current
///         <see cref="VisualizerTelemetryService.UpdatesPerSecond"/> value
///         and updates when the singleton publishes a new value.</item>
///   <item>The auto-lock header text is shaped <c>0:NN</c> on render.</item>
///   <item>The × close button raises the <c>OnClose</c> callback.</item>
///   <item>When <c>IsOpen=false</c> the tray still renders (we mount
///         persistently) but lacks the <c>is-open</c> class.</item>
/// </list>
/// </summary>
public class DevTrayTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;
  private readonly FakeLoggingApi _loggingApi;

  public DevTrayTests()
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

    Services.AddHttpClient<AudioApiService>();

    // LOG-5: the Verbose logs card talks to radio-api's logging endpoint through SystemApiService.
    // A stateful fake stands in for the API so the card's round trip is observable.
    _loggingApi = new FakeLoggingApi();
    Services.AddSingleton(new SystemApiService(
      new HttpClient(_loggingApi) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<SystemApiService>.Instance));

    Services.AddSingleton(sp =>
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        sp.GetRequiredService<IConfiguration>(),
        transport: new OfflineHubTransport()
      )
    );

    Services.AddSingleton<VisualizerTelemetryService>();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  // OPS-14: the API docs note. It must name the address another device uses (the box's name, not the
  // localhost radio-web talks to) and must never be a link — a page outside the app strands the kiosk.
  [Theory]
  [InlineData("http://localhost:5000", "RADIO", "http://radio:5000/scalar/v1")]
  [InlineData("http://localhost:5100/", "dev-pc", "http://dev-pc:5100/scalar/v1")]
  [InlineData("not a url", "radio", null)]
  [InlineData("", "radio", null)]
  [InlineData("http://localhost:5000", "", null)]
  public void BuildApiDocsUrl_UsesTheMachineNameAndTheApiPort(string apiBaseUrl, string machine, string? expected)
  {
    DevTray.BuildApiDocsUrl(apiBaseUrl, machine).Should().Be(expected);
  }

  [Fact]
  public void ApiDocsNote_IsPlainText_NeverALink()
  {
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.ApiDocsUrl, "http://radio:5000/scalar/v1"));

    var note = cut.Find(".dev-tray-note");
    note.TextContent.Should().Contain("http://radio:5000/scalar/v1");
    note.TextContent.Should().Contain("open on another device");
    cut.FindAll(".dev-tray a").Should().BeEmpty();
    note.QuerySelectorAll("button").Should().BeEmpty();
  }

  [Fact]
  public void ApiDocsNote_IsHidden_WithoutAnAddress()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    cut.FindAll(".dev-tray-note").Should().BeEmpty();
  }

  [Fact]
  public void DevTray_Closed_StillMounts()
  {
    // The tray is mounted persistently inside MainLayout so the
    // VisualizerTelemetryService subscription stays alive. IsOpen=false just
    // strips the .is-open class — the element still exists in the DOM.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, false));
    cut.FindAll(".dev-tray").Count.Should().Be(1);
    (cut.Find(".dev-tray").GetAttribute("class") ?? string.Empty).Should().NotContain("is-open");
  }

  [Fact]
  public void DevTray_Open_AppliesIsOpenClass()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    (cut.Find(".dev-tray").GetAttribute("class") ?? string.Empty).Should().Contain("is-open");
  }

  [Fact]
  public void DevTray_Open_RendersFiveActionsAndTwoReadings()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    cut.FindAll(".dev-card").Count.Should().Be(5);
    cut.FindAll(".dev-readout").Count.Should().Be(2);
  }

  [Fact]
  public void DevTray_Open_ListsAllSevenCardLabels()
  {
    // The six handoff labels (plus LOG-5's Verbose logs) are part of the acceptance criteria — every one
    // must surface so an operator can identify the action at a glance.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var labels = cut.FindAll(".dev-card-label, .dev-readout-label").Select(e => e.TextContent.Trim()).ToList();
    labels.Should().Contain("Mark distortion");
    labels.Should().Contain("Updates");
    labels.Should().Contain("Dump audio frame");
    labels.Should().Contain("Download logs");
    labels.Should().Contain("Fingerprint events");
    labels.Should().Contain("Engine state");
    labels.Should().Contain("Verbose logs");
  }

  /// <summary>
  /// UI-5 / UI-2: the card used to navigate to <c>/metrics</c>, which UI-2 deleted. It now lands on
  /// Settings → Diagnostics, where the <c>fingerprint.*</c> tiles live.
  /// </summary>
  [Fact]
  public void FingerprintEventsCard_NavigatesToDiagnostics_AndClosesTheTray()
  {
    var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    var closed = 0;
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.OnClose, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { closed++; })));

    cut.Find("button[aria-label='View fingerprint events']").Click();

    new Uri(nav.Uri).AbsolutePath.Should().Be("/diagnostics");
    closed.Should().Be(1, "the tray must not float over the page it just opened");
  }

  [Fact]
  public void DevTray_Header_DeclaresDialogRoleAndAriaLabel()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var root = cut.Find(".dev-tray");
    root.GetAttribute("role").Should().Be("dialog");
    root.GetAttribute("aria-label").Should().Be("Dev tray");
  }

  [Fact]
  public void DevTray_Closed_HidesFromAccessibilityTree()
  {
    // aria-hidden flips so screen readers don't announce the closed tray.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, false));
    cut.Find(".dev-tray").GetAttribute("aria-hidden").Should().Be("true");
  }

  [Fact]
  public void DevTray_Header_ContainsAutoLockCountdown()
  {
    // The header status string includes the auto-lock countdown so the
    // operator can see exactly how long they have before the tray re-locks.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var header = cut.Find(".dev-tray-status").TextContent;
    header.Should().Contain("auto-lock");
    header.Should().MatchRegex(@"0:\d{2}");
  }

  [Fact]
  public void DevTray_UpdatesCard_ReadsTelemetrySingleton()
  {
    // Seed the singleton before rendering — the card must pick up the seeded
    // value on first paint, not just on subsequent publishes.
    var telemetry = Services.GetRequiredService<VisualizerTelemetryService>();
    telemetry.SetUpdatesPerSecond(42);

    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Readout(cut, "Updates").Should().Be("42/sec");
  }

  [Fact]
  public async Task DevTray_UpdatesCard_RefreshesOnTelemetryChange()
  {
    // Subsequent publishes must propagate through the subscription —
    // otherwise the card would freeze at its initial value.
    var telemetry = Services.GetRequiredService<VisualizerTelemetryService>();
    telemetry.SetUpdatesPerSecond(10);

    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    Readout(cut, "Updates").Should().Be("10/sec");

    await cut.InvokeAsync(() => telemetry.SetUpdatesPerSecond(77));

    Readout(cut, "Updates").Should().Be("77/sec");
  }

  [Fact]
  public void DevTray_CloseButton_RaisesOnCloseCallback()
  {
    var closed = false;
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.OnClose, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { closed = true; }))
    );

    cut.Find(".dev-tray-close").Click();
    closed.Should().BeTrue();
  }

  [Fact]
  public void DevTray_MarkDistortionCard_HasClickHandler()
  {
    // We don't have a fake HttpClient that records POSTs, but we can verify
    // the button is wired (button rendered, not disabled, has the
    // "Mark audio distortion" aria-label). The handler itself is exercised
    // by integration UAT.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var card = cut.FindAll(".dev-card")
      .First(c => c.GetAttribute("aria-label") == "Mark audio distortion");
    card.HasAttribute("disabled").Should().BeFalse();
  }

  [Fact]
  public void DevTray_EngineStateCard_RendersInitialState()
  {
    // The engine-state card reads AudioStateHubService.ConnectionState as a
    // proxy for "is the audio engine reachable". In a unit-test rig with no
    // running hub it reads "Disconnected".
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    Readout(cut, "Engine state").Should().Be("Disconnected");
  }

  [Fact]
  public void DevTray_ReadOnlyReadings_AreNotButtons_AndCarryNoActionAffordance()
  {
    // UI-25. "Updates" and "Engine state" are read-only telemetry. They used to be disabled
    // <button class="dev-card">s styled exactly like the actions; now they are not buttons at all
    // and carry none of the action classes, so nothing about them invites a tap.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    foreach (var reading in cut.FindAll(".dev-readout"))
    {
      reading.TagName.Should().NotBe("BUTTON");
      reading.Closest("button").Should().BeNull();
      reading.ClassList.Should().NotContain("dev-card");
      reading.QuerySelector(".dev-card-icon").Should().BeNull();
    }
  }

  [Fact]
  public void DevTray_EveryAction_IsAnEnabledButtonWithTheActionAffordance()
  {
    // UI-25. Every tappable card is a <button> carrying the action class (raised, accent-bordered,
    // pressed state in design-system.css §Q) and an accent icon, and is enabled at rest.
    var cut = RenderOpenAndSettled();

    var actions = cut.FindAll(".dev-card");
    actions.Should().HaveCount(5);
    foreach (var card in actions)
    {
      card.TagName.Should().Be("BUTTON");
      card.ClassList.Should().Contain("dev-card--action");
      card.QuerySelector(".dev-card-icon").Should().NotBeNull();
      card.HasAttribute("disabled").Should().BeFalse();
    }
  }

  private static string Readout(IRenderedComponent<DevTray> cut, string label) =>
    cut.FindAll(".dev-readout")
      .First(r => r.QuerySelector(".dev-readout-label")?.TextContent.Trim() == label)
      .QuerySelector(".dev-readout-value")!.TextContent.Trim();

  // ─── Task #15 PR B (handoff item #17): auto-lock after 30s of inactivity ──
  //
  // The tray ticks an auto-lock timer every second; when 30s pass without an
  // interaction, OnClose fires. We don't want a flaky 30-second sleep in CI —
  // and the tray's Timer was deliberately not refactored to TimeProvider for
  // this PR (kept the just-shipped code untouched). Instead we back-date
  // _lastInteractionUtc and invoke TickAutoLockAsync directly via reflection.
  // That exercises the same elapsed-seconds branch a real 30s wait would hit.

  [Fact]
  public async Task DevTray_AutoLock_FiresOnCloseAfter30SecondsOfNoInteraction()
  {
    var closeCount = 0;
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.OnClose, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(
        this, () => { closeCount++; })));

    var instance = cut.Instance;
    var type = typeof(DevTray);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    // Back-date the last-interaction timestamp 31s into the past — the
    // elapsed-seconds check in TickAutoLockAsync will compute 31 ≥ 30 and
    // fire OnClose. The synthetic shift mirrors what a real wall-clock
    // 30s wait would produce, without the flakiness.
    type.GetField("_lastInteractionUtc", flags)!
      .SetValue(instance, DateTime.UtcNow.AddSeconds(-31));

    var tick = type.GetMethod("TickAutoLockAsync", flags)!;
    await cut.InvokeAsync(async () => await (Task)tick.Invoke(instance, null)!);

    closeCount.Should().Be(1,
      "the auto-lock window elapsed (31s ≥ 30s threshold) — OnClose must fire exactly once");
  }

  [Fact]
  public async Task DevTray_AutoLock_TimerResets_OnInteraction()
  {
    var closeCount = 0;
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.OnClose, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(
        this, () => { closeCount++; })));

    var instance = cut.Instance;
    var type = typeof(DevTray);
    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

    // Stage 1: back-date interaction 25s. Tick — still inside the window, no close.
    type.GetField("_lastInteractionUtc", flags)!
      .SetValue(instance, DateTime.UtcNow.AddSeconds(-25));
    var tick = type.GetMethod("TickAutoLockAsync", flags)!;
    await cut.InvokeAsync(async () => await (Task)tick.Invoke(instance, null)!);
    closeCount.Should().Be(0, "25s elapsed is inside the 30s window — must not auto-close");

    // Stage 2: simulate an interaction. RecordInteraction resets the
    // last-interaction timestamp to "now". Then back-date another 25s,
    // tick, and the timer is still inside the window — total elapsed since
    // the FIRST observation is now 50s, but the window was reset 25s ago.
    var record = type.GetMethod("RecordInteraction", flags)!;
    record.Invoke(instance, null);
    type.GetField("_lastInteractionUtc", flags)!
      .SetValue(instance, DateTime.UtcNow.AddSeconds(-25));
    await cut.InvokeAsync(async () => await (Task)tick.Invoke(instance, null)!);

    closeCount.Should().Be(0,
      "interaction must reset the auto-lock window — close handler must not fire");
  }

  // ── UI-25: the tray opens where it was tapped ─────────────────────────────────────────────────

  [Fact]
  public void Placement_Below_IsWrittenInline()
  {
    var placement = DevTrayPlacement.ForPress(1200, 30, 30, 1920, 720);
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.Placement, placement));

    var style = cut.Find(".dev-tray").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("left:960px").And.Contain("top:38px").And.Contain("right:auto").And.Contain("bottom:auto");
    style.Should().Contain("width:480px").And.Contain("--dev-tray-open-h:440px");
  }

  [Fact]
  public void Placement_Above_IsWrittenAsABottomOffset()
  {
    var placement = DevTrayPlacement.ForPress(900, 650, 650, 1920, 720);
    var cut = RenderComponent<DevTray>(p => p
      .Add(x => x.IsOpen, true)
      .Add(x => x.Placement, placement));

    var style = cut.Find(".dev-tray").GetAttribute("style") ?? string.Empty;
    style.Should().Contain("bottom:78px").And.Contain("top:auto").And.Contain("left:660px");
  }

  [Fact]
  public void NoPlacement_LeavesTheStylesheetDefaultPosition()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var style = cut.Find(".dev-tray").GetAttribute("style") ?? string.Empty;
    style.Should().NotContain("left:").And.NotContain("top:").And.NotContain("bottom:");
  }

  // ── UI-26: no action may navigate the kiosk away from the app ─────────────────────────────────

  private const string DownloadFn = "fileDownload.downloadFromStream";

  private static AngleSharp.Dom.IElement Card(IRenderedComponent<DevTray> cut, string ariaLabel) =>
    cut.Find($"button[aria-label='{ariaLabel}']");

  private static AngleSharp.Dom.IElement? Status(IRenderedComponent<DevTray> cut, string ariaLabel) =>
    Card(cut, ariaLabel).QuerySelector(".dev-card-status");

  private string StartUri() =>
    Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri;

  private void AssertStayedInTheApp(string startUri)
  {
    // The trap was JS window.open(<radio-api url>, "_blank"). Nothing may call open or touch
    // location, and the circuit's own location must not move.
    var identifiers = JSInterop.Invocations.Select(i => i.Identifier).ToList();
    identifiers.Should().NotContain("open");
    identifiers.Should().NotContain(id => id.Contains("location", StringComparison.OrdinalIgnoreCase));
    StartUri().Should().Be(startUri);
  }

  private static HttpResponseMessage Json(System.Net.HttpStatusCode code, string body) =>
    new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

  private static HttpResponseMessage FileResponse(string name, byte[] bytes)
  {
    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
    response.Content.Headers.ContentDisposition =
      new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = name };
    return response;
  }

  [Fact]
  public void DumpAudioFrame_Api501_ShowsTheApisReasonInTheCard_AndStaysInTheApp()
  {
    // Today's API: AudioDebugController.DumpAudioFrame is a deliberate 501 stub, and this is its body's
    // shape. Before UI-26 the kiosk was sent to this JSON in a new window it could not leave.
    _loggingApi.FileResponder = _ => Json((System.Net.HttpStatusCode)501,
      """{"error":"Audio-frame dump not yet implemented.","reason":"long text","tracked":"#23"}""");
    var start = StartUri();
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Card(cut, "Dump audio frame").Click();

    cut.WaitForAssertion(() =>
      Status(cut, "Dump audio frame")!.TextContent.Trim()
        .Should().Be("501 · Audio-frame dump not yet implemented"));
    Status(cut, "Dump audio frame")!.ClassList.Should().Contain("is-error");
    Card(cut, "Dump audio frame").HasAttribute("disabled")
      .Should().BeFalse("the card must be tappable again — the error is recoverable in place");
    JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain(DownloadFn);
    AssertStayedInTheApp(start);
  }

  [Fact]
  public void DumpAudioFrame_Success_SavesInPage_AndReportsNameAndSize()
  {
    var wav = new byte[] { 0x52, 0x49, 0x46, 0x46 };
    _loggingApi.FileResponder = _ => FileResponse("frame-0001.wav", wav);
    JSInterop.Setup<bool>(DownloadFn, _ => true).SetResult(true);
    var start = StartUri();
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Card(cut, "Dump audio frame").Click();

    cut.WaitForAssertion(() =>
      Status(cut, "Dump audio frame")!.TextContent.Trim().Should().Be("sent to downloads · 4 B"));
    Status(cut, "Dump audio frame")!.ClassList.Should().Contain("is-ok");
    Card(cut, "Dump audio frame").QuerySelector(".dev-card-detail")!.TextContent.Trim().Should().Be("frame-0001.wav");
    JSInterop.Invocations.Where(i => i.Identifier == DownloadFn)
      .Should().ContainSingle().Which.Arguments[0].Should().Be("frame-0001.wav");
    AssertStayedInTheApp(start);
  }

  [Fact]
  public void DownloadLogs_Success_SavesInPage_AndReportsNameAndSize()
  {
    _loggingApi.FileResponder = path => path == "/api/system/logs/download"
      ? FileResponse("radio-logs-1h-20261001-120000.zip", new byte[2048])
      : null;
    JSInterop.Setup<bool>(DownloadFn, _ => true).SetResult(true);
    var start = StartUri();
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Card(cut, "Download logs").Click();

    cut.WaitForAssertion(() =>
      Status(cut, "Download logs")!.TextContent.Trim().Should().Be("sent to downloads · 2 KB"));
    Status(cut, "Download logs")!.ClassList.Should().Contain("is-ok");
    Card(cut, "Download logs").QuerySelector(".dev-card-detail")!.TextContent.Trim()
      .Should().Be("radio-logs-1h-20261001-120000.zip");
    _loggingApi.FileRequests.Should().ContainSingle().Which.Should().Be("/api/system/logs/download?period=1h");
    AssertStayedInTheApp(start);
  }

  [Fact]
  public void DownloadLogs_ApiDown_SaysSoInTheCard_AndStaysInTheApp()
  {
    // The case window.open handled worst: an unreachable host is a browser error page.
    _loggingApi.FilesUnreachable = true;
    var start = StartUri();
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Card(cut, "Download logs").Click();

    cut.WaitForAssertion(() =>
      Status(cut, "Download logs")!.TextContent.Trim().Should().Be("radio-api unreachable"));
    Status(cut, "Download logs")!.ClassList.Should().Contain("is-error");
    AssertStayedInTheApp(start);
  }

  [Fact]
  public void DownloadLogs_Api404_ShowsOnlyTheErrorField_NotTheServerPath()
  {
    // The logs 404 body carries a server-side path; only its "error" field may reach the screen.
    _loggingApi.FileResponder = _ => Json(System.Net.HttpStatusCode.NotFound,
      """{"error":"Logs directory not found","path":"/opt/radio-console/logs"}""");
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Card(cut, "Download logs").Click();

    cut.WaitForAssertion(() =>
      Status(cut, "Download logs")!.TextContent.Trim().Should().Be("404 · Logs directory not found"));
    cut.Markup.Should().NotContain("/opt/radio-console");
  }

  [Fact]
  public void DownloadLogs_BrowserRefusesTheSave_SaysSo()
  {
    _loggingApi.FileResponder = _ => FileResponse("radio-logs.zip", new byte[10]);
    JSInterop.Setup<bool>(DownloadFn, _ => true).SetResult(false);
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    Card(cut, "Download logs").Click();

    cut.WaitForAssertion(() =>
      Status(cut, "Download logs")!.TextContent.Trim().Should().Be("failed · the browser would not take the file"));
    Status(cut, "Download logs")!.ClassList.Should().Contain("is-error");
  }

  [Fact]
  public void FileResults_AreClearedOnTheNextOpen()
  {
    _loggingApi.FilesUnreachable = true;
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    Card(cut, "Download logs").Click();
    cut.WaitForAssertion(() => Status(cut, "Download logs")!.ClassList.Should().Contain("is-error"));

    cut.SetParametersAndRender(p => p.Add(x => x.IsOpen, false));
    cut.SetParametersAndRender(p => p.Add(x => x.IsOpen, true));

    Status(cut, "Download logs").Should().BeNull();
    Card(cut, "Download logs").QuerySelector(".dev-card-hint")!.TextContent.Trim().Should().Be("last hour · .zip");
  }

  // ── LOG-5: Verbose logs card ──────────────────────────────────────────────────────────────────

  private IRenderedComponent<DevTray> RenderOpenAndSettled()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    // The first open fires a GET; wait on its rendered result, not on time.
    cut.WaitForAssertion(() => LogValue(cut).Should().NotBe("checking…"));
    return cut;
  }

  private static AngleSharp.Dom.IElement LogCard(IRenderedComponent<DevTray> cut) =>
    cut.FindAll(".dev-card").First(c => c.GetAttribute("aria-label") == "Toggle verbose logging");

  private static string LogValue(IRenderedComponent<DevTray> cut) =>
    LogCard(cut).QuerySelector(".dev-card-status")!.TextContent.Trim();

  [Fact]
  public void VerboseLogs_OnOpen_ReadsTheApisState()
  {
    var cut = RenderOpenAndSettled();
    LogValue(cut).Should().Be("config · tap for Debug");
    LogCard(cut).GetAttribute("aria-pressed").Should().Be("false");
  }

  [Fact]
  public void VerboseLogs_TapWhenOff_LowersOnlyRadioSwitchesToDebug()
  {
    var cut = RenderOpenAndSettled();

    LogCard(cut).Click();
    cut.WaitForAssertion(() => LogValue(cut).Should().Be("runtime · tap to reset"));

    // Radio and Radio.Infrastructure.Audio were above Debug and get lowered; Radio.Chatty is
    // already Verbose and must not be raised to Debug; Default is third-party and untouched.
    _loggingApi.Puts.Should().BeEquivalentTo(new[] { ("Radio", "Debug"), ("Radio.Infrastructure.Audio", "Debug") });
    _loggingApi.Level("Default").Should().Be("Warning");
    LogCard(cut).GetAttribute("aria-pressed").Should().Be("true");
  }

  [Fact]
  public void VerboseLogs_TapWhenOn_ResetsToConfiguration()
  {
    var cut = RenderOpenAndSettled();
    LogCard(cut).Click();
    cut.WaitForAssertion(() => LogValue(cut).Should().Be("runtime · tap to reset"));

    LogCard(cut).Click();
    cut.WaitForAssertion(() => LogValue(cut).Should().Be("config · tap for Debug"));

    _loggingApi.Resets.Should().Be(1);
    _loggingApi.Level("Radio").Should().Be("Information");
  }

  [Fact]
  public void VerboseLogs_ApiUnreachable_SaysSo()
  {
    _loggingApi.Unreachable = true;
    var cut = RenderOpenAndSettled();
    LogValue(cut).Should().Be("unavailable");
  }

  /// <summary>Stateful stand-in for radio-api's /api/system/logging endpoints.</summary>
  private sealed class FakeLoggingApi : HttpMessageHandler
  {
    private readonly object _sync = new();
    private readonly Dictionary<string, (string Level, string Configured)> _levels = new()
    {
      ["Default"] = ("Warning", "Warning"),
      ["Radio"] = ("Information", "Information"),
      ["Radio.Chatty"] = ("Verbose", "Verbose"),
      ["Radio.Infrastructure.Audio"] = ("Warning", "Warning"),
    };
    private readonly List<(string, string)> _puts = new();
    private int _resets;

    public bool Unreachable { get; set; }

    /// <summary>UI-26: answers the two file endpoints; null falls through to 404.</summary>
    public Func<string, HttpResponseMessage?>? FileResponder { get; set; }

    /// <summary>UI-26: when set, the file endpoints throw as if radio-api were down.</summary>
    public bool FilesUnreachable { get; set; }

    private readonly List<string> _fileRequests = new();

    /// <summary>UI-26: path and query of every request to the two file endpoints.</summary>
    public IReadOnlyList<string> FileRequests { get { lock (_sync) { return _fileRequests.ToList(); } } }

    public IReadOnlyList<(string, string)> Puts { get { lock (_sync) { return _puts.ToList(); } } }

    public int Resets { get { lock (_sync) { return _resets; } } }

    public string Level(string source) { lock (_sync) { return _levels[source].Level; } }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      if (Unreachable)
      {
        throw new HttpRequestException("unreachable");
      }

      var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
      if (path is "/api/audio/debug/dump-frame" or "/api/system/logs/download")
      {
        lock (_sync)
        {
          _fileRequests.Add(request.RequestUri!.PathAndQuery);
        }
        if (FilesUnreachable)
        {
          throw new HttpRequestException("unreachable");
        }
        return FileResponder?.Invoke(path) ?? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
      }
      const string prefix = "/api/system/logging/levels";
      lock (_sync)
      {
        if (request.Method == HttpMethod.Get && path == prefix)
        {
          var body = System.Text.Json.JsonSerializer.Serialize(
            _levels.Select(kv => new { source = kv.Key, level = kv.Value.Level, configuredLevel = kv.Value.Configured }));
          return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
          {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
          };
        }
        if (request.Method == HttpMethod.Post && path == prefix + "/reset")
        {
          _resets++;
          foreach (var key in _levels.Keys.ToList())
          {
            _levels[key] = (_levels[key].Configured, _levels[key].Configured);
          }
          return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
      }

      if (request.Method == HttpMethod.Put && path.StartsWith(prefix + "/", StringComparison.Ordinal))
      {
        var source = path[(prefix.Length + 1)..];
        using var doc = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var level = doc.RootElement.GetProperty("level").GetString()!;
        lock (_sync)
        {
          if (!_levels.ContainsKey(source))
          {
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
          }
          _puts.Add((source, level));
          _levels[source] = (level, _levels[source].Configured);
        }
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
      }

      return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }
  }
}
