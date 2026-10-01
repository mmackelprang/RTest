using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Components.Layout;

/// <summary>
/// AUD-85, at the layout: tapping the Cast pill with a saved default Cast device sends only the
/// connect — no <c>POST /api/devices/output</c>, which starts the API's own competing auto-connect —
/// and a failed connect leaves the saved default alone (no <c>DELETE /api/devices/cast/default</c>).
/// Rendering set-up mirrors <see cref="MainLayoutNavTests"/>; the devices client is backed by a
/// <see cref="RoutedApiHandler"/> so every request the layout sends is recorded.
/// </summary>
public class MainLayoutCastPickTests : TestContext
{
  private const string ConnectPath = "/api/devices/cast/connect";
  private const string DefaultCastPath = "/api/devices/cast/default";
  private const string OutputPath = "/api/devices/output";
  private const string CastPill = "button[aria-label='Cast device picker']";

  private static readonly CastDeviceDto SavedDefault =
    new("https://192.168.0.25/", "Test speaker", "192.168.0.25", 8009, "Nest Audio");

  private IRenderedComponent<Radio.Web.Components.Layout.MainLayout> RenderLayout(RoutedApiHandler devices)
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
    Services.AddSingleton(new DevicesApiService(
      new HttpClient(devices) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<DevicesApiService>.Instance));
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

    return RenderComponent<Radio.Web.Components.Layout.MainLayout>();
  }

  private static RoutedApiHandler DevicesWithSavedDefault(HttpStatusCode connectStatus)
  {
    var local = new AudioDeviceDto { Id = "local-1", Name = "Speakers", Type = "Output", IsDefault = true, IsActive = true };
    var cast = new AudioDeviceDto { Id = "google-cast", Name = "Google Cast", Type = "Cast" };

    return new RoutedApiHandler()
      .Get(OutputPath, new List<AudioDeviceDto> { local, cast })
      .Get("/api/devices/output/default", local)
      .Get(DefaultCastPath, SavedDefault)
      .Post(ConnectPath, connectStatus);
  }

  private static async Task TapCastAsync(IRenderedComponent<Radio.Web.Components.Layout.MainLayout> cut)
  {
    // Rendezvous on the observation: the pill renders only once the output list has loaded.
    cut.WaitForElement(CastPill, TimeSpan.FromSeconds(30));
    // ClickAsync completes when the layout's async click handler completes.
    await cut.Find(CastPill).ClickAsync(new MouseEventArgs());
  }

  [Theory]
  [InlineData(HttpStatusCode.InternalServerError)]
  [InlineData(HttpStatusCode.Conflict)]
  public async Task CastPick_WithSavedDefault_ConnectFails_KeepsDefault_AndOpensDropdown(HttpStatusCode status)
  {
    var devices = DevicesWithSavedDefault(status);
    var cut = RenderLayout(devices);

    await TapCastAsync(cut);

    var requests = devices.Requests;
    Assert.Single(requests, r => r.Method == HttpMethod.Post && r.Path == ConnectPath);
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Delete && r.Path == DefaultCastPath);
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Post && r.Path == OutputPath);
    // The dropdown opens for a manual pick, and the layout still holds the saved default.
    var dropdown = cut.FindComponent<CastDeviceDropdown>().Instance;
    Assert.True(dropdown.IsOpen);
    Assert.Equal(SavedDefault, dropdown.ConnectedDevice);
  }

  [Fact]
  public async Task CastPick_WithSavedDefault_ConnectSucceeds_SendsOnlyTheConnect()
  {
    var devices = DevicesWithSavedDefault(HttpStatusCode.OK);
    var cut = RenderLayout(devices);

    await TapCastAsync(cut);

    var requests = devices.Requests;
    Assert.Single(requests, r => r.Method == HttpMethod.Post && r.Path == ConnectPath);
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Post && r.Path == OutputPath);
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Delete && r.Path == DefaultCastPath);
    Assert.False(cut.FindComponent<CastDeviceDropdown>().Instance.IsOpen);
  }

  private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
  {
    public StubOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
}
