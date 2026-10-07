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
/// Review H1: after a failed connect the Cast dropdown must not show the default as connected (its
/// row stays selectable, and there is no Stop Casting row that would clear the default), and the
/// next tap on the Cast pill retries the connect.
/// Re-review M-b: an OutputChanged broadcast received during a pick is honoured when the pick fails,
/// and OutputChanged keeps the dropdown's connected device in line with the active output.
/// Rendering set-up mirrors <see cref="MainLayoutNavTests"/>; the devices client is backed by a
/// <see cref="RoutedApiHandler"/> so every request the layout sends is recorded.
/// </summary>
public class MainLayoutCastPickTests : TestContext
{
  private const string ConnectPath = "/api/devices/cast/connect";
  private const string DefaultCastPath = "/api/devices/cast/default";
  private const string OutputPath = "/api/devices/output";
  private const string CachedCastPath = "/api/devices/cast/cached";
  private const string CastPill = "button[aria-label='Cast device picker']";
  private const string CastRow = ".cast-device-row";

  private static readonly CastDeviceDto SavedDefault =
    new("https://192.168.0.25/", "Test speaker", "192.168.0.25", 8009, "Nest Audio");

  private IRenderedComponent<Radio.Web.Components.Layout.MainLayout> RenderLayout(HttpMessageHandler devices)
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

  // AUD-97 (owner, 2026-10-02): after a restart with Cast restored, the panel showed the Soundbar.
  // /output/default answers the hardware default sink whatever is playing; the list's IsActive flag
  // is the truth, so page load must read that.
  [Fact]
  public void PageLoad_WithCastActive_ShowsCast_NotTheDefaultSink()
  {
    var soundbar = new AudioDeviceDto { Id = "out:Built-in", Name = "Soundbar", Type = "Output", IsDefault = true };
    var cast = new AudioDeviceDto { Id = "google-cast", Name = "Cast", Type = "Output", IsActive = true };
    var devices = new RoutedApiHandler()
      .Get(OutputPath, new List<AudioDeviceDto> { soundbar, cast })
      .Get("/api/devices/output/default", soundbar)
      .Get(DefaultCastPath, SavedDefault);

    var cut = RenderLayout(devices);

    cut.WaitForAssertion(() =>
      Assert.Equal("google-cast", cut.FindComponent<OutputPickerDropdown>().Instance.CurrentOutputId),
      TimeSpan.FromSeconds(30));
    Assert.Equal(SavedDefault, cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);
  }

  [Fact]
  public void PageLoad_WithNoOutputActive_FallsBackToTheDefaultLookup()
  {
    var soundbar = new AudioDeviceDto { Id = "out:Built-in", Name = "Soundbar", Type = "Output", IsDefault = true };
    var cast = new AudioDeviceDto { Id = "google-cast", Name = "Cast", Type = "Output" };
    var devices = new RoutedApiHandler()
      .Get(OutputPath, new List<AudioDeviceDto> { soundbar, cast })
      .Get("/api/devices/output/default", soundbar)
      .Get(DefaultCastPath, SavedDefault);

    var cut = RenderLayout(devices);

    cut.WaitForAssertion(() =>
      Assert.Equal("out:Built-in", cut.FindComponent<OutputPickerDropdown>().Instance.CurrentOutputId),
      TimeSpan.FromSeconds(30));
    Assert.Null(cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);
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
    // The dropdown opens for a manual pick — and, since the connect failed, says nothing is
    // connected: no connected banner, and no Stop Casting row (which would clear the default).
    var dropdown = cut.FindComponent<CastDeviceDropdown>();
    Assert.True(dropdown.Instance.IsOpen);
    Assert.Null(dropdown.Instance.ConnectedDevice);
    Assert.Empty(dropdown.FindAll(".cast-stop-row"));
    Assert.DoesNotContain("cast_connected", dropdown.Markup, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CastPick_ConnectFails_TheDefaultsRowInTheDropdownIsSelectable()
  {
    var devices = DevicesWithSavedDefault(HttpStatusCode.InternalServerError)
      .Get(CachedCastPath, new List<CastDeviceDto> { SavedDefault });
    var cut = RenderLayout(devices);

    await TapCastAsync(cut);

    // Rendezvous on the observation: the row renders once the dropdown has loaded its cache.
    var row = cut.WaitForElement(CastRow, TimeSpan.FromSeconds(30));
    Assert.Contains(SavedDefault.Name, row.TextContent, StringComparison.Ordinal);
    Assert.DoesNotContain("pointer-events: none", row.GetAttribute("style") ?? "", StringComparison.Ordinal);

    await row.ClickAsync(new MouseEventArgs());

    // The row was not short-circuited as "already connected": it sent its own connect.
    var requests = devices.Requests;
    Assert.Equal(2, requests.Count(r => r.Method == HttpMethod.Post && r.Path == ConnectPath));
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Delete && r.Path == DefaultCastPath);
  }

  [Fact]
  public async Task CastPick_ConnectFails_ASecondTapRetriesTheOneTapConnect()
  {
    var devices = DevicesWithSavedDefault(HttpStatusCode.Conflict);
    var cut = RenderLayout(devices);

    await TapCastAsync(cut);
    await TapCastAsync(cut);

    var requests = devices.Requests;
    Assert.Equal(2, requests.Count(r => r.Method == HttpMethod.Post && r.Path == ConnectPath));
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Delete && r.Path == DefaultCastPath);
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Post && r.Path == OutputPath);
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
    var dropdown = cut.FindComponent<CastDeviceDropdown>();
    Assert.False(dropdown.Instance.IsOpen);
    Assert.Equal(SavedDefault, dropdown.Instance.ConnectedDevice);
  }

  [Fact]
  public async Task CastPick_ConnectSucceeds_ASecondTapOpensTheDropdownShowingTheDeviceConnected()
  {
    var devices = DevicesWithSavedDefault(HttpStatusCode.OK);
    var cut = RenderLayout(devices);

    await TapCastAsync(cut);
    await TapCastAsync(cut);

    // Cast is active, so the second tap toggles the dropdown rather than reconnecting.
    Assert.Single(devices.Requests, r => r.Method == HttpMethod.Post && r.Path == ConnectPath);
    var dropdown = cut.FindComponent<CastDeviceDropdown>();
    Assert.True(dropdown.Instance.IsOpen);
    Assert.Equal(SavedDefault, dropdown.Instance.ConnectedDevice);
    Assert.Single(dropdown.FindAll(".cast-stop-row"));
  }

  [Fact]
  public async Task CastPick_ServerReportsCastDuringThePick_ThenTheConnectFails_TheSelectionStaysOnCast()
  {
    // Re-review M-b: the API ended on Cast (e.g. it completed after this request gave up) and
    // broadcast OutputChanged("google-cast") while the pick was in flight — when the layout's
    // selection was already an optimistic "google-cast". The failed connect must not then put the
    // selection back to the local output the server is no longer on.
    AudioStateHubService? hub = null;
    var routes = DevicesWithSavedDefault(HttpStatusCode.InternalServerError);
    var devices = new BeforeConnectHandler(routes, () => FireOutputChangedAsync(hub!, "google-cast"));
    var cut = RenderLayout(devices);
    hub = WaitForOutputChangedSubscriber(cut);

    await TapCastAsync(cut);

    Assert.Equal(1, devices.ConnectsSeen);
    Assert.Equal("google-cast", cut.FindComponent<OutputPickerDropdown>().Instance.CurrentOutputId);
    // On Cast, the dropdown shows the device the API restores Cast to as connected.
    Assert.Equal(SavedDefault, cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);
    Assert.DoesNotContain(routes.Requests, r => r.Method == HttpMethod.Delete && r.Path == DefaultCastPath);
  }

  [Fact]
  public async Task OutputChanged_ToCast_TheDropdownShowsTheSavedDefaultAsConnected()
  {
    var devices = DevicesWithSavedDefault(HttpStatusCode.OK);
    var cut = RenderLayout(devices);
    // The subscription is made after the outputs and the saved default have loaded, so waiting for
    // it also means the default is known.
    var hub = WaitForOutputChangedSubscriber(cut);
    Assert.Null(cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);

    await FireOutputChangedAsync(hub, "google-cast");

    Assert.Equal("google-cast", cut.FindComponent<OutputPickerDropdown>().Instance.CurrentOutputId);
    Assert.Equal(SavedDefault, cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);
  }

  [Fact]
  public async Task OutputChanged_FromCastToLocal_ClearsTheConnectedDevice()
  {
    var devices = DevicesWithSavedDefault(HttpStatusCode.OK);
    var cut = RenderLayout(devices);
    var hub = WaitForOutputChangedSubscriber(cut);
    await TapCastAsync(cut);
    Assert.Equal(SavedDefault, cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);

    await FireOutputChangedAsync(hub, "local-1");

    Assert.Equal("local-1", cut.FindComponent<OutputPickerDropdown>().Instance.CurrentOutputId);
    Assert.Null(cut.FindComponent<CastDeviceDropdown>().Instance.ConnectedDevice);
  }

  /// <summary>
  /// The layout subscribes to OutputChanged only after its hub start, which can complete after
  /// the first render; rendezvous on the subscription rather than on time.
  /// </summary>
  private AudioStateHubService WaitForOutputChangedSubscriber(
    IRenderedComponent<Radio.Web.Components.Layout.MainLayout> cut)
  {
    var hub = Services.GetRequiredService<AudioStateHubService>();
    cut.WaitForAssertion(
      () => Assert.NotEmpty(HubEventFire.InvocationListOf<Func<string?, Task>>(hub, nameof(AudioStateHubService.OutputChanged))),
      TimeSpan.FromSeconds(30));
    return hub;
  }

  private static Task FireOutputChangedAsync(AudioStateHubService hub, string outputId) =>
    HubEventFire.FireAsync<string?>(hub, nameof(AudioStateHubService.OutputChanged), outputId);

  /// <summary>
  /// Runs a callback when the connect request arrives, before it is answered — the window in which
  /// a server broadcast can land while a pick is in flight. The callback runs on the thread pool
  /// so the layout's click handler has yielded the renderer and the broadcast's InvokeAsync can run.
  /// </summary>
  private sealed class BeforeConnectHandler : DelegatingHandler
  {
    private readonly Func<Task> _beforeConnect;
    private int _connectsSeen;

    public BeforeConnectHandler(HttpMessageHandler inner, Func<Task> beforeConnect) : base(inner) =>
      _beforeConnect = beforeConnect;

    public int ConnectsSeen => Volatile.Read(ref _connectsSeen);

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == ConnectPath)
      {
        Interlocked.Increment(ref _connectsSeen);
        await Task.Run(_beforeConnect, cancellationToken);
      }
      return await base.SendAsync(request, cancellationToken);
    }
  }

  private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
  {
    public StubOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
}
