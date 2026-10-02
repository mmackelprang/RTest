using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// UI-36: the live visualizer stream after a wake. The owner, 2026-10-02: <i>"visualizaitons sometimes
/// (but not always) "pause" sometimes after a deep sleep for ~15 seconds."</i>
/// </summary>
/// <remarks>
/// <para>
/// Three behaviours, each a guard rather than a proven cure — see the row's dossier for what was and was
/// not measured on the box:
/// </para>
/// <list type="number">
///   <item>The hub's handler never waits on the browser: a frame is handed to a latest-wins draw loop, so a
///   slow browser cannot hold the shared hub connection and comes back to the newest frame, not a queue.</item>
///   <item>The live frame handlers are attached before the panel's start-up HTTP reads, so a slow API at
///   wake cannot leave the canvas on its one start-up frame.</item>
///   <item>A connected, hub-fed panel that receives no frame for two ticks re-asserts its subscription,
///   with backoff.</item>
/// </list>
/// <para>
/// The hub never connects here (<see cref="OfflineHubTransport"/>); the connected side is reached through
/// <c>IsConnectedOverride</c>, frames are fired through <see cref="HubEventFire"/>, and the stall check is
/// driven by a <see cref="FakeTimeProvider"/>. Tests rendezvous on counts, never on elapsed time.
/// </para>
/// </remarks>
public class VisualizerPanelStreamTests : TestContext
{
  private readonly BunitJSModuleInterop _module;
  private readonly AudioVisualizationHubService _hub;
  private readonly FakeTimeProvider _clock = new();
  private readonly TaskCompletionSource _prefsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

  public VisualizerPanelStreamTests()
  {
    _prefsHandler = new GatedHandler(_prefsGate.Task);
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
    Services.AddHttpClient<RadioApiService>();
    Services.AddHttpClient<SourcesApiService>();
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));

    _hub = new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance, configuration, transport: new OfflineHubTransport());
    _hub.IsConnectedOverride = true;
    Services.AddSingleton(_hub);
    Services.AddSingleton<VisualizerTelemetryService>();
  }

  /// <summary>The preferences read fails at once, as with no API: start-up completes normally.</summary>
  private void WithPreferencesReadFailingAtOnce() => Services.AddHttpClient<ConfigurationApiService>();

  /// <summary>
  /// The preferences read does not answer until <see cref="_prefsGate"/> is completed: radio-api busy
  /// at a wake, resuming the source and reloading its output settings.
  /// </summary>
  /// <remarks>
  /// ⚠ The base address is load-bearing. Without one, the client's relative GET throws before it reaches
  /// any handler, the service swallows that and answers at once, and the "held" read is not held — which
  /// is how this test's first draft passed against the very ordering it exists to catch (caught by its
  /// mutation check).
  /// </remarks>
  private void WithPreferencesReadHeld() =>
    Services.AddHttpClient<ConfigurationApiService>(c => c.BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl))
      .ConfigurePrimaryHttpMessageHandler(() => _prefsHandler);

  private readonly GatedHandler _prefsHandler;

  private sealed class GatedHandler(Task gate) : HttpMessageHandler
  {
    private int _requests;

    /// <summary>Requests that have reached this handler and are waiting on the gate (or did).</summary>
    public int Requests => Volatile.Read(ref _requests);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _requests);
      await gate.WaitAsync(cancellationToken);
      throw new HttpRequestException("released by the test");
    }
  }

  private IRenderedComponent<VisualizerPanel> RenderInitialised()
  {
    var cut = RenderComponent<VisualizerPanel>(p => p.Add(x => x.Clock, _clock));
    cut.WaitForAssertion(() => cut.FindAll(".rz-skeleton, .skeleton").Should().BeEmpty(), TimeSpan.FromSeconds(5));
    return cut;
  }

  private static SpectrumDataDto Frame(float level) =>
    new() { Magnitudes = new[] { level }, Frequencies = new[] { 100f } };

  private Task FireFrameAsync(IRenderedComponent<VisualizerPanel> cut, float level) =>
    cut.InvokeAsync(() => HubEventFire.FireAsync(_hub, nameof(AudioVisualizationHubService.OnSpectrumData), Frame(level)));

  private IReadOnlyList<JSRuntimeInvocation> Draws => _module.Invocations["visualizer.drawSpectrum"];

  /// <summary>
  /// One tick. <see cref="FakeTimeProvider.Advance"/> runs the timer callback inline, so the count of
  /// recoveries STARTED is exact the moment it returns; the wait then lets every started recovery finish,
  /// so none is still in flight when the next tick or assertion runs.
  /// </summary>
  private async Task TickAsync(IRenderedComponent<VisualizerPanel> cut, int expectedRecoveries)
  {
    _clock.Advance(VisualizerPanel.TelemetryInterval);
    cut.Instance.StreamStallRecoveriesStarted.Should().Be(expectedRecoveries);
    cut.WaitForAssertion(() => cut.Instance.StreamStallRecoveries.Should().Be(expectedRecoveries), TimeSpan.FromSeconds(5));
    await Task.CompletedTask;
  }

  // --- 1. The hub never waits on the browser, and the newest frame wins --------------------------

  [Fact]
  public async Task WhileTheBrowserIsSlowToAcceptAFrame_TheHubIsNotHeld_AndTheNewestFrameIsDrawnNext()
  {
    WithPreferencesReadFailingAtOnce();
    var draw = _module.SetupVoid("visualizer.drawSpectrum", _ => true); // pending until released
    var cut = RenderInitialised();

    // Every fire is bounded only so a regression fails rather than hangs: with the handler awaiting the
    // draw, a fire cannot complete until the test releases the draw below, however long it waits. (The
    // first draft awaited this first fire directly, and its mutation check hung instead of failing.)
    foreach (float level in new[] { 0.1f, 0.2f, 0.3f, 0.4f })
    {
      Task fire = FireFrameAsync(cut, level);
      (await Task.WhenAny(fire, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(fire,
        "the hub's handler must not wait on a browser round trip");
      cut.WaitForAssertion(() => Draws.Should().HaveCount(1), TimeSpan.FromSeconds(5));
    }

    Draws.Should().HaveCount(1, "one draw in flight per panel");

    draw.SetVoidResult();
    cut.WaitForAssertion(() => Draws.Should().HaveCount(2), TimeSpan.FromSeconds(5));
    await cut.Instance.DrawIdle;

    Draws.Should().HaveCount(2, "the frames that arrived meanwhile replaced each other");
    ((float[])Draws[1].Arguments[1]!)[0].Should().Be(0.4f, "the newest frame is the one drawn");
  }

  // --- 2. Live frames reach the canvas before the start-up reads finish -------------------------

  [Fact]
  public async Task ALiveFrame_IsDrawn_WhileThePreferencesReadIsStillInFlight()
  {
    WithPreferencesReadHeld();
    var cut = RenderInitialised();
    cut.WaitForAssertion(() => _prefsHandler.Requests.Should().Be(1), TimeSpan.FromSeconds(5));

    await FireFrameAsync(cut, 0.5f);

    cut.WaitForAssertion(() => Draws.Should().HaveCount(1), TimeSpan.FromSeconds(5));
    _prefsGate.Task.IsCompleted.Should().BeFalse("the frame was drawn while the read was still held");
    _prefsGate.SetResult();
  }

  // --- 3. A stalled stream re-asserts its subscription --------------------------------------------

  [Fact]
  public async Task NoFrameForTwoTicks_WhileConnected_ReSubscribes_ThenBacksOff()
  {
    WithPreferencesReadFailingAtOnce();
    var cut = RenderInitialised();

    await TickAsync(cut, expectedRecoveries: 0);
    await TickAsync(cut, expectedRecoveries: 1); // the second silent tick

    await TickAsync(cut, expectedRecoveries: 1);
    await TickAsync(cut, expectedRecoveries: 1);
    await TickAsync(cut, expectedRecoveries: 2); // backoff: the fifth silent tick
  }

  [Fact]
  public async Task FramesArrivingEveryTick_NeverCountAsAStall()
  {
    WithPreferencesReadFailingAtOnce();
    var cut = RenderInitialised();

    for (int i = 0; i < 5; i++)
    {
      await FireFrameAsync(cut, 0.5f);
      await TickAsync(cut, expectedRecoveries: 0);
    }
  }

  [Fact]
  public async Task WhileDisconnected_NothingReSubscribes()
  {
    // UI-30 owns the disconnected state, and the hub's own reconnect replays subscriptions.
    WithPreferencesReadFailingAtOnce();
    _hub.IsConnectedOverride = false;
    var cut = RenderInitialised();

    for (int i = 0; i < 4; i++)
    {
      await TickAsync(cut, expectedRecoveries: 0);
    }
  }

  [Fact]
  public async Task AFrameAfterAStall_ResetsTheCount()
  {
    WithPreferencesReadFailingAtOnce();
    var cut = RenderInitialised();

    await TickAsync(cut, expectedRecoveries: 0);
    await TickAsync(cut, expectedRecoveries: 1);

    await FireFrameAsync(cut, 0.5f);
    await TickAsync(cut, expectedRecoveries: 1); // recovered: this tick had a frame

    await TickAsync(cut, expectedRecoveries: 1); // silent again: one tick is not a stall
    await TickAsync(cut, expectedRecoveries: 2); // two are, and the threshold applies afresh
  }
}
