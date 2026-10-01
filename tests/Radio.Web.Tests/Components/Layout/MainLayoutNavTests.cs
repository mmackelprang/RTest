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
/// UI-2 / D11: the top-level nav has no Metrics pill — diagnostics live under Settings, so the
/// Settings pill is what lights on <c>/diagnostics</c>. Rendering set-up mirrors
/// <see cref="ConsolePlaybackChipTests"/>.
/// </summary>
public class MainLayoutNavTests : TestContext
{
  private IRenderedComponent<Radio.Web.Components.Layout.MainLayout> RenderLayout(string? navigateTo = null)
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();
    Services.AddHermeticTestRig();
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
    Services.AddSingleton<GainPopoverService>();
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

  [Fact]
  public void Nav_HasNoMetricsPill()
  {
    var cut = RenderLayout();

    Assert.Empty(cut.FindAll("a[href='/metrics']"));
    Assert.DoesNotContain(
      cut.FindAll(".nav-pill-label"),
      label => label.TextContent.Trim() is "Metrics" or "Diagnostics");
  }

  [Fact]
  public void Nav_StillHasTheSettingsPill()
  {
    var cut = RenderLayout();

    // Guards the absence assertions above: the nav rendered, and the pill Diagnostics lives under is there.
    Assert.Single(cut.FindAll("a[href='/system']"));
  }

  [Fact]
  public void SettingsPill_IsActive_OnTheDiagnosticsRoute()
  {
    var cut = RenderLayout(navigateTo: "/diagnostics");

    var settings = cut.Find("a[href='/system']");
    Assert.Contains("nav-active", settings.GetAttribute("class") ?? string.Empty);
  }

  /// <summary>
  /// UI-25: the triple-tap callback carries the tap point, and the layout hands the tray a placement
  /// under it. Toggling again closes; reopening elsewhere moves it.
  /// </summary>
  [Fact]
  public async Task DevTrayGesture_OpensTheTrayUnderTheTap_AndFollowsANewTap()
  {
    var cut = RenderLayout();

    await cut.InvokeAsync(() => cut.Instance.ToggleDevTray(1200, 30, 1920, 720));
    var tray = cut.Find(".dev-tray");
    Assert.Contains("is-open", tray.GetAttribute("class") ?? string.Empty);
    Assert.Contains("left:960px", tray.GetAttribute("style") ?? string.Empty);
    Assert.Contains("top:42px", tray.GetAttribute("style") ?? string.Empty);

    await cut.InvokeAsync(() => cut.Instance.ToggleDevTray(1200, 30, 1920, 720));
    Assert.DoesNotContain("is-open", cut.Find(".dev-tray").GetAttribute("class") ?? string.Empty);

    await cut.InvokeAsync(() => cut.Instance.ToggleDevTray(1100, 50, 1920, 720));
    tray = cut.Find(".dev-tray");
    Assert.Contains("is-open", tray.GetAttribute("class") ?? string.Empty);
    Assert.Contains("left:860px", tray.GetAttribute("style") ?? string.Empty);
    Assert.Contains("top:62px", tray.GetAttribute("style") ?? string.Empty);
  }

  private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
  {
    public StubOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
}
