using Bunit;
using Microsoft.AspNetCore.Components;
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
/// ENC-4a's topbar MUTED chip, as amended by the main-page status-bar redesign (owner decision,
/// 2026-10-06): hidden on "/" only, where NowPlayingPanel's status bar shows mute in its own slot,
/// and still shown on every other route. Rendering set-up mirrors <see cref="MainLayoutNavTests"/>.
/// </summary>
public class MainLayoutMuteChipTests : TestContext
{
  private IRenderedComponent<Radio.Web.Components.Layout.MainLayout> RenderLayout(string? navigateTo = null)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
    Services.AddHermeticTestRig();
    Services.AddOfflineIncomingCallBanner();
    Services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    Services.AddSingleton<IOptionsMonitor<DevicesOptions>>(new StubOptionsMonitor<DevicesOptions>(new DevicesOptions()));
    Services.AddSingleton<IOptionsMonitor<DisplayOptions>>(new StubOptionsMonitor<DisplayOptions>(new DisplayOptions()));

    HttpClient Api() => new(new MockHttpHandler("{}")) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

    Services.AddSingleton(new SystemApiService(Api(), NullLogger<SystemApiService>.Instance));
    Services.AddSingleton(new SourcesApiService(Api(), NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(new DevicesApiService(Api(), NullLogger<DevicesApiService>.Instance));
    Services.AddSingleton(new AudioApiService(Api(), NullLogger<AudioApiService>.Instance));
    Services.AddSingleton(new QueueApiService(Api(), NullLogger<QueueApiService>.Instance));
    Services.AddSingleton(new IntegrationsApiService(Api(), NullLogger<IntegrationsApiService>.Instance));
    Services.AddSingleton(new EventPlaybackApiService(Api(), NullLogger<EventPlaybackApiService>.Instance));
    Services.AddSingleton(new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton<DeviceDisplayStateService>();
    Services.AddSingleton<CentrePanelViewService>();
    Services.AddSingleton<PhoneUnreadState>();
    Services.AddSingleton<EncoderFaultAnnouncer>();
    Services.AddSingleton<VisualizerTelemetryService>();
    Services.AddSingleton(new EncoderHudService());
    Services.AddSingleton(sp => new BellHealthService(
      sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<BellHealthService>.Instance, 15));

    var store = new AudioStateStore(
      NullLogger<AudioStateStore>.Instance,
      new AudioStateHubService(
        NullLogger<AudioStateHubService>.Instance,
        new ConfigurationBuilder().Build(),
        transport: new OfflineHubTransport()));
    Services.AddSingleton(store);
    Services.AddSingleton(sp => new ConsolePlaybackState(
      sp.GetRequiredService<AudioStateStore>(), NullLogger<ConsolePlaybackState>.Instance));

    // Resolving NavigationManager builds the provider, so it happens only after every registration.
    if (navigateTo != null)
    {
      Services.GetRequiredService<NavigationManager>().NavigateTo(navigateTo);
    }

    return RenderComponent<Radio.Web.Components.Layout.MainLayout>();
  }

  /// <summary>Mutes the console the way the API reports it: a VolumeChanged broadcast.</summary>
  private async Task MuteAsync(IRenderedComponent<Radio.Web.Components.Layout.MainLayout> cut)
  {
    var hub = Services.GetRequiredService<AudioStateHubService>();
    await cut.InvokeAsync(() => HubEventFire.FireAsync<VolumeDto?>(
      hub, nameof(AudioStateHubService.VolumeChanged), new VolumeDto(0.5f, true)));
  }

  [Theory]
  [InlineData("/queue")]
  [InlineData("/phone")]
  [InlineData("/history")]
  public async Task MutedChip_IsShown_OnEveryRouteButHome(string route)
  {
    var cut = RenderLayout(navigateTo: route);

    await MuteAsync(cut);

    Assert.Single(cut.FindAll(".topbar-mute-chip"));
  }

  [Fact]
  public async Task MutedChip_IsHidden_OnHome()
  {
    var cut = RenderLayout(navigateTo: "/");

    await MuteAsync(cut);

    Assert.Empty(cut.FindAll(".topbar-mute-chip"));
  }

  [Fact]
  public async Task MutedChip_FollowsNavigation_IntoAndOutOfHome()
  {
    var cut = RenderLayout(navigateTo: "/queue");
    await MuteAsync(cut);
    Assert.Single(cut.FindAll(".topbar-mute-chip"));

    var nav = Services.GetRequiredService<NavigationManager>();
    await cut.InvokeAsync(() => nav.NavigateTo("/"));
    Assert.Empty(cut.FindAll(".topbar-mute-chip"));

    await cut.InvokeAsync(() => nav.NavigateTo("/history"));
    Assert.Single(cut.FindAll(".topbar-mute-chip"));
  }

  [Fact]
  public void MutedChip_IsHidden_WhenNotMuted_OffHome()
  {
    // Guards the positive tests above: the chip appears because of the mute, not the route.
    var cut = RenderLayout(navigateTo: "/queue");

    Assert.Empty(cut.FindAll(".topbar-mute-chip"));
  }

  private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
  {
    public StubOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
}
