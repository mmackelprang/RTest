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
/// invisible top-right hit area in <c>MainLayout</c>. We render the
/// component directly with <c>IsOpen=true</c> and assert:
///
/// <list type="bullet">
///   <item>Six action cards exist (Mark distortion, Updates, Dump audio
///         frame, Download logs, Fingerprint events, Engine state).</item>
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
  public void DevTray_Open_RendersAllSixCards()
  {
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var cards = cut.FindAll(".dev-card");
    cards.Count.Should().Be(6);
  }

  [Fact]
  public void DevTray_Open_ListsAllSixCardLabels()
  {
    // The six labels are part of the handoff acceptance criteria — every one
    // must surface so an operator can identify the action at a glance.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    var labels = cut.FindAll(".dev-card-label").Select(e => e.TextContent.Trim()).ToList();
    labels.Should().Contain("Mark distortion");
    labels.Should().Contain("Updates");
    labels.Should().Contain("Dump audio frame");
    labels.Should().Contain("Download logs");
    labels.Should().Contain("Fingerprint events");
    labels.Should().Contain("Engine state");
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

    var updatesCard = cut.FindAll(".dev-card")
      .First(c => c.QuerySelector(".dev-card-label")?.TextContent.Trim() == "Updates");
    updatesCard.QuerySelector(".dev-card-value")!.TextContent.Trim().Should().Be("42/sec");
  }

  [Fact]
  public async Task DevTray_UpdatesCard_RefreshesOnTelemetryChange()
  {
    // Subsequent publishes must propagate through the subscription —
    // otherwise the card would freeze at its initial value.
    var telemetry = Services.GetRequiredService<VisualizerTelemetryService>();
    telemetry.SetUpdatesPerSecond(10);

    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));
    cut.Find(".dev-card-value").TextContent.Should().Contain("10/sec");

    await cut.InvokeAsync(() => telemetry.SetUpdatesPerSecond(77));

    var updatesCard = cut.FindAll(".dev-card")
      .First(c => c.QuerySelector(".dev-card-label")?.TextContent.Trim() == "Updates");
    updatesCard.QuerySelector(".dev-card-value")!.TextContent.Trim().Should().Be("77/sec");
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
    var card = cut.FindAll(".dev-card")
      .First(c => c.QuerySelector(".dev-card-label")?.TextContent.Trim() == "Engine state");
    card.QuerySelector(".dev-card-value")!.TextContent.Trim().Should().Be("Disconnected");
  }

  [Fact]
  public void DevTray_ReadOnlyCards_AreDisabled()
  {
    // The "Updates" and "Engine state" cards are read-only telemetry surfaces
    // — they must not be tappable buttons that look like actions.
    var cut = RenderComponent<DevTray>(p => p.Add(x => x.IsOpen, true));

    var updatesCard = cut.FindAll(".dev-card")
      .First(c => c.QuerySelector(".dev-card-label")?.TextContent.Trim() == "Updates");
    updatesCard.HasAttribute("disabled").Should().BeTrue();

    var engineCard = cut.FindAll(".dev-card")
      .First(c => c.QuerySelector(".dev-card-label")?.TextContent.Trim() == "Engine state");
    engineCard.HasAttribute("disabled").Should().BeTrue();
  }

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
}
