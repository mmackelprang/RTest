using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radzen;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Components.Layout;

/// <summary>
/// The console-playback chip in MainLayout's topbar — the global stop control that earns "playback
/// survives navigation" (ADR-029 §7.2 / handoff §Cross-3).
/// </summary>
/// <remarks>
/// ⚠ These render the REAL MainLayout. MainLayoutTests has said since it was written that the layout
/// "requires extensive JSInterop and service configuration" and asserted <c>Assert.True(true)</c>
/// instead; the harness below is what that sentence was standing in for. It needs the layout's 24
/// injected services plus two more its child components pull in, an OfflineHubTransport for each hub
/// service, and stub IOptionsMonitors — but it does render, so the chip is pinned by its own markup
/// rather than by the derivations behind it.
///
/// ⚠ What these still do NOT prove: that the chip lands in the empty span at x≈700 on a 1920×720
/// panel. That is a look, and §3 U1's "a cyan Voicemail chip appears in the top bar" is as close as
/// this row gets to checking it.
/// </remarks>
public class ConsolePlaybackChipTests : TestContext
{
  private AudioStateStore _store = default!;

  private static EventPlaybackSnapshotDto Snapshot(string state, string? kind = "RemoteMedia") =>
    new("evp-1", kind, "Voicemail from Jane", state, TimeSpan.FromSeconds(42), TimeSpan.Zero,
      DateTimeOffset.UtcNow, null);

  private IRenderedComponent<Radio.Web.Components.Layout.MainLayout> RenderLayout()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
    Services.AddHermeticTestRig();
    Services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    Services.AddSingleton<IOptionsMonitor<DevicesOptions>>(
      new StubOptionsMonitor<DevicesOptions>(new DevicesOptions()));
    Services.AddSingleton<IOptionsMonitor<DisplayOptions>>(
      new StubOptionsMonitor<DisplayOptions>(new DisplayOptions()));

    HttpClient Api() =>
      new(new MockHttpHandler("{}")) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

    Services.AddSingleton(new SystemApiService(Api(), NullLogger<SystemApiService>.Instance));
    Services.AddSingleton(new SourcesApiService(Api(), NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(new DevicesApiService(Api(), NullLogger<DevicesApiService>.Instance));
    Services.AddSingleton(new AudioApiService(Api(), NullLogger<AudioApiService>.Instance));
    Services.AddSingleton(new QueueApiService(Api(), NullLogger<QueueApiService>.Instance));
    Services.AddSingleton(new IntegrationsApiService(
      Api(), NullLogger<IntegrationsApiService>.Instance));
    Services.AddSingleton(new EventPlaybackApiService(
      Api(), NullLogger<EventPlaybackApiService>.Instance));
    Services.AddSingleton(new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton<DeviceDisplayStateService>();
    Services.AddSingleton<RadioPanelToggleService>();
    Services.AddSingleton<GainPopoverService>();
    Services.AddSingleton<PhoneUnreadState>();
    Services.AddSingleton<EncoderFaultAnnouncer>();
    Services.AddSingleton<VisualizerTelemetryService>();
    Services.AddSingleton(new EncoderHudService());
    Services.AddSingleton(sp => new BellHealthService(
      sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<BellHealthService>.Instance, 15));

    _store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));
    Services.AddSingleton(_store);
    Services.AddSingleton(sp => new ConsolePlaybackState(
      sp.GetRequiredService<AudioStateStore>(), NullLogger<ConsolePlaybackState>.Instance));

    return RenderComponent<Radio.Web.Components.Layout.MainLayout>();
  }

  private Task BroadcastAsync(
    IRenderedComponent<Radio.Web.Components.Layout.MainLayout> cut,
    EventPlaybackSnapshotDto snapshot) =>
    cut.InvokeAsync(() => _store.OnHubEventPlaybackChanged(snapshot));

  [Fact]
  public void TheChipIsAbsentWhenNothingIsPlaying()
  {
    var cut = RenderLayout();

    // It occupies no space at rest, and "at rest" is the state the panel is in almost all the time.
    Assert.Empty(cut.FindAll(".nav-pill-playing"));
  }

  [Fact]
  public async Task TheChipAppearsForAWaitingPlayback()
  {
    var cut = RenderLayout();

    // ⚠ Waiting, not Playing, and that is the point: PHN-1f §0.6 requires the chip to treat a queued
    // playback as LIVE and STOPPABLE. D28's wait-then-play queue has been live on the appliance since
    // PHN-1f deployed with no surface rendering it, and this is that surface.
    await BroadcastAsync(cut, Snapshot("Waiting"));

    Assert.Single(cut.FindAll(".nav-pill-playing"));

    // Handoff §Cross-3: the LABEL is the kind, never the sender.
    //
    // ⚠ Equal, not Contains, and the DoesNotContain is not redundant with it. The fixture's Label is
    // "Voicemail from Jane", so a Contains("Voicemail") passes just as happily on
    // @ConsolePlayback.Snapshot?.Label — which is the exact mutation §Cross-3 forbids and this
    // assertion exists to catch. Verified by making that mutation in MainLayout and watching this
    // test red.
    var label = cut.Find(".nav-pill-playing .nav-pill-label").TextContent.Trim();
    Assert.Equal("Voicemail", label);
    Assert.DoesNotContain("Jane", label);
  }

  [Fact]
  public async Task TheChipIsAbsentForACompletedPlayback()
  {
    var cut = RenderLayout();

    await BroadcastAsync(cut, Snapshot("Playing"));
    Assert.Single(cut.FindAll(".nav-pill-playing"));

    // ⚠ A terminal snapshot is RETAINED in the store rather than nulled, so the chip must gate on
    // IsLive and not on the snapshot's presence. Gating on "Snapshot is not null" would leave a stop
    // control on screen for something that already ended.
    await BroadcastAsync(cut, Snapshot("Completed"));
    Assert.Empty(cut.FindAll(".nav-pill-playing"));
  }

  [Fact]
  public async Task TheChipIsAButton_AndASiblingOfTheNavPills()
  {
    var cut = RenderLayout();
    await BroadcastAsync(cut, Snapshot("Playing"));

    // A real <button>, not a marker on the /phone pill: that pill's bottom-right is already the
    // bell-fault glyph, which is pointer-events: none, and this must be tappable.
    var chip = cut.Find(".nav-pill-playing");
    Assert.Equal("BUTTON", chip.TagName.ToUpperInvariant());

    // In .topbar-primary and OUTSIDE .topbar-nav — the empty span between the Out picker and the nav
    // pills, which is where handoff §Cross-3's diagram puts it.
    Assert.Single(cut.FindAll(".topbar-primary > .nav-pill-playing"));
    Assert.Empty(cut.FindAll(".topbar-nav .nav-pill-playing"));
  }

  private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
  {
    public StubOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
}
