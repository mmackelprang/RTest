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
/// The visualizer's BAND view (AUD-76 PR 2): the empty, aged and sweeping states, the Scan button,
/// the refresh cadence and tap-to-tune.
///
/// <para>
/// Every API call goes to a <see cref="RoutedApiHandler"/>, so the "server" is a table the test
/// controls. The saved preference is BAND, so the panel opens on it. The refresh timer is armed
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

    // Defaults: BAND is the saved mode, the radio is not the active source, no presets, no map.
    GetJson("/api/configuration/ui.visualizer", new Dictionary<string, object> { ["defaultMode"] = "Band" });
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.BadRequest, "{\"error\":\"Radio is not the active source\"}");
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
      cut.FindAll(".band-overlay").Should().HaveCount(1, "the canvas initialised and BAND is the saved mode");
      cut.Instance.CompletedBandRefreshes.Should().BeGreaterThanOrEqualTo(1);
    }, TimeSpan.FromSeconds(5));
    return cut;
  }

  private void WaitForRefreshes(IRenderedComponent<VisualizerPanel> cut, int atLeast) =>
    cut.WaitForAssertion(() => cut.Instance.CompletedBandRefreshes.Should().BeGreaterThanOrEqualTo(atLeast),
      TimeSpan.FromSeconds(5));

  private int IndexOf(HttpMethod method, string path) =>
    _api.Requests.ToList().FindIndex(r => r.Method == method && r.Path == path);

  // ── states ───────────────────────────────────────────────────────────────

  [Fact]
  public void EmptyMap_ShowsNoScanYet_AndNotTheAudioWaitingOverlay()
  {
    var cut = RenderBand();

    cut.Find(".band-empty-title").TextContent.Should().Be("No scan yet");
    cut.FindAll(".visualizer-waiting").Should().BeEmpty("BAND has no audio stream to wait for");
    cut.FindAll(".band-status-age").Should().BeEmpty();
  }

  [Fact]
  public void StoredMap_ShowsItsAge()
  {
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 3 * 3600 + 120));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-age").TextContent.Should().Be("scanned 3 h ago"));
    cut.FindAll(".band-empty").Should().BeEmpty();
    cut.FindAll(".visualizer-waiting").Should().BeEmpty();
  }

  [Fact]
  public void Sweeping_ShowsTheCountdown_AndDisablesScan()
  {
    GetJson("/api/radio/bandmap", EmptyMap(isSweeping: true, remaining: 12));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-scanning").TextContent.Should().Be("Scanning… 12 s"));
    cut.Find(".band-scan-btn").HasAttribute("disabled").Should().BeTrue();
  }

  [Fact]
  public void BandAxis_ShowsMhz()
  {
    var cut = RenderBand();

    cut.FindAll(".visualizer-axis.is-band span").Select(s => s.TextContent)
      .Should().Equal("88", "92", "96", "100", "104", "108");
  }

  // ── Scan button ──────────────────────────────────────────────────────────

  [Fact]
  public void ScanButton_PostsTheScan()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.Accepted, "{}");
    var cut = RenderBand();

    cut.Find(".band-scan-btn").Click();

    cut.WaitForAssertion(() => IndexOf(HttpMethod.Post, "/api/radio/bandmap/scan").Should().BeGreaterThanOrEqualTo(0));
  }

  [Fact]
  public void ScanButton_Unavailable_ShowsTheApiReason()
  {
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.ServiceUnavailable,
      "{\"error\":\"No SDR device is available\"}");
    var cut = RenderBand();

    cut.Find(".band-scan-btn").Click();

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
    cut.WaitForAssertion(() => cut.FindAll(".band-overlay").Should().BeEmpty());
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
    GetJson("/api/radio/bandmap", MapWithStation(ageSeconds: 60));
    var cut = RenderBand();

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
    _api.Route(HttpMethod.Get, "/api/radio/state", HttpStatusCode.InternalServerError, null);
    var cut = RenderBand();

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
    cut.FindAll(".visualizer-axis.is-band span").Select(s => s.TextContent).ToArray();

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
    cut.Find(".band-status-band").TextContent.Should().Be("FM");
    cut.Find(".band-scan-btn").GetAttribute("aria-label").Should().Be("Scan the FM band");
  }

  [Fact]
  public void WbMap_DrawsWbTicks_AndNamesTheBand()
  {
    GetJson("/api/radio/bandmap", WbMap());

    var cut = RenderBand();

    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("162.40", "162.45", "162.50", "162.55 MHz"));
    cut.Find(".band-scan-btn").GetAttribute("aria-label").Should().Be("Scan the WB band");
    cut.Find(".visualizer-canvas").GetAttribute("aria-label").Should().Be("WB band map. Tap a station to tune.");
    cut.Find(".band-status-band").TextContent.Should().Be("WB");
    cut.Find(".band-status-age").TextContent.Should().Be("scanned 3 min ago");
    LastDrawModel().GetProperty("grid").GetArrayLength().Should().Be(4);
  }

  [Fact]
  public async Task AmMap_DisablesScan_ShowsTheReason_AndATapTunesNothing()
  {
    GetJson("/api/radio/bandmap", AmMap());
    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-empty-title").TextContent.Should().Be("AM is out of this radio's range"));
    cut.Find(".band-empty-sub").TextContent.Should().Be(AmReason);
    cut.Markup.Should().NotContain("No scan yet");
    cut.Find(".band-scan-btn").HasAttribute("disabled").Should().BeTrue();
    AxisLabels(cut).Should().Equal("600", "800", "1000", "1200", "1400", "1600 kHz");

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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("AIR"));

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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("AIR"));

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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("WB"));

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
  public void Scan_PostsTheShownBand()
  {
    GetJson("/api/radio/bandmap", WbMap());
    _api.Route(HttpMethod.Post, "/api/radio/bandmap/scan", HttpStatusCode.Accepted, "{}");
    var cut = RenderBand();
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("WB"));

    cut.Find(".band-scan-btn").Click();

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

    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("110", "115", "120", "125", "130", "135 MHz"));
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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("AIR"));
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

    cut.WaitForAssertion(() => cut.Find(".band-status-scanning").TextContent.Should().Be("Scanning AIR… 12 s"));
    cut.Find(".band-empty-title").TextContent.Should().Be("No scan yet", "the sweep running is not of the band shown");
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
    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("110", "115", "120", "125", "130", "135 MHz"),
      TimeSpan.FromSeconds(5));
  }

  private string[] AxisLabelClasses(IRenderedComponent<VisualizerPanel> cut) =>
    cut.FindAll(".visualizer-axis.is-band span").Select(s => s.GetAttribute("class") ?? "").ToArray();

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

    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("161.5", "162.0", "162.5", "163.0", "163.5 MHz"));
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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("AIR"));
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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("VHF"));
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
    cut.WaitForAssertion(() => AxisLabels(cut).Should().Equal("169.0", "169.5", "170.0", "170.5", "171.0 MHz"),
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
      cut.FindAll(".band-overlay").Should().HaveCount(1);
      MapReads().Should().BeGreaterThanOrEqualTo(1, "the first map read is in flight");
    }, TimeSpan.FromSeconds(5));

    cut.FindAll(".band-status-band").Should().BeEmpty("the band is not known until a map has been read");
    cut.Find(".band-scan-btn").GetAttribute("aria-label").Should().Be("Scan the band");

    release.SetResult();
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("FM"), TimeSpan.FromSeconds(5));
  }

  [Fact]
  public void MapWithoutAUsableAxis_IsStillNamedForItsBand()
  {
    // Drawn on FM's axis for want of its own, but it is WB's map.
    GetJson("/api/radio/bandmap", BandMap("WB", 0, 0, 0, 0, 0));

    var cut = RenderBand();

    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("WB"));
    cut.Find(".band-scan-btn").GetAttribute("aria-label").Should().Be("Scan the WB band");
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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("WB"));

    JsonElement model = LastDrawModel();
    model.GetProperty("station").ValueKind.Should().Be(JsonValueKind.Null, "the radio is on FM, not WB");
    model.GetProperty("presets").EnumerateArray().Select(p => p.GetProperty("label").GetString())
      .Should().Equal(new[] { "NOAA" }, "only the shown band's presets are drawn");
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
    cut.Find(".band-status-line").TextContent.Should().MatchRegex(@"scanned 1 min ago\s*·\s*weak\s*fair\s*strong");
  }

  [Fact]
  public void EmptyMap_HasNoLegend()
  {
    var cut = RenderBand();

    cut.Find(".band-empty-title").TextContent.Should().Be("No scan yet");
    cut.FindAll(".band-legend").Should().BeEmpty();
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

    cut.WaitForAssertion(() => cut.Find(".band-status-scanning").TextContent.Should().Be("Scanning… 12 s"));
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
    cut.WaitForAssertion(() => cut.Find(".band-status-band").TextContent.Should().Be("WB"));

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
}
