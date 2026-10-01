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
/// UI-30: the visualizer's disconnected state. The owner ruled that a disconnected visualizer should say so
/// in the visualization area — "not a small dot" — so the header's 8 px connection dot is gone, and while the
/// visualization hub is not connected the hub-fed modes show an icon and a label over the canvas.
/// </summary>
/// <remarks>
/// <para>
/// The hub never connects in a test (<see cref="OfflineHubTransport"/>), so the connected side is reached
/// through <c>AudioVisualizationHubService.IsConnectedOverride</c>, and a change is announced by firing the
/// service's real <c>ConnectionStateChanged</c> event. The panel handles that event off the hub's thread and
/// counts what it has finished; tests rendezvous on <c>CompletedConnectionUpdates</c>, not on time.
/// </para>
/// <para>
/// <c>visualizer.init</c> is answered <c>true</c> so the canvas initialises; until it does, the skeleton
/// shows and neither overlay can. BAND's side (it shows no disconnected state) is in
/// <see cref="VisualizerPanelBandTests"/>, where the radio is active.
/// </para>
/// </remarks>
public class VisualizerPanelConnectionTests : TestContext
{
  private readonly BunitJSModuleInterop _module;
  private readonly AudioVisualizationHubService _hub;

  public VisualizerPanelConnectionTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    _module = JSInterop.SetupModule("./js/visualizer.js");
    _module.Mode = JSRuntimeMode.Loose;
    _module.Setup<bool>("visualizer.init", _ => true).SetResult(true);

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build();
    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddHttpClient<RadioApiService>();
    Services.AddHttpClient<SourcesApiService>();
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));

    _hub = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance, configuration, transport: new OfflineHubTransport());
    Services.AddSingleton(_hub);
    Services.AddSingleton<VisualizerTelemetryService>();
  }

  private IRenderedComponent<VisualizerPanel> RenderInitialised()
  {
    var cut = RenderComponent<VisualizerPanel>();
    cut.WaitForAssertion(() => cut.FindAll(".rz-skeleton, .skeleton").Should().BeEmpty(), TimeSpan.FromSeconds(5));
    cut.WaitForAssertion(
      () => (cut.FindAll(".visualizer-disconnected").Count + cut.FindAll(".visualizer-waiting").Count).Should().Be(1),
      TimeSpan.FromSeconds(5));
    return cut;
  }

  private async Task SetConnectedAsync(IRenderedComponent<VisualizerPanel> cut, bool connected)
  {
    int handled = cut.Instance.CompletedConnectionUpdates;
    _hub.IsConnectedOverride = connected;
    await cut.InvokeAsync(() => HubEventFire.FireAsync(_hub, nameof(AudioVisualizationHubService.ConnectionStateChanged)));
    cut.WaitForAssertion(() => cut.Instance.CompletedConnectionUpdates.Should().BeGreaterThan(handled),
      TimeSpan.FromSeconds(5));
  }

  [Fact]
  public void Disconnected_ShowsTheDisconnectedState_InTheCanvasArea()
  {
    _hub.IsConnected.Should().BeFalse("the hub is offline in this rig");
    var cut = RenderInitialised();

    var state = cut.Find(".visualizer-canvas-wrap .visualizer-disconnected");
    state.GetAttribute("role").Should().Be("status");
    state.GetAttribute("aria-live").Should().Be("polite");
    state.TextContent.Should().Contain("Visualizer disconnected").And.Contain("Reconnecting");
    state.QuerySelector(".visualizer-disconnected-icon").Should().NotBeNull();
  }

  [Fact]
  public void Disconnected_TakesPrecedenceOverWaitingForAudio()
  {
    // No data has arrived either, so without the precedence "Waiting for audio data" would show — and would
    // wait for something that cannot come while the hub is down.
    var cut = RenderInitialised();

    cut.FindAll(".visualizer-disconnected").Should().HaveCount(1);
    cut.FindAll(".visualizer-waiting").Should().BeEmpty();
  }

  [Fact]
  public void Connected_ShowsNoDisconnectedState()
  {
    _hub.IsConnectedOverride = true;
    var cut = RenderInitialised();

    cut.FindAll(".visualizer-disconnected").Should().BeEmpty();
    cut.FindAll(".visualizer-waiting").Should().HaveCount(1, "connected, but no audio data has arrived");
  }

  [Fact]
  public void NoConnectionDot_InEitherState()
  {
    var cut = RenderInitialised();
    cut.FindAll(".visualizer-conn-dot").Should().BeEmpty();
    cut.Markup.Should().NotContain("conn-dot");

    _hub.IsConnectedOverride = true;
    var connected = RenderInitialised();
    connected.FindAll(".visualizer-conn-dot").Should().BeEmpty();
  }

  [Fact]
  public async Task Reconnect_RemovesTheState_AndADrop_BringsItBack_AndClearsTheCanvas()
  {
    _hub.IsConnectedOverride = true;
    var cut = RenderInitialised();
    cut.FindAll(".visualizer-disconnected").Should().BeEmpty();
    int clearsBefore = _module.Invocations["visualizer.clear"].Count;

    await SetConnectedAsync(cut, connected: false);

    cut.WaitForAssertion(() => cut.FindAll(".visualizer-disconnected").Should().HaveCount(1));
    _module.Invocations["visualizer.clear"].Count.Should().BeGreaterThan(clearsBefore,
      "the frame drawn before the drop would otherwise sit under the state looking live");

    await SetConnectedAsync(cut, connected: true);

    cut.WaitForAssertion(() => cut.FindAll(".visualizer-disconnected").Should().BeEmpty());
  }

  [Fact]
  public async Task ConnectionStateChanged_WithNoChange_DoesNotClearTheCanvas()
  {
    // Reconnecting fires while already disconnected; only a connected → disconnected change clears.
    var cut = RenderInitialised();
    int clearsBefore = _module.Invocations["visualizer.clear"].Count;

    await SetConnectedAsync(cut, connected: false);

    _module.Invocations["visualizer.clear"].Count.Should().Be(clearsBefore);
    cut.FindAll(".visualizer-disconnected").Should().HaveCount(1);
  }

  [Fact]
  public async Task Dispose_UnsubscribesFromConnectionStateChanged()
  {
    var cut = RenderInitialised();
    DisposeComponents();

    // A raise after disposal reaches no handler of the panel's; the field-like event is empty again.
    var field = typeof(AudioVisualizationHubService).GetField(
      nameof(AudioVisualizationHubService.ConnectionStateChanged),
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    field.Should().NotBeNull("the reflection must see the event's backing field, or the next line proves nothing");
    field!.GetValue(_hub).Should().BeNull();
    await Task.CompletedTask;
  }
}
