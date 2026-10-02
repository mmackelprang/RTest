using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
/// UI-35, at the layout: the tapped source pill lights as soon as the switch succeeds, without waiting
/// for the SourceChanged subscribers the layout then notifies. It used to wait for all of them — the
/// centre panel's 1.4–4.7 s queue read among them. Set-up mirrors <see cref="MainLayoutCastPickTests"/>.
/// </summary>
/// <remarks>
/// ⚠ No wall clock is raced: a SourceChanged subscriber is HELD on a gate the test opens only after the
/// assertion, so "before the subscribers finish" is a fact, not a timing.
/// </remarks>
public class MainLayoutSourceSwitchTests : TestContext
{
  private const string VinylPill = "button[aria-label='Switch to Vinyl (Phono)']";

  private IRenderedComponent<Radio.Web.Components.Layout.MainLayout> RenderLayout(
    RoutedApiHandler sources, AudioStateHubService hub)
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
    Services.AddSingleton(new SourcesApiService(
      new HttpClient(sources) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(new DevicesApiService(Api(), NullLogger<DevicesApiService>.Instance));
    Services.AddSingleton(new AudioApiService(Api(), NullLogger<AudioApiService>.Instance));
    Services.AddSingleton(new QueueApiService(Api(), NullLogger<QueueApiService>.Instance));
    Services.AddSingleton(new IntegrationsApiService(Api(), NullLogger<IntegrationsApiService>.Instance));
    Services.AddSingleton(new EventPlaybackApiService(Api(), NullLogger<EventPlaybackApiService>.Instance));
    Services.AddSingleton(hub);
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

  private static AudioSourceDto Primary(string type, string name) => new()
  {
    Id = type.ToLowerInvariant(),
    Name = name,
    Type = type,
    Category = "Primary",
    State = "Active",
  };

  /// <summary>Radio active; the switch POST makes Vinyl the primary source, as the API would.</summary>
  private static RoutedApiHandler RadioAndVinyl()
  {
    var api = new RoutedApiHandler()
      .Get("/api/sources", new AvailableSourcesDto
      {
        PrimarySources = ["Radio", "Vinyl"],
        ActiveSourceType = "Radio",
      })
      .Get("/api/sources/primary", Primary("Radio", "FM/AM Radio"))
      .Post("/api/sources");
    api.OnRequest(HttpMethod.Post, "/api/sources",
      () => api.Get("/api/sources/primary", Primary("Vinyl", "Vinyl (Phono)")));
    return api;
  }

  private static bool IsActive(AngleSharp.Dom.IElement pill) =>
    (pill.GetAttribute("class") ?? string.Empty).Contains("is-active", StringComparison.Ordinal);

  [Fact]
  public async Task TheTappedPill_LightsBeforeTheSourceChangedSubscribersFinish()
  {
    var hub = new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, new ConfigurationBuilder().Build(),
      transport: new OfflineHubTransport());
    var subscriberGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var subscriberEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    hub.SourceChanged += async () =>
    {
      subscriberEntered.TrySetResult();
      await subscriberGate.Task;
    };

    var sources = RadioAndVinyl();
    var cut = RenderLayout(sources, hub);
    var vinyl = cut.WaitForElement(VinylPill, TimeSpan.FromSeconds(30));
    Assert.False(IsActive(vinyl));

    // Stop the layout's 1 s clock: each tick re-renders the whole layout, and would light the pill on its
    // own — measured: with the fix removed, this test passed on a tick.
    var clock = (System.Timers.Timer?)typeof(Radio.Web.Components.Layout.MainLayout)
      .GetField("_timer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
      .GetValue(cut.Instance);
    Assert.NotNull(clock);
    clock!.Stop();

    // Hold the switch POST so it completes asynchronously, as it does against the real API. Without the
    // hold the stub answers synchronously, the handler reaches the held subscriber before its first yield,
    // and the event dispatch's own post-yield render lights the pill whether or not the layout asks for it.
    TaskCompletionSource post = sources.Hold(HttpMethod.Post, "/api/sources");
    Task tap = cut.Find(VinylPill).ClickAsync(new MouseEventArgs());
    cut.WaitForAssertion(
      () => Assert.Contains(sources.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/sources"),
      TimeSpan.FromSeconds(10));
    Assert.False(IsActive(cut.Find(VinylPill)), "not lit before the switch has succeeded");
    post.SetResult();
    await subscriberEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));

    // The subscriber is still held: the pill must already be lit. Asserted at once, not with
    // WaitForAssertion — any later render would light it, which is exactly what this must not credit.
    Assert.True(IsActive(cut.Find(VinylPill)));
    Assert.False(tap.IsCompleted, "the layout's handler is still awaiting the held subscriber");

    subscriberGate.SetResult();
    await tap;
    Assert.True(IsActive(cut.Find(VinylPill)));
    Assert.Contains(sources.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/sources");
  }

  private sealed class StubOptionsMonitor<T> : IOptionsMonitor<T>
  {
    public StubOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
  }
}
