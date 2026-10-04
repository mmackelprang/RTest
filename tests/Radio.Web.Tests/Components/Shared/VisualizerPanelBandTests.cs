using System.Net;
using System.Text.Json;
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
/// The visualizer's band map view (AUD-76 PR 2; the RADIO tab since UI-31, BAND before): the empty, aged
/// and sweeping states, the Discover button (Scan before UI-31), the strip under the map, the refresh
/// cadence and tap-to-tune.
///
/// <para>
/// Every API call goes to a <see cref="RoutedApiHandler"/>, so the "server" is a table the test
/// controls. The saved preference is BAND and the radio is the active source, so the panel opens on
/// BAND (UI-29: BAND is shown only while the radio is active). The refresh timer is armed
/// against a <see cref="FakeTimeProvider"/>, and refreshes are counted through
/// <c>CompletedBandRefreshes</c> rather than timed (CLAUDE.md § Test Timing).
/// </para>
/// </summary>
public class VisualizerPanelBandTests : TestContext
{
  private readonly FakeTimeProvider _clock = new();
  private readonly RoutedApiHandler _api = new();
  private readonly BunitJSModuleInterop _module;

  public VisualizerPanelBandTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    BunitJSModuleInterop module = JSInterop.SetupModule("./js/visualizer.js");
    _module = module;
    module.Mode = JSRuntimeMode.Loose;
    module.Setup<bool>("visualizer.init", _ => true).SetResult(true);

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl } })
      .Build();
    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());
    Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    Services.AddRadzenComponents();

    Services.AddSingleton(_ => new ConfigurationApiService(Client(), NullLogger<ConfigurationApiService>.Instance));
    Services.AddSingleton(_ => new RadioApiService(Client(), NullLogger<RadioApiService>.Instance));
    Services.AddSingleton(_ => new SourcesApiService(Client(), NullLogger<SourcesApiService>.Instance));
    Services.AddSingleton(sp => new AudioVisualizationHubService(
      NullLogger<AudioVisualizationHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton(sp => new AudioStateHubService(
      NullLogger<AudioStateHubService>.Instance, sp.GetRequiredService<IConfiguration>(),
      transport: new OfflineHubTransport()));
    Services.AddSingleton<VisualizerTelemetryService>();

    // Defaults: BAND is the saved mode, the radio is the active source (UI-29: otherwise BAND is not
    // shown at all), no presets, no map. Tests about the radio NOT being active reroute the state.
    GetJson("/api/configuration/ui.visualizer", new Dictionary<string, object> { ["defaultMode"] = "Band" });
    GetJson("/api/radio/state", FmState(101_100_000));
    GetJson("/api/radio/presets", Array.Empty<object>());
    GetJson("/api/radio/bandmap", EmptyMap());
    _api.Post("/api/sources");
    _api.Post("/api/radio/frequency");
    _api.Post("/api/radio/band");
  }

  private HttpClient Client() => new(_api, disposeHandler: false) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) };

  private void GetJson(string path, object body) =>
    _api.Route(HttpMethod.Get, path, HttpStatusCode.OK, JsonSerializer.Serialize(body, JsonSerializerOptions.Web));

  private static object Sweep(bool isSweeping = false, double? remaining = null, double progress = 0) =>
    new { isSweeping, trigger = isSweeping ? "request" : null, path = isSweeping ? "idle" : null, progress, estimatedSecondsRemaining = remaining };

  private static object EmptyMap(bool isSweeping = false, double? remaining = null) => new
  {
    band = "FM",
    scannedAtUtc = (DateTimeOffset?)null,
    ageSeconds = (double?)null,
    channels = Array.Empty<object>(),
    sweep = Sweep(isSweeping, remaining),
  };

  /// <summary>A full map at -60 dB with a strong station at 99.5 MHz.</summary>
  private static object MapWithStation(double ageSeconds, bool isSweeping = false, double? remaining = null)
  {
    var channels = new List<object>();
    for (long hz = FmBandMath.FirstChannelHz; hz <= FmBandMath.LastChannelHz; hz += FmBandMath.ChannelSpacingHz)
    {
      channels.Add(new { frequencyHz = hz, levelDbfs = hz == 99_500_000 ? -30f : -60f });
    }

    return new
    {
      band = "FM",
      scannedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds),
      ageSeconds,
      channels,
      sweep = Sweep(isSweeping, remaining),
    };
  }

  private static object FmState(double frequencyHz, string band = "FM") => new
  {
    frequency = frequencyHz,
    band,
    step = 200_000,
    signalStrength = 50,
    isScanning = false,
    scanDirection = (string?)null,
    scanStopThreshold = -36.0,
    gain = 0,
    autoGain = false,
    equalizer = "Normal",
    deviceVolume = 50,
  };

  private IRenderedComponent<VisualizerPanel> RenderBand()
  {
    var cut = RenderComponent<VisualizerPanel>(p => p.Add(x => x.Clock, _clock));
    cut.WaitForAssertion(() =>
    {
      cut.FindAll(".band-strip").Should().HaveCount(1, "the canvas initialised and BAND is the saved mode");
      cut.Instance.CompletedBandRefreshes.Should().BeGreaterThanOrEqualTo(1);
    }, TimeSpan.FromSeconds(5));
    return cut;
  }

  private void WaitForRefreshes(IRenderedComponent<VisualizerPanel> cut, int atLeast) =>
    cut.WaitForAssertion(() => cut.Instance.CompletedBandRefreshes.Should().BeGreaterThanOrEqualTo(atLeast),
      TimeSpan.FromSeconds(5));

  private int IndexOf(HttpMethod method, string path) =>
    _api.Requests.ToList().FindIndex(r => r.Method == method && r.Path == path);

  private void RadioNotActive() =>
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.BadRequest, "{\"error\":\"Radio is not the active source\"}");

  // ── states ───────────────────────────────────────────────────────────────

  [Fact]
  public void EmptyMap_ShowsNoStationsDiscoveredYet_AndNotTheAudioWaitingOverlay()
  {
    var cut = RenderBand();

    cut.Find(".band-empty-title").TextContent.Should().Be("No stations discovered yet");
    cut.FindAll(".visualizer-waiting").Should().BeEmpty("BAND has no audio stream to wait for");
    cut.FindAll(".band-status-age").Should().BeEmpty();
  }

  [Fact]
  public void StoredMap_ShowsItsAge()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 3 * 3600 + 120));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-age").TextContent.Should().Be("discovered 3 h ago"));
    cut.FindAll(".band-empty").Should().BeEmpty();
    cut.FindAll(".visualizer-waiting").Should().BeEmpty();
  }

  [Fact]
  public void Sweeping_ShowsTheCountdown_AndDisablesDiscover()
  {
    GetJson("/api/radio/bandmap", EmptyMap(isSweeping: true, remaining: 12));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-discovering").TextContent.Should().Be("Discovering… 12 s"));
    cut.Find(".band-discover-btn").HasAttribute("disabled").Should().BeTrue();
  }

  [Fact]
  public void BandAxis_ShowsMhz()
  {
    var cut = RenderBand();

    cut.FindAll(".band-axis-labels span").Select(s => s.TextContent)
      .Should().Equal("88", "92", "96", "100", "104", "108");
  }

  // ── Discover button (UI-31; was Scan) ──────────────────────────────────────────────────────────

  [Fact]
  public void DiscoverButton_PostsTheScan()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.Accepted, "{}");
    var cut = RenderBand();

    cut.Find(".band-discover-btn").Click();

    cut.WaitForAssertion(() => IndexOf(HttpMethod.Post, "/api/radio/bandmap/scan").Should().BeGreaterThanOrEqualTo(0));
  }

  [Fact]
  public void DiscoverButton_Unavailable_ShowsTheApiReason()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.ServiceUnavailable,
      "{\"error\":\"No SDR device is available\"}");
    var cut = RenderBand();

    cut.Find(".band-discover-btn").Click();

    cut.WaitForAssertion(() =>
    {
      var message = cut.Find(".band-status-message");
      message.TextContent.Should().Be("No SDR device is available");
      message.ClassList.Should().Contain("is-error");
    });
  }

  // ── refresh cadence ──────────────────────────────────────────────────────

  private int MapReads() => _api.Requests.Count(r => r.Method == HttpMethod.Get && r.Path == "/api/radio/bandmap");

  [Fact]
  public void Idle_RefreshesOnTheThirtySecondCadence()
  {
    var cut = RenderBand();
    int start = cut.Instance.CompletedBandRefreshes;
    int readsBefore = MapReads();

    // The negative check counts requests, which the routed handler records when a GET arrives — not
    // completed cycles, which finish after the round trip and so would hide an early fire for longer.
    // Its direction: it can MISS an early fire whose request had not reached the handler yet when the
    // count is read, but it cannot fail spuriously — nothing but a fired timer sends a map read here.
    _clock.Advance(VisualizerPanel.BandIdleRefresh - TimeSpan.FromSeconds(1));
    MapReads().Should().Be(readsBefore, "the idle cadence has not elapsed");

    _clock.Advance(TimeSpan.FromSeconds(1));
    WaitForRefreshes(cut, start + 1);
    MapReads().Should().Be(readsBefore + 1);
  }

  [Fact]
  public void MapReadFails_WhileSweeping_DropsToTheIdleCadence()
  {
    GetJson("/api/radio/bandmap", EmptyMap(isSweeping: true, remaining: 12));
    var cut = RenderBand();
    int start = cut.Instance.CompletedBandRefreshes;

    // The API goes away mid-sweep. The kept map still says "sweeping"; the cadence must not.
    _api.Route(HttpMethod.Get, "/api/radio/bandmap", HttpStatusCode.ServiceUnavailable, null);
    _clock.Advance(VisualizerPanel.BandSweepingRefresh);
    WaitForRefreshes(cut, start + 1);
    int readsAfterFailure = MapReads();

    _clock.Advance(VisualizerPanel.BandIdleRefresh - TimeSpan.FromSeconds(1));
    MapReads().Should().Be(readsAfterFailure, "a failed read drops polling to the idle cadence");

    _clock.Advance(TimeSpan.FromSeconds(1));
    WaitForRefreshes(cut, start + 2);
  }

  [Fact]
  public void Sweeping_RefreshesEverySecond()
  {
    GetJson("/api/radio/bandmap", EmptyMap(isSweeping: true, remaining: 12));
    var cut = RenderBand();
    int start = cut.Instance.CompletedBandRefreshes;

    _clock.Advance(VisualizerPanel.BandSweepingRefresh);
    WaitForRefreshes(cut, start + 1);

    _clock.Advance(VisualizerPanel.BandSweepingRefresh);
    WaitForRefreshes(cut, start + 2);
  }

  [Fact]
  public void LeavingBand_StopsTheRefreshTimer()
  {
    var cut = RenderBand();
    cut.FindAll(".visualizer-mode").First(b => b.TextContent.Trim() == "Spectrum").Click();
    cut.WaitForAssertion(() => cut.FindAll(".band-strip").Should().BeEmpty());
    int requestsAfterLeaving = _api.Requests.Count(r => r.Path == "/api/radio/bandmap");

    _clock.Advance(VisualizerPanel.BandIdleRefresh * 4);

    // No further reads. This does not tell the two guards apart — the timer is disposed when BAND is
    // left, and a callback that fired anyway would return early on !_bandActive — it shows that
    // together they stop the polling.
    _api.Requests.Count(r => r.Path == "/api/radio/bandmap").Should().Be(requestsAfterLeaving);
  }

  // ── tap to tune ──────────────────────────────────────────────────────────

  [Fact]
  public async Task Tap_RadioNotActive_SwitchesToRadioBeforeTuning()
  {
    // UI-29 shows BAND only while the radio is active, so this is the race: the radio went away between
    // the render and the tap. The switch it sends then makes the radio active again.
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();
    RadioNotActive();
    _api.OnRequest(HttpMethod.Post, "/api/sources", () => GetJson("/api/radio/state", FmState(101_100_000)));

    // A tap 0.3 MHz below the station snaps to it.
    double fraction = FmBandMath.HzToFraction(99_200_000);
    await cut.InvokeAsync(() => cut.Instance.OnBandTap(fraction));

    int switchAt = IndexOf(HttpMethod.Post, "/api/sources");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    switchAt.Should().BeGreaterThanOrEqualTo(0, "the radio was not the active source");
    tuneAt.Should().BeGreaterThan(switchAt, "the source switch has to land before the frequency is set");

    _api.Requests[switchAt].Body.Should().Contain("\"sourceType\":\"Radio\"");
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(99_500_000);
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Tuning 99.5 FM"));
  }

  [Fact]
  public async Task Tap_RadioActive_TunesWithoutSwitching()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    GetJson("/api/radio/state", FmState(101_100_000));
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(99_200_000)));

    IndexOf(HttpMethod.Post, "/api/sources").Should().Be(-1, "the radio was already the active source");
    IndexOf(HttpMethod.Post, "/api/radio/band").Should().Be(-1, "the radio was already on FM");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    tuneAt.Should().BeGreaterThanOrEqualTo(0);
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(99_500_000);
  }

  [Fact]
  public async Task Tap_RadioOnAnotherBand_TunesTheFmFrequencyWithoutASeparateBandSwitch()
  {
    // RadioReceiver.SetFrequency selects the band containing the frequency itself; a SetBand("FM")
    // first would restart the stream twice.
    GetJson("/api/radio/state", FmState(1_010_000, band: "AM"));
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(95_060_000)));

    IndexOf(HttpMethod.Post, "/api/radio/band").Should().Be(-1);
    IndexOf(HttpMethod.Post, "/api/sources").Should().Be(-1, "the radio was the active source");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    tuneAt.Should().BeGreaterThanOrEqualTo(0);
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(95_100_000, "with no map the tap snaps to the nearest channel");
  }

  [Fact]
  public async Task Tap_StateReadFails_NeitherSwitchesNorTunes()
  {
    // A 500 is not "the radio is not active": switching on it would re-select a radio that may be
    // playing. The tap does nothing and says why.
    var cut = RenderBand();
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.InternalServerError, null);

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(0.5));

    IndexOf(HttpMethod.Post, "/api/sources").Should().Be(-1);
    IndexOf(HttpMethod.Post, "/api/radio/frequency").Should().Be(-1);
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("The radio API is not reachable"));
  }

  [Fact]
  public async Task Tap_DuringASeekScan_StopsTheScanBeforeTuning()
  {
    _api.Post("/api/radio/scan/stop");
    var scanning = new Dictionary<string, object?>
    {
      ["frequency"] = 101_100_000.0, ["band"] = "FM", ["step"] = 200_000, ["signalStrength"] = 50,
      ["isScanning"] = true, ["scanDirection"] = "Up", ["scanStopThreshold"] = -36.0, ["gain"] = 0,
      ["autoGain"] = false, ["equalizer"] = "Normal", ["deviceVolume"] = 50,
    };
    GetJson("/api/radio/state", scanning);
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(95_100_000)));

    int stopAt = IndexOf(HttpMethod.Post, "/api/radio/scan/stop");
    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    stopAt.Should().BeGreaterThanOrEqualTo(0, "a running seek scan would move off the tapped station");
    tuneAt.Should().BeGreaterThan(stopAt);
  }

  [Fact]
  public async Task TuneMessage_IsClearedWhenItsLifetimeEnds_NotOnTheIdleCadence()
  {
    GetJson("/api/radio/state", FmState(101_100_000));
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(95_100_000)));
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Tuning 95.1 FM"));
    int cycles = cut.Instance.CompletedBandRefreshes;

    _clock.Advance(VisualizerPanel.BandMessageLifetime);

    WaitForRefreshes(cut, cycles + 1);
    cut.WaitForAssertion(() => cut.FindAll(".band-status-message").Should().BeEmpty());
  }

  [Fact]
  public async Task Tap_SwitchFails_DoesNotTune()
  {
    _api.Post("/api/sources", HttpStatusCode.InternalServerError);
    var cut = RenderBand();
    RadioNotActive();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(0.5));

    IndexOf(HttpMethod.Post, "/api/radio/frequency").Should().Be(-1);
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Could not switch to the radio"));
  }

  // ── per band (AUD-91) ────────────────────────────────────────────────────

  /// <summary>
  /// A map response for a non-FM band, with the axis fields the API sends for it
  /// (BandMapController.WithAxis). <paramref name="channels"/> null: no scan yet.
  /// </summary>
  private static object BandMap(
    string band, long displayMin, long displayMax, long first, long last, long spacing,
    IEnumerable<object>? channels = null, bool mappable = true, string? reason = null, object? sweep = null) => new
  {
    band,
    mappable,
    unavailableReason = reason,
    displayMinHz = displayMin,
    displayMaxHz = displayMax,
    firstChannelHz = first,
    lastChannelHz = last,
    channelSpacingHz = spacing,
    scannedAtUtc = channels == null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddSeconds(-180),
    ageSeconds = channels == null ? (double?)null : 180.0,
    channels = channels ?? Array.Empty<object>(),
    sweep = sweep ?? Sweep(),
  };

  private static object WbMap() => BandMap("WB", 162_387_500, 162_562_500, 162_400_000, 162_550_000, 25_000,
    Enumerable.Range(0, 7).Select(i => (object)new { frequencyHz = 162_400_000L + i * 25_000L, levelDbfs = i == 3 ? -30f : -60f }));

  /// <summary>AIR channels 117.900–118.200 MHz at -60 dB, with a station at 118.050.</summary>
  private static object AirMap()
  {
    var channels = new List<object>();
    for (long hz = 117_900_000; hz <= 118_200_000; hz += 25_000)
    {
      channels.Add(new { frequencyHz = hz, levelDbfs = hz == 118_050_000 ? -30f : -60f });
    }

    return BandMap("AIR", 108_000_000, 137_000_000, 108_000_000, 137_000_000, 25_000, channels);
  }

  private const string AmReason =
    "The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit and cannot be scanned.";

  private static object AmMap() =>
    BandMap("AM", 530_000, 1_710_000, 530_000, 1_710_000, 10_000, mappable: false, reason: AmReason);

  private static RadioStateDto HubState(string band, double frequencyHz) => new(
    Frequency: frequencyHz, Band: band, Step: 25_000, SignalStrength: 50, IsScanning: false, ScanDirection: null,
    ScanStopThreshold: -36.0, Gain: 0, AutoGain: false, Equalizer: "Normal", DeviceVolume: 50);

  /// <summary>
  /// Raises a broadcast and waits until the panel has finished handling it. The handler returns to the
  /// hub at once and works afterwards, so the rendezvous is the panel's own count, not the raise.
  /// </summary>
  private async Task RaiseRadioStateAsync(IRenderedComponent<VisualizerPanel> cut, RadioStateDto dto)
  {
    int handled = cut.Instance.CompletedRadioStateUpdates;
    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      Services.GetRequiredService<AudioStateHubService>(), nameof(AudioStateHubService.RadioStateChanged), dto));
    cut.WaitForAssertion(() => cut.Instance.CompletedRadioStateUpdates.Should().BeGreaterThan(handled),
      TimeSpan.FromSeconds(5));
  }

  private int DrawCalls() =>
    _module.Invocations["visualizer.drawBandMap"].Count;

  private string[] AxisLabels(IRenderedComponent<VisualizerPanel> cut) =>
    cut.FindAll(".band-axis-labels span").Select(s => s.TextContent).ToArray();

  /// <summary>The model of the last <c>visualizer.drawBandMap</c> call, as the JSON the browser receives.</summary>
  private JsonElement LastDrawModel()
  {
    JSRuntimeInvocation draw = _module.Invocations["visualizer.drawBandMap"].Last();
    return JsonSerializer.SerializeToElement(draw.Arguments[1], JsonSerializerOptions.Web);
  }

  [Fact]
  public void FmMap_DrawModel_KeepsAud76sGridlinesAndBarWidth()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();

    cut.WaitForAssertion(() => _module.Invocations["visualizer.drawBandMap"].Should().NotBeEmpty());
    JsonElement model = LastDrawModel();
    model.GetProperty("channelsAcross").GetDouble().Should().Be(102.5, "AUD-76 drew bars width / 102.5 * 0.5 wide");
    model.GetProperty("grid").EnumerateArray().Select(g => g.GetDouble()).Should().Equal(
      new[] { 88.0, 92, 96, 100, 104, 108 }.Select(mhz => (mhz - 87.5) / 20.5),
      (a, b) => Math.Abs(a - b) < 1e-12);
    cut.Find(".band-strip-name").TextContent.Should().Be("FM");
    cut.Find(".band-discover-btn").GetAttribute("aria-label").Should().Be("Discover stations in the FM band");
  }

  [Fact]
  public void WbMap_DrawsWbTicks_AndNamesTheBand()
  {
    GetJson("/api/radio/bandmap", WbMap());

    var cut = RenderBand();

    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("162.40", "162.45", "162.50", "162.55"));
    cut.Find(".band-discover-btn").GetAttribute("aria-label").Should().Be("Discover stations in the WB band");
    cut.Find(".visualizer-canvas").GetAttribute("aria-label").Should().Be("WB signal map. Touch a signal to tune.");
    cut.Find(".band-strip-name").TextContent.Should().Be("WB");
    cut.Find(".band-status-age").TextContent.Should().Be("discovered 3 min ago");
    LastDrawModel().GetProperty("grid").GetArrayLength().Should().Be(4);
  }

  [Fact]
  public async Task AmMap_DisablesDiscover_ShowsTheReason_AndATapTunesNothing()
  {
    GetJson("/api/radio/bandmap", AmMap());
    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-empty-title").TextContent.Should().Be("AM is out of this radio's range"));
    cut.Find(".band-empty-sub").TextContent.Should().Be(AmReason);
    cut.Markup.Should().NotContain("No stations discovered yet");
    cut.Find(".band-discover-btn").HasAttribute("disabled").Should().BeTrue();
    AxisLabels(cut).Should().BeEmpty("UI-31: no axis on a band this radio cannot receive");

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(0.5));

    IndexOf(HttpMethod.Post, "/api/radio/frequency").Should().Be(-1, "AM cannot be mapped, so there is nothing to tap");
    IndexOf(HttpMethod.Post, "/api/sources").Should().Be(-1);
  }

  [Fact]
  public async Task AirMap_Tap_TunesWithTheBand()
  {
    GetJson("/api/radio/bandmap", AirMap());
    GetJson("/api/radio/state", FmState(118_000_000, band: "AIR"));
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("AIR"));

    // 40 kHz below the station: inside AIR's snap, so the tap lands on it.
    BandAxis air = new("AIR", 108_000_000, 137_000_000, 108_000_000, 137_000_000, 25_000);
    await cut.InvokeAsync(() => cut.Instance.OnBandTap(air.HzToFraction(118_010_000)));

    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    tuneAt.Should().BeGreaterThanOrEqualTo(0);
    JsonElement body = JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement;
    body.GetProperty("frequency").GetDouble().Should().Be(118_050_000);
    body.GetProperty("band").GetString().Should().Be("AIR");
    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Tuning 118.050 MHz AIR"));
  }

  [Fact]
  public async Task AirMap_TapThreeHundredKhzFromAStation_TunesTheStation()
  {
    // AIR's two-spacing snap (±50 kHz) is about ±1.5 px across 29 MHz; 2% of the span (±580 kHz) is a
    // window a finger can hit.
    GetJson("/api/radio/bandmap", AirMap());
    GetJson("/api/radio/state", FmState(118_000_000, band: "AIR"));
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("AIR"));

    BandAxis air = new("AIR", 108_000_000, 137_000_000, 108_000_000, 137_000_000, 25_000);
    await cut.InvokeAsync(() => cut.Instance.OnBandTap(air.HzToFraction(118_350_000)));

    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    tuneAt.Should().BeGreaterThanOrEqualTo(0);
    JsonDocument.Parse(_api.Requests[tuneAt].Body!).RootElement.GetProperty("frequency").GetDouble()
      .Should().Be(118_050_000);
  }

  [Fact]
  public async Task WbMap_Tap_NamesTheStationExactlyWithItsUnit()
  {
    GetJson("/api/radio/bandmap", WbMap());
    GetJson("/api/radio/state", FmState(162_400_000, band: "WB"));
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("WB"));

    BandAxis wb = new("WB", 162_387_500, 162_562_500, 162_400_000, 162_550_000, 25_000);
    await cut.InvokeAsync(() => cut.Instance.OnBandTap(wb.HzToFraction(162_475_000)));

    cut.WaitForAssertion(() => cut.Find(".band-status-message").TextContent.Should().Be("Tuning 162.475 MHz WB"));
  }

  [Fact]
  public async Task FmMap_Tap_SendsTheAud76BodyWithNoBand()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    GetJson("/api/radio/state", FmState(101_100_000));
    var cut = RenderBand();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(99_200_000)));

    int tuneAt = IndexOf(HttpMethod.Post, "/api/radio/frequency");
    tuneAt.Should().BeGreaterThanOrEqualTo(0);
    _api.Requests[tuneAt].Body.Should().Be("{\"frequency\":99500000}");
  }

  [Fact]
  public void Discover_PostsTheShownBand()
  {
    GetJson("/api/radio/bandmap", WbMap());
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.Accepted, "{}");
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("WB"));

    cut.Find(".band-discover-btn").Click();

    cut.WaitForAssertion(() => IndexOf(HttpMethod.Post, "/api/radio/bandmap/scan").Should().BeGreaterThanOrEqualTo(0));
    _api.Requests[IndexOf(HttpMethod.Post, "/api/radio/bandmap/scan")].Query.Should().Be("?band=WB");
  }

  [Fact]
  public async Task HubBandChange_ReReadsTheMapAtOnce_ButOncePerChange()
  {
    var cut = RenderBand();
    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("88", "92", "96", "100", "104", "108"));
    int readsBefore = MapReads();

    // The owner picks AIR in the radio control panel; the API now answers for AIR.
    GetJson("/api/radio/bandmap", AirMap());
    await RaiseRadioStateAsync(cut, HubState("AIR", 118_000_000));

    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("110", "115", "120", "125", "130", "135"));
    MapReads().Should().Be(readsBefore + 1, "the band change re-reads the map without waiting for the timer");

    // Telemetry ticks repeat the state; a new frequency in the same band moves only the marker.
    await RaiseRadioStateAsync(cut, HubState("AIR", 118_000_000));
    await RaiseRadioStateAsync(cut, HubState("AIR", 118_050_000));
    MapReads().Should().Be(readsBefore + 1, "only a band change re-reads the map");
  }

  [Fact]
  public async Task HubBandChange_RepeatedWhileTheMapStillShowsTheOldBand_ReadsOnce()
  {
    // The map read answers FM (say the API has not caught up). Telemetry ticks keep saying AIR; each
    // one must not trigger another read — the idle refresh retries instead.
    var cut = RenderBand();
    int readsBefore = MapReads();

    await RaiseRadioStateAsync(cut, HubState("AIR", 118_000_000));
    await RaiseRadioStateAsync(cut, HubState("AIR", 118_000_000));
    await RaiseRadioStateAsync(cut, HubState("AIR", 118_025_000));

    MapReads().Should().Be(readsBefore + 1);
  }

  [Fact]
  public async Task HubFrequencyChange_SameBand_MovesTheStationMarker()
  {
    GetJson("/api/radio/bandmap", AirMap());
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("AIR"));
    int readsBefore = MapReads();

    await RaiseRadioStateAsync(cut, HubState("AIR", 122_500_000));

    LastDrawModel().GetProperty("station").GetDouble().Should().Be(0.5);
    MapReads().Should().Be(readsBefore);
  }

  [Fact]
  public void SweepOfAnotherBand_NamesThatBand()
  {
    object sweep = new { isSweeping = true, band = "AIR", trigger = "request", path = "idle", progress = 0.4, estimatedSecondsRemaining = 12.0 };
    GetJson("/api/radio/bandmap", BandMap("WB", 162_387_500, 162_562_500, 162_400_000, 162_550_000, 25_000, sweep: sweep));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-discovering").TextContent.Should().Be("Discovering AIR… 12 s"));
    cut.Find(".band-empty-title").TextContent.Should().Be("No stations discovered yet", "the sweep running is not of the band shown");
  }

  // ── review fixes (AUD-91) ────────────────────────────────────────────────

  [Fact]
  public async Task HubBroadcast_ReturnsToTheHubAtOnce_WhileTheMapReadIsStillInFlight()
  {
    // The hub calls its subscribers one after another; a handler that awaited the map read would hold
    // every subscriber after it until the API answered.
    var cut = RenderBand();
    int readsBefore = MapReads();
    TaskCompletionSource release = _api.Hold(HttpMethod.Get, "/api/radio/bandmap");
    GetJson("/api/radio/bandmap", AirMap());

    Task raise = HubEventFire.FireAsync(
      Services.GetRequiredService<AudioStateHubService>(), nameof(AudioStateHubService.RadioStateChanged),
      HubState("AIR", 118_000_000));

    cut.WaitForAssertion(() => MapReads().Should().Be(readsBefore + 1), TimeSpan.FromSeconds(5));
    raise.IsCompleted.Should().BeTrue("the handler must not hold the hub while its map read is pending");

    release.SetResult();
    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("110", "115", "120", "125", "130", "135"),
      TimeSpan.FromSeconds(5));
  }

  private string[] AxisLabelClasses(IRenderedComponent<VisualizerPanel> cut) =>
    cut.FindAll(".band-axis-labels span").Select(s => s.GetAttribute("class") ?? "").ToArray();

  [Fact]
  public void Axis_LabelClasses_FmOnlyItsLastIsEndAligned()
  {
    var cut = RenderBand();

    AxisLabelClasses(cut).Should().Equal("", "", "", "", "", "is-edge-end");
  }

  [Fact]
  public void Axis_LabelClasses_VhfWindowWithTicksAtBothEdges()
  {
    GetJson("/api/radio/bandmap", BandMap("VHF", 161_500_000, 163_500_000, 161_500_000, 163_500_000, 12_500));

    var cut = RenderBand();

    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("161.5", "162.0", "162.5", "163.0", "163.5"));
    AxisLabelClasses(cut).Should().Equal("is-edge-start", "", "", "", "is-edge-end");
  }

  [Fact]
  public void Axis_LabelClasses_WbLastLabelIsCentred()
  {
    GetJson("/api/radio/bandmap", WbMap());

    var cut = RenderBand();

    cut.WaitForAssertion(() => AxisLabels(cut).Should().HaveCount(4));
    AxisLabelClasses(cut).Should().Equal("", "", "", "");
  }

  [Fact]
  public async Task HubRepeatedTick_SameFrequency_DoesNotRedraw()
  {
    GetJson("/api/radio/bandmap", AirMap());
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("AIR"));
    await RaiseRadioStateAsync(cut, HubState("AIR", 122_500_000));
    int draws = DrawCalls();

    // A telemetry tick repeating the state: up to twice a second, and AIR's map is ~1161 levels.
    await RaiseRadioStateAsync(cut, HubState("AIR", 122_500_000));

    DrawCalls().Should().Be(draws);
  }

  [Fact]
  public async Task HubVhfFrequencyOutsideTheWindow_ReReadsTheMap_OncePerWindow()
  {
    GetJson("/api/radio/bandmap", BandMap("VHF", 161_400_000, 163_400_000, 161_400_000, 163_400_000, 12_500));
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("VHF"));
    int readsBefore = MapReads();

    // Inside the window: a marker move, no read.
    await RaiseRadioStateAsync(cut, HubState("VHF", 162_000_000));
    MapReads().Should().Be(readsBefore);

    // Outside it: one read, however many ticks repeat it while the API still answers the old window.
    await RaiseRadioStateAsync(cut, HubState("VHF", 170_000_000));
    await RaiseRadioStateAsync(cut, HubState("VHF", 170_000_000));
    await RaiseRadioStateAsync(cut, HubState("VHF", 170_012_500));
    MapReads().Should().Be(readsBefore + 1);

    // The API's window has followed the radio: the next read brings it, and it is drawn.
    GetJson("/api/radio/bandmap", BandMap("VHF", 169_000_000, 171_000_000, 169_000_000, 171_000_000, 12_500));
    await RaiseRadioStateAsync(cut, HubState("VHF", 172_500_000));
    MapReads().Should().Be(readsBefore + 1, "the window shown has not changed, so its read is not repeated");
    _clock.Advance(VisualizerPanel.BandIdleRefresh);
    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("169.0", "169.5", "170.0", "170.5", "171.0"),
      TimeSpan.FromSeconds(5));

    await RaiseRadioStateAsync(cut, HubState("VHF", 172_500_000));
    MapReads().Should().Be(readsBefore + 3, "a new window shown re-arms the out-of-window read");
  }

  [Fact]
  public void BeforeTheFirstMapRead_NoBandIsNamed()
  {
    TaskCompletionSource release = _api.Hold(HttpMethod.Get, "/api/radio/bandmap");
    var cut = RenderComponent<VisualizerPanel>(p => p.Add(x => x.Clock, _clock));
    cut.WaitForAssertion(() =>
    {
      cut.FindAll(".band-strip").Should().HaveCount(1);
      MapReads().Should().BeGreaterThanOrEqualTo(1, "the first map read is in flight");
    }, TimeSpan.FromSeconds(5));

    cut.FindAll(".band-strip-name").Should().BeEmpty("the band is not known until a map has been read");
    cut.Find(".band-discover-btn").GetAttribute("aria-label").Should().Be("Discover stations in this band");

    release.SetResult();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("FM"), TimeSpan.FromSeconds(5));
  }

  [Fact]
  public void MapWithoutAUsableAxis_IsStillNamedForItsBand()
  {
    // Drawn on FM's axis for want of its own, but it is WB's map.
    GetJson("/api/radio/bandmap", BandMap("WB", 0, 0, 0, 0, 0));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("WB"));
    cut.Find(".band-discover-btn").GetAttribute("aria-label").Should().Be("Discover stations in the WB band");
  }

  [Fact]
  public void WbMap_DrawsNeitherFmPresetsNorAnFmStationMarker()
  {
    GetJson("/api/radio/bandmap", WbMap());
    GetJson("/api/radio/state", FmState(162_475_000, band: "FM"));
    // The FM preset and the FM station sit at frequencies inside WB's plot, so only the band keeps
    // them off it.
    GetJson("/api/radio/presets", new object[]
    {
      new { id = "1", name = "FMX", band = "FM", frequency = 162_450_000.0 },
      new { id = "2", name = "NOAA", band = "WB", frequency = 162_475_000.0 },
    });

    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("WB"));

    JsonElement model = LastDrawModel();
    model.GetProperty("station").ValueKind.Should().Be(JsonValueKind.Null, "the radio is on FM, not WB");
    BandAxis wb = new("WB", 162_387_500, 162_562_500, 162_400_000, 162_550_000, 25_000);
    model.GetProperty("presets").EnumerateArray().Select(p => p.GetProperty("f").GetDouble())
      .Should().Equal(new[] { wb.HzToFraction(162_475_000) }, "only the shown band's presets are drawn");
  }

  // ── signal colour (UI-22) ────────────────────────────────────────────────

  private string[] LegendWords(IRenderedComponent<VisualizerPanel> cut) =>
    cut.FindAll(".band-legend-item").Select(i => i.TextContent.Trim()).ToArray();

  [Fact]
  public void FmMap_ShowsTheColourLegend_AfterTheAge()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));

    var cut = RenderBand();

    cut.WaitForAssertion(() => LegendWords(cut).Should().Equal("weak", "fair", "strong"));
    cut.FindAll(".band-legend-swatch").Select(s => s.GetAttribute("class"))
      .Should().Equal("band-legend-swatch is-weak", "band-legend-swatch is-fair", "band-legend-swatch is-strong");
    cut.FindAll(".band-legend-swatch").Should().OnlyContain(s => s.GetAttribute("aria-hidden") == "true");
    cut.Find(".band-status-line").TextContent.Should().MatchRegex(@"discovered 1 min ago\s*weak\s*fair\s*strong");
  }

  [Fact]
  public void EmptyMap_HasNoLegend()
  {
    var cut = RenderBand();

    cut.Find(".band-empty-title").TextContent.Should().Be("No stations discovered yet");
    cut.FindAll(".band-legend").Should().BeEmpty();
  }

  // ── seek-observed stations (AUD-100) ─────────────────────────────────────

  /// <summary><paramref name="map"/> with a <c>seekStations</c> list at <paramref name="hz"/>.</summary>
  private static System.Text.Json.Nodes.JsonObject WithSeekStations(object map, params long[] hz)
  {
    System.Text.Json.Nodes.JsonObject json = JsonSerializer.SerializeToNode(map, JsonSerializerOptions.Web)!.AsObject();
    json["seekStations"] = new System.Text.Json.Nodes.JsonArray(hz
      .Select(f => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
      {
        ["frequencyHz"] = f,
        ["seekStrength"] = 0.9,
        ["observedAtUtc"] = "2026-10-04T12:00:00Z",
      })
      .ToArray());
    return json;
  }

  private static double[] SeekFractions(JsonElement model) =>
    model.GetProperty("seek").EnumerateArray().Select(s => s.GetProperty("f").GetDouble()).ToArray();

  private int BandMapReads() =>
    _api.Requests.Count(r => r.Method == HttpMethod.Get && r.Path == "/api/radio/bandmap");

  [Fact]
  public void UnsweptBand_WithSeekStations_DrawsTheirMarkers_AndStillReadsAsUndiscovered()
  {
    GetJson("/api/radio/bandmap", WithSeekStations(EmptyMap(), 95_500_000, 101_100_000));

    var cut = RenderBand();

    cut.WaitForAssertion(() => SeekFractions(LastDrawModel()).Should().Equal(
      BandAxis.Fm.HzToFraction(95_500_000), BandAxis.Fm.HzToFraction(101_100_000)));
    LastDrawModel().GetProperty("levels").GetArrayLength().Should().Be(0, "a seek stop is not a swept channel");
    cut.Find(".band-empty-title").TextContent.Should().Be("No stations discovered yet");
    cut.Find(".band-empty-sub").TextContent.Should().Be("The rings mark where Scan stopped. Tap Discover to sweep the whole band.");
    cut.FindAll(".band-status-age").Should().BeEmpty("a seek never makes a band look swept");
    LegendWords(cut).Should().Equal("found by scan");
    cut.Find(".band-legend-swatch").GetAttribute("class").Should().Be("band-legend-swatch is-seek");
  }

  [Fact]
  public void SweptBand_WithSeekStations_KeepsItsBarsAndTiers_AndAddsTheMarkersAndTheirKey()
  {
    GetJson("/api/radio/bandmap", WithSeekStations(MapWithStation(ageSeconds: 60), 95_500_000));

    var cut = RenderBand();

    cut.WaitForAssertion(() => LegendWords(cut).Should().Equal("weak", "fair", "strong", "found by scan"));
    JsonElement model = LastDrawModel();
    model.GetProperty("levels").GetArrayLength().Should().Be(101);
    SeekFractions(model).Should().Equal(BandAxis.Fm.HzToFraction(95_500_000));
    cut.Find(".band-status-age").TextContent.Should().Be("discovered 1 min ago");
  }

  [Fact]
  public void SeekStations_OffTheAxis_AreNotDrawn_AndNeedNoKey()
  {
    GetJson("/api/radio/bandmap", WithSeekStations(EmptyMap(), 120_000_000));

    var cut = RenderBand();

    cut.WaitForAssertion(() => _module.Invocations["visualizer.drawBandMap"].Should().NotBeEmpty());
    SeekFractions(LastDrawModel()).Should().BeEmpty();
    cut.FindAll(".band-legend").Should().BeEmpty();
    cut.Find(".band-empty-sub").TextContent.Should().StartWith("The band is swept automatically");
  }

  [Fact]
  public void OutOfRangeBand_DrawsNoSeekMarkers()
  {
    GetJson("/api/radio/bandmap", WithSeekStations(AmMap(), 1_000_000));

    var cut = RenderBand();

    cut.WaitForAssertion(() => _module.Invocations["visualizer.drawBandMap"].Should().NotBeEmpty());
    SeekFractions(LastDrawModel()).Should().BeEmpty();
    cut.FindAll(".band-legend").Should().BeEmpty();
  }

  [Fact]
  public async Task AScanEnding_RereadsTheMapOnce_SoItsStopsAppearAtOnce()
  {
    var cut = RenderBand();
    await RaiseRadioStateAsync(cut, HubState("FM", 101_100_000) with { IsScanning = true });
    int readsBefore = BandMapReads();
    GetJson("/api/radio/bandmap", WithSeekStations(EmptyMap(), 95_500_000));

    await RaiseRadioStateAsync(cut, HubState("FM", 95_500_000) with { IsScanning = false });

    cut.WaitForAssertion(() => SeekFractions(LastDrawModel()).Should().Equal(BandAxis.Fm.HzToFraction(95_500_000)));
    BandMapReads().Should().Be(readsBefore + 1);

    // A repeated not-scanning tick does not read again.
    await RaiseRadioStateAsync(cut, HubState("FM", 95_500_000));
    BandMapReads().Should().Be(readsBefore + 1);
  }

  [Theory]
  [InlineData("AM", 530_000L, 1_710_000L, 10_000L)]
  [InlineData("SW", 2_300_000L, 26_100_000L, 5_000L)]
  public void OutOfRangeBand_HasNoLegend_EvenWithChannelsInTheResponse(string band, long min, long max, long spacing)
  {
    // The channels are there so that only the out-of-range guard can be what hides the legend.
    GetJson("/api/radio/bandmap", BandMap(band, min, max, min, max, spacing,
      channels: [new { frequencyHz = min, levelDbfs = -30f }, new { frequencyHz = min + spacing, levelDbfs = -60f }],
      mappable: false, reason: "Out of range."));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-empty-title").TextContent.Should().Be($"{band} is out of this radio's range"));
    cut.FindAll(".band-legend").Should().BeEmpty();
  }

  [Fact]
  public void SweepOfTheShownBand_KeepsTheLegend_WhileItsMapIsDrawn()
  {
    object sweep = new { isSweeping = true, band = "FM", trigger = "request", path = "idle", progress = 0.4, estimatedSecondsRemaining = 12.0 };
    var map = new List<object>();
    for (long hz = FmBandMath.FirstChannelHz; hz <= FmBandMath.LastChannelHz; hz += FmBandMath.ChannelSpacingHz)
    {
      map.Add(new { frequencyHz = hz, levelDbfs = hz == 99_500_000 ? -30f : -60f });
    }

    GetJson("/api/radio/bandmap", BandMap("FM", 87_500_000, 108_000_000, 87_900_000, 107_900_000, 200_000, map, sweep: sweep));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-discovering").TextContent.Should().Be("Discovering… 12 s"));
    LegendWords(cut).Should().Equal("weak", "fair", "strong");
  }

  [Fact]
  public void FmMap_DrawModel_CarriesEachBarsTier()
  {
    // 99.5 at 30 dB above the -60 median is strong; every other channel is the noise floor.
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();
    cut.WaitForAssertion(() => _module.Invocations["visualizer.drawBandMap"].Should().NotBeEmpty());

    JsonElement[] levels = LastDrawModel().GetProperty("levels").EnumerateArray().ToArray();
    levels.Should().HaveCount(101);
    double station = FmBandMath.HzToFraction(99_500_000);
    levels.Single(l => Math.Abs(l.GetProperty("f").GetDouble() - station) < 1e-12).GetProperty("t").GetInt32()
      .Should().Be((int)BandSignalTier.Strong);
    levels.Count(l => l.GetProperty("t").GetInt32() == (int)BandSignalTier.Noise).Should().Be(100);
  }

  [Fact]
  public void WbMap_DrawModel_TiersFollowTheWbMapsOwnMedian()
  {
    // WB: seven channels, one at -30 over a -60 floor. A band other than FM gets tiers the same way.
    GetJson("/api/radio/bandmap", WbMap());
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("WB"));

    LastDrawModel().GetProperty("levels").EnumerateArray().Select(l => l.GetProperty("t").GetInt32())
      .Should().Equal(0, 0, 0, 3, 0, 0, 0);
    LegendWords(cut).Should().Equal("weak", "fair", "strong");
  }

  [Fact]
  public async Task Dispose_UnsubscribesFromTheHub_AndALaterBroadcastDoesNothing()
  {
    var cut = RenderBand();
    AudioStateHubService hub = Services.GetRequiredService<AudioStateHubService>();
    HubEventFire.InvocationListOf<Func<RadioStateDto, Task>>(hub, nameof(AudioStateHubService.RadioStateChanged))
      .Should().Contain(d => d.Target is VisualizerPanel);
    int readsBefore = MapReads();

    DisposeComponents();

    HubEventFire.InvocationListOf<Func<RadioStateDto, Task>>(hub, nameof(AudioStateHubService.RadioStateChanged))
      .Should().NotContain(d => d.Target is VisualizerPanel);
    await HubEventFire.FireAsync(hub, nameof(AudioStateHubService.RadioStateChanged), HubState("AIR", 118_000_000));
    MapReads().Should().Be(readsBefore);
  }

  // ── UI-29: BAND only while the radio is the active source ───────────────

  private static string[] TabLabels(IRenderedComponent<VisualizerPanel> cut) =>
    cut.FindAll(".visualizer-mode").Select(b => b.TextContent.Trim()).ToArray();

  private static AngleSharp.Dom.IElement Tab(IRenderedComponent<VisualizerPanel> cut, string label) =>
    cut.FindAll(".visualizer-mode").Single(b => b.TextContent.Trim() == label);

  private static bool IsActive(AngleSharp.Dom.IElement tab) =>
    (tab.GetAttribute("class") ?? string.Empty).Split(' ').Contains("is-active");

  /// <summary>Every visualizer preference write, as the bodies POSTed to the configuration API.</summary>
  private string[] PreferenceWrites() => _api.Requests
    .Where(r => r.Method == HttpMethod.Post && r.Path == "/api/configuration/ui.visualizer")
    .Select(r => r.Body ?? string.Empty)
    .ToArray();

  private async Task RaiseSourceChangedAsync(IRenderedComponent<VisualizerPanel> cut)
  {
    int handled = cut.Instance.CompletedSourceChecks;
    await cut.InvokeAsync(() => HubEventFire.FireAsync(
      Services.GetRequiredService<AudioStateHubService>(), nameof(AudioStateHubService.SourceChanged)));
    cut.WaitForAssertion(() => cut.Instance.CompletedSourceChecks.Should().BeGreaterThan(handled),
      TimeSpan.FromSeconds(5));
  }

  /// <summary>Renders with the radio NOT active and waits for the canvas and the radio-state read.</summary>
  private IRenderedComponent<VisualizerPanel> RenderWithRadioNotActive()
  {
    RadioNotActive();
    var cut = RenderComponent<VisualizerPanel>(p => p.Add(x => x.Clock, _clock));
    cut.WaitForAssertion(() =>
    {
      _api.Requests.Should().Contain(r => r.Path == "/api/radio/state");
      _module.Invocations["visualizer.init"].Should().NotBeEmpty();
      IsActive(Tab(cut, "Spectrum")).Should().BeTrue();
    }, TimeSpan.FromSeconds(5));
    return cut;
  }

  [Fact]
  public void BandTab_IsLast()
  {
    var cut = RenderBand();
    TabLabels(cut).Should().Equal("Wave", "Spectrum", "Ring", "Phase", "Radio");
  }

  [Fact]
  public void RadioActive_BandTabIsEnabled()
  {
    var cut = RenderBand();
    var band = Tab(cut, "Radio");
    band.HasAttribute("disabled").Should().BeFalse();
    band.GetAttribute("aria-disabled").Should().Be("false");
    IsActive(band).Should().BeTrue();
  }

  [Fact]
  public void SavedBand_RadioNotActive_ShowsSpectrum_DisablesBand_AndKeepsThePreference()
  {
    var cut = RenderWithRadioNotActive();

    var band = Tab(cut, "Radio");
    band.HasAttribute("disabled").Should().BeTrue("BAND is not selectable while the radio is not active");
    band.GetAttribute("aria-disabled").Should().Be("true");
    band.HasAttribute("title").Should().BeFalse("UI-31: no tooltip on the greyed tab");
    IsActive(band).Should().BeFalse();
    cut.FindAll(".band-strip").Should().BeEmpty();
    _api.Requests.Should().NotContain(r => r.Path == "/api/radio/bandmap", "BAND is not running");
    PreferenceWrites().Should().BeEmpty("the fallback is display-only; the saved BAND preference stays");
  }

  [Fact]
  public async Task RadioGoesAway_WhileOnBand_FallsBackToSpectrum_ThenReturnsToBand()
  {
    var cut = RenderBand();

    RadioNotActive();
    await RaiseSourceChangedAsync(cut);

    cut.WaitForAssertion(() =>
    {
      IsActive(Tab(cut, "Spectrum")).Should().BeTrue();
      Tab(cut, "Radio").HasAttribute("disabled").Should().BeTrue();
      cut.FindAll(".band-strip").Should().BeEmpty();
    });

    // BAND's polling stopped with it.
    int mapReads = MapReads();
    _clock.Advance(VisualizerPanel.BandIdleRefresh);
    MapReads().Should().Be(mapReads);

    GetJson("/api/radio/state", FmState(101_100_000));
    await RaiseSourceChangedAsync(cut);

    cut.WaitForAssertion(() =>
    {
      IsActive(Tab(cut, "Radio")).Should().BeTrue("the saved preference is still BAND");
      cut.FindAll(".band-strip").Should().HaveCount(1);
    });
    PreferenceWrites().Should().BeEmpty("neither the fallback nor the return is a pick");
  }

  [Fact]
  public async Task PickingAnotherTab_WhileFallenBack_ReplacesThePreference()
  {
    var cut = RenderWithRadioNotActive();

    Tab(cut, "Wave").Click();
    cut.WaitForAssertion(() => PreferenceWrites().Should().ContainSingle().Which.Should().Contain("Waveform"));

    GetJson("/api/radio/state", FmState(101_100_000));
    await RaiseSourceChangedAsync(cut);

    IsActive(Tab(cut, "Wave")).Should().BeTrue("the pick replaced the BAND preference");
    cut.FindAll(".band-strip").Should().BeEmpty();
  }

  [Fact]
  public async Task PickingSpectrum_WhileFallenBack_ReplacesThePreference_EvenThoughItIsAlreadyShown()
  {
    var cut = RenderWithRadioNotActive();

    Tab(cut, "Spectrum").Click();
    cut.WaitForAssertion(() => PreferenceWrites().Should().ContainSingle().Which.Should().Contain("Spectrum"));

    GetJson("/api/radio/state", FmState(101_100_000));
    await RaiseSourceChangedAsync(cut);

    IsActive(Tab(cut, "Spectrum")).Should().BeTrue("Spectrum was picked; BAND is no longer the preference");
  }

  [Fact]
  public async Task RadioBecomesActive_BandTabEnables_AndPickingItSavesBand()
  {
    GetJson("/api/configuration/ui.visualizer", new Dictionary<string, object> { ["defaultMode"] = "Spectrum" });
    var cut = RenderWithRadioNotActive();
    Tab(cut, "Radio").HasAttribute("disabled").Should().BeTrue();

    GetJson("/api/radio/state", FmState(101_100_000));
    await RaiseSourceChangedAsync(cut);
    cut.WaitForAssertion(() => Tab(cut, "Radio").HasAttribute("disabled").Should().BeFalse());
    IsActive(Tab(cut, "Spectrum")).Should().BeTrue("the preference is Spectrum; the radio arriving changes nothing else");

    Tab(cut, "Radio").Click();
    cut.WaitForAssertion(() =>
    {
      IsActive(Tab(cut, "Radio")).Should().BeTrue();
      cut.FindAll(".band-strip").Should().HaveCount(1);
      PreferenceWrites().Should().ContainSingle().Which.Should().Contain("Band");
    });
  }

  [Fact]
  public async Task ATapOnTheDisabledBandTab_SavesNothing()
  {
    // A browser does not deliver a click to a disabled button, but a tap can land in the moment between the
    // radio going away and the re-render. The screen would stay right anyway (the reconcile shows Spectrum);
    // what the guard in SelectMode prevents is BAND being saved as the preference by a tab the user could
    // not see was off.
    GetJson("/api/configuration/ui.visualizer", new Dictionary<string, object> { ["defaultMode"] = "Spectrum" });
    var cut = RenderWithRadioNotActive();

    await cut.InvokeAsync(() => Tab(cut, "Radio").Click());

    PreferenceWrites().Should().BeEmpty();
    IsActive(Tab(cut, "Spectrum")).Should().BeTrue();
  }

  [Fact]
  public async Task StartupReadFails_ThenARadioStateBroadcast_EnablesAndShowsBand()
  {
    // radio-web up before radio-api, or an API restart while the kiosk reloads: the start-up read fails, so
    // the panel does not know the radio is active. The server sends RadioStateChanged only while it is.
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.InternalServerError, null);
    var cut = RenderComponent<VisualizerPanel>(p => p.Add(x => x.Clock, _clock));
    cut.WaitForAssertion(() =>
    {
      _module.Invocations["visualizer.init"].Should().NotBeEmpty();
      Tab(cut, "Radio").HasAttribute("disabled").Should().BeTrue("the read failed, so the radio is not known to be active");
    }, TimeSpan.FromSeconds(5));

    GetJson("/api/radio/state", FmState(101_100_000));
    await RaiseRadioStateAsync(cut, HubState("FM", 101_100_000));

    cut.WaitForAssertion(() =>
    {
      Tab(cut, "Radio").HasAttribute("disabled").Should().BeFalse();
      IsActive(Tab(cut, "Radio")).Should().BeTrue("the saved preference is BAND");
      cut.FindAll(".band-strip").Should().HaveCount(1);
    });
    PreferenceWrites().Should().BeEmpty();
  }

  [Fact]
  public void BandsOwnRefresh_SeesTheRadioGone_AndFallsBack_WithoutASourceChanged()
  {
    // A SourceChanged missed while the audio-state hub was reconnecting: BAND's next state read is the
    // other way the panel learns the radio has gone.
    var cut = RenderBand();
    int cycles = cut.Instance.CompletedBandRefreshes;
    RadioNotActive();

    _clock.Advance(VisualizerPanel.BandIdleRefresh);
    WaitForRefreshes(cut, cycles + 1);

    cut.WaitForAssertion(() =>
    {
      IsActive(Tab(cut, "Spectrum")).Should().BeTrue();
      Tab(cut, "Radio").HasAttribute("disabled").Should().BeTrue();
      cut.FindAll(".band-strip").Should().BeEmpty();
    });
    PreferenceWrites().Should().BeEmpty();
  }

  [Fact]
  public async Task SourceChanged_ApiUnreachable_KeepsBand()
  {
    // A failed read says nothing about the source; BAND stays rather than flickering away on a blip.
    var cut = RenderBand();
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.InternalServerError, null);

    await RaiseSourceChangedAsync(cut);

    IsActive(Tab(cut, "Radio")).Should().BeTrue();
    cut.FindAll(".band-strip").Should().HaveCount(1);
  }

  [Theory]
  [InlineData("Band", true, "Band")]
  [InlineData("Band", false, "Spectrum")]
  [InlineData("Waveform", false, "Waveform")]
  [InlineData("PhaseScope", false, "PhaseScope")]
  [InlineData("Circular", true, "Circular")]
  [InlineData("Spectrum", false, "Spectrum")]
  public void DisplayModeFor_FallsBackOnlyForBandWithoutTheRadio(string preferred, bool radioActive, string shown)
  {
    var mode = VisualizerPanel.ParseSavedMode(preferred);
    VisualizerPanel.DisplayModeFor(mode, radioActive).ToString().Should().Be(shown);
  }

  // ── UI-30: BAND does not show the hub's disconnected state ───────────────

  [Fact]
  public void Band_HubDisconnected_ShowsNoDisconnectedState()
  {
    // BAND reads the REST API, not the visualization hub, so a hub drop does not stop it. The fixture's
    // hub is offline throughout.
    Services.GetRequiredService<AudioVisualizationHubService>().IsConnected.Should().BeFalse();
    var cut = RenderBand();

    cut.FindAll(".visualizer-disconnected").Should().BeEmpty();
    cut.FindAll(".band-empty").Should().HaveCount(1);
  }

  // ── UI-31: the strip under the map, Discover, and the RADIO tab ──────────

  private void BandList(params (string Type, string Range)[] bands) =>
    GetJson("/api/RadioBands", bands.Select(b => (object)new { type = b.Type, name = b.Type, range = b.Range }).ToArray());

  private static string StripText(IRenderedComponent<VisualizerPanel> cut) =>
    cut.Find(".band-strip").TextContent;

  [Fact]
  public void Strip_NamesTheBand_AndItsRangeAsTheBandPillDoes()
  {
    BandList(("FM", "87.5–108 MHz"), ("WB", "162.4–162.55 MHz"));
    GetJson("/api/radio/bandmap", WbMap());

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-strip-range").TextContent.Should().Be("162.4–162.55 MHz"));
    cut.Find(".band-strip-name").TextContent.Should().Be("WB");
    cut.FindAll(".band-strip-window").Should().BeEmpty("only VHF's map is a window");
  }

  [Fact]
  public void Strip_WithoutTheBandList_FallsBackToThePlottedRange()
  {
    // No /api/RadioBands route: the read fails, and the range is the map's own plotted range (WB's is
    // padded half a channel either side, so it differs from the pill's).
    GetJson("/api/radio/bandmap", WbMap());

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("WB"));
    cut.Find(".band-strip-range").TextContent.Should().Be("162.39–162.56 MHz");
  }

  private int BandListReads() => _api.Requests.Count(r => r.Method == HttpMethod.Get && r.Path == "/api/RadioBands");

  [Fact]
  public void Strip_TheBandList_IsReadOnce_ThenKept()
  {
    BandList(("FM", "87.5–108 MHz"));
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-range").TextContent.Should().Be("87.5–108 MHz"));
    int refreshes = cut.Instance.CompletedBandRefreshes;

    _clock.Advance(VisualizerPanel.BandIdleRefresh);
    WaitForRefreshes(cut, refreshes + 1);
    _clock.Advance(VisualizerPanel.BandIdleRefresh);
    WaitForRefreshes(cut, refreshes + 2);

    BandListReads().Should().Be(1, "the band list changes only with configuration");
  }

  [Fact]
  public void Strip_ABandListThatFailedAtFirst_IsRetried_AndThenReplacesTheFallback()
  {
    GetJson("/api/radio/bandmap", WbMap());
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-strip-range").TextContent.Should().Be("162.39–162.56 MHz"));
    int refreshes = cut.Instance.CompletedBandRefreshes;

    BandList(("WB", "162.4–162.55 MHz"));
    _clock.Advance(VisualizerPanel.BandIdleRefresh);
    WaitForRefreshes(cut, refreshes + 1);

    cut.WaitForAssertion(() => cut.Find(".band-strip-range").TextContent.Should().Be("162.4–162.55 MHz"));
    BandListReads().Should().Be(2);
  }

  [Fact]
  public void Strip_Vhf_ShowsItsWindow_NotTheBand()
  {
    BandList(("VHF", "30–300 MHz"));
    GetJson("/api/radio/bandmap", BandMap("VHF", 161_500_000, 163_500_000, 161_500_000, 163_500_000, 12_500));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("VHF"));
    cut.Find(".band-strip-window").TextContent.Should().Be("Window");
    cut.Find(".band-strip-range").TextContent.Should().Be("161.5–163.5 MHz");
  }

  /// <summary>
  /// The help lines as seen (visually-hidden words removed) and as read (aria-hidden glyphs removed), with
  /// whitespace collapsed.
  /// </summary>
  private static (string[] Seen, string[] Read) HelpLines(IRenderedComponent<VisualizerPanel> cut)
  {
    static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    static string Without(AngleSharp.Dom.IElement line, string selector)
    {
      string text = line.TextContent;
      foreach (var part in line.QuerySelectorAll(selector))
      {
        text = text.Replace(part.TextContent, " ");
      }

      return Collapse(text);
    }

    var lines = cut.FindAll(".band-strip-help > span").ToArray();
    return (lines.Select(l => Without(l, ".visually-hidden")).ToArray(), lines.Select(l => Without(l, "[aria-hidden=true]")).ToArray());
  }

  [Fact]
  public void Strip_ShowsTheHelpText_WhileNoMessageIsUp_AndScreenReadersGetWordsForTheGlyphs()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));

    var cut = RenderBand();

    var (seen, read) = HelpLines(cut);
    seen.Should().Equal("Touch a signal to snap to it. ▲ marks a preset.", "Fine-tune with the tuner's ‹ › buttons or the knob.");
    read.Should().Equal("Touch a signal to snap to it. A triangle marks a preset.", "Fine-tune with the tuner's step buttons or the knob.");
  }

  [Fact]
  public async Task Strip_ATuneMessage_ReplacesTheHelp_InThePersistentPoliteRegion()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();

    // The region is in the page before any message, so the message is announced when it lands.
    var region = cut.Find(".band-message-live");
    region.GetAttribute("role").Should().Be("status");
    region.GetAttribute("aria-live").Should().Be("polite");
    region.GetAttribute("aria-atomic").Should().Be("true");
    region.TextContent.Trim().Should().BeEmpty();

    await cut.InvokeAsync(() => cut.Instance.OnBandTap(FmBandMath.HzToFraction(99_200_000)));

    cut.WaitForAssertion(() =>
      cut.Find(".band-message-live .band-status-message").TextContent.Should().Be("Tuning 99.5 FM"));
    cut.FindAll(".band-strip-help").Should().BeEmpty("the message takes the help text's place");
    cut.FindAll("[role=alert]").Should().BeEmpty();

    _clock.Advance(VisualizerPanel.BandMessageLifetime);
    cut.WaitForAssertion(() => cut.FindAll(".band-strip-help").Should().HaveCount(1), TimeSpan.FromSeconds(5));
    cut.Find(".band-message-live").TextContent.Trim().Should().BeEmpty();
  }

  [Fact]
  public void Strip_AnError_IsAnAlert_OutsideThePoliteRegion()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.Conflict, "{}");
    var cut = RenderBand();

    cut.Find(".band-discover-btn").Click();

    cut.WaitForAssertion(() =>
    {
      var alert = cut.Find(".band-status-message.is-error");
      alert.GetAttribute("role").Should().Be("alert");
      alert.TextContent.Should().Be("Discover unavailable", "the API gave no reason");
    });
    cut.Find(".band-message-live").TextContent.Trim().Should().BeEmpty();
    cut.FindAll(".band-strip-help").Should().BeEmpty();
  }

  [Fact]
  public void Strip_OutOfRange_SaysSo_HidesTheHelp_AndKeepsDiscoverInPlaceDisabled()
  {
    BandList(("AM", "530–1710 kHz"));
    GetJson("/api/radio/bandmap", AmMap());

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-strip-name").TextContent.Should().Be("AM"));
    cut.Find(".band-strip-range").TextContent.Should().Be("530–1710 kHz");
    cut.Find(".band-status-unavailable").TextContent.Should().Be("Out of range");
    cut.FindAll(".band-strip-help").Should().BeEmpty("touching does nothing on a band the radio cannot receive");
    cut.FindAll(".band-axis-labels").Should().BeEmpty("there is no plot to read an axis against");
    cut.Find(".band-discover-btn").HasAttribute("disabled").Should().BeTrue();
    LastDrawModel().GetProperty("mappable").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public void FmMap_DrawModel_IsMappable_AndPresetsCarryNoLabel()
  {
    GetJson("/api/radio/presets", new object[] { new { id = "1", name = "KUER", band = "FM", frequency = 90_100_000.0 } });
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();
    cut.WaitForAssertion(() => LastDrawModel().GetProperty("presets").GetArrayLength().Should().Be(1));

    JsonElement model = LastDrawModel();
    model.GetProperty("mappable").GetBoolean().Should().BeTrue();
    JsonElement preset = model.GetProperty("presets")[0];
    preset.GetProperty("f").GetDouble().Should().BeApproximately(FmBandMath.HzToFraction(90_100_000), 1e-12);
    preset.TryGetProperty("label", out _).Should().BeFalse("presets are carets on the axis (UI-31)");
  }

  [Fact]
  public void Discover_IsTheOnlyButtonInTheStrip_AndNothingOverlaysThePlot()
  {
    var cut = RenderBand();

    cut.Find(".visualizer-canvas").GetAttribute("role").Should().Be("img", "an aria-label on a role-less canvas may be ignored");
    var button = cut.Find(".band-strip .band-discover-btn");
    button.TextContent.Should().Be("Discover");
    cut.FindAll(".band-strip button").Should().HaveCount(1);
    cut.FindAll(".visualizer-canvas-wrap button").Should().BeEmpty("nothing over the plot takes a tap from it");
  }

  [Theory]
  [InlineData("map")]
  [InlineData("empty")]
  [InlineData("sweeping")]
  [InlineData("otherBandSweeping")]
  [InlineData("am")]
  public void TheWordScan_AppearsNowhereInTheView(string state)
  {
    // The owner: "The 'SCAN' button is confusing since it overlaps meaning with the 'SCAN' buttons on
    // the central panel." Rendered text and accessible names both count.
    object sweepOther = new { isSweeping = true, band = "AIR", trigger = "request", path = "idle", progress = 0.4, estimatedSecondsRemaining = 12.0 };
    GetJson("/api/radio/bandmap", state switch
    {
      "map" => MapWithStation(ageSeconds: 60),
      "sweeping" => MapWithStation(ageSeconds: 60, isSweeping: true, remaining: 12),
      "otherBandSweeping" => BandMap("WB", 162_387_500, 162_562_500, 162_400_000, 162_550_000, 25_000, sweep: sweepOther),
      "am" => AmMap(),
      _ => EmptyMap(),
    });

    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.FindAll(".band-strip-name").Should().HaveCount(1));

    // The API's own sentences are shown verbatim and are out of this row's scope (the API is unchanged):
    // AM's reason ends "... and cannot be scanned." They are taken out before the check.
    var panel = cut.Find(".viz-panel");
    string text = panel.TextContent;
    foreach (var apiText in cut.FindAll(".band-empty-sub"))
    {
      text = text.Replace(apiText.TextContent, string.Empty);
    }

    text.Should().NotContainEquivalentOf("scan");
    panel.QuerySelectorAll("[aria-label]").Select(e => e.GetAttribute("aria-label"))
      .Should().NotContain(label => label!.Contains("scan", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void SavedBandPreference_OpensTheRadioTab_AndPickingItSavesBandUnchanged()
  {
    // UI-31 renamed the tab, not the stored value. The owner's box holds ui.visualizer/defaultMode=Band;
    // renaming the value would orphan it (the AUD-1 lesson about renamed config keys).
    var cut = RenderBand();
    IsActive(Tab(cut, "Radio")).Should().BeTrue("defaultMode=Band is the saved preference");

    Tab(cut, "Spectrum").Click();
    Tab(cut, "Radio").Click();

    cut.WaitForAssertion(() => PreferenceWrites().Should().HaveCount(2));
    JsonDocument.Parse(PreferenceWrites()[^1]).RootElement.GetProperty("defaultMode").GetString()
      .Should().Be("Band");
    VisualizerPanel.ParseSavedMode("Band").Should().Be(VisualizerPanel.VisualizationMode.Band);
  }
}
